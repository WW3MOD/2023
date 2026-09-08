#!/usr/bin/env python3
"""Two candidate photos for the conventional Kh-47M2 Kinzhal cameo, in situ.

The question is NOT which is the better photograph. It is which one still reads as
*a Kinzhal* at 62x46 in the row it will actually sit in -- beside the GBU-57 Bunker
Buster it used to share `precicon` with, and beside a badged nuclear entry. So each
candidate is shown three times over:

  * as the conventional Kinzhal ships today -- bare art, no caption, no badge
  * as a badged 50 kt Kinzhal-N would draw the same art, because the badge lands on
    the bottom-right corner of whatever is underneath it and that corner differs
    between the two photos
  * flanked by its real neighbours, so the comparison is against the sidebar and not
    against the other candidate alone

Rendered at 1x, 2x and 3x. 1x is the only row that is true to what the player sees;
the other two exist because nobody can judge a 13px trefoil at 1x on a screenshot.

    python3 WORKSPACE/mockups/render-kinzhal-source-compare.py

Reads the staged candidate PNGs if they are present, else re-stages them from
C:/Users/fredr/Desktop/WW3_Images via tools/cameo/convert.py.
"""

import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "tools/cameo"))

from badge import trefoil  # noqa: E402
import binmock  # noqa: E402

SRC_DIR = "C:/Users/fredr/Desktop/WW3_Images"
STAGED = os.path.join(ROOT, "tools/cameo/work/staged")

# (staged stem, source filename, one-line verdict)
CANDIDATES = [
    ("kinzhalicon", "kinzhal.jpg",
     "INSTALLED. Reads as a jet; the missile itself is a pale sliver on a pale belly."),
    ("candeicon", "sverhzvukovoi-...-1625087881.jpg",
     "ALTERNATIVE. Bluer sky, and the missile stays a distinct body under the fuselage."),
]

OUT = os.path.join(HERE, "kinzhal-source-compare.png")
BG = (32, 34, 39, 255)


def stage():
    """Make sure both candidates exist as 64x48 cameos, without installing either."""
    if all(os.path.exists(os.path.join(STAGED, s + ".png")) for s, _, _ in CANDIDATES):
        return
    work = os.path.join(ROOT, "tools/cameo/work/candidates")
    os.makedirs(work, exist_ok=True)
    for stem, src, _ in CANDIDATES:
        dst = os.path.join(work, stem + os.path.splitext(src)[1])
        if not os.path.exists(dst):
            import shutil
            shutil.copyfile(os.path.join(SRC_DIR, src), dst)
    subprocess.check_call([sys.executable, os.path.join(ROOT, "tools/cameo/convert.py"),
                           work, "--no-baked-captions", "--out", STAGED])


def main():
    stage()
    pal = binmock.load_palette(binmock.find_palette())
    badge = trefoil(13)
    font = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSansBold.ttf"), 7)
    lab = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSans.ttf"), 11)
    labb = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSansBold.ttf"), 13)
    small = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSans.ttf"), 10)

    gbu = binmock.load_cameo("precicon", pal)
    tac = binmock.load_cameo("paranukeicon", pal)

    def row(stem):
        """The four slots, as raw 62x46 tiles: neighbour, candidate, badged candidate, neighbour."""
        cand = Image.open(os.path.join(STAGED, stem + ".png")).convert("RGBA")
        return [
            (binmock.slot(gbu, None, None, font), "GBU-57"),
            (binmock.slot(cand, None, None, font), "Kinzhal"),
            (binmock.slot(cand, "50 KT", badge, font), "Kinzhal-N"),
            (binmock.slot(tac, "20 KT", badge, font), "Tactical"),
        ]

    W, H = binmock.SLOT_W, binmock.SLOT_H
    scales = [1, 2, 3]
    pad, gap = 14, 6

    # Measure first so the canvas is exact rather than cropped-to-fit.
    height = pad + 22
    for sc in scales:
        height += 20  # scale heading
        for _ in CANDIDATES:
            height += 16 + H * sc + (14 if sc == 1 else 0) + gap
        height += 10
    width = max(pad * 2 + 4 * W * 3 + 3 * gap + pad, 640)

    img = Image.new("RGBA", (width, height), BG)
    d = ImageDraw.Draw(img)

    d.text((pad, pad - 2), "Conventional Kh-47M2 Kinzhal - two candidate sources, in the row they sit in",
           font=labb, fill=(245, 245, 250, 255))
    y = pad + 22

    for sc in scales:
        d.text((pad, y), f"{sc}x" + ("   (actual size - this is the only honest row)" if sc == 1 else ""),
               font=small, fill=(150, 200, 150, 255) if sc == 1 else (150, 152, 160, 255))
        y += 20
        for stem, src, verdict in CANDIDATES:
            d.text((pad, y), src, font=lab, fill=(235, 235, 240, 255))
            d.text((pad + 250, y + 1), verdict, font=small, fill=(160, 162, 172, 255))
            y += 16
            x = pad
            for tile, name in row(stem):
                if sc > 1:
                    tile = tile.resize((W * sc, H * sc), Image.NEAREST)
                img.alpha_composite(tile, (x, y))
                if sc == 1:
                    d.text((x, y + H + 1), name, font=small, fill=(140, 142, 152, 255))
                x += W * sc + gap
            y += H * sc + (14 if sc == 1 else 0) + gap
        y += 10

    img.save(OUT)
    print("wrote", OUT, img.size)


if __name__ == "__main__":
    main()
