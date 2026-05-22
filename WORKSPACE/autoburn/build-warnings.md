# Autoburn — auto/build-warnings (260520)

## Summary

| Metric | Before | After |
|---|---|---|
| Total compiler/analyzer warnings (Debug rebuild) | 14 | 13 |
| Net fixed | — | 1 (SA1514) |

The engine's Release build emits **0 warnings** because `Directory.Build.props`
explicitly disables analyzers in Release (`<Target Name="DisableAnalyzers"
BeforeTargets="CoreCompile" Condition="'$(Configuration)'=='Release'">`). All
warnings surface only in the Debug configuration via StyleCop, Roslynator, and
the .NET CodeAnalysis (CA) packages. No `CS####` compiler warnings exist at
all — none of the task's "safe class" targets (CS0168 unused locals, CS0219
assigned-not-used, CS0414, CS8019 unused usings, CS8632 nullable) were
present.

## What I did

| Commit | File | Warning | Fix |
|---|---|---|---|
| `ecfda03` | `engine/OpenRA.Mods.Common/TraitsInterfaces.cs:602` | SA1514 — Element documentation header should be preceded by blank line | Inserted single blank line between the `MoveFollow` signature and the `/// <summary>` for `MoveToTarget`. Pure whitespace. |

That's the entire set of changes — one trivial whitespace insertion. Build
re-verified green at 13 warnings after the commit.

## Attempted and reverted

- **VehicleCrew.cs SA1013 ×4** (lines 381, 382, 562, 563 — `}[dir]`). My first
  attempt inserted a space (`} [dir]`) to satisfy SA1013, but that immediately
  tripped **SA1010** ("Opening square brackets should not be preceded by a
  space") at the same column. The two analyzer rules are mutually exclusive
  on the `new[] {...}[i]` immediate-indexer syntax — there is no whitespace
  formulation that satisfies both. Reverted. A genuine fix requires
  refactoring (e.g. extracting `static readonly int[] CrewWalkDx/Dy`
  fields) which is out of scope per the "no behavioural / no extra
  refactoring" rules in this task and CLAUDE.md.

## Skipped — categorized

Counts below are *unique warnings* (the build log lists each twice — once in
the project's compile pass, once in the final summary). 13 unique total.

| Code | Count | Files | Reason for skipping |
|---|---|---|---|
| **RCS1226** (Roslynator) — Add `<para>` to documentation comment | 4 | `OpenRA.Game/Actor.cs:489`, `OpenRA.Mods.Common/Tournament/IMatchScorer.cs:10`, `OpenRA.Mods.Common/Tournament/IWinRuleEvaluator.cs:12`, `OpenRA.Mods.Common/Traits/IProvideTooltipDescription.cs:17` | The existing convention in WW3MOD-authored doc blocks uses `///` blank-separator lines for paragraphs. Suppressing RCS1226 means wrapping each paragraph in `<para>...</para>` tags. Stylistic preference; not a defect; mass-applying would impose a convention the authors didn't choose. The task says "do not mass-fix style suggestions". |
| **SA1013** (StyleCop) — Closing brace should be followed by a space | 4 | `OpenRA.Mods.Common/Traits/VehicleCrew.cs:381,382,562,563` | Can't be satisfied without triggering SA1010 (see "Attempted and reverted"). Would need a refactor; flagged as a follow-up. |
| **SA1509** (StyleCop) — Opening braces should not be preceded by blank line | 2 | `OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyOptionsLogic.cs:127,131` | The blank lines inside the `CommonOptionSection` dictionary initializer are **intentional visual grouping** by section (Economy / Match / World). Removing them would degrade readability — falls under CLAUDE.md's "STOP AND ASK / never autonomously downgrade UX" rule. |
| **RCS1155** (Roslynator) — Use StringComparison when comparing strings | 2 | `OpenRA.Game/TestMode.cs:85,106` | **Behaviour change.** Adding `StringComparison.Ordinal` (or `.OrdinalIgnoreCase`, etc.) changes semantics versus the culture-sensitive default. Task explicitly says do not touch behaviour-changing warnings. |
| **CA2231** (CodeAnalysis) — Implement equality operators alongside `Equals` | 1 | `OpenRA.Mods.Common/Traits/Buildings/RallyPoint.cs:34` | **API surface change.** Adds `operator ==` / `!=` to the struct. Out of scope for a warning-cleanup pass; deserves its own review for `null`-handling and consistency with other engine structs. |

## Open questions / follow-ups

1. **Should StyleCop analyzers run in Release too?** Currently only Debug
   builds surface them. Release stays warning-free by design. Worth a
   discussion before deciding to maintain warning-free in both configs.
2. **VehicleCrew husk-cookoff direction arrays** (lines 381–382 and 562–563)
   are duplicated between two methods and re-allocated on every kill.
   Pulling them out to `static readonly int[] CrewWalkDx/Dy` would:
   (a) silence the 4 SA1013 warnings, (b) eliminate per-kill allocation,
   (c) dedupe the literal. Recommended as a follow-up — flagged here
   because the task forbids autonomous refactors of this scope.
3. **`StringComparison` audit on TestMode.cs**. The two `.Contains` /
   `.Equals` calls in `TestMode` parse the `Test.Mode=true` launch arg.
   They should almost certainly be `StringComparison.OrdinalIgnoreCase`
   to be robust against locale and casing, but that's a deliberate
   change worth verifying against current launch-arg handling elsewhere.
4. **`RallyPoint` equality operators**. The struct overrides `Equals` and
   `GetHashCode` but not `==`/`!=`. If callers ever compare via the
   operators today, they're using reference/default value-type equality
   that may diverge from `Equals`. Worth auditing call sites before
   adding operators blindly.

## Verification

- Build configuration tested: `dotnet build engine/OpenRA.sln -c Debug -t:Rebuild --nologo`
- Final state: **Build succeeded. 13 Warning(s). 0 Error(s).**
- Warning code distribution after fixes:
  ```
  1× CA2231
  2× RCS1155
  4× RCS1226
  4× SA1013
  2× SA1509
  ```
- Release build (`-c Release`) re-verified to remain at 0 warnings.

## Files touched

- `engine/OpenRA.Mods.Common/TraitsInterfaces.cs` — one blank line added before `MoveToTarget` doc comment (line 602).

## Methodology notes

- Started with the full solution; the Mods.Common-only target the task suggested
  emits 0 warnings under Release (analyzers off by design).
- Build cache hides warnings on incremental builds — required `-t:Rebuild`
  to get the analyzer pass to re-emit.
- Conservative bias applied throughout: where a "fix" risked changing
  behaviour, API surface, or layout/readability the author chose deliberately,
  I documented rather than shipped (per the task's `<70% sure → document
  don't ship` rule).
