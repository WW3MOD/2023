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

# Hand copy of the two palettes in chrome/ingame-player.yaml. Two rules ride on these numbers,
# both enforced against the real chrome by CameoCaptionBandTest and by nothing at all here:
#
#   BOTTOM_MARGIN is 0 because the caption is BOTTOM-ANCHORED. The generated text's last ink row
#   is SLOT_H - BOTTOM_MARGIN - 1, and it has to equal the row the baked lettering ends on, which
#   is slot row 45 on every shipped cameo. At 2 -- what this shipped with -- the runtime text
#   floated two rows above every baked caption beside it.
#
#   BAND_PAD must stay >= BOTTOM_MARGIN or the band stops short of the baked caption it is
#   covering. That is free at margin 0 and stops being free if anyone raises the margin.
SIDE_MARGIN, BOTTOM_MARGIN, BAND_PAD, BADGE_GAP = 1, 0, 2, 1
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


def icon_collection():
    """`icon:` from sequences-misc.yaml as {sequence name: art file}.

    Read rather than hand-copied, because a stale copy here draws a picture that is wrong and
    looks fine. Only the flat `key: file` children are taken; anything with its own sub-block
    (none today) is skipped rather than guessed at.
    """
    path = os.path.join(ROOT, "mods/ww3mod/sequences/sequences-misc.yaml")
    out, inside = {}, False
    for line in open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if not line.strip():
            inside = False
            continue
        if not line.startswith("\t"):
            inside = line.strip() == "icon:"
            continue
        if not inside or line.startswith("\t\t") or line.lstrip().startswith("#"):
            continue
        key, _, value = line.strip().partition(":")
        if value.strip():
            out[key.strip()] = value.strip()
    return out


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
        # `top` is the line box, not the baseline. SpriteFont.DrawText adds `size` to reach the
        # baseline (SpriteFont.cs:99), so a cap sits on rows top+2..top+6 at size 7 -- and Pillow's
        # default "la" anchor puts the same glyph on the same rows for the same y. So `top` is passed
        # through unchanged. It used to be `top - 1` here, which drew this mockup's captions one row
        # above where the engine draws them.
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                if dx or dy:
                    d.text((x + dx, top + dy), text, font=font, fill=(0, 0, 0, 255))
        d.text((x, top), text, font=font, fill=(255, 255, 255, 255))
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

    # SEQUENCE NAME -> ART FILE, read from the `icon:` block of sequences-misc.yaml rather than
    # hand-copied. It used to be a three-entry literal here, which is exactly the kind of copy the
    # header warns rots: on 2026-09-09 six new keys were added at once and a literal would have
    # drawn the wrong picture for four powers while looking like it worked.
    art = icon_collection()

    # Every power, grouped the way the roster actually divides -- five America, five Russia, five
    # event tier that no faction can reach. A None group label starts a new band on the sheet.
    # (icon, caption, badged, label)
    roster = [
        ("#", "AMERICA  (powers.america)", None, None),
        ("precicon", "PRECISION STR.", False, "GBU-57 conv"),
        ("paranuke", "0.3 KT", True, "B61 0.3kt"),
        ("paranuke", "10 KT", True, "B61 10kt"),
        ("paranuke", "50 KT", True, "B61 50kt"),
        ("cmissicon", "100 KT", True, "W76-1"),
        ("#", "RUSSIA  (powers.russia)", None, None),
        ("kinzhalicon", None, False, "Kinzhal conv"),
        ("ru9m729", "1 KT", True, "9M729"),
        ("ruiskander", "10 KT", True, "Iskander-M"),
        ("rukinzhaln", "50 KT", True, "Kinzhal-N"),
        ("rukalibr", "100 KT", True, "Kalibr"),
        ("#", "EVENT TIER  (powers.event -- sandbox only)", None, None),
        ("tacnuke", "20 KT", True, "Tactical"),
        ("v2bdgricon", "6x750 KT", True, "Sarmat"),
        ("abombfake", "1.2 MT", True, "B83-1"),
        ("highyieldnuke", "6 MT", True, "Strategic"),
        ("abomb", "50 MT", True, "TSAR 50MT"),
    ]

    pad, cols = 6, 5
    bands = sum(1 for r in roster if r[0] == "#")
    tiles = len(roster) - bands
    rows = (tiles + cols - 1) // cols
    w = pad + cols * (SLOT_W + pad)
    h = pad + rows * (SLOT_H + 12 + pad) + bands * 16
    sheet = Image.new("RGBA", (w, h), (46, 48, 54, 255))
    d = ImageDraw.Draw(sheet)
    label = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSans.ttf"), 9)
    head = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSansBold.ttf"), 10)
    col, y = 0, pad
    for icon, cap, badged, name in roster:
        if icon == "#":
            if col:
                col, y = 0, y + SLOT_H + 12 + pad
            d.text((pad, y + 2), cap, font=head, fill=(255, 210, 120, 255))
            y += 16
            continue

        cx = pad + col * (SLOT_W + pad)
        sheet.alpha_composite(slot(load_cameo(art.get(icon, icon), pal), cap, badge if badged else None, font), (cx, y))
        d.text((cx, y + SLOT_H + 1), name, font=label, fill=(220, 220, 225, 255))
        col += 1
        if col == cols:
            col, y = 0, y + SLOT_H + 12 + pad
    if col:
        y += SLOT_H + 12 + pad
    sheet = sheet.crop((0, 0, w, min(h, y + pad)))
    w, h = sheet.size

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
