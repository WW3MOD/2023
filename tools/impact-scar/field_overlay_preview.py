#!/usr/bin/env python3
"""Show what a nuclear scar looks like ON a crop field, without launching the game.

WHY THIS EXISTS
---------------
`wt/scar-over-fields` draws a second smudge pass after actors so scars appear over
crop fields. The mechanism is sound, but the worker that built it ranked ONE risk
above all others and could not settle it, because settling it needs pixels:

    the scar sprites are not opaque. Coverage peaks at 78-93% on ScarCore and
    never reaches 1.0 -- deliberately, because a fully-opaque cell is a 24x24
    square of solid colour and a disc built from those has visibly square edges,
    which is the exact artefact the banding work removed (gen_scars.py:68-70).

So the decal is a stipple. Over bare earth the gaps show dark terrain; over a crop
field the same gaps show BRIGHT GREEN CROP. The feature can therefore be working
perfectly and the farmland still read as green squares inside the blast -- which is
the user's original complaint, softened rather than fixed.

That is a question about appearance, and no amount of reading settles it. This
renders it.

WHAT IT IS NOT
--------------
Not an artistic impression. It reuses `contact_sheet.py` wholesale -- the same
cell bucketing (`ceil(sqrt(dx^2+dy^2))`, MapGrid.cs:210), the same variant choice
and depth-deepening (SmudgeLayer.AddSmudge), the same world.yaml layer Z order,
the same real palettes and the real generated SHPs. The crop art is the real
`v14`/`v17`/`v16` out of the shipped RA `temperat.mix`, read by the same decoder.

THE TWO PANELS ARE THE ENGINE'S TWO PASS ORDERS
-----------------------------------------------
    TODAY   terrain -> smudge -> actors
    CHANGE  terrain -> smudge -> actors -> smudge again, ground-cover cells only

which is precisely what the branch adds. The left panel should show the bright
rectangular holes the user reported; the right panel is the question.

Usage:  python tools/impact-scar/field_overlay_preview.py [--out PATH] [--radius N]
"""
import argparse
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import contact_sheet as cs  # noqa: E402
from PIL import Image  # noqa: E402

EXT = "tem"

# Candidates in preference order. `^CivField` members on river-zeta are v17 (1713
# of them), v16 (864) and rice (610); v14 is the one the small autotest rigs use.
# Whichever decodes first at roughly one cell is the one drawn.
FIELD_CANDIDATES = ["v14", "v17", "v16", "v12", "v13"]


def pick_field_sprite():
    """First candidate that decodes, with its size. Reports what it chose, because
    a silently-substituted sprite would change what the picture is evidence of."""
    for name in FIELD_CANDIDATES:
        try:
            frames = cs.load_sprite(name, EXT)
        except SystemExit:
            continue
        except Exception:
            continue
        if frames:
            return name, frames[0]
    raise SystemExit("no crop-field sprite could be decoded from temperat.mix")


def build(outer_r, seed):
    span = 2 * outer_r + 1
    pad = 2
    w = h = span + 2 * pad
    origin = (outer_r + pad, outer_r + pad)
    ox, oy = origin

    rng = random.Random(seed)
    base = cs.terrain_bg(w, h, EXT, rng)

    field_name, field_sp = pick_field_sprite()

    # A rectangular patch straddling the disc edge, so the same field art is
    # visible both inside and outside the blast in one image. That contrast is
    # the whole point: "did the fields go dark" is only answerable against
    # untouched fields of the same art a few cells away.
    fx0, fx1 = ox - outer_r + 1, ox + 3
    fy0, fy1 = oy - 4, oy + 4
    field_cells = {(cx, cy)
                   for cy in range(fy0, fy1 + 1)
                   for cx in range(fx0, fx1 + 1)
                   if 0 <= cx < w and 0 <= cy < h}

    # The engine's first pass: terrain, then smudges.
    sim = cs.SmudgeSim(EXT, seed)
    for name, inner, outer in cs.bands_for(outer_r):
        sim.disc(name, outer, inner, None, origin)
    scarred = sim.render(base, origin)

    def paste_fields(img):
        out = img.copy()
        for (cx, cy) in sorted(field_cells, key=lambda c: (c[1], c[0])):
            # Centre like SpriteFrame offset handling, matching cs.render.
            px = cx * cs.CELL + (cs.CELL - field_sp.width) // 2
            py = cy * cs.CELL + (cs.CELL - field_sp.height) // 2
            out.alpha_composite(field_sp, (px, py))
        return out

    def paste_scar_over(img, only_cells):
        """The branch's second pass: the SAME smudge sprites, re-composited, on
        cells whose every occupant is ground cover."""
        out = img.copy()
        for layer in cs.LAYER_ORDER:
            for (cx, cy), (name, depth) in sim.layers[layer].items():
                canvas = (cx + ox, cy + oy)
                if canvas not in only_cells:
                    continue
                sp = cs.load_sprite(name, EXT)[depth]
                px = canvas[0] * cs.CELL + (cs.CELL - sp.width) // 2
                py = canvas[1] * cs.CELL + (cs.CELL - sp.height) // 2
                out.alpha_composite(sp, (px, py))
        return out

    today = paste_fields(scarred)
    change = paste_scar_over(today, field_cells)
    return field_name, field_sp.size, today, change


HTML = """<!doctype html><meta charset="utf-8">
<style>
 body{{background:#14161a;color:#d7dae0;font:13px/1.55 system-ui,sans-serif;margin:0;padding:18px}}
 h1{{font-size:15px;margin:0 0 4px;font-weight:600}}
 p{{margin:6px 0 14px;max-width:78ch;color:#9aa1ab}}
 .row{{display:flex;gap:18px;flex-wrap:wrap}}
 .panel{{background:#0d0f12;border:1px solid #262b33;border-radius:6px;padding:10px}}
 .panel h2{{font-size:12px;margin:0 0 8px;font-weight:600;letter-spacing:.04em;text-transform:uppercase}}
 .a h2{{color:#e2a03f}} .b h2{{color:#5bc47a}}
 .panel img{{display:block;image-rendering:pixelated;border-radius:3px}}
 .cap{{margin-top:8px;font-size:12px;color:#8b929c;max-width:44ch}}
 code{{color:#c7cdd6}}
</style>
<h1>A nuclear scar on farmland, rendered offline</h1>
<p>Same blast, same seed, same real generated scar art and the real
<code>{field}</code> crop sprite out of <code>temperate.mix</code> ({fw}&times;{fh}px).
The only difference between the panels is the engine's pass order. The crop patch
deliberately straddles the blast edge, so untouched fields of the same art sit a few
cells away as a reference.</p>
<div class="row">
  <div class="panel a">
    <h2>Today &mdash; terrain, smudge, actors</h2>
    <img src="{a}">
    <div class="cap">The fields are drawn after the smudge, so they hide it. This is the
    rectangular grid of untouched green inside the disc.</div>
  </div>
  <div class="panel b">
    <h2>The change &mdash; &hellip; then smudge again, ground cover only</h2>
    <img src="{b}">
    <div class="cap">The same decal re-composited over the crop. The question is whether
    this reads as burnt ground or as crop under a dark wash &mdash; the scar art peaks at
    78&ndash;93% coverage and never reaches full opacity, by design.</div>
  </div>
</div>
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(cs.REPO, "WORKSPACE/mockups/scar-over-fields.html"))
    ap.add_argument("--radius", type=int, default=12, help="blast radius in cells; 12 matches Atomic's ScarRim")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--zoom", type=int, default=3)
    args = ap.parse_args()

    field, (fw, fh), today, change = build(args.radius, args.seed)
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as fh_out:
        fh_out.write(HTML.format(field=field, fw=fw, fh=fh,
                                 a=cs.b64(today, args.zoom), b=cs.b64(change, args.zoom)))
    print("field sprite: {} ({}x{})".format(field, fw, fh))
    print("wrote " + args.out)


if __name__ == "__main__":
    main()
