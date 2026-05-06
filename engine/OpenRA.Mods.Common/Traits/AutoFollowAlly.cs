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

using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Idle behavior: trail the nearest allied combat unit at a short distance.",
		"Active only when the actor's AutoTarget EngagementStance is Defensive — gives medics/support",
		"units a 'stay with the group' default while leaving HoldPosition (stay put) and Hunt (free roam) unchanged.")]
	public class AutoFollowAllyInfo : TraitInfo, Requires<IMoveInfo>
	{
		[Desc("How close to trail the followed ally.")]
		public readonly WDist FollowDistance = WDist.FromCells(3);

		[Desc("Maximum distance to consider an ally for following — picks the nearest within this radius.")]
		public readonly WDist SearchRange = WDist.FromCells(20);

		[Desc("Ticks between idle re-evaluations.")]
		public readonly int CheckInterval = 25;

		[Desc("If true, only follow allied actors that have an AttackBase (combat units).")]
		public readonly bool RequireAttackBase = true;

		public override object Create(ActorInitializer init) { return new AutoFollowAlly(init.Self, this); }
	}

	public class AutoFollowAlly : INotifyIdle, INotifyBecomingIdle
	{
		readonly AutoFollowAllyInfo info;
		readonly IMove move;
		AutoTarget autoTarget;
		int checkTick;

		public AutoFollowAlly(Actor self, AutoFollowAllyInfo info)
		{
			this.info = info;
			move = self.Trait<IMove>();
		}

		void EnsureRefs(Actor self)
		{
			if (autoTarget == null)
				autoTarget = self.TraitOrDefault<AutoTarget>();
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			// Reset the check tick so we evaluate quickly on becoming idle, instead of
			// waiting up to a full CheckInterval to start following.
			checkTick = 0;
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			EnsureRefs(self);

			// Only Defensive stance triggers the follow behavior. HoldPosition stays put,
			// Hunt does its own thing (handled by AutoTarget directly).
			if (autoTarget == null || autoTarget.EngagementStanceValue != EngagementStance.Defensive)
				return;

			if (--checkTick > 0)
				return;

			checkTick = info.CheckInterval;

			var ally = FindNearestAlly(self);
			if (ally == null)
				return;

			var distSq = (ally.CenterPosition - self.CenterPosition).HorizontalLengthSquared;
			if (distSq <= info.FollowDistance.LengthSquared)
				return;

			// Move within FollowDistance of the chosen ally. Queue (don't replace) so a heal
			// target picked up by HealerAutoTarget on the next tick still preempts cleanly.
			self.QueueActivity(false, move.MoveWithinRange(Target.FromActor(ally), info.FollowDistance,
				targetLineColor: self.Owner.Color));
		}

		Actor FindNearestAlly(Actor self)
		{
			Actor best = null;
			var bestDistSq = info.SearchRange.LengthSquared + 1;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, info.SearchRange))
			{
				if (a == self || a.IsDead || !a.IsInWorld)
					continue;

				if (a.Owner != self.Owner)
					continue;

				if (info.RequireAttackBase && !a.Info.HasTraitInfo<AttackBaseInfo>())
					continue;

				// Don't follow other auto-followers — avoids two medics endlessly trailing each other.
				if (a.Info.HasTraitInfo<AutoFollowAllyInfo>())
					continue;

				var distSq = (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared;
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					best = a;
				}
			}

			return best;
		}
	}
}
