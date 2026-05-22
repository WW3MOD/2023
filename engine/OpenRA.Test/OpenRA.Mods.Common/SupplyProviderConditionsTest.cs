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
	/// Mirrors the supply-ratio condition gating and weighted-need formula
	/// from SupplyProvider.cs. SupplyProvider needs a World/Actor to drive
	/// GrantCondition / RevokeCondition transitions end-to-end, but the
	/// decision math — ratio thresholds at 33% / 66%, weighted-need across
	/// multiple AmmoPools — is pure and reproduced here.
	/// </summary>
	[TestFixture]
	public class SupplyProviderConditionsTest
	{
		enum Tier { High, Medium, Low, Empty }

		// Mirrors UpdateSupplyConditions tier resolution.
		// ratio > 0.66 → High; 0.33 < ratio ≤ 0.66 → Medium; ratio ≤ 0.33 → Low;
		// currentSupply ≤ 0 is treated as Empty separately.
		// SupplyAnyCondition: granted iff currentSupply > 0.
		static Tier ResolveTier(int currentSupply, int totalSupply)
		{
			if (currentSupply <= 0)
				return Tier.Empty;

			var ratio = (float)currentSupply / totalSupply;
			if (ratio > 0.66f)
				return Tier.High;
			if (ratio > 0.33f)
				return Tier.Medium;
			return Tier.Low;
		}

		static bool HasAnySupply(int currentSupply)
		{
			return currentSupply > 0;
		}

		// Mirrors CalculateNeed: total missing × SupplyValue weight / total capacity × SupplyValue weight.
		// Pools: array of (max, current, supplyValue).
		static float CalculateNeed((int max, int current, int weight)[] pools)
		{
			var totalMissing = 0f;
			var totalCapacity = 0f;
			foreach (var p in pools)
			{
				totalMissing += (p.max - p.current) * p.weight;
				totalCapacity += p.max * p.weight;
			}

			if (totalCapacity <= 0)
				return 0f;

			return totalMissing / totalCapacity;
		}

		// Mirrors SetSupply clamp [0, TotalSupply]. SetSupply never goes negative or exceeds total.
		static int SetSupplyClamp(int amount, int totalSupply)
		{
			if (amount < 0)
				return 0;
			if (amount > totalSupply)
				return totalSupply;
			return amount;
		}

		// --- Tier resolution ---

		[Test]
		public void FullSupplyIsHighTier()
		{
			Assert.That(ResolveTier(500, 500), Is.EqualTo(Tier.High));
		}

		[Test]
		public void TwoThirdsBoundaryIsHigh()
		{
			// >0.66 = high. 67% → high; exactly 66% → medium.
			Assert.That(ResolveTier(340, 500), Is.EqualTo(Tier.High), "68% > 0.66 → high");
			Assert.That(ResolveTier(330, 500), Is.EqualTo(Tier.Medium), "66% not > 0.66 → medium");
		}

		[Test]
		public void OneThirdBoundaryIsMedium()
		{
			// 0.33 < ratio ≤ 0.66 = medium. >33% → medium; 33% exact → low.
			Assert.That(ResolveTier(170, 500), Is.EqualTo(Tier.Medium), "34% > 0.33 → medium");
			Assert.That(ResolveTier(165, 500), Is.EqualTo(Tier.Low), "33% not > 0.33 → low");
		}

		[Test]
		public void LowTierBelowOneThird()
		{
			Assert.That(ResolveTier(10, 500), Is.EqualTo(Tier.Low));
			Assert.That(ResolveTier(1, 500), Is.EqualTo(Tier.Low));
		}

		[Test]
		public void EmptySupplyIsEmptyTier()
		{
			Assert.That(ResolveTier(0, 500), Is.EqualTo(Tier.Empty));
		}

		[Test]
		public void NegativeSupplyIsEmptyTier()
		{
			// Defensive: shouldn't happen given SetSupplyClamp, but Empty either way.
			Assert.That(ResolveTier(-5, 500), Is.EqualTo(Tier.Empty));
		}

		[Test]
		public void HighMediumLowProgressionAcrossRange()
		{
			// Sanity sweep across 0..500.
			Assert.That(ResolveTier(500, 500), Is.EqualTo(Tier.High));
			Assert.That(ResolveTier(400, 500), Is.EqualTo(Tier.High));
			Assert.That(ResolveTier(350, 500), Is.EqualTo(Tier.High));
			Assert.That(ResolveTier(250, 500), Is.EqualTo(Tier.Medium));
			Assert.That(ResolveTier(200, 500), Is.EqualTo(Tier.Medium));
			Assert.That(ResolveTier(150, 500), Is.EqualTo(Tier.Low));
			Assert.That(ResolveTier(50, 500), Is.EqualTo(Tier.Low));
			Assert.That(ResolveTier(0, 500), Is.EqualTo(Tier.Empty));
		}

		// --- SupplyAnyCondition gating ---

		[Test]
		public void HasAnySupplyTrueForAnyPositive()
		{
			Assert.That(HasAnySupply(1), Is.True);
			Assert.That(HasAnySupply(500), Is.True);
		}

		[Test]
		public void HasAnySupplyFalseForZero()
		{
			Assert.That(HasAnySupply(0), Is.False);
		}

		// --- CalculateNeed (weighted by SupplyValue) ---

		[Test]
		public void FullPoolHasZeroNeed()
		{
			var need = CalculateNeed(new[] { (max: 100, current: 100, weight: 5) });
			Assert.That(need, Is.EqualTo(0f));
		}

		[Test]
		public void EmptyPoolHasFullNeed()
		{
			var need = CalculateNeed(new[] { (max: 100, current: 0, weight: 5) });
			Assert.That(need, Is.EqualTo(1f));
		}

		[Test]
		public void HalfPoolHasHalfNeed()
		{
			var need = CalculateNeed(new[] { (max: 100, current: 50, weight: 5) });
			Assert.That(need, Is.EqualTo(0.5f));
		}

		[Test]
		public void HighValuePoolDominatesNeed()
		{
			// Two pools — one big (autocannon: 900 ammo @ SV 5) full,
			// one small (missile: 8 ammo @ SV 75) empty.
			// totalMissing = 0*5 + 8*75 = 600.
			// totalCapacity = 900*5 + 8*75 = 4500 + 600 = 5100.
			// need = 600/5100 ≈ 0.1176.
			var need = CalculateNeed(new[]
			{
				(max: 900, current: 900, weight: 5),
				(max: 8, current: 0, weight: 75),
			});
			Assert.That(need, Is.EqualTo(0.1176f).Within(0.001f));
		}

		[Test]
		public void EmptyMissileVsEmptyAutocannonNeedsAreDifferent()
		{
			// Autocannon (large, low SV) and missile (small, high SV) have
			// the same percentage emptiness but very different absolute need scores.
			var autocannonEmpty = CalculateNeed(new[]
			{
				(max: 900, current: 0, weight: 5),
				(max: 8, current: 8, weight: 75),
			});
			var missileEmpty = CalculateNeed(new[]
			{
				(max: 900, current: 900, weight: 5),
				(max: 8, current: 0, weight: 75),
			});

			// missileEmpty < autocannonEmpty because 8*75 (= 600) is smaller than
			// 900*5 (= 4500), so the missile's share of total weighted capacity is smaller.
			Assert.That(missileEmpty, Is.LessThan(autocannonEmpty));
		}

		[Test]
		public void CalculateNeedHandlesEmptyPoolList()
		{
			var need = CalculateNeed(System.Array.Empty<(int, int, int)>());
			Assert.That(need, Is.EqualTo(0f));
		}

		[Test]
		public void CalculateNeedHandlesZeroWeightPools()
		{
			// All pools weight 0 → totalCapacity = 0 → safe-return 0.
			var need = CalculateNeed(new[]
			{
				(max: 100, current: 0, weight: 0),
				(max: 50, current: 0, weight: 0),
			});
			Assert.That(need, Is.EqualTo(0f));
		}

		// --- SetSupply clamping ---

		[Test]
		public void SetSupplyClampsNegativeToZero()
		{
			Assert.That(SetSupplyClamp(-50, 500), Is.EqualTo(0));
		}

		[Test]
		public void SetSupplyClampsExcessToTotal()
		{
			Assert.That(SetSupplyClamp(750, 500), Is.EqualTo(500));
		}

		[Test]
		public void SetSupplyPassesValidValuesThrough()
		{
			Assert.That(SetSupplyClamp(0, 500), Is.EqualTo(0));
			Assert.That(SetSupplyClamp(250, 500), Is.EqualTo(250));
			Assert.That(SetSupplyClamp(500, 500), Is.EqualTo(500));
		}
	}
}
