#!/usr/bin/env python3
"""Before/after for the 1-bit caption font, band on and band off, on the real cameo art.

    python tools/cameo/caption_proof.py                 # -> WORKSPACE/mockups/caption-font-1bit.png
    python tools/cameo/caption_proof.py OUT.png

Sits beside WORKSPACE/mockups/caption-vs-baked.png, which compared the GENERATED caption against
the BAKED one and found the only real gap was antialiasing. This compares the two generated
captions -- FreeSansBold 7px against WW3Caption 7px -- in the two states the band can be in,
because the whole point of the item is that the difference is invisible in one of them.

HOW TRUE IS IT. The glyph bitmaps are rasterised by the ENGINE'S OWN freetype6 through
ftprobe.py, and the outline is a port of SpriteFont.CreateContrastGlyph's dilation rather than
PIL's crude 8-way smear -- so the letters here are the bytes the game would upload. The cameo art
is decoded from the shipped SHPs. What is HAND-PORTED and can therefore drift is the layout:
CameoCaptionCache.Build's arithmetic, restated below, exactly as binmock.py restates it and with
the same warning. It is a mockup, not a screenshot.
"""

import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import binmock          # noqa: E402  -- cameo decoding and the palette
import ftprobe          # noqa: E402  -- the engine's FreeType
import pixelfont        # noqa: E402  -- the font under test and the slot constants

# chrome/ingame-player.yaml, both palettes. Same numbers binmock.py carries, same reason.
SLOT_W, SLOT_H = 62, 46
SIDE_MARGIN, BOTTOM_MARGIN, BAND_PAD, BADGE_GAP = 1, 0, 2, 1
SPRITE_OFFSET = (-1, -1)
SIZE = 7                                  # Fonts: CameoCaption: Size, and Caption: Size
BG = (24, 26, 30, 255)                    # the sidebar behind a cameo
FG, CONTRAST = (255, 255, 255), (0, 0, 0)  # CaptionColor / CaptionContrastColor defaults

OLD_TTF = os.path.join(ROOT, "engine", "mods", "common", "FreeSansBold.ttf")
NEW_TTF = pixelfont.TTF


def contrast_mask(w, h, data, r=1):
    """SpriteFont.CreateContrastGlyph, ported: greyscale dilation by a circular weight map.

    Reproduced rather than approximated because it is what makes the difference VISIBLE with the
    band off. The r=1 element is [[.6,1,.6],[1,1,1],[.6,1,.6]] (SpriteFont.CreateCircularWeightMap
    documents exactly this output), so a 1-bit glyph gets a 255 ring orthogonally and a 153 one
    diagonally -- the outline is softer than the letter even when the letter is solid.
    """
    elem = [0.6, 1.0, 0.6, 1.0, 1.0, 1.0, 0.6, 1.0, 0.6]
    ow, oh = w + 2 * r, h + 2 * r
    out = bytearray(ow * oh)
    for j in range(oh):
        for i in range(ow):
            best = 0
            for wj in range(2 * r + 1):
                for wi in range(2 * r + 1):
                    ii, jj = i + wi - 2 * r, j + wj - 2 * r
                    if 0 <= ii < w and 0 <= jj < h:
                        v = int(elem[wj * (2 * r + 1) + wi] * data[jj * w + ii])
                        if v > best:
                            best = v
            out[j * ow + i] = best
    return ow, oh, out


def blend(img, x, y, w, h, data, rgb):
    """Composite a coverage bitmap. SpriteFont copies the byte into ALL FOUR channels, so the
    coverage is the alpha AND scales the colour -- which is why a half-covered pixel of white
    text reads as dim grey rather than as translucent white."""
    px = img.load()
    for j in range(h):
        for i in range(w):
            a = data[j * w + i]
            if not a:
                continue
            tx, ty = x + i, y + j
            if not (0 <= tx < img.size[0] and 0 <= ty < img.size[1]):
                continue
            sr, sg, sb, sa = px[tx, ty]
            f = a / 255.0
            px[tx, ty] = (int(rgb[0] * f + sr * (1 - f)),
                          int(rgb[1] * f + sg * (1 - f)),
                          int(rgb[2] * f + sb * (1 - f)),
                          max(sa, a))


def draw_caption(img, ft, text, x, top):
    """SpriteFont.DrawTextWithContrast(text, (x, top), White, Black, 1), glyph by glyph.

    `top` is the LINE BOX; DrawText adds `size` to reach the baseline, and a glyph's bitmap goes
    at (pen + bitmapLeft, baseline - bitmapTop).
    """
    baseline = top + SIZE
    pen = x
    placed = []
    for c in text:
        g = ft.glyph(c, SIZE)
        if g and g[0] and g[1]:
            placed.append((pen + g[3], baseline - g[4], g))
        pen += g[2] if g else 0

    for gx, gy, (w, h, _, _, _, data) in placed:          # the outline pass, first
        ow, oh, mask = contrast_mask(w, h, data)
        blend(img, gx - 1, gy - 1, ow, oh, mask, CONTRAST)
    for gx, gy, (w, h, _, _, _, data) in placed:          # then the letters on top
        blend(img, gx, gy, w, h, data, FG)


def slot(cameo, caption, ft, badge=None, band=True):
    """One 62x46 icon slot. CameoCaptionCache.Build, hand-ported -- see the module header."""
    img = Image.new("RGBA", (SLOT_W, SLOT_H), BG)
    if cameo is not None:
        img.alpha_composite(cameo, SPRITE_OFFSET)

    reserved = badge.size[0] + BADGE_GAP if badge else 0
    text, width = None, 0
    if caption:
        budget = SLOT_W - 2 * SIDE_MARGIN - reserved
        t = caption.strip()
        while t:
            w = sum(ft.glyph(c, SIZE)[2] for c in t)
            if w <= budget:
                text, width = t, w
                break
            t = t[:-1].rstrip()

    bottom = SLOT_H - BOTTOM_MARGIN
    if text:
        top = bottom - SIZE
        if band:
            ImageDraw.Draw(img).rectangle(
                [0, max(0, top - BAND_PAD), SLOT_W - 1, min(SLOT_H, top + SIZE + BAND_PAD) - 1],
                fill=(0, 0, 0, 255))
        draw_caption(img, ft, text, (SLOT_W - reserved - width) // 2, top)
    if badge:
        img.alpha_composite(badge, (SLOT_W - SIDE_MARGIN - badge.size[0], bottom - badge.size[1]))
    return img


# (art file, caption, badged, what this row is here to show)
ROWS = [
    ("precicon", "PRECISION STR.", False,
     "Untexted art + the longest shipped caption. 56px in both fonts, on the 60px budget."),
    ("paranukeicon", "0.3 KT", True,
     "Baked PARANUKE lettering underneath -- the band is covering it, not decorating."),
    ("v2bdgricon", "6x750 KT", True,
     "The widest badged caption, and the mod's one lowercase letter."),
    ("t90icon", "T90", False,
     "Baked lettering the caption table would replace. Band off here shows both words at once."),
]


def render(out=None):
    out = out or os.path.join(ROOT, "WORKSPACE", "mockups", "caption-font-1bit.png")
    pal = binmock.load_palette(binmock.find_palette())
    from badge import trefoil
    nuke = trefoil(13)
    fonts = [("BEFORE  FreeSansBold 7px", ftprobe.FreeType(OLD_TTF)),
             ("AFTER  WW3Caption 7px", ftprobe.FreeType(NEW_TTF))]

    zoom, gap, label_w = 6, 10, 250
    cell = SLOT_W * zoom
    cols = 4                                     # old/band, old/no band, new/band, new/no band
    head, foot, rowlabel = 62, 74, 34
    width = label_w + cols * (cell + gap) + gap
    height = head + len(ROWS) * (cell + rowlabel + gap) + foot
    sheet = Image.new("RGBA", (width, height), (32, 34, 39, 255))
    d = ImageDraw.Draw(sheet)
    title = ImageFont.truetype(OLD_TTF, 15)
    hd = ImageFont.truetype(OLD_TTF, 11)
    note = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSans.ttf"), 11)

    d.text((gap, 8), "Cameo caption font: FreeSansBold 7px vs WW3Caption 7px, band on and band off",
           font=title, fill=(245, 245, 250, 255))
    d.text((gap, 28),
           "Glyphs rasterised by the engine's own freetype6 1.0.11 at 6x nearest-neighbour. "
           "Layout is a hand port of CameoCaptionCache.Build.",
           font=note, fill=(160, 162, 172, 255))

    x0 = label_w + gap
    for c, (fname, band) in enumerate([(fonts[0][0], True), (fonts[0][0], False),
                                       (fonts[1][0], True), (fonts[1][0], False)]):
        d.text((x0 + c * (cell + gap), head - 18),
               fname + ("   band ON" if band else "   band OFF"), font=hd,
               fill=(255, 210, 120, 255) if c >= 2 else (190, 192, 200, 255))

    y = head
    for art, caption, badged, why in ROWS:
        cameo = binmock.load_cameo(art, pal)
        d.text((gap, y + 4), art, font=hd, fill=(235, 235, 240, 255))
        for line, dy in zip(_wrap(why, 34), range(18, 200, 14)):
            d.text((gap, y + dy), line, font=note, fill=(150, 152, 162, 255))
        for c, (ft, band) in enumerate([(fonts[0][1], True), (fonts[0][1], False),
                                        (fonts[1][1], True), (fonts[1][1], False)]):
            s = slot(cameo, caption, ft, nuke if badged else None, band)
            sheet.alpha_composite(s.resize((cell, cell * SLOT_H // SLOT_W), Image.NEAREST),
                                  (x0 + c * (cell + gap), y))
        # actual size, under the blow-up, because 6x flatters everything
        for c, (ft, band) in enumerate([(fonts[0][1], True), (fonts[0][1], False),
                                        (fonts[1][1], True), (fonts[1][1], False)]):
            s = slot(cameo, caption, ft, nuke if badged else None, band)
            sheet.alpha_composite(s, (x0 + c * (cell + gap), y + cell * SLOT_H // SLOT_W + 6))
        y += cell * SLOT_H // SLOT_W + rowlabel + gap + 16

    d.text((gap, y + 6),
           "WITH THE BAND ON the two fonts differ only in their letterforms -- that is the state "
           "the mod ships in today and the state it stays in.",
           font=note, fill=(200, 202, 212, 255))
    d.text((gap, y + 22),
           "WITH THE BAND OFF the difference is the item: FreeSansBold has no fully opaque pixel "
           "at 7px (3 of 1307, measured through the engine's freetype6), so its",
           font=note, fill=(200, 202, 212, 255))
    d.text((gap, y + 38),
           "caption is a grey stipple over the art. WW3Caption is 481 of 481 opaque. Turning the "
           "band off is NOT part of this change; it needs untexted art first.",
           font=note, fill=(200, 202, 212, 255))

    os.makedirs(os.path.dirname(out), exist_ok=True)
    sheet.save(out)
    print("wrote", os.path.relpath(out, ROOT), sheet.size)
    return out


def _wrap(text, n):
    words, line, out = text.split(), "", []
    for w in words:
        if len(line) + len(w) + 1 > n:
            out.append(line)
            line = w
        else:
            line = (line + " " + w).strip()
    if line:
        out.append(line)
    return out


if __name__ == "__main__":
    render(sys.argv[1] if len(sys.argv) > 1 else None)
