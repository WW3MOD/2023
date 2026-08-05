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

		// ===== (4b) SelectDeficit with the target CEILING (CompositionEnforceTargetCeiling) =====

		[Test]
		public void SelectDeficitCeiling_ThreeArgOverloadIsTheUncappedBehaviour()
		{
			// The frozen call site must keep selecting the least-over entry — the ceiling is opt-in only, and
			// this is what @stable byte-identity rests on.
			var targets = new[] { 300, 300, 400 };
			var census = new[] { 400, 310, 500 };

			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, AllEligible(3)),
				Is.EqualTo(ForceCompositionMath.SelectDeficit(targets, census, AllEligible(3), false)));
			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, AllEligible(3), false), Is.EqualTo(1));
		}

		[Test]
		public void SelectDeficitCeiling_OverTargetSupportIsNeverSelected()
		{
			// Every eligible type is at or over target: the uncapped pick buys the least-over one, the capped
			// pick refuses outright so the caller can decline the cycle.
			var targets = new[] { 300, 300, 400 };
			var census = new[] { 400, 310, 500 };

			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, AllEligible(3), true), Is.EqualTo(-1));
		}

		[Test]
		public void SelectDeficitCeiling_ExactlyOnTargetIsNotSelected()
		{
			// Strictly-below, so a class sitting exactly on its share is held there instead of being nudged
			// over by one more buy.
			var targets = new[] { 300, 700 };
			var census = new[] { 300, 700 };

			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, AllEligible(2), true), Is.EqualTo(-1));
		}

		[Test]
		public void SelectDeficitCeiling_ZeroStandingBaselineWinsOverOverTargetSupport()
		{
			// The live-play shape, in the units the fix is about. Slot 0 = mortar, 1 = AA, 2 = medic — all
			// far over their support targets; slot 3 = rifleman, 4 = LMG — both at ZERO standing value.
			var targets = new[] { 37, 28, 9, 95, 78 };
			var census = new[] { 330, 270, 57, 0, 0 };

			var pick = ForceCompositionMath.SelectDeficit(targets, census, AllEligible(5), true);

			Assert.That(pick, Is.EqualTo(3), "The starved rifleman has the largest positive deficit.");
			Assert.That(ForceCompositionMath.DeficitAt(targets, census, 0), Is.LessThan(0),
				"Mortar is over target and must not be a candidate at all.");
		}

		[Test]
		public void SelectDeficitCeiling_StillBuysTheOnlyUnderTargetTypeEvenIfItIsSupport()
		{
			// The ceiling is not a ban on support: a support class BELOW its share is still the right buy when
			// the line is already at target. Slot 0 = rifleman (on target), 1 = antitank (starved).
			var targets = new[] { 95, 66 };
			var census = new[] { 95, 10 };

			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, AllEligible(2), true), Is.EqualTo(1));
		}

		[Test]
		public void SelectDeficitCeiling_TiesStillResolveToTheLowerOrdinalIndex()
		{
			var targets = new[] { 300, 300, 400 };
			var census = new[] { 200, 200, 400 };

			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, AllEligible(3), true), Is.EqualTo(0));
		}

		[Test]
		public void SelectDeficitCeiling_PreferUnderTargetPolicyNeverStalls()
		{
			// The two-stage policy ChooseByDeficit runs: strict pass first, least-over pass only if the strict
			// pass finds nothing. The stall case it exists for — the ONLY under-target slot (2) is capped by
			// UnitLimits, everything else is a hair over. Strict alone would decline forever; the policy still
			// buys, and buys the least-over of what it can actually get.
			var targets = new[] { 300, 300, 400 };
			var census = new[] { 340, 310, 350 };
			var eligible = new[] { true, true, false };

			var strict = ForceCompositionMath.SelectDeficit(targets, census, eligible, true);
			Assert.That(strict, Is.EqualTo(-1), "No eligible slot is under target.");

			var policy = strict >= 0 ? strict : ForceCompositionMath.SelectDeficit(targets, census, eligible, false);
			Assert.That(policy, Is.EqualTo(1), "Least-over eligible slot, so production never stalls.");
		}

		[Test]
		public void SelectDeficitCeiling_IneligibleUnderTargetSlotIsStillSkipped()
		{
			// Capped/unaffordable/not-buildable-here beats "is under target": slot 1 must not be picked.
			var targets = new[] { 300, 300, 400 };
			var census = new[] { 290, 0, 500 };
			var eligible = new[] { true, false, true };

			Assert.That(ForceCompositionMath.SelectDeficit(targets, census, eligible, true), Is.EqualTo(0));
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
