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
 * The user tests from main and this feature is landing in pieces. Every shipped map leaves Start
 * equal to End, so if a degenerate line ever stops being a no-op, those two say so.
 */

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
	}
}
