# Dispersion Cycle Design — "Spread to Move, Mass to Assault"

> **Cycle**: Experimental-AI Behavior — Cycle 2 (Dispersion Doctrine)
> **Mode**: EXPERIMENTAL — touches only `ModularBot@experimental` and its modules.
> **Non-goals**: no unit stat changes, no changes to Normal/Rush/Turtle/Stable bots,
> no new engine traits required (see §2d for the one optional exception).
> **Author date**: 2026-07-20

---

## 1. What Group Movement Looks Like Today — Evidence

### 1a. The offense order path

`PoiOffensiveBotModule.cs:386` issues a **single grouped AttackMove** for all axis units:

```csharp
// PoiOffensiveBotModule.cs CommitAndOrder(), line 386
bot.QueueOrder(new Order("AttackMove", null,
    Target.FromCell(world, axis.TargetCell), false,
    groupedActors: units));
```

All units on an axis get the **same target cell** (`axis.TargetCell`). `ModularBot.cs:81-112`
queues these via `world.IssueOrder()` → `OrderManager.IssueOrder()` → serialized into the
same order packet.

### 1b. CohesionMoveModifier DOES fire for bot orders

The dossier's claim that `CohesionMoveModifier` exists is **correct** — but the
`architecture.md` description of what it does is **wrong** (see §DISCOVERIES).

Trace confirming the modifier fires for bot orders:
- `Order.cs:400-401`: when `GroupedActors != null`, the serialized packet sets
  `OrderFields.Grouped` — GroupedActors IS included in the wire format.
- `UnitOrders.cs:397-413`: on receipt, `if (order.GroupedActors == null) ResolveOrder(...)
  else { var modifiers = world.WorldActor.TraitsImplementing<IModifyGroupOrder>(); foreach
  (var subject in order.GroupedActors) { individual = m.ModifyGroupOrder(...); ... } }`

So the bot's grouped AttackMove routes through `CohesionMoveModifier.ModifyGroupOrder`.

### 1c. What CohesionMoveModifier actually does

**The architecture.md description is wrong.** The correct description (from reading
`CohesionMoveModifier.cs:19-26` and the full implementation):

> Intent-aware cover-placement system. Classifies the click/target cell against
> `Map.DensityLayer` and dispatches to one of four formation strategies: **Open**
> (traditional box layout — fires when nearby density < `OpenDensityThreshold = 15`),
> **SpreadInside** (spread into cover cells), **EdgeLine** (line along cover gradient
> edge), **Approach** (boundary-anchored line for far-away cover clicks). Cohesion mode
> (`Tight`/`Loose`/`Spread`) controls only the **slot spacing**, not the strategy.

For AI moves to open-terrain objectives (typical on WW3MOD maps), **`Intent.Open` fires
almost always**, producing the classic box formation (`ComputeBoxSlots`).

Default cohesion for AI units: `AutoTarget.cs:120 — InitialCohesionAI = CohesionMode.Loose`.
Loose gives `LooseColSpacing = 2048` (2 cells) and `LooseRowSpacing = 1536` (1.5 cells)
(`CohesionMoveModifier.cs:36-41`).

For 8 units in Loose/Open (the standard case):
- Box: ~3 cols × 3 rows, cols=2c each, rows=1.5c each → approximately **6 cells wide × 4 cells deep**
- On a 128×128 cell map this is **a tight cluster — visually a death-ball on the minimap**

### 1d. Why the death-ball re-forms

Every `ReevaluateInterval = 100` ticks, if the target moved > `RepathThresholdCells = 3`
cells OR the axis unit set changed, `CommitAndOrder` issues a fresh AttackMove to the
**same single target cell** (`PoiOffensiveBotModule.cs:381-392`). This re-triggers
CohesionMoveModifier, which re-assigns the same compact box. Even if units spread
organically during AttackMove (fighting enemies en route), each re-eval squeezes them
back to a 6×4 cell box.

The legacy `SquadManagerBotModule` (Normal/Rush/Turtle) has the same pattern:
`GroundStates.cs:67` — identical single grouped AttackMove, same Loose cohesion.
Both Experimental and legacy issue a death-ball; they differ in *target selection*, not
*how units travel*.

---

## 2. Minimal Mechanism for Spread-to-Move / Mass-to-Assault

### 2a. Key enabler: the `SetCohesion` order is bot-callable

`AutoTarget.cs:434-435` handles the order:

```csharp
// AutoTarget.cs line 434-435
if (order.OrderString == "SetCohesion" && Info.EnableStances)
    SetCohesion(self, (CohesionMode)order.ExtraData);
```

The bot can issue:

```csharp
bot.QueueOrder(new Order("SetCohesion", unit, false) { ExtraData = (uint)CohesionMode.Spread });
```

`CohesionMoveModifier.ModifyGroupOrder` reads `subject.TraitOrDefault<AutoTarget>()?.CohesionValue`
at **order resolution time** (`CohesionMoveModifier.cs:625-626`). Because the bot's
queue drains in FIFO order — SetCohesion for each unit fires before the AttackMove —
all units have their new mode set when the grouped AttackMove is resolved.

Order-queue draining safety: `ModularBot.cs:101` issues `ceil(count / MinOrderQuotientPerTick)`
orders per tick (quotient = 5 by default). For 8 SetCohesion + 1 AttackMove = 9 orders,
the queue drains over ~5 ticks. SetCohesion fires before AttackMove in every case
because they are queued in that order. Other IBotTick modules push new orders to the
**back** of the queue, so they don't interleave between SetCohesion and AttackMove.

**Spread spacing**: `SpreadColSpacing = 3072` (3 cells col), `SpreadRowSpacing = 2560`
(2.5 cells row) → for 8 units: ~4-5 wide × 3-4 deep = **~12×9 cells. Visible on minimap.**

**Tight spacing**: `TightColSpacing = 1024` (1 cell col), `TightRowSpacing = 1024`
(1 cell row) → for 8 units: ~3-4 wide × 2 deep = **~4×3 cells. Dense assault cluster.**

### 2b. Proposed implementation: distance-gated stance switch in `CommitAndOrder`

**File**: `engine/OpenRA.Mods.Common/Traits/BotModules/PoiOffensiveBotModule.cs`
**Method**: `CommitAndOrder()` (line 368–393)
**Estimated change**: ~30 new lines + 2 new Info fields

Two new YAML-tunable constants on `PoiOffensiveBotModuleInfo`:

```yaml
# ai.yaml PoiOffensiveBotModule@experimental additions
AssaultRadiusCells: 15      # within this many cells of the target → Tight (mass to assault)
                             # outside this radius → Spread (spread to move)
CohesionSwitchEnabled: true # kill-switch to A/B without YAML rebuild
```

In `CommitAndOrder`, **before** issuing the grouped AttackMove, issue SetCohesion orders:

```csharp
void CommitAndOrder(IBot bot, Axis axis, int tick)
{
    // (Re)commit every unit to this axis (unchanged)
    if (goalGuard != null) { ... }

    // --- NEW: cohesion stance gating ---
    if (Info.CohesionSwitchEnabled && axis.Units.Count > 0)
    {
        // Centroid of current axis units
        long cx = 0, cy = 0;
        foreach (var u in axis.Units) { cx += u.Location.X; cy += u.Location.Y; }
        var centroid = new CPos((int)(cx / axis.Units.Count), (int)(cy / axis.Units.Count));
        var dist = (centroid - axis.TargetCell).Length; // Chebyshev

        var wantMode = dist > Info.AssaultRadiusCells
            ? CohesionMode.Spread    // en route: disperse
            : CohesionMode.Tight;    // close to objective: mass

        foreach (var u in axis.Units)
            bot.QueueOrder(new Order("SetCohesion", u, false)
                { ExtraData = (uint)wantMode });
    }
    // --- END NEW ---

    // Gate: only repath if needed (unchanged logic)
    var moved = !axis.HasOrdered || ...;
    if (!moved) return;

    var units = axis.Units.ToArray();
    bot.QueueOrder(new Order("AttackMove", null,
        Target.FromCell(world, axis.TargetCell), false,
        groupedActors: units));
    ...
}
```

**No other modules touched.** SetCohesion is issued per-unit-per-re-eval only when the
axis re-paths (the `!moved` early return already gates the expensive part). When the axis
is stationary and already ordered, no SetCohesion orders fire.

### 2c. Why this doesn't break captures or garrison escorts

Capture units (`tecn`, `e6`) and garrison-escort units are listed in
`ExcludeUnitTypes` on `PoiOffensiveBotModule@experimental` (`ai.yaml:177-179`).
They are never added to `axis.Units`. SetCohesion orders are only issued for units
**inside** `axis.Units`. `CaptureCoordinatorBotModule` and `PoiGarrisonBotModule`
are unaffected.

The `PoiGoalGuard` ledger is also unaffected — SetCohesion does not touch the ledger.

### 2d. Optional enhancement: sub-group column stagger (no new engine code)

The Spread spacing (3 cells) produces a ~12×9 box — better, but still a single compact
block. For the "dispersed columns" look, split the axis into 2 sub-groups with **offset
target cells** perpendicular to the approach direction:

```
Target cell offset = ±8 cells perpendicular to (centroid → axis.TargetCell) vector
Sub-group A targets: axis.TargetCell offset +8 cells left
Sub-group B targets: axis.TargetCell offset −8 cells right
```

Each sub-group gets its own grouped AttackMove. CohesionMoveModifier still fires and
applies Spread spacing within each sub-group. Result: two columns ~20 cells apart,
each internally spread — **looks like a real two-column advance**.

Implementation: split `axis.Units` into two halves sorted by ActorID (deterministic),
compute perp offset from the approach vector, issue two AttackMove orders. Change is
still inside `CommitAndOrder`. The `PoiGoalGuard` ledger is unchanged — both sub-groups
are committed to the same `"offense:<targetId>"` key.

**Risk**: two sub-groups with slightly different targets may not converge cleanly at
assault range. Mitigate by reverting to a single-target Tight AttackMove when dist ≤
`AssaultRadiusCells`. This collapses both columns to the same assault point.

**Defer if needed**: the simple Spread-mode approach in §2b is self-contained.
The sub-group stagger is an additive enhancement — stack it on top only if §2b
doesn't produce a visible enough effect on the minimap.

---

## 3. Observability

### 3a. What a watcher should SEE on the minimap

**Before** (current): a tight dot-cluster, ~6×4 cells, marching as one blob. When the
attack reaches the enemy, the blob hits the line at one point.

**After** (§2b alone): en route to a target 15+ cells away, axis units fan out to a
~12×9 cell spread formation visible as distinct dots. Within 15 cells of the objective
they converge into a ~4×3 tight cluster for the assault. The tighten-at-assault moment
is the "mass to assault" beat — visually satisfying.

**After** (§2b + 2d): two parallel columns, each 12 cells wide, approaching from
slightly different angles, converging at the assault point. Looks like a real two-
column advance.

### 3b. What the benchmark could MEASURE

**Per-axis spacing telemetry (cheap, no watcher changes):**

Add to the existing `CommitAndOrder` log line (`PoiOffensiveBotModule.cs:388-392`):

```csharp
// In CommitAndOrder, after computing wantMode:
int maxDist = 0;
foreach (var u in axis.Units)
{
    var d = (u.Location - centroid).Length; // Chebyshev
    if (d > maxDist) maxDist = d;
}
Log.Write("debug",
    $"[exp-offense] order ... wantMode={wantMode} clumpRadius={maxDist} distToTarget={dist}");
```

`clumpRadius` = max Chebyshev distance from the axis centroid to any member.
En-route baseline (current Loose): clumpRadius ≈ 3-4 cells.
After §2b (Spread): en-route clumpRadius ≈ 6-8 cells; assault clumpRadius ≈ 2-3 cells.
The difference is clear in the debug log and can be postprocessed from autotest output.

**Mean pairwise spacing (optional, requires watcher API):**

To compute mean pairwise Chebyshev distance in `BotVsBotMatchWatcher`, add a
public query to `PoiOffensiveBotModule`:

```csharp
// PoiOffensiveBotModule.cs: new public method
public IEnumerable<(string label, IReadOnlyList<Actor> units)> GetActiveAxes()
    => axes.Select(a => ($"offense:{a.TargetName}", (IReadOnlyList<Actor>)a.Units));
```

Then in the watcher's `ITick.Tick`, resolve the `PoiOffensiveBotModule` for each bot
player and compute mean pairwise spacing per axis. This is ≤N²/2 comparisons for N≤8
units per axis — negligible cost. Emit as a JSON field or watcher-log line.

For S2 benchmark scenarios, mean pairwise spacing > 5 cells en route and < 3 cells
at assault would confirm the doctrine is working.

---

## 4. Risk Assessment

### 4a. Spread units defeated in detail

**Risk**: Loose units encountered by an enemy patrol might be isolated and killed before
the rest of the axis reacts.

**Why it's bounded**:
- Spread mode = 3-cell column spacing. Most unit weapons have 5-10 cell range.
  At 3-cell spacing, units can still cover each other — no unit is truly isolated.
- `AttackMove` means each unit fires at enemies it encounters. Even a spread formation
  fights as a moving screen rather than a single target.
- The AssaultRadiusCells gate (15 cells from target) ensures massing BEFORE the enemy
  prepared position, not during the approach across empty ground.
- If an axis drops below `MinAxisSize = 3` due to en-route attrition, it is retired
  by `PruneAxes()`/`Reevaluate()` and units return to the free pool — no zombie axis.

**Not addressed in this cycle**: a threat-gated cohesion switch (read
`ThreatMapManager.GetThreat` along the approach route and revert to Tight in hot
corridors). Distance is a coarser but safe proxy for now. The architecture supports
adding the threat gate later — `poiMap` and `ThreatMapManager` are already resolved
in `Reevaluate`.

### 4b. Interaction with LayeredDefenceBotModule

`LayeredDefenceBotModule@experimental` runs a separate scan for reserve units to
push forward. Its `ExcludedActorTypes` (`ai.yaml:319`) does NOT overlap with offense
units — both modules may independently assign the same unit a stance. However,
LayeredDefence uses its own orders (`AttackMove`/`Move` per unit, not grouped), not
SetCohesion orders. A unit that gets a SetCohesion from the offense module retains
that cohesion for future orders. If LayeredDefence later issues an individual Move,
CohesionMoveModifier fires for that individual (n=1 → returns unchanged order). No
conflict.

### 4c. What this cycle explicitly does NOT do

- **No unit stat changes** (speed, armor, health).
- **No changes to Normal / Rush / Turtle / Stable bots**: gated behind the existing
  `PoiOffensiveBotModule@experimental` / `enable-ai-experimental` condition.
- **No new engine traits**: SetCohesion order, IModifyGroupOrder, and AttackMove all
  exist today; this cycle only adds ~30 lines to an existing bot module.
- **No PoiGoalGuard or PoiMap changes**.
- **No WORKSPACE/ai-bench/** changes (benchmark scaffolding owned by the scorer cycle).

---

## 5. Implementation Checklist

1. Add to `PoiOffensiveBotModuleInfo`:
   - `AssaultRadiusCells` (int, default 15)
   - `CohesionSwitchEnabled` (bool, default true)

2. Add cohesion-switching block to `CommitAndOrder()` in `PoiOffensiveBotModule.cs`,
   before the `!moved` early-return gate. Issue `SetCohesion(Spread)` when dist >
   AssaultRadiusCells, `SetCohesion(Tight)` when ≤. Only for axes that are re-pathing
   (cohesion switch is cheap but no need to re-issue when the order doesn't change).

3. Add `AssaultRadiusCells` and `CohesionSwitchEnabled` to `ai.yaml`
   `PoiOffensiveBotModule@experimental` (and later, `@stable` on promotion).

4. Add `clumpRadius` and `distToTarget` fields to the existing `[exp-offense] order`
   debug log line in `CommitAndOrder`.

5. Run one autotest (`make test`) for YAML validation. Run one `run-test.sh
   <dispersion-scenario>` for a quick smoke-test (no autonomous multi-test run without
   explicit go-ahead).

6. Optional (separate commit): sub-group column stagger (§2d). Wire only after §2b
   shows visible improvement.
