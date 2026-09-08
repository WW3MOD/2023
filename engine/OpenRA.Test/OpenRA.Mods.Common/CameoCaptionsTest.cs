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
using OpenRA.Primitives;

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

		static CameoCaptionCache Cache(int slotWidth = 62, int slotHeight = 46, int sideMargin = 1, int bottomMargin = 2, int backgroundPadding = 1, Func<string, string> resolve = null, int badgeGap = 1)
		{
			return new CameoCaptionCache(Measure, resolve ?? Identity, slotWidth, slotHeight, sideMargin, bottomMargin, backgroundPadding, badgeGap);
		}

		// The shipped nuclear badge. Square, and taller than the 7px caption line it sits beside -
		// which is the whole reason the two are laid out together rather than each placing itself.
		static readonly int2 Badge = new(13, 13);

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
		public void NoBadgeIsTheDefaultAndChangesNothing()
		{
			// Every actor and every power in every other mod is in this state, so it has to produce
			// exactly the geometry the version of this class that had no badge in it produced.
			var caption = Cache().Get("50 KT");
			Assert.That(caption.BadgeOffset, Is.Null);
			Assert.That(caption.Offset.X, Is.EqualTo((62 - 25) / 2));
			Assert.That(caption.Offset.Y, Is.EqualTo(46 - 2 - LineHeight));
			Assert.That(caption.Background, Is.EqualTo(Rectangle.FromLTRB(0, 46 - 2 - LineHeight - 1, 62, 46 - 2 + 1)));
		}

		[Test]
		public void AZeroSizedBadgeIsTheSameAsNoBadge()
		{
			// The OFF state arrives as a zero size rather than as a flag, because a zero size is what
			// the widget has in hand when the power names no badge sequence.
			var withZero = Cache().Get("50 KT", int2.Zero);
			var without = Cache().Get("50 KT");
			Assert.That(withZero.BadgeOffset, Is.Null);
			Assert.That(withZero.Offset, Is.EqualTo(without.Offset));
		}

		[Test]
		public void ABadgeAloneIsDrawnWithNoCaptionAndNoBand()
		{
			// A power can be nuclear without stating a yield, so the badge cannot depend on there
			// being a caption to hang off. No text also means no band: the band exists to cover
			// lettering baked into the art, and nothing is being written over it here.
			var caption = Cache().Get(null, Badge);
			Assert.That(caption, Is.Not.Null);
			Assert.That(caption.Text, Is.Null);
			Assert.That(caption.BadgeOffset, Is.Not.Null);
			Assert.That(caption.Background, Is.EqualTo(Rectangle.Empty));
		}

		[Test]
		public void NeitherACaptionNorABadgeStillDrawsNothing()
		{
			Assert.That(Cache().Get(null, int2.Zero), Is.Null);
			Assert.That(Cache().Get("", int2.Zero), Is.Null);
		}

		[Test]
		public void BadgeSitsInTheBottomRightInsideTheSideMargin()
		{
			var caption = Cache().Get("50 KT", Badge);
			Assert.That(caption.BadgeOffset.Value.X, Is.EqualTo(62 - 1 - 13));
			Assert.That(caption.BadgeOffset.Value.Y, Is.EqualTo(46 - 2 - 13));
		}

		[Test]
		public void BadgeAndCaptionShareTheSameBottomEdge()
		{
			// Bottom-aligned, not centred against each other: the badge is the taller of the two and
			// simply stands further up.
			var caption = Cache().Get("50 KT", Badge);
			Assert.That(caption.BadgeOffset.Value.Y + 13, Is.EqualTo(caption.Offset.Y + LineHeight));
		}

		[Test]
		public void ATallBadgeStandsAboveTheBandRatherThanGrowingIt()
		{
			// Growing the band to badge height would black out 15 rows of a 46-row slot. The badge is
			// an opaque disc with its own dark ring, so it does not need the band underneath it.
			var caption = Cache().Get("50 KT", Badge);
			Assert.That(caption.BadgeOffset.Value.Y, Is.LessThan(caption.Background.Top));
			Assert.That(caption.Background, Is.EqualTo(Rectangle.FromLTRB(0, 46 - 2 - LineHeight - 1, 62, 46 - 2 + 1)));
		}

		[Test]
		public void BandStaysFullWidthBesideABadge()
		{
			// Its job is covering the caption baked into the art, and that runs edge to edge whether
			// or not a badge is standing on top of its right-hand end.
			var band = Cache().Get("50 KT", Badge).Background;
			Assert.That(band.Left, Is.EqualTo(0));
			Assert.That(band.Right, Is.EqualTo(62));
		}

		[Test]
		public void CaptionCentresInWhatIsLeftOnceTheBadgeHasTakenItsBlock()
		{
			// 62 wide less a 13px badge and a 1px gap leaves 48, so 25px of text centres at 11 - not
			// at the 18 it would get if it were still centred across the whole slot and half of it
			// were sitting under the badge.
			var caption = Cache().Get("50 KT", Badge);
			Assert.That(caption.Offset.X, Is.EqualTo((62 - 14 - 25) / 2));
		}

		[Test]
		public void CaptionNeverReachesUnderTheBadge()
		{
			// The property that actually matters, asserted directly rather than inferred from the
			// arithmetic, over every caption the arsenal ships.
			var shipped = new[] { "0.3 KT", "1 KT", "10 KT", "20 KT", "50 KT", "100 KT", "6x750 KT", "1.2 MT", "6 MT", "50 MT" };
			foreach (var text in shipped)
			{
				var caption = Cache().Get(text, Badge);
				Assert.That(caption.Offset.X + Measure(caption.Text).X,
					Is.LessThanOrEqualTo(caption.BadgeOffset.Value.X), text + " runs under the badge");
			}
		}

		[Test]
		public void AnOverlongCaptionIsShortenedSoonerBesideABadge()
		{
			// 60px of room becomes 46 once the badge has taken 14, so the cut lands at 9 glyphs
			// rather than 12. The badge wins the pixels and the caption gives way, because a shorter
			// yield still reads as a yield while half a trefoil reads as nothing.
			var caption = Cache().Get("FLAMETHROWER", Badge);
			Assert.That(caption.Text, Is.EqualTo("FLAMETHRO"));
			Assert.That(Measure(caption.Text).X, Is.LessThanOrEqualTo(62 - 2 - 14));
		}

		[Test]
		public void ACaptionThatCannotFitBesideABadgeStillLeavesTheBadge()
		{
			// The badge is the part that must survive: it is the difference between a nuclear weapon
			// and a conventional one, where the caption is only its size.
			var caption = Cache(slotWidth: 17).Get("ABC", Badge);
			Assert.That(caption, Is.Not.Null);
			Assert.That(caption.Text, Is.Null);
			Assert.That(caption.BadgeOffset, Is.Not.Null);
		}

		[Test]
		public void ABadgeTooBigForTheSlotIsClampedIntoIt()
		{
			var caption = Cache(slotWidth: 8, slotHeight: 8).Get(null, Badge);
			Assert.That(caption.BadgeOffset.Value.X, Is.EqualTo(0));
			Assert.That(caption.BadgeOffset.Value.Y, Is.EqualTo(0));
		}

		[Test]
		public void TheGapBetweenBadgeAndCaptionIsConfigurable()
		{
			var caption = Cache(badgeGap: 5).Get("50 KT", Badge);
			Assert.That(caption.Offset.X, Is.EqualTo((62 - 18 - 25) / 2));
		}

		[Test]
		public void TheSameWordingBesideDifferentBadgesDoesNotShareACacheEntry()
		{
			// Layout depends on the badge as well as on the text, so the badge has to be part of the
			// key. Keyed on the caption alone, whichever power drew first would fix the position of
			// every other power that says the same thing.
			var cache = Cache();
			var bare = cache.Get("50 KT");
			var badged = cache.Get("50 KT", Badge);
			Assert.That(badged.Offset.X, Is.Not.EqualTo(bare.Offset.X));
			Assert.That(bare.BadgeOffset, Is.Null);
			Assert.That(badged.BadgeOffset, Is.Not.Null);
		}

		[Test]
		public void ABadgedLayoutIsResolvedExactlyOncePerDistinctPair()
		{
			var calls = 0;
			var cache = Cache(resolve: s => { calls++; return s; });
			for (var i = 0; i < 10; i++)
				cache.Get("50 KT", Badge);

			Assert.That(calls, Is.EqualTo(1));
		}

		[Test]
		public void TheCaptionIsAnchoredToTheBottomOfTheSlotByTheMarginAlone()
		{
			// The line box's bottom edge is exactly `slotHeight - bottomMargin`, whatever the font,
			// and SpriteFont.DrawText puts the baseline on that row (it adds `size` to the position
			// it is given, and lineHeight IS size). So the last row of an all-caps caption's ink is
			// `slotHeight - bottomMargin - 1`, and the margin is the ONLY thing that moves it.
			//
			// That is what makes matching the baked lettering a measurement rather than a taste
			// call: the baked ink ends on slot row 45 of 46, so the margin has to be 0. It shipped
			// at 2, floating the text two rows clear of every baked caption beside it.
			for (var margin = 0; margin <= 4; margin++)
			{
				var caption = Cache(bottomMargin: margin).Get("50 KT");
				Assert.That(caption.Offset.Y + LineHeight, Is.EqualTo(46 - margin),
					$"line box bottom at bottomMargin {margin}");
			}
		}

		[Test]
		public void ABadgeIsAnchoredToTheSameBottomEdgeAsTheCaption()
		{
			// Whatever the margin does to the text it must do to the badge, or the two stop being
			// one bottom-edge unit the moment the anchor is retuned.
			for (var margin = 0; margin <= 4; margin++)
			{
				var caption = Cache(bottomMargin: margin).Get("50 KT", Badge);
				Assert.That(caption.BadgeOffset.Value.Y + 13, Is.EqualTo(caption.Offset.Y + LineHeight));
			}
		}

		[Test]
		public void BandReachesTheSlotBottomWhenThePaddingIsAtLeastTheBottomMargin()
		{
			// THE RULE THE SHIPPED CONFIGURATION GOT WRONG. The band exists to REPLACE the caption
			// baked into the art, so it has to cover every row that lettering occupies - and the art
			// runs to the bottom of the slot and past it. The band's bottom is
			// `slotHeight - bottomMargin + padding` clamped into the slot, so it reaches the last row
			// only when the padding is at least the margin. Asserted as the relationship rather than
			// against a literal 2, so that lowering either number is what fails.
			for (var margin = 0; margin <= 4; margin++)
			{
				var band = Cache(bottomMargin: margin, backgroundPadding: margin).Get("50 KT").Background;
				Assert.That(band.Bottom, Is.EqualTo(46), $"padding == bottomMargin == {margin}");
			}
		}

		[Test]
		public void BandStopsShortOfTheSlotBottomWhenThePaddingIsLessThanTheBottomMargin()
		{
			// The failure this pins is not hypothetical: the caption feature shipped with the engine
			// default padding of 1 against a bottom margin of 2, leaving band rows 36-44 against
			// baked ink whose last row is slot row 45, so a dotted line of the old lettering survived
			// under every runtime caption. Kept as a test so the shape of that bug is on the record
			// and the test above cannot be "fixed" by clamping the band unconditionally.
			var band = Cache(bottomMargin: 2, backgroundPadding: 1).Get("50 KT").Background;
			Assert.That(band.Bottom, Is.EqualTo(45));
			Assert.That(band.Bottom, Is.LessThan(46));
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
