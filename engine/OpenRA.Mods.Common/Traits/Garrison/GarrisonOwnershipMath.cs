#region Copyright & License Information
/*
 * WW3MOD garrison inheritance — who a garrisoned building belongs to once its owner has no men left.
 *
 * The rule the design has always stated, in GarrisonManager's own comment on the branch this serves:
 * "Current owner has no soldiers left, but an ALLY does -> transfer". The code said
 * remainingOwners.First() — any player at all, with no relationship test — which made a building
 * hand itself to a hostile occupant with no CaptureManager, no Capturable, no technician and no
 * capture timer. That is a fourth ownership-change route the capture documentation does not list,
 * and it is free.
 *
 * Extracted as a pure function because the alternative is unreachable by test: the situation needs a
 * hostile co-garrison, which EnterAlliedActorTargeter refuses to let a player create, so the only
 * way to reach it in a running game is to stage it through a test binding that bypasses the gate.
 * The CHOICE is the part that was wrong, and the choice is ordinary bookkeeping over a sequence.
 *
 * DETERMINISM. This runs inside the sim on every client, so the pick must not depend on anything
 * clients can disagree about. It takes the first accepted element in ENUMERATION ORDER and does no
 * sorting of its own — deliberately, because the caller's sequence is already sim-deterministic (it
 * is built by walking PortStates in index order and then the shelter list in order), and because
 * preserving the existing order keeps this change to the relationship test alone. A caller whose
 * sequence is NOT deterministic must sort before calling; this cannot do it for them, because it
 * has no key to sort on.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class GarrisonOwnershipMath
	{
		/// <summary>
		/// The first element of <paramref name="remaining"/> that <paramref name="isAlly"/> accepts, or
		/// null when none does. A null result means "nobody here is entitled to this building" and the
		/// caller must fall back — NOT that the sequence was empty, which is the caller's own separate
		/// case and has a different answer.
		/// </summary>
		public static T ChooseHeir<T>(IEnumerable<T> remaining, Func<T, bool> isAlly) where T : class
		{
			if (remaining == null || isAlly == null)
				return null;

			foreach (var candidate in remaining)
				if (candidate != null && isAlly(candidate))
					return candidate;

			return null;
		}

		/// <summary>
		/// Whether UnloadCargo's CargoInfo.Neutral flip may hand this actor to the Neutral player.
		/// <para><paramref name="holdPassengerCount"/> is Cargo.PassengerCount, which is the HOLD and
		/// not the building: on a garrisoned actor a soldier deployed to a firing port has left the
		/// hold, so a zero here does NOT mean the building is empty. That is the whole defect, and it
		/// is why <paramref name="neutralRevertOverridden"/> comes first and is decisive on its own —
		/// when a trait owns the port-aware decision (IOverridesCargoNeutralRevert), the count is not
		/// evidence about anything and must not be consulted.</para>
		/// <para>Deliberately NOT the place to work out WHO the actor should belong to. The override
		/// case is already answered, correctly, by GarrisonManager.CheckOwnershipAfterExit before this
		/// is ever reached; re-deriving it here would be the second disagreeing implementation this
		/// change exists to remove.</para>
		/// </summary>
		public static bool MayRevertHoldToNeutral(bool neutralRevertOverridden, int holdPassengerCount, bool cargoNeutralFlag)
		{
			if (neutralRevertOverridden)
				return false;

			return cargoNeutralFlag && holdPassengerCount == 0;
		}
	}
}
