#region Copyright & License Information
/*
 * WW3MOD PoiOffenseMath tests — POI-strategy Phase 3.
 *
 * Pure-logic tests of the offense allocator that turns a scored POI list into
 * attack axes: how many axes to open, how to split the army across them by
 * score with a minimum viable size, and the hysteresis test that keeps axes
 * sticky. Exactly like PoiScoring / GoalGuardLedger, the decision math is a pure
 * static class (PoiOffenseMath) validated here without a World — it ports verbatim
 * into a future v3 brain.
 *
 * These encode the Phase 3 invariants from the plan (§5.5, decision #3):
 *   * army splits across MULTIPLE scored axes, not one death-ball;
 *   * each axis gets a minimum viable size (no single-unit dribbles);
 *   * higher-scored axes get proportionally more units;
 *   * an existing axis is only abandoned when clearly outscored (no thrash).
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class PoiOffenseTest
	{
		// ---------- DesiredAxisCount ----------

		[Test]
		public void DesiredAxisCount_ScalesOneAxisPerUnitsPerAxis()
		{
			// 24 units, 8 per axis, plenty of POIs → 3 axes.
			Assert.That(PoiOffenseMath.DesiredAxisCount(24, 5, 8, 3, 4), Is.EqualTo(3));
			// 8 units → 1 axis.
			Assert.That(PoiOffenseMath.DesiredAxisCount(8, 5, 8, 3, 4), Is.EqualTo(1));
		}

		[Test]
		public void DesiredAxisCount_CappedByMaxAxes()
		{
			// 80 units would want 10 axes, capped at 4.
			Assert.That(PoiOffenseMath.DesiredAxisCount(80, 9, 8, 3, 4), Is.EqualTo(4));
		}

		[Test]
		public void DesiredAxisCount_CappedByPoiCount()
		{
			// 40 units want 5 axes but only 2 POIs exist → 2 axes.
			Assert.That(PoiOffenseMath.DesiredAxisCount(40, 2, 8, 3, 4), Is.EqualTo(2));
		}

		[Test]
		public void DesiredAxisCount_CappedByFundabilityAtMinSize()
		{
			// 7 units, 8-per-axis wants 1; but 3 POIs and min size 3 → at most 2 fundable.
			// byPool = max(1, 7/8) = 1, so it's 1 here — check a case where fundability bites:
			// 10 units, unitsPerAxis 2 → byPool 5, capped maxAxes 4, poiCount 4, fundable 10/3=3 → 3.
			Assert.That(PoiOffenseMath.DesiredAxisCount(10, 4, 2, 3, 4), Is.EqualTo(3));
		}

		[Test]
		public void DesiredAxisCount_ZeroWhenNoTargetsOrTooFewUnits()
		{
			Assert.That(PoiOffenseMath.DesiredAxisCount(20, 0, 8, 3, 4), Is.EqualTo(0), "no POIs → no axes");
			Assert.That(PoiOffenseMath.DesiredAxisCount(2, 5, 8, 3, 4), Is.EqualTo(0), "fewer than min size → no axis");
		}

		// ---------- AllocateProportional ----------

		[Test]
		public void AllocateProportional_SumsToTotalUnits()
		{
			var alloc = PoiOffenseMath.AllocateProportional(new long[] { 900, 300, 100 }, 20, 3);
			Assert.That(alloc.Sum(), Is.EqualTo(20));
		}

		[Test]
		public void AllocateProportional_EachFundedAxisMeetsMinSize()
		{
			var alloc = PoiOffenseMath.AllocateProportional(new long[] { 500, 400, 300 }, 12, 3);
			foreach (var a in alloc)
				Assert.That(a, Is.GreaterThanOrEqualTo(3));
		}

		[Test]
		public void AllocateProportional_HigherScoreGetsMoreUnits()
		{
			var alloc = PoiOffenseMath.AllocateProportional(new long[] { 900, 100 }, 20, 3);
			Assert.That(alloc[0], Is.GreaterThan(alloc[1]), "the higher-scored axis takes the bigger share");
			Assert.That(alloc.Sum(), Is.EqualTo(20));
		}

		[Test]
		public void AllocateProportional_DropsTailWhenCannotFundAllAtMinSize()
		{
			// 3 axes, min size 3 needs 9 units, but only 7 available → drop the weakest tail.
			var alloc = PoiOffenseMath.AllocateProportional(new long[] { 900, 500, 100 }, 7, 3);
			Assert.That(alloc.Sum(), Is.EqualTo(7));
			Assert.That(alloc[2], Is.EqualTo(0), "the lowest-scored axis is dropped when unfundable");
			Assert.That(alloc[0], Is.GreaterThanOrEqualTo(3));
			Assert.That(alloc[1], Is.GreaterThanOrEqualTo(3));
		}

		[Test]
		public void AllocateProportional_SingleAxisTakesAll()
		{
			var alloc = PoiOffenseMath.AllocateProportional(new long[] { 500 }, 15, 3);
			Assert.That(alloc, Is.EqualTo(new[] { 15 }));
		}

		[Test]
		public void AllocateProportional_DeterministicForEqualScores()
		{
			var a = PoiOffenseMath.AllocateProportional(new long[] { 400, 400, 400 }, 11, 3);
			var b = PoiOffenseMath.AllocateProportional(new long[] { 400, 400, 400 }, 11, 3);
			Assert.That(a, Is.EqualTo(b), "same inputs → same split (no RNG)");
			Assert.That(a.Sum(), Is.EqualTo(11));
			// 11 across 3 equal axes: 3 each = 9, remainder 2 to the lowest indices.
			Assert.That(a[0], Is.EqualTo(4));
			Assert.That(a[1], Is.EqualTo(4));
			Assert.That(a[2], Is.EqualTo(3));
		}

		[Test]
		public void AllocateProportional_EmptyOrZeroUnits()
		{
			Assert.That(PoiOffenseMath.AllocateProportional(new long[0], 10, 3), Is.Empty);
			Assert.That(PoiOffenseMath.AllocateProportional(new long[] { 500 }, 0, 3), Is.EqualTo(new[] { 0 }));
		}

		// ---------- ScoreBeatsByThreshold (hysteresis) ----------

		[Test]
		public void ScoreBeatsByThreshold_RequiresStrictMarginOverThreshold()
		{
			// threshold 30%: candidate must exceed current * 1.30.
			Assert.That(PoiOffenseMath.ScoreBeatsByThreshold(131, 100, 30), Is.True, "31% > 30% → swap");
			Assert.That(PoiOffenseMath.ScoreBeatsByThreshold(130, 100, 30), Is.False, "exactly 30% → sticky");
			Assert.That(PoiOffenseMath.ScoreBeatsByThreshold(120, 100, 30), Is.False, "20% < 30% → sticky");
		}

		[Test]
		public void ScoreBeatsByThreshold_ZeroThresholdIsPlainGreaterThan()
		{
			Assert.That(PoiOffenseMath.ScoreBeatsByThreshold(101, 100, 0), Is.True);
			Assert.That(PoiOffenseMath.ScoreBeatsByThreshold(100, 100, 0), Is.False);
		}

		// ---------- Integration: the "spread not death-ball" invariant ----------

		[Test]
		public void Spread_ArmySplitsAcrossMultipleAxes_NotOneClump()
		{
			// 20 units, 3 enemy POIs with distinct scores → 2 axes (20/8=2), and the
			// pool is genuinely split (neither axis holds the whole army).
			var poiCount = 3;
			var k = PoiOffenseMath.DesiredAxisCount(20, poiCount, 8, 3, 4);
			Assert.That(k, Is.EqualTo(2));

			var topScores = new long[] { 9000, 6000 }; // the top-k axis scores
			var alloc = PoiOffenseMath.AllocateProportional(topScores, 20, 3);
			Assert.That(alloc.Sum(), Is.EqualTo(20));
			Assert.That(alloc.Count(x => x >= 3), Is.EqualTo(2), "both axes are viable");
			Assert.That(alloc.Max(), Is.LessThan(20), "no single axis swallows the whole army");
		}

		// ---------- Dispersion geometry (spread-to-move / mass-to-assault gate) ----------

		[Test]
		public void Chebyshev_IsChessboardDistanceNotEuclidean()
		{
			// The diagonal case is where Chebyshev (max of the axes) diverges from Euclidean:
			// (0,0)->(3,4) is Chebyshev 4, not the Euclidean 5.
			Assert.That(PoiOffenseMath.Chebyshev(0, 0, 3, 4), Is.EqualTo(4));
			Assert.That(PoiOffenseMath.Chebyshev(0, 0, 0, 0), Is.EqualTo(0));
			Assert.That(PoiOffenseMath.Chebyshev(5, 5, 2, 9), Is.EqualTo(4), "max(|dx|=3,|dy|=4)");
			Assert.That(PoiOffenseMath.Chebyshev(-2, -3, 4, 1), Is.EqualTo(6), "handles negatives");
		}

		[Test]
		public void CellCentroid_IsFloorDivisionAverage()
		{
			var square = new List<(int X, int Y)> { (0, 0), (4, 0), (0, 4), (4, 4) };
			Assert.That(PoiOffenseMath.CellCentroid(square), Is.EqualTo((2, 2)));

			// 3 cells summing X=7 → floor(7/3)=2 (integer division, not rounding).
			var trio = new List<(int X, int Y)> { (1, 1), (3, 2), (3, 3) };
			Assert.That(PoiOffenseMath.CellCentroid(trio), Is.EqualTo((2, 2)));

			Assert.That(PoiOffenseMath.CellCentroid(new List<(int X, int Y)>()), Is.EqualTo((0, 0)), "empty → origin");
		}

		[Test]
		public void MaxChebyshev_IsTheClumpRadius()
		{
			var centroid = PoiOffenseMath.CellCentroid(
				new List<(int X, int Y)> { (0, 0), (4, 0), (0, 4), (4, 4) });

			// Spread cluster: every corner is Chebyshev 2 from the (2,2) centroid.
			var spread = new List<(int X, int Y)> { (0, 0), (4, 0), (0, 4), (4, 4) };
			Assert.That(PoiOffenseMath.MaxChebyshev(spread, centroid.X, centroid.Y), Is.EqualTo(2));

			// Tight cluster around the same centroid → smaller clump radius (mass to assault).
			var tight = new List<(int X, int Y)> { (2, 2), (3, 2), (2, 3), (1, 2) };
			Assert.That(PoiOffenseMath.MaxChebyshev(tight, centroid.X, centroid.Y), Is.EqualTo(1));

			Assert.That(PoiOffenseMath.MaxChebyshev(new List<(int X, int Y)>(), 0, 0), Is.EqualTo(0), "empty → 0");
		}

		[Test]
		public void AssaultGate_FarCentroidSpreads_NearCentroidMasses()
		{
			// Mirrors the CommitAndOrder gate: dist > AssaultRadiusCells ⇒ approach (spread),
			// else assault (mass). Uses AssaultRadiusCells = 15 (the shipped default).
			const int assaultRadius = 15;
			var target = (X: 50, Y: 50);

			// Axis massed 30 cells out on X → Chebyshev 30 > 15 → en-route.
			var farCentroid = PoiOffenseMath.CellCentroid(
				new List<(int X, int Y)> { (20, 50), (20, 51), (20, 49) });
			var farDist = PoiOffenseMath.Chebyshev(farCentroid.X, farCentroid.Y, target.X, target.Y);
			Assert.That(farDist, Is.GreaterThan(assaultRadius), "far axis is en route → Spread");

			// Axis sitting on the objective → Chebyshev <= 15 → assault.
			var nearCentroid = PoiOffenseMath.CellCentroid(
				new List<(int X, int Y)> { (48, 50), (49, 50), (50, 51) });
			var nearDist = PoiOffenseMath.Chebyshev(nearCentroid.X, nearCentroid.Y, target.X, target.Y);
			Assert.That(nearDist, Is.LessThanOrEqualTo(assaultRadius), "near axis is at the objective → Tight");
		}

		// ---------- BalanceOfPowerFactor (territorial balance-of-power bias) ----------
		// Shipped @experimental tuning: weak 40, dominant 60, boost 150, damp 60.

		[Test]
		public void BalanceOfPowerFactor_NoEnemyInfluence_IsNeutral()
		{
			// e<=0 → not a contact cell → 100 regardless of friendly presence (the frontline guard:
			// empty ground is never boosted, so "push where enemy is weakest" can't degenerate into
			// an economy grab on empty ground).
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(50, 0, 40, 60, 150, 60), Is.EqualTo(100), "friendly-only ground → neutral");
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(0, 0, 40, 60, 150, 60), Is.EqualTo(100), "empty ground → neutral");
		}

		[Test]
		public void BalanceOfPowerFactor_WeDominateContact_Boosts()
		{
			// share = 90*100/(90+10) = 90 >= 60 → boost.
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(90, 10, 40, 60, 150, 60), Is.EqualTo(150));
		}

		[Test]
		public void BalanceOfPowerFactor_EnemyDominatesContact_Damps()
		{
			// share = 20*100/(20+80) = 20 <= 40 → damp (don't lunge into strength).
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(20, 80, 40, 60, 150, 60), Is.EqualTo(60));
		}

		[Test]
		public void BalanceOfPowerFactor_EvenFront_IsNeutral()
		{
			// f==e → share 50, strictly between weak (40) and dominant (60) → unchanged.
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(50, 50, 40, 60, 150, 60), Is.EqualTo(100));
		}

		[Test]
		public void BalanceOfPowerFactor_BoundaryInclusive()
		{
			// Exactly == dominant → boost; exactly == weak → damp (both thresholds inclusive).
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(60, 40, 40, 60, 150, 60), Is.EqualTo(150), "share 60 == dominant → boost");
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(40, 60, 40, 60, 150, 60), Is.EqualTo(60), "share 40 == weak → damp");
		}

		[Test]
		public void BalanceOfPowerFactor_ZeroFriendlyWithEnemy_Damps()
		{
			// f=0, e>0 → share 0 <= weak → damp (deep in enemy-dominated ground, no friendly presence).
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(0, 100, 40, 60, 150, 60), Is.EqualTo(60));
		}

		[Test]
		public void BalanceOfPowerFactor_InertMultipliersAreFrozen()
		{
			// The default-off sub-multipliers (boost=damp=100) leave every case at 100 — the
			// belt-and-suspenders guard so even a stray switch-flip can't move a score.
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(90, 10, 40, 60, 100, 100), Is.EqualTo(100), "dominant with inert boost → unchanged");
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(10, 90, 40, 60, 100, 100), Is.EqualTo(100), "enemy-dominant with inert damp → unchanged");
		}
	}
}
