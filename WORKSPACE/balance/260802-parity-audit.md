# 260802 — Static Faction Parity Audit: US/NATO vs RU/BRICS

**Branch:** `auto/balance-audit` (base `main @ 7df21413`).
**Method:** static YAML analysis only — no game runs, no sims, no combat-sim executions, zero rules edits. Every claim cites `file:line` at the base SHA.
**Scope:** `mods/ww3mod/rules/ingame/*.yaml` (rosters), `mods/ww3mod/rules/weapons/*.yaml`, `mods/ww3mod/rules/ai/*.yaml` (bot config).
**Sign-off flow:** per `WORKSPACE/AWAITING-USER.md` — proposals in `WORKSPACE/balance/NNN-*.md` are documents only; **no stat changes without explicit per-proposal user approval**. Test configs authored here are **not run** (runs need a user grant).

**Cite legend** (paths under `mods/ww3mod/rules/`):
`inf` = ingame/infantry.yaml · `iusa`/`irus` = ingame/infantry-america/-russia.yaml · `V` = ingame/vehicles.yaml · `A`/`R` = ingame/vehicles-america/-russia.yaml · `B` = ingame/aircraft.yaml · `aA`/`aR` = ingame/aircraft-america/-russia.yaml · `WB` = weapons/weapons-ballistics.yaml · `WM` = weapons/weapons-missiles.yaml · `WE` = weapons/weapons-explosions.yaml · `ai` = ai/ai.yaml · `aiA`/`aiR` = ai/ai-america/-russia.yaml · `D` = defaults.yaml

**Classification key:**
- **FLAVOR** — asymmetric-but-fair by design intent; no action.
- **SUSPICIOUS** — plausible imbalance; needs measurement (see §6) before proposing changes.
- **CLEAR** — imbalance/bug evident from stats alone; proposal authored (§7).
- **COSMETIC** — no gameplay effect; noted for hygiene.

---

## 0. Damage-model primer (read before interpreting tables)

This mod has **almost no classic `Versus` tables**. Armor interaction is a custom **Penetration vs Thickness** model (`engine/OpenRA.Mods.Common/Warheads/DamageWarhead.cs:216-231`): effective thickness = `Armor.Thickness × Distribution%` for the struck facing (`Armor.cs:23-27`; TopAttack weapons hit the Top slot, `DamageWarhead.cs:131-134`). If `Penetration < effective thickness`, damage scales by `pen/thickness`; otherwise full damage. Additionally, armor-class tokens in `Targetable.TargetTypes` (Unarmored/Light/Medium/Heavy) hard-gate what small arms can hit at all. The only live `Versus` tables in scope are on the HIMARS/Iskander shockwaves (WE:544-548, WE:581-585).

Infantry armor types `None` (inf:34-35) and `Kevlar` (inf:173-174) declare **no Thickness**, so infantry armor currently gates only ValidTargets matching, not damage scaling.

**Vision:** all combat units share the layered `^StandardVision` (D:47-86, out to 32c0); **no buyable unit overrides it** — vision parity is exact across factions (extra sensing only via `Radar` traits, noted per unit).

---

## 1. Infantry — VERDICT: fully mirrored (FLAVOR n/a; parity exact)

**The infantry rosters are 100% mirrored by construction.** Every buyable faction actor is a pure pass-through wrapper: it inherits the shared `^X` template and overrides only `Buildable.Prerequisites` and `RenderSprites.Image` (e.g. `E3.america` iusa:18-23 vs `E3.russia` irus:18-23). Zero combat stats are set in either faction file; all Cost/HP/Speed/weapon values live in shared templates in inf.

15 buyable roles, all identical pairs: E1 Conscript (both `~disabled`, iusa:5/irus:5), E3 Rifleman+RPG, AR, E2 Grenadier, MT Mortar, TL Team Leader, AT (ATGM), AA (MANPADS), MEDI, SN Sniper, E6 Engineer, SF Special Forces, TECN Technician, DR Drone operator, E4 Flamethrower (wrapper pairs at iusa:2-128 / irus:2-128; stats at inf:1103-2267).

Shared chassis: HP 200 (inf:32-33), Kevlar (inf:173-174), Speed 25 (inf:45-49), prone/suppression systems (inf:260-272, 348-514), upkeep 5‰ of cost (inf:153-154).

One-sided content: **none active**. A commented-out RU Attack Dog exists (`# DOG.russia`, irus:130-136) — if ever enabled it would be RU-only; flag at that time.

**Implication:** any observed US-vs-RU infantry performance difference in testing cannot originate from infantry YAML — it must come from vehicles/aircraft, AI config (§4), or map/side bias.

---

## 2. Ground vehicles — pair-by-pair

Buyable rosters: US 8 (humvee, m113, bradley, abrams, m109, m270, strykershorad, HIMARS) vs RU 8 (btr, bmp2, t90, giatsint, grad, tos, tunguska, iskander), plus 4 shared neutrals (MSAR, MNLY, TRUK, LCCV — V:376-592). `vehicles-ukraine.yaml` is **not loaded** (mod.yaml rules list omits it) — its t72 is dormant content.

### 2.1 MBT: abrams ↔ t90 — FLAVOR (compensated trade)

| Stat | abrams | t90 | Cite |
|---|---|---|---|
| Cost | 2500 | 2400 | A:481 / R:316 |
| HP | 28000 | 24000 | A:483 / R:318 |
| Thickness / Dist F,S,R,T,B | 700 / 100,40,15,10,10 | 280 / 100,60,40,15,15 | A:486-487 / R:321-322 |
| Speed | 90 | 100 | A:500 / R:332 |
| Gun (same shell 20000/pen 800) | range 25c0, wait 130 | range 24c0, wait **110** | WB:574-607 |
| Crew threshold/survival | 38% / 95% | 8% / 30% | A:462-463 / R:297-298 |

Mutual TTK is symmetric at 2 hits each (pen 800 beats both fronts); T-90 lands its second shot 20 ticks sooner and moves faster; Abrams resists mid-tier weapons far better (25/30mm pen 60: ~9% vs Abrams front, ~21% vs T-90 front; RPG pen 500: 71% vs Abrams front, 100% vs T-90) and keeps its crew. Reads as deliberate east/west doctrine. **Measure via mirror+cross tests, no proposal.**

### 2.2 IFV: bradley ↔ bmp2 — SUSPICIOUS (BMP-2 may be strictly better value)

| Stat | bradley | bmp2 | Cite |
|---|---|---|---|
| Cost / tech | 1500, medium | **1300, low** | A:324,321 / R:159,156 |
| HP / Armor | 14000, Medium 15 | identical | A:328-332 / R:161-165 |
| Autocannon DPS (dmg·burst/cycle) | 25mm: 500×4/29 ≈ 69/tick, 20c0 | 30mm: 500×6/25 ≈ **120/tick**, 19c0 | WB:387-408 / WB:410-431 |
| ATGM | 2-missile salvo per 1000 (burst-kill 20000) | 1 per 500 (same average) | WM:93-97 / WM:47 |
| Mobility | lighttracked | lighttracked-**amphibious** | A:341 / R:174 |
| Cargo | 6 | 7 | A:420 / R:264 |
| Crew threshold/survival | 30/85 | 12/45 | A:305-306 / R:143-144 |

Identical hulls, but BMP-2 is 200 cheaper, a tech tier earlier, ~74% higher sustained gun DPS, amphibious, +1 seat. Bradley's compensation: salvo-ATGM front-load, +1c0 gun range, crew survival. On paper the RU package looks better priced; **needs measurement** (the salvo front-load and range edge are exactly what static math can't weigh).

### 2.3 APC: m113 ↔ btr — SUSPICIOUS (BTR better hull per credit)

m113: 700 cost, 12000 HP, thickness 15, speed 100, **12 seats** (A:205-267). btr: 600 cost, **14000 HP**, thickness 10, speed 110, 8 seats (R:44-105). Same 12.7mm.MG (A:229 / R:67). Cost/HP: 0.058 vs 0.043. BTR wins hull value; M113 carries 50% more. Role-fair on paper but the AI uses them differently (§4) which muddies bot-vs-bot attribution. COSMETIC sub-finding: BTR tooltip claims 14.5mm MG but mounts 12.7mm.MG (R:39 vs R:67).

### 2.4 SPH: m109 ↔ giatsint — SUSPICIOUS (Paladin better at equal cost)

Identical: cost 1800, HP 14000, speed 80, 39 shells, same base shell 15000/pen 1000/40c0 (A:586-637 / R:424-466). Differences: Paladin fires 3-shell volleys per 480 ticks (~12.5% higher sustained throughput, front-loaded) with a **turret** (TurnSpeed 5, SetupTicks 25); Giatsint fires singly per 180 via **hull-turn** (AttackFrontal, SetupTicks 50) (WB:638-646; A:617-624,663 / R:489-495). Giatsint's only edge: thickness 19 vs 10 (R:431 / A:593) and crew 20/65 vs 24/75 (R:404-405 / A:571-572 — US wins that too). At equal cost the Paladin looks strictly better at the role's core job. **Needs measurement** (counter-battery exposure of the 3-shell volley could offset).

### 2.5 MLRS: m270 ↔ grad — FLAVOR leaning SUSPICIOUS

m270: 1800, 12 rockets × (15000/pen 500), inaccuracy 2c128, speed 80 (A:712-743; WB:768-799). grad: **1500**, 40 × (6000/pen 250), inaccuracy 4c0, speed **110** (R:537-567; WB:700-732). Total volley 180k vs **240k**; precision vs saturation is coherent doctrine, but the Grad is also 300 cheaper *and* faster. Both one-shot + Evacuate (A:700-705 / R:525-530). **Measure**; no proposal from stats alone.

### 2.6 SPAA: strykershorad ↔ tunguska — CLEAR bug + SUSPICIOUS design divergence

**CLEAR — duplicate `Health:` on tunguska:** HP declared 14000 (R:786-787) and re-declared 8000 (R:799-800) *within the same actor node*. Whatever way engine merge resolves it, the duplicate is an authoring bug; if the later key wins, the RU SPAA fields **8000 HP vs the Stryker's 14000**. → **Proposal 001**. (Effective value verifiable without a game run via `./tools/combat-sim/scripts/dump-stats.sh` once utility runs are permitted.)

Design divergence (SUSPICIOUS, measure): stryker 2500 = tri-role (25mm gun + Stinger.quad + 4 Hellfire pen-800 + **9 seats**, A:869-977); tunguska 1700 = pure AA (AG+AA 30mm guns + 9M311 at 5× the Stinger's sustained SAM cadence — 1/40 ticks vs Magazine-4/reload-1000, WM:403-413), presents `Heavy` so Light-only weapons can't target it (R:792-793), but hull pauses while firing SAMs (R:806). Not comparable at equal cost by design; the AI pays 47% more per US SHORAD at identical build ceilings (§4).

### 2.7 Ballistic: HIMARS ↔ iskander — CLEAR (Iskander strictly superior at identical cost)

Identical: cost 6000, tech high, 2 missiles, targeter 50c0/min 16c0, RequiresForceFire, 10× damage-taken while loaded (A:996-1053 / R:910-971).

| Stat | HIMARS | iskander | Cite |
|---|---|---|---|
| Warhead direct | 36000 (80% at max) | **54000 (100% at max)** | WE:564-567 / WE:527-530 |
| Impact spread | 2500/pen 1800 @768 | **4000/pen 2500 @1024** | WE:568-573 / WE:531-537 |
| Shockwave | 7000/pen 1500, radius 2c512 | **12000/pen 2000, radius 4c0** | WE:574-588 / WE:538-551 |
| Chassis HP | **6000** | 10000 | A:1015 / R:929 |
| Speed | 70 | 80 | A:1030 / R:945 |
| HIMARS edges | hull turn 10 vs 6; missile flies faster; missile HP 50 vs 100 (interception) | | A:1030-1031, A:1108-1117 / R:945-946, R:1025-1039 |

+50% direct damage, ~2.6× shockwave area, +67% chassis HP, +10 speed — for the same 6000. The HIMARS' only compensations are marginal. → **Proposal 002**.

### 2.8 No-counterpart units — FLAVOR (note the gaps)

- **humvee** (US-only): 450-cost, speed-150 recon/8-seat (A:2-160). RU's cheapest/fastest vehicle is btr (600, 110) — RU has no light recon.
- **tos** (RU-only): 2000-cost thermobaric MLRS, 24×3000 volley @28c0, 20000 HP Medium presenting **Heavy** (R:624-745). US has no anti-infantry saturation analogue.
These roughly trade off; but note **neither is compensated in the AI config** (§4).

### 2.9 Systematic pattern: crew survivability — FLAVOR (uncompensated, watch it)

Every US vehicle out-survives its RU pair on crew stats: abrams 38/95 vs t90 8/30; bradley 30/85 vs bmp2 12/45; m113 26/80 vs btr 22/70; stryker 24/78 vs tunguska 22/70; m109 24/75 vs giatsint 20/65; MLRS pair equal 14/50 (cites in tables above). Crew loss disables movement/turret with graceful-degradation modes (V:252-297). Clearly deliberate doctrine flavor — but it is a *systematic* US edge whose price appears nowhere in costs. If mirrors are clean and cross shows a US lean, this is a prime suspect.

Other symmetric mechanics checked and equal: cookoff tiers by class (WE:7-74), upkeep 5‰ (V:113-114), suppression effects on vehicles (V:306-360), ATGM firing-slow on both IFVs (A:350-352 / R:183-185), no smoke/ERA exists on either side (Abrams `JamsMissiles` commented out, A:495-497).

---

## 3. Aircraft — pair-by-pair (naval: nonexistent both sides)

**Naval:** `naval.yaml` is fully commented out; faction files empty. Parity vacuously exact.
**Fixed-wing:** A10, F16, MIG, FROG all `~disabled` (aA:417,529 / aR:432,540) — latent content, see §3.5.
**Buyable roster is exactly 6:** TRAN, littlebird, HELI (US) / HALO, HIND, MI28 (RU).

### 3.1 Transports: TRAN ↔ HALO — SUSPICIOUS-minor (one stat)

Identical (cost 2000, tech medium, HP 600, Light/10, turn 8, Cargo 36, crew Pilot+Copilot) **except Speed: TRAN 240 vs HALO 220** (aA:46 / aR:46). A 9% US speed edge at equal cost with no visible compensation. Tiny, but unexplained — confirm intent or equalize after measurement.

### 3.2 Heavy attack: HELI (Apache) ↔ MI28 — CLEAR bug inside a FLAVOR design

Identical airframes: cost 6000, tech high, HP 800, Heavy/20, speed 245, turn 12, AirRadar 24c0, same 30mm gun (200 rds), 8 missiles (aA:294-363 / aR:295-374).

Deliberate missile asymmetry (inline design comments, aR:315-320, aR:369-374): Hellfire = fire-and-forget, 25c0, pen 800, 200 supply/msl, **ValidTargets includes Air** (WM:152-203); Ataka = SACLOS, 22c0, pen 900, 150 supply/msl, 50% self-slow while guiding (aR:321-327), **no Air** (WM:105-113). "Expensive fire-and-forget vs cheap-but-committed" — coherent FLAVOR.

**CLEAR — Mi-28's advertised anti-air is non-functional:** `AttackAircraft.Armaments: primary, secondary, secondary-air` (aR:312), `GrantConditionOnPreparingAttack.ArmamentNames` (aR:322) and `AmmoPool@2.Armaments` (aR:367) all reference an armament named `secondary-air` that **is defined nowhere** (no `Name: secondary-air` in any ingame yaml; the HIND has the same reference commented out, aR:153). The buildable description claims "Can engage aircraft" (aR:291), but Ataka can't target Air and the 30mm.Heli override is `ValidTargets: Ground` (WB:483). Net: **Apache genuinely engages air; Mi-28 cannot**, at equal cost, contradicting its own tooltip. → **Proposal 003**.

### 3.3 Mid-tier: littlebird / HIND — FLAVOR with a SUSPICIOUS tier-access skew

No true counterparts. littlebird (US): 3000, **tech medium**, 300 HP, speed 265, minigun + 2 Hellfire, 4 seats, AI role Scout (aA:99-224). HIND (RU): 4000, **tech high**, 800 HP Heavy/10, speed 195, 12.7mm + 80 S-8 rockets, 8 seats, AI role AttackHeavy (aR:89-254).

Tier access: **US gets armed air a full tech level earlier**; RU fields nothing armed until high. Under the current default (tech unrestricted in bot games, `world.yaml:425` + engine default) this is moot for bots but live for humans on tech-limited setups. Roster breadth at high tier favors RU (2 attack helis vs 1). Deliberate shape; measurement will price it.

Sub-finding (COSMETIC-leaning): HIND hand-rolls its AutoTargetPriority with an Air tier (aR:118) yet neither weapon can hit fixed-wing Air (12.7mm hits `Helicopter` type; RocketPods Ground-only, WB:215/669) — wasted priority tier.

### 3.4 Rearm economics — FLAVOR

Hellfire 200/msl (aA:363) vs Ataka 150/msl (aR:374); S-8 80/rkt (aR:206); 30mm 5/rd both (aA:334 / aR:342). Priced with the design comments; fine. Minor: HIND is the only heli with explicit per-batch ReloadDelay on its pools (RD 6/16, aR:171/202) — others fall to trait default; runtime-verify some day.

### 3.5 Latent (disabled) fixed-wing — not at parity, flag for whenever enabled

F16 HP 400/Medium 10 vs MIG HP 550/Medium **3** (thickness likely unintended inherit, aA:536-539 / aR:551-553). A10 has a **duplicate `ReloadAmmoPool@1` key** (aA:451 and aA:481) — the gun pool's dock-reload entry gets overridden, so an enabled A10 would never rearm its gun. No proposals (dead content), but these belong on `WORKSPACE/bugs/discovered.md` the day the planes wake up.

---

## 4. AI / bot config parity (`rules/ai/`) — NOT fully symmetric

A bot-config asymmetry masquerades as faction imbalance in bot-vs-bot data. Both bots (`ModularBot@experimental` ai:29-31, `@stable` ai:32-34) were diffed; faction gating (`player.nato`/`player.brics`) is wired symmetrically (player.yaml:235-258); every `player.nato` block has a `player.brics` twin.

**Symmetric and clean:** infantry build values byte-identical (all 11 entries + limits, aiA:12-26,39-45 / aiR:11-25,38-45); AdaptiveProduction counter pools role-equivalent with identical parameters (ai:435-494); scouting params identical (ai:379-393); capture, defense, supply, base-builder (inert — all defenses `~disabled`), squad exclusion lists all mirrored.

**Asymmetries found, by severity:**

| # | Asymmetry | Cites | Severity |
|---|---|---|---|
| A1 | **Attack-heli fleet cap: RU 4×HIND + 3×MI28 (7 airframes, $34k) vs US 4×HELI (4, $24k)**; US 3rd heli type is a Scout, RU's is a 2nd attacker | ai:678-689 vs ai:732-743 | definitely-skews |
| A2 | **Ground-vehicle slot mismatch: US m113 (TransportLift taxi, uncapped at 15%) ↔ RU grad (IndirectFire, cap 2)** — RU gets ~10%-pts more offense-axis-eligible vehicle ceiling on both profiles and an extra fires-doctrine platform on @experimental (offense-eligible ceilings: US 35 vs RU 45 @experimental; 75 vs 85 @stable) | aiA:32 vs aiR:33; ai:220-224, 271-297, 852 | definitely-skews |
| A3 | @stable ExcludeUnitTypes removes 40%-pts of US vehicle ceilings vs 25 for RU (bradley+m113 vs bmp2) | ai:852 (also :220,:320,:365,:871) | could-skew |
| A4 | Carrier pool for mounted doctrine: US 2 types (bradley,m113) vs RU 1 (bmp2) | ai:556, 575 | could-skew |
| A5 | MI28 UnitDelay 3500 vs 2500 for all US airframes | ai:688 | could-skew |
| A6 | Heli scouting functional only for US (littlebird is the only Scout-role heli) | ai:754,767; aA:110-118 | could-skew (fog-respecting @experimental) |
| A7 | AA price at equal ceiling/limit: strykershorad 2500 vs tunguska 1700 | aiA:98 / aiR:98; A:838 / R:785 | could-skew (roster-priced) |
| A8 | Fixed-wing weights differ (A10 40/F16 30 vs MIG 30/FROG 20) — inert, all `~disabled` | ai:640-644, 699-703 | cosmetic |
| A9 | `paladin` token in LayeredDefence MainLine lists is not an actor (the actor is `m109`) | ai:529, 901 | cosmetic |
| A10 | "Conscripts" comment mislabels tecn entries | aiA:11 / aiR:10 | cosmetic |

**Bottom line:** a US-vs-RU bot series is **not** a clean unit-stats experiment. A1/A2 alone are enough to skew win rates independent of roster balance. Interpretation rule for §6's cross probe: a faction skew there = roster + AI-config combined; only the mirrors + per-pair duel evidence can separate them. If we ever want a stats-only cross probe, the AI layer needs a mirrored-config variant first (possible future proposal — not authored now, since it's an AI-config change, not a balance change).

---

## 5. Findings summary

**CLEAR (proposals authored, §7):**
1. Tunguska duplicate `Health:` — effective HP likely 8000 vs intended 14000 (R:786-787 vs R:799-800). → `001-tunguska-duplicate-health.md`
2. Iskander strictly dominates HIMARS at identical cost 6000 (WE:521-588; A:1015/R:929). → `002-himars-iskander-parity.md`
3. Mi-28 advertised AA non-functional: dangling `secondary-air` armament + no Air ValidTargets, vs Apache's live AA at equal cost (aR:291,312,322,367; WM:111/158). → `003-mi28-secondary-air.md`

**SUSPICIOUS (measure before proposing):** bradley↔bmp2 value (§2.2), m113↔btr hull value (§2.3), m109↔giatsint throughput (§2.4), m270↔grad price/volley (§2.5), stryker↔tunguska role pricing (§2.6), TRAN↔HALO speed (§3.1), littlebird/HIND tier access (§3.3), systematic US crew-survival edge (§2.9).

**Bot-config (fix separately from balance):** A1-A7 (§4) — especially A1 (heli fleet) and A2 (m113↔grad slot).

**COSMETIC/hygiene:** BTR tooltip 14.5mm vs actual 12.7mm (R:39/R:67); HIND dead Air priority tier (aR:118); `paladin` ghost token (ai:529,901); "Conscripts" comment (aiA:11/aiR:10); A10 duplicate ReloadAmmoPool@1 + F16/MIG thickness skew (dormant, §3.5); vehicles-ukraine.yaml unloaded (mod.yaml:131-133 area). These belong in `WORKSPACE/bugs/discovered.md` / normal fix flow, not balance proposals.

---

## 6. Measurement plan — authored test configs (NOT run; runs need user grant)

Four scenarios authored under `tools/autotest/scenarios/`, cloned from the `tournament-arena-skirmish-2p` conventions (same terrain `map.bin`, same SR/spawn placement, same `tournament.yaml` schema, `Bot: stable` both sides). The match watcher selects combatants by `!NonCombatant && IsBot` (BotVsBotMatchWatcher.cs:214) — player names are free; verdict JSON records each player's faction (:537), which `aggregate-tournament.sh` keys on (`faction_winrate_pct`).

| Scenario | Layout | Measures |
|---|---|---|
| `tournament-parity-mirror-us` | US-A (left) vs US-B (right), both `Faction: america` | Bot skill noise + side/map bias with roster held constant. Metric: `winner_name` winrates in `summary.json` — expect 40-60% band. |
| `tournament-parity-mirror-ru` | RU-A vs RU-B, both `Faction: russia` | Same, RU roster. Also: do RU-roster games crash/timeout more? |
| `tournament-parity-cross-usru` | USA-bot (left, america) vs Russia-bot (right, russia) | Faction skew, side A. |
| `tournament-parity-cross-usru-swapped` | Same map; factions swapped (left plays russia) — names kept per the arena-mirror convention | Cancels side bias when alternated with the above. |

**Commands (each needs an explicit user grant to run — multi-test rule):**

```bash
# Mirrors — read winner_name winrates from <result-dir>/summary.json
./tools/autotest/run-tournament.sh tournament-parity-mirror-us --seeds 20
./tools/autotest/run-tournament.sh tournament-parity-mirror-ru --seeds 20

# Cross probe — alternates side assignments per seed; read faction_winrate_pct
./tools/autotest/run-tournament.sh tournament-parity-cross-usru --seeds 20 \
    --mirror tournament-parity-cross-usru-swapped
```

**Reading the results:**
- Mirrors: `winner_name` split outside ~40-60% over 20 seeds ⇒ side/map bias — fix that before trusting the cross probe. Compare US-mirror vs RU-mirror average scores/durations for roster-wide pathologies.
- Cross: `faction_winrate_pct` outside the noise band ⇒ combined roster + AI-config skew. Attribute using §4 (A1/A2 are confounds) and per-pair duels (`test-balance-*` scenarios exist for tank/ifv/arty/heli/at duels) before blaming any single unit stat.
- Seeds are reproducible (`Test.RandomSeed = i*1000+17`, run-tournament.sh header) — a lopsided seed can be replayed for inspection.
- To probe the @experimental bot instead, change both `Bot: stable` fields in the scenario's map.yaml to `Bot: experimental` (matchup block in tournament.yaml is informational only).

---

## 7. Proposals authored (documents only — each awaits individual user sign-off)

See `WORKSPACE/balance/README.md` for the flow. Authored now, from stats-alone evidence:

- `001-tunguska-duplicate-health.md`
- `002-himars-iskander-parity.md`
- `003-mi28-secondary-air.md`

Nothing else meets the "clear from stats alone" bar; all SUSPICIOUS items wait for measurement data.
