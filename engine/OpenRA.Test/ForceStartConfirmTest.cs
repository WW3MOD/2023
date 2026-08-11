#region Copyright & License Information
/*
 * WW3MOD ForceStartConfirm tests — the lobby's inline force-start confirm.
 *
 * These pins exist because the path they cover cannot be reached by hand below a two-human
 * multiplayer lobby: arming requires a client that is in a slot, is not the admin, is not a bot and
 * is not ready, and in a solo skirmish the host IS the admin. Screenshots can show what the armed
 * state renders like; only these tests show that it arms and commits at all.
 *
 * The load-bearing property is that the HOST CAN ALWAYS EVENTUALLY START. This state machine
 * replaced a modal whose defect was cosmetic — the dialog rendered behind the map panel but worked.
 * A wrong transition here is a functional regression strictly worse than that: a lobby that cannot
 * be started. So the cases below are chosen to catch a Start button that swallows clicks — one that
 * arms when it should commit, arms on a lobby where everyone is already ready, or lapses into a
 * state where neither click does anything.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Widgets.Logic;

namespace OpenRA.Test
{
	[TestFixture]
	public class ForceStartConfirmTest
	{
		const int Window = 6000;

		// A click that neither starts nor leaves a live confirm behind is the failure mode that
		// matters: the button would look inert and the host could never start.
		static void AssertArmed(ForceStartClickResult result, long nextArmedUntil, long now)
		{
			Assert.That(result, Is.EqualTo(ForceStartClickResult.Arm));
			Assert.That(ForceStartConfirm.IsArmed(nextArmedUntil, now), Is.True,
				"arming must leave a confirm that is live at the moment it was armed");
		}

		[Test]
		public void FirstClickWithAnUnreadyPlayerArmsInsteadOfStarting()
		{
			var result = ForceStartConfirm.Resolve(0, 1000, true, Window, out var next);

			AssertArmed(result, next, 1000);
			Assert.That(next, Is.EqualTo(7000), "the window runs from the click, not from lobby open");
		}

		[Test]
		public void SecondClickInsideTheWindowCommits()
		{
			ForceStartConfirm.Resolve(0, 1000, true, Window, out var armed);

			// Still unready — the whole point is that this click overrides that.
			var result = ForceStartConfirm.Resolve(armed, 3500, true, Window, out var next);

			Assert.That(result, Is.EqualTo(ForceStartClickResult.StartNow));
			Assert.That(next, Is.Zero,
				"committing must disarm, or the button stays relabelled while the game starts");
		}

		[Test]
		public void SecondClickAfterTheWindowLapsedReArmsRatherThanStarting()
		{
			ForceStartConfirm.Resolve(0, 1000, true, Window, out var armed);

			// One millisecond past the deadline. A stale confirm must not commit — otherwise a click
			// the host made minutes ago could start the game.
			var result = ForceStartConfirm.Resolve(armed, 7001, true, Window, out var next);

			AssertArmed(result, next, 7001);
			Assert.That(next, Is.EqualTo(13001), "the fresh window runs from the new click");
		}

		[Test]
		public void AReadyLobbyStartsOnTheFirstClickAndNeverArms()
		{
			var result = ForceStartConfirm.Resolve(0, 1000, false, Window, out var next);

			Assert.That(result, Is.EqualTo(ForceStartClickResult.StartNow),
				"the confirm must not appear on the common path — it would be a new nuisance");
			Assert.That(next, Is.Zero);
		}

		[Test]
		public void ExactlyAtTheDeadlineTheConfirmHasLapsed()
		{
			Assert.Multiple(() =>
			{
				Assert.That(ForceStartConfirm.IsArmed(7000, 6999), Is.True, "one ms before: still live");
				Assert.That(ForceStartConfirm.IsArmed(7000, 7000), Is.False, "the comparison is exclusive");
				Assert.That(ForceStartConfirm.IsArmed(7000, 7001), Is.False);
			});

			// And the boundary resolves consistently: a click landing exactly on the deadline is
			// treated as a fresh first click, not as a commit.
			var result = ForceStartConfirm.Resolve(7000, 7000, true, Window, out var next);
			AssertArmed(result, next, 7000);
		}

		[Test]
		public void ADisarmedConfirmIsNeverLive()
		{
			Assert.That(ForceStartConfirm.IsArmed(0, 0), Is.False,
				"zero is the disarmed sentinel and must not read as armed at clock zero");
		}

		[Test]
		public void PlayersReadyingUpWhileArmedStillCommitsOnTheSecondClick()
		{
			ForceStartConfirm.Resolve(0, 1000, true, Window, out var armed);

			// Everyone readied up between the two clicks. The second click must start rather than
			// silently do nothing.
			var result = ForceStartConfirm.Resolve(armed, 2000, false, Window, out var next);

			Assert.That(result, Is.EqualTo(ForceStartClickResult.StartNow));
			Assert.That(next, Is.Zero);
		}
	}
}
