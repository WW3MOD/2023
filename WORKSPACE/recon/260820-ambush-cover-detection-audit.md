# Ambush, cover and detection — what the code actually does

**Date:** 2026-08-20 · **Branch:** `wt/ambush-audit` · **Base:** `main @ 4bb3fae9` (level with `origin/main`)
**Status:** read-only research. No code changed. **Game never launched** — launches are serialized
through the manager and none was requested for this pass. Every claim below is read from source, and
every number carries a `file:line`. Where a claim is derived rather than directly read, it says so
inline. Claims that can only be settled by playing are collected in §6; corrections this pass makes to
existing documents are collected in §7.

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
  (`^DetectableRangeCircles`, `infantry.yaml:750-800`).
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
  reaction anywhere (§1.6). The margin is computable and nothing reads it.
- **Take cover as a command.** There is no such control. Prone is fully automatic (it fires on
  `!moving`), and the dead `TAKE_COVER` button was removed on 2026-08-19 (`b62ee52f`, `486575a8`).
  Verified at HEAD: `TakeCover` appears nowhere in `mods/` and `TAKE_COVER` nowhere in the chrome (§3.1).
- **Benefit from being near cover objects for concealment.** The three largest concealment modifiers in
  the game — `@InCover1/2/3`, worth **+1/+2/+3** — are emitted only by **burnt** tree husks, at a radius
  of 0.18–0.63 cells centred inside a cell infantry **cannot stand on**. Zero of 23 husk types are
  reachable: they are dead (§1.4). This is the "modifier that can never fire" the audit was asked to
  find. Knock-on: infantry CV therefore tops out at 9, so the "invisible to standard vision" state
  cannot be reached at all.
- **Pay a detectability cost for firing an RPG, grenade launcher, or from a garrison.** The −2 firing
  penalty is filtered to the `primary` armament only (§1.4a b).
- **Dig in with a unit that has never moved.** A timer bug means the still-counter is only ever armed by
  a stop *transition*, so a map-placed soldier never reaches the dug-in tier — and a second bug can
  grant `dugin` to a unit that is running (§1.4a a).
- **Get damage reduction from going prone.** The five-tier `ProneDamageModifiers` block is still in the
  YAML, but the reduction only applies against warheads declaring a matching `DamageType`, and commit
  `1802191e` (2024-02-13) stripped those from every live weapon. Prone has given **0% damage
  reduction** for two and a half years (§3.2). It still shrinks the hitshape, which is real but is a
  hit-probability effect, not damage.
- **Know that veterancy is his biggest concealment lever.** Ranks 1–4 give **+1/+2/+3/+4** to required
  vision — more than prone and dug-in combined — and nothing says so (§1.3). A rank-4 soldier *running*
  is harder to spot than a rank-0 soldier dug in.
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
| `@Rank_1..4` | `rank-veteran == N` | **+1 / +2 / +3 / +4** | `^GainsExperience` at 100/200/400/800 XP (`defaults.yaml:198-206`), inherited by `^Infantry` (`infantry.yaml:4`) | **YES** |

⚠ **Veterancy is the largest live concealment modifier in the game — larger than prone and dug-in
combined** (`defaults.yaml:211-222`). Nothing in the UI says so. A rank-4 veteran rifleman that has
stood still for 200 ticks computes to `3 + 4 + 1 + 1 = 9`, i.e. **seen only from 4 cells** — effectively
invisible until it is on top of you. The same soldier at rank 0 is seen from 16c. This is a 4× swing in
detection radius driven by a stat the player never chose and is never shown.

### 1.4 ⚠ The dead modifiers — the cover ladder is granted, but geometrically unreachable

`object-proximity` **does have an emitter**, and an earlier draft of this document was wrong to say it
did not. The grantor is `ProximityExternalCondition@ObjectProximity` on `^TreeHusk` — a **burnt** tree —
with `Range: 384` (`husks.yaml:118-121`), plus 22 per-actor overrides tightening it to 182–640 WDist.
Living trees emit nothing. The `ExternalCondition` on `^DetectableInfantryStandard`
(`infantry.yaml:704-706`, `TotalCap: 3`) is the receiving seam.

**It nonetheless cannot fire, for a geometric reason:**

1. `Range: 384` is **0.375 cells**; the per-actor overrides go as low as 182 (0.18 cells).
2. The trigger point is the husk's `CenterPosition + Offset`, i.e. the centre of its own cell
   (`ProximityExternalCondition.cs:72-78,104`; `Building.cs:207-211,350`).
3. Containment is strict `<` on horizontal distance (`ActorMap.cs:143-144`).
4. **`^TreeHusk` carries `Building: Footprint: x` and no `Passable` trait** (`husks.yaml:91-121`), so it
   **blocks infantry** — unlike a *living* `^Tree`, which does carry `Passable: PassClasses: tree`
   (`decoration.yaml:12-14`) and is walkable. So no soldier can stand on the husk's cell.
5. Infantry occupy one of five quantised sub-cell offsets (`MapGrid.cs:117-125`; the mod does not
   override `SubCellOffsets`).

The nearest sub-cell an infantryman can legally occupy is therefore **244–771 WDist** from the trigger
point, against radii of **182–640** — and enumerating all 23 husk types, **zero are reachable**. The
radius is always smaller than the distance to the nearest standable cell.

*(This enumeration is arithmetic over the four code sites above, not measurement. The premises —
husk radii, husk impassability, the strict `<`, the sub-cell offsets — are each verified from code.)*

The one residual path is an accident: a soldier standing *under a live tree* when it burns down gets
the husk spawned on his own cell (`SpawnActorOnDeath`, `decoration.yaml:108-109`), and `Building`
does not eject occupants. From inside the cell, 12 of 23 husk types become reachable from some
sub-cell — until he takes one step.

**Consequence:** the three biggest concealment bonuses in the game — bigger than prone and dug-in
combined — never apply in practice. "Being next to cover" contributes zero to concealment, and the
thing that would grant it is a burnt tree you cannot stand next to closely enough.

**Knock-on:** with the ladder dead, infantry CV tops out at `3 + 1 (prone) + 1 (dugin) + 4 (rank 4) = 9`,
so the CV-10 "invisible to any standard-vision observer" state **cannot be reached by any infantry
configuration in shipped rules**.

This is the answer to the user's *"is any modifier unreachable in practice?"* — yes, three of the
eleven, and they are the largest.

### 1.4a ⚠ Three further defects in the live modifiers

**(a) `dugin` has two timer bugs** — `GrantConditionOnMovement.cs:44,52-61,68-80`:

- **A unit that has never moved never digs in.** `cooldown` initialises to `0` (`:44`) and `Tick`
  decrements it unconditionally while `dugin` is ungranted (`:54-56`), so it goes to `−1` on tick 1 and
  never equals 0 again. It is re-armed *only* in the stop branch (`:71`), which requires the unit to
  have been moving. **A map-placed or scenario-spawned soldier that is never ordered anywhere sits at
  CV 4 forever and never reaches CV 5.**
- **`dugin` can be granted while running.** If a unit stops (arming `cooldown = 200`) and moves again
  before the timer expires, `:73-79` grants `moving` but does not reset `cooldown`, and `Tick` has no
  "am I still?" guard. The countdown completes mid-stride and grants `+1`, held until the *next*
  stop→move transition. So `moving` (−1) and `dugin` (+1) are **not** mutually exclusive in practice.

`TimeToBeStill: 200` at `Timestep: 60` ms (`mod.yaml:380-383`) = **12.0 seconds**, not 8.

**(b) The −2 firing penalty applies to the primary armament only.** `GrantConditionOnAttackInfo.ArmamentNames`
defaults to `{ "primary" }` (`GrantConditionOnAttack.cs:25`, filtered at `:133-134`) and
`@Firing` (`infantry.yaml:722-726`) does not override it. So a soldier pays **no** detectability cost
for firing an RPG (`infantry.yaml:1154-1156`), a grenade launcher (`:1411-1413`), or the `garrisoned`
armament used from inside a building (`:1271-1274`, `:1577-1580`). Whether that is intended is a design
call; the code is unambiguous.

**(c) Prone and moving are not always exclusive.** `ProneCondition` contains `!moving`
(`infantry.yaml:294`), but `GrantCondition@HeavyDamageProne` (`infantry.yaml:988-990`) grants `prone`
while `heavy-damage-attained` — so a badly wounded soldier is prone *while running*, at CV 3 rather
than CV 2.

### 1.5 How the modifiers compose, and the resulting detection radii

**Composition is plain addition, then a clamp — nothing is multiplicative and nothing is mutually
exclusive.** `Detectable.ITick.Tick` and `IsVisibleInner` both do
`Util.ApplyAddativeModifiers(DetectableInfo.Vision, detectableModifiers)`, then clamp to
`[1, MapLayers.VisionLayers - 1]` = **[1, 10]** (`Modifiers/Detectable.cs:86-94,110-116`;
`VisionLayers = 11` at `MapLayers.cs:75`). Reveal is **strictly greater**:
`ResolvedVisibility[puv] > visibility` (`MapLayers.cs:579`).

Two consequences worth stating:

- **The lower clamp swallows over-exposure.** Base infantry `Vision: 3` (`infantry.yaml:96-97`) that is
  both moving (−1) and firing (−2) computes to 0, which clamps to 1 — the same as if it were only −2.
  So beyond −2 total, further exposure is free.
- **The upper clamp makes level 10 undetectable by standard vision**, since no band carries strength 11.

Derived ladder (level *N* is revealed by strength *N+1*, so its radius is the outer `Range` of the
`^StandardVision` band at strength *N+1*, `defaults.yaml:47-84`). This matches the shipped
`^DetectableRangeCircles` gauge radii exactly (`infantry.yaml:754,764,774,784`), which is a good
independent check on the derivation:

| Detectable level | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| Seen from | 28c | 25c | 22c | 19c | 16c | 13c | 10c | 7c | 4c | never |

**Realistic combinations for a standard rifleman (`Vision: 3`).** Note `prone` is granted by `!moving`,
so *every* stationary soldier is prone — "stopped" and "prone" are not independent choices:

| State | Modifiers | Level | Seen from |
|---|---|---|---|
| Running | moving −1 | 2 | **25c** |
| Running and firing | moving −1, firing −2 → −3, clamped | 1 | **28c** |
| Stopped (auto-prone) | prone +1 | 4 | **19c** |
| Stopped ≥200 ticks (prone + dug in) | +1 +1 | 5 | **16c** |
| Dug in, then fires | +1 +1 −2 | 3 | **22c** |
| Stopped, fires | +1 −2 | 2 | **25c** |
| Any of the above, "in cover" | `@InCover1/2/3` | **no change — dead (§1.4)** | — |
| Rank-4 veteran, stopped ≥200 ticks | +4 +1 +1 | 9 | **4c** |
| Rank-4 veteran, running | +4 −1 | 6 | **13c** |
| Map-placed, never ordered, standing still forever | prone +1 only (`dugin` bug A) | 4 | **19c** |
| Running while wounded to Heavy+ | moving −1, prone +1 | 3 | **22c** |
| Stopped, fires an **RPG** (not primary) | prone +1, **no firing penalty** | 4 | **19c** |
| Sniper `^SN` (`Vision: 5`), stopped ≥200 ticks | +1 +1 | 7 | **10c** |
| Any vehicle (`^Vehicle`, bare `Detectable:`) | none — no modifiers at all | 2 | **25c, permanently** |

So for a **rank-0** soldier the whole player-accessible span is **28c at worst to 16c at best**, and
firing costs him almost everything he gained by stopping. **Veterancy moves that span more than
anything the player can do deliberately** — a rank-4 soldier running (13c) is harder to see than a
rank-0 soldier dug in (16c). *(Derived from code and cross-checked against the shipped
gauge radii; **not measured in game** — the arithmetic is verified, the felt effect is not.)*

**Trees do not appear in this table, and that is the point.** Forest concealment is not a `Detectable`
modifier at all — it subtracts from the **observer's** strength along the sightline
(`modifiedStrength = strength - shadowModify`, `MapLayers.cs:355-378`, fed by `map.ShadowLayer`). The
two systems compose by acting on opposite sides of the same comparison. Depth is superlinear —
1 dense cell → 1, 4 → 6, 6 → 10 (`Map.ForestGroundShadow`, `Map.cs:1098,1102-1120`; knee at density 20,
`:1083`). A rifleman four cells deep in trees costs an observer 6 strength, which at 16c (strength 6)
takes the observer to 0 — i.e. **deep forest is worth far more than every stance and posture modifier
combined**, and unlike them it is invisible in the concealment gauge, which is viewer-independent.

### 1.6 There is no "almost spotted" reaction

The *quantity* exists and is cheap: detection is `observer strength > required level`, so
`required − strength` is a graded margin, and one step of margin is roughly three cells of approach
(the ladder is distance-quantised). `WORKSPACE/recon/260817-unit-indicators.md` §A.1 works this out in
detail and concludes *"nobody reads the margin, and nobody reacts to it."*

Verified at HEAD: no predictive detection logic exists. Grepped `engine/**/*.cs` for
`WillBeSpotted|AboutToBe|PredictDetect|ImminentDetect|SpotRisk|DetectionRisk` — **zero files**.
Detection is read only as the after-the-fact boolean `CanBeViewedByPlayer` (47 call sites).

**So the user's first scenario — "a soldier almost spotted stops running to stay hidden" — has no
implementation, in any stance, gated or not.**

### 1.7 What the player is shown

Two indicators ship and both are attached to base infantry (`defaults.yaml:811-827` via
`^UnitIndicators`; circles via `infantry.yaml:22`):

- **Spotted marker** — a red `!` while an enemy *the viewing player is aware of* can see this unit
  (`WithSpottedDecoration.cs:16-27`). Deliberately binary and deliberately asymmetric: an enemy that
  can see you but that you have not spotted does **not** light it, because that would be a wallhack.
- **Concealment gauge** — a grey circle at the range from which a standard observer first sees the
  unit, so stopping / going prone / digging in visibly shrinks it (`^DetectableRangeCircles`,
  `infantry.yaml:733-800`). Its own comment records that the tier→radius ladder is derived **from the
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

### 3.8 Two other detectability modifiers, for completeness

- **Aircraft on the ground**: `DetectableAddativeModifier@Ground`, `VisionModifier: 3` while
  `!airborne` (`aircraft.yaml:46-48`). Live; not infantry.
- **Special Forces have a quieter weapon**: `^SF` overrides the firing penalty to **−1** instead of −2
  (`infantry.yaml:1993-1995`) and carries `Detectable.Vision: 5` rather than 3 (`:1987`) — the one unit
  built around concealment. But `^SF` is `Prerequisites: ~disabled` (`infantry.yaml:1981`), i.e. **not
  buildable**, so no player can field it today.

---

## 4. The biggest gap between the vision and the code

The user's vision has two halves. They are in very different states.

**Half two — "detection of any one member makes them all open fire" — is BUILT AND REACHES HUMANS.**
`AmbushTickIdle`'s stock path plus `TriggerNearbyAmbushAllies` does exactly this, ungated, within 10
cells, and the damage-triggered variant works too (§2.2). It has been in the tree since `fea4617c`
(2026-03-17) and was never reverted. If the user cannot see it, the causes are most likely the three
caveats in §2.2 — it is **idle-only**, the volley is spread over a few ticks rather than simultaneous,
and the latch is terminal — not that it is missing.

**Half one — "behave so as to stay hidden; stop running when nearly spotted" — DOES NOT EXIST.**
There is no predictive detection anywhere (§1.6); detection is only ever read after the fact. The one
behaviour in the codebase that is close — Stage-2 halt-before-contact, which stops an advancing Ambush
unit *while its group is still unseen* — is **locked behind `enable-ambush-tactics`, which only
`LaneAmbushBotModule` grants, and only to the ≤4 units it posts itself** (§2.3).

**So the single biggest gap is a wiring gap, not a design gap.** The most sophisticated ambush
behaviour in the game — halt-before-contact plus a four-trigger spring table with hysteresis — is
finished, unit-tested, and reachable only by the AI. The four ambush autotests all grant the gate by
hand in Lua because it does not otherwise happen (§2.4). Every green ambush test validates a
configuration no player can reach.

A second, quieter gap: **the stance the user reached for is not the concealment lever, and the real
levers are unadvertised.** Concealment is driven by posture (automatic), veterancy (invisible), and
forest depth (invisible) — never by stance (§1.3, §1.5). Ambush changes *fire discipline*, not
detectability, which is exactly what the user said he wanted — but he also expected it to drive
hiding behaviour, and it does not.

---

## 5. What already exists that could be built on

Listed roughly by ratio of value to effort. **This section is orientation for a future implementation
pass; nothing here is a recommendation this audit is qualified to make without the user's call.**

1. **The gate is one condition.** Everything in §2.3 becomes player-facing the moment
   `enable-ambush-tactics` is granted to human-owned units. The grantor seam already exists and is
   already lint-clean — `ExternalCondition@ambushtactics` on `^AutoTarget` (`defaults.yaml:344-345`) —
   and there is an established idiom for exactly this grant shape next door:
   `GrantConditionOnHumanOwner@tacpos` (`defaults.yaml:44-45`), which is how
   `StancePositioningExecutor` was made default-ON for humans. **Caveat:** the gate was authored as
   default-off for benchmark byte-identity, so turning it on for humans is a behaviour change that
   needs the user's sign-off, and `AmbushMinSpringThreshold`/`AmbushHighSpringThreshold` have never
   been tuned against human play — their `[Desc]` says they are "meant to be tuned in autotest"
   (`AutoTarget.cs:104`).
2. **The "almost spotted" margin is already computed.** `required − observer strength` is a graded,
   distance-quantised quantity available on a synced trait; `260817-unit-indicators.md` §A.1 works out
   that one step ≈ three cells of approach. Nothing reads it. A "don't start moving while
   almost-spotted" behaviour is the cheap version of the user's scenario 1 and needs no new state.
3. **The concealment gauge already renders the right number.** `^DetectableRangeCircles`
   (`infantry.yaml:733-800`) draws the detection radius for a selected unit and its ladder is derived
   from code. Extending it to signal "almost spotted" is a render-side change — but see the PITFALL at
   `Detectable.cs:152`: driving visibility marks from a granted condition caused two shipped desyncs,
   which is why `WithSpottedDecoration` is deliberately render-only.
4. **Forest depth is already modelled, superlinearly, on both sides.** `Map.ForestGroundShadow` for
   concealment (`Map.cs:1102-1120`) and `ConcealmentScore` for order-time seating
   (`CohesionMoveModifier.cs:338-346`). The user's "dense forest either side of a road" picture is
   already expressible; what is missing is protection depth, not concealment depth.
5. **Edge-vs-interior is already computed and has exactly one consumer.** `TerrainAffordanceLayer`
   ships `CoverQuality`, `IsCoverEdge` and `OutwardFacing` per cell (`world.yaml:340`), read only by
   `StancePositioningExecutor` (§3.5). Any "hug the treeline facing the road" behaviour has its
   substrate already.
6. **Group detection is already a solved primitive.** `GroupDetectedBy`
   (`AttackMoveActivity.cs:201-230`) answers "has anyone in my ambush group been seen" and is the exact
   predicate the user's description needs.

**Two things that would need building from nothing:** an aim delay (there is none for infantry — §2.5),
and any reaction to imminent detection (§1.6).

---

## 6. What would need a run to settle

Every claim above is read from source. These need play:

1. **Is the human stock ambush perceptible?** Two squads in Ambush 10 cells apart, an enemy walking
   into detection range of one. Assert the *other* squad opens fire within a few ticks of the first.
   Settles whether the coordination the code performs is visible to a player.
2. **Is the concealment gauge legible?** Screenshot a selected squad, then have it stop for 200+ ticks
   (dug-in) and re-shoot. The circle should shrink twice (prone at `!moving`, dug-in at 200 ticks).
3. **Does the 20% forest damage reduction read as anything?** Two equal squads, one in deep trees
   (windowed density ≥50), one in the open, same attacker. 20% is inside normal combat variance, so
   this needs several seeds to say anything — and it is worth knowing whether the effect is
   *perceptible* before anyone spends effort tuning it.
4. **Is the rock/tree density asymmetry visible on real maps?** §3.3 is a rules fact; whether it
   matters depends on where maps actually place `ROCK*` actors relative to contested ground. That is a
   map-data question, answerable by inspection rather than a run, and this pass did not open it.

5. **Do the two just-merged capture scenarios actually fail?** §7.1 predicts both
   `test-visual-gauge-truth` and `test-visual-concealment-gauge` are calibrated one tier low. One run of
   either settles it, and it is worth doing before their output is trusted.

**Needs no run:** the `object-proximity` death (§1.4), the prone-damage death (§3.2), and the
`enable-ambush-tactics` gate (§2.3) are all code facts, settled by grep. They are listed here only so
nobody schedules a run to confirm them.

---

## 7. Corrections this pass makes to existing documents

Recorded so a curation pass can act; **no curated document was edited by this audit.**

| Document | Claim | Status |
|---|---|---|
| `WORKSPACE/recon/260819-infantry-visibility-stances.md` §0 #4 | concealment is "reported by nothing" | **outdated at HEAD** — the concealment gauge (`^DetectableRangeCircles`) and the spotted `!` marker both ship (§1.6) |
| `WORKSPACE/recon/260819-…` §0 #2 | Ambush "switches OFF the game's only automatic take-cover behaviour" | **half right** — it does opt out of `StancePositioningExecutor` (`:318`), but it opts *in* to the order-time concealment re-seat (`CohesionMoveModifier.cs:1079`). "Only" is wrong |
| `WORKSPACE/recon/260728-deploy-prone.md:13,52` | prone gives "per-damage-type damage reduction (down to 10–80%)" | **dead since `1802191e`, 2024-02-13** — no live weapon declares a matching `DamageType` (§3.2) |
| A prior pass's conclusion | `stance-ambush` has zero consumers **and** `StancePositioningExecutor.cs:313-314` explains why it deliberately does not consume it | **first half right, second half wrong** — the comment explains why the opt-out is written in C# rather than YAML; the executor *does* honour Ambush (§2.1) |
| `infantry.yaml:41` (shipped comment) | "a lone treeline barely helps" | **wrong** — one tree is density 10, below the 15 floor, so it helps exactly zero (§3.3) |
| `defaults.yaml:312-319` (shipped comment) | implies the gate is meaningfully live | **misleading in effect** — technically accurate (it *is* granted, to bot-posted units) but reads as though the machinery is generally on. No human unit ever carries it (§2.3) |
| An earlier draft of **this** document | `object-proximity` "is granted by nothing" | **wrong, and corrected in §1.4** — it *is* granted, by `ProximityExternalCondition` on tree husks (`husks.yaml:118-121`). The conclusion (dead) survives; the mechanism is geometric unreachability, not absence of a grantor |

### 7.1 ⚠ Two just-merged capture scenarios look mis-calibrated by one tier

Both scenarios from `88170a30` (2026-08-11, *"Five scripted capture scenarios"*) predict a **tier 3**
rifleman and derive their whole geometry from it:

- `tools/autotest/scenarios/test-visual-gauge-truth/test-visual-gauge-truth.lua:10-16` reasons *"Rifle is
  map-placed and never ordered anywhere, so he is not moving (no −1) and never gets `dugin` … He sits on
  tier 3"* and predicts a **22c** ring.
- `tools/autotest/scenarios/test-visual-concealment-gauge/test-visual-concealment-gauge.lua:21-27,57-59`
  asserts *"stopped ⇒ tier 3; dug in ⇒ tier 4"*.

**Both omit `prone`.** `ProneCondition` includes `!moving` (`infantry.yaml:294`), so a stationary
soldier is prone from spawn — `+1`. The correct tiers are **stopped 4** (ring 19c) and **dug in 5**
(ring 16c). Note the gauge-truth scenario reasons *correctly* about `dugin` bug A (§1.4a) and then
misses prone, so its "never ordered ⇒ no modifiers" conclusion is one modifier short.

`test-visual-concealment-gauge.lua:44-45` additionally derives 200 ticks as 8.0 s from 25 ticks/s; the
mod runs `Timestep: 60` ms (`mod.yaml:380-383`), so it is **12.0 s**. Its 13-second wait still clears
the timer, so that error is harmless in effect.

**Derived, not run.** This predicts both scenarios fail their own premise checks; a single run of
either would settle it, and is worth spending before anyone trusts their output.
