#!/usr/bin/env python3
"""Hit rate, not survival: what fraction of ATGMs actually damage what they were
fired at, and what fraction kill it?

`analyze-javelin-probe.py` answers a different question. It was written to hunt a
missile that misses and STAYS IN THE WORLD, so every one of its three signatures
is a filter on flight outcome (`flystraight_latches`, `end_tick`, `facing_span`)
and it reports `damage` only incidentally. The measured answer to that question
came back a flat zero across 556 flights.

The question that is actually open is the one the user asked: missiles connect
with humvees less often than expected, so a mass of humvees may overrun an AT
screen. That is a RATE, and the trace already carries everything needed to
measure it -- `damage_to_target` is written per missile (MissileTrace.cs:435) and
`min_dist` records the closest true approach (MissileTrace.cs:415). Nothing new
has to be recorded; it only has to be read differently.

Run it against any `result.missiles.jsonl`, including the ones the four existing
javelin scenarios already produce:

    tools/autotest/analyze-atgm-hit-rate.py <run-dir-or-jsonl> [--weapon ATGM]

It groups by (weapon, target actor type) so a run containing several weapons or
several target types splits cleanly, and it prints the distribution of closest
approach alongside the rate, because a rate on its own cannot distinguish "the
missile arrived and the warhead did nothing" from "the missile never got there".

Cross-check target: `tools/combat-sim/scripts/atgm-terminal-hit-rate.py`
simulates these same quantities from the shipped rules with no game running. If
the measured `killed %` here and the simulated one there disagree by more than a
few points, the simulation is wrong and its conclusions should be dropped.
"""
import argparse
import json
import os
import sys
from collections import defaultdict

HUMVEE_HP = 4000        # vehicles-america.yaml:57


def load(path):
    if os.path.isdir(path):
        hits = [os.path.join(path, f) for f in os.listdir(path)
                if f.endswith(".missiles.jsonl")]
        if not hits:
            sys.exit(f"no *.missiles.jsonl under {path}")
        path = hits[0]
    recs = []
    with open(path) as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                o = json.loads(line)
            except json.JSONDecodeError:
                continue
            if o.get("ev") == "m":
                recs.append(o)
    return path, recs


def pct(a, b):
    return 100.0 * a / b if b else 0.0


def quantiles(vals):
    if not vals:
        return (0, 0, 0)
    v = sorted(vals)
    return (v[len(v) // 4], v[len(v) // 2], v[(3 * len(v)) // 4])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("path", help="run directory or result.missiles.jsonl")
    ap.add_argument("--weapon", default=None, help="filter to one weapon name")
    ap.add_argument("--lethal", type=int, default=HUMVEE_HP,
                    help="damage counted as a kill (default humvee 4000 HP)")
    ap.add_argument("--by-launcher", action="store_true",
                    help="group by launcher CELL instead of by target type. In a "
                         "multi-lane rig each lane has its own launcher, so this "
                         "is how a per-condition rate is read out.")
    args = ap.parse_args()

    src, recs = load(args.path)
    if args.weapon:
        recs = [r for r in recs if r.get("weapon") == args.weapon]
    if not recs:
        sys.exit("no missile summary records matched")

    print(f"{src}\n{len(recs)} missile records"
          + (f", weapon={args.weapon}" if args.weapon else ""))

    groups = defaultdict(list)
    for r in recs:
        if args.by_launcher:
            lp = r.get("launch_pos") or [0, 0, 0]
            key = (r.get("weapon", "?"), f"cell {lp[0] // 1024},{lp[1] // 1024}")
        else:
            key = (r.get("weapon", "?"), r.get("target", "?"))
        groups[key].append(r)

    label = "launcher" if args.by_launcher else "target"
    hdr = (f"\n{'weapon':<18} {label:<14} {'n':>5} {'armed':>7} {'landed':>7} "
           f"{'killed':>7} {'per kill':>9} {'min_dist p25/50/75':>20}")
    print(hdr)
    print("-" * (len(hdr) - 1))
    for (w, t), rs in sorted(groups.items()):
        n = len(rs)
        armed = sum(1 for r in rs if r.get("armed"))
        # damage_to_target is the subset of `damage` credited to the actor the
        # missile was launched at -- splash onto a neighbour must not count.
        landed = sum(1 for r in rs if r.get("damage_to_target", 0) > 0)
        killed = sum(1 for r in rs if r.get("damage_to_target", 0) >= args.lethal)
        mds = [r["min_dist"] for r in rs if r.get("min_dist", -1) >= 0]
        q = quantiles(mds)
        per = f"{n / killed:.2f}" if killed else "inf"
        print(f"{w:<18} {t:<14} {n:>5} {pct(armed, n):>6.1f}% "
              f"{pct(landed, n):>6.1f}% {pct(killed, n):>6.1f}% {per:>9} "
              f"{q[0]:>6}/{q[1]:>5}/{q[2]:>6}")

    print("\nEnd reason x outcome -- where the misses go:")
    for (w, t), rs in sorted(groups.items()):
        by = defaultdict(lambda: [0, 0])
        for r in rs:
            slot = by[r.get("reason", "?")]
            slot[0] += 1
            if r.get("damage_to_target", 0) > 0:
                slot[1] += 1
        parts = ", ".join(
            f"{k} {v[0]} ({pct(v[1], v[0]):.0f}% landed)"
            for k, v in sorted(by.items(), key=lambda kv: -kv[1][0]))
        print(f"  {w}/{t}: {parts}")

    print("\nDamage histogram (damage_to_target), all groups:")
    buckets = [(0, 0), (1, 999), (1000, 1999), (2000, 3999),
               (4000, 9999), (10000, 10**9)]
    for lo, hi in buckets:
        c = sum(1 for r in recs
                if lo <= r.get("damage_to_target", 0) <= hi)
        label = "0 (clean miss)" if hi == 0 else f"{lo}-{hi}"
        bar = "#" * int(60 * c / len(recs))
        print(f"  {label:>16} {c:>5} {pct(c, len(recs)):>6.1f}% {bar}")

    zero = sum(1 for r in recs if r.get("damage_to_target", 0) == 0)
    arrived = sum(1 for r in recs
                  if r.get("damage_to_target", 0) == 0
                  and 0 <= r.get("min_dist", -1) < 600)
    print(f"\n{zero} missiles did no damage to their target; {arrived} of those "
          f"passed within 600 wdist of it.")
    print("A high second number means the warhead is the problem, not the "
          "guidance:\nthe missile arrived and the impact still fell outside the "
          "hitshape.")


if __name__ == "__main__":
    main()
