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
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	/// <summary>
	/// MapShadowLayer replaced CellLayer&lt;CellLayer&lt;(byte, byte)&gt;&gt; as the backing store for
	/// Map.ShadowLayer. Shadow feeds vision attenuation (MapLayers) and firing LOS (FiringLOS), both of
	/// which are simulation inputs, so the bar is not "close enough" — every query must return exactly
	/// what the nested CellLayer returned, on every machine. These tests build both representations,
	/// fill them through the same annulus enumeration the map loader uses, and compare every ordered
	/// pair of cells on a real map footprint.
	/// </summary>
	[TestFixture]
	public class MapShadowLayerTest
	{
		// river-zeta-ww3. The largest shipped map (woodland-warfare, 98x98) has the same geometry.
		const int Width = 98;
		const int Height = 82;

		const MapGridType Grid = MapGridType.Rectangular;

		/// <summary>
		/// The offsets Map.FindTilesInAnnulus(centre, 2, 32, true) can yield, before clipping to the map.
		/// MapGrid.CreateTilesByDistance buckets a CVec into TilesByDistance[ceil(sqrt(x^2 + y^2))], so
		/// asking for buckets 2..32 is asking for exactly these offsets — derived here from the same
		/// Exts.ISqrt primitive rather than restated as a disc, so the test cannot silently agree with a
		/// wrong assumption baked into MapShadowLayer.
		/// </summary>
		static IEnumerable<CVec> AnnulusOffsets()
		{
			for (var dv = -MapShadowLayer.MaxRange; dv <= MapShadowLayer.MaxRange; dv++)
			{
				for (var du = -MapShadowLayer.MaxRange; du <= MapShadowLayer.MaxRange; du++)
				{
					var distance = Exts.ISqrt((du * du) + (dv * dv), Exts.ISqrtRoundMode.Ceiling);
					if (distance >= MapShadowLayer.MinRange && distance <= MapShadowLayer.MaxRange)
						yield return new CVec(du, dv);
				}
			}
		}

		// Never zero, so a zero read is unambiguous evidence of an unwritten (out-of-annulus) pair.
		static (byte GroundShadow, byte AirborneShadow) Value(int fromIndex, int toIndex)
		{
			var ground = (byte)(1 + (((fromIndex * 31) + (toIndex * 17)) % 255));
			var airborne = (byte)(1 + (((fromIndex * 7) + (toIndex * 13)) % 255));
			return (ground, airborne);
		}

		[Test(Description = "Every offset the annulus can produce lands inside the window MapShadowLayer stores.")]
		public void AnnulusFitsTheStoredWindow()
		{
			var layer = new MapShadowLayer(Grid, new Size(Width, Height));
			var from = new MPos(Width / 2, Height / 2);
			var written = 0;

			foreach (var offset in AnnulusOffsets())
			{
				var to = new MPos(from.U + offset.X, from.V + offset.Y);
				if (!layer.Contains(to))
					continue;

				// Throws if the offset falls outside the stored window — i.e. if the annulus is wider
				// than MapShadowLayer believes and a real shadow value would have been dropped.
				layer[from, to] = (7, 9);
				Assert.That(layer[from, to], Is.EqualTo(((byte)7, (byte)9)));
				written++;
			}

			Assert.That(written, Is.GreaterThan(2000), "annulus enumeration produced implausibly few cells");

			// The point of the change: per-cell storage is a function of MaxRange alone, not of map
			// size. The nested CellLayer gave every cell a full-map array, so cost grew as cells^2.
			var smaller = new MapShadowLayer(Grid, new Size(40, 40));
			Assert.That(smaller.SlotsPerCell, Is.EqualTo(layer.SlotsPerCell));
			Assert.That(layer.SlotsPerCell, Is.LessThan(Width * Height),
				"the stored window must be smaller than a full-map array, or this is not a saving");
		}

		[Test(Description = "For a real map footprint, every from/to pair reads identically from the sparse " +
			"store and from the CellLayer<CellLayer<>> it replaces — including pairs outside the annulus.")]
		public void ByteIdenticalToNestedCellLayerForEveryPair()
		{
			var size = new Size(Width, Height);
			var cells = Width * Height;

			var dense = new CellLayer<CellLayer<(byte GroundShadow, byte AirborneShadow)>>(Grid, size);
			var sparse = new MapShadowLayer(Grid, size);

			var offsets = new List<CVec>(AnnulusOffsets());

			// Fill both through the same enumeration and clipping rule the map loader uses:
			// FindTilesInAnnulus(from, 2, 32, allowOutsideBounds: true) keeps every offset cell that is
			// still on the map (Tiles.Contains), which for a rectangular grid is exactly Contains(MPos).
			for (var v = 0; v < Height; v++)
			{
				for (var u = 0; u < Width; u++)
				{
					var from = new MPos(u, v);
					dense[from] = new CellLayer<(byte GroundShadow, byte AirborneShadow)>(Grid, size);

					foreach (var offset in offsets)
					{
						var to = new MPos(u + offset.X, v + offset.Y);
						if (to.U < 0 || to.U >= Width || to.V < 0 || to.V >= Height)
							continue;

						var value = Value((v * Width) + u, (to.V * Width) + to.U);
						dense[from][to] = value;
						sparse[from, to] = value;
					}
				}
			}

			var compared = 0L;
			var nonZero = 0L;
			for (var fromIndex = 0; fromIndex < cells; fromIndex++)
			{
				var from = new MPos(fromIndex % Width, fromIndex / Width);
				var denseRow = dense[from];

				for (var toIndex = 0; toIndex < cells; toIndex++)
				{
					var to = new MPos(toIndex % Width, toIndex / Width);
					var expected = denseRow[to];
					var actual = sparse[from, to];

					if (expected != actual)
						Assert.Fail($"mismatch at from {from} to {to}: expected {expected}, got {actual}");

					compared++;
					if (expected.GroundShadow != 0)
						nonZero++;
				}
			}

			Assert.That(compared, Is.EqualTo((long)cells * cells));

			// Sanity on the fixture itself: if the fill had silently done nothing, every pair would have
			// compared equal at (0, 0) and the test above would have passed while proving nothing.
			Assert.That(nonZero, Is.GreaterThan((long)cells * 1000), "the reference layer was barely populated");
		}

		[Test(Description = "Pairs outside the annulus read (0, 0) — the default the dense array returned " +
			"because nothing ever wrote there. Code downstream treats that as 'no obstacles known'.")]
		public void OutsideTheAnnulusReadsZero()
		{
			var layer = new MapShadowLayer(Grid, new Size(Width, Height));
			var from = new MPos(48, 40);

			foreach (var offset in AnnulusOffsets())
				layer[from, new MPos(from.U + offset.X, from.V + offset.Y)] = (5, 6);

			// Inside the inner hole (distance < MinRange).
			Assert.That(layer[from, from], Is.EqualTo(((byte)0, (byte)0)));
			Assert.That(layer[from, new MPos(from.U + 1, from.V)], Is.EqualTo(((byte)0, (byte)0)));

			// Beyond MaxRange, both on the axis and diagonally.
			Assert.That(layer[from, new MPos(from.U + 33, from.V)], Is.EqualTo(((byte)0, (byte)0)));
			Assert.That(layer[from, new MPos(from.U + 24, from.V + 24)], Is.EqualTo(((byte)0, (byte)0)));

			// And a pair that is inside is not zero, so the assertions above are not vacuous.
			Assert.That(layer[from, new MPos(from.U + 30, from.V)], Is.EqualTo(((byte)5, (byte)6)));
		}

		[Test(Description = "An MPos outside the map throws, exactly as indexing CellLayer.Entries did.")]
		public void OutOfMapIndexThrows()
		{
			var layer = new MapShadowLayer(Grid, new Size(Width, Height));
			var from = new MPos(10, 10);

			Assert.Throws<IndexOutOfRangeException>(() => _ = layer[from, new MPos(0, Height)]);
			Assert.Throws<IndexOutOfRangeException>(() => _ = layer[from, new MPos(-1, -1)]);
			Assert.Throws<IndexOutOfRangeException>(() => _ = layer[new MPos(0, Height), from]);
		}

		[Test(Description = "Two MPos that resolve to the same linear cell index alias onto the same entry, " +
			"as they did when CellLayer stored them by that index.")]
		public void OutOfColumnIndexAliasesLikeCellLayer()
		{
			var size = new Size(Width, Height);
			var dense = new CellLayer<(byte GroundShadow, byte AirborneShadow)>(Grid, size);
			var sparse = new MapShadowLayer(Grid, size);
			var from = new MPos(4, 4);

			// U == Width is off the right edge but its linear index is the first cell of the next row.
			var overflowed = new MPos(Width, 4);
			var aliased = new MPos(0, 5);

			dense[aliased] = (3, 4);
			sparse[from, aliased] = (3, 4);

			Assert.That(dense[overflowed], Is.EqualTo(((byte)3, (byte)4)));
			Assert.That(sparse[from, overflowed], Is.EqualTo(((byte)3, (byte)4)));
		}
	}
}
