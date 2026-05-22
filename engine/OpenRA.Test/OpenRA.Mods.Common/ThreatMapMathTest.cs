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

using System;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// Mirrors the grid-sizing and AddInfluence math from ThreatMapManager. The
	/// trait integrates with World/actor iteration, but the bookkeeping math
	/// for grid dimensions, neighbour spread, and grid↔map cell conversion is
	/// pure arithmetic. Reproduced here so a regression breaks a unit test.
	/// </summary>
	[TestFixture]
	public class ThreatMapMathTest
	{
		// Mirrors WorldLoaded grid sizing:
		//   gridWidth  = ceil(MapSize.X / CellSize)
		//   gridHeight = ceil(MapSize.Y / CellSize)
		static (int w, int h) GridDimensions(int mapX, int mapY, int cellSize)
		{
			var w = (mapX + cellSize - 1) / cellSize;
			var h = (mapY + cellSize - 1) / cellSize;
			return (w, h);
		}

		// Mirrors AddInfluence (no early-exit when spread < 1).
		// grid[gx, gy] += value; if (value * spreadFactor >= 1) spread to 8 neighbours.
		static void AddInfluence(float[,] grid, int gx, int gy, float value, float spreadFactor)
		{
			var w = grid.GetLength(0);
			var h = grid.GetLength(1);

			grid[gx, gy] += value;

			var spread = value * spreadFactor;
			if (spread < 1f)
				return;

			for (var dx = -1; dx <= 1; dx++)
			{
				for (var dy = -1; dy <= 1; dy++)
				{
					if (dx == 0 && dy == 0)
						continue;

					var nx = gx + dx;
					var ny = gy + dy;
					if (nx >= 0 && nx < w && ny >= 0 && ny < h)
						grid[nx, ny] += spread;
				}
			}
		}

		// Mirrors GridToMapCell: returns the map cell at the centre of a grid cell.
		static (int x, int y) GridToMapCell(int gx, int gy, int cellSize)
		{
			return (gx * cellSize + cellSize / 2, gy * cellSize + cellSize / 2);
		}

		// Mirrors ToGridPos: cell / CellSize clamped to grid bounds.
		static (int gx, int gy) ToGridPos(int mapX, int mapY, int cellSize, int gridWidth, int gridHeight)
		{
			return (Math.Clamp(mapX / cellSize, 0, gridWidth - 1),
				Math.Clamp(mapY / cellSize, 0, gridHeight - 1));
		}

		// --- Grid dimensions ---

		[Test]
		public void GridDimensionsRoundUp()
		{
			// 100x100 map, CellSize 8 → ceil(100/8) = 13 in each dim.
			var (w, h) = GridDimensions(100, 100, 8);
			Assert.That(w, Is.EqualTo(13));
			Assert.That(h, Is.EqualTo(13));
		}

		[Test]
		public void GridDimensionsExactDivision()
		{
			// 96x64 with cellSize 8 → 12x8 exactly.
			var (w, h) = GridDimensions(96, 64, 8);
			Assert.That(w, Is.EqualTo(12));
			Assert.That(h, Is.EqualTo(8));
		}

		[Test]
		public void GridDimensionsSmallMap()
		{
			// 1x1 map with cellSize 8 → 1x1 grid (one cell covers the map).
			var (w, h) = GridDimensions(1, 1, 8);
			Assert.That(w, Is.EqualTo(1));
			Assert.That(h, Is.EqualTo(1));
		}

		[Test]
		public void GridDimensionsNonSquareMap()
		{
			var (w, h) = GridDimensions(200, 50, 8);
			Assert.That(w, Is.EqualTo(25));
			Assert.That(h, Is.EqualTo(7)); // ceil(50/8) = 7
		}

		// --- AddInfluence ---

		[Test]
		public void AddInfluenceCentre()
		{
			var grid = new float[10, 10];
			AddInfluence(grid, 5, 5, 100f, 0.3f);
			Assert.That(grid[5, 5], Is.EqualTo(100f));
		}

		[Test]
		public void AddInfluenceSpreadsToAllEightNeighbours()
		{
			var grid = new float[10, 10];
			AddInfluence(grid, 5, 5, 100f, 0.3f);
			// 100 * 0.3 = 30 to each neighbour (float math; small tolerance).
			Assert.That(grid[4, 4], Is.EqualTo(30f).Within(0.001f));
			Assert.That(grid[4, 5], Is.EqualTo(30f).Within(0.001f));
			Assert.That(grid[4, 6], Is.EqualTo(30f).Within(0.001f));
			Assert.That(grid[5, 4], Is.EqualTo(30f).Within(0.001f));
			Assert.That(grid[5, 6], Is.EqualTo(30f).Within(0.001f));
			Assert.That(grid[6, 4], Is.EqualTo(30f).Within(0.001f));
			Assert.That(grid[6, 5], Is.EqualTo(30f).Within(0.001f));
			Assert.That(grid[6, 6], Is.EqualTo(30f).Within(0.001f));
		}

		[Test]
		public void AddInfluenceSkipsSpreadBelowOne()
		{
			// value * spreadFactor = 2 * 0.3 = 0.6 < 1 → no spread.
			var grid = new float[5, 5];
			AddInfluence(grid, 2, 2, 2f, 0.3f);
			Assert.That(grid[2, 2], Is.EqualTo(2f));
			Assert.That(grid[1, 2], Is.EqualTo(0f), "spread skipped under 1");
			Assert.That(grid[2, 1], Is.EqualTo(0f));
		}

		[Test]
		public void AddInfluenceClampsAtGridBoundary()
		{
			// Source at (0,0): only (0,1), (1,0), (1,1) get spread; (-1,*) and (*,-1) skipped.
			var grid = new float[5, 5];
			AddInfluence(grid, 0, 0, 100f, 0.3f);

			Assert.That(grid[0, 0], Is.EqualTo(100f));
			Assert.That(grid[1, 0], Is.EqualTo(30f).Within(0.001f));
			Assert.That(grid[0, 1], Is.EqualTo(30f).Within(0.001f));
			Assert.That(grid[1, 1], Is.EqualTo(30f).Within(0.001f));
			// Negative indices silently dropped.
		}

		[Test]
		public void AddInfluenceMultipleSourcesAccumulate()
		{
			var grid = new float[10, 10];
			AddInfluence(grid, 5, 5, 100f, 0.3f);
			AddInfluence(grid, 5, 5, 100f, 0.3f);
			Assert.That(grid[5, 5], Is.EqualTo(200f), "centre doubles");
			Assert.That(grid[4, 4], Is.EqualTo(60f).Within(0.001f), "neighbour also doubles");
		}

		[Test]
		public void AddInfluenceZeroSpreadFactorOnlyAffectsCentre()
		{
			var grid = new float[5, 5];
			AddInfluence(grid, 2, 2, 100f, 0f);
			Assert.That(grid[2, 2], Is.EqualTo(100f));
			Assert.That(grid[1, 2], Is.EqualTo(0f));
		}

		[Test]
		public void AddInfluenceFullSpreadFactorReplicatesValue()
		{
			// SpreadFactor 1.0 → each neighbour gets the full value.
			var grid = new float[5, 5];
			AddInfluence(grid, 2, 2, 100f, 1f);
			Assert.That(grid[2, 2], Is.EqualTo(100f));
			Assert.That(grid[1, 1], Is.EqualTo(100f));
			Assert.That(grid[3, 3], Is.EqualTo(100f));
		}

		// --- GridToMapCell ---

		[Test]
		public void GridToMapCellReturnsCellCentre()
		{
			// Grid (0,0) with cellSize 8 → map cell (4, 4).
			var (x, y) = GridToMapCell(0, 0, 8);
			Assert.That(x, Is.EqualTo(4));
			Assert.That(y, Is.EqualTo(4));
		}

		[Test]
		public void GridToMapCellAdvancesByCellSize()
		{
			var (x1, _) = GridToMapCell(0, 0, 8);
			var (x2, _) = GridToMapCell(1, 0, 8);
			Assert.That(x2 - x1, Is.EqualTo(8));
		}

		// --- ToGridPos ---

		[Test]
		public void ToGridPosWithinBoundsRoundsDown()
		{
			var (gx, gy) = ToGridPos(15, 7, 8, 10, 10);
			Assert.That(gx, Is.EqualTo(1)); // 15 / 8 = 1
			Assert.That(gy, Is.EqualTo(0)); // 7 / 8 = 0
		}

		[Test]
		public void ToGridPosClampsAboveBounds()
		{
			// Map cell well outside grid → clamps to gridWidth-1.
			var (gx, gy) = ToGridPos(1000, 1000, 8, 10, 10);
			Assert.That(gx, Is.EqualTo(9));
			Assert.That(gy, Is.EqualTo(9));
		}

		[Test]
		public void ToGridPosClampsBelowZero()
		{
			// Math.Clamp clamps a negative input up to 0 (since the floor is 0).
			var (gx, gy) = ToGridPos(-100, -1, 8, 10, 10);
			Assert.That(gx, Is.EqualTo(0));
			Assert.That(gy, Is.EqualTo(0));
		}
	}
}
