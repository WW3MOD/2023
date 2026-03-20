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
	/// Wraps a Move activity to selectively fire at targets while moving.
	/// Unlike AttackMoveActivity, this does NOT chase targets. It only fires when:
	/// - The unit was recently damaged (return fire / self-defense)
	/// - A target is within close range (good shot opportunity)
	/// </summary>
	public class SmartMoveActivity : Activity
	{
		readonly Activity moveInner;
		readonly SmartMoveInfo info;
		AutoTarget autoTarget;
		SmartMove smartMove;

		bool runningMoveActivity;
		int checkTick;

		public SmartMoveActivity(Activity moveInner, SmartMoveInfo info)
		{
			this.moveInner = moveInner;
			this.info = info;
			ChildHasPriority = false;
		}

		protected override void OnFirstRun(Actor self)
		{
			autoTarget = self.TraitOrDefault<AutoTarget>();
			smartMove = self.TraitOrDefault<SmartMove>();

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
				var target = autoTarget.ScanForTarget(self, false, true, !runningMoveActivity);

				if (target.Type != TargetType.Invalid)
				{
					// Find the max weapon range for the in-range check
					var maxRange = autoTarget.ActiveAttackBases
						.Select(ab => ab.GetMaximumRange())
						.DefaultIfEmpty(WDist.Zero)
						.Max();

					var inRange = autoTarget.ActiveAttackBases
						.Any(ab => target.IsInRange(self.CenterPosition, ab.GetMaximumRange()));

					if (inRange)
					{
						// Check if we should fire: under fire OR target is close
						var underFire = smartMove != null &&
							(self.World.WorldTick - smartMove.LastDamagedTick) < info.UnderFireDuration;

						var distToTarget = (target.CenterPosition - self.CenterPosition).Length;
						var closeRange = distToTarget < maxRange.Length * info.CloseRangeFraction / 100;

						if (underFire || closeRange)
						{
							checkTick = 0;
							runningMoveActivity = false;
							ChildActivity?.Cancel(self);

							foreach (var ab in autoTarget.ActiveAttackBases)
								QueueChild(ab.GetAttackActivity(self, AttackSource.AutoTarget, target, false, false));
						}
					}
				}

				// Resume or start moving when no valid in-range target
				if (ChildActivity == null)
				{
					runningMoveActivity = true;
					QueueChild(moveInner);
					checkTick = info.ScanInterval;
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
