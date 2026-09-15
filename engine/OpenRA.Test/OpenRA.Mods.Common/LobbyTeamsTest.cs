#region Copyright & License Information
/*
 * WHICH TEAM IS THIS PLAYER ON. The arithmetic behind LobbyTeams, extracted so the three traits
 * that resolve a team (ConquestVictoryConditions, StrategicVictoryConditions, and
 * NuclearExchange.SideKeyFor before them) share one rule instead of three copies that agree by
 * inspection until one of them stops.
 *
 * ONLY THE PURE OVERLOAD IS REACHABLE FROM HERE, and that is the constraint rather than a choice:
 * nothing in OpenRA.Test can construct a World, and therefore not a Player either. The
 * World-taking adapter is deliberately one statement of lookup-and-unpack for exactly that reason.
 * Its one line of judgement -- ClientInSlot rather than ClientWithIndex -- is not unit-testable and
 * is instead argued at length in LobbyTeams.cs's header and pinned end-to-end by
 * test-nuclear-band-regen.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class LobbyTeamsTest
	{
		[Test]
		public void AnOwningClientsTeamWinsAndAbsenceFallsBackToTheMap()
		{
			// THE REGRESSION THIS PINS, IN ONE LINE: a player no lobby client owns must take the
			// MAP's team. On 2026-09-14 a 2v1 scenario ran as a 3v0 and PASSED -- debug.log read
			// `NUCLEAR EXCHANGE sides: Volga(1), Enemy(1), USA(1)` with Enemy authored `Team: 2` --
			// because the trait asked `ClientWithIndex(p.ClientIndex)` for the team and Player.cs:191
			// hands every player with no client of its own the HOST'S client index. Every map player
			// inherited the human's lobby team and this second argument was unreachable.
			//
			// ConquestVictoryConditions and StrategicVictoryConditions had the same lookup with NO
			// map fallback behind it at all, which is what this class was extracted to fix.
			Assert.That(LobbyTeams.TeamFor(0, 2), Is.EqualTo(2),
				"a player no lobby client owns must take its PlayerReference team -- this is the " +
				"case that was unreachable while the lookup returned the host's client");

			// A REAL LOBBY CLIENT WINS, because a human or bot can be moved between teams in the
			// lobby and the map cannot know that. Asserted with the two DISAGREEING so an
			// implementation that took the wrong source cannot pass.
			Assert.That(LobbyTeams.TeamFor(2, 1), Is.EqualTo(2),
				"a client sitting in this player's own slot must outrank the map's Team:");

			// ZERO IS ABSENT, NOT A TEAM, in both vocabularies: lobby team 0 means "no team" and
			// PlayerReference.Team defaults to 0 (PlayerReference.cs). Callers must read 0 as "this
			// player is its own side" -- NuclearExchangeState.SideKeyFor turns it into a unique
			// negative for exactly that reason.
			Assert.That(LobbyTeams.TeamFor(0, 0), Is.EqualTo(0),
				"no team anywhere must stay 0 so callers can key the player as its own side");

			// AND THE AGREEING CASE, which is the only one the shipped 1v1 lobbies ever hit.
			Assert.That(LobbyTeams.TeamFor(1, 1), Is.EqualTo(1));
			Assert.That(LobbyTeams.TeamFor(3, 0), Is.EqualTo(3));
		}

		[Test]
		public void ThisIsTheSameRuleNuclearExchangeAlreadyUses()
		{
			// NOT A DUPLICATE FIXTURE -- A SEAM TEST. NuclearExchangeState.SideKeyFor carries its own
			// copy of this arithmetic inline (`owningClientTeam > 0 ? owningClientTeam : referenceTeam`)
			// because it then folds the result into a negative free-for-all key. Two copies of a rule
			// is exactly the shape that drifts, so pin them equal here: if someone changes one, this
			// fails rather than the two silently disagreeing about who is on whose team.
			foreach (var owningClientTeam in new[] { -5, 0, 1, 2, 3 })
			{
				foreach (var authoredTeam in new[] { -5, 0, 1, 2, 3 })
				{
					var shared = LobbyTeams.TeamFor(owningClientTeam, authoredTeam);
					var nuclear = NuclearExchangeState.SideKeyFor(owningClientTeam, authoredTeam, 0);

					// SideKeyFor only diverges BELOW the seam: a resolved team of 0 or less becomes
					// its own-side negative. Above it the two must be the same number.
					if (shared > 0)
						Assert.That(nuclear, Is.EqualTo(shared),
							$"LobbyTeams and NuclearExchangeState disagree for " +
							$"(client {owningClientTeam}, map {authoredTeam})");
					else
						Assert.That(nuclear, Is.EqualTo(-1),
							$"a teamless player must key as its own side for " +
							$"(client {owningClientTeam}, map {authoredTeam})");
				}
			}
		}
	}
}
