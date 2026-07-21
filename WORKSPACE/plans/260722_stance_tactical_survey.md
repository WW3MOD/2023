# Stance / Tactical-Layer Substrate Survey (READ-ONLY inventory)

**Date:** 2026-07-22
**Purpose:** Ground-truth inventory of what exists today before designing a
**strategic/tactical split** — bots issue strategic orders; a per-unit **STANCE**
layer autonomously handles micro (respond to threats, seek cover, position at a
treeline toward/away from known enemy direction), working **identically for
HUMAN-owned units** (toggleable). This doc is the "what exists" survey, NOT a
design. Every claim carries a `file:line`. `MISSING` = no substrate today.

Verified against `main` (working tree at survey time). Paths are relative to
repo root; engine = `engine/OpenRA.Mods.Common/Traits/` unless noted.

---

## Q1 — Stance inventory

**All four WW3MOD stance families live on ONE trait: `AutoTarget`.** Enums at
`AutoTarget.cs:20-26`. There is no separate engine `UnitStance{HoldFire,
ReturnFire, Defend, AttackAnything}` — WW3MOD **replaced** it. Correct the docs:
`architecture.md:247-278` is broadly right on families but the classic RA attack
stances are gone.

`EnableStances = true` (`AutoTarget.cs:94`) gates all four. `AutoTarget` may be
attached to **weaponless** units (supply trucks) purely to expose the UI
(`AutoTarget.cs:52-53`).

### 1. Fire-discipline — `UnitStance { HoldFire, Ambush, FireAtWill }`
- Enum `AutoTarget.cs:20`. Controls **WHEN** to fire.
- **Behavior:** HoldFire = never auto-fire (bail `AutoTarget.cs:667`); Ambush =
  pre-aim silently, open fire only when spotted/hit, coordinate allies within
  `AmbushCoordinationRadius = 10` cells (`AutoTarget.cs:84`, logic `:511-580`);
  FireAtWill = normal auto-engage.
- **Implemented by:** `AutoTarget` (order `"SetUnitStance"` `AutoTarget.cs:428`).
  Grants conditions `stance-fireatwill` / `stance-ambush` / `stance-holdfire`
  (`mods/ww3mod/rules/defaults.yaml:282-284`).
- **Set via:** UI buttons `STANCE_*` (`mods/ww3mod/chrome/ingame-player.yaml:326-387`),
  `StanceSelectorLogic.cs`; hotkey `StanceFireAtWill = A Alt`
  (`recommended_hotkeys_windows.yaml:154`); AI/YAML defaults below.
- **Default:** `InitialStance = InitialStanceAI = FireAtWill`
  (`AutoTarget.cs:70,73`).

### 2. Engagement — `EngagementStance { HoldPosition, Defensive, Hunt }`
- Enum `AutoTarget.cs:22`. Controls **WHERE** to position / whether to move to
  engage. This is the closest existing thing to a "tactical" toggle.
- **Behavior:** auto-move toward/after a target only when
  `engagementStance >= Hunt` (`AutoTarget.cs:468,506`). Defensive / HoldPosition
  never auto-advance (Defensive may still shuffle for LOS inside the Attack
  activity — see Q3). Grants `HoldPosition/Defensive/HuntCondition`
  (`AutoTarget.cs:103-113`).
- **Set via:** buttons `ENGAGEMENT_*` (`ingame-player.yaml:395-456`),
  `EngagementStanceSelectorLogic.cs`; hotkey `EngagementHunt = A Ctrl,Alt`
  (`recommended_hotkeys_windows.yaml:157`).
- **Default:** `InitialEngagementStance = InitialEngagementStanceAI = Defensive`
  (`AutoTarget.cs:98,101`).

### 3. Cohesion — `CohesionMode { Tight, Loose, Spread }`
- Enum `AutoTarget.cs:24`. Controls **HOW CLOSE** grouped units space on a move.
  Full deep-dive in Q2.
- Order `"SetCohesion"` (`AutoTarget.cs:434`); buttons `COHESION_*`
  (`ingame-player.yaml:464-525`), `CohesionSelectorLogic.cs`; hotkeys
  `CohesionTight = 1 Ctrl,Alt`, `CohesionSpread = 3 Ctrl,Alt`
  (`recommended_hotkeys_windows.yaml:160,162`).
- **Default:** `InitialCohesion = InitialCohesionAI = Loose`
  (`AutoTarget.cs:120,123`; conditions `defaults.yaml:285-286`).

### 4. Resupply — `ResupplyBehavior { Hold, Auto, Evacuate }`
- Enum `AutoTarget.cs:26`. Controls **WHAT to do when out of ammo** (see Q3).
- **DOC/CODE MISMATCH:** the `Desc` strings say "Hold, Seek and Rotate"
  (`AutoTarget.cs:125,129`) but the actual enum values are Hold/Auto/Evacuate.
  Desc text is stale.
- Order `"SetResupplyBehavior"` (`AutoTarget.cs:437`); buttons `RESUPPLY_*`
  (`ingame-player.yaml:533-583`), `ResupplyBehaviorSelectorLogic.cs`; hotkey
  `ResupplyEvacuate = 6 Ctrl,Alt`. Shown only for units with `AmmoPool`.
- **Default:** `InitialResupplyBehavior = InitialResupplyBehaviorAI = Auto`
  (`AutoTarget.cs:127,130`).

### Click-modifier meta-system (all four bars)
Per `CohesionSelectorLogic.cs:52-63` (mirrored in the other three selectors):
plain click = set on current selection; **Alt** = "do now" (immediate); **Ctrl**
= set per-unit default; **Ctrl+Alt** = persist per-**type** default via
`UnitDefaultsManager` → `unit-defaults.yaml` in `Platform.SupportDir`
(`UnitDefaultsManager.cs:40,54-76`). **Per-type defaults are applied at spawn
ONLY for human, non-bot players** (`AutoTarget.cs:358-388`) — this is the exact
seam where "toggleable stance behavior for human units" already plugs in.

### Which unit types
Combat units: infantry (`infantry.yaml:178,245,...`), aircraft
(`aircraft-russia.yaml:111-113,283,...`), vehicles (via `^AutoTarget*`
inherits). **Naval `AutoTarget` is entirely commented out**
(`naval.yaml:649-797`). No prone/suppression *stance* — prone is a condition,
not a toggle (see Q3).

---

## Q2 — Cohesion stance deep-dive (the "spread too much" bug)

Primary file: `CohesionMoveModifier.cs` — a **World trait**
(`[TraitLocation(SystemActors.World)]` `:18`) implementing `IModifyGroupOrder`.

### Trigger — ALWAYS-ON, not stance-gated
`ModifyGroupOrder` fires for order strings `"Move"` or `"AttackMove"` only
(`:588-590`) whenever `n > 1` valid grouped actors (`:600-601`). **CohesionMode
does NOT gate whether it fires — it only selects spacing** (`:626-627`). So
every grouped Move/AttackMove is reshaped, even in Tight/Loose. Fires for both
human grouped clicks and bot grouped orders (grouped-order plumbing
`UnitOrders.cs:397-413`).

### Four slot strategies — `enum Intent { Open, SpreadInside, EdgeLine, Approach }` (`:124`)
`ClassifyIntent` samples `Map.DensityLayer` in a `2*IntentSampleRadius+1 = 9x9`
window (`IntentSampleRadius = 4`, `:48`):
- **Open** — total density `< OpenDensityThreshold = 15` (`:52`) → legacy box
  formation `ComputeBoxSlots` (`:230-298`). **This is the typical open-terrain /
  AI case.**
- **EdgeLine** — centroid offset² `>= EdgeOffsetThresholdCellsSq = 9` (~3 cells,
  `:61`) → line perpendicular to the density gradient.
- **SpreadInside** — default → cluster into top-`CoverScore` cover cells
  (`ComputeSpreadSlots :305`).
- **Approach** — SpreadInside reclassified when group↔click Chebyshev
  `> ApproachGroupDistanceCells = 12` (`:93`, reclass `:648-655`).

### Spacing math — `GetSpacing` (`:126-143`)
| Mode  | ColSpacing (WDist) | RowSpacing (WDist) |
|-------|--------------------|--------------------|
| Tight | 1024 (1 cell)      | 1024               |
| Loose | 2048               | 1536               |
| Spread| **3072** (3 cells) | **2560** (2.5 cells)|

Values `:29-44`. Mode affects **only spacing, never strategy** (`:626-627`).

### WHY it over-spreads (root cause, confirmed)
In the **Open** box path (`ComputeBoxSlots :272-295`) slot offsets scale
**linearly and UNBOUNDED** with both spacing and unit count:
- `perpOffset = (2*col - (unitsInRow-1)) * colSpacing / 2` (`:279`)
- `depthOffset = -row * rowSpacing` (`:283`)
- Placed around `targetPos`; the **only** bound is `map.Clamp` (`:294`), which
  merely keeps slots on-map — there is **no maximum-footprint clamp**.

So a squad of N in Spread fans across `~cols * 3-cell` width by `rows * 2.5-cell`
depth (`cols = ceil(sqrt(2N))`, `:268`). Footprint grows with N and is ~3× the
Tight footprint. **This is the "spread way too much" the user reports** — Spread
spacing (3072/2560) × an unbounded box, most visible on Open terrain (the common
case). Contributing factor: mode is read from the **subject only**, so mixed
groups get inconsistent spacing (`:622-624`, flagged "acceptable for v1").

### How a unit gets OUT of spread — mostly MISSING
- **In the modifier: MISSING.** No per-order regroup / mass-to-assault.
- The only mass-to-assault lever is in the **AI**: `PoiOffensiveBotModule`
  uses `ApproachCohesion = Spread` while marching, switching to
  `AssaultCohesion = Tight` inside `AssaultRadiusCells = 15`
  (`PoiOffensiveBotModule.cs:99-106`, ~`:494-536`) — **but gated behind
  `CohesionSwitchEnabled = false` by default** (`:87,94`), enabled only on
  `@experimental`. **Human units NEVER auto-regroup.**
- `CohesionSlotMemory.cs` only leashes a *nudged* unit back to its assigned
  spread slot (`ForgetAfterTicks = 750` ≈ 30 s) — reinforces spread, doesn't
  undo it.

### Finished? Rough edges
- Temporary per-click diagnostic `Log.Write("debug", "[Cohesion]...")` still
  active (`:679-695`, "Strip again once we have an answer").
- Mixed-mode spacing inconsistency (`:622-624`).

### Benchmark credit (context, not code)
The dispersion doctrine (`CohesionSwitchEnabled: true`) scored **negative** causal
credit: median paired ON−OFF ≈ **−$1,500**, positive on only 5/10 seeds
(`WORKSPACE/ai-bench/REVIEW.md:107`, `LADDER.md:424-430`,
`reports/260721_rethink2.md:155-167`). Flagged for a confirming re-verify run
before flipping the toggle off on `@experimental`. It is `@experimental`-only and
was **never committed to `@stable`** (the auto-spread MOVE reshaping in
`CohesionMoveModifier` is separate and IS live for everyone — that is what the
user is seeing).

---

## Q3 — Existing autonomous micro (candidate "tactical layer" pieces)

Key arbitration axis noted per item: behaviors that route through **AutoTarget →
`AttackBase.AttackTarget`** are order-level and deconflicted by AutoTarget's
`IOverrideAutoTarget` chain; behaviors that **queue activities directly** bypass
that and will contend with player/other orders (see Q5).

1. **AutoTarget idle-scan + return-fire** — `AutoTarget.cs`. Idle scan every 3–8
   ticks (`MinimumScanTimeInterval=3`/`Maximum=8` `:133,136`; `TickIdle :493`),
   return-fire on `Damaged` (`:441`). Gated on fire stance (`>= Ambush` to
   retaliate `:443,495`; `> HoldFire` to fire `:667`) AND engagement stance
   (`allowMove` only if `>= Hunt` `:468,506`). Issues an **order-equivalent**
   (`ab.AttackTarget(..., AttackSource.AutoTarget, ...)` `:634`). Ambush pre-aim
   `AmbushTickIdle :511`. **This is the spine of any tactical layer.**
2. **Flee / retreat / panic — largely INERT / MISSING for the vision:**
   - `ScaredyCat.cs` — `Damaged`/`Attacking` roll panic → **queues
     `mobile.MoveTo` to a random adjacent cell** (`TickIdle :137`), always-on,
     activity-based. **Not confirmed applied to any WW3MOD actor** — verify YAML
     before treating as live.
   - `InfantryStates.cs:227` — `panicking` is **condition-driven, not
     self-triggered**; source condition `panicking` (`infantry.yaml:262`) has
     **no warhead granting it found → likely inert today.**
   - **No HP-threshold / superior-force flee exists.** AutoTarget itself notes
     "maybe we should automatically run away?" (`:472`) → **MISSING.**
3. **Suppression reactions — automatic, always-on, condition-driven.** The
   `suppressed` external condition (from suppression warheads,
   `GrantExternalConditionWarhead.cs`) drives: **prone** at `suppressed > 30`
   (`ProneCondition`, `infantry.yaml:252`; `ProneSpeedModifier 60`
   `InfantryStates.cs:182`, damage mods `:195`), plus speed/vision/burst/accuracy
   bands via `^SuppressionEffects` (`infantry.yaml:339+`). Not stance-gated. This
   is a *state* reaction, not a *positioning* one.
4. **Medic / repair auto-behavior** — `HealerAutoTarget.cs` (`IOverrideAutoTarget`
   `:69`, best-patient pick `FindBestTarget :157`, critical-first) deconflicted
   by `HealerClaimLayer.cs` (1:1 healer→patient). Feeds target back through
   AutoTarget (**order** path). **Stance-conditioned:** `DefensiveRange` caps
   search only when engagement == Defensive (`:142-154`). Building/vehicle repair
   is bot-module / player-triggered, not per-unit micro.
5. **Auto-reload / auto-resupply** — `AmmoPool.cs` `AutoRearmIfAllEmpty` (fires
   on last shot `:247` / on idle `:252`), **stance-gated on ResupplyBehavior**
   (`:178`): Auto → queues `SeekSupplyProvider` activity (`:290`); Hold → set
   `NeedsResupply`, wait (`:191`); Evacuate → queues `RotateToEdge` (`:203`).
   Aircraft excluded (`:173`). `QuickRearm.cs` is passive. All **activities.**
6. **SmartMove** — `SmartMove.cs` `IWrapMove` (`:49`) wraps every Move in
   `SmartMoveActivity` so units fire in self-defense while moving
   (`UnderFireDuration = 75` ticks `:75`, skips overkill targets). Always-on;
   an **activity WRAPPER** (composes with the move, doesn't compete).
7. **Damage/threat reactions (activity-based, always-on):**
   `HeliEmergencyLanding.cs` autorotate/crash on `DamageStateChanged`
   (`:167,223,239`); `VehicleCrew.cs:155` auto-ejects crew at Heavy;
   `GarrisonManager.cs` per-port suppression micro — duck at 30, recall at 60
   with hysteresis (`~:627-645`), idle recall (`~:668`), ambush-on-damage
   (`:1177,1299`).

**Takeaway:** the *reactive-fire* and *suppression-state* layers are solid and
partly stance-aware; the *positioning* layer the user wants (seek cover, flee,
face known threat) is **almost entirely MISSING** — only Hunt-chase and LOS
shuffle exist.

---

## Q4 — Substrate inventory for map layers

### InfluenceMap — EXISTS, global, **OMNISCIENT**, per-owner
`Traits/World/InfluenceMap.cs`. Per-cell military-value grid, `CellSize = 2`
tiles (`:32`), `UpdateInterval = 25` ticks (`:35`), disc `ContributionRadius = 3`
(`:38`). Sums sell-value of **armed** actors only (`:99-102`); one `int[,]` per
combatant player (`:58`) with friendly/enemy/frontline views (`:143,156,170`).
`Recompute()` iterates `world.Actors` with **no fog check** (`:92`) → sees all
actors regardless of who can see them. **A tactical layer that must not cheat
cannot use this as-is.**

Second, older grid: `ThreatMapManager.cs` — 8-cell blocks, military/economic
float grids, also omniscient (`:89`), but uniquely tracks `lastExploredTick[,]`
per cell (`:51,328`) — a coarse "when last observed" timer keyed on cells, not
enemy identity.

### PoiMap — EXISTS, global, omniscient, objective-scored
`Traits/World/PoiMap.cs`. Discovers capturable income structures + Supply Routes
(`:203-228`), scores per-perspective (`TryScore :411`) sampling threat FROM
InfluenceMap's enemy layer (`:481`) → inherits omniscience.
`DiscoveryInterval = 50` ticks (`:162`). This is a **strategic-objective** layer
(where to go), not tactical positioning.

### Last-known-enemy-position memory — EXISTS as `FrozenActorLayer`, but NOT as a queryable direction field
`OpenRA.Game/Traits/Player/FrozenActorLayer.cs`. A `FrozenActor` is a stale,
**per-player, fog-correct** snapshot of an actor last seen under fog: frozen
`CenterPosition`/`Footprint` at last sighting (`:37-38,107`), per-viewer owner
(`:41`), spatially partitioned (`:259`), visibility updated on shroud change
(`:165,276`). **This is the one true per-player last-seen store.** BUT nothing
aggregates frozen actors into an enemy **direction / density** field, and there
is no "enemy bearing from cell X" query. **Deriving "position relative to known
enemy direction" is MISSING — the raw last-seen data exists, the derivation does
not.**

### Terrain cover/concealment — substrate EXISTS (`Map.DensityLayer`), cover mechanic is real
`OpenRA.Game/Map/Map.cs:252` — `public CellLayer<byte> DensityLayer`, global,
per-cell byte, built from `IDensityInfo.Density()` (`TraitsInterfaces.cs:312`,
impl `Building.cs:141`); trees carry `Density:` values
(`mods/ww3mod/rules/ingame/decoration.yaml:104+`), summed per cell
(`Map.cs:977-1003`), persisted in `shadows.bin` (`:469-479`), maintained on
building add/remove (`UpdateDensityForBuilding :1145`).
- **Cover cells ARE queryable now:** `CohesionMoveModifier.CoverScore()`
  (`CohesionMoveModifier.cs:156`) reads DensityLayer to find passable cells
  adjacent to dense actors = the exact treeline primitive. But only the *cover*
  half (terrain), not "cover relative to threat."
- **LOS / shadows:** `ShadowLayer` (`Map.cs:253`) is a per-cell-pair LOS cache
  from DensityLayer (amortized recompute `UpdateShadowForCells :1020`,
  `FlushPendingShadowUpdates :1050`). `BlocksSight.cs` gives actors a
  sight-blocking density (`:23`), LOS via `BlockingActorsBetween` (`:74`).
  Weapon-level cover: `MissChancePerDensity` (`weapons-missiles.yaml:43`).
  **So a genuine cover/concealment mechanic exists.**
- **MISSING:** no prone/stance-in-cover damage or detection modifier (only
  weapon miss-through-density).

### Per-cell / per-tick precedent
`CellLayer<T>` (`OpenRA.Game/Map/CellLayer.cs`) is the standard container
(DefenseLayer, Ramp, CustomTerrain, resource/bridge/tunnel layers, DensityLayer,
ShadowLayer). Per-tick precedent: InfluenceMap/ThreatMapManager do staggered
N-tick grid recomputes over `world.Actors`; `FlushPendingShadowUpdates` does
budgeted per-tick per-cell LOS work. **A new per-cell tactical layer at 2-cell
granularity / ~25-tick cadence is a well-established, cheap pattern here.**

### Gaps for the user's vision
- Fused "cover relative to known enemy direction" field — **MISSING** (the two
  halves exist separately: DensityLayer/CoverScore + FrozenActorLayer, nothing
  joins them).
- Per-player **fog-respecting** threat/influence — **MISSING** (both existing
  grids are omniscient; FrozenActorLayer is the only fog-correct source to
  derive from).
- Per-unit autonomous cover-seeking — **MISSING** (CohesionMoveModifier uses
  cover cells only reactively, when interpreting a *player's* grouped click).

---

## Q5 — Order arbitration (the collision risk)

All AI orders flow through `bot.QueueOrder(new Order(...))` →
`UnitOrders.cs` → `Actor.ResolveOrder` → trait `IResolveOrder`. Every `Order`
carries a `queued` bool: **`queued:false` = interrupt** (`Actor.QueueActivity`
calls `CancelActivity()` first, `Actor.cs:381-387`); **`queued:true` = append.**
Grouped orders (`GroupedActors != null`) split per-subject and pass through
`IModifyGroupOrder` before resolve (`UnitOrders.cs:397-413`).

### Bot modules that issue per-unit orders today (collision sources)
- **`SquadManagerBotModule.cs`** — owns the classic squad; delegates to the squad
  FSM. Cadences `AttackForceInterval = 75`, `AssignRolesInterval = 50`,
  `RushInterval = 600` (`~:248-265`). `IgnoreGroundUnits` flag (`:40,302-309`)
  lets experimental YAML hand the whole ground pool to `PoiOffensiveBotModule`.
- **Squad FSM** — `Squads/Squad.cs`, `StateMachine.cs`, `States/*`. Mixes
  per-unit orders (`GroundStates.cs:75-95` attack/return, `:289-290` flee-move,
  `StateBase.cs:141-145` per-unit engagement stance) and **grouped** AttackMove
  (`GroundStates.cs:67,161,174`). **All squad orders are `queued:false`
  interrupts, re-issued every ~75 ticks.** This is the **primary collision
  source**: a stance layer's micro-moves get stomped every 75 t.
- **`HelicopterSquadBotModule.cs`** — per-unit Move/EnterTransport/Unload
  (`~:315,381-385`); claims units via `BotBlackboard.ClaimUnit` (`:162`).
- **`MountedTransportBotModule.cs`** — per-carrier FSM issues Stop/EnterTransport/
  Move/Unload/CaptureActor (`~:175,298,318,355,460-464`). **Exposes
  `IsPassengerReserved(actor)` (`:121-127`) as an explicit deconfliction API.**
- **`CaptureCoordinatorBotModule.cs`** — per-unit `CaptureActor` (`:535`,
  `queued:true`), grouped escort/defense AttackMove (`:676,733`).
- **`PoiOffensiveBotModule.cs`** — grouped AttackMove per axis (`:541`), per-unit
  `SetCohesion` (`:535`), `ReevaluateInterval = 100` (`~:167-177`).
- **No standalone "evac module"** — evac is inline `RotateToEdge` / out-of-ammo
  skipping in `PoiOffensiveBotModule.cs:84-89,419-437` and `LayeredDefenceBotModule`.

### Precedence idioms (what a stance layer MUST adopt)
1. **Idle-gate (`IsIdle`)** — the canonical human-precedence guarantee. AutoTarget
   auto-repositions only from `TickIdle` (`AutoTarget.cs:493-509`); `Mobile.Nudge`
   only fires `if (self.IsIdle)` (`Mobile.cs:934`); bot recruiters skip
   non-idle units (`CaptureCoordinatorBotModule.cs:756`,
   `LayeredDefenceBotModule.cs:256`). **A player's `queued:false` Move puts the
   unit in a Move activity → `IsIdle` false → automatic behaviors skip it.**
2. **The `queued` flag** — interrupt vs append (`Actor.cs:381-387`,
   `Mobile.cs:986-1013`). Player Move is `queued:false` unless Shift
   (`ForceQueue`, `Mobile.cs:1137`).
3. **Explicit commitment ledgers (WW3MOD-specific, strongest):**
   `PoiGoalGuard.Ledger.IsCommitted(actor, tick)` — a committed unit is invisible
   to every other module (`CaptureCoordinatorBotModule.cs:762`,
   `PoiOffensiveBotModule.cs:406`); plus `MountedTransport.IsPassengerReserved`
   and `BotBlackboard.ClaimUnit`. **The idle-gate alone is INSUFFICIENT** because
   squad orders re-fire every ~75 t regardless of idleness — a stance layer
   should register in a ledger so offense/capture/transport skip stance-owned
   units.

---

## Q6 — Sync / determinism constraints

### Two RNGs (`World.cs:50-51`)
- **`SharedRandom`** — network-synced lockstep RNG, seeded from lobby
  `RandomSeed` (`World.cs:217`), **hashed into the sync hash**
  (`World.cs:543 ret += SharedRandom.Last`). Combat rolls + AutoTarget scan
  intervals use it (`AutoTarget.cs:607`). Every consumption must be identical
  across clients.
- **`LocalRandom`** — in this fork, seeded *deterministically* from the same
  lobby seed via a decorrelated LCG transform (`World.cs:219-228`), drives **all
  bot decisions** (call-in, squad/scan timing, target choice — e.g.
  `SquadManagerBotModule.cs:190-195`, `PoiOffensiveBotModule.cs:164`). **NOT in
  the sync hash** — deterministic only because every client's bots run
  identically off the same seed. **Any client-divergent read silently desyncs
  with no assert.** For anything feeding a synced value, prefer `SharedRandom`
  or guarantee the `LocalRandom` read path is byte-identical on all clients.
  (Background: `architecture.md:311-313` — bot decisions are seed-reproducible
  since main @ 2d3c8fe0.)

### Prior art: per-player state kept OUT of synced sim
- **`RenderPlayer`** (`World.cs:89-114`) is render-only per-player vision; gates
  fog/shroud **display** only, set from `LocalPlayer` (`:100-104,137`) →
  **local-client state, must NEVER be read in synced sim.** Enters the sync hash
  *only* under `UnlockedRenderPlayer` (`World.cs:545-548`) — the deliberate
  observer exception.
- **`MapLayers`** (`Traits/Player/MapLayers.cs:18`) is the per-player shroud/fog
  trait on the player actor — per-player but **part of the synced sim** (fog is a
  deterministic lobby option). **This is the model a sighting-memory layer should
  follow:** built from synced inputs, updated identically on all clients.
- **`FrozenActorLayer`** updates track shroud (synced) — the safe source for
  last-seen memory.

### Pitfalls a design must respect
- **Reading local/render state in sim** — `World.cs:165`
  `Sync.AssertUnsynced("The current order generator may not be changed from
  synced code")`; `RenderPlayer`/`LocalPlayer` are the trap.
- **Iteration-order nondeterminism** — bot code already tie-breaks by ActorID
  (`PoiOffensiveBotModule.cs:267,304`; capture ledger keys `:573`). Any
  HashSet/Dictionary feeding a synced decision must be ordered first.
- **Floating point** — positions are integer `WDist`/`WPos`/`CPos`; offense math
  is deliberately integer-only (`PoiOffenseMath`, ~`:579-699`, `long` sums +
  floor division). `float` appears only in non-synced ratios
  (`GroundStates.cs:321`) — a risk if such a value ever gated a synced branch.
- **Wall-clock** — `LocalRandom` falls back to the wall-clock ctor only when
  `RandomSeed == 0` (`World.cs:226-228`), never hit in real matches. No
  `DateTime.Now`/`TickCount` in surveyed bot sim paths.

---

## HARD CONSTRAINTS any design must respect (the half-page)

1. **One host trait, one plug point.** All four stance families already live on
   `AutoTarget` (`AutoTarget.cs:20-26`), and per-type human defaults already
   apply at spawn for non-bot players (`:358-388`). A new "tactical stance" should
   extend this trait/enum family and reuse the click-modifier + `UnitDefaultsManager`
   plumbing, NOT invent a parallel UI. This is exactly where "toggleable for
   humans" is wired.

2. **Respect the order/activity split.** Order-level behaviors (AutoTarget,
   HealerAutoTarget) are deconflicted by the `IOverrideAutoTarget` chain;
   activity-queuing behaviors (SeekSupplyProvider, RotateToEdge, panic-nudge,
   heli autorotate, garrison recall) bypass it and already contend. A new layer
   should route through AutoTarget's attack path where possible, and where it must
   move a unit, do so as an activity that respects the idle-gate.

3. **Idle-gate is necessary but NOT sufficient for humans.** `IsIdle` (used by
   AutoTarget `:493`, `Mobile.Nudge` `:934`) protects a player's active order.
   But bot squad orders re-fire `queued:false` every ~75 t
   (`SquadManagerBotModule` `AttackForceInterval`), so a tactical layer sharing
   units with the squad FSM must also register in a **commitment ledger**
   (`PoiGoalGuard.Ledger`, `MountedTransport.IsPassengerReserved`,
   `BotBlackboard.ClaimUnit`) or it will be stomped.

4. **Determinism is unguarded on the bot path.** `LocalRandom` is NOT in the sync
   hash (`World.cs:543`); a client-divergent read desyncs silently. Any tactical
   decision that feeds a synced value must use `SharedRandom` or be provably
   byte-identical across clients. Never read `RenderPlayer`/`LocalPlayer` in sim.
   Order HashSet/Dictionary iteration by ActorID before it gates a decision. Keep
   positions integer.

5. **Cover exists; "cover relative to threat" and fog-correct threat do NOT.**
   `Map.DensityLayer` + `CohesionMoveModifier.CoverScore` give queryable cover
   cells; `FrozenActorLayer` gives per-player fog-correct last-seen enemy
   snapshots. But there is **no** enemy-direction/density field and **no**
   fog-respecting influence map (both `InfluenceMap` and `ThreatMapManager` are
   omniscient). The "position at a treeline toward/away from known enemy" feature
   requires building a new **per-player, fog-derived** layer on top of
   FrozenActorLayer — model it on `MapLayers` (synced, per-player), use the
   `CellLayer<T>` + staggered N-tick recompute precedent for perf.

6. **Fix, don't fork, the spread bug.** The auto-spread that over-spreads is the
   always-on box path in `CohesionMoveModifier` (`ComputeBoxSlots :272-295`,
   Spread spacing 3072/2560, unbounded footprint, only `map.Clamp` bounds it). It
   is live for humans AND bots and has **no regroup**. Any tactical redesign must
   decide whether spacing stays per-order (current) or becomes a stance the unit
   can exit — today there is no mass-to-assault path for human units at all
   (the AI one is `@experimental`, gated `CohesionSwitchEnabled=false`, and
   benchmark-negative).

7. **Naval has no AutoTarget** (`naval.yaml:649-797` commented) — any stance layer
   assuming universal `AutoTarget` presence must handle its absence.
