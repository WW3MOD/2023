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

		// True when Phase-4 strategic-target pinning is enabled (experimental-only, default off). Gates every
		// pin read/write in the states below so that with the flag off Squad.StrategicTarget is never touched
		// and each state's target logic is byte-identical to the frozen path.
		protected static bool StrategicTargetPinningEnabled(Squad owner)
		{
			var info = GetHeliModuleInfo(owner);
			return info != null && info.StrategicTargetPinning;
		}

		// Bounded commit-window backstop (ticks) for a pinned strategic objective; 0 = off.
		protected static int PinCommitWindowTicks(Squad owner)
		{
			var info = GetHeliModuleInfo(owner);
			return info != null ? info.PinCommitWindowTicks : 0;
		}

		// True when flight-path hysteresis is enabled (experimental-only, default off). Gates the move/attack-move
		// re-issue damping in the Approach/Withdraw states so every other profile's order cadence is byte-identical.
		protected static bool FlightPathHysteresisEnabled(Squad owner)
		{
			var info = GetHeliModuleInfo(owner);
			return info != null && info.FlightPathHysteresis;
		}

		// Minimum Chebyshev cell shift a recomputed destination must clear before a new path order is issued
		// mid-leg (experimental-only). 0 when off/absent ⇒ HeliPathHysteresis.ShouldRetarget always retargets.
		protected static int FlightPathHysteresisCells(Squad owner)
		{
			var info = GetHeliModuleInfo(owner);
			return info != null ? info.FlightPathHysteresisCells : 0;
		}

		// True while the squad's pinned strategic objective is still a worthwhile, engageable target — alive,
		// in world, occupies space, still an enemy, and not a husk or aircraft. Mirrors the FindClosestEnemy
		// filter so a pin and a fresh re-pick agree on what counts as a target. False (⇒ trigger-1 release) when
		// no pin is set. Reads live actor state exactly as the legacy IsTargetValid/FindClosestEnemy already do
		// (the heli FSM's target choice is not a fog-legal layer; that concern is the danger fields', not this).
		protected static bool StrategicTargetValid(Squad owner)
		{
			var t = owner.StrategicTarget;
			return t != null && !t.IsDead && t.IsInWorld && t.OccupiesSpace != null
				&& owner.Bot.Player.RelationshipWith(t.Owner) == PlayerRelationship.Enemy
				&& !t.Info.HasTraitInfo<HuskInfo>()
				&& !t.Info.HasTraitInfo<AircraftInfo>();
		}

		// Set (pin) the squad's strategic objective and stamp the commit tick. Only ever called under the
		// StrategicTargetPinning flag.
		protected static void PinStrategicTarget(Squad owner, Actor target)
		{
			owner.StrategicTarget = target;
			owner.StrategicCommitTick = owner.World.WorldTick;
		}

		// The pin's lease is still good this eval (trigger 1 + bounded window). Convenience wrapper over the
		// pure HeliMissionPinMath.EvaluatePin so the states read as intent.
		protected static bool StrategicPinHolds(Squad owner)
		{
			return HeliMissionPinMath.EvaluatePin(
				StrategicTargetValid(owner), owner.StrategicCommitTick, owner.World.WorldTick, PinCommitWindowTicks(owner))
				== HeliPinState.Hold;
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

			// Phase-4 strategic-target pinning: if this squad is already committed to a strategic objective
			// (it dropped back to Idle from Approach/Withdraw still carrying a pin), resume toward THAT
			// objective instead of re-picking the closest enemy — the whole point of the pin is that the
			// strategic destination outlives FSM micro-transitions (design §3.3: gate the Idle re-pick behind
			// "mission assigned"). The pin releases here (→ fresh re-pick below) only when its lease is up
			// (objective invalid / bounded window elapsed). Skipped entirely when the flag is off ⇒ byte-identical.
			if (StrategicTargetPinningEnabled(owner) && owner.StrategicTarget != null)
			{
				if (StrategicPinHolds(owner))
				{
					owner.TargetActor = owner.StrategicTarget;
					owner.FuzzyStateMachine.ChangeState(owner, new HelicopterApproachState());
					return;
				}

				owner.StrategicTarget = null;
			}

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

			// Pin the freshly-acquired target as the squad's strategic objective (experimental). From here
			// the tactical TargetActor may be re-aimed by micro-transitions, but this pin holds the strategic
			// destination until an abort trigger releases it.
			if (StrategicTargetPinningEnabled(owner))
				PinStrategicTarget(owner, target);

			owner.FuzzyStateMachine.ChangeState(owner, new HelicopterApproachState());
		}

		public void Deactivate(Squad owner) { }
	}

	class HelicopterApproachState : HelicopterStateBase, IState
	{
		int stuckTicks;

		// Flight-path hysteresis: the attack-move destination the squad is currently committed to. Held across
		// re-evals so a sub-threshold shift in the recomputed cell does not re-path the squad every tick. Only
		// ever written on the experimental hysteresis path ⇒ inert (byte-identical) when the flag is off.
		CPos? committedApproachCell;

		public void Activate(Squad owner)
		{
			stuckTicks = 0;
			committedApproachCell = null;
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
				// Phase-4 pinning: a lapsed TACTICAL target (a transient soft-swap that died, or the objective
				// itself) does not end the mission. If the strategic pin is still valid and its lease holds,
				// resume toward the objective and keep approaching this tick; only when the pin's lease is up
				// (objective invalid / window elapsed) do we release it and fall back to the legacy → Idle re-pick.
				var resumed = false;
				if (StrategicTargetPinningEnabled(owner) && owner.StrategicTarget != null)
				{
					if (StrategicPinHolds(owner))
					{
						owner.TargetActor = owner.StrategicTarget;
						resumed = true;
					}
					else
						owner.StrategicTarget = null;
				}

				if (!resumed)
				{
					owner.FuzzyStateMachine.ChangeState(owner, new HelicopterIdleState());
					return;
				}
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
					// Phase-4 pinning, trigger 2 (heli form, §3.3 N5): the current target is too hot and there is
					// NO soft target to divert to. If that too-hot target IS the strategic objective, release the
					// pin so the squad does not withdraw and then loop straight back onto an unassailable objective.
					// A too-hot TRANSIENT target (soft-swap victim) leaves the pin intact — only the objective itself
					// being unassailable is an abort. ObjectiveTooHotAbort(true, false) centralises the N5 rule.
					if (StrategicTargetPinningEnabled(owner)
						&& ReferenceEquals(owner.TargetActor, owner.StrategicTarget)
						&& HeliMissionPinMath.ObjectiveTooHotAbort(objectiveTooHot: true, softTargetAvailable: false))
						owner.StrategicTarget = null;

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
				if (SquadMaxAirDanger(owner, danger) > danger.AirDangerUnitsToField(info.AirDangerSpikeUnits))
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

			// Flight-path hysteresis (experimental, standoff-only): hold the committed attack-move destination
			// unless the recomputed cell has shifted at least the threshold — so the squad does not re-path on
			// every 5-tick re-eval (which reads as indecisive trajectory churn). A completed leg still re-issues
			// below via the BusyAttackMove guard. Order-cadence only: the destination is still the one the
			// standoff/danger-nav/frontier logic chose above, so the first-contact AA gate stays intact. Off ⇒
			// attackMoveCell is the freshly-computed cell (byte-identical).
			if (standoff && FlightPathHysteresisEnabled(owner))
			{
				if (HeliPathHysteresis.ShouldRetarget(committedApproachCell.HasValue,
					committedApproachCell ?? attackMoveCell, attackMoveCell, FlightPathHysteresisCells(owner)))
					committedApproachCell = attackMoveCell;

				attackMoveCell = committedApproachCell.Value;
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
				// Phase-4 pinning, trigger 6 (stalled): the squad made no progress toward the objective for the
				// stuck window (unreachable — terrain / pathing / an immovable block). Release the pin so the
				// return to Idle does a FRESH re-pick instead of resuming onto the same unreachable objective
				// (which would loop Approach→Idle→Approach forever). Mirrors the design's stall trigger reusing
				// this exact retired safety (§3.3 FIX 4). No-op when the flag is off ⇒ byte-identical.
				if (StrategicTargetPinningEnabled(owner))
					owner.StrategicTarget = null;

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

		// Flight-path hysteresis: the retreat cell the squad is currently committed to. The withdraw block
		// otherwise re-issues a Move every tick to a per-tick-recomputed (jittering) cell — the loudest source
		// of the "indecisive" back-and-forth. Only ever written on the experimental hysteresis path ⇒ inert
		// (byte-identical) when the flag is off.
		CPos? committedRetreatCell;

		public void Activate(Squad owner)
		{
			withdrawTicks = 0;
			committedRetreatCell = null;
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

				// Flight-path hysteresis (experimental): commit to a retreat cell and only adopt a new one once the
				// recomputed cell has shifted at least the threshold, so the squad stops re-pathing every tick. A
				// unit that has completed its leg (idle) still gets re-issued. Off ⇒ Move to the fresh cell every
				// tick for every non-rearming unit, exactly as before (byte-identical).
				var hysteresis = FlightPathHysteresisEnabled(owner);
				var retargeted = true;
				if (hysteresis)
				{
					retargeted = HeliPathHysteresis.ShouldRetarget(committedRetreatCell.HasValue,
						committedRetreatCell ?? retreatCell, retreatCell, FlightPathHysteresisCells(owner));
					if (retargeted)
						committedRetreatCell = retreatCell;

					retreatCell = committedRetreatCell.Value;
				}

				foreach (var u in owner.Units)
				{
					if (IsRearming(u))
						continue;

					// committedRetreatCell is stamped ABOVE this loop, so a dropped Move would leave
					// retargeted false forever after while the unit is non-idle (still flying the AttackMove
					// that created the standing record) — the withdrawal would be lost and the squad would
					// fly on into the AA envelope. That pre-loop stamp is sound because this order is
					// unmarked, hence Protected, hence never droppable. Do NOT mark it Recurring: this is a
					// one-shot-per-state issuance with no retry, so it cannot satisfy the Recurring contract.
					if (!hysteresis || retargeted || u.IsIdle)
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
					// Phase-4 pinning: the withdraw is a tactical pause; the strategic destination outlives it.
					// Re-engage toward the pinned objective (if its lease holds and it is not itself too hot)
					// rather than the nearest enemy, so the squad returns to its mission instead of drifting to
					// whatever wandered closest during the retreat. A lease that is up, or an objective now too
					// hot, releases the pin and falls through to the legacy closest-enemy re-pick.
					if (StrategicTargetPinningEnabled(owner) && owner.StrategicTarget != null)
					{
						if (StrategicPinHolds(owner) && !IsTargetTooHot(owner, owner.StrategicTarget.CenterPosition))
						{
							owner.TargetActor = owner.StrategicTarget;
							owner.FuzzyStateMachine.ChangeState(owner, new HelicopterApproachState());
							return;
						}

						owner.StrategicTarget = null;
					}

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

	// Pure, world-free flight-path hysteresis for attack-heli squads — split out for NUnit like HeliMissionPinMath
	// / HeliDangerNav. Decides whether a recomputed move/attack-move destination has shifted enough to warrant
	// re-pathing, versus holding the leg the squad is already committed to. The point is to stop the FSM re-issuing
	// a fresh path on every 5-tick re-eval (which reads as indecisive trajectory churn) while still following a
	// genuinely relocated objective. Integer-only, deterministic, zero RNG.
	public static class HeliPathHysteresis
	{
		/// <summary>Chebyshev (chessboard) cell distance. WW3MOD's map grid is Rectangular, so max(|dx|,|dy|) is
		/// the true "cells away" a watcher reads on the minimap (conventions.md: CVec.Length is Euclidean and
		/// over-reads diagonals ~1.4x).</summary>
		public static int CellDistance(CPos a, CPos b)
		{
			var dx = a.X - b.X; if (dx < 0) dx = -dx;
			var dy = a.Y - b.Y; if (dy < 0) dy = -dy;
			return dx > dy ? dx : dy;
		}

		/// <summary>Should the squad ADOPT <paramref name="candidate"/> as its committed leg destination? True when
		/// it has no committed destination yet, when <paramref name="thresholdCells"/> is non-positive (hysteresis
		/// off ⇒ always retarget), or when the candidate has moved at least the threshold (Chebyshev) from the
		/// committed cell. A sub-threshold shift returns false — the squad holds its committed leg, ignoring the
		/// churn. Deterministic; no RNG.</summary>
		public static bool ShouldRetarget(bool hasCommitted, CPos committed, CPos candidate, int thresholdCells)
		{
			if (!hasCommitted)
				return true;

			if (thresholdCells <= 0)
				return true;

			return CellDistance(committed, candidate) >= thresholdCells;
		}
	}

	// Whether a squad should keep pursuing its pinned strategic objective this eval or release it.
	public enum HeliPinState { Hold, Release }

	// Pure, world-free strategic-target-pinning decision math for attack-heli squads (Phase 4). Split out for
	// NUnit like HeliDangerNav / HeliStagingMath / HeliEmploymentMath — deterministic, integer-only, zero RNG.
	//
	// This is the heli-squad analogue of MissionCommitmentMath.ShouldReassign: MissionCommitmentMath governs a
	// GROUND offense axis; HeliMissionPinMath governs a heli squad's pinned strategic objective. The rule is the
	// same "commit and HOLD; release only on an explicit abort trigger" — but the trigger set is the SUBSET a
	// heli FSM can feed without the full Brain's score/danger plumbing (design §3.3):
	//   trigger 1 — objective invalid (dead / gone / no longer enemy)     → EvaluatePin
	//   backstop  — bounded commit window (safety valve; <= 0 disables)    → EvaluatePin
	//   trigger 2 — objective ITSELF too hot with no soft target to divert → ObjectiveTooHotAbort (N5)
	//   trigger 6 — stalled: objective unreachable (FSM stuck counter)     → handled at the stuck transition
	// Danger-spike-along-route (full trigger 2) and better-opportunity (trigger 3) need the danger field / rival
	// scores and belong to the future SquadBrain, not this executor-local pin — deliberately out of scope here.
	public static class HeliMissionPinMath
	{
		/// <summary>Trigger 1 + bounded-window backstop — the per-tick lease check. <paramref name="objectiveValid"/>
		/// folds trigger 1 (the pinned objective is dead / gone / no longer an enemy). A
		/// <paramref name="commitWindowTicks"/> &lt;= 0 disables the time valve (hold purely on validity), exactly as
		/// MissionCommitmentMath's window does. Integer-only, deterministic, zero RNG. Mirrors the trigger-1 +
		/// window head of <see cref="MissionCommitmentMath.ShouldReassign(bool,int,int,int,int,int,int,int,long,long,int,int,int,int,int)"/>.</summary>
		public static HeliPinState EvaluatePin(bool objectiveValid, int commitTick, int currentTick, int commitWindowTicks)
		{
			if (!objectiveValid)
				return HeliPinState.Release;

			if (commitWindowTicks > 0 && currentTick - commitTick >= commitWindowTicks)
				return HeliPinState.Release;

			return HeliPinState.Hold;
		}

		/// <summary>Trigger 2 (heli form, design §3.3 N5) — the strategic objective ITSELF reads too hot AND there is
		/// no soft target to service en route. Objective-too-hot WITH a soft target available is executor-local
		/// liberty (the FSM swaps to the soft target and resumes toward the objective), NOT a mission abort; only
		/// too-hot-with-nowhere-to-divert releases the pin, so the squad does not loop back onto an unassailable
		/// objective after every withdraw. Pure boolean predicate.</summary>
		public static bool ObjectiveTooHotAbort(bool objectiveTooHot, bool softTargetAvailable)
		{
			return objectiveTooHot && !softTargetAvailable;
		}
	}
}
