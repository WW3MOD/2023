#region Copyright & License Information
/*
 * The wall-clock -> ticks conversion every timed thing in the mod now runs on.
 *
 * WHAT WENT WRONG AND WHY NOTHING CAUGHT IT. The old idiom was
 * `ticksPerSecond = 1000 / world.Timestep`, then `minutes * 60 * ticksPerSecond`. That division is
 * INTEGER division, so at this mod's 60 ms timestep it produced 16 rather than 16.667 and every
 * duration built on it came out 4 % short: a 90-minute Time Limit ran 86:24. The on-screen countdown
 * was derived from the SAME tick count, so nothing on screen disagreed -- the clock lied
 * consistently. There was no observation that could have failed, which is why these tests are on the
 * arithmetic rather than on the trait.
 *
 * IT IS EXACT AT 40 ms, AND THAT IS THE TRAP. Upstream cnc/ra/d2k/ts all default to a 40 ms
 * timestep, where 1000 / 40 = 25 with no remainder. The defect is invisible in every mod OpenRA
 * ships and appears only at a timestep that does not divide 1000 -- which is this mod's. Both rates
 * are pinned below, and 40 ms is the one that proves the fix changed nothing upstream.
 *
 * SCOPE, HONESTLY. This is integer arithmetic over two ints and nothing else. It does NOT prove that
 * TimeLimitManager reads the lobby dropdown, that a warning notification is played, or that
 * DoomsdayStrike opens its window -- none of those are reachable without a World, which nothing in
 * OpenRA.Test can construct. What it proves is that every consumer converting through TickTime gets
 * the same exact answer, and the source tripwire at the bottom is what keeps them converting
 * through it.
 */
#endregion

using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Tournament;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class TickTimeTest
	{
		/// <summary>This mod: mod.yaml GameSpeeds DefaultSpeed: default, whose Timestep is 60 ms.</summary>
		const int ModTimestep = 60;

		/// <summary>Upstream cnc/ra/d2k/ts default. 1000 / 40 = 25 exactly, so the old form was right here.</summary>
		const int UpstreamTimestep = 40;

		/// <summary>Every Timestep in mods/ww3mod/mod.yaml's GameSpeeds block, slowest first.</summary>
		static readonly int[] EveryShippedTimestep = { 120, 100, 86, 75, 67, 60, 55, 48, 40, 34, 30 };

		/// <summary>Real milliseconds a tick count is worth at a timestep.</summary>
		static long RealMilliseconds(int ticks, int timestepMilliseconds)
		{
			return (long)ticks * timestepMilliseconds;
		}

		[Test]
		public void NinetyMinutesAtTheModTimestepIsExactlyNinetyRealMinutes()
		{
			// THE CASE THAT BROKE. `1000 / 60` is 16, so the shipped 90-minute Time Limit was
			// 90 * 60 * 16 = 86400 ticks = 5184 s = 86:24. Four per cent short, invisible on screen.
			var ticks = TickTime.TicksForMinutes(90, ModTimestep);
			Assert.That(ticks, Is.EqualTo(90000),
				"ninety minutes must be 90000 ticks at a 60 ms timestep. 86400 means the conversion " +
				"divided before multiplying and the match ends at 86:24");

			Assert.That(RealMilliseconds(ticks, ModTimestep), Is.EqualTo(90L * 60 * 1000),
				"the tick count must be worth exactly 90 real minutes when converted back");

			// The old shape, spelled out, so the regression is named rather than implied.
			var truncated = 90 * 60 * (1000 / ModTimestep);
			Assert.That(truncated, Is.EqualTo(86400));
			Assert.That(ticks - truncated, Is.EqualTo(3600),
				"the fix is worth 3600 ticks -- 216 s -- on a 90-minute limit");
		}

		[Test]
		public void EveryShippedTimeLimitOptionIsExactAtBothTimesteps()
		{
			// The lobby dropdown's own values, read off the trait rather than retyped, so a new
			// option cannot be added without this test seeing it.
			foreach (var minutes in new TimeLimitManagerInfo().TimeLimitOptions)
			{
				foreach (var timestep in new[] { ModTimestep, UpstreamTimestep })
				{
					var ticks = TickTime.TicksForMinutes(minutes, timestep);
					Assert.That(RealMilliseconds(ticks, timestep), Is.EqualTo((long)minutes * 60 * 1000),
						$"the {minutes}-minute lobby option is not exactly {minutes} real minutes at a " +
						$"{timestep} ms timestep");
				}
			}
		}

		[Test]
		public void TheUpstreamFortyMillisecondTimestepIsUnchangedByTheFix()
		{
			// 1000 / 40 = 25 with no remainder, so the OLD expression and the new one must agree
			// exactly for every input. This is the evidence that cnc/ra/d2k/ts scenario timings did
			// not move: DateTime.Seconds(n) there was n * 25 and still is.
			for (var seconds = 0; seconds <= 3600; seconds++)
				Assert.That(TickTime.TicksForSeconds(seconds, UpstreamTimestep), Is.EqualTo(seconds * 25),
					$"{seconds} s at a 40 ms timestep must still be {seconds * 25} ticks");

			for (var minutes = 0; minutes <= 120; minutes++)
				Assert.That(TickTime.TicksForMinutes(minutes, UpstreamTimestep), Is.EqualTo(minutes * 60 * 25));
		}

		[Test]
		public void AWarningLandsOnTheMinuteItAnnounces()
		{
			// TimeLimitManager fires a warning when `ticksRemaining == TicksForMinutes(m)`. For that to
			// mean what it says, the deadline minus the warning must be the elapsed minutes converted
			// the same way -- i.e. the conversion has to be additive over minutes. A warning computed
			// on the old arithmetic against a deadline set by the new one would land ~4 % off.
			const int Limit = 90;
			foreach (var timestep in new[] { ModTimestep, UpstreamTimestep })
			{
				foreach (var warning in new[] { 1, 2, 3, 4, 5, 10, 20, 30, 40 })
				{
					var elapsed = TickTime.TicksForMinutes(Limit, timestep) - TickTime.TicksForMinutes(warning, timestep);
					Assert.That(elapsed, Is.EqualTo(TickTime.TicksForMinutes(Limit - warning, timestep)),
						$"the {warning}-minute warning on a {Limit}-minute limit does not fall at " +
						$"{Limit - warning} elapsed minutes at a {timestep} ms timestep");
				}
			}
		}

		[Test]
		public void TheTournamentClockIsTheSecondsItIsConfiguredFor()
		{
			// TournamentConfig was `TimeLimitSeconds * 25` -- exact at 40 ms and wrong everywhere else.
			// The 41 configs setting `GameSpeed: fastest` run at 40 ms and were RIGHT; the 11 plain
			// tournament.yaml files set no GameSpeed, ran at the 60 ms default, and turned 720 into
			// 18000 ticks = 1080 real seconds. Those 11 were restated to 1080 in the same change so
			// the real duration every baseline was measured over is preserved.
			var config = new TournamentConfig { TimeLimitSeconds = 1080 };

			Assert.That(config.TimeLimitTicksAt(ModTimestep), Is.EqualTo(18000),
				"the restated 1080 s must produce the same 18000 ticks the old `720 * 25` did, or the " +
				"restatement has not preserved the duration the benchmarks were measured at");
			Assert.That(RealMilliseconds(config.TimeLimitTicksAt(ModTimestep), ModTimestep), Is.EqualTo(1080000L));

			// The pre-restatement number, to pin the diagnosis itself: 720 was never 720 seconds.
			var oldNumber = new TournamentConfig { TimeLimitSeconds = 720 };
			Assert.That(oldNumber.TimeLimitTicksAt(ModTimestep), Is.EqualTo(12000));

			// THE HALF THAT WAS ALREADY CORRECT, and the reason 41 of the 52 shipped configs were left
			// alone: at `GameSpeed: fastest` the old hardcoded 25 was exact, and the new conversion
			// returns the identical tick count. If this ever stops holding, the smoke, sanity, quick,
			// eco-5min and combat-12min families have all silently changed length.
			foreach (var seconds in new[] { 30, 60, 180, 300, 360, 720 })
				Assert.That(new TournamentConfig { TimeLimitSeconds = seconds }.TimeLimitTicksAt(UpstreamTimestep),
					Is.EqualTo(seconds * 25),
					$"a `GameSpeed: fastest` config carrying TimeLimitSeconds: {seconds} no longer " +
					$"produces the {seconds * 25} ticks it has always run for");

			// Every TimeLimitSeconds value the shipped tournament configs carry, restated or not.
			foreach (var seconds in new[] { 30, 60, 180, 300, 360, 720, 1080 })
			{
				var ticks = new TournamentConfig { TimeLimitSeconds = seconds }.TimeLimitTicksAt(ModTimestep);
				Assert.That(RealMilliseconds(ticks, ModTimestep), Is.EqualTo((long)seconds * 1000),
					$"TimeLimitSeconds: {seconds} is not {seconds} real seconds at a 60 ms timestep");
			}
		}

		[Test]
		public void NoShippedGameSpeedLosesMoreThanOneTick()
		{
			// The eleven GameSpeeds are not all divisors of 1000 (86, 75, 67, 55, 48, 34, 30 are not),
			// so an exact answer is not available at all of them. What IS required is that the residual
			// is under one tick -- the loss is the unavoidable floor, not a rate that truncated before
			// it multiplied. The old form lost up to 4 %; this loses at most one tick.
			foreach (var timestep in EveryShippedTimestep)
			{
				var ticks = TickTime.TicksForMinutes(90, timestep);
				var wanted = 90L * 60 * 1000;
				var got = RealMilliseconds(ticks, timestep);

				Assert.That(got, Is.LessThanOrEqualTo(wanted), $"rounded UP at a {timestep} ms timestep");
				Assert.That(wanted - got, Is.LessThan(timestep),
					$"a 90-minute limit is short by more than one whole tick at a {timestep} ms timestep");
			}
		}

		[Test]
		public void MillisecondsSecondsAndMinutesAgreeWithEachOther()
		{
			foreach (var timestep in EveryShippedTimestep)
			{
				Assert.That(TickTime.TicksForSeconds(600, timestep),
					Is.EqualTo(TickTime.TicksForMilliseconds(600000, timestep)));

				// Minutes is NOT implemented as Seconds(m * 60), so this is a real cross-check rather
				// than a tautology.
				Assert.That(TickTime.TicksForMinutes(10, timestep),
					Is.EqualTo(TickTime.TicksForSeconds(600, timestep)));
			}
		}

		[Test]
		public void DegenerateInputsYieldZeroRatherThanDividingByZero()
		{
			Assert.That(TickTime.TicksForMinutes(90, 0), Is.EqualTo(0));
			Assert.That(TickTime.TicksForSeconds(90, 0), Is.EqualTo(0));
			Assert.That(TickTime.TicksForMilliseconds(90, 0), Is.EqualTo(0));
			Assert.That(TickTime.TicksForMinutes(90, -60), Is.EqualTo(0));

			Assert.That(TickTime.TicksForMinutes(0, ModTimestep), Is.EqualTo(0));
			Assert.That(TickTime.TicksForSeconds(0, ModTimestep), Is.EqualTo(0));

			// NuclearUnlockSchedule keeps its own "no clock" floor on top of the general helper, and
			// RungAt divides by the result -- so that floor is load-bearing, not decorative.
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(-5, ModTimestep), Is.EqualTo(0));
			Assert.That(NuclearUnlockSchedule.TicksForMinutes(10, ModTimestep), Is.EqualTo(10000));

			// Saturates rather than wrapping: a wrapped value would be a NEGATIVE deadline, which
			// every consumer reads as "already expired".
			Assert.That(TickTime.TicksForSeconds(int.MaxValue, ModTimestep), Is.EqualTo(int.MaxValue));
			Assert.That(TickTime.TicksForMinutes(int.MaxValue, ModTimestep), Is.EqualTo(int.MaxValue));
		}

		/// <summary>
		/// The class fix, not the instance fix. A local patch that still materialises an integer
		/// ticks-per-second has lost the 4 % before it multiplies, so the idiom itself has to stay gone.
		/// </summary>
		[Test]
		public void NoConverterMaterialisesATruncatedTicksPerSecond()
		{
			var converted = new[]
			{
				Path.Combine("engine", "OpenRA.Mods.Common", "Traits", "World", "TimeLimitManager.cs"),
				Path.Combine("engine", "OpenRA.Mods.Common", "Traits", "World", "NuclearUnlockSchedule.cs"),
				Path.Combine("engine", "OpenRA.Mods.Common", "Tournament", "TournamentConfig.cs"),
				Path.Combine("engine", "OpenRA.Mods.Common", "Scripting", "Global", "DateTimeGlobal.cs"),
			};

			// `1000 / <something>Timestep` in code. Comment lines are excluded because all four files
			// DESCRIBE the old idiom on purpose, and a tripwire that forbade naming the defect would be
			// deleted the first time somebody documented it.
			var idiom = new Regex(@"^(?![ \t]*(//|\*)).*\b1000\s*/\s*\w*[Tt]imestep", RegexOptions.Multiline);

			foreach (var relative in converted)
			{
				var path = FindRepoFile(relative);
				if (path == null)
					Assert.Ignore($"{relative} not reachable from the test assembly - tripwire skipped, not passed");

				var match = idiom.Match(File.ReadAllText(path));
				Assert.That(match.Success, Is.False,
					$"{relative} has gone back to `1000 / Timestep`. That is INTEGER division and at this " +
					"mod's 60 ms timestep it is 16 rather than 16.667, so everything built on it runs 4 % " +
					$"early. Convert through TickTime instead. Offending line: {match.Value.Trim()}");
			}
		}

		static string FindRepoFile(string relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, relative);
				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}
	}
}
