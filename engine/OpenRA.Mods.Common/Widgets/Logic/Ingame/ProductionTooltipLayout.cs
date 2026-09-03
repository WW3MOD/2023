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

namespace OpenRA.Mods.Common.Widgets.Logic
{
	/// <summary>
	/// Width and offset arithmetic for the production tooltip panel. Pure — it touches no widget and
	/// no font, so every rule below is asserted in ProductionTooltipLayoutTest rather than eyeballed
	/// on screen.
	///
	/// WHAT THIS REPLACED. The panel used to be a LEFT COLUMN of fixed width with a RIGHT COLUMN
	/// beside it holding the cost, and the right column's width was added to the panel's. Two things
	/// followed. First, the cost gutter inset everything below it: the description and the stat rows
	/// wrapped at the left column's width and stopped short of the panel edge, leaving a band of dead
	/// space down the right of every tooltip. Second, the panel paid for that gutter over its whole
	/// height while only the top ~26px of it was ever drawn in — and after the build-time clock was
	/// removed the gutter held a single cost icon.
	///
	/// The cost now shares the name's row, flush right, and the content spans the full panel. That is
	/// where most of the width saving comes from: the gutter is deleted outright rather than squeezed,
	/// so the text itself only gives up <see cref="ContentWidth"/> against the old 350.
	/// </summary>
	public static class ProductionTooltipLayout
	{
		/// <summary>
		/// Width available to the description, the stat rows and the name — i.e. the panel less its
		/// two side margins. This is the wrap width, so it is also the knob for "the tooltip is too
		/// wide" / "the text is too cramped"; nothing else needs to change with it.
		///
		/// 280 against the previous 350. Combined with dropping the cost gutter this takes the whole
		/// panel from about 421px to 294px, a shade over the 30% reduction that was asked for:
		///   was: 350 content + ~31 cost text + 16 cost icon + 3 icon gap + 21 (three 7px margins)
		///   now: 280 content + 14 (two 7px margins)
		/// The ~31 is a four-digit cost measured in the 14px Bold font, so the "before" figure moves a
		/// few pixels with the widest cost on screen; the "after" figure does not move at all.
		/// </summary>
		public const int ContentWidth = 280;

		/// <summary>
		/// Blank pixels held between the end of the name (or its hotkey suffix) and the start of the
		/// cost icon, now that the two share a row. Only bites on an actor whose name is long enough
		/// to reach the cost; below that the gap is whatever the row has spare.
		/// </summary>
		public const int NameCostGap = 12;

		/// <summary>
		/// Extra blank pixels above the description block, on top of the Y the chrome authors. The
		/// name row and the description used to butt up against each other because the description
		/// began immediately below the name label's box.
		/// </summary>
		public const int DescriptionTopMargin = 4;

		/// <summary>
		/// Width the name row needs to hold the name, its optional hotkey suffix and the cost block
		/// without them touching. Excludes the panel's outer margins — it is a CONTENT width, directly
		/// comparable with <see cref="ContentWidth"/>.
		/// </summary>
		public static int NameRowContentWidth(int nameWidth, int hotkeyWidth, int costWidth, int costIconWidth, int iconMargin)
		{
			return nameWidth + hotkeyWidth + NameCostGap + costIconWidth + iconMargin + costWidth;
		}

		/// <summary>
		/// Overall panel width: the widest thing it must hold, plus a margin each side.
		///
		/// Takes a max rather than clamping to a constant. The code this replaced wrote
		/// <c>Math.Clamp(measured, 350, 350)</c> — equal bounds, so the measurement was computed and
		/// then discarded, and any content wider than the panel silently overflowed it instead of
		/// widening it. Long names and long prerequisite lists now push the panel out instead.
		/// </summary>
		public static int PanelWidth(int margin, params int[] contentWidths)
		{
			var widest = ContentWidth;
			foreach (var w in contentWidths)
				widest = Math.Max(widest, w);

			return widest + 2 * margin;
		}

		/// <summary>
		/// X of the cost label, so that its right edge sits exactly <paramref name="margin"/> in from
		/// the panel's right edge — mirroring the name's left margin.
		/// </summary>
		public static int CostLabelX(int panelWidth, int margin, int costWidth)
		{
			return panelWidth - margin - costWidth;
		}

		/// <summary>X of the cost icon, immediately left of the cost label.</summary>
		public static int CostIconX(int costLabelX, int iconMargin, int costIconWidth)
		{
			return costLabelX - iconMargin - costIconWidth;
		}
	}
}
