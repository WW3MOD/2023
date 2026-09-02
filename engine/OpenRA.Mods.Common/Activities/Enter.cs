#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public enum EnterBehaviour { Exit, Suicide, Dispose }

	public abstract class Enter : Activity
	{
		enum EnterState { Approaching, Entering, Exiting, Finished }

		readonly IMove move;
		readonly Color? targetLineColor;
		readonly MoveCooldownHelper moveCooldownHelper;

		/// <summary>Evidence that the approach will never arrive. Shared with AutoFollowAlly and
		/// AttendAllyActivity rather than re-derived here — the predicate has traps in it that two copies
		/// would drift apart on.</summary>
		readonly StallWatcher approachStall = new();

		/// <summary>World tick of the previous approach-stall check, so the budget is spent in real ticks.
		/// The check below runs only when an approach move has just ended, which is every other tick at
		/// this activity's (0, 1) cooldown and rarer behind a long move — passing 1 would make the budget
		/// mean whatever the caller's cadence happened to be.</summary>
		int lastApproachCheckTick = -1;

		Target target;
		Target lastVisibleTarget;
		bool useLastVisibleTarget;
		EnterState lastState = EnterState.Approaching;

		/// <summary>
		/// Ticks of approach without gaining a single cell before the order is abandoned. 0 disables the
		/// bound for a subclass that has a better test of its own.
		/// </summary>
		/// <remarks>
		/// Matches Cargo.BlockedUnloadTimeout deliberately: that is the only figure anyone in this
		/// codebase has committed to for "how long do we keep trying something that may be impossible",
		/// and a player who watches a technician give up and a transport give up wants one answer to
		/// "how patient is this game", not two. ~30 s at the mod's 16.67 tps.
		/// </remarks>
		public const int DefaultMaxStalledApproachTicks = 500;

		protected virtual int MaxStalledApproachTicks => DefaultMaxStalledApproachTicks;

		protected Enter(Actor self, in Target target, Color? targetLineColor = null)
		{
			move = self.Trait<IMove>();
			this.target = target;
			this.targetLineColor = targetLineColor;
			ChildHasPriority = false;
			// Cooldown collapsed to (0, 1) — the visible 0.8-1.2s pause "outside the building"
			// before entering came from MoveCooldownHelper's default (20, 31) cooldown firing
			// when the destination cell registered as blocked (the building's own cell).
			// RetryIfDestinationBlocked stays true so genuine blocks still abort cleanly via
			// TryStartEnter's Cancel path; just don't make the player wait between retries.
			moveCooldownHelper = new MoveCooldownHelper(self.World, move as Mobile)
			{
				RetryIfDestinationBlocked = true,
				Cooldown = (0, 1)
			};

			approachStall.MarkProgress(self.Location);
		}

		/// <summary>
		/// Called early in the activity tick to allow subclasses to update state.
		/// Call Cancel(self, true) if it is no longer valid to enter.
		/// </summary>
		protected virtual void TickInner(Actor self, in Target target, bool targetIsDeadOrHiddenActor) { }

		/// <summary>
		/// Called when the actor is ready to transition from approaching to entering the target actor.
		/// Return true to start entering, or false to wait in the WaitingToEnter state.
		/// Call Cancel(self, true) before returning false if it is no longer valid to enter.
		/// </summary>
		protected virtual bool TryStartEnter(Actor self, Actor targetActor) { return true; }

		/// <summary>
		/// Called when the actor has entered the target actor.
		/// Actor will be Killed/Disposed or they will enter/exit unharmed.
		/// Depends on either the EnterBehaviour of the actor or the requirements of an overriding function.
		/// </summary>
		protected virtual void OnEnterComplete(Actor self, Actor targetActor) { }

		public override bool Tick(Actor self)
		{
			// Update our view of the target
			target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				lastVisibleTarget = Target.FromTargetPositions(target);

			useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);

			// Cancel immediately if the target died while we were entering it
			if (!IsCanceling && useLastVisibleTarget && lastState == EnterState.Entering)
				Cancel(self, true);

			TickInner(self, target, useLastVisibleTarget);

			// We need to wait for movement to finish before transitioning to
			// the next state or next activity
			if (!TickChild(self))
				return false;

			var result = moveCooldownHelper.Tick(targetIsHiddenActor);
			if (result != null)
				return result.Value;

			// Note that lastState refers to what we have just *finished* doing
			switch (lastState)
			{
				case EnterState.Approaching:
				{
					// NOTE: We can safely cancel in this case because we know the
					// actor has finished any in-progress move activities
					if (IsCanceling)
						return true;

					// Lost track of the target
					if (useLastVisibleTarget && lastVisibleTarget.Type == TargetType.Invalid)
						return true;

					// We are not next to the target - lets fix that
					if (target.Type != TargetType.Invalid && !move.CanEnterTargetNow(self, target))
					{
						// BOUNDED. Reaching here means an approach move has just ENDED without putting us
						// beside the target, and the only response available is to queue another one — so
						// a target that can never be reached loops here for the rest of the match. That is
						// not a hypothetical: MoveAdjacentTo cannot report failure (Mobile.MoveResult is
						// never assigned), and MoveCooldownHelper's designed escape for it is doubly dead
						// here because this activity opts into RetryIfDestinationBlocked. The unit is never
						// idle while it loops, which silences every IsIdle-shaped guard it owns — for a
						// technician that means CaptureDispatchManager.CommittedTarget reads the queued
						// capture and counts him busy forever.
						//
						// The retry itself is worth keeping: traffic clears, bridges get repaired, and the
						// approach then succeeds. Only the "forever" part is wrong. One cell of ground
						// gained resets the whole budget, so a unit walking a long way round never spends
						// it — only one that is getting nowhere does.
						if (MaxStalledApproachTicks > 0)
						{
							var now = self.World.WorldTick;
							var elapsed = lastApproachCheckTick < 0 ? 1 : now - lastApproachCheckTick;
							lastApproachCheckTick = now;

							if (approachStall.IsStalled(self.Location, elapsed, MaxStalledApproachTicks))
							{
								Cancel(self, true);
								return true;
							}
						}

						// Target lines are managed by this trait, so we do not pass targetLineColor
						moveCooldownHelper.NotifyMoveQueued();
						var initialTargetPosition = (useLastVisibleTarget ? lastVisibleTarget : target).CenterPosition;
						QueueChild(move.MoveToTargetRaw(self, target, initialTargetPosition));
						return false;
					}

					// We are next to where we thought the target should be, but it isn't here
					// There's not much more we can do here
					if (useLastVisibleTarget || target.Type != TargetType.Actor)
						return true;

					// Are we ready to move into the target?
					if (TryStartEnter(self, target.Actor))
					{
						moveCooldownHelper.NotifyMoveQueued();
						lastState = EnterState.Entering;
						QueueChild(move.MoveIntoTargetRaw(self, target));
						return false;
					}

					// Subclasses can cancel the activity during TryStartEnter
					// Return immediately to avoid an extra tick's delay
					if (IsCanceling)
						return true;

					return false;
				}

				case EnterState.Entering:
				{
					// Check that we reached the requested position
					// Use tolerance-based comparison (half a cell) instead of exact equality
					// to handle sub-cell position mismatches (e.g., crash-landed aircraft not at cell center)
					var targetPos = target.Positions.ClosestToIgnoringPath(self.CenterPosition);
					var positionDelta = self.CenterPosition - targetPos;
					var withinTolerance = positionDelta.HorizontalLengthSquared <= 512 * 512;
					if (!IsCanceling && withinTolerance && target.Type == TargetType.Actor)
						OnEnterComplete(self, target.Actor);

					lastState = EnterState.Exiting;
					return false;
				}

				case EnterState.Exiting:
				{
					moveCooldownHelper.NotifyMoveQueued();
					QueueChild(move.ReturnToCell(self));
					lastState = EnterState.Finished;
					return false;
				}
			}

			return true;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (targetLineColor != null)
				yield return new TargetLineNode(useLastVisibleTarget ? lastVisibleTarget : target, targetLineColor.Value);
		}
	}
}
