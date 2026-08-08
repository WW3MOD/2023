#region Copyright & License Information
/*
 * WW3MOD ForwardStagingMath tests — @experimental free-pool forward staging (Phase 2).
 *
 * Pure-logic pins for the reserve muster-point math: the free pool is walked to a safe standoff BEHIND the
 * believed frontier (steepest descent on the control field's distance-to-enemy-frontier BFS) and fanned out
 * over several cells, and the anchor advances with the front under a hysteresis guard. Like the other bot math
 * classes this is validated without a World and ports verbatim into a future v3 brain.
 *
 * These encode the staging invariants:
 *   * a point already behind the standoff (or a FLAT/unpopulated field) takes zero steps ⇒ reserve idles at the
 *     SR, byte-identical to the legacy path;
 *   * the descent walks toward the nearest front and stops at the standoff;
 *   * it NEVER descends into a believed danger envelope (stays behind defended fronts);
 *   * the spread fans consecutive unit indices over distinct cells (anti-clog);
 *   * anchor hysteresis suppresses jitter re-lays.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ForwardStagingMathTest
	{
		// A frontier field whose distance-to-front equals the grid X (front at x=0, deep rear at high x), so a
		// steepest descent walks WEST toward the front. No danger anywhere unless a test overrides it.
		static int FrontierByX(int gx, int gy) => gx;
		static int NoDanger(int gx, int gy) => 0;
		static bool BigGrid(int gx, int gy) => gx >= -100 && gx <= 100 && gy >= -100 && gy <= 100;

		// ---------- StagingCell ----------

		[Test]
		public void StagingCell_AlreadyBehindStandoff_TakesNoStep()
		{
			// The SR already sits at/under the standoff (front is on top of us) ⇒ no forward walk.
			var cell = ForwardStagingMath.StagingCell(3, 0, standoffCells: 5, dangerSafeThreshold: 40, maxSteps: 20,
				FrontierByX, NoDanger, BigGrid);
			Assert.That(cell, Is.EqualTo((3, 0)), "start already inside the standoff is not walked");
		}

		[Test]
		public void StagingCell_FlatField_ReturnsStart()
		{
			// An unpopulated field reads the same 'far' sentinel everywhere ⇒ no improving neighbour ⇒ inert
			// (reserve idles at the SR, byte-identical). This is the load-bearing "off until populated" property.
			var cell = ForwardStagingMath.StagingCell(10, 10, standoffCells: 3, dangerSafeThreshold: 40, maxSteps: 20,
				(gx, gy) => 64, NoDanger, BigGrid);
			Assert.That(cell, Is.EqualTo((10, 10)), "a flat (unpopulated) field yields no staging descent");
		}

		[Test]
		public void StagingCell_DescendsToTheStandoffBehindTheFront()
		{
			// From deep rear (x=10) walk west until frontier distance drops to the standoff of 3 ⇒ stop at x=3.
			var cell = ForwardStagingMath.StagingCell(10, 0, standoffCells: 3, dangerSafeThreshold: 40, maxSteps: 20,
				FrontierByX, NoDanger, BigGrid);
			Assert.That(cell, Is.EqualTo((3, 0)), "descends to exactly the standoff distance behind the front");
		}

		[Test]
		public void StagingCell_DangerGuardHoldsItBehindTheDefendedLine()
		{
			// Danger is hot (100) for every cell closer than x=5. Standoff is 1, but the walk must NOT step into
			// the envelope — it holds at x=5, BEHIND the defended line, even though the standoff isn't reached.
			var cell = ForwardStagingMath.StagingCell(10, 0, standoffCells: 1, dangerSafeThreshold: 40, maxSteps: 20,
				FrontierByX, (gx, gy) => gx < 5 ? 100 : 0, BigGrid);
			Assert.That(cell, Is.EqualTo((5, 0)), "the danger guard holds the muster point behind the envelope");
		}

		[Test]
		public void StagingCell_NegativeThresholdDisablesTheDangerGuard()
		{
			// A negative threshold means "no danger guard": the same hot field is ignored and the walk reaches the
			// standoff. Proves the guard is what held it above, not the gradient.
			var cell = ForwardStagingMath.StagingCell(10, 0, standoffCells: 1, dangerSafeThreshold: -1, maxSteps: 20,
				FrontierByX, (gx, gy) => gx < 5 ? 100 : 0, BigGrid);
			Assert.That(cell, Is.EqualTo((1, 0)), "a negative threshold ignores danger and reaches the standoff");
		}

		[Test]
		public void StagingCell_BudgetBounded()
		{
			// The standoff is never reached within the budget ⇒ the walk advances exactly maxSteps west, no more.
			var cell = ForwardStagingMath.StagingCell(10, 0, standoffCells: 0 + 1, dangerSafeThreshold: 40, maxSteps: 3,
				FrontierByX, NoDanger, BigGrid);
			Assert.That(cell, Is.EqualTo((7, 0)), "the descent is bounded by the step budget (10 - 3 = 7)");
		}

		[Test]
		public void StagingCell_Disabled_ReturnsStart()
		{
			Assert.Multiple(() =>
			{
				Assert.That(ForwardStagingMath.StagingCell(10, 0, standoffCells: 0, dangerSafeThreshold: 40, maxSteps: 20,
					FrontierByX, NoDanger, BigGrid), Is.EqualTo((10, 0)), "standoff <= 0 is off");
				Assert.That(ForwardStagingMath.StagingCell(10, 0, standoffCells: 3, dangerSafeThreshold: 40, maxSteps: 0,
					FrontierByX, NoDanger, BigGrid), Is.EqualTo((10, 0)), "a zero budget takes no step");
			});
		}

		[Test]
		public void StagingCell_HaltsAtTheGridBoundary()
		{
			// Front never reached and only cells x in [4,10] are on-grid: the westward walk stops at the boundary,
			// never returning an off-grid cell.
			var cell = ForwardStagingMath.StagingCell(10, 0, standoffCells: 0 + 1, dangerSafeThreshold: 40, maxSteps: 20,
				FrontierByX, NoDanger, (gx, gy) => gx >= 4 && gx <= 10 && gy == 0);
			Assert.That(cell, Is.EqualTo((4, 0)), "halts at the last on-grid cell, never off-grid");
		}

		// ---------- SpreadCell ----------

		[Test]
		public void SpreadCell_IndexZeroIsTheAnchor()
		{
			Assert.That(ForwardStagingMath.SpreadCell(20, 20, index: 0, ringStep: 2, onGrid: (x, y) => true),
				Is.EqualTo((20, 20)));
		}

		[Test]
		public void SpreadCell_FirstRingFansOverEightDistinctCells()
		{
			var seen = new System.Collections.Generic.HashSet<(int, int)>();
			for (var i = 1; i <= 8; i++)
				seen.Add(ForwardStagingMath.SpreadCell(20, 20, i, ringStep: 2, onGrid: (x, y) => true));

			Assert.That(seen.Count, Is.EqualTo(8), "the first eight units fan out over eight distinct cells");
			Assert.That(seen, Does.Not.Contain((20, 20)), "no ring-1 unit piles on the anchor");
		}

		[Test]
		public void SpreadCell_RollsToTheSecondRing()
		{
			// Index 9 starts ring 2 (first octant), 2 * ringStep out.
			Assert.That(ForwardStagingMath.SpreadCell(20, 20, index: 9, ringStep: 2, onGrid: (x, y) => true),
				Is.EqualTo((20, 20 - 2 * 2)), "the ninth unit rolls onto the second ring");
		}

		[Test]
		public void SpreadCell_OffGridFallsBackToTheAnchor()
		{
			Assert.That(ForwardStagingMath.SpreadCell(0, 0, index: 4, ringStep: 2, onGrid: (x, y) => x >= 0 && y >= 0),
				Is.EqualTo((0, 0)), "an off-grid spread cell falls back to the anchor");
		}

		// ---------- StableSlot (NIT-1: no composition churn) ----------

		[Test]
		public void StableSlot_DependsOnlyOnOwnId_NoChurn()
		{
			// A unit's slot is a function of its OWN id + maxRings only — it does NOT depend on the pool contents,
			// so removing any OTHER unit cannot change it. This is the anti-churn guarantee.
			const int MaxRings = 3;
			var slotA = ForwardStagingMath.StableSlot(actorId: 40u, MaxRings);
			var slotB = ForwardStagingMath.StableSlot(actorId: 57u, MaxRings);

			// Re-derive after "unit 40 left the pool": 57's slot is unchanged (it never referenced 40).
			Assert.That(ForwardStagingMath.StableSlot(57u, MaxRings), Is.EqualTo(slotB),
				"a unit's slot is stable across pool-composition changes");
			Assert.That(slotA, Is.Not.EqualTo(slotB), "distinct ids here fall on distinct slots");
		}

		[Test]
		public void StableSlot_BoundedToMaxRings()
		{
			// Every slot stays within [0, maxRings*RingOctants], so SpreadCell's ring never exceeds maxRings and
			// the fan-out radius stays inside the standoff (NIT-2 invariant).
			const int MaxRings = 3;
			var max = MaxRings * ForwardStagingMath.RingOctants;
			for (var id = 0u; id < 200u; id++)
			{
				var slot = ForwardStagingMath.StableSlot(id, MaxRings);
				Assert.That(slot, Is.InRange(0, max), $"id {id} slot within the ring bound");
			}
		}

		[Test]
		public void StableSlot_ZeroRingsIsAnchorOnly()
		{
			Assert.That(ForwardStagingMath.StableSlot(12345u, maxRings: 0), Is.EqualTo(0),
				"maxRings <= 0 ⇒ everyone on the anchor (slot 0)");
		}

		// ---------- AnchorShifted ----------

		[Test]
		public void AnchorShifted_ThresholdHysteresis()
		{
			Assert.Multiple(() =>
			{
				Assert.That(ForwardStagingMath.AnchorShifted(10, 10, 12, 10, thresholdCells: 3), Is.False,
					"a 2-cell drift is below the 3-cell threshold ⇒ keep the old anchor");
				Assert.That(ForwardStagingMath.AnchorShifted(10, 10, 13, 10, thresholdCells: 3), Is.True,
					"a 3-cell drift meets the threshold ⇒ re-adopt");
				Assert.That(ForwardStagingMath.AnchorShifted(10, 10, 11, 10, thresholdCells: 0), Is.True,
					"a non-positive threshold always re-adopts (no hysteresis)");
			});
		}

		// ---------- MaxSpreadRings ----------

		// The property under test is SpreadCell's own stated precondition: the widest ring a spread can produce
		// must sit STRICTLY inside the standoff, because ring cells are NOT danger-guarded individually and the
		// anchor descent only cleared ground up to the standoff. A ring exactly ON the standoff would place a
		// unit on the frontier the descent deliberately stopped short of.
		static void AssertRingsStayInsideStandoff(int standoffMapCells, int ringStep)
		{
			var rings = ForwardStagingMath.MaxSpreadRings(standoffMapCells, ringStep);
			Assert.That(rings * ringStep, Is.LessThan(standoffMapCells),
				$"widest ring radius must stay strictly inside the standoff (standoff={standoffMapCells}, step={ringStep})");
		}

		[Test]
		public void MaxSpreadRings_ShippedReserveConfig_StaysInsideStandoff()
		{
			// The shipped capturer reserve: ReserveStandoffCells 10 x ControlField CellSize 2 = 20 map cells, with
			// ReserveSpreadStepCells 2 ⇒ 9 rings ⇒ widest radius 18 < 20. Dropping the -1 that makes the bound
			// strict yields 10 rings ⇒ radius 20, exactly ON the standoff, and both assertions below fail.
			Assert.That(ForwardStagingMath.MaxSpreadRings(20, 2), Is.EqualTo(9));
			AssertRingsStayInsideStandoff(20, 2);
		}

		[Test]
		public void MaxSpreadRings_HoldsAcrossStandoffAndStepRange()
		{
			Assert.Multiple(() =>
			{
				for (var standoff = 1; standoff <= 64; standoff++)
					for (var step = 1; step <= 8; step++)
						AssertRingsStayInsideStandoff(standoff, step);
			});
		}

		[Test]
		public void MaxSpreadRings_DegenerateInputs()
		{
			Assert.Multiple(() =>
			{
				Assert.That(ForwardStagingMath.MaxSpreadRings(20, 0), Is.EqualTo(0),
					"a non-positive step means no fan-out — everyone musters on the anchor cell");
				Assert.That(ForwardStagingMath.MaxSpreadRings(20, -3), Is.EqualTo(0),
					"a negative step must not produce a negative ring count");
				Assert.That(ForwardStagingMath.MaxSpreadRings(1, 2), Is.EqualTo(0),
					"a step wider than the standoff leaves no room for any ring");
				Assert.That(ForwardStagingMath.MaxSpreadRings(0, 2), Is.EqualTo(0),
					"a zero standoff admits no ring at all");
			});
		}
	}
}
