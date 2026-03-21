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
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Sends units to scout unexplored areas of the map and report enemy positions.")]
	public class ScoutBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Actor types preferred for scouting (fast, cheap units). First available is chosen.")]
		public readonly HashSet<string> ScoutTypes = new HashSet<string>();

		[Desc("Maximum number of active scouts at once.")]
		public readonly int MaxScouts = 2;

		[Desc("Delay (in ticks) between scouting decisions.")]
		public readonly int ScanInterval = 200;

		[Desc("Minimum distance in cells from base to scout. Avoids scouting own territory.")]
		public readonly int MinScoutDistance = 15;

		[Desc("How many cells of vision radius to assume per scout unit for exploration tracking.")]
		public readonly int ScoutVisionRadius = 8;

		public override object Create(ActorInitializer init) { return new ScoutBotModule(init.Self, this); }
	}

	public class ScoutBotModule : ConditionalTrait<ScoutBotModuleInfo>, IBotTick, IBotEnabled
	{
		readonly World world;
		readonly Player player;

		readonly List<Actor> activeScouts = new List<Actor>();
		readonly List<CPos> scoutTargets = new List<CPos>();

		IBot bot;
		ThreatMapManager threatMap;
		BotBlackboard blackboard;
		CPos baseCenter;
		int scanCountdown;
		bool initialized;

		public ScoutBotModule(Actor self, ScoutBotModuleInfo info)
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

			// Find base center
			var bases = world.ActorsHavingTrait<Building>()
				.Where(a => a.Owner == player)
				.ToList();

			baseCenter = bases.Count > 0
				? bases.Random(world.LocalRandom).Location
				: player.HomeLocation;

			initialized = true;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (--scanCountdown > 0)
				return;

			scanCountdown = Info.ScanInterval;
			Initialize();

			// Clean up dead/missing scouts
			activeScouts.RemoveAll(a => a == null || a.IsDead || !a.IsInWorld);

			// Update exploration tracking from scout positions
			if (threatMap != null)
				foreach (var scout in activeScouts)
					threatMap.MarkExplored(scout.Location);

			// Post any enemy sightings to blackboard
			ReportEnemySightings();

			// Recruit new scouts if needed
			if (activeScouts.Count < Info.MaxScouts)
				RecruitScout();

			// Assign destinations to idle scouts
			foreach (var scout in activeScouts)
			{
				if (!scout.IsIdle)
					continue;

				var target = FindScoutTarget(scout);
				if (target.HasValue)
				{
					bot.QueueOrder(new Order("Move", scout, Target.FromCell(world, target.Value), false));

					if (threatMap != null)
						threatMap.MarkExplored(target.Value);
				}
			}
		}

		void RecruitScout()
		{
			// Try to find a suitable scout unit that isn't already doing something
			foreach (var scoutType in Info.ScoutTypes)
			{
				var candidate = world.ActorsHavingTrait<Mobile>()
					.Where(a => a.Owner == player
						&& a.Info.Name == scoutType
						&& a.IsIdle
						&& !activeScouts.Contains(a)
						&& !IsClaimedByOtherModule(a))
					.FirstOrDefault();

				if (candidate != null)
				{
					activeScouts.Add(candidate);

					// Claim in blackboard
					if (blackboard != null)
						blackboard.ClaimUnit(candidate, "scout");

					break;
				}
			}
		}

		bool IsClaimedByOtherModule(Actor a)
		{
			if (blackboard == null)
				return false;

			var claimant = blackboard.GetUnitClaimant(a);
			return claimant != null && claimant != "scout";
		}

		CPos? FindScoutTarget(Actor scout)
		{
			if (threatMap == null)
			{
				// No threat map — just pick a random map location far from base
				return FindRandomFarCell();
			}

			// Find the least-explored grid cell that's reasonably far from base
			var bestCell = (CPos?)null;
			var bestAge = 0;

			for (var gx = 0; gx < threatMap.GridWidth; gx++)
			{
				for (var gy = 0; gy < threatMap.GridHeight; gy++)
				{
					var mapCell = threatMap.GridToMapCell(gx, gy);
					if (!world.Map.Contains(mapCell))
						continue;

					var distFromBase = (mapCell - baseCenter).Length;
					if (distFromBase < Info.MinScoutDistance)
						continue;

					var age = threatMap.GetExplorationAge(mapCell);

					// Bonus for cells near map edges (likely enemy approach routes)
					var edgeBonus = 0;
					var mapBounds = world.Map.Bounds;
					if (mapCell.X < mapBounds.Left + 5 || mapCell.X > mapBounds.Right - 5 ||
						mapCell.Y < mapBounds.Top + 5 || mapCell.Y > mapBounds.Bottom - 5)
						edgeBonus = 500;

					var score = age + edgeBonus;

					if (score > bestAge)
					{
						bestAge = score;
						bestCell = mapCell;
					}
				}
			}

			return bestCell;
		}

		CPos? FindRandomFarCell()
		{
			var map = world.Map;
			for (var attempts = 0; attempts < 10; attempts++)
			{
				var x = world.SharedRandom.Next(map.Bounds.Left, map.Bounds.Right);
				var y = world.SharedRandom.Next(map.Bounds.Top, map.Bounds.Bottom);
				var cell = new CPos(x, y);

				if (!map.Contains(cell))
					continue;

				var dist = (cell - baseCenter).Length;
				if (dist >= Info.MinScoutDistance)
					return cell;
			}

			return null;
		}

		void ReportEnemySightings()
		{
			if (blackboard == null)
				return;

			foreach (var scout in activeScouts)
			{
				var nearby = world.FindActorsInCircle(scout.CenterPosition, WDist.FromCells(Info.ScoutVisionRadius));

				var enemyBuildings = 0;
				var enemyVehicles = 0;
				var enemyInfantry = 0;
				CPos? enemyBaseLocation = null;

				foreach (var a in nearby)
				{
					if (a.IsDead || !a.IsInWorld || a.Owner == null)
						continue;

					if (player.RelationshipWith(a.Owner) != PlayerRelationship.Enemy)
						continue;

					if (a.Info.HasTraitInfo<BuildingInfo>())
					{
						enemyBuildings++;
						if (enemyBaseLocation == null)
							enemyBaseLocation = a.Location;
					}
					else if (a.Info.HasTraitInfo<AircraftInfo>())
					{
						// Skip aircraft for ground composition intel
					}
					else
					{
						var mobile = a.TraitOrDefault<Mobile>();
						if (mobile != null)
						{
							if (a.Info.HasTraitInfo<WithInfantryBodyInfo>())
								enemyInfantry++;
							else
								enemyVehicles++;
						}
					}
				}

				if (enemyBaseLocation.HasValue)
					blackboard.PostIntel("enemy-base-location", enemyBaseLocation.Value);

				if (enemyBuildings > 0 || enemyVehicles > 0 || enemyInfantry > 0)
				{
					blackboard.PostIntel("enemy-buildings-sighted", enemyBuildings);
					blackboard.PostIntel("enemy-vehicles-sighted", enemyVehicles);
					blackboard.PostIntel("enemy-infantry-sighted", enemyInfantry);
					blackboard.PostIntel("last-scout-tick", world.WorldTick);
				}
			}
		}

		protected override void TraitDisabled(Actor self)
		{
			// Release all scouts
			if (blackboard != null)
				foreach (var scout in activeScouts)
					blackboard.ReleaseUnit(scout);

			activeScouts.Clear();
		}
	}
}
