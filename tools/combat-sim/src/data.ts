/**
 * Loads tools/combat-sim/data/stats.json — the JSON dump produced by
 * `OpenRA.Utility ww3mod --dump-balance-json` (engine-side command in
 * OpenRA.Mods.Common/UtilityCommands/DumpBalanceJsonCommand.cs).
 *
 * This file replaces the pre-260511 hardcoded UNITS / SCENARIOS dictionaries
 * in scenarios/library.ts. Stat drift between the sim and the live YAML is
 * structurally impossible now — re-run scripts/dump-stats.sh whenever YAML
 * changes.
 */

import { readFileSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const STATS_PATH = join(here, '..', 'data', 'stats.json');

export interface Armor {
  type: string;
  thickness: number;
  distribution: number[];
}

export interface ArmamentRef {
  name: string;
  weapon: string;        // case-preserved YAML name
  aiming_delay: number;
  fire_delay: number;
  ammo_usage: number;
}

export interface Actor {
  name: string | null;
  cost: number | null;
  hp: number | null;
  armor: Armor | null;
  speed: number | null;
  mover: 'ground' | 'air' | null;
  cargo_max_weight: number | null;
  prerequisites: string[] | null;
  disabled: boolean;
  armaments: ArmamentRef[];
}

export interface Warhead {
  type: string;
  damage: number;
  penetration: number;
  damage_at_max_range: number;
  damage_percent: number;
  random_damage_addition: number;
  random_damage_subtraction: number;
  spread: number | null;
  falloff: number[] | null;
  versus: Record<string, number>;
  valid_targets: string[];
  invalid_targets: string[];
}

export interface Weapon {
  range: number;
  min_range: number;
  burst: number;
  burst_delays: number[];
  burst_wait: number;
  magazine: number;
  reload_delay: number;
  top_attack: boolean;
  bottom_attack: boolean;
  clear_sight_threshold: number;
  free_line_density: number;
  miss_chance_per_density: number;
  valid_targets: string[];
  invalid_targets: string[];
  projectile_kind: string | null;
  projectile_speed: number | null;
  inaccuracy: number | null;
  inaccuracy_type: string | null;
  warheads: Warhead[];
}

export interface Stats {
  _meta: {
    mod: string;
    version: string;
    generated_at: string;
    note: string;
  };
  actors: Record<string, Actor>;
  weapons: Record<string, Weapon>;
}

let cache: Stats | null = null;

export function loadStats(): Stats {
  if (cache) return cache;

  try {
    const raw = readFileSync(STATS_PATH, 'utf8');
    cache = JSON.parse(raw) as Stats;
  } catch (err) {
    throw new Error(
      `Could not load ${STATS_PATH}\n` +
      `Run ./tools/combat-sim/scripts/dump-stats.sh first.\n` +
      `Underlying error: ${(err as Error).message}`
    );
  }

  // Warn loudly if stats.json is older than any rules YAML — drift signal.
  const stale = isStale();
  if (stale) {
    console.error(
      `\n⚠  stats.json is older than ${stale}.\n` +
      `   Re-run ./tools/combat-sim/scripts/dump-stats.sh to refresh.\n`
    );
  }

  return cache;
}

function isStale(): string | null {
  try {
    const statsM = statSync(STATS_PATH).mtimeMs;
    const rulesDir = join(here, '..', '..', '..', 'mods', 'ww3mod', 'rules');
    return findNewerFile(rulesDir, statsM);
  } catch {
    return null;
  }
}

function findNewerFile(dir: string, threshold: number): string | null {
  const { readdirSync } = require('node:fs');
  const { join: pj } = require('node:path');
  const entries = readdirSync(dir, { withFileTypes: true });
  for (const e of entries) {
    const p = pj(dir, e.name);
    if (e.isDirectory()) {
      const inner = findNewerFile(p, threshold);
      if (inner) return inner;
    } else if (e.name.endsWith('.yaml')) {
      try {
        if (statSync(p).mtimeMs > threshold) return p;
      } catch { /* skip */ }
    }
  }
  return null;
}

/** Weapon names in actor armaments are case-preserved YAML; the engine
 *  normalises lookup keys to lowercase. Helper resolves either form. */
export function getWeapon(s: Stats, name: string): Weapon | undefined {
  return s.weapons[name] ?? s.weapons[name.toLowerCase()];
}

/** True if the actor looks combat-relevant for dashboard purposes:
 *  has cost > 0, isn't disabled, and at least one armament fires a
 *  damaging warhead. Filters out civilians, repair drones, and
 *  reference dummies. */
export function isCombatant(s: Stats, a: Actor): boolean {
  if (!a.cost || a.cost <= 0) return false;
  if (a.disabled) return false;
  if (a.armaments.length === 0) return false;
  return a.armaments.some(arm => {
    const w = getWeapon(s, arm.weapon);
    return w?.warheads.some(wh => wh.damage > 0);
  });
}
