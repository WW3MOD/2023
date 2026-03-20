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
	[Desc("Provides targeted single-unit resupply to nearby allied units with AmmoPool. Distance-based reload speed, limited supply capacity.")]
	public class SupplyProviderInfo : PausableConditionalTraitInfo
	{
		[Desc("Maximum resupply range.")]
		public readonly WDist Range = new WDist(6144);

		[Desc("Distance for fastest resupply speed.")]
		public readonly WDist MinRange = new WDist(1024);

		[Desc("Ticks between ammo increments at MinRange distance.")]
		public readonly int BaseDelay = 15;

		[Desc("At max range, delay is BaseDelay * MaxDelayMultiplier.")]
		public readonly int MaxDelayMultiplier = 4;

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

			// Read transferred supply from SupplyInit (when deployed from truck to cache)
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

			// Handle restocking
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

			// Periodic scan for targets
			if (--scanTicks <= 0)
			{
				scanTicks = Info.ScanInterval;
				UpdateTarget();
			}

			// Resupply current target
			if (currentTarget != null)
			{
				if (--rearmTicks <= 0)
					ResupplyTarget();
			}
		}

		void UpdateTarget()
		{
			// Check if current target is still valid
			if (currentTarget != null)
			{
				if (!IsValidTarget(currentTarget))
				{
					RevokeTargetCondition();
					currentTarget = null;
				}
				else
					return; // Keep current target
			}

			// Find closest valid target
			var bestTarget = FindClosestTarget();
			if (bestTarget == null)
				return;

			SetTarget(bestTarget);
		}

		Actor FindClosestTarget()
		{
			var range = Info.Range;
			Actor closest = null;
			var closestDist = long.MaxValue;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, range))
			{
				if (!IsValidTarget(a))
					continue;

				var dist = (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared;
				if (dist < closestDist)
				{
					closestDist = dist;
					closest = a;
				}
			}

			return closest;
		}

		bool IsValidTarget(Actor a)
		{
			if (a == null || a.IsDead || !a.IsInWorld || a == self)
				return false;

			if (!Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner)))
				return false;

			// Must have Rearmable trait
			var rearmable = a.TraitOrDefault<Rearmable>();
			if (rearmable == null)
				return false;

			// Must need ammo in at least one pool
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

			// Calculate initial delay based on distance
			rearmTicks = CalculateDelay();
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

		int CalculateDelay()
		{
			if (currentTarget == null)
				return Info.BaseDelay;

			var dist = (currentTarget.CenterPosition - self.CenterPosition).HorizontalLength;
			var minDist = Info.MinRange.Length;
			if (minDist <= 0)
				minDist = 1;

			var multiplier = (float)dist / minDist;
			multiplier = multiplier.Clamp(1f, Info.MaxDelayMultiplier);

			return (int)(Info.BaseDelay * multiplier);
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

			var gaveSomething = false;
			foreach (var pool in rearmable.RearmableAmmoPools)
			{
				if (pool.HasFullAmmo || currentSupply <= 0)
					continue;

				var supplyNeeded = pool.Info.SupplyValue;
				if (currentSupply < supplyNeeded)
					continue;

				if (pool.GiveAmmo(currentTarget, 1))
				{
					currentSupply -= supplyNeeded;
					gaveSomething = true;

					if (!string.IsNullOrEmpty(pool.Info.RearmSound))
						Game.Sound.PlayToPlayer(SoundType.World, currentTarget.Owner, pool.Info.RearmSound, currentTarget.CenterPosition);

					break; // One ammo increment per tick cycle
				}
			}

			if (!gaveSomething || rearmable.RearmableAmmoPools.All(p => p.HasFullAmmo))
			{
				// Target is full or we couldn't give anything — drop it
				RevokeTargetCondition();
				currentTarget = null;
				scanTicks = 0; // Immediate rescan
			}

			// Recalculate delay (distance may have changed)
			rearmTicks = CalculateDelay();
		}

		void TryRestock()
		{
			if (Info.RestockActors.Count == 0)
				return;

			var restockTarget = self.World.ActorsHavingTrait<RearmsUnits>()
				.Where(a => !a.IsDead && a.IsInWorld
					&& a.Owner == self.Owner
					&& Info.RestockActors.Contains(a.Info.Name))
				.ClosestTo(self);

			if (restockTarget != null)
			{
				restocking = true;
				self.QueueActivity(false, new Resupply(self, restockTarget, restockTarget.Trait<RearmsUnits>().Info.CloseEnough));
				self.QueueActivity(new CallFunc(() =>
				{
					currentSupply = Info.TotalSupply;
					restocking = false;
				}));
			}
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
