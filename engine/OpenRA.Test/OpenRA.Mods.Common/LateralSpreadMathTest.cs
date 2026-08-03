#region Copyright & License Information
/*
 * WW3MOD lateral-spread math test — frontline-influence Phase 7.
 *
 * Pins the pure offense-side reshaping (LateralSpreadMath.Rebalance) on synthetic inputs, no World:
 *   - the enemy-SR Pressure axis is capped at its pool share and the freed units are redistributed;
 *   - redistribution is coverage-first (every non-SR axis gets some) then mass-biased toward the WEAKEST-enemy
 *     (highest-opportunity) axis;
 *   - the total unit count is CONSERVED exactly;
 *   - the cap floors at the funding minimum and the inert paths (cap <= 0 / >= 100, SR under cap, no non-SR axis
 *     to receive the excess) leave the base sizes untouched;
 *   - the transform is deterministic (same inputs ⇒ same output).
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class LateralSpreadMathTest
	{
		[Test]
		public void CapsSrAxisAndRedistributesToTheWeakestSectorFirst()
		{
			// Pool 100: SR axis wants 80, two others want 10 each. Cap 40% ⇒ SR floored to 40, excess 40 spread
			// across the non-SR axes by opportunity {A:5, B:15} (B sits in the believed-thinner enemy sector, so it
			// draws the larger share). Coverage 1 each, then Hamilton over the opportunity weight.
			var baseSizes = new[] { 80, 10, 10 };
			var isSr = new[] { true, false, false };
			var opportunity = new[] { 0, 5, 15 };
			var sizes = LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, total: 100, srCapPct: 40, minAxisSize: 2);

			Assert.Multiple(() =>
			{
				Assert.That(sizes, Is.EqualTo(new[] { 40, 20, 40 }), "SR capped to 40%; excess spread, mass to the weaker-enemy axis B");
				Assert.That(sizes[0] + sizes[1] + sizes[2], Is.EqualTo(100), "total unit count conserved");
				Assert.That(sizes[2], Is.GreaterThan(sizes[1]), "the weakest-enemy (highest-opportunity) axis draws strictly more mass");
			});
		}

		[Test]
		public void CoverageGivesEveryNonSrAxisAShareEvenTheStrongEnemyOne()
		{
			// Equal opportunity (both floored to 1) ⇒ coverage-first + even Hamilton split: every non-SR axis is
			// staffed (the front is covered), none is starved. Pool 100, cap 40 ⇒ excess 50 split 25/25.
			var baseSizes = new[] { 90, 5, 5 };
			var isSr = new[] { true, false, false };
			var opportunity = new[] { 0, 1, 1 };
			var sizes = LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, total: 100, srCapPct: 40, minAxisSize: 2);

			Assert.Multiple(() =>
			{
				Assert.That(sizes, Is.EqualTo(new[] { 40, 30, 30 }), "SR capped; excess split evenly when opportunity is equal");
				Assert.That(sizes[1], Is.GreaterThan(0), "every non-SR axis is covered");
				Assert.That(sizes[2], Is.GreaterThan(0), "every non-SR axis is covered");
				Assert.That(sizes[0] + sizes[1] + sizes[2], Is.EqualTo(100), "total conserved");
			});
		}

		[Test]
		public void SubZeroOpportunityIsFlooredToOneSoNoAxisIsDropped()
		{
			// A negative/zero opportunity must not zero out an axis's coverage — it is floored to 1.
			var baseSizes = new[] { 80, 10, 10 };
			var isSr = new[] { true, false, false };
			var opportunity = new[] { 0, -4, 0 };
			var sizes = LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, total: 100, srCapPct: 40, minAxisSize: 2);

			Assert.Multiple(() =>
			{
				Assert.That(sizes[1], Is.GreaterThan(0), "floored opportunity still earns coverage");
				Assert.That(sizes[2], Is.GreaterThan(0), "floored opportunity still earns coverage");
				Assert.That(sizes[0], Is.EqualTo(40), "SR still capped");
				Assert.That(sizes[0] + sizes[1] + sizes[2], Is.EqualTo(100), "total conserved");
			});
		}

		[Test]
		public void CapFloorsAtTheFundingMinimum()
		{
			// A tiny cap percent would drive SR below the axis funding minimum; the cap floors at minAxisSize so a
			// capped SR axis is never pushed under min (and thus retired). total 100, cap 1% ⇒ raw 1, floored to 5.
			var baseSizes = new[] { 80, 20 };
			var isSr = new[] { true, false };
			var opportunity = new[] { 0, 3 };
			var sizes = LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, total: 100, srCapPct: 1, minAxisSize: 5);

			Assert.Multiple(() =>
			{
				Assert.That(sizes[0], Is.EqualTo(5), "SR cap floored at minAxisSize, not driven below it");
				Assert.That(sizes[1], Is.EqualTo(95), "the whole excess lands on the sole other axis");
				Assert.That(sizes[0] + sizes[1], Is.EqualTo(100), "total conserved");
			});
		}

		[Test]
		public void InertWhenCapDisabledOrOutOfRange()
		{
			var baseSizes = new[] { 80, 10, 10 };
			var isSr = new[] { true, false, false };
			var opportunity = new[] { 0, 5, 15 };

			Assert.Multiple(() =>
			{
				Assert.That(LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, 100, 0, 2),
					Is.EqualTo(baseSizes), "cap 0 ⇒ inert");
				Assert.That(LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, 100, 100, 2),
					Is.EqualTo(baseSizes), "cap 100 ⇒ inert");
				Assert.That(LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, 100, 250, 2),
					Is.EqualTo(baseSizes), "cap > 100 ⇒ inert");
			});
		}

		[Test]
		public void NoOpWhenSrAlreadyUnderCap()
		{
			// SR wants 30, cap 40 ⇒ nothing to shave, sizes unchanged.
			var baseSizes = new[] { 30, 40, 30 };
			var isSr = new[] { true, false, false };
			var opportunity = new[] { 0, 5, 15 };
			var sizes = LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, total: 100, srCapPct: 40, minAxisSize: 2);

			Assert.That(sizes, Is.EqualTo(baseSizes), "SR already under the cap ⇒ no reshape");
		}

		[Test]
		public void DoesNotCapWhenThereIsNoOtherAxisToReceiveTheExcess()
		{
			// The enemy SR is the ONLY axis — funnelling onto the sole viable target is correct, so the cap is NOT
			// applied (the units would otherwise strand with nowhere to spread).
			var baseSizes = new[] { 100 };
			var isSr = new[] { true };
			var opportunity = new[] { 0 };
			var sizes = LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, total: 100, srCapPct: 40, minAxisSize: 2);

			Assert.That(sizes, Is.EqualTo(new[] { 100 }), "sole SR target keeps its whole allocation");
		}

		[Test]
		public void EmptyInputReturnsEmpty()
		{
			var sizes = LateralSpreadMath.Rebalance(new int[0], new bool[0], new int[0], total: 0, srCapPct: 40, minAxisSize: 2);
			Assert.That(sizes, Is.Empty);
		}

		[Test]
		public void IsDeterministicAcrossRepeatedCalls()
		{
			var baseSizes = new[] { 77, 11, 7, 5 };
			var isSr = new[] { true, false, false, false };
			var opportunity = new[] { 0, 9, 9, 2 };

			var a = LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, 100, 40, 2);
			var b = LateralSpreadMath.Rebalance(baseSizes, isSr, opportunity, 100, 40, 2);

			Assert.Multiple(() =>
			{
				Assert.That(a, Is.EqualTo(b), "same inputs ⇒ same output (zero RNG, fixed tie-breaks)");
				Assert.That(a[0] + a[1] + a[2] + a[3], Is.EqualTo(100), "total conserved");
				Assert.That(a[0], Is.EqualTo(40), "SR capped at 40% of the pool");
			});
		}
	}
}
