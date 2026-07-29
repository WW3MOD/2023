# Adversarial review — `auto/spread-prefix`

Independent review (reviewer did not write the code). Base `main @ 4efe523f`. Commits under review:
`58d77760` (fix + 6 NUnit tests), `9b0239f6` (brief + discovered.md status). Brief:
`WORKSPACE/plans/260729_spread_prefix_brief.md`.

## Verdict: **MERGE-WITH-NITS**

The fix is correct, tightly scoped, and byte-identical for every non-bug selection shape. No blocking
findings. One merge **condition** and three nits below. My build/NUnit numbers reproduce the brief's.

The condition is not a defect in the code — it is the task's own constraint: the behavioural autotest
that actually exercises the gate + prefix path was **not run** (harness held by a ladder), and the 6
NUnit tests cover only the pure helper, not `PerformGroupScatter`. So *execution* evidence for the
behavioural fix is currently zero; my confidence rests on a static trace (below). **Gate final merge on
`test-spread-preserves-prefix` passing when the harness frees up.** My static-trace expectation: PASS.

## Claim-by-claim

| # | Claim (from brief / task) | Verdict | Evidence |
|---|---|---|---|
| a1 | `CommonSuffixLength` pure helper at ~:180 | **HELD** | `GroupScatterHotkeyLogic.cs:180-214`; no World/Actor dep; null/empty→0, single→full, bounded by shortest |
| a2 | Suffix-only path at ~:104 gated `suffixLen>=1 && hasUniquePrefix` | **HELD** | `:104`. Stops participants `:112-113`, re-issues each unit's unique prefix queued to that unit `:117-129`, then `DistributeSegment`s shared suffix `:132-134` |
| b1 | Legacy global-pool path retained as fallback at ~:141 | **HELD** | `:146-174`; aggregation/BuildSegments/Stop/DistributeSegment lines are byte-identical to base — diff shows only comment churn |
| b2 | ONLY the bug case changes behaviour vs base | **HELD** | New path is entered iff (shared suffix exists) ∧ (some unit has a prefix). Every such selection on base broadcast the prefixes = the bug. The diverted set == the bug set; no non-buggy shape is rerouted. See trace below. |
| c | NUnit 530/530 (524 base + 6 new) | **HELD** | My run: `Passed: 530, Failed: 0, Total: 530` (net6.0). Build: 0 warnings, 0 errors. |
| d | "longest common suffix" logic never existed in any commit; test post-dates broken aggregation by a day | **HELD** | `git log -S "Suffix"` / `-S "common suffix"` return **only** `58d77760`. Latest pre-test GroupScatter commit `65ac0e64` = 2026-05-10; test-move commit `e61f6826` = 2026-05-11. |
| — | `9935f54d` (spread-orders GroupScatter change) is an ancestor of base `4efe523f` | **HELD** | `git merge-base --is-ancestor` → YES. Confirms "not a spread-orders regression". |
| — | Hygiene: path-limited, no attribution trailers, main untouched | **HELD** | 2 commits touch only the 4 expected files; no co-author/generated trailers; main checkout clean, tip still `4efe523f`. |

## Adversarial trace of the gate (the load-bearing claim, b2)

- **Fully identical chains** (all length L, all match): `suffixLen == L`; `hasUniquePrefix = Any(c.Count > L)` → **false** → legacy path. Byte-identical to base. ✔
- **Fully divergent chains** (no shared tail): `suffixLen == 0` → gate false → legacy path. ✔
- **suffixLen == 0** and **suffixLen == chain length** boundaries both resolve to the legacy path, exactly as the brief states. ✔
- **Bug shape** (prefix + shared suffix, e.g. `[Move a][AM x][AM y]` / `[Move b][AM x][AM y]`): `suffixLen=2`, some chain longer → new path. On base this broadcast `Move a`/`Move b` to every unit (the reported bug); new path re-issues each prefix to its own unit only. ✔
- **Mixed selection, some units prefixless** (`U1=[Move a,AM x,AM y]`, `U2=[AM x,AM y]`, `U3=[AM x,AM y]`): `suffixLen=2`, `hasUniquePrefix` true (U1). New path: only U1 re-gets `Move a`; U2/U3 get `prefixCount=0`; suffix distributed across all three. On base, U2/U3 would have received U1's `Move a` (bug). Correct divert. ✔
- **Multi-order prefix** (`U1=[Move a,Move b,AM x]`, `U2=[Move c,AM x]`): `suffixLen=1`; prefixes re-issued in order per unit (U1 gets a,b; U2 gets c), lone AM broadcast. Correct. ✔
- **Shorter chain is a pure suffix of the longer** (`[Move a,AM x,AM y]` / `[AM x,AM y]`): `suffixLen=2`; suffix payload from `allChains[0]` is key- and (terrain) value-equal regardless of which chain is index 0. ✔
- **Single participant** (excluded siblings leave one unit): single chain ⇒ `suffixLen == full`, `hasUniquePrefix` false → legacy. New path never fires for one unit. ✔
- **Empty chains**: excluded at `:83-84` before `allChains.Add`, so production `allChains` never holds an empty chain; the helper's null/empty guards serve the NUnit callers and are defensive. ✔
- **Stop-then-queue semantics**: identical mechanism to the legacy path (`Stop` queued:false, then queued:true re-issues) and to `DistributeSegment`'s per-unit issue (`new Order(type, unit, target, true)`). Since the legacy path and both passing sibling autotests rely on the same mechanism, it is sound; the new path introduces no novel order shape. ✔
- **Null-OrderType guard** in prefix re-issue (`:124`): dead in production (every harvested `Waypoint` has a non-null `OrderType`; `Enter` yields a null `Waypoint?`, dropped in `CollectWaypoints`), harmless, mirrors `BuildSegments:393`. ✔

## Static trace — deferred autotests

**`test-spread-preserves-prefix`** (`.lua` verified independently — setup & predicate match the brief):
chains `TankA=[Move(8,10),AM(20,11),AM(20,13)]`, `TankB=[Move(8,14),AM(20,11),AM(20,13)]` →
`suffixLen=2`, new path. Prefixes preserved (A→Move(8,10), B→Move(8,14)); suffix `[AM(20,11),AM(20,13)]`
distributed by `unit.Location` proximity from spawns (A(10,10)→AM(20,11) dist² 101<109; B(10,14)→AM(20,13)).
Final `TankA=[Move(8,10),AM(20,11)]`, `TankB=[Move(8,14),AM(20,13)]`. Predicate `X>=18 ∧ TankA.Y<=12 ∧
TankB.Y>=12` at +35 s → **expected PASS**, contingent only on the map being clear enough for the ~14-cell
traverse to settle in 35 s (author-calibrated). If it FAILS, suspect AM proximity assignment or a
prefix Move not re-issued queued — not the gate.

**`test-spread-cargo-no-enter`**: 3 infantry with identical `[Move(28,13),Move(28,19)]` (Inf1's
`RideTransport` is `Enter` → filtered); BMP contributes 0 waypoints → excluded. `hasUniquePrefix=false`
→ legacy path → **unaffected**. ✔
**`test-spread-no-autotarget`**: InfA autotarget-only → excluded; InfB single chain → `hasUniquePrefix=false`
→ legacy path → **unaffected**. ✔

## Nits (non-blocking)

1. **NUnit covers the helper only, not the gate/prefix path.** The 6 tests pin `CommonSuffixLength`
   geometry well but never touch `suffixLen>=1 && hasUniquePrefix`, the Stop→re-issue, or
   `DistributeSegment`. A regression of the gate (e.g. `>=1`→`>=2`, or dropping `hasUniquePrefix`) or of
   the prefix re-issue would stay **green** in NUnit and be caught only by the deferred behavioural
   autotest. "530/530" therefore does **not** by itself validate the behavioural fix — the brief is
   honest about this ("pins the contract at the helper level"). Acceptable, since `PerformGroupScatter`
   needs a live World/Actor; just don't over-read the green.
2. **Actor-target suffix collision** (brief residual #2). `CommonSuffixLength` keys on `(Cell, OrderType)`
   and the suffix payload is taken from `allChains[0]`. Two units whose trailing `Attack` orders target
   **different** actors that share a Location cell would be treated as a shared suffix and both re-issued
   against `allChains[0]`'s actor (the other target lost). **Not a regression** — base's legacy
   dedupe-by-`(Cell,OrderType)` collapses them identically — and it requires two distinct actors on one
   cell plus per-unit manual attack orders. Latent, honestly flagged.
3. **Suffix proximity uses current `unit.Location`, ignoring the prefix's future destination**
   (pre-existing `DistributeSegment` behaviour, unchanged). A unit's suffix target is chosen from where it
   physically sits at scatter time, not where its preserved prefix will take it. Harmless for the test
   (both tanks already sit near their AMs) and not introduced by this change.

## Build / test numbers (run inside the worktree)

- `make all` → `0 Warning(s) / 0 Error(s)`, exit 0.
- `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release` →
  **Passed: 530, Failed: 0, Skipped: 0, Total: 530** (net6.0), exit 0.
- Main checkout (`/Users/fredrik/Desktop/WW3MOD`) not modified; tip still `4efe523f`; only untracked
  `worktrees/` etc. Game / run-test / run-batch were **not** invoked.

## Bottom line

Correct, minimal, well-archaeologised fix; hygiene clean. Merge once the behavioural
`test-spread-preserves-prefix` is run and passes (static-trace expectation: PASS). Nits are advisory.
