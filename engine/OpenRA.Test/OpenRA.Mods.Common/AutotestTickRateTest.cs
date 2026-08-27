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
	/// number is left alone and the RELATIONSHIPS it silently holds up are made executable instead:
	/// the two scenarios below are the ones whose arithmetic provably inverts, and they now fail at
	/// `dotnet test` the moment anyone edits the constant, instead of failing invisibly in a game
	/// nobody reran.
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

		/// <summary>AssertWithin/AssertAfter floor the product, so the tripwires must too.</summary>
		static int HarnessTicks(double seconds)
		{
			return (int)Math.Floor(seconds * HarnessTicksPerSecond());
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
				"TestHarness.TicksPerSecond has been edited. That constant is NOT free to change: 91 "
				+ "scenario deadlines across 137 files were authored against it, some deliberately "
				+ "(test-tunguska-missile-standoff:25, test-depot-vacate-phantom:32). Correcting it to "
				+ "the engine's 16 shortens every one of them by a third. The two tripwires in this "
				+ "fixture name the deadlines that provably invert; run the suite and re-author them "
				+ "rather than deleting this assertion");
		}

		[Test]
		public void PreemptAirOuterTimeoutStillOutlastsItsInnerBudget()
		{
			// The scenario's own comment (:141) does this arithmetic and asserts 10 * 25 = 250 > 174.
			// It is the clearest provable casualty of correcting the constant: the outer timeout is on
			// the harness base, the two quantities it must cover are on the engine base, so lowering
			// the harness rate moves one side of the comparison and not the other.
			const string Name = "test-autotarget-preempt-air";
			var src = Scenario(Name);

			var spawnSeconds = ReadInt(src, Name + " SpawnHeliAfterSeconds", @"^\s*local\s+SpawnHeliAfterSeconds\s*=\s*(\d+)\s*$");
			var deadlineTicks = ReadInt(src, Name + " DeadlineTicks", @"^\s*local\s+DeadlineTicks\s*=\s*(\d+)\s*$");
			var outerSeconds = ReadInt(src, Name + " outer AssertWithin", @"TestHarness\.AssertWithin\((\d+),");

			// Trigger.AfterDelay(DateTime.Seconds(SpawnHeliAfterSeconds), ...) — engine base.
			var innerTicks = (spawnSeconds * (1000 / DefaultTimestepMs())) + deadlineTicks;
			var outerTicks = HarnessTicks(outerSeconds);

			Assert.That(outerTicks, Is.GreaterThan(innerTicks),
				$"{Name} can no longer pass: its outer AssertWithin({outerSeconds}) expires after "
				+ $"{outerTicks} ticks, but the run needs {innerTicks} ticks ({spawnSeconds}s spawn delay "
				+ $"on the ENGINE base plus DeadlineTicks={deadlineTicks}). The outer timeout is the only "
				+ "one of those three quantities that moves with TestHarness.TicksPerSecond, so lowering "
				+ "that constant makes this scenario structurally impossible to pass. Re-author the "
				+ "deadline deliberately — do not widen it just to clear this assertion");
		}

		[Test]
		public void CriticalNoPanicObservationWindowStillFitsInsideItsDeadline()
		{
			// Second casualty, and the tighter of the two: the observation window is a raw tick count
			// that does not move, sitting inside a deadline that does.
			const string Name = "test-critical-no-panic";
			var src = Scenario(Name);

			var observeTicks = ReadInt(src, Name + " ObserveTicks", @"^\s*local\s+ObserveTicks\s*=\s*(\d+)");
			var setupTicks = ReadInt(src, Name + " setup delay", @"Trigger\.AfterDelay\((\d+),");
			var outerSeconds = ReadInt(src, Name + " outer AssertWithin", @"TestHarness\.AssertWithin\((\d+),");

			var needed = setupTicks + observeTicks;
			var outerTicks = HarnessTicks(outerSeconds);

			Assert.That(outerTicks, Is.GreaterThan(needed),
				$"{Name} can no longer pass: the window needs {needed} ticks (setup {setupTicks} + "
				+ $"ObserveTicks {observeTicks}, both raw tick counts that do NOT scale) but "
				+ $"AssertWithin({outerSeconds}) now expires after {outerTicks}. At the engine's 16 "
				+ "ticks/second this deadline is 320 ticks against 325 needed. Re-author it deliberately "
				+ "rather than nudging it up to clear this assertion");
		}
	}
}
