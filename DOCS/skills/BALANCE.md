# BALANCE — combat-sim driven tuning

**Trigger:** `BALANCE <unitA> <unitB>` for a duel comparison; `BALANCE <topic>` for broader tuning (e.g. `BALANCE artillery range falloff`, `BALANCE ammo costs T5`).

**Gives you:** data-driven tuning instead of vibes-based YAML edits. Wraps `tools/combat-sim/` to surface expected outcomes, damage per shot/sec/credit, and tier consistency.

**When *not* to use it:** balance changes you've already decided on with high confidence and just want to apply. Just edit the YAML.

---

## What I do

1. **Run combat-sim** at canonical ranges (close, medium, max) for the duel or matrix in question.
   ```bash
   cd tools/combat-sim && npm install && npx tsc      # if not built
   node build/index.js duel <a> <b> --range <r>       # 1v1
   node build/index.js run <scenario>                 # multi-unit
   node build/index.js stats <unitId>                 # unit details
   ```
2. **Surface** the relevant numbers:
   - Damage per shot / per second
   - Damage per credit (cost-efficiency)
   - Time-to-kill (TTK) at each range
   - Expected outcome (who wins, by how much HP)
   - For tier work: tier vs cost vs damage table
3. **Recommend tuning** with concrete YAML changes if values feel off (e.g. "T5 weapons average 80 damage/credit; this one is at 145 — drop SupplyValue 80→50 to align").
4. **Apply changes** if the user approves.
5. **Re-run sim post-change** to verify the tune lands where intended.

## What combat-sim already models

Per `tools/combat-sim/`: damage (penetration, directional armor, range falloff, AoE), weapon firing cycles, suppression (infantry 10-tier / vehicle 5-tier), formations. Phase 1 stats are hardcoded; Phase 2 will auto-load from YAML. Useful for relative comparisons even if absolute numbers drift slightly from in-game.

## When data conflicts with feel

Combat-sim says a fight should be 50/50 but the user says it feels lopsided in playtest? Trust the playtest — the sim is missing something (positioning, suppression dynamics, AI quirks). File as a TRIAGE item and dig in.
