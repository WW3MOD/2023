#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Damage forwarded to a shelter occupant must never turn back DOWN as the building is reduced.
	///
	/// <para>THE DEFECT THIS PINS. GarrisonManager.Indestructible clamps a garrison at 1 HP by
	/// returning a damage modifier of 0 for every hit once it is there; Health applies modifiers and
	/// only then raises INotifyDamage, so GarrisonProtection.Damaged saw a damage of ZERO and returned
	/// without forwarding anything. A rubbled garrison was therefore IMMUNE — safer than the same
	/// garrison one hit point earlier, and safer than an intact one — while the panel went on
	/// reporting RubbleProtection as though 70% were getting through. The building also cannot be
	/// destroyed, so the state is terminal: men in the rubble could not be killed by any weapon.</para>
	///
	/// <para>The ruling (manager, 2026-09-15) is the project's gradient rule applied to this curve:
	/// price the bad state, do not force the player out of it. Pass-through at the clamp is the
	/// curve's MAXIMUM rather than zero, and nobody is ejected.</para>
	///
	/// <para>REACHING the floor at all is a separate question and a separate fixture:
	/// GarrisonClampReachabilityTest. It has to be, because the first fix to this curve left the floor
	/// unreachable — see IDamageFloor.</para>
	/// </summary>
	[TestFixture]
	public class GarrisonRubbleProtectionTest
	{
		// The C# defaults (GarrisonProtectionInfo), which any actor that declares the trait without
		// overriding a field inherits. Carried explicitly so the fixture covers the unauthored case.
		const int DefaultBase = 80;
		const int DefaultCritical = 30;
		const int DefaultRubble = 30;

		struct Curve
		{
			public string Where;
			public int Base;
			public int Critical;
			public int Rubble;
			public int MinPassThrough;
		}

		static int Field(MiniYaml node, string key, int fallback)
		{
			var raw = ModRulesYaml.Child(node, key);
			return raw == null ? fallback : int.Parse(raw);
		}

		/// <summary>Every GarrisonProtection block authored in the mod, plus the bare C# defaults.</summary>
		static List<Curve> AuthoredCurves()
		{
			var curves = new List<Curve>
			{
				new Curve
				{
					Where = "GarrisonProtectionInfo defaults",
					Base = DefaultBase,
					Critical = DefaultCritical,
					Rubble = DefaultRubble,
					MinPassThrough = 5
				}
			};

			foreach (var (file, node) in ModRulesYaml.AllRuleNodes())
			{
				var gp = ModRulesYaml.ChildNode(node.Value, "GarrisonProtection");
				if (gp == null)
					continue;

				curves.Add(new Curve
				{
					Where = $"{file}: {node.Key}",
					Base = Field(gp, "BaseProtection", DefaultBase),
					Critical = Field(gp, "CriticalProtection", DefaultCritical),
					Rubble = Field(gp, "RubbleProtection", DefaultRubble),
					MinPassThrough = Field(gp, "MinPassThrough", 5)
				});
			}

			Assert.That(curves.Count, Is.GreaterThan(1),
				"no GarrisonProtection block was found in the mod — this fixture is scanning nothing, not passing.");

			return curves;
		}

		[Test]
		public void ProtectionNeverRisesAsTheBuildingIsReduced()
		{
			// Several scales because the curve is a function of hp/maxHp and the truncation to int can
			// only bite at the resolutions it is actually used at. 25000 and 75000 are real civilian
			// house pools; 15000 and 22500 are GTWR and PBOX.
			var pools = new[] { 15000, 22500, 25000, 60000, 75000, 120000 };

			foreach (var c in AuthoredCurves())
			{
				foreach (var maxHp in pools)
				{
					var previous = GarrisonProtection.ProtectionAt(maxHp, maxHp, c.Base, c.Critical, c.Rubble);
					for (var hp = maxHp; hp >= 1; hp -= System.Math.Max(1, maxHp / 500))
					{
						var here = GarrisonProtection.ProtectionAt(hp, maxHp, c.Base, c.Critical, c.Rubble);
						Assert.That(here, Is.LessThanOrEqualTo(previous),
							$"{c.Where}: protection ROSE from {previous} to {here} on the way down to {hp}/{maxHp}. " +
							"A garrison must never get safer as its building is reduced. If this fired at hp=1 the " +
							$"cause is RubbleProtection ({c.Rubble}) being above CriticalProtection ({c.Critical}), " +
							"which the curve approaches just above the clamp.");
						previous = here;
					}

					// The clamp itself, which the loop above may step over.
					Assert.That(GarrisonProtection.ProtectionAt(1, maxHp, c.Base, c.Critical, c.Rubble),
						Is.LessThanOrEqualTo(GarrisonProtection.ProtectionAt(2, maxHp, c.Base, c.Critical, c.Rubble)),
						$"{c.Where}: the building gets SAFER at the rubble clamp than it was one hit point above it.");
				}
			}
		}

		[Test]
		public void PassThroughIsMaximalAtTheClampRatherThanZero()
		{
			const int MaxHp = 75000;
			const int Hit = 15000;

			foreach (var c in AuthoredCurves())
			{
				var atClamp = GarrisonProtection.PassThroughFor(
					Hit, GarrisonProtection.ProtectionAt(1, MaxHp, c.Base, c.Critical, c.Rubble), c.MinPassThrough);

				Assert.That(atClamp, Is.GreaterThan(0),
					$"{c.Where}: a hit of {Hit} forwards NOTHING to the shelter at the rubble clamp. That is the " +
					"defect this fixture exists for — the state is terminal because the building cannot be " +
					"destroyed, so zero here means the occupants can never be killed by anything.");

				for (var hp = MaxHp; hp > 1; hp -= MaxHp / 200)
				{
					var above = GarrisonProtection.PassThroughFor(
						Hit, GarrisonProtection.ProtectionAt(hp, MaxHp, c.Base, c.Critical, c.Rubble), c.MinPassThrough);

					Assert.That(atClamp, Is.GreaterThanOrEqualTo(above),
						$"{c.Where}: the clamp forwards {atClamp} but {hp}/{MaxHp} forwards {above} — the rubble " +
						"is not the most exposed point on the curve.");
				}
			}
		}

		[Test]
		public void TheMinimumPassThroughIsAFloorOnTheHitAndNotOnTheCurve()
		{
			// A glancing hit on an intact building still forwards nothing — MinPassThrough is unchanged
			// by any of this, and a fix that quietly removed it would make garrisons far deadlier than
			// the ruling asked for.
			Assert.That(GarrisonProtection.PassThroughFor(100, 95, 15), Is.EqualTo(0));
			Assert.That(GarrisonProtection.PassThroughFor(1000, 95, 15), Is.EqualTo(50));
		}
	}
}
