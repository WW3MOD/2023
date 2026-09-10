#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

/*
 * The DEFCON 3 dividing wall's geometry, and the Info defaults that keep it inert.
 *
 * Nothing in OpenRA.Test can construct a World, which is exactly why DefconWallGeometry is a plain
 * class: every predicate the three enforcement layers rely on is decided there and can be driven
 * here. What is NOT covered, stated rather than hidden behind a thinner test: the CustomTerrain
 * write, the per-player side cache, and all three enforcement layers themselves need a World and a
 * Map and are verified by reading.
 *
 * THE LOAD-BEARING TESTS ARE NoLineAuthoredIsAStrictNoOp AND TheShippedDefaultsAuthorNoLine.
 * No map authors a line, so if a degenerate line ever stops being a no-op, those two say so.
 *
 * WHAT CHANGED WHEN THE WALL WENT LIVE: no shipped map authors a line and none needs to -- the
 * line is DERIVED from the two sides' home locations, switched on by DeriveFromSpawns in
 * world.yaml. The Info default for that field stays FALSE, and DerivingFromSpawnsIsOffInTheDefaults
 * guards it, so "the C# defaults are inert" remains true and only the mod turns the wall on.
 *
 * AND THE ONE THIS FILE PREVIOUSLY COULD NOT HAVE CAUGHT: every test here drove a VERTICAL line,
 * which is the single angle a one-cell band actually seals. TheDefaultBandSealsALineAtEveryAngle
 * is the regression test for that; AOneCellBandLeaksOnADiagonal pins WHY the default is not 512.
 */

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DefconWallTest
	{
		const int Cell = 1024;
		const int HalfCell = 512;

		// A vertical line down x = 44, the shape a two-spawn map's wall actually takes. Built the way
		// the trait builds it: from cell centres, which is Map.CenterOfCell on a rectangular grid.
		static DefconWallGeometry VerticalAt(int cellX, int halfWidth = HalfCell)
		{
			var x = (cellX * Cell) + HalfCell;
			return new DefconWallGeometry(x, HalfCell, x, (59 * Cell) + HalfCell, halfWidth);
		}

		static long CentreOf(int cell)
		{
			return (cell * Cell) + HalfCell;
		}

		[Test]
		public void NoLineAuthoredIsAStrictNoOp()
		{
			// Start == End, which is what every shipped map has and what the Info defaults give.
			var geometry = new DefconWallGeometry(CentreOf(10), CentreOf(10), CentreOf(10), CentreOf(10), HalfCell);

			Assert.That(geometry.IsDegenerate, Is.True, "A zero-length line is not reporting as degenerate.");

			// Sweep the whole of a 100x60 map from both players' points of view. Nothing anywhere may
			// be beyond a wall that was never drawn, and no cell may be inside its band.
			for (var x = 0; x < 100; x++)
			{
				for (var y = 0; y < 60; y++)
				{
					var px = CentreOf(x);
					var py = CentreOf(y);

					Assert.That(geometry.IsBeyond(1, px, py), Is.False, $"Cell {x},{y} is beyond a wall nobody authored.");
					Assert.That(geometry.IsBeyond(-1, px, py), Is.False, $"Cell {x},{y} is beyond a wall nobody authored.");
					Assert.That(geometry.IsInWallBand(px, py), Is.False, $"Cell {x},{y} is inside a wall nobody authored.");
				}
			}
		}

		[Test]
		public void TheShippedDefaultsAuthorNoLine()
		{
			var info = new DefconWallInfo();

			Assert.That(info.Start, Is.EqualTo(info.End),
				"The default Info authors a real line, so every shipped map would grow a wall.");

			// 3 is the positioning phase and the only level the design puts a wall at: DEFCON 2 is
			// hold-fire and DEFCON 1 is open war, and the line is gone in both.
			Assert.That(info.ActiveLevels, Is.EqualTo(new[] { DefconEscalationState.Ceiling }));

			// Skirmish holds NoLevel, so the wall must never stand in it. This is the same guard
			// DefconEscalationTest.SkirmishIsAStrictNoOp makes for the level itself.
			Assert.That(info.ActiveLevels, Has.No.Member(DefconEscalationState.NoLevel),
				"The wall would stand in Skirmish, which is required to be a strict no-op.");
		}

		[Test]
		public void TheTwoSidesAreOppositeAndNeitherMayCross()
		{
			var geometry = VerticalAt(44);

			var westSide = geometry.SideOf(CentreOf(8), CentreOf(28));
			var eastSide = geometry.SideOf(CentreOf(80), CentreOf(28));

			Assert.That(westSide, Is.Not.EqualTo(DefconWallGeometry.NoSide));
			Assert.That(eastSide, Is.Not.EqualTo(DefconWallGeometry.NoSide));
			Assert.That(westSide, Is.Not.EqualTo(eastSide), "Both spawns resolved to the same half-plane.");

			// The rule is symmetric: this is a wall, not a one-way gate.
			Assert.That(geometry.IsBeyond(westSide, CentreOf(80), CentreOf(28)), Is.True, "West may cross east.");
			Assert.That(geometry.IsBeyond(eastSide, CentreOf(8), CentreOf(28)), Is.True, "East may cross west.");

			Assert.That(geometry.IsBeyond(westSide, CentreOf(8), CentreOf(28)), Is.False, "West is beyond its own half.");
			Assert.That(geometry.IsBeyond(eastSide, CentreOf(80), CentreOf(28)), Is.False, "East is beyond its own half.");
		}

		[Test]
		public void TheLineItselfIsSolidForBothSides()
		{
			var geometry = VerticalAt(44);
			var westSide = geometry.SideOf(CentreOf(8), CentreOf(28));
			var eastSide = -westSide;

			// Dead centre of the line: NoSide, and forbidden to everyone. Without this a player could
			// park an airframe on the seam and sit in both halves at once.
			Assert.That(geometry.SideOf(CentreOf(44), CentreOf(28)), Is.EqualTo(DefconWallGeometry.NoSide));
			Assert.That(geometry.IsBeyond(westSide, CentreOf(44), CentreOf(28)), Is.True);
			Assert.That(geometry.IsBeyond(eastSide, CentreOf(44), CentreOf(28)), Is.True);
			Assert.That(geometry.IsInWallBand(CentreOf(44), CentreOf(28)), Is.True);

			// One cell either side is clear of the band at the default half-width.
			Assert.That(geometry.IsInWallBand(CentreOf(43), CentreOf(28)), Is.False);
			Assert.That(geometry.IsInWallBand(CentreOf(45), CentreOf(28)), Is.False);
		}

		[Test]
		public void TheLineIsInfiniteSoNothingFliesAroundTheEnd()
		{
			// The authored endpoints stop at y = 59. An aircraft at y = 400, far past the end of the
			// segment, must still be correctly sided -- otherwise the wall has two open ends and is
			// not a wall at all. This is the property that makes authoring endpoints cheap.
			var geometry = VerticalAt(44);
			var westSide = geometry.SideOf(CentreOf(8), CentreOf(28));

			Assert.That(geometry.IsBeyond(westSide, CentreOf(80), CentreOf(400)), Is.True,
				"A point past the authored end of the line escaped the half-plane test.");
			Assert.That(geometry.IsBeyond(westSide, CentreOf(80), -CentreOf(400)), Is.True,
				"A point before the authored start of the line escaped the half-plane test.");
		}

		[Test]
		public void DepthIsNegativeAtHomeAndPositiveBeyond()
		{
			// This sign convention is what lets the turn-back layer react BEFORE the line is reached,
			// by testing depth against a negative margin. If it ever inverts, layer 2 turns aircraft
			// back in their own territory and the mode is unplayable.
			var geometry = VerticalAt(44);
			var westSide = geometry.SideOf(CentreOf(8), CentreOf(28));

			var atHome = geometry.DepthBeyond(westSide, CentreOf(34), CentreOf(28));
			var beyond = geometry.DepthBeyond(westSide, CentreOf(54), CentreOf(28));

			Assert.That(atHome, Is.LessThan(0), "Depth is not negative on the actor's own side.");
			Assert.That(beyond, Is.GreaterThan(0), "Depth is not positive past the line.");

			// Ten cells either way, so the magnitudes match and the scale is world units.
			Assert.That(atHome, Is.EqualTo(-10 * Cell));
			Assert.That(beyond, Is.EqualTo(10 * Cell));
		}

		[Test]
		public void TheNormalPointsHome()
		{
			// NearestPositionOnOwnSide walks along this vector, so a sign error here flies every
			// turned-back aircraft deeper into enemy territory instead of home.
			var geometry = VerticalAt(44);
			var westSide = geometry.SideOf(CentreOf(8), CentreOf(28));

			var normal = geometry.NormalTowards(westSide);
			Assert.That(geometry.SideOf(CentreOf(44) + normal.X, CentreOf(28) + normal.Y), Is.EqualTo(westSide),
				"The normal for a side points into the other side.");

			var away = geometry.NormalTowards(-westSide);
			Assert.That(geometry.SideOf(CentreOf(44) + away.X, CentreOf(28) + away.Y), Is.EqualTo(-westSide));
		}

		[Test]
		public void MapScaleCoordinatesDoNotOverflow()
		{
			// THE REASON THE GEOMETRY IS `long` THROUGHOUT. A 256-cell map reaches 262144 world units,
			// and a cross product of two such coordinates reaches ~6.9e10 -- which wraps silently in
			// `int` and inverts the sign, i.e. hands a player the whole enemy half. Diagonal line
			// across the largest supported map, tested at both far corners.
			var far = (255 * Cell) + HalfCell;
			var geometry = new DefconWallGeometry(HalfCell, HalfCell, far, far, HalfCell);

			// Above the diagonal and below it must be opposite, at full map scale.
			var above = geometry.SideOf(HalfCell, far);
			var below = geometry.SideOf(far, HalfCell);

			Assert.That(above, Is.Not.EqualTo(DefconWallGeometry.NoSide));
			Assert.That(below, Is.Not.EqualTo(DefconWallGeometry.NoSide));
			Assert.That(above, Is.Not.EqualTo(below), "Sign inverted at map scale -- the cross product overflowed.");

			// And a point on the diagonal is still exactly on it.
			Assert.That(geometry.SideOf(far, far), Is.EqualTo(DefconWallGeometry.NoSide));
		}

		[Test]
		public void ThePerpendicularBisectorSeparatesTwoSpawnsFairly()
		{
			// The derivation offered in place of hand-authoring: given two spawns, the fair line is
			// their perpendicular bisector. If this holds, a map needs no authored line at all.
			var west = new CPos(8, 28);
			var east = new CPos(80, 28);

			var (start, end) = DefconWallGeometry.PerpendicularBisector(west, east, 200);
			var geometry = new DefconWallGeometry(
				(start.X * Cell) + HalfCell, (start.Y * Cell) + HalfCell,
				(end.X * Cell) + HalfCell, (end.Y * Cell) + HalfCell, HalfCell);

			Assert.That(geometry.IsDegenerate, Is.False);

			var westSide = geometry.SideOf(CentreOf(west.X), CentreOf(west.Y));
			var eastSide = geometry.SideOf(CentreOf(east.X), CentreOf(east.Y));
			Assert.That(westSide, Is.Not.EqualTo(eastSide), "The bisector put both spawns in the same half.");

			// Fair means equidistant. The midpoint of 8 and 80 is 44, so both are 36 cells out.
			var westDistance = geometry.DistanceToLine(CentreOf(west.X), CentreOf(west.Y));
			var eastDistance = geometry.DistanceToLine(CentreOf(east.X), CentreOf(east.Y));
			Assert.That(westDistance, Is.EqualTo(eastDistance), "The derived line favours one spawn.");
			Assert.That(westDistance, Is.EqualTo(36 * Cell));
		}

		[Test]
		public void TwoCoincidentSpawnsDeriveNoLineRatherThanAnArbitraryOne()
		{
			// Degenerate input must produce a degenerate line, not a line in some default direction.
			// A map with stacked spawns should end up with no wall, which is visibly wrong and
			// therefore fixable -- rather than a wall pointing somewhere nobody chose.
			var (start, end) = DefconWallGeometry.PerpendicularBisector(new CPos(20, 20), new CPos(20, 20), 200);
			Assert.That(start, Is.EqualTo(end));

			var geometry = new DefconWallGeometry(
				(start.X * Cell) + HalfCell, (start.Y * Cell) + HalfCell,
				(end.X * Cell) + HalfCell, (end.Y * Cell) + HalfCell, HalfCell);
			Assert.That(geometry.IsDegenerate, Is.True);
		}

		[Test]
		public void AWiderWallBandIsWiderInBothDirections()
		{
			// HalfWidth is half the thickness, centred on the line -- not an offset to one side.
			var geometry = VerticalAt(44, 2 * Cell);

			Assert.That(geometry.IsInWallBand(CentreOf(43), CentreOf(28)), Is.True);
			Assert.That(geometry.IsInWallBand(CentreOf(45), CentreOf(28)), Is.True);
			Assert.That(geometry.IsInWallBand(CentreOf(42), CentreOf(28)), Is.True);
			Assert.That(geometry.IsInWallBand(CentreOf(46), CentreOf(28)), Is.True);
			Assert.That(geometry.IsInWallBand(CentreOf(41), CentreOf(28)), Is.False);
			Assert.That(geometry.IsInWallBand(CentreOf(47), CentreOf(28)), Is.False);
		}

		/// <summary>
		/// Is there an 8-connected step that crosses the line without entering the band? That is the
		/// only question that decides whether the wall divides anything, and it is not the question
		/// "are the cells either side in the band", which a vertical line answers misleadingly well.
		/// </summary>
		static bool Leaks(DefconWallGeometry geometry, int extent = 60)
		{
			for (var y = 0; y < extent; y++)
			{
				for (var x = 0; x < extent; x++)
				{
					var side = geometry.SideOf(CentreOf(x), CentreOf(y));
					if (side == DefconWallGeometry.NoSide || geometry.IsInWallBand(CentreOf(x), CentreOf(y)))
						continue;

					for (var dy = -1; dy <= 1; dy++)
					{
						for (var dx = -1; dx <= 1; dx++)
						{
							if (dx == 0 && dy == 0)
								continue;

							int nx = x + dx, ny = y + dy;
							if (nx < 0 || ny < 0 || nx >= extent || ny >= extent)
								continue;

							var otherSide = geometry.SideOf(CentreOf(nx), CentreOf(ny));
							if (otherSide == DefconWallGeometry.NoSide || otherSide == side)
								continue;

							if (!geometry.IsInWallBand(CentreOf(nx), CentreOf(ny)))
								return true;
						}
					}
				}
			}

			return false;
		}

		static DefconWallGeometry Through(CPos a, CPos b, long halfWidth)
		{
			var (start, end) = DefconWallGeometry.PerpendicularBisector(a, b, 512);
			return new DefconWallGeometry(
				(start.X * Cell) + HalfCell, (start.Y * Cell) + HalfCell,
				(end.X * Cell) + HalfCell, (end.Y * Cell) + HalfCell, halfWidth);
		}

		[Test]
		public void AOneCellBandLeaksOnADiagonal()
		{
			// THE BUG THE FEATURE SHIPPED WITH, pinned so the default cannot quietly go back to 512.
			// A one-cell band on a diagonal is a staircase of cells touching only at their corners,
			// and an 8-connected step goes straight between two of them. Measured in the connectivity
			// audit as every derived line leaking on every ground locomotor on all ten shipped maps.
			Assert.That(Leaks(Through(new CPos(10, 10), new CPos(50, 50), HalfCell)), Is.True,
				"A one-cell band on a 45-degree line is expected to leak; if it no longer does, the " +
				"reason the default is 1024 has changed and both should be revisited together.");

			// The same band on the axis-aligned line every earlier test in this file used. This is
			// why nothing caught it: the worked example authors a vertical line.
			Assert.That(Leaks(VerticalAt(30)), Is.False);
		}

		[Test]
		public void TheDefaultBandSealsALineAtEveryAngle()
		{
			// The floor is arithmetic. Two 8-adjacent cells differ by at most one cell per axis, so
			// their perpendicular distances to the line differ by at most sqrt(2) cells; when they
			// straddle it those distances sum to at most sqrt(2), so the nearer is within
			// sqrt(2)/2 = 0.707 cells = 724 world units. At or above that, every straddling pair has
			// a member in the band, at every angle.
			var info = new DefconWallInfo();
			Assert.That(info.HalfWidth.Length, Is.GreaterThanOrEqualTo(724),
				"HalfWidth is below the sqrt(2)/2-cell floor, so diagonal lines will leak.");

			// Spawn pairs chosen to sweep the angles: axis-aligned both ways, 45 degrees both ways,
			// and two shallow ones that are neither.
			var pairs = new[]
			{
				(new CPos(8, 28), new CPos(80, 28)),
				(new CPos(28, 8), new CPos(28, 80)),
				(new CPos(10, 10), new CPos(50, 50)),
				(new CPos(10, 50), new CPos(50, 10)),
				(new CPos(6, 12), new CPos(54, 30)),
				(new CPos(12, 6), new CPos(30, 54)),
			};

			foreach (var (a, b) in pairs)
			{
				var geometry = Through(a, b, info.HalfWidth.Length);
				Assert.That(geometry.IsDegenerate, Is.False);
				Assert.That(Leaks(geometry), Is.False,
					$"The default band leaks on the line derived from {a} and {b}, so the wall is " +
					"drawn but does not divide the map.");
			}
		}

		[Test]
		public void TwoAllianceGroupsDeriveAFairLine()
		{
			// A 2v2: each side's centroid is what the line is bisecting, not any one spawn.
			var (start, end) = DefconWallGeometry.BisectorOfSides(new[]
			{
				(0, new CPos(8, 20)), (0, new CPos(8, 36)),
				(1, new CPos(80, 20)), (1, new CPos(80, 36)),
			}, 200);

			var geometry = new DefconWallGeometry(
				(start.X * Cell) + HalfCell, (start.Y * Cell) + HalfCell,
				(end.X * Cell) + HalfCell, (end.Y * Cell) + HalfCell, HalfCell);

			Assert.That(geometry.IsDegenerate, Is.False);

			// Centroids are (8,28) and (80,28), so this is the x=44 line the two-spawn case gives,
			// and every one of the four homes is the same distance from it as its own team-mate.
			foreach (var home in new[] { new CPos(8, 20), new CPos(8, 36) })
				Assert.That(geometry.DistanceToLine(CentreOf(home.X), CentreOf(home.Y)), Is.EqualTo(36 * Cell));

			var west = geometry.SideOf(CentreOf(8), CentreOf(20));
			var east = geometry.SideOf(CentreOf(80), CentreOf(20));
			Assert.That(west, Is.Not.EqualTo(east), "The derived line put both teams in the same half.");
			Assert.That(geometry.SideOf(CentreOf(8), CentreOf(36)), Is.EqualTo(west),
				"Two allies ended up on opposite sides of their own wall.");
		}

		[Test]
		public void AThreeWayFreeForAllDerivesNoLine()
		{
			// No line is visibly wrong and therefore fixable; a line pointing somewhere nobody chose
			// is not. Same reasoning as TwoCoincidentSpawnsDeriveNoLineRatherThanAnArbitraryOne.
			var (start, end) = DefconWallGeometry.BisectorOfSides(new[]
			{
				(0, new CPos(8, 8)), (1, new CPos(80, 8)), (2, new CPos(44, 80)),
			}, 200);

			Assert.That(start, Is.EqualTo(end), "A three-way FFA derived a dividing line.");
		}

		[Test]
		public void OneSideAloneDerivesNoLine()
		{
			var (start, end) = DefconWallGeometry.BisectorOfSides(new[]
			{
				(0, new CPos(8, 8)), (0, new CPos(20, 20)),
			}, 200);

			Assert.That(start, Is.EqualTo(end), "A match with no enemy derived a dividing line.");
		}

		[Test]
		public void TheDrawnLineStopsAtTheMapEdge()
		{
			// The line is INFINITE and the map is not. The derived endpoints sit hundreds of cells
			// outside the map, and the annotation pass runs after the shroud pass -- so drawing
			// between them would streak across the blacked-out margin with nothing covering it.
			var geometry = Through(new CPos(8, 28), new CPos(80, 28), Cell);

			var left = 1L * Cell;
			var top = 1L * Cell;
			var right = 96L * Cell;
			var bottom = 96L * Cell;

			Assert.That(geometry.ClipToRect(left, top, right, bottom, out var start, out var end), Is.True);

			foreach (var p in new[] { start, end })
			{
				// On the line, to within the truncation the integer division can introduce.
				Assert.That(geometry.DistanceToLine(p.X, p.Y), Is.LessThanOrEqualTo(Cell),
					"A clipped endpoint is not on the line it was clipped from.");

				Assert.That(p.X, Is.InRange(left, right));
				Assert.That(p.Y, Is.InRange(top, bottom));
			}

			// This pair bisects horizontally, so the drawn segment must span the map vertically.
			Assert.That(Math.Abs(start.Y - end.Y), Is.EqualTo(bottom - top));
		}

		[Test]
		public void ADiagonalLineIsClippedToTheMapOnBothEnds()
		{
			var geometry = Through(new CPos(1, 4), new CPos(96, 93), Cell);

			Assert.That(geometry.ClipToRect(Cell, Cell, 96L * Cell, 96L * Cell, out var start, out var end), Is.True);
			Assert.That(start, Is.Not.EqualTo(end));

			foreach (var p in new[] { start, end })
			{
				Assert.That(geometry.DistanceToLine(p.X, p.Y), Is.LessThanOrEqualTo(Cell));
				Assert.That(p.X, Is.InRange(Cell, 96L * Cell));
				Assert.That(p.Y, Is.InRange(Cell, 96L * Cell));
			}
		}

		[Test]
		public void ALineThatMissesTheMapDrawsNothing()
		{
			// A rectangle far off the line's path. Returning true here would draw a segment that is
			// not on the border anywhere.
			var geometry = Through(new CPos(8, 28), new CPos(80, 28), Cell);
			Assert.That(geometry.ClipToRect(200L * Cell, 200L * Cell, 300L * Cell, 300L * Cell,
				out _, out _), Is.False);

			// And a wall that was never authored draws nothing at all.
			var degenerate = new DefconWallGeometry(CentreOf(10), CentreOf(10), CentreOf(10), CentreOf(10), HalfCell);
			Assert.That(degenerate.ClipToRect(0, 0, 96L * Cell, 96L * Cell, out _, out _), Is.False);
		}

		[Test]
		public void DerivingFromSpawnsIsOffInTheDefaults()
		{
			// The mod turns the wall on in world.yaml. The C# defaults must stay inert so that a
			// scenario or test which never asked for a wall cannot grow one.
			Assert.That(new DefconWallInfo().DeriveFromSpawns, Is.False,
				"The Info default now authors a wall, so every map using default rules grows one.");
		}
	}
}
