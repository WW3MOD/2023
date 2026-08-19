#!/usr/bin/env python3
"""Collateral (splash) footprint comparison: IskanderExplosion vs HIMARSExplosion.

Companion to strike-payload-analysis.py. That script answers "how hard does one
missile hit the unit it was aimed at"; this one answers "how far does the hurt
reach", which is what decides how much army value a single strike deletes.

Models, from the resolved dump plus the shockwave params that the dump does not
carry (MaxRadius / Falloff / Spread, read from
mods/ww3mod/rules/weapons/weapons-explosions.yaml):

  SpreadDamageWarhead   falloff node i at distance i*Spread, linear lerp between
  ShockwaveDamageWarhead same, but expansion hard-capped at MaxRadius
                         (engine/.../ShockwaveDamageWarhead.cs:81,152-159)

The TargetDamage warhead is excluded on purpose -- it only ever applies to the
single designated target, so it contributes nothing to collateral.

Static arithmetic, not the engine. No positioning, interception or autotarget.
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
STATS = os.path.join(ROOT, "tools", "combat-sim", "data", "stats.json")

# Shockwave params not present in the balance dump; from weapons-explosions.yaml.
SHOCKWAVE = {
    "iskanderexplosion": {"max_radius": 4096, "spread": 1024,
                          "falloff": [100, 80, 60, 40, 25, 12, 5]},   # WE:536-543
    "himarsexplosion":   {"max_radius": 2560, "spread": 1024,
                          "falloff": [100, 70, 45, 25, 10]},          # WE:573-580
}


def lerp(a, b, num, den):
    return a + (b - a) * num // den


def falloff_at(distance, spread, table):
    """int2.Lerp over nodes at i*spread (ShockwaveDamageWarhead.GetDamageFalloff)."""
    if spread <= 0:
        return table[0]
    for i in range(len(table)):
        outer = i * spread
        if distance <= outer:
            if i == 0:
                return table[0]
            inner = (i - 1) * spread
            return lerp(table[i - 1], table[i], distance - inner, outer - inner)
    return 0


def apply_mods(value, mods):
    for m in mods:
        value = value * m // 100
    return value


def pen_reduce(damage, penetration, thickness):
    if thickness != 0 and penetration - thickness < 0:
        return damage * penetration // thickness
    return damage


def collateral_at(weapon_key, weapon, distance, thickness, armor_type):
    total = 0
    for wh in weapon["warheads"]:
        if wh["type"] == "TargetDamageWarhead":
            continue  # designated target only
        valid = wh["valid_targets"]
        if "Infantry" in valid and "Ground" not in valid:
            continue  # anti-infantry sub-warhead; vehicles only here

        if wh["type"] == "ShockwaveDamageWarhead":
            sw = SHOCKWAVE[weapon_key]
            if distance > sw["max_radius"]:
                continue
            pct = falloff_at(distance, sw["spread"], sw["falloff"])
        else:
            spread = wh["spread"] or 0
            table = wh["falloff"] or [100]
            pct = falloff_at(distance, spread, table)

        if pct <= 0:
            continue
        d = pen_reduce(wh["damage"], wh["penetration"], thickness)
        mods = [pct]
        if wh["versus"] and armor_type in wh["versus"]:
            mods.append(wh["versus"][armor_type])
        total += apply_mods(d, mods)
    return total


def main():
    stats = json.load(open(STATS))
    isk = stats["weapons"]["iskanderexplosion"]
    him = stats["weapons"]["himarsexplosion"]

    # Representative bystander: an IFV-class vehicle (Medium/15, 14000 HP),
    # and a main battle tank (Heavy/700, 28000 HP).
    cases = [("IFV  Medium/15 14000hp", 15, "Medium", 14000),
             ("MBT  Heavy/700 28000hp", 700, "Heavy", 28000)]

    for label, thickness, armor, hp in cases:
        print(f"\n{label} -- collateral damage by distance from impact")
        print(f"  {'dist':>6} {'cells':>6} {'ISK':>8} {'HIM':>8} {'ratio':>7}"
              f"  {'ISK %hp':>8} {'HIM %hp':>8}")
        print("  " + "-" * 62)
        for dist in (0, 512, 1024, 1536, 2048, 2560, 3072, 4096):
            di = collateral_at("iskanderexplosion", isk, dist, thickness, armor)
            dh = collateral_at("himarsexplosion", him, dist, thickness, armor)
            ratio = f"{di/dh:.2f}" if dh else ("inf" if di else "-")
            print(f"  {dist:>6} {dist/1024:>6.1f} {di:>8} {dh:>8} {ratio:>7}"
                  f"  {100*di/hp:>7.1f}% {100*dh/hp:>7.1f}%")

        # Outer radius at which collateral still exceeds 10% of the bystander's HP.
        def reach(key, w):
            last = 0
            for dist in range(0, 5000, 32):
                if collateral_at(key, w, dist, thickness, armor) >= hp * 0.10:
                    last = dist
            return last
        ri = reach("iskanderexplosion", isk)
        rh = reach("himarsexplosion", him)
        area = (ri * ri / (rh * rh)) if rh else float("inf")
        print(f"  reach at >=10% HP:  ISK {ri} ({ri/1024:.2f}c)   "
              f"HIM {rh} ({rh/1024:.2f}c)   area ratio {area:.2f}x")


if __name__ == "__main__":
    main()
