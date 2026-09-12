#!/usr/bin/env python3
"""Contact sheet of every diamond variant's two channel ladders, for eyeballing.

Run from the repo root:  python WORKSPACE/mockups/diamond_ladder_sheet.py

This exists to make the "resolves N levels" claims in WORKSPACE/diamond-variants.md
checkable rather than asserted. For each variant it lays out:

  * the FILL ladder  -- detection 1..5, impediment held at 50
  * the COLOUR ladder -- impediment 0..100 in tens, fill held at full

each drawn twice: once at 1x on the real grass tile (the size the claim is about)
and once at 6x nearest-neighbour (so a human can see which steps the 1x strip is
actually collapsing). Reading the 1x strip is the test; the 6x strip is only there
to identify WHICH pair merged.

Writes WORKSPACE/mockups/assets/_diamond-ladders.png.
"""

import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from buymenu_shp_dump import PAL, load_palette  # noqa: E402
from diamond_variants_assets import (  # noqa: E402
    DETS, IMPEDS, TERRAIN_PAL_ALT, VARIANTS, draw, terrain,
)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "assets", "_diamond-ladders.png")

CELLW, CELLH = 18, 20       # 1x cell
BIG = 6                     # zoom for the second strip
GAP, PAD, LABW = 10, 8, 118


def strip(tile, specs, zoom):
    """One horizontal run of diamonds on grass at `zoom`."""
    n = len(specs)
    w, h = CELLW * n, CELLH
    im = Image.new("RGBA", (w, h))
    for i in range(0, w, tile.width):
        for j in range(0, h, tile.height):
            im.paste(tile, (i, j))
    for i, d in enumerate(specs):
        if d is None:
            continue
        im.alpha_composite(d, (i * CELLW + (CELLW - d.width) // 2,
                               (CELLH - d.height) // 2))
    if zoom != 1:
        im = im.resize((im.width * zoom, im.height * zoom), Image.NEAREST)
    return im


def main():
    pal = load_palette(PAL)
    pal_t = load_palette(TERRAIN_PAL_ALT) if os.path.isfile(TERRAIN_PAL_ALT) else pal
    tile, prov = terrain(pal_t)
    if tile is None:
        sys.exit("no terrain tile")
    print("terrain:", prov)

    rows = []
    for spec in VARIANTS:
        fill = [draw(spec, 50, d) for d in DETS]
        col = [draw(spec, i, 5) for i in IMPEDS]
        rows.append((spec["id"], fill, col))

    rowh = CELLH * BIG + CELLH + 6
    width = LABW + PAD * 2 + max(
        CELLW * len(DETS) * BIG + GAP + CELLW * len(IMPEDS) * BIG,
        CELLW * len(DETS) + GAP + CELLW * len(IMPEDS))
    height = PAD * 2 + 16 + len(rows) * (rowh + GAP)
    sheet = Image.new("RGBA", (width, height), (20, 23, 28, 255))
    dr = ImageDraw.Draw(sheet)
    dr.text((PAD, PAD), "fill ladder: detection 1-5 (imped 50)   |   "
                        "colour ladder: impediment 0-100 (fill full)   "
                        "-- top strip is 1x, the size the claim is about",
            fill=(150, 160, 175))

    y = PAD + 16
    for vid, fill, col in rows:
        dr.text((PAD, y + rowh // 2 - 6), f"variant {vid}", fill=(230, 235, 245))
        x = LABW + PAD
        s1 = strip(tile, fill, 1)
        s2 = strip(tile, col, 1)
        sheet.alpha_composite(s1, (x, y))
        sheet.alpha_composite(s2, (x + s1.width + GAP, y))
        b1 = strip(tile, fill, BIG)
        b2 = strip(tile, col, BIG)
        sheet.alpha_composite(b1, (x, y + CELLH + 6))
        sheet.alpha_composite(b2, (x + b1.width + GAP, y + CELLH + 6))
        y += rowh + GAP

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.convert("RGB").save(OUT)
    print("wrote", OUT, sheet.size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
