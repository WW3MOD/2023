#!/usr/bin/env python3
"""Candidate replacements for one glyph of WW3Caption, rendered THROUGH THE ENGINE'S FreeType.

    python tools/cameo/n_candidates.py            # -> WORKSPACE/mockups/caption-n-candidates.png

Written for the N glyph on 2026-09-20 and kept because the next glyph argument will want it.
pixelfont.py's --verify answers "is this font still 1-bit and on the grid"; it cannot answer
"does this letter read as the letter it is", which is a question only a render of real words
settles. So this builds one TTF per candidate, rasterises the shipped caption vocabulary through
ftprobe.py -- the engine's own freetype6, not Pillow's -- and lays the variants out side by side
at 1x, 3x and 6x nearest neighbour.

WHAT THE N ARGUMENT ESTABLISHED, AND WHY THIS IS NOT A PIXEL-DISTANCE TOOL. The glyph that
shipped until 2026-09-20 was FOUR pixels from its nearest neighbour -- further than the one that
replaced it -- and every N in the game still read as an S, because those two were the only
glyphs in the font with no full-height vertical stem. Pixel distance did not see it and could
not. Look at the strip.
"""

import argparse
import os
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import ftprobe          # noqa: E402  -- the engine's FreeType
import pixelfont        # noqa: E402  -- the font under test

SIZE = pixelfont.PPEM

# Real captions, from rules/cameo-captions.yaml and rules/powers.yaml, chosen for the adjacencies
# that decide the argument: A-N and D-N (2px pairs), plus H, M, U and the full row.
WORDS = ["SNIPER", "ENGINEER", "DRONE OP", "RIFLEMAN", "MINELAYER", "TEAM LEADER", "KINZHAL",
         "SANDBAGS", "TECHNICIAN", "TUNGUSKA", "MN HN AN UN DN RN 0N SN",
         "ABCDEFGHIJKLMNOPQRSTUVWXYZ"]

GLYPH = "N"
CANDIDATES = [
    ("CURRENT (shipping, broken)", "#.#|##.|#.#|.##|#.#",
     "stepped diagonal. NO full-height stem -- the one property it shares with S, "
     "and nothing else in the font has it"),
    ("(a) lowercase-n form  <== PICKED", "##.|#.#|#.#|#.#|#.#",
     "full left stem + shoulder. Nearest letters A/D/R/0 at 2px, and all four differ at a CORNER"),
    ("(b) as briefed", "#.#|##.|#.#|#.#|#.#",
     "NB the brief called this 'both stems intact' -- row 2 is ##. so the RIGHT stem breaks "
     "there. Reads as K"),
    ("(b') both stems intact", "#.#|###|#.#|#.#|#.#",
     "what (b) describes. ONE PIXEL from M, which N sits beside in RIFLEMAN/MINELAYER. Reads as H"),
    ("(c) full top bar", "###|#.#|#.#|#.#|#.#",
     "full stems, closed top. ONE PIXEL from 0 (zero), a vertical mirror of U, and symmetric "
     "where N is not"),
    ("(d) stem + step", "#..|##.|#.#|#.#|#.#",
     "full left stem, 3-row right stem. Ragged against a blocky full-height alphabet; reads as "
     "lowercase h"),
    ("(e) (a) + diagonal pixel", "##.|###|#.#|#.#|#.#",
     "(a) with the diagonal carried one step down. Safest by pixel count (3px) but shares M's "
     "heavy midline"),
]


def render_line(ft, text, size=SIZE):
    """The glyph bitmaps the game would upload, placed the way SpriteFont.DrawText places them.

    ftprobe.art's placement, into an image instead of characters: baseline at row `size`, each
    glyph's top-left at (pen + bitmapLeft, baseline - bitmapTop).
    """
    glyphs, width = ft.line(text, size)
    h = size + 2
    grid = bytearray((width + 2) * h)
    pen, baseline = 0, size
    for _, g in glyphs:
        if g:
            gw, gh, adv, left, top, data = g
            for j in range(gh):
                for i in range(gw):
                    y, x = baseline - top + j, pen + left + i
                    if 0 <= y < h and 0 <= x < width + 2:
                        k = y * (width + 2) + x
                        grid[k] = max(grid[k], data[j * gw + i])
            pen += adv
    img = Image.frombytes("L", (width + 2, h), bytes(grid))
    return Image.merge("RGBA", (img.point(lambda v: 255 if v else 30),) * 3
                       + (img.point(lambda v: 255),))


def build_variant(pattern, glyph=GLYPH):
    """A throwaway TTF with one glyph swapped. Written to the temp dir, never into the mod."""
    keep = pixelfont.GLYPHS[glyph]
    pixelfont.GLYPHS[glyph] = pattern
    try:
        path = os.path.join(tempfile.gettempdir(), "ww3caption_%s_%s.ttf" % (
            glyph, pattern.replace("|", "_").replace("#", "x").replace(".", "o")))
        pixelfont.build(path)
    finally:
        pixelfont.GLYPHS[glyph] = keep
    return path


def _font(name, size):
    return ImageFont.truetype(os.path.join(ROOT, "engine", "mods", "common", name), size)


def render(out=None):
    out = out or os.path.join(ROOT, "WORKSPACE", "mockups", "caption-n-candidates.png")
    hd, note = _font("FreeSansBold.ttf", 14), _font("FreeSans.ttf", 11)
    mono = _font("FreeSansBold.ttf", 12)

    zooms = [1, 3, 6]
    colw = {1: 120, 3: 340, 6: 660}
    label_w, gap = 150, 14
    width = label_w + sum(colw[z] + gap for z in zooms) + gap

    blocks = []
    for name, pattern, why in CANDIDATES:
        ft = ftprobe.FreeType(build_variant(pattern))
        blocks.append((name, pattern, why,
                       [(w, {z: render_line(ft, w) for z in zooms}) for w in WORDS]))

    rowh = SIZE * 6 + 12
    blockh = 54 + len(WORDS) * (rowh + 8) + 16
    sheet = Image.new("RGBA", (width, 96 + len(blocks) * blockh + 40), (28, 30, 35, 255))
    d = ImageDraw.Draw(sheet)

    d.text((gap, 10), "Cameo caption font: candidate replacements for N  "
           "(GLYPHS[\"N\"], tools/cameo/pixelfont.py:125)",
           font=_font("FreeSansBold.ttf", 17), fill=(245, 245, 250, 255))
    d.text((gap, 36), "Every glyph below is rasterised by the ENGINE'S OWN freetype6 1.0.11 "
           "(ftprobe.py), at the shipped 7px, then scaled by nearest neighbour. These are the "
           "bytes the game uploads.", font=note, fill=(160, 162, 172, 255))
    d.text((gap, 52), "SYMPTOM on main @ 6d70f6f8: every N read as S -- RIFLEMAN/RIFLEMAS, "
           "SNIPER/SSIPER, TECHNICIAN/TECHSICIAS. Hard constraints: 3 ink columns, 4px advance, "
           "5 rows; must not collide with M H A U S.", font=note, fill=(160, 162, 172, 255))

    x0 = label_w + gap
    for c, z in enumerate(zooms):
        d.text((x0 + sum(colw[q] + gap for q in zooms[:c]), 76), "%dx" % z, font=mono,
               fill=(255, 210, 120, 255))

    y = 96
    for name, pattern, why, rows in blocks:
        broken, picked = name.startswith("CURRENT"), "PICKED" in name
        d.rectangle([gap // 2, y - 4, width - gap // 2, y + blockh - 12],
                    fill=(44, 30, 30, 255) if broken
                    else (30, 46, 36, 255) if picked else (36, 39, 45, 255))
        d.text((gap, y + 2), name, font=hd,
               fill=(255, 140, 130, 255) if broken
               else (150, 245, 170, 255) if picked else (255, 235, 180, 255))
        d.text((gap, y + 22), pattern, font=mono, fill=(235, 235, 240, 255))
        d.text((gap + 200, y + 24), why, font=note, fill=(155, 157, 167, 255))
        gx = width - gap - 60
        for r, line in enumerate(pattern.split("|")):
            for i, ch in enumerate(line):
                d.rectangle([gx + i * 11, y + 2 + r * 11, gx + i * 11 + 9, y + 2 + r * 11 + 9],
                            fill=(255, 255, 255, 255) if ch == "#" else (60, 63, 70, 255))
        yy = y + 54
        for word, imgs in rows:
            label = ("A-Z" if word.startswith("ABCDEF")
                     else word if len(word) <= 13 else "N vs M H A U D R 0 S")
            d.text((gap, yy + rowh // 2 - 7), label, font=mono if len(label) <= 13 else note,
                   fill=(200, 202, 212, 255))
            for c, z in enumerate(zooms):
                im = imgs[z]
                im = im.resize((im.size[0] * z, im.size[1] * z), Image.NEAREST)
                sheet.alpha_composite(im, (x0 + sum(colw[q] + gap for q in zooms[:c]),
                                           yy + (rowh - im.size[1]) // 2))
            yy += rowh + 8
        y += blockh

    os.makedirs(os.path.dirname(out), exist_ok=True)
    sheet.save(out)
    print("wrote", os.path.relpath(out, ROOT), sheet.size)
    return out


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("out", nargs="?")
    render(ap.parse_args().out)
