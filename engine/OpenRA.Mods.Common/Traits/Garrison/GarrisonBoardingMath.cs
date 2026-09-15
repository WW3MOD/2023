#region Copyright & License Information
/*
 * WW3MOD garrison boarding rule — who may finish walking into a garrisonable building.
 *
 * ORDERING INTO A NEUTRAL BUILDING STAYS LEGAL. The relationship gate in the entry chain runs
 * exactly once, at targeting time, and EnterAlliedActorTargeter admits an allied OR NEUTRAL owner
 * (:49-54). That is the intended mechanic — a contested neutral house is a race, and both players
 * are entitled to run it.
 *
 * What was missing is a second look at the moment of BOARDING. The first man to arrive flips the
 * building to his own player (GarrisonManager.DynamicOwnership), and the loser's man — whose order
 * was legal when it was issued, and is never re-examined — walked into a building that had become
 * his enemy's. Nothing downstream re-checked: Passenger.ResolveOrder tests liveness, space and
 * cargo type; RideTransport.OnEnterComplete tests identity and CanLoad; Cargo.CanLoad tests
 * LoadingBlocked, its ICargoCanLoadFilters and space. Measured end to end in run
 * 260915_184131_p88773: two hostile players held one building, and the sim handed the enemy's man
 * back only because the OWNER chose to unload. User ruling the same day: close it at the sim rather
 * than at the UI.
 *
 * ICargoCanLoadFilter is the seam that does it without touching the order layer, so the neutral
 * window is untouched: Cargo.CanLoad consults the filters (:527-530) and RideTransport.
 * OnEnterComplete simply returns when CanLoad is false (:81-82) — it does not kill the passenger or
 * teleport him, so the refused man is left standing outside with his activity finished, which is
 * exactly the shape the ruling asked for.
 *
 * The rule is stated over a RELATIONSHIP rather than over two Players so it can be tested without a
 * World. Neutral is admitted because that is the ordinary case this mechanic exists for; Ally
 * because an allied co-garrison is a designed feature; Enemy is the one that is refused.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public static class GarrisonBoardingMath
	{
		/// <summary>
		/// Whether a passenger may complete boarding, given the relationship FROM the building's
		/// current owner TO the passenger's owner. Refuses only Enemy: a null/None relationship is
		/// refused too, since it cannot be shown to be friendly.
		/// </summary>
		public static bool MayBoard(PlayerRelationship buildingOwnerToPassenger)
		{
			return buildingOwnerToPassenger == PlayerRelationship.Ally
				|| buildingOwnerToPassenger == PlayerRelationship.Neutral;
		}
	}
}
