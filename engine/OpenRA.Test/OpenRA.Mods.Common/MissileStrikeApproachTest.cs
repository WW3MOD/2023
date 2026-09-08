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

		// --- (3b) the corridor invariant, swept ------------------------------------------------

		/// <summary>
		/// Homes worth sweeping: every corner, every edge midpoint, and two interior points. Edge
		/// midpoints are the ones that matter and the ones a corner-only fixture misses -- a home
		/// halfway up the west edge has in-map aim points a full 90 degrees off its own corridor
		/// (anything due north or due south of it along that same edge), where a corner home's most
		/// extreme in-map aim point is only 45 degrees off.
		/// </summary>
		static IEnumerable<WPos> HomeSweep(int cellsX, int cellsY)
		{
			var lastX = cellsX - 2;
			var lastY = cellsY - 2;
			var midX = cellsX / 2;
			var midY = cellsY / 2;

			yield return Cell(1, 1);
			yield return Cell(lastX, 1);
			yield return Cell(1, lastY);
			yield return Cell(lastX, lastY);
			yield return Cell(midX, 1);
			yield return Cell(midX, lastY);
			yield return Cell(1, midY);
			yield return Cell(lastX, midY);
			yield return Cell(cellsX / 4, cellsY / 4);
			yield return Cell(3 * cellsX / 4, 3 * cellsY / 4);
		}

		/// <summary>
		/// The aim-point sweep of <see cref="AimPointSweep"/> plus the cells that are the whole
		/// point of this section: the launcher's own cell, its immediate neighbours, and a ring
		/// pushed OUTWARD from it -- away from the map centre, which is the family the old
		/// home-to-centroid bearing inverted and which no earlier test placed an aim point in.
		/// </summary>
		static IEnumerable<WPos> AimPointSweepIncludingOnAndBehindHome(int cellsX, int cellsY, WPos home)
		{
			foreach (var aim in AimPointSweep(cellsX, cellsY))
				yield return aim;

			var homeCellX = home.X / 1024;
			var homeCellY = home.Y / 1024;
			var outX = Math.Sign(homeCellX - (cellsX / 2));
			var outY = Math.Sign(homeCellY - (cellsY / 2));

			foreach (var d in new[] { 0, 1, 2, 4, 8, 16 })
			{
				// Straight out behind the launcher, and out along each axis separately, all clamped
				// back into the map the way MissileStrikePower.ResolveAimPoints clamps a real order.
				foreach (var (dx, dy) in new[] { (outX * d, outY * d), (outX * d, 0), (0, outY * d), (d, 0), (-d, 0), (0, d), (0, -d) })
				{
					var cx = Math.Clamp(homeCellX + dx, 0, cellsX - 1);
					var cy = Math.Clamp(homeCellY + dy, 0, cellsY - 1);
					yield return Cell(cx, cy);
				}
			}
		}

		[Test]
		public void EverySalvoIsBornBehindItsOwnLauncherWhereverThePlayerAimed()
		{
			// THE INVARIANT THE PLAYER STATED, and it is deliberately a sweep rather than a case:
			// "it should ALWAYS start from somewhere behind my own SR". Formally, the spawn must sit
			// further out along the launcher's own outward axis (home minus map centre) than the
			// launcher does. The bug this pins was invisible to every existing test in this file
			// because all of them aim INWARD of the home, which is the half of the map where reading
			// the bearing off home-to-centroid happens to agree with the corridor.
			var checkedPairs = 0;
			var clampEngaged = 0;

			foreach (var (cellsX, cellsY) in ShippedMapSizes())
			{
				var mapCenter = Cell(cellsX / 2, cellsY / 2);

				foreach (var home in HomeSweep(cellsX, cellsY))
				{
					var outward = home - mapCenter;
					Assert.That(outward.HorizontalLengthSquared, Is.Not.Zero,
						"HomeSweep must not place a launcher on the map centre; the corridor is " +
						"undefined there and the invariant below cannot be stated");

					foreach (var aim in AimPointSweepIncludingOnAndBehindHome(cellsX, cellsY, home))
					{
						var approach = MissileStrikeApproach.For(home, mapCenter, cellsX, cellsY, Margin, new[] { aim });
						var spawn = approach.SpawnPosition(aim);

						var fromHome = spawn - home;
						var outwardOffset = ((long)fromHome.X * outward.X) + ((long)fromHome.Y * outward.Y);

						Assert.That(outwardOffset, Is.GreaterThan(0),
							$"a {cellsX}x{cellsY} map, home {home}, aim {aim}: the salvo was born on " +
							"the map-interior side of its own launcher, so it flies in across the " +
							"board -- the 'comes in from the other side' bug");

						if (MissileStrikeApproach.Bearing(home, aim, mapCenter) != (aim - home).Yaw)
							clampEngaged++;

						checkedPairs++;
					}
				}
			}

			// The sweep has to actually exercise the clamp, or it is asserting an invariant that the
			// unclamped arithmetic would satisfy anyway and is proving nothing about the fix.
			Assert.That(clampEngaged, Is.GreaterThan(0),
				"no aim point in the sweep ever left the corridor, so this test would pass against " +
				"the bug it exists to pin");
			Assert.That(checkedPairs, Is.GreaterThan(1000));
		}

		[Test]
		public void TheOldBearingReallyDidFlyTheSalvoAcrossTheWholeBoard()
		{
			// The WITNESS, and the counterpart to TheOldGeometryReallyDidFan: the pre-2026-09-08
			// arithmetic, reproduced, on the aim point a player takes when the fighting reaches
			// their own Supply Route. Without this the test above could be pinning a property that
			// was never violated.
			const int Cells = LargestMapCells;
			var mapCenter = MapCenter(Cells);
			var home = Cell(14, 112);

			// Ten cells OUTWARD of the launcher -- deeper into its own corner, away from the centre.
			var aim = Cell(8, 120);

			var standoff = MissileStrikeApproach.StandoffFor(Cells, Cells, Margin);
			var oldFacing = (aim - home).Yaw;
			var oldSpawn = new WPos(
				aim.X - new WVec(0, -standoff, 0).Rotate(WRot.FromYaw(oldFacing)).X,
				aim.Y - new WVec(0, -standoff, 0).Rotate(WRot.FromYaw(oldFacing)).Y,
				0);

			var outward = home - mapCenter;
			var oldOutwardOffset = ((long)(oldSpawn - home).X * outward.X) + ((long)(oldSpawn - home).Y * outward.Y);
			Assert.That(oldOutwardOffset, Is.LessThan(0),
				"this fixture must reproduce the inversion -- the old spawn on the INTERIOR side of " +
				"the launcher -- or it is not a witness for the reported bug");

			// And it really was a cross-map traverse rather than a near miss: the old spawn sits on
			// the far side of the map from the launcher in both axes.
			Assert.That(oldSpawn.X / 1024, Is.GreaterThan(Cells / 2));
			Assert.That(oldSpawn.Y / 1024, Is.LessThan(Cells / 2));

			var fixedSpawn = MissileStrikeApproach
				.For(home, mapCenter, Cells, Cells, Margin, new[] { aim })
				.SpawnPosition(aim);

			var fixedOutwardOffset = ((long)(fixedSpawn - home).X * outward.X) + ((long)(fixedSpawn - home).Y * outward.Y);
			Assert.That(fixedOutwardOffset, Is.GreaterThan(0),
				"and the replacement must put it back behind the launcher");
		}

		[Test]
		public void AnOrdinaryStrikeOutInFrontOfTheLauncherIsBitIdenticalToWhatItFlewBefore()
		{
			// THE NO-OP HALF OF THE FIX, and the reason this is a clamp rather than a rewrite of the
			// bearing. Everything inside MaxCorridorDeviation must come out as the raw aim bearing,
			// unrounded and unshifted -- otherwise every strike in the mod moved, and a change that
			// was meant to touch one family of aim points has quietly touched all of them.
			var untouched = 0;

			foreach (var (cellsX, cellsY) in ShippedMapSizes())
			{
				var mapCenter = Cell(cellsX / 2, cellsY / 2);

				foreach (var home in new[] { Cell(1, 1), Cell(cellsX - 2, 1), Cell(1, cellsY - 2), Cell(cellsX - 2, cellsY - 2) })
				{
					foreach (var aim in AimPointSweep(cellsX, cellsY))
					{
						// A salvo aimed at the launcher's own cell has NO aim bearing: WVec.Yaw
						// answers WAngle.Zero for a zero vector, which is a real direction rather
						// than a refusal, so comparing against it here would be comparing against
						// noise. That case is the corridor fallback and is pinned by
						// AimingTheWholeSalvoAtYourOwnHomeCellStillProducesADirection.
						if ((aim - home).HorizontalLengthSquared == 0)
							continue;

						var raw = (aim - home).Yaw;
						if (WAngle.AngleDiff(raw, (mapCenter - home).Yaw).Angle > MissileStrikeApproach.MaxCorridorDeviation.Angle)
							continue;

						Assert.That(MissileStrikeApproach.Bearing(home, aim, mapCenter), Is.EqualTo(raw),
							$"a {cellsX}x{cellsY} map, home {home}, aim {aim}: an in-corridor strike " +
							"must fly exactly the bearing it flew before this clamp existed");

						untouched++;
					}
				}
			}

			Assert.That(untouched, Is.GreaterThan(1000),
				"the great majority of aim points must be inside the corridor, or 60 degrees is the " +
				"wrong limit and this is not a clamp but a redirection");
		}

		/// <summary>
		/// Signed lean of a bearing away from the corridor, in raw WAngle units: positive one way
		/// round, negative the other, in (-512, 512].
		/// </summary>
		static int Lean(WAngle bearing, WAngle corridorYaw)
		{
			var d = (bearing - corridorYaw).Angle;
			return d > 512 ? d - 1024 : d;
		}

		[Test]
		public void ALeanedApproachTurnsTowardTheAimPointAndNeverPastTheLimit()
		{
			// The SIGN and the CAP, neither of which the invariant sweep can catch: the mirror image
			// of the correct rotation is the same size and still lands the spawn behind the launcher,
			// so it satisfies everything the sweep asserts while sending the salvo round the wrong
			// side of the player.
			const int Cells = LargestMapCells;
			var mapCenter = MapCenter(Cells);
			var limit = MissileStrikeApproach.MaxCorridorDeviation.Angle;
			var leaned = 0;

			// An edge-midpoint home, because it is the one with in-map aim points a full 90 degrees
			// and more off its own corridor.
			var home = Cell(1, Cells / 2);
			var corridorYaw = (mapCenter - home).Yaw;

			foreach (var aim in AimPointSweepIncludingOnAndBehindHome(Cells, Cells, home))
			{
				if ((aim - home).HorizontalLengthSquared == 0)
					continue;

				var raw = Lean((aim - home).Yaw, corridorYaw);
				var got = Lean(MissileStrikeApproach.Bearing(home, aim, mapCenter), corridorYaw);

				Assert.That(Math.Abs(got), Is.LessThanOrEqualTo(limit),
					$"aim {aim}: the approach leaned past the limit");
				Assert.That(Math.Sign(got) == 0 || Math.Sign(got) == Math.Sign(raw), Is.True,
					$"aim {aim}: the approach turned round the wrong side of the launcher");
				Assert.That(Math.Abs(got), Is.LessThanOrEqualTo(Math.Abs(raw)),
					$"aim {aim}: the approach leaned FURTHER off the corridor than the player aimed");

				if (Math.Abs(raw) > limit)
					leaned++;
			}

			Assert.That(leaned, Is.GreaterThan(10),
				"an edge-midpoint home must produce plenty of out-of-corridor aim points, or this " +
				"test is asserting nothing");
		}

		[Test]
		public void ATargetDirectlyBehindTheLauncherIsApproachedFromDirectlyBehindIt()
		{
			// THE USER'S CASE, and the one an earlier draft of this fix got wrong by holding the lean
			// at the limit instead of tapering it: a strike placed between one's own Supply Route and
			// the map edge behind it. The standoff is a whole map diagonal plus a margin, so flying
			// the bare corridor still puts the birth point far behind the LAUNCHER as well as behind
			// the target -- which is the best answer available and is what the player asked for.
			const int Cells = LargestMapCells;
			var mapCenter = MapCenter(Cells);

			foreach (var (hx, hy, ax, ay) in new[]
			{
				(1, Cells / 2, 0, Cells / 2),        // due west of a west-edge home
				(Cells / 2, 1, Cells / 2, 0),        // due north of a north-edge home
				(Cells - 2, Cells / 2, Cells - 1, Cells / 2),
				(Cells / 2, Cells - 2, Cells / 2, Cells - 1),
			})
			{
				var home = Cell(hx, hy);
				var aim = Cell(ax, ay);
				var corridorYaw = (mapCenter - home).Yaw;

				Assert.That(Math.Abs(Lean((aim - home).Yaw, corridorYaw)), Is.EqualTo(512),
					$"home {hx},{hy}: the fixture must place the aim point DIRECTLY astern, or it is " +
					"not testing the taper's endpoint");

				Assert.That(MissileStrikeApproach.Bearing(home, aim, mapCenter), Is.EqualTo(corridorYaw),
					$"home {hx},{hy}: a target directly behind the launcher must be approached down " +
					"the bare corridor, dead astern");

				// And the spawn really is on the home-to-target line extended, not merely on the
				// right side of it: the lateral offset from that line is a rounding error.
				var spawn = MissileStrikeApproach
					.For(home, mapCenter, Cells, Cells, Margin, new[] { aim })
					.SpawnPosition(aim);
				var axis = aim - home;
				var off = spawn - home;
				var lateral = Math.Abs(((long)off.X * axis.Y) - ((long)off.Y * axis.X)) / axis.HorizontalLength;

				Assert.That(lateral, Is.LessThanOrEqualTo(1024),
					$"home {hx},{hy}: the birth point drifted off the launcher's own axis");
			}
		}

		[Test]
		public void TheLeanIsContinuousAcrossTheLimitAndFallsAwayBehindTheLauncher()
		{
			// The taper has to meet the cap where the cap ends, or a player walking an aim point
			// across that boundary sees the approach jump. Swept as a function of angle rather than
			// of aim point so the boundary is actually crossed rather than stepped over.
			var limit = MissileStrikeApproach.MaxCorridorDeviation.Angle;
			var home = new WPos(0, 0, 0);
			var mapCenter = new WPos(1024 * 1024, 0, 0);
			var corridorYaw = (mapCenter - home).Yaw;

			var previous = int.MaxValue;
			for (var raw = 0; raw <= 512; raw++)
			{
				// An aim point far enough out that its integer yaw is the angle asked for.
				var dir = new WVec(0, -(400 * 1024), 0).Rotate(WRot.FromYaw(corridorYaw + new WAngle(raw)));
				var aim = home + dir;
				if (Math.Abs(Lean((aim - home).Yaw, corridorYaw)) != raw)
					continue;

				var got = Math.Abs(Lean(MissileStrikeApproach.Bearing(home, aim, mapCenter), corridorYaw));

				if (raw <= limit)
				{
					Assert.That(got, Is.EqualTo(raw), $"raw {raw}: an in-corridor bearing was altered");
					previous = got;
					continue;
				}

				Assert.That(got, Is.LessThanOrEqualTo(limit), $"raw {raw}: leaned past the limit");
				Assert.That(got, Is.LessThanOrEqualTo(previous),
					$"raw {raw}: the lean grew again after the limit instead of tapering back");
				previous = got;
			}

			Assert.That(previous, Is.Zero,
				"the taper must reach exactly zero at 512 raw units (directly astern)");
		}

		[Test]
		public void TheClampNeverMovesTheSpawnBackInsideTheMap()
		{
			// The off-map proof of NoSpawnEverLandsInsideTheMapOnAnyShippedMapSize is bearing-FREE --
			// the spawn is a standoff away from an in-map aim point, and the standoff exceeds the
			// diagonal by the margin whichever way it points -- so redirecting the bearing cannot
			// break it. Asserted rather than merely argued, because it is the one property the
			// original design leaned on hardest and a reader has no reason to take on trust that a
			// change to the bearing left it alone.
			foreach (var (cellsX, cellsY) in ShippedMapSizes())
			{
				var mapCenter = Cell(cellsX / 2, cellsY / 2);
				var right = 1024 * cellsX;
				var bottom = 1024 * cellsY;

				foreach (var home in HomeSweep(cellsX, cellsY))
				{
					foreach (var aim in AimPointSweepIncludingOnAndBehindHome(cellsX, cellsY, home))
					{
						var spawn = MissileStrikeApproach
							.For(home, mapCenter, cellsX, cellsY, Margin, new[] { aim })
							.SpawnPosition(aim);

						var dx = Math.Max(Math.Max(0 - spawn.X, spawn.X - right), 0);
						var dy = Math.Max(Math.Max(0 - spawn.Y, spawn.Y - bottom), 0);
						var outside = new WVec(dx, dy, 0).HorizontalLength;

						Assert.That(outside, Is.GreaterThanOrEqualTo(Margin - 2048),
							$"a {cellsX}x{cellsY} map, home {home}, aim {aim}: the clamped bearing " +
							"brought the birth point back inside the map");

						Assert.That(spawn.X / 1024, Is.InRange(-2048, 2047));
						Assert.That(spawn.Y / 1024, Is.InRange(-2048, 2047));
					}
				}
			}
		}

		[Test]
		public void ASalvoStillFliesOneAzimuthWhenItsCentroidIsBehindTheLauncher()
		{
			// The clamp is applied ONCE, to the centroid's bearing, so a multi-warhead salvo placed
			// behind its own launcher must still arrive parallel. Applying it per warhead would
			// rebuild exactly the fan MissileStrikeApproach exists to remove, and would do it in the
			// case where the fan is widest, because aim points close to the launcher subtend the
			// largest angles at it.
			const int Cells = LargestMapCells;
			var home = Cell(14, 112);
			var salvo = new[]
			{
				Cell(10, 118), Cell(6, 122), Cell(14, 120),
				Cell(4, 114), Cell(11, 126), Cell(2, 119),
			};

			var approach = MissileStrikeApproach.For(home, MapCenter(Cells), Cells, Cells, Margin, salvo);
			var reference = salvo[0] - approach.SpawnPosition(salvo[0]);

			foreach (var aim in salvo)
				Assert.That(aim - approach.SpawnPosition(aim), Is.EqualTo(reference));

			// And that one azimuth is the CENTROID's, clamped -- not any single warhead's.
			var centroid = MissileStrikeApproach.Centroid(salvo);
			Assert.That(approach.Facing,
				Is.EqualTo(MissileStrikeApproach.Bearing(home, centroid, MapCenter(Cells))));
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

		// --- (5b) what the margin can and cannot buy -------------------------------------------

		/// <summary>
		/// Horizontal distance from <paramref name="aim"/> back along the flight path to the point
		/// where it crosses the map boundary -- i.e. the part of the approach that happens over the
		/// board. Found by bisection on the segment rather than by clipping algebra so that it stays
		/// integer: k runs over the segment in millionths, about a fifth of a world unit here.
		/// </summary>
		static int OnMapLeg(WPos spawn, WPos aim, int cellsX, int cellsY)
		{
			const int N = 1_000_000;
			var right = 1024 * cellsX;
			var bottom = 1024 * cellsY;
			var d = spawn - aim;

			WPos At(int k)
			{
				return new WPos(
					aim.X + (int)((long)d.X * k / N),
					aim.Y + (int)((long)d.Y * k / N),
					0);
			}

			bool Inside(int k)
			{
				var p = At(k);
				return p.X >= 0 && p.X <= right && p.Y >= 0 && p.Y <= bottom;
			}

			// k = 0 is the aim point, always inside; k = N is the spawn, which the margin proof puts
			// outside. So the crossing is bracketed and the bisection cannot run off either end.
			var lo = 0;
			var hi = N;
			while (hi - lo > 1)
			{
				var mid = lo + ((hi - lo) / 2);
				if (Inside(mid))
					lo = mid;
				else
					hi = mid;
			}

			return (aim - At(lo)).HorizontalLength;
		}

		[Test]
		public void RaisingTheApproachMarginCannotLengthenTheApproachThePlayerSees()
		{
			// THE POINT OF THIS TEST IS THAT IT IS A NEGATIVE RESULT, and it is here because the
			// obvious response to "I want to see the missile coming in from further away" is to raise
			// ApproachMargin, which does not do that and cannot.
			//
			// The flight path is the straight line through the aim point on the salvo's bearing, and
			// the bearing does not depend on the margin at all. Raising the margin slides the BIRTH
			// POINT further back along a line that is otherwise unchanged, so the segment of it lying
			// over the map -- every part of the approach a camera clamped to the map bounds can show
			// -- is identical to the world unit. All the extra distance is flown outside the board,
			// where the only thing it adds is warning time.
			foreach (var (cellsX, cellsY) in ShippedMapSizes())
			{
				var mapCenter = Cell(cellsX / 2, cellsY / 2);

				foreach (var home in new[] { Cell(1, 1), Cell(cellsX - 2, cellsY - 2), Cell(cellsX / 2, 1) })
				{
					foreach (var aim in AimPointSweep(cellsX, cellsY))
					{
						if ((aim - home).HorizontalLengthSquared == 0)
							continue;

						var today = MissileStrikeApproach.For(home, mapCenter, cellsX, cellsY, Margin, new[] { aim });
						var quadrupled = MissileStrikeApproach.For(home, mapCenter, cellsX, cellsY, 4 * Margin, new[] { aim });

						Assert.That(quadrupled.Facing, Is.EqualTo(today.Facing),
							"the margin must not touch the bearing, or the comparison below is not " +
							"comparing two lengths of one line");
						Assert.That(quadrupled.Standoff, Is.GreaterThan(today.Standoff),
							"the fixture must actually raise the standoff");

						var legToday = OnMapLeg(today.SpawnPosition(aim), aim, cellsX, cellsY);
						var legQuadrupled = OnMapLeg(quadrupled.SpawnPosition(aim), aim, cellsX, cellsY);

						Assert.That(Math.Abs(legQuadrupled - legToday), Is.LessThanOrEqualTo(4),
							$"a {cellsX}x{cellsY} map, home {home}, aim {aim}: quadrupling the margin " +
							"changed the over-the-board leg, which it has no way to do -- if this " +
							"fails the bearing has started depending on the standoff");
					}
				}
			}
		}

		[Test]
		public void RaisingTheApproachMarginBringsTheWarheadIntoViewLowerNotHigher()
		{
			// The second half of the same negative result, and the genuinely counter-intuitive one.
			// BallisticMissileFly ramps altitude linearly across the WHOLE standoff, from
			// SpawnAltitude down to the aim point (the baseZ term GetSlope is built from). At a fixed
			// distance from the target the fraction of that ramp still unspent is remaining /
			// standoff -- so a LONGER standoff leaves LESS of it, and the warhead comes into view
			// nearer the ground rather than higher up. Altitude is drawn as screen-y offset, so
			// lower means it also enters the frame LATER.
			//
			// Measured at 20 cells out, which is inside the visible half-window at every viewport
			// setting (see TheStandoffIsAlreadyFarBeyondAnythingTheCameraCanShow) and is therefore
			// the altitude the player actually sees the warhead arrive at. Modelled rather than run,
			// because the ramp is one line of arithmetic and the alternative is standing up a World.
			const int Cells = LargestMapCells;
			const int SpawnAltitude = 31 * 1024;   // highyieldnukemissile, among the tallest shipped.
			const int Remaining = 20 * 1024;
			var mapCenter = MapCenter(Cells);
			var home = Cell(1, 1);
			var aim = Cell(Cells - 2, Cells - 2);

			var today = MissileStrikeApproach.For(home, mapCenter, Cells, Cells, Margin, new[] { aim });
			var quadrupled = MissileStrikeApproach.For(home, mapCenter, Cells, Cells, 4 * Margin, new[] { aim });

			var rampToday = (long)SpawnAltitude * Remaining / today.Standoff;
			var rampQuadrupled = (long)SpawnAltitude * Remaining / quadrupled.Standoff;

			Assert.That(rampQuadrupled, Is.LessThan(rampToday),
				"raising the margin must flatten the terminal approach, not steepen it -- so it " +
				"makes the visible arrival slightly less dramatic while costing warning time");

			// The arc term is the other half of the altitude, and near the target it is very nearly
			// standoff-INVARIANT: peak = standoff * tan(LaunchAngle) / 4, and the parabola's height
			// at a remaining fraction f is 4 * peak * f * (1 - f), which for small f tends to
			// standoff * tan * f = tan * Remaining, with the standoff cancelling out. So the margin
			// cannot buy back through the arc the height it takes off the ramp.
			var tan30 = new WAngle(1024 / 12).Tan();

			long ArcHeight(int standoff)
			{
				var peak = (long)standoff * tan30 / (4 * 1024);
				return 4 * peak * Remaining / standoff * (standoff - Remaining) / standoff;
			}

			var arcToday = ArcHeight(today.Standoff);
			var arcQuadrupled = ArcHeight(quadrupled.Standoff);

			Assert.That(Math.Abs(arcQuadrupled - arcToday) * 100 / Math.Max(1, arcToday), Is.LessThanOrEqualTo(10),
				"the arc height 20 cells from the target must be near enough independent of the " +
				"standoff, or the reasoning above is wrong");

			// Together: the total height the warhead comes into view at goes DOWN, not up.
			Assert.That(arcQuadrupled + rampQuadrupled, Is.LessThan(arcToday + rampToday),
				"a quadrupled margin must not raise the altitude at which the warhead becomes " +
				"visible -- if it did, the margin would be a visible-drama lever after all");
		}

		[Test]
		public void TheStandoffIsAlreadyFarBeyondAnythingTheCameraCanShow()
		{
			// The bound that makes the two tests above matter rather than merely being true. The
			// world viewport is 1280x720 WORLD PIXELS at the default Medium setting on any native
			// resolution taller than 900 -- Viewport.CalculateMinimumZoom picks the zoom that lands
			// the window inside MediumWindowHeights 600..900 -- and 1920x1080 at Far on a 1080p
			// screen. mods/ww3mod/mod.yaml sets TileSize 24, so the widest a visible HALF-window can
			// ever be is 1920 / 2 / 24 = 40 cells, and the camera CENTRE is clamped to the map bounds
			// (Viewport.Center).
			//
			// The standoff on the SMALLEST shipped map is already more than twice that. There is no
			// setting, resolution or camera position at which a player watches the missile be born,
			// and none at which raising the margin shows them more of the flight.
			const int WidestVisibleHalfWindowCells = 40;

			var smallest = ShippedMapSizes().OrderBy(s => (s.X * s.X) + (s.Y * s.Y)).First();
			var standoffCells = MissileStrikeApproach.StandoffFor(smallest.X, smallest.Y, Margin) / 1024;

			Assert.That(standoffCells, Is.GreaterThan(2 * WidestVisibleHalfWindowCells),
				$"on the smallest shipped map ({smallest.X}x{smallest.Y}) the standoff is " +
				$"{standoffCells} cells; if it ever drops near {WidestVisibleHalfWindowCells} the " +
				"margin becomes a visible-approach lever and these tests need revisiting");
		}

		[Test]
		public void AQuadrupledMarginWouldStillNotReachEitherClamp()
		{
			// If the margin is ever raised anyway, the standoff must not silently hit MaxStandoff on
			// a large map while staying under it on a small one -- that would make big maps behave
			// differently from small ones for no reason a player could see. Checked at 4x the
			// shipped margin, well past anything worth proposing.
			foreach (var (cellsX, cellsY) in ShippedMapSizes())
			{
				var raised = MissileStrikeApproach.StandoffFor(cellsX, cellsY, 4 * Margin);

				Assert.That(raised, Is.LessThan(MissileStrikeApproach.MaxStandoff / 4),
					$"a {cellsX}x{cellsY} map at 4x the margin approaches the standoff ceiling");
				Assert.That(raised, Is.GreaterThan(MissileStrikeApproach.MinStandoff),
					$"a {cellsX}x{cellsY} map at 4x the margin hit the floor");
			}
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
