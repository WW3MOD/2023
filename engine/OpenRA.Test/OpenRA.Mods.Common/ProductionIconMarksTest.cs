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
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Test
{
	[TestFixture]
	public class ProductionIconMarksTest
	{
		const string AutoPrefix = "+";
		const string CountPrefix = "×";
		const int CountMinimum = 2;
		const string SequencePrefix = "rank";

		#region The split queue badge

		[Test]
		public void AnEmptyQueueDrawsNoBadgeAtAll()
		{
			Assert.That(ProductionIconMarks.ManualBadgeText(0), Is.Null);
			Assert.That(ProductionIconMarks.AutoBadgeText(0, 0, AutoPrefix), Is.Null);
		}

		[Test]
		public void APurelyManualStackIsOneWhiteNumber()
		{
			Assert.That(ProductionIconMarks.ManualBadgeText(3), Is.EqualTo("3"));
			Assert.That(ProductionIconMarks.AutoBadgeText(3, 0, AutoPrefix), Is.Null);
		}

		[Test]
		public void ALoneManualEntryStillGetsItsOne()
		{
			// The badge this replaced only drew a number at 2 or more unless the type was waiting, so
			// a single order that was actively building showed nothing. With the two halves now
			// carrying different meanings, "1" and "+1" have to be told apart at a count of one.
			Assert.That(ProductionIconMarks.ManualBadgeText(1), Is.EqualTo("1"));
			Assert.That(ProductionIconMarks.AutoBadgeText(0, 1, AutoPrefix), Is.EqualTo("1"));
		}

		[Test]
		public void APurelyAutoBuildStackWearsNoPlus()
		{
			// Nothing for the "+" to join onto - "+2" alone would read as an increment on a hidden
			// number rather than as "two of these recycle".
			Assert.That(ProductionIconMarks.AutoBadgeText(0, 2, AutoPrefix), Is.EqualTo("2"));
			Assert.That(ProductionIconMarks.ManualBadgeText(0), Is.Null);
		}

		[Test]
		public void AMixedStackReadsAsThreePlusTwo()
		{
			// The case the shipped UI could not state at all: five entries of one type, three of them
			// one-shot and two recycling, drawn as one lime "2" beside a white "3".
			Assert.That(ProductionIconMarks.ManualBadgeText(3), Is.EqualTo("3"));
			Assert.That(ProductionIconMarks.AutoBadgeText(3, 2, AutoPrefix), Is.EqualTo("+2"));
		}

		[Test]
		public void BadgeHalvesStayNumericIntoDoubleFigures()
		{
			Assert.That(ProductionIconMarks.ManualBadgeText(12), Is.EqualTo("12"));
			Assert.That(ProductionIconMarks.AutoBadgeText(12, 10, AutoPrefix), Is.EqualTo("+10"));
		}

		[Test]
		public void NegativeCountsDrawNothingRatherThanAMinusSign()
		{
			Assert.That(ProductionIconMarks.ManualBadgeText(-1), Is.Null);
			Assert.That(ProductionIconMarks.AutoBadgeText(-1, -1, AutoPrefix), Is.Null);
		}

		#endregion

		#region The rank count beside the chevron

		[Test]
		public void OneBankedRankIsTheChevronAlone()
		{
			// A lone chevron already says "one banked", and a digit that appears and disappears makes
			// the mark's width jump every time a rank lands.
			Assert.That(ProductionIconMarks.RankCountText(1, CountMinimum, CountPrefix), Is.Null);
			Assert.That(ProductionIconMarks.RankCountText(0, CountMinimum, CountPrefix), Is.Null);
		}

		[Test]
		public void TwoOrMoreBankedRanksGetACount()
		{
			Assert.That(ProductionIconMarks.RankCountText(2, CountMinimum, CountPrefix), Is.EqualTo("×2"));
			Assert.That(ProductionIconMarks.RankCountText(3, CountMinimum, CountPrefix), Is.EqualTo("×3"));
		}

		[Test]
		public void TheCountBudgetsTwoDigits()
		{
			// StockOf returns Stock + BonusStock and CreditWhole increments BonusStock with no cap
			// check, so the {3,2,1} caps bound accrual only: bringing veterans home alive can push a
			// holding well past them. Asserted here as a display rule and in
			// RankAccrualTest.EvacuationCreditPushesAHoldingIntoDoubleFigures as a simulation one.
			Assert.That(ProductionIconMarks.RankCountText(12, CountMinimum, CountPrefix), Is.EqualTo("×12"));
			Assert.That(ProductionIconMarks.RankCountText(99, CountMinimum, CountPrefix).Length, Is.EqualTo(3));
		}

		#endregion

		#region The count yielding to the badge

		// The shipped cell, so the cases below read as the real icon. IconSize 62,46 with
		// countRightAnchor = 62 - 3 (ingame-player.yaml:1180); the chevron ink is 14 wide inset 1
		// from the left with a 1px gap after it, putting the count at 16; every TinyBold glyph in
		// FreeSansBold advances 6px (verified against the font's hmtx, not assumed).
		const float BadgeAnchor = 59;
		const float CountLeft = 16;
		const int Glyph = 6;
		const int BadgeGap = 1;

		static bool Fits(int countGlyphs, int badgeGlyphs)
		{
			return ProductionIconMarks.RankCountFits(
				CountLeft, countGlyphs * Glyph, BadgeAnchor - badgeGlyphs * Glyph, BadgeGap);
		}

		[Test]
		public void TheOrdinaryCaseHasRoomToSpare()
		{
			// "x3" beside a "3+2" badge: 5 blank columns between them.
			Assert.That(Fits(2, 3), Is.True);

			// And with no badge at all there is the whole cell.
			Assert.That(Fits(3, 0), Is.True);
		}

		[Test]
		public void TwoDigitsStillFitBesideAFourGlyphBadge()
		{
			// "x12" ends at column 33 and a four-glyph badge starts at 35. Exactly one column clear,
			// which is the gap asked for — so this must not be treated as a collision.
			Assert.That(Fits(3, 4), Is.True);
		}

		[Test]
		public void AFiveGlyphBadgeTakesTheCountsColumns()
		{
			// "12+10" — 22 of one type queued in a mixed stack — reaches back to column 29, and a
			// two-digit count runs to 33. The count gives way; the chevron does not.
			Assert.That(Fits(3, 5), Is.False);
		}

		[Test]
		public void ASingleDigitCountSurvivesALongerBadge()
		{
			// The narrower the count, the deeper a queue it tolerates: "x3" only yields at six.
			Assert.That(Fits(2, 5), Is.True);
			Assert.That(Fits(2, 6), Is.False);
		}

		#endregion

		#region Which chevron sprite

		[TestCase(1, "rank1")]
		[TestCase(2, "rank2")]
		[TestCase(3, "rank3")]
		public void EachPurchasableTierNamesItsOwnSequence(int tier, string expected)
		{
			Assert.That(ProductionIconMarks.ChevronSequence(SequencePrefix, tier), Is.EqualTo(expected));
		}

		[Test]
		public void TiersOffTheEndClampInsteadOfNamingAMissingSequence()
		{
			// Rank 4 is forged in combat and never purchased, so iconchevrons frame 3 - the star - is
			// exposed by no sequence. A tier that somehow arrives out of range must not resolve to a
			// name the mod does not declare, because that throws when the sprite is first fetched.
			Assert.That(ProductionIconMarks.ChevronSequence(SequencePrefix, 4), Is.EqualTo("rank3"));
			Assert.That(ProductionIconMarks.ChevronSequence(SequencePrefix, 0), Is.EqualTo("rank1"));
			Assert.That(ProductionIconMarks.ChevronSequence(SequencePrefix, -7), Is.EqualTo("rank1"));
		}

		#endregion

		#region When the chevron is suppressed

		[Test]
		public void NothingQueuedAlwaysShowsTheChevron()
		{
			// The common case, and the one the mark exists for: an idle icon whose only overlay is
			// the rank you would get if you clicked it now.
			Assert.That(ProductionIconMarks.ShowRankMark(false, false, false, false, true), Is.True);
		}

		[Test]
		public void ReadyAndOnHoldOwnTheSpace()
		{
			Assert.That(ProductionIconMarks.ShowRankMark(true, true, false, false, true), Is.False);
			Assert.That(ProductionIconMarks.ShowRankMark(true, false, true, false, true), Is.False);

			// Done wins over paused, the same way the centre text's branch order does.
			Assert.That(ProductionIconMarks.ShowRankMark(true, true, true, false, true), Is.False);
		}

		[Test]
		public void ACountdownOwnsTheSpace()
		{
			Assert.That(ProductionIconMarks.ShowRankMark(true, false, false, false, true), Is.False);
		}

		[Test]
		public void AQueuedTypeWaitingItsTurnShowsTheChevron()
		{
			// The FIFO is global per tab and only Queue[0] ticks, so a type queued behind another
			// draws no countdown - nothing is contesting the corner, and what rank the next one
			// arrives at is still a live question.
			Assert.That(ProductionIconMarks.ShowRankMark(true, false, false, true, true), Is.True);
		}

		[Test]
		public void AModThatDrawsNoTimeLeavesTheCornerFree()
		{
			// DrawTime is a widget field. With it off there is no countdown to collide with, so the
			// suppression has nothing to protect and must not fire - otherwise every actively
			// building icon would silently lose its chevron for no reason at all.
			Assert.That(ProductionIconMarks.ShowRankMark(true, false, false, false, false), Is.True);

			// READY and ON HOLD are drawn regardless of DrawTime, so those two still suppress.
			Assert.That(ProductionIconMarks.ShowRankMark(true, true, false, false, false), Is.False);
			Assert.That(ProductionIconMarks.ShowRankMark(true, false, true, false, false), Is.False);
		}

		#endregion
	}
}
