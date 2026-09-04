#!/usr/bin/env python3
"""Splice real, decoded cameo pixels into buymenu-icon-arrangements.html.

Run from the repo root:  python WORKSPACE/mockups/buymenu_build_assets.py

Reads the shipped SHPs through buymenu_shp_dump, renders them to PNG, and
rewrites the ASSETS line in the HTML so the mockup is self-contained (data
URIs -- an artifact iframe cannot fetch sibling files).
"""

import base64
import io
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from buymenu_shp_dump import PAL, find, load_palette, read_shp, to_png  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
HTML = os.path.join(HERE, "buymenu-icon-arrangements.html")

CAMEOS = [
    ("abrams", "abramsicon"),
    ("bradley", "bradleyicon"),
    ("humvee", "humveeicon"),
    ("m113", "m113icon"),
    ("m270", "m270icon"),
    ("himars", "himarsicon"),
    ("conscript", "e1americaicon"),
    ("at", "atamericaicon"),
    ("littlebird", "littlebirdicon"),
]


def uri(im):
    buf = io.BytesIO()
    im.save(buf, "PNG")
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii")


def main():
    pal = load_palette(PAL)
    out = {}

    for key, stem in CAMEOS:
        w, h, frames = read_shp(find(stem))
        im = to_png(w, h, frames[0], pal, transparent=(0,), shadow=(3,))
        out[key] = {"w": w, "h": h, "src": uri(im), "file": stem + ".shp"}

    # iconchevrons.shp frames, cropped to their ink bbox so the HTML can place
    # them by their real drawn size rather than the 15x20 canvas.
    w, h, frames = read_shp(find("iconchevrons"))
    for i, f in enumerate(frames):
        im = to_png(w, h, f, pal, transparent=(0,), shadow=())
        bbox = im.getbbox()
        crop = im.crop(bbox)
        out[f"chev{i}"] = {"w": crop.width, "h": crop.height, "src": uri(crop),
                           "file": f"iconchevrons.shp frame {i}"}

    payload = "const ASSETS = " + repr(out).replace("'", '"') + ";"
    src = open(HTML, encoding="utf-8").read()
    new, n = re.subn(r"(?m)^const ASSETS = .*$", payload, src, count=1)
    if n != 1:
        sys.exit("no 'const ASSETS = ...' line found in " + HTML)

    # The mod's TinyBold is FreeSansBold @ 10. Embed it so the badge digits and the
    # READY/countdown text are measured in the real face rather than a browser default.
    ttf = os.path.join(os.path.dirname(HERE), "..", "engine", "mods", "common", "FreeSansBold.ttf")
    ttf = os.path.abspath(ttf)
    font = base64.b64encode(open(ttf, "rb").read()).decode("ascii")
    new, n = re.subn(r'(?m)^const FONT_B64 = .*$',
                     'const FONT_B64 = "' + font + '";', new, count=1)
    if n != 1:
        sys.exit("no 'const FONT_B64 = ...' line found in " + HTML)

    open(HTML, "w", encoding="utf-8", newline="\n").write(new)
    print(f"injected {len(out)} assets ({len(payload)} bytes) + font ({len(font)} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
