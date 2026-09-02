#!/usr/bin/env python3
"""Read ProximityPerf JSONL captures and apply a PRE-REGISTERED verdict rule.

    ./tools/autotest/analyze-proximity-perf.py CONTROL.jsonl [TREATMENT.jsonl]

WHAT IS BEING PRICED
--------------------
ProximityExternalCondition.Tick compares `self.CenterPosition` against a
`cachedPosition` stored as `self.CenterPosition + Info.Offset`. For any actor
with a non-zero Offset the comparison is true on every tick forever, so the
trigger is re-indexed across its ActorMap bins and its actor set rebuilt every
tick, permanently. The offset-carrying population on woodland-warfare-ww3 is
583 tree clumps (TC01-TC05).

THE THRESHOLD IS FIXED HERE, IN COMMITTED CODE, BEFORE ANY NUMBER EXISTS.
Editing these constants after seeing a capture is the one thing that would make
this whole exercise worthless.

  INVISIBLE_PCT = 1.0   under 1% of the 40 ms tick budget -> invisible, leave it
  VISIBLE_PCT   = 5.0   over 5% -> visible, worth fixing
                        between the two -> real but minor

WHY 1% AND 5%, ARGUED BEFORE THE FACT
  1%  (0.4 ms/tick). Frame-to-frame variance on a desktop OS from GC, scheduler
      preemption and render jitter routinely swings tick cost by 5-20%. A cost
      below 1% of budget cannot be perceived by a player and cannot be
      separated from that jitter by a single capture, so claiming it as an
      improvement would be claiming something this instrument cannot see.
  5%  (2 ms/tick). This is roughly where a fixed per-tick cost starts eating
      the headroom that keeps a 25 tps simulation inside its 40 ms deadline on
      a loaded machine — i.e. where it stops being an accounting entry and
      starts being able to produce a visible hitch.

The denominator is the FIXED 40 ms budget, not the observed tick_time. Observed
tick_time in this capture is small (nothing moves), and dividing by it would
flatter the defect by shrinking the denominator. The budget is the constraint
the game actually has to meet.

READ THE COUNTERS BEFORE THE TIMINGS. `updates` is a falsification test of the
whole premise: the static reading predicts ~583/tick sustained. If it reads ~0
the mechanism claim is wrong and no timing here means anything. If it reads
~1200 then the zero-Offset trees are firing too and the diagnosis is incomplete.
"""

import json
import sys

TICK_BUDGET_MS = 40.0        # 25 ticks per second
INVISIBLE_PCT = 1.0
VISIBLE_PCT = 5.0

PREDICTED_UPDATES = 583      # TC01-TC05 on woodland-warfare-ww3
WARMUP_TICKS = 250           # discard load/JIT settling


def load(path):
    rows = []
    with open(path) as fh:
        for line in fh:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    if not rows:
        sys.exit(f"{path}: no records — the capture wrote nothing.")
    return rows


def summarize(path, rows):
    kept = [r for r in rows if r["tick"] >= WARMUP_TICKS]
    if not kept:
        sys.exit(f"{path}: every record is inside the {WARMUP_TICKS}-tick warmup.")

    n = len(kept)

    def mean(key):
        return sum(r[key] for r in kept) / n

    updates = mean("updates")
    rebuilds = mean("rebuilds")
    samples = sum(r["reindex_samples"] for r in kept)
    sample_us = sum(r["reindex_sample_us"] for r in kept)

    # Reconstruct the re-index total from the sampled mean. Emitted separately
    # from rebuild_us because it is the weaker of the two numbers: it is an
    # estimate scaled by ~64, while rebuild_us is measured directly.
    per_update_us = (sample_us / samples) if samples else 0.0
    reindex_us = per_update_us * updates
    rebuild_us = mean("rebuild_us")
    total_us = reindex_us + rebuild_us

    return {
        "path": path,
        "ticks": n,
        "triggers": mean("triggers"),
        "updates": updates,
        "rebuilds": rebuilds,
        "bin_visits": mean("bin_visits"),
        "bin_scan": mean("bin_scan"),
        "matched": mean("matched"),
        "reindex_samples": samples,
        "per_update_us": per_update_us,
        "reindex_us": reindex_us,
        "rebuild_us": rebuild_us,
        "total_us": total_us,
        "pct_budget": 100.0 * total_us / 1000.0 / TICK_BUDGET_MS,
        "tick_time_ms": mean("tick_time_ms"),
    }


def report(s):
    print(f"\n=== {s['path']}  ({s['ticks']} ticks after warmup) ===")
    print(f"  live proximity triggers   {s['triggers']:10.1f}")
    print(f"  updates      / tick       {s['updates']:10.1f}   <- cache-miss re-indexes")
    print(f"  rebuilds     / tick       {s['rebuilds']:10.1f}   <- ActorsInBox + 2 HashSet rebuilds")
    print(f"  bin visits   / tick       {s['bin_visits']:10.1f}")
    print(f"  bin scan len / tick       {s['bin_scan']:10.1f}   <- List.Remove linear-scan work")
    print(f"  matched actors / tick     {s['matched']:10.1f}")
    print(f"  re-index samples (total)  {s['reindex_samples']:10d}")
    print(f"  per-update cost           {s['per_update_us']:10.3f} us  (sampled)")
    print(f"  re-index  / tick          {s['reindex_us']:10.1f} us  (estimated = per-update x updates)")
    print(f"  rebuild   / tick          {s['rebuild_us']:10.1f} us  (measured directly)")
    print(f"  TOTAL     / tick          {s['total_us']:10.1f} us  = {s['pct_budget']:.3f}% of the {TICK_BUDGET_MS:.0f} ms budget")
    print(f"  engine tick_time          {s['tick_time_ms']:10.2f} ms  (context only, NOT the denominator)")


def check_premise(s):
    u = s["updates"]
    print("\n--- premise check (read this before any timing) ---")
    if u < 1:
        print(f"  FALSIFIED: updates/tick = {u:.1f}, not ~{PREDICTED_UPDATES}. The trigger is NOT")
        print("  firing every tick. The static reading is wrong; discard the timings.")
        return False
    if u > PREDICTED_UPDATES * 1.5:
        print(f"  UNEXPECTED: updates/tick = {u:.1f}, well above the predicted ~{PREDICTED_UPDATES}.")
        print("  Zero-Offset actors appear to be firing too — the diagnosis is incomplete.")
        return True
    print(f"  CONFIRMED: updates/tick = {u:.1f} against a predicted ~{PREDICTED_UPDATES}.")
    return True


def verdict(pct):
    print("\n--- verdict against the pre-registered threshold ---")
    if pct < INVISIBLE_PCT:
        print(f"  {pct:.3f}% < {INVISIBLE_PCT}%  ->  INVISIBLE. Leave it as is.")
        print("  Note this capture is an upper bound: nothing moves, so every rebuild is")
        print("  attributable to the cache miss, and a real match has both a larger tick")
        print("  cost and fewer cache-attributable rebuilds. Invisible here is invisible.")
    elif pct < VISIBLE_PCT:
        print(f"  {INVISIBLE_PCT}% <= {pct:.3f}% < {VISIBLE_PCT}%  ->  REAL BUT MINOR.")
        print("  The one-line fix is free, so take it opportunistically; do not schedule work.")
    else:
        print(f"  {pct:.3f}% >= {VISIBLE_PCT}%  ->  VISIBLE. Worth fixing.")
        print("  But this is the upper-bound arm: re-measure on a busy map before acting,")
        print("  because a real match dirties many of these triggers anyway.")


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)

    control = summarize(sys.argv[1], load(sys.argv[1]))
    report(control)
    ok = check_premise(control)
    verdict(control["pct_budget"])

    if len(sys.argv) < 3:
        print("\n(No treatment file given. The control arm alone bounds the cost; the")
        print(" treatment arm is what proves the cost is attributable to this defect")
        print(" and not to something else the timer happens to span.)")
        return 0 if ok else 1

    treat = summarize(sys.argv[2], load(sys.argv[2]))
    report(treat)

    print("\n--- attribution (control - treatment) ---")
    du = control["updates"] - treat["updates"]
    dr = control["rebuilds"] - treat["rebuilds"]
    dt = control["total_us"] - treat["total_us"]
    print(f"  updates  / tick   {control['updates']:9.1f} -> {treat['updates']:9.1f}   (delta {du:+.1f})")
    print(f"  rebuilds / tick   {control['rebuilds']:9.1f} -> {treat['rebuilds']:9.1f}   (delta {dr:+.1f})")
    print(f"  total    / tick   {control['total_us']:9.1f} -> {treat['total_us']:9.1f} us (delta {dt:+.1f} us)")
    print(f"  delta as share of budget: {100.0 * dt / 1000.0 / TICK_BUDGET_MS:.3f}%")

    if treat["updates"] > 1:
        print("\n  WARNING: the treatment arm still shows updates every tick. Either the")
        print("  fix is not in this build, or something other than the Offset comparison")
        print("  is dirtying these triggers. Do not attribute the delta until that is")
        print("  explained.")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
