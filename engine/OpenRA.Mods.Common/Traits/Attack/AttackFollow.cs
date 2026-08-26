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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Actor will follow units until in range to attack them.")]
	public class AttackFollowInfo : AttackBaseInfo
	{
		[Desc("Automatically acquire and fire on targets of opportunity when not actively attacking.")]
		public readonly bool OpportunityFire = true;

		[Desc("Keep firing on targets even after attack order is cancelled")]
		public readonly bool PersistentTargeting = true;

		[Desc("Range to stay away from min and max ranges to give some leeway if the target starts moving.")]
		public readonly WDist RangeMargin = WDist.FromCells(1);

		public override object Create(ActorInitializer init) { return new AttackFollow(init.Self, this); }
	}

	public class AttackFollow : AttackBase, INotifyOwnerChanged, IOverrideAutoTarget, INotifyStanceChanged
	{
		public new readonly AttackFollowInfo Info;
		public Target RequestedTarget { get; private set; }
		public Target OpportunityTarget { get; private set; }

		Mobile mobile;
		AutoTarget autoTarget;
		bool requestedForceAttack;
		Activity requestedTargetPresetForActivity;
		bool opportunityForceAttack;
		bool opportunityTargetIsPersistentTarget;

		// Which code path committed us to the current targets. Needed because these targets are handed
		// back to AutoTarget.ScanForTarget ahead of its own scan (IOverrideAutoTarget), and an
		// automatic engagement may be replaced by a higher-priority one while a player/Lua/bot order
		// may NOT. Defaults to Default (= never yield) so any caller that does not say otherwise is
		// treated as deliberate.
		AttackSource requestedTargetSource = AttackSource.Default;
		AttackSource opportunityTargetSource = AttackSource.Default;

		public void SetRequestedTarget(in Target target, bool isForceAttack = false, Activity requestedTargetPreset = null,
			AttackSource source = AttackSource.Default)
		{
			RequestedTarget = target;
			requestedForceAttack = isForceAttack;
			requestedTargetPresetForActivity = requestedTargetPreset;
			requestedTargetSource = source;
		}

		public void ClearRequestedTarget()
		{
			if (Info.PersistentTargeting)
			{
				// PITFALL: this does not clear — it PROMOTES. The target the attack activity just
				// finished with becomes a persistent opportunity target, which TryGetAutoTargetOverride
				// then hands back to every subsequent AutoTarget scan. Carry the source across or the
				// promoted target looks deliberate and can never be re-evaluated.
				OpportunityTarget = RequestedTarget;
				opportunityForceAttack = requestedForceAttack;
				opportunityTargetIsPersistentTarget = true;
				opportunityTargetSource = requestedTargetSource;
			}

			RequestedTarget = Target.Invalid;
			requestedTargetPresetForActivity = null;
			requestedTargetSource = AttackSource.Default;
		}

		public AttackFollow(Actor self, AttackFollowInfo info)
			: base(self, info)
		{
			Info = info;
		}

		protected override void Created(Actor self)
		{
			mobile = self.TraitOrDefault<Mobile>();
			autoTarget = self.TraitOrDefault<AutoTarget>();
			base.Created(self);
		}

		/// <summary>Actor can be viewed, is within min/max range, is not blocked, and TargetInFiringArc </summary>
		protected bool CanAimAtTarget(Actor self, in Target target, bool forceAttack)
		{
			if (target.Type == TargetType.Actor && !target.Actor.CanBeViewedByPlayer(self.Owner))
				return false;

			if (target.Type == TargetType.FrozenActor && !target.FrozenActor.IsValid)
				return false;

			var pos = self.CenterPosition;
			var armaments = ChooseArmamentsForTarget(target, forceAttack);
			foreach (var a in armaments)
				if (target.IsInRange(pos, a.MaxRange()) && (a.Weapon.MinRange == WDist.Zero || !target.IsInRange(pos, a.Weapon.MinRange)))
					if (TargetInFiringArc(self, target, Info.FacingTolerance)) // Make sure target is valid or there can be an error in target.CenterPosition ?
						return true;

			return false;
		}

		protected override void Tick(Actor self)
		{
			if (IsTraitDisabled)
			{
				RequestedTarget = OpportunityTarget = Target.Invalid;
				opportunityTargetIsPersistentTarget = false;
				requestedTargetSource = opportunityTargetSource = AttackSource.Default;
			}

			if (requestedTargetPresetForActivity != null)
			{
				// RequestedTarget was set by OnQueueAttackActivity in preparation for a queued activity
				// requestedTargetPresetForActivity will be cleared once the activity starts running and calls UpdateRequestedTarget
				if (self.CurrentActivity != null && self.CurrentActivity.NextActivity == requestedTargetPresetForActivity)
				{
					RequestedTarget = RequestedTarget.Recalculate(self.Owner, out _);
				}

				// Requested activity has been canceled
				else
					ClearRequestedTarget();
			}

			// Can't fire on anything
			if (mobile != null && !mobile.CanInteractWithGroundLayer(self))
				return;

			// Drop a locked REQUESTED target the moment it acquires the break-off condition (critical
			// damage in WW3MOD). The opportunity-target twin of this guard is in the else branch below,
			// and Activities/Attack.cs:217 is the AttackFrontal twin — this path had neither. Every
			// attack reaches AttackFollow as RequestedTarget via OnResolveAttackOrder, INCLUDING one
			// AutoTarget picked for itself, and AttackFollow.AttackActivity only manages range and
			// movement — firing happens here. Before this, an AttackFollow unit could leave a doomed
			// target only through AutoTarget.TickPreemption, which switches solely to a STRICTLY higher
			// priority band and so cannot fire when the doomed target already sits in the top band.
			//
			// POPULATION: everything deriving from AttackFollow, not just turrets. AttackTurreted (35
			// declarations, though ~8 are husk stubs on Weapon: Dummy and permanently paused),
			// AttackAircraft (11 — no Tick override, and FlyAttack.cs:108 calls SetRequestedTarget with
			// the source, so this fires mid attack-run), and AttackGarrisoned (4 — overrides Tick but
			// calls base.Tick). Aircraft and garrisoned infantry are IN SCOPE and their post-clear
			// behaviour mid-run is unanalysed; queued for observation.
			//
			// Symptom reported from playtest 260826: a Tunguska walks a helicopter to Critical with its
			// 30mm, the heli enters HeliEmergencyLanding's crash descent (guaranteed dead within
			// CruiseAltitude/CrashDescentRate = 1280/50 = 26 ticks), and the Tunguska spends a
			// 65-supply 9M311 on it anyway.
			//
			// Scoped through BreakOffApplies, so a player / Lua / deliberate bot order still fires:
			// refusing those is the shipped defect BreakOffScopeTest pins. Clearing rather than merely
			// declining to fire is deliberate for the same reason — a unit that keeps the target and
			// silently skips DoAttack aims at something it will never shoot and is never idle, so it
			// never rescans. ChooseTarget already skips break-off targets, so the rescan this frees
			// picks a healthy one or holds fire.
			//
			// COUPLING, load-bearing and non-obvious: ClearRequestedTarget does not clear under
			// PersistentTargeting — it PROMOTES the target to OpportunityTarget (:69-79). What undoes
			// that is the opportunity guard below, and only by coincidence: its predicate is
			// `!opportunityForceAttack`, a DIFFERENT test from this one, which today happens to be
			// satisfied because BreakOffApplies already implies !forceAttack. Widen BreakOffApplies to
			// admit any force-attack case and the promotion survives silently, locking the unit onto the
			// doomed target — the exact failure this guard exists to prevent. Change both together, or
			// give them one shared predicate.
			if (RequestedTarget.Type == TargetType.Actor
				&& BreakOffApplies(requestedTargetSource, requestedForceAttack)
				&& autoTarget != null
				&& !string.IsNullOrEmpty(autoTarget.Info.BreakOffCondition)
				&& RequestedTarget.Actor.GetConditionCount(autoTarget.Info.BreakOffCondition) > 0)
				ClearRequestedTarget();

			if (RequestedTarget.IsValidFor(self))
			{
				// Gate IsAiming on the same checks as fire (HoldFireWhileMoving, SetupTicks).
				// Without this, the turret keeps tracking the target while the unit is rolling
				// to its destination cell or in setup countdown — visually contradicting the
				// "stop, deploy, then aim, then fire" sequence.
				IsAiming = CanAimAtTarget(self, RequestedTarget, requestedForceAttack)
					&& ReadyToEngage(self, RequestedTarget);
				if (IsAiming)
					DoAttack(self, RequestedTarget, isManualTarget: true);
			}
			else
			{
				IsAiming = false;

				// Drop a locked opportunity target the moment it acquires the break-off
				// condition (critical damage in WW3MOD) — except for opportunity force-attacks,
				// which are persistent player-issued orders that should keep firing.
				if (OpportunityTarget.Type == TargetType.Actor && !opportunityForceAttack
					&& autoTarget != null
					&& !string.IsNullOrEmpty(autoTarget.Info.BreakOffCondition)
					&& OpportunityTarget.Actor.GetConditionCount(autoTarget.Info.BreakOffCondition) > 0)
					OpportunityTarget = Target.Invalid;

				if (OpportunityTarget.IsValidFor(self))
					IsAiming = CanAimAtTarget(self, OpportunityTarget, opportunityForceAttack)
						&& ReadyToEngage(self, OpportunityTarget);

				if (!IsAiming && Info.OpportunityFire && autoTarget != null &&
				    !autoTarget.IsTraitDisabled && autoTarget.Stance >= UnitStance.FireAtWill)
				{
					OpportunityTarget = autoTarget.ScanForTarget(self, false, false);
					opportunityForceAttack = false;
					opportunityTargetIsPersistentTarget = false;
					opportunityTargetSource = AttackSource.AutoTarget;

					if (OpportunityTarget.IsValidFor(self))
					{
						IsAiming = CanAimAtTarget(self, OpportunityTarget, opportunityForceAttack)
							&& ReadyToEngage(self, OpportunityTarget);

						// Opportunity fire doesn't go through AttackTarget — mark explicitly so
						// other units' scans see this target as committed.
						if (IsAiming)
							AutoTarget.MarkTargetForAttack(self, OpportunityTarget);
					}
				}

				if (IsAiming)
					DoAttack(self, OpportunityTarget, isManualTarget: false);
			}

			base.Tick(self);
		}

		public override Activity GetAttackActivity(Actor self, AttackSource source, in Target newTarget, bool allowMove, bool forceAttack, Color? targetLineColor = null)
		{
			// HACK: Manually set force attacking if we persisted an opportunity target that required force attacking
			if (opportunityTargetIsPersistentTarget && opportunityForceAttack && newTarget == OpportunityTarget)
				forceAttack = true;

			return new AttackActivity(self, source, newTarget, allowMove, forceAttack, targetLineColor);
		}

		public override void OnResolveAttackOrder(Actor self, Activity activity, in Target target, bool queued, bool forceAttack)
		{
			// We can improve responsiveness for turreted actors by preempting
			// the last order (usually a move) and setting the target immediately
			if (!queued)
				SetRequestedTarget(target, forceAttack, activity,
					(activity as IAttackActivity)?.Source ?? AttackSource.Default);
		}

		public override void OnStopOrder(Actor self)
		{
			RequestedTarget = OpportunityTarget = Target.Invalid;
			opportunityTargetIsPersistentTarget = false;
			requestedTargetSource = opportunityTargetSource = AttackSource.Default;
			base.OnStopOrder(self);
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			RequestedTarget = OpportunityTarget = Target.Invalid;
			opportunityTargetIsPersistentTarget = false;
			requestedTargetSource = opportunityTargetSource = AttackSource.Default;
		}

		bool IOverrideAutoTarget.TryGetAutoTargetOverride(Actor self, out Target target, out bool canYieldToHigherPriority)
		{
			if (RequestedTarget.Type != TargetType.Invalid)
			{
				target = RequestedTarget;
				canYieldToHigherPriority = AutoTarget.IsAutoAcquiredSource(requestedTargetSource) && !requestedForceAttack;
				return true;
			}

			// IsValidFor, not Type != Invalid. Type already rejects a dead, removed or regenerated
			// actor (Target.cs:91-108), but not one that has become untargetable — cloaked, say. Such
			// a target would be handed back ahead of the scan and then rejected by AttackTarget's own
			// IsValidFor, leaving the unit stuck holding a target it cannot act on.
			if (opportunityTargetIsPersistentTarget && OpportunityTarget.IsValidFor(self))
			{
				target = OpportunityTarget;
				canYieldToHigherPriority = AutoTarget.IsAutoAcquiredSource(opportunityTargetSource) && !opportunityForceAttack;
				return true;
			}

			target = Target.Invalid;
			canYieldToHigherPriority = false;
			return false;
		}

		void INotifyStanceChanged.StanceChanged(Actor self, AutoTarget autoTarget, UnitStance oldStance, UnitStance newStance)
		{
			// Cancel opportunity targets when switching to a more restrictive stance if they are no longer valid for auto-targeting
			if (newStance > oldStance || opportunityForceAttack)
				return;

			if (OpportunityTarget.Type == TargetType.Actor)
			{
				var a = OpportunityTarget.Actor;
				if (!autoTarget.HasValidTargetPriority(self, a.Owner, a.GetEnabledTargetTypes()))
					OpportunityTarget = Target.Invalid;
			}
			else if (OpportunityTarget.Type == TargetType.FrozenActor)
			{
				var fa = OpportunityTarget.FrozenActor;
				if (!autoTarget.HasValidTargetPriority(self, fa.Owner, fa.TargetTypes))
					OpportunityTarget = Target.Invalid;
			}
		}

		internal class AttackActivity : Activity, IActivityNotifyStanceChanged, IAttackActivity
		{
			readonly AttackFollow attack;
			readonly Vision[] vision;
			readonly IMove move;
			readonly AttackSource source;
			readonly bool forceAttack;
			readonly Color? targetLineColor;

			Target target;

			Target IAttackActivity.Target => target;
			bool IAttackActivity.ForceAttack => forceAttack;
			AttackSource IAttackActivity.Source => source;

			Target lastVisibleTarget;
			bool useLastVisibleTarget;
			WDist lastVisibleMaximumRange;
			WDist lastVisibleMinimumRange;
			BitSet<TargetableType> lastVisibleTargetTypes;
			Player lastVisibleOwner;
			bool wasMovingWithinRange;
			bool hasTicked;

			public AttackActivity(Actor self, in Target target, bool allowMove, bool forceAttack, Color? targetLineColor = null)
				: this(self, AttackSource.Default, target, allowMove, forceAttack, targetLineColor) { }

			public AttackActivity(Actor self, AttackSource source, in Target target, bool allowMove, bool forceAttack, Color? targetLineColor = null)
			{
				attack = self.Trait<AttackFollow>();
				move = allowMove ? self.TraitOrDefault<IMove>() : null;
				vision = self.TraitsImplementing<Vision>().ToArray();

				this.source = source;
				this.target = target;
				this.forceAttack = forceAttack;
				this.targetLineColor = targetLineColor;

				// The target may become hidden between the initial order request and the first tick (e.g. if queued)
				// Moving to any position (even if quite stale) is still better than immediately giving up
				if ((target.Type == TargetType.Actor && target.Actor.CanBeViewedByPlayer(self.Owner))
				    || target.Type == TargetType.FrozenActor || target.Type == TargetType.Terrain)
				{
					lastVisibleTarget = Target.FromPos(target.CenterPosition);
					lastVisibleMaximumRange = attack.GetMaximumRangeVersusTarget(target);
					lastVisibleMinimumRange = attack.GetMinimumRangeVersusTarget(target);

					if (target.Type == TargetType.Actor)
					{
						lastVisibleOwner = target.Actor.Owner;
						lastVisibleTargetTypes = target.Actor.GetEnabledTargetTypes();
					}
					else if (target.Type == TargetType.FrozenActor)
					{
						lastVisibleOwner = target.FrozenActor.Owner;
						lastVisibleTargetTypes = target.FrozenActor.TargetTypes;
					}
				}
			}

			public override bool Tick(Actor self)
			{
				if (IsCanceling)
					return true;

				// PITFALL: the turreted twin of the guard in Activities/Attack.cs. A dry unit otherwise
				// closes to range and then parks on the `return false` at the end of this method forever
				// — never firing (the armament is ammo-paused), never idle, so never resupplying.
				if (AmmoPool.CannotFight(self))
					return true;

				// Check that AttackFollow hasn't cancelled the target by modifying attack.Target
				// Having both this and AttackFollow modify that field is a horrible hack.
				if (hasTicked && attack.RequestedTarget.Type == TargetType.Invalid)
					return true;

				if (attack.IsTraitPaused)
					return false;

				target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
				attack.SetRequestedTarget(target, forceAttack, null, source);
				hasTicked = true;

				if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				{
					lastVisibleTarget = Target.FromTargetPositions(target);
					lastVisibleMaximumRange = attack.GetMaximumRangeVersusTarget(target);
					lastVisibleMinimumRange = attack.GetMinimumRange();
					lastVisibleOwner = target.Actor.Owner;
					lastVisibleTargetTypes = target.Actor.GetEnabledTargetTypes();

					var leeway = attack.Info.RangeMargin.Length;
					if (leeway != 0 && move != null && target.Actor.Info.HasTraitInfo<IMoveInfo>())
					{
						var preferMinRange = Math.Min(lastVisibleMinimumRange.Length + leeway, lastVisibleMaximumRange.Length);
						var preferMaxRange = Math.Max(lastVisibleMaximumRange.Length - leeway, lastVisibleMinimumRange.Length);
						lastVisibleMaximumRange = new WDist((lastVisibleMaximumRange.Length - leeway).Clamp(preferMinRange, preferMaxRange));
					}
				}

				// The target may become hidden in the same tick the AttackActivity constructor is called,
				// causing lastVisible* to remain uninitialized.
				// Fix the fallback values based on the frozen actor properties
				else if (target.Type == TargetType.FrozenActor && !lastVisibleTarget.IsValidFor(self))
				{
					lastVisibleTarget = Target.FromTargetPositions(target);
					lastVisibleMaximumRange = attack.GetMaximumRangeVersusTarget(target);
					lastVisibleOwner = target.FrozenActor.Owner;
					lastVisibleTargetTypes = target.FrozenActor.TargetTypes;
				}

				var maxRange = lastVisibleMaximumRange;
				var minRange = lastVisibleMinimumRange;
				useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);

				// Most actors want to be able to see their target before shooting
				if (target.Type == TargetType.FrozenActor && !attack.Info.TargetFrozenActors && !forceAttack)
				{
					var rs = vision
						.Where(t => !t.IsTraitDisabled)
						.MaxByOrDefault(s => s.Range);

					// Default to 2 cells if there are no active traits
					var sightRange = rs != null ? rs.Range : WDist.FromCells(2);
					if (sightRange < maxRange)
						maxRange = sightRange;
				}

				// If we are ticking again after previously sequencing a MoveWithRange then that move must have completed
				// Either we are in range and can see the target, or we've lost track of it and should give up
				if (wasMovingWithinRange && targetIsHiddenActor)
					return true;

				// Target is hidden or dead, and we don't have a fallback position to move towards
				if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self))
					return true;

				var pos = self.CenterPosition;
				var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;

				// We've reached the required range - if the target is visible and valid then we wait
				// otherwise if it is hidden or dead we give up
				var losThreshold = FiringLOS.GetBestThreshold(self, checkTarget);
				if (checkTarget.IsInRange(pos, maxRange) && !checkTarget.IsInRange(pos, minRange)
					&& checkTarget.Type != TargetType.Invalid
					&& FiringLOS.HasClearLOS(self, checkTarget, losThreshold))
				{
					if (useLastVisibleTarget)
						return true;

					return false;
				}

				// We can't move into range, so give up
				if (move == null || maxRange == WDist.Zero || maxRange < minRange)
					return true;

				wasMovingWithinRange = true;
				QueueChild(move.MoveWithinRange(target, minRange, maxRange, checkTarget.CenterPosition));
				return false;
			}

			protected override void OnLastRun(Actor self)
			{
				// Cancel the requested target, but keep firing on it while in range.
				//
				// IMPORTANT: when a new attack activity has been queued behind us
				// (e.g. the player issued a force-attack-ground while our setup-aim
				// countdown was running and we ended naturally before they fired),
				// OnResolveAttackOrder has already set RequestedTarget to the new
				// target. Clearing here would silently eat the player's order — the
				// classic symptom is artillery showing the red attack waypoint but
				// never firing on the new ground point.
				//
				// IsCanceling is unreliable here: by the time OnLastRun runs the
				// state has already moved to Done, so IsCanceling is always false.
				// Use NextActivity instead — if there's an AttackActivity queued
				// after us, the firing pipeline already has its new target.
				if (NextActivity is AttackActivity)
					return;

				attack.ClearRequestedTarget();
			}

			void IActivityNotifyStanceChanged.StanceChanged(Actor self, AutoTarget autoTarget, UnitStance oldStance, UnitStance newStance)
			{
				// Cancel non-forced targets when switching to a more restrictive stance if they are no longer valid for auto-targeting
				if (newStance > oldStance || forceAttack)
					return;

				// If lastVisibleTarget is invalid we could never view the target in the first place, so we just drop it here too
				if (!lastVisibleTarget.IsValidFor(self) || !autoTarget.HasValidTargetPriority(self, lastVisibleOwner, lastVisibleTargetTypes))
					attack.ClearRequestedTarget();
			}

			public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
			{
				if (targetLineColor != null)
					yield return new TargetLineNode(useLastVisibleTarget ? lastVisibleTarget : target, targetLineColor.Value);
			}
		}
	}
}
