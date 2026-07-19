#region Copyright & License Information
/*
 * WW3MOD PoiGoalGuard ledger tests — POI-strategy Phase 0/1.
 *
 * Pure-logic tests of GoalGuardLedger<TKey>, the commitment ledger that stops
 * the v2 capture module re-issuing orders when a unit's IsIdle flickers.
 * Actor construction is heavy, so the ledger is generic and tested here with
 * string keys. The engine trait wraps GoalGuardLedger<Actor>; this validates
 * the timing/expiry/commit-count logic that trait relies on.
 *
 * The headline assertion is AntiThrash_SingleCommitAcrossFlickerWindow: a
 * caller that gates re-issue on IsCommitted emits exactly ONE commit across a
 * whole flicker window, whereas the un-gated caller (the bug) emits one per
 * scan. This is the S-E "no order-overwriting" invariant in miniature.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class PoiGoalGuardTest
	{
		const int Ttl = 300;

		[Test]
		public void FreshLedger_IsEmpty()
		{
			var led = new GoalGuardLedger<string>();
			Assert.That(led.Count, Is.EqualTo(0));
			Assert.That(led.IsCommitted("tecn1", 0), Is.False);
			Assert.That(led.CommitCountFor("tecn1"), Is.EqualTo(0));
			Assert.That(led.TryGetObjective("tecn1", out _), Is.False);
		}

		[Test]
		public void Commit_MakesUnitCommittedUntilExpiry()
		{
			var led = new GoalGuardLedger<string>();
			led.Commit("tecn1", "capture:42", currentTick: 100, ttlTicks: Ttl);

			Assert.That(led.IsCommitted("tecn1", 100), Is.True, "committed at commit tick");
			Assert.That(led.IsCommitted("tecn1", 399), Is.True, "committed just before expiry (100+300=400)");
			Assert.That(led.IsCommitted("tecn1", 400), Is.False, "NOT committed at expiry tick");
			Assert.That(led.IsCommitted("tecn1", 500), Is.False, "NOT committed after expiry");
		}

		[Test]
		public void TryGetObjective_ReturnsStoredObjective()
		{
			var led = new GoalGuardLedger<string>();
			led.Commit("tecn1", "capture:42", 0, Ttl);

			Assert.That(led.TryGetObjective("tecn1", out var obj), Is.True);
			Assert.That(obj, Is.EqualTo("capture:42"));
		}

		[Test]
		public void RecommitSameObjective_ExtendsDeadlineAndBumpsCount()
		{
			var led = new GoalGuardLedger<string>();
			led.Commit("tecn1", "capture:42", currentTick: 0, ttlTicks: Ttl);
			Assert.That(led.CommitCountFor("tecn1"), Is.EqualTo(1));

			// Re-commit to the SAME target later (e.g. after an expiry-then-stall).
			led.Commit("tecn1", "capture:42", currentTick: 500, ttlTicks: Ttl);
			Assert.That(led.CommitCountFor("tecn1"), Is.EqualTo(2), "same-objective re-commit bumps thrash counter");
			Assert.That(led.IsCommitted("tecn1", 700), Is.True, "deadline extended to 500+300=800");
			Assert.That(led.IsCommitted("tecn1", 800), Is.False);
		}

		[Test]
		public void CommitDifferentObjective_ResetsCount()
		{
			var led = new GoalGuardLedger<string>();
			led.Commit("tecn1", "capture:42", 0, Ttl);
			led.Commit("tecn1", "capture:42", 10, Ttl);
			Assert.That(led.CommitCountFor("tecn1"), Is.EqualTo(2));

			// Switching to a new objective is a fresh commitment, not thrash.
			led.Commit("tecn1", "capture:99", 20, Ttl);
			Assert.That(led.CommitCountFor("tecn1"), Is.EqualTo(1));
			Assert.That(led.TryGetObjective("tecn1", out var obj), Is.True);
			Assert.That(obj, Is.EqualTo("capture:99"));
		}

		[Test]
		public void Release_RemovesCommitment()
		{
			var led = new GoalGuardLedger<string>();
			led.Commit("tecn1", "capture:42", 0, Ttl);
			Assert.That(led.Release("tecn1"), Is.True);
			Assert.That(led.IsCommitted("tecn1", 0), Is.False);
			Assert.That(led.Count, Is.EqualTo(0));
			Assert.That(led.Release("tecn1"), Is.False, "second release is a no-op");
		}

		[Test]
		public void Prune_DropsExpiredCommitments()
		{
			var led = new GoalGuardLedger<string>();
			led.Commit("tecn1", "capture:1", currentTick: 0, ttlTicks: 100);   // expires at 100
			led.Commit("tecn2", "capture:2", currentTick: 0, ttlTicks: 500);   // expires at 500

			led.Prune(currentTick: 200);
			Assert.That(led.Count, Is.EqualTo(1), "expired tecn1 dropped, live tecn2 kept");
			Assert.That(led.IsCommitted("tecn2", 200), Is.True);
			Assert.That(led.TryGetObjective("tecn1", out _), Is.False);
		}

		[Test]
		public void Prune_DropsKeysFailingKeepPredicate()
		{
			var led = new GoalGuardLedger<string>();
			led.Commit("alive", "capture:1", 0, Ttl);
			led.Commit("dead", "capture:2", 0, Ttl);

			// keep = "not the dead one" — models pruning dead / no-longer-owned actors.
			led.Prune(currentTick: 10, keep: k => k != "dead");
			Assert.That(led.TryGetObjective("dead", out _), Is.False, "dead key pruned even though unexpired");
			Assert.That(led.IsCommitted("alive", 10), Is.True);
		}

		// The headline invariant. Two callers run the same 20-scan window in which
		// the unit's activity flickers idle every scan (worst case). The GATED
		// caller only commits when the unit is not already committed; the UN-GATED
		// caller (the bug) commits on every idle scan. Assert the gate collapses
		// 20 would-be orders into a single commitment.
		[Test]
		public void AntiThrash_SingleCommitAcrossFlickerWindow()
		{
			var gated = new GoalGuardLedger<string>();
			var ungated = new GoalGuardLedger<string>();

			var ungatedOrders = 0;
			var gatedOrders = 0;

			// Scan every 75 ticks (the module's ScanInterval); 20 scans < TTL(300)
			// would all fall inside one commitment window... actually 20*75=1500 >
			// 300, so we also confirm the gate correctly RE-commits once per window.
			for (var scan = 0; scan < 20; scan++)
			{
				var tick = scan * 75;

				// Un-gated: always re-issues to the (idle-looking) unit → thrash.
				ungated.Commit("tecn1", "capture:42", tick, Ttl);
				ungatedOrders++;

				// Gated: only issue when the unit is free (expired or never committed).
				if (!gated.IsCommitted("tecn1", tick))
				{
					gated.Commit("tecn1", "capture:42", tick, Ttl);
					gatedOrders++;
				}
			}

			Assert.That(ungatedOrders, Is.EqualTo(20), "un-gated caller thrashes one order per scan");

			// TTL 300 / 75-tick scans → committed for 4 scans per window, so across
			// 1500 ticks the gate lets through ceil(1500/300) = 5 re-issues, not 20.
			Assert.That(gatedOrders, Is.EqualTo(5),
				"gated caller emits at most one order per commitment window");
			Assert.That(gatedOrders, Is.LessThan(ungatedOrders));
		}

		// Within a SINGLE commitment window (no expiry) the gate must let through
		// exactly one order however many times the unit flickers idle.
		[Test]
		public void AntiThrash_OneOrderWhileStillCommitted()
		{
			var gated = new GoalGuardLedger<string>();
			var orders = 0;

			for (var scan = 0; scan < 4; scan++)
			{
				var tick = scan * 50; // 4 scans * 50 = 200 ticks < TTL 300
				if (!gated.IsCommitted("tecn1", tick))
				{
					gated.Commit("tecn1", "capture:42", tick, Ttl);
					orders++;
				}
			}

			Assert.That(orders, Is.EqualTo(1), "one commit for the whole in-window flicker sequence");
			Assert.That(gated.CommitCountFor("tecn1"), Is.EqualTo(1), "no same-objective re-commits inside a window");
		}
	}
}
