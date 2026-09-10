#!/usr/bin/env python3
"""Render the scar shoreline offline, over the REAL terrain of a real scenario.

WHY THIS EXISTS
---------------
`contact_sheet.py` can only express water as a half-plane (`water_from_x`), which
is a straight vertical line -- so it is structurally incapable of showing the
artefact this script was written to investigate: a scar meeting a narrow, winding
river and a 4-cell ford. It would have drawn a straight edge no matter what the
code did, and "the picture shows a straight edge" would have proven nothing.

So this reads the actual per-cell terrain of a scenario out of `map.bin` (via
`tools/nav-guard/modload.py`, static, no build and no launch) and runs the engine's
own per-cell decisions over it:

  * band membership by `ceil(sqrt(dx^2+dy^2))`, the engine's bucket (MapGrid.cs:210)
  * the terrain gate -- `AcceptsSmudgeType`, LeaveSmudgeWarhead.cs:73-74
  * the shore fade -- `SmudgeLayer.ShoreAlphaAt`, SmudgeLayer.cs:376-395, reproduced
    below rather than approximated, including the off-map rule

WHAT THE TERRAIN ART IS AND IS NOT
----------------------------------
Ground is drawn as `clear1.tem` and water as `w1.tem`, the real decoded tiles. Rock,
Cliffs and Beach are drawn with the CLEAR art because the extracted tile cache holds
no rock/cliff/sand templates -- so this picture is honest about which cells take a
scar and at what strength, and is NOT a picture of what the tileset looks like. The
alpha panel is the one to read for the mechanism; the composite is there to show what
the steps do to the eye.

Usage:  python tools/impact-scar/shore_fade_preview.py [--out PATH]
"""
import argparse
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "nav-guard"))

import contact_sheet as cs  # noqa: E402
from PIL import Image  # noqa: E402
from modload import load_map, load_tileset  # noqa: E402
from pathlib import Path  # noqa: E402

EXT = "tem"

# AtomicHighYield, weapons-superweapons.yaml:1422-1452. (type, inner, outer) in cells.
BANDS = [
    ("ScarCore", 0, 7),
    ("ScarCrater", 8, 16),
    ("ScarChar", 17, 27),
    ("ScarBurn", 28, 38),
    ("ScarRim", 39, 47),
]

# The five Scar types, as every ground-ish terrain type in temperat.yaml now lists them.
SCAR_TYPES = {b[0] for b in BANDS}

# Which terrain types carry the five Scar bands in AcceptsSmudgeType
# (mods/ww3mod/tilesets/temperat.yaml). Water, River and RiverShallow carry none.
ACCEPTS = {"Clear", "Rough", "Debris", "Road", "Bridge", "Wall", "Beach", "Rock", "Cliffs"}
WATERY = {"Water", "River", "RiverShallow"}


def shore_alpha(x, y, fade, is_boundary, metric="chebyshev", floor=0.0):
    """SmudgeLayer.ShoreAlphaAt (SmudgeLayer.cs:376-395), plus the two levers under test.

    metric="chebyshev", floor=0.0 is EXACTLY the shipped function.
    """
    if fade <= 0:
        return 1.0

    best = float(fade + 1)
    for dy in range(-fade, fade + 1):
        for dx in range(-fade, fade + 1):
            if metric == "chebyshev":
                d = float(max(abs(dx), abs(dy)))
            else:
                d = math.sqrt(dx * dx + dy * dy)
            if d >= best or not is_boundary(x + dx, y + dy):
                continue
            best = d

    raw = min(1.0, best / float(fade + 1))
    if floor <= 0.0:
        return raw
    # Same shape, but the ramp starts at `floor` instead of at 1/(fade+1), so the cell
    # ON the shoreline is still unmistakably scarred.
    return min(1.0, floor + (1.0 - floor) * raw)


def build(scenario, gz, cx, cy, half, fade, seed):
    gm = load_map(Path(scenario))
    ts = load_tileset(Path(os.path.join(cs.REPO, "mods/ww3mod/tilesets/temperat.yaml")))

    x0, x1 = cx - half, cx + half
    y0, y1 = cy - half, cy + half
    w = x1 - x0 + 1
    h = y1 - y0 + 1

    def terrain(x, y):
        if not (0 <= x < gm.width and 0 <= y < gm.height):
            return None
        return gm.terrain_type(ts, x, y)

    def is_boundary(x, y):
        t = terrain(x, y)
        # `world.Map.Contains(c) && !accepts(c)` -- off-map is deliberately FALSE.
        return t is not None and t not in ACCEPTS

    # ---- terrain background, real decoded tiles
    rng = random.Random(seed)
    clears = [Image.open(os.path.join(cs.TERRAIN, "tem", f)).convert("RGBA")
              for f in sorted(os.listdir(os.path.join(cs.TERRAIN, "tem")))]
    waters = [Image.open(os.path.join(cs.TERRAIN, "water", f)).convert("RGBA")
              for f in sorted(os.listdir(os.path.join(cs.TERRAIN, "water")))]

    bg = Image.new("RGBA", (w * cs.CELL, h * cs.CELL))
    for gy in range(y0, y1 + 1):
        for gx in range(x0, x1 + 1):
            t = terrain(gx, gy)
            src = waters if t in WATERY else clears
            bg.paste(rng.choice(src), ((gx - x0) * cs.CELL, (gy - y0) * cs.CELL))

    # ---- which band each cell is in, and whether it takes a smudge at all
    vrng = random.Random(seed + 1)
    placed = {}
    for gy in range(y0, y1 + 1):
        for gx in range(x0, x1 + 1):
            dx, dy = gx - gz[0], gy - gz[1]
            r = math.ceil(math.sqrt(dx * dx + dy * dy))
            band = next((b for b in BANDS if b[1] <= r <= b[2]), None)
            if band is None:
                continue
            t = terrain(gx, gy)
            if t is None or t not in ACCEPTS:
                continue
            # SmudgeLayer.AddSmudge picks a random variant; depth stays 0 because the
            # bands are annuli and this is a single strike.
            placed[(gx, gy)] = (band[0], vrng.choice(cs.VARIANTS[band[0]]), 0)

    def composite(metric, floor):
        out = bg.copy()
        for layer in cs.LAYER_ORDER:
            for (gx, gy), (band, variant, depth) in sorted(placed.items(), key=lambda kv: (kv[0][1], kv[0][0])):
                if band != layer:
                    continue
                a = shore_alpha(gx, gy, fade, is_boundary, metric, floor)
                if a <= 0:
                    continue
                sp = cs.load_sprite(variant, EXT)[depth]
                if a < 1.0:
                    sp = sp.copy()
                    alpha = sp.getchannel("A").point(lambda v: int(round(v * a)))
                    sp.putalpha(alpha)
                px = (gx - x0) * cs.CELL + (cs.CELL - sp.width) // 2
                py = (gy - y0) * cs.CELL + (cs.CELL - sp.height) // 2
                out.alpha_composite(sp, (px, py))
        return out

    def alphamap(metric, floor):
        """The fade field itself, as a picture. Grey = drawn at that strength;
        deep blue = a cell that takes no scar at all (water, river)."""
        im = Image.new("RGBA", (w * cs.CELL, h * cs.CELL))
        for gy in range(y0, y1 + 1):
            for gx in range(x0, x1 + 1):
                if (gx, gy) not in placed:
                    col = (24, 42, 86, 255) if terrain(gx, gy) in WATERY else (40, 40, 40, 255)
                else:
                    a = shore_alpha(gx, gy, fade, is_boundary, metric, floor)
                    v = int(round(a * 255))
                    col = (v, v, v, 255)
                im.paste(col, ((gx - x0) * cs.CELL, (gy - y0) * cs.CELL,
                               (gx - x0 + 1) * cs.CELL, (gy - y0 + 1) * cs.CELL))
        return im

    return composite, alphamap, (x0, y0, w, h)


HTML = """<!doctype html><meta charset="utf-8">
<title>scar shore fade &mdash; {scen}</title>
<style>
 body{{background:#101319;color:#d6dae1;font:13.5px/1.6 system-ui,sans-serif;margin:0;padding:24px 30px 70px;max-width:1600px}}
 h1{{font-size:20px;margin:0 0 4px}}
 h2{{font-size:15px;margin:34px 0 6px;color:#e6dfb2;border-bottom:1px solid #2c3140;padding-bottom:6px}}
 p{{max-width:96ch;color:#a0a7b2;margin:8px 0 14px}}
 code{{background:#1a1f28;padding:1px 5px;border-radius:3px;color:#c9d4e4;font-size:12.5px}}
 .row{{display:flex;gap:22px;flex-wrap:wrap;align-items:flex-start}}
 .panel{{background:#0b0e13;border:1px solid #262c38;border-radius:6px;padding:11px}}
 .panel h3{{font-size:12px;margin:0 0 8px;letter-spacing:.05em;text-transform:uppercase;font-weight:600}}
 .a h3{{color:#e0913f}} .b h3{{color:#5cc47c}} .c h3{{color:#7fa8d8}}
 img{{display:block;image-rendering:pixelated;border-radius:3px}}
 .cap{{margin-top:8px;font-size:12px;color:#848b96;max-width:40ch}}
 .note{{background:#171b22;border-left:3px solid #7d8a4a;padding:11px 16px;margin:18px 0;border-radius:0 4px 4px 0}}
 .warn{{border-left-color:#b0763a}}
</style>
<h1>The scar at a shoreline &mdash; rendered offline from real terrain</h1>
<p>Scenario <code>{scen}</code>, cells {x0},{y0} to {x1},{y1}, ground zero {gzx},{gzy},
<code>AtomicHighYield</code> bands. Terrain comes from the scenario's own <code>map.bin</code>;
the scar placement, the <code>AcceptsSmudgeType</code> gate and the
<code>ShoreAlphaAt</code> ramp are the engine's, reproduced line for line. No game was launched.</p>

<div class="note warn"><b>The ground art is not the tileset.</b> Cells are drawn with real
<code>clear1.tem</code> or <code>w1.tem</code>, so Rock, Cliffs and Beach appear as clear ground &mdash;
the extracted tile cache carries no rock or sand templates. What is exact here is <i>which cells take a
scar and at what strength</i>. Read the alpha panels for the mechanism.</p>

<h2>1 &nbsp;The fade field &mdash; this is the artefact, on its own</h2>
<p>White is a cell drawn at full strength, grey is a cell held below it, blue is a cell that takes no
scar at all. Nothing here is art: it is <code>ShoreAlphaAt</code> evaluated over the real terrain.</p>
<div class="row">
  <div class="panel a"><h3>Today &mdash; Chebyshev, ramp from 1/3</h3><img src="{a_alpha}">
  <div class="cap">Three flat steps, and the contours are literal squares because Chebyshev distance
  makes them so. The ford is held at 1/3 across its whole width.</div></div>
  <div class="panel b"><h3>Proposed &mdash; Euclidean, floor {floor}</h3><img src="{b_alpha}">
  <div class="cap">The shoreline cell stays clearly scarred, and the contour follows the water instead
  of boxing it.</div></div>
</div>

<h2>2 &nbsp;What that does to the picture</h2>
<div class="row">
  <div class="panel a"><h3>Today</h3><img src="{a_img}"></div>
  <div class="panel b"><h3>Proposed</h3><img src="{b_img}"></div>
</div>
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scenario", default="tools/autotest/scenarios/demo-highyield-nuke")
    ap.add_argument("--out", default=os.path.join(cs.REPO, "WORKSPACE/mockups/scar-shore-fade.html"))
    ap.add_argument("--gz", default="64,64")
    ap.add_argument("--centre", default="72,51")
    ap.add_argument("--half", type=int, default=13)
    ap.add_argument("--fade", type=int, default=2)
    ap.add_argument("--floor", type=float, default=0.7)
    ap.add_argument("--seed", type=int, default=11)
    ap.add_argument("--zoom", type=int, default=3)
    args = ap.parse_args()

    gz = tuple(int(v) for v in args.gz.split(","))
    cx, cy = (int(v) for v in args.centre.split(","))
    scen = os.path.join(cs.REPO, args.scenario)

    composite, alphamap, (x0, y0, w, h) = build(scen, gz, cx, cy, args.half, args.fade, args.seed)

    a_img = composite("chebyshev", 0.0)
    b_img = composite("euclidean", args.floor)
    a_alpha = alphamap("chebyshev", 0.0)
    b_alpha = alphamap("euclidean", args.floor)

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as fh:
        fh.write(HTML.format(
            scen=os.path.basename(args.scenario), x0=x0, y0=y0, x1=x0 + w - 1, y1=y0 + h - 1,
            gzx=gz[0], gzy=gz[1], floor=args.floor,
            a_img=cs.b64(a_img, args.zoom), b_img=cs.b64(b_img, args.zoom),
            a_alpha=cs.b64(a_alpha, args.zoom), b_alpha=cs.b64(b_alpha, args.zoom)))
    print("wrote " + args.out)

    # PNGs too, so they can be looked at directly.
    d = os.path.join(cs.REPO, "WORKSPACE/mockups")
    for name, im in (("scar-shore-today", a_img), ("scar-shore-proposed", b_img),
                     ("scar-shore-alpha-today", a_alpha), ("scar-shore-alpha-proposed", b_alpha)):
        p = os.path.join(d, name + ".png")
        im.resize((im.width * args.zoom, im.height * args.zoom), Image.NEAREST).save(p)
        print("wrote " + p)


if __name__ == "__main__":
    main()
