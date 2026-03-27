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

		public override object Create(ActorInitializer init) { return new HelicopterSquadBotModule(init.Self, this); }
	}

	public class HelicopterSquadBotModule : ConditionalTrait<HelicopterSquadBotModuleInfo>, IBotTick, IBotEnabled
	{
		readonly World world;
		readonly Player player;

		readonly List<Squad> activeSquads = new List<Squad>();
		readonly List<Actor> idleHelicopters = new List<Actor>();
		readonly HashSet<Actor> managedHelicopters = new HashSet<Actor>();

		IBot bot;
		SquadManagerBotModule squadManagerRef;
		ThreatMapManager threatMap;
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

			// Clean up squads
			for (var i = activeSquads.Count - 1; i >= 0; i--)
			{
				var squad = activeSquads[i];
				squad.Units.RemoveAll(a => a == null || a.IsDead || !a.IsInWorld
					|| a.Owner != player);

				if (!squad.IsValid)
				{
					activeSquads.RemoveAt(i);
					continue;
				}
			}

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

		void UpdateSquads()
		{
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

			// Check ammo
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
		}
	}
}
