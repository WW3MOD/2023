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
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace OpenRA.Primitives
{
	/// <summary>
	/// Splits a band of map rows into contiguous chunks and runs a body over each of them, either on the
	/// calling thread or across the thread pool. The body is handed a half-open row range rather than a
	/// single row, so a chunk stays a contiguous span of whatever row-major array it is writing.
	/// </summary>
	/// <remarks>
	/// <para>This decides HOW the rows are divided and nothing else. Whether dividing them is SAFE is the
	/// caller's problem: the body must be free of cross-row shared state. See TerrainLighting.NotifyCells
	/// for the one caller today and the four-part argument it carries.</para>
	/// </remarks>
	public static class CellRowSweep
	{
		/// <summary>
		/// Below this many cells in the band, the parallel path is refused and the body runs inline.
		/// </summary>
		// A Parallel.ForEach dispatch costs on the order of 10-30 microseconds. The cheapest per-cell
		// sweep in the tree is a mod with a handful of TerrainSpriteLayers, at roughly 1.5 us per cell,
		// so 1024 cells (a 32x32 band) is already ~1.5 ms of work -- two orders of magnitude over the
		// dispatch. Anything smaller is a muzzle flash or a lamp post and belongs on the calling thread.
		public const int MinCellsForParallel = 1024;

		/// <summary>Chunks handed to the range partitioner per worker.</summary>
		// WHY OVERSUBSCRIBE RATHER THAN ONE CHUNK PER WORKER. Rows in a light's bounding box are not
		// equal work: NotifyCells rejects cells outside the light's RADIUS, so a row's cost is the chord
		// of that circle and the middle row does ~2r cells while the top and bottom rows do ~0. Split
		// into exactly one chunk per worker, the critical path is whichever worker drew the middle -- on
		// a full circular footprint that chunk carries about 1.3x the mean, capping efficiency near 77%.
		// Handing out four chunks each lets the partitioner give the idle workers the cheap edge chunks.
		// The cost is four memo arm/disarms per worker instead of one, which is a thread-static bool
		// store apiece.
		public const int ChunksPerWorker = 4;

		/// <summary>Rows per chunk for a band of <paramref name="rows"/> rows across <paramref name="workers"/> workers.</summary>
		public static int ChunkRows(int rows, int workers)
		{
			if (rows <= 0)
				return 1;

			var chunks = Math.Max(1, workers * ChunksPerWorker);
			return Math.Max(1, Exts.IntegerDivisionRoundingAwayFromZero(rows, chunks));
		}

		/// <summary>Is a band of this shape worth dispatching to the thread pool at all?</summary>
		public static bool ShouldParallelize(int rows, int cols)
		{
			return rows > 1 && cols > 0 && (long)rows * cols >= MinCellsForParallel;
		}

		/// <summary>
		/// Runs <paramref name="body"/> over [firstRow, lastRowExclusive) in contiguous chunks. Every row in
		/// the band is covered exactly once whether or not the parallel path is taken.
		/// </summary>
		public static void Run(int firstRow, int lastRowExclusive, int cols, bool allowParallel, Action<int, int> body)
		{
			var rows = lastRowExclusive - firstRow;
			if (rows <= 0)
				return;

			if (!allowParallel || !ShouldParallelize(rows, cols))
			{
				body(firstRow, lastRowExclusive);
				return;
			}

			var chunkRows = ChunkRows(rows, Environment.ProcessorCount);
			Parallel.ForEach(
				Partitioner.Create(firstRow, lastRowExclusive, chunkRows),
				range => body(range.Item1, range.Item2));
		}
	}
}
