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

		[Desc("Minimum ammo need percentage (0-1) to consider a unit for resupply. Units above this % full are skipped.")]
		public readonly float MinNeedThreshold = 0.05f;

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

		[Desc("Total credit value of a full supply load. Missing supply reduces sell/rotation value proportionally.")]
		public readonly int SupplyCreditValue = 0;

		[GrantedConditionReference]
		[Desc("Condition granted when supply is above 66%.")]
		public readonly string SupplyHighCondition = null;

		[GrantedConditionReference]
		[Desc("Condition granted when supply is between 33% and 66%.")]
		public readonly string SupplyMediumCondition = null;

		[GrantedConditionReference]
		[Desc("Condition granted when supply is at or below 33%.")]
		public readonly string SupplyLowCondition = null;

		public override object Create(ActorInitializer init) { return new SupplyProvider(init, this); }
	}

	public class SupplyProvider : PausableConditionalTrait<SupplyProviderInfo>, ITick,
		ITransformActorInitModifier, ISelectionBar, ICargoCanLoadFilter
	{
		readonly Actor self;
		int currentSupply;
		int rearmTicks;
		int scanTicks;

		Actor currentTarget;
		ExternalCondition targetConditionTrait;
		int conditionToken = Actor.InvalidConditionToken;
		bool restocking;

		int supplyHighToken = Actor.InvalidConditionToken;
		int supplyMediumToken = Actor.InvalidConditionToken;
		int supplyLowToken = Actor.InvalidConditionToken;

		public int CurrentSupply => currentSupply;

		public SupplyProvider(ActorInitializer init, SupplyProviderInfo info)
			: base(info)
		{
			self = init.Self;
			currentSupply = init.GetValue<SupplyInit, int>(info, info.TotalSupply);
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			// Stagger so multiple Logistics Centers / supply providers don't all scan on the same tick.
			scanTicks = self.World.SharedRandom.Next(0, Info.ScanInterval);
			UpdateSupplyConditions();
		}

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
			var bestTarget = FindGreatestNeedTarget(out var hasUnaffordableTargets);

			if (bestTarget == null)
			{
				// When in Hunt stance and no nearby targets, seek out units flagged as needing resupply
				if (bestTarget == null)
				{
					var autoTarget = self.TraitOrDefault<AutoTarget>();
					if (autoTarget != null && autoTarget.EngagementStanceValue >= EngagementStance.Hunt)
						bestTarget = FindNeedsResupplyTarget();
				}
			}

			if (bestTarget == null)
			{
				if (currentTarget != null)
				{
					RevokeTargetCondition();
					currentTarget = null;
				}

				// We have supply but can't afford to help anyone nearby → restock
				if (hasUnaffordableTargets && Info.RestockActors.Count > 0 && !restocking)
					TryRestock();

				return;
			}

			SetTarget(bestTarget);
		}

		/// <summary>
		/// Find a friendly unit anywhere on the map that has NeedsResupply flag set.
		/// Used when the supply truck is in Hunt stance.
		/// </summary>
		Actor FindNeedsResupplyTarget()
		{
			return self.World.ActorsHavingTrait<AmmoPool>()
				.Where(a => !a.IsDead && a.IsInWorld && a != self
					&& Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner))
					&& a.TraitsImplementing<AmmoPool>().Any(ap => ap.NeedsResupply)
					&& a.TraitOrDefault<Rearmable>() != null)
				.ClosestToIgnoringPath(self);
		}

		Actor FindGreatestNeedTarget(out bool hasUnaffordableTargets)
		{
			Actor best = null;
			var bestNeed = 0f;
			hasUnaffordableTargets = false;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, Info.Range))
			{
				if (!IsValidTarget(a))
					continue;

				// Ammo target path
				var rearmable = a.TraitOrDefault<Rearmable>();
				if (rearmable != null && rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo))
				{
					// Check if we can afford any of this target's non-full ammo pools
					if (!rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo && currentSupply >= p.Info.SupplyValue))
					{
						hasUnaffordableTargets = true;
						continue;
					}

					var need = CalculateNeed(a);

					// Skip units that are nearly full (e.g., 499/500 ammo)
					if (need < Info.MinNeedThreshold)
						continue;

					if (need > bestNeed)
					{
						bestNeed = need;
						best = a;
					}

					continue;
				}

				// CargoSupply target path (supply truck refilling at LC)
				var cargoSupply = a.TraitOrDefault<CargoSupply>();
				if (cargoSupply != null && cargoSupply.SupplyCount < cargoSupply.Info.MaxSupply)
				{
					// Need = fraction of capacity missing.
					var capacity = cargoSupply.Info.MaxSupply;
					var missing = capacity - cargoSupply.SupplyCount;
					var need = (float)missing / capacity;

					if (need < Info.MinNeedThreshold)
						continue;

					if (need > bestNeed)
					{
						bestNeed = need;
						best = a;
					}
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

			// Must be in range
			var dist = (a.CenterPosition - self.CenterPosition).HorizontalLength;
			if (dist > Info.Range.Length)
				return false;

			// Ammo target: Rearmable with at least one non-full pool.
			var rearmable = a.TraitOrDefault<Rearmable>();
			if (rearmable != null && rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo))
			{
				if (!string.IsNullOrEmpty(Info.RearmCondition))
				{
					var ec = a.TraitsImplementing<ExternalCondition>()
						.FirstOrDefault(e => e.Info.Condition == Info.RearmCondition);
					if (ec == null)
						return false;
				}

				return true;
			}

			// Supply truck target: CargoSupply with free capacity. Truck must be standing still —
			// receiving supply while moving feels wrong (no time to dock and load).
			var cargoSupply = a.TraitOrDefault<CargoSupply>();
			if (cargoSupply != null && cargoSupply.SupplyCount < cargoSupply.Info.MaxSupply)
			{
				var mobile = a.TraitOrDefault<Mobile>();
				if (mobile != null && mobile.CurrentMovementTypes.HasFlag(MovementType.Horizontal))
					return false;

				return true;
			}

			return false;
		}

		void SetTarget(Actor target)
		{
			if (currentTarget == target)
				return;

			RevokeTargetCondition();
			currentTarget = target;

			// If target is out of range (Hunt mode found a distant flagged unit), move toward it
			if (currentTarget != null)
			{
				var dist = (currentTarget.CenterPosition - self.CenterPosition).HorizontalLength;
				if (dist > Info.Range.Length)
				{
					var move = self.TraitOrDefault<IMove>();
					if (move != null)
					{
						var targetCell = self.World.Map.CellContaining(currentTarget.CenterPosition);
						self.QueueActivity(false, move.MoveTo(targetCell, 2));
					}
				}
			}

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

			// CargoSupply target (supply truck refilling at LC): transfer one supply unit per cycle.
			var cargoSupply = currentTarget.TraitOrDefault<CargoSupply>();
			if (cargoSupply != null && cargoSupply.SupplyCount < cargoSupply.Info.MaxSupply)
			{
				// Cost of one truck-pip in LC supply units.
				var costPerUnit = cargoSupply.Info.SupplyPerUnit;
				if (currentSupply >= costPerUnit)
				{
					var added = cargoSupply.AddSupply(1);
					if (added > 0)
					{
						currentSupply -= costPerUnit;
						UpdateSupplyConditions();
					}
				}

				// Drop target to re-evaluate on next scan.
				RevokeTargetCondition();
				currentTarget = null;
				rearmTicks = Info.RearmDelay;
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

			if (bestPool != null)
			{
				// Scale ammo per cycle by pool capacity — large pools get more per tick
				// Targets ~50 cycles to fill from empty (e.g., 500-pool gets 10 per cycle)
				var missing = bestPool.Info.Ammo - bestPool.CurrentAmmoCount;
				var giveAmount = System.Math.Max(1, bestPool.Info.Ammo / 50);

				// Don't give more than what's missing or what we can afford
				giveAmount = System.Math.Min(giveAmount, missing);
				giveAmount = System.Math.Min(giveAmount, currentSupply / System.Math.Max(1, bestPool.Info.SupplyValue));

				if (giveAmount > 0 && bestPool.GiveAmmo(currentTarget, giveAmount))
				{
					currentSupply -= bestPool.Info.SupplyValue * giveAmount;
					UpdateSupplyConditions();

					if (!string.IsNullOrEmpty(bestPool.Info.RearmSound))
						Game.Sound.PlayToPlayer(SoundType.World, currentTarget.Owner, bestPool.Info.RearmSound, currentTarget.CenterPosition);
				}
			}

			// After giving ammo, drop target to re-evaluate on next scan
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
				.ClosestToIgnoringPath(self);

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
					UpdateSupplyConditions();
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
			UpdateSupplyConditions();
			return true;
		}

		/// <summary>Sets supply to an exact amount (e.g., for DropsCrate zeroing out).</summary>
		public void SetSupply(int amount)
		{
			currentSupply = amount.Clamp(0, Info.TotalSupply);
			UpdateSupplyConditions();
		}

		/// <summary>Adds supply (e.g., merging a dropped supply crate into existing cache). Can exceed TotalSupply if needed.</summary>
		public void AddSupply(int amount)
		{
			currentSupply += amount;
			UpdateSupplyConditions();
		}

		void UpdateSupplyConditions()
		{
			var ratio = Info.TotalSupply > 0 ? (float)currentSupply / Info.TotalSupply : 0f;

			if (!string.IsNullOrEmpty(Info.SupplyHighCondition))
			{
				if (ratio > 0.66f && supplyHighToken == Actor.InvalidConditionToken)
					supplyHighToken = self.GrantCondition(Info.SupplyHighCondition);
				else if (ratio <= 0.66f && supplyHighToken != Actor.InvalidConditionToken)
					supplyHighToken = self.RevokeCondition(supplyHighToken);
			}

			if (!string.IsNullOrEmpty(Info.SupplyMediumCondition))
			{
				if (ratio > 0.33f && ratio <= 0.66f && supplyMediumToken == Actor.InvalidConditionToken)
					supplyMediumToken = self.GrantCondition(Info.SupplyMediumCondition);
				else if ((ratio <= 0.33f || ratio > 0.66f) && supplyMediumToken != Actor.InvalidConditionToken)
					supplyMediumToken = self.RevokeCondition(supplyMediumToken);
			}

			if (!string.IsNullOrEmpty(Info.SupplyLowCondition))
			{
				if (ratio <= 0.33f && supplyLowToken == Actor.InvalidConditionToken)
					supplyLowToken = self.GrantCondition(Info.SupplyLowCondition);
				else if (ratio > 0.33f && supplyLowToken != Actor.InvalidConditionToken)
					supplyLowToken = self.RevokeCondition(supplyLowToken);
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

		bool ICargoCanLoadFilter.CanLoadPassenger(Actor self, Actor passenger)
		{
			return currentSupply > 0;
		}

		/// <summary>Credit value of missing supply, proportional to SupplyCreditValue.</summary>
		public int MissingSupplyValue
		{
			get
			{
				if (Info.SupplyCreditValue <= 0 || Info.TotalSupply <= 0)
					return 0;

				var missing = Info.TotalSupply - currentSupply;
				return (int)((long)Info.SupplyCreditValue * missing / Info.TotalSupply);
			}
		}
	}

	public class SupplyInit : ValueActorInit<int>
	{
		public SupplyInit(TraitInfo info, int value)
			: base(info, value) { }
	}
}
