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
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Tracks a numeric supply pool on a unit. When supply is loaded, passively auto-rearms nearby",
		"allied units. Supply has its own capacity independent of Cargo.")]
	public class CargoSupplyInfo : TraitInfo
	{
		[Desc("Maximum supply units this actor can hold.")]
		public readonly int MaxSupply = 10;

		[Desc("Ammo supply value per supply unit (how much ammo one unit can give before being consumed).")]
		public readonly int SupplyPerUnit = 50;

		[Desc("Number of supply units loaded at spawn. -1 = MaxSupply (full).")]
		public readonly int InitialSupply = -1;

		[Desc("Maximum resupply range when transport has supply loaded.")]
		public readonly WDist RearmRange = WDist.FromCells(4);

		[Desc("Ticks between ammo increments (one pip per cycle).")]
		public readonly int RearmDelay = 25;

		[Desc("How often (in ticks) to scan for new targets.")]
		public readonly int ScanInterval = 7;

		[Desc("Minimum ammo need in millipercent (0-1000) to consider a unit for resupply. 50 = 5%.")]
		public readonly int MinNeedThreshold = 50;

		[Desc("Relationships of actors that can be resupplied.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally;

		[Desc("Condition to grant to the unit currently being rearmed.")]
		public readonly string RearmCondition = "replenish-soldiers";

		[GrantedConditionReference]
		[Desc("Condition granted to self when carrying any supply.")]
		public readonly string CargoHasSupplyCondition = null;

		[Desc("Credit value per supply unit (for sell/value calculations).")]
		public readonly int CreditValuePerUnit = 50;

		[Desc("Actor to create when supply is unloaded onto the ground.")]
		public readonly string SupplyCacheActor = "supplycache";

		[CursorReference]
		[Desc("Cursor shown when right-clicking a friendly Logistics Center (or any actor with AbsorbsSupplyCache) to deliver supply.")]
		public readonly string DeliverCursor = "enter";

		[CursorReference]
		[Desc("Cursor shown on the deploy command when supply can be dropped as a SUPPLYCACHE.")]
		public readonly string DropCacheCursor = "deploy";

		[CursorReference]
		[Desc("Cursor shown on the deploy command when supply cannot be dropped (no supply, or cell blocked).")]
		public readonly string DropCacheBlockedCursor = "deploy-blocked";

		[VoiceReference]
		[Desc("Voice to play when ordered to drop supply as a SUPPLYCACHE.")]
		public readonly string DropCacheVoice = "Action";

		public override object Create(ActorInitializer init) { return new CargoSupply(init, this); }
	}

	public class CargoSupply : ITick, INotifyCreated, INotifyBecomingIdle, ISelectionBar, ITransformActorInitModifier, IResolveOrder, IIssueOrder, IIssueDeployOrder, IOrderVoice
	{
		public readonly CargoSupplyInfo Info;
		readonly Actor self;

		// Supply tracking
		[Sync]
		int supplyCount;
		[Sync]
		int effectiveSupply; // = supplyCount * Info.SupplyPerUnit, decremented as ammo is given

		// Rearm state
		int rearmTicks;
		int scanTicks;
		Actor currentTarget;
		ExternalCondition targetConditionTrait;
		int conditionToken = Actor.InvalidConditionToken;

		// Self condition
		int hasSupplyToken = Actor.InvalidConditionToken;

		public int SupplyCount => supplyCount;
		public int EffectiveSupply => effectiveSupply;
		public int TotalCapacity => Info.MaxSupply * Info.SupplyPerUnit;

		public CargoSupply(ActorInitializer init, CargoSupplyInfo info)
		{
			Info = info;
			self = init.Self;

			// Load initial supply from init (e.g., from transform), fallback to InitialSupply.
			// InitialSupply = -1 means "full tank" (MaxSupply).
			var defaultInitial = info.InitialSupply < 0 ? info.MaxSupply : info.InitialSupply;
			var initialCount = init.GetValue<CargoSupplyInit, int>(info, defaultInitial);
			supplyCount = System.Math.Min(initialCount, info.MaxSupply);
			effectiveSupply = supplyCount * info.SupplyPerUnit;
		}

		void INotifyCreated.Created(Actor self)
		{
			// Stagger so multiple supply trucks don't all scan on the same tick.
			scanTicks = self.World.SharedRandom.Next(0, Info.ScanInterval);
			UpdateSupplyCondition();
		}

		void ITick.Tick(Actor self)
		{
			if (supplyCount <= 0 || effectiveSupply <= 0)
			{
				RevokeTargetCondition();
				currentTarget = null;
				return;
			}

			// Periodic scan for targets needing rearm
			if (--scanTicks <= 0)
			{
				scanTicks = Info.ScanInterval;
				UpdateTarget();
			}

			// Resupply current target
			if (currentTarget != null)
			{
				if (--rearmTicks <= 0)
				{
					ResupplyTarget();
					scanTicks = 0; // Re-evaluate immediately after giving ammo
				}
			}
		}

		void UpdateTarget()
		{
			var bestTarget = FindGreatestNeedTarget();

			if (bestTarget == null)
			{
				// In Hunt stance, seek distant flagged units
				var autoTarget = self.TraitOrDefault<AutoTarget>();
				if (autoTarget != null && autoTarget.EngagementStanceValue >= EngagementStance.Hunt)
					bestTarget = FindNeedsResupplyTarget();
			}

			if (bestTarget == null)
			{
				if (currentTarget != null)
				{
					RevokeTargetCondition();
					currentTarget = null;
				}

				return;
			}

			SetTarget(bestTarget);
		}

		Actor FindNeedsResupplyTarget()
		{
			return self.World.ActorsHavingTrait<AmmoPool>()
				.Where(a => !a.IsDead && a.IsInWorld && a != self
					&& Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner))
					&& a.TraitsImplementing<AmmoPool>().Any(ap => ap.NeedsResupply)
					&& a.TraitOrDefault<Rearmable>() != null)
				.ClosestToIgnoringPath(self);
		}

		Actor FindGreatestNeedTarget()
		{
			Actor best = null;
			var bestNeed = 0;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, Info.RearmRange))
			{
				if (IsValidTarget(a))
				{
					var rearmable = a.Trait<Rearmable>();
					if (rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo && effectiveSupply >= p.Info.SupplyValue))
					{
						var need = CalculateNeed(a);
						if (need >= Info.MinNeedThreshold && need > bestNeed)
						{
							bestNeed = need;
							best = a;
						}
					}
				}

				// Also consider soldiers sheltering inside a garrison building.
				// They aren't in the world (removed when entering Cargo), so FindActorsInCircle
				// misses them. Treat the building's position as the soldier's effective position.
				var garrison = a.TraitOrDefault<GarrisonManager>();
				if (garrison != null && Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner)))
				{
					foreach (var soldier in garrison.ShelterPassengers)
					{
						if (soldier == null || soldier.IsDead)
							continue;

						var rearmable = soldier.TraitOrDefault<Rearmable>();
						if (rearmable == null || rearmable.RearmableAmmoPools.All(p => p.HasFullAmmo))
							continue;

						if (!rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo && effectiveSupply >= p.Info.SupplyValue))
							continue;

						var need = CalculateNeed(soldier);
						if (need < Info.MinNeedThreshold)
							continue;

						if (need > bestNeed)
						{
							bestNeed = need;
							best = soldier;
						}
					}
				}
			}

			return best;
		}

		// Returns need as millipercent (0-1000) using integer math for multiplayer sync safety.
		int CalculateNeed(Actor a)
		{
			var rearmable = a.TraitOrDefault<Rearmable>();
			if (rearmable == null)
				return 0;

			var totalMissing = 0;
			var totalCapacity = 0;
			foreach (var pool in rearmable.RearmableAmmoPools)
			{
				var weight = pool.Info.SupplyValue;
				totalMissing += (pool.Info.Ammo - pool.CurrentAmmoCount) * weight;
				totalCapacity += pool.Info.Ammo * weight;
			}

			if (totalCapacity <= 0)
				return 0;

			return totalMissing * 1000 / totalCapacity;
		}

		bool IsValidTarget(Actor a)
		{
			if (a == null || a.IsDead || !a.IsInWorld || a == self)
				return false;

			if (!Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner)))
				return false;

			var rearmable = a.TraitOrDefault<Rearmable>();
			if (rearmable == null)
				return false;

			if (rearmable.RearmableAmmoPools.All(p => p.HasFullAmmo))
				return false;

			if (!string.IsNullOrEmpty(Info.RearmCondition))
			{
				var ec = a.TraitsImplementing<ExternalCondition>()
					.FirstOrDefault(e => e.Info.Condition == Info.RearmCondition);
				if (ec == null)
					return false;
			}

			var dist = (a.CenterPosition - self.CenterPosition).HorizontalLength;
			if (dist > Info.RearmRange.Length)
				return false;

			return true;
		}

		void SetTarget(Actor target)
		{
			if (currentTarget == target)
				return;

			RevokeTargetCondition();
			currentTarget = target;

			// Shelter passengers in garrison buildings aren't in the world; their
			// CenterPosition is stale. The building they're inside is, by definition,
			// already in range — so skip move-toward and skip granting the rearm
			// condition (invisible anyway, and would leak if the soldier later
			// deploys to a port before our next ResupplyTarget tick).
			if (currentTarget != null && currentTarget.IsInWorld)
			{
				// If target out of range (Hunt mode), move toward it
				var dist = (currentTarget.CenterPosition - self.CenterPosition).HorizontalLength;
				if (dist > Info.RearmRange.Length)
				{
					var move = self.TraitOrDefault<IMove>();
					if (move != null)
					{
						var targetCell = self.World.Map.CellContaining(currentTarget.CenterPosition);
						self.QueueActivity(false, move.MoveTo(targetCell, 2));
					}
				}

				// Grant rearm condition to target
				if (!string.IsNullOrEmpty(Info.RearmCondition))
				{
					targetConditionTrait = currentTarget.TraitsImplementing<ExternalCondition>()
						.FirstOrDefault(e => e.Info.Condition == Info.RearmCondition);
					if (targetConditionTrait != null)
						conditionToken = targetConditionTrait.GrantCondition(currentTarget, this);
				}
			}

			rearmTicks = Info.RearmDelay;
		}

		void RevokeTargetCondition()
		{
			if (conditionToken != Actor.InvalidConditionToken && currentTarget != null &&
				!currentTarget.IsDead && currentTarget.IsInWorld && targetConditionTrait != null)
			{
				targetConditionTrait.TryRevokeCondition(currentTarget, this, conditionToken);
			}

			conditionToken = Actor.InvalidConditionToken;
			targetConditionTrait = null;
		}

		void ResupplyTarget()
		{
			// Note: !IsInWorld is valid here — shelter soldiers in garrison buildings are
			// intentionally removed from world. SetTarget already skipped move-toward and
			// condition-grant for them; we just need to deliver ammo. Only bail on null/dead.
			if (currentTarget == null || currentTarget.IsDead)
			{
				RevokeTargetCondition();
				currentTarget = null;
				return;
			}

			var rearmable = currentTarget.TraitOrDefault<Rearmable>();
			if (rearmable == null)
			{
				RevokeTargetCondition();
				currentTarget = null;
				return;
			}

			// Find pool with greatest need
			AmmoPool bestPool = null;
			var bestNeed = 0f;
			foreach (var pool in rearmable.RearmableAmmoPools)
			{
				if (pool.HasFullAmmo || effectiveSupply < pool.Info.SupplyValue)
					continue;

				var need = 1f - ((float)pool.CurrentAmmoCount / pool.Info.Ammo);
				if (need > bestNeed)
				{
					bestNeed = need;
					bestPool = pool;
				}
			}

			if (bestPool != null)
			{
				// Batch math: deliver one batch of ReloadCount rounds per cycle for
				// SupplyValue cost per batch. See SupplyProvider.ResupplyTarget for
				// the mirrored logic.
				var batchSize = System.Math.Max(1, bestPool.Info.ReloadCount);
				var missing = bestPool.Info.Ammo - bestPool.CurrentAmmoCount;
				var canAfford = effectiveSupply >= bestPool.Info.SupplyValue;
				var giveAmount = canAfford && missing > 0 ? System.Math.Min(batchSize, missing) : 0;

				if (giveAmount > 0 && bestPool.GiveAmmo(currentTarget, giveAmount))
				{
					effectiveSupply -= bestPool.Info.SupplyValue;
					if (effectiveSupply < 0)
						effectiveSupply = 0;

					// Recalculate: how many full units remain? (ceiling division)
					var newCount = effectiveSupply > 0
						? (effectiveSupply + Info.SupplyPerUnit - 1) / Info.SupplyPerUnit
						: 0;

					if (newCount != supplyCount)
					{
						supplyCount = newCount;
						UpdateSupplyCondition();
					}

					if (supplyCount <= 0)
						AutoRefillIfEmpty(self);

					if (!string.IsNullOrEmpty(bestPool.Info.RearmSound))
					{
						var soundPos = currentTarget.IsInWorld ? currentTarget.CenterPosition : self.CenterPosition;
						Game.Sound.PlayToPlayer(SoundType.World, currentTarget.Owner, bestPool.Info.RearmSound, soundPos);
					}
				}
			}

			// Drop target to re-evaluate on next scan
			RevokeTargetCondition();
			currentTarget = null;
			rearmTicks = Info.RearmDelay;
		}

		void UpdateSupplyCondition()
		{
			if (string.IsNullOrEmpty(Info.CargoHasSupplyCondition))
				return;

			if (supplyCount > 0 && hasSupplyToken == Actor.InvalidConditionToken)
				hasSupplyToken = self.GrantCondition(Info.CargoHasSupplyCondition);
			else if (supplyCount <= 0 && hasSupplyToken != Actor.InvalidConditionToken)
				hasSupplyToken = self.RevokeCondition(hasSupplyToken);
		}

		/// <summary>Add supply units. Returns actual amount added (clamped to MaxSupply).</summary>
		public int AddSupply(int count)
		{
			if (count <= 0)
				return 0;

			var available = Info.MaxSupply - supplyCount;
			var actual = System.Math.Min(count, available);

			if (actual <= 0)
				return 0;

			supplyCount += actual;
			effectiveSupply += actual * Info.SupplyPerUnit;
			UpdateSupplyCondition();

			return actual;
		}

		/// <summary>Remove supply units. Returns actual amount removed.</summary>
		public int RemoveSupply(int count)
		{
			if (count <= 0)
				return 0;

			var actual = System.Math.Min(count, supplyCount);
			if (actual <= 0)
				return 0;

			var supplyToRemove = System.Math.Min(actual * Info.SupplyPerUnit, effectiveSupply);

			supplyCount -= actual;
			effectiveSupply -= supplyToRemove;
			if (effectiveSupply < 0)
				effectiveSupply = 0;

			UpdateSupplyCondition();

			return actual;
		}

		// Selection bar: shows supply ratio as gold/orange bar
		float ISelectionBar.GetValue()
		{
			if (supplyCount <= 0 && effectiveSupply <= 0)
				return 0f;

			var maxSupply = Info.MaxSupply * Info.SupplyPerUnit;
			return maxSupply > 0 ? (float)effectiveSupply / maxSupply : 0f;
		}

		bool ISelectionBar.DisplayWhenEmpty => false;

		Color ISelectionBar.GetColor() { return Color.FromArgb(255, 255, 200, 0); }

		void ITransformActorInitModifier.ModifyTransformActorInit(Actor self, TypeDictionary init)
		{
			init.Add(new CargoSupplyInit(Info, supplyCount));
		}

		/// <summary>Credit value of current supply.</summary>
		public int SupplyCreditValue => supplyCount * Info.CreditValuePerUnit;

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "UnloadCargoSupply")
			{
				var amount = (int)order.ExtraData;
				if (amount <= 0 || supplyCount <= 0)
					return;

				amount = System.Math.Min(amount, supplyCount);
				DropSupplyCache(amount);
				return;
			}

			if (order.OrderString == "Restock")
			{
				if (order.Target.Type != TargetType.Actor)
					return;

				var targetActor = order.Target.Actor;
				if (targetActor == null || targetActor.IsDead || !targetActor.IsInWorld)
					return;

				if (targetActor.TraitOrDefault<SupplyProvider>() == null)
					return;

				// If damaged and the host accepts our repair, run Resupply first.
				var health = self.TraitOrDefault<IHealth>();
				var repairable = self.TraitOrDefault<Repairable>();
				var canRepairHere = health != null
					&& health.DamageState > DamageState.Undamaged
					&& repairable != null
					&& repairable.Info.RepairActors.Contains(targetActor.Info.Name);

				if (canRepairHere)
				{
					self.QueueActivity(order.Queued, new Resupply(self, targetActor, new WDist(512)));
					self.QueueActivity(true, new RefillFromHost(self, targetActor));
				}
				else
				{
					self.QueueActivity(order.Queued, new RefillFromHost(self, targetActor));
				}

				self.ShowTargetLines();
				return;
			}

			if (order.OrderString == "DeliverSupply")
			{
				if (order.Target.Type != TargetType.Actor)
					return;

				var targetActor = order.Target.Actor;
				if (targetActor == null || targetActor.IsDead || !targetActor.IsInWorld)
					return;

				if (targetActor.TraitOrDefault<AbsorbsSupplyCache>() == null)
					return;

				var move = self.TraitOrDefault<IMove>();
				if (move == null)
					return;

				var targetCell = self.World.Map.CellContaining(targetActor.CenterPosition);
				self.QueueActivity(order.Queued, move.MoveTo(targetCell, 2));
				self.QueueActivity(true, new CallFunc(() =>
				{
					if (supplyCount <= 0)
						return;

					DropSupplyCache(supplyCount);
				}));
				self.ShowTargetLines();
			}
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				// Restock has a higher priority than Repairable's "Repair" (5) so a
				// right-click on a friendly LC routes to Restock — which itself queues
				// Resupply (for repair) when the truck is damaged.
				yield return new RestockOrderTargeter(Info);
				yield return new DeliverSupplyOrderTargeter(Info);
				yield return new DeployOrderTargeter("DropCargoSupply", 5,
					() => CanDropCache() ? Info.DropCacheCursor : Info.DropCacheBlockedCursor);
			}
		}

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "Restock")
				return new Order(order.OrderID, self, target, queued);

			if (order.OrderID == "DeliverSupply")
				return new Order(order.OrderID, self, target, queued);

			if (order.OrderID == "DropCargoSupply")
				return new Order("UnloadCargoSupply", self, queued) { ExtraData = (uint)supplyCount };

			return null;
		}

		bool CanDropCache()
		{
			if (supplyCount <= 0)
				return false;

			// Allow deploy on an empty cell or one already holding a SUPPLYCACHE (which we'll merge into).
			return self.World.ActorMap.GetActorsAt(self.Location)
				.All(a => a == self || (!a.IsDead && a.Info.Name == Info.SupplyCacheActor));
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			return new Order("UnloadCargoSupply", self, queued) { ExtraData = (uint)supplyCount };
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued)
		{
			return CanDropCache();
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString == "UnloadCargoSupply")
				return Info.DropCacheVoice;

			return null;
		}

		sealed class RestockOrderTargeter : UnitOrderTargeter
		{
			public RestockOrderTargeter(CargoSupplyInfo info)
				: base("Restock", 7, info.DeliverCursor, false, true) { }

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				if (!self.Owner.IsAlliedWith(target.Owner))
					return false;

				// Only docking-aware providers (LC), not ground caches.
				var hostProvider = target.TraitOrDefault<SupplyProvider>();
				if (hostProvider == null || string.IsNullOrEmpty(hostProvider.Info.DockedCondition))
					return false;

				var supply = self.TraitOrDefault<CargoSupply>();
				if (supply == null)
					return false;

				// Only meaningful if the truck has something to gain: refill or repair.
				var notFull = supply.SupplyCount < supply.Info.MaxSupply;
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
			public DeliverSupplyOrderTargeter(CargoSupplyInfo info)
				: base("DeliverSupply", 6, info.DeliverCursor, false, true) { }

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				// Default right-click on a Logistics Center is "go dock and refill" via the
				// standard Repairable / Restock flow. Only Ctrl+click (ForceMove) means
				// "deliver my supply to this LC".
				if (!modifiers.HasModifier(TargetModifiers.ForceMove))
					return false;

				if (target.TraitOrDefault<AbsorbsSupplyCache>() == null)
					return false;

				if (!self.Owner.IsAlliedWith(target.Owner))
					return false;

				var supply = self.TraitOrDefault<CargoSupply>();
				if (supply == null || supply.SupplyCount <= 0)
					return false;

				var sp = target.TraitOrDefault<SupplyProvider>();
				if (sp != null && sp.CurrentSupply >= sp.Info.TotalSupply)
					return false;

				return true;
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				return false;
			}
		}

		/// <summary>Drop supply units as a SUPPLYCACHE at the transport's current location.</summary>
		void DropSupplyCache(int unitsToDrop)
		{
			var supplyAmount = unitsToDrop * Info.SupplyPerUnit;

			// Check if there's already a SUPPLYCACHE on this cell — merge if so
			var existingCache = self.World.ActorMap.GetActorsAt(self.Location)
				.FirstOrDefault(a => !a.IsDead && a.Info.Name == Info.SupplyCacheActor);

			if (existingCache != null)
			{
				var existingProvider = existingCache.TraitOrDefault<SupplyProvider>();
				if (existingProvider != null)
				{
					existingProvider.AddSupply(supplyAmount);
					RemoveSupply(unitsToDrop);
					return;
				}
			}

			// Create new SUPPLYCACHE
			var supplyProviderInfo = self.World.Map.Rules.Actors[Info.SupplyCacheActor]
				.TraitInfoOrDefault<SupplyProviderInfo>();

			if (supplyProviderInfo == null)
				return;

			RemoveSupply(unitsToDrop);

			self.World.AddFrameEndTask(w =>
			{
				var cache = w.CreateActor(Info.SupplyCacheActor, new TypeDictionary
				{
					new LocationInit(self.Location),
					new OwnerInit(self.Owner),
					new SupplyInit(supplyProviderInfo, supplyAmount),
				});
			});
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			AutoRefillIfEmpty(self);
		}

		/// <summary>
		/// When the supply pool is empty, dispatch the configured ResupplyBehavior:
		/// Hold = no-op, Auto = drive to nearest friendly Logistics Center with supply
		/// (falling through to Evacuate if none available), Evacuate = RotateToEdge.
		/// </summary>
		public void AutoRefillIfEmpty(Actor self)
		{
			if (supplyCount > 0 || effectiveSupply > 0)
				return;

			var autoTarget = self.TraitOrDefault<AutoTarget>();
			var behavior = autoTarget?.ResupplyBehaviorValue ?? ResupplyBehavior.Auto;

			switch (behavior)
			{
				case ResupplyBehavior.Hold:
					// Sit. No-op.
					return;

				case ResupplyBehavior.Auto:
					if (TryQueueMoveToLogisticsCenter(self))
						return;
					// No LC available — fall through to Evacuate.
					goto case ResupplyBehavior.Evacuate;

				case ResupplyBehavior.Evacuate:
					var amount = self.GetSellValue();
					self.QueueActivity(false, new RotateToEdge(self, true, amount));
					self.ShowTargetLines();
					return;
			}
		}

		bool TryQueueMoveToLogisticsCenter(Actor self)
		{
			if (self.TraitOrDefault<IMove>() == null)
				return false;

			// Only target docking-aware providers (LC), so an empty truck doesn't try
			// to "dock" at a ground SUPPLYCACHE. Picks the nearest LC with supply left.
			var targetLC = self.World.ActorsHavingTrait<SupplyProvider>()
				.Where(a => !a.IsDead && a.IsInWorld
					&& Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner))
					&& a.Trait<SupplyProvider>().CurrentSupply > 0
					&& !string.IsNullOrEmpty(a.Trait<SupplyProvider>().Info.DockedCondition))
				.ClosestToIgnoringPath(self);

			if (targetLC == null)
				return false;

			// Drive in and dock; the Restock activity handles the per-pip transfer
			// once the truck is in the LC's UNITDOCKED proximity range.
			self.QueueActivity(false, new RefillFromHost(self, targetLC));
			self.ShowTargetLines();
			return true;
		}
	}

	public class CargoSupplyInit : ValueActorInit<int>
	{
		public CargoSupplyInit(TraitInfo info, int value)
			: base(info, value) { }
	}
}
