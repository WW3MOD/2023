#region Copyright & License Information
/*
 * WW3MOD order fallback gate — decides whether a click that no targeter accepted may be retried
 * against the terrain cell underneath it.
 *
 * The retry is what turns "this unit cannot attack that" into "this unit walks to that", and it is
 * the behaviour being removed here: an attack order a unit cannot execute must leave the unit alone,
 * still doing whatever it was doing, rather than sending it unarmed into the target's guns.
 *
 * WHAT IS NOT AFFECTED, and why the gate keys on the TARGET rather than on the reason for refusal.
 * "Cannot fire on it" has two very different causes and only one of them ever reaches here:
 *
 *   - OUT OF RANGE still ACCEPTS the order. AttackOrderTargeter.CanTargetActor returns true for a
 *     target outside MaxRange (AttackBase.cs:747-753) — it only swaps in OutsideRangeCursor — so the
 *     unit gets a real Attack order and the attack activity closes the distance. That is the attack
 *     system working, not a fallback, and it never consults this gate.
 *   - CAN NEVER ENGAGE refuses, at AttackBase.cs:738 (ChooseArmamentsForTarget found no armament
 *     whose weapon IsValidAgainst the target, or none the relationship permits) or :744 (every
 *     armament valid for it is dry). Only these reach the retry, and only these are switched off.
 *
 * A unit carrying no AttackBase at all — a supply truck, an engineer — never offers an attack
 * targeter in the first place and lands here the same way, which is the case the gate is most
 * visibly for.
 *
 * TWO EXEMPTIONS, both explicit movement intent from the player, both load-bearing today:
 *
 *   - ForceMove (Ctrl). Mobile's MoveOrderTargeter only accepts TargetType.Terrain
 *     (Mobile.cs:1166), so force-moving onto ANY actor's cell — friend or enemy — reaches its
 *     order solely through the retry. Gating it would silently delete force-move onto enemies.
 *   - AttackMove (Alt) through the default generator, for the same reason: AttackMoveTargeter is
 *     terrain-only (AttackMove.cs:141).
 *
 * Non-hostile actors keep the retry unconditionally: right-clicking the cell a friendly unit or a
 * tree happens to occupy is an ordinary move request and always was.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	public static class OrderFallbackMath
	{
		/// <summary>
		/// True when a click that no order targeter accepted may be retried against the terrain cell
		/// underneath the target, producing a Move/ForceMove/AttackMove order.
		/// </summary>
		public static bool AllowsMoveFallback(bool targetIsActor, PlayerRelationship relationship, TargetModifiers modifiers)
		{
			if (!targetIsActor)
				return true;

			if (modifiers.HasModifier(TargetModifiers.ForceMove) || modifiers.HasModifier(TargetModifiers.AttackMove))
				return true;

			return relationship != PlayerRelationship.Enemy;
		}
	}
}
