"""
A faithful-enough Python port of OpenRA's MiniYaml loader, limited to what a
static rules audit needs: parse, cross-file merge, and inheritance resolution.

Ported from engine/OpenRA.Game/MiniYaml.cs at 64185a89:
  FromLines           -> parse()
  MergeSelfPartial    -> merge_self_partial()
  MergePartial        -> merge_partial_nodes() / merge_value()
  ResolveInherits     -> resolve_inherits()
  MergeIntoResolved   -> merge_into_resolved()
  Merge               -> merge_files()

Deliberate omissions, none of which affect an actor/trait audit: comments are
discarded (discardCommentsAndWhitespace=true, the engine's default for rules),
the string pool, and the leading/trailing whitespace guard escapes in values.

Key semantics this file exists to get right, because summarising them from
memory is how rank audits go wrong:
  * top-level keys merge CASE-SENSITIVELY (MiniYaml.cs:405-410)
  * `Inherits@X` and `-Key:` apply WHERE THEY APPEAR, in node order
    (MiniYaml.cs:457-487)
  * a `-Key:` with nothing to remove is a hard error (MiniYaml.cs:480-481)

This is NOT the engine's loader. It reproduces the documented semantics and is
cross-checked against known facts in the audit that consumes it; the
authoritative check remains `--check-yaml`, which this tool never runs.
"""

import os

SPACES_PER_LEVEL = 4


class Node:
    __slots__ = ("key", "value", "children", "loc")

    def __init__(self, key, value=None, children=None, loc=None):
        self.key = key
        self.value = value
        self.children = children if children is not None else []
        self.loc = loc

    def copy(self):
        return Node(self.key, self.value, [c.copy() for c in self.children], self.loc)

    def child(self, key):
        for c in self.children:
            if c.key == key:
                return c
        return None

    def child_value(self, key, default=None):
        c = self.child(key)
        return default if c is None or c.value is None else c.value

    def children_prefixed(self, prefix):
        """Trait nodes are `Name` or `Name@Suffix`; both mean the same trait."""
        return [c for c in self.children
                if c.key == prefix or c.key.startswith(prefix + "@")]

    def has_trait(self, prefix):
        return len(self.children_prefixed(prefix)) > 0

    def __repr__(self):
        return "Node(%r, %r, %d children)" % (self.key, self.value, len(self.children))


class YamlError(Exception):
    pass


def parse(text, name):
    """MiniYaml.FromLines with discardCommentsAndWhitespace=true."""
    parsed = []  # (level, key, value, loc)
    for lineno, line in enumerate(text.split("\n"), start=1):
        line = line.rstrip("\r")
        key_start = 0
        level = 0
        spaces = 0
        if line:
            while key_start < len(line):
                ch = line[key_start]
                if ch == " ":
                    spaces += 1
                    if spaces >= SPACES_PER_LEVEL:
                        spaces = 0
                        level += 1
                    key_start += 1
                elif ch == "\t":
                    level += 1
                    key_start += 1
                else:
                    break

        key = None
        value = None
        if line:
            key_length = len(line) - key_start
            value_start = -1
            value_length = 0
            for i in range(len(line)):
                if value_start < 0 and line[i] == ":":
                    value_start = i + 1
                    key_length = i - key_start
                    value_length = len(line) - i - 1
                if line[i] == "#" and (i == 0 or line[i - 1] != "\\"):
                    if i <= key_start + key_length:
                        key_length = i - key_start
                    else:
                        value_length = i - value_start
                    break
            if key_length > 0:
                key = line[key_start:key_start + key_length].strip()
            if value_start >= 0:
                trimmed = line[value_start:value_start + value_length].strip()
                if trimmed:
                    value = trimmed.replace("\\#", "#")

        if not key:
            continue

        if parsed and parsed[-1][0] < level - 1:
            raise YamlError("Bad indent in miniyaml at %s:%d" % (name, lineno))
        parsed.append((level, key, value, "%s:%d" % (name, lineno)))

    # Rebuild the tree from the flat (level, ...) list.
    root = Node("<root>")
    stack = [(-1, root)]
    for level, key, value, loc in parsed:
        while stack and stack[-1][0] >= level:
            stack.pop()
        node = Node(key, value, [], loc)
        stack[-1][1].children.append(node)
        stack.append((level, node))
    return root.children


def _last_index_of_key(nodes, key):
    for i in range(len(nodes) - 1, -1, -1):
        if nodes[i].key == key:
            return i
    return -1


def _index_of_key(nodes, key):
    for i, n in enumerate(nodes):
        if n.key == key:
            return i
    return -1


def merge_value(existing, override):
    """MiniYaml.MergePartial(MiniYaml, MiniYaml) -- MiniYaml.cs:520-539."""
    if existing is None:
        return override.copy()
    if override is None:
        return existing.copy()
    return Node(override.key,
                override.value if override.value is not None else existing.value,
                merge_partial_nodes(existing.children, override.children),
                override.loc or existing.loc)


def merge_partial_nodes(existing_nodes, override_nodes):
    """MiniYaml.MergePartial(collection, collection) -- MiniYaml.cs:541-585."""
    if not existing_nodes:
        return [n.copy() for n in override_nodes]
    if not override_nodes:
        return [n.copy() for n in existing_nodes]

    ret = []
    plain_keys = set()

    def merge_node(node):
        if node.key.startswith("-"):
            ret.append(node.copy())
            return
        if node.key not in plain_keys:
            plain_keys.add(node.key)
            ret.append(node.copy())
            return
        prev = _last_index_of_key(ret, node.key)
        prev_removal = _last_index_of_key(ret, "-" + node.key)
        if prev_removal != -1 and prev_removal > prev:
            ret.append(node.copy())
            return
        ret[prev] = merge_value(ret[prev], node)

    for n in existing_nodes:
        merge_node(n)
    for n in override_nodes:
        merge_node(n)
    return ret


def merge_self_partial(nodes):
    """MiniYaml.MergeSelfPartial -- MiniYaml.cs:497-517. Within-one-file dupes."""
    keys = set()
    ret = []
    for n in nodes:
        if n.key not in keys:
            keys.add(n.key)
            ret.append(n.copy())
        else:
            i = _index_of_key(ret, n.key)
            ret[i] = merge_value(ret[i], n)
    return ret


def merge_into_resolved(override_node, resolved, resolved_keys, tree, inherited, memo):
    """MiniYaml.MergeIntoResolved -- MiniYaml.cs:427-447."""
    existing_index = -1
    existing = None
    if override_node.key in resolved_keys:
        existing_index = _index_of_key(resolved, override_node.key)
        existing = resolved[existing_index]
    else:
        resolved_keys.add(override_node.key)

    value = merge_value(existing, override_node)
    value.children = resolve_inherits(value.children, tree, inherited, memo)

    if existing is not None:
        resolved[existing_index] = Node(existing.key, value.value, value.children, value.loc)
    else:
        resolved.append(Node(override_node.key, value.value, value.children, value.loc))


def resolve_inherits(children, tree, inherited, memo):
    """MiniYaml.ResolveInherits -- MiniYaml.cs:449-490."""
    if not children:
        return []

    resolved = []
    resolved_keys = set()

    for n in children:
        if n.key == "Inherits" or n.key.startswith("Inherits@"):
            parent_name = n.value
            parent = tree.get(parent_name)
            if parent is None:
                raise YamlError("%s: Parent type `%s` not found" % (n.loc, parent_name))
            if parent_name in inherited:
                raise YamlError("%s: Parent type `%s` was already inherited by this yaml tree"
                                % (n.loc, parent_name))
            inherited = inherited | {parent_name}

            if parent_name in memo:
                parent_resolved = [c.copy() for c in memo[parent_name]]
            else:
                parent_resolved = resolve_inherits(parent.children, tree, inherited, memo)
                memo[parent_name] = [c.copy() for c in parent_resolved]

            for r in parent_resolved:
                merge_into_resolved(r, resolved, resolved_keys, tree, inherited, memo)
        elif n.key.startswith("-"):
            removed = n.key[1:]
            before = len(resolved)
            resolved[:] = [r for r in resolved if r.key != removed]
            if len(resolved) == before:
                raise YamlError("%s: There are no elements with key `%s` to remove"
                                % (n.loc, removed))
            resolved_keys.discard(removed)
        else:
            merge_into_resolved(n, resolved, resolved_keys, tree, inherited, memo)

    return resolved


def merge_files(sources):
    """
    MiniYaml.Merge -- MiniYaml.cs:399-425.
    `sources` is an ordered list of (name, text). Returns {top_level_key: Node}.
    """
    merged = None
    for name, text in sources:
        nodes = merge_self_partial(parse(text, name))
        merged = nodes if merged is None else merge_partial_nodes(merged, nodes)

    tree = {n.key: n for n in (merged or [])}
    memo = {}
    resolved = {}
    for key, node in tree.items():
        children = resolve_inherits(node.children, tree, {key}, memo)
        resolved[key] = Node(key, node.value, children, node.loc)

    # Top-level removals (`-ACTOR:`) -- MiniYaml.cs:420-424.
    top = resolve_inherits(list(resolved.values()), tree, set(), memo)
    return {n.key: n for n in top}


def mod_rule_files(repo_root, mod="ww3mod"):
    """mod.yaml's `Rules:` list, in load order, as (entry, absolute path)."""
    mod_yaml = os.path.join(repo_root, "mods", mod, "mod.yaml")
    with open(mod_yaml, encoding="utf-8") as f:
        lines = f.read().split("\n")

    entries = []
    in_rules = False
    for line in lines:
        if line.startswith("Rules:"):
            in_rules = True
            continue
        if in_rules:
            if line and not line[0].isspace():
                break
            stripped = line.strip()
            if stripped and not stripped.startswith("#"):
                entries.append(stripped)

    out = []
    for entry in entries:
        rel = entry.split("|", 1)[1] if "|" in entry else entry
        out.append((rel, os.path.join(repo_root, "mods", mod, rel)))
    return out


def load_mod_rules(repo_root, mod="ww3mod"):
    """Merge exactly the files mod.yaml lists under `Rules:`, in that order."""
    files = mod_rule_files(repo_root, mod)
    sources = []
    for rel, path in files:
        with open(path, encoding="utf-8") as f:
            sources.append((rel, f.read()))
    return merge_files(sources), [rel for rel, _ in files]
