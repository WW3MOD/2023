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
using OpenRA.Mods.Common.Traits.Render;

namespace OpenRA.Test
{
	[TestFixture]
	public class DecorationRowGeometryTest
	{
		// The bug this whole file exists to stop recurring. Position: Top negates Margin.X, so the
		// visibility diamond's authored -8 drew it 8px to the RIGHT of centre. Nothing in the YAML
		// says so and the sign reads backwards, so it is asserted here rather than remembered.
		[TestCase("Top", -8, 0, 8, 0)]
		[TestCase("Top", 8, 0, -8, 0)]
		[TestCase("Top", 0, -10, 0, -10)]
		public void TopNegatesMarginXAndPassesMarginYThrough(string pos, int marginX, int marginY, int expectX, int expectY)
		{
			var offset = SelectionDecorations.GetDecorationMargin(pos, new int2(marginX, marginY));

			Assert.That(offset.X, Is.EqualTo(expectX), "Margin.X is negated at Position: Top");
			Assert.That(offset.Y, Is.EqualTo(expectY), "Margin.Y is NOT negated at Position: Top");
		}

		// Zero is the only margin that leaves a Top decoration on the actor's centre line, because the
		// origin is already the centre and the margin is a signed offset away from it. If this ever
		// fails, "centred" has stopped meaning Margin.X: 0 and both YAML sites need revisiting.
		[Test]
		public void CentredMarginXProducesNoHorizontalOffset()
		{
			var offset = SelectionDecorations.GetDecorationMargin("Top", new int2(DecorationRowGeometry.CentredMarginX, 0));

			Assert.That(offset.X, Is.Zero);
		}

		[Test]
		public void TopLeftIsTheOnlyCornerThatLeavesBothAxesAlone()
		{
			Assert.That(SelectionDecorations.GetDecorationMargin("TopLeft", new int2(3, 4)), Is.EqualTo(new int2(3, 4)));
			Assert.That(SelectionDecorations.GetDecorationMargin("TopRight", new int2(3, 4)), Is.EqualTo(new int2(-3, 4)));
			Assert.That(SelectionDecorations.GetDecorationMargin("BottomLeft", new int2(3, 4)), Is.EqualTo(new int2(3, -4)));
			Assert.That(SelectionDecorations.GetDecorationMargin("BottomRight", new int2(3, 4)), Is.EqualTo(new int2(-3, -4)));
		}

		// A sprite decoration is centred on its origin, so half of it hangs above the origin. Screen Y
		// grows downward, so "above" is the smaller number.
		[TestCase(0, 5, -2)]
		[TestCase(-5, 6, -8)]
		public void SpritePipInkTopIsHalfTheSpriteAboveItsOrigin(int marginY, int height, int expected)
		{
			Assert.That(DecorationRowGeometry.SpritePipInkTop(marginY, height), Is.EqualTo(expected));
		}

		// The property that actually matters on screen: the glyph's lowest ink must sit exactly
		// `clearance` pixels above the pip's highest ink. Asserted as that relationship rather than as
		// a constant, so it survives a retune of any of the inputs.
		[TestCase(0, 5, 3)]
		[TestCase(-5, 6, 3)]
		[TestCase(0, 5, 0)]
		[TestCase(-5, 6, 7)]
		[TestCase(-2, 17, 4)]
		public void GlyphClearsThePipByExactlyTheRequestedGap(int pipMarginY, int pipHeight, int clearance)
		{
			var marginY = DecorationRowGeometry.GlyphMarginYAbovePip(pipMarginY, pipHeight, clearance);

			var glyphInkBottom = marginY + DecorationRowGeometry.GlyphInkBottomOffset;
			var pipInkTop = DecorationRowGeometry.SpritePipInkTop(pipMarginY, pipHeight);

			Assert.That(pipInkTop - glyphInkBottom, Is.EqualTo(clearance));
		}

		// A glyph is centred on its em box, not on its ink: WithTextDecoration draws at
		// screenPos - size/2 and SpriteFont.DrawText then puts the baseline a further `size` down, so
		// a baseline-sitting glyph hangs size/2 below the point it is nominally centred on. That
		// overhang is why the diamond read as "too far down" even before the pip was considered.
		[Test]
		public void GlyphInkHangsHalfTheFontSizeBelowItsNominalOrigin()
		{
			Assert.That(DecorationRowGeometry.GlyphInkBottomOffset, Is.EqualTo(DecorationRowGeometry.GlyphFontSize / 2));
			Assert.That(DecorationRowGeometry.GlyphInkBottomOffset, Is.Positive);
		}

		// These two are what defaults.yaml and infantry.yaml assert as literals. If a retune changes
		// them, these fail and name the new numbers to copy across -- the YAML cannot read the C#.
		[Test]
		public void ShippedDiamondMarginsMatchTheValuesAuthoredInYaml()
		{
			Assert.That(DecorationRowGeometry.VehicleDiamondMarginY, Is.EqualTo(-10),
				"^UnitIndicators in mods/ww3mod/rules/defaults.yaml authors Margin: 0,-10");
			Assert.That(DecorationRowGeometry.InfantryDiamondMarginY, Is.EqualTo(-16),
				"^SpottedPipAdjustmentInfantry in mods/ww3mod/rules/ingame/infantry.yaml authors Margin: 0,-16");
		}

		// The reason infantry need an override at all. If these ever coincide, the override is dead
		// weight and should be deleted rather than left to rot.
		[Test]
		public void InfantryNeedsItsOwnMarginBecauseItsPipSitsHigher()
		{
			Assert.That(DecorationRowGeometry.InfantryDiamondMarginY,
				Is.LessThan(DecorationRowGeometry.VehicleDiamondMarginY),
				"infantry pips sit higher, so the diamond above them must sit higher too");
		}
	}
}
