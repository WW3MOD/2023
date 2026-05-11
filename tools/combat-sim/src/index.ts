#!/usr/bin/env node
/**
 * WW3MOD Balance Dashboard
 *
 * Stat-introspection tool backed by the live YAML, via the JSON dump
 * produced by `OpenRA.Utility ww3mod --dump-balance-json` (regenerate
 * with ./scripts/dump-stats.sh). The pre-260511 hardcoded combat
 * simulator was replaced after we discovered it had drifted by 5-15×
 * from the real YAML on key stats, producing misleading verdicts.
 *
 * For ground-truth combat outcomes, use the in-game test harness:
 *   ./tools/test/run-test.sh test-balance-tank-1v1
 *   ./tools/test/run-batch.sh test-balance-*
 *
 * Dashboard commands here only inspect stats / compute static derived
 * numbers (DPS, HP-per-credit, damage-per-credit). They never simulate
 * combat — the engine is the only authority on combat outcomes.
 */

import { loadStats, getWeapon, isCombatant, type Actor, type Weapon, type Stats } from './data.js';
import { formatWDist, wdistToCells } from './wdist.js';

interface Flags {
  positional: string[];
  format: 'table' | 'json';
}

function parseFlags(args: string[]): Flags {
  const flags: Flags = { positional: [], format: 'table' };
  for (let i = 0; i < args.length; i++) {
    const a = args[i];
    if (a === '--json') flags.format = 'json';
    else if (a === '--format' && i + 1 < args.length) flags.format = args[++i] as Flags['format'];
    else if (!a.startsWith('--')) flags.positional.push(a);
  }
  return flags;
}

function usage() {
  console.log(`
WW3MOD Balance Dashboard
=========================

Stat-introspection from live YAML (via --dump-balance-json).
For combat outcomes, use ./tools/test/run-test.sh test-balance-*.

Commands:
  units                   List combatant actors with key stats
  weapons                 List combat weapons
  actor <id>              Full stat dump for one actor
  weapon <id>             Full stat dump for one weapon
  compare <a> <b>         Side-by-side actor comparison
  dps <id>                DPS calculations for one actor's armaments
  tier-cost               Cost-vs-power tables grouped by class

Options:
  --json                  Emit JSON instead of tables

Refresh stats:
  ./tools/combat-sim/scripts/dump-stats.sh
`);
}

function main() {
  const args = process.argv.slice(2);
  if (args.length === 0) { usage(); return; }
  const cmd = args[0];
  const flags = parseFlags(args.slice(1));
  const stats = loadStats();

  switch (cmd) {
    case 'units':       return cmdUnits(stats, flags);
    case 'weapons':     return cmdWeapons(stats, flags);
    case 'actor':       return cmdActor(stats, flags);
    case 'weapon':      return cmdWeapon(stats, flags);
    case 'compare':     return cmdCompare(stats, flags);
    case 'dps':         return cmdDps(stats, flags);
    case 'tier-cost':   return cmdTierCost(stats, flags);
    case 'run':
    case 'duel':
    case 'list':
      console.error(
        `\n"${cmd}" was a combat-simulation command in the old sim.\n` +
        `Combat math now lives in the engine; use the AUTOTEST harness:\n` +
        `  ./tools/test/list-tests.sh        # all test-balance-* scenarios\n` +
        `  ./tools/test/run-test.sh test-balance-tank-1v1\n`
      );
      process.exit(2);
    default:
      console.error(`Unknown command: ${cmd}`);
      usage();
      process.exit(1);
  }
}

// ============================================================
// COMMANDS
// ============================================================

function cmdUnits(s: Stats, flags: Flags) {
  const ids = Object.keys(s.actors).filter(id => isCombatant(s, s.actors[id])).sort();
  if (flags.format === 'json') {
    const out: Record<string, Actor> = {};
    for (const id of ids) out[id] = s.actors[id];
    console.log(JSON.stringify(out, null, 2));
    return;
  }

  console.log(`\n${ids.length} combatant actors (live YAML):\n`);
  console.log(`  ${col('id', 28)} ${col('name', 24)} ${rcol('cost', 6)} ${rcol('hp', 8)} ${col('armor', 14)} ${rcol('spd', 4)} ${col('primary weapon', 22)}`);
  console.log(`  ${'─'.repeat(110)}`);
  for (const id of ids) {
    const a = s.actors[id];
    const armorStr = a.armor ? `${a.armor.type}/${a.armor.thickness}` : '';
    const weapon = a.armaments[0]?.weapon ?? '';
    console.log(
      `  ${col(id, 28)} ${col(a.name ?? '', 24)} ${rcol(num(a.cost), 6)} ${rcol(num(a.hp), 8)} ${col(armorStr, 14)} ${rcol(num(a.speed), 4)} ${col(weapon, 22)}`
    );
  }
  console.log('');
}

function cmdWeapons(s: Stats, flags: Flags) {
  const ids = Object.keys(s.weapons)
    .filter(k => !k.startsWith('^'))
    .filter(k => s.weapons[k].warheads.some(w => w.damage > 0))
    .sort();
  if (flags.format === 'json') {
    const out: Record<string, Weapon> = {};
    for (const id of ids) out[id] = s.weapons[id];
    console.log(JSON.stringify(out, null, 2));
    return;
  }

  console.log(`\n${ids.length} combat weapons (live YAML):\n`);
  console.log(`  ${col('id', 28)} ${rcol('dmg', 7)} ${rcol('pen', 6)} ${rcol('range', 8)} ${rcol('burst', 5)} ${rcol('bwait', 5)} ${rcol('mag', 5)} ${col('proj', 10)} ${col('flags', 12)}`);
  console.log(`  ${'─'.repeat(105)}`);
  for (const id of ids) {
    const w = s.weapons[id];
    const main = pickMainWarhead(w);
    const flags = [w.top_attack ? 'top' : '', w.bottom_attack ? 'bot' : ''].filter(Boolean).join(',');
    console.log(
      `  ${col(id, 28)} ${rcol(num(main?.damage), 7)} ${rcol(num(main?.penetration), 6)} ${rcol(formatWDist(w.range), 8)} ${rcol(String(w.burst), 5)} ${rcol(String(w.burst_wait), 5)} ${rcol(String(w.magazine), 5)} ${col(w.projectile_kind ?? '', 10)} ${col(flags, 12)}`
    );
  }
  console.log('');
}

function cmdActor(s: Stats, flags: Flags) {
  const id = flags.positional[0];
  if (!id) { console.error('actor: specify <id>'); process.exit(1); }
  const a = s.actors[id];
  if (!a) { console.error(`unknown actor "${id}"`); process.exit(1); }
  if (flags.format === 'json') { console.log(JSON.stringify(a, null, 2)); return; }

  console.log(`\n  ${a.name ?? id} (${id})`);
  console.log(`  ${'─'.repeat(48)}`);
  console.log(`  Cost:     ${a.cost ?? '—'}`);
  console.log(`  HP:       ${a.hp ?? '—'}`);
  if (a.armor) {
    console.log(`  Armor:    ${a.armor.type} (Thickness ${a.armor.thickness})`);
    if (a.armor.distribution.length === 5) {
      const [f, r, b, l, t] = a.armor.distribution;
      console.log(`  Direct'l: F ${f}% / R ${r}% / B ${b}% / L ${l}% / T ${t}%`);
    }
  }
  console.log(`  Speed:    ${a.speed ?? '—'} (${a.mover ?? 'static'})`);
  if (a.cargo_max_weight != null) console.log(`  Cargo:    MaxWeight ${a.cargo_max_weight}`);
  console.log(`  Disabled: ${a.disabled ? 'YES (~disabled prerequisite)' : 'no'}`);
  console.log('');
  for (const arm of a.armaments) {
    const w = getWeapon(s, arm.weapon);
    console.log(`  Armament: ${arm.name}  →  ${arm.weapon}${w ? '' : '  (UNKNOWN WEAPON)'}`);
    if (!w) continue;
    const main = pickMainWarhead(w);
    console.log(`    Range:     ${formatWDist(w.range)} (${wdistToCells(w.range).toFixed(1)} cells)`);
    if (w.min_range > 0) console.log(`    Min range: ${formatWDist(w.min_range)}`);
    console.log(`    Damage:    ${main?.damage ?? '—'}  Pen:${main?.penetration ?? '—'}` + (w.top_attack ? '  TopAttack' : ''));
    console.log(`    Burst:     ${w.burst} × ${main?.damage ?? 0}  delays=[${w.burst_delays.join(',')}]`);
    console.log(`    BurstWait: ${w.burst_wait}t   Mag: ${w.magazine}   Reload: ${w.reload_delay}t   Aim: ${arm.aiming_delay}t`);
    if (w.inaccuracy != null) console.log(`    Accuracy:  ${formatWDist(w.inaccuracy)} (${w.inaccuracy_type})`);
    if (main && (w.warheads.length > 1 || (main.spread ?? 0) > 0)) {
      const spread = w.warheads.find(wh => wh.spread != null && wh.spread > 0);
      if (spread) console.log(`    AoE:       Spread ${formatWDist(spread.spread!)}, Damage ${spread.damage}, Pen ${spread.penetration}`);
    }
    console.log('');
  }
}

function cmdWeapon(s: Stats, flags: Flags) {
  const id = flags.positional[0];
  if (!id) { console.error('weapon: specify <id>'); process.exit(1); }
  const w = getWeapon(s, id);
  if (!w) { console.error(`unknown weapon "${id}"`); process.exit(1); }
  if (flags.format === 'json') { console.log(JSON.stringify(w, null, 2)); return; }

  console.log(`\n  ${id}`);
  console.log(`  ${'─'.repeat(48)}`);
  console.log(`  Range:        ${formatWDist(w.range)} (${wdistToCells(w.range).toFixed(1)} cells)   Min: ${formatWDist(w.min_range)}`);
  console.log(`  Burst:        ${w.burst} round(s), delays=[${w.burst_delays.join(',')}], BurstWait ${w.burst_wait}t`);
  console.log(`  Magazine:     ${w.magazine} round(s), ReloadDelay ${w.reload_delay}t`);
  console.log(`  Projectile:   ${w.projectile_kind ?? '—'}` + (w.projectile_speed != null ? `  speed ${w.projectile_speed}` : ''));
  console.log(`  Inaccuracy:   ${w.inaccuracy != null ? formatWDist(w.inaccuracy) : '—'} (${w.inaccuracy_type ?? '—'})`);
  console.log(`  TopAttack:    ${w.top_attack ? 'yes' : 'no'}`);
  console.log(`  Targets:      ${w.valid_targets.join(', ')}` + (w.invalid_targets.length ? `   not: ${w.invalid_targets.join(', ')}` : ''));
  console.log('');
  for (const wh of w.warheads) {
    console.log(`  Warhead: ${wh.type}`);
    console.log(`    Damage ${wh.damage} / Pen ${wh.penetration}` + (wh.spread != null ? `   Spread ${formatWDist(wh.spread)}` : ''));
    if (wh.falloff && wh.falloff.length) console.log(`    Falloff [${wh.falloff.join(', ')}]`);
    if (Object.keys(wh.versus).length) console.log(`    Versus ${JSON.stringify(wh.versus)}`);
    if (wh.valid_targets.length) console.log(`    Targets ${wh.valid_targets.join(', ')}`);
    console.log('');
  }
}

function cmdCompare(s: Stats, flags: Flags) {
  const [aId, bId] = flags.positional;
  if (!aId || !bId) { console.error('compare: specify <a> <b>'); process.exit(1); }
  const a = s.actors[aId], b = s.actors[bId];
  if (!a || !b) { console.error('one or both actors not found'); process.exit(1); }

  const aw = a.armaments[0] ? getWeapon(s, a.armaments[0].weapon) : undefined;
  const bw = b.armaments[0] ? getWeapon(s, b.armaments[0].weapon) : undefined;
  const aMain = aw ? pickMainWarhead(aw) : undefined;
  const bMain = bw ? pickMainWarhead(bw) : undefined;

  const rows: [string, string, string][] = [
    ['name', a.name ?? aId, b.name ?? bId],
    ['cost', num(a.cost), num(b.cost)],
    ['hp', num(a.hp), num(b.hp)],
    ['hp/1000cr', a.cost && a.hp ? Math.round(a.hp / a.cost * 1000).toString() : '—',
                 b.cost && b.hp ? Math.round(b.hp / b.cost * 1000).toString() : '—'],
    ['armor', a.armor ? `${a.armor.type}/${a.armor.thickness}` : '—',
              b.armor ? `${b.armor.type}/${b.armor.thickness}` : '—'],
    ['armor F/T', a.armor && a.armor.distribution.length === 5 ? `${a.armor.distribution[0]}/${a.armor.distribution[4]}` : '—',
                  b.armor && b.armor.distribution.length === 5 ? `${b.armor.distribution[0]}/${b.armor.distribution[4]}` : '—'],
    ['speed', num(a.speed), num(b.speed)],
    ['cargo', num(a.cargo_max_weight), num(b.cargo_max_weight)],
    ['weapon', a.armaments[0]?.weapon ?? '—', b.armaments[0]?.weapon ?? '—'],
    ['  damage', num(aMain?.damage), num(bMain?.damage)],
    ['  pen', num(aMain?.penetration), num(bMain?.penetration)],
    ['  range', aw ? formatWDist(aw.range) : '—', bw ? formatWDist(bw.range) : '—'],
    ['  burst', aw ? `${aw.burst}` : '—', bw ? `${bw.burst}` : '—'],
    ['  burstwait', aw ? `${aw.burst_wait}` : '—', bw ? `${bw.burst_wait}` : '—'],
    ['  magazine', aw ? `${aw.magazine}` : '—', bw ? `${bw.magazine}` : '—'],
    ['  topattack', aw ? `${aw.top_attack}` : '—', bw ? `${bw.top_attack}` : '—'],
    ['  dps/sec', aw && aMain ? sustainedDpsPerSec(aw, aMain).toFixed(0) : '—',
                  bw && bMain ? sustainedDpsPerSec(bw, bMain).toFixed(0) : '—'],
    ['  dmg/credit', a.cost && aw && aMain ? (sustainedDpsPerSec(aw, aMain) / a.cost * 1000).toFixed(1) : '—',
                     b.cost && bw && bMain ? (sustainedDpsPerSec(bw, bMain) / b.cost * 1000).toFixed(1) : '—'],
  ];

  if (flags.format === 'json') {
    const out: Record<string, [string, string]> = {};
    for (const [k, av, bv] of rows) out[k] = [av, bv];
    console.log(JSON.stringify(out, null, 2));
    return;
  }

  console.log(`\n  ${col('field', 14)} ${col(aId, 28)} ${col(bId, 28)}`);
  console.log(`  ${'─'.repeat(74)}`);
  for (const [k, av, bv] of rows) {
    console.log(`  ${col(k, 14)} ${col(av, 28)} ${col(bv, 28)}`);
  }
  console.log('');
}

function cmdDps(s: Stats, flags: Flags) {
  const id = flags.positional[0];
  if (!id) { console.error('dps: specify <id>'); process.exit(1); }
  const a = s.actors[id];
  if (!a) { console.error(`unknown actor "${id}"`); process.exit(1); }
  if (flags.format === 'json') {
    console.log(JSON.stringify(armamentDps(s, a), null, 2));
    return;
  }
  console.log(`\n  ${a.name ?? id} (${id})  cost ${a.cost}\n`);
  for (const e of armamentDps(s, a)) {
    console.log(`  ${e.armament}  →  ${e.weapon}`);
    console.log(`    sustained: ${e.dpsPerSec.toFixed(1)} dmg/sec   per-credit: ${e.dpsPerCredit.toFixed(2)} (per 1000)`);
    console.log(`    burst:     ${e.burstDamage} over ${e.burstSec.toFixed(2)}s, then ${e.cooldownSec.toFixed(2)}s wait`);
    console.log('');
  }
}

function cmdTierCost(s: Stats, flags: Flags) {
  const ids = Object.keys(s.actors).filter(id => isCombatant(s, s.actors[id]));
  type Row = { id: string; name: string; cost: number; hp: number | null; armor: string; dps: number; dpsPerCr: number; hpPerCr: number };
  const rows: Row[] = [];
  for (const id of ids) {
    const a = s.actors[id];
    if (!a.cost) continue;
    const arm = a.armaments[0];
    const w = arm ? getWeapon(s, arm.weapon) : undefined;
    const main = w ? pickMainWarhead(w) : undefined;
    const dps = w && main ? sustainedDpsPerSec(w, main) : 0;
    rows.push({
      id,
      name: a.name ?? id,
      cost: a.cost,
      hp: a.hp,
      armor: a.armor ? `${a.armor.type}/${a.armor.thickness}` : '—',
      dps,
      dpsPerCr: dps / a.cost * 1000,
      hpPerCr: a.hp ? a.hp / a.cost * 1000 : 0,
    });
  }
  rows.sort((x, y) => x.cost - y.cost);
  if (flags.format === 'json') { console.log(JSON.stringify(rows, null, 2)); return; }

  console.log(`\n  Tier × cost dashboard (sustained DPS, primary armament only):\n`);
  console.log(`  ${col('id', 28)} ${col('name', 24)} ${rcol('cost', 6)} ${rcol('hp', 8)} ${col('armor', 14)} ${rcol('dps/s', 8)} ${rcol('hp/1k', 8)} ${rcol('dps/1k', 8)}`);
  console.log(`  ${'─'.repeat(110)}`);
  for (const r of rows) {
    console.log(
      `  ${col(r.id, 28)} ${col(r.name, 24)} ${rcol(String(r.cost), 6)} ${rcol(num(r.hp), 8)} ${col(r.armor, 14)} ${rcol(r.dps.toFixed(0), 8)} ${rcol(r.hpPerCr.toFixed(0), 8)} ${rcol(r.dpsPerCr.toFixed(1), 8)}`
    );
  }
  console.log('');
}

// ============================================================
// helpers
// ============================================================

const TICKS_PER_SEC = 25;

function pickMainWarhead(w: Weapon) {
  // The "main" warhead is the highest-damage one (TargetDamage usually
  // beats SpreadDamage / suppression warheads in damage value).
  return w.warheads
    .filter(wh => wh.damage > 0)
    .sort((a, b) => b.damage - a.damage)[0];
}

function sustainedDpsPerSec(w: Weapon, main: { damage: number }): number {
  // Per-cycle damage / per-cycle ticks. A cycle = one Burst, with
  // BurstWait between cycles. Cycle length = (burst-1)*avgDelay + BurstWait.
  if (w.burst < 1 || main.damage <= 0) return 0;
  const avgDelay = w.burst_delays.length > 0
    ? w.burst_delays.reduce((a, b) => a + b, 0) / w.burst_delays.length
    : 5;
  const cycleTicks = Math.max(1, (w.burst - 1) * avgDelay + w.burst_wait);
  return (w.burst * main.damage) / cycleTicks * TICKS_PER_SEC;
}

interface DpsEntry {
  armament: string;
  weapon: string;
  dpsPerSec: number;
  dpsPerCredit: number;
  burstDamage: number;
  burstSec: number;
  cooldownSec: number;
}

function armamentDps(s: Stats, a: Actor): DpsEntry[] {
  const out: DpsEntry[] = [];
  for (const arm of a.armaments) {
    const w = getWeapon(s, arm.weapon);
    if (!w) continue;
    const main = pickMainWarhead(w);
    if (!main) continue;
    const avgDelay = w.burst_delays.length > 0
      ? w.burst_delays.reduce((x, y) => x + y, 0) / w.burst_delays.length
      : 5;
    const burstTicks = (w.burst - 1) * avgDelay;
    const dps = sustainedDpsPerSec(w, main);
    out.push({
      armament: arm.name,
      weapon: arm.weapon,
      dpsPerSec: dps,
      dpsPerCredit: a.cost ? dps / a.cost * 1000 : 0,
      burstDamage: w.burst * main.damage,
      burstSec: burstTicks / TICKS_PER_SEC,
      cooldownSec: w.burst_wait / TICKS_PER_SEC,
    });
  }
  return out;
}

function num(v: number | null | undefined): string {
  if (v == null) return '—';
  return v.toLocaleString('en-US');
}

function col(s: string, n: number): string {
  if (s.length > n) return s.slice(0, n - 1) + '…';
  return s.padEnd(n);
}

function rcol(s: string, n: number): string {
  if (s.length > n) return s.slice(0, n - 1) + '…';
  return s.padStart(n);
}

main();
