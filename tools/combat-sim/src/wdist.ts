/**
 * WDist (World Distance) helpers. 1 cell = 1024 WDist units.
 * Notation "NcXXX": cells + fractional WDist (e.g. "3c768" = 3 cells + 768 = 3840).
 */

export const CELL_SIZE = 1024;

export function parseWDist(s: string): number {
  s = s.trim();
  const cellMatch = s.match(/^(-?\d+)c(\d+)$/);
  if (cellMatch) return parseInt(cellMatch[1], 10) * CELL_SIZE + parseInt(cellMatch[2], 10);
  const num = parseInt(s, 10);
  if (isNaN(num)) throw new Error(`Invalid WDist: "${s}"`);
  return num;
}

export function formatWDist(wd: number): string {
  if (wd === 0) return '0';
  const cells = Math.floor(wd / CELL_SIZE);
  const frac = wd % CELL_SIZE;
  return frac === 0 ? `${cells}c0` : `${cells}c${frac}`;
}

export function wdistToCells(wd: number): number {
  return wd / CELL_SIZE;
}
