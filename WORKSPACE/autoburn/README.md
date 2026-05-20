# Autoburn — 260520

Autonomous 4-hour orchestration run kicked off by the user 260520
~17:00 local. Single conductor (this doc's author) was spawning ≤10
concurrent workers, each on its own `auto/<topic>` branch in a
dedicated worktree under `/Users/fredrik/Desktop/WW3MOD-auto-<topic>`.

The Maestro daemon was terminated mid-run under CPU pressure (~18:20
local), killing 10 active workers. The conductor restarted, audited
the actual git state of every branch, salvaged what had real
commits, wrote post-mortem reports for branches where the worker died
before reporting, and pruned the worktrees that had zero commits.

## Goal (reminder)

Spend tokens on code quality, performance, obvious fixes,
consistency. Each branch is **for user review** — none will be
merged to main by an agent. User opens each branch afterwards,
judges, merges what's good, drops what's not.

## How to read this

1. Open the per-branch report under this folder
   (`<branch-name>.md`) — each branch has one (either worker-written
   or conductor post-mortem).
2. Check the branch out with `git worktree add /tmp/peek auto/<branch>`
   or just `git log auto/<branch> --oneline`.

## Branch inventory (final state after salvage)

### Clean — worker wrote its own report

| Branch | Commits | Summary |
|---|--:|---|
| `auto/console-cleanup` | 8 | `Console.Write[Line]` → `Log.Write` in tick-path code: MapDirectoryTracker, OpenGL callback, DebugServerTrait, PrintActivityTree, Sdl2PlatformWindow dup-drops, CursorManager + GameSaveBrowser dup-drops. 65 → 31 non-allowlisted sites. |
| `auto/build-warnings` | 2 | Trivial compiler warnings: 14 → 13 (1 SA1514 blank-line fix). Remaining warnings documented in report as user-judgment items (SA1013 needs static-field refactor, RCS1155/CA2231 are API changes). |
| `auto/yaml-faction-parity` | 5 | 4 YAML typo fixes (bmp2 Tooltip typo, A10 duplicate ReloadAmmoPool@1→@2, tunguska dangling pool ref, m113 dead Rearmable refs) + audit report flagging suspicious-but-not-typo drift between america/russia files. |
| `auto/pitfall-survey` | 6 | 5 `PITFALL` anchors at temptation sites: Move.cs MoveFirstHalf reverse-facing, GarrisonManager per-soldier suppression gate, HierarchicalPathFinder PassableClasses, RotateToEdge ChildHasPriority, AttackMoveActivity ignoreScanInterval. |
| `auto/discoveries-promote` | 4 | 6 patterns lifted from `WORKSPACE/DISCOVERIES.md` into CLAUDE.md (YAML traps, build workaround, engine debugging gotchas). CLAUDE.md 429 → 446 lines. |
| `auto/docs-drift` | 7 | 6 doc fixes redirecting AI-foundation / wakeup / capture-coordinator refs to their archive paths across `DOCS/gameplay/`, `DOCS/reference/`, `WORKSPACE/HOTBOARD.md`, `RELEASE_V1.md`, plans. 4 partial-rename cases flagged for user judgment. |

### Salvaged — work shipped, conductor wrote post-mortem report

| Branch | Commits | Summary |
|---|--:|---|
| `auto/dead-code` | 2 + report | Two surgical commits removing stale commented blocks: `AttackBase`/`AmmoPool` (Console.WriteLine + AutoRearm fragment), `Passable.cs` (3 abandoned RelationshipWith drafts) + `Cargo.cs` (empty stub). |
| `auto/linq-tickpath` | 2 + report | Two real perf wins: cache `AmmoPool` reference in `Armament` (was LINQ-scanning all traits per access on a render-frame hot path); drop per-frame `.ToArray()` allocation in `DrawLineToTarget` + `WithGarrisonDecoration`. |
| `auto/null-safety` | 1 + report | One well-justified null-guard in `AutoTarget.ChooseTarget` for `self.Owner.FrozenActorLayer` (`TraitOrDefault`, two other sites already guard it; exposure path real after AutoTarget dropped `Requires<AttackBaseInfo>`). |
| `auto/tests-math` | 6 + report | **94 new unit-test cases (~1300 LOC)** across 6 fixtures: SupplyRouteContestation, AbsorbsSupplyCache, HuskDecay, ThreatMap, CaptureCoordinator, SupplyProviderConditions. **Only 1 of 6 commits explicitly confirms tests green** — user must `dotnet test` before merging. |
| `auto/bugs-survey` | 2 (scaffold + report) | Complete autotest scenario for the "Drone autotarget of other drones broken" bug (`test-dr-jams-drone/`). Not run, no engine fix attempted — worker was killed mid-task. Ready for the user (or a fresh worker) to run + fix. |

### Pre-existing

- `auto/preserved-wip-260520` — captured 3 files that were uncommitted on main at run start (GroupScatter waypoint refactor, river-zeta map tree-thinning, EXPERIMENTAL_NOTES.md). User can cherry-pick from this branch.

### Pruned (zero commits, no salvageable artifact)

- `auto/autotest-sturdy` — pruned
- `auto/changelog-gen` — pruned
- `auto/lua-api-survey` — pruned
- `auto/release-v1-staleness` — pruned
- `auto/yaml-blanklines` — pruned

## Conductor's own log

### 17:00 — start
- Cleaned main: preserved 3 uncommitted files on `auto/preserved-wip-260520`.
- Tracking scaffold (this file) committed on main so all branches inherit it.
- First wave of 10 workers dispatched, one per `auto/<topic>` topic.

### 17:31 — Maestro crashed once (recovered)
- All 10 workers went idle simultaneously. User notified that the daemon had crashed. Workers nudged with `send_to_worker` "resume"; they came back up.

### 17:45–18:10 — first wave reported
- 6 branches finished cleanly with their own reports: pitfall-survey, discoveries-promote, yaml-faction-parity, build-warnings, console-cleanup, docs-drift.
- Conductor spawned a second wave of replacements: discoveries-promote, release-v1-staleness, lua-api-survey, autotest-sturdy, changelog-gen, yaml-blanklines.

### ~18:20 — Maestro daemon killed (CPU pressure)
- User terminated the daemon; the 10 then-active workers were killed mid-task.

### 18:21+ — salvage
- Audited every `auto/*` branch's git state.
- 4 branches (dead-code, linq-tickpath, null-safety, tests-math) had real committed work but no report → conductor wrote a salvage report on each.
- 1 branch (bugs-survey) had a complete uncommitted autotest scenario → conductor committed it on the branch + wrote a report.
- 5 branches (autotest-sturdy, changelog-gen, lua-api-survey, release-v1-staleness, yaml-blanklines) had zero commits → pruned worktrees + branches.
