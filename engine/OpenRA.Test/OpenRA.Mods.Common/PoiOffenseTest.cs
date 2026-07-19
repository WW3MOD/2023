#region Copyright & License Information
/*
 * WW3MOD PoiOffenseMath tests — POI-strategy Phase 3.
 *
 * Pure-logic tests of the offense allocator that turns a scored POI list into
 * attack axes: how many axes to open, how to split the army across them by
 * score with a minimum viable size, and the hysteresis test that keeps axes
 * sticky. Exactly like PoiScoring / GoalGuardLedger, the decision math is a pure
 * static class (PoiOffenseMath) validated here without a World — it ports verbatim
 * into a future v3 brain.
 *
 * These encode the Phase 3 invariants from the plan (§5.5, decision #3):
 *   * army splits across MULTIPLE scored axes, not one death-ball;
 *   * each axis gets a minimum viable size (no single-unit dribbles);
 *   * higher-scored axes get proportionally more units;
 *   * an existing axis is only abandoned when clearly outscored (no thrash).
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class PoiOffenseTest
	{
		// ---------- DesiredAxisCount ----------

		[Test]
		public void DesiredAxisCount_ScalesOneAxisPerUnitsPerAxis()
		{
			// 24 units, 8 per axis, plenty of POIs → 3 axes.
			Assert.That(PoiOffenseMath.DesiredAxisCount(24, 5, 8, 3, 4), Is.EqualTo(3));
			// 8 units → 1 axis.
			Assert.That(PoiOffenseMath.DesiredAxisCount(8, 5, 8, 3, 4), Is.EqualTo(1));
		}

		[Test]
		public void DesiredAxisCount_CappedByMaxAxes()
		{
			// 80 units would want 10 axes, capped at 4.
			Assert.That(PoiOffenseMath.DesiredAxisCount(80, 9, 8, 3, 4), Is.EqualTo(4));
		}

		[Test]
		public void DesiredAxisCount_CappedByPoiCount()
		{
			// 40 units want 5 axes but only 2 POIs exist → 2 axes.
			Assert.That(PoiOffenseMath.DesiredAxisCount(40, 2, 8, 3, 4), Is.EqualTo(2));
		}

		[Test]
		public void DesiredAxisCount_CappedByFundabilityAtMinSize()
		{
			// 7 units, 8-per-axis wants 1; but 3 POIs and min size 3 → at most 2 fundable.
			// byPool = max(1, 7/8) = 1, so it's 1 here — check a case where fundability bites:
			// 10 units, unitsPerAxis 2 → byPool 5, capped maxAxes 4, poiCount 4, fundable 10/3=3 → 3.
			Assert.That(PoiOffenseMath.DesiredAxisCount(10, 4, 2, 3, 4), Is.EqualTo(3));
		}

		[Test]
		public void DesiredAxisCount_ZeroWhenNoTargetsOrTooFewUnits()
		{
			Assert.That(PoiOffenseMath.DesiredAxisCount(20, 0, 8, 3, 4), Is.EqualTo(0), "no POIs → no axes");
			Assert.That(PoiOffenseMath.DesiredAxisCount(2, 5, 8, 3, 4), Is.EqualTo(0), "fewer than min size → no axis");
		}

		// ---------- AllocateProportional ----------

		[Test]
		public void AllocateProportional_SumsToTotalUnits()
		{
			var alloc = PoiOffenseMath.AllocateProportional(new long[] { 900, 300, 100 }, 20, 3);
			Assert.That(alloc.Sum(), Is.EqualTo(20));
		}

		[Test]
		public void AllocateProportional_EachFundedAxisMeetsMinSize()
		{
			var alloc = PoiOffenseMath.AllocateProportional(new long[] { 500, 400, 300 }, 12, 3);
			foreach (var a in alloc)
				Assert.That(a, Is.GreaterThanOrEqualTo(3));
		}

		[Test]
		public void AllocateProportional_HigherScoreGetsMoreUnits()
		{
			var alloc = PoiOffenseMath.AllocateProportional(new long[] { 900, 100 }, 20, 3);
			Assert.That(alloc[0], Is.GreaterThan(alloc[1]), "the higher-scored axis takes the bigger share");
			Assert.That(alloc.Sum(), Is.EqualTo(20));
		}

		[Test]
		public void AllocateProportional_DropsTailWhenCannotFundAllAtMinSize()
		{
			// 3 axes, min size 3 needs 9 units, but only 7 available → drop the weakest tail.
			var alloc = PoiOffenseMath.AllocateProportional(new long[] { 900, 500, 100 }, 7, 3);
			Assert.That(alloc.Sum(), Is.EqualTo(7));
			Assert.That(alloc[2], Is.EqualTo(0), "the lowest-scored axis is dropped when unfundable");
			Assert.That(alloc[0], Is.GreaterThanOrEqualTo(3));
			Assert.That(alloc[1], Is.GreaterThanOrEqualTo(3));
		}

		[Test]
		public void AllocateProportional_SingleAxisTakesAll()
		{
			var alloc = PoiOffenseMath.AllocateProportional(new long[] { 500 }, 15, 3);
			Assert.That(alloc, Is.EqualTo(new[] { 15 }));
		}

		[Test]
		public void AllocateProportional_DeterministicForEqualScores()
		{
			var a = PoiOffenseMath.AllocateProportional(new long[] { 400, 400, 400 }, 11, 3);
			var b = PoiOffenseMath.AllocateProportional(new long[] { 400, 400, 400 }, 11, 3);
			Assert.That(a, Is.EqualTo(b), "same inputs → same split (no RNG)");
			Assert.That(a.Sum(), Is.EqualTo(11));
			// 11 across 3 equal axes: 3 each = 9, remainder 2 to the lowest indices.
			Assert.That(a[0], Is.EqualTo(4));
			Assert.That(a[1], Is.EqualTo(4));
			Assert.That(a[2], Is.EqualTo(3));
		}

		[Test]
		public void AllocateProportional_EmptyOrZeroUnits()
		{
			Assert.That(PoiOffenseMath.AllocateProportional(new long[0], 10, 3), Is.Empty);
			Assert.That(PoiOffenseMath.AllocateProportional(new long[] { 500 }, 0, 3), Is.EqualTo(new[] { 0 }));
		}

		// ---------- ScoreBeatsByThreshold (hysteresis) ----------

		[Test]
		public void ScoreBeatsByThreshold_RequiresStrictMarginOverThreshold()
		{
			// threshold 30%: candidate must exceed current * 1.30.
			Assert.That(PoiOffenseMath.ScoreBeatsByThreshold(131, 100, 30), Is.True, "31% > 30% → swap");
			Assert.That(PoiOffenseMath.ScoreBeatsByThreshold(130, 100, 30), Is.False, "exactly 30% → sticky");
			Assert.That(PoiOffenseMath.ScoreBeatsByThreshold(120, 100, 30), Is.False, "20% < 30% → sticky");
		}

		[Test]
		public void ScoreBeatsByThreshold_ZeroThresholdIsPlainGreaterThan()
		{
			Assert.That(PoiOffenseMath.ScoreBeatsByThreshold(101, 100, 0), Is.True);
			Assert.That(PoiOffenseMath.ScoreBeatsByThreshold(100, 100, 0), Is.False);
		}

		// ---------- Integration: the "spread not death-ball" invariant ----------

		[Test]
		public void Spread_ArmySplitsAcrossMultipleAxes_NotOneClump()
		{
			// 20 units, 3 enemy POIs with distinct scores → 2 axes (20/8=2), and the
			// pool is genuinely split (neither axis holds the whole army).
			var poiCount = 3;
			var k = PoiOffenseMath.DesiredAxisCount(20, poiCount, 8, 3, 4);
			Assert.That(k, Is.EqualTo(2));

			var topScores = new long[] { 9000, 6000 }; // the top-k axis scores
			var alloc = PoiOffenseMath.AllocateProportional(topScores, 20, 3);
			Assert.That(alloc.Sum(), Is.EqualTo(20));
			Assert.That(alloc.Count(x => x >= 3), Is.EqualTo(2), "both axes are viable");
			Assert.That(alloc.Max(), Is.LessThan(20), "no single axis swallows the whole army");
		}
	}
}
