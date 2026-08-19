#!/usr/bin/env python3
"""Evaluate CASE-01's Bar A over a seeded batch.

The case's bar (`WORKSPACE/cases/case-01-forest-ambush.md`, status log 2026-07-29) has two
clauses, and they live at different levels:

  Bar A   mean def cost-loss <= 50cr AND mean att cost-loss >= 300cr, over >=6 seeds
  Bar B   every seed def = 0                                          (optional hard guard)

Bar A is a MEAN, so no single run can decide it -- which is why the scenario itself asserts
only Bar B (per-seed) and defers the two mean clauses here. Collapsing Bar A into a per-run
`attLoss >= 300` would be a stricter, different bar: the 2026-07-28 batch it was mined from
holds seed 5005 at attLoss=200.

Usage:
  python3 tools/autotest/parse-case01-bar.py <run-dir> [<run-dir> ...]
  python3 tools/autotest/parse-case01-bar.py --glob ~/.ww3mod-tests/screenshots

A <run-dir> is what run-test.sh prints as `Run dir:` -- it holds that run's result.json.

Exit: 0 = bar GREEN, 1 = bar RED, 2 = cannot evaluate (too few seeds / invalid runs).
Exit 2 is deliberately NOT green: an under-sized batch is an absent measurement, not a pass.
"""
import glob as globmod
import json
import os
import re
import sys

TEST_NAME = "test-case01-forest-ambush"

# Bar A, verbatim from the case. Do not retune these without the user -- the bar is theirs.
BAR_A_DEF_MAX_MEAN = 50     # mean defender cost-loss, credits
BAR_A_ATT_MIN_MEAN = 300    # mean attacker cost-loss, credits
BAR_A_MIN_SEEDS = 6         # "over >=6 seeds" -- distinct seeds, see load()

BAR_B_DEF_MAX = 0           # per-seed defender cost-loss

DEF_RE = re.compile(r"defLoss=(\d+)")
ATT_RE = re.compile(r"attLoss=(\d+)")


def load(run_dirs):
    """Read one result.json per run dir. Returns (rows, problems)."""
    rows, problems = [], []
    for d in run_dirs:
        path = d if d.endswith(".json") else os.path.join(d, "result.json")
        if not os.path.isfile(path):
            problems.append(f"{d}: no result.json")
            continue
        try:
            r = json.load(open(path))
        except (ValueError, OSError) as e:
            problems.append(f"{d}: unreadable ({e})")
            continue

        if r.get("name") != TEST_NAME:
            continue  # a different scenario's run dir; silently skip
        # The single shared ~/.ww3mod-tests/result.json is now a "moved" stub. Never a verdict.
        if r.get("status") == "moved":
            continue

        notes = r.get("notes", "")
        if "SETUP-INVALID" in notes:
            problems.append(f"{os.path.basename(d)}: SETUP-INVALID -- {notes}")
            continue

        dm, am = DEF_RE.search(notes), ATT_RE.search(notes)
        if not dm or not am:
            problems.append(f"{os.path.basename(d)}: no defLoss/attLoss in notes -- {notes[:120]}")
            continue

        rows.append({
            "dir": os.path.basename(d),
            "seed": r.get("seed"),
            "status": r.get("status"),
            "def": int(dm.group(1)),
            "att": int(am.group(1)),
        })
    return rows, problems


def main(run_dirs):
    rows, problems = load(run_dirs)
    rows.sort(key=lambda r: (r["seed"] is None, r["seed"]))

    print(f"\n## CASE-01 Bar A -- {len(rows)} usable run(s)\n")
    if rows:
        print("| seed | def loss (cr) | att loss (cr) | run verdict | run dir |")
        print("|" + "---|" * 5)
        for r in rows:
            print(f"| {r['seed']} | {r['def']} | {r['att']} | {r['status']} | {r['dir']} |")
        print()

    for p in problems:
        print(f"  ! {p}")
    if problems:
        print()

    # An under-sized or seed-duplicated batch cannot evaluate a bar written "over >=6 seeds".
    # Report that as UNEVALUABLE (exit 2), never as a pass -- a fake batch reading green is the
    # exact failure this checker exists to prevent.
    seeds = [r["seed"] for r in rows if r["seed"] is not None]
    dupes = {s for s in seeds if seeds.count(s) > 1}
    blockers = []
    if problems:
        blockers.append(f"{len(problems)} run(s) unusable (listed above)")
    if len(rows) < BAR_A_MIN_SEEDS:
        blockers.append(f"only {len(rows)} run(s); bar needs >={BAR_A_MIN_SEEDS}")
    if dupes:
        blockers.append(f"repeated seed(s) {sorted(dupes)} -- a reused seed is one measurement, not two")
    if len(seeds) != len(rows):
        blockers.append("a run recorded no seed, so distinctness cannot be checked")

    if blockers:
        print("BAR A: UNEVALUABLE")
        for b in blockers:
            print(f"  - {b}")
        return 2

    mean_def = sum(r["def"] for r in rows) / len(rows)
    mean_att = sum(r["att"] for r in rows) / len(rows)
    def_ok = mean_def <= BAR_A_DEF_MAX_MEAN
    att_ok = mean_att >= BAR_A_ATT_MIN_MEAN

    print(f"- mean def loss = {mean_def:.1f}cr  (bar: <={BAR_A_DEF_MAX_MEAN}) "
          f"{'PASS' if def_ok else 'FAIL'}")
    print(f"- mean att loss = {mean_att:.1f}cr  (bar: >={BAR_A_ATT_MIN_MEAN}) "
          f"{'PASS' if att_ok else 'FAIL'}")

    worst = max(r["def"] for r in rows)
    print(f"- Bar B (every seed def = {BAR_B_DEF_MAX}): "
          f"{'PASS' if worst <= BAR_B_DEF_MAX else f'FAIL (worst seed {worst}cr)'} "
          "-- also enforced per-run by the scenario")

    green = def_ok and att_ok
    print(f"\nBAR A: {'GREEN' if green else 'RED'}\n")
    return 0 if green else 1


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        sys.exit(2)
    if args[0] == "--glob":
        root = os.path.expanduser(args[1]) if len(args) > 1 else \
            os.path.expanduser("~/.ww3mod-tests/screenshots")
        args = sorted(globmod.glob(os.path.join(root, f"*{TEST_NAME}*")))
        if not args:
            print(f"no {TEST_NAME} run dirs under {root}")
            sys.exit(2)
    sys.exit(main([os.path.expanduser(a) for a in args]))
