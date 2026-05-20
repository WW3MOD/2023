# auto/linq-tickpath-2 — autoburn 260521

## Status

DONE — continuation of `auto/linq-tickpath`. Picked up where the salvaged run stopped
(2 commits + tracking doc). One additional fix shipped, several findings reported.

## Summary

- **Fixes:** 1 commit covering 3 selection-bar files (NextBurstBar, ReloadBar,
  ReloadArmamentsBar). Same pattern of deferred Where chain re-allocated per frame.
- **Reports:** 6 findings flagged for later, ranging from project-wide patterns
  (the `speedModifiers` Select chain in Aircraft/Mobile/Turreted) to single-call-site
  closure allocations in hot target/cloak checks.

## Fixes

### `c2e3a8b2` — perf: drop deferred Where chain on selection-bar armaments

Three sibling files (`NextBurstBar.cs`, `ReloadBar.cs`, `ReloadArmamentsBar.cs`) all
shared the same pattern:

```csharp
// BEFORE
IEnumerable<Armament> armaments;

void INotifyCreated.Created(Actor self) {
    armaments = self.TraitsImplementing<Armament>()
        .Where(a => info.Armaments.Contains(a.Info.Name))
        .ToArray()
        .Where(t => !t.IsTraitDisabled);  // <-- deferred over the array
}

float ISelectionBar.GetValue() {
    if (!self.Owner.IsAlliedWith(self.World.RenderPlayer)) return 0;
    return armaments.Min(a => ...);  // re-allocates WhereArrayIterator every call
}
```

The trailing `.Where(t => !t.IsTraitDisabled)` is deferred — every `GetValue()` call
(per render frame for each selected actor that has the bar) allocates a fresh
`WhereArrayIterator<Armament>`. `NextBurstBar` additionally enumerated the chain
twice (`Any` + `Min`).

```csharp
// AFTER
Armament[] armaments;

void INotifyCreated.Created(Actor self) {
    armaments = self.TraitsImplementing<Armament>()
        .Where(a => info.Armaments.Contains(a.Info.Name)).ToArray();
}

float ISelectionBar.GetValue() {
    if (!self.Owner.IsAlliedWith(self.World.RenderPlayer)) return 0;

    var min = float.MaxValue;
    foreach (var a in armaments) {
        if (a.IsTraitDisabled) continue;
        // (NextBurstBar only) if (!a.AmmoPool.HasAmmo) return 0;
        var v = a.ReloadDelay / (float)a.Weapon.ReloadDelay;
        if (v < min) min = v;
    }
    return min == float.MaxValue ? 0 : min;
}
```

**Behavioral change:** if every armament is disabled, the original `.Min(...)` threw
`InvalidOperationException` (empty sequence). The new code returns `0`, which the
selection-bar contract already treats as "don't display" (`DisplayWhenEmpty => false`).
Safer; the throw was a latent crash.

**Savings:** 1–2 LINQ enumerator allocations per render frame per selected actor with
each bar trait. Selection bars are common on units in combat.

PITFALL anchors added at each `Created()` site.

## Reported findings (worth a future pass)

### F1. `Util.ApplyPercentageModifiers(int, IEnumerable<int>)` — project-wide Select-iterator allocs

Pattern, repeated across many traits:

```csharp
IEnumerable<int> speedModifiers;
speedModifiers = self.TraitsImplementing<ISpeedModifier>().ToArray()
                     .Select(sm => sm.GetSpeedModifier());
...
return Util.ApplyPercentageModifiers(Info.Speed, speedModifiers);  // hot
```

Each call to `MovementSpeed`/`MovementSpeedForCell`/`TurretMoveSpeed` allocates a fresh
`WhereSelectArrayIterator<T,int>`. Call sites:

- `engine/OpenRA.Mods.Common/Traits/Air/Aircraft.cs:379` (cached as field), used in `MovementSpeed`
  (`:781`), `IdleSpeed` (`:783`), turn speed (`:276`). `MovementSpeed` is read multiple times per
  aircraft tick from movement/landing activities.
- `engine/OpenRA.Mods.Common/Traits/Mobile.cs:303` (wrapped in `Exts.Lazy` because trait init
  ordering — constructor runs before other traits exist), used in `MovementSpeedForCell`
  (`:799`) with an extra `.Append(terrainSpeed)` adding *another* `AppendIterator` alloc per
  call. Pathfinding hammers this.
- `engine/OpenRA.Mods.Common/Traits/Turreted.cs:193`, used in `MoveTurret` every tick and in
  `FaceTarget` with `.Any(v => v == 0)`.

**Suggested rewrite:** add an `ApplyPercentageModifiers<T>(int, T[], int (*selector)(T))`
overload that iterates an array of strongly-typed modifier traits with a static-method-group
selector (avoids the closure too), then cache `ISpeedModifier[]` directly instead of the
deferred `Select`. Touches `OpenRA.Mods.Common/Util.cs` plus each call site — moderate
blast radius, leave for a focused session.

### F2. `WithShadow.ModifyRender` — `r.ToList()` + `Where`/`Select`/`Concat` per frame

`engine/OpenRA.Mods.Common/Traits/Render/WithShadow.cs:49-64`:

```csharp
IEnumerable<IRenderable> IRenderModifier.ModifyRender(...) {
    if (IsTraitDisabled) return r;
    var renderables = r.ToList();          // List alloc per frame
    var shadowSprites = renderables.Where(...).Select(...);  // 2 iterators
    return shadowSprites.Concat(renderables);                // 1 more iterator
}
```

Per frame, per actor with `WithShadow`. `WithShadow` is applied broadly via
`mods/ww3mod/rules/defaults.yaml` — every aircraft and most ground units. Currently
4 allocs per actor per frame.

**Suggested rewrite:** an iterator method using `yield return` for shadows then originals
(reusing the materialized list once). Saves 3 LINQ iterators (~150B), keeps the 1 List
alloc and adds 1 generator state machine — net ~2 allocs saved per render. Pattern already
in use at `FrozenUnderFog.cs:193` (`ApplyCosmeticRevealAlpha`).

Bonus: shadow ZOffset and original ZOffset always differ by `height + info.ZOffset`, so
list order doesn't affect visual output — could interleave shadow+original in a single
pass over `r` and drop the `ToList` entirely. Higher win, slightly higher review burden.

### F3. `Hovers.ModifyRender` — single-Select-iterator alloc per frame

`engine/OpenRA.Mods.Common/Traits/Render/Hovers.cs:111-114`:

```csharp
return r.Select(a => a.OffsetBy(WorldVisualOffset));
```

One `SelectEnumerableIterator` alloc + closure (captures `WorldVisualOffset`) per render
per hovering aircraft. Modest, but the trait is on all helicopters and probably some
ground vehicles.

**Suggested rewrite:** iterator method that closes over `WorldVisualOffset` only via
the state machine field, no captured-lambda. Same approach as F2.

### F4. `Targetable.TargetableBy` — `cloaks.All(lambda)` allocs per attack scan

`engine/OpenRA.Mods.Common/Traits/Targetable.cs:54`:

```csharp
return cloaks.All(c => c.IsTraitDisabled || !c.ShouldHide(self, viewer.Owner));
```

Called many times per target scan (per Armament × per candidate target). Each call
allocates a closure (captures `self` and `viewer.Owner`) and an array enumerator.
WW3MOD has limited Cloak usage (2 yaml files: `misc.yaml`, `structures-defenses.yaml`),
so the predicate short-circuits via `cloaks.Length == 0` check on line 51 for most
actors — but for cloaked targets and their attackers, this adds up during combat.

**Suggested rewrite:** trivial manual `foreach` — short-circuits on first hider, no
closure, no iterator. Same semantics. Low risk.

### F5. `Cloak.ShouldHide` — `ActorsWithTrait<DetectCloaked>().Any(closure)` per call

`engine/OpenRA.Mods.Common/Traits/Cloak.cs:327-329`:

```csharp
var shouldHide = Cloaked && !self.World.ActorsWithTrait<DetectCloaked>()
    .Any(a => a.Actor.Owner.IsAlliedWith(viewer)
        && Info.DetectionTypes.Overlaps(a.Trait.Info.DetectionTypes)
        && (self.CenterPosition - a.Actor.CenterPosition).LengthSquared <= a.Trait.Range.LengthSquared);
```

Worker's own comments at `:296-318` document that an earlier caching attempt was reverted
due to desync. So the per-call cost is intentional for correctness. **Don't optimize
without a desync test rig.** Reporting for visibility — closures over `viewer` + iterator
make this a real cost when cloaked actors are present.

### F6. `CashTrickler.GetModifiedAmount` — `Concat`/`Select` allocs every tick

`engine/OpenRA.Mods.Common/Traits/CashTrickler.cs:110-116`:

```csharp
int GetModifiedAmount(Actor self) {
    var modifiers = self.TraitsImplementing<ICashTricklerModifier>()
        .Concat(self.Owner.PlayerActor.TraitsImplementing<ICashTricklerModifier>())
        .Select(x => x.GetCashTricklerModifier());
    return Util.ApplyPercentageModifiers(info.Amount, modifiers);
}
```

Called from `ITick.Tick` every tick. Allocates a Concat iterator + Select iterator
per call. WW3MOD uses CashTrickler only on neutral structures (oil derricks etc, see
`mods/ww3mod/rules/ingame/structures-neutral.yaml`) — probably <10 per map. Low priority.

Cleaner fix would be to cache modifier arrays on owner change events and only recompute
when conditions flip, but that touches the modifier interface contract. Connected to F1.

## Verification

```
$ (cd engine && dotnet build OpenRA.Mods.Common/OpenRA.Mods.Common.csproj -c Release \
    --nologo -clp:ErrorsOnly)
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

No autotest runs — selection-bar GetValue semantics are exercised any time the player
selects a unit with the bar; the equivalence to the original LINQ is structural.

## Files touched

```
engine/OpenRA.Mods.Common/Traits/Render/NextBurstBar.cs
engine/OpenRA.Mods.Common/Traits/Render/ReloadBar.cs
engine/OpenRA.Mods.Common/Traits/Render/ReloadArmamentsBar.cs
WORKSPACE/autoburn/linq-tickpath-2.md
```

## What got skipped and why

- **`AttackGarrisoned.cs:241`** (`foreach (var m in muzzles.ToArray())`) — the `ToArray` is
  *intentional*: `m.Animation.Tick()` runs `PlayThen` callbacks that mutate `muzzles`
  via `Remove`. Removing the copy would crash with collection-modified-during-iteration.
  PITFALL-worthy if it isn't already.
- **`Mobile.cs:303` Lazy speedModifiers** — fix is touch-many-call-sites (F1). Out of
  scope for surgical autoburn pass.
- **`Cloak.ShouldHide`** — explicit dev comment warns of desync. Don't touch.
- **`Cargo.cs:276`** (`CurrentAdjacentCells.Any(c => Passengers.Any(p => ...))`) — runs
  only when checking if cargo can unload (cold path, order-time).
