#region Copyright & License Information
/*
 * WW3MOD sub-tick clock arithmetic (2026-09-06).
 *
 * SubTickClock.Measure is the one number the whole motion-smoothing feature multiplies by, and it is
 * derived inside Game.Loop where no test can reach it. Measure is therefore a pure function of the
 * three values the loop holds, and this fixture is the only place its edges are pinned.
 */
#endregion

using NUnit.Framework;
using OpenRA.Graphics;

namespace OpenRA.Test
{
	[TestFixture]
	public class SubTickClockTest
	{
		// Game.Loop bumps nextLogic by logicInterval the instant a tick runs, so `now` and
		// `nextLogic - logicInterval` coincide on the frame immediately after a tick.
		const int Interval = 60;

		[Test]
		public void FrameImmediatelyAfterATickIsZero()
		{
			Assert.That(SubTickClock.Measure(1000, 1000 + Interval, Interval), Is.EqualTo(0),
				"The frame drawn right after a logic tick must predict nothing, or the sprite is " +
				"offset from the position everything else in the world agrees it is at.");
		}

		[Test]
		public void FrameImmediatelyBeforeTheNextTickIsOne()
		{
			Assert.That(SubTickClock.Measure(1000 + Interval, 1000 + Interval, Interval),
				Is.EqualTo(SubTickClock.One));
		}

		[Test]
		public void MidTickIsProportional()
		{
			Assert.That(SubTickClock.Measure(1000 + Interval / 2, 1000 + Interval, Interval),
				Is.EqualTo(SubTickClock.One / 2));

			// A quarter of the way through, checked separately because the halfway case would also
			// pass under a formula that has the numerator and denominator the wrong way round.
			Assert.That(SubTickClock.Measure(1000 + 15, 1000 + Interval, Interval),
				Is.EqualTo(SubTickClock.One / 4));
		}

		[Test]
		public void LateLogicIsClampedToOneTick()
		{
			// Game.Loop deliberately lets nextLogic fall behind `now` when the logic is running late
			// (MaxLogicTicksBehind). Unclamped this reads as a fraction well above one and predicts
			// the sprite several ticks of travel ahead of anything real.
			Assert.That(SubTickClock.Measure(2000, 1000, Interval), Is.EqualTo(SubTickClock.One));
		}

		[Test]
		public void EarlyFrameIsClampedToZero()
		{
			// nextLogic more than a full interval away — a clock adjustment, or a caller passing an
			// interval smaller than the one nextLogic was bumped by.
			Assert.That(SubTickClock.Measure(1000, 1000 + 10 * Interval, Interval), Is.EqualTo(0));
		}

		[Test]
		public void NonPositiveIntervalPredictsNothing()
		{
			Assert.That(SubTickClock.Measure(1000, 1000, 0), Is.EqualTo(0));
			Assert.That(SubTickClock.Measure(1000, 1000, -5), Is.EqualTo(0));
		}

		[Test]
		public void MeasureNeverLeavesTheUnitRange()
		{
			// Swept rather than spot-checked: everything downstream multiplies a displacement by this
			// and relies on the product never exceeding one tick of travel.
			for (var interval = 1; interval <= 200; interval++)
			{
				for (var offset = -3 * interval; offset <= 3 * interval; offset++)
				{
					var f = SubTickClock.Measure(1000, 1000 + offset, interval);
					Assert.That(f, Is.InRange(0, SubTickClock.One),
						$"interval {interval}, nextLogic offset {offset} produced {f}");
				}
			}
		}

		[Test]
		public void UpdateAndResetMoveTheSharedValue()
		{
			SubTickClock.Update(1000 + Interval / 2, 1000 + Interval, Interval);
			Assert.That(SubTickClock.Fraction, Is.EqualTo(SubTickClock.One / 2));

			SubTickClock.Reset();
			Assert.That(SubTickClock.Fraction, Is.EqualTo(0));
		}
	}
}
