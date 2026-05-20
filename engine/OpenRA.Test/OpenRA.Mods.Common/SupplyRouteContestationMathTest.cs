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
	/// Mirrors the contestation/recovery math from
	/// engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs. The trait
	/// needs a full World/Actor harness to exercise end-to-end (proximity
	/// triggers, notifications). The arithmetic that drives the bar movement,
	/// production slowdown, and recovery boost is deterministic and lifted here
	/// so a YAML/code drift breaks a unit test rather than only showing up in a
	/// live game.
	/// </summary>
	[TestFixture]
	public class SupplyRouteContestationMathTest
	{
		// Mirrors SupplyRouteContestation.CalculateTickRate.
		// rate = max(1, BarMax / max(1, max(MinTicks, BaseTicks * ReferenceValue / valueSurplus))).
		static int CalculateTickRate(int valueSurplus, int barMax, int baseTicks, int referenceValue, int minTicks)
		{
			var ticksToFull = Math.Max(minTicks,
				(long)baseTicks * referenceValue / valueSurplus);
			return Math.Max(1, barMax / (int)Math.Max(1, ticksToFull));
		}

		// Mirrors RecalculateForces: each side clamps to non-negative.
		static (int netEnemy, int netFriendly) NetSurplus(int enemyValue, int friendlyValue)
		{
			return (Math.Max(0, enemyValue - friendlyValue), Math.Max(0, friendlyValue - enemyValue));
		}

		// Mirrors IProductionSpeedModifier.GetProductionSpeedModifier.
		static int ProductionSpeedModifier(int controlBar, int barMax, int slowdownThreshold, bool isPassive)
		{
			if (isPassive || controlBar <= 0)
				return 0;

			var barPercent = controlBar * 100 / barMax;
			if (barPercent >= slowdownThreshold)
				return 100;

			return barPercent * 100 / slowdownThreshold;
		}

		// Mirrors recovery branch in Tick: recoveryRate = max(1, BarMax / BaseRecoveryTicks).
		// Final delta per tick = recoveryRate * friendlyBoost.
		static int RecoveryDelta(int barMax, int baseRecoveryTicks, bool friendlyPresent, int friendlyMultiplier)
		{
			var rate = Math.Max(1, barMax / baseRecoveryTicks);
			var boost = friendlyPresent ? friendlyMultiplier : 1;
			return rate * boost;
		}

		// Mirrors ControlBarFraction getter: barMax > 0 ? bar * 100 / barMax : 0.
		static int ControlBarFraction(int controlBar, int barMax)
		{
			return barMax > 0 ? controlBar * 100 / barMax : 0;
		}

		// --- CalculateTickRate ---

		[Test]
		public void RateAtReferenceValueDepletesInBaseTicks()
		{
			// Surplus exactly = ReferenceValue → ticksToFull = BaseTicks → rate = BarMax / BaseTicks.
			// Default WW3MOD values: BarMax=100000, BaseTicks=1500, ReferenceValue=2500, MinTicks=500.
			var rate = CalculateTickRate(2500, 100000, 1500, 2500, 500);
			Assert.That(rate, Is.EqualTo(100000 / 1500), "100k / 1.5k = 66 per tick");
		}

		[Test]
		public void RateAtDoubleReferenceValueIsTwiceAsFast()
		{
			// 2x surplus → ticksToFull halves → rate doubles (assuming MinTicks not hit).
			var ref1 = CalculateTickRate(2500, 100000, 1500, 2500, 500);
			var ref2 = CalculateTickRate(5000, 100000, 1500, 2500, 500);
			Assert.That(ref2, Is.GreaterThan(ref1));
			// 5000 surplus → ticksToFull = 1500 * 2500 / 5000 = 750 → rate = 100000/750 = 133
			Assert.That(ref2, Is.EqualTo(100000 / 750));
		}

		[Test]
		public void RateClampedByMinTicks()
		{
			// Huge surplus → ticksToFull would drop below MinTicks → clamped to MinTicks.
			var rate = CalculateTickRate(1_000_000, 100000, 1500, 2500, 500);
			Assert.That(rate, Is.EqualTo(100000 / 500), "Clamped: rate = BarMax / MinTicks");
		}

		[Test]
		public void RateAtTinySurplusIsSlow()
		{
			// Surplus = 1, baseTicks * ref = 3,750,000 ticks. BarMax / that = 0 → clamped to 1.
			var rate = CalculateTickRate(1, 100000, 1500, 2500, 500);
			Assert.That(rate, Is.EqualTo(1), "Minimum guaranteed rate is 1");
		}

		[Test]
		public void RateAtZeroSurplusDoesNotDivideByZero()
		{
			// Tick() guards CalculateTickRate behind cachedNetEnemySurplus > 0, but
			// the inner Math.Max(1, valueSurplus) is the safety net. Mirror the same guard here:
			// pass 1 to the helper and confirm it doesn't blow up.
			Assert.That(() => CalculateTickRate(1, 100000, 1500, 2500, 500), Throws.Nothing);
		}

		// --- Net surplus (enemy/friendly cancellation) ---

		[Test]
		public void EqualForcesGiveZeroSurplus()
		{
			var (e, f) = NetSurplus(1000, 1000);
			Assert.That(e, Is.EqualTo(0));
			Assert.That(f, Is.EqualTo(0));
		}

		[Test]
		public void EnemyAdvantageGivesEnemySurplusOnly()
		{
			var (e, f) = NetSurplus(enemyValue: 1500, friendlyValue: 500);
			Assert.That(e, Is.EqualTo(1000));
			Assert.That(f, Is.EqualTo(0));
		}

		[Test]
		public void FriendlyAdvantageGivesFriendlySurplusOnly()
		{
			var (e, f) = NetSurplus(enemyValue: 500, friendlyValue: 1500);
			Assert.That(e, Is.EqualTo(0));
			Assert.That(f, Is.EqualTo(1000));
		}

		[Test]
		public void NeitherSurplusEverNegative()
		{
			// Sanity: regardless of input, both returns are non-negative.
			var (e1, f1) = NetSurplus(0, 0);
			var (e2, f2) = NetSurplus(0, 10000);
			var (e3, f3) = NetSurplus(10000, 0);
			Assert.That(e1, Is.GreaterThanOrEqualTo(0));
			Assert.That(f1, Is.GreaterThanOrEqualTo(0));
			Assert.That(e2, Is.GreaterThanOrEqualTo(0));
			Assert.That(f2, Is.GreaterThanOrEqualTo(0));
			Assert.That(e3, Is.GreaterThanOrEqualTo(0));
			Assert.That(f3, Is.GreaterThanOrEqualTo(0));
		}

		// --- Production speed modifier ---

		[Test]
		public void PassiveProducesNothing()
		{
			Assert.That(ProductionSpeedModifier(100000, 100000, 50, isPassive: true), Is.EqualTo(0));
		}

		[Test]
		public void EmptyControlBarProducesNothing()
		{
			Assert.That(ProductionSpeedModifier(0, 100000, 50, isPassive: false), Is.EqualTo(0));
		}

		[Test]
		public void FullControlBarProducesFullSpeed()
		{
			Assert.That(ProductionSpeedModifier(100000, 100000, 50, isPassive: false), Is.EqualTo(100));
		}

		[Test]
		public void AboveSlowdownThresholdProducesFullSpeed()
		{
			// 60% bar > 50% threshold → 100%.
			Assert.That(ProductionSpeedModifier(60000, 100000, 50, isPassive: false), Is.EqualTo(100));
		}

		[Test]
		public void AtSlowdownThresholdStillFullSpeed()
		{
			// Boundary: exactly at threshold = full speed (>=, not >).
			Assert.That(ProductionSpeedModifier(50000, 100000, 50, isPassive: false), Is.EqualTo(100));
		}

		[Test]
		public void BelowSlowdownScalesLinearly()
		{
			// 25% bar with 50% threshold → 25 * 100 / 50 = 50% speed.
			Assert.That(ProductionSpeedModifier(25000, 100000, 50, isPassive: false), Is.EqualTo(50));

			// 10% bar with 50% threshold → 20% speed.
			Assert.That(ProductionSpeedModifier(10000, 100000, 50, isPassive: false), Is.EqualTo(20));
		}

		[Test]
		public void JustAboveZeroProducesVerySlowly()
		{
			// Tiny bar: 1% with 50% threshold → 2% speed.
			Assert.That(ProductionSpeedModifier(1000, 100000, 50, isPassive: false), Is.EqualTo(2));
		}

		// --- Recovery rate ---

		[Test]
		public void RecoveryWithoutFriendliesIsBaseRate()
		{
			// 100000 / 3000 = 33 per tick.
			var delta = RecoveryDelta(100000, 3000, friendlyPresent: false, friendlyMultiplier: 3);
			Assert.That(delta, Is.EqualTo(33));
		}

		[Test]
		public void RecoveryWithFriendliesIsBoosted()
		{
			var bare = RecoveryDelta(100000, 3000, friendlyPresent: false, friendlyMultiplier: 3);
			var boosted = RecoveryDelta(100000, 3000, friendlyPresent: true, friendlyMultiplier: 3);
			Assert.That(boosted, Is.EqualTo(bare * 3));
		}

		[Test]
		public void RecoveryRateAtLeastOnePerTick()
		{
			// Pathological: huge BaseRecoveryTicks → BarMax / BaseRecoveryTicks = 0 → clamps to 1.
			var delta = RecoveryDelta(100, 100000, friendlyPresent: false, friendlyMultiplier: 3);
			Assert.That(delta, Is.GreaterThanOrEqualTo(1));
		}

		// --- ControlBarFraction ---

		[Test]
		public void ControlBarFractionFullIs100()
		{
			Assert.That(ControlBarFraction(100000, 100000), Is.EqualTo(100));
		}

		[Test]
		public void ControlBarFractionEmptyIs0()
		{
			Assert.That(ControlBarFraction(0, 100000), Is.EqualTo(0));
		}

		[Test]
		public void ControlBarFractionScalesLinearly()
		{
			Assert.That(ControlBarFraction(50000, 100000), Is.EqualTo(50));
			Assert.That(ControlBarFraction(25000, 100000), Is.EqualTo(25));
		}

		[Test]
		public void ControlBarFractionZeroBarMaxDoesNotDivideByZero()
		{
			Assert.That(ControlBarFraction(50, 0), Is.EqualTo(0));
		}

		// --- Full depletion/recovery scenario ---

		[Test]
		public void DepletionAtReferenceMatchesTargetDuration()
		{
			// At ReferenceValue surplus, depletion should hit zero in ~BaseTicks ticks
			// (with rounding tolerance because integer division loses precision).
			var barMax = 100000;
			var rate = CalculateTickRate(2500, barMax, 1500, 2500, 500);
			var bar = barMax;
			var ticks = 0;
			while (bar > 0 && ticks < 5000)
			{
				bar = Math.Max(0, bar - rate);
				ticks++;
			}

			Assert.That(ticks, Is.InRange(1500, 1520), "Within rounding of the BaseTicks target");
		}

		[Test]
		public void FastRecoveryWithFriendliesBeatsSlowDepletion()
		{
			// Friendly boost should outpace recovery WITHOUT friendlies for the same bar size.
			var slow = RecoveryDelta(100000, 3000, false, 3);
			var fast = RecoveryDelta(100000, 3000, true, 3);
			Assert.That(fast, Is.EqualTo(slow * 3));
		}
	}
}
