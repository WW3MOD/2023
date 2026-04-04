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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Tracks supply units as numeric cargo weight. Any transport with this trait and loaded supply",
		"auto-rearms nearby allied units. Supply consumes Cargo weight (1 unit = WeightPerUnit).")]
	public class CargoSupplyInfo : TraitInfo, Requires<CargoInfo>
	{
		[Desc("Cargo weight consumed per supply unit.")]
		public readonly int WeightPerUnit = 1;

		[Desc("Ammo supply value per supply unit (how much ammo one unit can give before being consumed).")]
		public readonly int SupplyPerUnit = 50;

		[Desc("Number of supply units loaded at spawn (before template system). 0 = none.")]
		public readonly int InitialSupply = 0;

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

		public override object Create(ActorInitializer init) { return new CargoSupply(init, this); }
	}

	public class CargoSupply : ITick, INotifyCreated, ISelectionBar, ITransformActorInitModifier, IResolveOrder
	{
		public readonly CargoSupplyInfo Info;
		readonly Actor self;

		Cargo cargo;

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
		public int TotalCapacity => cargo != null ? (cargo.Info.MaxWeight / Info.WeightPerUnit) * Info.SupplyPerUnit : 0;

		public CargoSupply(ActorInitializer init, CargoSupplyInfo info)
		{
			Info = info;
			self = init.Self;

			// Load initial supply from init (e.g., from template system or transform), fallback to InitialSupply
			var initialCount = init.GetValue<CargoSupplyInit, int>(info, info.InitialSupply);
			supplyCount = initialCount;
			effectiveSupply = supplyCount * info.SupplyPerUnit;
		}

		void INotifyCreated.Created(Actor self)
		{
			cargo = self.Trait<Cargo>();

			// Reserve weight in cargo for our supply
			if (supplyCount > 0)
				cargo.ReserveSupplyWeight(supplyCount * Info.WeightPerUnit);

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
				if (!IsValidTarget(a))
					continue;

				var rearmable = a.Trait<Rearmable>();
				if (!rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo && effectiveSupply >= p.Info.SupplyValue))
					continue;

				var need = CalculateNeed(a);
				if (need < Info.MinNeedThreshold)
					continue;

				if (need > bestNeed)
				{
					bestNeed = need;
					best = a;
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

			// If target out of range (Hunt mode), move toward it
			if (currentTarget != null)
			{
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
			}

			// Grant rearm condition to target
			if (!string.IsNullOrEmpty(Info.RearmCondition) && currentTarget != null)
			{
				targetConditionTrait = currentTarget.TraitsImplementing<ExternalCondition>()
					.FirstOrDefault(e => e.Info.Condition == Info.RearmCondition);
				if (targetConditionTrait != null)
					conditionToken = targetConditionTrait.GrantCondition(currentTarget, this);
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
			if (currentTarget == null || currentTarget.IsDead || !currentTarget.IsInWorld)
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
				var missing = bestPool.Info.Ammo - bestPool.CurrentAmmoCount;
				var giveAmount = System.Math.Max(1, bestPool.Info.Ammo / 50);
				giveAmount = System.Math.Min(giveAmount, missing);
				giveAmount = System.Math.Min(giveAmount, effectiveSupply / System.Math.Max(1, bestPool.Info.SupplyValue));

				if (giveAmount > 0 && bestPool.GiveAmmo(currentTarget, giveAmount))
				{
					effectiveSupply -= bestPool.Info.SupplyValue * giveAmount;

					// When a supply unit's worth of ammo is depleted, free the cargo weight
					var unitsConsumed = 0;
					while (effectiveSupply < supplyCount * Info.SupplyPerUnit - (unitsConsumed + 1) * Info.SupplyPerUnit
						&& supplyCount > 0)
					{
						// This unit is fully consumed
						break;
					}

					// Recalculate: how many full units remain?
					var newCount = effectiveSupply > 0
						? (effectiveSupply + Info.SupplyPerUnit - 1) / Info.SupplyPerUnit  // ceiling division
						: 0;

					if (newCount < supplyCount)
					{
						var freed = supplyCount - newCount;
						supplyCount = newCount;
						cargo.FreeSupplyWeight(freed * Info.WeightPerUnit);
						UpdateSupplyCondition();
					}

					if (!string.IsNullOrEmpty(bestPool.Info.RearmSound))
						Game.Sound.PlayToPlayer(SoundType.World, currentTarget.Owner, bestPool.Info.RearmSound, currentTarget.CenterPosition);
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

		/// <summary>Add supply units. Returns actual amount added (may be less if cargo full).</summary>
		public int AddSupply(int count)
		{
			if (cargo == null || count <= 0)
				return 0;

			// Check available cargo weight
			var availableWeight = cargo.AvailableWeight;
			var maxUnits = availableWeight / Info.WeightPerUnit;
			var actual = System.Math.Min(count, maxUnits);

			if (actual <= 0)
				return 0;

			supplyCount += actual;
			effectiveSupply += actual * Info.SupplyPerUnit;
			cargo.ReserveSupplyWeight(actual * Info.WeightPerUnit);
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

			// Calculate effective supply to remove (proportional to remaining)
			var supplyToRemove = actual * Info.SupplyPerUnit;
			// If partially consumed, remove proportionally
			if (effectiveSupply < supplyCount * Info.SupplyPerUnit)
			{
				// Remove from the "fullest" units first, so take full SupplyPerUnit per unit removed
				supplyToRemove = System.Math.Min(supplyToRemove, effectiveSupply);
			}

			supplyCount -= actual;
			effectiveSupply -= supplyToRemove;
			if (effectiveSupply < 0)
				effectiveSupply = 0;

			cargo.FreeSupplyWeight(actual * Info.WeightPerUnit);
			UpdateSupplyCondition();

			return actual;
		}

		// Selection bar: shows supply ratio as gold/orange bar
		float ISelectionBar.GetValue()
		{
			if (supplyCount <= 0 && effectiveSupply <= 0)
				return 0f;

			var maxSupply = cargo != null ? (cargo.Info.MaxWeight / Info.WeightPerUnit) * Info.SupplyPerUnit : 1;
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
			if (order.OrderString != "UnloadCargoSupply")
				return;

			var amount = (int)order.ExtraData;
			if (amount <= 0 || supplyCount <= 0)
				return;

			amount = System.Math.Min(amount, supplyCount);
			DropSupplyCache(amount);
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
	}

	public class CargoSupplyInit : ValueActorInit<int>
	{
		public CargoSupplyInit(TraitInfo info, int value)
			: base(info, value) { }
	}
}
