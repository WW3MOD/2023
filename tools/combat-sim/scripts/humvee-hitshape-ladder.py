#!/usr/bin/env python3
"""Humvee hitshape ladder: what does widening the cross-axis buy against the
shipped ATGM (Inaccuracy 512 / Absolute)?

Replicates the engine's scatter exactly, not an approximation of it:

  Missile.cs:324-325   offset = WVec.FromPDF(rng, 2) * maxInaccuracyOffset / 1024
  WVec.cs:105-108      FromPDF(r, n) -> (WDist.FromPDF, WDist.FromPDF, 0)
  WDist.cs:56-60       FromPDF(r, n) = sum(n * r.Next(-1024, 1024)) / n   [int division]
  Util.cs:401-415      Absolute -> the raw value, independent of range

So each of X and Y is an INDEPENDENT triangular variate on [-512, 511], and the
2-D cloud is square-cornered rather than radial. That matters: the hit
probability depends on the humvee's facing relative to the world axes, so this
averages over all 256 facings and also reports the extremes.

Hit model: the missile detonates at the aim point and TargetDamage (Spread 1wd)
lands iff that point is inside the hitshape rectangle. One landed ATGM does
10250 to a humvee, which is lethal at both 8000 and 4000 HP, so
P(hit) == P(kill) and expected missiles-to-kill is 1 / P(hit).

No game launch; this is arithmetic over the shipped rules.
"""
import math
import random

TRIALS = 400_000
LENGTH = 1000                      # long axis, unchanged by this work
LADDER = [
    (440, "current, shipped"),
    (470, "SHIPPED BY THIS COMMIT"),
    (480, "himars / grad — ties the next-smallest, NOT shipped"),
    (540, "m113 — NOT shipped"),
    (580, "btr — NOT shipped"),
]


def pdf2(rng):
    """WDist.FromPDF(r, 2) — note int division truncates toward zero in C#."""
    s = rng.randrange(-1024, 1024) + rng.randrange(-1024, 1024)
    return int(s / 2)


def sample_offset(rng, inaccuracy):
    x, y = pdf2(rng), pdf2(rng)
    return int(x * inaccuracy / 1024), int(y * inaccuracy / 1024)


def hit_fraction(half_w, half_l, inaccuracy, facings, trials, seed):
    """Fraction of shots whose aim point falls inside the rotated rectangle."""
    rng = random.Random(seed)
    hits = 0
    per = max(1, trials // len(facings))
    for f in facings:
        a = 2 * math.pi * f / 256.0
        ca, sa = math.cos(a), math.sin(a)
        for _ in range(per):
            ox, oy = sample_offset(rng, inaccuracy)
            # rotate world offset into the actor's local frame
            lx = ox * ca + oy * sa
            ly = -ox * sa + oy * ca
            if abs(lx) <= half_w and abs(ly) <= half_l:
                hits += 1
    return hits / (per * len(facings))


def main():
    all_facings = list(range(0, 256, 8))
    print(f"ATGM Inaccuracy 512 Absolute — range-independent (Util.cs:411-412), "
          f"so these hold at every engagement range.\n"
          f"{TRIALS:,} trials, averaged over {len(all_facings)} facings, length {LENGTH} fixed.\n")

    hdr = f"{'width':>7}  {'miss %':>8}  {'hit %':>7}  {'missiles/kill':>14}  {'vs 440':>8}   note"
    print(hdr)
    print("-" * (len(hdr) + 18))
    base = None
    for w, note in LADDER:
        p = hit_fraction(w // 2, LENGTH // 2, 512, all_facings, TRIALS, 12345)
        if base is None:
            base = p
        mtk = 1.0 / p if p else float("inf")
        print(f"{w:>7}  {100*(1-p):>7.1f}%  {100*p:>6.1f}%  {mtk:>14.2f}  "
              f"{100*(p/base-1):>+7.1f}%   {note}")

    print("\nFacing sensitivity (the scatter cloud is square, not round):")
    for w in (440, 470):
        best = max(hit_fraction(w // 2, LENGTH // 2, 512, [f], TRIALS // 12, 999)
                   for f in range(0, 64, 8))
        worst = min(hit_fraction(w // 2, LENGTH // 2, 512, [f], TRIALS // 12, 999)
                    for f in range(0, 64, 8))
        print(f"  width {w}: hit rate {100*worst:.1f}% (worst facing) .. {100*best:.1f}% (best facing)")

    print("\nRPG compounding — Bullet, Inaccuracy 1c0, InaccuracyType Maximum")
    print("(Bullet.cs:40 defaults to Maximum, so scatter scales with range/12c0):")
    for cells in (4, 6, 8, 10):
        inacc = 1024 * (cells * 1024) // 12288
        row = []
        for w in (440, 470):
            p = hit_fraction(w // 2, LENGTH // 2, inacc, all_facings, TRIALS // 2, 777)
            row.append(p)
        print(f"  at {cells:>2}c0 (scatter {inacc:>4}): hit {100*row[0]:.1f}% -> {100*row[1]:.1f}%  "
              f"({100*(row[1]/row[0]-1):+.1f}%), RPGs/kill {1/row[0]:.2f} -> {1/row[1]:.2f}")


if __name__ == "__main__":
    main()
