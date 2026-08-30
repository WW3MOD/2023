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

		public DeliverSupply(Actor self, Actor host, int waitTicks)
		{
			this.host = host;
			this.waitTicks = waitTicks;
			supply = self.Trait<SupplyProvider>();
			move = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();
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

			// The host is captured at ISSUE time and the transfer happens after a drive, so re-validate:
			// a Centre destroyed or captured mid-drive would otherwise be credited by a dead actor. The
			// truck simply arrives at a stale cell and keeps its load, which its owner re-decides from.
			if (host.IsDead || !host.IsInWorld)
				return true;

			var hostProvider = host.TraitOrDefault<SupplyProvider>();
			if (hostProvider == null)
				return true;

			var given = SupplyTransferMath.AmountToDeliver(
				supply.CurrentSupply, hostProvider.CurrentSupply, hostProvider.Info.TotalSupply);

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
