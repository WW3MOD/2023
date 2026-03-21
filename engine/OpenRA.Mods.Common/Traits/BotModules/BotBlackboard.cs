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
	public enum BotTaskType
	{
		AttackArea,
		DefendArea,
		Scout,
		Capture,
		SupplyRun,
		Retreat,
		Garrison
	}

	public enum BotTaskStatus
	{
		Open,
		Claimed,
		InProgress,
		Completed,
		Failed
	}

	public class BotTask
	{
		public readonly string Id;
		public readonly BotTaskType Type;
		public readonly CPos Location;
		public readonly int Priority;
		public readonly int CreatedTick;

		public BotTaskStatus Status;
		public string ClaimedBy;
		public int LastUpdatedTick;

		public BotTask(string id, BotTaskType type, CPos location, int priority, int createdTick)
		{
			Id = id;
			Type = type;
			Location = location;
			Priority = priority;
			CreatedTick = createdTick;
			Status = BotTaskStatus.Open;
			LastUpdatedTick = createdTick;
		}
	}

	[Desc("Shared blackboard for AI inter-module coordination.",
		"Prevents modules from fighting over unit control. Provides task posting/claiming and intel sharing.")]
	public class BotBlackboardInfo : ConditionalTraitInfo
	{
		[Desc("Stale tasks older than this many ticks are automatically cleaned up.")]
		public readonly int TaskStaleTicks = 1500;

		[Desc("Interval between stale task cleanup passes.")]
		public readonly int CleanupInterval = 300;

		public override object Create(ActorInitializer init) { return new BotBlackboard(init.Self, this); }
	}

	public class BotBlackboard : ConditionalTrait<BotBlackboardInfo>, IBotTick
	{
		readonly World world;
		readonly Player player;

		// Task board — strategic layer posts, squads/modules claim
		readonly Dictionary<string, BotTask> tasks = new Dictionary<string, BotTask>();

		// Unit claims — which module/squad controls which unit
		readonly Dictionary<uint, string> unitClaims = new Dictionary<uint, string>();

		// Intel board — scouting/observation data shared between modules
		readonly Dictionary<string, object> intel = new Dictionary<string, object>();

		int cleanupCountdown;
		int nextTaskId;

		public BotBlackboard(Actor self, BotBlackboardInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
			cleanupCountdown = info.CleanupInterval;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (--cleanupCountdown <= 0)
			{
				cleanupCountdown = Info.CleanupInterval;
				CleanupStaleTasks();
				CleanupDeadUnitClaims();
			}
		}

		void CleanupStaleTasks()
		{
			var staleIds = tasks
				.Where(kvp =>
					(kvp.Value.Status == BotTaskStatus.Completed || kvp.Value.Status == BotTaskStatus.Failed) ||
					(world.WorldTick - kvp.Value.LastUpdatedTick > Info.TaskStaleTicks && kvp.Value.Status != BotTaskStatus.InProgress))
				.Select(kvp => kvp.Key).ToList();

			foreach (var id in staleIds)
				tasks.Remove(id);
		}

		void CleanupDeadUnitClaims()
		{
			var deadIds = unitClaims.Keys.Where(id =>
			{
				var actor = world.GetActorById(id);
				return actor == null || actor.IsDead || !actor.IsInWorld;
			}).ToList();

			foreach (var id in deadIds)
				unitClaims.Remove(id);
		}

		// ===== Task Management =====

		/// <summary>Post a new task to the blackboard. Returns the task ID.</summary>
		public string PostTask(BotTaskType type, CPos location, int priority)
		{
			var id = $"task-{nextTaskId++}";
			tasks[id] = new BotTask(id, type, location, priority, world.WorldTick);
			return id;
		}

		/// <summary>Claim a task. Returns true if successfully claimed.</summary>
		public bool ClaimTask(string taskId, string claimant)
		{
			if (!tasks.TryGetValue(taskId, out var task))
				return false;

			if (task.Status != BotTaskStatus.Open)
				return false;

			task.Status = BotTaskStatus.Claimed;
			task.ClaimedBy = claimant;
			task.LastUpdatedTick = world.WorldTick;
			return true;
		}

		/// <summary>Update a task's status.</summary>
		public void UpdateTaskStatus(string taskId, BotTaskStatus status)
		{
			if (tasks.TryGetValue(taskId, out var task))
			{
				task.Status = status;
				task.LastUpdatedTick = world.WorldTick;
			}
		}

		/// <summary>Get all open tasks of a given type, ordered by priority descending.</summary>
		public IEnumerable<BotTask> GetOpenTasks(BotTaskType type)
		{
			return tasks.Values
				.Where(t => t.Type == type && t.Status == BotTaskStatus.Open)
				.OrderByDescending(t => t.Priority);
		}

		/// <summary>Get all tasks (any status) of a given type.</summary>
		public IEnumerable<BotTask> GetTasks(BotTaskType type)
		{
			return tasks.Values.Where(t => t.Type == type);
		}

		/// <summary>Check if there's already a task covering a location (within range cells).</summary>
		public bool HasTaskNear(BotTaskType type, CPos location, int range)
		{
			return tasks.Values.Any(t =>
				t.Type == type &&
				t.Status != BotTaskStatus.Completed &&
				t.Status != BotTaskStatus.Failed &&
				(t.Location - location).Length <= range);
		}

		// ===== Unit Claims =====

		/// <summary>Claim a unit for a specific module/squad. Returns false if already claimed by someone else.</summary>
		public bool ClaimUnit(Actor actor, string claimant)
		{
			if (actor == null || actor.IsDead)
				return false;

			if (unitClaims.TryGetValue(actor.ActorID, out var existingClaimant))
			{
				if (existingClaimant != claimant)
					return false;

				return true; // Already claimed by same claimant
			}

			unitClaims[actor.ActorID] = claimant;
			return true;
		}

		/// <summary>Release a unit claim.</summary>
		public void ReleaseUnit(Actor actor)
		{
			if (actor != null)
				unitClaims.Remove(actor.ActorID);
		}

		/// <summary>Check if a unit is claimed by anyone.</summary>
		public bool IsUnitClaimed(Actor actor)
		{
			return actor != null && unitClaims.ContainsKey(actor.ActorID);
		}

		/// <summary>Check if a unit is claimed by a specific claimant.</summary>
		public bool IsUnitClaimedBy(Actor actor, string claimant)
		{
			return actor != null && unitClaims.TryGetValue(actor.ActorID, out var existing) && existing == claimant;
		}

		/// <summary>Get the claimant of a unit, or null if unclaimed.</summary>
		public string GetUnitClaimant(Actor actor)
		{
			if (actor != null && unitClaims.TryGetValue(actor.ActorID, out var claimant))
				return claimant;

			return null;
		}

		// ===== Intel Board =====

		/// <summary>Post intel data (key-value pairs shared between modules).</summary>
		public void PostIntel(string key, object value)
		{
			intel[key] = value;
		}

		/// <summary>Get intel data by key. Returns null if not found.</summary>
		public object GetIntel(string key)
		{
			return intel.TryGetValue(key, out var value) ? value : null;
		}

		/// <summary>Get typed intel data. Returns default if not found or wrong type.</summary>
		public T GetIntel<T>(string key, T defaultValue = default)
		{
			if (intel.TryGetValue(key, out var value) && value is T typed)
				return typed;

			return defaultValue;
		}

		/// <summary>Check if intel exists for a key.</summary>
		public bool HasIntel(string key)
		{
			return intel.ContainsKey(key);
		}

		/// <summary>Remove intel by key.</summary>
		public void RemoveIntel(string key)
		{
			intel.Remove(key);
		}
	}
}
