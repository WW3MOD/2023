#region Copyright & License Information
/*
 * WW3MOD grid-descent CALL-SITE pins — the phantom-anchor class, closed at the seam rather than one resolver
 * at a time.
 *
 * THE CLASS. A bot resolver seeds a steepest descent from a MAP cell, walks it over the coarse control grid,
 * and converts the result back to a map cell. MapToGrid floors; GridToMapCentre returns the block CENTRE; the
 * round trip is therefore NOT the identity (see InfluenceGridMath). A resolver that asks "did the descent
 * move?" in MAP space gets the wrong answer for 3 of 4 Supply Route placements, and a resolver that does not
 * ask at all publishes a phantom anchor for 4 of 4 — a cell derived from the SR by quantisation, which no
 * descent ever chose.
 *
 * THREE RESOLVERS SHIPPED THE SAME MISTAKE INDEPENDENTLY, and the lesson of the third is not "grid vs map":
 * ResolveMusterAnchor carried a correct guard AND a comment naming this exact hazard since d91e10f7
 * (2026-08-04), twenty lines from a sibling that stayed broken for eleven days, and CaptureCoordinator's
 * reserve muster went the whole time with no guard of any kind. Prose next to the code does not carry across;
 * a scan does. Hence this file: ForwardStagingMathTest pins the MATH, and the math was never what was wrong.
 *
 * MODELLED ON BotOrderGateCallerTest's source scan, which exists for the same reason — it is the only pin
 * that covers the REAL call sites, and its exact-count assertion is what stops the scope silently shrinking
 * until the scan polices nothing.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class GridDescentGuardTest
	{
		// The RAW descent call sites — those that drive the walk themselves and so must make the stall test
		// themselves. ForwardStagingMath.TryResolveAnchorCell is one of them (it owns the handoff for its
		// callers, and is pinned here like any other); the rest are resolvers that have not been folded onto
		// it. A resolver routed THROUGH TryResolveAnchorCell is guarded by construction and is deliberately
		// out of scope, so folding one in LOWERS this number.
		//
		// 4 since 2026-08-17: CaptureCoordinatorBotModule.ResolveReserveAnchor was the fourth raw site and had
		// no guard at all; it now goes through TryResolveAnchorCell and left the scan's scope.
		//
		// 5 since 2026-09-02: LogisticsCenterBotModule.ChooseSite. It is raw for the ONE reason that justifies
		// raw — TryResolveAnchorCell has no `passable` parameter, and this descent must have one. It sites a
		// specific LCCV, a real ground mover with a locomotor, so an unreachable result is not a missed scan
		// but a permanent stall: the walk is deterministic over a slowly-changing field, so it re-derives the
		// same rejected cell every scan for as long as the field holds still (the measured 24-consecutive-scan
		// outage in ForwardStagingMath's notes). Folding it onto TryResolveAnchorCell would mean dropping the
		// terrain filter, which is the defect, not the cleanup. It makes its stall test in grid space like
		// every other site here, which is what the scan above actually checks.
		//
		// If you add a raw descent, prefer routing it through TryResolveAnchorCell. If you genuinely need a new
		// raw one, update this number and say why in the commit message — do not just make the count fit.
		const int ExpectedRawDescentSites = 5;

		// `var (agx, agy) = <Something.>StagingCell(sgx, sgy, ...)` — the seed pair and the result pair in one
		// match, so the guard below can be checked against the ACTUAL variables rather than a naming habit.
		// Anchored on `var (` so the method DECLARATIONS of StagingCell/AdvanceCell are not matched.
		static readonly Regex DescentCall = new(
			@"var\s*\(\s*(\w+)\s*,\s*(\w+)\s*\)\s*=\s*(?:\w+\.)?(StagingCell|AdvanceCell)\(\s*(\w+)\s*,\s*(\w+)\s*,",
			RegexOptions.Compiled);

		static string FindBotModulesDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "OpenRA.Mods.Common", "Traits", "BotModules");
				if (Directory.Exists(candidate))
					return candidate;
			}

			return null;
		}

		static string StripComment(string line)
		{
			var idx = line.IndexOf("//", StringComparison.Ordinal);
			return idx < 0 ? line : line[..idx];
		}

		/// <summary>The two spellings of the grid-space stall test over these exact variables, whitespace
		/// removed. Nothing else counts: a map-space comparison is the shipped bug, and a comparison over
		/// different variables is not this descent's guard.</summary>
		static bool IsGuardFor(string line, string rx, string ry, string sx, string sy)
		{
			var squashed = Regex.Replace(StripComment(line), @"\s+", string.Empty);
			return squashed.Contains($"{rx}=={sx}&&{ry}=={sy}", StringComparison.Ordinal)
				|| squashed.Contains($"{sx}=={rx}&&{sy}=={ry}", StringComparison.Ordinal);
		}

		[Test]
		public void SourceScan_EveryRawGridDescentMakesItsStallTestInGridSpace()
		{
			var root = FindBotModulesDir();
			if (root == null)
				Assert.Ignore("source tree not reachable from the test assembly — scan skipped, not passed");

			var offenders = new List<string>();
			var sites = new List<string>();

			foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
			{
				var lines = File.ReadAllLines(file);
				for (var i = 0; i < lines.Length; i++)
				{
					var m = DescentCall.Match(lines[i]);
					if (!m.Success)
						continue;

					string rx = m.Groups[1].Value, ry = m.Groups[2].Value;
					string sx = m.Groups[4].Value, sy = m.Groups[5].Value;
					var site = $"{Path.GetFileName(file)}:{i + 1}";
					sites.Add(site);

					// Forward to the end of the ENCLOSING METHOD: the first unmatched close brace. Comments are
					// stripped before counting so a brace inside prose cannot end the search early — several of
					// these guards sit forty lines of explanation below their descent.
					var guarded = false;
					var depth = 0;
					for (var j = i + 1; j < lines.Length; j++)
					{
						if (IsGuardFor(lines[j], rx, ry, sx, sy))
						{
							guarded = true;
							break;
						}

						var code = StripComment(lines[j]);
						foreach (var ch in code)
						{
							if (ch == '{')
								depth++;
							else if (ch == '}')
								depth--;
						}

						if (depth < 0)
							break;
					}

					if (!guarded)
						offenders.Add(
							$"{site}: descends ({sx},{sy}) -> ({rx},{ry}) and never tests `{rx} == {sx} && {ry} == {sy}`");
				}
			}

			Assert.Multiple(() =>
			{
				Assert.That(offenders, Is.Empty,
					"A grid descent that stalls at its seed has found NO gradient, and its result is the seed "
					+ "re-projected through a lossy round trip — a phantom cell one block-centre off the Supply "
					+ "Route, not a destination. Every raw descent must make that test in GRID space, over its "
					+ "own seed and result variables, BEFORE converting to a map cell. Testing it after the "
					+ "conversion inverts it for 3 of 4 placements; omitting it publishes the phantom for 4 of "
					+ "4. See InfluenceGridMath, and prefer ForwardStagingMath.TryResolveAnchorCell.\n  "
					+ string.Join("\n  ", offenders));

				Assert.That(sites, Has.Count.EqualTo(ExpectedRawDescentSites),
					$"expected exactly {ExpectedRawDescentSites} raw grid-descent call sites, found "
					+ $"{sites.Count}: {string.Join(", ", sites)}. This count is a contract, not a smoke test — "
					+ "a scan that silently loses sight of a site polices nothing, which is how this class "
					+ "survived three separate fixes.");
			});
		}

		// ---------- The reserve muster, at the numbers it actually ships with ----------

		// Flat/unpopulated field: every cell reads the same 'far' sentinel, so no neighbour ever improves and
		// the descent cannot leave its seed. This is the pre-contact opening, and it is where the reserve
		// muster spent the whole of the seed-1017 run.
		static int FlatFar(int gx, int gy) => 64;
		static int NoDanger(int gx, int gy) => 0;
		static bool BigGrid(int gx, int gy) => gx >= 0 && gx < 128 && gy >= 0 && gy < 128;

		// CaptureCoordinatorBotModule as shipped on BOTH profiles (ai.yaml:277 @experimental.tecn,
		// ai.yaml:2103 @stable.tecn set ReserveStandoffCells: 10; the rest are the engine defaults at
		// CaptureCoordinatorBotModule.cs:350-372). ControlField ships CellSize 2.
		const int ReserveCellSize = 2;
		const int ReserveStandoffCells = 10;
		const int ReserveDangerSafeUnits = 0;
		const int ReserveMaxDescentSteps = 64;

		[Test]
		public void ReserveAnchor_FlatField_PublishesNoAnchorAtEveryParity()
		{
			// THE ACCEPTANCE BAR. Four Supply Route placements covering all four coordinate parities; on a flat
			// field every one of them must publish NO anchor, so the undispatched technicians are left alone
			// rather than fanned into a ring around a cell that means nothing.
			//
			// ON ITS OWN THIS PROVES NOTHING ABOUT THE RESERVE MUSTER, and saying so is the point: it passed
			// unchanged against the broken resolver, because the seam it exercises was already correct and the
			// resolver simply did not use it. What binds this to shipped behaviour is the source scan above,
			// which is what actually went red. Do not read a green here as "the reserve muster is guarded".
			var parities = new[] { (6, 16), (7, 16), (6, 17), (7, 17) };

			Assert.Multiple(() =>
			{
				foreach (var (srX, srY) in parities)
				{
					var published = ForwardStagingMath.TryResolveAnchorCell(
						ReserveCellSize, srX, srY,
						ReserveStandoffCells, ReserveDangerSafeUnits, ReserveMaxDescentSteps,
						FlatFar, NoDanger, BigGrid,
						out var ax, out var ay);

					Assert.That(published, Is.False,
						$"SR ({srX},{srY}): a flat field gives the reserve descent no gradient, so there is no "
						+ $"honest muster cell — but it published ({ax},{ay})");
				}
			});
		}

		[Test]
		public void ReserveAnchor_TheShippedPhantomIsExactlyTheLossyRoundTrip()
		{
			// The incident, pinned to the digit and built from the PRODUCTION conversion pair rather than from
			// arithmetic retyped here. Seed 1017 on 2026-08-17 logged, at ticks 504 and 654:
			//   [exp-capture] reserve unit=tecn.america#23 from=6,16 to=1,23 anchor=7,17
			// The Supply Route was at (6,16). (7,17) is not a staging cell — it is (6,16) pushed through
			// MapToGrid and back out of GridToMapCentre, and the unguarded resolver published it every eval.
			var phantomX = InfluenceGridMath.GridToMapCentre(
				ReserveCellSize, InfluenceGridMath.MapToGrid(ReserveCellSize, 6));
			var phantomY = InfluenceGridMath.GridToMapCentre(
				ReserveCellSize, InfluenceGridMath.MapToGrid(ReserveCellSize, 16));

			Assert.Multiple(() =>
			{
				Assert.That((phantomX, phantomY), Is.EqualTo((7, 17)),
					"the round trip of the logged SR must reproduce the logged anchor — if this ever stops "
					+ "holding, the incident record below has drifted from the conversion it describes");

				var published = ForwardStagingMath.TryResolveAnchorCell(
					ReserveCellSize, 6, 16,
					ReserveStandoffCells, ReserveDangerSafeUnits, ReserveMaxDescentSteps,
					FlatFar, NoDanger, BigGrid,
					out var ax, out var ay);

				Assert.That(published, Is.False,
					$"the ({phantomX},{phantomY}) quantisation artifact must never be published as a reserve "
					+ $"anchor (got {ax},{ay})");
			});
		}

		[Test]
		public void ReserveAnchor_PopulatedField_StillMustersForward()
		{
			// The fix must not make the reserve muster inert: a real gradient still resolves, and forward of
			// the SR. Front at x=0, SR at map x=40 (grid 20), standoff 10 => grid x=10 => map centre 21.
			var published = ForwardStagingMath.TryResolveAnchorCell(
				ReserveCellSize, 40, 0,
				ReserveStandoffCells, ReserveDangerSafeUnits, ReserveMaxDescentSteps,
				(gx, gy) => gx, NoDanger, BigGrid,
				out var ax, out var ay);

			Assert.Multiple(() =>
			{
				Assert.That(published, Is.True, "a populated field must still muster the reserve");
				Assert.That(ax, Is.EqualTo(21), "descends to the standoff and converts to that block's centre");
				Assert.That(ax, Is.LessThan(40), "the muster must be FORWARD of the Supply Route, never behind it");
			});
		}
	}
}
