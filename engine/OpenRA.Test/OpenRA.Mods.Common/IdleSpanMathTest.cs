#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the pure idle-span bookkeeping the behavior-lint logger folds per unit
	/// (total idle + longest single span, and the end-of-game "still idle" snapshot).
	/// The world-touching parts (reading Actor.IsIdle each tick, emitting JSONL) live
	/// in <see cref="UnitLifecycleLogger"/>; the arithmetic edge cases are
	/// <see cref="IdleSpanAccumulator"/> so they can be exercised with no World.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/World/UnitLifecycleLogger.cs
	/// </summary>
	[TestFixture]
	public class IdleSpanMathTest
	{
		[Test]
		public void FreshAccumulatorIsZeroAndNotIdle()
		{
			var acc = default(IdleSpanAccumulator);
			Assert.That(acc.Idle, Is.False);
			Assert.That(acc.TotalIdle, Is.EqualTo(0));
			Assert.That(acc.LongestIdle, Is.EqualTo(0));
		}

		[Test]
		public void SingleClosedSpanFoldsIntoTotals()
		{
			var acc = default(IdleSpanAccumulator);
			acc.Start(100);
			var dur = acc.End(150);

			Assert.That(dur, Is.EqualTo(50));
			Assert.That(acc.Idle, Is.False);
			Assert.That(acc.TotalIdle, Is.EqualTo(50));
			Assert.That(acc.LongestIdle, Is.EqualTo(50));
		}

		[Test]
		public void TotalSumsAndLongestTracksTheMaxSpan()
		{
			var acc = default(IdleSpanAccumulator);
			acc.Start(0);
			acc.End(20);   // span 20
			acc.Start(100);
			acc.End(600);  // span 500 (the longest)
			acc.Start(1000);
			acc.End(1100); // span 100

			Assert.That(acc.TotalIdle, Is.EqualTo(620));
			Assert.That(acc.LongestIdle, Is.EqualTo(500));
		}

		[Test]
		public void StartIsIdempotentWhileAlreadyIdle()
		{
			// A second Start without an intervening End must not move the span origin,
			// mirroring the trait's edge-triggered guard (it only Starts on a false->true
			// IsIdle transition).
			var acc = default(IdleSpanAccumulator);
			acc.Start(100);
			acc.Start(140);
			Assert.That(acc.End(200), Is.EqualTo(100));
		}

		[Test]
		public void EndWhileNotIdleIsANoOp()
		{
			var acc = default(IdleSpanAccumulator);
			Assert.That(acc.End(500), Is.EqualTo(0));
			Assert.That(acc.TotalIdle, Is.EqualTo(0));
			Assert.That(acc.Idle, Is.False);
		}

		[Test]
		public void NegativeDurationIsClampedToZero()
		{
			// Defensive: a close tick preceding the start (should never happen in the
			// monotonic sim clock) must not corrupt the totals with a negative span.
			var acc = default(IdleSpanAccumulator);
			acc.Start(500);
			Assert.That(acc.End(400), Is.EqualTo(0));
			Assert.That(acc.TotalIdle, Is.EqualTo(0));
		}

		[Test]
		public void SnapshotClosesAnOpenSpanWithoutMutating()
		{
			// The end-of-game census case: a unit still idle at match end. Snapshot
			// reports totals AS IF the open span closed at the given tick, but leaves
			// the accumulator open (Idle stays true, totals unchanged).
			var acc = default(IdleSpanAccumulator);
			acc.Start(200);

			var (total, longest) = acc.Snapshot(1200);
			Assert.That(total, Is.EqualTo(1000));
			Assert.That(longest, Is.EqualTo(1000));

			Assert.That(acc.Idle, Is.True);
			Assert.That(acc.TotalIdle, Is.EqualTo(0));
			Assert.That(acc.LongestIdle, Is.EqualTo(0));
		}

		[Test]
		public void SnapshotFoldsOpenSpanOnTopOfClosedTotals()
		{
			var acc = default(IdleSpanAccumulator);
			acc.Start(0);
			acc.End(300);  // closed span 300
			acc.Start(900); // open at snapshot

			var (total, longest) = acc.Snapshot(1000);
			Assert.That(total, Is.EqualTo(400));   // 300 closed + 100 open
			Assert.That(longest, Is.EqualTo(300)); // open span (100) does not beat 300
		}

		[Test]
		public void SnapshotWhenNotIdleReturnsPlainTotals()
		{
			var acc = default(IdleSpanAccumulator);
			acc.Start(0);
			acc.End(750);

			var (total, longest) = acc.Snapshot(9999);
			Assert.That(total, Is.EqualTo(750));
			Assert.That(longest, Is.EqualTo(750));
		}
	}
}
