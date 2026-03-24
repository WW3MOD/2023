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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	abstract class GroundStateBase : StateBase
	{
		Actor leader;

		/// <summary>
		/// Elects a unit to lead the squad, other units in the squad will regroup to the leader if they start to spread out.
		/// The leader remains the same unless a new one is forced or the leader is no longer part of the squad.
		/// </summary>
		protected Actor Leader(Squad owner)
		{
			if (leader == null || !owner.Units.Contains(leader))
				leader = NewLeader(owner);
			return leader;
		}

		static Actor NewLeader(Squad owner)
		{
			IEnumerable<Actor> units = owner.Units;

			// Identify the Locomotor with the most restrictive passable terrain list. For squads with mixed
			// locomotors, we hope to choose the most restrictive option. This means we won't nominate a leader who has
			// more options. This avoids situations where we would nominate a hovercraft as the leader and tanks would
			// fail to follow it because they can't go over water. By forcing us to choose a unit with limited movement
			// options, we maximise the chance other units will be able to follow it. We could still be screwed if the
			// squad has a mix of units with disparate movement, e.g. land units and naval units. We must trust the
			// squad has been formed from a set of units that don't suffer this problem.
			var leastCommonDenominator = units
				.Select(a => a.TraitOrDefault<Mobile>()?.Locomotor)
				.Where(l => l != null)
				.MinByOrDefault(l => l.Info.TerrainSpeeds.Count)
				?.Info.TerrainSpeeds.Count;
			if (leastCommonDenominator != null)
				units = units.Where(a => a.TraitOrDefault<Mobile>()?.Locomotor.Info.TerrainSpeeds.Count == leastCommonDenominator).ToList();

			// Choosing a unit in the center reduces the need for an immediate regroup.
			var centerPosition = units.Select(a => a.CenterPosition).Average();
			return units.MinBy(a => (a.CenterPosition - centerPosition).LengthSquared);
		}

		protected virtual bool ShouldFlee(Squad owner)
		{
			return ShouldFlee(owner, enemies => !AttackOrFleeFuzzy.Default.CanAttack(owner.Units, enemies));
		}

		protected static Actor FindClosestEnemy(Squad owner)
		{
			return owner.SquadManager.FindEnemies(
				actors,
				Leader(owner));
		}

		protected static Actor ClosestToEnemy(Squad owner)
		{
			return SquadManagerBotModule.ClosestTo(owner.Units, owner.TargetActor);
		}
	}

	sealed class GroundUnitsIdleState : GroundStateBase, IState
	{
		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			if (!owner.IsTargetValid(Leader(owner)))
			{
				var closestEnemy = NewLeaderAndFindClosestEnemy(owner);
				owner.SetActorToTarget(closestEnemy);
				if (closestEnemy.Actor == null)
					return;
			}

			var enemyUnits =
				FindEnemies(owner,
					owner.World.FindActorsInCircle(owner.Target.CenterPosition, WDist.FromCells(owner.SquadManager.Info.IdleScanRadius)))
				.Select(x => x.Actor)
				.ToList();

			if (enemyUnits.Count == 0)
				return;

			if (AttackOrFleeFuzzy.Default.CanAttack(owner.Units, enemyUnits))
			{
				// Set Hunt stance so units chase targets during attack
				SetSquadEngagementStance(owner, EngagementStance.Hunt);

				// If squad has an approach waypoint (multi-axis attack), move there first
				var attackTarget = owner.ApproachWaypoint ?? owner.TargetActor.Location;
				if (owner.ApproachWaypoint.HasValue)
					owner.ApproachWaypoint = null; // Consume the waypoint

				owner.Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(owner.World, attackTarget), false, groupedActors: owner.Units.ToArray()));

				// We have gathered sufficient units. Attack the nearest enemy unit.
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsAttackMoveState());
			}
			else
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsFleeState(), true);

			foreach (var a in owner.Units)
			{
				if (BusyAttack(a))
					continue;

				var ammoPools = a.TraitsImplementing<AmmoPool>().ToArray();
				if (!ReloadsAutomatically(ammoPools, a.TraitOrDefault<Rearmable>()))
				{
					if (IsRearming(a))
						continue;

					if (!HasAmmo(ammoPools))
					{
						owner.Bot.QueueOrder(new Order("ReturnToBase", a, false));
						continue;
					}
				}

				if (CanAttackTarget(a, owner.TargetActor))
					owner.Bot.QueueOrder(new Order("Attack", a, Target.FromActor(owner.TargetActor), false));
			}
		}

		public void Deactivate(Squad owner) { }
	}

	sealed class GroundUnitsAttackMoveState : GroundStateBase, IState
	{
		int lastUpdatedTick;
		CPos? lastLeaderLocation;
		Actor lastTarget;

		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			if (!owner.IsTargetValid(Leader(owner)))
			{
				var closestEnemy = NewLeaderAndFindClosestEnemy(owner);
				owner.SetActorToTarget(closestEnemy);
				if (closestEnemy.Actor == null)
				{
					owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsFleeState());
					return;
				}
			}

			var leader = Leader(owner);
			if (leader.Location != lastLeaderLocation)
			{
				lastLeaderLocation = leader.Location;
				lastUpdatedTick = owner.World.WorldTick;
			}

			if (owner.TargetActor != lastTarget)
			{
				lastTarget = owner.TargetActor;
				lastUpdatedTick = owner.World.WorldTick;
			}

			// HACK: Drop back to the idle state if we haven't moved in 2.5 seconds
			// This works around the squad being stuck trying to attack-move to a location
			// that they cannot path to, generating expensive pathfinding calls each tick.
			if (owner.World.WorldTick > lastUpdatedTick + 63)
			{
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsIdleState());
				return;
			}

			var ownUnits = owner.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(owner.Units.Count) / 3)
				.Where(owner.Units.Contains).ToHashSet();

			if (ownUnits.Count < owner.Units.Count)
			{
				// Since units have different movement speeds, they get separated while approaching the target.
				// Let them regroup into tighter formation.
				owner.Bot.QueueOrder(new Order("Stop", leader, false));

				var units = owner.Units.Where(a => !ownUnits.Contains(a)).ToArray();
				owner.Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(owner.World, leader.Location), false, groupedActors: units));
			}
			else
			{
				var target = owner.SquadManager.FindClosestEnemy(leader, WDist.FromCells(owner.SquadManager.Info.AttackScanRadius));
				if (target.Actor != null)
				{
					owner.SetActorToTarget(target);
					owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsAttackState());
				}
				else
					owner.Bot.QueueOrder(new Order("AttackMove", null, owner.Target, false, groupedActors: owner.Units.ToArray()));
			}

			if (ShouldFlee(owner))
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsFleeState());
		}

		public void Deactivate(Squad owner) { }
	}

	sealed class GroundUnitsAttackState : GroundStateBase, IState
	{
		int lastUpdatedTick;
		CPos? lastLeaderLocation;
		Actor lastTarget;

		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			if (!owner.IsTargetValid(Leader(owner)))
			{
				var closestEnemy = NewLeaderAndFindClosestEnemy(owner);
				owner.SetActorToTarget(closestEnemy);
				if (closestEnemy.Actor == null)
				{
					owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsFleeState());
					return;
				}
			}

			var leader = Leader(owner);
			if (leader.Location != lastLeaderLocation)
			{
				lastLeaderLocation = leader.Location;
				lastUpdatedTick = owner.World.WorldTick;
			}

			if (owner.TargetActor != lastTarget)
			{
				lastTarget = owner.TargetActor;
				lastUpdatedTick = owner.World.WorldTick;
			}

			// HACK: Drop back to the idle state if we haven't moved in 2.5 seconds
			// This works around the squad being stuck trying to attack-move to a location
			// that they cannot path to, generating expensive pathfinding calls each tick.
			if (owner.World.WorldTick > lastUpdatedTick + 63)
			{
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsIdleState());
				return;
			}

			foreach (var a in owner.Units)
			{
				if (BusyAttack(a))
					continue;

				// Send units with no ammo to resupply instead of uselessly attacking
				var ammoPools = a.TraitsImplementing<AmmoPool>().ToArray();
				if (!ReloadsAutomatically(ammoPools, a.TraitOrDefault<Rearmable>()))
				{
					if (IsRearming(a))
						continue;

					if (!HasAmmo(ammoPools))
					{
						owner.Bot.QueueOrder(new Order("ReturnToBase", a, false));
						continue;
					}
				}

				owner.Bot.QueueOrder(new Order("Attack", a, Target.FromActor(owner.TargetActor), false));
			}

			if (ShouldFlee(owner))
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsFleeState());
		}

		public void Deactivate(Squad owner) { }
	}

	sealed class GroundUnitsFleeState : GroundStateBase, IState
	{
		CPos retreatTarget;

		public void Activate(Squad owner)
		{
			// Reset to Defensive stance when retreating
			SetSquadEngagementStance(owner, EngagementStance.Defensive);

			// Use ThreatMapManager to find the safest retreat location
			var threatMap = owner.World.WorldActor.TraitOrDefault<ThreatMapManager>();
			if (threatMap != null && owner.Units.Count > 0)
			{
				var squadCenter = owner.CenterPosition;
				var squadCell = owner.World.Map.CellContaining(squadCenter);
				retreatTarget = threatMap.FindSafestRetreatCell(squadCell, owner.Bot.Player, 20);
			}
			else
			{
				retreatTarget = RandomBuildingLocation(owner);
			}
		}

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			// Move toward the retreat target
			foreach (var a in owner.Units)
				owner.Bot.QueueOrder(new Order("Move", a, Target.FromCell(owner.World, retreatTarget), false));

			// Transition to regroup state instead of dissolving
			owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsRegroupState(), true);
		}

		public void Deactivate(Squad owner) { }
	}

	class GroundUnitsRegroupState : GroundStateBase, IState
	{
		int regroupTicks;
		const int MaxRegroupTicks = 750; // ~12.5 seconds to regroup before re-engaging or dissolving

		public void Activate(Squad owner)
		{
			regroupTicks = 0;
		}

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			regroupTicks++;

			// Check if most of the squad has regrouped (units close together)
			var leader = owner.Units.First();
			var nearbyCount = owner.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(5))
				.Count(a => owner.Units.Contains(a));

			var regroupedRatio = (float)nearbyCount / owner.Units.Count;

			// If 70%+ of squad has regrouped, check if we should re-engage
			if (regroupedRatio >= 0.7f)
			{
				// Check local threat level
				var threatMap = owner.World.WorldActor.TraitOrDefault<ThreatMapManager>();
				if (threatMap != null)
				{
					var threat = threatMap.GetThreat(leader.Location, owner.Bot.Player);
					if (threat <= 0)
					{
						// Friendly territory — check if we can attack again
						var closestEnemy = FindClosestEnemy(owner);
						if (closestEnemy != null)
						{
							var enemyUnits = owner.World.FindActorsInCircle(closestEnemy.CenterPosition,
								WDist.FromCells(owner.SquadManager.Info.IdleScanRadius))
								.Where(owner.SquadManager.IsPreferredEnemyUnit).ToList();

							if (enemyUnits.Count == 0 || AttackOrFleeFuzzy.Default.CanAttack(owner.Units, enemyUnits))
							{
								// Re-engage! Set Hunt stance and attack
								SetSquadEngagementStance(owner, EngagementStance.Hunt);
								owner.TargetActor = closestEnemy;
								owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsAttackMoveState(), true);
								return;
							}
						}
					}
				}
				else
				{
					// No threat map — fall back to fuzzy logic
					var closestEnemy = FindClosestEnemy(owner);
					if (closestEnemy != null)
					{
						owner.TargetActor = closestEnemy;
						owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsIdleState(), true);
						return;
					}
				}
			}

			// Timeout — dissolve squad and return units to pool
			if (regroupTicks >= MaxRegroupTicks)
			{
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsIdleState(), true);
				return;
			}
		}

		public void Deactivate(Squad owner)
		{
			// If transitioning to idle after timeout, dissolve the squad
			if (regroupTicks >= MaxRegroupTicks)
				owner.Units.Clear();
		}
	}
}
