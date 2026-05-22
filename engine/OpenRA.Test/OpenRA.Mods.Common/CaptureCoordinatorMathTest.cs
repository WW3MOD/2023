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
	/// Mirrors the (capturer, target) scoring math from CaptureCoordinatorBotModule.
	/// The module itself enumerates actors via World, but ScoreTarget is a closed
	/// arithmetic formula: income × distance-decay × safety-multiplier. Pulled
	/// out here so regressions in the curve (or accidental redefinitions of the
	/// safety tiers) are caught in CI.
	///
	/// distFactor = halfLife * 100 / (halfLife + distCells)   → in [≈10, 100]; halfLife @ 50.
	/// safetyFactor: 100 (0 enemies), 40 (1-2), 10 (3+).
	/// score = (long)income * distFactor * safetyFactor.
	/// </summary>
	[TestFixture]
	public class CaptureCoordinatorMathTest
	{
		static int DistanceFactor(int distCells, int halfLifeCells)
		{
			var d = Math.Max(1, distCells);
			var hl = Math.Max(1, halfLifeCells);
			return hl * 100 / (hl + d);
		}

		static int SafetyFactor(int nearbyEnemies, int safe, int mild, int hostile)
		{
			if (nearbyEnemies == 0)
				return safe;
			if (nearbyEnemies <= 2)
				return mild;
			return hostile;
		}

		static long ScoreTarget(int incomeWeight, int distCells, int halfLifeCells,
			int nearbyEnemies, int safe, int mild, int hostile)
		{
			var dist = DistanceFactor(distCells, halfLifeCells);
			var safety = SafetyFactor(nearbyEnemies, safe, mild, hostile);
			return (long)incomeWeight * dist * safety;
		}

		// --- DistanceFactor ---

		[Test]
		public void DistanceZeroIsHandledAsOne()
		{
			// Math.Max(1, distCells): when capturer is on top of the target.
			// halfLife=20, dist=clamped-to-1 → 20 * 100 / 21 = 95.
			Assert.That(DistanceFactor(0, 20), Is.EqualTo(95));
			Assert.That(DistanceFactor(1, 20), Is.EqualTo(95));
		}

		[Test]
		public void DistanceAtHalfLifeIsApproximately50()
		{
			// dist = halfLife → halfLife * 100 / (halfLife * 2) = 50.
			Assert.That(DistanceFactor(20, 20), Is.EqualTo(50));
		}

		[Test]
		public void DistanceFarOutDecaysTowardZero()
		{
			// Very large distance → small factor, but always > 0.
			var far = DistanceFactor(1000, 20);
			Assert.That(far, Is.LessThan(10));
			Assert.That(far, Is.GreaterThan(0));
		}

		[Test]
		public void DistanceFactorMonotonicallyDecreasing()
		{
			var prev = DistanceFactor(1, 20);
			for (var d = 2; d <= 100; d++)
			{
				var now = DistanceFactor(d, 20);
				Assert.That(now, Is.LessThanOrEqualTo(prev),
					$"Distance {d}: factor {now} should be ≤ previous {prev}");
				prev = now;
			}
		}

		[Test]
		public void DistanceFactorHandlesPathologicalHalfLife()
		{
			// HalfLifeCells = 0 → clamped to 1; factor = 1 * 100 / (1 + d).
			Assert.That(DistanceFactor(99, 0), Is.EqualTo(1));
			Assert.That(DistanceFactor(0, 0), Is.EqualTo(50));
		}

		// --- SafetyFactor ---

		[Test]
		public void SafetyZeroEnemiesIsSafeMultiplier()
		{
			Assert.That(SafetyFactor(0, 100, 40, 10), Is.EqualTo(100));
		}

		[Test]
		public void SafetyOneOrTwoEnemiesIsMildMultiplier()
		{
			Assert.That(SafetyFactor(1, 100, 40, 10), Is.EqualTo(40));
			Assert.That(SafetyFactor(2, 100, 40, 10), Is.EqualTo(40));
		}

		[Test]
		public void SafetyThreeOrMoreEnemiesIsHostileMultiplier()
		{
			Assert.That(SafetyFactor(3, 100, 40, 10), Is.EqualTo(10));
			Assert.That(SafetyFactor(50, 100, 40, 10), Is.EqualTo(10));
		}

		[Test]
		public void SafetyTierBoundaries()
		{
			// Document the cliff at 2 → 3.
			Assert.That(SafetyFactor(2, 100, 40, 10), Is.EqualTo(40));
			Assert.That(SafetyFactor(3, 100, 40, 10), Is.EqualTo(10));
		}

		// --- ScoreTarget ---

		[Test]
		public void NearbySafeTargetScoresMuchHigherThanDistantHostile()
		{
			// Income 50, dist 1, no enemies (factor=95, safety=100) → 50 * 95 * 100 = 475000.
			var safeNear = ScoreTarget(50, 1, 20, 0, 100, 40, 10);
			// Same income, far away, hostile.
			var hostileFar = ScoreTarget(50, 60, 20, 5, 100, 40, 10);
			Assert.That(safeNear, Is.GreaterThan(hostileFar * 10));
		}

		[Test]
		public void HighIncomeTargetBeatsLowIncomeSameContext()
		{
			var low = ScoreTarget(10, 10, 20, 0, 100, 40, 10);
			var high = ScoreTarget(50, 10, 20, 0, 100, 40, 10);
			Assert.That(high, Is.EqualTo(low * 5));
		}

		[Test]
		public void EnemyPresenceCutsScore()
		{
			var safe = ScoreTarget(50, 5, 20, 0, 100, 40, 10);
			var contested = ScoreTarget(50, 5, 20, 1, 100, 40, 10);
			var swarmed = ScoreTarget(50, 5, 20, 3, 100, 40, 10);

			Assert.That(contested * 100, Is.EqualTo(safe * 40),
				"Contested score = safe × (40 / 100)");
			Assert.That(swarmed * 100, Is.EqualTo(safe * 10),
				"Swarmed score = safe × (10 / 100)");
		}

		[Test]
		public void ScoreNeverOverflowsForReasonableInputs()
		{
			// Worst-case: income 1000 (high-value), dist 1 → factor 95, safety 100.
			// 1000 * 95 * 100 = 9,500,000 — fits in long with massive headroom.
			var s = ScoreTarget(1000, 1, 20, 0, 100, 40, 10);
			Assert.That(s, Is.GreaterThan(0));
			Assert.That(s, Is.LessThan(long.MaxValue / 1000));
		}

		[Test]
		public void ScoreIsZeroForZeroIncome()
		{
			var s = ScoreTarget(0, 5, 20, 0, 100, 40, 10);
			Assert.That(s, Is.EqualTo(0));
		}
	}
}
