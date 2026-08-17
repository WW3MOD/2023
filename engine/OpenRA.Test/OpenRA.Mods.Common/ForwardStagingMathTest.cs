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

using System;
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

		// ---------- Passability (the 24-scan drop-anchor stall, 2026-08-09) ----------

		[Test]
		public void StagingCell_NeverTerminatesOnAnImpassableCell()
		{
			// THE REGRESSION PIN. The cell the unguarded descent lands on — (3,0), exactly at the standoff —
			// is impassable. In the user's play log this is what happened for real: the west player's drop
			// anchor descent returned the same unreachable cell for 24 CONSECUTIVE scans (~2.4 minutes) while
			// the caller dutifully rejected it every time, so drop-and-leave went dark for the whole window
			// and the supply-truck oscillation sits entirely inside it. A deterministic walk over a
			// slow-moving field does not "miss once" — it re-derives the identical bad answer forever.
			//
			// Reverting the passability filter puts this back at (3,0) and turns the assertion red.
			bool Passable(int gx, int gy) => !(gx == 3 && gy == 0);

			var cell = ForwardStagingMath.StagingCell(10, 0, standoffCells: 3, dangerSafeThreshold: 40, maxSteps: 20,
				FrontierByX, NoDanger, BigGrid, Passable);

			Assert.Multiple(() =>
			{
				Assert.That(Passable(cell.X, cell.Y), Is.True, "the descent must not hand back a cell the mover cannot stand on");
				Assert.That(cell.X, Is.EqualTo(3), "and it still reaches the standoff — routing around, not giving up");
			});
		}

		[Test]
		public void StagingCell_ImpassableGroundIsNotPreferredForReadingSafe()
		{
			// Impassable terrain carries NO danger stamp, so it reads 0 — maximally safe — and a danger-guarded
			// descent is therefore actively ATTRACTED to water and cliffs. That is what makes this a systematic
			// failure rather than a rare accident: here the whole forward column x<=4 is impassable but quiet,
			// while the passable route is merely un-hot. The walk must hold on passable ground at x=5.
			bool Passable(int gx, int gy) => gx > 4;

			var cell = ForwardStagingMath.StagingCell(10, 0, standoffCells: 1, dangerSafeThreshold: 40, maxSteps: 20,
				FrontierByX, NoDanger, BigGrid, Passable);

			Assert.That(cell, Is.EqualTo((5, 0)), "the walk halts on passable ground instead of descending into it");
		}

		[Test]
		public void StagingCell_NullPredicateKeepsTheLegacyWalk()
		{
			// Callers that cannot bind a locomotor pass no predicate, and must behave exactly as before —
			// this is what keeps the three call sites this change did NOT touch byte-identical.
			var guarded = ForwardStagingMath.StagingCell(10, 0, standoffCells: 3, dangerSafeThreshold: 40, maxSteps: 20,
				FrontierByX, NoDanger, BigGrid, null);
			var legacy = ForwardStagingMath.StagingCell(10, 0, standoffCells: 3, dangerSafeThreshold: 40, maxSteps: 20,
				FrontierByX, NoDanger, BigGrid);

			Assert.That(guarded, Is.EqualTo(legacy));
			Assert.That(legacy, Is.EqualTo((3, 0)));
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

		// ---------- SpreadSlot ----------

		// Everything on the map, and every cell standable. The geometry pins below are about ring layout, not
		// about the guard, so they say so once here rather than repeating two all-true lambdas each.
		static (int X, int Y) Spread(int anchorX, int anchorY, int index, int ringStep, Func<int, int, bool> inBounds)
		{
			return ForwardStagingMath.SpreadSlot(anchorX, anchorY, index, ringStep,
				inBounds, (x, y) => true, out _);
		}

		[Test]
		public void SpreadSlot_IndexZeroIsTheAnchor()
		{
			Assert.That(Spread(20, 20, index: 0, ringStep: 2, (x, y) => true), Is.EqualTo((20, 20)));
		}

		[Test]
		public void SpreadSlot_FirstRingFansOverEightDistinctCells()
		{
			var seen = new System.Collections.Generic.HashSet<(int, int)>();
			for (var i = 1; i <= 8; i++)
				seen.Add(Spread(20, 20, i, ringStep: 2, (x, y) => true));

			Assert.That(seen.Count, Is.EqualTo(8), "the first eight units fan out over eight distinct cells");
			Assert.That(seen, Does.Not.Contain((20, 20)), "no ring-1 unit piles on the anchor");
		}

		[Test]
		public void SpreadSlot_RollsToTheSecondRing()
		{
			// Index 9 starts ring 2 (first octant), 2 * ringStep out.
			Assert.That(Spread(20, 20, index: 9, ringStep: 2, (x, y) => true),
				Is.EqualTo((20, 20 - 2 * 2)), "the ninth unit rolls onto the second ring");
		}

		[Test]
		public void SpreadSlot_OffGridFallsBackToTheAnchor()
		{
			Assert.That(Spread(0, 0, index: 4, ringStep: 2, (x, y) => x >= 0 && y >= 0),
				Is.EqualTo((0, 0)), "an off-grid spread cell falls back to the anchor");
		}

		// ---------- SpreadSlot: the terrain guard (2026-08-17) ----------
		//
		// THE DEFECT THESE EXIST TO STOP. Until this was closed, the guard the two call sites handed the spread
		// was bounds-only Map.Contains unless the anchor had come from the fallback path — so on the gradient
		// path a ring slot could be on-map WATER or CLIFF and the unit was ordered into it. It survived because
		// the only instrumentation watching this counts DISTANCE FROM THE SUPPLY ROUTE: a unit walking into the
		// sea makes that number BETTER. The assertions below are the property a distance census cannot express.

		// A coast running down x = 12: everything at or east of it is water. `AnchorX` sits 2 cells inland, so at
		// ringStep 2 the eastern octants of ring 1 land exactly ON the waterline and ring 2 lands past it.
		const int CoastX = 12;
		const int AnchorX = 10;
		const int AnchorY = 10;
		static bool Ashore(int x, int y) => x < CoastX;
		static bool OnMap(int x, int y) => x >= 0 && x < 40 && y >= 0 && y < 40;

		[Test]
		public void SpreadSlot_CoastalAnchor_NeverOrdersAUnitOntoImpassableGround()
		{
			// Every slot a full pool can occupy at the shipped fallback geometry (6 cells / step 2 => 2 rings).
			var rings = ForwardStagingMath.MaxSpreadRings(6, 2);
			var slots = rings * ForwardStagingMath.RingOctants;
			var drowned = new System.Collections.Generic.List<string>();
			var collapses = 0;

			for (var slot = 0; slot <= slots; slot++)
			{
				var (sx, sy) = ForwardStagingMath.SpreadSlot(AnchorX, AnchorY, slot, ringStep: 2,
					OnMap, Ashore, out var collapsed);

				if (collapsed)
					collapses++;

				if (!Ashore(sx, sy))
					drowned.Add($"slot {slot} -> ({sx},{sy})");
			}

			Assert.Multiple(() =>
			{
				Assert.That(drowned, Is.Empty,
					$"{drowned.Count} of {slots + 1} slots ordered a unit onto impassable ground " +
					$"(anchor {AnchorX},{AnchorY}; coast at x={CoastX}): {string.Join("; ", drowned)}");
				Assert.That(collapses, Is.GreaterThan(0),
					"the rejection signal must fire — several ring slots genuinely are in the sea here, " +
					"so zero collapses means the terrain test never ran");
			});
		}

		[Test]
		public void SpreadSlot_RejectedSlotCollapsesOntoTheAnchorAndSignals()
		{
			// WHICH ANSWER a rejected slot gets, pinned so it cannot drift silently. The anchor is the one cell
			// every caller has already proved it wants units at (TryResolveFallbackCell terrain-tests it, and the
			// gradient descent walked onto it), so collapsing there is the same answer the fallback path gives one
			// level down. Cost, accepted knowingly: on a coastal anchor several units share a cell.
			// Slot 2 is the EAST octant of ring 1 => (12,10), exactly on the waterline.
			var (sx, sy) = ForwardStagingMath.SpreadSlot(AnchorX, AnchorY, index: 2, ringStep: 2,
				OnMap, Ashore, out var collapsed);

			Assert.Multiple(() =>
			{
				Assert.That(collapsed, Is.True, $"slot 2 resolves to ({sx},{sy}), which is water — it must be rejected");
				Assert.That((sx, sy), Is.EqualTo((AnchorX, AnchorY)), "a rejected slot collapses onto the anchor");
			});
		}

		[Test]
		public void SpreadSlot_OmittingTheTerrainTestIsRejectedAtTheContract()
		{
			// THE PIN THAT KEEPS THIS CLOSED RATHER THAN MERELY FIXED. The defect was never in the ring math — it
			// was that a call site could assemble a guard WITHOUT the terrain half and nothing said so. Both call
			// sites did exactly that, identically, for thirteen days. Reintroducing that assembly now throws here
			// instead of quietly ordering a unit into the sea, so the hole cannot be reopened by omission.
			Assert.Throws<ArgumentNullException>(
				() => ForwardStagingMath.SpreadSlot(AnchorX, AnchorY, index: 2, ringStep: 2, OnMap, null, out _),
				"a bounds-only spread guard must be a hard error, not a silent default");
		}

		[Test]
		public void SpreadSlot_GuardIsBoundToTheMover_NotToTheTerrainAlone()
		{
			// WHAT IS IMPASSABLE DEPENDS ON THE MOVER. A ridge line at x = 12 that infantry can cross and armour
			// cannot must produce different slots for the two, from the identical anchor and index — which is only
			// true if the caller binds the predicate to the unit being ORDERED rather than to a representative of
			// its group. A mixed pool slotted off one representative is how a tank gets sent where the scout went.
			bool ArmourCanStand(int x, int y) => x < CoastX;
			bool InfantryCanStand(int x, int y) => true;

			var armour = ForwardStagingMath.SpreadSlot(AnchorX, AnchorY, index: 2, ringStep: 2,
				OnMap, ArmourCanStand, out var armourCollapsed);
			var infantry = ForwardStagingMath.SpreadSlot(AnchorX, AnchorY, index: 2, ringStep: 2,
				OnMap, InfantryCanStand, out var infantryCollapsed);

			Assert.Multiple(() =>
			{
				Assert.That(armourCollapsed, Is.True, "the ridge is impassable for armour ⇒ its slot is rejected");
				Assert.That(infantryCollapsed, Is.False, "the same cell is fine for infantry ⇒ its slot stands");
				Assert.That(armour, Is.Not.EqualTo(infantry),
					$"same anchor and index must resolve differently per mover (armour {armour}, infantry {infantry})");
			});
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

		// ---------- TryResolveAnchorCell: the map<->grid handoff ----------
		//
		// StagingCell_FlatField_ReturnsStart above already pins the "inert until populated" property IN GRID
		// SPACE, and it has always passed. The shipped bug was one layer out: the caller checked that property
		// by round-tripping the result to MAP space and comparing against the SR cell. GridToMapCentre returns
		// the block CENTRE, so a zero-step descent compares UNEQUAL to its own seed unless the SR happens to sit
		// on the centre — at CellSize 2 that needs BOTH coordinates odd, i.e. one placement in four.
		//
		// So a green test on the math meant nothing about shipped behaviour. These pin the handoff itself.

		// The flat/unpopulated field: every cell reads the same 'far' sentinel, so no neighbour ever improves.
		static int FlatFar(int gx, int gy) => 64;

		[Test]
		public void TryResolveAnchorCell_FlatField_PublishesNoAnchorAtEveryParity()
		{
			// The regression, stated as the number that failed: 4 of 4 parities must publish NO anchor.
			// Before the fix this was 1 of 4 — only the odd/odd SR, which round-trips to itself by coincidence.
			var parities = new[] { (6, 16), (7, 16), (6, 17), (7, 17) };

			Assert.Multiple(() =>
			{
				foreach (var (srX, srY) in parities)
				{
					var published = ForwardStagingMath.TryResolveAnchorCell(
						cellSize: 2, srMapX: srX, srMapY: srY,
						standoffCells: 6, dangerSafeThreshold: 40, maxSteps: 64,
						FlatFar, NoDanger, BigGrid,
						out var ax, out var ay);

					Assert.That(published, Is.False,
						$"SR ({srX},{srY}): a flat field must publish no anchor, but published ({ax},{ay})");
				}
			});
		}

		[Test]
		public void TryResolveAnchorCell_FlatField_DoesNotRepublishTheSupplyRoute()
		{
			// The exact shipped incident, pinned to the digit. Run 260815_202509 had the SR at (6,16) and the
			// rendezvous consumed an "anchor" of (7,17) — which is not a staging cell at all, it is (6,16)
			// re-projected through the lossy grid round trip: 6/2=3, 16/2=8, then (3*2+1, 8*2+1) = (7,17).
			// The transport then delivered ONE cell from its own Supply Route and shuttled four times.
			var published = ForwardStagingMath.TryResolveAnchorCell(
				cellSize: 2, srMapX: 6, srMapY: 16,
				standoffCells: 6, dangerSafeThreshold: 40, maxSteps: 64,
				FlatFar, NoDanger, BigGrid,
				out var ax, out var ay);

			Assert.That(published, Is.False,
				$"the (6,16) -> (7,17) quantisation artifact must not be published as an anchor (got {ax},{ay})");
		}

		[Test]
		public void TryResolveAnchorCell_PopulatedField_StillPublishesAForwardAnchor()
		{
			// The fix must not make staging inert altogether: a real gradient still resolves, and forward.
			// FrontierByX puts the front at x=0, so descending from map x=20 (grid 10) to the standoff of 3
			// lands on grid x=3 => map centre 3*2+1 = 7, strictly nearer the front than the SR.
			var published = ForwardStagingMath.TryResolveAnchorCell(
				cellSize: 2, srMapX: 20, srMapY: 0,
				standoffCells: 3, dangerSafeThreshold: 40, maxSteps: 64,
				FrontierByX, NoDanger, BigGrid,
				out var ax, out var ay);

			Assert.Multiple(() =>
			{
				Assert.That(published, Is.True, "a populated field must still publish an anchor");
				Assert.That(ax, Is.EqualTo(7), "descends to the standoff and converts to that block's centre");
				Assert.That(ax, Is.LessThan(20), "the anchor must be FORWARD of the SR, never behind it");
				Assert.That(ay, Is.EqualTo(1), "the y block centre is unchanged by a pure-x descent");
			});
		}

		[Test]
		public void TryResolveAnchorCell_FrontAlreadyInsideTheStandoff_PublishesNoAnchor()
		{
			// The other zero-step case: the front is on top of us, so there is nothing to walk toward. Same
			// requirement, and it has the same parity exposure — SR (6,16) is even/even.
			var published = ForwardStagingMath.TryResolveAnchorCell(
				cellSize: 2, srMapX: 6, srMapY: 16,
				standoffCells: 100, dangerSafeThreshold: 40, maxSteps: 64,
				FrontierByX, NoDanger, BigGrid,
				out var ax, out var ay);

			Assert.That(published, Is.False,
				$"a front already inside the standoff must publish no anchor (got {ax},{ay})");
		}
		// ===== The deliberate fallback (TryResolveFallbackCell) =====
		//
		// Context these pin, measured 2026-08-17 in test-clog-census across both arms: with no gradient the
		// reserve does not merely fail to advance, it ACCUMULATES on the beachhead — 8 of a 12-unit pool within
		// 2 cells of the SR, and 100% of arriving reinforcements stopping there. The phantom anchor that used to
		// be published dispersed that same pool to 1 within 2 cells. The fallback has to reproduce the dispersal
		// while having a destination that means something, and must NEVER put a unit somewhere it cannot stand.

		[Test]
		public void TryResolveFallbackCell_OffByDefault_PublishesNothing()
		{
			// The baseline default is 0 and MUST stay inert: this trait is configured on BOTH bot profiles, so a
			// non-inert default would move the benchmark control silently.
			var published = ForwardStagingMath.TryResolveFallbackCell(
				srX: 6, srY: 16, towardX: 33, towardY: 17, maxCells: 0, (x, y) => true,
				out var cx, out var cy);

			Assert.Multiple(() =>
			{
				Assert.That(published, Is.False, "maxCells 0 is OFF and must publish no fallback");
				Assert.That(cx, Is.EqualTo(6), "the out params must degrade to the SR, never to a stray cell");
				Assert.That(cy, Is.EqualTo(16));
			});
		}

		[Test]
		public void TryResolveFallbackCell_ClearBearing_LandsExactlyMaxCellsFromTheSupplyRoute()
		{
			// SR 6,16 on a 66x34 map: centre is 33,17, so the bearing is almost due east.
			var published = ForwardStagingMath.TryResolveFallbackCell(
				srX: 6, srY: 16, towardX: 33, towardY: 17, maxCells: 6, (x, y) => true,
				out var cx, out var cy);

			var chebyshev = Math.Max(Math.Abs(cx - 6), Math.Abs(cy - 16));
			Assert.Multiple(() =>
			{
				Assert.That(published, Is.True);
				Assert.That(chebyshev, Is.EqualTo(6),
					$"the fallback must sit EXACTLY maxCells from the SR in the metric the census counts in (got {cx},{cy})");
				Assert.That(cx, Is.GreaterThan(6), "and on the map-centre side of the SR, not behind it");
			});
		}

		[Test]
		public void TryResolveFallbackCell_WaterOnTheBearing_WalksBackToTheFarthestCellItCanStandOn()
		{
			// THE FAILURE THIS EXISTS TO STOP: a 'sensible default' that puts a unit in the sea. Everything past
			// x=9 is impassable, so the answer is the farthest legal cell at or inside the distance, not the
			// ideal one and not nothing.
			var published = ForwardStagingMath.TryResolveFallbackCell(
				srX: 6, srY: 16, towardX: 33, towardY: 17, maxCells: 6, (x, y) => x <= 9,
				out var cx, out var cy);

			Assert.Multiple(() =>
			{
				Assert.That(published, Is.True, "a blocked bearing must degrade toward the SR, not give up");
				Assert.That(cx, Is.EqualTo(9), $"the FARTHEST passable cell wins (got {cx},{cy})");
				Assert.That(cx, Is.Not.EqualTo(6), "and it must still have left the Supply Route");
			});
		}

		[Test]
		public void TryResolveFallbackCell_NothingPassableAnywhere_PublishesNothing()
		{
			// Fully walled in: publish nothing and let the caller idle at the SR exactly as it does today.
			// UNRESOLVABLE and OFF must collapse to the same behaviour — no half-measure destination.
			var published = ForwardStagingMath.TryResolveFallbackCell(
				srX: 6, srY: 16, towardX: 33, towardY: 17, maxCells: 6, (x, y) => false,
				out _, out _);

			Assert.That(published, Is.False, "no passable cell on the bearing must publish no fallback");
		}

		[Test]
		public void TryResolveFallbackCell_SupplyRouteAtTheMapCentre_PublishesNothingRatherThanGuessing()
		{
			// Degenerate bearing. An SR mid-map should not happen (supply-route.md: SRs are an edge phenomenon)
			// but a neutral SR can be placed anywhere, and a zero-length bearing must not be normalised into an
			// arbitrary direction — that would be inventing a destination, which is the defect being fixed.
			var published = ForwardStagingMath.TryResolveFallbackCell(
				srX: 33, srY: 17, towardX: 33, towardY: 17, maxCells: 6, (x, y) => true,
				out _, out _);

			Assert.That(published, Is.False, "a degenerate bearing must publish nothing, not a guessed direction");
		}

		[Test]
		public void TryResolveFallbackCell_ClearsTheBandTheCensusCounts()
		{
			// The acceptance bar in cell terms. The census buckets are Chebyshev <= 2 and <= 4 from the SR; a
			// fallback at 6 with rings bounded by MaxSpreadRings(6, 2) = 2 puts the WIDEST slot 4 cells from the
			// anchor, so the nearest any unit can end up is 6 - 4 = 2... which would still register in near2.
			// Assert the tighter property the wiring actually relies on: no slot lands ON the SR, and the anchor
			// itself is clear of both bands.
			ForwardStagingMath.TryResolveFallbackCell(
				srX: 6, srY: 16, towardX: 33, towardY: 17, maxCells: 6, (x, y) => true,
				out var ax, out var ay);

			var rings = ForwardStagingMath.MaxSpreadRings(6, 2);
			var worst = 0;
			for (var slot = 0; slot <= rings * ForwardStagingMath.RingOctants; slot++)
			{
				var (sx, sy) = Spread(ax, ay, slot, 2, (x, y) => true);
				var d = Math.Max(Math.Abs(sx - 6), Math.Abs(sy - 16));
				if (d > worst)
					worst = d;
				Assert.That(d, Is.GreaterThan(0), $"slot {slot} landed on the Supply Route cell itself");
			}

			Assert.Multiple(() =>
			{
				Assert.That(Math.Max(Math.Abs(ax - 6), Math.Abs(ay - 16)), Is.GreaterThan(4),
					"the anchor must be outside BOTH census bands");
				Assert.That(worst, Is.LessThanOrEqualTo(10),
					"and the fan-out must stay bounded — the phantom's was 18 cells");
			});
		}
	}
}
