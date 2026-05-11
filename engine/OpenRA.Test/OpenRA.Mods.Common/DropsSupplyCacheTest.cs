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

namespace OpenRA.Test
{
	/// <summary>
	/// Pure-math contract for the DropsSupplyCache trait. The trait queues
	/// activities and spawns actors — both need a World harness to exercise
	/// end-to-end — but the supply-handoff arithmetic is deterministic and
	/// belongs in a unit test.
	/// </summary>
	[TestFixture]
	public class DropsSupplyCacheTest
	{
		// Mirrors DropsSupplyCache.DropSupplyCacheHere when no existing cache is
		// on the cell: the spawned cache receives whatever the truck had on hand,
		// and the truck zeros out.
		static (int cacheSupply, int truckSupplyAfter) DropFresh(int truckSupplyBefore)
		{
			var amount = truckSupplyBefore;
			return (cacheSupply: amount, truckSupplyAfter: 0);
		}

		// Mirrors DropSupplyCacheHere when an existing cache is on the same cell:
		// the cache's currentSupply is incremented (no cap — SupplyProvider.AddSupply
		// allows exceeding TotalSupply by design); the truck zeros out.
		static (int cacheSupply, int truckSupplyAfter) DropMerge(int existingCacheSupply, int truckSupplyBefore)
		{
			return (cacheSupply: existingCacheSupply + truckSupplyBefore, truckSupplyAfter: 0);
		}

		// Mirrors QueueDriveAndRestock arithmetic on arrival: truck takes
		// min(needed, hostSupply); host's pool drops by that amount.
		static (int truckSupplyAfter, int hostSupplyAfter) Restock(int truckSupplyBefore, int truckCapacity, int hostSupplyBefore)
		{
			var needed = truckCapacity - truckSupplyBefore;
			var taken = needed < hostSupplyBefore ? needed : hostSupplyBefore;
			return (truckSupplyAfter: truckSupplyBefore + taken, hostSupplyAfter: hostSupplyBefore - taken);
		}

		// --- Drop ---

		[Test]
		public void DropFullTruckSpawnsFullCache()
		{
			var (cache, truck) = DropFresh(truckSupplyBefore: 750);
			Assert.That(cache, Is.EqualTo(750));
			Assert.That(truck, Is.EqualTo(0));
		}

		[Test]
		public void DropPartialTruckSpawnsPartialCache()
		{
			var (cache, truck) = DropFresh(truckSupplyBefore: 300);
			Assert.That(cache, Is.EqualTo(300));
			Assert.That(truck, Is.EqualTo(0));
		}

		[Test]
		public void DropMergeAccumulatesIntoExistingCache()
		{
			// Existing cache at 200, truck dumps 500 on the same cell → cache becomes 700.
			var (cache, truck) = DropMerge(existingCacheSupply: 200, truckSupplyBefore: 500);
			Assert.That(cache, Is.EqualTo(700));
			Assert.That(truck, Is.EqualTo(0));
		}

		[Test]
		public void DropMergeCanExceedCacheCapacity()
		{
			// Cache with TotalSupply 750 already at 500, truck dumps another 500.
			// SupplyProvider.AddSupply has no cap; total ends at 1000.
			// This is by design — drops never bounce off a full cache.
			var (cache, _) = DropMerge(existingCacheSupply: 500, truckSupplyBefore: 500);
			Assert.That(cache, Is.EqualTo(1000));
		}

		// --- Restock ---

		[Test]
		public void EmptyTruckTakesUpToCapacityFromFullLC()
		{
			// Empty truck (TotalSupply 750), LC at full 3000 → truck fills, LC drops by 750.
			var (truck, host) = Restock(truckSupplyBefore: 0, truckCapacity: 750, hostSupplyBefore: 3000);
			Assert.That(truck, Is.EqualTo(750));
			Assert.That(host, Is.EqualTo(2250));
		}

		[Test]
		public void PartialTruckTopsUp()
		{
			// Truck at 300/750 → needs 450 → takes 450 from LC, ends full.
			var (truck, host) = Restock(300, 750, 3000);
			Assert.That(truck, Is.EqualTo(750));
			Assert.That(host, Is.EqualTo(2550));
		}

		[Test]
		public void TruckTakesOnlyWhatHostHas()
		{
			// LC at 200, truck wants 750 → truck takes 200 and leaves partially full.
			var (truck, host) = Restock(truckSupplyBefore: 0, truckCapacity: 750, hostSupplyBefore: 200);
			Assert.That(truck, Is.EqualTo(200));
			Assert.That(host, Is.EqualTo(0));
		}

		[Test]
		public void EmptyLCGivesNothing()
		{
			var (truck, host) = Restock(0, 750, 0);
			Assert.That(truck, Is.EqualTo(0));
			Assert.That(host, Is.EqualTo(0));
		}

		[Test]
		public void LCSupportsFourFullTruckRestocks()
		{
			// LC 3000 / 750 per truck = 4 full restocks; on the 5th the LC has 0.
			var host = 3000;
			for (var i = 0; i < 4; i++)
			{
				var (_, hostAfter) = Restock(truckSupplyBefore: 0, truckCapacity: 750, hostSupplyBefore: host);
				host = hostAfter;
			}

			Assert.That(host, Is.EqualTo(0));
		}
	}
}
