# AUTOTEST — Automated test-driven debug loop

**Trigger:** the word `AUTOTEST` in a user message — explicit (`AUTOTEST <bug>`), batch (`AUTOTEST after I decide`, `AUTOTEST these items`), or simply naming the workflow. **The trigger establishes a stance for the whole batch of fixes that follows, not just the first one.** Each item runs the full RED → fix → GREEN cycle; the trigger word doesn't need to be repeated.

**Apply automatically (no trigger required) when** the work fits the loop. Quick checklist before declaring a behavioral fix done:

1. Did this change behavior that could be observed in-game (firing, moving, ammo, conditions, kills, …)?
2. Could a deterministic Lua predicate verify it (`AssertWithin`, ammo-drop, `IsDead`, etc.)?
3. Is the change non-trivial (more than a typo / single-value tweak / removed dead code)?

**Yes / yes / yes → write the test BEFORE the fix.** RED → fix → GREEN → commit together. This is the default for behavioral fixes in RELEASE mode.

**Gives you:** a deterministic test, RED-then-GREEN proof of the fix, regression coverage going forward, and a commit you can read days later and trust. You can walk away while it runs — the verdict comes back as a JSON exit code.

**When *not* to use it:** visual / "feels off" / tuning bugs (your eyes are faster than my trace dumps — use **PLAYTEST**), trivial code (one-line typo, value tweak), no-code-change work (docs, refactor without behavior change), or **"show me X in game" requests where you want to look around yourself** — that's **DEMO**, not AUTOTEST. AUTOTEST loops to a verdict; DEMO stages and stops.

---

## What the harness is

The game can be launched into a small, deterministic scenario; the verdict (pass/fail/skip) is written to a JSON file and exit-coded back, so I can iterate without supervision. Activated only by the `Test.Mode=true` launch arg — normal launches are completely unaffected.

## Quick reference

```bash
./tools/autotest/list-tests.sh                          # what's available
./tools/autotest/run-test.sh <test-folder>              # run one (centered, background, muted)
./tools/autotest/run-batch.sh <t1> <t2> ...             # run several
./tools/autotest/run-batch.sh --all                     # run every test-* folder
./tools/autotest/run-test.sh L <test>                   # left half (also R, F, C)
./tools/autotest/run-test.sh --visible <test>           # foreground (alias: --no-minimize)
./tools/autotest/run-test.sh --audio <test>             # keep sound on
./tools/autotest/run-test.sh --speed 8 --timeout 900 <test>   # long scenario: see below
./tools/autotest/run-test.sh --map <shipped-map> <test>  # run a SHIPPED map on this scenario's rig
./tools/autotest/run-test.sh --help                     # flag list
```

**`--map` exists because `Launch.Map` resolves by map DIRECTORY NAME as well as by UID**, and that
second disjunct is easy to miss: `Game.LoadMap` matches
`m.Uid == launchMap || Path.GetFileName(m.PackageName) == launchMap` (`Game.cs:1222`), so
`Launch.Map=nuclear-winter-ww3` works with no UID lookup anywhere. `run-test.sh` has always relied on
it — it passes the scenario folder name straight through — but it also hard-requires
`tools/autotest/scenarios/<name>` to exist, so before the flag the runner could reach every scenario and
**no shipped map**, which is why nothing we ran had ever loaded `mods/ww3mod/maps/`. `--map` overrides
only the `Launch.Map` value while the scenario directory keeps supplying the run rig (test name,
description, result path, screenshot dir) — one flag instead of ten near-identical stub scenarios.

**It validates the name itself (`run-test.sh:573-580`) rather than letting the engine resolve it, and
that is not defensiveness:** `Game.LoadMap` *throws* `ArgumentException("Could not find map")` on a
miss, so a typo in the caller would surface as a process crash and be graded `CRASH` — **a caller's
typo reported as the very bug a smoke gate exists to detect.**

**Second-order consequence worth carrying past this flag: "can the harness LOAD it" and "can the harness
reach a VERDICT on it" are separate questions, and the second decides whether a gate is possible at
all.** A shipped map carries no Lua, so it can never reach `Test.Pass` and a `--map` run has no route to
a verdict on its own. `Test.SmokeTicks=<N>` plus the `SmokeTestExit` world trait (inert unless both
`Test.Mode=true` and the arg are set) exists to close exactly that gap, and is what `make.ps1 smoke`
runs.

**Window placement & focus.** Default: **centered, ~90% × ~85%, background, muted**. "Background" means the window is visible at full size but immediately defocused so your terminal/editor keeps focus. Cmd+Tab to OpenRA brings the window forward when you want to look at it. After the game exits, focus is restored to whatever app was frontmost at launch — no random focus shuffle. If the user includes `L`, `R`, or `F` in the trigger ("AUTOTEST L", "AUTOTEST <bug> R"), pass that letter through as the first positional arg to `run-test.sh`. `L`=left half, `R`=right half, `F`=fullscreen. Pass `--minimized` to opt back into the old SDL miniaturize behavior.

Exit codes: `0` pass, `1` fail, `2` skip, `3` error/crash/no-result.

**A scenario that is SUPPOSED to fail must say so, or it reds every batch forever.**
`run-batch.sh --all` globs every `test-*` folder and includes any scenario containing an
assertion — its only exclusion catches scenarios with no verdict call at all. So a by-merit
negative ("we measured it and it is within tolerance", or a knowingly-unfixed layer) becomes a
permanent false FAIL in every regression tally, which is precisely how a red batch stops meaning
anything. Declare the outcome instead, in the scenario's own folder:

```
tools/autotest/scenarios/test-<name>/expected-status
----------------------------------------------------
fail
The negative arm is by merit: the preference has not landed yet. Delete this file
when it does and the run goes green.
```

First non-comment line is `fail` or `skip`; the reason below it is **required**. The declared
outcome occurring is green and prints as `OK(fail)`. **The declared outcome no longer occurring is
RED and prints as `STALE`** — that asymmetry is the whole point, and it is the same one
`mods/ww3mod/lint-baseline.txt` implements deliberately: a floor you can only lower on purpose.
A declaration buys silence for exactly the one outcome it names and nothing else, so a scenario
declared `fail` that starts *crashing* still reds. Decision table and a launch-free selftest:
`./tools/autotest/expected-status.sh --selftest`.

**A declaration is only ever satisfied by a scenario that reached a verdict under its own power.**
A hang, a crash, a Ctrl-C, a run whose rules never loaded: all red under any declaration, listed
in the summary under `!! NEVER REACHED A VERDICT`. Until 2026-09-02 a *hang* was the exception and
graded green — `run-batch.sh` derived the outcome from the exit code, and exit 1 means both
"the scenario answered no" (`FAIL`) and "the watchdog killed it" (`TIMEOUT-FAIL`), so a declared
`fail` reported `OK(fail)` for a run that never happened. It now grades on the OUTCOME NAME,
carried out of `run-test.sh` in `AUTOTEST_OUTCOME_FILE` (written from the same EXIT trap as the
banner, so no exit path skips it). If that name is missing or disagrees with the exit code you get
`NO-OUTCOME` / `OUTCOME-MISMATCH` — red, never a fallback to the exit code, because the fallback
*is* the bug. The end-to-end proof is in `selftest.sh`; the decision table alone was green
throughout the entire life of the defect, which is why the check lives at the batch level.

This is strictly better than an opt-out marker, which is why there isn't one: a scenario excluded
from `--all` stops reporting, so if it later breaks in a new way nobody hears.

**Read the verdict from the banner, not just the exit code.** Every run ends with a line

```
AUTOTEST_VERDICT outcome=<OUTCOME> exit=<n> test=<name> run=<run-id>
```

where OUTCOME is one of `PASS`, `PASS-EMPTY`, `FAIL`, `SKIP`, `TIMEOUT-FAIL`, `CRASH`, `NO-RESULT`, `BAD-VERDICT`, `INTERRUPTED`, `HARNESS-ERROR`. It distinguishes what the exit code collapses: `CRASH` (the game threw — the exception log is named, and a crash is sometimes the *finding*, as when a sync guard fires) vs `NO-RESULT` (hung or closed by hand) vs `HARNESS-ERROR`; and `TIMEOUT-FAIL` (never answered) vs `FAIL` (answered no).

**`NO-RESULT` also covers "the game never launched", and a fresh worktree hits this on its first run.** `launch-game.sh:42` aborts with `Required engine files not found.` when `engine/bin/OpenRA.dll` is missing — and build output is neither shared between worktrees nor tracked in git, so a new `git worktree add` fails this and burns a granted run slot. **Run `make all` in a new worktree before the first `run-test.sh`, even when the diff contains no compiled code** — being built is a property of the worktree, not of the change. Tells: `lua.log` 0 bytes, run dir empty, `test -f engine/bin/OpenRA.dll` fails. (Related, and launch-free: `./utility.sh --check-yaml <MAPDIR>` lints a single map without starting the game, but `utility.sh:53` `cd`s into `engine/` first, so the path you pass is `../tools/autotest/scenarios/<name>`.)

**PITFALL: `run-test.sh <test> | tail` reports `tail`'s exit status, so a FAIL arrives as exit 0.** This has inverted a result twice. The harness defends what it can — the verdict line is last, so a tail-truncating filter still shows it, and non-PASS is also written to stderr whenever stdout is redirected — but the exit code itself is the **caller's** to preserve:

```bash
./tools/autotest/run-test.sh <test>; rc=$?      # capture first, filter after
```

**Results are per-run.** Each invocation writes to `~/.ww3mod-tests/screenshots/<timestamp>_p<pid>_<test>/result.json` — printed as `Run dir:` at the top of the run — alongside that run's screenshots and lifecycle log. Two runners cannot share a destination.

`./tools/autotest/selftest.sh` proves all of the above without launching a game (~1 min). Run it after touching `run-test.sh`.

## The 300-second watchdog, and the scenarios it cannot finish

**Every run carries a wall-clock watchdog, and its default is 300 s.** `run-test.sh:176` sets
`TIMEOUT_SECS=300`; `--timeout N` / `--timeout=N` overrides it (`:208-209`), validated as a positive
integer at `:326-335`. The watchdog loop is `:771-798`: if the game is still alive and no verdict has
been written after `TIMEOUT_SECS`, it kills the game and synthesises a FAIL, which the verdict line
reports as `TIMEOUT-FAIL` (`:983-985`). **It is pure wall clock and is deliberately NOT scaled by
`--speed`** (`:326-327`: *"a hung game never advances the sim, so speed is moot"*). Raising `--speed`
without also raising `--timeout` therefore buys you nothing past the 300 s mark — the two flags are
independent knobs and long scenarios need both.

**`--speed N` (1–16) is free, and it is the flag to reach for.** `--speed` (`:204-205`, range-checked
1–16 at `:291-300`) becomes `Test.SpeedMultiplier=N` (`:626-628`), which `TestModeSpeedMultiplier`
applies at world load as `world.Timestep = max(1, oldTimestep / N)`
(`engine/OpenRA.Mods.Common/Traits/World/TestModeSpeedMultiplier.cs:39-41`). A scenario that takes
~24 minutes at 1× runs in ~3 at `--speed 8`; `--speed 8 --timeout 900` is the workhorse pair.

**The simulation stays byte-identical, and that is VERIFIED here rather than taken from the comment
that asserts it.** The trait's header claims it "never enters a synced path"
(`TestModeSpeedMultiplier.cs:8-9`); the thing that could falsify that is a gameplay trait reading the
*mutable* `world.Timestep` (`World.cs:43`) rather than the immutable `world.GameSpeed.Timestep`.
Three do — `TimeLimitManager.cs:137`, `NuclearUnlockClock.cs:284`, `DefconEscalation.cs:415` — and all
three read it **once, in their constructor**, to convert minutes into ticks. The ordering settles it:
`World.cs:220` assigns `Timestep` from `GameSpeed`, `World.cs:252` then constructs the world actor and
its traits (which latch 60), and only afterwards does `World.cs:320`/`:334` run `IWorldLoaded` — where
the multiplier lands. So the converters have already captured the unmultiplied value, and the two
comments at `NuclearUnlockClock.cs:281-283` and `DefconEscalation.cs:410-413` say so explicitly. Every
other consumer is pacing (`Game.cs:1006`, `OrderManager`), rendering (`WeatherOverlay.cs:250`),
logging (`UnitLifecycleLogger.cs:257`), or reads the immutable base (`DoomsdayStrike.cs:738`,
`TimeLimitManager.cs:166`). **Use `--speed` without worrying about it changing a verdict.**

**Tick rate is 16.67/s, so 5000 ticks = 300 s — that is the arithmetic the default invocation can
afford.** (`Timestep: 60` ms, `mod.yaml:406-407` + `:431`. **Never write 25 tps**: 25 is what
`TestHarness.TicksPerSecond` used to carry as a conversion constant, and it was corrected to the
engine's real rate on 2026-09-21 — see §"The `seconds` argument is seconds".) A Lua
`AssertWithin(N, …)` budget is `N × 16.667` ticks, i.e. N real seconds, so `AssertWithin(300, …)` is
exactly 5000 ticks = exactly 300 s. **Every window authored before 2026-09-21 is now a third shorter
than its author measured it to be** — the old constant made `AssertWithin(200, …)` 5000 ticks.

### Scenarios the default invocation cannot complete

Audited at `e0674307` across all 343 directories under `tools/autotest/scenarios/`, over four
duration sources: `TimeLimitTicks` in `rules.yaml`, `TimeLimitSeconds` in `tournament*.yaml`,
`Trigger.AfterDelay(N)`, and Lua deadline constants. Everything over 5000 ticks:

| Scenario | Configured budget | Wall-clock @ 1× | Run it as |
|---|---|---|---|
| `test-escalation-full-match` | `TimeLimitTicks: 22000` (`rules.yaml:72`), outer `DEADLINE = 24000` (`.lua:64`) | **1320–1440 s (22–24 min)** | `--speed 8 --timeout 900` |
| `test-rank-accumulation` | `DeadlineTicks = PhaseBTick + 4300` = 11016 (`.lua:147`); the abrams rank-1 interval alone is 6666 ticks under the shipped curve (`rules.yaml` header comment). Lengthened by `ab4347fc`, AFTER the audit above | 660 s (11 min). Measured 2026-09-20 on main @ 2de1dc78: `--hidden` at 1x reached t=4799 when the 300 s watchdog fired, ticking normally the whole way | `--speed 8 --timeout 900` (PASS in ~75 s) |
| `test-experimental-buys-special-forces` | `DEADLINE_TICKS = 9000` (`.lua:37`) | 540 s (9 min) | `--speed 4 --timeout 600` |
| `test-experimental-msar-deploy` | `DEADLINE_TICKS = 6000` (`.lua:25`) | 360 s (6 min) | `--speed 4 --timeout 600` |
| `test-combined-arms-rendezvous` | `DeadlineSeconds = 200` (`.lua:39`) = 5000 ticks | 300 s — **exactly the watchdog** | `--speed 4 --timeout 600` |
| `wip-transport-delivers` | `DeadlineSeconds = 180` (`.lua:35`) = 4500 ticks | 270 s + load time | marginal; `--speed 4 --timeout 600` |

**Read "cannot complete" precisely, because it is not the same claim for every row.**
`test-escalation-full-match` is the strict case: its time limit is the *subject* of the test — the
match must actually run to 22000 ticks — so at 1× it is structurally unable to reach a verdict inside
300 s and **has effectively never been run to completion by the default invocation**. The others
configure a *give-up cap*, so a passing run may well finish early and report `PASS` honestly. What is
unreachable there is the **failing** path: the watchdog fires first and overwrites the scenario's own
diagnostic — which names the unit and the tick, and is the whole point of the message — with a
generic `TIMEOUT-FAIL`. So a `TIMEOUT-FAIL` on one of these rows at default settings says nothing
about the code under test; rerun it with the flags above before drawing any conclusion.

The last two rows are the ones to check by hand rather than trust: 5000 and 4500 ticks sit at or just
under the watchdog, so whether they clear it depends on map-load time, which this audit did not
measure. Treat them as "raise the timeout" rather than as known-good.

**The `tournament-*` scenarios carrying `TimeLimitSeconds: 720` are NOT on this list**, because
`run-tournament.sh` computes its own budget (`:164`, `TIME_LIMIT_SECS * 4 / SPEED_BUDGET_DIV`) and
does not use `run-test.sh`'s 300 s.

**Their clock was mis-stated, and FIXED 2026-09-19 — but only for 11 of the 53 configs, and the split
is the part worth carrying.** `TournamentConfig` converted with a hardcoded `* 25`, which is exact at
a 40 ms timestep and wrong at 60 ms. `run-tournament.sh:148` reads `GameSpeed:` out of the config and
passes it as `Test.GameSpeed`, so the key really does pick the timestep:

- **42 configs set `GameSpeed: fastest`** (40 ms) — the `-smoke`, `-sanity`, `-quick`, `-eco-5min` and
  `-combat-12min` variants plus `tournament-arena-composition-2p`. `* 25` was CORRECT for these and
  **their durations have not moved at all.**
- **11 plain `tournament.yaml` files set no GameSpeed** (60 ms default). For those, `720` was 18000
  ticks = **1080 real seconds**, so the file's own "12 in-game minutes" described a match nobody ever
  played. They were restated `720 -> 1080` alongside the arithmetic fix, which keeps the tick count
  at 18000 — every recorded baseline still compares — while making the number mean what it says.

A bug entry dated 2026-09-19 says "no shipped `tournament*.yaml` sets a `GameSpeed` key at all". That
is true of the 11 and **false of the other 42**; it generalised from the files it opened. **Read the
`GameSpeed:` key before reasoning about any tournament's duration** — and note the new failure mode
the fix introduces: the deadline is now a function of `world.GameSpeed`, and `Game.cs:1200-1204` warns
that an unknown speed key falls back to default *silently*. `BotVsBotMatchWatcher` logs the timestep it
actually resolved at `WorldLoaded` for exactly this reason.

### An empty `lua.log` from a TIMEOUT-FAIL means nothing on its own

The 0-byte `lua.log` tell — "the script was never wired in", from `map.yaml` rule 5 and §"A green run
is not evidence…" — is **only diagnostic once you know the run got far enough for the script to have
written anything.** A watchdog kill at 300 s produces the same empty file for a completely different
reason: on a long scenario the run was simply cut off, and on any scenario a kill can land before the
first `print` flushes. Both states present identically — `TIMEOUT-FAIL`, empty `lua.log`.

**Check the last tick reached first** (`debug.log`, or any periodic trace the scenario prints). A run
that reached a few thousand ticks was executing Lua; a run stuck near tick 0 was not.

**And the third state, which is the one that produces a false alarm on a HEALTHY run: a script that
runs perfectly and never calls `print`.** `lua.log` collects Lua `print` output and nothing else, so a
scenario that drives cameras, timers and captures without printing once writes 0 bytes exactly like an
unwired one. Two runs were read as failures on this — `260909_203115_p12697_demo-highyield-nuke`
(2026-09-09), which had captured all six screenshots and exited cleanly, and `test-heli-repairs-at-pad`
run `260905_171321`, where the script failed on its first patient, reported only that one and printed
nothing at all. **`debug.log` is the discriminator and it is free:** scripted activity there
(`Taking screenshot …` in scripted order, a trait's own traces) proves the timers fired. So —

| `lua.log` | `debug.log` | reading |
|---|---|---|
| 0 bytes | silent | **inert** — the script was never wired in. `make lua-gate` settles this statically, before any launch. |
| 0 bytes | scripted activity | the script ran and simply **does not print**. Not a finding. |
| 0 bytes | run stopped near tick 0 | cut off — see the watchdog paragraph above. |

**The tell is only ever diagnostic for scenarios that print at all**, which is a property of the
scenario you can check by reading it. A scenario worth diagnosing later should emit a periodic trace
for exactly this reason.

**And rule the unwired case out statically instead of spending a launch on it.** `make lua-gate` —
2 s, no build, no launch, and part of `.\make.ps1 test` — names an unwired scenario for free, before
any game starts. That is strictly better than inferring it from an empty log afterwards: lint proves
a scenario is well-formed but cannot prove the engine will ever *read* it, which is the gap lua-gate
covers (see §"Verify before you ask for a slot").

## The loop (what I run when you trigger AUTOTEST)

1. **Frame the assertion**: "X must happen within N seconds when Y is set up". Confirm with user if ambiguous. **If the change is visual** (UI, palette, animation, sprite, formation, lobby/menu work), also plan a `TestHarness.Screenshot(label, "expects: ...")` at the critical beat — see [`SCREENSHOT.md`](SCREENSHOT.md#apply-automatically-no-trigger-required-when). Apply without trigger.
2. **Write a failing test**: copy a `test-*` folder, set up the actors and a Lua `TestHarness.AssertWithin(...)` predicate. Use the `description.txt` to surface intent in the panel.
3. **Verify RED**: run the new test pre-fix. Must fail with the expected timeout / failure reason. If it passes accidentally, the test isn't measuring the right thing.
4. **Investigate + fix**: read code, apply changes. If diagnosis needs more data, add temporary `Console.WriteLine` traces gated on `TestMode.IsActive`.
5. **Verify GREEN**: re-run the new test. Must pass within reasonable time.
6. **Regression check**: `./tools/autotest/run-batch.sh --all` or at least the closest existing tests, to make sure the fix didn't break anything.
7. **Strip diagnostics**: remove any temporary trace lines I added.
8. **PITFALL check**: was the root cause a non-obvious trap a future reader would also fall into? If yes, drop a one-line `// PITFALL:` (or `# PITFALL:` in YAML) at the *temptation site* — the line a careless reader is actually looking at when at risk, not where the broken code lives. See CLAUDE.md "PITFALL Comments". Same commit as the fix. Skip for one-shot bugs that won't recur.
9. **Commit**: test scenario + fix + tracker update + any PITFALL anchor in a single commit. Test stays committed so the bug can't silently regress.

If the bug has multiple layers, fix what I can, leave the test RED for the unfixed parts, and document in `WORKSPACE/RELEASE_V1.md` what's left. The red test becomes the next session's gateway.

## Writing a test scenario

```
tools/autotest/scenarios/test-<name>/
├── description.txt        # one-line panel description (recommended)
├── map.yaml               # actor placement + player slots (gotchas below)
├── rules.yaml             # LuaScript: test-helpers.lua, test-<name>.lua
├── test-<name>.lua        # staging + (for auto) AssertWithin
├── map.bin                # copy from a sibling test
└── map.png                # copy from a sibling test
```

### `map.yaml` rules (Launch.Map quirks)

1. `Visibility: MissionSelector` and `Categories: Test` so it stays out of the regular lobby map list.
2. Actor names lowercase: `e1.russia`, `t90`, `m109`. (The engine lowercases internally; `E1.russia` will throw `KeyNotFoundException`.)
3. **Only ONE `Playable: True`** — the human slot. Every enemy/garrison faction must be `Playable: False`. `Launch.Map` only creates Player objects for slots with a connected client; an unclaimed `Playable: True` slot drops its actors to Neutral, which silently breaks targeting (no attack cursor, no auto-engage). Diagnosed the hard way; see commit history.
4. `LockColor: True` and `LockFaction: True` on every PlayerReference, so visual cues stay consistent across machines (human=blue, enemies=red, allies=green) regardless of the dev's `settings.yaml`.
5. **A top-level `Rules: rules.yaml` line, or the sibling `rules.yaml` is never read.** Last line of the file, preceded by a blank line (adjacent MiniYaml top-level entries merge). Working example: `tools/autotest/scenarios/test-experimental-poi-observe/map.yaml:96`. Omit it and the `LuaScript` trait is never attached, the match runs on stock mod rules, and the run ends as an ordinary `TIMEOUT-FAIL` — `run-test.sh` even prints *"No 'Failed to load rules'"*, because nothing failed to load; the rules were never requested. **The tell is `lua.log` at 0 bytes** (see the same rule under §"A green run is not evidence…"). Every committed scenario carries the line as of 2026-08-19.
6. **One `supplyroute` per active faction.** Always include a Supply Route per side (e.g. `OwnSR: supplyroute / OpponentSR: supplyroute`), even if the test's units never interact with it. Reasons: (a) WW3MOD's gameplay model is "every player has an SR"; tests should reflect that. (b) Faction elimination triggers a Mission-Accomplished overlay that ends the game before the Lua poller writes a verdict — the runner reports "no result file written". A single SR keeps the faction alive when all its actors die. (c) `supplyroute` has `Targetable.TargetTypes: NoAutoTarget`, so it won't be picked up by AutoTarget scans or operator-retarget hunts — safe to drop anywhere on the map.

### `rules.yaml`

```yaml
World:
    -StartGameNotification:
    -SpawnStartingUnits:
    -MapStartingLocations:
    -CrateSpawner:
    LuaScript:
        Scripts: test-helpers.lua, test-<name>.lua    # helpers FIRST
```

### Lua skeleton

```lua
-- test-<name>.lua
WorldLoaded = function()
    TestHarness.FocusBetween(Paladin, Target)   -- center camera
    TestHarness.Select(Paladin)                  -- pre-select unit-under-test

    -- For an auto-asserting test:
    TestHarness.AssertWithin(8, function()
        if Paladin.IsDead then return "fail: died first" end
        return Paladin.AmmoCount("primary-ammo") < startingAmmo
    end, "Paladin did not fire within 8s")

    -- For a manual test, omit AssertWithin. Player presses End=restart;
    -- they describe the verdict in chat.
end
```

## Verify before you ask for a slot

**Two gates check a scenario without launching it. Both take seconds, neither needs a build, and
between them they catch the two failures that most often burn a granted run and come back as an
ordinary `fail`: a Lua name the engine never registered, and geometry that is not what the scenario
believes.** Run both on a new or edited scenario before requesting a slot.

```bash
make lua-gate                                              # every scenario
./tools/lua-gate/lua_gate.py check --scenario test-<name>  # just yours
```

**What lua-gate proves:** every `Trigger.*` / `Actor.*` / `Test.*` member you name is a real binding,
every bare actor name resolves against your own `map.yaml`, and — the one that maps directly onto a
trap documented above — that a `.lua` file is actually **reached by a `Scripts:` line**. That last
check is the static form of the `lua.log` at 0 bytes tell in `map.yaml` rule 5: an unwired script
produces a scenario that runs on stock mod rules to a confident `TIMEOUT-FAIL`, and lua-gate names it
for free instead. Exit 2 is a hard fail, exit 1 a warning; `make lua-gate` fails only on 2.

**What it does not prove, and do not let a green here stand in for it:** it resolves *names*, never
calls. Argument types, arity and order are unchecked (`Trigger.AfterDelay("soon", 5)` passes and
throws at runtime), and **73 of the 92 actor properties are trait-gated** — `tank.Produce` is in the
union of all actor properties, so it passes here and throws in game because the tank has no
`Production` trait. Full limits in [`tools/lua-gate/README.md`](../../tools/lua-gate/README.md)
§"What this does NOT check".

**Geometry: use nav-guard's decoder directly.** `make nav-guard` does **not** cover
`tools/autotest/scenarios/` — its baseline is `mods/ww3mod/maps` only, so its green is byte-identical
before and after any scenario edit and says nothing whatever about your scenario. The decoder
underneath it has no such limit and models what the pathfinder sees:

```bash
./tools/nav-guard/nav_guard.py report  --scenarios --map test-<name>
./tools/nav-guard/nav_guard.py pockets --scenarios --map test-<name> --locomotor wheeled
```

`pockets` is the one to read: it prints every region that is **not** the largest, with a bounding box.
If your scenario means to seal something off, the pocket must be exactly the shape you sealed; if it
does not, a pocket is a unit that cannot reach what the test assumes it reaches. For a specific
"can A reach B?" question, label the cells and compare — worked, committed example with its measured
numbers at `tools/autotest/scenarios/test-restock-unreachable-centre/map.yaml:70-97`:

```bash
python3 - <<'PY'
import sys; from pathlib import Path; sys.path.insert(0, 'tools/nav-guard')
import modload, nav_guard
rules = modload.load_mod(nav_guard.MOD_DIR)
gm = modload.load_map(Path('tools/autotest/scenarios/test-<name>'))
loco = [l for l in modload.world_locomotors(rules, gm.rule_overrides) if l.name == 'wheeled'][0]
occ, _ = nav_guard.cell_occupancy(rules, gm, 'live')
m = nav_guard.build_cell_model(rules, gm, rules.tilesets[gm.tileset], loco, occ)
labels, sizes = nav_guard.component_labels(m, nav_guard.DEFAULT_SQUEEZE)
left, top, w, _ = gm.bounds
lab = lambda cx, cy: labels[(cy - top) * w + (cx - left)]
print('reachable:', lab(10, 10) == lab(31, 11))
PY
```

**FIXED 2026-09-01 — this note used to warn that the decoder over-blocked map markers, and that is no
longer true.** `mpspawn`, `spawnarea`, `waypoint`, `flare` and the `camera.*` actors carry
`Immobile: OccupiesSpace: false` and occupy **nothing** in game (`ImmobileInfo.OccupiedCells` returns
an empty dictionary, `Immobile.cs:23-27`), and `modload.actor_shape` now honours the flag via
`_immobile_occupies_space` (`modload.py:300-309`, branch at `:330`), so none of them is modelled as a
wall any more. **Do not reason from the old warning:** a cell whose only occupant is one of those
markers is passable to the tool as well as to the game, so if such a cell now reads blocked, something
else is on it and it is worth investigating rather than dismissing as an artefact. The historical
version of this note, and the false positive it caused a scenario author to "fix" by moving an actor,
are recorded in `WORKSPACE/DISCOVERIES.md`. See `tools/nav-guard/README.md` §Zero-footprint actors for
the current model.

**One more gate you get for free, and it is the one scenario-shaped check with no "scenarios are not
maps to the tooling" hole:** `ScenarioLuaParsesTest` parses every `tools/autotest/scenarios/**/*.lua`
under the engine's own Lua 5.1 runtime, so `dotnet test` catches a syntax error naming the file and
line. Verified by injecting `local x = = 1` and watching it fail, rather than assumed. Cheap insurance
against burning a slot on a typo.

### A scenario that passes every gate a launch-barred worker may run is NOT known to load

**Know what each gate READS, because the set of them has a hole exactly the shape of a field value.**
`test-bot-damages-garrisoned-building/map.yaml` carried `Facing: East` on a placed actor. It passed
`make lua-gate` twice, `make check`, `make all` and `dotnet test`, was committed and handed over — and
died at map load with `OpenRA.YamlException: FieldLoader: Cannot parse 'East' into WAngle`, **exit 3**,
before a single tick, taking the RED run queued behind it with it.

| gate | what it reads of a scenario |
|---|---|
| `lua-gate` | `map.yaml` for STRUCTURE only — which files are declared, whether `Scripts:` sits under `World`, whether a top-level key is mis-cased. It never asks the engine to *load* an actor, so it cannot type a field value. |
| `make check` / `dotnet test` | scenario CONTENT: not at all. (`ScenarioLuaParsesTest` parses the `.lua`, never the YAML.) |
| `make nav-guard` | scenario-blind — baseline is `mods/ww3mod/maps` only. |
| `./utility.sh --check-yaml ../tools/autotest/scenarios/<name>` | **the only one that types a field**, and the one a launch-barred dispatch usually forbids. |

So when you hand over a scenario you could not lint, **say so, and ask the manager to lint it before
the first launch.** The mechanism generalises past `Facing:`: a `map.yaml` value is a raw `WAngle` and
`FieldLoader` has no name table for it, while the four compass names exist only in the *Lua* binding
(`AngleGlobal.cs:23-38`) — which is why `Actor.Create(…, Facing = Angle.East)` in the `.lua` is correct
and the `map.yaml` line beside it is not. Angles: [`conventions.md` §WAngle](../reference/conventions.md).
The corpus check needs no build: `grep -rnE '^\s+(Turret)?Facing: ' tools/autotest/scenarios mods/ww3mod/maps | grep -vE ': -?[0-9]+$'`.

### Geometry the gates do not check: arms that are supposed to be independent

**A radius is a circle, so arms separated by ROWS are not separated.** `test-auto-capture-nearby` laid
four arms on rows 6/12/18/26 and reasoned about x-offsets along each row, under a comment asserting
they could not interact. They interacted at **7.81 cells** — `sqrt(5² + 6²)` — inside an 8-cell scan
radius, so one arm's technician captured another arm's structure and the run failed blaming shipped
code that was fine. Two compounding traps, both arithmetic and both invisible by reading:

- **Measure centre to centre, not `Location` to `Location`.** A 2x2 building's `CenterPosition` sits at
  `Location + (1.0, 1.0)` cells against a 1x1 unit's `Location + (0.5, 0.5)`, so `Location`-space
  distances understate every unit-to-building pair by ~0.5 cells. Re-deriving the same map with the
  offsets applied moved a second pair from 20.59 (safe) to **19.24** — inside a 20-cell Hunt radius,
  and a latent failure the first correction would have missed. Mechanism:
  [`conventions.md` §"`Dimensions` is a BOUNDING BOX, not the shape"](../reference/conventions.md).
- **A walk budget needs the speed.** `^Infantry` `Mobile: Speed: 25` world units/tick against 1024
  units/cell is **40.96 ticks per cell**, so a 20-cell approach is ~819 ticks. A sibling scenario
  allowed 900 for the walk *plus* the capture — a ~4% margin — and failed with a verdict ("dispatched
  at but never captured") that reads exactly like a defect in the capture activity.
  `CaptureClearDurationTest.InfantryCoverACellInAboutFortyOneTicks` now pins that speed, so a retune
  fails loudly instead of silently re-tightening every budget sized against it.

**So: when an autotest's arms are supposed to be independent, enumerate every cross-pair mechanically
before the run.** Both errors above were a five-line script away. The same discipline applies to a
guard radius derived from a mechanic: `test-drone-targeting`'s confound guard was a 28-cell circle
(28 = the verifying vision radius, correct in meaning) around a cell 30 cells from the acting unit,
which leaves a **two-cell shell** of safe ground near the player's own Supply Route — every unit
spawning there trips it. **Check the guard against the map before trusting it to fire only on real
contamination.**

**And adding scenery to satisfy a gate is not inert — the gate usually reads exactly the property that
makes some other module want the thing.** Same scenario: a neutral `oilb` was added purely to make a
hover disc POI-eligible, and `PoiMap` admits an income structure only if it carries
`CaptureManagerInfo` — so POI-eligibility and capture-eligibility are the **same predicate**, and the
addition handed `CaptureCoordinatorBotModule` a target. It reacted on the first scan (tick 28), sent a
technician walking from the Supply Route toward the derrick, and that technician crossed within 28 cells
of the cell the scenario needed left unobserved. Zeroing the economy does not contain it either. **When
a scenario needs a POI purely for eligibility, expect the capture layer to send something at it, and
place it where the resulting traffic cannot cross the region under measurement.** Mechanism:
[`influence-stack.md` §"Stage F"](../reference/influence-stack.md).

**And the setup can be right and still open the wrong code path.** `AutoSeekSupplies` runs two
dispatchers that queue **different activities**: the idle seek (`INotifyIdle.TickIdle`) queues
`SeekSuppliesAndReturn` (`AutoSeekSupplies.cs:202`, reach `SupplyHuntLeashCells: 20`), while the
break-off arm queues `SeekSupplyProvider` (reach `ReturnWhenEmptyLeashCells: 30`) — and `FindBest`, the
thing a re-pick scenario is testing, lives only in the second. A scenario about that leash must place
its host in the **20 < d ≤ 30 band** or the idle seek opens the errand and the run measures an activity
with nothing under test in it; a first cut at 11 cells passed pre-fix. The far host must additionally
sit outside *every* dispatcher's leash from *every* cell the unit occupies including its start, which
on a 66×34 map leaves no legal cell in any direction. **Before sizing a scenario, name which dispatcher
you intend to open it, and check the geometry can only open that one.** The two paths:
[`economy.md` §"Two host-discovery paths disagree about the same actor"](../reference/economy.md).

**Neither gate is a substitute for a run.** They establish that the scenario is *well-formed* — the
script loads, the names exist, the geometry is what you drew. They say nothing about whether your
predicate measures the thing you care about, which is what the two sections below are for.

## Test types

- **Manual** — Lua only stages (camera, selection); user watches and types verdict in chat. Example: `test-artillery-turret` (the original "did the turret rotate?" test). Best when the bug is visual or hard to assert numerically. *If there is no verdict question at all and the user just wants to look, that's a **DEMO**, not a manual test — see [`DEMO.md`](DEMO.md).*
- **Auto-asserting** — Lua uses `TestHarness.AssertWithin(...)` to verdict itself. Game writes JSON and exits; runner exit-codes back. Example: `test-paladin-fires`. Pair with `--all` for unattended regression sweeps.

## Lua API

### `TestHarness.*` (in `mods/ww3mod/scripts/test-helpers.lua`)

| Function | Purpose |
|---|---|
| `FocusBetween(a, b, ...)` | Center camera on the midpoint of N actors |
| `Select(actor)` | Pre-select unit-under-test (no manual click needed) |
| `AssertWithin(seconds, predicate, failReason)` | Poll predicate every tick. `true`→Pass, `"fail: <reason>"`→Fail immediately, timeout→Fail with reason. **The Pass is TERMINAL — see the rule below.** |
| `AssertAfter(seconds, predicate, failReason)` | Wait `seconds`, then assert once |
| `Screenshot(label, note?)` | Capture a PNG now. Wrapper around `Test.Screenshot`. See [`SCREENSHOT.md`](SCREENSHOT.md). |
| `ScreenshotAfter(seconds, label, note?)` | Schedule a screenshot N game-seconds from now |

**ONE VERDICT AUTHORITY PER SCENARIO. A predicate handed to `AssertWithin` must NEVER become true in a scenario that has its own deferred verdict.** `Test.Pass` is terminal — it writes `result.json` and exits the game — so the instant the predicate returns `true`, anything the scenario scheduled for later is dead: assertions never execute, screenshots never flush, and the run reports green having measured nothing. `test-himars-church-vs-block` did exactly this on 2026-09-21 (`return Reported` against a latch set 27 ticks before its own `Verdict`), and **both** runs passed with `"notes":""`, no `screenshots` key and no PNG on disk. `AssertWithin` is safe only as a **pure watchdog** (predicate always false, used for its timeout) or as the **sole verdict authority** (nothing scheduled after it). To judge *after* a latch: open-code the deadline in a `Trigger.OnTick` poller, or call `Test.Pass(note)` from **inside** the predicate and then `return false`. The same applies to `AssertAfter`. Full audit of all 46 callers that also carry their own payload verdict — 1 YES, 4 watchdog-only, 41 NO — in `WORKSPACE/audit/260921-assertwithin-false-green.md`.

**A green with no verdict text is a failed test, and the runner now says so.** Both helpers pass a note of their own, so `"notes":""` is an anomaly rather than the norm; `run-test.sh` reports it as the distinct outcome **`PASS-EMPTY`** (exit code still 0 — the contract is unchanged), `expected-status.sh` grades it `EMPTY`, and `run-batch.sh` counts and lists it separately so a batch cannot pass on one. A scenario that legitimately passes empty declares `pass-empty` plus a required reason in its `expected-status` file, which goes STALE (red) the moment somebody gives it a note.

**The `seconds` argument is seconds — as of 2026-09-21, and it was not before.** `test-helpers.lua:32-33` now derives `TestHarness.TicksPerSecond` from `TestHarness.TimestepMs = 60`, which mirrors `mod.yaml:431` (`DefaultSpeed: default`, `:407`), and `TestHarness.TicksForSeconds` (`:44`, consumed at `:119`/`:213`/`:321`) converts the way the engine does — `seconds * 1000 / timestep`, multiply before divide. So `AssertWithin(10, …)` waits 10 real seconds and a "within Ns" failure string is the truth. `run-test.sh` sets neither `Test.GameSpeed` nor `Test.SpeedMultiplier`, and `Game.LoadMap` hardcodes the `"default"` speed unless `Test.GameSpeed` overrides it (`Game.cs:1184`), so this applies to every scenario in the suite.

**What this cost, and it is not paid off yet.** The constant was `25` until that date, so every window was `N × 1.5` real seconds. Nothing was re-authored when it moved: **every deadline written before 2026-09-21 now allows a third less time than its author measured**, and the suite run that follows the flip is what finds the casualties.

**There is now ONE tick base, and this paragraph used to say there were three.** `DateTime.Seconds(n)` once computed `1000 / Timestep` in **integer** arithmetic and yielded 16; that truncation was fixed on 2026-09-19 (`DateTimeGlobal` → `TickTime.TicksForSeconds`, multiply before divide), and the harness was moved onto the same arithmetic on 2026-09-21. `AssertWithin(n)` and `DateTime.Seconds(n)` are now the same number of ticks for every integer n, so mixing the two in one scenario is no longer a trap. `AutotestTickRateTest.HarnessAndEngineAgreeTickForTickOnEveryIntegerSecond` executes that agreement over 0..600 s rather than asserting it.

The old error ran in the LENIENT direction, which is why nothing broke while it stood — and it is why the correction is the dangerous half. Two consequences to carry:

- **A pre-flip scenario tuned to "just barely times out" just lost a third of its window.** Expect red runs, and expect them concentrated in scenarios whose deadline was sized by measurement rather than by arithmetic.
- **A green is not evidence.** A scenario can keep passing while an INNER budget it contains becomes unreachable and silently stops being enforced — `test-autotarget-preempt-air` did exactly that when its outer deadline was evaluated at 16 (comment at `:84-95`). That is the failure to hunt, and it does not announce itself.

**The constant HAS been fixed, 2026-09-21 — this paragraph used to forbid exactly that, and the prohibition is spent.** The flip tightened every seconds-literal deadline in the suite by a third in one step. A 2026-08-27 static audit sized the blast radius and is the list to read the results against: **91 deadlines across 137 scenario files scaled with this constant**, 8 more round-trip through it and are immune, and two were named as provable casualties (`test-autotarget-preempt-air`, `test-critical-no-panic` — both since re-authored in ticks and now immune). Several scenarios banked the slack on purpose and said so (`test-tunguska-missile-standoff`, `test-depot-vacate-phantom`); their comments are updated but their numbers were NOT re-derived, so they are the first two to check. `AutotestTickRateTest.cs` still pins the rate, the mod's default `Timestep`, the harness/engine agreement and those two scenarios' arithmetic, so putting back either `25` or the truncated `16` fails `dotnet test` with the casualty named rather than failing invisibly in a game nobody reran. **Size new deadlines in ticks and divide** — prefer expressing tick-domain quantities (burst delays, reload times, projectile flight) in ticks and polling with `Trigger.AfterDelay(1, …)`, which is immune both to this constant and to whatever game speed a run happens to use. The underlying `world.Timestep`-vs-`GameSpeed.Timestep` mechanism, and a second consequence of the same baseline in `TimeLimitManager`, are in [`conventions.md` §Engine behaviors that surprise](../reference/conventions.md). This is one instance of a mod-wide pattern — comments and constants asserting a duration the code does not produce — collected in [`conventions.md` §A change believed made, documented as made, and inert](../reference/conventions.md#a-change-believed-made-documented-as-made-and-inert).

### `Test.*` (engine global, gated on TestMode.IsActive)

| Function | Effect |
|---|---|
| `Test.Pass()` | Write `pass` verdict, `Game.Exit()` (deferred until pending screenshots are flushed to disk) |
| `Test.Fail(reason)` | Write `fail` verdict + reason, exit |
| `Test.Skip(reason)` | Write `skip` verdict + reason, exit |
| `Test.Screenshot(label, note?)` | Capture a PNG tagged `label`. Path is emitted into the verdict JSON's `screenshots[]` array; agent reads + evaluates. See [`SCREENSHOT.md`](SCREENSHOT.md). |
| `Test.IssueEnterTransport(passenger, transport, queued?)` | Issue a real EnterTransport order through Passenger.ResolveOrder. Use this rather than `unit.EnterTransport(t)` when the test needs the resulting RideTransport activity to be visible to target-line scans (e.g. spread / Shift-G logic). |
| `Test.GroupScatter({actors})` | Run the Group Scatter (Shift-G) spread on the given actors. Mimics the hotkey path without needing a key press / live selection. |
| `Test.SetZoom(scale)` | Set zoom as a multiple of the default level, for reproducible screenshots. Identical to `Camera.Zoom` below, which is ungated — prefer that unless the call site reads better as staging. |

**Read `TestGlobal.cs` before reaching for a stock OpenRA binding.** A stock scripting API is a **bad
prior in this mod** — three consecutive ones did not exist, and each failure is the same shape: a
runtime *"does not define a property"* that kills the script at its first use, or worse a silent no-op.
`actor.Build` exists only on an actor holding queues, and WW3MOD has no factories (the queues live on
the player); `actor.Sell` requires `SellableInfo`, which in this mod is on **structures only**; and
`Evacuate` is not a Lua property at all — nothing under `engine/OpenRA.Mods.Common/Scripting/` is named
that, it is a raw order string reached from a scenario only via `TestHarness.Select` then
`Test.PressHotkey("Evacuate")`. The mod-specific seam usually exists and is usually the intended route:
`Test.*` carries `QueueProduction`, `PressHotkey`, `SelectActors`, `ClickOrder`, `IssueMoveOrder`,
`IssueResupply`, `ClickProductionIcon` and ~50 more. Which stock bindings are absent and *why*:
[`architecture.md` §"Key Lua APIs used"](../reference/architecture.md).

**And grepping the scenario corpus for prior art actively misleads here.**
`grep -rn "\.Build(" tools/autotest/scenarios/` returns four confident-looking hits — all a
**scenario-local helper of the same name**, unrelated to either binding. Four call sites reads as an
established idiom. **Confirm a scripting API in `engine/OpenRA.Mods.Common/Scripting/`, never by
counting scenario hits.**

**Fog IS testable, and the belief that it is not cost a structural-proof fallback.** Two independent
reasons: `TestMode.KeepRenderPlayer` exists (`TestMode.cs:39`, parsed at `:311` from
`Test.KeepRenderPlayer=true` — matched against the literal `"true"`, so `1` does not work) and guards
the null-assignment at `TestModeLogic.cs:30-31`; and more fundamentally **the frozen STATE never
depended on `RenderPlayer` at all** — `FrozenActorLayer` is a per-player trait reading the *viewer's*
own `MapLayers`, so frozen actors have existed for the local player in every fogged autotest ever run.
`RenderPlayer` only decides whether `World.FogObscures` answers honestly and which player's ghosts the
mouse paths consult. Several scenarios already set the flag. Three `Test.Frozen*` bindings exist for
reading a ghost directly — `FrozenActorState` (`TestGlobal.cs:1065`), `FrozenActorOwner` (`:1084`),
`FrozenActorTooltipOwner` (`:1099`) — plus `FrozenClickCursor` (`:1114`), which is the one that had to
be added: `Test.ClickCursor` builds `Target.FromActor` and so can never reach the
`CanTargetFrozenActor` arm.

### Presentation: `Camera.*`, `Trigger.OnTick` (engine globals, NOT test-mode gated)

These work in demos and real missions too. The demo-facing version of this table, with
worked guidance on framing a shot, is in [`DEMO.md`](DEMO.md#frame-the-shot-yourself--the-viewer-should-not-have-to).

| Function | Effect |
|---|---|
| `Camera.Position` | Read/write the centre of the view as a `WPos`. |
| `Camera.Zoom` | Read/write zoom as a **multiple of the default level** (`1` = default, `<1` further out, `>1` closer in). Clamped to `Camera.MinZoom`..`Camera.MaxZoom`; an out-of-range write is applied as far as it goes, not an error. Deliberately not the engine's raw `Viewport.Zoom`, which is resolution-dependent and so frames differently on the author's machine and the viewer's. |
| `Camera.MinZoom` / `Camera.MaxZoom` | The achievable range, same units. Read them rather than assuming — they depend on the display and the viewport-distance setting. |
| `Trigger.OnTick(func)` | Call `func()` once per world tick. Several may be registered; they run in registration order after the global `Tick` function. **Prefer this to a self-rescheduling `Trigger.AfterDelay(1, ...)` loop**, which allocates a `DelayedAction` and a frame-end task every tick to do the same thing. |
| `Trigger.ClearTickCallbacks()` | Drop every `OnTick` callback. |

**Camera state is CLIENT-LOCAL.** Writing it cannot affect the simulation — nothing
sync-hashed reads the viewport, which `ViewportIsNotSimulationStateTest` asserts by IL scan.
*Reading* it and branching simulation behaviour on the result would desync a multiplayer
match, exactly as branching on `Camera.Position` already would. `Trigger.OnTick` is the
opposite: it runs inside the simulation on the same `World.Tick` as the global `Tick`
function, so simulation work from a tick callback is fine.

### Useful actor methods (existing OpenRA Lua API + WW3MOD additions)

| Method | What it does |
|---|---|
| `Paladin.Attack(target, allowMove?, forceAttack?)` | Issue attack on actor (existing API). `queued: true` internally. |
| `Paladin.AttackGround(cell, allowMove?, queued?)` | Ctrl+click on terrain. WW3MOD addition. |
| `Paladin.AmmoCount("primary-ammo")` | Returns int. Note: pool name is `primary-ammo`, not `primary`. |
| `Paladin.Stance = "HoldFire"` | Force a unit into HoldFire (or "Ambush"/"FireAtWill") |
| `UserInterface.Select(actor)` | Replace local player's selection. WW3MOD addition. |

## A behaviour selected by a condition needs a test on EACH SIDE of it

If the thing you are fixing has two modes — danger vs quiet, empty vs full, first-run vs repeat — **one scenario cannot pin it.** Whichever branch you were thinking about will pass, and the other mode's mechanism will quietly satisfy your assertion.

Worked example, 2026-08-10 (details in `WORKSPACE/DISCOVERIES.md`): supply trucks are supposed to dump their whole load and leave under fire, and to serve in place keeping their cargo on a quiet front. Every single-scenario green that day was reachable by a change that broke the other scenario, twice — and each defect was caught by the test that was *not* being worked on. The pair must go green **together** or neither result means anything.

Two failure shapes that keep recurring and that a matched pair catches:

- **A fix correct in isolation, wrong in combination.** Guard A was harmless only because bug B stopped it ever firing. Fix B and A becomes a live defect. So: after any fix, re-run the scenario you were *not* working on.
- **A bug that cannot fire is indistinguishable from a bug that does not exist.** "We looked and it wasn't happening" is worthless whenever a gate upstream of it is known to be failing closed. Record such a hypothesis as UNTESTED, never as refuted — an accurate status keeps it in the queue where "dead" deletes it.

## A green run is not evidence unless something could have made it RED

**Prove your setup took effect by measuring a control — never by asserting the flag you yourself set.** A scenario that never built the world it describes still runs to completion and still writes `pass`. Nothing in the harness can tell you that happened; the verdict looks identical either way. So the question to ask of every green is not "did it pass?" but **"what would have made this fail?"** — and if you cannot name it, you have measured nothing.

Six instances have now landed, by completely different mechanisms (the first two on 2026-08-12 alone):

- **The control that refused to go red.** `test-autotarget-preempt-air` was written to prove air-target preemption works, with a RED control pinning `PreemptScanInterval: 0` so the fix is switched off. Both arms were finally run — **and both passed** (`f910ac7d`). The unaided behaviour beat the 110-tick deadline on its own, so the tick budget never isolated the mechanism under test and the green arm was never evidence of anything. The fix had shipped on the strength of it.
- **The setup that silently reverted to engine defaults.** A scenario overrode a warhead to lower its damage, restating `Damage` and omitting `Penetration: 15`. Warhead overrides are constructed fresh rather than merged per-field, so `Penetration` fell back to the **engine** default of 1, took the armour-reduction branch, and the intended effect was cut by an order of magnitude. The run **completed and reported `pass`**, and the number it produced was plausible. (Mechanism: [`conventions.md`](../reference/conventions.md) §Weapons live under `Weapons:`.)
- **The observable the rest of the system could satisfy on its own** (2026-08-15, `DISCOVERIES.md` same date). `test-combined-arms-rendezvous` was written to prove a transport change — that ferried infantry are set down with the armour rather than at a cell the armour was never going to — and asserted the obvious thing: *are the riflemen near the tank?* It **passed with the fix disabled**. Infantry are armed, so `PoiOffensiveBotModule.StageFreePool` recruits them into the free pool and `AttackMove`s them to the **same staging anchor as the armour**, on foot, from tick 3. They arrive next to the tank under their own feet and the predicate goes true without a transport being involved at all. Nothing was misconfigured and no default silently reverted; the assertion was simply satisfiable by a second, unrelated mechanism. (The same scenario also spent two earlier runs on a `map.yaml` missing its `Rules: rules.yaml` line, so `rules.yaml` — Lua and all — was never loaded and the match ran on stock mod rules with a 0-byte `lua.log`. When a scenario times out with no verdict, **check `lua.log` is non-empty first**: that one fact separates "my predicate never went true" from "my script never ran".)
- **The observable that was attributable, but not for the whole run** (2026-08-15, `DISCOVERIES.md` same date). `test-ferry-fills-seats` was written to prove the capture ferry fills the technician's spare seats, and deliberately avoided the positional trap above: the observable was **peak passenger count on one named carrier** — a loading fact only the ferry could produce, since `TryAssignNewTasks` skips any carrier already in `carrierTasks` and any carrier that is not empty. That reasoning was correct and still **passed while the ferry carried one technician and nothing else** (`ferry-escort … boarded=0`, `depart aboard=1`). It was true only *while the ferry owned the carrier*. Once the task is torn down the carrier returns to the general pool, the ordinary frontline delivery path loads riflemen into it, and the peak reaches 2 minutes later and a mission away. **A per-actor observable is not automatically an attributable one — the ownership window has to be in the predicate.** Fixed by freezing the peak at dismount. Worth saying plainly: nothing in the verdict betrayed this, and it was caught **only by reading the debug log instead of trusting the green**, which is the habit that separates a measured run from a confident one.

- **The named actors were not the ones the system chose to use** (2026-08-15, `bugs/discovered.md` same date). `test-transport-delivers` names a carrier and five riflemen and measures those. Its inherited `rules.yaml` comment asserted the opposite of the truth: *"the measurement only ever looks at NAMED actors, so incidental units the bot buys or spawns do not affect it."* At `DefaultCash: 7500` the bot **bought its own carriers and produced its own infantry and used those**, leaving the placed actors idle beside the measurement — `debug.log` recorded **six** departures carrying 2–3 passengers each while the predicate reported `everCarried=0, peakPax=0`. Both readings were correct about different actors. **A named-actor predicate is valid only if the named actors are the ones the system under test actually chooses to use, and production removes that guarantee.** The concrete remedy is `DefaultCash: 0`, which makes the placed force the entire force (the constraint `test-tecn-ride` already uses); the general one is to assert the named actor is the one that did the work, not merely that work happened.

- **The assertion the test naturally reaches for sat UPSTREAM of the defect** (2026-08-19, `DISCOVERIES.md` same date). The unload menu's height ceiling was fixed so a tall passenger list is no longer clipped. The obvious capture assertion is the row count — and `CargoUnloadMenuLogic.Refresh` adds **every** class row to the scroll panel and only then sizes it, so the count is identical on the broken and the fixed build (24 either way). A scenario asserting `"1:24"` goes green **against the exact defect it was written for**, and the screenshot beside it reads as corroboration. What separates the builds is the **clip** height: pre-fix `Math.Min(380, …)`, post-fix `Math.Min(<screen-derived ceiling>, …)` (`CargoUnloadMenuLogic.cs:180-181`). Measured live: `rows=24 content=551 clip=551 panel=574 screen=1224` — 551 is unreachable under a 380 cap, so the number is self-controlling. **When a fix changes how much of a collection is DRAWN, every count in the widget tree is a false control** — `Children.Count`, `PassengerCount`, the group count all sit upstream of the clip and all survive the bug. Assert the geometry, or assert nothing. (`Test.GetUnloadMenuGeometry()` exists for exactly this, `Scripting/Global/TestGlobal.cs:277`.)

- **The setup was never in the state under test** (2026-09-01, `wt/death-slide`). `test-husk-corner-slide` exists to catch a wreck crabbing sideways when its unit dies **mid-corner**, and staged each lane with two queued `Move` orders — east to a waypoint, then south. **Two queued Moves never produce a corner arc at all:** the first settles on the cell centre with `FromCell == ToCell` and the second turns in place, while the arc and its `ToCell` retarget live only inside `MoveFirstHalf`'s chained branch. Three of four lanes would have driven, stopped, turned, died and returned a clean green **having never cornered once** — and nothing would have contradicted it: the verdict was liveness-only and honestly green, and the screenshots show wrecks correctly settled, because on a straight leg they *are* correct. Nothing fell back to a default, no control refused to go red, no second mechanism satisfied the predicate. **A queued order boundary is a state boundary** — anything existing only *within* one activity (arc turning, carryover progress, mid-path retargeting) is destroyed by splitting the order in two, and splitting is the natural way to write the setup. Mechanism: [`conventions.md` §"Engine behaviors that surprise"](../reference/conventions.md).
- **The harness measured a SHORTER PIPELINE than the player uses** (2026-08-20, `wt/order-fallback`). `Test.ClickOrder` was documented as resolving "the `IIssueOrder` targeter chain in descending `OrderPriority` exactly as `UnitOrderGenerator` does", and it did — except the real `OrderForUnit` ran that chain **twice**, the second pass against the terrain cell under the clicked actor, which is where "cannot attack that" becomes "walks to that". The private copy had only the first pass. So the defect was **unreachable from any scripted path in the repo**, and a scenario written against `ClickOrder` to catch it goes green on the broken build. Not a reverted default and not a rival mechanism: **a duplicated copy of the code under test, missing the branch containing the bug.** Fixed by deletion — `ClickOrder` now calls the same public `UnitOrderGenerator.OrderForUnit` the mouse path calls (`TestGlobal.cs:739-747`). **Whenever a helper's docstring says "exactly as X does", that is a claim about a copy, and a copy is only as good as the day it was written.** Prefer delegating to X; where you cannot, name in the docstring which parts of X it does *not* reproduce, because the omitted part is exactly where a bug hides from every test you write. (That helper carries a second caveat worth reading before you use it: it is the **per-unit** layer, and a real click resolves for the whole SELECTION.)
- **The log line was named after the thing you wanted and printed something else** (2026-08-15, `wip-transport-delivers`). `[exp-transport] delivered … pax=N` printed **`task.SeatTarget`** — a target, not a count of passengers. The change under test raises `SeatTarget`, so the after-run read `pax=5` against a baseline `pax=1`: a fivefold "improvement" that is purely the inflation of the variable being reported. Both readings were internally consistent, the log line was pre-existing and trusted, and the number moved in the direction the hypothesis predicted. It also retro-invalidated three deliveries banked earlier the same day, whose real passenger counts are now unknown. **An observable named after the thing you want is not a measurement of it — `git grep` the format string and read the emit site; do not trust the field name.** Nothing else would have caught this one.

What to do about it, in order of cheapness:

1. **Run the control arm, and require it to FAIL.** A control that passes has falsified your test, not your hypothesis. Stop and rebuild the scenario before reading the green arm.
2. **Verify the pin was applied, do not assume it.** The preempt-air run did this correctly: a temporary trace in `AutoTarget.Created` printed the effective `PreemptScanInterval` per arm, so "the control really was switched off" was observed rather than inferred. A one-line trace is cheaper than a wasted run.
3. **Give the assertion a second, independent observable.** The warhead run was caught by arithmetic that did not fit its own story — the target survived 15 hits it should not have, and a third unit joined far earlier than the intended mark allowed. Neither was the thing under test; both were incompatible with the setup having worked.
4. **When you override anything, restate every field the consumer reads.** See the same `conventions.md` section — this is where "the scenario keeps running and returns a confident number" comes from.
5. **Ask who ELSE could satisfy your predicate.** Steps 1–4 all assume the failure is that your setup did not happen. The third instance above is the other shape: the setup was fine and the assertion was *reachable by another mechanism entirely*. Before running, name every path in the sim that could make the predicate true, and confirm the one under test is the only one. On the bot this bites hardest with **position**: a great many modules move units toward the same believed front, so "unit A ended up near unit B" is almost never attributable. Prefer an observable that only the mechanism under test can produce — for a transport change, that a unit was **carried** (latch that it left the world into a `Cargo`, then measure where it reappears), or the **timing gap** between armour and infantry arrival, rather than the distance between them once both are there.
6. **Then ask WHEN it could satisfy it.** The fourth instance is step 5 done right and still wrong: the observable genuinely was exclusive to the mechanism under test, but only during the window in which that mechanism **owned the actor**. Shared resources on this bot — carriers, squads, the free pool — are claimed, released and reclaimed by different modules across a match, so exclusivity is a property of an interval, not of an actor. Latch your measurement inside the interval and **stop it at the release** (here: freeze the peak at dismount), or you are reading someone else's later use of the same unit. Cheapest guard: emit the quantity you are asserting on from the code under test and read it back from `debug.log`, so the verdict and the mechanism are two independent observations rather than one.

7. **Ask what observable proves the run ENTERED the state you are testing.** Steps 1–6 all assume the setup happened and the question is who satisfied the predicate. The husk-corner instance is neither: the setup ran, nothing else satisfied anything, and the scenario simply never reached the state. So alongside "what would have made this fail?", ask "what would I see if the run got into the situation at all?" — and assert or log it. That scenario now finds its turn at runtime by watching for the first change in step direction and prints the advance it fired on, so "did it corner" is a line in `lua.log` rather than an assumption in the author's head; its straight-leg control lane shouts if the pathfinder turns it, for the same reason in reverse.
8. **Make the verdict self-diagnosing: print the whole board, not the first bad check.** `test-heli-repairs-at-pad` failed on the first of two patients, reported only that one and printed nothing else, so whether the leg that actually measures the fix had worked could not be read from the artefacts at all — it had to be reconstructed from cash arithmetic. Every failure string should carry the full state of every actor under test plus any purse being asserted on, and a trace should go to `lua.log` on an interval. Two related habits from the same run: **a tolerance band hides exactly this class** (that scenario's original ±15% leg would have passed while measuring income-minus-spend, permanently green and permanently meaningless — with the confound switched off, the bill can be asserted *exactly*); and **write attribution INTO the assertion**, because a scenario asserting "this structure must not change hands" cannot say WHICH unit took it, and that ambiguity cost a full round-trip when a technician six cells outside its intended arm captured the derrick an off-switch test was watching and the verdict blamed the off switch. A capture CONSUMES the captor in this mod (`ConsumedByCapture`, `EnterBehaviour: Dispose`), so `actor.IsDead` is a per-unit statement about who did the work — assert that alongside ownership.

Related: a corpus-scanning guard should assert it **measured something** before it asserts it found no violations, or a rename silently converts it into a test that passes by scanning nothing. `StancePositioningFireStanceTest` does this — it asserts it resolved more than zero stance assignments first.

**And a guard over source must pin the CALL SITES, not only the helper.** Three bot resolvers shipped the same map/grid round-trip mistake independently over eleven days, each written by someone who had just read the explanation — the correct guard and a comment naming the hazard sat twenty lines from a broken sibling and did not carry across, twice. **So the explanation is not the countermeasure.** What closed the class was `GridDescentGuardTest`: it walks `Traits/BotModules/**`, finds every grid-descent call site and fails unless the enclosing method tests the degenerate case over those exact variables, and it found the offender by file:line with no prior knowledge of which resolver was broken. The trap it exposes is worth more than the bug: **a pure-math test can be green, correct, and completely uninformative about shipped behaviour when the defect is that the call site does not use the seam the test covers** — `ForwardStagingMathTest`'s parity pins all passed against the broken resolver, because that resolver never called the guarded function. Carry the technique with its safeguard: an **exact-site-count assertion**, which is what stops a scan's scope silently shrinking until it polices nothing (precedent: `BotOrderGateCallerTest`).

### Two Lua traps that make a scenario lie about its own numbers

Both found on 2026-08-15, both cost a run, both look completely normal on the page.

**The failure message is evaluated EAGERLY, at registration.** `TestHarness.AssertWithin(deadline, fn, msg)` takes `msg` as an ordinary third argument, so Lua concatenates it **before the predicate runs even once**. Any counter interpolated into it therefore reports its **initial** value forever — usually zero. Measured: a verdict read `everCarried=0 peakPax=0` while a trace printed from inside the same closure, in the same run, read `everCarried=3 peakPax=2`. The message was not describing the run; it was describing the moment the test was registered. **Put live counters in a periodic `print` to `lua.log`, and keep the failure string static** — or you will diagnose from numbers that were never true.

**There is now a third option, and it is the one to reach for: `timeoutReason` may be a FUNCTION.** `TestHarness.AssertWithin` evaluates it once, at the moment of timeout (`test-helpers.lua:160-161`, documented at `:88-95`), so the note can carry end-of-run state — position, activity chain, counters — that no string built at registration could. Every pre-existing caller passes a string and is unaffected. This matters more than it sounds: a verdict saying only "the unit never went idle" is compatible with opposite root causes, and diagnosing that by reading code instead has already produced one published wrong answer. A failure string returned *from the predicate* is still built at the moment it is returned and was never affected by the eager trap.

**~~`IsDead` is true for a passenger inside a `Cargo`.~~ REFUTED BY DIRECT MEASUREMENT 2026-09-06 — it is FALSE, and the original reading was almost certainly the paragraph directly above wearing a different hat.** Run `260906_091912_p10120_test-combined-arms-rendezvous` (`main @ fc89296a`) printed each rifleman's raw flag from inside the predicate on every roll: all four read `oow/dead=false` while demonstrably aboard the carrier. The code agrees — `Actor.IsDead` is `Disposed || (health != null && health.IsDead)` (`Actor.cs:76`), and boarding calls `w.Remove(self)` (`RideTransport.cs:85`), which clears `IsInWorld` and drops the actor from the id dictionary **without disposing it or touching health** (`World.cs:404-412`). **Where the old claim came from:** it cited `peakPax=2` with `everCarried` stuck at 0, and "dropping the `IsDead` term made it read 3" — the same three numbers the eager-message paragraph above reports for its own frozen verdict (`everCarried=0 peakPax=0` in the message against `everCarried=3 peakPax=2` in the live trace). One observation, read twice, attributed to two causes; the eager message is the one the code supports. **The guidance is unchanged and still worth following**, because it is right for a different reason: latch on `not r.IsInWorld` alone when a separate clause requires the unit to **return** to the world. It is merely permissive (a corpse also latches) rather than necessary. `test-combined-arms-rendezvous` was changed to the permissive form on 2026-09-06 before this was measured; restoring the `IsDead` term there would make its `EverCarried` exact again.

### The converse: an UNCHANGED verdict is not evidence of safety unless you can show the change was live in that run

The rule above is about a green that proves nothing. This is its mirror, and it bites when you run a regression sweep to show a change is harmless: **a test that fails to move is indistinguishable from a test the change never reached.** "Ran 13 scenarios before and after, zero flips" sounds like evidence of safety and is compatible with the change having been completely inert in all 13 — wrong scenario set, a flag that did not apply, a build that did not get picked up. The verdict column looks identical in both worlds, exactly as it does for the false green.

So a no-flip sweep needs its own falsification control: **name the observable that proves the change was ACTIVE inside at least one of those runs.** It does not need to be the thing under test, and smaller is better — you want something the change could touch but the assertion does not read.

Worked example, 2026-08-14 (the `PlayerResources` economy gate, `DISCOVERIES.md` same date). Thirteen graded scenarios were run with and without the gate change at identical seeds; all thirteen returned identical verdicts, and the two failures failed identically on both sides. What made that a safety result rather than a blind one was a single number: in `test-supply-safe-front-keeps-cargo`, same seed and same scenario with only the gate differing, one unit's ammo read `71/100/100/71/70` before and `71/100/100/70/70` after. **One round.** That is worthless as a behavioural finding and decisive as a control — it proves the simulation diverged, so the change was live in that scenario, so the unchanged verdict is a real statement about the assertion rather than an artefact of the change never arriving.

Cheapest sources of such a control, in order: a per-tick telemetry line that already logs a quantity the change touches (`[composition] census` logs `earned`/`spent`); any incidental numeric in a failure note; or a one-line temporary trace. **If every observable in the sweep is byte-identical across the two arms, you have not shown the change is safe — you have shown it did not run.**

### A before/after pair is not an experiment unless both arms carry the same explicit `--seed`

`run-test.sh` defaults to a `DateTime.Now`-derived seed. It records it in `result.json`, so any run is
reproducible *after the fact* — but two runs launched without `--seed` are **two different matches**,
and in a long bot-vs-bot game the between-match variance on a quantity like "how long until the first
kill" is comfortably larger than the effect a one-field rule change produces.

Measured, 2026-09-20, on `test-escalation-full-match`, asking whether the DEFCON 3 border should stand
through DEFCON 2. The unseeded pair was wrong in **both magnitude and sign**:

```
unseeded pair   wall down: first fire t5003, kill t5098   (phase  98 ticks)
                wall up:   first fire t5076, kill t5224   (phase 224 ticks)
                read as: the wall delays contact by 73 ticks and doubles the phase

SAME SEED       wall down: first fire t5003, kill t5098   (phase  98 ticks)
                wall up:   first fire t5003, kill t5038   (phase  38 ticks)
                truth:     the wall delays contact by ZERO, and SHORTENS the phase
```

The 73-tick "delay" was a different opening, not a different rule. **Both runs look equally clean and
nothing in the output distinguishes the two worlds** — which is the same property that makes the false
green and the inert sweep above dangerous. The seed makes the two runs identical up to the tick the
change first bites and divergent only after it, which is the whole of what a controlled comparison is.
It costs one flag: `--seed N` (`run-test.sh:248-249`, validated `:392-403`, passed as
`Test.RandomSeed` at `:823-825`).

Three things that are not obvious:

- **Take the seed from the FIRST run rather than inventing one.** The baseline usually already exists;
  reading its recorded seed out of `result.json` and passing it to the second arm turns a finished run
  into a control for free. Inventing a fresh seed for both arms costs an extra run of the baseline.
- **`--seed 0` is rejected, deliberately.** The engine treats `RandomSeed == 0` as the *unset* sentinel
  and falls back to a wall clock, so it would not reproduce while the harness reported a fixed seed and
  the verdict stamped `"seed":0`. The runner refuses it rather than letting that through.
- **A controlled pair buys you the SIGN and the SIZE of an effect. It does not buy you the CAUSE**, and
  the temptation to infer one is strongest when the result surprises you. Above, *why* the phase got
  shorter came from the order log — ten units firing at one target with the wall up against three units
  over two targets with it down, the band narrowing each unit's valid-target set so the volley
  concentrates. That is an inference off one volley and should be labelled as one.

### A NUnit suite over the math does not cover the wiring that FEEDS it

`b69681d2` shipped 15 NUnit tests over `MissileStrikeApproach` and still left four autotest scenarios
asserting the rule it had deleted. That is not a gap in the suite, it is a **boundary** in it:
`MissileStrikeApproach.For` is deliberately World-free, so it is *handed* the home position, the map
size and the aim points, and every test of it therefore assumes the wiring passes the right three
things. The code that decides which player's `HomeLocation` and whether the standoff comes from
`MapSize` or `Bounds` is `MissileStrikePower.ApproachFor`, and nothing in `OpenRA.Test` can see it.
**The in-game scenario is the only place that binding is exercised** — so "we have unit tests for the
geometry" was never a reason to expect those scenarios green. When a helper is pure by design, ask what
supplies its arguments, and put *that* in a scenario.

Two riders from the same sweep:

- **A degenerate layout can make a directional assertion unfalsifiable, and it looks like coverage.**
  All four scenarios put home, aim point and entry on the same row. On that layout the shipped rule, a
  faction constant, a per-map bearing and a fixed "always from the east" bearing all produce the same
  entry cell, so a bearing assertion there is decoration and only the standoff *distance* can be read.
  It cannot be fixed by choosing a better assertion — every line through home passes near home, so the
  perpendicular deviation of an on-map edge cell is ~1 cell whatever the aim point. **The only fix is
  to move the aim point off the launching player's row.** Verified by simulation: on the moved layout
  an east-constant rule reads 22.8 cells off-axis and fails; on the other three it reads 0.0 and passes.
- **When sweeping for affected scenarios, discriminate on the right property.** Here it was neither the
  scenario name nor the missile actor but the power TYPE — the edge rule was left in place for
  aircraft. Which power types are live: [`architecture.md`](../reference/architecture.md).

## The mirror: a RED is not evidence either, unless the branch under test is REACHABLE at shipped config

*(2026-09-06, from DISCOVERIES.)* `test-supply-safe-front-keeps-cargo` asserts the supply doctrine's quiet
branch — truck closes, serves in place, keeps its cargo. That branch is selected by
`if (drop && Info.DropRequiresDanger && !Info.IgnoreDangerForDelivery && …)`
(`SupplyFollowerBotModule.cs:1662`), and shipped content sets `DropRequiresDanger: true` **and**
`IgnoreDangerForDelivery: true` (`ai/ai.yaml:1895`, `:1689`), which short-circuits the conjunct. So the mode
the scenario asserts was unreachable, its "no crate ever" clause failed any run in which the truck delivered,
and it could only have gone green by the truck failing entirely. It was red from 2026-08-13 to 2026-09-06 and
was written up **twice** as a selector defect. **A test asserting a mode that configuration has switched off
is neither passing nor failing about its subject — it is unwired, and no amount of re-running distinguishes
that from a real defect.** The check is cheap: grep the assertion's own gate for a **bypass flag**, not just
for the mechanism. Three weeks of diagnosis here went into a mechanism that was fine.

**Then re-derive what a false PASS looks like, because fixing reachability INVERTS the failure mode.** With
the gate reachable, all four Lua clauses can hold without the mode gate ever being consulted: any drop decline
(`NoDemand`, `Covered`, `LowLoad`, `NoAnchor`) also leaves `drop = false`, after which the truck takes the
follow path, drives to the platoon and serves from its aura — no crate, cargo kept, ammo up, platoon held.
Green, and evidence of nothing. Lua cannot observe the module's `reason`; only `debug.log` can, so the
acceptance criterion becomes a log line (`[supply] drop-declined … reason=SafeFront`) plus proof the override
merged at all (`[supply] init … ignore-danger=False`). **Whenever you make an unreachable branch reachable,
the clauses written against the old failure mode do not cover the new one.**

**A bypass flag over N sites cannot be cleared "just for the one you want", and the per-site audit is not
uniform.** `IgnoreDangerForDelivery` gates seven sites. On an enemy-free map six are inert or off-path — but
the *arguments differ in strength*, and that is the part worth recording: two are **structurally** equivalent
to the branch they replace, one is **provably** inert (its helper returns null when the straight path's max
danger is under threshold), one is **off-path** (fallback anchor only), and the evac site is inert only **by
threshold** (`EvacDangerUnits: 50` against a field of 0) — a weaker argument, and the first to re-check if a
run surprises. **Record which kind of argument each site rests on; they are not interchangeable.**

**And before deleting or retiring any scenario, grep `engine/OpenRA.Test/` for its name.**
`SupplyDriftClauseTest` parses a constant out of this scenario's `.lua`
(`ReadScenarioConstant`, `:55`) specifically so the assertion cannot agree with itself — and when the file is
missing it calls **`Assert.Ignore`, not `Assert.Fail`** (`:67`). Retiring the directory would have converted
a passing NUnit test into a skipped one, with the suite still reading green.

### Reverting a PROBABILISTIC fix is not a RED arm — compute the overlap before trusting the rerun

The standard way to validate a behavioural scenario is to revert the fix and confirm it goes red. Where
the effect depends on a random draw that is **unreliable, and the arithmetic says so before the run
does**. In `test-forward-deploy-clears-band` the motorized annulus around the package centre is **68
cells**, exactly **one** of which sits behind the DEFCON border. Twenty units drawing from 68 cells miss
that one cell about **three runs in four** — so a green pre-fix run is the COMMON case and proves
nothing at all. Two review estimates were both wrong in the same direction: the brief implied the
overlap was reliable, and an adversarial review computed five cells (P(red) ≈ 76%) against a true one
cell and ≈ 26%.

**So a scenario over a small overlap should assert the SHAPE of the overlap, not where the dice fell:**
68 annulus cells, one forbidden, at a named cell, 67 legal — all deterministic, and all of them move
loudly if the advance percentage, the package radius, the band half-width or the derivation changes.
Keep the probabilistic leg as a regression guard and put the deterministic proof in NUnit.

**And the corollary for reviewers: an overlap count is cheap to compute and expensive to guess.**
Fifteen lines of Python over the same bucket rule the engine uses (`MapGrid.CreateTilesByDistance`
buckets a cell by `ceil(sqrt(dx² + dy²))`) settles it in a second.

### A guard is code, and gets the same scrutiny as an assertion

A scenario guard exists so the run cannot pass over a world that was never built — which makes it the
one piece of the scenario whose own failure is silent by design. Both obvious spellings of one guard
were broken, in opposite directions, and both were live in `test-forward-deploy-clears-band` before
review caught it:

- `Map.LobbyOption(id) ~= expected` **faults on every run**, so the scenario can never pass.
- `Map.LobbyOptionOrDefault(id, expected) ~= expected` **can never fault**, because the fallback IS the
  expected value — a guard that reads as careful and is structurally vacuous.

The cause is a namespace collision that looks like nothing: `Map.LobbyOption` resolves
**`ScriptLobbyDropdown` traits only** (`MapGlobal.cs:112-123`), a separate script-facing mechanism with
its own trait and its own ID namespace. Every option this mod ships — game mode, starting units,
forward deployment, the phase clocks — is declared through **`ILobbyOptions`** instead and is invisible
to it: the miss is logged to the *Lua log*, not the verdict, and the call returns nil. Use
**`Test.LobbyOption(id)`** (`TestGlobal.cs:2141`), which reads `Session.Global.OptionOrDefault` — the
same call the consuming trait makes.

**The general rule outlives the binding: "it only fires when something is wrong" is not a reason to
skip verifying that it CAN fire, or that it can fail to.** Exercise a new guard against a deliberately
broken world once, the same RED-before-green discipline an assertion gets.

## The setup you wrote is not always the setup that ran — check the subject, not the config

Same family as the above, and it landed again on 2026-08-14. A tournament config's `Matchup:` block
(`P1Bot` / `P2Bot`) is **informational only** — `TournamentConfig.cs:8` says so — and the bot that
actually plays is the `Bot:` field on each `PlayerReference` in the scenario's **`map.yaml`**.
Passing `--config` with a new `Matchup` block changes nothing, because `--config` cannot reach
`map.yaml`. A run set up that way completes, writes a verdict, and reports a plausible number **for
the wrong bot**. To change the matchup you must fork the scenario directory and edit `map.yaml`.

The general rule, which outlives this particular field: **when a harness lets you declare the
subject of a measurement in one file and select it in another, assume you edited the wrong one until
the output proves otherwise.** Here the output does prove it — the verdict JSON's `bot_type` comes
from `player.BotType` at runtime, so the summary CSV's `p1_bot`/`p2_bot` columns are ground truth
and will disagree with your config when you have made this mistake. **Read them on every tournament
run before believing the result.** Full blast-radius check (prior committed scenarios are fine) in
`WORKSPACE/DISCOVERIES.md`, 2026-08-14.

Corollary for anything bot-related: **before concluding "the bot never did X", confirm X was
observable.** `AIUtils.BotDebug` is default-off *and* routes to game chat, never to `debug.log`, so
several procurement decisions leave no post-hoc trace whatsoever — a lane can be measured only after
someone adds an unconditional `Log.Write("debug", …)`. "We looked and it wasn't happening" is
worthless when the looking was impossible.

### A query that drives setup must be asserted non-empty — an API can answer a different question depending on WHEN you call it

Third instance of this family, 2026-08-15, and neither rule above catches it. The first two were a
value silently falling back to an engine default and a control that could not go red. This one is
neither: **nothing fell back and nothing was mis-specified.** You could restate every field the
consumer reads, and run both arms, and still get it — because the API answered a different question
on account of *when* it was asked.

The instance: `Map.ActorsInCircle` / `Map.ActorsInBox` **return nothing when called from
`WorldLoaded`.** They resolve through `World.FindActorsInCircle` (`WorldUtils.cs:79-85`) to
`ActorMap.ActorsInBox` (`ActorMap.cs:649`), which reads ActorMap's **position bins**; map-placed
actors only enter those bins via `ActorMap.TickFunction` (`ActorMap.cs:478`), invoked from `ITick` —
the first world tick, *after* `WorldLoaded` returns. Correct code, correct arguments, no error, wrong
answer. **Cell-keyed `ActorMap.GetActorsAt` is immune** (it is updated on add); only the position-bin
queries are affected. Query from inside the polling predicate or behind a grace window — which is
what `test-field-crate-drop` does, and now `test-field-swallows-shell`.

**It was caught only by luck, and that is what the rule is for.** The query happened to feed a guard
whose failure *is* the alarm, so it surfaced as a loud FAIL. The identical mistake in ordinary setup
code — "find the units near X at load and put them on `HoldFire`" — returns an empty list, sets
nothing, throws nothing, and the scenario runs to a confident verdict against a world that was never
built. That is the silent, false-green shape of the same defect, and nothing in the harness would
report it.

So, generalising the practice `StancePositioningFireStanceTest` already follows (it asserts it
resolved more than zero stance assignments before asserting no violations): **when setup is driven by
a query, assert the query RETURNED something before acting on its result. An empty lookup must never
be allowed to mean "nothing to do".** Mechanism and full write-up in `WORKSPACE/DISCOVERIES.md`,
2026-08-15.

### A result dir that two runners wrote does not look wrong — and `debug.log` is the only physical tell

*(2026-09-06, from DISCOVERIES; mechanism read at `tools/autotest/run-tournament.sh`.)* `run-tournament.sh`
derives its settings backup from the result dir **and from nothing else** — `SETTINGS_BACKUP="${RESULT_DIR}/.settings.yaml.bak"`
(`:276`, written `:277`, restored under an `[ -f ]` guard at `:347-348`). No pid, no match index, no scenario.
Two runners pointed at the same `--result-dir` therefore share one backup file, and the `[ -f ]` check is a
TOCTOU window: the other runner's `mv` can consume the file between the test and ours, whereupon `set -e`
(`:44`) kills the batch mid-ladder — so the *second* victim of the race is the one that dies, having already
written a verdict.

**The wreckage passes every completeness check.** The abandoned runner had written matches 1–5; the orphan
kept going in the same dir and wrote 6–10 *and* ran the aggregator. End state: ten `match_*.json`, a
`summary.csv`, a `summary.json`, and a `batch.meta.json` stamping a clean `git_sha` and `git_dirty: false`.
Nothing in the artefacts records that two processes wrote them, because both runners use the same match
indices. **The only physical tell we found was a 0-byte `match_3_debug.log`** (1.15 MB in the clean rerun):
the runner copies the *shared* `%APPDATA%/OpenRA/Logs/debug.log` per match, and the other game had just
truncated it. (Distinct from the 0-byte `lua.log` tell above, which means a script that was never wired in.)

**The cost is attribution, not arithmetic** — a clean rerun reproduced the contaminated dir byte-for-byte,
because the sim is deterministic per seed. But you cannot know that from inside the dir, and **a dir you
cannot attribute is not evidence.** Rules:

1. **Never point a new run at an existing result dir**, and never reuse one after a runner was killed until
   you have confirmed no game process from it is alive. A dir containing `match_*.json` is a used dir.
2. **`verdicts=10` + `git_dirty=false` does not mean one runner produced the batch.** Check the `match_*.json`
   mtimes form a single monotonically-spaced series with no second interleaved cadence, and that no
   `match_*_debug.log` is 0 bytes.
3. **When a batch dies mid-ladder, stop the whole ladder, not just that batch** — the next batch inherits a
   live competitor for the machine's single game slot. Observed: batch 2 started while the orphan still ran,
   and its first two matches shared the CPU with a second game, with nothing in any artefact recording it.

### Clear `debug.log` before the run, or you may be reading the previous run's world

Fourth instance of this family, 2026-08-15, and it is the cheapest to avoid and the most expensive to
suffer. The three above are all about a run that measured the wrong thing. **This one is about not
measuring your run at all.** The scenario was right, the build was right, the code was right, and the
conclusion was still wrong — because the file being read was written by an *earlier* game.

The instance: a composition fix was verified offline, then run live. The `[composition]` census in
`debug.log` showed the pre-fix symptom unchanged, so the fix was judged not to work in the live
game, and three rounds went into re-analysing code that was correct. It was not. The engine writes to
a **single fixed path** (`~/Library/Application Support/OpenRA/Logs/debug.log` on macOS; the harness
also references `${REPO_ROOT}/engine/Support/Logs/debug.log`), and that file already held output from
a previous session. `rm -f` on the log and an otherwise identical rerun inverted the finding
completely: the opening had in fact changed from two medics to line infantry.

**Every cue pointed the wrong way, which is why it survived scrutiny.** The file's mtime was later
than the run's start, so it looked current. It contained the right scenario's player names and the
right per-tick format, so it looked like the right match. Nothing in it is stamped with a run id — so
there is no field you can check to tell whose log you are holding. The harness's own run directory
(`~/.ww3mod-tests/screenshots/<run>/`) contains `result.json` and **not** the engine log, so the two
artefacts you need are in different places with different lifetimes, and only one of them is
per-run.

So: **`rm -f` the engine `debug.log` immediately before launching, as part of the run command, not as
a separate step you might skip.** And the general rule, which is the one worth carrying: **an
artefact at a fixed path with no run identity is not evidence about a particular run unless you
personally emptied it first.**

**Under concurrent workers that clearing rule becomes a destructive race, so COPY the log out, do not
just read it in place.** The path is global and unlocked, and `run-test.sh`'s single-instance guard
protects the *game*, not the *log* — so the moment your run ends, the next worker's `rm -f` is free
to fire. Observed 2026-08-15: a run finished at 11:47:55, another worker's run cleared the log at
11:48:07, and a copy issued in the same shell line as the runner still lost the race by seconds. The
run had executed correctly and produced a full log; the evidence simply no longer existed, and the
run had to be spent again. **When other workers may be active, poll-copy the log to a private path
while the run is in flight** — `tools/autotest/poll-copy-logs.sh <dest-dir>` exists for exactly this;
run it in the background and stop it the moment the runner returns. Copying once after the run exits
loses the race; copying during it captures the file while it is still being written and makes your
evidence independent of anyone else's cleanup. A stale log gives you a wrong answer; a deleted one
gives you none, and both cost a run.

**Do not "only copy if the source grew" — that guard inverts and preserves the stale file.** The
engine opens each log with `File.CreateText` (`Log.cs:160`), i.e. **truncate**, so at the start of a
run the live file is *smaller* than the previous run left it. The obvious defence against a competing
`rm -f` therefore refuses to copy for the whole run and hands you precisely the previous run's file it
was written to protect you from. Measured 2026-08-15: the guard held a 283,160-byte file from an
earlier session while the run under measurement wrote 238,451 bytes, and the resulting copy contained
**zero** lines from its own run. The tell is cheap — the copy is byte-identical in size to what you
recorded before launching. Copy unconditionally.

**And an erased log reads as a finding, which is the dangerous part.** A missing `departing aboard=`
line looks exactly like "the transport never departed"; a truncation by a concurrent worktree's launch
looks like nothing at all, because `result.json` still says PASS. Same family as the "measured
nothing" shapes above — an absence manufactured by the instrument rather than by the system.
**Before treating any empty grep as evidence, `stat` the log and confirm its mtime still falls inside
your run's window** — or grep the copy in your run dir and never the live file. A timeout or crash makes this worse, not better — the harness gives up
on its watchdog while the game keeps writing, so the log can be simultaneously stale at the top and
still growing at the bottom. When a live result contradicts a solid offline result, **suspect the log
before the code.** Full write-up in `WORKSPACE/DISCOVERIES.md`, 2026-08-15.

### The `debug.log` in your run directory can be a DIFFERENT GAME'S log

*(2026-09-03, `wt/capture-fix`.)* Two scenarios failed and their run directories were handed over as
"the only evidence". Neither `debug.log` was from the run it sat in. Checked, not assumed:

| check | what the run dir held | what that scenario is |
|---|---|---|
| max `tick=` in log | **10613** | ended at tick 701 |
| players named | Experimental AI 1–4 | only Neutral, USA, Russia |
| actor coords | `oilb#1299@80,78` | `Bounds: 1,1,64,32` — y=78 cannot exist |
| mentions its own actors | **0** | `TechHoldFire`, `DerrickQuiet`, … |

The sibling directory was the same shape. **Both files ended mid-line on a bare `[`** — the signature
of copying a file another process is still appending to. This is the copy-side twin of the clearing
race above: same global unlocked path, same absence of any run identity in the file.

**Consequence: for a `--hidden` run, `result.json`'s verdict is the ONLY trustworthy artefact.**
Screenshots are listed but never written (rendering is suspended — see
[`conventions.md` §"`--hidden` autotest runs write NO screenshots"](../reference/conventions.md)), the
logs may belong to someone else, and there is no third channel. **Anything you want to know afterwards
has to be put there by the scenario itself, in the verdict string** — which is why the "print the whole
board" and "write attribution into the assertion" habits above are not stylistic.

### The instrument reads a state that LAGS the thing you just asked for

Four separate runs were lost to one shape: the scenario asks the engine to do something, then measures
too early or measures a field that is not the one it means. All four are cheap to avoid and none
announces itself.

**1. `IsIdle` is TRUE both before an order lands and after it finishes.** `Actor.IsIdle` is
`CurrentActivity == null` (`Actor.cs:75`), and `Test.IssueMoveOrder` goes through `World.IssueOrder`, so
the order sits in the queue and becomes an activity a tick or more later. A
`WaitUntil(…, function() return unit.IsIdle end, …)` written to mean "wait until he has walked there"
therefore fires on its **first poll**, before he has taken a step. It then fails silently and
downstream: that scenario issued its attack order from the man's SPAWN cell, the order was correctly
refused as out of arc, nothing re-issued it when he did arrive, and the verdict twenty seconds later
re-evaluated the same question at his FINAL cell and got the opposite answer. The run reported "the
in-cone shooter landed nothing", which is true and tells you nothing. **Wait on POSITION, not on
activity** — poll `Location` against the target cell with a cell of slack (so a blocked exact cell
parks the man next door instead of hanging the run), then a settle beat, then act.

**2. A ticked accessor read from `WorldLoaded` returns its uninitialised default, and that default is
usually a plausible number.** `Detectable.CurrentVisibility` is written in exactly one place —
`ITick.Tick` (`Detectable.cs:131`) — and `Test.GetVisibilityLevel` returns it verbatim
(`TestGlobal.cs:471-478`). `World.LoadComplete` runs every `IWorldLoaded` **before the first tick**
(`World.cs:320-347`), so a premise check there read **0**, a value the clamp under test cannot produce
at all (`ClampConcealment` returns 1 for any input below 1, `Detectable.cs:120-121`), and the scenario
aborted at its own premise having never exercised the thing it was written for. The failure text named
three numbers and invited the reading that the clamp had produced the 0.

  **So learn the sentinel conventions of any `Test.*` binding you read — several have more than one
  zero-ish return and only one of them is about the subject.** For `GetVisibilityLevel`: **-1** = no
  `Detectable` trait, **0** = not ticked yet, **>= 1** = a real level. A scenario checking `< 0` to mean
  "trait missing" passes happily on a 0 that means something else entirely. Any tier read must sit
  behind a `Trigger.AfterDelay`; only per-cell map queries like `Test.GetDensity` are safe at
  `WorldLoaded`. (Same family as the `Map.ActorsInCircle` position-bin trap above — an API answering a
  different question depending on WHEN it is called.)

  **And read what the assertion MEASURED before reading what its prose claims.** Asking whether the code
  under test can produce that value at all settled this diagnosis in one step. A premise check that fires
  before the state it guards exists converts a working fix into a red run, and its message will describe
  the fix rather than the timing.

**3. Three states, not two — and `Test.ConditionCount` cannot see the interesting one.** It opens with
`if (!TestMode.IsActive || actor == null || actor.IsDead || !actor.IsInWorld) return 0;`
(`TestGlobal.cs:1016-1017`), and `Cargo.Load` removes the passenger from the world. So **a scenario can
never read a condition on a man inside a building or a transport, and gets a silent `0` rather than an
error.** The specific bite is that `Passenger.CargoCondition` is granted *precisely because* he boarded,
making the most natural "is he inside?" instrument a guaranteed false negative rather than a flaky one.
It cost two runs and produced a contradiction that looked like a game bug: one scenario reported
`House owner USA` — which only happens when a man boards — while simultaneously reporting
`UsMan loaded=false`.

  | state | what to ask | why |
  |---|---|---|
  | **inside** | the trait — `Test.IsLoadedInto(passenger, transport)` (`TestGlobal.cs:868`) reads `Cargo.Passengers`; `Test.IsAtGarrisonPort(soldier, building)` (`:989`) reads `GarrisonManager.PortStates` | true regardless of either flag |
  | **outside** | `IsInWorld` | sound in ONE direction only: in-world-and-not-loaded really does mean standing outside |
  | **GONE** | `IsDead` | the only state in which it means what it says — a passenger is **not** dead, so a dead-and-out-of-world actor was genuinely killed |

  That last row is not pedantry. In one of those runs `RuMan.IsDead` was true while out of world, which —
  passengers being excluded — meant he had genuinely been killed. That was the real finding hiding under
  the instrument bug, and reading `IsDead` as "he is aboard" would have buried it. The ownership flip
  (`DynamicOwnership`) remains the best *aggregate* proof that somebody got in, and it is the one
  instrument that behaved correctly in every run.

**4. Ask the engine for a value it chooses at runtime; do not encode your prediction in the map.**
Three runs were lost to assuming which garrison port a man lands on. `GarrisonManager` deploys to the
first port that confirms an in-arc, in-range target, so the winner depends on every enemy on the map, the
range of the weapon that scans, and the port declaration order. The trap underneath all three diagnoses:
with `Cone: 140` on four diagonal ports the cones **union to the whole circle**, so there is no bearing
that is "behind the building" — only behind *the port this man is standing at*. Placing a shooter by
compass direction is meaningless. **The fix that ends the class rather than the instance:** read the port
at runtime (`Test.GarrisonPortOf`, `TestGlobal.cs:935`), derive both shooters from its yaw, and re-read it
before the verdict so a mid-measurement port swap Skips instead of being reported as an arc result. Every
one of those three runs would have been saved by printing the port, and the same family of instrument
(`Test.TargetableReport`, `:961`) settled a fourth mystery in one run after three rounds of reading code.

**The instrument that catches all four: PRINT THE SAME FACT AT TWO MOMENTS.**
`CONE-SHOT … shooter cell 24,8 | canTarget=false` at order time, against
`canTarget=true … isValidFor=True` in the verdict. Neither line alone is a diagnosis; **the DISAGREEMENT
is**, and it names the moment the order was issued as the thing that was wrong rather than the geometry,
the arc, or the trait. Three earlier rounds on that scenario each printed one moment and misread it.

**Staging: prefer `Test.ClickOrder` over the direct-activity helpers.** `MobileProperties.EnterTransport`
queues a `RideTransport` activity directly; `Test.ClickOrder` issues a real order through the targeter a
player's click would use — so a staging failure is also a finding, and the returned order string gives a
cheap setup assertion that turns a silent no-op into a named SKIP. On one tree, one map and one building
type, the second walked a rifleman seven cells into a building and flipped its ownership while the first
moved nobody two cells in 25 s.

### Timing an event-driven scenario: anchor on what you OBSERVED, never on a duration you looked up

**Do not encode a flight time. The lobby can cut it to a third, and a dozen-plus scenarios set that
option.** `PowersLobbyOptionsInfo.SandboxRemovesLaunchDelay` **defaults to true**
(`PowersLobbyOptions.cs:168`), and `MissileStrikePower` then takes `baseMissileDelay = 0` (`:624-626`),
leaving the arc alone. That is not a rare configuration — the powers sandbox is what supplies the
`powers.event` prerequisite no faction provides, so every scenario needing a game-ender or an event-tier
power turns it on (15 scenario `rules.yaml` files as of `5a3e4b19`; the count grows, so **recount rather
than quoting it**: `grep -rl "PowersSandboxCheckboxEnabled: true" tools/autotest/scenarios/*/rules.yaml`).
On a 66x34 map a B61Low measured **110 ticks** with the sandbox on against ~310 with it off.

What that cost: a retiming assumed the long column, concluded a flight outlasts any compressed side
cooldown, and built a phase around firing the victim's own warhead as an incoming one detonated. With the
real flights the cooldown *outlasts* the flight, so the victim was still reloading and could not fire —
`charging:43`, run dead at t382. Polling for readiness would not have saved it: the gap between the
victim becoming ready and the escalation landing was **two ticks**.

**The lesson is not "look up the flight time". It is that a scenario should not encode one at all.** The
fix kept an event-driven watch, deleted the construction, and raised the side cooldowns so the natural
situation holds — with each phase checking its own precondition first and reporting
`SCENARIO SETUP: … must be raised` rather than looking like a defect in the build.

**Anchor on the event, and the anchor also tightens your bound.** A support power's countdown does **not**
run while the power is disabled: `SupportPowerInstance.Tick` pins `remainingSubTicks` back to
`TotalTicks * 100` on every tick `instancesEnabled` is false, and returns early when `!Active`
(`SupportPowerManager.cs:397-405`). So although the constructor sets a full interval (`:377`, `:384`) and
an unarmed band *looks* as if it has been counting since match start — the vacuity trap a 2026-09-15
review found in `test-nuclear-ender-level` — a band's interval really starts **at the rise**, not at match
start. A scenario anchored on the tick the band was *seen* to leave `hidden` is anchored on the condition
crossing from the World actor to the Player actor, so all that remains is one `ServicePendingReady` pass:
a **~12-tick** gap is safe where a rise-anchored check needed more than `GrantRetryTicks` (30) and used
40. **The check got stronger by being retimed, not weaker.** A scenario can then assert "the level rose
AFTER the explosion and within `EscalationDelayTicks` of it" without knowing any flight time at all.

**`Test.GetImpactEffectCount` is the instrument for that, and it is GLOBAL — which is both why it works
and why it expires mid-scenario.** It counts `CreateEffectWarhead` impacts past the validity gates, and
every nuclear weapon carries exactly one such warhead, so a delta on it is a detonation you did not have
to predict. Being global, it also catches a shot from something the script never names, which makes it the
best "nothing fired" observable there is — **in a phase where the scenario itself has ordered no shot.** It
cannot say WHICH warhead moved it, so a scenario that fires an unwatched shot must wait for that to land
before the next watch takes a baseline.

**Hence: a per-phase observable SET, not a per-phase threshold.** A hold-fire scenario that (1) asserts
silence, (2) issues an order to prove ordered fire is still permitted, then (3) asserts the victim does
not retaliate cannot carry one observable set across all three — in phase 3 the impact counter, the
shooter's ammunition and the victim's health all move *because phase 2 asked them to*. This is the
false-RED mirror of the false-green trap: the assertion would have failed the treatment for doing exactly
what the scenario told it to do. Write the phases as separate functions with a comment at each naming
which quantities are legitimately moving by then — a single function taking a flag was tried first and
reads as if the difference were a matter of strictness, which it is not.

## Gotchas

These bit during development. Documenting so they don't bite again.

1. **Build cache lies**. `make` reports success without picking up edits to a single file occasionally. If a trace doesn't fire, `touch <file>.cs && make` to force rebuild.
2. **`AttackTurreted` overrides `CanAttack`** — short-circuits on `turretReady = FaceTarget()` *before* calling `base.CanAttack`. If you trace `AttackBase.CanAttack` and see no fires, your override is gating earlier.
3. **`Activity.IsCanceling` is false in `OnLastRun`**. The framework sets `State = Done` *before* calling OnLastRun, so the cancel flag is already cleared. To detect "ended because something replaced me", check `NextActivity is X` instead.
4. **Window placement**: default is centered + background (visible but defocused) + muted. Use the L/R/F shorthand for side-docked layouts (`./tools/autotest/run-test.sh L <test>`). `--visible` keeps it foreground; `--audio` keeps sound on; `--minimized` opts back into the old SDL miniaturize behavior (which on macOS can only be restored via the dock icon next to Trash, not Cmd+Tab).
5. **Lua force-attack vs UI force-attack** are *not* always equivalent paths. `Paladin.Attack(t90, ..., forceAttack=false)` hard-codes `queued: true` (existing OpenRA API quirk); `Paladin.AttackGround(...)` defaults `queued: false` to mimic Ctrl+click replace.
6. **Never read `$HOME/.ww3mod-tests/result.json`** — and never `rm -f` it, which the old advice here told you to do. It was a single shared path, and that destroyed or misreported a verdict three times in two days: one run's `rm -f` deleted a verdict another run had already earned, and a run that read it got a stranger's result. It is now overwritten with a `"status":"moved"` stub that cannot be mistaken for a verdict. Read the per-run `result.json` the runner prints as `Run dir:` instead.
7. **The harness snapshots and restores the whole `settings.yaml`, so nothing the engine persists during a run survives it.** `run-test.sh:633-634` copies the live file to a backup before launching and `:773-774` `mv`s it back unconditionally afterwards; `screenshot-lobby.sh:135-136`/`:177-178` do the same. The stated purpose is narrow — keep a test's `Sound.Mute` / `Graphics.Mode` out of your normal launches — but the mechanism is a **whole-file** snapshot, so every key written inside that window is discarded, including ones the engine wrote for legitimate reasons. Consequences: a settings value cannot carry state between autotest runs, and a "show this once" feature keyed on one cannot be tested through this harness at all — its flag is written, saved, and thrown away, and the feature re-fires with no bug in its own gate. If you need a persisted key to survive, exclude it from the restore or have `TestMode` suppress the save; do not assume the file you read afterwards reflects the run.
8. **Never put a visibility assertion on a vision-band boundary — it is a coin flip between runs.** Vision is graded into discrete concentric bands (`^StandardVision`, `mods/ww3mod/rules/defaults.yaml:47-` — Strength 10 out to 4c0, 9 to 7c0, 8 to 10c0, …) and `Detectable` reveals an actor when a band reaching it still carries enough strength. **The threshold is not a per-type constant:** `Detectable.Tick` recomputes it every tick from `IDetectableAddativeModifier`s (`engine/OpenRA.Mods.Common/Traits/Modifiers/Detectable.cs:78-80`), and `^DetectableInfantryStandard` (`mods/ww3mod/rules/ingame/infantry.yaml:703-721`) adds +1 for `prone`, +1 for `dugin` and up to +3 for `object-proximity` cover. So the same rifleman needs a stronger observer while prone or in cover than standing in the open, and a scenario placed at exactly the band edge flips on posture alone — no seed, position or rules difference. Two runs of one scenario disagreed about whether three units were spotted for exactly this reason, and the clean-looking split was nearly reported as evidence. Compute the band and pick a distance at least one full band clear of the threshold.
9. **PITFALL: do not silence the unit-under-test with `Stance = "HoldFire"`** — silence the ENEMY instead (enemy on `HoldFire`, plus `Targetable: TargetTypes: NoAutoTarget` on the enemy in the scenario's `rules.yaml`, so your unit never acquires it and never leaves idle). Fire stance is not inert setup: `StancePositioningExecutor` opts out entirely below `FireAtWill` (`StancePositioningExecutor.cs:318`), so the convenient "no shots ⇒ no suppression, no chase" trick can switch off the very trait you are testing. This silently killed three stance scenarios for six weeks (2026-08-11 in `WORKSPACE/DISCOVERIES.md`); `StancePositioningFireStanceTest` now fails the build if a `test-stance-*` scenario does it. **Generally: before using a unit property as setup convenience, check that no gate reads it.**

## Engine integration points

For when you need to extend the harness (not just use it):

| File | Role |
|---|---|
| `engine/OpenRA.Game/TestMode.cs` | Static class — IsActive, Name, Description, ResultPath. Reads launch args. |
| `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/TestModeLogic.cs` | Mounts the in-game panel (title, description, RESTART button, End hotkey) |
| `engine/OpenRA.Mods.Common/Scripting/Global/TestGlobal.cs` | `Test.Pass / Fail / Skip` Lua bindings |
| `engine/OpenRA.Mods.Common/Scripting/Global/UserInterfaceGlobal.cs` | `UserInterface.Select` Lua binding |
| `engine/OpenRA.Mods.Common/Scripting/Properties/CombatProperties.cs` | `Paladin.AttackGround` Lua method |
| `engine/OpenRA.Mods.Common/Widgets/ViewportControllerWidget.cs` | Edge-pan disabled when test mode + windowed |
| `engine/OpenRA.Platforms.Default/Sdl2PlatformWindow.cs` | Honors `OPENRA_WINDOW_X/Y` env vars for window positioning |
| `mods/ww3mod/chrome/ingame-testmode.yaml` | Panel layout |
| `mods/ww3mod/scripts/test-helpers.lua` | Reusable Lua helpers |
| `tools/autotest/run-test.sh` | Single-test runner |
| `tools/autotest/selftest.sh` | Self-test of run-test.sh's result reporting (no game launched) |
| `tools/autotest/run-batch.sh` | Batch runner |
| `tools/autotest/expected-status.sh` | Declared expected status + its decision table (`--selftest`, no game launched) |
| `tools/autotest/list-tests.sh` | Discovery |

## Existing tests

- `test-artillery-turret` — manual: does the Paladin's turret rotate before firing?
- `test-paladin-fires` — auto: Paladin's primary ammo drops within 12 s of force-engage on t90 (HoldFire). Demonstrates the green path.
- `test-arty-force-attack-during-setup` — auto, currently RED: force-attack-ground during setup-ticks. Layer 1 of the bug fixed (commit 51db91f7); Layer 2 (turret stalls mid-rotation) still open.
