#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * ESCALATION NEEDS EXACTLY TWO SIDES, AND UNTIL NOW NOTHING SAID SO OUT LOUD.
 *
 * Decision 15 is "exactly two sides". Both traits that depend on it have always KNOWN when it was
 * broken and neither could do anything about it:
 *
 *   - DefconWall derives its line from the combatant home locations, and three alliance groups
 *     derive NO line at all (DefconWall.cs:397-402). There is no border.
 *   - NuclearExchange logs `N sides, not 2. ... Escalation is designed for two sides.`
 *     (NuclearExchange.cs:764-768) and carries on, escalating every other side on each launch.
 *
 * Put together, a three-way Escalation lobby gets NO BORDER AND A TOTAL CEASE-FIRE: DEFCON 3 holds
 * every weapon (DefconFireDiscipline.cs:112-141) and the wall that is supposed to be the other half
 * of that bargain never stands. Nobody can shoot and nobody is separated, so the correct play is to
 * park your whole army inside an enemy base and wait for the clock. A stranger filling a four-slot
 * lobby reaches that with no warning and no way back. Filed MEDIUM in bugs/discovered.md, §B3 of
 * the 2026-09-19 escalation review.
 *
 * ==== WHY A LOBBY REFUSAL AND NOT A GRACEFUL DEGRADE ====
 * The second-cheapest option in §B3 was "when no border derives, do not hold fire at DEFCON 3
 * either, so the match is at least an ordinary one". That silently converts a mode the host chose
 * into a different one, with nothing on screen saying it happened -- which is the failure this
 * branch exists to stop, wearing a different hat. Refusing names the problem while the host can
 * still fix it, which is the whole advantage of doing it in the lobby.
 *
 * ==== A SIDE IS A SEATED CLIENT'S TEAM, AND MAP-AUTHORED COMBATANTS ARE NOT COUNTED ====
 * This is the one place this rule is DELIBERATELY narrower than the runtime one, and it has to be.
 * At runtime NuclearExchange counts every player that CombatantSides.CountsAsASide admits, which
 * includes map-authored combatants that occupy no lobby slot. Two shipped autotest scenarios are
 * built that way on purpose -- test-nuclear-side-cooldown and test-bot-damages-garrisoned-building
 * each author three combatant map players behind ONE playable slot -- and counting map players here
 * would refuse to start both of them.
 *
 * So this counts SEATS: who is sitting in the lobby, which is exactly the population the host can
 * still do something about, and exactly the population in the reported failure ("a stranger filling
 * a 4-slot lobby"). A map that authors three combatants of its own is unaffected and still reaches
 * NuclearExchange's warning, unchanged.
 *
 * ==== THE ARITHMETIC IS Session's, NOT world.Players' ====
 * NuclearExchangeState.SideKeyFor keys a teamless player on its index in world.Players; there is no
 * such array in a lobby and no need for one, because only the COUNT is wanted here and not stable
 * keys. Same rule, stated over seats: every distinct positive team is one side, and every seated
 * client with no team is a side of its own.
 */

using System.Collections.Generic;
using System.Linq;
using OpenRA.Network;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>The lobby-side half of decision 15: Escalation is a two-sided mode.</summary>
	public static class EscalationLobbyRule
	{
		/// <summary>The most sides an Escalation match may start with.</summary>
		public const int MaximumSides = 2;

		/// <summary>
		/// How many sides a set of seated clients resolves into. Each distinct positive
		/// <paramref name="seatedTeams"/> entry is one side; each 0 (no team) is a side of its own.
		/// Observers must already be excluded by the caller.
		/// </summary>
		// PURE, AND THAT IS WHY IT IS SEPARATE -- the same split CombatantSides makes and for the
		// same reason: nothing in OpenRA.Test can construct a Server or a World, so the rule lives
		// where a test can reach it and the adapters below only unpack fields.
		public static int SideCount(IEnumerable<int> seatedTeams)
		{
			if (seatedTeams == null)
				return 0;

			var teamed = new HashSet<int>();
			var teamless = 0;

			foreach (var team in seatedTeams)
			{
				if (team > 0)
					teamed.Add(team);
				else
					teamless++;
			}

			return teamed.Count + teamless;
		}

		/// <summary>The whole rule, over plain values so it can be tested without a Server.</summary>
		public static bool RefusesToStart(bool escalationSelected, int sideCount)
		{
			return escalationSelected && sideCount > MaximumSides;
		}

		/// <summary>Is Escalation the selected mode in these lobby settings?</summary>
		// DEFAULTS TO FALSE WHEN THE OPTION IS ABSENT, which is the safe direction: a lobby whose
		// options have not been populated yet, or a mod with no DefconEscalation on its world actor,
		// must never be refused a start by a rule about a mode it does not have.
		public static bool EscalationSelected(Session.Global settings)
		{
			if (settings == null)
				return false;

			var skirmish = nameof(DefconGameMode.Skirmish).ToLowerInvariant();
			var mode = settings.OptionOrDefault(DefconEscalationInfo.ModeOptionId, skirmish);

			return string.Equals(mode, nameof(DefconGameMode.Escalation), System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The call-site form. Null-safe.</summary>
		public static bool RefusesToStart(Session lobbyInfo)
		{
			if (lobbyInfo == null)
				return false;

			if (!EscalationSelected(lobbyInfo.GlobalSettings))
				return false;

			// `!c.IsObserver` is `c.Slot != null` (Session.cs:157): a lobby spectator holds no slot,
			// gets no Player object at runtime and is not a side. Bots ARE seated clients and are
			// counted, because a bot in a slot is a combatant exactly as a human in one is.
			return RefusesToStart(true, SideCount(lobbyInfo.Clients.Where(c => !c.IsObserver).Select(c => c.Team)));
		}
	}
}
