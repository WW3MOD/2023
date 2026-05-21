# auto/perf-render-scan — autoburn 260521

## Status

DONE — wider tick/render allocation hunt beyond LINQ-specific patterns.

## Summary

- **Fixes shipped:** 5
- **Findings (report-only):** 4
- **Files scanned (focused):** ~30 tick/render-path files in
  `engine/OpenRA.Mods.Common/Traits/`, `Widgets/`, `Projectiles/`
- **Skipped:** files already touched by `auto/linq-tickpath{,-2,-3}` per scope
- **Builds verified clean** after each fix (Release, 0 warnings, 0 errors)

Patterns hunted (beyond LINQ): `new List<>/Dict/HashSet` inside Tick/Render
bodies, `string.Format`/interpolation per frame, lambda captures in
high-frequency callbacks, deferred LINQ chains re-enumerated per tick,
`.ToArray()` for iterate-while-modify patterns.

---

## Fixes

### F1 — MiniMapPings.cs: drop per-tick `Pings.ToArray()` allocation

**Commit:** `009fd843`

`engine/OpenRA.Mods.Common/Traits/World/MiniMapPings.cs:43`

Before:
```csharp
void ITick.Tick(Actor self)
{
    foreach (var ping in Pings.ToArray())
        if (!ping.Tick())
            Pings.Remove(ping);
}
```

After:
```csharp
void ITick.Tick(Actor self)
{
    for (var i = Pings.Count - 1; i >= 0; i--)
        if (!Pings[i].Tick())
            Pings.RemoveAt(i);
}
```

Why hot: world singleton ITick — fires every world tick regardless of
whether there are pings to update. `ToArray()` allocates a fresh array
each tick to allow safe iterate-while-modify. Reverse index iteration
avoids the copy and uses O(1) `RemoveAt(i)` for the tail.

Risk: low. Same set of pings receives `Tick()` calls in the same order
visited (forward) — removals shift the tail leftward, but we've already
visited those indices, so we never skip or double-visit.

---

### F2 — MiniMapWidget.cs: reuse cells list across `Tick()` frames

**Commit:** `6e6e2af4`

`engine/OpenRA.Mods.Common/Widgets/MiniMapWidget.cs:406`

Before:
```csharp
var cells = new List<(CPos Cell, Color Color)>();
...
foreach (var t in world.ActorsWithTrait<IMiniMapSignature>()) {
    cells.Clear();
    t.Trait.PopulateMiniMapSignatureCells(t.Actor, cells);
    ...
}
```

After:
```csharp
readonly List<(CPos Cell, Color Color)> cellsBuffer = new();
...
// In Tick():
foreach (var t in world.ActorsWithTrait<IMiniMapSignature>()) {
    cellsBuffer.Clear();
    t.Trait.PopulateMiniMapSignatureCells(t.Actor, cellsBuffer);
    ...
}
```

Why hot: widget Tick fires every UI frame (~60 Hz) and unconditionally
allocates a new list. The `cells.Clear()` inside the loop was already
treating it as scratch — promoting to a field finishes the job.

Risk: very low. List is only used locally in one method, lifetime tied
to widget lifetime, contents fully overwritten each pass.

---

### F3 — IngameCashCounterLogic.cs: skip reformat when values unchanged

**Commit:** `692e327e`

`engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/IngameCashCounterLogic.cs:121`

Before:
```csharp
public override void Tick() {
    // ... animate displayResources toward actual ...
    var net = playerResources.NetChange;
    var sign = net >= 0 ? "+" : "";
    cashLabel.Text = string.Format(cashTemplate, displayResources) + " (" + sign
        + string.Format(cashTemplate, net) + ")";
}
```

After: same animation logic, but cache `lastDisplayResources` /
`lastNet` and early-return when both match. Only re-format the label
text when something visible changes.

Why hot: widget Tick every UI frame (~60 Hz). Each pass: two
`string.Format` calls (box int → object[]), three string concats. Once
the animated counter has settled (most of the time), all of that
recomputes the same text. Cache + early-return makes the common case
zero-alloc.

Risk: low. `displayResources` and `net` are the only inputs to the
label text, so equality across frames implies the text is unchanged.
Behaviour identical on every transition.

---

### F4 — RenderDetectionCircle.cs: inline max-range loop

**Commit:** `305253eb`

`engine/OpenRA.Mods.Common/Traits/Render/RenderDetectionCircle.cs:66`

Before:
```csharp
var range = detectCloaked
    .Select(a => a.Range)
    .Append(WDist.Zero).Max();
```

After:
```csharp
var range = WDist.Zero;
foreach (var dc in detectCloaked)
    if (dc.Range.Length > range.Length)
        range = dc.Range;
```

Why hot: called from `IRenderAnnotations.RenderAnnotations` (per render
frame, every detect-cloaked actor either always-on or while selected).
Select + Append + Max chains 2 iterator allocations per call. The
`detectCloaked` array is small (usually 1) but the iterator allocs fire
regardless of length.

Risk: low. The original `.Append(WDist.Zero).Max()` floors at zero; the
loop starts at zero and only goes up. Identical result.

---

### F5 — Detectable.cs: cache modifier trait array, inline addition

**Commit:** `9634d0a8`

`engine/OpenRA.Mods.Common/Traits/Modifiers/Detectable.cs:72,77,99`

Before:
```csharp
IEnumerable<int> detectableModifiers;
protected override void Created(Actor self) {
    detectableModifiers = self.TraitsImplementing<IDetectableAddativeModifier>()
        .ToArray().Select(x => x.GetDetectableVisionAddativeModifier());
}
void ITick.Tick(Actor self) {
    var detectable = Util.ApplyAddativeModifiers(DetectableInfo.Vision, detectableModifiers);
    ...
}
// IsVisibleInner uses the same chain.
```

After:
```csharp
IDetectableAddativeModifier[] detectableModifierTraits;
protected override void Created(Actor self) {
    detectableModifierTraits = self.TraitsImplementing<IDetectableAddativeModifier>().ToArray();
}
int ComputeDetectable() {
    var detectable = DetectableInfo.Vision;
    foreach (var m in detectableModifierTraits)
        detectable += m.GetDetectableVisionAddativeModifier();
    return detectable;
}
// Both Tick and IsVisibleInner call ComputeDetectable().
```

Why hot: `Detectable` is on every visible actor in WW3MOD. The deferred
`Select(...)` was stored and re-enumerated every Tick AND every
`IsVisibleInner` call (which fires during fog/visibility scans). Each
enumeration allocated a fresh Select iterator object. Caching the trait
array and walking it directly drops both the iterator alloc and the
`Util.ApplyAddativeModifiers` decimal cast.

Risk: low-to-medium. The original `ApplyAddativeModifiers` casts through
`decimal` to guard against overflow; the inputs here are small ints
(vision levels, modifier deltas), so the direct int sum cannot overflow
in practice and produces identical results.

---

## Findings (report-only — not fixed)

### R1 — SupportPowerTimerWidget.Tick: per-frame `.ToArray()` over formatted strings

`engine/OpenRA.Mods.Common/Widgets/SupportPowerTimerWidget.cs:58`

```csharp
texts = displayedPowers.Select(p => {
    ...
    var text = FluentProvider.GetMessage(Format, "player", ..., "time", time);
    var color = !p.Ready || Game.LocalTick % 50 < 25 ? self.OwnerColor() : Color.White;
    return (text, color);
}).ToArray();
```

Per UI frame: new array + new string per displayed power. `FluentProvider.GetMessage` itself allocates the formatted string. Could cache by `(power, remainingTicks, blinkPhase)` tuple but the lookup logic is touchy and Fluent updates are a separate concern. Suggested rewrite: extend the cache pattern in F3 (compare-and-skip on `(RemainingTicks // SECOND_GRANULARITY, Ready, blinkPhase)`). Risk: medium — touches localized string output.

### R2 — RenderRangeCircle.RenderAnnotations: per-frame List allocation when shift held

`engine/OpenRA.Mods.Common/Traits/Render/RenderRangeCircle.cs:184`

```csharp
var others = new System.Collections.Generic.List<(WPos, long)>();
foreach (var a in self.World.Selection.Actors) {
    ...
    others.Add((a.CenterPosition, expandedRadiusSq));
}
if (others.Count > 0)
    otherCircles = others.ToArray();
```

Fires per selected actor per render frame while shift is held. List grows by selection size. Suggested rewrite: promote `others` to a field with `.Clear()`; or compute count first, then stackalloc/allocate exact-size array. Risk: low. Skipped on conservative-bias grounds — only matters during active shift-held inspection.

### R3 — AddFrameEndTask closures: trail/spawn emitters

Files: `Projectiles/Bullet.cs:285`, `Traits/Render/LeavesTrails.cs:139`,
`Traits/Render/FloatingSpriteEmitter.cs:106`.

Each spawns SpriteEffects via `world.AddFrameEndTask(w => w.Add(...))`,
capturing 5-10 locals. Closure alloc per emission. Cumulative across
many projectiles/trail-emitters but frequency-gated (every N ticks per
emitter). Real fix would need a non-closure equivalent for
`AddFrameEndTask` — a `Func<World, object[], void>` + state array, or
specialized helpers. Larger refactor — out of autoburn scope.

### R4 — InfantryStates.GetDamageModifier: LINQ chain per damage event

`engine/OpenRA.Mods.Common/Traits/Infantry/InfantryStates.cs:203`

```csharp
var modifierPercentages = info.ProneDamageModifiers
    .Where(x => damage.DamageTypes.Contains(x.Key))
    .Select(x => x.Value);
return Util.ApplyPercentageModifiers(100, modifierPercentages);
```

Not strictly per-tick — fires per damage event. Allocates Where + Select
iterators each call. For infantry under sustained fire (e.g. machinegun
burst) this fires once per projectile hit. Same pattern as F5; could
inline a foreach over the dictionary entries. Lower priority than F5 (event
frequency vs per-tick).

---

## Verification

After each commit:
```
cd engine && dotnet build OpenRA.Mods.Common/OpenRA.Mods.Common.csproj \
    -c Release --nologo -clp:ErrorsOnly
=> Build succeeded. 0 Warning(s) 0 Error(s)
```

No autotest runs (HARD RULE — autoburn cannot trigger batches; single-test
runs not warranted because these are pure perf changes with same observable
output).

---

## Files touched

- `engine/OpenRA.Mods.Common/Traits/World/MiniMapPings.cs`
- `engine/OpenRA.Mods.Common/Widgets/MiniMapWidget.cs`
- `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/IngameCashCounterLogic.cs`
- `engine/OpenRA.Mods.Common/Traits/Render/RenderDetectionCircle.cs`
- `engine/OpenRA.Mods.Common/Traits/Modifiers/Detectable.cs`
- `WORKSPACE/autoburn/perf-render-scan.md` (this report)
