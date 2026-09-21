# Rank system audit — 2026-09-19

**Ref: `wt/rank-audit @ 5a338b73`** (forked from `main @ 64185a89`; `5a338b73` is this audit's own
WIP commit, so every rules/engine claim below is equally true at `64185a89`).
Nothing was executed: no build, no game launch, no `--check-yaml`, no `make test`. Every number was
read from the cited `file:line`, or computed by the committed script in `tools/rank-audit/` from
those files.

**Headline.** There is essentially nothing to audit *per unit*: all 20 rank multiplier values are
**byte-identical on all 113 ranking actors** — not one actor overrides or removes a single
multiplier. So the five groups in §3 are groups of *earning rules*, not of bonuses. The real
findings are about who can **reach** that one profile and who can **use** it:

- **One flat profile is not right for every class, and the numbers say so by a factor of ~21.**
  Rank 1 costs 1× the unit's own cost in enemy value killed — cost-normalised, and therefore fair.
  But measured in *kills of a cheap target*, that spans **2 → 120** across the roster (60:1), while
  the free purchase-accrual path spans only **3.2 → 9.0 minutes** (2.8:1). Cheap infantry rank up
  in two trades; a 6000-credit MBT would need 120 conscript kills for rank 1 and **960 for rank 4**.
  **Rank 4 cannot be purchased at all**, so in practice rank 4 is an infantry-only rank (§4.2).
- **The concealment axis is inverted against its own design intent.** It lands 4 of 4 rank steps on
  vehicles and aircraft, **1 of 4 on line infantry in cover, and 0 of 4 on snipers and special
  forces** — the two units whose entire identity is concealment already clamp at the ceiling before
  veterancy is considered (§4.3).
- **Classes that can never rank by combat:** `MEDI` (heal-only armament), `TRAN`/`HALO` (no
  armament), `quadcopterdrone` (no armament *and* not buildable, so no accrual either), `DR` (its
  only lethal weapon can only kill drones, and drones award **zero** XP), and twelve unarmed
  civilians. Plus everything with no rank trait at all: `TRUK`, `MNLY`, `MSAR`, `LCCV`, and `SAM`
  (§5).
- **Two files in `mods/ww3mod/rules/` are not loaded by `mod.yaml`** and one commented-out rank
  bonus references a trait that does not exist (§2, §1.3).

**Companion artifacts**
- `WORKSPACE/audit/rank-system-260919.csv` — 113 rows, one per ranking actor, 18 columns.
- `tools/rank-audit/rank_audit.py` — regenerates the CSV and every table in §3/§4.
- `tools/rank-audit/miniyaml.py` — a port of the engine's MiniYaml loader
  (`engine/OpenRA.Game/MiniYaml.cs:399-600`), so the inheritance resolution behind §3 is
  reproducible rather than hand-derived.

```
python tools/rank-audit/rank_audit.py --csv WORKSPACE/audit/rank-system-260919.csv
python tools/rank-audit/rank_audit.py --groups        # §3, the grouped unit list
python tools/rank-audit/rank_audit.py --accrual       # §4.2, kill path vs purchase path
python tools/rank-audit/rank_audit.py --concealment   # §4.3, the capped axis
python tools/rank-audit/rank_audit.py --unit MEDI.america
```

**The resolver is validated against two numbers nobody wrote for it.** `player.yaml:18-21` asserts,
in prose, that rank-1 accrual runs "2m59s for a 50-credit Conscript to 8m59s for a 6000-credit
Iskander … rank 3 is 27m to 81m". The script's independent implementation of `RankAccrual` returns
**2m59.6s** and **8m59.3s**, and 29.1–80.9 min at rank 3. That is a strong check on both the cost
resolution and the accrual maths.

---

## §1 — How it works today

### 1.1 One template, one condition, four levels

All veterancy comes from `^GainsExperience` (`mods/ww3mod/rules/defaults.yaml:302-303`):

```
GainsExperience:                       # defaults.yaml:303-309
    LevelUpNotification: LevelUp
    Conditions:
        100: rank-veteran
        200: rank-veteran
        400: rank-veteran
        800: rank-veteran
    LevelUpImage: crate-effects
```

All four thresholds grant the **same condition name**, and `GiveExperience` calls `GrantCondition`
once per level crossed (`GainsExperience.cs:124-128`), so the token **stacks**: consumers read
`rank-veteran == 1 … == 4`. `MaxLevel` is just `Conditions.Count` (`GainsExperience.cs:79`) — **the
number of ranks is the number of lines in that map**, so adding a fifth rank is one line.

### 1.2 XP comes from kills only, priced in the victim's cost

`GivesExperience` sits on `^ExistsInWorld`, so nearly everything awards XP when it dies. The award
fires from `INotifyKilled.Killed` (`GivesExperience.cs:57`) — **there is no damage-dealt,
time-alive, healing, repair, capture or objective XP path anywhere in the mod.**

- Award = victim's `Valued.Cost` (`GivesExperience.cs:50-52`; `Experience` defaults to `-1` at
  `:22`) × `ActorExperienceModifier`, which defaults to **10000** (`:28`) and is never overridden.
  `Util.ApplyPercentageModifiers` is ×p/100, so that is **×100**.
- Thresholds are `Key × requiredExperience`, where `requiredExperience` is the actor's own `Cost`
  (`GainsExperience.cs:88-91`).

**The two ×100s cancel:**

| Rank | Threshold | In plain terms |
|---|---|---|
| 1 | 100 × own cost | kill enemy value equal to **1×** your own cost |
| 2 | 200 × own cost | **2×** your own cost, cumulative |
| 3 | 400 × own cost | **4×** cumulative |
| 4 | 800 × own cost | **8×** cumulative |

Only `Neutral` and `Enemy` kills count (`GivesExperience.cs:25`) — team-killing does not promote.

**A latent trap in that fallback.** `requiredExperience` falls back to **1**, not 0, when the actor
has no `Valued` (`GainsExperience.cs:88-89`). So any actor without a cost gets thresholds of
100/200/400/800 XP *absolute* — roughly 100× cheaper than any priced unit. That is live on
`quadcopterdrone` today (§5.3), and it is the same mechanism as the deliberate crew override (§3.D).

Two mod-side modifiers to the earn rate:

- **A passenger earns nothing.** `GainsExperienceMultiplier: Modifier: 0` gated on
  `disable-experience` (`infantry.yaml:94-96`), granted by `Passenger.CargoCondition` (`:89`).
- **A Team Leader's aura is +50% XP.** `GainsExperienceMultiplier@TLBoost: Modifier: 150` on
  `morale-boost` (`infantry.yaml:273-275`). `^TL` removes it from itself (`infantry.yaml:1517`).
  **Infantry only** — no vehicle, aircraft or civilian carries either modifier (§3, Group B).

### 1.3 What a rank grants — five axes

From `^GainsExperience` (`defaults.yaml:317-373`), gated on `rank-veteran == N`:

| Axis | Trait | R1 | R2 | R3 | R4 | Lines |
|---|---|---|---|---|---|---|
| Damage **taken** | `DamageMultiplier` | 95 | 90 | 85 | 80 | `defaults.yaml:329-341` |
| Damage **dealt** | `FirepowerMultiplier` | 105 | 110 | 115 | 120 | `:342-353` |
| Move + turn speed | `SpeedMultiplier` | 105 | 110 | 115 | 120 | `:354-365` |
| Reload delay | `ReloadDelayMultiplier` | 95 | 90 | 85 | 80 | `:366-377` |
| **Concealment** | `DetectableAddativeModifier` | +1 | +2 | +3 | +4 | `:317-328` |

Semantics worth stating because the obvious reading is wrong:

- **`DetectableAddativeModifier` is a CONCEALMENT bonus, not a vision bonus.** Its `[Desc]` is
  *"Modifies the required vision to see this actor"* (`DetectableAddativeModifier.cs:16`) and
  `DetectableInfo.Vision` is *"What level of vision is required to detect this actor"*
  (`Detectable.cs:23-24`). **Higher = harder to see.** A rank-4 unit is harder to spot; it does not
  see further. It is additive into `ClampConcealment` (`Detectable.cs:85`, `:146`) and **is capped**
  (§4.3).
- `SpeedMultiplier` is an `ISpeedModifier`, consumed by both `Mobile` and `Aircraft`; on aircraft it
  also scales turn rate. Aircraft ranks are not cosmetic.
- **`FirepowerMultiplier` applies to EVERY armament on the actor**, not just the offensive one. So a
  rank-4 medic heals 20% harder and a rank-4 engineer repairs 20% harder — see §5.1 and §5.6.
- A **commented-out** suppression-resistance block sits at `defaults.yaml:311-314`
  (`ConditionModifier@Rank_1 … Condition: suppressed, Modifier: -1`). **`ConditionModifier` is not a
  live trait** — `engine/OpenRA.Mods.Common/Traits/Multipliers/ConditionModifier.cs` has zero
  non-comment lines, and what is commented out inside it is a stale copy of
  `DetectableAddativeModifier`. Uncommenting the YAML would fail the rules load. It reads like a
  one-line re-enable and is not one.

Visuals: `WithDecoration@Rank_1..4`, `Image: rank`, `Sequence: rank-veteran-N`,
`ValidRelationships: Ally` (`defaults.yaml:379-421`) — **ally-only; you cannot see an enemy's
rank**, the `Enemy, Neutral` values being commented out on each of the four entries. Sequences at
`sequences/sequences-misc.yaml:581-586`. Promotion sound is `LevelUpNotification: LevelUp`; the
promotion sprite is suppressed for units you do not own so a level-up does not reveal an enemy
position (`GainsExperience.cs:134-137`).

### 1.4 The WW3MOD-specific half: a rank is also a CURRENCY

This has no Red Alert ancestor and is more than half the system.

**Free accrual.** `RankAccumulation` on the player (`player.yaml:22`, declared **bare**, so every
value below is the C# default — `RankAccumulation.cs:281-303`) runs one timer and one stock *per
actor type*:

| Field | Value |
|---|---|
| `Rank1BaseIntervalTicks` | 2400 |
| `CostReferenceBuildTicks` | 100 |
| `Rank1IntervalMultiplier` | 2700 |
| `Rank1MaxIntervalTicks` | 9000 |
| `HigherTierIntervalMultiplier` | 300 |
| `Caps` | `{3, 2, 1}` |

Interval = `BaseTicks + isqrt(build × reference) × Rank1Multiplier / 100`, capped
(`RankAccrual.Rank1IntervalTicks`, `:90-103`). Deliberately **not** linear in cost: the doc comment
records that linear scaling spread the roster 120:1 (`:79-88`). Each tier up is ×3 (`:111-121`).
At the shipped `Timestep: 60` ms (`mod.yaml:404-406`, `DefaultSpeed: default`) that is 16.67 ticks
per second — **not 25 tps**, per CLAUDE.md's standing correction.

**Only units that can hold a rank accrue one.** `Accrues` requires both `BuildableInfo` **and**
`GainsExperienceInfo` (`RankAccumulation.cs:349-357`). An actor without `^GainsExperience` is
excluded from the currency too — the two halves fail together, which is why §5's "gains nothing"
units gain nothing twice over.

**Ranks 1–3 only.** `MaxPurchasableRank = 3` (`RankAccumulation.cs:59`) — *"Rank 4 is forged in
combat only, never purchased."*

**Spending** happens in the production queue: `PeekRank` picks the highest held tier and adds a
`VeterancyLevelInit` (`ProductionQueue.cs:790-792`); `CommitRank` spends it only once the actor is
really out (`:796-799`), so a failed `Produce` does not burn stock.

**Recovery refunds rank.** `CreditsRankOnEvacuation` (`defaults.yaml:311`, on every ranking actor)
fires on `INotifySold.Sold`, reached only by an actor that physically got to the map edge
(`CreditsRankOnEvacuation.cs:46-51`). A recovered rank-4 credits back **rank 3**, clamped rather
than dropped (`:60-63`). Ejected crew credit a *fraction* of a rank toward the exact vehicle type
they bailed out of (`:65-68`, `crew.yaml:45-50`); a whole crew's shares sum to one rank
(`RankAccrual.ShareTicks`, `:160-169`). Recovery stock lands in `BonusStock` and is **not** capped
by `Caps` — documented as deliberate (`RankAccumulation.cs:174-181`).

---

## §2 — Bots vs humans, and three dead files

`RankAccumulation` is on the player actor with no owner gate and nothing in the trait branches on
`IsBot`, so **bots and humans accrue and spend identically**.

**`mods/ww3mod/rules/disable-player-experience.yaml` is NOT LOADED.** It is absent from `mod.yaml`'s
`Rules:` list (the script merges exactly that list — 38 files) and referenced nowhere in `mods/`,
`tools/` or `DOCS/`. It would zero `PlayerExperienceModifier` and the RA-era capture/repair/cash XP
hooks. Because it never loads, `^ExistsInWorld`'s default is live. Five of the actors it names
(`MECH`, `THF`, `SPEN`, `SYRD`, `FIX`) are Red Alert units that do not exist in WW3MOD. Dead
artifact or un-wired intent; either way it is misleading as it stands.

**`mods/ww3mod/rules/ingame/vehicles-ukraine.yaml` is not loaded either** — also absent from
`mod.yaml`. It carries an `Inherits@GainsExperience: ^GainsExperience` at `:4` that resolves for
nobody.

**Naval cannot rank because naval does not ship.** `naval.yaml`'s `^Naval` veterancy wiring is
commented out (`naval.yaml:4`, `:186`) and the resolver finds zero concrete naval actors.

---

## §3 — THE LIST: every ranking unit, grouped by identical bonus profile

**113 ranking actors in 5 profiles**, out of 459 concrete actors. Because all 20 multiplier values
are identical everywhere, the groups differ **only** in XP-rate modifiers, `ExperienceModifier`, and
the pilot-eject extra. Every group shares this profile:

> thresholds `100/200/400/800` (= 1×/2×/4×/8× own cost in enemy value killed) ·
> firepower `+5/+10/+15/+20%` · reload delay `−5/−10/−15/−20%` · speed & turn `+5/+10/+15/+20%` ·
> damage taken `−5/−10/−15/−20%` · concealment `+1/+2/+3/+4` · rank chevron (ally-only) ·
> evacuation credits rank back

`R1 min` below is wall-clock minutes per free accrued rank-1; `kills` is how many 50-credit targets
must die to that unit to reach rank 1 (×8 for rank 4). Both from `--accrual`.

### Group A — Line infantry + pilots (54 actors)

Profile: the shared set **plus BOTH XP-rate modifiers** — `+50%` under a Team Leader
(`morale-boost`), `0%` while riding as a passenger (`disable-experience`).
Template: `^Infantry` → `Inherits@GainsExperience: ^GainsExperience` (`infantry.yaml:4`).

| Unit | Faction | Cost | kills | R1 min | Note |
|---|---|---|---|---|---|
| `AR` | A + R | 100 | 2 | 3.2 | |
| `E2` | A + R | 100 | 2 | 3.2 | |
| `E3` | A + R | 100 | 2 | 3.2 | |
| `DR` | A + R | 150 | 3 | 3.4 | drone operator — **cannot earn XP, §5.4** |
| `E4` | A + R | 150 | 3 | 3.4 | |
| `E6` | A + R | 250 | 5 | 3.8 | engineer — `Repair`/`ClearMines` **do** scale with rank, §5.6 |
| `MEDI` | A + R | 250 | 5 | 3.8 | medic — **cannot earn XP, §5.1** |
| `AA` | A + R | 300 | 6 | 3.9 | |
| `AT` | A + R | 300 | 6 | 3.9 | |
| `MT` | A + R | 300 | 6 | 3.9 | |
| `SN` | A + R | 400 | 8 | 4.1 | sniper — **0 of 4 concealment steps land, §4.3** |
| `SF` | A + R | 600 | 12 | 4.5 | special forces — **0 of 4, §4.3** |
| `E1` | — | 50 | 1 | — | **`~disabled`; does not ship buildable** |
| `E1R1`/`E2R1`/`E3R1` (+ faction twins) | not buildable | 50–100 | — | — | ship pre-ranked at level 2 |
| `PILOT` / `PILOTR1`–`R4` | not buildable | 500–3000 | — | — | ejected pilots; `R1..R4` carry the airframe's rank |
| `E3.colorpicker` | not buildable | 100 | — | — | UI actor |

*"A + R" means the type exists three times: a `~disabled` faction-neutral base and two real
`.america` / `.russia` twins. Costs and bonuses are identical across the three.*

### Group B — Vehicles, helicopters, drone (25 actors)

Profile: the shared set, **no XP-rate modifiers at all** — no Team Leader boost, and no passenger
freeze either, because neither is declared outside the infantry chain.

| Unit | Faction | Cost | kills | R1 min | Note |
|---|---|---|---|---|---|
| `humvee` | America | 500 | 10 | 4.3 | |
| `btr` | Russia | 600 | 12 | 4.5 | |
| `m113` | America | 700 | 14 | 4.6 | |
| `bmp2` | Russia | 1300 | 26 | 5.5 | |
| `bradley` | America | 1500 | 30 | 5.7 | |
| `grad` | Russia | 1500 | 30 | 5.7 | |
| `tunguska` | Russia | 1700 | 34 | 5.9 | |
| `giatsint` | Russia | 1800 | 36 | 6.0 | |
| `m109`, `m270` | America | 1800 | 36 | 6.0 | |
| `TRAN` | America | 2000 | — | 6.2 | **2 of 5 axes inert, cannot earn XP, §5.2** |
| `HALO` | Russia | 2000 | — | 6.2 | **2 of 5 axes inert, cannot earn XP, §5.2** |
| `tos` | Russia | 2000 | 40 | 6.2 | |
| `t90` | Russia | 2400 | 48 | 6.6 | |
| `abrams`, `strykershorad` | America | 2500 | 50 | 6.7 | |
| `littlebird` | America | 3000 | 60 | 7.1 | |
| `HIND` | Russia | 4000 | 80 | 7.8 | |
| `HELI`, `HIMARS` | America | 6000 | 120 | 9.0 | |
| `MI28`, `iskander` | Russia | 6000 | 120 | 9.0 | |
| `A10.Airstrike`, `FROG.Airstrike` | not buildable | 6000 | — | — | support-power airframes; eject block stripped |
| `quadcopterdrone` | not buildable | *(none)* | — | — | **entire rank block unreachable, §5.3** |

### Group C — Civilians, Technician, Team Leader (20 actors)

Profile: the shared set **+ the passenger freeze only** (no Team Leader boost).

| Unit | Faction | Cost | Note |
|---|---|---|---|
| `TL` | A + R | 200 | Team Leader — grants the +50% aura, `-GainsExperienceMultiplier@TLBoost` on itself (`infantry.yaml:1517`) so it does not boost its own progression |
| `TECN` | A + R | 250 | `Pistol` only; a capture/support unit that will rarely kill (§5.5) |
| `C1`, `C7` | not buildable | 25 | civilians **with** a `Pistol` |
| `C2`–`C6`, `C8`–`C11` | not buildable | 25 | civilians with **no armament** — 2 inert axes (§5.7) |
| `DELPHI`, `EINSTEIN` | not buildable | 25 | no armament |
| `CHAN` | not buildable | 500 | no armament |

**That civilians carry the full rank block at all is almost certainly unintended.** They reach it
through `^Infantry` (`infantry.yaml:4`) with nothing removing it. Harmless in play — not buildable,
so no accrual — but a civilian can be promoted by kills and will draw a rank chevron.

### Group D — Vehicle crew (10 actors) — **the one genuine numeric outlier**

Profile: the shared set + both XP-rate modifiers, **but `GainsExperience: ExperienceModifier: 1`**
(`crew.yaml:45-46`).

`crew.commander`, `crew.copilot`, `crew.driver`, `crew.gunner`, `crew.pilot` — `.america` and
`.russia` each, cost 100, not buildable.

**Why this matters.** `ExperienceModifier` **replaces** cost-scaling: thresholds become
`100 × 1` = 100/200/400/800 XP *absolute* (`GainsExperience.cs:88-91`). A kill still awards
`victim cost × 100`. So killing one 50-credit target awards 5000 XP — past the rank-4 threshold of
800 **six times over**. A bailed-out crewman with a pistol reaches **rank 4 on his first kill of
anything worth ≥8 credits**, where every other actor needs 8× its own cost. Whether this is
intended I could not determine (§7.3).

### Group E — Fixed-wing aircraft (4 actors) — the only group with a unique mechanic

Profile: the shared set, no XP-rate modifiers, **plus `EjectOnDeath@Rank0..Rank4`**.
`A10`, `F16` (America) · `FROG`, `MIG` (Russia). All 6000 credits, 120 kills / 9.0 min.

A shot-down plane ejects a pilot **carrying the airframe's rank**: `PILOT` at rank 0, then
`PILOTR1`/`R2`/`R3`/`R4` gated on `rank-veteran == 1/2/3/ >= 4` (`aircraft.yaml:154-176`), each at
`SuccessRate: 80`. Those pilot actors carry matching `ProducibleWithLevel.InitialLevels`, and
walking one to the map edge credits rank stock back. **This is the best-designed piece of the rank
system and it exists on four units.** All four are themselves `~disabled` as buildables — they are
reached as support-power airframes — so the mechanic that best expresses the system is also the one
a player meets least directly.

---

## §4 — Proportionality: is one flat profile right for every class?

### 4.1 The declared curve is flawless, and that is the problem

| Axis | R1 | R2 | R3 | R4 | Step | Shape |
|---|---|---|---|---|---|---|
| Firepower | +5% | +10% | +15% | +20% | **+5 flat** | linear |
| Speed | +5% | +10% | +15% | +20% | **+5 flat** | linear |
| Damage taken | −5% | −10% | −15% | −20% | **−5 flat** | linear |
| Reload delay | −5% | −10% | −15% | −20% | **−5 flat** | linear |
| Concealment | +1 | +2 | +3 | +4 | **+1 flat** | linear |

Every axis is perfectly linear, no rank is weaker than the one below it, and the resolver confirms
**all five groups carry identical values**. On the narrow question "does rank N+1 add the same
increment as rank N, and is it the same across classes" — yes, exactly, everywhere. There is no
defect in the declared numbers.

Two consequences of that flatness are worth knowing before anyone tunes it:

- **Firepower and reload are the same axis counted twice.** `1.20 / 0.80` = **1.5× sustained DPS**
  at rank 4, not 1.2×. With `−20%` damage taken, effective combat power is roughly **1.875×** a
  green unit — a large number arrived at implicitly from four modest-looking +5% steps. (Arithmetic,
  not measurement — §7.7.)
- **A flat profile means the same thing only if every class reaches it at a comparable rate.** It
  does not (§4.2), and can use it only if every class consumes all five axes. It does not (§5).

### 4.2 The two earn paths disagree by a factor of ~21

Full table from `--accrual`; a representative slice:

| Unit | Cost | Kills of a 50cr target for R1 | …for R4 | Free R1 | Free R3 |
|---|---|---|---|---|---|
| `E2`/`E3`/`AR` (rifle infantry) | 100 | **2** | 16 | 3.2 min | 29.1 min |
| `AA`/`AT`/`MT` | 300 | 6 | 48 | 3.9 min | 34.7 min |
| `SF` | 600 | 12 | 96 | 4.5 min | 40.3 min |
| `bradley` | 1500 | 30 | 240 | 5.7 min | 51.2 min |
| `abrams` | 2500 | 50 | 400 | 6.7 min | 60.0 min |
| `HIND` | 4000 | 80 | 640 | 7.8 min | 70.2 min |
| `iskander`/`HIMARS`/`MI28`/`HELI` | 6000 | **120** | **960** | 9.0 min | 80.9 min |
| **spread** | **60:1** | **60:1** | 60:1 | **2.8:1** | 2.8:1 |

The kill threshold is denominated in the killer's own cost, so in *value* terms it is perfectly
cost-normalised. But a player does not experience value — they experience **kill counts against
whatever is in front of them**, and infantry are the most numerous targets in the game. So:

- **Cheap infantry rank in two trades.** A 100-credit rifleman reaching rank 1 by killing one
  100-credit rifleman is a single even exchange. Rank 4 is 16 cheap kills — an ordinary good game
  for one squad. Combat ranking dominates and the purchase path is nearly irrelevant.
- **Expensive units effectively cannot rank by combat.** 120 conscript kills for rank 1 on a
  6000-credit unit; 960 for rank 4 — more enemy value than many whole armies. The purchase path
  dominates completely.
- **The purchase path deliberately refuses to mirror this.** `Rank1IntervalTicks` square-roots the
  cost term specifically to compress the roster, and `Rank1MaxIntervalTicks: 9000` caps it —
  the doc comment at `RankAccumulation.cs:79-88` says linear scaling spread it 120:1 and was
  rejected. So the compression is intentional; the **mismatch with the kill path is the finding**,
  not the compression itself.

**Sharpest consequence: rank 4 is an infantry-only rank.** It is the one rank that cannot be
purchased (`MaxPurchasableRank = 3`), and for anything above roughly 1000 credits the kill path to
8× own cost is out of reach in a normal match. So the top of the ladder — and the pilot-eject
`PILOTR4`, and the "forged in combat" design statement — is in practice reachable by cheap infantry
and almost nothing else.

**And the cost curve is geometric while the reward curve is linear.** Thresholds double each rank
(1×→2×→4×→8×) for a flat +5%:

| Rank | Cumulative value needed | Cumulative firepower | Enemy value per +1% firepower |
|---|---|---|---|
| 1 | 1× own cost | +5% | 0.20× |
| 2 | 2× | +10% | 0.20× |
| 3 | 4× | +15% | **0.27×** |
| 4 | 8× | +20% | **0.40×** |

Rank 4 costs **twice as much per point of benefit as rank 1**. The purchase path has the same shape
(`HigherTierIntervalMultiplier: 300`, ×3 per tier), so both earn paths punish the top tier the same
way.

### 4.3 The concealment axis is inverted against its own intent

`Detectable.ClampConcealment` clamps to `MapLayers.VisionLayers - 2` (`Detectable.cs:118-125`) and
`VisionLayers = 11` (`engine/OpenRA.Game/Traits/Player/MapLayers.cs:75`), so the **ceiling is 9**.
Concealment is a stack of additive modifiers.

Infantry sources, all `DetectableAddativeModifier` on `^DetectableInfantryStandard`: base
`Vision: 3` (`infantry.yaml:97-98`); cover +1/+2/+3 on `object-proximity == 1/== 2/>= 3`
(`:780-788`, mutually exclusive); prone +1 (`:789-791`); dug-in +1 (`:792-794`); firing −2
(`:800-802`); moving −1 (`:803-805`). `SN` and `SF` start at **`Vision: 5`** (`:1699-1700`,
`:2202-2203`), and `SF`'s firing penalty is only −1 (`:2208-2209`).

Best case — stationary, not firing, full cover, prone, dug in (`--concealment`):

| Class | Base | Stacked before rank | Level after R1/R2/R3/R4 | Rank steps that land |
|---|---|---|---|---|
| `SN`, `SF` | 5 | **10 → clamps to 9** | 9 / 9 / 9 / 9 | **0 of 4** |
| Line infantry, civilians, crew, pilots | 3 | 8 | **9** / 9 / 9 / 9 | **1 of 4** |
| Aircraft (`A10`, …) | 2 | 5 | 6 / 7 / 8 / 9 | 4 of 4 |
| Vehicles (`HIMARS`, …) | 2 | 3 | 4 / 5 / 6 / 7 | 4 of 4 |

**A dug-in sniper in cover gets nothing from any rank on this axis — he is over the ceiling before
veterancy is considered. A dug-in rifleman in cover gets rank 1 and nothing more.** Meanwhile the
axis works in full on tanks and planes, which are not stealth units and for whom it is the least
thematic of the five. The axis is proportional *as declared* and inverted *as experienced*.

This is the most actionable single finding in the audit, and it is the clearest evidence that one
flat profile is the wrong shape: the same `+1/+2/+3/+4` is dead weight on the units built around
concealment and a full-value bonus on the units that are not.

### 4.4 How many of the five axes each class actually consumes

| Group | Firepower | Reload | Speed | Damage taken | Concealment | Live axes |
|---|---|---|---|---|---|---|
| A — line infantry | ✓ | ✓ | ✓ | ✓ | ¼ (§4.3) | **4¼** |
| A — `SN` / `SF` | ✓ | ✓ | ✓ | ✓ | **0** (§4.3) | **4** |
| A — `MEDI` | ✓ *(heals)* | ✓ *(heals)* | ✓ | ✓ | ¼ | 4¼, **unreachable** |
| A — `DR` | ✗ *(no lethal target)* | ✗ | ✓ | ✓ | ¼ | 2¼, **unreachable** |
| B — vehicles | ✓ | ✓ | ✓ | ✓ | ✓ | **5** |
| B — helicopters | ✓ | ✓ | ✓ *(+turn)* | ✓ | ✓ | **5** |
| B — `TRAN` / `HALO` | ✗ | ✗ | ✓ *(+turn)* | ✓ | ✓ | **3**, unreachable by combat |
| B — `quadcopterdrone` | ✗ | ✗ | ✓ | ✓ | ✓ | 3, **wholly unreachable** |
| C — `TECN` / `TL` / armed civilians | ✓ | ✓ | ✓ | ✓ | ¼ | 4¼ |
| C — 12 unarmed civilians | ✗ | ✗ | ✓ | ✓ | ¼ | 2¼ |
| D — crew | ✓ | ✓ | ✓ | ✓ | ¼ | 4¼, **thresholds ~100× low** |
| E — fixed-wing | ✓ | ✓ | ✓ *(+turn)* | ✓ | ✓ | **5 + pilot eject** |
| — `TRUK`/`MNLY`/`MSAR`/`LCCV`/`SAM` | — | — | — | — | — | **0** (no rank trait) |
| — all structures, all naval | — | — | — | — | — | **0** |

---

## §5 — Units that gain little or nothing from rank today

Each entry: the mechanical reason, then a concrete bonus that unit **could** get. All suggestions
are labelled recommendations, not findings.

### 5.1 `MEDI` — medic (250, both) · Group A
**Mechanically:** its only armament is `Armament@1: Weapon: Heal, TargetRelationships: Ally`. Since
`FirepowerMultiplier` applies to every armament, a rank-4 medic already **heals 20% harder and 20%
more often** — the bonus shapes are exactly right. But healing an ally is not a kill, and there is
no non-kill XP path in the mod, so it **can never earn a rank**. Only purchase stock (3.8 min) or
map placement can rank it.
**Could get (recommendation):** the earn path, not a new bonus — the bonuses are already correct. A
heal-triggered XP grant is the honest version; `ExperienceTrickler` (exists in engine, unused
anywhere in the mod) is the cheap version but rewards hiding. If a new *bonus* is wanted anyway,
`RangeMultiplier` on the heal armament — a veteran medic reaching further into a firefight — is
thematic and the trait already exists unused.

### 5.2 `TRAN` / `HALO` — transport helicopters (2000 each) · Group B
**Mechanically:** `Cargo` + `Aircraft` + `Health` but **no `Armament` anywhere in the resolved
set**, so `FirepowerMultiplier` and `ReloadDelayMultiplier` have nothing to scale — 2 of 5 axes
declared and inert — and with no weapon there is no kill, so no XP. Rank arrives only via purchase
stock (6.2 min) or map placement. What does work: damage taken −20%, speed and turn +20%,
concealment +4 (all four steps land).
**Could get (recommendation):** the three live axes are already the right ones for a hauler, so the
gap is the earn path again — XP for delivered cargo would need C#. Cheapest honest YAML change:
delete the two inert multipliers from a transport-specific template so the profile stops
advertising bonuses that do nothing, and lean on the survivability axes that already work.

### 5.3 `quadcopterdrone` — scout drone (no cost) · Group B
**Mechanically:** the worst case in the game — it carries all 24 rank nodes and can reach **none**
of them. No armament (2 axes inert, no kill path) **and** no `Buildable`, so `Accrues` is false
(`RankAccumulation.cs:349-357`) and it banks no purchase stock either. It is spawned by
`CarrierMaster` on `DR` (`infantry.yaml:2521`). Separately, having no `Valued` means its own
thresholds fall back to ×1 (§1.2) — so if it ever did earn XP it would rank almost instantly.
**Could get (recommendation):** vision is the right axis for a scout, and the `VisionModifier` trait
is **already live on this very actor** (`VisionModifier@OperatorLostContact`, `aircraft.yaml:487-489`),
so a per-rank `VisionModifier` is zero-risk YAML. Extending `CarrierSlave.MaxDistance` per rank —
a veteran operator flying his drone further — is the other natural axis, but note CLAUDE.md's
warning that the leash is enforcement and the bot's model of it must move together.

### 5.4 `DR` — drone operator (150, both) · Group A
**Mechanically:** `DR` has two armaments, `DroneTargeter` and `DroneJammer`. `DroneTargeter` is
explicitly *"NOT A WEAPON… a dummy trigger (Damage: 0)"* whose only job is to make `CarrierMaster`
release a quadcopter. `DroneJammer` **is** damaging (`Warhead@Spread: Damage: 3`,
`weapons-other.yaml:737-740`) but carries `ValidTargets: Drone` — it can only kill drones. And
`quadcopterdrone` has **no `Valued` node**, so `GivesExperience` computes `exp = 0` and returns
before awarding anything (`GivesExperience.cs:50-52`, `:61-62`). **Killing a drone awards zero XP to
anyone.** So `DR` cannot earn a rank by combat, and its firepower/reload bonuses scale a dummy
trigger and an anti-drone jammer.
**Could get (recommendation):** giving `quadcopterdrone` a `Valued: Cost` is a one-line YAML change
that fixes both halves — `DR` gains a real (if narrow) kill path, and the drone's own thresholds
stop falling back to ×1. Beyond that, XP for contacts the drone reveals would need C# but is the
bonus that actually matches the unit.

### 5.5 `TECN` — technician (250, both) · Group C
**Mechanically:** `Pistol` only, plus `AttacksSupplyRoutes`. All five axes are live, but it is a
capture/support unit that will rarely kill, so in practice it is a purchase-path-only ranker.
**Could get (recommendation):** capture speed per rank. `Captures` has no multiplier trait, so this
**needs C#**. Cheaper YAML alternative: lean on the concealment and damage-taken axes it already
has by giving it a larger step than a rifleman gets — a veteran technician who survives to reach the
objective is the right fantasy.

### 5.6 `E6` — engineer (250, both) · Group A — **not actually a gap**
Worth recording because it looks like one. `E6` carries `MP5` (a real kill path) alongside
`Armament@Repair` and `Armament@ClearMines`. Because `FirepowerMultiplier` applies to every
armament, **a rank-4 engineer already repairs 20% harder and 20% more often.** No change needed;
this is the system working.

### 5.7 The twelve unarmed civilians — `C2`–`C6`, `C8`–`C11`, `DELPHI`, `EINSTEIN`, `CHAN` · Group C
**Mechanically:** no armament (2 axes inert, no kill path), not buildable (no accrual). They inherit
the whole block from `^Infantry` with nothing removing it.
**Could get (recommendation):** nothing — **remove** the rank block instead. These are scenery.
`-GainsExperience:` on the civilian template is a one-line YAML change that also stops a civilian
drawing a rank chevron.

### 5.8 `TRUK` (1000) · `MNLY` (600) · `MSAR` (1600) · `LCCV` (3000) — no rank trait at all
**Mechanically:** no `Inherits ^GainsExperience`, so no multipliers, no chevron, **and** no purchase
stock (the two halves fail together, §1.4). A supply truck that survives 40 minutes of resupply runs
is identical to one built a second ago. None has an armament, so none could earn XP by killing
either.
**Could get (recommendation):** `SpeedMultiplier` and `DamageMultiplier` need no armament and would
work verbatim — a veteran hauler that is faster and harder to kill is the right reward shape. For
`MSAR`, the counter-battery radar, the concealment axis is the natural fit and lands all four steps
(it is a vehicle, base 2). Because these cannot earn XP, the useful half is the **purchase path**,
which the same template unlocks for free.

### 5.9 `iskander` / `HIMARS` (6000 each) · Group B — probably purchase-path-only
**Mechanically:** both fire a *targeter*, not a gun — `IskanderTargeter` / `HIMARSTargeter`,
`Projectile: InstantHit`, one `Warhead@Target TargetDamage Damage: 50`. That is the same shape as the
drone operator's dummy (§5.4) but with 50 damage rather than 0, so they are not strictly
kill-incapable — a 50-damage tick can in principle land a killing blow. On any real target it will
not. Their destructive payload arrives by a delivery path I did not trace (§7.11), so whether the
launcher is ever credited for what it destroys is **unresolved**.
**Could get (recommendation):** nothing until §7.11 is settled — if the launcher is not credited,
these are the two most expensive units in the game with no combat earn path, and the right fix is
attribution, not a new bonus. Worth checking before acting on any other item here.

### 5.10 `SAM` (2000) — the only armed structure that ships buildable
**Mechanically:** no rank trait. A `SAM` that kills a dozen aircraft gains nothing and banks
nothing.
**Note on scope, correcting an earlier reading:** every *other* defense and structure —
`HSAM`, `AGUN`, `GUN`, `PBOX`, `HBOX`, `FTUR`, `GTWR`, `CRAM`, `SUPPLYROUTE`, `AFLD`, `HPAD`,
`LOGISTICSCENTER` — carries `~disabled` in its `Buildable.Prerequisites` at this ref. So "give
defenses ranks" is a **one-actor** question today, not a twenty-actor one.
**Could get (recommendation):** a structure-specific template with the three axes that apply to a
turret — `FirepowerMultiplier`, `ReloadDelayMultiplier`, `DamageMultiplier` — and not
`SpeedMultiplier`. Whether it *should* is a design call, not a defect: rewarding a static for
sitting still cuts against the mobile, reinforcement-driven model in
[`DOCS/reference/game-model.md`](../../DOCS/reference/game-model.md).

---

## §6 — Ranked changes

Ordered by (importance × confidence) / effort. **All of these are recommendations**, not findings.
"YAML-only" means no C# compiles and no engine risk.

| # | Change | Rationale (one line) | Cost |
|---|---|---|---|
| 1 | **Split the flat profile into per-class templates** (`^InfantryRank`, `^VehicleRank`, `^AircraftRank`, `^SupportRank`) | One profile cannot be right for five classes when two of its axes are dead on transports and one is dead on snipers — every other item here gets cheaper once this seam exists | **YAML-only** |
| 2 | **Fix the concealment cap**: drop `SN`/`SF` base `Vision` 5 → 3, or remove the concealment step from the infantry template and give infantry a different axis | Ranks 2–4 are worth **zero** concealment to the two units built around concealment, and ranks 1–4 are worth ¼ to everyone else on foot (§4.3) | **YAML-only** (2 lines for the cheap version) |
| 3 | **Flatten the thresholds** so rank 4 is reachable above ~1000 credits, e.g. `100/250/450/700` instead of `100/200/400/800` | Rank 4 is 8× own cost and unpurchasable, making the top rank infantry-only in practice (§4.2) | **YAML-only** (one `Conditions:` block) |
| 4 | **Bend the reward curve to match the cost curve**, e.g. firepower `105/112/121/132` | Cost doubles per rank while reward adds a flat +5%, so rank 4 costs 2× per point of benefit (§4.2) | **YAML-only** (`defaults.yaml`) |
| 5 | **Remove `ExperienceModifier: 1` from `^CrewMember`**, or raise it to the crew's real cost | A bailed-out crewman reaches rank 4 on his first kill; every other actor needs 8× its own cost (§3.D) | **YAML-only** (1 line) |
| 6 | **Give `quadcopterdrone` a `Valued: Cost`** | Fixes two things at once: `DR` gains a real kill path, and the drone's own thresholds stop falling back to ×1 (§5.3, §5.4) | **YAML-only** (2 lines) |
| 7 | **Delete the `ConditionModifier@Rank_1` block** at `defaults.yaml:311-314`, or replace it with a comment saying the trait does not exist | It reads as a one-line re-enable of suppression resistance; uncommenting it would fail the rules load (§1.3) | **YAML-only** |
| 8 | **Remove the rank block from unarmed civilians** (`-GainsExperience:` on the civilian template) | Twelve scenery actors carry 24 rank nodes and can draw rank chevrons (§5.7) | **YAML-only** (1 line) |
| 9 | **Give `TRUK`/`MNLY`/`MSAR`/`LCCV` a support template** (speed + damage-taken + concealment, no firepower) | Four buildable vehicles gain nothing from rank *and* bank no purchase stock; the axes that suit them need no armament (§5.8) | **YAML-only** |
| 10 | **Resolve the two orphan files** — delete `disable-player-experience.yaml` and `vehicles-ukraine.yaml`, or wire them into `mod.yaml` | Both look load-bearing and neither loads; one of them names five Red Alert units that do not exist here (§2) | **YAML-only** |
| 11 | **Drop the two inert multipliers from a transport template** | `TRAN`/`HALO` advertise firepower and reload bonuses with no armament to apply them to (§5.2) | **YAML-only** |
| 12 | **Add a non-kill XP path** for `MEDI`, `TRAN`/`HALO` and `TECN` | Three classes whose bonuses are well-shaped and unreachable; `ExperienceTrickler`, `DeliversExperience` and `AcceptsDeliveredExperience` all exist in engine and are **unused anywhere in the mod** | **YAML-only** if `ExperienceTrickler` suffices; **needs C#** for a heal/repair/delivery-triggered grant (the better design) |
| 13 | **Add `InaccuracyMultiplier` as a rank axis** | Veterans shoot straighter; it is already proven in-mod (`aircraft.yaml`, `infantry.yaml`, `vehicles.yaml`) and unlike firepower it is **not** already double-counted (§4.1) | **YAML-only** |
| 14 | **Give `SAM` a structure template** (firepower + reload + damage-taken) | The only armed structure that ships buildable gains nothing from its kills — but see §5.10 on whether it should | **YAML-only** |
| 15 | **Show enemy rank** — uncomment `Enemy, Neutral` on the four chevrons (`defaults.yaml:379-421`) | You cannot see that the tank bearing down on you is a veteran, which hides the system at the moment it matters most | **YAML-only** (4 lines) — but this is an information-symmetry design choice; **recommend a ruling before touching** |

**Do NOT remove `ReloadDelayMultiplier`** despite its overlap with firepower (§4.1). It is what makes
burst weapons and single-shot weapons rank differently; fold the overlap into the *values* instead.

**Note the asymmetry, because it is the useful thing here.** Twelve of the fifteen items above are
one- or two-file YAML edits, and every *numeric* change is a single edit in `defaults.yaml`. If the
goal is tuning rather than new mechanics, this system is unusually cheap to work on.

### What could actually verify any of this
Per [`DOCS/recipes/BALANCE.md`](../../DOCS/recipes/BALANCE.md), `tools/combat-sim` answers "what
does this unit look like" from static YAML and never simulates combat — so it is the **wrong tool**
for rank work, because the rank multipliers are `ConditionalTrait`s gated on a runtime token that
does not exist in a static dump. Rank effects have to be measured in-engine. A
`tools/autotest/scenarios/test-rank-accumulation/` scenario already covers the *currency* half; a
`test-balance-*` duel with one side pre-ranked via `ProducibleWithLevel: InitialLevels` (pattern at
`infantry.yaml:1217`) or `VeterancyLevelInit` is the shape that would measure the *combat* half.
**I ran nothing.**

---

## §7 — Watch: what I could not resolve from the files

1. **Nothing was executed**, per the brief. Every statement is static reading at the stamped ref.
2. **My resolver is not the engine's loader.** `tools/rank-audit/miniyaml.py` ports
   `MiniYaml.cs:399-600` and reproduces the documented semantics (case-sensitive top-level merge,
   `Inherits@`/`-Key:` applied in appearance order). Cross-checks that passed: the
   `-GainsExperienceMultiplier@TLBoost` self-removal on `^TL`, the `crew.yaml` `ExperienceModifier`
   override, the stripped eject blocks on both `.Airstrike` variants, and — the strongest one — the
   accrual figures independently asserted in prose at `player.yaml:18-21` (2m59s / 8m59s / 27m /
   81m) reproducing to 2m59.6s / 8m59.3s / 29.1m / 80.9m. The authoritative check is still
   `--check-yaml`, which the brief barred. Treat counts as **±1**; group shapes I am confident in.
3. **Whether the crew `ExperienceModifier: 1` outlier is intentional.** The comment above it explains
   the neighbouring `SupplyValue` token, not the XP modifier. No design note found either way. This
   is the one item in §6 I would not action without asking.
4. **Whether civilians carrying the full rank block is intentional.** Nothing removes it; no
   document mentions it.
5. **The 50-credit anchor in `player.yaml:18-21` is a `~disabled` actor.** `E1` and both its faction
   twins carry `~disabled`, so the "2m59s for a 50-credit Conscript" floor describes a unit that
   cannot be bought. The cheapest buildable ranking unit is 100 credits at 3.2 min, making the
   shipped spread 2.8:1 rather than the 3:1 the comment quotes. The comment's arithmetic is right;
   its anchor is unbuyable. I did not chase why `E1` is disabled or what replaced it.
6. **Map and scenario rules can override anything here.** I read only `mods/ww3mod/rules/`. A shipped
   map or autotest scenario with its own `rules.yaml` could change ranks for that map. Not checked.
7. **§4.1's 1.875× is arithmetic, not measurement.** It assumes reload delay translates 1:1 into
   sustained DPS and that armour math is linear in `DamageMultiplier`. Both are standard OpenRA
   behaviour but I did not trace `Armament.ReloadDelay` or the damage pipeline, and burst weapons
   will not obey it exactly.
8. **The concealment level → cells mapping is not mine.** §4.3 works in vision *levels*; the ceiling
   arithmetic is exact, but "how many cells does a rank buy" is unanswered. The claim that ranks 2–4
   add **zero** for a dug-in infantryman does not depend on it.
9. **My concealment best case is a best case.** `--concealment` takes the largest positive modifier
   per condition variable and ignores the firing and moving penalties, which is the right frame for
   a ceiling question but is not a claim about the average engagement. A sniper who is firing sits
   at 8, not 10, and would see rank 1 land.
10. **`PlayerExperience` is live (§2) but I did not trace what consumes it.** Whether anything in
    WW3MOD reads that player-level pool, or whether it is fully vestigial, is unestablished.
11. **Artillery attribution is resolved; the two "targeter" launchers are not.** `Armament.cs:632`
    sets `SourceActor = self`, `DamageWarhead.cs:68` reads `firedBy = args.SourceActor` and calls
    `victim.InflictDamage(firedBy, …)` (`:306`, `:309`), so **for any weapon fired through an
    `Armament` the firing unit is credited** — tube and rocket artillery (`m270`, `grad`, `m109`,
    `giatsint`, `tos`) fire `Bullet` projectiles with real damage warheads and earn XP normally.
    **But `iskander` and `HIMARS` do not fire a weapon in that sense:** their armaments are
    `IskanderTargeter` / `HIMARSTargeter`, `Projectile: InstantHit` carrying a single
    `Warhead@Target TargetDamage Damage: 50` — the same *shape* as `DroneTargeter`, but 50 rather
    than 0, so my lethality check passed them. They can therefore earn XP only when that 50-damage
    tick is the killing blow, which on any real target it will not be; their actual destructive
    payload arrives by a path I did not trace. **Treat §4.2's "120 kills / 9.0 min" row for
    `iskander` and `HIMARS` as the purchase figure only** — in practice both are almost certainly
    purchase-path-only rankers and belong with §5's list. Confirming that needs the delivery path
    for those two weapons traced, which I did not do.
12. **Design intent vs defect.** I have called the `ConditionModifier` block (§1.3), the unreachable
    drone rank block (§5.3) and the zero-XP drone kill (§5.4) defects because the code contradicts
    itself. Ally-only chevrons, rankless statics and the flat profile itself may all be deliberate;
    I found no document either way.
13. **Two audit directories exist** — `WORKSPACE/audit/` (this file, per the brief) and
    `WORKSPACE/audits/`, which holds the earlier `rank-system-audit.md` written at `9cb423d4`, 504
    commits back. That document is sound on mechanism and I reproduced its threshold derivation and
    its concealment arithmetic independently. It differs from this one in three ways worth knowing:
    it reports **71** ranking actors against this audit's **113** (it counted only buildables, so it
    misses the civilians, crew, pilots, pre-ranked variants and the drone); it states **20 buildable
    structures** where only `SAM` is not `~disabled` at this ref (§5.10); and it did not have the
    accrual-vs-kill comparison in §4.2 or the resolved `DroneJammer`/zero-XP finding in §5.4.
    **Recommend superseding it with this file** rather than leaving two audits disagreeing on the
    roster.

---

## §8 — Rulings (2026-09-20)

Two questions §7 left open were put to the user and answered. A first batch of §6 items was then
implemented on `wt/rank-retune-batch`, forked from `main @ 1c806add`. Everything below that is not
in the "implemented" list is still an open recommendation.

### The two rulings

**§6 #5 / §7.3 — the crew `ExperienceModifier: 1` outlier is NOT intentional.** §7.3 listed this as
the one item it would not action without asking. Ruled a defect and fixed in this batch: the
override is removed, so crew thresholds scale by `Valued.Cost: 100` like every other actor's.

**§6 #15 / §7.12 — ally-only rank chevrons ARE deliberate.** The commented-out `Enemy, Neutral` on
the four `WithDecoration@Rank_N` entries (`defaults.yaml:379-421`) is an intentional information
asymmetry, not an oversight: you are not meant to know whether the tank bearing down on you is a
veteran. §6 #15 is **withdrawn**, not deferred. Do not "fix" it. §7.12's listing of ally-only
chevrons as possibly-deliberate is now settled as deliberate.

### Implemented in this batch (YAML only, no C#)

| § | What landed |
|---|---|
| #5 | `ExperienceModifier: 1` removed from `^CrewMember`. All ten crew actors go from thresholds `x1` (100/200/400/800 XP absolute, rank 4 on one kill worth ≥8 credits) to `x100` (10000/20000/40000/80000 XP = the standard 1×/2×/4×/8× own cost). |
| #6 | `quadcopterdrone` given `Valued: Cost: 25` — the mod's own price for one drone, from `^DR`'s `SupplyValue: 25` (`infantry.yaml:2553-2555`), which is denominated in the same credits as `Valued.Cost` (`economy.md:452`). Drone kills now award 2500 XP instead of zero, and the drone's own thresholds stop falling back to ×1. |
| #7 | The dead `ConditionModifier@Rank_1` block replaced with a comment recording that no such trait exists and that uncommenting it would fail the rules load. |
| #10 | `disable-player-experience.yaml` and `ingame/vehicles-ukraine.yaml` deleted. Wiring either in is a design change, not a repair: the first would *define* five nonexistent RA actors rather than override them, the second would add a second buildable Russian MBT. Two now-dangling citations in `DOCS/reference/economy.md` fixed in the same commit. |
| #11 | `^UnarmedHelicopter` added; `TRAN`/`HALO` inherit it instead of `^Helicopter`, dropping the eight armament-scaling rank traits they have no weapon to apply. The three axes that work on a hauler are untouched. |

### Deliberately NOT done here — these are design/tuning calls for the user

Each is live and unchanged. The line says what taking it would change *in play*, not what it would
change in the file.

- **#1 — split the flat profile into per-class templates.** Nothing changes on its own; it is the
  seam that makes every other numeric item below addressable per class instead of roster-wide.
  #11 above is the first concrete instance of that seam and can be generalised from.
- **#2 — fix the concealment cap.** Today ranks 2–4 buy a dug-in infantryman *zero* extra
  concealment and ranks 1–4 buy a sniper or special-forces operator zero, because both already clamp
  at the ceiling. Fixing it makes veteran infantry meaningfully harder to spot; it also makes snipers
  harder to counter, which is the reason it is a judgement call and not a bug fix.
- **#3 — flatten the thresholds** (e.g. `100/250/450/700`). Rank 4 currently costs 8× own cost and
  cannot be purchased, so in practice it is infantry-only: a 6000-credit MBT needs 960 conscript
  kills. Flattening puts rank 4 within reach of expensive units and makes elite armour a thing that
  happens in a match.
- **#4 — bend the reward curve to match the cost curve** (e.g. firepower `105/112/121/132`). Cost
  doubles per rank while reward adds a flat +5%, so rank 4 is worth half as much per credit as rank
  1. Bending it makes the later ranks feel like a payoff rather than a formality — and makes veteran
  units hit distinctly harder, which is a balance shift across the whole roster.
- **#8 — remove the rank block from unarmed civilians.** Twelve scenery actors currently carry 24
  rank nodes each and can draw rank chevrons. Removing it stops a civilian from ever showing a
  chevron. Cosmetic in effect, but it is the user's call whether ranked civilians are a bug or a
  joke worth keeping.
- **#9 — give `TRUK`/`MNLY`/`MSAR`/`LCCV` a support template.** Four buildable vehicles gain nothing
  from rank and bank no purchase stock. Adding one would make veteran logistics faster and harder to
  kill, and would put four more actors into the rank-purchase economy — which changes what the
  accrual stock gets spent on.
- **#12 — add a non-kill XP path** for `MEDI`, `TRAN`/`HALO` and `TECN`. These three classes have
  well-shaped bonuses they can never earn. A trickle or a heal/deliver-triggered grant would let
  support units veteran up from doing their job. The biggest behavioural change on this list, and
  the only one that likely needs C#.
- **#13 — add `InaccuracyMultiplier` as a rank axis.** Veterans would shoot straighter as well as
  harder. Unlike firepower it is not already double-counted, so it adds a genuinely new dimension to
  what a rank means — and it disproportionately helps long-range and burst weapons.
- **#14 — give `SAM` a structure template.** The only armed structure that ships buildable currently
  gains nothing from its kills. Adding it would make an established SAM site progressively harder to
  saturate. §5.10 argues both sides; it is not obvious that static defences *should* veteran.

### Still open from §7

**§7.13 — supersede `WORKSPACE/audits/rank-system-audit.md` with this file.** Unactioned. Two audit
documents still disagree on the roster (71 ranking actors against 113, and 20 buildable structures
against 1), because the older one counted only buildables and was written 504 commits back. Note
that this file is now itself out of date in the five places §8 lists as implemented — anything
superseding it should be read together with this section.
