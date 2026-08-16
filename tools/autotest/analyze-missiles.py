#!/usr/bin/env python3
"""Read a MissileTrace .missiles.jsonl and answer the audit's open questions.

The trace's rthd/rtd are distances to the LEAD point (Missile.cs:1005-1008 adds
CalculateLeadTarget before taking the length), while CloseEnough is a physical
constant. Every derived column here therefore keeps the two apart: `phys` is
recomputed from the logged positions, `rthd` is what the code actually tested.
"""
import json
import math
import sys
from collections import defaultdict


def load(path):
    meta, ticks, summaries = None, defaultdict(list), []
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
    return meta, ticks, summaries


def hor(a, b):
    return int(math.hypot(a[0] - b[0], a[1] - b[1]))


def dist3(a, b):
    return int(math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2))


def annotate(ticks_for_id):
    """Add physical distance and target velocity to each tick sample."""
    out = []
    prev_tgt = None
    for t in ticks_for_id:
        p, tgt = t["p"], t["tgt"]
        phys_hor = hor(p, tgt)
        tvel = 0 if prev_tgt is None else hor(tgt, prev_tgt)
        # signed: is the target's own motion opening or closing the range?
        closing = None
        if prev_tgt is not None:
            closing = hor(p, prev_tgt) - phys_hor
        out.append(dict(t, phys_hor=phys_hor, phys3=dist3(p, tgt), tvel=tvel, tclosing=closing))
        prev_tgt = tgt
    return out


def latch_report(ticks, summaries, lane_of):
    rows = []
    for s in summaries:
        if s["flystraight_tick"] < 0:
            continue
        tk = s["flystraight_tick"]
        seq = annotate(ticks.get(s["id"], []))
        at = next((t for t in seq if t["tk"] == tk), None)
        before = next((t for t in seq if t["tk"] == tk - 1), None)
        rows.append({
            "id": s["id"],
            "lane": lane_of(s),
            "weapon": s["weapon"],
            "tk": tk,
            "state": s["flystraight_state"],
            "hor": s["flystraight_hor_dist"],
            "min": s["flystraight_min_dist"],
            "ce": s["close_enough"],
            "phys": at["phys_hor"] if at else None,
            "phys_prev": before["phys_hor"] if before else None,
            "rthd_prev": before["rthd"] if before else None,
            "tvel": at["tvel"] if at else None,
            "latches": s["flystraight_latches"],
            "reason": s["reason"],
            "dmg_tgt": s["damage_to_target"],
            "min_dist": s["min_dist"],
        })
    return rows


def main(path, lane_key="launch_y"):
    meta, ticks, summaries = load(path)

    def lane_of(s):
        return LANES.get(s["launch_pos"][1] // 1024, f"y{s['launch_pos'][1] // 1024}")

    print(f"# {path}")
    print(f"meta: {meta}")
    print(f"missiles: {len(summaries)}")
    print()

    print("== per-lane summary ==")
    by = defaultdict(list)
    for s in summaries:
        by[(lane_of(s), s["weapon"])].append(s)
    print(f"{'lane':<14}{'weapon':<10}{'n':>4}{'latched':>8}{'hits':>6}{'reasons':>0}")
    for (lane, wpn), ss in sorted(by.items()):
        n = len(ss)
        latched = sum(1 for s in ss if s["flystraight_tick"] >= 0)
        hits = sum(1 for s in ss if s["damage_to_target"] > 0)
        rc = defaultdict(int)
        for s in ss:
            rc[s["reason"]] += 1
        oc = defaultdict(int)
        for s in ss:
            oc[s["outcome"]] += 1
        print(f"{lane:<14}{wpn:<10}{n:>4}{latched:>8}{hits:>6}  {dict(rc)} {dict(oc)}")
    print()

    print("== every flyStraight latch ==")
    rows = latch_report(ticks, summaries, lane_of)
    if not rows:
        print("(none)")
    else:
        hdr = f"{'id':>4}{'lane':>13}{'tk':>4}{'state':>9}{'rthd':>8}{'min':>8}{'ce':>5}{'phys':>7}{'physPrev':>9}{'rthdPrev':>9}{'tvel':>6}{'lat':>4}  {'reason':<14}{'dmgTgt':>7}{'minDist':>8}"
        print(hdr)
        for r in rows:
            print(f"{r['id']:>4}{r['lane']:>13}{r['tk']:>4}{r['state']:>9}{r['hor']:>8}{r['min']:>8}{r['ce']:>5}"
                  f"{str(r['phys']):>7}{str(r['phys_prev']):>9}{str(r['rthd_prev']):>9}{str(r['tvel']):>6}{r['latches']:>4}  "
                  f"{r['reason']:<14}{r['dmg_tgt']:>7}{r['min_dist']:>8}")
    print()

    print("== hypothesis discriminators ==")
    if rows:
        h1 = sum(1 for r in rows if r["hor"] == r["min"])
        h2 = sum(1 for r in rows if r["min"] + r["ce"] < r["hor"])
        # lead artefact: the lead-inflated distance grew much faster than physical did
        art = 0
        real = 0
        for r in rows:
            if r["phys"] is None or r["phys_prev"] is None or r["rthd_prev"] is None:
                continue
            d_phys = r["phys"] - r["phys_prev"]
            d_rthd = r["hor"] - r["rthd_prev"]
            if d_rthd > max(2 * max(d_phys, 0), 100):
                art += 1
            else:
                real += 1
        print(f"H1 flight-audit  (min == hor at latch)         : {h1}/{len(rows)}")
        print(f"H2 trace-worker  (min + closeEnough < hor)      : {h2}/{len(rows)}")
        print(f"H3 review        (lead grew >> physical at latch): {art}/{art + real}")
        print(f"   physical also grew normally                  : {real}/{art + real}")
    print()

    print("== by-products ==")
    oc = defaultdict(int)
    rc = defaultdict(int)
    db = defaultdict(int)
    for s in summaries:
        oc[s["outcome"]] += 1
        rc[s["reason"]] += 1
        db[(s["weapon"], s["end_dat_bucket"])] += 1
    print("outcome:", dict(oc))
    print("reason :", dict(rc))
    print("end_dat_bucket by weapon:", dict(db))
    print()
    print("== damage vs closest approach (min_dist to target centre) ==")
    print(f"{'id':>4}{'weapon':>10}{'minDist':>9}{'endDat':>8}{'dmgTgt':>8}{'dmgAll':>8}{'unattr':>7}  reason")
    for s in sorted(summaries, key=lambda x: (x["weapon"], x["min_dist"])):
        print(f"{s['id']:>4}{s['weapon']:>10}{s['min_dist']:>9}{s['end_dat']:>8}"
              f"{s['damage_to_target']:>8}{s['damage']:>8}{s['damage_unattributed']:>7}  {s['reason']}")


LANES = {}

if __name__ == "__main__":
    if len(sys.argv) > 2:
        LANES = json.loads(sys.argv[2])
    main(sys.argv[1])
