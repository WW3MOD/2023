#region Copyright & License Information
/*
 * WW3MOD CommitOnOrderMath tests — squad-brain Phase 2 (§4 executor commit audit).
 *
 * Pure-logic pins for the commit-on-order GATE that every ledger-blind executor now consults
 * before staking a recruited unit in the shared PoiGoalGuard ledger: CaptureCoordinator escorts
 * & structure defenders, MountedTransport frontline passengers, GarrisonBotModule@defenses, and
 * LayeredDefence line assignments. The coexistence invariant (§4, FIX 3) is commit-on-order:
 * EVERY executor commits EVERY unit it orders, at order time, so no other writer can poach a
 * briefly-idle recruit.
 *
 * Two things are pinned here without a game:
 *   1. The gate itself (ShouldCommit / ShouldCommitShared) — the byte-identity contract: OFF or
 *      no-ledger (or, for a shared enable-ai-any module, non-experimental bot) ⇒ NO commit ⇒
 *      frozen behaviour; ON + ledger (+ experimental) ⇒ commit.
 *   2. The END-TO-END effect on GoalGuardLedger<TKey>: a recruit committed under the gate is
 *      excluded from a second writer's free-pool (IsCommitted) filter — the steal window closes —
 *      whereas with the gate off it stays poachable. This is the whole point of the phase, pinned
 *      on plain int keys exactly as the engine drives it with Actor keys.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class CommitOnOrderMathTest
	{
		// ---- The per-profile gate (CaptureCoordinator / MountedTransport / LayeredDefence twins) ----

		[Test]
		public void ShouldCommit_OnlyWhenFlagOnAndLedgerPresent()
		{
			Assert.That(CommitOnOrderMath.ShouldCommit(true, true), Is.True, "flag on + ledger present ⇒ commit");
			Assert.That(CommitOnOrderMath.ShouldCommit(false, true), Is.False, "flag off ⇒ frozen (no commit)");
			Assert.That(CommitOnOrderMath.ShouldCommit(true, false), Is.False, "no ledger ⇒ inert");
			Assert.That(CommitOnOrderMath.ShouldCommit(false, false), Is.False, "both off ⇒ frozen");
		}

		// ---- The shared enable-ai-any gate (GarrisonBotModule@defenses) — adds the BotType confinement ----

		[Test]
		public void ShouldCommitShared_RequiresExperimentalBotToo()
		{
			// The one true path: flag on, ledger present, AND the player is the @experimental bot.
			Assert.That(CommitOnOrderMath.ShouldCommitShared(true, true, true), Is.True);

			// A non-experimental player that SHARES the same instance must stay byte-identical, even
			// though it may carry a ledger (post stable-0802 the @stable bot has PoiGoalGuard too).
			Assert.That(CommitOnOrderMath.ShouldCommitShared(true, true, false), Is.False,
				"shared module must NOT commit for the non-experimental player (byte-identity)");

			// The other two inputs still gate independently.
			Assert.That(CommitOnOrderMath.ShouldCommitShared(false, true, true), Is.False, "flag off ⇒ frozen");
			Assert.That(CommitOnOrderMath.ShouldCommitShared(true, false, true), Is.False, "no ledger ⇒ inert");
		}

		// ---- End-to-end: commit-on-order closes the steal window; gate-off leaves it open ----

		// Models the exact shape of DispatchEscort / QueueDefenseOrders / TryAssignNewTasks: writer A
		// recruits a unit from the ledger-checked free pool, orders it, and (under the gate) commits it.
		// Writer B (offense's BuildFreePool) then rebuilds its free pool by excluding IsCommitted units.
		// With the gate ON the recruit is invisible to B (no poach); with it OFF, B grabs it — the bug.
		static bool WriterBSeesUnitAsFree(bool commitEnabled)
		{
			const int Unit = 7;
			const int Ttl = 300;
			var ledger = new GoalGuardLedger<int>();

			// Writer A orders the unit into a mission and commits iff the gate says so.
			if (CommitOnOrderMath.ShouldCommit(commitEnabled, ledgerAvailable: true))
				ledger.Commit(Unit, "capture-escort:42", currentTick: 100, ttlTicks: Ttl);

			// Writer B's free-pool test a moment later (same commitment window).
			return !ledger.IsCommitted(Unit, currentTick: 120);
		}

		[Test]
		public void CommitOnOrder_ClosesStealWindow_WhenGateOn()
		{
			Assert.That(WriterBSeesUnitAsFree(commitEnabled: true), Is.False,
				"gate ON ⇒ recruit is committed ⇒ the second writer cannot poach it (steal window closed)");
		}

		[Test]
		public void CommitOnOrder_LeavesStealWindowOpen_WhenGateOff()
		{
			Assert.That(WriterBSeesUnitAsFree(commitEnabled: false), Is.True,
				"gate OFF ⇒ recruit is NOT committed ⇒ the second writer still grabs it (frozen bug path)");
		}

		// ---- Disjoint objective-key grammar (audit requirement (d)) ----

		// Each executor stamps its own namespace so a commitment is attributable to exactly one writer
		// and two writers claiming DIFFERENT units never collide. The ledger is per-key-per-unit, so
		// distinct actors under distinct executor keys stay independent.
		[Test]
		public void DisjointKeys_IndependentCommitmentsCoexist()
		{
			const int Ttl = 300;
			var ledger = new GoalGuardLedger<int>();

			ledger.Commit(1, "capture-escort:10", 0, Ttl);   // CaptureCoordinator escort
			ledger.Commit(2, "capture-defend:11", 0, Ttl);   // CaptureCoordinator structure defender
			ledger.Commit(3, "transport:12", 0, Ttl);        // MountedTransport passenger
			ledger.Commit(4, "garrison:13", 0, Ttl);         // GarrisonBotModule@defenses
			ledger.Commit(5, "defend-line:4,5", 0, Ttl);     // LayeredDefence line slot

			Assert.That(ledger.Count, Is.EqualTo(5), "five distinct executors, five independent claims");

			Assert.That(ledger.TryGetObjective(1, out var k1) && k1 == "capture-escort:10");
			Assert.That(ledger.TryGetObjective(2, out var k2) && k2 == "capture-defend:11");
			Assert.That(ledger.TryGetObjective(3, out var k3) && k3 == "transport:12");
			Assert.That(ledger.TryGetObjective(4, out var k4) && k4 == "garrison:13");
			Assert.That(ledger.TryGetObjective(5, out var k5) && k5 == "defend-line:4,5");

			for (var u = 1; u <= 5; u++)
				Assert.That(ledger.IsCommitted(u, 0), Is.True, $"unit {u} held by its executor");
		}

		// A transport passenger released on unload (ReleaseTaskPassengers) re-enters the free pool
		// immediately — the delivered-troops hand-off the phase intends (better than TTL-holding them
		// through the carrier's whole return trip, as the bespoke IsPassengerReserved seam did).
		[Test]
		public void TransportRelease_FreesPassengerOnUnload()
		{
			const int Pax = 9;
			const int Ttl = 600;
			var ledger = new GoalGuardLedger<int>();

			ledger.Commit(Pax, "transport:20", currentTick: 0, ttlTicks: Ttl);
			Assert.That(ledger.IsCommitted(Pax, 50), Is.True, "committed for the ride");

			// Unload well before the TTL: offense may recruit it right away.
			Assert.That(ledger.Release(Pax), Is.True);
			Assert.That(ledger.IsCommitted(Pax, 50), Is.False, "released on unload ⇒ free before TTL lapses");
		}

		// A carrier destroyed mid-Loading must release ALL its still-committed passengers (the stale-task
		// teardown), or they stay ledger-locked out of offense's free pool until the TTL lapses. This mirrors
		// ReleaseTaskPassengers iterating a task's ReservedPassengers — expressed at the ledger level (the
		// module's teardown is otherwise a World-harness path). Pins the invariant the review's FIX 1 restores.
		[Test]
		public void CarrierDeath_ReleasesAllReservedPassengers()
		{
			const int Ttl = 300;
			var ledger = new GoalGuardLedger<int>();
			var reserved = new[] { 31, 32, 33 }; // a task's ReservedPassengers

			foreach (var pax in reserved)
				ledger.Commit(pax, "transport:99", currentTick: 0, ttlTicks: Ttl);
			Assert.That(ledger.Count, Is.EqualTo(3), "all boarding passengers committed for the ride");

			// Carrier dies at tick 40 (far inside the TTL). Teardown releases every reserved passenger.
			foreach (var pax in reserved)
				ledger.Release(pax);

			foreach (var pax in reserved)
				Assert.That(ledger.IsCommitted(pax, 41), Is.False,
					$"passenger {pax} freed on carrier death, not stranded until the TTL");
			Assert.That(ledger.Count, Is.EqualTo(0));
		}
	}
}
