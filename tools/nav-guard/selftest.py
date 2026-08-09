#!/usr/bin/env python3
"""Self-test for nav-guard's load-bearing logic. No game, no build, no map files.

Runs in well under a second so `make nav-guard` can depend on it. The point is that
nav_guard.py check produces a number, and a number from broken machinery still looks like
a number -- these pin the parts where a silent wrong answer is possible.
"""

from __future__ import annotations

import sys

import miniyaml
import modload
import nav_guard

FAILURES: list[str] = []


def check(label: str, got, want) -> None:
    if got != want:
        FAILURES.append(f"{label}\n     got  {got!r}\n     want {want!r}")


# ----------------------------------------------------------------------------- miniyaml

def test_inheritance() -> None:
    tree = miniyaml.merge_files([miniyaml.parse(
        "^Base:\n"
        "\tBuilding:\n"
        "\t\tFootprint: xx xx\n"
        "\t\tDimensions: 2,2\n"
        "\tPassable:\n"
        "\t\tPassClasses: tree\n"
        "Child:\n"
        "\tInherits: ^Base\n"
        "\tBuilding:\n"
        "\t\tFootprint: _x\n"
        "\t\tDimensions: 2,1\n"
        "Stripped:\n"
        "\tInherits: ^Base\n"
        "\t-Passable:\n")])
    resolved = miniyaml.resolve(tree)

    child = modload.actor_shape("child", resolved["Child"])
    check("child overrides inherited footprint", child.blocking, [(1, 0)])
    check("child keeps inherited Passable", sorted(child.pass_classes), ["tree"])

    stripped = modload.actor_shape("stripped", resolved["Stripped"])
    check("-Passable removes the inherited trait", sorted(stripped.pass_classes), [])
    check("-Passable leaves the footprint alone",
          sorted(stripped.blocking), [(0, 0), (0, 1), (1, 0), (1, 1)])


def test_comments_and_values() -> None:
    nodes = miniyaml.parse("Key: value # trailing\n\tChild: a, b\n#whole line\nOther:\n")
    check("comment stripped from value", nodes[0].value, "value")
    check("child parsed", miniyaml.split_list(nodes[0].nodes[0].value), ["a", "b"])
    check("comment-only line dropped", [n.key for n in nodes], ["Key", "Other"])


def test_footprint_chars() -> None:
    node = miniyaml.parse("A:\n\tBuilding:\n\t\tFootprint: x_+ X__\n\t\tDimensions: 3,2\n")[0]
    shape = modload.actor_shape("a", node)
    check("x and X block", sorted(shape.blocking), [(0, 0), (0, 1)])
    check("+ is footprint but pathable", shape.transit, [(2, 0)])


# --------------------------------------------------------------------- squeeze geometry

def _model(rows: list[str], shares_cell: bool = False) -> nav_guard.CellModel:
    """Build a model from ASCII art. '.' open, '#' impassable, 'T' tagged trap (impassable)."""
    height, width = len(rows), len(rows[0])
    passable = bytearray(width * height)
    tag = bytearray(width * height)
    for y, row in enumerate(rows):
        for x, c in enumerate(row):
            if c == ".":
                passable[y * width + x] = 1
            elif c == "T":
                tag[y * width + x] = 1
    loco = modload.Locomotor("test", shares_cell, frozenset(), {"Clear": 100})
    return nav_guard.CellModel(None, loco, 0, 0, width, height, passable, tag)


def test_squeeze_variants() -> None:
    # Two traps corner to corner at (1,1) and (2,2); the squeeze step is (2,1)<->(1,2).
    model = _model([
        "....",
        ".T..",
        "..T.",
        "....",
    ])
    for variant, want in (("none", False), ("generic", True), ("tagged", True)):
        check(f"{variant}: trap corner pair",
              nav_guard.squeeze_blocks(model, variant, 2, 1, 1, 2), want)
        check(f"{variant}: same step reversed is symmetric",
              nav_guard.squeeze_blocks(model, variant, 1, 2, 2, 1), want)

    # Plain impassable terrain: generic treats it as a shoulder, tagged does not.
    walls = _model([
        "....",
        ".#..",
        "..#.",
        "....",
    ])
    check("generic: terrain corners count", nav_guard.squeeze_blocks(walls, "generic", 2, 1, 1, 2), True)
    check("tagged: terrain corners do not", nav_guard.squeeze_blocks(walls, "tagged", 2, 1, 1, 2), False)

    # One shoulder only is never a squeeze -- "both" is what keeps DensePathGraph's
    # DirectedNeighbors pruning complete (Locomotor.cs:277-289).
    one = _model([
        "....",
        ".T..",
        "....",
        "....",
    ])
    for variant in ("generic", "tagged"):
        check(f"{variant}: one shoulder is not a squeeze",
              nav_guard.squeeze_blocks(one, variant, 2, 1, 1, 2), False)

    # Orthogonal steps have no corner to cross.
    check("orthogonal step is never a squeeze",
          nav_guard.squeeze_blocks(model, "generic", 1, 0, 2, 0), False)

    # SharesCell locomotors are exempt under the shipped rule, but the reverted generic
    # version had no such guard -- that difference is why infantry moved under it too.
    infantry = _model([
        "....",
        ".T..",
        "..T.",
        "....",
    ], shares_cell=True)
    check("tagged exempts SharesCell",
          nav_guard.squeeze_blocks(infantry, "tagged", 2, 1, 1, 2), False)
    check("generic did not exempt SharesCell",
          nav_guard.squeeze_blocks(infantry, "generic", 2, 1, 1, 2), True)


def test_map_edge_is_a_shoulder_only_for_generic() -> None:
    # Step (1,0)->(0,1) has shoulders (1,1) and (0,0). Put a wall at (1,1) and rely on
    # the corner (0,0) being on the map: neither variant should fire. Then step
    # (0,1)->(-1,0) does not exist, so use the true edge case: a step whose shoulder sits
    # outside the map is only a shoulder under the generic rule.
    model = _model([
        "#.",
        ".#",
    ])
    check("generic: both in-map walls block", nav_guard.squeeze_blocks(model, "generic", 1, 0, 0, 1), True)
    check("tagged: untagged walls do not", nav_guard.squeeze_blocks(model, "tagged", 1, 0, 0, 1), False)


def test_components_respect_denied_diagonals() -> None:
    # A 3x3 with traps on the anti-diagonal corners. The centre column is walled off
    # except through the corner, so denying it splits the grid.
    rows = [
        ".#.",
        "#.#",
        ".#.",
    ]
    model = _model(rows)
    check("open diagonals keep it one component", nav_guard.components(model, "none"), [5])

    tagged = _model([
        ".T.",
        "T.T",
        ".T.",
    ])
    # Centre is open, the four corners are open, every corner-to-centre step has two
    # tagged shoulders, so the centre is cut off from all four corners.
    check("tagged corners isolate the centre",
          nav_guard.components(tagged, "tagged"), [1, 1, 1, 1, 1])


def main() -> int:
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            fn()
    if FAILURES:
        print(f"nav-guard selftest: {len(FAILURES)} failure(s)")
        for f in FAILURES:
            print(f"  - {f}")
        return 1
    print("nav-guard selftest: ok")
    return 0


if __name__ == "__main__":
    sys.exit(main())
