# auto/linq-tickpath — autoburn 260520

## Status

SALVAGED — original worker was killed when the Maestro daemon was terminated under CPU pressure. The 2 commits below are clean and shipped; this report is the conductor's post-mortem.

## Commits

- `73320bb3` — `perf: cache AmmoPool reference in Armament`
  - The `AmmoPool` property getter scanned all traits via LINQ on every access (`TraitsImplementing<AmmoPool>().FirstOrDefault` with nested `.Any`).
  - Hot path: `NextBurstBar.GetValue` calls `.AmmoPool.HasAmmo` per armament every render frame for selected actors with a burst bar.
  - The matching pool is fixed at YAML load → cache the reference once in `Created()`, serve property reads from the field.
- `87e6d76b` — `perf: drop redundant per-frame .ToArray() in DrawLineToTarget`
  - `IRenderAnnotationsWhenSelected.RenderAnnotations` is called every frame for every selected actor.
  - The cached list was being copied into a new array on the return path.
  - `WorldRenderer` enumerates the `IEnumerable` synchronously in the same frame — safe to return the list directly, saves one allocation per selected actor per frame.
  - Same pattern fixed in `WithGarrisonDecoration.cs`.

## Verification

No "build verified" note in either commit message. User should confirm with `make all` or `dotnet build`.

## Skipped / not done

Original prompt scoped 3-8 perf fixes plus a broader findings report. Worker shipped 2 and was killed mid-survey. There's likely more LINQ in tick paths worth a pass — the methodology in the prompt (grep `void Tick(`, `ITick`, `void Render(`) still applies.

## Files touched

```
engine/OpenRA.Mods.Common/Traits/Armament.cs
engine/OpenRA.Mods.Common/Traits/Render/DrawLineToTarget.cs
engine/OpenRA.Mods.Common/Traits/Render/WithGarrisonDecoration.cs
```
