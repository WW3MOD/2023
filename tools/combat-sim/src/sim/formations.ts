/**
 * Formation generators — produce arrays of WPos positions for unit groups.
 *
 * Formations:
 * - line: units side by side perpendicular to facing
 * - column: units in single file along facing direction
 * - wedge: V-shape (leader front, wings angled back)
 * - cluster: tight group (randomized within spacing radius)
 * - spread: like line but with 2x spacing
 */

import type { WPos } from '../model/wdist.js';
import type { FormationPreset } from '../model/types.js';

/** Convert degrees to radians */
function degToRad(deg: number): number {
  return deg * Math.PI / 180;
}

/**
 * Generate positions for a formation.
 * @param count Number of units
 * @param preset Formation configuration
 * @returns Array of WPos positions
 */
export function generateFormation(count: number, preset: FormationPreset): WPos[] {
  switch (preset.type) {
    case 'line':    return generateLine(count, preset);
    case 'column':  return generateColumn(count, preset);
    case 'wedge':   return generateWedge(count, preset);
    case 'cluster': return generateCluster(count, preset);
    case 'spread':  return generateSpread(count, preset);
  }
}

/**
 * Line formation: units side-by-side, perpendicular to facing.
 * Centered on origin.
 */
function generateLine(count: number, p: FormationPreset): WPos[] {
  const positions: WPos[] = [];
  const perpAngle = degToRad(p.facing + 90);
  const halfWidth = (count - 1) * p.spacing / 2;

  for (let i = 0; i < count; i++) {
    const offset = i * p.spacing - halfWidth;
    positions.push({
      x: Math.round(p.origin.x + Math.cos(perpAngle) * offset),
      y: Math.round(p.origin.y + Math.sin(perpAngle) * offset),
    });
  }
  return positions;
}

/**
 * Column formation: units in single file along facing direction.
 * First unit at front (origin), rest behind.
 */
function generateColumn(count: number, p: FormationPreset): WPos[] {
  const positions: WPos[] = [];
  const angle = degToRad(p.facing + 180); // Behind = opposite of facing

  for (let i = 0; i < count; i++) {
    positions.push({
      x: Math.round(p.origin.x + Math.cos(angle) * i * p.spacing),
      y: Math.round(p.origin.y + Math.sin(angle) * i * p.spacing),
    });
  }
  return positions;
}

/**
 * Wedge formation: V-shape. Leader at front, others alternate left/right behind.
 */
function generateWedge(count: number, p: FormationPreset): WPos[] {
  const positions: WPos[] = [];
  const backAngle = degToRad(p.facing + 180);
  const perpAngle = degToRad(p.facing + 90);

  // Leader at origin
  positions.push({ ...p.origin });

  for (let i = 1; i < count; i++) {
    const row = Math.ceil(i / 2);
    const side = i % 2 === 1 ? 1 : -1; // Alternate left/right

    const backOffset = row * p.spacing * 0.7;
    const sideOffset = row * p.spacing * 0.7 * side;

    positions.push({
      x: Math.round(p.origin.x + Math.cos(backAngle) * backOffset + Math.cos(perpAngle) * sideOffset),
      y: Math.round(p.origin.y + Math.sin(backAngle) * backOffset + Math.sin(perpAngle) * sideOffset),
    });
  }
  return positions;
}

/**
 * Cluster formation: tight group, positions randomized within spacing radius.
 * Uses deterministic pseudo-random for reproducibility.
 */
function generateCluster(count: number, p: FormationPreset): WPos[] {
  const positions: WPos[] = [];
  // Use a simple deterministic spread pattern
  const goldenAngle = 2.399963; // radians

  for (let i = 0; i < count; i++) {
    const radius = p.spacing * Math.sqrt(i / count) * 0.8;
    const angle = i * goldenAngle;

    positions.push({
      x: Math.round(p.origin.x + Math.cos(angle) * radius),
      y: Math.round(p.origin.y + Math.sin(angle) * radius),
    });
  }
  return positions;
}

/**
 * Spread formation: like line but with 2x spacing for dispersal.
 */
function generateSpread(count: number, p: FormationPreset): WPos[] {
  return generateLine(count, { ...p, spacing: p.spacing * 2 });
}
