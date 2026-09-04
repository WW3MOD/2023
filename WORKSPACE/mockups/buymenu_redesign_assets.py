#!/usr/bin/env python3
"""Splice real decoded pixels into buymenu-redesign.html.

Run from the repo root:  python WORKSPACE/mockups/buymenu_redesign_assets.py

Three sources, all shipped art:
  * cameo SHPs           -- mods/ww3mod/bits/misc/icons/*.shp, via buymenu_shp_dump
  * iconchevrons.shp     -- mods/ww3mod/bits/misc/ui/iconchevrons.shp, frames 0-2
  * the sidebar frame    -- mods/ww3mod/uibits/sidebar.png, the regions named by
                            chrome.yaml's sidebar-nato collection

The frame matters: Container@PALETTE_FOREGROUND draws background-iconrow OVER the
production palette (ingame-player.yaml:1186-1195, declared after the palette), so
the row's left 41px and right 9px are brushed-metal chrome, not free space.

Same palette caveat as buymenu_shp_dump: temperat.pal is substituted from the
map-local copy, so geometry is exact and hues are approximate.
"""

import base64
import io
import os
import re
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from buymenu_shp_dump import PAL, find, load_palette, read_shp, to_png  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
HTML = os.path.join(HERE, "buymenu-redesign.html")
SIDEBAR = os.path.join(REPO, "mods", "ww3mod", "uibits", "sidebar.png")
GLYPHS = os.path.join(REPO, "mods", "ww3mod", "uibits", "glyphs.png")

# (key, shp stem). Deliberately mixed: 64-wide and 60-wide art, long captions and
# short ones, all three tabs.
CAMEOS = [
    ("abrams",     "abramsicon"),
    ("bradley",    "bradleyicon"),
    ("humvee",     "humveeicon"),
    ("m113",       "m113icon"),
    ("m270",       "m270icon"),
    ("himars",     "himarsicon"),
    ("conscript",  "e1americaicon"),
    ("at",         "atamericaicon"),
    ("littlebird", "littlebirdicon"),
]

# chrome.yaml sidebar-nato Regions / production-icons Regions.
FRAME = {
    "iconrow": (SIDEBAR, 0, 116, 238, 47),    # background-iconrow  (chrome.yaml:32)
    "iconbg":  (SIDEBAR, 12, 227, 190, 47),   # background-iconbg   (chrome.yaml:31)
    "bottom":  (SIDEBAR, 0, 166, 238, 8),     # background-bottom   (chrome.yaml:33)
    "tabinf":  (GLYPHS, 34, 68, 16, 16),      # production-icons infantry
    "tabveh":  (GLYPHS, 51, 68, 16, 16),      # production-icons vehicle
    "tabair":  (GLYPHS, 68, 68, 16, 16),      # production-icons aircraft
}


def uri(im):
    buf = io.BytesIO()
    im.save(buf, "PNG")
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii")


def main():
    pal = load_palette(PAL)
    out = {}

    for key, stem in CAMEOS:
        path = find(stem)
        if path is None:
            sys.exit("missing cameo " + stem)
        w, h, frames = read_shp(path)
        im = to_png(w, h, frames[0], pal, transparent=(0,), shadow=(3,))
        out[key] = {"w": w, "h": h, "src": uri(im), "file": stem + ".shp"}

    # iconchevrons frames, cropped to ink so the HTML places them by drawn size.
    w, h, frames = read_shp(find("iconchevrons"))
    for i, f in enumerate(frames[:3]):
        im = to_png(w, h, f, pal, transparent=(0,), shadow=())
        crop = im.crop(im.getbbox())
        out[f"chev{i}"] = {"w": crop.width, "h": crop.height, "src": uri(crop),
                           "file": f"iconchevrons.shp frame {i}"}

    for key, (src, x, y, w, h) in FRAME.items():
        im = Image.open(src).convert("RGBA").crop((x, y, x + w, y + h))
        out[key] = {"w": w, "h": h, "src": uri(im),
                    "file": f"{os.path.basename(src)} {x},{y},{w},{h}"}

    payload = "const ASSETS = " + repr(out).replace("'", '"') + ";"
    src = open(HTML, encoding="utf-8").read()
    new, n = re.subn(r"(?m)^const ASSETS = .*$", payload, src, count=1)
    if n != 1:
        sys.exit("no 'const ASSETS = ...' line in " + HTML)

    # TinyBold is FreeSansBold @ 10; embed it so badge digits measure like the engine's.
    ttf = os.path.join(REPO, "engine", "mods", "common", "FreeSansBold.ttf")
    font = base64.b64encode(open(ttf, "rb").read()).decode("ascii")
    new, n = re.subn(r"(?m)^const FONT_B64 = .*$",
                     'const FONT_B64 = "' + font + '";', new, count=1)
    if n != 1:
        sys.exit("no 'const FONT_B64 = ...' line in " + HTML)

    open(HTML, "w", encoding="utf-8", newline="\n").write(new)
    print(f"injected {len(out)} assets ({len(payload)} bytes) + font ({len(font)} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
