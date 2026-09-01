"""Load the ww3mod ruleset, tilesets and maps far enough to reason about movement.

Everything here is a reimplementation of engine behaviour, so each piece names the file
it mirrors. The self-check in `nav_guard.py validate` exists because of that: a decoder
nobody checks is a number nobody should trust.
"""

from __future__ import annotations

import struct
from collections.abc import Sequence
from dataclasses import dataclass, field
from pathlib import Path

import miniyaml
from miniyaml import Node, base_key, split_list

# Building.cs:20 FootprintCellType. 'x'/'X' block, '=' and '+' are part of the footprint
# but pathable, '_' is empty. '+' additionally marks transit-only cells.
FOOTPRINT_BLOCKING = ("x", "X")
FOOTPRINT_TRANSIT_ONLY = "+"


# ---------------------------------------------------------------------------- mod rules

@dataclass
class ModRules:
    actors: dict[str, Node]          # lowercase actor name -> resolved node
    tilesets: dict[str, "Tileset"]   # tileset id -> Tileset

    def actor(self, name: str) -> Node | None:
        return self.actors.get(name.lower())


def _mod_file(mod_dir: Path, ref: str) -> Path:
    # `ww3mod|rules/world.yaml` -> <mod_dir>/rules/world.yaml
    return mod_dir / ref.split("|", 1)[-1]


def load_mod(mod_dir: Path) -> ModRules:
    """Parse mod.yaml, then every file it lists under Rules: and Terrain:."""
    manifest = miniyaml.parse((mod_dir / "mod.yaml").read_text(encoding="utf-8"))
    manifest_by_key = {n.key: n for n in manifest}

    rule_files = [n.key for n in manifest_by_key["Rules"].nodes]
    sources = [miniyaml.parse(_mod_file(mod_dir, r).read_text(encoding="utf-8"))
               for r in rule_files]
    resolved = miniyaml.resolve(miniyaml.merge_files(sources))

    tilesets: dict[str, Tileset] = {}
    for n in manifest_by_key["TileSets"].nodes:
        ts = load_tileset(_mod_file(mod_dir, n.key))
        tilesets[ts.id.upper()] = ts

    return ModRules({k.lower(): v for k, v in resolved.items()}, tilesets)


# ------------------------------------------------------------------------------ tileset

@dataclass
class Tileset:
    id: str
    # terrain type name -> preview colour, from the tileset `Terrain:` block.
    type_color: dict[str, tuple[int, int, int]]
    # (template id, tile index) -> terrain type name.
    tile_type: dict[tuple[int, int], str]
    # template id -> whether the template declares PickAny (index is then a variant).
    pick_any: dict[int, bool]
    default_type: str = "Clear"


def _hex_color(v: str) -> tuple[int, int, int]:
    v = v.strip().lstrip("#")
    if len(v) == 3:
        v = "".join(c * 2 for c in v)
    return (int(v[0:2], 16), int(v[2:4], 16), int(v[4:6], 16))


def load_tileset(path: Path) -> Tileset:
    """Mirrors DefaultTerrain's Terrain:/Templates: blocks.

    Only MinColor==MaxColor is modelled. The ww3mod tilesets set neither, so every tile
    falls back to its terrain type's Color (DefaultTerrain.cs:55-61) and the preview is
    deterministic. A tileset that added per-tile MinColor/MaxColor would silently drift
    from this -- validate() is what would catch it.
    """
    roots = {n.key: n for n in miniyaml.parse(path.read_text(encoding="utf-8"))}
    general = roots["General"]
    ts_id = general.child_value("Id", path.stem).upper()

    type_color: dict[str, tuple[int, int, int]] = {}
    for tt in roots["Terrain"].nodes:
        name = tt.child_value("Type")
        color = tt.child_value("Color")
        if name and color:
            type_color[name] = _hex_color(color)

    tile_type: dict[tuple[int, int], str] = {}
    pick_any: dict[int, bool] = {}
    for tmpl in roots["Templates"].nodes:
        tid_raw = tmpl.child_value("Id")
        if tid_raw is None:
            continue
        tid = int(tid_raw)
        pick_any[tid] = (tmpl.child_value("PickAny", "False") or "").lower() == "true"
        tiles = tmpl.child("Tiles")
        if tiles is None:
            continue
        for t in tiles.nodes:
            # `0: Clear` -- key is the index within the template, value the terrain type.
            if t.value:
                tile_type[(tid, int(t.key))] = t.value

    return Tileset(ts_id, type_color, tile_type, pick_any)


# ---------------------------------------------------------------------------------- map

@dataclass
class MapActor:
    name: str
    owner: str
    location: tuple[int, int]


@dataclass
class GameMap:
    name: str
    path: Path
    width: int
    height: int
    bounds: tuple[int, int, int, int]        # left, top, width, height
    tileset: str
    tiles: list[tuple[int, int]]             # row-major [y * width + x] -> (template, index)
    heights: list[int]
    resources: list[int]                     # row-major resource type id, 0 = none
    actors: list[MapActor]
    rule_overrides: list[Node] = field(default_factory=list)

    def terrain_type(self, ts: Tileset, x: int, y: int) -> str:
        template, index = self.tiles[y * self.width + x]
        t = ts.tile_type.get((template, index))
        if t is not None:
            return t
        # Map.cs:422 -- index 255 means "pick a variant", resolved from the cell coords.
        # A template we know but an index we do not is a tileset/map mismatch; fall back
        # to index 0 rather than inventing impassability.
        t = ts.tile_type.get((template, 0))
        return t if t is not None else ts.default_type


def _parse_int_pair(v: str) -> tuple[int, int]:
    a, b = v.split(",")
    return int(a), int(b)


def load_map(map_dir: Path) -> GameMap:
    """Read map.yaml + map.bin. Mirrors Map.cs:400-451."""
    roots = miniyaml.parse((map_dir / "map.yaml").read_text(encoding="utf-8"))
    by_key = {n.key: n for n in roots}

    width, height = _parse_int_pair(by_key["MapSize"].value)
    bl, bt, bw, bh = (int(p) for p in by_key["Bounds"].value.split(","))
    tileset = by_key["Tileset"].value.upper()

    actors: list[MapActor] = []
    actors_node = by_key.get("Actors")
    if actors_node is not None:
        for a in actors_node.nodes:
            loc = a.child_value("Location")
            if a.value and loc:
                actors.append(MapActor(a.value.lower(),
                                       a.child_value("Owner", "Neutral"),
                                       _parse_int_pair(loc)))

    rule_overrides: list[Node] = []
    rules_node = by_key.get("Rules")
    if rules_node is not None:
        for r in rules_node.nodes:
            # Either an inline override block or a `filename.yaml` include.
            if r.nodes:
                rule_overrides.append(r)
            elif r.key.endswith(".yaml"):
                inc = map_dir / r.key
                if inc.exists():
                    rule_overrides.extend(miniyaml.parse(inc.read_text(encoding="utf-8")))

    tiles, heights, resources = _read_map_bin(map_dir / "map.bin", width, height)
    return GameMap(map_dir.name, map_dir, width, height, (bl, bt, bw, bh),
                   tileset, tiles, heights, resources, actors, rule_overrides)


def _read_map_bin(path: Path, width: int, height: int):
    """BinaryDataHeader (Map.cs:29-58) then the tile and height planes.

    Both planes are stored column-major (`for i in X: for j in Y`), which is the single
    easiest thing to get backwards -- and a transposed decode still produces a plausible
    connectivity number, so only the preview comparison catches it.
    """
    data = path.read_bytes()
    fmt = data[0]
    w, h = struct.unpack_from("<HH", data, 1)
    if (w, h) != (width, height):
        raise ValueError(f"{path}: map.bin is {w}x{h}, map.yaml says {width}x{height}")

    if fmt == 1:
        tiles_offset, heights_offset = 5, 0
        resources_offset = 3 * width * height + 5
    elif fmt == 2:
        tiles_offset, heights_offset, resources_offset = struct.unpack_from("<III", data, 5)
    else:
        raise ValueError(f"{path}: unknown map.bin format {fmt}")

    tiles = [(0, 0)] * (width * height)
    pos = tiles_offset
    for i in range(width):
        for j in range(height):
            template, index = struct.unpack_from("<HB", data, pos)
            pos += 3
            if index == 0xFF:
                index = i % 4 + (j % 4) * 4
            tiles[j * width + i] = (template, index)

    heights = [0] * (width * height)
    if heights_offset > 0:
        pos = heights_offset
        for i in range(width):
            for j in range(height):
                heights[j * width + i] = data[pos]
                pos += 1

    # Resources play no part in movement -- ww3mod has no ResourceLayer at all. They are
    # read solely so `validate` can account for the RA-era ore still sitting in map.bin,
    # which the previews were generated with and which would otherwise read as decoder error.
    resources = [0] * (width * height)
    if resources_offset > 0:
        pos = resources_offset
        for i in range(width):
            for j in range(height):
                resources[j * width + i] = data[pos]
                pos += 2

    return tiles, heights, resources


def discover_maps(mod_dir: Path, extra_roots: Sequence[Path] = ()) -> list[Path]:
    """Map packages under `mod_dir/maps`, plus every package under each extra root.

    `extra_roots` is how autotest scenarios get in. They are NOT included by default and
    must never be: `baseline.json` is keyed by package name, so anything discovered here
    that is not in the baseline reads as "new map" and the gate's numbers move. Callers
    that compare against the baseline (`check`, `bless`) pass nothing; the inspection
    commands take `--scenarios`.
    """
    roots = [mod_dir / "maps", *extra_roots]
    return sorted((p for root in roots if root.is_dir() for p in root.iterdir()
                   if (p / "map.bin").exists() and (p / "map.yaml").exists()),
                  key=lambda p: (p.parent.name, p.name))


# ------------------------------------------------------------------- resolved actor info

@dataclass
class ActorShape:
    """What a placed actor does to the cells it sits on."""
    name: str
    blocking: list[tuple[int, int]]        # footprint offsets that can block ('x'/'X')
    transit: list[tuple[int, int]]         # '+' offsets: occupied but never block movement
    pass_classes: frozenset[str]           # from Passable.PassClasses, empty if not passable
    passed_by_anyone: bool                 # PassedBy/CrushedByRelationships covers all players
    blocks_squeeze: bool                   # carries BlocksDiagonalSqueeze
    mobile: bool                           # has Mobile -- can move aside, never a wall


def _traits(actor: Node) -> dict[str, list[Node]]:
    out: dict[str, list[Node]] = {}
    for n in actor.nodes:
        out.setdefault(base_key(n.key), []).append(n)
    return out


def husk_of(rules: ModRules, name: str) -> str | None:
    """The actor a SpawnActorOnDeath leaves behind, if it resolves to a known actor.

    Tree husks are not cosmetic: every ^Tree drops `Passable: PassClasses: tree` when it
    dies, so a cell infantry could walk through becomes solid, and several husks occupy a
    different or larger footprint than the tree did (t14/t15 1 -> 2 cells, tc02 1 -> 3).
    """
    node = rules.actor(name)
    if node is None:
        return None
    for n in node.nodes:
        if base_key(n.key) == "SpawnActorOnDeath":
            target = n.child_value("Actor")
            if target and rules.actor(target) is not None:
                return target
    return None


def actor_shape(name: str, actor: Node) -> ActorShape:
    traits = _traits(actor)

    blocking: list[tuple[int, int]] = []
    transit: list[tuple[int, int]] = []
    building = traits.get("Building")
    if building:
        b = building[0]
        dim = b.child_value("Dimensions", "1,1")
        dx, dy = _parse_int_pair(dim)
        fp = b.child_value("Footprint")
        chars = [c for c in fp if not c.isspace()] if fp else ["x"] * (dx * dy)
        for idx, c in enumerate(chars[:dx * dy]):
            off = (idx % dx, idx // dx)
            if c in FOOTPRINT_BLOCKING:
                blocking.append(off)
            elif c == FOOTPRINT_TRANSIT_ONLY:
                transit.append(off)
    else:
        # No Building trait: Mobile and the bare IOccupySpace traits are single-cell.
        # PITFALL: this is NOT true of `Immobile: OccupiesSpace: false`, which occupies
        # nothing at all -- ImmobileInfo.OccupiedCells returns an empty dictionary
        # (Immobile.cs:23-27). Five marker types carry it (mpspawn, spawnarea, waypoint,
        # camera.paradrop.detector, camera.spyplane) and every one is modelled here as a
        # solid 1-cell wall, so a unit sharing a cell with an mpspawn reads as standing on
        # impassable ground. Known and deliberately NOT fixed here: correcting it moves 150
        # of the 190 baselined map/locomotor pairs and needs its own reviewed `bless`.
        # The error is conservative -- it invents walls, never removes them -- so `check`
        # cannot have passed a real sealing-off because of it. Measured 2026-09-01;
        # blast radius in WORKSPACE/DISCOVERIES.md.
        blocking.append((0, 0))

    pass_classes: frozenset[str] = frozenset()
    passed_by_anyone = False
    passable = traits.get("Passable")
    if passable:
        p = passable[0]
        pass_classes = frozenset(split_list(p.child_value("PassClasses")))
        rels = set(split_list(p.child_value("PassedByRelationships"))) \
            | set(split_list(p.child_value("CrushedByRelationships")))
        # Passable.cs:109-124 resolves relationships to a player mask. Every ww3mod
        # Passable lists Ally+Enemy (usually +Neutral), which maps to AllPlayersMask.
        # Anything narrower is owner-dependent; nav-guard treats it as blocking, which
        # is the conservative direction for a connectivity floor.
        passed_by_anyone = "Ally" in rels and "Enemy" in rels

    return ActorShape(
        name=name,
        blocking=blocking,
        transit=transit,
        pass_classes=pass_classes,
        passed_by_anyone=passed_by_anyone,
        blocks_squeeze="BlocksDiagonalSqueeze" in traits,
        mobile="Mobile" in traits,
    )


# ---------------------------------------------------------------------------- locomotors

@dataclass
class Locomotor:
    name: str
    shares_cell: bool
    passes: frozenset[str]
    terrain_speeds: dict[str, int]

    def terrain_passable(self, terrain_type: str) -> bool:
        # LocomotorInfo.TerrainSpeeds: "leave out entries for impassable terrain".
        return terrain_type in self.terrain_speeds


def world_locomotors(rules: ModRules, overrides: list[Node]) -> list[Locomotor]:
    """Locomotor traits on the World actor, after any map-level rule overrides."""
    world = rules.actor("World")
    if world is None:
        return []

    nodes = list(world.nodes)
    for ov in overrides:
        if ov.key.lower() == "world":
            nodes = miniyaml._merge_node_lists(nodes, ov.nodes)  # noqa: SLF001
            nodes = [n for n in nodes if not n.key.startswith("-")]

    out: list[Locomotor] = []
    for n in nodes:
        if base_key(n.key) != "Locomotor":
            continue
        speeds: dict[str, int] = {}
        ts = n.child("TerrainSpeeds")
        if ts:
            for t in ts.nodes:
                try:
                    speeds[t.key] = int(t.value)
                except (TypeError, ValueError):
                    continue
        out.append(Locomotor(
            name=n.child_value("Name", "default"),
            shares_cell=(n.child_value("SharesCell", "False") or "").lower() == "true",
            passes=frozenset(split_list(n.child_value("Passes"))),
            terrain_speeds=speeds,
        ))
    return sorted(out, key=lambda loco: loco.name)
