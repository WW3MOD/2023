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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class AttackMoveActivity : Activity
	{
		readonly Func<Activity> getMove;
		readonly bool isAssaultMove;
		readonly AutoTarget autoTarget;
		readonly AttackMove attackMove;

		bool runningMoveActivity = false;
		int token = Actor.InvalidConditionToken;
		Target target = Target.Invalid;
		int checkTick = 0;

		/// <summary>The original destination cell, cached at construction time for reliable group scatter extraction.</summary>
		public readonly CPos? OriginalDestination;

		public AttackMoveActivity(Actor self, Func<Activity> getMove, bool assaultMoving = false)
		{
			this.getMove = getMove;
			autoTarget = self.TraitOrDefault<AutoTarget>();
			attackMove = self.TraitOrDefault<AttackMove>();
			isAssaultMove = assaultMoving;
			ChildHasPriority = false;

			// Cache the destination before any ticks can modify it (for group scatter)
			var tempActivity = getMove();
			if (tempActivity is SmartMoveActivity sma)
				OriginalDestination = sma.OriginalDestination;
			else if (tempActivity is Move m)
				OriginalDestination = m.Destination;
		}

		protected override void OnFirstRun(Actor self)
		{
			if (attackMove == null || autoTarget == null)
			{
				QueueChild(getMove());
				return;
			}

			if (isAssaultMove)
				token = self.GrantCondition(attackMove.Info.AssaultMoveCondition);
			else
				token = self.GrantCondition(attackMove.Info.AttackMoveCondition);
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling || attackMove == null || autoTarget == null)
				return TickChild(self);

			var engStance = autoTarget.EngagementStanceValue;

			// CPU improvement - Only check every 10 ticks
			if (checkTick-- <= 0 && (ChildActivity == null || runningMoveActivity))
			{
				// Scan for targets. Always bypass the AutoTarget per-actor scan-interval rate
				// limit — AttackFollow.Tick runs opportunity-fire scans every tick and shares
				// the same nextScanTime counter, which left AttackMove starved of scan slots
				// during a move (the symptom: attack-move never engages, opportunity-fire fires
				// only at the moment the unit happens to be still). The 10-tick checkTick
				// cadence below provides our own rate limit.
				target = autoTarget.ScanForTarget(self, false, true, ignoreScanInterval: true);

				// Cancel the current move activity and queue attack activities if we find a new target.
				if (target.Type != TargetType.Invalid)
				{
					// HoldPosition during attack-move: only fire at targets in range without stopping
					if (engStance == EngagementStance.HoldPosition)
					{
						var inRange = autoTarget.ActiveAttackBases
							.Any(ab => target.IsInRange(self.CenterPosition, ab.GetMaximumRange()));

						if (!inRange)
							target = Target.Invalid;
					}
				}

				if (target.Type != TargetType.Invalid)
				{
					checkTick = 0;

					runningMoveActivity = false;
					ChildActivity?.Cancel(self);

					foreach (var ab in autoTarget.ActiveAttackBases)
						QueueChild(ab.GetAttackActivity(self, AttackSource.AttackMove, target, false, false));
				}

				// Continue with the move activity (or queue a new one) when there are no targets.
				if (ChildActivity == null)
				{
					runningMoveActivity = true;
					QueueChild(getMove());
					checkTick = 10;
				}
			}

			// If the move activity finished, we have reached our destination and there are no more enemies on our path.
			return TickChild(self) && runningMoveActivity;
		}

		protected override void OnLastRun(Actor self)
		{
			if (token != Actor.InvalidConditionToken)
				token = self.RevokeCondition(token);
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			if (ChildActivity != null)
				return ChildActivity.GetTargets(self);

			return Target.None;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			foreach (var n in getMove().TargetLineNodes(self))
				yield return n;

			yield break;
		}
	}
}
