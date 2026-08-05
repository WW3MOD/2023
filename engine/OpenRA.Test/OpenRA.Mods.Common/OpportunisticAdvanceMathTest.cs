#region Copyright & License Information
/*
 * WW3MOD OpportunisticAdvanceMath tests — @experimental opportunistic advance (PIPELINE item 31, design §2.6).
 *
 * Pure-logic pins for "when a sector ahead is undefended and a free path exists, advance": the four-condition
 * grant test, the three Aggressiveness-shifted dials, the reserve split, and the extend-while-clear walk down
 * the distance-to-enemy-frontier gradient. Validated without a World and portable verbatim into the SquadBrain's
 * Advance mission.
 *
 * These encode the invariants the behaviour rests on:
 *   * INERTNESS — a failing gate, a zero depth, or ground that is not granted all return the seed / 0, which is
 *     what makes the default-off consumer byte-identical;
 *   * the walk never enters BELIEVED-ENEMY ground, so it provably halts on our side of the frontline contour;
 *   * a covered or impassable lane is not a candidate at all (not merely deprioritised);
 *   * a closing corridor SHORTENS the walk — the §2.6 abort, with no abort code;
 *   * the knob spans its declared range and CLAMPS at both extremes rather than inverting;
 *   * termination is guaranteed by the strict frontier-distance decrease plus the step budget.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class OpportunisticAdvanceMathTest
	{
		// A frontier field whose distance-to-front equals grid X (believed enemy at x<=0, deep friendly rear at
		// high x), so a steepest descent walks WEST toward the front. Mirrors the ForwardStagingMath fixture.
		static int FrontierByX(int gx, int gy) => gx;
		static int NoDanger(int gx, int gy) => 0;
		static bool NoContact(int gx, int gy) => false;
		static bool NoEnemyGround(int gx, int gy) => false;
		static bool AllPassable(int gx, int gy) => true;
		static bool BigGrid(int gx, int gy) => gx >= -100 && gx <= 100 && gy >= -100 && gy <= 100;

		// ---------- The knob-shifted dials (§2.6/§2.7) ----------

		[Test]
		public void Dials_AtNeutralKnob_ReturnTheirBase()
		{
			// 50 is the tuned baseline by definition: every dial reads its own base, whatever the slope.
			Assert.That(OpportunisticAdvanceMath.MaxSectors(3, 50, 4), Is.EqualTo(3));
			Assert.That(OpportunisticAdvanceMath.DangerCeiling(20, 50, 40), Is.EqualTo(20));
			Assert.That(OpportunisticAdvanceMath.ForceCap(5, 50, 6), Is.EqualTo(5));
		}

		[Test]
		public void Dials_ZeroSlope_AreInertForEveryKnob()
		{
			// The Phase-1d inertness contract still holds per dial: slope 0 ⇒ the knob moves nothing.
			Assert.That(OpportunisticAdvanceMath.MaxSectors(3, 0, 0), Is.EqualTo(3));
			Assert.That(OpportunisticAdvanceMath.MaxSectors(3, 100, 0), Is.EqualTo(3));
			Assert.That(OpportunisticAdvanceMath.DangerCeiling(20, 0, 0), Is.EqualTo(20));
			Assert.That(OpportunisticAdvanceMath.ForceCap(5, 100, 0), Is.EqualTo(5));
		}

		[Test]
		public void Dials_SpanTheShippedRangeAcrossTheKnob()
		{
			// The shipped base/slope pairings, end to end. Cautious: a 2-unit screen, 1 sector deep, into
			// TOTALLY clear ground only (ceiling 0) — §2.6's stated low-aggressiveness behaviour.
			Assert.That(OpportunisticAdvanceMath.MaxSectors(3, 0, 4), Is.EqualTo(1));
			Assert.That(OpportunisticAdvanceMath.DangerCeiling(20, 0, 40), Is.EqualTo(0));
			Assert.That(OpportunisticAdvanceMath.ForceCap(5, 0, 6), Is.EqualTo(2));

			// Reckless: deeper, through more marginal danger, with a larger force.
			Assert.That(OpportunisticAdvanceMath.MaxSectors(3, 100, 4), Is.EqualTo(5));
			Assert.That(OpportunisticAdvanceMath.DangerCeiling(20, 100, 40), Is.EqualTo(40));
			Assert.That(OpportunisticAdvanceMath.ForceCap(5, 100, 6), Is.EqualTo(8));
		}

		[Test]
		public void Dials_FloorAtZero_NeverInvert()
		{
			// A steep slope would drive these negative at the cautious end. Floored, so "cautious" degenerates to
			// "no advance / only totally clear ground" instead of a ceiling no cell can satisfy or a negative depth.
			Assert.That(OpportunisticAdvanceMath.MaxSectors(3, 0, 400), Is.EqualTo(0));
			Assert.That(OpportunisticAdvanceMath.DangerCeiling(20, 0, 400), Is.EqualTo(0));
			Assert.That(OpportunisticAdvanceMath.ForceCap(5, 0, 400), Is.EqualTo(0));
		}

		[Test]
		public void Dials_OutOfDomainKnob_ClampsToTheExtreme()
		{
			// A sweep harness typo must land on a legible extreme, not past it (PoiOffenseMath.ClampKnob).
			Assert.That(OpportunisticAdvanceMath.MaxSectors(3, 500, 4), Is.EqualTo(OpportunisticAdvanceMath.MaxSectors(3, 100, 4)));
			Assert.That(OpportunisticAdvanceMath.DangerCeiling(20, -500, 40), Is.EqualTo(OpportunisticAdvanceMath.DangerCeiling(20, 0, 40)));
		}

		// ---------- SectorIsClear — the four §2.6 conditions ----------

		[Test]
		public void SectorIsClear_AllFourConditionsMet_Grants()
		{
			Assert.That(OpportunisticAdvanceMath.SectorIsClear(
				believedEnemyOwned: false, contactPresent: false, danger: 10, dangerCeiling: 20, passable: true),
				Is.True);
		}

		[Test]
		public void SectorIsClear_EachConditionIndependentlyVetoes()
		{
			// Conjunctive by construction: any single failure denies the grant.
			Assert.That(OpportunisticAdvanceMath.SectorIsClear(true, false, 10, 20, true), Is.False, "believed enemy ground");
			Assert.That(OpportunisticAdvanceMath.SectorIsClear(false, true, 10, 20, true), Is.False, "believed contact");
			Assert.That(OpportunisticAdvanceMath.SectorIsClear(false, false, 21, 20, true), Is.False, "danger over ceiling");
			Assert.That(OpportunisticAdvanceMath.SectorIsClear(false, false, 10, 20, false), Is.False, "impassable");
		}

		[Test]
		public void SectorIsClear_DangerAtTheCeiling_IsStillClear()
		{
			// At/under, not strictly under — so a ceiling of 0 admits verified-empty ground (danger exactly 0),
			// which is what makes the cautious extreme "advance only into totally clear ground" and not "never".
			Assert.That(OpportunisticAdvanceMath.SectorIsClear(false, false, 20, 20, true), Is.True);
			Assert.That(OpportunisticAdvanceMath.SectorIsClear(false, false, 0, 0, true), Is.True);
		}

		// ---------- AdvanceGroupSize — the reserve split ----------

		[Test]
		public void AdvanceGroupSize_SpendsTheCapBoundedByWhatIsIdle()
		{
			Assert.That(OpportunisticAdvanceMath.AdvanceGroupSize(idleCount: 10, minUnits: 2, cap: 5), Is.EqualTo(5));
			Assert.That(OpportunisticAdvanceMath.AdvanceGroupSize(idleCount: 3, minUnits: 2, cap: 5), Is.EqualTo(3));
		}

		[Test]
		public void AdvanceGroupSize_BelowTheMinimumScreen_DeclinesEntirely()
		{
			// A token force walking into no-man's-land is a donation — decline rather than send one unit.
			Assert.That(OpportunisticAdvanceMath.AdvanceGroupSize(idleCount: 1, minUnits: 2, cap: 5), Is.EqualTo(0));
			Assert.That(OpportunisticAdvanceMath.AdvanceGroupSize(idleCount: 0, minUnits: 2, cap: 5), Is.EqualTo(0));
		}

		[Test]
		public void AdvanceGroupSize_CapDrivenUnderTheMinimum_CancelsTheAdvance()
		{
			// The cautious end of the ForceCap slope can push the cap below the minimum screen. That reads as
			// "do not advance", never as an under-strength probe.
			Assert.That(OpportunisticAdvanceMath.AdvanceGroupSize(idleCount: 10, minUnits: 3, cap: 2), Is.EqualTo(0));
			Assert.That(OpportunisticAdvanceMath.AdvanceGroupSize(idleCount: 10, minUnits: 2, cap: 0), Is.EqualTo(0));
		}

		[Test]
		public void AdvanceGroupSize_NonPositiveMinimum_DeclinesRatherThanSendingEverything()
		{
			Assert.That(OpportunisticAdvanceMath.AdvanceGroupSize(idleCount: 10, minUnits: 0, cap: 5), Is.EqualTo(0));
		}

		// ---------- ShouldAdvance — the master gate ----------

		[Test]
		public void ShouldAdvance_AllTermsMet_Fires()
		{
			Assert.That(OpportunisticAdvanceMath.ShouldAdvance(true, true, 3, 4), Is.True);
		}

		[Test]
		public void ShouldAdvance_EachTermIndependentlyVetoes()
		{
			Assert.That(OpportunisticAdvanceMath.ShouldAdvance(false, true, 3, 4), Is.False, "gate off");
			Assert.That(OpportunisticAdvanceMath.ShouldAdvance(true, false, 3, 4), Is.False, "no fields");
			Assert.That(OpportunisticAdvanceMath.ShouldAdvance(true, true, 0, 4), Is.False, "zero depth");
			Assert.That(OpportunisticAdvanceMath.ShouldAdvance(true, true, 3, 0), Is.False, "no screen");
		}

		// ---------- AdvanceCell — the extend-while-clear walk ----------

		[Test]
		public void AdvanceCell_ClearGround_WalksTheFullBudgetTowardTheFront()
		{
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 3, dangerCeiling: 20,
				FrontierByX, NoDanger, NoEnemyGround, NoContact, AllPassable, BigGrid);

			// Three sectors of granted ground ⇒ three steps west. "Extends while clear" is the whole point.
			Assert.That(cell.X, Is.EqualTo(7));
			Assert.That(cell.Y, Is.EqualTo(0));
		}

		[Test]
		public void AdvanceCell_ZeroDepth_ReturnsTheSeed()
		{
			// The cautious extreme of the MaxSectors slope. Seed returned ⇒ caller reads "no advance" ⇒ inert.
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 0, dangerCeiling: 20,
				FrontierByX, NoDanger, NoEnemyGround, NoContact, AllPassable, BigGrid);

			Assert.That(cell, Is.EqualTo((10, 0)));
		}

		[Test]
		public void AdvanceCell_FlatUnpopulatedField_ReturnsTheSeed()
		{
			// Before the belief store has anything, every cell reads the same 'far' sentinel: no neighbour is
			// STRICTLY closer, so nothing is accepted. This is the byte-identical early-game case.
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 5, dangerCeiling: 20,
				(gx, gy) => 999, NoDanger, NoEnemyGround, NoContact, AllPassable, BigGrid);

			Assert.That(cell, Is.EqualTo((10, 0)));
		}

		[Test]
		public void AdvanceCell_StopsAtBelievedEnemyGround_NeverEntersIt()
		{
			// Enemy-classified from x <= 6. The budget would allow reaching x=4, but condition (1) rejects those
			// cells, so the walk halts on OUR side of the frontline contour — the "forward frontier cell" §2.6
			// names as the objective. This is the load-bearing safety property of the whole behaviour.
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 6, dangerCeiling: 20,
				FrontierByX, NoDanger, (gx, gy) => gx <= 6, NoContact, AllPassable, BigGrid);

			Assert.That(cell, Is.EqualTo((7, 0)));
		}

		[Test]
		public void AdvanceCell_StopsAtADangerEnvelope()
		{
			// A defended lane ahead (danger 50 from x <= 8) exceeds the ceiling ⇒ not a candidate.
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 6, dangerCeiling: 20,
				FrontierByX, (gx, gy) => gx <= 8 ? 50 : 0, NoEnemyGround, NoContact, AllPassable, BigGrid);

			Assert.That(cell, Is.EqualTo((9, 0)));
		}

		[Test]
		public void AdvanceCell_HigherCeilingPushesThroughTheSameGround()
		{
			// The aggressiveness story, held against the previous test's identical field: raising the ceiling past
			// the envelope's reading converts a stalled advance into a committed one, with no other change.
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 6, dangerCeiling: 60,
				FrontierByX, (gx, gy) => gx <= 8 ? 50 : 0, NoEnemyGround, NoContact, AllPassable, BigGrid);

			Assert.That(cell, Is.EqualTo((4, 0)));
		}

		[Test]
		public void AdvanceCell_StopsAtABelievedContact()
		{
			// Condition (2) is genuinely independent of (1) and (3): a lone cheap contact may neither flip control
			// nor stamp danger above the ceiling, yet the sector is not undefended.
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 6, dangerCeiling: 20,
				FrontierByX, NoDanger, NoEnemyGround, (gx, gy) => gx == 8 && gy == 0, AllPassable, BigGrid);

			// x=8 is denied outright; the diagonals at x=8 are still clear, so the walk continues past it off-axis
			// rather than stalling — the corridor bends around the contact instead of stopping dead.
			Assert.That(cell.X, Is.LessThan(9));
			Assert.That(cell, Is.Not.EqualTo((8, 0)));
		}

		[Test]
		public void AdvanceCell_ImpassableGround_IsNeverPreferred()
		{
			// Water/cliff reads danger 0 — maximally "safe" — so without the passability term the walk would
			// actively steer into it and the resulting order would no-op. Here everything west of x=8 is
			// impassable, so the walk must stop rather than march into the lake.
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 6, dangerCeiling: 20,
				FrontierByX, NoDanger, NoEnemyGround, NoContact, (gx, gy) => gx > 8, BigGrid);

			Assert.That(cell, Is.EqualTo((9, 0)));
		}

		[Test]
		public void AdvanceCell_ClosingCorridor_ShortensTheWalk_TheAbort()
		{
			// §2.6's abort, with no abort code: the same seed and budget, re-derived after a contact appears in
			// the corridor, simply grants less ground. The consumer's next order is therefore rearward.
			var open = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 4, dangerCeiling: 20,
				FrontierByX, NoDanger, NoEnemyGround, NoContact, AllPassable, BigGrid);

			var closed = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 4, dangerCeiling: 20,
				FrontierByX, (gx, gy) => gx <= 9 ? 90 : 0, NoEnemyGround, NoContact, AllPassable, BigGrid);

			Assert.That(open.X, Is.EqualTo(6));
			Assert.That(closed, Is.EqualTo((10, 0)), "corridor closed at the first step ⇒ seed ⇒ no advance");
		}

		[Test]
		public void AdvanceCell_NeverLeavesTheGrid()
		{
			// A tight grid whose western edge is at x=8: the walk must stop at the boundary rather than stepping
			// off the playable area (the same discipline the staging descent applies).
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 20, dangerCeiling: 20,
				FrontierByX, NoDanger, NoEnemyGround, NoContact, AllPassable,
				(gx, gy) => gx >= 8 && gx <= 20 && gy >= -5 && gy <= 5);

			Assert.That(cell.X, Is.GreaterThanOrEqualTo(8));
		}

		[Test]
		public void AdvanceCell_IsDeterministicUnderTiedNeighbours()
		{
			// Two neighbours equally closer to the front must break by the fixed scan order, identically every
			// call — the influence-stack determinism invariant (two clients advance to the same cell).
			(int X, int Y) Run() => OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 3, dangerCeiling: 20,
				(gx, gy) => gx + gy, NoDanger, NoEnemyGround, NoContact, AllPassable, BigGrid);

			var first = Run();
			Assert.That(Run(), Is.EqualTo(first));
			Assert.That(Run(), Is.EqualTo(first));
		}
	}
}
