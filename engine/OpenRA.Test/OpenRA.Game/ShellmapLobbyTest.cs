#region Copyright & License Information
/*
 * WW3MOD ShellmapLobby tests — bot seating for the main-menu shellmap.
 *
 * These pins exist because the failure they cover is a crash to desktop reachable from the main
 * menu in two clicks, and nothing else in the tree defends the invariant. Session.ClientInSlot
 * uses SingleOrDefault, i.e. it ASSERTS at most one client per slot key. Normally the server is
 * the only thing that mutates Session.Clients and it removes the previous occupant when it seats
 * someone; WW3MOD's shellmap bot injection writes into the client-side session directly, with no
 * server in the loop, so the invariant is ours to keep.
 *
 * The two failure modes below both surfaced as exceptions thrown three layers away during world
 * creation, naming neither the shellmap nor the seating code:
 *   - a duplicated slot key throws InvalidOperationException in MapStartingLocations.Created
 *   - a slot left behind by the previously loaded map throws KeyNotFoundException in
 *     CreateMapPlayers, which indexes the NEW map's players by the STALE slot's PlayerReference
 * so the properties worth pinning are the postconditions, not the mechanics.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Network;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class ShellmapLobbyTest
	{
		const int LocalClientId = 0;

		static (string SlotKey, string Name, string Faction, Color Color, int Team)[] PlayableSlots(int count)
		{
			return Enumerable.Range(0, count)
				.Select(i => ($"Multi{i}", $"Multi{i}", "usa", default(Color), 0))
				.ToArray();
		}

		// Every LoadShellMap is supposed to start from JoinLocal, which leaves exactly one
		// unslotted spectator behind. Seating has to work from that starting point.
		static Session LobbyWithLocalSpectator()
		{
			var lobbyInfo = new Session();
			lobbyInfo.Clients.Add(new Session.Client
			{
				Index = LocalClientId,
				Name = "Player",
				State = Session.ClientState.Ready
			});

			return lobbyInfo;
		}

		static void SeatBots(Session lobbyInfo, (string SlotKey, string Name, string Faction, Color Color, int Team)[] players)
		{
			ShellmapLobby.SeatBots(lobbyInfo, players, "ww3bot", LocalClientId);
		}

		[Test]
		public void SeatingOnceGivesOneBotPerPlayableSlot()
		{
			var lobbyInfo = LobbyWithLocalSpectator();

			SeatBots(lobbyInfo, PlayableSlots(6));

			Assert.That(lobbyInfo.Slots.Count, Is.EqualTo(6));
			Assert.That(lobbyInfo.Clients.Count(c => c.Bot != null), Is.EqualTo(6));
			foreach (var kv in lobbyInfo.Slots)
				Assert.That(lobbyInfo.ClientInSlot(kv.Key), Is.Not.Null,
					$"{kv.Key} has a slot but nobody in it, so the map would load a player short");
		}

		// The crash. Two shellmap loads against one session: the second used to APPEND, leaving
		// two clients claiming Multi0..Multi5.
		[Test]
		public void SeatingTwiceIsTheSameAsSeatingOnce()
		{
			var lobbyInfo = LobbyWithLocalSpectator();

			SeatBots(lobbyInfo, PlayableSlots(6));
			SeatBots(lobbyInfo, PlayableSlots(6));

			Assert.That(lobbyInfo.Clients.Count(c => c.Bot != null), Is.EqualTo(6),
				"a second seating must replace the first, not stack on top of it");

			foreach (var kv in lobbyInfo.Slots)
				Assert.DoesNotThrow(() => lobbyInfo.ClientInSlot(kv.Key),
					$"{kv.Key} holds more than one client, which throws during world creation");
		}

		// The other half: the previous map's slots must not survive into the next one. A slot key
		// the new map does not define is a KeyNotFoundException in CreateMapPlayers.
		[Test]
		public void SeatingASmallerMapDropsThePreviousMapsSlots()
		{
			var lobbyInfo = LobbyWithLocalSpectator();

			SeatBots(lobbyInfo, PlayableSlots(6));
			SeatBots(lobbyInfo, PlayableSlots(2));

			Assert.That(lobbyInfo.Slots.Keys, Is.EquivalentTo(new[] { "Multi0", "Multi1" }));
			Assert.That(lobbyInfo.Clients.Select(c => c.Slot).Where(s => s != null),
				Is.EquivalentTo(new[] { "Multi0", "Multi1" }),
				"a client seated in a slot the new map does not define crashes CreateMapPlayers");
		}

		// The local player is a spectator on the shellmap and must survive reseating, or the
		// menu loses the client it renders and issues scripted orders as.
		[Test]
		public void TheLocalSpectatorSurvivesReseating()
		{
			var lobbyInfo = LobbyWithLocalSpectator();

			SeatBots(lobbyInfo, PlayableSlots(6));
			SeatBots(lobbyInfo, PlayableSlots(3));

			var spectators = lobbyInfo.Clients.Where(c => c.Slot == null).ToArray();
			Assert.That(spectators.Length, Is.EqualTo(1));
			Assert.That(spectators[0].Index, Is.EqualTo(LocalClientId));
		}

		// Bot clients are addressed by Index elsewhere (BotControllerClientIndex, order routing),
		// so reseating must not hand two live clients the same index.
		[Test]
		public void ReseatingKeepsClientIndicesUnique()
		{
			var lobbyInfo = LobbyWithLocalSpectator();

			SeatBots(lobbyInfo, PlayableSlots(4));
			SeatBots(lobbyInfo, PlayableSlots(4));

			var indices = lobbyInfo.Clients.Select(c => c.Index).ToArray();
			Assert.That(indices, Is.Unique);
			Assert.That(lobbyInfo.ClientWithIndex(LocalClientId), Is.Not.Null);
		}

		// Documents WHY the clear is load-bearing rather than tidiness: this is the exception the
		// user hit, reproduced against the raw Session that SeatBots is defending.
		[Test]
		public void TwoClientsInOneSlotIsAHardThrow()
		{
			var lobbyInfo = LobbyWithLocalSpectator();
			lobbyInfo.Clients.Add(new Session.Client { Index = 1, Slot = "Multi0" });
			lobbyInfo.Clients.Add(new Session.Client { Index = 2, Slot = "Multi0" });

			Assert.Throws<System.InvalidOperationException>(() => lobbyInfo.ClientInSlot("Multi0"));
		}
	}
}
