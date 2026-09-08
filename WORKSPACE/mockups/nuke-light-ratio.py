#!/usr/bin/env python3
"""Render WORKSPACE/mockups/nuke-light-ratio.png -- the 0.3 kt and 6 Mt lights, before and after.

Everything in the four main panels is at ONE common scale (px per cell), so the sizes are directly
comparable across all of them. The 0.3 kt row also gets an 8x inset, because at the common scale its
old light is a 4-pixel dot -- which is the finding, but is hard to look at.

The glow is drawn with the engine's own falloff rather than something that merely looks similar:
TerrainLighting.cs:48-50, :255-257 give w(f) = (1/(1 + 24*(1-f)^2) - 1/25) * 25/24 with f = 1 - r/R.

PIL only, no numpy.  python WORKSPACE/mockups/nuke-light-ratio.py
"""

import os
from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'nuke-light-ratio.png')

# cells. cloud radius = ScalePercent * 310 / (100*24) / 2
CASES = [
    ('B61-12, 0.3 kt', 7.9, 2.2, 14.9),
    ('AtomicHighYield, 6 Mt', 65.7, 124.0, 124.0),
]

PANEL = 430
HALF_CELLS = 132.0                       # half-width of a panel in cells, common to all four
PX_PER_CELL = (PANEL / 2.0) / HALF_CELLS
INSET_ZOOM = 8.0

TERRAIN = (26, 32, 28)
GLOW_TINT = (255, 246, 224)
CLOUD_TINT = (206, 208, 205)
PEAK = 7.0
GAIN = 36.0                              # intensity 7.0 -> 252, just under saturation

K = 24.0
EDGE = 1.0 / (1.0 + K)
SCALE = 1.0 / (1.0 - EDGE)


def falloff(r, radius):
    """The engine's InverseSquare weight at distance r from a light of the given radius."""
    if r >= radius:
        return 0.0
    u = r / radius
    return (1.0 / (1.0 + K * u * u) - EDGE) * SCALE


def cloud_alpha(r, radius):
    """A soft-edged disc standing in for the nuke_large sprite: solid core, feathered rim."""
    if r >= radius:
        return 0.0
    u = r / radius
    return 1.0 if u < 0.72 else (1.0 - u) / 0.28


def render(cloud_cells, light_cells, px_per_cell, size):
    img = Image.new('RGB', (size, size), TERRAIN)
    px = img.load()
    cx = cy = size / 2.0
    light_px = light_cells * px_per_cell
    cloud_px = cloud_cells * px_per_cell
    for y in range(size):
        dy = y + 0.5 - cy
        for x in range(size):
            dx = x + 0.5 - cx
            r = (dx * dx + dy * dy) ** 0.5

            value = PEAK * falloff(r, light_px) * GAIN
            cr, cg, cb = TERRAIN
            if value > 0:
                cr += value * GLOW_TINT[0] / 255.0
                cg += value * GLOW_TINT[1] / 255.0
                cb += value * GLOW_TINT[2] / 255.0

            # The cloud sprite draws OVER the lit ground. nuke_large is BlendMode: Additive, so it
            # does not occlude the glow -- it swamps it, which reads the same way to a player and is
            # why a light smaller than its own cloud cannot be seen at any brightness.
            a = cloud_alpha(r, cloud_px)
            if a > 0:
                cr += a * CLOUD_TINT[0] * 0.86
                cg += a * CLOUD_TINT[1] * 0.86
                cb += a * CLOUD_TINT[2] * 0.86

            px[x, y] = (min(255, int(cr)), min(255, int(cg)), min(255, int(cb)))
    return img


def font(sz, bold=False):
    for name in (('arialbd.ttf', 'seguisb.ttf') if bold else ('arial.ttf', 'segoeui.ttf')):
        try:
            return ImageFont.truetype(name, sz)
        except OSError:
            continue
    return ImageFont.load_default()


def main():
    pad, top, gap, label_h = 26, 108, 26, 76
    inset = 168
    W = max(pad * 2 + PANEL * 2 + gap, 1020)
    H = top + (PANEL + label_h) * 2 + gap + pad

    canvas = Image.new('RGB', (W, H), (17, 19, 21))
    d = ImageDraw.Draw(canvas)
    f_title, f_head, f_body, f_small = font(27, True), font(18, True), font(15), font(13)

    d.text((pad, 22), 'Nuclear light: reach against the weapon\'s own cloud', font=f_title, fill=(240, 240, 240))
    subtitle = [
        'All four panels share one scale (%.2f px per cell). Pale disc = the mushroom cloud sprite. '
        'Warm glow = the light, on the engine\'s own falloff.' % PX_PER_CELL,
        'Peak intensity is 7.0 in every panel, before and after — nothing here was brightened. A light '
        'smaller than its own cloud is invisible at any brightness.',
    ]
    for i, line in enumerate(subtitle):
        d.text((pad, 58 + i * 20), line, font=f_small, fill=(150, 152, 156))

    for row, (name, cloud, before, after) in enumerate(CASES):
        y = top + row * (PANEL + label_h + gap)
        for col, (tag, light) in enumerate((('BEFORE', before), ('AFTER', after))):
            x = pad + col * (PANEL + gap)
            canvas.paste(render(cloud, light, PX_PER_CELL, PANEL), (x, y))
            d.rectangle([x, y, x + PANEL - 1, y + PANEL - 1], outline=(58, 62, 66))

            ratio = light / cloud
            unchanged = abs(after - before) < 0.05
            head = '%s — %s' % (name, tag)
            if unchanged:
                head = '%s — the reference, unchanged' % name
            d.text((x, y + PANEL + 9), head, font=f_head,
                   fill=(236, 236, 236) if (col or unchanged) else (176, 178, 182))
            d.text((x, y + PANEL + 33),
                   'cloud radius %.1f cells   ·   light %.1f cells   ·   ratio %.2f×' % (cloud, light, ratio),
                   font=f_body, fill=(196, 152, 96) if ratio < 1 else (150, 196, 150))
            d.text((x, y + PANEL + 53),
                   'entire lit area sits under the sprite' if ratio < 1
                   else 'lit ground reaches %.1f cells past the cloud edge' % (light - cloud),
                   font=f_small, fill=(196, 152, 96) if ratio < 1 else (140, 148, 142))

            # 8x inset for the small weapon, whose real extents are a few pixels at the common scale.
            if cloud < 20:
                ins = render(cloud, light, PX_PER_CELL * INSET_ZOOM, inset)
                ix, iy = x + PANEL - inset - 12, y + 12
                canvas.paste(ins, (ix, iy))
                d.rectangle([ix, iy, ix + inset - 1, iy + inset - 1], outline=(120, 124, 128))
                d.text((ix + 6, iy + 5), '%.0f× zoom' % INSET_ZOOM, font=f_small, fill=(210, 212, 214))

    canvas.save(OUT)
    print('wrote ' + OUT)


if __name__ == '__main__':
    main()
