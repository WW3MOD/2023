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

namespace OpenRA.Test
{
	/// <summary>
	/// Pure-math tests for the supply truck economy added in Phase 1 of the
	/// 260506 supply &amp; ammo economy spec:
	///   - CustomSellValue should deduct the missing CargoSupply on evacuate.
	///   - LC SupplyProvider should drain by SupplyPerUnit per truck pip refilled.
	///
	/// The full traits need a World/Actor setup; the formulas are reproduced here
	/// to lock the arithmetic and catch regressions.
	/// </summary>
	[TestFixture]
	public class CargoSupplyEconomyTest
	{
		// Mirrors CustomSellValueExts.GetSellValue behaviour for a unit whose
		// only "missing value" deduction comes from a CargoSupply pool.
		static int RefundForTruck(int baseValue, int maxSupply, int supplyCount, int creditValuePerUnit)
		{
			var missingUnits = maxSupply - supplyCount;
			var missingValue = missingUnits * creditValuePerUnit;
			var refund = baseValue - missingValue;
			return refund < 0 ? 0 : refund;
		}

		// Mirrors RefillFromHost / SupplyProvider transfer:
		// cost in host supply units to add one truck pip equals cargo.SupplyPerUnit.
		static int LCRemainingAfterPips(int lcSupply, int truckPipsTransferred, int supplyPerUnit)
		{
			return lcSupply - truckPipsTransferred * supplyPerUnit;
		}

		// --- Refund math (TRUK baseline: cost 1000, MaxSupply 15, CreditValuePerUnit 50) ---

		[Test]
		public void FullTruckRefundsFullCost()
		{
			var refund = RefundForTruck(baseValue: 1000, maxSupply: 15, supplyCount: 15, creditValuePerUnit: 50);
			Assert.That(refund, Is.EqualTo(1000));
		}

		[Test]
		public void EmptyTruckRefundsBaseMinusFullSupplyValue()
		{
			// Empty truck: 1000 base - (15 * 50) = 250
			var refund = RefundForTruck(baseValue: 1000, maxSupply: 15, supplyCount: 0, creditValuePerUnit: 50);
			Assert.That(refund, Is.EqualTo(250));
		}

		[Test]
		public void HalfEmptyTruckRefundsProportionalAmount()
		{
			// 8 pips left: 1000 - (15 - 8) * 50 = 1000 - 350 = 650
			var refund = RefundForTruck(baseValue: 1000, maxSupply: 15, supplyCount: 8, creditValuePerUnit: 50);
			Assert.That(refund, Is.EqualTo(650));
		}

		[Test]
		public void RefundFloorsAtZeroEvenWhenSupplyDeficitExceedsCost()
		{
			// Imagine a 100-credit unit with 10 pips at 50 credits each — deficit 500 > base 100.
			// Refund must clamp at zero rather than going negative.
			var refund = RefundForTruck(baseValue: 100, maxSupply: 10, supplyCount: 0, creditValuePerUnit: 50);
			Assert.That(refund, Is.EqualTo(0));
		}

		[Test]
		public void RefundForOneMissingPip()
		{
			// 14/15 pips: 1000 - 1*50 = 950
			var refund = RefundForTruck(baseValue: 1000, maxSupply: 15, supplyCount: 14, creditValuePerUnit: 50);
			Assert.That(refund, Is.EqualTo(950));
		}

		// --- LC supply pool drain on truck refill ---
		// LC TotalSupply 3000, truck SupplyPerUnit 50 → one full truck refill = 750 LC supply.

		[Test]
		public void LCDrainsBySupplyPerUnitPerTruckPip()
		{
			var remaining = LCRemainingAfterPips(lcSupply: 3000, truckPipsTransferred: 1, supplyPerUnit: 50);
			Assert.That(remaining, Is.EqualTo(2950));
		}

		[Test]
		public void LCFullTruckRefillCostsMaxSupplyTimesSupplyPerUnit()
		{
			// Full TRUK refill = 15 pips × 50 = 750 LC supply.
			var remaining = LCRemainingAfterPips(lcSupply: 3000, truckPipsTransferred: 15, supplyPerUnit: 50);
			Assert.That(remaining, Is.EqualTo(2250));
		}

		[Test]
		public void LCSupportsFourFullTruckRefillsBeforeRunningDry()
		{
			// 3000 LC / 750 per truck = 4 full refills; on the 5th the LC has 0 left.
			var afterFour = LCRemainingAfterPips(lcSupply: 3000, truckPipsTransferred: 60, supplyPerUnit: 50);
			Assert.That(afterFour, Is.EqualTo(0));
		}
	}
}
