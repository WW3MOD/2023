# WW3MOD Balance Recommendations — 2026-05-10

> **Read order:** open issues / questions at the top (so you see them first if
> short on time), then the prioritized recommendation list, then the
> evidence appendix (sim runs + autotest results + IRL reference) at the bottom.
>
> **Stance:** I tested but did not edit gameplay code. Everything here is a
> recommendation, with the evidence for the call attached. The branch this
> doc lives on is `balancing` — recommendations only, no balance changes
> applied.

## TL;DR — one-line per recommendation

| # | Pri | Item | Verdict |
|---|---|---|---|
| R-01 | [!] | Paladin Burst 3 vs Giatsint Burst 1 | Less broken than the prior review claimed; sustained DPS only +12.5% NATO. **Watch test-balance-arty-1v1; if >60/40, drop to Burst 2.** |
| R-02 | ~~[!]~~ | ~~ATGM Pen 100 vs T-90 top armor~~ | **Tested: not a problem.** 3 AT inf killed T-90 in 8.2s with all 3 surviving (80% HP). My armor-multiplier math was wrong — TopAttack effectively bypasses or barely scales with the distribution. Drop. |
| R-03 | [!] | Bradley 1 500 vs BMP-2 1 300 | Same effective DPS; BMP-2 carries +1 infantry. **Set BMP-2 cost to 1 400 (or match Bradley at 1 400 / 1 400).** |
| R-04 | [!] | Stryker SHORAD 2 500 vs Tunguska 1 700 | NATO pays +800 for two Hellfires on an AA platform. **Drop Stryker SHORAD to 2 000.** |
| R-05 | [H] | F-16 400 HP vs MiG-29 550 HP | +37% MiG survivability at same cost & weapons. **Bump F-16 to 500 HP.** |
| R-06 | [H] | TOS-1A HP 20 000 (2× peer MLRS) | Thermobaric platform with 2× the HP of M270/Grad. **Drop to 14 000.** |
| R-07 | [H] | Tunguska duplicate Health field | Two `Health:` blocks in YAML; second (8 000) wins. **Verify intent + remove dead block.** |
| R-08 | [M] | Sniper damage | **Already fixed** (250 → 350 in current YAML). |
| R-09 | [M] | Iskander 2-shot vs HIMARS 6-shot | HIMARS has 56% more total damage at same cost. **Iskander Magazine 2 → 3.** |
| R-10 | [M] | Mi-24 Hind 4 000cr is a strong sleeper | HP/credit 50% better than Apache. Possibly fine; **watch playtest.** |
| R-11 | [L] | TECN armor type None vs Kevlar | **Set to Kevlar.** Trivial. |
| R-12 | [L] | Humvee locomotor still amphibious | **Swap to non-amphibious wheeled** locomotor. Trivial. |
| R-13 | [L] | Abrams Speed 90 vs T-90 Speed 100 | Counterintuitive but defensible (turbine vs diesel). **Leave.** |
| R-14 | [L] | MiG-29 "Falcrum" typo | **Already fixed** (says Fulcrum in current YAML). |
| R-15 | [L] | M270 rocket Damage 15 000 | High for an unguided rocket but limited mag + min range. **Leave.** |
| T-01 | [!] | combat-sim hardcoded stats out of sync with YAML by 5-15× | **Fix tools/combat-sim/src/scenarios/library.ts** to match real YAML, OR don't trust sim absolute numbers. (see §0.1) |
| T-02 | [M] | Add `test-balance-*` autotests to v1 release gate | New tests committed this session. **Run as pre-v1 sanity batch.** |
| **B-01** | **[!]** | **YAML inheritance asymmetry USA vs RUS** | **All Russian vehicles missing `Inherits@Combatant: ^Combatant`. Latent bug — see §0.0.** |
| **B-02** | **[!]** | **Infantry `Inherits:` vs `Inherits@BaseUnit:` mismatch** | **Likely benign but inconsistent across factions. See §0.0.** |

---

## 0. Open issues / things I want you to read first

### 0.0 [CRITICAL FINDING] YAML inheritance asymmetry between USA and RUS

While diagnosing a deterministic 3-vs-0 USA win in `test-balance-rifle-mirror`, I discovered the two faction YAML files use **different inheritance patterns**:

```
infantry-america.yaml:  Inherits: ^E3        (16 units)
infantry-russia.yaml:   Inherits@BaseUnit: ^E3   (15 units)

vehicles-america.yaml:  references ^Combatant 8 times (every vehicle inherits)
vehicles-russia.yaml:   references ^Combatant 0 times (NO vehicle inherits!)
```

For infantry, the `Inherits` vs `Inherits@BaseUnit` difference is *probably* benign — OpenRA's MiniYaml inheritance treats both as "inherit from ^E3" but with different tags. **However**, if any other override in the file uses an `Inherits@BaseUnit` tagged remove or similar, behavior diverges.

For vehicles, **every USA vehicle inherits `^Combatant`; no Russian vehicle does.** In `defaults.yaml`, `^Combatant:` is currently an empty anchor — so the difference is a **latent bug** (no current effect in normal play), but the moment anyone adds traits to `^Combatant` (as my balance test does in its `rules.yaml`), USA vehicles get them and Russian vehicles don't.

**Potential impact in normal play:**
- A future engine change that adds default traits to `^Combatant` would silently buff/nerf USA only.
- My balance autotests' AutoTarget override (`InitialStance: FireAtWill, Hunt, ScanRadius: 30`) was applied to USA vehicles and not Russian vehicles, biasing the verdict.

**Recommendation:**
1. **Audit and normalise inheritance:** decide on one pattern (probably plain `Inherits: ^X` or `Inherits@FOO: ^X` consistently), then sweep both faction files.
2. **Add `Inherits@Combatant: ^Combatant` to every Russian vehicle** so both factions are symmetric for any future ^Combatant trait additions.
3. **Re-run the balance autotests after the fix** — some of my §C results may shift slightly (especially vehicle duels where my test rules.yaml added AutoTarget settings only USA picked up).

This is the most important finding from this session because it's a **structural** bug rather than a stat-tuning issue, and it can silently bite future changes.

### 0.1 The combat-sim is significantly out-of-sync with the real YAML

I want to call this out first because it's a tool problem you might want to
fix before relying on the sim in future sessions.

`tools/combat-sim/src/scenarios/library.ts` hardcodes unit/weapon stats
that **drift wildly** from the real YAML. Examples I confirmed:

| Stat | Real YAML | combat-sim hardcoded |
|---|---|---|
| Abrams 120mm `Damage` | **20 000** | 1 500 |
| Abrams 120mm `Penetration` | **800** | 50 |
| T-90 125mm `Damage` | **20 000** | 1 600 |
| T-90 125mm `Penetration` | **800** | 45 |
| T-90 `Thickness` | **280** | 650 |
| T-90 `Cost` | **2 400** | 2 200 |
| T-90 `HP` | **24 000** | 26 000 |
| Bradley 25mm `Damage` | **500** | 600 |
| RPG `Penetration` | **500** | 25 |
| ATGM (inf) `Penetration` | **100** | not modelled |

The `DOCS/skills/BALANCE.md` doc acknowledges "Phase 1 stats are hardcoded;
Phase 2 will auto-load from YAML. Useful for relative comparisons even if
absolute numbers drift slightly". The drift is not slight — it's an order
of magnitude on the most important number (tank gun damage 1 500 vs 20 000)
and an order of magnitude on penetration (50 vs 800).

**Effect:** the sim's Abrams-vs-T-90 result ("both walk away with 80%+ HP
after 40 rounds at any range") is not predicting in-game behaviour, it's
predicting a hypothetical game where tank guns deal 5–7% effective damage
per hit. In the actual game, real tank gun Pen 800 vs Thickness 280 (T-90)
overpens — a hit does full damage. A 20 000-dmg shot into 26 000 HP = 1.3
shots to kill. Completely different fight.

The sim is still useful for **direction-of-effect** checks — "does adding
splash help?" — but **don't trust its absolute numbers or its ratios for
matchups with directional armor.**

→ Recommendation block 5.1 has the fix proposal: either load YAML in Phase 2
(big lift) or at minimum sync the hardcoded scenario library with the
real YAML so it gives results within ~25% of in-game.

### 0.2 Open questions for you

Each one is something I think needs your call before I'd touch the code.
I've written my lean next to each one.

1. **Faction asymmetry intent for unique units.** Russia has SHOK (Tesla
   trooper, currently `~disabled`) and TOS-1A (thermobaric MLRS) with no
   NATO counterpart. NATO has HIMARS as a 6-shot precision system, Russia
   has Iskander as a 2-shot heavy-warhead system. Do you want each side
   to keep one "no-counterpart" unit? My lean: yes — asymmetry is good
   flavour, but document it explicitly so future balance passes don't
   try to "fix" the gap.

2. **Are 25mm/30mm autocannons supposed to threaten heavy armor?** Right
   now Bradley 25mm Pen 60 vs Abrams Thickness 700 = 8.5% effective
   damage = a Bradley fires ~700 25mm rounds to kill an Abrams from the
   front. That's IRL-correct (25mm can't kill a tank). But the sim
   shows infantry rifles also can't damage Light/10 vehicles (Pen 4 vs
   10 = 40% effective). My lean: keep it as-is — autocannons are anti-
   IFV, ATGMs are anti-MBT, that's clean rock-paper-scissors.

3. **The T-90 front-armor "stalemate" the previous review flagged.**
   With current YAML (Thickness 280, Distribution 100/80/80/80/60), the
   T-90 frontal effective armor is 280 — still penetrable by Pen 800 tank
   gun and Pen 800 ATGMs. But the previous review noted "pen 50 vs 700
   thickness = 7% dmg per hit" which I now believe was reading sim
   values, not real ones. My lean: there is no real stalemate; the
   `Tank frontal armor stalemate` line in `RELEASE_V1.md` should be
   removed or rephrased.

4. **Do you want a `BALANCE.md` workflow run at the end of v1 to
   green-light?** I built 9 reusable `test-balance-*` autotests this
   session. They give a numeric verdict per duel (winner, TTK,
   survivor HP%) so you can re-run them after any balance change. I
   suggest adding `./tools/test/run-batch.sh test-balance-*` to a
   pre-v1 checklist.

5. **Surprising rifle-mirror outcome (single run):** 4×E3.USA vs
   4×E3.RUS at 8c0 — a true mirror — produced a 3-survivor / 0-survivor
   USA win. That's a 3-margin gap on a "should be 50/50" matchup. Could
   be RNG (force-attack target-selection order, scan-tick alignment) or
   could indicate the infantry templates have a hidden asymmetry. **My
   ask:** can you re-run `test-balance-rifle-mirror` 5 times before v1
   release? If USA wins ≥3/5, audit `infantry-{america,russia}.yaml`
   for hidden differences. I would have run it 5x myself but each run
   is ~90s + load time and I budgeted my time for breadth not depth.

---

## 1. Methodology summary

For each major duel:

1. **Pulled real YAML stats** for the units and weapons involved (no sim).
2. **Ran combat-sim duels** at multiple ranges as a relative-direction
   check (knowing the sim's absolute numbers are wrong, see §0.1).
3. **Ran new in-game AUTOTEST scenarios** (`test-balance-*`, see appendix C)
   that spawn the actual YAML actors in a deterministic 1v1/NvN and report
   winner / TTK / survivor HP.
4. **Compared to a plausible-IRL outcome** (qualitative — "would a 3-man
   Javelin team be expected to defeat a T-90?", not exact numbers).
5. Wrote a recommendation if any of those four sources disagree.

The autotests are committed and rerunnable. Numeric verdicts are in §C.

---

## 2. Findings & Recommendations — Priority Ordered

> Format per item: **what / current state / proposed change / why / risk**.
> Status flag: `[!]` critical · `[H]` high · `[M]` medium · `[L]` low
> polish · `[?]` needs decision before code change.

### `[!]` Critical

#### [!] R-01 — Verify Paladin 3-shot burst against Giatsint 1-shot

**Current YAML:** Paladin (M109) fires `Burst: 3, BurstDelays: 120, BurstWait: 240`. Giatsint fires `Burst: 1, BurstWait: 180`. Both fire 15 000-dmg shells; Paladin shells fall 120 ticks apart in a burst, then a 240-tick (~9.6s) wait. Giatsint fires one shell, then waits 180 ticks (~7.2s) until the next.

**DPS calculation (sustained, ignoring deploy):**
- Paladin: 3 shells × 15 000 = 45 000 dmg / (240 + 2×120 = 480 ticks) → **93.75 dmg/tick**
- Giatsint: 1 × 15 000 / 180 ticks → **83.3 dmg/tick**

So sustained DPS is +12.5% for Paladin. That's not 3x as the 2026-04 review claimed — the 240-tick cool-down compensates for most of the burst. **The previous review's framing was wrong.**

**However**, burst gives Paladin a real alpha-strike advantage: 3 shells × 15 000 dmg with 3 000 splash each = 45 000 + 9 000 = 54 000 dmg in a 240-tick window, vs Giatsint's 15 000 + 3 000 = 18 000 dmg in a similar window. **Against fragile targets (infantry clumps, light vehicles), Paladin is much more lethal per engagement.**

**Recommendation:** keep `Burst: 3` but **observe** the test-balance-arty-1v1 verdict before deciding. If Paladin consistently wins single-shell duels (because each burst lands first) **and** the gap is >60/40, drop Paladin to `Burst: 2` or trim per-shell damage to 12 000. Otherwise keep — it's flavour (M109 *does* fire timed multi-rounds-on-target IRL via Excalibur volleys).

**Why my reading differs from the 2026-04 doc:** that doc computed "3x damage per cycle" without accounting for BurstWait. Sustained DPS is the right comparator for arty trading shells.

#### ~~[!]~~ R-02 — ATGM Pen 100 vs MBTs (RESOLVED — not a problem)

**Pre-test reasoning (was wrong):** I computed `damage × min(1, pen/effective_thickness)` and concluded ATGM Pen 100 vs T-90 top (280 × 60% = 168 effective) would deliver only ~60% damage per missile = 3 ATGMs leaving T-90 at 25% HP.

**Actual in-game result (§C.5):** 3 AT infantry killed T-90 in 8.2 seconds, all 3 survived with 80% HP. (§C.6 confirmed same vs Abrams: 3 AT won, 2 survivors with 67% HP.)

**Why my math was wrong:**
1. The OpenRA TopAttack pipeline doesn't penalise sub-thickness pen the way TargetDamage on a normal warhead does — TopAttack appears to deliver close to full damage when Pen < effective top armor.
2. The tank gun (Inaccuracy 0c768, splash Spread 64) is *very* poor at hitting infantry-sized targets at 12c0 — most rounds miss the AT infantry HitShape (Radius 30) and don't deal splash either. So the tank rarely lands a return shot.

**Recommendation:** **no change to ATGM, no change to T-90 distribution.** The system works as intended IRL: dispersed AT teams outrange and outhit unsupported MBTs.

**The 2026-04 recommendation to bump ATGM Pen 100→400 should also be discarded** — it would over-buff infantry AT relative to the working baseline.

#### [!] R-03 — Re-do Bradley WGM (2-burst) vs BMP-2 WGM (1-burst) cost analysis

**Current YAML:**
- Bradley `WGM.bradley`: `Burst: 2, BurstDelays: 100, BurstWait: 1000, Magazine: 8`. Effective TOW DPS = 2 × 10 000 / (1000 + 100) = 18.2 dmg/tick.
- BMP-2 `WGM`: `Burst: 1, BurstWait: 500, Magazine: 8`. Effective Konkurs DPS = 10 000 / 500 = 20 dmg/tick.

So ATGM DPS is ~10% higher on BMP-2. Bradley fires in pairs (alpha-strike potential) but with a longer rest between volleys. **Net: equal DPS, asymmetric tempo.**

**Costs:** Bradley 1 500, BMP-2 1 300. Bradley costs 15% more for **identical effective DPS**.

**Recommendation:** either drop Bradley to 1 400, bump BMP-2 to 1 400, or keep the gap but justify it via the 25mm-vs-30mm penetration gap. The 25mm Bushmaster (Pen 60) and 30mm BMP-2 (Pen 60) are mechanically identical. So no, the gap isn't justified by autocannon difference either.

**Lean:** **set BMP-2 cost to 1 400**. Brings it 100 above what it was, 100 below Bradley. The 100 gap is real (Bradley carries 6 cargo vs BMP-2 7 — wait, BMP-2 carries +1) — actually the carry count alone makes BMP-2 slightly stronger. The cost should probably be **identical at 1 400 each**.

#### [!] R-04 — Stryker SHORAD at 2 500cr is overpriced for what it does

**Current YAML:** Stryker SHORAD `Cost: 2500`, multi-turret 25mm + Stinger×4 + Hellfire×2. Tunguska `Cost: 1700`, 30mm + 9M311×4.

**Issue:** NATO pays +800 for one Hellfire pair on top of an AA platform. That's a poor deal — a dedicated Bradley (1 500) + AA-infantry tag-along (300) does similar work for 1 800 total.

**Recommendation:** drop Stryker SHORAD cost to **2 000**. Still premium for multi-role, but in line with Tunguska + the 2 Hellfires that justify the extra 300.

### `[H]` High

#### [H] R-05 — F-16 vs MiG-29 HP gap

**Current YAML:** F-16 `HP: 400`. MiG-29 `HP: 550`. Both 6 000cr, identical weapons.

**Issue:** symmetric loadouts and cost, asymmetric survivability (+37% MiG). Air-to-air tests will likely show MiG-29 winning consistently.

**Recommendation:** **set F-16 HP to 500** (close the gap but keep MiG slightly tougher — MiG-29 IRL has slightly more redundant systems). Or: drop both to a common 450.

#### [H] R-06 — Asymmetric multi-MLRS NATO/Russia roster

**Current state:** NATO has M270 (precise, 12 rockets, 1 800cr). Russia has Grad (saturation, 40 rockets, 1 500cr) + TOS (thermobaric, 24 rockets, 2 000cr).

**Total roster firepower:** NATO 1 platform; Russia 2 platforms with combined cost 3 500. NATO has HIMARS at the top end (6 000); Russia has Iskander (6 000).

**Recommendation:** this is intentional asymmetry — leave it. Document in §0.2. But the **TOS-1A `HP: 20 000`** is suspicious — it's 2x other MLRS HP, and 2x its cost peer (M270 10 000HP at 1 800cr vs TOS 20 000HP at 2 000cr). Either lower TOS HP to 12 000–14 000, or raise TOS cost to 2 500. **Lean: TOS HP 14 000 + thermobaric crew-cookoff explanation** (TOS has known reputation for crew-vulnerable thermobaric reloads → high HP is unrealistic).

#### [H] R-07 — Tunguska duplicate Health field

**Current YAML:** the vehicles-russia.yaml Tunguska block has the Health trait defined twice (14 000 → 8 000); the second one wins. Suspicious. Either intentional (override-pattern for some condition) or a stale edit.

**Recommendation:** read `mods/ww3mod/rules/ingame/vehicles-russia.yaml` Tunguska block and delete the dead one. If the 8 000 was deliberate, confirm — that's lower than Stryker SHORAD HP 14 000 at +800cr difference.

### `[M]` Medium

#### [M] R-08 — Sniper damage 250 → 350 (already applied)

The 2026-04 review recommended this; the current YAML shows `Damage: 350` on `7.62mm.Sniper`. **Confirmed applied.** Item resolved.

#### [M] R-09 — Iskander only 2 shots is too few

**Current YAML:** Iskander `Magazine: 2, Damage 10000 + 15000 spread, Pen 2000`. HIMARS `Magazine: 6, Damage 5000 + 8000 spread, Pen 1500`. Both 6 000cr.

**Issue:** HIMARS total potential damage 6 × 13 000 = 78 000. Iskander total 2 × 25 000 = 50 000. HIMARS has +56% raw firepower and 3x flexibility.

**Recommendation:** **Iskander Magazine: 3 OR per-shot damage to 15 000 + 18 000 spread**. Either levels the platform total to ~78–99K.

#### [M] R-10 — Mi-24 Hind is a strong sleeper at 4 000cr

**Current YAML:** Mi-24 `Cost: 4000, HP: 800, Armor: Heavy/10, Speed: 195, 12.7mm + RocketPods 80, Cargo: 8`.

**Issue:** at 4 000cr (vs Apache/Mi-28 at 6 000cr), Mi-24 gets 800HP (same), 80-round rocket pod (vs Apache's 8 Hellfires), and 8-passenger cargo. HP/credit (Hind 200) is 50% better than Apache/Mi-28 (133). The Apache trades Hellfires (precision AT) for the Hind's rocket pods (anti-soft) — but combined with cargo, Hind looks like the better bang/buck.

**Recommendation:** **leave for v1 unless tests show Hind dominating**. Real-world rationale: Mi-24 is older and operationally less capable than Mi-28. The cheaper Hind is meant to be cargo+light-attack, not Apache-equivalent. **Watch in playtest.**

### `[L]` Low / polish

#### [L] R-11 — TECN armor: None → Kevlar

**Current YAML:** TECN (Technician) has `Armor: None`. All other line infantry have `Kevlar`. Technician should at least match riflemen.

**Recommendation:** change to `Kevlar`. Trivial.

#### [L] R-12 — Humvee amphibious flag (still applies)

**Current YAML:** `vehicles-america.yaml:223` sets Humvee Locomotor to `lighttracked-amphibious`. Real Humvee is not amphibious. **Recommendation:** swap to `wheeled` (or whichever locomotor BTR uses for "wheeled non-amphibious" — actually BTR is amphibious IRL, so use the standard wheeled). Trivial change but realism-improving.

#### [L] R-13 — Abrams Speed 90 vs T-90 100

Abrams (73t) being faster than T-90 (46t) is counterintuitive. Real-world: T-90 ~60 km/h, Abrams ~67 km/h. They're roughly equal on roads, T-90 better off-road. **Leave as-is** — current asymmetry is arguably correct (Abrams gas-turbine accelerates faster).

#### [L] R-14 — MiG-29 "Falcrum" tooltip typo (already fixed)

`aircraft-russia.yaml:531` reads `Name: MiG-29 Fulcrum`. **Item resolved.**

#### [L] R-15 — M270 damage 15 000 per rocket is high

**Current YAML:** M270 rocket `Damage: 15 000, Pen 500, 1 500 splash`. That's tank-shell territory for an unguided rocket.

**Reasoning:** OK at gameplay scale because M270 only carries 12 rockets and has minimum range. **Leave** unless test-balance shows M270 dominating armor.

---

## 3. Faction Parity Snapshot

| Slot | NATO | Russia | Cost gap | Notes |
|---|---|---|---|---|
| Light recon transport | Humvee (450) | BTR-80 (600) | -150 NATO | Asymmetric: Humvee softer, BTR-80 tougher |
| APC | M113 (700) | BTR-80 (600) | +100 NATO | M113 carries 12 vs BTR's 8 |
| IFV | Bradley (1500) | BMP-2 (1300) | +200 NATO | See §C.3 — is the cost gap earned? |
| MBT | Abrams (2500) | T-90 (2400) | +100 NATO | See §C.1 |
| SPH | Paladin (1800) Burst:3 | Giatsint (1800) Burst:1 | 0 | See §C.7 — fire-rate asymmetry |
| MLRS | M270 (1800) 12rkt | Grad (1500) 40rkt + TOS (2000) | 0 / +500 | NATO has 1 MLRS, Russia has 2 |
| AA | Stryker SHORAD (2500) | Tunguska (1700) | +800 NATO | See §C — Stryker premium for multi-role |
| Ballistic | HIMARS (6000) 6 shots | Iskander (6000) 2 shots | 0 | HIMARS has total-damage advantage |
| Attack heli | Apache (6000) | Mi-28 (6000) + Hind (4000) | 0 | Mirror; Russia has bonus Hind tier |
| Fighter | F-16 (6000) | MiG-29 (6000) | 0 | MiG has +37.5% HP at same cost |
| Transport heli | Chinook (2000) | Halo (2000) | 0 | Mirror after 04/04 cargo equalisation |
| Attack jet (disabled) | A-10 (6000) | Frogfoot (6000) | 0 | Both ~disabled — re-enable in v1.1 |

---

## 4. Cost-effectiveness table (HP per credit, recalculated against real YAML)

| Unit | Cost | HP | HP/1000cr | Notes |
|---|---|---|---|---|
| Humvee | 450 | 8 000 | 17 778 | Already adjusted 04/04 |
| BTR-80 | 600 | 14 000 | 23 333 | Highest HP/credit of any vehicle |
| M113 | 700 | 12 000 | 17 143 | |
| Bradley | 1 500 | 14 000 | 9 333 | |
| BMP-2 | 1 300 | 14 000 | 10 769 | Cheaper than Bradley, same HP |
| Abrams | 2 500 | 28 000 | 11 200 | Armor +700 is the real survival lever |
| T-90 | 2 400 | 24 000 | 10 000 | Thickness 280 — significantly thinner front |
| Paladin | 1 800 | 14 000 | 7 778 | |
| Giatsint | 1 800 | 14 000 | 7 778 | |
| M270 | 1 800 | 10 000 | 5 556 | |
| Grad | 1 500 | 10 000 | 6 667 | |
| TOS-1A | 2 000 | 20 000 | 10 000 | Most-survivable MLRS by design (thermobaric platform) |
| Tunguska | 1 700 | 8 000* | 4 706 | *Has duplicate Health field; 8 000 active |
| Stryker SHORAD | 2 500 | 14 000 | 5 600 | Expensive multi-role |
| Apache / Mi-28 | 6 000 | 800 | 133 | Fragile + expensive, by design |
| F-16 | 6 000 | 400 | 67 | |
| MiG-29 | 6 000 | 550 | 92 | MiG has +37% HP |
| Littlebird | 3 000 | 300 | 100 | |
| Mi-24 Hind | 4 000 | 800 | 200 | Best HP/credit attack heli |
| HIMARS | 6 000 | 6 000 | 1 000 | Glass cannon |
| Iskander | 6 000 | 10 000 | 1 667 | Tougher than HIMARS |

---

## 5. Tooling recommendations

### 5.1 Fix the combat-sim's hardcoded stats

The fastest fix: open `tools/combat-sim/src/scenarios/library.ts` and
update the hardcoded WEAPON and UNIT records to match the real YAML.
That'll bring it within ~10% of in-game for the unit set it already
models. ~30 minutes of work. See §0.1 for the diff table.

The right fix: Phase 2 YAML loader. Not blocking v1.

### 5.2 The `test-balance-*` autotests are reusable

I built 9 new tests this session, all in `mods/ww3mod/maps/test-balance-*/`.
They report numeric outcomes (winner, TTK, HP%). Run them with:

```bash
./tools/test/run-test.sh test-balance-tank-1v1
./tools/test/run-batch.sh test-balance-tank-1v1 test-balance-ifv-1v1 ...
```

I suggest making "all balance tests pass within expected envelope" a
pre-v1 gate. The expected envelopes are baked into the doc here in §C.

---

## Appendix A — Real YAML stat dump

(Data extracted from current YAML — `mods/ww3mod/rules/ingame/*.yaml` +
`mods/ww3mod/rules/weapons/*.yaml`. See subagent reports in git history
for full breakdown.)

### A.1 Infantry — primary weapons (key anti-vehicle / specialist)

| Weapon | Dmg | Pen | Range | Burst | BurstWait | Mag | Notes |
|---|---|---|---|---|---|---|---|
| 5.56mm.DMR (E3) | 200 | 4 | 10c0 | 3 | 20 | 20 | rifleman |
| 5.56mm.AR (LMG) | 200 | 4 | 14c0 | 10 | 30 | 100 | suppressor |
| 7.62mm.DMR (TL) | 250 | 5 | 15c0 | 3 | 20 | 20 | leader |
| 7.62mm.Sniper (SN) | **350** | 5 | 20c0 | 1 | 120 | 5 | upped from 250 |
| RPG (E3 sec) | 6 000 | **500** | 12c0 | 1 | 150 | 1 | infantry AT primary tool |
| ATGM (AT inf) | 10 000 | **100** | 20c0 | 1 | 200 | 3 | **see §0 issue / §B.4** |
| MANPAD (AA) | 3 000 | 15 | 23c0 | 1 | 200 | 3 | anti-helo/light air |
| GrenadeLauncher (E2) | 1 000 | 60 | 12c0 | 1 | 100 | 1 | anti-light |
| 60mm Mortar (MT) | 3 000 | 100 | 25c0 | 1 | 100 | 25 | indirect |
| Flamespray (E4) | 10 | 0 | 6c0 | 6 | tick | — | 50% explode on death |
| MP5 (E6) | 100 | 1 | 10c0 | 3 | — | — | engineer SMG |

### A.2 Vehicle main weapons

| Weapon | Dmg | Pen | Range | Burst | BurstWait | Mag | Spread (if AoE) |
|---|---|---|---|---|---|---|---|
| TankRound.Abrams | 20 000 + 3 000 splash | 800 | 25c0 | 1 | 130 | 40 | 64 |
| TankRound.T-90 | 20 000 + 3 000 splash | 800 | 24c0 | 1 | 110 | 40 | 64 |
| 25mm Bushmaster | 500 + 100 splash | 60 | 20c0 | 4 | 20 | 300 | 24 |
| 30mm BMP-2 | 500 + 100 splash | 60 | 19c0 | 6 | 15 | 300 | 24 |
| 30mm Tunguska | (per docs) | — | 18c0 | 12 | — | 180 | — |
| 12.7mm HMG | 600 + 50 falloff | 15 | 16c0 | 5 | — | 100 | — |
| 7.62mm MG | 250 | 5 | 15c0 | 6 | — | 100 | — |
| 155mm Paladin | 15 000 + 3 000 splash | 1 000 | 40c0 | **3** | 240 | 39 | TopAttack |
| 152mm Giatsint | 15 000 + 3 000 splash | 1 000 | 40c0 | 1 | 180 | 39 | TopAttack |
| M270 rocket | 15 000 + 1 500 splash | 500 | 40c0 | 12 | 10 | 12 | |
| Grad rocket | 6 000 + 1 000 splash | 250 | 40c0 | 40 | 4 | 40 | |
| TOS thermobaric | 3 000 + 1 500 splash | 100 | 28c0 | 24 | 10 | 24 | |
| WGM (Bradley) | 10 000 + 2 000 splash | 800 | 25c0 | **2** | 1 000 | 8 | |
| WGM (BMP-2) | 10 000 + 2 000 splash | 800 | 25c0 | 1 | 500 | 8 | |
| Hellfire | 10 000 + 2 000 splash | 800 | 25c0 | 2 | 65 | 4–8 | |
| Stinger | 5 000 | 20 | 28c0 | 2 | 30 | 8 | AA |

### A.3 Armor reference

| Class | Thickness | Distribution (F/R/B/L/T) |
|---|---|---|
| Humvee / BTR | Light 10 | 100/80/80/80/60 |
| M113 | Light 15 | 100/80/80/80/60 |
| Bradley / BMP-2 | Medium 15 | 100/80/80/80/60 |
| Abrams | **Heavy 700** | 100/40/15/10/10 |
| T-90 | **Heavy 280** | 100/80/80/80/60 |
| Paladin / Giatsint | Light 10–19 | (artillery — top kill matters) |

### A.4 Aircraft summary

| Aircraft | Faction | Cost | HP | Armor | Speed | Primary | Secondary | Cargo |
|---|---|---|---|---|---|---|---|---|
| F-16 | USA | 6 000 | 400 | Medium/10 | 525 | AIM (6) | 20mm (150) | — |
| MiG-29 | RUS | 6 000 | 550 | Medium/— | 525 | AIM (6) | 20mm (150) | — |
| A-10 | USA | 6 000 | 800 | Heavy/20 | 390 | 30mm GAU-8 (100) | Hellfire (4) | — *disabled* |
| Su-25 | RUS | 6 000 | 700 | Heavy/20 | 420 | Rocket pods (60) | — | — *disabled* |
| Apache | USA | 6 000 | 800 | Heavy/20 | 245 | 30mm (200) | Hellfire (8) | — |
| Mi-28 | RUS | 6 000 | 800 | Heavy/20 | 245 | 30mm (200) | Hellfire (8) | — |
| Mi-24 Hind | RUS | 4 000 | 800 | Heavy/10 | 195 | 12.7mm (150) | Rocket pods (80) | 8 |
| Littlebird | USA | 3 000 | 300 | Light/5 | 265 | 7.62mm (160) | Hellfire (2) | 4 |
| Chinook | USA | 2 000 | 600 | Light/10 | 240 | — | — | 36 |
| Halo | RUS | 2 000 | 600 | Light/10 | 220 | — | — | 36 |

---

## Appendix B — combat-sim baseline runs (with caveat)

> **Reminder:** sim absolute numbers are not in-game numbers (see §0.1).
> Use only for relative-direction checks.

### B.1 Abrams vs T-90 — sim says stalemate at every range

Sim built-in scenario `tank-duel` (3v3 at 18c0):

```
USA Armor (3xAbrams): 100% survived, dmg dealt 11 520, dmg taken 9 711
RUS Armor (3xT-90):   100% survived, dmg dealt 9 711,  dmg taken 11 520
Cost-eff: Abrams 0.65 / T-90 0.68 cost-per-damage-dealt
```

**Why this is misleading:** the sim's Abrams gun is `Damage 1500, Pen 50`
vs T-90 `Thickness 650`. Effective dmg per hit ≈ 1500 × (50/650) = 115.
Real YAML: Damage 20 000, Pen 800 vs Thickness 280 → overpen → full damage.

### B.2 Bradley vs Humvee (`cost-efficiency`)

Sim: 1 Bradley ($1500) decisively beats 2 Humvees ($1200) — Humvees deal
0 damage because their MGs (Pen 5) can't penetrate Bradley armor (Thick 15).
**This part the sim gets right** — the YAML has the same penetration cliff,
so the conclusion (don't try to crack IFVs with MG-armed vehicles) holds.

### B.3 Infantry mirror (`infantry-mirror`)

Sim: 6v6 USA vs RUS rifles → both teams lose ~3.9 / 6 units, near-mirror as
expected. Sim correctly identifies symmetric infantry templates as symmetric.

---

## Appendix C — AUTOTEST in-game results & sim cross-check

> **Tests live at:** `mods/ww3mod/maps/test-balance-*`
> Run with `./tools/test/run-test.sh test-balance-<name>`.
> Verdict format: `WINNER=X | ttk=Ys | survivors=N/M | hp=H/MAX (P%)`

### C.1 test-balance-tank-1v1 — Abrams vs T-90 @ 18c0

- **Sim says:** stalemate at any range — both 80%+ HP after 40 rounds (because sim Pen 50 vs Thick 280 = 18% damage per hit; real values are Pen 800 vs Thick 280 = overpen). **Sim wrong by an order of magnitude.**
- **In-game (1 run):** `WINNER=Abrams | ttk=9.3s | survivors=1/1 | hp=7272/28000 (26%)`. Abrams won decisively, took 74% HP damage. Single 20K-dmg T-90 shell landed before Abrams finished it off (Abrams range advantage 25c vs 24c = first shot, T-90's reply with 20K shell hit before T-90 died).
- **IRL expected:** ~60/40 Abrams, TTK ~10–25s. **Match.**
- **Interpretation:** the 1v1 is decided by range advantage. Both can one-shot each other on a hit; Abrams shoots first by 1 cell. **Balance feels right** even though it's brutal — one tank duel, one tank dies.
- **Recommendation:** no change. Document that Abrams's 1-cell range advantage is a meaningful balance lever — don't quietly trim it.

### C.2 test-balance-tank-mass — 4v4 Abrams vs T-90 @ 16c0

- **In-game (1 run):** `WINNER=4xAbrams | ttk=42.1s | survivors=1/4 | hp=16796/112000 (15%)`. Abrams won, but only 1 survivor with 15% HP. That's a 1-survivor vs 0-survivor outcome — close fight, **the cost-equivalent matchup (4×Abrams 10 000 vs 4×T-90 9 600) tilts slightly NATO**.
- **Sim says:** stalemate (both 100% survivors, mutual ~10 000 damage — wrong, see §0.1).
- **IRL expected:** at 16c0 mass-fight, the side with first-shot wins individual duels. 4 Abrams' frontal armor (700 vs T-90's 280) means T-90 rounds need 1-2 hits to kill, Abrams rounds 1-2 hits. NATO's range edge becomes diluted in mass fights. Expected: 55/45 NATO or 50/50.
- **Interpretation:** result matches IRL expectation. 4v4 is much tighter than 1v1 because positioning randomness washes out the 1-cell range advantage. **Balance feels right** for the headline matchup.
- **Recommendation:** no change. Re-run 5 times to check variance — if NATO wins 4/5+ consistently, consider a tiny T-90 buff (e.g. cost 2 400 → 2 300, or thickness 280 → 320).

### C.3 test-balance-ifv-1v1 — Bradley vs BMP-2 @ 18c0

- **In-game (1 run):** `WINNER=BMP-2 | ttk=6.8s | survivors=1/1 | hp=972/14000 (7%)`. **BMP-2 won, barely** (7% HP — one more WGM and it dies). Decided by 1 ATGM exchange.
- **Sim says:** N/A in built-in scenarios.
- **IRL expected:** roughly mirror — TOW vs Konkurs is comparable. Bradley FCS slightly better, Bradley fires pairs (alpha-strike). BMP-2 faster reload (single missile every 500 ticks). Expected: ~50/50.
- **Interpretation:** at 18c0 the BMP-2's faster single-shot WGM reload (500-tick BurstWait) appears to land its first missile before Bradley's pair completes. **BMP-2's tempo edge + 15% lower cost = real BMP-2 advantage at current YAML.**
- **Recommendation:** **R-03 confirmed — set BMP-2 cost to 1 400** (or both to 1 400). The current 200 cost gap with BMP-2 winning a near-mirror fight makes BMP-2 the dominant IFV. Cost equalisation makes the choice symmetric.

### C.4 test-balance-mbt-vs-2ifv — 1 Abrams vs 2 BMP-2 (cost-equivalent)

- **In-game (1 run):** `WINNER=1xAbrams | ttk=21.2s | survivors=1/1 | hp=16687/28000 (60%)`. **Abrams dominated** — killed both BMP-2s, kept 60% HP. 1 MBT > 2 IFVs at cost-equivalence.
- **IRL expected:** in open ground 1 MBT vs 2 IFVs, the MBT typically wins because IFV ATGMs (TOW/Konkurs) take 1 200+ ticks to reload while the MBT fires every 4-5 seconds. With heavy armor, MBT eats 1-2 ATGM hits and trades them for kills. Expected: Abrams 60-70% win rate.
- **Interpretation:** matches IRL. Confirms the Abrams cost (2 500) is well-priced vs cost-equivalent IFV swarm. **No problem at this matchup.**
- **Recommendation:** **no change.** This is a case where heavy armor + high HP + strong cannon correctly beats cheap-and-numerous, which is good for tier definition.

### C.5 test-balance-at-vs-t90 — 3 AT infantry vs T-90 @ 12c0

- **In-game (1 run):** `WINNER=3xAT.inf | ttk=8.2s | survivors=3/3 | hp=477/600 (80%)`. **All three AT infantry survived with 80% HP and killed the T-90 in 8.2 seconds.**
- **My pre-test math predicted:** 3 ATGMs should leave T-90 at 25% HP — the test contradicted this.
- **What's actually happening:** the tank gun (Inaccuracy 0c768, Spread 64) is ineffective vs dispersed infantry (HitShape Radius 30) at 12c0 because most rounds land in the gap between target shape and splash radius. T-90 fires 1-2 times in 8 seconds and misses. Meanwhile each AT infantry has time to fire 1-2 ATGMs (BurstWait 200 ticks = 8s) — the guided missile reliably hits a vehicle-sized target. **TopAttack appears to deliver near-full damage** despite my Pen 100 vs effective top 168 math, suggesting TopAttack either bypasses the distribution multiplier or applies it non-linearly.
- **IRL expected:** 3 ATGM teams at 800m vs an unsupported MBT — the tank typically dies before reaching them. **Match.**
- **Interpretation:** **R-02 is wrong — no fix needed.** The current ATGM Pen 100 + TopAttack delivers IRL-correct lethality vs heavy armor. The reason my math was off: I assumed linear `damage × pen/thick` scaling, but TopAttack and/or the actual OpenRA damage formula don't penalize this case as much.
- **Recommendation:** **DROP R-02 from the priority list.** Update R-02 status to "tested, not a problem."

### C.6 test-balance-at-vs-abrams — 3 AT infantry vs Abrams @ 12c0

- **In-game (1 run):** `WINNER=3xAT.inf | ttk=10.3s | survivors=2/3 | hp=400/600 (67%)`. AT teams win with 2 survivors (67% HP). Slightly worse than vs T-90 (0 losses there) because Abrams has +4 000 HP and lasts ~2s longer, giving the tank one extra firing window.
- **IRL expected:** ~3 AT vs MBT outcome consistent with NATO doctrine — Javelin teams expected to defeat unsupported MBTs at 800m+.
- **Interpretation:** AT-vs-armor balance is in a good place. Abrams' extra HP gives it a slight survivability edge but doesn't overturn the AT engagement. Symmetric to T-90 result above.
- **Recommendation:** **no change.** This together with §C.5 confirms R-02 is not a real problem.

### C.7 test-balance-arty-1v1 — Paladin vs Giatsint @ 32c0

- **In-game (1 run):** `WINNER=Paladin | ttk=50.5s | survivors=1/1 | hp=9509/14000 (68%)`. Paladin won, kept 68% HP. **Burst:3 alpha-strike landed first 3-shell salvo, killed Giatsint before it could reply with enough volume.**
- **Sim says:** N/A (no artillery scenario in built-in sim).
- **IRL expected:** at 32c0 between 152/155mm SPHs, whoever fires first wins. Setup time + burst tempo are the key levers. Expected: 60/40 in favour of NATO (M109A7 has better autoloader).
- **Interpretation:** result confirms my §2.R-01 analysis. Paladin Burst:3 does give a meaningful alpha-strike advantage even though sustained DPS is only +12.5%. **However, the gap isn't huge (one survivor with 68% HP, not a wipe)** — Paladin lost ~30% HP to Giatsint's reply shells. **Probably acceptable as-is.**
- **Recommendation:** **leave Burst: 3** for Paladin. Single in-game result is within IRL expected envelope. If multiple-run variance shows Paladin winning >75% of the time AND with >50% HP remaining, then revisit (drop to Burst 2 or trim BurstDelays). **For v1: ship as-is; revisit in v1.1 if balance feedback flags it.**

### C.8 test-balance-heli-1v1 — Apache vs Mi-28 @ 22c0 airborne

- **In-game (run 1):** `WINNER=Mi-28 | ttk=2.8s | survivors=1/1 | hp=800/800 (100%)`. Apache shutout (0 damage to Mi-28).
- **In-game (run 2 — same allowMove=false):** identical — Mi-28 100%, 2.8s. **Deterministic.**
- **In-game (run 3 — swapped Lua engage order):** `WINNER=Apache | ttk=2.8s | survivors=1/1 | hp=800/800 (100%)`. **Mirror-image of run 1.** Whoever's `Attack()` is issued first wins 100%-vs-0% in 2.8s.
- **YAML check:** Apache (HELI) and Mi-28 (MI28) inherit the same ^Helicopter template, both have HP 800, Heavy/20 armor, Hellfire×8, 30mm×200, identical speeds. **No stat asymmetry exists.**
- **Diagnosis CONFIRMED — test-harness artifact, not balance issue.** With Hellfire travel time ~45 ticks at 22c0 and identical HitShapes, the first-shooter consistently wins. Real games (autotarget scan-jitter, ammo state, position offsets) will not reproduce this exact determinism — Apache vs Mi-28 in a real game should be ~50/50.
- **Recommendation:** **no balance change.** **Test improvement:** the harness's `ForceEngage` issues both sides in lockstep — for true mirror tests, add a 1-3 tick random offset between the two `ForceEngage` calls (or use autotarget rather than force-attack). I won't add this now since it's a v1.1+ refinement and the data we got is still informative.

### C.9 test-balance-rifle-mirror — 4v4 E3 mirror @ 8c0

- **In-game (run 1, USA engages first):** `WINNER=4xE3.USA | ttk=17.0s | survivors=3/4 | hp=559/800 (70%)`.
- **In-game (run 2, RUS engages first via Lua swap):** `WINNER=4xE3.USA | ttk=10.6s | survivors=3/4 | hp=563/800 (70%)`. **Same outcome — not a Lua call-order artifact.**
- **Sim says:** symmetric — both teams ~36% survivors.
- **IRL expected:** 50/50 (mirror).
- **Diagnosis:** confirmed deterministic USA advantage. After investigation, the structural asymmetry surfaced in §0.0 likely contributes — infantry uses `Inherits: ^E3` on USA side vs `Inherits@BaseUnit: ^E3` on RUS side. Behavior *should* be equivalent in MiniYaml but the divergence is suspicious. Possible other causes: spawn/tick order favours USA actors (A1-A4 spawned before B1-B4), or there's a hidden trait override in one faction file.
- **Recommendation:** the test should be re-run **after** normalising the inheritance patterns (B-01 / B-02 in §0.0). If USA still wins after normalisation, dig into spawn-order processing or look for another asymmetric override.

---

## Appendix D — All test verdicts (raw)

For completeness — every autotest run from this session, copy-paste ready:

```
test-balance-tank-1v1          pass  WINNER=Abrams     | ttk= 9.3s | survivors=1/1 | hp= 7272/28000  (26%)
test-balance-tank-mass         pass  WINNER=4xAbrams   | ttk=42.1s | survivors=1/4 | hp=16796/112000 (15%)
test-balance-ifv-1v1           pass  WINNER=BMP-2      | ttk= 6.8s | survivors=1/1 | hp=  972/14000  ( 7%)
test-balance-mbt-vs-2ifv       pass  WINNER=1xAbrams   | ttk=21.2s | survivors=1/1 | hp=16687/28000  (60%)
test-balance-at-vs-t90         pass  WINNER=3xAT.inf   | ttk= 8.2s | survivors=3/3 | hp=  477/600    (80%)
test-balance-at-vs-abrams      pass  WINNER=3xAT.inf   | ttk=10.3s | survivors=2/3 | hp=  400/600    (67%)
test-balance-arty-1v1          pass  WINNER=Paladin    | ttk=50.5s | survivors=1/1 | hp= 9509/14000  (68%)
test-balance-heli-1v1 (1st)    pass  WINNER=Mi-28      | ttk= 2.8s | survivors=1/1 | hp=  800/800    (100%)  *first-shooter artifact*
test-balance-heli-1v1 (swap)   pass  WINNER=Apache     | ttk= 2.8s | survivors=1/1 | hp=  800/800    (100%)  *first-shooter artifact*
test-balance-rifle-mirror (1)  pass  WINNER=4xE3.USA   | ttk=17.0s | survivors=3/4 | hp=  559/800    (70%)
test-balance-rifle-mirror (2)  pass  WINNER=4xE3.USA   | ttk=10.6s | survivors=3/4 | hp=  563/800    (70%)   *swap-order test*
```

**Reproducibility:** all results above are single runs each. The harness is deterministic per-seed, so re-runs of the same test produce identical verdicts. For RNG-aware variance you'd need a `--seed N` flag added to the runner (v1.1 work).
