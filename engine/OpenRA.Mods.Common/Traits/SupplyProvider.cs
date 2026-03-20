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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Provides targeted single-unit resupply to nearby allied units with AmmoPool.",
		"Picks the unit with greatest need (lowest ammo %), gives 1 pip, then re-evaluates.")]
	public class SupplyProviderInfo : PausableConditionalTraitInfo
	{
		[Desc("Maximum resupply range.")]
		public readonly WDist Range = new WDist(5120);

		[Desc("Ticks between ammo increments (one pip per cycle).")]
		public readonly int RearmDelay = 25;

		[Desc("Total supply capacity.")]
		public readonly int TotalSupply = 500;

		[Desc("Auto-restock when supply drops below this threshold.")]
		public readonly int RestockThreshold = 50;

		[ActorReference]
		[Desc("Actor types where the supply provider can restock.")]
		public readonly HashSet<string> RestockActors = new HashSet<string>();

		[Desc("Condition to grant to the unit currently being rearmed.")]
		public readonly string RearmCondition = "replenish-soldiers";

		[Desc("How often (in ticks) to scan for new targets.")]
		public readonly int ScanInterval = 7;

		[Desc("Relationships of actors that can be resupplied.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally;

		public override object Create(ActorInitializer init) { return new SupplyProvider(init, this); }
	}

	public class SupplyProvider : PausableConditionalTrait<SupplyProviderInfo>, ITick,
		INotifyCreated, ITransformActorInitModifier, ISelectionBar
	{
		readonly Actor self;
		int currentSupply;
		int rearmTicks;
		int scanTicks;

		Actor currentTarget;
		ExternalCondition targetConditionTrait;
		int conditionToken = Actor.InvalidConditionToken;
		bool restocking;

		public int CurrentSupply => currentSupply;

		public SupplyProvider(ActorInitializer init, SupplyProviderInfo info)
			: base(info)
		{
			self = init.Self;
			currentSupply = init.GetValue<SupplyInit, int>(info, info.TotalSupply);
		}

		void INotifyCreated.Created(Actor self) { }

		void ITransformActorInitModifier.ModifyTransformActorInit(Actor self, TypeDictionary init)
		{
			init.Add(new SupplyInit(Info, currentSupply));
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitPaused || IsTraitDisabled)
			{
				RevokeTargetCondition();
				currentTarget = null;
				return;
			}

			if (restocking)
				return;

			// Check if we need to restock
			if (currentSupply <= 0 || (currentSupply < Info.RestockThreshold && currentTarget == null))
			{
				RevokeTargetCondition();
				currentTarget = null;

				if (Info.RestockActors.Count > 0 && !restocking)
					TryRestock();

				return;
			}

			// Periodic scan — always re-evaluate greatest need
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
					// After giving 1 pip, immediately re-evaluate who needs it most
					scanTicks = 0;
				}
			}
		}

		void UpdateTarget()
		{
			// Always re-evaluate — pick unit with greatest need
			var bestTarget = FindGreatestNeedTarget();

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

		Actor FindGreatestNeedTarget()
		{
			Actor best = null;
			var bestNeed = 0f;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, Info.Range))
			{
				if (!IsValidTarget(a))
					continue;

				var need = CalculateNeed(a);
				if (need > bestNeed)
				{
					bestNeed = need;
					best = a;
				}
			}

			return best;
		}

		float CalculateNeed(Actor a)
		{
			var rearmable = a.TraitOrDefault<Rearmable>();
			if (rearmable == null)
				return 0f;

			// Need = total missing ammo weighted by SupplyValue
			// Higher = more need
			var totalMissing = 0f;
			var totalCapacity = 0f;
			foreach (var pool in rearmable.RearmableAmmoPools)
			{
				var weight = pool.Info.SupplyValue;
				totalMissing += (pool.Info.Ammo - pool.CurrentAmmoCount) * weight;
				totalCapacity += pool.Info.Ammo * weight;
			}

			if (totalCapacity <= 0)
				return 0f;

			return totalMissing / totalCapacity;
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

			// Must have ExternalCondition for the rearm condition
			if (!string.IsNullOrEmpty(Info.RearmCondition))
			{
				var ec = a.TraitsImplementing<ExternalCondition>()
					.FirstOrDefault(e => e.Info.Condition == Info.RearmCondition);
				if (ec == null)
					return false;
			}

			// Must be in range
			var dist = (a.CenterPosition - self.CenterPosition).HorizontalLength;
			if (dist > Info.Range.Length)
				return false;

			return true;
		}

		void SetTarget(Actor target)
		{
			if (currentTarget == target)
				return;

			RevokeTargetCondition();
			currentTarget = target;

			// Grant condition to new target
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

			// Find the pool with the greatest need (lowest ammo %)
			AmmoPool bestPool = null;
			var bestNeed = 0f;
			foreach (var pool in rearmable.RearmableAmmoPools)
			{
				if (pool.HasFullAmmo || currentSupply < pool.Info.SupplyValue)
					continue;

				var need = 1f - ((float)pool.CurrentAmmoCount / pool.Info.Ammo);
				if (need > bestNeed)
				{
					bestNeed = need;
					bestPool = pool;
				}
			}

			if (bestPool != null && bestPool.GiveAmmo(currentTarget, 1))
			{
				currentSupply -= bestPool.Info.SupplyValue;

				if (!string.IsNullOrEmpty(bestPool.Info.RearmSound))
					Game.Sound.PlayToPlayer(SoundType.World, currentTarget.Owner, bestPool.Info.RearmSound, currentTarget.CenterPosition);
			}

			// After giving 1 pip, drop target to re-evaluate on next scan
			RevokeTargetCondition();
			currentTarget = null;
			rearmTicks = Info.RearmDelay;
		}

		void TryRestock()
		{
			if (Info.RestockActors.Count == 0)
				return;

			// Find nearest restock target by actor name (no RearmsUnits dependency)
			var restockTarget = self.World.Actors
				.Where(a => !a.IsDead && a.IsInWorld
					&& a.Owner == self.Owner
					&& Info.RestockActors.Contains(a.Info.Name))
				.ClosestTo(self);

			if (restockTarget != null)
			{
				restocking = true;
				var move = self.Trait<IMove>();

				// Drive to the logistics center
				var targetCell = self.World.Map.CellContaining(restockTarget.CenterPosition);
				self.QueueActivity(false, move.MoveTo(targetCell, ignoreActor: restockTarget));

				// Wait briefly to simulate restocking
				self.QueueActivity(new Wait(25));

				// Refill supply
				self.QueueActivity(new CallFunc(() =>
				{
					currentSupply = Info.TotalSupply;
					restocking = false;
				}));

				// Follow rally point if the restock target has one
				var rp = restockTarget.TraitOrDefault<RallyPoint>();
				if (rp != null && rp.Path.Count > 0)
					foreach (var cell in rp.Path)
						self.QueueActivity(move.MoveTo(cell, 1));
			}
		}

		/// <summary>Deducts supply when ammo is given directly (e.g., by QuickRearm).</summary>
		public bool DeductSupply(int amount)
		{
			if (currentSupply < amount)
				return false;

			currentSupply -= amount;
			return true;
		}

		float ISelectionBar.GetValue()
		{
			if (IsTraitDisabled)
				return 0f;

			return (float)currentSupply / Info.TotalSupply;
		}

		bool ISelectionBar.DisplayWhenEmpty => true;

		Color ISelectionBar.GetColor() { return Color.FromArgb(255, 255, 200, 0); }
	}

	public class SupplyInit : ValueActorInit<int>
	{
		public SupplyInit(TraitInfo info, int value)
			: base(info, value) { }
	}
}
