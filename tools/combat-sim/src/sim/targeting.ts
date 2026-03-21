/**
 * Target selection — finds the best target for a unit to engage.
 * Currently: nearest valid target (matching weapon's ValidTargets).
 */

import type { SimUnit } from '../model/unit.js';
import type { WeaponInstance } from '../model/weapon.js';

export interface TargetResult {
  target: SimUnit;
  weapon: WeaponInstance;
  distance: number;
}

/**
 * Find the best target for a unit from the enemy team.
 * Selects the nearest enemy that at least one weapon can engage.
 */
export function findTarget(unit: SimUnit, enemies: SimUnit[]): TargetResult | null {
  let bestTarget: SimUnit | null = null;
  let bestWeapon: WeaponInstance | null = null;
  let bestDistance = Infinity;

  for (const enemy of enemies) {
    if (!enemy.alive) continue;

    const distance = unit.distanceTo(enemy);

    for (const weapon of unit.weapons) {
      if (!weapon.canTarget(enemy.def.targetTypes)) continue;
      if (!weapon.inRange(distance)) continue;

      if (distance < bestDistance) {
        bestDistance = distance;
        bestTarget = enemy;
        bestWeapon = weapon;
      }
    }
  }

  if (!bestTarget || !bestWeapon) return null;

  return { target: bestTarget, weapon: bestWeapon, distance: bestDistance };
}

/**
 * Find the best weapon for a specific target.
 * Prefers highest damage weapon that can engage the target.
 */
export function findBestWeapon(unit: SimUnit, target: SimUnit, distance: number): WeaponInstance | null {
  let best: WeaponInstance | null = null;
  let bestDamage = -1;

  for (const weapon of unit.weapons) {
    if (!weapon.canTarget(target.def.targetTypes)) continue;
    if (!weapon.inRange(distance)) continue;

    // Estimate damage from first warhead
    const dmg = weapon.def.warheads[0]?.damage ?? 0;
    if (dmg > bestDamage) {
      bestDamage = dmg;
      best = weapon;
    }
  }

  return best;
}
