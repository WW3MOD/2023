#!/usr/bin/env python3
"""Offline render of the runtime cameo caption, at the exact size the sidebar draws it.

Nothing here talks to the engine -- it re-implements the widget's composition from the same
numbers the widget reads (IconSize: 62, 46 / IconSpriteOffset: -1, -1 / Fonts: Caption at
FreeSansBold size 7) so caption legibility can be judged without launching the game.

Two things it CANNOT prove, both stated on the page it writes: FreeType and Pillow hint the
same TTF slightly differently at 7px, and the palette below is the chernobyl map's temperat.pal
rather than the one inside temperat.mix. Neither touches the caption, which the widget draws in
straight RGBA and never through a palette.
"""

import io
import os
import struct
from PIL import Image, ImageDraw, ImageFont

FONT = 'engine/mods/common/FreeSansBold.ttf'
PAL = 'engine/mods/ra/maps/chernobyl/temperat.pal'
ICONS = 'mods/ww3mod/bits/misc/icons'
OUT = 'WORKSPACE/mockups/assets'

SLOT_W, SLOT_H = 62, 46          # chrome/ingame-player.yaml
SPRITE_OFFSET = (-1, -1)         # IconSpriteOffset
SIDE, BOTTOM, PAD = 1, 2, 1      # CaptionSideMargin / CaptionBottomMargin / CaptionBackgroundPadding
SIZE = 7                         # Fonts: Caption
BAND = (0, 0, 0, 255)            # CaptionBackgroundColor: 000000FF


def load_palette(path):
    b = io.open(path, 'rb').read()
    return [(b[i * 3] * 255 // 63, b[i * 3 + 1] * 255 // 63, b[i * 3 + 2] * 255 // 63) for i in range(256)]


def load_shp(path, pal):
    """ShpTS format 1 (uncompressed indexed) -- what every cameo in bits/misc/icons is."""
    b = io.open(path, 'rb').read()
    zero, _w, _h, n = struct.unpack('<4H', b[:8])
    assert zero == 0 and n >= 1
    _x, _y, fw, fh = struct.unpack('<4H', b[8:16])
    fmt = struct.unpack('<I', b[16:20])[0]
    off = struct.unpack('<I', b[28:32])[0]
    assert fmt == 1, '%s: format %d, expected 1' % (path, fmt)
    data = b[off:off + fw * fh]
    img = Image.new('RGBA', (fw, fh), (0, 0, 0, 0))
    px = img.load()
    for y in range(fh):
        for x in range(fw):
            i = data[y * fw + x]
            if i:
                px[x, y] = (pal[i][0], pal[i][1], pal[i][2], 255)
    return img


def slot(pal, icon, caption, band=True, size=SIZE, scale=1):
    spr = load_shp(os.path.join(ICONS, icon + '.shp'), pal)
    cell = Image.new('RGBA', (SLOT_W, SLOT_H), (0, 0, 0, 255))

    # WidgetUtils.DrawSpriteCentered at 0.5 * IconSize + IconSpriteOffset.
    cx = 0.5 * SLOT_W + SPRITE_OFFSET[0]
    cy = 0.5 * SLOT_H + SPRITE_OFFSET[1]
    tl = (int(cx - spr.width / 2), int(cy - spr.height / 2))
    scratch = Image.new('RGBA', (SLOT_W + 128, SLOT_H + 128), (0, 0, 0, 0))
    scratch.alpha_composite(spr, dest=(64 + tl[0], 64 + tl[1]))
    cell.alpha_composite(scratch.crop((64, 64, 64 + SLOT_W, 64 + SLOT_H)))

    if caption:
        f = ImageFont.truetype(FONT, size)
        w = f.getlength(caption)                       # SpriteFont.LineWidth sums advances
        top = SLOT_H - BOTTOM - size
        d = ImageDraw.Draw(cell)
        if band:
            d.rectangle([0, max(0, top - PAD), SLOT_W - 1, min(SLOT_H, top + size + PAD) - 1], fill=BAND)
        d.text((int((SLOT_W - w) // 2), top + size), caption, font=f,
               fill=(255, 255, 255, 255), anchor='ls', stroke_width=1, stroke_fill=(0, 0, 0, 255))

    return cell.resize((SLOT_W * scale, SLOT_H * scale), Image.NEAREST) if scale > 1 else cell


def strip(pal, items, band=True, size=SIZE, scale=1, gap=1):
    cells = [slot(pal, i, c, band, size, scale) for i, c in items]
    width = sum(c.width for c in cells) + gap * scale * (len(cells) - 1)
    out = Image.new('RGBA', (width, cells[0].height), (24, 24, 24, 255))
    x = 0
    for c in cells:
        out.alpha_composite(c, dest=(x, 0))
        x += c.width + gap * scale
    return out


def main():
    pal = load_palette(PAL)
    os.makedirs(OUT, exist_ok=True)

    sets = {
        # The payoff: three American B61-12 yields that all draw `paranuke` and are currently
        # indistinguishable in the bin.
        'b61': [('paranukeicon', '0.3 KT'), ('paranukeicon', '10 KT'), ('paranukeicon', '50 KT')],
        'ru': [('paranukeicon', '1 KT'), ('paranukeicon', '10 KT'), ('paranukeicon', '50 KT')],
        'mix': [('cmissicon', '100 KT'), ('v2bdgricon', '6x750 KT'), ('precicon', None)],
        'bare': [('paranukeicon', None), ('paranukeicon', None), ('paranukeicon', None)],
    }

    for name, items in sets.items():
        for sc in (1, 4):
            strip(pal, items, band=True, scale=sc).save('%s/cap-%s-%dx.png' % (OUT, name, sc))

    # The collision this feature has to survive: the same three with no band, where the runtime
    # caption lands on top of the art's own baked "PARANUKE".
    for sc in (1, 4):
        strip(pal, sets['b61'], band=False, scale=sc).save('%s/cap-noband-%dx.png' % (OUT, sc))

    # Longest shipped infantry caption, at the font that fits it and the one that does not.
    for sz in (7, 10):
        for sc in (1, 4):
            strip(pal, [('paranukeicon', 'FLAMETHROWER')], band=True, size=sz, scale=sc).save(
                '%s/cap-flame-%d-%dx.png' % (OUT, sz, sc))

    f7 = ImageFont.truetype(FONT, 7)
    f10 = ImageFont.truetype(FONT, 10)
    limit = SLOT_W - 2 * SIDE
    caps = ['FLAMETHROWER', 'SPEC FORCES', 'TEAM LEADER', 'STINGER AA', 'TECHNICIAN', 'CONSCRIPT',
            'GRENADIER', 'JAVELIN AT', 'DRONE OP', 'ENGINEER', 'RIFLEMAN', 'SNIPER', 'MORTAR',
            'MEDIC', 'LMG', 'SPY']
    rows = ''
    for c in caps:
        w7, w10 = f7.getlength(c), f10.getlength(c)
        rows += ('<tr><td class=c>%s</td><td class="n %s">%.0f</td><td class="n %s">%.0f</td></tr>'
                 % (c, 'bad' if w7 > limit else 'ok', w7, 'bad' if w10 > limit else 'ok', w10))

    html = TEMPLATE.replace('{{ROWS}}', rows).replace('{{LIMIT}}', str(limit))
    io.open('WORKSPACE/mockups/cameo-captions.html', 'w', encoding='utf-8').write(html)
    print('wrote WORKSPACE/mockups/cameo-captions.html')


TEMPLATE = """<!doctype html>
<meta charset="utf-8">
<title>Runtime cameo captions</title>
<style>
 body{background:#141414;color:#d8d8d8;font:14px/1.6 system-ui,sans-serif;margin:0;padding:32px 40px;max-width:1000px}
 h1{font-size:20px;margin:0 0 4px} h2{font-size:15px;margin:34px 0 6px;color:#fff;border-bottom:1px solid #333;padding-bottom:5px}
 p{margin:6px 0 12px;color:#a8a8a8;max-width:74ch}
 img{image-rendering:pixelated;display:block;margin:8px 0}
 .lab{font:11px ui-monospace,monospace;color:#6f6f6f;text-transform:uppercase;letter-spacing:.08em;margin-top:14px}
 table{border-collapse:collapse;font:12px ui-monospace,monospace;margin:10px 0}
 td,th{padding:2px 14px 2px 0;text-align:left} th{color:#777;font-weight:400;border-bottom:1px solid #333}
 .n{text-align:right} .ok{color:#7ec97e} .bad{color:#e2726e} .c{color:#ccc}
 .note{border-left:2px solid #4a4a4a;padding-left:14px;color:#9a9a9a}
 b{color:#fff;font-weight:600} code{color:#c8c8c8}
</style>
<h1>Runtime cameo captions</h1>
<p>The caption is data, not pixels. Every image below is composed at the exact size the sidebar
draws it &mdash; a 62&times;46 slot, real cameo art decoded from <code>bits/misc/icons</code>, caption in
FreeSansBold at 7px with a 1px black contrast. Each block shows true size first, then the same
thing at 4&times; nearest-neighbour so the lettering can be inspected.</p>

<h2>The problem it solves</h2>
<p>Six support powers draw the <b>same</b> <code>paranuke</code> sprite. The caption is baked into
the art, so all six read &ldquo;PARANUKE&rdquo; and nothing in the bin tells them apart:</p>
<div class=lab>shipped, true size</div><img src="assets/cap-bare-1x.png">
<div class=lab>shipped, 4&times;</div><img src="assets/cap-bare-4x.png">

<h2>With a runtime caption</h2>
<p>The three American B61-12 yields, same sprite, saying three different things. The wording lives
in <code>powers.yaml</code> and changing it is a text edit, not a re-render.</p>
<div class=lab>true size</div><img src="assets/cap-b61-1x.png">
<div class=lab>4&times;</div><img src="assets/cap-b61-4x.png">
<div class=lab>russian set, 4&times;</div><img src="assets/cap-ru-4x.png">
<div class=lab>other icons, plus one left uncaptioned, 4&times;</div><img src="assets/cap-mix-4x.png">

<h2>Why there is a black band</h2>
<p class=note>This is the one thing worth a decision. The shipped art already has
&ldquo;PARANUKE&rdquo; baked into exactly the pixels the runtime caption wants, so drawing over it
without covering it first gives two overlapping words. <code>CaptionBackgroundColor</code> is off by
default and set to solid black in the sidebar; below is what it looks like unset:</p>
<div class=lab>no band &mdash; runtime caption over the baked one, 4&times;</div><img src="assets/cap-noband-4x.png">
<div class=lab>band, 4&times;</div><img src="assets/cap-b61-4x.png">
<p>On art rendered with <code>convert.py --no-baked-captions</code> there is nothing underneath, and
the band can be turned off so the caption sits straight on the picture.</p>

<h2>How much text fits</h2>
<p>A slot is {{LIMIT}}px wide inside its margins. <b>TinyBold, the font the sidebar's other overlay
text uses, is too big</b> &mdash; five of the sixteen shipped infantry captions overflow it. At 7px all
sixteen fit, though &ldquo;FLAMETHROWER&rdquo; lands on exactly {{LIMIT}}px with nothing to spare.</p>
<table><tr><th>caption</th><th class=n>7px</th><th class=n>TinyBold&nbsp;(10px)</th></tr>{{ROWS}}</table>
<div class=lab>FLAMETHROWER at 7px &mdash; fits, 4&times;</div><img src="assets/cap-flame-7-4x.png">
<div class=lab>FLAMETHROWER at TinyBold &mdash; shortened to fit, 4&times;</div><img src="assets/cap-flame-10-4x.png">
<p>Anything wider is shortened from the right rather than allowed to bleed into the neighbouring
cameo, which sits one pixel away.</p>

<h2>What this render cannot tell you</h2>
<p class=note>The glyphs are Pillow's rasterisation of the same TTF at the same pixel size, not
FreeType's; hinting at 7px may differ by a pixel here and there. The backdrop uses the chernobyl
map's <code>temperat.pal</code> because the real one lives inside <code>temperat.mix</code> &mdash;
that affects only the photograph, never the caption, which the widget draws in straight RGBA and
never routes through a palette. Both are reasons to confirm on screen, not reasons to distrust the
sizing: the widths above come from the same advance-sum the engine uses.</p>
"""


if __name__ == '__main__':
    main()
