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
using OpenRA.Mods.Common.Widgets.Logic;

namespace OpenRA.Test
{
	[TestFixture]
	public class ProductionTooltipLayoutTest
	{
		// The chrome's authored values, so the cases below read as the real panel rather than as
		// arbitrary numbers. From engine/mods/common/chrome/tooltips.yaml, PRODUCTION_TOOLTIP.
		const int Margin = 7;          // Label@NAME X
		const int CostIconWidth = 16;  // Image@COST_ICON Width
		const int IconMargin = 3;      // Image@TIME_ICON X, which authors the icon-to-label gap

		// THE POINT OF THE WHOLE CHANGE. The cost's right edge must land the same distance in from the
		// panel's right edge as the name's left edge is from its left — that is what "shares the row"
		// means, and it is what stops a gutter reappearing.
		[TestCase(100)]
		[TestCase(1200)]
		[TestCase(99999)]
		public void CostBlockIsFlushRightAgainstTheSameMarginTheNameUsesOnTheLeft(int costWidth)
		{
			var panelWidth = ProductionTooltipLayout.PanelWidth(Margin, 0, 0, 0);
			var costLabelX = ProductionTooltipLayout.CostLabelX(panelWidth, Margin, costWidth);

			Assert.That(panelWidth - (costLabelX + costWidth), Is.EqualTo(Margin));
		}

		[Test]
		public void CostIconSitsImmediatelyLeftOfTheCostLabel()
		{
			var costLabelX = 200;
			var iconX = ProductionTooltipLayout.CostIconX(costLabelX, IconMargin, CostIconWidth);

			Assert.That(costLabelX - (iconX + CostIconWidth), Is.EqualTo(IconMargin));
		}

		// A short tooltip must not shrink-wrap, or the panel would jump about in width as the pointer
		// moves along the build palette.
		[Test]
		public void PanelHoldsItsWidthWhenEveryPieceOfContentIsNarrow()
		{
			Assert.That(ProductionTooltipLayout.PanelWidth(Margin, 10, 20, 30),
				Is.EqualTo(ProductionTooltipLayout.ContentWidth + 2 * Margin));
		}

		// ...but it must grow rather than overflow. The code this replaced ran the measurement through
		// Math.Clamp(measured, 350, 350), which discarded it, so anything wider than the panel simply
		// drew outside it. This is the regression guard for that.
		[Test]
		public void PanelGrowsToHoldContentWiderThanTheDefault()
		{
			var oversize = ProductionTooltipLayout.ContentWidth + 60;

			Assert.That(ProductionTooltipLayout.PanelWidth(Margin, oversize, 0, 0),
				Is.EqualTo(oversize + 2 * Margin));
			Assert.That(ProductionTooltipLayout.PanelWidth(Margin, 0, oversize, 0),
				Is.EqualTo(oversize + 2 * Margin));
			Assert.That(ProductionTooltipLayout.PanelWidth(Margin, 0, 0, oversize),
				Is.EqualTo(oversize + 2 * Margin));
		}

		// The name row and the cost block share a row, so the panel has to be wide enough that they do
		// not collide. Asserted as the no-overlap property directly, over a range of name widths that
		// spans both sides of the point where the name starts driving the panel width.
		[TestCase(0)]
		[TestCase(120)]
		[TestCase(200)]
		[TestCase(280)]
		[TestCase(400)]
		public void NameNeverReachesTheCostIconWhateverTheNameIsCalled(int nameWidth)
		{
			const int CostWidth = 31;   // a four-digit cost in the 14px Bold font
			const int HotkeyWidth = 0;

			var nameRowWidth = ProductionTooltipLayout.NameRowContentWidth(
				nameWidth, HotkeyWidth, CostWidth, CostIconWidth, IconMargin);
			var panelWidth = ProductionTooltipLayout.PanelWidth(Margin, nameRowWidth, 0, 0);

			var costLabelX = ProductionTooltipLayout.CostLabelX(panelWidth, Margin, CostWidth);
			var costIconX = ProductionTooltipLayout.CostIconX(costLabelX, IconMargin, CostIconWidth);

			var nameEnd = Margin + nameWidth + HotkeyWidth;

			Assert.That(costIconX - nameEnd, Is.GreaterThanOrEqualTo(ProductionTooltipLayout.NameCostGap),
				"the name must clear the cost icon by at least NameCostGap");
		}

		[Test]
		public void HotkeySuffixIsCountedAgainstTheCostBlockToo()
		{
			var withoutHotkey = ProductionTooltipLayout.NameRowContentWidth(100, 0, 31, CostIconWidth, IconMargin);
			var withHotkey = ProductionTooltipLayout.NameRowContentWidth(100, 40, 31, CostIconWidth, IconMargin);

			Assert.That(withHotkey - withoutHotkey, Is.EqualTo(40));
		}

		// The user asked for "30% smaller or so". This pins the claim to arithmetic so a later retune
		// of ContentWidth cannot quietly walk the panel back to its old size without failing here.
		//
		// Old panel, for a four-digit cost: 350 content + 31 cost + 16 icon + 3 icon gap + 3 * 7 margin.
		// The cost gutter is the part that disappears entirely; the text gives up only 70px.
		[Test]
		public void PanelIsAboutThirtyPercentNarrowerThanTheLayoutItReplaced()
		{
			const int OldContentWidth = 350;
			const int FourDigitCostWidth = 31;
			var oldPanelWidth = OldContentWidth + FourDigitCostWidth + CostIconWidth + IconMargin + 3 * Margin;

			var newPanelWidth = ProductionTooltipLayout.PanelWidth(Margin, 0, 0, 0);

			var reductionPercent = 100 * (oldPanelWidth - newPanelWidth) / oldPanelWidth;

			Assert.That(oldPanelWidth, Is.EqualTo(421), "guards the 'before' figure this claim is measured against");
			Assert.That(newPanelWidth, Is.EqualTo(294));
			Assert.That(reductionPercent, Is.InRange(27, 33));
		}

		// The content is centred in the panel: equal margins both sides. If this fails the description
		// has stopped running edge to edge and the dead band is back.
		[Test]
		public void ContentSpansThePanelWithEqualMarginsOnBothSides()
		{
			var panelWidth = ProductionTooltipLayout.PanelWidth(Margin, 0, 0, 0);
			var contentRightEdge = Margin + ProductionTooltipLayout.ContentWidth;

			Assert.That(panelWidth - contentRightEdge, Is.EqualTo(Margin));
		}

		[Test]
		public void DescriptionGetsExtraBreathingSpaceAboveIt()
		{
			Assert.That(ProductionTooltipLayout.DescriptionTopMargin, Is.Positive);
		}
	}
}
