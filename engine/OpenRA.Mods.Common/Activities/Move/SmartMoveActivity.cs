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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// Wraps a Move activity to pause and fire at targets within weapon range,
	/// then resume movement. Unlike AttackMoveActivity, this does NOT chase
	/// targets — it only engages what's already in range.
	/// </summary>
	public class SmartMoveActivity : Activity
	{
		readonly Activity moveInner;
		AutoTarget autoTarget;
		readonly int scanInterval;

		bool runningMoveActivity;
		int checkTick;

		public SmartMoveActivity(Activity moveInner, int scanInterval)
		{
			this.moveInner = moveInner;
			this.scanInterval = scanInterval;
			ChildHasPriority = false;
		}

		protected override void OnFirstRun(Actor self)
		{
			autoTarget = self.TraitOrDefault<AutoTarget>();

			// If no AutoTarget trait or on HoldFire stance, just run the plain move
			if (autoTarget == null || autoTarget.Stance <= UnitStance.HoldFire)
			{
				QueueChild(moveInner);
				return;
			}
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling || autoTarget == null || autoTarget.Stance <= UnitStance.HoldFire)
				return TickChild(self);

			if (checkTick-- <= 0 && (ChildActivity == null || runningMoveActivity))
			{
				// Scan for targets but don't allow moving toward them (allowMove = false)
				// This means only targets already within weapon range will be returned
				var target = autoTarget.ScanForTarget(self, false, true, !runningMoveActivity);

				if (target.Type != TargetType.Invalid)
				{
					// Only engage if target is within weapon range (don't chase)
					var inRange = autoTarget.ActiveAttackBases
						.Any(ab => target.IsInRange(self.CenterPosition, ab.GetMaximumRange()));

					if (inRange)
					{
						checkTick = 0;
						runningMoveActivity = false;
						ChildActivity?.Cancel(self);

						foreach (var ab in autoTarget.ActiveAttackBases)
							QueueChild(ab.GetAttackActivity(self, AttackSource.AutoTarget, target, false, false));
					}
				}

				// Resume or start moving when no valid in-range target
				if (ChildActivity == null)
				{
					runningMoveActivity = true;
					QueueChild(moveInner);
					checkTick = scanInterval;
				}
			}

			// Complete when the move finishes (we've reached the destination)
			return TickChild(self) && runningMoveActivity;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			if (ChildActivity != null)
				return ChildActivity.GetTargets(self);

			return Target.None;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			return moveInner.TargetLineNodes(self);
		}
	}
}
