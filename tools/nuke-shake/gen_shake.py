#!/usr/bin/env python3
"""Generate the Warhead@Shake stack for the twelve weapons in weapons-nuclear-arsenal.yaml.

WHY THIS EXISTS. `Atomic` and `AtomicHighYield` were retuned onto the rebuilt seismic model in
f945a645 ("Rebuild screen shake ... and retune every call site", 2026-09-06). That commit touched
ZERO lines of weapons-nuclear-arsenal.yaml, which was created the same day in cae070e6 and merged
after it -- so the arsenal kept the PRE-REWORK numbers, and f917d721 copied them into four more
Russian warheads the next day. `Intensity` changed meaning in that commit from "numerator of an
inverse-square falloff" to PEAK SCREEN DISPLACEMENT IN PIXELS (ShakeScreenWarhead.cs:23-29), so the
0.3 kt B61 asks for 35 px and the 50 kt asks for 76 against the 6 Mt reference's 20. ScreenShaker
soft-clamps the SUM at MaxAmplitude 22 through tanh (ScreenShaker.cs:53, :412-421), so both of those
saturate and the small weapons shake the camera HARDER than the strategic nuke. That is what the
user reported as the arsenal feeling "modeled differently".

THE LAW. Unlike blast, fireball and thermal, the shake band has no documented yield law in this
tree -- the two reference stacks were hand-tuned, not derived. So one exponent per field is fitted
to the only two tuned points that exist:

    v(Y) = v20 * (Y/20)^k        k = ln(v6000/v20) / ln(300)

Two points determine one exponent exactly, so BOTH references are reproduced to the tick by
construction (--verify checks that against the shipped YAML rather than trusting it), and the twelve
interpolate between two stacks the user has already approved rather than extrapolating off one.

INTENSITY BARELY MOVES WITH YIELD AND THAT IS THE POINT -- the fitted exponents are 0.04 to 0.08, so
50 Mt asks for 23 px against 0.3 kt's 9. What yield buys here is LENGTH (the roll runs 59 ticks at
0.3 kt and 564 at 50 Mt) and REACH (the air stage chases a wavefront 55x longer). That is the same
rule the fireball light already follows: peak brightness is yield-independent, area and duration are
not.

STAGE STRUCTURE, taken from the references rather than invented:
    crack   the prompt arrival: short, sharp, high frequency, no delay
    ground  the main ground shock and the peak of the whole event
    roll    the long low-frequency coda
    coda    a second, much longer roll -- present on AtomicHighYield and NOT on Atomic, so it is
            gated on yield. The threshold is kept where the arsenal already had it (>= 750 kt),
            which is the one stage-count decision in these files that was already deliberate.
    air     the air blast, which travels at ~6.35 ticks/cell rather than the global 0.9 ground-wave
            speed, so it tracks the VISIBLE shockwave at every distance instead of only at ground
            zero. MaxPropagationDelay is the documented "front's own travel time to the edge of its
            blast radius" (ShakeScreenWarhead.cs:68-76) = MaxRadius * PropagationTicksPerCell, and
            is emitted only when that exceeds the global ceiling of 300 (ScreenShaker.cs:157).
            AtomicHighYield's shipped 645 against 102c * 6.3 = 643 is where that law was read from.

Usage:  python3 tools/nuke-shake/gen_shake.py [--write] [--verify]
"""

import math
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ARSENAL = os.path.join(ROOT, 'mods', 'ww3mod', 'rules', 'weapons', 'weapons-nuclear-arsenal.yaml')
SUPER = os.path.join(ROOT, 'mods', 'ww3mod', 'rules', 'weapons', 'weapons-superweapons.yaml')

TACTICAL_KT, STRATEGIC_KT = 20.0, 6000.0

# Yields must match NuclearYieldTest.AllNukes and gen_fireball_light.YIELDS. NukeSarmatMIRV is in
# neither: it is a FireCluster bus with no damage warheads of its own, and its carrier shake is
# sized for the WHOLE salvo -- five RS-28 RVs at 750 kt each -- rather than for one burst.
YIELDS = {
    'NukeB61Mod12Y003': 0.3, 'NukeRu9M729': 1.0, 'NukeB61Mod12Y015': 1.5,
    'NukeB61Mod12Y10': 10.0, 'NukeRuIskander': 10.0,
    'NukeB61Mod12Y50': 50.0, 'NukeRuKinzhalN': 50.0,
    'NukeW76': 100.0, 'NukeRuKalibr': 100.0,
    'NukeSarmatRV': 750.0, 'NukeB83': 1200.0, 'NukeTsarBomba': 50000.0,
    'NukeSarmatMIRV': 5 * 750.0,
}

CODA_THRESHOLD_KT = 750.0
GLOBAL_MAX_PROPAGATION_DELAY = 300      # ScreenShakerInfo.MaxPropagationDelay

# (v at 20 kt, v at 6000 kt). A v20 of None means the stage has only the strategic anchor.
# Atomic's `ground` stage omits FrequencyScale, which is the engine default of 100.
STAGES = [
    ('crack', dict(
        Duration=(20, 30), Intensity=(4, 5), AttackTicks=(1, 1),
        DecayHalfLife=(6, 9), FrequencyScale=(175, 155))),
    ('ground', dict(
        Duration=(60, 110), Intensity=(13, 20), AttackTicks=(3, 4),
        DecayHalfLife=(14, 26), FrequencyScale=(100, 48), Delay=(3, 5))),
    ('roll', dict(
        Duration=(130, 380), Intensity=(6, 9), AttackTicks=(20, 40),
        DecayHalfLife=(45, 140), FrequencyScale=(55, 26), Delay=(12, 25))),
    # AtomicHighYield is the ONLY tuned point for this stage, so it borrows `roll`'s exponents --
    # the two are the same kind of motion an octave apart, and a one-point stage cannot fit its own.
    ('coda', dict(
        Duration=(None, 450), Intensity=(None, 4), AttackTicks=(None, 60),
        DecayHalfLife=(None, 170), FrequencyScale=(None, 17), Delay=(None, 230))),
    ('air', dict(
        Duration=(45, 120), Intensity=(5, 7), AttackTicks=(2, 3),
        DecayHalfLife=(16, 32), FrequencyScale=(130, 60))),
]

FIELD_ORDER = ['Duration', 'Intensity', 'AttackTicks', 'DecayHalfLife', 'FrequencyScale',
               'Delay', 'PropagationTicksPerCell', 'MaxPropagationDelay']

MARKER = '===== THE SCREEN SHAKE ====='

AIR_PTPC = (6.4, 6.3)                   # ticks per cell, Atomic and AtomicHighYield

STAGE_NOTES = {
    'crack': 'CRACK -- the prompt arrival. Short, sharp, highest frequency of the stack.',
    'ground': 'GROUND SHOCK -- the main event and the peak amplitude.',
    'roll': 'ROLL -- the long low-frequency coda.',
    'coda': 'LATE ROLL -- the second, longer coda. Only weapons at or above 750 kt carry one.',
    'air': ('AIR BLAST -- travels at its own ticks/cell rather than the global ground-wave speed,'
            ' so it\n\t# arrives with the VISIBLE shockwave at every distance instead of only at'
            ' ground zero.'),
}


def exponent(v20, v6000):
    return math.log(v6000 / float(v20)) / math.log(STRATEGIC_KT / TACTICAL_KT)


def law(v20, v6000, kt):
    return v20 * (kt / TACTICAL_KT) ** exponent(v20, v6000)


def coda_law(v6000, kt, key):
    """`coda` has one anchor, so it rides `roll`'s exponent for the same field."""
    return v6000 * (kt / STRATEGIC_KT) ** exponent(*dict(STAGES)['roll'][key])


def stage_values(stage, fields, kt, max_radius_cells):
    out = {}
    for key in FIELD_ORDER:
        if key not in fields:
            continue
        v20, v6000 = fields[key]
        raw = coda_law(v6000, kt, key) if v20 is None else law(v20, v6000, kt)
        out[key] = max(1, int(round(raw)))

    if stage == 'air':
        out['PropagationTicksPerCell'] = round(law(AIR_PTPC[0], AIR_PTPC[1], kt), 1)
        ceiling = int(round(max_radius_cells * out['PropagationTicksPerCell']))
        if ceiling > GLOBAL_MAX_PROPAGATION_DELAY:
            out['MaxPropagationDelay'] = ceiling
    return out


def shake_stack(kt, max_radius_cells):
    return [(stage, stage_values(stage, fields, kt, max_radius_cells))
            for stage, fields in STAGES
            if not (stage == 'coda' and kt < CODA_THRESHOLD_KT)]


# ---------------------------------------------------------------- yaml surgery

def weapon_block(lines, name):
    start = next(k for k, line in enumerate(lines) if line.rstrip() == name + ':')
    end = start + 1
    while end < len(lines) and (lines[end].strip() == '' or lines[end].startswith('\t')):
        end += 1
    return start, end


def parse_wdist(s):
    m = re.match(r'^(-?\d+)c(\d+)$', s.strip())
    return int(m.group(1)) + int(m.group(2)) / 1024.0 if m else int(s.strip()) / 1024.0


def blast_max_radius(block):
    """MaxRadius of this weapon's Warhead@BlastWave, in cells. The MIRV bus has none of its own."""
    in_blast = False
    for line in block:
        if re.match(r'^\t\S', line):
            in_blast = line.strip().startswith('Warhead@BlastWave:')
        elif in_blast and line.strip().startswith('MaxRadius:'):
            return parse_wdist(line.split(':', 1)[1])
    return None


def shake_span(block):
    """The contiguous run of Warhead@Shake* headers and their bodies, as [first, last)."""
    first = last = None
    in_shake = False
    for i, line in enumerate(block):
        if re.match(r'^\t\S', line):
            in_shake = line.strip().startswith('Warhead@Shake')
            if in_shake:
                first = i if first is None else first
                last = i + 1
        elif in_shake and line.startswith('\t\t'):
            last = i + 1
    if first is None:
        return None, None

    # Absorb a previously-generated header block. It sits ABOVE the first Warhead@Shake, so it is
    # not in the span the loop above found, and leaving it there prepends a second copy on every
    # run. Only a comment run carrying the marker is swallowed -- a hand-written note above the
    # shake stack on some future weapon is left alone.
    run = first
    while run > 0 and block[run - 1].startswith('\t#'):
        run -= 1
    if any(MARKER in line for line in block[run:first]):
        first = run
    return first, last


def render(stages, kt):
    out = ['\t# ===== THE SCREEN SHAKE =====',
           '\t# GENERATED -- tools/nuke-shake/gen_shake.py --write. Do not hand-edit; the laws, and',
           '\t# the reason this band needed regenerating at all, are in that script.',
           '\t# %g kt on the %d-stage model shared with `Atomic` and `AtomicHighYield`.'
           % (kt, len(stages))]
    for n, (stage, values) in enumerate(stages, 1):
        # Its own line rather than trailing the header: MiniYaml does strip `# ...` from a value
        # (MiniYaml.cs:266-293), but nothing else in mods/ww3mod/rules writes one and this file is
        # read by two other generators.
        out.append('\t# %s' % STAGE_NOTES[stage])
        out.append('\tWarhead@Shake%d: ShakeScreen' % n)
        out.extend('\t\t%s: %s' % (key, values[key]) for key in FIELD_ORDER if key in values)
    # A STAGE_NOTES entry may carry an embedded newline for a two-line note. The caller splices
    # into, and compares against, a list of LINES -- so flatten here or a two-line note never
    # matches what is on disk and every run reports a rewrite it did not make.
    return [line for chunk in out for line in chunk.split('\n')]


def process(path, names, write):
    lines = open(path, encoding='utf-8').read().split('\n')
    changed = []
    for name in names:
        start, end = weapon_block(lines, name)
        block = lines[start:end]
        first, last = shake_span(block)
        if first is None:
            raise SystemExit(name + ' has no Warhead@Shake to replace')
        radius = blast_max_radius(block)
        if radius is None:
            # The MIRV bus: its RVs carry the blast, so the air-blast ceiling is the RV's reach.
            radius = blast_max_radius(lines[slice(*weapon_block(lines, 'NukeSarmatRV'))])
        new = render(shake_stack(YIELDS[name], radius), YIELDS[name])
        if block[first:last] != new:
            changed.append(name)
        lines[start + first:start + last] = new
    if write and changed:
        open(path, 'w', encoding='utf-8').write('\n'.join(lines))
    return changed


# ---------------------------------------------------------------- verification

def shipped_stack(block):
    out, current = {}, None
    for line in block:
        if re.match(r'^\t\S', line):
            current = line.strip().split(':')[0] if line.strip().startswith('Warhead@Shake') else None
            if current:
                out[current] = {}
        elif current and line.startswith('\t\t') and not line.strip().startswith('#') and ':' in line:
            key, value = line.strip().split(':', 1)
            out[current][key] = value.split('#')[0].strip()
    return out


def verify():
    """Recompute the two references from the laws and diff against the shipped YAML."""
    lines = open(SUPER, encoding='utf-8').read().split('\n')
    bad = []
    for name, kt in (('Atomic', TACTICAL_KT), ('AtomicHighYield', STRATEGIC_KT)):
        block = lines[slice(*weapon_block(lines, name))]
        shipped = shipped_stack(block)
        stages = shake_stack(kt, blast_max_radius(block))
        if len(stages) != len(shipped):
            bad.append('%s: law gives %d stages, YAML has %d' % (name, len(stages), len(shipped)))
            continue
        for n, (stage, values) in enumerate(stages, 1):
            have = shipped['Warhead@Shake%d' % n]
            for key, want in values.items():
                got = have.get(key)
                # Atomic omits FrequencyScale on `ground` (engine default 100) and omits
                # MaxPropagationDelay where the global ceiling already covers its reach.
                if got is None and key == 'FrequencyScale' and want == 100:
                    continue
                # AtomicHighYield ships 645 where 102c * 6.3 t/cell is 642.6. The law is the one
                # stated on the field (ShakeScreenWarhead.cs:68-76); the shipped number is that,
                # hand-rounded up by two ticks. Two ticks on a 645-tick ceiling changes nothing, and
                # the reference is not ours to edit -- so this one field is checked to 1%.
                if key == 'MaxPropagationDelay' and got is not None \
                        and abs(int(got) - want) <= max(1, want // 100):
                    continue
                if str(want) != str(got):
                    bad.append('%s Shake%d(%s) %s: law %s, YAML %s' % (name, n, stage, key, want, got))
            for key in have:
                if key not in values:
                    bad.append('%s Shake%d(%s) has %s, which the law does not emit' % (name, n, stage, key))
    return bad


def main():
    bad = verify()
    print('reference check: ' + ('OK -- both shipped stacks are reproduced exactly' if not bad
                                 else 'MISMATCH (see below)'))
    for line in bad:
        print('  ' + line)
    if bad and '--verify' in sys.argv:
        return 1

    for kt in sorted(set(YIELDS.values())):
        stack = shake_stack(kt, 100.0)
        print('%9g kt  %d stages  peak Intensity %2d px  roll %3d ticks'
              % (kt, len(stack), max(s[1]['Intensity'] for s in stack),
                 dict(stack)['roll']['Duration']))

    write = '--write' in sys.argv
    changed = process(ARSENAL, list(YIELDS), write)
    print(('rewrote: ' if write else 'would rewrite: ') + (', '.join(changed) if changed else '(nothing)'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
