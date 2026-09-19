#!/usr/bin/env python3
"""Contact sheets of the shipped cameo art, decoded rather than described.

    python tools/cameo/contact_sheet.py --all     OUT.png   # every distinct icon art file, 4x
    python tools/cameo/contact_sheet.py --powers  OUT.png   # the support powers, as the bin draws them

WHY BOTH MODES EXIST
--------------------
`--all` answers "what does this cameo actually SAY", which nothing else here can: the baked
lettering is pixels, and rollout_survey.py can only tell you that lettering is PRESENT. Authoring
a caption table means reading the baked word off each cameo and shortening it, so the words have
to be legible. 4x nearest-neighbour is the smallest zoom at which a 5px cap height is.

`--powers` answers "is the art right for the weapon". It reads the roster from rules/powers.yaml
instead of carrying a hand copy the way binmock.py's does -- binmock's header warns that its copy
rots, and by 2026-09-19 it had: it still described cmissicon as a biohazard trefoil and abombfake
as a FAKE banner, both of which the art files had been replaced under on 2026-09-09.

Both modes resolve the sequence name to an art file through sequences-misc.yaml, so what is drawn
here is what the widget would load.
"""

import argparse
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import binmock        # noqa: E402
import caption_proof  # noqa: E402  -- slot(), so a power tile is laid out like the real bin
import ftprobe        # noqa: E402
import pixelfont      # noqa: E402

FONT_UI = os.path.join(ROOT, "engine/mods/common/FreeSans.ttf")
FONT_UI_B = os.path.join(ROOT, "engine/mods/common/FreeSansBold.ttf")


def powers_roster():
    """(actor, tooltip name, icon sequence, caption, badged) for every power.* buy proxy.

    Parsed out of rules/powers.yaml, which is where the Buildable: Icon/CameoCaption/CameoBadge
    that the PRODUCTION palette draws actually live. The support-power bin draws the same art from
    the matching SupportPower traits in player.yaml and ingame/nuclear-arsenal.yaml.
    """
    path = os.path.join(ROOT, "mods/ww3mod/rules/powers.yaml")
    out, cur = [], None
    for line in open(path, encoding="utf-8-sig"):
        line = line.rstrip("\n")
        if line.strip().startswith("#") or not line.strip():
            continue
        if not line.startswith("\t"):
            if cur:
                out.append(cur)
            name = line.strip().rstrip(":")
            cur = dict(actor=name, name="", icon=None, caption=None, badge=False) \
                if name.startswith("power.") else None
            continue
        if cur is None:
            continue
        t = line.strip()
        for key, field in (("Icon:", "icon"), ("CameoCaption:", "caption"), ("Name:", "name")):
            if t.startswith(key):
                cur[field] = t.split(":", 1)[1].strip()
        if t.startswith("CameoBadge:"):
            cur["badge"] = True
    if cur:
        out.append(cur)
    return [p for p in out if p["icon"]]


def distinct_art():
    """{art file: [actor, ...]} across every buildable actor, via rollout_survey's resolver."""
    import rollout_survey as rs
    rules = rs.merge(rs.walk(os.path.join(ROOT, "mods/ww3mod/rules")))
    seqs = rs.merge(rs.walk(os.path.join(ROOT, "mods/ww3mod/sequences")))
    low = {k.lower(): v for k, v in seqs.items()}

    def seq_art(image, icon):
        node, guard = low.get(image.lower()), 0
        while node is not None and guard < 8:
            guard += 1
            for key in node:
                m = re.match(rf"{re.escape(icon)}:\s*(\S+)", key)
                if m:
                    return m.group(1)
            parent = next((re.match(r"Inherits(?:@\w+)?:\s*(\S+)", k).group(1)
                           for k in node if re.match(r"Inherits(?:@\w+)?:\s*(\S+)", k)), None)
            node = low.get(parent.lower()) if parent else None
        return None

    out = {}
    for name in rules:
        if name.startswith("^") or name in ("Player", "World", "Defaults"):
            continue
        t = rs.resolve(name, rules)
        if "Buildable" not in t:
            continue
        icon = (rs.field(t.get("Buildable"), "Icon") or "icon").split()[0]
        image = rs.field(t.get("RenderSprites"), "Image") or name
        art = (seq_art(image, icon) or seq_art(name, icon) or "?").split("|")[-1]
        out.setdefault(art, []).append(name)
    return out


def grid(tiles, out, title, subtitle, zoom=4, cols=6):
    """tiles: [(PIL image or None, line1, line2)]."""
    tw, th = 64 * zoom, 48 * zoom
    lab, pad = 30, 8
    width = pad + cols * (tw + pad)
    rows = (len(tiles) + cols - 1) // cols
    head = 56
    height = head + rows * (th + lab + pad) + pad
    sheet = Image.new("RGBA", (width, height), (30, 32, 37, 255))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 8), title, font=ImageFont.truetype(FONT_UI_B, 16), fill=(245, 245, 250, 255))
    d.text((pad, 32), subtitle, font=ImageFont.truetype(FONT_UI, 11), fill=(160, 162, 172, 255))
    f1 = ImageFont.truetype(FONT_UI_B, 12)
    f2 = ImageFont.truetype(FONT_UI, 11)
    for i, (img, l1, l2) in enumerate(tiles):
        x = pad + (i % cols) * (tw + pad)
        y = head + (i // cols) * (th + lab + pad)
        d.rectangle([x, y, x + tw - 1, y + th - 1], fill=(12, 13, 16, 255))
        if img is not None:
            big = img.resize((img.width * zoom, img.height * zoom), Image.NEAREST)
            sheet.alpha_composite(big, (x + (tw - big.width) // 2, y + (th - big.height) // 2))
        else:
            d.text((x + 8, y + th // 2), "NOT IN THE REPO", font=f1, fill=(220, 90, 90, 255))
        d.text((x, y + th + 2), l1, font=f1, fill=(235, 235, 240, 255))
        d.text((x, y + th + 16), l2, font=f2, fill=(150, 152, 162, 255))
    sheet.save(out)
    print("wrote", os.path.relpath(out, ROOT), sheet.size)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--powers", action="store_true")
    ap.add_argument("out")
    args = ap.parse_args()

    pal = binmock.load_palette(binmock.find_palette())
    seq = binmock.icon_collection()

    def art_image(art):
        try:
            return binmock.load_cameo(art, pal)
        except Exception:
            return None

    if args.powers:
        ft = ftprobe.FreeType(pixelfont.TTF)
        from badge import trefoil
        nuke = trefoil(13)
        tiles = []
        for p in powers_roster():
            art = seq.get(p["icon"], p["icon"])
            cameo = art_image(art)
            tile = caption_proof.slot(cameo, p["caption"], ft, nuke if p["badge"] else None, True)
            tiles.append((tile, p["actor"].replace("power.", ""),
                          "%s  [%s]" % (p["name"][:30], art)))
        grid(tiles, args.out, "Every support power cameo, as the sidebar draws it",
             "Art decoded from the shipped SHP/PNG; caption and badge laid out by a port of "
             "CameoCaptionCache. 4x nearest-neighbour.", zoom=4, cols=5)
        return 0

    arts = distinct_art()
    tiles = [(art_image(a), a, "%d actor%s: %s" % (len(v), "" if len(v) == 1 else "s",
                                                   ", ".join(sorted(v))[:34]))
             for a, v in sorted(arts.items())]
    grid(tiles, args.out, "Every distinct cameo art file in the buildable roster",
         "%d files across %d actors, 4x nearest-neighbour. The baked lettering is what a caption "
         "would replace." % (len(arts), sum(len(v) for v in arts.values())))
    return 0


if __name__ == "__main__":
    sys.exit(main())
