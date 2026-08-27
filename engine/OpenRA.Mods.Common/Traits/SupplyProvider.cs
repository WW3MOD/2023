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

		[Desc("A SECOND clientele served from the SAME supply pool, on its own terms: never subject to",
			"DockedCondition, and using AuraRange/AuraRearmDelay instead of Range/RearmDelay. It exists",
			"because a provider can owe service to two populations that one set of fields cannot",
			"describe — the Logistics Center serves vehicles that must dock (2c0, unit.docked,",
			"replenish-vehicles) and infantry that merely stand nearby (4c0, no dock,",
			"replenish-soldiers). PITFALL: the dock gate is checked BEFORE RearmCondition in",
			"IsValidTarget, and only ^Vehicle declares unit.docked (vehicles.yaml:29) — so widening",
			"RearmCondition alone would NOT have let a soldier through, because the gate rejects him",
			"first. ONE pool and ONE bar are the whole point; two trait instances would give the actor",
			"two of each. Empty (the default) disables the mechanism, so a single-clientele provider —",
			"the supply truck and SUPPLYCACHE, neither of which has a dock gate — is untouched.")]
		public readonly string AuraRearmCondition = null;

		[Desc("Range for the AuraRearmCondition clientele. Zero (the default) falls back to Range.")]
		public readonly WDist AuraRange = WDist.Zero;

		[Desc("Ticks between ammo increments for the AuraRearmCondition clientele.",
			"Negative (the default) falls back to RearmDelay.")]
		public readonly int AuraRearmDelay = -1;

		/// <summary>Range of the aura clientele, resolving the fall-back to <see cref="Range"/>.</summary>
		public WDist EffectiveAuraRange => AuraRange > WDist.Zero ? AuraRange : Range;

		/// <summary>Cadence of the aura clientele, resolving the fall-back to <see cref="RearmDelay"/>.</summary>
		public int EffectiveAuraRearmDelay => AuraRearmDelay >= 0 ? AuraRearmDelay : RearmDelay;

		/// <summary>Whether a second, non-docking clientele is configured at all.</summary>
		public bool HasAuraClientele => !string.IsNullOrEmpty(AuraRearmCondition);

		/// <summary>
		/// Radius the per-scan sweep must cover to see BOTH clienteles. The aura clientele is
		/// routinely the wider of the two (the LC docks vehicles at 2c0 but reaches infantry at 4c0),
		/// so scanning at <see cref="Range"/> alone would never enumerate a soldier to serve.
		/// </summary>
		public WDist ScanRange => HasAuraClientele && EffectiveAuraRange > Range ? EffectiveAuraRange : Range;

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
		[Desc("Condition granted once per occupied load step, so a condition expression can compare",
			"against the resulting count — `supply-level >= 7` — in the same idiom AmmoPool.AmmoCondition",
			"already allows for rounds. This keeps the band count in YAML instead of baking it into a",
			"fixed set of named fields here. Inert unless SupplyLevelSteps is also set.")]
		public readonly string SupplyLevelCondition = null;

		[Desc("Number of load steps SupplyLevelCondition resolves to. 0 (default) disables it entirely,",
			"so an actor that does not name the condition is unaffected.")]
		public readonly int SupplyLevelSteps = 0;

		// Ceiling division: any non-zero supply occupies at least step 1, and only a full load reaches
		// the top step. That keeps "empty" distinguishable from "nearly empty" — the distinction the
		// death-explosion bands are keyed on — and stops a partial load reaching the top band.
		public static int SupplyLevel(int currentSupply, int totalSupply, int steps)
		{
			if (steps <= 0 || totalSupply <= 0 || currentSupply <= 0)
				return 0;

			var level = (currentSupply * steps + totalSupply - 1) / totalSupply;
			return level < steps ? level : steps;
		}

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

	/// <summary>
	/// Which of a provider's two clienteles a candidate target belongs to. Produced by
	/// <see cref="SupplyProvider.MatchClientele"/>.
	/// </summary>
	public readonly struct SupplyClienteleMatch
	{
		/// <summary>The target qualifies for service at all.</summary>
		public readonly bool Matched;

		/// <summary>It qualified as an AURA client, so the aura range/cadence/condition govern it.</summary>
		public readonly bool IsAura;

		public SupplyClienteleMatch(bool matched, bool isAura)
		{
			Matched = matched;
			IsAura = isAura;
		}

		public static readonly SupplyClienteleMatch None = new SupplyClienteleMatch(false, false);
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
		/// Which clientele <see cref="currentTarget"/> was accepted as. Latched at selection rather than
		/// recomputed per tick on purpose: the three consumers below (move-toward, condition tracking,
		/// delivery) must all use the SAME range and condition the target was admitted under, or a
		/// soldier admitted at 4c0 would be served on the 2c0 dock terms and dropped on the next tick.
		/// </summary>
		bool currentTargetIsAura;

		/// <summary>Range governing the target currently held.</summary>
		WDist ActiveRange => currentTargetIsAura ? Info.EffectiveAuraRange : Info.Range;

		/// <summary>Cadence governing the target currently held.</summary>
		int ActiveRearmDelay => currentTargetIsAura ? Info.EffectiveAuraRearmDelay : Info.RearmDelay;

		/// <summary>Condition granted to the target currently held.</summary>
		string ActiveRearmCondition => currentTargetIsAura ? Info.AuraRearmCondition : Info.RearmCondition;

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

		/// <summary>
		/// Is this provider's transport carrying out a COMMITTED SUPPLY ERRAND — a move whose whole point
		/// is where the supply ends up? Today: driving to a host to refill, driving to an ordered cell to
		/// unload as a ground cache, or driving to a ground cache to collect it.
		///
		/// <para>Such an errand is not interrupted by the serving halt. A truck normally stops for anyone
		/// in its aura who needs a batch, which is right for an ordinary move and exactly wrong here: a
		/// truck sent to unload NEAR a platoon would stop to serve that platoon from its aura, never
		/// reach the drop cell, never place a crate, and stay parked in the danger the drop-and-leave
		/// doctrine exists to get it out of. Serving from the aura is not a cheap substitute for the
		/// drop — the doctrine picks between them on believed danger, and the halt must not silently
		/// overrule that choice.</para>
		///
		/// <para>Type-based and queue-derived, like <see cref="AmmoPool.IsSeekingRearm"/>, so it cannot
		/// latch and cannot outlive a cancellation; walks <c>NextActivity</c> as well as the head for the
		/// same reason <see cref="Restocking"/> does.</para>
		///
		/// <para>It is also the exemption half of the DRY BREAK-OFF rule: <b>cancel a move that is
		/// invalidated by being empty; never cancel a move that exists to stop being empty.</b> Both
		/// questions have the same answer for the same reason — an errand that decides where the supply
		/// ends up must be allowed to finish — so they read one predicate rather than two that could
		/// drift. Collecting a crate is the case that makes this concrete: sending an EMPTY truck to
		/// fetch a crate is the natural use of that order, and cancelling it would make the order useless
		/// in exactly the situation it exists for.</para>
		///
		/// <para>Deliberately WIDER than <see cref="Restocking"/>, which answers a different question
		/// ("am I mid-refill and therefore serving nobody?"). A truck driving out to unload still has a
		/// full load and would happily serve — the point is that it must not stop to.</para>
		/// </summary>
		public bool OnSupplyErrand
		{
			get
			{
				for (var a = self.CurrentActivity; a != null; a = a.NextActivity)
					if (a is RestockSupply || a is PlaceSupplyCache || a is CollectSupplyCache)
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

		readonly Stack<int> supplyLevelTokens = new Stack<int>();

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

			// ITransformActorInitModifier hands a transforming actor's remaining supply to the actor it
			// becomes, so a Logistics Center MCV keeps exactly what it was carrying when it deployed.
			// USER RULING 2026-08-22: "There is no difference between when it is driving or when it is
			// deployed, it carries the supplies it carries." LCCV and LOGISTICSCENTER therefore share one
			// TotalSupply (2250) and this transfer is the whole of the deploy behaviour — no top-up.
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
			if (ReservesRemainderForRestock(currentSupply, Info.RestockThreshold, currentTarget != null, KeepServingBelowThreshold()))
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
			var bestTarget = FindGreatestNeedTarget(out var hasUnaffordableTargets, out var bestIsAura);

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
					{
						bestTarget = FindNeedsResupplyTarget();

						// The hunt sweep applies no clientele test at all, so classify what it returns
						// rather than leaving the flag reading whatever the aura scan last set.
						bestIsAura = bestTarget != null && Info.HasAuraClientele
							&& DeclaresCondition(bestTarget, Info.AuraRearmCondition)
							&& !HoldsGrantedCondition(bestTarget, Info.DockedCondition);
					}
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

			SetTarget(bestTarget, bestIsAura);
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

		Actor FindGreatestNeedTarget(out bool hasUnaffordableTargets) { return FindGreatestNeedTarget(out hasUnaffordableTargets, out _); }

		Actor FindGreatestNeedTarget(out bool hasUnaffordableTargets, out bool bestIsAura)
		{
			Actor best = null;
			var bestNeed = 0f;
			hasUnaffordableTargets = false;
			bestIsAura = false;

			// ScanRange, not Range: the aura clientele is routinely the wider of the two, and sweeping at
			// Range would never enumerate the soldiers the aura arm exists to serve.
			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, Info.ScanRange))
			{
				if (IsValidTarget(a, out var isAura))
				{
					var verdict = AcceptClient(a.TraitOrDefault<Rearmable>(), out var need);
					if (verdict == SupplyAcceptance.Unaffordable)
						hasUnaffordableTargets = true;
					else if (verdict == SupplyAcceptance.Accept && need > bestNeed)
					{
						bestNeed = need;
						best = a;
						bestIsAura = isAura;
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

						var verdict = AcceptClient(soldier.TraitOrDefault<Rearmable>(), out var need);
						if (verdict == SupplyAcceptance.Unaffordable)
						{
							hasUnaffordableTargets = true;
							continue;
						}

						if (verdict != SupplyAcceptance.Accept)
							continue;

						if (need > bestNeed)
						{
							bestNeed = need;
							best = soldier;

							// A sheltered passenger is out of the world, so IsValidTarget cannot classify
							// him. Class him by what he declares, preferring the aura arm — the garrison
							// case is infantry, and on a two-clientele provider the dock arm is the one he
							// could never have satisfied anyway.
							bestIsAura = Info.HasAuraClientele && DeclaresCondition(soldier, Info.AuraRearmCondition);
						}
					}
				}
			}

			return best;
		}

		/// <summary>
		/// <para>Verdict of the provider's ACCEPT test on one candidate: does it want ammunition, can we
		/// pay for any of it, and is it needy enough to be worth a cycle.</para>
		///
		/// <para>Extracted 2026-08-27 because there were THREE copies of it — the aura sweep, the garrison
		/// sweep, and <see cref="CanSelect"/> — and the last of those was hand-assembled from the other
		/// two and got the supply term wrong, which wedged a docked himars permanently. The house rule
		/// applies: never duplicate a subtle predicate, because prose is not the countermeasure. A guard
		/// added to the sweep must reach CanSelect automatically or the two silently disagree about the
		/// same client, and "both arms decline him" is a failure nobody sees.</para>
		/// </summary>
		enum SupplyAcceptance
		{
			/// <summary>Nothing to give: no Rearmable, or every rearmable pool already full.</summary>
			NoDemand,

			/// <summary>Wants ammunition, but this depot cannot pay for a batch of anything it wants.</summary>
			Unaffordable,

			/// <summary>Affordable, but too nearly full to be worth a serving cycle.</summary>
			BelowThreshold,

			/// <summary>Serve it.</summary>
			Accept,
		}

		/// <summary>
		/// <para>The one accept test — demand, affordability, need — and it says nothing about RANGE or
		/// about <c>currentTarget</c>. Range is <see cref="IsValidTarget"/>'s job and the two sweeps apply
		/// it DIFFERENTLY: the aura sweep guards with it, the garrison sweep must not, because a sheltered
		/// passenger is out of the world with a stale CenterPosition and would fail any position test.
		/// Contention is not a refusal either — <c>UpdateTarget</c> re-picks by greatest need every scan.</para>
		///
		/// <para>IT TAKES THE <see cref="Rearmable"/>, NOT THE ACTOR, AND THAT IS THE GUARD. A range clause
		/// added here would look obviously correct — it is the "accept test" — and would silently delete
		/// the entire garrison clientele: no error, no failing scenario, because nothing exercises that
		/// path in game. Prose does not stop that, so the parameter does: with no Actor there is no
		/// position to test, and acquiring one means changing this signature, which is a deliberate act
		/// rather than a one-line slip. <c>SupplyProviderAcceptTest</c> fails the build if it changes.</para>
		/// </summary>
		SupplyAcceptance AcceptClient(Rearmable rearmable, out float need)
		{
			need = 0f;

			if (rearmable == null || !rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo))
				return SupplyAcceptance.NoDemand;

			if (!rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo && currentSupply >= p.Info.SupplyValue))
				return SupplyAcceptance.Unaffordable;

			need = CalculateNeed(rearmable);
			if (need < Info.MinNeedThreshold)
				return SupplyAcceptance.BelowThreshold;

			return SupplyAcceptance.Accept;
		}

		/// <summary>
		/// Takes the <see cref="Rearmable"/> rather than the Actor, deliberately — see
		/// <see cref="AcceptClient"/>. Need is a property of the pools; handing this an Actor would put a
		/// position back within reach of the shared accept path.
		/// </summary>
		static float CalculateNeed(Rearmable rearmable)
		{
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

		bool IsValidTarget(Actor a) { return IsValidTarget(a, out _); }

		/// <summary>
		/// <para>WILL this provider's push arm serve <paramref name="client"/> — not merely "could it own
		/// him". The distinction is the whole point and the first cut of this method got it wrong: it
		/// returned <c>IsValidTarget</c> alone, which carries no supply term, so a docked himars the depot
		/// could no longer afford was still reported as owned. <c>Rearmable.RearmTick</c> deferred, and
		/// deferring is not an exit — once docked, RearmTick returning true is the ONLY way out
		/// (Resupply.cs:301), and the SelfAssignedErrandIsOver escape at Resupply.cs:240 is gated on
		/// <c>!actualResupplyStarted</c> and so unreachable after arrival. The unit stood at the depot
		/// forever, combat-inert, withheld from every bot module by StarvingRecruitGate. That is precisely
		/// the failure <see cref="AmmoPool.ChooseAffordableResupplier"/> guards at DISPATCH time, reintroduced
		/// at ARRIVAL time, downstream of the guard.</para>
		///
		/// <para>So this mirrors the sweep's ACCEPT test, which is strictly narrower than IsValidTarget:
		/// affordability of some non-full pool and <c>MinNeedThreshold</c> (FindGreatestNeedTarget), plus the
		/// Tick prologue's serving guards — paused/disabled, restocking, self-removal, drained, and
		/// remainder-reserved. A client this declines falls through to Rearmable's own per-pool affordability
		/// check, counts the pool done, and LEAVES with whatever it got.</para>
		///
		/// <para>Deliberately NOT consulting <c>currentTarget</c>: contention is not a lockout.
		/// <c>UpdateTarget</c> runs every ScanInterval unconditionally and re-picks by greatest need, so a
		/// client waiting behind another is reconsidered each scan and wins as soon as its need is greatest.
		/// Reading currentTarget here would make a client leave merely because someone else was mid-batch.</para>
		///
		/// <para>Uses the SAME IsValidTarget the sweep uses rather than a second opinion about it; the extra
		/// clauses are added around it, not re-derived inside it.</para>
		/// </summary>
		public bool CanSelect(Actor client)
		{
			if (IsTraitPaused || IsTraitDisabled || Restocking)
				return false;

			if (currentSupply <= 0)
				return false;

			if (Info.RemoveBelowSupply > 0 && currentSupply < Info.RemoveBelowSupply)
				return false;

			if (ReservesRemainderForRestock(currentSupply, Info.RestockThreshold, currentTarget != null, KeepServingBelowThreshold()))
				return false;

			if (!IsValidTarget(client, out _))
				return false;

			// THE SAME predicate both sweeps apply, called rather than restated. The first cut of this
			// method restated it and omitted the supply term, which is the bug this extraction exists to
			// make unrepeatable.
			//
			// A BelowThreshold client is declined here, and that is FORCED rather than incidental: the
			// push arm would skip a nearly-full docked client too, so claiming it here would defer
			// Rearmable.RearmTick to an arm that never serves — the wedge again. Declining hands it to
			// the pull path, which tops it up and CHARGES for it. Docking is a deliberate act and the
			// unit pays for what it gets. Note the asymmetry with a truck, which has no pull path, so a
			// nearly-full unit beside one is still skipped; that is pre-existing and unchanged.
			return AcceptClient(client.TraitOrDefault<Rearmable>(), out _) == SupplyAcceptance.Accept;
		}

		bool IsValidTarget(Actor a, out bool isAura)
		{
			isAura = false;

			if (a == null || a.IsDead || !a.IsInWorld || a == self)
				return false;

			if (!Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner)))
				return false;

			// Ammo target: Rearmable with at least one non-full pool.
			var rearmable = a.TraitOrDefault<Rearmable>();
			if (rearmable == null || !rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo))
				return false;

			// A missing RearmCondition means "no condition required", so an unset field must read as
			// SATISFIED rather than as an unmet requirement — the truck and the cache both rely on the
			// primary arm behaving exactly as it did before the aura arm existed.
			var match = MatchClientele(
				inPrimaryRange: InAuraRange(self.CenterPosition, a.CenterPosition, Info.Range),
				dockGateConfigured: !string.IsNullOrEmpty(Info.DockedCondition),
				targetIsDocked: HoldsGrantedCondition(a, Info.DockedCondition),
				targetDeclaresPrimaryCondition: DeclaresCondition(a, Info.RearmCondition),
				auraConfigured: Info.HasAuraClientele,
				inAuraRange: InAuraRange(self.CenterPosition, a.CenterPosition, Info.EffectiveAuraRange),
				targetDeclaresAuraCondition: DeclaresCondition(a, Info.AuraRearmCondition));

			isAura = match.IsAura;
			return match.Matched;
		}

		/// <summary>Does the actor DECLARE this ExternalCondition? An empty condition name is vacuously true.</summary>
		static bool DeclaresCondition(Actor a, string condition)
		{
			if (string.IsNullOrEmpty(condition))
				return true;

			return a.TraitsImplementing<ExternalCondition>().Any(e => e.Info.Condition == condition);
		}

		/// <summary>Is the actor currently HOLDING this ExternalCondition? An empty condition name is vacuously true.</summary>
		static bool HoldsGrantedCondition(Actor a, string condition)
		{
			if (string.IsNullOrEmpty(condition))
				return true;

			return a.TraitsImplementing<ExternalCondition>().Any(e => e.Info.Condition == condition && e.IsGranted);
		}

		void SetTarget(Actor target) { SetTarget(target, false); }

		void SetTarget(Actor target, bool isAura)
		{
			if (currentTarget == target)
				return;

			RevokeTargetCondition();
			currentTarget = target;
			currentTargetIsAura = isAura;

			// Sheltered passengers in garrison buildings aren't in the world; their
			// CenterPosition is stale. The building they're inside is, by definition,
			// already in range — so skip move-toward for them.
			if (currentTarget != null && currentTarget.IsInWorld)
			{
				// If target is out of range (Hunt mode found a distant flagged unit), move toward it
				if (!InAuraRange(self.CenterPosition, currentTarget.CenterPosition, ActiveRange))
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
			rearmTicks = ActiveRearmDelay;
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

			if (string.IsNullOrEmpty(ActiveRearmCondition))
				return;

			var inWorld = currentTarget.IsInWorld;
			var inAura = inWorld && InAuraRange(self.CenterPosition, currentTarget.CenterPosition, ActiveRange);
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
				.FirstOrDefault(e => e.Info.Condition == ActiveRearmCondition);
			if (targetConditionTrait != null)
				conditionToken = targetConditionTrait.GrantCondition(currentTarget, this);
		}

		/// <summary>
		/// <para>Release the rearm condition when this provider leaves play. Without this the grant is
		/// ORPHANED: ExternalCondition.permanentTokens is keyed by granting source and has no
		/// source-death sweep (the Tick expiry loop only walks timedTokens, and the ReduceTicks
		/// decay path is inert unless configured — infantry's ExternalCondition@AmmoReplenish sets
		/// only Condition). So a provider destroyed while serving leaves its target holding
		/// replenish-soldiers forever, which keeps ReloadAmmoPool trickling free ammo for the rest
		/// of the match. A parked truck is a prime artillery target and the token is held during
		/// every serving cycle, so this is an ordinary occurrence, not a corner case.</para>
		///
		/// <para>Note what does NOT stop the trait: leaving the world. ITick traits are not driven from the
		/// `actors` dict (World.cs:496-497 ticks that only for ACTIVITIES) but through
		/// ApplyToActorsWithTraitTimed&lt;ITick&gt; → TraitDictionary.ApplyToAllTimed
		/// (TraitDictionary.cs:305-316), which walks the trait container with NO IsInWorld or
		/// Disposed filter. An actor leaves that container only in Actor.Dispose's frame-end task
		/// (Actor.cs:469), so a removed-but-not-disposed provider KEEPS TICKING — see the IsInWorld
		/// guard at the top of Tick, which is what actually stops it.</para>
		///
		/// <para>Three notifications, because they answer different questions:
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
		///    guard then keeps the trait from re-granting on the following tick.</para>
		///
		/// <para>Redundant revokes are harmless: TryRevokeCondition returns false once the token is gone,
		/// and conditionToken is zeroed on the first call. It is world-independent and acts on the
		/// TARGET's trait, so running it while SELF is dead or disposing is safe.</para>
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
				inWorld && InAuraRange(self.CenterPosition, currentTarget.CenterPosition, ActiveRange));

			if (!decision.Deliver)
			{
				// Keep the target so an approaching provider serves it on arrival; just don't deliver
				// yet. SyncTargetCondition has already taken the rearm condition off, and puts it
				// back the tick we arrive.
				rearmTicks = ActiveRearmDelay;
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

			// Batch math, affordability and the charge all live in AmmoPool.TryServeBatch now, shared with
			// the docking pull path so the two cannot disagree about what a batch costs. The SELECTION above
			// — greatest need, and only pools this depot can pay for — stays here: that is the push arm's own
			// policy rather than part of the price.
			if (bestPool != null)
				AmmoPool.TryServeBatch(currentTarget, bestPool, this);

			// After giving ammo, drop target to re-evaluate on next scan. Order matters: the delay is
			// read off the clientele we just served, then the latch is cleared with the target.
			RevokeTargetCondition();
			rearmTicks = ActiveRearmDelay;
			currentTarget = null;
			currentTargetIsAura = false;
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

			if (!string.IsNullOrEmpty(Info.SupplyLevelCondition) && Info.SupplyLevelSteps > 0)
			{
				var level = SupplyProviderInfo.SupplyLevel(currentSupply, Info.TotalSupply, Info.SupplyLevelSteps);
				while (supplyLevelTokens.Count < level)
					supplyLevelTokens.Push(self.GrantCondition(Info.SupplyLevelCondition));

				while (supplyLevelTokens.Count > level)
					self.RevokeCondition(supplyLevelTokens.Pop());
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
		/// <para>The whole per-tick "what may I do with this target" rule, kept pure so both the delivery
		/// path and the condition tracker read it from one place.</para>
		///
		/// <para>The aura is a proximity push, so an out-of-aura target gets neither ammo NOR the
		/// RearmCondition — the condition enables the target's own ReloadAmmoPool (a free in-place
		/// trickle that carries no range check of its own), so leaving it granted at unlimited range
		/// is the same exploit as delivering at unlimited range. The target is still KEPT, because
		/// selection can legitimately hand us something we are only just driving toward; we simply
		/// serve it on arrival.</para>
		///
		/// <para>Sheltered garrison passengers are the exception: they are removed from the world with a
		/// stale CenterPosition, and their building was in range when they were picked, so they are
		/// served — but never granted the condition, which would be invisible and would leak if the
		/// soldier later deployed out.</para>
		/// </summary>
		/// <summary>
		/// <para>Which clientele, if either, a candidate belongs to — kept pure so the selection sweep,
		/// the delivery path and the condition tracker cannot drift apart about who is being served
		/// and on whose terms.</para>
		///
		/// <para>THE ORDER MATTERS AND IS THE WHOLE POINT. The primary clientele is gated on the dock
		/// condition FIRST and its rearm condition second; a soldier fails the dock gate before his
		/// rearm condition is ever looked at, because only ^Vehicle declares unit.docked. That is why
		/// the aura clientele is a separate arm rather than a widened RearmCondition: widening the
		/// condition list would have changed a test that never runs.</para>
		///
		/// <para>Primary is tried first so an actor that could satisfy both — a docked vehicle inside
		/// the wider aura, were a mod ever to declare both conditions on one actor — is served on the
		/// docked terms it actually docked for.</para>
		/// </summary>
		public static SupplyClienteleMatch MatchClientele(
			bool inPrimaryRange, bool dockGateConfigured, bool targetIsDocked, bool targetDeclaresPrimaryCondition,
			bool auraConfigured, bool inAuraRange, bool targetDeclaresAuraCondition)
		{
			if (inPrimaryRange && (!dockGateConfigured || targetIsDocked) && targetDeclaresPrimaryCondition)
				return new SupplyClienteleMatch(true, false);

			if (auraConfigured && inAuraRange && targetDeclaresAuraCondition)
				return new SupplyClienteleMatch(true, true);

			return SupplyClienteleMatch.None;
		}

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

			// A committed supply errand outranks a passer-by. See OnSupplyErrand: halting on the way to
			// a drop cell means the crate is never placed and the truck stays in the danger the errand
			// was routing it out of.
			if (OnSupplyErrand)
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
				if (ReservesRemainderForRestock(currentSupply, Info.RestockThreshold, currentTarget != null, KeepServingBelowThreshold()))
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
		/// Is this provider holding its remaining supply back for a drive home rather than serving it?
		///
		/// <para>THE POINT OF THE THRESHOLD is a trip to reserve for: a truck that keeps handing out
		/// batches until it is empty cannot reach a Logistics Center to refill, so it stops at
		/// <c>RestockThreshold</c> and drives. A provider that will never drive anywhere has no such trip,
		/// and holding supply back for it is pure loss — the supply is spent when the provider dies or is
		/// captured, and in the meantime it is a box with a visible supply bar that serves nobody.</para>
		///
		/// <para>Pure, and shared by <see cref="TickServing"/> and <see cref="CanServeNow"/> — which is the
		/// reason it exists as a function at all. The clause was written out twice, once in the tick ladder
		/// and once in the predicate that claims to mirror that ladder, so the two could disagree about
		/// whether a provider serves without anything failing.</para>
		///
		/// <para>A <paramref name="restockThreshold"/> of 0 disables the reservation outright: a stationary
		/// cache serves down to its last batch, which is what <c>RemoveBelowSupply</c> already assumes when
		/// it waits for supply 0 to despawn. The two fields are a matched pair — a non-zero reservation
		/// under a lower removal floor strands the provider permanently in the gap between them, holding
		/// supply it will not spend and sitting above the level at which it would clean itself up.</para>
		/// </summary>
		public static bool ReservesRemainderForRestock(
			int currentSupply, int restockThreshold, bool hasActiveTarget, bool keepServingBelowThreshold)
		{
			return currentSupply < restockThreshold && !hasActiveTarget && !keepServingBelowThreshold;
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
