#!/usr/bin/env node
/**
 * WW3MOD Combat Balance Simulator
 *
 * Usage:
 *   npx combat-sim run <scenario>          Run a scenario
 *   npx combat-sim list                    List available scenarios
 *   npx combat-sim units                   List available units
 *   npx combat-sim stats <unitId>          Show unit stats
 *   npx combat-sim duel <unit1> <unit2>    Quick 1v1 matchup
 *
 * Options:
 *   --format table|json     Output format (default: table)
 *   --runs N                Number of simulation runs (default: from scenario)
 *   --ticks N               Max ticks per run (default: from scenario)
 *   --range NcXXX           Engagement range for duel (default: 10c0)
 *   --events                Include per-tick event log in output
 */

import { SimUnit, resetUnitIds } from './model/unit.js';
import { parseWDist, formatWDist, wdistToCells } from './model/wdist.js';
import { generateFormation } from './sim/formations.js';
import { runSimulation, runMultiple, compileRunResult, type SimulationSetup } from './sim/engine.js';
import { formatTable, formatJson } from './output/formatter.js';
import { UNITS, SCENARIOS, listUnits, listScenarios, getUnit, getScenario } from './scenarios/library.js';
import type { Scenario, UnitDef, FormationPreset } from './model/types.js';

function main() {
  const args = process.argv.slice(2);
  if (args.length === 0) {
    printUsage();
    return;
  }

  const command = args[0];
  const flags = parseFlags(args.slice(1));

  switch (command) {
    case 'run':
      cmdRun(flags);
      break;
    case 'list':
      cmdList();
      break;
    case 'units':
      cmdUnits();
      break;
    case 'stats':
      cmdStats(flags);
      break;
    case 'duel':
      cmdDuel(flags);
      break;
    default:
      // Try as scenario name
      cmdRun({ ...flags, positional: [command, ...flags.positional] });
      break;
  }
}

interface Flags {
  positional: string[];
  format: string;
  runs: number | null;
  ticks: number | null;
  range: string;
  events: boolean;
}

function parseFlags(args: string[]): Flags {
  const flags: Flags = {
    positional: [],
    format: 'table',
    runs: null,
    ticks: null,
    range: '10c0',
    events: false,
  };

  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === '--format' && i + 1 < args.length) {
      flags.format = args[++i];
    } else if (arg === '--runs' && i + 1 < args.length) {
      flags.runs = parseInt(args[++i], 10);
    } else if (arg === '--ticks' && i + 1 < args.length) {
      flags.ticks = parseInt(args[++i], 10);
    } else if (arg === '--range' && i + 1 < args.length) {
      flags.range = args[++i];
    } else if (arg === '--events') {
      flags.events = true;
    } else if (!arg.startsWith('--')) {
      flags.positional.push(arg);
    }
  }

  return flags;
}

function printUsage() {
  console.log(`
WW3MOD Combat Balance Simulator
================================

Commands:
  run <scenario>          Run a named scenario
  list                    List available scenarios
  units                   List available unit types
  stats <unitId>          Show detailed unit stats
  duel <unit1> <unit2>    Quick 1v1 matchup

Options:
  --format table|json     Output format (default: table)
  --runs N                Number of simulation runs
  --ticks N               Max ticks per run
  --range NcXXX           Engagement range for duel (default: 10c0)
  --events                Include per-tick event log

Examples:
  node build/index.js run tank-duel
  node build/index.js duel abrams t90 --range 18c0 --runs 100
  node build/index.js run infantry-mirror --format json
  node build/index.js run formation-aoe
`);
}

// ============================================================
// COMMANDS
// ============================================================

function cmdRun(flags: Flags) {
  const scenarioId = flags.positional[0];
  if (!scenarioId) {
    console.error('Error: specify a scenario name. Use "list" to see available scenarios.');
    process.exit(1);
  }

  const scenario = getScenario(scenarioId);
  if (!scenario) {
    console.error(`Error: unknown scenario "${scenarioId}". Available: ${listScenarios().join(', ')}`);
    process.exit(1);
  }

  const runs = flags.runs ?? scenario.runs;
  const maxTicks = flags.ticks ?? scenario.maxTicks;

  const createSetup = (): SimulationSetup => {
    resetUnitIds();
    const teams = buildTeams(scenario);
    return {
      teams,
      maxTicks,
      scenarioName: scenario.name,
      recordEvents: flags.events,
    };
  };

  const result = runs > 1
    ? runMultiple(createSetup, runs)
    : compileRunResult(runSimulation(createSetup()), scenario.name);

  result.teams[0].name = scenario.teams[0].name;
  result.teams[1].name = scenario.teams[1].name;

  if (flags.format === 'json') {
    console.log(formatJson(result));
  } else {
    console.log(formatTable(result));
  }
}

function cmdList() {
  console.log('\nAvailable Scenarios:');
  console.log('─'.repeat(50));
  for (const [id, scenario] of Object.entries(SCENARIOS)) {
    console.log(`  ${id.padEnd(25)} ${scenario.description}`);
  }
  console.log('');
}

function cmdUnits() {
  console.log('\nAvailable Units:');
  console.log('─'.repeat(70));
  console.log(`  ${'ID'.padEnd(20)} ${'Name'.padEnd(22)} ${'Cost'.padStart(6)} ${'HP'.padStart(8)} ${'Armor'.padEnd(10)}`);
  console.log(`  ${'─'.repeat(66)}`);
  for (const [id, unit] of Object.entries(UNITS)) {
    const armorStr = unit.armorThickness > 0
      ? `${unit.armorType}/${unit.armorThickness}`
      : unit.armorType;
    console.log(
      `  ${id.padEnd(20)} ${unit.name.padEnd(22)} ${unit.cost.toString().padStart(6)} ${unit.hp.toString().padStart(8)} ${armorStr.padEnd(10)}`
    );
  }
  console.log('');
}

function cmdStats(flags: Flags) {
  const unitId = flags.positional[0];
  if (!unitId) {
    console.error('Error: specify a unit ID. Use "units" to see available units.');
    process.exit(1);
  }

  const unit = getUnit(unitId);
  if (!unit) {
    console.error(`Error: unknown unit "${unitId}". Available: ${listUnits().join(', ')}`);
    process.exit(1);
  }

  console.log(`\n  ${unit.name} (${unit.id})`);
  console.log(`  ${'─'.repeat(40)}`);
  console.log(`  Cost:     ${unit.cost}`);
  console.log(`  HP:       ${unit.hp}`);
  console.log(`  Armor:    ${unit.armorType}${unit.armorThickness > 0 ? ` (thickness: ${unit.armorThickness})` : ''}`);
  if (unit.armorThickness > 0) {
    const [f, r, b, l, t] = unit.armorDistribution;
    console.log(`  Armor Dir: Front ${f}% | Right ${r}% | Rear ${b}% | Left ${l}% | Top ${t}%`);
  }
  console.log(`  Infantry: ${unit.isInfantry ? 'yes' : 'no'}`);
  console.log(`  Targets:  ${unit.targetTypes.join(', ')}`);
  if (unit.turretTurnSpeed) {
    console.log(`  Turret:   turn speed ${unit.turretTurnSpeed}`);
  }
  console.log('');

  for (const arm of unit.armaments) {
    const w = arm.weapon;
    console.log(`  Weapon: ${w.id} (${arm.name})`);
    console.log(`    Range:     ${formatWDist(w.range)} (${wdistToCells(w.range).toFixed(1)} cells)`);
    if (w.minRange > 0) console.log(`    Min Range: ${formatWDist(w.minRange)}`);
    console.log(`    Damage:    ${w.warheads[0]?.damage ?? '?'} (pen: ${w.warheads[0]?.penetration ?? '?'})`);
    console.log(`    Burst:     ${w.burst} rounds, ${w.burstDelays}t delay`);
    console.log(`    BurstWait: ${w.burstWait}t`);
    console.log(`    Magazine:  ${w.magazine} rounds, ${w.reloadDelay}t reload`);
    console.log(`    Accuracy:  ${formatWDist(w.inaccuracy)} inaccuracy (${w.inaccuracyType})`);
    console.log(`    Targets:   ${w.validTargets.join(', ')}`);
    if (w.warheads[0]?.type === 'SpreadDamage') {
      console.log(`    AoE:       spread ${w.warheads[0].spread}, falloff [${w.warheads[0].falloff.join(', ')}]`);
    }
    console.log('');
  }
}

function cmdDuel(flags: Flags) {
  const unit1Id = flags.positional[0];
  const unit2Id = flags.positional[1];

  if (!unit1Id || !unit2Id) {
    console.error('Error: specify two unit IDs. Example: duel abrams t90');
    process.exit(1);
  }

  const unit1 = getUnit(unit1Id);
  const unit2 = getUnit(unit2Id);

  if (!unit1) {
    console.error(`Error: unknown unit "${unit1Id}". Available: ${listUnits().join(', ')}`);
    process.exit(1);
  }
  if (!unit2) {
    console.error(`Error: unknown unit "${unit2Id}". Available: ${listUnits().join(', ')}`);
    process.exit(1);
  }

  const range = parseWDist(flags.range);
  const runs = flags.runs ?? 50;
  const maxTicks = flags.ticks ?? 5000;

  const scenario: Scenario = {
    name: `${unit1.name} vs ${unit2.name} @ ${formatWDist(range)}`,
    description: `1v1 duel at ${wdistToCells(range).toFixed(1)} cell range`,
    maxTicks,
    runs,
    teams: [
      {
        name: unit1.name,
        units: [{ actorId: unit1Id, count: 1 }],
        formation: { type: 'cluster', spacing: 1024, origin: { x: 0, y: 0 }, facing: 0 },
      },
      {
        name: unit2.name,
        units: [{ actorId: unit2Id, count: 1 }],
        formation: { type: 'cluster', spacing: 1024, origin: { x: range, y: 0 }, facing: 180 },
      },
    ],
  };

  const createSetup = (): SimulationSetup => {
    resetUnitIds();
    const teams = buildTeams(scenario);
    return {
      teams,
      maxTicks,
      scenarioName: scenario.name,
      recordEvents: flags.events,
    };
  };

  const result = runs > 1
    ? runMultiple(createSetup, runs)
    : compileRunResult(runSimulation(createSetup()), scenario.name);

  result.teams[0].name = unit1.name;
  result.teams[1].name = unit2.name;

  if (flags.format === 'json') {
    console.log(formatJson(result));
  } else {
    console.log(formatTable(result));
  }
}

// ============================================================
// HELPERS
// ============================================================

/**
 * Build SimUnit arrays from a scenario definition.
 */
function buildTeams(scenario: Scenario): SimUnit[][] {
  return scenario.teams.map((teamDef, teamIdx) => {
    const units: SimUnit[] = [];

    // Collect all unit defs with counts
    const unitEntries: { def: UnitDef; count: number }[] = [];
    for (const su of teamDef.units) {
      const def = getUnit(su.actorId);
      if (!def) {
        console.error(`Warning: unknown unit "${su.actorId}", skipping`);
        continue;
      }
      unitEntries.push({ def, count: su.count });
    }

    const totalCount = unitEntries.reduce((s, e) => s + e.count, 0);

    // Generate positions from formation or explicit
    let positions: { x: number; y: number }[];
    if (teamDef.formation) {
      positions = generateFormation(totalCount, teamDef.formation);
    } else {
      // Default: line formation
      const defaultFormation: FormationPreset = {
        type: 'line',
        spacing: 1024,
        origin: { x: teamIdx * 10240, y: 0 },
        facing: teamIdx === 0 ? 0 : 180,
      };
      positions = generateFormation(totalCount, defaultFormation);
    }

    // Create units
    let posIdx = 0;
    for (const entry of unitEntries) {
      for (let i = 0; i < entry.count; i++) {
        const pos = positions[posIdx] ?? { x: posIdx * 1024, y: 0 };
        const facing = teamDef.formation?.facing ?? (teamIdx === 0 ? 0 : 180);
        units.push(new SimUnit(entry.def, teamIdx, pos, facing));
        posIdx++;
      }
    }

    return units;
  });
}

main();
