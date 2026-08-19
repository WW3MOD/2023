#!/usr/bin/env python3
"""Static payload comparison for the two strike launchers (Iskander / HIMARS).

The combat-sim dashboard reads each actor's *first* armament. For these two that
is the InstantHit "targeter" (50 damage, Versus all-zero) which is a designator,
not the payload -- the real damage arrives via
MissileSpawnerMaster -> <X>Missile -> SpawnedExplodes -> <X>Explosion.
So the dashboard reports 5 dps for both and is blind to the actual asymmetry.

This script replicates OpenRA.Mods.Common/Warheads/DamageWarhead.InflictDamage
(engine/.../DamageWarhead.cs:200-247) against the resolved stats dump. It is
static arithmetic, NOT the engine running -- no positioning, no projectile
travel, no interception, no autotarget. Treat it as an upper bound on a
clean direct hit.
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
STATS = os.path.join(ROOT, "tools", "combat-sim", "data", "stats.json")


def apply_percentage_modifiers(value, modifiers):
    """Util.ApplyPercentageModifiers -- integer division, applied in sequence."""
    for m in modifiers:
        value = value * m // 100
    return value


def inflict(damage, penetration, versus, victim_thickness, victim_armor_type,
            armor_percent=100):
    """DamageWarhead.InflictDamage, minus the RNG fields (all zero here)."""
    if victim_thickness != 0:
        thickness = victim_thickness * armor_percent // 100
        if penetration - thickness < 0:
            # "Can't penetrate - Reduce damage by how much it penetrated"
            damage = damage * penetration // thickness
    mods = []
    if versus:
        if victim_armor_type in versus:
            mods.append(versus[victim_armor_type])
    return apply_percentage_modifiers(damage, mods)


def direct_hit_damage(weapon, victim_thickness, victim_armor_type, is_infantry):
    """Total damage to a victim standing at the impact point (distance 0).

    At distance 0 every falloff table starts at 100, so each warhead applies at
    full value. Target-type gating is honoured.
    """
    total = 0
    breakdown = []
    for wh in weapon["warheads"]:
        valid = wh["valid_targets"]
        # Anti-infantry sub-warhead only bites Infantry/Unarmored.
        if "Infantry" in valid and "Ground" not in valid and not is_infantry:
            continue
        d = inflict(wh["damage"], wh["penetration"], wh["versus"],
                    victim_thickness, victim_armor_type)
        total += d
        breakdown.append((wh["type"], wh["damage"], wh["penetration"], d))
    return total, breakdown


def main():
    stats = json.load(open(STATS))
    actors = stats["actors"]
    weapons = stats["weapons"]

    isk = weapons["iskanderexplosion"]
    him = weapons["himarsexplosion"]

    targets = [t for t in sys.argv[1:]] or [
        "abrams", "t90", "bradley", "bmp2", "strykershorad", "tunguska",
        "himars", "iskander", "rifleman", "conscript",
    ]

    print(f"{'target':<16} {'armor':<12} {'HP':>7} "
          f"{'ISK dmg':>9} {'HIM dmg':>9} {'ratio':>6} "
          f"{'ISK shots':>10} {'HIM shots':>10}")
    print("-" * 88)

    for name in targets:
        a = actors.get(name)
        if a is None:
            print(f"{name:<16} -- not found in dump --")
            continue
        hp = a.get("hp") or 0
        armor = a.get("armor") or {}
        armor_type = armor.get("type")
        thickness = armor.get("thickness") or 0
        is_inf = armor_type in ("None", "Unarmored", None)

        di, _ = direct_hit_damage(isk, thickness, armor_type, is_inf)
        dh, _ = direct_hit_damage(him, thickness, armor_type, is_inf)

        ratio = (di / dh) if dh else float("inf")
        si = "kill" if di >= hp else f"{-(-hp // di)}" if di else "never"
        sh = "kill" if dh >= hp else f"{-(-hp // dh)}" if dh else "never"

        print(f"{name:<16} {str(armor_type)+'/'+str(thickness):<12} {hp:>7} "
              f"{di:>9} {dh:>9} {ratio:>6.2f} {si:>10} {sh:>10}")

    print()
    print("Per-warhead breakdown vs a Heavy/700 tank (Abrams):")
    for label, w in (("Iskander", isk), ("HIMARS", him)):
        _, bd = direct_hit_damage(w, 700, "Heavy", False)
        print(f"  {label}:")
        for t, raw, pen, final in bd:
            print(f"    {t:<24} raw={raw:>6} pen={pen:>5} -> {final:>6}")


if __name__ == "__main__":
    main()
