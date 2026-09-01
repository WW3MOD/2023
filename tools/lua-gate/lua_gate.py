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
# Scenario discovery
# --------------------------------------------------------------------------------------

def scenario_scripts(scen_dir):
    """`Scripts:` from rules.yaml — the exact set of Lua the engine loads for this map."""
    names = []
    for fn in ("rules.yaml", "map.yaml"):
        path = os.path.join(scen_dir, fn)
        if not os.path.exists(path):
            continue
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            for line in fh:
                m = re.match(r"^\s*Scripts:\s*(.+?)\s*$", line)
                if m:
                    names.extend(x.strip() for x in m.group(1).split(",") if x.strip())
    return names


def map_actor_names(scen_dir):
    """Instance names under `Actors:` in map.yaml — MapGlobal.cs:34-36 makes each a global."""
    path = os.path.join(scen_dir, "map.yaml")
    names = set()
    if not os.path.exists(path):
        return names
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        inside = False
        for line in fh:
            if re.match(r"^Actors:", line):
                inside = True
                continue
            if inside:
                if line.strip() and not line[0].isspace():
                    inside = False
                    continue
                m = re.match(r"^\t([A-Za-z_]\w*)\s*:", line)
                if m:
                    names.add(m.group(1))
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
    out = []
    if not os.path.isdir(SCENARIOS):
        return out
    for name in sorted(os.listdir(SCENARIOS)):
        d = os.path.join(SCENARIOS, name)
        if not os.path.isdir(d):
            continue
        if filters and not any(f in name for f in filters):
            continue
        luas = sorted(f for f in os.listdir(d) if f.endswith(".lua"))
        if luas:
            out.append((name, d, luas))
    return out


def run_check(args):
    api = scan_cs()
    findings = []
    scenarios = collect_scenarios(args.scenario)
    files = 0

    helper_cache = {}

    for name, d, luas in scenarios:
        declared = scenario_scripts(d)
        actors = map_actor_names(d)

        # Globals contributed by the helper scripts this map actually loads.
        extra = {}
        for s in declared:
            if s in luas:
                continue
            path = resolve_script(s, d)
            if not path:
                findings.append(Finding(
                    "warn", os.path.relpath(os.path.join(d, "rules.yaml"), REPO), 0, s,
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
                    "warn", os.path.relpath(os.path.join(d, lua), REPO), 0, lua,
                    "this scenario declares no `Scripts:` anywhere in rules.yaml or map.yaml, "
                    "so the engine never loads this file — its WorldLoaded never runs and "
                    "nothing in it is checked here."))
            continue

        for lua in luas:
            # Only gate the scripts the map actually loads; an orphan .lua in the
            # directory is dead weight the engine never sees.
            if lua not in declared:
                findings.append(Finding(
                    "warn", os.path.relpath(os.path.join(d, lua), REPO), 0, lua,
                    "not listed in this scenario's `Scripts:`; the engine never loads it, "
                    "so it is unchecked and probably dead."))
                continue
            files += 1
            check_file(os.path.join(d, lua), api, extra, actors, findings)

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
        print(f"lua-gate: FAIL — {len(errors)} undefined reference(s), {len(warns)} warning(s).")
        return 2
    if warns:
        print(f"lua-gate: WARN — {len(warns)} warning(s).")
        return 2 if args.strict else 1
    print("lua-gate: OK — every reference resolves to a registered binding.")
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
    ("Trigger.OnTick is NOT registered", lambda a: "OnTick" not in a["globals"]["Trigger"]["members"]),
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
    for want, sev in (("Trigger.OnTick", "error"), ("p.Location", "error"), ("Bogus", "warn"),
                      ("w.Location", "warn")):
        ok = any(f.symbol == want and f.severity == sev for f in findings)
        print(f"  {'ok  ' if ok else 'FAIL'}  scanner reports {want} as {sev}")
        if not ok:
            failed += 1
    # w.IsDead / w.Type are ungated (BaseActorProperties) — safe on the player actor,
    # so the GetActors check must NOT fire on them.
    for unwanted in ("Trigger.AfterDelay", "a.Location", "string.format", "Actor.Create",
                     "Player.GetPlayer", "w.IsDead", "w.Type"):
        ok = unwanted not in got
        print(f"  {'ok  ' if ok else 'FAIL'}  scanner stays quiet about {unwanted}")
        if not ok:
            failed += 1

    print()
    if failed:
        print(f"lua-gate selftest: FAIL — {failed} case(s).")
        return 2
    print(f"lua-gate selftest: OK — {len(SELFTEST_CASES) + 11} case(s).")
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
