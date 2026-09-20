#region Copyright & License Information
/*
 * WW3MOD DEFCON-wall zone-source tests.
 *
 * A map may now paint its DEFCON 3 border in the editor -- map.yaml's `Zones: DMZ` -- instead of
 * hand-authoring DefconWallInfo.RegionCells in its rules.yaml. DefconWall.BuildRegion unions the
 * three sources; DefconWall.UsesRegion decides whether the region path is taken at all.
 *
 * TWO THINGS ARE PINNED HERE, AND THE SECOND ONE IS THE POINT.
 *
 *   1. A PAINTED DMZ SELECTS THE REGION PATH. Without this the zone is drawn in the editor, saved
 *      into the package, and ignored at runtime while the wall falls back to the derived bisector --
 *      which looks like a working feature right up until someone measures where the border is.
 *
 *   2. AN EMPTY `Zones:` CHANGES NOTHING. Every map that predates this tool -- nine shipped maps
 *      before the migration, every autotest scenario, every map any player has -- has no zone at
 *      all, and their behaviour has to be bit-for-bit what it was. That is a statement about ONE
 *      expression, so it is checked exhaustively against the expression it replaced rather than
 *      argued about: for a zero-length zone, UsesRegion must equal the old
 *      `RegionTerrainTypes.Length > 0 || RegionCells.Length > 0` for every input.
 *
 * WHY THE SEPARATION HALF GOES THROUGH DefconWallRegion RATHER THAN THE TRAIT. Nothing in
 * OpenRA.Test can construct a World, and DefconWallRegion is deliberately World-free for exactly
 * that reason (see its file header). It is also the SAME object BuildRegion builds and the same one
 * the editor's split readout queries, so a band that reads as two components here reads as two
 * components in all three places.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DefconWallZoneTest
	{
		const int Size = 20;

		/// <summary>The legacy predicate, verbatim, as it stood before zones existed.</summary>
		static bool LegacyUsesRegion(int regionTerrainTypes, int regionCells)
		{
			return regionTerrainTypes > 0 || regionCells > 0;
		}

		static DefconWallRegion RegionOf(IEnumerable<CPos> blocked)
		{
			// Same shape as DefconWall.BuildRegion: the map's Bounds, and Map.Contains as the only
			// passability test.
			return new DefconWallRegion(0, 0, Size, Size, blocked,
				c => c.X >= 0 && c.X < Size && c.Y >= 0 && c.Y < Size);
		}

		/// <summary>A three-cell-wide vertical band straight down the middle, edge to edge.</summary>
		static List<CPos> DmzBand()
		{
			var cells = new List<CPos>();
			for (var y = 0; y < Size; y++)
				for (var x = 9; x <= 11; x++)
					cells.Add(new CPos(x, y));

			return cells;
		}

		[Test]
		public void APaintedDmzSelectsTheRegionPath()
		{
			Assert.That(DefconWall.UsesRegion(dmzZoneCells: 399, regionTerrainTypes: 0, regionCells: 0), Is.True,
				"a map whose only border is a painted DMZ fell through to the line");
		}

		[Test]
		public void AnEmptyZonesNodeLeavesThePrecedenceExactlyAsItWas()
		{
			for (var types = 0; types <= 3; types++)
				for (var cells = 0; cells <= 3; cells++)
					Assert.That(DefconWall.UsesRegion(0, types, cells), Is.EqualTo(LegacyUsesRegion(types, cells)),
						$"a map with no zone changed behaviour at RegionTerrainTypes={types}, RegionCells={cells}");
		}

		[Test]
		public void AMapWithNoBorderSourceAtAllStillUsesTheLine()
		{
			Assert.That(DefconWall.UsesRegion(0, 0, 0), Is.False);
		}

		[Test]
		public void ThePaintedZoneDoesNotSuppressTheOtherTwoSources()
		{
			// river-zeta-ww3 keeps RegionTerrainTypes after the migration; the sources are unioned,
			// so every combination has to select the region path rather than one winning.
			Assert.That(DefconWall.UsesRegion(10, 3, 0), Is.True);
			Assert.That(DefconWall.UsesRegion(10, 0, 14), Is.True);
			Assert.That(DefconWall.UsesRegion(0, 3, 14), Is.True);
		}

		[Test]
		public void ABandFromEdgeToEdgeSplitsTheMapInTwo()
		{
			var region = RegionOf(DmzBand());

			Assert.That(region.ComponentCount, Is.EqualTo(2));
			Assert.That(region.IsDegenerate, Is.False);
		}

		[Test]
		public void ABandWithAGapDividesNothingAndIsDiscarded()
		{
			// The failure the editor's split readout exists to catch: a border that is drawn, is
			// visible, and leaves the map in one piece. DefconWall throws this away and the wall
			// stays down for the whole match.
			var band = DmzBand();
			band.RemoveAll(c => c.Y == 10);

			var region = RegionOf(band);

			Assert.That(region.ComponentCount, Is.EqualTo(1));
			Assert.That(region.IsDegenerate, Is.True);
		}

		[Test]
		public void AOneCellDiagonalLeaksThroughItsOwnCorners()
		{
			// The band-thickness rule from DefconWallInfo.RegionCells, pinned: 8-connected steps go
			// straight between corner-touching cells, so a hand-painted one-cell diagonal separates
			// nothing. This is what a mapper will draw first with brush size 1.
			var diagonal = new List<CPos>();
			for (var i = 0; i < Size; i++)
				diagonal.Add(new CPos(i, i));

			Assert.That(RegionOf(diagonal).ComponentCount, Is.EqualTo(1),
				"a one-cell diagonal sealed the map, which would make the brush-size guidance wrong");
		}

		[Test]
		public void TheUnionOfTwoSourcesSeparatesWhereNeitherAloneDoes()
		{
			// The shape the migration relies on: a painted band that stops short of the edge, closed
			// by cells from another source. Neither half divides the map; together they do.
			var painted = new List<CPos>();
			for (var y = 0; y < Size - 2; y++)
				for (var x = 9; x <= 11; x++)
					painted.Add(new CPos(x, y));

			var closing = new List<CPos>();
			for (var y = Size - 2; y < Size; y++)
				for (var x = 9; x <= 11; x++)
					closing.Add(new CPos(x, y));

			Assert.That(RegionOf(painted).ComponentCount, Is.EqualTo(1));
			Assert.That(RegionOf(closing).ComponentCount, Is.EqualTo(1));

			var union = new List<CPos>(painted);
			union.AddRange(closing);
			Assert.That(RegionOf(union).ComponentCount, Is.EqualTo(2));
		}

		[Test]
		public void CellsRepeatedAcrossSourcesAreOneBorderCell()
		{
			// BuildRegion concatenates its three sources without de-duplicating, on the grounds that
			// DefconWallRegion takes a SET. Pinned, because that is the assumption being made.
			var band = DmzBand();
			var doubled = new List<CPos>(band);
			doubled.AddRange(band);

			var once = RegionOf(band);
			var twice = RegionOf(doubled);

			Assert.That(twice.BlockedCells.Count, Is.EqualTo(once.BlockedCells.Count));
			Assert.That(twice.ComponentCount, Is.EqualTo(once.ComponentCount));
		}
	}
}
