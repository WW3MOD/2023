#region Copyright & License Information
/*
 * WW3MOD blocked-unload budget tests — the number that decides how long a transport keeps trying to
 * put somebody down before it gives up and says so.
 *
 * WHY A TEST AND NOT JUST A DEFAULT. Cargo.BlockedUnloadTimeout was not chosen, it was INHERITED:
 * HelicopterSquadBotModule already had a patience for exactly this situation (UnloadRetryLimit spent
 * on ScanInterval), and it is the only figure anyone in this codebase has committed to. Reusing it
 * makes the human and the AI give up at the same moment. Nothing else relates the two — they are in
 * different files, different subsystems, and neither the build nor the linter can see the link — so
 * the agreement is exactly the kind that decays silently. This fixture is the only thing holding it.
 *
 * The failure it guards is not a crash. If these drift apart, the game still runs and still looks
 * right; a player's transport and the AI's simply stop agreeing about when a blocked unload is
 * hopeless, and the difference shows up only as inconsistent behaviour nobody can attribute.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class UnloadBudgetTest
	{
		[Test]
		public void TheHumanUnloadInheritsTheAisPatienceRatherThanPickingItsOwn()
		{
			var cargo = new CargoInfo();
			var heli = new HelicopterSquadBotModuleInfo();

			var aiPatience = HelicopterSquadBotModule.UnloadRetryLimit * heli.ScanInterval;

			Assert.That(cargo.BlockedUnloadTimeout, Is.EqualTo(aiPatience),
				"Cargo.BlockedUnloadTimeout is DERIVED from HelicopterSquadBotModule's own patience for a " +
				"blocked unload (UnloadRetryLimit spent on ScanInterval). If you changed either side on " +
				"purpose, change both — a player transport and an AI transport giving up at different " +
				"moments is a difference no one can see the cause of.");
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

			var seconds = new CargoInfo().BlockedUnloadTimeout * DefaultTimestepMilliseconds / TicksPerSecondNumerator;

			Assert.That(seconds, Is.EqualTo(30),
				"the blocked-unload budget is meant to read as ~30 seconds of patience to a player");
		}

		[Test]
		public void TheBudgetLeavesRoomToActuallyRetry()
		{
			var timeout = new CargoInfo().BlockedUnloadTimeout;

			Assert.Multiple(() =>
			{
				Assert.That(timeout, Is.GreaterThan(0),
					"0 disables the timeout and restores the retry-forever wedge this was written to end");

				// The retry loop waits in 10-tick increments (UnloadCargo.BlockedRetryDelay). A budget at or
				// below one increment would abandon on the FIRST failed exit search, turning a transient
				// block — units shuffling past, which clears on its own — into an outright refusal.
				Assert.That(timeout, Is.GreaterThan(10 * 5),
					"the budget must cover several retries, or a momentary block becomes a hard refusal");
			});
		}
	}
}
