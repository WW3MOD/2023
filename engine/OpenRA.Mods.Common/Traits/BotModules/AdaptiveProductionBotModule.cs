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
	[Desc("Adapts unit production based on enemy composition intel from scouts and combat.",
		"Requests counter-units through the standard IBotRequestUnitProduction interface.")]
	public class AdaptiveProductionBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Delay (in ticks) between adaptation evaluations.")]
		public readonly int EvaluationInterval = 500;

		[Desc("Maximum number of unit requests per evaluation cycle.")]
		public readonly int MaxRequestsPerCycle = 2;

		[Desc("Comma-separated list of units that counter enemy vehicles (e.g., AT infantry, tanks).")]
		public readonly HashSet<string> AntiVehicleUnits = new HashSet<string>();

		[Desc("Comma-separated list of units that counter enemy infantry.")]
		public readonly HashSet<string> AntiInfantryUnits = new HashSet<string>();

		[Desc("Comma-separated list of anti-air units.")]
		public readonly HashSet<string> AntiAirUnits = new HashSet<string>();

		[Desc("Minimum enemy units sighted before adapting production.")]
		public readonly int MinEnemySightings = 3;

		public override object Create(ActorInitializer init) { return new AdaptiveProductionBotModule(init.Self, this); }
	}

	public class AdaptiveProductionBotModule : ConditionalTrait<AdaptiveProductionBotModuleInfo>, IBotTick, IBotEnabled
	{
		readonly World world;
		readonly Player player;

		IBot bot;
		BotBlackboard blackboard;
		IBotRequestUnitProduction[] unitProducers;
		int evalCountdown;
		bool initialized;

		public AdaptiveProductionBotModule(Actor self, AdaptiveProductionBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void Created(Actor self)
		{
			unitProducers = self.Owner.PlayerActor.TraitsImplementing<IBotRequestUnitProduction>().ToArray();
		}

		void IBotEnabled.BotEnabled(IBot bot)
		{
			this.bot = bot;
		}

		void Initialize()
		{
			if (initialized)
				return;

			blackboard = player.PlayerActor.TraitsImplementing<BotBlackboard>().FirstOrDefault(b => !b.IsTraitDisabled);
			initialized = true;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (--evalCountdown > 0)
				return;

			evalCountdown = Info.EvaluationInterval;
			Initialize();

			if (blackboard == null)
				return;

			// Read enemy composition intel from blackboard (posted by scouts)
			var enemyVehicles = blackboard.GetIntel<int>("enemy-vehicles-sighted");
			var enemyInfantry = blackboard.GetIntel<int>("enemy-infantry-sighted");
			var enemyBuildings = blackboard.GetIntel<int>("enemy-buildings-sighted");

			var totalSightings = enemyVehicles + enemyInfantry;
			if (totalSightings < Info.MinEnemySightings)
				return;

			// Also do our own scan for a more current picture
			var currentEnemyComposition = ScanEnemyComposition();
			enemyVehicles = Math.Max(enemyVehicles, currentEnemyComposition.Vehicles);
			enemyInfantry = Math.Max(enemyInfantry, currentEnemyComposition.Infantry);
			var enemyAir = currentEnemyComposition.Aircraft;

			// Determine what we need most
			var requests = new List<(HashSet<string> Pool, float Priority)>();

			// Anti-vehicle priority: scales with enemy vehicle count
			if (Info.AntiVehicleUnits.Count > 0 && enemyVehicles > 0)
			{
				var avRatio = (float)enemyVehicles / Math.Max(totalSightings, 1);
				requests.Add((Info.AntiVehicleUnits, avRatio * enemyVehicles));
			}

			// Anti-infantry priority
			if (Info.AntiInfantryUnits.Count > 0 && enemyInfantry > 3)
			{
				var aiRatio = (float)enemyInfantry / Math.Max(totalSightings, 1);
				requests.Add((Info.AntiInfantryUnits, aiRatio * enemyInfantry * 0.5f));
			}

			// Anti-air priority: high urgency if any aircraft spotted
			if (Info.AntiAirUnits.Count > 0 && enemyAir > 0)
			{
				// AA is urgent — even 1 aircraft merits a response
				var aaCount = CountOwnUnits(Info.AntiAirUnits);
				if (aaCount < enemyAir * 2)
					requests.Add((Info.AntiAirUnits, enemyAir * 3f));
			}

			// Sort by priority and request top units
			requests.Sort((a, b) => b.Priority.CompareTo(a.Priority));

			var requestsMade = 0;
			foreach (var request in requests)
			{
				if (requestsMade >= Info.MaxRequestsPerCycle)
					break;

				// Pick a random unit from the counter pool that we can build
				var candidates = request.Pool
					.Where(u => world.Map.Rules.Actors.ContainsKey(u))
					.ToList();

				if (candidates.Count == 0)
					continue;

				var unitToBuild = candidates.Random(world.LocalRandom);

				// Check we haven't already requested too many
				var alreadyRequested = unitProducers.Sum(up => up.RequestedProductionCount(bot, unitToBuild));
				if (alreadyRequested >= 2)
					continue;

				foreach (var up in unitProducers)
				{
					up.RequestUnitProduction(bot, unitToBuild);
					requestsMade++;
					break;
				}
			}
		}

		int CountOwnUnits(HashSet<string> unitTypes)
		{
			return world.Actors.Count(a => a.Owner == player && !a.IsDead && a.IsInWorld && unitTypes.Contains(a.Info.Name));
		}

		EnemyComposition ScanEnemyComposition()
		{
			var result = new EnemyComposition();

			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null)
					continue;

				if (player.RelationshipWith(actor.Owner) != PlayerRelationship.Enemy)
					continue;

				// Only count visible enemies
				if (!actor.CanBeViewedByPlayer(player))
					continue;

				if (actor.Info.HasTraitInfo<AircraftInfo>())
					result.Aircraft++;
				else if (actor.Info.HasTraitInfo<BuildingInfo>())
					result.Buildings++;
				else
				{
					var mobile = actor.Info.HasTraitInfo<MobileInfo>();
					if (!mobile)
						continue;

					if (actor.Info.HasTraitInfo<Render.WithInfantryBodyInfo>())
						result.Infantry++;
					else
						result.Vehicles++;
				}
			}

			return result;
		}

		struct EnemyComposition
		{
			public int Infantry;
			public int Vehicles;
			public int Aircraft;
			public int Buildings;
		}
	}
}
