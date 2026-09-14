#!/usr/bin/env python3
"""Rasterise the SHIPPED diamond glyphs and put their silhouettes on the record.

Run from the repo root:  python WORKSPACE/mockups/glyph_probe.py

This is step 1 and 2 of the brief: get the true bitmap, then print the per-row
ink-width profile of both glyphs so "same shape as the current one" is checkable
rather than asserted. It also answers step 3 -- whether U+25CA and U+2666 are the
same outline at all.

WHAT THE ENGINE DOES, and this reproduces it exactly:

  mod.yaml:315-318     TinyBold = common|FreeSansBold.ttf, Size 10, Ascender 8
  WithTextDecoration.cs:27   Font defaults to "TinyBold"; the YAML at
                             defaults.yaml:928-964 does NOT override it, so the
                             graded diamond is TinyBold.
  DecorationRowGeometry.cs:46  GlyphFontSize = 10, independently asserting the
                             same size; :54 GlyphInkBottomOffset = 10/2 = 5.
  SpriteFont.cs:259    font.CreateGlyph(c, size, deviceScale)
  FreeTypeFont.cs:81-86
        FT_Set_Pixel_Sizes(face, size*deviceScale, size*deviceScale)
        FT_Load_Char(face, c, FT_LOAD_RENDER)

  FT_LOAD_RENDER alone means FT_LOAD_DEFAULT | render: HINTING IS ON (the font's
  own bytecode where present, else the autohinter) and the render mode is
  FT_RENDER_MODE_NORMAL -- an 8-BIT ANTIALIASED coverage bitmap, not a bilevel
  one. SpriteFont.cs:285-293 copies that coverage byte into all four channels
  (rgb AND alpha), and DrawText tints it (:103, :120). So the glyph on screen is
  a GREYSCALE-ANTIALIASED, PREMULTIPLIED-LOOKING tint of the hinted bitmap.
  That is the single most important fact for this job: the shipped mark has SOFT
  EDGES, and a hard-edged polygon of the same nominal size cannot look like it.

  deviceScale is the UI scale (SpriteFont.cs:35). It is 1.0 at 100% UI scale,
  which is what every number in DecorationRowGeometry assumes -- GlyphFontSize is
  a bare 10 with no scale term. So we rasterise at 10 px.

FIDELITY LIMITS, stated rather than glossed:
  * PIL's ImageFont.truetype(size=N) calls FT_Set_Char_Size(0, N*64, 0, 0), and
    with dpi defaulting to 72 that is 1pt == 1px, i.e. pixel size N. Equivalent
    to FT_Set_Pixel_Sizes(N, N) for a scalable face. Verified below by checking
    the resulting ink box against the 6x9 the tree independently reports.
  * PIL renders with FT_LOAD_DEFAULT too, so hinting matches in KIND. The exact
    FreeType build differs (PIL ships its own), so a hinted stem could in
    principle land one pixel differently from the game's freetype6. The ink-box
    agreement below is the evidence that it does not, for these two glyphs.
"""

import os
import sys

from PIL import Image, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
TTF = os.path.join(REPO, "engine", "mods", "common", "FreeSansBold.ttf")

# mod.yaml:315-318 TinyBold. DecorationRowGeometry.cs:46 asserts the same 10.
FONT_SIZE = 10

HOLLOW = "◊"      # WithSpottedDecoration.cs:92  HollowText, U+25CA LOZENGE
SOLID = "♦"       # WithSpottedDecoration.cs:96  SolidText,  U+2666 BLACK DIAMOND SUIT

# Glyphs the mod draws in the same font+size that we also want on the record,
# because the Shift-held column of the mockup has to show them.
STANCE = {"HoldFire X": "X", "Ambush A": "A", "HoldPosition H": "H", "Hunt >": ">"}


def glyph(ch, size=FONT_SIZE, ttf=TTF):
    """The engine's bitmap for one character: an 8-bit coverage image, cropped to ink.

    Returns (image, (left, top, w, h)) where left/top are the offsets PIL reports,
    i.e. the same quantities FreeTypeFont reads out of bitmap_left / bitmap_top.
    """
    f = ImageFont.truetype(ttf, size)
    mask = f.getmask(ch, mode="L")          # FT_RENDER_MODE_NORMAL, 8-bit coverage
    im = Image.frombytes("L", mask.size, bytes(mask))
    bb = im.getbbox()
    if bb is None:
        return None, None
    return im.crop(bb), (bb[0], bb[1], bb[2] - bb[0], bb[3] - bb[1])


def profile(im, threshold=0):
    """Ink width per row, top to bottom. threshold=0 counts any non-zero coverage."""
    px = im.load()
    return [sum(1 for x in range(im.width) if px[x, y] > threshold)
            for y in range(im.height)]


def coverage_rows(im):
    """Per-row coverage bytes, so the antialiasing is visible and not just asserted."""
    px = im.load()
    return [[px[x, y] for x in range(im.width)] for y in range(im.height)]


def art(im, threshold=0):
    """ASCII picture of one glyph: '#' full, '+' partial, '.' empty."""
    px = im.load()
    out = []
    for y in range(im.height):
        row = ""
        for x in range(im.width):
            v = px[x, y]
            row += "#" if v >= 200 else ("+" if v > threshold else ".")
        out.append(row)
    return out


def report(name, ch, size=FONT_SIZE):
    im, box = glyph(ch, size)
    print(f"\n=== {name}  U+{ord(ch):04X}  @{size}px ===")
    if im is None:
        print("  NO INK -- the font has no glyph for this codepoint, or it is blank.")
        return None
    left, top, w, h = box
    p = profile(im)
    print(f"  ink box        : {w}x{h}   (bitmap_left {left}, bitmap_top-ish {top})")
    print(f"  row ink widths : {p}")
    print(f"  total ink      : {sum(p)} px")
    print(f"  any-coverage   : {sum(p)} px;  >=50% coverage: "
          f"{sum(profile(im, 127))} px;  >=78% : {sum(profile(im, 199))} px")
    print("  picture (# >=200, + partial):")
    for row in art(im):
        print(f"    |{row}|")
    print("  coverage bytes per row:")
    for row in coverage_rows(im):
        print("    " + " ".join(f"{v:3d}" for v in row))
    return im, box, p


def compare(a, b, na, nb):
    """Are the two outlines the same shape? Answers brief step 3."""
    print(f"\n=== ARE {na} AND {nb} THE SAME OUTLINE? ===")
    ia, ba, pa = a
    ib, bb_, pb = b
    print(f"  {na}: {ba[2]}x{ba[3]} ink={sum(pa)} profile={pa}")
    print(f"  {nb}: {bb_[2]}x{bb_[3]} ink={sum(pb)} profile={pb}")
    if (ba[2], ba[3]) != (bb_[2], bb_[3]):
        print(f"  VERDICT: DIFFERENT SIZE. {na} is {ba[2]}x{ba[3]}, "
              f"{nb} is {bb_[2]}x{bb_[3]}. They are not one shape filled and unfilled.")
        return False
    # Same box: is the solid one exactly the hollow one's outline filled in?
    pxa, pxb = ia.load(), ib.load()
    only_a = only_b = both = 0
    for y in range(ia.height):
        for x in range(ia.width):
            va, vb = pxa[x, y] > 0, pxb[x, y] > 0
            if va and vb:
                both += 1
            elif va:
                only_a += 1
            elif vb:
                only_b += 1
    print(f"  overlap: both={both}  only-{na}={only_a}  only-{nb}={only_b}")
    if only_a == 0:
        print(f"  VERDICT: {na}'s ink is a SUBSET of {nb}'s -- consistent with one "
              f"outline, hollow vs filled.")
    else:
        print(f"  VERDICT: {na} has {only_a} px OUTSIDE {nb}. The outlines DIFFER; "
              f"'the same shape as the current one' is ambiguous and must be resolved.")
    return only_a == 0


def main():
    if not os.path.isfile(TTF):
        sys.exit(f"missing font: {TTF}")
    print(f"font : {TTF}")
    print(f"size : {FONT_SIZE} px  (TinyBold, mod.yaml:315-318; "
          f"DecorationRowGeometry.cs:46 asserts the same)")
    print("mode : FT_LOAD_RENDER -> FT_RENDER_MODE_NORMAL, 8-bit coverage, hinting ON")

    h = report("HollowText (LOZENGE)", HOLLOW)
    s = report("SolidText (BLACK DIAMOND SUIT)", SOLID)

    # The pair the trait's PITFALL says are ABSENT. Prove it rather than trusting
    # the comment -- the comment is the whole reason the mark uses the odd pair it
    # does, so it is worth one check.
    #
    # THE TEST HAS TO BE AGAINST .notdef, NOT AGAINST "has ink". An unmapped
    # codepoint makes FT_Load_Char fall back to glyph index 0, and FreeSansBold's
    # .notdef is a 3x7 HOLLOW RECTANGLE -- it renders 16 px of ink. A first pass of
    # this script asked "did anything rasterise?", got 3x7 for both diamonds, and
    # concluded the trait's PITFALL was wrong. It is not wrong: U+E000 (private
    # use), U+0870 and U+4E00 all produce the SAME 16 bytes. Comparing against a
    # codepoint no font maps is what distinguishes "absent" from "present".
    print("\n=== are the obvious diamonds really absent? (PITFALL check) ===")
    notdef = None
    im, _ = glyph("")          # private use: nothing maps this
    if im is not None:
        notdef = im.tobytes()
        print(f"  .notdef reference (U+E000): {im.width}x{im.height}, "
              f"{sum(1 for b in notdef if b)} px of ink -- a hollow box, NOT blank")
    for name, cp in (("U+25C6 BLACK DIAMOND", "◆"),
                     ("U+25C7 WHITE DIAMOND", "◇")):
        im, _ = glyph(cp)
        if im is None:
            state = "BLANK -- absent, confirms WithSpottedDecoration.cs:84-91"
        elif notdef is not None and im.tobytes() == notdef:
            state = (f"{im.width}x{im.height}, BYTE-IDENTICAL to .notdef -- ABSENT. "
                     f"Confirms WithSpottedDecoration.cs:84-91.")
        else:
            state = (f"PRESENT, {im.width}x{im.height}, and NOT .notdef -- this would "
                     f"contradict the trait's PITFALL comment. Investigate before acting.")
        print(f"  {name}: {state}")

    if h and s:
        compare(h, s, "LOZENGE", "DIAMOND SUIT")

    print("\n=== the stance glyphs, same font and size (for the Shift column) ===")
    for name, ch in STANCE.items():
        im, box = glyph(ch)
        print(f"  {name:16s} {box[2]}x{box[3]}  profile={profile(im)}")

    # Sanity: does the size the tree reports (6x9) fall out of this at all?
    print("\n=== cross-check against the tree's independent claim ===")
    if h:
        w, ht = h[1][2], h[1][3]
        print(f"  diamond-variants.md and the brief both say the shipped mark is 6x9.")
        print(f"  This rasterisation gives {w}x{ht} for the hollow glyph.")
        print(f"  {'AGREES' if (w, ht) == (6, 9) else 'DOES NOT AGREE -- investigate'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
