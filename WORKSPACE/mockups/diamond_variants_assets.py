#!/usr/bin/env python3
"""Build the asset set for WORKSPACE/mockups/diamond-variants.html.

Run from the repo root:  python WORKSPACE/mockups/diamond_variants_assets.py

Descends from indicator_layout_assets.py and reuses its SHP loaders, its unit
choices and its terrain path. What is NEW here is that every diamond is
RASTERISED IN PYTHON AT TRUE PIXEL SIZE and shipped as a PNG data URI, rather
than drawn as SVG and scaled by the browser. That is the whole point of this
mockup: the question is how many levels of each channel survive at 6x9 px, and
a vector diamond scaled to 4x cannot answer it -- it would show levels that do
not exist on the pixel grid. Zoom in the page is nearest-neighbour over these
exact pixels.

INHERITED TRAPS (fixed upstream, restated so they are not re-broken):
  1. The Abrams image is `abrams-correction.shp`, NOT `abrams.shp`.
  2. The turret is a SEPARATE frame ring; hull N is drawn under turret 32+N.
  3. Frame 0 is NORTH and the index advances COUNTER-CLOCKWISE, so a south-east
     facing is hull frame 19 / turret 51, and infantry `stand` SE is frame 5.
  4. `bradley` has no drawable sprite in this repo. Do not put it in a mockup.

PALETTE PROXY, restated because it still applies: the canonical temperat.pal is
inside a Blowfish-encrypted local.mix, so terrain renders through
engine/mods/ra/maps/chernobyl/plains.pal. For DECORATIONS the proxy is exact
(it reproduces pip-suppression f0 #FFD77D / f9 #9A2800 and the damage ramps
independently) -- but every diamond drawn here is a PROPOSAL, authored in RGB,
not a decode of a shipped sprite, so the only palette-dependent thing on screen
is the ground and the units. Judge contrast against ground with that caveat in
hand: the true in-game ground is somewhat more olive than this proxy.

THE TEN REAL SUPPRESSION HUES are decoded from pip-suppression.shp rather than
retyped, because variant X exists to show that exact ramp failing.
"""

import base64
import colorsys
import io
import json
import os
import re
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from buymenu_shp_dump import PAL, load_palette, read_shp  # noqa: E402
from indicator_layout_assets import (  # noqa: E402
    CELL, SE_CLASSIC, SE_INF8, TEAMS, TERRAIN_PAL_ALT, terrain, team_ramp,
    unit_sprite, uri,
)

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
HTML = os.path.join(HERE, "diamond-variants.html")

BITS = os.path.join(REPO, "mods", "ww3mod", "bits", "units")
VEH, INF, PIPS = (os.path.join(BITS, d) for d in ("vehicle", "infantry", "pips"))

# ---------------------------------------------------------------- the glyph
# A diamond as an L1 ball: |dx|/rx + |dy|/ry <= 1 + TOL, sampled on integer
# pixel centres. TOL exists because at 6x9 the exact ball loses both tips
# entirely (rows 0 and 8 come out empty), which would make the glyph a 6x7
# hexagon and quietly change the thing being measured.
TOL = 0.12


def mask(w, h):
    """Set of (x, y) inside the diamond. Rows, top to bottom."""
    cx, cy = (w - 1) / 2.0, (h - 1) / 2.0
    rx, ry = w / 2.0, h / 2.0
    out = set()
    for y in range(h):
        for x in range(w):
            if abs(x - cx) / rx + abs(y - cy) / ry <= 1.0 + TOL:
                out.add((x, y))
    return out


def profile(w, h):
    """Ink width per row -- printed at build time so the shape is on the record."""
    m = mask(w, h)
    return [sum(1 for x in range(w) if (x, y) in m) for y in range(h)]


def ring_of(m):
    """Mask pixels with at least one 4-neighbour outside the mask."""
    return {(x, y) for (x, y) in m
            if not all(n in m for n in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)))}


def hexrgb(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


def dim(rgb, f):
    return tuple(max(0, min(255, int(c * f))) for c in rgb)


# ---------------------------------------------------------------- colour ramps
# Impediment bands. Yellow->orange->red is deliberate: impediment is partly the
# permanent damage floor, and the mod's damage ramp is already that family
# (infantry.yaml:713-715). Grey is the ZERO state -- a unit with nothing wrong
# with it, which is most of the screen most of the time.
GREY = "#9AA4B0"
RAMP4 = [GREY, "#E8D24A", "#E8892B", "#D6392B"]                  # 0 / low / mid / high
RAMP3 = [GREY, "#E8A32B", "#D6392B"]                             # 0 / some / bad
RAMP5 = [GREY, "#E8D24A", "#E8A32B", "#E8892B", "#D6392B"]
# One hue, three lightnesses -- the colour-blind-safe variant.
LIGHT3 = [GREY, "#F2C98A", "#C8641A"]
LIGHT4 = [GREY, "#F6DCB4", "#E0995A", "#B4500E"]

# The five SHIPPED detectability colours, verbatim from defaults.yaml:959-964.
DET_SHIPPED = ["#6E9E76", "#ECC73C", "#F0B232", "#F09425", "#FF4A3C"]
DET_NAMES = ["Concealed", "Low", "Moderate", "High", "Spotted"]


def suppression_hues():
    """The ten real pip-suppression colours, decoded not retyped.

    Falls back to the two endpoints the tree states independently
    (WithGarrisonDecoration.cs:59) interpolated in HLS, if the SHP is missing.
    """
    p = os.path.join(PIPS, "pip-suppression.shp")
    pal = load_palette(PAL)
    if os.path.isfile(p):
        w, h, fr = read_shp(p)
        out = []
        for i in range(min(10, len(fr))):
            counts = {}
            for v in fr[i]:
                if v in (0, 3):          # transparent, chrome shadow
                    continue
                counts[v] = counts.get(v, 0) + 1
            if counts:
                best = max(counts, key=counts.get)
                out.append("#%02X%02X%02X" % pal[best])
        if len(out) == 10:
            return out, "decoded from pip-suppression.shp (10 frames)"
    a, b = hexrgb("FFD77D"), hexrgb("9A2800")
    ha, la, sa = colorsys.rgb_to_hls(*[c / 255 for c in a])
    hb, lb, sb = colorsys.rgb_to_hls(*[c / 255 for c in b])
    out = []
    for i in range(10):
        t = i / 9
        r, g, bl = colorsys.hls_to_rgb(ha + (hb - ha) * t, la + (lb - la) * t, sa + (sb - sa) * t)
        out.append("#%02X%02X%02X" % (int(r * 255), int(g * 255), int(bl * 255)))
    return out, "INTERPOLATED between the two stated endpoints (SHP not found)"


# ---------------------------------------------------------------- the renderer
KEY = (0, 0, 0, 200)        # 1px dark keyline, the sprite equivalent of
#                             SpriteFont.DrawTextWithContrast's outline. Every
#                             variant gets one; without it nothing separates
#                             from grass and the comparison is not about the
#                             encoding any more.
HOLLOW = (0, 0, 0, 120)     # unfilled interior: an empty vessel, not a hole


def band(value, n):
    """0..100 -> 0..n-1, with 0 reserved for exactly zero."""
    if value <= 0:
        return 0
    return min(n - 1, 1 + int((value - 1) * (n - 1) / 100))


def filled_rows(h, step, steps):
    """How many rows of a `steps`-step ladder are inked at `step`."""
    if steps <= 1:
        return h if step else 0
    return int(round(h * step / (steps - 1)))


def draw(spec, imped, det):
    """One diamond. `det` is 1..5 (Concealed..Spotted); `imped` is 0..100."""
    kind = spec["kind"]
    w, h = spec.get("size", (6, 9))
    m = mask(w, h)

    # --- shape erosion (variant W): impediment blunts the tips.
    if kind == "erode":
        cut = band(imped, 4)                      # 0..3 rows off each end
        m = {(x, y) for (x, y) in m if cut <= y < h - cut}

    if not m:
        m = {(x, y) for (x, y) in mask(w, h) if y == (h - 1) // 2}

    ring = ring_of(m)

    # --- which channel drives colour, which drives fill
    if spec.get("invert"):
        col_v, fill_v = det, imped            # colour = detection, fill = impediment
    else:
        col_v, fill_v = imped, det

    # --- colour
    if spec.get("invert"):
        ramp = spec.get("ramp", DET_SHIPPED)
        cidx = min(len(ramp) - 1, col_v - 1)
    else:
        ramp = spec["ramp"]
        cidx = band(col_v, len(ramp))
    rgb = hexrgb(ramp[cidx])

    # --- fill
    steps = spec["fill_steps"]
    if spec.get("invert"):
        step = band(fill_v, steps)
    else:
        step = min(steps - 1, int(round((fill_v - 1) * (steps - 1) / 4)))

    if spec.get("hide_at_zero") and step == 0:
        return None

    im = Image.new("RGBA", (w + 2, h + 2), (0, 0, 0, 0))
    px = im.load()

    def ink(x, y, c):
        px[x + 1, y + 1] = c

    # keyline first, so glyph pixels overwrite it where they coincide
    for (x, y) in m:
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)):
            nx, ny = x + dx, y + dy
            if (nx, ny) not in m and -1 <= nx <= w and -1 <= ny <= h:
                px[nx + 1, ny + 1] = KEY

    def is_filled(x, y):
        if kind in ("up", "erode", "split"):
            return y >= h - filled_rows(h, step, steps)
        if kind == "down":
            return y < filled_rows(h, step, steps)
        if kind == "centre":
            reach = filled_rows(h, step, steps) / 2.0
            return abs(y - (h - 1) / 2.0) <= reach - 0.5
        if kind == "inward":
            # hollow -> ring -> thick ring -> solid
            if step <= 0:
                return False
            if step >= steps - 1:
                return True
            depth = step
            return min(x, w - 1 - x, y, h - 1 - y) >= 0 and _depth(m, x, y) <= depth
        return True

    if kind == "split":
        # left half = impediment fill, right half = detection fill, one colour.
        li = band(imped, steps)
        ri = min(steps - 1, int(round((det - 1) * (steps - 1) / 4)))
        rgb = hexrgb(spec["ramp"][-1])
        for (x, y) in m:
            half = li if x < w / 2.0 else ri
            on = y >= h - filled_rows(h, half, steps)
            ink(x, y, rgb + (255,) if on else HOLLOW)
        for (x, y) in ring:
            ink(x, y, rgb + (255,))
        return im

    for (x, y) in m:
        ink(x, y, rgb + (255,) if is_filled(x, y) else HOLLOW)

    # the outline always carries the colour, so an empty glyph still says its band
    ow = spec.get("outline_weight")
    heavy = ow is not None and det >= ow
    for (x, y) in ring:
        ink(x, y, rgb + (255,))
    if heavy:
        for (x, y) in m:
            if _depth(m, x, y) <= 1:
                ink(x, y, rgb + (255,))

    return im


_DEPTH_CACHE = {}


def _depth(m, x, y):
    """Distance in pixels from (x,y) to the outside of the mask (ring = 0)."""
    key = frozenset(m)          # NOT id(m): m is rebuilt per call and ids get reused
    d = _DEPTH_CACHE.get(key)
    if d is None:
        d = {}
        cur = ring_of(m)
        seen = set(cur)
        lvl = 0
        while cur:
            for p in cur:
                d[p] = lvl
            nxt = set()
            for (px_, py_) in cur:
                for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                    n = (px_ + dx, py_ + dy)
                    if n in m and n not in seen:
                        seen.add(n)
                        nxt.add(n)
            cur = nxt
            lvl += 1
        _DEPTH_CACHE[key] = d
    return d.get((x, y), 99)


# ---------------------------------------------------------------- the variants
SUPP10, SUPP_PROV = suppression_hues()

VARIANTS = [
    # ---- the one drawn to fail, first so it is not buried
    dict(id="X", kind="up", ramp=SUPP10, fill_steps=5, size=(6, 9)),

    # ---- conservative: the user's proposal, small variations
    dict(id="A", kind="up", ramp=RAMP4, fill_steps=5, size=(6, 9)),
    dict(id="B", kind="up", ramp=RAMP3, fill_steps=4, size=(6, 9)),
    dict(id="C", kind="centre", ramp=RAMP4, fill_steps=4, size=(6, 9)),
    dict(id="D", kind="inward", ramp=RAMP4, fill_steps=3, size=(6, 9)),
    dict(id="E", kind="up", ramp=RAMP5, fill_steps=5, size=(9, 13)),
    dict(id="F", kind="up", ramp=RAMP4, fill_steps=2, size=(6, 9)),
    dict(id="G", kind="down", ramp=RAMP4, fill_steps=4, size=(6, 9)),

    # ---- experiments
    dict(id="H", kind="up", ramp=DET_SHIPPED, fill_steps=4, size=(6, 9), invert=True),
    dict(id="I", kind="up", ramp=LIGHT4, fill_steps=4, size=(6, 9)),
    dict(id="J", kind="up", ramp=RAMP4, fill_steps=4, size=(6, 9), outline_weight=5),
    dict(id="K", kind="up", ramp=RAMP4, fill_steps=5, size=(6, 9), hide_at_zero=True),

    # ---- out of the box
    dict(id="V", kind="split", ramp=["#C9D4E4"], fill_steps=4, size=(8, 11)),
    dict(id="W", kind="erode", ramp=["#C9D4E4"], fill_steps=4, size=(7, 11)),
]

IMPEDS = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100]
DETS = [1, 2, 3, 4, 5]


def main():
    pal = load_palette(PAL)
    pal_t = load_palette(TERRAIN_PAL_ALT) if os.path.isfile(TERRAIN_PAL_ALT) else pal
    ramp = {k: team_ramp(pal, v) for k, v in TEAMS.items()}
    out = {}

    units = [
        ("abrams", os.path.join(VEH, "abrams-correction.shp"), SE_CLASSIC, SE_CLASSIC + 32, "us", 1.25),
        ("e3us", os.path.join(INF, "e3.shp"), SE_INF8, None, "us", 0.65),
    ]
    for key, path, fr, tur, team, sc in units:
        if not os.path.isfile(path):
            print("MISSING", path)
            continue
        im, bb = unit_sprite(pal, path, fr, ramp[team], tur, sc)
        out[key] = {"w": im.width, "h": im.height, "src": uri(im),
                    "frame": fr, "scale": sc, "file": os.path.basename(path)}
        print(f"  {key:7s} {os.path.basename(path):24s} f{fr}"
              f"{'+' + str(tur) if tur else '':>4s}  ink->{im.width}x{im.height} @{sc}")

    tile, prov = terrain(pal_t)
    if tile:
        out["_terrain"] = {"src": uri(tile), "w": tile.width, "h": tile.height,
                           "cell": CELL, "provenance": prov}
    print(f"terrain: {prov}")
    print(f"suppression hues: {SUPP_PROV}\n  {' '.join(SUPP10)}")

    for wh in ((6, 9), (7, 11), (8, 11), (9, 13)):
        print(f"diamond {wh[0]}x{wh[1]} row widths: {profile(*wh)}  ink={sum(profile(*wh))}px")

    dia = {}
    for spec in VARIANTS:
        d = {}
        for i in IMPEDS:
            for t in DETS:
                im = draw(spec, i, t)
                d[f"{i}_{t}"] = None if im is None else {
                    "w": im.width, "h": im.height, "src": uri(im)}
        dia[spec["id"]] = d
        w, h = spec.get("size", (6, 9))
        print(f"  variant {spec['id']}: {spec['kind']:7s} {w}x{h} "
              f"fill={spec['fill_steps']} colours={len(spec['ramp'])}")

    if not os.path.isfile(HTML):
        print(f"(HTML not present: {HTML})")
        return 0

    src = open(HTML, encoding="utf-8").read()
    for name, payload in (("ASSETS", out), ("DIAMONDS", dia)):
        new, n = re.subn(rf"(?m)^const {name} = .*$",
                         lambda _: f"const {name} = " + json.dumps(payload) + ";", src, count=1)
        if n != 1:
            sys.exit(f"no 'const {name} = ...' line in {HTML}")
        src = new
    open(HTML, "w", encoding="utf-8", newline="\n").write(src)
    print(f"injected {len(out)} assets + {len(dia)} variants "
          f"({sum(len(v) for v in dia.values())} diamonds)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
