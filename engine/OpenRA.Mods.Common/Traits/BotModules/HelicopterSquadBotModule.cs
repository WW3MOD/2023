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

		IBot bot;
		SquadManagerBotModule squadManagerRef;
		ThreatMapManager threatMap;
		PoiMap poiMap;
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
		}

		void FindNewHelicopters()
		{
			var helicopters = world.ActorsHavingTrait<AIHelicopterRole>()
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld && !managedHelicopters.Contains(a));

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

			idleHelicopters.Remove(transport);
		}

		bool IsReadyForMission(Actor h)
		{
			if (h.IsDead || !h.IsInWorld)
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
}
