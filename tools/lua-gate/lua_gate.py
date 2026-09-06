#!/usr/bin/env python3
"""Static check that autotest scenario Lua only names bindings the engine registers.

See README.md — in particular "What this does NOT check", which is the part that
decides whether a green run here means anything for the change you just made.
"""

import argparse
import json
import os
import re
import sys
from collections import OrderedDict

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ENGINE = os.path.join(REPO, "engine")
SCENARIOS = os.path.join(REPO, "tools", "autotest", "scenarios")
MOD_SCRIPTS = os.path.join(REPO, "mods", "ww3mod", "scripts")

# ScriptContext.cs:177-197 — the sandbox whitelist, minus the forbidden math members,
# plus the four names the context installs directly (:200-208).
LUA_BUILTIN_TABLES = {
    "math": {
        "abs", "acos", "asin", "atan", "atan2", "ceil", "cos", "cosh", "deg", "exp",
        "floor", "fmod", "frexp", "huge", "ldexp", "log", "log10", "max", "min",
        "modf", "pi", "pow", "rad", "sin", "sinh", "sqrt", "tan", "tanh",
    },
    "string": {
        "byte", "char", "dump", "find", "format", "gmatch", "gsub", "len", "lower",
        "match", "rep", "reverse", "sub", "upper",
    },
    "table": {"concat", "insert", "maxn", "remove", "sort"},
}

LUA_BUILTIN_VALUES = {
    "ipairs", "next", "pairs", "pcall", "select", "tonumber", "tostring", "type",
    "unpack", "xpcall", "print", "FatalError", "EngineDir", "MaxUserScriptInstructions",
}

# Globals the engine READS out of the script rather than registering into it
# (ScriptContext.cs:242, :310) plus the world-actor hook names.
LUA_ENTRY_POINTS = {"WorldLoaded", "Tick"}

# CLR types whose Lua member set we know exactly, so a member access on a value
# of that type can be checked. Anything not in here is left alone.
SCALAR_TYPES = {"Actor", "Player", "CPos", "CVec", "WPos", "WVec", "WDist", "WAngle"}

# ScriptTypes.ToLuaValue (ScriptTypes.cs:153-196) converts LuaValue, null, double, int, bool,
# string, IScriptBindable and Array, and THROWS on everything else. C# `is int` does not match a
# boxed uint, so a binding declared with any type below returns a value that cannot cross back
# into Lua: the call kills the script outright, before any Test.Pass/Fail arm can run.
#
# This is the gate's blind spot that motivated the check. Resolving that a NAME is registered
# says nothing about whether its RETURN VALUE can be marshalled, and a scenario calling such a
# binding passes lua-gate and then dies at runtime with no verdict.
UNCONVERTIBLE_RETURN_TYPES = frozenset((
    "uint", "long", "ulong", "short", "ushort", "byte", "sbyte", "float", "decimal", "char",
))


# --------------------------------------------------------------------------------------
# C# source scanning
# --------------------------------------------------------------------------------------

def strip_cs(text):
    """Blank out comments, string and char literals, preserving offsets and newlines."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i))
            i = j
        elif c == "/" and i + 1 < n and text[i + 1] == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append("".join(ch if ch == "\n" else " " for ch in text[i:j]))
            i = j
        elif c == "@" and i + 1 < n and text[i + 1] == '"':
            j = i + 2
            while j < n:
                if text[j] == '"':
                    if j + 1 < n and text[j + 1] == '"':
                        j += 2
                        continue
                    j += 1
                    break
                j += 1
            out.append("".join(ch if ch == "\n" else " " for ch in text[i:j]))
            i = j
        elif c in ('"', "'"):
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == c:
                    j += 1
                    break
                if text[j] == "\n":
                    break
                j += 1
            out.append("".join(ch if ch == "\n" else " " for ch in text[i:j]))
            i = j
        else:
            out.append(c)
            i += 1
    return "".join(out)


CLASS_RE = re.compile(
    r"\b(?:public|internal|sealed|abstract|static|partial|readonly|\s)*\b(?:class|struct)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*(?:<[^>{]*>)?\s*(?::(?P<bases>[^{]*))?\{"
)

MEMBER_RE = re.compile(
    r"^\s*public\s+"
    r"(?:(?:virtual|override|sealed|new|abstract|async|extern|unsafe|readonly)\s+)*"
    r"(?P<type>[A-Za-z_][\w\.]*(?:<[^;{}=]*?>)?(?:\s*\[\s*\])*\??)"
    r"\s+(?P<name>[A-Za-z_]\w*)\s*"
    r"(?P<tail>=>|\(|<|\{|=|;|$)"
)

NOT_A_MEMBER = re.compile(r"^\s*public\s+(?:static\b|.*\b(?:class|struct|enum|interface|delegate|event)\b)")


def match_brace(text, open_idx):
    depth, i, n = 0, open_idx, len(text)
    while i < n:
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return n - 1


def class_members(body, class_name):
    """Members the engine would wrap: public, instance, declared here, non-generic.

    Mirrors ScriptMemberWrapper.WrappableMembers (ScriptMemberWrapper.cs:125-142),
    which is BindingFlags.Public | Instance | DeclaredOnly, dropping generic method
    definitions, compiler-generated specials, and fields.
    """
    members = OrderedDict()
    lines = body.split("\n")
    depth = 0
    for idx, line in enumerate(lines):
        if depth == 0 and line.lstrip().startswith("public") and not NOT_A_MEMBER.match(line):
            m = MEMBER_RE.match(line)
            if m:
                name, tail, ctype = m.group("name"), m.group("tail"), m.group("type").strip()
                if name != class_name and ctype not in ("class", "struct", "enum", "interface"):
                    kind = None
                    if tail == "(":
                        kind = "method"
                    elif tail in ("=>", "{"):
                        kind = "property"
                    elif tail == "":
                        # `public bool Foo` with the accessor block on the next line.
                        for nxt in lines[idx + 1:]:
                            s = nxt.strip()
                            if not s or s.startswith("["):
                                continue
                            if s.startswith("{"):
                                kind = "property"
                            break
                    # tail '<' is a generic method definition; tail '=' or ';' is a field.
                    if kind:
                        members[name] = {"kind": kind, "type": ctype, "line": idx}
        depth += line.count("{") - line.count("}")
    return members


def requires_traits(bases):
    return re.findall(r"Requires<\s*([\w\.]+?)Info\s*>", bases or "")


def scan_cs():
    """Walk engine/**/*.cs and build the Lua binding surface."""
    api = {
        "globals": OrderedDict(),
        "actor": OrderedDict(),
        "player": OrderedDict(),
        "value_types": OrderedDict(),
        "sources": 0,
    }
    for root, dirs, files in os.walk(ENGINE):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj", ".git")]
        for fn in sorted(files):
            if not fn.endswith(".cs"):
                continue
            path = os.path.join(root, fn)
            rel = os.path.relpath(path, REPO)
            with open(path, "r", encoding="utf-8", errors="replace") as fh:
                raw = fh.read()
            if "ScriptGlobal" not in raw and "ScriptActorProperties" not in raw \
                    and "ScriptPlayerProperties" not in raw and "ILuaTableBinding" not in raw:
                continue
            api["sources"] += 1
            text = strip_cs(raw)

            for cm in CLASS_RE.finditer(text):
                name, bases = cm.group("name"), cm.group("bases") or ""
                open_idx = text.index("{", cm.end() - 1)
                close_idx = match_brace(text, open_idx)
                body = text[open_idx + 1:close_idx]
                body_line0 = text[:open_idx].count("\n")

                if re.search(r"\bScriptGlobal\b", bases):
                    # [ScriptGlobal("Name")] sits in the attribute block above the class.
                    # Read it from `raw`, not `text` — strip_cs blanks the literal but
                    # preserves offsets, so the same slice is valid in both.
                    am = None
                    for am in re.finditer(r"\[\s*ScriptGlobal\s*\(\s*\"([\w]*)\"",
                                          raw[max(0, cm.start() - 400):cm.start()]):
                        pass
                    if am is None:
                        continue
                    table = am.group(1)
                    if not table:
                        continue
                    mem = class_members(body, name)
                    for v in mem.values():
                        v["line"] += body_line0 + 1
                        v["file"] = rel
                    api["globals"][table] = {"class": name, "file": rel, "members": mem}

                target = None
                if re.search(r"\bScriptActorProperties\b", bases):
                    target = "actor"
                elif re.search(r"\bScriptPlayerProperties\b", bases):
                    target = "player"
                if target:
                    req = requires_traits(bases)
                    for mname, v in class_members(body, name).items():
                        v["line"] += body_line0 + 1
                        v["file"] = rel
                        v["group"] = name
                        v["requires"] = req
                        api[target].setdefault(mname, v)

                if "ILuaTableBinding" in bases and name in SCALAR_TYPES:
                    idx = body.find("LuaValue this[")
                    if idx >= 0:
                        # The case labels are string literals, which strip_cs blanked;
                        # read them from `raw` at the same offsets.
                        raw_body = raw[open_idx + 1:close_idx]
                        cases = re.findall(r'case\s+"(\w+)"\s*:', raw_body[idx:])
                        if cases:
                            api["value_types"][name] = sorted(set(cases))

    api["value_types"]["Actor"] = sorted(api["actor"])
    api["value_types"]["Player"] = sorted(api["player"])
    return api


# --------------------------------------------------------------------------------------
# Lua source scanning
# --------------------------------------------------------------------------------------

def strip_lua(text):
    """Blank out comments and string literals, preserving offsets and newlines."""
    out = []
    i, n = 0, len(text)

    def blank(s):
        return "".join(ch if ch == "\n" else " " for ch in s)

    while i < n:
        c = text[i]
        long_open = re.match(r"\[(=*)\[", text[i:]) if c == "[" else None
        if c == "-" and text.startswith("--", i):
            lm = re.match(r"--\[(=*)\[", text[i:])
            if lm:
                close = "]" + lm.group(1) + "]"
                j = text.find(close, i)
                j = n if j < 0 else j + len(close)
            else:
                j = text.find("\n", i)
                j = n if j < 0 else j
            out.append(blank(text[i:j]))
            i = j
        elif long_open:
            close = "]" + long_open.group(1) + "]"
            j = text.find(close, i)
            j = n if j < 0 else j + len(close)
            out.append(blank(text[i:j]))
            i = j
        elif c in ('"', "'"):
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == c or text[j] == "\n":
                    j += 1
                    break
                j += 1
            out.append(blank(text[i:j]))
            i = j
        else:
            out.append(c)
            i += 1
    return "".join(out)


IDENT = r"[A-Za-z_]\w*"
LOCAL_RE = re.compile(r"\blocal\s+(?:function\s+)?(" + IDENT + r"(?:\s*,\s*" + IDENT + r")*)")
PARAM_RE = re.compile(r"\bfunction\b[^(\n]*\(([^)]*)\)")
FORIN_RE = re.compile(r"\bfor\s+(" + IDENT + r"(?:\s*,\s*" + IDENT + r")*)\s*(?:=|\bin\b)")
GLOBAL_ASSIGN_RE = re.compile(r"(?m)^[ \t]*(" + IDENT + r")\s*=(?!=)")
GLOBAL_FUNC_RE = re.compile(r"(?m)^[ \t]*function\s+(" + IDENT + r")\s*[.:(]")
GLOBAL_FIELD_FUNC_RE = re.compile(r"(?m)^[ \t]*function\s+(" + IDENT + r")\s*[.:]\s*(" + IDENT + r")")
GLOBAL_FIELD_ASSIGN_RE = re.compile(r"(?m)^[ \t]*(" + IDENT + r")\s*\.\s*(" + IDENT + r")\s*=(?!=)")
# A base identifier followed by a member access. The lookbehind stops us re-reading
# `b` in `a.b.c` as if it were a base identifier of its own.
MEMBER_ACCESS_RE = re.compile(r"(?<![\w.:])(" + IDENT + r")\s*([.:])\s*(" + IDENT + r")")
# `x = Table.Member(` with or without a leading `local`.
TYPED_ASSIGN_RE = re.compile(
    r"(?<![\w.:])(?:local\s+)?(" + IDENT + r")\s*=\s*(" + IDENT + r")\s*\.\s*(" + IDENT + r")\s*(\(?)")
ASSIGN_TARGET_RE = re.compile(r"(?m)(?<![\w.:])(" + IDENT + r")\s*=(?!=)")
BRACE_RE = re.compile(r"[{}]")
# `<player>.GetActors()` is the one actor collection that contains the PLAYER ACTOR,
# which carries almost no traits. GetActors is defined on Player only, so any receiver
# will do.
GETACTORS_CALL = r"[\w.:]*\bGetActors\s*\(\s*\)"
GETACTORS_BIND_RE = re.compile(
    r"(?<![\w.:])(?:local\s+)?(" + IDENT + r")\s*=\s*" + GETACTORS_CALL)
IPAIRS_LOOP_RE = re.compile(r"\bfor\s+([\w\s,]+?)\s+in\s+ipairs\s*\(\s*(.+?)\s*\)\s*do")


def table_depth(text):
    """Returns a fn(pos) -> unclosed `{` count before pos.

    Lua scopes with `end`, not braces, so a non-zero depth means exactly one thing:
    we are inside a table constructor, where `Owner = x` is a field name and not an
    assignment to a variable called Owner.
    """
    import bisect
    positions, depths, d = [], [], 0
    for m in BRACE_RE.finditer(text):
        d += 1 if m.group(0) == "{" else -1
        positions.append(m.start())
        depths.append(d)

    def at(pos):
        i = bisect.bisect_right(positions, pos - 1)
        return depths[i - 1] if i else 0

    return at


def lua_bindings(text):
    """Names bound anywhere in the file: locals, params, loop vars."""
    names = set()
    for m in LOCAL_RE.finditer(text):
        names.update(x.strip() for x in m.group(1).split(","))
    for m in PARAM_RE.finditer(text):
        for p in m.group(1).split(","):
            p = p.strip()
            if re.fullmatch(IDENT, p):
                names.add(p)
    for m in FORIN_RE.finditer(text):
        names.update(x.strip() for x in m.group(1).split(","))
    return names


def lua_globals_defined(text):
    """Globals a script installs: `X = ...`, `function X(...)`, `function X.Y(...)`."""
    out = {}
    depth = table_depth(text)
    for m in GLOBAL_ASSIGN_RE.finditer(text):
        if depth(m.start()) == 0:
            out.setdefault(m.group(1), set())
    for m in GLOBAL_FUNC_RE.finditer(text):
        out.setdefault(m.group(1), set())
    for m in GLOBAL_FIELD_FUNC_RE.finditer(text):
        out.setdefault(m.group(1), set()).add(m.group(2))
    for m in GLOBAL_FIELD_ASSIGN_RE.finditer(text):
        if depth(m.start()) == 0:
            out.setdefault(m.group(1), set()).add(m.group(2))
    local = lua_bindings(text)
    return {k: v for k, v in out.items() if k not in local}


def line_of(text, pos):
    return text.count("\n", 0, pos) + 1


# --------------------------------------------------------------------------------------
# MiniYaml structure — enough of it to answer "does the engine ever read this?"
# --------------------------------------------------------------------------------------

def miniyaml_split(line):
    """Reproduce MiniYaml.FromLines' level/key/value split for one line.

    Indentation is copied from the engine verbatim (MiniYaml.cs:239-263): a tab is one
    level, four spaces are one level, and a leftover run of 1-3 spaces is discarded
    without contributing anything. Guessing `leading_whitespace // 4` would NOT reproduce
    it -- a tab does not reset the space counter, so `\t  \t` is two levels and the two
    spaces vanish. Getting this wrong in either direction turns a structural check into a
    liar, so it is transcribed rather than approximated.
    """
    key_start, level, spaces, n = 0, 0, 0, len(line)
    while key_start < n:
        c = line[key_start]
        if c == " ":
            spaces += 1
            if spaces >= 4:
                spaces = 0
                level += 1
            key_start += 1
        elif c == "\t":
            level += 1
            key_start += 1
        else:
            break

    key_length = n - key_start
    value_start, value_length, comment_start = -1, 0, -1
    for i in range(n):
        if value_start < 0 and line[i] == ":":
            value_start = i + 1
            key_length = i - key_start
            value_length = n - i - 1
        if comment_start < 0 and line[i] == "#" and (i == 0 or line[i - 1] != "\\"):
            comment_start = i + 1
            if i <= key_start + key_length:
                key_length = i - key_start
            else:
                value_length = i - value_start
            break

    key = line[key_start:key_start + key_length].strip() if key_length > 0 else ""
    value = ""
    if value_start >= 0 and value_length > 0:
        value = line[value_start:value_start + value_length].strip()
    return level, key, value


def miniyaml_nodes(path):
    """(level, key, value, lineno) for every line the engine would keep."""
    out = []
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            for lineno, raw in enumerate(fh, 1):
                level, key, value = miniyaml_split(raw.rstrip("\n").rstrip("\r"))
                if key:
                    out.append((level, key, value, lineno))
    except OSError:
        pass
    return out


def node_ancestry(nodes, index):
    """Keys of the enclosing nodes of nodes[index], outermost first."""
    level = nodes[index][0]
    out = []
    for j in range(index - 1, -1, -1):
        if nodes[j][0] < level:
            out.append(nodes[j][1])
            level = nodes[j][0]
            if level == 0:
                break
    out.reverse()
    return out


# Map.YamlFields (Map.cs:164-184) -- every field whose value names external files. Each is
# declared `required: false`, and THAT is the whole failure mode: MapField.Deserialize
# (Map.cs:99-108) looks the key up in map.yaml and, when it is absent and optional, plainly
# `return`s. No warning, no log line, no lint finding. A rules.yaml sitting next to a
# map.yaml that never says `Rules: rules.yaml` is read by nobody, and the scenario starts,
# renders, and does nothing.
#
# The value is a comma-separated file list resolved through the map package and then the
# mod filesystem (MiniYaml.cs:625-639 via Map.cs:583-584, Map.Open at :2018-2025).
OPTIONAL_MAP_FILE_FIELDS = (
    "Rules", "Weapons", "Voices", "Notifications", "Music",
    "Sequences", "ModelSequences", "FluentMessages",
)

# Files in a scenario directory that the ENGINE never opens: they are read by the autotest
# shell scripts, not by Map. Excluded from the "present but undeclared" check.
NON_MAP_YAML = re.compile(r"^(map\.yaml|tournament.*\.yaml)$")


def map_declared_files(scen_dir):
    """{filename: (field, lineno)} for every file map.yaml actually asks Map to load."""
    out = OrderedDict()
    for level, key, value, lineno in miniyaml_nodes(os.path.join(scen_dir, "map.yaml")):
        if level != 0 or key not in OPTIONAL_MAP_FILE_FIELDS or not value:
            continue
        for name in (x.strip() for x in value.split(",")):
            if name:
                out.setdefault(name, (key, lineno))
    return out


def loaded_yaml_files(scen_dir):
    """Scenario-local yaml the engine really reads, map.yaml first."""
    files = [os.path.join(scen_dir, "map.yaml")]
    for name in map_declared_files(scen_dir):
        p = os.path.join(scen_dir, name)
        if os.path.exists(p):
            files.append(p)
    return files


def mod_toplevel_keys():
    """Top-level keys of every rules file in the manifest -- what a map override merges against.

    MiniYaml.Merge keys its tree with ordinal, case-sensitive equality (MiniYaml.cs:410,
    :550), so `t03:` against a defining `T03:` contributes a SEPARATE actor rather than
    overriding one. See DOCS/reference/conventions.md, "The override isn't taking effect".
    """
    manifest = os.path.join(REPO, "mods", "ww3mod", "mod.yaml")
    keys = set()
    inside = False
    for level, key, value, _ in miniyaml_nodes(manifest):
        if level == 0:
            inside = key == "Rules"
            continue
        if not inside or level != 1:
            continue
        rel = key.split("|", 1)[-1]
        path = os.path.join(REPO, "mods", "ww3mod", rel)
        for lv, k, _v, _ln in miniyaml_nodes(path):
            if lv == 0:
                keys.add(k)
    return keys


# --------------------------------------------------------------------------------------
# Scenario discovery
# --------------------------------------------------------------------------------------

# --------------------------------------------------------------------------------------
# Silent-inertness checks — a scenario that LOADS CLEAN and does nothing
# --------------------------------------------------------------------------------------
#
# Every finding below describes a scenario the engine accepts without complaint, that lint
# passes, that `--check-yaml` passes, and that then sits on screen doing nothing. That is a
# different failure class from "malformed", which the existing gates already cover, and it
# is strictly worse: a malformed scenario stops the run, an inert one produces a green map
# and an empty Lua log that reads like a slow start.

def scenario_scripts(scen_dir):
    """The Lua the engine really loads, plus findings for every way that can be nothing.

    Returns (names, findings). `names` is empty whenever the engine would load nothing,
    INCLUDING the case where a `Scripts:` line plainly exists in rules.yaml but map.yaml
    never declared `Rules: rules.yaml` — which is precisely the trap this function used to
    fall into itself, by reading rules.yaml unconditionally and so reporting a green gate
    for a scenario whose script the engine had never heard of.
    """
    findings = []
    names = []
    removed_at = None

    for path in loaded_yaml_files(scen_dir):
        rel = os.path.relpath(path, REPO)
        nodes = miniyaml_nodes(path)
        for i, (level, key, value, lineno) in enumerate(nodes):
            if key.startswith("-LuaScript") and node_ancestry(nodes, i) == ["World"]:
                removed_at = (rel, lineno, key)
                continue
            if key != "Scripts":
                continue

            ancestry = node_ancestry(nodes, i)
            ok = (len(ancestry) == 2 and ancestry[0] == "World"
                  and ancestry[1].split("@")[0] == "LuaScript")
            if not ok:
                where = " > ".join(ancestry + ["Scripts"]) if ancestry else "the top level"
                findings.append(Finding(
                    "error", rel, lineno, "Scripts",
                    "this `Scripts:` is at " + where + ", not World > LuaScript > Scripts. "
                    "LuaScriptInfo is [TraitLocation(SystemActors.World)] "
                    "(Scripting/LuaScript.cs:21-26), so nothing here is read and the scripts "
                    "it names never load."))
                continue
            names.extend(x.strip() for x in value.split(",") if x.strip())

    if names and removed_at:
        rel, lineno, key = removed_at
        findings.append(Finding(
            "error", rel, lineno, key,
            "removes the LuaScript trait from World while a `Scripts:` line still names "
            "scripts. Whichever resolves last, one of the two is a lie — and if the removal "
            "wins, nothing in this scenario runs."))

    return names, findings


def check_map_wiring(scen_dir, rel_dir, findings):
    """Optional map files that exist and are never read, or are declared and absent.

    Both come from the same handful of lines. `MapField.Deserialize` (Map.cs:99-108) looks
    the key up in map.yaml and, when it is absent and the field is optional, plainly
    `return`s — no warning, no log line. And `Map.PostInit` (Map.cs:583-592) SWALLOWS the
    exception when a declared file cannot be opened, logging to debug.log and falling back
    to `Ruleset.LoadDefaultsForTileSet`. Neither path reaches the player, the runner, or
    lint. `CheckLuaScript` cannot see it either: it reads the World actor's `LuaScriptInfo`
    and returns early when there is none (Lint/CheckLuaScript.cs:21-23), which is exactly
    the state an unloaded rules.yaml produces.
    """
    declared = map_declared_files(scen_dir)
    present = {f for f in os.listdir(scen_dir)
               if f.endswith(".yaml") and not NON_MAP_YAML.match(f)}
    map_rel = os.path.join(rel_dir, "map.yaml")

    for name in sorted(present - set(declared)):
        field = "Weapons" if "weapon" in name else "Rules"
        findings.append(Finding(
            "error", map_rel, 0, name,
            "`" + name + "` sits in the scenario directory and map.yaml never declares it. "
            "Map only reads a file it is told about, so everything in there — rules, "
            "weapons, and any LuaScript > Scripts line — is inert. Add `" + field + ": "
            + name + "` to map.yaml, or delete the file."))

    for name, (field, lineno) in declared.items():
        if not os.path.exists(os.path.join(scen_dir, name)):
            findings.append(Finding(
                "error", map_rel, lineno, name,
                "`" + field + ": " + name + "` names a file that is not in the scenario "
                "directory. Map.PostInit catches the open failure, logs it to debug.log only "
                "and falls back to the stock ruleset — so the map still starts, with none of "
                "its overrides."))


def check_key_casing(scen_dir, findings):
    """Top-level override keys that differ from the mod's spelling only by case.

    `MiniYaml.Merge` compares keys ordinally (MiniYaml.cs:410, :550), so `t03:` against a
    defining `T03:` overrides nothing — it contributes a second, unrelated top-level key.
    The silent variant is the one worth gating: an abstract `^Template` is dropped by
    `filterNode` before the duplicate-key check (Ruleset.cs:185) and simply sits unused.
    See DOCS/reference/conventions.md, "The override isn't taking effect".
    """
    defined = mod_toplevel_keys()
    if not defined:
        return
    lower = {}
    for k in defined:
        lower.setdefault(k.lower(), set()).add(k)

    for path in loaded_yaml_files(scen_dir):
        if os.path.basename(path) == "map.yaml":
            continue
        rel = os.path.relpath(path, REPO)
        for level, key, _value, lineno in miniyaml_nodes(path):
            if level != 0 or key in defined:
                continue
            alts = lower.get(key.lower())
            if alts:
                findings.append(Finding(
                    "error", rel, lineno, key,
                    "differs only in case from " + " / ".join(sorted(alts)) + ", which is how "
                    "the mod spells it. MiniYaml.Merge is ordinal (MiniYaml.cs:410), so this "
                    "overrides nothing — it defines a separate top-level key that nothing "
                    "refers to."))


def verdict_seeds():
    """`Test.*` bindings that end the run, re-derived from TestGlobal.cs.

    Every one of them funnels through `ExitWhenCapturesFlushed`, which writes result.json
    and exits — so the marker is the call, not a hand-kept list of three names. Today that
    is Pass, Fail and Skip; a fourth verdict added in C# is picked up here for free, and a
    renamed one stops being treated as terminal instead of silently still counting.
    """
    path = os.path.join(ENGINE, "OpenRA.Mods.Common", "Scripting", "Global", "TestGlobal.cs")
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            text = strip_cs(fh.read())
    except OSError:
        return {"Test.Pass", "Test.Fail", "Test.Skip"}

    starts = [(m.start(), m.group(1)) for m in
              re.finditer(r"\bpublic\s+[\w<>\[\],\s]+?\s(\w+)\s*\(", text)]
    out = set()
    for i, (pos, name) in enumerate(starts):
        end = starts[i + 1][0] if i + 1 < len(starts) else len(text)
        if "ExitWhenCapturesFlushed" in text[pos:end]:
            out.add("Test." + name)
    return out or {"Test.Pass", "Test.Fail", "Test.Skip"}


def lua_file_functions(text):
    """{name: body} for file-scope `function Name(...)` / `function Tbl.Name(...)`.

    Bodies are delimited by the next file-scope `function` line rather than by a matching
    `end`, which is how every helper in mods/ww3mod/scripts is actually written. A
    terminating call nested inside a local closure is therefore attributed to the enclosing
    file-scope function — the direction that errs toward silence rather than false alarms.
    """
    out = OrderedDict()
    starts = [(m.start(), m.group(1)) for m in
              re.finditer(r"^function\s+([A-Za-z_][\w.:]*)\s*\(", text, re.M)]
    for i, (pos, name) in enumerate(starts):
        end = starts[i + 1][0] if i + 1 < len(starts) else len(text)
        out[name.replace(":", ".")] = text[pos:end]
    return out


def verdict_names(script_texts, seeds=None):
    """Every symbol whose use can transitively reach a terminal Test.* verdict."""
    bodies = OrderedDict()
    for text in script_texts:
        bodies.update(lua_file_functions(text))

    terminating = set(seeds if seeds is not None else verdict_seeds())
    for _ in range(8):  # depth cap; helper call chains run one or two deep in practice
        grew = False
        for name, body in bodies.items():
            if name in terminating:
                continue
            if any(t.split(".")[-1] in body for t in terminating):
                terminating.add(name)
                grew = True
        if not grew:
            break
    return terminating


def check_verdict_reachable(name, scen_dir, luas, declared, own_text, findings, seeds=None):
    """A `test-` scenario whose Lua names no way to finish.

    A demo is allowed to run forever — that is what a demo is. A test is not: with no
    Test.Pass and no Test.Fail the harness never gets a result.json, and the run ends as a
    watchdog TIMEOUT-FAIL minutes later, which reads like a slow scenario rather than an
    unfinished one.
    """
    if not name.startswith("test-"):
        return
    texts = [own_text]
    for s in declared:
        path = resolve_script(s, scen_dir)
        if path and os.path.basename(path) not in luas:
            with open(path, "r", encoding="utf-8", errors="replace") as fh:
                texts.append(strip_lua(fh.read()))

    for sym in verdict_names(texts, seeds):
        probe = sym.split(".")[-1]
        if re.search(r"\b" + re.escape(probe) + r"\b", own_text):
            return

    findings.append(Finding(
        "warn", os.path.relpath(os.path.join(scen_dir, name + ".lua"), REPO), 0, name,
        "a `test-` scenario whose script names nothing that reaches a terminal Test verdict, "
        "directly or through a helper it loads. It cannot produce a result.json, so the run ends "
        "as a watchdog timeout. Either it asserts nothing and should be a `demo-`, or its "
        "assertion was lost."))


def map_actor_names(scen_dir):
    """Instance names under `Actors:` in map.yaml — MapGlobal.cs:34-36 makes each a global.

    Read through the MiniYaml level parser rather than a `^\\t` regex. Every scenario in the
    tree happens to indent map.yaml with tabs today, but four spaces is equally valid to the
    engine, and a regex that only knows about tabs would return an EMPTY actor set for such a
    file — silently turning off every actor-global check in this gate for that scenario. A
    false green in the guard is the failure this whole tool exists to prevent.
    """
    nodes = miniyaml_nodes(os.path.join(scen_dir, "map.yaml"))
    names = set()
    for i, (level, key, _value, _lineno) in enumerate(nodes):
        if level == 1 and node_ancestry(nodes, i) == ["Actors"] and re.match(r"^[A-Za-z_]\w*$", key):
            names.add(key)
    return names


def resolve_script(name, scen_dir):
    for cand in (os.path.join(scen_dir, name), os.path.join(MOD_SCRIPTS, name)):
        if os.path.exists(cand):
            return cand
    return None


# --------------------------------------------------------------------------------------
# The checks
# --------------------------------------------------------------------------------------

class Finding:
    def __init__(self, severity, path, line, symbol, message):
        self.severity = severity
        self.path = path
        self.line = line
        self.symbol = symbol
        self.message = message

    def __str__(self):
        return f"{self.path}:{self.line}: [{self.severity}] {self.symbol} — {self.message}"


def suggest(name, candidates, limit=3):
    """Cheap nearest-name hint; no stdlib difflib fuzziness beyond a ratio cut."""
    import difflib
    return difflib.get_close_matches(name, list(candidates), n=limit, cutoff=0.7)


def check_file(lua_path, api, extra_globals, actor_globals, findings):
    with open(lua_path, "r", encoding="utf-8", errors="replace") as fh:
        raw = fh.read()
    text = strip_lua(raw)
    rel = os.path.relpath(lua_path, REPO)

    bound = lua_bindings(text)
    own_globals = lua_globals_defined(text)
    tables = api["globals"]

    known_bases = set()
    known_bases |= set(tables)
    known_bases |= set(LUA_BUILTIN_TABLES)
    known_bases |= LUA_BUILTIN_VALUES
    known_bases |= LUA_ENTRY_POINTS
    known_bases |= set(extra_globals)
    known_bases |= actor_globals
    known_bases |= set(own_globals)
    known_bases |= bound

    # Variables whose CLR type we can pin: assigned exactly once in the file, from a
    # global-table member whose C# return type we know. Anything assigned more than
    # once is dropped rather than guessed at.
    depth = table_depth(text)
    assign_counts = {}
    for m in ASSIGN_TARGET_RE.finditer(text):
        if depth(m.start()) == 0:
            assign_counts[m.group(1)] = assign_counts.get(m.group(1), 0) + 1
    typed = {}
    for m in TYPED_ASSIGN_RE.finditer(text):
        var, table, member, called = m.groups()
        if depth(m.start()) != 0 or var in tables:
            continue
        if table not in tables or table in bound:
            continue
        info = tables[table]["members"].get(member)
        if not info:
            continue
        ctype = info["type"].rstrip("?")
        if ctype not in SCALAR_TYPES:
            continue
        if info["kind"] == "method" and not called:
            continue
        if assign_counts.get(var, 0) != 1 or var in typed:
            typed[var] = None  # ambiguous — stop trusting it
            continue
        typed[var] = ctype
    typed = {k: v for k, v in typed.items() if v}
    # Map actors are registered as Actor globals (MapGlobal.cs:34-36). One that is
    # never rebound in this file keeps that type.
    for name in actor_globals:
        if name not in bound and assign_counts.get(name, 0) == 0:
            typed.setdefault(name, "Actor")

    # Elements of `<player>.GetActors()`. That collection includes the PLAYER ACTOR,
    # which has almost no traits, and reading a property an actor does not define
    # THROWS rather than returning nil — so an `a.Location ~= nil` guard can never
    # fire. This aborted test-drone-lost-track mid-tick and cost a launch slot
    # (fixed in 1d3c9db0).
    from_getactors = set()
    bound_lists = {m.group(1) for m in GETACTORS_BIND_RE.finditer(text)}
    for m in IPAIRS_LOOP_RE.finditer(text):
        names = [x.strip() for x in m.group(1).split(",")]
        src = m.group(2)
        if re.fullmatch(GETACTORS_CALL, src) or src in bound_lists:
            if names:
                from_getactors.add(names[-1])

    gated = {k for k, v in api["actor"].items() if v["requires"]}

    for m in MEMBER_ACCESS_RE.finditer(text):
        base, sep, member = m.group(1), m.group(2), m.group(3)
        line = line_of(text, m.start())

        if base in from_getactors and member in gated:
            req = ", ".join(api["actor"][member]["requires"])
            findings.append(Finding(
                "warn", rel, line, f"{base}.{member}",
                f"'{base}' iterates a player's GetActors(), which includes the player "
                f"actor; '{member}' needs {req} and reading it off an actor that lacks "
                f"the trait THROWS (it does not return nil). Ask the question spatially "
                f"(Map.ActorsInCircle) or filter by Type first."))
            continue

        if base in tables and base not in bound and base not in own_globals:
            if member not in tables[base]["members"]:
                hint = suggest(member, tables[base]["members"])
                extra = f" Did you mean {', '.join(hint)}?" if hint else ""
                findings.append(Finding(
                    "error", rel, line, f"{base}.{member}",
                    f"table '{base}' ({tables[base]['class']}) defines no member "
                    f"'{member}'.{extra}"))
            else:
                rtype = tables[base]["members"][member]["type"].rstrip("?")
                if rtype in UNCONVERTIBLE_RETURN_TYPES:
                    findings.append(Finding(
                        "warn", rel, line, f"{base}.{member}",
                        f"'{base}.{member}' is registered, but returns '{rtype}', which "
                        f"ScriptTypes.ToLuaValue cannot convert — it handles double, int, bool, "
                        f"string, IScriptBindable and Array, and a boxed '{rtype}' matches none "
                        f"of them. CALLING THIS KILLS THE SCRIPT ('Cannot convert type') before "
                        f"any Test.Pass/Fail arm can run, so the run reports a bare FAIL with no "
                        f"verdict. Fix the BINDING's return type (int or double), not the call."))
            continue

        if base in typed:
            ctype = typed[base]
            allowed = api["value_types"].get(ctype)
            if allowed is not None and member not in allowed:
                hint = suggest(member, allowed)
                extra = f" Did you mean {', '.join(hint)}?" if hint else ""
                where = "any actor" if ctype == "Actor" else ("any player" if ctype == "Player" else ctype)
                findings.append(Finding(
                    "error", rel, line, f"{base}.{member}",
                    f"'{base}' is a {ctype}; no {ctype} property '{member}' exists on "
                    f"{where}.{extra}"))
            continue

        if base in LUA_BUILTIN_TABLES:
            if member not in LUA_BUILTIN_TABLES[base]:
                findings.append(Finding(
                    "error", rel, line, f"{base}.{member}",
                    f"Lua 5.1 '{base}' has no member '{member}' (or it is sandboxed out)."))
            continue

        if base not in known_bases:
            findings.append(Finding(
                "warn", rel, line, base,
                f"'{base}' is not an engine table, a map actor, a local, or a global "
                f"defined by this scenario's scripts; '{base}{sep}{member}' will index nil."))
            known_bases.add(base)  # report each unknown base once per file


def collect_scenarios(filters):
    """Every scenario directory, with the .lua files it carries (possibly none).

    A directory with no Lua is still checked for wiring: a tournament scenario with an
    undeclared rules.yaml is as inert as a scripted one, and skipping it here is how the
    whole class stayed invisible.
    """
    out = []
    if not os.path.isdir(SCENARIOS):
        return out
    for name in sorted(os.listdir(SCENARIOS)):
        d = os.path.join(SCENARIOS, name)
        if not os.path.isdir(d):
            continue
        if filters and not any(f in name for f in filters):
            continue
        out.append((name, d, sorted(f for f in os.listdir(d) if f.endswith(".lua"))))
    return out


def run_check(args):
    api = scan_cs()
    findings = []
    scenarios = collect_scenarios(args.scenario)
    files = 0

    helper_cache = {}
    seeds = verdict_seeds()

    for name, d, luas in scenarios:
        rel_dir = os.path.relpath(d, REPO)

        # --- wiring, before anything else: it decides which files the rest may believe.
        check_map_wiring(d, rel_dir, findings)
        check_key_casing(d, findings)
        declared, wiring = scenario_scripts(d)
        findings.extend(wiring)

        actors = map_actor_names(d)

        # Globals contributed by the helper scripts this map actually loads.
        extra = {}
        for s in declared:
            if s in luas:
                continue
            path = resolve_script(s, d)
            if not path:
                findings.append(Finding(
                    "warn", os.path.join(rel_dir, "rules.yaml"), 0, s,
                    "rules.yaml declares this script but it was not found in the map "
                    "directory or mods/ww3mod/scripts."))
                continue
            if path not in helper_cache:
                with open(path, "r", encoding="utf-8", errors="replace") as fh:
                    helper_cache[path] = lua_globals_defined(strip_lua(fh.read()))
            extra.update(helper_cache[path])

        if not declared:
            for lua in luas:
                findings.append(Finding(
                    "error", os.path.join(rel_dir, lua), 0, lua,
                    "no `Scripts:` line the engine can reach names this file, so it never "
                    "loads: its WorldLoaded never runs, nothing in it is checked here, and "
                    "the scenario starts and does nothing."))
            continue

        for lua in luas:
            # Only gate the scripts the map actually loads; an orphan .lua in the
            # directory is dead weight the engine never sees.
            if lua not in declared:
                findings.append(Finding(
                    "warn", os.path.join(rel_dir, lua), 0, lua,
                    "not listed in this scenario's `Scripts:`; the engine never loads it, "
                    "so it is unchecked and probably dead."))
                continue
            files += 1
            check_file(os.path.join(d, lua), api, extra, actors, findings)

        own = os.path.join(d, name + ".lua")
        if os.path.basename(own) in declared and os.path.exists(own):
            with open(own, "r", encoding="utf-8", errors="replace") as fh:
                check_verdict_reachable(
                    name, d, luas, declared, strip_lua(fh.read()), findings, seeds)

    seen, deduped = set(), []
    for f in findings:
        key = (f.path, f.line, f.symbol, f.severity)
        if key in seen:
            continue
        seen.add(key)
        deduped.append(f)
    findings = deduped

    errors = [f for f in findings if f.severity == "error"]
    warns = [f for f in findings if f.severity == "warn"]

    for f in sorted(errors, key=lambda x: (x.path, x.line)):
        print(f)
    if errors and warns:
        print()
    for f in sorted(warns, key=lambda x: (x.path, x.line)):
        print(f)

    print()
    print(f"lua-gate: {files} script(s) in {len(scenarios)} scenario(s); "
          f"{len(api['globals'])} engine tables, {len(api['actor'])} actor properties, "
          f"{len(api['player'])} player properties from {api['sources']} C# file(s).")
    if errors:
        print(f"lua-gate: FAIL — {len(errors)} error(s), {len(warns)} warning(s).")
        return 2
    if warns:
        print(f"lua-gate: WARN — {len(warns)} warning(s).")
        return 2 if args.strict else 1
    print("lua-gate: OK — every reference resolves, and every scenario is wired to run.")
    return 0


def run_api(args):
    api = scan_cs()
    if args.json:
        print(json.dumps({
            "globals": {k: {"class": v["class"], "file": v["file"],
                            "members": {mk: mv["kind"] for mk, mv in v["members"].items()}}
                        for k, v in api["globals"].items()},
            "actor": sorted(api["actor"]),
            "player": sorted(api["player"]),
            "value_types": api["value_types"],
        }, indent=2, sort_keys=True))
        return 0
    for table in sorted(api["globals"]):
        info = api["globals"][table]
        print(f"{table}  ({info['class']}, {info['file']})")
        for m in sorted(info["members"]):
            print(f"    {m}  [{info['members'][m]['kind']}: {info['members'][m]['type']}]")
    print(f"\nActor properties ({len(api['actor'])}):")
    print("    " + ", ".join(sorted(api["actor"])))
    print(f"\nPlayer properties ({len(api['player'])}):")
    print("    " + ", ".join(sorted(api["player"])))
    print("\nValue types:")
    for t in sorted(api["value_types"]):
        if t in ("Actor", "Player"):
            continue
        print(f"    {t}: {', '.join(api['value_types'][t])}")
    return 0


# The signature is the first bold run on the row. Do not anchor on the trailing `|`:
# queued-activity rows carry a `<br />*Queued Activity*` suffix after the bold close.
DOCS_ROW_RE = re.compile(r"^\|\s*\*\*(.+?)\*\*")


def parse_lua_docs(path):
    """Parse `utility.sh --lua-docs` output — the engine's own reflection dump.

    ExtractLuaDocsCommand walks exactly the types this file parses out of source
    (ScriptGlobal / ScriptActorProperties / ScriptPlayerProperties) through
    ScriptMemberWrapper.WrappableMembers, so it is ground truth for the extractor.
    """
    out = {"globals": OrderedDict(), "actor": set(), "player": set()}
    section, table = None, None
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if line.startswith("## Global Tables"):
                section, table = "globals", None
                continue
            if line.startswith("## Actor Properties"):
                section, table = "actor", None
                continue
            if line.startswith("## Player Properties"):
                section, table = "player", None
                continue
            if line.startswith("### "):
                table = line[4:].strip()
                if section == "globals":
                    out["globals"].setdefault(table, set())
                continue
            m = DOCS_ROW_RE.match(line)
            if not m or section is None:
                continue
            sig = m.group(1).replace("<s>", "").replace("</s>", "").strip()
            sm = re.match(r"^\S+\s+(\w+)", sig)
            if not sm:
                continue
            if section == "globals":
                if table is not None:
                    out["globals"][table].add(sm.group(1))
            else:
                out[section].add(sm.group(1))
    return out


def run_verify(args):
    api = scan_cs()
    docs = parse_lua_docs(args.docs)
    mismatches = 0

    mine = set(api["globals"])
    theirs = set(docs["globals"])
    for t in sorted(mine - theirs):
        print(f"  EXTRA table   {t}")
        mismatches += 1
    for t in sorted(theirs - mine):
        print(f"  MISSING table {t}")
        mismatches += 1
    for t in sorted(mine & theirs):
        m, d = set(api["globals"][t]["members"]), docs["globals"][t]
        for x in sorted(m - d):
            print(f"  EXTRA   {t}.{x}")
            mismatches += 1
        for x in sorted(d - m):
            print(f"  MISSING {t}.{x}")
            mismatches += 1

    for key in ("actor", "player"):
        m, d = set(api[key]), docs[key]
        for x in sorted(m - d):
            print(f"  EXTRA   <{key}>.{x}")
            mismatches += 1
        for x in sorted(d - m):
            print(f"  MISSING <{key}>.{x}")
            mismatches += 1

    total = sum(len(v["members"]) for v in api["globals"].values()) + len(api["actor"]) + len(api["player"])
    print()
    print(f"lua-gate verify: {len(api['globals'])} tables, {total} members parsed from source "
          f"vs {len(docs['globals'])} tables, "
          f"{sum(len(v) for v in docs['globals'].values()) + len(docs['actor']) + len(docs['player'])} "
          f"from --lua-docs.")
    if mismatches:
        print(f"lua-gate verify: FAIL — {mismatches} difference(s). The source parser has "
              f"drifted from what the engine actually registers.")
        return 2
    print("lua-gate verify: OK — source parse is byte-identical to the engine's reflection dump.")
    return 0


SELFTEST_CASES = [
    # (description, callable(api) -> bool)
    ("Trigger table exists", lambda a: "Trigger" in a["globals"]),
    ("Trigger.AfterDelay is registered", lambda a: "AfterDelay" in a["globals"]["Trigger"]["members"]),
    ("Trigger.OnKilled is registered", lambda a: "OnKilled" in a["globals"]["Trigger"]["members"]),
    # OnTick was ADDED on 2026-09-06 (0b9c7482). This case pinned its absence and so had been
    # failing — and with it `make lua-gate` and `make test` — from that merge onward.
    ("Trigger.OnTick is registered (added 2026-09-06, 0b9c7482)",
     lambda a: "OnTick" in a["globals"]["Trigger"]["members"]),
    # A public STATIC helper on TriggerGlobal, so not a binding. Pins the WrappableMembers
    # instance-only rule that OnTick's absence used to pin.
    ("Trigger.GetScriptTriggers is NOT registered",
     lambda a: "GetScriptTriggers" not in a["globals"]["Trigger"]["members"]),
    ("Player.GetPlayer is registered", lambda a: "GetPlayer" in a["globals"]["Player"]["members"]),
    ("Player.GetPlayer returns Player", lambda a: a["globals"]["Player"]["members"]["GetPlayer"]["type"] == "Player"),
    ("Actor.Create is registered", lambda a: "Create" in a["globals"]["Actor"]["members"]),
    ("Actor.Create returns Actor", lambda a: a["globals"]["Actor"]["members"]["Create"]["type"] == "Actor"),
    ("Test table exists", lambda a: "Test" in a["globals"]),
    ("Test.Fail is registered", lambda a: "Fail" in a["globals"]["Test"]["members"]),
    ("actor property Location exists", lambda a: "Location" in a["actor"]),
    ("player property Location does NOT exist", lambda a: "Location" not in a["player"]),
    ("player property Cash exists", lambda a: "Cash" in a["player"]),
    ("static helper GetScriptTriggers is not exposed",
     lambda a: "GetScriptTriggers" not in a["globals"]["Trigger"]["members"]),
    ("CPos value members are X, Y, Layer", lambda a: a["value_types"]["CPos"] == ["Layer", "X", "Y"]),
    ("WPos value members are X, Y, Z", lambda a: a["value_types"]["WPos"] == ["X", "Y", "Z"]),
    ("WAngle value member is Angle", lambda a: a["value_types"]["WAngle"] == ["Angle"]),
    # Loose floors, not exact counts: a legitimate new binding must not turn this red,
    # or the number just gets bumped without thought. `verify` is the exact check.
    ("at least 20 global tables", lambda a: len(a["globals"]) >= 20),
    ("at least 90 actor properties", lambda a: len(a["actor"]) >= 90),
    ("at least 45 player properties", lambda a: len(a["player"]) >= 45),
    ("queued-activity commands are parsed", lambda a: {"Move", "Attack", "Wait"} <= set(a["actor"])),
]

LUA_SELFTEST = """
WorldLoaded = function()
\tlocal p = Player.GetPlayer("USA")
\tlocal a = Actor.Create("halo", true, { Owner = p })
\tTrigger.OnEveryTick(function() end)
\tTrigger.OnTick(function() end)
\tTrigger.AfterDelay(5, function() end)
\tprint(p.Location)
\tprint(a.Location.X)
\tprint(Bogus.Thing)
\tprint(string.format("%d", 1))
\tfor _, w in ipairs(p.GetActors()) do
\t\tif w.Location ~= nil and not w.IsDead then print(w.Type) end
\tend
end
"""


# --------------------------------------------------------------------------------------
# Acceptance test for the inertness checks
# --------------------------------------------------------------------------------------
#
# These checks find nothing on a clean tree, which is the whole problem with them: a guard
# nobody has seen fire is indistinguishable from a guard that cannot. So each failure mode
# is built here as a synthetic scenario and the check is required to report it, and a
# correctly-wired scenario built the same way is required to produce silence.

WIRING_MAP = """MapFormat: 12
RequiresMod: ww3mod
Title: TEST
Author: WW3MOD
Tileset: TEMPERAT
MapSize: 32,32
Bounds: 1,1,30,30
Visibility: MissionSelector
Categories: Test
Players:
Actors:
"""

WIRING_RULES = """World:
\tLuaScript:
\t\tScripts: zz.lua
"""

WIRING_LUA = "WorldLoaded = function() Test.Pass(\"ok\") end\n"


def _mk(root, name, files):
    d = os.path.join(root, name)
    os.makedirs(d, exist_ok=True)
    for fn, body in files.items():
        with open(os.path.join(d, fn), "w", encoding="utf-8", newline="\n") as fh:
            fh.write(body)
    return d


def _wiring_findings(d, name):
    """Every inertness check, run over one synthetic scenario directory."""
    findings = []
    check_map_wiring(d, name, findings)
    check_key_casing(d, findings)
    declared, wiring = scenario_scripts(d)
    findings.extend(wiring)
    luas = sorted(f for f in os.listdir(d) if f.endswith(".lua"))
    if not declared:
        for lua in luas:
            findings.append(Finding("error", os.path.join(name, lua), 0, lua, "never loaded"))
    return declared, findings


def run_wiring_acceptance():
    """Build each failure mode, require it to fire, require the clean one to stay quiet."""
    import tempfile
    results = []

    with tempfile.TemporaryDirectory() as root:
        # -- the control: correctly wired, must produce nothing at all.
        d = _mk(root, "zz-ok", {
            "map.yaml": WIRING_MAP + "Rules: rules.yaml\n",
            "rules.yaml": WIRING_RULES, "zz.lua": WIRING_LUA})
        declared, f = _wiring_findings(d, "zz-ok")
        results.append(("a correctly wired scenario produces no finding",
                        declared == ["zz.lua"] and not f))

        # -- mode 1: rules.yaml on disk, never declared. THE bug of 2026-09-06.
        d = _mk(root, "zz-undeclared", {
            "map.yaml": WIRING_MAP, "rules.yaml": WIRING_RULES, "zz.lua": WIRING_LUA})
        declared, f = _wiring_findings(d, "zz-undeclared")
        results.append(("an undeclared rules.yaml is reported",
                        any(x.symbol == "rules.yaml" and x.severity == "error" for x in f)))
        results.append(("...and its Scripts: line is NOT counted as loaded", declared == []))

        # -- mode 1b: declared and absent. Map.PostInit swallows the open failure.
        d = _mk(root, "zz-ghost", {
            "map.yaml": WIRING_MAP + "Rules: rules.yaml\n", "zz.lua": WIRING_LUA})
        _declared, f = _wiring_findings(d, "zz-ghost")
        results.append(("a declared-but-absent rules.yaml is reported",
                        any(x.symbol == "rules.yaml" and "not in the scenario directory" in x.message
                            for x in f)))

        # -- mode 2: Scripts: parented somewhere the engine never reads it.
        d = _mk(root, "zz-misparented", {
            "map.yaml": WIRING_MAP + "Rules: rules.yaml\n",
            "rules.yaml": "Player:\n\tLuaScript:\n\t\tScripts: zz.lua\n",
            "zz.lua": WIRING_LUA})
        declared, f = _wiring_findings(d, "zz-misparented")
        results.append(("a Scripts: under Player instead of World is reported",
                        any(x.symbol == "Scripts" and x.severity == "error" for x in f)
                        and declared == []))

        # -- mode 2b: the trait removed out from under a live Scripts: line.
        d = _mk(root, "zz-removed", {
            "map.yaml": WIRING_MAP + "Rules: rules.yaml\n",
            "rules.yaml": "World:\n\t-LuaScript:\n\tLuaScript:\n\t\tScripts: zz.lua\n",
            "zz.lua": WIRING_LUA})
        _declared, f = _wiring_findings(d, "zz-removed")
        results.append(("a -LuaScript: alongside a live Scripts: is reported",
                        any(x.symbol.startswith("-LuaScript") for x in f)))

        # -- mode 3: a top-level key that differs from the mod's only by case.
        defined = mod_toplevel_keys()
        cased = next((k for k in sorted(defined) if k.lower() != k and not k.startswith("^")), None)
        if cased:
            d = _mk(root, "zz-case", {
                "map.yaml": WIRING_MAP + "Rules: rules.yaml\n",
                "rules.yaml": WIRING_RULES + "\n" + cased.lower() + ":\n\tHealth:\n\t\tHP: 1\n",
                "zz.lua": WIRING_LUA})
            _declared, f = _wiring_findings(d, "zz-case")
            results.append((f"a mis-cased top-level key ({cased.lower()} vs {cased}) is reported",
                            any(x.symbol == cased.lower() for x in f)))
            # ...and the correctly-cased spelling of the same key must stay silent.
            d = _mk(root, "zz-case-ok", {
                "map.yaml": WIRING_MAP + "Rules: rules.yaml\n",
                "rules.yaml": WIRING_RULES + "\n" + cased + ":\n\tHealth:\n\t\tHP: 1\n",
                "zz.lua": WIRING_LUA})
            _declared, f = _wiring_findings(d, "zz-case-ok")
            results.append((f"...and the correctly-cased {cased} does not fire", not f))
        else:
            results.append(("a mis-cased top-level key is reported  (SKIPPED: no mixed-case "
                            "key in the manifest)", False))

        # -- mode 4: a test- scenario that can never reach a verdict.
        seeds = verdict_seeds()
        d = _mk(root, "test-zz-silent", {
            "map.yaml": WIRING_MAP + "Rules: rules.yaml\n",
            "rules.yaml": "World:\n\tLuaScript:\n\t\tScripts: test-zz-silent.lua\n",
            "test-zz-silent.lua": "WorldLoaded = function() end\n"})
        f = []
        check_verdict_reachable("test-zz-silent", d, ["test-zz-silent.lua"],
                                ["test-zz-silent.lua"], "WorldLoaded = function() end", f, seeds)
        results.append(("a test- scenario with no reachable verdict is reported", len(f) == 1))

        f = []
        check_verdict_reachable("test-zz-loud", d, ["x.lua"], ["x.lua"],
                                "WorldLoaded = function() Test.Skip('x') end", f, seeds)
        results.append(("...and one reaching Test.Skip does not fire", not f))

    return results


# MiniYaml's indent arithmetic is transcribed rather than approximated, so pin it: these are
# the cases where `leading_whitespace // 4` and the engine disagree.
MINIYAML_INDENT_CASES = (
    ("\tKey: v", 1), ("        Key: v", 2), ("  Key: v", 0),
    ("\t  \tKey: v", 2), ("      Key: v", 1), ("Key: v", 0),
)


def run_selftest(args):
    api = scan_cs()
    failed = 0
    for desc, fn in SELFTEST_CASES:
        try:
            ok = bool(fn(api))
        except Exception as e:  # noqa: BLE001 - a missing key IS the failure
            ok = False
            desc = f"{desc}  ({type(e).__name__}: {e})"
        print(f"  {'ok  ' if ok else 'FAIL'}  {desc}")
        if not ok:
            failed += 1

    # End-to-end: the scanner must fire on a snippet carrying both real failures.
    import tempfile
    findings = []
    with tempfile.TemporaryDirectory() as td:
        path = os.path.join(td, "selftest.lua")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(LUA_SELFTEST)
        check_file(path, api, {}, set(), findings)
    got = {f.symbol for f in findings}
    for want, sev in (("Trigger.OnEveryTick", "error"), ("p.Location", "error"), ("Bogus", "warn"),
                      ("w.Location", "warn")):
        ok = any(f.symbol == want and f.severity == sev for f in findings)
        print(f"  {'ok  ' if ok else 'FAIL'}  scanner reports {want} as {sev}")
        if not ok:
            failed += 1
    # w.IsDead / w.Type are ungated (BaseActorProperties) — safe on the player actor,
    # so the GetActors check must NOT fire on them.
    for unwanted in ("Trigger.AfterDelay", "Trigger.OnTick", "a.Location", "string.format",
                     "Actor.Create", "Player.GetPlayer", "w.IsDead", "w.Type"):
        ok = unwanted not in got
        print(f"  {'ok  ' if ok else 'FAIL'}  scanner stays quiet about {unwanted}")
        if not ok:
            failed += 1

    # The MiniYaml indent transcription, then every inertness check on synthetic scenarios.
    extra = [(f"miniyaml indent: {line!r} is level {want}",
              miniyaml_split(line)[0] == want) for line, want in MINIYAML_INDENT_CASES]
    extra += run_wiring_acceptance()
    for desc, ok in extra:
        print(f"  {'ok  ' if ok else 'FAIL'}  {desc}")
        if not ok:
            failed += 1

    print()
    if failed:
        print(f"lua-gate selftest: FAIL — {failed} case(s).")
        return 2
    print(f"lua-gate selftest: OK — {len(SELFTEST_CASES) + 11 + len(extra)} case(s).")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.strip().split("\n")[0])
    sub = ap.add_subparsers(dest="cmd")

    c = sub.add_parser("check", help="gate the scenario Lua (default)")
    c.add_argument("--scenario", action="append", default=[],
                   help="only scenarios whose directory name contains this (repeatable)")
    c.add_argument("--strict", action="store_true",
                   help="exit 2 on warnings as well as undefined references")
    c.set_defaults(fn=run_check)

    a = sub.add_parser("api", help="dump the extracted binding surface")
    a.add_argument("--json", action="store_true")
    a.set_defaults(fn=run_api)

    s = sub.add_parser("selftest", help="assert the C# extractor and the scanner still work")
    s.set_defaults(fn=run_selftest)

    v = sub.add_parser("verify", help="diff the source parse against `utility.sh --lua-docs`")
    v.add_argument("--docs", required=True, help="path to saved --lua-docs markdown")
    v.set_defaults(fn=run_verify)

    args = ap.parse_args()
    if not args.cmd:
        args = ap.parse_args(["check"])
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main())
