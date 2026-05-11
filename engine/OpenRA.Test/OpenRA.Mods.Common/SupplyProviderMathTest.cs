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
	/// Mirrors the supply-economy math defined in DOCS/reference/economy.md.
	/// AmmoPool rearm and evac/sell deductions are charged per BATCH of
	/// ReloadCount rounds, at SupplyValue cost per batch. The actual trait
	/// needs a full World/Actor setup; the formulas are reproduced here so
	/// regressions break a unit test instead of a playtest.
	/// </summary>
	[TestFixture]
	public class SupplyProviderMathTest
	{
		// Mirrors SupplyProvider.CalculateDelay() logic.
		static int CalculateDelay(int distance, int minRange, int baseDelay, int maxDelayMultiplier)
		{
			if (minRange <= 0)
				minRange = 1;

			var multiplier = (float)distance / minRange;
			if (multiplier < 1f) multiplier = 1f;
			if (multiplier > maxDelayMultiplier) multiplier = maxDelayMultiplier;

			return (int)(baseDelay * multiplier);
		}

		// Mirrors SupplyProvider rearm cost: one batch delivered per cycle,
		// SupplyValue charged per batch (regardless of how many rounds the
		// batch actually contains — partial last batches still cost full price).
		static int SupplyAfterBatchDelivered(int currentSupply, int supplyValuePerBatch)
		{
			return currentSupply - supplyValuePerBatch;
		}

		// Mirrors CustomSellValue per-pool deduction: floor(missing / ReloadCount)
		// full batches missing × SupplyValue per batch.
		static int PoolMissingValue(int maxAmmo, int currentAmmo, int reloadCount, int supplyValue)
		{
			var batchSize = reloadCount < 1 ? 1 : reloadCount;
			var missing = maxAmmo - currentAmmo;
			var missingBatches = missing / batchSize;
			return missingBatches * supplyValue;
		}

		// --- Distance-based delay ---

		[Test]
		public void DelayAtMinRangeIsBaseDelay()
		{
			var delay = CalculateDelay(distance: 1024, minRange: 1024, baseDelay: 15, maxDelayMultiplier: 4);
			Assert.That(delay, Is.EqualTo(15));
		}

		[Test]
		public void DelayCloserThanMinRangeIsClamped()
		{
			var delay = CalculateDelay(distance: 512, minRange: 1024, baseDelay: 15, maxDelayMultiplier: 4);
			Assert.That(delay, Is.EqualTo(15));
		}

		[Test]
		public void DelayAtDoubleRangeIsDoubleDelay()
		{
			var delay = CalculateDelay(distance: 2048, minRange: 1024, baseDelay: 15, maxDelayMultiplier: 4);
			Assert.That(delay, Is.EqualTo(30));
		}

		[Test]
		public void DelayAtMaxRangeIsCapped()
		{
			var delay = CalculateDelay(distance: 100000, minRange: 1024, baseDelay: 15, maxDelayMultiplier: 4);
			Assert.That(delay, Is.EqualTo(60)); // 15 * 4
		}

		[Test]
		public void DelayScalesLinearly()
		{
			var delay = CalculateDelay(distance: 3072, minRange: 1024, baseDelay: 15, maxDelayMultiplier: 4);
			Assert.That(delay, Is.EqualTo(45));
		}

		[Test]
		public void DelayWithZeroMinRangeDoesNotDivideByZero()
		{
			var delay = CalculateDelay(distance: 1024, minRange: 0, baseDelay: 15, maxDelayMultiplier: 4);
			Assert.That(delay, Is.EqualTo(60));
		}

		// --- Per-batch rearm cost ---

		[Test]
		public void OneBatchCostsOneSupplyValue()
		{
			// Bradley 25mm: ReloadCount 100, SV 5. One batch of 100 rounds costs 5.
			var remaining = SupplyAfterBatchDelivered(currentSupply: 500, supplyValuePerBatch: 5);
			Assert.That(remaining, Is.EqualTo(495));
		}

		[Test]
		public void MissilePoolCostsMoreThanAutocannonBatch()
		{
			// TOW: ReloadCount 1, SV 75. Autocannon: ReloadCount 100, SV 5.
			// Same pool budget shape, different per-batch cost.
			var afterTow = SupplyAfterBatchDelivered(500, 75);
			var afterAutocannon = SupplyAfterBatchDelivered(500, 5);
			Assert.That(afterTow, Is.EqualTo(425));
			Assert.That(afterAutocannon, Is.EqualTo(495));
		}

		[Test]
		public void FullBradleyAutocannonRefillCost()
		{
			// 9 batches × 5 supply = 45 supply for a full 900-round refill.
			var supply = 500;
			for (var i = 0; i < 9; i++)
				supply = SupplyAfterBatchDelivered(supply, 5);
			Assert.That(supply, Is.EqualTo(500 - 45));
		}

		// --- Per-batch evac/sell deduction (CustomSellValue) ---

		[Test]
		public void FullPoolHasNoDeduction()
		{
			// Bradley autocannon at max: Ammo=900, current=900 → 0 missing.
			var missing = PoolMissingValue(maxAmmo: 900, currentAmmo: 900, reloadCount: 100, supplyValue: 5);
			Assert.That(missing, Is.EqualTo(0));
		}

		[Test]
		public void EmptyPoolDeductsFullBudget()
		{
			// Bradley autocannon fully empty: 9 batches × 5 = 45.
			var missing = PoolMissingValue(maxAmmo: 900, currentAmmo: 0, reloadCount: 100, supplyValue: 5);
			Assert.That(missing, Is.EqualTo(45));
		}

		[Test]
		public void PartialBatchDoesNotCount()
		{
			// 99/100 rounds missing in a 100-round batch = 0 full batches missing.
			// Spec: "missingBatches = floor(missing / ReloadCount)".
			var missing = PoolMissingValue(maxAmmo: 100, currentAmmo: 1, reloadCount: 100, supplyValue: 5);
			Assert.That(missing, Is.EqualTo(0));
		}

		[Test]
		public void TowMissileDeductsImmediately()
		{
			// TOW: ReloadCount 1, SV 75. Each fired missile = 1 missing batch = 75 deduction.
			var missing = PoolMissingValue(maxAmmo: 8, currentAmmo: 5, reloadCount: 1, supplyValue: 75);
			Assert.That(missing, Is.EqualTo(3 * 75));
		}

		[Test]
		public void TankShellDeductsByBatch()
		{
			// Abrams 120mm: Ammo=40, ReloadCount=5, SV=30.
			// Firing 13 shells = 13 missing → 2 full batches (10 rounds) = 60.
			var missing = PoolMissingValue(maxAmmo: 40, currentAmmo: 27, reloadCount: 5, supplyValue: 30);
			Assert.That(missing, Is.EqualTo(2 * 30));
		}

		// --- Selection bar ---

		[Test]
		public void SelectionBarValue()
		{
			var totalSupply = 500;

			Assert.That((float)500 / totalSupply, Is.EqualTo(1.0f));
			Assert.That((float)250 / totalSupply, Is.EqualTo(0.5f));
			Assert.That((float)0 / totalSupply, Is.EqualTo(0.0f));
			Assert.That((float)100 / totalSupply, Is.EqualTo(0.2f));
		}
	}
}
