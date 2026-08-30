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

using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class ForestShadowTest
	{
		// Reference table for uniform fully-dense (density-10) tree cells crossed on the sightline.
		// This is the contract PIPELINE item 26 phase 1 tunes: thin line barely hides, deep cluster
		// genuinely conceals stock Vision-3 infantry from a moderate-range viewer. The generated LOS
		// cache bakes exactly these values, so changing them means bumping ShadowCache.AlgoVersion —
		// ShadowCacheKeyTermsTest.ShadowCurveMatchesTheRecordedAlgoVersion enforces that.
		[TestCase(0, 0, TestName = "No trees on the sightline → no shadow")]
		[TestCase(10, 1, TestName = "Thin 1-cell treeline barely hides (shadow 1)")]
		[TestCase(20, 2, TestName = "2 dense cells still weak (at the knee, shadow 2)")]
		[TestCase(30, 4, TestName = "3 dense cells → shadow 4 (hides Vision-3 at long range)")]
		[TestCase(40, 6, TestName = "4 dense cells → shadow 6 (hides Vision-3 at moderate range)")]
		[TestCase(50, 8, TestName = "5 dense cells → shadow 8 (hides until close)")]
		[TestCase(60, 10, TestName = "6 dense cells → deep-forest concealment (shadow 10)")]
		public void CurveMatchesReferenceTable(int crossedDensity, int expectedShadow)
		{
			Assert.That(Map.ForestGroundShadow(crossedDensity), Is.EqualTo(expectedShadow));
		}

		[TestCase(-5, TestName = "Negative density clamps to zero")]
		[TestCase(-1, TestName = "Negative density clamps to zero (boundary)")]
		public void NegativeDensityIsZero(int crossedDensity)
		{
			Assert.That(Map.ForestGroundShadow(crossedDensity), Is.EqualTo(0));
		}

		[Test(Description = "Below and at the knee the curve is pure ceil(density/10) — linear, thin lines weak.")]
		public void SubKneeIsLinearCeil()
		{
			Assert.That(Map.ForestGroundShadow(1), Is.EqualTo(1));
			Assert.That(Map.ForestGroundShadow(9), Is.EqualTo(1));
			Assert.That(Map.ForestGroundShadow(11), Is.EqualTo(2));
			Assert.That(Map.ForestGroundShadow(15), Is.EqualTo(2)); // a single T15 (density 15) tree
			Assert.That(Map.ForestGroundShadow(Map.ForestShadowKneeDensity), Is.EqualTo(2));
		}

		[Test(Description = "The curve never decreases as crossed density grows (monotonic non-decreasing).")]
		public void Monotonic()
		{
			var prev = 0;
			for (var d = 0; d <= 400; d++)
			{
				var s = Map.ForestGroundShadow(d);
				Assert.That(s, Is.GreaterThanOrEqualTo(prev), $"regressed at density {d}");
				prev = s;
			}
		}

		[Test(Description = "Just past the knee steps up by exactly 1 (2 → 3), proving the boost is smooth, not a cliff.")]
		public void KneeTransitionIsSmooth()
		{
			Assert.That(Map.ForestGroundShadow(Map.ForestShadowKneeDensity), Is.EqualTo(2));
			Assert.That(Map.ForestGroundShadow(Map.ForestShadowKneeDensity + 1), Is.EqualTo(3));
		}
	}
}
