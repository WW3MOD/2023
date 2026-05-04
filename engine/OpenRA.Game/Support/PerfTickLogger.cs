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
using System.Diagnostics;

namespace OpenRA.Support
{
	public static class PerfTickLogger
	{
		public const long TimestampDisabled = 0L;

		static float durationThresholdMs = Game.Settings.Debug.LongTickThresholdMs;
		static long durationThresholdTicks = PerfTimer.MillisToTicks(Game.Settings.Debug.LongTickThresholdMs);

		// GC counts captured at the start of the currently-measured trait/activity tick. Single-threaded
		// (sim ticks run on one thread) so plain statics are sufficient. Used to detect whether a GC fired
		// during a long-tick — long ticks attributed to trivial code (e.g. Mobile.Tick) are usually GC pauses.
		static int startGen0;
		static int startGen1;
		static int startGen2;

		/// <summary>Retrieve the current timestamp.</summary>
		/// <returns>TimestampDisabled if performance logging is disabled.</returns>
		public static long GetTimestamp()
		{
			var settings = Game.Settings.Debug;
			if (!settings.EnableSimulationPerfLogging)
				return TimestampDisabled;

			// TODO: Let settings notify listeners on changes
			if (durationThresholdMs != settings.LongTickThresholdMs)
			{
				durationThresholdMs = Game.Settings.Debug.LongTickThresholdMs;
				durationThresholdTicks = PerfTimer.MillisToTicks(durationThresholdMs);
			}

			startGen0 = GC.CollectionCount(0);
			startGen1 = GC.CollectionCount(1);
			startGen2 = GC.CollectionCount(2);
			return Stopwatch.GetTimestamp();
		}

		/// <summary>Logs an entry in the performance log when the current time since the start tick exceeds the game debug setting `LongTickThresholdMs`.</summary>
		/// <returns>TimestampDisabled if performance logging is disabled.</returns>
		public static long LogLongTick(long startTimestamp, string name, object item)
		{
			if (startTimestamp == TimestampDisabled)
				return TimestampDisabled;

			var currentTimetamp = Stopwatch.GetTimestamp();
			var endGen0 = GC.CollectionCount(0);
			var endGen1 = GC.CollectionCount(1);
			var endGen2 = GC.CollectionCount(2);

			if (currentTimetamp - startTimestamp > durationThresholdTicks)
				PerfTimer.LogLongTick(startTimestamp, currentTimetamp, name, item,
					endGen0 - startGen0, endGen1 - startGen1, endGen2 - startGen2);

			startGen0 = endGen0;
			startGen1 = endGen1;
			startGen2 = endGen2;
			return currentTimetamp;
		}
	}
}
