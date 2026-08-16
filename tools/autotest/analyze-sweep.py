#!/usr/bin/env python3
"""Range-vs-outcome table for a MissileTrace summary stream (I1 invariance)."""
import json
import sys
from collections import defaultdict


def load(path):
    out = []
    with open(path) as fh:
        for line in fh:
            line = line.strip()
            if line and json.loads(line).get("ev") == "m":
                out.append(json.loads(line))
    return out


def main(path):
    recs = load(path)
    print(f"# {path}\nmissiles: {len(recs)}\n")

    by = defaultdict(list)
    for r in recs:
        # lane range in cells, rounded from the launch geometry itself
        cells = round(r["launch_hor_range"] / 1024)
        by[(r["weapon"], cells)].append(r)

    for weapon in sorted({r["weapon"] for r in recs}):
        print(f"== {weapon} ==")
        print(f"{'cells':>6}{'n':>5}{'hit%':>7}{'near%':>7}{'medDmg':>8}{'meanDmg':>9}"
              f"{'medMin':>8}{'latch%':>8}{'fuelOut':>8}   reasons")
        rows = sorted(k for k in by if k[0] == weapon)
        for key in rows:
            ss = by[key]
            n = len(ss)
            dmg = sorted(s["damage_to_target"] for s in ss)
            mind = sorted(s["min_dist"] for s in ss)
            hit = sum(1 for s in ss if s["damage_to_target"] > 0)
            near = sum(1 for s in ss if s["min_dist"] <= 512)
            latch = sum(1 for s in ss if s["flystraight_tick"] >= 0)
            fo = sum(1 for s in ss if s["reason"] == "fuel_out")
            rc = defaultdict(int)
            for s in ss:
                rc[s["reason"]] += 1
            print(f"{key[1]:>6}{n:>5}{100*hit//n:>6}%{100*near//n:>6}%{dmg[n//2]:>8}"
                  f"{sum(dmg)//n:>9}{mind[n//2]:>8}{100*latch//n:>7}%{fo:>8}   {dict(rc)}")
        print()

    print("== end-altitude bucket (settles how often a detonation renders as 'air') ==")
    b = defaultdict(int)
    for r in recs:
        b[(r["weapon"], r["end_dat_bucket"])] += 1
    for k in sorted(b):
        print(f"  {k[0]:<8}{k[1]:<12}{b[k]:>5}")
    thr = {r["weapon"]: r["air_threshold"] for r in recs}
    print("  air_threshold per weapon:", thr)
    print()

    print("== outcome / termination census ==")
    oc, rc = defaultdict(int), defaultdict(int)
    for r in recs:
        oc[r["outcome"]] += 1
        rc[(r["weapon"], r["reason"])] += 1
    print("  outcome:", dict(oc))
    for k in sorted(rc):
        print(f"  {k[0]:<8}{k[1]:<16}{rc[k]:>5}")
    print()

    print("== damage vs closest approach (Measurement 4) ==")
    for weapon in sorted({r["weapon"] for r in recs}):
        ss = [r for r in recs if r["weapon"] == weapon]
        print(f"  -- {weapon} (air_threshold {ss[0]['air_threshold']}, "
              f"unattributed={sum(1 for s in ss if s['damage_unattributed'])}/{len(ss)}) --")
        buckets = [(0, 64), (64, 128), (128, 256), (256, 384), (384, 512),
                   (512, 768), (768, 1024), (1024, 1 << 30)]
        print(f"    {'minDist':>14}{'n':>5}{'meanDmgTgt':>12}{'maxDmg':>9}{'minDmg':>9}")
        for lo, hi in buckets:
            sel = [s for s in ss if lo <= s["min_dist"] < hi]
            if not sel:
                continue
            d = [s["damage_to_target"] for s in sel]
            label = f"{lo}-{hi if hi < (1 << 30) else 'inf'}"
            print(f"    {label:>14}{len(sel):>5}{sum(d)//len(d):>12}{max(d):>9}{min(d):>9}")
        print()


if __name__ == "__main__":
    main(sys.argv[1])
