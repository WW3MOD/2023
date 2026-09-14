#!/usr/bin/env python3
"""Contact sheet for every true-glyph variant, so the level counts are checked.

Run from the repo root:  python WORKSPACE/mockups/diamond_true_glyph_sheet.py

This exists for the same reason diamond_ladder_sheet.py did: the "resolves N
levels" claims in WORKSPACE/diamond-true-glyph.md have to be READ OFF A RENDER,
not asserted. Last round five of fourteen counts moved after looking.

Three strips per variant:
  * PROOF   -- the shipped mark's five grades, then the variant's five detection
               states. Same row, same ground. This is what makes "the silhouette
               is preserved" checkable instead of a promise.
  * FILL    -- detection 1..5, impediment held at 50.
  * COLOUR  -- impediment 0..100 in tens, detection held at 5.

Each drawn at 1x (the size the claim is about) and at 6x nearest-neighbour (only
to identify WHICH pair merged). Reading the 1x strip is the test.

Writes WORKSPACE/mockups/assets/_true-glyph-ladders.png
   and WORKSPACE/mockups/assets/_true-glyph-proof.png
"""

import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from buymenu_shp_dump import PAL, load_palette  # noqa: E402
from diamond_true_glyph_assets import (  # noqa: E402
    DET_NAMES, DETS, IMPEDS, TERRAIN_PAL_ALT, VARIANTS, draw, shipped, terrain,
)

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "assets")
LADDERS = os.path.join(ASSETS, "_true-glyph-ladders.png")
PROOF = os.path.join(ASSETS, "_true-glyph-proof.png")

CELLW, CELLH = 18, 20
BIG = 6
GAP, PAD, LABW = 12, 8, 132


def strip(tile, marks, zoom):
    n = len(marks)
    w, h = CELLW * n, CELLH
    im = Image.new("RGBA", (w, h))
    for i in range(0, w, tile.width):
        for j in range(0, h, tile.height):
            im.paste(tile, (i, j))
    for i, d in enumerate(marks):
        if d is None:
            continue
        im.alpha_composite(d, (i * CELLW + (CELLW - d.width) // 2,
                               (CELLH - d.height) // 2))
    if zoom != 1:
        im = im.resize((im.width * zoom, im.height * zoom), Image.NEAREST)
    return im


def sheet(rows, header, out, pal_t):
    tile, prov = terrain(pal_t)
    if tile is None:
        sys.exit("no terrain tile")
    rowh = CELLH * BIG + CELLH + 6
    width = LABW + PAD * 2 + max(
        sum(CELLW * len(m) for _, m in [(0, r[1])]) * BIG for r in rows) + 40
    width = max(width, LABW + PAD * 2 + max(CELLW * len(r[1]) for r in rows) * BIG + 40)
    height = PAD * 2 + 18 + len(rows) * (rowh + GAP)
    im = Image.new("RGBA", (width, height), (20, 23, 28, 255))
    dr = ImageDraw.Draw(im)
    dr.text((PAD, PAD), header, fill=(150, 160, 175))
    y = PAD + 18
    for label, marks in rows:
        dr.text((PAD, y + rowh // 2 - 6), label, fill=(230, 235, 245))
        x = LABW + PAD
        im.alpha_composite(strip(tile, marks, 1), (x, y))
        im.alpha_composite(strip(tile, marks, BIG), (x, y + CELLH + 6))
        y += rowh + GAP
    os.makedirs(ASSETS, exist_ok=True)
    im.convert("RGB").save(out)
    print(f"wrote {out} {im.size}")
    return prov


def main():
    pal = load_palette(PAL)
    pal_t = load_palette(TERRAIN_PAL_ALT) if os.path.isfile(TERRAIN_PAL_ALT) else pal

    ship = [shipped(g) for g in DETS]

    # --- proof sheet: shipped five, then each variant's five, same ground.
    rows = [("SHIPPED today", ship)]
    for spec in VARIANTS:
        rows.append((f"{spec['id']}", [draw(spec, 50, d) for d in DETS]))
    prov = sheet(rows, "PROOF STRIP -- row 1 is the mark AS IT SHIPS (U+25CA for "
                       "Concealed/Low, U+2666 for Moderate/High/Spotted). Every row "
                       "below is the same glyph, filled. Detection 1-5, impediment 50.",
                 PROOF, pal_t)
    print("terrain:", prov)

    # --- ladder sheet: both channels per variant.
    rows = []
    for spec in VARIANTS:
        rows.append((f"{spec['id']} fill", [draw(spec, 50, d) for d in DETS]))
        rows.append((f"{spec['id']} colour", [draw(spec, i, 5) for i in IMPEDS]))
    sheet(rows, "FILL ladder: detection 1-5 (impediment 50).   COLOUR ladder: "
                "impediment 0-100 (detection 5).   Top strip is 1x -- judge there.",
          LADDERS, pal_t)

    print("\ndetection grades, in order: " + ", ".join(
        f"{i+1}={n}" for i, n in enumerate(DET_NAMES)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
