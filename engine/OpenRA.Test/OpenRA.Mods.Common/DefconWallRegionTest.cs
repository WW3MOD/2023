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
 * The DEFCON 3 border authored as a SET OF CELLS rather than a line.
 *
 * DefconWallRegion is a plain class for exactly the reason DefconWallGeometry is: nothing in
 * OpenRA.Test can construct a World, so every predicate the enforcement layers rely on has to be
 * decidable without one. What is NOT covered here, stated rather than hidden: the terrain scan that
 * turns `Water, River, Bridge` into a cell set needs a Map and is verified by reading plus by
 * tools/nav-guard/defcon_wall_audit.py, which runs the same scan statically over the shipped maps.
 *
 * THE TWO LOAD-BEARING TESTS ARE ASeparatingRegionPutsTheTwoHomesInDifferentComponents AND
 * AOneCellDiagonalRegionLeaks. The first is the whole point of the feature; the second is the
 * 2026-09-10 defect (DISCOVERIES, `HalfWidth: 512` leaking on 131 map/locomotor combinations)
 * reappearing in a form a hand-authored region can reintroduce -- a diagonal of single cells is
 * drawn, is visible, and divides nothing.
 */

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DefconWallRegionTest
	{
		const int Cell = 1024;
		const int HalfCell = 512;

		const int Width = 40;
		const int Height = 30;

		static long CentreOf(int cell)
		{
			return (cell * Cell) + HalfCell;
		}

		static DefconWallRegion Build(IEnumerable<CPos> blocked)
		{
			return new DefconWallRegion(0, 0, Width, Height, blocked, _ => true);
		}

		/// <summary>A full-height vertical wall down one column -- the region spelling of the line every
		/// DefconWallTest case drives, so the two can be compared directly.</summary>
		static DefconWallRegion VerticalAt(int columnX)
		{
			var cells = new List<CPos>();
			for (var y = 0; y < Height; y++)
				cells.Add(new CPos(columnX, y));

			return Build(cells);
		}

		[Test]
		public void ASeparatingRegionPutsTheTwoHomesInDifferentComponents()
		{
			// THE WHOLE POINT OF THE FEATURE. Sides are component ids, so "different sides" is
			// "different components" -- and unlike the line's sign test, this is a statement about
			// reachability: there is genuinely no 8-connected walk between these two cells.
			var region = VerticalAt(20);

			Assert.That(region.IsDegenerate, Is.False);
			Assert.That(region.ComponentCount, Is.EqualTo(2), "A full-height wall did not cut the map in two.");

			var west = region.SideOf(new CPos(5, 15));
			var east = region.SideOf(new CPos(35, 15));

			Assert.That(west, Is.Not.EqualTo(DefconWallRegion.Unlabelled));
			Assert.That(east, Is.Not.EqualTo(DefconWallRegion.Unlabelled));
			Assert.That(west, Is.Not.EqualTo(east), "Both homes resolved to the same component.");

			// Symmetric, like the line: this is a border, not a one-way gate.
			Assert.That(region.IsBeyond(west, new CPos(35, 15)), Is.True, "West may cross east.");
			Assert.That(region.IsBeyond(east, new CPos(5, 15)), Is.True, "East may cross west.");
			Assert.That(region.IsBeyond(west, new CPos(5, 15)), Is.False, "West is beyond its own half.");
			Assert.That(region.IsBeyond(east, new CPos(35, 15)), Is.False, "East is beyond its own half.");
		}

		[Test]
		public void TheBorderItselfIsSolidForBothSides()
		{
			// A cell of the border belongs to no component and is forbidden to everyone, which is what
			// stops a player parking on it. Same ruling the line makes for its band.
			var region = VerticalAt(20);
			var west = region.SideOf(new CPos(5, 15));
			var east = region.SideOf(new CPos(35, 15));

			Assert.That(region.SideOf(new CPos(20, 15)), Is.EqualTo(DefconWallRegion.Unlabelled));
			Assert.That(region.IsInWallBand(new CPos(20, 15)), Is.True);
			Assert.That(region.IsBeyond(west, new CPos(20, 15)), Is.True);
			Assert.That(region.IsBeyond(east, new CPos(20, 15)), Is.True);

			// One cell either side is clear of the border.
			Assert.That(region.IsInWallBand(new CPos(19, 15)), Is.False);
			Assert.That(region.IsInWallBand(new CPos(21, 15)), Is.False);
		}

		[Test]
		public void AOneCellDiagonalRegionLeaks()
		{
			// THE 2026-09-10 DEFECT, IN THE SHAPE A HAND-AUTHORED REGION CAN REINTRODUCE. A staircase
			// of single cells touches only at its corners and an 8-connected step goes straight
			// between two of them, so the border is drawn, is visible, and divides nothing. The
			// labelling is 8-connected precisely so that this comes back as ONE component rather than
			// as a separation that does not exist.
			var diagonal = new List<CPos>();
			for (var i = 0; i < Height; i++)
				diagonal.Add(new CPos(i, i));

			var region = Build(diagonal);

			Assert.That(region.ComponentCount, Is.EqualTo(1),
				"A one-cell-wide diagonal reported a separation it does not provide -- the labelling " +
				"has stopped being 8-connected, and every hand-authored diagonal border is now a lie.");
			Assert.That(region.IsDegenerate, Is.True,
				"A region that leaves the map in one piece must report degenerate, so the wall stays down.");
		}

		[Test]
		public void ATwoCellDiagonalRegionSeals()
		{
			// The fix for the above, and the rule the Info documentation states: a hand-drawn diagonal
			// must be two cells wide. Same arithmetic as the line's 724-unit floor.
			var diagonal = new List<CPos>();
			for (var i = 0; i < Height; i++)
			{
				diagonal.Add(new CPos(i, i));
				diagonal.Add(new CPos(i + 1, i));
			}

			var region = Build(diagonal);

			Assert.That(region.IsDegenerate, Is.False);
			Assert.That(region.ComponentCount, Is.GreaterThanOrEqualTo(2),
				"A two-cell-wide diagonal failed to seal, so the analytic floor no longer holds.");

			var above = region.SideOf(new CPos(25, 5));
			var below = region.SideOf(new CPos(5, 25));
			Assert.That(above, Is.Not.EqualTo(DefconWallRegion.Unlabelled));
			Assert.That(below, Is.Not.EqualTo(DefconWallRegion.Unlabelled));
			Assert.That(above, Is.Not.EqualTo(below), "The two-cell diagonal put both corners in one component.");
		}

		[Test]
		public void ARegionThatDividesNothingIsDegenerate()
		{
			// A wall that stops short of the map edge leaves a gap round the end -- the exact shape
			// River Zeta's water has, and the reason end caps have to be authored by hand. The border
			// must report this rather than claim a division it does not make.
			var stubby = new List<CPos>();
			for (var y = 0; y < Height - 3; y++)
				stubby.Add(new CPos(20, y));

			var region = Build(stubby);

			Assert.That(region.ComponentCount, Is.EqualTo(1), "A wall with a gap round the end still separated the map.");
			Assert.That(region.IsDegenerate, Is.True);

			// And every query is inert while it is degenerate, exactly as a degenerate line is.
			Assert.That(region.IsBeyond(0, new CPos(35, 15)), Is.False);
			Assert.That(region.DepthBeyond(0, CentreOf(35), CentreOf(15)), Is.LessThan(0));
		}

		[Test]
		public void AnEmptyRegionIsAStrictNoOp()
		{
			var region = Build(System.Array.Empty<CPos>());

			Assert.That(region.IsDegenerate, Is.True);
			Assert.That(region.BlockedCells, Is.Empty);

			for (var x = 0; x < Width; x++)
			{
				for (var y = 0; y < Height; y++)
				{
					Assert.That(region.IsBeyond(0, new CPos(x, y)), Is.False,
						$"Cell {x},{y} is beyond a border nobody authored.");
					Assert.That(region.IsInWallBand(new CPos(x, y)), Is.False,
						$"Cell {x},{y} is inside a border nobody authored.");
				}
			}
		}

		[Test]
		public void DepthIsNegativeAtHomeAndPositiveBeyond()
		{
			// The sign convention DefconWallTurnBack's margin arithmetic depends on, and it is the
			// line's convention exactly -- if it ever inverts, the turn-back layer sends aircraft
			// home from their own territory and the mode is unplayable.
			var region = VerticalAt(20);
			var west = region.SideOf(new CPos(5, 15));

			var atHome = region.DepthBeyond(west, CentreOf(10), CentreOf(15));
			var beyond = region.DepthBeyond(west, CentreOf(30), CentreOf(15));
			var onBorder = region.DepthBeyond(west, CentreOf(20), CentreOf(15));

			Assert.That(atHome, Is.LessThan(0), "Depth is not negative on the actor's own side.");
			Assert.That(beyond, Is.GreaterThan(0), "Depth is not positive past the border.");
			Assert.That(onBorder, Is.Zero, "Depth is not zero inside the border itself.");

			// Ten cells either way from column 20, and the magnitude is in world units.
			Assert.That(atHome, Is.EqualTo(-10 * Cell));
			Assert.That(beyond, Is.EqualTo(10 * Cell));
		}

		[Test]
		public void DepthGrowsWithDistanceFromTheBorder()
		{
			// Monotonic, which is what lets the turn-back layer compare two depths and tell whether a
			// move is taking an airframe further in -- Aircraft.SetPosition does exactly that.
			var region = VerticalAt(20);
			var west = region.SideOf(new CPos(5, 15));

			var near = region.DepthBeyond(west, CentreOf(25), CentreOf(15));
			var far = region.DepthBeyond(west, CentreOf(30), CentreOf(15));

			Assert.That(far, Is.GreaterThan(near),
				"Depth past the border did not grow with distance, so the deeper-in test cannot work.");
		}

		[Test]
		public void TheNormalPointsHome()
		{
			// NearestPositionOnOwnSide walks along this vector, so a sign error flies every turned-back
			// aircraft deeper into enemy territory. Asked from BEYOND the border, which is where the
			// turn-back layer actually asks it.
			var region = VerticalAt(20);
			var west = region.SideOf(new CPos(5, 15));

			var normal = region.NormalTowards(west, CentreOf(25), CentreOf(15));
			Assert.That(normal.X, Is.LessThan(0), "The way home from the east side does not point west.");

			var east = region.SideOf(new CPos(35, 15));
			var other = region.NormalTowards(east, CentreOf(15), CentreOf(15));
			Assert.That(other.X, Is.GreaterThan(0), "The way home from the west side does not point east.");
		}

		[Test]
		public void TheNormalBendsWithABorderThatBends()
		{
			// THE ONE PLACE THE REGION GENUINELY DIFFERS FROM THE LINE, and the reason it is worth
			// having: the line's normal is a constant, so on an L-shaped border it points the wrong
			// way along one arm. The region's depends on where it is asked from.
			//
			// An L: down column 20 for the top half, then along row 15 to the east edge.
			var cells = new List<CPos>();
			for (var y = 0; y <= 15; y++)
				cells.Add(new CPos(20, y));
			for (var x = 20; x < Width; x++)
				cells.Add(new CPos(x, 15));

			var region = new DefconWallRegion(0, 0, Width, Height, cells, _ => true);
			Assert.That(region.IsDegenerate, Is.False);

			// The pocket enclosed by the L's two arms: north-east of the corner.
			var pocket = region.SideOf(new CPos(30, 5));
			var outside = region.SideOf(new CPos(5, 5));
			Assert.That(pocket, Is.Not.EqualTo(outside), "The L did not enclose the north-east quarter.");

			// From inside the pocket the way out is WEST across the vertical arm; from below the
			// horizontal arm it is NORTH. A single constant normal cannot be both.
			var fromPocket = region.NormalTowards(outside, CentreOf(30), CentreOf(5));
			var fromBelow = region.NormalTowards(outside, CentreOf(30), CentreOf(25));

			Assert.That(fromPocket, Is.Not.EqualTo(fromBelow),
				"The region's normal is constant, so it is no better than the line on a bending border.");
			Assert.That(fromPocket.X, Is.LessThan(0), "Leaving the pocket did not head west toward the open map.");
		}

		[Test]
		public void APlayerWithNoSideIsNeverFencedIn()
		{
			// Unlabelled is the region's "no side": spectators, and anything whose home is off-map or
			// inside the border. They must be unconstrained rather than forbidden everywhere, which is
			// the ruling the line makes for NoSide and for the same reason.
			var region = VerticalAt(20);

			Assert.That(region.IsBeyond(DefconWallRegion.Unlabelled, new CPos(5, 15)), Is.False);
			Assert.That(region.IsBeyond(DefconWallRegion.Unlabelled, new CPos(35, 15)), Is.False);
			Assert.That(region.DepthBeyond(DefconWallRegion.Unlabelled, CentreOf(5), CentreOf(15)),
				Is.LessThan(0), "A sideless player got a positive depth, i.e. was ruled out of bounds.");
		}

		[Test]
		public void OffMapCellsAreNotBeyondTheBorder()
		{
			// A line divides the whole plane and its answer off the map edge is meaningful; a region's
			// is not. Returning "beyond" for every unlabelled cell would forbid the entire border ring
			// outside Bounds and strand aircraft that legitimately orbit the map edge -- and nothing
			// can cross out there anyway, because reaching it means crossing labelled land first.
			var region = VerticalAt(20);
			var west = region.SideOf(new CPos(5, 15));

			Assert.That(region.IsBeyond(west, new CPos(-5, 15)), Is.False);
			Assert.That(region.IsBeyond(west, new CPos(Width + 5, 15)), Is.False);
			Assert.That(region.SideOf(new CPos(-5, 15)), Is.EqualTo(DefconWallRegion.Unlabelled));
		}

		[Test]
		public void ImpassableTerrainDoesNotBecomeAThirdSide()
		{
			// The passable predicate exists so terrain the border did not author still breaks
			// connectivity honestly. A cliff inside one player's half must not read as "beyond" --
			// it is not the enemy's territory, it is just somewhere nobody can stand.
			var cells = new List<CPos>();
			for (var y = 0; y < Height; y++)
				cells.Add(new CPos(20, y));

			// A lake at 10,10 that belongs to no component.
			static bool Passable(CPos c) => !(c.X >= 10 && c.X <= 12 && c.Y >= 10 && c.Y <= 12);

			var region = new DefconWallRegion(0, 0, Width, Height, cells, Passable);
			var west = region.SideOf(new CPos(5, 15));

			Assert.That(region.SideOf(new CPos(11, 11)), Is.EqualTo(DefconWallRegion.Unlabelled));
			Assert.That(region.IsBeyond(west, new CPos(11, 11)), Is.False,
				"Impassable terrain inside a player's own half is being reported as across the border.");
			Assert.That(region.DepthBeyond(west, CentreOf(11), CentreOf(11)), Is.LessThan(0),
				"An unlabelled cell in our own half got a positive depth, so the turn-back layer will " +
				"fly airframes home from over their own cliffs.");
		}

		[Test]
		public void TheCellForAWorldPositionTruncatesTowardNegativeInfinity()
		{
			// C#'s integer division truncates toward ZERO, which folds the two cells either side of
			// the origin into one and mis-sides everything in the map's top-left border ring. The map
			// bounds start at 1,1 and AllCells reaches 0,0 and beyond, so this is reachable.
			Assert.That(DefconWallRegion.CellContaining(CentreOf(3), CentreOf(4)), Is.EqualTo(new CPos(3, 4)));
			Assert.That(DefconWallRegion.CellContaining(0, 0), Is.EqualTo(new CPos(0, 0)));
			Assert.That(DefconWallRegion.CellContaining(-1, -1), Is.EqualTo(new CPos(-1, -1)));
			Assert.That(DefconWallRegion.CellContaining(-Cell, -Cell), Is.EqualTo(new CPos(-1, -1)));
			Assert.That(DefconWallRegion.CellContaining(-Cell - 1, -Cell - 1), Is.EqualTo(new CPos(-2, -2)));
		}

		[Test]
		public void TheRegionFieldsAreEmptyInTheShippedDefaults()
		{
			// The C# defaults must leave every map on the line path, for the same reason
			// DeriveFromSpawns defaults false: a scenario or test that never asked for a region border
			// cannot grow one, and world.yaml stays the single place the border is configured.
			var info = new DefconWallInfo();

			Assert.That(info.RegionTerrainTypes, Is.Empty,
				"The Info default authors a terrain region, so every map using default rules grows one.");
			Assert.That(info.RegionCells, Is.Empty,
				"The Info default authors border cells, so every map using default rules grows one.");
		}

		[Test]
		public void BlockedCellsAreDeduplicatedAndOrdered()
		{
			// The renderer enumerates this collection, and the trait writes CustomTerrain from it. A
			// duplicate would be harmless there but an unordered set leaking into a render order would
			// be a needless nondeterminism, and the same cell authored twice is a plausible typo.
			var region = Build(new[]
			{
				new CPos(20, 5), new CPos(20, 4), new CPos(20, 5), new CPos(19, 4),
			});

			Assert.That(region.BlockedCells.Count, Is.EqualTo(3), "A duplicated cell was not collapsed.");

			CPos? previous = null;
			foreach (var cell in region.BlockedCells)
			{
				if (previous.HasValue)
					Assert.That(cell.Y > previous.Value.Y || (cell.Y == previous.Value.Y && cell.X > previous.Value.X),
						"The blocked cells are not in a deterministic row-major order.");

				previous = cell;
			}
		}
	}
}
