#!/usr/bin/env python3
"""Parse an S2 (Force Efficiency / combat) tournament result dir.

S2's ladder metric is NET COMBAT SWING = stats.kills_cost - stats.deaths_cost,
read post-hoc from the per-match verdict JSON (verdict_version >= 5, `notes` is a
JSON string). Companion to parse-s1-batch.py (which reads the S1 economy metric
capture_income_gross) — kept separate so the validated S1 tool is untouched.

Two modes:
  <dir>          experimental-vs-normal: net swing / engagement by exp vs ctl.
  calib:<dir>    normal-vs-normal: side-fairness + min-engagement, labelled by
                 faction/slot (america = USA 14,45 ; russia = 80,35).

Reports per side: net-swing median, engagement-volume median (kills_cost +
deaths_cost), units_dead, score median, win split — and a min-engagement verdict.

Usage: python parse-s2-batch.py <result_dir | calib:result_dir> [...]
"""
import json, sys, statistics, os, glob


def med(x):
    return statistics.median(x) if x else 0


def load_matches(result_dir):
    rows = []
    for path in sorted(glob.glob(os.path.join(result_dir, "match_*.json")),
                       key=lambda p: int(''.join(c for c in os.path.basename(p) if c.isdigit()) or 0)):
        outer = json.load(open(path))
        notes = json.loads(outer["notes"])
        idx = int(''.join(c for c in os.path.basename(path) if c.isdigit()))
        name = outer.get("name", "")
        rows.append({
            "match": idx,
            "scenario": "mirror" if "mirror" in name else "primary",
            "vv": notes.get("verdict_version"),
            "seed": notes.get("seed"),
            "ticks": notes.get("duration_ticks"),
            "winner_name": notes.get("winner_name"),
            "win_reason": notes.get("win_reason"),
            "players": notes["players"],
        })
    return rows


def swing(p):
    s = p["stats"]
    return s["kills_cost"] - s["deaths_cost"]


def engagement(p):
    s = p["stats"]
    return s["kills_cost"] + s["deaths_cost"]


def pget(players, bot_type):
    return next((p for p in players if p.get("bot_type") == bot_type), None)


def summarize(rows, exp_bt="experimental", ctl_bt="normal"):
    calib = (exp_bt == ctl_bt)
    label = "CALIBRATION (normal vs normal)" if calib else f"{exp_bt} vs {ctl_bt}"
    print(f"\n### {label}  (N={len(rows)})\n")
    print("| m | scen | seed | ticks | A side | A swing | A eng | A k/d | B side | B swing | B eng | B k/d | winner | reason |")
    print("|" + "---|" * 14)
    a_sw, b_sw, a_eng, b_eng = [], [], [], []
    a_sc, b_sc = [], []
    a_dead, b_dead = [], []
    slot_wins, draws = {}, 0
    exp_wins = ctl_wins = 0
    a_sw_primary, a_sw_mirror = [], []
    for r in rows:
        ps = r["players"]
        if calib:
            A = next((p for p in ps if p["faction"] == "america"), ps[0])
            B = next((p for p in ps if p["faction"] == "russia"), ps[1])
        else:
            A = pget(ps, exp_bt); B = pget(ps, ctl_bt)
        wname = r["winner_name"]
        win_fac = next((p["faction"] for p in ps if p["name"] == wname), None)
        win_bt = next((p["bot_type"] for p in ps if p["name"] == wname), None)
        if wname in (None, "", "draw"):
            draws += 1; wlabel = "draw"
        else:
            wlabel = win_fac if calib else win_bt
            slot_wins[win_fac] = slot_wins.get(win_fac, 0) + 1
            if not calib:
                if win_bt == exp_bt: exp_wins += 1
                else: ctl_wins += 1
        asw, bsw = swing(A), swing(B)
        aen, ben = engagement(A), engagement(B)
        a_sw.append(asw); b_sw.append(bsw); a_eng.append(aen); b_eng.append(ben)
        a_sc.append(A["score_total"]); b_sc.append(B["score_total"])
        a_dead.append(A["stats"]["deaths_cost"]); b_dead.append(B["stats"]["deaths_cost"])
        (a_sw_mirror if r["scenario"] == "mirror" else a_sw_primary).append(asw)
        print(f"| {r['match']} | {r['scenario']} | {r['seed']} | {r['ticks']} | {A['faction']} | {asw} | {aen} | "
              f"{A['stats']['units_killed']}/{A['stats']['units_dead']} | {B['faction']} | {bsw} | {ben} | "
              f"{B['stats']['units_killed']}/{B['stats']['units_dead']} | {wlabel} | {r['win_reason']} |")
    print()
    if calib:
        aname, bname = "america(USA 14,45)", "russia(80,35)"
        print(f"- win split by faction/slot: {slot_wins}  draw={draws}")
        print(f"- net-swing median: {aname}={med(a_sw)}  {bname}={med(b_sw)}  (calibration wants ~0 & symmetric)")
        print(f"- engagement-volume median (kills_cost+deaths_cost): {aname}={med(a_eng)}  {bname}={med(b_eng)}")
        print(f"- deaths_cost median: {aname}={med(a_dead)}  {bname}={med(b_dead)}  (both > 0 => a real fight)")
        print(f"- score_total median: {aname}={med(a_sc)}  {bname}={med(b_sc)}")
        eng_ok = med(a_eng) > 0 and med(b_eng) > 0 and med(a_dead) > 0 and med(b_dead) > 0
        print(f"- MIN-ENGAGEMENT verdict: {'PASS' if eng_ok else 'FAIL'} "
              f"(both sides' engagement + deaths medians > 0 => Normal fights at 720s => Normal is a viable S2 opponent)")
    else:
        print(f"- {exp_bt} net-swing median ALL: {med(a_sw)} | primary: {med(a_sw_primary)} (n={len(a_sw_primary)}) | mirror: {med(a_sw_mirror)} (n={len(a_sw_mirror)})")
        pos = sum(1 for x in a_sw if x > 0)
        print(f"- {exp_bt} net-swing positive on {pos}/{len(a_sw)} seeds (sign robustness; bar wants >=7/10)")
        print(f"- {exp_bt} engagement-volume median: {med(a_eng)} | {ctl_bt}: {med(b_eng)}")
        print(f"- win split: {exp_bt}={exp_wins}  {ctl_bt}={ctl_wins}  draw={draws}")


if __name__ == "__main__":
    dirs = sys.argv[1:]
    if not dirs:
        print("usage: parse-s2-batch.py <result_dir | calib:result_dir> [...]"); sys.exit(1)
    for d in dirs:
        if d.startswith("calib:"):
            d = d[len("calib:"):]
            print(f"\n## CALIBRATION dir: {d}")
            summarize(load_matches(d), exp_bt="normal", ctl_bt="normal")
        else:
            print(f"\n## dir: {d}")
            summarize(load_matches(d))
