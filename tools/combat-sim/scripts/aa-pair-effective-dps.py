#!/usr/bin/env python3
"""Tunguska vs Stryker SHORAD: effective DPS by target class, plus magazine endurance.

The dashboard's `dps` command reports raw sustained DPS per armament and only
ever shows the FIRST armament in `compare`. Neither accounts for (a) the
penetration-vs-thickness reduction, which is the dominant term in this mod, nor
(b) AmmoPool depletion. Both matter for this pair: Tunguska's autocannon has
pen 70 (useless against an MBT's thickness 700) and burns its magazine ~8x
faster than the Stryker's.

Static arithmetic replicating DamageWarhead.InflictDamage. Not the engine.
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
STATS = os.path.join(ROOT, "tools", "combat-sim", "data", "stats.json")
TICKS = 25

# AmmoPool sizes and per-shot usage, from the actor YAML (the balance dump does
# not carry AmmoPool). tunguska: vehicles-russia.yaml:835-839, 865-869.
# strykershorad: vehicles-america.yaml:883-891, 911-915, 945-949.
AMMO = {
    "tunguska":      {"30mm.tunguska.ag": 180, "30mm.tunguska.aa": 180, "9m311": 8},
    "strykershorad": {"25mm.bradley": 400, "stinger.quad": 8, "hellfire.strykershorad": 4},
}


def pen_reduce(damage, penetration, thickness):
    if thickness != 0 and penetration - thickness < 0:
        return damage * penetration // thickness
    return damage


def per_shot(weapon, thickness, target_kinds):
    """Damage from one projectile against a victim of the given target kinds."""
    total = 0
    for wh in weapon["warheads"]:
        if not (set(wh["valid_targets"]) & target_kinds):
            continue
        total += pen_reduce(wh["damage"], wh["penetration"], thickness)
    return total


def cycle_ticks(w):
    delays = w["burst_delays"] or [0]
    avg = sum(delays) // len(delays)
    return max(1, (w["burst"] - 1) * avg + w["burst_wait"])


def main():
    stats = json.load(open(STATS))
    W = stats["weapons"]

    # (label, thickness, target kinds a warhead must list to apply)
    classes = [
        ("infantry",        0,   {"Infantry", "Unarmored", "Ground"}),
        ("IFV Medium/15",   15,  {"Ground", "Water"}),
        ("MBT Heavy/700",   700, {"Ground", "Water"}),
        ("helicopter",      0,   {"Air"}),
        ("fixed-wing air",  0,   {"Air"}),
    ]
    # Which armament may legally engage which class (weapon-level ValidTargets).
    engages = {
        "30mm.tunguska.ag":       {"infantry", "IFV Medium/15", "MBT Heavy/700"},
        "30mm.tunguska.aa":       {"helicopter"},
        "9m311":                  {"helicopter", "fixed-wing air"},
        "25mm.bradley":           {"infantry", "IFV Medium/15", "MBT Heavy/700"},
        "stinger.quad":           {"helicopter", "fixed-wing air"},
        "hellfire.strykershorad": {"IFV Medium/15", "MBT Heavy/700"},
    }
    units = {
        "tunguska": ["30mm.tunguska.ag", "30mm.tunguska.aa", "9m311"],
        "strykershorad": ["25mm.bradley", "stinger.quad", "hellfire.strykershorad"],
    }
    cost = {"tunguska": 1700, "strykershorad": 2500}

    print("Effective sustained DPS by target class (penetration applied, ammo ignored)\n")
    print(f"  {'target class':<18} {'Tunguska':>10} {'SHORAD':>10} {'ratio':>7}"
          f" {'ISK/cr':>9} {'SHO/cr':>9}")
    print("  " + "-" * 68)
    for label, thickness, kinds in classes:
        totals = {}
        for unit, arms in units.items():
            s = 0
            for a in arms:
                if label not in engages[a]:
                    continue
                w = W[a]
                s += w["burst"] * per_shot(w, thickness, kinds) / cycle_ticks(w) * TICKS
            totals[unit] = s
        t, sh = totals["tunguska"], totals["strykershorad"]
        ratio = f"{t/sh:.2f}" if sh else ("inf" if t else "-")
        print(f"  {label:<18} {t:>10.0f} {sh:>10.0f} {ratio:>7}"
              f" {1000*t/cost['tunguska']:>9.0f} {1000*sh/cost['strykershorad']:>9.0f}")

    print("\nMagazine endurance (one full AmmoPool, continuous fire)\n")
    print(f"  {'unit / armament':<34} {'rounds':>7} {'bursts':>7} {'seconds':>8}"
          f" {'raw dmg/mag':>12}")
    print("  " + "-" * 74)
    for unit, arms in units.items():
        for a in arms:
            w = W[a]
            rounds = AMMO[unit][a]
            bursts = rounds // w["burst"]
            secs = bursts * cycle_ticks(w) / TICKS
            main_wh = max(w["warheads"], key=lambda x: x["damage"])
            print(f"  {unit + ' / ' + a:<34} {rounds:>7} {bursts:>7} {secs:>8.1f}"
                  f" {rounds * main_wh['damage']:>12,}")


if __name__ == "__main__":
    main()
