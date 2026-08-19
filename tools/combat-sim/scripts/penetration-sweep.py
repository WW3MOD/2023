#!/usr/bin/env python3
"""Penetration audit: does the Penetration=1 default actually cost anything?

`DamageWarhead.Penetration` defaults to 1 and most warheads never override it,
which reads as alarming. This script walks the count down from the raw headline
to the warheads where the default can actually change a damage number, and
prices the two things a reader might do about it.

Static arithmetic replicating DamageWarhead.InflictDamage (DamageWarhead.cs
:200-247). Not the engine, and no game launch. Integer division is deliberate:
the engine does int math and the truncation carries much of the effect.
"""
import glob
import json
import os
import re
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
STATS = os.path.join(ROOT, "tools", "combat-sim", "data", "stats.json")
RULES = os.path.join(ROOT, "mods", "ww3mod", "rules")


def target_type_index():
    """actor -> Targetable.TargetTypes, following Inherits. The balance dump does
    not carry target types, and guessing them is how you miscount: `gtwr` is
    Thickness 25 and targetable as `Unarmored`, `quadcopterdrone` is Thickness 3
    and targetable as `Drone`."""
    blocks = defaultdict(list)
    for f in glob.glob(os.path.join(RULES, "**", "*.yaml"), recursive=True):
        for b in re.split(r"\n(?=\S)", open(f, encoding="utf-8", errors="replace").read()):
            name = b.split("\n", 1)[0].split(":")[0].strip()
            if name and not name.startswith("#"):
                blocks[name.lower()].append(b)

    def resolve(name, depth=0):
        out = set()
        if depth > 4:
            return out
        for b in blocks.get(name.lower(), []):
            for m in re.finditer(r"\n\t*Targetable[^\n]*\n((?:\t\t+[^\n]*\n)*)", b):
                mm = re.search(r"TargetTypes:\s*([^\n]+)", m.group(1))
                if mm:
                    out |= {x.strip() for x in mm.group(1).split(",")}
            for m in re.finditer(r"\n\tInherits[^:]*:\s*([^\n]+)", b):
                out |= resolve(m.group(1).strip(), depth + 1)
        return out
    return resolve


def pen_reduce(damage, penetration, thickness):
    """DamageWarhead.cs:216-231. armorPercent already folded into `thickness`."""
    if thickness != 0 and penetration - thickness < 0:
        return damage * penetration // thickness
    return damage


def facing_thickness(armor, facing="front"):
    """DamageWarhead.cs:219-220. Distribution is {front, side, rear, top, bottom}."""
    t, dist = armor["thickness"], armor["distribution"]
    if not dist or len(dist) != 5:
        return t
    return t * dist[{"front": 0, "side": 1, "rear": 2, "top": 3, "bottom": 4}[facing]] // 100


def avg_raw(wh):
    """Damage after RandomDamage*, which the engine applies before penetration."""
    return wh["damage"] + wh["random_damage_addition"] // 2 - wh["random_damage_subtraction"] // 2


def section(n, title):
    print(f"\n{'=' * 78}\n{n}. {title}\n{'=' * 78}")


def load():
    s = json.load(open(STATS))
    return s["weapons"], s["actors"]


def armament_index(A, enabled_only):
    idx = defaultdict(list)
    for k, a in A.items():
        if enabled_only and a.get("disabled"):
            continue
        for arm in a.get("armaments") or []:
            wn = (arm.get("weapon") or "").lower()
            if wn:
                idx[wn].append(k)
    return idx


def sibling_pen(W, key):
    """Max Penetration among the other warheads of the same weapon."""
    pens = [wh["penetration"] for wh in W[key]["warheads"]]
    return max(pens) if pens else 1


def funnel(W, A):
    section(1, "THE FUNNEL — from the alarming headline to what actually matters")
    any_arm = armament_index(A, False)
    en_arm = armament_index(A, True)

    en_armoured = {k for k, a in A.items()
                   if (a.get("armor") or {}).get("thickness") and not a.get("disabled")}
    # Every target type through which some obtainable armoured actor can be hit.
    # A pen-1 warhead is reducible iff its ValidTargets intersects this set.
    resolve = target_type_index()
    armoured_types = set()
    for k in en_armoured:
        armoured_types |= resolve(k)

    rows = []
    stage = defaultdict(lambda: [0, 0])

    def bump(name, is_one):
        stage[name][0] += 1
        stage[name][1] += is_one

    for k, w in W.items():
        for wh in w["warheads"]:
            one = wh["penetration"] == 1
            bump("every damage warhead in the dump", one)
            if not k.startswith("^"):
                bump("  on a concrete weapon (^ templates dropped)", one)
            if k.lower() in any_arm:
                bump("    fired by some unit's armament", one)
            if k.lower() in en_arm:
                bump("      fired by a unit a player can actually get", one)
                if one:
                    reducible = bool(set(wh["valid_targets"]) & armoured_types)
                    rows.append((k, wh, reducible))

    print(f"  {'population':<52}{'pen=1':>8}{'total':>8}{'share':>8}")
    print("  " + "-" * 74)
    for name in ("every damage warhead in the dump",
                 "  on a concrete weapon (^ templates dropped)",
                 "    fired by some unit's armament",
                 "      fired by a unit a player can actually get"):
        tot, one = stage[name]
        print(f"  {name:<52}{one:>8}{tot:>8}{100*one/tot:>7.1f}%")

    red = [r for r in rows if r[2]]
    dead = [r for r in rows if not r[2]]
    print(f"\n  Of those {len(rows)} pen-1 warheads on obtainable units:")
    print(f"    {len(dead):>3} can NEVER reach the penetration branch — every target type they")
    print(f"        list belongs to actors with Thickness 0. The default is a pure no-op.")
    print(f"    {len(red):>3} CAN be reduced. These are the only ones worth arguing about.")

    print(f"\n  Splitting those {len(red)} by the Spread/Target idiom "
          f"(a penetrating sibling warhead\n  on the same weapon means the low value is the "
          f"documented deliberate design):")
    lone = []
    for k, wh, _ in sorted(red):
        sp = sibling_pen(W, k)
        if sp <= 1:
            lone.append((k, wh))
    print(f"    {len(red)-len(lone):>3} have a penetrating sibling  -> deliberate splash, correct by design")
    print(f"    {len(lone):>3} stand alone:")
    for k, wh in lone:
        print(f"          {k:<24} {wh['type'].replace('DamageWarhead',''):<8} "
              f"damage={wh['damage']:<6} targets={','.join(wh['valid_targets'])}")
    return red


def bulk_fix_cost(W, A, red):
    section(2, "WHAT A NAIVE BULK FIX WOULD COST")
    print("  If someone 'fixed the 167 defaults' by giving each pen-1 warhead the")
    print("  penetration of its own weapon's main warhead, splash stops being")
    print("  harmless to armour. Total damage per shot, now vs then:\n")
    victims = [(k, A[k]) for k in ("abrams", "t90", "bradley", "bmp2", "btr") if k in A]
    print(f"  {'weapon':<26}" + "".join(f"{k[:7]:>18}" for k, _ in victims))
    print(f"  {'':<26}" + "".join(f"{'now -> fixed':>18}" for _ in victims))
    print("  " + "-" * (26 + 18 * len(victims)))
    seen = set()
    for k, _, _ in sorted(red):
        if k in seen:
            continue
        seen.add(k)
        sp = sibling_pen(W, k)
        if sp <= 1:
            continue
        row = f"  {k:<26}"
        for vk, a in victims:
            th = facing_thickness(a["armor"])
            now = fixed = 0
            for wh in W[k]["warheads"]:
                if not (set(wh["valid_targets"]) & {"Ground", "Water", "Vehicle", "Defense"}):
                    continue
                raw = avg_raw(wh)
                now += pen_reduce(raw, wh["penetration"], th)
                fixed += pen_reduce(raw, sp if wh["penetration"] == 1 else wh["penetration"], th)
            delta = f"{now}->{fixed}" if now != fixed else f"{now} ="
            row += f"{delta:>18}"
        print(row)
    print("\n  Reading: the increase is real but bounded (+15-20% on the big AT weapons),")
    print("  because the penetrating Target warhead already carries most of the damage.")
    print("  It is still a mod-wide buff to every armoured target, applied by accident.")


def latent_hazard(W, A):
    section(3, "THE LATENT HAZARD — pen-1 on anti-air, all of it behind ~disabled")
    print("  Every shipped AA weapon has a deliberate penetration matched to aircraft")
    print("  thickness (max 20 in this mod). Every AA weapon left at the default sits")
    print("  on a ~disabled actor. Re-enabling any of them ships a broken weapon.\n")
    aircraft = [(k, A[k]) for k in ("heli", "mi28", "a10", "hind", "littlebird", "mig")
                if k in A and (A[k].get("armor") or {}).get("thickness")]
    weapons = [("stinger.quad", "strykershorad"), ("9m311", "tunguska"),
               ("30mm.tunguska.aa", "tunguska"), ("12.7mm.hind.aa", "hind"),
               ("manpad", "aa"), ("surfacetoairmissile.double", "sam / hsam"),
               ("airtoairmissile", "f16 / mig"), ("aacannon", "agun")]
    print(f"  {'weapon':<28}{'carrier':<16}{'avail':<10}{'pen':>4}{'raw':>7}  "
          + "".join(f"{k[:8]:>11}" for k, _ in aircraft))
    print("  " + "-" * (65 + 11 * len(aircraft)))
    for wn, carrier in weapons:
        w = W[wn]
        wh = max(w["warheads"], key=lambda x: x["damage"])
        avail = "disabled" if all(A[c.strip()].get("disabled")
                                  for c in carrier.split("/") if c.strip() in A) else "SHIPPED"
        row = f"  {wn:<28}{carrier:<16}{avail:<10}{wh['penetration']:>4}{avg_raw(wh):>7}  "
        for k, a in aircraft:
            eff = pen_reduce(avg_raw(wh), wh["penetration"], a["armor"]["thickness"])
            shots = -(-a["hp"] // eff) if eff > 0 else 999
            row += f"{eff:>7}/{shots:<3}"
        print(row)
    print("\n  cells: effective-damage/shots-to-kill.  aircraft thickness "
          + ", ".join(f"{k}={a['armor']['thickness']}" for k, a in aircraft))


def deliberate_but_odd(W, A):
    section(4, "A DELIBERATE VALUE WORTH A SECOND LOOK (not a default)")
    print("  ATGM's main warhead carries Penetration 100 — chosen, not inherited — but")
    print("  that is below every MBT's thickness, so the infantry AT team is graded")
    print("  down against exactly the target it exists to kill.\n")
    victims = [(k, A[k]) for k in ("bradley", "bmp2", "t90", "abrams") if k in A]
    print(f"  {'weapon':<22}{'pen':>5}{'raw':>8}  " + "".join(f"{k[:8]:>16}" for k, _ in victims))
    print("  " + "-" * (35 + 16 * len(victims)))
    for wn in ("atgm", "rpg", "hellfire", "tankround.abrams"):
        w = W[wn]
        wh = max(w["warheads"], key=lambda x: x["damage"])
        row = f"  {wn:<22}{wh['penetration']:>5}{avg_raw(wh):>8}  "
        for vk, a in victims:
            th = facing_thickness(a["armor"])
            eff = pen_reduce(avg_raw(wh), wh["penetration"], th)
            shots = -(-a["hp"] // eff) if eff > 0 else 999
            row += f"{eff:>9}/{shots:<6}"
        print(row)
    print("\n  cells: effective-damage/shots-to-kill, front facing, main warhead only.")


def main():
    W, A = load()
    red = funnel(W, A)
    bulk_fix_cost(W, A, red)
    latent_hazard(W, A)
    deliberate_but_odd(W, A)


if __name__ == "__main__":
    main()
