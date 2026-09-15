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
 * WHO IS A SIDE IN THE MATCH -- one predicate, because two traits answering it differently is a
 * bug that only shows up as a phantom third player.
 *
 * WHY THE AUTHORED FLAGS ARE CHECKED AND NOT ONLY THE RUNTIME ONES. Player has TWO constructor
 * branches (Player.cs:161-204) and they do not populate the same fields:
 *
 *   - the MAP-PLAYER branch (`client == null`, :187-203) copies the PlayerReference through --
 *     `NonCombatant = pr.NonCombatant; Playable = pr.Playable; spectating = pr.Spectating;`
 *   - the CLIENT branch (`client != null`, :170-186) copies ClientIndex, colour, name, faction,
 *     HomeLocation, spawn and handicap, and ASSIGNS NONE OF THOSE THREE.
 *
 * So a slot that a lobby client occupies -- which is every `Playable: True` slot in an autotest
 * scenario, because the harness's own client is seated in one -- has `Player.NonCombatant == false`
 * and `spectating == false` NO MATTER WHAT ITS PlayerReference SAYS. Writing `NonCombatant: True`
 * on such a slot has no runtime effect whatsoever.
 *
 * `Player.Spectating` cannot cover for it either: it is `!inMissionMap && (spectating || WinState
 * != Undefined)` (Player.cs:86) and `inMissionMap` is MapVisibility.MissionSelector (:167), which
 * EVERY autotest scenario sets. Both runtime arms are therefore dead in a scenario, which is why
 * this reads the PlayerReference directly.
 *
 * MEASURED COST OF NOT DOING THIS, runs 260914_141246 and 260914_181212: an Observer slot authored
 * `Playable: True, Spectating: True, NonCombatant: True` was counted as a third combatant by both
 * DefconWall and NuclearExchange --
 *     DEFCON wall: no line derived from 3 combatant home(s) in 3 alliance group(s)
 *     NUCLEAR EXCHANGE sides: USA-bot(-2), Russia-bot(-3), Observer(-4)
 * -- and a three-way free-for-all derives no line on purpose, so the DEFCON 3 border never stood
 * for a whole no-rush period, twice, across two different attempted fixes.
 *
 * THIS CANNOT AFFECT A REAL LOBBY SPECTATOR, and that is what makes the widened predicate safe
 * rather than merely useful: a lobby spectator is a client with NO SLOT and gets no Player object
 * at all. The only thing the new arm can exclude is a MAP-AUTHORED spectator or non-combatant slot
 * that a client happens to be sitting in -- exactly the case that must never be a side.
 */

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Whether a player counts as a side: a combatant that can hold territory and fire.</summary>
	public static class CombatantSides
	{
		/// <summary>The call-site form. Null-safe in both arguments.</summary>
		// NOT UNIT-TESTABLE, AND DELIBERATELY ONE LINE OF LOGIC BECAUSE OF IT. Nothing in
		// OpenRA.Test can construct a World and therefore not a Player either -- the constraint
		// NuclearExchangeStateTest's header states outright. So every rule lives in the pure
		// overload below, which IS pinned, and this adapter only unpacks fields.
		public static bool CountsAsASide(Player player)
		{
			if (player == null)
				return false;

			var authored = player.PlayerReference;

			return CountsAsASide(
				player.NonCombatant,
				player.Spectating,
				authored != null && authored.NonCombatant,
				authored != null && authored.Spectating);
		}

		/// <summary>
		/// The whole rule, over plain flags so it can be tested without a World.
		/// <paramref name="nonCombatant"/> and <paramref name="spectating"/> are the RUNTIME
		/// <see cref="Player"/> fields; the two `authored` flags are the map's
		/// <see cref="PlayerReference"/>, which the runtime pair does not always reflect.
		/// </summary>
		public static bool CountsAsASide(bool nonCombatant, bool spectating, bool authoredNonCombatant, bool authoredSpectating)
		{
			return !nonCombatant && !spectating && !authoredNonCombatant && !authoredSpectating;
		}
	}
}
