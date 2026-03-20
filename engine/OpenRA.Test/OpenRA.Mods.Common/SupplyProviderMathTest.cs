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
	/// Tests the pure math behind SupplyProvider's distance-based delay calculation.
	/// The actual SupplyProvider trait requires a full World/Actor setup, so we test
	/// the formula in isolation.
	/// </summary>
	[TestFixture]
	public class SupplyProviderMathTest
	{
		// Mirrors SupplyProvider.CalculateDelay() logic
		static int CalculateDelay(int distance, int minRange, int baseDelay, int maxDelayMultiplier)
		{
			if (minRange <= 0)
				minRange = 1;

			var multiplier = (float)distance / minRange;
			if (multiplier < 1f) multiplier = 1f;
			if (multiplier > maxDelayMultiplier) multiplier = maxDelayMultiplier;

			return (int)(baseDelay * multiplier);
		}

		// Mirrors SupplyProvider supply deduction logic
		static int CalculateSupplyAfterRearm(int currentSupply, int ammoToGive, int supplyValuePerAmmo)
		{
			return currentSupply - (ammoToGive * supplyValuePerAmmo);
		}

		// --- Distance-based delay ---

		[Test]
		public void DelayAtMinRangeIsBaseDelay()
		{
			// At minRange distance, multiplier = 1.0, so delay = baseDelay
			var delay = CalculateDelay(distance: 1024, minRange: 1024, baseDelay: 15, maxDelayMultiplier: 4);
			Assert.That(delay, Is.EqualTo(15));
		}

		[Test]
		public void DelayCloserThanMinRangeIsClamped()
		{
			// Closer than minRange still uses multiplier 1.0 (clamped)
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
			// Very far away: multiplier capped at maxDelayMultiplier
			var delay = CalculateDelay(distance: 100000, minRange: 1024, baseDelay: 15, maxDelayMultiplier: 4);
			Assert.That(delay, Is.EqualTo(60)); // 15 * 4
		}

		[Test]
		public void DelayScalesLinearly()
		{
			// At 3x minRange: delay = 15 * 3 = 45
			var delay = CalculateDelay(distance: 3072, minRange: 1024, baseDelay: 15, maxDelayMultiplier: 4);
			Assert.That(delay, Is.EqualTo(45));
		}

		[Test]
		public void DelayWithZeroMinRangeDoesNotDivideByZero()
		{
			// minRange = 0 should be treated as 1 to avoid division by zero
			var delay = CalculateDelay(distance: 1024, minRange: 0, baseDelay: 15, maxDelayMultiplier: 4);
			// distance/1 = 1024, capped at 4 → 15 * 4 = 60
			Assert.That(delay, Is.EqualTo(60));
		}

		// --- Supply deduction ---

		[Test]
		public void SupplyDeductedBySupplyValue()
		{
			// Give 1 ammo with SupplyValue=5, costs 5 supply
			var remaining = CalculateSupplyAfterRearm(currentSupply: 500, ammoToGive: 1, supplyValuePerAmmo: 5);
			Assert.That(remaining, Is.EqualTo(495));
		}

		[Test]
		public void ExpensiveAmmoDepletesSupplyFaster()
		{
			// AT missiles cost 10 supply each, MG rounds cost 1
			var supplyAfterMissile = CalculateSupplyAfterRearm(500, 1, 10);
			var supplyAfterMG = CalculateSupplyAfterRearm(500, 1, 1);
			Assert.That(supplyAfterMissile, Is.EqualTo(490));
			Assert.That(supplyAfterMG, Is.EqualTo(499));
		}

		[Test]
		public void SupplyCanGoNegative()
		{
			// If we don't check before deducting, supply goes negative
			// (The real code checks currentSupply < supplyNeeded before calling)
			var remaining = CalculateSupplyAfterRearm(3, 1, 5);
			Assert.That(remaining, Is.EqualTo(-2));
		}

		// --- Selection bar ---

		[Test]
		public void SelectionBarValue()
		{
			// Mirrors ISelectionBar.GetValue: currentSupply / totalSupply
			var totalSupply = 500;

			Assert.That((float)500 / totalSupply, Is.EqualTo(1.0f));
			Assert.That((float)250 / totalSupply, Is.EqualTo(0.5f));
			Assert.That((float)0 / totalSupply, Is.EqualTo(0.0f));
			Assert.That((float)100 / totalSupply, Is.EqualTo(0.2f));
		}
	}
}
