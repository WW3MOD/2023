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
 * WHO MAY BE HANDED A GAME-ENDER, asked once for the two paths that hand them out.
 *
 * ==== WHY THIS IS A SHARED FILE AND NOT A METHOD ON EITHER CALLER ====
 * There are exactly two ways a game-ender reaches a player in this mod and they are different
 * mechanisms with the same answer to this question:
 *
 *   * DoomsdayStrike.ArmGameEnders -- the FINAL EXCHANGE. Every surviving side is handed its
 *     game-enders for fifteen seconds when the ending begins.
 *   * NuclearExchange.MakeBandsReady -- the RETALIATION WINDOW at NuclearRung.GameEnder. One side
 *     is handed its game-enders for the length of the window, because it was hit by 100 kt.
 *
 * The second of those was WRONG for the whole of its life and the first was right, which is the
 * argument for this file rather than a reason to congratulate it. The window path gated on
 * SupportPowerInstance.Permitted, which folds in prereqsAvailable, and BOTH shipped national
 * game-enders declare `powers.event` -- a prerequisite no faction provides (player.yaml:144). So
 * the gate could never open, MakeReady was never called, and the END band drew no cameo at all in
 * a normal match while the ledger lit its box and counted down. A user reported exactly that on
 * 2026-09-14. DoomsdayStrike had solved it with OverriddenPrerequisites + an ownership check and
 * documented the trap at length; the window path was written later and did not read it.
 *
 * Duplicating the predicate to fix that would have made three copies of a rule whose two halves
 * pull in opposite directions -- override the TIER, never the FACTION -- so the rule is here once
 * and both callers ask it.
 *
 * ==== THE RULE, AND WHY ITS TWO HALVES ARE NOT THE SAME KIND OF THING ====
 * `powers.event` is a SHOP SHELF that is deliberately empty: it exists so a game-ender is never
 * purchasable, and both of the paths above are the sanctioned routes around that. `player.america`
 * and `player.russia` are an IDENTITY: provided by faction alone, never by the sandbox option, and
 * overriding one hands an America player Russia's Sarmat. The first is the override's whole
 * purpose; the second is what c8cadc8a ruled on 2026-09-14 and must survive it.
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.Common.Traits
{
	public static class NuclearGameEnders
	{
		/// <summary>
		/// <para>Is this power a GAME-ENDER -- one of the weapons the exchange and the final salvo hand
		/// out?</para>
		///
		/// <para>Asked of the YIELD rather than of the condition string or the order name, because the
		/// yield is what the ladder itself asks: <see cref="NuclearReleaseLadder.RungForYield"/> is the
		/// single definition of which band a warhead is in, and reading it here means a new 2 Mt power
		/// is picked up with no edit to either caller. Matching on `RequiresCondition` text would be a
		/// second, silent copy of the band table.</para>
		///
		/// <para>THE TSAR BOMBA IS EXCLUDED, and by the ladder's own constant rather than by name. At
		/// 50 Mt it is above <see cref="NuclearReleaseLadder.SandboxOnlyAboveTons"/>, which is decision
		/// 04's ruling that it is unreachable in normal play; handing it out at the end of every match,
		/// or through a retaliation window, would be exactly the route that ruling closes.</para>
		/// </summary>
		public static bool Is(SupportPowerInfo powerInfo)
		{
			if (powerInfo is not MissileStrikePowerInfo missileInfo)
				return false;

			var tons = missileInfo.NuclearYieldTons;
			if (tons <= 0 || tons > NuclearReleaseLadder.SandboxOnlyAboveTons)
				return false;

			return NuclearReleaseLadder.RungForYield(tons) == (int)NuclearRung.GameEnder;
		}

		/// <summary>
		/// <para>Does this player's faction OWN this game-ender? Every prerequisite the power declares
		/// must be genuinely held, except the ones <paramref name="overriddenPrerequisites"/> licenses
		/// the caller to ignore.</para>
		/// </summary>
		// ==== WHY THE TECH TREE AND NOT A FACTION FIELD ON THE POWER ====
		// Ownership is already declared, once, in rules/player.yaml's tier table: `player.america` and
		// `player.russia` are granted by ProvidesPrerequisite with a `Factions:` filter
		// (player.yaml:1123-1140). A second declaration on the power -- an OwnerFactions field, say --
		// would be a copy of that table free to drift from it, and the drift would be invisible until
		// somebody was handed the wrong warhead. Asking the tech tree asks the table itself.
		//
		// AND IT IS NOT A NO-OP UNDER SANDBOX, deliberately. `powers-sandbox` grants all three tiers to
		// every player (player.yaml:186-195), so under that option both factions really do own both
		// national game-enders and both are armed -- which is what the option is for and what the demos
		// that switch it on depend on.
		//
		// A MISSING TechTree ARMS NOTHING RATHER THAN EVERYTHING. It is a stock trait on every player
		// actor, so this is the "a map stripped it" path; failing closed there costs a scenario its
		// game-enders, while failing open would silently restore the bug this closes.
		public static bool OwnedByFaction(TechTree techTree, SupportPowerInfo powerInfo,
			IReadOnlyCollection<string> overriddenPrerequisites)
		{
			var required = powerInfo?.Prerequisites;
			if (required == null || required.Length == 0)
				return true;

			// A NULL OVERRIDE LIST IS THE STRICT SETTING, matching the empty one: a caller that
			// licenses nothing gets a plain "does this player hold every prerequisite".
			var mustHold = overriddenPrerequisites == null || overriddenPrerequisites.Count == 0
				? required.ToList()
				: required.Where(r => !overriddenPrerequisites.Contains(r, StringComparer.Ordinal)).ToList();

			if (mustHold.Count == 0)
				return true;

			return techTree != null && techTree.HasPrerequisites(mustHold);
		}
	}
}
