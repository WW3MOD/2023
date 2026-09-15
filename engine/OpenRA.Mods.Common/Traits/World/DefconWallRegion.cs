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
 * The DEFCON 3 dividing border as an arbitrary SET OF CELLS, with no dependency on Actor, World or
 * Map -- the same rule DefconWallGeometry follows and for the same reason: nothing in OpenRA.Test
 * can construct a World, so a predicate that lives inside a trait method is a predicate verified by
 * reading only.
 *
 * WHY THIS EXISTS AT ALL. DefconWallGeometry is an infinite straight line, and on a map whose
 * natural division is a river it draws a border that ignores the river. The line is still the right
 * answer on an open map and is NOT replaced here -- see the precedence note on DefconWallInfo. This
 * class is the other shape: a border authored as terrain ("every water, river and bridge cell") plus
 * hand-drawn additions, which is a set and not a function of position.
 *
 * ---------------------------------------------------------------------------------------------
 * A REGION HAS NO SIDE, AND THAT IS THE WHOLE DIFFICULTY.
 *
 * SideOf on a line is a sign test on a cross product: total, cheap, and defined for every point in
 * the plane including points off the map. A region has no such function. What it has instead is
 * CONNECTIVITY: with the blocked cells removed, the remaining land falls into connected components,
 * and "same side" means "same component". That is a strictly better notion for the purpose the sides
 * are actually used for -- it answers "can this unit walk there without crossing the border", which
 * is the question every consumer is really asking -- but it is only defined ON the map, and it has
 * to be computed once rather than evaluated per call.
 *
 * So the sides here are COMPONENT IDS, not the line's -1/0/+1. They are opaque integers; nothing may
 * assume two of them are negatives of each other, and NoSide (0 on the line) is Unlabelled (-1) here.
 * Callers that only ever compare two side values for equality -- which is all of them, see below --
 * are unaffected by that change.
 *
 * DEPTH AND THE NORMAL, which are the two genuinely line-shaped questions.
 *
 * DefconWallTurnBack needs "how far past the border is this airframe" (to react BEFORE the border is
 * reached, via a negative margin) and "which way is home" (to fly there). Neither is a property of a
 * region as such. Both ARE properties of a cell relative to the region, and this class computes them
 * from one multi-source BFS distance transform out of the blocked set:
 *
 *   DEPTH   = -(distance to the nearest blocked cell)   on your own component
 *           = +(distance to the nearest blocked cell)   on any other component
 *           = 0                                         inside the border itself
 *
 *     which reproduces the line's sign convention exactly (negative at home, positive beyond, zero
 *     on the border) and its units (world units), and degrades to the line's own answer when the
 *     region happens to be a straight band. It is NOT the perpendicular distance to a line, because
 *     there is no line; it is the distance to the border, which is what the line's version also was.
 *
 *   NORMAL  = the direction from this cell toward the nearest cell of the target component, taken
 *             from the SAME transform run once per component.
 *
 * THIS IS A DELIBERATE CHOICE OVER FAILING LOUDLY, and the reasoning is that the alternative is
 * worse in the only situation that matters. DefconWallTurnBack ticks on live airframes mid-match; a
 * throw there is a crash in the middle of a game, and a "do nothing" degradation is an aircraft that
 * flies across a border the readout says is closed. A distance transform is a real answer to a real
 * question, computed with the same integer arithmetic as everything else here. What it is NOT is the
 * line's answer, and the one caller that could tell the difference is documented at its call site.
 *
 * COST AND WHEN IT IS PAID. One BFS over the map per component plus one over the blocked set, at
 * WorldLoaded, on a map of at most 256x256. Every query afterwards is an array index. Nothing here
 * allocates or iterates per tick, which is the property Aircraft.SetPosition needs -- it is on the
 * hot path for every airframe.
 *
 * NO FLOATING POINT, NO RNG, and every loop walks a deterministic order, so every client computes
 * byte-identical labels. Same invariant as DefconWallGeometry and for the same reason.
 */

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// The DEFCON 3 border as a free-form set of cells, with connectivity-based sides. See the file
	/// header for why sides are components here and what depth and the normal mean for a region.
	/// </summary>
	public class DefconWallRegion
	{
		/// <summary>
		/// The side value for a cell that belongs to no component: off-map, or inside the border
		/// itself. The region analogue of <see cref="DefconWallGeometry.NoSide"/>, and deliberately
		/// NOT 0 -- component ids start at 0, so sharing the sentinel would make the first component
		/// indistinguishable from "no side at all".
		/// </summary>
		public const int Unlabelled = -1;

		/// <summary>One cell in world units. The border is authored in cells and queried in world units.</summary>
		public const int CellUnits = 1024;

		readonly int left, top, width, height;

		// Per-cell component id, or Unlabelled for a blocked or off-map cell. Row-major, bounds-local.
		readonly int[] labels;

		// Per-cell distance IN CELLS to the nearest blocked cell, from one multi-source BFS seeded
		// with the whole blocked set. Zero inside the border. int.MaxValue when the region is empty
		// or nothing is reachable, which only happens on a degenerate region.
		readonly int[] depthCells;

		// Per-component: distance IN CELLS to the nearest cell OF THAT COMPONENT, again by multi-source
		// BFS. This is what makes NormalTowards answerable -- the direction home is the descent
		// direction of the target component's own transform. componentDistance[c][i] for component c.
		readonly int[][] componentDistance;

		/// <summary>The cells the border occupies. These are impassable to everyone, exactly as the line's band is.</summary>
		public readonly IReadOnlyCollection<CPos> BlockedCells;

		/// <summary>How many connected components the non-blocked land falls into. Two is the shape a border wants.</summary>
		public readonly int ComponentCount;

		/// <summary>
		/// True when this region divides nothing -- no blocked cells at all, or fewer than two
		/// components. Consumers must treat it exactly as <see cref="DefconWallGeometry.IsDegenerate"/>:
		/// there is no border. A region that leaves the map in one piece is the region equivalent of
		/// two coincident spawns, and the same ruling applies -- no border is visibly wrong and
		/// therefore fixable, a border that divides nothing while claiming to is not.
		/// </summary>
		public bool IsDegenerate => BlockedCells.Count == 0 || ComponentCount < 2;

		/// <summary>
		/// Build the region from a blocked cell set and the map's bounds. <paramref name="passable"/>
		/// reports whether a cell is land the components may spread through at all -- cliffs and water
		/// that are NOT part of the authored border still break connectivity, and a region that
		/// ignored them would claim a separation the terrain already provides or denies.
		/// </summary>
		public DefconWallRegion(int left, int top, int width, int height,
			IEnumerable<CPos> blocked, Func<CPos, bool> passable)
		{
			if (blocked == null)
				throw new ArgumentNullException(nameof(blocked));

			if (passable == null)
				throw new ArgumentNullException(nameof(passable));

			this.left = left;
			this.top = top;
			this.width = Math.Max(0, width);
			this.height = Math.Max(0, height);

			var count = this.width * this.height;
			labels = new int[count];
			depthCells = new int[count];

			var blockedSet = new List<CPos>();
			var isBlocked = new bool[count];
			foreach (var cell in blocked)
			{
				var i = IndexOf(cell);
				if (i < 0 || isBlocked[i])
					continue;

				isBlocked[i] = true;
				blockedSet.Add(cell);
			}

			// Sorted so the collection is in a deterministic order for anything that enumerates it
			// (the renderer does). The labelling below does not depend on it -- it walks the grid --
			// but an unordered set leaking into a render order would be a needless nondeterminism.
			blockedSet.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
			BlockedCells = blockedSet;

			ComponentCount = Label(isBlocked, passable);
			ComputeDepth(isBlocked);

			componentDistance = new int[ComponentCount][];
			for (var c = 0; c < ComponentCount; c++)
				componentDistance[c] = DistanceToComponent(c);
		}

		int IndexOf(CPos cell)
		{
			var x = cell.X - left;
			var y = cell.Y - top;
			if (x < 0 || y < 0 || x >= width || y >= height)
				return -1;

			return (y * width) + x;
		}

		/// <summary>
		/// Flood the non-blocked, passable land into connected components. 8-CONNECTED, which is the
		/// connectivity a ground unit actually moves on -- a 4-connected labelling would report a
		/// diagonal staircase of blocked cells as sealing the map when a unit walks straight through
		/// its corners. That is precisely the one-cell-band leak this border already shipped once; see
		/// DefconWallInfo.HalfWidth and DISCOVERIES 2026-09-10.
		/// </summary>
		int Label(bool[] isBlocked, Func<CPos, bool> passable)
		{
			for (var i = 0; i < labels.Length; i++)
				labels[i] = Unlabelled;

			var next = 0;
			var queue = new Queue<int>();

			for (var start = 0; start < labels.Length; start++)
			{
				if (isBlocked[start] || labels[start] != Unlabelled || !passable(CellAt(start)))
					continue;

				var id = next++;
				labels[start] = id;
				queue.Enqueue(start);

				while (queue.Count > 0)
				{
					var current = queue.Dequeue();
					var cy = current / width;
					var cx = current - (cy * width);

					for (var dy = -1; dy <= 1; dy++)
					{
						for (var dx = -1; dx <= 1; dx++)
						{
							if (dx == 0 && dy == 0)
								continue;

							var nx = cx + dx;
							var ny = cy + dy;
							if (nx < 0 || ny < 0 || nx >= width || ny >= height)
								continue;

							var n = (ny * width) + nx;
							if (isBlocked[n] || labels[n] != Unlabelled || !passable(CellAt(n)))
								continue;

							labels[n] = id;
							queue.Enqueue(n);
						}
					}
				}
			}

			return next;
		}

		/// <summary>
		/// Multi-source BFS out of the whole blocked set: distance in cells from every cell to the
		/// nearest border cell. This is the magnitude half of <see cref="DepthBeyond"/>; the sign
		/// comes from the component comparison.
		/// </summary>
		void ComputeDepth(bool[] isBlocked)
		{
			var queue = new Queue<int>();
			for (var i = 0; i < depthCells.Length; i++)
			{
				if (isBlocked[i])
				{
					depthCells[i] = 0;
					queue.Enqueue(i);
				}
				else
					depthCells[i] = int.MaxValue;
			}

			Spread(queue, depthCells);
		}

		/// <summary>Distance in cells from every cell to the nearest cell of <paramref name="component"/>.</summary>
		int[] DistanceToComponent(int component)
		{
			var distance = new int[labels.Length];
			var queue = new Queue<int>();
			for (var i = 0; i < labels.Length; i++)
			{
				if (labels[i] == component)
				{
					distance[i] = 0;
					queue.Enqueue(i);
				}
				else
					distance[i] = int.MaxValue;
			}

			Spread(queue, distance);
			return distance;
		}

		/// <summary>
		/// The BFS both transforms share. Spreads through EVERY cell including blocked ones -- an
		/// airframe sitting over the border needs a direction home, and a transform that refused to
		/// cross the border could not give it one.
		/// </summary>
		void Spread(Queue<int> queue, int[] distance)
		{
			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				var cy = current / width;
				var cx = current - (cy * width);
				var next = distance[current] + 1;

				for (var dy = -1; dy <= 1; dy++)
				{
					for (var dx = -1; dx <= 1; dx++)
					{
						if (dx == 0 && dy == 0)
							continue;

						var nx = cx + dx;
						var ny = cy + dy;
						if (nx < 0 || ny < 0 || nx >= width || ny >= height)
							continue;

						var n = (ny * width) + nx;
						if (distance[n] <= next)
							continue;

						distance[n] = next;
						queue.Enqueue(n);
					}
				}
			}
		}

		CPos CellAt(int index)
		{
			var y = index / width;
			return new CPos(left + index - (y * width), top + y);
		}

		static int CellOf(long world)
		{
			// Truncate toward negative infinity: world unit -1 is in cell -1, not cell 0. C#'s integer
			// division truncates toward ZERO, which would fold the two cells either side of the origin
			// into one and mis-side everything in the map's top-left border ring.
			return (int)((world >= 0 ? world : world - CellUnits + 1) / CellUnits);
		}

		/// <summary>The cell containing a world position, on the rectangular grid this mod uses.</summary>
		public static CPos CellContaining(long px, long py)
		{
			return new CPos(CellOf(px), CellOf(py));
		}

		/// <summary>True when this cell is part of the border itself.</summary>
		public bool IsInWallBand(CPos cell)
		{
			var i = IndexOf(cell);
			return i >= 0 && depthCells[i] == 0;
		}

		/// <summary>True when this world position is inside the border itself.</summary>
		public bool IsInWallBand(long px, long py)
		{
			return IsInWallBand(CellContaining(px, py));
		}

		/// <summary>
		/// Which component this cell belongs to, or <see cref="Unlabelled"/> for a border cell, an
		/// off-map cell or impassable terrain. The region's answer to
		/// <see cref="DefconWallGeometry.SideOf"/> -- see the file header on why it is a component id.
		/// </summary>
		public int SideOf(CPos cell)
		{
			var i = IndexOf(cell);
			return i < 0 ? Unlabelled : labels[i];
		}

		public int SideOf(long px, long py)
		{
			return SideOf(CellContaining(px, py));
		}

		/// <summary>
		/// May an actor whose home component is <paramref name="ownSide"/> be here? Being inside the
		/// border is beyond, so the border is solid rather than a seam to park on -- the same ruling
		/// the line makes, for the same reason.
		/// </summary>
		// OFF-MAP AND IMPASSABLE CELLS ARE NOT BEYOND. A line divides the whole plane, so its answer
		// off the map edge is meaningful; a region's is not, and returning "beyond" for every
		// unlabelled cell would forbid the entire border ring outside Bounds -- through which nothing
		// can cross anyway, because reaching it means crossing labelled land first. Refusing there
		// would strand aircraft that legitimately orbit the map edge.
		public bool IsBeyond(int ownSide, CPos cell)
		{
			if (IsDegenerate || ownSide == Unlabelled)
				return false;

			if (IsInWallBand(cell))
				return true;

			var side = SideOf(cell);
			return side != Unlabelled && side != ownSide;
		}

		public bool IsBeyond(int ownSide, long px, long py)
		{
			return IsBeyond(ownSide, CellContaining(px, py));
		}

		/// <summary>
		/// How far past the border a position is, in WORLD UNITS. Negative on the actor's own side,
		/// positive beyond it, zero inside the border -- the line's sign convention exactly, so the
		/// turn-back layer's margin arithmetic is unchanged. The magnitude is the distance to the
		/// nearest border cell rather than the perpendicular distance to a line, because a region has
		/// no perpendicular; see the file header.
		/// </summary>
		public long DepthBeyond(int ownSide, long px, long py)
		{
			if (IsDegenerate || ownSide == Unlabelled)
				return long.MinValue / 4;

			var cell = CellContaining(px, py);
			var i = IndexOf(cell);
			if (i < 0)
				return long.MinValue / 4;

			var distance = depthCells[i];
			if (distance == int.MaxValue)
				return long.MinValue / 4;

			var magnitude = (long)distance * CellUnits;
			if (distance == 0)
				return 0;

			var side = SideOf(cell);

			// An unlabelled but non-border cell is impassable terrain inside somebody's half. It is
			// not "beyond" (IsBeyond says so), so its depth must not be positive either, or the
			// turn-back layer would fly airframes home from over their own cliffs.
			if (side == Unlabelled || side == ownSide)
				return -magnitude;

			return magnitude;
		}

		/// <summary>
		/// A vector of about one cell pointing from here toward the nearest cell of
		/// <paramref name="side"/> -- the direction home. The region analogue of
		/// <see cref="DefconWallGeometry.NormalTowards"/>, and the one place the two genuinely differ:
		/// the line's normal is a constant, this one depends on where you are asking from, which is
		/// what a border that bends requires.
		/// </summary>
		public (long X, long Y) NormalTowards(int side, long px, long py)
		{
			if (IsDegenerate || side == Unlabelled || side < 0 || side >= ComponentCount)
				return (0, 0);

			var cell = CellContaining(px, py);
			var i = IndexOf(cell);
			if (i < 0)
				return (0, 0);

			var distance = componentDistance[side];
			if (distance[i] == 0)
				return (0, 0);

			// Steepest descent over the target component's transform: the neighbour that is closest to
			// that component is the way home. Ties break on the scan order, which is fixed, so this is
			// the same answer on every client.
			var cy = i / width;
			var cx = i - (cy * width);
			var best = distance[i];
			var bx = 0;
			var by = 0;

			for (var dy = -1; dy <= 1; dy++)
			{
				for (var dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dy == 0)
						continue;

					var nx = cx + dx;
					var ny = cy + dy;
					if (nx < 0 || ny < 0 || nx >= width || ny >= height)
						continue;

					var n = (ny * width) + nx;
					if (distance[n] >= best)
						continue;

					best = distance[n];
					bx = dx;
					by = dy;
				}
			}

			if (bx == 0 && by == 0)
				return (0, 0);

			// Scaled to about one cell, like the line's normal. A diagonal is left at (1024, 1024)
			// rather than normalised to 724 each: the caller multiplies by a travel distance and
			// divides by 1024, so a diagonal step simply travels sqrt(2) times further per unit of
			// depth -- which is correct, because the depth transform is itself 8-connected and counts
			// a diagonal step as one cell.
			return (bx * CellUnits, by * CellUnits);
		}
	}
}
