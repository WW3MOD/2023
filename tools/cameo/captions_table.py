#!/usr/bin/env python3
"""The authored cameo caption table, and the generator that writes it to YAML.

    python tools/cameo/captions_table.py          # rewrite mods/ww3mod/rules/cameo-captions.yaml

THE TABLE IS HERE AND THE YAML IS GENERATED, not the other way round, for one reason: actor names
in a rules override must match the DEFINING case exactly. Top-level MiniYaml keys merge
case-sensitively and the lower-casing happens afterwards, so a `t90:` written against a defining
`t90:` works and against a defining `T90:` silently overrides nothing -- and this roster mixes the
two (`E1`, `MCV`, `SUPPLYROUTE` but `abrams`, `t90`, `power.b83`). Taking the key from the survey's
own resolution means the case is never typed by hand.

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

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

OUT = os.path.join(ROOT, "mods", "ww3mod", "rules", "cameo-captions.yaml")

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
    "t72": ("T72", ""),
    "t90": ("T90", ""),
    "tos": ("SOLNTSEPYOK", ""),
    "tunguska": ("TUNGUSKA", ""),
    "MNLY": ("MINELAYER", ""),
    "TRUK": ("SUPPLY TRUCK", ""),
    "MSAR": ("RANGING", "DERIVED"),       # msaricnh is ShpTD
    # SPLIT: five actors draw mcvicon, whose baked word is MCV. Two of them are not an MCV.
    "MCV": ("MCV", ""),
    "MCV2": ("FIELD BASE", "SPLIT"),
    "LCCV": ("LOGISTICS MCV", "SPLIT"),

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
# CAMEO CAPTIONS -- AUTHORED, COMPLETE, AND DELIBERATELY NOT LOADED.
#
# TO TURN THIS ON, ADD ONE LINE. In mods/ww3mod/mod.yaml, under `Rules:`, add
#
#     \tww3mod|rules/cameo-captions.yaml
#
# as the LAST entry in the list, and nothing else anywhere. Until that line exists this file is
# inert: the mod never reads it, no cameo draws a caption it does not draw today, and deleting it
# would change nothing. That is the point -- the wording is reviewable without being live.
#
# WHY IT GOES LAST. These are overrides on actors defined elsewhere, and MiniYaml resolves
# `Inherits@`/`-Key:` where they appear, so an override loaded before its definition is an
# override of nothing. Last is the only position that is right regardless of what moves above it.
#
# WHAT HAPPENS THE DAY IT IS ON. Every cameo below starts drawing its caption over a black band
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


def survey():
    """{actor: (tooltip name, art file)} for every buildable actor that resolves to a cameo."""
    import rollout_survey as rs
    rules = rs.merge(rs.walk(os.path.join(ROOT, "mods/ww3mod/rules")))
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
        live = rs.field(t.get("Buildable"), "CameoCaption")
        out[name] = (rs.field(t.get("Tooltip"), "Name") or "", art, live)
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
    for actor in sorted(table, key=lambda a: (actors[a][1], a)):
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
    return path


if __name__ == "__main__":
    write()
