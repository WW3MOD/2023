using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Eluant;
using NUnit.Framework;
using OpenRA.Mods.Common;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the seconds -> ticks conversion the Lua autotest harness runs on, and the scenario
	/// deadlines that were sized against it.
	///
	/// THE DEFECT THIS FIXTURE WAS BUILT AROUND IS FIXED, 2026-09-21. TestHarness.TicksPerSecond was
	/// the literal 25 — a rate belonging to no tick base in this mod (RA's 40 ms timestep gives 25;
	/// ours gives 16.667), so every AssertWithin(n) was worth 1.5 n engine seconds. It is now derived
	/// from TestHarness.TimestepMs, which mirrors the mod's default GameSpeed Timestep, and
	/// AssertWithin converts the way the engine does (multiply before divide). Harness and engine agree
	/// tick-for-tick on every integer second, and every window in the suite got a third shorter.
	///
	/// WHAT THESE TESTS NOW PROTECT. Three things, and none of them is "the number is 25":
	///   1. The harness rate still equals 1000 / the real default Timestep. Putting back either 25
	///      (the old harness literal) or 16 (the old TRUNCATED engine value, which conventions.md:1107
	///      records as an exactly-backwards prescription) fails this fixture.
	///   2. AssertWithin agrees with DateTime.Seconds for every integer second, executed rather than
	///      restated — this is what fails if the multiply-before-divide is reverted.
	///   3. The two scenarios that were casualties of the old constant are still immune to it: their
	///      deadlines are budgeted in ticks and round-trip exactly at the shipped timestep and at the
	///      40 ms one that yields the old 25.
	///
	/// A CORRECTION TO THE ORIGINAL FRAMING, worth keeping because it inverts the risk. Only ONE of
	/// the two — test-critical-no-panic — actually went red when the rate dropped; its deadline fell 5
	/// ticks short of the window it contains, so the predicate could never succeed.
	/// test-autotarget-preempt-air did NOT become "structurally impossible to pass": a healthy build
	/// engaged at poll ~155 against a 160-tick deadline and kept passing, while its documented 110-tick
	/// responsiveness budget became unreachable and silently stopped being enforced. A green that has
	/// quietly stopped measuring is the worse of the two failures and the harder to notice. That is the
	/// shape to hunt in the full-suite run that follows the flip, across the deadlines this fixture
	/// does NOT cover: the seconds-literal ones, which nothing here audits.
	///
	/// Sources of truth: mods/ww3mod/scripts/test-helpers.lua, mods/ww3mod/mod.yaml,
	/// engine/OpenRA.Mods.Common/Scripting/Global/DateTimeGlobal.cs, DOCS/recipes/AUTOTEST.md.
	/// </summary>
	[TestFixture]
	public class AutotestTickRateTest
	{
		static string FindRepoFile(params string[] parts)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, Path.Combine(parts));
				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}

		static string ReadRepoFile(params string[] parts)
		{
			var path = FindRepoFile(parts);
			if (path == null)
				Assert.Ignore($"{string.Join("/", parts)} not reachable from the test assembly — tick-rate check skipped, not passed");

			return File.ReadAllText(path);
		}

		static string HarnessSource()
		{
			return ReadRepoFile("mods", "ww3mod", "scripts", "test-helpers.lua");
		}

		/// <summary>
		/// Re-derives the harness rate from a hypothetical timestep exactly as test-helpers.lua does,
		/// for the immunity checks. Injected as source rather than as a rate literal so the two stay
		/// coupled the way the shipped file couples them.
		/// </summary>
		static string OverrideTimestep(int timestepMs)
		{
			return $"TestHarness.TimestepMs = {timestepMs}\nTestHarness.TicksPerSecond = 1000 / TestHarness.TimestepMs";
		}

		/// <summary>
		/// Reads a numeric field out of the harness by EXECUTING the real helper rather than restating
		/// its value. A fixture that declares the rate itself agrees with itself whatever the helper
		/// ships. The engine-bound helpers in that file are never called and Lua resolves globals at call
		/// time, so loading it needs no world. The number is taken through Eluant rather than through
		/// ToString: the rate is no longer an integer, and a string round-trip would be at the mercy of
		/// the current culture's decimal separator.
		/// </summary>
		static double HarnessNumber(string expression)
		{
			using (var runtime = new LuaRuntime())
			{
				runtime.DoBuffer(HarnessSource(), "test-helpers.lua").Dispose();
				using (var results = runtime.DoBuffer("return " + expression, "probe"))
				{
					Assert.That(results.Count, Is.GreaterThan(0),
						$"test-helpers.lua no longer exposes {expression}; the scenario deadlines pinned "
						+ "below are derived from it and the pin must be re-pointed rather than deleted");

					var number = results[0].ToNumber();
					Assert.That(number.HasValue, Is.True, $"{expression} is no longer a number");
					return number.Value;
				}
			}
		}

		static double HarnessTicksPerSecond()
		{
			return HarnessNumber("TestHarness.TicksPerSecond");
		}

		/// <summary>Timestep of the GameSpeed named by DefaultSpeed, read out of the real mod.yaml.</summary>
		static int DefaultTimestepMs()
		{
			var manifest = ReadRepoFile("mods", "ww3mod", "mod.yaml");

			var defaultSpeed = Regex.Match(manifest, @"^\s*DefaultSpeed:\s*(\S+)\s*$", RegexOptions.Multiline);
			Assert.That(defaultSpeed.Success, Is.True, "mod.yaml no longer declares GameSpeeds: DefaultSpeed");

			// The named speed's block, up to its Timestep line.
			var block = Regex.Match(manifest,
				@"^\s*" + Regex.Escape(defaultSpeed.Groups[1].Value) + @":\s*$(?:(?!^\t\t\w).)*?^\s*Timestep:\s*(\d+)\s*$",
				RegexOptions.Multiline | RegexOptions.Singleline);
			Assert.That(block.Success, Is.True,
				$"mod.yaml declares DefaultSpeed: {defaultSpeed.Groups[1].Value} but no Timestep could be read for it");

			return int.Parse(block.Groups[1].Value, CultureInfo.InvariantCulture);
		}

		static string Scenario(string name)
		{
			return ReadRepoFile("tools", "autotest", "scenarios", name, name + ".lua");
		}

		static int ReadInt(string source, string what, string pattern)
		{
			var m = Regex.Match(source, pattern, RegexOptions.Multiline);
			Assert.That(m.Success, Is.True,
				$"could not read {what} — the scenario has been re-authored and this pin must be "
				+ "re-pointed rather than deleted");
			return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
		}

		[Test]
		public void SingleTestRunsPlayAtTheModDefaultTimestep()
		{
			// If this changes, every "seconds" figure in every scenario changes meaning at once, and
			// the tripwires below stop describing the suite that actually runs.
			Assert.That(DefaultTimestepMs(), Is.EqualTo(60),
				"the mod's default GameSpeed Timestep has moved. Autotest single-test runs play at this "
				+ "speed (Game.LoadMap hardcodes \"default\"; run-test.sh never passes Test.GameSpeed), so "
				+ "every seconds-based scenario deadline has just changed meaning. Re-derive the harness "
				+ "conversion and re-check the scenario tripwires below before updating this number");

			// And the harness must be looking at the same number, not at a copy that drifted.
			Assert.That(HarnessNumber("TestHarness.TimestepMs"), Is.EqualTo((double)DefaultTimestepMs()),
				"TestHarness.TimestepMs no longer mirrors mod.yaml's default GameSpeed Timestep. Every "
				+ "AssertWithin window is derived from it, so the harness is now converting seconds at a "
				+ "rate the game does not run at");
		}

		[Test]
		public void TheHarnessConversionMatchesTheEngineConversion()
		{
			// Assert the ENGINE side by the arithmetic the engine performs (DateTimeGlobal ->
			// TickTime.TicksForSeconds), not by the `1000 / Timestep` it used to perform: that expression
			// truncated to 16, and pinning 16 here would re-pin the defect rather than the behaviour.
			var engineTicksForSixtySeconds = TickTime.TicksForSeconds(60, DefaultTimestepMs());
			Assert.That(engineTicksForSixtySeconds, Is.EqualTo(1000),
				"the engine's Lua seconds->ticks conversion has moved; DateTime.Seconds(n) in every "
				+ "scenario has changed meaning. 960 means the truncated `1000 / Timestep` is back");

			// The two bases, stated as the relationship scenario authors rely on. This was an assertion
			// that they differ by exactly 1.5 until the harness was corrected on 2026-09-21. Compared
			// through the harness's own converter, not as rate * 60: the rate is 1000/60, which has no
			// exact double, so rate * 60 is 1000.0000000000001 and an equality on it tests IEEE rounding
			// rather than the agreement this fixture is about.
			Assert.That(HarnessTicksForSecondsRange(60, DefaultTimestepMs())[60], Is.EqualTo(engineTicksForSixtySeconds),
				"the harness and engine conversions no longer agree, so every scenario that mixes "
				+ "AssertWithin with DateTime.Seconds has changed meaning. 1500 means the old hardcoded "
				+ "25 is back");

			Assert.That(HarnessTicksPerSecond(), Is.EqualTo(1000.0 / 60),
				"TestHarness.TicksPerSecond has been edited. It must stay derived from the mod's default "
				+ "Timestep: 25 is the old hardcoded harness literal, which made every AssertWithin window "
				+ "1.5x longer than it read, and 16 is the old TRUNCATED engine value — DOCS/reference/"
				+ "conventions.md:1107 records the prescription to aim at 16 as exactly backwards. Either "
				+ "one re-opens a divergence between AssertWithin and DateTime.Seconds");
		}

		[Test]
		public void HarnessAndEngineAgreeTickForTickOnEveryIntegerSecond()
		{
			// The deliverable of the 2026-09-21 flip, executed rather than restated: AssertWithin(n) and
			// DateTime.Seconds(n) must land on the SAME tick. This is what fails if TicksForSeconds is
			// reverted to `seconds * TicksPerSecond` on a rate with no exact double, or if the multiply
			// and the divide are swapped back into a truncating `1000 / Timestep` rate.
			const int MaxSeconds = 600;
			var timestepMs = DefaultTimestepMs();
			var harness = HarnessTicksForSecondsRange(MaxSeconds, timestepMs);

			for (var n = 0; n <= MaxSeconds; n++)
				Assert.That(harness[n], Is.EqualTo(TickTime.TicksForSeconds(n, timestepMs)),
					$"TestHarness.TicksForSeconds({n}) disagrees with DateTime.Seconds({n}) at a "
					+ $"{timestepMs} ms timestep. The harness conversion has stopped mirroring "
					+ "TickTime.TicksForSeconds (multiply before divide, then truncate)");
		}

		/// <summary>
		/// Runs the shipped TestHarness.TicksForSeconds over 0..maxSeconds at a given timestep.
		/// </summary>
		static int[] HarnessTicksForSecondsRange(int maxSeconds, int timestepMs)
		{
			using (var runtime = new LuaRuntime())
			{
				runtime.DoBuffer(HarnessSource(), "test-helpers.lua").Dispose();
				runtime.DoBuffer(OverrideTimestep(timestepMs), "timestep").Dispose();

				var chunk = "local out = {} for n = 0, " + maxSeconds.ToString(CultureInfo.InvariantCulture)
					+ " do out[#out + 1] = TestHarness.TicksForSeconds(n) end return table.concat(out, \",\")";

				using (var results = runtime.DoBuffer(chunk, "range"))
				{
					Assert.That(results.Count, Is.GreaterThan(0), "test-helpers.lua no longer exposes TestHarness.TicksForSeconds");
					return results[0].ToString().Split(',')
						.Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
				}
			}
		}

		/// <summary>
		/// The timesteps a scenario deadline must survive: the one the mod ships, and 40 ms — the RA/cnc
		/// value, which derives the harness's former hardcoded 25 ticks/second. A budget written as
		/// `ticks / TestHarness.TicksPerSecond` round-trips exactly at both.
		/// </summary>
		static readonly int[] CandidateTimestepsMs = { 60, 40 };

		/// <summary>
		/// Puts `ticks` through the round-trip a scenario performs — `ticks / TestHarness.TicksPerSecond`
		/// into AssertWithin — by running the SHIPPED helper at the given timestep. Reproducing that
		/// arithmetic in C# instead would be restating the thing under test, including its epsilon.
		/// </summary>
		static int HarnessRoundTrip(int ticks, int timestepMs)
		{
			using (var runtime = new LuaRuntime())
			{
				runtime.DoBuffer(HarnessSource(), "test-helpers.lua").Dispose();
				runtime.DoBuffer(OverrideTimestep(timestepMs), "timestep").Dispose();

				var chunk = "local seconds = " + ticks.ToString(CultureInfo.InvariantCulture)
					+ " / TestHarness.TicksPerSecond return TestHarness.TicksForSeconds(seconds)";

				using (var results = runtime.DoBuffer(chunk, "roundtrip"))
					return (int)results[0].ToNumber().Value;
			}
		}

		/// <summary>
		/// Asserts a scenario's outer deadline covers what the run needs at EVERY candidate timestep —
		/// i.e. that the deadline is immune to TestHarness.TicksPerSecond rather than merely large
		/// enough at today's value. `outerTicks` is the scenario's tick budget; the round-trip through
		/// seconds and back is performed by the shipped helper.
		/// </summary>
		static void AssertDeadlineIsRateImmune(string name, int outerTicks, int neededTicks, string composition)
		{
			foreach (var timestepMs in CandidateTimestepsMs)
			{
				var effective = HarnessRoundTrip(outerTicks, timestepMs);

				Assert.That(effective, Is.EqualTo(outerTicks),
					$"{name}: the ticks->seconds->ticks round-trip is lossy at a {timestepMs} ms timestep "
					+ $"({outerTicks} became {effective}). Pick a budget that round-trips exactly rather "
					+ "than absorbing the loss into headroom");

				Assert.That(effective, Is.GreaterThan(neededTicks),
					$"{name} cannot pass at a {timestepMs} ms timestep: its outer deadline is {effective} "
					+ $"ticks but the run needs {neededTicks} ({composition}). Those needed ticks do NOT "
					+ "scale with TestHarness.TicksPerSecond, so the deadline must not either — budget it "
					+ "in ticks and divide by TestHarness.TicksPerSecond. Do not widen it to clear this");
			}
		}

		/// <summary>Guards the idiom itself: a seconds LITERAL here is the defect these tests exist for.</summary>
		static void AssertOuterDeadlineIsConvertedNotLiteral(string name, string src)
		{
			Assert.That(Regex.IsMatch(src, @"^\s*local\s+OuterSeconds\s*=\s*OuterTicks\s*/\s*TestHarness\.TicksPerSecond\s*$", RegexOptions.Multiline),
				Is.True,
				$"{name} no longer derives OuterSeconds from a tick budget. That division is what makes "
				+ "its deadline immune to TestHarness.TicksPerSecond; without it the scenario is a "
				+ "casualty of that constant again");

			Assert.That(Regex.IsMatch(src, @"TestHarness\.AssertWithin\(\s*\d"), Is.False,
				$"{name} passes a seconds LITERAL to AssertWithin. That is the exact defect this fixture "
				+ "guards: the literal scales with TestHarness.TicksPerSecond while everything it has to "
				+ "cover does not. Pass OuterSeconds");
		}

		/// <summary>
		/// Loads the REAL scenario Lua at the given timestep and returns (tick budget, deadline
		/// AssertWithin would actually use). This executes the shipped arithmetic instead of restating
		/// it in C#, which is the difference between checking that the scenario is written the way we
		/// think and checking what it computes. Appended to the same chunk because OuterTicks and
		/// OuterSeconds are file-scope LOCALS and a second chunk cannot see them. Only the scenario's top
		/// level runs — WorldLoaded is defined, never called — so no engine-bound global is touched and
		/// this needs no world.
		/// </summary>
		static (int Budget, int Effective) RunScenarioDeadlineAt(string scenarioName, int timestepMs)
		{
			var scenario = Scenario(scenarioName);

			using (var runtime = new LuaRuntime())
			{
				runtime.DoBuffer(HarnessSource(), "test-helpers.lua").Dispose();

				// The flip, simulated exactly: the scenario reads these as it loads.
				runtime.DoBuffer(OverrideTimestep(timestepMs), "timestep").Dispose();

				var probe = scenario
					+ "\nreturn tostring(OuterTicks) .. \":\" .. tostring(TestHarness.TicksForSeconds(OuterSeconds))";

				using (var results = runtime.DoBuffer(probe, scenarioName + ".lua"))
				{
					Assert.That(results.Count, Is.GreaterThan(0),
						$"{scenarioName} did not yield OuterTicks/OuterSeconds at file scope");
					var parts = results[0].ToString().Split(':');
					return (int.Parse(parts[0], CultureInfo.InvariantCulture),
						int.Parse(parts[1], CultureInfo.InvariantCulture));
				}
			}
		}

		/// <summary>
		/// THE DELIVERABLE, EXECUTED. Both scenarios are loaded for real at the shipped 60 ms timestep
		/// (16.667 ticks/second) and at 40 ms (25 ticks/second, the harness's former hardcoded value),
		/// and the deadline AssertWithin would use is read back out of Lua. Byte-identical at both is
		/// what "immune to the constant" means; the arithmetic above only shows we believe it.
		/// </summary>
		[TestCase("test-autotarget-preempt-air", 174)]
		[TestCase("test-critical-no-panic", 325)]
		public void ScenarioDeadlineIsIdenticalAtBothRatesWhenTheLuaActuallyRuns(string scenarioName, int neededTicks)
		{
			var atShippedRate = RunScenarioDeadlineAt(scenarioName, 60);
			var atOldHarnessRate = RunScenarioDeadlineAt(scenarioName, 40);

			Assert.That(atShippedRate.Effective, Is.EqualTo(atOldHarnessRate.Effective),
				$"{scenarioName} computes a different deadline at 16.667 ({atShippedRate.Effective} ticks) "
				+ $"than at 25 ({atOldHarnessRate.Effective} ticks), so it is sensitive to "
				+ "TestHarness.TicksPerSecond again");

			Assert.That(atShippedRate.Effective, Is.EqualTo(atShippedRate.Budget),
				$"{scenarioName}: the ticks->seconds->ticks round-trip lost a tick at the shipped 16.667");
			Assert.That(atOldHarnessRate.Effective, Is.EqualTo(atOldHarnessRate.Budget),
				$"{scenarioName}: the ticks->seconds->ticks round-trip lost a tick at 25");

			Assert.That(atShippedRate.Effective, Is.GreaterThan(neededTicks),
				$"{scenarioName} budgets {atShippedRate.Effective} ticks but the run needs {neededTicks}. "
				+ "That requirement is a sum of raw tick counts and does not move with the constant");
		}

		[Test]
		public void PreemptAirDeadlineIsImmuneToTheHarnessConstant()
		{
			// Formerly the clearest casualty of correcting the constant. Its outer timeout was the
			// seconds literal 10 while the two quantities it must cover were on the engine base, so
			// lowering the harness rate moved one side of the comparison and not the other. Note the
			// failure mode was NOT a red run: at 16 the outer expired at 160 against 174 needed, so a
			// healthy build still passed at ~155 while DeadlineTicks became unreachable and stopped
			// being enforced. Now every quantity is a tick count and the deadline round-trips.
			const string Name = "test-autotarget-preempt-air";
			var src = Scenario(Name);

			var spawnTicks = ReadInt(src, Name + " SpawnHeliAfterTicks", @"^\s*local\s+SpawnHeliAfterTicks\s*=\s*(\d+)");
			var deadlineTicks = ReadInt(src, Name + " DeadlineTicks", @"^\s*local\s+DeadlineTicks\s*=\s*(\d+)");
			var headroom = ReadInt(src, Name + " OuterTicks headroom",
				@"^\s*local\s+OuterTicks\s*=\s*SpawnHeliAfterTicks\s*\+\s*DeadlineTicks\s*\+\s*(\d+)");

			// The spawn delay is a raw Trigger.AfterDelay, deliberately NOT DateTime.Seconds. The two
			// converters agree today, but DateTime.Seconds still moves with mod.yaml's Timestep while
			// DeadlineTicks does not, so keeping it raw keeps the pair locked together.
			Assert.That(Regex.IsMatch(src, @"DateTime\.Seconds\(\s*\w+\s*\)\s*,\s*function"), Is.False,
				$"{Name} schedules from DateTime.Seconds again. That converts through mod.yaml's Timestep, "
				+ "so the spawn delay would move with the game speed while DeadlineTicks does not. Keep it "
				+ "a raw tick count");

			AssertDeadlineIsRateImmune(Name, spawnTicks + deadlineTicks + headroom, spawnTicks + deadlineTicks,
				$"spawn delay {spawnTicks} + DeadlineTicks {deadlineTicks}");
			AssertOuterDeadlineIsConvertedNotLiteral(Name, src);
		}

		[Test]
		public void CriticalNoPanicDeadlineIsImmuneToTheHarnessConstant()
		{
			// The tighter of the two, and the one that genuinely inverted: the observation window is a
			// raw tick count that does not move, sitting inside a deadline that did. At 16 the deadline
			// was 320 ticks against 325 needed, so the predicate could never reach `ticks >=
			// ObserveTicks` and the run always failed having measured nothing.
			const string Name = "test-critical-no-panic";
			var src = Scenario(Name);

			var observeTicks = ReadInt(src, Name + " ObserveTicks", @"^\s*local\s+ObserveTicks\s*=\s*(\d+)");
			var setupTicks = ReadInt(src, Name + " SetupTicks", @"^\s*local\s+SetupTicks\s*=\s*(\d+)");
			var headroom = ReadInt(src, Name + " OuterTicks headroom",
				@"^\s*local\s+OuterTicks\s*=\s*SetupTicks\s*\+\s*ObserveTicks\s*\+\s*(\d+)");

			AssertDeadlineIsRateImmune(Name, setupTicks + observeTicks + headroom, setupTicks + observeTicks,
				$"setup {setupTicks} + ObserveTicks {observeTicks}, both raw tick counts that do NOT scale");
			AssertOuterDeadlineIsConvertedNotLiteral(Name, src);
		}
	}
}
