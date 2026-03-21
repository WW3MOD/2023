/**
 * Runtime unit state during simulation.
 * Wraps a UnitDef with mutable state (HP, suppression, weapon instances, position).
 */

import type { UnitDef, CombatEvent } from './types.js';
import type { WPos } from './wdist.js';
import { wposDistance } from './wdist.js';
import { WeaponInstance } from './weapon.js';
import {
  createSuppressionState, addSuppression, tickSuppression,
  getSuppressionModifiers, getSuppressionAmount,
  type SuppressionState, type SuppressionModifiers,
} from '../sim/suppression.js';

let nextUnitId = 0;

export class SimUnit {
  readonly uid: string;
  readonly def: UnitDef;
  readonly teamIndex: number;

  pos: WPos;
  facing: number;  // degrees

  hp: number;
  alive: boolean = true;

  weapons: WeaponInstance[];
  suppression: SuppressionState;

  // Stats tracking
  damageDealt: number = 0;
  damageReceived: number = 0;
  roundsFired: number = 0;
  tickOfDeath: number | null = null;
  peakSuppression: number = 0;

  currentTarget: SimUnit | null = null;

  constructor(def: UnitDef, teamIndex: number, pos: WPos, facing: number) {
    this.uid = `${def.id}_${nextUnitId++}`;
    this.def = def;
    this.teamIndex = teamIndex;
    this.pos = pos;
    this.facing = facing;
    this.hp = def.hp;
    this.weapons = def.armaments.map(a => new WeaponInstance(a));
    this.suppression = createSuppressionState();
  }

  get isInfantry(): boolean {
    return this.def.isInfantry;
  }

  get hpPercent(): number {
    return this.hp / this.def.hp * 100;
  }

  /** Apply damage to this unit */
  takeDamage(amount: number, tick: number): boolean {
    if (!this.alive) return false;
    this.hp -= amount;
    this.damageReceived += amount;
    if (this.hp <= 0) {
      this.hp = 0;
      this.alive = false;
      this.tickOfDeath = tick;
      // Reset all weapons
      for (const w of this.weapons) w.reset();
      return true; // died
    }
    return false;
  }

  /** Apply suppression from a hit at given distance */
  applySuppression(tiers: { amount: number; range: number }[], distance: number): number {
    const amount = getSuppressionAmount(tiers, distance);
    if (amount > 0) {
      addSuppression(this.suppression, amount, this.isInfantry);
      this.peakSuppression = Math.max(this.peakSuppression, this.suppression.level);
    }
    return amount;
  }

  /** Get current suppression modifiers */
  getModifiers(): SuppressionModifiers {
    return getSuppressionModifiers(this.suppression, this.isInfantry);
  }

  /** Update weapon modifiers from suppression */
  updateWeaponModifiers(): void {
    const mods = this.getModifiers();
    for (const w of this.weapons) {
      w.burstWaitModifier = mods.burstWaitMult;
      w.inaccuracyModifier = mods.inaccuracyMult;
    }
  }

  /** Tick suppression decay */
  tickSuppression(): void {
    tickSuppression(this.suppression, this.isInfantry);
  }

  /** Distance to another unit */
  distanceTo(other: SimUnit): number {
    return wposDistance(this.pos, other.pos);
  }
}

/** Reset the global unit ID counter (call between simulation runs) */
export function resetUnitIds(): void {
  nextUnitId = 0;
}
