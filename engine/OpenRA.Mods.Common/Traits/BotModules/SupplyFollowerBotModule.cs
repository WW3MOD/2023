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
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits.BotModules.Squads;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Orders supply trucks to follow attack squads and resupply units in the field.")]
	public class SupplyFollowerBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Actor types that are supply trucks.")]
		public readonly HashSet<string> SupplyTruckTypes = new HashSet<string>();

		[Desc("Delay (in ticks) between supply follow-up scans.")]
		public readonly int ScanInterval = 120;

		[Desc("Minimum distance in cells to keep from the front line (stay behind the army).")]
		public readonly int SafeFollowDistance = 5;

		[Desc("Maximum distance in cells a truck will travel to follow a squad.")]
		public readonly int MaxFollowDistance = 40;

		[Desc("Minimum number of friendly units near a location to consider it worth following.")]
		public readonly int MinNearbyFriendlies = 3;

		[Desc("Influence stack Stage E: consume the per-player ANTI-GROUND danger field (DangerFieldLayer)",
			"so a supply truck relocating along the front does NOT drive point-to-point through the danger —",
			"it detours toward the safer side, and because the Stage-C territory baseline makes deep enemy",
			"ground expensive while the friendly rear reads ~0, the pull-back / lateral / re-enter path EMERGES",
			"from the cost rather than being scripted. Emits a two-leg Move (safe waypoint, then the follow",
			"cell). This module is enable-ai-ANY, so the reroute is additionally gated on",
			"InfluenceStack.Participates (only @experimental bots read the stack) — every other profile is",
			"byte-identical. OFF by default; the @supply instance opts in via YAML.")]
		public readonly bool DangerFieldRouting = false;

		[Desc("Stage-E: path ground-danger above which a truck's relocation is rerouted via safer depth.",
			"Lower than the offensive threshold — a non-combatant should avoid even moderate exposure.")]
		public readonly int GroundDangerSafeThreshold = 15;

		[Desc("Stage-E: lateral offset magnitude (cells) for the truck's rear-lateral detour waypoint.")]
		public readonly int GroundDangerDetourCells = 8;

		[Desc("Stage-E: how many lateral steps (× GroundDangerDetourCells) the detour search may probe —",
			"a larger budget lets a high-value mover route deeper into the safe rear.")]
		public readonly int GroundDangerDetourSteps = 3;

		[Desc("Stage-E deadband (cells): re-issue a truck's two-leg detour only when the recomputed",
			"waypoint shifts by at least this much. Since the detour is recomputed from the MOVING truck",
			"each scan, without this the waypoint recedes and the maneuver restarts before it completes.")]
		public readonly int RepathThresholdCells = 3;

		public override object Create(ActorInitializer init) { return new SupplyFollowerBotModule(init.Self, this); }
	}

	public class SupplyFollowerBotModule : ConditionalTrait<SupplyFollowerBotModuleInfo>, IBotTick, IBotEnabled
	{
		readonly World world;
		readonly Player player;

		IBot bot;
		ThreatMapManager threatMap;
		BotBlackboard blackboard;
		DangerFieldLayer dangerField;
		int scanCountdown;
		bool initialized;

		// Track which trucks are assigned to follow duty
		readonly HashSet<Actor> activeTrucks = new HashSet<Actor>();

		// Stage-E: last detour waypoint ordered per truck (absent = last order went direct). Drives the
		// re-issue deadband so a truck mid-detour isn't restarted every scan as its waypoint recedes.
		readonly Dictionary<Actor, CPos> lastVia = new Dictionary<Actor, CPos>();

		public SupplyFollowerBotModule(Actor self, SupplyFollowerBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		void IBotEnabled.BotEnabled(IBot bot)
		{
			this.bot = bot;
		}

		void Initialize()
		{
			if (initialized)
				return;

			threatMap = world.WorldActor.TraitOrDefault<ThreatMapManager>();
			blackboard = player.PlayerActor.TraitsImplementing<BotBlackboard>().FirstOrDefault(b => !b.IsTraitDisabled);

			// Stage-E danger-weighted routing. enable-ai-ANY module, so ALSO gate on Participates:
			// only @experimental bots read the influence stack, keeping every other profile byte-identical.
			dangerField = Info.DangerFieldRouting && InfluenceStack.Participates(player)
				? world.WorldActor.TraitOrDefault<DangerFieldLayer>() : null;

			initialized = true;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (--scanCountdown > 0)
				return;

			scanCountdown = Info.ScanInterval;
			Initialize();

			// Clean up dead trucks (or low-supply trucks, which we're releasing back to
			// SupplyProvider's built-in auto-restock).
			activeTrucks.RemoveWhere(a => a == null || a.IsDead || !a.IsInWorld || IsLowOnSupply(a));

			// Keep the Stage-E deadband memory bounded to trucks still on active follow duty.
			if (lastVia.Count > 0)
			{
				var stale = lastVia.Keys.Where(a => !activeTrucks.Contains(a)).ToList();
				foreach (var a in stale)
					lastVia.Remove(a);
			}

			// Find all supply trucks — eligible only if they actually have supplies to give.
			// SupplyProvider auto-restocks at low/zero supply by queuing a MoveTo(LC)
			// activity; if we issue a forward Move here it cancels that restock and the
			// empty truck ends up at the front with nothing to give. Filter them out.
			var trucks = world.ActorsHavingTrait<Mobile>()
				.Where(a => a.Owner == player
					&& !a.IsDead
					&& a.IsInWorld
					&& Info.SupplyTruckTypes.Contains(a.Info.Name)
					&& !IsClaimedByOtherModule(a)
					&& !IsLowOnSupply(a))
				.ToList();

			if (trucks.Count == 0)
				return;

			// Find clusters of friendly combat units that might need supply
			var friendlyUnits = world.ActorsHavingTrait<Mobile>()
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld && !Info.SupplyTruckTypes.Contains(a.Info.Name))
				.ToList();

			if (friendlyUnits.Count == 0)
				return;

			// Find unit clusters by looking for groups of friendly units away from base
			var clusters = FindUnitClusters(friendlyUnits);

			foreach (var truck in trucks)
			{
				if (clusters.Count == 0)
					break;

				// Find the best cluster for this truck (closest cluster with ammo need)
				var bestCluster = clusters
					.Where(c => (c.Center - truck.CenterPosition).Length < WDist.FromCells(Info.MaxFollowDistance).Length)
					.OrderByDescending(c => c.AmmoNeed)
					.ThenBy(c => (c.Center - truck.CenterPosition).LengthSquared)
					.FirstOrDefault();

				if (bestCluster == null)
					continue;

				// Find a safe position behind the cluster (away from enemy threat)
				var followPos = FindSafeFollowPosition(bestCluster);

				if (followPos.HasValue)
				{
					if (dangerField == null)
					{
						// Flag off / non-participant: unchanged base behaviour (byte-identical), a single
						// direct Move re-issued each scan to track the moving cluster.
						bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, followPos.Value), false));
					}
					else
					{
						// Stage-E: if the straight drive from the truck to its follow cell would cross a
						// ground kill zone, detour via a safer waypoint first (queued: false), then the follow
						// cell (queued: true). Against the territory-baseline gradient the safer side is the
						// rear, so the pull-back-lateral-re-enter path emerges. WaypointPassable rejects a
						// waypoint the truck cannot stand on (rear water reads 0 danger = falsely "safe").
						var ground = GroundDangerSampler();
						var passable = WaypointPassable(truck);
						var via = GroundDangerNav.DetourWaypoint(
							truck.Location, followPos.Value,
							Info.GroundDangerDetourCells, Info.GroundDangerDetourSteps,
							Info.GroundDangerSafeThreshold, ground, passable);

						if (via.HasValue)
						{
							// Deadband: leave an in-flight two-leg maneuver alone unless the recomputed
							// waypoint shifted >= threshold. `from` is the MOVING truck, so re-issuing every
							// scan would make the waypoint recede and restart the detour before it completes.
							var had = lastVia.TryGetValue(truck, out var prev);
							if (!had || (prev - via.Value).LengthSquared >= Info.RepathThresholdCells * Info.RepathThresholdCells)
							{
								bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, via.Value), false));
								bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, followPos.Value), true));
								lastVia[truck] = via.Value;
							}
						}
						else
						{
							// No detour needed — a single direct Move (no restart problem) each scan, as
							// before. Drop any stale detour memory so the next detour re-issues cleanly.
							bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, followPos.Value), false));
							lastVia.Remove(truck);
						}
					}

					if (!activeTrucks.Contains(truck))
					{
						activeTrucks.Add(truck);
						if (blackboard != null)
							blackboard.ClaimUnit(truck, "supply-follow");
					}
				}
			}
		}

		List<UnitCluster> FindUnitClusters(List<Actor> units)
		{
			var clusters = new List<UnitCluster>();
			var assigned = new HashSet<Actor>();

			foreach (var unit in units)
			{
				if (assigned.Contains(unit))
					continue;

				// Find nearby units to form a cluster
				var nearby = units
					.Where(a => !assigned.Contains(a) && (a.CenterPosition - unit.CenterPosition).Length < WDist.FromCells(10).Length)
					.ToList();

				if (nearby.Count < Info.MinNearbyFriendlies)
					continue;

				// Calculate cluster center and ammo need
				var center = nearby.Select(a => a.CenterPosition).Average();
				var ammoNeed = 0f;

				foreach (var a in nearby)
				{
					var ammoPools = a.TraitsImplementing<AmmoPool>().ToArray();
					foreach (var pool in ammoPools)
					{
						if (pool.Info.Ammo > 0)
							ammoNeed += 1f - (float)pool.CurrentAmmoCount / pool.Info.Ammo;
					}
				}

				clusters.Add(new UnitCluster
				{
					Center = center,
					CenterCell = world.Map.CellContaining(center),
					UnitCount = nearby.Count,
					AmmoNeed = ammoNeed
				});

				foreach (var a in nearby)
					assigned.Add(a);
			}

			return clusters;
		}

		CPos? FindSafeFollowPosition(UnitCluster cluster)
		{
			if (threatMap == null)
				return cluster.CenterCell;

			// Find the safest cell near the cluster (behind the front line)
			var bestCell = cluster.CenterCell;
			var bestScore = float.MinValue;

			for (var dx = -3; dx <= 3; dx++)
			{
				for (var dy = -3; dy <= 3; dy++)
				{
					var cell = new CPos(cluster.CenterCell.X + dx, cluster.CenterCell.Y + dy);
					if (!world.Map.Contains(cell))
						continue;

					var threat = threatMap.GetThreat(cell, player);
					// Prefer cells with friendly advantage (negative threat) near the cluster
					var score = -threat;

					if (score > bestScore)
					{
						bestScore = score;
						bestCell = cell;
					}
				}
			}

			return bestCell;
		}

		// A ground-danger sampler bound to this player's own anti-ground channel. Off-map cells read
		// Impassable so a detour waypoint never lands off the playable area. Fog-legal by construction.
		Func<CPos, int> GroundDangerSampler()
		{
			var map = world.Map;
			return c => map.Contains(c) ? dangerField.GroundDanger(player, c) : GroundDangerNav.Impassable;
		}

		// A terrain-passability predicate bound to the truck's locomotor: true when it can actually stand
		// on the cell (not on-map water/cliff, not off-map). Rejects detour WAYPOINTS that read "safe"
		// only because unstamped impassable ground carries no danger. All-passable fallback if no Mobile.
		Func<CPos, bool> WaypointPassable(Actor mover)
		{
			var loco = mover.TraitOrDefault<Mobile>()?.Locomotor;
			if (loco == null)
				return _ => true;

			return c => loco.MovementCostForCell(c) != PathGraph.MovementCostForUnreachableCell;
		}

		bool IsClaimedByOtherModule(Actor a)
		{
			if (blackboard == null)
				return false;

			var claimant = blackboard.GetUnitClaimant(a);
			return claimant != null && claimant != "supply-follow";
		}

		// A truck below its RestockThreshold has effectively no supplies to give. Don't
		// issue forward orders — SupplyProvider's built-in restock will route it back to
		// a LogisticsCenter / SR if we leave it alone.
		static bool IsLowOnSupply(Actor a)
		{
			var sp = a.TraitOrDefault<SupplyProvider>();
			if (sp == null)
				return false;
			return sp.CurrentSupply < sp.Info.RestockThreshold;
		}

		protected override void TraitDisabled(Actor self)
		{
			if (blackboard != null)
				foreach (var truck in activeTrucks)
					blackboard.ReleaseUnit(truck);

			activeTrucks.Clear();
			lastVia.Clear();
		}

		class UnitCluster
		{
			public WPos Center;
			public CPos CenterCell;
			public int UnitCount;
			public float AmmoNeed;
		}
	}
}
