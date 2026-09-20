#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * HOW MANY WARHEADS ONE SIDE GETS IN THE FINAL EXCHANGE, as arithmetic over the map.
 *
 * ==== WHY THE MAP AND NOT A NUMBER IN YAML ====
 * Decision 20 retired the host-facing "Game-enders" control with the note that it "was arithmetic:
 * one per ~1340 cells of map, split between sides. Offering it invited a host to override a
 * calculation the engine already does correctly." This file IS that calculation. It was described
 * in the ruling and never built: MissileStrikePowerInfo.AimPoints stayed at a static 6 for the
 * Sarmat and a static 1 for the B83, so arena-tank-duel (2048 playable cells) and x-lake (16384)
 * got the same six-warhead package from Russia and the same single bomb from America.
 *
 * ==== ONE NUMBER, BOTH NATIONS ====
 * The package is symmetric by construction -- the same N is handed to whichever game-ender a side
 * holds -- which is the half of the old shape that was actually wrong. It was not that six was too
 * many; it was that six against one is not an exchange.
 *
 * ==== ROUNDING IS TO NEAREST, INTEGER-ONLY ====
 * (cells + K/2) / K is round-half-up in integer arithmetic. No float appears anywhere on this path:
 * the result decides how many warheads exist and is therefore simulation state, which must be
 * byte-identical on every client (conventions.md; DefconWallGeometry.cs's header for the long
 * version of why).
 */

namespace OpenRA.Mods.Common.Traits
{
	public static class FinalExchangePackage
	{
		/// <summary>
		/// <para>Warheads one side's game-ender delivers on a map of <paramref name="playableCells"/>
		/// cells, clamped to [<paramref name="minPackage"/>, <paramref name="maxPackage"/>].</para>
		///
		/// <para>PLAYABLE CELLS, NOT MAP SIZE. The caller passes Map.Bounds.Width * Map.Bounds.Height --
		/// the rectangle a unit can actually stand in -- rather than MapSize, which includes the
		/// border margin every OpenRA map carries and which would inflate a small map's package.</para>
		///
		/// <para>DEFENSIVE AGAINST A ZERO OR NEGATIVE DIVISOR rather than trusting YAML: a
		/// <paramref name="cellsPerImpact"/> of 0 would be a divide-by-zero on the world actor's
		/// construction, which is a crash before any map loads rather than a mis-sized package.</para>
		/// </summary>
		public static int SizeFor(int playableCells, int cellsPerImpact, int minPackage, int maxPackage)
		{
			// The clamp bounds are normalised first, so a YAML file with Min above Max gets Min
			// rather than a clamp that cannot be satisfied and returns whichever bound ran last.
			var min = minPackage < 1 ? 1 : minPackage;
			var max = maxPackage < min ? min : maxPackage;

			if (cellsPerImpact <= 0 || playableCells <= 0)
				return min;

			var raw = (playableCells + cellsPerImpact / 2) / cellsPerImpact;

			if (raw < min)
				return min;

			return raw > max ? max : raw;
		}
	}
}
