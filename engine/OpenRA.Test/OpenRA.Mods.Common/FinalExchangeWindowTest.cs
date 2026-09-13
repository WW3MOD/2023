#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * THE FIFTEEN SECONDS, pinned.
 *
 * The user's ruling (2026-09-13): "Both players have the 15 seconds to choose targets, or the Dead
 * Hand places them for them. Either way the outcome is the same, the nukes fly and the game ends."
 *
 * Four properties carry that, and each is a way the feature could be wrong while still looking
 * right on screen:
 *   START       -- the window opens once, with a real deadline, and the trigger is already a placer.
 *   IDEMPOTENCY -- a second trigger during the exchange changes NOTHING. Both paths can fire on the
 *                  same tick (the time limit expiring while a warhead is in the air), and a restart
 *                  would hand everyone a second game-ender and reset the clock.
 *   EXPIRY      -- the closing edge is reported exactly once, so the caller can hang the Dead Hand
 *                  placement off it without a second flag. Twice would build two salvos.
 *   THE SPLIT   -- placed and placed-for are a partition of the sides, in a fixed order, so nobody
 *                  is both shot for and left out.
 *
 * FinalExchangeWindow takes no World and no Actor, which is the whole reason it exists as a
 * separate class -- these run as ordinary unit tests with no engine fixture, no RNG and no clock.
 */

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FinalExchangeWindowTest
	{
		static readonly string[] BothSides = { "USA", "Russia" };

		[Test]
		public void TheWindowOpensOnTheTriggerTickAndRunsForItsDuration()
		{
			var w = new FinalExchangeWindow();
			Assert.That(w.Phase, Is.EqualTo(FinalExchangePhase.Idle));
			Assert.That(w.TicksRemaining(0), Is.EqualTo(0), "An unopened window has no clock.");

			Assert.That(w.Begin(1000, 250, BothSides, null), Is.True);
			Assert.That(w.Phase, Is.EqualTo(FinalExchangePhase.Open));
			Assert.That(w.OpenedTick, Is.EqualTo(1000));
			Assert.That(w.ClosesTick, Is.EqualTo(1250));
			Assert.That(w.TicksRemaining(1000), Is.EqualTo(250));
			Assert.That(w.TicksRemaining(1125), Is.EqualTo(125));
			Assert.That(w.TicksRemaining(1250), Is.EqualTo(0));
		}

		[Test]
		public void ADurationOfZeroSkipsTheWindowEntirely()
		{
			// The escape hatch: FinalExchangeWindowTicks <= 0 must leave the mode with the shape it had
			// before the window existed -- salvo on the trigger tick -- rather than a window that opens
			// and shuts on the same tick, which would still arm everybody for an instant.
			var w = new FinalExchangeWindow();
			Assert.That(w.Begin(10, 0, BothSides, null), Is.False);
			Assert.That(w.Phase, Is.EqualTo(FinalExchangePhase.Idle));

			Assert.That(new FinalExchangeWindow().Begin(10, -1, BothSides, null), Is.False);
		}

		[Test]
		public void TheTriggerHasAlreadyPlaced()
		{
			// Path (b): the side that fired a game-ender IS the first warhead of the exchange, so it is
			// not a side Dead Hand places for.
			var w = new FinalExchangeWindow();
			w.Begin(0, 250, BothSides, "USA");

			Assert.That(w.HasPlaced("USA"), Is.True);
			Assert.That(w.HasPlaced("Russia"), Is.False);
			Assert.That(w.PlacementCount, Is.EqualTo(1));
			Assert.That(w.Trigger, Is.EqualTo("USA"));
		}

		[Test]
		public void TheTimeLimitPathHasNoTriggerAndNobodyHasPlaced()
		{
			var w = new FinalExchangeWindow();
			w.Begin(0, 250, BothSides, null);

			Assert.That(w.Trigger, Is.Null);
			Assert.That(w.PlacementCount, Is.EqualTo(0));
			Assert.That(w.SidesPlacedForByDeadHand(), Is.EqualTo(BothSides));
		}

		[Test]
		public void ASecondTriggerDuringTheExchangeChangesNothing()
		{
			var w = new FinalExchangeWindow();
			w.Begin(1000, 250, BothSides, "USA");
			w.RecordPlacement("Russia");

			// Same tick, the other path, a different side and a different duration: all of it ignored.
			Assert.That(w.Begin(1000, 900, new[] { "China" }, "Russia"), Is.False);
			Assert.That(w.Begin(1200, 900, new[] { "China" }, null), Is.False);

			Assert.That(w.ClosesTick, Is.EqualTo(1250), "A second trigger restarted the clock.");
			Assert.That(w.Trigger, Is.EqualTo("USA"));
			Assert.That(w.PlacementCount, Is.EqualTo(2));
			Assert.That(w.SidesThatPlaced(), Is.EqualTo(BothSides));
			Assert.That(w.SidesPlacedForByDeadHand(), Is.Empty);
		}

		[Test]
		public void ThereIsNoReopeningAfterItHasClosed()
		{
			var w = new FinalExchangeWindow();
			w.Begin(0, 100, BothSides, null);
			w.Tick(100);

			Assert.That(w.Phase, Is.EqualTo(FinalExchangePhase.Closed));
			Assert.That(w.Begin(200, 250, BothSides, null), Is.False);
			Assert.That(w.Phase, Is.EqualTo(FinalExchangePhase.Closed));
		}

		[Test]
		public void TheClosingEdgeIsReportedExactlyOnce()
		{
			var w = new FinalExchangeWindow();
			w.Begin(1000, 250, BothSides, null);

			var edges = 0;
			for (var t = 1000; t < 1400; t++)
				if (w.Tick(t))
					edges++;

			Assert.That(edges, Is.EqualTo(1), "Dead Hand would have placed a salvo per tick.");
			Assert.That(w.Phase, Is.EqualTo(FinalExchangePhase.Closed));
		}

		[Test]
		public void TheClosingEdgeLandsOnTheDeadlineTickAndNotBefore()
		{
			var w = new FinalExchangeWindow();
			w.Begin(0, 250, BothSides, null);

			Assert.That(w.Tick(249), Is.False);
			Assert.That(w.Phase, Is.EqualTo(FinalExchangePhase.Open));
			Assert.That(w.Tick(250), Is.True);
		}

		[Test]
		public void AnOverrunTickStillClosesTheWindow()
		{
			// The caller ticks every frame, but a paused or stepped session can hand this a tick past
			// the deadline without ever hitting it exactly. Missing the edge would hang the match with
			// victory checks suspended and no salvo -- the worst failure this file guards.
			var w = new FinalExchangeWindow();
			w.Begin(0, 250, BothSides, null);

			Assert.That(w.Tick(9999), Is.True);
			Assert.That(w.Phase, Is.EqualTo(FinalExchangePhase.Closed));
		}

		[Test]
		public void PlacedAndPlacedForArePartitionOfTheSides()
		{
			var w = new FinalExchangeWindow();
			w.Begin(0, 250, new[] { "USA", "Russia", "Belarus" }, null);
			Assert.That(w.RecordPlacement("Russia"), Is.True);

			Assert.That(w.SidesThatPlaced(), Is.EqualTo(new[] { "Russia" }));
			Assert.That(w.SidesPlacedForByDeadHand(), Is.EqualTo(new[] { "USA", "Belarus" }));

			// The partition property itself, stated rather than implied by the two lists above.
			var union = w.SidesThatPlaced().Concat(w.SidesPlacedForByDeadHand()).OrderBy(s => s).ToArray();
			Assert.That(union, Is.EqualTo(new[] { "Belarus", "Russia", "USA" }));
			Assert.That(w.SidesThatPlaced().Intersect(w.SidesPlacedForByDeadHand()), Is.Empty);
		}

		[Test]
		public void TheSplitIsInSeatOrderRegardlessOfWhoFiredFirst()
		{
			// Determinism: the two lists are walked in the order the sides were handed in -- which is
			// world.Players order, fixed at world creation -- and NOT in the order they fired.
			var w = new FinalExchangeWindow();
			w.Begin(0, 250, new[] { "USA", "Russia", "Belarus" }, null);
			w.RecordPlacement("Belarus");
			w.RecordPlacement("USA");

			Assert.That(w.SidesThatPlaced(), Is.EqualTo(new[] { "USA", "Belarus" }));
		}

		[Test]
		public void RepeatedPlacementsByOneSideCountOnce()
		{
			// A Sarmat is six warheads on one order and MissileStrikePower reports each of them, so
			// this is the ordinary case rather than a defensive one.
			var w = new FinalExchangeWindow();
			w.Begin(0, 250, BothSides, null);

			Assert.That(w.RecordPlacement("USA"), Is.True);
			Assert.That(w.RecordPlacement("USA"), Is.False);
			Assert.That(w.RecordPlacement("USA"), Is.False);
			Assert.That(w.PlacementCount, Is.EqualTo(1));
			Assert.That(w.SidesPlacedForByDeadHand(), Is.EqualTo(new[] { "Russia" }));
		}

		[Test]
		public void APlacementOutsideTheWindowIsNotAPlacement()
		{
			// Before: nothing to place into. After: a warhead fired while the salvo is already running
			// is part of the salvo, and counting it would retroactively move a side out of the
			// placed-for list that Dead Hand has already shot for.
			var w = new FinalExchangeWindow();
			Assert.That(w.RecordPlacement("USA"), Is.False);

			w.Begin(0, 100, BothSides, null);
			w.Tick(100);
			Assert.That(w.RecordPlacement("USA"), Is.False);
			Assert.That(w.PlacementCount, Is.EqualTo(0));
		}

		[Test]
		public void ASideThatFiresWithoutBeingEnumeratedStillCountsAsHavingPlaced()
		{
			// A Lua scenario, or a side the survivor scan did not list. It must not end up in BOTH
			// lists, which is what appending it to the seat list prevents.
			var w = new FinalExchangeWindow();
			w.Begin(0, 250, new[] { "USA" }, null);
			w.RecordPlacement("Russia");

			Assert.That(w.SidesThatPlaced(), Is.EqualTo(new[] { "Russia" }));
			Assert.That(w.SidesPlacedForByDeadHand(), Is.EqualTo(new[] { "USA" }));
		}

		[Test]
		public void NullAndEmptySidesAreIgnoredRatherThanRecorded()
		{
			// The time-limit path passes a null trigger, and world.Players can carry a side with no
			// usable name; neither may become a phantom entry in the split.
			var w = new FinalExchangeWindow();
			w.Begin(0, 250, new[] { "USA", null, "" }, null);

			Assert.That(w.RecordPlacement(null), Is.False);
			Assert.That(w.RecordPlacement(""), Is.False);
			Assert.That(w.SidesPlacedForByDeadHand(), Is.EqualTo(new[] { "USA" }));
		}
	}
}
