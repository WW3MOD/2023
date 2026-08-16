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
	public enum CohesionIntent { Open, SpreadInside, EdgeLine, Approach }

	/// <summary>
	/// Result of classifying a click against sampled cover density. The double fields are the
	/// intermediates of the covariance/eigenvalue step, surfaced so a determinism harness can
	/// compare them bit-for-bit across runtimes — they are diagnostic only and no simulation
	/// code reads them.
	/// </summary>
	public readonly struct CohesionIntentResult
	{
		public readonly CohesionIntent Intent;
		public readonly int CentroidDxCells;
		public readonly int CentroidDyCells;
		public readonly int LineAlongX;
		public readonly int LineAlongY;

		public readonly double Cxx;
		public readonly double Cyy;
		public readonly double Cxy;
		public readonly double Disc;
		public readonly double Lambda1;
		public readonly double Lambda2;

		public CohesionIntentResult(CohesionIntent intent, int centroidDxCells, int centroidDyCells,
			int lineAlongX, int lineAlongY, double cxx, double cyy, double cxy, double disc, double lambda1, double lambda2)
		{
			Intent = intent;
			CentroidDxCells = centroidDxCells;
			CentroidDyCells = centroidDyCells;
			LineAlongX = lineAlongX;
			LineAlongY = lineAlongY;
			Cxx = cxx;
			Cyy = cyy;
			Cxy = cxy;
			Disc = disc;
			Lambda1 = lambda1;
			Lambda2 = lambda2;
		}
	}

	/// <summary>
	/// The formation-intent classification arithmetic, split out of CohesionMoveModifier.ClassifyIntent
	/// so that it can be exercised without a Map.
	///
	/// DETERMINISM HAZARD: this is double-precision math on the SYNCED path — ModifyGroupOrder runs on
	/// every client (UnitOrders.cs) and its result picks each actor's destination cell. OpenRA's
	/// determinism model is integer-only (WDist/WAngle/WPos are all ints) precisely to avoid this. The
	/// hazardous shapes here are the a*b - c*d discriminant (an FMA-contraction candidate, where a JIT
	/// that fuses and one that does not disagree in the last bits) and the comparisons at the Treeline
	/// branch, which turn a one-ULP difference into a different formation strategy. Isolated here so
	/// it can be measured across runtimes and, ultimately, replaced with fixed-point.
	/// </summary>
	public static class CohesionIntentMath
	{
		public static CohesionIntentResult Classify(
			int totalDensity, int weightedX, int weightedY, long sumXX, long sumYY, long sumXY,
			int openDensityThreshold, float treelineMinSpreadSq, float treelineAnisotropyRatio, int edgeOffsetThresholdCellsSq)
		{
			if (totalDensity < openDensityThreshold)
				return new CohesionIntentResult(CohesionIntent.Open, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

			// Integer-round the centroid offset so the gradient is whole-cell stable.
			var centroidDxCells = RoundDiv(weightedX, totalDensity);
			var centroidDyCells = RoundDiv(weightedY, totalDensity);

			// Covariance of the density about its centroid (parallel-axis theorem: Cov = E[XY] - E[X]E[Y]).
			var mx = weightedX / (double)totalDensity;
			var my = weightedY / (double)totalDensity;
			var cxx = sumXX / (double)totalDensity - mx * mx;
			var cyy = sumYY / (double)totalDensity - my * my;
			var cxy = sumXY / (double)totalDensity - mx * my;

			// Eigenvalues of the symmetric covariance [[cxx,cxy],[cxy,cyy]]: lambda = tr/2 ± sqrt(...).
			var tr = cxx + cyy;
			var disc = Math.Sqrt(Math.Max(0.0, tr * tr / 4.0 - (cxx * cyy - cxy * cxy)));
			var lambda1 = tr / 2.0 + disc;
			var lambda2 = tr / 2.0 - disc;

			if (lambda1 >= treelineMinSpreadSq && lambda1 >= treelineAnisotropyRatio * Math.Max(lambda2, 0.0))
			{
				double vx, vy;
				if (Math.Abs(cxy) > 1e-6)
				{
					vx = lambda1 - cyy;
					vy = cxy;
				}
				else if (cxx >= cyy)
				{
					vx = 1.0;
					vy = 0.0;
				}
				else
				{
					vx = 0.0;
					vy = 1.0;
				}

				var vlen = Math.Sqrt(vx * vx + vy * vy);
				if (vlen > 0)
				{
					var lineAlongX = (int)Math.Round(vx / vlen * 4.0);
					var lineAlongY = (int)Math.Round(vy / vlen * 4.0);
					if (lineAlongX == 0 && lineAlongY == 0)
						lineAlongX = 1;

					return new CohesionIntentResult(CohesionIntent.EdgeLine, centroidDxCells, centroidDyCells,
						lineAlongX, lineAlongY, cxx, cyy, cxy, disc, lambda1, lambda2);
				}
			}

			var offsetMagSq = centroidDxCells * centroidDxCells + centroidDyCells * centroidDyCells;
			var intent = offsetMagSq >= edgeOffsetThresholdCellsSq ? CohesionIntent.EdgeLine : CohesionIntent.SpreadInside;

			return new CohesionIntentResult(intent, centroidDxCells, centroidDyCells, 0, 0,
				cxx, cyy, cxy, disc, lambda1, lambda2);
		}

		public static int RoundDiv(int numerator, int denominator)
		{
			if (denominator == 0)
				return 0;

			var half = denominator / 2;
			return numerator >= 0
				? (numerator + half) / denominator
				: -((-numerator + half) / denominator);
		}
	}
}
