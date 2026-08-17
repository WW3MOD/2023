# Build & test health audit — 2026-08-16

**Repo state audited: `main` @ `55459146`** (clean tree, in sync with `origin/main`, verified via `git status -sb`).
Read-only audit. No code or content was changed. The game was running throughout (OpenRA pid 97376);
no scenario was launched and no autotest was run.

## Status board — measured, not quoted

| Gate | Status | Evidence |
|---|---|---|
| `./make.ps1 all` | **GREEN** — exit 0, 2 warnings, 0 errors | measured, this session |
| `dotnet test ... --configuration Release` | **GREEN** — 1481/1481 passed, 0 failed, 0 skipped | measured, this session |
| `make test` (YAML lint) | **RED** — exit 1, 623 error lines / **8 distinct defects** | measured, this session |
| `make nav-guard` | **GREEN** — 10 maps, 190 map/locomotor pairs match baseline | measured, this session |
| Autotest suite | **UNKNOWN — no machine-readable record exists in the repo** | measured (see F5) |

---

## F1 — **[BLOCKER]** `make test` is red on main, and 9 of the 10 shipped maps are the cause

**Confirms the parallel audit.** The guard rail the next session wants to use as a merge gate is
already failing, so it cannot gate anything: every merge would be "red before and red after".

Suffering if it stays: internal only in gameplay terms (the cordon rule is a validator requirement,
not an observed in-game fault), but it is release-blocking as *process* — a permanently-red gate
trains everyone to ignore the one check that would catch a real content regression.

Evidence — measured output of `.\make.ps1 test`:

```
OpenRA.Utility(1,1): Error: This map does not define a valid cordon.
A one cell (or greater) border is required on all four sides between the playable bounds and the map edges.
Errors: 623
EXITCODE=1
```

**All 9 playable maps fail the cordon check** (attributed by walking the log and binding each error to
the preceding `Testing map:` line). `shellmap-open-field` is the only shipped map that passes:

`Arena: Tank Duel`, `Nuclear Winter WW3`, `Polar Disorder WW3`, `River Zeta WW3`, `Seventh Woods WW3`,
`Siberian Pass WW3`, `Twin Rivers WW3`, `Woodland Warfare WW3`, `X-Lake WW3`.

I did **not** open `siberian-pass-ww3/map.yaml:13` myself — I attributed by map *title*, not by file.
The parallel audit's specific line cite is consistent with my measurement and I have no reason to
doubt it, but treat the exact line number as theirs, not mine.

Already filed, and honestly: `4f67b375` (2026-08-15) — *"bugs: make test has been red on main since the
map-bounds expansion"*. Its own body says **"Recorded rather than fixed"**, names the same cause
(`Bounds == MapSize`, no cordon), and states the cost: *"CLAUDE.md points every worker at a check that
is already failing."* That commit touches only `WORKSPACE/bugs/discovered.md` (+36 lines, verified via
`git show --stat`). So this is a known, unfixed, correctly-diagnosed defect — the audit's job here is
just to confirm it is still live at HEAD. It is.

- Confidence: **high** (measured directly)
- Fix size: **hours** — but it needs the decision `4f67b375` names first: re-cordon the maps, or waive
  the lint deliberately. Do not let a worker pick one silently.

## F2 — **[SHOULD-FIX]** The top commit on main (`55459146`) added 3 new distinct lint errors

The `has-gunner-seat` fix merged at HEAD made `make test` *redder* the same day another commit filed a
bug about it being red. 534 of the 623 error lines are new as of this merge.

```
Error: Actor type `littlebird` consumes conditions that are not granted: has-gunner-seat.   (×179)
Error: Actor type `tran`       consumes conditions that are not granted: has-gunner-seat.   (×179)
Error: Actor type `halo`       consumes conditions that are not granted: has-gunner-seat.   (×176)
Error: Actor type `halo`       consumes conditions that are not granted: crash-disabled,
       has-gunner-seat, autorotation, crash-landing, rotor-stopped.                          (×3)
```

This is **collateral of a correct gameplay fix**, not a gameplay bug. `mods/ww3mod/rules/ingame/aircraft.yaml:294-296`
gates `FirepowerMultiplier@NoGunner` on `has-gunner-seat && !has-gunner`, and `has-gunner-seat` is granted
only by actors that *declare* a Gunner slot (`aircraft-america.yaml:299-300`, `aircraft-russia.yaml:105-106`
and `:299-300`). The littlebird/tran/halo deliberately have no gunner seat — that is the whole point of the
fix, documented at length in the PITFALL at `aircraft.yaml:278-293`. The lint has no way to express
"consumed on a base template, granted only by some children", so it errors.

What a player suffers: nothing. What the release suffers: the lint signal is now 86% noise by volume.

- Confidence: **high** (measured; grant sites read directly)
- Fix size: **minutes to hours** — either move the multiplier onto the gunner-equipped actors, or add a
  no-op grant, or waive. Same decision-shaped problem as F1.

## F3 — **[SHOULD-FIX]** The lint error *count* is meaningless; only the deduplicated list is usable

Verified the knowledge-base claim, and it is **correct**. The validator lints the full ruleset once per
map, and 185 maps are tested (`TOTAL_MAPS_TESTED=185`, counted from `Testing map:` lines). A single
rules-level defect is therefore reported up to 179 times. 623 error lines collapse to **8 distinct defects**:

| # | Distinct defect | Lines | Where |
|---|---|---|---|
| 1 | `has-gunner-seat` consumed, never granted (littlebird / tran / halo) | 534 | rules — see F2 |
| 2 | `halo` also missing crash-disabled, autorotation, crash-landing, rotor-stopped | 3 | rules |
| 3 | No valid cordon (1-cell border) | 69 | 9 shipped maps + ~60 test/tournament maps — see F1 |
| 4 | `Multi0`–`Multi5` must specify `LockFaction: True` | 6 | `DIAG: Cohesion on actual river-zeta` |
| 5 | Bot player enemy-lists reference invalid players (`USA-bot`/`Russia-bot`) | 4 | `demo-experimental-capture-coordinator` |
| 6 | `OwnSR`/`OpponentSR` owned by unknown players | 4 | `demo-experimental-capture-coordinator` |
| 7 | Map allows 2 players but defines 1 spawn point | 2 | `test-supply-crate-rearm`, `test-idle-low-ammo-seeks-supplies` (by title) |
| 8 | `CheckFluentReferences` lacks know-how for `ResourceTypeInfo.Name` | 1 | engine lint limitation, mod-level |

Note also **43,867 warning lines** in the same run (mostly "grants conditions that are not consumed" —
the damage-state and stance conditions on nearly every actor). Not errors, not gating, but they bury
the 623 error lines completely. Anyone eyeballing this output will not find the errors.

Defects 4–7 are in **test/demo fixtures, not shipped content** — a player never sees them.

- Confidence: **high** (measured, attributed programmatically)
- Fix size: 4–7 are **minutes** each. 1–3 are the real work.

## F4 — **[POLISH]** nav-guard covers 100% of shipped maps, not "10 of 167"

The docs' framing makes coverage look like 6%. Measured: `mods/ww3mod/maps/` contains exactly **10**
directories, and nav-guard's `baseline.json` `states` block names exactly those 10
(`arena-tank-duel, nuclear-winter-ww3, polar-disorder-ww3, river-zeta-ww3, seventh-woods-ww3,
shellmap-open-field, siberian-pass-ww3, twin-rivers-ww3, woodland-warfare-ww3, x-lake-ww3`).
Coverage of player-facing maps is **10/10**.

The 167-ish remainder are autotest scenario maps (175 `map.yaml` under `tools/autotest/scenarios/`,
185 repo-wide). Those genuinely are uncovered — a blocking-rule change that seals a *test* map surfaces
as a confusing autotest failure rather than a nav-guard error. Real, but low stakes.

```
nav-guard selftest: ok
nav-guard OK: 10 maps, 190 map/locomotor pairs match baseline in both the authored and all-husks world states.
```

- Confidence: **high** — Fix size: **minutes** (a doc sentence)

## F5 — **[BLOCKER]** There is no machine-readable record of autotest pass/fail anywhere in the repo

This is the finding I would most want fixed before a release, and it is why item 3 of the brief cannot
be answered properly by anyone, not just by me.

**Verified:** `git ls-files tools/autotest | grep -E 'result|\.json$'` returns **nothing**. `.gitignore:44`
excludes `/tools/autotest/tournament-results/` and `:56` `/tools/autotest/tournament-loops/`. Per-run
verdicts land in `~/.ww3mod-tests/screenshots/<run_id>/result.json` — outside the repo, local to one
machine, overwritten. So the only record of what passes is **prose in WORKSPACE**, hand-maintained.

Consequence: "the suite is green" is not a checkable statement. Nobody can diff this week's results
against last week's, and the tally below is the only artifact that exists.

- Confidence: **high** — Fix size: **hours** (commit a results ledger, or have `run-batch.sh` write one)

### The inventory (measured by directory scan)

`tools/autotest/scenarios/` holds **175** directories:

- **129** `test-*` — the regression suite
- **31** `tournament-*`
- **14** `demo-*`
- **1** `wip-*` — **`wip-transport-delivers`**, the only parked scenario (parked by `fe692f17`,
  *"park the transport delivery scenario as wip-* until it goes green"*)

Discovery is a directory scan, not a manifest: `run-batch.sh:117` enumerates `tools/autotest/scenarios/test-*/`,
`run-test.sh:354` resolves `MAP_DIR="tools/autotest/scenarios/${TEST_NAME}"`.

**Note the arithmetic problem:** the suite has **129** `test-*` scenarios but the standing tally covers
**68**. Even at its own date the tally was a partial sweep, and nothing records which 68.

### Last recorded state — 2026-08-10, now 6 days and **298 commits** stale

`git log --oneline --since=2026-08-10 | Measure-Object` → **298**.

Quoted, `WORKSPACE/HOTBOARD.md:43`: *"Regression tally 2026-08-10: 60 pass / 8 fail (none traceable to
the two engine merges)"*. Same figure at `WORKSPACE/PIPELINE.md:203`, `WORKSPACE/AWAITING-USER.md:64`,
and `WORKSPACE/plan-260810-post-measurement.html:91`.

`AWAITING-USER.md:68` admits the gap: *"One is already attributed… The remaining seven have not been
triaged."* **The 8 failing scenarios are not enumerated anywhere.** Five are identifiable from
surrounding prose; three are not named in any document I found.

Per-scenario follow-up (`git log --since=2026-08-10 --grep=<name>`, verified):

| Scenario | Fix claimed since 08-10? |
|---|---|
| `test-offense-ammo-guard` | No fix. Premise recorded as **stale/pre-existing** (`418e9c60`). Touched incidentally by `779d0b62`, `65d19a8b`. |
| `test-autotarget-preempt-air` | **No.** `f910ac7d`: *"the control does not go RED — this test does not discriminate the fix"*. `97746a1d`: *"a green run is not evidence unless something could have made it RED"*. The test is broken, not the feature. |
| `test-supply-far-front-reached` | **No commit mentions it since 08-10.** `PIPELINE.md:203` claims it *"passes for the first time"* at `377085db` — undated relative to the tally, unverified. |
| `test-savegame-resume-riverzeta` | **No — explicitly still red.** `227ba2b4`: *"the Detectable sync fix does not fix the restore desync — verified RED"*; `c440906e` merges that finding. |
| `test-stance-optout` | Partial. `a4d85b0c` fixed three stance scenarios that *"were disabling the trait they test"*; `08b78a50` separately files this one's **false green**. No re-run recorded. |
| 3 unnamed | Cannot report — not enumerated in any document. |

**Nothing has been re-run since.** Every "fixed" claim above is a code change plus a prediction, not a
green result.

## F6 — **[BLOCKER]** The 2026-08-15 harness commits did **not** fix anything — they filed bugs

Correcting the brief's premise, which matters because it changes what is left to do. All three touch
**only** `WORKSPACE/bugs/discovered.md` (verified with `git show --stat`):

```
67bceb4d  bugs: demos die at 300s to a watchdog waiting for a verdict they never write   +24  discovered.md
591fd98c  bugs: run-demo.sh's exit-3-means-success mapping is unreachable under set -e    +22  discovered.md
67a9721c  bugs: Restart drops out of harness scenarios and ends the run                   +35  discovered.md
```

`67bceb4d`'s body says so outright: *"Recorded, not fixed."* The class is **catalogued, not swept**.
All three defects are live in the tree at `55459146`. Verified by reading the code:

**`run-demo.sh`** — `:17` `set -e`, then `:51` calls `run-test.sh` as a bare command, `:52` `rc=$?`,
`:54-56` maps exit 3 → 0. Under `set -e` the script **dies at :51**; lines 52-57 are unreachable. And
because demos never write a verdict (`:11-12`), the 300s watchdog always fires and synthesizes a FAIL.
Both filed bugs, both confirmed present. Confidence: **high**.

## F7 — **[SHOULD-FIX]** `run-tournament.sh` exits 0 even when every match crashed

Verified at `tools/autotest/run-tournament.sh:373-377`:

```sh
if [ ${OK} -gt 0 ]; then
    "${REPO_ROOT}/tools/autotest/aggregate-tournament.sh" "${RESULT_DIR}"
fi
exit 0
```

`FAIL` is counted at `:363` and printed at `:368`, then discarded. A run where `OK=0, FAIL=N` still
exits 0. Worse, `:358` decides "ok" purely from `[ -f "${MATCH_RESULT_FILE}" ]` — **a file existing**,
never the status inside it. Any caller judging by `$?` reads success from a total wipeout.

Internal only (no player impact), but this is exactly the self-deception shape of F6.

- Confidence: **high** (read directly) — Fix size: **minutes**

## F8 — **[SHOULD-FIX]** 7 scenarios named `test-*` cannot fail, and `--all` counts them as passes

Measured by stripping Lua comments and looking for any failure path (`Test.Fail` or `Assert*`):

```
test-case01-forest-ambush        test-case01b-detect         test-cohesion-river-zeta-actual
test-experimental-poi-observe    test-frontline-reachability test-game-clock
test-screenshot-smoke
```

Each reaches `Test.Pass()` from a bare `Trigger.AfterDelay` with no predicate — e.g.
`test-experimental-poi-observe.lua:15-17`, whose own header (`:1`) is honest: *"BOUNDED OBSERVATION
(not an assertion)"*. Honest in the file, **dishonest in the aggregate**: `run-batch.sh:145-146` filters
on the presence of `Test.(Pass|Fail|Skip)|Assert(Within|After)`, these match, so all 7 are included in
`--all` and land in the `Pass:` tally. So ~10% of any reported pass count is structurally guaranteed.
This is the `a4d85b0c` failure mode ("scenarios disabling the trait they test") one level up.

*Caveat on my own method:* `test-game-clock` and `test-screenshot-smoke` are plausibly legitimate smoke
tests where "it ran without crashing" is the whole assertion. Treat the list as 7 candidates, ~5 of
which are real.

*Cross-checked:* two independent implementations of this scan — one in PowerShell regex, one in
`sed`/`grep` — returned the identical set of 7, so the membership of the list is not an artifact of one
comment-stripping approach.

- Confidence: **high** on the mechanism and on the list; **medium** on which of the 7 deserve renaming
- Fix size: **minutes** — rename to `observe-*` so `--all` skips them

## F9 — **[SHOULD-FIX]** `make` is not installed; half of CLAUDE.md's documented commands do not run here

`Get-Command make` → not found. `./utility.sh --check-yaml` refuses independently:
*"The OpenRA mod SDK requires make."* So on the user's primary machine, `make test`, `make nav-guard`
and `make check` as written in CLAUDE.md **all fail instantly**.

The Windows equivalents exist and work — `.\make.ps1 test` (`make.ps1:433-435`), `.\make.ps1 nav-guard`
— and I used them for every measurement above. But CLAUDE.md's routing table sends workers at the
`make` form. Internal only; costs a worker one confused cycle each time.

Related, measured the same way: **`luac` is not installed either**, so `make check-scripts` (the Lua
syntax check over every scenario's `.lua`) has never run on this machine and would fail immediately
with *"'luac' not found."* (`Makefile:175-177`). Given that 129 scenarios are driven by Lua, that is
a real uncovered surface, not just a missing binary. `ww3-dev.ps1` is present and intact.

- Confidence: **high** (measured) — Fix size: **minutes** for the doc line; **minutes** to install `luac`

## F10 — **[COSMETIC]** Build warnings are 2 NuGet advisories, and the build was incremental

```
warning NU1901: Package 'NuGet.CommandLine' 6.12.1 has a known low severity vulnerability
    2 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.94
EXITCODE=0 ELAPSED=5.2s
```

Both warnings are the same NU1901 advisory, restore-level, not code. Zero C# compiler warnings.

> **STALE as of 2026-08-17 — do not spend the "minutes" this finding asks for.** A from-scratch `make all`
> on macOS at `main @ f5998c6d` reports `0 Warning(s), 0 Error(s)` and **zero** `NU1901`, and `NU1901`
> appears zero times in CI runs `31981227086` / `31978609314`. The advisory has aged out; there is nothing
> to bump. This also answers the caveat below: the from-scratch Release warning count is **0**. That says
> nothing about the analyzers — `engine/Directory.Build.props:50-55` strips them in Release, and the Debug
> analyzer build reports **106 errors** in CI (see `DISCOVERIES.md`, 2026-08-16 determinism sweep entry).

**Caveat I want on the record:** the run completed in 5.2s with *"All projects are up-to-date for
restore"* — this was an **incremental** build against an already-warm tree, so it proves the tree
compiles but does **not** establish a from-scratch warning count. I deliberately did not force a clean
rebuild: the game was running and I was not willing to risk disturbing the user's live session over a
warning tally.

- Confidence: **high** on "0 errors, builds"; **low** on "2 is the true warning count"
- Fix size: **minutes** (bump NuGet.CommandLine) / **unknown** for the real warning count

## F11 — **[POLISH]** Nine scripts in `tools/autotest/` have no caller anywhere outside WORKSPACE prose

Measured per script with `git grep -l <name>` in two scopes — outside `WORKSPACE/` and `tools/autotest/`
(`extRefs`), and within `tools/autotest/` excluding the file itself (`toolRefs`). Zero in both means
nothing in the shipped toolchain or in `DOCS/` invokes it:

```
analyze-hellfire.py   analyze-sweep.py   compare-batches.sh   lua-balance.py
parse-floor-traj.py   parse-s2-bar.py    parse-tecn-batch.py  poll-copy-logs.sh
tournament-report.sh
```

These look like one-shot analysis leftovers from past investigations (missile work, tournament sweeps).
Internal only — no player impact, and no risk in leaving them. The cost is that a future worker cannot
tell a live tool from an abandoned one, and `tools/autotest/` is the directory workers are pointed at
most often.

Two corrections to a subagent's earlier claims, both of which I re-measured and **contradict**:
`parse-s2-bar.py` is *not* called by `loop-tournament.sh` (0 refs inside `tools/autotest/`), and there
is **no committed `__pycache__`** anywhere in the repo (`git ls-files` for `__pycache__|\.pyc$` returns
nothing). The uncommitted `tools/nav-guard/__pycache__/` I saw on disk is untracked build litter.

- Confidence: **medium** — a dynamically-constructed invocation would evade a literal-name grep
- Fix size: **minutes** (delete, or move to `tools/autotest/attic/`)

---

## What I did NOT get to — do not mistake these gaps for clean results

Audit cut short at the user's request (machine reboot). Unfinished:

1. **A clean-tree build warning count.** Only an incremental build was measured (F10). A `make clean`
   rebuild could surface compiler warnings this run cannot see.
2. ~~Dead-script sweep of `tools/autotest/`.~~ **Completed after the cut-off — see F11.** Nine
   unreferenced scripts, measured. The `__pycache__` claim was wrong and is retracted there.
3. **The remaining harness self-deception sweep.** F6/F7 are verified. A subagent additionally flagged
   `run-test.sh:791` (crash detection reachable only in the no-result branch, so a run that passes and
   then throws during shutdown still reports PASS) and `run-batch.sh:145` excluding the two
   non-Lua savegame scenarios from `--all`. I read the `run-batch.sh` filter and confirm it is
   Lua-only **and announced** (`:153-157`), so that one is a coverage hole, not a false verdict.
   The `run-test.sh:791` claim I did **not** verify.
4. **Counter-evidence worth noting:** the same subagent reports `run-test.sh` defines nine distinct
   outcomes and **defaults `OUTCOME` to `HARNESS-ERROR`** (`:220`), narrowing only when something is
   actually determined, and that `GameSaveRoundTripProbe.cs:92` writes `skip` rather than `pass` when
   game saves are disabled. If true, the harness's core is better than F6/F7 suggest and the rot is at
   the edges (demo/tournament wrappers). I did not verify this either — but it is the thing to check
   before anyone concludes the harness is broadly untrustworthy.
5. **Per-scenario current state for all 129 `test-*` scenarios.** Impossible without running the suite,
   which is user-gated and was out of scope. F5 is the structural reason nobody else can answer it
   either.
6. **`siberian-pass-ww3/map.yaml:13` specifically** — I attributed cordon failures by map title from
   lint output, not by opening map files.
