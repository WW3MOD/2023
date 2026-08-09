#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage B danger-kernel test.
 *
 * Pins the two things the design §2B fixes in stone: (1) which channel a weapon
 * feeds, derived from its ValidTargets (Air/Helicopter vs ground types), and
 * (2) the kernel shape — range sets radius, lethality×durability×confidence sets
 * intensity — so a sniper reads big-and-faint while a humvee reads small-and-dense
 * and an unarmed truck reads ~nothing. Pure math; no world mounted.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DangerFieldKernelTest
	{
		// Mirrors DangerFieldLayerInfo defaults.
		static readonly DangerKernelParams Params = new(
			rangeBufferCells: 2, maxRadiusCells: 32, durabilityBase: 100, healthDivisor: 10, costDivisor: 50);

		static int Cells(int n) => n * 1024;

		// ---- Channel membership from ValidTargets (the Air ≠ Helicopter discriminator) ----

		static readonly (string Name, string[] Targets, bool Ground, bool Air)[] MembershipTable =
		{
			// Ground MG: lists Helicopter, so it threatens BOTH ground troops and our helis.
			("mg-7.62mm",     new[] { "Infantry", "Unarmored", "Helicopter" }, true,  true),
			// Tank main gun: heavy/light ground armour only — no air threat.
			("tank-gun",      new[] { "Heavy", "Light" },                      true,  false),
			// Rifle: infantry only.
			("rifle",         new[] { "Infantry" },                            true,  false),
			// MANPAD / Stinger-class SAM: Air only — pure anti-air, no ground threat.
			("manpad",        new[] { "Air" },                                 false, true),
			// Tunguska's 30mm autocannon: Helicopter only in the data — anti-air, not anti-ground.
			("tunguska-30mm", new[] { "Helicopter" },                          false, true),
			// Interceptors / CRAM (20mm_CRAM, AACannon, SurfaceToAirMissile, AirToAirMissile) list
			// "Air, ICBM". ICBM is an air-domain marker, NOT a ground target — so these must NOT
			// stamp a ground aura. Regression guard for the ICBM ground-leak fix.
			("cram-air-icbm", new[] { "Air", "ICBM" },                         false, true),
		};

		[Test]
		public void ChannelMembershipMatchesValidTargets()
		{
			Assert.Multiple(() =>
			{
				foreach (var row in MembershipTable)
				{
					var targets = new BitSet<TargetableType>(row.Targets);
					Assert.That(DangerKernelMath.WeaponThreatensGround(targets), Is.EqualTo(row.Ground),
						$"{row.Name} ground membership");
					Assert.That(DangerKernelMath.WeaponThreatensAir(targets), Is.EqualTo(row.Air),
						$"{row.Name} air membership");
				}
			});
		}

		// ---- Kernel shapes ----

		// Representative ground-domain facts (range in cells, throughput = damage/window).
		static readonly DangerKernelFacts Sniper = new(groundRange: Cells(12), airRange: 0, groundThroughput: 30, airThroughput: 0, health: 100, cost: 300);
		static readonly DangerKernelFacts Humvee = new(groundRange: Cells(6), airRange: 0, groundThroughput: 300, airThroughput: 0, health: 300, cost: 600);
		static readonly DangerKernelFacts Tank = new(groundRange: Cells(24), airRange: 0, groundThroughput: 400, airThroughput: 0, health: 1000, cost: 1500);
		static readonly DangerKernelFacts Truck = new(groundRange: 0, airRange: 0, groundThroughput: 0, airThroughput: 0, health: 250, cost: 400);

		[Test]
		public void SniperIsWideAndFaint_HumveeIsNarrowAndDense()
		{
			var sniper = DangerKernelMath.Compute(Sniper, DangerChannel.Ground, 100, Params);
			var humvee = DangerKernelMath.Compute(Humvee, DangerChannel.Ground, 100, Params);

			Assert.Multiple(() =>
			{
				Assert.That(sniper.Contributes, Is.True);
				Assert.That(humvee.Contributes, Is.True);
				// Range sets width: the sniper reaches farther.
				Assert.That(sniper.RadiusCells, Is.GreaterThan(humvee.RadiusCells), "sniper aura is wider");
				// Lethality sets density: the humvee's core is far more intense.
				Assert.That(humvee.Intensity, Is.GreaterThan(sniper.Intensity), "humvee aura is denser");
			});
		}

		[Test]
		public void TankOutweighsHumvee()
		{
			// Bigger gun + far higher durability/cost ⇒ the densest ground aura of the set.
			var tank = DangerKernelMath.Compute(Tank, DangerChannel.Ground, 100, Params);
			var humvee = DangerKernelMath.Compute(Humvee, DangerChannel.Ground, 100, Params);
			Assert.That(tank.Intensity, Is.GreaterThanOrEqualTo(humvee.Intensity));
			Assert.That(tank.RadiusCells, Is.GreaterThan(humvee.RadiusCells));
		}

		// ---- The danger UNIT: reference intensity + threshold conversion ----
		//
		// WW3MOD-SCALE facts, unlike the small illustrative ones above. Weapon damage in this mod is 10^3-10^5
		// and ~90% of weapons leave ReloadDelay unset (paced by BurstWait instead), so WeaponThroughput's
		// divisor is 1 and throughput is raw burst damage x 100. These numbers are what the field ACTUALLY
		// holds in play, and every test below depends on that being represented honestly rather than scaled
		// down for readability — the entire bug was reasoning about this field at the wrong magnitude.
		static readonly DangerKernelFacts Mbt = new(groundRange: Cells(9), airRange: 0,
			groundThroughput: 23000 * 100, airThroughput: 0, health: 50000, cost: 2000);
		static readonly DangerKernelFacts Spg = new(groundRange: Cells(20), airRange: 0,
			groundThroughput: 18000 * 100, airThroughput: 0, health: 25000, cost: 2000);
		static readonly DangerKernelFacts AtgmTeam = new(groundRange: Cells(10), airRange: 0,
			groundThroughput: 12000 * 100, airThroughput: 0, health: 800, cost: 200);
		// The one class that DOES set ReloadDelay (60) and Burst (2): 200 dmg x 2 x 100 / 60.
		static readonly DangerKernelFacts Rifleman = new(groundRange: Cells(6), airRange: 0,
			groundThroughput: 200 * 2 * 100 / 60, airThroughput: 0, health: 800, cost: 100);

		[Test]
		public void ReferenceIntensityIsTheMedianContributingType()
		{
			// Five contributing types ⇒ the reference is the 3rd smallest core intensity. Sorted internally,
			// so the answer cannot depend on ruleset iteration order (a determinism requirement of the stack).
			var facts = new[] { Mbt, Spg, AtgmTeam, Rifleman, Humvee };
			var expected = new List<int>();
			foreach (var f in facts)
				expected.Add(DangerKernelMath.Compute(f, DangerChannel.Ground, 100, Params).Intensity);

			expected.Sort();

			var reference = DangerKernelMath.ReferenceIntensity(facts, DangerChannel.Ground, Params);
			Assert.That(reference, Is.EqualTo(expected[2]), "reference is the median contributing core intensity");

			var shuffled = new[] { Rifleman, Mbt, Humvee, Spg, AtgmTeam };
			Assert.That(DangerKernelMath.ReferenceIntensity(shuffled, DangerChannel.Ground, Params),
				Is.EqualTo(reference), "reference is independent of enumeration order");
		}

		[Test]
		public void ReferenceIntensitySkipsTypesThatDoNotThreatenTheChannel()
		{
			// An unarmed truck has no kernel; it must not drag the median down by counting as a 0.
			var withTruck = DangerKernelMath.ReferenceIntensity(
				new[] { Mbt, Spg, AtgmTeam, Truck }, DangerChannel.Ground, Params);
			var withoutTruck = DangerKernelMath.ReferenceIntensity(
				new[] { Mbt, Spg, AtgmTeam }, DangerChannel.Ground, Params);

			Assert.That(withTruck, Is.EqualTo(withoutTruck), "non-contributing types are excluded, not counted as 0");
			Assert.That(DangerKernelMath.ReferenceIntensity(new[] { Truck }, DangerChannel.Ground, Params),
				Is.EqualTo(0), "no contributing type ⇒ no reference");
		}

		[Test]
		public void DangerUnitConversionPreservesItsSentinels()
		{
			var reference = DangerKernelMath.ReferenceIntensity(
				new[] { Mbt, Spg, AtgmTeam, Rifleman, Humvee }, DangerChannel.Ground, Params);

			Assert.Multiple(() =>
			{
				// 0 units is 0 raw at any scale — this is what lets a literal "outside every believed
				// envelope" test convert losslessly instead of needing to stay on the raw scale.
				Assert.That(DangerKernelMath.DangerUnitsToField(0, reference), Is.EqualTo(0));

				// Negative means "guard disabled" to several consumers and must survive the conversion.
				Assert.That(DangerKernelMath.DangerUnitsToField(-1, reference), Is.EqualTo(-1));

				// 100 units IS one reference contact at point-blank — the definition of the unit.
				Assert.That(DangerKernelMath.DangerUnitsToField(100, reference), Is.EqualTo(reference));
				Assert.That(DangerKernelMath.DangerUnitsToField(50, reference), Is.EqualTo(reference / 2));

				// No reference (a ruleset with no ground-threatening type) must fail CLOSED: a level test
				// becomes unreachable rather than reading "everywhere is dangerous".
				Assert.That(DangerKernelMath.DangerUnitsToField(50, 0), Is.EqualTo(int.MaxValue));

				// References reach 10^8, so the product must be computed wide and clamped, not overflowed.
				Assert.That(DangerKernelMath.DangerUnitsToField(100000, 1000000000), Is.EqualTo(int.MaxValue));
			});
		}

		[Test]
		public void EvacThresholdInUnitsClearsTheAmbientDangerMeasuredInPlay()
		{
			// THE REGRESSION PIN FOR THE 2026-08-09 TRUCK LOOP, and it is stated in the numbers the user's own
			// play log produced rather than in the abstract. In that log the MEDIAN believed ground danger at
			// the moment a supply truck entered its danger-evac was 66,834 raw field units, and trucks
			// standing within 4 cells of their own Supply Route entered evac at readings as low as 68. The
			// shipped threshold was 60 RAW — below both — so the evac branch fired on ambient flicker and the
			// truck lurched backwards ~12 cells every ~48 s for the whole match.
			//
			// Expressed in danger units the same knob has to clear that ambient band by construction. If
			// anyone re-expresses these thresholds on the raw scale, or drops the reference denominator, this
			// goes red: 50 raw is not greater than 66,834.
			const int AmbientMedianAtEvacEntry = 66834;
			const int AmbientNearOwnSupplyRoute = 68;

			var reference = DangerKernelMath.ReferenceIntensity(
				new[] { Mbt, Spg, AtgmTeam, Rifleman, Humvee }, DangerChannel.Ground, Params);
			var evacLevel = DangerKernelMath.DangerUnitsToField(50, reference);

			Assert.Multiple(() =>
			{
				Assert.That(evacLevel, Is.GreaterThan(AmbientMedianAtEvacEntry),
					"EvacDangerUnits: 50 must sit above the ambient median measured at evac entry in play");
				Assert.That(evacLevel, Is.GreaterThan(AmbientNearOwnSupplyRoute * 100),
					"and far above the flicker seen on trucks parked at their own beachhead");

				// It must remain a THRESHOLD, not a disable: a believed contact actually on the cell still
				// trips it, so the fix cannot be mistaken for switching the evac off.
				var mbtCore = DangerKernelMath.Compute(Mbt, DangerChannel.Ground, 100, Params).Intensity;
				Assert.That(mbtCore, Is.GreaterThan(evacLevel),
					"a believed MBT at point-blank must still evacuate the truck");
			});
		}

		[Test]
		public void UnarmedTruckContributesNothing()
		{
			var ground = DangerKernelMath.Compute(Truck, DangerChannel.Ground, 100, Params);
			var air = DangerKernelMath.Compute(Truck, DangerChannel.Air, 100, Params);
			Assert.Multiple(() =>
			{
				Assert.That(ground.Contributes, Is.False);
				Assert.That(ground.RadiusCells, Is.EqualTo(0));
				Assert.That(ground.Intensity, Is.EqualTo(0));
				Assert.That(air.Contributes, Is.False);
			});
		}

		[Test]
		public void GroundOnlyUnitDoesNotFeedAirChannel()
		{
			// The tank has no air-capable weapon: it contributes to ground but not air.
			Assert.That(DangerKernelMath.Compute(Tank, DangerChannel.Ground, 100, Params).Contributes, Is.True);
			Assert.That(DangerKernelMath.Compute(Tank, DangerChannel.Air, 100, Params).Contributes, Is.False);
		}

		[Test]
		public void AirOnlyUnitFeedsOnlyAirChannel()
		{
			// A SAM: air range + throughput, zero ground.
			var manpad = new DangerKernelFacts(groundRange: 0, airRange: Cells(20), groundThroughput: 0, airThroughput: 120, health: 80, cost: 250);
			var air = DangerKernelMath.Compute(manpad, DangerChannel.Air, 100, Params);
			var ground = DangerKernelMath.Compute(manpad, DangerChannel.Ground, 100, Params);
			Assert.Multiple(() =>
			{
				Assert.That(air.Contributes, Is.True);
				Assert.That(air.RadiusCells, Is.EqualTo(20 + 2));
				Assert.That(ground.Contributes, Is.False);
			});
		}

		[Test]
		public void ConfidenceScalesIntensity()
		{
			var full = DangerKernelMath.Compute(Tank, DangerChannel.Ground, 100, Params);
			var half = DangerKernelMath.Compute(Tank, DangerChannel.Ground, 50, Params);
			Assert.Multiple(() =>
			{
				Assert.That(half.Intensity, Is.LessThan(full.Intensity), "lower confidence ⇒ fainter aura");
				Assert.That(half.RadiusCells, Is.EqualTo(full.RadiusCells), "confidence does not change reach");
				// Roughly linear: half confidence ≈ half intensity.
				Assert.That(half.Intensity, Is.EqualTo(full.Intensity / 2).Within(1));
			});
		}

		[Test]
		public void TerritoryBaselineFeedsGroundOnly()
		{
			// The Stage-C territory baseline is derived from believed enemy GROUND reach, so it must
			// contribute to the ground channel ONLY — never the anti-air channel. An AA-free rear
			// area must stay air-safe for the Stage-D helicopter consumer.
			var (ground, air) = DangerKernelMath.BaselineChannels(37);
			Assert.Multiple(() =>
			{
				Assert.That(ground, Is.EqualTo(37), "baseline feeds the ground channel");
				Assert.That(air, Is.EqualTo(0), "baseline must NOT feed the air channel");
			});
		}

		[Test]
		public void RadiusIsBufferedAndCapped()
		{
			// Buffer: reach = range + RangeBufferCells.
			var humvee = DangerKernelMath.Compute(Humvee, DangerChannel.Ground, 100, Params);
			Assert.That(humvee.RadiusCells, Is.EqualTo(6 + 2));

			// Cap: a long-reach artillery kernel is clamped to MaxRadiusCells.
			var arty = new DangerKernelFacts(groundRange: Cells(40), airRange: 0, groundThroughput: 200, airThroughput: 0, health: 300, cost: 1200);
			var kernel = DangerKernelMath.Compute(arty, DangerChannel.Ground, 100, Params);
			Assert.That(kernel.RadiusCells, Is.EqualTo(Params.MaxRadiusCells));
		}
	}
}
