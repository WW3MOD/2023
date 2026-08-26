#region Copyright & License Information
/*
 * WW3MOD ballistic direct-hit damage — pins the three independent reasons a large `Damage:` on a
 * TargetDamage warhead used to arrive as almost nothing, or as HEALING, against an armoured target.
 *
 * The user report was "I hit a tank directly with an Iskander and it didn't get destroyed". Three
 * separate defaults conspired, and each one is pinned here:
 *
 *   (1) PENETRATION. DamageWarhead.Penetration defaults to 1 and IskanderExplosion/HIMARSExplosion
 *       set no value, so ApplyPenetration returned damage*1/thickness. Every other anti-armour
 *       weapon in the mod (TankRound 800, ArtilleryRound 1000, Hellfire 800, Ataka 900, RPG 500)
 *       sets it explicitly on its Warhead@Target, which is what makes the omission a bug and not a
 *       balance choice.
 *
 *   (2) IMPACT POSITION. WarheadArgs.ImpactPosition is only ever assigned by a *projectile*
 *       (Bullet/Missile/GravityBomb/LaserZap/Railgun/AreaBeam/InstantHit). These two weapons are
 *       delivered by Explodes on the missile actor -- BallisticMissileFly queues self.Kill(self)
 *       and Explodes calls WeaponInfo.Impact(Target.FromPos(...), firedBy), which built WarheadArgs
 *       without it. ImpactPosition therefore stayed WPos.Zero, the MAP ORIGIN, and
 *       TargetDamageWarhead scales damage by closestActiveShape.CenterProximityPercent(victim,
 *       args.ImpactPosition). Measured from the map corner that percentage is large and NEGATIVE,
 *       and Health.InflictDamage does HP = (HP - damage).Clamp(0, MaxHP) -- so the direct-hit
 *       warhead healed its victim to full.
 *
 *   (3) DAMAGE AT MAX RANGE. RangeDamageFactor divides by the warhead's own weapon Range, which is
 *       zero for a weapon that is never fired from an armament. Any value other than 100 therefore
 *       produced a non-finite percentage rather than a range falloff. HIMARSExplosion carried 80.
 *
 * Pure arithmetic plus a YAML read; no World, no Actor, no game run.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.GameRules;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.HitShapes;
using OpenRA.Mods.Common.Warheads;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class BallisticPenetrationTest
	{
		// The thickest armour in the mod. MSLO (missile silo) is Concrete 2000 and carries no
		// Distribution, so no direction discount ever reduces it. A warhead that is meant to kill
		// "any target in one shot" has to out-penetrate this number, not the 280 of a T-90.
		const int ThickestArmour = 2000;

		// T-90: Armor.Thickness 280, Health.HP 24000 (vehicles-russia.yaml).
		const int T90Thickness = 280;
		const int T90Hp = 24000;

		// Infantry and other actors that set no Armor.Thickness at all default to 0.
		const int UnarmouredThickness = 0;

		// --- (1) penetration ------------------------------------------------------------------

		[Test]
		public void UnsetPenetrationGutsDamageAgainstArmour()
		{
			// Penetration defaults to 1. This is the arithmetic that made 54000 disappear.
			var delivered = DamageWarhead.ApplyPenetration(54000, 1, T90Thickness);

			Assert.That(delivered, Is.EqualTo(192),
				"the default Penetration of 1 should divide 54000 down to 192 against 280mm armour");
			Assert.That(delivered, Is.LessThan(T90Hp / 100),
				"192 is under 1% of a T-90's 24000 HP -- this is the reported bug");
		}

		[Test]
		public void PenetratingWarheadDeliversFullDamage()
		{
			Assert.That(DamageWarhead.ApplyPenetration(54000, 2500, T90Thickness), Is.EqualTo(54000));
			Assert.That(DamageWarhead.ApplyPenetration(54000, 2500, ThickestArmour), Is.EqualTo(54000));
		}

		[Test]
		public void PenetrationEqualToThicknessStillPenetrates()
		{
			// The predicate is `penetration >= thickness`, so exact equality must NOT divide.
			Assert.That(DamageWarhead.ApplyPenetration(1000, 280, 280), Is.EqualTo(1000));
			Assert.That(DamageWarhead.ApplyPenetration(1000, 279, 280), Is.EqualTo(996));
		}

		[Test]
		public void UnarmouredVictimsAreNeverDivided()
		{
			// Thickness 0 is the default, so an unset-Penetration warhead is perfectly fine against
			// infantry. That is why the defect only ever showed up on vehicles, and why the great
			// majority of the mod's Penetration-less SpreadDamage warheads are not bugs.
			Assert.That(DamageWarhead.ApplyPenetration(54000, 1, UnarmouredThickness), Is.EqualTo(54000));
		}

		// --- (2) impact position --------------------------------------------------------------

		[Test]
		public void ImmediateImpactArgsCarryTheImpactPosition()
		{
			// The Explodes path. Before the fix ImpactPosition was left at WPos.Zero here, which is
			// what fed the map origin into the proximity scaling below.
			var impact = new WPos(20992, 20992, 0);
			var args = WeaponInfo.ImmediateImpactArgs(null, Target.FromPos(impact), null);

			Assert.That(args.ImpactPosition, Is.EqualTo(impact),
				"a projectile-less impact must record where it actually landed, not WPos.Zero");
		}

		[Test]
		public void ProximityMeasuredFromMapOriginInvertsTheDamage()
		{
			// T-90 hitshape: TopLeft -400,-950  BottomRight 400,950.
			var shape = new RectangleShape(new int2(-400, -950), new int2(400, 950));
			shape.Initialize();

			var victim = new WPos(20992, 20992, 0);

			// A true direct hit on the centre is full damage.
			var direct = shape.CenterProximityPercent(victim, victim, WRot.None);
			Assert.That(direct, Is.EqualTo(100));

			// The same call with the unset ImpactPosition the Explodes path used to supply. The
			// tank sits ~20.5 cells along each axis, so the origin is ~29687 units away against a
			// half-diagonal of 1030 -- proximity goes far below zero.
			var fromOrigin = shape.CenterProximityPercent(WPos.Zero, victim, WRot.None);
			Assert.That(fromOrigin, Is.EqualTo(-2782),
				"measuring proximity from the map origin yields a large negative percentage");

			// Negative percentage * positive damage = negative damage, and Health.InflictDamage
			// does HP = (HP - damage).Clamp(0, MaxHP). The direct-hit warhead healed the tank to full.
			Assert.That(Util.ApplyPercentageModifiers(54000, new[] { fromOrigin }), Is.EqualTo(-1502280),
				"a negative proximity modifier turns the warhead into a repair beam");
		}

		[Test]
		public void ProximityIsFlooredSoAnAdmittedNearMissCannotHeal()
		{
			// Spread admits victims by DISTANCE FROM THE HITSHAPE EDGE, but the proximity percentage
			// normalises against the victim's CENTRE-to-corner distance. For a long, thin hull the
			// two disagree: a hit half a cell past the T-90's long edge is 1462 from its centre
			// against a half-diagonal of 1030, so the raw percentage is negative. Without the floor,
			// widening Spread would have traded the old direct-hit bug for a near-miss repair beam.
			var shape = new RectangleShape(new int2(-400, -950), new int2(400, 950));
			shape.Initialize();

			var victim = new WPos(20992, 20992, 0);
			var nearMiss = victim + new WVec(0, 950 + 512, 0);

			Assert.That(shape.DistanceFromEdge(nearMiss, victim, WRot.None).Length, Is.EqualTo(512),
				"precondition: this point is exactly Spread away from the hull edge, so it IS admitted");

			var raw = shape.CenterProximityPercent(nearMiss, victim, WRot.None);
			Assert.That(raw, Is.LessThan(0), "precondition: the raw percentage is negative here");

			Assert.That(TargetDamageWarhead.ProximityDamagePercent(raw), Is.Zero,
				"TargetDamageWarhead floors the proximity, so an admitted near miss does nothing " +
				"rather than repairing its target");

			// A real hit is untouched by the floor.
			Assert.That(TargetDamageWarhead.ProximityDamagePercent(100), Is.EqualTo(100));
			Assert.That(TargetDamageWarhead.ProximityDamagePercent(50), Is.EqualTo(50));
		}

		// --- (3) damage at max range ----------------------------------------------------------

		[Test]
		public void RangeFalloffIsIdentityWhenDamageAtMaxRangeIs100()
		{
			Assert.That(DamageWarhead.RangeDamageFactor(0, 50 * 1024, 100), Is.EqualTo(100));
			Assert.That(DamageWarhead.RangeDamageFactor(16 * 1024, 50 * 1024, 100), Is.EqualTo(100));
			Assert.That(DamageWarhead.RangeDamageFactor(30 * 1024, 50 * 1024, 100), Is.EqualTo(100));
			Assert.That(DamageWarhead.RangeDamageFactor(50 * 1024, 50 * 1024, 100), Is.EqualTo(100));
		}

		[Test]
		public void RangeFalloffIsNotAUsablePercentageWhenTheWeaponHasNoRange()
		{
			// An Explodes payload has no armament and therefore Range: 0. TargetDamageWarhead only
			// consults this when DamageAtMaxRange != 100, so 100 is the only safe value on such a
			// weapon -- anything else divides by zero.
			// (float)0/0 is NaN, so the whole expression is NaN and the cast to int is not a
			// percentage at all. The exact value of (int)NaN is platform-defined, so pin only the
			// thing that matters: it is not the 100 that would leave damage untouched.
			var factor = DamageWarhead.RangeDamageFactor(0, 0, 80);
			Assert.That(factor, Is.Not.EqualTo(100),
				$"zero Range cannot produce a meaningful falloff percentage (got {factor})");
			Assert.That(DamageWarhead.RangeDamageFactor(16 * 1024, 0, 80), Is.Not.EqualTo(100));
		}

		// --- YAML pins ------------------------------------------------------------------------

		static string FindModRules()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules", "weapons");
				if (Directory.Exists(candidate))
					return candidate;

				dir = dir.Parent;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod/rules/weapons");
		}

		/// <summary>Flat read of `Warhead@Target:` fields for one top-level weapon block.</summary>
		static Dictionary<string, string> ReadTargetWarhead(string weapon)
		{
			var path = Path.Combine(FindModRules(), "weapons-explosions.yaml");
			var fields = new Dictionary<string, string>();
			var inWeapon = false;
			var inWarhead = false;

			foreach (var raw in File.ReadLines(path))
			{
				var line = raw.Split('#')[0].TrimEnd();
				if (line.Trim().Length == 0)
					continue;

				var indent = line.TakeWhile(c => c == '\t').Count();
				var body = line.Trim();

				if (indent == 0)
				{
					if (inWeapon)
						break;

					inWeapon = body == weapon + ":";
					inWarhead = false;
				}
				else if (indent == 1 && inWeapon)
					inWarhead = body.StartsWith("Warhead@Target:", StringComparison.Ordinal);
				else if (indent >= 2 && inWarhead && body.Contains(':'))
				{
					var parts = body.Split(new[] { ':' }, 2);
					fields[parts[0].Trim()] = parts[1].Trim();
				}
			}

			Assert.That(inWeapon || fields.Count > 0, Is.True, $"{weapon} not found in weapons-explosions.yaml");
			return fields;
		}

		[TestCase("IskanderExplosion", 54000)]
		[TestCase("HIMARSExplosion", 36000)]
		public void DirectHitWarheadOutPenetratesTheThickestArmourInTheGame(string weapon, int expectedDamage)
		{
			var fields = ReadTargetWarhead(weapon);

			Assert.That(fields.ContainsKey("Damage"), Is.True);
			Assert.That(int.Parse(fields["Damage"]), Is.EqualTo(expectedDamage),
				$"{weapon} damage changed -- re-derive the penetration floor if this was deliberate");

			Assert.That(fields.ContainsKey("Penetration"), Is.True,
				$"{weapon} Warhead@Target sets no Penetration, so it inherits the default of 1 and " +
				$"delivers {expectedDamage}/thickness against anything armoured");

			var penetration = int.Parse(fields["Penetration"]);
			Assert.That(penetration, Is.GreaterThanOrEqualTo(ThickestArmour),
				$"{weapon} must out-penetrate the thickest armour in the mod ({ThickestArmour}mm, MSLO) " +
				"for a direct hit to be lethal to any target");

			// The whole point: full damage arrives on the heaviest and the lightest target alike.
			Assert.That(DamageWarhead.ApplyPenetration(expectedDamage, penetration, ThickestArmour),
				Is.EqualTo(expectedDamage));
			Assert.That(DamageWarhead.ApplyPenetration(expectedDamage, penetration, T90Thickness),
				Is.EqualTo(expectedDamage));
			Assert.That(DamageWarhead.ApplyPenetration(expectedDamage, penetration, UnarmouredThickness),
				Is.EqualTo(expectedDamage));
		}

		[TestCase("IskanderExplosion")]
		[TestCase("HIMARSExplosion")]
		public void DirectHitWarheadKeepsRangeFalloffDisabled(string weapon)
		{
			var fields = ReadTargetWarhead(weapon);

			// Delivered by Explodes, so the weapon has no Range and any value but 100 divides by zero.
			Assert.That(fields.TryGetValue("DamageAtMaxRange", out var value) ? int.Parse(value) : 100,
				Is.EqualTo(100),
				$"{weapon} is an Explodes payload with no Range: DamageAtMaxRange must stay 100");
		}

		[TestCase("IskanderExplosion")]
		[TestCase("HIMARSExplosion")]
		public void DirectHitWarheadSetsAnExplicitSpread(string weapon)
		{
			var fields = ReadTargetWarhead(weapon);

			Assert.That(fields.ContainsKey("Spread"), Is.True,
				$"{weapon} Warhead@Target relies on the TargetDamageWarhead default Spread of ONE " +
				"world unit (a cell is 1024), so only a near-exact hit lands at all");

			Assert.That(int.Parse(fields["Spread"]), Is.GreaterThan(1));
		}
	}
}
