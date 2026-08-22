#region Copyright & License Information
/*
 * WW3MOD order fallback gate — decides what a click that no targeter accepted may become when it is
 * retried against the terrain cell underneath the thing that was clicked.
 *
 * The behaviour being prevented: an attack order a unit cannot execute must leave the unit alone,
 * still doing whatever it was doing, rather than sending it unarmed into the target's guns.
 *
 * THE RETRY IS NOT "THE MOVE FALLBACK", and reading it as one is what made this file wrong once
 * already. UnitOrderGenerator's second pass replaces the clicked ACTOR with the CELL it stands on and
 * re-runs the whole targeter chain, so it is the only route to ANY order against that cell — a Move,
 * an AttackMove, and equally a force-fire at the ground. Skipping the retry outright therefore
 * deleted force-attack-ground under any enemy the firing unit could not itself target, which is a
 * legitimate and common artillery gesture and was never the thing being stopped.
 *
 * So the retry always runs, and it is the RESULT that is gated:
 *   - a relocation (Move / AttackMove / anything else that drives the unit onto the cell) is refused
 *     against a hostile actor, unless the player explicitly asked for movement;
 *   - a force-fire at the ground is allowed, because it does not move the unit anywhere.
 *
 * WHY THE RELOCATION GATE KEYS ON THE TARGET RATHER THAN ON THE REASON FOR REFUSAL.
 * "Cannot fire on it" has two very different causes and only one of them ever reaches here:
 *
 *   - OUT OF RANGE still ACCEPTS the order. AttackOrderTargeter.CanTargetActor returns true for a
 *     target outside MaxRange (AttackBase.cs:747-753) — it only swaps in OutsideRangeCursor — so the
 *     unit gets a real Attack order and the attack activity closes the distance. That is the attack
 *     system working, not a fallback, and it never consults this gate. MinRange is not consulted by
 *     that method at all, so a target too CLOSE to fire on is likewise never a matter for this file.
 *   - CAN NEVER ENGAGE refuses, at AttackBase.cs:738 (ChooseArmamentsForTarget found no armament
 *     whose weapon IsValidAgainst the target, or none the relationship permits, or none the player
 *     may use without holding force-fire) or :744 (every armament valid for it is dry). Only these
 *     reach the retry.
 *
 * A unit carrying no AttackBase at all — a supply truck, an engineer — never offers an attack
 * targeter in the first place and lands here the same way, which is the case the gate is most
 * visibly for.
 *
 * TWO EXEMPTIONS on the relocation gate, both explicit movement intent from the player:
 *
 *   - ForceMove (Ctrl). Mobile's MoveOrderTargeter only accepts TargetType.Terrain
 *     (Mobile.cs:1166), so force-moving onto ANY actor's cell — friend or enemy — reaches its
 *     order solely through the retry. Gating it would silently delete force-move onto enemies.
 *   - AttackMove (Alt) through the default generator, for the same reason: AttackMoveTargeter is
 *     terrain-only (AttackMove.cs:141).
 *
 * Non-hostile actors keep the retry unconditionally: right-clicking the cell a friendly unit or a
 * tree happens to occupy is an ordinary move request and always was.
 *
 * FINALLY, THE GATE IS PER-SELECTION, NOT PER-UNIT — see SelectionSuppressesRefusers. Holding a unit
 * to "you cannot do this, so you get nothing" only makes sense while somebody else in the selection
 * IS doing the thing. When nothing selected can carry out the specific order there is no specific
 * order to hold anyone to, and the click is the default order for everybody, which is also what the
 * player must see under the cursor before they commit to it.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	public static class OrderFallbackMath
	{
		/// <summary>
		/// The OrderID AttackBase gives a force-fire, at an actor or at the ground
		/// (AttackBase.cs:104). It is only ever set while the player holds force-attack.
		/// </summary>
		public const string ForceAttackOrderID = "ForceAttack";

		/// <summary>
		/// True when an order that would RELOCATE the clicking unit onto the target's cell may be
		/// issued. False only for a hostile actor the player did not explicitly ask to move onto.
		/// </summary>
		public static bool AllowsMoveFallback(bool targetIsActor, PlayerRelationship relationship, TargetModifiers modifiers)
		{
			if (!targetIsActor)
				return true;

			if (modifiers.HasModifier(TargetModifiers.ForceMove) || modifiers.HasModifier(TargetModifiers.AttackMove))
				return true;

			return relationship != PlayerRelationship.Enemy;
		}

		/// <summary>
		/// True when an order the terrain retry produced may be issued. <paramref name="relocationAllowed"/>
		/// is <see cref="AllowsMoveFallback"/> for the clicked target; when it is false the only order
		/// permitted to reach past that target to the cell under it is a force-fire at the ground,
		/// which leaves the unit exactly where it stands.
		/// </summary>
		public static bool AllowsRetryResult(string orderID, bool relocationAllowed)
		{
			return relocationAllowed || orderID == ForceAttackOrderID;
		}

		/// <summary>
		/// True when the refusers in a selection should be left alone rather than given the default
		/// order — which is the case exactly while at least one OTHER unit in the selection accepted
		/// the specific order the click resolved to.
		/// </summary>
		/// <remarks>
		/// The rule is per-selection because that is where it is meaningful. Applied per-unit it also
		/// silences a selection in which NOTHING can attack, and a click that produces no order also
		/// produces no cursor — the player is left hovering an enemy with a bare pointer, unable to
		/// tell a refusal apart from a broken build.
		/// </remarks>
		public static bool SelectionSuppressesRefusers(int unitsAcceptingSpecificOrder)
		{
			return unitsAcceptingSpecificOrder > 0;
		}
	}
}
