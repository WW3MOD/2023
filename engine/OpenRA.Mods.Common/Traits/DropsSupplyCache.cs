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
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Mobile supply transport (TRUK) behaviors: drop a SUPPLYCACHE on deploy,",
		"deliver supply to a friendly LC, drive in to restock when low.",
		"Requires SupplyProvider as the underlying storage.")]
	public class DropsSupplyCacheInfo : TraitInfo, Requires<SupplyProviderInfo>
	{
		[ActorReference]
		[Desc("Actor to create when supply is unloaded onto the ground.")]
		public readonly string SupplyCacheActor = "supplycache";

		[Desc("Relationships of allies whose Logistics Centers we can deliver / restock at.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally;

		[CursorReference]
		[Desc("Cursor for right-click on a friendly Logistics Center (default restock flow).")]
		public readonly string RestockCursor = "enter";

		[CursorReference]
		[Desc("Cursor for the deploy command when supply can be dropped as a SUPPLYCACHE.")]
		public readonly string DropCacheCursor = "deploy";

		[CursorReference]
		[Desc("Cursor for the deploy command when supply cannot be dropped (no supply, or cell blocked).")]
		public readonly string DropCacheBlockedCursor = "deploy-blocked";

		[VoiceReference]
		[Desc("Voice played when ordered to drop a SUPPLYCACHE.")]
		public readonly string DropCacheVoice = "Action";

		public override object Create(ActorInitializer init) { return new DropsSupplyCache(init, this); }
	}

	public class DropsSupplyCache : INotifyCreated, INotifyBecomingIdle, IResolveOrder,
		IIssueOrder, IIssueDeployOrder, IOrderVoice
	{
		public readonly DropsSupplyCacheInfo Info;
		readonly Actor self;
		SupplyProvider supply;

		public DropsSupplyCache(ActorInitializer init, DropsSupplyCacheInfo info)
		{
			Info = info;
			self = init.Self;
		}

		void INotifyCreated.Created(Actor self)
		{
			supply = self.Trait<SupplyProvider>();
		}

		bool CanDropCache()
		{
			if (supply == null || supply.CurrentSupply <= 0)
				return false;

			// Cell must be clear or already hold a SUPPLYCACHE to merge into.
			return self.World.ActorMap.GetActorsAt(self.Location)
				.All(a => a == self || (!a.IsDead && a.Info.Name == Info.SupplyCacheActor));
		}

		/// <summary>Drop all current supply as a SUPPLYCACHE at the transport's cell.</summary>
		void DropSupplyCacheHere()
		{
			if (supply == null || supply.CurrentSupply <= 0)
				return;

			var amount = supply.CurrentSupply;

			// Merge into an existing cache on this cell, if any.
			var existing = self.World.ActorMap.GetActorsAt(self.Location)
				.FirstOrDefault(a => !a.IsDead && a.Info.Name == Info.SupplyCacheActor);

			if (existing != null)
			{
				var existingProvider = existing.TraitOrDefault<SupplyProvider>();
				if (existingProvider != null)
				{
					existingProvider.AddSupply(amount);
					supply.SetSupply(0);
					return;
				}
			}

			// Otherwise spawn a fresh cache initialized with this amount.
			var cacheInfo = self.World.Map.Rules.Actors[Info.SupplyCacheActor]
				.TraitInfoOrDefault<SupplyProviderInfo>();
			if (cacheInfo == null)
				return;

			supply.SetSupply(0);

			self.World.AddFrameEndTask(w =>
			{
				w.CreateActor(Info.SupplyCacheActor, new TypeDictionary
				{
					new LocationInit(self.Location),
					new OwnerInit(self.Owner),
					new SupplyInit(cacheInfo, amount),
				});
			});
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "DropSupplyCache")
			{
				DropSupplyCacheHere();
				return;
			}

			if (order.OrderString == "Restock")
			{
				if (order.Target.Type != TargetType.Actor)
					return;

				var host = order.Target.Actor;
				if (host == null || host.IsDead || !host.IsInWorld)
					return;

				var hostProvider = host.TraitOrDefault<SupplyProvider>();
				if (hostProvider == null)
					return;

				QueueDriveAndRestock(host);
				self.ShowTargetLines();
				return;
			}

			if (order.OrderString == "DeliverSupply")
			{
				if (order.Target.Type != TargetType.Actor)
					return;

				var host = order.Target.Actor;
				if (host == null || host.IsDead || !host.IsInWorld)
					return;

				if (host.TraitOrDefault<AbsorbsSupplyCache>() == null)
					return;

				var move = self.TraitOrDefault<IMove>();
				if (move == null)
					return;

				// Drive next to the LC and drop the supply on our cell. The LC's
				// AbsorbsSupplyCache pulls the cache in on its next tick.
				var targetCell = self.World.Map.CellContaining(host.CenterPosition);
				self.QueueActivity(order.Queued, move.MoveTo(targetCell, 2));
				self.QueueActivity(true, new CallFunc(() => DropSupplyCacheHere()));
				self.ShowTargetLines();
			}
		}

		void QueueDriveAndRestock(Actor host)
		{
			var move = self.TraitOrDefault<IMove>();
			if (move == null)
				return;

			var targetCell = self.World.Map.CellContaining(host.CenterPosition);
			self.QueueActivity(false, move.MoveTo(targetCell, ignoreActor: host));
			self.QueueActivity(new Wait(25));
			self.QueueActivity(new CallFunc(() =>
			{
				var hostProvider = host.TraitOrDefault<SupplyProvider>();
				if (hostProvider == null || supply == null)
					return;

				var needed = supply.Info.TotalSupply - supply.CurrentSupply;
				var taken = System.Math.Min(needed, hostProvider.CurrentSupply);
				if (taken > 0 && hostProvider.DeductSupply(taken))
					supply.AddSupply(taken);
			}));
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			// Empty truck with no orders: try to drive back to the nearest friendly
			// LC; if none can host us, evacuate via RotateToEdge.
			if (supply == null || supply.CurrentSupply > 0)
				return;

			var autoTarget = self.TraitOrDefault<AutoTarget>();
			var behavior = autoTarget?.ResupplyBehaviorValue ?? ResupplyBehavior.Auto;

			switch (behavior)
			{
				case ResupplyBehavior.Hold:
					return;

				case ResupplyBehavior.Auto:
					if (TryQueueRestockAtNearestHost(self))
						return;
					goto case ResupplyBehavior.Evacuate;

				case ResupplyBehavior.Evacuate:
					var amount = self.GetSellValue();
					self.QueueActivity(false, new RotateToEdge(self, true, amount));
					self.ShowTargetLines();
					return;
			}
		}

		bool TryQueueRestockAtNearestHost(Actor self)
		{
			if (self.TraitOrDefault<IMove>() == null || supply == null)
				return false;

			// Only target docking-aware hosts (LCs), so an empty truck doesn't try
			// to "dock" at a ground SUPPLYCACHE.
			var host = self.World.ActorsHavingTrait<SupplyProvider>()
				.Where(a => !a.IsDead && a.IsInWorld && a != self
					&& Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner))
					&& a.Trait<SupplyProvider>().CurrentSupply > 0
					&& !string.IsNullOrEmpty(a.Trait<SupplyProvider>().Info.DockedCondition))
				.ClosestToIgnoringPath(self);

			if (host == null)
				return false;

			QueueDriveAndRestock(host);
			self.ShowTargetLines();
			return true;
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new RestockOrderTargeter(Info);
				yield return new DeliverSupplyOrderTargeter(Info);
				yield return new DeployOrderTargeter("DropSupplyCache", 5,
					() => CanDropCache() ? Info.DropCacheCursor : Info.DropCacheBlockedCursor);
			}
		}

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "Restock" || order.OrderID == "DeliverSupply")
				return new Order(order.OrderID, self, target, queued);

			if (order.OrderID == "DropSupplyCache")
				return new Order("DropSupplyCache", self, queued);

			return null;
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			return new Order("DropSupplyCache", self, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued)
		{
			return CanDropCache();
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString == "DropSupplyCache")
				return Info.DropCacheVoice;
			return null;
		}

		sealed class RestockOrderTargeter : UnitOrderTargeter
		{
			public RestockOrderTargeter(DropsSupplyCacheInfo info)
				: base("Restock", 7, info.RestockCursor, false, true) { }

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				if (!self.Owner.IsAlliedWith(target.Owner))
					return false;

				// Only docking-aware providers (LC), not ground caches.
				var hostProvider = target.TraitOrDefault<SupplyProvider>();
				if (hostProvider == null || string.IsNullOrEmpty(hostProvider.Info.DockedCondition))
					return false;

				var truckSupply = self.TraitOrDefault<SupplyProvider>();
				if (truckSupply == null)
					return false;

				var notFull = truckSupply.CurrentSupply < truckSupply.Info.TotalSupply;
				var damaged = self.TraitOrDefault<IHealth>()?.DamageState > DamageState.Undamaged;
				if (!notFull && !damaged)
					return false;

				return true;
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				return false;
			}
		}

		sealed class DeliverSupplyOrderTargeter : UnitOrderTargeter
		{
			public DeliverSupplyOrderTargeter(DropsSupplyCacheInfo info)
				: base("DeliverSupply", 6, info.RestockCursor, false, true) { }

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				// Default right-click on an LC goes to Restock (priority 7). Only
				// Ctrl+click (ForceMove) means "deliver my supply to this LC".
				if (!modifiers.HasModifier(TargetModifiers.ForceMove))
					return false;

				if (!self.Owner.IsAlliedWith(target.Owner))
					return false;

				if (target.TraitOrDefault<AbsorbsSupplyCache>() == null)
					return false;

				var truckSupply = self.TraitOrDefault<SupplyProvider>();
				return truckSupply != null && truckSupply.CurrentSupply > 0;
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				return false;
			}
		}
	}
}
