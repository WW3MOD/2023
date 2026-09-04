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
		static readonly int[] Caps = { 3, 2, 1 };

		/// <summary>
		/// A deliberately plain curve for the state-machine fixtures below: no base term and no
		/// ceiling, with the reference set to the build time they all use, so the cost term collapses
		/// to sqrt(240*240) = 240 and a rank-1 interval is a round 1200 ticks. Those fixtures are
		/// about caps, spending and recovery, not about tuning; keeping their arithmetic legible is
		/// worth more there than running them on the shipped numbers. The shipped numbers are pinned
		/// separately, against the trait's own defaults, in "The shipped curve" below.
		/// </summary>
		static readonly RankCurve TestCurve = new(0, 240, 500, 0, 300);

		/// <summary>Exactly what RankAccumulationInfo ships. Asserted to match in ShippedCurveIsTheTraitDefault.</summary>
		static readonly RankCurve ShippedCurve = new(2400, 100, 2700, 9000, 300);

		// Build times of the cheapest and dearest types that actually accrue, at cost / 10.
		const int ConscriptTicks = 5;      // E1, 50 credits
		const int IskanderTicks = 600;     // iskander / HELI / MIG and the rest of the 6000-credit tier

		static UnitRankStock Stock(int buildTimeTicks)
		{
			return new UnitRankStock(buildTimeTicks, TestCurve, Caps);
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
			// No accruing type currently sets it - MSAR, the one actor that does, is a support vehicle
			// without GainsExperience and so is never tracked - but the path is live for anything added.
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
		public void ReferenceBuildTimeCollapsesTheCostTermToItself()
		{
			// sqrt(240 * 240) == 240, so a unit built in exactly the reference time contributes the
			// reference before the multiplier. This is what makes TestCurve's arithmetic round.
			Assert.That(RankAccrual.IntervalTicks(240, 1, TestCurve), Is.EqualTo(1200));
		}

		[Test]
		public void HigherTiersAreThreeTimesRarerEachStep()
		{
			Assert.That(RankAccrual.IntervalTicks(240, 2, TestCurve), Is.EqualTo(3600));
			Assert.That(RankAccrual.IntervalTicks(240, 3, TestCurve), Is.EqualTo(10800));
		}

		[Test]
		public void IntervalIsNeverZero()
		{
			Assert.That(RankAccrual.IntervalTicks(1, 1, new RankCurve(0, 0, 0, 0, 0)), Is.EqualTo(1));
		}

		[TestCase(0)]
		[TestCase(4)]
		public void IntervalRejectsTiersOutsideOneToThree(int tier)
		{
			Assert.Throws<ArgumentOutOfRangeException>(
				() => RankAccrual.IntervalTicks(240, tier, TestCurve));
		}

		#endregion

		#region The curve compresses cost rather than tracking it

		[Test]
		public void CostStillOrdersTheRoster()
		{
			// Compression must not flatten the roster: a dearer unit always waits at least as long,
			// and the endpoints must be genuinely apart rather than clamped together.
			var previous = 0;
			for (var buildTicks = 1; buildTicks <= 1000; buildTicks++)
			{
				var interval = RankAccrual.Rank1IntervalTicks(buildTicks, ShippedCurve);
				Assert.That(interval, Is.GreaterThanOrEqualTo(previous), $"went backwards at T={buildTicks}");
				previous = interval;
			}

			Assert.That(RankAccrual.Rank1IntervalTicks(IskanderTicks, ShippedCurve),
				Is.GreaterThan(RankAccrual.Rank1IntervalTicks(ConscriptTicks, ShippedCurve) * 2),
				"the dearest unit must still be meaningfully rarer than the cheapest");
		}

		[Test]
		public void SpreadAcrossTheRosterStaysWithinThreeAndAHalfToOne()
		{
			// The property this retune exists for. Linear scaling spread the shipped roster 120:1 in
			// wall-clock, which is what let cheap infantry fill their bank in seconds. Cost may still
			// order the roster, but the ratio between its ends is bounded.
			var cheapest = RankAccrual.Rank1IntervalTicks(ConscriptTicks, ShippedCurve);
			var dearest = RankAccrual.Rank1IntervalTicks(IskanderTicks, ShippedCurve);

			Assert.That(dearest * 100 / cheapest, Is.LessThanOrEqualTo(350),
				$"cheapest {cheapest} ticks vs dearest {dearest} ticks");
		}

		[Test]
		public void TheCeilingBoundsTheSpreadForAnyCostAtAll()
		{
			// Stronger than the roster check, and the reason the ceiling exists: however expensive a
			// unit added later is, it can never wait more than MaxRank1Ticks. The floor is the T=1
			// interval, so this bounds the ratio for the whole input domain and not just for today's
			// roster.
			var floor = RankAccrual.Rank1IntervalTicks(1, ShippedCurve);

			foreach (var buildTicks in new[] { 1, 5, 600, 10_000, 1_000_000, int.MaxValue })
			{
				var interval = RankAccrual.Rank1IntervalTicks(buildTicks, ShippedCurve);
				Assert.That(interval, Is.LessThanOrEqualTo(ShippedCurve.MaxRank1Ticks), $"T={buildTicks}");
				Assert.That(interval * 100 / floor, Is.LessThanOrEqualTo(350), $"T={buildTicks}");
			}
		}

		[Test]
		public void TheCeilingAppliesBeforeTheTiersAreStepped()
		{
			// Clamping rank 1 and then stepping is what carries the ratio bound up to rank 3. Were the
			// clamp applied per tier instead, every tier would collapse onto the same ceiling.
			var capped = RankAccrual.IntervalTicks(int.MaxValue, 1, ShippedCurve);
			Assert.That(RankAccrual.IntervalTicks(int.MaxValue, 3, ShippedCurve), Is.EqualTo(capped * 9));
		}

		[Test]
		public void ZeroCeilingDisablesTheCeiling()
		{
			var uncapped = new RankCurve(2400, 100, 2700, 0, 300);
			Assert.That(RankAccrual.Rank1IntervalTicks(100_000, uncapped),
				Is.GreaterThan(ShippedCurve.MaxRank1Ticks));
		}

		#endregion

		#region The shipped curve

		[Test]
		public void ShippedCurveIsTheTraitDefault()
		{
			// Everything in this region grades the numbers players actually get. If a default moves in
			// RankAccumulationInfo without this fixture moving with it, this is what fails - so the
			// wall-clock figures below can never quietly come to describe a config nobody ships.
			var info = new RankAccumulationInfo();

			Assert.That(info.Rank1BaseIntervalTicks, Is.EqualTo(ShippedCurve.BaseTicks));
			Assert.That(info.CostReferenceBuildTicks, Is.EqualTo(ShippedCurve.ReferenceBuildTicks));
			Assert.That(info.Rank1IntervalMultiplier, Is.EqualTo(ShippedCurve.Rank1Multiplier));
			Assert.That(info.Rank1MaxIntervalTicks, Is.EqualTo(ShippedCurve.MaxRank1Ticks));
			Assert.That(info.HigherTierIntervalMultiplier, Is.EqualTo(ShippedCurve.HigherTierMultiplier));
			Assert.That(info.Caps, Is.EqualTo(Caps));
		}

		// Ticks, at the default 60ms timestep, so 16.67 ticks per second.
		// Conscript: 2994 = 2m59s, 8982 = 8m58s, 26946 = 26m56s.
		// Iskander:  8988 = 8m59s, 26964 = 26m57s, 80892 = 80m53s.
		[TestCase(ConscriptTicks, 1, 2994)]
		[TestCase(ConscriptTicks, 2, 8982)]
		[TestCase(ConscriptTicks, 3, 26946)]
		[TestCase(IskanderTicks, 1, 8988)]
		[TestCase(IskanderTicks, 2, 26964)]
		[TestCase(IskanderTicks, 3, 80892)]
		public void ShippedIntervalsAtTheEndsOfTheRoster(int buildTicks, int tier, int expected)
		{
			Assert.That(RankAccrual.IntervalTicks(buildTicks, tier, ShippedCurve), Is.EqualTo(expected));
		}

		[Test]
		public void TheCheapestUnitTakesMinutesNotSeconds()
		{
			// The user's headline complaint: a 50-credit Conscript used to bank a rank every 25 ticks,
			// a second and a half, and filled all three tiers inside fourteen seconds. The brief for
			// this retune asks for two to four minutes before the first one.
			var firstRank = RankAccrual.IntervalTicks(ConscriptTicks, 1, ShippedCurve);

			Assert.That(firstRank, Is.InRange(2000, 4000), "2000 ticks is 2m00s, 4000 is 4m00s");
		}

		[Test]
		public void RankThreeIsRareEvenForTheCheapestUnit()
		{
			// "A rank 3 should be very rare." Grading against a 20-minute match: the first accrued
			// rank-3 must land beyond it, so seeing one at all means a long game or a recovered crew.
			const int TwentyMinutes = 20 * 60 * 1000 / 60;

			Assert.That(RankAccrual.IntervalTicks(ConscriptTicks, 3, ShippedCurve),
				Is.GreaterThan(TwentyMinutes));
		}

		[Test]
		public void EveryTierIsSlowerThanItWasBeforeTheRetune()
		{
			// No unit anywhere on the roster may accrue faster than it did at the old linear 500%,
			// because the whole point was "much slower in general".
			foreach (var buildTicks in new[] { ConscriptTicks, 10, 20, 40, 60, 150, 250, 400, IskanderTicks })
			{
				for (var tier = 1; tier <= RankAccrual.MaxPurchasableRank; tier++)
				{
					var old = RankAccrual.IntervalTicks(buildTicks, tier, new RankCurve(0, buildTicks, 500, 0, 300));
					Assert.That(RankAccrual.IntervalTicks(buildTicks, tier, ShippedCurve),
						Is.GreaterThan(old), $"T={buildTicks} tier {tier}");
				}
			}
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

		#region Recovery: whole units evacuated

		[TestCase(1)]
		[TestCase(2)]
		[TestCase(3)]
		public void EvacuatingAWholeUnitAddsOneOfItsRank(int rank)
		{
			var s = Stock(240);
			s.CreditWhole(rank);

			Assert.That(s.Total(rank), Is.EqualTo(1));
			Assert.That(s.Peek(), Is.EqualTo(rank));
		}

		[Test]
		public void EvacuatedStockSpendsLikeAccruedStock()
		{
			var s = Stock(240);
			s.CreditWhole(2);

			Assert.That(s.Peek(), Is.EqualTo(2));
			s.Spend(2);
			Assert.That(s.Total(2), Is.EqualTo(0));
		}

		[Test]
		public void AccruedStockIsSpentBeforeRecoveredStock()
		{
			// Draining the capped pool first is what lets the wall clock start granting again
			// instead of idling full.
			var s = Stock(240);
			RunTo(s, 1200);
			s.CreditWhole(1);

			Assert.That(s.Stock[0], Is.EqualTo(1));
			Assert.That(s.BonusStock[0], Is.EqualTo(1));

			s.Spend(1);
			Assert.That(s.Stock[0], Is.EqualTo(0));
			Assert.That(s.BonusStock[0], Is.EqualTo(1));
		}

		#endregion

		#region Recovery: crew fractional credit

		[Test]
		public void ASingleCrewMemberOfTwoLeavesAPartialTimer()
		{
			var s = Stock(240);
			s.CreditShare(1, 100, 200);

			Assert.That(s.Total(1), Is.EqualTo(0), "half a crew is not yet a rank");
			Assert.That(s.PendingCreditTicks(1), Is.EqualTo(600), "half of the 1200-tick interval");
		}

		[Test]
		public void AFullCrewOfTwoCompletesExactlyOneRank()
		{
			var s = Stock(240);
			s.CreditShare(1, 100, 200);
			s.CreditShare(1, 100, 200);

			Assert.That(s.Total(1), Is.EqualTo(1));
			Assert.That(s.PendingCreditTicks(1), Is.EqualTo(0), "no surplus banked past the whole crew");
		}

		[Test]
		public void AFullCrewOfThreeCompletesExactlyOneRank()
		{
			// 1/3 does not divide the interval evenly, so this also pins the rounding: three shares
			// of 400 ticks each against a 1200-tick interval must still land on exactly one.
			var s = Stock(240);
			for (var i = 0; i < 3; i++)
				s.CreditShare(1, 100, 300);

			Assert.That(s.Total(1), Is.EqualTo(1));
		}

		[Test]
		public void PartialCreditPersistsAndAddsUpAcrossWrecks()
		{
			var s = Stock(240);
			s.CreditShare(1, 100, 200);   // one man out of the first tank
			Assert.That(s.Total(1), Is.EqualTo(0));

			s.CreditShare(1, 100, 200);   // one man out of a second tank of the same type
			Assert.That(s.Total(1), Is.EqualTo(1), "two half-crews recovered are worth one rank");
		}

		[Test]
		public void MixedRankCrewCreditsDifferentTiers()
		{
			// A rank-2 commander and a rank-1 driver each credit their own tier, not a shared one.
			var s = Stock(240);
			s.CreditShare(2, 100, 200);
			s.CreditShare(1, 100, 200);

			Assert.That(s.PendingCreditTicks(1), Is.EqualTo(600));
			Assert.That(s.PendingCreditTicks(2), Is.EqualTo(1800), "half of rank-2's 3600-tick interval");
			Assert.That(s.Total(1), Is.EqualTo(0));
			Assert.That(s.Total(2), Is.EqualTo(0));
		}

		[Test]
		public void CrewShareIsScaledToItsOwnTiersInterval()
		{
			// A full crew must be worth exactly one rank at every tier, even though the tiers have
			// very different intervals.
			var s = Stock(240);
			s.CreditShare(3, 100, 200);
			s.CreditShare(3, 100, 200);

			Assert.That(s.Total(3), Is.EqualTo(1));
		}

		[Test]
		public void CreditingAnOutOfRangeTierIsIgnored()
		{
			var s = Stock(240);
			s.CreditWhole(0);
			s.CreditWhole(4);
			s.CreditShare(4, 100, 100);

			Assert.That(s.Peek(), Is.EqualTo(0));
		}

		[Test]
		public void ZeroOrNegativeShareCreditsNothing()
		{
			var s = Stock(240);
			s.CreditShare(1, 0, 200);
			s.CreditShare(1, 100, 0);

			Assert.That(s.PendingCreditTicks(1), Is.EqualTo(0));
		}

		#endregion

		#region Recovery is exempt from the cap

		[Test]
		public void RecoveryPushesStockAboveTheCapButAccrualDoesNot()
		{
			var s = Stock(240);

			// Accrual alone stops dead at the rank-1 cap of 3, however long it runs.
			RunTo(s, 1200 * 20);
			Assert.That(s.Stock[0], Is.EqualTo(3));

			// Recovering units pushes past it.
			s.CreditWhole(1);
			s.CreditWhole(1);
			Assert.That(s.Total(1), Is.EqualTo(5), "cap 3 accrued plus 2 recovered");

			// And running the clock further still adds nothing, so the cap still binds accrual.
			RunTo(s, 1200 * 40);
			Assert.That(s.Total(1), Is.EqualTo(5));
		}

		[Test]
		public void RecoveryPastTheCapAtEveryTier()
		{
			var s = Stock(240);
			RunTo(s, 10800);
			Assert.That(new[] { s.Total(1), s.Total(2), s.Total(3) }, Is.EqualTo(new[] { 3, 2, 1 }));

			s.CreditWhole(3);
			Assert.That(s.Total(3), Is.EqualTo(2), "rank-3 cap is 1; recovery takes it to 2");
		}

		[Test]
		public void EvacuationCreditPushesAHoldingIntoDoubleFigures()
		{
			// The production icon prints this number beside its chevron, so how wide it can get is a
			// layout question and not only a simulation one. There is no ceiling: CreditWhole adds to
			// BonusStock with no cap check, so a player who evacuates veterans steadily can bank a
			// two-digit count of a tier whose cap is 3. Budget two digits, not one.
			// No accrual at all, so every one of these came home alive and the rank-1 cap of 3 is the
			// only thing that could have stopped it.
			var s = Stock(240);
			for (var i = 0; i < 14; i++)
				s.CreditWhole(1);

			Assert.That(s.Total(1), Is.EqualTo(14), "the rank-1 cap is 3 and does not apply to recovery");
			Assert.That(s.Total(1).ToString().Length, Is.EqualTo(2));

			// And the tier the icon draws is still 1 - depth never promotes.
			Assert.That(s.Peek(), Is.EqualTo(1));
		}

		[Test]
		public void TheChevronShowsTheTierThatWouldBeSpent()
		{
			// The icon draws exactly one chevron, chosen by Peek, and Spend only ever touches that
			// tier. These two must not be able to disagree: a mark naming a tier the purchase will
			// not consume is worse than no mark.
			var s = Stock(240);
			RunTo(s, 10800);

			for (var expected = 3; expected >= 1; expected--)
			{
				Assert.That(s.Peek(), Is.EqualTo(expected));
				Assert.That(RankAccrual.HighestHeldTier(new[] { s.Total(1), s.Total(2), s.Total(3) }),
					Is.EqualTo(expected), "Peek and HighestHeldTier are two implementations of one rule");

				// Drain the tier the mark is currently naming.
				while (s.Total(expected) > 0)
					s.Spend(expected);
			}

			Assert.That(s.Peek(), Is.EqualTo(0), "nothing banked draws no chevron at all");
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
