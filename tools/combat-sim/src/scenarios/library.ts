/**
 * Hardcoded unit definitions and built-in scenarios for Phase 1.
 * These will be replaced by YAML loading in Phase 2.
 */

import type { UnitDef, WeaponDef, WarheadDef, ArmamentDef, SuppressionWarheadDef, Scenario } from '../model/types.js';
import { parseWDist } from '../model/wdist.js';

// ============================================================
// SUPPRESSION WARHEAD TEMPLATES
// ============================================================

const smallCaliberSuppression: SuppressionWarheadDef = {
  validTargets: ['Infantry'],
  tiers: [
    { amount: 50, range: parseWDist('2c0') },
    { amount: 25, range: parseWDist('4c0') },
    { amount: 12, range: parseWDist('8c0') },
    { amount: 6, range: parseWDist('16c0') },
    { amount: 3, range: parseWDist('32c0') },
    { amount: 2, range: parseWDist('64c0') },
    { amount: 1, range: parseWDist('128c0') },
  ],
};

const largeCaliberInfantrySuppression: SuppressionWarheadDef = {
  validTargets: ['Infantry'],
  tiers: [
    { amount: 50, range: parseWDist('5c0') },
    { amount: 25, range: parseWDist('10c0') },
    { amount: 12, range: parseWDist('20c0') },
    { amount: 6, range: parseWDist('40c0') },
    { amount: 3, range: parseWDist('80c0') },
    { amount: 2, range: parseWDist('160c0') },
    { amount: 1, range: parseWDist('320c0') },
  ],
};

const largeCaliberVehicleSuppression: SuppressionWarheadDef = {
  validTargets: ['Light', 'Medium', 'Heavy'],
  tiers: [
    { amount: 8, range: parseWDist('10c0') },
    { amount: 4, range: parseWDist('20c0') },
    { amount: 2, range: parseWDist('40c0') },
  ],
};

const explosionInfantrySuppression: SuppressionWarheadDef = {
  validTargets: ['Infantry'],
  tiers: [
    { amount: 50, range: parseWDist('20c0') },
    { amount: 25, range: parseWDist('40c0') },
    { amount: 12, range: parseWDist('80c0') },
    { amount: 6, range: parseWDist('160c0') },
    { amount: 3, range: parseWDist('320c0') },
  ],
};

const explosionVehicleSuppression: SuppressionWarheadDef = {
  validTargets: ['Light', 'Medium', 'Heavy'],
  tiers: [
    { amount: 12, range: 128 },
    { amount: 6, range: 256 },
    { amount: 3, range: 512 },
  ],
};

// ============================================================
// WEAPON DEFINITIONS
// ============================================================

function makeWarhead(overrides: Partial<WarheadDef> = {}): WarheadDef {
  return {
    type: 'TargetDamage',
    damage: 0,
    penetration: 0,
    damageAtMaxRange: 50,
    randomDamageAddition: 0,
    randomDamageSubtraction: 0,
    spread: 0,
    falloff: [],
    versus: {},
    validTargets: [],
    invalidTargets: [],
    topAttack: false,
    ...overrides,
  };
}

// 5.56mm DMR (Rifleman primary)
const weapon556: WeaponDef = {
  id: '5.56mm.DMR',
  range: parseWDist('10c0'),
  minRange: 0,
  burst: 3,
  burstDelays: 4,
  burstWait: 20,
  magazine: 20,
  reloadDelay: 60,
  validTargets: ['Infantry', 'Unarmored'],
  invalidTargets: [],
  inaccuracy: 256,
  inaccuracyType: 'Maximum',
  warheads: [makeWarhead({ damage: 200, penetration: 4, validTargets: ['Infantry', 'Unarmored'] })],
  suppressionWarheads: [smallCaliberSuppression],
};

// 7.62mm MG (Humvee primary)
const weapon762MG: WeaponDef = {
  id: '7.62mm.MG',
  range: parseWDist('15c0'),
  minRange: 0,
  burst: 6,
  burstDelays: 2,
  burstWait: 10,
  magazine: 100,
  reloadDelay: 150,
  validTargets: ['Infantry', 'Unarmored'],
  invalidTargets: [],
  inaccuracy: 384,
  inaccuracyType: 'Maximum',
  warheads: [makeWarhead({ damage: 250, penetration: 5, validTargets: ['Infantry', 'Unarmored'] })],
  suppressionWarheads: [smallCaliberSuppression],
};

// 12.7mm HMG
const weapon127: WeaponDef = {
  id: '12.7mm.MG',
  range: parseWDist('16c0'),
  minRange: 0,
  burst: 5,
  burstDelays: 2,
  burstWait: 12,
  magazine: 100,
  reloadDelay: 150,
  validTargets: ['Infantry', 'Unarmored', 'Light', 'Helicopter'],
  invalidTargets: [],
  inaccuracy: 256,
  inaccuracyType: 'Maximum',
  warheads: [makeWarhead({
    damage: 600, penetration: 15,
    validTargets: ['Infantry', 'Unarmored', 'Light', 'Helicopter'],
  })],
  suppressionWarheads: [largeCaliberInfantrySuppression, largeCaliberVehicleSuppression],
};

// 25mm Chaingun (Bradley)
const weapon25mm: WeaponDef = {
  id: '25mm.Bradley',
  range: parseWDist('18c0'),
  minRange: 0,
  burst: 5,
  burstDelays: 3,
  burstWait: 15,
  magazine: 900,
  reloadDelay: 300,
  validTargets: ['Infantry', 'Unarmored', 'Light', 'Medium', 'Helicopter'],
  invalidTargets: [],
  inaccuracy: 200,
  inaccuracyType: 'Maximum',
  warheads: [makeWarhead({
    damage: 600, penetration: 20,
    validTargets: ['Infantry', 'Unarmored', 'Light', 'Medium', 'Helicopter'],
  })],
  suppressionWarheads: [explosionInfantrySuppression, explosionVehicleSuppression],
};

// TOW ATGM (Bradley secondary)
const weaponTOW: WeaponDef = {
  id: 'TOW.Bradley',
  range: parseWDist('40c0'),
  minRange: parseWDist('2c0'),
  burst: 1,
  burstDelays: 0,
  burstWait: 120,
  magazine: 8,
  reloadDelay: 300,
  validTargets: ['Light', 'Medium', 'Heavy'],
  invalidTargets: [],
  inaccuracy: 0,
  inaccuracyType: 'Absolute',
  warheads: [makeWarhead({
    damage: 1500, penetration: 40, damageAtMaxRange: 100,
    validTargets: ['Light', 'Medium', 'Heavy'],
  })],
  suppressionWarheads: [explosionVehicleSuppression],
};

// 120mm Tank Gun (Abrams)
const weapon120mm: WeaponDef = {
  id: '120mm.Abrams',
  range: parseWDist('22c0'),
  minRange: 0,
  burst: 1,
  burstDelays: 0,
  burstWait: 120,
  magazine: 40,
  reloadDelay: 300,
  validTargets: ['Infantry', 'Unarmored', 'Light', 'Medium', 'Heavy'],
  invalidTargets: [],
  inaccuracy: 128,
  inaccuracyType: 'Maximum',
  warheads: [makeWarhead({
    damage: 1500, penetration: 50, damageAtMaxRange: 80,
    validTargets: ['Infantry', 'Unarmored', 'Light', 'Medium', 'Heavy'],
  })],
  suppressionWarheads: [explosionInfantrySuppression, explosionVehicleSuppression],
};

// 125mm Tank Gun (T-90)
const weapon125mm: WeaponDef = {
  id: '125mm.T90',
  range: parseWDist('20c0'),
  minRange: 0,
  burst: 1,
  burstDelays: 0,
  burstWait: 130,
  magazine: 40,
  reloadDelay: 320,
  validTargets: ['Infantry', 'Unarmored', 'Light', 'Medium', 'Heavy'],
  invalidTargets: [],
  inaccuracy: 150,
  inaccuracyType: 'Maximum',
  warheads: [makeWarhead({
    damage: 1600, penetration: 45, damageAtMaxRange: 80,
    validTargets: ['Infantry', 'Unarmored', 'Light', 'Medium', 'Heavy'],
  })],
  suppressionWarheads: [explosionInfantrySuppression, explosionVehicleSuppression],
};

// RPG (Rifleman secondary)
const weaponRPG: WeaponDef = {
  id: 'RPG',
  range: parseWDist('6c0'),
  minRange: parseWDist('1c0'),
  burst: 1,
  burstDelays: 0,
  burstWait: 80,
  magazine: 1,
  reloadDelay: 200,
  validTargets: ['Unarmored', 'Light', 'Medium', 'Heavy'],
  invalidTargets: [],
  inaccuracy: 512,
  inaccuracyType: 'Maximum',
  warheads: [makeWarhead({
    type: 'SpreadDamage',
    damage: 1000, penetration: 25, damageAtMaxRange: 100,
    spread: 256,
    falloff: [100, 50, 25, 10],
    validTargets: ['Unarmored', 'Light', 'Medium', 'Heavy'],
  })],
  suppressionWarheads: [explosionInfantrySuppression],
};

// Mortar
const weaponMortar: WeaponDef = {
  id: 'Mortar.60mm',
  range: parseWDist('20c0'),
  minRange: parseWDist('4c0'),
  burst: 1,
  burstDelays: 0,
  burstWait: 60,
  magazine: 8,
  reloadDelay: 200,
  validTargets: ['Infantry', 'Unarmored', 'Light'],
  invalidTargets: [],
  inaccuracy: parseWDist('2c0'),
  inaccuracyType: 'Maximum',
  warheads: [makeWarhead({
    type: 'SpreadDamage',
    damage: 1200, penetration: 10, damageAtMaxRange: 100,
    spread: 200,
    falloff: [100, 70, 40, 20, 10, 5],
    validTargets: ['Infantry', 'Unarmored', 'Light'],
  })],
  suppressionWarheads: [explosionInfantrySuppression],
};

// ============================================================
// UNIT DEFINITIONS
// ============================================================

function arm(name: string, weapon: WeaponDef, aimingDelay = 5): ArmamentDef {
  return { name, weapon, aimingDelay };
}

export const UNITS: Record<string, UnitDef> = {
  // --- Infantry ---
  'E3.america': {
    id: 'E3.america', name: 'Rifleman (USA)', cost: 100, hp: 200,
    armorType: 'None', armorThickness: 0, armorDistribution: [100, 100, 100, 100, 100],
    targetTypes: ['Ground', 'Infantry'], hitShapeRadius: 30, isInfantry: true,
    armaments: [arm('primary', weapon556), arm('secondary', weaponRPG, 10)],
  },
  'E3.russia': {
    id: 'E3.russia', name: 'Rifleman (RUS)', cost: 100, hp: 200,
    armorType: 'None', armorThickness: 0, armorDistribution: [100, 100, 100, 100, 100],
    targetTypes: ['Ground', 'Infantry'], hitShapeRadius: 30, isInfantry: true,
    armaments: [arm('primary', weapon556), arm('secondary', weaponRPG, 10)],
  },
  'mortar.america': {
    id: 'mortar.america', name: 'Mortar Team (USA)', cost: 300, hp: 200,
    armorType: 'None', armorThickness: 0, armorDistribution: [100, 100, 100, 100, 100],
    targetTypes: ['Ground', 'Infantry'], hitShapeRadius: 30, isInfantry: true,
    armaments: [arm('primary', weaponMortar, 15)],
  },

  // --- Vehicles ---
  'humvee': {
    id: 'humvee', name: 'Humvee', cost: 600, hp: 8000,
    armorType: 'Light', armorThickness: 10, armorDistribution: [100, 80, 80, 80, 60],
    targetTypes: ['Ground', 'Unarmored', 'Light'], hitShapeRadius: 512, isInfantry: false,
    armaments: [arm('primary', weapon762MG, 3)],
  },
  'hmg.humvee': {
    id: 'hmg.humvee', name: 'HMG Humvee', cost: 800, hp: 8000,
    armorType: 'Light', armorThickness: 10, armorDistribution: [100, 80, 80, 80, 60],
    targetTypes: ['Ground', 'Unarmored', 'Light'], hitShapeRadius: 512, isInfantry: false,
    armaments: [arm('primary', weapon127, 3)],
    turretTurnSpeed: 40,
  },
  'bradley': {
    id: 'bradley', name: 'Bradley IFV', cost: 1500, hp: 14000,
    armorType: 'Medium', armorThickness: 15, armorDistribution: [100, 80, 80, 80, 60],
    targetTypes: ['Ground', 'Medium'], hitShapeRadius: 600, isInfantry: false,
    armaments: [arm('primary', weapon25mm, 5), arm('secondary', weaponTOW, 10)],
    turretTurnSpeed: 30,
  },
  'abrams': {
    id: 'abrams', name: 'M1 Abrams', cost: 2500, hp: 28000,
    armorType: 'Heavy', armorThickness: 700, armorDistribution: [100, 40, 15, 10, 10],
    targetTypes: ['Ground', 'Heavy'], hitShapeRadius: 700, isInfantry: false,
    armaments: [arm('primary', weapon120mm, 8)],
    turretTurnSpeed: 20,
  },
  't90': {
    id: 't90', name: 'T-90', cost: 2200, hp: 26000,
    armorType: 'Heavy', armorThickness: 650, armorDistribution: [100, 35, 15, 10, 10],
    targetTypes: ['Ground', 'Heavy'], hitShapeRadius: 680, isInfantry: false,
    armaments: [arm('primary', weapon125mm, 8)],
    turretTurnSpeed: 22,
  },
};

// ============================================================
// BUILT-IN SCENARIOS
// ============================================================

export const SCENARIOS: Record<string, Scenario> = {
  'infantry-mirror': {
    name: 'Infantry Mirror Match',
    description: '6 riflemen vs 6 riflemen at 8 cell range',
    maxTicks: 3000,
    runs: 20,
    teams: [
      {
        name: 'USA Rifles',
        units: [{ actorId: 'E3.america', count: 6 }],
        formation: { type: 'line', spacing: 1024, origin: { x: 0, y: 0 }, facing: 0 },
      },
      {
        name: 'RUS Rifles',
        units: [{ actorId: 'E3.russia', count: 6 }],
        formation: { type: 'line', spacing: 1024, origin: { x: 8192, y: 0 }, facing: 180 },
      },
    ],
  },

  'infantry-vs-vehicle': {
    name: 'Infantry Squad vs Bradley',
    description: '6 riflemen + 2 AT vs 1 Bradley at 12 cell range',
    maxTicks: 3000,
    runs: 20,
    teams: [
      {
        name: 'Infantry Squad',
        units: [
          { actorId: 'E3.america', count: 6 },
          { actorId: 'E3.america', count: 2 },  // AT capable via RPG
        ],
        formation: { type: 'line', spacing: 1024, origin: { x: 0, y: 0 }, facing: 0 },
      },
      {
        name: 'IFV',
        units: [{ actorId: 'bradley', count: 1 }],
        formation: { type: 'cluster', spacing: 2048, origin: { x: 12288, y: 0 }, facing: 180 },
      },
    ],
  },

  'tank-duel': {
    name: 'Abrams vs T-90',
    description: '3 Abrams vs 3 T-90 at 18 cell range, frontal engagement',
    maxTicks: 5000,
    runs: 50,
    teams: [
      {
        name: 'USA Armor',
        units: [{ actorId: 'abrams', count: 3 }],
        formation: { type: 'line', spacing: 2048, origin: { x: 0, y: 0 }, facing: 0 },
      },
      {
        name: 'RUS Armor',
        units: [{ actorId: 't90', count: 3 }],
        formation: { type: 'line', spacing: 2048, origin: { x: 18432, y: 0 }, facing: 180 },
      },
    ],
  },

  'formation-aoe': {
    name: 'Clustered vs Spread vs Mortar',
    description: '4 riflemen clustered vs 2 mortars, then 4 spread vs 2 mortars',
    maxTicks: 3000,
    runs: 30,
    teams: [
      {
        name: 'Infantry (clustered)',
        units: [{ actorId: 'E3.america', count: 4 }],
        formation: { type: 'cluster', spacing: 512, origin: { x: 0, y: 0 }, facing: 0 },
      },
      {
        name: 'Mortar Team',
        units: [{ actorId: 'mortar.america', count: 2 }],
        formation: { type: 'line', spacing: 2048, origin: { x: 12288, y: 0 }, facing: 180 },
      },
    ],
  },

  'formation-spread-aoe': {
    name: 'Spread Infantry vs Mortar',
    description: '4 riflemen spread vs 2 mortars — compare with formation-aoe',
    maxTicks: 3000,
    runs: 30,
    teams: [
      {
        name: 'Infantry (spread)',
        units: [{ actorId: 'E3.america', count: 4 }],
        formation: { type: 'spread', spacing: 1024, origin: { x: 0, y: 0 }, facing: 0 },
      },
      {
        name: 'Mortar Team',
        units: [{ actorId: 'mortar.america', count: 2 }],
        formation: { type: 'line', spacing: 2048, origin: { x: 12288, y: 0 }, facing: 180 },
      },
    ],
  },

  'suppression-test': {
    name: 'HMG Suppression Test',
    description: '1 HMG Humvee vs 6 riflemen — measures suppression impact',
    maxTicks: 2000,
    runs: 20,
    teams: [
      {
        name: 'HMG Vehicle',
        units: [{ actorId: 'hmg.humvee', count: 1 }],
        formation: { type: 'cluster', spacing: 1024, origin: { x: 0, y: 0 }, facing: 0 },
      },
      {
        name: 'Rifle Squad',
        units: [{ actorId: 'E3.america', count: 6 }],
        formation: { type: 'line', spacing: 1024, origin: { x: 12288, y: 0 }, facing: 180 },
      },
    ],
  },

  'cost-efficiency': {
    name: 'Equal Cost: Humvees vs Bradley',
    description: '2 Humvees ($1200) vs 1 Bradley ($1500) — cost efficiency test',
    maxTicks: 3000,
    runs: 30,
    teams: [
      {
        name: '2x Humvee',
        units: [{ actorId: 'humvee', count: 2 }],
        formation: { type: 'line', spacing: 2048, origin: { x: 0, y: 0 }, facing: 0 },
      },
      {
        name: '1x Bradley',
        units: [{ actorId: 'bradley', count: 1 }],
        formation: { type: 'cluster', spacing: 1024, origin: { x: 14336, y: 0 }, facing: 180 },
      },
    ],
  },
};

/** List all available unit IDs */
export function listUnits(): string[] {
  return Object.keys(UNITS);
}

/** List all available scenario IDs */
export function listScenarios(): string[] {
  return Object.keys(SCENARIOS);
}

/** Get a unit definition by ID */
export function getUnit(id: string): UnitDef | undefined {
  return UNITS[id];
}

/** Get a scenario by ID */
export function getScenario(id: string): Scenario | undefined {
  return SCENARIOS[id];
}
