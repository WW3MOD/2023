#!/usr/bin/env python3
"""Humvee HP halving (8000 -> 4000): where does it cross a shots-to-kill boundary?

Static arithmetic replicating DamageWarhead.InflictDamage (DamageWarhead.cs:200-247)
via the helpers already in penetration-sweep.py. Not the engine, no game launch.
Integer division is deliberate — the engine does int math.

A direct hit is assumed (distance 0 from the hitshape edge), so SpreadDamage warheads
take falloff[0]. That is the best case for the shooter and therefore the conservative
reading for "how much easier does the humvee die".
"""
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("pensweep", os.path.join(HERE, "penetration-sweep.py"))
pen = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pen)

OLD_HP, NEW_HP = 8000, 4000
TARGET_TYPES = {"Ground", "Vehicle", "Light"}


def applies(wh):
    vt, it = set(wh["valid_targets"]), set(wh["invalid_targets"])
    if it & TARGET_TYPES:
        return False
    return bool(vt & TARGET_TYPES)


def per_hit(w, armor):
    """Damage one landed shot does to the humvee, summed over applicable warheads."""
    facing = "top" if w.get("top_attack") else ("bottom" if w.get("bottom_attack") else "front")
    thickness = pen.facing_thickness(armor, facing)
    total, parts = 0, []
    for wh in w["warheads"]:
        if not applies(wh):
            continue
        dmg = pen.avg_raw(wh)
        if wh["falloff"]:
            dmg = dmg * wh["falloff"][0] // 100          # direct hit -> index 0
        dmg = pen.pen_reduce(dmg, wh["penetration"], thickness)
        versus = wh.get("versus") or {}
        for t in TARGET_TYPES:
            if t in versus:
                dmg = dmg * versus[t] // 100
        total += dmg
        parts.append(f"{wh['type'].replace('DamageWarhead','')}:{dmg}")
    return total, facing, thickness, parts


def stk(dmg, hp):
    return None if dmg <= 0 else -(-hp // dmg)          # ceil


def main():
    W, A = pen.load()
    hum = A["humvee"]
    armor = hum["armor"]
    print(f"humvee armor: type={armor.get('type')} thickness={armor['thickness']} "
          f"distribution={armor['distribution']}  HP {OLD_HP} -> {NEW_HP}\n")

    idx = pen.armament_index(A, True)
    rows = []
    for k, w in W.items():
        if k.startswith("^") or k.lower() not in idx:
            continue
        dmg, facing, thick, parts = per_hit(w, armor)
        if dmg <= 0:
            continue
        burst = max(1, w.get("burst") or 1)
        s_old, s_new = stk(dmg, OLD_HP), stk(dmg, NEW_HP)
        b_old, b_new = stk(dmg * burst, OLD_HP), stk(dmg * burst, NEW_HP)
        rows.append((k, dmg, burst, s_old, s_new, b_old, b_new, facing, thick,
                     ",".join(parts), sorted(set(c.split(":")[0] for c in idx[k.lower()]))))

    rows.sort(key=lambda r: -r[1])
    hdr = f"{'weapon':<24}{'dmg/hit':>8}{'brst':>5}{'hits@8000':>10}{'hits@4000':>10}{'bursts 8k>4k':>14}  carriers"
    print(hdr)
    print("-" * len(hdr))
    for k, dmg, burst, so, sn, bo, bn, facing, thick, parts, carriers in rows:
        mark = "  <== CROSSES" if so != sn else ""
        print(f"{k:<24}{dmg:>8}{burst:>5}{so:>10}{sn:>10}{str(bo)+' > '+str(bn):>14}  "
              f"{','.join(carriers[:3])}{mark}")

    print("\nSTK boundary crossings (hits needed drops):")
    for k, dmg, burst, so, sn, *_ , carriers in rows:
        if so != sn:
            print(f"  {k:<24} {so} -> {sn} hits   ({dmg}/hit)  carried by {','.join(carriers[:4])}")
    if not any(r[3] != r[4] for r in rows):
        print("  (none)")

    print("\nNo change in hits-to-kill (already lethal, or still needs the same count):")
    for k, dmg, burst, so, sn, *_ in rows:
        if so == sn:
            print(f"  {k:<24} {so} hit(s) both before and after ({dmg}/hit)")


if __name__ == "__main__":
    main()
