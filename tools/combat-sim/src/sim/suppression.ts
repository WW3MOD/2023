/**
 * Suppression system — models infantry (10-tier) and vehicle (5-tier) suppression.
 *
 * Infantry: cap 100, decay 1 per 5 ticks
 *   Tiers every 10 points: speed 90%→0%, inaccuracy 120%→300%, burstWait 110%→200%
 *
 * Vehicle: cap 50, decay 1 per 3 ticks
 *   Tiers every 10 points: turretSpeed 85%→25%, inaccuracy 115%→200%, burstWait 105%→150%
 *   NO speed reduction
 */

export interface SuppressionState {
  level: number;
  ticksSinceLastDecay: number;
}

export interface SuppressionModifiers {
  speedMult: number;         // percentage (100 = normal)
  inaccuracyMult: number;    // percentage (100 = normal, higher = worse)
  burstWaitMult: number;     // percentage (100 = normal, higher = slower)
  turretSpeedMult: number;   // percentage (100 = normal)
  isProne: boolean;          // infantry only, at suppression > 30
}

// Infantry suppression tier tables (tier 0 = no suppression)
// Index = tier (1-10), value = modifier percentage
const INFANTRY_SPEED: number[] =       [100, 90, 80, 70, 60, 50, 40, 30, 20, 10, 0];
const INFANTRY_INACCURACY: number[] =  [100, 120, 140, 160, 180, 200, 220, 240, 260, 280, 300];
const INFANTRY_BURST_WAIT: number[] =  [100, 110, 115, 120, 130, 140, 150, 160, 170, 180, 200];

// Vehicle suppression tier tables (tier 0 = no suppression)
const VEHICLE_TURRET: number[] =       [100, 85, 70, 55, 40, 25];
const VEHICLE_INACCURACY: number[] =   [100, 115, 130, 150, 175, 200];
const VEHICLE_BURST_WAIT: number[] =   [100, 105, 110, 120, 135, 150];

const INFANTRY_CAP = 100;
const INFANTRY_DECAY_INTERVAL = 5;   // ticks between decay
const INFANTRY_DECAY_AMOUNT = 1;

const VEHICLE_CAP = 50;
const VEHICLE_DECAY_INTERVAL = 3;
const VEHICLE_DECAY_AMOUNT = 1;

export function createSuppressionState(): SuppressionState {
  return { level: 0, ticksSinceLastDecay: 0 };
}

/**
 * Add suppression to a unit.
 * @param amount Raw suppression amount from warhead
 * @param isInfantry Whether this is an infantry unit
 */
export function addSuppression(state: SuppressionState, amount: number, isInfantry: boolean): void {
  const cap = isInfantry ? INFANTRY_CAP : VEHICLE_CAP;
  state.level = Math.min(cap, state.level + amount);
}

/**
 * Tick suppression decay.
 */
export function tickSuppression(state: SuppressionState, isInfantry: boolean): void {
  if (state.level <= 0) return;

  state.ticksSinceLastDecay++;
  const interval = isInfantry ? INFANTRY_DECAY_INTERVAL : VEHICLE_DECAY_INTERVAL;
  const amount = isInfantry ? INFANTRY_DECAY_AMOUNT : VEHICLE_DECAY_AMOUNT;

  if (state.ticksSinceLastDecay >= interval) {
    state.level = Math.max(0, state.level - amount);
    state.ticksSinceLastDecay = 0;
  }
}

/**
 * Get current suppression modifiers based on level.
 */
export function getSuppressionModifiers(state: SuppressionState, isInfantry: boolean): SuppressionModifiers {
  if (isInfantry) {
    const tier = Math.min(10, Math.floor(state.level / 10) + (state.level > 0 ? 0 : -1));
    const t = state.level <= 0 ? 0 : Math.min(10, Math.ceil(state.level / 10));
    return {
      speedMult: INFANTRY_SPEED[t] ?? 0,
      inaccuracyMult: INFANTRY_INACCURACY[t] ?? 300,
      burstWaitMult: INFANTRY_BURST_WAIT[t] ?? 200,
      turretSpeedMult: 100,
      isProne: state.level > 30,
    };
  } else {
    const t = state.level <= 0 ? 0 : Math.min(5, Math.ceil(state.level / 10));
    return {
      speedMult: 100,  // Vehicles don't slow down
      inaccuracyMult: VEHICLE_INACCURACY[t] ?? 200,
      burstWaitMult: VEHICLE_BURST_WAIT[t] ?? 150,
      turretSpeedMult: VEHICLE_TURRET[t] ?? 25,
      isProne: false,
    };
  }
}

/**
 * Calculate suppression to apply based on distance from impact and weapon's suppression tiers.
 * Each tier has { amount, range } — we find the first tier where distance <= range.
 */
export function getSuppressionAmount(
  tiers: { amount: number; range: number }[],
  distance: number
): number {
  // Tiers should be sorted by range ascending
  // Find the tier where the unit falls
  for (const tier of tiers) {
    if (distance <= tier.range) {
      return tier.amount;
    }
  }
  return 0; // Outside all suppression ranges
}
