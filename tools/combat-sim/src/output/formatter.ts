/**
 * Output formatter — produces both JSON and formatted table output.
 */

import type { SimulationResult } from '../model/types.js';

/**
 * Format simulation result as a human-readable table.
 */
export function formatTable(result: SimulationResult): string {
  const lines: string[] = [];
  const ticksPerSec = 25; // OpenRA runs at 25 ticks/sec

  lines.push('');
  lines.push(`${'='.repeat(60)}`);
  lines.push(`  ${result.scenario}`);
  lines.push(`  ${result.runs} run${result.runs > 1 ? 's' : ''}, ${result.ticks} ticks (${(result.ticks / ticksPerSec).toFixed(1)}s)`);
  lines.push(`${'='.repeat(60)}`);
  lines.push('');

  // Team summaries
  for (let i = 0; i < result.teams.length; i++) {
    const team = result.teams[i];
    const survRate = team.totalUnits > 0
      ? (team.avgSurvivors / team.totalUnits * 100).toFixed(0)
      : '0';

    lines.push(`  ${team.name}`);
    lines.push(`  ${'─'.repeat(40)}`);
    lines.push(`  Units:       ${team.avgSurvivors.toFixed(1)} / ${team.totalUnits} survived (${survRate}%)`);
    lines.push(`  Cost:        ${team.totalCost}`);
    lines.push(`  Dmg dealt:   ${Math.round(team.totalDamageDealt)}`);
    lines.push(`  Dmg taken:   ${Math.round(team.totalDamageReceived)}`);
    lines.push(`  Suppression: peak tier ${suppressionTier(team.avgSuppressionPeak)} (avg ${Math.round(team.avgSuppressionPeak)})`);
    lines.push('');

    // Per-unit breakdown
    lines.push(`  Unit Details:`);
    lines.push(`  ${'Actor'.padEnd(20)} ${'HP'.padStart(12)} ${'Dmg Dealt'.padStart(10)} ${'Rounds'.padStart(8)} ${'Death'.padStart(8)} ${'Supp'.padStart(6)}`);
    lines.push(`  ${'─'.repeat(66)}`);

    for (const unit of team.units) {
      const hpStr = unit.alive
        ? `${unit.hpRemaining}/${unit.hpMax}`
        : `DEAD`;
      const deathStr = unit.tickOfDeath !== null
        ? `${(unit.tickOfDeath / ticksPerSec).toFixed(1)}s`
        : '-';

      lines.push(
        `  ${unit.actorId.padEnd(20)} ${hpStr.padStart(12)} ${unit.damageDealt.toString().padStart(10)} ${unit.roundsFired.toString().padStart(8)} ${deathStr.padStart(8)} ${unit.peakSuppression.toString().padStart(6)}`
      );
    }
    lines.push('');
  }

  // Cost efficiency
  if (result.teams.length === 2) {
    const t0 = result.teams[0];
    const t1 = result.teams[1];
    lines.push(`  Cost Efficiency`);
    lines.push(`  ${'─'.repeat(40)}`);

    const t0CostPerDmg = t0.totalDamageDealt > 0 ? (t0.totalCost / t0.totalDamageDealt).toFixed(2) : 'N/A';
    const t1CostPerDmg = t1.totalDamageDealt > 0 ? (t1.totalCost / t1.totalDamageDealt).toFixed(2) : 'N/A';

    lines.push(`  ${t0.name}: ${t0CostPerDmg} cost per damage dealt`);
    lines.push(`  ${t1.name}: ${t1CostPerDmg} cost per damage dealt`);

    // Win rate (for multi-run)
    if (result.runs > 1) {
      lines.push('');
      lines.push(`  Win rate: computed across ${result.runs} runs`);
    }
    lines.push('');
  }

  return lines.join('\n');
}

/**
 * Format simulation result as JSON.
 */
export function formatJson(result: SimulationResult): string {
  return JSON.stringify(result, null, 2);
}

function suppressionTier(level: number): number {
  if (level <= 0) return 0;
  return Math.min(10, Math.ceil(level / 10));
}
