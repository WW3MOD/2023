#!/usr/bin/env python3
"""Who owns what under the BORDER rule, per shipped map, without building or launching.

The sibling script in this directory (precaptured_calibration.py) answers the same
question for the RATIO rule, which is now only the fallback: it is read on a map
where no DefconWall border resolves. Nine of the ten shipped maps author one, so on
those the rule that actually runs is this one:

  1. a structure with ANY footprint cell inside the border band stays Neutral;
  2. otherwise its side is its location cell's side, and if its footprint cells
     disagree on side it stays Neutral;
  3. it goes to the NEAREST contender whose own home is on that side;
  4. a side with no contender on it keeps its structures Neutral.

    python tools/precaptured-calibration/precaptured_border_table.py
    python tools/precaptured-calibration/precaptured_border_table.py --map woodland

FFA with every spawn occupied, which is the widest reading and IS the 1v1 case on
every two-spawn map. The alliance clause that the ratio rule carries has no analogue
here -- see PreCapturedOwnership.ResolveOnSide -- so a 2v2 changes nothing on this
table beyond which teammate a shared side's structures go to.

WHAT IS MIRRORED, AND FROM WHERE. Everything below reproduces engine code rather than
paraphrasing it, because a table that disagrees with the game is worse than no table:

  - the border cell set, incl. river-zeta's RegionTerrainTypes terrain scan, comes
    from tools/nav-guard/defcon_wall_audit.py:region_from_map, which already mirrors
    DefconWall.BuildRegion (and handles `Rules: rules.yaml` as an inline file list,
    which modload alone does not).
  - the component labelling mirrors DefconWallRegion.Label: 8-connected, row-major
    scan order so component ids match the engine's, cells outside Bounds dropped
    (DefconWallRegion.IndexOf returns -1), and Map.Contains as the only passability.
  - the capturable set, the three trait filters and the building footprint centre
    come from precaptured_calibration.py, which resolves the mod's own MiniYaml
    inheritance.

EACH CONTENDER'S SIDE IS THEIR SPAWN CELL, which is what the trait reads too: it asks
DefconWall.SideOf(WPos) about the player's ANCHOR, and the anchor is the Supply
Route's CenterPosition, which on every shipped map IS CenterOfCell(HomeLocation) --
SpawnStartingUnits places the SR at HomeLocation + (-1,-1) and a 3x3 building's
CenterOffset is (+1,+1) cells. Do not confuse that with the border audit's note about
SRs landing outside Bounds: that is the actor's top-left LOCATION cell, not its
centre.
"""

import argparse
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))

sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(ROOT, 'tools', 'nav-guard'))

import precaptured_calibration as calib  # noqa: E402
import defcon_wall_audit as audit  # noqa: E402
import modload  # noqa: E402
import nav_guard  # noqa: E402

NO_SIDE = -1


# ------------------------------------------------------------------- region ---

def label_components(bounds, blocked):
    """Per-cell component ids, mirroring DefconWallRegion.Label.

    8-CONNECTED, and the row-major scan order is load-bearing rather than incidental:
    it is what fixes WHICH component gets id 0, and the engine assigns ids the same
    way, so a side id printed here is the side id the trait sees. Cells outside
    Bounds are absent from the dict rather than labelled, which is IndexOf's -1.
    """
    left, top, width, height = bounds
    labels = {}
    inside = {c for c in blocked
              if left <= c[0] < left + width and top <= c[1] < top + height}

    nxt = 0
    for y in range(top, top + height):
        for x in range(left, left + width):
            if (x, y) in inside or (x, y) in labels:
                continue

            cid = nxt
            nxt += 1
            stack = [(x, y)]
            labels[(x, y)] = cid
            while stack:
                cx, cy = stack.pop()
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        if dx == 0 and dy == 0:
                            continue
                        n = (cx + dx, cy + dy)
                        if not (left <= n[0] < left + width and top <= n[1] < top + height):
                            continue
                        if n in inside or n in labels:
                            continue
                        labels[n] = cid
                        stack.append(n)

    return labels, inside, nxt


def side_of(labels, cell):
    return labels.get(cell, NO_SIDE)


# ---------------------------------------------------------------- footprint ---

_FOOTPRINT = {}


def _resolved_footprint(key):
    """The resolved `Building: Footprint:` string, walking Inherits the way calib does."""
    seen = set()
    state = {'fp': None}

    def walk(name):
        if name in seen:
            return
        seen.add(name)
        parents, own = [], []
        for child, gc in calib.DEFS.get(name, []):
            k = child.rstrip(':').strip()
            head = k.split(':', 1)[0].strip()
            if head.startswith('Inherits'):
                parents.append(k.split(':', 1)[1].strip())
            elif head == 'Building':
                own.append(gc)
        for parent in parents:
            walk(parent)
        for gc in own:
            for g, _ in gc:
                if g.startswith('Footprint:'):
                    state['fp'] = g.split(':', 1)[1].strip()

    walk(key)
    return state['fp']


def footprint_cells(key, loc):
    """The cells a building occupies, exactly as BuildingInfo.Tiles reports them.

    Tiles() yields OccupiedPassable, Occupied, OccupiedUntargetable and
    OccupiedPassableTransitOnly -- i.e. everything in the Footprint string EXCEPT `_`
    (Empty). FootprintCellType's characters are `_ = x X +` (Building.cs:20-27). A
    building with no Footprint at all occupies its whole Dimensions rectangle.

    Every capturable structure on the shipped maps authors `xx xx`-style all-occupied
    footprints, so in practice this equals the rectangle -- but it is parsed rather
    than assumed, because a table that quietly widened a footprint would move a
    straddling verdict.
    """
    if key not in _FOOTPRINT:
        dims, _off = calib._footprint(key)
        raw = _resolved_footprint(key)
        if raw:
            rows = raw.split()
            cells = []
            for dy, row in enumerate(rows):
                for dx, ch in enumerate(row):
                    if ch != '_':
                        cells.append((dx, dy))
        else:
            cells = [(dx, dy) for dy in range(dims[1]) for dx in range(dims[0])]
        _FOOTPRINT[key] = cells

    return [(loc[0] + dx, loc[1] + dy) for dx, dy in _FOOTPRINT[key]]


def structure_side(labels, blocked_inside, key, loc):
    """DefconWall-side of a structure, mirroring PreCapturedStructures.SideOfFootprint."""
    side = side_of(labels, loc)
    for cell in footprint_cells(key, loc):
        if cell in blocked_inside:
            return NO_SIDE, 'band'
        if side_of(labels, cell) != side:
            return NO_SIDE, 'straddles'

    if side == NO_SIDE:
        return NO_SIDE, 'unlabelled'

    return side, ''


# --------------------------------------------------------------------- main ---

def bounds_of(map_yaml_path):
    for line in open(map_yaml_path, encoding='utf-8').read().splitlines():
        m = re.match(r'^Bounds:\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)', line)
        if m:
            return tuple(int(g) for g in m.groups())
    raise ValueError('no Bounds in ' + map_yaml_path)


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument('--map', action='append', default=None,
                    help='substring filter, repeatable')
    ap.add_argument('--ratio', type=float, default=10.0,
                    help='MiddleBandPercent for the fallback column (default 10)')
    args = ap.parse_args(argv)

    threshold = args.ratio / 100.0
    maps = [modload.load_map(p) for p in modload.discover_maps(nav_guard.MOD_DIR)]
    if args.map:
        maps = [m for m in maps if any(f in m.name for f in args.map)]

    print('PRE-CAPTURED STRUCTURES -- the BORDER rule, per shipped map')
    print('FFA, every spawn occupied. Ratio column is the FALLBACK at {:.1f}%, shown only '
          'to expose disagreement.'.format(args.ratio))

    disagreements = 0
    for game_map in sorted(maps, key=lambda m: m.name):
        map_yaml = os.path.join(str(game_map.path), 'map.yaml')
        spawns, caps = calib.read_map(map_yaml)
        bounds = bounds_of(map_yaml)
        blocked, types, authored = audit.region_from_map(game_map)

        print('\n### {}   bounds={}  spawns={}'.format(game_map.name, bounds, spawns))

        if not blocked:
            print('    no DefconWall region authored -- HasBorder is decided by the DERIVED '
                  'line; this script does not model it, and the ratio fallback applies only '
                  'if the derivation also produces nothing.')
            continue

        labels, inside, components = label_components(bounds, blocked)
        print('    border: {} cell(s) authored ({} in Bounds), terrain types {}, '
              '{} component(s)'.format(
                  len(blocked), len(inside), types or 'none', components))

        if components < 2:
            print('    ! DEGENERATE -- BuildRegion discards it and HasBorder is false. '
                  'The ratio fallback runs on this map.')
            continue

        if not spawns:
            print('    (no spawn points -- no contenders, so every structure stays Neutral)')
            continue

        spawn_sides = [side_of(labels, s) for s in spawns]
        for i, (s, sd) in enumerate(zip(spawns, spawn_sides)):
            if sd == NO_SIDE:
                print('    ! spawn{} at {} is UNLABELLED (in the band or off Bounds) -- that '
                      'player contends for nothing'.format(i, s))
        print('    contender sides: ' + ', '.join(
            'spawn{}={}'.format(i, sd) for i, sd in enumerate(spawn_sides)))

        if not caps:
            print('    (no Neutral capturable structures)')
            continue

        per_side = {}
        rows = []
        for actor_type, key, loc in caps:
            cx, cy = calib.centre(key, loc)
            dists = [math.hypot(cx - sx, cy - sy) for sx, sy in spawns]

            # ---- the border rule
            side, why = structure_side(labels, inside, key, loc)
            winner = -1
            for i, d in enumerate(dists):
                if spawn_sides[i] != side or side == NO_SIDE:
                    continue
                if winner < 0 or d < dists[winner]:
                    winner = i

            border_verdict = 'NEUTRAL' if winner < 0 else 'spawn{}'.format(winner)
            note = why if winner < 0 else ''
            if winner < 0 and side != NO_SIDE:
                note = 'side{} has no contender'.format(side)

            # ---- the ratio rule, for comparison only
            ranked = sorted((d, i) for i, d in enumerate(dists))
            near, ni = ranked[0]
            second = ranked[1][0] if len(ranked) > 1 else near * 99
            margin = (second / near - 1.0) if near > 0 else 99.0
            ratio_verdict = 'NEUTRAL' if margin <= threshold else 'spawn{}'.format(ni)

            per_side.setdefault(side, []).append(actor_type)
            rows.append((actor_type, loc, side, border_verdict, note,
                         margin * 100, ratio_verdict))

        print('    {:<16} {:<10} {:>4}  {:<9} {:<26} {:>8}  {:<9} {}'.format(
            'actor', 'at', 'side', 'BORDER', '(why neutral)', 'margin', 'ratio', ''))
        for r in sorted(rows, key=lambda row: (row[0], row[1])):
            flag = '' if r[3] == r[6] else '   <-- DISAGREE'
            if flag:
                disagreements += 1
            print('    {:<16} {:<10} {:>4}  {:<9} {:<26} {:>7.1f}%  {:<9}{}'.format(
                r[0], str(r[1]), r[2] if r[2] >= 0 else -1, r[3], r[4], r[5], r[6], flag))

        tally = {}
        for actor_type, loc, side, verdict, _n, _m, _r in rows:
            tally.setdefault(verdict, []).append(actor_type)
        print('    border-rule tally: ' + ', '.join(
            '{}={} ({})'.format(k, len(v), ' '.join(sorted(v)))
            for k, v in sorted(tally.items())))
        print('    capturables per side: ' + ', '.join(
            'side{}={}'.format(k if k >= 0 else -1, len(v))
            for k, v in sorted(per_side.items())))

    print('\n{} row(s) where the border rule and the ratio fallback disagree.'
          .format(disagreements))
    return 0


if __name__ == '__main__':
    sys.exit(main())
