#!/usr/bin/env python3
"""The authored cameo caption table, and the generator that writes it to YAML.

    python tools/cameo/captions_table.py          # rewrite mods/ww3mod/rules/cameo-captions.yaml

THE TABLE IS HERE AND THE YAML IS GENERATED, not the other way round, for one reason: actor names
in a rules override must match the DEFINING case exactly. Top-level MiniYaml keys merge
case-sensitively and the lower-casing happens afterwards, so a `t90:` written against a defining
`t90:` works and against a defining `T90:` silently overrides nothing -- and this roster mixes the
two (`E1`, `MCV`, `SUPPLYROUTE` but `abrams`, `t90`, `power.b83`). Taking the key from the survey's
own resolution means the case is never typed by hand.

AND THE SURVEY'S UNIVERSE IS mod.yaml's `Rules:` LIST, NOT `os.walk` OVER THE RULES DIRECTORY.
Some .yaml files under mods/ww3mod/rules/ are never loaded by the mod (18 of them on 2026-09-20;
unloaded_rules_paths() is the authority, and the number moves), and an override on an actor only
they define creates a bare actor instead of overriding one -- see loaded_rules_paths() for what
that cost on 2026-09-20.

WHERE THE WORDING COMES FROM. Every caption whose cameo has legible baked lettering is that
lettering, verbatim -- read off tools/cameo/contact_sheet.py --all at 4x. That is deliberate: the
standing direction is that swapping a cameo for untexted art plus a CameoCaption should be a
VISUAL NO-OP, and a caption that says something other than the word it is painted over is not.

Four kinds of entry depart from that, each marked below:
  DERIVED   the art is ShpTD or absent and cannot be decoded here, so the wording comes from the
            Tooltip Name instead. Verdict on the baked lettering is unknown, not clean.
  SPLIT     several actors share one sprite and the baked word cannot name them all. This is the
            case the whole caption mechanism exists for -- five actors draw `mcvicon`.
  WRONG     the baked word belongs to a different actor because the art was reused. `fixicon`
            says SERVICE DEPOT and is the Supply Route; `facticon` says CONVARD and is the
            Logistics Center. Copying those through would ship a known-wrong caption.
  AMBIG     the baked word is right but does not discriminate. `migicon` says MIG in a mod that
            also flies MiG-31s.
"""

import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

OUT = os.path.join(ROOT, "mods", "ww3mod", "rules", "cameo-captions.yaml")
MOD_YAML = os.path.join(ROOT, "mods", "ww3mod", "mod.yaml")
RULES_DIR = os.path.join(ROOT, "mods", "ww3mod", "rules")
TABLE_REL = "rules/cameo-captions.yaml"
TABLE_REF = "ww3mod|" + TABLE_REL


def mod_rules_entries(mod_yaml=MOD_YAML):
    """The `Rules:` block of mod.yaml, in load order, verbatim (`ww3mod|rules/misc.yaml`)."""
    lines = io.open(mod_yaml, encoding="utf-8-sig").read().splitlines()
    if "Rules:" not in lines:
        raise SystemExit("%s has no top-level Rules: block" % mod_yaml)
    out = []
    for ln in lines[lines.index("Rules:") + 1:]:
        if not ln[:1].isspace():
            break                      # first non-indented line ends the block
        t = ln.strip()
        if t and not t.startswith("#"):
            out.append(t)
    return out


def loaded_rules_paths(include_table=False, mod_yaml=MOD_YAML):
    """Absolute paths of the rules files mod.yaml ACTUALLY loads, in load order.

    THE ACTOR UNIVERSE COMES FROM HERE AND NOT FROM os.walk, and that distinction is the whole
    reason this function exists. `mods/ww3mod/rules/` holds .yaml files mod.yaml never loads --
    every weapons/ and sound/ file, the campaign/ tree, `ingame/old.yaml`; 18 of them on
    2026-09-20, and unloaded_rules_paths() recounts in a second rather than being quoted from
    here. An actor defined only in one of those is NOT in the game, so a caption override
    on it does not override anything: MiniYaml creates a NEW top-level actor carrying only
    `Buildable: CameoCaption`, which then fails `Actor type `x` does not define a default
    visibility type` and `The following buildable actor has no (enabled) Tooltip` on every map in
    the lint. That is exactly what shipped on 2026-09-20 for MCV, MCV.ai, MCV2, MCV2.ai and t72.

    A file listed here but absent from disk is a hard error -- the mod itself would not load. The
    converse is deliberately harmless: an UNlisted file may be deleted or added freely and this
    function does not notice, which is what keeps the generator correct across branches that add
    or remove orphans.
    """
    paths = []
    for entry in mod_rules_entries(mod_yaml):
        if "|" not in entry:
            raise SystemExit("Rules: entry %r has no `prefix|path` form" % entry)
        prefix, rel = entry.split("|", 1)
        if prefix != "ww3mod":
            continue                   # ^EngineDir| and friends are not this mod's rules
        if not include_table and rel == TABLE_REL:
            continue
        path = os.path.join(ROOT, "mods", "ww3mod", *rel.split("/"))
        if not os.path.exists(path):
            raise SystemExit("mod.yaml lists %s but %s does not exist" % (entry, path))
        paths.append(path)
    return paths


def unloaded_rules_paths():
    """Rules .yaml files present on disk that mod.yaml does NOT load. Diagnostics only."""
    import rollout_survey as rs
    loaded = {os.path.abspath(p) for p in loaded_rules_paths(include_table=True)}
    return sorted(p for p in rs.walk(RULES_DIR) if os.path.abspath(p) not in loaded)


def top_level_keys(path):
    """Top-level (unindented) keys defined by one rules file, verbatim case."""
    out = set()
    for raw in io.open(path, encoding="utf-8-sig"):
        line = raw.rstrip()
        if not line.strip() or line[:1].isspace() or line.lstrip().startswith("#"):
            continue
        out.add(line.strip().rstrip(":"))
    return out


def defined_in_loaded_rules():
    """Every top-level key the loaded rules files define, verbatim case. The table is excluded."""
    out = set()
    for p in loaded_rules_paths():
        out |= top_level_keys(p)
    return out

# actor -> (caption, source). source is "" for a verbatim baked word, else DERIVED/SPLIT/WRONG/AMBIG.
# Faction variants share their base actor's caption; they are expanded automatically below, so
# `AA`, `AA.america` and `AA.russia` are one line here.
CAPTIONS = {
    # --- infantry. Every one of these is the baked lettering, verbatim. -----------------------
    "E1": ("CONSCRIPT", ""),
    "E2": ("GRENADIER", ""),
    "E3": ("RIFLEMAN", ""),
    "E4": ("FLAMETHROWER", ""),
    "E6": ("ENGINEER", ""),
    "MEDI": ("MEDIC", ""),
    "SN": ("SNIPER", "DERIVED"),          # snamericaicon/snrussiaicon are not in the repo
    "AR": ("LMG", ""),
    "MT": ("MORTAR", ""),
    "AT": ("JAVELIN AT", ""),
    "AA": ("STINGER AA", ""),
    "SF": ("SPEC FORCES", ""),
    "TL": ("TEAM LEADER", ""),
    "TECN": ("TECHNICIAN", ""),
    "DR": ("DRONE OP", ""),

    # --- vehicles -----------------------------------------------------------------------------
    "abrams": ("ABRAMS", ""),
    "bmp2": ("BMP2", ""),
    "bradley": ("BRADLEY", ""),
    "btr": ("BTR-80", "DERIVED"),         # btricon is ShpTD, undecodable here
    "giatsint": ("GIATSINT", ""),
    "grad": ("GRAD", ""),
    "HIMARS": ("HIMARS", ""),
    "humvee": ("HUMVEE", ""),
    "iskander": ("ISKANDER", "DERIVED"),  # art carries no lettering
    "m109": ("PALADIN", ""),
    "m113": ("APC", ""),
    "m270": ("M270 MLRS", ""),
    "strykershorad": ("STRYKER AA", ""),
    # t72 is defined only in rules/ingame/vehicles-ukraine.yaml, which mod.yaml does not load
    # (and which another branch deletes as an orphan). Inert, kept for the same reason as MCV.
    "t72": ("T72", ""),
    "t90": ("T90", ""),
    "tos": ("SOLNTSEPYOK", ""),
    "tunguska": ("TUNGUSKA", ""),
    "MNLY": ("MINELAYER", ""),
    "TRUK": ("SUPPLY TRUCK", ""),
    "MSAR": ("RANGING", "DERIVED"),       # msaricnh is ShpTD
    # mcvicon's baked word is MCV and five actors used to draw it. Four of those five -- MCV,
    # MCV.ai, MCV2, MCV2.ai -- live only in rules/ingame/old.yaml, which mod.yaml does not load,
    # so they are NOT in the game and resolve_table places nothing for them. Their wording is
    # kept here, inert, against the day that file is loaded; captions_table.py names them on
    # every run ("no actor in the loaded mod for: ..."), and check_captions.py check 7 would
    # reject them if they ever reached the YAML while still unloaded.
    "MCV": ("MCV", ""),
    "MCV2": ("FIELD BASE", "SPLIT"),
    # LCCV is the only loaded actor left drawing mcvicon, so this is no longer a SPLIT: nothing
    # shares the sprite with it. It is a WRONG -- the art is reused and its baked word names the
    # MCV, which a Logistics MCV is not. The caption text is unchanged either way.
    "LCCV": ("LOGISTICS MCV", "WRONG"),

    # --- aircraft -----------------------------------------------------------------------------
    "A10": ("A-10", "DERIVED"),           # a10icon is ShpTD
    "F16": ("F-16", "DERIVED"),           # f16icon is not in the repo
    "FROG": ("SU-25", "DERIVED"),         # frogicon is ShpTD
    "MIG": ("MIG-29", "AMBIG"),           # baked word is "MIG"; the mod also flies MiG-31s
    "MI28": ("HAVOC", ""),
    "HIND": ("HIND", ""),
    "HELI": ("APACHE", ""),
    "HALO": ("MI-26 HALO", "DERIVED"),    # haloicon is ShpTD
    "TRAN": ("CHINOOK", ""),
    "littlebird": ("LITTLEBIRD", ""),

    # --- buildings ----------------------------------------------------------------------------
    "AFLD": ("AIRFIELD", ""),
    "HPAD": ("HELIPAD", ""),
    # WRONG: facticon's baked word is CONVARD -- Red Alert's construction yard, an actor this mod
    # does not have. fixicon's is SERVICE DEPOT and it is the Supply Route, the one structure the
    # whole economy turns on. Both would be actively misleading if copied through.
    "LOGISTICSCENTER": ("LOGISTICS", "WRONG"),
    "SUPPLYROUTE": ("SUPPLY ROUTE", "WRONG"),
    "MSLO": ("MISSILE SILO", ""),

    # --- defences -----------------------------------------------------------------------------
    "AGUN": ("ANTI AIR GUN", ""),
    "CRAM": ("C-RAM", "DERIVED"),         # cramicnh is ShpTD
    "HSAM": ("CAMO SAM SITE", ""),
    "SAM": ("SAM SITE", "DERIVED"),       # samicon is base-game RA content, not in this repo
    "GUN": ("GUN TURRET", ""),
    "FTUR": ("FLAME TURRET", ""),
    "PBOX": ("PILLBOX", ""),
    "HBOX": ("CAMO PILLBOX", ""),
    "GTWR": ("GUARD TOWER", "DERIVED"),   # gtwricnh is ShpTD
    "SBAG": ("SANDBAGS", ""),
    "BARB": ("BARBED WIRE", ""),
    "FENC": ("WIRE FENCE", ""),
    "BRIK": ("CONCRETE WALL", ""),
    # SPLIT: hgateicon and vgateicon are different files, but both bake "GATE" plus a glyph the
    # caption font has no equivalent for. The orientation is the whole difference between them.
    "HGATE": ("GATE H", "SPLIT"),
    "VGATE": ("GATE V", "SPLIT"),

    # --- the two power proxies that carry NO live caption ---------------------------------------
    # The other fourteen `power.*` actors state a yield in rules/powers.yaml and are left to it.
    # These two are the conventional powers: no yield to state, and so nothing captioning them
    # today. Both draw art with no lettering, so there is no baked word to be verbatim about.
    "power.kinzhal": ("KINZHAL", "DERIVED"),
    "power.oreshnik": ("ORESHNIK", "DERIVED"),
}

NOTE = {
    "": "baked lettering, verbatim",
    "DERIVED": "art undecodable or absent -- wording from the Tooltip Name",
    "SPLIT": "shares a sprite with another actor; the baked word cannot name both",
    "WRONG": "the baked word names a DIFFERENT actor -- reused art",
    "AMBIG": "baked word is right but does not discriminate",
}

HEADER = """\
# CAMEO CAPTIONS -- AUTHORED, COMPLETE, AND LIVE.
#
# The user ruled these live on 2026-09-20. mods/ww3mod/mod.yaml lists
#
#     \tww3mod|rules/cameo-captions.yaml
#
# as the LAST entry under `Rules:`, and that position is not incidental.
#
# WHY IT GOES LAST. These are overrides on actors defined elsewhere, and MiniYaml resolves
# `Inherits@`/`-Key:` where they appear, so an override loaded before its definition is an
# override of nothing. Last is the only position that is right regardless of what moves above it.
#
# WHY EVERY ENTRY BELOW IS AN ACTOR mod.yaml ACTUALLY LOADS. An override on an actor that no
# loaded rules file defines does not fail loudly -- MiniYaml simply creates a new top-level actor
# carrying only this `Buildable: CameoCaption`, and that actor then trips `does not define a
# default visibility type` and `has no (enabled) Tooltip` on EVERY map in the lint. Five entries
# did exactly that on 2026-09-20 (MCV, MCV.ai, MCV2, MCV2.ai from rules/ingame/old.yaml and t72
# from rules/ingame/vehicles-ukraine.yaml -- mod.yaml loads neither file): 3,650 lint errors, ten
# messages across 365 maps. The generator now takes its actor universe from mod.yaml's Rules:
# list, and check_captions.py check 7 fails if such an entry is reintroduced.
#
# WHAT IT LOOKS LIKE IN GAME. Every cameo below draws its caption over a black band
# (CaptionBackgroundColor: 000000FF in chrome/ingame-player.yaml), which covers the baked
# lettering underneath. The wording is that lettering verbatim wherever it was legible, so for
# most of the roster the sidebar should look unchanged. It is NOT a licence to turn the band off:
# that needs untexted art, cameo by cameo, and the band is a widget-wide setting with no
# per-actor form.
#
# FOURTEEN OF THE SIXTEEN `power.*` PROXIES ARE ABSENT ON PURPOSE. They already carry a live
# CameoCaption in rules/powers.yaml -- yields like `0.3 KT` that this file must not become a
# second authority for. The two that ARE here, power.kinzhal and power.oreshnik, are the
# conventional powers: they have no yield to state and nothing captions them today.
# tools/cameo/check_captions.py asserts that every buildable actor with a cameo is in exactly one
# of the two places.
#
# GENERATED by tools/cameo/captions_table.py, which is where the wording and the reasoning live.
# Edit that and regenerate; an edit made here is lost on the next run.
"""


def survey(paths=None):
    """{actor: {tooltip, art, live}} for every buildable actor with a cameo IN THE LOADED MOD.

    `paths` defaults to loaded_rules_paths() -- the files mod.yaml lists, minus the table itself.
    Excluding the table is load-bearing, not tidiness: the table IS a loaded rules file now, and
    every entry in it carries a `Buildable:`. Survey a universe that includes it and each entry
    vouches for its own existence, so a stale entry can never be dropped by a regeneration.
    """
    import rollout_survey as rs
    rules = rs.merge(paths if paths is not None else loaded_rules_paths())
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
        out[name] = dict(tooltip=rs.field(t.get("Tooltip"), "Name") or "", art=art,
                         live=rs.field(t.get("Buildable"), "CameoCaption"))
    return out


def resolve_table(actors):
    """{actor: (caption, source)} -- CAPTIONS expanded across faction variants.

    `AA.america` takes `AA`'s caption. The variants are separate actors with separate Buildable
    traits, so each needs its own override; authoring them separately would be 45 lines of
    duplication and 45 chances to let one drift.
    """
    out = {}
    for actor in actors:
        base = actor.split(".")[0]
        if actor in CAPTIONS:
            out[actor] = CAPTIONS[actor]
        elif base in CAPTIONS and actor.split(".")[-1] in ("america", "russia", "ai"):
            out[actor] = CAPTIONS[base]
    return out


def write(path=OUT):
    actors = survey()
    table = resolve_table(actors)
    lines = [HEADER]
    for actor in sorted(table, key=lambda a: (actors[a]["art"], a)):
        caption, source = table[actor]
        lines.append("")
        if source:
            lines.append("# %s: %s" % (source, NOTE[source]))
        lines.append("%s:" % actor)
        lines.append("\tBuildable:")
        lines.append("\t\tCameoCaption: %s" % caption)
    # A trailing newline and no trailing blank line: blank lines between top-level entries are
    # significant in MiniYaml (adjacent entries silently merge without one), and a stray pair at
    # the end is the kind of thing that survives review and then confuses the next reader.
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print("wrote %s -- %d actors, %d distinct captions"
          % (os.path.relpath(path, ROOT), len(table), len(set(c for c, _ in table.values()))))
    # A CAPTIONS key that placed nothing is either a base name whose variants carried it (normal)
    # or an actor the loaded mod no longer has (worth seeing). Printed, never fatal.
    placed = {a.split(".")[0] for a in table} | set(table)
    idle = sorted(k for k in CAPTIONS if k not in placed)
    if idle:
        print("  no actor in the loaded mod for: %s" % ", ".join(idle))
    return path


if __name__ == "__main__":
    write()
