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
 * DefconReadoutWidget: a Draw() is not reachable from a test. See LobbyTimelineMathTest.
 *
 * ==== IT IS A READ-ONLY OVERVIEW AND NO LONGER A CONTROL (user ruling 2026-09-13) ====
 * The dropdowns lower in the panel are the source of truth; this bar only DRAWS what they say.
 * Everything that existed to serve dragging is therefore gone rather than merely disabled:
 * TimelineMarker, TimelineStop, NearestStop (the snapping) and VisibleLabels (the marker-caption
 * collision rule). They were not dead code that accumulated -- they were the drag feature, and a
 * bar with no drag has no use for any of them.
 *
 * THE SAFETY ARGUMENT THEY CARRIED IS NOW STRUCTURAL RATHER THAN DEFENDED. Snapping existed
 * because an out-of-set option value is validated by the server as `Values.ContainsKey` and then
 * read back UNCHECKED by LobbySettingsNotification.cs:39, so a bad value throws
 * KeyNotFoundException on the next CLIENT JOIN -- the host sees a working lobby and the next player
 * to connect is thrown out. This file now WRITES NOTHING AT ALL, so it cannot produce a value of
 * any kind. Do not reintroduce a write here without reintroducing the enumerated stops with it.
 *
 * ==== TWO LAYOUTS, BECAUSE THE TWO MODES MEASURE DIFFERENT THINGS ====
 * In Skirmish every boundary is a time on the MATCH CLOCK -- the unlock intervals and the time
 * limit -- so the bar is an absolute axis with a minute ruler under it, exactly as before.
 *
 * In Escalation one boundary is an EVENT: the peace phase ends when somebody takes the first kill,
 * which has no duration. An absolute ruler under that is a lie in the one place a host would most
 * want to trust it, so Escalation draws PHASE LENGTHS with no ruler at all. That is the user's own
 * framing of the ruling -- "it can show how long each phase is and that will be enough" -- and it is
 * why TimelineLayout carries ShowRuler rather than the widget assuming one.
 */

using System;
using System.Collections.Generic;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Widgets
{
	/// <summary>One span of the bar, positioned in seconds on whatever axis its layout describes.</summary>
	// GetCaption/GetDetail are delegates rather than strings because a band's wording tracks other
	// lobby options -- the tail band says whether the ending is nuclear, which is the `doomsday`
	// checkbox and not this widget's business to know statically.
	public class TimelineBand
	{
		public readonly int FromSeconds;
		public readonly int ToSeconds;
		public readonly Func<string> GetCaption;
		public readonly Func<string> GetDetail;
		public readonly Color Fill;
		public readonly Color Ink;

		/// <summary>Drawn hatched: this span ends on an EVENT and its width means nothing.</summary>
		public readonly bool Indeterminate;

		public TimelineBand(int fromSeconds, int toSeconds, Func<string> getCaption, Func<string> getDetail,
			Color fill, Color ink, bool indeterminate = false)
		{
			FromSeconds = fromSeconds;
			ToSeconds = toSeconds;
			GetCaption = getCaption;
			GetDetail = getDetail;
			Fill = fill;
			Ink = ink;
			Indeterminate = indeterminate;
		}

		public int LengthSeconds => Math.Max(0, ToSeconds - FromSeconds);
	}

	/// <summary>Everything the widget needs for one frame: the bands, the axis, and the two notes.</summary>
	public class TimelineLayout
	{
		public static readonly TimelineLayout Empty = new(Array.Empty<TimelineBand>(), TimelineModel.TickSeconds, false, null, null);

		public readonly IReadOnlyList<TimelineBand> Bands;
		public readonly int AxisSeconds;

		/// <summary>Whether the minute ruler is TRUE of this layout. See the file header.</summary>
		public readonly bool ShowRuler;

		public readonly string Hint;

		/// <summary>The consistency flag, or null. Drawn in place of the hint, in the warning ink.</summary>
		public readonly string Warning;

		public TimelineLayout(IReadOnlyList<TimelineBand> bands, int axisSeconds, bool showRuler, string hint, string warning)
		{
			Bands = bands ?? Array.Empty<TimelineBand>();
			AxisSeconds = axisSeconds > 0 ? axisSeconds : TimelineModel.TickSeconds;
			ShowRuler = showRuler;
			Hint = hint;
			Warning = warning;
		}
	}

	public static class TimelineModel
	{
		// The mockup's axis grid: a labelled tick every ten minutes.
		public const int TickSeconds = 600;

		// ==== THE NOMINAL SPANS, AND WHY THEY ARE SHARES RATHER THAN CONSTANTS ====
		// Two phases on the Escalation bar have no length to draw: the peace ends on the FIRST KILL,
		// and the nuclear exchange runs until something ends the match. They are given nominal widths
		// so the bar has five readable bands instead of three and a pair of hairlines.
		//
		// THEY WERE FIXED SECOND COUNTS AND THAT MADE THE BAR ILLEGIBLE AT LONG SETTINGS. A fixed
		// nominal is a SHRINKING SHARE of an axis whose other terms grow with the host's clocks: at
		// a 15-minute no-rush the axis more than doubles, so a 300 s nominal fell from 121px to 83px
		// of a 664px bar and "NUCLEAR EXCHANGE" -- 16 characters -- stopped being drawn at all. A
		// 2026-09-13 lobby capture shows that state: two unlabelled colour blocks at the right-hand
		// end, exactly where the match gets decided.
		//
		// As a SHARE of the configured clocks every band keeps its proportions at every setting, so
		// a caption that fits at the default fits everywhere. The widths were meaningless either way
		// -- that is what the hatch on the peace band says -- and meaningless-and-readable beats
		// meaningless-and-blank.
		const int PeaceShareNumerator = 1;
		const int PeaceShareDenominator = 4;
		const int ExchangeShareNumerator = 1;
		const int ExchangeShareDenominator = 3;

		/// <summary>The width the peace phase is drawn at, given the clocks around it. NOT a duration.</summary>
		public static int IndeterminateNominalSeconds(int configuredSeconds)
		{
			return Math.Max(TickSeconds / 4, configuredSeconds * PeaceShareNumerator / PeaceShareDenominator);
		}

		/// <summary>The width of a phase that runs until something ends the match. NOT a duration.</summary>
		// The floor matters at the shortest configuration a host can set (2 + 2 minutes): without it
		// the share collapses and the two open-ended bands vanish, which is the same blank-band
		// failure in the other direction.
		public static int OpenEndedNominalSeconds(int configuredSeconds)
		{
			return Math.Max(TickSeconds / 4, configuredSeconds * ExchangeShareNumerator / ExchangeShareDenominator);
		}

		public static int PxFromSeconds(int seconds, int axisSeconds, int width)
		{
			if (axisSeconds <= 0 || width <= 0)
				return 0;

			var clamped = Math.Clamp(seconds, 0, axisSeconds);
			return (int)((long)width * clamped / axisSeconds);
		}

		/// <summary>The axis length: the largest position any band can reach, rounded up to a labelled tick.</summary>
		// ONLY MEANINGFUL FOR A RULED LAYOUT. An Escalation layout's axis is the sum of its own band
		// lengths and is exact, not rounded -- rounding it up would leave a stub of empty track past
		// the last phase, which on a bar with no ruler reads as a phase nobody captioned.
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
		// host that the number is a GAP measured from the previous phase rather than a time on the
		// match clock -- which in Escalation is now true of every caption on the bar except the
		// time limit, so it is what keeps the one absolute number legible as the exception.
		public static string Offset(int seconds)
		{
			return "+" + Clock(seconds);
		}

		/// <summary>Minimum clear pixels a caption needs inside its own band before it is drawn.</summary>
		// 8 to match the padding DrawBands has always required of a band caption, so the caption row
		// and the detail row agree on what "too tight to read" means.
		public const int LabelGap = 8;
	}
}
