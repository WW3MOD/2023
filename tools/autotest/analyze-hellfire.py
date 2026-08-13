#!/usr/bin/env python3
"""Per-lane latch/hit report for the Hellfire probe, plus the terrain-climb check.

Two things this does that analyze-missiles.py cannot:

1. Lane attribution by TARGET, not by launcher position. analyze-missiles.py keys
   lanes off `launch_pos.y`, which is fine for the MANPAD rig's infantry but wrong
   here: the Hellfire air launchers are aircraft and reposition before firing, so
   launch_pos drifts off the spawn row. Each lane owns exactly one target actor,
   so `target_id` is the stable key. It also resolves the air/ground y-collision
   (air_flee and gnd_static both launch from y=3) without relying on the weapon
   name.

2. Classifies each latch as firing on a physically CLOSING or OPENING tick using
   the 3D separation, which is the quantity the fix moved the predicate onto,
   and reports the minimum missile Z ever observed — the only way the incline
   branch at Missile.cs:669 can be entered on a mod whose Map.Height is
   uniformly zero.
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


def d3(a, b):
    return int(math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2))


# (target actor type, target spawn row) -> lane name. The target never changes
# row (every motion script moves in x only), so this is stable for the whole run
# and, unlike launcher position, is unaffected by the air launchers repositioning.
# Rig-neutral on purpose: test-missile-hellfire-probe reuses test-missile-latch-probe's
# target cells and motion scripts verbatim, so one mapping reads both rigs and the
# Hellfire lanes line up row-for-row against the MANPAD/ATGM lanes.
LANE_BY_TARGET = {
    ("littlebird", 3): "air_flee",
    ("littlebird", 9): "air_approach",
    ("littlebird", 15): "air_reverse",
    ("littlebird", 21): "air_hover",
    ("t90", 3): "gnd_static",
    ("t90", 15): "gnd_flee",
    ("t90", 25): "gnd_reverse",
}


def lane_name(s, override):
    if override:
        return override.get(str(s["target_id"]), f"tgt{s['target_id']}")
    key = (s["target"], s["launch_tgt"][1] // 1024)
    return LANE_BY_TARGET.get(key, f"{key[0]}@y{key[1]}")


def main(path, override=None):
    meta, ticks, summaries = load(path)
    print(f"# {path}")
    print(f"# meta: {meta}")
    print(f"# missiles: {len(summaries)}")
    print()

    by = defaultdict(list)
    for s in summaries:
        by[(lane_name(s, override), s["weapon"])].append(s)

    print("== per-lane ==")
    print(f"{'lane':<22}{'weapon':<24}{'n':>4}{'hits':>6}{'hit%':>7}{'latch':>7}   reasons")
    tot_n = tot_h = tot_l = 0
    for (lane, wpn), ss in sorted(by.items()):
        n = len(ss)
        hits = sum(1 for s in ss if s["damage_to_target"] > 0)
        lat = sum(1 for s in ss if s["flystraight_tick"] >= 0)
        tot_n += n
        tot_h += hits
        tot_l += lat
        rc = defaultdict(int)
        for s in ss:
            rc[s["reason"]] += 1
        print(f"{lane:<22}{wpn:<24}{n:>4}{hits:>6}{100 * hits // max(n, 1):>6}%{lat:>7}   {dict(rc)}")
    print(f"{'TOTAL':<22}{'':<24}{tot_n:>4}{tot_h:>6}{100 * tot_h // max(tot_n, 1):>6}%{tot_l:>7}")
    print()

    print("== every latch, classified by 3D physical separation ==")
    hdr = (f"{'id':>5}{'lane':<20}{'tk':>4}{'state':>9}{'min3D':>8}{'ce':>5}"
           f"{'phys3':>8}{'phys3Prev':>10}{'dPhys':>7}{'verdict':>9}  {'reason':<16}{'dmg':>6}{'minDist':>8}")
    print(hdr)
    closing = opening = unknown = 0
    for s in sorted(summaries, key=lambda x: x["id"]):
        tk = s["flystraight_tick"]
        if tk < 0:
            continue
        seq = ticks.get(s["id"], [])
        at = next((t for t in seq if t["tk"] == tk), None)
        prev = next((t for t in seq if t["tk"] == tk - 1), None)
        p_now = d3(at["p"], at["tgt"]) if at else None
        p_prv = d3(prev["p"], prev["tgt"]) if prev else None
        if p_now is None or p_prv is None:
            verdict, unknown = "?", unknown + 1
            dphys = None
        else:
            dphys = p_now - p_prv
            if dphys < 0:
                verdict, closing = "CLOSING", closing + 1
            else:
                verdict, opening = "opening", opening + 1
        print(f"{s['id']:>5}{lane_name(s, override):<20}{tk:>4}{s['flystraight_state']:>9}"
              f"{s['flystraight_min_dist']:>8}{s['close_enough']:>5}"
              f"{str(p_now):>8}{str(p_prv):>10}{str(dphys):>7}{verdict:>9}  "
              f"{s['reason']:<16}{s['damage_to_target']:>6}{s['min_dist']:>8}")
    if closing + opening + unknown == 0:
        print("(no latches)")
    print()
    print(f"latches on a physically CLOSING tick : {closing}")
    print(f"latches on an opening tick           : {opening}")
    print(f"latches with no previous tick logged : {unknown}")
    print()

    # --- Gap 2: can the TerrainHeightAware incline branch be entered at all? ---
    # InclineLookahead reads Map.Height[cell]*512. On this mod Map.Height is
    # uniformly zero (MapGrid sets no MaximumTerrainHeight, so the height layer is
    # never even read from map.bin), so predClfHgt is identically 0 and
    #     diffClfMslHgt = predClfHgt - pos.Z = -pos.Z
    # The climb branch needs diffClfMslHgt >= 0, i.e. pos.Z <= 0. Report the
    # minimum missile Z ever observed, per weapon.
    print("== incline-branch reachability (min missile Z seen; branch needs Z <= 0) ==")
    minz = {}
    zle0 = defaultdict(int)
    mindat = {}
    for s in summaries:
        w = s["weapon"]
        for t in ticks.get(s["id"], []):
            z = t["p"][2]
            minz[w] = z if w not in minz else min(minz[w], z)
            dat = t.get("dat")
            if dat is not None:
                mindat[w] = dat if w not in mindat else min(mindat[w], dat)
            if z <= 0:
                zle0[w] += 1
    print(f"{'weapon':<24}{'minZ':>10}{'minDAT':>9}{'ticks with Z<=0':>18}")
    for w in sorted(minz):
        print(f"{w:<24}{minz[w]:>10}{mindat.get(w, -1):>9}{zle0[w]:>18}")
    print()


if __name__ == "__main__":
    ov = json.loads(sys.argv[2]) if len(sys.argv) > 2 else None
    main(sys.argv[1], ov)
