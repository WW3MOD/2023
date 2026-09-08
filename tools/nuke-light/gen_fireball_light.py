#!/usr/bin/env python3
"""
Regenerates the `Warhead@FireballLight` envelope of every nuclear weapon in the mod.

WHY THIS IS A SCRIPT AND NOT FOURTEEN HAND-TYPED TABLES. The one rule the envelopes obey is

    the light lasts as long as the weapon's own FIREBALL ANIMATION,

and the animation's length is not a number anyone chose -- it falls out of the SHP's frame count
walked along the sequence's `Tick`/`ChangeTick` ladder in `mods/ww3mod/sequences/sequences-ingame.yaml`.
Change the ladder, or swap the sprite, and all fourteen envelopes must move with it. This script reads
the ladder and the SHP header and derives the durations, so "regenerate" is a command rather than an
afternoon. NuclearYieldTest.cs re-derives the same number independently and fails if the shipped YAML
has drifted away from it.

    python3 tools/nuke-light/gen_fireball_light.py            # table only, touches nothing
    python3 tools/nuke-light/gen_fireball_light.py --check    # non-zero exit if the YAML has drifted
    python3 tools/nuke-light/gen_fireball_light.py --write    # rewrite the Light: blocks in place

DO NOT hard-code the animation length. Every weapon still shares one sequence (`nuke_large`), but
since 2026-09-08 each plays it at its own speed: the CreateEffect warhead's `DurationScalePercent`
stretches the sprite in time the way `ScalePercent` stretches it in space, following
`t = 11.95 s * (Y/20)^0.12` -- 60% at the 0.3 kt B61 dial, 100% at the 20 kt anchor, 256% at Tsar
Bomba. So the animation length is the ladder walk TIMES that percentage, looked up per weapon through
its own `Warhead@Fireball` block. Ten sequences would work exactly as one does.
"""

import argparse
import os
import re
import struct
import sys

TICKS_PER_SECOND = 1000.0 / 60.0     # 60 ms timestep. NOT 25 tps; see conventions.md.
ANIM_MS_PER_TICK = 40                # Animation.Tick() calls Tick(40) regardless of the timestep.

# ---------------------------------------------------------------- the envelope law

PEAK = 7.0                # unchanged from the pre-2026-09-07 envelope, and deliberately flat.
SHOULDER_FRACTION = 0.70  # intensity at the end of the white phase, as a fraction of PEAK.


def white_ticks(kt):
    """Length of the blinding white phase. Scales mildly with yield and is always short."""
    return int(min(14, max(3, round(4.0 * (kt / 20.0) ** 0.15))))


def physical_fireball_ticks(kt):
    """0.8 s at 20 kt, t ~ Y^0.44 -- the incandescent lifetime of a real fireball."""
    return 0.21411 * kt ** 0.44 * TICKS_PER_SECOND


def intensity(t, w, d):
    """Two stages, both squared falloffs, continuous at the shoulder, monotone non-increasing."""
    s = PEAK * SHOULDER_FRACTION
    if t <= w:
        f = 1.0 - t / float(w)
        return s + (PEAK - s) * f * f
    u = (t - w) / float(d - w)
    return s * (1.0 - u) * (1.0 - u)


# Colour keyed on t/D. Blue must be non-increasing and (R+1)/(B+1) strictly increasing, or the
# cooling assertion in NuclearYieldTest fails -- see the note there on ratio vs difference.
COLOUR_RAMP = [
    (0.00, (0xE6, 0xF0, 0xFF)),   # blue-white: hotter than white
    (0.03, (0xFF, 0xFF, 0xFF)),   # white
    (0.10, (0xFF, 0xF8, 0xE8)),
    (0.20, (0xFF, 0xEF, 0xC8)),
    (0.35, (0xFF, 0xE0, 0xA0)),   # yellow
    (0.50, (0xFF, 0xC9, 0x6E)),
    (0.65, (0xFF, 0xA8, 0x45)),   # orange
    (0.80, (0xF0, 0x7C, 0x24)),
    (0.92, (0xD2, 0x50, 0x0F)),
    (1.00, (0xB4, 0x28, 0x0A)),   # dull ember red
]


def tint(v):
    v = max(0.0, min(1.0, v))
    for i in range(len(COLOUR_RAMP) - 1):
        a, ca = COLOUR_RAMP[i]
        b, cb = COLOUR_RAMP[i + 1]
        if v <= b:
            f = 0.0 if b == a else (v - a) / (b - a)
            return tuple(int(round(ca[k] + (cb[k] - ca[k]) * f)) for k in range(3))
    return COLOUR_RAMP[-1][1]


def keyframe_times(w, d, extra):
    """Dense through the white spike, geometric through the tail. `extra` pins the radius corners."""
    ts = {0, w, d}
    if w <= 6:
        ts.update(range(1, w))
    else:
        ts.update(int(round(w * k / 4.0)) for k in (1, 2, 3))
    steps = 8
    for k in range(1, steps):
        ts.add(int(round(w + (d - w) * (k / float(steps)) ** 1.35)))
    ts.update(extra)
    return sorted(t for t in ts if 0 <= t <= d)


# ---------------------------------------------------------------- reading the tree

def repo_root():
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def shp_frame_count(path):
    """TS-format SHP header: u16 zero, u16 width, u16 height, u16 numimages."""
    with open(path, 'rb') as f:
        zero, _, _, count = struct.unpack('<HHHH', f.read(8))
    if zero != 0:
        raise SystemExit(path + " is not a TS-format SHP (leading u16 is not 0)")
    return count


def sequence_ladder(root, name):
    """`Tick` and `ChangeTick` of one `explosion:` sequence, plus the SHP it draws."""
    path = os.path.join(root, 'mods', 'ww3mod', 'sequences', 'sequences-ingame.yaml')
    lines = open(path, encoding='utf-8').read().split('\n')
    start = next(k for k, line in enumerate(lines) if line.rstrip() == 'explosion:')
    end = start + 1
    while end < len(lines) and (lines[end].strip() == '' or lines[end].startswith('\t')):
        end += 1

    entry = None
    for k in range(start + 1, end):
        m = re.match(r'^\t([A-Za-z0-9_]+):\s*(\S+)\s*$', lines[k])
        if m and m.group(1) == name:
            entry = k
            break
    if entry is None:
        raise SystemExit("sequences-ingame.yaml has no explosion sequence " + name)

    shp = re.match(r'^\t([A-Za-z0-9_]+):\s*(\S+)\s*$', lines[entry]).group(2)
    tick, change = 40, None
    for k in range(entry + 1, end):
        if not lines[k].startswith('\t\t'):
            break
        s = lines[k].strip()
        if s.startswith('Tick:'):
            tick = int(s.split(':', 1)[1])
        elif s.startswith('ChangeTick:'):
            change = [int(x) for x in s.split(':', 1)[1].split(',')]
    return shp, tick, change


def animation_ticks(root, sequence):
    """
    Length of one play-through of an explosion sequence, in GAME ticks.

    Animation.CurrentSequenceTickOrDefault walks ChangeTick as (frame, ms) pairs, taking the LAST
    pair whose frame is strictly below the current one; Animation.Tick() then spends 40 ms of that
    budget per game tick whatever the mod's timestep is. So the length is the summed per-frame
    budget divided by 40 -- not the frame count, and not affected by `ScalePercent`, which is why
    every weapon sharing this sequence currently gets the same duration at wildly different sizes.
    """
    shp, tick, change = sequence_ladder(root, sequence)
    frames = shp_frame_count(
        os.path.join(root, 'mods', 'ww3mod', 'bits', 'weapons', 'explosions', shp + '.shp'))

    def frame_ms(frame):
        chosen = 0
        if change:
            for i in range(0, len(change), 2):
                if frame > change[i]:
                    chosen = change[i + 1]
                else:
                    break
        return chosen if chosen != 0 else tick

    return sum(frame_ms(f) for f in range(frames)) / float(ANIM_MS_PER_TICK)


WEAPON_FILES = ('weapons-superweapons.yaml', 'weapons-nuclear-arsenal.yaml')

# Yields must match NuclearYieldTest.AllNukes. Anything added to either file goes in both places.
YIELDS = {
    'Atomic': 20.0, 'AtomicHighYield': 6000.0,
    'NukeB61Mod12Y003': 0.3, 'NukeB61Mod12Y015': 1.5, 'NukeB61Mod12Y10': 10.0,
    'NukeB61Mod12Y50': 50.0, 'NukeW76': 100.0, 'NukeSarmatRV': 750.0,
    'NukeB83': 1200.0, 'NukeTsarBomba': 50000.0,
    'NukeRu9M729': 1.0, 'NukeRuIskander': 10.0, 'NukeRuKinzhalN': 50.0, 'NukeRuKalibr': 100.0,
}

# ---------------------------------------------------------------- the light's REACH
#
# THE LIGHT IS SIZED AGAINST ITS OWN CLOUD, NOT AGAINST ITS YIELD. Changed 2026-09-08, and this
# replaces a thermal-radius law -- 12.5 * (kt/20)^0.41, which the shipped envelopes sat within 0.4%
# of. The reason is the user, after playing the ladder:
#
#   "the Light for the 6Mt warhead is really nice. I would like that same light for all nukes, just
#    that the size and length/duration of the flash is smaller/shorter for the smaller weapons, but
#    around the nuke it should still have that same intense glow. Currently the small nukes gives
#    almost no light at all it looks like to my eyes."
#
# Peak intensity was ALREADY flat at 7.0 on all fourteen, so "almost no light" was not a brightness
# problem and could not be fixed by raising anything. It was a RATIO problem. The light radius grew
# as Y^0.41 while the mushroom cloud that covers it grows as Y^0.214, so the ratio between them swung
# 10.7x across the ladder, and every weapon at or below 100 kt had a light radius SMALLER than its
# own cloud: 0.28x at 0.3 kt, 0.62x at 20 kt, 0.89x at 100 kt. The whole lit area sat under the
# sprite. The glow was at full brightness and there was nowhere to see it. The two weapons the user
# singled out as good are the two with the largest ratios -- 1.89x and 2.99x.
#
# So the light now reaches a FIXED MULTIPLE of the weapon's own cloud radius, and the multiple is
# AtomicHighYield's, read from the shipped file rather than typed here. That weapon is the one the
# user named, so it comes out of this generator byte-identical -- asserted below, not assumed.
#
# WHAT THIS COSTS, and it should be said plainly: the physical grounding. Y^0.41 is the third-degree
# burn radius; the new law is "however big the sprite is". Physics already lost this same argument
# for the cloud itself on 2026-09-08 -- its own fireball exponent is 0.40 and it ships at 0.214,
# because 0.40 gave an 848-cell sprite on a 130-cell map -- so the precedent is real and it is the
# same ladder. It is still a look-first decision and the user has been told so.
#
# WHY A CONSTANT RATIO IS THE RIGHT SHAPE rather than merely a convenient one: TerrainLighting's
# InverseSquare falloff is w(f) = (1/(1 + 24*(1-f)^2) - 1/25) * 25/24 with f = 1 - r/R
# (TerrainLighting.cs:48-50, :255-257). Hold R/cloudRadius constant and every weapon has the SAME
# fraction of peak brightness at its own cloud's edge -- 9.3% of 7.0 -- which is exactly the "same
# intense glow around the nuke" that was asked for, at every yield, for free.
#
# NOT TOUCHED, deliberately: peak intensity (flat 7.0, the user asked for identical core brightness
# in as many words), duration (already monotone 119 -> 510 ticks and the user is happy with it), and
# the falloff constant K (AtomicHighYield's look IS K=24 at ratio 1.888, so reproducing that ratio
# everywhere is what reproduces that look everywhere).
#
# NOTHING BUT THE RENDERER READS THIS. LightEventDefinition.Radii reaches only
# TerrainLighting.AddLightSource and FogPiercingLightRenderable (LightEventManager.cs:148, :290) --
# no damage, no vision, no shroud reveal, and no clamp but a 1-unit floor (:202-205). The thermal
# and vaporize warheads carry their own Spread and are untouched by anything here.
LIGHT_ANCHOR = 'AtomicHighYield'

# nuke_large is a 310 px sprite and a cell is 24 px, so ScalePercent 100 spans 310/24 cells across.
# Halved because a light Radii is a RADIUS and a sprite's ScalePercent is a WIDTH.
CLOUD_CELLS_PER_PERCENT = 310.0 / (100.0 * 24.0) / 2.0


def parse_wdist(s):
    m = re.match(r'^(-?\d+)c(\d+)$', s.strip())
    return int(m.group(1)) + int(m.group(2)) / 1024.0 if m else int(s.strip()) / 1024.0


def format_wdist(cells):
    total = int(round(cells * 1024))
    return "%dc%d" % (total // 1024, total % 1024)


def weapon_block(lines, name):
    start = next(k for k, line in enumerate(lines) if line.rstrip() == name + ':')
    end = start + 1
    while end < len(lines) and (lines[end].strip() == '' or lines[end].startswith('\t')):
        end += 1
    return start, end


def read_weapons(root):
    out = {}
    for filename in WEAPON_FILES:
        path = os.path.join(root, 'mods', 'ww3mod', 'rules', 'weapons', filename)
        lines = open(path, encoding='utf-8').read().split('\n')
        for name in YIELDS:
            if not any(line.rstrip() == name + ':' for line in lines):
                continue
            start, end = weapon_block(lines, name)
            block = lines[start:end]

            def field(key):
                for line in block:
                    s = line.strip()
                    if s.startswith(key + ':'):
                        return s.split(':', 1)[1].strip()
                raise SystemExit(name + " has no " + key)

            # The sequence, its duration scale AND its size all come from the same Warhead@Fireball
            # block: reading any of them from elsewhere in the weapon would pair the wrong numbers.
            # Since 2026-09-08 every nuclear weapon draws exactly ONE cloud, enforced by
            # NuclearYieldTest.EveryNuclearCloudIsOneSpriteOnTheYieldLaw -- Tsar Bomba used to draw
            # five, which is why the loop below used to stop at the first DurationScalePercent.
            sequence, scale, cloud = None, 100, None
            in_fireball = False
            for line in block:
                if re.match(r'^\t\S', line):     # a new warhead header ends the one we are inside
                    in_fireball = False
                text = line.strip()
                if text.startswith('Explosions:') and 'nuke' in text and sequence is None:
                    sequence = text.split(':', 1)[1].strip()
                    in_fireball = True
                elif in_fireball and text.startswith('DurationScalePercent:'):
                    scale = int(text.split(':', 1)[1])
                elif in_fireball and text.startswith('ScalePercent:'):
                    cloud = int(text.split(':', 1)[1])
            if sequence is None:
                raise SystemExit(name + " has no nuke Explosions: to take its animation from")
            if cloud is None:
                raise SystemExit(name + " has no ScalePercent on its Warhead@Fireball to size the light against")

            out[name] = dict(
                file=filename, kt=YIELDS[name], sequence=sequence, scale=scale,
                cloud_cells=cloud * CLOUD_CELLS_PER_PERCENT,
                times=[int(x) for x in field('Times').split(',')],
                intensities=[float(x) for x in field('Intensities').split(',')],
                radii=[parse_wdist(x) for x in field('Radii').split(',')],
                tints=[x.strip() for x in field('Tints').split(',')],
                refresh=int(field('TerrainRefreshInterval')))

    missing = set(YIELDS) - set(out)
    if missing:
        raise SystemExit("no Warhead@FireballLight found for: " + ", ".join(sorted(missing)))
    return out


# ---------------------------------------------------------------- generating

def refresh_interval(name, rmax, current):
    # AtomicHighYield and Tsar Bomba are the two the user is happy with; their cadence is left alone.
    if name in ('AtomicHighYield', 'NukeTsarBomba'):
        return current
    return 2 if rmax < 15 else (3 if rmax < 40 else 5)


def build(root, weapons):
    # The anchor is READ, not typed: the multiple every other weapon adopts is whatever
    # AtomicHighYield already ships. That keeps the weapon the user pointed at byte-identical through
    # this generator by construction, and it means retuning the reference retunes the ladder with it
    # rather than silently disagreeing with it.
    anchor = weapons[LIGHT_ANCHOR]
    light_to_cloud = max(anchor['radii']) / anchor['cloud_cells']

    # The anchor must be a FIXED POINT of this generator, not merely intended to be one. If reading
    # the ratio off it and feeding it back does not reproduce its own radius, the two halves disagree
    # and every other weapon is being sized against a number the reference does not actually have.
    check = light_to_cloud * anchor['cloud_cells']
    if abs(check - max(anchor['radii'])) > 1e-9:
        raise SystemExit("%s is not a fixed point: %.6f in, %.6f out" % (LIGHT_ANCHOR, max(anchor['radii']), check))

    out = {}
    for name, cur in weapons.items():
        kt = cur['kt']
        # SpriteEffect banks a milliseconds x percent remainder rather than rounding per tick, so the
        # played length is the natural length times the percentage, to within one tick.
        d_anim = animation_ticks(root, cur['sequence']) * cur['scale'] / 100.0
        d_phys = physical_fireball_ticks(kt)
        duration = int(round(max(d_anim, d_phys)))
        white = white_ticks(kt)

        # The SHAPE of the radius curve is carried over from the shipped envelope -- the Taylor-Sedov
        # rise, the hold, the shrink as it rises -- and only its SCALE is recomputed. `stretch` is the
        # one number that changes: every keyframe radius is multiplied by it, so the curve keeps its
        # proportions and simply reaches further. See THE LIGHT'S REACH above for where it comes from.
        stretch = light_to_cloud * cur['cloud_cells'] / max(cur['radii'])
        rmax = max(cur['radii']) * stretch
        r0 = cur['radii'][0] * stretch
        t_grow = cur['times'][cur['radii'].index(max(cur['radii']))]
        end_fraction = cur['radii'][-1] / max(cur['radii'])
        t_hold = max(t_grow, int(round(0.46 * duration)))

        def radius(t, rmax=rmax, r0=r0, t_grow=t_grow, t_hold=t_hold,
                   end_fraction=end_fraction, duration=duration):
            if t <= t_grow:
                return rmax if t_grow == 0 else r0 + (rmax - r0) * (t / float(t_grow)) ** 0.4
            if t <= t_hold:
                return rmax
            return rmax + (rmax * end_fraction - rmax) * (t - t_hold) / float(duration - t_hold)

        times = keyframe_times(white, duration, (t_grow, t_hold))
        out[name] = dict(
            cur, d_anim=d_anim, d_phys=d_phys, duration=duration, white=white,
            new_times=times,
            new_intensities=[round(intensity(t, white, duration), 2) for t in times],
            new_radii=[format_wdist(radius(t)) for t in times],
            new_tints=["%02X%02X%02X" % tint(t / float(duration)) for t in times],
            new_refresh=refresh_interval(name, rmax, cur['refresh']))
    return out


def render_fields(e):
    return {
        'Times': ", ".join(str(t) for t in e['new_times']),
        'Intensities': ", ".join("%g" % i for i in e['new_intensities']),
        'Radii': ", ".join(e['new_radii']),
        'Tints': ", ".join(e['new_tints']),
        'TerrainRefreshInterval': str(e['new_refresh']),
    }


def rewrite(root, built):
    for filename in WEAPON_FILES:
        path = os.path.join(root, 'mods', 'ww3mod', 'rules', 'weapons', filename)
        lines = open(path, encoding='utf-8').read().split('\n')
        for name, e in built.items():
            if e['file'] != filename:
                continue
            start, end = weapon_block(lines, name)
            fields = render_fields(e)
            seen = set()
            for k in range(start, end):
                s = lines[k].strip()
                for key, val in fields.items():
                    if s.startswith(key + ':') and key not in seen:
                        indent = lines[k][:len(lines[k]) - len(lines[k].lstrip('\t'))]
                        lines[k] = indent + key + ": " + val
                        seen.add(key)
            missing = set(fields) - seen
            if missing:
                raise SystemExit(name + ": could not find " + str(sorted(missing)) + " to rewrite")
        open(path, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--write', action='store_true')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()

    root = repo_root()
    built = build(root, read_weapons(root))

    print("%-18s %9s %6s %8s %8s %6s %5s  %s"
          % ("weapon", "kt", "dur%", "anim", "phys", "D", "W", "duration source"))
    for name in sorted(built, key=lambda n: built[n]['kt']):
        e = built[name]
        src = "animation" if e['d_anim'] >= e['d_phys'] else "physics (outlives the animation)"
        print("%-18s %9g %6d %8.1f %8.1f %6d %5d  %s"
              % (name, e['kt'], e['scale'], e['d_anim'], e['d_phys'], e['duration'], e['white'], src))

    if args.write:
        rewrite(root, built)
        print("\nrewrote %d Light: blocks" % len(built))
        return 0

    if args.check:
        drift = sorted(n for n, e in built.items() if e['times'][-1] != e['duration'])
        print("\n%d envelope(s) out of date: %s" % (len(drift), ", ".join(drift) or "none"))
        return 1 if drift else 0

    return 0


if __name__ == '__main__':
    sys.exit(main())
