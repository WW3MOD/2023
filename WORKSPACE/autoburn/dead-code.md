# auto/dead-code — autoburn 260520

## Status

SALVAGED — original worker was killed when the Maestro daemon was terminated under CPU pressure. The 2 commits below are clean and shipped; this report is the conductor's post-mortem.

## Commits

- `c09cc75e` — `dead-code: remove stale commented Console.WriteLine + AutoRearm block`
  - `engine/OpenRA.Mods.Common/Traits/Attack/AttackBase.cs`: 2025-04-20 debug `Console.WriteLine` (violates the "no Console.Write in tick-path" engine rule too).
  - `engine/OpenRA.Mods.Common/Traits/AmmoPool.cs`: 2024-01 commented `AutoRearmIfAllEmpty` call. The method is now invoked from callers (`Attacking`, `OnBecomingIdle`); the in-`TakeAmmo` call was intentionally lifted out and the comment is stale.
- `ea11106e` — `dead-code: drop stale commented blocks in Passable.cs + Cargo stub`
  - `Passable.cs`: 3 commented blocks from 2024-02/08 — old guard-clause drafts and a 19-line abandoned `RelationshipWith` ladder superseded by the live implementation above them.
  - `Cargo.cs`: empty `PickUpClosestActors` stub (commented body, 2024-08), unreferenced, not in any planning doc.
  - **Kept** the NRE stack trace in `Passable.OnBeingPassed` — explains the `passerMobile != null` null check that fixes the heli-mine NRE.

## Verification

Worker's commit messages claim "Build verified" on both. User should still confirm with `make all` or a quick `dotnet build` before merging.

## Skipped / not done

The original prompt asked for a wider survey of unused private methods/fields in WW3MOD-touched files. The worker only got to stale commented blocks before being killed. The branch should be treated as a partial pass — there's still more dead code to be found.

## Files touched

```
engine/OpenRA.Mods.Common/Traits/Attack/AttackBase.cs
engine/OpenRA.Mods.Common/Traits/AmmoPool.cs
engine/OpenRA.Mods.Common/Traits/Passable.cs
engine/OpenRA.Mods.Common/Traits/Cargo.cs
```
