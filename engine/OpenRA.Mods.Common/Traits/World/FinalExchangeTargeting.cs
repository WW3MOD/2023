#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * WHERE AN UNPLACED PACKAGE GOES, as a pure function over cells.
 *
 * ==== WHAT REPLACED DEAD HAND, AND WHY THE OLD SHAPE HAD TO GO ====
 * Dead Hand built ONE map-wide salvo over every derrick, Supply Route and city on the map and fired
 * it with no owner and no notion of sides. It therefore bombed the player who had just declined to
 * aim as thoroughly as it bombed their enemy, which is not a retaliatory launch, it is weather. The
 * replacement is per-SIDE: a side that places nothing fires ITS OWN package, from ITS OWN
 * game-ender, at the OTHER side's half of the map.
 *
 * ==== FOUR TIERS, IN PRIORITY ORDER, AND THE PACKAGE IS ALWAYS FULL ====
 *   1. The enemy's SUPPLY ROUTES. One per player and the thing the whole mod is about; nothing
 *      outranks them.
 *   2. The enemy's largest CONCENTRATIONS -- single-linkage clusters of their own actors, ranked by
 *      member count. DoomsdayMath.ClusterAssets does the clustering and is unchanged; what changed
 *      is that it is now run over one side's actors instead of over the whole map.
 *   3. Neutral HIGH-VALUE assets standing on the enemy's side. A derrick the enemy is drawing on is
 *      a target whoever nominally owns it.
 *   4. SPREAD points, by farthest-point sampling over the enemy's remaining ground. This tier is
 *      what makes "always full" true: a package is sized from the MAP (FinalExchangePackage) and a
 *      side with two buildings left must still deliver all of it.
 *
 * ==== THE SIDE CLASSIFIER IS INJECTED, AND THAT IS NOT A TESTING CONVENIENCE ====
 * The real classifier is DefconWall's, which is being made public on a sibling branch. Taking it as
 * two delegates means this file can be written, reviewed and pinned by NUnit before that lands, and
 * it means the one thing this file must never do -- keep its own second copy of where the border is
 * -- is impossible by construction. HomeProximitySide below is the fallback for a map with no
 * border at all, and it is in this file rather than in the trait so that it is tested too.
 *
 * ==== DETERMINISM ====
 * Integer only, no RNG, no float, no dictionary or set enumeration. Every ordering decision has an
 * explicit final tie-break on input index, because List.Sort is NOT a stable sort and a tie broken
 * by the sort's internal pivot choice is a tie broken differently on different runtimes. The caller
 * hands in assets in a deterministic order (DoomsdayStrike sorts by ActorID) and the cell sweep
 * below walks the bounds rectangle in a fixed row-major order.
 */

using System;
using System.Collections.Generic;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Priority bands for <see cref="FinalExchangeTargeting.Choose"/>. Lower is aimed at first.</summary>
	public enum FinalExchangeTier
	{
		SupplyRoute = 0,
		Concentration = 1,
		NeutralAsset = 2,
	}

	/// <summary>One thing worth a warhead, and how badly.</summary>
	public readonly struct FinalExchangeAsset
	{
		public readonly CPos Cell;
		public readonly FinalExchangeTier Tier;

		/// <summary>Rank WITHIN the tier, higher first. Cluster member count, or 0 where a tier has no ranking.</summary>
		public readonly int Weight;

		public FinalExchangeAsset(CPos cell, FinalExchangeTier tier, int weight)
		{
			Cell = cell;
			Tier = tier;
			Weight = weight;
		}
	}

	public static class FinalExchangeTargeting
	{
		/// <summary>
		/// <para>Aim points for one side's package: up to <paramref name="count"/> cells, all on the
		/// enemy's side of the border and none inside the band.</para>
		///
		/// <para>THE SEPARATION RULE APPLIES TO THE ASSET TIERS AND NOT TO THE PADDING, and the
		/// asymmetry is deliberate. An asset closer than <paramref name="minSeparationCells"/> to
		/// something already aimed at is DROPPED, because a second warhead there buys nothing --
		/// that is the same argument <see cref="MissileStrikePowerInfo.AimPointRadius"/> makes to the
		/// player during placement, and the caller passes that very number. Padding cannot drop
		/// anything, because its job is to fill the package; farthest-point sampling maximises the
		/// separation it can achieve rather than refusing the points it cannot.</para>
		///
		/// <para>Returns fewer than <paramref name="count"/> ONLY when the enemy side contains no
		/// cells at all to spread over -- a degenerate classifier, or a border that put the whole
		/// map on one side. The caller decides what to do about that; this does not invent cells.</para>
		/// </summary>
		/// <param name="count">Package size, from <see cref="FinalExchangePackage.SizeFor"/>.</param>
		/// <param name="bounds">The playable rectangle. Padding is drawn from inside it and nowhere else.</param>
		/// <param name="assets">Candidate targets, in a caller-fixed deterministic order.</param>
		/// <param name="sideOf">Which side a cell belongs to. DefconWall's classifier, or <see cref="HomeProximitySide"/>.</param>
		/// <param name="inBand">Whether a cell is inside the border band itself. Band cells are never aimed at.</param>
		/// <param name="enemySide">The side value that counts as "theirs".</param>
		/// <param name="minSeparationCells">Minimum gap between two aimed assets.</param>
		public static List<CPos> Choose(
			int count,
			Rectangle bounds,
			IReadOnlyList<FinalExchangeAsset> assets,
			Func<CPos, int> sideOf,
			Func<CPos, bool> inBand,
			int enemySide,
			int minSeparationCells)
		{
			var chosen = new List<CPos>();
			if (count <= 0)
				return chosen;

			bool OnEnemyGround(CPos c)
			{
				if (c.X < bounds.Left || c.X >= bounds.Right || c.Y < bounds.Top || c.Y >= bounds.Bottom)
					return false;

				if (inBand != null && inBand(c))
					return false;

				// A null classifier is the "no border on this map" case and puts the whole playable
				// rectangle in play, which is the only honest answer when there are no sides to be on.
				return sideOf == null || sideOf(c) == enemySide;
			}

			// ---- 1-3. THE ASSET TIERS, in priority order with an explicit tie-break on input index.
			var order = new List<int>();
			if (assets != null)
				for (var i = 0; i < assets.Count; i++)
					if (OnEnemyGround(assets[i].Cell))
						order.Add(i);

			order.Sort((a, b) =>
			{
				var tier = ((int)assets[a].Tier).CompareTo((int)assets[b].Tier);
				if (tier != 0)
					return tier;

				// Descending weight: the biggest concentration is aimed at first.
				var weight = assets[b].Weight.CompareTo(assets[a].Weight);
				return weight != 0 ? weight : a.CompareTo(b);
			});

			var minSq = (long)minSeparationCells * minSeparationCells;
			foreach (var i in order)
			{
				if (chosen.Count >= count)
					break;

				var cell = assets[i].Cell;
				var clear = true;
				foreach (var k in chosen)
				{
					if (DistanceSquared(cell, k) < minSq)
					{
						clear = false;
						break;
					}
				}

				if (clear)
					chosen.Add(cell);
			}

			if (chosen.Count >= count)
				return chosen;

			// ---- 4. PADDING. Row-major over the enemy's ground, so the candidate list is the same
			// list on every client before a single distance is measured.
			var spread = new List<CPos>();
			for (var y = bounds.Top; y < bounds.Bottom; y++)
			{
				for (var x = bounds.Left; x < bounds.Right; x++)
				{
					var c = new CPos(x, y);
					if (OnEnemyGround(c))
						spread.Add(c);
				}
			}

			PadByFarthestPoint(chosen, spread, count);

			return chosen;
		}

		/// <summary>
		/// <para>Grow <paramref name="chosen"/> to <paramref name="count"/> by repeatedly taking the
		/// candidate FURTHEST from everything already chosen -- the standard farthest-point traversal,
		/// which is what spreads a handful of points over an arbitrary region without a lattice and
		/// without a random draw.</para>
		///
		/// <para>COST IS count * candidates, and both are small: count is at most MaxPackage and
		/// candidates is at most one map's worth of cells (16,384 on the largest shipped map). It
		/// runs once per side per match, at the close of the window.</para>
		/// </summary>
		// TIES GO TO THE LOWEST INDEX, i.e. to the earliest cell of the row-major sweep, because the
		// comparison below is `>` and not `>=`. On an empty `chosen` every candidate scores
		// long.MaxValue and the first cell of the sweep wins, which is a defined answer rather than
		// an arbitrary one.
		static void PadByFarthestPoint(List<CPos> chosen, IReadOnlyList<CPos> candidates, int count)
		{
			if (candidates.Count == 0)
				return;

			var taken = new bool[candidates.Count];

			while (chosen.Count < count)
			{
				var bestIndex = -1;
				var bestScore = -1L;

				for (var i = 0; i < candidates.Count; i++)
				{
					if (taken[i])
						continue;

					var score = long.MaxValue;
					foreach (var c in chosen)
					{
						var d = DistanceSquared(candidates[i], c);
						if (d < score)
							score = d;
					}

					if (score > bestScore)
					{
						bestScore = score;
						bestIndex = i;
					}
				}

				// Every candidate already taken. The package is short and the caller is told so by
				// the returned count; inventing a duplicate aim point would hide it.
				if (bestIndex < 0)
					return;

				taken[bestIndex] = true;
				chosen.Add(candidates[bestIndex]);
			}
		}

		/// <summary>
		/// <para>THE FALLBACK CLASSIFIER, for a map with no DEFCON border: a cell belongs to whichever
		/// side owns the nearest home location. Ties go to the first home in the list, which is seat
		/// order and therefore identical on every client.</para>
		///
		/// <para>It is a Voronoi split by spawn point, which is the same thing
		/// DefconWallGeometry.BisectorOfSides derives its LINE from -- so on a two-player map this
		/// agrees with the real border almost everywhere, and where it does not, it still puts a
		/// player's own base firmly on their own side. That is the property the targeting needs.</para>
		/// </summary>
		/// <param name="homes">One home cell per side, indexed by the side value this returns.</param>
		/// <param name="cell">The cell to classify.</param>
		public static int HomeProximitySide(IReadOnlyList<CPos> homes, CPos cell)
		{
			var best = -1;
			var bestSq = long.MaxValue;
			for (var i = 0; i < homes.Count; i++)
			{
				var d = DistanceSquared(homes[i], cell);
				if (d < bestSq)
				{
					bestSq = d;
					best = i;
				}
			}

			return best;
		}

		/// <summary>Squared cell distance in long, so nothing overflows and nothing takes a root.</summary>
		static long DistanceSquared(CPos a, CPos b)
		{
			long dx = a.X - b.X;
			long dy = a.Y - b.Y;
			return (dx * dx) + (dy * dy);
		}
	}
}
