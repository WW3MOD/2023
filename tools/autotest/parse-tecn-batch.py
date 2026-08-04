#!/usr/bin/env python3
"""Extract per-game oilb-held + max-TECN-fielded (by bot_type) for the TECN batch.

Reads each match_<i>.json (verdict + notes.players[].bot_type) and
match_<i>_debug.log ([exp-capture] telemetry) from a tournament result dir.

oilb held  = # of oilb# entries in the LAST ownership-snapshot per owner name.
max TECN   = max total-tecns seen in no-idle-capturers lines per player name.
Attribution: strictly by notes.players[].bot_type (never slot/faction).
"""
import json, re, sys, glob, os

RESULT_DIR = sys.argv[1] if len(sys.argv) > 1 else \
    "tools/autotest/tournament-results/260731_streak_exp_vs_stable0730_tecn_s2combat"

def load_notes(match_json):
    """The verdict's `notes` is a JSON *string*. Parse and return the dict."""
    with open(match_json) as f:
        outer = json.load(f)
    notes = outer.get("notes")
    if isinstance(notes, str):
        notes = json.loads(notes)
    return notes or {}

def player_bot_map(match_json):
    """name -> bot_type from the verdict's notes.players."""
    m = {}
    v = load_notes(match_json)
    for p in v.get("players", []):
        nm = p.get("name")
        bt = p.get("bot_type")
        if nm:
            m[nm] = bt
    return m, v

def parse_debug(debug_log):
    """Return (last_oilb_by_owner, max_tecn_by_player).
    last_oilb_by_owner: owner_name -> count of oilb# in latest snapshot for that owner.
    """
    # For oilb we want, at the final tick that has snapshots, the count per owner.
    # Snapshots are emitted per observer; owner field is the actual holder.
    snap_re = re.compile(r"ownership-snapshot observer=(\S+) owner=(\S+) count=(\d+) held=(\S*) tick=(\d+)")
    # Loss-analysis "max TECN" metric = max over samples of (total-tecns + committed),
    # so the new batch's numbers line up with WORKSPACE/recon/260731-loss-analysis-*.
    tecn_re = re.compile(r"no-idle-capturers player=(\S+) total-tecns=(\d+) committed=(\d+) idle=(\d+)")
    floor_re = re.compile(r"tecn-floor-request player=(\S+).*alive=(\d+) pending=(\d+)")

    # oilb: track by (owner) -> (tick, count) keep latest tick
    oilb_latest = {}   # owner -> (tick, oilb_count)
    max_tecn = {}      # player -> max total-tecns
    max_alive = {}     # player -> max alive (from floor-request)
    with open(debug_log, encoding="utf-8", errors="replace") as f:
        for line in f:
            if "ownership-snapshot" in line:
                mm = snap_re.search(line)
                if mm:
                    owner = mm.group(2)
                    held = mm.group(4)
                    tick = int(mm.group(5))
                    if owner in ("Neutral",):
                        # still record for completeness but not a bot
                        pass
                    oilb_count = len(re.findall(r"oilb#", held))
                    prev = oilb_latest.get(owner)
                    if prev is None or tick >= prev[0]:
                        oilb_latest[owner] = (tick, oilb_count)
            elif "no-idle-capturers" in line:
                mm = tecn_re.search(line)
                if mm:
                    pl = mm.group(1); tot = int(mm.group(2)); comm = int(mm.group(3))
                    max_tecn[pl] = max(max_tecn.get(pl, 0), tot + comm)
            elif "tecn-floor-request" in line:
                mm = floor_re.search(line)
                if mm:
                    pl = mm.group(1); alive = int(mm.group(2))
                    max_alive[pl] = max(max_alive.get(pl, 0), alive)
    return oilb_latest, max_tecn, max_alive

def main():
    rows = []
    for mj in sorted(glob.glob(os.path.join(RESULT_DIR, "match_*.json")),
                     key=lambda p: int(re.search(r"match_(\d+)", p).group(1))):
        idx = int(re.search(r"match_(\d+)", mj).group(1))
        dbg = mj.replace(".json", "_debug.log")
        names, v = player_bot_map(mj)
        winner = v.get("winner_name")
        reason = v.get("win_reason")
        scores = {}
        for p in v.get("players", []):
            scores[p.get("name")] = p.get("score_total")
        oilb_latest, max_tecn, max_alive = ({}, {}, {})
        if os.path.exists(dbg):
            oilb_latest, max_tecn, max_alive = parse_debug(dbg)
        # attribute
        by_bot = {}
        for nm, bt in names.items():
            if not bt:
                continue
            oilb = oilb_latest.get(nm, (None, 0))[1]
            tecn = max(max_tecn.get(nm, 0), max_alive.get(nm, 0))
            by_bot[bt] = {"name": nm, "oilb": oilb, "tecn": tecn, "score": scores.get(nm)}
        win_bot = names.get(winner)
        rows.append({"idx": idx, "winner": winner, "win_bot": win_bot,
                     "reason": reason, "by_bot": by_bot,
                     "oilb_neutral": oilb_latest.get("Neutral", (None, None))[1]})
    # print table
    print(f"{'#':>2} {'win_bot':>12} {'reason':>12} | "
          f"{'exp_oilb':>8} {'sta_oilb':>8} {'exp_tecn':>8} {'sta_tecn':>8} "
          f"{'exp_score':>10} {'sta_score':>10} {'neutral_oilb':>12}")
    exp_w = sta_w = 0
    tecn_fired = 0
    exp_oilb_tot = sta_oilb_tot = 0
    for r in rows:
        e = r["by_bot"].get("experimental", {})
        s = r["by_bot"].get("stable", {})
        if r["win_bot"] == "experimental": exp_w += 1
        elif r["win_bot"] == "stable": sta_w += 1
        if (e.get("tecn") or 0) > 0: tecn_fired += 1
        exp_oilb_tot += (e.get("oilb") or 0); sta_oilb_tot += (s.get("oilb") or 0)
        print(f"{r['idx']:>2} {str(r['win_bot']):>12} {str(r['reason']):>12} | "
              f"{str(e.get('oilb')):>8} {str(s.get('oilb')):>8} "
              f"{str(e.get('tecn')):>8} {str(s.get('tecn')):>8} "
              f"{str(e.get('score')):>10} {str(s.get('score')):>10} "
              f"{str(r['oilb_neutral']):>12}")
    print(f"\nExperimental {exp_w} / Stable {sta_w}  (N={len(rows)})")
    print(f"TECN fired (exp tecn>0): {tecn_fired}/{len(rows)}")
    print(f"oilb held totals: exp={exp_oilb_tot}  sta={sta_oilb_tot}")

if __name__ == "__main__":
    main()
