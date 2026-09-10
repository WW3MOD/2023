#!/usr/bin/env python3
"""defcon-wall-audit -- does the DEFCON 3 dividing wall actually divide the map?

WHY THIS EXISTS AND WHY `make nav-guard` CANNOT ANSWER IT. The wall is not an authored
blocking actor and not a terrain edit: DefconWall.RaiseWall writes Map.CustomTerrain at
runtime, on the tick the level reaches 3. nav-guard is a static decode of map.bin plus the
map.yaml Actors: block and contains no CustomTerrain handling at all (grep: zero hits), so
its baseline is byte-identical green whether this feature is on or off. A green nav-guard
run is real evidence that this change did not disturb the AUTHORED maps -- and says nothing
whatever about the wall.

So the wall needs its own check, and the failure to look for is NOT a shrink. The wall is
SUPPOSED to cut the map roughly in half; "the largest component got smaller" fires by
design and means nothing. Two things actually matter, and this reports both:

  1. SEPARATION. No connected component may contain cells from both half-planes. If one
     does, the wall leaks and a ground unit walks straight through it -- which is the
     readout's claim ("The border is closed. Neither side may cross it.") being false
     again, in a new way.

     THE MEASURED RESULT, and the reason the shipped default is wrong: at HalfWidth 512
     (a one-cell band) EVERY derived line on EVERY shipped map leaks on EVERY ground
     locomotor. A one-cell band only seals an AXIS-ALIGNED line. On a diagonal the band
     degenerates to a staircase of corner-touching cells, and an 8-connected step goes
     straight between two of them. The one worked example (test-defcon-wall) authors a
     VERTICAL line, which is the single case where 512 works -- so nothing caught it.

     The bound is analytic, not a tuning guess. Two 8-adjacent cells differ by at most
     (+/-1, +/-1) cells, so their perpendicular distances to the line differ by at most
     sqrt(2) cells. If they straddle the line, dist(P) + dist(Q) <= sqrt(2), hence
     min(dist) <= sqrt(2)/2 = 0.707 cells = 724 world units. A HalfWidth of 724 or more
     therefore forces at least one of any straddling pair into the band, at EVERY angle.
     1024 is the shipped choice: it clears the bound with margin and is one whole cell.

  2. NO NEW POCKETS. Each side's own half should stay in one piece. A derived line that
     grazes a chokepoint can sever a lobe from a player's own territory -- the nav-guard
     failure shape, reappearing inside a half where the gate cannot see it. Reported as
     `pocketed` per side, against the same map with no wall.

THE ARITHMETIC IS MIRRORED FROM C#, NOT REIMPLEMENTED. Every operation below matches
DefconWallGeometry: integer square root by Newton, cross product for the side, truncating
division for the distance, and C#'s truncate-toward-zero integer division (Python's // is
FLOOR, which differs on negatives -- see _trunc_div, and the perpendicular components here
are routinely negative).

Usage:
    python defcon_wall_audit.py                     # shipped default, extreme spawn pair
    python defcon_wall_audit.py --half-width 512    # reproduce the leak
    python defcon_wall_audit.py --all-pairs         # every pair a 1v1 could produce
"""

from __future__ import annotations

import argparse
import itertools
import sys
from collections import deque
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import modload  # noqa: E402
import nav_guard  # noqa: E402

CELL = 1024
HALF_CELL = 512

# The analytic floor derived in the module docstring: sqrt(2)/2 cells, in world units.
SAFE_HALF_WIDTH = 724


# ------------------------------------------------------------------ C#-faithful integer math

def isqrt(value: int) -> int:
    """DefconWallGeometry.ISqrt -- Newton, integer, deterministic."""
    if value <= 0:
        return 0
    x = value
    y = (x + 1) // 2
    while y < x:
        x = y
        y = (x + value // x) // 2
    return x


def _trunc_div(a: int, b: int) -> int:
    """C# integer division: truncates TOWARD ZERO. Python's // floors."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def perpendicular_bisector(a: tuple[int, int], b: tuple[int, int],
                           extend_cells: int) -> tuple[tuple[int, int], tuple[int, int]]:
    """DefconWallGeometry.PerpendicularBisector, in cells."""
    mid_x = _trunc_div(a[0] + b[0], 2)
    mid_y = _trunc_div(a[1] + b[1], 2)

    px = -(b[1] - a[1])
    py = b[0] - a[0]

    scale = isqrt(px * px + py * py)
    if scale == 0:
        return (mid_x, mid_y), (mid_x, mid_y)

    ex = _trunc_div(px * extend_cells, scale)
    ey = _trunc_div(py * extend_cells, scale)
    return (mid_x - ex, mid_y - ey), (mid_x + ex, mid_y + ey)


class Geometry:
    """DefconWallGeometry, built the way DefconWall builds it: from CELL CENTRES."""

    def __init__(self, start_cell, end_cell, half_width: int):
        self.ax = CELL * start_cell[0] + HALF_CELL
        self.ay = CELL * start_cell[1] + HALF_CELL
        ex = CELL * end_cell[0] + HALF_CELL
        ey = CELL * end_cell[1] + HALF_CELL
        self.dx = ex - self.ax
        self.dy = ey - self.ay
        self.half_width = max(0, half_width)
        self.length = isqrt(self.dx * self.dx + self.dy * self.dy)

    @property
    def degenerate(self) -> bool:
        return self.length == 0

    def signed_cross(self, px: int, py: int) -> int:
        return self.dx * (py - self.ay) - self.dy * (px - self.ax)

    def side_of(self, px: int, py: int) -> int:
        c = self.signed_cross(px, py)
        return 1 if c > 0 else (-1 if c < 0 else 0)

    def distance_to_line(self, px: int, py: int) -> int:
        if self.length == 0:
            return 0
        return abs(self.signed_cross(px, py)) // self.length

    def in_band(self, px: int, py: int) -> bool:
        return not self.degenerate and self.distance_to_line(px, py) <= self.half_width

    def side_of_cell(self, cx: int, cy: int) -> int:
        return self.side_of(CELL * cx + HALF_CELL, CELL * cy + HALF_CELL)

    def in_band_cell(self, cx: int, cy: int) -> bool:
        return self.in_band(CELL * cx + HALF_CELL, CELL * cy + HALF_CELL)

    def distance_cells(self, cx: int, cy: int) -> float:
        return self.distance_to_line(CELL * cx + HALF_CELL, CELL * cy + HALF_CELL) / CELL

    def angle_note(self) -> str:
        """Axis-aligned lines are the ONLY ones a one-cell band seals; flag them."""
        if self.dx == 0 or self.dy == 0:
            return "axis-aligned"
        return "diagonal"


# ---------------------------------------------------------------------------- the audit

def spawns_of(game_map) -> list[tuple[int, int]]:
    return sorted(a.location for a in game_map.actors if a.name == "mpspawn")


def _walk(model, passable, variant, visit):
    """8-connected flood over `passable`, honouring the diagonal-squeeze rule."""
    width, height = model.width, model.height
    seen = bytearray(width * height)
    out = []
    for start in range(width * height):
        if not passable[start] or seen[start]:
            continue
        seen[start] = 1
        queue = deque([start])
        cells = []
        while queue:
            cur = queue.popleft()
            cells.append(cur)
            cy_, cx_ = divmod(cur, width)
            for dx, dy in nav_guard.NEIGHBOURS:
                nx, ny = cx_ + dx, cy_ + dy
                if not (0 <= nx < width and 0 <= ny < height):
                    continue
                n = ny * width + nx
                if seen[n] or not passable[n]:
                    continue
                if dx and dy and nav_guard.squeeze_blocks(
                        model, variant, model.left + cx_, model.top + cy_,
                        model.left + nx, model.top + ny):
                    continue
                seen[n] = 1
                queue.append(n)
        out.append(visit(cells))
    return out


def audit_line(model, geometry, variant: str):
    width = model.width
    base_comps = _walk(model, model.passable, variant, list)
    base_comps.sort(key=len, reverse=True)
    base_sizes = [len(c) for c in base_comps]
    # The cells that were mutually reachable BEFORE the wall. Anything here that survives the
    # wall but ends up outside its own side's main body is a region the wall sealed off --
    # which is the nav-guard failure shape, occurring inside a half where the gate is blind.
    base_main = set(base_comps[0]) if base_comps else set()

    passable = bytearray(model.passable)
    band = 0
    for y in range(model.height):
        for x in range(width):
            i = y * width + x
            if passable[i] and geometry.in_band_cell(model.left + x, model.top + y):
                passable[i] = 0
                band += 1

    def classify(cells):
        sides = set()
        for c in cells:
            cy_, cx_ = divmod(c, width)
            sides.add(geometry.side_of_cell(model.left + cx_, model.top + cy_))
        return cells, sides - {0}

    comps = _walk(model, passable, variant, classify)
    leaks = [len(cells) for cells, sides in comps if len(sides) > 1]

    per_side = {}
    kept_main = set()
    for side in (1, -1):
        mine = sorted((cells for cells, sides in comps if sides == {side}),
                      key=len, reverse=True)
        largest = len(mine[0]) if mine else 0
        total = sum(len(c) for c in mine)
        per_side[side] = (largest, total - largest, len(mine))
        if mine:
            kept_main |= set(mine[0])

    # Precise: was mutually reachable, is still passable, but is no longer in either side's
    # main body. Band cells are excluded -- being inside the wall is the wall working.
    sealed_cells = [c for c in base_main if passable[c] and c not in kept_main]

    base_largest = base_sizes[0] if base_sizes else 0
    return {
        "band": band,
        "leaks": leaks,
        "per_side": per_side,
        "base_largest": base_largest,
        "base_comps": len(base_sizes),
        # Pockets the MAP already had. Anything at or below this is not the wall's doing --
        # every shipped map carries pre-existing pocketed cells (nav-guard baseline), so a
        # raw pocket count here would flag ten maps and mean nothing.
        "base_pocketed": sum(base_sizes) - base_largest,
        "newly_isolated": len(sealed_cells),
        "sealed_cells": sealed_cells,
    }


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--half-width", type=int, default=1024,
                    help="DefconWallInfo.HalfWidth in world units (512 = one-cell band)")
    ap.add_argument("--extend", type=int, default=512, help="extendCells for the bisector")
    ap.add_argument("--map", action="append", default=None)
    ap.add_argument("--locomotor", action="append", default=None)
    ap.add_argument("--squeeze", default=nav_guard.DEFAULT_SQUEEZE)
    ap.add_argument("--all-pairs", action="store_true",
                    help="test every spawn pair, not just the farthest-apart pair")
    ap.add_argument("--quiet", action="store_true", help="only print lines and failures")
    ap.add_argument("--show-sealed", action="store_true",
                    help="print the bounding box of any region the wall sealed off")
    args = ap.parse_args(argv)

    rules = modload.load_mod(nav_guard.MOD_DIR)
    maps = [modload.load_map(p) for p in modload.discover_maps(nav_guard.MOD_DIR)]
    if args.map:
        maps = [m for m in maps if any(f in m.name for f in args.map)]

    print(f"DEFCON wall audit  (HalfWidth {args.half_width}, extend {args.extend} cells, "
          f"squeeze {args.squeeze})")
    if args.half_width < SAFE_HALF_WIDTH:
        print(f"  ! HalfWidth is below the analytic floor of {SAFE_HALF_WIDTH} "
              f"(sqrt(2)/2 cells): expect diagonal lines to leak.")
    print("  SEPARATION is the test: a component spanning both half-planes is a LEAK.\n")

    any_leak = False
    any_pocket = False
    for game_map in maps:
        spawns = spawns_of(game_map)
        tileset = rules.tilesets[game_map.tileset]
        locos = modload.world_locomotors(rules, game_map.rule_overrides)
        if args.locomotor:
            locos = [x for x in locos if x.name in args.locomotor]
        occupancy, _ = nav_guard.cell_occupancy(rules, game_map, "live")

        print(f"{game_map.name}   bounds={game_map.bounds}  spawns={len(spawns)} {spawns}")
        if len(spawns) < 2:
            print("    ! fewer than two spawns -- no line derivable\n")
            continue

        pairs = list(itertools.combinations(spawns, 2))
        if not args.all_pairs and len(spawns) > 2:
            pairs = [max(pairs, key=lambda p: (p[0][0] - p[1][0]) ** 2 + (p[0][1] - p[1][1]) ** 2)]

        for a, b in pairs:
            start, end = perpendicular_bisector(a, b, args.extend)
            geo = Geometry(start, end, args.half_width)
            if geo.degenerate:
                print(f"    pair {a}-{b}: DEGENERATE (coincident spawns) -- no wall")
                continue

            print(f"    {a} vs {b}  ->  line {start}..{end}  [{geo.angle_note()}]"
                  f"  clearance {geo.distance_cells(*a):.1f}/{geo.distance_cells(*b):.1f} cells")

            for loco in locos:
                model = nav_guard.build_cell_model(rules, game_map, tileset, loco, occupancy)
                r = audit_line(model, geo, args.squeeze)
                if r["base_largest"] == 0:
                    continue
                (la, pa, ca), (lb, pb, cb) = r["per_side"][1], r["per_side"][-1]
                leaked = bool(r["leaks"])
                sealed = r["newly_isolated"]
                any_leak |= leaked
                flag = "LEAK" if leaked else ("SEAL" if sealed else "ok  ")
                if sealed:
                    any_pocket = True
                if args.quiet and not leaked and not sealed:
                    continue
                print(f"        {flag} {loco.name:<26} band={r['band']:>4} "
                      f"A={la:>6}(+{pa} in {ca}) B={lb:>6}(+{pb} in {cb}) "
                      f"base={r['base_largest']}"
                      + (f"  SEALED OFF {sealed} cells" if sealed else "")
                      + (f"  SPANNING {r['leaks']}" if leaked else ""))

                if args.show_sealed and r["sealed_cells"]:
                    xs = [model.left + (c % model.width) for c in r["sealed_cells"]]
                    ys = [model.top + (c // model.width) for c in r["sealed_cells"]]
                    print(f"             sealed bbox x {min(xs)}..{max(xs)} "
                          f"y {min(ys)}..{max(ys)}  e.g. ({xs[0]}, {ys[0]})")
        print()

    print("RESULT:", "LEAKS FOUND" if any_leak else "every derived line separates cleanly")
    if any_pocket:
        print("NOTE: some sides carry pocketed cells; compare against the map's own base "
              "component count before reading that as wall-induced.")
    return 1 if any_leak else 0


if __name__ == "__main__":
    sys.exit(main())
