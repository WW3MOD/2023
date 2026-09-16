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

A REGION IS THE OTHER SHAPE THE BORDER CAN TAKE, and it is checked here too. A map may
author the border as a SET OF CELLS instead of a line -- terrain types plus hand-drawn
additions (DefconWallInfo.RegionTerrainTypes / RegionCells) -- which is what a map divided
by a river wants, because the bisector of two spawns ignores the water entirely. The two
questions are identical for a region and the answers matter more, because a region is
authored by hand and nothing derives it:

  SEPARATION. Same test, and the region's failure mode is the one a line cannot have: a
  river that stops short of the map edge leaves a land bridge round the end, so the border
  is drawn over real water, looks completely convincing, and divides nothing.

  AND IT DIFFERS PER LOCOMOTOR, which the line's audit never had to care about, because a
  line is a statement about POSITION and a region is a statement about REACHABILITY -- and
  reachability is a property of the mover. Measured on river-zeta-ww3: blocking
  Water+River+Bridge separates every VEHICLE locomotor 3/3 by spawn (2667/2622 cells) while
  leaving `foot`, `walker`, `template` and both amphibious foot classes as ONE 6843-cell
  component containing all six spawns. A region checked against one locomotor is not checked.

  THE MECHANISM IS NOT A GAP IN THE WATER, and this cost two wrong hypotheses before the
  path trace settled it. The obvious reading is that the river stops short of the map edge
  and infantry walk round the end -- river-zeta-ww3 really does have three water-free rows
  at each end (y3-y5, y77-y79), so the story fits. It is wrong: capping both ends changes
  NOTHING, at any cap width from 66 to 126 cells. The actual crossing is mid-river at
  x51-60, y43-49, over cells whose terrain type is ROCK. The channel is not solid water --
  it has dry rock outcrops in it, `foot` lists Rock in its TerrainSpeeds and `heavywheeled`
  does not, and that single terrain difference IS the entire vehicle-versus-infantry split.
  Adding Rock to the type list separates every locomotor (721 cells) and makes the end caps
  unnecessary, because the same rock closes those rows too.

  GENERALISE: when a terrain border leaks for one mover class and not another, the cause is
  a terrain type inside the barrier that one locomotor passes -- not, as it appears, a hole
  at the barrier's ends. Trace a path before authoring a cap; a row-by-row scan of the
  barrier's extent will show you gaps that are not the gap being used.

Usage:
    python defcon_wall_audit.py                     # shipped default, extreme spawn pair
    python defcon_wall_audit.py --half-width 512    # reproduce the leak
    python defcon_wall_audit.py --all-pairs         # every pair a 1v1 could produce
    python defcon_wall_audit.py --map river-zeta --region-terrain Water,River,Bridge
    python defcon_wall_audit.py --map river-zeta --region-terrain Water,River,Bridge \
        --region-cells "44,0,44,1"                  # terrain plus hand-drawn end caps
    python defcon_wall_audit.py --map river-zeta --region-file region.txt
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


def parse_region_cells(text: str) -> list[tuple[int, int]]:
    """A flat comma-separated X,Y list, exactly as FieldLoader.ParseCPosArray reads it.

    Mirrored rather than loosened on purpose: if this accepted a form the engine rejects,
    a region could pass the audit and then fail to load.
    """
    parts = [p.strip() for p in text.replace("\n", ",").split(",") if p.strip()]
    if len(parts) % 2 != 0:
        raise ValueError(f"expected an even number of coordinates, got {len(parts)}")
    return [(int(parts[2 * i]), int(parts[2 * i + 1])) for i in range(len(parts) // 2)]


def region_cells_for(game_map, tileset, terrain_types: list[str],
                     explicit: list[tuple[int, int]]) -> set[tuple[int, int]]:
    """The border cell set: every cell of an authored terrain type, plus the authored cells.

    Mirrors DefconWall.BuildRegion. The terrain scan walks the whole map rather than only
    Bounds because the engine's does -- Map.AllCells includes the border ring, and a border
    that stopped at Bounds would leave a one-cell seam round the edge.
    """
    cells = {c for c in explicit}
    if terrain_types:
        wanted = set(terrain_types)
        for y in range(game_map.height):
            for x in range(game_map.width):
                if game_map.terrain_type(tileset, x, y) in wanted:
                    cells.add((x, y))
    return cells


def engine_load_gate(game_map, blocked):
    """DefconWallRegion's OWN labelling, which decides whether the wall is raised at all.

    THIS IS NOT THE PER-LOCOMOTOR TEST AND IT IS STRICTLY STRONGER. DefconWall.BuildRegion
    hands DefconWallRegion a passability predicate of `Map.Contains` and nothing else
    (DefconWall.cs, "PASSABILITY HERE IS `Map.Contains` AND NOTHING ELSE") -- deliberately,
    because passability is a property of a locomotor and a World-actor trait would have to
    pick one arbitrarily. So the engine floods a grid in which EVERY in-Bounds cell that is
    not a border cell is passable, and `IsDegenerate => ComponentCount < 2` then decides
    whether the region survives. A degenerate region is DISCARDED and the wall stays down for
    the whole match; it does not fall back to a line.

    A path in a locomotor's graph is also a path in that fully-open graph, so open-graph
    separation implies separation for every locomotor -- and the converse fails. A region can
    therefore pass every locomotor line below, exit 0, and still never raise a wall in game.
    That is not hypothetical: Water,River,Bridge on river-zeta-ww3 separates all six vehicle
    locomotors while leaving the open graph in ONE 7060-cell piece.

    Also mirrored: cells outside Bounds are DROPPED. DefconWallRegion.IndexOf returns -1 for
    them, so an out-of-Bounds authored cell never enters BlockedCells, never reaches
    CustomTerrain and is never drawn -- whatever BuildRegion's own comment about closing the
    border ring says.
    """
    left, top, width, height = game_map.bounds
    inside = {c for c in blocked
              if left <= c[0] < left + width and top <= c[1] < top + height}
    dropped = len(blocked) - len(inside)

    seen = set()
    components = 0
    for y in range(top, top + height):
        for x in range(left, left + width):
            if (x, y) in inside or (x, y) in seen:
                continue
            components += 1
            stack = [(x, y)]
            seen.add((x, y))
            while stack:
                cx, cy = stack.pop()
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        if dx == 0 and dy == 0:
                            continue
                        n = (cx + dx, cy + dy)
                        if not (left <= n[0] < left + width
                                and top <= n[1] < top + height):
                            continue
                        if n in inside or n in seen:
                            continue
                        seen.add(n)
                        stack.append(n)

    return len(inside), dropped, components


def audit_region(model, blocked: set[tuple[int, int]], variant: str, spawns):
    """Separation and pockets for a REGION border, reported per locomotor.

    The structure mirrors audit_line, with one difference that matters: a line has two
    half-planes known in advance, so `side` is a function of position. A region has
    COMPONENTS, which are only known after the flood -- so separation here is "do the
    spawns land in more than one component", and a leak is "two spawns that should be
    opposed are in the same one".
    """
    width = model.width
    base_comps = _walk(model, model.passable, variant, list)
    base_comps.sort(key=len, reverse=True)
    base_sizes = [len(c) for c in base_comps]
    base_main = set(base_comps[0]) if base_comps else set()

    passable = bytearray(model.passable)
    band = 0
    for (cx, cy) in blocked:
        lx, ly = cx - model.left, cy - model.top
        if 0 <= lx < model.width and 0 <= ly < model.height:
            i = ly * width + lx
            if passable[i]:
                passable[i] = 0
                band += 1

    comps = _walk(model, passable, variant, list)
    comps.sort(key=len, reverse=True)

    label = {}
    for ci, cells in enumerate(comps):
        for i in cells:
            label[i] = ci

    spawn_components = []
    for (sx, sy) in spawns:
        lx, ly = sx - model.left, sy - model.top
        if 0 <= lx < model.width and 0 <= ly < model.height:
            spawn_components.append(label.get(ly * width + lx))
        else:
            spawn_components.append(None)

    reachable = [c for c in spawn_components if c is not None]
    distinct = len(set(reachable))

    # Cells that were mutually reachable before the border and are now in NO spawn's
    # component -- the region's version of the pocket test. A lobe of land nobody can
    # reach is the nav-guard failure shape occurring inside a half.
    spawn_bodies = set()
    for c in set(reachable):
        spawn_bodies |= set(comps[c])
    sealed = [i for i in base_main if passable[i] and i not in spawn_bodies]

    return {
        "band": band,
        "components": [len(c) for c in comps],
        "spawn_components": spawn_components,
        "distinct": distinct,
        "separates": distinct > 1,
        "unreachable_spawns": sum(1 for c in spawn_components if c is None),
        "newly_isolated": len(sealed),
        "sealed_cells": sealed,
        "base_largest": base_sizes[0] if base_sizes else 0,
        "base_pocketed": sum(base_sizes) - (base_sizes[0] if base_sizes else 0),
    }


def run_region(args, rules, maps) -> int:
    terrain_types = [t.strip() for t in (args.region_terrain or "").split(",") if t.strip()]

    explicit: list[tuple[int, int]] = []
    if args.region_cells:
        explicit += parse_region_cells(args.region_cells)
    if args.region_file:
        explicit += parse_region_cells(Path(args.region_file).read_text(encoding="utf-8"))

    print(f"DEFCON wall audit -- REGION  (terrain {terrain_types or 'none'}, "
          f"{len(explicit)} authored cell(s), squeeze {args.squeeze})")
    print("  SEPARATION is the test: the spawns must not all land in ONE component.")
    print("  A one-cell-wide diagonal seals nothing -- 8-connected steps go through its corners.\n")

    failed = False
    for game_map in maps:
        tileset = rules.tilesets[game_map.tileset]
        spawns = spawns_of(game_map)
        locos = modload.world_locomotors(rules, game_map.rule_overrides)
        if args.locomotor:
            locos = [x for x in locos if x.name in args.locomotor]
        occupancy, _ = nav_guard.cell_occupancy(rules, game_map, "live")

        blocked = region_cells_for(game_map, tileset, terrain_types, explicit)
        print(f"{game_map.name}   bounds={game_map.bounds}  spawns={len(spawns)} {spawns}")
        print(f"    border cells: {len(blocked)}")
        if not blocked:
            print("    ! the region is empty -- nothing to audit\n")
            failed = True
            continue

        # The engine's own load-time gate, consulted before any locomotor is. See
        # engine_load_gate: a region that leaves this in one piece is discarded by
        # DefconWall.BuildRegion and no wall is ever raised, however green the lines below.
        in_bounds, dropped, open_components = engine_load_gate(game_map, blocked)
        gate_ok = open_components >= 2
        failed |= not gate_ok
        print(f"    {'ok  ' if gate_ok else 'DEGENERATE'} engine load gate "
              f"(DefconWallRegion, Map.Contains passability): {in_bounds} cell(s) in Bounds"
              + (f", {dropped} dropped as out-of-Bounds" if dropped else "")
              + f", {open_components} component(s)")
        if not gate_ok:
            print("         ! IsDegenerate -- BuildRegion logs and the wall stays DOWN. "
                  "The per-locomotor results below are moot.")

        for loco in locos:
            model = nav_guard.build_cell_model(rules, game_map, tileset, loco, occupancy)
            if sum(model.passable) == 0:
                continue

            r = audit_region(model, blocked, args.squeeze, spawns)

            # A locomotor that cannot reach any spawn has no opinion about this border --
            # naval on a land map, `immobile`. Reporting it as a failure would drown the
            # real answer in noise.
            if not [c for c in r["spawn_components"] if c is not None]:
                continue

            ok = r["separates"]
            failed |= not ok
            flag = "ok  " if ok else "LEAK"
            if args.quiet and ok and not r["newly_isolated"]:
                continue

            sizes = r["components"][:4]
            print(f"        {flag} {loco.name:<26} blocked={r['band']:>4} "
                  f"components={r['distinct']} spawns={r['spawn_components']} "
                  f"sizes={sizes}"
                  + (f"  SEALED OFF {r['newly_isolated']} cells" if r["newly_isolated"] else ""))

            if args.show_sealed and r["sealed_cells"]:
                xs = [model.left + (c % model.width) for c in r["sealed_cells"]]
                ys = [model.top + (c // model.width) for c in r["sealed_cells"]]
                print(f"             sealed bbox x {min(xs)}..{max(xs)} "
                      f"y {min(ys)}..{max(ys)}  e.g. ({xs[0]}, {ys[0]})")
        print()

    print("RESULT:", "REGION DOES NOT SEPARATE" if failed
          else "the region separates the spawns on every locomotor that can reach them")
    return 1 if failed else 0


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

    # ---- REGION MODE. Any of these three switches the audit from the derived LINE to an
    # authored REGION (DefconWallInfo.RegionTerrainTypes / RegionCells). The line flags above
    # are then unused: a map authors one or the other, never both.
    ap.add_argument("--region-terrain", default=None,
                    help="comma-separated terrain type names forming the border, "
                         "e.g. Water,River,Bridge (DefconWallInfo.RegionTerrainTypes)")
    ap.add_argument("--region-cells", default=None,
                    help="flat comma-separated X,Y list of extra border cells, exactly as "
                         "FieldLoader reads DefconWallInfo.RegionCells")
    ap.add_argument("--region-file", default=None,
                    help="read the same flat X,Y list from a file (newlines count as commas)")
    args = ap.parse_args(argv)

    rules = modload.load_mod(nav_guard.MOD_DIR)
    maps = [modload.load_map(p) for p in modload.discover_maps(nav_guard.MOD_DIR)]
    if args.map:
        maps = [m for m in maps if any(f in m.name for f in args.map)]

    if args.region_terrain or args.region_cells or args.region_file:
        return run_region(args, rules, maps)

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
