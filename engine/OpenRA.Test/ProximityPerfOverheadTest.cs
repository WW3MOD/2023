#region Copyright & License Information
/*
 * WW3MOD ProximityPerf instrument calibration.
 *
 * ProximityPerf's `reindex_us` is accumulated from a Stopwatch pair taken around
 * every UpdateProximityTrigger call. On the map this instrument was built for
 * that is ~583 calls per tick, so the timestamp reads are inside the number the
 * instrument reports and inflate it. This fixture measures what a pair costs on
 * the machine that will run the capture, so the report can subtract a measured
 * overhead instead of asserting the overhead is negligible.
 *
 * It is a MEASUREMENT, not a behavioural test. The assertions are deliberately
 * loose sanity rails — a timing assertion tight enough to be interesting is
 * tight enough to flake on a loaded CI box, and a flaky red here would teach
 * everyone to ignore it. The value lives in the console line, which is why it is
 * printed unconditionally.
 */
#endregion

using System;
using System.Diagnostics;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ProximityPerfOverheadTest
	{
		const int Iterations = 2_000_000;

		[Test]
		public void MeasureStopwatchPairCost()
		{
			// Warm up: first calls pay JIT and, on some platforms, a first-touch
			// cost in the clock source that is not representative of steady state.
			long warm = 0;
			for (var i = 0; i < 100_000; i++)
			{
				var t = Stopwatch.GetTimestamp();
				warm += Stopwatch.GetTimestamp() - t;
			}

			Assert.That(warm, Is.GreaterThanOrEqualTo(0));

			var outer = Stopwatch.GetTimestamp();
			long accumulated = 0;
			for (var i = 0; i < Iterations; i++)
			{
				var t = Stopwatch.GetTimestamp();
				accumulated += Stopwatch.GetTimestamp() - t;
			}

			var elapsed = Stopwatch.GetTimestamp() - outer;

			// Cost of the whole pair-plus-accumulate, which is what the instrument
			// actually pays per UpdateProximityTrigger call.
			var nsPerPair = 1_000_000_000.0 * elapsed / Stopwatch.Frequency / Iterations;

			// Cost the pair can SEE of itself: the inner delta. The gap between this
			// and nsPerPair is the part of the read that lands outside the window.
			var nsSelfObserved = 1_000_000_000.0 * accumulated / Stopwatch.Frequency / Iterations;

			// 583 offset-carrying tree clumps on woodland-warfare-ww3, the population
			// this instrument was built to price.
			const int UpdatesPerTick = 583;
			var perCallUsPerTick = nsPerPair * UpdatesPerTick / 1000.0;
			var sampledUsPerTick = perCallUsPerTick / ProximityPerf.ReindexSampleRate;

			Console.WriteLine(
				$"[ProximityPerf calibration] Stopwatch.Frequency={Stopwatch.Frequency} Hz; " +
				$"pair+accumulate={nsPerPair:0.0} ns; self-observed={nsSelfObserved:0.0} ns. " +
				$"At {UpdatesPerTick} updates/tick: timing EVERY call would cost " +
				$"{perCallUsPerTick:0.0} us/tick of instrument (the reason it is sampled); " +
				$"sampling 1-in-{ProximityPerf.ReindexSampleRate} costs {sampledUsPerTick:0.00} us/tick.");

			// Sanity rails only. A pair that costs over 10 us means the clock source
			// is a syscall rather than a vDSO/TSC read, and every reindex_us figure
			// from that machine is overhead rather than signal — worth failing on.
			Assert.That(nsPerPair, Is.GreaterThan(0.0));
			Assert.That(nsPerPair, Is.LessThan(10_000.0));
		}
	}
}
