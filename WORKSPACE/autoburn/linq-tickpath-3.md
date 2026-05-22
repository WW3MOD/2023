# auto/linq-tickpath-3 — autoburn 260521

## Status

DONE — third stage of the linq-tickpath escalation. Took the 6 findings
the previous worker reported, judged each on still-valid / safe-to-ship,
and shipped the 4 that passed the bar. 2 deferred as designed.

## Summary

- **Findings actioned:** 4 of 6 (F2, F3, F4, F6).
- **Skipped:** 2 of 6 (F1 broad blast radius, F5 documented desync risk).
- **All 4 builds verified clean** (Release, 0 warnings, 0 errors).
- **No autotest runs** — HARD RULE forbids batch runs in autoburn.

## Per-finding verdicts

### F1 — `Util.ApplyPercentageModifiers` project-wide

**Verdict:** SKIP.

The original report explicitly classified this as "moderate blast radius,
leave for a focused session" — the fix needs a new overload on `Util.cs`
plus touch-up at three caller sites (`Aircraft.cs`, `Mobile.cs`,
`Turreted.cs`), each of which has its own caching subtlety
(`Exts.Lazy` wrapping in Mobile because of trait init ordering). Outside
the surgical autoburn pass scope. Left for a focused session.

### F2 — `WithShadow.ModifyRender`

**Verdict:** SHIP.

Confirmed at `engine/OpenRA.Mods.Common/Traits/Render/WithShadow.cs:49`.
Trait is applied via `mods/ww3mod/rules/defaults.yaml` so it's on every
aircraft + most ground units. `ModifyRender` ran per render frame, allocating
3 LINQ iterators (Where + Select + Concat) plus the existing ToList.

Converted to iterator method that yields shadow sprites first, then
originals — same output order as `shadowSprites.Concat(renderables)`.
Folded the double `((IModifyableRenderable)ma)` cast into a single
pattern-match assignment, and hoisted the per-frame WVec out of the
per-item path.

**Commit:** `8a6fe3cc`.

### F3 — `Hovers.ModifyRender`

**Verdict:** SHIP.

Confirmed at `engine/OpenRA.Mods.Common/Traits/Render/Hovers.cs:111`.
`r.Select(a => a.OffsetBy(WorldVisualOffset))` allocates a Select iterator
plus a closure capturing `this` (the lambda reads the `WorldVisualOffset`
property each step). Iterator method allocates only the generator state
machine — no per-call closure. Semantically identical: both forms read
`WorldVisualOffset` lazily as each item is yielded.

**Commit:** `10d7df19`.

### F4 — `Targetable.TargetableBy`

**Verdict:** SHIP.

Confirmed at `engine/OpenRA.Mods.Common/Traits/Targetable.cs:54`.
`cloaks.All(c => c.IsTraitDisabled || !c.ShouldHide(self, viewer.Owner))`
allocates a closure capturing self + viewer.Owner per call. Replaced
with foreach that short-circuits identically (returns false on the first
hider, true after the loop).

The pre-existing `cloaks.Length == 0` early-return on line 51 still
covers the common no-cloak case, so the saved allocation only matters
when cloaks are present — but for cloaked targets and their attackers
this fires per Armament × per candidate target during combat scans.

**Commit:** `0a9e48b2`.

### F5 — `Cloak.ShouldHide`

**Verdict:** SKIP.

The previous worker's notes and the in-file comments at
`engine/OpenRA.Mods.Common/Traits/Cloak.cs:296-318` document an earlier
caching attempt that was reverted due to desync. Per-call cost is
intentional for correctness. Not touchable without a dedicated desync
test rig — out of scope here.

### F6 — `CashTrickler.GetModifiedAmount`

**Verdict:** SHIP.

Confirmed at `engine/OpenRA.Mods.Common/Traits/CashTrickler.cs:110`.
`Tick()` calls this once per CashTrickler actor every tick (line 146 —
checks if modifiers changed to decide whether to update the registered
income entry). The `Concat(...).Select(x => x.GetCashTricklerModifier())`
form allocated 2 LINQ iterators per call.

Converted to a static iterator method `EnumerateModifiers(self)` that
yields modifier values from the actor traits then the player-actor
traits. Keeps the `Util.ApplyPercentageModifiers(IEnumerable<int>)`
signature stable (no API churn). Static method also avoids implicit
`this` capture.

Side cleanup: dropped `using System.Linq` since this is now the only
collection-related import the file needs — replaced with
`System.Collections.Generic`.

**Commit:** `6593ae4a`.

## Verification

```
$ (cd engine && dotnet build OpenRA.Mods.Common/OpenRA.Mods.Common.csproj \
    -c Release --nologo -clp:ErrorsOnly)
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Ran after each of the 4 fixes (one error on F6 from a missing
`System.Collections.Generic` import, fixed and re-verified clean).

## Files touched

```
engine/OpenRA.Mods.Common/Traits/Targetable.cs
engine/OpenRA.Mods.Common/Traits/Render/Hovers.cs
engine/OpenRA.Mods.Common/Traits/Render/WithShadow.cs
engine/OpenRA.Mods.Common/Traits/CashTrickler.cs
WORKSPACE/autoburn/linq-tickpath-3.md
```

## Commits

```
6593ae4a perf: convert CashTrickler modifier chain to iterator method (per linq-tickpath-2 F6)
8a6fe3cc perf: convert WithShadow.ModifyRender to iterator method (per linq-tickpath-2 F2)
10d7df19 perf: drop deferred Select in Hovers.ModifyRender (per linq-tickpath-2 F3)
0a9e48b2 perf: replace cloaks.All lambda with foreach in Targetable (per linq-tickpath-2 F4)
```

## What remains

- **F1** is the highest-value remaining item (Aircraft/Mobile/Turreted
  movement speed paths are all hot) but needs a focused session for the
  Util.cs overload + per-call-site caching.
- **F5** stays as-is unless someone builds a desync test rig.

Each shipped fix has a PITFALL anchor at the rewritten site so a future
reader who tries to "simplify back to LINQ" sees the cost call-out.
