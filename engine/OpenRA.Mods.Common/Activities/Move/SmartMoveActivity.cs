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
	/// - The target isn't already saturated with enough incoming damage (needs our firepower)
	/// </summary>
	public class SmartMoveActivity : Activity
	{
		readonly Activity moveInner;
		readonly SmartMoveInfo info;
		AutoTarget autoTarget;
		SmartMove smartMove;

		bool runningMoveActivity;
		int checkTick;

		/// <summary>The original destination cell, cached at construction time before any ticks can modify the inner Move.</summary>
		public readonly CPos? OriginalDestination;

		public SmartMoveActivity(Activity moveInner, SmartMoveInfo info)
		{
			this.moveInner = moveInner;
			this.info = info;
			ChildHasPriority = false;

			// Cache the original destination before any execution modifies it
			if (moveInner is Move move)
				OriginalDestination = move.Destination;
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

			var engStance = autoTarget.EngagementStanceValue;

			if (checkTick-- <= 0 && (ChildActivity == null || runningMoveActivity))
			{
				// Scan for targets but don't allow moving toward them (allowMove = false)
				var target = autoTarget.ScanForTarget(self, false, true, !runningMoveActivity);

				if (target.Type != TargetType.Invalid)
				{
					// Only interrupt movement for enemy targets — allied heal/repair targets
					// from HealerAutoTarget should not cause stop-and-engage loops
					var isEnemy = target.Type == TargetType.Actor &&
						self.Owner.RelationshipWith(target.Actor.Owner).HasRelationship(PlayerRelationship.Enemy);

					if (!isEnemy)
						target = Target.Invalid;
				}

				if (target.Type != TargetType.Invalid)
				{
					// Filter armaments: NoSelfDefenseInterrupt weapons (e.g. drone jammer) can fire
					// opportunistically when stationary, but must NOT cancel a player Move.
					var interruptingArmaments = autoTarget.ActiveAttackBases
						.SelectMany(ab => ab.ChooseArmamentsForTarget(target, false))
						.Where(a => !a.Info.NoSelfDefenseInterrupt);

					var inRange = interruptingArmaments.Any(a => target.IsInRange(self.CenterPosition, a.MaxRange()));

					if (inRange)
					{
						// Self-defense: always return fire when under attack
						var underFire = smartMove != null &&
							(self.World.WorldTick - smartMove.LastDamagedTick) < info.UnderFireDuration;

						// Overkill check: skip targets that already have enough damage incoming
						var targetSaturated = target.Type == TargetType.Actor &&
							target.Actor.AverageDamagePercent >= info.OverkillThreshold;

						// HoldPosition during SmartMove: don't stop to engage, keep moving
						// (fire stance still controls IF they fire while passing)
						var holdingPosition = engStance == EngagementStance.HoldPosition;

						if (!holdingPosition && (underFire || !targetSaturated))
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
