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
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/* non-turreted attack */
	public class Attack : Activity, IActivityNotifyStanceChanged, IActivityNotifyEngagementStanceChanged, IAttackActivity
	{
		Target IAttackActivity.Target => target;
		bool IAttackActivity.ForceAttack => forceAttack;

		[Flags]
		protected enum AttackStatus { UnableToAttack, NeedsToTurn, NeedsToMove, Attacking }

		readonly IEnumerable<AttackFrontal> attackTraits;
		readonly Vision[] vision;
		readonly IMove move;
		readonly IFacing facing;
		readonly IPositionable positionable;
		readonly bool forceAttack;
		readonly bool callerAllowedMove;
		readonly Color? targetLineColor;
		readonly AutoTarget autoTarget;

		protected Target target;
		Target lastVisibleTarget;
		WDist lastVisibleMaximumRange;
		BitSet<TargetableType> lastVisibleTargetTypes;
		Player lastVisibleOwner;
		bool useLastVisibleTarget;
		bool wasMovingWithinRange;

		WDist minRange;
		WDist maxRange;
		AttackStatus attackStatus = AttackStatus.UnableToAttack;

		/* protected INotifyNewTarget[] notifyNewTarget;
		Target? oldTarget = null; */

		public Attack(Actor self, in Target target, bool allowMovement, bool forceAttack, Color? targetLineColor = null)
		{
			this.target = target;
			this.targetLineColor = targetLineColor;
			this.forceAttack = forceAttack;
			callerAllowedMove = allowMovement;

			attackTraits = self.TraitsImplementing<AttackFrontal>().ToArray().Where(t => !t.IsTraitDisabled);
			vision = self.TraitsImplementing<Vision>().ToArray();
			facing = self.Trait<IFacing>();
			positionable = self.Trait<IPositionable>();
			autoTarget = self.TraitOrDefault<AutoTarget>();

			move = allowMovement ? self.TraitOrDefault<IMove>() : null;

			// The target may become hidden between the initial order request and the first tick (e.g. if queued)
			// Moving to any position (even if quite stale) is still better than immediately giving up
			if ((target.Type == TargetType.Actor && target.Actor.CanBeViewedByPlayer(self.Owner))
			    || target.Type == TargetType.FrozenActor || target.Type == TargetType.Terrain)
			{
				lastVisibleTarget = Target.FromPos(target.CenterPosition);

				// Lambdas can't use 'in' variables, so capture a copy for later
				var rangeTarget = target;
				lastVisibleMaximumRange = attackTraits.Min(x => x.GetMaximumRangeVersusTarget(rangeTarget));

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

		protected virtual Target RecalculateTarget(Actor self, out bool targetIsHiddenActor)
		{
			return target.Recalculate(self.Owner, out targetIsHiddenActor);
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			if (!attackTraits.Any())
			{
				Cancel(self);
				return false;
			}

			target = RecalculateTarget(self, out var targetIsHiddenActor);

			// FF
			// Never got used in the end but seems to work
			/* if (!target.Equals(oldTarget))
			{
				oldTarget = target;
				foreach (var n in notifyNewTarget)
					n.Acquired(self);
			} */

			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
			{
				lastVisibleTarget = Target.FromTargetPositions(target);
				lastVisibleMaximumRange = attackTraits.Min(x => x.GetMaximumRangeVersusTarget(target));

				lastVisibleOwner = target.Actor.Owner;
				lastVisibleTargetTypes = target.Actor.GetEnabledTargetTypes();
			}

			useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);

			// FF: Fire-and-forget weapons (e.g. ballistic missile launchers) commit to all
			// loaded shots — if the target dies mid-salvo we keep firing at the last known
			// position instead of canceling. PersistsAtLastTargetPosition opts in.
			if (useLastVisibleTarget && lastVisibleTarget.IsValidFor(self)
				&& attackTraits.Any(at => at.Info.PersistsAtLastTargetPosition))
			{
				target = lastVisibleTarget;
				useLastVisibleTarget = false;
			}

			// If we are ticking again after previously sequencing a MoveWithRange then that move must have completed
			// Either we are in range and can see the target, or we've lost track of it and should give up
			if (wasMovingWithinRange && targetIsHiddenActor)
				return true;

			// Target is hidden or dead, and we don't have a fallback position to move towards
			if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self))
				return true;

			wasMovingWithinRange = false;
			var pos = self.CenterPosition;
			var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;

			// We don't know where the target actually is, so move to where we last saw it
			if (useLastVisibleTarget)
			{
				// We've reached the assumed position but it is not there or we can't move any further - give up
				if (checkTarget.IsInRange(pos, lastVisibleMaximumRange) || move == null || lastVisibleMaximumRange == WDist.Zero)
					return true;

				// Move towards the last known position
				wasMovingWithinRange = true;
				QueueChild(move.MoveWithinRange(target, WDist.Zero, lastVisibleMaximumRange, checkTarget.CenterPosition, Color.Red));
				return false;
			}

			attackStatus = AttackStatus.UnableToAttack;

			foreach (var attack in attackTraits)
			{
				var status = TickAttack(self, attack);
				attack.IsAiming = status == AttackStatus.Attacking || status == AttackStatus.NeedsToTurn;
			}

			if (attackStatus.HasFlag(AttackStatus.NeedsToMove))
				wasMovingWithinRange = true;

			if (attackStatus >= AttackStatus.NeedsToTurn)
				return false;

			return true;
		}

		protected override void OnLastRun(Actor self)
		{
			foreach (var attack in attackTraits)
				attack.IsAiming = false;
		}

		protected virtual AttackStatus TickAttack(Actor self, AttackFrontal attack)
		{
			if (!target.IsValidFor(self))
				return AttackStatus.UnableToAttack;

			if (attack.Info.AttackRequiresEnteringCell && !positionable.CanEnterCell(target.Actor.Location, null, BlockedByActor.None))
				return AttackStatus.UnableToAttack;

			if (!attack.Info.TargetFrozenActors && !forceAttack && target.Type == TargetType.FrozenActor)
			{
				// Try to move within range, drop the target otherwise
				if (move == null)
					return AttackStatus.UnableToAttack;

				var rs = vision
					.Where(t => !t.IsTraitDisabled)
					.MaxByOrDefault(s => s.Range);

				// Default to 2 cells if there are no active traits
				var sightRange = rs != null ? rs.Range : WDist.FromCells(2);

				attackStatus |= AttackStatus.NeedsToMove;
				QueueChild(move.MoveWithinRange(target, sightRange, target.CenterPosition, Color.Red));
				return AttackStatus.NeedsToMove;
			}

			// Drop the target once none of the weapons are effective against it
			var armaments = attack.ChooseArmamentsForTarget(target, forceAttack).ToList();
			if (armaments.Count == 0)
				return AttackStatus.UnableToAttack;

			// Update ranges
			minRange = armaments.Max(a => a.Weapon.MinRange);
			maxRange = armaments.Min(a => a.MaxRange());

			var pos = self.CenterPosition;
			var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;

			var engStance = autoTarget?.EngagementStanceValue ?? EngagementStance.Defensive;

			var outOfRange = !target.IsInRange(pos, maxRange);
			var tooClose = minRange.Length != 0 && target.IsInRange(pos, minRange);
			var cantInteract = move is Mobile mobile && !mobile.CanInteractWithGroundLayer(self);
			var losThreshold = FiringLOS.GetBestThreshold(self, checkTarget);
			var losBlocked = checkTarget.Type != TargetType.Invalid
				&& !FiringLOS.HasClearLOS(self, checkTarget, losThreshold);

			var needsToMove = outOfRange || tooClose || cantInteract || losBlocked;

			if (needsToMove)
			{
				// Engagement stance movement restrictions only apply for auto-targeted attacks.
				// Direct player attack orders (callerAllowedMove=true) always move to range,
				// since AutoTarget already gates allowMove based on stance before creating this activity.
				if (!callerAllowedMove)
				{
					if (engStance == EngagementStance.HoldPosition)
						return AttackStatus.UnableToAttack;

					if (engStance == EngagementStance.Defensive && outOfRange && !tooClose && !losBlocked)
						return AttackStatus.UnableToAttack;
				}

				// Try to move within range, drop the target otherwise
				if (move == null)
					return AttackStatus.UnableToAttack;

				attackStatus |= AttackStatus.NeedsToMove;

				if (checkTarget.Type != TargetType.Invalid)
				{
					QueueChild(move.MoveWithinRange(target, minRange, maxRange, checkTarget.CenterPosition, Color.Red));
					return AttackStatus.NeedsToMove;
				}
			}

			var desiredFacing = (attack.GetTargetPosition(pos, target) - pos).Yaw;

			if (!attack.TargetInFiringArc(self, target, attack.Info.FacingTolerance))
			{
				// Don't queue a turn activity: Executing a child takes an additional tick during which the target may have moved again
				// FacingLockCondition: hold position and don't turn — the launch sequence has already committed to a heading.
				if (!attack.IsFacingLocked)
					facing.Facing = Util.TickFacing(facing.Facing, desiredFacing, facing.TurnSpeed);

				// Check again if we turned enough and directly continue attacking if we did
				if (!attack.TargetInFiringArc(self, target, attack.Info.FacingTolerance))
				{
					attackStatus |= AttackStatus.NeedsToTurn;
					return AttackStatus.NeedsToTurn;
				}
			}
			else if (attack.Info.AlignBodyToTarget && !attack.IsFacingLocked)
			{
				// Already in firing arc but keep refining aim toward exact facing
				facing.Facing = Util.TickFacing(facing.Facing, desiredFacing, facing.TurnSpeed);
			}

			attackStatus |= AttackStatus.Attacking;
			DoAttack(self, attack, armaments);

			return AttackStatus.Attacking;
		}

		protected virtual void DoAttack(Actor self, AttackFrontal attack, IEnumerable<Armament> armaments)
		{
			if (!attack.IsTraitPaused)
				foreach (var a in armaments)
					a.CheckFire(self, facing, target);
		}

		void IActivityNotifyStanceChanged.StanceChanged(Actor self, AutoTarget autoTarget, UnitStance oldStance, UnitStance newStance)
		{
			// Cancel non-forced targets when switching to a more restrictive stance if they are no longer valid for auto-targeting
			if (newStance > oldStance || forceAttack)
				return;

			// If lastVisibleTarget is invalid we could never view the target in the first place, so we just drop it here too
			if (!lastVisibleTarget.IsValidFor(self) || !autoTarget.HasValidTargetPriority(self, lastVisibleOwner, lastVisibleTargetTypes))
				target = Target.Invalid;
		}

		void IActivityNotifyEngagementStanceChanged.EngagementStanceChanged(Actor self, AutoTarget autoTarget, EngagementStance oldStance, EngagementStance newStance)
		{
			// When switching to HoldPosition, cancel any auto-movement toward targets
			if (newStance == EngagementStance.HoldPosition && !forceAttack && wasMovingWithinRange)
				target = Target.Invalid;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (targetLineColor != null)
				yield return new TargetLineNode(useLastVisibleTarget ? lastVisibleTarget : target, targetLineColor.Value);
		}
	}
}
