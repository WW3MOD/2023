# PITFALL backfill survey — `auto/pitfall-survey`

## Summary

Surveyed git history for recurring-bug commits (the high-precision
backfill described in `CLAUDE.md` → PITFALL Comments → Backfill).
Confirmed five traps that match the criteria — bug recurred at least
once across separate commits, root cause sits at a "tidy-looking"
temptation site where a future careless reader would naturally simplify
or revert the fix.

Five anchors shipped, one per file, each as its own commit. Build is
clean (`OpenRA.Mods.Common` Release, 0 warnings / 0 errors).

Cap from prompt was ~10 — stayed well under so each anchor earns its
keep. Candidates that were strong-but-one-shot are listed below under
*Skipped*.

## Anchors shipped

| # | File:line (post-edit) | Trap (one-line) | Originating bug commit |
|---|---|---|---|
| 1 | `engine/OpenRA.Mods.Common/Activities/Move/AttackMoveActivity.cs:76` | `ignoreScanInterval=true` must be unconditional — AttackFollow burns the per-actor scan counter every tick, any "smarter" reuse starves attack-move | `66569aa0` (May 2026) |
| 2 | `engine/OpenRA.Mods.Common/Activities/RotateToEdge.cs:55` (and twin at `:73`) | `ChildHasPriority=false` so early-sell intercepts mid-flight before `Aircraft.Repulse` pins the helicopter at the map edge | `c4e82b96` (May 2026) — explicitly the same regression as `768df672`, re-introduced by FlyOffMap switch `8857c3a4` |
| 3 | `engine/OpenRA.Mods.Common/Pathfinder/HierarchicalPathFinder.cs:660` | `ActorIsBlocking` must use `PassableClasses` (Passes ∪ Crushes), NOT `Crushes` alone — mirror `Locomotor.UpdateCellBlocking` | `7ffc5dd3` (May 2026) — upstream-merge regression from `release-20250330`, will likely regress on next merge |
| 4 | `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs:460` | `FindBestShelterSoldier` needs the per-soldier suppression gate; per-port lockouts cannot stop shelter↔port flapping because the other port has its own lockouts | `437e33cd` (May 2026) — same flapping pattern as `bf3e14d9` |
| 5 | `engine/OpenRA.Mods.Common/Activities/Move/Move.cs:650` | `MoveFirstHalf.OnComplete` must check `mobile.MovingBackward` when computing `toFacing` — the chain re-runs for every remaining path cell, so a fix only in `Move.Tick` rotates the vehicle on cell 2+ | `d68e01b2` (Mar 2026) |

All five anchors are at the **temptation site** (the line a future
careless reader would actually be looking at), not at the broken-code
site — per the Log.cs cautionary tale in `CLAUDE.md`.

## Candidates skipped

| Commit | Why skipped |
|---|---|
| `de94ae8b` Z-order secondary sort | Already PITFALL'd at both temptation sites (`WorldRenderer.RenderableZPositionComparisonKey` and the `OrderBy.ThenBy`). Verified, not double-anchored. |
| `8cafe7a2` CounterBatteryRadar persistence | Already PITFALL'd in `MapLayers.AddSource` by the fix commit itself. |
| `c620a9f2` border-fog visibility-aware second pass | Recurring shape (previous fix broke sprites), but the new approach lives at one well-named call site `DrawBeyondMapFogVisibilityAware`. Low risk of careless simplification — a tidy-up would rename, not delete. |
| `be46cde9` ScaredyCat panic-resume | Looks like a one-shot fix to me; the commit notes a *parallel* pattern in `InfantryStates.PanicTraitEnabled` but doesn't describe a recurrence — adding a PITFALL would be speculative. |
| `170a3702` build-menu infinite right-click | One-shot UX bug, not a recurring trap. |
| `a29db695` / `d6ff35ff` (2023) animation scale + null handling | Three years old, code in question has been heavily reshaped since. Adding an anchor now would tag fully-evolved code with a stale incident. |
| `22d147391` (2020, upstream) "Fix regressions introduced in #133" | Upstream OpenRA, mod.yaml only — out of scope. |
| `90d1b415` shadow recalc disabled | The intent is documented in-line (`CURRENTLY UNUSED (260503)`) and the call sites are commented out, so there's no live temptation site. |
| `48d762cc` right-click own SR | One-shot fix; the trap (targeter `Owner == self.Owner`) is the *fix* itself, not a recurring pull. |
| `9af581b1` (2023) blocking/detection | Old, vague commit message — couldn't confirm a recurring trap. |
| `437e33cd` SuppressionLockout misuse (different temptation site) | The recall-side counterpart could in theory get its own PITFALL, but the soldier gate IS the load-bearing fix — anchoring both would dilute, not strengthen. One anchor for the trap pattern. |

## Verification

```bash
$ dotnet build engine/OpenRA.Mods.Common/OpenRA.Mods.Common.csproj -c Release
…
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:02:15.11
```

No tests run — these are comment-only edits at sites with existing
behavioural test coverage (`test-attackmove-paladin`,
`test-pathfinder-tree-pass`, `test-evac-suite`, etc., all already
green per their fix commits).

## Files touched

- `engine/OpenRA.Mods.Common/Activities/Move/AttackMoveActivity.cs`
- `engine/OpenRA.Mods.Common/Activities/RotateToEdge.cs` (both ctors)
- `engine/OpenRA.Mods.Common/Pathfinder/HierarchicalPathFinder.cs`
- `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs`
- `engine/OpenRA.Mods.Common/Activities/Move/Move.cs`
- `WORKSPACE/autoburn/pitfall-survey.md` (this report)

## Notes for the user

- All five anchors quote the originating fix commit hash, so
  `git show <hash>` resurrects the full context if the anchor's
  one-line WHY isn't enough later.
- The HPF anchor (#3) is the one most likely to pay back — every
  upstream merge is a chance for it to regress, and the comment
  is now greppable as `PITFALL`.
- The garrison anchor (#4) replaces a verbose multi-line block with a
  shorter PITFALL note that points to the recurrence pattern; the
  body fits on four lines and links to both fix commits.
