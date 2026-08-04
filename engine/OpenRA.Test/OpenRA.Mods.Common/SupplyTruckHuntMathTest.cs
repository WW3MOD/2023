#region Copyright & License Information
/*
 * WW3MOD supply-truck hunt (@experimental) — idle-truck target selection test.
 *
 * Pins the decisions SupplyFollowerBotModule turns into Move orders when IdleTruckHunt is on, so "an idle
 * truck drives to the starving squad" can't silently regress into "an idle truck wanders":
 *   (1) THE BOUND — nothing starving inside the leash ⇒ NoDemand ⇒ the caller issues no order ⇒ the truck
 *       stays put. Inclusive at the boundary, and Euclidean (a 20-cell diagonal is OUTSIDE a 20-cell leash).
 *   (2) THE ORDER — need band desc, then distance asc, then ActorID asc.
 *   (3) BANDING — near-equal need ties so distance decides (the anti-retarget-churn rule); band 0/1 falls
 *       back to raw shortfall.
 *   (4) DEMAND READING — shortfall is cross-multiplied, so a 3-round pool and a 900-round pool read the
 *       same percentage; starvation defers to Tier 1's threshold rule.
 *   (5) DETERMINISM — the pick is independent of enumeration order, and repeats exactly.
 *   (6) THE GATES — NeedsApproach (a truck already covering its pick must not be re-ordered) and
 *       ShouldHunt (the shared-instance @experimental BotType gate).
 *   (7) THE APPROACH CLAMP — the truck stops one cell short of the aura edge instead of driving onto the
 *       soldier, and the margin is large enough that the quantized destination cell still lies inside the
 *       aura AND is not the truck's own cell (the anti-stall property).
 * Pure math over synthetic candidates; no world mounted.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupplyTruckHuntMathTest
	{
		const int Cell = 1024;

		const int Leash = 20;

		// The shipped band (ai.yaml @supply): 10% of capacity per band.
		const int Band = 100;

		// TRUK's push aura (vehicles.yaml Range: 5c0) — the number the approach clamp stops short of.
		const int Aura = 5 * Cell;

		// Half a cell diagonal, 1024 * sqrt(2) / 2, rounded UP: the worst-case distance between a point and
		// the centre of the cell Map.CellContaining resolves it to. The margin has to beat this.
		const int HalfCellDiagonal = 724;

		static WPos Pos(int x, int y) => new(x, y, 0);

		static int DistanceFrom(WPos a, WPos b) => (b - a).HorizontalLength;

		static long DistSqCells(int xCells, int yCells) =>
			((long)xCells * Cell * xCells * Cell) + ((long)yCells * Cell * yCells * Cell);

		static SupplyTruckHuntMath.Demand Demand(long distanceSquared, int shortfall, uint actorId) =>
			new(distanceSquared, shortfall, actorId);

		#region (1) The bound — leash

		[Test]
		public void EmptyCandidateList_IsNoDemand()
		{
			// The truck-stays-put case: the sweep found nobody.
			Assert.That(
				SupplyTruckHuntMath.SelectDemand(new List<SupplyTruckHuntMath.Demand>(), Leash, Band),
				Is.EqualTo(SupplyTruckHuntMath.NoDemand));
		}

		[Test]
		public void CandidateBeyondLeash_IsNeverPicked()
		{
			// One starving soldier, bone dry, but 21 cells out. Need must not buy him a truck.
			var demands = new List<SupplyTruckHuntMath.Demand> { Demand(DistSqCells(21, 0), 1000, 1) };

			Assert.That(
				SupplyTruckHuntMath.SelectDemand(demands, Leash, Band),
				Is.EqualTo(SupplyTruckHuntMath.NoDemand));
		}

		[Test]
		public void CandidateExactlyOnLeash_IsPicked()
		{
			// Inclusive boundary, matching SupplyHuntMath.WithinLeash / the aura's own inclusive edge.
			var demands = new List<SupplyTruckHuntMath.Demand> { Demand(DistSqCells(20, 0), 500, 1) };

			Assert.That(SupplyTruckHuntMath.SelectDemand(demands, Leash, Band), Is.EqualTo(0));
		}

		[Test]
		public void LeashIsEuclidean_NotChessboard()
		{
			// 20 cells on each axis reads ~28 cells straight-line — outside a 20-cell leash, even though a
			// player counting squares on the minimap would call it "20 away". Same metric as Tier 1.
			var demands = new List<SupplyTruckHuntMath.Demand> { Demand(DistSqCells(20, 20), 1000, 1) };

			Assert.That(
				SupplyTruckHuntMath.SelectDemand(demands, Leash, Band),
				Is.EqualTo(SupplyTruckHuntMath.NoDemand));
		}

		[Test]
		public void ZeroLeash_AdmitsNobodyButAColocatedSoldier()
		{
			var colocated = new List<SupplyTruckHuntMath.Demand> { Demand(0, 500, 1) };
			var oneCell = new List<SupplyTruckHuntMath.Demand> { Demand(DistSqCells(1, 0), 500, 1) };

			Assert.That(SupplyTruckHuntMath.SelectDemand(colocated, 0, Band), Is.EqualTo(0));
			Assert.That(
				SupplyTruckHuntMath.SelectDemand(oneCell, 0, Band),
				Is.EqualTo(SupplyTruckHuntMath.NoDemand));
		}

		[Test]
		public void OutOfLeashCandidates_DoNotHideAnInLeashOne()
		{
			// The nearer soldier is less needy, but the needier one is out of reach — the bound wins, and the
			// unreachable candidate must not poison the pick into NoDemand either.
			var demands = new List<SupplyTruckHuntMath.Demand>
			{
				Demand(DistSqCells(30, 0), 1000, 1),
				Demand(DistSqCells(5, 0), 300, 2),
			};

			Assert.That(SupplyTruckHuntMath.SelectDemand(demands, Leash, Band), Is.EqualTo(1));
		}

		#endregion

		#region (2) The order — need, then distance, then ActorID

		[Test]
		public void NeedierBandWins_OverNearer()
		{
			// The whole point of the sweep: relieve the emptiest squad, not the closest one.
			var demands = new List<SupplyTruckHuntMath.Demand>
			{
				Demand(DistSqCells(2, 0), 200, 1),   // band 2, close
				Demand(DistSqCells(10, 0), 900, 2),  // band 9, far
			};

			Assert.That(SupplyTruckHuntMath.SelectDemand(demands, Leash, Band), Is.EqualTo(1));
		}

		[Test]
		public void WithinTheSameBand_NearerWins()
		{
			// 910 and 990 both band to 9, so distance is the decider.
			var demands = new List<SupplyTruckHuntMath.Demand>
			{
				Demand(DistSqCells(10, 0), 990, 1),
				Demand(DistSqCells(3, 0), 910, 2),
			};

			Assert.That(SupplyTruckHuntMath.SelectDemand(demands, Leash, Band), Is.EqualTo(1));
		}

		[Test]
		public void EqualBandAndDistance_LowestActorIdWins()
		{
			// Two soldiers of the same squad standing on the same spot: the pick must be a total order, not
			// whatever the spatial query happened to yield first.
			var demands = new List<SupplyTruckHuntMath.Demand>
			{
				Demand(DistSqCells(4, 0), 500, 77),
				Demand(DistSqCells(4, 0), 500, 12),
				Demand(DistSqCells(4, 0), 500, 40),
			};

			Assert.That(SupplyTruckHuntMath.SelectDemand(demands, Leash, Band), Is.EqualTo(1));
		}

		[Test]
		public void SingleInLeashCandidate_IsPicked()
		{
			var demands = new List<SupplyTruckHuntMath.Demand> { Demand(DistSqCells(7, 3), 250, 5) };

			Assert.That(SupplyTruckHuntMath.SelectDemand(demands, Leash, Band), Is.EqualTo(0));
		}

		#endregion

		#region (3) Banding

		[Test]
		public void NeedBand_QuantizesToTheBandWidth()
		{
			Assert.That(SupplyTruckHuntMath.NeedBand(0, 100), Is.EqualTo(0));
			Assert.That(SupplyTruckHuntMath.NeedBand(99, 100), Is.EqualTo(0));
			Assert.That(SupplyTruckHuntMath.NeedBand(100, 100), Is.EqualTo(1));
			Assert.That(SupplyTruckHuntMath.NeedBand(1000, 100), Is.EqualTo(10));
		}

		[Test]
		public void NeedBand_ZeroOrOne_IsRawShortfall()
		{
			Assert.That(SupplyTruckHuntMath.NeedBand(437, 0), Is.EqualTo(437));
			Assert.That(SupplyTruckHuntMath.NeedBand(437, 1), Is.EqualTo(437));
		}

		[Test]
		public void OnePerMilleDifference_DoesNotDragTheTruckPastACloserSoldier()
		{
			// The churn case banding exists for. Unbanded, the 1‰-needier soldier 15 cells away wins and the
			// truck re-targets across the sector every time an ammo pip lands. Banded, distance decides.
			var demands = new List<SupplyTruckHuntMath.Demand>
			{
				Demand(DistSqCells(15, 0), 501, 1),
				Demand(DistSqCells(2, 0), 500, 2),
			};

			Assert.That(SupplyTruckHuntMath.SelectDemand(demands, Leash, Band), Is.EqualTo(1));
			Assert.That(SupplyTruckHuntMath.SelectDemand(demands, Leash, 1), Is.EqualTo(0));
		}

		#endregion

		#region (4) Demand reading

		[Test]
		public void ShortfallPerMille_ReadsEmptiness()
		{
			Assert.That(SupplyTruckHuntMath.ShortfallPerMille(0, 10), Is.EqualTo(1000));
			Assert.That(SupplyTruckHuntMath.ShortfallPerMille(10, 10), Is.EqualTo(0));
			Assert.That(SupplyTruckHuntMath.ShortfallPerMille(1, 4), Is.EqualTo(750));
		}

		[Test]
		public void ShortfallPerMille_IsScaleFree()
		{
			// A 3-missile pool and a 900-round pool at the same fraction must read the same — the whole
			// reason the arithmetic is cross-multiplied rather than divided down.
			Assert.That(SupplyTruckHuntMath.ShortfallPerMille(1, 3), Is.EqualTo(SupplyTruckHuntMath.ShortfallPerMille(300, 900)));
		}

		[Test]
		public void ShortfallPerMille_DegenerateInputs_AreZero()
		{
			// No capacity, and an over-full pool: neither is demand.
			Assert.That(SupplyTruckHuntMath.ShortfallPerMille(0, 0), Is.EqualTo(0));
			Assert.That(SupplyTruckHuntMath.ShortfallPerMille(5, -1), Is.EqualTo(0));
			Assert.That(SupplyTruckHuntMath.ShortfallPerMille(12, 10), Is.EqualTo(0));
		}

		[Test]
		public void IsStarving_MatchesTier1SeekThreshold()
		{
			// Same rule the soldier applies to himself, asserted against Tier 1 directly so the two halves
			// of the meeting can never drift apart.
			for (var ammo = 0; ammo <= 10; ammo++)
				Assert.That(
					SupplyTruckHuntMath.IsStarving(ammo, 10, 250),
					Is.EqualTo(SupplyHuntMath.BelowSeekThreshold(ammo, 10, 250)),
					$"ammo {ammo}/10");
		}

		[Test]
		public void IsStarving_IsStrictlyBelowTheThreshold()
		{
			// Exactly on the threshold is NOT starving — a soldier sitting on the line must not oscillate.
			Assert.That(SupplyTruckHuntMath.IsStarving(250, 1000, 250), Is.False);
			Assert.That(SupplyTruckHuntMath.IsStarving(249, 1000, 250), Is.True);
			Assert.That(SupplyTruckHuntMath.IsStarving(1000, 1000, 250), Is.False);
		}

		#endregion

		#region (5) Determinism

		[Test]
		public void Pick_IsIndependentOfEnumerationOrder()
		{
			// Same soldiers, reversed scan order — the spatial query gives no ordering guarantee, so the
			// pick must be identified by ActorID, not by index.
			var demands = new List<SupplyTruckHuntMath.Demand>
			{
				Demand(DistSqCells(5, 0), 400, 3),
				Demand(DistSqCells(5, 0), 400, 1),
				Demand(DistSqCells(9, 0), 880, 2),
			};

			var forward = demands[SupplyTruckHuntMath.SelectDemand(demands, Leash, Band)].ActorId;

			var reversed = Enumerable.Reverse(demands).ToList();
			var backward = reversed[SupplyTruckHuntMath.SelectDemand(reversed, Leash, Band)].ActorId;

			Assert.That(forward, Is.EqualTo(2u));
			Assert.That(backward, Is.EqualTo(forward));
		}

		[Test]
		public void Pick_RepeatsExactly()
		{
			// Zero RNG: identical synced inputs, identical answer, every time.
			var demands = new List<SupplyTruckHuntMath.Demand>
			{
				Demand(DistSqCells(3, 4), 610, 9),
				Demand(DistSqCells(6, 0), 640, 4),
				Demand(DistSqCells(1, 1), 120, 7),
			};

			var first = SupplyTruckHuntMath.SelectDemand(demands, Leash, Band);
			for (var i = 0; i < 50; i++)
				Assert.That(SupplyTruckHuntMath.SelectDemand(demands, Leash, Band), Is.EqualTo(first));
		}

		#endregion

		#region (6) The gates

		[Test]
		public void NeedsApproach_IsFalseInsideTheAura()
		{
			// TRUK's aura is 5c0. A soldier at 4 cells is already being pushed to — issue nothing.
			var aura = (long)(5 * Cell) * (5 * Cell);

			Assert.That(SupplyTruckHuntMath.NeedsApproach(DistSqCells(4, 0), aura), Is.False);
			Assert.That(SupplyTruckHuntMath.NeedsApproach(DistSqCells(6, 0), aura), Is.True);
		}

		[Test]
		public void NeedsApproach_IsInclusiveAtTheAuraEdge()
		{
			// Matches SupplyProvider.InAuraRange's inclusive edge, so "in the aura" means the same thing to
			// the hunt and to the provider that will do the pushing.
			var aura = (long)(5 * Cell) * (5 * Cell);

			Assert.That(SupplyTruckHuntMath.NeedsApproach(aura, aura), Is.False);
			Assert.That(SupplyTruckHuntMath.NeedsApproach(aura + 1, aura), Is.True);
		}

		[Test]
		public void WithinTheAura_NeverConsultsTheClamp()
		{
			// The clamp only ever runs behind NeedsApproach. Pinned together so the "already covering him ⇒
			// issue no order at all" contract can't be quietly relaxed by a later change to the stop rule.
			var aura = (long)Aura * Aura;

			Assert.That(SupplyTruckHuntMath.NeedsApproach(DistSqCells(3, 0), aura), Is.False);
			Assert.That(SupplyTruckHuntMath.NeedsApproach(DistSqCells(3, 4), aura), Is.False);
		}

		[Test]
		public void ShouldHunt_RequiresBothTheFlagAndTheExperimentalBot()
		{
			// The shared-instance gate: the flag alone must never reach the @stable player, because
			// InfluenceStack.Participates admits it since the 0802 promotion.
			Assert.That(SupplyTruckHuntMath.ShouldHunt(true, true), Is.True);
			Assert.That(SupplyTruckHuntMath.ShouldHunt(true, false), Is.False);
			Assert.That(SupplyTruckHuntMath.ShouldHunt(false, true), Is.False);
			Assert.That(SupplyTruckHuntMath.ShouldHunt(false, false), Is.False);
		}

		#endregion

		#region (7) The approach clamp

		[Test]
		public void ApproachTarget_StopsOneCellShortOfTheAuraEdge()
		{
			// Soldier at the origin, truck 12 cells east, TRUK's 5c0 aura. The truck should be sent to
			// 4 cells from the SOLDIER — inside the push aura — not onto his cell 12 cells away.
			var stop = SupplyTruckHuntMath.ApproachTarget(Pos(12 * Cell, 0), Pos(0, 0), Aura);

			Assert.That(DistanceFrom(Pos(0, 0), stop), Is.EqualTo(Aura - Cell));
		}

		[Test]
		public void ApproachTarget_JustOutsideTheAura_SurvivesCellQuantization()
		{
			// The stall case. A truck one world unit outside the aura must be given a destination that is
			// still strictly inside the aura AFTER Map.CellContaining snaps it to a cell centre, and that
			// cannot snap back onto the truck's own cell — otherwise the Move is a no-op, the next scan
			// re-derives the same point, and the truck parks out of range forever.
			var soldier = Pos(0, 0);
			var truck = Pos(Aura + 1, 0);

			var stop = SupplyTruckHuntMath.ApproachTarget(truck, soldier, Aura);

			// (a) Worst-case quantized cell centre is still inside the aura.
			Assert.That(DistanceFrom(soldier, stop) + HalfCellDiagonal, Is.LessThan(Aura));

			// (b) The point is at least a full cell from the truck, and two distinct cell centres are at
			// least one cell apart — so the resolved cell is never the truck's own and the order moves it.
			Assert.That(DistanceFrom(truck, stop), Is.GreaterThanOrEqualTo(Cell));
		}

		[Test]
		public void ApproachTarget_SmallAura_FallsBackToTheSoldiersCell()
		{
			// Degenerate: an aura no wider than the margin leaves nothing to stop short of, so the rule
			// collapses to the old behaviour rather than producing a point behind the truck. TRUK never
			// reaches this, but the function has to be total.
			var soldier = Pos(0, 0);
			var truck = Pos(20 * Cell, 0);

			Assert.That(SupplyTruckHuntMath.ApproachTarget(truck, soldier, Cell), Is.EqualTo(soldier));
			Assert.That(SupplyTruckHuntMath.ApproachTarget(truck, soldier, 500), Is.EqualTo(soldier));
			Assert.That(SupplyTruckHuntMath.ApproachTarget(truck, soldier, 0), Is.EqualTo(soldier));

			// One unit above the margin is no longer degenerate: it stops 1 unit from the soldier.
			Assert.That(DistanceFrom(soldier, SupplyTruckHuntMath.ApproachTarget(truck, soldier, Cell + 1)), Is.EqualTo(1));
		}

		[Test]
		public void ApproachTarget_TruckAlreadyInsideTheStopRadius_IsNotPushedOut()
		{
			// Out of the caller's contract (NeedsApproach gates it), but the math must not answer "drive
			// away from him". Includes the co-located case, which is also what keeps the scaling from
			// dividing by zero.
			var soldier = Pos(0, 0);
			var near = Pos(2 * Cell, 0);

			Assert.That(SupplyTruckHuntMath.ApproachTarget(near, soldier, Aura), Is.EqualTo(near));
			Assert.That(SupplyTruckHuntMath.ApproachTarget(soldier, soldier, Aura), Is.EqualTo(soldier));
		}

		[Test]
		public void ApproachTarget_HoldsOnADiagonal()
		{
			// 3-4-5: the truck sits exactly one aura out on a diagonal. The stop point must be on the same
			// line at the margin distance — within the engine's integer-sqrt rounding, not exact.
			var soldier = Pos(0, 0);
			var truck = Pos(3 * Cell, 4 * Cell);

			var stop = SupplyTruckHuntMath.ApproachTarget(truck, soldier, Aura);

			Assert.That(DistanceFrom(soldier, stop), Is.EqualTo(Aura - Cell).Within(2));
			Assert.That(DistanceFrom(soldier, stop) + HalfCellDiagonal, Is.LessThan(Aura));
			Assert.That(DistanceFrom(truck, stop), Is.GreaterThanOrEqualTo(Cell));

			// Still on the soldier→truck ray (both components shrink in the same proportion, sign kept).
			Assert.That(stop.X, Is.GreaterThan(0));
			Assert.That(stop.Y, Is.GreaterThan(stop.X));
		}

		[Test]
		public void ApproachTarget_IsDeterministic()
		{
			var soldier = Pos(1234, 5678);
			var truck = Pos(9012, 3456);

			var first = SupplyTruckHuntMath.ApproachTarget(truck, soldier, Aura);
			for (var i = 0; i < 50; i++)
				Assert.That(SupplyTruckHuntMath.ApproachTarget(truck, soldier, Aura), Is.EqualTo(first));
		}

		#endregion
	}
}
