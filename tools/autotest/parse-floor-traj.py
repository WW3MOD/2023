#!/usr/bin/env python3
"""Per-game TECN floor-request trajectory, attributed by bot_type.

For each match_<i>.json + match_<i>_debug.log in a result dir, report for the
EXPERIMENTAL and STABLE bots (by notes.players[].bot_type, never slot/faction):
  max_alive   = max TECN alive seen in tecn-floor-request lines
  max_pending = max pending seen
  fin_pending = pending in the last floor-request line for that player
  floor       = last floor value requested
Answers: is the pending=82/alive=0 deadlock gone, and did the cap hold
(pending <= floor)?
"""
import json, re, sys, glob, os

RESULT_DIR = sys.argv[1]

floor_re = re.compile(
    r"tecn-floor-request player=(\S+).*?alive=(\d+) pending=(\d+) floor=(\d+)")

def load_notes(mj):
    with open(mj) as f:
        outer = json.load(f)
    n = outer.get("notes")
    if isinstance(n, str):
        n = json.loads(n)
    return n or {}

def main():
    print(f"{'#':>2} {'bot':>12} {'player':>10} "
          f"{'max_alive':>9} {'max_pend':>8} {'fin_pend':>8} {'floor':>5}")
    for mj in sorted(glob.glob(os.path.join(RESULT_DIR, "match_*.json")),
                     key=lambda p: int(re.search(r"match_(\d+)", p).group(1))):
        idx = int(re.search(r"match_(\d+)", mj).group(1))
        v = load_notes(mj)
        name2bot = {p.get("name"): p.get("bot_type") for p in v.get("players", [])}
        dbg = mj.replace(".json", "_debug.log")
        stats = {}  # player -> [max_alive, max_pend, fin_pend, floor]
        if os.path.exists(dbg):
            with open(dbg, encoding="utf-8", errors="replace") as f:
                for line in f:
                    if "tecn-floor-request" not in line:
                        continue
                    m = floor_re.search(line)
                    if not m:
                        continue
                    pl, al, pd, fl = m.group(1), int(m.group(2)), int(m.group(3)), int(m.group(4))
                    s = stats.setdefault(pl, [0, 0, 0, 0])
                    s[0] = max(s[0], al)
                    s[1] = max(s[1], pd)
                    s[2] = pd       # last wins
                    s[3] = fl       # last wins
        for pl, bot in sorted(name2bot.items(), key=lambda kv: kv[1] or ""):
            if not bot:
                continue
            s = stats.get(pl, [0, 0, 0, 0])
            print(f"{idx:>2} {bot:>12} {pl:>10} "
                  f"{s[0]:>9} {s[1]:>8} {s[2]:>8} {s[3]:>5}")

if __name__ == "__main__":
    main()
