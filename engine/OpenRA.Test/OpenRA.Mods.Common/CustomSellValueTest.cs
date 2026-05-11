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
	/// Mirrors the per-batch evac/sell deduction defined in
	/// engine/OpenRA.Mods.Common/Traits/CustomSellValue.cs. The real call needs
	/// a full Actor/World harness; we reproduce the formula so the contract is
	/// locked behind a unit test.
	/// </summary>
	[TestFixture]
	public class CustomSellValueTest
	{
		// Pool deduction = floor(missingRounds / batchSize) × supplyValue.
		static int PoolMissingValue(int maxAmmo, int currentAmmo, int reloadCount, int supplyValue)
		{
			if (supplyValue <= 0)
				return 0;

			var batchSize = reloadCount < 1 ? 1 : reloadCount;
			var missing = maxAmmo - currentAmmo;
			var missingBatches = missing / batchSize;
			return missingBatches * supplyValue;
		}

		// Final refund = max(0, baseValue - sum(pool missing) - missing supply).
		static int Refund(int baseValue, params int[] poolMissingValues)
		{
			var total = baseValue;
			foreach (var m in poolMissingValues)
				total -= m;

			return total < 0 ? 0 : total;
		}

		// --- Single-pool deduction ---

		[Test]
		public void FullPoolDeductsNothing()
		{
			var missing = PoolMissingValue(maxAmmo: 900, currentAmmo: 900, reloadCount: 100, supplyValue: 5);
			Assert.That(missing, Is.EqualTo(0));
		}

		[Test]
		public void EmptyPoolDeductsFullBudget()
		{
			// Bradley 25mm fully fired: 9 batches × 5 = 45.
			var missing = PoolMissingValue(900, 0, 100, 5);
			Assert.That(missing, Is.EqualTo(45));
		}

		[Test]
		public void PartialBatchDoesNotDeduct()
		{
			// 99/100 rounds fired in a single batch: floor(99/100) = 0 full batches missing.
			var missing = PoolMissingValue(100, 1, 100, 5);
			Assert.That(missing, Is.EqualTo(0));
		}

		[Test]
		public void EachWholeBatchDeducts()
		{
			// 250 of 900 rounds fired = 2 full batches missing × 5 = 10.
			var missing = PoolMissingValue(900, 650, 100, 5);
			Assert.That(missing, Is.EqualTo(10));
		}

		[Test]
		public void ReloadCountOneDeductsPerRound()
		{
			// TOW: ReloadCount 1, SupplyValue 75. 3 fired = 3 × 75 = 225.
			var missing = PoolMissingValue(maxAmmo: 8, currentAmmo: 5, reloadCount: 1, supplyValue: 75);
			Assert.That(missing, Is.EqualTo(225));
		}

		[Test]
		public void SupplyValueZeroNeverDeducts()
		{
			// Pools with SupplyValue 0 (e.g. internal/disabled) skip the loop.
			var missing = PoolMissingValue(maxAmmo: 100, currentAmmo: 0, reloadCount: 10, supplyValue: 0);
			Assert.That(missing, Is.EqualTo(0));
		}

		// --- Multi-pool sum ---

		[Test]
		public void MultiplePoolsSum()
		{
			// Bradley evac empty: 25mm full (0 missing) + TOW empty (8 × 75 = 600).
			// Cost 1500 - 600 = 900 refund.
			var refund = Refund(1500, PoolMissingValue(900, 900, 100, 5), PoolMissingValue(8, 0, 1, 75));
			Assert.That(refund, Is.EqualTo(900));
		}

		[Test]
		public void RefundFloorsAtZero()
		{
			// A 100-cost unit with a 600-budget missile pool fully expended.
			// Refund clamps at 0 rather than going negative.
			var refund = Refund(100, PoolMissingValue(8, 0, 1, 75));
			Assert.That(refund, Is.EqualTo(0));
		}

		[Test]
		public void FullUnitRefundsFullCost()
		{
			// Nothing fired = no deductions = full cost back.
			var refund = Refund(1500, PoolMissingValue(900, 900, 100, 5), PoolMissingValue(8, 8, 1, 75));
			Assert.That(refund, Is.EqualTo(1500));
		}

		// --- SupplyProvider missing supply (LC / truck / cache) ---
		// Mirrors SupplyProvider.MissingSupplyValue:
		//   (long)SupplyCreditValue × missing / TotalSupply

		static int MissingSupplyValue(int supplyCreditValue, int totalSupply, int currentSupply)
		{
			if (supplyCreditValue <= 0 || totalSupply <= 0)
				return 0;

			var missing = totalSupply - currentSupply;
			return (int)((long)supplyCreditValue * missing / totalSupply);
		}

		[Test]
		public void FullSupplyProviderDeductsNothing()
		{
			var missing = MissingSupplyValue(supplyCreditValue: 750, totalSupply: 750, currentSupply: 750);
			Assert.That(missing, Is.EqualTo(0));
		}

		[Test]
		public void EmptySupplyProviderDeductsAllCredit()
		{
			// Drained TRUK: 750 × 750 / 750 = 750.
			var missing = MissingSupplyValue(750, 750, 0);
			Assert.That(missing, Is.EqualTo(750));
		}

		[Test]
		public void PartialSupplyProviderScalesProportionally()
		{
			// Half-drained: 750 × 375 / 750 = 375.
			var missing = MissingSupplyValue(750, 750, 375);
			Assert.That(missing, Is.EqualTo(375));
		}

		[Test]
		public void TruckRefundCombinesCostAndMissingSupply()
		{
			// TRUK cost 1000, half-drained supply: refund = 1000 - 375 = 625.
			var refund = Refund(1000, MissingSupplyValue(750, 750, 375));
			Assert.That(refund, Is.EqualTo(625));
		}
	}
}
