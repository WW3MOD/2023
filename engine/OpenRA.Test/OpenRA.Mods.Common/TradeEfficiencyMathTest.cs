#region Copyright & License Information
/*
 * WW3MOD @experimental — trade-efficiency purchasing feedback pins.
 *
 * TradeEfficiencyMath turns each composed type's own kill-value-vs-loss-value ledger into a bounded bias on
 * its target share, judged RELATIVE to the same ratio for the whole army. These tests pin the properties the
 * feature is worth having for, and the ones that stop it doing harm:
 *   * the lever OFF is exactly zero bias (the byte-identity contract for @stable);
 *   * a class trading above the army average is upweighted and one trading below is downweighted, with the
 *     ordering strict rather than merely non-equal — the RED case is a formula that returns 0 for everything,
 *     which every "no crash" assertion would happily pass;
 *   * the evidence floor and the smoothed divisor between them make a lucky first kill inert;
 *   * ApplyBias always renormalises to exactly 1000 and never resurrects a zero share.
 * No world is mounted; this is integer bookkeeping with zero simulation coupling.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class TradeEfficiencyMathTest
	{
		const int Floor = 2000;

		[Test]
		public void LeverOff_IsExactlyZeroBias()
		{
			// The @stable / frozen contract: TradeFeedbackMaxPct defaults to 0, so however lopsided the
			// ledger is, every slot must read 0 and ApplyBias must be an identity-plus-renormalise.
			var killed = new long[] { 90000, 0 };
			var lost = new long[] { 1000, 80000 };

			var off = TradeEfficiencyMath.BiasPercent(killed, lost, Floor, 100, 0);
			Assert.That(off, Is.EqualTo(new[] { 0, 0 }), "maxPct 0 must disable the pass entirely");

			var noScale = TradeEfficiencyMath.BiasPercent(killed, lost, Floor, 0, 25);
			Assert.That(noScale, Is.EqualTo(new[] { 0, 0 }), "scalePct 0 must disable the pass entirely");
		}

		[Test]
		public void GoodTraderIsUpweighted_AndBadTraderDownweighted()
		{
			// Slot 0 destroyed 60k for 10k of losses (6.0); slot 1 destroyed 10k for 50k (0.2).
			// Army average is 70k/60k = ~1.17, so 0 is far above it and 1 far below.
			var killed = new long[] { 60000, 10000 };
			var lost = new long[] { 10000, 50000 };

			var bias = TradeEfficiencyMath.BiasPercent(killed, lost, Floor, 100, 25);

			Assert.That(bias[0], Is.GreaterThan(0), "the class earning more than the army average must be bought MORE");
			Assert.That(bias[1], Is.LessThan(0), "the class earning less than the army average must be bought LESS");

			// Strict ordering, not just distinct signs: a formula that collapses to a constant would still
			// produce two different numbers if it happened to straddle zero, so pin the relation itself.
			Assert.That(bias[0], Is.GreaterThan(bias[1]));
			Assert.That(bias[0], Is.EqualTo(25), "a 6.0 trader against a 1.17 average saturates the clamp");
			Assert.That(bias[1], Is.EqualTo(-25), "a 0.2 trader against a 1.17 average saturates the other way");
		}

		[Test]
		public void ClampBindsAtMaxPct_InBothDirections()
		{
			var killed = new long[] { 500000, 0 };
			var lost = new long[] { 1000, 500000 };

			var bias = TradeEfficiencyMath.BiasPercent(killed, lost, Floor, 100, 10);
			Assert.That(bias[0], Is.EqualTo(10));
			Assert.That(bias[1], Is.EqualTo(-10));
		}

		[Test]
		public void ScalePctHalvesTheDeviation()
		{
			var killed = new long[] { 60000, 10000 };
			var lost = new long[] { 10000, 50000 };

			// A clamp high enough not to bind, so the scaling itself is what is under test.
			var full = TradeEfficiencyMath.BiasPercent(killed, lost, Floor, 100, 100000);
			var half = TradeEfficiencyMath.BiasPercent(killed, lost, Floor, 50, 100000);

			Assert.That(half[0], Is.EqualTo(full[0] / 2));
			Assert.That(half[1], Is.EqualTo(full[1] / 2));
		}

		[Test]
		public void UnderEvidenceFloor_ContributesNothing()
		{
			// Slot 1 has a perfect record on one cheap kill. Without the floor its ratio would dwarf the army
			// average and rewrite the plan off a single skirmish. Slots 0 and 2 are well-evidenced and sit
			// either side of the average, so this also pins that the floor silences ONLY the thin slot —
			// asserting bias[1] == 0 alone would pass against a function that returned all zeros.
			var killed = new long[] { 60000, 300, 5000 };
			var lost = new long[] { 5000, 0, 40000 };

			var bias = TradeEfficiencyMath.BiasPercent(killed, lost, Floor, 100, 25);

			Assert.That(bias[1], Is.EqualTo(0), "300 of evidence against a 2000 floor must not bias anything");
			Assert.That(bias[0], Is.EqualTo(25), "the well-evidenced good trader is still judged");
			Assert.That(bias[2], Is.EqualTo(-25), "the well-evidenced bad trader is still judged");
		}

		[Test]
		public void ZeroLossesDoesNotReadAsInfiniteRatio()
		{
			// Past the evidence floor but with nothing lost: the divisor smoothing (max(lost, floor)) is what
			// keeps this finite. It should be positive and clamped, never an overflow or a divide-by-zero.
			var killed = new long[] { 8000, 8000 };
			var lost = new long[] { 0, 8000 };

			var bias = TradeEfficiencyMath.BiasPercent(killed, lost, Floor, 100, 25);

			Assert.That(bias[0], Is.EqualTo(25));
			Assert.That(bias[0], Is.GreaterThan(bias[1]), "losing nothing must still beat trading one-for-one");
		}

		[Test]
		public void NoKillsAnywhere_IsNoOpinion()
		{
			// An army that has not fought yet must not have its shape rewritten. Deliberately NOT
			// "everything traded badly".
			var killed = new long[] { 0, 0, 0 };
			var lost = new long[] { 9000, 4000, 0 };

			Assert.That(TradeEfficiencyMath.BiasPercent(killed, lost, Floor, 100, 25),
				Is.EqualTo(new[] { 0, 0, 0 }));
		}

		[Test]
		public void EvidenceFloorOfZeroIsClampedAndDoesNotDivideByZero()
		{
			var killed = new long[] { 5000, 0 };
			var lost = new long[] { 0, 0 };

			Assert.That(() => TradeEfficiencyMath.BiasPercent(killed, lost, 0, 100, 25), Throws.Nothing);
		}

		[Test]
		public void DegenerateInputs_ReturnEmptyRatherThanThrow()
		{
			Assert.That(TradeEfficiencyMath.BiasPercent(null, null, Floor, 100, 25), Is.Empty);
			Assert.That(TradeEfficiencyMath.BiasPercent(new long[0], new long[0], Floor, 100, 25), Is.Empty);

			// A short/absent loss vector reads as "lost nothing", not as an index crash.
			Assert.That(() => TradeEfficiencyMath.BiasPercent(new long[] { 5000, 5000 }, new long[] { 1000 },
				Floor, 100, 25), Throws.Nothing);
		}

		[Test]
		public void ApplyBias_RenormalisesToExactlyOneThousand()
		{
			var targets = new[] { 500, 300, 200 };
			var biased = TradeEfficiencyMath.ApplyBias(targets, new[] { 25, -25, 0 });

			var sum = 0;
			foreach (var v in biased)
				sum += v;

			Assert.That(sum, Is.EqualTo(ForceCompositionMath.Total));
			Assert.That(biased[0], Is.GreaterThan(targets[0]), "the upweighted slot gains share");
			Assert.That(biased[1], Is.LessThan(targets[1]), "the downweighted slot loses share");
		}

		[Test]
		public void ApplyBias_NeverResurrectsAZeroShare()
		{
			// A type the designer excluded (share 0) must stay excluded however well it trades — feedback
			// corrects the designer's shape, it does not reintroduce types the designer left out.
			var biased = TradeEfficiencyMath.ApplyBias(new[] { 0, 500, 500 }, new[] { 100, 0, 0 });

			Assert.That(biased[0], Is.EqualTo(0));
		}

		[Test]
		public void ApplyBias_NullBiasIsIdentityPlusRenormalise()
		{
			var targets = new[] { 400, 400, 200 };
			Assert.That(TradeEfficiencyMath.ApplyBias(targets, null), Is.EqualTo(targets),
				"an already-normalised vector must survive the inert path unchanged");
		}

		[Test]
		public void ApplyBias_LargeNegativeFloorsAtZeroRatherThanInverting()
		{
			var biased = TradeEfficiencyMath.ApplyBias(new[] { 500, 500 }, new[] { -400, 0 });

			foreach (var v in biased)
				Assert.That(v, Is.GreaterThanOrEqualTo(0), "a big negative bias must never make a share negative");
		}

		[Test]
		public void ApplyBias_DegenerateInputsReturnEmpty()
		{
			Assert.That(TradeEfficiencyMath.ApplyBias(null, new[] { 10 }), Is.Empty);
			Assert.That(TradeEfficiencyMath.ApplyBias(new int[0], new[] { 10 }), Is.Empty);
		}
	}
}
