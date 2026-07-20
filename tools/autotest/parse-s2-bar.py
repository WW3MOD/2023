#!/usr/bin/env python3
"""Evaluate the S2 PAIRED-RELATIVE force-efficiency bar, and the dispersion A/B.

parse-s2-batch.py reports each bot's net swing and a sign count of Exp swing > 0.
The ratified S2 bar (runs/260720_s2_calibrate_nn.md §3) is instead PAIRED-RELATIVE,
because a real fight carries a structural negative attrition offset on BOTH sides
(deaths_cost counts every loss; kills_cost only credits enemy kills), so an absolute
"Exp swing >= +1400" is biased against passing. The bar is therefore:

  median(Exp net swing) >= median(Normal net swing) + margin   (margin = one IFV = 1400)
  AND per-seed sign-delta (Exp swing > Normal swing) on >= 7/10
  AND both-spawn symmetry: positive delta on >= 3/5 primary AND >= 3/5 mirror

Modes:
  <dir>                 evaluate the paired-relative bar for one Exp-vs-Normal batch.
  ab:<onDir>,<offDir>   dispersion causal credit: per-seed paired delta of Exp net
                        swing with cohesion ON vs OFF on the identical seed set.

Usage:
  python parse-s2-bar.py <dir>
  python parse-s2-bar.py ab:<onDir>,<offDir>
"""
import json, sys, statistics, os, glob

MARGIN = 1400  # one IFV (bradley $1500 / bmp2 $1300 -> ~$1400 faction-mean)


def med(x):
    return statistics.median(x) if x else 0


def load(result_dir):
    rows = []
    for path in sorted(glob.glob(os.path.join(result_dir, "match_*.json")),
                       key=lambda p: int(''.join(c for c in os.path.basename(p) if c.isdigit()) or 0)):
        outer = json.load(open(path))
        notes = json.loads(outer["notes"])
        name = outer.get("name", "")
        exp = next((p for p in notes["players"] if p.get("bot_type") == "experimental"), None)
        nrm = next((p for p in notes["players"] if p.get("bot_type") == "normal"), None)
        def sw(p):
            return p["stats"]["kills_cost"] - p["stats"]["deaths_cost"]
        def eng(p):
            return p["stats"]["kills_cost"] + p["stats"]["deaths_cost"]
        rows.append({
            "seed": notes.get("seed"),
            "spawn": "mirror" if "mirror" in name else "primary",
            "ticks": notes.get("duration_ticks"),
            "exp_swing": sw(exp), "nrm_swing": sw(nrm),
            "exp_eng": eng(exp), "nrm_eng": eng(nrm),
            "exp_faction": exp["faction"],
            "winner_bt": next((p["bot_type"] for p in notes["players"] if p["name"] == notes.get("winner_name")), None),
        })
    return {r["seed"]: r for r in rows}, rows


def bar(result_dir):
    by_seed, rows = load(result_dir)
    print(f"\n## PAIRED-RELATIVE BAR: {result_dir}  (N={len(rows)})\n")
    print("| seed | spawn | exp faction | Exp swing | Normal swing | delta | Exp eng | winner |")
    print("|" + "---|" * 8)
    deltas, exp_sw, nrm_sw, exp_eng, nrm_eng = [], [], [], [], []
    prim_pos = mir_pos = prim_n = mir_n = 0
    exp_wins = nrm_wins = 0
    for r in rows:
        d = r["exp_swing"] - r["nrm_swing"]
        deltas.append(d); exp_sw.append(r["exp_swing"]); nrm_sw.append(r["nrm_swing"])
        exp_eng.append(r["exp_eng"]); nrm_eng.append(r["nrm_eng"])
        if r["spawn"] == "primary":
            prim_n += 1; prim_pos += (d > 0)
        else:
            mir_n += 1; mir_pos += (d > 0)
        if r["winner_bt"] == "experimental": exp_wins += 1
        elif r["winner_bt"] == "normal": nrm_wins += 1
        print(f"| {r['seed']} | {r['spawn']} | {r['exp_faction']} | {r['exp_swing']} | {r['nrm_swing']} | {d} | {r['exp_eng']} | {r['winner_bt']} |")
    m_exp, m_nrm = med(exp_sw), med(nrm_sw)
    edge = m_exp - m_nrm
    sign = sum(1 for d in deltas if d > 0)
    print()
    print(f"- median Exp swing = {m_exp} | median Normal swing = {m_nrm} | relative edge = {edge}")
    print(f"- BAR (edge >= +{MARGIN}): {'PASS' if edge >= MARGIN else 'FAIL'}  (margin over bar = {edge - MARGIN})")
    print(f"- sign-delta (Exp > Normal) on {sign}/{len(deltas)}  (bar wants >=7/10): {'PASS' if sign >= 7 else 'FAIL'}")
    print(f"- both-spawn: primary {prim_pos}/{prim_n}, mirror {mir_pos}/{mir_n}  (bar wants >=3/5 each): "
          f"{'PASS' if prim_pos >= 3 and mir_pos >= 3 else 'FAIL'}")
    print(f"- engagement-volume median: Exp {med(exp_eng)} | Normal {med(nrm_eng)}  (NN calib ref: america 7475 / russia 5950)")
    print(f"- min-engagement floor (both eng medians > 0): {'PASS' if med(exp_eng) > 0 and med(nrm_eng) > 0 else 'FAIL'}")
    print(f"- win split: experimental {exp_wins} / normal {nrm_wins}")


def ab(on_dir, off_dir):
    on, _ = load(on_dir)
    off, _ = load(off_dir)
    seeds = sorted(set(on) & set(off))
    print(f"\n## DISPERSION A/B (causal credit): ON={on_dir}  OFF={off_dir}  (paired N={len(seeds)})\n")
    print("| seed | spawn | Exp swing ON | Exp swing OFF | delta(ON-OFF) | winner ON | winner OFF |")
    print("|" + "---|" * 7)
    deltas = []
    on_wins = off_wins = 0
    for s in seeds:
        d = on[s]["exp_swing"] - off[s]["exp_swing"]
        deltas.append(d)
        if on[s]["winner_bt"] == "experimental": on_wins += 1
        if off[s]["winner_bt"] == "experimental": off_wins += 1
        print(f"| {s} | {on[s]['spawn']} | {on[s]['exp_swing']} | {off[s]['exp_swing']} | {d} | "
              f"{on[s]['winner_bt']} | {off[s]['winner_bt']} |")
    pos = sum(1 for d in deltas if d > 0)
    print()
    print(f"- median Exp swing ON = {med([on[s]['exp_swing'] for s in seeds])} | "
          f"OFF = {med([off[s]['exp_swing'] for s in seeds])}")
    print(f"- median paired delta (ON-OFF) = {med(deltas)}  (>0 => cohesion improves combat economy)")
    print(f"- delta positive on {pos}/{len(deltas)} seeds")
    print(f"- Exp win split: ON {on_wins}/{len(seeds)} | OFF {off_wins}/{len(seeds)}")


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print("usage: parse-s2-bar.py <dir> | ab:<onDir>,<offDir>"); sys.exit(1)
    for a in args:
        if a.startswith("ab:"):
            on_dir, off_dir = a[len("ab:"):].split(",")
            ab(on_dir, off_dir)
        else:
            bar(a)
