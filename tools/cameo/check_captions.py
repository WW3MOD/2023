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

  7. DEFINED      every entry names an actor that some file in mod.yaml's `Rules:` list actually
                   defines. An override on an actor only an UNLOADED file defines does not fail
                   loudly -- MiniYaml creates a bare actor from it, which then trips two lints on
                   every map in the mod. Added 2026-09-20, after exactly that shipped.

THE ACTOR UNIVERSE IS mod.yaml's `Rules:` LIST, not the rules directory. The two differed by 18
files on 2026-09-20 and the count moves; captions_table.unloaded_rules_paths() is the authority.
captions_table.loaded_rules_paths() resolves the loaded half and is shared with the generator AND
with rollout_survey.py, so a table entry the generator would not emit is one this gate rejects,
and all three tools report the same roster size.
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
    """Every buildable actor with a cameo, from the rules files mod.yaml LOADS, table excluded.

    Both halves matter and both used to be wrong here. This walked the rules DIRECTORY, so an
    actor defined only in a file mod.yaml never loads (ingame/old.yaml, all of weapons/ and
    sound/ and campaign/) counted as real and a table entry on it
    looked legitimate -- check 7 below is the check that state needed. captions_table.survey()
    now owns both the universe and the exclusion, so the generator and this gate cannot drift.
    """
    return captions_table.survey()


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

    # 5 -- case. parse_table keys come from the file; the authority is every top-level key the
    # LOADED rules files define -- not just the buildable ones, so a mis-cased key is still caught
    # when the actor it meant to hit is not itself in the survey.
    defined = captions_table.defined_in_loaded_rules()
    by_lower = {a.lower(): a for a in defined}
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

    # 7 -- DEFINED. Every entry must name an actor some LOADED rules file defines. An entry that
    # does not is not inert and is not a no-op: MiniYaml creates a bare top-level actor carrying
    # only this Buildable, which then fails `does not define a default visibility type` and
    # `has no (enabled) Tooltip` on EVERY map. On 2026-09-20 five such entries produced 3,650
    # lint errors -- ten messages across 365 maps -- and nothing before the full gate saw them.
    undefined = sorted(set(table) - defined)
    if undefined:
        elsewhere = {}
        for path in captions_table.unloaded_rules_paths():
            keys = captions_table.top_level_keys(path)
            rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
            for a in undefined:
                if a in keys:
                    elsewhere.setdefault(a, rel)
        for a in undefined:
            where = elsewhere.get(a)
            fails.append(
                "%s: no loaded rules file defines this actor, so the entry CREATES a bare actor "
                "and breaks every map's lint -- %s"
                % (a, ("it is defined in %s, which mod.yaml's Rules: list does not load" % where)
                   if where else "nothing under mods/ww3mod/rules defines it at all"))

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
