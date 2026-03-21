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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Maintains a coarse influence/threat grid for AI decision-making.",
		"Tracks friendly and enemy military value, economic value, and exploration age per cell.")]
	public class ThreatMapManagerInfo : TraitInfo
	{
		[Desc("Size of each threat map cell in map tiles. Smaller = more precise but slower.")]
		public readonly int CellSize = 8;

		[Desc("Number of ticks between full threat map recalculations.")]
		public readonly int UpdateInterval = 90;

		[Desc("Influence spread to adjacent cells as a fraction of the source cell (0.0 to 1.0).")]
		public readonly float SpreadFactor = 0.3f;

		public override object Create(ActorInitializer init) { return new ThreatMapManager(init.Self, this); }
	}

	public class ThreatMapManager : ITick, IWorldLoaded
	{
		readonly ThreatMapManagerInfo info;
		readonly World world;

		int gridWidth;
		int gridHeight;

		// Per-player threat layers: player index -> grid
		// Military value (units + armed buildings)
		float[,] militaryGrid;

		// Economic value (production buildings, logistics)
		float[,] economicGrid;

		// Tick when each cell was last observed by any player (for exploration scoring)
		int[,] lastExploredTick;

		int updateCountdown;

		public ThreatMapManager(Actor self, ThreatMapManagerInfo info)
		{
			this.info = info;
			world = self.World;
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			var map = w.Map;
			gridWidth = (map.MapSize.X + info.CellSize - 1) / info.CellSize;
			gridHeight = (map.MapSize.Y + info.CellSize - 1) / info.CellSize;

			militaryGrid = new float[gridWidth, gridHeight];
			economicGrid = new float[gridWidth, gridHeight];
			lastExploredTick = new int[gridWidth, gridHeight];

			updateCountdown = world.SharedRandom.Next(0, info.UpdateInterval);
		}

		void ITick.Tick(Actor self)
		{
			if (--updateCountdown <= 0)
			{
				updateCountdown = info.UpdateInterval;
				RecalculateThreatMap();
			}
		}

		void RecalculateThreatMap()
		{
			// Clear grids
			Array.Clear(militaryGrid, 0, militaryGrid.Length);
			Array.Clear(economicGrid, 0, economicGrid.Length);

			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null || actor.Owner.NonCombatant)
					continue;

				var valuedInfo = actor.Info.TraitInfoOrDefault<ValuedInfo>();
				var cost = valuedInfo?.Cost ?? 0;
				if (cost <= 0)
					continue;

				var gx = actor.Location.X / info.CellSize;
				var gy = actor.Location.Y / info.CellSize;
				if (gx < 0 || gx >= gridWidth || gy < 0 || gy >= gridHeight)
					continue;

				// Determine if this is a military or economic actor
				var isMilitary = actor.Info.HasTraitInfo<AttackBaseInfo>() ||
					actor.Info.HasTraitInfo<AutoTargetInfo>();
				var isEconomic = actor.Info.HasTraitInfo<ProductionInfo>() ||
					actor.Info.HasTraitInfo<ProductionFromMapEdgeInfo>() ||
					actor.Info.HasTraitInfo<RearmsUnitsInfo>() ||
					actor.Info.HasTraitInfo<RepairsUnitsInfo>();

				// Scale military value by current health ratio
				var health = actor.TraitOrDefault<IHealth>();
				var healthRatio = health != null ? (float)health.HP / health.MaxHP : 1f;

				if (isMilitary)
				{
					var value = cost * healthRatio;
					AddInfluence(militaryGrid, gx, gy, value);
				}

				if (isEconomic)
					AddInfluence(economicGrid, gx, gy, cost);
			}
		}

		void AddInfluence(float[,] grid, int gx, int gy, float value)
		{
			grid[gx, gy] += value;

			// Spread to adjacent cells
			var spread = value * info.SpreadFactor;
			if (spread < 1f)
				return;

			for (var dx = -1; dx <= 1; dx++)
			{
				for (var dy = -1; dy <= 1; dy++)
				{
					if (dx == 0 && dy == 0)
						continue;

					var nx = gx + dx;
					var ny = gy + dy;
					if (nx >= 0 && nx < gridWidth && ny >= 0 && ny < gridHeight)
						grid[nx, ny] += spread;
				}
			}
		}

		CPos ToGridPos(CPos mapCell)
		{
			return new CPos(
				Math.Clamp(mapCell.X / info.CellSize, 0, gridWidth - 1),
				Math.Clamp(mapCell.Y / info.CellSize, 0, gridHeight - 1));
		}

		// ===== Public Query API =====

		/// <summary>Get the total military value at a map position (all players combined).</summary>
		public float GetMilitaryValue(CPos mapCell)
		{
			var g = ToGridPos(mapCell);
			return militaryGrid[g.X, g.Y];
		}

		/// <summary>Get military value for a specific player at a map position.</summary>
		public float GetPlayerMilitaryValue(CPos mapCell, Player player)
		{
			// For per-player queries, we need to scan actual units in the area
			var worldPos = world.Map.CenterOfCell(mapCell);
			var radius = WDist.FromCells(info.CellSize);
			var value = 0f;

			foreach (var actor in world.FindActorsInCircle(worldPos, radius))
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner != player)
					continue;

				if (!actor.Info.HasTraitInfo<AttackBaseInfo>() && !actor.Info.HasTraitInfo<AutoTargetInfo>())
					continue;

				var valuedInfo = actor.Info.TraitInfoOrDefault<ValuedInfo>();
				if (valuedInfo != null)
				{
					var health = actor.TraitOrDefault<IHealth>();
					var healthRatio = health != null ? (float)health.HP / health.MaxHP : 1f;
					value += valuedInfo.Cost * healthRatio;
				}
			}

			return value;
		}

		/// <summary>Get the threat level at a position from the perspective of a player.
		/// Positive = enemy advantage, negative = friendly advantage.</summary>
		public float GetThreat(CPos mapCell, Player perspective)
		{
			var worldPos = world.Map.CenterOfCell(mapCell);
			var radius = WDist.FromCells(info.CellSize);
			var friendlyValue = 0f;
			var enemyValue = 0f;

			foreach (var actor in world.FindActorsInCircle(worldPos, radius))
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null || actor.Owner.NonCombatant)
					continue;

				if (!actor.Info.HasTraitInfo<AttackBaseInfo>() && !actor.Info.HasTraitInfo<AutoTargetInfo>())
					continue;

				var valuedInfo = actor.Info.TraitInfoOrDefault<ValuedInfo>();
				if (valuedInfo == null)
					continue;

				var health = actor.TraitOrDefault<IHealth>();
				var healthRatio = health != null ? (float)health.HP / health.MaxHP : 1f;
				var value = valuedInfo.Cost * healthRatio;

				var rel = perspective.RelationshipWith(actor.Owner);
				if (rel == PlayerRelationship.Ally || actor.Owner == perspective)
					friendlyValue += value;
				else if (rel == PlayerRelationship.Enemy)
					enemyValue += value;
			}

			return enemyValue - friendlyValue;
		}

		/// <summary>Find the grid cell with the weakest enemy presence (best attack target).
		/// Returns the map cell center of that grid cell.</summary>
		public CPos FindWeakestEnemyCell(Player perspective)
		{
			var bestCell = CPos.Zero;
			var bestScore = float.MaxValue;

			for (var gx = 0; gx < gridWidth; gx++)
			{
				for (var gy = 0; gy < gridHeight; gy++)
				{
					// Check if there's any enemy presence in this cell
					var mapCell = new CPos(gx * info.CellSize + info.CellSize / 2, gy * info.CellSize + info.CellSize / 2);
					if (!world.Map.Contains(mapCell))
						continue;

					var enemyValue = 0f;
					var hasEnemy = false;

					var worldPos = world.Map.CenterOfCell(mapCell);
					foreach (var actor in world.FindActorsInCircle(worldPos, WDist.FromCells(info.CellSize)))
					{
						if (actor.IsDead || !actor.IsInWorld || actor.Owner == null)
							continue;

						if (perspective.RelationshipWith(actor.Owner) != PlayerRelationship.Enemy)
							continue;

						hasEnemy = true;
						var valuedInfo = actor.Info.TraitInfoOrDefault<ValuedInfo>();
						if (valuedInfo != null)
							enemyValue += valuedInfo.Cost;
					}

					if (hasEnemy && enemyValue < bestScore)
					{
						bestScore = enemyValue;
						bestCell = mapCell;
					}
				}
			}

			return bestCell;
		}

		/// <summary>Find the safest retreat cell (highest friendly influence, lowest enemy).
		/// Searches within maxRange cells of the given position.</summary>
		public CPos FindSafestRetreatCell(CPos from, Player perspective, int maxRange = 20)
		{
			var bestCell = from;
			var bestScore = float.MinValue;

			var fromGrid = ToGridPos(from);
			var searchRadius = maxRange / info.CellSize + 1;

			for (var dx = -searchRadius; dx <= searchRadius; dx++)
			{
				for (var dy = -searchRadius; dy <= searchRadius; dy++)
				{
					var gx = fromGrid.X + dx;
					var gy = fromGrid.Y + dy;
					if (gx < 0 || gx >= gridWidth || gy < 0 || gy >= gridHeight)
						continue;

					var mapCell = new CPos(gx * info.CellSize + info.CellSize / 2, gy * info.CellSize + info.CellSize / 2);
					if (!world.Map.Contains(mapCell))
						continue;

					// Score: negative threat is good (means friendly advantage)
					var threat = GetThreat(mapCell, perspective);
					var score = -threat;

					// Prefer cells closer to the start position (don't retreat across the map)
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

		/// <summary>Get the economic value at a map position.</summary>
		public float GetEconomicValue(CPos mapCell)
		{
			var g = ToGridPos(mapCell);
			return economicGrid[g.X, g.Y];
		}

		/// <summary>Mark a cell as explored at the current tick.</summary>
		public void MarkExplored(CPos mapCell)
		{
			var g = ToGridPos(mapCell);
			lastExploredTick[g.X, g.Y] = world.WorldTick;
		}

		/// <summary>Get the staleness of exploration at a cell (ticks since last explored).
		/// Returns int.MaxValue if never explored.</summary>
		public int GetExplorationAge(CPos mapCell)
		{
			var g = ToGridPos(mapCell);
			var lastTick = lastExploredTick[g.X, g.Y];
			if (lastTick == 0)
				return int.MaxValue;

			return world.WorldTick - lastTick;
		}

		/// <summary>Find multiple enemy clusters for multi-axis attacks.
		/// Returns up to maxTargets map cells with enemy presence, sorted by vulnerability
		/// (weakest first), spread apart by at least minSpacing cells.</summary>
		public List<CPos> FindAttackTargets(Player perspective, int maxTargets = 3, int minSpacing = 12)
		{
			var candidates = new List<(CPos Cell, float EnemyValue, float FriendlyValue)>();

			for (var gx = 0; gx < gridWidth; gx++)
			{
				for (var gy = 0; gy < gridHeight; gy++)
				{
					var mapCell = GridToMapCell(gx, gy);
					if (!world.Map.Contains(mapCell))
						continue;

					var worldPos = world.Map.CenterOfCell(mapCell);
					var enemyValue = 0f;
					var friendlyValue = 0f;
					var hasEnemy = false;

					foreach (var actor in world.FindActorsInCircle(worldPos, WDist.FromCells(info.CellSize)))
					{
						if (actor.IsDead || !actor.IsInWorld || actor.Owner == null || actor.Owner.NonCombatant)
							continue;

						var valuedInfo = actor.Info.TraitInfoOrDefault<ValuedInfo>();
						if (valuedInfo == null)
							continue;

						var rel = perspective.RelationshipWith(actor.Owner);
						if (rel == PlayerRelationship.Enemy)
						{
							hasEnemy = true;
							enemyValue += valuedInfo.Cost;
						}
						else if (rel == PlayerRelationship.Ally || actor.Owner == perspective)
						{
							friendlyValue += valuedInfo.Cost;
						}
					}

					if (hasEnemy)
						candidates.Add((mapCell, enemyValue, friendlyValue));
				}
			}

			// Sort by vulnerability: low enemy value and high friendly value = easy target
			candidates.Sort((a, b) => (a.EnemyValue - a.FriendlyValue * 0.5f)
				.CompareTo(b.EnemyValue - b.FriendlyValue * 0.5f));

			// Select targets with minimum spacing between them
			var results = new List<CPos>();
			foreach (var candidate in candidates)
			{
				if (results.Count >= maxTargets)
					break;

				var tooClose = false;
				foreach (var existing in results)
				{
					if ((candidate.Cell - existing).Length < minSpacing)
					{
						tooClose = true;
						break;
					}
				}

				if (!tooClose)
					results.Add(candidate.Cell);
			}

			return results;
		}

		/// <summary>Get grid dimensions for debugging/iteration.</summary>
		public int GridWidth => gridWidth;
		public int GridHeight => gridHeight;
		public int CellSize => info.CellSize;

		/// <summary>Convert grid coordinates to map cell.</summary>
		public CPos GridToMapCell(int gx, int gy)
		{
			return new CPos(gx * info.CellSize + info.CellSize / 2, gy * info.CellSize + info.CellSize / 2);
		}
	}
}
