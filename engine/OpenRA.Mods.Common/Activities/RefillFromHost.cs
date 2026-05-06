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

using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// Drives a unit with a CargoSupply pool to a host with a SupplyProvider, docks,
	/// and transfers supply pip-by-pip until full or the host runs dry.
	/// Used for the supply truck → Logistics Center "Restock" flow.
	/// </summary>
	public class RefillFromHost : Activity
	{
		readonly Target host;
		readonly CargoSupply cargo;
		readonly IMove move;
		readonly IMoveInfo moveInfo;
		readonly WDist closeEnough;

		int ticksUntilNextTransfer;
		bool moveQueued;

		public RefillFromHost(Actor self, Actor hostActor)
		{
			host = Target.FromActor(hostActor);
			cargo = self.Trait<CargoSupply>();
			move = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();
			closeEnough = new WDist(512);
			ticksUntilNextTransfer = 0;
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			if (host.Type != TargetType.Actor || host.Actor == null
				|| host.Actor.IsDead || !host.Actor.IsInWorld)
				return true;

			// Already full — nothing to do.
			if (cargo.SupplyCount >= cargo.Info.MaxSupply)
				return true;

			var hostProvider = host.Actor.TraitOrDefault<SupplyProvider>();
			if (hostProvider == null || hostProvider.CurrentSupply <= 0)
				return true;

			// Move into dock range if not already there.
			var distSq = (host.Actor.CenterPosition - self.CenterPosition).HorizontalLengthSquared;
			if (distSq > closeEnough.LengthSquared)
			{
				if (!moveQueued)
				{
					QueueChild(move.MoveOntoTarget(self, host, WVec.Zero, null, moveInfo.GetTargetLineColor()));
					moveQueued = true;
				}

				return false;
			}

			moveQueued = false;

			// Drip-feed supply: one truck pip per host RearmDelay tick.
			if (--ticksUntilNextTransfer > 0)
				return false;

			ticksUntilNextTransfer = hostProvider.Info.RearmDelay;

			// Cost in host supply units to add one truck pip.
			var costPerUnit = cargo.Info.SupplyPerUnit;
			if (hostProvider.CurrentSupply < costPerUnit)
				return true;

			var added = cargo.AddSupply(1);
			if (added > 0)
				hostProvider.DeductSupply(costPerUnit);

			return false;
		}

		public override System.Collections.Generic.IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (ChildActivity == null)
				yield return new TargetLineNode(host, moveInfo.GetTargetLineColor());
			else
			{
				var current = ChildActivity;
				while (current != null)
				{
					foreach (var n in current.TargetLineNodes(self))
						yield return n;

					current = current.NextActivity;
				}
			}
		}
	}
}
