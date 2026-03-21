/**
 * WDist (World Distance) utilities for OpenRA.
 * 1 cell = 1024 WDist units.
 * Notation: "NcXXX" where N = cells, XXX = fractional WDist units.
 * Examples: "1c0" = 1024, "3c768" = 3840, "0c512" = 512, "16384" = 16384
 */

export const CELL_SIZE = 1024;

/** Parse WDist string to integer WDist units */
export function parseWDist(s: string): number {
  s = s.trim();
  const cellMatch = s.match(/^(-?\d+)c(\d+)$/);
  if (cellMatch) {
    const cells = parseInt(cellMatch[1], 10);
    const frac = parseInt(cellMatch[2], 10);
    return cells * CELL_SIZE + frac;
  }
  const num = parseInt(s, 10);
  if (isNaN(num)) throw new Error(`Invalid WDist: "${s}"`);
  return num;
}

/** Convert WDist units to cells (float) */
export function wdistToCells(wd: number): number {
  return wd / CELL_SIZE;
}

/** Convert cells to WDist units */
export function cellsToWDist(cells: number): number {
  return Math.round(cells * CELL_SIZE);
}

/** Format WDist as string */
export function formatWDist(wd: number): string {
  const cells = Math.floor(wd / CELL_SIZE);
  const frac = wd % CELL_SIZE;
  if (cells === 0 && frac === 0) return '0';
  if (frac === 0) return `${cells}c0`;
  return `${cells}c${frac}`;
}

/** 2D position in WDist units */
export interface WPos {
  x: number;
  y: number;
}

/** Distance between two positions */
export function wposDistance(a: WPos, b: WPos): number {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  return Math.sqrt(dx * dx + dy * dy);
}
