# Rank / veterancy system audit

**Ref: `main @ 9cb423d4`** (branch `wt/rank-audit`). Read-only audit — no engine or YAML changed.
Every claim below was read from the cited file at that ref. Nothing here was run: no game launch,
no `--check-yaml`, no `make test`.

> Filed as `rank-system-audit.md` per the brief. Note the local convention in this directory is
> `YYMMDD-slug.md` (e.g. `260901-autotest-suite-audit.md`); rename if that matters.

---

## §1 — How ranking works today

### 1.1 The four ranks and the one condition

All veterancy comes from a single template, `^GainsExperience` (`mods/ww3mod/rules/defaults.yaml:279`):

```
GainsExperience:                       # defaults.yaml:280-287
    LevelUpNotification: LevelUp
    Conditions:
        100: rank-veteran
        200: rank-veteran
        400: rank-veteran
        800: rank-veteran
    LevelUpImage: crate-effects
```

Four levels, and **all four grant the same condition name**. `GainsExperience.GiveExperience` calls
`self.GrantCondition(...)` once per level crossed (`GainsExperience.cs:126-130`), so the token
*stacks*: the unit reads `rank-veteran == 1` after the first promotion, `== 2` after the second, and
so on. Every consumer in the mod is written against that count. `MaxLevel` is just
`Conditions.Count` (`GainsExperience.cs:81`), so **the number of ranks is the number of lines in that
map** — adding a fifth line is the whole change.

### 1.2 Experience is earned from KILLS ONLY, and is priced in the victim's cost

`GivesExperience` is on `^ExistsInWorld` (`defaults.yaml:5-6`), so effectively everything grants XP
when it dies. The award fires from `INotifyKilled.Killed` (`GivesExperience.cs:57`) — **there is no
damage-dealt, time-alive, or objective path anywhere**. The amount:

- base = the victim's `Valued.Cost` (`GivesExperience.cs:51-52`, since `Experience` defaults to `-1`
  at `:22`);
- multiplied by `ActorExperienceModifier`, which **defaults to 10000** (`:28`) and is never overridden
  in the mod. `Util.ApplyPercentageModifiers` multiplies by `p/100` per modifier
  (`engine/OpenRA.Mods.Common/Util.cs:238-246`), so 10000 means **×100**.

The thresholds are scaled the same way. `GainsExperience` reads its keys as *"XP requirements for
each level as a percentage of our own value"* (`GainsExperience.cs:25`) and computes
`kv.Key * requiredExperience` where `requiredExperience` is the actor's own `Cost`
(`GainsExperience.cs:89-91`).

**The two ×100s cancel.** The rule that falls out is clean and worth stating plainly:

| Rank | Threshold | In plain terms |
|---|---|---|
| 1 | 100 × own cost | kill enemy value equal to **1×** your own cost |
| 2 | 200 × own cost | **2×** your own cost, cumulative |
| 3 | 400 × own cost | **4×** cumulative |
| 4 | 800 × own cost | **8×** cumulative |

Only `Neutral` and `Enemy` kills count (`GivesExperience.cs:25`) — team-killing does not promote.

Two mod-side modifiers to the earn rate:

- **A passenger earns nothing.** `^Infantry` carries `GainsExperienceMultiplier: Modifier: 0` gated on
  `disable-experience` (`infantry.yaml:94-96`), which `Passenger.CargoCondition` grants while loaded
  (`:89`). Riding in an APC freezes progression.
- **A Team Leader's aura is +50% XP.** `GainsExperienceMultiplier@TLBoost: Modifier: 150` on
  `morale-boost` (`infantry.yaml:273-275`). `^TL` removes it from itself (`infantry.yaml:1517`), so
  the leader does not boost his own progression.

### 1.3 What a rank actually grants

All five multipliers live in `^GainsExperience` and are gated on `rank-veteran == N`
(`defaults.yaml:294-353`). Values as shipped:

| Axis | Trait | R1 | R2 | R3 | R4 | Lines |
|---|---|---|---|---|---|---|
| Damage **taken** | `DamageMultiplier` | 95 | 90 | 85 | 80 | `defaults.yaml:306-317` |
| Damage **dealt** | `FirepowerMultiplier` | 105 | 110 | 115 | 120 | `:318-329` |
| Move + turn speed | `SpeedMultiplier` | 105 | 110 | 115 | 120 | `:330-341` |
| Reload delay | `ReloadDelayMultiplier` | 95 | 90 | 85 | 80 | `:342-353` |
| Concealment | `DetectableAddativeModifier` (`VisionModifier`) | +1 | +2 | +3 | +4 | `:294-305` |

Notes on what each really touches:

- `SpeedMultiplier` is an `ISpeedModifier` (`Multipliers/SpeedMultiplier.cs:24-29`) and is consumed by
  **both** `Mobile` (`Mobile.cs:344`, `:845`) and `Aircraft` (`Aircraft.cs:403`, `:805`) — and on
  aircraft it also scales **turn rate** (`Aircraft.cs:300`). Aircraft ranks are not cosmetic.
- `DetectableAddativeModifier` raises `Detectable.Vision`, documented as *"What level of vision is
  required to detect this actor"* (`Detectable.cs:24-25`). **Higher = harder to see**, so this is a
  concealment bonus, not a penalty. It is additive into `ClampConcealment`
  (`Detectable.cs:129`, `:146`). **It is capped** — see §4.3.
- There is a **commented-out** suppression-resistance block at `defaults.yaml:290-293`
  (`ConditionModifier@Rank_1 … Condition: suppressed, Modifier: -1`). See §3.6: that trait does not
  exist, so it could not be uncommented as-is.

### 1.4 Visuals and audio

- **Chevrons.** `WithDecoration@Rank_1..4`, `Image: rank`, `Sequence: rank-veteran-N`,
  `Position: Top`, `Margin: 16,0`, `ValidRelationships: Ally` (`defaults.yaml:355-386`). Ally-only —
  **you cannot see an enemy's rank**; the `Enemy, Neutral` values are commented out on each entry
  (`:361`, `:369`, `:377`, `:385`).
- Sequences are defined at `mods/ww3mod/sequences/sequences-misc.yaml:530-531` (`rank-veteran-1`, `-2`, …).
- Infantry re-position the same decorations to `Margin: 10,0` via `^RankPipsAdjustmentInfantry`
  (`infantry.yaml:749-757`), pulled in at `infantry.yaml:23`.
- **Promotion sound** is `LevelUpNotification: LevelUp` (`defaults.yaml:281`) →
  `LevelUp: hydrod1` (`mods/ww3mod/rules/sound/notifications.yaml:127`).
- **Promotion sprite** is `LevelUpImage: crate-effects` (`defaults.yaml:287`), and is deliberately
  suppressed for units you do not own so a level-up does not reveal an enemy position
  (`GainsExperience.cs:137-139`).

### 1.5 The WW3MOD-specific half: ranks are also a CURRENCY

This is the part with no Red Alert ancestor, and it is more than half the system.

**Free accrual.** `RankAccumulation` on the player (`mods/ww3mod/rules/player.yaml:22`, all defaults)
runs one timer and one stock *per actor type*. A type accrues a purchasable rank on a wall-clock
interval derived from its nominal build time. The curve
(`engine/OpenRA.Mods.Common/Traits/Player/RankAccumulation.cs:281-303`):

| Field | Value |
|---|---|
| `Rank1BaseIntervalTicks` | 2400 |
| `CostReferenceBuildTicks` | 100 |
| `Rank1IntervalMultiplier` | 2700 |
| `Rank1MaxIntervalTicks` | 9000 |
| `HigherTierIntervalMultiplier` | 300 |
| `Caps` | `{3, 2, 1}` |

The interval is `BaseTicks + isqrt(build × reference) × Rank1Multiplier / 100`, capped
(`RankAccrual.Rank1IntervalTicks`, `:90-103`) — deliberately **not** linear in cost, because linear
spread the roster 120:1 (`:79-88`). Each tier up is ×3 (`:111-121`). I recomputed this over the live
resolved costs; it reproduces the figure in the YAML comment (`player.yaml:18-21`) exactly: rank 1
runs **3.0 min** for a 50-credit Conscript to **9.0 min** for a 6000-credit Iskander, rank 3 from
27 min to 81 min.

**Only combat units accrue.** `Accrues` requires `BuildableInfo` **and** `GainsExperienceInfo`
(`RankAccumulation.cs:354-357`). A unit with no `^GainsExperience` banks nothing — so the four
non-ranking vehicles in §3.1 are excluded from the currency too, not just from the multipliers.

**Ranks 1–3 only.** `MaxPurchasableRank = 3` (`:59`) — *"Rank 4 is forged in combat only, never
purchased."*

**Spending** happens in the production queue: `PeekRank` picks the highest held tier and adds a
`VeterancyLevelInit` to the new actor (`ProductionQueue.cs:734-736`), then `CommitRank` spends it
(`:742-744`). The buy menu draws the held chevron on the cameo (`ProductionPaletteWidget.cs:870-884`).

**Recovery refunds rank.** `CreditsRankOnEvacuation` (`defaults.yaml:289`) fires on `INotifySold.Sold`,
which is only reached by an actor that physically got to the map edge
(`CreditsRankOnEvacuation.cs:46-51`). A recovered rank-4 credits back **rank 3**, clamped rather than
dropped (`:60-63`). Ejected crew credit a *fraction* of a rank toward the exact vehicle type they
bailed out of (`:65-66`, and `crew.yaml:45-50`). Note that recovery-earned stock is **not** capped by
`Caps` — `CreditWhole` increments `BonusStock` with no cap check (`RankAccumulation.cs:245`), as
already documented in `DOCS/reference/architecture.md`.

**Pre-ranked actors.** `ProducibleWithLevel` ships on map-placed variants: `E1R1`/`E2R1`/`E3R1` at
`InitialLevels: 2` (`infantry.yaml:1217`, `:1346`, `:1506`, and the faction twins at
`infantry-america.yaml:13,26,47` / `infantry-russia.yaml:13,26,47`), and `PILOTR1..4` at levels 1–4
(`infantry.yaml:2645,2655,2665,2675`). None are `Buildable`.

---

## §2 — Resolved bonuses per unit

Method: I parsed the 35 rules files in `mod.yaml`'s `Rules:` list (`mods/ww3mod/mod.yaml:109-144`) in
load order, resolved every `Inherits@` and `-Trait` **in the order they appear** (which is how MiniYaml
applies them — `DOCS/reference/conventions.md`), and grouped the concrete actors by their resolved
rank-trait signature. 586 actors, 427 concrete, **95 buildable, of which 71 carry `^GainsExperience`**.

**The headline is that there is almost no variation.** Every ranking unit in the game gets the exact
same five multipliers at the exact same values. The groups below differ only in *extras*, and in
whether the unit has the traits those multipliers feed.

### Group A — Line infantry (39 actors)

**Bonuses:** the full standard set (§1.3) + chevrons + `CreditsRankOnEvacuation` + **`GainsExperienceMultiplier@TLBoost`** (+50% XP under a Team Leader).
**Template:** `^Infantry` → `Inherits@GainsExperience: ^GainsExperience` (`infantry.yaml:4`).

```
AA AA.america AA.russia    AR AR.america AR.russia    AT AT.america AT.russia
DR DR.america DR.russia    E1 E1.america E1.russia    E2 E2.america E2.russia
E3 E3.america E3.russia    E4 E4.america E4.russia    E6 E6.america E6.russia
MEDI MEDI.america MEDI.russia    MT MT.america MT.russia
SF SF.america SF.russia    SN SN.america SN.russia
```

*(The bare `AA`/`E1`/… entries are `Prerequisites: ~disabled` faction-neutral bases; the `.america` /
`.russia` twins are what a player actually buys. See §3.7.)*

### Group B — Ground vehicles, plus TECN and TL (22 actors)

**Bonuses:** identical to Group A **minus** the Team Leader XP boost.
**Templates:** `^Vehicle` families via `Inherits@GainsExperience: ^GainsExperience`
(`vehicles-america.yaml:7,183,303,468,589,717,879,1072`; `vehicles-russia.yaml:7,131,293,410,528,695,827,990`);
`^TECN` via `infantry.yaml:2446`.

```
abrams bmp2 bradley btr giatsint grad HIMARS humvee iskander
m109 m113 m270 strykershorad t90 tos tunguska
TECN TECN.america TECN.russia    TL TL.america TL.russia
```

Why these two lack the boost: `^TL` deletes it from itself (`infantry.yaml:1517`), `^TECN` descends
from `^ArmedCivilian` (`infantry.yaml:2398`) rather than `^Soldier` (where the boost is declared,
`infantry.yaml:273`), and vehicles were never in that chain at all.

### Group C — Armed helicopters (4 actors)

**Bonuses:** standard set + chevrons + evacuation credit. No eject, no XP boost.
**Template:** `^Helicopter` → `^Airborne` → `Inherits@UnitExperience: ^GainsExperience` (`aircraft.yaml:102`).

```
HELI (Apache)   HIND (Mi-24)   MI28 (Mi-28)   littlebird
```

### Group D — Fixed-wing (4 actors) — **the only group with a unique mechanic**

**Bonuses:** standard set + chevrons + evacuation credit + **`EjectOnDeath@Rank0..Rank4`**.
**Template:** `^Aircraft` (`aircraft.yaml:141-169`).

```
A10   F16   FROG (Su-25)   MIG (MiG-29)
```

A shot-down plane ejects a pilot **carrying the airframe's rank**: `PILOT` at rank 0, then
`PILOTR1`/`PILOTR2`/`PILOTR3`/`PILOTR4` gated on `rank-veteran == 1/2/3/ >= 4`
(`aircraft.yaml:145-169`), each at `SuccessRate: 80`. Those pilot actors carry matching
`ProducibleWithLevel.InitialLevels` (`infantry.yaml:2645-2675`), and walking one home credits rank
stock back. **This is the single best-designed piece of the rank system** and it exists on four units.
(The two support-power variants `A10.Airstrike` / `FROG.Airstrike` strip the eject block —
`aircraft-america.yaml:693-697`, `aircraft-russia.yaml:713-717`.)

### Group E — Transport helicopters (2 actors) — **two of five bonuses are inert**

**Bonuses:** the same five multipliers are *declared*, but the unit has **no `Armament` at all**.

```
HALO (Russia)   TRAN (Chinook)
```

Both are `^Helicopter` + `^CargoPips` (`aircraft-russia.yaml:3-5`, `aircraft-america.yaml:3-5`) with
`AttackAircraft` (`aircraft.yaml:199`) but no armament node anywhere in their resolved set.
`FirepowerMultiplier` and `ReloadDelayMultiplier` therefore have nothing to scale. What still works:
damage taken −20%, speed and turn +20%, concealment +4. See §3.2.

### Groups F–H — units with NO rank at all

| | Units | Why |
|---|---|---|
| **F — Vehicles that cannot rank** | `TRUK` (Supply Truck, `vehicles.yaml:571`), `MNLY` (Minelayer, `:504`), `MSAR` (Ranging system, `:417`), `LCCV` (Logistics Center MCV, `:682`) | No `Inherits ^GainsExperience`. No multipliers, no chevron, **and no accrued purchase stock** (`RankAccumulation.cs:354-357`) |
| **G — All 20 buildable structures / defenses** | `AFLD AGUN BARB BRIK CRAM FENC FTUR GTWR GUN HBOX HGATE HPAD HSAM LOGISTICSCENTER MSLO PBOX SAM SBAG SUPPLYROUTE VGATE` | Same. Static defenses kill things and gain nothing |
| **H — Naval** | *none ship* | `mods/ww3mod/rules/ingame/naval.yaml` is **commented out in its entirety**, including `^Naval`'s `Inherits@GainsExperience` (`naval.yaml:2-8`). Zero concrete naval actors resolve |

---

## §3 — Special cases, and what a rank does or could do for them

### 3.1 The four non-ranking vehicles (Group F)

**Today:** nothing. A Supply Truck that survives 40 minutes of resupply runs is identical to one built
a second ago, and the player cannot bank or buy a veteran one either.

**Could apply, with mechanisms already in the mod:** `SpeedMultiplier` and `DamageMultiplier` need no
armament and would work verbatim — a veteran truck that is faster and harder to kill is exactly the
right reward shape for a logistics unit, and `MSAR` (the counter-battery radar,
`Detectable.cs:36` calls it out by name) is a natural fit for the concealment axis. The blocker is
purely that `^GainsExperience` is not inherited; but note these units also cannot **earn** XP
(no weapon → no kills), so giving them the template only makes sense together with either the
purchase path (which the same template unlocks, `RankAccumulation.cs:354-357`) or a non-kill XP source
(§5.4).

### 3.2 Transports: HALO / TRAN (Group E)

**Today:** 3 of 5 axes live. `FirepowerMultiplier` and `ReloadDelayMultiplier` are declared and inert.
They also **cannot earn a rank in combat** — no armament means no kill means no XP
(`GivesExperience.cs:67-72` awards to `e.Attacker` only). Their rank can *only* arrive via the
purchase stock or map placement. That is not a bug, but it means the accrual timer is their sole path.

**Could apply:** the cargo axis is untouched. There is no "capacity" or "load speed" multiplier in the
engine, but `Cargo` load/unload is not the interesting lever anyway — the honest one is survivability,
which already works. A cheap, coherent addition would be to make the concealment step bigger for
transports specifically, since a rank-4 Chinook's +4 sits well under the cap (§4.3).

### 3.3 The Medic (MEDI) — firepower scales *healing*

`MEDI` sits in Group A with the full set, but its only `Armament@1` is `Weapon: Heal`,
`TargetRelationships: Ally` (`infantry.yaml:2353-2357`). So on a medic:

- `FirepowerMultiplier` scales the **heal weapon** — a rank-4 medic heals 20% harder;
- `ReloadDelayMultiplier` makes him heal 20% more often;
- and he can essentially never rank up, because healing an ally is not a kill.

The direction is right and the earn path is missing. `DISCOVERIES.md:7099` already notes this
("*whether healing an ally can earn a rank is a separate question and looks unlikely*"). This is the
clearest case in the game of a unit whose rank bonuses are well-shaped but unreachable.

### 3.4 The Technician (TECN)

Group B, full multipliers, `Armament: Weapon: Pistol` inherited from `^ArmedCivilian`
(`infantry.yaml:416-417`) plus `AttacksSupplyRoutes` (`defaults.yaml:415`). So `FirepowerMultiplier`
scales a pistol and its Supply-Route contestation. It is a capture/support unit that will rarely kill
anything; like the medic, it is a purchase-path-only ranker in practice.

### 3.5 Structures and defenses (Group G)

A `PBOX` or `SAM` that kills a dozen units gains nothing, and no rank stock accrues for it. Two of the
five axes are meaningless for a static (`SpeedMultiplier`), but three are not:
`FirepowerMultiplier`, `ReloadDelayMultiplier` and `DamageMultiplier` all apply cleanly to a turret.
This is the largest untouched surface in the system — 20 buildable structures. Whether it *should* be
touched is a design call, not a defect: rewarding a defense for sitting still cuts against the mobile,
reinforcement-driven model in `DOCS/reference/game-model.md`.

### 3.6 The suppression bonus that was written and cannot be enabled

`defaults.yaml:290-293` carries a commented-out `ConditionModifier@Rank_1` intended to give veterans
`suppressed: -1`. **`ConditionModifier` is not a live trait.** Its file
(`engine/OpenRA.Mods.Common/Traits/Multipliers/ConditionModifier.cs`) is 34 lines and has **zero
non-comment lines** — the whole thing is commented out, and what is commented out is a stale copy of
`DetectableAddativeModifier`, not a condition modifier at all. Uncommenting the YAML would fail the
rules load. Anyone reading that block will reasonably assume it is a one-line re-enable; it is not.

### 3.7 Faction twins double-count in the rank bank

Each infantry type exists three times: a `~disabled` faction-neutral base (`E3`) and two real ones
(`E3.america`, `E3.russia`) — e.g. `infantry-america.yaml:18-21`. `RankAccumulation` keys its stock by
**actor name** (`RankAccumulation.cs:345`) and `Accrues` checks only `BuildableInfo` + `GainsExperienceInfo`
(`:354-357`) — **it never checks prerequisites**. So the disabled `E3` runs a live accrual timer and
banks ranks that no queue can ever spend. Harmless (a handful of ints per tick) but it is dead work,
and it means "how many types accrue" is ~1.5× the real roster. The same applies to `A10`/`F16`/`FROG`/`MIG`,
which are `~disabled` support-power airframes.

### 3.8 RA-era leftovers the WW3MOD model no longer intends

- **`mods/ww3mod/rules/disable-player-experience.yaml` is not loaded.** It is absent from `mod.yaml`'s
  `Rules:` list (`mod.yaml:109-144`) and referenced nowhere. It exists to zero out
  `PlayerExperienceModifier` and the RA capture/repair/cash XP hooks (`E6`, `MECH`, `THF`, `SPEN`,
  `SYRD`, `FIX`, `TRUK`). Since it never loads, `^ExistsInWorld`'s `PlayerExperienceModifier: 1`
  (`defaults.yaml:5-6`) **is live**, and `PlayerExperience` is on the player (`player.yaml:472`).
  Several of the actors that file names (`MECH`, `THF`, `SPEN`, `SYRD`, `FIX`) are RA units. The file
  is either a dead artifact or an un-wired intent; either way it is misleading as it stands.
- **`mods/ww3mod/rules/ingame/vehicles-ukraine.yaml` is not loaded either** — not in `mod.yaml`. It
  contains an `Inherits@GainsExperience: ^GainsExperience` at `:4` that resolves for nobody.
- **Naval is fully commented out** (`naval.yaml`), including its veterancy wiring.
- The `Enemy, Neutral` relationships commented out of every rank chevron (`defaults.yaml:361` et al.)
  are an inherited RA default, not a WW3MOD decision that was written down anywhere I found.

---

## §4 — Proportionality check

### 4.1 Per-level increments

| Axis | R1 | R2 | R3 | R4 | Step | Shape |
|---|---|---|---|---|---|---|
| Firepower | +5% | +10% | +15% | +20% | **+5 flat** | linear |
| Speed | +5% | +10% | +15% | +20% | **+5 flat** | linear |
| Damage taken | −5% | −10% | −15% | −20% | **−5 flat** | linear |
| Reload delay | −5% | −10% | −15% | −20% | **−5 flat** | linear |
| Concealment | +1 | +2 | +3 | +4 | **+1 flat** | linear |

**Every axis is perfectly linear, and identical across all five unit groups.** No group scales faster
than another; no rank is weaker than the one below it on any axis. On the narrow question the brief
asks — *does level N+1 add roughly the same increment as level N, and is it the same across unit
classes* — the answer is **yes, exactly, everywhere**. There is no proportionality defect in the
declared numbers.

The problems are all in the relationship between that flat curve and the things around it.

### 4.2 The COST curve is geometric while the BONUS curve is linear

Thresholds double each rank (1× → 2× → 4× → 8× own cost, §1.2) while rewards add a flat +5%.

| Rank | Cumulative kill value needed | Cumulative firepower | Enemy value spent per +1% firepower |
|---|---|---|---|
| 1 | 1× own cost | +5% | 0.20× |
| 2 | 2× | +10% | 0.20× |
| 3 | 4× | +15% | **0.27×** |
| 4 | 8× | +20% | **0.40×** |

Rank 4 costs **twice as much per point of benefit as rank 1**. That is the single clearest
disproportionality in the system: the last rank is the hardest to reach and the least worth reaching.
The linear reward and the geometric cost are simply different curves. (The same ×3-per-tier geometry
governs the purchase path — `HigherTierIntervalMultiplier: 300`, `RankAccumulation.cs:299` — so both
earn paths punish the top tier the same way.)

### 4.3 The concealment axis is silently capped, and infantry hit the cap

`Detectable.ClampConcealment` clamps to `MapLayers.VisionLayers - 2` (`Detectable.cs:118-125`), and
`VisionLayers = 11` (`engine/OpenRA.Game/Traits/Player/MapLayers.cs:75`) — so the **hard ceiling is 9**.
Concealment is a stack of additive modifiers (`Detectable.cs:129`, `:146`).

Infantry sources (`^DetectableInfantryStandard`, inherited at `infantry.yaml:21`):
base `Vision: 3` (`:97-98`); cover +1/+2/+3 (`:780-788`); prone +1 (`:789-791`); dug-in +1 (`:792-794`);
firing −2 (`:800-802`); moving −1 (`:803-805`). Snipers and Special Forces start at
**`Vision: 5`** (`^SN` `:1691-1692`, `^SF` `:2177-2178`).

| Unit, stationary and not firing | Pre-rank total | R1 | R2 | R3 | R4 |
|---|---|---|---|---|---|
| Rifleman, open ground | 3 | 4 | 5 | 6 | 7 |
| Rifleman, full cover + prone + dug in | 8 | **9 (cap)** | 9 | 9 | 9 |
| Sniper / SF, full cover + prone + dug in | 10 → **clamps to 9** | 9 | 9 | 9 | 9 |

**A dug-in rifleman in cover gets +1 from rank 1 and nothing from ranks 2, 3 and 4. A dug-in sniper in
cover gets nothing from any rank at all** — he is already over the ceiling before veterancy is
considered. Vehicles are safe (`^Vehicle` has no explicit `Vision`, so default 2 — `Detectable.cs:25`;
+1 stationary and −1 firing at `vehicles.yaml:92-97`; worst case 2+1+4 = 7 < 9), and so are aircraft.

So the concealment axis is proportional *as declared* and non-proportional *as experienced*, and it
degrades exactly for the units whose whole identity is concealment. This is the most actionable finding
in the audit.

### 4.4 Where the axes land unevenly across groups

Not a scaling difference — a *consumption* difference. Counting how many of the five axes actually do
something:

| Group | Firepower | Reload | Speed | Damage taken | Concealment | Live axes |
|---|---|---|---|---|---|---|
| A — line infantry | ✓ | ✓ | ✓ | ✓ | ✓ (capped when dug in, §4.3) | 5 (4½) |
| A — MEDI | ✓ *(heals)* | ✓ *(heals)* | ✓ | ✓ | ✓ | 5, ~unreachable (§3.3) |
| B — vehicles, TECN, TL | ✓ | ✓ | ✓ | ✓ | ✓ | 5 |
| C — armed helicopters | ✓ | ✓ | ✓ *(+turn)* | ✓ | ✓ | 5 |
| D — fixed-wing | ✓ | ✓ | ✓ *(+turn)* | ✓ | ✓ | 5 **+ pilot eject** |
| E — HALO / TRAN | ✗ | ✗ | ✓ *(+turn)* | ✓ | ✓ | **3** |
| F — TRUK/MNLY/MSAR/LCCV | — | — | — | — | — | **0** |
| G — structures / defenses | — | — | — | — | — | **0** |

### 4.5 Reload and firepower are the same axis, counted twice

`FirepowerMultiplier` +20% and `ReloadDelayMultiplier` −20% both multiply sustained DPS. A rank-4 unit
is at `1.20 / 0.80` = **1.5× sustained DPS**, not 1.2×. Combined with −20% damage taken, effective
combat power at rank 4 is roughly **1.875×** a green unit of the same type. That is a large number to
arrive at implicitly from four modest-looking +5% steps, and it is worth knowing before anyone tunes
the visible values.

---

## §5 — Candidate changes, ranked

Not implemented. Ordered by (importance × confidence) / effort.

### 5.1 Fix the concealment cap for infantry — **S effort, high value**
The bug in §4.3. Ranks 2–4 are worth zero concealment to exactly the units built around concealment.
Options, cheapest first: lower the pre-rank stack (e.g. drop `^SN`/`^SF` base `Vision` from 5 to 3 so
rank has headroom); or make the rank step conditional so it does not stack on top of dug-in cover; or
raise `MapLayers.VisionLayers` (engine change, wide blast radius — it is a shroud-rendering constant,
`ShroudRenderer.cs:127` et al., and I would not).
**Touches:** `infantry.yaml` (2 lines) for the cheap version.
**Risk:** low mechanically; it is a real stealth balance change for snipers.

### 5.2 Bend the reward curve to match the cost curve — **S effort, high value**
The §4.2 finding. Thresholds double per rank; rewards add flat +5%. Either flatten the thresholds
(`defaults.yaml:283-286`, e.g. `100/250/450/700`) or make the reward geometric
(e.g. firepower `105/112/121/132`). One `Conditions:` block, one file.
**Touches:** `defaults.yaml` only.
**Risk:** medium — this changes every ranking unit at once, and §4.5 means the true multiplier moves
faster than the printed number.

### 5.3 Give the four support vehicles and/or defenses *some* rank — **M effort**
§3.1 and §3.5. Adding `Inherits@GainsExperience: ^GainsExperience` also opens the purchase path
(`RankAccumulation.cs:354-357`), which is the more useful half for units that cannot kill. For
structures the honest subset is firepower/reload/damage-taken, not speed.
**Touches:** `vehicles.yaml` (4 actors) and/or `structures-defenses.yaml` (~20 actors) + a new template.
**Risk:** medium; a ranking `PBOX` changes defensive value materially, and rank stock for statics
interacts with the buy menu.

### 5.4 Add a non-kill XP path for medics, engineers and transports — **M effort**
§3.3 / §3.4 / §3.2 — three unit classes whose bonuses are well-shaped and unreachable. The engine
already has the machinery: `ExperienceTrickler`, `DeliversExperience` and `AcceptsDeliveredExperience`
all exist under `engine/OpenRA.Mods.Common/Traits/` and **none is used anywhere in `mods/ww3mod/rules/`**.
`ExperienceTrickler` on a medic is the smallest version of this.
**Risk:** medium — a time-based trickle rewards hiding, which cuts against the model. A heal-triggered
grant would be better and needs engine work.

### 5.5 Show enemy rank — **S effort, pure UX**
Every chevron is `ValidRelationships: Ally` with `Enemy, Neutral` commented out on all four entries
(`defaults.yaml:361,369,377,385`). You cannot see that the tank bearing down on you is a veteran, which
makes the whole system invisible in the moment it matters most.
**Touches:** `defaults.yaml`, 4 lines.
**Risk:** low mechanically. It is a deliberate-looking information-symmetry choice, so it needs a ruling
rather than a fix. **Recommend asking before touching.**

### 5.6 Widen the axes with traits the mod already has — **S–M effort each**
Currently unused *anywhere* in `mods/ww3mod/rules/`: `RangeMultiplier`, `RevealsShroudMultiplier`,
`VisionModifier` (defined in `Multipliers/VisionMultiplier.cs:15-27`), `ReloadAmmoDelayMultiplier`,
`DetectCloakedMultiplier`, `CreatesShroudMultiplier`, `GivesExperienceMultiplier`.
Already used elsewhere and therefore proven in this mod:
`InaccuracyMultiplier` (`aircraft.yaml:399`), `BurstMultiplier` (`infantry.yaml:497`),
`BurstWaitMultiplier` (`aircraft.yaml:405`), `TurretTurnSpeedMultiplier` (`vehicles.yaml:290`).

The two best fits for a "veteran" fantasy, in order:
- **`InaccuracyMultiplier`** — veterans shoot straighter. Proven in-mod, reads instantly, and unlike
  firepower it is not already double-counted (§4.5).
- **`VisionModifier`** — veterans see further. Live in the suppression ladder (`infantry.yaml:465-495`),
  so zero engine risk, and it is the natural pair to the concealment axis that is currently capped out.

`TurretTurnSpeedMultiplier` is a good third for vehicles specifically.

### 5.7 Candidates to REMOVE
- **`ConditionModifier@Rank_1` block** (`defaults.yaml:290-293`) — delete it or replace it with a
  comment saying the trait does not exist (§3.6). It is a trap as written. **S.**
- **`disable-player-experience.yaml`** — delete, or wire it into `mod.yaml`. Today it is a file that
  looks load-bearing and is not (§3.8). Decide which. **S.**
- **`vehicles-ukraine.yaml`** — same shape, same call. **S.**
- **The `~disabled` accrual timers** (§3.7) — a `Prerequisites` check in `RankAccumulation.Accrues`
  (`:354-357`) would stop banking ranks nobody can spend. Cosmetic; low priority. **S.**
- **Do NOT remove `ReloadDelayMultiplier`** even though it overlaps firepower (§4.5) — it is what makes
  burst weapons and single-shot weapons rank differently. Fold the overlap into the *values* instead.

### 5.8 What can actually verify any of this
Per `DOCS/recipes/BALANCE.md`, the two tools split cleanly: `tools/combat-sim` answers *"what does this
unit look like"* from live YAML and **"never simulates combat"**; `tools/autotest/run-test.sh
test-balance-*` answers *"who wins"* by running the engine.

So for rank work: **the combat-sim dashboard is the wrong tool** — the rank multipliers are
`ConditionalTrait`s gated on a runtime `rank-veteran` token that does not exist in a static ruleset
dump, and the dashboard does not fight anything anyway. Rank effects have to be measured in-engine.
There is already a `tools/autotest/scenarios/test-rank-accumulation/` scenario covering the *currency*
half. A `test-balance-*` duel with one side pre-ranked via `ProducibleWithLevel: InitialLevels`
(pattern at `infantry.yaml:1217`) or `VeterancyLevelInit` is the shape that would measure the *combat*
half. I did not run anything.

---

## §6 — Where to change what, ranked by files touched

| Goal | Edit | Files |
|---|---|---|
| **Change thresholds / add or remove a rank** | `Conditions:` map, `defaults.yaml:282-286`. Level count = number of entries (`GainsExperience.cs:81`) | **1** |
| **Change what every rank grants** | the five `*@Rank_N` blocks, `defaults.yaml:294-353` | **1** |
| **Change a per-unit XP rate** | `GainsExperience: ExperienceModifier:` on the actor (pattern: `crew.yaml:45-46`) — overrides cost-scaling with a flat value (`GainsExperience.cs:89`) | **1** |
| **Change the purchase/accrual curve** | fields on `RankAccumulation`, `player.yaml:22` (currently bare — all defaults from `RankAccumulation.cs:281-303`) | **1** |
| **Change what a rank grants for ONE class** | add/remove `*@Rank_N` on that class's template — `^Vehicle` (`vehicles.yaml`), `^Infantry` (`infantry.yaml`), `^Aircraft`/`^Helicopter` (`aircraft.yaml`). Removals use `-Trait@Rank_N:` and **apply where they appear** (see `aircraft-america.yaml:693-697` for the idiom) | **1–2** |
| **Add ranks to a class that has none** | `Inherits@GainsExperience: ^GainsExperience` on the template + the actors' own file | **1–2** |
| **Change the visual/audio cue** | chevron `WithDecoration@Rank_N` (`defaults.yaml:355-386`) · sprite frames (`sequences/sequences-misc.yaml:530+`) · infantry margin override (`infantry.yaml:749-757`) · promotion sound (`rules/sound/notifications.yaml:127`) · promotion sprite (`defaults.yaml:287`) | **3–4** |

**Note the asymmetry, because it is the useful thing here:** every *numeric* change is a one-file edit
in `defaults.yaml`, while every *cosmetic* change is spread across three or four. If the goal is
tuning, this system is unusually cheap to work on.

---

## §7 — What I could not verify (hypotheses, flagged)

1. **Nothing was executed.** No build, no game, no `--check-yaml`, no `make test`, no autotest — per the
   brief. Every statement is static reading of `main @ 9cb423d4`.
2. **My inheritance resolver is not the engine's MiniYaml loader.** I parsed the 35 `Rules:` files in
   load order and applied `Inherits@` / `-Trait` in appearance order, which matches the documented
   semantics in `DOCS/reference/conventions.md`. It reproduces known facts (the 3.0–9.0 min accrual
   spread asserted in `player.yaml:18-21`, the `-GainsExperienceMultiplier@TLBoost` on `^TL`), so I
   trust the group table — but the authoritative check is `--check-yaml`, which I did not run. **The
   unit counts (95 buildable / 71 ranking) are hypothesis-grade at ±1**; the group *shapes* I am
   confident in.
3. **Map-level rules can override anything here.** I read only `mods/ww3mod/rules/`. A shipped map or
   autotest scenario carrying its own rules could change ranks for that map. Not checked.
4. **The concealment→cells conversion is not mine.** §4.3 works in vision *levels* and the ceiling
   arithmetic is exact. The comment at `vehicles.yaml:77-80` maps level 2 → 28 cells and level 3 → 25
   cells; I did not re-derive that mapping, so *"how many cells does a rank buy"* is unanswered.
   The claim that ranks 2–4 add **zero** for a dug-in infantryman does not depend on it.
5. **§4.5's 1.875× is arithmetic, not measurement.** `1.20 × (1/0.80) × (1/0.80)` assumes reload delay
   translates 1:1 into sustained DPS and that armour math is linear in `DamageMultiplier`. Both are
   standard OpenRA behaviour but I did not trace `Armament.ReloadDelay` or the damage pipeline to
   confirm, and burst weapons will not obey it exactly.
6. **Whether `combat-sim --dump-balance-json` emits conditional traits at all** — I inferred from
   `BALANCE.md`'s "the dashboard never simulates combat" that it cannot measure rank effects. I did not
   read the dumper source. The conclusion (measure ranks in-engine, not in the dashboard) holds either
   way, but the reason might be narrower than I stated.
7. **`PlayerExperience` is live but I did not trace what consumes it.** `disable-player-experience.yaml`
   does not load (§3.8), so `PlayerExperienceModifier: 1` (`defaults.yaml:6`) is in force and
   `PlayerExperience` is on the player (`player.yaml:472`). Whether anything in WW3MOD *reads* that
   player-level pool — or whether it is a fully vestigial RA mechanic — I did not establish.
8. **Design intent vs. defect.** I have called the concealment cap (§4.3) and the `ConditionModifier`
   block (§3.6) defects because the code contradicts itself. Everything in §5 is a *proposal*.
   In particular, ally-only chevrons (§5.5) and rankless defenses (§3.5) may well be deliberate;
   I found no document either way.
