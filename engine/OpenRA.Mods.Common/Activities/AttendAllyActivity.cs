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

		public AttendAllyActivity(Actor self, Func<Activity> getMove, in Target patient)
			: base(self, getMove)
		{
			this.patient = patient;

			// Nothing requires an attending actor to be a healer — AttendAlly is a general escort order.
			healer = self.TraitOrDefault<HealerAutoTarget>();
		}

		protected override void OnFirstRun(Actor self)
		{
			base.OnFirstRun(self);

			if (patient.Type == TargetType.Actor)
				healer?.LockPatient(self, patient.Actor);
		}

		protected override void OnLastRun(Actor self)
		{
			healer?.ReleaseLock();

			base.OnLastRun(self);
		}
	}
}
