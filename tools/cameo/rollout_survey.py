"""How big is the caption rollout, in numbers rather than estimates.

Answers three things for the follow-on:
  1. how many buildable actors have a cameo
  2. how many of those cameos have lettering baked into the art
  3. whether a caption string could be derived from the tooltip name, or must be authored

THE ACTOR UNIVERSE IS mod.yaml's `Rules:` LIST, NOT `os.walk` OVER THE RULES DIRECTORY, and this
tool is where that distinction was got wrong last. `mods/ww3mod/rules/` holds .yaml files mod.yaml
never loads (all of `weapons/` and `sound/`, the `campaign/` tree, `ingame/old.yaml` -- 18 of them
as of 2026-09-20, recount with captions_table.unloaded_rules_paths()), and an actor defined only
in one of those is NOT in the game. Counting it inflates every figure below and, worse, makes a
caption on it look legitimate: five such entries reached a shipped rules file on 2026-09-20 and
produced 3,650 lint errors. captions_table.py and check_captions.py moved to the loaded list that
day; this survey did not, so the repo stated 115 and 111 for the same quantity for as long as it
took to notice. It now calls captions_table.survey(), which owns both the universe and the
resolver, so the three tools cannot report different rosters. `--all-on-disk` restores the
directory walk for diagnostics and says so on stdout.

METHOD, and its limits. Reads the loaded rules files and sequences/**.yaml with a plain
MiniYaml-shaped parser, resolves `Inherits`/`Inherits@x` transitively, and treats an actor as
buildable if `Buildable:` survives resolution and is not removed by `-Buildable:`. It does NOT
evaluate prerequisites, so an actor gated to unbuildable (`Prerequisites: ~disabled`, like MSLO)
still counts as having a cameo -- which is right for this question, since the cameo exists either
way. Icon art is resolved through the `icon` sequence on the actor's own image.
"""
import argparse
import io
import os
import re
import sys
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import binmock  # noqa: E402
from PIL import ImageFont  # noqa: E402


def parse(path):
    """Top-level key -> {child key: [grandchild lines]}. Enough for Inherits/Buildable/Tooltip."""
    out, cur, sub = {}, None, None
    for raw in io.open(path, encoding="utf-8-sig"):
        line = raw.rstrip("\n")
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        indent = len(line) - len(line.lstrip("\t"))
        text = line.strip()
        if indent == 0:
            cur = text.rstrip(":")
            out.setdefault(cur, {})
            sub = None
        elif indent == 1 and cur is not None:
            sub = text.rstrip(":")
            out[cur].setdefault(sub, [])
        elif indent >= 2 and cur is not None and sub is not None:
            out[cur][sub].append(text)
    return out


def walk(root):
    for base, _, files in os.walk(root):
        for f in files:
            if f.endswith(".yaml"):
                yield os.path.join(base, f)


def merge(paths):
    merged = {}
    for p in paths:
        for k, v in parse(p).items():
            tgt = merged.setdefault(k, {})
            for tk, tv in v.items():
                tgt[tk] = tv
    return merged


def resolve(name, defs, seen=None):
    """Flatten Inherits chains into one trait dict. Later entries win; -Trait removes."""
    seen = seen or set()
    if name not in defs or name in seen:
        return {}
    seen.add(name)
    out = {}
    for key, body in defs[name].items():
        if key == "Inherits" or key.startswith("Inherits@"):
            parent = body[0] if body else None
            # `Inherits: X` has its value on the key line, which this parser puts nowhere; re-read.
            continue
        out[key] = body
    # Re-read inherit targets from the raw key lines.
    for key in defs[name]:
        if key.startswith("Inherits"):
            m = re.match(r"Inherits(?:@\w+)?:\s*(\S+)", key)
            if m:
                base = resolve(m.group(1), defs, seen)
                for k, v in base.items():
                    out.setdefault(k, v)
    for key in list(out):
        if key.startswith("-"):
            out.pop(key[1:], None)
            out.pop(key, None)
    return out


def field(body, key):
    for line in body or []:
        if line.startswith(key + ":"):
            return line.split(":", 1)[1].strip()
    return None


def art_format(art):
    """PNG / ShpTS / ShpTD / missing. Only the first two can be decoded here.

    ShpTD is the older Command & Conquer format (first word = image count); ShpTS starts with a
    zero word. Parsing one as the other yields GARBAGE rather than an exception, so the format has
    to be checked BEFORE the pixels are trusted -- an earlier cut of this survey did not, and
    called every ShpTD cameo "no baked lettering" on the strength of decoded noise.
    """
    path = os.path.join(ROOT, "mods/ww3mod/bits/misc/icons", art + ".shp")
    if not os.path.exists(path):
        return "missing"
    head = open(path, "rb").read(8)
    if head[1:4] == b"PNG":
        return "png"
    return "shpts" if head[0] == 0 and head[1] == 0 else "shptd"


def has_baked_lettering(art, pal):
    """Does this cameo carry lettering baked into its bottom rows?

    NOT a brightness threshold. The first cut of this used one and cleared seven cameos that
    plainly have captions -- precicon's lettering is pure white, but T90's and MIG's are dim grey,
    and a threshold tuned to one misses the other. This compares the 1px-pitch horizontal contrast
    of the caption rows against the picture's own, which is high for text at any brightness.

    STILL NOT EXACT: art whose picture is as busy as its lettering (t72icon: trees behind a tank)
    scores near 1 and reads as clean. So a "clean" verdict is weaker than a "lettered" one, and the
    real figure for lettered art is a floor, not a count.
    """
    try:
        im = binmock.load_cameo(art, pal)
    except Exception:
        return None
    px, (w, h) = im.load(), im.size

    def lum(x, y):
        r, g, b, _ = px[x, y]
        return (r * 299 + g * 587 + b * 114) // 1000

    def grad(rows):
        v = [abs(lum(x + 1, y) - lum(x, y)) for y in rows for x in range(1, w - 2)]
        return sum(v) / len(v) if v else 0.0

    return grad(range(h - 6, h - 1)) / max(grad(range(8, h - 12)), 1e-6) >= 1.6


def on_disk_rules_paths():
    """Every .yaml under mods/ww3mod/rules, loaded or not. Diagnostics only -- see --all-on-disk."""
    return sorted(walk(os.path.join(ROOT, "mods/ww3mod/rules")))


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="Count cameos, baked lettering and caption candidates across the roster.")
    ap.add_argument("--all-on-disk", action="store_true",
                    help="survey every .yaml under mods/ww3mod/rules instead of the files "
                         "mod.yaml loads. Counts actors that are NOT in the game.")
    args = ap.parse_args(argv)

    # ONE universe and ONE resolver for the three tools. captions_table.survey() takes its files
    # from mod.yaml's Rules: list and excludes the generated caption table, which is what makes
    # this headline reconcile with check_captions.py's exactly.
    import captions_table  # noqa: E402  -- deferred; captions_table imports this module back
    if args.all_on_disk:
        print("WARNING: --all-on-disk counts actors that are NOT in the game -- mod.yaml's "
              "Rules: list does not load every file under mods/ww3mod/rules. The figures below "
              "are an upper bound on ART work, not the roster a player sees.")
    actors = captions_table.survey(on_disk_rules_paths() if args.all_on_disk else None)

    pal = binmock.load_palette(binmock.find_palette())
    font = ImageFont.truetype(os.path.join(ROOT, "engine/mods/common/FreeSansBold.ttf"), 7)

    print(f"BUILDABLE ACTORS WITH A CAMEO: {len(actors)}")

    arts, unresolved_names = Counter(), []
    for name, info in actors.items():
        # survey() marks art it could not resolve through the icon sequence with "?".
        if info["art"] == "?":
            unresolved_names.append(name)
        else:
            arts[info["art"]] += 1
    unresolved = len(unresolved_names)

    print(f"  distinct icon art files resolved: {len(arts)}   actors whose art did not resolve: {unresolved}")
    if unresolved:
        print("    unresolved:", ", ".join(sorted(unresolved_names)[:12]), "...")

    fmt = {a: art_format(a) for a in arts}
    print("  art formats:", dict(Counter(fmt.values())))
    readable = [a for a in arts if fmt[a] in ("png", "shpts")]
    opaque = [a for a in arts if fmt[a] == "shptd"]
    gone = [a for a in arts if fmt[a] == "missing"]

    baked = {a: has_baked_lettering(a, pal) for a in readable}
    yes = [a for a, v in baked.items() if v is True]
    no = [a for a, v in baked.items() if v is False]
    print()
    print(f"  DECODABLE HERE ({len(readable)} files, {sum(arts[a] for a in readable)} actors):")
    print(f"    WITH baked lettering:    {len(yes):3} files, {sum(arts[a] for a in yes):3} actors")
    print(f"    WITHOUT baked lettering: {len(no):3} files, {sum(arts[a] for a in no):3} actors  {sorted(no)}")
    print(f"  NOT DECODABLE HERE (ShpTD): {len(opaque):3} files, {sum(arts[a] for a in opaque):3} actors -- verdict unknown")
    print(f"  NOT IN THE REPO:            {len(gone):3} files, {sum(arts[a] for a in gone):3} actors  {sorted(gone)}")

    # can the caption be derived from the tooltip name?
    fits = short = long_ = noname = 0
    examples = []
    for name, info in actors.items():
        nm = info["tooltip"]
        if not nm:
            noname += 1
            continue
        upper = nm.upper()
        wide = sum(font.getlength(c) for c in upper)
        if wide <= 60:
            fits += 1
            if len(examples) < 6:
                examples.append((nm, round(wide)))
        else:
            long_ += 1
    print(f"\nTOOLTIP NAME AS THE CAPTION (upper-cased, 60px budget, no badge):")
    print(f"  fits: {fits}   too wide: {long_}   no Tooltip Name: {noname}")
    print(f"  examples that fit: {examples}")


if __name__ == "__main__":
    main()
