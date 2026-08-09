#!/usr/bin/env python3
"""nav-guard -- catch movement/blocking changes that seal part of a map off.

Per map and per locomotor, decodes terrain from map.bin, places the statically-authored
blocking actors from map.yaml, builds the 8-connected movement graph the pathfinder would
see, and measures the largest connected component. A committed baseline turns "the biggest
walkable region got smaller" into a build failure instead of something a reviewer has to
happen to think about.

    ./nav_guard.py validate    decoder self-check against the checked-in map.png previews
    ./nav_guard.py report      per-map/per-locomotor component table
    ./nav_guard.py check       compare against baseline.json; non-zero exit on a shrink
    ./nav_guard.py bless       record current numbers as the new baseline
    ./nav_guard.py compare     diff two diagonal-squeeze rule variants against each other

See README.md for what is modelled and -- more importantly -- what is not.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import deque
from dataclasses import dataclass
from pathlib import Path

import modload
from modload import ActorShape, GameMap, Locomotor, ModRules, Tileset

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
MOD_DIR = REPO / "mods" / "ww3mod"
BASELINE = HERE / "baseline.json"

# Diagonal-squeeze rule variants. A diagonal step crosses the corner shared by four cells;
# the two cells that are neither endpoint are its "shoulders" (DiagonalSqueezeGeometry.cs).
#   none    no squeeze rule at all -- movement before the 2026-08-08 tank-trap work.
#   generic both shoulders impassable blocks the step. The first, reverted implementation
#           (b164a312): terrain, map edge, trees, rocks, walls all count as shoulders.
#   tagged  both shoulders hold a BlocksDiagonalSqueeze actor. Shipped (be036370).
SQUEEZE_VARIANTS = ("none", "generic", "tagged")
DEFAULT_SQUEEZE = "tagged"

NEIGHBOURS = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)]


# --------------------------------------------------------------------------- cell model

@dataclass
class CellModel:
    """Per-(map, locomotor) passability, independent of the squeeze variant."""
    game_map: GameMap
    locomotor: Locomotor
    left: int
    top: int
    width: int
    height: int
    passable: bytearray        # indexed [y * width + x] in bounds-local coords
    squeeze_tag: bytearray     # cell holds a BlocksDiagonalSqueeze actor

    def index(self, x: int, y: int) -> int:
        return y * self.width + x


def cell_occupancy(rules: ModRules, game_map: GameMap,
                   state: str = "live") -> tuple[dict[tuple[int, int], list[ActorShape]], set[str]]:
    """Map cell -> the shapes covering it. Second return is the set of unknown actor names.

    state="dead" substitutes every actor's SpawnActorOnDeath husk. That is the worst case
    for a late-game map: tree husks lose the `tree` pass class that let infantry through,
    and several occupy more cells than the tree they replace.
    """
    occupancy: dict[tuple[int, int], list[ActorShape]] = {}
    unknown: set[str] = set()
    cache: dict[str, ActorShape | None] = {}

    for placed in game_map.actors:
        if placed.name not in cache:
            name = placed.name
            if state == "dead":
                name = modload.husk_of(rules, placed.name) or placed.name
            node = rules.actor(name)
            cache[placed.name] = modload.actor_shape(name, node) if node else None
        shape = cache[placed.name]
        if shape is None:
            unknown.add(placed.name)
            continue
        ox, oy = placed.location
        for dx, dy in shape.blocking:
            occupancy.setdefault((ox + dx, oy + dy), []).append(shape)

    return occupancy, unknown


def build_cell_model(rules: ModRules, game_map: GameMap, tileset: Tileset,
                     locomotor: Locomotor,
                     occupancy: dict[tuple[int, int], list[ActorShape]]) -> CellModel:
    left, top, width, height = game_map.bounds
    passable = bytearray(width * height)
    squeeze_tag = bytearray(width * height)

    for y in range(height):
        for x in range(width):
            cx, cy = left + x, top + y
            i = y * width + x
            shapes = occupancy.get((cx, cy), ())

            # Locomotor.CellBlocksCorner asks only whether the cell holds a tagged actor;
            # it never consults passability. Tagging must therefore happen before the
            # terrain test, or a trap on terrain this locomotor cannot cross stops
            # counting as a shoulder.
            for shape in shapes:
                if shape.blocks_squeeze:
                    squeeze_tag[i] = 1
                    break

            if not locomotor.terrain_passable(game_map.terrain_type(tileset, cx, cy)):
                continue

            blocked = False
            for shape in shapes:
                if shape.mobile:
                    # A unit can be told to move; it is not a wall. Matches Locomotor's
                    # HasMovableActor handling for the BlockedByActor.Immovable check
                    # that ordinary movement orders use.
                    continue
                # Locomotor.IsBlockedBy: we pass only if the actor is Passable in a class
                # this locomotor lists under `Passes`.
                if shape.pass_classes & locomotor.passes and shape.passed_by_anyone:
                    continue
                blocked = True
                break

            if not blocked:
                passable[i] = 1

    return CellModel(game_map, locomotor, left, top, width, height, passable, squeeze_tag)


# ------------------------------------------------------------------------- connectivity

def squeeze_blocks(model: CellModel, variant: str,
                   ax: int, ay: int, bx: int, by: int) -> bool:
    """Is the diagonal step a->b denied by the squeeze rule? Symmetric by construction."""
    if variant == "none":
        return False
    if variant == "tagged" and model.locomotor.shares_cell:
        # Locomotor.IsDiagonalSqueeze bails on SharesCell: a subcell occupant can slip
        # past a corner. The reverted generic version had no such guard, which is one
        # reason it bit infantry-adjacent geometry too.
        return False

    shoulders = ((ax, by), (bx, ay))
    for sx, sy in shoulders:
        lx, ly = sx - model.left, sy - model.top
        inside = 0 <= lx < model.width and 0 <= ly < model.height
        if variant == "generic":
            # b164a312 CellBlocksCorner: off-map returns the unreachable cost, so the map
            # edge counts as solid; otherwise any cell this locomotor cannot enter counts.
            if inside and model.passable[ly * model.width + lx]:
                return False
        else:  # tagged
            if not inside or not model.squeeze_tag[ly * model.width + lx]:
                return False
    return True


def components(model: CellModel, variant: str) -> list[int]:
    """Connected component sizes, largest first."""
    width, height = model.width, model.height
    passable = model.passable
    seen = bytearray(width * height)
    sizes: list[int] = []

    for start in range(width * height):
        if not passable[start] or seen[start]:
            continue
        seen[start] = 1
        queue = deque([start])
        size = 0
        while queue:
            cur = queue.popleft()
            size += 1
            cy, cx = divmod(cur, width)
            for dx, dy in NEIGHBOURS:
                nx, ny = cx + dx, cy + dy
                if not (0 <= nx < width and 0 <= ny < height):
                    continue
                n = ny * width + nx
                if seen[n] or not passable[n]:
                    continue
                if dx and dy and squeeze_blocks(model, variant,
                                                model.left + cx, model.top + cy,
                                                model.left + nx, model.top + ny):
                    continue
                seen[n] = 1
                queue.append(n)
        sizes.append(size)

    sizes.sort(reverse=True)
    return sizes


def component_labels(model: CellModel, variant: str) -> tuple[list[int], list[int]]:
    """(label per cell, size per label). Label -1 means impassable. For pocket listing."""
    width, height = model.width, model.height
    passable = model.passable
    labels = [-1] * (width * height)
    sizes: list[int] = []

    for start in range(width * height):
        if not passable[start] or labels[start] >= 0:
            continue
        label = len(sizes)
        labels[start] = label
        queue = deque([start])
        size = 0
        while queue:
            cur = queue.popleft()
            size += 1
            cy, cx = cur // width, cur % width
            for dx, dy in NEIGHBOURS:
                nx, ny = cx + dx, cy + dy
                if not (0 <= nx < width and 0 <= ny < height):
                    continue
                n = ny * width + nx
                if labels[n] >= 0 or not passable[n]:
                    continue
                if dx and dy and squeeze_blocks(model, variant,
                                                model.left + cx, model.top + cy,
                                                model.left + nx, model.top + ny):
                    continue
                labels[n] = label
                queue.append(n)
        sizes.append(size)

    return labels, sizes


# ------------------------------------------------------------------------------ analysis

@dataclass
class MapResult:
    name: str
    per_locomotor: dict[str, dict[str, int]]
    unknown_actors: list[str]


def analyse(variant: str = DEFAULT_SQUEEZE, only_maps: list[str] | None = None,
            only_locos: list[str] | None = None,
            state: str = "live") -> tuple[ModRules, list[GameMap], list[MapResult]]:
    rules = modload.load_mod(MOD_DIR)
    maps = [modload.load_map(p) for p in modload.discover_maps(MOD_DIR)]
    if only_maps:
        maps = [m for m in maps if any(f in m.name for f in only_maps)]

    results: list[MapResult] = []
    for game_map in maps:
        tileset = rules.tilesets[game_map.tileset]
        locos = modload.world_locomotors(rules, game_map.rule_overrides)
        if only_locos:
            locos = [loco for loco in locos if loco.name in only_locos]
        occupancy, unknown = cell_occupancy(rules, game_map, state)

        per_loco: dict[str, dict[str, int]] = {}
        for loco in locos:
            model = build_cell_model(rules, game_map, tileset, loco, occupancy)
            sizes = components(model, variant)
            total = sum(sizes)
            per_loco[loco.name] = {
                "passable": total,
                "largest": sizes[0] if sizes else 0,
                "components": len(sizes),
                "pocketed": total - (sizes[0] if sizes else 0),
            }
        results.append(MapResult(game_map.name, per_loco, sorted(unknown)))

    return rules, maps, results


# ------------------------------------------------------------------------------ commands

def scripted_blockers() -> list[str]:
    """Actor types named in map Lua that would be static blockers if spawned.

    nav-guard reads only the statically-authored Actors: block, so anything a scenario
    scripts into place is invisible to it. Rather than leave that as prose in a README,
    this looks for the case actually arising: a quoted string in a map's Lua that names an
    immobile, non-passable actor. Mobile reinforcements are ignored -- a unit that can be
    ordered out of the way is not a wall.
    """
    rules = modload.load_mod(MOD_DIR)
    found: list[str] = []
    for lua in sorted((MOD_DIR / "maps").glob("*/*.lua")):
        text = lua.read_text(encoding="utf-8", errors="replace")
        for token in sorted(set(re.findall(r"[\"']([A-Za-z][A-Za-z0-9._-]{1,24})[\"']", text))):
            node = rules.actor(token)
            if node is None:
                continue
            shape = modload.actor_shape(token, node)
            if shape.mobile or not shape.blocking:
                continue
            if shape.pass_classes:
                continue
            found.append(f"{lua.parent.name}/{lua.name}: '{token}'")
    return found


def cmd_report(args) -> int:
    _, _, results = analyse(args.squeeze, args.map, args.locomotor, args.state)
    print(f"nav-guard report  (squeeze: {args.squeeze}, world state: {args.state})\n")
    for res in results:
        print(res.name)
        if res.unknown_actors:
            print(f"  ! actor types not found in rules: {', '.join(res.unknown_actors)}")
        print(f"    {'locomotor':<32}{'passable':>10}{'largest':>10}{'comps':>7}{'pocketed':>10}")
        for loco, m in sorted(res.per_locomotor.items()):
            flag = "  <-- pockets" if m["pocketed"] else ""
            print(f"    {loco:<32}{m['passable']:>10}{m['largest']:>10}"
                  f"{m['components']:>7}{m['pocketed']:>10}{flag}")
        print()
    return 0


def cmd_bless(args) -> int:
    states = {}
    for state in ("live", "dead"):
        _, _, results = analyse(args.squeeze, state=state)
        states[state] = {r.name: r.per_locomotor for r in results}
    payload = {
        "_comment": "nav-guard baseline -- largest connected component per map and "
                    "locomotor. Regenerate with ./nav_guard.py bless after a deliberate, "
                    "reviewed map or movement-rule change, and review the diff.",
        "_states": {"live": "map actors as authored",
                    "dead": "every destructible map actor replaced by its husk"},
        "squeeze_variant": args.squeeze,
        "states": states,
    }
    # newline="" keeps LF on Windows: the baseline is committed and diffed by reviewers,
    # so it must not flip line endings depending on who ran bless.
    with BASELINE.open("w", encoding="utf-8", newline="") as f:
        f.write(json.dumps(payload, indent=2, sort_keys=True) + "\n")
    total = sum(len(v) for v in states["live"].values())
    print(f"Wrote {BASELINE.relative_to(REPO)}: {len(states['live'])} maps, "
          f"{total} map/locomotor pairs x 2 world states.")
    return 0


def _diff_state(results: list[MapResult], recorded: dict) -> tuple[list[str], list[str]]:
    shrinks: list[str] = []
    notes: list[str] = []
    for res in results:
        base_map = recorded.get(res.name)
        if base_map is None:
            notes.append(f"{res.name}: new map, not in baseline")
            continue
        for loco, m in sorted(res.per_locomotor.items()):
            was = base_map.get(loco)
            if was is None:
                notes.append(f"{res.name}/{loco}: new locomotor, not in baseline")
            elif m["largest"] < was["largest"]:
                shrinks.append(
                    f"{res.name}/{loco}: largest {was['largest']} -> {m['largest']} "
                    f"({was['largest'] - m['largest']} cells lost); "
                    f"pocketed {was['pocketed']} -> {m['pocketed']}, "
                    f"passable {was['passable']} -> {m['passable']}")
            elif m != was:
                notes.append(f"{res.name}/{loco}: {was} -> {m}")
    for name in recorded:
        if not any(r.name == name for r in results):
            notes.append(f"{name}: in baseline but no longer present")
    return shrinks, notes


def cmd_check(args) -> int:
    if not BASELINE.exists():
        print("nav-guard: no baseline.json. Run ./nav_guard.py bless first.", file=sys.stderr)
        return 2

    baseline = json.loads(BASELINE.read_text(encoding="utf-8"))
    variant = baseline.get("squeeze_variant", DEFAULT_SQUEEZE)

    _, _, live = analyse(variant, state="live")
    live_shrinks, live_notes = _diff_state(live, baseline["states"]["live"])

    _, _, dead = analyse(variant, state="dead")
    dead_shrinks, dead_notes = _diff_state(dead, baseline["states"]["dead"])

    scripted = scripted_blockers()

    if live_shrinks:
        print("nav-guard FAIL: the largest reachable region got smaller.\n")
        for s in live_shrinks:
            print(f"  {s}")
        print("\nIf this is intended -- a deliberate map edit or movement-rule change --")
        print("re-record it and put the baseline diff in the same commit so it is reviewed:")
        print("  ./tools/nav-guard/nav_guard.py bless")
        for label, items in (("Also changed (live)", live_notes),
                             ("All-husks world state", dead_shrinks + dead_notes)):
            if items:
                print(f"\n{label}:")
                for n in items:
                    print(f"  {n}")
        return 2

    advisory = live_notes + [f"[all-husks] {s}" for s in dead_shrinks + dead_notes]
    if scripted:
        advisory += [f"[scripted] map Lua names a static blocker nav-guard does not "
                     f"place: {s}" for s in scripted]

    if advisory:
        print("nav-guard: no authored region shrank, but something changed.\n")
        for n in advisory:
            print(f"  {n}")
        print("\nRe-record with ./tools/nav-guard/nav_guard.py bless once reviewed.")
        return 1

    total = sum(len(r.per_locomotor) for r in live)
    print(f"nav-guard OK: {len(live)} maps, {total} map/locomotor pairs match baseline "
          f"in both the authored and all-husks world states.")
    return 0


def cmd_compare(args) -> int:
    """Diff two squeeze variants. This is the acceptance test for the tool itself."""
    _, _, before = analyse(args.before, args.map, args.locomotor, args.state)
    _, _, after = analyse(args.after, args.map, args.locomotor, args.state)
    by_name = {r.name: r for r in before}

    print(f"nav-guard compare: {args.before} -> {args.after}  (world state: {args.state})\n")
    any_diff = False
    for res in after:
        prev = by_name.get(res.name)
        if prev is None:
            continue
        rows = []
        for loco, m in sorted(res.per_locomotor.items()):
            was = prev.per_locomotor.get(loco)
            if was is None or was == m:
                continue
            rows.append((loco, was, m))
        if not rows:
            continue
        any_diff = True
        print(res.name)
        for loco, was, m in rows:
            delta = m["largest"] - was["largest"]
            print(f"    {loco:<32} largest {was['largest']:>6} -> {m['largest']:>6} "
                  f"({delta:+d})   pocketed {was['pocketed']:>5} -> {m['pocketed']:>5}   "
                  f"comps {was['components']} -> {m['components']}")
        print()

    if not any_diff:
        print("  no locomotor on any map changes between these two rule variants.")
    return 0


def cmd_pockets(args) -> int:
    """List the cells of every non-largest component, so a finding can be eyeballed."""
    rules = modload.load_mod(MOD_DIR)
    paths = modload.discover_maps(MOD_DIR)
    if args.map:
        paths = [p for p in paths if any(f in p.name for f in args.map)]

    for game_map in (modload.load_map(p) for p in paths):
        tileset = rules.tilesets[game_map.tileset]
        locos = modload.world_locomotors(rules, game_map.rule_overrides)
        if args.locomotor:
            locos = [loco for loco in locos if loco.name in args.locomotor]
        occupancy, _ = cell_occupancy(rules, game_map, args.state)
        for loco in locos:
            model = build_cell_model(rules, game_map, tileset, loco, occupancy)
            labels, sizes = component_labels(model, args.squeeze)
            if len(sizes) <= 1:
                continue
            biggest = max(range(len(sizes)), key=lambda i: sizes[i])
            print(f"{game_map.name} / {loco.name}: {len(sizes)} components, "
                  f"largest {sizes[biggest]}")
            order = sorted((i for i in range(len(sizes)) if i != biggest),
                           key=lambda i: -sizes[i])
            for label in order[:args.limit]:
                cells = [(i % model.width + model.left, i // model.width + model.top)
                         for i, l in enumerate(labels) if l == label]
                xs = [c[0] for c in cells]
                ys = [c[1] for c in cells]
                print(f"    size {sizes[label]:>6}  bbox x {min(xs)}..{max(xs)} "
                      f"y {min(ys)}..{max(ys)}  e.g. {cells[0]}")
            if len(order) > args.limit:
                print(f"    ... and {len(order) - args.limit} smaller")
    return 0


def cmd_validate(args) -> int:
    import validate
    return validate.run(MOD_DIR, args)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command")

    def add_filters(p, with_state=True):
        p.add_argument("--map", action="append", default=[],
                       help="substring filter on map folder name; repeatable")
        p.add_argument("--locomotor", action="append", default=[],
                       help="exact locomotor name; repeatable")
        if with_state:
            p.add_argument("--state", choices=("live", "dead"), default="live",
                           help="'dead' replaces every map actor with its death husk")

    p = sub.add_parser("report", help="per-map/per-locomotor component table")
    p.add_argument("--squeeze", choices=SQUEEZE_VARIANTS, default=DEFAULT_SQUEEZE)
    add_filters(p)
    p.set_defaults(func=cmd_report)

    p = sub.add_parser("check", help="compare against baseline.json")
    p.set_defaults(func=cmd_check)

    p = sub.add_parser("bless", help="record current numbers as the baseline")
    p.add_argument("--squeeze", choices=SQUEEZE_VARIANTS, default=DEFAULT_SQUEEZE)
    p.set_defaults(func=cmd_bless)

    p = sub.add_parser("compare", help="diff two diagonal-squeeze rule variants")
    p.add_argument("--before", choices=SQUEEZE_VARIANTS, default="none")
    p.add_argument("--after", choices=SQUEEZE_VARIANTS, default="generic")
    add_filters(p)
    p.set_defaults(func=cmd_compare)

    p = sub.add_parser("pockets", help="list non-largest components")
    p.add_argument("--squeeze", choices=SQUEEZE_VARIANTS, default=DEFAULT_SQUEEZE)
    p.add_argument("--limit", type=int, default=10)
    add_filters(p)
    p.set_defaults(func=cmd_pockets)

    p = sub.add_parser("validate", help="decoder self-check against map.png previews")
    p.add_argument("--write-renders", metavar="DIR",
                   help="also write the rendered terrain and a mismatch mask to DIR")
    add_filters(p, with_state=False)
    p.set_defaults(func=cmd_validate)

    args = parser.parse_args(argv)
    if args.command is None:
        args = parser.parse_args(["check"])
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
