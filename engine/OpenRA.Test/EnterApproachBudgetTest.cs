#region Copyright & License Information
/*
 * WW3MOD approach-budget tests — how long any Enter-derived order keeps walking at something it may
 * never reach before it gives the unit back.
 *
 * WHY A TEST AND NOT JUST A DEFAULT. The number is not independent: it is the same patience
 * Cargo.BlockedUnloadTimeout spends on a blocked unload, chosen so the game has ONE answer to "how
 * long does this keep trying" rather than one per subsystem. Nothing in the build or the linter can
 * see that link — the two live in different files and different subsystems — so it is exactly the
 * kind of agreement that decays silently. This fixture is the only thing holding it.
 *
 * The failure it guards is not a crash. If these drift apart the game still runs and still looks
 * right; a technician sent at an island and a transport blocked in a ring simply stop agreeing about
 * when a task is hopeless, and the difference surfaces only as inconsistent behaviour nobody can
 * attribute to a cause.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class EnterApproachBudgetTest
	{
		[Test]
		public void TheApproachBudgetMatchesTheBlockedUnloadBudget()
		{
			Assert.That(Enter.DefaultMaxStalledApproachTicks, Is.EqualTo(new CargoInfo().BlockedUnloadTimeout),
				"Both numbers answer the same player-facing question — how long does this game keep trying " +
				"something that may be impossible — and were deliberately set equal. If you changed one on " +
				"purpose, change both, or say in the commit why a capture should be more patient than an " +
				"unload.");
		}

		[Test]
		public void ThePatienceIsThirtySecondsAtTheModsDefaultTickRate()
		{
			// mod.yaml GameSpeeds 'default': Timestep 60ms ⇒ 16.67 ticks/s. Pinned in seconds as well as
			// ticks because the tick count is the thing that gets edited and the DURATION is the thing that
			// was actually agreed — and this project has already shipped duration comments that were wrong
			// by 1.5x because they assumed 25 tps (conventions.md, "A change believed made ... and inert").
			const int TicksPerSecondNumerator = 1000;
			const int DefaultTimestepMilliseconds = 60;

			var seconds = Enter.DefaultMaxStalledApproachTicks * DefaultTimestepMilliseconds / TicksPerSecondNumerator;

			Assert.That(seconds, Is.EqualTo(30),
				"the approach budget is meant to read as ~30 seconds of patience to a player watching a " +
				"technician stand still");
		}

		[Test]
		public void TheBudgetIsLongEnoughThatOrdinaryTrafficDoesNotSpendIt()
		{
			Assert.Multiple(() =>
			{
				Assert.That(Enter.DefaultMaxStalledApproachTicks, Is.GreaterThan(0),
					"0 disables the bound and restores the retry-forever approach loop this was written to end");

				// The budget only accumulates while the unit holds ONE cell — any cell of ground gained
				// resets it. So this bound is not about how far away the target is, it is about how long a
				// unit may be pinned in place before we conclude it will never arrive. A unit waiting for
				// traffic to clear or for a transport to move off a chokepoint must comfortably survive it.
				Assert.That(Enter.DefaultMaxStalledApproachTicks, Is.GreaterThan(10 * 16),
					"a unit stuck behind ordinary traffic for a few seconds must not lose its order");
			});
		}
	}
}
