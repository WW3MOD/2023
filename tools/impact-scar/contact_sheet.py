#!/usr/bin/env python3
"""Render the impact-scar contact sheet: a self-contained HTML page showing the
generated art and, more importantly, the BEFORE/AFTER of the banding change.

This exists because the worker that produced the art is not allowed to launch
the game, so this is how the change gets reviewed. Everything here is a
faithful simulation of what the engine does, not an artistic impression:

  * cell membership uses ceil(sqrt(dx^2+dy^2)), which is exactly how
    MapGrid.CreateTilesByDistance buckets cells (MapGrid.cs:207-210), so the
    rings here are the rings the engine will draw
  * variant choice and depth-deepening reproduce SmudgeLayer.AddSmudge
    (SmudgeLayer.cs:153-190), including the detail that each SmudgeLayer keeps
    its OWN tile dictionary, so a cell can carry one smudge per layer
  * layer draw order is the world.yaml order, which is the Z order
    ("Order of the layers defines the Z sorting", SmudgeLayer.cs:30)
  * terrain underneath is the real clear1.tem/.sno/.des and w1.tem, decoded by
    the engine's own --png command

Usage:  python contact_sheet.py [--out PATH]
"""
import argparse
import base64
import io
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import racontent as rc  # noqa: E402
from PIL import Image   # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SCARS = os.path.join(REPO, "mods/ww3mod/bits/misc/scars")
TERRAIN = "C:/Users/fredr/AppData/Local/Temp/terrain"
CONTENT = os.path.expanduser("~/AppData/Roaming/OpenRA/Content/ra/v2")
CELL = 24

# Draw order == world.yaml layer order == Z order, lightest first.
LAYER_ORDER = ["Scorch", "Crater", "ScarRim", "ScarBurn", "ScarChar", "ScarCrater", "ScarCore"]

VARIANTS = {
    "ScarCore":   ["bza1", "bza2", "bza3", "bza4"],
    "ScarCrater": ["bzb1", "bzb2", "bzb3", "bzb4"],
    "ScarChar":   ["bzc1", "bzc2", "bzc3", "bzc4"],
    "ScarBurn":   ["bzd1", "bzd2", "bzd3", "bzd4"],
    "ScarRim":    ["bze1", "bze2", "bze3", "bze4"],
    # stock, only used to draw the BEFORE panels
    "Crater":  ["cr1", "cr2", "cr3", "cr4", "cr5", "cr6"],
    "Scorch":  ["sc1", "sc2", "sc3", "sc4", "sc5", "sc6"],
}

TILESETS = {
    "tem": ("temperat.mix", "temperat", "Temperate"),
    "sno": ("snow.mix", "snow", "Snow"),
    "des": ("cnc/desert.mix", "desert", "Desert"),
}

# Bands as rewire_bands.py writes them: (type, inner, outer) as fractions.
BAND_FRACS = [("ScarCore", 0.15), ("ScarCrater", 0.35), ("ScarChar", 0.58),
              ("ScarBurn", 0.80), ("ScarRim", 1.00)]


def bands_for(outer_r):
    """Mirror rewire_bands.py exactly, including the drop-empty-band rule."""
    out, next_inner = [], 0
    for name, frac in BAND_FRACS:
        o = int(round(frac * outer_r))
        if o < next_inner:
            continue
        out.append((name, next_inner, o))
        next_inner = o + 1
    return out


# ------------------------------------------------------------------ sprites
_cache = {}


def load_sprite(name, ext):
    """(name, ext) -> list of RGBA frames. New art is loose on disk, stock art
    comes out of the mix; both are ShpTD and both go through the same reader."""
    key = (name, ext)
    if key in _cache:
        return _cache[key]

    mixname, palname, _label = TILESETS[ext]
    pal = rc.read_pal(open(os.path.join(HERE, "pal", palname + ".pal"), "rb").read())

    path = os.path.join(SCARS, name + "." + ext)
    if os.path.exists(path):
        data = open(path, "rb").read()
    else:
        data = rc.MixFile(os.path.join(CONTENT, mixname)).get(name + "." + ext)
        if data is None:
            raise SystemExit("missing sprite " + name + "." + ext)

    w, h, frames = rc.read_shp(data)
    imgs = []
    for f in frames:
        im = Image.new("RGBA", (w, h))
        px = []
        for v in f:
            px.append((0, 0, 0, 0) if v == 0 else pal[v] + (255,))
        im.putdata(px)
        imgs.append(im)
    _cache[key] = imgs
    return imgs


def terrain_bg(cells_w, cells_h, ext, rng, water_from_x=None):
    """Real clear-terrain tiles, tiled at random like the engine's PickAny
    templates. `water_from_x` floods everything at or right of that cell."""
    d = {"tem": "tem", "sno": "sno", "des": "des"}[ext]
    tiles = [Image.open(os.path.join(TERRAIN, d, f)).convert("RGBA")
             for f in sorted(os.listdir(os.path.join(TERRAIN, d)))]
    water = [Image.open(os.path.join(TERRAIN, "water", f)).convert("RGBA")
             for f in sorted(os.listdir(os.path.join(TERRAIN, "water")))]

    bg = Image.new("RGBA", (cells_w * CELL, cells_h * CELL))
    for cy in range(cells_h):
        for cx in range(cells_w):
            src = water if (water_from_x is not None and cx >= water_from_x) else tiles
            bg.paste(rng.choice(src), (cx * CELL, cy * CELL))
    return bg


# ------------------------------------------------------------------ sim
class SmudgeSim:
    """One dict per layer, exactly as SmudgeLayer keeps one `tiles` per trait."""

    def __init__(self, ext, seed):
        self.ext = ext
        self.layers = {k: {} for k in VARIANTS}
        self.rng = random.Random(seed)

    def add(self, layer, cell):
        tiles = self.layers[layer]
        if cell not in tiles:
            # SmudgeLayer.cs:176 -- a NEW smudge picks a random variant.
            tiles[cell] = [self.rng.choice(VARIANTS[layer]), 0]
        else:
            # SmudgeLayer.cs:183-187 -- an EXISTING smudge deepens instead,
            # capped at the sequence length. Stock scorches are 1 frame, so
            # they never deepen at all.
            name, depth = tiles[cell]
            maxd = len(load_sprite(name, self.ext))
            if depth < maxd - 1:
                tiles[cell][1] = depth + 1

    def disc(self, layer, outer, inner=0, water_from_x=None, render_origin=(0, 0)):
        """Cells are stored blast-local; `water_from_x` is in CANVAS space, so
        it is compared against the cell's canvas column. Keeping the two in one
        space matters -- an earlier cut compared a canvas value against a
        blast-local column and drew the scar straight across open water."""
        ox, _oy = render_origin
        for dy in range(-outer, outer + 1):
            for dx in range(-outer, outer + 1):
                d2 = dx * dx + dy * dy
                # ceil(sqrt(d2)) is the engine's own bucket (MapGrid.cs:210).
                r = int(-(-(d2 ** 0.5) // 1))
                if inner <= r <= outer:
                    # Water carries no AcceptsSmudgeType, so LeaveSmudgeWarhead
                    # skips the cell (LeaveSmudgeWarhead.cs:59-61).
                    if water_from_x is not None and ox + dx >= water_from_x:
                        continue
                    self.add(layer, (dx, dy))

    def render(self, bg, origin):
        ox, oy = origin
        out = bg.copy()
        for layer in LAYER_ORDER:
            for (cx, cy), (name, depth) in self.layers[layer].items():
                sp = load_sprite(name, self.ext)[depth]
                # Sub-cell sprites (stock scorches are e.g. 20x10) centre in
                # the cell, matching SpriteFrame offset handling.
                px = (cx + ox) * CELL + (CELL - sp.width) // 2
                py = (cy + oy) * CELL + (CELL - sp.height) // 2
                out.alpha_composite(sp, (px, py))
        return out


def render_case(outer_r, ext, banded, seed, water_from_x=None, rehits=1):
    span = 2 * outer_r + 1
    pad = 1
    w = h = span + 2 * pad
    rng = random.Random(seed)
    bg = terrain_bg(w, h, ext, rng, water_from_x)
    origin = (outer_r + pad, outer_r + pad)

    sim = SmudgeSim(ext, seed)
    for _ in range(rehits):
        if banded:
            for name, inner, outer in bands_for(outer_r):
                sim.disc(name, outer, inner, water_from_x, origin)
        else:
            # What ships today: three NESTED FILLED DISCS, applied in warhead
            # order. Crater 0.35R, Scorch 0.58R, Scorch 1.0R -- the two scorch
            # warheads target the same layer, so the second one only deepens
            # what the first left, and stock scorches have one frame, so it
            # does nothing at all. Everything from 0.35R out is one flat tone.
            sim.disc("Crater", int(round(0.35 * outer_r)), 0, water_from_x, origin)
            sim.disc("Scorch", int(round(0.58 * outer_r)), 0, water_from_x, origin)
            sim.disc("Scorch", outer_r, 0, water_from_x, origin)
    return sim.render(bg, origin)


# ------------------------------------------------------------------ html
def b64(im, zoom=1):
    if zoom != 1:
        im = im.resize((im.width * zoom, im.height * zoom), Image.NEAREST)
    buf = io.BytesIO()
    im.save(buf, "PNG")
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(REPO, "WORKSPACE/mockups/impact-scarring.html"))
    args = ap.parse_args()

    P = []
    A = P.append

    A("""<!doctype html><html><head><meta charset="utf-8">
<title>WW3MOD - impact scarring contact sheet</title>
<style>
 body{background:#12140f;color:#cfcab8;font:14px/1.55 -apple-system,Segoe UI,sans-serif;
      margin:0;padding:38px 44px 90px;max-width:1500px}
 h1{font-size:26px;margin:0 0 6px;color:#f0ead6;letter-spacing:.2px}
 h2{font-size:18px;margin:52px 0 6px;color:#e8dfae;border-bottom:1px solid #33372a;padding-bottom:7px}
 h3{font-size:14px;margin:26px 0 8px;color:#b9b49c;font-weight:600}
 p{max-width:104ch;color:#a8a georgia}
 p{max-width:104ch;color:#a8a493}
 .sub{color:#7f7c6c;font-size:13px;margin:0 0 22px}
 code{background:#1e2118;padding:1px 5px;border-radius:3px;color:#cbd6a8;font-size:12.5px}
 img{image-rendering:pixelated;display:block;border:1px solid #2c3025;background:#000}
 .row{display:flex;gap:26px;flex-wrap:wrap;align-items:flex-start;margin:14px 0 4px}
 .cap{font-size:12px;color:#82806f;margin:7px 0 0}
 .cap b{color:#c9c4ae;font-weight:600}
 table{border-collapse:collapse;margin:12px 0}
 td,th{padding:5px 9px;text-align:center;font-size:12px;color:#9c9884;
       border:1px solid #2a2e23;vertical-align:top}
 th{color:#cfcab8;background:#1a1d15;font-weight:600}
 .note{background:#1a1d15;border-left:3px solid #7d8a4a;padding:12px 18px;margin:20px 0;
       border-radius:0 4px 4px 0}
 .warn{border-left-color:#a8763a}
 .k{color:#e8dfae}
</style></head><body>""")

    A("<h1>Impact scarring &mdash; concentric blast bands</h1>")
    A('<p class="sub">Generated from the built tree at <code>wt/impact-scarring</code>, '
      'base <code>main @ 30ba5681</code>. Every sprite below is decoded from the actual '
      '<code>.tem</code>/<code>.sno</code>/<code>.des</code> files that ship in this branch, '
      'over real extracted terrain tiles. Nothing here is hand-drawn or approximated.</p>')

    A("""<div class="note"><b>What to look at first.</b> The two images in the next section are
the same weapon, same terrain, same seed. The left one is what ships today. The right one is what
this branch does. The thing to judge is whether the right one reads as a <span class="k">circular
impact point</span> and the left one does not.</div>""")

    # ---------------- headline before/after
    A("<h2>1 &nbsp;Before and after</h2>")
    A("<p>Today each nuke fires three <code>LeaveSmudge</code> warheads whose <code>Size</code> is a "
      "single number, and a single number means a <b>filled disc</b>. So the three discs are nested, "
      "not adjacent, and both scorch warheads write into the <i>same</i> layer &mdash; the second one "
      "only deepens what the first left, and stock scorch art has one frame, so it does nothing at all. "
      "Everything outside the crater is one flat tone with a ragged square rim. "
      "After: five true annuli, each with its own art and its own density, sparse at the edge.</p>")

    cases = [(9, "NukeW76 / NukeRuKalibr", "9-cell scar", 4),
             (21, "NukeB83", "21-cell scar", 2),
             (34, "NukeTsarBomba", "34-cell scar", 1)]
    for r, wname, desc, zoom in cases:
        before = render_case(r, "tem", False, seed=1000 + r)
        after = render_case(r, "tem", True, seed=1000 + r)
        A("<h3>%s &mdash; %s</h3>" % (wname, desc))
        A('<div class="row">')
        A('<div><img src="%s"><p class="cap"><b>BEFORE</b> &mdash; three nested filled discs</p></div>'
          % b64(before, zoom))
        A('<div><img src="%s"><p class="cap"><b>AFTER</b> &mdash; five annuli, %s</p></div>'
          % (b64(after, zoom), " / ".join("%s %d-%d" % b for b in bands_for(r))))
        A("</div>")

    # ---------------- water
    A("<h2>2 &nbsp;Over water</h2>")
    A("<p>The open question in the brief. <b>Water already rejects smudges and needs no new code.</b> "
      "<code>LeaveSmudgeWarhead.cs:59</code> looks the cell's terrain up in "
      "<code>AcceptsSmudgeType</code> and <code>continue</code>s when the type is not listed; "
      "<code>TerrainType@Water</code> carries no <code>AcceptsSmudgeType</code> line at all "
      "(<code>tilesets/temperat.yaml:62-66</code>), so every water cell is skipped. That is true of "
      "the five new bands for the same reason it is already true of Crater and Scorch. "
      "Below, the right half of the map is water.</p>")
    A('<div class="row">')
    for r, z, lab in [(21, 2, "NukeB83"), (34, 1, "NukeTsarBomba")]:
        im = render_case(r, "tem", True, seed=77, water_from_x=r + 1 + 3)
        A('<div><img src="%s"><p class="cap"><b>%s</b> centred on the shoreline</p></div>'
          % (b64(im, z), lab))
    A("</div>")
    A("""<div class="note warn"><b>What this does not do.</b> The scar simply stops at the waterline
&mdash; there is no steam ring, no disturbed-water effect. The brief suggested the commented-out
<code>ParaBomb</code> pattern (a <code>Warhead@EffectWater</code> with
<code>ValidTargets: Water, Underwater</code>) as the way to add one. That is a real and cheap
follow-up, but it is a new visual effect rather than part of the scarring change, so it is
<b>not built here</b>. Note also that <code>Beach</code>, <code>River</code>, <code>Rock</code>,
<code>Cliffs</code> and <code>Field</code> likewise carry no <code>AcceptsSmudgeType</code>, so the
scar already stops short at a beach today &mdash; that is pre-existing, not new.</div>""")

    # ---------------- bands
    A("<h2>3 &nbsp;The five bands, frame by frame</h2>")
    A("<p>Each band is 24&times;24 with five depth frames, matching the stock craters. "
      "<code>SmudgeLayer.AddSmudge</code> deepens a cell that is hit again, so frame 0 is a first "
      "strike and frame 4 is ground that has been hit repeatedly. Coverage is applied as a "
      "<b>quantile</b> of the noise field, so the printed percentages are exact.</p>")

    for ext in ("tem", "sno", "des"):
        A("<h3>%s</h3>" % TILESETS[ext][2])
        A("<table><tr><th>band</th>" +
          "".join("<th>depth %d</th>" % d for d in range(5)) + "<th>coverage</th></tr>")
        for band, cov in (("ScarCore", "78 &rarr; 93%"), ("ScarCrater", "62 &rarr; 80%"),
                          ("ScarChar", "46 &rarr; 64%"), ("ScarBurn", "30 &rarr; 48%"),
                          ("ScarRim", "14 &rarr; 30%")):
            name = VARIANTS[band][0]
            frames = load_sprite(name, ext)
            bgt = terrain_bg(1, 1, ext, random.Random(5))
            A("<tr><td><b>%s</b><br><span style='color:#6f6d5e'>%s</span></td>" % (band, name))
            for d in range(5):
                comp = bgt.copy()
                comp.alpha_composite(frames[d])
                A('<td><img src="%s"></td>' % b64(comp, 3))
            A("<td>%s</td></tr>" % cov)
        A("</table>")

    # ---------------- repeat strikes
    A("<h2>4 &nbsp;Repeat strikes on the same ground</h2>")
    A("<p>Because the bands are annuli they never overlap themselves, so depth only advances when "
      "the <i>same ground is hit again</i>. Three strikes on one spot:</p>")
    A('<div class="row">')
    for n in (1, 2, 3):
        im = render_case(9, "tem", True, seed=31, rehits=n)
        A('<div><img src="%s"><p class="cap"><b>%d strike%s</b></p></div>'
          % (b64(im, 4), n, "" if n == 1 else "s"))
    A("</div>")

    # ---------------- tilesets
    A("<h2>5 &nbsp;The same weapon on each tileset</h2>")
    A("<p>Smudges draw through the per-tileset <code>terrain</code> palette, and the four tilesets "
      "do <b>not</b> agree: index 18 is near-black <code>(20,20,24)</code> on temperate and pale sand "
      "<code>(247,206,142)</code> on desert. Each tileset's art is therefore quantised against a ramp "
      "harvested from <i>that tileset's own</i> stock <code>cr*</code>/<code>sc*</code> art, so the "
      "new bands sit in the same colour space the existing smudges already occupy. "
      "Interior has no files of its own &mdash; it borrows temperate, exactly as craters and "
      "scorches already do.</p>")
    A('<div class="row">')
    for ext in ("tem", "sno", "des"):
        im = render_case(9, ext, True, seed=404)
        A('<div><img src="%s"><p class="cap"><b>%s</b></p></div>'
          % (b64(im, 4), TILESETS[ext][2]))
    A("</div>")
    A("""<div class="note warn"><b>Snow is deliberately pale.</b> Stock RA snow smudges are mid-grey
&mdash; 22.7% of all drawn pixels in <code>cr*.sno</code>/<code>sc*.sno</code> are index 136,
<code>(125,125,125)</code>. Harvesting the ramp from that art reproduces the choice rather than
overriding it. If a nuclear scar on snow should instead be near-black exposed earth, that is a
deliberate departure from the tileset's own palette and wants a decision, not a tweak.</div>""")

    A('<p class="sub" style="margin-top:54px">Regenerate with '
      '<code>python tools/impact-scar/gen_scars.py &amp;&amp; python tools/impact-scar/contact_sheet.py</code>.</p>')
    A("</body></html>")

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    open(args.out, "w", encoding="utf-8").write("\n".join(P))
    print("wrote " + args.out + " (" + str(os.path.getsize(args.out) // 1024) + " KB)")


if __name__ == "__main__":
    main()
