#!/usr/bin/env python3
"""The scar's outer boundary, with and without the sparser edge cuts.

WHY A SECOND SHORELINE TOOL
---------------------------
`shore_fade_preview.py` answered a different question and answered it: the fade's
METRIC was Chebyshev, whose iso-contours are literal squares, so the faded region
around a river bend was drawn as a rectangle with corners several cells clear of
any water. That is fixed, and this tool does not revisit it.

What is left is not the metric. It is that a smudge is ONE FLAT ALPHA OVER A WHOLE
CELL, so at partial coverage the CELL is the visible unit: bright sand hard against
dark, along square axis-aligned edges. Dimming cannot reach below the cell, and in
any case there is barely any dimming left to see -- with `ShoreFadeCells: 2` and
`ShoreFadeMinAlpha: 0.7` the entire ramp spans alpha 0.800 to 1.0.

So the boundary cells draw SPARSER ART instead: cuts of the same band at 50% and
25% of its coverage, which `gen_scars.py` generates as nested subsets of the same
noise field, and which `SmudgeLayer` selects by the shore-fade value it already
computes. This renders that, over the real terrain of the scenario the artefact was
reported on.

WHERE IT LOOKS
--------------
`demo-nuke-river-zeta`, ground zero 38,20, centred on 45,40 -- the beach 21 cells
out that the demo's own frame `05-shore-outer` is pointed at. Terrain comes from
that scenario's `map.bin`; band membership, the `AcceptsSmudgeType` gate and the
fade ramp are the engine's, reproduced in `shore_fade_preview.build`.

WHAT THE GROUND ART IS NOT
--------------------------
Cells are drawn with real `clear1.tem` or `w1.tem`, so Beach and Rock appear as
clear ground -- the extracted tile cache carries no sand or rock templates. What is
exact is which cells take a scar, at what strength, and from which cut. The tier map
is the panel to read for the mechanism.

Usage:  python tools/impact-scar/scar_edge_preview.py [--out PATH]
"""
import argparse
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "nav-guard"))

import contact_sheet as cs  # noqa: E402
import numpy as np  # noqa: E402
import shore_fade_preview as sfp  # noqa: E402
from PIL import Image  # noqa: E402
from modload import load_map, load_tileset  # noqa: E402
from pathlib import Path  # noqa: E402

# world.yaml's EdgeAlphaThresholds on all five Scar bands, sparsest tier first.
EDGE_THRESHOLDS = [0.81, 0.91]

# Rings to report the gradient over, as Euclidean distance to the nearest cell that
# refuses a scar. The names are the distances a cell can actually be: edge-on,
# diagonal, two cells, then everything the fade still reaches.
RINGS = [("d=1", 1.0), ("d=1.41", 1.45), ("d=2", 2.0), ("d=2..3", 3.0), ("inland", 99.0)]


def ring_profile(img, scen, x0, y0, w, h):
    """Mean cell luminance by distance to the nearest unscarrable cell.

    THIS IS THE NUMBER THE DEFECT IS. A boundary that dissolves has a gradient here; a
    boundary that ends on a cell edge does not, however carefully its alpha was ramped.
    Measured on the shipped code at this window every ring reads 43.3-44.4 -- flat to
    within a luminance point, which is what "bright sand hard against dark" means when
    it is counted rather than looked at."""
    gm = load_map(Path(scen))
    ts = load_tileset(Path(os.path.join(cs.REPO, "mods/ww3mod/tilesets/temperat.yaml")))

    def terrain(x, y):
        return gm.terrain_type(ts, x, y) if 0 <= x < gm.width and 0 <= y < gm.height else None

    def distance_to_boundary(x, y):
        best = 99.0
        for dy in range(-3, 4):
            for dx in range(-3, 4):
                t = terrain(x + dx, y + dy)
                if t is not None and t not in sfp.ACCEPTS:
                    best = min(best, math.sqrt(dx * dx + dy * dy))
        return best

    a = np.asarray(img.convert("RGB")).astype(int)
    lum = a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114

    buckets = {}
    for gy in range(y0, y0 + h):
        for gx in range(x0, x0 + w):
            t = terrain(gx, gy)
            if t is None or t not in sfp.ACCEPTS:
                continue
            d = distance_to_boundary(gx, gy)
            name = next(n for n, limit in RINGS if d <= limit)
            cell = lum[(gy - y0) * cs.CELL:(gy - y0 + 1) * cs.CELL,
                       (gx - x0) * cs.CELL:(gx - x0 + 1) * cs.CELL]
            buckets.setdefault(name, []).append(cell.mean())

    return [(n, len(buckets[n]), float(np.mean(buckets[n]))) for n, _l in RINGS if n in buckets]


HTML = """<!doctype html><meta charset="utf-8">
<title>scar edge cuts &mdash; {scen}</title>
<style>
 body{{background:#101319;color:#d6dae1;font:13.5px/1.6 system-ui,sans-serif;margin:0;padding:24px 30px 70px;max-width:1700px}}
 h1{{font-size:20px;margin:0 0 4px}}
 h2{{font-size:15px;margin:34px 0 6px;color:#e6dfb2;border-bottom:1px solid #2c3140;padding-bottom:6px}}
 p{{max-width:96ch;color:#a0a7b2;margin:8px 0 14px}}
 code{{background:#1a1f28;padding:1px 5px;border-radius:3px;color:#c9d4e4;font-size:12.5px}}
 .row{{display:flex;gap:22px;flex-wrap:wrap;align-items:flex-start}}
 .panel{{background:#0b0e13;border:1px solid #262c38;border-radius:6px;padding:11px}}
 .panel h3{{font-size:12px;margin:0 0 8px;letter-spacing:.05em;text-transform:uppercase;font-weight:600}}
 .a h3{{color:#e0913f}} .b h3{{color:#5cc47c}} .c h3{{color:#7fa8d8}}
 img{{display:block;image-rendering:pixelated;border-radius:3px}}
 .cap{{margin-top:8px;font-size:12px;color:#848b96;max-width:42ch}}
 .note{{background:#171b22;border-left:3px solid #7d8a4a;padding:11px 16px;margin:18px 0;border-radius:0 4px 4px 0}}
 .warn{{border-left-color:#b0763a}}
 .key span{{display:inline-block;width:11px;height:11px;border-radius:2px;margin:0 5px 0 14px;vertical-align:-1px}}
</style>
<h1>The scar's outer boundary &mdash; alpha alone against sparser art</h1>
<p>Scenario <code>{scen}</code>, cells {x0},{y0} to {x1},{y1}, ground zero {gzx},{gzy},
<code>AtomicHighYield</code> bands. This window is the beach {dist} cells out that the demo's
own <code>05-shore-outer</code> frame is aimed at. Terrain is read from the scenario's
<code>map.bin</code>; band membership, the <code>AcceptsSmudgeType</code> gate and
<code>ShoreAlphaAt</code> are the engine's. No game was launched.</p>

<div class="note warn"><b>The ground art is not the tileset.</b> Cells are drawn with real
<code>clear1.tem</code> or <code>w1.tem</code>, so Beach and Rock appear as clear ground &mdash;
the extracted tile cache carries no sand or rock templates. What is exact here is
<i>which cells take a scar, at what strength, and from which cut</i>.</div>

<h2>1 &nbsp;The boundary</h2>
<div class="row">
  <div class="panel a"><h3>Today &mdash; one sprite, ramped alpha</h3><img src="{a}">
  <div class="cap">Every scarred cell draws the same art at alpha 0.80&ndash;1.00. A cell is the
  smallest thing that can change, so the waterline is a staircase of square cell edges.</div></div>
  <div class="panel b"><h3>Edge cuts &mdash; 25% and 50% of the band</h3><img src="{b}">
  <div class="cap">The ramped alpha is unchanged; what differs is that the two cells nearest the
  water draw thinner cuts of their own variant, so the boundary breaks up into stipple
  instead of ending on a cell edge.</div></div>
</div>

<h2>2 &nbsp;Which cut each cell draws</h2>
<p class="key">Not art: this is <code>EdgeSequenceFor</code> evaluated over the real terrain.
<span style="background:#4a4a4a"></span>band's own art
<span style="background:#969696"></span>50% cut
<span style="background:#cecece"></span>25% cut
<span style="background:#182a56"></span>takes no scar
<span style="background:#282828"></span>outside the disc</p>
<div class="row">
  <div class="panel c"><h3>Tier field</h3><img src="{t}"></div>
  <div class="panel c"><h3>Alpha field, for comparison</h3><img src="{al}">
  <div class="cap">The same cells, as the shore fade sees them. White is full strength. The
  whole visible range is 0.80 to 1.00 &mdash; which is how little there was to see in alpha
  alone.</div></div>
</div>

<h2>3 &nbsp;The gradient, counted</h2>
<p>Mean cell luminance by Euclidean distance to the nearest cell that refuses a scar.
A boundary that dissolves has a gradient in this column; a boundary that ends on a cell
edge does not, however carefully its alpha was ramped.</p>
{profile}
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scenario", default="tools/autotest/scenarios/demo-nuke-river-zeta")
    ap.add_argument("--out", default=os.path.join(cs.REPO, "WORKSPACE/mockups/scar-edge-cuts.html"))
    ap.add_argument("--gz", default="38,20", help="ground zero cell of the demo's strike")
    ap.add_argument("--centre", default="45,40", help="the demo's 05-shore-outer camera cell")
    ap.add_argument("--half", type=int, default=11)
    ap.add_argument("--fade", type=int, default=2)
    ap.add_argument("--floor", type=float, default=0.7)
    ap.add_argument("--seed", type=int, default=11)
    ap.add_argument("--zoom", type=int, default=4)
    args = ap.parse_args()

    gz = tuple(int(v) for v in args.gz.split(","))
    cx, cy = (int(v) for v in args.centre.split(","))
    scen = os.path.join(cs.REPO, args.scenario)

    composite, alphamap, tiermap, (x0, y0, w, h) = sfp.build(
        scen, gz, cx, cy, args.half, args.fade, args.seed)

    today = composite("euclidean", args.floor)
    edged = composite("euclidean", args.floor, EDGE_THRESHOLDS)
    tiers = tiermap("euclidean", args.floor, EDGE_THRESHOLDS)
    alphas = alphamap("euclidean", args.floor)

    dist = int(round(((cx - gz[0]) ** 2 + (cy - gz[1]) ** 2) ** 0.5))

    before = ring_profile(today, scen, x0, y0, w, h)
    after = ring_profile(edged, scen, x0, y0, w, h)
    rows = ["<table style='border-collapse:collapse'><tr>"
            "<th style='padding:4px 12px;text-align:left;color:#cfd6e2'>ring</th>"
            "<th style='padding:4px 12px;color:#cfd6e2'>cells</th>"
            "<th style='padding:4px 12px;color:#e0913f'>today</th>"
            "<th style='padding:4px 12px;color:#5cc47c'>edge cuts</th></tr>"]
    for (name, n, a_lum), (_n2, _c2, b_lum) in zip(before, after):
        rows.append(f"<tr><td style='padding:3px 12px'><code>{name}</code></td>"
                    f"<td style='padding:3px 12px;text-align:center;color:#7d858f'>{n}</td>"
                    f"<td style='padding:3px 12px;text-align:center'>{a_lum:.1f}</td>"
                    f"<td style='padding:3px 12px;text-align:center'>{b_lum:.1f}</td></tr>")
    rows.append("</table>")
    profile_html = "".join(rows)

    print("mean cell luminance by distance to unscarrable ground:")
    for (name, n, a_lum), (_n2, _c2, b_lum) in zip(before, after):
        print("  %-7s n=%-4d today %.1f -> edge cuts %.1f" % (name, n, a_lum, b_lum))

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as fh:
        fh.write(HTML.format(
            scen=os.path.basename(args.scenario), x0=x0, y0=y0, x1=x0 + w - 1, y1=y0 + h - 1,
            gzx=gz[0], gzy=gz[1], dist=dist,
            a=cs.b64(today, args.zoom), b=cs.b64(edged, args.zoom),
            t=cs.b64(tiers, args.zoom), al=cs.b64(alphas, args.zoom),
            profile=profile_html))
    print("wrote " + args.out)

    d = os.path.join(cs.REPO, "WORKSPACE/mockups")
    os.makedirs(d, exist_ok=True)
    for name, im in (("scar-edge-today", today), ("scar-edge-cuts", edged),
                     ("scar-edge-tiers", tiers)):
        path = os.path.join(d, name + ".png")
        im.resize((im.width * args.zoom, im.height * args.zoom), Image.NEAREST).save(path)
        print("wrote " + path)


if __name__ == "__main__":
    main()
