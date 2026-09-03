#region Copyright & License Information
/*
 * WW3MOD capture/clear durations (2026-09-03).
 *
 * WHAT THIS PINS. How long a soldier spends taking an enemy-held structure, and how long a
 * technician spends taking a neutral one. Both are YAML data on the two Captures templates in
 * infantry.yaml, so nothing in the build or the linter would notice either number changing —
 * which is exactly why the durations are asserted here rather than left to a comment.
 *
 * THE TICK RATE IS THE TRAP. This mod runs at Timestep 60 ms (mod.yaml GameSpeeds, DefaultSpeed:
 * default), i.e. 16.667 ticks/second. It does NOT run at 25 tps, and this project has already
 * shipped duration comments that were wrong by 1.5x because they assumed it did (conventions.md,
 * "A change believed made, documented as made, and inert"). Every fixture below therefore asserts
 * the DURATION IN SECONDS as well as the raw tick count: the tick count is the thing somebody
 * edits, the duration is the thing that was actually agreed.
 *
 * WHERE THE DELAY IS SPENT, because it is not where the field name suggests. CaptureManager.cs:232
 * sets enteringCurrentTarget = currentTargetDelay >= CaptureDelay, and currentTargetDelay
 * increments once per tick from Enter's Approaching state — so the whole delay is spent STANDING
 * ADJACENT to the building, before the unit enters it. It is the entirety of the "waits about,
 * then goes in" that a player sees. The walk to reach the building is the only other cost and is
 * distance-dependent, so there is no second constant to tune.
 *
 * WHAT NO FIXTURE HERE COVERS. That the durations FEEL right. These assert the numbers the mod
 * ships, not that 30 s is the correct answer to "clearing should be faster".
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class CaptureClearDurationTest
	{
		// mod.yaml GameSpeeds 'default'. Named rather than inlined so a reader cannot mistake the
		// arithmetic below for an assumption about 25 tps.
		const int DefaultTimestepMilliseconds = 60;
		const int MillisecondsPerSecond = 1000;

		static string InfantryRulesPath()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules", "ingame", "infantry.yaml");
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/rules/ingame/infantry.yaml");
		}

		/// <summary>The CaptureDelay declared on one template's Captures trait, in ticks.</summary>
		static int CaptureDelayOf(string template)
		{
			var nodes = MiniYaml.FromFile(InfantryRulesPath());

			var node = nodes.FirstOrDefault(n => n.Key == template);
			Assert.That(node, Is.Not.Null,
				$"{template} is no longer a top-level node in infantry.yaml. Note MiniYaml merges " +
				"top-level keys CASE-SENSITIVELY, so a rename that only changed case would land here.");

			// The trait is suffixed (Captures@OCCUPIED / Captures@CAPTURESNEUTRAL). Matching on the
			// prefix keeps the fixture working if the suffix is renamed, which is cosmetic, while
			// still failing if the trait is removed, which is not.
			var captures = node.Value.Nodes
				.FirstOrDefault(n => n.Key == "Captures" || n.Key.StartsWith("Captures@", StringComparison.Ordinal));

			Assert.That(captures, Is.Not.Null, $"{template} declares no Captures trait.");

			var delay = captures.Value.Nodes.FirstOrDefault(n => n.Key == "CaptureDelay")?.Value.Value?.Trim();
			Assert.That(delay, Is.Not.Null,
				$"{template} no longer sets CaptureDelay explicitly, so it silently falls back to the " +
				"CapturesInfo C# default of 0 — an instant capture. That is a behaviour change, not a " +
				"tidy-up.");

			return int.Parse(delay, System.Globalization.CultureInfo.InvariantCulture);
		}

		[Test]
		public void ClearingAnEnemyStructureTakesThirtySeconds()
		{
			var ticks = CaptureDelayOf("^CapturesOccupiedBuildings");

			Assert.Multiple(() =>
			{
				Assert.That(ticks, Is.EqualTo(500),
					"the enemy-clear delay moved. It was halved from 1000 to 500 ticks on 2026-09-03 " +
					"because clearing an oil derrick read as interminable; if you are changing it again, " +
					"change the seconds assertion below with it and say so in the commit.");

				Assert.That(ticks * DefaultTimestepMilliseconds / MillisecondsPerSecond, Is.EqualTo(30),
					"the enemy-clear delay no longer reads as 30 seconds to a player. This is the " +
					"assertion that catches an edit made against the wrong tick rate: at the 25 tps " +
					"several in-tree comments still assume, 500 ticks would look like 20 s, and a " +
					"'make it 30 seconds' edit computed that way would land on 750 and actually buy 45.");
			});
		}

		[Test]
		public void TakingANeutralStructureStaysEssentiallyImmediate()
		{
			// The technician's own delay is deliberately NOT halved and is asserted so that a future
			// sweep of "capture is too slow" does not quietly touch it. At 20 ticks it is already
			// 1.2 s — the pause is a readability beat, not a cost, and halving it would only make the
			// capture look like it happened by accident.
			var ticks = CaptureDelayOf("^CapturesNeutralBuildings");

			Assert.Multiple(() =>
			{
				Assert.That(ticks, Is.EqualTo(20), "the neutral-capture delay moved.");

				Assert.That(ticks * DefaultTimestepMilliseconds, Is.EqualTo(1200),
					"the neutral capture no longer takes 1.2 s.");
			});
		}

		[Test]
		public void ClearingAnEnemyStructureCostsMoreThanTakingANeutralOne()
		{
			// The ordering is the design, not an accident of two numbers: an enemy building is
			// contested and a neutral one is not. A change that inverted this would mean walking into
			// a defended structure was cheaper than walking into an empty one.
			Assert.That(CaptureDelayOf("^CapturesOccupiedBuildings"),
				Is.GreaterThan(CaptureDelayOf("^CapturesNeutralBuildings")),
				"clearing an enemy-held structure is now no slower than taking an undefended neutral " +
				"one, which inverts the intended cost of contesting a building.");
		}

		[Test]
		public void InfantryCoverACellInAboutFortyOneTicks()
		{
			// NOT a balance assertion — a guard on the premise every capture AUTOTEST BUDGET rests on.
			//
			// Both capture scenarios allow a technician a fixed number of ticks to walk to a derrick
			// and take it. That allowance is only meaningful against a known speed, and the first run
			// of test-capture-click-selected-and-enemy failed purely because the budget was computed
			// without it: 20-cell walks against 900 ticks, when 20 cells cost ~819 ticks of pure
			// movement. The verdict read "dispatched at but never captured", which looks exactly like
			// a defect in the capture activity, and was reported as one.
			//
			// If infantry speed is ever retuned, the scenarios silently go tight again and fail the
			// same misleading way. This is the thing that fails loudly first.
			var nodes = MiniYaml.FromFile(InfantryRulesPath());

			var infantry = nodes.FirstOrDefault(n => n.Key == "^Infantry");
			Assert.That(infantry, Is.Not.Null, "^Infantry is no longer a top-level node in infantry.yaml.");

			var mobile = infantry.Value.Nodes.FirstOrDefault(n => n.Key == "Mobile");
			Assert.That(mobile, Is.Not.Null, "^Infantry no longer declares Mobile.");

			var speed = mobile.Value.Nodes.FirstOrDefault(n => n.Key == "Speed")?.Value.Value?.Trim();
			Assert.That(speed, Is.Not.Null, "^Infantry's Mobile no longer sets Speed explicitly.");

			var unitsPerTick = int.Parse(speed, System.Globalization.CultureInfo.InvariantCulture);

			Assert.Multiple(() =>
			{
				Assert.That(unitsPerTick, Is.EqualTo(25), "infantry speed moved.");

				// 1024 world units to the cell. Asserted as a range because the point is the ORDER OF
				// MAGNITUDE the budgets were sized against, not the exact quotient.
				var ticksPerCell = 1024 / unitsPerTick;
				Assert.That(ticksPerCell, Is.InRange(35, 45),
					"a technician no longer covers a cell in roughly 41 ticks, so every walk budget in " +
					"tools/autotest/scenarios/test-capture-* was sized against the wrong speed. Those " +
					"budgets carry the arithmetic in their map.yaml headers — re-derive them before " +
					"trusting a green run, and expect a red one to blame the capture activity rather " +
					"than the clock.");
			});
		}
	}
}
