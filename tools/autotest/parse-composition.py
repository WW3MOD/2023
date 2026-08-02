#!/usr/bin/env python3
"""WW3MOD tournament — per-unit-type composition table.

Answers "WHAT did each bot build and lose?" across a batch, using the additive
`unit_types` block added to the match verdict in verdict_version 7 (see
SerializeVerdict in engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs).

Attribution keys off notes.players[].bot_type — the established convention — so
mirror-matches and side/faction swaps aggregate into the right column regardless
of slot order.

This is a standalone renderer: it reads the same match_*.json the tournament
scripts already collect and touches none of them, so existing consumers are
unaffected. If a batch predates verdict_version 7 (no `unit_types`), it says so
and exits cleanly rather than erroring.

Usage:
    ./tools/autotest/parse-composition.py <batch-dir>
    ./tools/autotest/parse-composition.py <batch-dir> --csv
"""

import glob
import json
import os
import sys


def load_verdicts(batch_dir):
    """Yield each match's unwrapped verdict dict (skips init-failure rows)."""
    for path in sorted(glob.glob(os.path.join(batch_dir, "match_*.json"))):
        try:
            with open(path) as fh:
                outer = json.load(fh)
        except (OSError, ValueError):
            continue

        notes = outer.get("notes", "")
        try:
            verdict = json.loads(notes)
        except (ValueError, TypeError):
            continue

        if isinstance(verdict, dict):
            yield verdict


def aggregate(batch_dir):
    """Roll up unit_types per bot_type across every match in the batch.

    Returns (totals, match_count, saw_unit_types) where totals is
    { bot_type: { actor_type: {produced_count, produced_cost, lost_*, alive_*} } }.
    alive_* are summed across matches (i.e. end-of-match totals, not a single
    snapshot) — read them as "alive at the end of N matches combined".
    """
    fields = ("produced_count", "produced_cost", "lost_count", "lost_cost",
              "alive_count", "alive_value")
    totals = {}
    match_count = 0
    saw_unit_types = False

    for verdict in load_verdicts(batch_dir):
        match_count += 1
        for p in verdict.get("players", []):
            bot = p.get("bot_type") or "(unknown)"
            unit_types = p.get("unit_types")
            if not isinstance(unit_types, dict):
                continue

            saw_unit_types = True
            bucket = totals.setdefault(bot, {})
            for actor, stat in unit_types.items():
                acc = bucket.setdefault(actor, dict.fromkeys(fields, 0))
                for f in fields:
                    acc[f] += stat.get(f, 0)

    return totals, match_count, saw_unit_types


def render_text(totals, match_count):
    lines = []
    for bot in sorted(totals):
        lines.append(f"=== bot_type={bot}  (aggregated over {match_count} matches) ===")
        header = f"{'actor':<20} {'prod#':>6} {'prod$':>9} {'lost#':>6} {'lost$':>9} {'alive#':>7} {'alive$':>9}"
        lines.append(header)
        lines.append("-" * len(header))
        rows = totals[bot]
        for actor in sorted(rows):
            s = rows[actor]
            lines.append(
                f"{actor:<20} {s['produced_count']:>6} {s['produced_cost']:>9} "
                f"{s['lost_count']:>6} {s['lost_cost']:>9} "
                f"{s['alive_count']:>7} {s['alive_value']:>9}")
        lines.append("")
    return "\n".join(lines)


def render_csv(totals):
    out = ["bot_type,actor,produced_count,produced_cost,lost_count,lost_cost,alive_count,alive_value"]
    for bot in sorted(totals):
        for actor in sorted(totals[bot]):
            s = totals[bot][actor]
            out.append(",".join(str(x) for x in [
                bot, actor,
                s["produced_count"], s["produced_cost"],
                s["lost_count"], s["lost_cost"],
                s["alive_count"], s["alive_value"],
            ]))
    return "\n".join(out)


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    as_csv = "--csv" in argv

    if not args or not os.path.isdir(args[0]):
        sys.stderr.write(f"Usage: {argv[0]} <batch-dir> [--csv]\n")
        return 3

    batch_dir = args[0]
    totals, match_count, saw_unit_types = aggregate(batch_dir)

    if match_count == 0:
        sys.stderr.write(f"No match_*.json verdicts found in {batch_dir}.\n")
        return 3

    if not saw_unit_types:
        sys.stderr.write(
            f"No composition data (unit_types) in {batch_dir} -- "
            "these verdicts predate verdict_version 7. Nothing to render.\n")
        return 0

    print(render_csv(totals) if as_csv else render_text(totals, match_count))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
