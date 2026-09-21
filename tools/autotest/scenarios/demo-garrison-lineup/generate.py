#!/usr/bin/env python3
"""Regenerate demo-garrison-lineup's map.yaml and the Grid/Squads tables in its .lua.

Run from the repository root:  python3 tools/autotest/scenarios/demo-garrison-lineup/generate.py

It rewrites map.yaml in place and writes the Lua tables to /tmp/lineup-grid.lua for pasting
into demo-garrison-lineup.lua. Committed rather than left in /tmp because the placement, the
enemy-marker ring and the Lua table have to agree cell for cell, and the .lua header points
here: a reader who wants to move a building or add a garrisoned row edits THIS, not three
files by hand. It also carries the collision and bounds checks at the bottom, which is how
the 109-actor placement was proved free of overlapping footprints without a launch."""
import os

COLS = [7, 16, 25, 34, 43, 52, 61, 70]
ROWS = [6, 14, 22, 30, 38, 46]

# (actor type, footprint dims WxH) -- dims only used to place the ring of targets
# symmetrically about the footprint, never to decide anything about tuning.
DIMS = {
    'gtwr': (1, 1), 'pbox': (1, 1), 'hbox': (1, 1),
    'v01': (2, 2), 'v02': (2, 2), 'v03': (2, 2), 'v04': (2, 2),
    'v05': (2, 1), 'v06': (2, 1), 'v07': (2, 1),
    'v08': (1, 1), 'v09': (1, 1), 'v10': (1, 1), 'v11': (1, 1), 'v12': (1, 1), 'v13': (1, 1),
    'v19': (1, 1), 'v19.husk': (1, 1),
    'v20': (2, 2), 'v21': (2, 2), 'v22': (2, 1), 'v23': (1, 1), 'v24': (2, 2), 'v25': (2, 2),
    'v26': (2, 1), 'v27': (1, 1), 'v28': (1, 1), 'v29': (1, 1), 'v30': (2, 1), 'v31': (2, 1),
    'v32': (2, 1), 'v33': (2, 1), 'v34': (1, 1), 'v35': (1, 1), 'v36': (1, 1), 'v37': (5, 2),
    'rushouse': (1, 2), 'asianhut': (1, 1), 'snowhut': (1, 2), 'lhus': (1, 1), 'windmill': (1, 1),
}

GRID = [
    ['gtwr', 'v02', 'v03', 'v04', 'v05', 'v06', 'v07', 'v08'],
    ['pbox', 'v09', 'v10', 'v11', 'v12', 'v13', 'v20', 'v21'],
    ['hbox', 'v22', 'v23', 'v24', 'v25', 'v26', 'v27', 'v28'],
    ['v01',  'v29', 'v30', 'v31', 'v32', 'v33', 'v34', 'v35'],
    ['v19',  'v36', 'v37', 'asianhut', 'snowhut', 'lhus', 'windmill', 'v19.husk'],
    ['rushouse', None, None, None, None, None, None, None],
]

# Column 0 of every row is garrisoned. men = how many riflemen enter;
# dirs = where the enemy targets sit, one per port bearing we want to light up.
#   'ring'  -> the four diagonals (the 8-port civilian ring: NE/SE/SW/NW x2)
#   'cross' -> N/E/S/W (GTWR's four ports)
#   'ew'    -> E/W (PBOX and HBOX have exactly two ports, front/back)
GARRISON = {
    'gtwr':     dict(dirs='cross'),
    'pbox':     dict(dirs='ew'),
    'hbox':     dict(dirs='ew'),
    'v01':      dict(dirs='ring'),
    'v19':      dict(dirs='ring'),
    'rushouse': dict(dirs='ring'),
}

# HOW MANY MEN IS NOT WRITTEN HERE, AND THAT IS THE POINT.
#
# It used to be: `men=10` on three of the six, back when every civilian inherited
# `MaxWeight: 10` from ^CivBuilding and the .lua header could truthfully say "squad size
# equalled MaxWeight in all six cases". The per-building capacity table
# (f9a4c583) split that one template scalar into 37 per-actor values and neither the
# literal nor the sentence moved -- so V19 fell to 2 and the demo died on the third
# LoadPassenger with a fatal Lua error (run 260921_150809), while RUSHOUSE rose to 12 and
# the demo quietly filled ten twelfths of it under a header still claiming otherwise.
# A literal that must equal a number in another file is a literal that will stop equalling
# it, silently, on a commit that never mentions this directory.
#
# So the count is RESOLVED FROM THE RULES at generation time, and the .lua re-checks the
# placed squad against Test.CargoCapacity at RUN time. Two independent reads of the same
# YAML figure: the generator's is static and can be wrong about inheritance, the engine's
# cannot, and a capture whose census line shows them disagreeing is a capture that says so
# on its face rather than one that is merely wrong.

RULES_DIR = 'mods/ww3mod/rules'


def _rule_blocks():
    """Every top-level actor/template block in the mod's rules, as {key: [lines]}.

    Deliberately NOT a MiniYaml parser -- it only has to find one child of one child, and a
    parser that understands `Inherits@` ordering and removals would be a second, worse copy
    of the engine's. Case is preserved: MiniYaml merges top-level keys case-SENSITIVELY
    (see CLAUDE.md), so `v19:` and `V19:` would be different blocks here exactly as they are
    to the engine."""
    blocks = {}
    for root, _, files in os.walk(RULES_DIR):
        for fn in sorted(files):
            if not fn.endswith('.yaml'):
                continue
            key = None
            for line in open(os.path.join(root, fn)):
                if line.strip() and not line[0].isspace() and not line.startswith('#'):
                    key = line.split(':', 1)[0].strip()
                    blocks.setdefault(key, [])
                elif key is not None:
                    blocks[key].append(line.rstrip('\n'))
    return blocks


def _child(lines, parent, child):
    """Value of `child` under top-level-child `parent`, or None."""
    depth = None
    for line in lines:
        if not line.strip() or line.strip().startswith('#'):
            continue
        indent = len(line) - len(line.lstrip('\t'))
        if depth is None:
            if indent == 1 and line.strip().split(':', 1)[0] == parent:
                depth = indent
            continue
        if indent <= depth:
            depth = None
            if indent == 1 and line.strip().split(':', 1)[0] == parent:
                depth = indent
            continue
        k, _, v = line.strip().partition(':')
        if k == child:
            return v.strip()
    return None


def max_weight(actor, blocks, seen=None):
    """Resolved `Cargo: MaxWeight` for `actor`, following Inherits when it does not declare
    one itself. Raises rather than defaulting: a garrisoned building whose capacity cannot be
    found is a generator that would otherwise emit a confidently wrong squad."""
    seen = seen or set()
    for key in (actor, actor.upper(), actor.capitalize()):
        if key in blocks and key not in seen:
            seen.add(key)
            lines = blocks[key]
            got = _child(lines, 'Cargo', 'MaxWeight')
            if got is not None:
                return int(got)
            for line in lines:
                k, _, v = line.strip().partition(':')
                if k == 'Inherits' or k.startswith('Inherits@'):
                    try:
                        return max_weight(v.strip(), blocks, seen)
                    except LookupError:
                        continue
    raise LookupError(f'no Cargo.MaxWeight resolvable for {actor!r}')


BLOCKS = _rule_blocks()
for _t, _g in GARRISON.items():
    _g['men'] = max_weight(_t, BLOCKS)

def gname(t):
    return 'B_' + t.replace('.', '').upper()

def target_cells(x, y, w, h, mode):
    """Cells for the enemy markers, placed symmetrically about the FOOTPRINT (not the
    Location cell) so a 2x2 gets its ring centred on the sprite rather than on its corner."""
    lo_x, hi_x = x - 3, x + w + 2
    lo_y, hi_y = y - 3, y + h + 2
    mid_x, mid_y = x + (w - 1) // 2, y + (h - 1) // 2
    if mode == 'ring':
        return [(lo_x, lo_y), (hi_x, lo_y), (lo_x, hi_y), (hi_x, hi_y)]
    if mode == 'cross':
        return [(mid_x, lo_y), (hi_x, mid_y), (mid_x, hi_y), (lo_x, mid_y)]
    return [(hi_x, mid_y), (lo_x, mid_y)]

actors = []       # (name, type, owner, x, y)
squads = {}       # building global -> [rifleman globals]
placed = []       # (type, x, y, row, col)

for r, row in enumerate(GRID):
    for c, t in enumerate(row):
        if t is None:
            continue
        x, y = COLS[c], ROWS[r]
        owner = 'USA' if t in ('gtwr', 'pbox', 'hbox') else 'Neutral'
        actors.append((gname(t), t, owner, x, y))
        placed.append((t, x, y, r, c))

        if c != 0:
            continue
        g = GARRISON[t]
        w, h = DIMS[t]
        for i, (tx, ty) in enumerate(target_cells(x, y, w, h, g['dirs']), 1):
            actors.append((f'X{r + 1}_{i}', 'e3', 'Russia', tx, ty))
        squad = []
        # Riflemen wait two cells west of the grid column, five per file, clear of
        # every target cell (targets sit at x-3 = 4 or further out).
        # SIX per file, not five. RUSHOUSE resolves to 12 and a third file would land on
        # x = 4, which is exactly where the `lo_x = x - 3` target marker for a column-0
        # building sits -- the collision check at the bottom of this file catches it, but
        # six-per-file means two files hold the widest squad in the mod and the riflemen
        # stay clear of the ring without moving the ring. Vertical span is y-2..y+3, and the
        # row step is 8, so no squad can reach the next row down.
        for i in range(g['men']):
            sx = 2 + (i // 6)
            sy = y - 2 + (i % 6)
            nm = f'S{r + 1}_{i + 1}'
            actors.append((nm, 'e1', 'USA', sx, sy))
            squad.append(nm)
        squads[gname(t)] = squad

OUT = 'tools/autotest/scenarios/demo-garrison-lineup'

header = f"""MapFormat: 12

RequiresMod: ww3mod

Title: DEMO: Garrison lineup — every garrisonable building

Author: WW3MOD test harness

Tileset: TEMPERAT

MapSize: 100,60

Bounds: 1,1,98,58

Visibility: MissionSelector

Categories: Test

Players:
\tPlayerReference@Neutral:
\t\tName: Neutral
\t\tOwnsWorld: True
\t\tNonCombatant: True
\t\tFaction: america
\tPlayerReference@USA:
\t\tName: USA
\t\tPlayable: True
\t\tLockFaction: True
\t\tLockColor: True
\t\tFaction: america
\t\tColor: 4488FF
\t\tEnemies: Russia
\tPlayerReference@Russia:
\t\tName: Russia
\t\tLockFaction: True
\t\tLockColor: True
\t\tFaction: russia
\t\tColor: FF4444
\t\tEnemies: USA

Actors:
"""

lines = [header]
lines.append('\t# --- the lineup: 38 actors inheriting ^CivBuilding plus GTWR/PBOX/HBOX ---\n')
lines.append(f'\t# Grid origin ({COLS[0]},{ROWS[0]}); column step {COLS[1] - COLS[0]}, row step {ROWS[1] - ROWS[0]}.\n')
lines.append('\t# Column 1 of every row is the garrisoned example for that row.\n')
for nm, t, owner, x, y in actors:
    lines.append(f'\t{nm}: {t}\n\t\tOwner: {owner}\n\t\tLocation: {x},{y}\n')
lines.append('''\t# Off to the right, clear of the grid and out of every rifle's range.
\tOwnSR: supplyroute
\t\tOwner: USA
\t\tLocation: 90,6
\tOpponentSR: supplyroute
\t\tOwner: Russia
\t\tLocation: 90,52
\tSpawnUSA: mpspawn
\t\tOwner: Neutral
\t\tLocation: 90,6
\tSpawnRussia: mpspawn
\t\tOwner: Neutral
\t\tLocation: 90,52

Rules: rules.yaml
''')

with open(os.path.join(OUT, 'map.yaml'), 'w') as f:
    f.write(''.join(lines))

# ---- the Lua grid table, generated from the same source ----
lua = ['-- GENERATED BLOCK (tools: /tmp/gen-lineup.py, kept in the commit message) --\n']
lua.append('Grid = {\n')
for t, x, y, r, c in placed:
    lua.append(f'\t{{ actor = {gname(t)}, name = "{t.upper()}", x = {x}, y = {y}, row = {r + 1}, col = {c + 1} }},\n')
lua.append('}\n\n')
lua.append('Squads = {\n')
for b, sq in squads.items():
    lua.append(f'\t{{ building = {b}, name = "{b[2:]}", men = {{ {", ".join(sq)} }} }},\n')
lua.append('}\n')

with open('/tmp/lineup-grid.lua', 'w') as f:
    f.write(''.join(lua))

print(f'actors written: {len(actors)}  (buildings {len(placed)})')
print('squad sizes resolved from rules:',
      '  '.join(f"{t.upper()}={g['men']}" for t, g in GARRISON.items()))
print('cols', COLS, 'rows', ROWS)
# cell-collision check
seen = {}
for nm, t, owner, x, y in actors:
    seen.setdefault((x, y), []).append(nm)
dup = {k: v for k, v in seen.items() if len(v) > 1}
print('duplicate cells:', dup if dup else 'none')
oob = [(nm, x, y) for nm, t, o, x, y in actors if not (1 <= x <= 98 and 1 <= y <= 58)]
print('out of bounds:', oob if oob else 'none')
