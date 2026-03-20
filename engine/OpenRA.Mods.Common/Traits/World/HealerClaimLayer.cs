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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Coordinates healers so multiple medics don't pile onto the same patient. Attach this to the world actor.")]
	public sealed class HealerClaimLayerInfo : TraitInfo<HealerClaimLayer> { }

	public sealed class HealerClaimLayer
	{
		readonly Dictionary<Actor, Actor> claimByHealer = new Dictionary<Actor, Actor>();
		readonly Dictionary<Actor, Actor> claimByPatient = new Dictionary<Actor, Actor>();

		/// <summary>
		/// Attempt to claim a patient for the given healer. Returns false if the patient
		/// is already claimed by a different living healer.
		/// </summary>
		public bool TryClaim(Actor healer, Actor patient)
		{
			// Check if patient is already claimed by a different living healer
			if (claimByPatient.TryGetValue(patient, out var existingHealer)
				&& existingHealer != healer && !existingHealer.IsDead && !existingHealer.Disposed)
				return false;

			// Release healer's previous claim if any
			RemoveClaim(healer);

			claimByHealer[healer] = patient;
			claimByPatient[patient] = healer;
			return true;
		}

		/// <summary>
		/// Returns true if the patient is claimed by a living healer other than excludeHealer.
		/// </summary>
		public bool IsClaimed(Actor patient, Actor excludeHealer = null)
		{
			if (!claimByPatient.TryGetValue(patient, out var healer))
				return false;

			if (healer == excludeHealer || healer.IsDead || healer.Disposed)
				return false;

			return true;
		}

		/// <summary>
		/// Release the claim held by this healer.
		/// </summary>
		public void RemoveClaim(Actor healer)
		{
			if (claimByHealer.TryGetValue(healer, out var patient))
				claimByPatient.Remove(patient);

			claimByHealer.Remove(healer);
		}
	}
}
