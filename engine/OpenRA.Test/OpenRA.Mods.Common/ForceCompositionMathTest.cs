#region Copyright & License Information
/*
 * WW3MOD @experimental — composition-directed purchasing math tests.
 *
 * Pins the decision UnitBuilderBotModule.ChooseByDeficit turns into a call-in when CompositionDirected is on,
 * so "the army holds the designer's shape instead of drifting toward whatever type lives longest" cannot
 * silently regress:
 *   (1) APPORTIONMENT — shares always sum to EXACTLY 1000, including the awkward remainder cases, with an
 *       ordinal tie-break; an empty army reads all zeros (no shape), never an even split.
 *   (2) SMOOTHING — the integer EMA converges, is stable at a fixed point, and its endpoints (alpha 0/100)
 *       behave.
 *   (3) BIAS — a below-deadband threat class contributes nothing, the summed bias is clamped, and the biased
 *       vector is renormalised back to exactly 1000.
 *   (4) SELECTION — largest deficit wins, ties resolve to the lower ordinal, ineligible slots are skipped,
 *       an all-at-or-over-target vector still selects (the least-over entry), and -1 comes back ONLY when
 *       nothing is eligible.
 *   (4b) THE CEILING — the two things CompositionEnforceTargetCeiling actually changes: when a build cycle
 *       DECLINES rather than falling back to the uniform lottery, and when an external FIFO request is over
 *       target. Note what is absent: the flag does NOT change the pick, because restricting the argmax to
 *       under-target slots is a provable no-op.
 *   (5) THE REGRESSION — the measured drift shape (mortars far over target, frontline far under) must select
 *       frontline and must never select mortar.
 * Pure integer math; no world mounted.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ForceCompositionMathTest
	{
		// ===== (1) SharesPerMille =====

		[Test]
		public void SharesPerMille_SumsToExactlyOneThousand()
		{
			var shares = ForceCompositionMath.SharesPerMille(new[] { 1500, 2500, 300, 700 });
			Assert.That(shares.Sum(), Is.EqualTo(1000));
		}

		[Test]
		public void SharesPerMille_ExactSplitIsExact()
		{
			var shares = ForceCompositionMath.SharesPerMille(new[] { 1, 1, 1, 1 });
			Assert.That(shares, Is.EqualTo(new[] { 250, 250, 250, 250 }));
		}

		[Test]
		public void SharesPerMille_RemainderGoesToLargestFractionsThenLowestIndex()
		{
			// Three equal values: 1000/3 = 333 each, one unit of remainder. All remainders are equal, so the
			// ordinal tie-break hands it to index 0.
			var shares = ForceCompositionMath.SharesPerMille(new[] { 10, 10, 10 });
			Assert.That(shares, Is.EqualTo(new[] { 334, 333, 333 }));
			Assert.That(shares.Sum(), Is.EqualTo(1000));
		}

		[Test]
		public void SharesPerMille_SevenWayRemainderStillSumsToOneThousand()
		{
			var shares = ForceCompositionMath.SharesPerMille(new[] { 1, 1, 1, 1, 1, 1, 1 });
			Assert.That(shares.Sum(), Is.EqualTo(1000));
			Assert.That(shares.Take(6).All(s => s == 143), Is.True, "Largest remainders take the leftover first.");
		}

		[Test]
		public void SharesPerMille_AllZeroIsAllZero()
		{
			// An empty army has NO shape. Returning zeros (not an even split) is what lets every target read
			// as a full deficit so the opening buys follow the target vector.
			var shares = ForceCompositionMath.SharesPerMille(new[] { 0, 0, 0 });
			Assert.That(shares, Is.EqualTo(new[] { 0, 0, 0 }));
		}

		[Test]
		public void SharesPerMille_NegativesAreTreatedAsZeroAndDoNotBreakTheSum()
		{
			var shares = ForceCompositionMath.SharesPerMille(new[] { -50, 100, 100 });
			Assert.That(shares[0], Is.EqualTo(0));
			Assert.That(shares.Sum(), Is.EqualTo(1000));
		}

		// ===== (2) SmoothShares =====

		[Test]
		public void SmoothShares_IsStableAtAFixedPoint()
		{
			var state = new[] { 400, 600 };
			for (var i = 0; i < 20; i++)
				state = ForceCompositionMath.SmoothShares(state, new[] { 400, 600 }, 20);

			Assert.That(state, Is.EqualTo(new[] { 400, 600 }));
		}

		[Test]
		public void SmoothShares_ConvergesTowardTheObservation()
		{
			var state = new[] { 0, 0 };
			for (var i = 0; i < 50; i++)
				state = ForceCompositionMath.SmoothShares(state, new[] { 1000, 0 }, 20);

			// Integer truncation stops just short of the observation; it must be close and never overshoot.
			Assert.That(state[0], Is.GreaterThan(950));
			Assert.That(state[0], Is.LessThanOrEqualTo(1000));
			Assert.That(state[1], Is.EqualTo(0));
		}

		[Test]
		public void SmoothShares_AlphaEndpointsFreezeOrTrack()
		{
			Assert.That(ForceCompositionMath.SmoothShares(new[] { 300 }, new[] { 900 }, 0), Is.EqualTo(new[] { 300 }));
			Assert.That(ForceCompositionMath.SmoothShares(new[] { 300 }, new[] { 900 }, 100), Is.EqualTo(new[] { 900 }));
		}

		[Test]
		public void SmoothShares_NullPreviousStateStartsFromZero()
		{
			Assert.That(ForceCompositionMath.SmoothShares(null, new[] { 1000, 0 }, 50), Is.EqualTo(new[] { 500, 0 }));
		}

		// ===== (3) ApplyCounterBias =====

		// Two own-roles (0 = antitank, 1 = frontline); three enemy classes (0 = air, 1 = armor, 2 = infantry).
		static int[,] Matrix()
		{
			var m = new int[3, 2];
			m[1, 0] = 40; // armor -> antitank
			m[2, 1] = 20; // infantry -> frontline
			return m;
		}

		[Test]
		public void ApplyCounterBias_BelowDeadbandContributesNothing()
		{
			var baseTargets = new[] { 500, 500 };

			// 20‰ of believed armor is under the 30‰ deadband ⇒ identity (renormalised) result.
			var biased = ForceCompositionMath.ApplyCounterBias(baseTargets, new[] { 0, 20, 0 }, Matrix(), 200, 30);
			Assert.That(biased, Is.EqualTo(new[] { 500, 500 }));
		}

		[Test]
		public void ApplyCounterBias_HeavyArmorRaisesAntiTankShare()
		{
			var biased = ForceCompositionMath.ApplyCounterBias(new[] { 500, 500 }, new[] { 0, 900, 100 }, Matrix(), 200, 30);

			Assert.That(biased[0], Is.GreaterThan(500), "Believed armor must raise the anti-tank target.");
			Assert.That(biased[0], Is.GreaterThan(biased[1]));
			Assert.That(biased.Sum(), Is.EqualTo(1000));
		}

		[Test]
		public void ApplyCounterBias_ClampsAtBiasMaxPct()
		{
			// A huge matrix weight must not run away: the bias is clamped to +/-biasMaxPct BEFORE it is applied.
			var runaway = new int[3, 2];
			runaway[1, 0] = 100000;

			// The weight that lands EXACTLY on the clamp (bias = 50 * 1000/1000 = 50).
			var exact = new int[3, 2];
			exact[1, 0] = 50;

			var clamped = ForceCompositionMath.ApplyCounterBias(new[] { 500, 500 }, new[] { 0, 1000, 0 }, runaway, 50, 30);
			var atClamp = ForceCompositionMath.ApplyCounterBias(new[] { 500, 500 }, new[] { 0, 1000, 0 }, exact, 50, 30);

			// base*(100+50)/100 = 750 vs 500 ⇒ 600/400 after renormalisation. The clamp, not the weight, decides.
			Assert.That(clamped, Is.EqualTo(new[] { 600, 400 }));
			Assert.That(clamped, Is.EqualTo(atClamp), "A runaway weight must land on the same clamp as the exact one.");
		}

		[Test]
		public void ApplyCounterBias_RenormalisesToExactlyOneThousand()
		{
			var biased = ForceCompositionMath.ApplyCounterBias(new[] { 190, 140, 90, 50, 530 },
				new[] { 300, 500, 200 }, new int[3, 5], 200, 30);

			Assert.That(biased.Sum(), Is.EqualTo(1000));
		}

		[Test]
		public void ApplyCounterBias_NullMatrixIsIdentityApportionment()
		{
			// The inert default: no matrix ⇒ the designer's targets, apportioned, unchanged in ordering.
			var biased = ForceCompositionMath.ApplyCounterBias(new[] { 250, 250, 500 }, new[] { 0, 1000, 0 }, null, 200, 30);
			Assert.That(biased, Is.EqualTo(new[] { 250, 250, 500 }));
		}

		// ===== (4) SelectDeficit =====

		static bool[] AllEligible(int n) => Enumerable.Repeat(true, n).ToArray();

		[Test]
		public void SelectDeficit_PicksTheLargestDeficit()
		{
			var targets = new[] { 300, 300, 400 };
			var census = new[] { 290, 100, 390 };
			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, AllEligible(3)), Is.EqualTo(1));
		}

		[Test]
		public void SelectDeficit_TiesResolveToTheLowerOrdinalIndex()
		{
			var targets = new[] { 300, 300, 400 };
			var census = new[] { 200, 200, 400 };
			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, AllEligible(3)), Is.EqualTo(0));
		}

		[Test]
		public void SelectDeficit_SkipsIneligibleEvenWhenItHasTheLargestDeficit()
		{
			var targets = new[] { 300, 300, 400 };
			var census = new[] { 290, 0, 390 };
			var eligible = new[] { true, false, true };

			// Slot 1 has by far the largest deficit but is capped/unaffordable/not buildable here.
			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, eligible), Is.EqualTo(0));
		}

		[Test]
		public void SelectDeficit_AllAtOrOverTargetStillSelectsTheLeastOver()
		{
			// No positive-deficit requirement: purchase VOLUME must not drop when the army is on-shape, and the
			// least-over type is the buy that keeps the proportions closest to target.
			var targets = new[] { 300, 300, 400 };
			var census = new[] { 400, 310, 500 };
			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, AllEligible(3)), Is.EqualTo(1));
		}

		[Test]
		public void SelectDeficit_ReturnsMinusOneOnlyWhenNothingIsEligible()
		{
			var targets = new[] { 300, 300, 400 };
			var census = new[] { 0, 0, 0 };
			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, new[] { false, false, false }), Is.EqualTo(-1));
			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, new[] { false, true, false }), Is.EqualTo(1));
		}

		// ===== (4b) The two real effects of CompositionEnforceTargetCeiling =====
		//
		// NOT pinned here, deliberately: "the pick prefers under-target classes". Restricting SelectDeficit to
		// deficit > 0 and falling back to the unrestricted argmax when that finds nothing is a provable no-op
		// (if any eligible slot is under target, the unrestricted maximizer is itself under target and both
		// walks tie-break to the same lowest index), so there is nothing to pin. The flag's real effects are
		// the declined cycle and the FIFO ceiling below.

		[Test]
		public void ShouldDeclineCycle_FlagOffAlwaysTakesTheLegacyFallback()
		{
			// The byte-identity guarantee: with the flag off nothing can ever decline, whatever the queue looks
			// like, so the frozen path still reaches ChooseRandomUnitToBuild and keeps its RNG draw.
			Assert.That(ForceCompositionMath.ShouldDeclineCycle(false, false, true), Is.False);
			Assert.That(ForceCompositionMath.ShouldDeclineCycle(false, false, false), Is.False);
		}

		[Test]
		public void ShouldDeclineCycle_NothingEligibleButComposedTypesBuildableDeclines()
		{
			// Composed types ARE buildable here and none came back eligible — priced out, or at UnitLimit.
			// That is a decision not to buy, not an absence of opinion.
			Assert.That(ForceCompositionMath.ShouldDeclineCycle(true, false, true), Is.True);
		}

		[Test]
		public void ShouldDeclineCycle_NoComposedTypeInThisQueueStillFallsBack()
		{
			// A heli-only pool has no composition opinion at all; declining there would cut purchase volume.
			Assert.That(ForceCompositionMath.ShouldDeclineCycle(true, false, false), Is.False);
		}

		[Test]
		public void ShouldDeclineCycle_ASelectionNeverDeclines()
		{
			Assert.That(ForceCompositionMath.ShouldDeclineCycle(true, true, true), Is.False);
			Assert.That(ForceCompositionMath.ShouldDeclineCycle(true, true, false), Is.False);
		}

		[Test]
		public void RequestExceedsCeiling_ExcludesTheRequestsOwnPendingCredit()
		{
			// THE FIX-3 CASE. Slot 0 is antitank at cost 300; the census already credits the very request under
			// test (it is still on queuedBuildRequests when the predicate is asked). Raw census 300/2700 is
			// exactly 100‰ against a 100‰ target — reading that as "at target" would refuse a class whose
			// STANDING share is 0. Excluding the candidate's own cost is what restores "already over".
			var census = new[] { 300, 2700 };
			var targets = new[] { 100, 900 };

			Assert.That(ForceCompositionMath.RequestExceedsCeiling(census, 0, 300, targets), Is.False,
				"Standing value at this slot is zero once the request's own credit comes out.");

			// Same numbers WITHOUT the exclusion is the buggy reading, and it refuses.
			Assert.That(ForceCompositionMath.DeficitAt(targets, ForceCompositionMath.SharesPerMille(census), 0),
				Is.LessThanOrEqualTo(0), "The un-subtracted census is exactly at target — the old rule refused here.");
		}

		[Test]
		public void RequestExceedsCeiling_GenuinelyOverTargetIsStillRefused()
		{
			// 1500 standing + 300 for the request under test, against a 100‰ target on a 3000 army: still far
			// over once its own cost is removed.
			var census = new[] { 1800, 1200 };
			var targets = new[] { 100, 900 };

			Assert.That(ForceCompositionMath.RequestExceedsCeiling(census, 0, 300, targets), Is.True);
		}

		[Test]
		public void RequestExceedsCeiling_UnderTargetIsAllowed()
		{
			var census = new[] { 300, 2700 };
			var targets = new[] { 400, 600 };

			Assert.That(ForceCompositionMath.RequestExceedsCeiling(census, 0, 300, targets), Is.False);
		}

		[Test]
		public void RequestExceedsCeiling_NeverMutatesTheCallersCensus()
		{
			// The caller reuses CensusValues() output; a subtracting predicate must not corrupt it.
			var census = new[] { 1800, 1200 };
			var targets = new[] { 100, 900 };

			ForceCompositionMath.RequestExceedsCeiling(census, 0, 300, targets);

			Assert.That(census, Is.EqualTo(new[] { 1800, 1200 }));
		}

		[Test]
		public void RequestExceedsCeiling_UnknownSlotOrNullInputReadsAsNotOver()
		{
			var census = new[] { 1800, 1200 };
			var targets = new[] { 100, 900 };

			Assert.That(ForceCompositionMath.RequestExceedsCeiling(census, -1, 300, targets), Is.False);
			Assert.That(ForceCompositionMath.RequestExceedsCeiling(census, 9, 300, targets), Is.False);
			Assert.That(ForceCompositionMath.RequestExceedsCeiling(null, 0, 300, targets), Is.False);
			Assert.That(ForceCompositionMath.RequestExceedsCeiling(census, 0, 300, null), Is.False);
		}

		[Test]
		public void RequestExceedsCeiling_CostLargerThanTheCensusClampsToZero()
		{
			// Defensive: a cost bigger than the recorded slot value must floor at 0, not go negative and
			// corrupt the apportionment.
			var census = new[] { 100, 900 };
			var targets = new[] { 100, 900 };

			Assert.That(ForceCompositionMath.RequestExceedsCeiling(census, 0, 5000, targets), Is.False);
		}

		// ===== (5) The regression this whole lane exists to fix =====

		[Test]
		public void Regression_DriftedArmyBuysFrontlineNotMortar()
		{
			// The measured drift shape: mortars have accumulated to 400‰ of army value against a 50‰ target
			// (they are long-lived rear-line units and the uniform lottery kept buying them), while frontline
			// armour has been eaten down to 20‰ against a 180‰ target.
			// Slot 0 = mortar, 1 = frontline infantry, 2 = antitank, 3 = armour, 4 = support. Both vectors sum
			// to 1000, so this is a real drifted army: the mortar over-share came OUT of the frontline.
			var targets = new[] { 50, 180, 90, 400, 280 };
			var census = new[] { 400, 20, 90, 350, 140 };

			var pick = ForceCompositionMath.SelectDeficit(targets, census, AllEligible(5));

			Assert.That(pick, Is.EqualTo(1), "Must buy the starved frontline.");
			Assert.That(pick, Is.Not.EqualTo(0), "Must never buy another mortar at 8x its target share.");
			Assert.That(ForceCompositionMath.DeficitAt(targets, census, 0), Is.LessThan(0),
				"The mortar deficit is strongly negative — it is the over-accumulated type.");
		}
	}
}
