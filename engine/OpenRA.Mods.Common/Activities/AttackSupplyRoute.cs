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
	/// <summary>
	/// "Attack" a Supply Route: walk into the SR's contestation range and stay there until
	/// the situation is resolved. For an enemy SR, that means the SR has been neutralized
	/// (its owner is no longer playable, e.g. defeated or the SR went Neutral). For an
	/// allied SR being defended, that means no enemy contesters remain in range.
	/// Designed to be queueable so the player can chain post-capture/post-defense orders.
	/// </summary>
	public class AttackSupplyRoute : Activity
	{
		readonly Target target;
		readonly IMove move;
		readonly IMoveInfo moveInfo;
		readonly Color targetLineColor;

		bool moveQueued;
		int repathTicks;

		const int RepathInterval = 50;

		public AttackSupplyRoute(Actor self, Target target, Color targetLineColor)
		{
			this.target = target;
			this.targetLineColor = targetLineColor;
			move = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			if (target.Type != TargetType.Actor || target.Actor == null
				|| target.Actor.IsDead || !target.Actor.IsInWorld)
				return true;

			var sr = target.Actor;
			var contestation = sr.TraitOrDefault<SupplyRouteContestation>();
			if (contestation == null)
			{
				// Not actually an SR (or trait got removed) — nothing meaningful to do.
				return true;
			}

			var rel = self.Owner.RelationshipWith(sr.Owner);
			var isEnemySR = rel == PlayerRelationship.Enemy;
			var isAlliedSR = rel == PlayerRelationship.Ally || sr.Owner == self.Owner;

			if (IsResolved(sr, contestation, isEnemySR, isAlliedSR))
				return true;

			// Move within the contestation range so this unit's value contributes to the bar.
			// Re-queue the move periodically so the unit follows the SR if it ever moves
			// (SRs don't move today, but it's robust against SubCell jitter / repathing).
			var range = contestation.Info.Range;
			var distSq = (sr.CenterPosition - self.CenterPosition).HorizontalLengthSquared;

			if (distSq > range.LengthSquared)
			{
				if (!moveQueued || --repathTicks <= 0)
				{
					if (ChildActivity != null)
						ChildActivity.Cancel(self);

					QueueChild(move.MoveWithinRange(target, range, targetLineColor: targetLineColor));
					moveQueued = true;
					repathTicks = RepathInterval;
				}

				TickChild(self);
				return false;
			}

			// In contestation range — stay put until resolved.
			if (ChildActivity != null && moveQueued)
			{
				ChildActivity.Cancel(self);
				moveQueued = false;
			}

			return false;
		}

		static bool IsResolved(Actor sr, SupplyRouteContestation contestation, bool isEnemySR, bool isAlliedSR)
		{
			if (isEnemySR)
			{
				// Enemy SR is "neutralized" once its owner is no longer an active playable opponent.
				// SRs flip to Neutral on owner defeat (per ^Building OwnerLostAction = ChangeOwner),
				// so this also covers the post-defeat case.
				return sr.Owner.WinState != WinState.Undefined
					|| !sr.Owner.Playable
					|| sr.Owner.NonCombatant;
			}

			if (isAlliedSR)
			{
				// Allied SR is "secured" once no enemies remain in the contestation zone
				// AND the bar is back near full (production resumed).
				return contestation.NetEnemySurplus <= 0
					&& contestation.ControlBarFraction >= 95;
			}

			// Neutral SR — once anyone has flipped it / no enemies are pressing, we're done.
			return contestation.NetEnemySurplus <= 0;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			yield return new TargetLineNode(target, targetLineColor);
		}
	}
}
