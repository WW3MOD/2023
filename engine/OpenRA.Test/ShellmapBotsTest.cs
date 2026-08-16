#region Copyright & License Information
/*
 * WW3MOD shellmap bot seating tests.
 *
 * These pin the fix for a hard crash to desktop (WORKSPACE/audit/260816-crash-clientinslot.md).
 * SetupShellmapBots appended bot clients into the live client-side Session; a second run against
 * the same Session put two clients in one slot, and Session.ClientInSlot (SingleOrDefault) then
 * threw during world creation. The crash surfaced one world-load away from the corruption, which
 * is why it needs a pin here rather than a screenshot: nothing looks wrong at the moment of damage.
 *
 * The load-bearing property is ONE CLIENT PER SLOT KEY, no matter how many times seating runs.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Network;

namespace OpenRA.Test
{
	[TestFixture]
	public class ShellmapBotsTest
	{
		const int LocalClientId = 0;

		static List<Session.Client> Bots(params string[] slotKeys)
		{
			return slotKeys.Select(s => new Session.Client
			{
				Bot = "test-bot",
				BotControllerClientIndex = LocalClientId,
				Slot = s,
				State = Session.ClientState.Ready
			}).ToList();
		}

		// JoinLocal seats the local player as a spectator (Slot == null) before any bot injection.
		static Session SessionWithSpectator()
		{
			var session = new Session();
			session.Clients.Add(new Session.Client
			{
				Index = LocalClientId,
				Name = "local",
				State = Session.ClientState.Ready
			});

			return session;
		}

		[Test]
		public void SeatingTwiceLeavesOneClientPerSlot()
		{
			var session = SessionWithSpectator();

			ShellmapBots.SeatBots(session, Bots("Multi0", "Multi1"), LocalClientId);
			ShellmapBots.SeatBots(session, Bots("Multi0", "Multi1"), LocalClientId);

			// The assertion that matches the crash: this call is what threw.
			Assert.That(session.ClientInSlot("Multi0"), Is.Not.Null);
			Assert.That(session.ClientInSlot("Multi1"), Is.Not.Null);
			Assert.That(session.Clients.Count(c => c.Slot != null), Is.EqualTo(2),
				"a re-seat must replace the previous bots, not append a second set");
		}

		[Test]
		public void SeatingADifferentMapEvictsTheOldMapsBots()
		{
			var session = SessionWithSpectator();

			ShellmapBots.SeatBots(session, Bots("Multi0", "Multi1", "Multi2"), LocalClientId);
			ShellmapBots.SeatBots(session, Bots("Multi0"), LocalClientId);

			Assert.That(session.ClientInSlot("Multi1"), Is.Null,
				"a bot left in a slot the new map does not have would outlive its map");
			Assert.That(session.Clients.Count(c => c.Slot != null), Is.EqualTo(1));
		}

		[Test]
		public void SeatingPreservesTheLocalSpectator()
		{
			var session = SessionWithSpectator();

			ShellmapBots.SeatBots(session, Bots("Multi0"), LocalClientId);
			ShellmapBots.SeatBots(session, Bots("Multi0"), LocalClientId);

			Assert.That(session.Clients.Count(c => c.Slot == null), Is.EqualTo(1),
				"the spectator drives the shellmap via scripted orders; evicting it would break it");
		}

		[Test]
		public void SeatedBotsGetDistinctClientIndices()
		{
			var session = SessionWithSpectator();

			ShellmapBots.SeatBots(session, Bots("Multi0", "Multi1"), LocalClientId);
			ShellmapBots.SeatBots(session, Bots("Multi0", "Multi1"), LocalClientId);

			var indices = session.Clients.Select(c => c.Index).ToList();
			Assert.That(indices.Distinct().Count(), Is.EqualTo(indices.Count),
				"ClientWithIndex is also a SingleOrDefault — a repeated index throws the same way");
		}

		[Test]
		public void SeatingEvictsAHumanHoldingATargetSlot()
		{
			var session = SessionWithSpectator();
			session.Clients.Add(new Session.Client { Index = 7, Name = "human", Slot = "Multi0" });

			ShellmapBots.SeatBots(session, Bots("Multi0"), LocalClientId);

			Assert.That(session.ClientInSlot("Multi0").Bot, Is.EqualTo("test-bot"));
			Assert.That(session.Clients.Count(c => c.Slot == "Multi0"), Is.EqualTo(1));
		}
	}
}
