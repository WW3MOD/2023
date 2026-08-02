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

		// ---------- Stage F: BalanceOfPowerFactor (terr-bias revival on the control field) ----------

		// GrayBand mirrors the shipped ControlFieldInfo.GrayBand so the axis tri-state matches the
		// field's own ControlFieldMath.Classify buckets exactly.
		const int GrayBand = 150;
		const int BopBoost = 150;
		const int BopDamp = 60;

		[Test]
		public void BalanceOfPower_WeBelieveWeHold_Presses()
		{
			// control score above +GrayBand ⇒ believed OURS ⇒ the enemy's grip here is weak ⇒ boost.
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(500, GrayBand, BopBoost, BopDamp), Is.EqualTo(BopBoost));
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(151, GrayBand, BopBoost, BopDamp), Is.EqualTo(BopBoost));
		}

		[Test]
		public void BalanceOfPower_WeBelieveEnemyHolds_Damps()
		{
			// control score below −GrayBand ⇒ believed ENEMY ⇒ lunging into strength ⇒ damp.
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(-500, GrayBand, BopBoost, BopDamp), Is.EqualTo(BopDamp));
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(-151, GrayBand, BopBoost, BopDamp), Is.EqualTo(BopDamp));
		}

		[Test]
		public void BalanceOfPower_ContestedFront_IsNeutral()
		{
			// |score| ≤ GrayBand ⇒ contested front ⇒ 100 (neutral) — no thrash on a knife-edge balance.
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(0, GrayBand, BopBoost, BopDamp), Is.EqualTo(100));
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(100, GrayBand, BopBoost, BopDamp), Is.EqualTo(100));
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(-100, GrayBand, BopBoost, BopDamp), Is.EqualTo(100));
		}

		[Test]
		public void BalanceOfPower_BandBoundaryIsContestedInclusive()
		{
			// Exactly ±GrayBand reads contested (neutral) — matches ControlFieldMath.Classify's
			// gray-inclusive boundary (ClassifyBoundariesAreGrayInclusive), so the axis bias and the
			// overlay/field classification never disagree at the edge.
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(GrayBand, GrayBand, BopBoost, BopDamp), Is.EqualTo(100));
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(-GrayBand, GrayBand, BopBoost, BopDamp), Is.EqualTo(100));
		}

		[Test]
		public void BalanceOfPower_DefaultMultipliersAreInert()
		{
			// A bare StrategicRepointEnabled (multipliers left at the code default 100) changes only the
			// threat SOURCE, never the ranking — every bucket returns 100.
			Assert.Multiple(() =>
			{
				Assert.That(PoiOffenseMath.BalanceOfPowerFactor(500, GrayBand, 100, 100), Is.EqualTo(100));
				Assert.That(PoiOffenseMath.BalanceOfPowerFactor(-500, GrayBand, 100, 100), Is.EqualTo(100));
				Assert.That(PoiOffenseMath.BalanceOfPowerFactor(0, GrayBand, 100, 100), Is.EqualTo(100));
			});
		}

		// ---------- Stage F: NeighborhoodControlScore (anchor-exclusion — the review MERGE-WITH-FIX) ----------

		// The shipped ControlField anchor footprint: AnchorRadiusCells = 4, so the module samples the ring
		// at radius 5 (AnchorRadiusCells + 1), one grid cell past the target's own anchor taper.
		const int RingRadius = 5;

		[Test]
		public void Neighborhood_ExcludesAnchorFlooredCentre_ReadsSurroundingTerritory()
		{
			// THE MOTIVATING CASE. The target's own cell is an enemy anchor floor (≈ −800 — a site-anchor
			// structure), but it is ENCIRCLED by ours-painted ground (+500 all around). Reading the target
			// cell directly would always damp (the shipped-before-fix defect); the ring read must ignore the
			// centre and see the surrounding +500 → boost. Every ring point sits ≥ radius from the centre, so
			// the −800 centre is never sampled.
			int Sampler(int x, int y) => x == 10 && y == 10 ? -800 : 500;
			var neighborhood = PoiOffenseMath.NeighborhoodControlScore(Sampler, 10, 10, RingRadius);
			Assert.That(neighborhood, Is.EqualTo(500), "centre excluded → reads the surrounding +500, not the anchor floor");
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(neighborhood, GrayBand, BopBoost, BopDamp),
				Is.EqualTo(BopBoost), "encircled enemy structure → press it (the boost the fix restores)");
		}

		[Test]
		public void Neighborhood_DeepEnemy_Damps()
		{
			// A target whose surrounding territory is uniformly believed-enemy (−600) → damp, even though
			// the centre anchor is not sampled. This is the correct "don't lunge into strength" behaviour.
			int Sampler(int x, int y) => -600;
			var neighborhood = PoiOffenseMath.NeighborhoodControlScore(Sampler, 10, 10, RingRadius);
			Assert.That(neighborhood, Is.EqualTo(-600));
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(neighborhood, GrayBand, BopBoost, BopDamp), Is.EqualTo(BopDamp));
		}

		[Test]
		public void Neighborhood_ContestedSurroundings_Neutral()
		{
			// Surrounding balance near zero (a genuine contested front, not an artefact of the centre anchor)
			// → neutral. Mixed +100/−100 ring averages inside the gray band.
			int Sampler(int x, int y) => (x + y) % 2 == 0 ? 100 : -100;
			var neighborhood = PoiOffenseMath.NeighborhoodControlScore(Sampler, 10, 10, RingRadius);
			Assert.That(PoiOffenseMath.BalanceOfPowerFactor(neighborhood, GrayBand, BopBoost, BopDamp), Is.EqualTo(100));
		}

		[Test]
		public void Neighborhood_SamplesEightRingPointsAtRadius_NeverTheCentre()
		{
			// Pin the ring geometry: exactly the 8 cardinal+diagonal points at ±radius are sampled, and the
			// centre (gx,gy) is provably never touched (sentinel would corrupt the average if it were).
			var sampled = new List<(int, int)>();
			int Sampler(int x, int y)
			{
				sampled.Add((x, y));
				Assert.That((x, y), Is.Not.EqualTo((10, 10)), "the centre cell must never be sampled");
				return 0;
			}

			PoiOffenseMath.NeighborhoodControlScore(Sampler, 10, 10, RingRadius);
			Assert.That(sampled, Has.Count.EqualTo(8), "8 fixed directions");
			Assert.That(sampled, Does.Contain((15, 10)).And.Contain((5, 10))
				.And.Contain((10, 15)).And.Contain((10, 5)), "cardinals at ±radius");
			Assert.That(sampled, Does.Contain((15, 15)).And.Contain((5, 5)), "diagonals at ±radius");
		}

		// ---------- Stage F: BelievedDangerFactor (fog-legal threat, replaces the omniscient grid) ----------

		const int DangerMild = 40;
		const int DangerHostile = 120;
		const int DangerSafeMul = 100;
		const int DangerMildMul = 60;
		const int DangerHostileMul = 20;

		[Test]
		public void BelievedDanger_SafeGroundIsNeutral()
		{
			// At/below the mild threshold (verified-safe ground or the low Stage-C baseline) ⇒ safe ⇒
			// not damped. This is why the threshold sits ABOVE the territory baseline intensity.
			Assert.That(PoiOffenseMath.BelievedDangerFactor(0, DangerMild, DangerHostile,
				DangerSafeMul, DangerMildMul, DangerHostileMul), Is.EqualTo(DangerSafeMul));
			Assert.That(PoiOffenseMath.BelievedDangerFactor(DangerMild, DangerMild, DangerHostile,
				DangerSafeMul, DangerMildMul, DangerHostileMul), Is.EqualTo(DangerSafeMul), "== mild boundary is safe-inclusive");
		}

		[Test]
		public void BelievedDanger_ProbedGroundDamps()
		{
			// Between the mild and hostile thresholds ⇒ mild (probed approach) ⇒ mild damp.
			Assert.That(PoiOffenseMath.BelievedDangerFactor(41, DangerMild, DangerHostile,
				DangerSafeMul, DangerMildMul, DangerHostileMul), Is.EqualTo(DangerMildMul));
			Assert.That(PoiOffenseMath.BelievedDangerFactor(DangerHostile, DangerMild, DangerHostile,
				DangerSafeMul, DangerMildMul, DangerHostileMul), Is.EqualTo(DangerMildMul), "== hostile boundary is still mild");
		}

		[Test]
		public void BelievedDanger_InsideEnvelopeIsHostile()
		{
			// Above the hostile threshold (a dense believed weapon envelope) ⇒ strong damp — the
			// fog-legal analogue of the old omniscient hostile-threat gate.
			Assert.That(PoiOffenseMath.BelievedDangerFactor(121, DangerMild, DangerHostile,
				DangerSafeMul, DangerMildMul, DangerHostileMul), Is.EqualTo(DangerHostileMul));
			Assert.That(PoiOffenseMath.BelievedDangerFactor(10000, DangerMild, DangerHostile,
				DangerSafeMul, DangerMildMul, DangerHostileMul), Is.EqualTo(DangerHostileMul));
		}

		[Test]
		public void BelievedDanger_DefaultMultipliersAreInert()
		{
			// Multipliers left at the code default 100 ⇒ inert in every bucket (bare-enable no-op).
			Assert.Multiple(() =>
			{
				Assert.That(PoiOffenseMath.BelievedDangerFactor(0, DangerMild, DangerHostile, 100, 100, 100), Is.EqualTo(100));
				Assert.That(PoiOffenseMath.BelievedDangerFactor(80, DangerMild, DangerHostile, 100, 100, 100), Is.EqualTo(100));
				Assert.That(PoiOffenseMath.BelievedDangerFactor(500, DangerMild, DangerHostile, 100, 100, 100), Is.EqualTo(100));
			});
		}

		[Test]
		public void Repoint_CombinedFactorStacksBalanceAndDanger()
		{
			// The module multiplies the two factors (÷100): a target we believe we hold (boost x150) but
			// which sits inside a believed weapon envelope (hostile x20) nets 150*20/100 = 30 — pressable
			// ground is still declined when a kill-zone covers it. This is the exact combine the rescale does.
			var bop = PoiOffenseMath.BalanceOfPowerFactor(500, GrayBand, BopBoost, BopDamp);
			var danger = PoiOffenseMath.BelievedDangerFactor(200, DangerMild, DangerHostile,
				DangerSafeMul, DangerMildMul, DangerHostileMul);
			Assert.That(bop * danger / 100, Is.EqualTo(30));

			// A believed-ours cell in safe ground nets the full boost (150*100/100 = 150).
			var safe = PoiOffenseMath.BelievedDangerFactor(0, DangerMild, DangerHostile,
				DangerSafeMul, DangerMildMul, DangerHostileMul);
			Assert.That(bop * safe / 100, Is.EqualTo(BopBoost));
		}

		// ---------- ShiftByKnob (Phase 1d slider seam) ----------

		[Test]
		public void ShiftByKnob_NeutralKnob_IsNoOp()
		{
			// knob 50 = neutral: the base is returned unchanged for ANY slope.
			Assert.That(PoiOffenseMath.ShiftByKnob(50, 50, 100), Is.EqualTo(50));
			Assert.That(PoiOffenseMath.ShiftByKnob(50, 50, 0), Is.EqualTo(50));
		}

		[Test]
		public void ShiftByKnob_ZeroSlope_IsInertForEveryKnob()
		{
			// slope 0 = the frozen default: the knob cannot move the base regardless of its value.
			Assert.That(PoiOffenseMath.ShiftByKnob(50, 0, 0), Is.EqualTo(50));
			Assert.That(PoiOffenseMath.ShiftByKnob(50, 100, 0), Is.EqualTo(50));
		}

		[Test]
		public void ShiftByKnob_ShiftsLinearlyAroundNeutral()
		{
			// effective = base + (knob-50)*slope/100. slope 40: knob 100 => +20, knob 0 => -20.
			Assert.That(PoiOffenseMath.ShiftByKnob(50, 100, 40), Is.EqualTo(70));
			Assert.That(PoiOffenseMath.ShiftByKnob(50, 0, 40), Is.EqualTo(30));
			// Integer truncation toward zero: (65-50)*40/100 = 600/100 = 6.
			Assert.That(PoiOffenseMath.ShiftByKnob(50, 65, 40), Is.EqualTo(56));
		}
	}
}
