"""WW3MOD: the one-cell band at a map edge, before and after wt/light-bounds.

Every number here is the shipped arithmetic, not an impression of it:
  * transmission  = ShroudRenderer.CompositeTransmission (ShroudRenderer.cs:319) at FogDarkness 1.4
  * falloff       = TerrainLighting.ApplyFalloff, InverseSquare branch (TerrainLighting.cs:255)
  * light         = Atomic Warhead@FireballLight keyframe 0 (weapons-superweapons.yaml:226-228)
  * ring geometry = every shipped map is MapSize = Bounds + 2, Bounds origin (1,1)

Projection is rectangular top-down. This is a diagram of the BRIGHTNESS, not a screenshot
replica -- the isometric projection changes where a cell lands, never what colour it gets.
"""
from PIL import Image, ImageDraw, ImageFont

# --- shipped constants -------------------------------------------------------
FOG_PALETTE_ALPHA = 160.0 / 255.0
FOG_DARKNESS = 1.4
VISION_LAYERS = 11
INV_SQ_K = 24.0
INV_SQ_EDGE = 1.0 / (1.0 + INV_SQ_K)
INV_SQ_SCALE = 1.0 / (1.0 - INV_SQ_EDGE)

TILE = 1024
RADIUS = 19 * TILE + 833      # 19c833
INTENSITY = 7.0
TINT = (0xE6 / 255.0, 0xF0 / 255.0, 0xFF / 255.0)

BOUNDS_TOP = 1                # ring row is V = 0; beyond the grid is V < 0
TERRAIN = (0.20, 0.23, 0.18)  # unlit ground


def layer_alpha(i, fd):
    a = 1.0
    if i > 1:
        a -= (i - 1) * (1.0 / 12)
    if i > 0:
        a = min(a * fd / 3.0, 1.0)
    return a


def transmission(vis, fd=FOG_DARKNESS):
    if vis <= 0:
        return 0.0
    t = 1.0
    for layer in range(vis, VISION_LAYERS - 1):
        t *= 1.0 - FOG_PALETTE_ALPHA * layer_alpha(layer, fd)
    return t


def apply_falloff(f):
    u = 1.0 - f
    return (1.0 / (1.0 + INV_SQ_K * u * u) - INV_SQ_EDGE) * INV_SQ_SCALE


def contribution(du_cells, dv_cells):
    """The light own contribution, same arithmetic as FogPiercingLightRenderable.ContributionAt."""
    d = ((du_cells * TILE) ** 2 + (dv_cells * TILE) ** 2) ** 0.5
    if d > RADIUS:
        return (0.0, 0.0, 0.0)
    f = apply_falloff((RADIUS - d) * 1.0 / RADIUS)
    return tuple(f * INTENSITY * c for c in TINT)


# --- the two renderers -------------------------------------------------------
# Screen value for one cell:
#   TerrainLighting adds the light INSIDE the ordinary draw   -> (terrain + contribution)
#   ShroudRenderer multiplies the finished world by T          -> * T
#   FogPiercingLight adds back the missing fraction            -> + (1 - T) * contribution
# With restoration the terrain alone stays fogged and the LIGHT reaches full strength, which
# is the identity FogPiercingLightTest.RestoredPlusTransmittedIsExactlyOne pins.

def cell_colour(u, v, cu, cv, vis, restore_ring):
    if v < 0:
        # Beyond the cell grid: DrawBeyondMapFog paints opaque black over the terrain BEFORE
        # actors (WorldRenderer.cs:455), so there is no ground here to light -- by design.
        # Only the cloud SPRITE is drawn, then DrawBeyondMapActorFog attenuates it.
        return None

    ring = v < BOUNDS_TOP
    # ShroudRenderer clamps a ring cell onto the playable cell it abuts (ShroudRenderer.cs:193),
    # so the fog quad a ring cell receives is its neighbour own -- in both builds.
    t = transmission(vis)
    contrib = contribution(u - cu, v - cv)

    restored = (not ring) or restore_ring
    out = []
    for i in range(3):
        val = (TERRAIN[i] + contrib[i]) * t
        if restored:
            val += (1.0 - t) * contrib[i]
        out.append(val)
    return out


def beyond_grid_colour(u, v, cu, cv, vis):
    """Opaque black, plus the cloud halo drawn on top and then fogged down."""
    contrib = contribution(u - cu, v - cv)
    a = 1.0 - transmission(vis)   # DrawBeyondMapActorFog alpha at this visibility
    return [ci * (1.0 - a) * 0.55 for ci in contrib]


def draw_panel(img, ox, oy, cols, rows, cell_px, cu, cv, vis, restore_ring):
    px = img.load()
    for vi, v in enumerate(rows):
        for ui, u in enumerate(cols):
            c = cell_colour(u, v, cu, cv, vis, restore_ring)
            if c is None:
                c = beyond_grid_colour(u, v, cu, cv, vis)
            rgb = tuple(min(255, max(0, int(ci * 255 + 0.5))) for ci in c)
            x0, y0 = ox + ui * cell_px, oy + vi * cell_px
            for y in range(y0, y0 + cell_px):
                for x in range(x0, x0 + cell_px):
                    px[x, y] = rgb


def main():
    cols = list(range(18, 62))     # 44 cells across
    rows = list(range(-7, 23))     # 30 cells down: 7 beyond-grid, 1 ring, 22 playable
    cell_px = 13
    cu, cv = 40, 12                # blast centre, 12 cells inside the playable edge
    vis = 1                        # explored but fully fogged -- a nuke landing in the dark

    pw, ph = len(cols) * cell_px, len(rows) * cell_px
    margin, gap, top = 16, 26, 76
    W = margin * 2 + pw * 2 + gap
    zh = 8 * 26          # zoomed boundary strip: 8 cell rows at 26 px
    H = top + ph + zh + 300
    img = Image.new("RGB", (W, H), (16, 16, 18))
    d = ImageDraw.Draw(img)

    try:
        f_h = ImageFont.truetype("arialbd.ttf", 17)
        f_s = ImageFont.truetype("arial.ttf", 11)
    except OSError:
        f_h = f_s = ImageFont.load_default()

    draw_panel(img, margin, top, cols, rows, cell_px, cu, cv, vis, restore_ring=False)
    draw_panel(img, margin + pw + gap, top, cols, rows, cell_px, cu, cv, vis, restore_ring=True)

    d.text((margin, 12), "BEFORE  -  main @ 5e93a2f0", font=f_h, fill=(255, 150, 130))
    d.text((margin, 36), "restoration clipped to Bounds; the ring keeps its neighbour fog quad "
                         "but gets no light back", font=f_s, fill=(190, 190, 195))
    d.text((margin + pw + gap, 12), "AFTER  -  wt/light-bounds", font=f_h, fill=(150, 230, 160))
    d.text((margin + pw + gap, 36), "sweep runs to the cell grid; the ring mask is read through "
                                    "the same clamp", font=f_s, fill=(190, 190, 195))

    # Zone rules on both panels.
    ring_y = top + rows.index(0) * cell_px
    grid_y = ring_y + cell_px
    for ox in (margin, margin + pw + gap):
        d.line([(ox, ring_y), (ox + pw, ring_y)], fill=(120, 170, 255), width=1)
        d.line([(ox, grid_y), (ox + pw, grid_y)], fill=(255, 200, 90), width=1)
        d.text((ox + 4, grid_y + 3), "playable Bounds", font=f_s, fill=(255, 200, 90))
        d.text((ox + pw - 30, ring_y + 1), "ring", font=f_s, fill=(255, 200, 90))
    d.text((margin + 4, ring_y - 15), "beyond the cell grid - opaque black by design "
                                      "(DrawBeyondMapFog)", font=f_s, fill=(120, 170, 255))

    # ---- magnified boundary strip ------------------------------------------------
    zrows = list(range(-3, 5))     # 3 beyond-grid, the ring, 4 playable
    zcols = list(range(29, 51))   # 22 cells, sized so two panels fit the figure width
    zcell = 26
    zy = top + ph + 46
    zw = len(zcols) * zcell
    d.text((margin, zy - 26), "The boundary strip itself, magnified 2x", font=f_h,
           fill=(225, 225, 230))
    draw_panel(img, margin, zy, zcols, zrows, zcell, cu, cv, vis, restore_ring=False)
    draw_panel(img, margin + zw + gap, zy, zcols, zrows, zcell, cu, cv, vis, restore_ring=True)
    zring_y = zy + zrows.index(0) * zcell
    for ox in (margin, margin + zw + gap):
        d.rectangle([ox, zring_y, ox + zw - 1, zring_y + zcell - 1], outline=(255, 90, 90))
    d.text((margin + 4, zring_y + zcell + 4),
           "the band the user reported: one tile, 6.4x darker than the tile below it",
           font=f_s, fill=(255, 130, 120))
    d.text((margin + zw + gap + 4, zring_y + zcell + 4),
           "continuous with the tile below it", font=f_s, fill=(150, 230, 160))

    # ---- brightness profile through the boundary --------------------------------
    py0 = top + ph + zh + 110
    ph2 = 110
    d.text((margin, py0 - 24), "Green channel down the blast axis (u = 40), both builds",
           font=f_h, fill=(225, 225, 230))
    plot_w = pw * 2 + gap
    d.rectangle([margin, py0, margin + plot_w, py0 + ph2], outline=(70, 70, 78))

    def profile(restore_ring):
        pts = []
        for vi, v in enumerate(rows):
            c = cell_colour(cu, v, cu, cv, vis, restore_ring)
            if c is None:
                c = beyond_grid_colour(cu, v, cu, cv, vis)
            y = min(1.0, c[1])
            x = margin + int(vi * plot_w / (len(rows) - 1))
            pts.append((x, py0 + ph2 - int(y * (ph2 - 8)) - 4))
        return pts

    rx = margin + int(rows.index(0) * plot_w / (len(rows) - 1))
    d.line([(rx, py0), (rx, py0 + ph2)], fill=(255, 200, 90), width=1)
    d.line(profile(False), fill=(255, 150, 130), width=3)
    d.line(profile(True), fill=(150, 230, 160), width=3)
    d.text((rx + 6, py0 + 4), "ring row", font=f_s, fill=(255, 200, 90))

    before = cell_colour(cu, 0, cu, cv, vis, False)[1]
    after = cell_colour(cu, 0, cu, cv, vis, True)[1]
    inside = cell_colour(cu, 1, cu, cv, vis, True)[1]
    d.text((margin, py0 + ph2 + 10),
           "ring cell: {:.3f} before, {:.3f} after.  Playable cell one step inside: {:.3f}.  "
           "The step across that one tile goes from {:.1f}x to {:.2f}x -".format(
               before, after, inside, inside / before, inside / after),
           font=f_s, fill=(200, 200, 205))
    d.text((margin, py0 + ph2 + 26),
           "and the residual is the light OWN falloff over one cell, which is exactly what "
           "every neighbouring pair of playable cells already shows.",
           font=f_s, fill=(200, 200, 205))
    d.text((margin, py0 + ph2 + 48),
           "Both panels agree above the yellow rule: NOTHING changes beyond the cell grid. That "
           "region is opaque black before any light is",
           font=f_s, fill=(150, 150, 158))
    d.text((margin, py0 + ph2 + 64),
           "considered, and the faint halo visible there is the cloud SPRITE, attenuated by "
           "DrawBeyondMapActorFog - a separate mechanism.",
           font=f_s, fill=(150, 150, 158))

    out = "WORKSPACE/mockups/nuke-edge-band.png"
    img.save(out)
    print("wrote {}  ({}x{})".format(out, W, H))
    print("ring before={:.4f} after={:.4f} inside={:.4f} step {:.2f}x -> {:.2f}x".format(
        before, after, inside, inside / before, inside / after))
    print("transmission(vis=1) = {:.4f}".format(transmission(1)))


main()
