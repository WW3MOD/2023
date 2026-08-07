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
			"InfluenceStack.Participates — which admits BOTH fog-respecting profiles (@experimental and,",
			"since the 2026-08-02 parity promotion, @stable); only Normal/Rush/Turtle and legacy are",
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

		[Desc("Sector spread: when several trucks are free, greedily assign each to a DISTINCT unit cluster",
			"(neediest first) instead of every truck piling onto the same blob; only double up when trucks",
			"outnumber clusters. This is a shared enable-ai-ANY module, so it is additionally gated on",
			"InfluenceStack.Participates — which admits @stable as well as @experimental. OFF by default; only",
			"Normal/Rush/Turtle and legacy profiles are byte-identical.")]
		public readonly bool SectorSpread = false;

		[Desc("Small-squad coverage: lower the servable-cluster floor to SmallSquadMinNearbyFriendlies (below",
			"MinNearbyFriendlies) so small squads become visible to the follower once the big clusters are",
			"covered. OFF by default; gated on InfluenceStack.Participates (both fog-respecting profiles).")]
		public readonly bool SmallSquadCoverage = false;

		[Desc("Minimum friendlies to form a servable cluster when SmallSquadCoverage is on. Only applied for",
			"participating (fog-respecting bot / human) profiles; capped at MinNearbyFriendlies so it only widens.")]
		public readonly int SmallSquadMinNearbyFriendlies = 2;

		[Desc("Danger evac: when the believed ground danger at the truck (or its target cluster centroid)",
			"reaches EvacDangerThreshold, retreat the truck toward its Supply Route instead of idling in the",
			"fire. Fog-legal — reads DangerFieldLayer only, never an omniscient enemy scan. OFF by default;",
			"gated on InfluenceStack.Participates, which since the 2026-08-02 @stable parity promotion admits",
			"@stable as well as @experimental — so this is NOT @experimental-only, and the damper below is",
			"therefore load-bearing on both fog-respecting profiles.")]
		public readonly bool DangerEvac = false;

		[Desc("Danger-evac: believed ground-danger reading at/above which a truck pulls back. Set ABOVE the",
			"Stage-E reroute threshold — a reroute avoids exposure, an evac abandons a spot already too hot.",
			"Doubles as the SELECTION filter: a cluster whose centroid is at/above this never becomes a follow",
			"target, so the module cannot pick a cluster it is about to refuse to approach.")]
		public readonly int EvacDangerThreshold = 60;

		[Desc("Danger-evac: how far (cells) to pull the truck back toward its Supply Route when evacuating.")]
		public readonly int EvacRetreatCells = 12;

		[Desc("Danger-evac damper: scans an evacuating truck holds the evac decision before the branch may be",
			"re-decided. Sized so the branch is not flipped mid-leg: at TRUK's speed a 12-cell retreat is",
			"~164 ticks plus the acceleration ramp, against a 150-tick scan — so one held scan puts the",
			"re-decision at the first boundary AFTER the leg completes, and a larger value only delays the",
			"truck's return to useful work. (Bounding the retreat DISTANCE is a separate mechanism — see",
			"StepEvac's leg model — so this does not need to cover the drive.) ENTERING an evac is never",
			"delayed by this; only the return to following is. 0 disables the dwell.")]
		public readonly int EvacDwellScans = 1;

		[Desc("Danger-evac damper: how far below EvacDangerThreshold the danger must fall before an evacuating",
			"truck follows again. Without this deadband a reading parked on the threshold flips the branch on",
			"alternate scans. Clamped so the release level is never below 1.")]
		public readonly int EvacReleaseHysteresis = 15;

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

			// Case-harden actor-name config (see ActorNameCase). SupplyRouteTypes ships a lowercase default
			// but IS overridable from YAML, and a mis-cased override there fails SILENTLY and expensively:
			// FindOwnSupplyRoutes matches nothing, so the whole evac path — damper included — is skipped
			// while the danger gate on selection keeps running.
			ActorNameCase.NormalizeInPlace(SupplyTruckTypes);
			ActorNameCase.NormalizeInPlace(SupplyRouteTypes);
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

		// Read for its grid GEOMETRY only (GroundDangerAt's lattice de-aliasing), never for control scores.
		ControlField controlField;
		int scanCountdown;
		bool initialized;

		// Cached in Initialize: whether this player reads the influence stack, and whether the Stage-E two-leg
		// reroute is live. NOTE `participates` is NOT an @experimental-only gate — since the 2026-08-02 @stable
		// parity promotion (b8d2e601) InfluenceStack.Participates admits BOTH fog-respecting bot profiles
		// (InfluenceStack.cs:47-48), so a flag gated on it alone reaches @stable too. Only Normal/Rush/Turtle
		// and legacy profiles stay byte-identical. Behaviour that must be @experimental-only needs the explicit
		// BotType gate below instead.
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

		// Danger-evac damper state per truck: whether it is currently on the evac branch, and how many scans
		// that decision is still committed for. Absent = following, no dwell. Read only for the truck being
		// processed (never enumerated for a decision), so it adds no ordering dependence.
		readonly Dictionary<Actor, EvacState> evacState = new Dictionary<Actor, EvacState>();

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

			// enable-ai-ANY module: cache the participation gate once. See the field comment — this admits
			// @stable as well as @experimental, so it narrows to "the fog-respecting profiles", not to
			// @experimental alone.
			participates = InfluenceStack.Participates(player);

			// Tier 2 hunt: explicit bot-type gate (see the field comment) — never widened to Participates.
			isExperimentalBot = player.BotType == InfluenceStack.ExperimentalBotType;

			// Fetch the ground danger field if any believed-danger consumer (Stage-E reroute or danger evac) is
			// active. With DangerEvac at its default off, this is exactly the old condition.
			dangerField = participates && (Info.DangerFieldRouting || Info.DangerEvac)
				? world.WorldActor.TraitOrDefault<DangerFieldLayer>() : null;

			// Grid geometry for GroundDangerAt's de-aliasing. Null is tolerated (raw single-cell reads).
			controlField = dangerField != null ? world.WorldActor.TraitOrDefault<ControlField>() : null;

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

			// Clean up dead trucks, and low-supply ones (see IsLowOnSupply — they are released because they
			// have nothing left to give, NOT because a restock is waiting to claim them). The blackboard
			// claim is handed back with them: dropping a truck from the roster while keeping its claim left
			// it alive-and-claimed forever, invisible to every other claim-respecting module (the 2026-08-04
			// entry in WORKSPACE/bugs/discovered.md — the same defect fixed in GarrisonBotModule this pass).
			var dropped = activeTrucks
				.Where(a => a == null || a.IsDead || !a.IsInWorld || IsLowOnSupply(a))
				.ToList();

			foreach (var a in dropped)
			{
				activeTrucks.Remove(a);
				if (a != null && blackboard != null && blackboard.IsUnitClaimedBy(a, "supply-follow"))
					blackboard.ReleaseUnit(a);
			}

			// Keep the per-truck deadband / damper memory bounded to trucks still on active follow duty.
			if (lastVia.Count > 0)
			{
				var stale = lastVia.Keys.Where(a => !activeTrucks.Contains(a)).ToList();
				foreach (var a in stale)
					lastVia.Remove(a);
			}

			if (evacState.Count > 0)
			{
				var stale = evacState.Keys.Where(a => !activeTrucks.Contains(a)).ToList();
				foreach (var a in stale)
					evacState.Remove(a);
			}

			// Find all supply trucks — eligible only if they actually have supplies to give. An empty truck
			// driven forward just arrives at the front with nothing, so filter them out.
			//
			// CORRECTION (2026-08-07): this filter used to be justified by "SupplyProvider auto-restocks by
			// queuing a MoveTo(LC) and a forward Move would cancel it". That is FALSE for AI trucks. Every
			// TryRestock call site is gated on ShouldSelfRestock (SupplyProvider.cs:330-338), which returns
			// false under ResupplyBehavior.Evacuate — and TRUK ships InitialResupplyBehaviorAI: Evacuate
			// (vehicles.yaml:514-516). There is no restock activity to protect here; what actually picks a
			// released truck up is DropsSupplyCache, which drives it to the MAP EDGE and sells it. The filter
			// is still right, but for the plain reason above, not the one it used to claim.
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

			// Selection is gated at the RELEASE level, not the entry threshold. Gating at the entry threshold
			// while releasing lower leaves the whole band between them as a latch — see the two-levels note in
			// SupplyLogisticsMath's header. Same number on both sides, so the truck is only ever sent somewhere
			// it would not immediately leave.
			var releaseLevel = SupplyLogisticsMath.ReleaseLevel(Info.EvacDangerThreshold, Info.EvacReleaseHysteresis);

			// DECORRELATE SELECTION FROM REJECTION. Cluster choice below is need-descending, and the neediest
			// cluster is the one that has been fighting — i.e. the one deepest in believed danger, which is
			// exactly what the evac rule then refuses to approach. Selecting a cluster the module is about to
			// reject is what turned the evac branch into a limit cycle (SupplyLogisticsMath's EVAC DAMPER note).
			// Gating on danger BEFORE selection makes the two criteria consistent by construction rather than
			// merely less correlated. Chosen over softening the merit (need weighted against danger) because a
			// weighting always leaves inputs where a big enough need still selects a vetoed cluster, so the
			// cycle survives at lower frequency; a gate leaves none.
			if (evac)
			{
				foreach (var c in clusters)
				{
					c.FollowCell = FindSafeFollowPosition(c) ?? c.CenterCell;
					c.Danger = GroundDangerAt(c.FollowCell);
				}

				clusters = SelectServableClusters(clusters, releaseLevel);
			}

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

			// Own SRs — the fog-legal safe rear an evacuating truck pulls back toward (our own actors). A
			// player can hold more than one, so the NEAREST is picked per truck inside the loop.
			var supplyRoutes = evac ? FindOwnSupplyRoutes() : null;

			foreach (var truck in orderedTrucks)
			{
				// Find the best cluster for this truck (closest cluster with ammo need). Null when there are
				// no clusters at all, none in range, or the spread left this truck unassigned.
				UnitCluster bestCluster = null;
				if (clusters.Count > 0)
				{
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
				}

				// Danger evac, damped. Deliberately evaluated BEFORE the no-cluster bail and with a possibly
				// null cluster: the relief valve can still leave a truck with no target (nothing needs ammo),
				// and a truck standing in fire must be able to pull back regardless. Pre-damper this case fell
				// through unevacuated.
				var srActor = evac ? NearestSupplyRoute(supplyRoutes, truck.CenterPosition) : null;
				if (srActor != null)
				{
					if (StepEvac(truck, srActor, bestCluster))
						continue;
				}
				else
					evacState.Remove(truck);

				// Tier 2: an unassigned truck hunts rather than parking. Hunt off ⇒ plain `continue`, the
				// old behaviour for both the no-spread-target and the no-in-range-cluster cases.
				if (bestCluster == null)
				{
					if (hunt)
						HuntStarvingInfantry(truck);

					continue;
				}

				// The follow cell. On the evac path it was already resolved (and danger-gated) above, so reuse
				// it rather than recomputing — the gate must apply to the cell actually ordered.
				var followPos = evac ? (CPos?)bestCluster.FollowCell : FindSafeFollowPosition(bestCluster);

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
		/// truck's OWN RearmCondition — replenish-soldiers for TRUK (vehicles.yaml:546), which only soldiers
		/// HOLD as an ExternalCondition (infantry.yaml:215). LOGISTICSCENTER names the same condition
		/// (structures.yaml:382-386) but as a ProximityExternalCondition GRANTER, which is not an
		/// ExternalCondition subclass — so the TraitsImplementing&lt;ExternalCondition&gt; scan below does not
		/// match it, and the building never reads as demand. A vehicle therefore never appears
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

			// Stop as soon as he is inside the push aura (less a cell of margin), not on top of him — the
			// last aura's worth of driving buys nothing and this sweep runs precisely where the line has
			// come apart. The margin is also what keeps the order from stalling on cell quantization; the
			// reasoning lives with the constant, in ApproachTarget.
			var target = candidates[pick];
			var stopPosition = SupplyTruckHuntMath.ApproachTarget(truck.CenterPosition, target.CenterPosition, provider.Info.Range.Length);
			bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, world.Map.CellContaining(stopPosition)), false));
			lastVia.Remove(truck);

			if (!activeTrucks.Contains(truck))
			{
				activeTrucks.Add(truck);
				blackboard?.ClaimUnit(truck, "supply-follow");
			}
		}

		/// <summary>Narrow the clusters to the ones actually worth sending a truck to, as a RELIEF-VALVED
		/// filter rather than a hard veto. Only reached when evac is live, so non-participating profiles keep
		/// the old candidate set exactly.
		///
		/// <para>THE VALVE IS THE ORDINARY IN-CONTACT PATH, NOT A CORNER CASE — read the two branches as equal
		/// partners. GroundDangerAt lifts every cell to at least its control-block baseline, that baseline
		/// stacks additively past 40 in a dense sector (DOCS/reference/influence-stack.md, Stage B), and the
		/// gate sits at 45 — so anywhere near a contested frontier the servable set is routinely EMPTY before
		/// a single weapon is counted. Add one contact kernel and the relieved cluster is at or above the evac
		/// entry threshold, which is to say: the valve fires precisely when a cluster is in a firefight, which
		/// is precisely when resupply matters most. The resulting design is deliberate and is the whole
		/// contract of this module's danger handling: GATE TO COMFORTABLE CELLS WHERE ANY EXIST, OTHERWISE
		/// APPROACH THE LEAST-BAD ONE AND ABORT ON THE TRUCK'S OWN READING. Treating the second branch as a
		/// rare fallback is how it ends up feeding ungated readings into gates that assume bounded input.</para>
		///
		/// <para>Two things a plain "drop everything over the threshold" gets wrong. First, a hard veto can
		/// empty the set, and an empty set is not a safe default — the truck falls through to a bare
		/// <c>continue</c> and PARKS on every profile except @experimental, because the idle-truck hunt that
		/// would otherwise catch it is behind an explicit BotType gate. Starving the resupply because the
		/// front is hot is a worse failure than approaching carefully, so when nothing is servable the least
		/// dangerous cluster that actually needs ammo is handed back anyway and the (undamped) evac entry test
		/// is left to stop the truck if it genuinely becomes too hot.</para>
		///
		/// <para>Second, FindUnitClusters applies no need gate at all, so a full-ammo rear cluster is a
		/// candidate with AmmoNeed 0. Need-descending ordering hides that while a needy cluster is present —
		/// but veto the needy one and the truck drives, confidently, to units that need nothing. Requiring
		/// real need makes "no servable cluster" mean what it says.</para></summary>
		static List<UnitCluster> SelectServableClusters(List<UnitCluster> clusters, int releaseLevel)
		{
			var needy = clusters.Where(c => NeedScore(c.AmmoNeed) > 0).ToList();
			if (needy.Count == 0)
				return needy;

			var servable = needy.Where(c => c.Danger < releaseLevel).ToList();
			if (servable.Count > 0)
				return servable;

			// Relief valve: nothing is comfortably approachable, so fall back to the least dangerous needy
			// cluster(s) rather than abandoning resupply entirely. Ties are kept so the ordinary need-desc
			// selection still decides between equally-safe clusters; min over a list is order-independent.
			//
			// Marked Relieved because these clusters did NOT pass the gate — Danger here is unbounded and is
			// routinely at or above the evac entry threshold. StepEvac must therefore not feed it to the evac
			// decision (SupplyLogisticsMath.DestinationDanger); the truck approaches under its own reading
			// instead, which is exactly the contract in this method's summary.
			var minDanger = needy.Min(c => c.Danger);
			var relieved = needy.Where(c => c.Danger == minDanger).ToList();
			foreach (var c in relieved)
				c.Relieved = true;

			return relieved;
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

		// The player's own Supply Routes (our own actors — fog-legal to read), the safe rear an evacuating
		// truck pulls back toward. A player can hold SEVERAL (the starting beachhead plus any captured
		// neutral ones, per DOCS/reference/game-model.md), so this returns all of them and the caller picks
		// the nearest per truck — see NearestSupplyRoute. Ordered by ActorID for a deterministic tie-break.
		List<Actor> FindOwnSupplyRoutes()
		{
			return world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& Info.SupplyRouteTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.ToList();
		}

		// The nearest SR to a truck. Taking the FIRST one instead is a correctness bug, not a nicety: with
		// several SRs the first by spawn order can be on the far side of the map, and RetreatTarget steps
		// the truck TOWARD it — i.e. an "evacuation" that drives through or past the front, which the
		// undamped entry test then re-triggers the whole way. Strict `<` keeps the lowest ActorID on a tie,
		// so the choice is deterministic given the caller's ActorID ordering.
		static Actor NearestSupplyRoute(List<Actor> supplyRoutes, WPos from)
		{
			Actor best = null;
			var bestDistanceSquared = 0L;

			foreach (var sr in supplyRoutes)
			{
				var distanceSquared = (sr.CenterPosition - from).HorizontalLengthSquared;
				if (best == null || distanceSquared < bestDistanceSquared)
				{
					best = sr;
					bestDistanceSquared = distanceSquared;
				}
			}

			return best;
		}

		/// <summary>Believed ground danger at a cell, DE-ALIASED against the control-grid lattice. Every
		/// binary gate in this module reads the field through here so entry and exit cannot disagree.
		///
		/// <para>The Stage-B territory baseline is stamped only at each control-grid cell's CENTRE map cell —
		/// <c>ControlField.GridCellToMapCell(gx, gy) = (gx * CellSize + CellSize / 2, ...)</c> — so at the
		/// shipping CellSize of 2 only map cells with BOTH coordinates odd carry any baseline at all, and
		/// three of every four read zero. That baseline is not small: it stamps additively from every frontier
		/// cell and "a dense sector's baseline stacks and can exceed 40 easily"
		/// (DOCS/reference/influence-stack.md, Stage B). So near a contested frontier a single-cell read can
		/// swing by more than 40 on a ONE-CELL move purely on lattice parity — against a 15-unit release
		/// hysteresis, that is quantisation noise nearly 3x the deadband, and a threshold gate reading one
		/// cell decides on parity rather than on danger.</para>
		///
		/// <para>Taking the MAX over the cell and its grid-centre representative recovers the stamped baseline
		/// for every member of the block while keeping the densely-stamped contact kernel at the cell itself.
		/// MIN is simply unsafe — it reports the unstamped member, i.e. it under-reports danger, which is the
		/// wrong direction for a safety gate. A MEAN over the block would NOT reintroduce parity (a mean over
		/// a fixed block is uniform within it); the objection to it is different and is about the other term:
		/// it dilutes the densely-stamped CONTACT kernel across four cells, roughly quartering the local peak
		/// that the gate exists to notice. MAX is the only one of the three that preserves both terms.</para></summary>
		int GroundDangerAt(CPos cell)
		{
			var danger = dangerField.GroundDanger(player, cell);
			if (controlField == null)
				return danger;

			var (gx, gy) = controlField.MapCellToGridCell(cell);
			var representative = controlField.GridCellToMapCell(gx, gy);
			if (representative == cell)
				return danger;

			return Math.Max(danger, dangerField.GroundDanger(player, representative));
		}

		/// <summary>Run the DAMPED danger-evac decision for one truck and, when it is on the evac branch, issue
		/// the retreat. Returns true when the caller should skip the follow path this scan.
		///
		/// Believed ground danger (DangerFieldLayer) at the truck and at its target cluster centroid drives the
		/// decision — fog-legal by construction; dangerField is non-null only for participating profiles.
		///
		/// Two pieces of memory live here, and they are what stop the branch oscillating (the full reasoning is
		/// in SupplyLogisticsMath's EVAC DAMPER note):
		///   * the DWELL latches the branch for EvacDwellScans so a retreat already ordered is not re-decided
		///     while it is still being driven, and the RELEASE DEADBAND then requires danger to fall clear of
		///     the threshold before the truck follows again. Entering an evac is never delayed by either.
		///   * the retreat is issued ONE LEG AT A TIME. RetreatTarget steps EvacRetreatCells from the truck's
		///     OWN position, so recomputing it every scan is a receding target — at ~11 cells covered per
		///     150-tick scan against a 12-cell leg the truck never arrives and simply walks to the SR, which
		///     is the pre-fix failure verbatim. A leg is therefore issued once and then left alone until it
		///     has actually been driven (or the truck went idle because it could not be), at which point the
		///     next one is stepped. The dwell alone does NOT bound this: the counter arms on the entry edge,
		///     so from the scan after it expires every scan would re-issue.
		/// </summary>
		bool StepEvac(Actor truck, Actor srActor, UnitCluster cluster)
		{
			// An entry exists exactly while the truck is on the evac branch (it is removed on release below).
			var wasEvacuating = evacState.TryGetValue(truck, out var state);
			var heldBefore = wasEvacuating ? state.Hold : 0;

			var dangerAtTruck = GroundDangerAt(truck.Location);

			// The cell the truck is being SENT to — but read ONLY when that cell passed the danger gate this
			// scan. No cluster, or a cluster that came through the relief valve, contributes nothing: the
			// term exists to catch the front arriving on a GATED destination, and an ungated reading in the
			// entry test pins the branch true forever regardless of where the truck drives. The reasoning,
			// and why 0 is the valve's contract rather than a fudge, is on DestinationDanger.
			var destinationWasGated = cluster is { Relieved: false };
			var dangerAtDestination = SupplyLogisticsMath.DestinationDanger(destinationWasGated, cluster?.Danger ?? 0);

			var evacNow = SupplyLogisticsMath.EvacuateWithDwell(wasEvacuating, heldBefore,
				dangerAtTruck, dangerAtDestination, Info.EvacDangerThreshold, Info.EvacReleaseHysteresis);

			if (!evacNow)
			{
				evacState.Remove(truck);
				return false;
			}

			var hold = SupplyLogisticsMath.StepEvacDwell(heldBefore, !wasEvacuating, Info.EvacDwellScans);

			// Step a new leg on entry, and thereafter only once the previous one has been driven. The arrival
			// tolerance reuses RepathThresholdCells, the same deadband the Stage-E detour uses one branch
			// over. IsIdle is the second half and is not optional: a truck whose Move failed (blocked cell,
			// no path) never reaches the target, and without it the leg model would strand it in the danger
			// it was trying to leave.
			var retreatCell = state.Retreat;
			var legDriven = !wasEvacuating
				|| truck.IsIdle
				|| (truck.Location - retreatCell).LengthSquared <= Info.RepathThresholdCells * Info.RepathThresholdCells;

			if (legDriven)
			{
				var retreat = SupplyLogisticsMath.RetreatTarget(
					truck.CenterPosition, srActor.CenterPosition, WDist.FromCells(Info.EvacRetreatCells).Length);
				retreatCell = world.Map.CellContaining(retreat);
				bot.QueueOrder(new Order("Move", truck, Target.FromCell(world, retreatCell), false));
				lastVia.Remove(truck);
			}

			evacState[truck] = new EvacState(hold, retreatCell);

			if (!activeTrucks.Contains(truck))
			{
				activeTrucks.Add(truck);
				blackboard?.ClaimUnit(truck, "supply-follow");
			}

			return true;
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

		// A truck below its RestockThreshold — or one holding an unusable residue that counts as empty — has
		// effectively no supplies to give, so don't issue forward orders for it.
		//
		// CORRECTION (2026-08-07): the old comment here claimed "SupplyProvider's restock / the transport's
		// evacuate will route it away if we leave it alone". Only the second half is true for AI trucks. The
		// restock half is inert — ShouldSelfRestock (SupplyProvider.cs:330-338) returns false under
		// ResupplyBehavior.Evacuate, which is TRUK's AI default. Releasing the truck hands it to
		// DropsSupplyCache, which sends it to the map edge; it is NOT going to refill and come back.
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
			evacState.Clear();
		}

		// Danger-evac damper state for one truck on the evac branch: scans the branch is still committed for
		// (0 = free to re-decide next scan) and the retreat cell its current leg was ordered to. Presence in
		// the dictionary IS the "currently evacuating" flag.
		readonly struct EvacState
		{
			public readonly int Hold;
			public readonly CPos Retreat;

			public EvacState(int hold, CPos retreat)
			{
				Hold = hold;
				Retreat = retreat;
			}
		}

		class UnitCluster
		{
			public WPos Center;
			public CPos CenterCell;
			public int UnitCount;
			public float AmmoNeed;

			// The cell a truck assigned here would actually be SENT to, and the believed ground danger there.
			// Gating on the follow cell rather than the centroid matters: FindSafeFollowPosition scores a
			// +/-3 box by ThreatMapManager.GetThreat (enemyValue - friendlyValue), and MINIMISING that
			// deliberately prefers the friendliest-dense cell — i.e. it walks toward the contact line, up to
			// ~4 cells off the centroid. A centroid reading safe can therefore hand out a follow cell that is
			// not, which would put the veto and the destination on two different quantities.
			public CPos FollowCell;
			public int Danger;

			// True when this cluster reached selection through the relief valve rather than through the
			// danger gate — i.e. Danger is NOT bounded by the gate and may sit at or above the evac entry
			// threshold. The evac decision must not read the destination of such a cluster; see
			// SupplyLogisticsMath.DestinationDanger for why that is a latch rather than a conservatism.
			public bool Relieved;
		}
	}
}
