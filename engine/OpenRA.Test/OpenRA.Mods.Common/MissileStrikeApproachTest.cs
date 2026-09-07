#region Copyright & License Information
/*
 * WW3MOD missile-strike APPROACH GEOMETRY — the shared azimuth a support-power salvo flies in on.
 * Pure integer arithmetic against the real MissileStrikeApproach. No World, no Actor, no game run.
 *
 * The thing being pinned is not "the missiles come from off-map". It is that ONE bearing is taken
 * from the salvo's CENTROID and reused by every warhead. Take it per warhead instead — which is
 * exactly what spawning each at the owner's nearest map-edge cell and facing it at its own aim
 * point amounted to — and a six-warhead MIRV leaves one point and FANS OUT to six, which is the
 * inverse of what re-entry vehicles from a single launch do. TheOldGeometryReallyDidFan below is
 * the witness for that, and it is the test that would still pass if someone quietly moved the
 * bearing back inside the per-warhead loop, so read it alongside the parallelism ones.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class MissileStrikeApproachTest
	{
		// MissileStrikePowerInfo.ApproachMargin's default. Not read from YAML: no shipped power
		// overrides it, and a test that quietly followed an override would stop pinning the default.
		const int Margin = 16 * 1024;

		// x-lake-ww3, the largest shipped map. TheShippedMapSizesAreStillWhatThisFileAssertsAgainst
		// keeps this honest against mods/ww3mod/maps.
		const int LargestMapCells = 130;

		static WPos Cell(int x, int y)
		{
			// Map.CenterOfCell on the Rectangular grid this mod uses.
			return new WPos((1024 * x) + 512, (1024 * y) + 512, 0);
		}

		/// <summary>
		/// A six-click Sarmat salvo: AimPoints 6, spread wider than the 30c0 AimPointRadius ring the
		/// placement overlay draws, which is how a player who read the overlay would place them.
		/// Deliberately NOT symmetric about anything — a symmetric salvo can hide a centroid bug.
		/// </summary>
		static WPos[] SixClickSalvo()
		{
			return new[]
			{
				Cell(88, 24), Cell(96, 41), Cell(74, 52),
				Cell(103, 63), Cell(85, 77), Cell(99, 90),
			};
		}

		static readonly WPos Home = Cell(14, 112);

		static WPos MapCenter(int cells)
		{
			return Cell(cells / 2, cells / 2);
		}

		static MissileStrikeApproach ApproachFor(WPos[] aimPoints, int cells = LargestMapCells)
		{
			return MissileStrikeApproach.For(Home, MapCenter(cells), cells, cells, Margin, aimPoints);
		}

		// --- (1) one salvo, one azimuth --------------------------------------------------------

		[Test]
		public void EveryWarheadInASalvoFliesTheIdenticalVector()
		{
			var salvo = SixClickSalvo();
			var approach = ApproachFor(salvo);

			// The STRONGEST form of "parallel": not merely equal yaws, which two vectors of different
			// length can share while arriving on different schedules, but the same WVec component for
			// component. That is what makes the tracks parallel AND the flight times equal.
			var reference = salvo[0] - approach.SpawnPosition(salvo[0]);

			foreach (var aim in salvo)
			{
				var track = aim - approach.SpawnPosition(aim);
				Assert.That(track, Is.EqualTo(reference),
					"every re-entry vehicle of one launch flies the same track vector; a per-warhead " +
					"bearing is the fan this replaced");
			}
		}

		[Test]
		public void TheBearingIsTakenFromTheCentroidNotFromAnyOneAimPoint()
		{
			var salvo = SixClickSalvo();
			var approach = ApproachFor(salvo);

			var centroidBearing = (MissileStrikeApproach.Centroid(salvo) - Home).Yaw;
			Assert.That(approach.Facing, Is.EqualTo(centroidBearing));

			// And the centroid bearing is genuinely NOT any individual aim point's bearing, so the
			// assertion above is distinguishing something rather than agreeing with everything.
			var distinct = salvo.Select(a => (a - Home).Yaw).Distinct().ToArray();
			Assert.That(distinct.Length, Is.GreaterThan(1),
				"the fixture salvo must span several per-warhead bearings or it cannot tell a " +
				"centroid bearing from a per-warhead one");
			Assert.That(distinct, Does.Not.Contain(centroidBearing),
				"and the centroid's bearing must differ from all of them, so a regression to " +
				"'bearing of aim point i' cannot pass by coincidence");
		}

		[Test]
		public void TheAzimuthDoesNotDependOnTheOrderTheAimPointsArrivedIn()
		{
			var salvo = SixClickSalvo();
			var reversed = salvo.Reverse().ToArray();
			var rotated = salvo.Skip(2).Concat(salvo.Take(2)).ToArray();

			// The centroid is a long-accumulated sum with ONE integer division at the end, so it is
			// order-independent exactly rather than approximately. If someone rewrites it as a
			// running mean this test is what fails.
			Assert.That(ApproachFor(reversed).Facing, Is.EqualTo(ApproachFor(salvo).Facing));
			Assert.That(ApproachFor(rotated).Facing, Is.EqualTo(ApproachFor(salvo).Facing));
		}

		[Test]
		public void LateralSpacingIsExactlyTheSpacingThePlayerClicked()
		{
			var salvo = SixClickSalvo();
			var approach = ApproachFor(salvo);

			for (var i = 0; i < salvo.Length; i++)
			{
				for (var j = 0; j < salvo.Length; j++)
				{
					var aimSeparation = salvo[j] - salvo[i];
					var bornSeparation = approach.SpawnPosition(salvo[j]) - approach.SpawnPosition(salvo[i]);

					Assert.That(bornSeparation, Is.EqualTo(aimSeparation),
						"the warheads are born in the same formation they land in — that is what " +
						"'parallel' buys, and it is why the player's own aim-point spacing survives");
				}
			}
		}

		[Test]
		public void EveryWarheadFliesTheSameHorizontalDistanceAndThereforeTheSameFlightTime()
		{
			var salvo = SixClickSalvo();
			var approach = ApproachFor(salvo);

			var lengths = salvo
				.Select(a => (a - approach.SpawnPosition(a)).HorizontalLength)
				.Distinct()
				.ToArray();

			Assert.That(lengths.Length, Is.EqualTo(1),
				"BallisticMissileFly.EstimateArcTicks is a function of the horizontal distance alone " +
				"(Acceleration is 0 on every shipped strike missile, so it is hDist / Speed), so one " +
				"distance is one flight time");

			// And that distance is the standoff, up to WVec.Rotate's integer truncation.
			Assert.That(Math.Abs(lengths[0] - approach.Standoff), Is.LessThanOrEqualTo(2),
				"the flight length must be the standoff, or the beacon and camera timings derived " +
				"from hDist are measuring something else");
		}

		// --- (2) the fan this replaced ---------------------------------------------------------

		[Test]
		public void TheOldGeometryReallyDidFan()
		{
			// Pre-2026-09-07 arithmetic, reproduced: ONE spawn point shared by the whole salvo (the
			// map-edge cell nearest the owner's HomeLocation) and a bearing computed per warhead,
			// from that shared point to that warhead's own aim point.
			var salvo = SixClickSalvo();
			var sharedSpawn = Cell(0, 112);

			var oldBearings = salvo.Select(a => (a - sharedSpawn).Yaw.Angle).ToArray();
			var spread = oldBearings.Max() - oldBearings.Min();

			Assert.That(spread, Is.GreaterThan(64),
				"this fixture must reproduce a WIDE fan (64 raw WAngle units is 22.5 degrees) or it " +
				"is not a witness for the bug that was fixed");

			var newBearings = salvo.Select(_ => ApproachFor(salvo).Facing.Angle).Distinct().ToArray();
			Assert.That(newBearings.Length, Is.EqualTo(1),
				"and the replacement must collapse that fan to a single azimuth");
		}

		// --- (3) player-situated, and pointing the right way -----------------------------------

		[Test]
		public void TheSalvoApproachesFromBehindTheLauncherRatherThanFromInFrontOfIt()
		{
			// THE SIGN TEST, and the reason it is written as a dot product rather than as an angle:
			// WAngle is COUNTERCLOCKWISE and north is -Y, and getting that inverted is a mistake this
			// codebase has made twice. An inverted approach would put the spawn between the player
			// and the target — the missile would fly AWAY from the enemy — and every parallelism
			// assertion above would still pass, because a fan turned inside out is still not a fan.
			foreach (var home in new[] { Cell(14, 112), Cell(115, 12), Cell(64, 4), Cell(4, 64) })
			{
				var salvo = SixClickSalvo();
				var approach = MissileStrikeApproach.For(home, MapCenter(LargestMapCells),
					LargestMapCells, LargestMapCells, Margin, salvo);

				var centroid = MissileStrikeApproach.Centroid(salvo);
				var intent = centroid - home;

				foreach (var aim in salvo)
				{
					var track = aim - approach.SpawnPosition(aim);
					var alignment = ((long)track.X * intent.X) + ((long)track.Y * intent.Y);

					Assert.That(alignment, Is.GreaterThan(0),
						$"the warhead must travel the way the launcher is pointing (home {home})");
					Assert.That((approach.SpawnPosition(aim) - centroid).HorizontalLengthSquared,
						Is.GreaterThan((home - centroid).HorizontalLengthSquared),
						"and be born further out than the launcher, not between it and the target");
				}
			}
		}

		[Test]
		public void AimingTheWholeSalvoAtYourOwnHomeCellStillProducesADirection()
		{
			// Degenerate but reachable: a player nuking their own Supply Route. There is no
			// home-to-centroid bearing to read, and the fallback must still mean "in over my own
			// back line" rather than throwing or silently picking due north.
			var onlyPoint = new[] { Home };
			var approach = ApproachFor(onlyPoint);

			var inward = MapCenter(LargestMapCells) - Home;
			Assert.That(approach.Facing, Is.EqualTo(inward.Yaw));

			var spawn = approach.SpawnPosition(Home);
			var toCenter = MapCenter(LargestMapCells) - spawn;
			Assert.That(((long)toCenter.X * inward.X) + ((long)toCenter.Y * inward.Y), Is.GreaterThan(0),
				"the fallback must come from outside the player's own side, inward");
		}

		[Test]
		public void AHomeAtTheExactMapCentreAimedAtItselfDoesNotThrow()
		{
			var centre = MapCenter(LargestMapCells);
			var approach = MissileStrikeApproach.For(centre, centre, LargestMapCells, LargestMapCells,
				Margin, new[] { centre });

			Assert.That(approach.Facing, Is.EqualTo(WAngle.Zero));
			Assert.That(approach.Standoff, Is.GreaterThan(0));
		}

		// --- (4) determinism ------------------------------------------------------------------

		[Test]
		public void IdenticalInputsProduceByteIdenticalPositions()
		{
			// The spawn is [Sync]-relevant simulation state on the synced order-resolution path: the
			// missile is a real damageable actor, not an effect. Nothing here may be float or RNG.
			var salvo = SixClickSalvo();
			var first = ApproachFor(salvo);
			var expected = salvo.Select(a => first.SpawnPosition(a)).ToArray();

			for (var run = 0; run < 64; run++)
			{
				var again = ApproachFor(SixClickSalvo());
				Assert.That(again.Facing, Is.EqualTo(first.Facing));
				Assert.That(again.Standoff, Is.EqualTo(first.Standoff));

				for (var i = 0; i < salvo.Length; i++)
					Assert.That(again.SpawnPosition(salvo[i]), Is.EqualTo(expected[i]));
			}
		}

		// --- (5) the bound -------------------------------------------------------------------

		static IEnumerable<(int X, int Y)> ShippedMapSizes()
		{
			// The DISTINCT sizes under mods/ww3mod/maps, as of main @ 89a1f31f — polar-disorder and
			// woodland-warfare are both 98x98, and this is a set of sizes to sweep rather than a
			// roster of maps. Pinned by the test below.
			yield return (66, 34);
			yield return (102, 72);
			yield return (98, 98);
			yield return (98, 82);
			yield return (123, 114);
			yield return (92, 62);
			yield return (97, 67);
			yield return (128, 128);
			yield return (130, 130);
		}

		static IEnumerable<WPos> AimPointSweep(int cellsX, int cellsY)
		{
			// Corners and edges included deliberately: the corner opposite the approach is the ONE
			// aim point for which the walk-back is at its longest, and it is the case a standoff
			// tuned by eye gets wrong.
			for (var x = 0; x < cellsX; x += Math.Max(1, cellsX / 8))
				for (var y = 0; y < cellsY; y += Math.Max(1, cellsY / 8))
					yield return Cell(x, y);

			yield return Cell(0, 0);
			yield return Cell(cellsX - 1, 0);
			yield return Cell(0, cellsY - 1);
			yield return Cell(cellsX - 1, cellsY - 1);
		}

		[Test]
		public void NoSpawnEverLandsInsideTheMapOnAnyShippedMapSize()
		{
			foreach (var (cellsX, cellsY) in ShippedMapSizes())
			{
				var mapCenter = Cell(cellsX / 2, cellsY / 2);
				var right = 1024 * cellsX;
				var bottom = 1024 * cellsY;

				foreach (var home in new[] { Cell(1, 1), Cell(cellsX - 2, 1), Cell(1, cellsY - 2), Cell(cellsX - 2, cellsY - 2), mapCenter })
				{
					foreach (var aim in AimPointSweep(cellsX, cellsY))
					{
						var approach = MissileStrikeApproach.For(home, mapCenter, cellsX, cellsY, Margin, new[] { aim });
						var spawn = approach.SpawnPosition(aim);

						// Distance from the spawn to the map rectangle, which is zero if it is inside.
						var dx = Math.Max(Math.Max(0 - spawn.X, spawn.X - right), 0);
						var dy = Math.Max(Math.Max(0 - spawn.Y, spawn.Y - bottom), 0);
						var outside = new WVec(dx, dy, 0).HorizontalLength;

						Assert.That(outside, Is.GreaterThan(0),
							$"a {cellsX}x{cellsY} map, home {home}, aim {aim}: the warhead was born " +
							"INSIDE the map, which is the pop-into-existence this change removes");

						// The diagonal is the largest separation of any two in-map points, so the
						// walk-back clears the boundary by at least the margin, minus rounding.
						Assert.That(outside, Is.GreaterThanOrEqualTo(Margin - 2048),
							$"a {cellsX}x{cellsY} map, home {home}, aim {aim}: the margin proof failed");
					}
				}
			}
		}

		[Test]
		public void NoSpawnCellEverWrapsTheTwelveBitCPos()
		{
			// CPos packs X and Y into 12 SIGNED bits and wraps outside -2048..2047 in its
			// constructor — silently, with no exception. The missile really does convert: its
			// IOccupySpace.TopLeft is Map.CellContaining(CenterPosition), so a spawn far enough out
			// decodes as a cell on the far side of the world.
			foreach (var (cellsX, cellsY) in ShippedMapSizes())
			{
				var mapCenter = Cell(cellsX / 2, cellsY / 2);
				foreach (var home in new[] { Cell(1, 1), Cell(cellsX - 2, cellsY - 2) })
				{
					foreach (var aim in AimPointSweep(cellsX, cellsY))
					{
						var spawn = MissileStrikeApproach
							.For(home, mapCenter, cellsX, cellsY, Margin, new[] { aim })
							.SpawnPosition(aim);

						// Map.CellContaining, Rectangular grid.
						var cx = spawn.X / 1024;
						var cy = spawn.Y / 1024;

						Assert.That(cx, Is.InRange(-2048, 2047));
						Assert.That(cy, Is.InRange(-2048, 2047));
						Assert.That(new CPos(cx, cy), Is.EqualTo(new CPos(cx, cy)));

						// Round-tripping through the packed representation must be lossless, which is
						// the actual thing the range above is protecting.
						var packed = new CPos(cx, cy);
						Assert.That(packed.X, Is.EqualTo(cx), $"CPos wrapped X at {cx}");
						Assert.That(packed.Y, Is.EqualTo(cy), $"CPos wrapped Y at {cy}");
					}
				}
			}
		}

		[Test]
		public void TheStandoffStaysWellClearOfBothCeilingsItHasToRespect()
		{
			var largest = MissileStrikeApproach.StandoffFor(LargestMapCells, LargestMapCells, Margin);

			Assert.That(largest, Is.LessThan(MissileStrikeApproach.MaxStandoff / 4),
				"the shipped maps must sit far under the cap, or the cap is silently doing tuning");

			// WVec.Yaw runs through WAngle.ArcTan, which evaluates `1024 * ay` in int arithmetic and
			// returns a plausible WRONG angle above 2^31/1024 = 2,097,151 (conventions.md). Every
			// vector this geometry builds is bounded by the standoff plus the map extent.
			var worstComponent = MissileStrikeApproach.MaxStandoff + (1024 * LargestMapCells);
			Assert.That(worstComponent, Is.LessThan(2_097_151),
				"even at the cap, no component may reach the ArcTan overflow ceiling");
		}

		[Test]
		public void AnAbsurdMarginIsClampedRatherThanAllowedToWrapTheWorld()
		{
			var clamped = MissileStrikeApproach.StandoffFor(LargestMapCells, LargestMapCells, int.MaxValue / 2);
			Assert.That(clamped, Is.EqualTo(MissileStrikeApproach.MaxStandoff));

			var floored = MissileStrikeApproach.StandoffFor(1, 1, -1_000_000);
			Assert.That(floored, Is.EqualTo(MissileStrikeApproach.MinStandoff),
				"BallisticMissileFly divides by the horizontal distance and completes on its first " +
				"tick at zero, so a zero standoff would teleport the warhead onto its aim point");
		}

		// --- (6) keep the fixture honest -------------------------------------------------------

		[Test]
		public void TheShippedMapSizesAreStillWhatThisFileAssertsAgainst()
		{
			var maps = FindModMaps();
			var actual = new List<(int X, int Y)>();

			foreach (var dir in Directory.EnumerateDirectories(maps))
			{
				var yaml = Path.Combine(dir, "map.yaml");
				if (!File.Exists(yaml))
					continue;

				var line = File.ReadAllLines(yaml)
					.FirstOrDefault(l => l.StartsWith("MapSize:", StringComparison.Ordinal));
				if (line == null)
					continue;

				var parts = line.Split(new[] { ':' }, 2)[1].Split(',');
				actual.Add((int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim())));
			}

			Assert.That(actual.Count, Is.GreaterThanOrEqualTo(10),
				"the mod ships ten maps; finding fewer means this test read the wrong directory and " +
				"is proving nothing");

			// Sets, not counts: two shipped maps are the same size, and what the off-map proof needs
			// is that every size it will meet is one it sweeps.
			foreach (var size in actual)
				Assert.That(ShippedMapSizes(), Does.Contain(size),
					$"unpinned shipped map size {size}; add it to ShippedMapSizes so the off-map " +
					"proof covers it");

			foreach (var size in ShippedMapSizes())
				Assert.That(actual, Does.Contain(size),
					$"ShippedMapSizes still sweeps {size}, which no shipped map is any more");

			var biggest = actual.Max(s => Math.Max(s.X, s.Y));
			Assert.That(biggest, Is.EqualTo(LargestMapCells),
				"LargestMapCells is stale against mods/ww3mod/maps");
		}

		static string FindModMaps()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "maps");
				if (Directory.Exists(candidate))
					return candidate;

				dir = dir.Parent;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod/maps");
		}
	}
}
