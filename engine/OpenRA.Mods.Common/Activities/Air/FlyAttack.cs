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
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class FlyAttack : Activity, IActivityNotifyStanceChanged, IAttackActivity
	{
		readonly Aircraft aircraft;
		readonly AttackAircraft attackAircraft;
		readonly Rearmable rearmable;
		readonly AttackSource source;
		readonly bool forceAttack;
		readonly Color? targetLineColor;
		readonly WDist strafeDistance;

		Target target;

		Target IAttackActivity.Target => target;
		bool IAttackActivity.ForceAttack => forceAttack;
		AttackSource IAttackActivity.Source => source;

		Target lastVisibleTarget;
		WDist lastVisibleMaximumRange;
		BitSet<TargetableType> lastVisibleTargetTypes;
		Player lastVisibleOwner;
		bool useLastVisibleTarget;
		bool hasTicked;
		bool returnToBase;

		public FlyAttack(Actor self, AttackSource source, in Target target, bool forceAttack, Color? targetLineColor)
		{
			this.source = source;
			this.target = target;
			this.forceAttack = forceAttack;
			this.targetLineColor = targetLineColor;

			aircraft = self.Trait<Aircraft>();
			attackAircraft = self.Trait<AttackAircraft>();
			rearmable = self.TraitOrDefault<Rearmable>();

			strafeDistance = attackAircraft.Info.StrafeRunLength;

			// The target may become hidden between the initial order request and the first tick (e.g. if queued)
			// Moving to any position (even if quite stale) is still better than immediately giving up
			if ((target.Type == TargetType.Actor && target.Actor.CanBeViewedByPlayer(self.Owner))
				|| target.Type == TargetType.FrozenActor || target.Type == TargetType.Terrain)
			{
				lastVisibleTarget = Target.FromPos(target.CenterPosition);
				lastVisibleMaximumRange = attackAircraft.GetMaximumRangeVersusTarget(target);

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
			returnToBase = false;

			// FF - if all ammo is depleted and the current order is not a move order, cancel and return to rearm
			// OR - if just have fired and cannot fire anymore, turn back and attack from same direction again
			if (rearmable != null && attackAircraft.Armaments.All(x => x.IsTraitPaused))
			{
				self.QueueActivity(!attackAircraft.Info.AbortOnResupply, new ReturnToBase(self));
				returnToBase = true;
				return true;
			}

			// Refuse to take off if it would land immediately again.
			if (aircraft.ForceLanding)
				Cancel(self);

			if (IsCanceling)
				return true;

			// Check that AttackFollow hasn't cancelled the target by modifying attack.Target
			// Having both this and AttackFollow modify that field is a horrible hack.
			if (hasTicked && attackAircraft.RequestedTarget.Type == TargetType.Invalid)
				return true;

			if (attackAircraft.IsTraitPaused)
				return false;

			target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
			attackAircraft.SetRequestedTarget(target, forceAttack, null, source);
			hasTicked = true;

			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
			{
				lastVisibleTarget = Target.FromTargetPositions(target);
				lastVisibleMaximumRange = attackAircraft.GetMaximumRangeVersusTarget(target);
				lastVisibleOwner = target.Actor.Owner;
				lastVisibleTargetTypes = target.Actor.GetEnabledTargetTypes();
			}

			// The target may become hidden in the same tick the FlyAttack constructor is called,
			// causing lastVisible* to remain uninitialized.
			// Fix the fallback values based on the frozen actor properties
			else if (target.Type == TargetType.FrozenActor && !lastVisibleTarget.IsValidFor(self))
			{
				lastVisibleTarget = Target.FromTargetPositions(target);
				lastVisibleMaximumRange = attackAircraft.GetMaximumRangeVersusTarget(target);
				lastVisibleOwner = target.FrozenActor.Owner;
				lastVisibleTargetTypes = target.FrozenActor.TargetTypes;
			}

			useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);

			// Target is hidden or dead, and we don't have a fallback position to move towards
			if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self))
				return true;

			// If all valid weapons have depleted their ammo and Rearmable trait exists, return to RearmActor to reload
			// and resume the activity after reloading if AbortOnResupply is set to 'false'
			if (rearmable != null && !useLastVisibleTarget && attackAircraft.Armaments.All(x => x.IsTraitPaused || !x.Weapon.IsValidAgainst(target, self.World, self)))
			{
				// Attack moves never resupply
				if (source == AttackSource.AttackMove)
					return true;

				// AbortOnResupply cancels the current activity (after resupplying) plus any queued activities
				if (attackAircraft.Info.AbortOnResupply)
					NextActivity?.Cancel(self);

				QueueChild(new ReturnToBase(self));
				returnToBase = true;
				return attackAircraft.Info.AbortOnResupply;
			}

			var pos = self.CenterPosition;
			var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;

			// We don't know where the target actually is, so move to where we last saw it
			if (useLastVisibleTarget)
			{
				// HACK: Bot players ignore the standard visibility checks in target.Recalculate,
				// which means that targetIsHiddenActor is always false, allowing lastVisibleMaximumRange
				// to be assigned zero range by attackAircraft.GetMaximumRangeVersusTarget for e.g. cloaked actors.
				// Catch and cancel this edge case to avoid the aircraft stopping mid-air!
				if (self.Owner.IsBot && lastVisibleMaximumRange == WDist.Zero)
					return true;

				// We've reached the assumed position but it is not there - give up
				if (checkTarget.IsInRange(pos, lastVisibleMaximumRange))
					return true;

				// Fly towards the last known position
				QueueChild(new Fly(self, target, WDist.Zero, lastVisibleMaximumRange, checkTarget.CenterPosition, Color.Red));
				return false;
			}

			var delta = attackAircraft.GetTargetPosition(pos, target) - pos;
			var desiredFacing = delta.HorizontalLengthSquared != 0 ? delta.Yaw : aircraft.Facing;

			QueueChild(new TakeOff(self));

			var minimumRange = attackAircraft.Info.AttackType == AirAttackType.Strafe ? WDist.Zero : attackAircraft.GetMinimumRangeVersusTarget(target);

			// Move into range of the target.
			if (!target.IsInRange(pos, lastVisibleMaximumRange) || target.IsInRange(pos, minimumRange))
				QueueChild(aircraft.MoveWithinRange(target, minimumRange, lastVisibleMaximumRange, target.CenterPosition, Color.Red));

			// The aircraft must keep moving forward even if it is already in an ideal position.
			else if (attackAircraft.Info.AttackType == AirAttackType.Strafe)
				QueueChild(new StrafeAttackRun(attackAircraft, aircraft, target, strafeDistance != WDist.Zero ? strafeDistance : lastVisibleMaximumRange));
			else if (attackAircraft.Info.AttackType == AirAttackType.Default && !aircraft.Info.CanHover)
				QueueChild(new FlyAttackRun(target, lastVisibleMaximumRange, attackAircraft));

			// Turn to face the target if required.
			else if (!attackAircraft.TargetInFiringArc(self, target, attackAircraft.Info.FacingTolerance))
				aircraft.Facing = Util.TickFacing(aircraft.Facing, desiredFacing, aircraft.TurnSpeed);

			// In firing arc but keep refining aim toward exact target facing.
			else if (attackAircraft.Info.AlignBodyToTarget)
				aircraft.Facing = Util.TickFacing(aircraft.Facing, desiredFacing, aircraft.TurnSpeed);

			return false;
		}

		protected override void OnLastRun(Actor self)
		{
			// Cancel the requested target, but keep firing on it while in range
			attackAircraft.ClearRequestedTarget();
		}

		void IActivityNotifyStanceChanged.StanceChanged(Actor self, AutoTarget autoTarget, UnitStance oldStance, UnitStance newStance)
		{
			// Cancel non-forced targets when switching to a more restrictive stance if they are no longer valid for auto-targeting
			if (newStance > oldStance || forceAttack)
				return;

			// If lastVisibleTarget is invalid we could never view the target in the first place, so we just drop it here too
			if (!lastVisibleTarget.IsValidFor(self) || !autoTarget.HasValidTargetPriority(self, lastVisibleOwner, lastVisibleTargetTypes))
				attackAircraft.ClearRequestedTarget();
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (targetLineColor != null)
			{
				if (returnToBase)
					foreach (var n in ChildActivity.TargetLineNodes(self))
						yield return n;
				if (!returnToBase || !attackAircraft.Info.AbortOnResupply)
					yield return new TargetLineNode(useLastVisibleTarget ? lastVisibleTarget : target, targetLineColor.Value);
			}
		}
	}

	class FlyAttackRun : Activity
	{
		readonly AttackAircraft attack;
		readonly WDist exitRange;

		Target target;
		bool targetIsVisibleActor;

		public FlyAttackRun(in Target t, WDist exitRange, AttackAircraft attack)
		{
			ChildHasPriority = false;

			target = t;
			this.exitRange = exitRange;
			this.attack = attack;
		}

		protected override void OnFirstRun(Actor self)
		{
			// The target may have died while this activity was queued
			if (target.IsValidFor(self))
			{
				QueueChild(new Fly(self, target, target.CenterPosition));

				// Fly a single tick forward so we have passed the target and start flying out of range facing away from it
				QueueChild(new FlyForward(self, 1));
				QueueChild(new Fly(self, target, exitRange, WDist.MaxValue, target.CenterPosition));
			}
			else
				Cancel(self);
		}

		public override bool Tick(Actor self)
		{
			if (TickChild(self) || IsCanceling)
				return true;

			// Cancel the run if the target become invalid (e.g. killed) while visible
			var targetWasVisibleActor = targetIsVisibleActor;
			target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
			targetIsVisibleActor = target.Type == TargetType.Actor && !targetIsHiddenActor;

			if (targetWasVisibleActor && (!target.IsValidFor(self) || !attack.HasAnyValidWeapons(target)))
				Cancel(self);

			return false;
		}
	}

	class StrafeAttackRun : Activity
	{
		readonly AttackAircraft attackAircraft;
		readonly Aircraft aircraft;
		readonly WDist exitRange;

		Target target;

		// The ground point this pass is committed to, built ONCE in OnFirstRun and re-asserted
		// unchanged every tick. Locking it is what makes a Strafe airframe able to fire at all.
		//
		// PITFALL, and it cost this trait its entire firing ability from the day StrafeAttackRun
		// shipped: rebuilding this every tick made the airframe RE-ACQUIRE its own target every
		// tick. Target.FromTargetPositions is `new Target(t.CenterPosition, t.Positions.ToArray())`
		// (Target.cs:86) — .ToArray() allocates a fresh WPos[] per call — and Target's operator==
		// for TargetType.Terrain compares terrainPositions with ==, i.e. BY REFERENCE
		// (Target.cs:233). So two consecutive rebuilds against a motionless actor never compare
		// equal, Armament.CheckFire:412 treated every tick as a new target and reset AimingDelay to
		// Info.AimingDelay (15 by default, Armament.cs:101/:415), and a counter decremented at most
		// once per tick (Armament.cs:354-355) never reached zero. CanFire then bailed on IsAiming
		// (Armament.cs:392/:751) and CheckFire returned before FireBarrel — so NO AttackType: Strafe
		// airframe could fire, ever, with any weapon, at any range. Measured
		// (test-strafe-engage, 2026-09-02): the airframe flew a correct pass to minDist0 and spent
		// zero of thirty rounds. Assign this field once; do not "refresh" it.
		Target aimPoint;

		public StrafeAttackRun(AttackAircraft attackAircraft, Aircraft aircraft, in Target t, WDist exitRange)
		{
			ChildHasPriority = false;

			target = t;
			this.attackAircraft = attackAircraft;
			this.aircraft = aircraft;
			this.exitRange = exitRange;
		}

		protected override void OnFirstRun(Actor self)
		{
			// The target may have died while this activity was queued
			if (target.IsValidFor(self))
			{
				// A strafing run is a COMMITTED PASS: the aircraft picks a line, dives, fires along
				// it and pulls out. Freezing the aim point here is that commitment expressed in
				// code, and it is deliberately traded against the intra-run tracking the old
				// comment below asked for — a pass no longer curves its fire onto a target that
				// runs. That loss is knowing: a strafe run that follows a fleeing target is not a
				// behaviour anyone asked for, and it is a smaller problem to add back on top of a
				// lane that can shoot than to keep one that cannot.
				//
				// Each new run gets a fresh lock, because FlyAttack.Tick queues a new
				// StrafeAttackRun once this one completes — so "the duration of one run" is exactly
				// the scope of the commitment, and re-acquiring between passes correctly costs one
				// AimingDelay.
				aimPoint = Target.FromTargetPositions(target);

				QueueChild(new Fly(self, target, target.CenterPosition));
				QueueChild(new FlyForward(self, exitRange));

				// Exit the range and then fly enough to turn towards the target for another run
				var distanceToTurn = new WDist(aircraft.Info.Speed * 256 / aircraft.Info.TurnSpeed.Angle);
				QueueChild(new Fly(self, target, exitRange + distanceToTurn, WDist.MaxValue, target.CenterPosition));
			}
			else
				Cancel(self);
		}

		public override bool Tick(Actor self)
		{
			if (TickChild(self) || IsCanceling)
				return true;

			// Strafe attacks target the ground below the original target. Re-assert the SAME value
			// every tick rather than a rebuilt one: the write is still needed, because FlyAttack
			// sets its own Actor target whenever it ticks (FlyAttack.cs:108) and this must win, but
			// passing the identical struct keeps terrainPositions reference-equal so CheckFire sees
			// one continuous engagement instead of a new target per tick.
			//
			// Unconditional now, where the rebuild was gated on the target still being a visible
			// Actor. That gate is what actually broke "keep the previous one if it dies or
			// disappears": once the target died the requested target simply stopped being
			// refreshed. A committed pass keeps shooting the ground it aimed at.
			if (aimPoint.Type != TargetType.Invalid)
				attackAircraft.SetRequestedTarget(aimPoint, true);

			return false;
		}
	}
}
