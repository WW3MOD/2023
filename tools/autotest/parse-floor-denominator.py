#!/usr/bin/env python3
"""Live denominator trajectory for the two ratio-scaled standing floors.

Both floors are min(cap, denominator / N) via SupportFloorMath.EffectiveFloor, so
each has a CLIFF: below N the floor is 0 and never fires at all. The offline
--composition-plan replay estimated where those cliffs sit, but its infantry count
is a lower bound on the live one. This reconstructs the denominators from the
unconditional [composition] census lines in a tournament's per-match debug.log.

  medic floor   min(UnitFloors[medi], CountSupportedForce()   / UnitFloorPer[medi])
  truck floor   min(SupplyTruckFloor, CountResupplyCapableUnits() / SupplyTruckFloorPer)

CountSupportedForce walks UnitFloorSupportedTypes; CountResupplyCapableUnits walks
every owned unit whose Rearmable.RearmActors overlaps ResupplyUnitTypes (= truk),
which in this ruleset is the combat-infantry templates only -- vehicles rearm at
logisticscenter and never qualify. Both count owned + in-cargo, which is exactly
what the census prints, so the reconstruction uses the same population the decision
does.

The reported "engaged" figure is the live analogue of the offline replay's column
of that name: the share of census samples on which the effective floor was >= 1,
i.e. the ratio cleared its denominator at all.

Usage: ./tools/autotest/parse-floor-denominator.py <tournament-result-dir>
"""

import glob
import json
import os
import re
import sys

# UnitFloorSupportedTypes, ai-america.yaml / ai-russia.yaml @experimental blocks.
SUPPORTED = ["e3", "ar", "tl", "e2", "at", "mt", "aa", "sn", "e6", "tecn"]

# Concrete infantry inheriting a template whose RearmActors lists truk
# (^E1 ^E3 ^AR ^E2 ^TL ^MT ^SN ^AT ^AA ^E6 ^E4 ^SF ^DR). medi/tecn carry no ammo
# pool and are not truck customers.
TRUK_REARMABLE = ["e1", "e3", "ar", "e2", "mt", "tl", "at", "aa", "sn", "e6", "sf", "dr", "e4"]

MEDIC_PER, MEDIC_CAP = 10, 2
TRUCK_PER, TRUCK_CAP = 10, 3

SLOT = re.compile(r"(\S+?)=(\d+)\+(\d+)/(\d+)v(\d+)")
HEAD = re.compile(r"\[composition\] census tick=(\d+) player=(\S+)")


def faction_of(names):
    """Which faction suffix this player's census slots carry."""
    for n in names:
        if "." in n:
            return n.rsplit(".", 1)[1]
    return "?"


def verdict_bots(match_json):
    """bot_type per player from the verdict, when one was written.

    The config's Matchup block is informational, so the verdict is the usual
    ground truth. A wall-clock kill leaves no verdict at all, which is why the
    census itself is treated as the primary identity evidence: CensusLogInterval
    is set ONLY on the two @experimental blocks, and the @normal blocks define no
    UnitTargetShares, so compositionTypes stays null and the census returns early
    for them. A [composition] line naming a player is therefore proof that player
    ran the experimental module -- it cannot be produced by any other bot.
    """
    try:
        with open(match_json) as fh:
            outer = json.load(fh)
        notes = outer.get("notes")
        notes = json.loads(notes) if isinstance(notes, str) else (notes or {})
        return {p.get("name"): p.get("bot_type") for p in notes.get("players", [])}
    except (OSError, ValueError):
        return {}


def parse_match(debug_path):
    """-> {player: [(tick, {type: alive}), ...]} in log order."""
    series = {}
    with open(debug_path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if "[composition] census" not in line:
                continue
            head = HEAD.search(line)
            if not head:
                continue
            tick, player = int(head.group(1)), head.group(2)
            tail = line.split("(type=inWorld+inCargo/census‰vtarget‰)", 1)
            if len(tail) != 2:
                continue
            counts = {}
            for m in SLOT.finditer(tail[1]):
                counts[m.group(1)] = int(m.group(2)) + int(m.group(3))
            series.setdefault(player, []).append((tick, counts))
    return series


def denominators(counts, faction):
    sup = sum(counts.get(f"{t}.{faction}", 0) for t in SUPPORTED)
    truk = sum(counts.get(f"{t}.{faction}", 0) for t in TRUK_REARMABLE)
    return sup, truk


def pct(n, d):
    return 0.0 if d == 0 else 100.0 * n / d


def summarize(label, values, per, cap):
    """Trajectory stats plus the cliff placement."""
    if not values:
        return None
    peak = max(values)
    med = sorted(values)[len(values) // 2]
    engaged = sum(1 for v in values if min(cap, v // per) >= 1)
    at_cap = sum(1 for v in values if v // per >= cap)
    first = next((i for i, v in enumerate(values) if v >= per), None)
    return {
        "label": label, "n": len(values), "peak": peak, "median": med,
        "final": values[-1], "cliff": per,
        "engaged_pct": pct(engaged, len(values)),
        "atcap_pct": pct(at_cap, len(values)),
        "first_clear_idx": first,
    }


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 3
    result_dir = sys.argv[1]

    agg = {}   # (faction, kind) -> list of values across all matches
    traj = {}  # (faction, game-minute) -> supported values
    bot_types = set()
    matches = 0

    # Driven off the debug logs, not the verdicts: a match killed on the wall
    # clock writes no verdict but its census is complete, and the census is the
    # measurement. A missing verdict is reported, never silently dropped.
    for dbg in sorted(glob.glob(os.path.join(result_dir, "match_*_debug.log")),
                      key=lambda p: int(re.search(r"match_(\d+)", p).group(1))):
        idx = int(re.search(r"match_(\d+)", dbg).group(1))
        mj = dbg.replace("_debug.log", ".json")
        if not os.path.exists(mj):
            print(f"match {idx}: NO VERDICT written (wall-clock kill) -- census still read")

        name2bot = verdict_bots(mj)

        series = parse_match(dbg)
        if not series:
            print(f"match {idx}: ZERO census lines -- census not active in this run")
            continue
        matches += 1

        for player, samples in sorted(series.items()):
            faction = faction_of(samples[-1][1].keys())
            bot = name2bot.get(player, "?")
            bot_types.add((player, bot))
            sup_series, truk_series = [], []
            for tick, counts in samples:
                s, t = denominators(counts, faction)
                sup_series.append(s)
                truk_series.append(t)
                traj.setdefault((faction, tick // (25 * 60)), []).append(s)
            agg.setdefault((faction, "supported"), []).extend(sup_series)
            agg.setdefault((faction, "truk"), []).extend(truk_series)
            print(f"match {idx} player={player} bot={bot} faction={faction} "
                  f"samples={len(sup_series)} "
                  f"supported peak={max(sup_series)} med={sorted(sup_series)[len(sup_series)//2]} "
                  f"| truk-capable peak={max(truk_series)} "
                  f"med={sorted(truk_series)[len(truk_series)//2]}")

    print(f"\nbot_type ground truth: {sorted(bot_types)}")
    print(f"matches with census: {matches}\n")

    print(f"{'faction':>8} {'denominator':>12} {'cliff':>5} {'peak':>5} {'med':>4} "
          f"{'final':>5} {'engaged%':>9} {'at-cap%':>8} {'n':>5}")
    for (faction, kind), values in sorted(agg.items()):
        per, cap = (MEDIC_PER, MEDIC_CAP) if kind == "supported" else (TRUCK_PER, TRUCK_CAP)
        s = summarize(kind, values, per, cap)
        print(f"{faction:>8} {kind:>12} {s['cliff']:>5} {s['peak']:>5} {s['median']:>4} "
              f"{s['final']:>5} {s['engaged_pct']:>8.1f}% {s['atcap_pct']:>7.1f}% {s['n']:>5}")

    # Trajectory: the cliff question is about a count that RISES then decays under
    # attrition, so a single peak hides when the floor switches back off.
    print(f"\nsupported-force trajectory (median across matches, by game minute @ 25 ticks/s)")
    print(f"{'min':>4} " + " ".join(f"{f:>9}" for f in sorted({k[0] for k in traj})))
    factions = sorted({k[0] for k in traj})
    for minute in range(0, 7):
        cells = []
        for f in factions:
            vals = [v for (ff, mm), vv in traj.items() if ff == f and mm == minute for v in vv]
            cells.append(f"{sorted(vals)[len(vals) // 2]:>9}" if vals else f"{'-':>9}")
        print(f"{minute:>4} " + " ".join(cells))
    return 0


if __name__ == "__main__":
    sys.exit(main())
