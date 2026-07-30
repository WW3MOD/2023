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
using OpenRA.Mods.Common.Activities;
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
				var ammoPools = u.TraitsImplementing<AmmoPool>();
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
				var ammoPools = u.TraitsImplementing<AmmoPool>();
				if (ReloadsAutomatically(ammoPools, u.TraitOrDefault<Rearmable>()))
					continue;

				if (HasAmmo(ammoPools))
					return true;
			}

			return false;
		}

		// True when this squad's HelicopterSquadBotModule has the rearm-ready gate bypassed.
		// SquadHasAmmo skips every unit whose pools are all covered by a Rearmable (they
		// "reload automatically"), so an all-attack-heli squad reports NO ammo even at full
		// ammo — the launch/re-engage gates then never pass and the squad parks forever.
		// The bypass (experimental-only, default off) lets such squads fly missions anyway.
		protected static bool RearmReadyCheckBypassed(Squad owner)
		{
			var module = owner.Bot.Player.PlayerActor
				.TraitsImplementing<HelicopterSquadBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			return module != null && module.Info.SkipRearmReadyCheck;
		}

		// True when this squad's HelicopterSquadBotModule uses standoff (attack-move) engagement
		// (experimental-only, default off). When on, the FSM issues AttackMove toward the target
		// cell so helis engage the nearest in-range threat at weapon standoff instead of boring
		// toward a distant target and overflying nearer enemies. See HelicopterSquadBotModuleInfo.
		protected static bool StandoffEngagementEnabled(Squad owner)
		{
			var info = GetHeliModuleInfo(owner);
			return info != null && info.StandoffEngagement;
		}

		// The (enabled) HelicopterSquadBotModule Info for this squad's owner, or null. One lookup point
		// for every experimental-only heli tunable.
		protected static HelicopterSquadBotModuleInfo GetHeliModuleInfo(Squad owner)
		{
			var module = owner.Bot.Player.PlayerActor
				.TraitsImplementing<HelicopterSquadBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			return module?.Info;
		}

		// True when Stage-D anti-air danger-field consumption is enabled (experimental-only, default off).
		// Gates the AA-avoidance routing, safe-zone leash, and withdraw-on-spike behaviours below so that
		// every other profile's heli code path stays byte-identical.
		protected static bool DangerFieldAvoidanceEnabled(Squad owner)
		{
			var info = GetHeliModuleInfo(owner);
			return info != null && info.DangerFieldAvoidance;
		}

		// The owner's per-player ANTI-AIR danger field (Stage B), or null if the trait is absent.
		protected static DangerFieldLayer GetDangerField(Squad owner)
		{
			return owner.World.WorldActor.TraitOrDefault<DangerFieldLayer>();
		}

		// Minimum coarse-cell frontier standoff for this squad's heli module (experimental-only, default 0 = off).
		protected static int MinFrontierDistanceCells(Squad owner)
		{
			var info = GetHeliModuleInfo(owner);
			return info != null ? info.MinFrontierDistanceCells : 0;
		}

		// The believed-territory control field (Stage C), or null if the trait is absent.
		protected static ControlField GetControlField(Squad owner)
		{
			return owner.World.WorldActor.TraitOrDefault<ControlField>();
		}

		// Frontier standoff: walk the standoff `cell` rearward — from the target toward the squad (the rear
		// bearing) — in one-coarse-cell hops until the believed frontier distance reaches minFrontierCells,
		// bounded by a small step budget so it is never a free search. Keeps the heli standoff BEHIND the
		// believed front line. Done in WPos (sub-cell) so a diagonal hop crosses a full coarse cell without the
		// integer-cell-space undershoot; the shared FrontierStandoffMath helper also halts at the grid boundary
		// so the engage cell never lands off the playable area. Fog-legal read (ControlField), zero random draws.
		protected static CPos PushHeliBehindFrontier(Squad owner, ControlField control, CPos cell, CPos target, int minFrontierCells)
		{
			var map = owner.World.Map;
			var player = owner.Bot.Player;

			var away = map.CenterOfCell(owner.Units.First().Location) - map.CenterOfCell(target); // toward our own rear
			var step = FrontierStandoffMath.RearwardStep(away, WDist.FromCells(control.Info.CellSize).Length);
			if (step == WVec.Zero)
				return cell;

			var start = map.CenterOfCell(cell);
			var maxSteps = minFrontierCells + 2; // enough to lift a distance-0 cell clear, bounded.
			var steps = FrontierStandoffMath.RearwardSteps(start, step, minFrontierCells, maxSteps,
				w =>
				{
					var (gx, gy) = control.MapCellToGridCell(map.CellContaining(w));
					return control.FrontierDistanceAt(player, gx, gy);
				},
				w => map.Contains(map.CellContaining(w)));

			return map.CellContaining(start + new WVec(step.X * steps, step.Y * steps, 0));
		}

		// An air-danger sampler bound to this squad owner's own air channel. Off-map cells read as
		// Impassable so a leash/detour/retreat search never steers off the playable area. Fog-legal:
		// the field is stamped from the owner's belief store; reads 0 outside every believed AA envelope.
		protected static Func<CPos, int> AirDangerSampler(Squad owner, DangerFieldLayer field)
		{
			var player = owner.Bot.Player;
			var map = owner.World.Map;
			return c => map.Contains(c) ? field.AirDanger(player, c) : HeliDangerNav.Impassable;
		}

		// Highest anti-air danger over the squad's current unit cells — "is a believed AA now shooting us?"
		protected static int SquadMaxAirDanger(Squad owner, DangerFieldLayer field)
		{
			var player = owner.Bot.Player;
			var max = 0;
			foreach (var u in owner.Units)
			{
				var d = field.AirDanger(player, u.Location);
				if (d > max)
					max = d;
			}

			return max;
		}

		// True while the unit is executing an attack-move — either moving toward the destination or,
		// via its child FlyAttack, holding at standoff on an auto-target. BusyAttack only inspects the
		// top-level activity, which under attack-move is AttackMoveActivity, so it cannot see the
		// nested FlyAttack; this guard stops the FSM re-issuing (and thereby cancelling) an active
		// attack-move on every update tick, which would interrupt firing.
		protected static bool BusyAttackMove(Actor a)
		{
			return !a.IsIdle && a.CurrentActivity is AttackMoveActivity;
		}

		protected static Actor FindClosestEnemy(Squad owner, WPos pos)
		{
			return owner.World.Actors
				.Where(a => a.Owner != null && !a.IsDead && a.IsInWorld
					// world.Actors also holds positionless actors (each player's PlayerActor, the world
					// actor). An enemy PlayerActor passes every clause below, but has no IOccupySpace, so
					// the CenterPosition read inside ClosestToIgnoringPath NREs. Skip anything without a
					// position — real units/structures always occupy space, so this only drops non-targets.
					&& a.OccupiesSpace != null
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

			// Don't launch if low on ammo — unless the rearm-ready gate is bypassed. An all-
			// auto-reload heli squad (every pool covered by a Rearmable) makes SquadHasAmmo
			// return false even at FULL ammo, so without the bypass the squad never launches
			// and the helicopters park forever (WW3MOD has no hpad to rearm at).
			if (!RearmReadyCheckBypassed(owner) && !SquadHasAmmo(owner))
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

			var standoff = StandoffEngagementEnabled(owner);

			// Stage-D anti-air consumer (experimental, rides on standoff): route around believed AA,
			// leash the standoff to the AA-safe envelope, and withdraw the moment a NEW AA covers us.
			var avoid = standoff && DangerFieldAvoidanceEnabled(owner);
			var danger = avoid ? GetDangerField(owner) : null;
			if (avoid && danger == null)
				avoid = false;

			if (avoid)
			{
				// Withdraw-on-spike: a newly-believed AA now reads over the squad's own position —
				// stop pushing in, hand to the withdraw state (which re-routes to air-safe ground).
				var info = GetHeliModuleInfo(owner);
				if (SquadMaxAirDanger(owner, danger) > info.AirDangerSpikeThreshold)
				{
					owner.FuzzyStateMachine.ChangeState(owner, new HelicopterWithdrawState());
					return;
				}
			}

			// Standoff engagement (experimental, default off) keeps the squad in this attack-move
			// loop instead of handing off to the close-range AttackRun. AutoTarget engages the
			// nearest in-range threat at weapon standoff and the squad only advances when clear, so
			// helis never bore toward a distant TargetActor's standoff point and overfly nearer
			// enemies. Legacy behaviour (bare Attack + AttackRun hand-off at 8 cells) is preserved
			// byte-for-byte when the flag is off.
			if (!standoff)
			{
				// Check if we're close enough to attack
				var distToTarget = (owner.CenterPosition - owner.TargetActor.CenterPosition).HorizontalLength;
				if (distToTarget < WDist.FromCells(8).Length)
				{
					owner.FuzzyStateMachine.ChangeState(owner, new HelicopterAttackRunState());
					return;
				}
			}

			// Stage-D destination: leash to the AA-safe cell nearest the target, then detour around any
			// AA the straight approach would cross. Falls back to the raw target cell when avoidance is
			// off, so the standoff/legacy paths are unchanged.
			var attackMoveCell = owner.TargetActor.Location;
			if (avoid)
			{
				var info = GetHeliModuleInfo(owner);
				var air = AirDangerSampler(owner, danger);
				var engageCell = HeliDangerNav.LeashedEngageCell(
					owner.TargetActor.Location, info.AirDangerLeashCells, info.AirDangerSafeThreshold, air);
				var from = owner.Units.First().Location;
				var waypoint = HeliDangerNav.DetourWaypoint(
					from, engageCell, info.AirDangerDetourCells, info.AirDangerSafeThreshold, air);
				attackMoveCell = waypoint ?? engageCell;
			}

			// Frontier standoff (experimental, rides on standoff): push the standoff cell rearward until it is
			// at least MinFrontierDistanceCells behind the believed enemy frontier, so helis hold BEHIND the
			// front line. Inert until a ControlField is populated for this player (⇒ zero steps, byte-identical).
			var minFrontier = standoff ? MinFrontierDistanceCells(owner) : 0;
			if (minFrontier > 0)
			{
				var control = GetControlField(owner);
				if (control != null && control.HasField(owner.Bot.Player))
					attackMoveCell = PushHeliBehindFrontier(
						owner, control, attackMoveCell, owner.TargetActor.Location, minFrontier);
			}

			// Move toward target
			var engaging = false;
			foreach (var u in owner.Units)
			{
				if (BusyAttack(u) || IsRearming(u))
				{
					engaging = true;
					continue;
				}

				if (standoff)
				{
					// Don't re-issue while an attack-move is already running (moving or engaging under
					// AttackMoveActivity) — that would cancel an in-progress FlyAttack every update tick.
					if (BusyAttackMove(u))
					{
						engaging = true;
						continue;
					}

					owner.Bot.QueueOrder(new Order("AttackMove", u, Target.FromCell(owner.World, attackMoveCell), false));
				}
				else
					owner.Bot.QueueOrder(new Order("Attack", u, Target.FromActor(owner.TargetActor), false));
			}

			// Stuck detection — an actively engaging / attack-moving standoff squad is not stuck.
			if (standoff && engaging)
				stuckTicks = 0;
			else
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

				var ammoPools = u.TraitsImplementing<AmmoPool>();
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
			if (GetSquadHealthPercent(owner) < 50 || (!RearmReadyCheckBypassed(owner) && !SquadHasAmmo(owner)))
			{
				owner.FuzzyStateMachine.ChangeState(owner, new HelicopterReturnState());
				return;
			}

			// Move away from combat for a bit
			if (withdrawTicks < 75)
			{
				// Find safe retreat location
				CPos retreatCell;

				// Stage-D: re-route to the least AA-covered heading (air-safe corridor) instead of the
				// omniscient ThreatMap. Experimental-only, and — like the approach path — rides on
				// standoff so avoidance is fully inert when StandoffEngagement is off; every other
				// profile keeps the legacy retreat.
				var danger = StandoffEngagementEnabled(owner) && DangerFieldAvoidanceEnabled(owner)
					? GetDangerField(owner) : null;
				if (danger != null)
				{
					var info = GetHeliModuleInfo(owner);
					var air = AirDangerSampler(owner, danger);
					retreatCell = HeliDangerNav.SafestAirCellOnRing(
						owner.Units.First().Location, info.AirDangerRetreatCells, air);
				}
				else
				{
					var threatMap = owner.World.WorldActor.TraitOrDefault<ThreatMapManager>();
					if (threatMap != null)
						retreatCell = threatMap.FindSafestRetreatCell(
							owner.Units.First().Location, owner.Bot.Player, 15);
					else
						retreatCell = RandomBuildingLocation(owner);
				}

				foreach (var u in owner.Units)
				{
					if (IsRearming(u))
						continue;

					owner.Bot.QueueOrder(new Order("Move", u, Target.FromCell(owner.World, retreatCell), false));
				}

				return;
			}

			// After withdrawal period: re-engage if still healthy
			if (GetSquadHealthPercent(owner) >= 70 && (RearmReadyCheckBypassed(owner) || SquadHasAmmo(owner)))
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
