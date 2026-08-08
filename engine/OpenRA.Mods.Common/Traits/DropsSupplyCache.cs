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

	public class DropsSupplyCache : INotifyCreated, INotifyBecomingIdle, ITick, IResolveOrder,
		IIssueOrder, IIssueDeployOrder, IOrderVoice
	{
		public readonly DropsSupplyCacheInfo Info;
		readonly Actor self;
		SupplyProvider supply;

		// Guards against OnBecomingIdle and ITick both firing EvacuateOrRestock on the same
		// idle-transition frame (which would double-queue RotateToEdge).
		int lastEvacuateTick = -1;

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

			// DROP-AND-LEAVE (bot-issued; no targeter, so it never appears on a human's cursor). The WHOLE
			// errand — drive out, unload, then dispose of the emptied chassis — is composed here as ONE
			// activity chain rather than as a sequence of orders, and that is a correctness requirement, not
			// tidiness. Dropping calls SetSupply(0), which makes CountsAsEmpty true, and the ITick below
			// fires EvacuateOrRestock on ANY tick the truck is idle-and-empty: under TRUK's AI default
			// (InitialResupplyBehaviorAI: Evacuate, vehicles.yaml) that means RotateToEdge — driven to the
			// map edge and sold. A caller issuing "drop" and "then restock" as two orders loses that race,
			// because ModularBot meters its queue (MinOrderQuotientPerTick) and can put the two on different
			// ticks. Chaining them means the truck is never idle between the drop and its disposition.
			if (order.OrderString == "DropSupplyCacheAt")
			{
				var dropMove = self.TraitOrDefault<IMove>();
				if (dropMove == null)
					return;

				// Stop WITHIN 2 cells rather than on the exact cell: the anchor is a belief-field cell, not a
				// reserved parking space, and an exact-cell MoveTo fails outright when something is standing
				// there. The crate lands on whatever cell the truck actually stopped on.
				var dropCell = self.World.Map.CellContaining(order.Target.CenterPosition);
				self.QueueActivity(order.Queued, dropMove.MoveTo(dropCell, 2));

				// CanDropCache re-checked at arrival, not at issue: the cell's occupancy is only knowable
				// once we are there. A blocked cell means no drop this errand — the truck keeps its load and
				// its owner re-decides next scan, which is self-correcting because the blocker moves.
				self.QueueActivity(true, new CallFunc(() =>
				{
					if (CanDropCache())
						DropSupplyCacheHere();
				}));

				// POST-DROP DISPOSITION, decided here so it is a stated design rather than an emergent
				// consequence of the idle path above. If we hold a docking-aware host (a Logistics Centre) the
				// truck runs a real supply shuttle: drive back, refill, and its owner re-adopts it once it is
				// no longer low on supply. Otherwise NOTHING is appended and the existing evacuate path
				// retires the chassis for its sell value — which is not a regression but the status quo made
				// deliberate, since an emptied truck is dropped by SupplyFollowerBotModule and retired that
				// way today regardless of where its supply went. LOGISTICSCENTER is Prerequisites: ~disabled
				// and exists only as a neutral capturable, so the retire arm is the ORDINARY case and the
				// shuttle arm is the reward for having taken one.
				var restockHost = NearestRestockHost();
				if (restockHost != null)
					QueueDriveAndRestock(restockHost, true);

				self.ShowTargetLines();
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

		void QueueDriveAndRestock(Actor host, bool queued = false)
		{
			var move = self.TraitOrDefault<IMove>();
			if (move == null)
				return;

			var targetCell = self.World.Map.CellContaining(host.CenterPosition);
			self.QueueActivity(queued, move.MoveTo(targetCell, ignoreActor: host));
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
			// Empty (or holding an unusable residue) truck with no orders: try to drive
			// back to the nearest friendly LC; if none can host us, evacuate via RotateToEdge.
			if (supply == null || !supply.CountsAsEmpty)
				return;

			EvacuateOrRestock(self);
		}

		void ITick.Tick(Actor self)
		{
			// A residue only becomes unusable after the truck has already gone idle at the
			// front, so OnBecomingIdle (which fires on the idle transition) has come and gone.
			// Re-check here: if we're idle and now count as empty, run the same evac/restock
			// flow. RotateToEdge / the restock MoveTo make us non-idle, so this self-limits.
			if (supply == null || !supply.CountsAsEmpty || !self.IsIdle)
				return;

			EvacuateOrRestock(self);
		}

		void EvacuateOrRestock(Actor self)
		{
			// Only act once per frame — OnBecomingIdle and ITick can both reach here on the
			// same idle-transition tick.
			if (lastEvacuateTick == self.World.WorldTick)
				return;

			lastEvacuateTick = self.World.WorldTick;

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

		/// <summary>The nearest host this truck could actually refill at, or null. Only docking-aware
		/// providers (Logistics Centres) qualify, so an empty truck never tries to "dock" at a ground
		/// SUPPLYCACHE — the crate has no DockedCondition and no arrival gate, and the transfer path assumes
		/// one. Shared by the idle evacuate/restock path and by the drop errand's post-drop disposition so
		/// the two can never disagree about what counts as a host.</summary>
		Actor NearestRestockHost()
		{
			if (self.TraitOrDefault<IMove>() == null || supply == null)
				return null;

			return self.World.ActorsHavingTrait<SupplyProvider>()
				.Where(a => !a.IsDead && a.IsInWorld && a != self
					&& Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner))
					&& a.Trait<SupplyProvider>().CurrentSupply > 0
					&& !string.IsNullOrEmpty(a.Trait<SupplyProvider>().Info.DockedCondition))
				.ClosestToIgnoringPath(self);
		}

		bool TryQueueRestockAtNearestHost(Actor self)
		{
			var host = NearestRestockHost();
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
