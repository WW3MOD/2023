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

		[Desc("@experimental sector spread: when several trucks are free, greedily assign each to a DISTINCT",
			"unit cluster (neediest first) instead of every truck piling onto the same blob; only double up",
			"when trucks outnumber clusters. This is a shared enable-ai-ANY module, so it is additionally gated",
			"on InfluenceStack.Participates — OFF by default, every non-@experimental profile byte-identical.")]
		public readonly bool SectorSpread = false;

		[Desc("@experimental small-squad coverage: lower the servable-cluster floor to",
			"SmallSquadMinNearbyFriendlies (below MinNearbyFriendlies) so small squads become visible to the",
			"follower once the big clusters are covered. OFF by default; double-gated on InfluenceStack.Participates.")]
		public readonly bool SmallSquadCoverage = false;

		[Desc("Minimum friendlies to form a servable cluster when SmallSquadCoverage is on. Only applied for",
			"participating (@experimental / human) profiles; capped at MinNearbyFriendlies so it only widens.")]
		public readonly int SmallSquadMinNearbyFriendlies = 2;

		[Desc("@experimental danger evac: when the believed ground danger at the truck (or its target cluster",
			"centroid) reaches EvacDangerThreshold, retreat the truck toward its Supply Route instead of idling",
			"in the fire. Fog-legal — reads DangerFieldLayer only, never an omniscient enemy scan. OFF by",
			"default; double-gated on InfluenceStack.Participates.")]
		public readonly bool DangerEvac = false;

		[Desc("Danger-evac: believed ground-danger reading at/above which a truck pulls back. Set ABOVE the",
			"Stage-E reroute threshold — a reroute avoids exposure, an evac abandons a spot already too hot.")]
		public readonly int EvacDangerThreshold = 60;

		[Desc("Danger-evac: how far (cells) to pull the truck back toward its Supply Route when evacuating.")]
		public readonly int EvacRetreatCells = 12;

		[Desc("Actor types that count as the player's own Supply Route (the safe rear an evacuating truck",
			"pulls back toward). Only read when DangerEvac is on.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("TIER 2 (@experimental) IDLE-TRUCK HUNT. The follow path above only tasks a truck that has a",
			"CLUSTER to follow (MinNearbyFriendlies-strong); a truck with none parks and waits for units to",
			"wander into its aura. Tier 1 gave the infantry the legs (AutoSeekSupplies walks a dry soldier to",
			"a truck) — this closes the other half: an unassigned truck drives to the neediest STARVING",
			"soldier inside HuntLeashCells. INFANTRY ONLY, by construction rather than by a name list — the",
			"candidate must carry the truck's own RearmCondition (replenish-soldiers), which only soldiers",
			"hold; vehicles pull from the static Logistics Centre instead (replenish-vehicles, docked). No",
			"in-leash demand ⇒ no order ⇒ the truck stays put, so there is no cross-map wandering. Decision is",
			"the pure SupplyTruckHuntMath (NUnit-pinned), zero RNG. OFF by default; and because this is a",
			"SHARED enable-ai-any module whose Participates gate now admits @stable too, the flag is",
			"additionally confined to the @experimental player by an explicit BotType gate — @stable / Normal",
			"/ legacy take the identical old path and are byte-identical.")]
		public readonly bool IdleTruckHunt = false;

		[Desc("Idle-truck hunt: a soldier whose ammo pool sits below this many parts per thousand of capacity",
			"counts as starving (250 = 25%). Matches AutoSeekSupplies.AutoSeekAmmoThresholdPerMille so the",
			"truck and the soldier agree on who needs help. Only read when IdleTruckHunt is on.")]
		public readonly int HuntStarvingThresholdPerMille = 250;

		[Desc("Idle-truck hunt: furthest (cells, straight-line) a starving soldier can be and still be worth",
			"driving to. This is the bound on the sweep — same leash metric as AutoSeekSupplies, not a",
			"parallel one. Only read when IdleTruckHunt is on.")]
		public readonly int HuntLeashCells = 20;

		[Desc("Idle-truck hunt: shortfall band width in parts per thousand. Needs within one band tie and",
			"DISTANCE decides, so a landed ammo pip can't make the truck re-target across the sector every",
			"scan. 0 or 1 disables banding (raw shortfall order). Only read when IdleTruckHunt is on.")]
		public readonly int HuntNeedBandPerMille = 100;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). SupplyRouteTypes is a hardcoded
			// lowercase default (not user config), so it is left untouched.
			ActorNameCase.NormalizeInPlace(SupplyTruckTypes);
		}

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

		// Cached in Initialize: whether this player reads the influence stack (only @experimental bots / humans),
		// and whether the Stage-E two-leg reroute is live. Every new @experimental behaviour is double-gated on
		// `participates` so @stable/Normal/Rush/Turtle stay byte-identical.
		bool participates;
		bool routeViaDanger;

		// Tier 2 idle-truck hunt. Participates is NOT enough to confine this one: it admits @stable since the
		// 0802 promotion (ai.yaml:1335-1337), and this module is a single shared instance. Cached BotType gate,
		// same seam GarrisonBotModule uses for its shared-instance commit (GarrisonBotModule.cs:102).
		bool isExperimentalBot;

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

			// enable-ai-ANY module: only @experimental bots (and humans) read the influence stack. Cache the
			// participation gate once — every new @experimental behaviour double-gates on it so every other
			// profile is byte-identical.
			participates = InfluenceStack.Participates(player);

			// Tier 2 hunt: explicit bot-type gate (see the field comment) — never widened to Participates.
			isExperimentalBot = player.BotType == InfluenceStack.ExperimentalBotType;

			// Fetch the ground danger field if any believed-danger consumer (Stage-E reroute or danger evac) is
			// active. With DangerEvac at its default off, this is exactly the old condition.
			dangerField = participates && (Info.DangerFieldRouting || Info.DangerEvac)
				? world.WorldActor.TraitOrDefault<DangerFieldLayer>() : null;

			// The Stage-E two-leg reroute stays the old condition (DangerFieldRouting + a live field), so
			// enabling DangerEvac alone never flips a truck onto the reroute path.
			routeViaDanger = Info.DangerFieldRouting && dangerField != null;

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

			// @experimental small-squad coverage widens the servable-cluster floor so small squads (< the
			// default 4) become visible once big clusters are covered. Capped at MinNearbyFriendlies so it only
			// ever widens; off / non-participant profiles use the frozen floor → byte-identical.
			var minFriendlies = Info.SmallSquadCoverage && participates
				? Math.Max(1, Math.Min(Info.MinNearbyFriendlies, Info.SmallSquadMinNearbyFriendlies))
				: Info.MinNearbyFriendlies;

			// Find unit clusters by looking for groups of friendly units away from base
			var clusters = FindUnitClusters(friendlyUnits, minFriendlies);

			var spread = Info.SectorSpread && participates;
			var evac = Info.DangerEvac && dangerField != null;
			var hunt = SupplyTruckHuntMath.ShouldHunt(Info.IdleTruckHunt, isExperimentalBot);
			var maxFollowLength = WDist.FromCells(Info.MaxFollowDistance).Length;

			// @experimental sector spread: precompute distinct-cluster assignments over a STABLY sorted truck
			// list (ActorID) so the greedy result is enumeration-order-independent and deterministic.
			Dictionary<Actor, UnitCluster> spreadTargets = null;
			var orderedTrucks = trucks;
			if (spread && clusters.Count > 0)
			{
				orderedTrucks = trucks.OrderBy(t => t.ActorID).ToList();
				var truckPositions = orderedTrucks.Select(t => t.CenterPosition).ToList();
				var sectors = clusters.Select(c => new SupplyLogisticsMath.Sector(c.Center, NeedScore(c.AmmoNeed))).ToList();
				var assignment = SupplyLogisticsMath.AssignSectors(truckPositions, sectors, maxFollowLength);

				spreadTargets = new Dictionary<Actor, UnitCluster>();
				for (var i = 0; i < orderedTrucks.Count; i++)
					if (assignment[i] != SupplyLogisticsMath.NoSector)
						spreadTargets[orderedTrucks[i]] = clusters[assignment[i]];
			}

			// Own SR — the fog-legal safe rear an evacuating truck pulls back toward (our own actor).
			var srActor = evac ? FindOwnSupplyRoute() : null;

			foreach (var truck in orderedTrucks)
			{
				if (clusters.Count == 0)
				{
					// Hunt off: `break` as before — clusters.Count is loop-invariant, so bailing on the first
					// truck is the same as never entering the loop. Byte-identical.
					if (!hunt)
						break;

					HuntStarvingInfantry(truck);
					continue;
				}

				// Find the best cluster for this truck (closest cluster with ammo need)
				UnitCluster bestCluster = null;
				if (spread)
				{
					spreadTargets?.TryGetValue(truck, out bestCluster);
				}
				else
				{
					bestCluster = clusters
						.Where(c => (c.Center - truck.CenterPosition).Length < WDist.FromCells(Info.MaxFollowDistance).Length)
						.OrderByDescending(c => c.AmmoNeed)
						.ThenBy(c => (c.Center - truck.CenterPosition).LengthSquared)
						.FirstOrDefault();
				}

				// Tier 2: an unassigned truck hunts rather than parking. Hunt off ⇒ plain `continue`, the
				// old behaviour for both the no-spread-target and the no-in-range-cluster cases.
				if (bestCluster == null)
				{
					if (hunt)
						HuntStarvingInfantry(truck);

					continue;
				}

				// @experimental danger evac: a truck whose follow position (or cluster centroid) reads high
				// believed ground danger pulls back toward its SR rather than dying in place. Fog-legal.
				if (evac && srActor != null && ShouldEvacuate(truck, bestCluster))
				{
					var retreat = SupplyLogisticsMath.RetreatTarget(
						truck.CenterPosition, srActor.CenterPosition, WDist.FromCells(Info.EvacRetreatCells).Length);
					bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, world.Map.CellContaining(retreat)), false));
					lastVia.Remove(truck);

					if (!activeTrucks.Contains(truck))
					{
						activeTrucks.Add(truck);
						if (blackboard != null)
							blackboard.ClaimUnit(truck, "supply-follow");
					}

					continue;
				}

				// Find a safe position behind the cluster (away from enemy threat)
				var followPos = FindSafeFollowPosition(bestCluster);

				if (followPos.HasValue)
				{
					if (!routeViaDanger)
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

		/// <summary>
		/// Tier 2 idle-truck hunt: drive an unassigned truck to the neediest starving soldier inside its
		/// leash. Called only for a truck the follow pass left with nothing to do, and only for the
		/// @experimental bot with IdleTruckHunt on.
		///
		/// Infantry only, and by construction rather than by a name list: the candidate must carry the
		/// truck's OWN RearmCondition — replenish-soldiers for TRUK (vehicles.yaml:546), an
		/// ExternalCondition only soldiers hold (infantry.yaml:214-215). A vehicle therefore never appears
		/// as demand here, which is correct: the only provider that serves replenish-vehicles is the static
		/// Logistics Centre (structures.yaml:394), and it is docking-gated, so vehicles PULL and trucks
		/// cannot push to them.
		///
		/// The candidate scan is a leash-radius spatial query, so the bound holds twice over: FindActorsInCircle
		/// applies the identical inclusive squared-distance filter SupplyHuntMath.WithinLeash does, and the
		/// pure selection re-checks it. No candidate ⇒ no order ⇒ the truck stays put.
		/// </summary>
		void HuntStarvingInfantry(Actor truck)
		{
			var provider = truck.TraitOrDefault<SupplyProvider>();

			// CanServeNow is the provider's own serving ladder — a truck that is paused, mid-restock or
			// reserving its remainder for the drive home would arrive with nothing to give.
			if (provider == null || provider.CountsAsEmpty || !provider.CanServeNow)
				return;

			// No recipient-side condition means no way to tell infantry demand from vehicle demand, and a
			// truck without one would push to anything — don't guess.
			var rearmCondition = provider.Info.RearmCondition;
			if (string.IsNullOrEmpty(rearmCondition))
				return;

			var demands = new List<SupplyTruckHuntMath.Demand>();
			var candidates = new List<Actor>();

			foreach (var a in world.FindActorsInCircle(truck.CenterPosition, WDist.FromCells(Info.HuntLeashCells)))
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld || a == truck)
					continue;

				if (Info.SupplyTruckTypes.Contains(a.Info.Name))
					continue;

				var rearmable = a.TraitOrDefault<Rearmable>();
				if (rearmable == null)
					continue;

				if (!a.TraitsImplementing<ExternalCondition>().Any(e => e.Info.Condition == rearmCondition))
					continue;

				// Worst starving pool we can actually afford a batch of. An unaffordable pool is not demand
				// this truck can relieve, so it must not pull it out of position.
				var shortfall = 0;
				foreach (var pool in rearmable.RearmableAmmoPools)
				{
					if (provider.CurrentSupply < pool.Info.SupplyValue)
						continue;

					if (!SupplyTruckHuntMath.IsStarving(pool.CurrentAmmoCount, pool.Info.Ammo, Info.HuntStarvingThresholdPerMille))
						continue;

					var s = SupplyTruckHuntMath.ShortfallPerMille(pool.CurrentAmmoCount, pool.Info.Ammo);
					if (s > shortfall)
						shortfall = s;
				}

				if (shortfall == 0)
					continue;

				var distanceSquared = (a.CenterPosition - truck.CenterPosition).HorizontalLengthSquared;
				demands.Add(new SupplyTruckHuntMath.Demand(distanceSquared, shortfall, a.ActorID));
				candidates.Add(a);
			}

			var pick = SupplyTruckHuntMath.SelectDemand(demands, Info.HuntLeashCells, Info.HuntNeedBandPerMille);
			if (pick == SupplyTruckHuntMath.NoDemand)
				return;

			// Already covering him: the push is reaching him where the truck stands, so issue nothing rather
			// than nudging a serving truck onto his cell every scan.
			if (!SupplyTruckHuntMath.NeedsApproach(demands[pick].DistanceSquared, provider.Info.Range.LengthSquared))
				return;

			var target = candidates[pick];
			bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, world.Map.CellContaining(target.CenterPosition)), false));
			lastVia.Remove(truck);

			if (!activeTrucks.Contains(truck))
			{
				activeTrucks.Add(truck);
				blackboard?.ClaimUnit(truck, "supply-follow");
			}
		}

		List<UnitCluster> FindUnitClusters(List<Actor> units, int minFriendlies)
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

				if (nearby.Count < minFriendlies)
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

		// The player's own Supply Route (our own actor — fog-legal to read), the safe rear an evacuating truck
		// pulls back toward. Null before one exists / if it was lost, in which case evac is skipped.
		Actor FindOwnSupplyRoute()
		{
			return world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.SupplyRouteTypes.Contains(a.Info.Name));
		}

		// Believed ground danger (DangerFieldLayer) at the truck and at its target cluster centroid, vs the
		// evac threshold. Fog-legal by construction; dangerField is non-null only for participating profiles.
		bool ShouldEvacuate(Actor truck, UnitCluster cluster)
		{
			var dangerAtTruck = dangerField.GroundDanger(player, truck.Location);
			var dangerAtCluster = dangerField.GroundDanger(player, cluster.CenterCell);
			return SupplyLogisticsMath.ShouldEvacuate(dangerAtTruck, dangerAtCluster, Info.EvacDangerThreshold);
		}

		// Scale the float AmmoNeed to a stable non-negative integer for the deterministic sector assignment.
		// Only used on the @experimental spread path, so it never touches the byte-identical base ordering.
		static int NeedScore(float ammoNeed)
		{
			var s = (int)(ammoNeed * 1000f);
			return s < 0 ? 0 : s;
		}

		bool IsClaimedByOtherModule(Actor a)
		{
			if (blackboard == null)
				return false;

			var claimant = blackboard.GetUnitClaimant(a);
			return claimant != null && claimant != "supply-follow";
		}

		// A truck below its RestockThreshold — or one holding an unusable residue that
		// counts as empty — has effectively no supplies to give. Don't issue forward
		// orders: SupplyProvider's restock / the transport's evacuate will route it away
		// if we leave it alone. Issuing a forward Move would fight that and re-park it.
		static bool IsLowOnSupply(Actor a)
		{
			var sp = a.TraitOrDefault<SupplyProvider>();
			if (sp == null)
				return false;
			return sp.CurrentSupply < sp.Info.RestockThreshold || sp.CountsAsEmpty;
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
