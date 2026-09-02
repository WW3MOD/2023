#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// A mobile SupplyProvider (a supply truck) driving to a docking-aware host — a Logistics Centre —
	/// to fill it: drive, settle, transfer. The exact mirror of <see cref="RestockSupply"/>, and
	/// deliberately built to the same shape so the two directions of one gesture cannot drift apart.
	///
	/// <para>WHY THIS IS A DIRECT TRANSFER AND NOT A CRATE DROP. The delivery order used to be a bare
	/// <c>MoveTo</c> with a <c>CallFunc(DropSupplyCacheHere)</c> queued behind it: the truck drove to
	/// the Centre, dumped its ENTIRE load on the ground as a SUPPLYCACHE actor, and left the Centre's
	/// own <see cref="AbsorbsSupplyCache"/> to pull the crate in over the following ticks. That works,
	/// and it was the right first cut while the gesture was a rarely-used Ctrl+click, but it has three
	/// properties that stop being acceptable once delivery is the DEFAULT click on an LC:</para>
	///
	/// <list type="number">
	/// <item>IT IS ALL-OR-NOTHING. The drop calls <c>SetSupply(0)</c>, so the amount transferred is not
	/// a decision anybody makes — there is no line to change when the partial-load policy is settled.
	/// Routing through <see cref="SupplyTransferMath.AmountToDeliver"/> gives that policy exactly one
	/// home.</item>
	/// <item>THE REMAINDER BECOMES LITTER. Absorption stops at the host's TotalSupply, so delivering
	/// into a nearly-full Centre leaves the excess sitting on the ground beside it as a crate the
	/// player did not ask for and has to clean up with a second order.</item>
	/// <item>THE TRANSFER IS NOT ATOMIC WITH THE ARRIVAL. Between the drop and the absorb the supply
	/// belongs to a third actor that can be shot, which makes "I delivered and it did not arrive" a
	/// reachable outcome with no diagnosis.</item>
	/// </list>
	///
	/// <para>The crate path is not deleted — it remains the DropSupplyCache deploy order, which is a
	/// different gesture with a different purpose (leave supply at a FORWARD position, for units that
	/// are not at a Centre). Only the LC-directed order stops routing through it.</para>
	///
	/// <para>Being a named activity type is load-bearing for the same reason RestockSupply's is: it is
	/// what lets <see cref="SupplyProvider.OnSupplyErrand"/> recognise a committed delivery off the
	/// activity queue and refrain from cancelling it. A bare MoveTo is indistinguishable by type from
	/// a player's move order.</para>
	/// </summary>
	public class DeliverSupply : Activity
	{
		readonly Actor host;
		readonly SupplyProvider supply;
		readonly IMove move;
		readonly IMoveInfo moveInfo;
		readonly int waitTicks;
		readonly int hostFootprintCells;
		readonly int approachMarginCells;

		public DeliverSupply(Actor self, Actor host, int waitTicks, int approachMarginCells)
		{
			this.host = host;
			this.waitTicks = waitTicks;
			this.approachMarginCells = approachMarginCells;
			supply = self.Trait<SupplyProvider>();
			move = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();

			var building = host.Info.TraitInfoOrDefault<BuildingInfo>();
			hostFootprintCells = building == null
				? 0
				: System.Math.Max(building.Dimensions.X, building.Dimensions.Y);
		}

		protected override void OnFirstRun(Actor self)
		{
			// ignoreActor: the host is a building standing on the cell we are driving at, so without
			// this the path is refused outright rather than ending alongside it.
			QueueChild(move.MoveTo(self.World.Map.CellContaining(host.CenterPosition), ignoreActor: host));
			QueueChild(new Wait(waitTicks));
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			// The host is captured at ISSUE time and the transfer happens after a drive, so all three of
			// these are re-checked on arrival rather than trusted from issue time.
			//
			// THE OWNERSHIP TEST IS NOT REDUNDANT WITH THE OTHER TWO, which is the trap: LOGISTICSCENTER
			// is capturable (^Building -> ^BasicBuilding -> ^NeutralOrOccupiedCapturable) and carries
			// OwnerLostAction: ChangeOwner, so a Centre that changes hands mid-drive is neither dead nor
			// out of the world. Without this line the truck hands its whole load to whoever took it.
			//
			// UNTESTED, DELIBERATELY RECORDED AS SUCH. Nothing in the suite exercises this line: it
			// needs a capture to land inside the window between issue and arrival, which the scenario
			// (test-lc-refill-gesture) does not stage and could not stage without a capture actor and
			// timing control it has no reason to carry otherwise. It is reasoned from the trait
			// inheritance above, not observed. Anyone touching this line should assume it has never run.
			if (host.IsDead || !host.IsInWorld || !self.Owner.IsAlliedWith(host.Owner))
				return true;

			// ARRIVAL CHECK — the load-bearing guard, not a formality, and the same one PlaceSupplyCache
			// and CollectSupplyCache carry. A Move to a cell with no route does not FAIL: the path finder
			// bails to NoPath and Move.Tick treats an empty path as arrival (Move.cs:173-177), completing
			// in ~2 ticks at the cell the truck was already standing on. Without this, ordering a truck
			// onto a Centre it cannot reach — across water, behind a wall, on terrain its locomotor
			// refuses — credits the Centre with the entire load from anywhere on the map.
			//
			// Refusing keeps the supply in the truck, which is always recoverable. Logged unconditionally
			// and once per errand: this is one of the ways an errand that was issued, driven and
			// completed still moves nothing, and from outside it is otherwise indistinguishable from a
			// delivery that was never ordered.
			//
			// WHAT IS AND IS NOT COVERED. SupplyTransferMath.ArrivalTolerance* pins the ARITHMETIC —
			// that the tolerance admits the diagonal corner approach and rejects a truck that never
			// left. NOTHING PINS THE WIRING. test-lc-refill-gesture drives a real delivery, but on a map
			// with no water and no wall, so its Centre is always reachable: delete these lines and that
			// scenario still passes. Verifying this guard needs a scenario staging genuinely unreachable
			// terrain, which does not exist yet.
			//
			// The composition footprint -> tolerance -> distance is ArrivedAtHost rather than three steps
			// written out here. Open-coding it is exactly how RestockSupply — this activity's documented
			// mirror — came to ship with no arrival check at all: there was no named thing for it to be
			// missing, so nothing looked absent.
			var hostCell = self.World.Map.CellContaining(host.CenterPosition);
			var delta = self.Location - hostCell;
			var arrived = SupplyTransferMath.ArrivedAtHost(
				delta.X, delta.Y, hostFootprintCells, approachMarginCells);

			if (!arrived)
				Log.Write("debug",
					$"[supply] deliver-refused truck={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
					+ $"reason=never-arrived host={hostCell} "
					+ $"tolerance={SupplyTransferMath.ArrivalTolerance(hostFootprintCells, approachMarginCells)}c "
					+ $"amount={supply.CurrentSupply}");

			var hostProvider = host.TraitOrDefault<SupplyProvider>();
			if (hostProvider == null)
				return true;

			// The arrival term is a parameter of the amount rather than a short-circuit above it, matching
			// RestockSupply. Both directions of this gesture now refuse through the same shape.
			var given = SupplyTransferMath.AmountToDeliver(
				arrived, supply.CurrentSupply, hostProvider.CurrentSupply, hostProvider.Info.TotalSupply);

			// DeductSupply before AddSupply, and only credit the host if the deduction actually took:
			// the pair must never create supply, and DeductSupply is the half that can refuse.
			if (given > 0 && supply.DeductSupply(given))
				hostProvider.AddSupply(given);

			return true;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (!host.IsDead && host.IsInWorld)
				yield return new TargetLineNode(Target.FromActor(host), moveInfo.GetTargetLineColor());
		}
	}
}
