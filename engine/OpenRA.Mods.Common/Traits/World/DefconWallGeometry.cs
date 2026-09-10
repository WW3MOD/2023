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
 * The DEFCON 3 dividing line, as pure geometry, with no dependency on Actor, World or Map.
 *
 * It is a plain class for the same reason DefconEscalationState is: nothing in OpenRA.Test can
 * construct a World, so a predicate that lives inside a trait method is a predicate verified by
 * reading only. Every question the three enforcement layers ask -- which side of the line is this,
 * how far from the line is this, which way is home -- is answered here and nowhere else.
 *
 * COORDINATES. Everything is in WORLD units (1024 per cell) and everything is `long`. The map is up
 * to 256 cells across, so a coordinate reaches ~262144 and a cross product of two such reaches
 * ~6.9e10 -- which overflows `int` silently. That overflow is the whole reason this class does not
 * take CPos or WPos: it takes raw longs, the caller converts through Map.CenterOfCell, and there is
 * exactly one conversion formula in the codebase rather than a second one written out here.
 *
 * NO FLOATING POINT ANYWHERE. This runs in the simulation on every aircraft, so it must be
 * bit-identical across machines. The one square root needed (to turn a cross product into a
 * distance) is an integer Newton iteration below.
 *
 * THE LINE IS INFINITE, NOT A SEGMENT. A dividing line that stops short of the map edge is a line
 * an aircraft flies around, which is not a wall. The authored endpoints therefore only fix the
 * line's POSITION and DIRECTION; the half-planes either side of it extend forever. Authoring is
 * expected to place the endpoints on or beyond the map edges, and nothing here depends on it.
 */

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public class DefconWallGeometry
	{
		// A point exactly on the line belongs to neither side. Callers treat it as "beyond", which is
		// what makes the line itself impassable rather than a seam both players may sit on.
		public const int NoSide = 0;

		readonly long ax, ay;
		readonly long dx, dy;
		readonly long length;

		public readonly long HalfWidth;

		// True when the two authored endpoints coincide, i.e. no line was authored at all. Every
		// consumer must treat this as "there is no wall" -- it is the state every shipped map is in,
		// and it is what makes this whole feature inert until somebody draws a line.
		public bool IsDegenerate => length == 0;

		public DefconWallGeometry(long startX, long startY, long endX, long endY, long halfWidth)
		{
			ax = startX;
			ay = startY;
			dx = endX - startX;
			dy = endY - startY;
			HalfWidth = halfWidth > 0 ? halfWidth : 0;
			length = ISqrt(dx * dx + dy * dy);
		}

		/// <summary>
		/// Twice the signed area of the triangle (start, end, p). Sign is the side; magnitude divided
		/// by the line length is the distance. Both readings come from this one number.
		/// </summary>
		public long SignedCross(long px, long py)
		{
			return (dx * (py - ay)) - (dy * (px - ax));
		}

		/// <summary>-1, 0 or +1. 0 means exactly on the line, which is <see cref="NoSide"/>.</summary>
		public int SideOf(long px, long py)
		{
			var cross = SignedCross(px, py);
			if (cross > 0)
				return 1;

			return cross < 0 ? -1 : NoSide;
		}

		/// <summary>Perpendicular distance from the point to the (infinite) line, in world units.</summary>
		public long DistanceToLine(long px, long py)
		{
			if (length == 0)
				return 0;

			var cross = SignedCross(px, py);
			if (cross < 0)
				cross = -cross;

			// cross and length are both derived from coordinates bounded by the map, so this division
			// cannot overflow and needs no widening. Truncation toward zero is deliberate: it errs
			// toward "closer to the line", which errs toward enforcing.
			return cross / length;
		}

		/// <summary>The band of cells the line itself occupies -- the wall proper.</summary>
		public bool IsInWallBand(long px, long py)
		{
			return !IsDegenerate && DistanceToLine(px, py) <= HalfWidth;
		}

		/// <summary>
		/// The question all three enforcement layers actually ask: may an actor whose home half is
		/// <paramref name="ownSide"/> be at this point? Being in the wall band is beyond, so the line
		/// is solid rather than a seam to park on; being exactly on it is beyond for the same reason.
		/// </summary>
		public bool IsBeyond(int ownSide, long px, long py)
		{
			if (IsDegenerate || ownSide == NoSide)
				return false;

			return SideOf(px, py) != ownSide || IsInWallBand(px, py);
		}

		/// <summary>
		/// How far past the line a point is, in world units. Negative on the actor's own side, so a
		/// caller can trigger a turn-back BEFORE the line is reached by testing against a margin.
		/// </summary>
		public long DepthBeyond(int ownSide, long px, long py)
		{
			if (IsDegenerate || ownSide == NoSide)
				return long.MinValue / 4;

			var distance = DistanceToLine(px, py);
			return SideOf(px, py) == ownSide ? -distance : distance;
		}

		/// <summary>
		/// A vector of length ~1024 (one cell) pointing from the line into the given half-plane. This
		/// is how the turn-back layer picks somewhere to fly to: home is straight back along it.
		/// </summary>
		public (long X, long Y) NormalTowards(int side)
		{
			if (IsDegenerate || side == NoSide)
				return (0, 0);

			// (-dy, dx) satisfies cross > 0, so it points into the +1 half-plane by construction.
			var nx = -dy * 1024 / length;
			var ny = dx * 1024 / length;
			return side > 0 ? (nx, ny) : (-nx, -ny);
		}

		/// <summary>
		/// The dividing line for a whole match: split the homes into their two alliance groups, take
		/// each group's centroid, and bisect. This is what replaces per-map authoring.
		/// </summary>
		/// <summary>
		/// The segment of the (infinite) line that lies inside an axis-aligned world-space rectangle,
		/// for drawing. Returns false when the line misses the rectangle entirely.
		/// </summary>
		// THE LINE IS INFINITE AND THE MAP IS NOT, WHICH MATTERS ONLY FOR RENDERING. The authored and
		// derived endpoints sit hundreds of cells outside the map on purpose, so drawing between them
		// would streak an annotation across the black border area -- annotations are drawn AFTER the
		// shroud pass (WorldRenderer.cs:199 then :287), so nothing hides it. Clipping here keeps the
		// drawn line exactly as long as the map and no longer.
		//
		// Integer throughout, like everything else in this class. It is only used for drawing today,
		// but a renderer that quietly used floating point would be a second arithmetic for the same
		// line, and the whole point of this class is that there is one.
		public bool ClipToRect(long left, long top, long right, long bottom, out WPos start, out WPos end)
		{
			start = WPos.Zero;
			end = WPos.Zero;

			if (IsDegenerate)
				return false;

			// Candidate crossings with the four edges. A point is kept only when it lies within the
			// span of the edge it crossed, which is what discards the two extensions of the rectangle.
			var points = new List<(long X, long Y)>();

			if (dx != 0)
			{
				foreach (var x in new[] { left, right })
				{
					var y = ay + ((x - ax) * dy / dx);
					if (y >= top && y <= bottom)
						points.Add((x, y));
				}
			}

			if (dy != 0)
			{
				foreach (var y in new[] { top, bottom })
				{
					var x = ax + ((y - ay) * dx / dy);
					if (x >= left && x <= right)
						points.Add((x, y));
				}
			}

			// A line through a corner produces the same point twice; a line along an edge produces
			// several. Take the two that are farthest apart, which is the segment in every case.
			if (points.Count < 2)
				return false;

			var bestA = points[0];
			var bestB = points[0];
			var bestDistance = -1L;
			for (var i = 0; i < points.Count; i++)
			{
				for (var j = i + 1; j < points.Count; j++)
				{
					var ddx = points[i].X - points[j].X;
					var ddy = points[i].Y - points[j].Y;
					var distance = (ddx * ddx) + (ddy * ddy);
					if (distance > bestDistance)
					{
						bestDistance = distance;
						bestA = points[i];
						bestB = points[j];
					}
				}
			}

			if (bestDistance <= 0)
				return false;

			start = new WPos((int)bestA.X, (int)bestA.Y, 0);
			end = new WPos((int)bestB.X, (int)bestB.Y, 0);
			return true;
		}

		// EXACTLY TWO GROUPS OR NO LINE AT ALL. A three-way free-for-all has no dividing line, and a
		// wall pointing somewhere nobody chose is worse than no wall -- the same reasoning as
		// TwoCoincidentSpawnsDeriveNoLineRatherThanAnArbitraryOne. A degenerate return is visibly
		// wrong and therefore fixable; an arbitrary one is not.
		//
		// The caller supplies homes ALREADY GROUPED, as (group, home) pairs, because alliance is a
		// Player concept and this class deliberately knows nothing about Player, World or Map. Group
		// numbers need not be 0 and 1 -- only the count of distinct values matters -- but the caller
		// must produce them deterministically, because which group lands first fixes the SIGN of the
		// line and therefore every SideOf answer.
		//
		// Centroids are integer means. On an odd sum the midpoint truncates by half a cell, which is
		// well inside the wall band and cannot move a spawn to the wrong side.
		public static (CPos Start, CPos End) BisectorOfSides(
			IEnumerable<(int Group, CPos Home)> homes, int extendCells)
		{
			var groups = new List<int>();
			var sumX = new List<long>();
			var sumY = new List<long>();
			var count = new List<int>();

			foreach (var (group, home) in homes)
			{
				var i = groups.IndexOf(group);
				if (i < 0)
				{
					groups.Add(group);
					sumX.Add(0);
					sumY.Add(0);
					count.Add(0);
					i = groups.Count - 1;
				}

				sumX[i] += home.X;
				sumY[i] += home.Y;
				count[i]++;
			}

			if (groups.Count != 2)
				return (CPos.Zero, CPos.Zero);

			var a = new CPos((int)(sumX[0] / count[0]), (int)(sumY[0] / count[0]));
			var b = new CPos((int)(sumX[1] / count[1]), (int)(sumY[1] / count[1]));
			return PerpendicularBisector(a, b, extendCells);
		}

		/// <summary>
		/// The perpendicular bisector of two points, extended far enough to leave any map. This is the
		/// derivation an authored line can be replaced by -- see the trait's Info documentation. Pure
		/// cell arithmetic, so it is the same answer on every machine.
		/// </summary>
		public static (CPos Start, CPos End) PerpendicularBisector(CPos a, CPos b, int extendCells)
		{
			var midX = (a.X + b.X) / 2;
			var midY = (a.Y + b.Y) / 2;

			// Perpendicular to (b - a), scaled so the endpoints land outside any plausible map.
			var px = -(b.Y - a.Y);
			var py = b.X - a.X;

			var scale = ISqrt((long)px * px + (long)py * py);
			if (scale == 0)
				return (new CPos(midX, midY), new CPos(midX, midY));

			var ex = (int)(px * extendCells / scale);
			var ey = (int)(py * extendCells / scale);

			return (new CPos(midX - ex, midY - ey), new CPos(midX + ex, midY + ey));
		}

		// Integer square root by Newton's method. Deterministic, allocation-free, and the reason this
		// class needs no floating point at all. Written here rather than borrowed so that the pure
		// geometry has no dependency the test project has to drag in.
		static long ISqrt(long value)
		{
			if (value <= 0)
				return 0;

			var x = value;
			var y = (x + 1) / 2;
			while (y < x)
			{
				x = y;
				y = (x + value / x) / 2;
			}

			return x;
		}
	}
}
