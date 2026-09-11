#region Copyright & License Information
/*
 * WW3MOD movement-modifier precedence — decides when an ACTOR-targeting order must step aside because
 * the player is holding a modifier that declares they want a MOVEMENT order instead.
 *
 * THE BUG THIS EXISTS FOR. Holding Alt over a friendly unit made the attack-move cursor vanish, while
 * the attack-move order still issued to the rest of the selection. Both halves have one cause: a click
 * resolves through an OrderPriority contest (UnitOrderGenerator.OrderForUnit), AttackMoveTargeter is
 * TERRAIN-ONLY (AttackMove.cs:209) and priority 4, so over an actor it is reachable only through the
 * second pass that rewrites the click to the cell underneath. That pass is never reached, because the
 * first pass returns on the first targeter that accepts — and UnitOrderTargeter, the base every
 * actor-targeting targeter derives from, gated only on ForceAttack and relationship. The attack-move
 * modifier appeared NOWHERE in it. So holding Alt did not make attack-move win the contest; it only
 * won where nothing else happened to apply.
 *
 * What outranked it, all of it shipping: AttendAlly 7 (a medic + any friendly infantry), the Restock
 * /PickupSupply/DeliverSupply family 6-8, EnterTransport 5 via Passenger — which defaults.yaml grants
 * to nearly every infantry — plus Repair 5, CrewMember 6, DeliversCash 5 and the rest. One such unit
 * anywhere in the selection also named the CURSOR for the whole click, because CursorForOrders
 * collapses the selection with MaxByOrDefault on OrderPriority (UnitOrderGenerator.cs:301-305) while
 * Order() issues per unit. That is exactly "the cursor is gone but the order still issues".
 *
 * THIS CHANGES THE ORDER, NOT ONLY THE CURSOR, AND THAT IS DELIBERATE. Cursor and order come from the
 * same resolution (UnitOrderGenerator.cs:98 and :193), so suppressing the cursor alone would simply
 * move the lie: the pointer would promise an attack-move that the click then did not issue.
 *
 * WHY THE TEST IS ALLY-ONLY, AND NOT "any actor". Alt over a HOSTILE or NEUTRAL actor still has a
 * better answer than walking at it, and those orders must survive:
 *   - AttackBase's targeter (6) — Alt-clicking an enemy should still attack it, not attack-move onto
 *     its cell. It is not a UnitOrderTargeter anyway, but the rule would be wrong even if it were.
 *   - AttacksSupplyRoutes (8) — the Supply Route is the mod's win condition; deleting that order
 *     under a held Alt would be a serious regression.
 *   - Captures (6), whose ValidRelationships default to Neutral|Enemy (Captures.cs:57, unoverridden in
 *     ww3mod) — Alt-clicking a neutral capturable must still capture.
 *   - Demolition 6, Disguise 7, Infiltrates 7 — all enemy-facing.
 * PlayerRelationship.Ally covers the player's OWN units as well as a teammate's, because
 * Player.RelationshipWith returns Ally for self (Player.cs:250-251) — which is precisely the "hovers
 * over a friendly unit" case that was reported.
 *
 * WHY FORCE-MOVE IS NOT COVERED, THOUGH THE REASONING APPLIES TO IT. Ctrl declares "move" every bit as
 * much as Alt declares "attack-move", so by symmetry it belongs here. The implementation does not
 * transfer, because ForceMove is already LOAD-BEARING AS A TRIGGER for the very targeters that would
 * be suppressed: Aircraft's ForceEnter (Aircraft.cs:1282-1286) is priority 6 and its predicate demands
 * Ctrl, DockActorTargeter turns Ctrl into ForceDock (DockClientManager.cs:159), Carryall's drop-off
 * targeter refuses outright without it (Carryall.cs:448), and Passenger.IsCorrectCargoType uses it
 * to force-board a transport. Suppressing on ForceMove would therefore silently delete force-enter,
 * force-board and force-dock. Covering Ctrl properly means giving those gestures a different trigger
 * first, which is a larger design change than the reported bug needs and is NOT attempted here.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	public static class MovementModifierMath
	{
		/// <summary>Whether an actor-targeting order must step aside for a declared movement order.</summary>
		/// <param name="modifiers">The target modifiers in effect for this click.</param>
		/// <param name="relationship">The clicking actor's relationship to the actor under the cursor.</param>
		public static bool YieldsToMovementOrder(TargetModifiers modifiers, PlayerRelationship relationship)
		{
			// Scope discipline: an UNMODIFIED click is untouched, so clicking a friendly transport still
			// loads and clicking a wounded ally with a medic still heals. The distinguishing fact is that
			// the player is holding something that names the kind of order they want.
			if (!modifiers.HasModifier(TargetModifiers.AttackMove))
				return false;

			return relationship == PlayerRelationship.Ally;
		}
	}
}
