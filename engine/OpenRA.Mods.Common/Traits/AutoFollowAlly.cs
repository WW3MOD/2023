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

		[Desc("A new candidate must be at least this much closer than the one currently being followed",
			"before switching to it. Without a margin two allies at equal range flip back and forth as",
			"the follower drifts between them.")]
		public readonly WDist SwitchMargin = WDist.FromCells(1);

		[Desc("Give up on the current destination after this many ticks without moving a cell.",
			"Mobile.MoveResult is never assigned, so a move that cannot path reports InProgress forever",
			"instead of failing — see WORKSPACE/DISCOVERIES.md. Nothing else will break the follower out.")]
		public readonly int MaxStalledTicks = 100;

		public override object Create(ActorInitializer init) { return new AutoFollowAlly(init.Self, this); }
	}

	public class AutoFollowAlly : INotifyIdle, INotifyBecomingIdle
	{
		readonly AutoFollowAllyInfo info;
		readonly IMove move;
		AutoTarget autoTarget;
		HealerAutoTarget healer;
		Actor followTarget;
		Actor benched;
		CPos lastCell;
		int stalledTicks;
		int benchedTicks;
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

			if (healer == null)
				healer = self.TraitOrDefault<HealerAutoTarget>();
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

			if (benchedTicks > 0 && (benchedTicks -= info.CheckInterval) <= 0)
				benched = null;

			// A claimed patient outranks trailing the squad. Closing the distance to a heal target has
			// to happen here: the attack layer refuses to move toward an auto-target on any engagement
			// stance below Hunt, so a patient noticed at the far edge of the healer's search radius
			// would otherwise be watched and never treated.
			var patient = healer?.CurrentPatient;
			var target = patient ?? FindNearestAlly(self);
			if (target == null)
			{
				followTarget = null;
				return;
			}

			var range = patient != null ? healer.HealRange : info.FollowDistance;

			if (TrackStall(self, target, patient != null))
				return;

			var distSq = (target.CenterPosition - self.CenterPosition).HorizontalLengthSquared;
			if (distSq <= range.LengthSquared)
				return;

			// PITFALL: queued=false means CancelActivity FIRST (Actor.cs) — this REPLACES the current
			// activity, it does not queue behind it. That is only safe because this trait dispatches
			// solely from the idle path: a player order makes the actor non-idle, which silences this
			// trait entirely until the order ends. Do not move this dispatch off TickIdle.
			self.QueueActivity(false, move.MoveWithinRange(Target.FromActor(target), range,
				targetLineColor: self.Owner.Color));
		}

		/// <summary>Watch for a follow that is making no progress and break out of it. Returns true if the
		/// target was just abandoned (so the caller should sit this cycle out).</summary>
		bool TrackStall(Actor self, Actor target, bool isPatient)
		{
			if (target != followTarget)
			{
				followTarget = target;
				lastCell = self.Location;
				stalledTicks = 0;
				return false;
			}

			if (self.Location != lastCell)
			{
				lastCell = self.Location;
				stalledTicks = 0;
				return false;
			}

			// A move that cannot reach its destination never reports failure (Mobile.MoveResult is
			// never assigned), so the only evidence of a stall is that we have not changed cell.
			if ((stalledTicks += info.CheckInterval) < info.MaxStalledTicks)
				return false;

			if (isPatient)
				healer.AbandonPatient(self);

			benched = target;
			benchedTicks = info.MaxStalledTicks;
			followTarget = null;
			stalledTicks = 0;
			return true;
		}

		bool CanFollow(Actor self, Actor a)
		{
			if (a == self || a == benched || a.IsDead || !a.IsInWorld)
				return false;

			if (a.Owner != self.Owner)
				return false;

			if (info.RequireAttackBase && !a.Info.HasTraitInfo<AttackBaseInfo>())
				return false;

			// Don't follow other auto-followers — avoids two medics endlessly trailing each other.
			return !a.Info.HasTraitInfo<AutoFollowAllyInfo>();
		}

		Actor FindNearestAlly(Actor self)
		{
			Actor best = null;
			var bestDistSq = info.SearchRange.LengthSquared + 1;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, info.SearchRange))
			{
				if (!CanFollow(self, a))
					continue;

				var distSq = (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared;
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					best = a;
				}
			}

			if (best == null || followTarget == null || followTarget == best || !CanFollow(self, followTarget))
				return best;

			// Stickiness: two allies at near-equal range would otherwise swap places as the follower
			// drifts, and every swap restarts the move.
			var currentDist = (followTarget.CenterPosition - self.CenterPosition).HorizontalLength;
			var bestDist = (best.CenterPosition - self.CenterPosition).HorizontalLength;
			return currentDist - bestDist < info.SwitchMargin.Length ? followTarget : best;
		}
	}
}
