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

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Test
{
	[TestFixture]
	public class CameoCaptionsTest
	{
		// A fixed-pitch stand-in for a SpriteFont: every glyph 5px wide, every line 7px tall. Real
		// FreeSansBold is proportional, but every rule under test is about the number the measurer
		// returns and not about which glyphs produced it.
		const int GlyphWidth = 5;
		const int LineHeight = 7;

		static int2 Measure(string s)
		{
			return new int2(s.Length * GlyphWidth, LineHeight);
		}

		static string Identity(string s)
		{
			return s;
		}

		static CameoCaptionCache Cache(int slotWidth = 62, int slotHeight = 46, int sideMargin = 1, int bottomMargin = 2, int backgroundPadding = 1, Func<string, string> resolve = null)
		{
			return new CameoCaptionCache(Measure, resolve ?? Identity, slotWidth, slotHeight, sideMargin, bottomMargin, backgroundPadding);
		}

		[Test]
		public void UnsetCaptionDrawsNothing()
		{
			// The OFF state, and the state every shipped cameo is in.
			Assert.That(Cache().Get(null), Is.Null);
			Assert.That(Cache().Get(""), Is.Null);
		}

		[Test]
		public void WhitespaceOnlyCaptionDrawsNothing()
		{
			Assert.That(Cache().Get("   "), Is.Null);
		}

		[Test]
		public void FittingCaptionSurvivesIntact()
		{
			var caption = Cache().Get("50 KT");
			Assert.That(caption, Is.Not.Null);
			Assert.That(caption.Text, Is.EqualTo("50 KT"));
		}

		[Test]
		public void CaptionIsCentredAcrossTheWholeSlotNotInsideTheMargins()
		{
			// 5 glyphs = 25px in a 62px slot, so 18px of slack a side. An implementation that centred
			// inside the 60px between the margins would answer 17 and drift left.
			var caption = Cache().Get("50 KT");
			Assert.That(caption.Offset.X, Is.EqualTo((62 - 25) / 2));
		}

		[Test]
		public void CaptionSitsTheMarginAboveTheSlotBottom()
		{
			var caption = Cache().Get("50 KT");
			Assert.That(caption.Offset.Y, Is.EqualTo(46 - 2 - LineHeight));
		}

		[Test]
		public void OverlongCaptionIsShortenedRatherThanAllowedToBleed()
		{
			// 60px of room at 5px a glyph is 12 glyphs. A 62px-wide overflow would cross into the
			// neighbouring cameo, which sits one pixel away.
			var caption = Cache().Get("FLAMETHROWERS");
			Assert.That(caption, Is.Not.Null);
			Assert.That(caption.Text, Is.EqualTo("FLAMETHROWER"));
			Assert.That(Measure(caption.Text).X, Is.LessThanOrEqualTo(60));
		}

		[Test]
		public void ShorteningDropsTheTrailingSpaceItLandsOn()
		{
			// Cutting "SPEC FORCES XX" to 12 would leave "SPEC FORCES " with a hanging space, which
			// measures wider than it looks and pushes the visible text off centre.
			var caption = Cache().Get("SPEC FORCES XX");
			Assert.That(caption.Text, Is.EqualTo("SPEC FORCES"));
		}

		[Test]
		public void CaptionThatCannotFitEvenOneGlyphDrawsNothing()
		{
			Assert.That(Cache(slotWidth: 4).Get("ABC"), Is.Null);
		}

		[Test]
		public void SurroundingWhitespaceIsTrimmedBeforeMeasuring()
		{
			var caption = Cache().Get("  50 KT  ");
			Assert.That(caption.Text, Is.EqualTo("50 KT"));
		}

		[Test]
		public void ResolverRunsSoAFluentKeyBecomesItsMessage()
		{
			var cache = Cache(resolve:  s => s == "cameo-yield-50kt" ? "50 KT" : s);
			Assert.That(cache.Get("cameo-yield-50kt").Text, Is.EqualTo("50 KT"));
		}

		[Test]
		public void ResolverRunsExactlyOncePerDistinctCaption()
		{
			// Every icon re-asks for its caption on every frame; the Fluent lookup, the fitting loop
			// and the measuring must not run again for a caption already answered.
			var calls = 0;
			var cache = Cache(resolve:  s => { calls++; return s; });
			for (var i = 0; i < 10; i++)
				cache.Get("50 KT");

			Assert.That(calls, Is.EqualTo(1));
		}

		[Test]
		public void AnUnfittableCaptionIsCachedAsNullAndNotRetriedEveryFrame()
		{
			var calls = 0;
			var cache = Cache(slotWidth: 4, resolve:  s => { calls++; return s; });
			for (var i = 0; i < 10; i++)
				Assert.That(cache.Get("ABC"), Is.Null);

			Assert.That(calls, Is.EqualTo(1));
		}

		[Test]
		public void DistinctCaptionsDoNotShareACacheEntry()
		{
			// The whole point of the feature is three powers on one sprite saying three things.
			var cache = Cache();
			Assert.That(cache.Get("0.3 KT").Text, Is.EqualTo("0.3 KT"));
			Assert.That(cache.Get("10 KT").Text, Is.EqualTo("10 KT"));
			Assert.That(cache.Get("50 KT").Text, Is.EqualTo("50 KT"));
		}

		[Test]
		public void FitReturnsNullForANonPositiveWidth()
		{
			Assert.That(CameoCaptionCache.Fit("ABC", Measure, 0), Is.Null);
			Assert.That(CameoCaptionCache.Fit("ABC", Measure, -10), Is.Null);
		}
		[Test]
		public void BackgroundBandSpansTheFullSlotWidthSoABakedCaptionIsFullyCovered()
		{
			// Every shipped cameo bakes its caption edge to edge; a band only as wide as the new text
			// would leave the ends of the old word sticking out either side.
			var band = Cache().Get("50 KT").Background;
			Assert.That(band.Left, Is.EqualTo(0));
			Assert.That(band.Right, Is.EqualTo(62));
		}

		[Test]
		public void BackgroundBandPadsTheLineBoxOnBothEdges()
		{
			var band = Cache(backgroundPadding: 1).Get("50 KT").Background;
			Assert.That(band.Top, Is.EqualTo(46 - 2 - LineHeight - 1));
			Assert.That(band.Bottom, Is.EqualTo(46 - 2 + 1));
		}

		[Test]
		public void BackgroundBandIsClampedIntoTheSlot()
		{
			// A padding generous enough to reach past either edge must not, or the band would paint
			// over the art above or past the cameo's bottom bevel.
			var band = Cache(backgroundPadding: 500).Get("50 KT").Background;
			Assert.That(band.Top, Is.EqualTo(0));
			Assert.That(band.Bottom, Is.EqualTo(46));
		}
	}
}
