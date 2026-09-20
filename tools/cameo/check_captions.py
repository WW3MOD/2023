#!/usr/bin/env python3
"""Gate the cameo caption table: complete, non-conflicting, renderable, and loaded last.

    python tools/cameo/check_captions.py        # exit 0 / 1, prints every failure

Six checks, each of which is a mistake somebody could actually make here:

  1. COVERAGE      every buildable actor with a cameo is captioned exactly once -- either by
                   rules/cameo-captions.yaml or by a live CameoCaption elsewhere, never neither
                   and never both. "Never both" matters as much as "never neither": two files
                   setting the same key is a silent load-order coin toss.
  2. NO GHOSTS     no entry names an actor that is not a buildable actor with a cameo. An
                   override on a name nothing defines is not an error to MiniYaml -- it just
                   creates the actor, or does nothing, depending on what else loaded.
  3. FITS          every caption measures within the slot budget IN THE SHIPPED FONT, measured
                   through the engine's own freetype6. A caption one pixel over is not clipped;
                   CameoCaptionCache shortens it from the right, silently.
  4. RENDERABLE    every character has a glyph. WW3Caption's .notdef is a solid block precisely
                   so this is loud in game, but catching it here is better.
  5. CASE          every key matches the defining actor's case exactly. MiniYaml merges
                   top-level keys case-sensitively and lower-cases afterwards, so `t90:` against
                   a defining `T90:` overrides nothing and reports nothing.
  6. LOADED        rules/cameo-captions.yaml IS listed in mod.yaml's Rules, as the LAST entry.
                   Until 2026-09-20 this check asserted the opposite (the table was a proposal);
                   the user ruled it live, so a missing or non-final entry is now the mistake.

WHAT THIS CANNOT CHECK. It reads the rules DIRECTORY, not mod.yaml's Rules list, so "already has
a live caption" means "some file under mods/ww3mod/rules sets one". The table's own file is
excluded from that scan by name -- without which every actor would look already-captioned by
itself and check 1 would pass vacuously.
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import captions_table  # noqa: E402
import pixelfont       # noqa: E402

TABLE = os.path.join(ROOT, "mods", "ww3mod", "rules", "cameo-captions.yaml")
TABLE_REF = "ww3mod|rules/cameo-captions.yaml"
MOD_YAML = os.path.join(ROOT, "mods", "ww3mod", "mod.yaml")


def survey_excluding_table():
    """captions_table.survey(), but blind to the table file itself.

    Done by moving the file aside in memory: rollout_survey.merge walks the directory, so the
    only way to exclude one file is to filter the walk. Reimplemented here rather than
    parameterised into rollout_survey, which other tools depend on.
    """
    import rollout_survey as rs
    rules_dir = os.path.join(ROOT, "mods/ww3mod/rules")
    paths = [p for p in rs.walk(rules_dir) if os.path.abspath(p) != os.path.abspath(TABLE)]
    rules = rs.merge(paths)
    seqs = rs.merge(rs.walk(os.path.join(ROOT, "mods/ww3mod/sequences")))
    low = {k.lower(): v for k, v in seqs.items()}

    def seq_art(image, icon):
        node, guard = low.get(image.lower()), 0
        while node is not None and guard < 8:
            guard += 1
            for key in node:
                m = re.match(rf"{re.escape(icon)}:\s*(\S+)", key)
                if m:
                    return m.group(1)
            parent = next((re.match(r"Inherits(?:@\w+)?:\s*(\S+)", k).group(1)
                           for k in node if re.match(r"Inherits(?:@\w+)?:\s*(\S+)", k)), None)
            node = low.get(parent.lower()) if parent else None
        return None

    out = {}
    for name in rules:
        if name.startswith("^") or name in ("Player", "World", "Defaults"):
            continue
        t = rs.resolve(name, rules)
        if "Buildable" not in t:
            continue
        icon = (rs.field(t.get("Buildable"), "Icon") or "icon").split()[0]
        image = rs.field(t.get("RenderSprites"), "Image") or name
        art = (seq_art(image, icon) or seq_art(name, icon) or "?").split("|")[-1]
        out[name] = dict(art=art, live=rs.field(t.get("Buildable"), "CameoCaption"))
    return out


def parse_table(path=TABLE):
    """{actor: caption} straight out of the generated YAML -- the file, not the Python table.

    Reading the artefact rather than CAPTIONS is the point: a generator bug that drops entries
    would be invisible if this checked the generator's own dictionary.
    """
    out, actor, in_buildable = {}, None, False
    for raw in open(path, encoding="utf-8-sig"):
        line = raw.rstrip("\n")
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        indent = len(line) - len(line.lstrip("\t"))
        t = line.strip()
        if indent == 0:
            actor, in_buildable = t.rstrip(":"), False
        elif indent == 1:
            in_buildable = t.rstrip(":") == "Buildable"
        elif indent == 2 and in_buildable and t.startswith("CameoCaption:"):
            out[actor] = t.split(":", 1)[1].strip()
    return out


def main():
    fails, notes = [], []
    actors = survey_excluding_table()
    table = parse_table()
    live = {a for a, v in actors.items() if v["live"]}

    # 1 + 2
    both = sorted(set(table) & live)
    neither = sorted(set(actors) - set(table) - live)
    ghosts = sorted(set(table) - set(actors))
    if both:
        fails.append("captioned TWICE (table and a live rules file): %s" % ", ".join(both))
    if neither:
        fails.append("no caption anywhere: %s" % ", ".join(neither))
    if ghosts:
        fails.append("entry names something that is not a buildable actor with a cameo: %s"
                     % ", ".join(ghosts))
    notes.append("%d buildable actors with a cameo = %d in the table + %d already live"
                 % (len(actors), len(table), len(live)))

    # 5 -- case. parse_table keys come from the file; actors keys come from the tree.
    by_lower = {a.lower(): a for a in actors}
    wrong_case = sorted("%s (defined as %s)" % (a, by_lower[a.lower()])
                        for a in table if a.lower() in by_lower and by_lower[a.lower()] != a)
    if wrong_case:
        fails.append("key case does not match the defining actor, so the override is inert: %s"
                     % ", ".join(wrong_case))

    # 3 + 4 -- measured in the shipped font, through the engine's own FreeType where available.
    try:
        import ftprobe
        ft = ftprobe.FreeType(pixelfont.TTF)
        width = lambda s: sum(ft.glyph(c, pixelfont.PPEM)[2] for c in s)   # noqa: E731
        notes.append("widths measured through the engine's freetype6")
    except Exception as e:                                       # noqa: BLE001
        f = pixelfont.pil_font(pixelfont.TTF)
        width = lambda s: int(sum(f.getlength(c) for c in s))     # noqa: E731
        notes.append("engine freetype6 unavailable (%s); widths measured through Pillow" % e)

    badged = {a for a, v in actors.items() if False}   # no table entry takes a badge today
    for actor, caption in sorted(table.items()):
        # GLYPHS is keyed on the capitals; the font also maps a-z onto them, so a lowercase
        # letter IS renderable and must not be reported as missing. (It has its own check below.)
        missing = sorted({c for c in caption if c not in pixelfont.GLYPHS
                          and c.upper() not in pixelfont.GLYPHS})
        if missing:
            fails.append("%s: caption %r has no glyph for %r -- would draw the .notdef block"
                         % (actor, caption, missing))
            continue
        if any(c in "ij" for c in caption):
            fails.append("%s: caption %r contains a lowercase i or j, which some FreeType builds "
                         "hold a pixel high. Use the capital." % (actor, caption))
        budget = pixelfont.BUDGET_BADGED if actor in badged else pixelfont.BUDGET_PLAIN
        w = width(caption)
        if w > budget:
            fails.append("%s: caption %r is %dpx, budget %dpx -- would be shortened from the right"
                         % (actor, caption, w, budget))

    widest = max(table.items(), key=lambda kv: width(kv[1]))
    notes.append("widest caption: %r on %s at %dpx of %dpx"
                 % (widest[1], widest[0], width(widest[1]), pixelfont.BUDGET_PLAIN))

    # 6 -- loaded, and loaded LAST so its captions win over anything an earlier file sets.
    mod_text = open(MOD_YAML, encoding="utf-8-sig").read()
    mod_lines = mod_text.splitlines()
    start = mod_lines.index("Rules:") + 1 if "Rules:" in mod_lines else len(mod_lines)
    rules_entries = []
    for ln in mod_lines[start:]:
        if not ln[:1].isspace():
            break          # first non-indented line ends the Rules: block
        if ln.strip():
            rules_entries.append(ln.strip())
    if TABLE_REF not in rules_entries:
        fails.append("mod.yaml does not list %s under Rules, so the table is INERT. It was ruled live "
                     "on 2026-09-20: list it as the last Rules: entry, or revert that ruling here." % TABLE_REF)
    elif rules_entries[-1] != TABLE_REF:
        fails.append("mod.yaml lists %s but not LAST under Rules (last is %s); a later file could "
                     "silently override a caption." % (TABLE_REF, rules_entries[-1]))
    else:
        notes.append("LOADED: mod.yaml lists %s as the last Rules: entry" % TABLE_REF)

    for n in notes:
        print("  note:", n)
    for f in fails:
        print("  FAIL:", f)
    print("RESULT:", "PASS" if not fails else "FAIL (%d)" % len(fails))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
