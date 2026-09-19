#!/usr/bin/env python3
"""Generate WW3Caption.ttf -- the 1-bit pixel font the sidebar draws cameo captions with.

    python tools/cameo/pixelfont.py --build     # writes mods/ww3mod/WW3Caption.ttf
    python tools/cameo/pixelfont.py --verify    # rasterises it and checks every lit pixel is 255

WHY A GENERATED FONT AND NOT A DOWNLOADED ONE
---------------------------------------------
The caption font shipped until now was `Caption` = common|FreeSansBold.ttf at 7px. A vector
typeface at 7px has NO fully opaque pixel: FreeType renders FT_RENDER_MODE_NORMAL (8-bit
coverage), SpriteFont.cs:285-293 copies that coverage byte into all four channels, and the
result is a grey, partly transparent stipple. Over the solid black caption band you cannot see
it; with the band off you can, immediately. --verify prints the measured figure.

A pixel font fixes it WITHOUT touching the engine, and that is the load-bearing part of this
design. FreeType antialiases EDGES -- a pixel that a contour covers completely still comes back
255. So if every contour edge lands exactly on a device pixel boundary at the shipped size,
there are no partial pixels and the output is 1-bit for free. No FT_RENDER_MODE_MONO, no
FT_LOAD_TARGET_MONO, no per-font flag in the Fonts block, no engine change at all.

THE ARITHMETIC THAT MAKES THE EDGES LAND ON THE GRID
-----------------------------------------------------
FreeTypeFont.cs:81 calls FT_Set_Pixel_Sizes(face, size*deviceScale, size*deviceScale), so
ppem == the mod.yaml Size (times the UI scale). FreeType then scales font units to pixels by
FT_DivFix(ppem * 64, unitsPerEm), a 16.16 fixed-point divide. That divide is EXACT -- and only
then is the grid alignment exact -- when unitsPerEm divides ppem * 2^22.

    ppem 7, unitsPerEm 896 = 7 * 2^7   ->  scale = 0.5 exactly, 1 device pixel = 128 font units.

So every glyph here is drawn on a 128-unit lattice and lands on whole pixels at 7px. It stays
exact at any INTEGER UI scale (14px, 21px, ...); at a FRACTIONAL UI scale it does not, and the
antialiasing comes back. That is a real limit, not a hypothetical -- see --verify, which
measures at several scales rather than assuming.

METRICS, AND WHY 3x5 RATHER THAN THE 4x5 IN convert.py
-------------------------------------------------------
The baked lettering on the shipped cameos is 5 ink rows ending on sprite row 46 = SLOT row 45
(measured here on e1americaicon and t90icon), and tools/cameo/README.md records
"PRECISION STR." -- 14 characters -- measuring 56px, i.e. 4.0px per character.

convert.py's baked-caption font is 4px of ink on a 5px advance. At that pitch "PRECISION STR."
is 69px against a 60px slot budget, so the widget would shorten the mod's own longest shipped
caption to "PRECISION". 3px of ink on a 4px advance gives exactly the 56px the baked lettering
measures, and 15 characters fit the un-badged budget.

Cap height is 5px = 640 units, sitting on the baseline. CameoCaptionCache puts the line box at
slotHeight - bottomMargin - lineHeight and SpriteFont.DrawText adds `size` to reach the
baseline, so with CaptionBottomMargin: 0 the baseline is slot row 46 and a 5-row cap occupies
rows 41..45 -- which is where the baked lettering already is. The font SIZE cancels out of that
derivation entirely; what has to be 5 is the CAP HEIGHT, not the size.

THE AUTOHINTER IS ON, AND IT MOVES LOWERCASE i AND j UP BY ONE PIXEL
---------------------------------------------------------------------
FT_LOAD_RENDER is FT_LOAD_DEFAULT plus a render, so HINTING IS ENABLED. A TTF with no bytecode
-- which is everything fontTools writes -- takes FreeType's `maxSizeOfInstructions == 0` branch
and gets the AUTOHINTER, whose latin module derives blue zones by sampling specific CHARACTERS
through the cmap and snapping glyph edges to them.

Measured here, not assumed: with no a-z in the cmap every glyph rasterises with its ink box at
exactly (0, 0, 4, 5). Add a-z -> A-Z and uppercase I and J move to (0, -1, 4, 5) -- one pixel
high, out of line with the other 24 letters. The small-letter blue zones the autofitter builds
from `i`/`j` land at the same height as the capital ones (because the lowercase entries point at
5px capitals), and I/J then snap against the wrong zone.

Giving a-z their OWN glyph ids with the same outlines confines the damage: A-Z and 0-9 are all
exactly (0, 0, 4, 5), and only lowercase `i` and `j` are still a pixel high. That is what is
built below, because dropping lowercase entirely would make the roster's own
`CameoCaption: 6x750 KT` draw a .notdef block for its x. --verify asserts the uppercase set and
reports the i/j deviation; check_captions.py rejects a caption containing either.

Two things NOT to reach for here. Adding an embedded bitmap strike (EBDT/EBLC) looks like the
obvious answer for a pixel font and would BREAK the engine: FT_LOAD_RENDER returns an embedded
bitmap verbatim in FT_PIXEL_MODE_MONO, and FreeTypeFont.cs:117-124 reads the buffer one BYTE per
pixel. And FT_LOAD_NO_HINTING in the engine would fix this without a per-font opt-in, but it is
engine-wide and would change how every other font in the mod renders.
"""

import argparse
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# --- the numbers everything else is derived from ---------------------------------------------
PPEM = 7            # mods/ww3mod/mod.yaml -> Fonts: CameoCaption: Size
UPEM = 896          # 7 * 2^7. See the header: this is what makes FT_DivFix exact at ppem 7.
PX = UPEM // PPEM   # 128 font units per device pixel
CELL_W, CELL_H = 3, 5   # ink box
ADVANCE = 4             # ink box + 1px letterspacing; matches the baked lettering's 4.0px pitch

TTF = os.path.join(ROOT, "mods", "ww3mod", "WW3Caption.ttf")

# Slot geometry, from mods/ww3mod/chrome/ingame-player.yaml (both palettes agree) and
# CameoCaptionCache.Build, which reserves the badge out of the width BEFORE fitting the text.
SLOT_W, SIDE_MARGIN, BADGE_W, BADGE_GAP = 62, 1, 13, 1
BUDGET_PLAIN = SLOT_W - 2 * SIDE_MARGIN                        # 60
BUDGET_BADGED = BUDGET_PLAIN - (BADGE_W + BADGE_GAP)           # 46

# --- glyphs -----------------------------------------------------------------------------------
# 3 wide, 5 tall, top row first. '#' is ink. Every glyph sits ON the baseline (no descenders), so
# the last ink row of a line is the same row for every character -- which is what keeps a runtime
# caption on the same row as the baked lettering it replaces.
#
# M/N/W/V/U are the five a 3px box makes ambiguous, so they are drawn as a deliberate set:
# M fills rows 1-2, W fills rows 2-3, N is the diagonal, U closes at the bottom and V points.
# 0 is the full box and O is the round one, for the same reason.
GLYPHS = {
    " ": "...|...|...|...|...",
    "A": ".#.|#.#|###|#.#|#.#",
    "B": "##.|#.#|##.|#.#|##.",
    "C": ".##|#..|#..|#..|.##",
    "D": "##.|#.#|#.#|#.#|##.",
    "E": "###|#..|##.|#..|###",
    "F": "###|#..|##.|#..|#..",
    "G": ".##|#..|#.#|#.#|.##",
    "H": "#.#|#.#|###|#.#|#.#",
    "I": "###|.#.|.#.|.#.|###",
    "J": "..#|..#|..#|#.#|.#.",
    "K": "#.#|#.#|##.|#.#|#.#",
    "L": "#..|#..|#..|#..|###",
    "M": "#.#|###|###|#.#|#.#",
    "N": "#.#|##.|###|.##|#.#",
    "O": ".#.|#.#|#.#|#.#|.#.",
    "P": "##.|#.#|##.|#..|#..",
    "Q": ".#.|#.#|#.#|##.|.##",
    "R": "##.|#.#|##.|#.#|#.#",
    "S": ".##|#..|.#.|..#|##.",
    "T": "###|.#.|.#.|.#.|.#.",
    "U": "#.#|#.#|#.#|#.#|###",
    "V": "#.#|#.#|#.#|#.#|.#.",
    "W": "#.#|#.#|###|###|#.#",
    "X": "#.#|#.#|.#.|#.#|#.#",
    "Y": "#.#|#.#|.#.|.#.|.#.",
    "Z": "###|..#|.#.|#..|###",
    "0": "###|#.#|#.#|#.#|###",
    "1": ".#.|##.|.#.|.#.|###",
    "2": "##.|..#|.#.|#..|###",
    "3": "##.|..#|.#.|..#|##.",
    "4": "#.#|#.#|###|..#|..#",
    "5": "###|#..|##.|..#|##.",
    "6": ".##|#..|##.|#.#|.#.",
    "7": "###|..#|.#.|.#.|.#.",
    "8": ".#.|#.#|.#.|#.#|.#.",
    "9": ".#.|#.#|.##|..#|##.",
    "!": ".#.|.#.|.#.|...|.#.",
    "\"": "#.#|#.#|...|...|...",
    "#": "#.#|###|#.#|###|#.#",
    "$": ".##|##.|.#.|.##|##.",
    "%": "#.#|..#|.#.|#..|#.#",
    "&": ".#.|#.#|.#.|#.#|.##",
    "'": ".#.|.#.|...|...|...",
    "(": "..#|.#.|.#.|.#.|..#",
    ")": "#..|.#.|.#.|.#.|#..",
    "*": "#.#|.#.|#.#|...|...",
    "+": "...|.#.|###|.#.|...",
    ",": "...|...|...|.#.|#..",
    "-": "...|...|###|...|...",
    ".": "...|...|...|...|.#.",
    "/": "..#|..#|.#.|#..|#..",
    ":": "...|.#.|...|.#.|...",
    ";": "...|.#.|...|.#.|#..",
    "<": "..#|.#.|#..|.#.|..#",
    "=": "...|###|...|###|...",
    ">": "#..|.#.|..#|.#.|#..",
    "?": "##.|..#|.#.|...|.#.",
    "@": ".#.|#.#|###|#..|.##",
    "[": ".##|.#.|.#.|.#.|.##",
    "\\": "#..|#..|.#.|..#|..#",
    "]": "##.|.#.|.#.|.#.|##.",
    "^": ".#.|#.#|...|...|...",
    "_": "...|...|...|...|###",
    "`": "#..|.#.|...|...|...",
    "{": "..#|.#.|##.|.#.|..#",
    "|": ".#.|.#.|.#.|.#.|.#.",
    "}": "#..|.#.|.##|.#.|#..",
    "~": "...|.##|#.#|##.|...",
}

# A missing glyph draws a SOLID BLOCK, not the empty box a TTF would normally give you. A caption
# with a character this font does not cover is an authoring mistake, and a loud one costs a
# second to spot where a silent one ships.
NOTDEF = "###|###|###|###|###"


def rects(pattern):
    """Ink cells merged into horizontal runs -- one rectangle per run, in font units.

    Runs rather than per-pixel squares only to keep the contour count down; either would
    rasterise identically, because adjacent rectangles share an edge that the nonzero winding
    rule cancels and no device pixel is ever partly covered.
    """
    out = []
    for r, row in enumerate(pattern.split("|")):
        x = 0
        while x < len(row):
            if row[x] != "#":
                x += 1
                continue
            x1 = x
            while x1 + 1 < len(row) and row[x1 + 1] == "#":
                x1 += 1
            # Row r counted from the top of a CELL_H-tall cap sitting on the baseline at y=0.
            out.append((x * PX, (CELL_H - r - 1) * PX, (x1 + 1) * PX, (CELL_H - r) * PX))
            x = x1 + 1
    return out


def glyph_name(ch):
    return "space" if ch == " " else "uni%04X" % ord(ch)


LOWERCASE = "abcdefghijklmnopqrstuvwxyz"


def build(path=TTF):
    from fontTools.fontBuilder import FontBuilder
    from fontTools.pens.ttGlyphPen import TTGlyphPen

    chars = sorted(GLYPHS)
    # a-z get their OWN glyph ids carrying the capital outlines, rather than cmap entries pointing
    # at the capitals. See the header: sharing the glyph pulls uppercase I and J a pixel out of
    # line, and separate ids confine that to lowercase i and j.
    entries = ([(".notdef", NOTDEF)]
               + [(glyph_name(c), GLYPHS[c]) for c in chars]
               + [(glyph_name(c), GLYPHS[c.upper()]) for c in LOWERCASE])
    order = [n for n, _ in entries]

    pens = {}
    for name, pattern in entries:
        pen = TTGlyphPen(None)
        for (x0, y0, x1, y1) in rects(pattern):
            pen.moveTo((x0, y0))
            pen.lineTo((x1, y0))
            pen.lineTo((x1, y1))
            pen.lineTo((x0, y1))
            pen.closePath()
        pens[name] = pen.glyph()

    # Lowercase is covered because the roster already ships a caption that is not typed in caps --
    # `CameoCaption: 6x750 KT` at rules/ingame/nuclear-arsenal.yaml:249 and rules/powers.yaml:559.
    # Without it that x would draw the .notdef block.
    cmap = {ord(c): glyph_name(c) for c in chars}
    cmap.update({ord(c): glyph_name(c) for c in LOWERCASE})

    fb = FontBuilder(UPEM, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap(cmap)
    fb.setupGlyf(pens)
    fb.setupHorizontalMetrics({n: (ADVANCE * PX, 0) for n in order})
    fb.setupHorizontalHeader(ascent=CELL_H * PX, descent=-PX, lineGap=0)
    fb.setupNameTable({
        "familyName": "WW3 Caption",
        "styleName": "Regular",
        "uniqueFontIdentifier": "WW3MOD;WW3Caption;1.000",
        "fullName": "WW3 Caption",
        "psName": "WW3Caption-Regular",
        "version": "Version 1.000",
        "copyright": "Generated by tools/cameo/pixelfont.py for WW3MOD. GPLv3, as the rest of the mod.",
    })
    fb.setupOS2(sTypoAscender=CELL_H * PX, sTypoDescender=-PX, sTypoLineGap=0,
                usWinAscent=CELL_H * PX, usWinDescent=PX, sCapHeight=CELL_H * PX,
                achVendID="WW3M")
    fb.setupPost(isFixedPitch=1)
    fb.save(path)
    return path


# --- measuring ---------------------------------------------------------------------------------
def pil_font(path, size=PPEM):
    from PIL import ImageFont
    return ImageFont.truetype(path, size, layout_engine=ImageFont.Layout.BASIC)


def coverage(font, text):
    """Every non-zero coverage byte FreeType produces for `text`, as a flat list.

    PIL renders through FreeType with FT_LOAD_DEFAULT + FT_RENDER_MODE_NORMAL, which is the same
    hinting decision and the same render mode as FreeTypeFont.cs's FT_LOAD_RENDER. It is not the
    same freetype BUILD, so this is strong evidence and not proof; the header of
    WORKSPACE/mockups/glyph_probe.py makes the same caveat for the same reason.
    """
    mask = font.getmask(text, mode="L")
    return [v for v in mask if v]


def measure(font, text):
    """Width the widget will see: CameoCaptionCache calls SpriteFont.Measure, which sums the
    per-glyph ADVANCE (SpriteFont.cs LineWidth) rather than measuring an ink box."""
    return sum(font.getlength(c) for c in text)


# The captions the mod ships TODAY, hand-copied from rules/powers.yaml, rules/player.yaml and
# rules/ingame/nuclear-arsenal.yaml. Every one of these is drawn on screen right now, so a font
# that shortens any of them is a regression and not a change.
LIVE_CAPTIONS = ["PRECISION STR.", "0.3 KT", "10 KT", "50 KT", "100 KT", "1 KT",
                 "6x750 KT", "1.2 MT", "6 MT", "50 MT", "20 KT"]


def verify(path=TTF, verbose=True):
    font = pil_font(path)
    bad, lines = [], []

    sample = "".join(sorted(GLYPHS))
    cov = coverage(font, sample)
    grey = sorted({v for v in cov if v != 255})
    lines.append("  every glyph, %d lit pixels: %s" % (
        len(cov), "ALL 255" if not grey else "NOT 1-BIT -- other values seen: %r" % grey[:12]))
    if grey:
        bad.append("glyph coverage is not 1-bit")

    # Integer UI scales must stay exact too; a fractional one cannot and is reported, not failed.
    for scale in (2, 3):
        c = coverage(pil_font(path, PPEM * scale), sample)
        g = sorted({v for v in c if v != 255})
        lines.append("  at UI scale %dx (ppem %d): %s" % (
            scale, PPEM * scale, "ALL 255" if not g else "NOT 1-BIT %r" % g[:8]))
        if g:
            bad.append("coverage is not 1-bit at UI scale %dx" % scale)
    c = coverage(pil_font(path, 10), sample)  # ppem 10 == UI scale 1.5, the fractional case
    g = sorted({v for v in c if v != 255})
    lines.append("  at UI scale 1.5x (ppem 10): %s  <- EXPECTED: 896/10 is not a whole number of "
                 "units per pixel, so this one antialiases. Not a failure." % (
                     "all 255" if not g else "antialiased, %d grey values" % len(g)))

    # Advance: designed 4px for every glyph. If the autohinter moved one, widths stop being
    # predictable and every budget number in check_captions.py is wrong.
    wrong = {c: a for c, a in ((ch, font.getlength(ch)) for ch in GLYPHS) if a != ADVANCE}
    lines.append("  advance: %s" % ("all %dpx" % ADVANCE if not wrong else "WRONG: %r" % wrong))
    if wrong:
        bad.append("advances are not the designed width")

    # Ink box: 5 rows tall, sitting on the baseline. This is what puts the caption on slot row 45.
    # Checked PER GLYPH, because the failure this catches is not a wrong height but a single
    # letter snapped a pixel out of line by the autohinter -- see the header.
    want = (0, 0, ADVANCE, CELL_H)
    off = {c: font.getbbox(c) for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
           if font.getbbox(c) != want}
    lines.append("  A-Z 0-9 ink box == %r: %s" % (want, "all 36" if not off else "OFF: %r" % off))
    if off:
        bad.append("glyphs sit off the baseline grid: %s" % ", ".join(sorted(off)))

    lo = {c: font.getbbox(c) for c in LOWERCASE if font.getbbox(c) != want}
    lines.append("  lowercase off-grid: %s  <- EXPECTED to be exactly i and j; see the header. "
                 "check_captions.py rejects a caption containing either." % (sorted(lo) or "none"))

    # The comparison that is the whole point of the item.
    old = pil_font(os.path.join(ROOT, "engine", "mods", "common", "FreeSansBold.ttf"))
    o = sorted(coverage(old, sample))
    lines.append("  FreeSansBold 7px, same sample: max %d, median %d, %d/%d opaque" % (
        o[-1], o[len(o) // 2], sum(1 for v in o if v == 255), len(o)))

    lines.append("  budget %dpx plain / %dpx badged = %d / %d characters" % (
        BUDGET_PLAIN, BUDGET_BADGED, BUDGET_PLAIN // ADVANCE, BUDGET_BADGED // ADVANCE))
    for t in LIVE_CAPTIONS:
        w = measure(font, t)
        verdict = "fits" if w <= BUDGET_BADGED else "fits plain only" if w <= BUDGET_PLAIN else "TOO WIDE"
        lines.append("    %-18r %3.0fpx  %s" % (t, w, verdict))
        if w > BUDGET_PLAIN:
            bad.append("shipped caption %r does not fit" % t)

    if verbose:
        print("VERIFY %s at %dpx (upem %d, %d units/px)" % (os.path.relpath(path, ROOT), PPEM, UPEM, PX))
        print("\n".join(lines))
        print("  RESULT:", "PASS" if not bad else "FAIL -- " + "; ".join(bad))
    return bad


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--build", action="store_true", help="write the TTF")
    ap.add_argument("--verify", action="store_true", help="rasterise it and check the acceptance rules")
    args = ap.parse_args()
    if not (args.build or args.verify):
        ap.error("nothing to do: pass --build or --verify")

    if args.build:
        print("wrote", os.path.relpath(build(), ROOT))
    return 1 if (args.verify and verify()) else 0


if __name__ == "__main__":
    sys.exit(main())
