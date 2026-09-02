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
	/// to refill: drive, settle, transfer.
	///
	/// <para>THIS TYPE EXISTS TO BE RECOGNISED, which is as much its job as the drive is. The restock
	/// drive used to be a bare <c>move.MoveTo</c> with a <c>Wait</c> and a <c>CallFunc</c> queued behind
	/// it, and the truck's intent was recorded out-of-band in a private <c>bool restocking</c> that was
	/// cleared at exactly one line: inside that tail CallFunc. Any pre-emption — a player Move, an evac,
	/// a bot re-task — drops the tail, because <see cref="Activity.Cancel"/> nulls
	/// <c>NextActivity</c>. So the flag LATCHED TRUE FOREVER, and since <c>CanServeNow</c> is false while
	/// restocking, a truck interrupted once on the way to an LC silently served nobody for the rest of
	/// the match while looking perfectly healthy: supply on board, amber bar, CountsAsEmpty false so
	/// even the evacuate path would not dispose of it.</para>
	///
	/// <para>Deriving the state from the ACTIVITY QUEUE instead cannot latch, because the queue is what
	/// cancellation edits. Same technique, and for the same reason, as
	/// <see cref="AmmoPool.IsSeekingRearm"/> on the ammunition side.</para>
	///
	/// <para>The general rule this is an instance of, worth stating because it has now bitten twice in
	/// this subsystem: <b>a flag that records "an activity is in flight" must be cleared when the
	/// activity ENDS, not when it SUCCEEDS</b> — in an RTS, cancellation is the common ending.</para>
	///
	/// <para>Being a named type is also what lets a caller tell the two kinds of truck movement apart:
	/// a move that is INVALIDATED by the truck being empty, versus a move whose entire purpose is to
	/// stop being empty. A bare MoveTo is indistinguishable by type from a player's move order, so that
	/// question had no answer before.</para>
	/// </summary>
	public class RestockSupply : Activity
	{
		readonly Actor host;
		readonly SupplyProvider supply;
		readonly IMove move;
		readonly IMoveInfo moveInfo;
		readonly int waitTicks;
		readonly int hostFootprintCells;
		readonly int approachMarginCells;

		public RestockSupply(Actor self, Actor host, int waitTicks, int approachMarginCells)
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
			// ChildHasPriority is left at its default of true, which is correct HERE and is worth saying
			// because the sibling supply activities had to turn it off: this activity has no per-tick
			// re-evaluation to do. Tick runs once, after the drive and the settle have both finished, and
			// transfers. (Activity.cs:112.)
			if (IsCanceling)
				return true;

			// The host is captured at ISSUE time and the transfer happens after a drive, so re-validate:
			// an LC destroyed or captured mid-drive would otherwise be deducted from as a dead actor. The
			// truck simply arrives at a stale cell and takes nothing, which its owner re-decides from.
			if (host.IsDead || !host.IsInWorld)
				return true;

			// ARRIVAL CHECK — the guard this activity shipped WITHOUT, while the mirror it is documented
			// against (DeliverSupply) has carried it from the start. A Move to a cell with no route does
			// not fail: the path finder bails to NoPath and Move.Tick treats an empty path as arrival
			// (Move.cs:173-177), completing in about two ticks at the cell the truck was already on. So
			// without this, ordering a truck to restock at a Centre it cannot reach — across water, behind
			// a wall, on terrain its locomotor refuses — DRAINED that Centre from anywhere on the map and
			// filled the truck for free.
			//
			// Refusing leaves the supply in the Centre, which is always recoverable. Logged for the same
			// reason the delivery side logs: an errand that was issued, driven and completed but moved
			// nothing is otherwise indistinguishable from one that was never ordered.
			var hostCell = self.World.Map.CellContaining(host.CenterPosition);
			var delta = self.Location - hostCell;
			var arrived = SupplyTransferMath.ArrivedAtHost(
				delta.X, delta.Y, hostFootprintCells, approachMarginCells);

			if (!arrived)
				Log.Write("debug",
					$"[supply] restock-refused truck={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
					+ $"reason=never-arrived host={hostCell} "
					+ $"tolerance={SupplyTransferMath.ArrivalTolerance(hostFootprintCells, approachMarginCells)}c "
					+ $"held={supply.CurrentSupply}");

			var hostProvider = host.TraitOrDefault<SupplyProvider>();
			if (hostProvider == null)
				return true;

			// No free refills — the host's pool drops by exactly what was transferred, capped at what it
			// has on hand, so a truck may leave partially full. The arrival term is passed INTO the amount
			// rather than short-circuiting above it, and that is deliberate: it is what makes the refusal
			// a property of the shared arithmetic that a test can pin, rather than a line in an activity
			// that only a live scenario could exercise. Refusing leaves the supply in the Centre.
			var taken = SupplyTransferMath.AmountToRestock(
				arrived, supply.CurrentSupply, supply.Info.TotalSupply, hostProvider.CurrentSupply);

			if (taken > 0 && hostProvider.DeductSupply(taken))
				supply.AddSupply(taken);   // clears the residue-unusable latch on a genuine refill

			return true;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (!host.IsDead && host.IsInWorld)
				yield return new TargetLineNode(Target.FromActor(host), moveInfo.GetTargetLineColor());
		}
	}
}
