#!/usr/bin/env python3
"""Parse an S1 tournament result dir into a per-match table + medians.

Reads match_<i>.json (verdict schema >=4; the `notes` field is a JSON string).
Identifies players by bot_type (experimental vs normal) so it is robust across
primary/mirror scenarios. Prints a markdown table and per-side capture_income_gross
medians + capture rate.

Usage: python parse-s1-batch.py <result_dir> [<result_dir> ...]
"""
import json, sys, statistics, os, glob


def load_matches(result_dir):
    rows = []
    for path in sorted(glob.glob(os.path.join(result_dir, "match_*.json")),
                       key=lambda p: int(''.join(c for c in os.path.basename(p) if c.isdigit()) or 0)):
        with open(path) as f:
            outer = json.load(f)
        notes = json.loads(outer["notes"])
        idx = int(''.join(c for c in os.path.basename(path) if c.isdigit()))
        name = outer.get("name", "")
        scenario = "mirror" if "mirror" in name else "primary"
        players = notes["players"]
        rows.append({
            "match": idx, "scenario": scenario,
            "vv": notes.get("verdict_version"),
            "ticks": notes.get("duration_ticks"),
            "winner_name": notes.get("winner_name"),
            "win_reason": notes.get("win_reason"),
            "players": players,
        })
    return rows


def pget(players, bot_type):
    for p in players:
        if p.get("bot_type") == bot_type:
            return p
    return None


def summarize(rows, exp_bt="experimental", ctl_bt="normal"):
    calib = (exp_bt == ctl_bt)
    if not calib and rows:
        # auto-detect control bot_type from the data (stable for the
        # primary/mirror in the 2026-07-21 regime, normal for the sanity floor).
        ps0 = rows[0]["players"]
        e0 = pget(ps0, exp_bt)
        c0 = next((p for p in ps0 if p is not e0 and p.get("bot_type")), None)
        if c0 and c0.get("bot_type"):
            ctl_bt = c0["bot_type"]
    print(f"\n### {exp_bt} vs {ctl_bt}  (N={len(rows)})\n")
    hdr = "| m | scen | ticks | exp faction | exp gross | exp score(a/cap/k) | ctl faction | ctl gross | ctl score(a/cap/k) | winner | reason |"
    print(hdr)
    print("|" + "---|" * 11)
    exp_gross_primary, exp_gross_mirror, exp_gross_all = [], [], []
    ctl_gross_all = []
    exp_wins = ctl_wins = draws = 0
    exp_captures = 0
    slot_wins = {}  # faction -> wins, for identical-bot calibration
    for r in rows:
        ps = r["players"]
        exp = pget(ps, exp_bt)
        # for calibration both are same bot_type; disambiguate by SLOT NAME
        # (USA-bot = spawn 14,45 ; Russia-bot = spawn 80,35). Under the 2026-07-21
        # same-faction regime both bots are america, so faction no longer labels
        # the slot — the player Name does.
        if calib:
            a = next((p for p in ps if p["name"] == "USA-bot"), ps[0])
            b = next((p for p in ps if p["name"] == "Russia-bot"), ps[1])
            exp, ctl = a, b
        else:
            # control = the playable bot that isn't the experimental one
            # (bot_type "stable" for primary/mirror, "normal" for the floor).
            ctl = next((p for p in ps if p is not exp and p.get("bot_type")), None)
        eg = exp["stats"]["capture_income_gross"]
        cg = ctl["stats"]["capture_income_gross"]
        esc = exp["score_components"]; csc = ctl["score_components"]
        wname = r["winner_name"]
        # winner side by matching name
        win_bt = next((p["bot_type"] for p in ps if p["name"] == wname), "?")
        win_fac = next((p["faction"] for p in ps if p["name"] == wname), "?")
        if wname in (None, "", "draw"):
            draws += 1; wlabel = "draw"
        elif calib:
            # identical bots: label the win by SLOT NAME (USA-bot / Russia-bot).
            wlabel = wname
            slot_wins[wname] = slot_wins.get(wname, 0) + 1
        else:
            wlabel = win_bt
            if win_bt == exp_bt: exp_wins += 1
            else: ctl_wins += 1
        if eg > 0: exp_captures += 1
        exp_gross_all.append(eg); ctl_gross_all.append(cg)
        (exp_gross_mirror if r["scenario"] == "mirror" else exp_gross_primary).append(eg)
        print(f"| {r['match']} | {r['scenario']} | {r['ticks']} | {exp['faction']} | {eg} | "
              f"{esc['army_value']}/{esc['capture_income']}/{esc['kills_value']} | {ctl['faction']} | {cg} | "
              f"{csc['army_value']}/{csc['capture_income']}/{csc['kills_value']} | {wlabel} | {r['win_reason']} |")

    def med(x): return statistics.median(x) if x else 0
    print()
    if exp_bt != ctl_bt:
        print(f"- {exp_bt} capture rate (gross>0): {exp_captures}/{len(rows)}")
        print(f"- {exp_bt} gross median ALL: {med(exp_gross_all)}  | primary: {med(exp_gross_primary)} (n={len(exp_gross_primary)}) | mirror: {med(exp_gross_mirror)} (n={len(exp_gross_mirror)})")
        print(f"- {ctl_bt} gross median ALL: {med(ctl_gross_all)}")
        print(f"- win split: {exp_bt}={exp_wins}  {ctl_bt}={ctl_wins}  draw={draws}")
    else:
        am = [next(p for p in r['players'] if p['name']=='USA-bot')['stats']['capture_income_gross'] for r in rows]
        ru = [next(p for p in r['players'] if p['name']=='Russia-bot')['stats']['capture_income_gross'] for r in rows]
        am_s = [next(p for p in r['players'] if p['name']=='USA-bot')['score_total'] for r in rows]
        ru_s = [next(p for p in r['players'] if p['name']=='Russia-bot')['score_total'] for r in rows]
        print(f"- identical-bot win split by slot: {slot_wins}  draw={draws}")
        print(f"- USA-bot(14,45) gross median: {med(am)} | Russia-bot(80,35) gross median: {med(ru)}")
        print(f"- USA-bot score median: {med(am_s)} | Russia-bot score median: {med(ru_s)}")
    return {
        "exp_captures": exp_captures, "n": len(rows),
        "exp_gross_all": exp_gross_all, "ctl_gross_all": ctl_gross_all,
        "slot_wins": slot_wins, "draws": draws,
    }


if __name__ == "__main__":
    dirs = sys.argv[1:]
    if not dirs:
        print("usage: parse-s1-batch.py <result_dir> [calib:<dir>]"); sys.exit(1)
    for d in dirs:
        if d.startswith("calib:"):
            d = d[len("calib:"):]
            print(f"\n## CALIBRATION dir: {d}")
            summarize(load_matches(d), exp_bt="stable", ctl_bt="stable")
        else:
            print(f"\n## dir: {d}")
            summarize(load_matches(d))
