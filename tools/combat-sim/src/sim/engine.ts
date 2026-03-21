/**
 * Simulation engine — tick-by-tick combat resolution.
 *
 * Each tick:
 * 1. Decay suppression for all units
 * 2. Update weapon modifiers from suppression
 * 3. For each alive unit: find target, tick weapons, apply damage/suppression
 * 4. Check win condition (one team eliminated)
 */

import type { Scenario, CombatEvent, SimulationResult, TeamResult, UnitResult } from '../model/types.js';
import { SimUnit, resetUnitIds } from '../model/unit.js';
import { findTarget, findBestWeapon } from './targeting.js';
import { calculateDamage, getArmorFace, type DamageCalcInput } from './damage.js';
import { wposDistance } from '../model/wdist.js';

export interface SimulationSetup {
  teams: SimUnit[][];
  maxTicks: number;
  scenarioName: string;
  /** Random seed function (returns 0-1). Default: Math.random */
  rng?: () => number;
  /** Whether to record per-tick events (can be large) */
  recordEvents?: boolean;
}

export interface SingleRunResult {
  ticks: number;
  winnerTeam: number | null;  // team index, or null for draw
  teams: SimUnit[][];
  events: CombatEvent[];
}

/**
 * Run a single simulation to completion.
 */
export function runSimulation(setup: SimulationSetup): SingleRunResult {
  const { teams, maxTicks, rng = Math.random } = setup;
  const recordEvents = setup.recordEvents ?? false;
  const events: CombatEvent[] = [];
  const allUnits = teams.flat();

  // Shuffle function to randomize firing order each tick (prevents first-mover advantage)
  function shuffle<T>(arr: T[]): T[] {
    for (let i = arr.length - 1; i > 0; i--) {
      const j = Math.floor(rng() * (i + 1));
      [arr[i], arr[j]] = [arr[j], arr[i]];
    }
    return arr;
  }

  for (let tick = 0; tick < maxTicks; tick++) {
    // Step 1: Decay suppression
    for (const unit of allUnits) {
      if (unit.alive) unit.tickSuppression();
    }

    // Step 2: Update weapon modifiers from suppression
    for (const unit of allUnits) {
      if (unit.alive) unit.updateWeaponModifiers();
    }

    // Step 3: Each unit tries to fire (randomized order to prevent first-mover bias)
    const firingOrder = shuffle([...allUnits]);
    for (const unit of firingOrder) {
      if (!unit.alive) continue;

      // Get enemies
      const enemyTeamIndex = unit.teamIndex === 0 ? 1 : 0;
      const enemies = teams[enemyTeamIndex];

      // Find or keep target
      if (!unit.currentTarget || !unit.currentTarget.alive) {
        const result = findTarget(unit, enemies);
        if (result) {
          unit.currentTarget = result.target;
          // Acquire target on all weapons that can engage
          for (const weapon of unit.weapons) {
            if (weapon.canTarget(result.target.def.targetTypes) &&
                weapon.inRange(result.distance)) {
              weapon.acquire();
            }
          }
        } else {
          unit.currentTarget = null;
          for (const weapon of unit.weapons) weapon.reset();
          continue;
        }
      }

      const target = unit.currentTarget;
      const distance = unit.distanceTo(target);

      // Tick each weapon
      for (const weapon of unit.weapons) {
        if (!weapon.canTarget(target.def.targetTypes)) continue;
        if (!weapon.inRange(distance)) {
          weapon.reset();
          continue;
        }

        if (weapon.state === 'idle') {
          weapon.acquire();
        }

        const action = weapon.tick();

        if (action.type === 'fire') {
          unit.roundsFired++;

          // Calculate inaccuracy offset for this shot
          const effectiveInaccuracy = weapon.effectiveInaccuracy;
          let inaccuracyOffset = 0;
          if (effectiveInaccuracy > 0) {
            inaccuracyOffset = calculateInaccuracy(
              effectiveInaccuracy,
              weapon.def.inaccuracyType,
              distance,
              weapon.def.range,
              rng
            );
          }

          // Apply each warhead
          for (const warhead of weapon.def.warheads) {
            // Check warhead valid targets
            if (warhead.validTargets.length > 0 &&
                !warhead.validTargets.some(t => target.def.targetTypes.includes(t))) {
              continue;
            }
            if (warhead.invalidTargets.some(t => target.def.targetTypes.includes(t))) {
              continue;
            }

            // Determine armor face based on relative positions
            const attackAngle = getAttackAngle(unit, target);
            const armorFace = warhead.topAttack ? 'top' as const : getArmorFace(attackAngle);

            const damageInput: DamageCalcInput = {
              warhead,
              distance,
              maxRange: weapon.def.range,
              targetArmorType: target.def.armorType,
              targetThickness: target.def.armorThickness,
              targetDistribution: target.def.armorDistribution,
              armorFace,
              distanceFromImpact: warhead.type === 'SpreadDamage' ? inaccuracyOffset : undefined,
              rng,
            };

            const result = calculateDamage(damageInput);

            if (result.finalDamage > 0) {
              const killed = target.takeDamage(result.finalDamage, tick);
              unit.damageDealt += result.finalDamage;

              if (recordEvents) {
                events.push({
                  tick,
                  type: killed ? 'kill' : 'hit',
                  sourceId: unit.uid,
                  targetId: target.uid,
                  damage: result.finalDamage,
                  weapon: weapon.def.id,
                });
              }

              // Apply AoE damage to nearby enemies if SpreadDamage
              if (warhead.type === 'SpreadDamage' && warhead.spread > 0) {
                applyAoE(unit, target, warhead, weapon, enemies, distance, tick, events, recordEvents, rng);
              }
            }
          }

          // Apply suppression from this weapon
          for (const suppWarhead of weapon.def.suppressionWarheads) {
            // Check if suppression applies to this target type
            if (suppWarhead.validTargets.length > 0 &&
                !suppWarhead.validTargets.some(t => target.def.targetTypes.includes(t))) {
              continue;
            }

            const suppAmount = target.applySuppression(suppWarhead.tiers, distance);
            if (suppAmount > 0 && recordEvents) {
              events.push({
                tick,
                type: 'suppression',
                sourceId: unit.uid,
                targetId: target.uid,
                suppression: suppAmount,
                weapon: weapon.def.id,
              });
            }

            // Apply suppression to nearby enemies too (AoE suppression)
            for (const nearby of enemies) {
              if (!nearby.alive || nearby === target) continue;
              const nearbyDist = wposDistance(target.pos, nearby.pos);
              nearby.applySuppression(suppWarhead.tiers, nearbyDist);
            }
          }
        }
      }
    }

    // Step 4: Check win condition
    const team0Alive = teams[0].some(u => u.alive);
    const team1Alive = teams[1].some(u => u.alive);

    if (!team0Alive && !team1Alive) {
      return { ticks: tick + 1, winnerTeam: null, teams, events };
    }
    if (!team0Alive) {
      return { ticks: tick + 1, winnerTeam: 1, teams, events };
    }
    if (!team1Alive) {
      return { ticks: tick + 1, winnerTeam: 0, teams, events };
    }
  }

  // Time limit reached — determine winner by remaining HP
  const team0Hp = teams[0].reduce((s, u) => s + (u.alive ? u.hp : 0), 0);
  const team1Hp = teams[1].reduce((s, u) => s + (u.alive ? u.hp : 0), 0);
  const winner = team0Hp > team1Hp ? 0 : team1Hp > team0Hp ? 1 : null;

  return { ticks: maxTicks, winnerTeam: winner, teams, events };
}

/**
 * Apply AoE (SpreadDamage) to all enemies near the impact point.
 * The primary target already took damage; this handles splash to others.
 */
function applyAoE(
  attacker: SimUnit,
  primaryTarget: SimUnit,
  warhead: CombatEvent extends never ? never : Parameters<typeof calculateDamage>[0]['warhead'],
  weapon: { def: { range: number; id: string } },
  enemies: SimUnit[],
  firingDistance: number,
  tick: number,
  events: CombatEvent[],
  recordEvents: boolean,
  rng: () => number,
): void {
  for (const nearby of enemies) {
    if (!nearby.alive || nearby === primaryTarget) continue;

    const distFromImpact = wposDistance(primaryTarget.pos, nearby.pos);

    // Check if within AoE range
    const maxAoeRange = warhead.spread * (warhead.falloff.length - 1);
    if (distFromImpact > maxAoeRange) continue;

    const attackAngle = getAttackAngle(attacker, nearby);
    const armorFace = warhead.topAttack ? 'top' as const : getArmorFace(attackAngle);

    const result = calculateDamage({
      warhead,
      distance: firingDistance,
      maxRange: weapon.def.range,
      targetArmorType: nearby.def.armorType,
      targetThickness: nearby.def.armorThickness,
      targetDistribution: nearby.def.armorDistribution,
      armorFace,
      distanceFromImpact: distFromImpact,
      rng,
    });

    if (result.finalDamage > 0) {
      const killed = nearby.takeDamage(result.finalDamage, tick);
      attacker.damageDealt += result.finalDamage;

      if (recordEvents) {
        events.push({
          tick,
          type: killed ? 'kill' : 'hit',
          sourceId: attacker.uid,
          targetId: nearby.uid,
          damage: result.finalDamage,
          weapon: weapon.def.id,
        });
      }
    }
  }
}

/**
 * Calculate attack angle (degrees) for armor face determination.
 * 0 = attacking from front, 180 = from rear.
 */
function getAttackAngle(attacker: SimUnit, target: SimUnit): number {
  const dx = attacker.pos.x - target.pos.x;
  const dy = attacker.pos.y - target.pos.y;
  const attackFromAngle = Math.atan2(dy, dx) * 180 / Math.PI;
  // Relative to target's facing
  let relAngle = attackFromAngle - target.facing;
  relAngle = ((relAngle % 360) + 360) % 360;
  return relAngle;
}

/**
 * Calculate inaccuracy offset based on weapon type.
 * Returns WDist offset from intended target position.
 */
function calculateInaccuracy(
  baseInaccuracy: number,
  type: string,
  distance: number,
  maxRange: number,
  rng: () => number,
): number {
  let maxOffset: number;

  switch (type) {
    case 'Maximum':
      // Scales 0→max from min range to max range
      maxOffset = maxRange > 0 ? baseInaccuracy * distance / maxRange : baseInaccuracy;
      break;
    case 'PerCellIncrement':
      // Scales linearly with distance (per cell)
      maxOffset = baseInaccuracy * distance / 1024;
      break;
    case 'Absolute':
    default:
      maxOffset = baseInaccuracy;
      break;
  }

  // Random offset in range [-maxOffset, maxOffset]
  return Math.floor((rng() * 2 - 1) * maxOffset);
}

/**
 * Compile results from a single run into structured output.
 */
export function compileRunResult(run: SingleRunResult, scenarioName: string): SimulationResult {
  const teamResults: TeamResult[] = run.teams.map((team, i) => {
    const units: UnitResult[] = team.map(u => ({
      id: u.uid,
      actorId: u.def.id,
      alive: u.alive,
      hpRemaining: u.hp,
      hpMax: u.def.hp,
      damageDealt: u.damageDealt,
      damageReceived: u.damageReceived,
      roundsFired: u.roundsFired,
      tickOfDeath: u.tickOfDeath,
      peakSuppression: u.peakSuppression,
    }));

    return {
      name: `Team ${i}`,
      totalUnits: team.length,
      survivors: team.filter(u => u.alive).length,
      avgSurvivors: team.filter(u => u.alive).length,
      totalCost: team.reduce((s, u) => s + u.def.cost, 0),
      totalDamageDealt: team.reduce((s, u) => s + u.damageDealt, 0),
      totalDamageReceived: team.reduce((s, u) => s + u.damageReceived, 0),
      avgSuppressionPeak: team.reduce((s, u) => s + u.peakSuppression, 0) / team.length,
      units,
    };
  });

  return {
    scenario: scenarioName,
    ticks: run.ticks,
    runs: 1,
    teams: teamResults,
    events: run.events,
  };
}

/**
 * Run multiple simulations and average the results.
 */
export function runMultiple(
  createSetup: () => SimulationSetup,
  runs: number,
): SimulationResult {
  const allResults: SingleRunResult[] = [];

  for (let i = 0; i < runs; i++) {
    resetUnitIds();
    const setup = createSetup();
    allResults.push(runSimulation(setup));
  }

  // Average results across runs
  const firstRun = allResults[0];
  const scenarioName = createSetup().scenarioName;

  // Compile average stats
  const teamResults: TeamResult[] = firstRun.teams.map((_, teamIdx) => {
    const avgSurvivors = allResults.reduce(
      (s, r) => s + r.teams[teamIdx].filter(u => u.alive).length, 0
    ) / runs;

    const avgDamageDealt = allResults.reduce(
      (s, r) => s + r.teams[teamIdx].reduce((ss, u) => ss + u.damageDealt, 0), 0
    ) / runs;

    const avgDamageReceived = allResults.reduce(
      (s, r) => s + r.teams[teamIdx].reduce((ss, u) => ss + u.damageReceived, 0), 0
    ) / runs;

    const avgSuppressionPeak = allResults.reduce(
      (s, r) => s + r.teams[teamIdx].reduce((ss, u) => ss + u.peakSuppression, 0) / r.teams[teamIdx].length, 0
    ) / runs;

    const totalUnits = firstRun.teams[teamIdx].length;
    const totalCost = firstRun.teams[teamIdx].reduce((s, u) => s + u.def.cost, 0);

    // Use last run's unit details for individual results
    const lastRun = allResults[allResults.length - 1];
    const units: UnitResult[] = lastRun.teams[teamIdx].map(u => ({
      id: u.uid,
      actorId: u.def.id,
      alive: u.alive,
      hpRemaining: u.hp,
      hpMax: u.def.hp,
      damageDealt: u.damageDealt,
      damageReceived: u.damageReceived,
      roundsFired: u.roundsFired,
      tickOfDeath: u.tickOfDeath,
      peakSuppression: u.peakSuppression,
    }));

    return {
      name: `Team ${teamIdx}`,
      totalUnits,
      survivors: lastRun.teams[teamIdx].filter(u => u.alive).length,
      avgSurvivors,
      totalCost,
      totalDamageDealt: avgDamageDealt,
      totalDamageReceived: avgDamageReceived,
      avgSuppressionPeak,
      units,
    };
  });

  const avgTicks = allResults.reduce((s, r) => s + r.ticks, 0) / runs;
  const winsTeam0 = allResults.filter(r => r.winnerTeam === 0).length;
  const winsTeam1 = allResults.filter(r => r.winnerTeam === 1).length;

  return {
    scenario: scenarioName,
    ticks: Math.round(avgTicks),
    runs,
    teams: teamResults,
    events: [], // Don't aggregate events across runs
  };
}
