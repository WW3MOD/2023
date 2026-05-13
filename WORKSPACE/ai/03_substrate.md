# Substrate — the plumbing layer

> Speculative design doc. The brain's decision logic comes later (`04_brain.md`, TBD). This doc covers **the data and infrastructure that has to exist for the brain to make good decisions** — shared world layers, per-bot state stores, observability channels, persistence. The argument is: if the substrate is solid, decision-making is mostly bookkeeping; if the substrate has holes, no amount of clever decision code patches them.
>
> Treat each section as a proposal, not a binding spec. Push back. The goal is to converge on a substrate worth building before we start writing it.
>
> Read `01_default_ai_explained.md` for the existing machinery and `02_problem_statement.md` for the failure modes this substrate has to dissolve.

---

## 1. Why plumbing-first

Three reasons this order is right:

1. **We've already debugged the gaps.** Every v2 bug we hit (`MountedTransport` blocked on `IsIdle`, `TECN` order-overwriting, `LayeredDefence` stealing carriers, `CaptureCoordinator` ignoring contested-ness) maps directly to a missing substrate piece. We aren't speculating about future needs — we are filling holes we ran into in real playtests.
2. **Substrate is provably correct in isolation.** A `ResourceMap` either lists all capturable structures or it doesn't. A `GoalLedger` either preserves a goal across the next tick or it doesn't. These are testable. Decision logic is much harder to evaluate in isolation; with a working substrate, decision tests become unit tests on small functions.
3. **Substrate is reusable across brain variants.** If we end up wanting two brains (Normal vs Rush), they share the substrate. If we end up wanting to revive v2 for some specific scenario, the substrate still helps. Decision code is the volatile part; data is the durable part.

One legitimate counter-argument the user pre-empted: **don't build infrastructure we won't use.** The discipline this doc enforces: every substrate component below must trace to a specific failure in `02_problem_statement.md`. If it doesn't, drop it.

---

## 2. The layer cake

We'll build the substrate as five tiers, bottom-up. Each tier sits on the one below. The brain (`04_brain.md` someday) is the top layer that calls down into all of these.

```
┌─────────────────────────────────────────────────────────────────┐
│  Tier 4 — Brain (decisions; see 04_brain.md)                    │
├─────────────────────────────────────────────────────────────────┤
│  Tier 3 — Observability — debug logs, overlays, snapshots       │
├─────────────────────────────────────────────────────────────────┤
│  Tier 2 — Per-player state — goals, claims, sector budgets,     │
│           memory, production plan                               │
├─────────────────────────────────────────────────────────────────┤
│  Tier 1 — Shared perception — world traits read by anyone       │
│           (InfluenceMap, FrontlineOverlay, ResourceMap, …)      │
├─────────────────────────────────────────────────────────────────┤
│  Tier 0 — Engine primitives — Actor, Order, IBotTick, condition │
│           gates (unchanged; we just use them)                   │
└─────────────────────────────────────────────────────────────────┘
```

This is not a hard layering — Tier 2 will read Tier 1 every tick. But the dependency direction is strict: Tier N reads Tier N−1, never the reverse. No feedback loops.

---

## 3. Tier 0 — engine primitives (unchanged)

We use what OpenRA gives us. Listed here for completeness so we know what we don't have to invent.

- `Actor` / `World` / `Player` — the standard model.
- `Order` — the engine's command unit. The brain emits orders; the engine executes.
- `IBotTick` / `IBotRespondToAttack` — the hooks. Brain registers as one or both.
- `IBot.QueueOrder` — what the brain calls to dispatch.
- `ConditionalTrait` + `RequiresCondition` — gating mechanism. Use `enable-ai-v3` for the new path.
- `INotifyActorDisposing`, `INotifyOwnerChanged`, `INotifyCreated` — for keeping the unit roster in sync.
- Trait construction order — YAML declaration order determines tick order. We control this.

Nothing on this tier changes. No engine fork. We stay above the line.

---

## 4. Tier 1 — shared perception (world-scoped, read-only by consumers)

These are `World`-scoped traits. There's exactly one of each per match. Any module, any brain, any overlay can read them. They cache derived perception so that consumers don't re-scan the world.

### 4.1 `InfluenceMap` — already exists, keep

Friendly/enemy unit-density grid at configurable resolution. Friendly and enemy maps are separate `int[,]` layers. Refreshed on `Updated` per N ticks. `GetFriendlyInfluence(player)` returns the per-player friendly density (perspective-aware).

**Motivates which failure:** answers "where is our line / where is their line / where is contested" for every consumer. Without this, we had each module re-scanning. Already shipped, works.

### 4.2 `FrontlineOverlay` — already exists, keep

Derived from `InfluenceMap`: `GetFrontline(player)` returns a `bool[,]` of contested cells (per-player perspective). Has an in-game `/frontline` chat command for visual debugging.

**Motivates which failure:** lets the brain reason about "where on the line is thin" — the prerequisite for slotting reserves and choosing carrier drop-off cells. Already shipped.

### 4.3 `ResourceMap` — new

A per-tick refresh of the world's **capturable structures** with derived data. One entry per `oilb` / `bio` / `fcom` / `miss` / `hosp` / `logisticscenter`. Each entry has:

```
struct CapturableEntry {
  Actor       structure
  CPos        location
  Player      owner            // null if neutral
  int         incomeWeight     // from YAML config, see CaptureCoordinator.IncomeWeights
  bool        isContested      // ≥ N enemies within R cells
  int         nearbyEnemies    // exact count, for scoring
  int         distanceFromOwnSR
}
```

Read by: CaptureCoordinator-equivalent (target selection), brain (strategic priority), Garrison-equivalent (which capturables to defend).

**Motivates which failure:** today every capture-related module re-queries `world.Actors` filtered by capturable type — three+ scanners doing the same scan, each with slightly different filtering. A shared map removes the inconsistency.

### 4.4 `TerrainCache` — new

A precomputed-at-map-load index of static terrain features. Refreshed only when the map changes (which is "never" during a match).

- `coverCells: HashSet<CPos>` — cells with `Tree` / `Rough` / `Field` terrain type. Read by reserve assignment to pick covered slots.
- `chokepoints: List<CPos>` — narrow corridors flagged by adjacency analysis at load. Read by defensive placement and minelayer-equivalent (someday).
- `sectorIndex: CPos → SectorId` — see §4.6 below.

**Motivates which failure:** LayeredDefence does cover-snapping today by scanning a 6-cell radius around each slot. Precomputed, this is O(1). Same for chokepoints — we don't compute them at all today, so anything depending on chokepoints (defensive placement, ambush logic) doesn't work.

### 4.5 `ThreatMapManager` — already exists, partial; keep and extend

Already provides `ExplorationAge[,]` and per-cell threat values. Scout module uses it. Helicopter module uses it. We extend it (or wrap it) so the brain can read both.

**Motivates which failure:** Memory of "where was the enemy last seen" decays naturally via exploration age. Without this, the brain has no concept of "we haven't checked the north corridor in 30 seconds; send a scout".

### 4.6 `SectorMap` — new

Partition the playable area into N sectors. Most likely approach: **Voronoi diagram seeded from spawn points + named map features** (rivers, choke crossings if marked). Each sector has:

```
struct Sector {
  SectorId    id
  CPos        center
  HashSet<CPos> cells
  Player      controllingPlayer    // null if contested or neutral
  int         contestedScore       // function of InfluenceMap friendly+enemy in cells
  List<CapturableEntry> capturables
  List<CPos>  chokepointsToNeighbors
}
```

Read by: brain (for "commit 6 units to sector A this scan"), all assignment logic that wants hierarchical reasoning, overlays.

**Motivates which failure:** today, every "where should I send units" decision is per-cell (LayeredDefence) or per-target (CaptureCoordinator). There's no hierarchical layer in between. "Multi-axis play" (S-H in §5 of `02_problem_statement.md`) needs this — the brain budgets at the sector level, then expands to cell-level commitments within a sector.

**Open question — sectorization approach.** Voronoi from spawn points is the simplest defensible default and gives 2 sectors for 1v1, 4 for 2v2, etc. A regular grid (every K cells) is uniform but doesn't respect terrain. Hand-painted via map YAML is precise but maintenance-heavy. I'd start with Voronoi-from-spawns, allow YAML overrides per-map for tournament/featured maps.

---

## 5. Tier 2 — per-player AI state (owned by the brain)

These live on the player actor. One instance per bot. The brain reads and mutates them; nothing outside the brain touches them directly.

### 5.1 `UnitRoster` — new

A typed view of own units, refreshed every scan. The brain calls into this instead of `world.Actors.Where(...)` each time it wants "all my infantry".

```
class UnitRoster {
  IEnumerable<Actor> Infantry;      // anything with Passenger trait + InfantryClass
  IEnumerable<Actor> TECN;          // capturers
  IEnumerable<Actor> Carriers;      // Bradleys/BMP-2s/M113s — Cargo trait + carrier type
  IEnumerable<Actor> Tanks;         // Abrams/T90 — main battle armor
  IEnumerable<Actor> Helicopters;
  IEnumerable<Actor> SupplyTrucks;
  // … and so on, configurable by YAML

  // Convenient derived predicates:
  bool IsAtRally(Actor a);          // within R cells of own SR rally
  bool IsOnLine(Actor a);           // within R cells of any contested cell
  bool IsInReserve(Actor a);        // !IsOnLine and !IsAtRally — somewhere between
  Cargo? Cargo(Actor a);            // cached trait lookup
  int    AmmoState(Actor a);        // sum of pools, % of max
}
```

**Motivates which failure:** today every module does its own filter+predicate work, repeatedly. Centralizing this removes both inconsistency and the per-tick scan cost. Also makes the brain code readable.

### 5.2 `GoalLedger` — new — **the most important data structure**

A persistent per-unit goal record. **This is the primitive that replaces `IsIdle`.**

```
enum GoalType {
  HoldSector,        // sit on a contested cell, scored by LayeredDefence-style logic
  FerryInfantry,     // carrier: load at rally, drop at gap, return
  RideToFront,       // passenger: walk to carrier and EnterTransport
  CaptureStructure,  // TECN: walk to target, capture
  EscortCapturer,    // armed unit: follow a TECN, engage threats
  ScoutCorridor,     // light unit: explore a sector
  RetreatToSupply,   // ammo-out: walk to nearest TRUK/LC
  RearmAtBuilding,   // helicopter: land at airfield/helipad
  HoldFireAtAnchor,  // sit at a specific cell, no engagement (TECN parking)
}

class UnitGoal {
  GoalType   type
  Target     target                 // actor or cell
  CPos?      stagingCell             // optional intermediate (e.g. carrier loading bay)
  int        assignedAtTick
  int        expiresAtTick           // soft expiry — brain may renew or replace
  string     assignedBy              // module name (for debug)
  int        priority                // for arbitration
  object     payload                 // type-specific extras (e.g. passenger list for carrier)
}

class GoalLedger {
  Dictionary<Actor, UnitGoal> Goals;

  void Assign(Actor a, UnitGoal g);     // overrides any prior goal
  UnitGoal? Get(Actor a);
  bool HasActiveGoal(Actor a);          // unit has a Goal whose expiry > current tick
  void Clear(Actor a);
  IEnumerable<Actor> WithGoalType(GoalType t);
}
```

**Why this matters:** the brain decides on a UnitGoal at assignment time. From then on, the brain re-orders the unit **only if the goal changes**, not because the unit's engine activity dropped. `IsIdle` becomes irrelevant — replaced by `HasActiveGoal`.

The lifecycle: brain sets a goal → brain emits the engine order that fulfills it → engine activity completes / fails → brain detects fulfillment or expiry → brain renews or assigns a new goal. The dispatch logic owns the translation from "goal" to "order", and that's the only place orders come from.

**Motivates which failure:** the entire §3.1 of `02_problem_statement.md`. TECN order-overwriting goes away because once a TECN has goal `CaptureStructure → oilb-3`, the brain doesn't re-decide for that TECN until the goal expires or fulfills. AutoTarget activity flicker can't dethrone the goal.

### 5.3 `ClaimRegistry` — new (generalization of `BotBlackboard.ClaimUnit`)

Every unit assignment goes through this. A unit can be claimed by at most one source.

```
class ClaimRegistry {
  void Claim(Actor a, string claimant);    // returns false if already claimed differently
  void Release(Actor a);
  bool IsClaimedBy(Actor a, string claimant);
  string? Claimant(Actor a);
  IEnumerable<Actor> UnclaimedOfRoster(IEnumerable<Actor> roster);  // utility filter
}
```

This is essentially `BotBlackboard.unitClaims` promoted to a first-class type. In the v3 world, **the brain enforces single-claim by construction**: every assignment in the brain pipeline calls `ClaimRegistry.Claim`; double-claim is a debug-time assertion. The legacy modules' "I forgot to check the blackboard" failure mode goes away because the v3 brain is the only writer.

**Motivates which failure:** §3.2 (no central decider). With the brain as the single dispatcher, claims are an invariant, not a polite suggestion.

### 5.4 `SectorBudget` — new

For each sector (from `SectorMap`), how many units the brain wants there.

```
class SectorBudget {
  SectorId sector
  int      desiredInfantry
  int      desiredArmor
  int      desiredAA
  int      desiredArtillery
  // current vs desired delta is the "shift this many units" answer
}
```

Refreshed per high-cadence brain tick (every few hundred ticks, not every frame). Drives the brain's assignment logic: "sector A is under desired by 3 infantry; pull 3 reservists toward it".

**Motivates which failure:** §1.H (multi-axis play). Hierarchical budgeting at the sector level prevents the "drain everyone to one flashpoint" bug.

### 5.5 `ProductionPlan` — new

A queue of next-N units to call in.

```
class ProductionPlan {
  Queue<UnitOrderRequest> Queue;
  void Append(string unitType, string rationale);
  void Insert(int index, string unitType, string rationale);
  UnitOrderRequest? Peek();
  UnitOrderRequest? Pop();
}
```

Built by the brain based on (a) current roster vs sector budgets, (b) enemy composition observed in `ThreatMapManager` / `ResourceMap`, (c) supply availability. Consumed by an updated `UnitBuilderBotModule` or its replacement: instead of `UnitsToBuild` fractions, the queue drives.

**Motivates which failure:** §1.G (production responsive). Today `UnitBuilder` picks by static `UnitsToBuild` fractions — no demand-driven logic. A queue puts the brain in control.

### 5.6 `Memory` — new

Decaying records of enemy sightings and recent events.

```
class Memory {
  Dictionary<CPos, EnemySighting> RecentSightings;   // decays over time
  List<AttackEvent> RecentAttacks;                   // last N
  List<CaptureEvent> RecentCaptures;
  // …
}
```

Read by: brain (for "have we seen artillery recently?"), threat-response logic, scout dispatch. Decay handled in the perception phase of each brain tick.

**Motivates which failure:** §3.3 (no memory layer). Without this, "the AI remembers a Tunguska was sighted in north-east 20 seconds ago" is uncomputable.

---

## 6. Tier 3 — observability

The brain has to be debuggable. Today the AI is a black box — we infer behavior by watching units move. This is unacceptable for a complex brain; we'd be unable to debug it. Three observability surfaces:

### 6.1 Channel-tagged debug log

Every brain phase emits structured log lines on a tagged channel. We already use `[v2-transport]` and `[v2-capture]`; extend the pattern:

```
[v3-perceive]   tick=N  rosters: Inf=12 TECN=3 Cars=2 Tank=4 Heli=2  sectors-contested: 2/5
[v3-plan]       tick=N  sector-A: want Inf=8 have=4 delta=+4   sector-B: want Inf=4 have=6 delta=-2
[v3-assign]     tick=N  bradley-3 → goal=FerryInfantry target=cell(34,12) staging=cell(8,5)
[v3-dispatch]   tick=N  bradley-3 → Move(cell(8,5))   3× EnterTransport(bradley-3) ← e3-7, e3-8, e3-9
[v3-react]      tick=N  attack on cy-1 by bradley@(33,20); replan: shift 2 reservists to sector-A
```

Channels are individually toggleable (env var or YAML knob) so we can silence noise while debugging one phase.

### 6.2 In-game overlays

Existing: `/frontline` toggles the orange contested band.

Add:
- `/sectors` — color-code sectors and label sector IDs / controlling player
- `/goals` — render each own-unit's `Goal` as a colored line + label. Carrier ferry → orange-pink line (we already implemented this for `EnterTransport`); just extend.
- `/threat` — heatmap from `ThreatMapManager`
- `/budget` — show sector budget delta as numbers on each sector centroid
- `/claims` — visualize which units are claimed and by whom (color by claimant)

Each overlay is implementable as a world trait reading the relevant Tier 1/2 data. Cost is low; debuggability is enormous.

### 6.3 Pre/post-tick snapshots

Once per brain scan (every 100 ticks or so), emit a one-line summary:

```
[v3-snap] tick=2500 roster=I12/T3/C2/M4/H2 goals=10/12 unclaimed=2/19 sectors-thin=1/5 production-q=[abrams,e3,bradley]
```

This is the rolling state we can diff between scans to spot drift. Cheap and high-signal.

---

## 7. Persistence — what survives save/load

OpenRA's save/load system uses `IGameSaveTraitData`. We need to declare what survives.

**Persisted:**
- `GoalLedger.Goals` — units mid-goal should resume on load. Note: actor references must be re-resolved via `ActorID`.
- `SectorBudget` and `ProductionPlan` — strategic state.
- `Memory` — decayed records.
- `ClaimRegistry` — implied by Goals (rebuildable), but persisting is simpler.

**Not persisted (rebuilt on load):**
- `UnitRoster` — derived from world state.
- Tier 1 world traits' caches — `InfluenceMap`, `FrontlineOverlay`, `ResourceMap`, `TerrainCache` — recomputed.
- `SectorMap` — recomputed (or persisted if generation is expensive; benchmark first).

This matters because OpenRA replays need bit-exact reproducibility. Anything that's persisted has to round-trip cleanly.

---

## 8. Where the brain plugs in

This doc deliberately doesn't design the brain. But we name the interface points.

The brain is one or more new traits on the player actor implementing `IBotTick` (and optionally `IBotRespondToAttack`). It's `ConditionalTrait` gated on `enable-ai-v3`.

The brain trait reads:
- Tier 1 world traits via `world.WorldActor.TraitOrDefault<…>()`
- Tier 2 per-player state which it owns

The brain trait emits:
- Engine orders via `bot.QueueOrder(…)`
- Debug log lines via `Log.Write("debug", …)`
- In-game system lines via `TextNotificationsManager.AddSystemLine(…)` (sparingly)

**Whether the brain is one trait or several is a brain-design question, not a substrate question.** The substrate doesn't care; it's data the brain reads and writes. The brain doc (`04_brain.md`) decides whether `BotBrain.Tick` runs a sequential pipeline or whether multiple `IBotTick` traits collaborate via the goal ledger.

---

## 9. What we delete or shrink

If we ship v3 with the substrate above and a brain riding on top, several things in the codebase become redundant.

| Thing | Disposition |
|---|---|
| `BotBlackboard.PostTask` / `ClaimTask` / `GetOpenTasks` | Delete — `GoalLedger` subsumes |
| `BotBlackboard.PostIntel` / `GetIntel` | Replace with `Memory` |
| `BotBlackboard.ClaimUnit` | Replace with `ClaimRegistry` (same idea, typed) |
| Legacy `SquadManagerBotModule` | Keep under `enable-ai-legacy-only`, ignore for v3 |
| Legacy `CaptureManagerBotModule` | Keep under `enable-ai-legacy-only`, ignore for v3 |
| `LayeredDefenceBotModule` (v2) | Decide: revive as a brain method, or delete in favor of brain's slot assignment |
| `MountedTransportBotModule` (v2) | Same — likely becomes brain method, since goal-based assignment dissolves its IsIdle bug |
| `CaptureCoordinatorBotModule` (v2) | Same — its scoring logic is reusable, but assignment moves to brain |

The pattern: legacy modules stay; v2 modules either become brain methods or get retired. No engine code we wrote is wasted — the substrate carries it forward.

---

## 10. Open questions / push-back I want from the user

I am NOT certain on these. Each one is a defensible choice with a real trade-off.

1. **Sectorization approach.** Voronoi-from-spawn-points is simplest; hand-painted is precise. My default is Voronoi with YAML override per map. Worth your input — are there featured maps where the bot needs to know e.g. "the bridge is the chokepoint between A and B"?

2. **Per-actor vs per-cell-id goals.** The ledger above keys by `Actor`. But goals could also be cell-anchored ("hold cell X") with a list of assigned actors. Per-actor is simpler; per-cell may be more efficient for sector reasoning. I'd default to per-actor and revisit if performance bites.

3. **One brain trait or many?** A single `BotBrain` is conceptually clean but a 2000-line file. Splitting into phase traits (`BotPerceive` → `BotPlan` → `BotAssign` → `BotDispatch`) keeps each small but means the ordering invariant must be enforced via trait construction order. I'm leaning toward one trait with phase methods; happy to be wrong.

4. **How much of `SectorMap` is computed at map load vs. dynamic?** If sectors are seeded from spawn points and spawn points don't move, the partition is static — compute once. But "controlling player" and "contested score" are dynamic. Default: static partition, dynamic state. Probably uncontroversial.

5. **Should the v3 brain be allowed to look at the legacy modules' state?** E.g. read `BaseBuilderBotModule.initialBaseCenter`. My default is no — the brain owns its own state, doesn't peer-read. Costs a little duplication; gains clean boundaries.

6. **Build the substrate in big-bang or piece-by-piece?** Big-bang = land all of §4-§7 in one go before any brain code. Piece-by-piece = ship GoalLedger first, prove it on TECN capture, then add ResourceMap, then SectorMap, etc. I lean piece-by-piece — every piece earns its place by removing a known bug. Big-bang risks building unused infrastructure.

7. **What to do about `SupplyFollowerBotModule`?** It works today. It's an `IBotTick` that doesn't participate in the v3 substrate. Do we leave it alone (works, no value in changing), or migrate (consistency)? My default: leave it alone for now, migrate later if it bites.

8. **Threat-response cadence.** `IBotRespondToAttack` fires per damage event. Some attacks are 1000 events per second (machine gun fire). We need debouncing/aggregation, not "react to every bullet". Open question what the aggregation window is.

---

## 11. What this gets us, concretely

If we ship just §4-§5 (the data layer) and the simplest possible brain that does **only**: maintain `UnitRoster`, assign `CaptureStructure` goals to TECNs based on `ResourceMap`, dispatch one order per assigned goal — that alone:

- Kills the TECN order-overwriting bug (goal persists, no re-issue).
- Replaces the legacy random-target capture with income-weighted selection.
- Gives us `/goals` overlay to debug.
- Costs ~1 week of work (new types are small; the brain method is a single loop).

That's the minimum useful v3. Everything else (LayeredDefence-equivalent, MountedTransport-equivalent, multi-axis budgeting) is additive on top.

---

## 12. Files this would touch

For the substrate alone (Tier 1-3):

```
engine/OpenRA.Mods.Common/Traits/World/
  ResourceMap.cs                  (new)
  TerrainCache.cs                 (new)
  SectorMap.cs                    (new)
  InfluenceMap.cs                 (existing, keep)
  FrontlineOverlay.cs             (existing, keep)
  ThreatMapManager.cs             (existing, extend)

engine/OpenRA.Mods.Common/Traits/BotV3/      (new namespace)
  UnitRoster.cs                   (new)
  GoalLedger.cs                   (new)
  ClaimRegistry.cs                (new)
  SectorBudget.cs                 (new)
  ProductionPlan.cs               (new)
  Memory.cs                       (new)
  BotV3Diagnostics.cs             (new — overlays + log channels)

mods/ww3mod/rules/ai/
  ai-v3.yaml                      (new — v3 AI definitions, condition-gated)

mods/ww3mod/rules/world.yaml      (modified — register new world traits)
mods/ww3mod/chrome/                 (modified — overlay UI strings if needed)

engine/OpenRA.Test/OpenRA.Mods.Common/
  GoalLedgerTest.cs               (new)
  ResourceMapTest.cs              (new)
  SectorMapTest.cs                (new)
```

Roughly 10-15 new files, ~1500-2500 lines, plus modest changes to existing files. Sized for ~2-3 sessions of focused work, plus tests.

---

## 13. What I'd suggest for `04_*`

After this substrate doc, the next doc could go in three directions. My ranked preference, with rationale:

**Suggested file 4: `04_brain.md` — brain architecture and tick-pipeline design.**

Rationale: substrate without a brain consumer is half a system. Once we know what data we have, the next-most-pressing question is "what does the brain do with it?" — specifically the phase pipeline (perceive → plan → assign → dispatch → react), goal-to-order translation, replan triggers, conflict arbitration. Without this, the open questions in §10 above stay open.

**Alternative: `04_migration_plan.md`** — would specify build order, what ships first, condition gates, fallback strategy. Valuable but depends on knowing the brain shape (so we know what's "minimum useful brain").

**Alternative: `04_doctrine_knobs.md`** — would specify what's tunable per AI variant (Normal vs Rush vs Turtle). Valuable for tournament/balance, but premature until we have a brain to tune.

So: **04 = brain architecture**. Then 05 = migration plan, then 06 = doctrine knobs / personality, in that order.

But that's a suggestion — willing to flip if you'd rather lock migration in first to constrain the brain design.
