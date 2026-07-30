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

		[Desc("SR-DEFENSE (experimental) master gate. When on, believed enemy contacts near our own Supply",
			"Route(s) are classified by attacker class (air/armor/infantry) and the MATCHED counter pool",
			"(AntiAirUnits / AntiVehicleUnits / AntiInfantryUnits) is called in, BYPASSING MinEnemySightings",
			"(a 2-tank rush = 2 sightings never trips MinEnemySightings=3). Reads the fog-legal belief store",
			"(same source the danger field stamps from), so it carries actor identity — classification, not a",
			"scalar danger read, so an anti-air threat buys AA (not AT) and light infantry never draws AT.",
			"Default false: the @stable/frozen twin never enters this path -> byte-identical, zero new RNG.")]
		public readonly bool SupplyRouteDefenseEnabled = false;

		[Desc("SR-DEFENSE: Chebyshev cell radius around each owned Supply Route within which a believed enemy",
			"contact counts as an SR threat. Contact cells are read directly (no danger-kernel tail), so this",
			"is a literal proximity radius.")]
		public readonly int SupplyRouteScanRadius = 10;

		[Desc("SR-DEFENSE: believed enemy ARMOR value near the SR at or above which we call in an anti-armor",
			"counter. Value = sum over believed armored contacts of (unit build cost * confidence / 100), on",
			"the unit-cost scale (mirrors the DefenseEnemyValueThreshold $-value convention). Set above a lone",
			"light-recon vehicle so a scout does not draw a counter; a fresh IFV/MBT or a 2-tank rush trips it.")]
		public readonly int SupplyRouteArmorValueThreshold = 1200;

		[Desc("SR-DEFENSE: believed enemy AIR value threshold -> anti-air counter (AA infantry / SHORAD / Tunguska).",
			"Aircraft are expensive, so a single attack aircraft near the SR trips this.")]
		public readonly int SupplyRouteAirValueThreshold = 1000;

		[Desc("SR-DEFENSE: believed enemy INFANTRY value threshold -> anti-infantry counter. Set high enough that",
			"a lone scout does not trip it (needs a real squad) — this restores, per class, the anti-overreaction",
			"the MinEnemySightings gate provided.")]
		public readonly int SupplyRouteInfantryValueThreshold = 1000;

		[Desc("SR-DEFENSE: actor name(s) of our Supply Route beachhead(s), used to locate them on the map.",
			"Mirrors MountedTransportBotModuleInfo.SupplyRouteTypes. ALL owned SRs are scanned (capture can",
			"grant more than one).")]
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
		BeliefStore beliefStore;
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

			// SR-defense reads the fog-legal belief store only when enabled. Left null on the frozen
			// path, so @stable never touches the store or its trait lookup.
			if (Info.SupplyRouteDefenseEnabled)
				beliefStore = world.WorldActor.TraitOrDefault<BeliefStore>();

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

			// SR-DEFENSE (experimental): classify believed enemy contacts near our own Supply Route(s)
			// and call in the MATCHED counter (armor->AntiVehicle, air->AntiAir, infantry->AntiInfantry)
			// AHEAD of the static composition — BYPASSING the MinEnemySightings gate below (2 rushing
			// tanks = 2 sightings never trips it). Reserves AT MOST ONE request/cycle so the scouted-
			// composition path below is never starved. Frozen when SupplyRouteDefenseEnabled is false,
			// so the @stable twin skips the whole block (beliefStore is null there) and stays
			// byte-identical — zero new RNG on any path.
			var requestsMade = 0;
			if (Info.SupplyRouteDefenseEnabled)
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

		enum ThreatClass { None, Air, Armor, Infantry }

		// Deterministic, fog-legal SR defense. Classifies believed enemy contacts within
		// SupplyRouteScanRadius of ANY owned Supply Route by attacker class, accumulating believed VALUE
		// (unit build cost * confidence / 100) per class, and calls in the MATCHED counter for the single
		// most-threatening class over its per-class value threshold. Reserves AT MOST ONE request/cycle
		// (FIX 4a — never starves the scouted-composition path). Zero RNG: additive value sums are
		// order-independent, the class pick uses a fixed evaluation order with a strict-greater tie-break,
		// and the counter is the cheapest buildable in the matched pool (stable cost+name ordering).
		int TrySupplyRouteDefense()
		{
			if (beliefStore == null)
				return 0;

			// All owned Supply Routes (capture can grant more than one) — FIX 4b.
			var srCells = new List<CPos>();
			foreach (var a in world.Actors)
				if (a.Owner == player && !a.IsDead && a.IsInWorld && Info.SupplyRouteTypes.Contains(a.Info.Name))
					srCells.Add(a.Location);

			if (srCells.Count == 0)
				return 0;

			// Believed enemy value per attacker class within reach of any owned SR.
			long airValue = 0, armorValue = 0, infantryValue = 0;
			foreach (var c in beliefStore.Contacts(player))
			{
				if (c.IsStatic || !NearAnySupplyRoute(c.Cell, srCells))
					continue;

				if (!world.Map.Rules.Actors.TryGetValue(c.TypeName, out var ai))
					continue;

				var value = (long)UnitCost(ai) * c.Confidence / 100;
				if (value <= 0)
					continue;

				switch (ClassifyContact(ai))
				{
					case ThreatClass.Air: airValue += value; break;
					case ThreatClass.Armor: armorValue += value; break;
					case ThreatClass.Infantry: infantryValue += value; break;
				}
			}

			// Pick the single most-threatening class over its threshold. Fixed evaluation order with a
			// strict-greater comparison makes ties deterministic (earlier-listed class wins): Air first
			// (only AA can answer it), then Armor, then Infantry.
			HashSet<string> pool = null;
			var bestExcess = -1L;
			foreach (var cand in new[]
			{
				(Excess: airValue - Info.SupplyRouteAirValueThreshold, Pool: Info.AntiAirUnits),
				(Excess: armorValue - Info.SupplyRouteArmorValueThreshold, Pool: Info.AntiVehicleUnits),
				(Excess: infantryValue - Info.SupplyRouteInfantryValueThreshold, Pool: Info.AntiInfantryUnits),
			})
			{
				if (cand.Excess >= 0 && cand.Excess > bestExcess)
				{
					bestExcess = cand.Excess;
					pool = cand.Pool;
				}
			}

			if (pool == null || pool.Count == 0)
				return 0;

			// Cheapest buildable counter first (AT infantry before tanks, AA infantry before SHORAD),
			// name-ordinal tie-break — deterministic, no random draw.
			var ordered = pool
				.Where(u => world.Map.Rules.Actors.ContainsKey(u))
				.OrderBy(u => UnitCost(world.Map.Rules.Actors[u]))
				.ThenBy(u => u, StringComparer.Ordinal);

			foreach (var unit in ordered)
			{
				// Don't stack more than a couple of the same call-in (mirrors the static-counter cap below).
				var alreadyRequested = unitProducers.Sum(up => up.RequestedProductionCount(bot, unit));
				if (alreadyRequested >= 2)
					continue;

				foreach (var up in unitProducers)
				{
					up.RequestUnitProduction(bot, unit);
					return 1; // Reserve at most ONE slot per cycle for SR defense.
				}
			}

			return 0;
		}

		bool NearAnySupplyRoute(CPos cell, List<CPos> srCells)
		{
			foreach (var sr in srCells)
				if (Math.Max(Math.Abs(cell.X - sr.X), Math.Abs(cell.Y - sr.Y)) <= Info.SupplyRouteScanRadius)
					return true;

			return false;
		}

		static ThreatClass ClassifyContact(ActorInfo ai)
		{
			// Classify by the ENEMY unit's own type, not by what its weapon can target — so an attack
			// helicopter is AIR (answered by AA), never ground (which would draw AT it cannot use).
			if (ai.HasTraitInfo<AircraftInfo>())
				return ThreatClass.Air;

			if (!ai.HasTraitInfo<MobileInfo>())
				return ThreatClass.None; // structures / immobile — not a mobile SR threat.

			return ai.HasTraitInfo<Render.WithInfantryBodyInfo>() ? ThreatClass.Infantry : ThreatClass.Armor;
		}

		static int UnitCost(ActorInfo ai)
		{
			var valued = ai.TraitInfoOrDefault<ValuedInfo>();
			return valued?.Cost ?? 0;
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
