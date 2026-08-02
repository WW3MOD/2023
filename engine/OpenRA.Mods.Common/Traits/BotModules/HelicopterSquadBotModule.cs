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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits.BotModules.Squads;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Manages AI helicopter squads with role-based behavior (attack, scout, transport).",
		"Helicopters are grouped into squads based on their AIHelicopterRole trait and managed independently from ground units.")]
	public class HelicopterSquadBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Minimum attack helicopters needed before launching an attack mission.")]
		public readonly int AttackSquadSize = 2;

		[Desc("Random bonus added to attack squad size.")]
		public readonly int AttackSquadSizeBonus = 1;

		[Desc("Ticks between launching attack missions.")]
		public readonly int AttackCooldown = 900;

		[Desc("Ticks between scout missions.")]
		public readonly int ScoutInterval = 400;

		[Desc("Ticks between transport missions.")]
		public readonly int TransportInterval = 600;

		[Desc("Minimum infantry to load before launching a transport mission.")]
		public readonly int TransportMinInfantry = 4;

		[Desc("Maximum number of active helicopter squads at once.")]
		public readonly int MaxActiveSquads = 3;

		[Desc("Ticks between checking helicopter pool for new assignments.")]
		public readonly int ScanInterval = 100;

		[Desc("Ticks between updating active squads.")]
		public readonly int SquadUpdateInterval = 5;

		[Desc("Skip the full-ammo readiness gate when launching missions. WW3MOD attack helis only refill",
			"at an hpad and the mod builds none, so a heli below full ammo can NEVER become mission-ready —",
			"no squad ever forms and the helicopters idle forever. This is the squad-path twin of the",
			"production-side SkipRearmBuildingCheck trap. OFF by default so legacy/normal/stable behaviour is",
			"unchanged; only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool SkipRearmReadyCheck = false;

		[Desc("Use standoff (attack-move) engagement for attack-heli squads. When on, the squad FSM issues",
			"AttackMove toward the target cell instead of a bare Attack on a single (possibly distant) target,",
			"so AutoTarget engages the nearest in-range threat at weapon standoff and the squad only advances",
			"when clear — helis stop and fire at missile range instead of overflying nearer enemies to reach a",
			"distant target. OFF by default so legacy/normal/stable behaviour is byte-identical; only",
			"HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool StandoffEngagement = false;

		[Desc("Influence stack Stage D: consume the per-player ANTI-AIR danger field (DangerFieldLayer) so",
			"attack-heli squads route AROUND believed AA, leash their standoff to the AA-safe envelope, and",
			"withdraw/re-route when a NEW AA threat lights up on the field mid-flight. Rides on top of",
			"StandoffEngagement (only takes effect while that is on). OFF by default so legacy/normal/stable",
			"behaviour is byte-identical; only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool DangerFieldAvoidance = false;

		[Desc("Stage-D: cell air-danger at or below which a cell is treated as AA-safe (leash target /",
			"acceptable detour). 0 = strictly outside every believed anti-air envelope. Air-only tunable.")]
		public readonly int AirDangerSafeThreshold = 0;

		[Desc("Stage-D: air-danger at the squad's own position above which a newly-believed AA is taken to",
			"cover the squad and it withdraws / re-routes. Above SafeThreshold so leash grazing does not flap.")]
		public readonly int AirDangerSpikeThreshold = 30;

		[Desc("Stage-D: how far from the target (cells) to search for an AA-safe standoff cell to leash to.")]
		public readonly int AirDangerLeashCells = 6;

		[Desc("Stage-D: lateral offset magnitude (cells) for detour waypoints that route around AA.")]
		public readonly int AirDangerDetourCells = 6;

		[Desc("Stage-D: ring radius (cells) searched for the safest air-aware retreat cell on withdraw.")]
		public readonly int AirDangerRetreatCells = 12;

		[Desc("Influence-stack frontier standoff: hold the attack-heli standoff at least this many COARSE",
			"control-field cells behind the believed enemy frontier (ControlField distance-to-enemy-region).",
			"When the leashed engage cell lands closer than this, it is walked rearward (bounded) toward the",
			"squad so helis hold BEHIND the believed front line, not on it. Rides on StandoffEngagement.",
			"0 = OFF (default; byte-identical). Inert until a ControlField is populated for this player.")]
		public readonly int MinFrontierDistanceCells = 0;

		[Desc("Experimental (default false = frozen): when an idle attack heli is still loitering",
			"within ForwardStagingMaxDistanceCells of its own Supply Route and no squad has formed,",
			"push it forward to a pre-contact staging cell (a fraction of the way from the SR toward",
			"the top PoiMap offensive target) instead of leaving it hovering at the SR corner. Mirrors",
			"MountedTransportBotModule.DeliverBeforeContact. OFF by default so normal/rush/turtle/stable",
			"stay byte-identical; only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool ForwardStaging = false;

		[Desc("Fraction (percent) of the SR->top-offensive-POI distance used as the staging cell.",
			"50 = halfway between our SR and the top offensive POI. Clamp well short of contact so",
			"ammo-carrying, target-less helis do not stage into believed AA. Only used when ForwardStaging is set.")]
		public readonly int ForwardStagingPct = 40;

		[Desc("Only stage attack helis whose distance from the SR is at or below this (map cells).",
			"Helis already forward (e.g. a low-ammo heli that returned near the front) are left alone.",
			"Only used when ForwardStaging is set.")]
		public readonly int ForwardStagingMaxDistanceCells = 8;

		[Desc("Actor types of the bot's home Supply Route — used to anchor the staging vector.",
			"Mirrors MountedTransportBotModuleInfo.SupplyRouteTypes.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("Experimental mission-employment (default false = frozen): treat attack helis as hit-and-run",
			"assets with an exit strategy. An attack heli that goes idle with no believed worthwhile target",
			"— or is spent (out of ammo) with no rearm host to refill at — is EVACUATED to reserves via the",
			"map edge (RotateToEdge), reclaiming its salvage value (full Cost with ammo, less spent-ammo value)",
			"and stopping its upkeep drain, instead of parking at the SR/staging corner forever (the corner-idle",
			"bug). A believed target instead keeps the heli HELD for the squad mission loop. Fog-legal: the",
			"'worthwhile target' read is the belief store, never ground truth. OFF by default so normal/rush/",
			"turtle/stable stay byte-identical; only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool EvacuateWhenIdle = false;

		[Desc("Consecutive idle ticks an attack heli must loiter near home with no believed worthwhile target",
			"before it is evacuated to reserves. Spent-with-no-rearm helis evac immediately (this gate does not",
			"apply to them). Only used when EvacuateWhenIdle is set.")]
		public readonly int EvacuateIdleTicks = 500;

		[Desc("Radius (map cells) around the own Supply Route within which an idle, target-less heli counts as",
			"'loitering at home' and becomes evac-eligible. A heli forward of this (e.g. mid-withdraw near the",
			"front) is left to the squad FSM. Only used when EvacuateWhenIdle is set.")]
		public readonly int EvacuateHomeRadiusCells = 12;

		[Desc("Max distance (map cells) from a heli to a believed enemy contact for that contact to count as a",
			"worthwhile mission target: a target in range HOLDS the heli for a mission, none in range makes it",
			"evac-eligible. Only used when EvacuateWhenIdle is set.")]
		public readonly int MissionTargetRangeCells = 60;

		[Desc("Phase 4 strategic-target pinning (experimental, default false = frozen). Pin an attack-heli",
			"squad's STRATEGIC objective in the squad separate from its tactical TargetActor: the FSM keeps its",
			"5-tick standoff/danger-nav micro AND the bounded too-hot soft-swap, but a lapsed tactical target,",
			"a too-hot cell, or a withdraw no longer churn the strategic destination — the squad resumes toward",
			"the pinned objective instead of re-picking the nearest enemy (root cause C, design §1.3/§3.3). The",
			"pin releases ONLY on an abort trigger: objective invalid / objective-too-hot-with-no-divert / stalled",
			"(unreachable) / the bounded commit window below. OFF by default so legacy/normal/rush/turtle/stable are",
			"byte-identical; only HelicopterSquadBotModule@experimental turns it on. Mirrors the offense module's",
			"MissionCommitmentEnabled gating.")]
		public readonly bool StrategicTargetPinning = false;

		[Desc("Bounded commit-window backstop (ticks) for a pinned strategic objective (design §3.3 TTL valve).",
			"A pin held longer than this releases so an objective that never resolves cannot trap the squad",
			"forever. 0 = OFF (hold purely on the abort triggers), matching MissionCommitmentMath's window valve.",
			"Only used when StrategicTargetPinning is set.")]
		public readonly int PinCommitWindowTicks = 0;

		public override object Create(ActorInitializer init) { return new HelicopterSquadBotModule(init.Self, this); }
	}

	public class HelicopterSquadBotModule : ConditionalTrait<HelicopterSquadBotModuleInfo>, IBotTick, IBotEnabled
	{
		readonly World world;
		readonly Player player;

		readonly List<Squad> activeSquads = new List<Squad>();
		readonly List<Actor> idleHelicopters = new List<Actor>();
		readonly HashSet<Actor> managedHelicopters = new HashSet<Actor>();
		readonly Dictionary<Actor, CPos> stagedTo = new Dictionary<Actor, CPos>();

		// EvacuateWhenIdle bookkeeping (experimental). Consecutive idle ticks per managed heli, a reused
		// scratch list of believed-contact cells so the worthwhile-target scan allocates nothing per tick,
		// and the set of helis currently flying their evac (RotateToEdge) — excluded from re-adoption and
		// recruitment so the evac is never cancelled by a squad order. `enemyEverObserved` latches once the
		// belief store has ever held a contact, gating the target-less evac branch. All only ever touched
		// on the EvacuateWhenIdle path ⇒ inert (byte-identical) when the flag is off.
		readonly Dictionary<Actor, int> idleTicks = new Dictionary<Actor, int>();
		readonly List<CPos> targetScratch = new List<CPos>();
		readonly HashSet<Actor> evacuating = new HashSet<Actor>();
		bool enemyEverObserved;

		// Transport-role helis dispatched on a delivery, tracked until their cargo is confirmed unloaded.
		// The dispatch queues an immediate Move home for the common (successful-unload) case; this set is the
		// safety net for the rare case where the drop cell is unlandable, so UnloadCargo completes WITHOUT
		// unloading (Cargo.CanUnload false → UnloadCargo.cs:161 returns done) and the queued Move would fly the
		// heli home still LOADED. EnsureTransportsUnload re-issues Unload wherever it ends up, so a full
		// transport never idles loaded. (The mounted path's dispatch-time CanUnload gate does NOT transfer
		// here: at dispatch the passengers are only just ordered to board, so cargo is empty and the heli is
		// not at the drop yet — CanUnload would always be false and would delete the retreat entirely.)
		readonly HashSet<Actor> transportsAwaitingUnload = new HashSet<Actor>();

		IBot bot;
		SquadManagerBotModule squadManagerRef;
		ThreatMapManager threatMap;
		PoiMap poiMap;
		BeliefStore beliefStore;
		BotBlackboard blackboard;
		bool initialized;

		int scanCountdown;
		int attackCooldown;
		int scoutCooldown;
		int transportCooldown;
		int squadUpdateCountdown;

		public HelicopterSquadBotModule(Actor self, HelicopterSquadBotModuleInfo info)
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

			// Find SquadManager on player actor for Squad construction (required by Squad class)
			squadManagerRef = player.PlayerActor.TraitsImplementing<SquadManagerBotModule>()
				.FirstOrDefault(s => !s.IsTraitDisabled);

			threatMap = world.WorldActor.TraitOrDefault<ThreatMapManager>();
			poiMap = world.WorldActor.TraitOrDefault<PoiMap>();
			beliefStore = world.WorldActor.TraitOrDefault<BeliefStore>();
			blackboard = player.PlayerActor.TraitsImplementing<BotBlackboard>()
				.FirstOrDefault(b => !b.IsTraitDisabled);

			initialized = true;
		}

		void IBotTick.BotTick(IBot bot)
		{
			Initialize();

			// Update active squads frequently
			if (--squadUpdateCountdown <= 0)
			{
				squadUpdateCountdown = Info.SquadUpdateInterval;
				UpdateSquads();
			}

			// Scan for new helicopters less frequently
			if (--scanCountdown <= 0)
			{
				scanCountdown = Info.ScanInterval;
				FindNewHelicopters();
				CleanUpHelicopters();
				StageIdleHelicopters();
				EnsureTransportsUnload(bot);
			}

			// Attack missions
			if (--attackCooldown <= 0)
			{
				attackCooldown = Info.AttackCooldown;
				TryLaunchAttackMission();
			}

			// Scout missions
			if (--scoutCooldown <= 0)
			{
				scoutCooldown = Info.ScoutInterval;
				TryLaunchScoutMission();
			}

			// Transport missions
			if (--transportCooldown <= 0)
			{
				transportCooldown = Info.TransportInterval;
				TryLaunchTransportMission();
			}

			// Mission-employment: evacuate idle/spent attack helis to reserves (experimental-only,
			// default off ⇒ byte-identical). Runs every tick so the idle-duration gate counts game
			// ticks, not scan intervals.
			EvaluateIdleHelicopters();
		}

		void FindNewHelicopters()
		{
			// Exclude helis currently flying their evac: re-adopting one (it left managedHelicopters when
			// evacuated) would put it back in the idle pool and a squad order would cancel the RotateToEdge,
			// so the heli would fight without ammo and never stop draining upkeep. The set is only ever
			// populated on the EvacuateWhenIdle path, so this clause is a no-op (byte-identical) when off.
			var helicopters = world.ActorsHavingTrait<AIHelicopterRole>()
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& !managedHelicopters.Contains(a) && !evacuating.Contains(a));

			foreach (var h in helicopters)
			{
				managedHelicopters.Add(h);

				// Claim in blackboard to prevent other modules from taking it
				if (blackboard != null)
					blackboard.ClaimUnit(h, "helicopter");

				// Add to idle pool if not rearming
				if (!idleHelicopters.Contains(h))
					idleHelicopters.Add(h);
			}
		}

		void CleanUpHelicopters()
		{
			// Remove dead/destroyed helicopters
			managedHelicopters.RemoveWhere(a => a == null || a.IsDead || !a.IsInWorld);
			idleHelicopters.RemoveAll(a => a == null || a.IsDead || !a.IsInWorld);

			// Drop staged entries the moment a heli dies OR leaves the idle pool (recruited into a
			// squad). A returning heli is re-eligible for staging only when near the SR again (§distance
			// gate). Only ever populated on the ForwardStaging path, so this is a no-op when the flag is off.
			foreach (var a in stagedTo.Keys.ToList())
				if (a == null || a.IsDead || !a.IsInWorld || !idleHelicopters.Contains(a))
					stagedTo.Remove(a);

			// Drop idle-tick counters for helis that died or left management (evacuated / disowned).
			// Only ever populated on the EvacuateWhenIdle path, so this is a no-op when the flag is off.
			foreach (var a in idleTicks.Keys.ToList())
				if (a == null || a.IsDead || !a.IsInWorld || !managedHelicopters.Contains(a))
					idleTicks.Remove(a);

			// Drop evacuating helis once they have left the world (RotateToEdge disposes them at the map
			// edge). Predicate-based ⇒ iteration-order-independent. No-op when the flag is off.
			evacuating.RemoveWhere(a => a == null || a.IsDead || !a.IsInWorld);

			// Drop dead/gone transports from the awaiting-unload tracker (EnsureTransportsUnload also prunes,
			// but keep the hygiene at the same choke point as the other sets). No-op when none are tracked.
			transportsAwaitingUnload.RemoveWhere(a => a == null || a.IsDead || !a.IsInWorld);

			// Clean up squads
			PruneSquads();

			// Return idle helicopters from disbanded squads back to pool
			foreach (var h in managedHelicopters)
			{
				if (h.IsDead || !h.IsInWorld)
					continue;

				var inSquad = false;
				foreach (var squad in activeSquads)
				{
					if (squad.Units.Contains(h))
					{
						inSquad = true;
						break;
					}
				}

				if (!inSquad && !idleHelicopters.Contains(h))
					idleHelicopters.Add(h);
			}
		}

		Actor FindOwnSupplyRoute()
		{
			return world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.SupplyRouteTypes.Contains(a.Info.Name));
		}

		// Pre-contact forward staging (experimental, ForwardStaging). Push idle attack helis that
		// are still loitering near the SR forward to a fraction of the SR->top-POI vector, so they
		// stage toward the fight instead of hovering at the SR corner. Deterministic: PoiMap query
		// + integer vector math, ZERO random draws. Fully skipped (byte-identical) when the flag is off.
		void StageIdleHelicopters()
		{
			if (!Info.ForwardStaging)
				return;

			var ownSR = FindOwnSupplyRoute();
			if (ownSR == null)
				return;
			var srCell = ownSR.Location;

			var stageCell = ForwardStagingCell(srCell);
			if (!stageCell.HasValue)
				return;

			var maxDistSq = (long)Info.ForwardStagingMaxDistanceCells * Info.ForwardStagingMaxDistanceCells;

			foreach (var h in idleHelicopters)
			{
				if (h.IsDead || !h.IsInWorld || !h.IsIdle)
					continue;
				if (stagedTo.ContainsKey(h))
					continue;

				// Attack helis only — scouts/transports have their own mission paths.
				var role = h.TraitOrDefault<AIHelicopterRole>();
				if (role == null)
					continue;
				var r = role.Info.Role;
				if (r != HelicopterAIRole.AttackHeavy && r != HelicopterAIRole.AttackLight)
					continue;

				// Same readiness definition the squad launch uses (health gate always applies;
				// ammo gate bypassed under SkipRearmReadyCheck exactly as for TryLaunchAttackMission).
				if (!IsReadyForMission(h))
					continue;

				// Only stage helis still loitering near the SR — leave forward/returned helis alone.
				if ((h.Location - srCell).LengthSquared > maxDistSq)
					continue;

				bot.QueueOrder(new Order("Move", h, Target.FromCell(world, stageCell.Value), false));
				stagedTo[h] = stageCell.Value;

				AIUtils.BotDebug("AI ({0}): heli forward-staging {1} {2} -> {3}",
					player.ClientIndex, h.Info.Name, h.Location, stageCell.Value);
			}
		}

		// Staging-cell math — mirrors MountedTransportBotModule.PreContactStagingCell exactly.
		// The pure WPos interpolation is extracted to HeliStagingMath (NUnit-pinned, world-free).
		CPos? ForwardStagingCell(CPos srCell)
		{
			if (poiMap == null)
				return null;

			var targets = poiMap.GetOffensiveTargets(player);
			if (targets.Count == 0)
				return null;

			var srPos = world.Map.CenterOfCell(srCell);
			var tgtPos = world.Map.CenterOfCell(targets[0].Location);
			var stagePos = HeliStagingMath.StagePos(srPos, tgtPos, Info.ForwardStagingPct);
			var cell = world.Map.CellContaining(stagePos);
			return world.Map.Contains(cell) ? cell : (CPos?)null;
		}

		// Drop dead/not-in-world/foreign members from every active squad and remove squads left
		// with no units. Mirrors the engine-standard SquadManagerBotModule.CleanSquads. MUST run
		// before UpdateSquads: a squad state tick that reaches a Disposed member throws
		// ("Attempted to get trait from destroyed object") the instant it touches a trait
		// (GetRole/health/ammo). Pruning only on the slow ScanInterval is not enough — members die
		// between scans and the 5-tick squad update would iterate the stale list first.
		void PruneSquads()
		{
			for (var i = activeSquads.Count - 1; i >= 0; i--)
			{
				var squad = activeSquads[i];
				squad.Units.RemoveAll(a => a == null || a.IsDead || !a.IsInWorld || a.Owner != player);

				if (!squad.IsValid)
					activeSquads.RemoveAt(i);
			}
		}

		void UpdateSquads()
		{
			PruneSquads();

			foreach (var squad in activeSquads)
				squad.Update();
		}

		void TryLaunchAttackMission()
		{
			if (activeSquads.Count >= Info.MaxActiveSquads)
				return;

			if (squadManagerRef == null)
				return;

			// Get idle attack helicopters
			var attackHelicopters = idleHelicopters
				.Where(h =>
				{
					var role = h.TraitOrDefault<AIHelicopterRole>();
					if (role == null)
						return false;

					var r = role.Info.Role;
					return r == HelicopterAIRole.AttackHeavy || r == HelicopterAIRole.AttackLight;
				})
				.Where(h => IsReadyForMission(h))
				.ToList();

			var neededSize = Info.AttackSquadSize + world.LocalRandom.Next(Info.AttackSquadSizeBonus + 1);
			if (attackHelicopters.Count < neededSize)
				return;

			// Create a helicopter attack squad
			var squad = new Squad(bot, squadManagerRef, SquadType.Helicopter);

			var assigned = 0;
			foreach (var h in attackHelicopters)
			{
				if (assigned >= neededSize)
					break;

				squad.Units.Add(h);
				idleHelicopters.Remove(h);
				assigned++;
			}

			activeSquads.Add(squad);
		}

		void TryLaunchScoutMission()
		{
			if (activeSquads.Count >= Info.MaxActiveSquads)
				return;

			if (squadManagerRef == null)
				return;

			// Get an idle scout helicopter
			var scout = idleHelicopters
				.Where(h =>
				{
					var role = h.TraitOrDefault<AIHelicopterRole>();
					return role != null && role.Info.Role == HelicopterAIRole.Scout;
				})
				.Where(h => IsReadyForMission(h))
				.FirstOrDefault();

			if (scout == null)
				return;

			// Scouts go alone — find unexplored areas
			CPos? scoutTarget = null;

			if (threatMap != null)
			{
				var bestAge = 0;
				for (var gx = 0; gx < threatMap.GridWidth; gx++)
				{
					for (var gy = 0; gy < threatMap.GridHeight; gy++)
					{
						var mapCell = threatMap.GridToMapCell(gx, gy);
						if (!world.Map.Contains(mapCell))
							continue;

						var age = threatMap.GetExplorationAge(mapCell);
						if (age > bestAge)
						{
							bestAge = age;
							scoutTarget = mapCell;
						}
					}
				}
			}

			if (!scoutTarget.HasValue)
			{
				// Random location
				var map = world.Map;
				scoutTarget = new CPos(
					world.LocalRandom.Next(map.Bounds.Left, map.Bounds.Right),
					world.LocalRandom.Next(map.Bounds.Top, map.Bounds.Bottom));
			}

			// Send scout directly — don't need a full squad for one unit
			bot.QueueOrder(new Order("Move", scout, Target.FromCell(world, scoutTarget.Value), false));
			idleHelicopters.Remove(scout);

			// Still track as managed — it'll return to idle pool when it comes home
			// Don't create a squad for a single scout; just let it explore
		}

		void TryLaunchTransportMission()
		{
			if (activeSquads.Count >= Info.MaxActiveSquads)
				return;

			if (squadManagerRef == null)
				return;

			// Get idle transport helicopter
			var transport = idleHelicopters
				.Where(h =>
				{
					var role = h.TraitOrDefault<AIHelicopterRole>();
					return role != null && role.Info.Role == HelicopterAIRole.Transport;
				})
				.Where(h => IsReadyForMission(h))
				.FirstOrDefault();

			if (transport == null)
				return;

			// Check if transport has cargo capability
			var cargo = transport.TraitOrDefault<Cargo>();
			if (cargo == null)
				return;

			// Find idle infantry near base to load
			var infantry = world.ActorsHavingTrait<Mobile>()
				.Where(a => a.Owner == player
					&& !a.IsDead && a.IsInWorld
					&& a.IsIdle
					&& a.Info.HasTraitInfo<WithInfantryBodyInfo>()
					&& cargo.Info.Types.Overlaps(a.GetAllTargetTypes()))
				.Take(cargo.Info.MaxWeight)
				.ToList();

			if (infantry.Count < Info.TransportMinInfantry)
				return;

			// Find a front-line drop zone
			CPos? dropZone = null;

			if (threatMap != null)
			{
				// Find an enemy-adjacent cell that isn't too dangerous
				var weakCell = threatMap.FindWeakestEnemyCell(player);
				if (weakCell != CPos.Zero)
				{
					var threat = threatMap.GetThreat(weakCell, player);
					if (threat < 50)
						dropZone = weakCell;
				}
			}

			if (!dropZone.HasValue)
				return;

			// Load infantry into transport
			foreach (var inf in infantry)
				bot.QueueOrder(new Order("EnterTransport", inf, Target.FromActor(transport), false));

			// Send transport to drop zone after loading, then unload
			bot.QueueOrder(new Order("Move", transport, Target.FromCell(world, dropZone.Value), queued: true));
			bot.QueueOrder(new Order("Unload", transport, queued: true));

			// WW3MOD retreat-on-unload: withdraw the transport heli to our Supply Route the instant unloading
			// completes, instead of leaving it hovering IDLE at the drop zone deep in contested territory — an
			// easy kill. Queued after the Unload so the return is engine-driven (no scan-loop gap). A Transport-
			// role heli is not covered by EvaluateIdleHelicopters (attack-only), and CleanUpHelicopters would
			// only re-pool it FOR THE NEXT transport mission (>=4 idle infantry + a weak drop cell, up to
			// TransportInterval away) — so without this order nothing brings it home. Bug-class fix, ungated:
			// applies to every profile that runs this module (@stable + @experimental). We also TRACK it so
			// EnsureTransportsUnload can re-dump the cargo in the rare unlandable-drop case (see the field
			// comment) — the queued Move alone would otherwise fly the heli home still loaded.
			var ownSR = FindOwnSupplyRoute();
			if (ownSR != null)
				bot.QueueOrder(new Order("Move", transport, Target.FromCell(world, ownSR.Location), queued: true));
			transportsAwaitingUnload.Add(transport);

			idleHelicopters.Remove(transport);
		}

		// Safety net for the pre-queued transport retreat: confirm each dispatched transport heli actually
		// unloaded. Common path — Unload empties the cargo and the queued Move flies it home — is a no-op here
		// (empty ⇒ dropped from tracking). Rare path — the drop cell was unlandable so UnloadCargo finished
		// without unloading and the heli flew home LOADED — is caught here: re-issue Unload wherever it now
		// sits (typically the open SR area, so it dumps safely) rather than leaving a full transport idle and
		// loaded. Deterministic: ActorID-ordered, zero RNG. Inert (byte-identical) until a transport is
		// actually dispatched, so attack/scout-only profiles are unaffected.
		void EnsureTransportsUnload(IBot bot)
		{
			if (transportsAwaitingUnload.Count == 0)
				return;

			foreach (var h in transportsAwaitingUnload.OrderBy(a => a.ActorID).ToList())
			{
				if (h == null || h.IsDead || !h.IsInWorld)
				{
					transportsAwaitingUnload.Remove(h);
					continue;
				}

				var cargo = h.TraitOrDefault<Cargo>();
				if (cargo == null || cargo.IsEmpty())
				{
					// Delivered (and already retreating/home via the queued Move) — done tracking.
					transportsAwaitingUnload.Remove(h);
					continue;
				}

				// Still loaded: only act once it is idle (the delivery/return chain has run to its end and
				// left it loaded), so we never interrupt an in-progress unload or flight. Re-issue Unload to
				// dump the cargo where it sits; keep tracking until the cargo actually empties.
				if (h.IsIdle)
					bot.QueueOrder(new Order("Unload", h, false));
			}
		}

		bool IsReadyForMission(Actor h)
		{
			if (h.IsDead || !h.IsInWorld)
				return false;

			// A heli flying its evac must never be recruited/staged — that would cancel the RotateToEdge.
			// Empty set when EvacuateWhenIdle is off ⇒ byte-identical for every other profile.
			if (evacuating.Contains(h))
				return false;

			// Check HP
			var health = h.TraitOrDefault<IHealth>();
			if (health != null)
			{
				var role = h.TraitOrDefault<AIHelicopterRole>();
				var reEngagePercent = role != null ? role.Info.ReEngageHealthPercent : 80;
				if (health.HP * 100 / health.MaxHP < reEngagePercent)
					return false;
			}

			// Check ammo — unless the rearm-ready gate is bypassed. WW3MOD attack helis rearm only
			// at an hpad (none built), so requiring full ammo permanently benches any heli that
			// dipped below full and no squad ever forms. SkipRearmReadyCheck lets them launch anyway.
			if (!Info.SkipRearmReadyCheck)
			{
				var ammoPools = h.TraitsImplementing<AmmoPool>().ToArray();
				var rearmable = h.TraitOrDefault<Rearmable>();
				if (ammoPools.Length > 0 && rearmable != null)
				{
					foreach (var ap in ammoPools)
					{
						if (!ap.HasFullAmmo)
							return false;
					}
				}
			}

			// Check if currently rearming
			if (!h.IsIdle)
			{
				var activity = h.CurrentActivity;
				if (activity != null && activity.GetType().Name == "Resupply")
					return false;
			}

			return true;
		}

		// Mission-employment evac (experimental, EvacuateWhenIdle). An attack heli that is genuinely idle
		// (empty activity queue — so never one mid-mission, mid-withdraw, or already flying its evac) is
		// evacuated to reserves when it has no further use: either it is spent with no rearm host, or it has
		// loitered near home past the patience window with no believed worthwhile target. Evac reclaims the
		// heli's salvage value (RotateToEdge → GetSellValue: full Cost with ammo, less spent-ammo value) and
		// stops its upkeep drain — the "helicopters are perfect for short hit-and-run missions" model, and the
		// fix for the SR/staging corner-idle park. Deterministic: belief-store + integer geometry, ActorID-
		// ordered iteration, ZERO random draws. Fully skipped (byte-identical) when the flag is off.
		void EvaluateIdleHelicopters()
		{
			if (!Info.EvacuateWhenIdle || managedHelicopters.Count == 0)
				return;

			// Latch first contact: once the belief store has EVER held an enemy contact, the target-less
			// evac branch is allowed. Before first contact the bot cannot know where the enemy is, so
			// anticipatory helis are HELD (and staged forward) rather than evac'd and re-bought — this
			// matters because EvacuateHomeRadiusCells (12) > ForwardStagingMaxDistanceCells (8), so a
			// forward-staged, target-less heli is otherwise inside the evac-eligible home radius.
			if (!enemyEverObserved && beliefStore != null && beliefStore.Contacts(player).Count > 0)
				enemyEverObserved = true;

			var ownSR = FindOwnSupplyRoute();
			var homeRadiusSq = (long)Info.EvacuateHomeRadiusCells * Info.EvacuateHomeRadiusCells;
			var missionRangeSq = (long)Info.MissionTargetRangeCells * Info.MissionTargetRangeCells;

			// managedHelicopters is a HashSet (iteration order not guaranteed); sort by synced ActorID
			// for lockstep-deterministic order. OrderBy buffers, and ToList snapshots, so Evacuate can
			// mutate the set safely inside the loop.
			foreach (var h in managedHelicopters.OrderBy(a => a.ActorID).ToList())
			{
				if (h == null || h.IsDead || !h.IsInWorld)
					continue;

				var role = h.TraitOrDefault<AIHelicopterRole>();
				if (role == null)
					continue;
				var r = role.Info.Role;
				if (r != HelicopterAIRole.AttackHeavy && r != HelicopterAIRole.AttackLight)
					continue;

				// Only act on a heli with an empty activity queue. A heli executing a mission
				// (attack-move / attack / return) or already flying its evac is never idle, so the
				// active FSM arc is never disturbed.
				if (!h.IsIdle)
				{
					idleTicks[h] = 0;
					continue;
				}

				var ticks = (idleTicks.TryGetValue(h, out var t) ? t : 0) + 1;
				idleTicks[h] = ticks;

				var hasUsableAmmo = HasUsableAmmo(h);
				var canRearm = CanRearm(h);
				var nearHome = ownSR == null || (h.Location - ownSR.Location).LengthSquared <= homeRadiusSq;
				var hasTarget = HasWorthwhileBelievedTarget(h, missionRangeSq);

				if (HeliEmploymentMath.Decide(hasUsableAmmo, canRearm, hasTarget, enemyEverObserved, nearHome, ticks, Info.EvacuateIdleTicks)
					== HeliDisposition.Evacuate)
					Evacuate(h);
			}
		}

		// True if the heli still has a usable round in any pool. A heli carrying no AmmoPool at all is
		// not ammo-limited, so it never counts as "spent".
		static bool HasUsableAmmo(Actor h)
		{
			var any = false;
			foreach (var ap in h.TraitsImplementing<AmmoPool>())
			{
				any = true;
				if (ap.HasAmmo)
					return true;
			}

			return !any;
		}

		// True if any friendly rearm host this heli could dock at exists in the world. WW3MOD builds no
		// hpad, so this is normally false and a spent heli evacs; written generally so it self-heals if a
		// rearm structure is ever added. Boolean Any ⇒ iteration-order-independent (deterministic).
		bool CanRearm(Actor h)
		{
			var rearmable = h.TraitOrDefault<Rearmable>();
			if (rearmable == null || rearmable.Info.RearmActors.Count == 0)
				return false;

			var hosts = rearmable.Info.RearmActors;
			return world.Actors.Any(a => a.Owner == player && !a.IsDead && a.IsInWorld && hosts.Contains(a.Info.Name));
		}

		// Fog-legal worthwhile-target test: is any BELIEVED enemy contact within mission range of the heli?
		// Reads the belief store (Stage A), never ground truth. No belief field ⇒ we cannot assert a target
		// ⇒ evac-eligible — we do not cheat by peeking through fog.
		bool HasWorthwhileBelievedTarget(Actor h, long missionRangeSq)
		{
			if (beliefStore == null)
				return false;

			targetScratch.Clear();
			foreach (var c in beliefStore.Contacts(player))
				targetScratch.Add(c.Cell);

			return HeliEmploymentMath.AnyTargetWithin(h.Location, targetScratch, missionRangeSq);
		}

		// Evacuate a heli to reserves and drop it from ALL bot management so nothing re-tasks it and cancels
		// the evac (the recruit-cancels-evac hazard PoiOffensive guards against for out-of-ammo ground units).
		void Evacuate(Actor h)
		{
			h.QueueActivity(false, new RotateToEdge(h, true, h.GetSellValue()));

			// Mark evacuating BEFORE dropping from management: FindNewHelicopters / IsReadyForMission both
			// exclude this set, so the heli can never be re-adopted or recruited while flying its evac (the
			// order would cancel the RotateToEdge). Cleared once it leaves the world (CleanUpHelicopters).
			evacuating.Add(h);

			idleHelicopters.Remove(h);
			foreach (var squad in activeSquads)
				squad.Units.Remove(h);
			managedHelicopters.Remove(h);
			stagedTo.Remove(h);
			idleTicks.Remove(h);
			if (blackboard != null)
				blackboard.ReleaseUnit(h);

			AIUtils.BotDebug("AI ({0}): heli evac-to-reserves {1} at {2}",
				player.ClientIndex, h.Info.Name, h.Location);
		}

		protected override void TraitDisabled(Actor self)
		{
			// Release all helicopters
			if (blackboard != null)
				foreach (var h in managedHelicopters)
					if (h != null && !h.IsDead)
						blackboard.ReleaseUnit(h);

			managedHelicopters.Clear();
			idleHelicopters.Clear();
			activeSquads.Clear();
			stagedTo.Clear();
			idleTicks.Clear();
			evacuating.Clear();
			transportsAwaitingUnload.Clear();
			enemyEverObserved = false;
		}
	}

	// Pure, world-free staging-vector math for HelicopterSquadBotModule forward staging.
	// Split out for NUnit like HeliDangerNav / the influence-stack math classes — deterministic,
	// zero RNG. StagePos mirrors MountedTransportBotModule.PreContactStagingCell's WPos interpolation.
	public static class HeliStagingMath
	{
		// A fraction (percent) of the way from the SR position toward the target position.
		// pct = 0 -> sr, pct = 100 -> tgt, pct = 50 -> midpoint. Integer WVec math, no rounding drift
		// beyond what the shipped MountedTransport pattern already accepts.
		public static WPos StagePos(WPos sr, WPos tgt, int pct)
		{
			return sr + (tgt - sr) * pct / 100;
		}
	}

	// What to do with an idle attack heli that is not currently executing a mission.
	public enum HeliDisposition { HoldForMission, Evacuate }

	// Pure, world-free mission-employment decision math for HelicopterSquadBotModule. Split out for NUnit
	// like HeliStagingMath / HeliDangerNav — deterministic, integer-only, zero RNG.
	public static class HeliEmploymentMath
	{
		// Decide the disposition of an idle attack heli.
		//   hasUsableAmmo        — any pool still has a round.
		//   canRearm             — a friendly rearm host exists it could refill at.
		//   hasWorthwhileTarget  — a believed enemy contact is within mission range.
		//   contactEverObserved  — the bot has believed at least one enemy contact at some point (first
		//                          contact has happened). Gates the target-less branch so anticipatory
		//                          helis are not evac'd/re-bought during the opening before any contact.
		//   nearHome             — the heli is loitering within the home radius (at the SR/staging area).
		//   idleTicks            — consecutive ticks the heli has been idle.
		//   evacuateIdleTicks    — patience window before a target-less home heli is evacuated.
		public static HeliDisposition Decide(
			bool hasUsableAmmo, bool canRearm, bool hasWorthwhileTarget,
			bool contactEverObserved, bool nearHome, int idleTicks, int evacuateIdleTicks)
		{
			// Spent and unable to refill: no combat value remains — bank the salvage and stop the upkeep
			// drain rather than parking a disarmed heli forever. Fires regardless of target/home/window/contact.
			if (!hasUsableAmmo && !canRearm)
				return HeliDisposition.Evacuate;

			// Armed (or able to rearm) but nothing believed worth striking, and loitering at home past the
			// patience window: reclaim full value + stop upkeep instead of corner-parking. Only once first
			// contact has been made — a believed target instead keeps the heli HELD for the squad mission loop.
			if (contactEverObserved && !hasWorthwhileTarget && nearHome && idleTicks >= evacuateIdleTicks)
				return HeliDisposition.Evacuate;

			return HeliDisposition.HoldForMission;
		}

		// True if any candidate cell is within maxRangeCellsSq (squared map-cell distance) of the heli.
		// Caller supplies the believed-contact cells; pure integer geometry, deterministic.
		public static bool AnyTargetWithin(CPos heliCell, IReadOnlyList<CPos> candidateCells, long maxRangeCellsSq)
		{
			for (var i = 0; i < candidateCells.Count; i++)
			{
				var d = candidateCells[i] - heliCell;
				if ((long)d.LengthSquared <= maxRangeCellsSq)
					return true;
			}

			return false;
		}
	}
}
