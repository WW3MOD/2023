#region Copyright & License Information
/*
 * WW3MOD proximity-trigger cost instrument.
 *
 * Off-by-default observation sink for ActorMap's proximity-trigger subsystem.
 * Emits one JSONL record per world tick: how many triggers were re-indexed,
 * how many were rebuilt, how much bin and actor scanning that cost, and the
 * wall time of each half.
 *
 * WHY IT EXISTS. ProximityExternalCondition.Tick compares `self.CenterPosition`
 * against a `cachedPosition` that was stored as `self.CenterPosition +
 * Info.Offset`, so for any actor with a non-zero Offset the comparison is true
 * forever and the trigger is re-indexed and rebuilt every tick. That is a
 * reading of the code, not a measurement, and the population that hits it grew
 * from a handful of tree husks to ~583 permanent tree clumps on
 * woodland-warfare-ww3. This instrument exists to decide whether that costs
 * anything a player could notice, rather than arguing it from the source.
 *
 * Active ONLY when the Test.ProximityPerfLog launch arg resolves a path. With
 * it absent, `Enabled` stays false and the subsystem pays one static bool test
 * per UpdateProximityTrigger call and one per tick — no timestamps, no
 * counters, no file.
 *
 * Determinism: reads sim state and writes a file only. Draws no RNG, mutates
 * no actor or trait field the simulation reads, and reorders nothing. Same
 * discipline as UnitLifecycleLogger and MissileTrace.
 *
 * WHY THE RE-INDEX HALF IS SAMPLED. The first cut of this file timed every
 * UpdateProximityTrigger call. ProximityPerfOverheadTest then measured a
 * Stopwatch pair at ~190 ns on the development machine, which at ~583 calls a
 * tick is ~110 us/tick of instrument — plausibly the same size as the signal,
 * so the instrument would have been reporting mostly itself. One call in
 * ReindexSampleRate is now timed instead (~2 us/tick), and the total is
 * reconstructed downstream as `reindex_sample_us / reindex_samples * updates`.
 * Both terms are emitted so that reconstruction can be checked rather than
 * trusted.
 *
 * `rebuild_us` needs no such treatment: it takes ONE timestamp pair around the
 * whole trigger loop, so its overhead is ~0.2 us/tick regardless of population.
 * It is consequently the more trustworthy of the two numbers, and it covers the
 * more expensive half of the work.
 */
#endregion

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace OpenRA.Mods.Common.Traits
{
	public static class ProximityPerf
	{
		public static bool Enabled { get; private set; }

		// Reset after every emit. int is ample: the largest of these is `scanned`,
		// bounded by rebuilds x actors-per-bin-box, which is ~1e6 on the densest map.
		public static int Updates;
		public static int Rebuilds;
		public static int BinVisits;
		public static int BinScan;
		public static int Matched;
		public static int ReindexSamples;
		public static long ReindexSampleStopwatchTicks;
		public static long RebuildStopwatchTicks;

		// One re-index in this many is timed. 64 puts ~9 timestamp pairs a tick on
		// the map this was built for — under 2 us of instrument — while still
		// accumulating ~27000 samples over a 3000-tick capture.
		public const int ReindexSampleRate = 64;

		static int reindexCallsSeen;

		public static bool ShouldSampleReindex()
		{
			return (reindexCallsSeen++ & (ReindexSampleRate - 1)) == 0;
		}

		static StreamWriter writer;
		static bool initialized;

		public static void Initialize(string path)
		{
			if (initialized)
				return;

			initialized = true;

			if (string.IsNullOrEmpty(path))
				return;

			var dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);

			writer = new StreamWriter(path, false, new UTF8Encoding(false));
			Enabled = true;
		}

		public static long Timestamp()
		{
			return Stopwatch.GetTimestamp();
		}

		static double ToMicroseconds(long stopwatchTicks)
		{
			return 1000000.0 * stopwatchTicks / Stopwatch.Frequency;
		}

		static string F(double v)
		{
			return v.ToString("0.###", CultureInfo.InvariantCulture);
		}

		// `triggers` is the live trigger count and `tickTimeMs` the engine's own
		// whole-loop figure, both passed in by the caller: emitting the numerator
		// and its denominator on the same line is the entire point, because a
		// microsecond figure with no frame budget beside it decides nothing.
		public static void Emit(int worldTick, int triggers, double tickTimeMs)
		{
			if (!Enabled)
				return;

			writer.Write(
				"{\"tick\":" + worldTick.ToString(CultureInfo.InvariantCulture) +
				",\"triggers\":" + triggers.ToString(CultureInfo.InvariantCulture) +
				",\"updates\":" + Updates.ToString(CultureInfo.InvariantCulture) +
				",\"rebuilds\":" + Rebuilds.ToString(CultureInfo.InvariantCulture) +
				",\"bin_visits\":" + BinVisits.ToString(CultureInfo.InvariantCulture) +
				",\"bin_scan\":" + BinScan.ToString(CultureInfo.InvariantCulture) +
				",\"matched\":" + Matched.ToString(CultureInfo.InvariantCulture) +
				",\"reindex_samples\":" + ReindexSamples.ToString(CultureInfo.InvariantCulture) +
				",\"reindex_sample_us\":" + F(ToMicroseconds(ReindexSampleStopwatchTicks)) +
				",\"rebuild_us\":" + F(ToMicroseconds(RebuildStopwatchTicks)) +
				",\"tick_time_ms\":" + F(tickTimeMs) +
				"}\n");

			Updates = 0;
			Rebuilds = 0;
			BinVisits = 0;
			BinScan = 0;
			Matched = 0;
			ReindexSamples = 0;
			ReindexSampleStopwatchTicks = 0;
			RebuildStopwatchTicks = 0;

			// Flushed every tick because the capture is expected to end by the
			// harness killing the process on its watchdog, which runs no finaliser.
			// A buffered tail would be exactly the busy ticks worth reading.
			writer.Flush();
		}
	}
}
