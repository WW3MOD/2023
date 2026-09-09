#!/usr/bin/env python3
"""Render burnt trees inside a nuclear scar, offline.

THIS IS A PYTHON COMPOSITE, NOT AN ENGINE SCREENSHOT. No renderer runs and no
game is launched. Every sprite is real -- the clear-terrain templates, the tree
SHPs and the five generated scar bands are decoded from the files this branch
and the installed RA content actually ship -- but the ARRANGEMENT is a hand port
of the engine's own arithmetic and is only as true as that port.

Ported, with the source of each:
  * a cell's band is ceil(sqrt(dx^2+dy^2)), which is how MapGrid.CreateTilesByDistance
    buckets offsets (MapGrid.cs:201-210)
  * `Size: a, b` is the annulus b..a, a bare `Size: a` is the filled disc 0..a
    (LeaveSmudgeWarhead.cs:53-54)
  * a cell with a blocking actor the warhead cannot target gets NO smudge
    (LeaveSmudgeWarhead.cs:67-68). Trees are Targetable TargetTypes: Trees and the
    scar warheads take the default ValidTargets: Ground, Water, so EVERY TREE CELL
    IS SKIPPED -- the unscorched patches under the trunks here are the shipped
    behaviour, not a bug in this script
  * smudge layer draw order is the world.yaml order, which is the Z order
    (SmudgeLayer.cs:30, rules/world.yaml:473-508) -- lightest first, darkest wins
  * palette indices 3 and 4 are shadow on the `terrain` palette
    (rules/palettes.yaml:40-49) and ImmutablePalette maps them to alpha-140 black
    (Palette.cs:98-99), so they are composited as translucent black rather than as
    their literal palette colour, which on temperate is a vivid green
  * a burnt tree is frame 1 of the tree's OWN shp -- the frame that `t##.husk`'s
    `idle: Start: 1` names (sequences/sequences-decorations.yaml)

APPROXIMATE, deliberately: sprite anchoring is taken as "sprite box == footprint
box", which is right for these actors but is not a port of RenderSprites, so a
tree may sit a pixel or two off where the engine would put it. Nothing being
compared here turns on that. WithColoredOverlay's second pass is modelled as a
per-pixel wash weighted by the pixel's own alpha; the engine does it as a second
renderable with ReplaceColor + WithAlpha (WithColoredOverlay.cs:50-56), which is
the same arithmetic for opaque pixels and close for the alpha-140 shadow ones.

    python tools/burnt-trees/burntmock.py           -> WORKSPACE/mockups/burnt-trees.png
    python tools/burnt-trees/burntmock.py --decide  -> WORKSPACE/mockups/_burnt-trees-candidates.png

Terrain art comes from tools/burnt-trees/work/, which is NOT committed -- see
prepare-art.sh. Tree art is read straight out of the installed RA mixes.
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
WORK = os.path.join(HERE, "work")
SCARS = os.path.join(REPO, "mods/ww3mod/bits/misc/scars")
CONTENT = os.path.expanduser("~/AppData/Roaming/OpenRA/Content/ra/v2")

sys.path.insert(0, os.path.join(REPO, "tools", "impact-scar"))
import racontent as rc          # noqa: E402

CELL = 24                       # OpenRA draws a cell at 24 px at 1x zoom

# NukeB61Mod12Y10 (10 kt), rules/weapons/weapons-nuclear-arsenal.yaml:997.
# Chosen because its two radii both fit one screen: the ground scar stops at 4
# cells while the thermal reach this change binds to is 9.41 cells (its
# Warhead@Fire10 Range, 9c418), so a single frame shows scorched ground, burnt
# trees past the edge of it, and living trees past those.
BANDS = [("ScarCore", 0, 1), ("ScarChar", 2, 2), ("ScarBurn", 3, 3), ("ScarRim", 4, 4)]
THERMAL = 9 + 418 / 1024.0
LAYER_ORDER = ["ScarRim", "ScarBurn", "ScarChar", "ScarCrater", "ScarCore"]
VARIANTS = {"ScarCore": "bza", "ScarCrater": "bzb", "ScarChar": "bzc",
            "ScarBurn": "bzd", "ScarRim": "bze"}

TILESETS = {"temperat": "tem", "snow": "sno"}

# Species that inherit ^TreeIndestructible and exist outside DESERT, with the
# Dimensions of their Building footprint (rules/ingame/decoration.yaml).
# T04/T09 are desert-only and no shipped map uses that tileset.
SPECIES = [("t01", 2, 2), ("t02", 2, 2), ("t03", 2, 2), ("t05", 2, 2), ("t06", 2, 2),
           ("t07", 2, 2), ("t10", 2, 2), ("t12", 2, 2), ("t13", 2, 2), ("t16", 2, 2),
           ("t17", 2, 2), ("tc01", 3, 2), ("tc02", 3, 2), ("tc03", 3, 2)]

W, H = 27, 21
CX, CY = 13, 10

_pal, _cache = {}, {}


def pal(ts):
    if ts not in _pal:
        p = os.path.join(REPO, "tools/impact-scar/pal", ts + ".pal")
        if not os.path.exists(p):
            raise SystemExit("missing " + p + " -- run tools/impact-scar/extract-palettes.sh")
        _pal[ts] = rc.read_pal(open(p, "rb").read())
    return _pal[ts]


def to_rgba(w, h, frame, ts):
    """Palette indices -> RGBA, with 0 transparent and 3/4 as the engine's shadow."""
    p = pal(ts)
    im = Image.new("RGBA", (w, h))
    im.putdata([(0, 0, 0, 0) if v == 0 else
                (0, 0, 0, 140) if v in (3, 4) else
                p[v] + (255,) for v in frame])
    return im


def tree(name, ts, frame):
    key = (name, ts, frame)
    if key not in _cache:
        data = rc.MixFile(os.path.join(CONTENT, ts + ".mix")).get(name + "." + TILESETS[ts])
        if data is None:
            raise SystemExit("no art for %s in %s.mix" % (name, ts))
        w, h, frames = rc.read_shp(data)
        _cache[key] = to_rgba(w, h, frames[frame], ts)
    return _cache[key]


def scar(band, variant, ts, depth=0):
    key = ("scar", band, variant, ts, depth)
    if key not in _cache:
        name = VARIANTS[band] + str(variant + 1) + "." + TILESETS[ts]
        w, h, frames = rc.read_shp(open(os.path.join(SCARS, name), "rb").read())
        _cache[key] = to_rgba(w, h, frames[depth], ts)
    return _cache[key]


def ground(ts):
    key = ("ground", ts)
    if key not in _cache:
        d = os.path.join(WORK, ts)
        out, i = [], 0
        shadows = {pal(ts)[3] + (255,), pal(ts)[4] + (255,)}
        while os.path.exists(os.path.join(d, "clear1-%04d.png" % i)):
            im = Image.open(os.path.join(d, "clear1-%04d.png" % i)).convert("RGBA")
            im.putdata([(0, 0, 0, 140) if px in shadows else px for px in im.getdata()])
            out.append(im)
            i += 1
        if not out:
            raise SystemExit("no terrain in " + d + " -- run tools/burnt-trees/prepare-art.sh")
        _cache[key] = out
    return _cache[key]


def wash(im, tint, alpha):
    """WithColoredOverlay: a flat ReplaceColor pass at `alpha` over the sprite."""
    out = im.copy()
    px = out.load()
    for y in range(out.height):
        for x in range(out.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            k = alpha * (a / 255.0)
            px[x, y] = (round(r + (tint[0] - r) * k),
                        round(g + (tint[1] - g) * k),
                        round(b + (tint[2] - b) * k), a)
    return out


def forest(seed=11):
    """A stand-planted wood, the way RA forests are placed."""
    rng = random.Random(seed)
    taken, out = set(), []
    for _ in range(13):
        sx, sy = rng.randrange(1, W - 1), rng.randrange(2, H - 1)
        for _ in range(5):
            x, y = sx + rng.randint(-3, 3), sy + rng.randint(-2, 2)
            if not (1 <= x < W - 3 and 2 <= y < H - 1) or (x, y) in taken:
                continue
            taken.add((x, y))
            name, bw, bh = SPECIES[rng.randrange(len(SPECIES))]
            out.append({"img": name, "cell": (x, y), "box": (x, y - bh + 1, bw, bh)})
    return sorted(out, key=lambda a: a["cell"][1])


def smudges(occupied, seed=5):
    """Which cell gets which band, with the tree-cell skip the engine applies."""
    rng = random.Random(seed)
    per_layer = {}
    for band, lo, hi in BANDS:
        for y in range(H):
            for x in range(W):
                d = math.ceil(math.hypot(x - CX, y - CY))
                if not lo <= d <= hi:
                    continue
                if (x, y) in occupied:      # LeaveSmudgeWarhead.cs:67-68
                    continue
                per_layer.setdefault(band, {})[(x, y)] = rng.randrange(4)
    return per_layer


def render(ts, mode, actors, per_layer, seed=5):
    rng = random.Random(seed)
    bg = Image.new("RGBA", (W * CELL, H * CELL), (0, 0, 0, 255))
    g = ground(ts)
    for y in range(H):
        for x in range(W):
            bg.alpha_composite(rng.choice(g), (x * CELL, y * CELL))
    for band in LAYER_ORDER:
        for (x, y), v in per_layer.get(band, {}).items():
            bg.alpha_composite(scar(band, v, ts), (x * CELL, y * CELL))
    for a in actors:
        burnt = mode != "living" and math.hypot(a["cell"][0] - CX, a["cell"][1] - CY) <= THERMAL
        sp = tree(a["img"], ts, 1 if burnt else 0)
        if burnt and mode == "husk+black":
            sp = wash(sp, (0, 0, 0), 0.5)
        elif burnt and mode == "husk+char":
            sp = wash(sp, (24, 18, 12), 0.6)
        bx, by, _, _ = a["box"]
        bg.alpha_composite(sp, (bx * CELL, by * CELL))
    return bg.convert("RGB")


def zoom(im, z):
    return im.resize((im.width * z, im.height * z), Image.NEAREST)


def font(sz, bold=False):
    for p in (("C:/Windows/Fonts/segoeuib.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf"),
              "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(p):
            return ImageFont.truetype(p, sz)
    return ImageFont.load_default()


def crop3x(im, cx=CX - 4, cy=CY - 4, w=9, h=7):
    return zoom(im.crop((cx * CELL, cy * CELL, (cx + w) * CELL, (cy + h) * CELL)), 3)


def scene():
    actors = forest()
    occupied = {a["cell"] for a in actors}
    return actors, smudges(occupied)


def decide(out):
    """Four candidate treatments per tileset, so the choice is made by looking."""
    actors, per_layer = scene()
    modes = ["living", "husk", "husk+char", "husk+black"]
    labels = ["SHIPPED (living)", "husk frame", "husk + warm char wash",
              "husk + black wash"]
    tiles, w, h = [], 0, 0
    for ts in ("temperat", "snow"):
        for m in modes:
            im = crop3x(render(ts, m, actors, per_layer), CX - 5, CY - 4, 11, 8)
            tiles.append((ts.upper() + "  -  " + labels[modes.index(m)], im))
            w, h = im.size
    pad, hdr = 12, 30
    sheet = Image.new("RGB", (4 * (w + pad) + pad, 2 * (h + hdr + pad) + pad + 34),
                      (24, 24, 26))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 8), "PYTHON COMPOSITE, NOT AN ENGINE SCREENSHOT  -  "
                     "candidate treatments, 3x zoom, 10 kt scar",
           font=font(17, True), fill=(240, 200, 120))
    for k, (lab, im) in enumerate(tiles):
        x = pad + (k % 4) * (w + pad)
        y = 34 + pad + (k // 4) * (h + hdr + pad)
        d.text((x, y), lab, font=font(15), fill=(230, 230, 235))
        sheet.paste(im, (x, y + hdr - 6))
    sheet.save(out)
    print("wrote", out, sheet.size)


def deliverable(out, mode):
    actors, per_layer = scene()
    panels = []
    for ts in ("temperat", "snow"):
        before = render(ts, "living", actors, per_layer)
        after = render(ts, mode, actors, per_layer)
        panels.append((ts, before, after))

    fw, fh = panels[0][1].size
    cw, ch = crop3x(panels[0][1]).size
    pad, hdr, sec = 14, 26, 40
    W_ = max(2 * fw + 3 * pad, 2 * cw + 3 * pad)
    H_ = 52 + 2 * (sec + hdr + fh + pad + hdr + ch + pad)
    sheet = Image.new("RGB", (W_, H_), (24, 24, 26))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 8), "PYTHON COMPOSITE, NOT AN ENGINE SCREENSHOT", font=font(20, True),
           fill=(240, 200, 120))
    d.text((pad, 32), "Decoded shipped art composited by tools/burnt-trees/burntmock.py. "
                      "10 kt (NukeB61Mod12Y10): ground scar to 4 cells, burn to 9.41 cells.",
           font=font(15), fill=(170, 170, 178))

    y = 52
    for ts, before, after in panels:
        d.text((pad, y + 8), ts.upper() + "  -  1x (24 px/cell, actual game zoom)",
               font=font(18, True), fill=(150, 200, 240))
        y += sec
        for j, (lab, im) in enumerate((("BEFORE - shipped", before), ("AFTER - this branch", after))):
            x = pad + j * (fw + pad)
            d.text((x, y), lab, font=font(15), fill=(230, 230, 235))
            sheet.paste(im, (x, y + hdr - 6))
        y += hdr + fh + pad
        for j, (lab, im) in enumerate((("BEFORE - 3x", crop3x(before)), ("AFTER - 3x", crop3x(after)))):
            x = pad + j * (cw + pad)
            d.text((x, y), lab, font=font(15), fill=(230, 230, 235))
            sheet.paste(im, (x, y + hdr - 6))
        y += hdr + ch + pad
    sheet.save(out)
    print("wrote", out, sheet.size)


if __name__ == "__main__":
    os.makedirs(os.path.join(REPO, "WORKSPACE", "mockups"), exist_ok=True)
    if "--decide" in sys.argv:
        decide(os.path.join(REPO, "WORKSPACE/mockups/_burnt-trees-candidates.png"))
    else:
        # "husk+char" is what ^TreeIndestructible actually ships: the frame swap AND the
        # WithColoredOverlay wash. Rendering plain "husk" here would show a mockup the YAML does
        # not produce -- correct on TEMPERAT and materially wrong on SNOW.
        deliverable(os.path.join(REPO, "WORKSPACE/mockups/burnt-trees.png"), "husk+char")
