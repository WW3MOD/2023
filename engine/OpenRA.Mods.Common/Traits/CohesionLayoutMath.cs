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

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// The floating-point kernels of CohesionMoveModifier's slot layout, split out so they can be
	/// exercised without a Map (see tools/fp-determinism).
	///
	/// DETERMINISM HAZARD — this is the arithmetic that produces the actual destination CELLS. It runs
	/// inside ModifyGroupOrder, i.e. on the synced path on every client (UnitOrders.cs). Every kernel
	/// here has the same shape: integers in, double math, then a cast or Math.Round back to an integer
	/// cell offset. That final rounding is what turns a one-ULP disagreement between two machines into
	/// a unit standing in a different cell.
	///
	/// OpenRA's determinism model is integer-only — WDist, WAngle and WPos are all ints precisely to
	/// avoid this — so this whole class is a standing convention breach and wants replacing with
	/// fixed-point. The Map lookups the layout code performs around these kernels (Clamp, Contains,
	/// CellContaining, density and passability) are all integer and are deliberately NOT in here.
	/// </summary>
	public static class CohesionLayoutMath
	{
		/// <summary>Box formation column count. ComputeBoxSlots.</summary>
		public static int BoxColumns(int n)
		{
			var cols = (int)Math.Ceiling(Math.Sqrt(n * 2.0));
			cols = Math.Min(cols, n);
			return Math.Max(cols, 2);
		}

		/// <summary>
		/// Normalises the classifier's small integer treeline axis and returns the FORWARD vector
		/// LayCoverAwareLine expects (the perpendicular of the along-axis). ComputeEdgeLineSlots.
		/// </summary>
		public static (double ForwardX, double ForwardY, double AlongLen) TreelineForward(int lineAlongX, int lineAlongY)
		{
			var alongLen = Math.Sqrt((double)lineAlongX * lineAlongX + (double)lineAlongY * lineAlongY);
			var alongUX = lineAlongX / alongLen;
			var alongUY = lineAlongY / alongLen;

			return (alongUY, -alongUX, alongLen);
		}

		/// <summary>
		/// Gradient normalisation plus the anchor advance. The two integer offsets are the output that
		/// reaches the world; the doubles are returned for the determinism harness.
		/// </summary>
		public static (int AnchorDx, int AnchorDy, double UnitX, double UnitY, double GradLen, double Advance)
			EdgeAnchorOffset(int gradXCells, int gradYCells, int edgeAdvancePercent)
		{
			var gradLenSq = gradXCells * gradXCells + gradYCells * gradYCells;
			var gradLen = Math.Sqrt(gradLenSq);
			var unitX = gradXCells / gradLen;
			var unitY = gradYCells / gradLen;

			var advance = gradLen * edgeAdvancePercent / 100.0;

			return ((int)Math.Round(unitX * advance), (int)Math.Round(unitY * advance), unitX, unitY, gradLen, advance);
		}

		/// <summary>Offset of slot i along the axis perpendicular to forward. LayCoverAwareLine.</summary>
		public static (int Dx, int Dy, double T) LineSlotOffset(double forwardX, double forwardY, int i, int n, int colSpacing)
		{
			// Perpendicular axis (90° CCW): (-forwardY, forwardX).
			var perpUX = -forwardY;
			var perpUY = forwardX;

			var spacingCells = colSpacing / 1024.0;
			var t = (2.0 * i - (n - 1)) * 0.5 * spacingCells;

			return ((int)Math.Round(perpUX * t), (int)Math.Round(perpUY * t), t);
		}

		/// <summary>Horizontal fallback line offset. ComputeOpenLine.</summary>
		public static (int Dx, double T) OpenLineOffset(int i, int n, int colSpacing)
		{
			var spacingCells = colSpacing / 1024.0;
			var t = (2.0 * i - (n - 1)) * 0.5 * spacingCells;

			return ((int)Math.Round(t), t);
		}

		/// <summary>Click→group walk geometry. ComputeApproachSlots.</summary>
		public static (double DistCells, int MaxSteps, double UnitX, double UnitY) ApproachWalk(int dxCells, int dyCells)
		{
			var distCells = Math.Sqrt(dxCells * dxCells + dyCells * dyCells);
			if (distCells < 1)
				return (distCells, 0, 0, 0);

			return (distCells, (int)Math.Ceiling(distCells), dxCells / distCells, dyCells / distCells);
		}

		/// <summary>Offset of the step-th cell back along the approach axis. ComputeApproachSlots.</summary>
		public static (int Dx, int Dy) ApproachStepOffset(double unitX, double unitY, int step)
		{
			return ((int)Math.Round(unitX * step), (int)Math.Round(unitY * step));
		}

		/// <summary>Offset of the step-th passability probe. NudgeToPassable.</summary>
		public static (int Dx, int Dy) NudgeOffset(double dx, double dy, int step)
		{
			return ((int)Math.Round(dx * step), (int)Math.Round(dy * step));
		}
	}
}
