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

THE THIRD PANEL, ADDED 2026-09-19
---------------------------------
The mechanism above shipped, and left a hole its own restriction created: the
over-actors pass redraws a cell only when EVERY occupant is ground cover, so a
vehicle parked on a scarred field vetoes its own cell and that cell keeps showing
bright unburnt wheat inside a black disc. `GroundCoverOverlayUnderActors` emits
one sorted renderable for such a cell instead, at a ZOffset between the field's
-8192 and the unit's 0. The third row renders that case with a real RA vehicle
sprite over the real crop art, and prints the same wheat% metric
`tools/impact-scar/scar_density.py` measures off the autotest screenshots -- so
the offline number and the in-game number are the same number.

Usage:  python tools/impact-scar/field_overlay_preview.py [--out PATH] [--radius N]
"""
import argparse
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import contact_sheet as cs  # noqa: E402
import numpy as np  # noqa: E402
import racontent as rc  # noqa: E402
import scar_density as sd  # noqa: E402
from PIL import Image, ImageFilter  # noqa: E402

EXT = "tem"

# A real RA vehicle, decoded by the same reader as everything else. It is not the
# mod's humvee -- that art is not in the stock mixes -- but it is a genuine 24x24
# vehicle sprite at the right scale, and what the third row is evidence ABOUT is
# draw order, not which vehicle. Index 4 is RA's shadow colour.
VEHICLE_MIX = "conquer.mix"
VEHICLE_CANDIDATES = ["jeep.shp", "1tnk.shp", "apc.shp"]
VEHICLE_FACING_FRAME = 8
SHADOW_INDEX = 4

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


def pick_vehicle_sprite():
    """First stock vehicle that decodes, as an RGBA frame. Same palette as everything
    else on this tileset -- RA's tileset .pal files are the whole 256-colour game
    palette, units included, so this is the real colour and not an approximation."""
    mix = rc.MixFile(os.path.join(cs.CONTENT, VEHICLE_MIX))
    pal = rc.read_pal(open(os.path.join(HERE, "pal", "temperat.pal"), "rb").read())
    for name in VEHICLE_CANDIDATES:
        data = mix.get(name)
        if data is None:
            continue
        w, h, frames = rc.read_shp(data)
        f = frames[min(VEHICLE_FACING_FRAME, len(frames) - 1)]
        im = Image.new("RGBA", (w, h))
        px = []
        for v in f:
            if v == 0:
                px.append((0, 0, 0, 0))
            elif v == SHADOW_INDEX:
                px.append((0, 0, 0, 110))
            else:
                px.append(pal[v] + (255,))
        im.putdata(px)
        return name, im
    raise SystemExit("no stock vehicle sprite could be decoded from " + VEHICLE_MIX)


def wheat_percent(img, cx, cy, actor=None):
    """The metric scar_density.py measures off the autotest screenshots, evaluated on
    one cell of an offline render, so an offline number and an in-game number mean the
    same thing.

    The wheat mask is scar_density's, verbatim. The ACTOR mask is not, and must not be:
    scar_density identifies the vehicle by its blue hull because the witness humvee in
    `test-field-swallows-nuke` is a blue player's. The stock RA jeep drawn here is olive,
    so that test finds nothing and the jeep's own pixels are counted as unburnt ground --
    which drove the occupied cell's wheat% BELOW its neighbours' rather than up to them.
    Here the sprite's alpha is known exactly, so it is passed in."""
    box = (cx * cs.CELL, cy * cs.CELL, (cx + 1) * cs.CELL, (cy + 1) * cs.CELL)
    a = np.asarray(img.crop(box).convert("RGB")).astype(int)
    wheat, auto = sd.masks(a)
    hidden = auto if actor is None else (auto | actor[box[1]:box[3], box[0]:box[2]])
    ground = ~hidden
    n = ground.sum()
    return (100.0 * (wheat & ground).sum() / n if n else 0.0), int(n)


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

    # ---- the occupied cell -------------------------------------------------------
    # Four cells west of ground zero, so it lands in ScarCrater -- the same band as
    # witness cell 36,14 in test-field-swallows-nuke, which is what the in-game
    # measurement is taken on.
    veh_name, veh_sp = pick_vehicle_sprite()
    veh_cell = (ox - 4, oy)
    assert veh_cell in field_cells, veh_cell

    veh_px = veh_cell[0] * cs.CELL + (cs.CELL - veh_sp.width) // 2
    veh_py = veh_cell[1] * cs.CELL + (cs.CELL - veh_sp.height) // 2

    def paste_vehicle(img):
        out = img.copy()
        out.alpha_composite(veh_sp, (veh_px, veh_py))
        return out

    # Where the vehicle's own pixels are, to the pixel.
    #
    # DILATED BY 1 PX, NOT THE 3 scar_density USES, and the difference is scale rather
    # than taste. A dilation radius is only meaningful as a fraction of the cell: the
    # screenshots are captured at 48 or 60 px per cell depending on the session's display
    # scaling, so its 3 px is 5-6% of a cell edge, while a cell here is 24 px and the same
    # 3 px would be 12.5%. Applied at that strength it swallowed most of the cell's ground and
    # drove the occupied cell's wheat% to 3% against neighbours at 33% -- a number that
    # measured the structuring element rather than the scar.
    veh_mask = Image.new("L", (w * cs.CELL, h * cs.CELL), 0)
    veh_mask.paste(veh_sp.getchannel("A").point(lambda v: 255 if v > 0 else 0),
                   (veh_px, veh_py))
    veh_mask = np.asarray(veh_mask.filter(ImageFilter.MaxFilter(3))) > 0

    # SHIPPED: the over-actors pass takes every ground-cover-only cell and skips the
    # one the vehicle is standing in, because that cell is not ground cover ONLY.
    shipped = paste_vehicle(paste_scar_over(today, field_cells - {veh_cell}))

    # FIXED: the vehicle's cell gets a sorted renderable at ZOffset -7680 instead,
    # which lands after the field sprite (-8192) and before the vehicle (0). Drawing
    # it here, between the two composites, IS that sort order.
    fixed = paste_vehicle(paste_scar_over(today, field_cells))

    # THE OFFLINE NUMBER IS A PAIRED BEFORE/AFTER, NOT A COMPARISON WITH NEIGHBOURS,
    # and that limit is structural rather than fussiness. A cell here is 24 px against
    # the 60 px the autotest captures at, and the vehicle plus its shadow hides about
    # 70% of it -- so roughly 170 ground pixels survive, and WHICH 170 depends on where
    # the sprite happens to sit. Measured against unoccupied neighbours that lands
    # wherever the sprite's footprint lands: the first cut of this read 7.5% for the
    # fixed cell against 33% for neighbours and looked like a regression, when the two
    # numbers were simply not sampling the same thing.
    #
    # Both readings below are taken on the SAME cell through the SAME mask, so the only
    # thing that differs between them is the pass order. The neighbour comparison is
    # the in-game measurement's job -- scar_density.py, 3600 px a cell.
    stats = {
        "vehicle": veh_name,
        "cell": veh_cell,
        "cell_shipped": wheat_percent(shipped, *veh_cell, actor=veh_mask),
        "cell_fixed": wheat_percent(fixed, *veh_cell, actor=veh_mask),
        "band_typical": wheat_percent(change, veh_cell[0], veh_cell[1]),
    }

    # CROP LAST. The stats above are taken from the FULL canvases: a cropped image
    # indexed with full-canvas cell coordinates reads off the end of it, and PIL
    # obligingly returns black rather than raising -- which showed up as every cell
    # measuring 0.0% wheat over a full 576 px of "ground".
    def crop_around(img, half=2):
        x0 = (veh_cell[0] - half) * cs.CELL
        y0 = (veh_cell[1] - half) * cs.CELL
        return img.crop((x0, y0, x0 + (2 * half + 1) * cs.CELL, y0 + (2 * half + 1) * cs.CELL))

    return (field_name, field_sp.size, today, change,
            crop_around(shipped), crop_around(fixed), stats)


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

<h1 style="margin-top:26px">A vehicle parked on a scarred field cell</h1>
<p>The hole the restriction above leaves. A cell qualifies for that second pass only when
<i>every</i> occupant is ground cover, so one vehicle vetoes its own cell and the crop
sprite goes on hiding the decal underneath it. 5&times;5 cells of the same blast, centred
on a <code>{veh}</code> standing in the <b>ScarCrater</b> band &mdash; the band witness cell
36,14 sits in. <code>wheat%</code> is
<code>tools/impact-scar/scar_density.py</code>'s metric, evaluated here on the offline
render: bright wheat pixels over ground pixels, with the vehicle excluded.</p>
<div class="row">
  <div class="panel a">
    <h2>Shipped &mdash; the vehicle's cell is skipped</h2>
    <img src="{c}">
    <div class="cap">wheat <b>{c_pct:.1f}%</b> on the visible ground of the occupied
    cell. The crop sprite is fully opaque, so it hides the terrain-pass decal outright
    and the cell reads as untouched farmland inside a black disc.</div>
  </div>
  <div class="panel b">
    <h2>GroundCoverOverlayUnderActors &mdash; sorted renderable at ZOffset -7680</h2>
    <img src="{d}">
    <div class="cap">wheat <b>{d_pct:.1f}%</b> on the same pixels through the same mask.
    The scar is above the crop and below the vehicle; no scar pixel is on the hull. An
    unoccupied cell of this band reads {t_pct:.1f}% &mdash; not directly comparable,
    since the vehicle hides about 70% of a 24&nbsp;px cell here.</div>
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

    field, (fw, fh), today, change, shipped, fixed, stats = build(args.radius, args.seed)
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as fh_out:
        fh_out.write(HTML.format(
            field=field, fw=fw, fh=fh, veh=stats["vehicle"],
            a=cs.b64(today, args.zoom), b=cs.b64(change, args.zoom),
            c=cs.b64(shipped, args.zoom + 3), d=cs.b64(fixed, args.zoom + 3),
            c_pct=stats["cell_shipped"][0], d_pct=stats["cell_fixed"][0],
            t_pct=stats["band_typical"][0]))
    print("field sprite: {} ({}x{})".format(field, fw, fh))
    print("vehicle sprite: {}".format(stats["vehicle"]))
    print("occupied cell %s wheat%%: shipped %.1f -> fixed %.1f on the same %d ground "
          "px; an unoccupied cell of this band reads %.1f"
          % (stats["cell"], stats["cell_shipped"][0], stats["cell_fixed"][0],
             stats["cell_fixed"][1], stats["band_typical"][0]))
    print("wrote " + args.out)

    # PNGs too, so the two states can be looked at without a browser.
    d = os.path.join(cs.REPO, "WORKSPACE/mockups")
    os.makedirs(d, exist_ok=True)
    for name, im in (("scar-field-vehicle-shipped", shipped),
                     ("scar-field-vehicle-fixed", fixed)):
        path = os.path.join(d, name + ".png")
        z = args.zoom + 3
        im.resize((im.width * z, im.height * z), Image.NEAREST).save(path)
        print("wrote " + path)


if __name__ == "__main__":
    main()
