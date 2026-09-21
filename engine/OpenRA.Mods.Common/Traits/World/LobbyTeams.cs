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
 * WHICH TEAM IS THIS PLAYER ON -- one predicate, because the wrong lookup does not fail, it
 * silently answers "the host's team" and every map-authored `Team:` on the board becomes dead text.
 *
 * ==== `ClientInSlot`, NEVER `ClientWithIndex(p.ClientIndex)` =================================
 * `ClientWithIndex(p.ClientIndex)` answers "which client is ASSOCIATED with this player". It does
 * NOT answer "does a client OWN this player", and for a map player the two differ:
 * Player.cs:191 hands every player with no client of its own the HOST'S client index --
 * `world.LobbyInfo.Clients.FirstOrDefault(c => c.IsAdmin)?.Index ?? 0`, under its own upstream
 * `// Owned by the host (TODO: fix this)`. So `ClientWithIndex` returns a real, non-null client for
 * a map player, that client is the human's, and every `?? 0` / `if (team <= 0)` fallback to
 * `PlayerReference.Team` sitting behind it is UNREACHABLE whenever the host is in a team at all.
 *
 * MEASURED, not theoretical: `NuclearExchange.SideKeyFor` had exactly this and a 2v1 autotest ran
 * as a 3v0 and PASSED -- `NUCLEAR EXCHANGE sides: Volga(1), Enemy(1), USA(1)` with `Enemy` authored
 * `Team: 2` (fixed at b6e1ac8c, 2026-09-14). This class is that fix generalised so the next reader
 * of a team inherits it instead of re-deriving it.
 *
 * A lobby slot's key is the PlayerReference's own name (LobbyCommands.MakeSlotFromPlayerReference
 * builds `Slots[pr.Name]`, and `Player.InternalName` is that same name), so `ClientInSlot` is null
 * exactly when no client owns that player -- which is the question every caller here is asking.
 */

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Resolving a player's team without mistaking the host's team for its own.</summary>
	public static class LobbyTeams
	{
		/// <summary>The call-site form. Null-safe in both arguments.</summary>
		// NOT UNIT-TESTABLE, AND DELIBERATELY ONE LINE OF LOGIC BECAUSE OF IT -- nothing in
		// OpenRA.Test can construct a World and therefore not a Player either. Every rule lives in
		// the pure overload below, which IS pinned (LobbyTeamsTest). Same split, and for the same
		// reason, as CombatantSides.
		public static int TeamFor(World world, Player player)
		{
			if (world == null || player == null)
				return 0;

			var owningClient = world.LobbyInfo.ClientInSlot(player.InternalName);

			return TeamFor(owningClient?.Team ?? 0, player.PlayerReference?.Team ?? 0);
		}

		/// <summary>
		/// The whole rule, over plain numbers so it can be tested without a World. The LOBBY team
		/// when a client owns this player's slot and has set one; otherwise the team the MAP
		/// authored. 0 means "no team" in both vocabularies and is what a caller must treat as
		/// "this player is its own side".
		/// </summary>
		public static int TeamFor(int owningClientTeam, int authoredTeam)
		{
			return owningClientTeam > 0 ? owningClientTeam : authoredTeam;
		}
	}
}
