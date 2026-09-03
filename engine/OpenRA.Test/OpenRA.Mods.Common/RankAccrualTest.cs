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
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class RankAccrualTest
	{
		const int Rank1Mult = 500;      // rank 1 every 5 build times
		const int HigherMult = 300;     // each tier up is 3x the previous
		static readonly int[] Caps = { 3, 2, 1 };

		static UnitRankStock Stock(int buildTimeTicks)
		{
			return new UnitRankStock(buildTimeTicks, Rank1Mult, HigherMult, Caps);
		}

		/// <summary>Run a stock forward to an absolute tick.</summary>
		static void RunTo(UnitRankStock stock, int tick)
		{
			for (var t = 1; t <= tick; t++)
				stock.Advance(t);
		}

		#region Interval derived from cost

		[TestCase(200, 20)]        // Team Leader
		[TestCase(600, 60)]        // BTR
		[TestCase(1500, 150)]      // Grad
		[TestCase(2400, 240)]      // T-90
		[TestCase(6000, 600)]      // Iskander
		public void BuildTimeIsCostOverTen(int cost, int expectedTicks)
		{
			Assert.That(RankAccrual.BaseBuildTimeTicks(cost, -1, 100), Is.EqualTo(expectedTicks));
		}

		[Test]
		public void ExplicitBuildDurationBeatsCost()
		{
			// BuildDuration != -1 wins outright; cost is not consulted.
			Assert.That(RankAccrual.BaseBuildTimeTicks(6000, 42, 100), Is.EqualTo(42));
		}

		[Test]
		public void BuildDurationModifierIsApplied()
		{
			// msar is the one shipped actor that deviates, at BuildDurationModifier: 50.
			Assert.That(RankAccrual.BaseBuildTimeTicks(2400, -1, 50), Is.EqualTo(120));
		}

		[Test]
		public void BuildTimeIsNeverZero()
		{
			// A free or near-free actor must not produce a zero interval, which would grant every tick.
			Assert.That(RankAccrual.BaseBuildTimeTicks(0, -1, 100), Is.EqualTo(1));
			Assert.That(RankAccrual.BaseBuildTimeTicks(5, -1, 100), Is.EqualTo(1));
		}

		[Test]
		public void Rank1IntervalIsFiveBuildTimes()
		{
			Assert.That(RankAccrual.IntervalTicks(240, 1, Rank1Mult, HigherMult), Is.EqualTo(1200));
		}

		[Test]
		public void HigherTiersAreThreeTimesRarerEachStep()
		{
			Assert.That(RankAccrual.IntervalTicks(240, 2, Rank1Mult, HigherMult), Is.EqualTo(3600));
			Assert.That(RankAccrual.IntervalTicks(240, 3, Rank1Mult, HigherMult), Is.EqualTo(10800));
		}

		[Test]
		public void IntervalIsLinearInCost()
		{
			// The user's requirement: spending an equal budget across the roster must accrue an equal
			// amount of free rank. A unit costing 30x as much must wait exactly 30x as long.
			var cheap = RankAccrual.IntervalTicks(RankAccrual.BaseBuildTimeTicks(200, -1, 100), 1, Rank1Mult, HigherMult);
			var dear = RankAccrual.IntervalTicks(RankAccrual.BaseBuildTimeTicks(6000, -1, 100), 1, Rank1Mult, HigherMult);
			Assert.That(dear, Is.EqualTo(cheap * 30));
		}

		[Test]
		public void IntervalIsNeverZero()
		{
			Assert.That(RankAccrual.IntervalTicks(1, 1, 0, 0), Is.EqualTo(1));
		}

		[TestCase(0)]
		[TestCase(4)]
		public void IntervalRejectsTiersOutsideOneToThree(int tier)
		{
			Assert.Throws<ArgumentOutOfRangeException>(
				() => RankAccrual.IntervalTicks(240, tier, Rank1Mult, HigherMult));
		}

		#endregion

		#region Accrual and the cap per tier

		[Test]
		public void NothingIsHeldBeforeTheFirstInterval()
		{
			var s = Stock(240);
			RunTo(s, 1199);
			Assert.That(s.Stock[0], Is.EqualTo(0));
		}

		[Test]
		public void FirstGrantLandsExactlyOnTheInterval()
		{
			var s = Stock(240);
			RunTo(s, 1200);
			Assert.That(s.Stock[0], Is.EqualTo(1));
		}

		[Test]
		public void StockAccumulatesOneGrantPerInterval()
		{
			var s = Stock(240);
			RunTo(s, 2400);
			Assert.That(s.Stock[0], Is.EqualTo(2));
		}

		[Test]
		public void StockStopsAtTheCap()
		{
			var s = Stock(240);
			RunTo(s, 1200 * 3);
			Assert.That(s.Stock[0], Is.EqualTo(3), "rank-1 cap is 3");
		}

		[Test]
		public void TimerFiringAtCapIsWastedNotBanked()
		{
			var s = Stock(240);

			// Ten intervals' worth of grants against a cap of three.
			RunTo(s, 1200 * 10);
			Assert.That(s.Stock[0], Is.EqualTo(3));

			// Spending one must leave room for exactly one more, not for the seven that were discarded.
			s.Spend(1);
			Assert.That(s.Stock[0], Is.EqualTo(2));

			RunTo(s, 1200 * 11);
			Assert.That(s.Stock[0], Is.EqualTo(3));
		}

		[Test]
		public void TiersFillInParallelAndIndependently()
		{
			// A rank-2 grant does not consume rank-1 stock: these are separate timers, not a merge.
			var s = Stock(240);
			RunTo(s, 3600);

			Assert.That(s.Stock[0], Is.EqualTo(3), "rank-1 at cap after three of its own intervals");
			Assert.That(s.Stock[1], Is.EqualTo(1), "rank-2 has fired once");
			Assert.That(s.Stock[2], Is.EqualTo(0), "rank-3 has not fired yet");
		}

		[Test]
		public void EachTierRespectsItsOwnCap()
		{
			var s = Stock(240);
			RunTo(s, 10800 * 6);
			Assert.That(s.Stock[0], Is.EqualTo(3));
			Assert.That(s.Stock[1], Is.EqualTo(2));
			Assert.That(s.Stock[2], Is.EqualTo(1));
		}

		#endregion

		#region The rank-3 ceiling

		[Test]
		public void NoTierAboveThreeExists()
		{
			Assert.That(RankAccrual.MaxPurchasableRank, Is.EqualTo(3));

			// GainsExperience allows four levels; purchase must never reach the fourth.
			var s = Stock(240);
			RunTo(s, 10800 * 50);
			Assert.That(s.Stock.Length, Is.EqualTo(3));
			Assert.That(s.Peek(), Is.EqualTo(3), "a fully stocked type still only ever offers rank 3");
		}

		#endregion

		#region Highest-first spend

		[Test]
		public void PeekReturnsZeroOnAnEmptyStock()
		{
			Assert.That(Stock(240).Peek(), Is.EqualTo(0));
		}

		[Test]
		public void PeekReturnsTheHighestHeldTier()
		{
			Assert.That(RankAccrual.HighestHeldTier(new[] { 3, 0, 0 }), Is.EqualTo(1));
			Assert.That(RankAccrual.HighestHeldTier(new[] { 3, 1, 0 }), Is.EqualTo(2));
			Assert.That(RankAccrual.HighestHeldTier(new[] { 0, 0, 1 }), Is.EqualTo(3));
			Assert.That(RankAccrual.HighestHeldTier(new[] { 0, 0, 0 }), Is.EqualTo(0));
		}

		[Test]
		public void SpendingTakesTheHighestAndLeavesTheRest()
		{
			var s = Stock(240);
			RunTo(s, 10800);

			Assert.That(s.Stock[0], Is.EqualTo(3));
			Assert.That(s.Stock[1], Is.EqualTo(2));
			Assert.That(s.Stock[2], Is.EqualTo(1));

			// Buy one: the rank-3 goes, both lower stocks are untouched.
			var tier = s.Peek();
			Assert.That(tier, Is.EqualTo(3));
			s.Spend(tier);
			Assert.That(s.Stock, Is.EqualTo(new[] { 3, 2, 0 }));

			// Buy again: now the highest held is rank 2.
			tier = s.Peek();
			Assert.That(tier, Is.EqualTo(2));
			s.Spend(tier);
			Assert.That(s.Stock, Is.EqualTo(new[] { 3, 1, 0 }));
		}

		[Test]
		public void SpendingFromAnEmptyStockIsHarmless()
		{
			var s = Stock(240);
			Assert.That(s.Peek(), Is.EqualTo(0));

			s.Spend(0);
			s.Spend(1);
			s.Spend(2);
			s.Spend(3);

			Assert.That(s.Stock, Is.EqualTo(new[] { 0, 0, 0 }), "no stock may go negative");
		}

		[Test]
		public void SpendingAnOutOfRangeTierIsIgnored()
		{
			var s = Stock(240);
			RunTo(s, 1200);

			s.Spend(-1);
			s.Spend(4);
			s.Spend(99);

			Assert.That(s.Stock[0], Is.EqualTo(1), "the held rank-1 must survive a nonsense spend");
		}

		#endregion

		#region Buying never resets the timer

		[Test]
		public void PurchaseDoesNotDelayTheNextGrant()
		{
			// The user was explicit: buying spends stock, it does not touch the clock.
			var bought = Stock(240);
			var untouched = Stock(240);

			// Take the rank-1 the instant it lands, then run both to the second interval.
			RunTo(bought, 1200);
			bought.Spend(1);
			for (var t = 1201; t <= 2400; t++)
				bought.Advance(t);

			RunTo(untouched, 2400);

			// The buyer spent one and re-earned one; the abstainer simply holds two. Both are on the
			// same schedule, which is what "the timer never resets" means.
			Assert.That(bought.Stock[0], Is.EqualTo(1));
			Assert.That(untouched.Stock[0], Is.EqualTo(2));
		}

		#endregion

		#region Determinism

		[Test]
		public void AdvancingTickByTickMatchesASingleJump()
		{
			// The accrual is a pure function of elapsed ticks, so a client that ticks it 10800 times
			// and one that catches up in a single call must land on identical state. Guards the
			// catch-up loop in RankAccrual.Advance.
			var stepped = Stock(240);
			RunTo(stepped, 10800);

			var jumped = Stock(240);
			jumped.Advance(10800);

			Assert.That(jumped.Stock, Is.EqualTo(stepped.Stock));
		}

		[Test]
		public void ScheduleIsSeededFromTickZeroNotFromConstruction()
		{
			// Two stocks built at different moments must agree once advanced to the same absolute
			// tick. If the seed came from a construction timestamp instead, these would diverge.
			var early = Stock(240);
			var late = Stock(240);

			RunTo(early, 5000);
			late.Advance(5000);

			Assert.That(late.Stock, Is.EqualTo(early.Stock));
		}

		#endregion
	}
}
