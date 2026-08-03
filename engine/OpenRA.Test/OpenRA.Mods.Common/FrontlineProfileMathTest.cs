#region Copyright & License Information
/*
 * WW3MOD frontline-profile math test — frontline-influence Phase 4.
 *
 * Pins the pure per-frontier-sector strength profile + avenue mapping on a synthetic River-Zeta-shaped
 * fixture (mirroring CrossingMapMathTest's style):
 *   - a river runs east-west: the enemy holds the NORTH bank, we hold the SOUTH ⇒ a frontier edge in
 *     every column, split into 3 equal-width vertical sectors (west flank / centre / east flank);
 *   - believed ENEMY strength is THIN on the west flank and heavy in the centre + east ⇒ the west flank
 *     is the weakest (min-strength) enemy sector;
 *   - 4 crossings (1 west flank, 2 central, 1 east flank) map to their sectors, so the avenue serving the
 *     weakest sector is the WEST FLANK crossing — the Phase-4 acceptance case.
 *
 * Pins: sector-partition determinism + banding, own/enemy accumulation, weakest-enemy-sector selection
 * incl. deterministic tie-break + front-only gating, avenue→sector association, and the disabled-path no-op.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FrontlineProfileMathTest
	{
		const int Width = 6;
		const int Height = 4;
		const int Sectors = 3;   // sector 0 = cols {0,1}, sector 1 = {2,3}, sector 2 = {4,5}
		const int GrayBand = 150;
		const int CellSize = 2;  // map cell X / CellSize → grid column

		// Score grid: enemy holds the NORTH bank (rows 0-1, −500), we hold the SOUTH (rows 2-3, +500) ⇒
		// exactly one vertical frontier edge per column (between row 1 and row 2).
		static int[,] ScoreGrid()
		{
			var score = new int[Width, Height];
			for (var gx = 0; gx < Width; gx++)
			{
				score[gx, 0] = -500;
				score[gx, 1] = -500;
				score[gx, 2] = 500;
				score[gx, 3] = 500;
			}

			return score;
		}

		// Believed ENEMY strength (unit counts) on the north bank: THIN on the west flank (sector 0 = 1),
		// heavy in the centre (sector 1 = 10) and east (sector 2 = 10).
		static int[,] EnemyStrengthGrid()
		{
			var e = new int[Width, Height];
			e[0, 0] = 1;             // sector 0 → 1
			e[2, 0] = 6; e[3, 1] = 4; // sector 1 → 10
			e[4, 0] = 5; e[5, 1] = 5; // sector 2 → 10
			return e;
		}

		// Own strength on the south bank.
		static int[,] OwnStrengthGrid()
		{
			var o = new int[Width, Height];
			o[0, 2] = 3; o[1, 3] = 2; // sector 0 → 5
			o[2, 2] = 4;              // sector 1 → 4
			o[5, 3] = 7;              // sector 2 → 7
			return o;
		}

		// 4 crossings by MAP cell X: west flank (1 → col 0 → sector 0), 2 central (5,6 → cols 2,3 → sector 1),
		// east flank (11 → col 5 → sector 2).
		static readonly int[] AvenueMapCellX = { 1, 5, 6, 11 };

		[Test]
		public void SectorPartitionIsDeterministicAndBanded()
		{
			Assert.Multiple(() =>
			{
				// Equal-width bands: 2 columns per sector on a width-6 / 3-sector grid.
				Assert.That(FrontlineProfileMath.SectorOfColumn(0, Width, Sectors), Is.EqualTo(0));
				Assert.That(FrontlineProfileMath.SectorOfColumn(1, Width, Sectors), Is.EqualTo(0));
				Assert.That(FrontlineProfileMath.SectorOfColumn(2, Width, Sectors), Is.EqualTo(1));
				Assert.That(FrontlineProfileMath.SectorOfColumn(3, Width, Sectors), Is.EqualTo(1));
				Assert.That(FrontlineProfileMath.SectorOfColumn(4, Width, Sectors), Is.EqualTo(2));
				Assert.That(FrontlineProfileMath.SectorOfColumn(5, Width, Sectors), Is.EqualTo(2));

				// Out-of-range columns clamp into the end bands (never out of [0, sectorCount)).
				Assert.That(FrontlineProfileMath.SectorOfColumn(-3, Width, Sectors), Is.EqualTo(0), "negative clamps to first");
				Assert.That(FrontlineProfileMath.SectorOfColumn(99, Width, Sectors), Is.EqualTo(Sectors - 1), "over clamps to last");

				// A single sector swallows everything; monotone in gx.
				Assert.That(FrontlineProfileMath.SectorOfColumn(4, Width, 1), Is.EqualTo(0), "one sector");

				// Map-cell X buckets via CellSize: mapX 1 → col 0 → sector 0; mapX 11 → col 5 → sector 2.
				Assert.That(FrontlineProfileMath.SectorOfMapCellX(1, CellSize, Width, Sectors), Is.EqualTo(0));
				Assert.That(FrontlineProfileMath.SectorOfMapCellX(5, CellSize, Width, Sectors), Is.EqualTo(1));
				Assert.That(FrontlineProfileMath.SectorOfMapCellX(11, CellSize, Width, Sectors), Is.EqualTo(2));
			});
		}

		[Test]
		public void AccumulationTotalsPerSector()
		{
			var own = new int[Sectors];
			var enemy = new int[Sectors];
			var edges = new int[Sectors];

			FrontlineProfileMath.Accumulate(ScoreGrid(), OwnStrengthGrid(), EnemyStrengthGrid(),
				Width, Height, GrayBand, Sectors, own, enemy, edges);

			Assert.Multiple(() =>
			{
				Assert.That(own, Is.EqualTo(new[] { 5, 4, 7 }), "own totals per sector");
				Assert.That(enemy, Is.EqualTo(new[] { 1, 10, 10 }), "enemy totals per sector — west flank is thin");

				// One vertical frontier edge per column ⇒ 2 per sector (2 columns each).
				Assert.That(edges, Is.EqualTo(new[] { 2, 2, 2 }), "every sector is on the front");
			});
		}

		[Test]
		public void AccumulationClearsOutputArrays()
		{
			// Pre-dirtied outputs must be reset, not added to (the trait reuses the arrays each recompute).
			var own = new[] { 111, 222, 333 };
			var enemy = new[] { 444, 555, 666 };
			var edges = new[] { 7, 8, 9 };

			FrontlineProfileMath.Accumulate(ScoreGrid(), OwnStrengthGrid(), EnemyStrengthGrid(),
				Width, Height, GrayBand, Sectors, own, enemy, edges);

			Assert.Multiple(() =>
			{
				Assert.That(own, Is.EqualTo(new[] { 5, 4, 7 }), "own totals overwrite prior contents");
				Assert.That(enemy, Is.EqualTo(new[] { 1, 10, 10 }));
				Assert.That(edges, Is.EqualTo(new[] { 2, 2, 2 }));
			});
		}

		[Test]
		public void WeakestEnemySectorIsTheThinFlank()
		{
			var enemy = new[] { 1, 10, 10 };
			var edges = new[] { 2, 2, 2 };
			Assert.That(FrontlineProfileMath.WeakestEnemySector(enemy, edges, Sectors), Is.EqualTo(0),
				"the thin west flank is the weakest enemy sector");
		}

		[Test]
		public void WeakestEnemySectorTieBreaksToLowestIndex()
		{
			// Two sectors share the minimum enemy strength — the lower index wins (deterministic).
			var enemy = new[] { 5, 5, 9 };
			var edges = new[] { 2, 2, 2 };
			Assert.That(FrontlineProfileMath.WeakestEnemySector(enemy, edges, Sectors), Is.EqualTo(0),
				"tie resolves to the lowest sector index");
		}

		[Test]
		public void WeakestEnemySectorSkipsSectorsNotOnTheFront()
		{
			// Sector 0 has the least enemy strength but NO frontier edge ⇒ it is not on the line and is
			// skipped; the weakest FRONT sector is sector 1.
			var enemy = new[] { 0, 4, 9 };
			var edges = new[] { 0, 2, 2 };
			Assert.That(FrontlineProfileMath.WeakestEnemySector(enemy, edges, Sectors), Is.EqualTo(1),
				"a sector with no frontier edge is not a push candidate");
		}

		[Test]
		public void AvenueAssociationNamesTheFlankCrossing()
		{
			// End-to-end acceptance: accumulate → pick the weakest sector → name its avenue.
			var own = new int[Sectors];
			var enemy = new int[Sectors];
			var edges = new int[Sectors];
			FrontlineProfileMath.Accumulate(ScoreGrid(), OwnStrengthGrid(), EnemyStrengthGrid(),
				Width, Height, GrayBand, Sectors, own, enemy, edges);

			var weakest = FrontlineProfileMath.WeakestEnemySector(enemy, edges, Sectors);
			var serving = FrontlineProfileMath.AvenueIndicesForSector(AvenueMapCellX, CellSize, Width, Sectors, weakest);

			Assert.Multiple(() =>
			{
				Assert.That(weakest, Is.EqualTo(0), "weakest sector = west flank");
				Assert.That(serving, Is.EqualTo(new List<int> { 0 }), "only the west-flank crossing (index 0) serves it");

				// The two central crossings both serve the centre sector; the east flank serves sector 2.
				Assert.That(FrontlineProfileMath.AvenueIndicesForSector(AvenueMapCellX, CellSize, Width, Sectors, 1),
					Is.EqualTo(new List<int> { 1, 2 }), "both central crossings serve the centre");
				Assert.That(FrontlineProfileMath.AvenueIndicesForSector(AvenueMapCellX, CellSize, Width, Sectors, 2),
					Is.EqualTo(new List<int> { 3 }), "east-flank crossing serves the east sector");
			});
		}

		[Test]
		public void DisabledPathIsNoOp()
		{
			Assert.Multiple(() =>
			{
				// No frontier anywhere (the un-consumed / empty profile) ⇒ no push candidate.
				var enemy = new[] { 3, 1, 4 };
				var noEdges = new[] { 0, 0, 0 };
				Assert.That(FrontlineProfileMath.WeakestEnemySector(enemy, noEdges, Sectors),
					Is.EqualTo(FrontlineProfileMath.NoSector), "no frontier ⇒ no weakest sector (−1)");

				// A −1 sector selects no avenues; a null avenue list is tolerated.
				Assert.That(FrontlineProfileMath.AvenueIndicesForSector(AvenueMapCellX, CellSize, Width, Sectors,
					FrontlineProfileMath.NoSector), Is.Empty, "no sector ⇒ no avenue");
				Assert.That(FrontlineProfileMath.AvenueIndicesForSector(null, CellSize, Width, Sectors, 0),
					Is.Empty, "null avenue input ⇒ empty, not a throw");
			});
		}
	}
}
