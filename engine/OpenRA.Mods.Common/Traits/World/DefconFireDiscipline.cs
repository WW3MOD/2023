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

/*
 * THE DEFCON 2 HOLD-FIRE RULE, as a pure predicate with no dependency on Actor, World or the lobby.
 *
 * Split out for exactly the reason DefconEscalationState is split out: nothing in OpenRA.Test can
 * construct a World, so a rule living inside a trait method is a rule verified by reading only. The
 * six sites that consult this are all inside per-actor traits and are therefore NOT unit-testable;
 * this class is the part that is, and it holds the whole decision.
 *
 * WHAT THE RULE IS. At DEFCON 2 either side may attack anything, but units do not fire autonomously:
 * every shot is one a player gave. DEFCON 3 is the positioning phase behind a wall and DEFCON 1 is
 * open war, so the hold applies at exactly one rung.
 *
 * WHAT "A SHOT SOMEBODY GAVE" MEANS. It is not defined here. AutoTarget.IsAutoAcquiredSource is
 * already the engine's own provenance test, and its docstring settles the bot case outright: a bot's
 * named-target Attack counts as a direct order, its AttackMove does not, because the player/bot
 * ordered a move and not that particular target. Permits below is a thin skin over that method
 * rather than a second, parallel definition that could drift from it -- and
 * DefconFireDisciplineTest.TheRuleIsExpressedInTheEngineOwnProvenanceTest pins the two together over
 * every AttackSource value, so adding a fourth source fails the build's tests rather than silently
 * defaulting it to "autonomous".
 *
 * WHAT IT DOES NOT COVER, AND THAT IS CORRECT. Explosions, mines, crushing, crash weapons and
 * vaporisation consult no stance and never reach a fire path at all. A unit dying to a mine at
 * DEFCON 2 is not somebody's shot; the rule is about fire, not about damage.
 */

namespace OpenRA.Mods.Common.Traits
{
	public static class DefconFireDiscipline
	{
		// The one rung the hold applies at. Derived from Ceiling rather than written as a literal 2 so
		// that "the middle rung of three" survives someone re-numbering the ladder.
		public const int HoldFireLevel = DefconEscalationState.Ceiling - 1;

		/// <summary>Is the match at the rung where units may not fire of their own accord? False for
		/// every other level INCLUDING <see cref="DefconEscalationState.NoLevel"/>, which is what makes
		/// Skirmish a strict no-op without this class knowing what Skirmish is.</summary>
		public static bool HoldsFire(int level)
		{
			return level == HoldFireLevel;
		}

		/// <summary>May a shot with this provenance happen at this level? Everything is permitted at
		/// every level but <see cref="HoldFireLevel"/>; there, only what somebody ordered.</summary>
		public static bool Permits(int level, AttackSource source, bool forceAttack)
		{
			if (!HoldsFire(level))
				return true;

			// A force-attack is a direct order by construction -- it can only come from a player click,
			// the Lua binding or a deliberate bot order. It arrives with source Default today, so this
			// clause is belt-and-braces rather than load-bearing; it is written out because the reader
			// of a hold-fire rule needs to see that force-attacks are not caught by it.
			if (forceAttack)
				return true;

			return !AutoTarget.IsAutoAcquiredSource(source);
		}
	}
}
