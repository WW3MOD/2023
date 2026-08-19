# Ambush, cover and detection — what the code actually does

**Date:** 2026-08-20 · **Branch:** `wt/ambush-audit` · **Base:** `main @ 4bb3fae9` (level with `origin/main`)
**Status:** read-only research. No code changed. **Game never launched** — launches are serialized
through the manager and none was requested for this pass. Every claim below is read from source.
Claims that can only be settled by playing are marked `[NEEDS RUN]` and collected in §6.

**Scope:** the three questions the user asked, in his order — (1) is the detection model correct,
(2) does coordinated ambush exist, (3) does cover/protection exist at all.

**Relationship to prior work:** `WORKSPACE/recon/260819-infantry-visibility-stances.md` (one day old)
covers overlapping ground and is largely right. It is **cited but not trusted**: its distance table is
arithmetic over the vision ladder rather than measurement, and its author said so. Where this document
agrees, it says so; where it corrects 260819 or the shipped comments, it says so loudly.

---

## 0. The short answer, in plain English

**What a player can do today:**

- Set a squad to **Ambush** stance from the command bar (`StanceSelectorLogic.cs:34-36`). The unit
  draws a yellow **`A`** glyph (`WithStanceDecoration.cs:50,52`).
- Have those units, **while idle**, silently rotate to face a target and **hold fire until one of them
  is seen** — then all of them within 10 cells open fire together. **This works. It is not a stub.**
  (`AutoTarget.cs:702-793`, coordination at `:976-993`.)
- Have that same group spring **when any one of them is shot**, whether or not anyone was seen
  (`AutoTarget.cs:634-681`).
- Get real, automatic concealment by **standing still and not shooting** — and now *see* it, as a grey
  circle drawn at the range from which a standard observer would spot the unit
  (`^DetectableRangeCircles`, `infantry.yaml:750-…`).
- Get a one-shot **"seat my ambushers in the trees"** pass: a human, Ambush-stance, non-Tight squad
  given a **group move order** has each formation slot re-seated onto the most tree-dense nearby cell
  (`CohesionMoveModifier.cs:1079,1183`; scoring at `:338-346`).

**What a player cannot do today, and would expect to:**

- **The good half of Ambush is bot-only.** The "halt before you walk into contact so you are never
  seen at all" behaviour and the four-trigger smart spring table are behind a condition,
  `enable-ambush-tactics`, that **only `LaneAmbushBotModule` grants, and only to the ≤4 units it posts
  itself** (`defaults.yaml:320`, `LaneAmbushBotModule.cs:451,474-490`). **No human unit ever carries
  it, on any map, in any mode.** Every one of the four ambush autotests grants it by hand in Lua
  because the behaviour does not otherwise happen (§2.4). This is the single biggest gap in the audit.
- **Stop running to avoid being spotted.** Nothing predicts detection. There is no "about to be seen"
  reaction anywhere (§1.5). The margin is computable and nothing reads it.
- **Take cover as a command.** There is no such control. Prone is fully automatic (it fires on
  `!moving`), and the dead `TAKE_COVER` button was removed on 2026-08-19 (`b62ee52f`, `486575a8`).
  Verified at HEAD: `TakeCover` appears nowhere in `mods/` and `TAKE_COVER` nowhere in the chrome (§3.1).
- **Benefit from being near cover objects for concealment.** The three largest concealment modifiers in
  the game — `@InCover1/2/3`, worth **+1/+2/+3** — are gated on a condition that **nothing anywhere
  grants**. They are dead (§1.4). This is the "modifier that can never fire" the audit was asked to find.
- **Get damage reduction from going prone.** The five-tier `ProneDamageModifiers` block is still in the
  YAML, but the reduction only applies against warheads declaring a matching `DamageType`, and commit
  `1802191e` (2024-02-13) stripped those from every live weapon. Prone has given **0% damage
  reduction** for two and a half years (§3.2). It still shrinks the hitshape, which is real but is a
  hit-probability effect, not damage.
- **Get meaningful protection from trees.** Position-based damage reduction exists and is live, but it
  caps at **20%**, and it keys on obstacle *density* rather than on trees specifically: one rock cell
  (density 50) grants the full 20% instantly, while one tree (density 10) falls below the 15 floor and
  grants **nothing** (§3.3). Buildings, at 80–97%, are the only cover in the game with a felt effect.

**Against the user's stated vision:** the *second* half of his vision (hold fire until one is
detected, then all fire at once) is **built and works for humans**. The *first* half (behave so as to
stay hidden — stop running when nearly spotted) is **not built at all**, and the closest thing to it
is locked to the AI.

---

## 1. The detection model

### 1.1 The trap: two different things are called "Vision"

| Trait | Field | Meaning | Direction |
|---|---|---|---|
| `Vision` (`^StandardVision`, `defaults.yaml:47-84`) | `Strength`, `Range` | how far **this unit sees** | higher = better eyesight |
| `Detectable` (`Detectable.cs`) | `Vision` | observer strength **required to see this unit** | **higher = stealthier** |

`DetectableInfo.Vision` is *"what level of vision is required to detect this actor"*. So a **positive**
`VisionModifier` makes a unit **harder** to see. Firing is **−2** (easier to see); prone is **+1**
(harder). This sign convention is inverted from most people's intuition and is the first thing to get
wrong. Agrees with 260819 §1.

### 1.2 The observer ladder

`^StandardVision` (`defaults.yaml:47-84`) is ten concentric annuli: strength 10 within 4 cells, 9 from
4–7c, 8 from 7–10c, and so on down to 1 at 28–32c. Reveal requires observer strength **strictly
greater** than the target's required level (`MapLayers.cs:579`). Terrain is already folded in —
`AddSource` subtracts a per-cell shadow term, `modifiedStrength = strength - shadowModify`
(`MapLayers.cs:355-378`), sourced from `map.ShadowLayer`. So forest along the real sightline already
reduces the observer's effective strength.

### 1.3 The modifiers, and which are live

All on `^DetectableInfantryStandard` (`infantry.yaml:703-732`), inherited by base infantry
(`infantry.yaml:21`).

| Modifier | Condition | `VisionModifier` | Granted by | Live? |
|---|---|---|---|---|
| `@Prone` | `prone` | **+1** | `ProneCondition: deployed \|\| suppressed > 30 \|\| !moving \|\| critical-damage` (`infantry.yaml:294-295`) | **YES** |
| `@Dugin` | `dugin` | **+1** | `GrantConditionOnMovement.ConditionWhenStill`, after `TimeToBeStill: 200` ticks (`infantry.yaml:139-142`) | **YES** |
| `@Moving` | `moving` | **−1** | same trait, `Condition: moving` (`infantry.yaml:140`) | **YES** |
| `@Firing` | `firinganyweapon` | **−2** | `GrantConditionOnAttack`, `RevokeDelay: 12` (`infantry.yaml:722-726`) | **YES** |
| `@InCover1` | `object-proximity == 1` | **+1** | — | **DEAD** |
| `@InCover2` | `object-proximity == 2` | **+2** | — | **DEAD** |
| `@InCover3` | `object-proximity >= 3` | **+3** | — | **DEAD** |
| `@Rank_1..4` | `rank-veteran == N` | see `defaults.yaml:211-223` | veterancy | live (see agent section) |

### 1.4 ⚠ The dead modifiers — `object-proximity` is granted by nothing

`object-proximity` is declared as an `ExternalCondition` on `^DetectableInfantryStandard`
(`infantry.yaml:704-706`, `TotalCap: 3`) and on husks (`husks.yaml:119`). An `ExternalCondition` is a
*seam*: it makes the token grantable but never grants it. **Nothing grants it.** Searched:

- all `*.cs` in `engine/` — no occurrence of `object-proximity` or `ObjectProximity` at all;
- all `*.yaml` and `*.lua` in `mods/` — only the two declarations above and the three consumers;
- `mods/ww3mod/maps/**` (map dirs, not archives) — no occurrence.

The only other trace is a **commented-out** `^DetectionProximity` template (`defaults.yaml:161-170`)
and its **commented-out** inherit on base infantry (`infantry.yaml:20`) — and note that template is
itself a *consumer* (`VisionModifier@1/2/3`, a different trait), not the grantor. So the grantor never
existed in shipped rules, or was removed with no trace left in the seam.

**Consequence:** the three biggest concealment bonuses in the game — bigger than prone and dug-in
combined — can never apply. "Being next to cover" contributes exactly zero to concealment.

This is the answer to the user's *"is any modifier unreachable in practice?"* — yes, three of the eight,
and they are the largest.

### 1.5 There is no "almost spotted" reaction

The *quantity* exists and is cheap: detection is `observer strength > required level`, so
`required − strength` is a graded margin, and one step of margin is roughly three cells of approach
(the ladder is distance-quantised). `WORKSPACE/recon/260817-unit-indicators.md` §A.1 works this out in
detail and concludes *"nobody reads the margin, and nobody reacts to it."*

Verified at HEAD: no predictive detection logic exists. Grepped `engine/**/*.cs` for
`WillBeSpotted|AboutToBe|PredictDetect|ImminentDetect|SpotRisk|DetectionRisk` — **zero files**.
Detection is read only as the after-the-fact boolean `CanBeViewedByPlayer` (47 call sites).

**So the user's first scenario — "a soldier almost spotted stops running to stay hidden" — has no
implementation, in any stance, gated or not.**

### 1.6 What the player is shown

Two indicators ship and both are attached to base infantry (`defaults.yaml:811-827` via
`^UnitIndicators`; circles via `infantry.yaml:22`):

- **Spotted marker** — a red `!` while an enemy *the viewing player is aware of* can see this unit
  (`WithSpottedDecoration.cs:16-27`). Deliberately binary and deliberately asymmetric: an enemy that
  can see you but that you have not spotted does **not** light it, because that would be a wallhack.
- **Concealment gauge** — a grey circle at the range from which a standard observer first sees the
  unit, so stopping / going prone / digging in visibly shrinks it (`^DetectableRangeCircles`,
  `infantry.yaml:733-…`). Its own comment records that the tier→radius ladder is derived **from the
  code, not from the arithmetic table in 260819 §3.4**, and that a previous version of the ladder was
  one band (~3 cells) too wide.

This meaningfully softens 260819's finding #4 (*"applied automatically, reported by nothing"*) — as of
HEAD it **is** reported, for a selected unit.

---

## 2. Coordinated ambush — it exists, and half of it is locked to the AI

### 2.1 Settling the "zero consumers" claim

A prior pass concluded `stance-ambush` has zero consumers and that `StancePositioningExecutor.cs:313-314`
explains why it deliberately does not consume it. **Both halves need correcting.**

- **The condition token `stance-ambush` genuinely has zero consumers.** It is granted in five places
  (`defaults.yaml:309` `^AutoTarget`, `:570` `^AutoTargetGround`, `:670` `^AutoTargetAirAssaultMove`,
  `:682` `^AutoTargetAll`, and `aircraft-russia.yaml:115`) and read by nothing: grepping `mods/` for
  `RequiresCondition:.*stance-ambush` or `!stance-ambush` returns **no matches**. It is a marker only.
- **But the Ambush *stance* is consumed heavily.** `UnitStance.Ambush` (`AutoTarget.cs:22`) drives a
  full state machine in `AutoTarget`, the garrison system (`GarrisonManager.cs:704,748,773,1197,1322`),
  `CohesionMoveModifier.cs:1079`, `AttackMoveActivity.cs:156`, and `LaneAmbushBotModule.cs:496,519`.
- **`StancePositioningExecutor` does *not* ignore Ambush — it honours it.** `:318` reads
  `FireStanceAllowsRepositioning(autoTarget.Stance)` in C# and refuses to reposition an Ambush or
  HoldFire unit. The comment at `:313-314` explains why the opt-out is written **in C# rather than as a
  `!stance-ambush` YAML clause** (the clause would force every `^Combatant` to consume a token granted
  on a different template, tripping the `CheckConditions` lint). It is not saying the executor ignores
  Ambush.

### 2.2 What a human actually gets — the stock path

`INotifyIdle.TickIdle` routes any unit at `Stance >= Ambush` with `ScanOnIdle` into `AmbushTickIdle`
(`AutoTarget.cs:684-693`). Ungated, every tick the unit is **idle**:

1. Scan for a target at **full range** — ambush does not shrink the scan radius (`:710-721`).
2. **Pre-aim**: rotate turrets, or face the body, toward the target **without firing** (`:752-753`,
   implementation `:956-974`).
3. Compute `isSpotted = self.CanBeViewedByPlayer(targetOwner)` (`:757`).
4. Stock branch (`:759-774`): if `isSpotted || ambushTriggered` → latch, and **if spotted, call
   `TriggerNearbyAmbushAllies`**, then attack.

`TriggerNearbyAmbushAllies` (`:976-993`) finds every actor within `AmbushCoordinationRadius` — default
**10 cells** (`:86`, no YAML override) — owned by the same player, and sets `ambushTriggered = true` on
any that is itself in Ambush stance. It also calls `GarrisonManager.TriggerAmbush()` on garrisoned
buildings, which force-deploys shelter soldiers to ports with valid targets (`GarrisonManager.cs:1194-1219`).

Separately and **also ungated**, `INotifyDamage.Damaged` (`:632-681`) springs the group when any
ambusher is shot: at `:674-678`, `if (Stance == UnitStance.Ambush) { ambushTriggered = true;
TriggerNearbyAmbushAllies(...) }`. Requires `self.IsIdle` (`:634`).

**So the user's second scenario is built and reaches humans: a group in Ambush holds fire, and when any
one of them is detected — or shot — they all engage.** That answers "I think we have tried implementing
this before" with: yes, `fea4617c` (2026-03-17, *"Rewrite Ambush stance: detection-aware with group
coordination"*), and it was never reverted.

**Three caveats that likely explain "I haven't seen it clearly in game":**

- **Idle-only.** `TickIdle` fires only when `self.CurrentActivity == null`. A unit still walking to
  position, or under any order, is not ambushing.
- **The spring is not literally simultaneous.** An ally is only *latched*; it fires on its own next
  idle tick, and scans are re-armed to a random 3–8 ticks (`:199,202,1157`). So the volley is spread
  over a few ticks rather than one.
- **The latch is terminal.** `ambushTriggered` is cleared only by changing stance away from Ambush
  (`:995-1003`). A group that springs once stays sprung.

### 2.3 ⚠ What a human does *not* get — and this is the big one

Two behaviours sit behind `AutoTargetInfo.AmbushTacticsCondition`, wired to `enable-ambush-tactics`
(`defaults.yaml:320`):

- **Stage 2 — halt before contact.** An Ambush unit that is attack-moving and scans an enemy **while
  its group is still unseen** ends the march and drops to idle instead of engaging
  (`AttackMoveActivity.cs:155-171`; predicate `AmbushTactics.cs:48-60`; group-level detection at
  `AttackMoveActivity.cs:201-230`, which returns "detected" if *self or any nearby Ambush ally* is
  visible to the target's owner). **This is the closest thing in the codebase to the user's forest-road
  scenario.**
- **Stage 3 — the smart spring table.** Five triggers in precedence order
  (`AmbushTactics.cs:161-191`): Detected → Damaged → BestStrikeDegrading (target predicted to leave
  range, with hysteresis) → Saturation (kill-zone value peaked) → Overrun (enemy inside minimum
  range). Fed by a cadenced kill-zone scan (`AutoTarget.cs:805-859`), defaults `AmbushKillZoneRadius: 8`,
  `AmbushScoreCadence: 25` (`:106,110`).

**Who grants `enable-ambush-tactics`:** exactly one thing. `LaneAmbushBotModule` grants it **per unit**
to the units it posts (`LaneAmbushBotModule.cs:451,474-490`), on both bot profiles
(`ai.yaml:834` `@experimental`, `:2218` `@stable`). Both are capped at
`MaxAmbushes: 2 × UnitsPerAmbush: 2` = **4 units** (`ai.yaml:837-838,2221-2222`). The only other
occurrences in shipped content are the `AutoTarget` field and the grantor seam
`ExternalCondition@ambushtactics` (`defaults.yaml:344-345`) — searched all `mods/**` `*.yaml`/`*.lua`.

Humans, and the Normal/Rush/Turtle bots, never instantiate the module, so `GetConditionCount` is
permanently 0 for them and **both branches are dead code from a player's point of view**.

Note also a documented dead argument: `Stage3EvaluateSpring` passes `damaged: false` literally
(`AutoTarget.cs:820`), so trigger 2 never fires through the table — damage is handled synchronously in
`INotifyDamage` instead (`:779` says so). Intentional, but it means the table has four reachable
triggers, not five.

### 2.4 The autotests prove it

All four ambush scenarios grant the gate by hand in Lua, because the behaviour does not otherwise occur:

- `test-ambush-detection/test-ambush-detection.lua:34` — `Ambusher.GrantCondition("enable-ambush-tactics")`,
  commented *"opt-in seam: ExternalCondition@ambushtactics"*.
- `test-ambush-enemy-stops/…lua:17` — same, and `:7` names the RED baseline as *"comment out
  GrantCondition below"*.
- `test-ambush-convoy/…lua:11` — same RED recipe: *"comment out the GrantCondition line below (gate off
  ⇒ stock ambush)"*.
- `test-ambush-fast-convoy` — same family.

Every green ambush test is therefore validating a configuration **no player can reach**. The machinery
is proven; the wiring to the player is absent.

### 2.5 Aim delay — there isn't one, for infantry

The user believes there should be an aim delay that ambushers skip. Today:

- **Facing imposes no delay on infantry.** `AttackFrontal.CanAttack` gates on
  `TargetInFiringArc(self, target, Info.FacingTolerance)` (`AttackFrontal.cs:34-39`). `FacingTolerance`
  defaults to `WAngle(512)`, and the field's own `[Desc]` reads *"Range [0, 512], 512 covers 360
  degrees"* (`AttackBase.cs:45-46`). **No infantry actor overrides it** — grepping `mods/ww3mod/` for
  `FacingTolerance` returns only aircraft, naval (commented) and one vehicle. So infantry can fire in
  any direction the instant a target is valid.
- **Therefore `PreAimAtTarget`'s infantry branch is cosmetic.** Its non-turreted path
  (`AutoTarget.cs:963-973`) turns the body toward the target — but since the firing arc is already 360°,
  that rotation buys no time advantage. It matters only for **turreted** units, where turret traverse is
  real. Labelled: *verified from code, not measured.*
- **The one real shot-timing delay is not bypassed by ambush.** `Armament.FireDelay` (default 3 ticks,
  `Armament.cs:42`; `RPG` 8, `60mm_Mortar` 10 — `infantry.yaml:1157,1479`) schedules the projectile
  after the fire decision (`Armament.cs:467`). Ambushers pay it like everyone else.

**So the user's "no aim delay for ambushers" is already true — because there is no aim delay for anyone.**
If an aim delay is wanted as a *cost* that ambushers are exempt from, it does not exist and would have
to be built.

### 2.6 History

`git log -S` on the ambush symbols, oldest last:

| Commit | Date | What |
|---|---|---|
| `fea4617c` | 2026-03-17 | **Rewrite Ambush stance: detection-aware with group coordination** — the origin of `AmbushTickIdle` + `TriggerNearbyAmbushAllies` |
| `b76d85d0` | 2026-03-26 | Garrison stance integration — fire discipline controls garrison behaviour |
| `9c94ce63` | 2026-07-21 | Stance/tactical-layer substrate survey (read-only) |
| `1a3f81f1` | 2026-07-22 | Widened-ambush design + staged plan |
| `3ddd0b40` | 2026-07-25 | **Stage 2** halt-before-contact |
| `d7549f83` | 2026-07-25 | **Stage 3** stationary state machine (DORMANT/TRACKING/SPRUNG) |
| `15922a38` | 2026-07-25 | Stage-3 fix: cooldown-`Invalid` was wiping cadence counters |
| `b8d2e601` | 2026-08-02 | `@stable` twin of `LaneAmbushBotModule` — gate now granted to bot ambushers on both profiles |

**Nothing was reverted.** The feature was built forward in stages and then, at `b8d2e601`, wired to the
*bots* rather than to the player. That is the whole story: not abandoned, just never connected to a human.

---

## 3. Cover and protection

**Short answer: yes, position gives damage reduction — one mechanism, capped at 20%, and it is
triggered far more reliably by rocks than by trees. Meanwhile the prone damage reduction that the
YAML still configures has been dead since February 2024.**

### 3.1 The damage pipeline, and every modifier on it

All reduction flows through one loop (`Health.cs:166-186`): each `IDamageModifier` on the victim
returns a percentage and they multiply. Healing is never modified (`:166`). There are exactly **eight**
`IDamageModifier` implementations in the tree; on WW3MOD infantry only these are attached:

| Implementation | `file:line` | On infantry? |
|---|---|---|
| `InfantryStates` (prone) | `InfantryStates.cs:74` | attached but **inert** — §3.2 |
| `DensityModifiesDamage` | `DensityModifiesDamage.cs:47` | **the only live positional reduction** — §3.3 |
| `TerrainModifiesDamage` | `TerrainModifiesDamage.cs:29` | **attached to nothing, in any mod** |
| `DamageMultiplier` | `DamageMultiplier.cs:27` | yes — veterancy + garrison |
| `GarrisonManager` | `GarrisonManager.cs:176` | buildings only |
| `HandicapDamageMultiplier` | `HandicapDamageMultiplier.cs:22` | commented out, `defaults.yaml:967` |
| `DrainPrerequisitePowerOnDamage`, `AttackPopupTurreted` | Cnc mod | no |

There is no "Take Cover" order. `82f0b8eb` (2023-04-06) renamed RA's `TakeCover` trait to
`InfantryStates` and made prone automatic and condition-driven; the vestigial UI button was removed on
2026-08-19 (`b62ee52f`, `486575a8`). Verified at HEAD: **zero** hits for `TakeCover` in `mods/`, zero
for `TAKE_COVER` in the chrome.

### 3.2 ⚠ Prone gives no damage reduction, and has not since 2024

`InfantryStates` implements the modifier (`InfantryStates.cs:195-205`) and `^CamoSoldier` configures
five tiers (`infantry.yaml:297-302`, `Prone10Percent: 10` … `Prone80Percent: 80`). Prone itself is
reached constantly — `ProneCondition: deployed || suppressed > 30 || !moving || critical-damage`
(`infantry.yaml:294`).

But the reduction only applies if the **incoming warhead** declares a matching string in its
`DamageTypes` (`InfantryStates.cs:200-203`). Exactly **one** warhead in the mod still does — `EmpBomb`
(`weapons-superweapons.yaml:399`) — and `EmpBomb` is referenced by **no actor anywhere in the repo**.

**Cause:** commit `1802191e` (2024-02-13), titled *"Remove all ProneXXPercent DamageTypes"*, stripped
them from six weapon files (~80 edits) and missed the one dead `EmpBomb` line. Empty commit body — no
rationale recorded.

**Prone does still protect, by a different route that never appears in damage arithmetic:** the
hitshape shrinks from radius **30** standing to **20** prone (`infantry.yaml:143-150`) — roughly a 56%
smaller cross-section, so fewer projectiles connect at all. It costs 40% speed
(`ProneSpeedModifier: 60`, `infantry.yaml:296`).

This also means `WORKSPACE/recon/260728-deploy-prone.md:13,52` **over-credits prone** with a damage
benefit it lost in 2024. Flagged for curation; not edited by this pass.

### 3.3 ⚠ "Forest cover" is really obstacle cover, and rocks beat trees five to one

`DensityModifiesDamage` is attached once, on `^Infantry` (`infantry.yaml:37`) — so every infantryman,
human and bot. Vehicles, aircraft and structures get nothing. It sums `Map.DensityLayer` over a **3×3
window including the centre cell** (`DensityModifiesDamage.cs:61-88`, `SampleRadius: 1` at `:39`) and
picks the highest threshold ≤ that sum (`:95-109`). Configured (`infantry.yaml:42-45`):

| Windowed density sum | Damage taken | Reduction |
|---|---|---|
| < 15 | 100% | none |
| ≥ 15 | 94% | 6% |
| ≥ 30 | 88% | 12% |
| ≥ 50 | 80% | **20% — the game maximum outside buildings** |

**`DensityLayer` is not a tree layer.** It is populated from *any* actor carrying `Building.Density`
(`Map.cs:976-1001`, `Building.cs:141-144`). In `decoration.yaml`:

- **Trees** `T01`–`T07`: `Density: 0,0, 10,0` — **10, on one cell** of a 2×2 footprint
  (`decoration.yaml:104,117,130,143,156,169,182`). `T08` gives **5** (`:195`).
- **Rocks** `ROCK1`–`ROCK7`: **50 per occupied cell** (`decoration.yaml:469,475,481,487,493,499,505`).
- **Tank traps**: **20** on one cell (`decoration.yaml:531,546`).

Consequences, which look unintended:

- **One tree beside you = 10, below the 15 floor = exactly zero protection.** The YAML comment at
  `infantry.yaml:41` says *"a lone treeline barely helps"*; in fact it does not help at all.
- **One rock cell anywhere in your 3×3 = 50 = the full 20%, instantly.** One rock outperforms five trees.
- Reaching 50 from trees needs five trunk cells among your nine — very tight packing.

So the mechanic that ships as forest cover is in practice **rock cover**, and trees — the feature the
user associates with hiding — are the weakest contributor per actor in the game. *(Whether maps
actually place rocks near contested ground is a map-data question this pass did not open — unverified.)*

### 3.4 Buildings dwarf everything else

- **Firing-port soldiers**: `DamageMultiplier@GarrisonCover`, `Modifier: 20` — **80% reduction** —
  on `^Soldier` under `RequiresCondition: garrisoned-at-port` (`infantry.yaml:190-192`), granted from
  C# (`GarrisonManager.cs:63`).
- **Shelter occupants**: not an `IDamageModifier` at all. Occupants take no direct fire; a fraction of
  the *building's* damage passes to one randomly-chosen occupant (`GarrisonProtection.cs:76-116`).
  Shipped `BaseProtection` 95–97 (`structures-defenses.yaml:153-156,238-241,328-331`,
  `civilian.yaml:109-113`), so an occupant of a healthy garrison takes **3–5%**, and `MinPassThrough: 15`
  (`GarrisonProtection.cs:108-109`) makes any hit under 15 pass-through deal **exactly zero**.

**So: buildings 80–97%, everything else ≤20%.** That two-order-of-magnitude gap in survivability is the
most likely reason none of the terrain cover is perceptible in play.

### 3.5 Edge versus interior of a forest

**For damage — an implicit depth effect only.** A cell deep in a cluster has more neighbours carrying
density, so the 3×3 sum is higher and a higher tier is selected. Nothing in the damage path knows the
words "edge" or "interior"; there is no falloff and no adjacency test beyond the fixed window.

**For visibility — depth is explicit and deliberately superlinear.** `Map.ForestGroundShadow`
(`Map.cs:1102-1120`) converts crossed tree density along the sightline into an observer-strength
subtraction: below the knee (`ForestShadowKneeDensity = 20`, `:1083`) it is `ceil(density/10)`; above
it, `2 + ceil(extra/5)`. Its own reference table (`:1098`): **1 dense cell → 1, 2 → 2, 3 → 4, 4 → 6,
5 → 8, 6 → 10**. The comment states the design intent outright (`:1090-1094`): *"a thin 1-cell treeline
barely dents detection … a genuinely DEEP cluster ramps up to real concealment … linear alone cannot
keep 1 cell weak AND make 4 cells hide."*

**So the user's forest-road picture is correct for concealment and wrong for protection:** soldiers 3–4
cells deep in trees are meaningfully harder to see than ones at the treeline, but they take essentially
the same damage once shot at.

**The engine does compute edge-vs-interior explicitly — and no damage code reads it.**
`TerrainAffordanceLayer` (registered at `world.yaml:340`) computes per cell at map load:
`CoverQuality` (`:101,115,126`), `IsCoverEdge` from the local density gradient (`:129-132`), and
`OutwardFacing` (`:133`). Its **only** consumer is `StancePositioningExecutor` (`:541,544,559`) — i.e.
where an idle unit chooses to stand. Note also that the two systems disagree about what "in cover"
means: the affordance layer **skips** cells that themselves carry density and **excludes** the centre
cell (`TerrainAffordanceLayer.cs:98-99,108-109`) because it is bidding for somewhere to stand, while
`DensityModifiesDamage` **includes** the centre and has no such guard (documented at
`DensityModifiesDamage.cs:14-17`).

### 3.6 Dug in gives no protection at all

`dugin` is granted after `TimeToBeStill: 200` ticks (`infantry.yaml:139-142`) and has exactly **one**
consumer in the mod: `DetectableAddativeModifier@Dugin` (`infantry.yaml:719-721`), which is
concealment. There is no damage or protection effect attached to it anywhere.

### 3.7 History of "hiding/hunkering behind trees"

**Shipped 2026-07-28, never reverted, still live at HEAD.**

| Date | SHA | Event |
|---|---|---|
| 2023-04-06 | `82f0b8eb` | `TakeCover` → `InfantryStates`; prone becomes automatic |
| 2024-02-13 | `1802191e` | **"Remove all ProneXXPercent DamageTypes"** — prone damage reduction killed |
| 2026-07-28 | `69e6ee86` | **"forest-concealment (item 26 ph2): tree-density-aware cover damage reduction"** — this *is* the hunkering-behind-trees work |
| 2026-07-28 | `fc9fe396` / `37cef097` | item 26 merged and recorded as shipped |
| 2026-07-28 | `243d8da0`, `25d599df` | item 21 cover *positioning* (the concealment re-seat in §2) |
| 2026-08-19 | `1492b225`, `b62ee52f`, `486575a8` | dead `TAKE_COVER` button documented, then removed |

`git log --all --grep='hunker'` returns **zero** commits. There is no revert of `DensityModifiesDamage`.
So the user is not misremembering a lost feature — he is failing to feel a live one whose ceiling is
20% and whose floor excludes single trees entirely. The thing that genuinely *was* lost is prone's
damage reduction, in 2024.

---

## 6. What would need a run to settle

Every claim above is read from source. These need play:

1. **Is the human stock ambush perceptible?** Two squads in Ambush 10 cells apart, an enemy walking
   into detection range of one. Assert the *other* squad opens fire within a few ticks of the first.
   Settles whether the coordination the code performs is visible to a player.
2. **Is the concealment gauge legible?** Screenshot a selected squad, then have it stop for 200+ ticks
   (dug-in) and re-shoot. The circle should shrink twice (prone at `!moving`, dug-in at 200 ticks).
3. **Does the `object-proximity` death matter in play?** It cannot be measured while dead — this is a
   code fact, not a play question. Listed only to note it needs no run.
