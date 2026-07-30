#region Copyright & License Information
/*
 * WW3MOD influence stack — frontier standoff (@experimental) — rearward-push decision test.
 *
 * Pins the coordinate-agnostic step-count decision both standoff consumers (the artillery echelon anchor and
 * the attack-heli standoff) turn into a rearward walk, so "standoff units hold BEHIND the believed front line"
 * can't silently regress:
 *   (1) ALREADY CLEAR — a point already past the minimum takes zero steps (⇒ un-consumed/unpopulated field is
 *       byte-identical; no push).
 *   (2) WALK TO CLEAR — returns the first step index whose sampled frontier distance reaches the minimum.
 *   (3) BOUNDED — the budget caps the walk; an un-clearable axis returns maxSteps (push back as far as allowed).
 *   (4) DISABLED — minCells <= 0 or maxSteps <= 0 never pushes.
 * Pure integer stepping over a synthetic sampler; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FrontierStandoffMathTest
	{
		[Test]
		public void AlreadyClear_TakesZeroSteps()
		{
			// The un-pushed point (i=0) already reads at/above the minimum ⇒ no rearward walk.
			Assert.That(FrontierStandoffMath.RearwardSteps(i => 10, minCells: 4, maxSteps: 6), Is.EqualTo(0),
				"a point already behind the front is not pushed");

			// The 'far' sentinel case a populated-but-enemy-free field produces: distance huge everywhere ⇒ 0 steps.
			Assert.That(FrontierStandoffMath.RearwardSteps(i => 64, minCells: 4, maxSteps: 6), Is.EqualTo(0));
		}

		[Test]
		public void WalksToTheFirstClearingStep()
		{
			// Frontier distance grows one per step back (a point sitting on the front, i=0, reads 0).
			Assert.That(FrontierStandoffMath.RearwardSteps(i => i, minCells: 4, maxSteps: 10), Is.EqualTo(4),
				"first step whose sampled distance reaches the minimum");

			// Two-cells-per-step gradient clears the minimum of 4 at step 2.
			Assert.That(FrontierStandoffMath.RearwardSteps(i => 2 * i, minCells: 4, maxSteps: 10), Is.EqualTo(2));
		}

		[Test]
		public void Bounded_BudgetExhaustedReturnsMaxSteps()
		{
			// The axis never clears within the budget (e.g. moving 'away' didn't gain ground) ⇒ push to the cap,
			// the safe direction — never an unbounded search.
			Assert.That(FrontierStandoffMath.RearwardSteps(i => 0, minCells: 4, maxSteps: 6), Is.EqualTo(6),
				"an un-clearable axis returns the step budget, not more");
		}

		[Test]
		public void Disabled_NeverPushes()
		{
			Assert.Multiple(() =>
			{
				Assert.That(FrontierStandoffMath.RearwardSteps(i => 0, minCells: 0, maxSteps: 6), Is.EqualTo(0),
					"minCells <= 0 is off");
				Assert.That(FrontierStandoffMath.RearwardSteps(i => 0, minCells: 4, maxSteps: 0), Is.EqualTo(0),
					"a zero budget takes no step");
			});
		}
	}
}
