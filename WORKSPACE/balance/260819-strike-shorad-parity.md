# 260819 — Iskander↔HIMARS and Tunguska↔Stryker SHORAD: asymmetry audit

Scope: the two open balance questions in `WORKSPACE/AWAITING-USER.md` items 4
and 5. **Analysis only — no stat was changed.** Every figure below is
re-derived from the resolved ruleset, not from prior docs.

Method:

- Actor/weapon numbers come from `--dump-balance-json` (the engine's resolved
  `Ruleset`, post-inheritance), regenerated at HEAD `68e8b885`.
  `file:line` citations are to the authoring YAML.
- Damage arithmetic replicates
  `engine/OpenRA.Mods.Common/Warheads/DamageWarhead.cs:200-247` and
  `ShockwaveDamageWarhead.cs:81,152-159`.
- Scripts (rerunnable, committed alongside this doc):
  `tools/combat-sim/scripts/strike-payload-analysis.py`,
  `strike-collateral.py`, `aa-pair-effective-dps.py`.

**No game was launched.** See §5 for what that leaves unverified.

---

## 0. The mechanic that governs both pairs

`DamageWarhead.InflictDamage` (DamageWarhead.cs:216-231):

```
if (thickness != 0) {
    thickness = thickness * armorPercent / 100;
    if (Penetration - thickness < 0)
        damage = damage * Penetration / thickness;   // can't penetrate
}
```

`Penetration` **defaults to 1** (DamageWarhead.cs:24). In the **resolved**
ruleset, **166 of 236 damage warheads (70%) carry that default**, so for most
weapons the damage a target actually takes is `raw × 1 / thickness`.

Share of all damage warheads whose damage is reduced against a given thickness:

| Victim thickness | warheads reduced |
|---|---|
| 15 (Iskander, SHORAD) | 179 / 236 — **76%** |
| 19 (Tunguska) | 183 / 236 — **78%** |
| 700 (Abrams) | 213 / 236 — **90%** |
| **0 (HIMARS)** | **0 / 236 — the block is skipped entirely** |

Consequence: **armour `Thickness` is the dominant survivability term in this
mod, worth far more than HP.** A unit with `Thickness: 15` takes 1/15th of the
damage from a default-penetration warhead that a unit with `Thickness: 0` takes.
Any comparison that reads only cost and HP — as both AWAITING-USER items do —
is reading the least important column.

---

## 1. Iskander ↔ HIMARS — asymmetry is REAL, and larger than proposal 002 records

### 1.1 Verified platform figures

| | Iskander | HIMARS | edge |
|---|---|---|---|
| Cost | **6000** (`vehicles-russia.yaml:912`) | **6000** (`vehicles-america.yaml:1009`) | equal |
| HP | **10000** (`:928`) | **6000** (`:1025`) | RU +67% |
| Armour type | Light (`:930`) | Light (`:1027`) | equal |
| Armour **thickness** | **15** (`:931`) | **0** (field absent, `:1026-1027`) | **RU ×15** |
| Speed | 80 (`:944`) | 70 (`:1040`) | RU |
| Turn | hull, TurnSpeed 6, `FacingTolerance: 0` (`:945`, `:984`) | turret, TurnSpeed 10 (`:1079`) | **US** |
| HitShape | 600×2540 (`:938-939`) | 480×1400 (`:1033-1034`) | **US** (2.27× smaller footprint) |
| Missile HP | 100 (`:1025`) | 50 (`:1121`) | RU |
| Missile speed | 600 / term 600 (`:1028,1037`) | 500 / term 550 (`:1124,1128`) | RU |
| Ammo | 2 (`:967`) | 2 (`:1062`) | equal |
| Range / MinRange | 50c0 / 16c0 | 50c0 / 16c0 (inherited) | equal |
| `DamageMultiplier@Loaded` | 1000 (`:924-926`) | 1000 (`:1021-1023`) | equal (both take 10× while loaded) |

Both fire via `MissileSpawnerMaster` → `<X>Missile` → `SpawnedExplodes`. The
`Armament` weapon (`IskanderTargeter` / `HIMARSTargeter`) is a **designator**,
not the payload: 50 damage with `Versus:` all-zero
(`weapons-missiles.yaml:380-402`). `HIMARSTargeter` inherits `IskanderTargeter`
with **no overrides** — the two targeters are byte-identical.

> **The combat-sim dashboard cannot see this pair's payload.** `compare
> iskander himars` reports `dps/sec 5` for both, because it reads the first
> armament — the designator. Anyone using the dashboard alone would conclude
> these two units are identical. They are not.

### 1.2 Verified warhead figures

`weapons-explosions.yaml:521-593`, cross-checked against the resolved dump:

| Warhead | IskanderExplosion | HIMARSExplosion | RU edge |
|---|---|---|---|
| TargetDamage | 54000, falloff@max **100** | 36000, falloff@max **80** | +50% |
| Spread impact | 4000, pen 2500, spread **1024** | 2500, pen 1800, spread **768** | +60% dmg, +39% pen, +33% radius |
| Shockwave | 12000, pen 2000, MaxRadius **4c0** | 7000, pen 1500, MaxRadius **2c512** | +71% dmg, +33% pen, +60% radius |
| Anti-infantry spread | 200, pen 1, spread 768 | 200, pen 1, spread 768 | equal |

Both shockwaves share `Versus: Light 80 / Medium 60 / Heavy 40 / Concrete 25`.

**Iskander's warhead is better on every axis and worse on none.** "Strictly
better" survives scrutiny for the warhead.

### 1.3 Magnitude — direct hit

`strike-payload-analysis.py`. Damage to a victim at the impact point:

| Target | Armour | HP | Iskander | HIMARS | ratio | ISK shots | HIM shots |
|---|---|---:|---:|---:|---:|---:|---:|
| Abrams | Heavy/700 | 28000 | 8877 | 5351 | 1.66 | 4 | 6 |
| T-90 | Heavy/280 | 24000 | 8992 | 5428 | 1.66 | 3 | 5 |
| Bradley / BMP-2 | Medium/15 | 14000 | 14800 | 9100 | 1.63 | **1** | 2 |
| Tunguska | Medium/19 | 8000 | 14042 | 8594 | 1.63 | 1 | 1 |
| Iskander | Light/15 | 10000 | 17200 | 10500 | 1.64 | 1 | 1 |
| **HIMARS** | **Light/0** | 6000 | **67600** | **44100** | 1.53 | 1 | 1 |

Consistent **~1.63× damage** across the whole target spectrum.

Note the headline `54000 vs 36000` is nearly irrelevant against armour: with
`Penetration: 1`, that warhead contributes only 77 (Iskander) / 51 (HIMARS)
damage to an Abrams. **The real gap lives in the Spread and Shockwave
components**, whose penetration (2500/2000 vs 1800/1500) exceeds every
thickness in the mod, so they land at full value.

### 1.4 Magnitude — collateral footprint (the bigger effect)

`strike-collateral.py`. These are area-strike weapons; what decides how much
army value a salvo deletes is reach, not point damage.

Against IFV-class bystanders (Medium/15, 14000 HP):

| Distance | 0c | 0.5c | 1c | 1.5c | 2c | 2.5c | 3c | 4c |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Iskander | 11200 | 9200 | 7240 | 6040 | 4880 | 3960 | 3080 | 1800 |
| HIMARS | 6700 | 5020 | 3665 | 2744 | 2090 | 1545 | **0** | **0** |
| ratio | 1.67 | 1.83 | 1.98 | 2.20 | 2.33 | 2.56 | ∞ | ∞ |

**The advantage grows with distance.** HIMARS's shockwave is hard-capped at
`MaxRadius: 2c512`; Iskander's runs to `4c0`. Beyond 2.5 cells the HIMARS
salvo does nothing at all while Iskander is still doing 22% of an IFV's health.

Radius at which collateral still costs ≥10% HP:

| Bystander | Iskander | HIMARS | **area ratio** |
|---|---|---|---|
| IFV (Medium/15) | 4.00c | 2.50c | **2.56×** |
| MBT (Heavy/700) | 2.44c | 0.91c | **7.23×** |

So against a clustered force — the intended use of both units — one Iskander
salvo covers **2.5× to 7× the effective area** of one HIMARS salvo, for the
same 6000 credits.

### 1.5 The likely authoring bug — REPORTED, NOT CHANGED

**HIMARS is the only mobile combat vehicle in the mod that declares an armour
type but no `Thickness`.** Every peer has one:

| Unit | Cost | Armour | Thickness |
|---|---:|---|---:|
| M270 MLRS | 1800 | Light | 8 |
| BM-21 Grad | 1500 | Light | 5 |
| Paladin | 1800 | Light | 10 |
| 2S5 Giatsint-S | 1800 | Light | 19 |
| Iskander | 6000 | Light | **15** |
| **HIMARS** | **6000** | **Light** | **0** |

The only other zero-thickness actors are static defences (SAM sites, gun
turrets, flame turret) — a coherent category HIMARS does not belong to. Its own
direct counterpart `m270` has 8.

Effect, given §0: against the 166 default-penetration warheads, HIMARS takes
**full** damage where Iskander takes **1/15th**. Combined with HP:

- HIMARS: 6000 raw damage to kill.
- Iskander: 150,000 raw damage to kill (10000 HP × 15).

**~25× effective durability against the majority of the mod's weapons.** This
dwarfs every other number in this audit, and it is almost certainly an omitted
field rather than a design decision — nothing else about HIMARS is authored as
a glass cannon, and it is already behind on damage *and* HP.

Flagging loudly as instructed. **Not changed.**

### 1.6 Correction to proposal 002

`002-himars-iskander-parity.md` is right that the asymmetry exists but
understates it and cites stale lines:

- Its line refs `vehicles-russia.yaml:913` / `vehicles-america.yaml:999` are
  off; the `Cost:` keys are now at `:912` and `:1009`.
- It says "platform HP/speed/cost do not offset a strict warhead dominance".
  In fact HP and armour **compound** it: 10000/15 vs 6000/0. Its Evidence
  section never mentions HP or thickness at all.
- It records the warhead gap as the whole story. The collateral-area gap
  (2.5–7×) is larger than the point-damage gap (1.63×).

### 1.7 What HIMARS actually wins

Falsification attempt, for completeness. HIMARS is genuinely better at:

1. **Aiming.** `AttackTurreted` + `Turreted TurnSpeed: 10` vs Iskander's
   `AttackFrontal` with `FacingTolerance: 0` and hull `TurnSpeed: 6` — Iskander
   must rotate its whole hull to an exact bearing. HIMARS retargets faster.
2. **Silhouette.** HitShape 480×1400 vs 600×2540 — 2.27× smaller footprint, so
   meaningfully harder to catch in someone else's splash.
3. Marginally better turn rate (10 vs 6) at the cost of 12% top speed.

None of these are damage, and none scale with the 6000-credit price. They do
not come close to offsetting 1.63× payload × 2.5–7× area × 25× durability.

**Verdict: the asymmetry is real, one-directional, and substantially larger
than previously documented.**

---

## 2. Tunguska ↔ Stryker SHORAD — the stated asymmetry does NOT survive; it inverts

### 2.1 Verified platform figures

| | Tunguska | Stryker SHORAD |
|---|---|---|
| Cost | **1700** (`vehicles-russia.yaml:785`) | **2500** (`vehicles-america.yaml:846`) |
| HP | **8000** (`:787`) | **14000** (`:850`) |
| Armour | Medium, **thickness 19** (`:789-791`) | Medium, **thickness 15** (`:852-854`) |
| Speed | 100, heavytracked (`:801`) | 120, heavywheeled (`:863`) |
| Cargo | none | **9 weight, Infantry** (`:984-986`) |
| Armaments | 30mm AG, 30mm AA, 9M311 SAM | 25mm, Stinger quad, **Hellfire** |

The AWAITING-USER framing — "1700/8000 against 2500/14000" — is arithmetically
correct and materially misleading. It compares only cost and HP, i.e. the two
columns §0 shows matter least, and omits that the SHORAD costs **47% more**.

Durability per credit, once thickness is included:

- Tunguska: 8000 × 19 = 152,000 effective ÷ 1700 = **89.4 / credit**
- SHORAD: 14000 × 15 = 210,000 effective ÷ 2500 = **84.0 / credit**

**Against default-penetration weapons the Tunguska is already the more
cost-efficient survivor**, despite the 43% raw-HP deficit the item cites.

### 2.2 Magnitude — effective DPS by target class

`aa-pair-effective-dps.py`, penetration applied, weapon-level `ValidTargets`
respected:

| Target class | Tunguska | SHORAD | ratio | ISK/1k cr | SHO/1k cr |
|---|---:|---:|---:|---:|---:|
| Infantry | 14674 | 2155 | **6.81×** | 8632 | 862 |
| IFV (Medium/15) | 13122 | 2308 | **5.68×** | 7719 | 923 |
| MBT (Heavy/700) | 1304 | 617 | **2.11×** | 767 | 247 |
| Helicopter | 17473 | 2778 | **6.29×** | 10278 | 1111 |
| Fixed-wing air | 3125 | 2778 | 1.12× | 1838 | 1111 |

**The Tunguska out-damages the Stryker SHORAD in every target class, at 68% of
the cost.** The asymmetry runs opposite to the direction the item states.

Two structural reasons:

- `30mm.Tunguska.AA` carries `ValidTargets: Helicopter`
  (`weapons-ballistics.yaml:639-642`), so the Tunguska brings a 13043-dps
  autocannon to bear on helicopters. `25mm.Bradley` is
  `Infantry, Vehicle, Defense` (`:546` via `^30mm`) — **the SHORAD's autocannon
  cannot engage air at all.** Its entire AA capability is 8 Stinger rounds.
- The Tunguska's 30mm fires Burst 12 / BurstWait 12 (`:622-624`) against the
  Stryker's Burst 4 / BurstWait 20 (`:575-578`) — 7.6× the raw autocannon DPS.

For band context (`tier-cost`, primary armament only): SHORAD's 690 dps/1000cr
is the lowest of any combat vehicle between 1500 and 2500 credits — against
Grad 15625, M270 8065, Tunguska 7673, Abrams 1538.

### 2.3 What the SHORAD genuinely buys for its extra 800 credits

This is why the pair is **differentiated, not dominated**:

1. **Infantry transport** — `Cargo: MaxWeight 9` (`:984-986`). The Tunguska has
   none. A real doctrinal role the DPS table cannot price.
2. **Anti-tank capability.** `Hellfire.strykershorad` is `pen 800`
   (`weapons-missiles.yaml:271-274` via `Hellfire`), above the Abrams's
   thickness 700, so it lands its full 10000 unreduced — 20000 per 2-missile
   salvo. The Tunguska's 30mm is `pen 70` against thickness 700, i.e. 100
   damage per round: **the Tunguska essentially cannot kill an MBT.**
3. **Magazine endurance.** 400 rounds / Burst 4 = 100 bursts = **116s** of
   continuous fire, against the Tunguska's 180 / 12 = 15 bursts = **13.8s**.
   The SHORAD sustains 8.4× longer. Per full magazine the two deliver
   comparable total damage (200,000 vs 180,000) — the Tunguska simply dumps it
   8× faster and then needs a `logisticscenter`.
4. Raw HP 14000 vs 8000 — survives a single big hit that thickness cannot
   blunt (e.g. anything with pen > 19).
5. Longer reach: 20c0 autocannon vs 18c0; Hellfire 25c0.

**Verdict: NOT a real asymmetry.** These are two different units doing two
different jobs at two different prices. The Tunguska is a burst-damage
gun/SAM system; the SHORAD is a sustained-fire multi-role IFV with AT and
transport. Item 5's framing should be retired.

The one finding worth carrying forward is not a parity problem but a **role
problem**: a 1700-credit *air-defence* unit currently has the best
soft-target ground DPS in its cost band (14674 vs infantry). If that bothers
anyone it is a Tunguska question, not a SHORAD question.

---

## 3. Options for pair 1 (ranked). NOT APPLIED — user's call

Pair 2 needs no change; see §2.

### Option A — set `HIMARS` `Armor: Thickness` (fix the suspected omission)

`vehicles-america.yaml:1026-1027`, add `Thickness: 8` (matching its own
counterpart `m270`) or `15` (matching Iskander).

- **For:** smallest possible diff; corrects what §1.5 shows is almost certainly
  an authoring omission rather than a decision; addresses the single largest
  term (25× effective durability) without touching any deliberate balance
  choice; leaves both factions' flavour intact.
- **Against:** does not touch the payload gap — Iskander still lands 1.63×
  damage over 2.5–7× the area. This is a bug fix, not a balance fix.
- **Risk:** HIMARS becomes materially harder to kill by the 76% of damage
  warheads that a thickness of 15 blunts — a real buff to US survivability, and
  will move counter-battery outcomes. Needs a fresh benchmark baseline.
  Also: `Distribution` is absent too, so directional armour stays uniform
  unless that is added with it (`ArmorDirectionPercent` returns a flat 100
  when `Distribution.Length != 5`).

### Option B — raise Iskander cost 6000 → 8000–9000

`vehicles-russia.yaml:912`. This is proposal 002's Option A.

- **For:** cost is the mod's universal lever under the budget-allocation model
  (`DOCS/reference/economy.md` — no production, cost is allocation against
  off-map reserves); preserves the "Iskander is the scarier missile" flavour;
  one-line diff.
- **Against:** §1.4 says the gap is 2.5–7× in delivered area, not 1.5×. A 33%
  price rise (8000) prices roughly the point-damage gap and undershoots the
  area gap. Correctly pricing it would mean something closer to 12000+, which
  is a different unit.
- **Risk:** the AI's budget ceilings key on cost (`mods/ww3mod/rules/ai/`) —
  verify the RU bot still buys Iskanders at the new price, or the change
  silently deletes the unit from bot play.

### Option C — converge the warheads

`weapons-explosions.yaml:521-551`: bring `IskanderExplosion` toward the HIMARS
envelope (direct 54000 → 42000, shockwave `MaxRadius 4c0` → `3c0`).

- **For:** attacks the term that actually dominates — shockwave radius drives
  the 2.56×/7.23× area ratio, and nothing else does.
- **Against:** largest behavioural change of the three; alters RU strike feel
  most; touches a shared weapons file (`IskanderExplosionAirborne` inherits
  from it, `weapons-explosions.yaml:595-596`, and the launcher's own
  `Explodes@Loaded` uses it — a cook-off would get weaker too).
- **Risk:** grep for other consumers before applying; re-run `nav-guard` is not
  needed but a fresh balance baseline is.

### Recommendation

**A first, alone, and then re-measure.** It is the only one of the three that
is not a balance opinion — §1.5 makes a strong case that the zero thickness is
an omission, and it happens to be the largest single term in the comparison. It
should not be bundled with B or C, because doing so makes it impossible to tell
which change moved the benchmark.

If, after A, measured cross-faction winrate still favours RU, **C over B** —
because the evidence says the dominance lives in delivered area, and B prices
a gap (1.63×) that is not the one doing the damage.

I would not do B alone. It is the smallest diff but it targets the wrong term.

---

## 4. Standing constraint

Per `WORKSPACE/balance/README.md` §4 and the standing no-stat-changes rule:
**no YAML was edited.** If the user approves any option, it should be applied
as its own commit referencing this audit, with proposal 002 updated (its
Evidence section is incomplete — see §1.6) and its status flipped.

## 5. What this audit does NOT establish

- **No engine run.** Every number is static arithmetic replicating the damage
  path. Positioning, projectile travel, `AimingDelay`/`SetupTicks`, missile
  interception (both missiles are `^ShootableMissile` — Iskander's has 2× the
  HP and is faster, so it is harder to shoot down, an effect not modelled),
  autotarget priority and AI purchase behaviour are all unmodelled. The
  in-game `test-balance-*` harness is the only authority on who wins.
- **`ArmorDirectionPercent` is modelled as a flat 100.** Iskander's
  `Distribution: 100,80,80,80,60` means a hit from a non-frontal bearing
  reduces its effective thickness — so §1.5's 25× figure is the frontal case
  and an upper bound. HIMARS has no `Distribution`, so it is flat 100 by
  definition and unaffected. Directional geometry would narrow the gap
  somewhat; it cannot close it, since thickness 0 short-circuits the entire
  block regardless of angle.
- **Shockwave `MaxRadius`/`Falloff`/`Spread` are not in the balance dump.** They
  were read from `weapons-explosions.yaml:536-543` and `:573-580` and hardcoded
  into `strike-collateral.py`. If those YAML values change, that script goes
  stale silently — it has no drift guard, unlike the dashboard.
- **The dashboard's `dps` ignores `Magazine`, `ReloadDelay` and `AmmoPool`.**
  §2.3's endurance table corrects for AmmoPool by hand; the §2.2 DPS table does
  not, and therefore flatters the Tunguska, whose magazine lasts 13.8s.
- **`tier-cost` is primary-armament only**, so it understates the SHORAD (3
  armaments, 2 of them missiles) more than most units.
