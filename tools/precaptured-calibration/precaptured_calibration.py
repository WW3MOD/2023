#!/usr/bin/env python3
"""Offline calibrator for the "Pre-captured structures" lobby option.

Answers, WITHOUT building or launching the game: for a given middle-band
threshold X, which Neutral-owned capturable structure on each shipped map would
be handed to which spawn, and which would stay Neutral.

It resolves the mod's MiniYaml inheritance itself (Inherits@ plus -Key removals)
so the capturable set is read off the rules rather than guessed, then reproduces
the trait's distance arithmetic against each map's `mpspawn` actors.

    python tools/precaptured-calibration/precaptured_calibration.py [X_percent]

X defaults to 10, the shipped default of PreCapturedStructuresInfo.MiddleBandPercent.
A structure stays NEUTRAL when the nearest non-allied player's distance is within
X% of the nearest player's. This script assumes a full house of mutual enemies
(FFA) -- which IS the 1v1 case on every two-spawn map, and the widest reading on
the four- and six-spawn ones. Design note and the resulting tables:
WORKSPACE/notes/precaptured-structures-design-260919.md

CAVEAT: the trait anchors on each player's Supply Route, falling back to the
spawn cell. On the shipped maps those coincide EXACTLY -- SpawnStartingUnits
places the SR at `HomeLocation + BaseActorOffset` with BaseActorOffset (-1,-1)
(MapStartingUnits.cs:37) and a 3x3 building's CenterOffset is (+1,+1) cells
(Building.cs:207-211) -- so the spawn cell used here IS the SR's CenterPosition.
Do not carry that coincidence over to a map that overrides BaseActorOffset.
"""

import glob
import math
import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
MOD = os.path.join(ROOT, 'mods', 'ww3mod')


# ---------------------------------------------------------------- MiniYaml ---

def _rules_files():
    files, inside = [], False
    for line in open(os.path.join(MOD, 'mod.yaml'), encoding='utf-8').read().splitlines():
        if line.startswith('Rules:'):
            inside = True
            continue
        if inside:
            if not line.startswith('\t'):
                break
            files.append(line.strip().split('|', 1)[1])
    return files


def _parse(path):
    """Tab-indented MiniYaml -> nested [(text, children)]."""
    rows = []
    for raw in open(path, encoding='utf-8').read().splitlines():
        if raw.strip() == '' or raw.lstrip().startswith('#'):
            continue
        rows.append((len(raw) - len(raw.lstrip('\t')), raw.strip()))

    root = []
    stack = [(-1, root)]
    for indent, text in rows:
        while stack and stack[-1][0] >= indent:
            stack.pop()
        node = (text, [])
        stack[-1][1].append(node)
        stack.append((indent, node[1]))
    return root


DEFS = {}
for _f in _rules_files():
    for _key, _children in _parse(os.path.join(MOD, _f)):
        DEFS.setdefault(_key.rstrip(':'), []).extend(_children)

_TRAITS = {}


def traits(name):
    """Resolved trait keys (with @suffix) for an actor or ^Template."""
    if name in _TRAITS:
        return _TRAITS[name]
    _TRAITS[name] = set()  # cycle guard
    acc = []
    for child, _gc in DEFS.get(name, []):
        key = child.rstrip(':').strip()
        head = key.split(':', 1)[0].strip()
        if head.startswith('Inherits'):
            if ':' in key:
                for t in traits(key.split(':', 1)[1].strip()):
                    if t not in acc:
                        acc.append(t)
        elif head.startswith('-'):
            if head[1:] in acc:
                acc.remove(head[1:])
        elif head not in acc:
            acc.append(head)
    _TRAITS[name] = set(acc)
    return _TRAITS[name]


def has(name, trait):
    return any(t == trait or t.startswith(trait + '@') for t in traits(name))


_CAP = {}


def capturable_structure(actor_type):
    """(is_in_scope, canonical_key). Mirrors the trait's own three filters."""
    up = actor_type.upper()
    if up not in _CAP:
        key = next((k for k in DEFS if k.upper() == up), None)
        ok = bool(key) and has(key, 'Capturable') and has(key, 'Building') and has(key, 'Selectable')
        _CAP[up] = (ok, key)
    return _CAP[up]


def _footprint(key):
    """(dimensions, LocalCenterOffset-in-cells) for a resolved actor."""
    state = {'dims': (1, 1), 'off': (0.0, 0.0)}
    seen = set()

    def walk(name):
        if name in seen:
            return
        seen.add(name)
        parents, own = [], []
        for child, gc in DEFS.get(name, []):
            k = child.rstrip(':').strip()
            head = k.split(':', 1)[0].strip()
            if head.startswith('Inherits'):
                parents.append(k.split(':', 1)[1].strip())
            elif head == 'Building':
                own.append(gc)
        for p in parents:
            walk(p)
        for gc in own:
            for g, _ in gc:
                m = re.match(r'Dimensions:\s*(\d+)\s*,\s*(\d+)', g)
                if m:
                    state['dims'] = (int(m.group(1)), int(m.group(2)))
                m = re.match(r'LocalCenterOffset:\s*(-?\d+)\s*,\s*(-?\d+)', g)
                if m:
                    state['off'] = (int(m.group(1)) / 1024.0, int(m.group(2)) / 1024.0)

    walk(key)
    return state['dims'], state['off']


def centre(key, loc):
    dims, off = _footprint(key)
    return (loc[0] + (dims[0] - 1) / 2.0 + off[0], loc[1] + (dims[1] - 1) / 2.0 + off[1])


# -------------------------------------------------------------------- maps ---

def read_map(path):
    spawns, caps = [], []
    actor_type = owner = None
    for line in open(path, encoding='utf-8').read().splitlines():
        m = re.match(r'^\t(Actor\w+): (\S+)\s*$', line)
        if m:
            actor_type, owner = m.group(2).lower(), None
            continue
        m = re.match(r'^\t\tOwner: (\S+)', line)
        if m:
            owner = m.group(1)
            continue
        m = re.match(r'^\t\tLocation: (\d+),(\d+)', line)
        if m and actor_type:
            loc = (int(m.group(1)), int(m.group(2)))
            if actor_type == 'mpspawn':
                spawns.append(loc)
            elif owner == 'Neutral':
                ok, key = capturable_structure(actor_type)
                if ok:
                    caps.append((actor_type, key, loc))
    return spawns, caps


def main():
    threshold = (float(sys.argv[1]) if len(sys.argv) > 1 else 10.0) / 100.0
    print('middle band = {:.1f}%   (FFA: every spawn a mutual enemy)'.format(threshold * 100))

    for d in sorted(glob.glob(os.path.join(MOD, 'maps', '*', ''))):
        name = os.path.basename(os.path.normpath(d))
        spawns, caps = read_map(os.path.join(d, 'map.yaml'))
        print('\n### {}   spawns={}'.format(name, spawns))
        if not spawns:
            print('    (no spawn points -- the option is a no-op here)')
            continue
        if not caps:
            print('    (no Neutral capturable structures)')
            continue
        rows = []
        for actor_type, key, loc in caps:
            cx, cy = centre(key, loc)
            ranked = sorted((math.hypot(cx - sx, cy - sy), i) for i, (sx, sy) in enumerate(spawns))
            near, ni = ranked[0]
            second = ranked[1][0]
            margin = (second / near - 1.0) if near > 0 else 99.0
            rows.append((actor_type, loc, near, second, margin * 100,
                         'NEUTRAL' if margin <= threshold else 'spawn{}'.format(ni)))
        for r in sorted(rows, key=lambda row: (row[0], row[1])):
            print('    {:<16} at {:<10} near={:>6.1f}  2nd={:>6.1f}  margin={:>7.1f}%  -> {}'
                  .format(r[0], str(r[1]), r[2], r[3], r[4], r[5]))


if __name__ == '__main__':
    main()
