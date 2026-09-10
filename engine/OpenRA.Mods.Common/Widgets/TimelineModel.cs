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
 * THE MATCH TIMELINE -- the data types and the arithmetic, with no pixels in it.
 *
 * Split from TimelineWidget for the same reason DefconReadoutModel is split from
 * DefconReadoutWidget: a Draw() is not reachable from a test, and the part of this feature that
 * can be WRONG rather than merely ugly is the snapping. See LobbyTimelineMathTest.
 *
 * ==== WHY EVERY POSITION IS AN ENUMERATED STOP AND NEVER A FLOAT ====
 * The wire protocol is the text order `option <id> <value>` and the server validates only
 * `option.Values.ContainsKey(value)` (LobbyCommands.cs). A value outside that dictionary is
 * rejected twice, and the second rejection is the dangerous one: LobbySettingsNotification.cs:39
 * does an UNCHECKED `option.Values[...]` on live session state, so an out-of-set value throws
 * KeyNotFoundException on the next CLIENT JOIN -- the host sees nothing wrong and the next player
 * to connect is thrown out. So a marker never holds a continuous position: it holds an INDEX into
 * a stop list, and every stop carries the exact string that already exists in that option's own
 * Values dictionary.
 *
 * ==== SNAPPING IS BY PIXEL DISTANCE, NOT BY TIME DISTANCE ====
 * Stops are not evenly spaced -- `defcon-pace` is 2:30 / 5:00 / 9:00 and `timelimit` is
 * 10/20/30/40/60/90 -- so nearest-in-seconds and nearest-in-pixels are different answers, and the
 * one the host is actually aiming with is the pixel. Ties go to the LOWER index, deterministically,
 * because a tie that resolved by list order would move a marker differently depending on how the
 * caller happened to sort its stops.
 */

using System;
using System.Collections.Generic;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Widgets
{
	// One settable position on the timeline. Value is the key written to the wire and MUST be a key
	// of the bound option's own Values dictionary; it is null for a derived marker, which has
	// exactly one stop and never issues an order.
	public readonly struct TimelineStop
	{
		public readonly int Seconds;
		public readonly string Value;
		public readonly string Label;

		public TimelineStop(int seconds, string value, string label)
		{
			Seconds = seconds;
			Value = value;
			Label = label;
		}
	}

	// A marker on the bar. RelativeTo is the index of the marker this one is measured FROM, or -1
	// for a position on the match clock.
	//
	// THE RELATIVE CASE IS THE USER'S RULING, NOT AN IMPLEMENTATION CONVENIENCE (2026-09-10): the
	// warhead marker is an OFFSET from the no-rush marker, so dragging the no-rush marker carries it
	// along. Note what that buys -- the two markers cannot be dragged into an incoherent order
	// (warheads before the line lifts) because the relationship makes that state unreachable by
	// construction. There is deliberately NO clamp guarding it. Do not add one: a clamp asserts that
	// the state it guards against is reachable, and the next reader would lose an hour working out
	// when.
	public class TimelineMarker
	{
		public readonly string OptionId;
		public readonly string Caption;
		public readonly TimelineStop[] Stops;
		public readonly int[] StopSeconds;
		public readonly int DefaultStop;
		public readonly int RelativeTo;
		public readonly bool Highlight;

		// A marker on a LobbyOption.Placeholder option is drawn dimmed and cannot be dragged. This is
		// the same rule LobbyOptionsLogic.cs:557 and :609 apply to the checkbox and the dropdown, and
		// it is here for the same stated reason: an accepted order resets EVERY client to NotReady and
		// posts a settings-changed chat line, which is a disruptive consequence for a control that
		// governs nothing. A timeline that ignored this would be a back door onto exactly the options
		// the sibling panel deliberately made dead to the mouse.
		public readonly bool Placeholder;

		public TimelineMarker(string optionId, string caption, TimelineStop[] stops, int defaultStop,
			int relativeTo = -1, bool highlight = false, bool placeholder = false)
		{
			OptionId = optionId;
			Caption = caption;
			Stops = stops;
			DefaultStop = defaultStop;
			RelativeTo = relativeTo;
			Highlight = highlight;
			Placeholder = placeholder;

			StopSeconds = new int[stops.Length];
			for (var i = 0; i < stops.Length; i++)
				StopSeconds[i] = stops[i].Seconds;
		}

		public bool IsDraggable => OptionId != null && !Placeholder && Stops.Length > 1;
	}

	// A coloured span of the bar between two markers. FromMarker/ToMarker are marker indices, or -1
	// for the start/end of the axis. GetText is a delegate rather than a string because a band's
	// wording tracks other lobby options -- the tail band says whether the ending is nuclear, which
	// is the `doomsday` checkbox and not this widget's business to know statically.
	public class TimelineBand
	{
		public readonly int FromMarker;
		public readonly int ToMarker;
		public readonly Func<string> GetText;
		public readonly Color Fill;
		public readonly Color Ink;

		public TimelineBand(int fromMarker, int toMarker, Func<string> getText, Color fill, Color ink)
		{
			FromMarker = fromMarker;
			ToMarker = toMarker;
			GetText = getText;
			Fill = fill;
			Ink = ink;
		}
	}

	public static class TimelineModel
	{
		// The mockup's axis grid: a labelled tick every ten minutes.
		public const int TickSeconds = 600;

		public static int PxFromSeconds(int seconds, int axisSeconds, int width)
		{
			if (axisSeconds <= 0 || width <= 0)
				return 0;

			var clamped = Math.Clamp(seconds, 0, axisSeconds);
			return (int)((long)width * clamped / axisSeconds);
		}

		public static int SecondsFromPx(int px, int axisSeconds, int width)
		{
			if (width <= 0)
				return 0;

			return (int)Math.Clamp((long)axisSeconds * px / width, 0, axisSeconds);
		}

		/// <summary>Index of the stop whose drawn position is closest to px. Ties go to the lower index.</summary>
		public static int NearestStop(int px, IReadOnlyList<int> stopSeconds, int axisSeconds, int width)
		{
			if (stopSeconds == null || stopSeconds.Count == 0)
				return 0;

			var best = 0;
			var bestDistance = int.MaxValue;
			for (var i = 0; i < stopSeconds.Count; i++)
			{
				var distance = Math.Abs(PxFromSeconds(stopSeconds[i], axisSeconds, width) - px);

				// Strictly less-than, so the FIRST stop at a given distance wins and the result does
				// not depend on the caller's sort order.
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = i;
				}
			}

			return best;
		}

		/// <summary>The axis length: the largest position any marker can reach, rounded up to a labelled tick.</summary>
		public static int AxisSecondsFor(IEnumerable<int> stopSeconds, int minimumSeconds)
		{
			var max = minimumSeconds;
			if (stopSeconds != null)
				foreach (var seconds in stopSeconds)
					max = Math.Max(max, seconds);

			if (max <= 0)
				return TickSeconds;

			return ((max + TickSeconds - 1) / TickSeconds) * TickSeconds;
		}

		/// <summary>Ticks to seconds at a given millisecond timestep. See conventions.md — the default is 60 ms, NOT 40.</summary>
		public static int TicksToSeconds(int ticks, int timestepMilliseconds)
		{
			if (timestepMilliseconds <= 0)
				return 0;

			return (int)Math.Round(ticks * timestepMilliseconds / 1000d, MidpointRounding.AwayFromZero);
		}

		// DELIBERATELY NOT WidgetUtils.FormatTimeSeconds, and this was caught by the fixture rather
		// than by review: that helper rolls over into an HOURS field at sixty minutes
		// (WidgetUtils.cs:235-236), so a 60-minute time limit captioned itself "1:00:00" and a
		// 90-minute one "1:30:00". The axis this sits under is labelled in minutes all the way to its
		// end ("60 min", "90 min"), so an hours field on the caption directly above it contradicts the
		// ruler beside it. Minutes here are unbounded on purpose.
		public static string Clock(int seconds)
		{
			return $"{seconds / 60:D}:{seconds % 60:D2}";
		}

		// THE PLUS SIGN IS LOAD-BEARING AND IS THE USER'S RULING (2026-09-10). It is what tells the
		// host that the number is a GAP between two markers rather than a time on the match clock.
		public static string Offset(int seconds)
		{
			return "+" + Clock(seconds);
		}

		/// <summary>Minimum clear pixels between two drawn labels.</summary>
		// 8 to match the padding DrawBands already requires of a band caption (`size.X + 8 > width`),
		// so the two label rows and the band row agree on what "too tight to read" means.
		public const int LabelGap = 8;

		/// <summary>
		/// Which labels in one row may be drawn without overstriking each other: one bool per label, in
		/// the caller's order. Higher <paramref name="priorities"/> win; ties go left to right.
		/// </summary>
		/// <remarks>
		/// <para>WHY THIS IS NOT A LOOP IN THE WIDGET. A caption is centred on its own marker, so
		/// whether it is drawable is a property of its NEIGHBOURS rather than of itself, and the row has
		/// to be resolved as a whole. Two markers 48px apart carrying captions 60px and 101px wide
		/// overstrike into one unreadable run of letters, and that was the DEFAULT configuration
		/// (`defcon-pace: standard` = 5:00 against `nuclear-unlock-interval: 10` = 10:00) — so it was
		/// what every host saw on opening the lobby, not a corner case. Captions need
		/// (60+101)/2 = 80.5px of centre separation and will never fit in 48.</para>
		///
		/// <para>SUPPRESSION RATHER THAN STAGGERING OR ABBREVIATION, and both alternatives were real.
		/// A second caption row needs the widget taller, and LobbyTimelineChromeTest pins the option
		/// grid at exactly the widget's Height below it, so that is a layout change across three files
		/// with a fresh way to be wrong. An abbreviation would be new player-facing copy invented by an
		/// implementer, in a panel whose wording is taken from an approved mockup and governed by
		/// rulings (decision 18 removed a single word from it). Suppression changes no geometry and
		/// invents no words: it is the same trade DrawBands already makes, where a band too narrow for
		/// its caption is drawn blank and the COLOUR carries the span.</para>
		///
		/// <para>THE TOP-PRIORITY LABEL IS ALWAYS DRAWN, by construction: it is placed first, against an
		/// empty row. So a rule that drops something can never drop the label the panel exists to show.
		/// LobbyTimelineMathTest pins that as an invariant across the whole configurable range.</para>
		///
		/// <para>Takes LEFT EDGES, not centres, so it sees exactly the spans the widget will draw —
		/// including the clamp that keeps an edge label inside the bar. Duplicating that clamp here
		/// would be a second copy of the geometry to keep in step.</para>
		/// </remarks>
		public static bool[] VisibleLabels(IReadOnlyList<int> lefts, IReadOnlyList<int> widths,
			IReadOnlyList<int> priorities, int gap = LabelGap)
		{
			var count = lefts?.Count ?? 0;
			var visible = new bool[count];
			if (count == 0 || widths == null || widths.Count < count)
				return visible;

			// Highest priority first, then LEFT TO RIGHT. No two entries ever compare equal, so the
			// result cannot depend on the sort's stability or on the caller's array order.
			var order = new int[count];
			for (var i = 0; i < count; i++)
				order[i] = i;

			Array.Sort(order, (a, b) =>
			{
				var pa = priorities != null && a < priorities.Count ? priorities[a] : 0;
				var pb = priorities != null && b < priorities.Count ? priorities[b] : 0;
				return pa != pb ? pb.CompareTo(pa) : a.CompareTo(b);
			});

			var placed = new List<(int Left, int Right)>();
			foreach (var i in order)
			{
				// A zero-width label is nothing to draw and must not reserve space, or it would
				// suppress a real neighbour on behalf of an empty string.
				if (widths[i] <= 0)
					continue;

				var left = lefts[i];
				var right = left + widths[i];

				var clear = true;
				foreach (var (placedLeft, placedRight) in placed)
				{
					if (left < placedRight + gap && placedLeft < right + gap)
					{
						clear = false;
						break;
					}
				}

				if (!clear)
					continue;

				visible[i] = true;
				placed.Add((left, right));
			}

			return visible;
		}
	}
}
