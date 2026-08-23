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

using System;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// An <see cref="AttendAlly"/> order: the attack-move-around-a-follow that <c>Guard</c> uses, plus the
	/// binding that makes the order name a PATIENT and not just a place to stand.
	/// </summary>
	/// <remarks>
	/// Without the lock this is a positioning order wearing a heal cursor. The attack-move rescans every
	/// 10 ticks and hands the attack layer whatever <see cref="HealerAutoTarget"/>'s ranking names, so
	/// ordering a medic onto a man at 70% treats the man at 55% a cell away instead.
	/// <para>The lock is scoped to this activity on purpose: the order ends exactly when the activity does
	/// — cancelled, superseded by another order, or the actor going away — so there is no second lifetime
	/// to keep in step. An activity cancelled before its first tick goes straight to Done without
	/// OnLastRun, which is safe here only because OnFirstRun is where the lock is taken.</para>
	/// </remarks>
	public class AttendAllyActivity : AttackMoveActivity
	{
		readonly Target patient;
		readonly HealerAutoTarget healer;
		readonly StallWatcher stall = new StallWatcher();
		readonly WDist range;
		readonly int maxStalledTicks;

		bool stalled;

		public AttendAllyActivity(Actor self, Func<Activity> getMove, in Target patient, WDist range, int maxStalledTicks)
			: base(self, getMove)
		{
			this.patient = patient;
			this.range = range;
			this.maxStalledTicks = maxStalledTicks;

			// Nothing requires an attending actor to be a healer — AttendAlly is a general escort order.
			healer = self.TraitOrDefault<HealerAutoTarget>();
		}

		protected override void OnFirstRun(Actor self)
		{
			base.OnFirstRun(self);

			stall.MarkProgress(self.Location);

			if (patient.Type == TargetType.Actor)
				healer?.LockPatient(self, patient.Actor);
		}

		public override bool Tick(Actor self)
		{
			if (healer != null && !IsCanceling && patient.Type == TargetType.Actor && patient.Actor.IsInWorld)
				TickStallFallback(self);

			return base.Tick(self);
		}

		/// <summary>
		/// An ordered patient the healer cannot reach must not cost him the rest of the battle.
		/// </summary>
		/// <remarks>
		/// The follow underneath this order chases a target it may never arrive at, and it will never say
		/// so — <c>Mobile.MoveResult</c> is never assigned, so an unpathable move reports InProgress
		/// forever. Before the patient lock that cost nothing visible, because the ranking would quietly
		/// re-point the medic at somebody he could treat. The lock closes that escape, so without this a
		/// medic ordered across a river stands on the bank healing nobody.
		/// <para>The order is NOT dropped. He was told to attend that man and the follow keeps trying, so
		/// the lock is merely suspended: he treats whoever the ranking names while he is stuck, and takes
		/// his patient back the moment he makes ground again. That makes the state recoverable if the path
		/// opens — a bridge repaired, a blocking unit moving — with nothing to re-issue.</para>
		/// </remarks>
		void TickStallFallback(Actor self)
		{
			// Guarded explicitly because the natural "off" value is violent without it: a zero budget is
			// already spent on the first tick, so the healer would give up his patient before taking a step.
			if (maxStalledTicks <= 0)
				return;

			// Standing still while treating, or standing with the man he is escorting, is the CORRECT
			// state and must never read as a stall. Checked against the same range the follow uses, so
			// the two cannot disagree about what "arrived" means.
			var arrived = (patient.Actor.CenterPosition - self.CenterPosition).HorizontalLengthSquared <= range.LengthSquared;

			// IsStalled is called on EVERY non-arrived tick, including while already stalled: it is what
			// keeps MovedOnLastCheck fresh, and that is the signal the recovery below reads. Guarding the
			// call on !stalled instead would freeze the watcher at the moment we gave up, and the healer
			// would never take his patient back however far he walked.
			if (arrived)
				stall.MarkProgress(self.Location);
			else if (stall.IsStalled(self.Location, 1, maxStalledTicks) && !stalled)
			{
				stalled = true;
				healer.ReleaseLock();
				return;
			}

			if (stalled && (arrived || stall.MovedOnLastCheck))
			{
				stalled = false;
				healer.LockPatient(self, patient.Actor);
			}
		}

		protected override void OnLastRun(Actor self)
		{
			healer?.ReleaseLock();

			base.OnLastRun(self);
		}
	}
}
