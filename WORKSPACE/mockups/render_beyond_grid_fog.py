"""WW3MOD: the fog overlay beyond the cell grid, before and after wt/beyond-map-fog.

Base: main @ 63ad2182 (includes the wt/light-bounds ring fix).

Every number is the shipped arithmetic:
  * map fog        = 1 - ShroudRenderer.CompositeTransmission (ShroudRenderer.cs:319), FogDarkness 1.4
  * beyond-grid    = WorldRenderer.DrawBeyondMapActorFog's local curve, as it stood before this branch
  * light          = TerrainLighting.ApplyFalloff InverseSquare (TerrainLighting.cs:255),
                     Atomic Warhead@FireballLight keyframe 10 (weapons-superweapons.yaml:226-228)

What this shows, and the second panel matters as much as the first:
  1. the overlay beyond the grid was ~4.35x too opaque at visibility 1, so any sprite spilling
     past the map edge was drawn far darker than the same sprite one cell further in;
  2. fixing that does NOT make the beyond-grid region match the map during a nuke, because the
     fog-piercing LIGHT is grid-bounded by design and cannot cross without lighting terrain that
     DrawBeyondMapFog deliberately paints opaque black.
"""
from PIL import Image, ImageDraw, ImageFont

FOG_PALETTE_ALPHA = 160.0 / 255.0
FOG_DARKNESS = 1.4
VISION_LAYERS = 11
INV_SQ_K = 24.0
INV_SQ_EDGE = 1.0 / (1.0 + INV_SQ_K)
INV_SQ_SCALE = 1.0 / (1.0 - INV_SQ_EDGE)

TILE = 1024
# Atomic's Warhead@FireballLight keyframe 10 (Times 107): radius 35c303, intensity 1.09, tint
# FFC164. Chosen over the t=0 peak deliberately -- at peak intensity 7.0 every cell inside the grid
# near the cloud clips to white, and a saturated row either side of a boundary teaches nothing. 107
# ticks after detonation is inside the capture window demo-nuke-edge-band asks for, so this is the
# frame the screenshot will actually show.
RADIUS = 35 * TILE + 303
INTENSITY = 1.09
TINT = (0xFF / 255.0, 0xC1 / 255.0, 0x64 / 255.0)
TERRAIN = (0.20, 0.23, 0.18)


def layer_alpha(i, fd):
    a = 1.0
    if i > 1:
        a -= (i - 1) * (1.0 / 12)
    if i > 0:
        a = min(a * fd / 3.0, 1.0)
    return a


def transmission(vis, fd=FOG_DARKNESS):
    """ShroudRenderer.CompositeTransmission -- what the fog over the MAP lets through."""
    if vis <= 0:
        return 0.0
    t = 1.0
    for layer in range(vis, VISION_LAYERS - 1):
        t *= 1.0 - FOG_PALETTE_ALPHA * layer_alpha(layer, fd)
    return t


def legacy_transmission(vis, fd=FOG_DARKNESS):
    """The hand-rolled copy: the same product WITHOUT the fog palette's own alpha."""
    if vis <= 0:
        return 0.0
    if vis >= VISION_LAYERS - 1:
        return 1.0
    t = 1.0
    for layer in range(vis, VISION_LAYERS - 1):
        t *= 1.0 - layer_alpha(layer, fd)
    return t


def apply_falloff(f):
    u = 1.0 - f
    return (1.0 / (1.0 + INV_SQ_K * u * u) - INV_SQ_EDGE) * INV_SQ_SCALE


def light_at(du, dv):
    d = ((du * TILE) ** 2 + (dv * TILE) ** 2) ** 0.5
    if d > RADIUS:
        return (0.0, 0.0, 0.0)
    f = apply_falloff((RADIUS - d) * 1.0 / RADIUS)
    return tuple(f * INTENSITY * c for c in TINT)


def cloud_sprite(du, dv):
    """A stand-in mushroom cloud: soft radial falloff, warm core. Its own pixel value, unfogged."""
    d = (du * du + dv * dv) ** 0.5
    r = 11.0
    if d > r:
        return (0.0, 0.0, 0.0)
    k = 1.0 - (d / r) ** 1.6
    return (min(1.0, 1.25 * k), min(1.0, 1.05 * k), min(1.0, 0.72 * k))


def screen(u, v, cu, cv, vis, fixed_curve):
    """
    Final pixel. Inside the grid: terrain and sprite are fogged by ShroudRenderer, and the
    fog-piercing light adds back what the fog took (additively, over both).
    Beyond the grid: DrawBeyondMapFog has painted opaque black, the sprite is drawn on top, and
    DrawBeyondMapActorFog attenuates it. No ground, and therefore no restoration.
    """
    sprite = cloud_sprite(u - cu, v - cv)
    light = light_at(u - cu, v - cv)
    t = transmission(vis)

    if v >= 0:                     # inside the cell grid
        lost = 1.0 - t
        return [(TERRAIN[i] + sprite[i] + light[i]) * t + lost * light[i] for i in range(3)]

    keep = t if fixed_curve else legacy_transmission(vis)
    return [sprite[i] * keep for i in range(3)]


def draw_panel(img, ox, oy, cols, rows, cell, cu, cv, vis, fixed_curve):
    px = img.load()
    for vi, v in enumerate(rows):
        for ui, u in enumerate(cols):
            c = screen(u, v, cu, cv, vis, fixed_curve)
            rgb = tuple(min(255, max(0, int(ci * 255 + 0.5))) for ci in c)
            x0, y0 = ox + ui * cell, oy + vi * cell
            for y in range(y0, y0 + cell):
                for x in range(x0, x0 + cell):
                    px[x, y] = rgb


def main():
    cols = list(range(22, 58))
    rows = list(range(-13, 11))
    cell = 15
    cu, cv = 40, 4          # cloud centred four cells inside the grid, spilling well past it
    vis = 1                 # ordinary fully-fogged ground

    pw, ph = len(cols) * cell, len(rows) * cell
    margin, gap, top = 18, 28, 292
    W = margin * 2 + pw * 2 + gap
    H = top + ph + 150
    img = Image.new("RGB", (W, H), (16, 16, 18))
    d = ImageDraw.Draw(img)
    try:
        f_h = ImageFont.truetype("arialbd.ttf", 17)
        f_s = ImageFont.truetype("arial.ttf", 11)
    except OSError:
        f_h = f_s = ImageFont.load_default()

    # ---- the two curves ---------------------------------------------------------
    d.text((margin, 14), "What an overlay beyond the cell grid costs a sprite, against what the "
                         "fog over the map costs it", font=f_h, fill=(225, 225, 230))
    d.text((margin, 36), "higher = more of the sprite survives. The two must agree, or a sprite "
                         "crossing the map edge has a step through it.", font=f_s,
           fill=(170, 170, 178))
    px0, py0, pw2, ph2 = margin, 56, W - margin * 2, 150
    d.rectangle([px0, py0, px0 + pw2, py0 + ph2], outline=(70, 70, 78))
    for frac, lab in ((0.0, "0"), (0.5, "0.5"), (1.0, "1.0")):
        y = py0 + ph2 - int(frac * (ph2 - 10)) - 5
        d.line([(px0, y), (px0 + pw2, y)], fill=(44, 44, 50))
        d.text((px0 + pw2 + 4, y - 7), lab, font=f_s, fill=(120, 120, 128))

    def curve(fn, colour, width=3):
        pts = []
        for v in range(VISION_LAYERS):
            x = px0 + int(v * pw2 / (VISION_LAYERS - 1))
            y = py0 + ph2 - int(min(1.0, fn(v)) * (ph2 - 10)) - 5
            pts.append((x, y))
        d.line(pts, fill=colour, width=width)
        for p in pts:
            d.ellipse([p[0] - 3, p[1] - 3, p[0] + 3, p[1] + 3], fill=colour)

    curve(transmission, (150, 230, 160))
    curve(legacy_transmission, (255, 150, 130))
    for v in range(VISION_LAYERS):
        d.text((px0 + int(v * pw2 / (VISION_LAYERS - 1)) - 3, py0 + ph2 + 4), str(v), font=f_s,
               fill=(120, 120, 128))
    d.text((px0 + 10, py0 + 8), "GREEN  the fog over the map (ShroudRenderer.CompositeTransmission) "
                                "-- and what the overlay now uses", font=f_s, fill=(150, 230, 160))
    d.text((px0 + 10, py0 + 24), "RED    the hand-rolled copy the overlay used to use -- omits "
                                 "FogPaletteAlpha (160/255), so every layer bit harder",
           font=f_s, fill=(255, 150, 130))
    d.text((px0 + 10, py0 + 40), "At visibility 1 the sprite kept 0.0317 instead of 0.1378: "
                                 "4.35x too dark. Both curves agree only at the two fixed ends.",
           font=f_s, fill=(200, 200, 205))
    d.text((px0 + 10, py0 + ph2 + 20), "visibility level", font=f_s, fill=(120, 120, 128))

    # ---- before / after ---------------------------------------------------------
    draw_panel(img, margin, top, cols, rows, cell, cu, cv, vis, fixed_curve=False)
    draw_panel(img, margin + pw + gap, top, cols, rows, cell, cu, cv, vis, fixed_curve=True)
    d.text((margin, top - 40), "BEFORE  -  a cloud spilling past the map edge", font=f_h,
           fill=(255, 150, 130))
    d.text((margin + pw + gap, top - 40), "AFTER  -  wt/beyond-map-fog", font=f_h,
           fill=(150, 230, 160))
    d.text((margin, top - 20), "the half beyond the grid is drawn at 0.230x what the fog would "
                               "have cost it", font=f_s, fill=(190, 190, 195))
    d.text((margin + pw + gap, top - 20), "the sprite is now attenuated by exactly the map's own "
                                          "fog curve", font=f_s, fill=(190, 190, 195))

    grid_y = top + rows.index(0) * cell
    for ox in (margin, margin + pw + gap):
        d.line([(ox, grid_y), (ox + pw, grid_y)], fill=(255, 200, 90), width=1)
        d.text((ox + 4, grid_y + 3), "cell grid edge -- ground below, opaque black above",
               font=f_s, fill=(255, 200, 90))

    # Straight across the boundary: the last row inside the grid against the first row outside it.
    # Like for like -- one cell apart, same sprite, same light, only the renderer differs.
    ins = min(1.0, screen(cu, 0, cu, cv, vis, True)[1])
    b = min(1.0, screen(cu, -1, cu, cv, vis, False)[1])
    a = min(1.0, screen(cu, -1, cu, cv, vis, True)[1])
    d.text((margin, top + ph + 12),
           "Across the boundary on the cloud axis, one cell apart: outside was {:.3f}, is now "
           "{:.3f} -- {:.2f}x brighter, and now exactly the fog the map applies.".format(b, a, a / b),
           font=f_s, fill=(200, 200, 205))
    d.text((margin, top + ph + 30),
           "WHAT THIS DOES NOT FIX: the last row INSIDE the grid reads {:.3f}. The step across that "
           "boundary goes from {:.0f}x to {:.1f}x -- reduced, not closed.".format(
               ins, ins / b, ins / a),
           font=f_s, fill=(255, 200, 90))
    d.text((margin, top + ph + 46),
           "That residual is the fog-piercing LIGHT, not a fog mismatch. It is drawn per-cell over "
           "ground and stops at the grid because beyond it there is no",
           font=f_s, fill=(200, 200, 205))
    d.text((margin, top + ph + 62),
           "ground to light -- DrawBeyondMapFog paints that region opaque black before actors, by "
           "design. Closing this last step would mean lighting terrain",
           font=f_s, fill=(200, 200, 205))
    d.text((margin, top + ph + 78),
           "the game deliberately blacks out, which is the one thing the no-leak argument forbids. "
           "The step that remains is the light; the fog half is gone.",
           font=f_s, fill=(200, 200, 205))
    d.text((margin, top + ph + 100),
           "Note also that both panels are identical at visibility 10: anything carrying its own "
           "vision lights its border cell to full, where the overlay is absent",
           font=f_s, fill=(150, 150, 158))
    d.text((margin, top + ph + 116),
           "entirely. This correction cannot brighten a unit the player is already watching -- it "
           "reaches only what is beyond the grid AND under fog.",
           font=f_s, fill=(150, 150, 158))

    out = "WORKSPACE/mockups/beyond-grid-fog.png"
    img.save(out)
    print("wrote {}  ({}x{})".format(out, W, H))
    print("beyond-grid sprite v=1: legacy keeps {:.4f}, corrected keeps {:.4f}  ({:.2f}x)".format(
        legacy_transmission(1), transmission(1), transmission(1) / legacy_transmission(1)))
    print("axis sample 4 cells out: {:.4f} -> {:.4f}   inside-grid neighbour {:.4f}".format(b, a, ins))


main()
