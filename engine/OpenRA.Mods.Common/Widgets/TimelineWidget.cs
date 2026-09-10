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
 * THE MATCH TIMELINE -- one horizontal bar for the whole match, with draggable markers, replacing a
 * row of dropdowns a host previously had to combine mentally. This file owns PIXELS; the arithmetic
 * and the data types are in TimelineModel, and what the markers MEAN is in LobbyTimelineLogic.
 *
 * Drawn 1:1 against WORKSPACE/mockups/lobby-settings-panel.html (the .tlrow block), which is itself
 * a measured reproduction of the shipped chrome rather than a concept sketch.
 *
 * ==== WHY THIS IS NOT SliderWidget, WHICH EXISTS AND IS GENERAL ====
 * Two traps, both read in its source rather than assumed:
 *   1. SliderWidget.Draw() calls UpdateValue(GetValue()) EVERY FRAME (SliderWidget.cs:118). GetValue
 *      is the authority, so a port that read the synced lobby value would snap the thumb back to the
 *      server's value in the middle of a drag -- the drag would be unusable over a network.
 *   2. Its Ticks field is DRAW-ONLY (:128-135). It renders tickmarks and does not quantise Value,
 *      and there is an in-source TODO at :74 conceding that snapping was never reimplemented. So
 *      snapping had to be written from scratch either way, and snapping is the whole feature here:
 *      see TimelineModel for why a free continuous value is not merely ugly but throws on client
 *      join.
 * The focus take/yield below IS modelled on SliderWidget.cs:58-86, which is the part of it that is
 * right.
 *
 * ==== THE ORDER IS ISSUED ON MOUSE-UP, NEVER ON DRAG-MOVE ====
 * Every accepted `option` order resets ALL clients to NotReady and posts a settings-changed chat
 * line (LobbyCommands.cs). A drag that emitted per move would do that dozens of times a second: the
 * lobby would fill with chat and nobody could ever stay ready long enough to start. So a drag moves
 * a LOCAL index only, and exactly one order is issued when the button comes up. There is no
 * GetValue readback anywhere in Draw() -- that absence is the fix for trap 1 above, so do not add
 * one "for consistency".
 *
 * Prediction closes the remaining gap: the logic's OnSet predicts the new value the way the lobby
 * CHECKBOX does (LobbyOptionsLogic.cs:562) rather than the way the dropdown does, which deliberately
 * does not predict. A dropdown can afford the round trip because its label is closed while it waits;
 * a marker the host is still looking at would visibly jump back and then forward again.
 */

using System;
using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	// Lifted from the CSS custom properties in lobby-settings-panel.html rather than re-picked by
	// eye, so the built thing and the approved drawing are the same colours.
	//
	// NOTE A DELIBERATE DEPARTURE from mods/ww3mod/chrome/_lobby-palette.yaml, which specifies pure
	// grayscale for lobby chrome. The four band fills are chromatic on purpose: they are the one
	// place in the lobby where a span of time carries a MEANING (safe / conventional / nuclear /
	// over) that the ink scale alone cannot separate, which is the same informative-coding argument
	// the palette file already accepts for the option chips and the supply amber. The markers, the
	// axis and every label stay on the documented gray scale.
	public static class TimelinePalette
	{
		public static readonly Color TrackFill = Color.FromArgb(0x10, 0x10, 0x10);
		public static readonly Color TrackBevelDark = Color.FromArgb(0x03, 0x03, 0x03);
		public static readonly Color TrackBevelLight = Color.FromArgb(0x2a, 0x2a, 0x2a);

		public static readonly Color NoRushFill = Color.FromArgb(0x1b, 0x27, 0x33);
		public static readonly Color NoRushInk = Color.FromArgb(0x8f, 0xb4, 0xc8);
		public static readonly Color ConventionalFill = Color.FromArgb(0x18, 0x18, 0x18);
		public static readonly Color ConventionalInk = Color.FromArgb(0x7a, 0x7a, 0x7a);
		public static readonly Color WarheadFill = Color.FromArgb(0x2a, 0x20, 0x13);
		public static readonly Color WarheadInk = Color.FromArgb(0xd0, 0xa8, 0x60);
		public static readonly Color EndingFill = Color.FromArgb(0x2a, 0x14, 0x12);
		public static readonly Color EndingInk = Color.FromArgb(0xc8, 0x80, 0x70);

		public static readonly Color Axis = Color.FromArgb(0x26, 0x26, 0x26);
		public static readonly Color Tick = Color.FromArgb(0x5e, 0x5e, 0x5e);

		// accent / ink-3 / ink from _lobby-palette.yaml, and the sanctioned supply amber for the
		// marker the mockup draws in gold.
		public static readonly Color Marker = Color.FromArgb(0xb4, 0xb4, 0xb4);
		public static readonly Color MarkerHighlight = Color.FromArgb(0xc8, 0xa4, 0x5a);
		public static readonly Color MarkerDisabled = Color.FromArgb(0x68, 0x68, 0x68);
		public static readonly Color MarkerEdge = Color.FromArgb(0x05, 0x05, 0x05);
		public static readonly Color Micro = Color.FromArgb(0x68, 0x68, 0x68);
		public static readonly Color Value = Color.FromArgb(0xff, 0xff, 0xff);
		public static readonly Color Placeholder = Color.FromArgb(0x96, 0x96, 0x96);
	}

	public class TimelineWidget : InputWidget
	{
		public string NoteFont = "TinyBold";
		public string BandFont = "TinyBold";
		public string CaptionFont = "TinyBold";
		public string ValueFont = "Regular";
		public string TickFont = "Tiny";

		// The mockup's own measurements, in the units it was drawn in.
		public int TrackY = 20;
		public int TrackHeight = 56;
		public int BandHeight = 34;
		public int CaptionY = 80;

		public string NoteLabel = "THE MATCH";
		public string NoteHint = "drag either marker";
		public string AxisSuffix = " min";

		public Func<IReadOnlyList<TimelineMarker>> GetMarkers = () => Array.Empty<TimelineMarker>();
		public Func<IReadOnlyList<TimelineBand>> GetBands = () => Array.Empty<TimelineBand>();
		public Func<int> GetAxisSeconds = () => 3600;

		// Current wire value of a marker's option, and the one call that writes one. Both are
		// supplied by LobbyTimelineLogic; the widget knows nothing about OrderManager.
		public Func<TimelineMarker, string> GetValue = _ => null;
		public Action<TimelineMarker, string> OnSet = (_, _) => { };

		// Local drag state. While draggingMarker >= 0 the drawn position of that marker comes from
		// draggingStop and NOT from GetValue -- that is what stops the network snapping it back.
		int draggingMarker = -1;
		int draggingStop = -1;

		const int MarkerWidth = 3;
		const int MarkerOverhang = 6;
		const int MarkerDrop = 46;
		const int HandleSize = 11;

		public TimelineWidget() { }

		// REQUIRED, BOTH OF THEM. Widget.cs:256-259 throws InvalidOperationException from the base
		// Clone() for any widget type that does not override it, and the lobby clones row templates.
		public TimelineWidget(TimelineWidget other)
			: base(other)
		{
			NoteFont = other.NoteFont;
			BandFont = other.BandFont;
			CaptionFont = other.CaptionFont;
			ValueFont = other.ValueFont;
			TickFont = other.TickFont;
			TrackY = other.TrackY;
			TrackHeight = other.TrackHeight;
			BandHeight = other.BandHeight;
			CaptionY = other.CaptionY;
			NoteLabel = other.NoteLabel;
			NoteHint = other.NoteHint;
			AxisSuffix = other.AxisSuffix;
			GetMarkers = other.GetMarkers;
			GetBands = other.GetBands;
			GetAxisSeconds = other.GetAxisSeconds;
			GetValue = other.GetValue;
			OnSet = other.OnSet;
		}

		public override Widget Clone() { return new TimelineWidget(this); }

		Rectangle TrackRect => new(RenderBounds.X, RenderBounds.Y + TrackY, RenderBounds.Width, TrackHeight);

		// The rows a mouse-down is allowed to start a drag on: the track, plus the marker handles
		// that stand proud above it. Deliberately NOT the whole widget -- the caption block below the
		// bar is text, and a click there moving a marker would read as the panel misfiring.
		Rectangle GrabRect
		{
			get
			{
				var track = TrackRect;
				return new Rectangle(track.X, track.Y - MarkerOverhang - HandleSize, track.Width, TrackHeight + MarkerOverhang + HandleSize);
			}
		}

		int StopIndex(IReadOnlyList<TimelineMarker> markers, int index)
		{
			var marker = markers[index];
			if (index == draggingMarker)
				return draggingStop;

			if (marker.OptionId != null)
			{
				var value = GetValue(marker);
				for (var i = 0; i < marker.Stops.Length; i++)
					if (marker.Stops[i].Value == value)
						return i;
			}

			return Math.Clamp(marker.DefaultStop, 0, marker.Stops.Length - 1);
		}

		// A marker measured from an earlier marker sits at that marker's position plus its own
		// offset. RelativeTo is required to point STRICTLY EARLIER in the list, which is what makes
		// this recursion terminate; LobbyTimelineLogic is the only caller and satisfies it.
		int PositionSeconds(IReadOnlyList<TimelineMarker> markers, int index)
		{
			var marker = markers[index];
			var own = marker.Stops[StopIndex(markers, index)].Seconds;
			if (marker.RelativeTo >= 0 && marker.RelativeTo < index)
				return PositionSeconds(markers, marker.RelativeTo) + own;

			return own;
		}

		int MarkerPx(IReadOnlyList<TimelineMarker> markers, int index)
		{
			return TimelineModel.PxFromSeconds(PositionSeconds(markers, index), GetAxisSeconds(), RenderBounds.Width);
		}

		int StopFromPx(IReadOnlyList<TimelineMarker> markers, int index, int x)
		{
			var marker = markers[index];
			var axis = GetAxisSeconds();
			var width = RenderBounds.Width;
			var local = x - RenderBounds.X;

			// A relative marker is aimed at in absolute pixels but stored as an offset, so take the
			// base marker's position off before snapping.
			var baseSeconds = marker.RelativeTo >= 0 && marker.RelativeTo < index ? PositionSeconds(markers, marker.RelativeTo) : 0;
			var basePx = TimelineModel.PxFromSeconds(baseSeconds, axis, width);

			return TimelineModel.NearestStop(local - basePx, marker.StopSeconds, axis, width);
		}

		int NearestDraggable(IReadOnlyList<TimelineMarker> markers, int x)
		{
			var best = -1;
			var bestDistance = int.MaxValue;
			for (var i = 0; i < markers.Count; i++)
			{
				if (!markers[i].IsDraggable)
					continue;

				var distance = Math.Abs(RenderBounds.X + MarkerPx(markers, i) - x);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = i;
				}
			}

			return best;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Button != MouseButton.Left)
				return false;

			if (IsDisabled())
				return false;

			var markers = GetMarkers();
			if (markers.Count == 0)
				return false;

			if (mi.Event == MouseInputEvent.Down)
			{
				// Tested BEFORE taking focus: a widget that grabbed the mouse and then decided it was
				// not interested would swallow the click from whatever was underneath it.
				if (!GrabRect.Contains(mi.Location))
					return false;

				if (!TakeMouseFocus(mi))
					return false;
			}

			if (!HasMouseFocus)
				return false;

			switch (mi.Event)
			{
				case MouseInputEvent.Down:
					draggingMarker = NearestDraggable(markers, mi.Location.X);
					if (draggingMarker >= 0)
						draggingStop = StopFromPx(markers, draggingMarker, mi.Location.X);

					break;

				case MouseInputEvent.Move:
					// Local only. No order, no prediction, nothing on the wire.
					if (draggingMarker >= 0 && draggingMarker < markers.Count)
						draggingStop = StopFromPx(markers, draggingMarker, mi.Location.X);

					break;

				case MouseInputEvent.Up:
					// THE ONLY SITE IN THIS FILE THAT PUTS ANYTHING ON THE WIRE.
					if (draggingMarker >= 0 && draggingMarker < markers.Count)
					{
						var marker = markers[draggingMarker];
						var stop = marker.Stops[Math.Clamp(draggingStop, 0, marker.Stops.Length - 1)];
						if (stop.Value != null && stop.Value != GetValue(marker))
							OnSet(marker, stop.Value);
					}

					draggingMarker = -1;
					draggingStop = -1;
					YieldMouseFocus(mi);
					break;
			}

			return true;
		}

		public override void Draw()
		{
			if (!IsVisible())
				return;

			var markers = GetMarkers();

			// NOTHING TO DRAW YET, WHICH IS NOT THE SAME AS NOT BEING VISIBLE. The widget stays visible
			// with no markers so that Widget.TickOuter keeps ticking its ChromeLogic -- that method
			// ticks LogicObjects only inside `if (IsVisible())` (Widget.cs:512-524), so a widget that
			// hid itself while empty could never be refilled by its own logic. Drawing nothing here
			// gives the same blank panel an invisible widget would, without the latch.
			if (markers.Count == 0)
				return;

			var axis = GetAxisSeconds();
			var track = TrackRect;

			DrawNote();
			DrawTrack(track);
			DrawBands(markers, track);
			DrawAxis(track, axis);

			for (var i = 0; i < markers.Count; i++)
				DrawMarker(markers, i, track);

			for (var i = 0; i < markers.Count; i++)
				DrawCaption(markers, i);
		}

		void DrawNote()
		{
			var font = Game.Renderer.Fonts[NoteFont];
			var label = NoteLabel.ToUpperInvariant() + "   ·   ";
			font.DrawText(label, new float2(RenderBounds.X, RenderBounds.Y), TimelinePalette.Micro);
			font.DrawText(NoteHint, new float2(RenderBounds.X + font.Measure(label).X, RenderBounds.Y), TimelinePalette.MarkerHighlight);
		}

		// An INSET bevel, which is the opposite pairing to the raised one in _lobby-palette.yaml: the
		// track is a channel the markers sit in, so the dark edge is top-left. The mockup draws it
		// this way and it is the only inset surface in the panel.
		static void DrawTrack(Rectangle track)
		{
			WidgetUtils.FillRectWithColor(track, TimelinePalette.TrackFill);
			WidgetUtils.FillRectWithColor(new Rectangle(track.X, track.Y, track.Width, 1), TimelinePalette.TrackBevelDark);
			WidgetUtils.FillRectWithColor(new Rectangle(track.X, track.Y, 1, track.Height), TimelinePalette.TrackBevelDark);
			WidgetUtils.FillRectWithColor(new Rectangle(track.Right - 1, track.Y, 1, track.Height), TimelinePalette.TrackBevelLight);
			WidgetUtils.FillRectWithColor(new Rectangle(track.X, track.Bottom - 1, track.Width, 1), TimelinePalette.TrackBevelLight);
		}

		int EdgePx(IReadOnlyList<TimelineMarker> markers, int marker, int fallbackPx)
		{
			if (marker < 0 || marker >= markers.Count)
				return fallbackPx;

			return MarkerPx(markers, marker);
		}

		void DrawBands(IReadOnlyList<TimelineMarker> markers, Rectangle track)
		{
			var font = Game.Renderer.Fonts[BandFont];
			foreach (var band in GetBands())
			{
				var from = EdgePx(markers, band.FromMarker, 0);
				var to = EdgePx(markers, band.ToMarker, track.Width);
				var width = to - from;
				if (width <= 0)
					continue;

				var rect = new Rectangle(track.X + from, track.Y, width, BandHeight);
				WidgetUtils.FillRectWithColor(rect, band.Fill);

				// A band narrower than its own caption is left blank rather than drawn with text
				// spilling over its neighbours. The colour still carries the span.
				var text = band.GetText();
				if (string.IsNullOrEmpty(text))
					continue;

				var size = font.Measure(text);
				if (size.X + 8 > width)
					continue;

				font.DrawText(text, new float2(rect.X + ((width - size.X) / 2), rect.Y + ((BandHeight - size.Y) / 2)), band.Ink);
			}
		}

		void DrawAxis(Rectangle track, int axisSeconds)
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

		Color MarkerColor(TimelineMarker marker)
		{
			if (marker.Placeholder || IsDisabled())
				return TimelinePalette.MarkerDisabled;

			return marker.Highlight ? TimelinePalette.MarkerHighlight : TimelinePalette.Marker;
		}

		void DrawMarker(IReadOnlyList<TimelineMarker> markers, int index, Rectangle track)
		{
			var marker = markers[index];
			var color = MarkerColor(marker);
			var x = track.X + MarkerPx(markers, index);

			// Kept fully inside the track at both ends, so a marker at 0 or at the axis end is still
			// a whole marker rather than a sliver.
			x = Math.Clamp(x, track.X, track.Right - MarkerWidth);

			WidgetUtils.FillRectWithColor(new Rectangle(x, track.Y - MarkerOverhang, MarkerWidth, MarkerDrop), color);

			var handle = new Rectangle(x - ((HandleSize - MarkerWidth) / 2), track.Y - MarkerOverhang - (HandleSize / 2), HandleSize, HandleSize);
			WidgetUtils.FillRectWithColor(new Rectangle(handle.X - 1, handle.Y - 1, handle.Width + 2, handle.Height + 2), TimelinePalette.MarkerEdge);
			WidgetUtils.FillRectWithColor(handle, color);
		}

		void DrawCaption(IReadOnlyList<TimelineMarker> markers, int index)
		{
			var marker = markers[index];
			var captionFont = Game.Renderer.Fonts[CaptionFont];
			var valueFont = Game.Renderer.Fonts[ValueFont];

			var stop = marker.Stops[Math.Clamp(StopIndex(markers, index), 0, marker.Stops.Length - 1)];
			var caption = marker.Caption.ToUpperInvariant();
			var value = stop.Label;

			var captionSize = captionFont.Measure(caption);
			var valueSize = valueFont.Measure(value);
			var centre = RenderBounds.X + MarkerPx(markers, index);
			var y = RenderBounds.Y + CaptionY;

			var captionX = Math.Clamp(centre - (captionSize.X / 2), RenderBounds.X, RenderBounds.Right - captionSize.X);
			var valueX = Math.Clamp(centre - (valueSize.X / 2), RenderBounds.X, RenderBounds.Right - valueSize.X);

			var ink = marker.Placeholder ? TimelinePalette.Placeholder : TimelinePalette.Value;
			captionFont.DrawText(caption, new float2(captionX, y), TimelinePalette.Micro);
			valueFont.DrawText(value, new float2(valueX, y + captionSize.Y + 2), ink);
		}
	}
}
