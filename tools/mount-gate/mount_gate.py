#!/usr/bin/env python3
"""Static check that every non-optional mount in mod.yaml resolves inside a PACKAGED build.

v0.1.0 shipped unable to reach the main menu: `MapFolders` carried
`^EngineDir|../tools/autotest/scenarios`, a development-checkout path that no installer
ships, and `MapCache.LoadMaps` rethrows the missing-package exception for a non-optional
entry (engine/OpenRA.Game/Map/MapCache.cs:113-119). Every gate we run executes against the
git checkout, where `tools/` exists -- so none of them could see it.

This gate models the PACKAGED filesystem instead of the checkout. See README.md, in
particular "What this does NOT check".

Exit codes:
    0   every non-optional mount resolves (warnings may still have printed)
    2   at least one non-optional mount does not resolve, or a usage error
    3   the tree/zip given could not be read
"""

import argparse
import os
import posixpath
import sys
import zipfile
from collections import namedtuple

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
FIXTURES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")


# --------------------------------------------------------------------------------------
# MiniYaml line splitting
#
# Transcribed from MiniYaml.cs:239-263 rather than approximated: a tab is one level, four
# spaces are one level, and a leftover run of 1-3 spaces is discarded without contributing
# anything -- a tab does not reset the space counter. mod.yaml mixes both (most sections
# are tab-indented, `Voices:` and `Music:` are space-indented), so `leading // 4` would
# misread the file. Same transcription as tools/lua-gate/lua_gate.py:462-506.
# --------------------------------------------------------------------------------------

def miniyaml_split(line):
    """Reproduce MiniYaml.FromLines' level/key/value split for one line."""
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


def miniyaml_nodes(text):
    """(level, key, value, lineno) for every line the engine would keep."""
    out = []
    for lineno, raw in enumerate(text.splitlines(), 1):
        level, key, value = miniyaml_split(raw.rstrip("\r"))
        if key:
            out.append((level, key, value, lineno))
    return out


def section_children(nodes, path):
    """Child (key, value, lineno) tuples of the node reached by following `path` of keys."""
    depth, start, end = 0, 0, len(nodes)
    for want in path:
        found = None
        for i in range(start, end):
            level, key = nodes[i][0], nodes[i][1]
            if level < depth:
                break
            if level == depth and key == want:
                found = i
                break
        if found is None:
            return []
        start, depth = found + 1, depth + 1
        end = len(nodes)
        for i in range(start, len(nodes)):
            if nodes[i][0] < depth:
                end = i
                break

    return [(k, v, ln) for (lvl, k, v, ln) in nodes[start:end] if lvl == depth]


# --------------------------------------------------------------------------------------
# Trees: the thing a mount is resolved against
# --------------------------------------------------------------------------------------

class Tree:
    """A packaged root -- the directory that IS Platform.EngineDir at runtime.

    In a packaged build nothing passes Engine.EngineDir, so Platform.EngineDir falls back
    to Platform.BinDir (Platform.cs:247-260) and both name the directory holding `mods/`.
    That is the whole difference from the dev checkout, where launch-game.sh passes
    Engine.EngineDir=".." and EngineDir becomes engine/ -- which is why
    `^EngineDir|../tools` resolves to the repo root there and to nothing in an installer.
    """

    synthetic = False
    label = "<tree>"

    def exists(self, rel):
        raise NotImplementedError

    def isdir(self, rel):
        raise NotImplementedError

    def read(self, rel):
        raise NotImplementedError


class DirTree(Tree):
    def __init__(self, root):
        self.root = os.path.abspath(root)
        self.label = self.root

    def _abs(self, rel):
        return os.path.join(self.root, rel.replace("/", os.sep))

    def exists(self, rel):
        return os.path.exists(self._abs(rel))

    def isdir(self, rel):
        return os.path.isdir(self._abs(rel))

    def read(self, rel):
        try:
            with open(self._abs(rel), "r", encoding="utf-8", errors="replace") as fh:
                return fh.read()
        except OSError:
            return None


class ZipTree(Tree):
    def __init__(self, path, prefix):
        self.zf = zipfile.ZipFile(path)
        self.prefix = prefix
        self.label = path + "!" + (prefix or "/")
        self.files, self.dirs = set(), set()
        for name in self.zf.namelist():
            if prefix:
                if not name.startswith(prefix):
                    continue
                name = name[len(prefix):]
            trimmed = name.rstrip("/")
            if not trimmed:
                continue
            if name.endswith("/"):
                self.dirs.add(trimmed)
            else:
                self.files.add(trimmed)
            parts = trimmed.split("/")
            for i in range(1, len(parts)):
                self.dirs.add("/".join(parts[:i]))

    def exists(self, rel):
        # "" is the packaged root itself -- what a bare `^EngineDir` mounts. A zip holds no
        # entry for its own root, so it has to be answered here or every manifest that
        # mounts the engine dir reads as broken.
        return rel == "" or rel in self.files or rel in self.dirs

    def isdir(self, rel):
        return rel == "" or rel in self.dirs

    def read(self, rel):
        try:
            return self.zf.read(self.prefix + rel).decode("utf-8", "replace")
        except KeyError:
            return None


class PredictedTree(Tree):
    """A model of the packaged root built from the packaging scripts, with no build.

    Every entry below is a copy rule read out of a packaging script, cited. This is a
    MODEL: it is right only for as long as those scripts say what they say today. Prefer
    --tree against a real packaged output when you have one; this form exists so the gate
    can run in `make test` on a checkout, which is where the mistakes are actually made.
    """

    synthetic = True

    def __init__(self, repo, mod_id):
        self.repo = repo
        self.mod_id = mod_id
        self.label = "predicted packaged root (modelled from mod.config + packaging scripts)"

        # engine/packaging/functions.sh:65-74 -- install_data, called by every SDK
        # buildpackage.sh with no mod ids, so only mods/common comes across from there.
        self.map = {
            "VERSION": "engine/VERSION",
            "AUTHORS": "engine/AUTHORS",
            "COPYING": "engine/COPYING",
            "glsl": "engine/glsl",
            "lua": "engine/lua",
            "mods/common": "engine/mods/common",
        }

        # mod.config PACKAGING_COPY_ENGINE_FILES, copied at
        # packaging/linux/buildpackage.sh:74-77 (same loop in macOS/Windows).
        for f in self._copy_engine_files():
            rel = f.lstrip("./")
            self.map[rel] = "engine/" + rel

        # packaging/linux/buildpackage.sh:82 -- `cp -Lr mods/* <root>/mods`
        mods_dir = os.path.join(repo, "mods")
        if os.path.isdir(mods_dir):
            for name in os.listdir(mods_dir):
                self.map["mods/" + name] = "mods/" + name

    def _copy_engine_files(self):
        path = os.path.join(self.repo, "mod.config")
        try:
            with open(path, "r", encoding="utf-8", errors="replace") as fh:
                for line in fh:
                    if line.startswith("PACKAGING_COPY_ENGINE_FILES="):
                        return line.split("=", 1)[1].strip().strip('"').split()
        except OSError:
            pass
        return []

    def _source(self, rel):
        if rel in self.map:
            return os.path.join(self.repo, self.map[rel].replace("/", os.sep))
        for key, src in self.map.items():
            if rel.startswith(key + "/"):
                tail = rel[len(key) + 1:]
                return os.path.join(self.repo, src.replace("/", os.sep),
                                    tail.replace("/", os.sep))
        return None

    def exists(self, rel):
        if rel in ("", "mods"):
            return True
        src = self._source(rel)
        return src is not None and os.path.exists(src)

    def isdir(self, rel):
        if rel in ("", "mods"):
            return True
        src = self._source(rel)
        return src is not None and os.path.isdir(src)

    def read(self, rel):
        src = self._source(rel)
        if src is None:
            return None
        try:
            with open(src, "r", encoding="utf-8", errors="replace") as fh:
                return fh.read()
        except OSError:
            return None


# --------------------------------------------------------------------------------------
# Mount resolution -- the prefix rules, transcribed
# --------------------------------------------------------------------------------------

Finding = namedtuple("Finding", "severity code section entry line detail")

# Platform.ResolvePath (Platform.cs:300-322) knows exactly three caret names. Anything
# else starting with '^' is left in the string verbatim and then fails to open as a
# relative path -- silently, for an optional entry.
CARET_NAMES = ("SupportDir", "EngineDir", "BinDir")


class Resolver:
    def __init__(self, tree, mod_id):
        self.tree = tree
        self.mod_id = mod_id
        # explicit mount name -> (root-relative path or None, optional, kind)
        self.mounts = {}

    def note_mount(self, name, rel, optional, kind):
        if name:
            self.mounts[name] = (rel, optional, kind)

    def resolve(self, spec):
        """(kind, rel, note) for a mount spec with its `~` already stripped.

        kind is one of:
          root       -- resolves to `rel` inside the packaged root
          outside    -- normalises above the packaged root; unshippable
          supportdir -- a user-machine path; never present in a packaged output
          mod        -- a `$id` mod reference; `rel` is mods/<id>
          via        -- through an explicit mount; `rel` is the joined path, or None when
                        the parent mount is not itself resolvable in the tree
          unknown    -- an explicit-mount prefix nobody declared, or a stray `^Name`
        """
        # FileSystem.cs:93-102 -- `$id` is looked up in InstalledMods, not on disk. The
        # search paths are EngineDir/mods and EngineDir/../mods (Game.cs:399-401).
        if spec.startswith("$"):
            return "mod", "mods/" + spec[1:], spec[1:]

        if spec.startswith("^"):
            head = spec[1:].split("|", 1)[0]
            if head not in CARET_NAMES:
                return "unknown", None, \
                    "^" + head + " is not one of ^SupportDir / ^EngineDir / ^BinDir"
            if head == "SupportDir":
                return "supportdir", None, None
            # ^EngineDir and ^BinDir are the same directory in a packaged build.
            tail = spec.split("|", 1)[1] if "|" in spec else ""
            rel = posixpath.normpath(tail.strip("/")) if tail.strip("/") else ""
            if rel in (".", ""):
                return "root", "", None
            if rel == ".." or rel.startswith("../"):
                return "outside", rel, None
            return "root", rel, None

        if "|" in spec:
            prefix, tail = spec.split("|", 1)
            if prefix not in self.mounts:
                return "unknown", None, \
                    "no mount is declared with the explicit name '" + prefix + "'"
            parent_rel = self.mounts[prefix][0]
            if parent_rel is None:
                return "via", None, prefix
            joined = posixpath.normpath(posixpath.join(parent_rel, tail.strip("/")))
            if joined == ".." or joined.startswith("../"):
                return "outside", joined, prefix
            return "via", joined, prefix

        # A bare relative path. Platform.ResolvePath leaves it untouched, so at runtime it
        # is tried against the process working directory and then against every already
        # mounted package (FileSystem.cs:62-81, :244-251). The packaged working directory
        # is the packaged root (AppRun cds there), so that is what we check it against.
        rel = posixpath.normpath(spec.strip("/"))
        if rel == ".." or rel.startswith("../"):
            return "outside", rel, None
        return "root", rel, None


def check_manifest(text, source_label, tree, mod_id):
    nodes = miniyaml_nodes(text)
    resolver = Resolver(tree, mod_id)
    findings = []

    def add(sev, code, section, entry, line, detail):
        findings.append(Finding(sev, code, section, entry, line, detail))

    packages = section_children(nodes, ["FileSystem", "Packages"])
    mapfolders = section_children(nodes, ["MapFolders"])

    if not packages:
        add("error", "no-packages", "FileSystem", "", 0,
            source_label + " declares no FileSystem/Packages block; nothing to check")
        return findings, resolver

    for section, entries, is_mapfolder in (("Packages", packages, False),
                                           ("MapFolders", mapfolders, True)):
        for raw, value, line in entries:
            # FileSystem.cs:86-88 and MapCache.cs:101-103 -- identical `~` handling.
            optional = raw.startswith("~")
            spec = raw[1:] if optional else raw
            explicit = None if is_mapfolder else (value or None)

            kind, rel, note = resolver.resolve(spec)

            if not is_mapfolder:
                # Register the explicit name before judging the entry, so ordering matches
                # the engine's: Packages is walked in file order and a later `name|sub`
                # can only see mounts declared above it.
                resolver.note_mount(
                    explicit, rel if kind in ("root", "via", "mod") else None, optional, kind)

            if optional:
                # FileSystem.cs:112-114 swallows every exception; MapCache.cs:114-119
                # `continue`s. An absent optional mount is not a defect.
                if kind in ("root", "via", "mod") and rel is not None and not tree.exists(rel):
                    add("info", "optional-absent", section, raw, line,
                        "optional, resolves to '" + rel + "' which the package lacks")
                continue

            if kind == "outside":
                add("error", "escapes-package", section, raw, line,
                    "resolves to '" + rel + "', ABOVE the packaged root. Nothing above "
                    "that root is shipped by any installer, so this can never resolve on a "
                    "real install regardless of what exists on the build machine. This is "
                    "the v0.1.0 shape. Prefix it with `~` or delete it.")
            elif kind == "supportdir":
                if is_mapfolder:
                    # MapCache.cs:105-108 creates a missing support-dir path before opening
                    # it, so a non-optional support-dir MapFolder survives a fresh install.
                    add("info", "supportdir-mapfolder", section, raw, line,
                        "support-dir path; MapCache creates it (MapCache.cs:105-108)")
                else:
                    add("error", "supportdir-required", section, raw, line,
                        "non-optional ^SupportDir package. FileSystem.Mount has no "
                        "create-if-missing hack (unlike MapCache), so this throws on any "
                        "machine that has not installed content yet. Prefix it with `~`.")
            elif kind == "unknown":
                add("error", "unresolvable", section, raw, line, note)
            elif kind == "via":
                # Two ways a `name|sub` entry dies: the mount `name` is itself not in the
                # package, or it is but `sub` under it is not. The first is the more
                # interesting one -- an optional parent that no installer ships makes every
                # non-optional child unreachable, and the child's own line says nothing
                # about it.
                parent_rel, parent_optional, _ = resolver.mounts.get(note, (None, False, None))
                parent_present = parent_rel is not None and tree.exists(parent_rel)
                if not parent_present and parent_optional:
                    add("error", "parent-optional", section, raw, line,
                        "non-optional, but its explicit mount '" + note + "' is declared "
                        "optional and is absent from the package, so this cannot resolve")
                elif not parent_present:
                    add("warn", "parent-unresolvable", section, raw, line,
                        "explicit mount '" + note + "' could not be located in the tree")
                elif not tree.exists(rel):
                    add("error", "missing", section, raw, line,
                        "resolves to '" + rel + "' through mount '" + note + "', which the "
                        "package lacks")
            elif kind == "mod":
                manifest = posixpath.join(rel, "mod.yaml")
                if not tree.exists(manifest):
                    add("error", "mod-missing", section, raw, line,
                        "mod '" + note + "' is not in the package: no '" + manifest + "'. "
                        "FileSystem.Mount throws \"Could not load mod '" + note + "'\" "
                        "(FileSystem.cs:97-98). Add it to PACKAGING_COPY_ENGINE_FILES "
                        "or to mods/.")
            elif kind == "root":
                if not tree.exists(rel):
                    add("error", "missing", section, raw, line,
                        "resolves to '" + (rel or ".") + "', which the package lacks")

    return findings, resolver


# --------------------------------------------------------------------------------------
# Tree discovery
# --------------------------------------------------------------------------------------

def find_dir_root(start, mod_id):
    """Locate the packaged root under `start`: the dir holding mods/<mod_id>/mod.yaml."""
    start = os.path.abspath(start)
    candidates = [start]
    # An extracted AppImage puts it three levels down; a macOS .app puts it under
    # Contents/Resources; a portable zip puts it at the top.
    for extra in ("usr/lib/openra", "squashfs-root/usr/lib/openra", "Contents/Resources"):
        candidates.append(os.path.join(start, extra.replace("/", os.sep)))
    for c in candidates:
        if os.path.isfile(os.path.join(c, "mods", mod_id, "mod.yaml")):
            return c
    return None


def find_zip_prefix(path, mod_id):
    want = "mods/" + mod_id + "/mod.yaml"
    with zipfile.ZipFile(path) as zf:
        for name in zf.namelist():
            if name.endswith(want):
                return name[:len(name) - len(want)]
    return None


# --------------------------------------------------------------------------------------
# Commands
# --------------------------------------------------------------------------------------

SEV_ORDER = {"error": 0, "warn": 1, "info": 2}


def report(findings, tree, source_label, verbose):
    shown = [f for f in findings if verbose or f.severity != "info"]
    shown.sort(key=lambda f: (SEV_ORDER[f.severity], f.line))
    errors = [f for f in findings if f.severity == "error"]
    warns = [f for f in findings if f.severity == "warn"]

    print("mount-gate: manifest " + source_label)
    print("mount-gate: tree     " + tree.label)
    if tree.synthetic:
        print("mount-gate: NOTE -- this tree is PREDICTED from the packaging scripts, not "
              "a real packaged output. Re-run with --tree against a built artifact to be "
              "sure.")
    print()

    for f in shown:
        tag = {"error": "ERROR", "warn": "warn ", "info": "info "}[f.severity]
        print("  %s  %s:%d  %s" % (tag, f.section, f.line, f.entry))
        print("         %s: %s" % (f.code, f.detail))

    if shown:
        print()

    if errors:
        print("mount-gate: FAIL -- %d non-optional mount(s) do not resolve inside the "
              "packaged output." % len(errors))
        return 2

    suffix = " (%d warning(s))" % len(warns) if warns else ""
    print("mount-gate: OK -- every non-optional mount resolves inside the packaged "
          "output" + suffix + ".")
    return 0


def read_mod_id(repo):
    try:
        with open(os.path.join(repo, "mod.config"), "r", encoding="utf-8") as fh:
            for line in fh:
                if line.startswith("MOD_ID="):
                    return line.split("=", 1)[1].strip().strip('"')
    except OSError:
        pass
    return "ww3mod"


def build_tree(args, mod_id):
    if args.zip:
        if not os.path.isfile(args.zip):
            print("mount-gate: no such file: " + args.zip, file=sys.stderr)
            return None
        prefix = find_zip_prefix(args.zip, mod_id)
        if prefix is None:
            print("mount-gate: %s contains no mods/%s/mod.yaml" % (args.zip, mod_id),
                  file=sys.stderr)
            return None
        return ZipTree(args.zip, prefix)

    if args.tree:
        root = find_dir_root(args.tree, mod_id)
        if root is None:
            print("mount-gate: no mods/%s/mod.yaml under %s (looked there, and in "
                  "usr/lib/openra, squashfs-root/usr/lib/openra, Contents/Resources)"
                  % (mod_id, args.tree), file=sys.stderr)
            return None
        return DirTree(root)

    return PredictedTree(REPO, mod_id)


def run_check(args):
    mod_id = args.mod_id or read_mod_id(REPO)
    tree = build_tree(args, mod_id)
    if tree is None:
        return 3

    if args.mod_yaml:
        try:
            with open(args.mod_yaml, "r", encoding="utf-8", errors="replace") as fh:
                text = fh.read()
        except OSError as e:
            print("mount-gate: cannot read %s: %s" % (args.mod_yaml, e), file=sys.stderr)
            return 3
        label = args.mod_yaml
    else:
        rel = "mods/" + mod_id + "/mod.yaml"
        text = tree.read(rel)
        if text is None:
            print("mount-gate: cannot read %s from %s" % (rel, tree.label), file=sys.stderr)
            return 3
        label = os.path.join(REPO, rel) if tree.synthetic else tree.label + "/" + rel

    findings, _ = check_manifest(text, label, tree, mod_id)
    return report(findings, tree, label, args.verbose)


def run_selftest(args):
    """Assert the gate catches the v0.1.0 shape and stays quiet on its fixed twin.

    Both run against tools/mount-gate/fixtures/packaged-root, a SYNTHETIC packaged tree:
    the directories an installer really ships, and no `tools/`.
    """
    root = os.path.join(FIXTURES, "packaged-root")
    broken = os.path.join(FIXTURES, "mod-v010-broken.yaml")
    fixed = os.path.join(FIXTURES, "mod-v010-fixed.yaml")
    failed = 0

    def case(desc, ok):
        nonlocal failed
        print("  %s  %s" % ("ok  " if ok else "FAIL", desc))
        if not ok:
            failed += 1

    if not os.path.isdir(root):
        print("mount-gate selftest: FAIL -- fixture tree missing: " + root)
        return 2

    tree = DirTree(root)

    with open(broken, "r", encoding="utf-8") as fh:
        bad_text = fh.read()
    with open(fixed, "r", encoding="utf-8") as fh:
        good_text = fh.read()

    bad, _ = check_manifest(bad_text, broken, tree, "ww3mod")
    good, _ = check_manifest(good_text, fixed, tree, "ww3mod")

    bad_codes = set(f.code for f in bad if f.severity == "error")
    good_errors = [f for f in good if f.severity == "error"]

    case("the v0.1.0 manifest is rejected", bool(bad_codes))
    case("...and the reason given is escapes-package", "escapes-package" in bad_codes)
    case("...naming the scenarios MapFolders entry",
         any("autotest/scenarios" in f.entry for f in bad if f.code == "escapes-package"))
    case("the same manifest with the `~` restored passes", not good_errors)

    # The delta between the two fixtures must be exactly the tilde, or the test proves
    # nothing about the tilde.
    bad_lines, good_lines = bad_text.splitlines(), good_text.splitlines()
    delta = [(a, b) for a, b in zip(bad_lines, good_lines) if a != b]
    case("the two fixtures differ on exactly one line",
         len(delta) == 1 and len(bad_lines) == len(good_lines))
    case("...and that line differs only by the `~`",
         len(delta) == 1 and delta[0][1].replace("~", "", 1) == delta[0][0])

    # Prefix semantics, exercised directly.
    r = Resolver(tree, "ww3mod")
    r.note_mount("ww3mod", "mods/ww3mod", False, "mod")
    r.note_mount("lores", None, True, "root")
    for spec, want_kind, want_rel in (
            ("^EngineDir", "root", ""),
            ("^EngineDir|mods/common", "root", "mods/common"),
            ("^BinDir|mods/common", "root", "mods/common"),
            ("^EngineDir|../tools/autotest/scenarios", "outside", "../tools/autotest/scenarios"),
            ("^SupportDir|Content/ra/v2/", "supportdir", None),
            ("^SupportDir", "supportdir", None),
            ("$ra", "mod", "mods/ra"),
            ("ww3mod|bits/units/", "via", "mods/ww3mod/bits/units"),
            ("lores|anything", "via", None),
            ("nosuchmount|x", "unknown", None),
            ("^NotAKnownCaret|x", "unknown", None),
            ("main.mix", "root", "main.mix"),
    ):
        kind, rel, _ = r.resolve(spec)
        case("resolve(%r) -> %s/%r" % (spec, want_kind, want_rel),
             kind == want_kind and rel == want_rel)

    # MiniYaml indentation, the thing that decides whether we see the entries at all.
    for line, want in (("\tPackages:", 1), ("    Packages:", 1), ("\t\t~main.mix", 2),
                       ("\t  \tx:", 2), ("        x:", 2)):
        case("miniyaml indent %r is level %d" % (line, want), miniyaml_split(line)[0] == want)

    # A non-optional entry behind an optional mount must be an error, not a warning.
    synth = ("FileSystem: DefaultFileSystem\n"
             "\tPackages:\n"
             "\t\t~lores.mix: lores\n"
             "\t\tlores|thing\n"
             "MapFolders:\n")
    f2, _ = check_manifest(synth, "<synthetic>", tree, "ww3mod")
    case("a required mount behind an optional one is an error",
         any(f.code == "parent-optional" and f.severity == "error" for f in f2))

    print()
    if failed:
        print("mount-gate selftest: FAIL -- %d case(s)." % failed)
        return 2
    print("mount-gate selftest: OK.")
    return 0


def run_explain(args):
    """Print how each mount resolves, without judging it."""
    mod_id = args.mod_id or read_mod_id(REPO)
    tree = build_tree(args, mod_id)
    if tree is None:
        return 3
    text = tree.read("mods/" + mod_id + "/mod.yaml")
    if text is None:
        return 3
    nodes = miniyaml_nodes(text)
    resolver = Resolver(tree, mod_id)
    print("tree: " + tree.label)
    for section, path, is_mf in (("Packages", ["FileSystem", "Packages"], False),
                                 ("MapFolders", ["MapFolders"], True)):
        print("[" + section + "]")
        for raw, value, line in section_children(nodes, path):
            optional = raw.startswith("~")
            spec = raw[1:] if optional else raw
            kind, rel, _ = resolver.resolve(spec)
            if not is_mf:
                resolver.note_mount(value or None,
                                    rel if kind in ("root", "via", "mod") else None,
                                    optional, kind)
            present = tree.exists(rel) if rel is not None else None
            mark = {True: "present", False: "ABSENT", None: "n/a"}[present]
            print("  %s%-10s %-7s %s%s" % ("opt " if optional else "REQ ", kind, mark, raw,
                                           ("  ->  " + rel) if rel is not None else ""))
        print()
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.strip().split("\n")[0])
    sub = ap.add_subparsers(dest="cmd")

    def common(p):
        p.add_argument("--tree", help="a packaged output directory (the dir holding mods/), "
                                      "an extracted AppImage, or a macOS .app")
        p.add_argument("--zip", help="a portable zip to check instead of a directory")
        p.add_argument("--mod-id", help="mod id to look for (default: mod.config MOD_ID)")
        p.add_argument("--mod-yaml", help="read this manifest instead of the one in the "
                                          "tree; used by the fixtures")
        p.add_argument("-v", "--verbose", action="store_true",
                       help="also list optional mounts that are absent")

    c = sub.add_parser("check", help="gate the mounts (default)")
    common(c)
    c.set_defaults(fn=run_check)

    e = sub.add_parser("explain", help="print how every mount resolves, without judging")
    common(e)
    e.set_defaults(fn=run_explain)

    s = sub.add_parser("selftest", help="assert the gate still catches the v0.1.0 shape")
    s.set_defaults(fn=run_selftest)

    args = ap.parse_args()
    if not args.cmd:
        args = ap.parse_args(["check"])
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main())
