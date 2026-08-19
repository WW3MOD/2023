#region Copyright & License Information
/*
 * WW3MOD @experimental — AdaptiveProduction threat-aware unit-need scoring tests.
 *
 * Pins the pure decision CompositionNeedMath turns believed enemy composition + budget into, so the
 * behaviour is proven WITHOUT a game run and cannot silently regress:
 *   * gap detection at the weak-AA boundary (the air-strike window opens/closes exactly at AaWeakThreshold),
 *   * the affordability gate (expensive airframes rare-but-real),
 *   * weights-off == legacy (every weight 0 -> no candidate -> -1, the @stable byte-identical no-op),
 *   * tie-break determinism (equal scores resolve by fixed Order).
 * Pure integer decision; no world mounted.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class CompositionNeedMathTest
	{
		[Test]
		public void CounterScore_ScalesWithValueAndWeight_ZeroWhenOff()
		{
			// score = value * weightPct / 100.
			Assert.That(CompositionNeedMath.CounterScore(4000, 100), Is.EqualTo(4000));
			Assert.That(CompositionNeedMath.CounterScore(4000, 50), Is.EqualTo(2000));
			Assert.That(CompositionNeedMath.CounterScore(4000, 150), Is.EqualTo(6000));

			// Off / no value => 0 (the legacy no-op).
			Assert.That(CompositionNeedMath.CounterScore(4000, 0), Is.EqualTo(0));
			Assert.That(CompositionNeedMath.CounterScore(0, 100), Is.EqualTo(0));
			Assert.That(CompositionNeedMath.CounterScore(-500, 100), Is.EqualTo(0));
		}

		[Test]
		public void AirOpportunity_ClosedAtAndAboveThreshold_OpenBelow()
		{
			const int threshold = 2000;
			const int weight = 100;
			const long ground = 4000;

			// Boundary: AA exactly at threshold -> sky defended -> window CLOSED (0).
			Assert.That(CompositionNeedMath.AirOpportunityScore(threshold, ground, threshold, weight), Is.EqualTo(0));

			// Above threshold -> still closed.
			Assert.That(CompositionNeedMath.AirOpportunityScore(threshold + 1, ground, threshold, weight), Is.EqualTo(0));

			// Just below threshold -> a sliver of a window (gap = 1).
			// score = weight * ground * gap / (threshold * 100) = 100 * 4000 * 1 / 200000 = 2.
			Assert.That(CompositionNeedMath.AirOpportunityScore(threshold - 1, ground, threshold, weight), Is.EqualTo(2));

			// Zero believed AA -> widest window. gap = threshold => score = weight*ground/100 = 4000.
			Assert.That(CompositionNeedMath.AirOpportunityScore(0, ground, threshold, weight), Is.EqualTo(4000));

			// Half AA -> half the window. gap = 1000 => 100*4000*1000/(2000*100) = 2000.
			Assert.That(CompositionNeedMath.AirOpportunityScore(1000, ground, threshold, weight), Is.EqualTo(2000));
		}

		[Test]
		public void AirOpportunity_Monotonic_WeakerAaScoresHigher()
		{
			const int threshold = 3000;
			var strong = CompositionNeedMath.AirOpportunityScore(2000, 5000, threshold, 100);
			var weaker = CompositionNeedMath.AirOpportunityScore(1000, 5000, threshold, 100);
			var none = CompositionNeedMath.AirOpportunityScore(0, 5000, threshold, 100);
			Assert.That(weaker, Is.GreaterThan(strong));
			Assert.That(none, Is.GreaterThan(weaker));
		}

		[Test]
		public void AirOpportunity_ZeroWhenLeverOffOrNothingToHit()
		{
			// Weight off.
			Assert.That(CompositionNeedMath.AirOpportunityScore(0, 4000, 2000, 0), Is.EqualTo(0));

			// Threshold off (<=0) — treat as no window.
			Assert.That(CompositionNeedMath.AirOpportunityScore(0, 4000, 0, 100), Is.EqualTo(0));

			// No believed ground force worth striking => 0 even with a wide-open sky.
			Assert.That(CompositionNeedMath.AirOpportunityScore(0, 0, 2000, 100), Is.EqualTo(0));

			// Negative believed AA is clamped to 0 (widest window), not an under/overflow.
			Assert.That(CompositionNeedMath.AirOpportunityScore(-500, 4000, 2000, 100), Is.EqualTo(4000));
		}

		[Test]
		public void Affordable_RequiresReserveMultipleOfCost()
		{
			// reservePct 200 => need 2x cost banked.
			Assert.That(CompositionNeedMath.Affordable(11999, 6000, 200), Is.False);
			Assert.That(CompositionNeedMath.Affordable(12000, 6000, 200), Is.True);
			Assert.That(CompositionNeedMath.Affordable(20000, 6000, 200), Is.True);

			// reservePct 100 => exactly affordable at cost.
			Assert.That(CompositionNeedMath.Affordable(5999, 6000, 100), Is.False);
			Assert.That(CompositionNeedMath.Affordable(6000, 6000, 100), Is.True);

			// Gate off (reservePct <= 0) or unknown cost (<= 0) => always affordable.
			Assert.That(CompositionNeedMath.Affordable(0, 6000, 0), Is.True);
			Assert.That(CompositionNeedMath.Affordable(0, 0, 200), Is.True);
			Assert.That(CompositionNeedMath.Affordable(0, int.MaxValue, 200), Is.False);
		}

		[Test]
		public void SelectNeed_AllWeightsOff_ReturnsMinusOne_LegacyNoOp()
		{
			// Every score 0 (the weights-off / @stable path) => no candidate => -1 => module makes no request.
			var candidates = new List<CompositionNeedMath.Candidate>
			{
				new CompositionNeedMath.Candidate(0, 6000, 0),
				new CompositionNeedMath.Candidate(0, 600, 1),
				new CompositionNeedMath.Candidate(0, 600, 2),
				new CompositionNeedMath.Candidate(0, 700, 3),
			};
			Assert.That(CompositionNeedMath.SelectNeed(candidates, 100000, 200), Is.EqualTo(-1));
		}

		[Test]
		public void SelectNeed_PicksHighestScoringAffordable()
		{
			// Air-strike (idx 0) has the top score but costs 6000; with only 5000 banked (reserve 200 needs
			// 12000) it is UNAFFORDABLE, so the next-highest affordable candidate (anti-armor idx 1) wins.
			var candidates = new List<CompositionNeedMath.Candidate>
			{
				new CompositionNeedMath.Candidate(5000, 6000, 0), // air-strike, unaffordable
				new CompositionNeedMath.Candidate(4000, 600, 1),  // anti-armor, affordable
				new CompositionNeedMath.Candidate(1000, 600, 2),  // anti-infantry
			};
			Assert.That(CompositionNeedMath.SelectNeed(candidates, 5000, 200), Is.EqualTo(1));

			// Rich enough (12000 banked) => the air-strike is now affordable AND top-scoring => it wins.
			Assert.That(CompositionNeedMath.SelectNeed(candidates, 12000, 200), Is.EqualTo(0));
		}

		[Test]
		public void SelectNeed_TieBrokenByOrder_EarlierWins()
		{
			// Equal scores, all affordable: the smaller Order wins deterministically (idx 2 has Order 0).
			var candidates = new List<CompositionNeedMath.Candidate>
			{
				new CompositionNeedMath.Candidate(3000, 600, 5),
				new CompositionNeedMath.Candidate(3000, 600, 3),
				new CompositionNeedMath.Candidate(3000, 600, 0),
				new CompositionNeedMath.Candidate(3000, 600, 9),
			};
			Assert.That(CompositionNeedMath.SelectNeed(candidates, 100000, 200), Is.EqualTo(2));
		}

		[Test]
		public void SelectNeed_EmptyPoolCostSentinel_NeverSelected()
		{
			// A configured-but-empty pool reports cost int.MaxValue -> never affordable even with a positive
			// score, so it is skipped and the affordable lower-scoring candidate wins.
			var candidates = new List<CompositionNeedMath.Candidate>
			{
				new CompositionNeedMath.Candidate(9000, int.MaxValue, 0), // empty pool, huge score, unbuyable
				new CompositionNeedMath.Candidate(1000, 600, 1),
			};
			Assert.That(CompositionNeedMath.SelectNeed(candidates, 1000000, 200), Is.EqualTo(1));
		}

		[Test]
		public void SelectNeed_NoneAffordable_ReturnsMinusOne()
		{
			var candidates = new List<CompositionNeedMath.Candidate>
			{
				new CompositionNeedMath.Candidate(5000, 6000, 0),
				new CompositionNeedMath.Candidate(4000, 6000, 1),
			};
			// Only 1000 banked; reserve 200 needs 12000 for either => nothing affordable => -1.
			Assert.That(CompositionNeedMath.SelectNeed(candidates, 1000, 200), Is.EqualTo(-1));
		}

		[Test]
		public void SelectNeed_NullCandidates_ReturnsMinusOne()
		{
			Assert.That(CompositionNeedMath.SelectNeed(null, 100000, 200), Is.EqualTo(-1));
		}
	}
}
