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
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Which stage of the Dead Hand salvo an impact belongs to. The ORDER OF THE VALUES IS THE ORDER OF
	/// THE SEQUENCE and <see cref="DoomsdayMath.BuildSchedule"/> reads it as such, so do not reorder them
	/// to group related things together.
	///
	/// Small warheads on the outliers open, because they are the build rather than the climax; the cities
	/// go up after a deliberate pause long enough that the viewer has decided it was over. Fill comes last
	/// and is not part of the show — it is the mechanism that makes "nothing survives" true, and it is
	/// separated so it cannot dilute the climax.
	/// </summary>
	public enum DoomsdayTier
	{
		/// <summary>Wave 1: single small warheads on derricks and isolated structures.</summary>
		Outlier = 0,

		/// <summary>Wave 2, after the pause: the large warheads on the population centres.</summary>
		City = 1,

		/// <summary>Cleanup: whatever the targeted salvo did not already cover.</summary>
		Fill = 2,
	}

	/// <summary>One scheduled detonation: where, when relative to the first impact, and in which stage.</summary>
	public readonly struct DoomsdayImpact
	{
		public readonly CPos Cell;
		public readonly int ArrivalOffset;
		public readonly DoomsdayTier Tier;

		public DoomsdayImpact(CPos cell, int arrivalOffset, DoomsdayTier tier)
		{
			Cell = cell;
			ArrivalOffset = arrivalOffset;
			Tier = tier;
		}
	}

	/// <summary>
	/// The placement and timing arithmetic behind the Dead Hand salvo, extracted from
	/// <see cref="DoomsdayStrike"/> so it can be tested without a World. Everything here is PURE: same
	/// inputs, same outputs, no RNG, no trait lookups, and — the one that actually desyncs games — no
	/// iteration over a hash-ordered container. The trait supplies the randomness, and supplies it as an
	/// already-drawn offset per aim point (see <see cref="DiscJitter"/>), so the layout stays reproducible
	/// under test.
	///
	/// ALL DISTANCES HERE ARE IN CELLS, never WDist. The trait converts once, at the boundary. Cell units
	/// are what the coverage guarantee is stated in and what the tests assert over; mixing the two is the
	/// quiet way to get a factor-of-1024 error into a radius comparison that still compiles.
	/// </summary>
	public static class DoomsdayMath
	{
		// sqrt(2) as a rational, for the lattice spacing derivation in GridSpacing.
		const int Sqrt2Num = 1414;
		const int Sqrt2Den = 1000;

		/// <summary>A drawn perturbation, in cells, guaranteed by construction to satisfy X^2 + Y^2 &lt;= bound^2.</summary>
		public readonly struct Jitter
		{
			public readonly int X;
			public readonly int Y;
			public Jitter(int x, int y) { X = x; Y = y; }
		}

		/// <summary>
		/// Lattice spacing, in cells, such that a cell at the WORST position inside a spacing-sized square
		/// centred on a lattice point is still within <paramref name="lethalRadiusCells"/> of it after the
		/// RNG has pushed the lattice point <paramref name="jitterCells"/> away.
		///
		/// The worst case is the square's corner, at half-diagonal s*sqrt(2)/2 = s/sqrt(2). Requiring
		///     s/sqrt(2) + jitter &lt;= radius
		/// gives s &lt;= sqrt(2) * (radius - jitter), which is what this returns. The floor only ever makes
		/// the cover tighter than the bound demands, so the inequality survives the integer arithmetic.
		/// </summary>
		public static int GridSpacing(int lethalRadiusCells, int jitterCells)
		{
			var effective = lethalRadiusCells - jitterCells;
			if (effective < 1)
				return 1;

			return Math.Max(1, effective * Sqrt2Num / Sqrt2Den);
		}

		/// <summary>
		/// The radius placement is actually done against: the lethal radius less the jitter bound. Every
		/// coverage decision in this file uses THIS, never the raw radius, and that single substitution is
		/// what makes the randomness incapable of breaking the guarantee rather than merely unlikely to.
		/// </summary>
		public static int EffectiveRadius(int lethalRadiusCells, int jitterCells)
		{
			return Math.Max(1, lethalRadiusCells - jitterCells);
		}

		public static long DistanceSquared(CPos a, CPos b)
		{
			long dx = a.X - b.X;
			long dy = a.Y - b.Y;
			return dx * dx + dy * dy;
		}

		public static bool WithinRadius(CPos a, CPos b, int radiusCells)
		{
			return DistanceSquared(a, b) <= (long)radiusCells * radiusCells;
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
		/// Single-linkage clustering of asset cells into "cities": two assets join the same cluster when
		/// they are within <paramref name="linkDistanceCells"/> of each other, transitively. A city is a
		/// cluster, not an actor, which is the whole reason this exists.
		///
		/// DETERMINISM. The input list order fixes the output completely. Seeds are taken in ascending
		/// index order, each cluster grows by a FIFO walk that appends in ascending index order, and the
		/// members are sorted before the cluster is emitted. No dictionary, no set, no LINQ grouping. The
		/// caller must hand in a list that is itself deterministically ordered — DoomsdayStrike sorts by
		/// ActorID, which is assigned in world-creation order and is identical on every client.
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

		/// <summary>
		/// The two cluster members furthest apart — the cluster's long axis. Scanned in ascending index
		/// order with a strict &gt;, so ties resolve to the lowest-index pair and the result depends on
		/// nothing but the input order.
		/// </summary>
		public static (int A, int B) LongAxis(IReadOnlyList<CPos> assets, IReadOnlyList<int> members)
		{
			var bestA = members[0];
			var bestB = members[0];
			var best = -1L;

			for (var i = 0; i < members.Count; i++)
			{
				for (var j = i + 1; j < members.Count; j++)
				{
					var d = DistanceSquared(assets[members[i]], assets[members[j]]);
					if (d > best)
					{
						best = d;
						bestA = members[i];
						bestB = members[j];
					}
				}
			}

			return (bestA, bestB);
		}

		/// <summary>
		/// Aim points for one city: <paramref name="count"/> of them, placed symmetrically about the
		/// cluster centroid along its long axis and separated by up to
		/// <paramref name="minSeparationCells"/>, so two warheads on one city read as two rather than as
		/// one smeared one.
		///
		/// THE SPREAD IS CAPPED BY COVERAGE, not merely requested. Pushing the pair apart moves each point
		/// away from the far side of the city, so the half-spread is clamped to
		/// (effectiveRadius - clusterReach): a member sits at most `reach` from the centroid and the split
		/// costs it at most `half` more to reach the NEARER point, so reach + half &lt;= effectiveRadius
		/// keeps every member inside somebody's lethal radius. A city too tight to hold the requested
		/// separation collapses its points toward the centroid and the caller's min-separation filter drops
		/// the duplicate. Coverage beats spectacle every time this is close.
		/// </summary>
		public static List<CPos> CityAimPoints(
			IReadOnlyList<CPos> assets, IReadOnlyList<int> members, int count, int minSeparationCells, int effectiveRadiusCells)
		{
			var result = new List<CPos>();
			var centre = Centroid(assets, members);

			if (count <= 1 || members.Count < 2)
			{
				result.Add(centre);
				return result;
			}

			var (a, b) = LongAxis(assets, members);
			var axis = assets[b] - assets[a];
			var axisLen = Isqrt(DistanceSquared(assets[a], assets[b]));
			if (axisLen == 0)
			{
				result.Add(centre);
				return result;
			}

			var reach = 0;
			foreach (var m in members)
			{
				var d = Isqrt(DistanceSquared(assets[m], centre));
				if (d > reach)
					reach = d;
			}

			var budget = Math.Max(0, effectiveRadiusCells - reach);
			var half = Math.Min(minSeparationCells / 2, budget);

			for (var i = 0; i < count; i++)
			{
				// Symmetric about the centroid: -half and +half for count 2, evenly spaced across
				// [-half, +half] above that.
				var t = 2 * i - (count - 1);
				var den = count - 1;
				var ox = (int)((long)axis.X * half * t / ((long)den * axisLen));
				var oy = (int)((long)axis.Y * half * t / ((long)den * axisLen));
				result.Add(new CPos(centre.X + ox, centre.Y + oy));
			}

			return result;
		}

		/// <summary>
		/// Greedy minimum-separation filter: walk the candidates in order, keep one only when it is at
		/// least <paramref name="minSeparationCells"/> from everything already kept. The caller's order is
		/// the priority order, so high-value aim points survive and the crowded low-value ones are what
		/// gets dropped.
		///
		/// A DROPPED CANDIDATE IS NOT AN UNCOVERED ASSET — it was dropped precisely because a kept point
		/// sits near it. But "near" here is min-separation, not lethal radius, and those are different
		/// numbers, so that intuition is not a proof. The caller still runs <see cref="UncoveredCells"/>
		/// afterwards, which is what turns it into a checked fact.
		/// </summary>
		public static List<int> MinSeparationFilter(IReadOnlyList<CPos> candidates, int minSeparationCells)
		{
			var kept = new List<int>();
			var minSq = (long)minSeparationCells * minSeparationCells;

			for (var i = 0; i < candidates.Count; i++)
			{
				var ok = true;
				foreach (var k in kept)
				{
					if (DistanceSquared(candidates[i], candidates[k]) < minSq)
					{
						ok = false;
						break;
					}
				}

				if (ok)
					kept.Add(i);
			}

			return kept;
		}

		/// <summary>
		/// Every cell of <paramref name="bounds"/> not already within
		/// <paramref name="effectiveRadiusCells"/> of some aim point. This is the input to the fill pass,
		/// and an empty result IS the coverage proof.
		/// </summary>
		public static List<CPos> UncoveredCells(Rectangle bounds, IReadOnlyList<CPos> aimPoints, int effectiveRadiusCells)
		{
			var uncovered = new List<CPos>();
			var radiusSq = (long)effectiveRadiusCells * effectiveRadiusCells;

			for (var y = bounds.Top; y < bounds.Bottom; y++)
			{
				for (var x = bounds.Left; x < bounds.Right; x++)
				{
					var c = new CPos(x, y);
					var covered = false;
					foreach (var p in aimPoints)
					{
						if (DistanceSquared(c, p) <= radiusSq)
						{
							covered = true;
							break;
						}
					}

					if (!covered)
						uncovered.Add(c);
				}
			}

			return uncovered;
		}

		/// <summary>
		/// The lattice point responsible for a cell: the centre of the spacing-sized strip the cell falls
		/// into, on each axis. Deriving the fill point FROM the cell, rather than laying a lattice down and
		/// hoping it lines up, is what makes the cover argument a construction instead of a search.
		/// </summary>
		public static CPos LatticePointFor(CPos cell, Rectangle bounds, int spacing)
		{
			return new CPos(
				StripCentre(cell.X, bounds.Left, bounds.Width, spacing),
				StripCentre(cell.Y, bounds.Top, bounds.Height, spacing));
		}

		static int StripCentre(int v, int origin, int span, int spacing)
		{
			if (span <= 0)
				return origin;

			var strips = Math.Max(1, (span + spacing - 1) / spacing);
			var index = (int)((long)(v - origin) * strips / span);
			index = Math.Clamp(index, 0, strips - 1);

			var lo = origin + (int)((long)index * span / strips);
			var hi = origin + (int)((long)(index + 1) * span / strips) - 1;
			return (lo + hi) / 2;
		}

		/// <summary>
		/// Fill aim points closing whatever the asset-driven salvo left open: one per distinct lattice
		/// strip containing at least one uncovered cell, so the count scales with the size of the hole
		/// rather than with the size of the map.
		///
		/// GUARANTEE. For any cell c, either c was already inside the effective radius of an asset aim
		/// point, or c is uncovered — in which case LatticePointFor(c) is in this list, and each strip is
		/// at most `spacing` wide so c is within spacing/sqrt(2) &lt;= effectiveRadius of it. Every cell is
		/// therefore covered by construction, which <see cref="UncoveredCells"/> then re-checks
		/// empirically in the test rather than taking this paragraph's word for it.
		/// </summary>
		public static List<CPos> FillPoints(Rectangle bounds, IReadOnlyList<CPos> uncovered, int spacing)
		{
			var points = new List<CPos>();
			foreach (var c in uncovered)
			{
				var p = LatticePointFor(c, bounds, spacing);
				var seen = false;
				foreach (var q in points)
				{
					if (q == p)
					{
						seen = true;
						break;
					}
				}

				if (!seen)
					points.Add(p);
			}

			return points;
		}

		/// <summary>
		/// Draw a displacement inside a DISC of radius <paramref name="boundCells"/> from two integers the
		/// caller pulled off the synced RNG.
		///
		/// Sampling radius and angle separately, rather than two independent axis offsets, is what makes
		/// the magnitude bound exact instead of approximate: a square sample reaches bound*sqrt(2) at its
		/// corners and would quietly overrun the coverage margin on the diagonal — the one direction
		/// nobody spot-checks.
		/// </summary>
		/// <param name="angleSample">Any integer; reduced modulo a full turn.</param>
		/// <param name="radiusSample">Any non-negative integer; reduced modulo (boundCells + 1).</param>
		public static Jitter DiscJitter(int angleSample, int radiusSample, int boundCells)
		{
			if (boundCells <= 0)
				return new Jitter(0, 0);

			var r = radiusSample % (boundCells + 1);
			if (r < 0)
				r += boundCells + 1;

			var a = new WAngle(angleSample & 1023);

			// Cos/Sin are 1024-scaled. Truncation toward zero keeps |offset| <= r <= boundCells, so the
			// disc bound holds on both axes without a further clamp.
			var x = (int)((long)r * a.Cos() / 1024);
			var y = (int)((long)r * a.Sin() / 1024);
			return new Jitter(x, y);
		}

		/// <summary>
		/// Timing parameters for <see cref="BuildSchedule"/>. All values are in ticks.
		/// </summary>
		public readonly struct ScheduleTimings
		{
			/// <summary>Gap between consecutive impacts inside one wave — tight, so a wave reads as a group.</summary>
			public readonly int WithinWave;

			/// <summary>
			/// THE PAUSE. The gap between the last outlier impact and the first city impact, and the single
			/// most important number in the sequence: long enough that the viewer has concluded it is over,
			/// short enough that the whole salvo stays inside "a few seconds".
			/// </summary>
			public readonly int OutlierToCityPause;

			/// <summary>Gap between the last city impact and the first fill impact.</summary>
			public readonly int CityToFillPause;

			public ScheduleTimings(int withinWave, int outlierToCityPause, int cityToFillPause)
			{
				WithinWave = Math.Max(0, withinWave);
				OutlierToCityPause = Math.Max(0, outlierToCityPause);
				CityToFillPause = Math.Max(0, cityToFillPause);
			}
		}

		/// <summary>
		/// Turn tier-tagged aim points into scheduled impacts: arrival offsets, relative to the first
		/// impact, that group each tier into a tight wave and separate the tiers by the pauses above.
		///
		/// The input order within a tier is preserved, so the caller's priority order is also the arrival
		/// order. Tiers are emitted in <see cref="DoomsdayTier"/> declaration order — outliers, then
		/// cities, then fill.
		/// </summary>
		public static List<DoomsdayImpact> BuildSchedule(
			IReadOnlyList<CPos> cells, IReadOnlyList<DoomsdayTier> tiers, ScheduleTimings timings)
		{
			var impacts = new List<DoomsdayImpact>();
			var clock = 0;
			var emittedAny = false;

			// Explicit tier order rather than a group-by: a LINQ GroupBy would be deterministic here, but
			// walking the enum in declaration order says the sequence out loud and cannot be reordered by
			// an unrelated edit to the input list.
			var order = new[] { DoomsdayTier.Outlier, DoomsdayTier.City, DoomsdayTier.Fill };
			foreach (var tier in order)
			{
				var first = true;
				for (var i = 0; i < cells.Count; i++)
				{
					if (tiers[i] != tier)
						continue;

					if (first)
					{
						// The pause is charged on entry to a tier, and only when something has already
						// landed — so an empty outlier wave does not leave dead air before the cities.
						if (emittedAny)
							clock += tier == DoomsdayTier.City ? timings.OutlierToCityPause : timings.CityToFillPause;

						first = false;
					}
					else
						clock += timings.WithinWave;

					impacts.Add(new DoomsdayImpact(cells[i], clock, tier));
					emittedAny = true;
				}
			}

			return impacts;
		}
	}
}
