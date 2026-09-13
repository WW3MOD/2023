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

/*
 * THE MATCH TIMELINE -- one horizontal bar showing how long each phase of the match runs. This file
 * owns PIXELS; the data types are in TimelineModel and what the bands MEAN is in LobbyTimelineLogic.
 *
 * Drawn in the vocabulary of WORKSPACE/lobby/mockups/full-page-realistic.html and the documented
 * palette at mods/ww3mod/chrome/_lobby-palette.yaml.
 *
 * ==== IT IS READ-ONLY, AND THAT IS THE WHOLE OF THE 2026-09-13 CHANGE ====
 * It was a draggable control; the dropdowns lower in the panel are now the source of truth and this
 * is an overview of what they say. So:
 *
 *   - THERE IS NO HandleMouseInput OVERRIDE. Widget's base returns false, which is exactly the
 *     wanted behaviour: the click falls through to the scroll panel underneath and the bar never
 *     takes mouse focus. An override that took focus and then did nothing would swallow the wheel
 *     and make the option list below unscrollable over the bar -- which looks like a broken panel,
 *     not like a read-only one.
 *   - NOTHING HERE WRITES AN OPTION. The drag issued `option <id> <value>` on mouse-up, and every
 *     accepted order resets all clients to NotReady and posts a chat line. A read-only bar cannot
 *     do that to a lobby by accident.
 *   - IT STILL DERIVES FROM InputWidget, for IsDisabled alone: the bar dims with the rest of the
 *     panel for a non-host. Widget has no IsDisabled of its own.
 *
 * ==== A BAND TOO NARROW FOR ITS WORDS IS DRAWN BLANK ====
 * The colour still carries the span. That trade predates this rework and is kept because the
 * alternative -- text spilling across a neighbour -- is the one failure mode that makes the whole
 * row unreadable rather than merely incomplete.
 */

using System;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	// Lifted from the CSS custom properties in the approved mockup rather than re-picked by eye, so
	// the built thing and the drawing are the same colours.
	//
	// NOTE A DELIBERATE DEPARTURE from mods/ww3mod/chrome/_lobby-palette.yaml, which specifies pure
	// grayscale for lobby chrome. The band fills are chromatic on purpose: they are the one place in
	// the lobby where a span of time carries a MEANING (safe / cease-fire / conventional / nuclear /
	// over) that the ink scale alone cannot separate, which is the same informative-coding argument
	// the palette file already accepts for the option chips and the supply amber. The axis and every
	// label stay on the documented gray scale.
	public static class TimelinePalette
	{
		public static readonly Color TrackFill = Color.FromArgb(0x10, 0x10, 0x10);
		public static readonly Color TrackBevelDark = Color.FromArgb(0x03, 0x03, 0x03);
		public static readonly Color TrackBevelLight = Color.FromArgb(0x2a, 0x2a, 0x2a);

		public static readonly Color NoRushFill = Color.FromArgb(0x1b, 0x27, 0x33);
		public static readonly Color NoRushInk = Color.FromArgb(0x8f, 0xb4, 0xc8);
		public static readonly Color PeaceFill = Color.FromArgb(0x14, 0x1c, 0x18);
		public static readonly Color PeaceInk = Color.FromArgb(0x7e, 0x9a, 0x8c);
		public static readonly Color ConventionalFill = Color.FromArgb(0x18, 0x18, 0x18);
		public static readonly Color ConventionalInk = Color.FromArgb(0x7a, 0x7a, 0x7a);
		public static readonly Color WarheadFill = Color.FromArgb(0x2a, 0x20, 0x13);
		public static readonly Color WarheadInk = Color.FromArgb(0xd0, 0xa8, 0x60);
		public static readonly Color EndingFill = Color.FromArgb(0x2a, 0x14, 0x12);
		public static readonly Color EndingInk = Color.FromArgb(0xc8, 0x80, 0x70);

		/// <summary>The hatch drawn over an indeterminate band, one shade up from its own fill.</summary>
		public static readonly Color Hatch = Color.FromArgb(0x2b, 0x38, 0x32);

		public static readonly Color Axis = Color.FromArgb(0x26, 0x26, 0x26);
		public static readonly Color Tick = Color.FromArgb(0x5e, 0x5e, 0x5e);
		public static readonly Color Boundary = Color.FromArgb(0x05, 0x05, 0x05);

		// accent / ink-3 / ink from _lobby-palette.yaml, plus the sanctioned supply amber and the
		// active-changes chip red -- the palette file's own two informative colours, reused rather
		// than re-picked.
		public static readonly Color Highlight = Color.FromArgb(0xc8, 0xa4, 0x5a);
		public static readonly Color Micro = Color.FromArgb(0x68, 0x68, 0x68);
		public static readonly Color Value = Color.FromArgb(0xff, 0xff, 0xff);
		public static readonly Color Warning = Color.FromArgb(0xe7, 0x7d, 0x7d);
	}

	public class TimelineWidget : InputWidget
	{
		public string NoteFont = "TinyBold";
		public string BandFont = "TinyBold";
		public string DetailFont = "Tiny";
		public string TickFont = "Tiny";

		// The mockup's own measurements, in the units it was drawn in.
		public int TrackY = 20;
		public int TrackHeight = 56;
		public int BandHeight = 34;
		public int DetailY = 80;

		public string NoteLabel = "THE MATCH";
		public string AxisSuffix = " min";

		public Func<TimelineLayout> GetLayout = () => TimelineLayout.Empty;

		const int HatchSpacing = 6;

		public TimelineWidget() { }

		// REQUIRED, BOTH OF THEM. Widget.cs:256-259 throws InvalidOperationException from the base
		// Clone() for any widget type that does not override it, and the lobby clones row templates.
		public TimelineWidget(TimelineWidget other)
			: base(other)
		{
			NoteFont = other.NoteFont;
			BandFont = other.BandFont;
			DetailFont = other.DetailFont;
			TickFont = other.TickFont;
			TrackY = other.TrackY;
			TrackHeight = other.TrackHeight;
			BandHeight = other.BandHeight;
			DetailY = other.DetailY;
			NoteLabel = other.NoteLabel;
			AxisSuffix = other.AxisSuffix;
			GetLayout = other.GetLayout;
		}

		public override Widget Clone() { return new TimelineWidget(this); }

		Rectangle TrackRect => new(RenderBounds.X, RenderBounds.Y + TrackY, RenderBounds.Width, TrackHeight);

		public override void Draw()
		{
			if (!IsVisible())
				return;

			var layout = GetLayout();

			// NOTHING TO DRAW YET, WHICH IS NOT THE SAME AS NOT BEING VISIBLE. The widget stays visible
			// with no bands so that Widget.TickOuter keeps ticking its ChromeLogic -- that method ticks
			// LogicObjects only inside `if (IsVisible())` (Widget.cs:512-524), so a widget that hid
			// itself while empty could never be refilled by its own logic. Drawing nothing here gives
			// the same blank panel an invisible widget would, without the latch.
			if (layout.Bands.Count == 0)
				return;

			var track = TrackRect;

			DrawNote(layout);
			DrawTrack(track);
			DrawBands(layout, track);

			if (layout.ShowRuler)
				DrawRuler(track, layout.AxisSeconds);
		}

		void DrawNote(TimelineLayout layout)
		{
			var font = Game.Renderer.Fonts[NoteFont];
			var label = NoteLabel.ToUpperInvariant() + "   ·   ";
			font.DrawText(label, new float2(RenderBounds.X, RenderBounds.Y), TimelinePalette.Micro);

			// THE WARNING REPLACES THE HINT RATHER THAN JOINING IT. The two occupy one row, and a
			// warning drawn after a hint is a warning the eye reads second; there is nothing on the
			// hint worth keeping while the host's clocks contradict each other.
			var trailing = layout.Warning ?? layout.Hint;
			if (string.IsNullOrEmpty(trailing))
				return;

			var ink = layout.Warning != null ? TimelinePalette.Warning : TimelinePalette.Highlight;
			font.DrawText(trailing, new float2(RenderBounds.X + font.Measure(label).X, RenderBounds.Y), ink);
		}

		// An INSET bevel, which is the opposite pairing to the raised one in _lobby-palette.yaml: the
		// track is a channel the bands sit in, so the dark edge is top-left. The mockup draws it this
		// way and it is the only inset surface in the panel.
		static void DrawTrack(Rectangle track)
		{
			WidgetUtils.FillRectWithColor(track, TimelinePalette.TrackFill);
			WidgetUtils.FillRectWithColor(new Rectangle(track.X, track.Y, track.Width, 1), TimelinePalette.TrackBevelDark);
			WidgetUtils.FillRectWithColor(new Rectangle(track.X, track.Y, 1, track.Height), TimelinePalette.TrackBevelDark);
			WidgetUtils.FillRectWithColor(new Rectangle(track.Right - 1, track.Y, 1, track.Height), TimelinePalette.TrackBevelLight);
			WidgetUtils.FillRectWithColor(new Rectangle(track.X, track.Bottom - 1, track.Width, 1), TimelinePalette.TrackBevelLight);
		}

		// Diagonal 1px bars, drawn as a column stagger rather than as real lines: WidgetUtils has no
		// line primitive and a per-pixel loop over a 34px band is cheaper to read than to optimise.
		// The point is only that the eye reads "this span is not measured", so the texture matters
		// more than the angle.
		static void DrawHatch(Rectangle rect)
		{
			for (var x = rect.X - rect.Height; x < rect.Right; x += HatchSpacing)
			{
				for (var y = 0; y < rect.Height; y++)
				{
					var px = x + y;
					if (px < rect.X || px >= rect.Right)
						continue;

					WidgetUtils.FillRectWithColor(new Rectangle(px, rect.Y + y, 1, 1), TimelinePalette.Hatch);
				}
			}
		}

		void DrawBands(TimelineLayout layout, Rectangle track)
		{
			var bandFont = Game.Renderer.Fonts[BandFont];
			var detailFont = Game.Renderer.Fonts[DetailFont];
			var detailY = RenderBounds.Y + DetailY;

			foreach (var band in layout.Bands)
			{
				var from = TimelineModel.PxFromSeconds(band.FromSeconds, layout.AxisSeconds, track.Width);
				var to = TimelineModel.PxFromSeconds(band.ToSeconds, layout.AxisSeconds, track.Width);
				var width = to - from;
				if (width <= 0)
					continue;

				var rect = new Rectangle(track.X + from, track.Y, width, BandHeight);
				WidgetUtils.FillRectWithColor(rect, band.Fill);

				if (band.Indeterminate)
					DrawHatch(rect);

				// A hairline between bands, so two spans of similar value still read as two spans.
				// Not drawn at the left edge, where it would double the track's own inset bevel.
				if (from > 0)
					WidgetUtils.FillRectWithColor(new Rectangle(rect.X, rect.Y, 1, BandHeight), TimelinePalette.Boundary);

				DrawCentred(bandFont, band.GetCaption?.Invoke(), rect, rect.Y + ((BandHeight - bandFont.Measure("X").Y) / 2), band.Ink);
				DrawCentred(detailFont, band.GetDetail?.Invoke(), rect, detailY, TimelinePalette.Value);
			}
		}

		// Centred in its own band and suppressed when the band cannot hold it.
		//
		// THE TWO ROWS ARE RESOLVED INDEPENDENTLY, and a comment here used to claim the opposite --
		// that a band carries both of its lines or neither. It never did: each row measures its own
		// string, and the caption is always the shorter one, so the reachable outcome is a band that
		// keeps its NAME and drops its detail. The 2026-09-13 lobby capture shows exactly that on
		// CEASE-FIRE. Independent is also the behaviour worth keeping -- suppressing a caption that
		// fits, because the detail under it does not, would throw away the more important of the two.
		static void DrawCentred(SpriteFont font, string text, Rectangle band, int y, Color ink)
		{
			if (string.IsNullOrEmpty(text))
				return;

			var size = font.Measure(text);
			if (size.X + TimelineModel.LabelGap > band.Width)
				return;

			font.DrawText(text, new float2(band.X + ((band.Width - size.X) / 2), y), ink);
		}

		void DrawRuler(Rectangle track, int axisSeconds)
		{
			var font = Game.Renderer.Fonts[TickFont];
			var axisY = track.Y + BandHeight;
			WidgetUtils.FillRectWithColor(new Rectangle(track.X, axisY, track.Width, 1), TimelinePalette.Axis);

			for (var seconds = 0; seconds <= axisSeconds; seconds += TimelineModel.TickSeconds)
			{
				var label = (seconds / 60).ToString(System.Globalization.CultureInfo.InvariantCulture);
				if (seconds == axisSeconds)
					label += AxisSuffix;

				var size = font.Measure(label);
				var x = track.X + TimelineModel.PxFromSeconds(seconds, axisSeconds, track.Width) - (size.X / 2);

				// Clamped so the first and last labels stay inside the track instead of hanging off
				// its ends, which is what the mockup's translateX(-50%) does at the edges.
				x = Math.Clamp(x, track.X + 2, track.Right - size.X - 2);
				font.DrawText(label, new float2(x, axisY + 3), TimelinePalette.Tick);
			}
		}
	}
}
