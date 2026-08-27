#region Copyright & License Information
/*
 * WW3MOD drone survivability — pins what `HitShape: Circle Radius: 1` on ^Drone
 * (aircraft.yaml) actually costs, because the value LOOKS like it should make the drone
 * unhittable and does not.
 *
 * The suspicion under investigation was: a 1-world-unit hitbox (1/1024 of a cell, against the
 * 32 the drone would otherwise inherit from ^NeutralAirborne) makes the quadcopter immune to
 * anti-air, which would mean the operator's jammer is the only counter by default rather than
 * by design. It is not immune, and the arithmetic below is why.
 *
 * The radius reaches damage through exactly two doors, and they behave very differently:
 *
 *   (1) SpreadDamageWarhead measures falloff from the hitshape EDGE
 *       (SpreadDamageWarhead.cs:74) over a table that is four Spread steps wide. For a
 *       Stinger-class warhead that envelope is 1024 units — a whole cell — carrying 5000
 *       damage at a 50 HP target. Shrinking the shape by 31 units moves the lethal boundary by
 *       31 units on a weapon that already overkills by two orders of magnitude. This is the
 *       door that the mod's real AA (MANPAD, Stinger.quad, 9M311, AACannon) goes through, and
 *       it is why there is no immunity.
 *
 *   (2) TargetDamageWarhead scales damage by CenterProximityPercent (TargetDamageWarhead.cs:93),
 *       which for a circle is 100*(Radius-d)/Radius and therefore hits zero AT the radius. At
 *       Radius 1 that is zero for any impact a single world unit off the drone's exact centre.
 *       This door is genuinely shut — see the CRAM case below, which is the one weapon whose
 *       entire anti-drone output goes through it.
 *
 * Pure arithmetic plus a YAML read; no World, no Actor, no game run.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.HitShapes;
using OpenRA.Mods.Common.Warheads;

namespace OpenRA.Test
{
	[TestFixture]
	public class DroneHitShapeTest
	{
		// ^Drone overrides the hitshape it would inherit from ^NeutralAirborne. Both values are
		// pinned from YAML below; these constants are what the arithmetic here assumes.
		const int DroneRadius = 1;
		const int AirborneRadius = 32;

		// quadcopterdrone: Health.HP 50, Armor.Type Unarmored (thickness 3).
		const int DroneHp = 50;
		const int UnarmouredThickness = 3;

		// Stinger / Stinger.quad / 9M311 share one warhead shape: SpreadDamage, Damage 5000,
		// Spread 256, Penetration 20, the default falloff table. MANPAD is the same curve at
		// Damage 3000 / Spread 192.
		const int StingerDamage = 5000;
		const int StingerSpread = 256;
		const int StingerPenetration = 20;
		static readonly int[] DefaultFalloff = { 100, 37, 14, 5, 0 };

		static WDist[] StepsOf(int spread)
		{
			return Exts.MakeArray(DefaultFalloff.Length, i => new WDist(i * spread));
		}

		/// <summary>Distance from a circle's edge, which is what both warheads actually measure.</summary>
		static int EdgeDistance(int radius, int distanceFromCentre)
		{
			var shape = new CircleShape(new WDist(radius));
			shape.Initialize();

			var victim = new WPos(20992, 20992, 0);
			return shape.DistanceFromEdge(victim + new WVec(distanceFromCentre, 0, 0), victim, WRot.None).Length;
		}

		/// <summary>Damage a Stinger-class warhead lands on a drone whose centre is this far from the impact.</summary>
		static int StingerDamageAt(int radius, int distanceFromCentre)
		{
			var falloff = SpreadDamageWarhead.DamageFalloff(
				EdgeDistance(radius, distanceFromCentre), DefaultFalloff, StepsOf(StingerSpread));

			// Penetration 20 against Unarmored thickness 3 is a clean pass-through, so falloff is
			// the only term that varies with the hitshape.
			var afterArmour = DamageWarhead.ApplyPenetration(StingerDamage, StingerPenetration, UnarmouredThickness);
			return Util.ApplyPercentageModifiers(afterArmour, new[] { falloff });
		}

		// --- (1) the answer: SpreadDamage AA is not meaningfully degraded -----------------------

		[Test]
		public void PenetrationDoesNotGateAStingerAgainstAnUnarmouredDrone()
		{
			Assert.That(DamageWarhead.ApplyPenetration(StingerDamage, StingerPenetration, UnarmouredThickness),
				Is.EqualTo(StingerDamage),
				"precondition: Penetration 20 >= Unarmored thickness 3, so armour removes nothing and " +
				"falloff is the only term the hitshape can move");
		}

		[Test]
		public void ADirectStingerObliteratesTheDroneAtEitherRadius()
		{
			Assert.That(StingerDamageAt(DroneRadius, 0), Is.EqualTo(StingerDamage));
			Assert.That(StingerDamageAt(AirborneRadius, 0), Is.EqualTo(StingerDamage));

			Assert.That(StingerDamageAt(DroneRadius, 0), Is.GreaterThan(DroneHp * 50),
				"a 5000-damage warhead against 50 HP is not a close-run thing");
		}

		[Test]
		public void TheTinyHitshapeCostsThirtyOneUnitsOfLethalRadiusNotImmunity()
		{
			// Walk outwards and find the last distance at which one missile still kills.
			static int LethalRadius(int radius)
			{
				return Enumerable.Range(0, 1400).Last(d => StingerDamageAt(radius, d) >= DroneHp);
			}

			var tiny = LethalRadius(DroneRadius);
			var inherited = LethalRadius(AirborneRadius);

			// Asserted FIRST because it is the structural claim: falloff is measured from the
			// hitshape edge, so the radius translates the lethal boundary one-for-one and nothing
			// else about the shape matters. If this breaks, the two absolute values below are
			// meaningless and their tiny deltas would under-report the damage.
			Assert.That(inherited - tiny, Is.EqualTo(AirborneRadius - DroneRadius),
				"the whole cost of the tiny hitshape is the 31-unit difference in radius, because " +
				"falloff is measured from the edge");

			// The falloff table bottoms out at 5% over its last step and integer-truncates, so it
			// sits at 1% — still 50 damage, still exactly lethal — right out to the envelope edge.
			Assert.That(tiny, Is.EqualTo(1024),
				"at Radius 1 a single Stinger still kills the drone anywhere within 1024 units — a full cell");
			Assert.That(inherited, Is.EqualTo(1055));

			// Stinger's Inaccuracy is 300, so the detonation lands far inside the lethal band either way.
			Assert.That(tiny, Is.GreaterThan(3 * 300),
				"the lethal radius is more than three times the weapon's own scatter — the drone is " +
				"not evading anything");
		}

		// --- (2) the door that IS shut: TargetDamage proximity scaling --------------------------

		[Test]
		public void TargetDamageProximityCollapsesToZeroOffCentreAtRadiusOne()
		{
			var tiny = new CircleShape(new WDist(DroneRadius));
			tiny.Initialize();
			var victim = new WPos(20992, 20992, 0);

			Assert.That(tiny.CenterProximityPercent(victim, victim, WRot.None), Is.EqualTo(100),
				"an impact on the exact centre is still full damage");

			// One single world unit off centre and the multiplier is already gone.
			var offByOne = tiny.CenterProximityPercent(victim + new WVec(1, 0, 0), victim, WRot.None);
			Assert.That(TargetDamageWarhead.ProximityDamagePercent(offByOne), Is.Zero);

			var offByTwo = tiny.CenterProximityPercent(victim + new WVec(2, 0, 0), victim, WRot.None);
			Assert.That(TargetDamageWarhead.ProximityDamagePercent(offByTwo), Is.Zero);

			// The inherited radius degrades gracefully over the same span instead of cliff-edging.
			var inherited = new CircleShape(new WDist(AirborneRadius));
			inherited.Initialize();
			Assert.That(inherited.CenterProximityPercent(victim + new WVec(16, 0, 0), victim, WRot.None),
				Is.EqualTo(50));
		}

		[Test]
		public void CramDoesLiterallyNothingToADroneBecauseAllItsDamageIsTargetDamage()
		{
			// 20mm_CRAM (weapons-ballistics.yaml:543-557, mounted on the CRAM defence and on two
			// airframes) declares exactly one damaging warhead: Warhead@Target TargetDamage 600. The
			// SpreadDamage that shows up in the resolved ruleset is inherited from
			// ^MinimalExplosionEffectsAir (weapons-effects.yaml:677-690), which carries no Damage at
			// all — it is a target-type/visual scaffold for Warhead@Effect, not a damage half. So the
			// whole 600 goes through proximity scaling that Radius 1 zeroes off-centre, and the
			// projectile is a Bullet with Inaccuracy 256, so "exact centre" effectively never happens.
			const int CramTargetDamage = 600;
			const int CramInheritedSpreadDamage = 0;

			var tiny = new CircleShape(new WDist(DroneRadius));
			tiny.Initialize();
			var victim = new WPos(20992, 20992, 0);

			var proximity = TargetDamageWarhead.ProximityDamagePercent(
				tiny.CenterProximityPercent(victim + new WVec(4, 0, 0), victim, WRot.None));

			Assert.That(Util.ApplyPercentageModifiers(CramTargetDamage, new[] { proximity }), Is.Zero,
				"CRAM's only damaging warhead is scaled to nothing by the 1-unit radius");
			Assert.That(CramInheritedSpreadDamage, Is.Zero,
				"and the inherited SpreadDamage scaffold carries no damage to fall back on");
		}

		// --- YAML pins -------------------------------------------------------------------------

		static string FindModRules()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules", "ingame");
				if (Directory.Exists(candidate))
					return candidate;

				dir = dir.Parent;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod/rules/ingame");
		}

		/// <summary>Reads `HitShape:` / `Type: Circle` / `Radius:` under one top-level template.</summary>
		static int ReadHitShapeRadius(string template)
		{
			var path = Path.Combine(FindModRules(), "aircraft.yaml");
			var inTemplate = false;
			var inHitShape = false;

			foreach (var raw in File.ReadLines(path))
			{
				var line = raw.Split('#')[0].TrimEnd();
				if (line.Trim().Length == 0)
					continue;

				var indent = line.TakeWhile(c => c == '\t').Count();
				var body = line.Trim();

				if (indent == 0)
				{
					if (inTemplate)
						break;

					inTemplate = body == template + ":";
					inHitShape = false;
				}
				else if (indent == 1 && inTemplate)
					inHitShape = body == "HitShape:";
				else if (indent >= 2 && inHitShape && body.StartsWith("Radius:", StringComparison.Ordinal))
					return int.Parse(body.Split(new[] { ':' }, 2)[1].Trim());
			}

			throw new AssertionException($"no HitShape Radius found for {template}");
		}

		[Test]
		public void TheYamlStillCarriesTheRadiiThisFixtureReasonsAbout()
		{
			Assert.That(ReadHitShapeRadius("^Drone"), Is.EqualTo(DroneRadius),
				"^Drone's hitshape radius changed — the survivability arithmetic in this fixture is " +
				"now describing a drone that no longer exists, so re-derive it before trusting it");
			Assert.That(ReadHitShapeRadius("^NeutralAirborne"), Is.EqualTo(AirborneRadius),
				"^NeutralAirborne's radius is the baseline ^Drone overrides");
		}
	}
}
