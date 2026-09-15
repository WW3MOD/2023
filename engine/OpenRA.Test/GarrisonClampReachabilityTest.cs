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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// An indestructible garrison must be REDUCIBLE to its last hit point, not merely unkillable.
	///
	/// <para>THE DEFECT, measured. `Indestructible` used to be an IDamageModifier returning
	/// `maxAllowedDamage * 100 / damage.Value` — an integer percentage — which truncates to 0 once
	/// (HP-1)*100 is below the incoming damage. A shipped 75000 HP church under ~14000-damage tank
	/// rounds walked 75000 -> 61000 -> 47000 -> 33000 -> 19000 -> 5000 -> 100 and then STOPPED,
	/// permanently, because every further hit was multiplied by 0%. Its 1 HP rubble state was
	/// unreachable by any weapon over about 100 damage, which made every RubbleProtection value in the
	/// mod dead tuning. Found by autotest run 260915_184945 (a church stuck at 2/20 HP with its whole
	/// garrison dead) and reproduced here on the shipped numbers.</para>
	///
	/// <para>No integer percentage fixes it, which is why the mechanism moved rather than the formula:
	/// at 140 HP against 14000 damage the only representable outcomes are 0% (nothing lands, stall)
	/// and 1% (140 lands, which kills a building that must not die). The clamp is now a FLOOR applied
	/// where HP is assigned (IDamageFloor, Health.ApplyDamageToHp) and the arithmetic below is the real
	/// one — this fixture calls the engine's own function, not a copy of it.</para>
	/// </summary>
	[TestFixture]
	public class GarrisonClampReachabilityTest
	{
		const int ChurchHp = 75000;      // V01, civilian.yaml
		const int TankRound = 14000;     // ^TankRound Warhead@Target 20000, after falloff/armour
		const int Floor = 1;             // GarrisonManager.Indestructible

		/// <summary>The formula this replaced, reproduced ONLY so the defect has a regression test.
		/// Nothing in the engine calls this any more; if it ever comes back, the second test fails.</summary>
		static int HpUnderTheOldPercentageClamp(int hp, int damage)
		{
			if (hp <= 1)
				return hp;

			if (damage >= hp)
			{
				var maxAllowedDamage = hp - 1;
				if (maxAllowedDamage <= 0)
					return hp;

				var modifier = maxAllowedDamage * 100 / damage;
				return hp - (damage * modifier / 100);
			}

			return hp - damage;
		}

		[Test]
		public void AChurchUnderTankFireReachesOneHitPoint()
		{
			var hp = ChurchHp;
			var trace = new System.Collections.Generic.List<int> { hp };

			for (var shot = 0; shot < 200 && hp > Floor; shot++)
			{
				var next = Health.ApplyDamageToHp(hp, TankRound, Floor, ChurchHp);
				Assert.That(next, Is.LessThan(hp),
					$"the church stopped taking damage at {hp}/{ChurchHp} HP after {shot} shots of {TankRound}. " +
					$"That is the stall this fixture exists for — trace: {string.Join(" -> ", trace)}");

				hp = next;
				trace.Add(hp);
			}

			Assert.That(hp, Is.EqualTo(Floor),
				$"the church settled at {hp} HP rather than the {Floor} HP floor — trace: {string.Join(" -> ", trace)}");
		}

		[Test]
		public void TheOldPercentageClampStalledAtAHundredAndNeverReachedIt()
		{
			var hp = ChurchHp;
			for (var shot = 0; shot < 200; shot++)
			{
				var next = HpUnderTheOldPercentageClamp(hp, TankRound);
				if (next == hp)
					break;

				hp = next;
			}

			// The exact number matters less than that it is not 1, but pinning it makes the
			// regression unmistakable if anyone reintroduces a percentage.
			Assert.That(hp, Is.EqualTo(100),
				$"the replaced formula settled at {hp} rather than the measured 100 — if this moved, " +
				"re-derive the stall before trusting the numbers quoted in IDamageFloor.");

			Assert.That(hp, Is.Not.EqualTo(Floor),
				"the replaced formula reached the floor after all, which would mean this whole fixture " +
				"is testing the wrong defect.");
		}

		[TestCase(1)]
		[TestCase(37)]
		[TestCase(100)]
		[TestCase(101)]
		[TestCase(3000)]
		[TestCase(14000)]
		[TestCase(200000)]
		public void EveryDamageSizeTerminatesExactlyOnTheFloor(int damage)
		{
			// The old rule's failure was size-dependent — it worked for small hits and stalled for
			// large ones — so a single damage value could have passed while the mod's real weapons
			// could not reduce a garrison at all.
			var hp = ChurchHp;
			for (var shot = 0; shot < 100000 && hp > Floor; shot++)
				hp = Health.ApplyDamageToHp(hp, damage, Floor, ChurchHp);

			Assert.That(hp, Is.EqualTo(Floor),
				$"a stream of {damage}-damage hits left the church at {hp} rather than the floor.");
		}

		[Test]
		public void AZeroFloorIsStillTheOrdinaryRuleAndStillKills()
		{
			// Every actor in the mod but the garrisonable buildings has no IDamageFloor at all, so it
			// resolves to 0 here. If that stopped meaning "damage can kill it", the floor would have
			// made the entire game indestructible.
			Assert.That(Health.ApplyDamageToHp(200, 14000, 0, 200), Is.EqualTo(0));
			Assert.That(Health.ApplyDamageToHp(200, 199, 0, 200), Is.EqualTo(1));
			Assert.That(Health.ApplyDamageToHp(200, -50, 0, 200), Is.EqualTo(200), "a heal must still cap at MaxHP");
		}
	}
}
