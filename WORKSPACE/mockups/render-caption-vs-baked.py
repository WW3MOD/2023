#!/usr/bin/env python3
"""Generated caption against the baked lettering it is meant to be indistinguishable from.

The user's direction on 2026-09-08 is that runtime captions become the standard for every
unit, and that replacing a cameo's art with an untexted version plus a CameoCaption should
be a visual no-op. That is a claim about pixels, so this puts them side by side:

  * `precicon` as it ships -- baked "PRECISION STR.", which keeps its baked text and will
    sit directly beside generated ones in the same sidebar
  * the same art with a generated caption of the same words, drawn the way the widget
    draws it, at the anchored geometry
  * a generated caption at the geometry that shipped (CaptionBottomMargin 2), which is
    what the user noticed floating

Row rulers mark slot rows 40-45 so the alignment is countable rather than eyeballed.

    python3 WORKSPACE/mockups/render-caption-vs-baked.py
"""

import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "tools/cameo"))

import binmock  # noqa: E402

OUT = os.path.join(HERE, "caption-vs-baked.png")
ZOOM = 6
BG = (32, 34, 39, 255)


def generated(art, text, margin, font, band=True):
    """One slot with a runtime caption at an arbitrary bottom margin."""
    img = Image.new("RGBA", (binmock.SLOT_W, binmock.SLOT_H), (24, 26, 30, 255))
    img.alpha_composite(art, binmock.SPRITE_OFFSET)
    d = ImageDraw.Draw(img)
    tw = sum(font.getlength(c) for c in text)
    top = binmock.SLOT_H - margin - 7
    if band:
        pad = binmock.BAND_PAD
        d.rectangle([0, max(0, top - pad), binmock.SLOT_W - 1,
                     min(binmock.SLOT_H, top + 7 + pad) - 1], fill=(0, 0, 0, 255))
    x = (binmock.SLOT_W - int(tw)) // 2
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            if dx or dy:
                d.text((x + dx, top + dy), text, font=font, fill=(0, 0, 0, 255))
    d.text((x, top), text, font=font, fill=(255, 255, 255, 255))
    return img


def main():
    pal = binmock.load_palette(binmock.find_palette())
    font = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSansBold.ttf"), 7)
    lab = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSans.ttf"), 11)
    title = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSansBold.ttf"), 13)
    tiny = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSans.ttf"), 9)

    prec = binmock.load_cameo("precicon", pal)
    bare = binmock.load_cameo("kinzhalicon", pal)  # photo cameo: no baked lettering at all

    panels = [
        (binmock.slot(prec, None, None, font), "BAKED", "precicon as it ships. No runtime caption, so no band."),
        (generated(prec, "PRECISION STR.", 0, font), "GENERATED, anchored",
         "Same words, drawn by the widget at CaptionBottomMargin 0."),
        (generated(prec, "PRECISION STR.", 2, font), "GENERATED, as shipped",
         "CaptionBottomMargin 2 - two rows high. What the user noticed."),
        (generated(bare, "KINZHAL", 0, font, band=False), "GENERATED on untexted art",
         "No baked text underneath, so the band is off. The future case."),
    ]

    cw, ch = binmock.SLOT_W * ZOOM, binmock.SLOT_H * ZOOM
    pad = 16
    ruler = 30
    width = pad + len(panels) * (cw + pad) + ruler
    height = pad + 24 + 18 + ch + 44
    img = Image.new("RGBA", (width, height), BG)
    d = ImageDraw.Draw(img)
    d.text((pad, pad - 4), "Runtime caption vs baked lettering, 6x - slot rows 40-45 ruled",
           font=title, fill=(245, 245, 250, 255))

    y0 = pad + 24 + 18
    for i, (tile, name, note) in enumerate(panels):
        x = pad + i * (cw + pad)
        d.text((x, pad + 22), name, font=lab, fill=(235, 235, 240, 255))
        img.alpha_composite(tile.resize((cw, ch), Image.NEAREST), (x, y0))
        # Wrap the note under its panel.
        words, line, ly = note.split(), "", 0
        for word in words:
            trial = (line + " " + word).strip()
            if tiny.getlength(trial) > cw:
                d.text((x, y0 + ch + 4 + ly), line, font=tiny, fill=(155, 157, 167, 255))
                line, ly = word, ly + 11
            else:
                line = trial
        d.text((x, y0 + ch + 4 + ly), line, font=tiny, fill=(155, 157, 167, 255))

    # Row rulers, drawn ONLY in the gutters between panels and in the right margin. Ruling across
    # the panels would put red lines over the very pixels being compared, which is what the first
    # cut of this image did.
    gutters = [(pad + i * (cw + pad) + cw, pad + (i + 1) * (cw + pad)) for i in range(len(panels) - 1)]
    gutters.append((pad + (len(panels) - 1) * (cw + pad) + cw, width - ruler))
    for row in range(40, 46):
        yy = y0 + row * ZOOM + ZOOM // 2
        for x0, x1 in gutters:
            d.line([(x0 + 1, yy), (x1 - 1, yy)], fill=(255, 100, 100, 150))
        d.text((width - ruler + 4, yy - 5), str(row), font=tiny, fill=(255, 130, 130, 255))
    d.text((pad, y0 + 46 * ZOOM + 4), "", font=tiny, fill=BG)

    img.save(OUT)
    print("wrote", OUT, img.size)


if __name__ == "__main__":
    main()
