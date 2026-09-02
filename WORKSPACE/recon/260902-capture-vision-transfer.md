# Stale vision on captured buildings — recon and costed proposal

**Date:** 2026-09-02 · **Branch:** `wt/vision-transfer` · **Base:** `main @ cd782ae9`
**Scope:** research only, no engine code written. The game was never launched.

## Executive summary

The bug is real, and it is **broader than the brief states** — the brief names two capture sites;
there are **three**, plus a fourth, higher-traffic path (garrison ownership flips) that the brief
never mentions and which was the *original* user of the in-place path.

The framing of the task — "the obvious repair reintroduces a hitch two authors judged unacceptable" —
**does not survive contact with the code.** It rests on conflating two different repairs:

- **Reverting to `ChangeOwnerSync`** would reintroduce the hitch. Nobody should do this.
- **Adding `INotifyOwnerChanged` to `AffectsMapLayer`** does *not* call `World.Remove`/`Add` at all.
  It recomputes only the captured actor's own vision sources. It cannot reintroduce the freeze,
  because the freeze — whatever its true size — lives in `World.Remove`/`Add`, which the targeted
  fix never touches.

So the dilemma dissolves, but **not** for the reason Q2 anticipated (a stale 0.5s figure). It
dissolves because the cheap fix and the expensive path are not the same fix. The 0.5s number is
indeed unmeasured and its stated mechanism is wrong (§2), but that turns out not to matter.

There *is* a genuine trap, and it is not the one in the brief: a naive `INotifyOwnerChanged`
implementation **crashes the game with an unhandled exception** on every non-building owner change.
Details and the four-line guard that avoids it in §3.

**Recommendation: Option A**, ~15 lines, one afternoon including a RED-then-GREEN autotest.

---

## Q1 — Is the mechanism real, and is the consequence real?

**Yes to both.** Every claim below was read in source, not summarised from the brief.

### The mechanism

`AffectsMapLayer` declares its interfaces at `engine/OpenRA.Mods.Common/Traits/AffectsMapLayer.cs:42-43`:

```
ConditionalTrait<AffectsMapLayerInfo>, IAffectsMapLayer, ISync, INotifyAddedToWorld,
INotifyRemovedFromWorld, INotifyMoving, INotifyCenterPositionChanged, ITick
```

`INotifyOwnerChanged` is absent. Confirmed.

The owner-dependent decision lives in the subclass, `Vision.cs:48-54`:

```csharp
protected override void AddCellsToPlayerMapLayer(Actor self, Player p, IReadOnlyList<PPos> uv)
{
    if (!info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(p)))
        return;
    p.MapLayers.AddSource(this, info.Strength, uv, self);
}
```

Note the shape carefully, because it is what makes the bug permanent rather than transient. The base
class fans out over **every** player (`AffectsMapLayer.cs:165-169` and `:188-189`) and the subclass
filters by relationship *at the moment of the call*. The result is a **snapshot** of the ownership
relationships, written into each player's `MapLayers.sources` dictionary
(`engine/OpenRA.Game/Traits/Player/MapLayers.cs:116`), keyed by the trait instance. The trait
instance does not change when the actor changes owner, so nothing about the flip disturbs the entry.

Only four things re-run that snapshot, and a captured building triggers none of them:

| Trigger | Site | Fires on capture? |
|---|---|---|
| `AddedToWorld` | `AffectsMapLayer.cs:172` | No — in-place path skips `World.Add` |
| `RemovedFromWorld` | `AffectsMapLayer.cs:195` | No — skips `World.Remove` |
| `CenterPositionChanged` / `MovementTypeChanged` | `:108`, `:205` | No — buildings do not move |
| `ITick`, **only** if range or disabled-state changed | `:138-147` | No — capture changes neither |

`ITick` is the one worth spelling out, because it is the obvious candidate for an accidental
self-heal and it is not one. It early-returns unless `cachedRange != range` or the disabled state
flipped (`:146-147`). A capture changes neither, so the trait never recomputes.

### The consequence

**Old owner keeps live vision: TRUE, and permanently.** The per-cell counters
(`MapLayers.cs:376`, `visibilityCount[index][modifiedStrength]++`) are only decremented by
`RemoveSource` (`:398-430`), which is reached only from `RemoveCellsFromPlayerMapLayer`. Nothing
calls it. The old owner keeps full-strength live vision — not merely explored/fogged memory — out to
the building's full range, for the rest of the match.

**New owner gains nothing: TRUE.** At `AddedToWorld` the relationship test failed for the (then
enemy) captor, so no source was ever inserted into their `MapLayers`. Nothing inserts one later.

**Does anything compensate? No, with two narrow exceptions I could find:**

1. If the building has an `IVisionModifier` that later changes its range, `ITick` fires `UpdateCells`
   and the trait self-heals for both players. Incidental, not a designed mitigation.
2. On destruction, `RemovedFromWorld` (`:195-199`) removes the source from **all** players. So the
   leak does not outlive the building — there is no permanent state corruption, only a
   lifetime-of-the-building leak.

### Scope: which buildings? Essentially all of them

`^BasicBuilding` (`mods/ww3mod/rules/ingame/structures.yaml:10`) carries **three** `Vision` layers —
`Vision@3` strength 3 to `1c0`, `Vision@2` strength 2 from `1c0` to `2c0`, `Vision@1` strength 1 from
`2c0` to `3c0` (`:13-24`). The brief's "3c0" is right as the outer radius, and it is three trait
instances, not one.

`^BasicBuilding` also inherits `^NeutralOrOccupiedCapturable` (`:9`, defined `:169-177`), which
supplies `CaptureManager` and two `Capturable` blocks. **Every basic building in the mod is
capturable and carries three leaking Vision traits.**

### Correction to the brief: three capture sites, not two, plus garrison

| Site | Call |
|---|---|
| `engine/OpenRA.Mods.Common/Activities/CaptureActor.cs:140-141` | engineer capture |
| `engine/OpenRA.Mods.Common/Traits/ProximityCapturable.cs:224-225` | proximity capture |
| **`engine/OpenRA.Mods.Common/Traits/ProximityCapturableBase.cs:190-191`** | **not in the brief** |
| **`engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs:260, :324, :329`** | **not in the brief — highest traffic** |

The garrison path matters most and is the one I would lead with when explaining the bug to a player.
`GarrisonManager` flips a building Neutral → occupying player on entry (`:260`) and back on exit
(`:324`, `:329`). The player-visible symptom is blunt and testable: **garrisoning a building gives
you none of its vision.** That is not an edge case reachable only by an engineer rush; it is the
core garrison loop. It also predates the capture fix — commit `deee6733` says so explicitly:
"GarrisonManager already uses ChangeOwnerInPlace to avoid this."

---

## Q2 — Is the 0.5s figure true?

**Provenance found. It is a single unmeasured author estimate, and the mechanism it names is wrong.**

All three doc comments are copy-paste from one commit. `git log -S "0.5s freeze on capture"` returns
exactly one: **`deee6733`, "Capture freeze fix: use ChangeOwnerInPlace for buildings", 2026-04-06**,
also the commit that introduced `ChangeOwnerInPlaceSync` itself. Its message reads:

> That fires INotifyRemovedFromWorld/AddedToWorld on every AffectsMapLayer trait
> (Vision/Radar/Shroud), each iterating all players and recomputing visibility — ~0.5s freeze on capture.

Two problems.

**The stated mechanism is wrong.** `World.Remove`/`Add` (`engine/OpenRA.Game/World.cs:394-412`) fire
`INotifyAddedToWorld`/`RemovedFromWorld` only on **the traits of the actor being passed in** — one
building — not on "every AffectsMapLayer trait" in the world. The `Actor.cs:528` doc comment repeats
the error in different words ("shroud recalc on 10+ Vision traits per player"). A `^BasicBuilding`
has three Vision traits, not ten-plus.

**The arithmetic does not reach 0.5s.** Outer range `3c0` gives
`r = (3072 + 1023 + 512) / 1024 = 4` (`MapLayers.cs:294`), so `FindTilesInAnnulus` scans ~50 cells,
of which ~28 fall inside a radius-3 circle. Three Vision layers partition that circle, so the total
is ~30 cell-updates per player, ~240 across eight players. Each is an array index and a `short`
increment (`MapLayers.cs:350-381`). That is microseconds. It is off from 0.5s by roughly four orders
of magnitude.

**So what was the author actually seeing?** I do not know, and I want to be clear that I did not
establish it. `World.Remove`/`Add` is thin in itself, but it fires `ActorAdded`/`ActorRemoved`
(`World.cs:397`, `:407`) to every subscriber, and re-runs `Building.AddedToWorld`, which touches
`ActorMap` influence and can invalidate pathfinder caches. A real hitch from *that* is plausible.
The freeze was probably real; the attribution to vision recalc looks like a wrong guess at its cause.

**This turns out not to matter**, which is the important part. Q2 was posed on the theory that a
stale number would make "the cheap repair simply correct". The cheap repair is correct *regardless*,
because it does not go anywhere near `World.Remove`/`Add`. Measuring the 0.5s would refine a comment;
it would not change the recommendation. **I would not spend a run on it.**

If you want it measured anyway, the run I would ask for — and I am not asking:

> `./run-test.sh test-capture-rules` with a stopwatch `Trace.TickTime` probe bracketing the
> `ChangeOwnerSync` branch, forced on by temporarily inverting the `BuildingInfo` test at
> `CaptureActor.cs:140`. **The answer** is the wall-clock tick duration of the capture tick vs. the
> surrounding ticks. >100 ms confirms a real hitch; single-digit ms means the comment is folklore and
> all three sites should say so.

---

## Q3 — What would a direct hand-over look like?

### The layer that holds the cells

Per-player, in `MapLayers.sources`, a `Dictionary<object, VisionSource>` keyed by the trait instance
(`MapLayers.cs:116`). The `VisionSource` stores the exact per-cell nodes that were added
(`:80-94`), so `RemoveSource` decrements precisely what `AddSource` incremented (`:406-427`).

This is the good news for the fix: **remove-then-re-add is exact and cheap.** There is no "recompute
the world" involved and no global structure to reconcile. The cascade exists for reasons that have
nothing to do with vision — vision is genuinely incrementally updatable, and `UpdateCells`
(`AffectsMapLayer.cs:162-170`) is already exactly the "hand it over" primitive, called on every other
trigger.

So there is nothing to invent. The fix is to call the existing primitive on one more trigger.

### The specific reason a naive implementation would be wrong

**A naive `INotifyOwnerChanged` → `UpdateCells(self)` throws an unhandled
`InvalidOperationException` on every non-building owner change.** This is the sharp edge, and it is
not mentioned anywhere in the brief.

`AddSource` throws on a duplicate key (`MapLayers.cs:323-324`):

```csharp
if (sources.ContainsKey(mapLayer))
    throw new InvalidOperationException("Attempting to add duplicate mapLayer");
```

Now trace `ChangeOwnerSync` (`Actor.cs:569-593`) — still the path for **all non-building actors**,
i.e. every infantry and vehicle, and they all carry `Vision`:

1. `World.Remove(this)` → `RemovedFromWorld` → sources removed from all players. Dict clean.
2. `Owner = newOwner` (`:581`).
3. `INotifyOwnerChanged` fires (`:585-589`) — **while the actor is out of the world**. Naive handler
   calls `UpdateCells` → `AddSource` → **entry now present**.
4. `World.Add(this)` (`:592`) → `AddedToWorld` (`AffectsMapLayer.cs:172-190`) → and note line 188-189
   calls `AddCellsToPlayerMapLayer` with **no preceding `RemoveSource`**, unlike `UpdateCells` which
   removes first at `:167`. → duplicate key → **throw**.

The asymmetry between `AddedToWorld` (add only) and `UpdateCells` (remove then add) is what converts
a harmless-looking handler into a crash.

**The guard.** One line, and it is idiomatic in this very file — `CenterPositionChanged` (`:110`),
`ITick` (`:140`) and `MovementTypeChanged` (`:209`) all already gate on exactly this:

```csharp
void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
{
    if (!self.IsInWorld)
        return;

    UpdateCells(self);
}
```

- In-place path (buildings): `IsInWorld` stays `true` → `UpdateCells` runs → **fixed**.
- `ChangeOwnerSync` path (everything else): `IsInWorld` is `false` during the notify → early return →
  `World.Add` rebuilds correctly → **behaviour byte-identical to today, no crash.**

### In-repo precedent, and why it must not be copied verbatim

`RevealsMap` (`engine/OpenRA.Mods.Common/Traits/RevealsShroud.cs:28`) — the sibling map-layer trait —
**already implements `INotifyOwnerChanged`** and does precisely the remove-all-then-add-all dance
(`:56-67`). Two things follow.

First, it is direct evidence the `AffectsMapLayer` omission is an **oversight, not a design decision**.
Somebody solved this exact problem one file over.

Second — and this is a trap for whoever implements it — **`RevealsMap`'s handler is not safe to
paste into `AffectsMapLayer`.** `RevealsMap` has no `IsInWorld` guard, and does not need one: its
class declaration (`:28`) implements neither `INotifyAddedToWorld` nor `INotifyRemovedFromWorld`. It
manages its cells through `TraitEnabled`/`TraitDisabled` (`:81-90`) instead, so it can never
double-add. `AffectsMapLayer` *does* have the world hooks, so it needs the guard that `RevealsMap`
omits. Copying the visible pattern without the invisible precondition is how this gets shipped broken.

---

## Q4 — What else is in this class?

The precise predicate is: a trait that does owner-dependent work in `AddedToWorld`/`RemovedFromWorld`
(or caches an owner-derived value at construction), does **not** implement `INotifyOwnerChanged`, and
can sit on a building. Census below; the `AffectsMapLayer` subclasses I verified directly, the
remainder come from a delegated sweep and are marked accordingly.

### Confirmed, reachable today

| Trait | Site | Stale state | Visibility |
|---|---|---|---|
| **`Vision`** ×3 per building | `Vision.cs:48-56` via `AffectsMapLayer.cs:172-190` | The subject of this report. | **High** |
| `BaseProvider` | `Traits/Buildings/BaseProvider.cs:47,:59` | Caches `self.Owner.PlayerActor.Trait<DeveloperMode>()` in the constructor; after capture it queries the *previous* owner's dev-mode. Also gates `RequiresBaseProvider` placement (`Building.cs:262`). | Low |
| `RenderDebugState` | `Traits/Render/RenderDebugState.cs:28,:50` | Caches the owner's `SquadManagerBotModule` list in `Created`. On `^ExistsInWorld`, so on every building. Debug overlay only. | Debug-only |

### Same defect, not currently reachable — but they inherit the Option A fix for free

`Radar` (`Radar.cs:47-56`), `CounterBatteryRadar` (`CounterBatteryRadar.cs:45-54`) and
`CreatesShroud` (`CreatesShroud.cs:56-71`) are all `AffectsMapLayer` subclasses with byte-identical
owner logic. Radar/CBR ship only on vehicles and aircraft; `CreatesShroud` appears in no YAML. None
is reachable by the building-only in-place path today. **A fix on the base class covers all of them
at zero extra cost**, which is a genuine argument for fixing at `AffectsMapLayer` rather than
`Vision`.

`SupplyRouteContestation` (`SupplyRouteContestation.cs:126-127,:249`) has the same shape, but
`SUPPLYROUTE` (`structures.yaml:222`) does not inherit `^BasicBuilding` and carries no `Capturable`.
Unreachable — and it stays unreachable, since SR capture is not wired. Worth a comment, not a fix.

### Wider blast radius — same root cause, outside the stated predicate

`ChangeOwnerInPlaceSync` also skips the `ActorAdded`/`ActorRemoved` events (`World.cs:397`, `:407`),
so **owner-keyed world indexes never see the flip**. These are player/world-level rather than
per-actor, so they are out of scope for a fix to `AffectsMapLayer`, but they are the same bug and
some look higher-impact than the vision leak:

- **`SupportPowerManager`** (`SupportPowerManager.cs:44-45,:53-92`) — a captured support-power
  building stays in the old owner's `Powers` dict and never enters the captor's.
- **`TechTree`** (`Player/TechTree.cs:32-40`) — `ActorChanged` never fires, so **neither** player's
  prerequisite set updates on capture.
- **`ActorIndex.OwnerAndNames`** (`ActorIndex.cs:32-49`) — consumed by `HarvesterBotModule.cs:88-89`,
  `McvManagerBotModule.cs:79-81`, `CaptureManagerBotModule.cs:83`,
  `CaptureCoordinatorBotModule.cs:638`. A captured building permanently ghosts in the victim bot's
  index.

I did not verify these three myself in full — see the trust ledger. If they hold, **`TechTree` and
`SupportPowerManager` are probably more important than the vision leak** and deserve their own queue
item. I would not fold them into this one: different layer, different fix, different test.

### Flagged, unresolved

`FrozenUnderFog.OnOwnerChanged` (`Modifiers/FrozenUnderFog.cs:219-225`) implements the interface but
refreshes only the **old** owner's frozen actor. Under `ChangeOwnerSync` the `World.Add` re-created
the rest; under the in-place path nothing does. Implementing the interface is not the same as
handling it. Unresolved — worth ten minutes before Option A lands, since it sits in the same
shroud subsystem and could confound the autotest's fog assertions.

---

## Options, costed

### Option A — `INotifyOwnerChanged` on `AffectsMapLayer`, with the `IsInWorld` guard · **recommended**

~8 lines in `AffectsMapLayer.cs` plus a RED-then-GREEN autotest scenario.

- **Cost:** one afternoon, dominated by the test, not the fix.
- **Risk: low.** Reuses `UpdateCells`, the primitive every other trigger already calls. The guard is
  idiomatic three times over in the same file. Non-building actors take a proven-unchanged path
  (early return), so the blast radius is confined to actors whose owner changes while in-world —
  which today is exactly buildings on the in-place path, i.e. exactly the broken case.
- **Perf:** ~240 cell-updates per capture (§2 arithmetic). Not measurable. **Cannot reintroduce the
  freeze — it never calls `World.Remove`/`Add`.**
- **Bonus:** fixes `Radar`, `CounterBatteryRadar`, `CreatesShroud` for free if they ever land on a
  building.
- **`@stable` note:** this changes shared-trait behaviour with no new Info field, so per CLAUDE.md
  it flows to `@stable` deliberately and the commit message must say so, so the next benchmark
  baseline is re-taken knowingly.

### Option B — the same handler on `Vision` only

- **Cost:** same afternoon. **Risk:** same.
- **Rejected:** strictly worse than A. Identical work, identical risk, and it leaves three sibling
  classes carrying a known defect awaiting a future YAML change to detonate. The duplication also
  invites exactly the divergence the project has been burned by before.

### Option C — revert the three capture sites to `ChangeOwnerSync`

- **Cost:** minutes. **Risk: high, and it is the wrong fix.**
- **Rejected:** reintroduces whatever `deee6733` was actually seeing (unmeasured, but the garrison
  path would now hit it on every soldier entering and leaving a building — far more often than
  capture). It also does not fix the `TechTree`/`SupportPowerManager` class, since those were broken
  by the same commit and would only be *masked* here.

### Option D — measure the 0.5s first, then decide

- **Cost:** one instrumented run plus analysis. **Risk:** low, but it buys nothing.
- **Rejected:** the measurement cannot change the recommendation, because Option A's cost is
  independent of the cascade's cost. Left on the table only if someone wants to correct the three
  doc comments, which is a cleanup, not a blocker.

---

## The test I would ask for (I did not run it)

Per CLAUDE.md this is behavioural, so AUTOTEST applies by default, and per the standing memory the
RED run is budgeted, not skipped.

New scenario `test-capture-vision-handover`, derived from the existing `test-capture-rules`
(`tools/autotest/scenarios/test-capture-rules`). Two players, one capturable `^BasicBuilding` sited
so its `3c0` radius covers ground **no other unit of either player can see**, which is the whole
difficulty of the scenario — any overlapping vision source makes the assertion vacuous.

- **RED (sabotage first, per policy):** on unfixed `main`, assert post-capture that the captor sees a
  cell inside the radius. Expected failure text must name *that cell being invisible to the captor* —
  not a generic timeout, which would pass for the wrong reason.
- **GREEN:** after Option A, captor sees it and **the old owner does not**. Both halves matter; the
  leak and the gap are separate assertions and a fix could plausibly deliver one without the other.
- **Second scenario, cheaper and higher-value:** extend
  `tools/autotest/scenarios/test-garrison-ownership-flip-evacuation` to assert the garrisoning player
  gains the building's vision on entry. This is the high-traffic path and the one a player would
  actually report.
- **Regression guard against the §3 crash:** any existing scenario that changes a **non-building**
  actor's owner exercises the `IsInWorld` guard. If Option A is implemented without it, that scenario
  throws `InvalidOperationException` rather than failing an assertion. `test-crate-proximity-capture`
  is the likely candidate — worth confirming it covers a mobile target before relying on it.

Note the scenario-tooling trap from CLAUDE.md: **`make nav-guard` and bare `--check-yaml` both skip
`tools/autotest/scenarios/` entirely**, so a new scenario's green is meaningless as validation of the
scenario itself. Lint it explicitly with `./utility.sh --check-yaml ../tools/autotest/scenarios/test-capture-vision-handover`
(the `../` is required) — that is a manager-run gate under this session's constraints.

---

## Trust ledger

### Verified by reading the code myself

- `AffectsMapLayer` does not implement `INotifyOwnerChanged` — class declaration, `AffectsMapLayer.cs:42-43`.
- The four re-run triggers and their non-applicability to a stationary captured building — `:108`, `:138-147`, `:172`, `:195`, `:205`.
- The relationship snapshot and its per-player fan-out — `Vision.cs:48-54`, `AffectsMapLayer.cs:165-169`, `:188-189`.
- Sources keyed by trait instance; counters only decremented via `RemoveSource` — `MapLayers.cs:116`, `:376`, `:398-430`.
- `AddSource` throws on duplicate key — `MapLayers.cs:323-324`.
- `ChangeOwnerSync` fires `INotifyOwnerChanged` **while the actor is out of the world** — `Actor.cs:578-592`. This is the crash mechanism.
- `AddedToWorld` adds without removing first, unlike `UpdateCells` — `AffectsMapLayer.cs:188-189` vs `:167`.
- Three in-place capture sites — `CaptureActor.cs:140-141`, `ProximityCapturable.cs:224-225`, `ProximityCapturableBase.cs:190-191`.
- Garrison call sites — `GarrisonManager.cs:260`, `:324`, `:329`.
- `^BasicBuilding` has three Vision layers to `3c0` and inherits `^NeutralOrOccupiedCapturable` — `structures.yaml:9`, `:13-24`, `:169-177`.
- The 0.5s figure originates in exactly one commit, `deee6733`, and is an author estimate with no measurement — full message read.
- `World.Add`/`Remove` notify only the passed actor's traits — `World.cs:394-412`. This is what makes the commit message's mechanism wrong.
- `RevealsMap` implements `INotifyOwnerChanged` and lacks the world hooks that would require a guard — `RevealsShroud.cs:28`, `:56-67`, `:81-90`.

### Taken on trust (delegated sweep, not personally read)

- `BaseProvider`, `RenderDebugState`, `ProximityContestable` line numbers and their staleness.
- The entire "wider blast radius" section — `SupportPowerManager`, `TechTree`, `ActorIndex`. **These
  are the least-verified claims in this report and the ones with the largest implications.**
- The `FrozenUnderFog` partial-handler flag.
- The "checked and NOT affected" exclusions, including that `Radar`/`CounterBatteryRadar` appear on
  no building.

### Not established

- What the ~0.5s freeze actually was. I showed the stated cause cannot account for it and did not
  determine the real one.
- Any runtime behaviour whatsoever. **No game was launched, no test run, nothing compiled.** Every
  claim here is static reading of source and YAML.

### The single thing I would most expect to be wrong

**That the old owner keeps *live, full-strength* vision rather than merely explored/fogged terrain.**

The reasoning is a chain of three static inferences — the source stays in the dictionary, therefore
the counters stay incremented, therefore `ResolvedVisibility` keeps resolving above the fog
threshold — and I verified each link by reading, but never observed the outcome. The `Tick` resolver
at `MapLayers.cs:226-285` recomputes `ResolvedVisibility` from `visibilityCount` every tick, and it
has interactions I did not fully trace: `explored[index]` gates the whole block (`:241`), and there
is a floor at `visibility = 1` (`:255-256`) whose relationship to `FogEnabled` I did not chase
through every caller. If some other mechanism clamps a captured building's contribution, the leak
could present as "old owner keeps explored terrain" — cosmetic — rather than "old owner keeps a live
enemy-detecting sensor deep in enemy territory", which is a competitive-integrity bug.

**This distinction is the difference between a nice-to-have and a must-fix, and it is exactly what
the RED run in §"The test I would ask for" would settle.** It does not change which option to take —
Option A is correct either way — but it changes the priority, so I would want the RED observed before
this is scheduled rather than after.
