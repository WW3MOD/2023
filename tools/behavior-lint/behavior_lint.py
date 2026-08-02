#!/usr/bin/env python3
"""WW3MOD behavior-lint — read one .lifecycle.jsonl and flag AI unit anti-patterns.

Consumes the per-unit JSONL event stream emitted by the UnitLifecycleLogger world
trait (engine/OpenRA.Mods.Common/Traits/World/UnitLifecycleLogger.cs), which the
autotest harness produces when run with `run-test.sh --lifecycle`. See
WORKSPACE/behavior-lint-spec.md for the schema and rule catalogue.

First slice rules (spec §2d): R1 (under-tasked), R2 (excessive idle),
R6 (died untasked), R8 (end-of-game idle census). Plus `--actor <aid>` for a
full per-unit timeline drill-down.

This is advisory: it prints WARN lines and exits non-zero if any fired (so a
batch/CI can gate on it), but it never speaks to a match's pass/fail verdict.
A missing file, or one that predates schema:1, prints a one-line note and exits 0
(the parse-composition.py "nothing to render" convention).

Usage:
    ./tools/behavior-lint/behavior_lint.py <file.lifecycle.jsonl>
    ./tools/behavior-lint/behavior_lint.py <file> --actor 413
    ./tools/behavior-lint/behavior_lint.py <file> --warn-only
    ./tools/behavior-lint/behavior_lint.py <file> --json
    ./tools/behavior-lint/behavior_lint.py <file> --idle-total 2000 --idle-span 900
"""

import json
import statistics
import sys

# Rule thresholds (CLI-overridable via --<name> N). Starting points to tune
# against real logs — see spec §2b.
DEFAULTS = {
    "r1_max_orders": 1,     # R1: <= this many orders over a lifetime = under-tasked
    "idle_total": 1500,     # R2: total idle ticks (~60s @ 25t/s)
    "idle_span": 750,       # R2: single idle span ticks (~30s)
    "min_life": 250,        # R1/R6: ignore units that lived < this (late call-ins
                            # aren't "forgotten" — they haven't had time yet, ~10s)
}


def load(path):
    """Stream the JSONL once into a structured model.

    Returns (meta, units) or (None, None) when the file has no schema:1 meta line
    (missing/old/empty), so the caller can print a note and exit 0.
    """
    meta = None
    units = {}
    saw_meta = False

    def unit(aid):
        return units.setdefault(aid, {
            "aid": aid, "type": None, "owner": None,
            "spawn_tick": None, "cost": 0, "last_tick": None, "mobile": None,
            "orders": [], "idle_events": [], "death": None, "end": None,
        })

    try:
        fh = open(path)
    except OSError:
        return None, None

    with fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                e = json.loads(line)
            except ValueError:
                continue

            ev = e.get("ev")
            if ev == "meta":
                if e.get("schema") == 1:
                    meta = e
                    saw_meta = True
                continue

            t = e.get("t")
            if ev == "order":
                subj = e.get("subj", -1)
                # An order's subject may be an untracked queue actor (-1 or a
                # production queue) — record only orders aimed at tracked units.
                if subj is not None and subj >= 0:
                    u = unit(subj)
                    u["orders"].append(e)
                    if t is not None:
                        u["last_tick"] = max(u["last_tick"] or t, t)
                continue

            aid = e.get("aid")
            if aid is None:
                continue
            u = unit(aid)
            if t is not None:
                u["last_tick"] = max(u["last_tick"] or t, t)

            if ev == "spawn":
                u["type"] = e.get("type")
                u["owner"] = e.get("owner")
                u["spawn_tick"] = t
                u["cost"] = e.get("cost", 0)
                u["mobile"] = e.get("mobile")
            elif ev in ("idle_start", "idle_end"):
                u["idle_events"].append(e)
            elif ev == "death":
                u["death"] = e
            elif ev == "end":
                u["end"] = e
                if u["type"] is None:
                    u["type"] = e.get("type")
                if u["owner"] is None:
                    u["owner"] = e.get("owner")

    if not saw_meta:
        return None, None
    return meta, units


def match_window(meta, units):
    """(first_tick, last_tick) across all events — the whole-match 'at war' window.

    The first slice has no per-side SR-loss marker, so 'while owner at war' is the
    whole match (spec §2b notes SR-loss narrowing is a full-build follow-up).
    """
    ticks = []
    for u in units.values():
        for key in ("spawn_tick", "last_tick"):
            if u[key] is not None:
                ticks.append(u[key])
        for e in u["idle_events"]:
            if e.get("t") is not None:
                ticks.append(e["t"])
        for e in u["orders"]:
            if e.get("t") is not None:
                ticks.append(e["t"])
        if u["death"] and u["death"].get("t") is not None:
            ticks.append(u["death"]["t"])
        if u["end"] and u["end"].get("t") is not None:
            ticks.append(u["end"]["t"])
    if not ticks:
        return 0, 0
    return min(ticks), max(ticks)


def idle_spans(u, match_end):
    """Reconstruct (start, end, dur, terr) idle spans by pairing idle_start/idle_end.

    A trailing unmatched idle_start (unit still idle at death/match end) is closed
    at the unit's exit tick, so the analyzer's totals match the trait's — including
    the final open span the minimal death/end lines don't re-emit as an idle_end.
    """
    spans = []
    open_start = None
    open_terr = "unknown"
    for e in sorted(u["idle_events"], key=lambda x: x.get("t", 0)):
        if e["ev"] == "idle_start":
            open_start = e.get("t")
            open_terr = e.get("terr", "unknown")
        elif e["ev"] == "idle_end" and open_start is not None:
            end_t = e.get("t", open_start)
            spans.append((open_start, end_t, end_t - open_start, open_terr))
            open_start = None
            open_terr = "unknown"

    if open_start is not None:
        # Still idle at exit: close at death tick, else end census tick, else match end.
        if u["death"] and u["death"].get("t") is not None:
            close = u["death"]["t"]
        elif u["end"] and u["end"].get("t") is not None:
            close = u["end"]["t"]
        else:
            close = match_end
        spans.append((open_start, close, max(0, close - open_start), open_terr))
    return spans


def summarize(u, match_end):
    spans = idle_spans(u, match_end)
    total_idle = sum(s[2] for s in spans)
    longest = max(spans, key=lambda s: s[2]) if spans else None
    # End census is authoritative for survivors (it includes the live open span
    # measured by the trait). Prefer it when present.
    end = u["end"]
    if end is not None:
        total_idle = end.get("total_idle", total_idle)
        end_longest = end.get("longest_idle", longest[2] if longest else 0)
    else:
        end_longest = longest[2] if longest else 0

    death_tick = u["death"]["t"] if u["death"] else None
    exit_tick = death_tick if death_tick is not None else match_end
    spawn_tick = u["spawn_tick"]
    lifetime = (exit_tick - spawn_tick) if spawn_tick is not None else 0

    # Missing `mobile` (pre-field logs) defaults to mover so nothing is silently
    # hidden; the trait always stamps it now (1 for Mobile/Aircraft, 0 structures).
    mobile = u["mobile"] if u["mobile"] is not None else True

    return {
        "aid": u["aid"],
        "type": u["type"] or "?",
        "owner": u["owner"] if u["owner"] is not None else -1,
        "orders": len(u["orders"]),
        "spawn_tick": spawn_tick,
        "mobile": bool(mobile),
        "lifetime": lifetime,
        "total_idle": total_idle,
        "longest_idle": end_longest,
        "longest_span": longest,
        "died": u["death"] is not None,
        "death_tick": death_tick,
        "survived": end is not None,
        "end_idle": bool(end.get("idle")) if end else False,
        "spans": spans,
    }


def run_rules(units, match_window_ticks, cfg):
    first_tick, last_tick = match_window_ticks
    warns = []
    summaries = {}
    for aid, u in units.items():
        summaries[aid] = summarize(u, last_tick)

    # The "forgotten unit" rules apply only to combat MOVERS. Structures carry
    # UpdatesPlayerStatistics (so the trait tracks them) but are inherently idle
    # and never ordered — flagging them would bury the real signal (movers that
    # got one order and were abandoned). Mobility comes from the spawn `mobile`
    # field (IPositionableInfo). Rules also skip late call-ins that lived too
    # briefly to have been "forgotten".
    for aid, s in summaries.items():
        # R1 — under-tasked: <= r1_max_orders over lifetime. Only units we saw
        # spawn (a real call-in), so untracked-subject order noise can't phantom.
        if (s["mobile"] and units[aid]["spawn_tick"] is not None
                and s["lifetime"] >= cfg["min_life"]
                and s["orders"] <= cfg["r1_max_orders"]):
            warns.append(("R1", aid, s,
                          f"orders={s['orders']} (<= {cfg['r1_max_orders']}) over lifetime"))

        # R2 — excessive idle while at war.
        if s["mobile"] and (s["total_idle"] > cfg["idle_total"]
                            or s["longest_idle"] > cfg["idle_span"]):
            terr = s["longest_span"][3] if s["longest_span"] else "?"
            warns.append(("R2", aid, s,
                          f"idle_total={s['total_idle']}t longest={s['longest_idle']}t terr={terr}"))

        # R6 — died with zero orders (and lived long enough to be commandable).
        if (s["mobile"] and s["died"] and s["orders"] == 0
                and s["lifetime"] >= cfg["min_life"]):
            warns.append(("R6", aid, s,
                          f"died t={s['death_tick']} orders=0"))

    return warns, summaries


def census_by_type(summaries):
    """R8 — per type: survivors, idle-at-end count/fraction, median total_idle.

    Movers only — a structure census ("supplyroute idle=1") is noise; the view
    exists to answer "which units were standing around when the game ended".
    """
    by_owner_type = {}
    for s in summaries.values():
        if not s["survived"] or not s["mobile"]:
            continue
        key = (s["owner"], s["type"])
        by_owner_type.setdefault(key, []).append(s)

    rows = {}
    for (owner, typ), group in by_owner_type.items():
        alive = len(group)
        idle = sum(1 for g in group if g["end_idle"])
        med = int(statistics.median([g["total_idle"] for g in group])) if group else 0
        rows.setdefault(owner, []).append({
            "type": typ, "alive": alive, "idle": idle,
            "frac": (idle / alive) if alive else 0.0, "median_total_idle": med,
        })
    return rows


def render_report(meta, warns, summaries, cfg, warn_only=False):
    lines = []
    scenario = meta.get("scenario", "?")
    seed = meta.get("seed", "?")
    players = meta.get("players", [])
    pstr = " ".join(f"{p.get('ci')}={p.get('bot_type') or '?'}" for p in players)
    lines.append(f"== behavior-lint: {scenario} seed={seed}  (players: {pstr}) ==")

    order = {"R1": 0, "R2": 1, "R6": 2}
    for rule, aid, s, detail in sorted(warns, key=lambda w: (order.get(w[0], 9), w[1])):
        lines.append(
            f"WARN {rule}  aid={aid} type={s['type']} owner={s['owner']}  {detail}")

    if not warn_only:
        census = census_by_type(summaries)
        for owner in sorted(census):
            lines.append(f"R8 end-of-game idle census (owner {owner}):")
            for row in sorted(census[owner], key=lambda r: r["type"]):
                lines.append(
                    f"  {row['type']:<12} alive={row['alive']:<3} "
                    f"idle={row['idle']} ({int(round(row['frac'] * 100))}%)  "
                    f"median_total_idle={row['median_total_idle']}t")

    rules_fired = sorted({w[0] for w in warns})
    r6_count = sum(1 for w in warns if w[0] == "R6")
    lines.append(
        f"Summary: {len(warns)} WARN across {len(rules_fired)} rules"
        + (f"; {r6_count} units flagged R6." if r6_count else "."))
    if warns:
        example = warns[0][1]
        lines.append(f"  drill:  ./tools/behavior-lint/behavior_lint.py <file> --actor {example}")
    return "\n".join(lines)


def render_actor(meta, units, aid, match_end):
    u = units.get(aid)
    if u is None:
        return f"aid={aid}: not found in this log."
    s = summarize(u, match_end)
    out = [f"== aid={aid} type={s['type']} owner={s['owner']} =="]
    out.append(f"spawn t={u['spawn_tick']} cost={u['cost']}")
    out.append(f"orders: {s['orders']}")
    for o in sorted(u["orders"], key=lambda x: x.get("t", 0)):
        tgt = ""
        if o.get("tactor", -1) >= 0:
            tgt = f" -> actor#{o['tactor']} @({o.get('tx')},{o.get('ty')})"
        elif o.get("tx", -1) >= 0:
            tgt = f" -> cell({o.get('tx')},{o.get('ty')})"
        q = " [queued]" if o.get("queued") else ""
        out.append(f"  t={o.get('t')} mod={o.get('mod') or '?'} ord={o.get('ord')}{tgt}{q}")
    out.append(f"idle spans: {len(s['spans'])}  total_idle={s['total_idle']}t longest={s['longest_idle']}t")
    for st, en, dur, terr in s["spans"]:
        out.append(f"  [{st}..{en}] dur={dur}t terr={terr}")
    if s["died"]:
        d = u["death"]
        out.append(f"death t={d.get('t')} at ({d.get('x')},{d.get('y')}) orders={d.get('orders')} terr={d.get('terr')}")
    if s["survived"]:
        e = u["end"]
        out.append(f"end t={e.get('t')} idle={bool(e.get('idle'))} terr={e.get('terr')} "
                   f"total_idle={e.get('total_idle')}t longest_idle={e.get('longest_idle')}t")
    return "\n".join(out)


def parse_args(argv):
    cfg = dict(DEFAULTS)
    positional = []
    actor = None
    flags = {"warn_only": False, "json": False, "csv": False}
    i = 1
    while i < len(argv):
        a = argv[i]
        if a == "--warn-only":
            flags["warn_only"] = True
        elif a == "--json":
            flags["json"] = True
        elif a == "--csv":
            flags["csv"] = True
        elif a == "--actor":
            i += 1
            actor = int(argv[i])
        elif a.startswith("--actor="):
            actor = int(a.split("=", 1)[1])
        elif a.startswith("--"):
            # Threshold override: --idle-total 2000 or --idle-total=2000.
            key = a[2:].replace("-", "_")
            if "=" in key:
                key, val = key.split("=", 1)
            else:
                i += 1
                val = argv[i]
            if key in cfg:
                cfg[key] = int(val)
            else:
                sys.stderr.write(f"Unknown flag: {a}\n")
                return None
        else:
            positional.append(a)
        i += 1
    return cfg, positional, actor, flags


def main(argv):
    parsed = parse_args(argv)
    if parsed is None:
        return 3
    cfg, positional, actor, flags = parsed

    if not positional:
        sys.stderr.write(f"Usage: {argv[0]} <file.lifecycle.jsonl> [--actor N] "
                         "[--warn-only] [--json] [--idle-total N] [--idle-span N]\n")
        return 3

    path = positional[0]
    meta, units = load(path)
    if meta is None:
        print(f"behavior-lint: no schema:1 lifecycle data in {path} -- nothing to render.")
        return 0

    window = match_window(meta, units)

    if actor is not None:
        print(render_actor(meta, units, actor, window[1]))
        return 0

    warns, summaries = run_rules(units, window, cfg)

    if flags["json"]:
        payload = {
            "scenario": meta.get("scenario"),
            "seed": meta.get("seed"),
            "warns": [{"rule": w[0], "aid": w[1], "type": w[2]["type"],
                       "owner": w[2]["owner"], "detail": w[3]} for w in warns],
            "census": census_by_type(summaries),
        }
        print(json.dumps(payload, indent=2, default=list))
        return 1 if warns else 0

    if flags["csv"]:
        print("rule,aid,type,owner,detail")
        for rule, aid, s, detail in warns:
            print(f"{rule},{aid},{s['type']},{s['owner']},{detail}")
        return 1 if warns else 0

    print(render_report(meta, warns, summaries, cfg, warn_only=flags["warn_only"]))
    return 1 if warns else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
