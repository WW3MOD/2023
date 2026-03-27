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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	abstract class HelicopterStateBase : StateBase
	{
		static readonly BitSet<TargetableType> AirTargetTypes = new BitSet<TargetableType>("Air");

		protected static int CountAntiAirUnits(IEnumerable<Actor> units)
		{
			var count = 0;
			foreach (var unit in units)
			{
				if (unit == null || unit.Info.HasTraitInfo<AircraftInfo>())
					continue;

				foreach (var ab in unit.TraitsImplementing<AttackBase>())
				{
					if (ab.IsTraitDisabled || ab.IsTraitPaused)
						continue;

					foreach (var a in ab.Armaments)
					{
						if (a.Weapon.IsValidTarget(AirTargetTypes))
						{
							count++;
							break;
						}
					}
				}
			}

			return count;
		}

		protected static int GetSquadHealthPercent(Squad owner)
		{
			if (owner.Units.Count == 0)
				return 0;

			var totalPercent = 0;
			foreach (var u in owner.Units)
			{
				var health = u.TraitOrDefault<IHealth>();
				if (health != null)
					totalPercent += (int)(health.HP * 100L / health.MaxHP);
			}

			return totalPercent / owner.Units.Count;
		}

		protected static int GetUnitHealthPercent(Actor a)
		{
			var health = a.TraitOrDefault<IHealth>();
			if (health == null)
				return 100;

			return (int)(health.HP * 100L / health.MaxHP);
		}

		protected static AIHelicopterRole GetRole(Actor a)
		{
			return a.TraitOrDefault<AIHelicopterRole>();
		}

		protected static int GetFleeThreshold(Squad owner)
		{
			// Use the highest flee threshold from any unit in the squad
			var threshold = 30;
			foreach (var u in owner.Units)
			{
				var role = GetRole(u);
				if (role != null && role.Info.FleeHealthPercent > threshold)
					threshold = role.Info.FleeHealthPercent;
			}

			return threshold;
		}

		protected static void SendDamagedUnitsHome(Squad owner)
		{
			foreach (var u in owner.Units.ToList())
			{
				var role = GetRole(u);
				var threshold = role != null ? role.Info.FleeHealthPercent : 30;
				if (GetUnitHealthPercent(u) < threshold)
					owner.Bot.QueueOrder(new Order("ReturnToBase", u, false));
			}
		}

		protected static void SendLowAmmoUnitsHome(Squad owner)
		{
			foreach (var u in owner.Units)
			{
				var ammoPools = u.TraitsImplementing<AmmoPool>().ToArray();
				if (!ReloadsAutomatically(ammoPools, u.TraitOrDefault<Rearmable>()) && !HasAmmo(ammoPools))
				{
					if (!IsRearming(u))
						owner.Bot.QueueOrder(new Order("ReturnToBase", u, false));
				}
			}
		}

		protected static bool SquadHasAmmo(Squad owner)
		{
			foreach (var u in owner.Units)
			{
				var ammoPools = u.TraitsImplementing<AmmoPool>().ToArray();
				if (ReloadsAutomatically(ammoPools, u.TraitOrDefault<Rearmable>()))
					continue;

				if (HasAmmo(ammoPools))
					return true;
			}

			return false;
		}

		protected static Actor FindClosestEnemy(Squad owner, WPos pos)
		{
			return owner.World.Actors
				.Where(a => a.Owner != null && !a.IsDead && a.IsInWorld
					&& owner.Bot.Player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
					&& !a.Info.HasTraitInfo<HuskInfo>()
					&& !a.Info.HasTraitInfo<AircraftInfo>())
				.ClosestToIgnoringPath(pos);
		}

		protected static int CountAntiAirNearTarget(Squad owner, WPos targetPos, int radiusCells)
		{
			var enemies = owner.World.FindActorsInCircle(targetPos, WDist.FromCells(radiusCells))
				.Where(a => a.Owner != null
					&& owner.Bot.Player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy);
			return CountAntiAirUnits(enemies);
		}

		protected static bool IsTargetTooHot(Squad owner, WPos targetPos)
		{
			var aaCount = CountAntiAirNearTarget(owner, targetPos, 10);
			// More than 2 AA units per helicopter = too dangerous
			return aaCount > owner.Units.Count * 2;
		}

		protected virtual bool ShouldFlee(Squad owner)
		{
			return GetSquadHealthPercent(owner) < GetFleeThreshold(owner);
		}
	}

	class HelicopterIdleState : HelicopterStateBase, IState
	{
		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			// Don't launch if any unit is rearming
			foreach (var u in owner.Units)
				if (IsRearming(u))
					return;

			// Don't launch if squad is damaged — wait for repair
			if (GetSquadHealthPercent(owner) < 80)
				return;

			// Don't launch if low on ammo
			if (!SquadHasAmmo(owner))
				return;

			// Find a target — prefer weak enemy clusters via ThreatMap
			var threatMap = owner.World.WorldActor.TraitOrDefault<ThreatMapManager>();
			Actor target = null;

			if (threatMap != null)
			{
				var weakCell = threatMap.FindWeakestEnemyCell(owner.Bot.Player);
				if (weakCell != CPos.Zero)
				{
					var enemies = owner.World.FindActorsInCircle(
						owner.World.Map.CenterOfCell(weakCell), WDist.FromCells(12))
						.Where(a => a.Owner != null
							&& owner.Bot.Player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
							&& !a.Info.HasTraitInfo<AircraftInfo>()
							&& a.Info.HasTraitInfo<IHealthInfo>());

					target = enemies
						.Where(e => !IsTargetTooHot(owner, e.CenterPosition))
						.OrderByDescending(e =>
						{
							var valued = e.Info.TraitInfoOrDefault<ValuedInfo>();
							return valued != null ? valued.Cost : 0;
						})
						.FirstOrDefault();
				}
			}

			// Fallback: find closest enemy that isn't heavily defended
			if (target == null)
			{
				var leader = owner.Units.First();
				var closestEnemy = FindClosestEnemy(owner, leader.CenterPosition);
				if (closestEnemy != null && !IsTargetTooHot(owner, closestEnemy.CenterPosition))
					target = closestEnemy;
			}

			if (target == null)
				return;

			owner.TargetActor = target;
			owner.FuzzyStateMachine.ChangeState(owner, new HelicopterApproachState());
		}

		public void Deactivate(Squad owner) { }
	}

	class HelicopterApproachState : HelicopterStateBase, IState
	{
		int stuckTicks;

		public void Activate(Squad owner)
		{
			stuckTicks = 0;
		}

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			// HP check — abort approach if damaged
			if (ShouldFlee(owner))
			{
				owner.FuzzyStateMachine.ChangeState(owner, new HelicopterReturnState());
				return;
			}

			if (!owner.IsTargetValid)
			{
				owner.FuzzyStateMachine.ChangeState(owner, new HelicopterIdleState());
				return;
			}

			// Check if target has become too dangerous
			if (IsTargetTooHot(owner, owner.TargetActor.CenterPosition))
			{
				// Try to find a softer target nearby
				var leader = owner.Units.First();
				var softTarget = owner.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(20))
					.Where(a => a.Owner != null
						&& owner.Bot.Player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
						&& !a.Info.HasTraitInfo<AircraftInfo>()
						&& !IsTargetTooHot(owner, a.CenterPosition))
					.ClosestToIgnoringPath(leader.CenterPosition);

				if (softTarget != null)
					owner.TargetActor = softTarget;
				else
				{
					owner.FuzzyStateMachine.ChangeState(owner, new HelicopterWithdrawState());
					return;
				}
			}

			// Check if we're close enough to attack
			var distToTarget = (owner.CenterPosition - owner.TargetActor.CenterPosition).HorizontalLength;
			if (distToTarget < WDist.FromCells(8).Length)
			{
				owner.FuzzyStateMachine.ChangeState(owner, new HelicopterAttackRunState());
				return;
			}

			// Move toward target
			foreach (var u in owner.Units)
			{
				if (BusyAttack(u) || IsRearming(u))
					continue;

				owner.Bot.QueueOrder(new Order("Attack", u, Target.FromActor(owner.TargetActor), false));
			}

			// Stuck detection
			stuckTicks++;
			if (stuckTicks > 200)
			{
				owner.FuzzyStateMachine.ChangeState(owner, new HelicopterIdleState());
				return;
			}

			SendLowAmmoUnitsHome(owner);
		}

		public void Deactivate(Squad owner) { }
	}

	class HelicopterAttackRunState : HelicopterStateBase, IState
	{
		int attackTicks;

		public void Activate(Squad owner)
		{
			attackTicks = 0;
		}

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			attackTicks++;

			// Individual HP checks — send damaged units home immediately
			SendDamagedUnitsHome(owner);

			// Squad-level flee check
			if (ShouldFlee(owner))
			{
				owner.FuzzyStateMachine.ChangeState(owner, new HelicopterWithdrawState());
				return;
			}

			// Hit-and-run: pull back after cooldown ticks
			var hitAndRunCooldown = 150;
			foreach (var u in owner.Units)
			{
				var role = GetRole(u);
				if (role != null)
				{
					hitAndRunCooldown = role.Info.HitAndRunCooldown;
					break;
				}
			}

			if (hitAndRunCooldown > 0 && attackTicks >= hitAndRunCooldown)
			{
				owner.FuzzyStateMachine.ChangeState(owner, new HelicopterWithdrawState());
				return;
			}

			if (!owner.IsTargetValid)
			{
				// Target died — find another nearby
				var leader = owner.Units.First();
				var nextTarget = owner.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(12))
					.Where(a => a.Owner != null
						&& owner.Bot.Player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
						&& !a.Info.HasTraitInfo<AircraftInfo>()
						&& a.Info.HasTraitInfo<IHealthInfo>())
					.ClosestToIgnoringPath(leader.CenterPosition);

				if (nextTarget != null)
					owner.TargetActor = nextTarget;
				else
				{
					// No more targets — withdraw
					owner.FuzzyStateMachine.ChangeState(owner, new HelicopterWithdrawState());
					return;
				}
			}

			// Attack!
			foreach (var u in owner.Units)
			{
				if (IsRearming(u))
					continue;

				var ammoPools = u.TraitsImplementing<AmmoPool>().ToArray();
				if (!ReloadsAutomatically(ammoPools, u.TraitOrDefault<Rearmable>()) && !HasAmmo(ammoPools))
				{
					owner.Bot.QueueOrder(new Order("ReturnToBase", u, false));
					continue;
				}

				if (BusyAttack(u))
					continue;

				if (CanAttackTarget(u, owner.TargetActor))
					owner.Bot.QueueOrder(new Order("Attack", u, Target.FromActor(owner.TargetActor), false));
			}

			SendLowAmmoUnitsHome(owner);
		}

		public void Deactivate(Squad owner) { }
	}

	class HelicopterWithdrawState : HelicopterStateBase, IState
	{
		int withdrawTicks;

		public void Activate(Squad owner)
		{
			withdrawTicks = 0;
		}

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			withdrawTicks++;

			// Send damaged units home
			SendDamagedUnitsHome(owner);
			SendLowAmmoUnitsHome(owner);

			// Check if squad is too damaged to re-engage — full return
			if (GetSquadHealthPercent(owner) < 50 || !SquadHasAmmo(owner))
			{
				owner.FuzzyStateMachine.ChangeState(owner, new HelicopterReturnState());
				return;
			}

			// Move away from combat for a bit
			if (withdrawTicks < 75)
			{
				// Find safe retreat location
				var threatMap = owner.World.WorldActor.TraitOrDefault<ThreatMapManager>();
				CPos retreatCell;

				if (threatMap != null)
					retreatCell = threatMap.FindSafestRetreatCell(
						owner.Units.First().Location, owner.Bot.Player, 15);
				else
					retreatCell = RandomBuildingLocation(owner);

				foreach (var u in owner.Units)
				{
					if (IsRearming(u))
						continue;

					owner.Bot.QueueOrder(new Order("Move", u, Target.FromCell(owner.World, retreatCell), false));
				}

				return;
			}

			// After withdrawal period: re-engage if still healthy
			if (GetSquadHealthPercent(owner) >= 70 && SquadHasAmmo(owner))
			{
				// Find a new target
				var leader = owner.Units.FirstOrDefault();
				if (leader != null)
				{
					var newTarget = FindClosestEnemy(owner, leader.CenterPosition);
					if (newTarget != null && !IsTargetTooHot(owner, newTarget.CenterPosition))
					{
						owner.TargetActor = newTarget;
						owner.FuzzyStateMachine.ChangeState(owner, new HelicopterApproachState());
						return;
					}
				}
			}

			// Can't re-engage — return to base
			owner.FuzzyStateMachine.ChangeState(owner, new HelicopterReturnState());
		}

		public void Deactivate(Squad owner) { }
	}

	class HelicopterReturnState : HelicopterStateBase, IState
	{
		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			foreach (var u in owner.Units)
			{
				if (IsRearming(u))
					continue;

				owner.Bot.QueueOrder(new Order("ReturnToBase", u, false));
			}

			// Go back to idle — the idle state will wait for repair/rearm before launching again
			owner.FuzzyStateMachine.ChangeState(owner, new HelicopterIdleState());
		}

		public void Deactivate(Squad owner) { }
	}
}
