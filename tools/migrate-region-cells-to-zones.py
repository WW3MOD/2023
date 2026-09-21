#!/usr/bin/env python3
"""Move a map's hand-authored DEFCON border from rules.yaml into map.yaml's `Zones: DMZ`.

A ONE-OFF, AND IT IS KEPT RATHER THAN DELETED because it is the only written record of exactly
what the nine shipped borders were before the move. Re-running it on an already-migrated map is a
no-op: it looks for `RegionCells:` and does nothing when there is none.

WHY IT IS TEXTUAL AND DOES NOT GO THROUGH Map.Save. Round-tripping a map package through the
engine rewrites every field from the in-memory Map and -- because map.yaml is part of the UID hash
-- changes the map's identity. A map whose UID moved no longer matches anyone else's copy of it in
a lobby. So this edits the two text files and touches nothing else: map.bin, map.png and every
other field come out byte-identical.

WHERE THE BLOCK GOES. Map.Save writes map.yaml in `Map.YamlFields` order and `Zones` is declared
between `Actors` and `Rules`. Putting it anywhere else would load perfectly well today and produce
a large spurious diff the first time anyone saved the map in the editor, so it goes where the
engine will put it: immediately before the `Rules:` node.

WHAT IS LEFT BEHIND IN rules.yaml. The `RegionCells:` line goes. `RegionTerrainTypes:` STAYS --
river-zeta-ww3 needs it, and it is a separate source that DefconWall unions with the zone rather
than one replacing the other. Every comment stays, because those comments are the only record of
why each border is drawn where it is; the one line that would become false -- the
`# N cells, the whole border` header directly above the removed cells -- is rewritten to say where
they went.

Usage:
    python tools/migrate-region-cells-to-zones.py              # every shipped map, in place
    python tools/migrate-region-cells-to-zones.py --dry-run    # report only, write nothing
    python tools/migrate-region-cells-to-zones.py --map twin-rivers-ww3
"""

import argparse
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent
MAPS = REPO / "mods" / "ww3mod" / "maps"

# Mirrors MapZones.Dmz. The one zone id the engine gives meaning to.
ZONE_ID = "DMZ"

SHARED_HEADER = re.compile(r"^\t\t# (\d+) cells, the whole border\.")
REGION_CELLS = re.compile(r"^\t\tRegionCells:[ \t]*(.*)$")


def parse_region_cells(text):
    """A flat comma-separated X,Y list, exactly as FieldLoader.ParseCPosArray reads it."""
    parts = [p.strip() for p in text.replace("\n", ",").split(",") if p.strip()]
    if len(parts) % 2 != 0:
        raise ValueError(f"expected an even number of coordinates, got {len(parts)}")
    return [(int(parts[2 * i]), int(parts[2 * i + 1])) for i in range(len(parts) // 2)]


def encode_rows(cells):
    """The row-range form, byte-identical to MapZones.EncodeRows.

    Rows ascend, ranges within a row ascend, runs are coalesced, and a one-cell run is written
    bare. Nothing keeps this in step with the C# automatically, so the C# side is pinned by
    MapZoneCodecTest and this side is checked against its own inverse before anything is written.
    """
    by_row = {}
    for x, y in cells:
        by_row.setdefault(y, set()).add(x)

    lines = []
    for y in sorted(by_row):
        xs = sorted(by_row[y])
        runs = []
        lo = hi = xs[0]
        for x in xs[1:]:
            if x == hi + 1:
                hi = x
                continue
            runs.append((lo, hi))
            lo = hi = x
        runs.append((lo, hi))
        lines.append((y, ", ".join(str(a) if a == b else f"{a}-{b}" for a, b in runs)))

    return lines


def decode_rows(rows):
    """The inverse, used only to verify the encode round-trips before anything is written."""
    cells = set()
    for y, value in rows:
        for token in value.split(","):
            token = token.strip()
            if not token:
                continue
            split = token.find("-", 1)
            lo, hi = (int(token), int(token)) if split < 0 else (int(token[:split]), int(token[split + 1:]))
            for x in range(lo, hi + 1):
                cells.add((x, y))
    return cells


def rewrite_rules(rules, count):
    """Drop the RegionCells line and leave a pointer in its place. Returns the new text."""
    lines = rules.split("\n")
    idx = next(i for i, line in enumerate(lines) if REGION_CELLS.match(line))

    pointer = [
        f"\t\t# THE {count} HAND-AUTHORED CELLS NOW LIVE IN map.yaml, as `Zones: {ZONE_ID}` -- painted in",
        "\t\t# the editor's Zones tool rather than pasted in here, because the editor cannot write",
        "\t\t# rules.yaml. DefconWall reads that zone and UNIONS it with any RegionTerrainTypes above",
        "\t\t# (DefconWall.BuildRegion); everything this file says about which cells they are and why",
        "\t\t# still describes them exactly.",
    ]

    # Eight of the nine maps carry the same generated header line immediately above the cells. It
    # states a cell count for a field that is about to not exist, so it is replaced rather than
    # kept. river-zeta-ww3 wrote its own and keeps it; the pointer goes in on its own there.
    if idx > 0 and SHARED_HEADER.match(lines[idx - 1]):
        lines[idx - 1:idx + 1] = pointer
    else:
        lines[idx:idx + 1] = pointer

    return "\n".join(lines)


def insert_zones(map_yaml, rows, name):
    """Put the Zones block where Map.YamlFields orders it: after Actors, before Rules."""
    if re.search(r"^Zones:", map_yaml, re.MULTILINE):
        raise SystemExit(f"{name}: map.yaml already has a Zones node; nothing to migrate")

    block = f"Zones:\n\t{ZONE_ID}:\n" + "".join(f"\t\t{y}: {value}\n" for y, value in rows)

    rules_node = re.search(r"^Rules:", map_yaml, re.MULTILINE)
    if rules_node is None:
        return map_yaml.rstrip("\n") + "\n\n" + block

    return map_yaml[:rules_node.start()] + block + "\n" + map_yaml[rules_node.start():]


def migrate(map_dir, dry_run):
    rules_path = map_dir / "rules.yaml"
    map_path = map_dir / "map.yaml"
    if not rules_path.exists() or not map_path.exists():
        return None

    rules = rules_path.read_text(encoding="utf-8")
    match = REGION_CELLS.search(rules) if REGION_CELLS.match(rules) else None
    for line in rules.split("\n"):
        match = REGION_CELLS.match(line)
        if match:
            break

    if not match:
        return None

    old_len = len(match.group(1))
    cells = parse_region_cells(match.group(1))
    rows = encode_rows(cells)

    # NOTHING IS WRITTEN UNTIL THE ROUND TRIP AGREES. A cell lost here is a border that stops
    # separating the map, and the engine reports that only as one debug-log line.
    if decode_rows(rows) != set(cells):
        raise SystemExit(f"{map_dir.name}: encode/decode disagreed -- refusing to write")

    new_rules = rewrite_rules(rules, len(cells))
    new_map = insert_zones(map_path.read_text(encoding="utf-8"), rows, map_dir.name)

    if not dry_run:
        rules_path.write_text(new_rules, encoding="utf-8", newline="")
        map_path.write_text(new_map, encoding="utf-8", newline="")

    new_len = sum(len(f"{y}: {v}") for y, v in rows)
    return len(cells), len(rows), old_len, new_len


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--map", help="one map directory name; default is every shipped map")
    ap.add_argument("--dry-run", action="store_true", help="report what would change and write nothing")
    args = ap.parse_args()

    targets = [MAPS / args.map] if args.map else sorted(p for p in MAPS.iterdir() if p.is_dir())

    migrated = 0
    for map_dir in targets:
        result = migrate(map_dir, args.dry_run)
        if result is None:
            print(f"{map_dir.name:24s}  no RegionCells -- skipped")
            continue

        cells, rows, old_len, new_len = result
        print(f"{map_dir.name:24s}  {cells:5d} cells -> {rows:4d} rows   "
              f"{old_len:5d} -> {new_len:5d} chars  ({'dry-run' if args.dry_run else 'written'})")
        migrated += 1

    print(f"\n{migrated} map(s) {'would be ' if args.dry_run else ''}migrated.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
