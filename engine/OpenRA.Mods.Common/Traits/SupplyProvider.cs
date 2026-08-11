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
			"from the world — declutters a drained cache no unit can draw a batch from. 0",
			"disables self-removal; Logistics Centers and trucks leave it 0 (trucks evacuate",
			"via DropsSupplyCache instead). A ground cache has no trip home to reserve supply",
			"for, so set 1 on SUPPLYCACHE: it serves down to empty and only despawns at 0. A",
			"higher threshold would vanish a freshly-dropped crate carrying less than it.")]
		public readonly int RemoveBelowSupply = 0;

		[Desc("When the provider holds a residue too small for any reachable unit to use",
			"(no needy unit in range can be given even one batch), treat it as empty so",
			"its transport (DropsSupplyCache) evacuates instead of parking forever.",
			"Intended for supply trucks; leave false on Logistics Centers and caches.")]
		public readonly bool EvacuateOnUnusableResidue = false;

		[Desc("Consecutive unusable-residue verdicts required before the latch is set (and the",
			"transport is allowed to evacuate). 0 or 1 = latch on the first verdict, the",
			"undamped behaviour. Only the latch-TRUE direction is damped: a verdict of 'usable'",
			"clears the latch on the same scan it is seen.")]
		public readonly int ResidueConfirmScans = 5;

		[ActorReference]
		[Desc("Actor types where the supply provider can restock.")]
		public readonly HashSet<string> RestockActors = new HashSet<string>();

		[Desc("Ticks a transport settles at a restock host before the supply transfers. Read by both",
			"paths that send a truck to an LC (this trait's own low-supply drive and DropsSupplyCache's",
			"idle/ordered one), which is the point — the two used to carry the same literal 25 in two",
			"places and could drift apart silently.")]
		public readonly int RestockWaitTicks = 25;

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

		[GrantedConditionReference]
		[Desc("Condition granted to self while this provider has somebody in its aura it could serve a",
			"batch to right now — i.e. while it is doing its job. Intended for a MOBILE provider, whose",
			"Mobile.PauseOnCondition should name it: the transport then HALTS for as long as there is",
			"anyone left to serve and resumes the order it was already carrying out the moment there is",
			"not, instead of driving past its customers. Pausing Mobile rather than cancelling the order",
			"is what makes 'and then continue moving' free — Move.Tick returns false while paused",
			"(Move.cs:168), leaving the activity intact rather than tearing it down.",
			"Empty (the default) disables the whole mechanism, so Logistics Centers and ground caches —",
			"which cannot move anyway — are unaffected.",
			"The halt is switched off per-unit by EngagementStance.HoldPosition; see ShouldHaltToServe.")]
		public readonly string ServingCondition = null;

		public override object Create(ActorInitializer init) { return new SupplyProvider(init, this); }
	}

	/// <summary>
	/// What a provider may do with the target it is currently holding, this tick. Produced by
	/// <see cref="SupplyProvider.DecideServe"/> and consumed by both the delivery path and the
	/// condition tracker, so the two cannot disagree about whether a target is being served.
	/// </summary>
	public readonly struct SupplyServeDecision
	{
		/// <summary>Ammo may be handed over now.</summary>
		public readonly bool Deliver;

		/// <summary>The RearmCondition should be granted to the target.</summary>
		public readonly bool HoldCondition;

		/// <summary>The target stays selected even when it cannot be served yet.</summary>
		public readonly bool KeepTarget;

		public SupplyServeDecision(bool deliver, bool holdCondition, bool keepTarget)
		{
			Deliver = deliver;
			HoldCondition = holdCondition;
			KeepTarget = keepTarget;
		}
	}

	public class SupplyProvider : PausableConditionalTrait<SupplyProviderInfo>, ITick,
		ITransformActorInitModifier, ISelectionBar, ICargoCanLoadFilter,
		INotifyKilled, INotifyRemovedFromWorld, INotifyActorDisposing
	{
		readonly Actor self;
		int currentSupply;
		int rearmTicks;
		int scanTicks;

		Actor currentTarget;
		ExternalCondition targetConditionTrait;
		int conditionToken = Actor.InvalidConditionToken;

		/// <summary>
		/// Is this provider on its way to (or settling at) a restock host? Read off the ACTIVITY QUEUE,
		/// never latched in a field — see <see cref="RestockSupply"/> for the bug that shape caused, and
		/// <see cref="AmmoPool.IsSeekingRearm"/> for the same technique answering the same kind of
		/// question on the ammunition side.
		///
		/// <para>Walks the whole queue rather than just the head, and that is load-bearing:
		/// <c>QueueActivity(false, …)</c> CANCELS the current activity rather than removing it, so the
		/// dying activity stays HEAD while the restock we just queued sits behind it in
		/// <c>NextActivity</c>. A head-only test would answer "no" during exactly the window that
		/// matters and let the caller queue a second drive on top of the first.</para>
		/// </summary>
		public bool Restocking
		{
			get
			{
				for (var a = self.CurrentActivity; a != null; a = a.NextActivity)
					if (a is RestockSupply)
						return true;

				return false;
			}
		}

		// Latched true when EvacuateOnUnusableResidue and the remaining supply is a
		// residue no reachable unit can utilize. Cleared on replenish or full drain.
		bool residueUnusable;

		// Consecutive unusable verdicts seen so far, stepped by StepResidueConfirmations.
		int residueConfirmations;

		// Result of the LAST greatest-need scan: was there a unit in the aura we could actually hand a
		// batch to? Refreshed only in UpdateTarget, which is deliberate — it therefore survives the
		// single tick between "delivered a batch, dropped the target" and the forced re-scan, so the
		// halt does not blink off and let the transport inch forward between every batch.
		bool servableTargetInAura;

		int servingToken = Actor.InvalidConditionToken;

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
			TickServing(self);

			// AFTER the body, never inside it, and that placement is the whole reason this is a separate
			// method. TickServing is a ladder of early returns and EVERY ONE of them is a state in which
			// this provider serves nobody — out of the world, paused, disabled, mid-restock, despawning,
			// drained, or reserving its remainder for the drive home. CanServeNow is that same ladder
			// written as a predicate, so asking it once here covers all of them; a revoke at each return
			// would be seven chances to miss one, and a missed one latches a truck in place for good.
			SyncServingCondition();
		}

		void TickServing(Actor self)
		{
			// Removed from the world but still ticking: ITick traits run off the trait container,
			// which has no IsInWorld filter and is only cleaned when the actor is disposed
			// (TraitDictionary.cs:305-316, Actor.cs:468). A truck picked up by a Carryall is removed
			// without being disposed (PickupUnit.cs:174), as is one loaded into cargo, and its
			// CenterPosition stays frozen at the pickup point — so without this guard it would go on
			// scanning from there: re-granting the rearm condition one tick after RemovedFromWorld
			// revoked it, and pushing ammo to units near its last position from inside the carrier.
			// Unloading needs no re-acquisition logic; the next UpdateTarget scan picks a target again.
			if (!self.IsInWorld)
			{
				ReleaseTargetOnExit();
				return;
			}

			if (IsTraitPaused || IsTraitDisabled)
			{
				RevokeTargetCondition();
				currentTarget = null;
				return;
			}

			if (Restocking)
			{
				// Defensive, and a no-op today: every path that sets restocking already revoked and
				// nulled the target first, so conditionToken is already invalid here. Stating it
				// locally means the "a restocking provider holds no grant" safety no longer rests on
				// reading three other call sites.
				RevokeTargetCondition();
				return;
			}

			// A stationary provider flagged to self-remove when almost empty despawns once its
			// pool drops below the threshold — the stationary analog of a truck driving home
			// when low (DropsSupplyCache). Disposal path mirrors AbsorbsSupplyCache.cs.
			if (Info.RemoveBelowSupply > 0 && currentSupply < Info.RemoveBelowSupply)
			{
				// Defensive, and a no-op today: currentSupply only falls in ResupplyTarget, whose tail
				// already revoked and cleared the target. Doing it here too means the grant is released
				// before the frame-end Dispose regardless of how we reached this branch.
				RevokeTargetCondition();
				currentTarget = null;

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
				residueConfirmations = 0;

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

			// Keep the rearm condition tracking aura membership every tick. This CANNOT live on
			// SetTarget's target-change edge: SetTarget early-returns when the target is unchanged,
			// so a target that leaves (or enters) the aura while still selected would never be
			// re-evaluated, and the condition would latch.
			SyncTargetCondition();

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

			// Recorded HERE, before the Hunt fallback below can overwrite bestTarget with something
			// anywhere on the map. The halt means "somebody within reach still needs me", so it must read
			// the aura scan and not the hunt: a Hunt-stance truck that stopped dead for a customer twenty
			// cells away would never reach him.
			servableTargetInAura = bestTarget != null;

			// Residue-unusable latch, decided by the same pure predicate the tests pin.
			// bestTarget != null means a reachable unit we can afford met MinNeedThreshold
			// (serviceable); hasUnaffordableTargets means a reachable needy unit exists that
			// we can't afford. A null verdict (no demand at all) leaves the latch unchanged
			// so an already-evacuating truck stays evacuating.
			//
			// DWELL, and it is the ONLY assignment of residueUnusable from a verdict — the
			// confirm counter cannot be bypassed by adding a caller. Setting the latch sends an
			// IDLE truck map-edge-ward to sell itself (DropsSupplyCache.ITick queues RotateToEdge
			// within one tick of CountsAsEmpty going true), so it is damped; clearing it only
			// resumes serving, so it is not.
			//
			// NOT because the set is irreversible — it is not, and an earlier version of this
			// comment claimed otherwise. Clearing the latch puts the truck back on the follow
			// roster and the next Move cancels the drive; that reversal is exactly the wiggle
			// being fixed here. The set is damped because it is EXPENSIVE: the recovery is owned
			// by a 150-tick bot scan, so a spurious set costs ~9 s of a truck driving the wrong
			// way, while a spurious clear costs one 7-tick scan of serving nobody. Polarity comes
			// from the costs, and is the opposite of the evac damper's one file over for the same
			// reason — there the expensive error is a DELAYED withdrawal.
			//
			// Why a DWELL and not a value band: the verdict is a boolean function of a SET SCAN
			// (aura membership + need >= MinNeedThreshold + affordability), not a threshold on one
			// scalar, so there is no single axis to band — a band on any one of the three leaves the
			// other two undamped. Of the three, affordability is integer and coarsely quantised
			// (currentSupply moves in SupplyValue steps of 5..200 out of 750, so any band narrow
			// enough to be safe is sub-quantum), and banding the aura would reopen the exact
			// selection-vs-delivery boundary disagreement InAuraRange was extracted to close. A time
			// bound is scale-free and covers all three at once.
			//
			// The forced re-scan after each pip (scanTicks = 0 in Tick) does NOT accelerate the
			// latch: serving means a serviceable target exists, i.e. the verdict is false on exactly
			// those scans, which resets the counter.
			if (Info.EvacuateOnUnusableResidue)
			{
				var verdict = ResidueVerdict(currentSupply, bestTarget != null, hasUnaffordableTargets);
				(residueUnusable, residueConfirmations) = StepResidueLatch(
					residueUnusable, residueConfirmations, verdict, Info.ResidueConfirmScans);
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
			if (Info.RestockActors.Count == 0 || Restocking)
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
			if (!InAuraRange(self.CenterPosition, a.CenterPosition, Info.Range))
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
			// already in range — so skip move-toward for them.
			if (currentTarget != null && currentTarget.IsInWorld)
			{
				// If target is out of range (Hunt mode found a distant flagged unit), move toward it
				if (!InAuraRange(self.CenterPosition, currentTarget.CenterPosition, Info.Range))
				{
					var move = self.TraitOrDefault<IMove>();
					if (move != null)
					{
						var targetCell = self.World.Map.CellContaining(currentTarget.CenterPosition);
						self.QueueActivity(false, move.MoveTo(targetCell, 2));
					}
				}
			}

			// The rearm condition is NOT granted here. Granting on the target-change edge is what
			// let it latch on an out-of-aura target: this method early-returns when the target is
			// unchanged, so it never gets a second look. SyncTargetCondition owns the whole
			// grant/revoke lifecycle and re-evaluates every tick.
			rearmTicks = Info.RearmDelay;
		}

		/// <summary>
		/// Grants or revokes the rearm condition so it tracks aura membership, re-evaluated every
		/// tick for as long as the target is held. Out of the aura the condition comes off — it
		/// enables the target's own ReloadAmmoPool trickle, which has no range check of its own —
		/// and goes back on when the target enters the aura, without the target ever being dropped.
		/// </summary>
		void SyncTargetCondition()
		{
			if (currentTarget == null || currentTarget.IsDead)
			{
				RevokeTargetCondition();
				return;
			}

			if (string.IsNullOrEmpty(Info.RearmCondition))
				return;

			var inWorld = currentTarget.IsInWorld;
			var inAura = inWorld && InAuraRange(self.CenterPosition, currentTarget.CenterPosition, Info.Range);
			var shouldHold = DecideServe(inWorld, inAura).HoldCondition;
			var held = conditionToken != Actor.InvalidConditionToken;

			if (shouldHold == held)
				return;

			if (!shouldHold)
			{
				RevokeTargetCondition();
				return;
			}

			targetConditionTrait = currentTarget.TraitsImplementing<ExternalCondition>()
				.FirstOrDefault(e => e.Info.Condition == Info.RearmCondition);
			if (targetConditionTrait != null)
				conditionToken = targetConditionTrait.GrantCondition(currentTarget, this);
		}

		/// <summary>
		/// Release the rearm condition when this provider leaves play. Without this the grant is
		/// ORPHANED: ExternalCondition.permanentTokens is keyed by granting source and has no
		/// source-death sweep (the Tick expiry loop only walks timedTokens, and the ReduceTicks
		/// decay path is inert unless configured — infantry's ExternalCondition@AmmoReplenish sets
		/// only Condition). So a provider destroyed while serving leaves its target holding
		/// replenish-soldiers forever, which keeps ReloadAmmoPool trickling free ammo for the rest
		/// of the match. A parked truck is a prime artillery target and the token is held during
		/// every serving cycle, so this is an ordinary occurrence, not a corner case.
		///
		/// Note what does NOT stop the trait: leaving the world. ITick traits are not driven from the
		/// `actors` dict (World.cs:496-497 ticks that only for ACTIVITIES) but through
		/// ApplyToActorsWithTraitTimed&lt;ITick&gt; → TraitDictionary.ApplyToAllTimed
		/// (TraitDictionary.cs:305-316), which walks the trait container with NO IsInWorld or
		/// Disposed filter. An actor leaves that container only in Actor.Dispose's frame-end task
		/// (Actor.cs:469), so a removed-but-not-disposed provider KEEPS TICKING — see the IsInWorld
		/// guard at the top of Tick, which is what actually stops it.
		///
		/// Three notifications, because they answer different questions:
		///  - Killed and Disposing are the terminal pair, and between them they cover every way a
		///    provider permanently leaves play: combat death, the RemoveBelowSupply self-Dispose in
		///    Tick, sell, and the TRUK/LCCV transform. Killed fires at the moment of death, ahead of
		///    Dispose's frame-end task, so a truck destroyed mid-cycle releases its target
		///    immediately rather than at end of frame; Disposing is the backstop that fires however
		///    the actor got there, including when it was already out of the world (Actor.Dispose
		///    calls World.Remove only `if (IsInWorld)`, Actor.cs:463).
		///  - RemovedFromWorld is belt and braces at the removal moment, and the only one that fires
		///    for a NON-terminal exit — a Carryall pickup removes the truck without disposing it
		///    (PickupUnit.cs:174), as does loading into cargo. It revokes immediately; the Tick
		///    guard then keeps the trait from re-granting on the following tick.
		///
		/// Redundant revokes are harmless: TryRevokeCondition returns false once the token is gone,
		/// and conditionToken is zeroed on the first call. It is world-independent and acts on the
		/// TARGET's trait, so running it while SELF is dead or disposing is safe.
		/// </summary>
		void ReleaseTargetOnExit()
		{
			RevokeTargetCondition();
			currentTarget = null;
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e) { ReleaseTargetOnExit(); }

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { ReleaseTargetOnExit(); }

		void INotifyActorDisposing.Disposing(Actor self) { ReleaseTargetOnExit(); }

		void RevokeTargetCondition()
		{
			// IsInWorld is deliberately NOT checked. A held target boarding a garrison shelter is
			// exactly when this revoke must fire: the soldier leaves the world, DecideServe drops
			// HoldCondition (shelter passengers are served but never granted), and SyncTargetCondition
			// revokes — but an IsInWorld guard would skip TryRevokeCondition while the fields below
			// are zeroed regardless, orphaning a permanent token that no longer has an owner to
			// release it. The soldier would then carry a free perpetual ReloadAmmoPool trickle out of
			// the garrison. TryRevokeCondition is world-independent (list bookkeeping plus a
			// TokenValid-guarded revoke), so calling it on an out-of-world actor is safe.
			if (conditionToken != Actor.InvalidConditionToken && currentTarget != null &&
				!currentTarget.IsDead && targetConditionTrait != null)
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

			// The aura is a proximity push: enforce Range on delivery, not just on selection.
			// Target selection can legitimately hand us a target that is out of range right now —
			// the Hunt branch in UpdateTarget picks a flagged unit anywhere on the map and SetTarget
			// only *starts* driving toward it — and a selected target can also walk out of the aura
			// during the RearmDelay wait. Without this gate GiveAmmo fires at any distance.
			var inWorld = currentTarget.IsInWorld;
			var decision = DecideServe(inWorld,
				inWorld && InAuraRange(self.CenterPosition, currentTarget.CenterPosition, Info.Range));

			if (!decision.Deliver)
			{
				// Keep the target so an approaching provider serves it on arrival; just don't deliver
				// yet. SyncTargetCondition has already taken the rearm condition off, and puts it
				// back the tick we arrive.
				rearmTicks = Info.RearmDelay;
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

			if (restockTarget == null)
				return;

			var move = self.TraitOrDefault<IMove>();
			if (move == null)
				return;

			// The whole errand — drive, settle, transfer — as ONE named activity, so the truck's intent
			// lives in the activity queue rather than in a bool alongside it. See RestockSupply for what
			// the bool cost.
			self.QueueActivity(false, new RestockSupply(self, restockTarget, Info.RestockWaitTicks));

			// Follow rally point if the restock target has one.
			var rp = restockTarget.TraitOrDefault<RallyPoint>();
			if (rp != null && rp.Path.Count > 0)
				foreach (var cell in rp.Cells)
					self.QueueActivity(move.MoveTo(cell, 1));
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

			// A genuine replenish (restock/refill) makes the residue usable again. The confirm
			// counter goes with it: evidence gathered against the OLD load says nothing about
			// the new one, and leaving it standing would let a refilled truck re-latch on the
			// first adverse scan instead of on ResidueConfirmScans of them.
			if (amount > 0)
			{
				residueUnusable = false;
				residueConfirmations = 0;
			}

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
		/// The whole per-tick "what may I do with this target" rule, kept pure so both the delivery
		/// path and the condition tracker read it from one place.
		///
		/// The aura is a proximity push, so an out-of-aura target gets neither ammo NOR the
		/// RearmCondition — the condition enables the target's own ReloadAmmoPool (a free in-place
		/// trickle that carries no range check of its own), so leaving it granted at unlimited range
		/// is the same exploit as delivering at unlimited range. The target is still KEPT, because
		/// selection can legitimately hand us something we are only just driving toward; we simply
		/// serve it on arrival.
		///
		/// Sheltered garrison passengers are the exception: they are removed from the world with a
		/// stale CenterPosition, and their building was in range when they were picked, so they are
		/// served — but never granted the condition, which would be invisible and would leak if the
		/// soldier later deployed out.
		/// </summary>
		public static SupplyServeDecision DecideServe(bool targetInWorld, bool inAura)
		{
			if (!targetInWorld)
				return new SupplyServeDecision(deliver: true, holdCondition: false, keepTarget: true);

			if (inAura)
				return new SupplyServeDecision(deliver: true, holdCondition: true, keepTarget: true);

			return new SupplyServeDecision(deliver: false, holdCondition: false, keepTarget: true);
		}

		/// <summary>
		/// Should a MOBILE provider stand still right now because it has somebody to serve?
		///
		/// <para>THE STANCE AXIS, and why this one. The user asked for the halt to be switchable by
		/// stance, so the axis had to be one no truck-side code already reads. ResupplyBehavior is fully
		/// occupied — all three values are live on TRUK (Evacuate is its shipped default for human and
		/// AI alike), so it was not available. UnitStance is entirely free but for the wrong reason: TRUK
		/// declares no armament and ^Vehicle does not inherit ^Combatant, so a fire stance on a truck is
		/// not merely unread, it is meaningless to a player. That leaves EngagementStance, where Hunt is
		/// taken (the whole-map needs-resupply scan above), Defensive is the shipped default, and
		/// <see cref="EngagementStance.HoldPosition"/> has no truck-side reader at all.</para>
		///
		/// <para>HoldPosition is also the right MEANING rather than merely a free slot: it is already the
		/// engine's "this unit does what I told it and nothing else" marker — ControlAllUnitsManager
		/// keeps HoldPosition units under player control instead of handing them to a bot
		/// (ControlAllUnitsManager.cs:56-59). "Do not self-divert" reads the same way for a truck. So the
		/// halt is ON at Defensive and at Hunt (a hunting truck that would not stop for the customer it
		/// drove to would be absurd) and OFF at HoldPosition.</para>
		///
		/// <para>Terminates by construction: the halt holds only while a unit in the aura can be handed a
		/// batch, and serving it is what removes it from that set — either it fills up, or it drops below
		/// MinNeedThreshold, or the supply runs out and CanServeNow goes false. There is no state in
		/// which the transport is stopped and also not making progress toward being able to move again.</para>
		/// </summary>
		public bool ShouldHaltToServe()
		{
			// Cheapest test first, and the one that is false almost always. Also means a provider that
			// never opted in (no ServingCondition) costs nothing beyond a null check.
			if (!servableTargetInAura || string.IsNullOrEmpty(Info.ServingCondition))
				return false;

			if (!self.IsInWorld || !CanServeNow)
				return false;

			var autoTarget = self.TraitOrDefault<AutoTarget>();
			if (autoTarget != null && autoTarget.EngagementStanceValue == EngagementStance.HoldPosition)
				return false;

			return true;
		}

		void SyncServingCondition()
		{
			if (string.IsNullOrEmpty(Info.ServingCondition))
				return;

			var shouldHold = ShouldHaltToServe();
			var held = servingToken != Actor.InvalidConditionToken;
			if (shouldHold == held)
				return;

			servingToken = shouldHold
				? self.GrantCondition(Info.ServingCondition)
				: self.RevokeCondition(servingToken);
		}

		/// <summary>
		/// Whether this provider is in a state where it serves anyone at all this tick — the exact
		/// early-return ladder <see cref="TickServing"/> walks before it ever looks for a target.
		/// Exposed so a unit deciding whether to walk here can ask instead of reproducing the rule.
		/// </summary>
		public bool CanServeNow
		{
			get
			{
				// Tick: paused/disabled clears the target and returns.
				if (IsTraitPaused || IsTraitDisabled)
					return false;

				// Tick: mid-restock drive — serves nobody until it arrives.
				if (Restocking)
					return false;

				// Tick: about to remove itself from the world.
				if (Info.RemoveBelowSupply > 0 && currentSupply < Info.RemoveBelowSupply)
					return false;

				// Tick: drained.
				if (currentSupply <= 0)
					return false;

				// Tick: below the restock threshold with no active customer, and not one of the
				// evacuating trucks that keep serving down to the last usable batch — it is about
				// to reserve its remainder and drive home.
				if (currentSupply < Info.RestockThreshold && currentTarget == null && !KeepServingBelowThreshold())
					return false;

				return true;
			}
		}

		/// <summary>
		/// Whether a target sits inside the provider's push aura. Horizontal (2D) distance compared
		/// squared, which is exactly the filter WorldUtils.FindActorsInCircle applies
		/// (<c>HorizontalLengthSquared &lt;= r.LengthSquared</c>) — so selection and delivery agree on
		/// the boundary instead of drifting by the floor() that WVec.HorizontalLength's ISqrt applies.
		/// Pure so the unit tests pin the exact rule every range site in this trait uses.
		/// </summary>
		public static bool InAuraRange(WPos providerPos, WPos targetPos, WDist range)
		{
			return (targetPos - providerPos).HorizontalLengthSquared <= range.LengthSquared;
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

		/// <summary>The residue latch WITH MEMORY — the anti-oscillation replacement for assigning the raw
		/// <see cref="ResidueVerdict"/> straight to the latch every scan. Returns the new latch state AND the
		/// new confirmation count together, deliberately as ONE call: an earlier split into a step-the-counter
		/// function plus a read-the-counter predicate left the ordering as a caller obligation enforced only
		/// by a doc comment, and swapping the two lines cost a scan silently. Shape mirrors
		/// CombatRetreatMath.Step, which returns its decision and its streak for the same reason.
		///
		/// <para>Three rules:</para>
		/// <list type="number">
		/// <item>CLEARING IS NEVER DAMPED. A <c>false</c> verdict (someone in reach can be served a batch)
		/// clears the latch on the scan it is seen, whatever the count says, and resets the count — the
		/// evidence for "nobody can use this" is destroyed the moment somebody can.</item>
		/// <item>A <c>null</c> verdict (no demand in reach) leaves BOTH alone, matching the null contract of
		/// <see cref="ResidueVerdict"/>: absence of evidence must neither confirm nor deny.</item>
		/// <item>LATCHING IS DAMPED. A <c>true</c> verdict counts up — saturating at
		/// <paramref name="requiredConfirmations"/>, so a long-latched truck cannot overflow and stays exactly
		/// one <c>false</c> away from clearing — and sets the latch only on reaching it.</item>
		/// </list>
		///
		/// <para>WHY THE ASYMMETRY, CORRECTED. An earlier version of this comment said setting the latch
		/// "drives the truck off the map and sells it, which no later scan can undo". THAT IS FALSE, and the
		/// falsehood mattered: <c>RotateToEdge</c> is queued only while the truck <c>IsIdle</c>
		/// (DropsSupplyCache.cs), and once the latch clears the truck re-enters the follow roster and the next
		/// Move cancels the drive. The set is reversible right up until the sale actually completes — indeed
		/// that reversal IS the observed wiggle. The asymmetry is justified on COST, not reversibility: a
		/// truck driving map-edge-ward is doing nothing useful for as long as it takes a 150-tick bot scan to
		/// notice the latch cleared and yank it back, whereas a truck left serving with a residue it cannot
		/// spend has merely wasted one 7-tick scan. Damp the expensive direction; the cheap one stays instant.
		/// (Contrast the evac damper one file over, which damps the OPPOSITE direction — there the expensive
		/// error is a delayed withdrawal. Derive the polarity from the costs each time; do not copy it.)</para>
		///
		/// <para><paramref name="requiredConfirmations"/> &lt;= 1 ⇒ latch on the first true verdict, i.e. the
		/// pre-damper behaviour, so the field can be turned back off to a known baseline.</para>
		///
		/// <para>A genuinely DRAINED provider does not depend on this at all: <c>CountsAsEmpty</c> ORs
		/// <c>currentSupply &lt;= 0</c> independently of the latch, and Tick early-returns on that case before
		/// UpdateTarget ever runs. So the dwell can never strand an actually-empty truck at the front — it
		/// governs only the residue judgement, which is the one that was flipping.</para>
		/// Pure, zero RNG.</summary>
		public static (bool Latched, int Confirmations) StepResidueLatch(
			bool latched, int confirmations, bool? verdict, int requiredConfirmations)
		{
			var required = requiredConfirmations > 0 ? requiredConfirmations : 1;

			if (!verdict.HasValue)
				return (latched, confirmations > required ? required : confirmations);

			if (!verdict.Value)
				return (false, 0);

			var stepped = confirmations >= required ? required : confirmations + 1;
			return (stepped >= required, stepped);
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
