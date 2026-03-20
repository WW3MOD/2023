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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Drops a crate actor at the current location and transfers all supply to it.")]
	public class DropsCrateInfo : TraitInfo
	{
		[ActorReference]
		[Desc("Actor to spawn as the crate.")]
		public readonly string CrateActor = "supplycache";

		[CursorReference]
		[Desc("Cursor to display when deploying.")]
		public readonly string DeployCursor = "deploy";

		[CursorReference]
		[Desc("Cursor to display when deploy is blocked.")]
		public readonly string DeployBlockedCursor = "deploy-blocked";

		[VoiceReference]
		[Desc("Voice to use when ordered to deploy.")]
		public readonly string Voice = "Action";

		[Desc("Sounds to play on deploy.")]
		public readonly string[] DeploySounds = { };

		public override object Create(ActorInitializer init) { return new DropsCrate(init.Self, this); }
	}

	public class DropsCrate : IIssueDeployOrder, IIssueOrder, IResolveOrder, IOrderVoice
	{
		readonly DropsCrateInfo info;
		readonly Actor self;

		public DropsCrate(Actor self, DropsCrateInfo info)
		{
			this.self = self;
			this.info = info;
		}

		bool CanDeploy()
		{
			var supply = self.TraitOrDefault<SupplyProvider>();
			if (supply == null || supply.CurrentSupply <= 0)
				return false;

			return self.World.ActorMap.GetActorsAt(self.Location).All(a => a == self);
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			return new Order("DropCrate", self, Target.FromCell(self.World, self.Location), queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued)
		{
			return CanDeploy();
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new DeployOrderTargeter("DropCrate", 5,
					() => CanDeploy() ? info.DeployCursor : info.DeployBlockedCursor);
			}
		}

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID != "DropCrate")
				return null;

			return new Order("DropCrate", self, Target.FromCell(self.World, self.Location), queued);
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "DropCrate")
				return;

			if (!CanDeploy())
				return;

			var supply = self.Trait<SupplyProvider>();
			var currentSupply = supply.CurrentSupply;

			self.World.AddFrameEndTask(w =>
			{
				w.CreateActor(info.CrateActor, new TypeDictionary
				{
					new LocationInit(self.Location),
					new OwnerInit(self.Owner),
					new SupplyInit(w.Map.Rules.Actors[info.CrateActor].TraitInfo<SupplyProviderInfo>(), currentSupply),
				});
			});

			supply.SetSupply(0);

			foreach (var sound in info.DeploySounds)
				Game.Sound.Play(SoundType.World, sound, self.CenterPosition);
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString == "DropCrate")
				return info.Voice;

			return null;
		}
	}
}
