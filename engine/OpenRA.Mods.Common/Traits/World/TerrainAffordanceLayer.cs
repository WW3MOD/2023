#region Copyright & License Information
/*
 * WW3MOD strategic/tactical split — Phase 1, §3b.
 *
 * Static, player-agnostic terrain-affordance field, computed ONCE at map load
 * from the cover substrate (Map.DensityLayer, the same input CohesionMoveModifier
 * reads via CoverScore). It answers two questions per cell, cheaply, at runtime:
 *
 *   1. CoverQuality(cell)  — how good a hide spot this passable cell is
 *      (sum of neighbouring density; a treeline-adjacent cell scores high).
 *   2. IsCoverEdge(cell) + OutwardFacing(cell) — for cells on the EDGE of a
 *      forest/cover cluster, the outward normal (bearing toward open ground).
 *
 * This is what makes the Phase-2 treeline example a lookup instead of a search:
 * "edge cell of this cluster facing the threat direction" becomes
 * "an edge cell whose OutwardFacing points at (or away from) the threat".
 *
 * The outward normal is derived from the local density gradient: sum the
 * neighbour offset vectors weighted by neighbour density → that points INTO the
 * dense mass; negate it to point OUT toward open terrain. A cell surrounded by
 * density on all sides has a near-zero gradient (interior, not an edge); a cell
 * on the boundary has a strong gradient (edge).
 *
 * Fully static + deterministic: identical on every client from map data, so it
 * carries no sync concerns. Integer cell math throughout. NO consumer in Phase 1.
 * See WORKSPACE/plans/260722_strategic_tactical_split_SPEC.md.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("WW3MOD Phase 1 (§3b): static per-cell cover quality + cover-edge orientation.",
		"Computed once at map load from Map.DensityLayer. Pure lookup — no consumer yet",
		"(Phase 2 positioning executor reads it).")]
	public class TerrainAffordanceLayerInfo : TraitInfo
	{
		[Desc("Minimum CoverScore for a cell to count as usable cover.")]
		public readonly int CoverThreshold = 1;

		[Desc("Minimum squared density-gradient magnitude for a cover cell to count",
			"as an EDGE (boundary) cell rather than deep interior. Higher = only",
			"strongly-directional boundaries qualify.")]
		public readonly int EdgeGradientThresholdSq = 1;

		public override object Create(ActorInitializer init) { return new TerrainAffordanceLayer(this); }
	}

	public class TerrainAffordanceLayer : IWorldLoaded
	{
		public readonly TerrainAffordanceLayerInfo Info;

		CellLayer<short> coverQuality;
		CellLayer<bool> isEdge;
		CellLayer<WAngle> outwardFacing;
		Map map;

		public TerrainAffordanceLayer(TerrainAffordanceLayerInfo info)
		{
			Info = info;
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			map = w.Map;

			coverQuality = new CellLayer<short>(map);
			isEdge = new CellLayer<bool>(map);
			outwardFacing = new CellLayer<WAngle>(map);

			// DensityLayer is built at map load (cached in SupportDir, see ShadowCache). Guard anyway:
			// a map with no cover simply yields an all-zero affordance field.
			if (map.DensityLayer == null)
				return;

			Compute();
		}

		static int SafeDensity(Map map, int x, int y)
		{
			if (map.DensityLayer == null || !map.DensityLayer.IsValidCoordinate(x, y))
				return 0;

			return map.DensityLayer[new CPos(x, y)];
		}

		void Compute()
		{
			foreach (var cell in map.AllCells)
			{
				var x = cell.X;
				var y = cell.Y;

				// A cell occupied by dense actors (a tree footprint) is not itself a hide
				// spot — we want the passable cells beside the trees. Mirrors CoverScore.
				if (SafeDensity(map, x, y) > 0)
					continue;

				var score = 0;
				var gradX = 0;
				var gradY = 0;
				for (var dy = -1; dy <= 1; dy++)
				{
					for (var dx = -1; dx <= 1; dx++)
					{
						if (dx == 0 && dy == 0)
							continue;

						var d = SafeDensity(map, x + dx, y + dy);
						if (d == 0)
							continue;

						score += d;

						// Gradient points toward the dense side (offset * density).
						gradX += dx * d;
						gradY += dy * d;
					}
				}

				if (score < Info.CoverThreshold)
					continue;

				coverQuality[cell] = (short)System.Math.Min(score, short.MaxValue);

				// Outward normal = away from the dense mass = negated gradient.
				var magSq = gradX * gradX + gradY * gradY;
				if (magSq >= Info.EdgeGradientThresholdSq)
				{
					isEdge[cell] = true;
					outwardFacing[cell] = new WVec(-gradX, -gradY, 0).Yaw;
				}
			}
		}

		// ---------- Public query API ----------

		/// <summary>Cover score (summed neighbour density) at a passable cell; 0 if none/off-map.</summary>
		public int CoverQuality(CPos cell)
		{
			return coverQuality != null && coverQuality.Contains(cell) ? coverQuality[cell] : 0;
		}

		/// <summary>True if this is a boundary (edge) cell of a cover cluster.</summary>
		public bool IsCoverEdge(CPos cell)
		{
			return isEdge != null && isEdge.Contains(cell) && isEdge[cell];
		}

		/// <summary>For an edge cell, the outward normal (bearing toward open ground).
		/// WAngle.Zero for non-edge/off-map cells — always pair with IsCoverEdge.</summary>
		public WAngle OutwardFacing(CPos cell)
		{
			return outwardFacing != null && outwardFacing.Contains(cell) ? outwardFacing[cell] : WAngle.Zero;
		}
	}
}
