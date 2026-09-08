#!/usr/bin/env python3
"""Rewrite the three flat LeaveSmudge discs on each nuclear weapon into five
concentric annuli.

WHAT IS WRONG TODAY. Every nuke carries Warhead@Crater, @Scorch1 and @Scorch2,
each with a single `Size: N`, and a single value means a FILLED DISC of radius
N (LeaveSmudgeWarhead.cs:53 -- minRange is 0 unless Size has a second value).
So the three discs are NESTED, not adjacent: on NukeTsarBomba, Crater 12 sits
entirely inside Scorch1 20, which sits entirely inside Scorch2 34. Every cell
within 12 gets three smudges, and SmudgeLayer.AddSmudge picks a RANDOM variant
each time (SmudgeLayer.cs:176). The result has no radial structure at all --
it is uniform noise with a square-ish rim, which is the thing the user asked to
be rid of.

WHAT THIS WRITES. Five bands, inner to outer, as true annuli (`Size: outer,
inner`), each with its own smudge type and its own art density:

    ScarCore    0.00 - 0.15 R    78-93% coverage, darkest
    ScarCrater  0.15 - 0.35 R    62-80%
    ScarChar    0.35 - 0.58 R    46-64%
    ScarBurn    0.58 - 0.80 R    30-48%
    ScarRim     0.80 - 1.00 R    14-30%, sparse -- this is the soft rim

All five are NEW types with generated art. The stock Crater and Scorch types
are deliberately left alone: an earlier cut reused them for two of the middle
rings and the gradient inverted, because stock crater art covers only 4-40% of
its cell and stock scorch art varies 20-46% between variants. Every non-nuclear
weapon that leaves a Crater or a Scorch is unaffected by any of this.

The fractions are not invented. They were chosen because they REPRODUCE the
radii already tuned in this file: on Tsar Bomba they give Crater 12 and Charred
20 against the existing Crater 12 and Scorch1 20, and the same holds at B83
(7/12), Sarmat RV (6/10), W76 (3/5) and B61-50 (2/4). The existing tuning
already described these contours; it just drew them as fills.

DELAYS ARE INTERPOLATED, NOT COPIED. In this tree a smudge Delay is the tick
the blast wavefront reaches that radius -- weapons-superweapons.yaml:600-601
and :1240-1241 both say so explicitly, and AtomicHighYield's 34/102/211 over
18/31/47 is a decelerating wave, not a flat number. Each new band's delay is
interpolated piecewise-linearly through the weapon's own existing (radius,
delay) points, so the scar still paints outward at the weapon's own wave speed.

Bands that would be empty at small yields are dropped rather than clamped: a
0.3 kt weapon whose whole scar is one cell gets two bands, not five rings
stacked on the same cell.

Usage:  python rewire_bands.py [--check]
"""
import argparse
import os
import re
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

TARGETS = [
    "mods/ww3mod/rules/weapons/weapons-nuclear-arsenal.yaml",
    "mods/ww3mod/rules/weapons/weapons-superweapons.yaml",
]

# Only weapons that model a nuclear-scale burst. MOPPenetration (a bunker
# buster) and EmpBomb keep their single small smudge -- a conventional
# penetrator has no thermal ring to draw.
ELIGIBLE = re.compile(r"^(Nuke\w+|Atomic|AtomicHighYield):$")

# (band, smudge type, outer radius as a fraction of the weapon's own outermost)
BANDS = [
    ("Scar1Core",   "ScarCore",   0.15),
    ("Scar2Crater", "ScarCrater", 0.35),
    ("Scar3Char",   "ScarChar",   0.58),
    ("Scar4Burn",   "ScarBurn",   0.80),
    ("Scar5Rim",    "ScarRim",    1.00),
]

BLOCK = re.compile(
    r"\tWarhead@(Crater|Scorch1|Scorch2): LeaveSmudge\n"
    r"((?:\t\t[^\n]*\n)+)")


def parse_fields(body):
    out = {}
    for line in body.splitlines():
        k, _, v = line.strip().partition(":")
        out[k.strip()] = v.strip()
    return out


def interp_delay(r, anchors):
    """Piecewise-linear through the weapon's own (radius, delay) points, with
    linear extrapolation off both ends. Anchors are sorted by radius."""
    if len(anchors) == 1:
        return anchors[0][1]
    if r <= anchors[0][0]:
        (r0, d0), (r1, d1) = anchors[0], anchors[1]
    elif r >= anchors[-1][0]:
        (r0, d0), (r1, d1) = anchors[-2], anchors[-1]
    else:
        for i in range(len(anchors) - 1):
            if anchors[i][0] <= r <= anchors[i + 1][0]:
                (r0, d0), (r1, d1) = anchors[i], anchors[i + 1]
                break
    if r1 == r0:
        return d0
    return max(0, int(round(d0 + (d1 - d0) * (r - r0) / (r1 - r0))))


def rewrite_weapon(name, text, report):
    blocks = list(BLOCK.finditer(text))
    if len(blocks) != 3:
        return text, False

    parsed = [parse_fields(m.group(2)) for m in blocks]
    sizes = [int(p["Size"].split(",")[0]) for p in parsed]
    delays = [int(p.get("Delay", 0)) for p in parsed]
    anchors = sorted(zip(sizes, delays))
    outer_r = max(sizes)

    # Everything except Size/SmudgeType/Delay/Chance is carried through
    # unchanged -- InvalidTargets and AirThreshold especially, since
    # AirThreshold is load-bearing (a warhead above it does nothing at all,
    # silently: see the TRAPS block at weapons-nuclear-arsenal.yaml:127-132).
    carried = {k: v for k, v in parsed[0].items()
               if k not in ("Size", "SmudgeType", "Delay", "Chance")}

    emitted = []
    next_inner = 0
    for key, smudge, frac in BANDS:
        outer = int(round(frac * outer_r))
        if outer < next_inner:
            continue                      # band has no cells left at this yield
        emitted.append((key, smudge, outer, next_inner))
        next_inner = outer + 1

    # `Chance` gates the WHOLE warhead, not each cell (LeaveSmudgeWarhead.cs:41-43),
    # so it is a coin flip on whether that entire ring exists. Only `Atomic`
    # carries one (60, on its outermost scorch). It is preserved here, on the
    # outermost band, rather than dropped: banding is a rendering change and has
    # no business quietly retuning a balanced power. Note though that it now
    # gates the soft rim, so 40% of Atomic strikes will end at the hard Scorch
    # edge -- worth revisiting, but that is the user's call, not this script's.
    outermost = max(range(len(emitted)), key=lambda i: emitted[i][2])
    chance = next((p["Chance"] for p, s in zip(parsed, sizes)
                   if "Chance" in p and s == outer_r), None)

    lines = []
    for idx, (key, smudge, outer, inner) in enumerate(emitted):
        size = str(outer) if inner == 0 else f"{outer}, {inner}"
        lines.append(f"\tWarhead@{key}: LeaveSmudge\n")
        lines.append(f"\t\tSize: {size}\n")
        lines.append(f"\t\tSmudgeType: {smudge}\n")
        lines.append(f"\t\tDelay: {interp_delay(outer, anchors)}\n")
        if chance is not None and idx == outermost:
            lines.append(f"\t\tChance: {chance}\n")
        for k, v in carried.items():
            lines.append(f"\t\t{k}: {v}\n")
    new_block = "".join(lines)

    start, end = blocks[0].start(), blocks[-1].end()
    report.append((name, sizes, [(k, o, i) for k, _s, o, i in emitted], chance))
    return text[:start] + new_block + text[end:], True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="report what would change; write nothing")
    args = ap.parse_args()

    report = []
    for rel in TARGETS:
        path = os.path.join(REPO, rel)
        src = open(path, encoding="utf-8").read()

        # Split on top-level weapon definitions so each weapon is rewritten in
        # isolation and a weapon with no smudge warheads is left byte-identical.
        parts = re.split(r"(?m)^(?=[A-Za-z][A-Za-z0-9]*:$)", src)
        out = []
        for part in parts:
            head = part.split("\n", 1)[0]
            if ELIGIBLE.match(head):
                part, _changed = rewrite_weapon(head[:-1], part, report)
            out.append(part)
        new = "".join(out)

        if not args.check and new != src:
            open(path, "w", encoding="utf-8", newline="").write(new)
        print(f"{'CHECK' if args.check else 'WROTE'} {rel}"
              f"{'  (unchanged)' if new == src else ''}")

    print()
    print(f"{'weapon':<20} {'was (r)':<14} bands as (outer..inner)")
    for name, sizes, bands, chance in report:
        desc = "  ".join(f"{k[5:]}:{i}-{o}" for k, o, i in bands)
        print(f"{name:<20} {str(sizes):<14} {desc}" + (f"   [Chance {chance} kept on outer band]" if chance else ""))
    print(f"\n{len(report)} weapons rebanded")
    return 0


if __name__ == "__main__":
    sys.exit(main())
