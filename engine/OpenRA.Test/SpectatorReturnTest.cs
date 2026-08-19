#region Copyright & License Information
/*
 * WW3MOD SpectatorReturn tests — the lobby's "Return to Slot" control.
 *
 * These pins exist because the state they cover cannot be reached without a mouse: you have to
 * click Spectate to become a spectator at all, and the interesting case — no slot left to return
 * to — additionally needs every slot filled or closed. A screenshot of the default lobby shows
 * neither.
 *
 * The load-bearing property is AGREEMENT WITH THE SERVER. The client cannot seat itself; it issues
 * "slot <key>" and LobbyCommands.Slot decides, rejecting a closed or occupied slot and — via
 * ValidateCommand — any command at all from a ready client. So the failure mode these cases are
 * chosen to catch is a predicate LOOSER than the server's: a button that reads "Return to Slot",
 * accepts the click, and leaves the player exactly where they were. A dead control is worse than
 * the missing control it replaces, because it also destroys the player's belief that a way back
 * exists.
 *
 * The mirrored risk is a predicate TIGHTER than the server's, which would show "No Open Slot" over
 * a lobby with a visibly empty row — so the open-slot cases are pinned just as hard.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Widgets.Logic;
using OpenRA.Network;

namespace OpenRA.Test
{
	[TestFixture]
	public class SpectatorReturnTest
	{
		const int LocalClientId = 0;

		static Session LobbyWithSlots(params string[] slotKeys)
		{
			var session = new Session();
			foreach (var key in slotKeys)
				session.Slots.Add(key, new Session.Slot { PlayerReference = key });

			return session;
		}

		static Session.Client Spectator()
		{
			return new Session.Client { Index = LocalClientId, Name = "local", Slot = null };
		}

		static void Occupy(Session session, string slotKey, int index)
		{
			session.Clients.Add(new Session.Client { Index = index, Name = "someone", Slot = slotKey });
		}

		static void Seat(Session session, string slotKey, string bot)
		{
			session.Clients.Add(new Session.Client { Index = 90, Name = bot, Bot = bot, Slot = slotKey });
		}

		[Test]
		public void AnOpenEmptySlotIsAvailable()
		{
			var session = LobbyWithSlots("Multi0", "Multi1");

			Assert.That(SpectatorReturn.FirstAvailableSlot(session), Is.EqualTo("Multi0"));
		}

		// The slot offered must be one the player can see is empty, otherwise the button appears to
		// pick at random. Roster order is Session.Slots order, so the first free one wins.
		[Test]
		public void TheFirstFreeSlotInRosterOrderIsTheOneOffered()
		{
			var session = LobbyWithSlots("Multi0", "Multi1", "Multi2");
			Occupy(session, "Multi0", 1);

			Assert.That(SpectatorReturn.FirstAvailableSlot(session), Is.EqualTo("Multi1"));
		}

		[Test]
		public void AClosedSlotIsNotAvailable()
		{
			var session = LobbyWithSlots("Multi0");
			session.Slots["Multi0"].Closed = true;

			Assert.That(SpectatorReturn.FirstAvailableSlot(session), Is.Null,
				"the server rejects 'slot' for a closed slot, so offering it would be a dead click");
		}

		// The trap the whole control exists for: Add Bots fills every slot, then Spectate. A bot is
		// a client in the slot exactly like a human, and the server refuses the slot either way.
		[Test]
		public void ALobbyFullOfBotsHasNoAvailableSlot()
		{
			var session = LobbyWithSlots("Multi0", "Multi1");
			Seat(session, "Multi0", "test-bot");
			Seat(session, "Multi1", "test-bot");

			Assert.That(SpectatorReturn.FirstAvailableSlot(session), Is.Null);
		}

		[Test]
		public void ASlotVacatedByItsOccupantBecomesAvailableAgain()
		{
			var session = LobbyWithSlots("Multi0");
			Seat(session, "Multi0", "test-bot");
			Assert.That(SpectatorReturn.FirstAvailableSlot(session), Is.Null);

			session.Clients.Clear();

			Assert.That(SpectatorReturn.FirstAvailableSlot(session), Is.EqualTo("Multi0"),
				"Remove Bots must re-enable the button without a lobby reload");
		}

		[Test]
		public void CanReturnWhenSpectatingWithAFreeSlot()
		{
			var session = LobbyWithSlots("Multi0");
			var client = Spectator();
			session.Clients.Add(client);

			Assert.That(SpectatorReturn.CanReturn(session, client), Is.True);
		}

		[Test]
		public void CannotReturnWhenEverySlotIsTaken()
		{
			var session = LobbyWithSlots("Multi0");
			var client = Spectator();
			session.Clients.Add(client);
			Seat(session, "Multi0", "test-bot");

			Assert.That(SpectatorReturn.CanReturn(session, client), Is.False);
		}

		// ValidateCommand refuses every non-state command from a ready client, so a ready spectator
		// clicking Return would be silently dropped by the server.
		[Test]
		public void AReadySpectatorCannotReturnEvenWithAFreeSlot()
		{
			var session = LobbyWithSlots("Multi0");
			var client = Spectator();
			client.State = Session.ClientState.Ready;
			session.Clients.Add(client);

			Assert.That(SpectatorReturn.FirstAvailableSlot(session), Is.Not.Null,
				"guard: this case must fail on readiness, not on slot availability");
			Assert.That(SpectatorReturn.CanReturn(session, client), Is.False);
		}

		// The control is the inverse of Spectate, so it must never appear while the player already
		// holds a slot — that space belongs to SPECTATE_AREA and the two would overdraw.
		[Test]
		public void AClientInASlotIsNotSpectating()
		{
			var client = new Session.Client { Index = LocalClientId, Slot = "Multi0" };

			Assert.That(SpectatorReturn.IsSpectating(client), Is.False);
			Assert.That(SpectatorReturn.CanReturn(LobbyWithSlots("Multi1"), client), Is.False);
		}

		// PITFALL guard: LocalClient is null on the early lobby tick and during disconnect
		// teardown, and these predicates run every tick from a widget lambda.
		[Test]
		public void ANullClientIsNeitherSpectatingNorAbleToReturn()
		{
			Assert.That(SpectatorReturn.IsSpectating(null), Is.False);
			Assert.That(SpectatorReturn.CanReturn(LobbyWithSlots("Multi0"), null), Is.False);
			Assert.That(SpectatorReturn.FirstAvailableSlot(null), Is.Null);
		}
	}
}
