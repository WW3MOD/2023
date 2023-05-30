#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Unit has upkeep cost.")]
	public class InfersUpkeepInfo : TraitInfo
	{
		public readonly int FixedCost = 0;
		public readonly int PermilleCost = 0;

		public override object Create(ActorInitializer init) { return new InfersUpkeep(init.Self, this); }
	}

	public class InfersUpkeep : INotifyOwnerChanged, INotifyCapture, ISync, INotifyKilled, INotifyAddedToWorld, INotifyRemovedFromWorld
	{
		readonly Actor self;
		readonly InfersUpkeepInfo info;
		PlayerResources player;

		public InfersUpkeep(Actor self, InfersUpkeepInfo info)
		{
			this.info = info;
			this.self = self;
			player = self.Owner.PlayerActor.Trait<PlayerResources>();
		}

		public float Cost
		{
			get
			{
				float cost = info.FixedCost;

				if (info.PermilleCost != 0)
					cost += self.Info.TraitInfoOrDefault<ValuedInfo>().Cost * (float)info.PermilleCost / 1000;

				return cost;
			}
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			player = newOwner.PlayerActor.Trait<PlayerResources>();
		}

		void INotifyCapture.OnCapture(Actor self, Actor captor, Player oldOwner, Player newOwner, BitSet<CaptureType> captureTypes)
		{
			oldOwner.PlayerActor.Trait<PlayerResources>().RemoveFromUpkeep(Cost);
			newOwner.PlayerActor.Trait<PlayerResources>().AddToUpkeep(Cost);
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			player.RemoveFromUpkeep(Cost);
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			player.AddToUpkeep(Cost);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			player.RemoveFromUpkeep(Cost);
		}
	}
}
