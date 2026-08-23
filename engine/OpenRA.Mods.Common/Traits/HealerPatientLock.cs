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

namespace OpenRA.Mods.Common.Traits
{
	public enum HealerPatientLockDecision
	{
		/// <summary>No player order stands. Rank patients as the healer always has.</summary>
		Rank,

		/// <summary>Treat the man the player named, and do not rank at all.</summary>
		TreatLockedPatient,

		/// <summary>The locked man can never be treated again. Forget him, then rank.</summary>
		DropLock,
	}

	/// <summary>
	/// Whether an explicit heal order still governs, extracted from <see cref="HealerAutoTarget"/> so the
	/// rule can be tested without a World.
	/// </summary>
	public static class HealerPatientLock
	{
		/// <param name="lockHeld">A player order named a specific patient and has not ended.</param>
		/// <param name="patientGone">Dead, disposed, or out of the world — a transport counts.</param>
		/// <param name="patientNeedsTreatment">Hurt, and still advertising a treatable target type.</param>
		public static HealerPatientLockDecision Resolve(bool lockHeld, bool patientGone, bool patientNeedsTreatment)
		{
			if (!lockHeld)
				return HealerPatientLockDecision.Rank;

			if (patientGone)
				return HealerPatientLockDecision.DropLock;

			// A locked man at full health is not abandoned — the follow half of the order still holds him —
			// but he is not bleeding either, so the healer is freed to treat whoever else is. Anything else
			// leaves a medic standing over an unhurt escort while a casualty dies at his feet. The lock
			// reasserts by itself the moment the escort is hit again.
			return patientNeedsTreatment
				? HealerPatientLockDecision.TreatLockedPatient
				: HealerPatientLockDecision.Rank;
		}

		/// <summary>
		/// Whether the healer's own candidate ranking may RUN — not merely whether its answer is used.
		/// </summary>
		/// <remarks>
		/// The distinction is the whole point. <c>HealerAutoTarget.SelectPatient</c> reassigns
		/// <c>currentTarget</c> as a side effect, so a caller that ranks and then discards the answer has
		/// still re-pointed the healer: that is the known walk-phase bug where a marching medic is handed
		/// off to a new patient by a scan whose result was thrown away. A lock that only filtered the
		/// RESULT would be silently defeated by it.
		/// </remarks>
		public static bool RunsRanking(HealerPatientLockDecision decision)
		{
			return decision != HealerPatientLockDecision.TreatLockedPatient;
		}
	}
}
