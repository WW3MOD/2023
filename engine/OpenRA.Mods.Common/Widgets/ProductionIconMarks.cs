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

namespace OpenRA.Mods.Common.Widgets
{
	/// <summary>
	/// The arithmetic behind the two marks a production icon still carries: the split queue badge in
	/// the top-right and the held-rank chevron in the top-left. Pure - ints and strings in, strings
	/// and bools out, with no widget, no font and no world - so every rule below is asserted in
	/// ProductionIconMarksTest instead of being eyeballed on screen.
	/// <para>Everything else the icon used to draw is gone: the per-tier rank strip moved into the
	/// production tooltip, and the lime auto-build stripe down the left edge was deleted outright
	/// because it carried the same single bit as the badge's lime half.</para>
	/// </summary>
	public static class ProductionIconMarks
	{
		/// <summary>
		/// White half of the badge: how many entries of this type were queued by hand. Null when
		/// there are none, so the caller draws nothing rather than a "0".
		/// </summary>
		public static string ManualBadgeText(int manual)
		{
			return manual > 0 ? manual.ToString() : null;
		}

		/// <summary>
		/// Lime half of the badge: how many entries of this type recycle. The prefix is only worn
		/// when there is a white half for it to join onto - a pure auto-build stack reads "2", a
		/// mixed one reads "3+2". Null when nothing recycles.
		/// </summary>
		/// <remarks>
		/// The prefix is "+" and not a recycle arrow on purpose. FreeSansBold ships no U+21BB (and no
		/// U+221E, which is why the stripe this replaces was a hand-drawn primitive), and ww3mod
		/// declares no Symbols font, so a missing glyph renders as nothing at all with the widget
		/// working perfectly. "+" is ASCII and is proven present.
		/// </remarks>
		public static string AutoBadgeText(int manual, int auto, string prefix)
		{
			if (auto <= 0)
				return null;

			return manual > 0 ? prefix + auto : auto.ToString();
		}

		/// <summary>
		/// The count beside the chevron, or null when it would say nothing worth the pixels. Two
		/// digits are reachable and must be budgeted for: RankAccumulation.StockOf returns
		/// Stock + BonusStock, and CreditWhole increments BonusStock with no cap check
		/// (RankAccumulation.cs:245), so the {3,2,1} caps bound accrual only and evacuating veterans
		/// can push a holding well past them.
		/// </summary>
		public static string RankCountText(int held, int minimum, string prefix)
		{
			return held >= minimum ? prefix + held : null;
		}

		/// <summary>
		/// Whether the count beside the chevron fits in the gap left by the queue badge, which grows
		/// leftwards from the same row. Both marks are on line 1 of a 62px cell, so they CAN meet:
		/// every TinyBold glyph here advances 6px, the count sits at x 16 and is 3 glyphs wide at two
		/// digits, and the badge is right-anchored at x 59 - so a badge of five glyphs or more
		/// ("12+10", i.e. 22 queued of one type in a mixed stack) reaches back past x 34 and lands on
		/// it. Rare, reachable, and illegible when it happens.
		/// <para>The COUNT gives way rather than the chevron: the tier is the actionable half of the
		/// mark, the depth is a stock reading that the tooltip carries in full either way, and the
		/// player looking at a 22-deep queue is not asking how many ranks they have banked. Nothing
		/// is repositioned - a mark that moves as the queue grows is worse than a mark that yields.
		/// </para>
		/// </summary>
		public static bool RankCountFits(float countLeft, int countWidth, float badgeLeft, int gap)
		{
			return countLeft + countWidth + gap <= badgeLeft;
		}

		/// <summary>
		/// Sequence name for a tier's chevron sprite. Tiers above what can be bought clamp rather
		/// than throw: rank 4 is forged in combat and never purchased, so it has no frame to name.
		/// </summary>
		public static string ChevronSequence(string prefix, int tier)
		{
			return prefix + Math.Clamp(tier, 1, Traits.RankAccrual.MaxPurchasableRank);
		}

		/// <summary>
		/// Whether the held-rank chevron may be drawn. It is suppressed whenever the centre text is
		/// up - READY, ON HOLD, or the countdown - because those states already own that reading:
		/// while a unit is mid-build, what rank the NEXT one would arrive at is not the decision
		/// being made. An icon with nothing queued always shows it, which is the common case.
		/// <para>The three flags describe the head of this type's queue and mirror, exactly, the
		/// branch order the centre text is drawn by.</para>
		/// </summary>
		public static bool ShowRankMark(bool anyQueued, bool headDone, bool headPaused, bool headWaiting, bool drawTime)
		{
			if (!anyQueued)
				return true;

			if (headDone || headPaused)
				return false;

			return headWaiting || !drawTime;
		}
	}
}
