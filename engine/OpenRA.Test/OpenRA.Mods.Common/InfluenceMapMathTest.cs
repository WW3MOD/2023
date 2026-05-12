#region Copyright & License Information
/*
 * WW3MOD InfluenceMap math tests — Stage A.1 of the doctrine roadmap.
 *
 * Pure-math tests of InfluenceMapMath helpers. Trait integration is tested
 * in-game via the debug overlay (Stage A.3+) and the tournament watcher.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class InfluenceMapMathTest
	{
		[Test]
		public void EmptyGrid_AllZeros()
		{
			var grid = new int[10, 10];
			Assert.That(grid[5, 5], Is.EqualTo(0));
			Assert.That(grid[0, 0], Is.EqualTo(0));
		}

		[Test]
		public void Contribution_AccumulatesAtCentre()
		{
			var grid = new int[10, 10];
			InfluenceMapMath.AddContribution(grid, 5, 5, 100, 3);
			Assert.That(grid[5, 5], Is.EqualTo(100), "centre cell gets full value");
		}

		[Test]
		public void Contribution_FallsOffByDistance()
		{
			var grid = new int[10, 10];
			InfluenceMapMath.AddContribution(grid, 5, 5, 100, 3);

			// Manhattan distance 1: contribution = 100 * (3 - 1 + 1) / (3 + 1) = 75
			Assert.That(grid[4, 5], Is.EqualTo(75));
			Assert.That(grid[5, 6], Is.EqualTo(75));

			// Manhattan distance 2: 100 * 2 / 4 = 50
			Assert.That(grid[3, 5], Is.EqualTo(50));

			// Manhattan distance 3 (edge): 100 * 1 / 4 = 25
			Assert.That(grid[2, 5], Is.EqualTo(25));
		}

		[Test]
		public void Contribution_ZeroBeyondRadius()
		{
			var grid = new int[20, 20];
			InfluenceMapMath.AddContribution(grid, 10, 10, 100, 3);

			// Manhattan distance 4 — beyond radius.
			Assert.That(grid[6, 10], Is.EqualTo(0));
			Assert.That(grid[10, 6], Is.EqualTo(0));
			Assert.That(grid[0, 0], Is.EqualTo(0));
		}

		[Test]
		public void Contribution_ClampedToGridBounds()
		{
			var grid = new int[5, 5];
			InfluenceMapMath.AddContribution(grid, 0, 0, 100, 3);

			Assert.That(grid[0, 0], Is.EqualTo(100), "centre still gets full value");
			Assert.That(grid[1, 0], Is.EqualTo(75));
			// (-1, 0) etc. should be silently skipped, not throw.
		}

		[Test]
		public void Contribution_TwoSources_Sum()
		{
			var grid = new int[10, 10];
			InfluenceMapMath.AddContribution(grid, 3, 5, 100, 3);
			InfluenceMapMath.AddContribution(grid, 7, 5, 100, 3);

			// Cell (5, 5) is Manhattan-2 from both sources, so each contributes 50.
			Assert.That(grid[5, 5], Is.EqualTo(100));
		}

		[Test]
		public void DeriveFrontline_EmptyGrids_NoFrontline()
		{
			var friendly = new int[5, 5];
			var enemy = new int[5, 5];
			var front = InfluenceMapMath.DeriveFrontline(friendly, enemy);

			Assert.That(InfluenceMapMath.CountFrontlineCells(front), Is.EqualTo(0));
		}

		[Test]
		public void DeriveFrontline_OnlyFriendly_NoFrontline()
		{
			var friendly = new int[5, 5];
			friendly[2, 2] = 50;
			var enemy = new int[5, 5];
			var front = InfluenceMapMath.DeriveFrontline(friendly, enemy);

			Assert.That(InfluenceMapMath.CountFrontlineCells(front), Is.EqualTo(0));
		}

		[Test]
		public void DeriveFrontline_OverlappingCells_AreFrontline()
		{
			var friendly = new int[10, 10];
			var enemy = new int[10, 10];

			// Friendly at (3,5), enemy at (7,5). With radius 3, their contributions overlap in (4..6, 5).
			InfluenceMapMath.AddContribution(friendly, 3, 5, 100, 3);
			InfluenceMapMath.AddContribution(enemy, 7, 5, 100, 3);

			var front = InfluenceMapMath.DeriveFrontline(friendly, enemy);

			// Cells between the two sources have both friendly AND enemy influence.
			Assert.That(front[5, 5], Is.True, "middle cell is frontline");
			Assert.That(front[4, 5], Is.True);
			Assert.That(front[6, 5], Is.True);

			// Cells right on top of the friendly unit shouldn't be frontline
			// (enemy doesn't reach there).
			Assert.That(front[3, 5], Is.False, "friendly origin: no enemy influence");
			Assert.That(front[7, 5], Is.False, "enemy origin: no friendly influence");
		}

		[Test]
		public void DeriveFrontline_DistantOpposingUnits_NoFrontline()
		{
			var friendly = new int[20, 20];
			var enemy = new int[20, 20];

			// Friendly at (3, 5), enemy at (17, 5). With radius 3 they don't overlap.
			InfluenceMapMath.AddContribution(friendly, 3, 5, 100, 3);
			InfluenceMapMath.AddContribution(enemy, 17, 5, 100, 3);

			var front = InfluenceMapMath.DeriveFrontline(friendly, enemy);
			Assert.That(InfluenceMapMath.CountFrontlineCells(front), Is.EqualTo(0));
		}
	}
}
