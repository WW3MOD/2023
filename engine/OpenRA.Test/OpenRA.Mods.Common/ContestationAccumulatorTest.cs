#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Tournament;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the verdict_version 9 contestation telemetry accumulator behind BotVsBotMatchWatcher:
	/// min_control_bar_pct, ticks_bar_below_100 and ticks_with_enemy_in_ring. Pure math over the
	/// samples the watcher reads from SupplyRouteContestation; no World/Actor setup needed.
	/// </summary>
	[TestFixture]
	public class ContestationAccumulatorTest
	{
		const int BarMax = 100000;
		const int Interval = 25;

		[Test]
		public void NoSampleReadsMinusOneAndZeroTicks()
		{
			// The serialized "never sampled" state must be distinguishable from "never dented" (100).
			var acc = new ContestationAccumulator();
			Assert.That(acc.Samples, Is.EqualTo(0));
			Assert.That(acc.MinControlBarPct, Is.EqualTo(-1));
			Assert.That(acc.TicksBarBelow100, Is.EqualTo(0L));
			Assert.That(acc.TicksWithEnemyInRing, Is.EqualTo(0L));
		}

		[Test]
		public void UncontestedMatchReadsFullBarAndNoTicks()
		{
			// The expected shape of a quiet S1 eco match: 300 samples (7500 ticks), bar never moves.
			var acc = new ContestationAccumulator();
			for (var i = 0; i < 300; i++)
				acc.Sample(BarMax, BarMax, false, Interval);

			Assert.That(acc.MinControlBarPct, Is.EqualTo(100));
			Assert.That(acc.TicksBarBelow100, Is.EqualTo(0L));
			Assert.That(acc.TicksWithEnemyInRing, Is.EqualTo(0L));
		}

		[Test]
		public void MinIsTheLowestSampleNotTheLast()
		{
			var acc = new ContestationAccumulator();
			acc.Sample(BarMax, BarMax, false, Interval);
			acc.Sample(40000, BarMax, true, Interval);
			acc.Sample(90000, BarMax, false, Interval);

			Assert.That(acc.MinControlBarPct, Is.EqualTo(40));
		}

		[Test]
		public void PercentFloorsSoAnyDentReadsBelow100()
		{
			// One bar unit short of full must not round up to 100 — otherwise min_control_bar_pct
			// would say "never dented" while ticks_bar_below_100 counts the same sample.
			var acc = new ContestationAccumulator();
			acc.Sample(BarMax - 1, BarMax, false, Interval);

			Assert.That(acc.MinControlBarPct, Is.EqualTo(99));
			Assert.That(acc.TicksBarBelow100, Is.EqualTo((long)Interval));
		}

		[Test]
		public void EmptiedBarReadsZero()
		{
			// controlBar == 0 is the defeat phase; it counts as below full.
			var acc = new ContestationAccumulator();
			acc.Sample(0, BarMax, true, Interval);

			Assert.That(acc.MinControlBarPct, Is.EqualTo(0));
			Assert.That(acc.TicksBarBelow100, Is.EqualTo((long)Interval));
		}

		[Test]
		public void EnemyPresenceIsCountedIndependentlyOfTheBar()
		{
			// A defended SR: enemy in the ring, friendly surplus, bar stays full. The two counters
			// must not be coupled — that difference is what the presence counter exists to show.
			var acc = new ContestationAccumulator();
			for (var i = 0; i < 4; i++)
				acc.Sample(BarMax, BarMax, true, Interval);

			Assert.That(acc.TicksWithEnemyInRing, Is.EqualTo(4L * Interval));
			Assert.That(acc.TicksBarBelow100, Is.EqualTo(0L));
			Assert.That(acc.MinControlBarPct, Is.EqualTo(100));
		}

		[Test]
		public void EachSampleIsCreditedTheWholeInterval()
		{
			var acc = new ContestationAccumulator();
			acc.Sample(50000, BarMax, true, 7);
			acc.Sample(BarMax, BarMax, false, 7);
			acc.Sample(60000, BarMax, true, 7);

			Assert.That(acc.Samples, Is.EqualTo(3));
			Assert.That(acc.TicksBarBelow100, Is.EqualTo(14L));
			Assert.That(acc.TicksWithEnemyInRing, Is.EqualTo(14L));
		}

		[Test]
		public void NonPositiveBarMaxDoesNotThrow()
		{
			var acc = new ContestationAccumulator();
			Assert.DoesNotThrow(() => acc.Sample(0, 0, false, Interval));
			Assert.That(acc.MinControlBarPct, Is.EqualTo(0));
		}
	}
}
