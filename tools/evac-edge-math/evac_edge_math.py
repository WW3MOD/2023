#!/usr/bin/env python3
"""Static answer to pipeline item 78: when a ground unit evacuates, does it leave
through its OWN player's map edge or an OPPONENT's, and how much shorter is the trip?

No game, no simulation. Reads `mods/ww3mod/maps/*/map.yaml` + `map.bin` through
nav-guard's decoder, replicates the engine's edge choice exactly, and counts.

The engine path being modelled (RotateToEdge.cs:161-168, ground branch):

    var searchOrigin = FindClosestSpawnAreaForOwner(self) ?? self.Location;
    return Map.ChooseClosestMatchingEdgeCell(searchOrigin,
        c => mobileInfo.CanEnterCell(world, null, c) && CanReach(self, mobileInfo, c));

and (Map.cs:1874-1877):

    AllEdgeCells.OrderBy(c => (cell - c).LengthSquared).FirstOrDefault(c => match(c))

so the destination is the nearest PASSABLE, REACHABLE cell on the Bounds perimeter,
by exact squared Euclidean distance in cell coords, ties broken by UpdateEdgeCells'
enumeration order (Map.cs:1940-1968) because OrderBy is stable.

Grid is Rectangular with MaximumTerrainHeight 0 (mods/ww3mod/mod.yaml:328-330,
MapGrid.cs:110), so CPos == MPos == PPos and projection is the identity
(Map.cs:781-790). That is what lets this be done in cell coords at all.

Two attributions of "whose edge", because they disagree and only one is honest:

  voronoi  - the spawn nearest the chosen cell. This is close to TAUTOLOGICAL: the
             chosen cell is the perimeter cell nearest the unit, so a unit standing
             in the enemy half is nearly guaranteed to pick a cell the enemy spawn
             is nearest to. Reported for completeness; do not lean on it.
  wall     - which of the four Bounds walls the chosen cell lies on, against the wall
             each spawn sits nearest. On 8 of 10 shipped maps every spawn is on Left
             or Right, so an exit through Top or Bottom is a NEUTRAL FLANK, not
             anybody's back edge. This is the number that answers item 78.

And the magnitude, which decides whether the label matters at all:

  drive    - cells the unit actually drives to the chosen cell.
  fix      - cells it would drive under item 78's proposed one-token change
             (`?? self.Location` -> `?? self.Owner.HomeLocation`), i.e. to
             ChooseClosestMatchingEdgeCell(HomeLocation, ...).
"""

from __future__ import annotations

import argparse
import math
import statistics
import sys
from dataclasses import dataclass, field
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(ROOT / "tools" / "nav-guard"))

import modload            # noqa: E402
import nav_guard          # noqa: E402

MOD_DIR = ROOT / "mods" / "ww3mod"

# The locomotor a "wrecked tank" uses. Item 78's scenario is a vehicle; `tracked` is the
# main battle tank one. --locomotor overrides.
DEFAULT_LOCOMOTOR = "tracked"

# "A raider would plausibly be here": within this many cells of an opponent's spawn.
DEFAULT_RAID_RADIUS = 20


# --------------------------------------------------------------------- edge modelling

def edge_cells_in_engine_order(bounds):
    """Replicates Map.UpdateEdgeCells (Map.cs:1940-1968) for a flat Rectangular grid.

    Order matters: it is the stable-sort tiebreak inside ChooseClosestMatchingEdgeCell.
    Corners are emitted TWICE, once by the row loop and once by the column loop - that
    duplication is in the engine list too, and is harmless for a single-winner pick.
    """
    left, top, width, height = bounds
    right, bottom = left + width, top + height     # exclusive, as Rectangle.Right/Bottom
    last_row = bottom - 1
    cells = []
    for u in range(left, right):
        cells.append((u, top))
        cells.append((u, last_row))
    for v in range(top, bottom):
        cells.append((left, v))
        cells.append((right - 1, v))
    return cells


def perpendicular_edge_cell(bounds, cell):
    """Replicates Map.ChooseClosestEdgeCell (Map.cs:1821-1863) - the OTHER chooser, used
    by the aircraft branch and five non-evac callers. Half-plane pick of a horizontal and
    a vertical bound, then whichever is nearer. No passability filter.

    Note it can return a cell one PAST the bounds on the right/bottom: the engine uses
    Bounds.Right / Bounds.Bottom, which are exclusive. Reproduced, not corrected.
    """
    left, top, width, height = bounds
    right, bottom = left + width, top + height
    u, v = cell
    horizontal = left if (u - left) < width // 2 else right
    vertical = top if (v - top) < height // 2 else bottom
    du, dv = abs(horizontal - u), abs(vertical - v)
    return (horizontal, v) if du < dv else (u, vertical)


def nearest_perimeter_cell(fx, cell):
    """The unfiltered argmin - what ChooseClosestMatchingEdgeCell would return if every
    perimeter cell matched. Used only to measure how often the filter diverts the answer.
    NOT the same as perpendicular_edge_cell, which has the engine's exclusive-Right
    off-by-one and can name a cell outside Bounds."""
    return perimeter_order(fx, cell)[0]


def wall_of(bounds, cell):
    """Which Bounds wall a cell sits on / is nearest: 'L', 'R', 'T' or 'B'.
    Corners resolve to the horizontal wall, matching ChooseClosestEdgeCell's du<dv."""
    left, top, width, height = bounds
    u, v = cell
    d = [(u - left, "L"), ((left + width - 1) - u, "R"),
         (v - top, "T"), ((top + height - 1) - v, "B")]
    d.sort()
    return d[0][1]


# ------------------------------------------------------------------------- map fixture

@dataclass
class MapFixture:
    name: str
    bounds: tuple
    spawns: list
    passable: bytearray          # bounds-local, [y*w + x]
    labels: list                 # component label per bounds-local cell, -1 = impassable
    edge: list                   # perimeter, engine order
    has_spawnarea: bool
    spawn_areas: list = field(default_factory=list)
    spawn_walls: list = field(default_factory=list)
    # Per-owner searchOrigin when the map HAS spawnareas: the spawnarea nearest
    # that owner's SUPPLYROUTE, which world.yaml:463+ places at their mpspawn.
    # RotateToEdge.FindClosestSpawnAreaForOwner:111-127. Empty when no spawnarea.
    owner_origin: list = field(default_factory=list)

    def local(self, cell):
        return cell[0] - self.bounds[0], cell[1] - self.bounds[1]

    def in_bounds(self, cell):
        lx, ly = self.local(cell)
        return 0 <= lx < self.bounds[2] and 0 <= ly < self.bounds[3]

    def label_at(self, cell):
        if not self.in_bounds(cell):
            return -1
        lx, ly = self.local(cell)
        return self.labels[ly * self.bounds[2] + lx]


def build_fixture(rules, tileset_cache, map_dir, locomotor_name, squeeze):
    game_map = modload.load_map(map_dir)
    if game_map.tileset not in tileset_cache:
        tileset_cache[game_map.tileset] = modload.load_tileset(
            MOD_DIR / "tilesets" / (game_map.tileset.lower() + ".yaml"))
    tileset = tileset_cache[game_map.tileset]

    locomotors = modload.world_locomotors(rules, game_map.rule_overrides)
    loco = next(l for l in locomotors if l.name == locomotor_name)

    occupancy, _unknown = nav_guard.cell_occupancy(rules, game_map, "live")
    model = nav_guard.build_cell_model(rules, game_map, tileset, loco, occupancy)
    labels, _sizes = nav_guard.component_labels(model, squeeze)

    spawns = sorted(a.location for a in game_map.actors if a.name == "mpspawn")
    areas = sorted(a.location for a in game_map.actors if a.name == "spawnarea")

    fx = MapFixture(game_map.name, game_map.bounds, spawns,
                    model.passable, labels,
                    edge_cells_in_engine_order(game_map.bounds), bool(areas))
    fx.spawn_areas = areas
    fx.spawn_walls = [wall_of(fx.bounds, s) for s in spawns]
    # FindClosestSpawnAreaForOwner anchors on the player's own ProductionFromMapEdge
    # (the SUPPLYROUTE), which starts at their mpspawn - so the owner's searchOrigin is
    # the spawnarea nearest their spawn point, NOT the evacuating unit's position.
    if areas:
        fx.owner_origin = [min(areas, key=lambda a: (a[0] - s[0]) ** 2 + (a[1] - s[1]) ** 2)
                           for s in spawns]
    return fx


# ------------------------------------------------------------------ the engine's choice

def perimeter_order(fx, origin):
    ox, oy = origin
    return [c for _, _, c in sorted(
        ((ox - c[0]) ** 2 + (oy - c[1]) ** 2, i, c) for i, c in enumerate(fx.edge))]


def chosen_edge_cell(fx, order_origin, reach_from=None):
    """ChooseClosestMatchingEdgeCell(order_origin, CanEnterCell && CanReach).

    The two origins are DIFFERENT and that is not a typo in this model - it is the
    engine's shape. The sort key is `searchOrigin` (RotateToEdge.cs:165-167), but the
    reachability predicate is `PathExistsForLocomotor(locomotor, cell, self.Location)`
    (RotateToEdge.cs:177-180), i.e. from the UNIT. They coincide only when searchOrigin
    fell back to `self.Location` - which is the nine maps with no spawnarea.

    Returns None when no perimeter cell qualifies - FirstOrDefault's default(CPos) is
    (0,0), which RotateToEdge would then try to drive to; treated here as "no answer".
    """
    if reach_from is None:
        reach_from = order_origin
    my_label = fx.label_at(reach_from)
    if my_label < 0:
        return None
    for cell in perimeter_order(fx, order_origin):
        if fx.label_at(cell) == my_label:
            return cell
    return None


def nearest_spawn(fx, cell):
    d = sorted(((cell[0] - s[0]) ** 2 + (cell[1] - s[1]) ** 2, i)
               for i, s in enumerate(fx.spawns))
    return d[0][1]


def dist(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


# ------------------------------------------------------------------------------ counting

@dataclass
class Tally:
    n: int = 0
    own_wall: int = 0
    enemy_wall: int = 0
    flank_wall: int = 0
    voronoi_enemy: int = 0
    drive: list = field(default_factory=list)
    fix_drive: list = field(default_factory=list)

    def pct(self, k):
        return 100.0 * getattr(self, k) / self.n if self.n else float("nan")

    def med(self, k):
        v = getattr(self, k)
        return statistics.median(v) if v else float("nan")


POPS = ("all", "enemy-half", "raid")


def analyse_map(fx, stride, raid_radius, examples_wanted=0):
    left, top, bw, bh = fx.bounds
    n = len(fx.spawns)
    pops = {k: Tally() for k in POPS}
    examples = []
    diverted = sampled = unreachable = 0
    # Does the filter's diversion change the ANSWER (which wall) or only the exact cell?
    wall_flip = 0
    # Does the intuitive chooser (perpendicular foot on the nearest wall, i.e. what
    # ChooseClosestEdgeCell computes for the aircraft branch) name a different wall than
    # the unfiltered argmin over the perimeter list? If not, the intuition is sound and
    # the filter is the only source of surprise.
    perp_wall_mismatch = 0

    # The counterfactual destination under item 78's proposed fix: one cell per owner,
    # resolved from HomeLocation instead of the unit's position.
    fix_target = [chosen_edge_cell(fx, s) for s in fx.spawns]

    # On a map WITH spawnareas the shipped ground branch already resolves searchOrigin
    # from an owner-side anchor, so the exit is per-owner and does not move with the
    # unit. river-zeta is the one shipped map in that state - it is already living
    # under item 78's proposal.
    owner_anchored = bool(fx.owner_origin)

    for ly in range(0, bh, stride):
        for lx in range(0, bw, stride):
            if not fx.passable[ly * bw + lx]:
                continue
            cell = (left + lx, top + ly)
            # Sort origin per the engine; reachability always from the unit.
            per_owner_e = [chosen_edge_cell(fx, fx.owner_origin[si], cell)
                           for si in range(n)] if owner_anchored else None
            e = per_owner_e[0] if owner_anchored else chosen_edge_cell(fx, cell)
            if e is None:
                unreachable += 1
                continue
            sampled += 1
            unfiltered = nearest_perimeter_cell(fx, cell)
            if not owner_anchored:
                if e != unfiltered:
                    diverted += 1
                if wall_of(fx.bounds, e) != wall_of(fx.bounds, unfiltered):
                    wall_flip += 1
            if wall_of(fx.bounds, perpendicular_edge_cell(fx.bounds, cell)) \
                    != wall_of(fx.bounds, unfiltered):
                perp_wall_mismatch += 1
            d2 = [(cell[0] - s[0]) ** 2 + (cell[1] - s[1]) ** 2 for s in fx.spawns]

            for si in range(n):
                if owner_anchored:
                    e = per_owner_e[si]
                    if e is None:
                        continue
                e_wall = wall_of(fx.bounds, e)
                e_vor = nearest_spawn(fx, e)
                d_chosen = dist(cell, e)
                own = e_wall == fx.spawn_walls[si]
                enemy = (not own) and e_wall in {fx.spawn_walls[j]
                                                 for j in range(n) if j != si}
                buckets = [pops["all"]]
                nd2 = min(d2[j] for j in range(n) if j != si)
                if nd2 < d2[si]:
                    buckets.append(pops["enemy-half"])
                if nd2 <= raid_radius * raid_radius:
                    buckets.append(pops["raid"])
                    if enemy and len(examples) < examples_wanted:
                        examples.append(
                            "      p=%s owner=spawn%d%s wall %s -> exit %s wall %s, "
                            "drive %.0f (fix would drive %.0f)"
                            % (cell, si, fx.spawns[si], fx.spawn_walls[si], e, e_wall,
                               d_chosen,
                               dist(cell, fix_target[si]) if fix_target[si] else -1))
                for t in buckets:
                    t.n += 1
                    t.own_wall += own
                    t.enemy_wall += enemy
                    t.flank_wall += not (own or enemy)
                    t.voronoi_enemy += e_vor != si
                    t.drive.append(d_chosen)
                    if fix_target[si] is not None:
                        t.fix_drive.append(dist(cell, fix_target[si]))

    return (pops, examples, diverted, sampled, unreachable,
            wall_flip, perp_wall_mismatch)


# ---------------------------------------------------------------------------------- cli

HEAD = ("%-22s%3s %6s | %7s %7s %7s | %7s | %6s %6s %6s"
        % ("map", "sp", "pop", "own%", "enemy%", "flank%", "voron%",
           "drive", "fix", "delta"))


def row(name, sp, popname, t):
    return ("%-22s%3s %6s | %7.1f %7.1f %7.1f | %7.1f | %6.1f %6.1f %+6.1f"
            % (name, sp, popname, t.pct("own_wall"), t.pct("enemy_wall"),
               t.pct("flank_wall"), t.pct("voronoi_enemy"),
               t.med("drive"), t.med("fix_drive"),
               t.med("fix_drive") - t.med("drive")))


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--locomotor", default=DEFAULT_LOCOMOTOR)
    ap.add_argument("--stride", type=int, default=2,
                    help="sample every Nth cell in each axis (default 2)")
    ap.add_argument("--squeeze", default=nav_guard.DEFAULT_SQUEEZE)
    ap.add_argument("--map", action="append", default=[])
    ap.add_argument("--pop", action="append", default=[], choices=POPS,
                    help="restrict printed populations (default: all three)")
    ap.add_argument("--raid-radius", type=int, default=DEFAULT_RAID_RADIUS,
                    help="the 'raid' population is cells within this many of an "
                         "opponent's spawn (default %d)" % DEFAULT_RAID_RADIUS)
    ap.add_argument("--examples", type=int, default=0)
    args = ap.parse_args(argv)
    wanted = args.pop or list(POPS)

    rules = modload.load_mod(MOD_DIR)
    tileset_cache = {}
    map_dirs = sorted(d for d in (MOD_DIR / "maps").iterdir() if (d / "map.yaml").exists())
    if args.map:
        map_dirs = [d for d in map_dirs if any(f in d.name for f in args.map)]

    print("locomotor=%s  stride=%d  squeeze=%s  raid-radius=%d"
          % (args.locomotor, args.stride, args.squeeze, args.raid_radius))
    print("own/enemy/flank% = which of the four Bounds walls the chosen exit cell is on,")
    print("against the wall each spawn sits nearest. voron% = the near-tautological")
    print("nearest-spawn attribution. drive/fix = median cells driven, now vs under the")
    print("proposed HomeLocation fix.\n")
    print(HEAD)
    print("-" * len(HEAD))

    grand = {k: Tally() for k in POPS}
    gd = gs = gu = gwf = gpm = 0
    geometry = []

    for d in map_dirs:
        fx = build_fixture(rules, tileset_cache, d, args.locomotor, args.squeeze)
        (pops, examples, diverted, sampled, unreach,
         wflip, pmis) = analyse_map(
            fx, args.stride, args.raid_radius, args.examples)
        gd += diverted
        gs += sampled
        gu += unreach
        gwf += wflip
        gpm += pmis
        geometry.append((fx.name, fx.bounds, list(zip(fx.spawns, fx.spawn_walls)),
                         fx.has_spawnarea, diverted, sampled, unreach))
        for k, t in pops.items():
            g = grand[k]
            g.n += t.n
            for a in ("own_wall", "enemy_wall", "flank_wall", "voronoi_enemy"):
                setattr(g, a, getattr(g, a) + getattr(t, a))
            g.drive += t.drive
            g.fix_drive += t.fix_drive
        first = True
        for k in wanted:
            print(row(fx.name if first else "", len(fx.spawns) if first else "",
                      k, pops[k]))
            first = False
        for e in examples:
            print(e)
        print()

    print("-" * len(HEAD))
    first = True
    for k in wanted:
        print(row("ALL MAPS" if first else "", "", k, grand[k]))
        first = False
    print("\nsample sizes (owner,cell pairs): " +
          "  ".join("%s=%d" % (k, grand[k].n) for k in POPS))
    pc = lambda k: 100.0 * k / gs if gs else 0.0
    print("cells sampled=%d   no reachable perimeter cell at all=%d" % (gs, gu))
    print("  filter diverted the chosen CELL off the unfiltered argmin: %d (%.1f%%)"
          % (gd, pc(gd)))
    print("  ...and of those, changed which WALL the unit exits by:      %d (%.1f%%)"
          % (gwf, pc(gwf)))
    print("  intuitive chooser (perpendicular foot) names a different wall")
    print("  than the unfiltered argmin:                                 %d (%.1f%%)"
          % (gpm, pc(gpm)))

    print("\ngeometry")
    for name, b, sw, sa, dv, sm, un in geometry:
        print("  %-22s bounds=%-18s spawnarea=%-5s spawns=%s"
              % (name, str(b), sa, ", ".join("%s%s" % (s, w) for s, w in sw)))
    print("")
    print("NOTE: a map with spawnarea=True already resolves the ground evac from an")
    print("owner-side anchor (FindClosestSpawnAreaForOwner -> the spawnarea nearest that")
    print("player's SUPPLYROUTE), i.e. it is ALREADY living under item 78's proposal.")
    print("Its drive/fix columns are therefore near-identical by construction.")
    print("The ALL MAPS row MIXES the two regimes and is not the number to quote. For the")
    print("nine unit-anchored maps, re-run excluding the spawnarea map with --map filters.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
