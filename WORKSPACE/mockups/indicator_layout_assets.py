#!/usr/bin/env python3
"""Build the asset set for WORKSPACE/mockups/indicator-layout.html.

Run from the repo root:  python WORKSPACE/mockups/indicator_layout_assets.py
                         python WORKSPACE/mockups/indicator_layout_assets.py --contact <shp>

Decodes the shipped in-game sprites, the real terrain tile and the decoration
pips to PNG data URIs, then rewrites the ASSETS / FONT_B64 lines in the HTML so
the mockup is self-contained. Reuses the SHP loaders in buymenu_shp_dump.py.

WHAT THE GAME ACTUALLY DRAWS -- four traps, all of which an earlier pass of this
script fell into:

  1. The Abrams image is `abrams-correction.shp`, NOT `abrams.shp`. The actor
     names Image: abrams, whose sequence node sets `idle: abrams-correction`
     (sequences/sequences.yaml). Both files exist; only one is drawn.
  2. The turret is a SEPARATE frame ring. Frames 0-31 are the hull, 32-63 the
     turret (`turret: ... Start: 32`). A complete tank is hull N over turret 32+N.
  3. Frame 0 is NORTH and the index advances COUNTER-CLOCKWISE (N -> W -> S -> E).
     WVec.cs:66-76 defines north as -y; SE is angle 640, which under the classic
     32-facing table is frame 19. Not 12, and not clockwise.
  4. `bradley` is not usable: its sequence is `idle: 1tnk` and 1tnk.shp is absent
     from this repo (it lives in RA's conquer.mix). bradley.shp exists on disk but
     is never drawn. Do not put it in a mockup.

PALETTES. Units draw through `player` (RenderSprites.PlayerPalette), which is
temperat.pal with ShadowIndex 4 and a player-colour remap over indices 80-95
(palettes.yaml:50-53, :132-134). Decorations default to `chrome` -- same file,
ShadowIndex 3. rank and class set Palette: effect -- same file, ShadowIndex 4.

PALETTE PROXY, and it is exact for decorations: the canonical temperat.pal is
inside a Blowfish-encrypted local.mix, so we substitute
engine/mods/ra/maps/chernobyl/temperat.pal. That proxy reproduces four values the
tree states independently -- pip-suppression f0 #FFD77D and f9 #9A2800
(WithGarrisonDecoration.cs:59) and the damage ramps at infantry.yaml:713-715 and
defaults.yaml:185 -- so decoration hues are not approximate. Note the chernobyl
copy is deliberately DARKENED (nuclear-wasteland map); terrain rendered through it
reads darker than the real game. plains.pal in the same directory is a more
conventional olive and is offered as TERRAIN_PAL_ALT.

SCALE. RenderSprites: Scale is 1.25 on abrams/t90 and 0.65 on ^E3 infantry, over a
24x24 cell (mod.yaml MapGrid TileSize). We apply it with NEAREST so the mockup
stays pixel-honest; the engine uses a filtered texture scale, so fine detail in
game is slightly smoother than shown. Decoration marks are UI-space and do NOT
scale -- that asymmetry is the point of the mockup, so it is preserved here.
"""

import base64
import colorsys
import io
import json
import os
import re
import struct
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from buymenu_shp_dump import PAL, load_palette, read_shp  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
HTML = os.path.join(HERE, "indicator-layout.html")

TERRAIN_PAL_ALT = os.path.join(REPO, "engine", "mods", "ra", "maps", "chernobyl", "plains.pal")
MIX = os.path.join(os.path.expanduser("~"), "AppData", "Roaming", "OpenRA",
                   "Content", "ra", "v2", "temperat.mix")
REPO_TEM = os.path.join(REPO, "mods", "ww3mod", "bits", "misc", "tiles", "tem")

BITS = os.path.join(REPO, "mods", "ww3mod", "bits", "units")
VEH, INF, PIPS, CLS = (os.path.join(BITS, d) for d in ("vehicle", "infantry", "pips", "classes"))

REMAP = list(range(80, 96))
SH_PLAYER, SH_CHROME = 4, 3
CELL = 24

TEAMS = {"us": (0x3C, 0x78, 0xC8), "ru": (0xC8, 0x3C, 0x3C)}

# hull frame for a south-east facing, 32-facing classic ring; turret is +32.
SE_CLASSIC = 19
SE_INF8 = 5          # 8-facing infantry `stand`: 0=N,1=NW,2=W,3=SW,4=S,5=SE,6=E,7=NE


def uri(im):
    buf = io.BytesIO()
    im.save(buf, "PNG")
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii")


def team_ramp(pal, rgb):
    h, _, s = colorsys.rgb_to_hls(*[c / 255 for c in rgb])
    out = {}
    for i in REMAP:
        r, g, b = pal[i]
        lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255
        rr, gg, bb = colorsys.hls_to_rgb(h, lum, s)
        out[i] = (int(rr * 255), int(gg * 255), int(bb * 255))
    return out


def paint(pal, w, h, idx, shadow, remap=None, base=None):
    im = base or Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = im.load()
    for y in range(h):
        for x in range(w):
            v = idx[y * w + x]
            if v == 0:
                continue
            if v == shadow:
                if base is None:
                    px[x, y] = (0, 0, 0, 110)
                continue
            px[x, y] = (remap[v] if (remap and v in remap) else pal[v]) + (255,)
    return im


def unit_sprite(pal, path, frame, remap, turret=None, scale=1.0):
    """Hull frame, optionally with its turret frame composited over it."""
    w, h, fr = read_shp(path)
    im = paint(pal, w, h, fr[frame], SH_PLAYER, remap)
    if turret is not None and turret < len(fr):
        im = paint(pal, w, h, fr[turret], SH_PLAYER, remap, base=im)
    bb = im.getbbox()
    im = im.crop(bb) if bb else im
    if scale != 1.0:
        im = im.resize((max(1, round(im.width * scale)),
                        max(1, round(im.height * scale))), Image.NEAREST)
    return im, bb


# ---------------------------------------------------------------- MIX + TmpRA
def mix_hash(name):
    """OpenRA classic MIX filename hash: uppercase, NUL-pad to a multiple of 4,
    then per 4-byte LE word  a = rotl32(a,1) + word."""
    d = name.upper().encode("ascii")
    d += b"\0" * ((-len(d)) % 4)
    a = 0
    for i in range(0, len(d), 4):
        word = struct.unpack_from("<I", d, i)[0]
        a = (((a << 1) | (a >> 31)) & 0xFFFFFFFF)
        a = (a + word) & 0xFFFFFFFF
    return a


def mix_read(path, name):
    if not os.path.isfile(path):
        return None
    d = open(path, "rb").read()
    flags = struct.unpack_from("<I", d, 0)[0]
    if flags != 0:
        return None                      # encrypted / checksummed; not handled
    count = struct.unpack_from("<H", d, 4)[0]
    data0 = 10 + count * 12
    want = mix_hash(name)
    for i in range(count):
        h, off, ln = struct.unpack_from("<IiI", d, 10 + i * 12)
        if (h & 0xFFFFFFFF) == want:
            return d[data0 + off: data0 + off + ln]
    return None


def tmpra_tiles(blob):
    """TmpRA -> (w, h, [tile index arrays]). Layout per TmpRALoader.cs:55-85."""
    w, h = struct.unpack_from("<HH", blob, 0)
    img_start = struct.unpack_from("<I", blob, 16)[0]
    index_end = struct.unpack_from("<i", blob, 28)[0]
    index_start = struct.unpack_from("<i", blob, 36)[0]
    out = []
    for b in blob[index_start:index_end]:
        if b == 255:
            out.append(None)
            continue
        o = img_start + b * w * h
        out.append(blob[o:o + w * h])
    return w, h, out


def terrain(pal_t):
    """Real grass, best available source. Returns (image, provenance string)."""
    blob = mix_read(MIX, "clear1.tem")
    if blob:
        w, h, tiles = tmpra_tiles(blob)
        good = [t for t in tiles if t]
        src = f"clear1.tem from {MIX} (unencrypted mix, {len(good)} sub-tiles)"
    else:
        cand = [os.path.join(REPO_TEM, n) for n in ("decg.tem", "decc.tem", "dech.tem")]
        cand = [c for c in cand if os.path.isfile(c)]
        if not cand:
            return None, "NONE -- no tile source; caller must fall back to flat colour"
        w, h, tiles = tmpra_tiles(open(cand[0], "rb").read())
        good = [t for t in tiles if t]
        src = f"{os.path.basename(cand[0])} (in-repo, {len(good)} sub-tiles)"
    n = 6
    sheet = Image.new("RGBA", (w * n, h * n))
    for i in range(n * n):
        t = good[i % len(good)]
        sheet.paste(paint(pal_t, w, h, t, -1), ((i % n) * w, (i // n) * h))
    return sheet, src


def contact(path, count, cols, scale=3):
    pal = load_palette(PAL)
    w, h, fr = read_shp(path)
    n = min(count, len(fr))
    rows = (n + cols - 1) // cols
    im = Image.new("RGBA", (w * cols, h * rows), (24, 28, 34, 255))
    for i in range(n):
        f = paint(pal, w, h, fr[i], SH_PLAYER)
        im.paste(f, ((i % cols) * w, (i // cols) * h), f)
    im = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
    out = os.path.join(HERE, "assets", "_contact.png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    im.save(out)
    print(f"{os.path.basename(path)}: {w}x{h}, {len(fr)} frames -> {out}")


def main():
    if len(sys.argv) > 2 and sys.argv[1] == "--contact":
        return contact(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 64, 8)

    pal = load_palette(PAL)
    pal_t = load_palette(TERRAIN_PAL_ALT) if os.path.isfile(TERRAIN_PAL_ALT) else pal
    ramp = {k: team_ramp(pal, v) for k, v in TEAMS.items()}
    out = {}

    units = [
        ("abrams", os.path.join(VEH, "abrams-correction.shp"), SE_CLASSIC, SE_CLASSIC + 32, "us", 1.25),
        ("t90",    os.path.join(VEH, "t90.shp"),               SE_CLASSIC, SE_CLASSIC + 32, "ru", 1.25),
        ("e3us",   os.path.join(INF, "e3.shp"),                SE_INF8,    None,            "us", 0.65),
        ("e3ru",   os.path.join(INF, "e3.shp"),                SE_INF8,    None,            "ru", 0.65),
    ]
    for key, path, fr, tur, team, sc in units:
        if not os.path.isfile(path):
            print("MISSING", path)
            continue
        im, bb = unit_sprite(pal, path, fr, ramp[team], tur, sc)
        out[key] = {"w": im.width, "h": im.height, "src": uri(im),
                    "frame": fr, "turret": tur, "scale": sc,
                    "inkbox": list(bb) if bb else None,
                    "file": os.path.basename(path)}
        print(f"  {key:7s} {os.path.basename(path):24s} f{fr}"
              f"{'+'+str(tur) if tur else '':>4s}  ink->{im.width}x{im.height} @{sc}")

    decos = [
        ("dmg_inf", "pip-damage-infantry.shp", 5,  SH_CHROME),
        ("dmg_veh", "pip-damage-vehicle.shp",  5,  SH_CHROME),
        ("supp",    "pip-suppression.shp",     10, SH_CHROME),
        ("rank",    "rank.shp",                4,  SH_PLAYER),
        ("ammo",    "pip-ammo.shp",            4,  SH_CHROME),
        ("pips2",   "pips2.shp",               8,  SH_CHROME),
    ]
    for key, fn, n, shadow in decos:
        p = os.path.join(PIPS, fn)
        if not os.path.isfile(p):
            print("MISSING", p)
            continue
        w, h, fr = read_shp(p)
        for i in range(min(n, len(fr))):
            out[f"{key}{i}"] = {"w": w, "h": h, "src": uri(paint(pal, w, h, fr[i], shadow)),
                                "file": f"{fn} f{i}"}

    cp = os.path.join(CLS, "e3_class.shp")
    if os.path.isfile(cp):
        w, h, fr = read_shp(cp)
        out["class0"] = {"w": w, "h": h, "src": uri(paint(pal, w, h, fr[0], SH_PLAYER)),
                         "file": "e3_class.shp"}

    tile, prov = terrain(pal_t)
    if tile:
        out["_terrain"] = {"src": uri(tile), "w": tile.width, "h": tile.height,
                           "cell": CELL, "provenance": prov}
    print(f"terrain: {prov}")

    payload = "const ASSETS = " + json.dumps(out) + ";"
    if not os.path.isfile(HTML):
        print(f"(HTML not present: {HTML})\nbuilt {len(out)} assets")
        return 0

    src = open(HTML, encoding="utf-8").read()
    new, n = re.subn(r"(?m)^const ASSETS = .*$", lambda _: payload, src, count=1)
    if n != 1:
        sys.exit("no 'const ASSETS = ...' line in " + HTML)
    ttf = os.path.join(REPO, "engine", "mods", "common", "FreeSansBold.ttf")
    font = base64.b64encode(open(ttf, "rb").read()).decode("ascii")
    new, n = re.subn(r"(?m)^const FONT_B64 = .*$",
                     lambda _: 'const FONT_B64 = "' + font + '";', new, count=1)
    if n != 1:
        sys.exit("no 'const FONT_B64 = ...' line in " + HTML)
    open(HTML, "w", encoding="utf-8", newline="\n").write(new)
    print(f"injected {len(out)} assets + font")
    return 0


if __name__ == "__main__":
    sys.exit(main())
