# auto/dead-code-2 — autoburn 260521

Continuation of the killed `auto/dead-code` worker. Prior branch shipped 2 commits removing stale commented blocks (AttackBase, AmmoPool, Passable, Cargo); see `git show auto/dead-code:WORKSPACE/autoburn/dead-code.md` for that report.

## Status

**DONE.** 10 commits shipped (cap reached), 7 findings reported but not removed.

## Summary

- **Removals shipped:** 10 (cap was ~10)
- **Findings reported, not removed:** 7
- **Build status:** clean at every step (`OpenRA.Mods.Common` builds with 0 warnings, 0 errors after each commit)
- **Truly unused private methods found:** 0 (subagent search across 996 WW3MOD-touched files turned up no method names with exactly one engine-wide grep hit — prior sweeps drained this well)

## Removals

Each commit was build-verified before landing.

| # | Commit | File | What was removed |
|---|---|---|---|
| 1 | `95735d45` | `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/CommandBarLogic.cs` | `int patrolHighlighted` field + its Tick() decrement. The patrolButton was rewired to read `world.OrderGenerator is PatrolOrderGenerator` in 9b7eb78a (2026-03-24), dropping all `patrolHighlighted = 2` writes; field + decrement loop survived as dead. |
| 2 | `d1e5bbc3` | `engine/OpenRA.Mods.Common/Traits/Armament.cs` | `// int ticksSinceLastShot; // FF ??` (2023-03-20) and 4-line commented `INotifyNewTarget.Acquired` stub whose only body was a `Game.Debug` call. |
| 3 | `e3a378df` | `engine/OpenRA.Mods.Common/Activities/Move/Move.cs` | 8-line "Decelerate if close to target" alternative formula commented out 2024-03-06 (284ad8a5). The trailing `// else` dangled in front of the live `if (progress >= Distance)`. |
| 4 | `95da33b6` | `engine/OpenRA.Mods.Common/Traits/Conditions/GrantConditionOnPreparingAttack.cs` | 7 commented blocks (37 lines) from 2023-03-20/21 referencing `PreparingRevokeDelay`, `AttackingRevokeDelay`, `RevokeOnNewTarget` — fields that only exist on `GrantConditionOnAttack` (a different trait). Won't compile if uncommented. File was just touched 2026-05-06 ('Setup/aim phase + DR auto-fire stabilization') without revive. |
| 5 | `297f73de` | `engine/OpenRA.Mods.Common/Traits/CarrierSlave.cs` | Commented `NeedToReload` helper and `ammoPools` init (2023-03-20/21). References `ammoPools` field that never existed on this class — would not compile if uncommented. |
| 6 | `ae11327a` | `engine/OpenRA.Mods.Common/Traits/BlocksSight.cs` | 9-line commented `AnyBlockingActorAt` stub (2024-01-23/26). Copy-paste from `BlocksProjectiles.cs` that references `t.BlockingHeight` — only present on `IBlocksProjectiles`, not `IBlocksSight` (which exposes only `Density`). |
| 7 | `11a2998f` | `engine/OpenRA.Mods.Common/Traits/AffectsMapLayer.cs` | 6-line "CPU improvement - Update shroud every 10 ticks" gate commented 2023-06-18/07-17. References undeclared `checkTick` field. |
| 8 | `f2cc6a65` | `engine/OpenRA.Mods.Common/Traits/World/ResourceLayer.cs` | 10-line "Remove all starting ore" block from 2023-03-20 (e1cdc0706). References lowercase `world.Map` instead of in-scope `w.Map` — would not compile. Comment label also misleading (body adds resources, doesn't remove them). |
| 9 | `0e3f80ba` | `engine/OpenRA.Mods.Common/Warheads/TargetDamageWarhead.cs` | One-line commented empty `InflictDamage` override (2023-05-15). A commented-out empty override is invisible whether present or absent. |
| 10 | `8f8fb34c` | `engine/OpenRA.Mods.Common/Traits/Conditions/ExternalCondition.cs` | 10-line abandoned while-loop from 2024-08-13. Indexes `permanentTokens` (a `Dictionary<object, HashSet<int>>`) by `int` — won't compile. Superseded by the live nested-foreach immediately above. |

**Total deletion: ~111 lines across 10 files.**

## Reports (uncertain — kept)

These looked tempting on first pass but were rejected on closer reading. Conservative bias.

1. **`engine/OpenRA.Mods.Common/Traits/BotModules/SquadManagerBotModule.cs:28`** — `IncludeInSquadTypes` HashSet field
   - **Reason:** Has `[Desc]` attribute, putting it on the worker's forbidden-attributes list (YAML-bound trait fields). Even though no live C# uses it and no YAML in `mods/` references it, removing a `[Desc]`-tagged field is out of scope.
   - **Related comment at line 273-274** (`// FF TODO: This could be useful // Info.IncludeInSquadTypes.Contains(a.Info.Name) &&`) was also left alone — the `FF TODO` marker signals active intent.

2. **`engine/OpenRA.Mods.Common/Activities/Attack.cs:55-56` and `:119-126`** — `oldTarget` / `notifyNewTarget` commented field decls + usage
   - **Reason:** Inline comment "FF / Never got used in the end but seems to work" explicitly marks this as preserved-by-intent. The author kept it as a known-working sketch. Not stale-by-neglect.

3. **`engine/OpenRA.Mods.Common/Traits/Cloak.cs:132-135`, `:296-319`, `:338-358`** — three caching/desync experiment blocks
   - **Reason:** The line "// Desynced, hence uncommented" above one of them is a load-bearing warning. These blocks document failed CPU-improvement attempts that caused desync; removing them risks a future author re-trying the same broken pattern.

4. **`engine/OpenRA.Mods.Common/Traits/LeavesTrailsCA.cs:121-124`** — `/* ---- removed for CA version for V3/ICBM */`
   - **Reason:** Label explicitly explains why the bounds-check is missing (CA-fork variant for ballistic/ICBM trails). Removing the comment loses the rationale.

5. **`engine/OpenRA.Mods.Common/Traits/LeavesTrailsCA.cs:137`** — single duplicated-line comment
   - **Reason:** Trivial (one line, identical to the live line below). Not worth a commit on its own.

6. **`engine/OpenRA.Mods.Common/Traits/Player/EnemyWatcher.cs:42 / 71 / 93 / 114`** — `playedNotifications` write-only set
   - **Reason:** Field is added to but never read (the commented line 94 was the only reader). Genuine dead state, but cleanly removing it requires touching live non-commented code in 4 places + the deduplication intent might be partly intentional ("track in case we want gating back"). Recommend a deliberate follow-up rather than an autonomous removal.

7. **`engine/OpenRA.Mods.Common/Traits/PaletteEffects/GlobalLightingPaletteEffect.cs:76`** — "Here is the reference code for the operation we are performing"
   - **Reason:** Block is explicitly labeled as reference documentation for the live GPU equivalent. Intentional, not stale.

## Verification

```
$ cd engine && dotnet build OpenRA.Mods.Common/OpenRA.Mods.Common.csproj -c Release --nologo -clp:ErrorsOnly
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Verified after every commit. Final state clean.

## Files touched

```
engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/CommandBarLogic.cs
engine/OpenRA.Mods.Common/Traits/Armament.cs
engine/OpenRA.Mods.Common/Activities/Move/Move.cs
engine/OpenRA.Mods.Common/Traits/Conditions/GrantConditionOnPreparingAttack.cs
engine/OpenRA.Mods.Common/Traits/CarrierSlave.cs
engine/OpenRA.Mods.Common/Traits/BlocksSight.cs
engine/OpenRA.Mods.Common/Traits/AffectsMapLayer.cs
engine/OpenRA.Mods.Common/Traits/World/ResourceLayer.cs
engine/OpenRA.Mods.Common/Warheads/TargetDamageWarhead.cs
engine/OpenRA.Mods.Common/Traits/Conditions/ExternalCondition.cs
```

## Notes

- Search via subagent for *truly unused private methods* (name appearing exactly once in engine-wide grep) returned **zero** — the codebase is clean on that front after prior sweeps. The 10 removals here are all stale commented blocks ≥ 4 months old.
- Most removed blocks would not have compiled if uncommented (referenced fields/types that don't exist). This is the strongest signal of dead code.
- The 7 reported-but-kept items represent the conservative-bias guardrail working as intended: when the comment carries documented intent ("Desynced", "removed for CA version", "but seems to work", or sits behind a `[Desc]`-tagged field), leave it alone.
