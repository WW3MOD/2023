#!/usr/bin/env python3
"""Render the standardised nuclear cameo badge -- a radiation trefoil -- as an RGBA PNG.

The badge is drawn by the sidebar ON TOP of whatever cameo a power already uses
(see engine/OpenRA.Mods.Common/Widgets/CameoCaptions.cs), so it can never assume
anything about the pixels underneath.  That is why it is a FILLED YELLOW DISC with a
dark ring rather than a bare symbol: a flat-coloured trefoil disappears into any
cameo region that happens to share its brightness, and the mod's cameos are cropped
photographs whose local brightness varies wildly.  The disc carries its own backing,
so the contrast problem is solved once here instead of per-cameo forever.

Geometry is ISO 361 in outline -- a hub and three 60-degree blades with 60-degree
gaps -- but NOT in its ratios; see trefoil() for why the published ones cannot be
rendered at this size.  Supersampled 16x and downsampled, because the whole question
here is whether the three gaps survive.

    python3 tools/cameo/badge.py --install          # write the shipped badge
    python3 tools/cameo/badge.py --sheet out.png    # legibility contact sheet
"""

import argparse
import math
import os
import sys

from PIL import Image, ImageDraw

SS = 16  # supersample factor

# Black on yellow is the real-world radiation signifier and the highest-contrast pair
# available; the ring keeps the disc off a pale cameo.
YELLOW = (250, 204, 21, 255)
INK = (12, 12, 12, 255)
RING = (12, 12, 12, 255)


def trefoil(size, ring=0.8, hub=0.26, inner=0.50, outer=0.94):
    """An RGBA badge `size` px square: dark ring, yellow disc, black trefoil.

    NOT the ISO 361 ratios.  ISO puts the blades between 1.5r and 5r of a hub of
    radius r, which makes the hub/blade gap 0.5r = 0.09 of the disc radius -- 0.8 px
    on a 20 px badge, so the hub fuses to the blades at every size a cameo can carry
    and the symbol degrades to a lumpy dot.  The ratios here hold that gap at about a
    quarter of the disc radius instead, which is what keeps four separate pieces of
    ink -- hub plus three blades -- resolvable at 13 px.  Orientation is the
    conventional one: one blade down, two up.
    """
    n = size * SS
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = n / 2.0

    d.ellipse([0, 0, n - 1, n - 1], fill=RING)
    r = n / 2.0 - ring * SS
    d.ellipse([c - r, c - r, c + r, c + r], fill=YELLOW)

    for a in (90, 210, 330):
        d.pieslice([c - outer * r, c - outer * r, c + outer * r, c + outer * r], a - 30, a + 30, fill=INK)
    d.ellipse([c - inner * r, c - inner * r, c + inner * r, c + inner * r], fill=YELLOW)
    d.ellipse([c - hub * r, c - hub * r, c + hub * r, c + hub * r], fill=INK)

    return img.resize((size, size), Image.LANCZOS)


def sheet(path, sizes):
    """Every candidate size side by side on the three backgrounds the badge must survive:
    the caption band's solid black, a bright sky, and a mid grey."""
    pad, cell = 6, max(sizes) + 6
    bgs = [(0, 0, 0, 255), (206, 219, 235, 255), (110, 110, 110, 255)]
    w = pad + len(sizes) * (cell + pad)
    h = pad + len(bgs) * (cell + pad) + 14
    img = Image.new("RGBA", (w, h), (30, 30, 34, 255))
    d = ImageDraw.Draw(img)
    for row, bg in enumerate(bgs):
        y = pad + row * (cell + pad)
        for col, s in enumerate(sizes):
            x = pad + col * (cell + pad)
            d.rectangle([x, y, x + cell - 1, y + cell - 1], fill=bg)
            img.alpha_composite(trefoil(s), (x + (cell - s) // 2, y + (cell - s) // 2))
    for col, s in enumerate(sizes):
        d.text((pad + col * (cell + pad) + 2, h - 12), str(s), fill=(230, 230, 230, 255))
    img.resize((w * 4, h * 4), Image.NEAREST).save(path)
    print(f"wrote {path} ({w}x{h}, shown at 4x)")


def main():
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    p = argparse.ArgumentParser()
    p.add_argument("--size", type=int, default=13)
    p.add_argument("--install", action="store_true")
    p.add_argument("--sheet")
    p.add_argument("--out")
    a = p.parse_args()

    if a.sheet:
        sheet(a.sheet, [8, 9, 10, 11, 12, 13, 14, 16])
        return 0

    img = trefoil(a.size)
    out = a.out or (os.path.join(root, "mods/ww3mod/bits/misc/ui/nukebadge.shp")
                    if a.install else "nukebadge.png")
    img.save(out, "PNG")
    print(f"wrote {out} ({a.size}x{a.size} RGBA PNG)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
