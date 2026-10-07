# Opportunities — BALANCE & UNIT ROLES (scout, 2026-10-07)

**Read against `main @ feaae6c4`** (worktree `wt/scout-bal`, clean, not behind). Every `file:line` below is at that ref.
**Nothing was built, launched, linted or autotested.** Numbers are tagged:

- **[yaml]** — read straight off shipped YAML at `feaae6c4`.
- **[dump]** — from `tools/combat-sim/data/stats.json` (engine `--dump-balance-json`, generated 2026-09-08 at `e420cb03`).
  The 131 rules commits since then were checked for stat drift (`git diff e420cb03..HEAD` over `Cost/HP/Damage/Range/Burst*/Thickness/Penetration/Ammo`):
  the only stat moves are garrison-building HP/Thickness and the quadcopter's `Cost: 25`. **No unit or weapon row used below moved.**
- **[sim]** — the existing pure-Python engine-port Monte Carlo `tools/combat-sim/scripts/tunguska-aa-hit-rate.py`, imported with new target
  profiles; no engine involved. It replicates `Bullet` scatter, `InaccuracyType.Maximum` range scaling, `TargetDamageWarhead`'s
  `CenterProximityPercent` scaling and penetration. Wrappers I used are reproduced in §Appendix so anyone can rerun them.
- **[calc]** — arithmetic on the above. **[hyp]** — argued, not measured.

**User rule that governs every item here** (PIPELINE item 32, user 2026-08-02): *"I do not want you to change any unit stats without my explicit
review and approval."* So every stat change below is a **proposal for the `WORKSPACE/balance/` NNN-flow**, not a dispatch. Items 6 (docs) and the
description half of 3 are the only things a worker could do without sign-off.

Already queued, therefore **not re-proposed** here: item 32 (US/RU pair audit — Grad↔M270, Bradley↔BMP-2, M109↔Giatsint, crew survivability,
`260802-parity-audit.md` §5), AWAITING-USER item 3 (HIMARS armour omission / Iskander), item 44 (AA autotarget serialisation), item 45
(missile fuse), item 81 (aircraft contestation), item 82 (`EngageAtLongestArmamentRange`), item 71 (cover/prone).

---

## Ranked

| # | Title | Ladder | Cost to fix | Needs user sign-off |
|---|---|---|---|---|
| 1 | The SAM Site — the only buildable anti-air — needs 7 hits on a gunship that a 300-credit MANPAD kills in 1 | **SHOULD-FIX** | 1–2 YAML lines | yes (stat) |
| 2 | Mi-28's Ataka (22c) is out-ranged by the MANPAD (23c); its equal-cost twin the Apache (25c) is not | **SHOULD-FIX** | 1 YAML line | yes (stat) |
| 3 | Team Leader's "morale" aura only speeds up XP; at 200 credits it is the worst soldier in the roster | **SHOULD-FIX** (tooltip) / POLISH (stat) | 1 line text, or 3 lines YAML | text: no · stat: yes |
| 4 | A rifle's printed "200 damage" against 200 HP reads as one-shot; at its own range it takes 5–6 hits and ~35 s | **POLISH** (design question) | 0 — a decision first | yes |
| 5 | TOS and Tunguska are targeted as **Heavy** tanks though their armour is **Medium**/19 | **POLISH** | 2 YAML lines | yes (target priority) |
| 6 | `economy.md`'s tank-ammo budget row (~10%) is 2.5× off the shipped 23–26% | **COSMETIC** (but it misroutes balance work) | doc edit | no |
| 7 | Saturation weapons (Grad, Hind rocket pods) buy 5–10× more raw damage per supply credit than precision ones | **POLISH** (measurement) | a script | no (measurement only) |

Ranking function: what a stranger meets, how early, how visibly; fix cost breaks ties (`PIPELINE.md` §"RELEASE AUDIT").
#1 and #2 sit high because helicopters are a headline purchase and the only fixed AA answer is the one that does not work.
#3 sits high on the tooltip half alone: a player reads the description, buys the unit, and nothing they can see happens.

---

## 1. The SAM Site needs 7 missiles to kill a gunship that a MANPAD soldier kills with 1

**Today.** You build the SAM Site (2000 credits, the only buildable AA structure) beside your Supply Route to stop helicopter raids. An Apache
or Mi-28 parks over it and takes **~7 SAM hits** to come down — while a 300-credit MANPAD soldier one-shots the same helicopter.
**After.** The SAM kills an armoured gunship in one hit, like every other AA missile in the game.

**Premise check.** Already recorded as `[high]` at `WORKSPACE/bugs/discovered.md:4374` (2026-08-22, `wt/aa-interval`) and in
`DOCS/reference/missiles.md:629-644`. **Unowned:** no PIPELINE item, no `balance/` proposal (`grep -n SurfaceToAirMissile WORKSPACE/PIPELINE.md` → 0).
`git log -S'Penetration' -- weapons-missiles.yaml` shows no commit touching this weapon's penetration since. Re-verified at `feaae6c4`: no
`Penetration:` key between `weapons-missiles.yaml:431` and `:462`.

| AA shooter (cost) | Weapon, cite | Damage / Pen | vs Apache·Mi-28 (800 HP, Thk 20) | vs Hind (800, Thk 10) | vs Littlebird (300, Thk 5) |
|---|---|---|---|---|---|
| MANPAD soldier (300) | `MANPAD` `weapons-missiles.yaml:499` | 3000 / 15 | **2250 → 1 hit** | 3000 → 1 | 3000 → 1 |
| Stryker SHORAD (2500) | `Stinger.quad` `:583` ← `Stinger` `:547` | 5000 / 20 | 5000 → 1 | 5000 → 1 | 5000 → 1 |
| Tunguska (1700) | `9M311` `:617` ← `Stinger` | 5000 / 20 | 5000 → 1 | 5000 → 1 | 5000 → 1 |
| **SAM Site (2000)** | `SurfaceToAirMissile.double` `:463` ← `:431`; Damage `:454`, `RandomDamageAddition` `:455` | 2000+rand 1000 / **unset → 1** | **125 avg → 7 hits** | 250 → 4 | 500 → 1 |

[yaml][calc] — damage rule `DamageWarhead.cs:128-134`: `damage * pen / thickness` when `pen < thickness`. Airframe stats
`aircraft-america.yaml:318,324,327` (HELI), `aircraft-russia.yaml:311,317,320` (MI28), `:135,141,144` (HIND), `aircraft-america.yaml:134,138,141` (littlebird).
SAM actor: `structures-defenses.yaml:877`, `Prerequisites: supplyroute, ~techlevel.medium` `:921`, `Cost: 2000` `:924`, weapon `:937`.

**Ranking rationale — SHOULD-FIX.** Noticed within the first few matches by anyone who meets an enemy gunship; reads as "the SAM is broken".

**Cheapest verification.**
- Static, already done: the table above.
- One autotest, new scenario `test-balance-sam-vs-heli` (copy the frame of `test-sam-intercepts-iskander`, which already places a SAM):
  one SAM (player A), one `HELI` (player B) spawned airborne and **hovering, no orders**, 20c0 away, inside the SAM's 35c0. Read the Apache's death tick.
  **Pass bar: Apache dead ≤ 15 s after the first SAM launch.** On current YAML 7 hits at `BurstWait 80` + `BurstDelays 20` is ≥ 4 volleys ≈ 24 s
  even at 100% hit rate, so the **RED run on shipped YAML must fail** with the death-time message — budget it per `feedback_red_before_green`.

**Dispatch brief (after sign-off via a `WORKSPACE/balance/NNN-sam-penetration.md`).**
- `mods/ww3mod/rules/weapons/weapons-missiles.yaml`, `SurfaceToAirMissile:` `Warhead@Spread` (`:453`): add `Penetration: 20`
  (matches `Stinger`, the value every other AA missile uses).
- **Same pass, required by `discovered.md:4393`:** the two-missile salvo (`SurfaceToAirMissile.double` `BurstDelays: 20` `:466`) was
  load-bearing only because the SAM needed many hits. With a one-hit missile the second launch 20 ticks later (lifetime 55) is
  overkill. Decide explicitly: keep `Burst: 2` as a hit-probability pair (it costs nothing, since the SAM has no `AmmoPool`), or move
  `BurstDelays` above the 55-tick lifetime. Recommendation: **keep the pair** and say so in the commit.
- Do **not** touch `AirToAirMissile` in the same commit — it has the same defect but sits only on disabled fixed-wing.
- Read first: `DOCS/reference/missiles.md` §"Widening an interval is only correct if the FIRST missile is lethal" (`:621-644`), `discovered.md:4374-4398`.
- Regression: `test-sam-intercepts-iskander`, `test-sam-vs-kinzhal` (interception unaffected: the `ICBM` targets carry `Armor: Type: Light`
  with no thickness, `defaults.yaml:1124-1125`, so penetration never applies — **verify that claim before trusting it**), and the
  `test-aa-overkill-*` trio.
- Risk: a SAM beside the SR becomes a hard counter to gunship raids on the beachhead. That is arguably the point, but it interacts with
  item 81 (aircraft vs contestation): say so in the proposal.

---

## 2. Mi-28's Ataka is out-ranged by the MANPAD; the Apache's Hellfire is not

**Today.** Both gunships cost 6000 and share an airframe (`260802-parity-audit.md` §3.2). Escort an armour column with one MANPAD soldier:
the **Apache** can hit the tanks from 25c and stay outside the MANPAD's 23c; the **Mi-28** must close to 22c to fire Ataka and is inside the
MANPAD's envelope for every shot. Neither gunship can shoot the MANPAD soldier first — both gunships' only anti-infantry weapon reaches 18c.
**After.** Both gunships can stand off a MANPAD-escorted column; neither can bully the MANPAD itself.

| Platform (cost) | Anti-vehicle reach | Anti-infantry reach | vs MANPAD 23c (`weapons-missiles.yaml:502`) |
|---|---|---|---|
| Apache `HELI` (6000) | `Hellfire` **25c0** `weapons-missiles.yaml:244` | `30mm.Heli` 18c0 `weapons-ballistics.yaml:744` | stands off armour by **+2c** |
| Mi-28 `MI28` (6000) | `Ataka` **22c0** `weapons-missiles.yaml:127` | `30mm.Heli` 18c0 | **−1c — inside** |
| Hind `HIND` (4000) | `RocketPods` 25c0 `weapons-ballistics.yaml:912` (`ValidTargets: Ground…` `:910`, so hits infantry too) | same rockets | **+2c on both** |
| Littlebird (3000) | `Hellfire.Littlebird` 20c0 [dump] | `7.62mm.Minigun` 8c0 `weapons-ballistics.yaml:232` | −3c |

[yaml] One MANPAD hit kills either gunship (§1 table). Air-to-ground guns cannot hit a *moving* helicopter (no lead, `Bullet.cs:200`,
documented in the `tunguska-aa-hit-rate.py` header), so the MANPAD is the decisive ground counter and its reach is the number that matters.

**Premise check.** `git log -S'Range: 22c0' -- weapons-missiles.yaml` → `1810bddd` (2026-05-11, *"Mi-28 fires Ataka (SACLOS, slowdown);
Apache stays Longbow"*). The audit records Ataka's shorter reach as deliberate flavour ("expensive fire-and-forget vs cheap-but-committed",
`260802-parity-audit.md:135`) — **but nothing in the tree weighs the 22c against the MANPAD's 23c.** The flavour is the SACLOS slow-down
and the 150-vs-200 supply price; the 1-cell inversion against the shared MANPAD looks incidental. **[hyp]**

**Ranking rationale — SHOULD-FIX.** It is the one place the two equal-cost flagship aircraft differ in *whether they can do their job*,
and it is a faction asymmetry in Russia's disfavour. Noticed by a Russia player within a few matches of using gunships.

**Cheapest verification.** One autotest pair on one new scenario `test-balance-gunship-vs-escorted-ifv`: a stationary enemy IFV with one
`aa.*` soldier 1 cell in front of it; gunship ordered `AttackMove` past it (the `test-heli-standoff` frame). Run once with `MI28` vs a US
column, once with `HELI` vs a RU column as the control. **Pass bar: the gunship fires its first ATGM AND is alive 20 s later.** Expect the
Apache control to pass and the Mi-28 to fail on shipped YAML — that pair *is* the RED/GREEN.

**Dispatch brief (after sign-off).** `weapons-missiles.yaml:127` `Ataka` `Range: 22c0` → `24c0` (keeps it 1c short of Hellfire, so the
"committed" flavour survives; `Ataka.AA` inherits it at `:185` — check that the Mi-28's air-to-air reach growing by 2c is acceptable).
**And `Projectile: RangeLimit: 22c0` at `:148` must move with it to `24c0`.** Otherwise the weapon is offered at 24c and the
missile self-destructs at 22c. Read first: `DOCS/reference/missiles.md` §2 (SACLOS class), audit §3.2. Risk: at `Speed: 400` (`:138`) the
extra 2c adds ~5 ticks of SACLOS guidance, during which the Mi-28 is slowed 50%.
**Alternative the user may prefer:** leave it and write the inversion into the Mi-28's description, so a player is told.

---

## 3. The Team Leader's "morale boost" only makes nearby soldiers learn faster

**Today.** The description says *"Boosts the morale of friendly soldiers within 4 cells"* (`infantry.yaml:1525`). The aura grants
`morale-boost` (`:1530-1534`), and the **only live consumer** of that condition is `GainsExperienceMultiplier@TLBoost: 150` (`:273-275`).
The damage, speed and pip effects are commented out (`:276-291`) and have been since the 2023 rules WIP (`98a4dc09`). Nothing on screen
shows the aura is on. So you pay 200 for a soldier whose weapons kill slower than a 100-credit Automatic Rifleman's (table below) and whose
advertised perk is invisible.
**After.** Either the description says what it does, or the aura does something a player can see.

| Soldier (cost) | Weapons | Time to kill 1 standing rifleman, 10c / 14c [sim] |
|---|---|---|
| Automatic Rifleman `AR` (100) | `5.56mm.AR` | 11.4 s / 28.0 s |
| Rifleman `E3` (100) | `5.56mm.DMR` + RPG | 15.2 s / 35.6 s |
| **Team Leader `TL` (200)** | `7.62mm.DMR` + `GrenadeLauncher` | not simulated; [dump] static DPS vs infantry 236+196, below the AR's 1042 |

**Premise check.** `grep -rn morale-boost mods/ww3mod/rules` → one live consumer. `git log -S'DamageMultiplier@TLBoost'` → only `98a4dc09`.
Not in PIPELINE, `bugs/discovered.md`, or `DOCS/reference`.

**Ranking.** Tooltip half **SHOULD-FIX** (a promise the unit does not keep, on a unit every player sees in the first menu).
Stat half **POLISH**.

**Cheapest verification.** Text half: none needed. Stat half: rerun the §Appendix wrapper with the inaccuracy multiplier. At
`InaccuracyMultiplier 85`, a DMR's rounds-to-kill drops 38.9 → 28.2 at 14c and 16.6 → 12.3 at 10c (**−27% to −28% time to kill**) [sim].
In-engine: one run of the existing `test-balance-rifle-mirror` with a TL added to one side. **Pass bar: the TL side wins by ≥ 1 surviving
soldier more than the mirror baseline, across the scenario's own seed.**

**Dispatch brief.**
- **(a) No sign-off needed:** `infantry.yaml:1525` description → *"Squad leader and designated marksman. Soldiers within 4 cells gain
  experience 50% faster."* Read `c2ef52db` first: descriptions must not restate numbers the tooltip already prints, and this one does not.
- **(b) Sign-off needed — pick one effect, not several:** add to `^Soldier` beside `:273`
  `InaccuracyMultiplier@TLBoost: RequiresCondition: morale-boost / Modifier: 85` ("steadies their aim"). The trait already exists and
  infantry already stack ten condition-driven `InaccuracyMultiplier@Suppression_N` on the same template (`:559+`), so the idiom is
  in-file. Restore the commented pip (`:282-291`) so it is visible. Risk: it stacks with suppression multipliers, so the boost is larger
  on a suppressed squad. Check that `TL`'s own `-GainsExperienceMultiplier@TLBoost` (`:1517`) does not also need a matching removal.

---

## 4. A rifle prints 200 damage against 200 HP — and takes 5 to 6 hits to kill at range

**Today.** Every 5.56 rifle and the soldier it shoots both read **200** (`weapons-ballistics.yaml` `^5.56mm` `Damage: 200`; infantry HP 200).
The obvious reading is one hit, one kill. In the engine every `TargetDamage` hit is multiplied by `CenterProximityPercent`: 100% dead centre,
0% on the rim of the r30 hit circle. It is then multiplied again by `DamageAtMaxRange: 50`. The mean *landed* hit is **48** at 10c and
**36** at 14c [sim], so a kill takes 4–6 hits. With scatter, a DMR rifleman needs **~39 rounds and ~36 s to kill one standing soldier at
its own max range, and ~80 rounds and ~73 s against a prone one** [sim]. An AR firing at a prone soldier at 14c uses 328 rounds,
**two-thirds of its 500-round pool, on a single kill.**
**After (if the user wants it).** Firefights resolve at a pace the user chose rather than one that fell out of a 2023 hit model.

| Shooter → standing / **prone** rifleman | 10c rounds/kill | 14c rounds/kill | 14c seconds/kill |
|---|---|---|---|
| `5.56mm.DMR` (E3) | 16.6 / **32.8** | 38.9 / **79.7** | 35.6 / **73** |
| `5.56mm.AR` (AR) | 59.6 / **123.8** | 146.0 / **328.2** | 28.0 / **63** |

[sim] stationary target, no suppression, no cover/foliage refusal, no prone-induced accuracy change on the shooter. Cycle times from
[dump]. **This is not a bug.** Centre-proximity scaling is authored WW3MOD behaviour (`920f8d24`/`11b9d344`, 2023-04-13, *"TargetDamage WIP"*;
renamed `3aa5d901`) and is documented for *buildings* at `conventions.md:336-360`. What is **not** written down anywhere is its consequence
for infantry pacing, and no number in the tooltip shows it.

**Ranking — POLISH, and a design question, not a fix.** An engaged player notices that firefights are slow and soldiers "whiff". Item 71
(cover protects almost nothing) is the same family: both are about what infantry damage *feels* like.

**Cheapest verification.** `test-balance-rifle-mirror` already exists. One run, then read the time to first kill. **Pass bar (descriptive):
the measured median falls inside the [sim] band, 15–36 s at 10–14c.** If it does, the sim is trustworthy for tuning this without more launches.

**Brief — the decision, not a diff.** Options for the user: (A) accept, and document the effective-damage band in `conventions.md`;
(B) raise `DamageAtMaxRange` on `^5.56mm`/`^7.62mm` from 50 toward 100 (halves the max-range kill time; a sweep is cheap in the sim);
(C) change nothing in YAML and add an "effective damage" line to the production tooltip. **Do not** remove centre-proximity scaling:
§1 of `missiles.md` and the building cases depend on it.

---

## 5. TOS and Tunguska are tagged Heavy for targeting but armoured Medium

**Today.** Both carry `Armor: Type: Medium, Thickness: 19` but `Targetable: TargetTypes: Ground, Vehicle, Heavy`:

| Actor | Armor | Targetable | Cite |
|---|---|---|---|
| `tos` (2000) | Medium / 19 | **Heavy** | `vehicles-russia.yaml:741-742` / `:744-745` |
| `tunguska` (1700) | Medium / 19 | **Heavy** | `:864-865` / `:867-868` |
| `bmp2`, `bradley`, `strykershorad` | Medium | Medium | `:170-174` / `vehicles-america.yaml:351`, `:923` |
| `m109`, `giatsint` | **Light** | **Medium** | `vehicles-america.yaml:627` / `vehicles-russia.yaml:449-453` — symmetric, both factions |

[yaml] The class token drives two things. (1) **Autotarget priority** — `^AutoTargetMBT`/`IFV`/`Artillery` rank Heavy 5 > Medium 4
(`defaults.yaml:459-528`), so tanks and IFVs treat a Tunguska like a T-90. `^AutoTargetMHL`, which the AT soldier inherits
(`infantry.yaml:1759`), ranks Medium 4 > Heavy 3 (`defaults.yaml:634-642`), so AT teams **de-prioritise** them below every IFV. (2) **Gating** —
`60mm_Mortar` `InvalidTargets: Concrete, Heavy` (`weapons-ballistics.yaml:785`) cannot target either. `ai-russia.yaml:533` already notices
this mortar exclusion for other reasons.

**Premise check.** `git log -S'TargetTypes: Ground, Vehicle, Heavy' -- vehicles-russia.yaml` → only `98a4dc09` (2023-03-21, "rules WIP"). That
predates the penetration/thickness model (`c946ceae` 2023-05-15, `c53f7bf8` 2023-06-07) which set `Type: Medium`. So the tag is **stale**
rather than chosen. **[hyp]** — no commit says so.

**Ranking — POLISH.** Only Russia's units are tagged *up*. The artillery pair is tagged up symmetrically, and that also makes them immune to
rifles, which may well be intended. Leave the artillery pair alone.

**Cheapest verification.** Static: a one-off script over `stats.json` plus the YAML asserting `Armor.Type` equals the class token on every
buildable vehicle, with an explicit allow-list for `m109`/`giatsint`. If accepted, that becomes an NUnit guard in the
`AirborneArmorTargetableTest` family. In-engine: not needed for (2). For (1), one run of `test-balance-at-vs-t90` with a Tunguska added beside
the T-90. **Pass bar: the AT team engages the T-90 first on shipped YAML and the Tunguska first after the change** (it is the nearer and
softer target) — or the user decides the old order is wanted.

**Dispatch brief (after sign-off).** `vehicles-russia.yaml:745` and `:868`: `TargetTypes: Ground, Vehicle, Heavy` → `Ground, Vehicle, Medium`.
Read `DOCS/reference/conventions.md` §target-types first. Risk: any bot module that reads believed enemy armour by class token (the
`CompositionNeed` weights, `ai.yaml:2129-2140`) will count Russia's AA and TOS as medium rather than heavy armour. That **moves `@stable`** —
say so in the commit.

---

## 6. `economy.md` tells balance workers a tank's ammo is ~10% of its price; it is 23–26%

**Today.** `DOCS/reference/economy.md:640` gives "Tank main gun (40 shells) | ~10%", and `:631` cites `vehicles-america.yaml:519` as
"8 batches × 5 shells × 30 supply = 240 (9.6% of cost 2500)". Shipped since `c53294fc` (2026-09-03, *"main-gun ammunition costs what a
tank shell should"*): Abrams `40/5 × 80 = 640` = **25.6%** (`vehicles-america.yaml:538-557`), T-90 `40/5 × 70 = 560` = **23.3%**
(`vehicles-russia.yaml:360`). The doc says "Audit against these ratios and nothing else", so the next balance worker will flag both tanks
as 2.5× over budget.

Full refill cost by unit, for reference [yaml][calc] (pool totals `ceil(Ammo/ReloadCount) × SupplyValue`):

| US | cost | refill | % | RU | cost | refill | % |
|---|---|---|---|---|---|---|---|
| humvee | 500 | 24 | 5 | btr | 600 | 80 | 13 |
| m113 | 700 | 80 | 11 | bmp2 | 1300 | 808 | 62 |
| bradley | 1500 | 852 | 57 | t90 | 2400 | 560 | 23 |
| abrams | 2500 | 640 | 26 | giatsint | 1800 | 480 | 27 |
| m109 | 1800 | 480 | 27 | grad | 1500 | 680 | 45 |
| m270 | 1800 | 840 | 47 | tos | 2000 | 960 | 48 |
| strykershorad | 2500 | 1432 | 57 | tunguska | 1700 | 580 | 34 |
| HIMARS | 6000 | 3000 | 50 | iskander | 6000 | 3000 | 50 |

**Ranking — COSMETIC.** It is a curated-doc defect, so it is fixable on sight under the knowledge-bank rule. It is listed here because it
would misdirect item 32's pass.
**Brief.** Edit `economy.md:640` to "~23–26% (since `c53294fc`)" and re-point `:631`'s example at `vehicles-america.yaml:546-547`.
No other row is off: the IFV ATGM row (~40%) holds on the missile pool alone (Bradley 600/1500, BMP-2 520/1300).

---

## 7. Saturation weapons buy 5–10× more raw damage per supply credit than precision ones

**Today [calc, raw — no hit rate].** Raw warhead damage per full refill ÷ refill supply cost, against a Medium/15 hull (every pen ≥ 15):

| Weapon (platform) | Shots × (TargetDmg + Spread) | Refill supply | Raw dmg / supply |
|---|---|---|---|
| `RocketPods` (Hind, 4000) | 80 × (5000 + 200) | 640 | **650** |
| `GradRockets` (Grad, 1500) | 40 × (6000 + 1000) | 680 | **412** |
| `M270Rockets` (M270, 1800) | 12 × (15000 + 1500) | 840 | 236 |
| `TosRockets` (TOS, 2000) | 24 × (3000 + 1500) + shockwave | 960 | ≥ 113 |
| `Ataka` (Mi-28) | 8 × (10000 + 2000) | 1200 | 80 |
| `Hellfire` (Apache) | 8 × (10000 + 2000) | 1600 | 60 |

Weapon rows from [dump]; pools from `vehicles-*.yaml` and `aircraft-*.yaml` (`russia:211` HIND rockets 80/10×80, `america:386` Hellfire 8×200).
**Raw is not delivered.** The scatter (Grad 4c0, Hind 2c0, both `Maximum`) against vehicle hit rectangles under centre-proximity scaling
(§4) probably discounts saturation by an order of magnitude, while guided missiles hit at ~77% (`260821-missile-hit-rate-vs-humvee.md` §0).
**So the table proves nothing about balance yet — it proves the static metric cannot rank these.** That matters, because item 32's
Grad↔M270 question (`260802-parity-audit.md` §2.5) is currently framed on "total volley 180k vs 240k", which is exactly this raw metric.

**Ranking — POLISH (measurement infrastructure).**
**Brief (no sign-off needed — tooling only).** Generalise `tunguska-aa-hit-rate.py` into a `delivered-damage.py`: any weapon × any
profile (circle or rectangle) × range × stationary/moving, output delivered damage per round and per supply credit. Add a `bmp2` rectangle
profile beside the existing `humvee` one. Acceptance: it reproduces the shipped script's published littlebird and humvee rows
**byte-for-byte at the same seed** before any new number is quoted. Then re-state audit §2.5 in delivered terms. Read
`DOCS/recipes/BALANCE.md` §"Anchor on ONE thing, then VALIDATE against a SECOND" — anchor on the 77% ATGM figure.

---

## Considered and dropped — so nobody re-scouts them

- **"The AR dominates the Rifleman at equal cost"** (static DPS vs infantry 1042 vs 219, 4.8×). **Refuted by [sim]:** the AR's scatter is
  twice as wide (`Inaccuracy 256` vs `128`). Its real edge is 1.3–1.7× in time to kill. It pays ~1.9× the supply per kill and has no RPG. Not dominated.
- **"Helicopters at 800 HP die to two 12.7 mm hits from a 600-credit BTR"** (static: 450/hit). **Refuted by [sim]:** under centre-proximity
  and scatter, a BTR needs ~41 rounds (11 s) against a *hovering* Apache at 8c and ~237 rounds at 16c. Against a *moving* one it gets
  **zero damaging rounds at any range** (no lead). The gunship's real killer is the missile, which is why #1/#2 exist.
- **Tunguska ground cannon as the best soft-target DPS in its band** (`260819-strike-shorad-parity.md:294-297`): already tuned on purpose,
  with measured kills per load, in the `30mm.Tunguska.AG` comment (`weapons-ballistics.yaml:678-690`), and it is pool-limited to 15 bursts.
- **Flamethrower `E4` shows 29 DPS** in static math: its damage is the `onfire` condition (`weapons-other.yaml:24-41` →
  `infantry.yaml:968-1001`), which the static model cannot see. Not a dead unit on this evidence.
- **BMP-2 is `~techlevel.low`, Bradley `~techlevel.medium`** (`vehicles-russia.yaml:163`): latent only, because `TechLevelDropdownVisible: false`
  (`world.yaml:588`).
- **Grad↔M270, Bradley↔BMP-2, M109↔Giatsint, crew survivability, HIMARS armour:** item 32 / AWAITING-USER item 3. Not re-proposed.

---

## Appendix — rerunning the [sim] numbers

Both wrappers import `tools/combat-sim/scripts/tunguska-aa-hit-rate.py` unchanged and override only `AMMO` and the target profile:

```python
import importlib.util
spec = importlib.util.spec_from_file_location('t', 'tunguska-aa-hit-rate.py')   # run from tools/combat-sim/scripts
t = importlib.util.module_from_spec(spec); spec.loader.exec_module(t)
t.AMMO = 10**9
rifleman = t.Profile('Rifleman', None, 200, 0, [], 25, t.Circle(30), ['Ground', 'Infantry'], True)   # prone: t.Circle(20)
apache   = t.Profile('Apache', None, 800, 20, [], 245, t.Circle(32), ['Air', 'AirHeavy', 'Helicopter', 'AirDetonateAttack'], False)
w = t.load_weapon('5.56mm.dmr')            # or '5.56mm.ar', '12.7mm.mg', '30mm.tunguska.aa'
r = t.simulate(rifleman, w, w['warheads'], 14336, w['inaccuracy'], False, 20000, 7, engagements=10)
print(r['mean_rtk'], r['pct_damaging'])
```

Seeds were 7 (infantry) and 20261007 (air); 20,000 rounds per cell. Helicopter hit shape `Circle r32` comes from `^NeutralAirborne`
(`aircraft.yaml:89-91`), and no helicopter overrides it.
