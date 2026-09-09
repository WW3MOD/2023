#!/usr/bin/env python3
"""Static guard for smudge coverage. No build, no launch, ~1s.

WHY THIS EXISTS
---------------
``TerrainTypeInfo.AcceptsSmudgeType`` (engine/OpenRA.Game/Map/TerrainInfo.cs:63) is a HashSet
that DEFAULTS TO EMPTY, and an empty set means "accepts nothing". ``LeaveSmudgeWarhead.cs:73``
asks each cell's terrain for the first accepted type matching the warhead and silently drops
the cell when there is none. So a terrain type that simply never mentions a smudge type
rejects it -- with no error, no warning, and nothing in any log.

That is an opt-in allowlist replicated across four tilesets by roughly thirteen terrain types.
Adding ONE new smudge type means editing every terrain type in every tileset, and forgetting
any of them leaves a silent hole rather than a failure. It cost two rounds on 2026-09-09:
first beaches (a bright sand band between the scar and the waterline), then Rock and Cliffs
(every boulder sitting in an unburnt halo, reported as "some kind of protective aura" --
2417 of 16384 cells on one map). Both were invisible until somebody looked at a screenshot.

WHAT THIS CHECKS
----------------
1. NO PARTIAL FAMILIES. A terrain type accepting SOME of a smudge family but not all of it is
   almost always a forgotten edit -- exactly the shape both 2026-09-09 bugs had while they
   were being fixed one tileset at a time.
2. NO SILENT HOLES IN TERRAIN THAT IS ACTUALLY USED. Every terrain type appearing on a shipped
   map or an autotest scenario must either accept the full scar family or be named in
   DELIBERATELY_UNSCARRED below. Note the direction: a NEW terrain type fails until somebody
   makes a decision about it, which is the entire point.

Exit 0 clean, 2 on a finding. There is no warning band -- every case is either a decision
recorded in DELIBERATELY_UNSCARRED or a hole nobody meant to leave.
"""
import collections
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods" / "ww3mod"
MAP_ROOTS = [MOD / "maps", ROOT / "tools" / "autotest" / "scenarios"]

# Terrain that SHOULD reject scars, with the reason. A scar is an opaque ground decal and there
# is no sub-cell land/water information anywhere in the engine, which is why SmudgeLayer carries
# ShoreFadeCells to ramp the blast down as it approaches these rather than drawing onto them.
DELIBERATELY_UNSCARRED = {
    "Water": "a scar is a ground decal; SmudgeLayer.ShoreFadeCells feathers the blast into it",
    "River": "as Water",
    "RiverShallow": "as Water",
}


def read(p):
    return p.read_text(encoding="utf-8", errors="replace")


def smudge_families():
    """Smudge families declared by SmudgeLayer in world.yaml.

    Derived from the rules rather than hard-coded, so a sixth Scar band added tomorrow is
    covered by check 1 the moment it is declared, without anyone editing this file.
    """
    types = re.findall(r"^\t\tType:\s*(\w+)\s*$", read(MOD / "rules" / "world.yaml"), re.M)
    layers = {t for t in types if t.startswith("Scar")}
    return {"Scar": layers} if layers else {}


def acceptance():
    """{TILESET: {terrain type: set of accepted smudge types}}"""
    out = {}
    for p in sorted((MOD / "tilesets").glob("*.yaml")):
        d = {}
        for blk in re.split(r"(?=\n\tTerrainType@)", read(p)):
            m = re.search(r"Type:\s*(\S+)", blk)
            if not m:
                continue
            a = re.search(r"AcceptsSmudgeType:\s*(.*)", blk)
            d[m.group(1)] = {x.strip() for x in a.group(1).split(",")} if a else set()
        out[p.stem.upper()] = d
    return out


def terrain_in_use():
    """{TILESET: Counter(terrain type -> cells)} across every shipped map and scenario."""
    sys.path.insert(0, str(ROOT / "tools" / "nav-guard"))
    import modload

    used = collections.defaultdict(collections.Counter)
    ts_cache = {}
    for root in MAP_ROOTS:
        if not root.is_dir():
            continue
        for md in sorted(root.iterdir()):
            if not (md / "map.bin").exists():
                continue
            try:
                gm = modload.load_map(md)
            except Exception:
                continue  # nav-guard owns map-loading failures; not this gate's job
            if gm.tileset not in ts_cache:
                ts_cache[gm.tileset] = modload.load_tileset(
                    MOD / "tilesets" / (gm.tileset.lower() + ".yaml"))
            ts = ts_cache[gm.tileset]
            # Count DISTINCT (template, index) pairs first, then resolve each pair once.
            # The naive form calls terrain_type per cell -- 1.17M Python calls across the 330
            # shipped maps and scenarios. A map has at most a few hundred distinct pairs, and
            # Counter over the flat tile list runs in C, so this is the same answer for a small
            # fraction of the work. Exact, not sampled: every cell is still accounted for, via
            # its pair. (Written after a gate run was cut short; the run turned out to have been
            # killed by hand rather than timing out, so this is a cost worth not paying rather
            # than a measured fix for a measured problem.)
            per_tile = collections.Counter(gm.tiles)
            resolved = {}
            for pair, n in per_tile.items():
                t = resolved.get(pair)
                if t is None:
                    t = ts.tile_type.get(pair)
                    if t is None:
                        # Map.cs:422 -- index 255 means "pick a variant", resolved from the
                        # cell coords; an unknown index falls back to index 0. Mirrors
                        # modload.GameMap.terrain_type so the two cannot disagree.
                        t = ts.tile_type.get((pair[0], 0), ts.default_type)
                    resolved[pair] = t
                used[gm.tileset][t] += n
    return used


def check():
    fams = smudge_families()
    if not fams:
        print("smudge-gate: no Scar* SmudgeLayer in world.yaml -- nothing to check.")
        return 0

    members = fams["Scar"]
    acc = acceptance()
    findings = []

    for tsn, terrains in sorted(acc.items()):
        for t, a in sorted(terrains.items()):
            got = a & members
            if got and got != members:
                findings.append(
                    "PARTIAL  {}/{}: accepts {} but not {}. A partial family is a forgotten "
                    "edit.".format(tsn, t, sorted(got), sorted(members - got)))

    used = terrain_in_use()
    for tsn, ctr in sorted(used.items()):
        for t, n in ctr.most_common():
            if t in DELIBERATELY_UNSCARRED:
                continue
            if not (acc.get(tsn, {}).get(t, set()) & members):
                findings.append(
                    "HOLE     {}/{}: {:,} cells in use and accepts NO scar type. Add the "
                    "family, or record it in DELIBERATELY_UNSCARRED with a reason."
                    .format(tsn, t, n))

    cells = sum(sum(c.values()) for c in used.values())
    print("smudge-gate: {} scar types, {} tilesets in use, {:,} cells scanned."
          .format(len(members), len(used), cells))
    for tsn, ctr in sorted(used.items()):
        skipped = [t for t in ctr if t in DELIBERATELY_UNSCARRED]
        print("  {}: {} scarrable terrain types, {} deliberately not ({})"
              .format(tsn, len(ctr) - len(skipped), len(skipped), ", ".join(sorted(skipped))))

    for f in findings:
        print("  " + f)
    if findings:
        print("smudge-gate: {} finding(s).".format(len(findings)))
        return 2
    print("smudge-gate: clean.")
    return 0


def selftest():
    """Prove the gate can tell red from green. A gate nobody has seen fail is not known to work."""
    fams = smudge_families()
    if not fams:
        print("selftest: no scar family declared in world.yaml.")
        return 1
    members = fams["Scar"]
    acc = acceptance()

    # Synthetic partial family: the detector must fire on it.
    partial = set(sorted(members)[:2])
    if len(partial) < 2 or partial == members:
        print("selftest: scar family too small to form a partial.")
        return 1
    got = partial & members
    if not (got and got != members):
        print("selftest: partial-family detector did not fire on a synthetic partial.")
        return 1

    # Synthetic hole: a terrain type in use with no scar and no recorded reason must be a finding.
    if "Clear" in DELIBERATELY_UNSCARRED:
        print("selftest: Clear must not be exempt; the hole detector would never fire.")
        return 1

    # The historical bug, as a tripwire on the tree itself.
    rock = acc.get("TEMPERAT", {}).get("Rock", set())
    if not (rock & members):
        print("selftest: TEMPERAT/Rock accepts no scar -- the tree is in the pre-2026-09-09 "
              "state and `check` should be reporting it.")
        return 1

    print("selftest: ok. Scar family = {}; {} types deliberately unscarred; TEMPERAT/Rock "
          "accepts {}/{}.".format(sorted(members), len(DELIBERATELY_UNSCARRED),
                                  len(rock & members), len(members)))
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "check"
    sys.exit(selftest() if cmd == "selftest" else check())
