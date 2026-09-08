#!/usr/bin/env python3
"""Render the scar-blending options sheet offline: the shipped smudge look beside
candidate treatments, over real decoded terrain art including a beach edge.

THIS IS A PYTHON COMPOSITE, NOT AN ENGINE SCREENSHOT. No renderer is involved and
no game is launched. Every sprite is real -- terrain templates, trees, the civilian
building and the five scar bands are decoded from the files that ship in this
branch -- but the *arrangement* is a hand port of the engine's own arithmetic, and
it is only as true as that port. What is ported, and where from:

  * cell membership in a band uses ceil(sqrt(dx^2+dy^2)), which is exactly how
    MapGrid.CreateTilesByDistance buckets offsets (MapGrid.cs:201-210), so the
    annuli here are the annuli the engine draws
  * a `Size: a, b` warhead is the annulus b..a and a bare `Size: a` is the FILLED
    disc 0..a (LeaveSmudgeWarhead.cs:53-54)
  * a cell is skipped when its terrain type does not list the smudge type in
    AcceptsSmudgeType (LeaveSmudgeWarhead.cs:59-61) -- Beach and Water list none
    (tilesets/temperat.yaml:60-68)
  * a cell is skipped when a blocking actor on it is not a valid target for the
    warhead (LeaveSmudgeWarhead.cs:67-68). Trees are Targetable TargetTypes:
    Trees, which does not overlap the warhead default ValidTargets: Ground, Water,
    so every tree cell is skipped; civilian buildings carry Structure, which the
    scar warheads name in InvalidTargets.
  * variant choice per new smudge is random over the sequence's four images
    (SmudgeLayer.cs:176); depth stays 0 because annuli never overlap themselves
  * layer draw order is the world.yaml order, which is the Z order
    (SmudgeLayer.cs:30, rules/world.yaml:474-508)

WHAT IS APPROXIMATE, and deliberately so: the anchoring of the tree and building
sprites over their footprint cells is taken as "sprite box == footprint box",
which is right for these actors but is not a port of RenderSprites. The smudge
itself is exact; the decoration around it may be a pixel or two off. Nothing in
the comparison turns on it.

    python tools/scar-blend/blendmock.py
        -> WORKSPACE/mockups/scar-blending-options.png

Art is read from tools/scar-blend/work/art, which is NOT committed -- see
prepare-art.sh for how to regenerate it from the installed RA content.
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(HERE, "work", "art")
SCARS = os.path.join(REPO, "mods/ww3mod/bits/misc/scars")

sys.path.insert(0, os.path.join(REPO, "tools", "impact-scar"))
import racontent as rc          # noqa: E402
import gen_scars as gs          # noqa: E402

CELL = 24

# NukeW76 (100 kt), rules/weapons/weapons-nuclear-arsenal.yaml:1649. Its five
# LeaveSmudge warheads, verbatim, as (type, inner, outer):
#   Scar1Core   Size: 1      -> filled disc 0..1
#   Scar2Crater Size: 3, 2   -> annulus   2..3
#   Scar3Char   Size: 5, 4   -> annulus   4..5
#   Scar4Burn   Size: 7, 6   -> annulus   6..7
#   Scar5Rim    Size: 9, 8   -> annulus   8..9
WEAPON = "NukeW76"
BANDS = [("ScarCore", 0, 1), ("ScarCrater", 2, 3), ("ScarChar", 4, 5),
         ("ScarBurn", 6, 7), ("ScarRim", 8, 9)]
R = 9

# rules/world.yaml:484-508, lightest first, so the darkest draws last and wins.
LAYER_ORDER = ["ScarRim", "ScarBurn", "ScarChar", "ScarCrater", "ScarCore"]
VARIANTS = {"ScarCore": "bza", "ScarCrater": "bzb", "ScarChar": "bzc",
            "ScarBurn": "bzd", "ScarRim": "bze"}

# tilesets/temperat.yaml: Clear lists every Scar* type in AcceptsSmudgeType;
# Beach and Water list none at all.
ACCEPTS = {"clear": True, "beach": False, "water": False}


# ---------------------------------------------------------------- art loading
_pal = None
_cache = {}


def pal():
    global _pal
    if _pal is None:
        p = os.path.join(REPO, "tools/impact-scar/pal/temperat.pal")
        if not os.path.exists(p):
            raise SystemExit("missing " + p + " -- run tools/impact-scar/extract-palettes.sh")
        _pal = rc.read_pal(open(p, "rb").read())
    return _pal


def tiles(prefix):
    """Frames of an extracted template/sprite, as RGBA.

    `--png` writes the shadow indices out as their literal palette colours, which
    on temperate are a vivid green. The `terrain` palette these sprites draw
    through declares `ShadowIndex: 3, 4` (rules/palettes.yaml:40-44, and the same
    for the other three tilesets), and ImmutablePalette maps every listed index to
    `140u << 24` -- transparent black at alpha 140 (Palette.cs:99). Undo it here or
    every tree wears a green skirt.

    Index 3 is handled for correctness, not because it is needed: it occurs zero
    times across all 126 extracted frames, where index 4 occurs 3824 times.
    """
    if prefix not in _cache:
        shadows = {pal()[3] + (255,), pal()[4] + (255,)}
        out, i = [], 0
        while True:
            p = os.path.join(ART, "%s-%04d.png" % (prefix, i))
            if not os.path.exists(p):
                break
            im = Image.open(p).convert("RGBA")
            im.putdata([(0, 0, 0, 140) if px in shadows else px for px in im.get_flattened_data()])
            out.append(im)
            i += 1
        if not out:
            raise SystemExit("no art for " + prefix + " in " + ART + " -- run prepare-art.sh")
        _cache[prefix] = out
    return _cache[prefix]


def scar(band, variant, depth=0):
    """One shipped scar band frame, decoded straight from the .tem SHP."""
    name = VARIANTS[band] + str(variant + 1)
    key = ("scar", name, depth)
    if key not in _cache:
        data = open(os.path.join(SCARS, name + ".tem"), "rb").read()
        w, h, frames = rc.read_shp(data)
        im = Image.new("RGBA", (w, h))
        im.putdata([(0, 0, 0, 0) if v == 0 else pal()[v] + (255,) for v in frames[depth]])
        _cache[key] = im
    return _cache[key]


def stock(name):
    """A stock RA smudge frame (cr*/sc*), straight out of temperate.mix. These are
    what every nuclear weapon drew before 0793564f, and what every NON-nuclear
    weapon still draws today."""
    key = ("stock", name)
    if key not in _cache:
        content = os.path.expanduser("~/AppData/Roaming/OpenRA/Content/ra/v2")
        data = rc.MixFile(os.path.join(content, "temperat.mix")).get(name + ".tem")
        if data is None:
            raise SystemExit("missing stock smudge " + name)
        w, h, frames = rc.read_shp(data)
        im = Image.new("RGBA", (w, h))
        im.putdata([(0, 0, 0, 0) if v == 0 else pal()[v] + (255,) for v in frames[0]])
        _cache[key] = im
    return _cache[key]


# NukeW76 BEFORE 0793564f (merged 2026-09-08 03:35): three LeaveSmudge warheads
# whose Size was a single number, and a single number is a FILLED DISC
# (LeaveSmudgeWarhead.cs:53). Crater 3, Scorch1 5, Scorch2 9 -- all nested, all
# from cell 0. Both scorch warheads write the SAME layer, and AddSmudge answers a
# repeat hit by deepening rather than adding (SmudgeLayer.cs:181-188); stock
# sc*.tem has one frame, so Scorch2 does nothing to the cells Scorch1 already
# took. The result is one flat tone out to r=9 with a cell-quantised rim.
OLD_BANDS = [("Scorch", ["sc1", "sc2", "sc3", "sc4", "sc5", "sc6"], 9),
             ("Crater", ["cr1", "cr2", "cr3", "cr4", "cr5", "cr6"], 3)]


# ---------------------------------------------------------------- the scene
W, H = 23, 21
CENTRE = (11, 10)

# The coastline is laid the way a mapper lays one: whole 3x3 Beach TEMPLATES,
# not shuffled tiles. Template@6 (sh04.tem), @8 (sh06) and @9 (sh07) all have the
# same shape -- tiles 0-5 Beach over tiles 6-8 Water (tilesets/temperat.yaml:
# 2246-2302) -- so a run of them gives two rows of sand with the template's own
# surf line beneath, and w1.tem fills the open water past that. The step at
# x = 12 is there because a straight coast would beg the question.
SHORE_TOP = {0: 15, 3: 15, 6: 15, 9: 15, 12: 16, 15: 16, 18: 16, 21: 16}

TREE_ART = ["t01", "t02", "t05", "t03", "t06", "t02"]


def forest(seed=3, density=0.42):
    """A real forest, not a scattering.

    The report this illustrates is about a nuke dropped on woodland, and tree
    DENSITY is not a cosmetic choice here: every tree cell is a cell the warhead
    skips, so the denser the wood the more the scar becomes a patchwork of
    survivors rather than a disc. At 22 trees the effect is a curiosity; at forest
    density it is the dominant artefact, which is what the screenshot shows.
    Clumped rather than uniform, because RA forests are placed in stands.
    """
    rng = random.Random(seed)
    cells = set()
    for _ in range(14):                       # stand centres
        sx, sy = rng.randrange(1, W - 1), rng.randrange(1, H - 6)
        for _ in range(int(density * 26)):
            x = sx + rng.randint(-3, 3)
            y = sy + rng.randint(-2, 2)
            if 0 < x < W - 1 and 0 < y < H and terrain_type(x, y) == "clear":
                cells.add((x, y))
    return sorted(cells)
BUILDING = (13, 6)          # V01, Footprint "xx xx" -> four cells from here


def shore(seed):
    """Place the beach templates; returns per-cell art and per-cell terrain type
    for every cell the templates cover."""
    rng = random.Random(seed)
    art, ter, top = {}, {}, {}
    for tx in range(0, W, 3):
        ty = SHORE_TOP[tx]
        frames = tiles(rng.choice(["sh04", "sh06", "sh07"]))
        for c in range(3):
            top[tx + c] = ty
            for r in range(3):
                x, y = tx + c, ty + r
                if 0 <= x < W and 0 <= y < H:
                    art[(x, y)] = frames[r * 3 + c]
                    ter[(x, y)] = "beach" if r < 2 else "water"
    return art, ter, top


_shore = None


def shore_cached(seed=7):
    global _shore
    if _shore is None:
        _shore = shore(seed)
    return _shore


def terrain_type(x, y):
    sart, ster, top = shore_cached()
    if (x, y) in ster:
        return ster[(x, y)]
    return "clear" if y < top[x] else "water"


def build_scene(seed=7):
    """Terrain grid + the occupancy map the warhead consults."""
    rng = random.Random(seed)
    sart, _ster, _top = shore_cached(seed)
    ter = [[terrain_type(x, y) for x in range(W)] for y in range(H)]

    art = {}
    for y in range(H):
        for x in range(W):
            if (x, y) in sart:
                art[(x, y)] = sart[(x, y)]
            elif ter[y][x] == "clear":
                art[(x, y)] = rng.choice(tiles("clear1"))
            else:
                art[(x, y)] = tiles("w1")[0]

    # Actors. `kind` is what decides the smudge, not the sprite.
    actors = []
    for i, (x, y) in enumerate(forest()):
        if terrain_type(x, y) != "clear":
            continue
        actors.append({"kind": "tree", "img": TREE_ART[i % len(TREE_ART)],
                       # T## is Footprint "__ x_" Dimensions 2,2: one occupied
                       # cell, at the bottom-left of a 2x2 sprite box.
                       "cells": [(x, y)], "box": (x, y - 1, 2, 2)})
    bx, by = BUILDING
    actors.append({"kind": "building", "img": "v01",
                   "cells": [(bx, by), (bx + 1, by), (bx, by + 1), (bx + 1, by + 1)],
                   "box": (bx, by, 2, 2)})

    blocked = {}
    for a in actors:
        for c in a["cells"]:
            blocked[c] = a["kind"]
    return ter, art, actors, blocked


def draw_terrain(art):
    bg = Image.new("RGBA", (W * CELL, H * CELL), (0, 0, 0, 255))
    for (x, y), im in art.items():
        bg.alpha_composite(im, (x * CELL, y * CELL))
    return bg


def draw_actors(base, actors, tint=None):
    """Trees and the building, over whatever is already composited.

    `tint` is a callback (cell) -> (frame_index, scorch_strength). It is how the
    'draw over actors' treatments get their effect; None is the shipped look.
    """
    out = base.copy()
    for a in actors:
        frame, scorch = 0, 0.0
        if tint is not None:
            frame, scorch = tint(a)
        frames = tiles(a["img"])
        sp = frames[min(frame, len(frames) - 1)].copy()
        if scorch > 0:
            px = sp.load()
            for j in range(sp.height):
                for i in range(sp.width):
                    r, g, b, al = px[i, j]
                    if al == 0:
                        continue
                    k = 1.0 - scorch
                    px[i, j] = (int(r * k + 26 * scorch), int(g * k + 20 * scorch),
                                int(b * k + 18 * scorch), al)
        bx, by, _bw, _bh = a["box"]
        out.alpha_composite(sp, (bx * CELL, by * CELL))
    return out


# ---------------------------------------------------------------- the warhead
def ring(inner, outer):
    """The cells LeaveSmudgeWarhead gets back from Map.FindTilesInAnnulus.

    MapGrid buckets an offset by the CEILING integer sqrt of its squared length
    (MapGrid.cs:209), so membership of band r is exactly ceil(sqrt(dx^2+dy^2)) == r.
    """
    for dy in range(-outer, outer + 1):
        for dx in range(-outer, outer + 1):
            d2 = dx * dx + dy * dy
            r = math.isqrt(d2)
            if r * r < d2:
                r += 1
            if inner <= r <= outer:
                yield dx, dy


def smudge_plan(ter, blocked, seed=11, ignore_actors=False):
    """What the five LeaveSmudge warheads of NukeW76 actually leave on this map.

    The variant RNG is a stand-in: the engine draws from Game.CosmeticRandom in
    impact order (SmudgeLayer.cs:176), which is not reproducible here. Which of
    the four images a cell gets is cosmetic; which cells exist is not, and that
    part is exact.
    """
    rng = random.Random(seed)
    cx, cy = CENTRE
    plan = {}
    for name, inner, outer in BANDS:
        for dx, dy in ring(inner, outer):
            x, y = cx + dx, cy + dy
            if not (0 <= x < W and 0 <= y < H):
                continue
            if not ACCEPTS[ter[y][x]]:                  # LeaveSmudgeWarhead.cs:59-61
                continue
            if not ignore_actors and (x, y) in blocked:  # LeaveSmudgeWarhead.cs:67-68
                continue
            plan.setdefault(name, {})[(x, y)] = rng.randrange(4)
    return plan


def render_old(base, ter, blocked, seed=13):
    """The pre-0793564f look, same weapon, same footprint. Draw order is the
    world.yaml layer order, SCORCH before CRATER (rules/world.yaml:459-469)."""
    rng = random.Random(seed)
    cx, cy = CENTRE
    out = base.copy()
    for _layer, names, outer in OLD_BANDS:
        for dx, dy in ring(0, outer):
            x, y = cx + dx, cy + dy
            if not (0 <= x < W and 0 <= y < H):
                continue
            if not ACCEPTS[ter[y][x]] or (x, y) in blocked:
                continue
            sp = stock(rng.choice(names))
            out.alpha_composite(sp, (x * CELL + (CELL - sp.width) // 2,
                                     y * CELL + (CELL - sp.height) // 2))
    return out


def radial(x, y):
    """True (unquantised) distance from the blast centre, in cells."""
    cx, cy = CENTRE
    return math.hypot(x - cx, y - cy)


def falloff(x, y, start=0.55):
    """Smooth 1 -> 0 ramp over the outer part of the blast, on true distance."""
    u = radial(x, y) / R
    if u <= start:
        return 1.0
    t = min(1.0, (u - start) / (1.0 - start + 0.11))
    return 1.0 - t * t * (3 - 2 * t)


# ---------------------------------------------------------------- treatments
def render_smudges(base, plan, alpha_fn=None, jitter=0, seed=5):
    """Composite the per-cell smudge sprites, optionally with per-cell alpha and
    per-cell placement jitter.

    Per-cell alpha is NOT invented for this mockup: TerrainSpriteLayer.Update
    already takes an `alpha` and multiplies it into the vertex tint
    (TerrainSpriteLayer.cs:168-195), and the fragment shader applies that tint to
    paletted sprites too (`c *= vTint`, glsl/combined.frag:216-220). SmudgeLayer
    simply passes the sequence-wide value instead of a per-cell one
    (SmudgeLayer.cs:149, :221).

    Jitter likewise uses the overload that already takes an explicit screen
    position (TerrainSpriteLayer.cs:168), so a per-cell offset needs no new
    plumbing -- but it DOES need oversized art, or the offset tears holes between
    neighbours, which is why the jittered panels are drawn with 32px tiles.
    """
    out = base.copy()
    rng = random.Random(seed)
    off = {}
    for layer in LAYER_ORDER:
        for (x, y), v in sorted(plan.get(layer, {}).items()):
            sp = scar_big(layer, v, 32) if jitter else scar(layer, v)
            dx = dy = 0
            if jitter:
                if (x, y) not in off:
                    off[(x, y)] = (rng.randint(-jitter, jitter), rng.randint(-jitter, jitter),
                                   rng.randrange(4))
                dx, dy, rot = off[(x, y)]
                sp = sp.rotate(90 * rot)
            if alpha_fn is not None:
                a = alpha_fn(x, y)
                if a <= 0.004:
                    continue
                if a < 1.0:
                    sp = sp.copy()
                    sp.putalpha(sp.getchannel("A").point(lambda p, a=a: int(p * a)))
            px = x * CELL + (CELL - sp.width) // 2 + dx
            py = y * CELL + (CELL - sp.height) // 2 + dy
            out.alpha_composite(sp, (px, py))
    return out


_ramp = None


def ramp():
    global _ramp
    if _ramp is None:
        content = os.path.expanduser("~/AppData/Roaming/OpenRA/Content/ra/v2")
        _ramp = gs.harvest_ramp(os.path.join(content, "temperat.mix"), "tem",
                                os.path.join(REPO, "tools/impact-scar/pal/temperat.pal"))[0]
    return _ramp


def scar_big(band, variant, size):
    """An oversized band tile from gen_scars' own noise and harvested ramp, so a
    jittered cell overlaps its neighbours instead of leaving a seam. This is what
    the art would have to become; it is generated here, not shipped."""
    key = ("big", band, variant, size)
    if key not in _cache:
        old = gs.CELL
        gs.CELL = size
        try:
            data = gs.make_frame(band, variant + 1, 0, ramp(), sum(map(ord, band)) * 1013)
        finally:
            gs.CELL = old
        im = Image.new("RGBA", (size, size))
        im.putdata([(0, 0, 0, 0) if v == 0 else pal()[v] + (255,) for v in data])
        _cache[key] = im
    return _cache[key]


# ---------------------------------------------------------------- the decal
def coverage(u):
    """Burnt-pixel fraction as a function of true radius, u = r/R.

    The five shipped bands ask for 0.78 / 0.62 / 0.46 / 0.30 / 0.14 at depth 0
    (gen_scars.py:80-86), sitting at band centres u = 0.06 / 0.28 / 0.50 / 0.72 /
    0.94. That is very nearly a straight line, so the decal uses the line and
    then forces it to zero at the rim -- which is the point of the treatment: the
    shipped version cannot go below its outermost band's 0.14 and so ends on a
    step, and this can.
    """
    c = 0.82 - 0.72 * u
    if u > 0.84:
        t = min(1.0, (u - 0.84) / 0.18)
        c *= 1.0 - t * t * (3 - 2 * t)
    return max(0.0, c)


def band_window(u):
    """Ramp window interpolated between the five shipped band windows."""
    pts = [(0.06, 0.00, 0.25), (0.28, 0.05, 0.40), (0.50, 0.15, 0.55),
           (0.72, 0.25, 0.70), (0.94, 0.35, 0.80)]
    if u <= pts[0][0]:
        return pts[0][1], pts[0][2]
    for (ua, la, ha), (ub, lb, hb) in zip(pts, pts[1:]):
        if u <= ub:
            t = (u - ua) / (ub - ua)
            return la + (lb - la) * t, ha + (hb - ha) * t
    return pts[-1][1], pts[-1][2]


def is_water_px(r, g, b):
    """Sub-cell land/water classification, read off the tile ART.

    The engine has no such notion: a cell carries ONE terrain type
    (TerrainTileInfo.TerrainType, TerrainInfo.cs:42-47) and a shoreline template
    is a grid of whole Beach and Water cells (tilesets/temperat.yaml:2246-2258).
    The half-and-half the player SEES is baked into the 24x24 tile image and
    nothing reads it back. `b > r + 6` recovers it: measured over the extracted
    art it marks 99.7% of open-water pixels and 3-6% of grass.
    """
    return b > r + 6


def land_mask(art, blur=3.2):
    """Per-pixel land coverage for the whole scene, softened so the scar eases
    off across the waterline instead of ending on a cell boundary."""
    from PIL import ImageFilter
    m = Image.new("L", (W * CELL, H * CELL), 0)
    mp = m.load()
    for (x, y), im in art.items():
        ip = im.load()
        for j in range(im.height):
            for i in range(im.width):
                r, g, b, a = ip[i, j]
                mp[x * CELL + i, y * CELL + j] = 0 if (a == 0 or is_water_px(r, g, b)) else 255
    return m.filter(ImageFilter.GaussianBlur(blur))


_fields = {}


def noise_fields(x0, y0, w, h, seed):
    """gen_scars' own fbm, evaluated per PIXEL rather than per 24px tile. Same
    generator, same frequency, so the decal carries the same grain as the shipped
    band art -- the difference under test is quantisation, not texture."""
    key = (x0, y0, w, h, seed)
    if key not in _fields:
        mask = [[gs._fbm(x0 + i, y0 + j, seed) for i in range(w)] for j in range(h)]
        tone = [[gs._fbm(x0 + i + 512, y0 + j + 512, seed + 31337) for i in range(w)] for j in range(h)]
        _fields[key] = (mask, tone)
    return _fields[key]


def render_decal(base, ter, blocked, mask_img=None, ignore_actors=False, seed=99):
    """ONE renderable scaled to the blast, instead of one sprite per cell.

    Nothing is cell-locked: coverage, tone and alpha are all functions of the
    true radius, so there is no 24px lattice to see. `mask_img`, when given,
    replaces the per-cell AcceptsSmudgeType gate with the sub-cell land coverage.
    """
    cx, cy = CENTRE
    ccx, ccy = (cx + 0.5) * CELL, (cy + 0.5) * CELL
    rad = (R + 0.7) * CELL
    x0, y0 = int(ccx - rad), int(ccy - rad)
    side = int(2 * rad) + 1
    mask, tone = noise_fields(x0, y0, side, side, seed)

    # One global quantile table: fbm is bell-shaped, so a raw threshold on its
    # value bears no relation to the coverage asked for (gen_scars.py:191-204).
    flat = sorted(v for row in mask for v in row)
    n = len(flat)

    rmp = ramp()
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    lp = layer.load()
    mp = mask_img.load() if mask_img is not None else None
    for j in range(side):
        py = y0 + j
        if not (0 <= py < base.height):
            continue
        for i in range(side):
            px = x0 + i
            if not (0 <= px < base.width):
                continue
            u = math.hypot(px + 0.5 - ccx, py + 0.5 - ccy) / (R * CELL)
            if u > 1.02:
                continue
            cov = coverage(u)
            if cov <= 0.001:
                continue
            gx, gy = px // CELL, py // CELL
            if not ignore_actors and (gx, gy) in blocked:
                continue
            a = 1.0
            if mp is not None:
                a = mp[px, py] / 255.0
            elif not ACCEPTS[ter[gy][gx]]:
                continue
            if a <= 0.02:
                continue
            m = mask[j][i]
            thr = flat[min(n - 1, int((1.0 - cov) * (n - 1)))]
            if m < thr:
                continue
            bias = 0.0 if thr >= 1.0 else min(1.0, max(0.0, (m - thr) / (1.0 - thr)))
            lo, hi = band_window(u)
            sub = gs.window(rmp, lo, hi)
            k = 0.62 * (1.0 - bias) + 0.38 * tone[j][i]
            idx = sub[min(int(min(max(k, 0.0), 0.999) * len(sub)), len(sub) - 1)]
            r, g, b = pal()[idx]
            lp[px, py] = (r, g, b, int(255 * a))

    out = base.copy()
    out.alpha_composite(layer)
    return out


# ---------------------------------------------------------------- sheet
def main():
    import sheet as sh

    ter, art, actors, blocked = build_scene()
    base = draw_terrain(art)
    plan = smudge_plan(ter, blocked)
    mask = land_mask(art)

    def finish(img, tint=None):
        return draw_actors(img, actors, tint)

    old = finish(render_old(base, ter, blocked))
    cur = finish(render_smudges(base, plan))
    a_alpha = finish(render_smudges(base, plan, alpha_fn=falloff))
    c_jit = finish(render_smudges(base, plan, jitter=5))
    ac = finish(render_smudges(base, plan, alpha_fn=falloff, jitter=5))
    b_dec = finish(render_decal(base, ter, blocked))
    b_mask = finish(render_decal(base, ter, blocked, mask_img=mask))
    holes_fixed = finish(render_decal(base, ter, blocked, mask_img=mask, ignore_actors=True))

    def scorch(a):
        """Husk frame plus darkening, strongest at the centre.

        Frame 1 of the tree's OWN shipped SHP is already the burnt tree: the
        t01.husk sequence is `Defaults: t01` with `idle: Start: 1`
        (sequences-decorations.yaml:404-406). Measured on the extracted art,
        frame 1 of t01 has 219 opaque pixels against frame 0's 490 and a mean
        RGB of (32,65,29) against (63,119,60). No new art is needed for this.
        """
        rr = min(radial(*c) for c in a["cells"])
        s = max(0.0, 1.0 - rr / (R * 1.05))
        if a["kind"] != "tree":
            return 0, 0.62 * s
        return (1 if s > 0.12 else 0), 0.72 * s

    full = draw_actors(render_decal(base, ter, blocked, mask_img=mask, ignore_actors=True),
                       actors, scorch)

    # The panels go out at 2x nearest-neighbour. At 1x the artefact under
    # discussion is a 24px lattice on a 550px image and simply cannot be judged.
    def z(img, k=2):
        return img.convert("RGB").resize((img.width * k, img.height * k), Image.NEAREST)

    old, cur, a_alpha, c_jit, ac, b_dec, b_mask, holes_fixed, full = (
        z(old), z(cur), z(a_alpha), z(c_jit), z(ac), z(b_dec), z(b_mask), z(holes_fixed), z(full))

    fig0 = [
        (old, "BEFORE - what shipped until 03:35 THIS MORNING (commit 0793564f)", True),
        (cur, "NOW - the five graded annuli that replaced it, same weapon, same footprint", False),
    ]
    fig1 = [
        (cur, "1.  CURRENT - THIS IS WHAT SHIPS TODAY",
         "Five annuli of 24px sprites at full opacity. The RADIAL gradient is already smooth: mean luminance "
         "over the scar runs 32.2 -> 47.0 with no step, so there is no ring banding left to fix. What is still "
         "cell-shaped is every BOUNDARY - the holes, the waterline, and the outer contour.", True),
        (a_alpha, "2.  Per-cell alpha, smooth radial falloff",
         "Measured against panel 1 this changes almost nothing (44.8 -> 45.0 at r=5.5): the outermost band is "
         "already only 14% covered, so there is no hard edge left for alpha to soften. WORTH KNOWING IT IS "
         "CHEAP - the layer already takes a per-cell alpha - but on this art it is a fix for a solved problem.", False),
        (c_jit, "3.  Per-cell offset + rotation, 32px art",
         "Breaks the axis-alignment of cell boundaries, which is real but subtle here because the noise art "
         "already hides the lattice inside a band. It needs new oversized art to avoid tearing seams, and it "
         "does nothing at all for the holes or the waterline.", False),
        (ac, "4.  Panels 2 and 3 together",
         "Both, for completeness. The honest reading of panels 2-4 is that the per-cell treatments are now "
         "low-value: the banding work already bought the gradient they were meant to buy.", False),
        (b_dec, "5.  One decal scaled to the blast",
         "The radial profile is no better than panel 1 - but nothing is cell-locked any more, which is what "
         "makes panel 6 possible. On its own it is not worth the rewrite; as the vehicle for sub-cell masking "
         "and for drawing across terrain types, it is.", False),
        (b_mask, "6.  Decal + sub-cell land coverage",
         "The per-cell AcceptsSmudgeType gate is replaced by a land mask read off the tile art, so the scar "
         "crosses the sand and eases out into the surf instead of ending a whole cell early. THIS is the "
         "panel that fixes something panel 1 cannot.", False),
    ]
    fig2 = [
        (cur, "1.  CURRENT - a hole at every tree, and at the building", "", True),
        (holes_fixed, "2.  Same render; the warhead no longer skips those cells", "", False),
        (full, "3.  ...and the trees switch to their own burnt frame", "", False),
    ]
    # Beach detail: crop the 2x panels around the waterline and take them to 6x.
    box = (3 * CELL * 2, 13 * CELL * 2, 20 * CELL * 2, 20 * CELL * 2)

    def crop(img):
        c = img.crop(box)
        return c.resize((c.width * 3, c.height * 3), Image.NEAREST)

    detail = [(crop(cur), "CURRENT - the scar stops on a cell boundary, one full cell short of the water", True),
              (crop(b_mask), "Sub-cell land coverage - it crosses the sand and dies in the surf", False)]

    # Rim close-up: the top edge of the blast, taken to 4x.
    rbox = (7 * CELL * 2, 0, 15 * CELL * 2, 5 * CELL * 2)

    def rcrop(img):
        c = img.crop(rbox)
        return c.resize((c.width * 2, c.height * 2), Image.NEAREST)

    rim = [(rcrop(old), "BEFORE 0793564f - flat tone, cell-quantised rim", True),
           (rcrop(cur), "CURRENT - the sparse outer band already dissolves this", False),
           (rcrop(a_alpha), "Per-cell alpha - near-indistinguishable from current", False),
           (rcrop(b_dec), "Decal - no cell boundaries, but no visible gain either", False)]

    out = sh.build(fig0, fig1, fig2, rim, detail, cur.size,
                   os.path.join(REPO, "WORKSPACE/mockups/scar-blending-options.png"))
    print("wrote %s (%d KB)" % (out, os.path.getsize(out) // 1024))


if __name__ == "__main__":
    main()
