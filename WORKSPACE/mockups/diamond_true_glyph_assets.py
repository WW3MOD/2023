#!/usr/bin/env python3
"""Build the asset set for WORKSPACE/mockups/diamond-true-glyph.html.

Run from the repo root:  python WORKSPACE/mockups/diamond_true_glyph_assets.py

WHAT IS DIFFERENT ABOUT THIS PASS. Every mark here is the SHIPPED FONT GLYPH,
rasterised by FreeType out of the shipped FreeSansBold.ttf at the shipped 10 px,
then masked and tinted. There is not one hand-drawn polygon in the output. The
previous pass (diamond_variants_assets.py) drew an L1 ball; this one loads
U+2666 and U+25CA and fills those.

THE ENGINE PATH, reproduced rather than approximated:

  mod.yaml:315-318          TinyBold = common|FreeSansBold.ttf, Size 10
  WithTextDecoration.cs:27  Font defaults to "TinyBold"; defaults.yaml:928-964
                            does not override it.
  DecorationRowGeometry.cs:46  GlyphFontSize = 10, asserting the same size.
  FreeTypeFont.cs:81-86     FT_Set_Pixel_Sizes(10,10); FT_Load_Char(FT_LOAD_RENDER)
                            -> FT_RENDER_MODE_NORMAL, 8-bit coverage, hinting ON.
  SpriteFont.cs:285-293     that coverage byte is copied into R, G, B AND A.
  UITextRenderable.cs:57    Render() calls DrawTextWithContrast(..., offset 1).
  SpriteFont.cs:181-187     -> DrawTextContrast then DrawText.
  SpriteFont.cs:360-417     the contrast sprite is a GREYSCALE DILATION of the
                            glyph's alpha by CreateCircularWeightMap(1), i.e. a
                            SOFT HALO whose corner weight is 0.60, drawn one
                            pixel up-left of the glyph and one pixel larger on
                            every side. IT IS NOT A HARD KEYLINE.
  metrics.yaml:34-35        TextContrastColorDark 000000 / Light 7F7F7F.
  SpriteFont.cs:419-422     GetContrastColor picks Dark when the foreground's
                            brightness > 0.33. DERIVED: all five shipped
                            detectability colours have HSL lightness 0.52-0.62,
                            so every grade of this mark takes 000000 today.
  Sdl2GraphicsContext.cs:215  glBlendFunc(GL_ONE, GL_ONE_MINUS_SRC_ALPHA), and
                            combined.frag does `c *= vTint`. So rgb = tint*cov
                            and a = cov: premultiplied source, which composites
                            to EXACTLY straight-alpha `tint` at alpha `cov`.
                            That is what a browser does with an RGBA PNG, so the
                            PNGs below are colour=tint, alpha=coverage and the
                            page composites them the way the game does.

FIDELITY LIMIT, stated not glossed: PIL ships its own FreeType, so a hinted stem
could in principle land a pixel differently from the game's freetype6. The
evidence that it does not, for these two glyphs, is that the ink box comes out
6x9 -- the size the tree reports independently (diamond-variants.md, and the
6x9 the whole previous pass was built around). glyph_probe.py prints the full
coverage grid if you want to re-check it.

INHERITED TRAPS from indicator_layout_assets.py, restated so they stay fixed:
  1. The Abrams image is `abrams-correction.shp`, NOT `abrams.shp`.
  2. The turret is a SEPARATE frame ring; hull N is drawn under turret 32+N.
  3. Frame 0 is NORTH, index advances COUNTER-CLOCKWISE: SE is hull 19 / turret
     51, and infantry `stand` SE is frame 5.
  4. `bradley` has no drawable sprite in this repo.

PALETTE PROXY, restated: temperat.pal is inside a Blowfish-encrypted local.mix,
so ground renders through plains.pal. Decoration hues are authored RGB and exact;
the GROUND under them is a proxy and every contrast verdict is provisional.
"""

import base64
import io
import json
import os
import re
import sys

from PIL import Image, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from buymenu_shp_dump import PAL, load_palette, read_shp  # noqa: E402
from indicator_layout_assets import (  # noqa: E402
    CELL, SE_CLASSIC, SE_INF8, TEAMS, TERRAIN_PAL_ALT, paint, terrain, team_ramp,
    unit_sprite, uri,
)

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
HTML = os.path.join(HERE, "diamond-true-glyph.html")

TTF = os.path.join(REPO, "engine", "mods", "common", "FreeSansBold.ttf")
FONT_SIZE = 10                       # TinyBold; DecorationRowGeometry.cs:46
CONTRAST_R = 1                       # UITextRenderable.cs:57 passes offset 1
CONTRAST_DARK = (0x00, 0x00, 0x00)   # metrics.yaml:34

BITS = os.path.join(REPO, "mods", "ww3mod", "bits", "units")
VEH, INF, PIPS, CLS = (os.path.join(BITS, d) for d in
                       ("vehicle", "infantry", "pips", "classes"))

SOLID_CH = "♦"      # WithSpottedDecoration.cs:96  SolidText
HOLLOW_CH = "◊"     # WithSpottedDecoration.cs:92  HollowText

# defaults.yaml:959-964, and WithSpottedDecorationInfo:99-112.
DET = ["#6E9E76", "#ECC73C", "#F0B232", "#F09425", "#FF4A3C"]
DET_NAMES = ["Concealed", "Low", "Moderate", "High", "Spotted"]
# defaults.yaml:958  SolidFromGrade: Moderate -> grades 3,4,5 draw the SOLID glyph.
SOLID_FROM_GRADE = 3

# Impediment ramp. Grey is the ZERO state (nothing wrong with the unit), then the
# mod's own damage family (infantry.yaml:713-717) because the permanent half of
# impediment is derived from HP.
GREY = "#9AA4B0"
IMP4 = [GREY, "#E8D24A", "#E8892B", "#D6392B"]
IMP5 = [GREY, "#E8D24A", "#E8A32B", "#E8892B", "#D6392B"]

# WithStanceDecorationInfo:44-64 -- glyph and colour, same font and size.
STANCE = [("X", (235, 235, 235), "HoldFire"), ("A", (255, 210, 70), "Ambush"),
          ("H", (105, 205, 255), "HoldPosition"), (">", (255, 145, 45), "Hunt")]


# ---------------------------------------------------------------- the real glyph
def glyph_coverage(ch, size=FONT_SIZE):
    """The engine's own bitmap for `ch`: rows of 8-bit coverage, cropped to ink."""
    f = ImageFont.truetype(TTF, size)
    m = f.getmask(ch, mode="L")
    im = Image.frombytes("L", m.size, bytes(m))
    bb = im.getbbox()
    if bb is None:
        raise SystemExit(f"glyph U+{ord(ch):04X} has no ink in {TTF}")
    im = im.crop(bb)
    px = im.load()
    return [[px[x, y] for x in range(im.width)] for y in range(im.height)]


SOLID = glyph_coverage(SOLID_CH)
HOLLOW = glyph_coverage(HOLLOW_CH)
GW, GH = len(SOLID[0]), len(SOLID)


def profile(cov):
    """Ink width per row. This is the silhouette, on the record."""
    return [sum(1 for v in row if v > 0) for row in cov]


def ink(cov):
    return sum(profile(cov))


# ---------------------------------------------------------------- engine contrast
def circular_weight_map(r):
    """Verbatim port of SpriteFont.CreateCircularWeightMap (:302-358).

    Its own doc comment says r=1 must come out as
        0.60 1.00 0.60
        1.00 1.00 1.00
        0.60 1.00 0.60
    which main() asserts below, so a mistranscription cannot pass silently.
    """
    stride = 2 * r + 1
    elem = [0.0] * (stride * stride)
    for j in range(2 * r + 1):
        for i in range(2 * r + 1):
            di, dj = i - r, j - r
            if di * di + dj * dj > (r + 1) * (r + 1):
                continue
            if di * di + dj * dj < (r - 1) * (r - 1):
                elem[j * stride + i] = 1.0
                continue
            acc = 0.0
            for jj in range(5):
                for ii in range(5):
                    si = di - (1 if di > 0 else -1 if di < 0 else 0) * ii / 5
                    sj = dj - (1 if dj > 0 else -1 if dj < 0 else 0) * jj / 5
                    if si * si + sj * sj <= r * r:
                        acc += 0.04
            elem[j * stride + i] = acc
    return elem


def dilate(cov, r=CONTRAST_R):
    """Verbatim port of SpriteFont.CreateContrastGlyph (:360-417).

    Output is (w+2r) x (h+2r). Note the engine reads the source at
    `i + wi - 2*r` and the caller then draws the result at -r, so the sample
    window is centred; both quirks are preserved rather than tidied.
    """
    h, w = len(cov), len(cov[0])
    elem = circular_weight_map(r)
    stride = 2 * r + 1
    out = [[0] * (w + 2 * r) for _ in range(h + 2 * r)]
    for j in range(h + 2 * r):
        for i in range(w + 2 * r):
            first, alpha = True, 0
            for wj in range(2 * r + 1):
                for wi in range(2 * r + 1):
                    ii, jj = i + wi - 2 * r, j + wj - 2 * r
                    if ii < 0 or ii >= w or jj < 0 or jj >= h:
                        continue
                    weighted = int(elem[wj * stride + wi] * cov[jj][ii])
                    if first or weighted > alpha:
                        alpha, first = weighted, False
            out[j][i] = alpha
    return out


# ---------------------------------------------------------------- colour helpers
def hexrgb(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


def dim(rgb, f):
    return tuple(max(0, min(255, int(c * f))) for c in rgb)


def band(value, n):
    """0..100 -> 0..n-1, with 0 reserved for exactly zero."""
    if value <= 0:
        return 0
    return min(n - 1, 1 + int((value - 1) * (n - 1) / 100))


# ---------------------------------------------------------------- the fill ladder
def fill_row_count(cov, step, steps):
    """How many rows from the BOTTOM are inked at `step` of a `steps`-step ladder.

    Chosen by INK FRACTION, not by row fraction. On this silhouette the rows are
    2,2,4,6,6,6,4,2,2 px wide, so equal row counts are wildly unequal areas and a
    row-fraction ladder wastes two of its steps on the 2px tips.
    """
    if steps <= 1:
        return len(cov) if step else 0
    if step <= 0:
        return 0
    rows = profile(cov)
    total = sum(rows)
    target = total * step / (steps - 1)
    acc = 0
    for k in range(1, len(rows) + 1):
        acc += rows[len(rows) - k]
        if acc >= target - 1e-9:
            return k
    return len(rows)


def ladder(cov, steps):
    """The row counts a ladder actually uses -- printed so the steps are on record."""
    return [fill_row_count(cov, s, steps) for s in range(steps)]


# ---------------------------------------------------------------- the renderer
def render(cov, tint, filled_rows, halo, unfilled_scale, full_cov=None):
    """One mark: the true glyph, masked by a fill, tinted, over the engine's halo.

    cov            coverage grid actually drawn (the glyph)
    tint           rgb of the inked part
    filled_rows    rows from the bottom that carry the full tint; the rest are
                   drawn at `unfilled_scale` of their coverage
    halo           None, or (rgb, dx, dy) -- the dilated contrast sprite and where
                   to put it relative to the glyph. The engine's own values are
                   (000000, -1, -1) (SpriteFont.cs:87, contrastVector).
    full_cov       silhouette the halo is dilated from. Defaults to `cov`. Passing
                   the FULL footprint keeps the outline legible at low fill, which
                   is a deliberate choice and is called out on the page.
    """
    h, w = len(cov), len(cov[0])
    src = full_cov if full_cov is not None else cov
    pad = CONTRAST_R
    cw, ch = w + 2 * pad, h + 2 * pad
    out = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))

    if halo is not None:
        hrgb, dx, dy = halo
        d = dilate(src, CONTRAST_R)
        layer = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
        lp = layer.load()
        for j in range(len(d)):
            for i in range(len(d[0])):
                a = d[j][i]
                if a <= 0:
                    continue
                x, y = i + pad + dx, j + pad + dy
                if 0 <= x < cw and 0 <= y < ch:
                    lp[x, y] = hrgb + (a,)
        out = Image.alpha_composite(out, layer)

    layer = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    lp = layer.load()
    cut = h - filled_rows
    for y in range(h):
        for x in range(w):
            c = cov[y][x]
            if c <= 0:
                continue
            if y >= cut:
                lp[x + pad, y + pad] = tint + (c,)
            elif unfilled_scale > 0:
                lp[x + pad, y + pad] = dim(tint, 0.42) + (int(c * unfilled_scale),)
    return Image.alpha_composite(out, layer)


ENGINE_HALO = (CONTRAST_DARK, -1, -1)


def shipped(grade):
    """The mark EXACTLY as it ships today, for the proof strip. grade is 1..5."""
    cov = SOLID if grade >= SOLID_FROM_GRADE else HOLLOW
    return render(cov, hexrgb(DET[grade - 1]), len(cov), ENGINE_HALO, 0.0)


def stance_glyph(ch, rgb):
    cov = glyph_coverage(ch)
    return render(cov, rgb, len(cov), ENGINE_HALO, 0.0)


# ---------------------------------------------------------------- the variants
# Every variant below draws the SHIPPED SILHOUETTE and nothing else. What varies
# is (a) which channel drives fill and which drives colour, and (b) the contrast
# treatment -- which is the variable the previous pass got wrong and which the
# brief asks to be solved rather than inherited.
#
# MERGE POLICY, stated because the last round found it undecided. Five detection
# grades do not fit four fill steps. Where a 4-step fill carries detection, this
# file merges LOW into MODERATE -- two adjacent posture-only grades. It does NOT
# merge High with Spotted: Spotted is the only enemy-derived grade on the scale
# and is the one state players already recognise, so it keeps its own step.
DET_MERGE_4 = {1: 0, 2: 1, 3: 1, 4: 2, 5: 3}      # grade -> step of 4
DET_MERGE_NOTE = "Low+Moderate share a step; Spotted keeps its own"

VARIANTS = [
    dict(id="T1", title="Engine-true baseline",
         fill="det", fill_steps=4, colour="imped", ramp=IMP4,
         halo=ENGINE_HALO, unfilled=0.34,
         blurb="The faithful reference: true coverage, the engine's own radius-1 "
               "dilated halo in 000000, fill on detection, colour on impediment."),

    dict(id="T2", title="Inverted — colour is the channel that exists",
         fill="imped", fill_steps=4, colour="det", ramp=DET,
         halo=ENGINE_HALO, unfilled=0.34,
         blurb="Detection drives COLOUR, so all five shipped grades and their five "
               "shipped colours survive; impediment takes the 4-step fill. The only "
               "variant here that is fully drawable today."),

    dict(id="T3", title="Both shipped glyphs — hollow at the bottom of the ladder",
         fill="det", fill_steps=4, colour="imped", ramp=IMP4,
         halo=ENGINE_HALO, unfilled=0.34, hollow_glyph=True,
         blurb="Step 0 is the real U+25CA lozenge, exactly as it ships; every step "
               "above it is U+2666 partially filled. The most literal reading of "
               "'the same shape as the current one'."),

    dict(id="T4", title="No halo at all",
         fill="det", fill_steps=4, colour="imped", ramp=IMP4,
         halo=None, unfilled=0.34,
         blurb="Tests whether the contrast sprite is load-bearing. If the glyph's "
               "own antialiased edge is enough, the halo is 34px of darkening the "
               "mark does not need."),

    dict(id="T5", title="Halo as a drop shadow, down-right only",
         fill="det", fill_steps=4, colour="imped", ramp=IMP4,
         halo=(CONTRAST_DARK, 1, 1), unfilled=0.34,
         blurb="The same dilation, displaced down-right instead of up-left, so it "
               "reads as a shadow under two edges rather than a ring around four. "
               "Half the darkening, and it leaves the top-left edge clean."),

    dict(id="T6", title="Halo in the mark's own hue",
         fill="det", fill_steps=4, colour="imped", ramp=IMP4,
         halo="ownhue", unfilled=0.34,
         blurb="The dilation tinted to a darkened version of the mark's own colour "
               "instead of black. Keeps the hue reading at the silhouette edge, "
               "where a black ring throws it away."),

    dict(id="T7", title="Binary fill — the only encoding honest on a vehicle",
         fill="det", fill_steps=2, colour="imped", ramp=IMP4,
         halo=ENGINE_HALO, unfilled=0.34,
         blurb="Two fill states, matching the shipped hollow/solid split at "
               "SolidFromGrade. On a vehicle concealment spans 3 of 9 levels and is "
               "permanently solid, so four of five grades never fire and every finer "
               "ladder is drawing a quantity that does not vary."),

    dict(id="T8", title="Five fill steps, no merge",
         fill="det", fill_steps=5, colour="imped", ramp=IMP5,
         halo=ENGINE_HALO, unfilled=0.34,
         blurb="One fill step per detection grade, so nothing is merged. The "
               "question the contact sheet has to answer is whether the fifth step "
               "is visible or whether two of the five collapse anyway."),
]

IMPEDS = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100]
DETS = [1, 2, 3, 4, 5]


def draw(spec, imped, det):
    """One mark for one (impediment, detection) pair."""
    steps = spec["fill_steps"]

    if spec["fill"] == "det":
        if steps == 4:
            step = DET_MERGE_4[det]
        elif steps == 2:
            step = 1 if det >= SOLID_FROM_GRADE else 0
        else:
            step = min(steps - 1, det - 1)
        cval = imped
    else:
        step = band(imped, steps)
        cval = det

    ramp = spec["ramp"]
    if spec["colour"] == "det":
        tint = hexrgb(ramp[min(len(ramp) - 1, cval - 1)])
    else:
        tint = hexrgb(ramp[band(cval, len(ramp))])

    use_hollow = spec.get("hollow_glyph") and step == 0
    cov = HOLLOW if use_hollow else SOLID
    rows = len(cov) if use_hollow else fill_row_count(SOLID, step, steps)

    halo = spec["halo"]
    if halo == "ownhue":
        halo = (dim(tint, 0.26), -1, -1)

    return render(cov, tint, rows, halo, spec["unfilled"], full_cov=cov)


# ---------------------------------------------------------------- build
def main():
    elem = circular_weight_map(1)
    want = [0.60, 1.00, 0.60, 1.00, 1.00, 1.00, 0.60, 1.00, 0.60]
    if max(abs(a - b) for a, b in zip(elem, want)) > 0.005:
        sys.exit(f"circular_weight_map(1) mistranscribed: {elem}\n"
                 f"SpriteFont.cs:308-312 documents {want}")
    print("circular_weight_map(1) matches SpriteFont.cs:308-312 -> "
          "the halo is a SOFT dilation, corner weight 0.60")

    print(f"\nfont : {TTF}\nsize : {FONT_SIZE} px (TinyBold)")
    print(f"  U+2666 SolidText  {GW}x{GH}  profile={profile(SOLID)}  ink={ink(SOLID)}px")
    print(f"  U+25CA HollowText {len(HOLLOW[0])}x{len(HOLLOW)}  "
          f"profile={profile(HOLLOW)}  ink={ink(HOLLOW)}px")
    print(f"  same outline? only-hollow px outside solid = "
          f"{sum(1 for y in range(GH) for x in range(GW) if HOLLOW[y][x] > 0 and SOLID[y][x] == 0)}")

    pal = load_palette(PAL)
    pal_t = load_palette(TERRAIN_PAL_ALT) if os.path.isfile(TERRAIN_PAL_ALT) else pal
    ramp = {k: team_ramp(pal, v) for k, v in TEAMS.items()}
    out = {}

    units = [
        ("abrams", os.path.join(VEH, "abrams-correction.shp"), SE_CLASSIC,
         SE_CLASSIC + 32, "us", 1.25),
        ("e3us", os.path.join(INF, "e3.shp"), SE_INF8, None, "us", 0.65),
    ]
    for key, path, fr, tur, team, sc in units:
        if not os.path.isfile(path):
            print("MISSING", path)
            continue
        im, bb = unit_sprite(pal, path, fr, ramp[team], tur, sc)
        out[key] = {"w": im.width, "h": im.height, "src": uri(im),
                    "file": os.path.basename(path), "frame": fr, "scale": sc}
        print(f"  {key:7s} {os.path.basename(path):24s} f{fr} -> {im.width}x{im.height} @{sc}")

    # Marks for the Shift-held column. indicator-audit.md §1 rows 5-10 and 16.
    for key, fn, n, shadow in (("dmg_inf", "pip-damage-infantry.shp", 5, 3),
                               ("dmg_veh", "pip-damage-vehicle.shp", 5, 3),
                               ("supp", "pip-suppression.shp", 10, 3),
                               ("rank", "rank.shp", 4, 4),
                               ("pips2", "pips2.shp", 8, 3)):
        p = os.path.join(PIPS, fn)
        if not os.path.isfile(p):
            print("MISSING", p)
            continue
        w, h, fr = read_shp(p)
        for i in range(min(n, len(fr))):
            out[f"{key}{i}"] = {"w": w, "h": h, "file": f"{fn} f{i}",
                                "src": uri(paint(pal, w, h, fr[i], shadow))}

    cp = os.path.join(CLS, "e3_class.shp")
    if os.path.isfile(cp):
        w, h, fr = read_shp(cp)
        out["class0"] = {"w": w, "h": h, "src": uri(paint(pal, w, h, fr[0], 4)),
                         "file": "e3_class.shp"}

    tile, prov = terrain(pal_t)
    if tile:
        out["_terrain"] = {"src": uri(tile), "w": tile.width, "h": tile.height,
                           "cell": CELL, "provenance": prov}
    print(f"terrain: {prov}")

    # The shipped mark, for the side-by-side proof strip.
    ship = {}
    for g in DETS:
        im = shipped(g)
        ship[str(g)] = {"w": im.width, "h": im.height, "src": uri(im),
                        "name": DET_NAMES[g - 1], "colour": DET[g - 1],
                        "glyph": "U+2666" if g >= SOLID_FROM_GRADE else "U+25CA"}
    print("\nshipped reference marks: " +
          ", ".join(f"{DET_NAMES[g-1]}={ship[str(g)]['glyph']}" for g in DETS))

    st = {}
    for ch, rgb, name in STANCE:
        im = stance_glyph(ch, rgb)
        st[name] = {"w": im.width, "h": im.height, "src": uri(im), "glyph": ch}

    print("\nfill ladders, in rows-from-the-bottom (ink-fraction chosen):")
    for steps in (2, 4, 5):
        rows = ladder(SOLID, steps)
        inks = []
        p = profile(SOLID)
        for k in rows:
            inks.append(sum(p[len(p) - k:]) if k else 0)
        print(f"  {steps}-step: rows={rows}  ink={inks} of {ink(SOLID)}px")

    dia, meta = {}, {}
    for spec in VARIANTS:
        d = {}
        for i in IMPEDS:
            for t in DETS:
                im = draw(spec, i, t)
                d[f"{i}_{t}"] = {"w": im.width, "h": im.height, "src": uri(im)}
        dia[spec["id"]] = d
        meta[spec["id"]] = {
            "title": spec["title"], "blurb": spec["blurb"],
            "fill": spec["fill"], "colour": spec["colour"],
            "fill_steps": spec["fill_steps"], "bands": len(spec["ramp"]),
            "claimed": spec["fill_steps"] * len(spec["ramp"]),
            "halo": ("none" if spec["halo"] is None else
                     "ownhue" if spec["halo"] == "ownhue" else
                     f"{spec['halo'][1]:+d},{spec['halo'][2]:+d}"),
            "rows": ladder(SOLID, spec["fill_steps"]),
            "merge": DET_MERGE_NOTE if (spec["fill"] == "det"
                                        and spec["fill_steps"] == 4) else "",
        }
        print(f"  {spec['id']}: fill={spec['fill']}({spec['fill_steps']}) "
              f"colour={spec['colour']}({len(spec['ramp'])}) "
              f"halo={meta[spec['id']]['halo']}")

    if not os.path.isfile(HTML):
        print(f"\n(HTML not present yet: {HTML})")
        print(f"built {len(out)} assets + {len(dia)} variants "
              f"({sum(len(v) for v in dia.values())} marks)")
        return 0

    src = open(HTML, encoding="utf-8").read()
    for name, payload in (("ASSETS", out), ("MARKS", dia), ("META", meta),
                          ("SHIPPED", ship), ("STANCE", st)):
        new, n = re.subn(rf"(?m)^const {name} = .*$",
                         lambda _: f"const {name} = " + json.dumps(payload) + ";",
                         src, count=1)
        if n != 1:
            sys.exit(f"no 'const {name} = ...' line in {HTML}")
        src = new
    open(HTML, "w", encoding="utf-8", newline="\n").write(src)
    print(f"\ninjected {len(out)} assets + {len(dia)} variants "
          f"({sum(len(v) for v in dia.values())} marks) into {os.path.basename(HTML)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
