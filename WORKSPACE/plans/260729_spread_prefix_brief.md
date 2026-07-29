# Spread-orders prefix-preservation fix — DO NOT MERGE brief

Branch `auto/spread-prefix` (off `main @ 4efe523f`). Written for an independent adversarial
reviewer. Impersonal register throughout.

## The contract

Group Scatter (Shift-G, `GroupScatterHotkeyLogic.PerformGroupScatter`) redistributes a player's
*group* orders across the selected units so they fan out instead of stacking on identical cells.
The intended contract, as asserted by `tools/autotest/scenarios/test-spread-preserves-prefix`:

- A unit's order chain can be `[unique prefix …][shared group suffix …]`. The **prefix** is the
  per-unit result of an *earlier* individual spread (or individual click); the **suffix** is the
  run of orders the player queued on the *whole selection* (so every unit holds it identically).
- Shift-G must redistribute **only the shared suffix** and leave **each unit's unique prefix
  intact**. The shared suffix is the **longest common suffix** across the participant chains,
  compared by `(Cell, OrderType)`.

Concretely, the test: TankA `[Move(8,10), AM(20,11), AM(20,13)]`, TankB `[Move(8,14), AM(20,11),
AM(20,13)]`. Expected post-fix chains: TankA `[Move(8,10), AM(20,11)]`, TankB `[Move(8,14),
AM(20,13)]` — each keeps its own prefix Move, the two shared AttackMoves are split one per unit.

## Decision branch taken: **Branch 2 — implement suffix-only substitution**

The task's decision rule defaults to implementing prefix preservation unless archaeology shows the
comment was aspirational **and** the spread mechanism fundamentally re-plans the whole queue *by
design*. Archaeology (below) shows the comment was aspirational but the design intent is clearly
preservation, not whole-queue re-planning. So Branch 2, not Branch 3.

### Evidence

- **The "longest common suffix" logic never existed in any commit.**
  `git log -S "Suffix" -- …/GroupScatterHotkeyLogic.cs` and `-S "common suffix" -- engine` both
  return nothing across all history. The test's "Post-fix: PerformGroupScatter computes the longest
  common suffix" comment described a fix that was *planned but never landed* — the code always did
  global-pool aggregation.
- **The broken global-pool aggregation predates the test by one day.** The aggregation
  (dedupe-by-`(Cell,OrderType)` into one pool → `BuildSegments` → broadcast lone segments) was
  already present at `65ac0e64` (2026-05-10), the latest GroupScatter commit before the test was
  authored at `e61f6826` (2026-05-11). The test's `.lua` was a pure addition at that commit (only
  the `map.bin`/`map.png` assets were true git renames), i.e. the test was written 2026-05-11
  asserting a behaviour the code did not implement.
- **The design trajectory is preservation of human intent, not re-planning.** Every GroupScatter
  commit tightens toward "only redistribute what the human actually issued" — drop `Enter`
  activities (`57c8c5ef`), only redistribute human orders not autotargets/nudges (`65ac0e64`),
  preserve attack-ground intent (`539d5ea3`), and the merge-tip `9935f54d` "distribute the human
  order points, re-issue grouped so cohesion re-spreads". Nothing re-plans the whole queue on
  purpose; the global pool is an incomplete implementation, not an intentional whole-queue re-plan.
  → Branch 3's trigger condition is absent.
- **Not a spread-orders regression** (confirms the discovered.md finding): `9935f54d` is the
  GroupScatter change inside merge `91949fe5`, and is an ancestor of the worktree base `4efe523f`;
  the current file still carries the global-pool aggregation. The test failed identically on the
  pre-merge base `e7a5ac96` and the tip `91949fe5` because the bug is older than that window.

## The fix

`engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/Hotkeys/GroupScatterHotkeyLogic.cs`

- **New pure helper** `CommonSuffixLength(IReadOnlyList<IReadOnlyList<(CPos Cell, string OrderType)>>)`
  (`GroupScatterHotkeyLogic.cs:180-214` post-edit). Longest common suffix by `(Cell, OrderType)`,
  bounded by the shortest chain; a single chain yields its own full length; null/empty → 0. No
  `World`/`Actor` dependency, so it is unit-testable.
- **New suffix-only path** in `PerformGroupScatter` (`GroupScatterHotkeyLogic.cs:104-138` post-edit),
  taken **only when `suffixLen >= 1 && hasUniquePrefix`** (some unit's chain is longer than the
  shared suffix). It: `Stop`s participants; re-issues **each unit's unique prefix to that same
  unit**, queued and in order (same per-unit `new Order(type, unit, target, queued:true)` that
  `DistributeSegment` already uses); then `DistributeSegment`s the shared suffix, queued behind.
- **Legacy global-pool aggregation retained as the fallback** (`GroupScatterHotkeyLogic.cs:141-172`
  post-edit) for the two cases where nothing needs preserving: `suffixLen == 0` (fully divergent
  chains — no shared group order to scatter) and `hasUniquePrefix == false` (every participant
  holds the same chain — the common basic case, where suffix == whole chain and the legacy path is
  behaviourally equivalent to the suffix path anyway).

**Blast radius:** the only behaviour that changes versus the base is the *(divergent-prefix +
shared-suffix)* case — the exact bug. Every other selection shape routes through the untouched
legacy path, so all currently-passing behaviour stays byte-identical.

Why the two passing sibling tests still pass (traced, not run — see run constraint below):
- `test-spread-cargo-no-enter`: 3 infantry with **identical** `[Move,Move]` chains (the `Enter`
  tail is filtered by `CollectWaypoints`). `hasUniquePrefix == false` → legacy path → unchanged.
- `test-spread-no-autotarget`: a **single** participant (InfB); the autotarget-only InfA
  contributes zero waypoints and is excluded. Single chain ⇒ `suffixLen == full`,
  `hasUniquePrefix == false` → legacy path → unchanged.

## NUnit coverage

`engine/OpenRA.Test/OpenRA.Mods.Common/GroupScatterSuffixTest.cs` (6 tests) pins the contract at the
helper level: empty/null/degenerate → 0; single chain → full length; identical chains → whole
chain; **divergent prefix + shared AttackMove suffix → 2** (the test scenario); fully divergent →
0; suffix bounded by shortest chain and stops at first mismatch; `OrderType` distinguishes
otherwise-identical cells. Worktree NUnit: **530/530 pass** (524 base + 6 new), 0 failed.

## How to interpret the deferred autotest run

`test-spread-preserves-prefix` is behavioural and **was NOT run** (a tournament ladder holds the
harness; per task constraints no game launch was performed from either checkout). Expected result
when the manager serializes it: **PASS**. Predicate (unchanged from the committed test):

```
TankA.X >= 18 && TankB.X >= 18 && TankA.Y <= 12 && TankB.Y >= 12   (checked at +35s)
```

Static trace of the fix against the test map (spawns TankA `(10,10)`, TankB `(10,14)`; suffix AMs
`(20,11)` north, `(20,13)` south): at scatter time no ticks have elapsed, so `DistributeSegment`
computes proximity from the spawn cells — TankA(10,10) is closer to AM(20,11) (dist² 101 vs 109),
TankB(10,14) closer to AM(20,13). Final chains TankA `[Move(8,10), AM(20,11)]` → settles ≈(20,11);
TankB `[Move(8,14), AM(20,13)]` → settles ≈(20,13). Predicate satisfied. If it FAILS, first suspect
the AM proximity assignment order or that a prefix Move was not re-issued queued.

## Residual risks for the reviewer to scrutinise

1. **Prefix re-issue fidelity.** The prefix is reconstructed from the harvested `Waypoint.Target`
   (`Target.FromCell` for terrain; the actor Target for actor-orders) and re-issued as
   `new Order(wp.OrderType, unit, wp.Target, queued:true)`. This mirrors `DistributeSegment`'s
   single-unit issue exactly, but a prefix order whose *original* target carried richer intent than
   a cell/actor Target (unusual for Move/AttackMove/Attack) would be re-issued in that reduced form.
   No such order type is currently harvested by `CollectWaypoints` (Enter is dropped), so this is
   latent, not live.
2. **Suffix payload taken from `allChains[0]`.** All chains share the suffix *by key* `(Cell,
   OrderType)`, but the `Target` objects are per-actor. For terrain orders these are value-equal;
   for an actor-target suffix the `Target.Actor` is the same actor, so `allChains[0]`'s copy is
   correct. Flagged in case a future order type makes per-actor Targets semantically diverge under
   an equal key.
3. **`unit.Location` proximity timing.** The fix relies (as the pre-existing code already did) on
   `world.IssueOrder` not physically relocating units within the synchronous
   `PerformGroupScatter` call, so suffix distances use spawn/current cells. True today; a change to
   order application timing would shift assignments.
4. **The `suffixLen == 1` sub-case** routes through the new path and "redistributes" a single shared
   trailing order — effectively a broadcast of that one order to every unit, with prefixes
   preserved. Intended and harmless, but noted as a boundary the tests exercise only indirectly.

## Files changed

- `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/Hotkeys/GroupScatterHotkeyLogic.cs` — helper +
  suffix-only path + retained legacy fallback.
- `engine/OpenRA.Test/OpenRA.Mods.Common/GroupScatterSuffixTest.cs` — new, 6 tests.
- `WORKSPACE/bugs/discovered.md` — 2026-07-24 entry status updated inline.
- `WORKSPACE/plans/260729_spread_prefix_brief.md` — this brief.
