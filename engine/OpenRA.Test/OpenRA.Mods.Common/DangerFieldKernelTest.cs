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

using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Warheads;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DangerFieldKernelTest
	{
		// READ OFF THE SHIPPING Info, not transcribed from it. The transcribed form carried
		// `healthDivisor: 10, costDivisor: 50` — the RA-scale values — and stayed green after the ruleset
		// outgrew them by two orders, because a copy of a default cannot notice the default moving. Every
		// number below is therefore whatever the field actually computes today.
		static readonly DangerFieldLayerInfo Shipping = new();
		static readonly DangerKernelParams Params = new(
			Shipping.RangeBufferCells, Shipping.MaxRadiusCells,
			Shipping.DurabilityBase, Shipping.HealthDivisor, Shipping.CostDivisor);

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

		// ---- The fire-cycle model (SustainedThroughput) ----
		//
		// TRANSCRIBED FROM THE MOD, AND COMPUTED THROUGH THE REAL FUNCTION — never hard-coded. An earlier
		// revision of this file wrote `groundThroughput: 23000 * 100` with a comment claiming those numbers
		// were "what the field ACTUALLY holds in play". They were the BROKEN formula's output, so the fixtures
		// documented the defect as ground truth and would have gone green against it forever. Deriving the
		// fixtures from real weapon parameters through the function under test is what makes that impossible:
		// if the cadence model regresses, every test below moves with it.
		const int Window = 100;

		// TankRound.Abrams (weapons-ballistics.yaml:600-603 inheriting ^TankRound :~570-599):
		// Damage 20000 (TargetDamage) + 3000 (SpreadDamage), Burst 1 (default), BurstWait 130,
		// no Magazine (default 1), no ReloadDelay. Actor ABRAMS: HP 28000, Cost 2500.
		static int AbramsThroughput => DangerFieldLayer.SustainedThroughput(
			damagePerShot: 23000, burst: 1, burstDelays: new[] { 5 }, burstWait: 130,
			magazine: 1, reloadDelay: 0, throughputWindow: Window);

		// 5.56mm.AR (weapons-ballistics.yaml:110-117 inheriting ^5.56mm): Damage 200, Range 14c0,
		// Magazine 100, ReloadDelay 150, Burst 10, BurstDelays 1, BurstWait 8. Actor E3: HP 200, Cost 100.
		static int RifleThroughput => DangerFieldLayer.SustainedThroughput(
			damagePerShot: 200, burst: 10, burstDelays: new[] { 1 }, burstWait: 8,
			magazine: 100, reloadDelay: 150, throughputWindow: Window);

		// 5.56mm.E3 (weapons-ballistics.yaml:103-109): Damage 200, Magazine 20, ReloadDelay 60,
		// Burst 2, BurstDelays 5, BurstWait 12.
		static int CarbineThroughput => DangerFieldLayer.SustainedThroughput(
			damagePerShot: 200, burst: 2, burstDelays: new[] { 5 }, burstWait: 12,
			magazine: 20, reloadDelay: 60, throughputWindow: Window);

		[Test]
		public void FireCycleIsMaxOfReloadAndBurstWait_NotTheirSum_AndCountsTheMagazine()
		{
			// THE CADENCE RULING, pinned. Armament blocks firing while `IsReloading || IsWaitingBurst`
			// (Armament.cs:327) and decrements both counters in parallel (:283-287), so a weapon arming both
			// on the same shot waits the LONGER, not the sum. And ReloadDelay is armed only when the MAGAZINE
			// empties (:608-622, called once per SHOT at :380) — so Magazine counts shots and a magazine-swap
			// delay is amortised over Magazine shots, not paid every burst.
			Assert.Multiple(() =>
			{
				// Abrams: one shot, then the full 130-tick BurstWait. 23,000 damage / 130 ticks.
				Assert.That(AbramsThroughput, Is.EqualTo(23000 * Window / 130));
				Assert.That(AbramsThroughput, Is.EqualTo(17692), "a tank shell is ~17.7k damage per 100 ticks");

				// Rifle: 10 bursts of (9 intra-burst gaps of 1 + 8 wait) = 170 ticks, plus the 150-tick
				// magazine swap MINUS the final burst's 8-tick wait it overlaps = 312 ticks per 100 shots.
				Assert.That(RifleThroughput, Is.EqualTo(100 * 200 * Window / 312));
				Assert.That(RifleThroughput, Is.EqualTo(6410), "an automatic rifle is ~6.4k damage per 100 ticks");

				// Carbine: 10 bursts of (5 + 12) = 170, plus (60 - 12) = 218 ticks per 20 shots.
				Assert.That(CarbineThroughput, Is.EqualTo(20 * 200 * Window / 218));
			});
		}

		[Test]
		public void TheOldCadenceMisrankedWeaponClassesAgainstEachOther()
		{
			// WHY THE OLD FORMULA COULD NOT BE FIXED BY RESCALING ANYTHING DOWNSTREAM. `damage x Burst x
			// window / ReloadDelay` erred in OPPOSITE directions for the two weapon classes, so no single
			// factor corrects it — and a reference RATIO cannot cancel it either, which is the whole reason
			// the danger unit had to wait on this fix rather than ship alongside it.
			var oldAbrams = 23000 * 1 * Window / 1;      // no ReloadDelay ⇒ the `reload = 1` fallback.
			var oldRifle = 200 * 10 * Window / 150;      // divided by a magazine swap as if it were the shot gap.

			Assert.Multiple(() =>
			{
				// Scaled by 10 throughout so integer division does not blunt the comparison.
				Assert.That(oldAbrams, Is.EqualTo(2300000));
				Assert.That(oldAbrams * 10 / AbramsThroughput, Is.EqualTo(1300), "the tank was over-stated 130.0x");
				Assert.That(oldRifle, Is.EqualTo(1333));
				Assert.That(RifleThroughput * 10 / oldRifle, Is.EqualTo(48), "the rifle was UNDER-stated 4.8x");

				// The net effect is a RE-RANKING, not a rescale. Old: the tank read 1725x the rifle's
				// throughput. True: 2.8x. Both favour the tank, but the old gap was three orders too wide —
				// and once the RA-scale durability weight then in force (x29.5 for the tank; rescaled to
				// x1.28 by the divisor fix pinned in DurabilityWeightIsATieBreakNotASecondLethalityTerm)
				// and the int overflow it caused are applied on top, the sign of the comparison actually
				// inverted in the shipped field.
				Assert.That(oldAbrams * 10 / oldRifle, Is.EqualTo(17254));
				Assert.That(AbramsThroughput * 10 / RifleThroughput, Is.EqualTo(27));
			});
		}

		// ---- The danger UNIT: reference intensity + threshold conversion ----
		//
		// Facts built from the REAL throughputs above plus the real actor HP/Cost, so the durability weight
		// and the kernel intensity are both what the shipping field computes.
		static DangerKernelFacts Mbt => new(groundRange: Cells(25), airRange: 0,
			groundThroughput: AbramsThroughput, airThroughput: 0, health: 28000, cost: 2500);
		static DangerKernelFacts Rifleman => new(groundRange: Cells(14), airRange: 0,
			groundThroughput: RifleThroughput, airThroughput: 0, health: 200, cost: 100);
		static DangerKernelFacts Carbine => new(groundRange: Cells(10), airRange: 0,
			groundThroughput: CarbineThroughput, airThroughput: 0, health: 200, cost: 100);

		[Test]
		public void ReferenceIntensityIsTheMedianContributingType()
		{
			// Three contributing types ⇒ the reference is the middle core intensity. Sorted internally,
			// so the answer cannot depend on ruleset iteration order (a determinism requirement of the stack).
			var facts = new[] { Mbt, Rifleman, Carbine };
			var expected = new List<int>();
			foreach (var f in facts)
				expected.Add(DangerKernelMath.Compute(f, DangerChannel.Ground, 100, Params).Intensity);

			expected.Sort();

			var reference = DangerKernelMath.ReferenceIntensity(facts, DangerChannel.Ground, Params);
			Assert.That(reference, Is.EqualTo(expected[1]), "reference is the median contributing core intensity");

			var shuffled = new[] { Carbine, Mbt, Rifleman };
			Assert.That(DangerKernelMath.ReferenceIntensity(shuffled, DangerChannel.Ground, Params),
				Is.EqualTo(reference), "reference is independent of enumeration order");
		}

		[Test]
		public void ReferenceIntensitySkipsTypesThatDoNotThreatenTheChannel()
		{
			// An unarmed truck has no kernel; it must not drag the median down by counting as a 0.
			var withTruck = DangerKernelMath.ReferenceIntensity(
				new[] { Mbt, Rifleman, Carbine, Truck }, DangerChannel.Ground, Params);
			var withoutTruck = DangerKernelMath.ReferenceIntensity(
				new[] { Mbt, Rifleman, Carbine }, DangerChannel.Ground, Params);

			Assert.That(withTruck, Is.EqualTo(withoutTruck), "non-contributing types are excluded, not counted as 0");
			Assert.That(DangerKernelMath.ReferenceIntensity(new[] { Truck }, DangerChannel.Ground, Params),
				Is.EqualTo(0), "no contributing type ⇒ no reference");
		}

		[Test]
		public void DangerUnitConversionPreservesItsSentinels()
		{
			var reference = DangerKernelMath.ReferenceIntensity(
				new[] { Mbt, Rifleman, Carbine }, DangerChannel.Ground, Params);

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
		public void ComputeSaturatesInsteadOfWrappingOnAbsurdWeaponData()
		{
			// The clamp added on 2026-08-09 was previously exercised by NOTHING — the same shape as the bug
			// that hid here before, since an untested saturation path is indistinguishable from a wrap until
			// something reaches it. Real data no longer comes close (a believed Abrams is ~5.2e5, ~41x below
			// the ceiling), so this drives it with data no ruleset should produce, which is exactly the case
			// the clamp exists for: the guarantee is that intensity stays MONOTONE and POSITIVE however
			// extreme the inputs, because a wrapped negative would read as the safest ground on the map.
			var absurd = new DangerKernelFacts(groundRange: Cells(20), airRange: 0,
				groundThroughput: int.MaxValue / 2, airThroughput: 0, health: 1000000, cost: 100000);
			var merelyHuge = new DangerKernelFacts(groundRange: Cells(20), airRange: 0,
				groundThroughput: int.MaxValue / 4, airThroughput: 0, health: 1000000, cost: 100000);

			var a = DangerKernelMath.Compute(absurd, DangerChannel.Ground, 100, Params);
			var h = DangerKernelMath.Compute(merelyHuge, DangerChannel.Ground, 100, Params);

			Assert.Multiple(() =>
			{
				Assert.That(a.Intensity, Is.GreaterThan(0), "saturated intensity must stay positive, never wrap");
				Assert.That(h.Intensity, Is.GreaterThan(0));
				Assert.That(a.Intensity, Is.GreaterThanOrEqualTo(h.Intensity), "and must stay monotone in throughput");
				Assert.That(a.Intensity, Is.EqualTo(int.MaxValue), "the ceiling is int.MaxValue, not a wrapped value");
			});
		}

		// The kernel taper Stamp applies: full intensity at the contact, 1/(r+1) of it at the outer ring.
		static int ContributionAt(in DangerKernelFacts f, int confidence, int distanceCells)
		{
			var k = DangerKernelMath.Compute(f, DangerChannel.Ground, confidence, Params);
			return (int)((long)k.Intensity * (k.RadiusCells - distanceCells + 1) / (k.RadiusCells + 1));
		}

		[Test]
		public void EvacThresholdSeparatesAContactOnTopOfUsFromADecayedRumourAtMaxRange()
		{
			// THE REGRESSION PIN FOR THE 2026-08-09 TRUCK LOOP, restated STRUCTURALLY rather than against the
			// play log's raw numbers. An earlier revision asserted `evacLevel > 66834`, the median reading at
			// evac entry in that log — but that log was recorded with the cadence bug live, so every heavy
			// contact in it was stamping the clamped floor of 1 and the distribution it measured no longer
			// exists. Calibrating the new threshold against it would have been fitting to a corrupted sample.
			//
			// What survives the recalibration is the PROPERTY the threshold has to have, which is what the
			// truck loop actually violated: a truck must evacuate for something that can hurt it now, and must
			// NOT evacuate for a fading rumour at the edge of its envelope. The old raw 60 failed the second
			// half — trucks entered evac at readings of 68 while parked at their own beachhead.
			//
			// BOUNDED FROM BOTH SIDES, which the first version of this test was not: it asserted only that a
			// point-blank TANK (521,914) still fires, and a tank is so far above the threshold that doubling
			// EvacDangerUnits 50 -> 100, or even 50 -> 500, left the test green. It could detect a threshold
			// set too LOW (the shipped bug) but not one set high enough to disable the evac outright. The
			// upper bound below is a fully-believed RIFLEMAN on the truck's own cell — an unarmoured supply
			// truck with enemy infantry standing on it must pull back — which binds at roughly 2x.
			//
			// And it reads the SHIPPING value off the Info rather than a literal 50, so retuning the field
			// moves this test instead of leaving it pinned to a number nothing uses.
			var evacUnits = new SupplyFollowerBotModuleInfo().EvacDangerUnits;
			var reference = DangerKernelMath.ReferenceIntensity(
				new[] { Mbt, Rifleman, Carbine }, DangerChannel.Ground, Params);
			var evacLevel = DangerKernelMath.DangerUnitsToField(evacUnits, reference);

			Assert.Multiple(() =>
			{
				// MUST fire (lower bound): a fully-believed tank on the truck's cell.
				Assert.That(ContributionAt(Mbt, 100, 0), Is.GreaterThan(evacLevel),
					"a believed MBT at point-blank must still evacuate the truck — this is a threshold, not a disable");

				// MUST fire (UPPER bound — the binding one): enemy infantry standing on the truck.
				Assert.That(ContributionAt(Rifleman, 100, 0), Is.GreaterThan(evacLevel),
					"a believed rifleman ON the truck's cell must evacuate it — a threshold above this disables the evac");

				// MUST NOT fire: a mobile contact decayed to BeliefStore.MinConfidence (15) sitting at the
				// outer ring of its own envelope. That is the "distant rumour" the old threshold could not
				// distinguish from a tank in the truck's face.
				Assert.That(ContributionAt(Mbt, 15, 27), Is.LessThan(evacLevel),
					"a decayed tank contact at max range must NOT evacuate a truck");
				Assert.That(ContributionAt(Rifleman, 100, 16), Is.LessThan(evacLevel),
					"nor a fully-believed rifleman at the edge of ITS envelope");

				// And the ordering the cadence fix restored: a tank must out-threaten a rifleman at the cell.
				Assert.That(ContributionAt(Mbt, 100, 0), Is.GreaterThan(ContributionAt(Rifleman, 100, 0)),
					"an armoured contact must read denser than an infantry one");
			});
		}

		// ---- The durability weight: dynamic range, and what that range is allowed to do ----
		//
		// REAL WW3MOD HULLS (HP / Cost transcribed from the ruleset, cross-checked against
		// `OpenRA.Utility ww3mod --danger-reference --verbose`, which computes this table through the
		// production code). Throughputs come from the same dumper — they are outputs of the cadence model
		// pinned above, not independent constants.
		static readonly (string Name, int Health, int Cost, int Throughput)[] RealHulls =
		{
			("e2 rifleman",  200,   100,   2797),
			("e6 at-team",   200,   250,    818),
			("bradley",      14000, 1500,  10014),
			("t90",          24000, 2400,  21090),
			("abrams",       28000, 2500,  17846),
			("tos",          20000, 2000,  25116),
			("m270",         10000, 1800,  64645),
			("hind",         800,   4000, 245000),
		};

		static int Weight(int health, int cost, int healthDivisor, int costDivisor)
			=> 100 + health / healthDivisor + cost / costDivisor;

		[Test]
		public void DurabilityWeightIsATieBreakNotASecondLethalityTerm()
		{
			// THE DIVISOR PIN. `DurabilityBase + HP/HealthDivisor + Cost/CostDivisor` shipped with RA-era
			// divisors (10 and 50) under a comment claiming "~1.0x for a fragile, cheap unit, rising" — true
			// of Red Alert's ~50-800 HP hulls, false of WW3MOD's 200-28,000. The real band was 1.20x-29.50x,
			// so the "weight" had a wider dynamic range than most of the throughput spread it was meant to
			// break ties within, and was silently acting as a second, HP-shaped lethality term.
			var weights = new List<int>();
			foreach (var h in RealHulls)
				weights.Add(Weight(h.Health, h.Cost, Shipping.HealthDivisor, Shipping.CostDivisor));

			weights.Sort();
			var min = weights[0];
			var max = weights[^1];

			Assert.Multiple(() =>
			{
				// A fragile, cheap unit is EXACTLY the baseline — the documented intent, now literally true
				// rather than approximately claimed.
				Assert.That(Weight(200, 100, Shipping.HealthDivisor, Shipping.CostDivisor),
					Is.EqualTo(Shipping.DurabilityBase), "a rifleman must weigh exactly 1.00x");

				// And the heaviest hull in the ruleset is a modest premium on top of it, not an order.
				Assert.That(max, Is.EqualTo(128), "the Abrams, the ruleset's heaviest contributor, weighs 1.28x");
				Assert.That(min, Is.EqualTo(Shipping.DurabilityBase));

				// THE PROPERTY THAT MATTERS, stated as a bound rather than as "it never re-ranks" — which
				// would be false for any weight with range at all. A multiplicative weight can only invert
				// two types whose throughputs sit WITHIN its own max/min ratio; everything further apart is
				// safe from it. That ratio is what the fix collapses, and it is the honest measure of how
				// much of the throughput ranking the weight is allowed to overrule.
				Assert.That(max * 100 / min, Is.LessThan(150),
					"the weight may only reorder types within ~1.3x throughput of each other");

				// The same ratio under the divisors this replaces, computed rather than asserted from
				// memory: 29.50x / 1.20x. A weight that can overrule a 24x throughput gap is not a tie-break
				// — 24x spans most of the ruleset, so the field was ranking by hit points wearing damage's name.
				var oldMin = int.MaxValue;
				var oldMax = 0;
				foreach (var h in RealHulls)
				{
					var w = Weight(h.Health, h.Cost, 10, 50);
					oldMin = Math.Min(oldMin, w);
					oldMax = Math.Max(oldMax, w);
				}

				Assert.That(oldMax, Is.EqualTo(2950), "the Abrams used to weigh 29.50x");
				Assert.That(oldMax * 100 / oldMin, Is.GreaterThan(2000),
					"the RA-scale weight could overrule a 20x+ throughput gap");
			});
		}

		[Test]
		public void DurabilityWeightPreservesTheThroughputRankingItIsAllowedToBreakTiesWithin()
		{
			// The bound above, exercised: every pair of real hulls whose throughputs differ by more than the
			// weight's dynamic range must come out of Compute in throughput order. This is the guarantee the
			// old divisors could not offer for ANY pair inside 24x — which is nearly every pair here.
			Assert.Multiple(() =>
			{
				for (var i = 0; i < RealHulls.Length; i++)
				{
					for (var j = i + 1; j < RealHulls.Length; j++)
					{
						var a = RealHulls[i];
						var b = RealHulls[j];
						var lo = Math.Min(a.Throughput, b.Throughput);
						var hi = Math.Max(a.Throughput, b.Throughput);

						// Only pairs outside the weight's reach are constrained; inside it, a reorder is the
						// tie-break doing its job.
						if (hi * 100 / lo <= 150)
							continue;

						var ka = DangerKernelMath.Compute(
							new DangerKernelFacts(Cells(10), 0, a.Throughput, 0, a.Health, a.Cost),
							DangerChannel.Ground, 100, Params);
						var kb = DangerKernelMath.Compute(
							new DangerKernelFacts(Cells(10), 0, b.Throughput, 0, b.Health, b.Cost),
							DangerChannel.Ground, 100, Params);

						Assert.That(ka.Intensity > kb.Intensity, Is.EqualTo(a.Throughput > b.Throughput),
							$"{a.Name} vs {b.Name}: durability must not overrule a {hi * 100 / lo}% throughput gap");
					}
				}
			});
		}

		// ---- Warheads that cannot harm anything ----

		// The ruleset's actual armor population, from `--danger-reference`. `Brick` is deliberately absent:
		// it is listed by the targeter tables below but exists on no WW3MOD actor, which is half of why
		// those weapons look harmless and are not.
		static readonly HashSet<string> Ww3ArmorTypes = new()
		{
			"None", "Wood", "Concrete", "Light", "Medium", "Heavy", "Unarmored", "Kevlar", "Indestructable"
		};

		static TargetDamageWarhead WarheadVersus(params (string Armor, int Percent)[] versus)
		{
			// `Versus` is a readonly REFERENCE to a mutable dictionary, so a warhead's damage table can be
			// built in a test even though every scalar on the warhead is YAML-loaded and unsettable.
			var wh = new TargetDamageWarhead();
			foreach (var (armor, percent) in versus)
				wh.Versus[armor] = percent;

			return wh;
		}

		[Test]
		public void OmittedArmorClassIsFullDamage_NotZero()
		{
			// THE ASYMMETRY, pinned, because it is the one that makes "this weapon is harmless" unsafe to
			// read off a Versus table. DamageWarhead.DamageVersus (:101-108) early-returns full damage for an
			// EMPTY table, but for a non-empty one filters to the classes it LISTS — an unlisted class
			// matches nothing and takes the unmodified 100%. Omission is the OPPOSITE of a zero.
			//
			// The concrete case is IskanderTargeter / HIMARSTargeter (weapons-missiles.yaml:284-306), the two
			// force-fire spotter weapons reported as phantom contributors to the danger field. They zero
			// None/Wood/Concrete/Light/Medium/Heavy/Brick — but `Brick` is not an armor class here, while
			// Kevlar (EVERY soldier), Unarmored and Indestructable are, and are unlisted. So the targeters
			// deal their full 50 to infantry and are real, if feeble, threats. The exclusion below therefore
			// drops NOTHING from the current ruleset, and that is the correct outcome, not a broken filter.
			var targeter = WarheadVersus(
				("None", 0), ("Wood", 0), ("Concrete", 0), ("Light", 0),
				("Medium", 0), ("Heavy", 0), ("Brick", 0));

			Assert.Multiple(() =>
			{
				Assert.That(DangerFieldLayer.WarheadIsHarmless(targeter, Ww3ArmorTypes, false), Is.False,
					"a table that omits Kevlar cannot be harmless in a ruleset whose infantry wear it");

				// Against a ruleset containing ONLY the classes it zeroes, the very same table IS harmless —
				// so the verdict is a property of the (weapon, ruleset) pair, never of the weapon alone.
				var narrowRuleset = new HashSet<string> { "None", "Wood", "Concrete", "Light", "Medium", "Heavy" };
				Assert.That(DangerFieldLayer.WarheadIsHarmless(targeter, narrowRuleset, false), Is.True);
			});
		}

		[Test]
		public void HarmlessTestFailsOpenInEveryUncertainDirection()
		{
			var allZero = WarheadVersus(
				("None", 0), ("Wood", 0), ("Concrete", 0), ("Light", 0), ("Medium", 0),
				("Heavy", 0), ("Unarmored", 0), ("Kevlar", 0), ("Indestructable", 0));

			Assert.Multiple(() =>
			{
				// The one and only case that drops a weapon: every class the ruleset has, explicitly zeroed.
				Assert.That(DangerFieldLayer.WarheadIsHarmless(allZero, Ww3ArmorTypes, false), Is.True);

				// An EMPTY table means full damage to everything (the early return), not "no damage".
				Assert.That(DangerFieldLayer.WarheadIsHarmless(WarheadVersus(), Ww3ArmorTypes, false), Is.False,
					"an absent Versus is full damage, not an all-zero one");

				// WEAK IS NOT HARMLESS. One class at 1% keeps the weapon in the field — the danger field is
				// allowed to rank it last, never to pretend it is not there.
				var oneWeakClass = WarheadVersus(
					("None", 0), ("Wood", 0), ("Concrete", 0), ("Light", 0), ("Medium", 0),
					("Heavy", 0), ("Unarmored", 0), ("Kevlar", 1), ("Indestructable", 0));
				Assert.That(DangerFieldLayer.WarheadIsHarmless(oneWeakClass, Ww3ArmorTypes, false), Is.False);

				// An armor the ruleset cannot prove will be matched — null-typed, conditional, or restricted
				// by a HitShape — makes the whole verdict unprovable, so even a complete zero table is kept.
				Assert.That(DangerFieldLayer.WarheadIsHarmless(allZero, Ww3ArmorTypes, true), Is.False,
					"unprovable armor must keep every weapon, not drop one");

				// And no armor population at all (an empty or absent set) is no evidence of harmlessness.
				Assert.That(DangerFieldLayer.WarheadIsHarmless(allZero, new HashSet<string>(), false), Is.False);
				Assert.That(DangerFieldLayer.WarheadIsHarmless(allZero, null, false), Is.False);
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
