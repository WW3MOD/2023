/**
 * Damage calculation — replicates DamageWarhead.cs and SpreadDamageWarhead.cs logic.
 *
 * Pipeline:
 * 1. Base damage + random variation
 * 2. Armor penetration check (penetration vs effective thickness)
 * 3. Range damage falloff
 * 4. Versus armor-type modifiers
 * 5. SpreadDamage distance-based falloff (for AoE)
 */

import type { WarheadDef, ArmorType } from '../model/types.js';

export type ArmorFace = 'front' | 'right' | 'rear' | 'left' | 'top';

/**
 * Get the armor distribution index for a given face.
 * Distribution array: [Front, Right, Rear, Left, Top]
 */
function armorFaceIndex(face: ArmorFace): number {
  switch (face) {
    case 'front': return 0;
    case 'right': return 1;
    case 'rear':  return 2;
    case 'left':  return 3;
    case 'top':   return 4;
  }
}

/**
 * Determine which armor face is hit based on attack angle.
 * Angle is in degrees: 0 = attacking from front, 90 = from right, 180 = from rear.
 */
export function getArmorFace(attackAngleDeg: number): ArmorFace {
  const a = ((attackAngleDeg % 360) + 360) % 360;
  if (a <= 45 || a > 315) return 'front';
  if (a > 45 && a <= 135) return 'right';
  if (a > 135 && a <= 225) return 'rear';
  return 'left';
}

export interface DamageCalcInput {
  warhead: WarheadDef;
  /** Distance from firer to target in WDist */
  distance: number;
  /** Max weapon range in WDist */
  maxRange: number;
  /** Target armor type */
  targetArmorType: ArmorType;
  /** Target armor thickness */
  targetThickness: number;
  /** Target armor distribution [F, R, B, L, T] */
  targetDistribution: [number, number, number, number, number];
  /** Which face is hit */
  armorFace: ArmorFace;
  /** For SpreadDamage: distance from impact point to this unit (WDist) */
  distanceFromImpact?: number;
  /** Random number generator (returns 0-1). Default: Math.random */
  rng?: () => number;
}

export interface DamageResult {
  /** Final damage after all modifiers */
  finalDamage: number;
  /** Whether the shot penetrated armor */
  penetrated: boolean;
  /** Effective armor thickness after direction */
  effectiveThickness: number;
  /** Range damage multiplier (0-100) */
  rangeMult: number;
  /** Versus modifier (percentage) */
  versusMult: number;
  /** AoE falloff multiplier (0-100), 100 if not AoE */
  aoeFalloff: number;
}

/**
 * Calculate damage from a single warhead hit.
 */
export function calculateDamage(input: DamageCalcInput): DamageResult {
  const { warhead, distance, maxRange, targetArmorType, targetThickness,
          targetDistribution, armorFace, distanceFromImpact } = input;
  const rng = input.rng ?? Math.random;

  // Step 1: Base damage + random variation
  let damage = warhead.damage;
  if (warhead.randomDamageAddition > 0) {
    damage += Math.floor(rng() * (warhead.randomDamageAddition + 1));
  }
  if (warhead.randomDamageSubtraction > 0) {
    damage -= Math.floor(rng() * (warhead.randomDamageSubtraction + 1));
  }

  // Step 2: Armor penetration
  const dirPercent = targetDistribution[armorFaceIndex(armorFace)];
  const effectiveThickness = Math.floor(targetThickness * dirPercent / 100);
  let penetrated = true;

  if (effectiveThickness > 0 && warhead.penetration > 0) {
    if (warhead.penetration < effectiveThickness) {
      // Can't fully penetrate — damage reduced proportionally
      damage = Math.floor(damage * warhead.penetration / effectiveThickness);
      penetrated = false;
    }
    // If penetration >= thickness, full damage passes through
  } else if (effectiveThickness > 0 && warhead.penetration === 0) {
    // No penetration value and armor exists — minimal damage
    damage = Math.floor(damage * 1 / (effectiveThickness + 1));
    penetrated = false;
  }

  // Step 3: Range damage falloff (for TargetDamage warheads)
  let rangeMult = 100;
  if (warhead.type === 'TargetDamage' && maxRange > 0) {
    const rangeRatio = Math.min(1, distance / maxRange);
    rangeMult = Math.round((1 - rangeRatio) * 100 + rangeRatio * warhead.damageAtMaxRange);
    damage = Math.floor(damage * rangeMult / 100);
  }

  // Step 4: Versus armor-type modifiers
  const versusMult = warhead.versus[targetArmorType] ?? 100;
  damage = Math.floor(damage * versusMult / 100);

  // Step 5: SpreadDamage AoE falloff
  let aoeFalloff = 100;
  if (warhead.type === 'SpreadDamage' && distanceFromImpact !== undefined
      && warhead.falloff.length > 0 && warhead.spread > 0) {
    aoeFalloff = getSpreadFalloff(distanceFromImpact, warhead.spread, warhead.falloff);
    damage = Math.floor(damage * aoeFalloff / 100);
  }

  return {
    finalDamage: Math.max(0, damage),
    penetrated,
    effectiveThickness,
    rangeMult,
    versusMult,
    aoeFalloff,
  };
}

/**
 * Linear interpolation of SpreadDamage falloff.
 * Spread = distance between falloff steps.
 * Falloff = [100, 50, 25, 12, ...] percentages at each step.
 * Returns damage percentage (0-100) for a given distance from impact.
 */
export function getSpreadFalloff(distance: number, spread: number, falloff: number[]): number {
  if (distance <= 0) return falloff[0] ?? 100;
  if (spread <= 0) return falloff[0] ?? 100;

  const stepIndex = distance / spread;
  const lowerIndex = Math.floor(stepIndex);
  const upperIndex = lowerIndex + 1;

  if (lowerIndex >= falloff.length - 1) {
    // Beyond last falloff step — use last value or 0
    return falloff[falloff.length - 1] ?? 0;
  }

  // Linear interpolation between steps
  const t = stepIndex - lowerIndex;
  const lower = falloff[lowerIndex] ?? 0;
  const upper = falloff[upperIndex] ?? 0;
  return Math.round(lower + (upper - lower) * t);
}
