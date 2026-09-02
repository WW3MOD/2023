# RED arm for `test-capture-vision-handover`

Branch `wt/vision-fix`. The scenario was authored against the fix in
`engine/OpenRA.Mods.Common/Traits/AffectsMapLayer.cs`; this file is how you prove it is
measuring that fix and not agreeing with itself.

**I did not run either arm.** Launches are serialised through the manager. Everything below is
a prediction to be checked against what actually prints.

---

## The sabotage — one token

In `AffectsMapLayer.OnOwnerChanged`, replace `IsInWorld` with `Disposed`:

```diff
 void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
 {
-    if (!self.IsInWorld)
+    if (!self.Disposed)
         return;

     UpdateCells(self);
 }
```

Then `make all` and run the scenario. Restore with the inverse edit before the GREEN arm.

### Why this token and not the obvious one

The tempting sabotage is deleting the `!`, so the guard reads `if (self.IsInWorld) return;`.
Do not use it. It disables the fix for buildings **and** un-guards the out-of-world path at the
same time, so an unlucky ordering could kill the run with the duplicate-key throw before the
assertion under test is ever evaluated — a crash and a RED look nothing alike in a log, and the
run would not tell you what you asked.

`!self.Disposed` is inert instead. `ChangeOwnerInPlaceSync` and `ChangeOwnerSync` both open with
`if (Disposed) return;` (`Actor.cs:547`, `:571`), so no actor that reaches this notification is
ever disposed, so the guard always returns and the handler is a pure no-op — which is precisely
the pre-fix state, since pre-fix the handler did not exist. No behaviour changes anywhere except
the one thing the fix added. It also compiles without a warning: the `return` is statically
reachable, so no unreachable-code diagnostic fires under this repo's warnings-as-errors setup.

---

## What RED must print

Expected verdict **FAIL**, from the gap half of Phase 2
(`test-capture-vision-handover.lua`, `Measure()`):

```
the building changed hands and its new owner got NO LIVE VISION from it: USA resolves 0 at the
probe cell, expected >= 2 from Vision@PROBE (Strength: 3). 0 means the cell is still shrouded
for the captor; 1 means explored ground with nothing watching it (MapLayers.cs:255-256,
:592-597). This is AffectsMapLayer carrying no INotifyOwnerChanged: the building flipped through
ChangeOwnerInPlaceSync, which skips World.Remove/Add, so the AddedToWorld snapshot that decided
only Neutral may see is never recomputed. [probe (31,14): USA=0 Neutral=3] [sentry (10,27):
USA=3 Russia=0] [Fort owner USA, Trooper in world false]
```

The prose is fixed. Three numbers are predictions rather than constants:

| Reading | Predicted | Meaning if it differs |
|---|---|---|
| `USA=0` at probe | `0` | `1` would mean USA had explored the cell anyway — Trooper's 2c0 bubble reached further than the geometry allows for. The verdict is still a legitimate FAIL (1 < 2 is still not live vision), but the scenario's isolation is weaker than claimed and the probe cell should move north. |
| `Neutral=3` at probe | `3` | **This is the answer the recon asked for — see below.** |
| `Fort owner USA` | `USA` | Anything else and the run should have Skipped, not Failed. |

### `Neutral=3` is the finding, not decoration

`WORKSPACE/recon/260902-capture-vision-transfer.md` closes by naming the claim it would most
expect to be wrong: whether the old owner keeps **live** vision or merely **explored** terrain.
It is a three-link static inference and nobody observed it. This number observes it:

- **`Neutral=3`** — the stale source resolves at its full authored strength. The old owner keeps
  a live, detecting sensor. Competitive-integrity bug, and the priority the recon assumed.
- **`Neutral=1`** — the stale source is being clamped to the explored floor
  (`MapLayers.cs:255-256`), which `MapLayers.cs:592-597` documents as indistinguishable from
  "nobody is looking". The leak is then cosmetic and the item should be re-priced downward.
- **`Neutral=0`** — the scenario is not measuring what it thinks. Treat the whole run as void.

I predict `3`, on the reading that `MapLayers.Tick` recomputes `visibility` from
`visibilityCount` every tick and takes the highest non-zero band, and that nothing decrements
that counter except `RemoveSource` — which the in-place path never reaches. But this is exactly
the link the recon flagged, so **believe the number over me.**

One caveat I want on the record: this measures Neutral, not a human enemy, because on unfixed
code only a *world-start* owner ever holds a source and garrison can never make a player one
(`GarrisonManager.cs:259` claims only from Neutral). The argument that it generalises is that
`MapLayers` is per-player and no part of the resolver branches on player identity. That argument
is sound but it is an argument, not a second measurement.

## What GREEN must print

Expected verdict **PASS**, no message. The readings behind it:
`USA=3` and `Neutral=1` at the probe, then `Russia=3` and `USA=1` at the sentry cell.

If GREEN instead fails on the **leak** half — Neutral still >= 2 after the flip — the fix moved
vision to the captor without withdrawing it from the former owner, which `UpdateCells` should
make impossible (it removes from every player before re-adding, `AffectsMapLayer.cs:167-168`).
That would point at `RemoveSource` rather than at this fix.

---

## Why no setup control can produce the RED text

The scenario has **four** `Test.Fail` sites and every one of them is behind the flip having
provably happened. Everything that can go wrong in setup calls `Test.Skip` instead, and Skip and
Fail are distinct verdicts in `result.json`. So the RED text above is unreachable unless all of
the following were already true:

1. **All three players resolved.** Otherwise `WorldLoaded` Skips.
2. **Neutral had live vision of the probe cell at t+2s** (`Baseline`, `Neutral >= 2`). This is
   the one that retires "the probe cell is outside the radius" and "the `Vision@PROBE` override
   never attached" — both would otherwise produce a captor reading of 0 for a reason that has
   nothing to do with ownership. Failing it Skips.
3. **USA read exactly 0 at the probe cell at t+2s** (`Baseline`, `captorSees > 0` Skips). So no
   USA source reached the cell before the flip, and the post-flip reading cannot be inherited
   from one.
4. **`Fort.Owner` actually became `USA`** within 25 s (`Garrison`). A flip that never happened
   Skips. This is what makes the FAIL a statement about hand-over rather than about pathing,
   `DynamicOwnership`, or `EnterTransport`.
5. **Trooper was out of the world when measured** (`Measure`). A soldier deployed to a firing
   port stands in-world at the building's `Location`, where his own bubble could light the probe
   cell and manufacture a *green*. Refusing to measure in that state removes the only way the
   captor reading could be produced by something other than the building.

Given 1–5, the probe cell was inside a building whose vision Neutral was receiving, that
building is now USA's, and no other USA source is in range. `USA < 2` then has exactly one
remaining cause: the flip did not move the source.

The reverse direction matters too — **the scenario cannot go green against the live bug**, which
is the failure mode that would make this whole exercise worthless. Two traps were live here and
both are closed in-tree:

- **The Lua `Owner =` setter would not have tested this at all.** It calls `Actor.ChangeOwner`
  (`GeneralProperties.cs:63`) → `ChangeOwnerSync` → `World.Remove`/`Add`, which hands vision over
  correctly with or without the fix. The neighbouring scenario
  `test-garrison-ownership-flip-evacuation` uses exactly that idiom (`Seized.Owner = russia`), so
  it is the natural thing to copy. This scenario drives the flip through `EnterTransport` and a
  real `GarrisonManager` claim instead, and says so at `Garrison()`.
- **A stock `v01` would have self-healed.** Every one of its ten Vision traits carries
  `RequiresCondition: loaded` (`^CivBuilding` pulls `^StandardVisionWhenLoaded` over
  `^BasicBuilding`'s unconditional layers), and a conditional trait's enable transition is picked
  up by `ITick` (`AffectsMapLayer.cs:143-147`) *after* the frame-end ownership flip — repairing
  the staleness under test through a trigger unrelated to the fix, on a race decided by
  intra-tick actor order. `rules.yaml` strips all ten and installs one **unconditional**
  `Vision@PROBE`, which leaves exactly one of the five `UpdateCells` triggers live: the new one.

---

## Run

```
./run-test.sh test-capture-vision-handover
```

Read `result.json`, not the tail of stdout — per `WORKSPACE` standing guidance a piped verdict
returns the pipe's exit code. Budget two slots: one RED, one GREEN.
