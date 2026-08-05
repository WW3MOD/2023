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

using System.Linq;
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

		// The shipped base/slope pairings (ai.yaml @experimental) and the parked sweep grid. Kept as named
		// constants because the pins below assert the dials move AT these grid points — the first cut advertised
		// ranges that were only reachable at knob 0/100, so on-grid movement is the property under test.
		const int DepthBase = 3;
		const int DepthSlope = 7;
		const int CeilingBase = 20;
		const int CeilingSlope = 40;
		const int ForceBase = 5;
		const int ForceSlope = 7;
		static readonly int[] SweepGrid = { 20, 35, 50, 65, 80 };

		[Test]
		public void Dials_AtNeutralKnob_ReturnTheirBase()
		{
			// 50 is the tuned baseline by definition: every dial reads its own base, whatever the slope. This is
			// also what makes WIDENING a slope safe — (50-50)*slope/100 = 0 for any slope, so re-ranging a dial
			// never moves the neutral bot.
			Assert.That(OpportunisticAdvanceMath.MaxSectors(DepthBase, 50, DepthSlope), Is.EqualTo(DepthBase));
			Assert.That(OpportunisticAdvanceMath.DangerCeiling(CeilingBase, 50, CeilingSlope), Is.EqualTo(CeilingBase));
			Assert.That(OpportunisticAdvanceMath.ForceCap(ForceBase, 50, ForceSlope), Is.EqualTo(ForceBase));

			// Neutrality is slope-independent, not just true at the shipped numbers.
			Assert.That(OpportunisticAdvanceMath.MaxSectors(DepthBase, 50, 999), Is.EqualTo(DepthBase));
			Assert.That(OpportunisticAdvanceMath.ForceCap(ForceBase, 50, 999), Is.EqualTo(ForceBase));
		}

		[Test]
		public void Dials_MoveAtEveryPointOfTheParkedSweepGrid()
		{
			// THE pin that the first cut would have failed. Integer division truncates toward zero, so a knob 15
			// points off neutral needs slope >= 7 to shift an integer dial at all: at the original depth-4 /
			// force-6 slopes the grid produced {2,3,3,3,4} and {4,5,5,5,6}, i.e. three of five points identical
			// to neutral, and a sweep over them would have measured the danger ceiling alone.
			var depth = SweepGrid.Select(k => OpportunisticAdvanceMath.MaxSectors(DepthBase, k, DepthSlope)).ToArray();
			var ceiling = SweepGrid.Select(k => OpportunisticAdvanceMath.DangerCeiling(CeilingBase, k, CeilingSlope)).ToArray();
			var force = SweepGrid.Select(k => OpportunisticAdvanceMath.ForceCap(ForceBase, k, ForceSlope)).ToArray();

			Assert.That(depth, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
			Assert.That(ceiling, Is.EqualTo(new[] { 8, 14, 20, 26, 32 }));
			Assert.That(force, Is.EqualTo(new[] { 3, 4, 5, 6, 7 }));

			// Strictly monotonic across the grid — every sweep point is a distinct configuration on every dial.
			foreach (var series in new[] { depth, ceiling, force })
				for (var i = 1; i < series.Length; i++)
					Assert.That(series[i], Is.GreaterThan(series[i - 1]), "a sweep point must differ from its neighbour");
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
		public void Dials_SpanTheShippedEndpointsAcrossTheKnob()
		{
			// The absolute endpoints, off-grid. Cautious: a 2-unit screen into TOTALLY clear ground only
			// (ceiling 0), with depth floored to 0 — advance disabled outright, the blessed cautious extreme
			// (§2.3 blesses the mirror case for the posture cuts).
			Assert.That(OpportunisticAdvanceMath.MaxSectors(DepthBase, 0, DepthSlope), Is.EqualTo(0));
			Assert.That(OpportunisticAdvanceMath.DangerCeiling(CeilingBase, 0, CeilingSlope), Is.EqualTo(0));
			Assert.That(OpportunisticAdvanceMath.ForceCap(ForceBase, 0, ForceSlope), Is.EqualTo(2));

			// Reckless: deeper, through more marginal danger, with a larger force.
			Assert.That(OpportunisticAdvanceMath.MaxSectors(DepthBase, 100, DepthSlope), Is.EqualTo(6));
			Assert.That(OpportunisticAdvanceMath.DangerCeiling(CeilingBase, 100, CeilingSlope), Is.EqualTo(40));
			Assert.That(OpportunisticAdvanceMath.ForceCap(ForceBase, 100, ForceSlope), Is.EqualTo(8));
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

		// ---------- AdoptAdvanceAnchor — one-way hysteresis ----------
		//
		// Depth is FRONTIER DISTANCE: larger = shallower. The whole point of these pins is that hysteresis may
		// damp only a DEEPENING move; every way of giving ground adopts at once.

		[Test]
		public void AdoptAdvanceAnchor_HeldAnchorNoLongerGranted_AdoptsImmediately()
		{
			// The defect this guard exists for: candidates are grid-cell CENTRES one coarse cell apart, so a
			// one-sector closure moves the anchor by CellSize (2) — under the shipped hysteresis of 3. Symmetric
			// hysteresis would suppress it and keep ordering the screen at ground that just FAILED the grant test.
			Assert.That(OpportunisticAdvanceMath.AdoptAdvanceAnchor(
				heldStillGranted: false, heldDepth: 1, candidateDepth: 1, shiftedPastHysteresis: false), Is.True);
		}

		[Test]
		public void AdoptAdvanceAnchor_ShallowerCandidate_AdoptsImmediately()
		{
			// The walk no longer reaches as far (frontier distance 1 -> 3 = two sectors shallower). Giving ground
			// is never damped, even while the held anchor still happens to read granted.
			Assert.That(OpportunisticAdvanceMath.AdoptAdvanceAnchor(
				heldStillGranted: true, heldDepth: 1, candidateDepth: 3, shiftedPastHysteresis: false), Is.True);
		}

		[Test]
		public void AdoptAdvanceAnchor_DeeperCandidateWithinHysteresis_HoldsTheAnchor()
		{
			// The one case hysteresis is allowed to damp: the walk found deeper ground but only just, so the
			// screen is not re-laid on a one-cell field wobble.
			Assert.That(OpportunisticAdvanceMath.AdoptAdvanceAnchor(
				heldStillGranted: true, heldDepth: 3, candidateDepth: 1, shiftedPastHysteresis: false), Is.False);
		}

		[Test]
		public void AdoptAdvanceAnchor_EqualDepthWithinHysteresis_HoldsTheAnchor()
		{
			// A lateral move at the same depth is also damped — that is the jitter case staging's hysteresis
			// exists for, and it is retained here.
			Assert.That(OpportunisticAdvanceMath.AdoptAdvanceAnchor(
				heldStillGranted: true, heldDepth: 2, candidateDepth: 2, shiftedPastHysteresis: false), Is.False);
		}

		[Test]
		public void AdoptAdvanceAnchor_PastHysteresis_AdoptsEvenWhenDeepening()
		{
			// Hysteresis is a damper, not a lock: a big enough shift is adopted on the ordinary path.
			Assert.That(OpportunisticAdvanceMath.AdoptAdvanceAnchor(
				heldStillGranted: true, heldDepth: 3, candidateDepth: 1, shiftedPastHysteresis: true), Is.True);
		}

		// The shipped @experimental value. 0 is a considered setting, not a disabled knob — see the field Desc.
		const int ShippedHysteresisCells = 0;

		// One sector of anchor movement in MAP cells: anchors are control-grid cell CENTRES, so the smallest
		// possible move is ControlField.CellSize (2) map cells. This is the quantum every hysteresis threshold
		// is really being compared against, and the reason a scalar map-cell threshold is ill-posed here.
		const int OneSectorInMapCells = 2;

		[Test]
		public void AnchorShifted_ZeroThreshold_AdoptsUnconditionally_IncludingZeroDisplacement()
		{
			// Load-bearing dependency pin, stated because it is easy to assume the wrong mechanism: a
			// non-positive threshold is an EARLY RETURN of true (ForwardStagingMath.cs:169-170), NOT a
			// "displacement > threshold" test. So at the shipped 0 the hysteresis disjunct is always true even
			// when the anchor has not moved at all — and what actually prevents a re-issue in that case is the
			// per-unit target-cell dedup in StageFreePool, not this function.
			Assert.That(ForwardStagingMath.AnchorShifted(10, 10, 10, 10, ShippedHysteresisCells), Is.True);
			Assert.That(ForwardStagingMath.AnchorShifted(10, 10, 12, 10, ShippedHysteresisCells), Is.True);
		}

		[Test]
		public void AdoptAdvanceAnchor_ShippedConfig_ReadoptsDepthAfterAClosure()
		{
			// THE regression trace for FIX B, run at the SHIPPED config rather than at hand-chosen booleans.
			// Depth here is FRONTIER DISTANCE, so "3 sectors deep" is frontier distance 1 and one sector of
			// retreat is distance 2. Eval A holds the deep cell. Eval B: ground closes, the walk only reaches
			// one sector shallower — adopt (that is FIX 2 working). Eval C: the ground re-opens and the walk
			// reaches the deep cell again — this MUST adopt. With a non-zero map-cell threshold it never could
			// (next test), so the advance would ratchet to the shallowest depth it had ever seen and quietly
			// bias the very dial the sweep is meant to read.
			bool Shifted(int fromX, int toX) =>
				ForwardStagingMath.AnchorShifted(fromX, 0, toX, 0, ShippedHysteresisCells);

			// B: held at frontier distance 1, candidate one sector shallower (distance 2).
			Assert.That(OpportunisticAdvanceMath.AdoptAdvanceAnchor(
				heldStillGranted: true, heldDepth: 1, candidateDepth: 2,
				shiftedPastHysteresis: Shifted(10, 10 + OneSectorInMapCells)), Is.True, "closure must adopt");

			// C: held at the shallower cell (distance 2), candidate one sector DEEPER again (distance 1), and
			// the held anchor still reads granted — so the first two disjuncts are both false by construction
			// and only the hysteresis term can carry this. That is exactly what the shipped 0 guarantees.
			Assert.That(OpportunisticAdvanceMath.AdoptAdvanceAnchor(
				heldStillGranted: true, heldDepth: 2, candidateDepth: 1,
				shiftedPastHysteresis: Shifted(10 + OneSectorInMapCells, 10)), Is.True, "re-deepening must adopt");
		}

		[Test]
		public void AdoptAdvanceAnchor_NonZeroMapCellHysteresis_PermanentlyBlocksOneSectorRedeepening()
		{
			// Why the shipped value is 0, pinned as the counterfactual. At the previously-shipped 3, a
			// one-sector re-deepening (Chebyshev 2 < 3) fires none of the three disjuncts — and because the
			// identical 1-sector gap recurs on every re-eval there is no later pass where it wins. Not a delay:
			// a permanent refusal.
			const int OldHysteresisCells = 3;

			var shifted = ForwardStagingMath.AnchorShifted(10, 0, 10 + OneSectorInMapCells, 0, OldHysteresisCells);
			Assert.That(shifted, Is.False, "one sector is below a 3-map-cell threshold");

			Assert.That(OpportunisticAdvanceMath.AdoptAdvanceAnchor(
				heldStillGranted: true, heldDepth: 2, candidateDepth: 1, shiftedPastHysteresis: shifted), Is.False);

			// And the grid-space alternative is no rescue: a 1-sector threshold admits every move (equivalent to
			// 0), while a 2-sector threshold re-creates exactly the refusal above.
			Assert.That(ForwardStagingMath.AnchorShifted(5, 0, 6, 0, thresholdCells: 1), Is.True, "grid threshold 1 == no damping");
			Assert.That(ForwardStagingMath.AnchorShifted(5, 0, 6, 0, thresholdCells: 2), Is.False, "grid threshold 2 blocks 1 sector");
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
		public void AdvanceCell_BelievedContact_BendsTheCorridorOffAxis()
		{
			// Condition (2) is genuinely independent of (1) and (3): a lone cheap contact may neither flip control
			// nor stamp danger above the ceiling, yet the sector is not undefended.
			//
			// PINNED AS AN EXACT PATH, not as a bound. The earlier form asserted only X < 9 and != (8,0), which
			// a build with the !contactPresent term DELETED also satisfies (it runs straight down y=0 to (4,0)).
			// The contact at (8,0) denies that cell outright while its diagonals stay clear, so the walk steps
			// (10,0) -> (9,0) -> (8,1) and then continues west along y=1. The y coordinate is therefore the
			// witness: neuter condition (2) and the walk lands on y == 0, reddening this pin.
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 6, dangerCeiling: 20,
				FrontierByX, NoDanger, NoEnemyGround, (gx, gy) => gx == 8 && gy == 0, AllPassable, BigGrid);

			Assert.That(cell, Is.EqualTo((4, 1)));

			// The detour step itself, pinned at the budget where it happens, so a regression in the bend is
			// distinguishable from a regression in the run-out.
			var detour = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 2, dangerCeiling: 20,
				FrontierByX, NoDanger, NoEnemyGround, (gx, gy) => gx == 8 && gy == 0, AllPassable, BigGrid);

			Assert.That(detour, Is.EqualTo((8, 1)));
		}

		[Test]
		public void AdvanceCell_ContactWallAcrossTheCorridor_StopsTheWalk()
		{
			// The other half of condition (2): a contact set the walk cannot bend around halts it outright, with
			// no danger and no enemy classification anywhere. Without the term the walk would reach (6,0).
			var cell = OpportunisticAdvanceMath.AdvanceCell(10, 0, maxSectors: 4, dangerCeiling: 20,
				FrontierByX, NoDanger, NoEnemyGround, (gx, gy) => gx <= 9, AllPassable, BigGrid);

			Assert.That(cell, Is.EqualTo((10, 0)));
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
