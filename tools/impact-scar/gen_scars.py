#!/usr/bin/env python3
"""Generate the impact-scar smudge art for WW3MOD.

Three new smudge bands sit inside and outside the two the mod already has, so
a blast leaves a graded disc instead of one flat field of noise:

    ScarCore    bza   78-93% coverage, darkest    vaporised centre
    ScarCrater  bzb   62-80%                      crater lip
    ScarChar    bzc   46-64%                      heavy burn
    ScarBurn    bzd   30-48%                      burn
    ScarRim     bze   14-30%, sparsest, lightest  the soft rim -- this is the
                                                  band that makes the edge read
                                                  as a CIRCLE rather than a
                                                  staircase of cells

The stock Crater and Scorch types are left completely alone: plenty of
non-nuclear weapons still use them and none of this touches those.

Everything is deterministic: a fixed seed per (band, variant, tileset, frame),
so re-running writes byte-identical SHPs and the tree stays diffable.

Palette handling is the load-bearing part. A smudge renders through the
`terrain` palette, which is per-tileset, and the four tilesets do NOT agree:
temperat/snow/interior share indices 16-31 and 88-95, desert does not (index
18 is (20,20,24) near-black on temperate and (247,206,142) pale sand on
desert). Rather than guess which indices are safe, each tileset ramp is
harvested from THAT tileset own stock cr*/sc* art -- indices already proven
to render as smudge colours in that exact layer and palette.

Usage:  python gen_scars.py [--outdir DIR]
"""
import argparse
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import racontent as rc  # noqa: E402

CONTENT = os.path.expanduser("~/AppData/Roaming/OpenRA/Content/ra/v2")
HERE = os.path.dirname(os.path.abspath(__file__))

CELL = 24          # RA cell is 24x24; stock cr*.tem are exactly this
DEPTHS = 5         # stock craters carry 5 depth frames; AddSmudge deepens on re-hit
VARIANTS = 4       # stock ships 6 per type; 4 is ample given 5 depths on top

# (mix, tileset extension, palette file). INTERIOR is absent on purpose: the
# smudge sequences carry `TilesetOverrides: INTERIOR: TEMPERAT`, so interior
# maps read the .tem files.
TILESETS = [
    ("temperat.mix", "tem", "temperat"),
    ("snow.mix", "sno", "snow"),
    ("cnc/desert.mix", "des", "desert"),
]

# band -> (prefix, coverage at depth 0, coverage at depth 4, ramp window)
# The ramp window is a (lo, hi) fraction into the tileset dark->light ramp;
# lower is darker. Bands overlap slightly so the rings blend rather than step.
# FIVE bands, and all five are generated here. An earlier cut reused the stock
# Crater and Scorch art for the two middle rings and it did not work: measured
# over the 24x24 cell, stock cr*.tem covers 4-6% at depth 0 rising to only 40%
# at depth 4, and stock sc*.tem ranges from 20% (sc3) to 46% (sc6) BETWEEN
# VARIANTS. Dropped into a graded ring system that inverts the gradient -- the
# Crater ring came out lighter than the Charred ring outside it, and the scar
# rendered as a donut with a pale gap. Stock art was never drawn to be part of
# a radial ramp; these five are.
#
# Coverage steps by ~0.16 per band and never reaches 1.0. A fully-opaque cell
# is a 24x24 square of solid colour, and a disc assembled from those has
# visibly square edges -- the exact artefact this work removes.
#
# band -> (file prefix, coverage at depth 0, coverage at depth 4, ramp window)
# The ramp windows all stay in the DARK end and overlap heavily; COVERAGE, not
# colour, is what carries the radial gradient. That is not a stylistic choice.
# The harvested ramp runs past the terrain's own luminance -- on temperate its
# pale end is (89,85,45) and (101,105,89) against clear ground of roughly
# (45,65,45) -- so a band drawn from the top of the ramp is LIGHTER than the
# grass it sits on and reads as haze, not burning. The first five-band cut gave
# the outer two rings the pale end and they vanished against the terrain.
BANDS = [
    ("ScarCore",   "bza", 0.78, 0.93, (0.00, 0.25)),
    ("ScarCrater", "bzb", 0.62, 0.80, (0.05, 0.40)),
    ("ScarChar",   "bzc", 0.46, 0.64, (0.15, 0.55)),
    ("ScarBurn",   "bzd", 0.30, 0.48, (0.25, 0.70)),
    ("ScarRim",    "bze", 0.14, 0.30, (0.35, 0.80)),
]
BAND_BY_NAME = {b[0]: b for b in BANDS}


# ---------------------------------------------------------------- noise
def _hash(x, y, seed):
    """Deterministic 32-bit integer hash -> float in [0,1). No numpy, no RNG
    state, so output depends only on the arguments and never on call order."""
    h = (x * 374761393 + y * 668265263 + seed * 2246822519) & 0xFFFFFFFF
    h = (h ^ (h >> 13)) * 1274126177 & 0xFFFFFFFF
    h = h ^ (h >> 16)
    return h / 0xFFFFFFFF


def _value_noise(x, y, seed):
    """Bilinearly interpolated value noise at fractional (x, y)."""
    x0, y0 = int(x // 1), int(y // 1)
    fx, fy = x - x0, y - y0
    fx = fx * fx * (3 - 2 * fx)     # smoothstep, so cells do not show as creases
    fy = fy * fy * (3 - 2 * fy)
    n00 = _hash(x0, y0, seed)
    n10 = _hash(x0 + 1, y0, seed)
    n01 = _hash(x0, y0 + 1, seed)
    n11 = _hash(x0 + 1, y0 + 1, seed)
    return (n00 * (1 - fx) + n10 * fx) * (1 - fy) + (n01 * (1 - fx) + n11 * fx) * fy


def _fbm(x, y, seed, octaves=4):
    """Fractal noise. Deliberately high base frequency (1/4 cell): the whole
    point is that no cell-sized structure survives, because neighbouring cells
    get independently-chosen variants and any low-frequency feature would read
    as a 24px grid."""
    total = 0.0
    amp = 1.0
    norm = 0.0
    freq = 0.25
    for o in range(octaves):
        total += amp * _value_noise(x * freq, y * freq, seed + o * 7919)
        norm += amp
        amp *= 0.5
        freq *= 2.1          # non-integer, so octaves do not align into a lattice
    return total / norm


# ---------------------------------------------------------------- palette
def harvest_ramp(mixpath, ext, palpath):
    """Per-tileset dark->light index ramp, taken from that tileset own stock
    smudge art.

    Two filters, and both earn their place:
      * >= 0.1% of drawn pixels. Set at 1% this discards the genuine dark end
        the artists DID use but used sparingly -- snow index 142 (36,36,36) is
        0.44% and is the only thing on that tileset dark enough for a blast
        centre.
      * saturation <= 60. Stock art carries a handful of strays that are
        nothing to do with burnt ground: snow cr*.sno has one pixel of index 11
        (0,0,170, pure blue). A frequency cut alone cannot tell that apart from
        a rare-but-correct colour, and it renders as a blue speck in the crater.

    Note what this deliberately does NOT do: it does not force every tileset to
    the same darkness. Snow smudges are mid-grey in stock RA and stay mid-grey
    here, because that is what the tileset artists chose."""
    mix = rc.MixFile(mixpath)
    pal = rc.read_pal(open(palpath, "rb").read())
    counts = collections.Counter()
    for prefix, n in (("cr", 6), ("sc", 6)):
        for i in range(1, n + 1):
            data = mix.get(prefix + str(i) + "." + ext)
            if not data:
                continue
            _w, _h, frames = rc.read_shp(data)
            for f in frames:
                counts.update(f)
    counts.pop(0, None)                      # index 0 is transparent
    total = sum(counts.values())

    # Keep the indices that account for the first 92% of drawn stock pixels,
    # most-used first. A flat frequency floor cannot do this job: it keeps every
    # pale outlier that clears the floor, and those are what pushed the outer
    # bands lighter than the ground. Taking the bulk of the distribution keeps
    # the colours the tileset artists actually leaned on.
    ranked = sorted(counts.items(), key=lambda kv: -kv[1])
    keep, acc = [], 0
    for i, c in ranked:
        if max(pal[i]) - min(pal[i]) > 60:
            continue                         # stray hue, e.g. snow's index 11
        keep.append(i)
        acc += c
        if acc / total >= 0.92:
            break
    ramp = sorted(keep, key=lambda i: sum(pal[i]))
    if len(ramp) < 4:
        raise SystemExit(ext + ": harvested ramp too short " + repr(ramp))
    return ramp, pal


def window(ramp, lo, hi):
    """Slice a fraction of the ramp, always at least two entries wide so a band
    has some internal tonal variation."""
    a = int(lo * (len(ramp) - 1))
    b = max(a + 2, int(hi * (len(ramp) - 1)))
    return ramp[a:b + 1] or ramp[:2]


# ---------------------------------------------------------------- art
def make_frame(band, variant, depth, ramp, seed_base):
    """One 24x24 indexed frame. Index 0 = transparent.

    Coverage is applied as a QUANTILE of the noise field, not as a threshold on
    its raw value. That distinction is the whole band structure: fbm output is
    bell-shaped around 0.5, not uniform, so `mask > 1 - coverage` gives nothing
    like `coverage`. Measured on the first cut of this file: singed asked for
    0.16 and got 0.00-0.02 (three frames were entirely empty, and the engine
    silently drops an empty frame -- `--png` wrote 57 files where 60 were
    expected), while blasted asked for 0.82 and got 1.00, i.e. a solid opaque
    24x24 square. A disc built from solid squares is precisely the blocky
    artefact this work exists to remove.
    """
    _name, _prefix, cov0, cov1, win = BAND_BY_NAME[band]
    sub = window(ramp, *win)
    t = depth / (DEPTHS - 1)
    coverage = cov0 + (cov1 - cov0) * t

    seed = seed_base + variant * 104729 + depth * 15485863

    # Two decorrelated fields: one decides IF a pixel is burnt, the other HOW
    # DARK. Driving both from one field makes the sparse bands read as contour
    # lines rather than scatter.
    mask = [_fbm(x + variant * 64, y + variant * 64, seed)
            for y in range(CELL) for x in range(CELL)]
    tone = [_fbm(x + 512, y + 512, seed + 31337)
            for y in range(CELL) for x in range(CELL)]

    n = CELL * CELL
    want = int(round(coverage * n))
    order = sorted(range(n), key=lambda i: mask[i], reverse=True)
    burnt = order[:want]

    out = bytearray(n)
    for rank, i in enumerate(burnt):
        # Pixels deepest inside the burnt region (highest mask rank) go furthest
        # down the dark end of the ramp.
        bias = 1.0 - rank / max(want - 1, 1)
        k = 0.62 * (1.0 - bias) + 0.38 * tone[i]
        # Depth also darkens: a re-hit cell should read as more burnt, not
        # merely as more covered.
        k *= 1.0 - 0.35 * t
        out[i] = sub[min(int(min(max(k, 0.0), 0.999) * len(sub)), len(sub) - 1)]

    return bytes(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--outdir", default=os.path.join(HERE, "out"))
    args = ap.parse_args()
    os.makedirs(args.outdir, exist_ok=True)

    written = []
    for mixname, ext, palname in TILESETS:
        mixpath = os.path.join(CONTENT, mixname)
        palpath = os.path.join(HERE, "pal", palname + ".pal")
        ramp, _pal = harvest_ramp(mixpath, ext, palpath)
        print("[" + ext + "] ramp of " + str(len(ramp)) + " indices: " + repr(ramp))

        for band, prefix, *_cfg in BANDS:
            for v in range(1, VARIANTS + 1):
                frames = [make_frame(band, v, d, ramp, sum(map(ord, band)) * 1013)
                          for d in range(DEPTHS)]
                name = prefix + str(v) + "." + ext
                path = os.path.join(args.outdir, name)
                size = rc.write_shp(path, CELL, CELL, frames)

                # Read it straight back through the ported decoder; a silent
                # LCW bug would otherwise only surface in-game.
                w, h, got = rc.read_shp(open(path, "rb").read())
                assert (w, h) == (CELL, CELL) and got == frames, name
                written.append((name, size))

    print("")
    print(str(len(written)) + " SHPs written to " + args.outdir +
          " (" + str(sum(s for _n, s in written)) + " bytes total)")


if __name__ == "__main__":
    main()
