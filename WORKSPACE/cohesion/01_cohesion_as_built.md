# Cohesion as built — current implementation

> Reference doc. The implementation as of 2026-05-13 (commit `657d94ad`). Before designing improvements, we need an honest map of what already exists, what each piece is responsible for, and which assumptions are load-bearing vs incidental.
>
> Read this alongside `archive/260512_intent_aware_movement.md` (the original plan) and the live source files cited inline. Treat this as the snapshot of the machinery — `02_problem_statement.md` names the gaps; `03_design_directions.md` proposes paths forward.

---

## 1. The big picture, in one paragraph

Cohesion in WW3MOD is **a single rewrite hook between order issuance and order resolution**. When the player or a bot issues a grouped `Move`/`AttackMove` order, the engine's `UnitOrders.ProcessOrder` dispatches each per-subject suborder through the `IModifyGroupOrder` modifiers attached to the world actor. `CohesionMoveModifier` is that modifier today. It reads `Map.DensityLayer` (a per-cell `byte` cache populated from each map's `shadows.bin`, sourced from `Building.Density` on tree/wall actors), classifies the click into one of four intents — `Open`, `SpreadInside`, `EdgeLine`, `Approach` — and rewrites each suborder's target to a per-unit slot chosen by an intent-specific bidder. A separate per-actor trait, `CohesionSlotMemory`, remembers the assigned slot and walks the unit back to it if a passing actor bumps it out. There is no preview UI, no per-stance leash budget, no per-unit-type role differentiation, no voice cue. The "cover-aware" claim is currently the slot bidder; everything else from the original plan is deferred.

The system is intentionally narrow — one trait at one hook point. That makes it cheap to reason about and easy to disable (toggle `CohesionMoveModifier` off in `world.yaml` and grouped orders go through unmodified). The narrowness is also why it's *almost* working: the classifier + bidder logic is correct in isolation; the gap is everything outside that one trait.

---

## 2. Where cohesion lives in the order pipeline

File: `engine/OpenRA.Game/Network/UnitOrders.cs:393–416`

Every player or bot order eventually lands in `UnitOrders.ProcessOrder` after a round trip through the order manager and the server-echo loop. The relevant branch:

```csharp
default:
{
    if (world == null) break;

    if (order.GroupedActors == null)
        ResolveOrder(order, world, orderManager, clientId);
    else
    {
        var modifiers = world.WorldActor.TraitsImplementing<IModifyGroupOrder>().ToArray();
        foreach (var subject in order.GroupedActors)
        {
            var individual = Order.FromGroupedOrder(order, subject);
            foreach (var m in modifiers)
                individual = m.ModifyGroupOrder(individual, subject, order.GroupedActors);
            ResolveOrder(individual, world, orderManager, clientId);
        }
    }
}
```

Three things to notice:

1. **`order.GroupedActors` is the trigger.** A grouped order is any order constructed with `groupedActors != null` — produced by the `UnitOrderGenerator` for player right-clicks on multiple selected actors, and by `Test.GroupMove` from the Lua test harness. A single-actor order takes the non-grouped path and `ModifyGroupOrder` is never called. **Single-unit short-circuit is implicit in the engine; we don't have to enforce it in the modifier.**
2. **Per-subject dispatch is sequential.** Each grouped actor goes through every modifier, in trait-construction order, and produces a per-actor `Order`. There's no "look at the whole group, decide once, return a list" API — each call is per-actor. The modifier reconstructs the group decision from `allGroupedActors` and indexes into it via the subject's position. This means **any aggregation work the modifier does is recomputed N times per grouped order** (once per subject). For N ≤ 12, this is fine; we don't cache.
3. **The modifier can rewrite the target only.** `IModifyGroupOrder.ModifyGroupOrder` returns an `Order`. We use `Order.WithTarget(...)` to swap the destination cell; the rest of the order (string, subject, queued flag) passes through unchanged. We cannot, e.g., split one grouped order into two different intents — every unit gets one target.

The interface itself:

```csharp
// engine/OpenRA.Game/Traits/TraitsInterfaces.cs:158
public interface IModifyGroupOrder
{
    Order ModifyGroupOrder(Order individualOrder, Actor subject, Actor[] allGroupedActors);
}
```

Multiple modifiers can exist; the loop runs them in sequence and the output of one feeds the next. Today there is only `CohesionMoveModifier`.

---

## 3. The cover signal — `Map.DensityLayer`

File: `engine/OpenRA.Game/Map/Map.cs:252` (declaration), `:977` (population), `:473–509` (load).

`DensityLayer` is a `CellLayer<byte>` populated once per map load. Two paths:

- **From `shadows.bin`** — if the cached binary exists, `DensityLayer` is read straight out of it during `MapBinaryData.PostInit`.
- **Recomputed via `SetDensityLayer()`** — if the cache is missing or invalidated, the engine walks every `ActorDefinitions` entry, queries its `IDensityInfo.Density()`, and accumulates `byte` values per cell.

Today only one trait implements `IDensityInfo`:

- **`BuildingInfo`** (`engine/OpenRA.Mods.Common/Traits/Buildings/Building.cs:29, :141`). Returns the YAML `Density:` grid keyed by `CVec` offsets within the building footprint.

There is a commented-out `// , IDensityInfo` on `BlocksSightInfo` (`engine/OpenRA.Mods.Common/Traits/BlocksSight.cs:18`) suggesting an older design intended blocks-sight actors to contribute too. They don't, today. **The cover signal is strictly "what does the `Building` trait say about itself"**.

### What this means concretely

Every tree in `mods/ww3mod/rules/ingame/decoration.yaml` inherits `^Tree` which has `Building: Footprint: x  Dimensions: 1,1`. Then each `Tnn:` overrides the footprint and density. Typical values:

| Actor   | Footprint     | Density (per cell)         |
|---------|---------------|----------------------------|
| `T01..T13`, `T16`, `T17` | `__ x_` | `0,0, 10,0` — single trunk cell, density 10 |
| `T10`, `T11` | `__ xx`  | `0,0, 10,10` — two trunk cells, density 10 each |
| `T14`   | `___ _x_`     | `0,0,0, 0,10,0` — single trunk |
| `T15`   | `___ _x_`     | `0,0,0, 0,15,0` — denser single trunk |
| `TC03`  | `x=_ xx_`     | `10,5,0, 10,10,0` — composite cluster |
| `TC04`  | `x==_ xx=_ x___` | up to 10/cell across multiple cells |
| `ROCK1..7` | various    | `50` per rock cell — denser than trees |
| `TANKTRAP1/2` | `x`      | `20` |
| `T08`   | `x_`          | `5` — half-density |
| `T09`   | inherits ^Tree's `Footprint: x` — **no Density override → contributes nothing** |

So:

- A typical tree contributes **density 10 to exactly one cell** (the trunk).
- A 9×9 sample window around a clicked cell contains 81 cells; a *single* nearby tree puts `totalDensity = 10` in that window.
- Walls / sandbags / regular buildings can also contribute via `Building.Density`, but most don't ship with a density grid configured. The cover signal today is **almost entirely trees and rocks**.

### Regeneration

When the shadows-compute pipeline or density formula changes, every map's cached `shadows.bin` becomes stale. Two flows refresh it:

- `./utility.sh --regen-shadows ../mods/ww3mod/maps/<name>` — rewrites just `shadows.bin`.
- `./utility.sh --refresh-map ../mods/ww3mod/maps/<name>` — also rewrites `map.yaml` and `map.png`.

Saving a map in the in-game editor regenerates automatically. Currently-used maps that need refresh after a shadow change: `river-zeta-ww3`, `woodland-warfare-ww3`.

---

## 4. `CohesionMoveModifier` — the intent classifier and slot bidder

File: `engine/OpenRA.Mods.Common/Traits/CohesionMoveModifier.cs`

Registered on the world actor in `mods/ww3mod/rules/world.yaml:267` as a bare `CohesionMoveModifier:` block (no overrides — all defaults from `CohesionMoveModifierInfo` apply).

### 4.1 Entry — `ModifyGroupOrder`

The single public method (`:505`). High-level flow:

1. Bail if subject is null / dead / not in world.
2. Bail if order is not `Move` or `AttackMove`.
3. Count valid grouped actors. If `n ≤ 1`, return the order unchanged (single-unit case, even if other actors in the group are dying).
4. Sort `validActors` by `ActorID` so slot assignment is deterministic across all per-subject calls (each subject sees the same actor ordering and picks the same slot at its index).
5. Resolve subject's index `idx` in the sorted array.
6. Compute click cell, subject's `CohesionMode` (via `AutoTarget`), and per-mode spacing.
7. Classify intent (§4.2).
8. Compute group centroid for `Approach` reclassification and group-side biasing.
9. Reclassify `SpreadInside → Approach` if click is > `ApproachGroupDistanceCells` (default 12) chebyshev from the group's centroid.
10. Dispatch to the intent's slot bidder, get an array of N cells.
11. Diagnostic `Log.Write` on `idx == 0` (one line per grouped order).
12. Call `subject.TraitOrDefault<CohesionSlotMemory>()?.Assign(slots[idx], tick)` so the leash can recall the unit later.
13. Return the order with target replaced by `slots[idx]`.

The classifier and bidders are functionally pure on `(map, click, group, n, mode, mobile)`. They take no per-tick state — every grouped order recomputes from scratch. This is fine for typical N ≤ 12.

### 4.2 The classifier — `ClassifyIntent`

`:162`. Walks a 9×9 sample window (`IntentSampleRadius = 4`) around the click. Accumulates `totalDensity` and the density-weighted centroid offset `(dx, dy)` in cells.

```
if totalDensity < OpenDensityThreshold (default 15):
    return Open
else compute centroid offset (cx, cy)
    if cx² + cy² >= EdgeOffsetThresholdCellsSq (default 9):
        return EdgeLine
    else:
        return SpreadInside
```

Then in `ModifyGroupOrder`, if `intent == SpreadInside` and the group centroid is > `ApproachGroupDistanceCells` chebyshev from the click, intent is upgraded to `Approach`.

**Key thresholds** (all YAML-tunable on the `CohesionMoveModifier` trait):

| Field | Default | Meaning |
|-------|---------|---------|
| `IntentSampleRadius` | 4 | 9×9 sample window for the classifier |
| `OpenDensityThreshold` | 15 | Total density in window below which Open fires. 1.5 trunks. |
| `EdgeOffsetThresholdCellsSq` | 9 | Centroid offset² above which EdgeLine. 9 = offset > 3 cells. **Raised from 2 on 260513.** |
| `ApproachGroupDistanceCells` | 12 | SpreadInside reclassifies to Approach if group is further than this from click. |
| `SpreadSearchRadius` | 4 | 9×9 search window for SpreadInside slot candidates |
| `SpreadDistancePenalty` | 5 | Per-chebyshev penalty in SpreadInside slot scoring |
| `SpreadGroupPenalty` | 2 | Group-side bias for SpreadInside |
| `EdgeAdvancePercent` | 100 | EdgeLine anchor advance along gradient. 100% = anchor at centroid. |
| `LineSlotSearchRadius` | 2 | 5×5 per-slot search for EdgeLine/Approach. **New 260513.** |
| `LineSlotDistancePenalty` | 5 | Per-chebyshev penalty in line-slot scoring. **New 260513.** |
| `FilterByPathability` | true | Skip candidates the subject's `Mobile.CanStayInCell` rejects |
| `TightColSpacing` / `TightRowSpacing` | 1024 / 1024 | Box spacing in Tight cohesion mode |
| `LooseColSpacing` / `LooseRowSpacing` | 2048 / 1536 | Box spacing in Loose mode (default) |
| `SpreadColSpacing` / `SpreadRowSpacing` | 3072 / 2560 | Box spacing in Spread mode |

### 4.3 `Open` — legacy box formation

`:214` (`ComputeBoxSlots`). The pre-cover-aware behavior. Builds a `cols × rows` grid centered on the click, oriented along the centroid→click axis, with optional half-cell row-staggering. Rows extend backward from the click toward the group (`depthOffset = -row * rowSpacing`).

Fires only when `totalDensity == 0` in the 9×9 window — i.e., genuinely-open ground far from any tree or building.

### 4.4 `SpreadInside` — top-K cover cells around click

`:289` (`ComputeSpreadSlots`). For a click in or very near a cover patch:

1. Scan a `(2 · SpreadSearchRadius + 1)²` window around click.
2. For each passable cell with `CoverScore > 0` (sum of 8-neighbor density, self excluded), score `effective = CoverScore - chebyshev * SpreadDistancePenalty - groupChebyshev * SpreadGroupPenalty`.
3. Sort candidates by effective score (deterministic tiebreak on cell coords).
4. Greedy pick top N with chebyshev min-spacing derived from `colSpacing`.
5. Second pass: if N slots not found, relax spacing and pick more.
6. Last resort: pad with the click cell itself.

`groupCheb` is the chebyshev distance from each candidate cell to the group's centroid — it biases slot picks toward the squad's side of the cover, so units don't get assigned to far-side cells the pathfinder can't reach through dense trees.

Result: a scattered cluster of cells near trunks, with the squad's approach side preferred.

### 4.5 `EdgeLine` — perpendicular line at the cover edge

`:370` (`ComputeEdgeLineSlots`). For a click with detectable cover offset (centroid offset > 3 cells):

1. Compute the gradient unit vector from click toward cover centroid.
2. Advance along the gradient by `gradLen * EdgeAdvancePercent / 100` cells → anchor cell at the cover centroid.
3. Build N ideal positions along the perpendicular axis, spaced by `colSpacing`.
4. For each ideal position, call `PickCoverSlotNear` (the new helper) to find the best-CoverScore passable cell within a 5×5 window, respecting min-spacing against earlier picks.

The bidder is shared with Approach via `LayCoverAwareLine` (`:419`). The 5×5 neighborhood + `LineSlotDistancePenalty = 5` weighting means units can deviate up to 2 cells from the geometric line to find better cover.

### 4.6 `Approach` — march to cover at the destination

`:446` (`ComputeApproachSlots`). For a SpreadInside-classified click that's far from the group (chebyshev > `ApproachGroupDistanceCells`):

1. Compute the direction from group centroid to click.
2. **Walk *backward* from the click toward the group.** Find the first cell with `CoverScore > 0`. That cell is the cover patch closest to the destination.
3. If no cover is found along the path, boundary stays at the click — Approach degenerates to an open line at the destination (correct behavior for a long march into open ground).
4. Lay slots via `LayCoverAwareLine` perpendicular to the approach direction at the boundary cell.

PITFALL — the previous implementation walked *forward* from group toward click and stopped at the first cover cell. When the squad was already adjacent to cover (e.g., spawn-camped next to a tree cluster), step=1 tripped immediately and slots anchored right next to the starting position. Walking click→group reverses that bias. The PITFALL comment lives at `:441`.

### 4.7 The shared helper — `LayCoverAwareLine`

`:419`. Both `EdgeLine` and `Approach` use this. Lays N slots in a line perpendicular to a "forward" direction, anchored at a given cell. For each ideal line position, calls `PickCoverSlotNear` (`:447`) which:

1. Searches the `(2 · LineSlotSearchRadius + 1)²` window around ideal.
2. Filters by pathability.
3. Excludes candidates that violate min-spacing against earlier picks.
4. Scores each candidate `cover - chebyshev * LineSlotDistancePenalty`.
5. Returns the highest-scoring cell, or falls back to `NudgeToPassable` if none qualify.

`NudgeToPassable` (`:416`) walks up to 3 cells in a given direction looking for a passable cell — used as the last-ditch fallback.

---

## 5. `CohesionSlotMemory` — the leash

File: `engine/OpenRA.Mods.Common/Traits/CohesionSlotMemory.cs`

Attached to `^Combatant` in `mods/ww3mod/rules/defaults.yaml` so every infantry/vehicle gets one. Per-actor trait that:

1. Remembers the slot cell assigned by the most recent `ModifyGroupOrder.Assign()` call, with the tick it was assigned.
2. On `INotifyIdle.TickIdle(self)` — fired when the actor has no pending activity — walks the actor back to the slot if `self.Location != assignedSlot`, `mobile.CanEnterCell(assignedSlot)`, and the slot is not stale (`tick - lastAssignTick < ForgetAfterTicks`).
3. On `INotifyBlockingMove.OnNotifyBlockingMove(self, blocking)` — fired when another actor wants to push through us — also tries to return. `Mobile.cs` queues its `Nudge` activity first; we queue a `Move` second, so the unit nudges aside then returns.

YAML knobs:

| Field | Default | Meaning |
|-------|---------|---------|
| `ForgetAfterTicks` | 750 | 30s. After this, slot becomes stale and the unit stops trying to return. |
| `ReturnCooldownTicks` | 25 | 1s minimum between successive return attempts (prevents thrashing). |

**The leash is gentle.** It fires only on `TickIdle` or block notifications. If the unit is busy attacking, moving on its own initiative, or has a queued activity from elsewhere, the leash never fires. Per-stance forward-step budgets, ambush/hold modes, and "step out for a free shot then snap back" semantics from the original plan are **not implemented**.

---

## 6. `CohesionMode` — the Tight/Loose/Spread toggle

File: `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs` (CohesionMode enum at `:24`, dispatch at `:286–292`).

`AutoTarget` carries a `CohesionMode` field with three values:

- `Tight` — 1024 × 1024 wdist spacing (1 × 1 cells).
- `Loose` (default) — 2048 × 1536 wdist spacing (2 × 1.5 cells).
- `Spread` — 3072 × 2560 wdist spacing (3 × 2.5 cells).

The mode is set by a hotkey-driven `SetCohesion` order bound to Ctrl+Alt+1/2/3. `CohesionMoveModifier` reads the subject's mode at `:548` and routes to one of the per-mode `XColSpacing`/`XRowSpacing` Info fields.

Known gaps in the hotkey wiring (called out in the original plan, still pending):

- `SetCohesion` has **no `IResolveOrder` handler**. The mode is set locally on the trait but never synced over the network. This is harmless in single-player but means mode changes do not roundtrip through `OrderManager` and are not deterministic in replay or multiplayer.
- There is no `INotifyCohesionChanged` interface — stationary or individually-moving units don't recompute their cohesion-affected behavior when mode changes; only the next grouped order picks up the new mode.

These are foundational wiring fixes from the original plan's Phase 1 that were skipped in favor of jumping straight to the bidder. They don't affect single-player single-machine play but block any multiplayer feel work.

---

## 7. Group Scatter — Shift-G

File: `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/Hotkeys/GroupScatterHotkeyLogic.cs`

Bound to Shift-G. Issues a per-unit-randomized scatter Move to nearby empty cells. Independent of the cohesion modifier — it operates directly on the selected actors, not via the order pipeline. Also exposed to autotests as `Test.GroupScatter(actors)` for verification that scatter doesn't redistribute unit-specific waypoints (e.g., `EnterTransport`) across the selection.

There is a notable interaction risk: if a unit's scatter Move queues immediately after a grouped Move just resolved via `CohesionMoveModifier`, the scatter overrides the cohesion slot — which is correct behavior (user explicitly asked to scatter), but the `CohesionSlotMemory` leash will then try to walk the unit *back* to the pre-scatter slot when next idle. The leash has no awareness that "scatter happened" should clear the memory. In practice this is rare because scatter is followed quickly by another order.

---

## 8. The diagnostic surface

### Lua bindings (Test mode only)

File: `engine/OpenRA.Mods.Common/Scripting/Global/TestGlobal.cs`

- `Test.GroupMove(actors, cell, orderString?)` (`:264`) — issues a real grouped `Move`/`AttackMove` order through `world.IssueOrder` so it hits the IModifyGroupOrder path. Unlike `Actor.Move` which queues the activity directly and bypasses order resolution.
- `Test.GetDensity(cell)` (`:245`) — returns `DensityLayer[cell]` as an `int`. Useful for probing where trees are without parsing map.yaml.
- `Test.GetCohesionSlot(actor)` (`:232`) — returns the CPos the actor's `CohesionSlotMemory` is currently remembering.
- `Test.GroupScatter(actors)` (`:286`) — exercises the Shift-G scatter path.

### Diagnostic log line

`CohesionMoveModifier.cs:614` writes a `Log.Write("debug", ...)` line on the `idx == 0` per-grouped-order call:

```
[Cohesion] click=X,Y intent=<Open|SpreadInside|EdgeLine|Approach> n=N totalDensity=D grad=(gx,gy) groupCentroid=X,Y slots: c1 c2 c3 ...
```

Lands in `~/Library/Application Support/OpenRA/Logs/debug.log` (or platform equivalent). Spammy — fires every grouped Move/AttackMove in normal play. The plan is to strip it once feel is dialed; left in for now because the autotest-driven diagnosis loop benefits from it.

### Autotest scenarios

Under `tools/autotest/scenarios/`:

- `test-cohesion-cover-bid/` — click in a small cluster, expects all 4 units adjacent to a trunk. Smoke test that the bidder pulls units toward cover at all.
- `test-cohesion-cover-redirect/` — click 3 cells west of the trunk column (offset enough to fire EdgeLine even at the widened threshold). Discrimination test: passes only if the bidder actively redirects (box formation alone would leave units in open ground).
- `test-cohesion-real-cluster/` — replicates river-zeta's dense bucket at smaller scale. Probes density and issues two moves; asserts cluster density > 0.
- `test-cohesion-river-zeta-actual/` — loads the actual river-zeta `map.bin` and runs a 12-probe battery across the visible clusters and open ground. Pre-probes density at a grid and logs all results. The diagnostic scenario, not a hard-asserting test.
- `test-cohesion-slot-leash/` — verifies `CohesionSlotMemory.Assign` is called by the modifier.

---

## 9. What we just measured — river-zeta probe (2026-05-13, post-fix)

Twelve grouped moves with a 4-infantry squad on the real river-zeta map (98×82 cells, 1291 tree actors, 4 visible clusters per `map.png`). All probes reached `IModifyGroupOrder` — dispatch works.

| Click | Density (9×9) | Intent | Notes |
|-------|---------------|--------|-------|
| (25,35) center of A cluster | 530 | SpreadInside | Slots scatter in cover, click cell itself density 0 (passable, next to trunks). |
| (22,35) west-trunk cell | 320 | SpreadInside | Trunk at (22,35) — bidder picks adj cells. |
| (21,35) west edge | 260 | SpreadInside | At threshold 9 it stays SpreadInside (would have been EdgeLine at 2). |
| (19,35) 3 cells west of cluster | 130 | SpreadInside | Just inside the band. |
| (17,35) 5 cells west | 70 | EdgeLine | Offset (4,-1) magSq 17 — true cover-edge geometry. |
| (12,35) 10 cells west, open | 0 | Open | Real open ground, box formation. |
| (20,60) B cluster (south-left) | 155 | Approach | Group still at A, click 26 cells south — Approach to B. |
| (70,65) C cluster (south-right) | 215 | EdgeLine | Cover-edge geometry, anchored at C centroid. |
| (68,20) D cluster (north-right) | 210 | Approach | Slots land at D, not next to A. |
| (50,40) open center | 20 | EdgeLine | 2 trunks somewhere in window — marginal. |
| (80,75) far SE open | 255 | Approach | Slots land near (80,75). |
| (10,75) far SW open | 385 | Approach | Slots land near (10,75). |

Findings:

- **Open is rare.** Only `(12,35)` truly had zero density. The classifier successfully recognizes nearly all "near cover" clicks.
- **SpreadInside fires for clicks within ~3 cells of cluster centroid.** That's the wide-band intent — produces scattered cover-biased formation.
- **EdgeLine fires for clicks clearly off cover** (offset > 3 cells). With the per-slot CoverScore search, slots vary off the geometric line to land behind trunks.
- **Approach fires for far clicks** and now actually marches to the destination (post-fix). Pre-fix, slots all landed at the cover patch nearest the squad regardless of click distance.

The single-row probe across A is the cleanest comparison: clicking at the same y=35 row, moving the x position out from center, the intent ladders correctly through SpreadInside → SpreadInside → SpreadInside → EdgeLine → Open as the offset grows.

---

## 10. Architectural assumptions worth flagging

These aren't bugs — they're choices baked into the current shape. Naming them so we know what we'd be changing if we picked them up.

**A. The classifier samples a fixed 9×9 window.** Larger window = more cover detected = fewer Open classifications, but also slower discrimination between "near cover" and "in cover". 9×9 was chosen by the original plan; it has not been retuned.

**B. The cover signal is `Building.Density` only.** Walls, sandbags, regular buildings can contribute in principle but most ship without a `Density:` grid. There is no per-cell aggregation of multiple signal types — `Map.DensityLayer` is a single `byte` per cell, sum of contributions. The original plan's `ICoverSignal { float Sample(cell, world) }` modular API does not exist yet.

**C. Slot assignment is by sorted ActorID.** Each per-subject call indexes `slots[idx]` where `idx` is the subject's position in the ID-sorted `validActors` array. This is deterministic but ignores **starting position** — the unit closest to a given slot doesn't necessarily get it. A unit starting at the east edge of the cluster can be assigned the westernmost slot and walk through the entire cluster to reach it. For 4-unit squads in a small cluster this rarely shows; for larger squads or larger formations it produces awkward crossings.

**D. There is no per-unit-type role profile.** AT, MG, sniper, and rifle units are all candidates for any slot. The original plan's "AT prefers front-arc with LOS to vehicle approach, sniper prefers overwatch/depth" is unimplemented.

**E. The leash is `TickIdle`-driven.** A unit that's busy attacking from a slightly-displaced position doesn't get pulled back. A unit that's been queued an explicit Move by the player doesn't get pulled back. This is "soft" leash behavior; the original plan's per-stance forward-step budgets are unimplemented.

**F. The intent classifier doesn't recognize garrisonable buildings.** Clicking on a building that has `Garrison` traits available routes through whatever density that building contributes, but the intent is never "enter as occupants". Garrison logic is currently a separate UI path (right-click target on an enemy-held building, etc.).

**G. Waypoint chains (shift-click) get naive forward placement.** Each waypoint resolves independently through the modifier — there's no "plan from final formation back" rewriting.

**H. The bot uses the same dispatch.** Anything a player can issue, a bot can issue, and both flow through the same `IModifyGroupOrder` path. This is a feature — bots inherit cover-aware behavior for free. But it also means **AI tuning and player feel are coupled**: a knob that makes the bot behave better may make the player feel worse, and vice versa. There is no AI-specific override.

---

## 11. File pointers — where everything lives

| Concern | File | Line |
|---------|------|------|
| Order pipeline hook | `engine/OpenRA.Game/Network/UnitOrders.cs` | 393–416 |
| Interface declaration | `engine/OpenRA.Game/Traits/TraitsInterfaces.cs` | 158 |
| Modifier entry point | `engine/OpenRA.Mods.Common/Traits/CohesionMoveModifier.cs` | 505 |
| Classifier | same | 162 |
| Open / box bidder | same | 214 |
| SpreadInside bidder | same | 289 |
| EdgeLine bidder | same | 370 |
| Approach bidder | same | 446 |
| Shared line helper | same | 419 |
| Per-slot cover pick | same | 447 |
| Pathability nudge | same | 416 |
| Diagnostic log | same | 614 |
| Leash trait | `engine/OpenRA.Mods.Common/Traits/CohesionSlotMemory.cs` | — |
| Density signal storage | `engine/OpenRA.Game/Map/Map.cs` | 252 (decl), 977 (populate) |
| Density-bearing trait | `engine/OpenRA.Mods.Common/Traits/Buildings/Building.cs` | 141 |
| Tree YAML | `mods/ww3mod/rules/ingame/decoration.yaml` | 100+ |
| World registration | `mods/ww3mod/rules/world.yaml` | 267 |
| Combatant leash attach | `mods/ww3mod/rules/defaults.yaml` | grep `CohesionSlotMemory:` |
| Cohesion mode enum | `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs` | 24, 286–292 |
| Scatter hotkey | `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/Hotkeys/GroupScatterHotkeyLogic.cs` | — |
| Test API | `engine/OpenRA.Mods.Common/Scripting/Global/TestGlobal.cs` | 232, 245, 264, 286 |
| Autotest scenarios | `tools/autotest/scenarios/test-cohesion-*/` | — |
| Original plan | `WORKSPACE/cohesion/archive/260512_intent_aware_movement.md` | — |
