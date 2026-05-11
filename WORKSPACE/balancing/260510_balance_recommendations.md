# WW3MOD Balance Recommendations — 2026-05-10 (revised 2026-05-11)

> Pruned 2026-05-11: items that were already fixed, disproven, or apply-and-leave have been removed wholesale per "no correction notes that just bloat the doc". What's left is still actionable.

## TL;DR — open balance items

| # | Pri | Item | Action |
|---|---|---|---|
| R-01 | [M] | Paladin Burst 3 vs Giatsint Burst 1 | **Re-validate.** Post-AutoTarget-fix `arty-1v1` had Giatsint shut out Paladin 100%/0%. Pre-fix verdict (Paladin alpha-strike) is no longer trustworthy. Run multi-seed before any change. |
| R-03 | [H] | Bradley 1 500 vs BMP-2 1 300 | Same effective DPS, BMP-2 carries +1 infantry, BMP-2 wins 1v1 by tempo. **Set BMP-2 cost to 1 400** (or both to 1 400). |
| R-04 | [H] | Stryker SHORAD 2 500 vs Tunguska 1 700 | NATO pays +800 for two Hellfires on an AA platform. **Drop Stryker SHORAD to 2 000.** |
| R-05 | [H] | F-16 400 HP vs MiG-29 550 HP | +37% MiG survivability at same cost & weapons. **Bump F-16 to 500 HP.** Add a `test-balance-fighter-1v1` autotest before locking. |
| R-06 | [H] | TOS-1A HP 20 000 (2× peer MLRS) | Thermobaric platform with 2× the HP of M270/Grad. **Drop to 14 000** (or cost up to 2 500). |
| R-07 | [H] | Tunguska duplicate Health field | Two `Health:` blocks in YAML; second (8 000) wins. **Audit + remove dead block.** |
| R-09 | [M] | Iskander 2-shot vs HIMARS 6-shot | HIMARS has 56% more total damage at same cost. **Iskander Magazine 2 → 3.** |
| R-10 | [M] | Mi-24 Hind 4 000cr is a strong sleeper | HP/credit 50% better than Apache. **Watch in playtest.** |
| R-11 | [L] | TECN armor None vs Kevlar | **Set to Kevlar.** Trivial. |
| **MBT cost re-think** | **[!]** | **NEW** | Post-AutoTarget-fix tank duels: T-90 wins 1v1, 4v4, AND 2 BMP-2 beats 1 Abrams at 77% HP. Abrams 2 500 vs T-90 2 400 cost gap is no longer earned. Decision needed before any cost edit. |
| B-02 | [L] | Infantry `Inherits:` vs `Inherits@BaseUnit:` mismatch | Probably the cause of the deterministic rifle-mirror asymmetry. Cosmetic but inconsistent. Normalisation sweep recommended. |
| T-02 | [M] | Add `test-balance-*` to v1 release gate | Tests are committed; just need them in the pre-ship checklist. |

## Open questions for you

1. **Faction asymmetry intent** — TOS-1A gives Russia a thermobaric MLRS with no NATO counterpart. Document as design or close the gap?
2. **25mm/30mm vs heavy armor** — keep autocannons unable to crack MBTs (current state, IRL-correct), or soften the cliff?
3. **Add `test-balance-*` to v1 release gate** — yes/no for the pre-ship checklist?

---

## Methodology

For each item:
1. Pulled real YAML stats via the live dashboard (`tools/combat-sim` post-refactor reads from `--dump-balance-json`).
2. Ran in-game `test-balance-*` autotests for combat outcomes.
3. Compared to a plausible-IRL outcome.
4. Wrote a recommendation if any source disagreed.

Combat-sim no longer simulates combat — it's a stat dashboard now. AUTOTEST is the only authority on combat outcomes.

---

## Recommendations — full reasoning

### R-01 — Paladin Burst 3 (re-validate)

**Current YAML:** Paladin (M109) fires `Burst: 3, BurstDelays: 120, BurstWait: 240`. Giatsint fires `Burst: 1, BurstWait: 180`. Both 15 000 dmg / 1 000 pen / TopAttack.

**Sustained DPS:** Paladin ~94 dmg/tick, Giatsint ~83. NATO +12.5% sustained.

**Test verdict (pre-AutoTarget-fix, not trustworthy):** Paladin won 50.5s @ 68% HP.
**Test verdict (post-AutoTarget-fix):** Giatsint won 19.4s @ 100% HP.

The flip is too large to be RNG. With balanced AutoTarget on both sides, Giatsint's faster cycle (180 vs 240) lets it land its shells first. **Burst 3 may be fine; the perceived NATO arty advantage may have been an artifact.** Decide after a multi-seed re-run with the categorical-priority fix in place.

### R-03 — Bradley vs BMP-2 cost gap

Bradley 1 500cr, BMP-2 1 300cr. Same effective ATGM DPS (Bradley 2/1100t pairs ≈ BMP-2 1/500t singles). Same autocannon penetration. BMP-2 carries +1 infantry. In-game `ifv-1v1` has BMP-2 winning at 1% HP (post-fix) — near mirror, BMP edge. The 200cr gap isn't earned.

**Recommendation:** **BMP-2 cost 1 300 → 1 400** (or both to 1 400).

### R-04 — Stryker SHORAD overpriced

Stryker SHORAD 2 500, Tunguska 1 700. Both are AA platforms with 25/30mm + SAM. Stryker adds 2 Hellfires for the +800 cost. Bradley (1 500) + AA-infantry (300) = 1 800 covers similar ground.

**Recommendation:** **Stryker SHORAD cost 2 500 → 2 000.**

### R-05 — F-16 vs MiG-29 HP gap

F-16 400 HP, MiG-29 550 HP — same cost (6 000), same weapons (AIM ×6 + 20mm). MiG +37% survivability for free.

**Recommendation:** **F-16 HP 400 → 500.** Write a `test-balance-fighter-1v1` autotest first to confirm direction.

### R-06 — TOS-1A HP 2× peer MLRS

M270 10 000 HP @ 1 800cr, Grad 10 000 HP @ 1 500cr, TOS-1A 20 000 HP @ 2 000cr. TOS has 2× HP of peer platforms. Real TOS has reputation for catastrophic crew loss (thermobaric reload bay), so high HP is unrealistic.

**Recommendation:** **TOS-1A HP 20 000 → 14 000** (or cost up to 2 500 if you want to keep the durability).

### R-07 — Tunguska duplicate Health field

`vehicles-russia.yaml` has two `Health:` blocks on Tunguska. The second (8 000) wins via override; the first (14 000) is dead code. Either intentional override or stale edit.

**Recommendation:** **read the block, decide which is correct, delete the other.** If 8 000 is intentional, Tunguska becomes the most fragile AA platform (Stryker at 14 000) — that's a balance call.

### R-09 — Iskander Magazine 2 vs HIMARS 6

Both 6 000cr, top-end ballistic missile platforms. HIMARS 6 missiles × (5 000 + 8 000 spread) = 78 000 total potential. Iskander 2 × (10 000 + 15 000) = 50 000. HIMARS has +56% raw firepower and 3× tactical flexibility.

**Recommendation:** **Iskander Magazine 2 → 3** (or per-shot damage to 15 000 + 18 000 spread).

### R-10 — Mi-24 Hind cost

Mi-24 4 000cr, HP 800, Heavy/10 armor, 12.7mm + 80-rocket pod, cargo 8. Apache/Mi-28 6 000cr for similar HP and Hellfire×8 (precision AT). Hind HP/credit 200 vs Apache 133 — 50% better.

**Recommendation:** **leave for v1 unless playtest flags Hind dominating.** Hind is meant to be cargo+light-attack hybrid, not Apache-equivalent. IRL slower and less capable.

### R-11 — TECN armor

TECN (Technician) inherits `Armor: None` via `^TECN` → `^ArmedCivilian`. All other line infantry are Kevlar.

**Recommendation:** **change ^TECN's armor to Kevlar.** Trivial.

### NEW — MBT cost re-think (post-AutoTarget-fix)

Post-fix verdicts:
- `tank-1v1`: T-90 won, 10s, 66% HP (pre-fix: Abrams won 9.3s @ 26%).
- `tank-mass` (4v4): 4×T-90 won, 1 surv @ 16% (pre-fix: 4×Abrams won, 1 surv @ 15%).
- `mbt-vs-2ifv`: 2×BMP-2 ($2 600) won, both at 77% HP (pre-fix: 1×Abrams won @ 60%).

The Abrams 2 500cr premium was riding on a test artifact. With balanced AutoTarget: T-90's faster cycle (BurstWait 110 vs 130) wins straight trades. Abrams' frontal armor advantage (Thickness 700 vs 280) doesn't matter when both guns Pen 800 → both overpen.

**Decision needed:** raise T-90 cost? Slow T-90 cycle? Lower Abrams cost? Increase Abrams BurstWait advantage some other way? This affects the whole MBT meta and should not be a snap call.

---

## Live data — autotest verdicts

```
test-balance-tank-1v1          T-90 won 10.0s @ 66%
test-balance-tank-mass         4xT-90 won 47.9s, 1/4 surv @ 16%
test-balance-ifv-1v1           BMP-2 won 6.2s @ 1%
test-balance-mbt-vs-2ifv       2xBMP-2 won 12.3s, both at 77%
test-balance-at-vs-t90         3xAT.inf won 8.2s, 3/3 surv @ 80%
test-balance-at-vs-abrams      3xAT.inf won 4.6s, 2/3 surv @ 67%
test-balance-arty-1v1          Giatsint won 19.4s @ 100% (Paladin shut out)
test-balance-heli-1v1          first-shooter wins 100%/0% — harness artifact, real-game mirror
test-balance-rifle-mirror      4xE3.RUS won 13.0s, 4/4 surv @ 95% — likely B-02 inheritance asymmetry
```

All deterministic per-seed. For RNG variance work, parameterise scenarios or add tests at multiple ranges.

## Faction parity snapshot

| Slot | NATO | Russia | Cost gap | Notes |
|---|---|---|---|---|
| Light recon | Humvee (450) | BTR-80 (600) | -150 NATO | Humvee softer, BTR-80 tougher. Humvee is land-only, BTR-80 amphibious — both correct. |
| APC | M113 (700) | BTR-80 (600) | +100 NATO | M113 carries 12 vs BTR's 8. Both amphibious. |
| IFV | Bradley (1 500) | BMP-2 (1 300) | +200 NATO | See R-03. |
| MBT | Abrams (2 500) | T-90 (2 400) | +100 NATO | See "MBT cost re-think". |
| SPH | Paladin (1 800) Burst:3 | Giatsint (1 800) Burst:1 | 0 | See R-01. |
| MLRS | M270 (1 800) 12rkt | Grad (1 500) 40rkt + TOS (2 000) | NATO 1 / RUS 2 | Russia gets a second MLRS (TOS) by design. |
| AA | Stryker SHORAD (2 500) | Tunguska (1 700) | +800 NATO | See R-04. |
| Ballistic | HIMARS (6 000) 6 shots | Iskander (6 000) 2 shots | 0 | See R-09. |
| Attack heli | Apache (6 000) | Mi-28 (6 000) + Hind (4 000) | 0 / NATO -4k tier | Mirror at top tier; Russia gets a bonus mid-tier (Hind). |
| Fighter | F-16 (6 000) | MiG-29 (6 000) | 0 | See R-05. |
| Transport heli | Chinook (2 000) | Halo (2 000) | 0 | Mirror after 04/04 cargo equalisation. |

## Cost-efficiency table (live YAML)

For up-to-date numbers, run:

```bash
./tools/combat-sim/scripts/dump-stats.sh   # refresh from YAML
node ./tools/combat-sim/build/index.js tier-cost
```

The dashboard prints HP/credit and DPS/credit grouped by cost. Updates automatically with any YAML edit.
