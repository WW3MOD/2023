#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Activities
{
	// WW3MOD path string-pulling (pipeline item 28, recon §Q7(b)).
	//
	// Pure, deterministic, integer-only geometry for corner-cutting the RENDERED movement line while the
	// A* cell path, cell reservations and per-cell pop cadence stay exactly as the pathfinder produced them.
	// The A* graph only emits 8-directional steps, so a diagonal-ish route zig-zags between 0 and 45 degrees.
	// Instead of aiming each move segment at the geometric cell boundary, we aim it at the projection of that
	// boundary onto the straight sightline toward the FARTHEST later waypoint the actor could walk to in a
	// clear straight line. The projection keeps the rendered position a bounded "shadow" of the reserved cell,
	// so occupancy/crush/adjacency logic (which key on the reserved CPos, never on WPos) are untouched.
	//
	// Assumes a Rectangular map grid (WW3MOD, mod.yaml MapGrid: Type: Rectangular) where a cell is CellSize
	// units and CellContaining is floor(pos / CellSize). Smoothing is gated to the ground layer by the caller,
	// so custom-layer coordinate mappings never reach here. Zero RNG, zero float — safe for the synced sim path.
	public static class PathStringPulling
	{
		public const int CellSize = 1024;

		// Hard ceiling on how far the rendered (smoothed) position may sit from the un-smoothed on-grid
		// boundary point it replaces — half a cell. This is what bounds the visual-vs-reserved divergence in
		// the stateful sim (where each segment re-anchors on the previous shadow), independent of corridor shape.
		public const int DefaultMaxDivergence = 512;

		// Floor division (round toward negative infinity) so cell derivation is correct for negative positions.
		public static int FloorDiv(int a, int b)
		{
			var q = a / b;
			if (a % b != 0 && (a < 0) != (b < 0))
				q--;

			return q;
		}

		// Rectangular-grid cell center. Matches Map.CenterOfCell for MapGridType.Rectangular.
		public static WPos CellCenter(CPos c)
		{
			return new WPos(CellSize * c.X + CellSize / 2, CellSize * c.Y + CellSize / 2, 0);
		}

		// Cell whose square contains pos on the ground plane. Matches Map.CellContaining for Rectangular.
		public static CPos CellContaining(WPos pos)
		{
			return new CPos(FloorDiv(pos.X, CellSize), FloorDiv(pos.Y, CellSize));
		}

		// Line-of-walk: true iff EVERY ground cell the open segment [from, to] passes through is enterable.
		// Integer grid DDA (Amanatides & Woo) with an exact corner guard: when the segment crosses a lattice
		// corner, both orthogonal neighbours must be enterable so a unit can never "squeeze" the diagonal gap
		// between two blocked cells. Deterministic; no sampling gaps, no float.
		public static bool LineOfWalkClear(WPos from, WPos to, Func<CPos, bool> canEnter)
		{
			var x0 = from.X;
			var y0 = from.Y;
			var x1 = to.X;
			var y1 = to.Y;

			var cx = FloorDiv(x0, CellSize);
			var cy = FloorDiv(y0, CellSize);
			var ex = FloorDiv(x1, CellSize);
			var ey = FloorDiv(y1, CellSize);

			if (!canEnter(new CPos(cx, cy)))
				return false;

			if (cx == ex && cy == ey)
				return true;

			var dx = x1 - x0;
			var dy = y1 - y0;
			var stepX = Math.Sign(dx);
			var stepY = Math.Sign(dy);
			long adx = Math.Abs((long)dx);
			long ady = Math.Abs((long)dy);

			// Running numerators. tMaxX = mx / adx is the parametric distance to the next x boundary (0..1],
			// likewise tMaxY = my / ady. mx starts at the WDist to the first boundary, then advances a full
			// cell (CellSize) each time we cross one. Sentinel MaxValue means "no movement on this axis".
			long mx, my;
			if (stepX > 0)
				mx = (cx + 1L) * CellSize - x0;
			else if (stepX < 0)
				mx = x0 - (long)cx * CellSize;
			else
				mx = long.MaxValue;

			if (stepY > 0)
				my = (cy + 1L) * CellSize - y0;
			else if (stepY < 0)
				my = y0 - (long)cy * CellSize;
			else
				my = long.MaxValue;

			// Hard iteration cap: the DDA is monotone toward (ex, ey), so it terminates in |dx|+|dy| cells.
			var guard = (int)(adx / CellSize + ady / CellSize) + 4;
			for (var i = 0; i < guard; i++)
			{
				// Compare tMaxX vs tMaxY via cross multiply, respecting the no-move sentinels.
				int cmp;
				if (adx == 0)
					cmp = 1;
				else if (ady == 0)
					cmp = -1;
				else
					cmp = (mx * ady).CompareTo(my * adx);

				if (cmp < 0)
				{
					cx += stepX;
					mx += CellSize;
				}
				else if (cmp > 0)
				{
					cy += stepY;
					my += CellSize;
				}
				else
				{
					// Exact lattice corner: forbid the diagonal squeeze, then step diagonally.
					if (!canEnter(new CPos(cx + stepX, cy)) || !canEnter(new CPos(cx, cy + stepY)))
						return false;

					cx += stepX;
					cy += stepY;
					mx += CellSize;
					my += CellSize;
				}

				if (!canEnter(new CPos(cx, cy)))
					return false;

				if (cx == ex && cy == ey)
					return true;
			}

			// Never reached the target cell within the bound: treat as not clear (defensive; should not happen).
			return false;
		}

		// Index into `upcoming` ([0] = the immediate next cell) of the FARTHEST waypoint, within maxLookahead,
		// reachable from `from` by a clear straight line. Returns 0 when nothing past the immediate next helps.
		public static int FarthestVisible(WPos from, IReadOnlyList<CPos> upcoming, int maxLookahead,
			Func<CPos, WPos> centerOf, Func<CPos, bool> canEnter)
		{
			var limit = Math.Min(upcoming.Count, maxLookahead);
			for (var k = limit - 1; k >= 1; k--)
				if (LineOfWalkClear(from, centerOf(upcoming[k]), canEnter))
					return k;

			return 0;
		}

		// Project geomTo onto the ray from -> target (integer, clamped to the [from, target] span).
		// This is the "shadow" of the geometric waypoint on the straight sightline.
		public static WPos ProjectOntoSightline(WPos from, WPos geomTo, WPos target)
		{
			var dirX = target.X - from.X;
			var dirY = target.Y - from.Y;
			long len2 = (long)dirX * dirX + (long)dirY * dirY;
			if (len2 == 0)
				return geomTo;

			var vx = geomTo.X - from.X;
			var vy = geomTo.Y - from.Y;
			var dot = (long)vx * dirX + (long)vy * dirY;

			// Degenerate (geomTo not ahead of the sightline): keep the actor where it is.
			if (dot <= 0)
				return from;

			// Clamp: never render past the sightline target.
			if (dot >= len2)
				return new WPos(target.X, target.Y, geomTo.Z);

			var px = from.X + (int)(dirX * dot / len2);
			var py = from.Y + (int)(dirY * dot / len2);
			return new WPos(px, py, geomTo.Z);
		}

		// Clamp `smoothed` so it is never more than maxDivergence WDist from geomTo (the un-smoothed on-grid
		// point). Guarantees the documented rendered-vs-reserved bound regardless of corridor geometry.
		public static WPos ClampDivergence(WPos smoothed, WPos geomTo, int maxDivergence)
		{
			if (maxDivergence <= 0)
				return smoothed;

			var off = smoothed - geomTo;
			var lenSq = (long)off.X * off.X + (long)off.Y * off.Y;
			if (lenSq <= (long)maxDivergence * maxDivergence)
				return smoothed;

			var len = off.HorizontalLength;
			if (len == 0)
				return smoothed;

			return geomTo + new WVec(off.X * maxDivergence / len, off.Y * maxDivergence / len, 0);
		}

		// Full pipeline: returns the smoothed segment target, or geomTo unchanged when there is no useful
		// straight-line shortcut. `upcoming[0]` is the cell geomTo heads toward; later entries are farther.
		// The result is clamped to within maxDivergence WDist of geomTo (pass 0 to disable the clamp).
		public static WPos SmoothTarget(WPos from, WPos geomTo, IReadOnlyList<CPos> upcoming, int maxLookahead,
			Func<CPos, WPos> centerOf, Func<CPos, bool> canEnter, int maxDivergence = DefaultMaxDivergence)
		{
			if (upcoming.Count == 0 || maxLookahead <= 1)
				return geomTo;

			var k = FarthestVisible(from, upcoming, maxLookahead, centerOf, canEnter);
			if (k <= 0)
				return geomTo;

			return ClampDivergence(ProjectOntoSightline(from, geomTo, centerOf(upcoming[k])), geomTo, maxDivergence);
		}
	}
}
