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
