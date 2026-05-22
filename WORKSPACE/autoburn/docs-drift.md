# Docs Drift Audit — 260520

Branch: `auto/docs-drift`. Doc-only — no engine changes.

## Summary

Crawled every markdown file under `DOCS/` and `WORKSPACE/`
(excluding `archive/`, `playtests/`, `sessions/`) for references to
code paths and symbols, then verified each one against the current
tree.

| Bucket | Count |
|---|---|
| Markdown files scanned | 54 |
| Link targets (`[text](path)`) | 79 |
| Code-span path candidates | 799 |
| Symbol-like spans (PascalCase) | 1238 |
| **Confidently stale, fixed** | 6 doc references across 6 files |
| **Possibly renamed / dropped — REPORTED, not fixed** | 4 cases |
| **Forward references in plan docs — intentionally left alone** | many |

The dominant pattern of real drift in this audit is the **AI re-org**:
several v2-era docs (doctrine, handoff, foundation, WAKEUP_CHECKLIST,
v2_experiment_002_capture_coordinator,
stage_a_frontline_perception) moved under
`WORKSPACE/ai/archive/` when the AI workspace was reset
(see `WORKSPACE/ai/README.md`), but live docs and trackers were not
updated to follow them.

## Doc fixes (committed)

Each commit is a single doc-file edit, scoped to a path that no
longer resolves and a known new home.

| Commit | File | Change |
|---|---|---|
| `37110785` | `DOCS/gameplay/ai-overlay.md` | 3 refs to `WORKSPACE/ai/doctrine.md` / `stage_a_frontline_perception.md` → `archive/` paths |
| `4a1d59a7` | `DOCS/gameplay/capturing.md` | `WORKSPACE/ai/v2_experiment_002_capture_coordinator.md` → `archive/` |
| `6f1baad0` | `DOCS/reference/supply-route.md` | `WORKSPACE/ai/foundation_260511.md` → `archive/` |
| `29c2ce17` | `WORKSPACE/HOTBOARD.md` | `doctrine.md` + `handoff_260513.md` → `archive/` paths |
| `dbaa0ce3` | `WORKSPACE/RELEASE_V1.md` | `foundation_260511.md` + `WAKEUP_CHECKLIST_260512.md` → `archive/` |
| `2387c115` | `WORKSPACE/plans/260511_ai_tournament_harness.md` | `foundation_260511.md` → `archive/` |

All six fixes are mechanical path updates — the doc text and meaning
are unchanged. The archived target docs still exist; readers
following the link land where the prose intends.

## Possible-renames (need user judgment — NOT fixed)

These references look stale but the fix isn't obvious from the
artifact alone. Flagging for the user.

### 1. `WORKSPACE/RELEASE_V1.md` L45 — `RefillFromHost` trait, `CargoSupplyEconomyTest.cs`

Context: the "Supply & ammo economy overhaul" tracker entry says
P1 shipped *"repair+refill via new `RefillFromHost`/`Restock`. Tests
in `CargoSupplyEconomyTest.cs`"*.

State today:
- `engine/OpenRA.Mods.Common/Activities/RefillFromHost.cs` —
  **deleted in commit `7a32e3df` ("Rip CargoSupply: TRUK is now a
  SupplyProvider")**.
- `engine/OpenRA.Test/OpenRA.Mods.Common/CargoSupplyEconomyTest.cs` —
  **deleted in the same commit**.
- `Restock` (order class) — still alive in
  `engine/OpenRA.Mods.Common/Traits/DropsSupplyCache.cs`.
- Current WW3MOD-specific tests per `CLAUDE.md` are
  `AmmoPoolTest.cs`, `SupplyProviderMathTest.cs`,
  `SuppressionMathTest.cs`.

So this is a partial breakage: P1 shipped one way (`RefillFromHost`
+ tests file), then was ripped/refactored in the 260511 economy
refactor. The tracker line reads as if both still exist. Not
auto-fixed because:
- `RefillFromHost` may no longer have a successor in the new
  `SupplyProvider`/`DropsSupplyCache` architecture — calling it
  "Restock-via-SupplyProvider" might or might not be the right
  rename.
- The tests previously in `CargoSupplyEconomyTest.cs` may have
  been folded into one of the three current test files, or just
  dropped.

User call: rewrite L45 to reflect the current trait names and the
current test file location, or drop the trait names entirely since
the [T] entry is about the v1 feature, not its implementation.

### 2. `WORKSPACE/HOTBOARD.md` L18 — `WORKSPACE/EXPERIMENTAL_NOTES.md`

The "Recent Wins" WGM/Hellfire bullet ends with
*"Experimental package (uncommitted) in
`WORKSPACE/EXPERIMENTAL_NOTES.md`"*.

This file does **not** exist on `main`. `git log --all` shows it
exists only on `auto/preserved-wip-260520` (per
`WORKSPACE/autoburn/README.md`: *"GroupScatter waypoint refactor,
river-zeta map tree-thinning, EXPERIMENTAL_NOTES.md"* among the
captured uncommitted files).

The bullet already says *"(uncommitted)"*, which is half-honest, but
a reader on `main` can't open the path. Two reasonable fixes:
- Update the path: ``WORKSPACE/EXPERIMENTAL_NOTES.md` on branch `auto/preserved-wip-260520``.
- Or drop the pointer entirely as the WGM bullet has aged out of
  "recent wins" anyway.

Not auto-fixing because either choice rewrites meaning.

### 3. `WORKSPACE/automation/README.md` L33 — `engine/OpenRA.Game/Sdl2PlatformWindow.cs`

The Phase 0 plan lists this file under "Files touched", hedged
with *"or similar — if off-screen path needs engine support"*. The
actual file lives at
`engine/OpenRA.Platforms.Default/Sdl2PlatformWindow.cs`.

The "or similar" hedge means this isn't strictly stale, but the
specific path is wrong. Could be tightened to the real path, or
left as-is since Phase 0 has not been started. Not auto-fixing
because the plan-doc convention here is intentionally aspirational.

### 4. `DOCS/recipes/SCREENSHOT.md` L15 — `chrome/lobby.yaml`

Trigger pattern reads *"Touching `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/` or `chrome/lobby.yaml`"*.
WW3MOD's actual lobby chrome file is
`mods/ww3mod/chrome/lobby-options.yaml`. `chrome/lobby.yaml` exists
in `engine/mods/common/` and `engine/mods/cnc/` (upstream OpenRA).

Soft reference — not technically broken since the file does exist in
the engine mods. Could be tightened to
`mods/ww3mod/chrome/lobby-options.yaml` if the intent is the
WW3MOD-specific path. Not auto-fixing because the intent is
ambiguous.

## Deliberately NOT fixed

- **Plan-doc forward references** (`WORKSPACE/automation/README.md`,
  `WORKSPACE/lobby/IMPLEMENTATION_PLAN.md`,
  `WORKSPACE/plans/260512_screenshot_evaluation.md`). These list
  files that *will* exist once a phase ships
  (`tools/autotest/run-queue.sh`, `composite.py`,
  `WORKSPACE/automation/autonomous_queue.md`,
  `~/claude-orchestrator/*`, `corner-bracket.png`, etc.). These are
  not stale — they are intentional roadmap entries.
- **`WORKSPACE/ai/03_substrate.md` substrate types**
  (`GoalLedger`, `ResourceMap`, `TerrainCache`, `SectorMap`,
  `SectorBudget`, `ProductionPlan`) — referenced in
  speculative-design docs that say *"not yet binding"* and propose
  these as future plumbing. Zero hits in engine code, but expected.
- **`04_brain.md`** referenced from `01_default_ai_explained.md`,
  `02_problem_statement.md`, `03_substrate.md` — explicitly marked
  *"TBD"* / *"someday"* in the source docs.
- **Line numbers** (`Missile.cs:1067`, `world.yaml:316–388`, etc.) —
  ignored per task brief. They drift continuously and fighting that
  is wasted effort.
- **Bare filenames found elsewhere in repo** (e.g. `Missile.cs`,
  `Mobile.cs`, `Capturable.cs`, `Captures.cs`) — these are
  intentionally short prose references; the file still exists in
  the tree and a reader can `find` it. Not stale.
- **Soft / hedged references** with `...` ellipsis or "or whatever"
  qualifiers (`engine/.../Activities/Move/SmartMoveActivity.cs`,
  `Effects/SpriteAnnotation.cs or whatever the order-feedback path
  uses today`) — the docs are explicit about their imprecision.
- **`DOCS/archive/AI_STRATEGY.md`** — archive content, scope-excluded
  per the task brief.

## Files touched

Six markdown files, one commit each:

- `DOCS/gameplay/ai-overlay.md`
- `DOCS/gameplay/capturing.md`
- `DOCS/reference/supply-route.md`
- `WORKSPACE/HOTBOARD.md`
- `WORKSPACE/RELEASE_V1.md`
- `WORKSPACE/plans/260511_ai_tournament_harness.md`

Plus this report.

## Method notes

Extraction:
- Markdown link regex `\[([^\]]+)\]\(([^)]+)\)` against all
  in-scope `.md` files
- Backtick code-span regex `` `([^`]+)` ``
- Resolution: link targets relative to the doc dir; code-paths
  tried both doc-relative and repo-root
- Symbol grep: batched alternation over PascalCase spans against
  `engine/`, `mods/`, `tools/`

Filtering was conservative — anything containing `<placeholder>`,
`*`, whitespace, or math operators was skipped as obviously
non-path. Symbols < 4 chars and ambiguous English-word PascalCase
(`Auto`, `Back`, `Beach`, `Bridge`, `Default`, `Close`) were treated
as too noisy to verify and skipped from the report (would have
needed per-context judgement).

The strongest signal of real drift was **basenames whose only
on-disk location was under `WORKSPACE/ai/archive/`** — i.e. the
AI re-org moved them and the live docs didn't follow. That accounts
for all six of the confident fixes.
