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
		///
		/// <para>THIS IS NOT THE ARMING QUESTION AND MUST NOT BECOME IT. "Is this warhead a game-ender"
		/// and "may this player be handed one" are different, and <see cref="ArmableBy"/> is the second.
		/// The distinction has a live consumer: DoomsdayStrike.ReportExchangeLaunch asks THIS predicate
		/// of a warhead somebody just FIRED, to decide whether that side has placed its own and should
		/// come off the Dead Hand list. Folding the 2026-09-14 attribution rule in here would mean a
		/// player who fired an unattributed game-ender under Sandbox stopped counting as having placed
		/// one, and Dead Hand would drop a second salvo on top of theirs.</para>
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
		/// <para>The prerequisites this power declares that the caller is NOT licensed to ignore --
		/// in practice its OWNER, since the only licensed name is the event tier. Empty means the
		/// power names nobody in particular.</para>
		/// </summary>
		// A NULL OVERRIDE LIST IS THE STRICT SETTING, matching the empty one: a caller that licenses
		// nothing gets every prerequisite back.
		static List<string> OwnerPrerequisites(SupportPowerInfo powerInfo,
			IReadOnlyCollection<string> overriddenPrerequisites)
		{
			var required = powerInfo?.Prerequisites;
			if (required == null || required.Length == 0)
				return new List<string>();

			if (overriddenPrerequisites == null || overriddenPrerequisites.Count == 0)
				return required.ToList();

			return required.Where(r => !overriddenPrerequisites.Contains(r, StringComparer.Ordinal)).ToList();
		}

		/// <summary>
		/// <para>Does this game-ender name an OWNER -- a prerequisite beyond the tier the caller may
		/// override? Split out from <see cref="ArmableBy"/> because it is the whole of the user's
		/// 2026-09-14 ruling and is the one part of the rule a unit test can reach in both
		/// directions without a World.</para>
		/// </summary>
		public static bool NamesAnOwner(SupportPowerInfo powerInfo,
			IReadOnlyCollection<string> overriddenPrerequisites)
		{
			return OwnerPrerequisites(powerInfo, overriddenPrerequisites).Count > 0;
		}

		/// <summary>
		/// <para>MAY THIS PLAYER BE HANDED THIS GAME-ENDER? The question both arming paths ask, and it
		/// is deliberately NOT the same question as <see cref="Is"/> -- see the note there.</para>
		///
		/// <para>THREE RULES, IN ORDER:</para>
		///
		/// <para>1. IT MUST BE A GAME-ENDER AT ALL (<see cref="Is"/>), which is also what keeps the
		/// 50 Mt Tsar Bomba out: decision 04's <see cref="NuclearReleaseLadder.SandboxOnlyAboveTons"/>
		/// says that weapon is not in play in any mode.</para>
		///
		/// <para>2. IT MUST NAME AN OWNER. USER RULING, 2026-09-14: "national ender only" -- each side
		/// gets exactly one END cameo, the B83 for America and the Sarmat for Russia. An UNATTRIBUTED
		/// top-rung power is armed by nobody, which is what keeps the 6 Mt `MissileStrikePower@HighYieldNuke`
		/// (`powers.event` and no faction name, player.yaml:842) out of both the retaliation window and
		/// the final exchange; the user accepted that the Dead Hand ending loses it too. It stays
		/// reachable exactly where it was designed to be -- under the Sandbox lobby option, where it is
		/// bought like anything else, because that route goes through `Permitted` and never through
		/// this predicate.</para>
		///
		/// <para>ATTRIBUTION RATHER THAN A YIELD THRESHOLD, and that was the choice. A ceiling between
		/// the B83's 1.2 Mt and the strategic strike's 6 Mt would split two weapons that differ in
		/// nothing else, would have to be re-judged for every warhead added, and would say nothing
		/// about WHY. "A game-ender belongs to a faction" is the rule the ruling actually states, it
		/// needs no maintenance, and it fails CLOSED: a new ender that forgets its `player.*` name is
		/// armed by nobody rather than by everybody.</para>
		///
		/// <para>SO THE TOP RUNG HAS TWO DISQUALIFIERS AND THEY ANSWER DIFFERENT QUESTIONS. Rule 1 asks
		/// "is this weapon in play at all" (the Tsar Bomba fails it, on yield). Rule 2 asks "is it
		/// anyone's in particular" (the strategic strike fails it, on attribution). Neither subsumes
		/// the other and neither is a restatement: the Tsar Bomba DOES name an owner, and the strategic
		/// strike IS within the yield ceiling.</para>
		///
		/// <para>3. THIS PLAYER MUST HOLD THAT OWNER NAME.</para>
		/// </summary>
		// ==== WHY THE TECH TREE AND NOT A FACTION FIELD ON THE POWER ====
		// Ownership is already declared, once, in rules/player.yaml's tier table: `player.america` and
		// `player.russia` are granted by ProvidesPrerequisite with a `Factions:` filter
		// (player.yaml:1123-1140). A second declaration on the power -- an OwnerFactions field, say --
		// would be a copy of that table free to drift from it, and the drift would be invisible until
		// somebody was handed the wrong warhead. Asking the tech tree asks the table itself.
		//
		// SANDBOX DOES NOT WIDEN THIS, AND A COMMENT HERE USED TO SAY IT DID. Inherited from
		// DoomsdayStrike, it read: "`powers-sandbox` grants all three tiers to every player, so under
		// that option both factions really do own both game-enders and both are armed". That was true
		// while the enders were gated on `powers.russia` / `powers.america` and c8cadc8a made it FALSE
		// on 2026-09-14 by moving them to `player.russia` / `player.america` -- names provided by
		// ProvidesPrerequisite@Russia/@America with a `Factions:` filter and by NOTHING ELSE in the mod
		// (verified: those are the only two providers). The sandbox option's three unfiltered traits
		// grant the `powers.*` shelf names, never the `player.*` identity ones. So even under Sandbox
		// an America player is armed with the B83 and not the Sarmat. Which is the correct behaviour
		// and is the point of the `player.` spelling; only the comment was wrong.
		//
		// A MISSING TechTree ARMS NOTHING RATHER THAN EVERYTHING. It is a stock trait on every player
		// actor, so this is the "a map stripped it" path; failing closed there costs a scenario its
		// game-enders, while failing open would silently restore the bug this closes.
		public static bool ArmableBy(TechTree techTree, SupportPowerInfo powerInfo,
			IReadOnlyCollection<string> overriddenPrerequisites)
		{
			if (!Is(powerInfo))
				return false;

			var owner = OwnerPrerequisites(powerInfo, overriddenPrerequisites);
			if (owner.Count == 0)
				return false;

			return techTree != null && techTree.HasPrerequisites(owner);
		}
	}
}
