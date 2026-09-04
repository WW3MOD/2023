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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets.Logic;

namespace OpenRA.Test
{
	/// <summary>
	/// The production icon shows one chevron for the highest tier banked and nothing else. The depth
	/// behind it did not vanish when it left the cameo — it moved here, and these fixtures are what
	/// say so.
	/// </summary>
	[TestFixture]
	public class ProductionTooltipRankRowsTest
	{
		static string[] Values(int[] held)
		{
			return ProductionTooltipLogic.BankedRankRows(held)
				.Where(e => e.Kind == TooltipElementKind.StatRow)
				.Select(e => e.Label + "=" + e.Value)
				.ToArray();
		}

		[Test]
		public void ATypeHoldingNothingContributesNoRowsAtAll()
		{
			Assert.That(ProductionTooltipLogic.BankedRankRows(new[] { 0, 0, 0 }), Is.Empty);
		}

		[Test]
		public void TheDepthTheIconGaveUpIsHere()
		{
			// "I am three deep in rank-1s" — the exact reading the cameo can no longer show, because
			// only the highest tier is drawn there and that tier is all a purchase can spend.
			Assert.That(Values(new[] { 3, 0, 0 }), Is.EqualTo(new[] { "Rank 1=3" }));
		}

		[Test]
		public void EveryHeldTierGetsARowHighestFirst()
		{
			Assert.That(Values(new[] { 3, 2, 1 }),
				Is.EqualTo(new[] { "Rank 3=1", "Rank 2=2", "Rank 1=3" }));
		}

		[Test]
		public void EmptyTiersBelowAHeldOneAreSkipped()
		{
			Assert.That(Values(new[] { 0, 0, 1 }), Is.EqualTo(new[] { "Rank 3=1" }));
			Assert.That(Values(new[] { 2, 0, 1 }), Is.EqualTo(new[] { "Rank 3=1", "Rank 1=2" }));
		}

		[Test]
		public void CountsAreNotClampedToTheAccrualCaps()
		{
			// Caps are {3,2,1} and bound the wall-clock grant only; evacuation credit is uncapped.
			Assert.That(Values(new[] { 14, 0, 0 }), Is.EqualTo(new[] { "Rank 1=14" }));
		}

		[Test]
		public void TheBlockNamesTheTierThatWouldBeSpent()
		{
			var prose = ProductionTooltipLogic.BankedRankRows(new[] { 3, 2, 0 })
				.Single(e => e.Kind == TooltipElementKind.Prose);

			// Agrees with RankAccrual.HighestHeldTier, which is what the purchase path itself spends
			// and what the icon's single chevron shows.
			Assert.That(prose.Label, Does.Contain("rank 2"));
		}

		[Test]
		public void TheBlockOpensWithAGapAndASubhead()
		{
			var rows = ProductionTooltipLogic.BankedRankRows(new[] { 1, 0, 0 });
			Assert.That(rows[0].Kind, Is.EqualTo(TooltipElementKind.SectionGap));
			Assert.That(rows[1].Kind, Is.EqualTo(TooltipElementKind.Subhead));
		}
	}
}
