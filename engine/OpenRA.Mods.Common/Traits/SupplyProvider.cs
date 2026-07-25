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

		[Desc("When a stationary provider's supply falls below this value it removes itself",
			"from the world — the stationary analog of a truck driving home when almost empty.",
			"A near-empty cache left on the field clutters it and misleads units into pathing",
			"to a box that can no longer hand over a batch. 0 disables self-removal; Logistics",
			"Centers and trucks leave it 0 (trucks evacuate via DropsSupplyCache instead).",
			"Set 50 on SUPPLYCACHE to mirror TRUK's RestockThreshold 'almost empty' value.")]
		public readonly int RemoveBelowSupply = 0;

		[Desc("When the provider holds a residue too small for any reachable unit to use",
			"(no needy unit in range can be given even one batch), treat it as empty so",
			"its transport (DropsSupplyCache) evacuates instead of parking forever.",
			"Intended for supply trucks; leave false on Logistics Centers and caches.")]
		public readonly bool EvacuateOnUnusableResidue = false;

		[ActorReference]
		[Desc("Actor types where the supply provider can restock.")]
		public readonly HashSet<string> RestockActors = new HashSet<string>();

		[Desc("Condition to grant to the unit currently being rearmed.")]
		public readonly string RearmCondition = "replenish-soldiers";

		[Desc("External condition the target must already have for it to be considered docked",
			"with this provider. Empty disables the docking gate (any rearmable in range qualifies).",
			"Logistics Centers should set this to 'unit.docked' so trucks/vehicles must dock to refill;",
			"ground caches like SUPPLYCACHE should leave it empty for passive proximity refill.")]
		public readonly string DockedCondition = null;

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

		[GrantedConditionReference]
		[Desc("Condition granted to self while any supply is available (currentSupply > 0).")]
		public readonly string SupplyAnyCondition = null;

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

		// Latched true when EvacuateOnUnusableResidue and the remaining supply is a
		// residue no reachable unit can utilize. Cleared on replenish or full drain.
		bool residueUnusable;

		int supplyHighToken = Actor.InvalidConditionToken;
		int supplyMediumToken = Actor.InvalidConditionToken;
		int supplyLowToken = Actor.InvalidConditionToken;
		int supplyAnyToken = Actor.InvalidConditionToken;

		public int CurrentSupply => currentSupply;

		/// <summary>True while a residue too small for any reachable unit to use is being held.</summary>
		public bool ResidueUnusable => residueUnusable;

		/// <summary>
		/// A provider counts as empty when it is genuinely drained, or (for trucks with
		/// EvacuateOnUnusableResidue) when the remaining supply is a residue no reachable
		/// unit can utilize. Transports use this to trigger the same evacuate flow an
		/// actually-empty truck uses.
		/// </summary>
		public bool CountsAsEmpty => currentSupply <= 0 || residueUnusable;

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

			// A stationary provider flagged to self-remove when almost empty despawns once its
			// pool drops below the threshold — the stationary analog of a truck driving home
			// when low (DropsSupplyCache). Disposal path mirrors AbsorbsSupplyCache.cs.
			if (Info.RemoveBelowSupply > 0 && currentSupply < Info.RemoveBelowSupply)
			{
				self.World.AddFrameEndTask(w => { if (!self.IsDead && self.IsInWorld) self.Dispose(); });
				return;
			}

			// Drained: clear the residue latch (a truly empty provider is not "residue"),
			// and hand off to restock if this provider self-restocks.
			if (currentSupply <= 0)
			{
				RevokeTargetCondition();
				currentTarget = null;
				residueUnusable = false;

				if (ShouldSelfRestock())
					TryRestock();

				return;
			}

			// Below the restock threshold with no active customer: a provider that drives
			// itself home reserves its remaining supply for the trip and stops serving. A
			// residue-evacuating truck with no trip home (Evacuate stance) keeps serving
			// down to the last usable batch — once the residue is genuinely unusable,
			// CountsAsEmpty carries it to evac. Reserving supply it will never restock would
			// just strand it, amber-barred, next to a unit it could still help.
			if (currentSupply < Info.RestockThreshold && currentTarget == null && !KeepServingBelowThreshold())
			{
				RevokeTargetCondition();
				currentTarget = null;

				if (ShouldSelfRestock())
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

			// Residue-unusable latch, decided by the same pure predicate the tests pin.
			// bestTarget != null means a reachable unit we can afford met MinNeedThreshold
			// (serviceable); hasUnaffordableTargets means a reachable needy unit exists that
			// we can't afford. A null verdict (no demand at all) leaves the latch unchanged
			// so an already-evacuating truck stays evacuating.
			if (Info.EvacuateOnUnusableResidue)
			{
				var verdict = ResidueVerdict(currentSupply, bestTarget != null, hasUnaffordableTargets);
				if (verdict.HasValue)
					residueUnusable = verdict.Value;
			}

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
				// (unless this provider evacuates on unusable residue and is set to
				// Evacuate — then the transport drives it off-map instead).
				if (hasUnaffordableTargets && ShouldSelfRestock())
					TryRestock();

				return;
			}

			SetTarget(bestTarget);
		}

		/// <summary>
		/// Whether this provider should drive itself off to a restock host. A provider set
		/// to Evacuate never self-restocks — its transport evacuates it off-map instead.
		/// </summary>
		bool ShouldSelfRestock()
		{
			if (Info.RestockActors.Count == 0 || restocking)
				return false;

			var behavior = self.TraitOrDefault<AutoTarget>()?.ResupplyBehaviorValue ?? ResupplyBehavior.Auto;
			return behavior != ResupplyBehavior.Evacuate;
		}

		/// <summary>
		/// A residue-evacuating provider that will NOT drive itself home (Evacuate stance, or
		/// no restock host) has no trip to reserve supply for — it keeps serving below the
		/// restock threshold until the residue becomes genuinely unusable, then evacuates.
		/// Self-restocking providers still reserve their remaining supply for the drive home.
		/// </summary>
		bool KeepServingBelowThreshold()
		{
			return Info.EvacuateOnUnusableResidue && !ShouldSelfRestock();
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
				if (IsValidTarget(a))
				{
					var rearmable = a.TraitOrDefault<Rearmable>();
					if (rearmable != null && rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo))
					{
						// Check if we can afford any of this target's non-full ammo pools
						if (!rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo && currentSupply >= p.Info.SupplyValue))
						{
							hasUnaffordableTargets = true;
						}
						else
						{
							var need = CalculateNeed(a);

							// Skip units that are nearly full (e.g., 499/500 ammo)
							if (need >= Info.MinNeedThreshold && need > bestNeed)
							{
								bestNeed = need;
								best = a;
							}
						}
					}

				}

				// Also consider soldiers sheltering inside a garrison building.
				// Garrisoned passengers are removed from the world (Cargo holds them),
				// so FindActorsInCircle misses them. Treat the building's position as
				// the soldier's effective position — the building is in range, so the
				// soldier is in range.
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

						if (!rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo && currentSupply >= p.Info.SupplyValue))
						{
							hasUnaffordableTargets = true;
							continue;
						}

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

			// If a docking gate is configured (e.g. unit.docked on the LC), the target
			// must already be holding that external condition. This implies stationary
			// (the docking trigger only fires inside a tight proximity range).
			if (!string.IsNullOrEmpty(Info.DockedCondition))
			{
				var docked = a.TraitsImplementing<ExternalCondition>()
					.Any(e => e.Info.Condition == Info.DockedCondition && e.IsGranted);
				if (!docked)
					return false;
			}

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

			return false;
		}

		void SetTarget(Actor target)
		{
			if (currentTarget == target)
				return;

			RevokeTargetCondition();
			currentTarget = target;

			// Sheltered passengers in garrison buildings aren't in the world; their
			// CenterPosition is stale. The building they're inside is, by definition,
			// already in range — so skip move-toward and skip granting the rearm
			// condition (invisible anyway, and would leak if the soldier later
			// deploys to a port before our next ResupplyTarget tick).
			if (currentTarget != null && currentTarget.IsInWorld)
			{
				// If target is out of range (Hunt mode found a distant flagged unit), move toward it
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

				// Grant condition to new target
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
			// Note: !IsInWorld is valid here — shelter soldiers in garrison buildings
			// are intentionally removed from world. SetTarget already skipped move-toward
			// and condition-grant for them; we just need to deliver ammo. Only bail on
			// null/dead.
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
				// Batch math: deliver one batch of ReloadCount rounds per cycle for
				// SupplyValue cost per batch. Falls back to 1 round per cycle when
				// the pool has the default ReloadCount: 1.
				var batchSize = System.Math.Max(1, bestPool.Info.ReloadCount);
				var missing = bestPool.Info.Ammo - bestPool.CurrentAmmoCount;
				var canAfford = currentSupply >= bestPool.Info.SupplyValue;

				if (canAfford && missing > 0)
				{
					var roundsToGive = System.Math.Min(batchSize, missing);
					if (bestPool.GiveAmmo(currentTarget, roundsToGive))
					{
						currentSupply -= bestPool.Info.SupplyValue;
						UpdateSupplyConditions();

						if (!string.IsNullOrEmpty(bestPool.Info.RearmSound))
							Game.Sound.PlayToPlayer(SoundType.World, currentTarget.Owner, bestPool.Info.RearmSound, currentTarget.CenterPosition);
					}
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

				// Drive to the host (e.g., logistics center).
				var targetCell = self.World.Map.CellContaining(restockTarget.CenterPosition);
				self.QueueActivity(false, move.MoveTo(targetCell, ignoreActor: restockTarget));

				// Wait briefly to simulate restocking.
				self.QueueActivity(new Wait(25));

				// Drain supply from the host into self. No free refills — the
				// host's pool drops by exactly the amount transferred, capped at
				// what the host has on hand.
				self.QueueActivity(new CallFunc(() =>
				{
					var hostProvider = restockTarget.TraitOrDefault<SupplyProvider>();
					if (hostProvider != null)
					{
						var needed = Info.TotalSupply - currentSupply;
						var taken = System.Math.Min(needed, hostProvider.CurrentSupply);
						if (taken > 0 && hostProvider.DeductSupply(taken))
							AddSupply(taken);   // clears the residue-unusable latch on genuine refill
					}

					restocking = false;
				}));

				// Follow rally point if the restock target has one.
				var rp = restockTarget.TraitOrDefault<RallyPoint>();
				if (rp != null && rp.Path.Count > 0)
					foreach (var cell in rp.Cells)
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

			// A genuine replenish (restock/refill) makes the residue usable again.
			if (amount > 0)
				residueUnusable = false;

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

			if (!string.IsNullOrEmpty(Info.SupplyAnyCondition))
			{
				if (currentSupply > 0 && supplyAnyToken == Actor.InvalidConditionToken)
					supplyAnyToken = self.GrantCondition(Info.SupplyAnyCondition);
				else if (currentSupply <= 0 && supplyAnyToken != Actor.InvalidConditionToken)
					supplyAnyToken = self.RevokeCondition(supplyAnyToken);
			}
		}

		float ISelectionBar.GetValue()
		{
			if (IsTraitDisabled)
				return 0f;

			return (float)currentSupply / Info.TotalSupply;
		}

		bool ISelectionBar.DisplayWhenEmpty => true;

		// Red while holding an unusable residue (counts as empty, sliver remains); the
		// normal amber otherwise. A truly empty truck (currentSupply == 0) keeps amber.
		Color ISelectionBar.GetColor() { return residueUnusable ? Color.FromArgb(255, 200, 0, 0) : Color.FromArgb(255, 255, 200, 0); }

		bool ICargoCanLoadFilter.CanLoadPassenger(Actor self, Actor passenger)
		{
			return currentSupply > 0;
		}

		/// <summary>
		/// Residue verdict that drives the residueUnusable latch (see <see cref="UpdateTarget"/>).
		/// Pure so the unit tests pin the exact rule the live scan applies. Inputs are the two
		/// facts a greatest-need scan produces:
		///  - <paramref name="serviceableNeedyPresent"/>: a reachable unit we can afford one
		///    batch for cleared MinNeedThreshold (i.e. FindGreatestNeedTarget picked a best
		///    target). A near-full affordable unit does NOT count — it is below threshold.
		///  - <paramref name="unaffordableNeedyPresent"/>: a reachable unit still needs ammo but
		///    we cannot afford one batch of any of its pools.
		/// Returns true = unusable residue (evacuate), false = still usable, null = no demand in
		/// reach, so the caller leaves the latch unchanged (an evacuating truck keeps evacuating).
		/// </summary>
		public static bool? ResidueVerdict(int currentSupply, bool serviceableNeedyPresent, bool unaffordableNeedyPresent)
		{
			if (currentSupply <= 0)
				return true;

			if (serviceableNeedyPresent)
				return false;

			if (unaffordableNeedyPresent)
				return true;

			return null;
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
