#region Copyright & License Information
/*
 * WW3MOD frontline-allocation math test — frontline-influence Phase 5.
 *
 * Pins the pure Phase-5 decision math (FrontlineAllocationMath) on synthetic inputs, no World:
 *   - man-the-line: budget spreads across ALL meaningfully-threatened avenues (coverage-first), surplus
 *     concentrates on the outnumbered ones, guarantees are deterministic, and the disabled/degenerate paths
 *     no-op;
 *   - weakest-point bias: the multiplier fires ONLY on the weakest sector, is inert at 100, and no-ops on the
 *     −1 (no-front) sentinel;
 *   - posture-hold: the enemy-vs-own ratio flips at the pinned boundary, gates on front-presence, and is inert
 *     when disabled.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FrontlineAllocationMathTest
	{
		// ---------- (1) Man-the-line allocation ----------

		[Test]
		public void AllocationCoversEveryMeaningfullyThreatenedAvenue()
		{
			// 4 avenues; enemy strength {5,3,0,8}, own {1,1,0,1}. minThreat 3 ⇒ avenues 0,1,3 are "manned"
			// (avenue 2 has no believed enemy, skipped). Budget 3 ⇒ exactly one picket on each manned avenue.
			var enemy = new[] { 5, 3, 0, 8 };
			var own = new[] { 1, 1, 0, 1 };
			var alloc = FrontlineAllocationMath.AllocateAcrossAvenues(enemy, own, totalForce: 3, minThreat: 3);

			Assert.Multiple(() =>
			{
				Assert.That(alloc, Is.EqualTo(new[] { 1, 1, 0, 1 }), "each threatened avenue manned; the un-threatened one is skipped");
				Assert.That(alloc[2], Is.EqualTo(0), "no believed enemy ⇒ no picket");
			});
		}

		[Test]
		public void AllocationConcentratesSurplusOnTheOutnumberedAvenue()
		{
			// 3 avenues on the front, enemy {10,4,4}, own {2,4,4}. minThreat 3 ⇒ all manned. Budget 6:
			//   coverage = 1 each (3 used); surplus 3 by outnumbered-weight max(0,enemy−own) = {8,0,0} ⇒ all 3
			//   extra pickets go to avenue 0 (the only one where the enemy outnumbers us).
			var enemy = new[] { 10, 4, 4 };
			var own = new[] { 2, 4, 4 };
			var alloc = FrontlineAllocationMath.AllocateAcrossAvenues(enemy, own, totalForce: 6, minThreat: 3);

			Assert.Multiple(() =>
			{
				Assert.That(alloc, Is.EqualTo(new[] { 4, 1, 1 }), "surplus lands on the outnumbered flank; the held sectors keep their picket");
				Assert.That(alloc[0] + alloc[1] + alloc[2], Is.EqualTo(6), "whole budget allocated");
			});
		}

		[Test]
		public void AllocationSplitsSurplusProportionallyWithDeterministicRemainder()
		{
			// enemy {9,6}, own {0,0} ⇒ weights {9,6}, both manned. Budget 5: coverage 1+1=2, surplus 3 by weight:
			//   3*9/15 = 1 (rem 12), 3*6/15 = 1 (rem 3); handedOut 2, leftover 1 → highest remainder = avenue 0.
			// Result {1+1+1, 1+1} = {3,2}.
			var enemy = new[] { 9, 6 };
			var own = new[] { 0, 0 };
			var alloc = FrontlineAllocationMath.AllocateAcrossAvenues(enemy, own, totalForce: 5, minThreat: 1);

			Assert.That(alloc, Is.EqualTo(new[] { 3, 2 }), "largest-remainder resolves the odd picket to the heavier avenue");
		}

		[Test]
		public void AllocationCoverageIsBudgetLimitedByThreatOrder()
		{
			// 3 manned avenues but only budget 2 ⇒ the two HEAVIEST get a picket (enemy desc), the lightest none.
			var enemy = new[] { 4, 9, 6 };
			var own = new[] { 0, 0, 0 };
			var alloc = FrontlineAllocationMath.AllocateAcrossAvenues(enemy, own, totalForce: 2, minThreat: 1);

			Assert.Multiple(() =>
			{
				Assert.That(alloc, Is.EqualTo(new[] { 0, 1, 1 }), "budget covers the two heaviest-threatened avenues first");
				Assert.That(alloc[0] + alloc[1] + alloc[2], Is.EqualTo(2), "budget not exceeded");
			});
		}

		[Test]
		public void AllocationParitySectorsGetCoverageButNoSurplus()
		{
			// enemy == own everywhere (parity), all manned. Budget 5, coverage 2, but every outnumbered-weight is
			// 0 ⇒ NO surplus is handed out (mass phase needs someone outweighed). Leftover 3 stays in reserve.
			var enemy = new[] { 4, 4 };
			var own = new[] { 4, 4 };
			var alloc = FrontlineAllocationMath.AllocateAcrossAvenues(enemy, own, totalForce: 5, minThreat: 1);

			Assert.That(alloc, Is.EqualTo(new[] { 1, 1 }), "parity ⇒ each avenue manned once, surplus held back (no concentration signal)");
		}

		[Test]
		public void AllocationDisabledAndDegeneratePathsNoOp()
		{
			Assert.Multiple(() =>
			{
				// No budget ⇒ all zeros.
				Assert.That(FrontlineAllocationMath.AllocateAcrossAvenues(new[] { 5, 5 }, new[] { 0, 0 }, 0, 1),
					Is.EqualTo(new[] { 0, 0 }), "zero force ⇒ nothing manned");

				// No avenue clears the threshold ⇒ all zeros (nothing to man).
				Assert.That(FrontlineAllocationMath.AllocateAcrossAvenues(new[] { 2, 1 }, new[] { 0, 0 }, 4, 5),
					Is.EqualTo(new[] { 0, 0 }), "sub-threshold threat ⇒ no picket");

				// Empty / null inputs ⇒ empty result, not a throw.
				Assert.That(FrontlineAllocationMath.AllocateAcrossAvenues(new int[0], new int[0], 4, 1), Is.Empty);
				Assert.That(FrontlineAllocationMath.AllocateAcrossAvenues(null, null, 4, 1), Is.Empty);
			});
		}

		// ---------- (2) Weakest-point bias ----------

		[Test]
		public void WeakestSectorBiasFiresOnlyOnTheWeakestSector()
		{
			Assert.Multiple(() =>
			{
				// Target in the weakest sector ⇒ the boost multiplier; elsewhere ⇒ neutral 100.
				Assert.That(FrontlineAllocationMath.WeakestSectorBiasFactor(2, weakestSector: 2, biasMultiplier: 150),
					Is.EqualTo(150), "target in the thin sector is boosted");
				Assert.That(FrontlineAllocationMath.WeakestSectorBiasFactor(1, weakestSector: 2, biasMultiplier: 150),
					Is.EqualTo(100), "a target in another sector is neutral");
			});
		}

		[Test]
		public void WeakestSectorBiasIsInertAtHundredAndOnNoFront()
		{
			Assert.Multiple(() =>
			{
				// A bare enable (multiplier 100) never changes a score ⇒ ranking byte-identical.
				Assert.That(FrontlineAllocationMath.WeakestSectorBiasFactor(2, 2, 100), Is.EqualTo(100),
					"multiplier 100 is inert even on the weakest sector");

				// No believed front (−1 sentinel) ⇒ always neutral, whatever the multiplier.
				Assert.That(FrontlineAllocationMath.WeakestSectorBiasFactor(0, FrontlineProfileMath.NoSector, 150),
					Is.EqualTo(100), "no front ⇒ no bias");
			});
		}

		[Test]
		public void WeakestSectorBiasReordersASyntheticCandidateSet()
		{
			// Two candidates: A scores 100 in the weakest sector 0, B scores 120 in sector 1. Pure score order is
			// B, A. A x150 bias lifts A to 150 > 120 ⇒ the reorder is B,A → A,B (tie-break not needed).
			const int weakest = 0;
			var aBiased = 100 * FrontlineAllocationMath.WeakestSectorBiasFactor(0, weakest, 150) / 100;
			var bBiased = 120 * FrontlineAllocationMath.WeakestSectorBiasFactor(1, weakest, 150) / 100;
			Assert.That(aBiased, Is.GreaterThan(bBiased), "the weakest-sector target overtakes a higher raw score");

			// Tie-break: equal biased scores keep their prior (score,dist,id) order — the bias is additive weight,
			// not a hard override, so equal results stay stable. Here A x150=150, C(sector 0, raw 100) x150=150.
			var cBiased = 100 * FrontlineAllocationMath.WeakestSectorBiasFactor(0, weakest, 150) / 100;
			Assert.That(aBiased, Is.EqualTo(cBiased), "same sector + same raw score ⇒ same biased score (stable tie)");
		}

		// ---------- (3) Sector posture hold ----------

		[Test]
		public void PostureHoldFlipsAtThePinnedRatioBoundary()
		{
			// ownStrengthFloor 0 ⇒ floor disabled, so this pins the pure ratio math (identical to the pre-floor
			// signature). own 5 is above any sensible floor anyway.
			Assert.Multiple(() =>
			{
				// holdRatioPct 200 = "hold when the enemy is at least 2× our strength here."
				Assert.That(FrontlineAllocationMath.SectorPostureHold(sectorOwn: 5, sectorEnemy: 10, frontierEdges: 2, holdRatioPct: 200, ownStrengthFloor: 0),
					Is.True, "enemy exactly 2× own ⇒ hold (boundary inclusive)");
				Assert.That(FrontlineAllocationMath.SectorPostureHold(5, 9, 2, 200, 0),
					Is.False, "enemy just under 2× own ⇒ press");
				Assert.That(FrontlineAllocationMath.SectorPostureHold(5, 11, 2, 200, 0),
					Is.True, "enemy over 2× own ⇒ hold");
			});
		}

		[Test]
		public void PostureHoldGatesOnFrontPresenceAndEnemyForce()
		{
			Assert.Multiple(() =>
			{
				// Not on the front (no frontier edge) ⇒ never hold, however lopsided.
				Assert.That(FrontlineAllocationMath.SectorPostureHold(0, 50, frontierEdges: 0, holdRatioPct: 200, ownStrengthFloor: 3),
					Is.False, "a sector off the front is not a hold candidate");

				// No believed enemy force ⇒ never hold (nothing to hold against).
				Assert.That(FrontlineAllocationMath.SectorPostureHold(0, 0, 2, 200, 3), Is.False, "no enemy ⇒ press");

				// A genuinely outnumbered line we DO occupy still holds (own above the floor, enemy ≥ 2× own).
				Assert.That(FrontlineAllocationMath.SectorPostureHold(5, 20, 2, 200, 3), Is.True,
					"occupied + outnumbered ⇒ still hold");
			});
		}

		[Test]
		public void PostureHoldReleasesASectorWeDoNotOccupy()
		{
			// The live River Zeta freeze: an offensive axis's TARGET sector sits in the enemy rear where our
			// believed own strength is ~0, so "enemy ≥ own × ratio" was trivially true and every push held at home.
			// The own-strength floor is the backstop — below it, PRESS (you cannot hold a sector you don't occupy).
			Assert.Multiple(() =>
			{
				// own = 0 vs enemy present, floor 3 ⇒ the degeneracy no longer holds.
				Assert.That(FrontlineAllocationMath.SectorPostureHold(0, 4, 2, 200, 3), Is.False,
					"degeneracy fixed: own 0 (unoccupied) ⇒ press, not hold");

				// Just below the floor still presses, even against overwhelming believed force.
				Assert.That(FrontlineAllocationMath.SectorPostureHold(2, 100, 2, 200, 3), Is.False,
					"own below floor ⇒ press regardless of ratio");

				// At the floor we occupy the sector, so the ratio decides: floor-strength vs 100 enemy ⇒ hold.
				Assert.That(FrontlineAllocationMath.SectorPostureHold(3, 100, 2, 200, 3), Is.True,
					"own == floor + outnumbered ⇒ hold");

				// At the floor but NOT outnumbered ⇒ press (occupies the sector, enemy under 2×).
				Assert.That(FrontlineAllocationMath.SectorPostureHold(3, 5, 2, 200, 3), Is.False,
					"own == floor + enemy under 2× ⇒ press");
			});
		}

		[Test]
		public void PostureHoldPressesFromOwnHeldGround()
		{
			// Consequence of evaluating the axis's CONTACT sector (its unit centroid) instead of the deep target
			// sector: an axis standing on its own held ground reads high sectorOwn + low believed enemy ⇒ it presses
			// forward rather than freezing. (The old target-sector evaluation read the enemy rear here and froze.)
			Assert.That(FrontlineAllocationMath.SectorPostureHold(40, 10, 2, 200, 3), Is.False,
				"own-heavy contact sector ⇒ press");
		}

		[Test]
		public void PostureHoldFloorZeroPreservesLegacyMath()
		{
			// ownStrengthFloor <= 0 disables the floor ⇒ byte-identical to the pre-fix ratio-only behaviour,
			// including the degenerate own=0-vs-enemy hold. Documents the floor's off sentinel.
			Assert.Multiple(() =>
			{
				Assert.That(FrontlineAllocationMath.SectorPostureHold(0, 4, 2, 200, 0), Is.True,
					"floor 0 ⇒ legacy own-0-vs-enemy hold");
				Assert.That(FrontlineAllocationMath.SectorPostureHold(0, 4, 2, 200, -1), Is.True,
					"negative floor ⇒ also disabled (legacy hold)");
			});
		}

		[Test]
		public void PostureHoldIsInertWhenDisabled()
		{
			// holdRatioPct <= 0 is the disabled sentinel ⇒ always press, even into an overwhelming sector. The
			// ratio gate is checked before the floor, so a disabled hold presses whatever the floor.
			Assert.Multiple(() =>
			{
				Assert.That(FrontlineAllocationMath.SectorPostureHold(1, 100, 2, 0, 3), Is.False, "ratio 0 ⇒ disabled");
				Assert.That(FrontlineAllocationMath.SectorPostureHold(1, 100, 2, -5, 3), Is.False, "negative ratio ⇒ disabled");
			});
		}
	}
}
