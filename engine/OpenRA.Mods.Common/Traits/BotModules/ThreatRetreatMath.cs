#region Copyright & License Information
/*
 * WW3MOD — where a losing ground squad runs to (pure math).
 *
 * PERCEIVED BEHAVIOUR: a squad that breaks off retreats to ground it can actually stand on, instead of
 * picking the open sea and then not moving at all.
 *
 * WHY THIS IS EXTRACTED. The selection lived inside ThreatMapManager, which needs a World, so the one
 * property that matters could not be pinned without mounting one. The property is not about the scoring
 * — it is about WHICH CELLS ARE ELIGIBLE, and that is expressible over an injected oracle.
 *
 * DETERMINISM (influence-stack invariant): zero random draws; the scan order is fixed (dx outer, dy inner)
 * and ties are resolved by strict `>` so the first cell in that order wins. Two clients over the same
 * synced threat field choose the identical cell.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class ThreatRetreatMath
	{
		/// <summary>The best-scoring cell to fall back to, over the coarse threat grid around
		/// <paramref name="from"/>. Score is the negated threat at the cell minus a tenth of the distance from
		/// <paramref name="from"/>, so a squad prefers quiet ground without running across the map for it.
		///
		/// <para>WHY <paramref name="passable"/> IS NOT OPTIONAL, and why this is the one guard that cannot be
		/// left to the engine. The score is <c>-threat</c>, and threat is <c>enemyValue - friendlyValue</c> — so
		/// an EMPTY cell scores <c>-0</c>, beating any cell with a live enemy near it. Open water is the emptiest
		/// terrain on any map, which makes it a structurally attractive retreat: the metric does not merely
		/// tolerate the sea, it PREFERS it. That is the same shape as the spread-slot defect, where the only
		/// instrument watching counted distance from the supply route and a unit walking into the sea improved
		/// the number. Here the bad answer is the winning answer.</para>
		///
		/// <para>The engine does not save this one. A "Move" order is relocated to the nearest movable cell
		/// within 10 cells (Mobile.cs:1030 -> Mobile.NearestMoveableCell), so a retreat aimed a short way
		/// offshore lands on the beach; but a retreat aimed at open water further out than that resolves to no
		/// destination at all and the whole fleeing squad stands still and dies (GroundStates.cs:290 issues the
		/// order to every unit in the squad).</para></summary>
		public static CPos ChooseSafestCell(CPos from, int fromGridX, int fromGridY,
			int gridWidth, int gridHeight, int cellSize, int searchRadius,
			Func<CPos, float> threatAt, Func<CPos, bool> inBounds, Func<CPos, bool> passable)
		{
			if (threatAt == null)
				throw new ArgumentNullException(nameof(threatAt));

			if (passable == null)
				throw new ArgumentNullException(nameof(passable), "a retreat cell must be terrain-tested for the units being sent to it");

			// Two arguments to the caller, one oracle to the scan: `inBounds` alone is what shipped, and a
			// caller that can only supply bounds must now say so with an explicit all-true `passable`.
			bool Standable(CPos c) => (inBounds == null || inBounds(c)) && passable(c);

			var bestCell = from;
			var bestScore = float.MinValue;

			for (var dx = -searchRadius; dx <= searchRadius; dx++)
			{
				for (var dy = -searchRadius; dy <= searchRadius; dy++)
				{
					var gx = fromGridX + dx;
					var gy = fromGridY + dy;
					if (gx < 0 || gx >= gridWidth || gy < 0 || gy >= gridHeight)
						continue;

					var mapCell = new CPos(gx * cellSize + cellSize / 2, gy * cellSize + cellSize / 2);
					if (!Standable(mapCell))
						continue;

					var score = -threatAt(mapCell);

					// Prefer cells closer to the start position (don't retreat across the map).
					var dist = (mapCell - from).Length;
					score -= dist * 0.1f;

					if (score > bestScore)
					{
						bestScore = score;
						bestCell = mapCell;
					}
				}
			}

			return bestCell;
		}
	}
}
