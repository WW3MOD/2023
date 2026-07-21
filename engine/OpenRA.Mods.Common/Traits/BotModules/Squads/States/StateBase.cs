#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
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
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	abstract class StateBase
	{
		protected static void GoToRandomOwnBuilding(Squad squad)
		{
			var loc = RandomBuildingLocation(squad);
			foreach (var a in squad.Units)
				squad.Bot.QueueOrder(new Order("Move", a, Target.FromCell(squad.World, loc), false));
		}

		protected static CPos RandomBuildingLocation(Squad squad)
		{
			var location = squad.SquadManager.GetRandomBaseCenter();
			var buildings = squad.World.ActorsHavingTrait<Building>()
				.Where(a => a.Owner == squad.Bot.Player).ToList();
			if (buildings.Count > 0)
				location = buildings.Random(squad.Random).Location;

			return location;
		}

		protected static bool BusyAttack(Actor a)
		{
			if (a.IsIdle)
				return false;

			var activity = a.CurrentActivity;
			var type = activity.GetType();
			if (type == typeof(Attack) || type == typeof(FlyAttack))
				return true;

			var next = activity.NextActivity;
			if (next == null)
				return false;

			var nextType = next.GetType();
			if (nextType == typeof(Attack) || nextType == typeof(FlyAttack))
				return true;

			return false;
		}

		protected static bool CanAttackTarget(Actor a, Actor target)
		{
			if (!a.Info.HasTraitInfo<AttackBaseInfo>())
				return false;

			var targetTypes = target.GetEnabledTargetTypes();
			if (targetTypes.IsEmpty)
				return false;

			var arms = a.TraitsImplementing<Armament>();
			foreach (var arm in arms)
			{
				if (arm.IsTraitDisabled)
					continue;

				if (arm.Weapon.IsValidTarget(targetTypes))
					return true;
			}

			return false;
		}

		protected virtual bool ShouldFlee(Squad squad, Func<IReadOnlyCollection<Actor>, bool> flee)
		{
			if (!squad.IsValid)
				return false;

			var dangerRadius = squad.SquadManager.Info.DangerScanRadius;
			var units = squad.World.FindActorsInCircle(squad.CenterPosition, WDist.FromCells(dangerRadius)).ToList();

			// If there are any own buildings within the DangerRadius, don't flee
			// PERF: Avoid LINQ
			foreach (var u in units)
				if (u.Owner == squad.Bot.Player && u.Info.HasTraitInfo<BuildingInfo>())
					return false;

			var enemyAroundUnit = units
				.Where(unit => squad.SquadManager.IsPreferredEnemyUnit(unit) && unit.Info.HasTraitInfo<AttackBaseInfo>())
				.ToList();
			if (enemyAroundUnit.Count == 0)
				return false;

			return flee(enemyAroundUnit);
		}

		protected static bool IsRearming(Actor a)
		{
			return !a.IsIdle && (a.CurrentActivity.ActivitiesImplementing<Resupply>().Any() || a.CurrentActivity.ActivitiesImplementing<ReturnToBase>().Any());
		}

		protected static bool FullAmmo(IEnumerable<AmmoPool> ammoPools)
		{
			foreach (var ap in ammoPools)
				if (!ap.HasFullAmmo)
					return false;

			return true;
		}

		protected static bool HasAmmo(IEnumerable<AmmoPool> ammoPools)
		{
			foreach (var ap in ammoPools)
				if (!ap.HasAmmo)
					return false;

			return true;
		}

		protected static bool ReloadsAutomatically(IEnumerable<AmmoPool> ammoPools, Rearmable rearmable)
		{
			if (rearmable == null)
				return true;

			foreach (var ap in ammoPools)
				if (!rearmable.Info.AmmoPools.Contains(ap.Info.Name))
					return false;

			return true;
		}

		protected static void SetSquadEngagementStance(Squad squad, EngagementStance stance)
		{
			foreach (var a in squad.Units)
				squad.Bot.QueueOrder(new Order("SetEngagementStance", a, false) { ExtraData = (uint)stance });
		}

		// B1 rider (Phase 2): the squad FSM re-issues grouped orders ~every 75 ticks, which the
		// PoiGoalGuard commitment ledger does NOT gate — so a unit the tactical positioning executor
		// is adjusting would be yanked back into the formation order every re-fire. Exclude units
		// holding a `tacpos:` claim from the grouped order so their adjustment survives the re-fire.
		//
		// Behavior-inert where it must be: with no executor claims the ledger holds no `tacpos:`
		// objectives, so nothing is filtered and the unit list is identical (order preserved). On
		// profiles with no PoiGoalGuard the guard is null and the input passes through unchanged.
		protected static Actor[] ExcludeTacticallyCommitted(Squad squad, IEnumerable<Actor> units)
		{
			var guard = squad.Bot.Player.PlayerActor.TraitOrDefault<PoiGoalGuard>();
			if (guard == null)
				return units.ToArray();

			var tick = squad.World.WorldTick;
			return units.Where(u => !IsTacticallyCommitted(guard, u, tick)).ToArray();
		}

		static bool IsTacticallyCommitted(PoiGoalGuard guard, Actor unit, int tick)
		{
			return guard.Ledger.IsCommitted(unit, tick)
				&& guard.Ledger.TryGetObjective(unit, out var objective)
				&& objective != null
				&& objective.StartsWith("tacpos:", StringComparison.Ordinal);
		}
	}
}
