#!/usr/bin/env python3
"""Before/after render for wt/scar-coverage: the two holes in a blast scar, closed.

Three panels over ONE scene, so the only variable between them is the branch:

    1. `main` @ 44300cb3 -- trees skipped, beach refused, hard cell edge at the water.
    2. + the tree fix    -- 61 Scar* warheads gain `Trees` in ValidTargets.
    3. + the shore work  -- Beach accepts the five Scar types, and SmudgeLayer fades
                            the last two cells before terrain it cannot draw on.

Everything is blendmock's: its scene, its real decoded art, its port of the engine
arithmetic. This file only flips the three gates and adds the fade, each of which maps
to one hook blendmock already exposes -- `ignore_actors`, `ACCEPTS`, `alpha_fn`.

READ tools/scar-blend/README.md ON HOW FAR TO TRUST THIS. Short version: every sprite
is real, the arrangement is a hand port, and nothing checks the port is still in step
with the engine. Specifically NOT reproduced here: the map is
blendmock's synthetic scene rather than a shipped map, so read the SHAPE of the coverage,
not the exact cell count.

    ./tools/scar-blend/prepare-art.sh          # once; writes work/, gitignored
    python tools/scar-blend/coveragemock.py    # writes WORKSPACE/mockups/scar-coverage.png
"""
import math
import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blendmock as bm

FADE_CELLS = 2          # rules/world.yaml ShoreFadeCells on the five Scar layers


def shore_alpha(x, y, accepts):
    """The port of SmudgeLayer.ShoreAlphaAt.

    Chebyshev distance to the nearest cell this layer cannot draw on, ramped over
    FADE_CELLS. Off-scene cells deliberately do NOT count as boundary -- same rule as
    the engine, where the edge of the map is not a shoreline.
    """
    distance = FADE_CELLS + 1
    for dy in range(-FADE_CELLS, FADE_CELLS + 1):
        for dx in range(-FADE_CELLS, FADE_CELLS + 1):
            cx, cy = x + dx, y + dy
            if not (0 <= cx < bm.W and 0 <= cy < bm.H):
                continue
            if accepts[bm.terrain_type(cx, cy)]:
                continue
            distance = min(distance, max(abs(dx), abs(dy)))
    return min(1.0, distance / float(FADE_CELLS + 1))


def panel(ter, art, actors, blocked, *, trees_fixed, beach_fixed, faded):
    saved = dict(bm.ACCEPTS)
    if beach_fixed:
        bm.ACCEPTS["beach"] = True
    try:
        # NOT blendmock's `ignore_actors`, which drops EVERY actor including the building.
        # The branch adds `Trees` to ValidTargets and leaves `InvalidTargets: Vehicle, Structure,
        # Wall` alone, so a building must keep its clean footprint in every panel -- that hole is
        # deliberate (WORKSPACE/reports/scar-blending-260908.md, "the building shadow"). Dropping
        # only the tree cells is the honest model of the change.
        occupied = {c: k for c, k in blocked.items() if not (trees_fixed and k == "tree")}
        plan = bm.smudge_plan(ter, occupied)
        alpha_fn = None
        if faded:
            accepts = dict(bm.ACCEPTS)
            cache = {}

            def alpha_fn(x, y, _a=accepts, _c=cache):
                if (x, y) not in _c:
                    _c[(x, y)] = shore_alpha(x, y, _a)
                return _c[(x, y)]

        base = bm.draw_terrain(art)
        out = bm.render_smudges(base, plan, alpha_fn=alpha_fn)

        # Burnt trees, so the panels show what the player actually sees. ^TreeIndestructible
        # swaps a tree inside the thermal radius to frame 1 of its own shp and washes it with a
        # dark char (rules/ingame/decoration.yaml). Without this the trees render green and the
        # comparison flatters panel 1 -- a green tree over an unscorched cell hides the hole that
        # a black skeleton over the same cell makes obvious.
        def tint(a):
            cx, cy = bm.CENTRE
            x, y = a["cells"][0]
            if a["kind"] != "tree" or math.hypot(x - cx, y - cy) > bm.R:
                return 0, 0.0
            return 1, 0.6

        return bm.draw_actors(out, actors, tint=tint), plan
    finally:
        bm.ACCEPTS.clear()
        bm.ACCEPTS.update(saved)


def mask(plan, beach_fixed):
    """Which cells the warhead actually marked -- the diagnostic the art panels cannot show,
    because a dark smudge over dark grass under a black tree is hard to read at any zoom."""
    im = Image.new("RGB", (bm.W * bm.CELL, bm.H * bm.CELL), (26, 26, 30))
    d = ImageDraw.Draw(im)
    marked = set()
    for cells in plan.values():
        marked |= set(cells)
    for y in range(bm.H):
        for x in range(bm.W):
            t = bm.terrain_type(x, y)
            if (x, y) in marked:
                c = (232, 226, 210)                       # scarred
            elif t == "water":
                c = (40, 62, 104)                         # water: correctly never scarred
            elif math.hypot(x - bm.CENTRE[0], y - bm.CENTRE[1]) <= bm.R:
                c = (198, 66, 52)                         # INSIDE the blast and left unscarred
            else:
                c = (52, 56, 52)                          # outside the blast
            d.rectangle([x * bm.CELL, y * bm.CELL, (x + 1) * bm.CELL - 1, (y + 1) * bm.CELL - 1], fill=c)
    return im


def main():
    ter, art, actors, blocked = bm.build_scene()

    panels = [
        ("1. main @ 44300cb3",
         "every tree cell skipped; scar stops a cell short of the water",
         *panel(ter, art, actors, blocked, trees_fixed=False, beach_fixed=False, faded=False)),
        ("2. + Trees in ValidTargets",
         "the wood is scorched through; the waterline is unchanged",
         *panel(ter, art, actors, blocked, trees_fixed=True, beach_fixed=False, faded=False)),
        ("3. + Beach accepts, + ShoreFadeCells 2",
         "scar reaches the sand and eases off over the last two cells",
         *panel(ter, art, actors, blocked, trees_fixed=True, beach_fixed=True, faded=True)),
    ]

    # Crop to the blast and the shore under it, then double: at 1x a 24 px cell is too small to
    # judge an edge, which is the same reason the demo scenario's MinZoom frame cannot be used.
    CROP = (1 * bm.CELL, 2 * bm.CELL, 22 * bm.CELL, 20 * bm.CELL)
    def shrink(im, f=2):
        return im.crop(CROP).resize(((CROP[2] - CROP[0]) * f, (CROP[3] - CROP[1]) * f), Image.NEAREST)

    rows = [(t, s2, shrink(im), shrink(mask(pl, i == 2).convert("RGBA"), 1))
            for i, (t, s2, im, pl) in enumerate(panels)]
    panels = rows

    pad, top, cap = 16, 34, 42
    pw, ph = panels[0][2].size
    mw, mh = panels[0][3].size
    sheet = Image.new("RGBA", (pad + len(panels) * (pw + pad), top + ph + cap + mh + pad * 2), (22, 22, 26, 255))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 10), "Blast-scar coverage -- wt/scar-coverage. MOCKUP: real art, ported arithmetic, "
                      "NOT an engine screenshot. Top row: the look. Bottom row: which cells took a smudge.",
           fill=(210, 210, 215))
    for i, (title, sub, im, mk) in enumerate(panels):
        x = pad + i * (pw + pad)
        sheet.alpha_composite(im, (x, top))
        d.text((x, top + ph + 4), title, fill=(235, 235, 240))
        d.text((x, top + ph + 17), sub, fill=(150, 150, 160))
        sheet.alpha_composite(mk, (x, top + ph + cap))
    d.text((pad, top + ph + cap + mh + 4),
           "mask key:  pale = cell took a smudge     RED = inside the blast and left unscarred "
           "(the defect)     blue = water, correctly never scarred",
           fill=(150, 150, 160))

    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                       "WORKSPACE", "mockups", "scar-coverage.png")
    out = os.path.normpath(out)
    sheet.convert("RGB").save(out)
    print("wrote", out)


if __name__ == "__main__":
    main()
