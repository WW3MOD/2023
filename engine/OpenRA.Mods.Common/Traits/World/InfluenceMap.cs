#region Copyright & License Information
/*
 * WW3MOD InfluenceMap — Stage A.1 of the doctrine roadmap.
 *
 * Per-cell military-value grid. One layer per combatant player; per-perspective
 * friendly/enemy/frontline views are derived on query. Shared across all
 * AI decisions — every behaviour that needs to ask "where is the enemy"
 * reads from this single source instead of doing its own world scan.
 *
 * Coexists with the older ThreatMapManager (8-cell blocks, separate
 * military/economic grids, scans per query). ThreatMapManager stays for
 * legacy consumers; new doctrine-aware bot modules should use this.
 *
 * Granularity is intentionally fine (default 2-cell blocks) — Stage A
 * needs an overlay that visibly tracks contact, which 8-cell blocks
 * smear too much. Perf budget at 2-cell granularity: roughly 33x17
 * = 561 cells on a 66x34 map, refreshed every 25 ticks → ~22 cells/tick
 * for the dominant player. Negligible.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Per-player military influence grid + frontline derivation.",
		"Shared layer for doctrine-aware AI modules (see WORKSPACE/ai/doctrine.md).")]
	public class InfluenceMapInfo : TraitInfo
	{
		[Desc("Size of each grid cell in map tiles. Smaller = sharper but slower.")]
		public readonly int CellSize = 2;

		[Desc("Ticks between full grid recomputations.")]
		public readonly int UpdateInterval = 25;

		[Desc("Radius (in grid cells) over which a single actor's value spreads.")]
		public readonly int ContributionRadius = 3;

		[Desc("Per-actor base value scaled down by this divisor before being summed",
			"into the grid. Keeps grid integers small enough to avoid overflow on",
			"crowded maps.")]
		public readonly int ValueDivisor = 100;

		public override object Create(ActorInitializer init) { return new InfluenceMap(init.Self, this); }
	}

	public class InfluenceMap : ITick, IWorldLoaded
	{
		public readonly InfluenceMapInfo Info;
		readonly World world;

		int gridWidth;
		int gridHeight;
		int updateCountdown;

		// One layer per combatant player. Rebuilt every UpdateInterval ticks.
		readonly Dictionary<Player, int[,]> playerLayers = new();

		public InfluenceMap(Actor self, InfluenceMapInfo info)
		{
			Info = info;
			world = self.World;
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			var map = w.Map;
			gridWidth = (map.MapSize.X + Info.CellSize - 1) / Info.CellSize;
			gridHeight = (map.MapSize.Y + Info.CellSize - 1) / Info.CellSize;

			// Stagger initial fire so multiple world traits don't all recompute on the same tick.
			updateCountdown = w.SharedRandom.Next(0, Info.UpdateInterval);
		}

		void ITick.Tick(Actor self)
		{
			if (--updateCountdown > 0)
				return;

			updateCountdown = Info.UpdateInterval;
			Recompute();
		}

		void Recompute()
		{
			// Reset existing layers and lazily allocate for new players (lobby joins are rare,
			// but a fresh layer per RefreshLayer call is cheap enough).
			foreach (var layer in playerLayers.Values)
				System.Array.Clear(layer, 0, layer.Length);

			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null)
					continue;
				if (actor.Owner.NonCombatant || actor.Owner.Spectating)
					continue;

				// Only military actors contribute to the influence layer. Economic
				// buildings live on a separate (future) layer.
				if (!actor.Info.HasTraitInfo<AttackBaseInfo>() && !actor.Info.HasTraitInfo<AutoTargetInfo>())
					continue;

				var sellValue = actor.GetSellValue();
				if (sellValue <= 0)
					continue;

				if (!playerLayers.TryGetValue(actor.Owner, out var layer))
				{
					layer = new int[gridWidth, gridHeight];
					playerLayers[actor.Owner] = layer;
				}

				var (gx, gy) = MapCellToGridCell(actor.Location);
				var scaledValue = sellValue / Info.ValueDivisor;
				if (scaledValue < 1)
					scaledValue = 1;

				InfluenceMapMath.AddContribution(layer, gx, gy, scaledValue, Info.ContributionRadius);
			}
		}

		// ---------- Public query API ----------

		/// <summary>Grid cell containing the given map cell.</summary>
		public (int X, int Y) MapCellToGridCell(CPos mapCell)
		{
			var gx = InfluenceGridMath.MapToGrid(Info.CellSize, mapCell.X);
			var gy = InfluenceGridMath.MapToGrid(Info.CellSize, mapCell.Y);
			return (gx, gy);
		}

		/// <summary>
		/// Map cell at the CENTRE of the given grid cell. Deliberately NOT an inverse of
		/// <see cref="MapCellToGridCell"/> — read the note on <see cref="InfluenceGridMath"/> before
		/// composing the two.
		/// </summary>
		public CPos GridCellToMapCell(int gx, int gy)
		{
			return new CPos(
				InfluenceGridMath.GridToMapCentre(Info.CellSize, gx),
				InfluenceGridMath.GridToMapCentre(Info.CellSize, gy));
		}

		public int GridWidth => gridWidth;
		public int GridHeight => gridHeight;

		/// <summary>Snapshot of friendly influence (player's own + allies) from a perspective.</summary>
		public int[,] GetFriendlyInfluence(Player perspective)
		{
			var result = new int[gridWidth, gridHeight];
			foreach (var (owner, layer) in playerLayers)
			{
				if (owner == perspective || perspective.RelationshipWith(owner) == PlayerRelationship.Ally)
					AddInto(result, layer);
			}

			return result;
		}

		/// <summary>Snapshot of enemy influence from a perspective.</summary>
		public int[,] GetEnemyInfluence(Player perspective)
		{
			var result = new int[gridWidth, gridHeight];
			foreach (var (owner, layer) in playerLayers)
			{
				if (perspective.RelationshipWith(owner) == PlayerRelationship.Enemy)
					AddInto(result, layer);
			}

			return result;
		}

		/// <summary>Frontline cells from a perspective: cells where both friendly and enemy
		/// have non-zero influence. Bool array, gridWidth x gridHeight.</summary>
		public bool[,] GetFrontline(Player perspective)
		{
			var friendly = GetFriendlyInfluence(perspective);
			var enemy = GetEnemyInfluence(perspective);
			return InfluenceMapMath.DeriveFrontline(friendly, enemy);
		}

		/// <summary>Frontline ignoring perspective — every cell where any two enemies of each
		/// other both have influence. Used by spectator/observer overlays.</summary>
		public bool[,] GetFrontlineAnyPerspective()
		{
			var result = new bool[gridWidth, gridHeight];

			// For each pair of enemy players, mark cells where both have influence.
			var owners = new List<Player>(playerLayers.Keys);
			for (var i = 0; i < owners.Count; i++)
			{
				for (var j = i + 1; j < owners.Count; j++)
				{
					if (owners[i].RelationshipWith(owners[j]) != PlayerRelationship.Enemy)
						continue;

					var a = playerLayers[owners[i]];
					var b = playerLayers[owners[j]];
					for (var x = 0; x < gridWidth; x++)
						for (var y = 0; y < gridHeight; y++)
							if (a[x, y] > 0 && b[x, y] > 0)
								result[x, y] = true;
				}
			}

			return result;
		}

		static void AddInto(int[,] target, int[,] source)
		{
			var w = target.GetLength(0);
			var h = target.GetLength(1);
			for (var x = 0; x < w; x++)
				for (var y = 0; y < h; y++)
					target[x, y] += source[x, y];
		}
	}

	// ============================================================
	// Pure math — testable without a World context.
	// ============================================================
	public static class InfluenceMapMath
	{
		/// <summary>Spread `value` from (gx, gy) over a Manhattan-radius disc, with linear
		/// falloff: full value at centre, zero just past the radius.</summary>
		public static void AddContribution(int[,] grid, int gx, int gy, int value, int radius)
		{
			var w = grid.GetLength(0);
			var h = grid.GetLength(1);

			for (var dx = -radius; dx <= radius; dx++)
			{
				for (var dy = -radius; dy <= radius; dy++)
				{
					var dist = System.Math.Abs(dx) + System.Math.Abs(dy);
					if (dist > radius)
						continue;

					var x = gx + dx;
					var y = gy + dy;
					if (x < 0 || x >= w || y < 0 || y >= h)
						continue;

					// Linear falloff: full at centre, 1/(radius+1) at the edge.
					var contribution = value * (radius - dist + 1) / (radius + 1);
					if (contribution > 0)
						grid[x, y] += contribution;
				}
			}
		}

		/// <summary>Frontline cells = both layers have non-zero influence.</summary>
		public static bool[,] DeriveFrontline(int[,] friendly, int[,] enemy)
		{
			var w = friendly.GetLength(0);
			var h = friendly.GetLength(1);
			var result = new bool[w, h];

			for (var x = 0; x < w; x++)
				for (var y = 0; y < h; y++)
					if (friendly[x, y] > 0 && enemy[x, y] > 0)
						result[x, y] = true;

			return result;
		}

		/// <summary>Convenience: count frontline cells. Useful for tournament watcher diagnostics.</summary>
		public static int CountFrontlineCells(bool[,] frontline)
		{
			var w = frontline.GetLength(0);
			var h = frontline.GetLength(1);
			var count = 0;
			for (var x = 0; x < w; x++)
				for (var y = 0; y < h; y++)
					if (frontline[x, y])
						count++;
			return count;
		}
	}
}
