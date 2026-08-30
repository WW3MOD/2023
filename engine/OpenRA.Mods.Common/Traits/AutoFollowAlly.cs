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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Idle behavior: trail the nearest allied combat unit at a short distance.",
		"Active on the engagement stances listed in FollowStances — gives medics/support units a",
		"'stay with the group' default. HoldPosition means stay put and is never followed from.",
		"On an actor that can heal, an ally its healer would treat outranks a healthy one outright,",
		"whatever the distance between them — the follow target decides where the healer's notice",
		"radius is centred, so trailing the nearest healthy man can hide a casualty entirely.")]
	public class AutoFollowAllyInfo : TraitInfo, Requires<IMoveInfo>
	{
		[Desc("Engagement stances on which to trail an ally. Defaults to Defensive alone, which is the",
			"only stance this trait has ever acted on. HoldPosition is ignored even if listed: it means",
			"stay put (the actor still treats or fires on anything that walks into range).")]
		public readonly HashSet<EngagementStance> FollowStances = new HashSet<EngagementStance> { EngagementStance.Defensive };

		[Desc("How close to trail the followed ally.")]
		public readonly WDist FollowDistance = WDist.FromCells(3);

		[Desc("Maximum distance to consider an ally for following — picks the nearest within this radius.")]
		public readonly WDist SearchRange = WDist.FromCells(20);

		[Desc("Ticks between idle re-evaluations.")]
		public readonly int CheckInterval = 25;

		[Desc("If true, only follow allied actors that have an AttackBase (combat units).",
			"This costs no casualty coverage and must not be relaxed to buy some. Every player-ownable",
			"Heal-targetable actor in the mod carries an AttackBase — including the engineer, the drone",
			"operator, and the technician, whose Buildable description says 'Unarmed' but which inherits",
			"^ArmedCivilian's pistol. Unarmed vehicles are not Heal-targetable at all (Targetable@Heal is",
			"on ^Infantry alone), so IsCasualty already declines them. What this DOES exclude is those",
			"vehicles as ESCORT anchors: turn it off and an idle medic trails the nearest supply truck",
			"instead of the squad.")]
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
		readonly StallWatcher stall = new StallWatcher();
		Actor followTarget;
		Actor benched;
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

			// HoldPosition is stay-put and is never followed from, whatever the config says.
			var stance = autoTarget?.EngagementStanceValue;
			if (stance == null || stance == EngagementStance.HoldPosition || !info.FollowStances.Contains(stance.Value))
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

			var distSq = (target.CenterPosition - self.CenterPosition).HorizontalLengthSquared;
			if (distSq <= range.LengthSquared)
			{
				// Arrived. Standing still is the CORRECT state here, so it must not be read as a stall —
				// keep the hysteresis state warm instead, or four quiet checks in a row would bench the
				// very ally we are successfully escorting.
				followTarget = target;
				stall.MarkProgress(self.Location);
				return;
			}

			if (TrackStall(self, target, patient != null))
				return;

			// PITFALL: queued=false means CancelActivity FIRST (Actor.cs) — this REPLACES the current
			// activity, it does not queue behind it. That is only safe because this trait dispatches
			// solely from the idle path: a player order makes the actor non-idle, which silences this
			// trait entirely until the order ends. Do not move this dispatch off TickIdle.
			//
			// Being on the idle path is NOT on its own enough to keep us off AutoTarget's toes, because
			// Actor.Tick walks every INotifyIdle in one sweep WITHOUT re-checking IsIdle between them
			// (Actor.cs, `else if (wasIdle)`). So AutoTarget.TickIdle issuing an attack does not stop this
			// method running afterwards and cancelling it — or the reverse, depending on trait construction
			// order. What actually keeps the two apart is that they are never both willing to act on the
			// same tick: HealerAutoTarget hands AutoTarget a patient ONLY when he is already within
			// GetMaxHealRange, and that is exactly the case the arrival early-out above returns on.
			// Both predicates are plain centre-to-centre distance against the same range, so they cannot
			// disagree. Change either one and the medic starts trading walk orders with attack orders every
			// idle tick — which looks precisely like the aimless shuffling this trait exists to prevent.
			self.QueueActivity(false, move.MoveWithinRange(Target.FromActor(target), range,
				targetLineColor: AutomaticOrder.LineColor));
		}

		/// <summary>Watch for a follow that is making no progress and break out of it. Returns true if the
		/// target was just abandoned (so the caller should sit this cycle out).</summary>
		bool TrackStall(Actor self, Actor target, bool isPatient)
		{
			if (target != followTarget)
			{
				followTarget = target;
				stall.MarkProgress(self.Location);
				return false;
			}

			// A move that cannot reach its destination never reports failure (Mobile.MoveResult is
			// never assigned), so the only evidence of a stall is that we have not changed cell. We
			// sample once per CheckInterval, so that is the elapsed count the budget is spent in.
			if (!stall.IsStalled(self.Location, info.CheckInterval, info.MaxStalledTicks))
				return false;

			if (isPatient)
				healer.AbandonPatient(self);

			benched = target;
			benchedTicks = info.MaxStalledTicks;
			followTarget = null;
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

		/// <summary>A man this actor's healer would treat if he could reach him. Asking the healer rather
		/// than reading Health here is deliberate — see <see cref="HealerAutoTarget.WouldTreat"/>. A
		/// follower with no healer has no casualties, only allies, and ranks purely by distance as before.</summary>
		bool IsCasualty(Actor self, Actor a)
		{
			return healer != null && healer.WouldTreat(self, a, out _);
		}

		Actor FindNearestAlly(Actor self)
		{
			Actor best = null;
			var maxDistSq = info.SearchRange.LengthSquared;
			var bestDistSq = maxDistSq + 1;
			var bestIsCasualty = false;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, info.SearchRange))
			{
				if (!CanFollow(self, a))
					continue;

				var distSq = (a.CenterPosition - self.CenterPosition).HorizontalLengthSquared;
				if (distSq > maxDistSq)
					continue;

				// A man who needs treating outranks a healthy one OUTRIGHT, however much nearer the
				// healthy one is; distance only ever separates two allies of the same kind. Without this
				// the medic anchors himself at FollowDistance from whoever happens to be closest, and his
				// healer's notice radius is measured from wherever that leaves him — so a casualty
				// further out than the radius is not merely outranked, he is never a candidate at all.
				var isCasualty = IsCasualty(self, a);

				if (best != null)
				{
					if (bestIsCasualty && !isCasualty)
						continue;

					if (isCasualty == bestIsCasualty && distSq >= bestDistSq)
						continue;
				}

				best = a;
				bestDistSq = distSq;
				bestIsCasualty = isCasualty;
			}

			if (best == null || followTarget == null || followTarget == best || !CanFollow(self, followTarget))
				return best;

			// The margin below is measured purely in distance, so on its own it would quietly undo the
			// precedence above: an escort at two cells beats a casualty at ten by eight of them. Stickiness
			// is a tie-breaker between equals — hand a casualty over the moment one appears.
			if (bestIsCasualty && !IsCasualty(self, followTarget))
				return best;

			// Stickiness: two allies at near-equal range would otherwise swap places as the follower
			// drifts, and every swap restarts the move.
			var currentDist = (followTarget.CenterPosition - self.CenterPosition).HorizontalLength;
			var bestDist = (best.CenterPosition - self.CenterPosition).HorizontalLength;
			return currentDist - bestDist < info.SwitchMargin.Length ? followTarget : best;
		}
	}
}
