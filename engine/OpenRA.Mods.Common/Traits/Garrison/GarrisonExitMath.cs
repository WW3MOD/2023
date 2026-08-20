#region Copyright & License Information
/*
 * WW3MOD garrison exit-cell choice — which door a soldier ordered out of a building walks out of.
 *
 * The player's request is "leave in the direction I told him to go", and there are two readings of
 * that. A STRICT BEARING test picks the cell whose angle from the building best matches the angle to
 * the destination; NEAREST-TO-DESTINATION picks the free cell with the shortest remaining walk. This
 * implements the second, for three reasons:
 *
 *   - For any destination outside the building's own footprint the two agree, because the adjacent
 *     ring is a single cell thick: the cell on the destination's side IS the closest one. They can
 *     only disagree for a destination essentially on top of the building, where the bearing is noise.
 *   - It cannot pick a worse cell. A bearing test is free to choose a correctly-angled cell that is
 *     further from where he was actually sent; distance cannot do that by construction.
 *   - It degrades honestly when the obvious door is blocked. The candidate set is already filtered to
 *     free cells, so a boxed-in north side yields the next-closest opening rather than nothing.
 *
 * DETERMINISM. This runs inside order resolution on every client, so ties must not be broken by
 * enumeration order or by SharedRandom. Equal distances are settled on (X, then Y), which is total
 * over distinct cells and independent of how the candidate sequence was built.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class GarrisonExitMath
	{
		/// <summary>
		/// Picks the cell in <paramref name="candidates"/> that <paramref name="isFree"/> accepts and
		/// that lies closest to <paramref name="destination"/>. Returns null when nothing is free.
		/// </summary>
		public static CPos? ChooseExitCell(IEnumerable<CPos> candidates, CPos destination, Func<CPos, bool> isFree)
		{
			if (candidates == null)
				return null;

			CPos? best = null;
			var bestScore = 0L;

			foreach (var c in candidates)
			{
				if (!isFree(c))
					continue;

				// long, because a cell delta squared on a 256-cell map still fits an int but the sum on a
				// pathological destination need not; the cast costs nothing and removes the question.
				var dx = (long)c.X - destination.X;
				var dy = (long)c.Y - destination.Y;
				var score = (dx * dx) + (dy * dy);

				if (best == null || score < bestScore || (score == bestScore && IsLower(c, best.Value)))
				{
					bestScore = score;
					best = c;
				}
			}

			return best;
		}

		static bool IsLower(CPos a, CPos b)
		{
			return a.X != b.X ? a.X < b.X : a.Y < b.Y;
		}
	}
}
