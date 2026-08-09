"""Decoder self-check: render our decoded terrain and compare to the checked-in map.png.

nav-guard's connectivity numbers are worth exactly as much as its terrain decode, and a
wrong decode does not look wrong -- a transposed tile plane or an off-by-one still yields
plausible component counts. Each map.png beside a map was produced by the engine itself
(Map.SavePreview, Map.cs:1222) from the same bytes, so it is an independent rendering to
check against. Where we disagree we have to be able to name the reason.

Three numbers per map:

  terrain    agreement restricted to cells where we predict no overlay at all. This is
             the decoder in isolation and is the number that must be 100%.
  overall    agreement over every pixel, with the actor and legacy-resource overlays
             the engine draws on top of terrain.
  align      where the preview sits in map coordinates. Several ww3mod previews predate
             a later hand-edit of `Bounds:` in map.yaml, so they are smaller than the
             current playable area and offset by a cell.

Requires Pillow, and only here -- `check` stays standard-library-only so it can gate a
build anywhere.
"""

from __future__ import annotations

from pathlib import Path

import modload
from miniyaml import Node, base_key
from modload import GameMap, ModRules, Tileset

# RA-era ore. ww3mod has no resource layer in its rules any more, but the ore cells are
# still in map.bin and the previews were saved while a ResourceRenderer was still painting
# them. Not resolvable from current yaml, so it is pinned here -- used by validate only,
# never by the connectivity model.
LEGACY_RESOURCE_COLOR = (0x94, 0x80, 0x60)


def _hex(v: str) -> tuple[int, int, int]:
    v = v.strip().lstrip("#")
    if len(v) == 3:
        v = "".join(c * 2 for c in v)
    if len(v) < 6:
        raise ValueError(v)
    return (int(v[0:2], 16), int(v[2:4], 16), int(v[4:6], 16))


def _player_colors(game_map: GameMap) -> dict[str, tuple[int, int, int]]:
    roots = {n.key: n for n in modload.miniyaml.parse(
        (game_map.path / "map.yaml").read_text(encoding="utf-8"))}
    out: dict[str, tuple[int, int, int]] = {}
    players = roots.get("Players")
    if players is None:
        return out
    for p in players.nodes:
        name = p.child_value("Name")
        if name is None:
            continue
        raw = p.child_value("Color")
        try:
            out[name] = _hex(raw) if raw else (255, 255, 255)
        except ValueError:
            out[name] = (255, 255, 255)
    return out


def _signature(rules: ModRules, name: str):
    node = rules.actor(name)
    if node is None:
        return None
    traits: dict[str, Node] = {}
    for n in node.nodes:
        traits.setdefault(base_key(n.key), n)
    aomp = traits.get("AppearsOnMapPreview")
    if aomp is None:
        return None
    shape = modload.actor_shape(name, node)
    explicit = None
    raw = aomp.child_value("Color")
    if raw:
        try:
            explicit = _hex(raw)
        except ValueError:
            explicit = None
    # OccupiedCells for a Building is OccupiedTiles: the blocking cells plus transit-only.
    return (shape.blocking + shape.transit, aomp.child_value("Terrain"), explicit)


def preview_overlay(rules: ModRules, game_map: GameMap, tileset: Tileset):
    """Cells the engine paints with an actor colour, and the palette of such colours.

    Map.cs:1286-1288 groups the signature cells and keeps `g.First()`, so where two
    actors cover one cell the one declared EARLIER in map.yaml wins. Taking the last
    instead silently mis-paints every stacked pair -- on river-zeta that is barbed wire
    under trees, which is exactly the kind of disagreement this check exists to surface.
    """
    player_colors = _player_colors(game_map)
    out: dict[tuple[int, int], tuple[int, int, int]] = {}
    palette: set[tuple[int, int, int]] = {LEGACY_RESOURCE_COLOR}
    cache: dict[str, object] = {}

    for placed in game_map.actors:
        if placed.name not in cache:
            cache[placed.name] = _signature(rules, placed.name)
        sig = cache[placed.name]
        if sig is None:
            continue
        offsets, terrain, explicit = sig
        if terrain is not None:
            color = tileset.type_color.get(terrain)
        elif explicit is not None:
            color = explicit
        else:
            color = player_colors.get(placed.owner)
        if color is None:
            continue
        palette.add(color)
        ox, oy = placed.location
        for dx, dy in offsets:
            out.setdefault((ox + dx, oy + dy), color)

    return out, palette


def _expected(game_map: GameMap, tileset: Tileset, overlay, cx: int, cy: int):
    terrain = tileset.type_color.get(game_map.terrain_type(tileset, cx, cy), (0, 0, 0))
    return overlay.get((cx, cy), terrain), (cx, cy) in overlay


def _find_alignment(game_map: GameMap, tileset: Tileset, overlay, pixels,
                    pw: int, ph: int) -> tuple[int, int]:
    """Where does this preview sit in map coordinates?

    Tries the current Bounds first. When the preview predates a `Bounds:` edit it will not
    fit, so fall back to a coarse sampled search over every position it could occupy.
    """
    left, top, width, height = game_map.bounds
    if (pw, ph) == (width, height):
        return left, top

    step = 4
    xs = range(0, pw, step)
    ys = range(0, ph, step)
    best = None
    for oy in range(0, game_map.height - ph + 1):
        for ox in range(0, game_map.width - pw + 1):
            score = 0
            for y in ys:
                for x in xs:
                    exp, _ = _expected(game_map, tileset, overlay, ox + x, oy + y)
                    if pixels[x, y] == exp:
                        score += 1
            if best is None or score > best[0]:
                best = (score, ox, oy)
    return best[1], best[2]


def run(mod_dir: Path, args) -> int:
    try:
        from PIL import Image
    except ImportError:
        print("validate needs Pillow (pip install pillow). `check` does not.")
        return 2

    rules = modload.load_mod(mod_dir)
    paths = modload.discover_maps(mod_dir)
    if args.map:
        paths = [p for p in paths if any(f in p.name for f in args.map)]

    out_dir = Path(args.write_renders) if args.write_renders else None
    if out_dir:
        out_dir.mkdir(parents=True, exist_ok=True)

    print("nav-guard decoder self-check -- decoded terrain vs the engine's own map.png\n")
    header = (f"{'map':<24}{'preview':>10}{'terrain':>9}{'overall':>9}"
              f"{'stale':>7}{'ore':>5}{'??':>4}  align")
    print(header)
    print("-" * (len(header) + 30))

    rows = []
    worst_terrain = 100.0
    total_unexplained = 0
    stale_maps = []

    for path in paths:
        game_map = modload.load_map(path)
        tileset = rules.tilesets[game_map.tileset]
        png = path / "map.png"
        if not png.exists():
            rows.append((game_map.name, "none", None, None, 0, 0, 0, "no map.png"))
            continue

        with Image.open(png) as im:
            preview = im.convert("RGB")
            pw, ph = preview.size
            pixels = preview.load()

        overlay, palette = preview_overlay(rules, game_map, tileset)
        ox, oy = _find_alignment(game_map, tileset, overlay, pixels, pw, ph)
        left, top, width, height = game_map.bounds
        aligned = (pw, ph) == (width, height) and (ox, oy) == (left, top)
        note = "bounds" if aligned else \
            f"offset {ox},{oy} (predates Bounds {left},{top},{width},{height})"
        if not aligned:
            stale_maps.append(game_map.name)

        strict_total = strict_ok = 0
        total = ok = 0
        stale_actor = ore = unexplained = 0
        unexplained_samples = []
        render = [] if out_dir else None
        mask = [] if out_dir else None

        for y in range(ph):
            for x in range(pw):
                cx, cy = ox + x, oy + y
                terrain = tileset.type_color.get(
                    game_map.terrain_type(tileset, cx, cy), (0, 0, 0))
                predicted_overlay = overlay.get((cx, cy))
                expected = predicted_overlay if predicted_overlay is not None else terrain
                actual = pixels[x, y]
                hit = actual == expected
                total += 1
                ok += hit

                # The strict decoder number: cells where neither side involves an actor
                # colour, so the only thing under test is tile -> terrain type -> colour.
                strict = predicted_overlay is None and actual not in palette
                if strict:
                    strict_total += 1
                    strict_ok += hit

                if not hit:
                    has_ore = game_map.resources[cy * game_map.width + cx] != 0
                    if has_ore and actual == LEGACY_RESOURCE_COLOR:
                        # RA-era ore, painted when the preview was saved, no longer in rules.
                        ore += 1
                    elif predicted_overlay is not None and actual == terrain:
                        # Actor is in map.yaml but was not there when the preview was saved.
                        stale_actor += 1
                    elif predicted_overlay is None and actual in palette:
                        # Preview still carries an actor that map.yaml no longer places.
                        stale_actor += 1
                    elif not aligned and actual in palette:
                        # Both sides paint an actor, but different ones: the stack on this
                        # cell changed since the preview. Only forgiven where the preview is
                        # independently known stale (its bounds predate map.yaml). On an
                        # up-to-date preview this stays unexplained on purpose -- an
                        # actor-colour disagreement there is a bug in the overlay model,
                        # which is exactly how the first-vs-last-wins error was caught.
                        stale_actor += 1
                    else:
                        unexplained += 1
                        if len(unexplained_samples) < 6:
                            unexplained_samples.append((cx, cy, expected, actual))

                if out_dir:
                    render.append(expected)
                    mask.append((32, 32, 32) if hit else (255, 0, 0))

        pct_terrain = 100.0 * strict_ok / strict_total if strict_total else 100.0
        pct_overall = 100.0 * ok / total if total else 100.0
        worst_terrain = min(worst_terrain, pct_terrain)
        total_unexplained += unexplained
        rows.append((game_map.name, f"{pw}x{ph}", pct_terrain, pct_overall,
                     stale_actor, ore, unexplained, note))
        if unexplained_samples:
            print(f"  ! {game_map.name} unexplained: " +
                  ", ".join(f"({x},{y}) want {e} got {a}"
                            for x, y, e, a in unexplained_samples))

        if out_dir:
            img = Image.new("RGB", (pw, ph))
            img.putdata(render)
            img.save(out_dir / f"{game_map.name}.render.png")
            msk = Image.new("RGB", (pw, ph))
            msk.putdata(mask)
            msk.save(out_dir / f"{game_map.name}.mismatch.png")

    for name, size, pt, po, sa, ore, un, note in rows:
        if pt is None:
            print(f"{name:<24}{size:>10}{'-':>9}{'-':>9}{'-':>7}{'-':>5}{'-':>4}  {note}")
        else:
            print(f"{name:<24}{size:>10}{pt:>8.2f}%{po:>8.2f}%{sa:>7}{ore:>5}{un:>4}  {note}")

    print("\n  terrain  agreement on cells with no actor colour on either side -- the decoder"
          "\n           in isolation, and the number that must read 100%."
          "\n  overall  agreement on every pixel."
          "\n  stale    mismatches where one side has an actor the other does not: map.yaml"
          "\n           edited without regenerating map.png."
          "\n  ore      RA-era resource cells still in map.bin, painted when the preview was"
          "\n           saved; ww3mod has no resource layer today."
          "\n  ??       unexplained. Must be zero.\n")

    if stale_maps:
        print("Previews whose bounds predate a hand-edit of map.yaml: "
              + ", ".join(stale_maps) + "\n")

    if worst_terrain >= 99.999 and total_unexplained == 0:
        print(f"PASS: terrain decode matches every engine preview on every comparable cell "
              f"({worst_terrain:.2f}% worst), 0 unexplained pixels.")
        return 0
    print(f"FAIL: worst terrain agreement {worst_terrain:.2f}%, {total_unexplained} "
          f"unexplained pixels. Treat as a decoder bug until explained; "
          f"--write-renders DIR shows where.")
    return 1
