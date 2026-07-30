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

		[Desc("EXPERIMENTAL: filter each counter pool by UnitRoleResolver class before drawing a call-in,",
			"so odd mixes are dropped — anti-air keeps only ShortRangeAD, anti-vehicle keeps MainBattle/",
			"IndirectFire, anti-infantry keeps MainBattle/IndirectFire/Recon. For the current roster every",
			"configured unit already classifies into its pool's class, so this is a class-purity sanity",
			"filter (robust to roster edits) that adds NO random draws. Default false = frozen name-list",
			"behaviour, so the @stable/legacy twins stay byte-identical.")]
		public readonly bool UseUnitRoles = false;

		[Desc("SR-DEFENSE (experimental): believed anti-ground danger near our own Supply Route at or",
			"above which we push prioritized anti-armor call-ins, BYPASSING MinEnemySightings (a 2-tank",
			"rush = 2 sightings never trips MinEnemySightings=3). On the DangerFieldLayer throughput scale;",
			"a tank's danger kernel is far denser than infantry, so a value above ambient/light-infantry",
			"noise makes this fire on ARMOR near the SR. Near our own SR the territory baseline is ~0, so",
			"any real reading here is a believed enemy weapon actually reaching the beachhead. Default 0 =",
			"disabled: the @stable/frozen twin never enters the SR-defense path -> byte-identical, zero new RNG.")]
		public readonly int SupplyRouteDangerThreshold = 0;

		[Desc("SR-DEFENSE: Chebyshev radius (cells) around our Supply Route to sample for ground danger.")]
		public readonly int SupplyRouteScanRadius = 8;

		[Desc("SR-DEFENSE: anti-armor units to call in, IN PRIORITY ORDER (AT infantry first, then IFV/tank),",
			"when the SR is under armored threat. Requested deterministically in this order (no random draw).",
			"Empty = off.")]
		public readonly string[] SupplyRouteDefenseUnits = System.Array.Empty<string>();

		[Desc("SR-DEFENSE: actor name(s) of our Supply Route beachhead, used to locate it on the map.",
			"Mirrors MountedTransportBotModuleInfo.SupplyRouteTypes.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		public override object Create(ActorInitializer init) { return new AdaptiveProductionBotModule(init.Self, this); }
	}

	public class AdaptiveProductionBotModule : ConditionalTrait<AdaptiveProductionBotModuleInfo>, IBotTick, IBotEnabled
	{
		// UnitRoleResolver class-filters per counter category (UseUnitRoles). The taxonomy has no
		// anti-vehicle/anti-infantry split, so both keep the ground-combat classes; anti-infantry also
		// admits Recon (light wheeled scouts like humvee/btr are valid infantry counters). Anti-air maps
		// 1:1 to ShortRangeAD. See WORKSPACE/DISCOVERIES.md (2026-07-24).
		static readonly UnitRole[] AntiVehicleRoles = { UnitRole.MainBattle, UnitRole.IndirectFire };
		static readonly UnitRole[] AntiInfantryRoles = { UnitRole.MainBattle, UnitRole.IndirectFire, UnitRole.Recon };
		static readonly UnitRole[] AntiAirRoles = { UnitRole.ShortRangeAD };

		readonly World world;
		readonly Player player;

		IBot bot;
		BotBlackboard blackboard;
		IBotRequestUnitProduction[] unitProducers;
		UnitRoleResolver resolver;
		DangerFieldLayer dangerField;
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
			if (Info.UseUnitRoles)
				resolver = world.WorldActor.TraitOrDefault<UnitRoleResolver>();

			// SR-defense reads the belief-based danger field only when enabled (threshold > 0).
			// Left null on the frozen path, so @stable never touches the field or its trait lookup.
			if (Info.SupplyRouteDangerThreshold > 0)
				dangerField = world.WorldActor.TraitOrDefault<DangerFieldLayer>();

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

			// SR-DEFENSE (experimental): read the fog-legal believed ground-danger field near our own
			// Supply Route and, when armored threat is closing on the beachhead, push deliberate
			// prioritized anti-armor call-ins AHEAD of the static composition — BYPASSING the
			// MinEnemySightings gate below (2 rushing tanks = 2 sightings never trips it). Frozen when
			// SupplyRouteDangerThreshold <= 0, so the @stable twin skips the whole block (dangerField is
			// null there) and stays byte-identical — zero new RNG on any path.
			var requestsMade = 0;
			if (Info.SupplyRouteDangerThreshold > 0)
				requestsMade = TrySupplyRouteDefense();

			if (requestsMade >= Info.MaxRequestsPerCycle)
				return;

			if (totalSightings < Info.MinEnemySightings)
				return;

			// Also do our own scan for a more current picture
			var currentEnemyComposition = ScanEnemyComposition();
			enemyVehicles = Math.Max(enemyVehicles, currentEnemyComposition.Vehicles);
			enemyInfantry = Math.Max(enemyInfantry, currentEnemyComposition.Infantry);
			var enemyAir = currentEnemyComposition.Aircraft;

			// Determine what we need most. Roles is the UnitRoleResolver class-filter applied to the pool
			// only when UseUnitRoles is set (empty on the frozen path — never consulted there).
			var requests = new List<(HashSet<string> Pool, float Priority, UnitRole[] Roles)>();

			// Anti-vehicle priority: scales with enemy vehicle count
			if (Info.AntiVehicleUnits.Count > 0 && enemyVehicles > 0)
			{
				var avRatio = (float)enemyVehicles / Math.Max(totalSightings, 1);
				requests.Add((Info.AntiVehicleUnits, avRatio * enemyVehicles, AntiVehicleRoles));
			}

			// Anti-infantry priority
			if (Info.AntiInfantryUnits.Count > 0 && enemyInfantry > 3)
			{
				var aiRatio = (float)enemyInfantry / Math.Max(totalSightings, 1);
				requests.Add((Info.AntiInfantryUnits, aiRatio * enemyInfantry * 0.5f, AntiInfantryRoles));
			}

			// Anti-air priority: high urgency if any aircraft spotted
			if (Info.AntiAirUnits.Count > 0 && enemyAir > 0)
			{
				// AA is urgent — even 1 aircraft merits a response
				var aaCount = CountOwnUnits(Info.AntiAirUnits);
				if (aaCount < enemyAir * 2)
					requests.Add((Info.AntiAirUnits, enemyAir * 3f, AntiAirRoles));
			}

			// Sort by priority and request top units. requestsMade may already be non-zero if the
			// SR-defense path above consumed part of this cycle's budget (experimental only).
			requests.Sort((a, b) => b.Priority.CompareTo(a.Priority));

			foreach (var request in requests)
			{
				if (requestsMade >= Info.MaxRequestsPerCycle)
					break;

				// Pick a random unit from the counter pool that we can build
				var candidates = request.Pool
					.Where(u => world.Map.Rules.Actors.ContainsKey(u))
					.ToList();

				// Role-model class filter (experimental): drop pool members whose resolver class does not
				// match this request's category, so odd call-ins are pruned. Applied BEFORE the empty check
				// and the single draw below, so the RNG call sequence is untouched (still one draw per
				// non-empty pool). The frozen path skips this entirely and stays byte-identical.
				if (Info.UseUnitRoles && resolver != null)
					candidates = candidates
						.Where(u => request.Roles.Contains(resolver.GetRole(world.Map.Rules.Actors[u])))
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

		// Deterministic, fog-legal SR defense. Samples the believed anti-ground danger field within a
		// Chebyshev radius of our own Supply Route; if the peak exceeds SupplyRouteDangerThreshold
		// (armor-weighted — a tank's kernel is far denser than infantry) it requests the configured
		// anti-armor units IN DECLARED PRIORITY ORDER (AT infantry first, then IFV/tank). No random draw:
		// fixed iteration over ActiveCells + the ordered unit list, integer compares only.
		int TrySupplyRouteDefense()
		{
			if (dangerField == null || Info.SupplyRouteDefenseUnits.Length == 0)
				return 0;

			var ownSR = world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.SupplyRouteTypes.Contains(a.Info.Name));
			if (ownSR == null)
				return 0;

			var srCell = ownSR.Location;
			var peakDanger = 0;
			foreach (var c in dangerField.ActiveCells(player))
			{
				if (Math.Max(Math.Abs(c.X - srCell.X), Math.Abs(c.Y - srCell.Y)) > Info.SupplyRouteScanRadius)
					continue;

				var d = dangerField.GroundDanger(player, c);
				if (d > peakDanger)
					peakDanger = d;
			}

			if (peakDanger < Info.SupplyRouteDangerThreshold)
				return 0;

			var made = 0;
			foreach (var unit in Info.SupplyRouteDefenseUnits)
			{
				if (made >= Info.MaxRequestsPerCycle)
					break;

				if (!world.Map.Rules.Actors.ContainsKey(unit))
					continue;

				// Don't stack more than a couple of the same call-in (mirrors the static-counter cap below).
				var alreadyRequested = unitProducers.Sum(up => up.RequestedProductionCount(bot, unit));
				if (alreadyRequested >= 2)
					continue;

				foreach (var up in unitProducers)
				{
					up.RequestUnitProduction(bot, unit);
					made++;
					break;
				}
			}

			return made;
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
