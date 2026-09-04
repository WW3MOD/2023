"""Dump every actor RankAccumulation tracks, with the inputs its interval is derived from.

Mirrors RankAccumulation's constructor filter (Buildable + GainsExperience, no ^templates)
and RankAccrual.BaseBuildTimeTicks. Run from the repo root:  python WORKSPACE/mockups/roster_dump.py
"""

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "nav-guard"))

import miniyaml as my  # noqa: E402

MOD = ROOT / "mods" / "ww3mod"


def rules_files():
    text = (MOD / "mod.yaml").read_text(encoding="utf-8-sig")
    out, seen = [], False
    for raw in text.replace("\r\n", "\n").split("\n"):
        if raw.startswith("Rules:"):
            seen = True
            continue
        if seen:
            if raw.strip() == "" or raw.startswith("#"):
                continue
            if not raw.startswith(("\t", " ")):
                break
            out.append(raw.strip().split("|", 1)[1])
    return out


def main():
    sources = []
    for rel in rules_files():
        p = MOD / rel
        sources.append(my.parse(p.read_text(encoding="utf-8-sig")))

    tree = my.resolve(my.merge_files(sources))

    roster = []
    for name, node in tree.items():
        if name.startswith("^"):
            continue

        traits = {my.base_key(n.key) for n in node.nodes if not n.key.startswith("-")}
        if "Buildable" not in traits or "GainsExperience" not in traits:
            continue

        buildable = next(n for n in node.nodes if my.base_key(n.key) == "Buildable")
        valued = next((n for n in node.nodes if my.base_key(n.key) == "Valued"), None)
        tooltip = next((n for n in node.nodes if my.base_key(n.key) in ("Tooltip", "ActorTooltip")), None)

        cost = int(valued.child_value("Cost", "0")) if valued else 0
        duration = int(buildable.child_value("BuildDuration", "-1"))
        modifier = int(buildable.child_value("BuildDurationModifier", "100"))
        queue = my.split_list(buildable.child_value("Queue", "")) or []

        base = cost // 10 if duration == -1 else duration
        build_ticks = max(1, base * modifier // 100)

        roster.append({
            "name": name,
            "label": (tooltip.child_value("Name", name) if tooltip else name) or name,
            "cost": cost,
            "buildDuration": duration,
            "buildDurationModifier": modifier,
            "queue": queue,
            "buildTicks": build_ticks,
        })

    roster.sort(key=lambda r: (r["buildTicks"], r["name"]))
    print(json.dumps(roster, indent=1))


if __name__ == "__main__":
    main()
