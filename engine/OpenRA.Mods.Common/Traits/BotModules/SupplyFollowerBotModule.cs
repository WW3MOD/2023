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

using System.Collections.Generic;
using System.Linq;
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

		public override object Create(ActorInitializer init) { return new SupplyFollowerBotModule(init.Self, this); }
	}

	public class SupplyFollowerBotModule : ConditionalTrait<SupplyFollowerBotModuleInfo>, IBotTick, IBotEnabled
	{
		readonly World world;
		readonly Player player;

		IBot bot;
		ThreatMapManager threatMap;
		BotBlackboard blackboard;
		int scanCountdown;
		bool initialized;

		// Track which trucks are assigned to follow duty
		readonly HashSet<Actor> activeTrucks = new HashSet<Actor>();

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
			initialized = true;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (--scanCountdown > 0)
				return;

			scanCountdown = Info.ScanInterval;
			Initialize();

			// Clean up dead trucks
			activeTrucks.RemoveWhere(a => a == null || a.IsDead || !a.IsInWorld);

			// Find all supply trucks
			var trucks = world.ActorsHavingTrait<Mobile>()
				.Where(a => a.Owner == player
					&& !a.IsDead
					&& a.IsInWorld
					&& Info.SupplyTruckTypes.Contains(a.Info.Name)
					&& !IsClaimedByOtherModule(a))
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
					bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, followPos.Value), false));

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

		bool IsClaimedByOtherModule(Actor a)
		{
			if (blackboard == null)
				return false;

			var claimant = blackboard.GetUnitClaimant(a);
			return claimant != null && claimant != "supply-follow";
		}

		protected override void TraitDisabled(Actor self)
		{
			if (blackboard != null)
				foreach (var truck in activeTrucks)
					blackboard.ReleaseUnit(truck);

			activeTrucks.Clear();
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
