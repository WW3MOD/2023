#!/usr/bin/env python3
"""Score a MissileTrace .missiles.jsonl against the three success signatures in
WORKSPACE/audit/javelin-terminal-geometry.md sections 6.1-6.3.

The signatures are the audit's, quoted verbatim in the code below. Nothing here eyeballs a
trajectory: each scenario has a boolean fingerprint and the script counts records that match it.

  6.1 survival   flystraight_latches >= 1, flystraight_state == "hitting",
                 end_tick - min_dist_tick > 5, min_dist > 298, min_aim_dist > 298,
                 reason in {fuel_out, off_map}, damage == 0
  6.2 tail       min_aim_dist > 298   (corpus maximum is 6, so one is significant)
  6.3 loop       flystraight_latches == 0, end_tick near the 71-74 tick fuel ceiling,
                 and the hf series rotating through more than 128 facings (180 degrees)

Lane attribution is by launcher cell: the rig puts one trigger range per lane and lane 1 of each
sweep is an unperturbed control, so "did the control lane also do it?" is answerable from the same
file. Usage:

    python tools/autotest/analyze-javelin-probe.py <run-dir-or-jsonl> [--scenario 6.1|6.2|6.3]
"""
import argparse
import json
import os
import sys
from collections import Counter, defaultdict

CLOSE_ENOUGH = 298

TRIGGERS = {
    "6.1": [0, 800, 1000, 1200, 1400, 1600, 1800, 2000],
    "6.3": [0, 900, 1200, 1500, 1800, 2100, 2400, 2700],
    "latch": [0, 1000, 1500, 2000],
}


def lane_index(summaries):
    """Map each launcher cell to a lane number.

    Derived from the cells actually present rather than a hardcoded grid, so the same script reads
    a two-column sweep and a single-column long-range arm. Sorting by (x, y) reproduces the rig's
    own ordering, which fills all rows of one column before moving to the next.
    """
    cells = sorted({(s["launch_pos"][0] // 1024, s["launch_pos"][1] // 1024) for s in summaries})
    return {c: i + 1 for i, c in enumerate(cells)}


def load(path):
    meta, ticks, summaries, end = None, defaultdict(list), [], None
    with open(path) as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            r = json.loads(line)
            ev = r.get("ev")
            if ev == "meta":
                meta = r
            elif ev == "t":
                ticks[r["id"]].append(r)
            elif ev == "m":
                summaries.append(r)
            elif ev == "end":
                end = r
    return meta, ticks, summaries, end


def facing_span(tick_rows):
    """Total signed rotation of the horizontal facing, in facings (256 = 360 degrees).

    Summed as shortest-arc per-tick deltas so wrapping through 0/255 is not read as a 255-facing
    jump, and returned as the peak-to-trough excursion of the cumulative turn. A missile flying a
    straight line scores ~0; the audit's loop signature is >128.
    """
    if not tick_rows:
        return 0, 0
    cum, lo, hi, prev = 0, 0, 0, None
    for row in tick_rows:
        hf = row.get("hf")
        if hf is None:
            continue
        if prev is not None:
            d = (hf - prev) % 256
            if d > 128:
                d -= 256
            cum += d
            lo, hi = min(lo, cum), max(hi, cum)
        prev = hf
    return hi - lo, abs(cum)


def survived_61(s):
    return (
        s["flystraight_latches"] >= 1
        and s["flystraight_state"] == "hitting"
        and s["end_tick"] - s["min_dist_tick"] > 5
        and s["min_dist"] > CLOSE_ENOUGH
        and s["min_aim_dist"] > CLOSE_ENOUGH
        and s["reason"] in ("fuel_out", "off_map")
        and s["damage"] == 0
    )


def report(path, scenario):
    meta, ticks, summaries, end = load(path)
    # Weapon keys arrive lowercased from the map ruleset, so match case-insensitively.
    atgm_all = [s for s in summaries if s["weapon"].lower() == "atgm"]

    # `unterminated` means the match ended with the missile still aloft, so its min_dist is a
    # truncated running minimum rather than a closest approach. Those records can inflate every
    # signature here without meaning anything, so they are counted and then excluded.
    truncated = [s for s in atgm_all if s["reason"] == "unterminated"]
    atgm = [s for s in atgm_all if s["reason"] != "unterminated"]

    print(f"file      {path}")
    if meta:
        print(f"meta      scenario={meta.get('scenario')} seed={meta.get('seed')} ticks={meta.get('ticks')}")
    if end:
        print(f"end       records={end['records']} dropped_records={end['dropped_records']} "
              f"dropped_tick_lines={end['dropped_tick_lines']}")
    print(f"records   {len(summaries)} total, {len(atgm_all)} ATGM, "
          f"{len(truncated)} of them truncated (still aloft at match end) and excluded below")
    if not atgm:
        print("\nNO ATGM RECORDS — the rig did not fire. Nothing below is evidence of anything.")
        return 1

    print("\nend reasons: " + ", ".join(f"{k}={v}" for k, v in Counter(s["reason"] for s in atgm).most_common()))
    latched = [s for s in atgm if s["flystraight_latches"] >= 1]
    print(f"latched:     {len(latched)}/{len(atgm)}")
    print(f"min_aim_dist max={max(s['min_aim_dist'] for s in atgm)}  "
          f"min_dist max={max(s['min_dist'] for s in atgm)}")
    print(f"end_tick     max={max(s['end_tick'] for s in atgm)}  "
          f"launch_range {min(s['launch_range'] for s in atgm)}..{max(s['launch_range'] for s in atgm)}")

    lanes = lane_index(atgm)

    def lane_of(s):
        return lanes[(s["launch_pos"][0] // 1024, s["launch_pos"][1] // 1024)]

    by_lane = defaultdict(list)
    for s in atgm:
        by_lane[lane_of(s)].append(s)

    trig = TRIGGERS.get(scenario)
    print("\nper lane" + ("  (lane 1 and 5 offsets differ; lane 1 = CONTROL, no perturbation)" if trig else ""))
    header = f"{'lane':>4} {'trigger':>8} {'n':>5} {'hit':>5} {'maxAim':>7} {'maxMin':>7} {'maxTurn':>8} {'6.1sig':>7} {'6.3sig':>7}"
    print(header)
    for lane in sorted(by_lane):
        rows = by_lane[lane]
        t = trig[lane - 1] if trig and lane <= len(trig) else "-"
        hit = sum(1 for s in rows if s["damage"] > 0)
        max_turn = 0
        loop_sig = 0
        for s in rows:
            span, _ = facing_span(ticks.get(s["id"], []))
            max_turn = max(max_turn, span)
            if s["flystraight_latches"] == 0 and s["end_tick"] >= 71 and span > 128:
                loop_sig += 1
        sig61 = sum(1 for s in rows if survived_61(s))
        print(f"{lane:>4} {str(t):>8} {len(rows):>5} {hit:>5} "
              f"{max(s['min_aim_dist'] for s in rows):>7} {max(s['min_dist'] for s in rows):>7} "
              f"{max_turn:>8} {sig61:>7} {loop_sig:>7}")

    print()
    s61 = [s for s in atgm if survived_61(s)]
    s62 = [s for s in atgm if s["min_aim_dist"] > CLOSE_ENOUGH]
    s63 = []
    for s in atgm:
        span, _ = facing_span(ticks.get(s["id"], []))
        if s["flystraight_latches"] == 0 and s["end_tick"] >= 71 and span > 128:
            s63.append((s, span))

    print(f"6.1 survival fingerprint : {len(s61)}")
    print(f"6.2 min_aim_dist > 298   : {len(s62)}")
    print(f"6.3 loop fingerprint     : {len(s63)}")

    for s in s61[:10]:
        print(f"  [6.1] id={s['id']} lane={lane_of(s)} min_dist={s['min_dist']} "
              f"min_aim_dist={s['min_aim_dist']} fs_tick={s['flystraight_tick']} "
              f"end_tick={s['end_tick']} reason={s['reason']} dmg={s['damage']}")
    for s in sorted(s62, key=lambda r: -r["min_aim_dist"])[:10]:
        print(f"  [6.2] id={s['id']} lane={lane_of(s)} min_aim_dist={s['min_aim_dist']} "
              f"min_dist={s['min_dist']} reason={s['reason']} dmg={s['damage']}")
    for s, span in s63[:10]:
        print(f"  [6.3] id={s['id']} lane={lane_of(s)} turn={span} facings "
              f"end_tick={s['end_tick']} reason={s['reason']} min_dist={s['min_dist']}")

    # Widest turn seen anywhere, latched or not. A loop needs >128; printing the maximum says how
    # far the run actually got rather than only whether it cleared the bar.
    widest = max(((facing_span(ticks.get(s["id"], []))[0], s) for s in atgm), key=lambda p: p[0])
    print(f"\nwidest hf excursion in the run: {widest[0]} facings "
          f"({widest[0] * 360 // 256} deg) on id={widest[1]['id']} lane={lane_of(widest[1])} "
          f"latches={widest[1]['flystraight_latches']} end_tick={widest[1]['end_tick']}")
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("path", help="run directory or a .missiles.jsonl")
    ap.add_argument("--scenario", default="", help="6.1 / 6.2 / 6.3, for the lane trigger labels")
    a = ap.parse_args()

    path = a.path
    if os.path.isdir(path):
        hits = [f for f in os.listdir(path) if f.endswith(".missiles.jsonl")]
        if not hits:
            print(f"no .missiles.jsonl in {path}", file=sys.stderr)
            return 2
        path = os.path.join(path, hits[0])

    return report(path, a.scenario)


if __name__ == "__main__":
    sys.exit(main())
