#region Copyright & License Information
/*
 * WW3MOD modifier-override gate — decides whether the held-modifier handler in CommandBarLogic may
 * take ownership of World.OrderGenerator.
 *
 * Holding Ctrl+Alt (ForceAttack) or Alt (AttackMove) makes CommandBarLogic's MODIFIER_OVERRIDES
 * listener install a matching order generator on every key event for as long as the modifiers are
 * down. That is fine against the default click-handling generator, which is what those modes are a
 * variation of. It is NOT fine against an explicit input mode the player has already entered.
 *
 * The minefield selector is the case that exposed it. It is opened BY a Ctrl+Alt click
 * (Minelayer.BeginMinefield assigns World.OrderGenerator from inside IIssueOrder.IssueOrder), so the
 * modifiers are still held at the moment it appears. Any key event arriving before the player lets
 * go — a key-down, an auto-repeat, or the key-up of a non-modifier — re-entered the branch and
 * overwrote the selector, which is the "activates for a frame and then cancels" report.
 *
 * The rule: the modifier handler owns only the UnitOrderGenerator FAMILY. That is the default
 * generator plus the modifier-driven variants derived from it (ForceModifiersOrderGenerator,
 * AttackMoveOrderGenerator, GuardOrderGenerator) — all of which are ordinary click handling with a
 * modifier folded in, and all of which the handler is entitled to swap between. Everything else is a
 * deliberate mode with its own entry and exit (the minefield selector, building placement, patrol,
 * support-power targeting) and must be left alone.
 *
 * PITFALL: the family test cannot be "not the default type". ForceModifiersOrderGenerator,
 * AttackMoveOrderGenerator and GuardOrderGenerator all DERIVE from UnitOrderGenerator, so an
 * equality check against typeof(UnitOrderGenerator) would protect the handler's own generators from
 * itself and freeze the first modifier mode you entered until you clicked. Assignability is the
 * correct test.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Orders
{
	public static class ModifierOrderGeneratorMath
	{
		/// <summary>
		/// True when the held-modifier handler may replace (or cancel) the currently active order
		/// generator. False when an explicit input mode is up and must survive the modifiers.
		/// </summary>
		/// <param name="currentOrderGeneratorType">
		/// Runtime type of <see cref="World.OrderGenerator"/>, or null when there is none.
		/// </param>
		public static bool AllowsModifierOverride(Type currentOrderGeneratorType)
		{
			if (currentOrderGeneratorType == null)
				return true;

			return typeof(UnitOrderGenerator).IsAssignableFrom(currentOrderGeneratorType);
		}
	}
}
