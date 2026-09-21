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
 * CELL GEOMETRY FOR THE ENDING -- AND IT IS A QUARTER OF WHAT IT WAS. Read this before looking for
 * something that used to be here.
 *
 * WHAT WENT, AND WITH WHAT. On 2026-09-20 the map-wide Dead Hand salvo was replaced by two
 * per-side strike packages (DoomsdayStrike's header for the why). Everything in this file that
 * existed to lay that salvo out went with it and is recoverable from git: the tier enum and the
 * wave scheduler (DoomsdayTier, DoomsdayImpact, ScheduleTimings, BuildSchedule), the city spread
 * (LongAxis, CityAimPoints), the coverage proof and the fill pass it fed (EffectiveRadius,
 * UncoveredCells, GridSpacing, LatticePointFor, FillPoints -- the last three already dead since
 * the 2026-09-07 retune), the jitter (Jitter, DiscJitter), and the separation filter, which
 * FinalExchangeTargeting now runs inline because it has to interleave with a package size.
 *
 * WHAT STAYED IS WHAT STILL HAS A CALLER. The clustering is the piece with a second life: it is
 * run over ONE SIDE'S actors now instead of over every building on the map, so what it finds is an
 * army or a base rather than a town -- but the algorithm, its determinism argument and its tests
 * are untouched.
 *
 * DETERMINISM, unchanged and still the reason any of this is a static class: these are pure integer
 * functions with no RNG, no float and no collection whose enumeration order is undefined. They
 * decide where warheads land and therefore must be byte-identical on every client.
 */

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class DoomsdayMath
	{
		public static long DistanceSquared(CPos a, CPos b)
		{
			long dx = a.X - b.X;
			long dy = a.Y - b.Y;
			return dx * dx + dy * dy;
		}

		/// <summary>Integer square root, for turning a squared cell distance back into cells.</summary>
		public static int Isqrt(long value)
		{
			if (value <= 0)
				return 0;

			var r = (long)Math.Sqrt(value);

			// Math.Sqrt on a long can land one either side of the true root; walk it back onto the exact
			// floor so the result is a pure function of the input rather than of the FPU's rounding.
			while (r > 0 && r * r > value)
				r--;
			while ((r + 1) * (r + 1) <= value)
				r++;

			return (int)r;
		}

		/// <summary>
		/// <para>Single-linkage clustering of asset cells into CONCENTRATIONS: two assets join the same
		/// cluster when they are within <paramref name="linkDistanceCells"/> of each other, transitively.
		/// A concentration is a cluster, not an actor, which is the whole reason this exists -- a column
		/// on a road and a base of twelve buildings are each one target, not twelve.</para>
		///
		/// <para>DETERMINISM. The input list order fixes the output completely. Seeds are taken in ascending
		/// index order, each cluster grows by a FIFO walk that appends in ascending index order, and the
		/// members are sorted before the cluster is emitted. No dictionary, no set, no LINQ grouping. The
		/// caller must hand in a list that is itself deterministically ordered — DoomsdayStrike sorts by
		/// ActorID, which is assigned in world-creation order and is identical on every client.</para>
		/// </summary>
		public static List<List<int>> ClusterAssets(IReadOnlyList<CPos> assets, int linkDistanceCells)
		{
			var clusters = new List<List<int>>();
			if (assets == null || assets.Count == 0)
				return clusters;

			var linkSq = (long)linkDistanceCells * linkDistanceCells;
			var assigned = new bool[assets.Count];

			for (var seed = 0; seed < assets.Count; seed++)
			{
				if (assigned[seed])
					continue;

				var cluster = new List<int> { seed };
				assigned[seed] = true;

				// `cluster` doubles as the queue: everything before `head` has already been expanded.
				for (var head = 0; head < cluster.Count; head++)
				{
					var from = assets[cluster[head]];
					for (var i = 0; i < assets.Count; i++)
					{
						if (assigned[i])
							continue;

						if (DistanceSquared(from, assets[i]) > linkSq)
							continue;

						assigned[i] = true;
						cluster.Add(i);
					}
				}

				cluster.Sort();
				clusters.Add(cluster);
			}

			return clusters;
		}

		/// <summary>Integer centroid of a set of cells.</summary>
		public static CPos Centroid(IReadOnlyList<CPos> assets, IReadOnlyList<int> members)
		{
			long sx = 0, sy = 0;
			foreach (var m in members)
			{
				sx += assets[m].X;
				sy += assets[m].Y;
			}

			return new CPos((int)(sx / members.Count), (int)(sy / members.Count));
		}
	}
}
