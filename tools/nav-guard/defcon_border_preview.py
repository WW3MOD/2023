#!/usr/bin/env python3
"""Render a map with its DEFCON 3 border painted on, so the band can be LOOKED at.

The audit answers "does it separate"; it cannot answer "does this read as a border a player
would recognise", and that second question is the whole point of authoring a region instead
of taking the derived bisector. This renders the decoded terrain in the tileset's own
preview colours -- the same colours `nav_guard.py validate` checks against the engine's
`map.png` -- with the border cells washed in the trait's own `BandColor` amber, spawns and
Supply Routes marked, and capturable structures ringed so it is obvious which side each
fell on.

Reads the authored region out of the map's own rules.yaml via `defcon_wall_audit`'s reader,
so what it draws is what the engine will load, not what the designer script proposed.

    python defcon_border_preview.py --out WORKSPACE/mockups/borders
    python defcon_border_preview.py --map x-lake --scale 6

Requires Pillow, like `nav_guard.py validate` and for the same reason -- it is a rendering
tool and is not on any gate's path.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import modload  # noqa: E402
import nav_guard  # noqa: E402
from defcon_wall_audit import region_from_map  # noqa: E402

# DefconWallInfo.BandColor, flattened against the terrain rather than alpha-blended per pixel.
BAND = (255, 96, 48)
BAND_ALPHA = 0.55
SPAWN = (255, 255, 255)
SR = (40, 220, 255)
CAP = (255, 230, 60)
OUTSIDE = (24, 24, 28)

CAPNAMES = ('oilb', 'logisticscenter', 'fcom', 'miss', 'hosp', 'bio', 'gun', 'mslo',
            'afld', 'hpad', 'agun', 'sam', 'hsam', 'cram', 'ftur')


def render(rules, game_map, scale: int, out_dir: Path) -> Path:
    from PIL import Image, ImageDraw

    tileset = rules.tilesets[game_map.tileset]
    w, h = game_map.width, game_map.height
    img = Image.new("RGB", (w, h), OUTSIDE)
    px = img.load()

    left, top, bw, bh = game_map.bounds
    for y in range(h):
        for x in range(w):
            t = game_map.terrain_type(tileset, x, y)
            c = tileset.type_color.get(t, (90, 90, 90))
            if not (left <= x < left + bw and top <= y < top + bh):
                # The cordon outside Bounds is not playable and the border never reaches it;
                # dimming it stops a reader mistaking the ring for part of the band.
                c = tuple(v // 3 for v in c)
            px[x, y] = c

    blocked, types, cells = region_from_map(game_map)
    for (x, y) in blocked:
        if 0 <= x < w and 0 <= y < h:
            r, g, b = px[x, y]
            px[x, y] = (int(r * (1 - BAND_ALPHA) + BAND[0] * BAND_ALPHA),
                        int(g * (1 - BAND_ALPHA) + BAND[1] * BAND_ALPHA),
                        int(b * (1 - BAND_ALPHA) + BAND[2] * BAND_ALPHA))

    img = img.resize((w * scale, h * scale), Image.NEAREST)
    d = ImageDraw.Draw(img)

    def box(cx, cy, colour, pad=0):
        d.rectangle([cx * scale - pad, cy * scale - pad,
                     (cx + 1) * scale - 1 + pad, (cy + 1) * scale - 1 + pad],
                    outline=colour, width=max(1, scale // 3))

    for a in game_map.actors:
        if a.name == 'mpspawn':
            box(*a.location, SPAWN, pad=scale)
            # SpawnStartingUnits places the base actor at HomeLocation + CVec(-1,-1), so the
            # Supply Route is one cell up-left of the spawn -- and on these maps that can put
            # it OUTSIDE Bounds, where DefconWallRegion.IndexOf returns -1.
            box(a.location[0] - 1, a.location[1] - 1, SR)
        elif a.name in CAPNAMES:
            box(*a.location, CAP)

    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / f"{game_map.name}-defcon3-border.png"
    img.save(path)
    print(f"{game_map.name}: {len(blocked)} border cell(s) "
          f"(types {types or 'none'}, {len(cells)} authored) -> {path}")
    return path


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--map", action="append", default=None)
    ap.add_argument("--scale", type=int, default=5)
    ap.add_argument("--out", default="WORKSPACE/mockups/borders")
    args = ap.parse_args(argv)

    rules = modload.load_mod(nav_guard.MOD_DIR)
    maps = [modload.load_map(p) for p in modload.discover_maps(nav_guard.MOD_DIR)]
    if args.map:
        maps = [m for m in maps if any(f in m.name for f in args.map)]

    out = Path(args.out)
    if not out.is_absolute():
        out = nav_guard.REPO / out

    drawn = 0
    for game_map in maps:
        blocked, _, _ = region_from_map(game_map)
        if not blocked:
            print(f"{game_map.name}: no authored region -- skipped")
            continue
        render(rules, game_map, args.scale, out)
        drawn += 1

    print(f"{drawn} map(s) rendered.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
