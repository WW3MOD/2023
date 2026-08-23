#region Copyright & License Information
/*
 * WW3MOD stall detection — contract pins for the shared follow-stall predicate.
 *
 * WHY THIS IS SHARED AND NOT COPIED. Mobile.MoveResult is never assigned, so a move that cannot path
 * reports InProgress forever rather than failing; the only evidence of a follow that will never arrive
 * is that the follower has not changed cell. AutoFollowAlly has always inferred it that way, and
 * AttendAllyActivity now needs the same inference to stop an unreachable ordered patient from pinning a
 * medic who then heals nobody.
 *
 * Three traps live in six lines, and each has its own test below: standing still AT the destination is
 * the CORRECT state and must not read as a stall; one cell of progress must clear the whole accumulator
 * and not merely pause it; and the accumulator must reset when it fires, or a per-tick caller re-fires
 * every tick after the first and abandons its target over and over.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common;

namespace OpenRA.Test
{
	[TestFixture]
	public class StallWatcherTest
	{
		const int Max = 100;

		static CPos Cell(int x)
		{
			return new CPos(x, 0);
		}

		[Test]
		public void AFollowerWhoNeverChangesCellIsStalledOnceTheBudgetIsSpent()
		{
			var stall = new StallWatcher();
			stall.MarkProgress(Cell(5));

			for (var i = 0; i < Max - 1; i++)
				Assert.That(stall.IsStalled(Cell(5), 1, Max), Is.False,
					"Firing before the budget is spent would abandon a follower who is merely walking a " +
					"long way round.");

			Assert.That(stall.IsStalled(Cell(5), 1, Max), Is.True,
				"A follower who has not moved a cell in the whole budget is stalled. Without this the " +
				"caller waits forever: a move that cannot path never reports failure.");
		}

		[Test]
		public void OneCellOfProgressClearsTheWholeAccumulator()
		{
			var stall = new StallWatcher();
			stall.MarkProgress(Cell(0));

			for (var i = 0; i < Max - 1; i++)
				stall.IsStalled(Cell(0), 1, Max);

			Assert.That(stall.IsStalled(Cell(1), 1, Max), Is.False);
			Assert.That(stall.MovedOnLastCheck, Is.True,
				"Movement must be reported so a caller that gave up on a stall knows it may resume.");

			for (var i = 0; i < Max - 1; i++)
				Assert.That(stall.IsStalled(Cell(1), 1, Max), Is.False,
					"Progress must RESET the accumulator, not pause it. Topping up a nearly-spent budget " +
					"would bench a follower who is moving, just slowly.");
		}

		[Test]
		public void ArrivingClearsAStallThatWasAlreadyAccumulating()
		{
			var stall = new StallWatcher();
			stall.MarkProgress(Cell(7));

			// The last stretch of the approach was spent inside one cell, so the accumulator is nearly
			// spent at the moment of arrival. This interleaving is the trap: asserting only that a
			// follower who arrives IMMEDIATELY never stalls proves nothing, because nothing had accrued.
			for (var i = 0; i < Max - 1; i++)
				stall.IsStalled(Cell(7), 1, Max);

			stall.MarkProgress(Cell(7));

			for (var i = 0; i < Max - 1; i++)
				Assert.That(stall.IsStalled(Cell(7), 1, Max), Is.False,
					"An escort standing with the man he is escorting, or a medic standing over the patient " +
					"he is treating, must never be read as a follow that failed. Arrival has to CLEAR what " +
					"the approach accrued, not merely stop adding to it.");
		}

		[Test]
		public void TheAccumulatorResetsWhenTheStallFires()
		{
			var stall = new StallWatcher();
			stall.MarkProgress(Cell(3));

			for (var i = 0; i < Max; i++)
				stall.IsStalled(Cell(3), 1, Max);

			Assert.That(stall.IsStalled(Cell(3), 1, Max), Is.False,
				"A per-tick caller must get ONE edge per stall, not a stream. Re-firing every tick would " +
				"re-abandon the same target on every frame for as long as the follower stays put.");
		}

		[Test]
		public void ASamplingCallerSpendsTheBudgetInItsOwnUnits()
		{
			// AutoFollowAlly samples once per CheckInterval and passes that as the elapsed count, so the
			// budget is spent in wall-clock ticks either way. Getting this wrong makes an interval-based
			// caller wait CheckInterval times too long.
			var stall = new StallWatcher();
			stall.MarkProgress(Cell(2));

			Assert.That(stall.IsStalled(Cell(2), 25, Max), Is.False);
			Assert.That(stall.IsStalled(Cell(2), 25, Max), Is.False);
			Assert.That(stall.IsStalled(Cell(2), 25, Max), Is.False);
			Assert.That(stall.IsStalled(Cell(2), 25, Max), Is.True);
		}
	}
}
