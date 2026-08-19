# Cover as protection, and the Take Cover search

**Date:** 2026-08-20
**Status:** research + design. No behaviour changes, no stat changes in this branch.
**Branch:** `wt/cover-protection`

> *"Cover means defensive though, more than about visibility. And currently I am not sure if
> any positions provide higher defense or damage reduction, or cover? ... Ideally I want take
> cover to find the highest protected cells, and secondarily also look for those that have the
> lowest chance of being seen at."*

**Headline.** Positional protection **does exist** — the user's uncertainty is well founded but the
answer is not "no". It exists in two forms of very unequal size, and the *larger* one is not damage
reduction at all. Meanwhile the one mechanic a player would most expect to protect them — going
prone — is **functionally dead**. And none of it is visible in-game.

---

## Part 1 — the ground truth: what reduces incoming damage today

### 1.1 The damage pipeline

`Health.InflictDamage` (`engine/OpenRA.Mods.Common/Traits/Health.cs:158-185`) applies every
`IDamageModifier` **multiplicatively**:

```csharp
var appliedDamage = (decimal)damage.Value;
foreach (var dm in damageModifiers)          // Health.cs:172-177
    appliedDamage *= modifier / 100m;
```

Modifiers are gated on `damage.Value > 0` (`Health.cs:169`), so healing is never dampened.
Multiplicative composition is the reason the existing systems do not stack into absurdity —
see §3.2.

### 1.2 Inventory of positional protection

| # | Mechanism | Where | Magnitude | Type | Live? |
|---|---|---|---|---|---|
| 1 | `DensityModifiesDamage` (forest cover) | `infantry.yaml:37-45` | 94% / 88% / 80% | % of all damage | **yes** |
| 2 | `DamageMultiplier@GarrisonCover` | `infantry.yaml:190-192` | **20%** (i.e. −80%) | % of all damage | **yes** |
| 3 | `ClearSightThreshold` shot refusal | `Armament.cs:364`, `AutoTarget.cs:1428` | binary immunity | LOS gate | **yes — dominant** |
| 4 | `MissChancePerDensity` (ATGM only) | `weapons-missiles.yaml:56-58,123-125,240-242` | up to 95% miss | hit chance | **yes, 3 weapons** |
| 5 | Veterancy `DamageMultiplier@Rank_1..4` | `defaults.yaml:223-234` | 95/90/85/80 | % | yes — *not positional* |
| 6 | Prone `ProneDamageModifiers` | `infantry.yaml:297-302` | 10–80% | per-damage-type | **effectively dead** |
| 7 | Dug in (`dugin`) | `infantry.yaml:141,719-721` | — | **concealment only** | yes, but no protection |
| 8 | `TerrainModifiesDamage` | engine trait exists | — | — | **dormant, unattached** |
| 9 | `^CivField` fields | `civilian.yaml:129-175` | **zero** | — | contributes nothing |

### 1.3 The big one is not damage reduction — it is shot refusal

`FiringLOS.HasClearLOS` (`engine/OpenRA.Mods.Common/Traits/FiringLOS.cs:46-113`) reads the
precomputed pairwise `ShadowLayer[from,to]` and returns `shadow <= threshold`. If the foliage
between shooter and target exceeds the weapon's `ClearSightThreshold`, **the weapon cannot fire
at all.** Not reduced damage — no shot.

Density→shadow curve, `Map.ForestGroundShadow` (`engine/OpenRA.Game/Map/Map.cs:1102-1120`),
deliberately superlinear so a thin treeline is weak and a deep one is strong:

| dense tree cells crossed | 1 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|---|
| ground shadow | 1 | 2 | 4 | 6 | 8 | 10 |

Weapon thresholds in `weapons-ballistics.yaml` run 2, 3, 4, 6, 7, 10, 255. So a rifle at
threshold 4 is **refused entirely** once 4+ dense tree cells lie on the line. This dwarfs the
20% ceiling of mechanism #1 and is almost certainly what a player perceives as "cover working",
without any UI ever saying so.

Three caveats that matter for design:
- Only active between **2 and 32 cells** (`FiringLOS.cs:77-82`). Inside 2 cells it always
  returns clear; beyond 32 it falls back to `BlocksProjectiles`.
- **`IndirectFire` units bypass it completely** (`FiringLOS.cs:49-51`) — artillery and mortars
  do not care about your forest.
- It is *binary*. One unit of shadow either side of the threshold flips between full exposure
  and total immunity. That is a cliff, and Take Cover would sit right on it (§4.5).

### 1.4 Prone is a decoration

`InfantryStates.GetDamageModifier` (`engine/OpenRA.Mods.Common/Traits/Infantry/InfantryStates.cs:195-205`):

```csharp
if (damage == null || damage.DamageTypes.IsEmpty)  return 100;   // :200-201
var modifierPercentages = info.ProneDamageModifiers.Where(x => damage.DamageTypes.Contains(x.Key))...
```

Prone only reduces damage from warheads that **explicitly declare** a matching damage type.
The mod configures five tiers — `Prone10Percent` … `Prone80Percent` (`infantry.yaml:297-302`).

**Census of the whole mod's weapon set:** 109 `DamageTypes:` declarations, of which **exactly
one** carries a `Prone*` token — `Prone30Percent` on a superweapon warhead
(`weapons-superweapons.yaml:399`). Every bullet is `BulletDeath`, every shell `ExplosionDeath`.

> **Going prone reduces damage from one superweapon and from nothing else in the game.**

This directly answers the user's doubt. The prone table *looks* like a working cover system in
the YAML and does nothing. Note also that prone is largely involuntary here —
`ProneCondition: deployed || suppressed > 30 || !moving || critical-damage`
(`infantry.yaml:294`) means any halted infantryman is already prone.

Prone *does* still buy: −40% move speed (`ProneSpeedModifier: 60`), a muzzle offset, a sequence
change, and **+1 concealment** (`DetectableAddativeModifier@Prone`, `infantry.yaml:716-718`).

### 1.5 Dug in protects nothing

`dugin` is granted after 200 ticks stationary (`GrantConditionOnMovement.ConditionWhenStill`,
`infantry.yaml:139-142`). Its only consumer is `DetectableAddativeModifier@Dugin`
(`infantry.yaml:719-721`), worth **+1 concealment and zero damage reduction**. There is no
`DamageMultiplier` keyed on `dugin` anywhere in the mod.

### 1.6 What the numbers actually mean in trees

`DensityModifiesDamage` sums `Map.DensityLayer` over a 3×3 window (`SampleRadius: 1`) and picks
the highest threshold ≤ that sum (`DensityModifiesDamage.cs:77-87`, `SelectModifier` `:95-109`).

Every tree in `decoration.yaml` is **density 10 on one cell** (`:104,117,130,143,156,169,182`;
`T08` = 5 at `:195`). Buildings are 15/20/50. So the shipped thresholds mean:

| threshold | damage | = how many trees in the 3×3 |
|---|---|---|
| 15 | 94% | **2** trees (a lone tree at 10 does *not* qualify) |
| 30 | 88% | 3 trees |
| 50 | 80% | 5 trees |

**A single density-50 building neighbour clears all three tiers on its own** — standing next to
a house is maximal "forest cover". That is probably not intended, and it is invisible.

Attachment is `^Infantry` only (`infantry.yaml:37`) — **vehicles get no cover from anything**
except the LOS gate.

### 1.7 Is any of it visible to the player?

**No.** This is a finding in its own right.

- `DensityModifiesDamage` has no decoration, overlay, icon or tooltip anywhere.
- Shot refusal has no indicator; the weapon simply never fires.
- The one relevant HUD element, `^DetectableRangeCircles` (`infantry.yaml:751+`, inherited at
  `:22`), draws a detection-radius ring — but it keys on `Detectable.CurrentVisibility`, which
  the sightline shadow system never touches (shadow is applied downstream in `MapLayers`).
  **Walking into deep forest moves that ring by exactly zero.**

So the user's "I am not sure if any positions provide higher defense" is the correct reading of
the available evidence. Three protection systems are running and the game tells him about none
of them.

---

## Part 2 — deeper into the forest

**Is forest a per-cell quantity?** Yes. `Map.DensityLayer` is a `CellLayer<byte>`
(`engine/OpenRA.Game/Map/Map.cs:252`), a dense whole-map array with O(1) random access, built at
map load from map-authored actors implementing `IDensityInfo` (`SetDensityLayer`,
`Map.cs:976-1002`) and baked into `shadows.bin` (`Map.cs:469-494`, `946-974`).

**Can a cell know how far it is from the forest edge?** Partly, and cheaply — but not fully.

- There is **no distance transform** anywhere in the codebase.
- `TerrainAffordanceLayer` (`engine/OpenRA.Mods.Common/Traits/World/TerrainAffordanceLayer.cs`,
  registered `mods/ww3mod/rules/world.yaml:340`) precomputes per cell, once at load: an
  8-neighbour density sum → `CoverQuality`, plus a density *gradient* whose magnitude flags
  `IsCoverEdge` and whose direction gives `OutwardFacing` (`:101-134`, lookups `:141-157`).
  That is an edge detector; interior = high `CoverQuality` && `!IsCoverEdge`.
- A windowed sum is a **saturating** proxy for depth. `ConcealmentScore` uses
  `ConcealmentWindowRadius = 2` (`CohesionMoveModifier.cs:209,338-346`), so past ~2 cells from
  the edge every interior cell scores identically. A 5-deep interior is indistinguishable from
  a 3-deep one.

**Verdict:** "deeper is better" is expressible **for the first 2–3 cells and no further**, at
zero runtime cost. True depth would need a new precomputed layer — a multi-source BFS outward
from all zero-density cells over `DensityLayer`, O(cells) once at map load, stored as another
`CellLayer<byte>` beside the existing ones. That is genuinely cheap and is the honest way to get
what the user asked for. It is not a reason to abandon the feature.

**But the pairwise layer is better than either.** `ShadowLayer[from,to]` already stores the
crossed density between *every* pair of cells 2–32 apart, and `FiringLOS.GetGroundShadowDensity`
(`FiringLOS.cs:120-154`) reads it in O(1). "How well does this cell protect me *from that
shooter*" is already computed and sitting in memory. See §3.1 — this reframes the whole design.

### 2.1 Two defects found along the way

**(a) Density is static; dead trees still give cover.** `UpdateDensityForBuilding`
(`Map.cs:1187`), `QueueShadowUpdate`, and `FlushPendingShadowUpdates` are all present and all
**unused** — the only callers are commented out (`Building.cs:377-396`, `World.cs:514-517`).
Husks spawn at runtime and are not in `ActorDefinitions`. **A forest that has been shelled flat
still grants full cover, full concealment, and still refuses rifle shots.** This is the most
player-visible correctness problem in the whole area, and Take Cover would actively steer squads
into burnt ground.

**(b) The `object-proximity` cover ladder is dead code.** `DetectableAddativeModifier@InCover1/2/3`
(`infantry.yaml:704-715`) grades concealment by an `object-proximity` condition. The only
emitters of that condition in the entire mod are tree **husks**
(`mods/ww3mod/rules/husks/husks.yaml:118-121` + 19 more) — **live trees grant none** (zero
`ProximityExternalCondition` in `decoration.yaml`). And `Range: 384` is 0.375 cells, so even a
husk only fires when a unit stands almost exactly on it. In a living forest this ladder never
triggers.

Both are recorded for `WORKSPACE/bugs/discovered.md`; neither is fixed on this branch.

---

## Part 3 — what it would cost to build protection

Protection is **not** absent, so the question becomes: what is missing, and what would the
missing parts cost? Three things are missing — *directionality*, *calibration*, and *legibility*.

### 3.1 Where the value lives — directional cover is NOT out of reach

The expected answer is "directional cover is too expensive". **That is wrong here**, and it is
the most important design finding in this document.

| option | cost | fidelity |
|---|---|---|
| per terrain type | trivial | useless — trees sit on Clear tiles, which is exactly why `TerrainModifiesDamage` is dormant |
| per cell, omnidirectional (**shipped today**) | 9 array reads per damage event | a wall to the north protects you from a shot from the south |
| **per attacker-target pair (directional)** | **1 array read** | correct |

Directional is *cheaper* than what ships today, because `ShadowLayer` is already built for the
LOS gate. `DensityModifiesDamage.GetDamageModifier` receives `Actor attacker`
(`DensityModifiesDamage.cs:61`) and currently ignores it. Replacing the 3×3 window with
`FiringLOS.GetGroundShadowDensity(attacker, self)` yields true directional cover, reuses the
already-calibrated `ForestGroundShadow` curve, stays pure-integer and zero-RNG, and removes the
"standing next to a house is deep forest" artefact.

Costs and limits of that swap, honestly stated:
- The 2–32 cell window (`FiringLOS.cs:137`) means point-blank and very long-range attacks return
  0 — no cover. Point-blank is defensible; the >32 case is a real hole.
- Splash damage has an `attacker` but the meaningful line is from the *impact point*, not the
  shooter. Needs thought before implementation.
- The layer is baked, so it inherits defect 2.1(a) — dead trees still shelter you.
- Thresholds would need re-authoring: shadow units (1–10) not window sums (0–100+).

### 3.2 Composition with prone, dug-in and armour

Composition is already bounded by construction: `Health.cs:172-177` multiplies. Garrison 20% ×
forest 80% × veterancy 80% = 12.8%, not a negative number. There is no additive path to absurdity.

The real bound needed is on **shot refusal**, which does not compose — it short-circuits. Stacking
forest cover on top of a threshold that already refuses the shot buys literally nothing, while
stacking it under one that doesn't buys the full 20%. Any Take Cover scoring must model that
cliff or it will systematically mis-rank cells (§4.5).

Since prone (§1.4) and dug-in (§1.5) contribute **zero** damage reduction, there is currently
nothing to compose them *with*. If prone is revived, the natural bound is: prone reduces
*direct-fire* damage, cover reduces *all* damage, and the two multiply — worst case
0.8 × 0.7 = 56%, which is survivable design.

### 3.3 Balance blast radius

`DensityModifiesDamage` is global and applies to humans and bots alike, on `^Infantry` only.
Any change to it — including the directional swap in §3.1 — invalidates:

- **`tools/combat-sim/`** — every infantry-vs-infantry matchup result. The sim would need a
  density input it may not model at all; if it assumes open ground it is currently *correct*
  only by accident for open-field fights and wrong for every forest map.
- **The AI benchmark baseline.** Per `CLAUDE.md`, a behavioural change reaching `@stable` must be
  re-baselined knowingly and stated in the commit message. Combat outcomes shift on every
  tree-bearing map.
- **Every autotest scenario with trees**, notably `tools/autotest/scenarios/test-case01-forest-ambush/`,
  which already asserts against density behaviour.
- **Nothing for vehicles** — they have no cover trait, so armour balance is untouched.

Worth stating plainly: the 20% ceiling is small next to the binary LOS gate. Re-measuring is
required regardless, but the *dominant* term in any forest firefight today is already
shot refusal, so existing balance numbers taken on forest maps are questionable already.

### 3.4 How the player sees it

A protection system the player cannot read reproduces the exact problem this effort exists to
fix. Minimum viable legibility, cheapest first:

1. **A cover pip on the selected unit** — the sequence `pip-cover` already exists
   (`sequences-misc.yaml:236-238`, with a `pip-dugin` frame). Three states (none / light / deep)
   driven by the same value the damage modifier uses. This is nearly free and is the single
   highest-value change in this document.
2. **Make shot refusal audible/visible.** A unit that refuses to fire currently looks broken. A
   "no clear shot" cue is worth more than any damage-number tuning.
3. **A cover overlay while a Take Cover order is being placed** — tint candidate cells by
   protection score. Reuses `TerrainAffordanceLayer.CoverQuality` as an O(1) read.
4. Fix the detection ring (§1.7) so shadow feeds `CurrentVisibility`, or drop the claim it makes.

---

## Part 4 — the Take Cover search

### 4.1 What already exists (do not rebuild it)

- **No player-facing Take Cover command exists.** The `Button@TAKE_COVER` widget was a dummy —
  no hotkey, no `OnClick`, no receiving trait — and was **removed** on `wt/take-cover`
  (`WORKSPACE/bugs/discovered.md:2073-2130`). Order plumbing and UI are greenfield.
  That entry also records *why* it was never wired, which matters here: commit `82f0b8eb`
  (2023-04-06) renamed the `TakeCover` trait to `InfantryStates` and **made prone automatic**,
  three years before the button was authored. The button was unfinished, not cut. So the
  mechanic the user is asking for was, historically, deliberately taken out of player hands —
  and §1.4 shows the damage reduction it once implied has since decayed to nothing.
- **Cover scoring exists**, three times over, all private to one trait:
  `CoverScore` (`CohesionMoveModifier.cs:310-328`, 8-neighbour, excludes own cell),
  `ConcealmentScore` (`:338-346`, 5×5 through `ForestGroundShadow`),
  and `TerrainAffordanceLayer.CoverQuality` (O(1), precomputed).
- **Cell searches exist**: `ComputeSpreadSlots` (`:698-777`, radius 4),
  `PickCoverSlotNear` (`:858-930`, radius 2–4), `PickConcealedCellNear` (`:420-465`, radius 3).
  All run **at order time**, never per tick, memoised across the order's subjects (`:1093,1201`).
- **`StancePositioningExecutor.cs`** already does autonomous idle repositioning to a
  threat-facing cover edge: `ChooseTarget` (`:510-580`) walks a Manhattan disk of
  `LeashRadius = 4`, keeps `IsCoverEdge` cells whose outward normal faces the threat, validates
  with `CanStayInCell && CanEnterCell` (`:620-626`), ranks by `CoverQuality`, throttled to every
  30 ticks (`:335-339`). It is **live for human players** via
  `GrantConditionOnHumanOwner@tacpos` (`defaults.yaml:41-45`).

> Take Cover is therefore best specified as **a player-triggered, explicit invocation of the
> search `StancePositioningExecutor` already performs implicitly** — not a new subsystem. The
> first implementation step is extracting that scorer into a shared service, because there are
> already three near-duplicate copies and `feedback_duplication_vs_verification` is explicit
> that a fourth copy is not acceptable.

### 4.2 The scoring function

Per candidate cell `c`, for unit `u` with threat bearing `T`:

```
protection(c) = shadow_between(c, T)                      // O(1), ShadowLayer
                + garrison_bonus(c)                        // if c is an enterable garrison slot
concealment(c) = ForestGroundShadow(windowed_density(c))   // 25 reads, or CoverQuality O(1)
score(c)      = W_P * protection(c)
              + W_C * concealment(c)
              - D   * chebyshev(u.Location, c)
```

**Protection first, concealment second** is the user's requirement, so `W_P >> W_C`. But strict
lexicographic ordering is wrong: protection saturates (§2), so a lexicographic sort leaves large
plateaus where concealment never gets consulted, and equally lets a +1 protection difference
override a huge concealment gain. A weighted sum with `W_P` dominant but finite is the correct
shape — and it matches what `PickCoverSlotNear` already does.

Where `T` comes from: `SightingThreatLayer` (`world.yaml:337`) gives fog-legal per-player threat
intensity and direction. With no known threat, fall back to omnidirectional `CoverQuality`.

### 4.3 Cost and affordable radius

Measured against what the code actually does:

| operation | cost |
|---|---|
| `DensityLayer` / `CoverQuality` read | array index + bounds check |
| `ShadowLayer[from,to]` read | one array index |
| `Mobile.CanStayInCell` | cached `CellFlag` bit test (`Mobile.cs:597-600` → `Locomotor.cs:368-374`) |
| `Mobile.CanEnterCell` | same `blockingCache`; iterates actors only when the cell is not free |
| **`PathFinder.FindPathToTargetCell`** | **hierarchical bidirectional A* — dominates everything above by orders of magnitude** |

**A single A* costs more than scanning hundreds of candidate cells.** Every existing cover search
in this repo therefore scores with `CanStayInCell`/`CanEnterCell` and **never pathfinds per
candidate** — that convention must hold.

Affordable radius: **6 cells (13×13 = 169 cells) per unit, comfortably**, at order time only.
Existing searches use 2–4; 6 is a safe ceiling and still tactically meaningful. Use `PathExistsForLocomotor`
(`PathFinder.cs:190-193`, cheap HPF connectivity) rather than a full path if reachability must be
verified, and issue exactly one real path for the chosen cell.

### 4.4 Several soldiers, one best cell

Reuse the existing, unit-tested primitive rather than inventing one: `ResolveConcealmentSlots`
(`CohesionMoveModifier.cs:487-501`) seeds a `taken` set with every original slot, frees the
current unit's own cell exactly once, and re-adds the pick — pinned by
`StanceCoverPositioningTest.cs` (keep-assigned bias, never trade concealment away, margin gate,
deterministic tie-break). Process units in a deterministic order, each excluding cells already
claimed. `AssignAll` (`:1270-1316`) additionally does greedy minimum-distance matching so units
do not criss-cross.

Note the engine fallback is *not* good enough on its own: `Mobile.NearestMoveableCell`
(`Mobile.cs:834-856`) has no cross-unit awareness, so N units sent to one cell converge and
shuffle.

### 4.5 What stops the squad scattering — and the worst failure mode

Anti-scatter is the distance penalty `D`, and it must be steep. Existing code uses
`- cheb*5` (`PickCoverSlotNear`) and `- cheb*2` for group cohesion in `ComputeSpreadSlots`. Plus:
a hard radius cap (§4.3), a **minimum improvement margin** below which the unit does not move at
all (the margin gate already exists in the tested code), and squad-relative cohesion so the
chosen cells stay mutually close.

**Worst failure mode — the transit loss.** Cover tops out at **20% damage reduction**. A squad
ordered to Take Cover will happily cross open ground to reach a marginally better treeline, and
**take more damage walking there than the cover will ever save** — while prone-and-halted units
stand up to move, losing their (concealment) prone bonus and moving at 60% speed. The feature
then looks actively harmful, which is worse than looking stupid.

Three mitigations, all required:
1. The margin gate must be expressed in *expected damage over the walk*, not raw score delta —
   a 4-cell walk under fire must clear a much higher bar than a 1-cell shuffle.
2. Prefer cells that cross the **shot-refusal threshold** (§1.3) over cells that merely add a few
   percent. Crossing from "shootable" to "not shootable" is worth an order of magnitude more
   than any damage percentage, and a naive linear score will not see that. Score the threshold
   crossing explicitly.
3. Never route through a cell with lower protection than both endpoints if an equal-length route
   exists — otherwise the path itself defeats the order.

**Second failure mode:** because density is static (§2.1a), a shelled-flat forest still scores as
deep cover. Take Cover would confidently march a squad into a burnt husk field and report success.
This should be fixed — or Take Cover explicitly scoped — before the feature ships.

---

## Recommended order of work

1. **Legibility first** (§3.4.1–2) — a cover pip and a "no clear shot" cue. Cheapest, and it
   resolves the user's actual complaint ("that is not clear in-game") without touching balance.
2. **Fix the static-density defect** (§2.1a) — otherwise every later step builds on a lie.
3. **Extract one shared cover scorer** from the three near-duplicates (§4.1).
4. **Directional cover** (§3.1) — reuses `ShadowLayer`, cheaper than today, needs re-baselining.
5. **Take Cover order + UI** (§4) — last, because it is the only step that is genuinely greenfield
   and it depends on all of the above.

Steps 2 and 4 are behavioural and require an AI-benchmark re-baseline stated in the commit
message per `CLAUDE.md`. Step 1 is not behavioural and can ship independently.

---

## Sources

All line references verified against `wt/cover-protection` @ `4bb3fae9` on 2026-08-20.
Prone census (`109 DamageTypes:` declarations, 1 carrying `Prone30Percent`) counted directly
over `mods/ww3mod/rules/weapons/`. The prior recon claim that trees are visible from "3–5 cells"
was re-derived and **does not hold** — base infantry `Detectable.Vision: 3` against the
`^StandardVision` ladder (`defaults.yaml:47-90`) means a halted infantryman in the open is
detected from ~22 cells, and `moving` grants −1 (`infantry.yaml:730-732`), so halting makes a
unit *harder* to see, not easier.
