/**
 * Core type definitions for the combat simulator.
 * These mirror the OpenRA YAML structure for combat-relevant traits.
 */

import type { WPos } from './wdist.js';

// --- Unit Definition (parsed from YAML) ---

export interface UnitDef {
  id: string;               // Actor ID (e.g., "E3.america", "abrams")
  name: string;             // Display name
  cost: number;
  hp: number;
  armorType: ArmorType;
  armorThickness: number;   // 0 for infantry
  /** [Front, Right, Rear, Left, Top] as % of thickness */
  armorDistribution: [number, number, number, number, number];
  targetTypes: string[];    // e.g., ["Ground", "Infantry"]
  hitShapeRadius: number;   // WDist
  isInfantry: boolean;
  armaments: ArmamentDef[];
  /** Turret turn speed (WAngle/tick). undefined = no turret (instant facing) */
  turretTurnSpeed?: number;
}

export type ArmorType = 'None' | 'Kevlar' | 'Unarmored' | 'Light' | 'Medium' | 'Heavy';

export interface ArmamentDef {
  name: string;             // "primary", "secondary"
  weapon: WeaponDef;
  aimingDelay: number;      // Ticks to aim before first shot
}

// --- Weapon Definition ---

export interface WeaponDef {
  id: string;
  range: number;            // WDist max range
  minRange: number;         // WDist min range
  burst: number;            // Rounds per burst
  burstDelays: number;      // Ticks between rounds in burst
  burstWait: number;        // Ticks between bursts
  magazine: number;         // Rounds per magazine
  reloadDelay: number;      // Ticks to reload magazine
  validTargets: string[];
  invalidTargets: string[];
  inaccuracy: number;       // WDist
  inaccuracyType: InaccuracyType;
  warheads: WarheadDef[];
  /** Per-caliber suppression effects applied on hit */
  suppressionWarheads: SuppressionWarheadDef[];
}

export type InaccuracyType = 'Maximum' | 'PerCellIncrement' | 'Absolute';

// --- Warhead Definition ---

export interface WarheadDef {
  type: 'TargetDamage' | 'SpreadDamage';
  damage: number;
  penetration: number;
  damageAtMaxRange: number;   // Percentage (100 = no falloff)
  /** Random damage addition (0 = none) */
  randomDamageAddition: number;
  /** Random damage subtraction (0 = none) */
  randomDamageSubtraction: number;
  /** For SpreadDamage: distance between falloff steps (WDist) */
  spread: number;
  /** For SpreadDamage: damage % at each range step */
  falloff: number[];
  /** Armor type -> damage modifier (percentage) */
  versus: Partial<Record<ArmorType, number>>;
  validTargets: string[];
  invalidTargets: string[];
  /** Whether this warhead hits from top (uses top armor) */
  topAttack: boolean;
}

export interface SuppressionWarheadDef {
  /** Suppression amount at each range tier */
  tiers: { amount: number; range: number }[];
  /** Which target types this suppression affects */
  validTargets: string[];
}

// --- Scenario Definition ---

export interface Scenario {
  name: string;
  description: string;
  maxTicks: number;
  runs: number;
  teams: TeamDef[];
}

export interface TeamDef {
  name: string;
  units: ScenarioUnitDef[];
  formation?: FormationPreset;
}

export interface ScenarioUnitDef {
  actorId: string;
  count: number;
  /** Explicit position override (WDist). If not set, uses formation. */
  position?: WPos;
  /** Facing in degrees (0 = right/east, 90 = down/south). Default: face opponent */
  facing?: number;
}

export interface FormationPreset {
  type: 'line' | 'column' | 'wedge' | 'cluster' | 'spread';
  spacing: number;    // WDist between units
  origin: WPos;
  /** Facing direction in degrees */
  facing: number;
}

// --- Simulation Output ---

export interface SimulationResult {
  scenario: string;
  ticks: number;
  runs: number;
  teams: TeamResult[];
  events: CombatEvent[];
}

export interface TeamResult {
  name: string;
  totalUnits: number;
  survivors: number;
  avgSurvivors: number;
  totalCost: number;
  totalDamageDealt: number;
  totalDamageReceived: number;
  avgSuppressionPeak: number;
  units: UnitResult[];
}

export interface UnitResult {
  id: string;
  actorId: string;
  alive: boolean;
  hpRemaining: number;
  hpMax: number;
  damageDealt: number;
  damageReceived: number;
  roundsFired: number;
  tickOfDeath: number | null;
  peakSuppression: number;
}

export interface CombatEvent {
  tick: number;
  type: 'fire' | 'hit' | 'kill' | 'suppression';
  sourceId: string;
  targetId: string;
  damage?: number;
  suppression?: number;
  weapon?: string;
}
