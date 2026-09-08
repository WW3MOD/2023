"""Render the sidebar's support-power bin exactly as the widget lays it out, offline.

Decodes the shipped cameos (TS SHP, indexed, temperat.pal) and the PNG-under-.shp ones,
then reproduces CameoCaptionCache's arithmetic in Python and draws the caption band, the
caption and the badge with the real 7px FreeSansBold. No renderer, no game launch.

    python3 tools/cameo/binmock.py   ->  tools/cameo/work/bin-1x.png, bin-3x.png

This is a MOCKUP, not a screenshot. It is only as true as the arithmetic in slot(), which
is a hand port of CameoCaptionCache.Build -- if that changes, this drifts and will keep
producing a confident wrong picture. Its job is to answer "do the badge and the yield
fight for the same pixels" and "is the trefoil legible on THIS cameo" without launching
the game, which several workflows here forbid. It is not a substitute for seeing the real
sidebar; it is what you use when you cannot.

The roster at the bottom is a hand copy of the Icon/CameoCaption/CameoBadge values in
rules/powers.yaml. Nothing checks that it is still in step with them.
"""
import io
import os
import struct
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ICONS = os.path.join(ROOT, "mods/ww3mod/bits/misc/icons")

SLOT_W, SLOT_H = 62, 46

# Hand copy of the two palettes in chrome/ingame-player.yaml. BAND_PAD must stay >= BOTTOM_MARGIN
# or the band stops short of the baked caption underneath it -- which is exactly the defect this
# script found on 2026-09-08, when both were at the engine default of 1 against a margin of 2.
# CameoCaptionBandTest enforces that relationship on the real chrome; nothing enforces it here.
SIDE_MARGIN, BOTTOM_MARGIN, BAND_PAD, BADGE_GAP = 1, 2, 2, 1
SPRITE_OFFSET = (-1, -1)  # IconSpriteOffset in chrome/ingame-player.yaml
BAND = (0, 0, 0, 255)


def find_palette():
    for base, _, files in os.walk(os.path.join(ROOT, "mods")):
        for f in files:
            if f.lower() == "temperat.pal":
                return os.path.join(base, f)
    for base, _, files in os.walk(os.path.join(ROOT, "engine", "mods")):
        for f in files:
            if f.lower() == "temperat.pal":
                return os.path.join(base, f)
    return None


def load_palette(path):
    raw = open(path, "rb").read()
    # 6-bit VGA palette, 256 RGB triples.
    return [((raw[i * 3] * 255) // 63, (raw[i * 3 + 1] * 255) // 63, (raw[i * 3 + 2] * 255) // 63)
            for i in range(256)]


def decode_shp_ts(path, pal):
    """One frame of a Tiberian Sun SHP, as RGBA. Index 0 transparent, 3 is the chrome shadow."""
    d = open(path, "rb").read()
    _, w, h, count = struct.unpack_from("<HHHH", d, 0)
    if count < 1:
        return None
    x, y, fw, fh = struct.unpack_from("<HHHH", d, 8)
    fmt = d[16]
    off = struct.unpack_from("<I", d, 8 + 20)[0]
    dw = fw + (fw % 2)
    dh = fh + (fh % 2)
    data = bytearray(dw * dh)
    p = off
    if fmt == 3:
        for j in range(fh):
            length = struct.unpack_from("<H", d, p)[0] - 2
            p += 2
            src, dst = d[p:p + length], dw * j
            p += length
            i = 0
            while i < len(src):
                v = src[i]
                i += 1
                if v != 0:
                    data[dst] = v
                    dst += 1
                else:
                    dst += src[i]
                    i += 1
    else:
        length = fw
        if fmt == 2:
            length = struct.unpack_from("<H", d, p)[0] - 2
            p += 2
        for j in range(fh):
            data[dw * j:dw * j + length] = d[p:p + length]
            p += length

    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = img.load()
    for j in range(dh):
        for i in range(dw):
            v = data[j * dw + i]
            if v in (0, 3):
                continue
            tx, ty = x + i, y + j
            if 0 <= tx < w and 0 <= ty < h:
                px[tx, ty] = pal[v] + (255,)
    return img


def load_cameo(name, pal):
    path = os.path.join(ICONS, name + ".shp")
    head = open(path, "rb").read(8)
    if head.startswith(b"\x89PNG"):
        return Image.open(path).convert("RGBA")
    return decode_shp_ts(path, pal)


def fit(text, font, maxw):
    def width(t):
        return sum(font.getlength(c) for c in t)

    text = text.strip()
    if width(text) <= maxw:
        return text, width(text)
    for n in range(len(text) - 1, 0, -1):
        c = text[:n].rstrip()
        if c and width(c) <= maxw:
            return c, width(c)
    return None, 0


def slot(cameo, caption, badge, font, line_h=7):
    """One 62x46 icon slot, drawn the way SupportPowersWidget draws it."""
    img = Image.new("RGBA", (SLOT_W, SLOT_H), (24, 26, 30, 255))
    if cameo is not None:
        img.alpha_composite(cameo, SPRITE_OFFSET)
    d = ImageDraw.Draw(img)

    reserved = badge.size[0] + BADGE_GAP if badge else 0
    text, tw = (None, 0)
    if caption:
        text, tw = fit(caption, font, SLOT_W - 2 * SIDE_MARGIN - reserved)

    bottom = SLOT_H - BOTTOM_MARGIN
    if text:
        top = bottom - line_h
        d.rectangle([0, max(0, top - BAND_PAD), SLOT_W - 1, min(SLOT_H, top + line_h + BAND_PAD) - 1], fill=BAND)
        x = (SLOT_W - reserved - int(tw)) // 2
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            d.text((x + dx, top + dy - 1), text, font=font, fill=(0, 0, 0, 255))
        d.text((x, top - 1), text, font=font, fill=(255, 255, 255, 255))
    if badge:
        img.alpha_composite(badge, (SLOT_W - SIDE_MARGIN - badge.size[0], bottom - badge.size[1]))
    return img


def main():
    pal_path = find_palette()
    if not pal_path:
        sys.exit("temperat.pal not found")
    pal = load_palette(pal_path)
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from badge import trefoil
    badge = trefoil(13)
    font = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSansBold.ttf"), 7)

    # Every power, in SupportPowerPaletteOrder-ish order: (icon, caption, badged, label)
    # Sequence name -> art file, from the `icon:` block of sequences-misc.yaml.
    art = {"abomb": "atomicon", "abombfake": "atomfakeicon", "paranuke": "paranukeicon"}
    roster = [
        ("precicon", None, False, "GBU-57 conv"),
        ("kinzhalicon", None, False, "Kinzhal conv"),
        ("paranuke", "0.3 KT", True, "B61 0.3kt"),
        ("paranuke", "10 KT", True, "B61 10kt"),
        ("paranuke", "50 KT", True, "B61 50kt"),
        ("cmissicon", "100 KT", True, "W76-1 *"),
        ("paranuke", "1 KT", True, "9M729"),
        ("v2bdgricon", "10 KT", True, "Iskander"),
        ("paranuke", "50 KT", True, "Kinzhal-N"),
        ("paranuke", "100 KT", True, "Kalibr"),
        ("paranuke", "20 KT", True, "Tactical"),
        ("v2bdgricon", "6x750 KT", True, "Sarmat"),
        ("abombfake", "1.2 MT", True, "B83-1 *"),
        ("v2bdgricon", "6 MT", True, "Strategic"),
        ("abomb", "50 MT", True, "TSAR 50MT"),
    ]

    pad, cols = 6, 5
    rows = (len(roster) + cols - 1) // cols
    w = pad + cols * (SLOT_W + pad)
    h = pad + rows * (SLOT_H + 12 + pad)
    sheet = Image.new("RGBA", (w, h), (46, 48, 54, 255))
    d = ImageDraw.Draw(sheet)
    label = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSans.ttf"), 9)
    for i, (icon, cap, badged, name) in enumerate(roster):
        cx = pad + (i % cols) * (SLOT_W + pad)
        cy = pad + (i // cols) * (SLOT_H + 12 + pad)
        sheet.alpha_composite(slot(load_cameo(art.get(icon, icon), pal), cap, badge if badged else None, font), (cx, cy))
        d.text((cx, cy + SLOT_H + 1), name, font=label, fill=(220, 220, 225, 255))

    sheet.save(os.path.join(ROOT, "tools/cameo/work/bin-1x.png"))
    big = sheet.resize((w * 3, h * 3), Image.NEAREST)
    big.save(os.path.join(ROOT, "tools/cameo/work/bin-3x.png"))
    print("wrote tools/cameo/work/bin-1x.png and bin-3x.png")

    # A single titled sheet, 3x over actual size, for staging under WORKSPACE/mockups/ where
    # someone can look at it without knowing this script exists. work/ is gitignored.
    out = sys.argv[1] if len(sys.argv) > 1 else None
    if out:
        title = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSansBold.ttf"), 13)
        note = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSans.ttf"), 10)
        top = 26
        combined = Image.new("RGBA", (max(big.width, w) + 24, top + big.height + 26 + h + 14), (32, 34, 39, 255))
        cd = ImageDraw.Draw(combined)
        cd.text((12, 6), "Support power bin with the nuclear badge - every cameo, 3x",
                font=title, fill=(245, 245, 250, 255))
        combined.alpha_composite(big, (12, top))
        cd.text((12, top + big.height + 8),
                "Actual size. * = cameo art that is wrong for its weapon and predates this branch "
                "(W76-1 is a biohazard trefoil, B83-1 has a FAKE banner).",
                font=note, fill=(160, 162, 172, 255))
        combined.alpha_composite(sheet, (12, top + big.height + 24))
        combined.save(out)
        print("wrote", out, combined.size)


if __name__ == "__main__":
    main()
