"""MiniYaml reader: parse, merge, resolve Inherits and -removals.

Mirrors engine/OpenRA.Game/MiniYaml.cs (FromLines / Merge / ResolveInherits) for the
subset nav-guard needs. Deliberately not general: the escaped-whitespace value guards
and the map-package `Rules:` include syntax are the only exotica handled.

Divergence worth knowing: MiniYaml.cs throws when a `-Key` removal matches nothing and
when the same parent is inherited twice. Here both are tolerated, because nav-guard is
an analysis tool run against yaml the engine has already accepted -- raising would only
turn an engine-legal tree into a nav-guard crash.
"""

from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class Node:
    key: str
    value: str | None = None
    nodes: list["Node"] = field(default_factory=list)

    def child(self, key: str) -> "Node | None":
        for n in self.nodes:
            if n.key == key:
                return n
        return None

    def child_value(self, key: str, default: str | None = None) -> str | None:
        n = self.child(key)
        return default if n is None or n.value is None else n.value


def base_key(key: str) -> str:
    """`Locomotor@FOOT` -> `Locomotor`. The @suffix only disambiguates sibling keys."""
    at = key.find("@")
    return key if at < 0 else key[:at]


def parse(text: str) -> list[Node]:
    """Parse tab-indented MiniYaml into a node forest. Comments and blanks are dropped."""
    roots: list[Node] = []
    # stack[i] holds the node list that a line at indent level i appends to.
    stack: list[list[Node]] = [roots]

    for raw in text.replace("\r\n", "\n").split("\n"):
        line = raw.rstrip("\n")

        level = 0
        key_start = 0
        for ch in line:
            if ch == " ":
                key_start += 1
            elif ch == "\t":
                level += 1
                key_start += 1
            else:
                break

        key_length = len(line) - key_start
        value_start = -1
        value_length = 0
        for i, ch in enumerate(line):
            if value_start < 0 and ch == ":":
                value_start = i + 1
                key_length = i - key_start
                value_length = len(line) - i - 1
            if ch == "#" and (i == 0 or line[i - 1] != "\\"):
                if i <= key_start + key_length:
                    key_length = i - key_start
                else:
                    value_length = i - value_start
                break

        key = line[key_start:key_start + key_length].strip() if key_length > 0 else ""
        value: str | None = None
        if value_start >= 0:
            trimmed = line[value_start:value_start + value_length].strip()
            if trimmed:
                value = trimmed
        if value is not None and len(value) > 1:
            lead = 1 if value[0] == "\\" and value[1] in " \t" else 0
            trail = 1 if value[-1] == "\\" and value[-2] in " \t" else 0
            if lead or trail:
                value = value[lead:len(value) - lead - trail]
            value = value.replace("\\#", "#")

        if not key:
            continue

        # Trailing lines deeper than their parent are an engine-level yaml error; clamp
        # rather than raise so a malformed file surfaces as a bad result, not a crash.
        level = min(level, len(stack) - 1)
        del stack[level + 1:]

        node = Node(key, value)
        stack[level].append(node)
        stack.append(node.nodes)

    return roots


def _merge_partial(a: Node | None, b: Node | None) -> Node:
    """Merge b over a. b's value wins when set; children merge by key, a's order first."""
    if a is None:
        return Node(b.key, b.value, list(b.nodes))
    if b is None:
        return Node(a.key, a.value, list(a.nodes))
    return Node(a.key, b.value if b.value is not None else a.value,
                _merge_node_lists(a.nodes, b.nodes))


def _merge_node_lists(existing: list[Node], override: list[Node]) -> list[Node]:
    out = [Node(n.key, n.value, list(n.nodes)) for n in existing]
    index = {n.key: i for i, n in enumerate(out)}
    for n in override:
        if n.key in index:
            out[index[n.key]] = _merge_partial(out[index[n.key]], n)
        else:
            index[n.key] = len(out)
            out.append(Node(n.key, n.value, list(n.nodes)))
    return out


def merge_self(nodes: list[Node]) -> list[Node]:
    """Collapse duplicate keys inside one file. Does not resolve inheritance."""
    return _merge_node_lists([], nodes)


def merge_files(sources: list[list[Node]]) -> dict[str, Node]:
    """Merge parsed files in load order into an unresolved top-level tree."""
    merged: list[Node] = []
    for src in sources:
        merged = _merge_node_lists(merged, merge_self(src))
    return {n.key: n for n in merged}


def resolve(tree: dict[str, Node]) -> dict[str, Node]:
    """Resolve Inherits/-removals for every top-level entry. Returns key -> resolved node."""
    out: dict[str, Node] = {}
    for key, node in tree.items():
        out[key] = Node(key, node.value, _resolve_inherits(node.nodes, tree, {key}))
    return out


def _resolve_inherits(nodes: list[Node], tree: dict[str, Node], inherited: set[str]) -> list[Node]:
    resolved: list[Node] = []
    for n in nodes:
        if n.key == "Inherits" or n.key.startswith("Inherits@"):
            parent = tree.get(n.value)
            if parent is None or n.value in inherited:
                # Unknown or already-applied parent: the engine raises, we skip. A cycle
                # here would otherwise recurse forever.
                continue
            for r in _resolve_inherits(parent.nodes, tree, inherited | {n.value}):
                _merge_into(r, resolved, tree, inherited)
        elif n.key.startswith("-"):
            removed = n.key[1:]
            resolved[:] = [r for r in resolved if r.key != removed]
        else:
            _merge_into(n, resolved, tree, inherited)
    return resolved


def _merge_into(override: Node, resolved: list[Node], tree: dict[str, Node], inherited: set[str]) -> None:
    for i, existing in enumerate(resolved):
        if existing.key == override.key:
            merged = _merge_partial(existing, override)
            merged.nodes = _resolve_inherits(merged.nodes, tree, inherited)
            resolved[i] = merged
            return
    copy = Node(override.key, override.value, _resolve_inherits(override.nodes, tree, inherited))
    resolved.append(copy)


def split_list(value: str | None) -> list[str]:
    """`mine, infantry, crate` -> ['mine', 'infantry', 'crate']."""
    if not value:
        return []
    return [p.strip() for p in value.split(",") if p.strip()]
