/**
 * Weapon firing cycle state machine.
 *
 * States: idle → aiming → bursting → burstWaiting → (reloading) → idle
 *
 * Each tick, call weapon.tick() to advance the state.
 * When tick() returns a 'fire' action, the simulation should apply damage.
 */

import type { WeaponDef, ArmamentDef } from './types.js';

export type WeaponState = 'idle' | 'aiming' | 'bursting' | 'burstWaiting' | 'reloading';

export interface WeaponAction {
  type: 'fire' | 'none';
  weaponId?: string;
}

export class WeaponInstance {
  readonly def: WeaponDef;
  readonly armament: ArmamentDef;

  state: WeaponState = 'idle';
  aimingTicksLeft: number = 0;
  burstRoundsLeft: number;
  burstDelayTicksLeft: number = 0;
  burstWaitTicksLeft: number = 0;
  magazineRoundsLeft: number;
  reloadTicksLeft: number = 0;
  totalRoundsFired: number = 0;

  /** Modifiers from suppression (percentage, 100 = normal) */
  burstWaitModifier: number = 100;
  inaccuracyModifier: number = 100;

  constructor(armament: ArmamentDef) {
    this.armament = armament;
    this.def = armament.weapon;
    this.burstRoundsLeft = this.def.burst;
    this.magazineRoundsLeft = this.def.magazine;
  }

  /** Returns effective inaccuracy (WDist) after modifiers */
  get effectiveInaccuracy(): number {
    return Math.floor(this.def.inaccuracy * this.inaccuracyModifier / 100);
  }

  /** Check if this weapon can engage a target with the given target types */
  canTarget(targetTypes: string[]): boolean {
    // Check valid targets — at least one must match
    const hasValid = this.def.validTargets.length === 0 ||
      this.def.validTargets.some(vt => targetTypes.includes(vt));
    if (!hasValid) return false;

    // Check invalid targets — none must match
    const hasInvalid = this.def.invalidTargets.some(it => targetTypes.includes(it));
    return !hasInvalid;
  }

  /** Check if target is in range */
  inRange(distance: number): boolean {
    return distance <= this.def.range && distance >= this.def.minRange;
  }

  /** Acquire a new target — starts aiming */
  acquire(): void {
    if (this.state === 'idle') {
      this.state = 'aiming';
      this.aimingTicksLeft = this.armament.aimingDelay;
    }
  }

  /** Reset to idle (target lost) */
  reset(): void {
    this.state = 'idle';
    this.aimingTicksLeft = 0;
    this.burstDelayTicksLeft = 0;
    this.burstWaitTicksLeft = 0;
  }

  /** Advance one tick. Returns action to take. */
  tick(): WeaponAction {
    switch (this.state) {
      case 'idle':
        return { type: 'none' };

      case 'aiming':
        if (this.aimingTicksLeft > 0) {
          this.aimingTicksLeft--;
          return { type: 'none' };
        }
        // Aiming complete — fire first round
        return this.fireRound();

      case 'bursting':
        if (this.burstDelayTicksLeft > 0) {
          this.burstDelayTicksLeft--;
          return { type: 'none' };
        }
        // Burst delay elapsed — fire next round
        return this.fireRound();

      case 'burstWaiting':
        if (this.burstWaitTicksLeft > 0) {
          this.burstWaitTicksLeft--;
          return { type: 'none' };
        }
        // Burst wait over — check magazine
        if (this.magazineRoundsLeft <= 0) {
          this.state = 'reloading';
          this.reloadTicksLeft = this.def.reloadDelay;
          return { type: 'none' };
        }
        // Start new burst
        this.burstRoundsLeft = this.def.burst;
        return this.fireRound();

      case 'reloading':
        if (this.reloadTicksLeft > 0) {
          this.reloadTicksLeft--;
          return { type: 'none' };
        }
        // Reload complete
        this.magazineRoundsLeft = this.def.magazine;
        this.burstRoundsLeft = this.def.burst;
        this.state = 'bursting';
        return this.fireRound();
    }
  }

  private fireRound(): WeaponAction {
    this.burstRoundsLeft--;
    this.magazineRoundsLeft--;
    this.totalRoundsFired++;

    if (this.burstRoundsLeft <= 0) {
      // Burst complete
      this.state = 'burstWaiting';
      const effectiveBurstWait = Math.floor(this.def.burstWait * this.burstWaitModifier / 100);
      this.burstWaitTicksLeft = effectiveBurstWait;
      this.burstRoundsLeft = this.def.burst;
    } else {
      // More rounds in burst
      this.state = 'bursting';
      this.burstDelayTicksLeft = this.def.burstDelays;
    }

    // Check if magazine empty after this round
    if (this.magazineRoundsLeft <= 0 && this.state === 'bursting') {
      // Need to reload before next round in burst
      this.state = 'reloading';
      this.reloadTicksLeft = this.def.reloadDelay;
    }

    return { type: 'fire', weaponId: this.def.id };
  }
}
