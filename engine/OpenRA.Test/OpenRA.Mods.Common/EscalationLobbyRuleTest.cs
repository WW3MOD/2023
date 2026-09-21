#region Copyright & License Information
/*
 * THE LOBBY-SIDE HALF OF DECISION 15 -- "Escalation is a two-sided mode", enforced at last.
 *
 * WHAT THIS FIXTURE CAN AND CANNOT REACH, stated up front because it decides the shape of the file.
 * Nothing in OpenRA.Test can construct a Server, so LobbyCommands.StartGame -- the refusal path
 * itself -- is not reachable from here. What IS reachable is a `Session`: it is a plain class in
 * OpenRA.Network with public fields, so the ADAPTER that reads one is testable too, and that is the
 * part where the bugs live (which clients count, what a missing option means). Only the two lines
 * inside StartGame and CheckAutoStart that call the adapter and send the message ship verified by
 * reading -- the same split, and the same honest limit, as CombatantSides.
 *
 * THE LOAD-BEARING TESTS ARE TwoTeamsOfTwoIsTwoSides, AFreeForAllOfThreeIsRefused AND
 * SkirmishIsNeverRefused.
 *   The first is the case that MUST keep working: a 2v2 is four clients and two sides, and a rule
 *   that counted heads instead of teams would refuse the mode's flagship lobby.
 *   The second is the reported failure -- a stranger filling a four-slot lobby -- and the only
 *   reason this branch exists.
 *   The third is the blast radius: Skirmish is the mode a stranger actually plays, it shares every
 *   one of these lobbies, and nothing about it may change.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Network;

namespace OpenRA.Test
{
	[TestFixture]
	public class EscalationLobbyRuleTest
	{
		static Session.Client Seated(int index, int team)
		{
			// `Slot` is what makes a client a player rather than a spectator: Session.cs:157 defines
			// IsObserver as `Slot == null`. The key's value is never read by the rule, only its
			// presence, so the index doubles as the slot name.
			return new Session.Client { Index = index, Team = team, Slot = "Multi" + index };
		}

		static Session.Client Spectator(int index)
		{
			return new Session.Client { Index = index, Team = 0, Slot = null };
		}

		static Session Lobby(string mode, params Session.Client[] clients)
		{
			var session = new Session();
			session.Clients.AddRange(clients);

			if (mode != null)
				session.GlobalSettings.LobbyOptions[DefconEscalationInfo.ModeOptionId] =
					new Session.LobbyOptionState { Value = mode, IsLocked = false };

			return session;
		}

		static Session Escalation(params Session.Client[] clients)
		{
			return Lobby(nameof(DefconGameMode.Escalation).ToLowerInvariant(), clients);
		}

		// ---- THE ARITHMETIC ---------------------------------------------------------------------

		[TestCase(new int[0], 0)]
		[TestCase(new[] { 0 }, 1)]
		[TestCase(new[] { 0, 0 }, 2)]
		[TestCase(new[] { 1, 1 }, 1)]
		[TestCase(new[] { 1, 2 }, 2)]
		[TestCase(new[] { 1, 1, 2, 2 }, 2)]
		[TestCase(new[] { 0, 0, 0 }, 3)]
		[TestCase(new[] { 1, 2, 3 }, 3)]
		[TestCase(new[] { 1, 1, 0 }, 2)]
		public void SideCountIsTeamsPlusTheTeamless(int[] teams, int expected)
		{
			Assert.That(EscalationLobbyRule.SideCount(teams), Is.EqualTo(expected));
		}

		[Test]
		public void ANullSeatListIsNoSides()
		{
			Assert.That(EscalationLobbyRule.SideCount(null), Is.EqualTo(0));
		}

		[Test]
		public void TeamlessClientsAreEachTheirOwnSideRatherThanOneSharedNoTeamGroup()
		{
			// THE SLIP THIS EXISTS TO CATCH. `Distinct().Count()` over the raw team numbers would
			// collapse every teamless client into a single "team 0" and report a four-way free-for-all
			// as ONE side -- which refuses nothing and leaves the bug exactly where it was found.
			Assert.That(EscalationLobbyRule.SideCount(new List<int> { 0, 0, 0, 0 }), Is.EqualTo(4));
		}

		// ---- THE RULE ---------------------------------------------------------------------------

		[TestCase(1, false)]
		[TestCase(2, false)]
		[TestCase(3, true)]
		[TestCase(8, true)]
		public void MoreThanTwoSidesIsRefusedInEscalation(int sides, bool refused)
		{
			Assert.That(EscalationLobbyRule.RefusesToStart(true, sides), Is.EqualTo(refused));
		}

		[TestCase(1)]
		[TestCase(2)]
		[TestCase(3)]
		[TestCase(8)]
		public void NoModeButEscalationIsEverRefused(int sides)
		{
			Assert.That(EscalationLobbyRule.RefusesToStart(false, sides), Is.False);
		}

		// ---- THE ADAPTER, OVER A REAL Session ----------------------------------------------------

		[Test]
		public void AOneVersusOneWithNoTeamsStarts()
		{
			Assert.That(EscalationLobbyRule.RefusesToStart(Escalation(Seated(0, 0), Seated(1, 0))), Is.False);
		}

		[Test]
		public void TwoTeamsOfTwoIsTwoSides()
		{
			// The mode's flagship lobby: four clients, two sides. A rule that counted clients would
			// refuse this, and it is the one outcome that would make the gate worse than the bug.
			Assert.That(
				EscalationLobbyRule.RefusesToStart(Escalation(Seated(0, 1), Seated(1, 1), Seated(2, 2), Seated(3, 2))),
				Is.False);
		}

		[Test]
		public void AFreeForAllOfThreeIsRefused()
		{
			// THE REPORTED FAILURE. Three teamless seats derive no border (DefconWall.cs:397-402)
			// while DEFCON 3 holds every weapon, so today this lobby starts a cease-fire nobody can end.
			Assert.That(EscalationLobbyRule.RefusesToStart(Escalation(Seated(0, 0), Seated(1, 0), Seated(2, 0))), Is.True);
		}

		[Test]
		public void ThreeTeamsAreRefusedExactlyAsThreeTeamlessSeatsAre()
		{
			Assert.That(EscalationLobbyRule.RefusesToStart(Escalation(Seated(0, 1), Seated(1, 2), Seated(2, 3))), Is.True);
		}

		[Test]
		public void ATwoVersusOneIsTwoSidesAndStarts()
		{
			// WRITTEN THE OTHER WAY ROUND FIRST, AND IT WAS WRONG. Two allies plus one teamless
			// player is a 2v1 -- TWO sides, not three -- and the shipped scenario
			// test-nuclear-side-cooldown is built on exactly that shape. "A seat with no team" is a
			// side of its own, not a side in addition to the team it is not in.
			Assert.That(EscalationLobbyRule.RefusesToStart(Escalation(Seated(0, 1), Seated(1, 1), Seated(2, 0))), Is.False);
		}

		[Test]
		public void AThirdSeatOutsideTwoDISTINCTTeamsIsRefused()
		{
			// The shape a stranger actually produces: two players pick opposing teams, a third joins
			// and never picks one at all. Two teams plus one teamless seat IS three sides.
			Assert.That(EscalationLobbyRule.RefusesToStart(Escalation(Seated(0, 1), Seated(1, 2), Seated(2, 0))), Is.True);
		}

		[Test]
		public void SpectatorsAreNotSides()
		{
			// A lobby spectator holds no slot, gets no Player object at runtime and cannot fire. Three
			// of them watching a 1v1 must not refuse the start.
			Assert.That(
				EscalationLobbyRule.RefusesToStart(Escalation(Seated(0, 0), Seated(1, 0), Spectator(2), Spectator(3), Spectator(4))),
				Is.False);
		}

		[Test]
		public void SkirmishIsNeverRefused()
		{
			// SKIRMISH IS A STRICT NO-OP HERE TOO. It is the mode a stranger actually plays and it
			// shares this lobby; a four-way Skirmish free-for-all is an ordinary game and must start.
			var skirmish = Lobby(nameof(DefconGameMode.Skirmish).ToLowerInvariant(),
				Seated(0, 0), Seated(1, 0), Seated(2, 0), Seated(3, 0));

			Assert.That(EscalationLobbyRule.RefusesToStart(skirmish), Is.False);
		}

		[Test]
		public void AnAbsentModeOptionIsNotEscalation()
		{
			// The safe direction, and the one that keeps this rule out of every other mod's way: a
			// lobby whose options have not been populated, or a world actor with no DefconEscalation
			// at all, must never be refused a start over a mode it does not have.
			var unset = Lobby(null, Seated(0, 0), Seated(1, 0), Seated(2, 0), Seated(3, 0));

			Assert.That(EscalationLobbyRule.EscalationSelected(unset.GlobalSettings), Is.False);
			Assert.That(EscalationLobbyRule.RefusesToStart(unset), Is.False);
		}

		[Test]
		public void TheModeValueIsMatchedCaseInsensitively()
		{
			// The wire value is lowercased by DefconEscalationInfo.LobbyOptions, and DefconEscalation
			// itself parses it with `Enum.TryParse(mode, true, ...)` -- ignoreCase. This must agree
			// with the trait or the gate and the mode it gates would disagree about what is running.
			Assert.That(EscalationLobbyRule.EscalationSelected(Lobby("Escalation").GlobalSettings), Is.True);
			Assert.That(EscalationLobbyRule.EscalationSelected(Lobby("escalation").GlobalSettings), Is.True);
			Assert.That(EscalationLobbyRule.EscalationSelected(Lobby("sandbox").GlobalSettings), Is.False);
		}

		[Test]
		public void ANullLobbyIsNotRefused()
		{
			Assert.That(EscalationLobbyRule.RefusesToStart(null), Is.False);
		}

		[Test]
		public void BotsAreSidesBecauseTheyFire()
		{
			// A bot in a slot is a combatant exactly as a human in one is -- NuclearExchange counts
			// it, DefconWall derives from its home. Three bot seats are three sides.
			var bots = Escalation(Seated(0, 0), Seated(1, 0), Seated(2, 0));
			foreach (var c in bots.Clients)
				c.Bot = "stable";

			Assert.That(EscalationLobbyRule.RefusesToStart(bots), Is.True);
		}
	}
}
