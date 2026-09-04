"""Evaluate the old and new accrual curves over the real roster. python WORKSPACE/mockups/curve_check.py"""

import json
import math
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TPS = 1000 / 60  # mod.yaml GameSpeeds default: Timestep 60ms


def isqrt(n):
    return math.isqrt(n)


def old_interval(t, tier, mult=500, higher=300):
    v = max(1, t) * mult // 100
    for _ in range(tier - 1):
        v = v * higher // 100
    return max(1, v)


def new_interval(t, tier, base=2400, ref=100, mult=2700, cap=9000, higher=300):
    v = base + isqrt(max(1, t) * ref) * mult // 100
    v = min(v, cap)
    for _ in range(tier - 1):
        v = v * higher // 100
    return max(1, v)


def mmss(ticks):
    s = ticks / TPS
    return f"{int(s // 60)}m{int(s % 60):02d}s"


def main():
    out = subprocess.run([sys.executable, str(ROOT / "WORKSPACE/mockups/roster_dump.py")],
                         capture_output=True, text=True, cwd=ROOT, check=True)
    roster = json.loads(out.stdout)

    # Collapse .america/.russia faction clones onto their base type.
    seen, uniq = set(), []
    for r in roster:
        key = r["name"].split(".")[0]
        if key in seen:
            continue
        seen.add(key)
        uniq.append(r)

    print(f"{'unit':<14}{'cost':>6}{'T':>5} | {'OLD r1':>9}{'r3':>10} | {'NEW r1':>9}{'r2':>10}{'r3':>10}")
    print("-" * 78)
    for r in uniq:
        t = r["buildTicks"]
        print(f"{r['label'][:13]:<14}{r['cost']:>6}{t:>5} | "
              f"{mmss(old_interval(t,1)):>9}{mmss(old_interval(t,3)):>10} | "
              f"{mmss(new_interval(t,1)):>9}{mmss(new_interval(t,2)):>10}{mmss(new_interval(t,3)):>10}")

    ts = [r["buildTicks"] for r in uniq]
    lo, hi = min(ts), max(ts)
    for tier in (1, 2, 3):
        o = old_interval(hi, tier) / old_interval(lo, tier)
        n = new_interval(hi, tier) / new_interval(lo, tier)
        print(f"tier {tier}: old spread {o:.1f}:1   new spread {n:.2f}:1")
    print(f"\ncheapest r1 = {new_interval(lo,1)} ticks = {mmss(new_interval(lo,1))}")
    print(f"dearest  r1 = {new_interval(hi,1)} ticks = {mmss(new_interval(hi,1))}")

    # Monotonicity over every build time the engine can hand us.
    prev = 0
    for t in range(1, 5001):
        v = new_interval(t, 1)
        assert v >= prev, t
        prev = v
    print("monotonic over T=1..5000: ok")


if __name__ == "__main__":
    main()
