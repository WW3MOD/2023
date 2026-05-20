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

using System;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// Mirrors the per-tick transfer math from AbsorbsSupplyCache.Tick. The
	/// trait orchestration (finding a nearby cache, disposing empties) needs a
	/// World; the arithmetic is independent and reproduced here so a regression
	/// breaks a unit test, not a playtest.
	///
	/// Real formula:
	///     headroom  = TotalSupply - CurrentSupply
	///     toTransfer = min(TransferRate, headroom)
	///     available  = min(toTransfer, cacheCurrentSupply)
	///     deduct `available` from cache, add `available` to host.
	/// </summary>
	[TestFixture]
	public class AbsorbsSupplyCacheMathTest
	{
		static (int hostAfter, int cacheAfter, int transferred) Tick(
			int hostCurrent, int hostTotal, int transferRate, int cacheCurrent)
		{
			if (hostCurrent >= hostTotal)
				return (hostCurrent, cacheCurrent, 0); // Pool full short-circuit.

			var headroom = hostTotal - hostCurrent;
			var toTransfer = Math.Min(transferRate, headroom);
			var available = Math.Min(toTransfer, cacheCurrent);
			if (available <= 0)
				return (hostCurrent, cacheCurrent, 0);

			return (hostCurrent + available, cacheCurrent - available, available);
		}

		// --- Single tick ---

		[Test]
		public void EmptyHostTakesFullTransferRateFromFullCache()
		{
			// Host 0/500, transferRate 50, cache 750 → +50 to host, -50 from cache.
			var (host, cache, t) = Tick(hostCurrent: 0, hostTotal: 500, transferRate: 50, cacheCurrent: 750);
			Assert.That(host, Is.EqualTo(50));
			Assert.That(cache, Is.EqualTo(700));
			Assert.That(t, Is.EqualTo(50));
		}

		[Test]
		public void NearFullHostOnlyTakesHeadroom()
		{
			// Host 480/500 → headroom = 20 → transfer = min(50, 20) = 20.
			var (host, cache, t) = Tick(hostCurrent: 480, hostTotal: 500, transferRate: 50, cacheCurrent: 750);
			Assert.That(host, Is.EqualTo(500));
			Assert.That(cache, Is.EqualTo(730));
			Assert.That(t, Is.EqualTo(20));
		}

		[Test]
		public void FullHostTransfersNothing()
		{
			var (host, cache, t) = Tick(hostCurrent: 500, hostTotal: 500, transferRate: 50, cacheCurrent: 750);
			Assert.That(host, Is.EqualTo(500));
			Assert.That(cache, Is.EqualTo(750));
			Assert.That(t, Is.EqualTo(0));
		}

		[Test]
		public void AlmostEmptyCacheLimitsTransfer()
		{
			// Cache 7, transferRate 50 → only 7 available.
			var (host, cache, t) = Tick(hostCurrent: 100, hostTotal: 500, transferRate: 50, cacheCurrent: 7);
			Assert.That(host, Is.EqualTo(107));
			Assert.That(cache, Is.EqualTo(0));
			Assert.That(t, Is.EqualTo(7));
		}

		[Test]
		public void EmptyCacheTransfersNothing()
		{
			var (host, cache, t) = Tick(hostCurrent: 100, hostTotal: 500, transferRate: 50, cacheCurrent: 0);
			Assert.That(host, Is.EqualTo(100));
			Assert.That(cache, Is.EqualTo(0));
			Assert.That(t, Is.EqualTo(0));
		}

		[Test]
		public void HostOverfilledClamps()
		{
			// Defensive: if AddSupply or merges pushed host past TotalSupply, no further
			// transfer should happen (real Tick early-returns on >= TotalSupply).
			var (host, cache, t) = Tick(hostCurrent: 600, hostTotal: 500, transferRate: 50, cacheCurrent: 100);
			Assert.That(t, Is.EqualTo(0));
			Assert.That(host, Is.EqualTo(600));
			Assert.That(cache, Is.EqualTo(100));
		}

		// --- Iterated transfer to drain a cache ---

		[Test]
		public void RepeatedTicksDrainCacheIntoHost()
		{
			var host = 0;
			var cache = 250;
			var steps = 0;
			while (cache > 0 && steps < 100)
			{
				(host, cache, _) = Tick(host, hostTotal: 500, transferRate: 50, cacheCurrent: cache);
				steps++;
			}

			// 250 / 50 = 5 ticks
			Assert.That(steps, Is.EqualTo(5));
			Assert.That(cache, Is.EqualTo(0));
			Assert.That(host, Is.EqualTo(250));
		}

		[Test]
		public void TransferStopsWhenHostFills()
		{
			// Cache far larger than host can hold → transfer stops at 500.
			var host = 0;
			var cache = 10_000;
			var steps = 0;
			while (host < 500 && steps < 100)
			{
				(host, cache, _) = Tick(host, hostTotal: 500, transferRate: 50, cacheCurrent: cache);
				steps++;
			}

			Assert.That(host, Is.EqualTo(500));
			Assert.That(cache, Is.EqualTo(9500));
			Assert.That(steps, Is.EqualTo(10));
		}

		[Test]
		public void TransferConservesTotalSupply()
		{
			// The math is a zero-sum transfer — total supply across host+cache is invariant.
			var startTotal = 100 + 600;
			var (host, cache, _) = Tick(hostCurrent: 100, hostTotal: 500, transferRate: 50, cacheCurrent: 600);
			Assert.That(host + cache, Is.EqualTo(startTotal));
		}
	}
}
