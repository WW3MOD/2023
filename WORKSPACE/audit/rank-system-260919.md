# Rank system audit — 2026-09-19

> **INCOMPLETE — paused by the user mid-audit. RESUME AT §4 (proportionality) and §5 (ranked
> changes), which are not written.** §1–§3 below are complete and verified at the ref stamped
> under them. The CSV companion and the generator script are complete and working.
>
> **Resume checklist:**
> 1. §4 proportionality: the arithmetic is gathered (see §3.4 stub notes) but not written up.
> 2. §5 ranked changes with cost estimates: not written.
> 3. Reconcile with the PRIOR audit `WORKSPACE/audits/rank-system-audit.md` (ref `9cb423d4`,
>    **504 commits behind this one**). It is sound on mechanism and I agree with its §1/§4 maths,
>    but its roster is **incomplete**: it reports 71 ranking actors, all buildable, and misses
>    the 14 civilians, 10 crew, 5 pilots, 6 pre-ranked `*R1` variants and `quadcopterdrone` that
>    also carry the full rank block (113 total, this audit). Decide whether to supersede it or
>    merge the two; do not leave two audits disagreeing on the roster size.
> 4. Open questions are in §6 Watch. The one that blocks a recommendation is whether `DroneJammer`
>    is damaging — it decides whether the drone operator `DR` can earn a rank at all.

**Ref: `wt/rank-audit @ 64185a89`** (forked from `main @ 64185a89`; tree clean at start).
Nothing was executed: no build, no game launch, no `--check-yaml`, no `make test`. Every number
below was read from the cited `file:line` at that ref, or computed by the committed script in
`tools/rank-audit/` from those files.

**Companion artifacts**
- `WORKSPACE/audit/rank-system-260919.csv` — 113 rows, one per ranking actor.
- `tools/rank-audit/rank_audit.py` — regenerates the CSV and the §3 grouping.
- `tools/rank-audit/miniyaml.py` — a port of the engine's MiniYaml loader
  (`engine/OpenRA.Game/MiniYaml.cs:399-600`), so the inheritance resolution behind §3 is
  reproducible rather than hand-derived. Re-run with:

```
python tools/rank-audit/rank_audit.py --csv WORKSPACE/audit/rank-system-260919.csv --groups
python tools/rank-audit/rank_audit.py --unit MEDI.america      # one actor, resolved
```

---

## §1 — The mechanism

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

All four thresholds grant the **same condition name**, and `GiveExperience` calls
`GrantCondition` once per level crossed (`GainsExperience.cs:124-128`), so the token **stacks**:
consumers read `rank-veteran == 1 … == 4`. `MaxLevel` is just `Conditions.Count`
(`GainsExperience.cs:79`) — **the number of ranks is the number of lines in that map.**

### 1.2 XP comes from kills only, priced in the victim's cost

`GivesExperience` sits on `^ExistsInWorld`, so nearly everything awards XP when it dies. The award
fires from `INotifyKilled.Killed` (`GivesExperience.cs:57`) — **there is no damage-dealt,
time-alive, healing, capture or objective path anywhere in the mod.**

- Award = victim's `Valued.Cost` (`GivesExperience.cs:50-52`, since `Experience` defaults to `-1`
  at `:22`) × `ActorExperienceModifier`, which defaults to **10000** (`:28`) and is never
  overridden in the mod. `Util.ApplyPercentageModifiers` is ×p/100, so that is **×100**.
- Thresholds = `Key × requiredExperience` where `requiredExperience` is the actor's own `Cost`
  (`GainsExperience.cs:88-91`).

**The two ×100s cancel**, which gives the one rule worth memorising:

| Rank | Threshold | In plain terms |
|---|---|---|
| 1 | 100 × own cost | kill enemy value equal to **1×** your own cost |
| 2 | 200 × own cost | **2×** your own cost, cumulative |
| 3 | 400 × own cost | **4×** cumulative |
| 4 | 800 × own cost | **8×** cumulative |

Only `Neutral` and `Enemy` kills count (`GivesExperience.cs:25`) — team-killing does not promote.
Because the threshold is denominated in the killer's own cost, **the kill-earn path is
cost-normalised by construction**: a 50-credit Conscript and a 6000-credit Iskander both need to
kill their own worth to reach rank 1. There is no proportionality defect in the *earn* curve
across the roster; the defects are elsewhere (§4, unwritten).

Two mod-side modifiers to the earn rate, both confirmed present in the resolved trees:

- **A passenger earns nothing.** `GainsExperienceMultiplier: Modifier: 0` gated on
  `disable-experience` (`infantry.yaml:94-96`), granted by `Passenger.CargoCondition` (`:89`).
  Riding in an APC freezes progression.
- **A Team Leader's aura is +50% XP.** `GainsExperienceMultiplier@TLBoost: Modifier: 150` on
  `morale-boost` (`infantry.yaml:273-275`). `^TL` removes it from itself (`infantry.yaml:1517`),
  so the leader does not boost his own progression. **This applies to infantry only** — no vehicle,
  aircraft or civilian group carries it (§3, Groups B and C).

### 1.3 What a rank grants — five axes, and all 20 values are identical everywhere

From `^GainsExperience` (`defaults.yaml:317-373`), gated on `rank-veteran == N`:

| Axis | Trait | R1 | R2 | R3 | R4 | Lines |
|---|---|---|---|---|---|---|
| Damage **taken** | `DamageMultiplier` | 95 | 90 | 85 | 80 | `defaults.yaml:329-341` |
| Damage **dealt** | `FirepowerMultiplier` | 105 | 110 | 115 | 120 | `:342-353` |
| Move + turn speed | `SpeedMultiplier` | 105 | 110 | 115 | 120 | `:354-365` |
| Reload delay | `ReloadDelayMultiplier` | 95 | 90 | 85 | 80 | `:366-377` |
| **Concealment** | `DetectableAddativeModifier` (`VisionModifier`) | +1 | +2 | +3 | +4 | `:317-328` |

The resolver confirms these 20 values are **byte-identical on all 113 ranking actors** — not one
actor overrides or removes a single multiplier. The five profile groups in §3 differ *only* in XP
rate modifiers, `ExperienceModifier`, and the pilot-eject extra.

Semantics worth stating because the obvious reading is wrong:

- **`DetectableAddativeModifier` is a CONCEALMENT bonus, not a vision bonus.** Its `[Desc]` is
  *"Modifies the required vision to see this actor"* (`DetectableAddativeModifier.cs:16`), and
  `DetectableInfo.Vision` is *"What level of vision is required to detect this actor"*
  (`Detectable.cs:23-24`). **Higher = harder to see.** A rank-4 unit is harder to spot; it does
  not see further. It is additive into `ClampConcealment` (`Detectable.cs:85`, `:146`), and
  **it is capped** — see §3.4.
- `SpeedMultiplier` is an `ISpeedModifier` consumed by both `Mobile` and `Aircraft`, and on
  aircraft it also scales turn rate. Aircraft ranks are not cosmetic.
- A **commented-out** suppression-resistance block sits at `defaults.yaml:311-314`
  (`ConditionModifier@Rank_1 … Condition: suppressed, Modifier: -1`). **`ConditionModifier` is
  not a live trait** — `engine/OpenRA.Mods.Common/Traits/Multipliers/ConditionModifier.cs` has
  zero non-comment lines, and what is commented out there is a stale copy of
  `DetectableAddativeModifier`, not a condition modifier. Uncommenting the YAML would fail the
  rules load. It reads like a one-line re-enable and is not one.

Visuals: `WithDecoration@Rank_1..4`, `Image: rank`, `Sequence: rank-veteran-N`,
`ValidRelationships: Ally` (`defaults.yaml:379-421`) — **ally-only; you cannot see an enemy's
rank**, the `Enemy, Neutral` values are commented out on each of the four entries. Sequences at
`sequences/sequences-misc.yaml:581-586`.

### 1.4 The WW3MOD-specific half: a rank is also a CURRENCY

This has no Red Alert ancestor and is more than half the system.

**Free accrual.** `RankAccumulation` on the player (`player.yaml:22`, declared bare so **every
value is the C# default**) runs one timer and one stock *per actor type*
(`RankAccumulation.cs:281-303`):

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

**Only units that can hold a rank accrue one.** `Accrues` requires both `BuildableInfo` **and**
`GainsExperienceInfo` (`RankAccumulation.cs:349-357`). So an actor without `^GainsExperience` is
excluded from the currency too, not just from the multipliers — the two halves fail together.

**Ranks 1–3 only.** `MaxPurchasableRank = 3` (`RankAccumulation.cs:59`) — *"Rank 4 is forged in
combat only, never purchased."*

**Spending** happens in the production queue: `PeekRank` picks the highest held tier and adds a
`VeterancyLevelInit` (`ProductionQueue.cs:790-792`), then `CommitRank` spends it once the actor is
really out (`:796-799`) — a failed `Produce` does not burn stock.

**Recovery refunds rank.** `CreditsRankOnEvacuation` (`defaults.yaml:311`, on every ranking actor)
fires on `INotifySold.Sold`, reached only by an actor that physically got to the map edge
(`CreditsRankOnEvacuation.cs:46-51`). A recovered rank-4 credits back **rank 3**, clamped rather
than dropped (`:60-63`). Ejected crew credit a *fraction* of a rank toward the exact vehicle type
they bailed out of (`:65-68`, `crew.yaml:45-50`); a whole crew's shares sum to one rank
(`RankAccrual.ShareTicks`, `:160-169`). Recovery stock is **not** capped by `Caps` — it lands in
`BonusStock`, documented as deliberate (`RankAccumulation.cs:174-181`).

---

## §2 — Bots vs humans, and the player-level XP pool

`RankAccumulation` is on the player actor with no owner gate, so **bots and humans accrue
identically**; nothing in the trait branches on `IsBot`.

**`mods/ww3mod/rules/disable-player-experience.yaml` is NOT LOADED.** It is absent from
`mod.yaml`'s `Rules:` list (verified by the script, which merges exactly that list: 38 files) and
referenced nowhere in `mods/`, `tools/` or `DOCS/`. It would zero `PlayerExperienceModifier` and
the RA-era capture/repair/cash XP hooks (`E6`, `MECH`, `THF`, `SPEN`, `SYRD`, `FIX`, `TRUK`).
Because it never loads, `^ExistsInWorld`'s `PlayerExperienceModifier` default is live. Several
actors it names (`MECH`, `THF`, `SPEN`, `SYRD`, `FIX`) are Red Alert units that do not exist in
WW3MOD. The file is either a dead artifact or an un-wired intent; as it stands it is misleading.

**`mods/ww3mod/rules/ingame/vehicles-ukraine.yaml` is not loaded either** — also absent from
`mod.yaml`. It carries an `Inherits@GainsExperience: ^GainsExperience` at `:4` that resolves for
nobody.

**Naval cannot rank because naval does not ship.** `naval.yaml`'s `^Naval` veterancy wiring is
commented out (`naval.yaml:4`, `:186`), and the resolver finds zero concrete naval actors.

---

## §3 — THE LIST: every ranking unit, grouped by identical bonus profile

**113 ranking actors in 5 profiles**, out of 459 concrete actors (`--groups` output). Because all
20 multiplier values are identical everywhere (§1.3), the groups are distinguished only by XP-rate
modifiers, `ExperienceModifier`, and the pilot-eject extra. **Every group shares this profile:**

> thresholds `100/200/400/800` (= 1×/2×/4×/8× own cost in enemy value killed) ·
> firepower `+5/+10/+15/+20%` · reload delay `−5/−10/−15/−20%` · speed & turn `+5/+10/+15/+20%` ·
> damage taken `−5/−10/−15/−20%` · concealment `+1/+2/+3/+4` · rank chevron ·
> evacuation credits rank back

### Group A — Line infantry + pilots (54 actors)

Profile: **the shared set, plus BOTH XP-rate modifiers** — `+50%` under a Team Leader
(`morale-boost`) and `0%` while riding as a passenger (`disable-experience`).

| Unit | Faction | Cost | Note |
|---|---|---|---|
| `AR` | both, America, Russia | 100 | |
| `E1` | (faction twins are `both/neutral`) | 50 | |
| `E2` | both, America, Russia | 100 | |
| `E3` | both, America, Russia | 100 | |
| `E4` | both, America, Russia | 150 | |
| `DR` | both, America, Russia | 150 | drone operator — **see §3.1** |
| `E6` | both, America, Russia | 250 | engineer — `Repair` + `ClearMines` armaments are ally/utility; its `MP5` is the only kill path |
| `MEDI` | both, America, Russia | 250 | **cannot earn XP — see §3.2** |
| `AA` | both, America, Russia | 300 | |
| `AT` | both, America, Russia | 300 | |
| `MT` | both, America, Russia | 300 | |
| `SN` | both, America, Russia | 400 | sniper — **concealment capped, §3.4** |
| `SF` | both, America, Russia | 600 | special forces — **concealment capped, §3.4** |
| `E1R1`, `E2R1`, `E3R1` (+ `.america`/`.russia`) | not buildable | 50–100 | ship pre-ranked at level 2 |
| `PILOT` | not buildable | 500 | ejected pilot, rank 0 |
| `PILOTR1`/`R2`/`R3`/`R4` | not buildable | 800/1200/2000/3000 | ejected pilot carrying the airframe's rank |
| `E3.colorpicker` | not buildable | 100 | UI actor |

### Group B — Vehicles, aircraft, helicopters, drone (25 actors)

Profile: the shared set, **no XP-rate modifiers at all** — no Team Leader boost, and *no
passenger freeze either*, because neither is declared outside the infantry chain.

| Unit | Faction | Cost | Note |
|---|---|---|---|
| `humvee` | America | 500 | |
| `btr` | Russia | 600 | |
| `m113` | America | 700 | |
| `bmp2` | Russia | 1300 | |
| `bradley`, `grad` | America, Russia | 1500 | |
| `tunguska` | Russia | 1700 | |
| `giatsint`, `m109`, `m270` | Russia, America, America | 1800 | |
| `TRAN` | America | 2000 | **2 of 5 axes inert — §3.3** |
| `HALO` | Russia | 2000 | **2 of 5 axes inert — §3.3** |
| `tos` | Russia | 2000 | |
| `t90` | Russia | 2400 | |
| `abrams`, `strykershorad` | America | 2500 | |
| `littlebird` | America | 3000 | |
| `HIND` | Russia | 4000 | |
| `HELI`, `HIMARS` | America | 6000 | |
| `MI28`, `iskander` | Russia | 6000 | |
| `A10.Airstrike`, `FROG.Airstrike` | not buildable | 6000 | support-power airframes; eject block stripped |
| `quadcopterdrone` | not buildable | — | **entire rank block unreachable — §3.1** |

### Group C — Civilians, Technician, Team Leader (20 actors)

Profile: the shared set **+ the passenger freeze only** (no Team Leader boost).

| Unit | Faction | Cost | Note |
|---|---|---|---|
| `C1`–`C11` | not buildable | 25 | civilians. `C1` and `C7` carry a `Pistol`; the other nine have **no armament** and 2 inert axes |
| `DELPHI`, `EINSTEIN` | not buildable | 25 | no armament |
| `CHAN` | not buildable | 500 | no armament |
| `TECN` | both, America, Russia | 250 | `Pistol` only; a capture/support unit that will rarely kill |
| `TL` | both, America, Russia | 200 | Team Leader — grants the +50% aura, `-GainsExperienceMultiplier@TLBoost` on itself (`infantry.yaml:1517`) so it does not boost its own progression |

**That civilians carry the full rank block at all is almost certainly unintended** — they reach it
through `^Infantry` (`infantry.yaml:4`) with nothing removing it. Harmless in play (they are not
buildable, so they accrue no stock), but it means a civilian can be promoted by kills and will
draw a rank chevron.

### Group D — Vehicle crew (10 actors) — **the one genuine outlier in the whole system**

Profile: the shared set + both XP-rate modifiers, **but `GainsExperience: ExperienceModifier: 1`**
(`crew.yaml:45-46`).

`crew.commander`, `crew.copilot`, `crew.driver`, `crew.gunner`, `crew.pilot` — `.america` and
`.russia` each, cost 100, not buildable.

**Why this matters.** `ExperienceModifier` **replaces** the cost-scaling: thresholds become
`100 × 1 = 100/200/400/800` XP *absolute* (`GainsExperience.cs:88-91`). But a kill still awards
`victim cost × 100`. So killing a single 50-credit Conscript awards 5000 XP — past the rank-4
threshold of 800 **eight times over**. A bailed-out crewman with a pistol reaches **rank 4 on his
first kill of anything worth ≥8 credits.** Every other actor in the game needs 8× its own cost.
Whether that is intended I could not determine from the files (§6).

### Group E — Fixed-wing aircraft (4 actors) — the only group with a unique mechanic

Profile: the shared set, no XP-rate modifiers, **plus `EjectOnDeath@Rank0..Rank4`**.

`A10`, `F16` (America) · `FROG`, `MIG` (Russia).

A shot-down plane ejects a pilot **carrying the airframe's rank**: `PILOT` at rank 0, then
`PILOTR1`/`R2`/`R3`/`R4` gated on `rank-veteran == 1/2/3/ >= 4` (`aircraft.yaml:154-176`), each at
`SuccessRate: 80`. Those pilot actors carry matching `ProducibleWithLevel.InitialLevels`, and
walking one to the map edge credits rank stock back. **This is the best-designed piece of the rank
system and it exists on four units.**

---

## §3.1–§3.4 — Special cases inside the groups

### §3.1 `quadcopterdrone` and `DR` (drone operator) — a rank block with no way in

`quadcopterdrone` (Group B) carries all 24 rank nodes and **cannot use or reach any of them**:

- **No armament at all** → `FirepowerMultiplier` and `ReloadDelayMultiplier` are inert, and there
  is no kill path, so it cannot earn a rank.
- **Not `Buildable`** → `Accrues` returns false (`RankAccumulation.cs:349-357`), so no purchase
  stock either. It is spawned by `CarrierMaster` on `DR` (`infantry.yaml:2521`).

So the drone's entire rank block is unreachable by construction. What *would* work if it ever got
a rank: speed/turn `+20%`, damage taken `−20%`, concealment `+4`.

`DR`'s own armaments are `DroneTargeter` and `DroneJammer` (`infantry.yaml:2539+`). `DroneTargeter`
is explicitly *"NOT A WEAPON… a dummy trigger (Damage: 0, weapons-other.yaml:714)"* whose only job
is to make `CarrierMaster` release a quadcopter. **Whether `DroneJammer` is damaging decides
whether `DR` can rank up at all** — my script's lethality check passed `DR`, so at least one of its
warheads carries `Damage > 0`; I did not read the weapon to confirm which, and it may be a
self-damaging or utility warhead. **This is the one unresolved question blocking a
recommendation** (§6).

### §3.2 `MEDI` (medic) — firepower scales *healing*, and it can never rank up

`MEDI`'s only armament is `Armament@1: Weapon: Heal, TargetRelationships: Ally`. So:

- `FirepowerMultiplier` scales the **heal weapon** — a rank-4 medic heals 20% harder;
- `ReloadDelayMultiplier` makes it heal 20% more often;
- and it **can essentially never rank up**, because healing an ally is not a kill and there is no
  non-kill XP path anywhere in the mod (§1.2).

The bonus shapes are right and the earn path is missing. This is the clearest case in the game of
a unit whose rank bonuses are well-designed and unreachable except by purchase stock.

### §3.3 `TRAN` / `HALO` (transport helicopters) — 2 of 5 axes inert

Both have `Cargo`, `Aircraft` and `Health` but **no `Armament` anywhere in the resolved set**, so
`FirepowerMultiplier` and `ReloadDelayMultiplier` have nothing to scale, and neither can earn a
rank in combat. Their rank can arrive **only** via purchase stock or map placement.
What still works: damage taken `−20%`, speed and turn `+20%`, concealment `+4`.

### §3.4 The concealment axis is silently capped, and infantry hit the cap

`Detectable.ClampConcealment` clamps to `MapLayers.VisionLayers - 2` (`Detectable.cs:118-125`) and
`VisionLayers = 11` (`engine/OpenRA.Game/Traits/Player/MapLayers.cs:75`), so the **hard ceiling is
9**. Concealment is a stack of additive modifiers.

Infantry base is `Vision: 3` (`infantry.yaml:97-98`); snipers and special forces start at
**`Vision: 5`** (`infantry.yaml:1699-1700`, `:2202-2203`). Vehicles carry no explicit `Vision`, so
they take `DetectableInfo.Vision`'s default of **2** (`Detectable.cs:23`).

From base alone every rank step fits. The cap bites once the cover/prone/dug-in terms are added —
`SN` and `SF` at base 5 are only 4 under the ceiling before veterancy is considered, so a dug-in
sniper in cover gets little or nothing from ranks 2–4, exactly the units whose identity is
concealment. **The full stacking arithmetic is gathered but NOT written up — this is where §4
resumes.** The prior audit's §4.3 (`WORKSPACE/audits/rank-system-audit.md`) works this out and I
agree with its derivation; re-verify its line numbers at this ref before reusing it.

---

## §4 — Proportionality — **NOT WRITTEN. RESUME HERE.**

Material already established and ready to write up:

1. **Every axis is perfectly linear** (+5 flat per rank on four axes, +1 on concealment) and
   **identical across all five groups** — verified by the resolver on all 113 actors. On the narrow
   question "does rank N+1 add the same increment as rank N", the answer is yes, exactly,
   everywhere.
2. **The cost curve is geometric while the reward curve is linear.** Thresholds double each rank
   (1×→2×→4×→8× own cost) for a flat +5%. Rank 4 therefore costs ~2× as much enemy value per point
   of benefit as rank 1. The purchase path has the same shape
   (`HigherTierIntervalMultiplier: 300`). **This is the single clearest disproportionality.**
3. **Firepower and reload are the same axis counted twice.** `1.20 / 0.80` = **1.5× sustained
   DPS** at rank 4, not 1.2×; with `−20%` damage taken, effective combat power is roughly
   **1.875×** a green unit. A large number arrived at implicitly from four modest +5% steps.
   (Arithmetic, not measurement — see §6.)
4. **The concealment cap** (§3.4) — proportional as declared, non-proportional as experienced.
5. **Classes that can realistically never rank up by combat:** `MEDI` (heal-only), `TRAN`/`HALO`
   and `quadcopterdrone` (no armament), the nine unarmed civilians, and — pending §6 —
   possibly `DR`. Plus everything with no `^GainsExperience` at all: all structures and defenses,
   `TRUK`/`MNLY`/`MSAR`/`LCCV`, and all naval (which does not ship).

## §5 — Ranked changes with cost estimates — **NOT WRITTEN. RESUME HERE.**

## §6 — Watch: what I could not resolve from the files

1. **Nothing was executed**, per the brief. Every statement is static reading of `64185a89`.
2. **My resolver is not the engine's loader.** `tools/rank-audit/miniyaml.py` is a port of
   `MiniYaml.cs:399-600` and reproduces the documented semantics (case-sensitive top-level merge,
   `Inherits@`/`-Key` applied in appearance order). It reproduces known facts — the
   `-GainsExperienceMultiplier@TLBoost` removal on `^TL`, the `crew.yaml` `ExperienceModifier`
   override, the stripped eject blocks on the `.Airstrike` variants. But the authoritative check is
   `--check-yaml`, which I did not run. Counts are **±1 confidence**; group *shapes* I am confident
   in.
3. **`DroneJammer`'s lethality is unresolved** and it decides whether `DR` can rank (§3.1). My
   lethality check found a `Damage > 0` warhead on one of `DR`'s two armaments but I did not read
   which, or whether it can be aimed at an enemy. **Read `weapons-other.yaml` for `DroneJammer`
   first on resume.**
4. **Whether the crew `ExperienceModifier: 1` outlier (§3.D) is intentional.** The comment above it
   explains the *cost* token, not the XP modifier. No design doc found either way.
5. **Whether civilians carrying the full rank block (§3.C) is intentional.** Nothing removes it;
   no document mentions it.
6. **Map and scenario rules can override anything here.** I read only `mods/ww3mod/rules/`. A
   shipped map or autotest scenario carrying its own `rules.yaml` could change ranks for that map.
   Not checked.
7. **§4.3's 1.875× is arithmetic, not measurement.** It assumes reload delay translates 1:1 into
   sustained DPS and that armour math is linear in `DamageMultiplier`. Standard OpenRA behaviour,
   but I did not trace `Armament.ReloadDelay` or the damage pipeline, and burst weapons will not
   obey it exactly.
8. **The concealment level → cells mapping is not mine.** §3.4 works in vision *levels*; the
   ceiling arithmetic is exact, but "how many cells does a rank buy" is unanswered.
9. **`PlayerExperience` is live (§2) but I did not trace what consumes it.** Whether anything in
   WW3MOD reads that player-level pool, or whether it is fully vestigial, is unestablished.
10. **Design intent vs defect.** I have called the `ConditionModifier` block (§1.3) and the
    unreachable drone rank block (§3.1) defects because the code contradicts itself. Ally-only
    chevrons and rankless defenses may well be deliberate; I found no document either way.
11. **Two audit directories exist** — `WORKSPACE/audit/` (this file) and `WORKSPACE/audits/` (the
    prior one). The brief specified the former. Worth collapsing.
