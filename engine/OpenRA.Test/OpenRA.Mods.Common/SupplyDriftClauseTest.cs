using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Eluant;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the two things test-supply-safe-front-keeps-cargo's drift clause rests on, both of which
	/// were previously only prose:
	///
	///  1. TestHarness.DriftTracker really measures the PEAK over a run and not the value at verdict
	///     time. That distinction is the entire reason the clause exists — SeekSuppliesAndReturn walks
	///     a soldier back to where it started, so a platoon that abandoned the front is home and tidy
	///     by the time a naive check looks. The tracker is driven here against stub actors, in the same
	///     Lua runtime the game uses, so the assertion is executed rather than argued.
	///
	///  2. The geometry that makes HOLD_DRIFT = 1 the right allowance for that scenario. One cell is
	///     claimed to be exactly enough for every rifleman to reach a truck that has closed to aura
	///     range, and no more than enough. That is arithmetic over SupplyProvider.InAuraRange, so it
	///     can be checked directly instead of taken on trust.
	///
	/// WHY THIS FIXTURE AND NOT A GAME RUN. The scenario itself cannot be executed here, and at the
	/// time of writing it is red for an unrelated reason (a crate dropped on a quiet front, PIPELINE
	/// item 51/56) which would mask whether a newly added clause behaves. A pure function driven
	/// directly is worth more than a clause whose only evidence is a run that fails for other reasons.
	/// </summary>
	[TestFixture]
	public class SupplyDriftClauseTest
	{
		const string HelperRelativePath = "mods/ww3mod/scripts/test-helpers.lua";

		static string FindHelperScript()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "scripts", "test-helpers.lua");
				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}

		/// <summary>
		/// Read a `local NAME = &lt;int&gt;` declaration out of a scenario script. The allowance the drift
		/// clause rests on lives in the scenario file and nowhere else, so a test that restates the
		/// number as its own constant pins nothing — it agrees with itself no matter what the scenario
		/// says. Reading the real declaration is what makes the assertion able to fail.
		/// </summary>
		static int ReadScenarioConstant(string scenario, string name)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			string path = null;
			for (var i = 0; i < 10 && dir != null && path == null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "tools", "autotest", "scenarios", scenario, scenario + ".lua");
				if (File.Exists(candidate))
					path = candidate;
			}

			if (path == null)
				Assert.Ignore($"tools/autotest/scenarios/{scenario}/{scenario}.lua not reachable from the test assembly — drift allowance check skipped, not passed");

			var match = Regex.Match(File.ReadAllText(path), @"^\s*local\s+" + name + @"\s*=\s*(\d+)\s*$", RegexOptions.Multiline);
			Assert.That(match.Success, Is.True,
				$"{scenario}.lua no longer declares `local {name} = <integer>`; the drift allowance this "
				+ "fixture pins has been renamed or made dynamic, and the pin must be re-pointed rather "
				+ "than deleted");

			return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Load the real test-helpers.lua and run <paramref name="script"/> against it, returning the
		/// single string the script yields. Only TestHarness.CellDrift and TestHarness.DriftTracker are
		/// exercised; the engine-bound helpers in that file (Camera, UserInterface, Test, Trigger) are
		/// never called, and Lua resolves globals at call time, so loading the file needs no world.
		/// </summary>
		static string RunAgainstHelpers(string script)
		{
			var helper = FindHelperScript();
			if (helper == null)
				Assert.Ignore($"{HelperRelativePath} not reachable from the test assembly — drift check skipped, not passed");

			using (var runtime = new LuaRuntime())
			{
				runtime.DoBuffer(File.ReadAllText(helper), HelperRelativePath).Dispose();

				using (var results = runtime.DoBuffer(script, "drift-tracker-check"))
				{
					Assert.That(results.Count, Is.GreaterThan(0), "the check script returned nothing");
					return results[0].ToString();
				}
			}
		}

		/// <summary>
		/// Stub actors good enough for the tracker, which reads only IsDead and Location.X/.Y. Moving a
		/// stub REPLACES its Location table rather than mutating one in place, which is the conservative
		/// choice: it matches a binding with value semantics, and it means the tracker cannot pass this
		/// test by accidentally aliasing a cell object it was handed at construction.
		/// </summary>
		const string StubPrelude = @"
			local function actor(x, y)
				return { IsDead = false, Location = { X = x, Y = y } }
			end
			local function moveTo(a, x, y)
				a.Location = { X = x, Y = y }
			end
		";

		[Test]
		public void PeakIsKeptAfterTheActorWalksHomeAgain()
		{
			// THE CLAUSE'S WHOLE POINT. A rifleman leaves its position by five cells and returns. A
			// verdict-time reading sees drift 0 and reports a platoon that held; the peak sees 5.
			var report = RunAgainstHelpers(StubPrelude + @"
				local men = { actor(44, 16) }
				local t = TestHarness.DriftTracker(men)
				t.Sample()
				for x = 43, 39, -1 do
					moveTo(men[1], x, 16)
					t.Sample()
				end
				for x = 40, 44 do
					moveTo(men[1], x, 16)
					t.Sample()
				end
				return string.format('peak=%d final=%d trace=%s',
					t.Peak(), TestHarness.CellDrift(44, 16, men[1].Location.X, men[1].Location.Y), t.Trace())
			");

			Assert.That(report, Is.EqualTo("peak=5 final=0 trace=44->44(5)"),
				"DriftTracker must report the WORST drift seen over the run. A peak that decays back to "
				+ "the final position would let a front collapse and recover inside one window without "
				+ "the safe-front or under-danger scenario ever noticing.");
		}

		[Test]
		public void DriftCountsBothAxes()
		{
			// A platoon shoved sideways off its position has left it just as surely as one that walked
			// west, so the metric is Chebyshev over both axes rather than a distance along X.
			var report = RunAgainstHelpers(StubPrelude + @"
				local men = { actor(44, 16) }
				local t = TestHarness.DriftTracker(men)
				moveTo(men[1], 44, 19)
				t.Sample()
				local sideways = t.Peak()
				moveTo(men[1], 42, 18)
				t.Sample()
				return string.format('sideways=%d diagonal=%d cheb=%d',
					sideways, t.Peak(), TestHarness.CellDrift(0, 0, -3, 2))
			");

			// Sideways 3 cells reads 3; a later (2,2) offset does not beat it; CellDrift is max(|dx|,|dy|).
			Assert.That(report, Is.EqualTo("sideways=3 diagonal=3 cheb=3"),
				"drift must be max(|dx|, |dy|) from spawn, measured on both axes");
		}

		[Test]
		public void PeakIsTheWorstManNotAnAverage()
		{
			// One man walking out is a front with a hole in it. Averaging would let four men holding
			// hide the fifth one leaving, which is the failure mode the danger scenario measured at
			// 14499b0a (the whole platoon walked, but a single defector is the same doctrine break).
			var report = RunAgainstHelpers(StubPrelude + @"
				local men = { actor(44, 14), actor(44, 15), actor(44, 16) }
				local t = TestHarness.DriftTracker(men)
				moveTo(men[3], 37, 16)
				t.Sample()
				return string.format('peak=%d trace=%s', t.Peak(), t.Trace())
			");

			Assert.That(report, Is.EqualTo("peak=7 trace=44->44(0) 44->44(0) 44->37(7)"),
				"Peak must be the worst single actor, so one man leaving cannot be averaged away");
		}

		[Test]
		public void DeadActorKeepsItsPeakAndStopsContributing()
		{
			// TWO PROPERTIES, and the second is the one that needs the IsDead guard in Sample(). A man
			// shot while out of position must keep the excursion he made — death is not an alibi — and a
			// corpse must stop feeding the measurement at all.
			//
			// The corpse is moved to 0,0 rather than back to its spawn on purpose. A max fold alone
			// already preserves the peak of an actor that stops moving, so a test where the dead man
			// simply walks home passes with or without the guard and proves nothing about it. Location on
			// a disposed actor is not a value this test can rely on (IsDead is `Disposed || health.IsDead`,
			// Actor.cs:76), so the guard has to hold against a cell that reads as anything at all — and
			// 0,0 is what an unset one reads as.
			var report = RunAgainstHelpers(StubPrelude + @"
				local men = { actor(44, 16), actor(44, 17) }
				local t = TestHarness.DriftTracker(men)
				moveTo(men[1], 38, 16)
				t.Sample()
				men[1].IsDead = true
				moveTo(men[1], 0, 0)
				t.Sample()
				return string.format('peak=%d trace=%s', t.Peak(), t.Trace())
			");

			Assert.That(report, Is.EqualTo("peak=6 trace=dead 44->44(0)"),
				"a dead actor must keep the peak it reached while alive AND stop contributing new drift; "
				+ "reading a corpse's cell would invent an excursion nobody made and fail the position "
				+ "clause of both supply scenarios on a casualty");
		}

		[Test]
		public void SpawnIsCapturedAtConstructionNotAtFirstSample()
		{
			// The tracker copies X/Y out as numbers when it is built. If it instead held the cell object
			// and that object tracked the actor, every drift would read 0 and both scenarios' position
			// clauses would be silently vacuous — a test that passes for the wrong reason, which is the
			// exact defect PIPELINE item 51 exists to remove.
			var report = RunAgainstHelpers(StubPrelude + @"
				local men = { actor(44, 16) }
				local t = TestHarness.DriftTracker(men)
				moveTo(men[1], 40, 16)
				t.Sample()
				return string.format('peak=%d', t.Peak())
			");

			Assert.That(report, Is.EqualTo("peak=4"),
				"drift must be measured from the cell held at construction, even if the actor moved "
				+ "before the first Sample()");
		}

		// ---------------------------------------------------------------------------------------
		// The geometry behind HOLD_DRIFT = 1.
		// ---------------------------------------------------------------------------------------

		// The platoon column's x, documented in the scenario itself
		// (test-supply-safe-front-keeps-cargo.lua:72), and the x a truck sits at once it has closed to
		// the centre man's aura edge (:73).
		const int PlatoonColumnX = 44;
		const int TruckAuraEdgeX = 39;

		// Flat-grid cell centre in world units: 1c = 1024, and the centre of cell n sits at 1024n + 512.
		static WPos CellCentre(int x, int y)
		{
			return new WPos((1024 * x) + 512, (1024 * y) + 512, 0);
		}

		[Test]
		public void OneCellIsExactlyEnoughToReachATruckAtAuraRange()
		{
			// test-supply-safe-front-keeps-cargo allows a peak drift of 1 cell and justifies it from this
			// arithmetic, so the arithmetic is asserted rather than asserted-about. The platoon is a
			// column at x=44, y=14..18; the truck drives in along y=16 and the safe branch closes to
			// "just inside its own aura" (SupplyFollowerBotModule.cs:1468). TRUK's aura is Range: 5c0
			// (rules/ingame/vehicles.yaml:569), compared as horizontal distance SQUARED
			// (SupplyProvider.InAuraRange) — so the boundary is dx^2 + dy^2 <= 25 in cells, NOT Chebyshev
			// 5, and that difference is the whole reason a cell of slack is needed at all.
			var range = new WDist(5 * 1024);
			var truck = CellCentre(39, 16);

			// Held in place, the centre man is served and the flankers are NOT: 25 <= 25, but 26 and 29.
			Assert.That(SupplyProvider.InAuraRange(truck, CellCentre(44, 16), range), Is.True,
				"the centre man sits exactly on the 5c boundary and must be inside it");
			Assert.That(SupplyProvider.InAuraRange(truck, CellCentre(44, 15), range), Is.False,
				"dx=5,dy=1 is 26 cells squared — outside a 5c aura, so a flanker cannot be served in place");
			Assert.That(SupplyProvider.InAuraRange(truck, CellCentre(44, 14), range), Is.False,
				"dx=5,dy=2 is 29 cells squared — outside a 5c aura");

			// One cell west and every man in the column is inside. This is what the allowance buys, and
			// it is why the number is 1 rather than 0.
			for (var y = 14; y <= 18; y++)
				Assert.That(SupplyProvider.InAuraRange(truck, CellCentre(43, y), range), Is.True,
					$"one cell west must put the rifleman at (44,{y}) inside the truck's aura; if this "
					+ "fails, HOLD_DRIFT in test-supply-safe-front-keeps-cargo is no longer derivable "
					+ "from the map geometry and must be re-derived rather than nudged");

			// And it buys nothing more than that: two cells is not needed for any man in the column, so
			// an allowance of 2 would be slack with no doctrinal justification behind it.
			Assert.That(SupplyProvider.InAuraRange(truck, CellCentre(43, 14), range), Is.True,
				"the worst-placed flanker is already inside at one cell, so the allowance need not be 2");
		}

		[Test]
		public void SixCellsWouldLicenseAWalkOutToTheTruckItself()
		{
			// WHY THE SAFE SCENARIO DOES NOT INHERIT THE SIBLING'S 6-CELL ALLOWANCE. 6 is licensed by one
			// correct behaviour — walking to a crate dropped short — and the safe scenario's clause 2
			// fails any run in which a crate existed, so that behaviour can never legitimately occur
			// there. Unconditional 6 in a no-crate scenario is the configuration test-supply-under-danger
			// already measured as broken at 9861bcf4: five men walked out to meet the TRUCK and the run
			// passed at drift 5 with `crate=NONE placed`.
			//
			// Concretely: a truck loitering at x=39 is 5 cells from the platoon, so under a 6-cell
			// allowance the entire platoon can reach the truck and still be judged to have held.
			var range = new WDist(5 * 1024);
			var truck = CellCentre(39, 16);

			// Both numbers are READ FROM THE SCENARIOS, not restated here. Restating them was the
			// original defect: the assertion compared two constants declared in this file, so it
			// agreed with itself whatever the scenarios actually shipped, and the one edit it exists
			// to catch — nudging the safe scenario's allowance up to make a red run green — could not
			// fail it.
			var safeHoldDrift = ReadScenarioConstant("test-supply-safe-front-keeps-cargo", "HOLD_DRIFT");
			var siblingMaxDrift = ReadScenarioConstant("test-supply-under-danger", "MAX_DRIFT");

			Assert.That(safeHoldDrift, Is.EqualTo(1),
				"test-supply-safe-front-keeps-cargo now allows a peak drift of " + safeHoldDrift + " cells. "
				+ "One cell is the number OneCellIsExactlyEnoughToReachATruckAtAuraRange derives from the "
				+ "map geometry; anything larger is no longer derivable from it and has been nudged rather "
				+ "than re-derived. At 6 this scenario becomes the configuration measured broken at "
				+ "9861bcf4, where five men walked out to meet the truck and the run passed at drift 5 "
				+ "with `crate=NONE placed`");

			// A man who has spent the sibling's whole allowance is not merely loose — he is PAST the
			// truck's cell, so the platoon has crossed the thing it was supposed to wait for.
			Assert.That(PlatoonColumnX - siblingMaxDrift, Is.LessThanOrEqualTo(TruckAuraEdgeX),
				"the sibling's " + siblingMaxDrift + "-cell allowance no longer reaches the truck at x="
				+ TruckAuraEdgeX + " from the platoon column at x=" + PlatoonColumnX + ", so the reason "
				+ "this scenario refuses to inherit it has changed and the refusal must be re-argued");

			Assert.That(SupplyProvider.InAuraRange(truck, CellCentre(PlatoonColumnX - siblingMaxDrift, 16), range), Is.True,
				"a man who has spent the sibling's full allowance is inside the truck's aura and therefore "
				+ "fed — by clauses 1-3 indistinguishable from one who never left the front");

			// The men do not even have to reach the truck's cell — entering its aura is enough to be fed,
			// which is why the loose allowance is dangerous rather than merely imprecise.
			Assert.That(SupplyProvider.InAuraRange(truck, CellCentre(40, 16), range), Is.True,
				"a man who has walked 4 cells is deep inside the truck's aura — well within the sibling's "
				+ "allowance, and fed, and by clauses 1-3 indistinguishable from one who never moved");
		}
	}
}
