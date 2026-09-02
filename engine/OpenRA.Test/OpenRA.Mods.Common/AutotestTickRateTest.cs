using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Eluant;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the seconds -> ticks conversion the Lua autotest harness runs on, and the scenario
	/// deadlines that were sized against it.
	///
	/// THE STANDING DEFECT. TestHarness.TicksPerSecond is 25. Single-test autotest runs are played at
	/// the mod's "default" GameSpeed (Game.LoadMap hardcodes "default" unless Test.GameSpeed overrides
	/// it, and tools/autotest/run-test.sh never passes that), whose Timestep is 60 ms. The engine's own
	/// Lua converter derives 1000 / 60 = 16 ticks per second by INTEGER division
	/// (DateTimeGlobal.cs:31), so DateTime.Seconds(n) and TestHarness.AssertWithin(n) disagree by
	/// 25/16 — every harness "second" is worth about 1.56 engine seconds.
	///
	/// WHY THIS FIXTURE EXISTS RATHER THAN A CORRECTED CONSTANT. The error runs in the LENIENT
	/// direction, so 137 scenarios were authored, tuned and accepted against it, several of them
	/// knowingly (test-tunguska-missile-standoff:25 "Left alone deliberately";
	/// test-depot-vacate-phantom:32 "Generous on purpose"). Correcting the constant shortens 91
	/// deadlines by a third at once, and it cannot be validated without running the suite. So the
	/// number is left alone and the RELATIONSHIPS it silently holds up are made executable instead.
	///
	/// WHAT CHANGED, 2026-09-02. The two scenarios this fixture used to pin as casualties are no
	/// longer sensitive to the constant at all: both now budget in TICKS and convert back with
	/// `ticks / TestHarness.TicksPerSecond`, which round-trips exactly through the Math.Floor in
	/// AssertWithin. The tests below no longer assert "still passes at 25" — they evaluate each
	/// deadline at EVERY rate in CandidateRates and require it to hold at all of them, so they are
	/// proofs of immunity rather than pins on a fragile relationship. The gate on the constant
	/// itself stays, because the OTHER seconds-literal deadlines across the suite have not been
	/// audited and that is a full-suite question rather than a `dotnet test` one.
	///
	/// A CORRECTION TO THE ORIGINAL FRAMING, worth keeping because it inverts the risk. Only ONE of
	/// the two — test-critical-no-panic — actually went red at 16; its deadline fell 5 ticks short of
	/// the window it contains, so the predicate could never succeed. test-autotarget-preempt-air did
	/// NOT become "structurally impossible to pass": a healthy build engaged at poll ~155 against a
	/// 160-tick deadline and kept passing, while its documented 110-tick responsiveness budget became
	/// unreachable at 174 and silently stopped being enforced. A green that has quietly stopped
	/// measuring is the worse of the two failures and the harder to notice.
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

		/// <summary>
		/// The harness constant, read by EXECUTING the real helper rather than restating its value. A
		/// fixture that declares 25 itself agrees with itself whatever the helper ships.
		/// The engine-bound helpers in that file are never called and Lua resolves globals at call
		/// time, so loading it needs no world.
		/// </summary>
		static double HarnessTicksPerSecond()
		{
			var helper = ReadRepoFile("mods", "ww3mod", "scripts", "test-helpers.lua");
			using (var runtime = new LuaRuntime())
			{
				runtime.DoBuffer(helper, "test-helpers.lua").Dispose();
				using (var results = runtime.DoBuffer("return TestHarness.TicksPerSecond", "tps"))
				{
					Assert.That(results.Count, Is.GreaterThan(0),
						"test-helpers.lua no longer exposes TestHarness.TicksPerSecond; the scenario "
						+ "deadlines pinned below are derived from it and the pin must be re-pointed "
						+ "rather than deleted");
					return double.Parse(results[0].ToString(), CultureInfo.InvariantCulture);
				}
			}
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
			// the two tripwires below stop describing the suite that actually runs.
			Assert.That(DefaultTimestepMs(), Is.EqualTo(60),
				"the mod's default GameSpeed Timestep has moved. Autotest single-test runs play at this "
				+ "speed (Game.LoadMap hardcodes \"default\"; run-test.sh never passes Test.GameSpeed), so "
				+ "every seconds-based scenario deadline has just changed meaning. Re-derive the harness "
				+ "conversion and re-check the scenario tripwires below before updating this number");
		}

		[Test]
		public void TheHarnessConversionDisagreesWithTheEngineConversion()
		{
			// DateTimeGlobal.cs:31 — `1000 / Timestep` in INTEGER arithmetic, so 60 ms yields 16, not
			// 16.67. Scenarios mixing DateTime.Seconds with AssertWithin are mixing these two bases,
			// which is exactly the trap test-autotarget-preempt-air:70-77 documents.
			var engineTicksPerSecond = 1000 / DefaultTimestepMs();
			Assert.That(engineTicksPerSecond, Is.EqualTo(16),
				"the engine's Lua seconds->ticks rate has moved; DateTime.Seconds(n) in every scenario "
				+ "has changed meaning");

			Assert.That(HarnessTicksPerSecond(), Is.EqualTo(25.0),
				"TestHarness.TicksPerSecond has been edited. The two scenarios that used to invert on "
				+ "this constant no longer do — they are budgeted in ticks and converted back, and the "
				+ "immunity tripwires below prove it at BOTH rates — but the constant is still not free "
				+ "to change unattended: the remaining seconds-literal deadlines across the suite were "
				+ "authored against 25, some deliberately (test-tunguska-missile-standoff:25, "
				+ "test-depot-vacate-phantom:32), and correcting it to the engine's 16 shortens every "
				+ "one of them by a third. That is a full-suite question, not a dotnet-test one. This "
				+ "assertion is the gate on making it deliberately; do not delete it to go green");
		}

		/// <summary>
		/// The rates a scenario deadline must survive: the harness constant as it ships today, and
		/// the engine's real rate that a future correction would move it to. A budget written as
		/// `ticks / TestHarness.TicksPerSecond` round-trips through Math.Floor at both.
		/// </summary>
		static readonly double[] CandidateRates = { 25.0, 16.0 };

		/// <summary>
		/// Asserts a scenario's outer deadline covers what the run needs at EVERY candidate rate —
		/// i.e. that the deadline is immune to TestHarness.TicksPerSecond rather than merely large
		/// enough at today's value. `outerTicks` is the scenario's tick budget; the round-trip
		/// through seconds and back is reproduced exactly as test-helpers.lua performs it.
		/// </summary>
		static void AssertDeadlineIsRateImmune(string name, int outerTicks, int neededTicks, string composition)
		{
			foreach (var rate in CandidateRates)
			{
				// Exactly what the scenario computes, then exactly what AssertWithin does with it.
				var outerSeconds = outerTicks / rate;
				var effective = (int)Math.Floor(outerSeconds * rate);

				Assert.That(effective, Is.EqualTo(outerTicks),
					$"{name}: the ticks->seconds->ticks round-trip is lossy at {rate} ticks/second "
					+ $"({outerTicks} became {effective}). Pick a budget that round-trips exactly rather "
					+ "than absorbing the loss into headroom");

				Assert.That(effective, Is.GreaterThan(neededTicks),
					$"{name} cannot pass at {rate} ticks/second: its outer deadline is {effective} ticks "
					+ $"but the run needs {neededTicks} ({composition}). Those needed ticks do NOT scale "
					+ "with TestHarness.TicksPerSecond, so the deadline must not either — budget it in "
					+ "ticks and divide by TestHarness.TicksPerSecond. Do not widen it to clear this");
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
		/// Loads the REAL scenario Lua with TestHarness.TicksPerSecond forced to `ticksPerSecond` and
		/// returns (tick budget, deadline AssertWithin would actually use). This executes the shipped
		/// arithmetic instead of restating it in C#, which is the difference between checking that the
		/// scenario is written the way we think and checking what it computes. Appended to the same
		/// chunk because OuterTicks/OuterSeconds are file-scope LOCALS and a second chunk cannot see
		/// them. Only the scenario's top level runs — WorldLoaded is defined, never called — so no
		/// engine-bound global is touched and this needs no world.
		/// </summary>
		static (int Budget, int Effective) RunScenarioDeadlineAt(string scenarioName, double ticksPerSecond)
		{
			var helper = ReadRepoFile("mods", "ww3mod", "scripts", "test-helpers.lua");
			var scenario = Scenario(scenarioName);

			using (var runtime = new LuaRuntime())
			{
				runtime.DoBuffer(helper, "test-helpers.lua").Dispose();

				// The flip, simulated exactly: the scenario reads this constant as it loads.
				runtime.DoBuffer(
					"TestHarness.TicksPerSecond = " + ticksPerSecond.ToString(CultureInfo.InvariantCulture),
					"rate").Dispose();

				var probe = scenario
					+ "\nreturn tostring(OuterTicks) .. \":\" .. tostring(math.floor(OuterSeconds * TestHarness.TicksPerSecond))";

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
		/// THE DELIVERABLE, EXECUTED. Both scenarios are loaded for real at the harness constant as it
		/// ships (25) and at the engine's rate a correction would move it to (16), and the deadline
		/// AssertWithin would use is read back out of Lua. Byte-identical at both rates is what
		/// "immune to the constant" means; the arithmetic above only shows we believe it.
		/// </summary>
		[TestCase("test-autotarget-preempt-air", 174)]
		[TestCase("test-critical-no-panic", 325)]
		public void ScenarioDeadlineIsIdenticalAtBothRatesWhenTheLuaActuallyRuns(string scenarioName, int neededTicks)
		{
			var atHarnessRate = RunScenarioDeadlineAt(scenarioName, 25.0);
			var atEngineRate = RunScenarioDeadlineAt(scenarioName, 16.0);

			Assert.That(atHarnessRate.Effective, Is.EqualTo(atEngineRate.Effective),
				$"{scenarioName} computes a different deadline at 25 ({atHarnessRate.Effective} ticks) "
				+ $"than at 16 ({atEngineRate.Effective} ticks), so it is still sensitive to "
				+ "TestHarness.TicksPerSecond and the flip cannot be a one-line change");

			Assert.That(atHarnessRate.Effective, Is.EqualTo(atHarnessRate.Budget),
				$"{scenarioName}: the ticks->seconds->ticks round-trip lost a tick at 25");
			Assert.That(atEngineRate.Effective, Is.EqualTo(atEngineRate.Budget),
				$"{scenarioName}: the ticks->seconds->ticks round-trip lost a tick at 16");

			Assert.That(atEngineRate.Effective, Is.GreaterThan(neededTicks),
				$"{scenarioName} budgets {atEngineRate.Effective} ticks but the run needs {neededTicks}. "
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

			// The spawn delay is a raw Trigger.AfterDelay now, deliberately NOT DateTime.Seconds: that
			// would re-couple it to Timestep, which is the third tick base in play.
			Assert.That(Regex.IsMatch(src, @"DateTime\.Seconds\(\s*\w+\s*\)\s*,\s*function"), Is.False,
				$"{Name} schedules from DateTime.Seconds again. That is the ENGINE base (1000/Timestep, "
				+ "integer), so the spawn delay would move with mod.yaml's Timestep while DeadlineTicks "
				+ "does not. Keep it a raw tick count");

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
