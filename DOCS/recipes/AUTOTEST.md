# AUTOTEST — Automated test-driven debug loop

**Trigger:** the word `AUTOTEST` in a user message — explicit (`AUTOTEST <bug>`), batch (`AUTOTEST after I decide`, `AUTOTEST these items`), or simply naming the workflow. **The trigger sets the stance for the whole batch of fixes that follows, not just the first one.** Each item runs the full RED → fix → GREEN cycle; the trigger word does not need repeating.

**Apply automatically (no trigger required) when** the work fits the loop. Before declaring a behavioural fix done:

1. Did this change behaviour that could be observed in-game (firing, moving, ammo, conditions, kills, …)?
2. Could a deterministic Lua predicate verify it (`AssertWithin`, ammo drop, `IsDead`, …)?
3. Is the change non-trivial (more than a typo, a single-value tweak, or removed dead code)?

**Yes / yes / yes → write the test BEFORE the fix.** RED → fix → GREEN → commit together. This is the default for behavioural fixes in RELEASE mode.

**Gives you:** a deterministic test, RED-then-GREEN proof of the fix, regression coverage going forward, and a commit you can trust days later. The verdict comes back as a named outcome and an exit code.

**When *not* to use it:** visual / "feels off" / tuning bugs (use **PLAYTEST**), trivial code, no-code-change work, or **"show me X in game" requests where the user wants to look around** — that is **DEMO**. AUTOTEST loops to a verdict; DEMO stages and stops.

**Who launches.** Every step below that starts the game — RED, GREEN, regression — is run by whoever CLAUDE.md §"Who runs what" assigns it to. A worker dispatched by a manager never launches: it writes the scenario, states exactly which outcome counts as the answer (e.g. *"RED = `FAIL` with `never fired`; GREEN = `PASS` with a note naming the tick"*), and hands both up. An agent working directly with the user may run one `./tools/autotest/run-test.sh --hidden <test>` for the bug at hand; batches, tournaments, repeated runs and a third rerun of one test need the user's go-ahead in the current turn.

---

## What the harness is

The game can be launched into a small, deterministic scenario; the verdict (pass/fail/skip) is written to a per-run JSON file and exit-coded back. It is activated only by the `Test.Mode=true` launch arg — normal launches are unaffected.

## Quick reference

```bash
./tools/autotest/list-tests.sh                               # what's available
./tools/autotest/run-test.sh --hidden <test>                 # one run, no window (unattended profile)
./tools/autotest/run-test.sh <test>                          # one run, centered, background, muted
./tools/autotest/run-batch.sh <t1> <t2> ...                  # several (needs go-ahead; see above)
./tools/autotest/run-batch.sh --all                          # every test-* with a verdict call (needs go-ahead)
./tools/autotest/run-test.sh L <test>                        # left half (also R, F, C)
./tools/autotest/run-test.sh --visible <test>                # foreground (aliases: --no-minimize, --foreground)
./tools/autotest/run-test.sh --audio <test>                  # keep sound on
./tools/autotest/run-test.sh --speed 8 --timeout 900 <test>  # long scenario: see the watchdog section
./tools/autotest/run-test.sh --map <shipped-map> <test>      # run a SHIPPED map on this scenario's rig
./tools/autotest/run-test.sh --help                          # flag list (first part of the header)
```

The launchers live in `tools/autotest/`, not at the repo root; `./run-test.sh` from the root is exit 127 — a launch failure, not a result.

**`--hidden` never maps a window and suspends rendering, so it writes no screenshot PNGs** — the capture is still listed in `result.json` and `manifest.json` (see the logs section). Use it for assertion scenarios; use the default `--background` for any scenario whose answer is a frame.

**Window placement.** Default: centered (~90% × ~85%), background (visible but immediately defocused), muted. Focus is restored to whatever app was frontmost at launch. If the user puts `L`, `R`, `F` or `C` in the trigger ("AUTOTEST L"), pass it as the first positional arg. `--minimized` is the legacy SDL miniaturize behaviour (on macOS restorable only from the dock icon next to Trash).

**`--map NAME` loads a directory under `mods/ww3mod/maps/` while the scenario directory still supplies the run rig** (test name, description, result path, screenshot dir); its own `map.yaml`, `rules.yaml` and Lua are not loaded. It works because `Game.LoadMap` matches `Launch.Map` by UID *or* by package directory name. `run-test.sh` validates the name itself, because `Game.LoadMap` throws on a miss and a caller's typo would otherwise be graded `CRASH`. A shipped map carries no Lua and so cannot reach `Test.Pass`; `Test.SmokeTicks=<N>` plus the `SmokeTestExit` world trait gives it a route to a verdict, which is what `make smoke` / `.\make.ps1 smoke` uses.

### Outcomes and exit codes

Exit codes: `0` pass, `1` fail, `2` skip, `3` error. **Read the verdict line, not just the exit code.** Every run ends with

```
AUTOTEST_VERDICT outcome=<OUTCOME> exit=<n> test=<name> run=<run-id>
```

| OUTCOME | exit | meaning |
|---|---|---|
| `PASS` | 0 | the scenario answered yes |
| `PASS-EMPTY` | 0 | a pass whose `notes` is empty — it answered but said nothing; `run-batch.sh` counts it separately |
| `FAIL` | 1 | the scenario answered no |
| `TIMEOUT-FAIL` | 1 | the watchdog killed a run that never answered |
| `SKIP` | 2 | the scenario declined (usually a failed precondition) |
| `LAUNCH-FAIL` | 3 | the local server refused the client at join; no world was built and nothing ran. Detected from `server.log`'s `Dropping connection` or `client.log`'s `Connection to … failed`. **Not a test result.** |
| `CRASH` | 3 | the game threw; the exception log is named. Sometimes the crash *is* the finding (a sync guard firing). |
| `NO-RESULT` | 3 | the game exited without a verdict — hung, closed by hand, or never launched |
| `BAD-VERDICT` | 3 | `result.json` was unparseable |
| `INTERRUPTED` | 3 | Ctrl-C / killed |
| `HARNESS-ERROR` | 3 | the runner itself failed (bad flag, lock held, …) |

**A launcher exit of 126/127, or a fast non-zero exit with no output, is a launch failure — nothing ran.** Same for `NO-RESULT` on a fresh worktree: `launch-game.sh` refuses to start (`Required engine files not found.`) when `engine/bin/OpenRA.dll` is missing **or `engine/VERSION` does not match**, and `engine/bin` is not in git, so a new worktree has no build until `make all` / `.\make.ps1 all` runs there. Tells: run dir empty, `lua.log` 0 bytes, `test -f engine/bin/OpenRA.dll` fails. Being built is a property of the worktree, not of the change.

**PITFALL: `run-test.sh <test> | tail` reports `tail`'s exit status, so a FAIL arrives as exit 0.** The verdict line is printed last (so a tail-truncating filter still shows it) and non-PASS outcomes also go to stderr, but the exit code is the caller's to keep:

```bash
./tools/autotest/run-test.sh --hidden <test>; rc=$?      # capture first, filter after
```

A parent script should set `AUTOTEST_OUTCOME_FILE=<path>`: the runner writes `outcome=… exit=… test=… run=…` there from the same EXIT trap that prints the banner, on every exit path.

**Results are per-run.** Each invocation writes `~/.ww3mod-tests/screenshots/<timestamp>_p<pid>_<test>/result.json` — printed as `Run dir:` — beside that run's screenshots and lifecycle log. The shared `~/.ww3mod-tests/result.json` is a `"status":"moved"` stub: never read it, never `rm` it. Run dirs older than 7 days are deleted at the start of each run.

`./tools/autotest/selftest.sh` proves the outcome reporting and `run-batch.sh`'s grading without launching a game (stub launcher, sandboxed `HOME`). Run it after touching `run-test.sh` or `run-batch.sh`. The launch-failure detector has its own launch-free check, `sh tools/autotest/selftest-launch-failure.sh` — invoke it through `sh`: the file is tracked mode `100644`, so `./tools/autotest/selftest-launch-failure.sh` dies with exit 126.

### A scenario that is SUPPOSED to fail must say so

`run-batch.sh --all` includes every `test-*` scenario that contains a verdict call, so a by-merit negative (a knowingly-unfixed layer, "within tolerance") becomes a permanent false FAIL in every tally — which is how a red batch stops meaning anything. Declare the outcome in the scenario's folder:

```
tools/autotest/scenarios/test-<name>/expected-status
----------------------------------------------------
fail
The negative arm is by merit: the preference has not landed yet. Delete this file
when it does and the run goes green.
```

First non-comment line is `fail`, `skip` or `pass-empty`; the reason below it is **required**. The declared outcome occurring is green (`OK(fail)`); **the declared outcome no longer occurring is RED (`STALE`)** — the same can-only-be-lowered-on-purpose floor as `mods/ww3mod/lint-baseline.txt`. A declaration silences exactly the outcome it names: a scenario declared `fail` that starts crashing still reds.

**A declaration is only satisfied by a scenario that reached a verdict under its own power.** A hang, crash, Ctrl-C or unloaded rules is red under any declaration and listed under `!! NEVER REACHED A VERDICT`. `run-batch.sh` grades on the OUTCOME NAME carried in `AUTOTEST_OUTCOME_FILE`, never on the exit code (exit 1 is both `FAIL` and `TIMEOUT-FAIL`); a missing or contradictory name is `NO-OUTCOME` / `OUTCOME-MISMATCH`, red. Decision table and a launch-free selftest: `./tools/autotest/expected-status.sh --selftest`. `ls tools/autotest/scenarios/*/expected-status` lists the current declarations.

**Known hole in `--all`:** its filter greps only `scenarios/<name>/*.lua` for a verdict call, so a scenario whose verdict lives in a shared `mods/ww3mod/scripts/*-lib.lua` (or `balance-helpers.lua`) loaded through `Scripts:` is listed under "Excluded from --all" and never graded. Filed in `WORKSPACE/bugs/discovered.md`. Run such scenarios by name.

---

## The 300-second watchdog, and the scenarios it cannot finish

**Every `run-test.sh` run carries a wall-clock watchdog, default 300 s** (`TIMEOUT_SECS`; override with `--timeout N`). If the game is alive and no verdict exists after that, it kills the game and reports `TIMEOUT-FAIL`. **It is pure wall clock and deliberately NOT scaled by `--speed`**, so long scenarios need both flags.

**`--speed N` (1–16) is free.** It becomes `Test.SpeedMultiplier=N`, which `TestModeSpeedMultiplier` applies at world load as `world.Timestep = max(1, timestep / N)`. A 24-minute scenario runs in ~3 at `--speed 8`; `--speed 8 --timeout 900` is the workhorse pair. **`run-batch.sh` defaults to `--speed 8`** (`BATCH_SPEED`), unlike `run-test.sh`, which defaults to 1×.

**The simulation stays byte-identical under `--speed`.** The gameplay traits that read the *mutable* `world.Timestep` (`TimeLimitManager`, `NuclearUnlockClock`, `DefconEscalation`) read it once in their constructor; the world actor's traits are constructed before `IWorldLoaded` runs (`World` constructor, then `World.LoadComplete`), and the multiplier lands in `IWorldLoaded`. Every other consumer is pacing, rendering or logging, or reads the immutable `world.GameSpeed.Timestep`.

**Tick rate is 16.67/s** — `DefaultSpeed: default` → `Timestep: 60` ms in `mods/ww3mod/mod.yaml` `GameSpeeds`. So 5000 ticks = 300 s, the most the default invocation affords. **Never write 25 tps.**

### Scenarios the default invocation cannot complete

| Scenario | Budget | Wall @ 1× | Run it as |
|---|---|---|---|
| `test-escalation-full-match` | `TimeLimitTicks: 22000` (`rules.yaml`), outer `DEADLINE = 24000` (`.lua`) | 22–24 min | `--speed 8 --timeout 900` |
| `test-rank-accumulation` | `DeadlineTicks = PhaseBTick + 4300` = 11016 | ~11 min | `--speed 8 --timeout 900` |
| `test-experimental-buys-special-forces` | `DEADLINE_TICKS = 9000` | 9 min | `--speed 4 --timeout 600` |
| `test-experimental-msar-deploy` | `DEADLINE_TICKS = 6000` | 6 min | `--speed 4 --timeout 600` |

This is a list of known cases, not an audit — new long scenarios land regularly. Size any scenario you write against 5000 ticks, and when reading a `TIMEOUT-FAIL`, compare the scenario's own budget to that.

**"Cannot complete" means different things per row.** `test-escalation-full-match`'s time limit is the *subject*, so at 1× it cannot reach a verdict at all. The others carry a give-up cap: a passing run may finish early and honestly, but the **failing** path is unreachable — the watchdog fires first and replaces the scenario's own diagnostic (which names the unit and tick) with a generic `TIMEOUT-FAIL`. A `TIMEOUT-FAIL` on these rows at default settings says nothing about the code; rerun with the flags shown. Inside `run-batch.sh` (default `--speed 8`) `test-escalation-full-match`'s 22000 ticks fit under 300 s.

`AssertWithin` budgets are converted at the real rate, so `test-combined-arms-rendezvous` (`DeadlineSeconds = 200`) and `wip-transport-delivers` (`DeadlineSeconds = 180`) are 200 s and 180 s and fit — with 100–120 s left for map load, which has not been measured. If either reports `TIMEOUT-FAIL` rather than its own `within Ns` message, raise `--timeout` before reading anything into it.

### Tournament runs have their own clock

`run-tournament.sh` does not use `run-test.sh`'s watchdog. Its per-match cap defaults to `TimeLimitSeconds × 4 / SpeedMultiplier` (minimum 60 s; 600 s if the config has no time limit), and it **budgets the full multiplier even though a slow host does not achieve it**: `tournament-eco-5min.yaml` gets 300 × 4 / 8 = 150 s, and a macOS run delivering ~15–22 ticks/s against the 200/s an 8× of 40 ms asks for was culled before the first supply truck existed. **Pass `--max-wall-secs` explicitly for anything you need to finish.** `BotVsBotMatchWatcher`'s `WorldLoaded: speed multiplier Nx` line proves the multiplier applied and the host simply could not keep up.

**A tournament's duration depends on its `GameSpeed:` key — read it before reasoning about time.** `run-tournament.sh` passes the key as `Test.GameSpeed`, and `TournamentConfig` converts `TimeLimitSeconds` with the resolved timestep. Configs setting `GameSpeed: fastest` run at 40 ms (25 ticks/s); configs setting none run at the 60 ms default. Count each with `grep -l 'GameSpeed: fastest' tools/autotest/scenarios/*/tournament*.yaml tools/autotest/tournament*.yaml`. An unknown speed key silently falls back to `default` (comment in `Game.LoadMap`), which is why `BotVsBotMatchWatcher` logs the timestep it resolved.

Three launch traps in the same script, each ending in exit 3 or a missing verdict with nothing run:

- **Some ladder scenarios ship no `tournament.yaml`** (e.g. `tournament-s1-eco-river-zeta` carries only `tournament-eco-5min.yaml`), and `--config` defaults to `<scenario>/tournament.yaml` — pass `--config`.
- **An ABSOLUTE `--result-dir` splits the run**: per-match paths prefix `${REPO_ROOT}/` unconditionally, giving `<repo>//tmp/x`, whose parent never exists, and the watchdog reports "Game exited without writing verdict". Use a repo-relative dir.
- **`-v/--visible` does nothing** and the script exits 0 even when no match produced a verdict — filed in `WORKSPACE/bugs/discovered.md`. Read the per-match JSON, not the exit code.

A `[composition] census` line prints `truk=0+0` one census before the first truck exists, so "the term is printed" is not "a truck exists" — parse and sum the `inWorld+inCargo` pair. Bracketed log tags need `grep -F` (`grep` on the macOS host is ugrep).

### An empty `lua.log` means nothing on its own

`lua.log` collects Lua `print` output and nothing else. Three states produce a 0-byte file:

| `lua.log` | `debug.log` | reading |
|---|---|---|
| 0 bytes | silent | **inert** — the script was never wired in. `make lua-gate` settles this statically, before any launch. |
| 0 bytes | scripted activity (`Taking screenshot …` in scripted order, a trait's own traces) | the script ran and simply **does not print**. Not a finding. |
| 0 bytes | the run stopped near tick 0 | cut off by the watchdog before the first `print` flushed. |

Check the last tick reached first (`debug.log`, or the scenario's periodic trace). The tell is only diagnostic for scenarios that print, so any scenario worth diagnosing later should emit a periodic trace.

---

## The loop

1. **Frame the assertion**: "X must happen within N seconds when Y is set up". Confirm with the user if ambiguous. **If the change is visual** (UI, palette, animation, sprite, formation, lobby/menu), also plan a `TestHarness.Screenshot(label, "expects: ...")` at the critical beat — see [`SCREENSHOT.md`](SCREENSHOT.md).
2. **Write a failing test**: copy a `test-*` folder, set up the actors and a Lua `TestHarness.AssertWithin(...)` predicate. Use `description.txt` to surface intent in the panel. Run the static checks in §"Verify before you ask for a slot".
3. **Verify RED** *(launch — see "Who launches")*: run the new test pre-fix. It must fail with the expected reason. If it passes, the test is not measuring the right thing. Sabotage the mechanism and confirm the SPECIFIC failure text, not just "it went red".
4. **Investigate + fix**: read code, apply changes. Temporary traces go behind `TestMode.IsActive`.
5. **Verify GREEN** *(launch)*: re-run the new test.
6. **Regression check** *(launch)*: run the closest existing scenarios by name. A full `run-batch.sh --all` is a batch — it needs the go-ahead CLAUDE.md §"Who runs what" requires, or is the manager's call.
7. **Strip diagnostics**: remove any temporary trace lines.
8. **PITFALL check**: was the root cause a non-obvious trap a future reader would also fall into? If yes, drop a one-line `// PITFALL:` (or `# PITFALL:` in YAML) at the *temptation site* — the line a careless reader is looking at when at risk. Spec: [`conventions.md` §"PITFALL comments"](../reference/conventions.md). Same commit as the fix. Skip for one-shot bugs.
9. **Commit**: scenario + fix + tracker update + any PITFALL anchor together. The test stays committed so the bug cannot silently regress.

If the bug has several layers, fix what you can and leave the test RED for the rest — **with an `expected-status` file declaring `fail` and naming the open layer**, or it reds every batch. Record the open layer where the work is tracked (`WORKSPACE/PIPELINE.md` item, `WORKSPACE/bugs/discovered.md`).

## Writing a test scenario

```
tools/autotest/scenarios/test-<name>/
├── description.txt        # one-line panel description (recommended)
├── map.yaml               # actor placement + player slots (rules below)
├── rules.yaml             # LuaScript: test-helpers.lua, test-<name>.lua
├── test-<name>.lua        # staging + (for auto) AssertWithin
├── map.bin                # copy from a sibling test
└── map.png                # copy from a sibling test
```

### `map.yaml` rules

1. `Visibility: MissionSelector` and `Categories: Test`.
2. Actor **types** in `map.yaml` lowercase: `e1.russia`, `t90`, `m109`. (This is the map's actor list, which is resolved after the rules are lowercased. It is NOT a rule for `rules.yaml` override keys, which merge case-sensitively against the defining key — see [`conventions.md` §"The override isn't taking effect"](../reference/conventions.md).)
3. **Only ONE `Playable: True`** — the human slot. Every other faction is `Playable: False`. `Launch.Map` creates Player objects only for slots with a connected client; an unclaimed `Playable: True` slot drops its actors to Neutral, which silently breaks targeting.
4. `LockColor: True` and `LockFaction: True` on every PlayerReference, so colours and factions do not depend on the dev's `settings.yaml`.
5. **A top-level `Rules: rules.yaml` line, preceded by a blank line, or the sibling `rules.yaml` is never read** (adjacent MiniYaml top-level entries merge). Without it the `LuaScript` trait is never attached, the match runs on stock rules, and the run ends as an ordinary `TIMEOUT-FAIL` with a 0-byte `lua.log`. `make lua-gate` catches it statically.
6. **One `supplyroute` per active faction**, even if the test never touches it: the game model has one per player, and faction elimination ends the match (Mission Accomplished) before the Lua poller writes a verdict. `supplyroute` carries `Targetable: TargetTypes: NoAutoTarget`, so it is never auto-targeted and is safe anywhere on the map.
7. **No compass names in `map.yaml` field values.** `Facing: East` dies at map load (`FieldLoader: Cannot parse 'East' into WAngle`, exit 3). The eight compass names (`North`, `NorthWest`, … `NorthEast`) exist only in the Lua `Angle` binding (`AngleGlobal`), so `Facing = Angle.East` in `.lua` is right and `East` in `map.yaml` is not. Corpus check: `grep -rnE '^\s+(Turret)?Facing: ' tools/autotest/scenarios mods/ww3mod/maps | grep -vE ': -?[0-9]+$'`. Angles: [`conventions.md` §WAngle](../reference/conventions.md).

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

---

## Verify before you ask for a slot

**These checks need no game and no build beyond what you already have, and they catch the failures that most often burn a granted run.** Run them on every new or edited scenario.

```bash
make lua-gate                                              # every scenario  (.\make.ps1 lua-gate on Windows)
./tools/lua-gate/lua_gate.py check --scenario test-<name>  # just yours
```

**lua-gate proves** every `Trigger.*` / `Actor.*` / `Test.*` member you name is a real binding, every bare actor name resolves against your `map.yaml`, and each `.lua` is reached by a `Scripts:` line (the static form of map.yaml rule 5). Exit 2 is a hard fail, exit 1 a warning; `make lua-gate` fails only on 2. **It does not prove** argument types, arity or order (`Trigger.AfterDelay("soon", 5)` passes and throws at runtime), and most actor properties are trait-gated — `tank.Produce` passes and throws in game because the tank has no `Production` trait. Limits: [`tools/lua-gate/README.md`](../../tools/lua-gate/README.md) §"What this does NOT check".

**Geometry: use nav-guard's decoder directly.** `make nav-guard` does **not** cover `tools/autotest/scenarios/` — its baseline is `mods/ww3mod/maps` only, so its green says nothing about your scenario. The decoder underneath models what the pathfinder sees. `nav_guard.py` is tracked mode `100644`, so invoke the interpreter (`./tools/nav-guard/nav_guard.py` is exit 126):

```bash
python3 tools/nav-guard/nav_guard.py report  --scenarios --map test-<name>
python3 tools/nav-guard/nav_guard.py pockets --scenarios --map test-<name> --locomotor wheeled
```

`pockets` prints every region that is **not** the largest, with a bounding box. If the scenario means to seal something off, the pocket must be exactly the shape you sealed; otherwise a pocket is a unit that cannot reach what the test assumes. For "can A reach B?", label the cells and compare — committed worked example with measured numbers in `tools/autotest/scenarios/test-restock-unreachable-centre/map.yaml`:

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

**Map markers occupy nothing, in the game and in the decoder alike.** `mpspawn`, `spawnarea`, `waypoint`, `flare` and the `camera.*` actors carry `Immobile: OccupiesSpace: false`, so `ImmobileInfo.OccupiedCells` is empty, and `modload.actor_shape` honours the same flag (`_immobile_occupies_space`). If a cell holding only a marker reads blocked, something else is on it — investigate rather than dismiss. Model: `tools/nav-guard/README.md` §Zero-footprint actors.

**`ScenarioLuaParsesTest`** parses every `tools/autotest/scenarios/**/*.lua` under the engine's own Lua runtime, so `dotnet test` names a syntax error by file and line.

### A scenario's LOGIC can be run offline — its DIALECT cannot

The macOS dev host has a stand-alone `lua` (5.5). `lua -e 'assert(loadfile("<scenario>.lua"))'` is a free syntax check, and stubbing `Test.*`, `TestHarness.*` and `Player.*` makes a whole scenario runnable without a slot (`tools/nuke-perf/drive-populated.lua` is the worked driver). Two limits:

- **5.5 accepts what the engine's Eluant rejects** — floor division `//` above all — so a clean local parse is evidence about logic, never about dialect.
- **An offline driver must take the quantity under test as an INPUT, not inherit it from the code under test**, or both share the error and the arm passes.

**A detector calibrated on an inert rig does not transfer to a populated one.** A window anchored on the first rise of `Test.GetImpactEffectCount()` is sound on a map of statues; in a two-bot match ordinary tank fire moves that counter every few ticks. Any predicate on a mod-wide running counter is in this class the moment a second combatant appears. Anchor on an attributable observation (`Test.GetLastBallisticMissileImpactTick(actorType)`), and stamp a census at both ends of each window: a census that changes across a window it should not change across means the window is in the wrong place.

### What each gate reads of a scenario

A scenario that passes every gate a launch-barred worker may run is not known to load — the gates have a hole exactly the shape of a field value.

| gate | what it reads of a scenario |
|---|---|
| `lua-gate` | `map.yaml` STRUCTURE only — declared files, `Scripts:` placement, mis-cased top-level keys. It never loads an actor, so it cannot type a field value. |
| `make check` / `dotnet test` | the `.lua` syntax (`ScenarioLuaParsesTest`); no scenario YAML. |
| `make nav-guard` | nothing — its baseline is `mods/ww3mod/maps`. |
| `./utility.sh --check-yaml ../tools/autotest/scenarios/<name>` (from the repo root) | **the only one that types a field.** The `../` is required because `utility.sh` changes into `engine/` first. Do not `cd engine` and run `engine/utility.sh`: it is tracked `100644` and exits 126 with a zero-byte log. Require `Testing map:` in the output to confirm it ran. |
| `make test` / `.\make.ps1 test` | the same validator over the whole tree; on Windows it lints every scenario by name. Whether the Linux/macOS `make test` does is unverified — a captured log, counting `Testing map:` lines against `ls -d tools/autotest/scenarios/*/ \| wc -l`, would settle it. |

The YAML lint is not a worker's to run (CLAUDE.md §"Who runs what"). **A worker handing up a scenario lists the YAML files it touched and what lint would say if one were wrong**, so the manager lints before the first launch.

### Geometry the gates do not check: arms that are supposed to be independent

**A radius is a circle, so arms separated by ROWS are not separated.** Four arms on rows 6/12/18/26 interacted at `sqrt(5² + 6²)` = 7.81 cells, inside an 8-cell scan radius, and the run failed blaming shipped code that was fine.

- **Measure centre to centre.** A 2x2 building's `CenterPosition` is `Location + (1.0, 1.0)` cells against a 1x1 unit's `Location + (0.5, 0.5)`, so `Location`-space distances understate unit-to-building pairs by ~0.5 cells. Mechanism: [`conventions.md` §"`Dimensions` is a BOUNDING BOX, not the shape"](../reference/conventions.md).
- **A walk budget needs the speed.** `^Infantry` `Mobile: Speed: 25` against 1024 units/cell is 40.96 ticks per cell, so a 20-cell approach is ~819 ticks. `CaptureClearDurationTest.InfantryCoverACellInAboutFortyOneTicks` pins that speed.

**So enumerate every cross-pair mechanically before the run** — a five-line script. The same applies to a guard radius derived from a mechanic: a correct-in-meaning 28-cell guard circle around a cell 30 cells from the acting unit leaves a two-cell shell of safe ground, and every unit spawning near it trips the guard. Check the guard against the map.

**Adding scenery to satisfy a gate is not inert.** `PoiMap` admits an income structure only if it carries `CaptureManagerInfo`, so POI-eligibility and capture-eligibility are the same predicate: a neutral `oilb` added to make a disc POI-eligible handed `CaptureCoordinatorBotModule` a target, and its technician walked through the region under measurement. Zeroing the economy does not contain it. Place such scenery where the capture traffic cannot cross what you measure. Mechanism: [`influence-stack.md` §"Stage F"](../reference/influence-stack.md).

**The setup can be right and still open the wrong code path.** `AutoSeekSupplies` runs two dispatchers queuing different activities: the idle seek (`INotifyIdle.TickIdle`) queues `SeekSuppliesAndReturn` (reach `SupplyHuntLeashCells: 20`); the break-off arm queues `SeekSupplyProvider` (reach `ReturnWhenEmptyLeashCells: 30`), and `FindBest` lives only in the second. A scenario about that leash must put its host in the **20 < d ≤ 30 band** or it measures an activity with nothing under test in it. **Name which dispatcher you intend to open, and check the geometry can only open that one.** [`economy.md` §"Two host-discovery paths disagree about the same actor"](../reference/economy.md).

**None of this substitutes for a run.** These checks establish that the scenario is well-formed; whether the predicate measures what you care about is §"A green run is not evidence".

## Test types

- **Manual** — Lua only stages (camera, selection); the user watches and gives the verdict in chat. Example: `test-artillery-turret`. *If there is no verdict question at all, that's a **DEMO** — see [`DEMO.md`](DEMO.md).*
- **Auto-asserting** — Lua uses `TestHarness.AssertWithin(...)` to verdict itself. Example: `test-paladin-fires`. These are what `run-batch.sh --all` grades.

---

## Lua API

### `TestHarness.*` (in `mods/ww3mod/scripts/test-helpers.lua`)

| Function | Purpose |
|---|---|
| `FocusBetween(a, b, ...)` | Center camera on the midpoint of N actors |
| `Select(actor)` | Pre-select unit-under-test |
| `AssertWithin(seconds, predicate, failReason)` | Poll predicate every tick. `true`→Pass, `"fail: <reason>"`→Fail immediately, timeout→Fail with reason. `failReason` may be a function, evaluated once at timeout. **The Pass is TERMINAL — see below.** |
| `AssertAfter(seconds, predicate, failReason)` | Wait `seconds`, then assert once |
| `Screenshot(label, note?)` | Capture a PNG now (wraps `Test.Screenshot`). See [`SCREENSHOT.md`](SCREENSHOT.md). |
| `ScreenshotAfter(seconds, label, note?)` | Schedule a screenshot N seconds from now |
| `TicksForSeconds(seconds)` | Seconds → ticks, the engine's way |

**ONE VERDICT AUTHORITY PER SCENARIO.** `Test.Pass` writes `result.json` and exits, so the instant an `AssertWithin` predicate returns `true`, everything the scenario scheduled for later is dead — assertions never execute, screenshots never flush, and the run reports green having measured nothing. `AssertWithin` is safe only as a **pure watchdog** (predicate always false) or as the **sole verdict authority** (nothing scheduled after it). To judge after a latch, open-code the deadline in a `Trigger.OnTick` poller, or call `Test.Pass(note)` from inside the predicate and `return false`. Same for `AssertAfter`. Audit of the existing callers: `WORKSPACE/audit/260921-assertwithin-false-green.md`.

**A green with no verdict text is a failed test.** Both helpers pass a note, so `"notes":""` is an anomaly: `run-test.sh` reports it as `PASS-EMPTY` (exit still 0), `expected-status.sh` grades it `EMPTY`, and `run-batch.sh` lists it separately. A scenario that legitimately passes empty declares `pass-empty` with a reason.

**`seconds` means real seconds.** `TestHarness.TimestepMs = 60` mirrors the mod's default `Timestep`, `TestHarness.TicksPerSecond` is derived from it, and `TestHarness.TicksForSeconds` converts the way the engine's `DateTime.Seconds` does (`TickTime.TicksForSeconds`: multiply before divide). So `AssertWithin(n)` and `DateTime.Seconds(n)` are the same tick count for every integer n, and a "within Ns" failure string is the truth. `run-test.sh` sets no `Test.GameSpeed`, and `Game.LoadMap` uses `"default"` unless `Test.GameSpeed` overrides it, so this holds for every scenario. `AutotestTickRateTest` pins the rate, the mod default, the harness/engine agreement over 0..600 s, and the arithmetic of `test-autotarget-preempt-air` and `test-critical-no-panic` — putting back `25` or a truncated `16` fails `dotnet test` naming the casualty.

- **Size deadlines in ticks** and convert with `TicksForSeconds` (or hand `ticks / TestHarness.TicksPerSecond` to `AssertWithin`, which round-trips exactly), not `math.floor(s * TicksPerSecond)`, which can lose a tick. For tick-domain quantities (burst delays, reloads, flight), poll with `Trigger.AfterDelay(1, …)`, which is immune to any rate or game speed.
- **Scenarios whose budget was sized by measurement before the harness moved to the real rate (2026-09-21) have a third less time than their author measured.** Several banked the old slack on purpose and say so in comments (`test-tunguska-missile-standoff`, `test-depot-vacate-phantom`); their numbers were not re-derived. A scenario can also keep passing while an inner budget becomes unreachable and stops being enforced — `test-autotarget-preempt-air`'s comment documents the instance.

**When a rate or a converter moves, triage reds with discriminators that need no reasoning about behaviour** (per-row evidence for the last such move: `WORKSPACE/audit/260922-tick16-suite-triage.md`), cheapest first: **(1)** which converter does the scenario use — one timing only in raw ticks, or through a converter that did not move, cannot be a casualty; **(2)** which string wrote the verdict — a predicate's own `fail: …` was not caused by a shorter window, a `timeoutReason` can be; **(3)** on a green claimed for a fix, compare the recorded tick ("predicate true at tick N of M") to the old budget; **(4)** audit the SKIPs — a shrunken budget gating a precondition reports `skip`; **(5)** a scaled PHASE BOUNDARY fails differently from a scaled deadline — a sample window that starts early measures the wrong stretch and never times out. **Restoring an authored budget is a diagnostic, not a fix.** Wall-clock `TIMEOUT-FAIL`s in a back-to-back batch are throughput, not rate. The general pattern — durations asserted by comments and constants that the code does not produce — is [`conventions.md` §A change believed made, documented as made, and inert](../reference/conventions.md#a-change-believed-made-documented-as-made-and-inert).

### `Test.*` (engine global, gated on TestMode.IsActive)

| Function | Effect |
|---|---|
| `Test.Pass(note?)` | Write `pass` verdict, `Game.Exit()` (deferred until pending screenshots are flushed) |
| `Test.Fail(reason)` | Write `fail` verdict + reason, exit |
| `Test.Skip(reason)` | Write `skip` verdict + reason, exit |
| `Test.Screenshot(label, note?)` | Arm a PNG capture tagged `label`; its path goes into the verdict's `screenshots[]`. See [`SCREENSHOT.md`](SCREENSHOT.md). |
| `Test.IssueEnterTransport(passenger, transport, queued?)` | A real EnterTransport order through `Passenger.ResolveOrder`, so the resulting `RideTransport` is visible to target-line scans |
| `Test.GroupScatter({actors})` | The Group Scatter (Shift-G) spread on the given actors |
| `Test.SetZoom(scale)` | Zoom as a multiple of the default; same as the ungated `Camera.Zoom` |

**Read `TestGlobal.cs` before reaching for a stock OpenRA binding.** Stock scripting APIs are a bad prior here: `actor.Build` exists only on an actor holding queues and WW3MOD has none (the queues live on the player); `actor.Sell` needs `SellableInfo`, which only structures carry; `Evacuate` is not a Lua property at all — reach it with `TestHarness.Select` then `Test.PressHotkey("Evacuate")`. The failure is a runtime *"does not define a property"* or a silent no-op. `Test.*` carries the mod's seams — `QueueProduction`, `PressHotkey`, `SelectActors`, `ClickOrder`, `IssueMoveOrder`, `IssueResupply`, `ClickProductionIcon` and about a hundred more. Which stock bindings are absent and why: [`architecture.md` §"Key Lua APIs used"](../reference/architecture.md).

**Grepping the scenario corpus for prior art misleads.** `grep -rn "\.Build(" tools/autotest/scenarios/` mostly returns a scenario-local helper of the same name (`JavelinProbe.Build`); the one real binding call is the player-level `Russia.Build({ … })` in `test-power-buy-loop`. **Confirm a scripting API in `engine/OpenRA.Mods.Common/Scripting/`, never by counting scenario hits.**

**Fog IS testable.** `Test.KeepRenderPlayer=true` (parsed in `TestMode`; matched case-insensitively against `"true"`, so `1` does not work) stops `TestModeLogic` nulling the render player. And the frozen STATE never depended on `RenderPlayer`: `FrozenActorLayer` is per-player and reads the viewer's own `MapLayers`, so frozen actors exist in every fogged autotest; `RenderPlayer` decides only whether `World.FogObscures` answers honestly and whose ghosts the mouse paths consult. Read a ghost with `Test.FrozenActorState`, `Test.FrozenActorOwner`, `Test.FrozenActorTooltipOwner`, and `Test.FrozenClickCursor` — the last because `Test.ClickCursor` builds `Target.FromActor` and can never reach the `CanTargetFrozenActor` arm.

### Presentation: `Camera.*`, `Trigger.OnTick` (engine globals, NOT test-mode gated)

These work in demos and missions too. The demo-facing version, with framing guidance, is in [`DEMO.md`](DEMO.md#frame-the-shot-yourself--the-viewer-should-not-have-to).

| Function | Effect |
|---|---|
| `Camera.Position` | Read/write the view centre as a `WPos`. |
| `Camera.Zoom` | Read/write zoom as a **multiple of the default level** (`1` default, `<1` further out). Clamped to `Camera.MinZoom`..`Camera.MaxZoom`. Not the raw `Viewport.Zoom`, which depends on resolution. |
| `Camera.MinZoom` / `Camera.MaxZoom` | The achievable range, same units; depends on display and viewport-distance setting. |
| `Trigger.OnTick(func)` | Call `func()` once per world tick, after the global `Tick`. **Prefer this to a self-rescheduling `Trigger.AfterDelay(1, ...)` loop.** |
| `Trigger.ClearTickCallbacks()` | Drop every `OnTick` callback. |

**Camera state is client-local.** Writing it cannot affect the simulation (`ViewportIsNotSimulationStateTest` asserts by IL scan); *branching* simulation behaviour on a read would desync a multiplayer match. `Trigger.OnTick` runs inside the simulation, so simulation work there is fine.

### Useful actor methods

| Method | What it does |
|---|---|
| `Paladin.Attack(target, allowMove?, forceAttack?)` | Attack an actor (stock API; hard-codes `queued: true`). |
| `Paladin.AttackGround(cell, allowMove?, queued?)` | Ctrl+click on terrain. WW3MOD addition; defaults `queued: false`. |
| `Paladin.AmmoCount("primary-ammo")` | Returns int. The pool name is `primary-ammo`; the parameter defaults to `"primary"`. |
| `Paladin.Stance = "HoldFire"` | Force a fire stance (`"Ambush"`, `"FireAtWill"`) — but see Gotcha 9. |
| `UserInterface.Select(actor)` | Replace the local player's selection. WW3MOD addition. |

---

## A behaviour selected by a condition needs a test on EACH SIDE of it

If the thing you are fixing has two modes — danger vs quiet, empty vs full, first-run vs repeat — **one scenario cannot pin it.** Whichever branch you were thinking about will pass, and the other mode's mechanism can quietly satisfy your assertion. Supply trucks dump and leave under fire and serve in place on a quiet front: every single-scenario green on that change was reachable by a change that broke the other scenario. The pair goes green **together** or neither result means anything.

- **A fix correct in isolation, wrong in combination.** Guard A was harmless only because bug B stopped it firing; fix B and A is live. After any fix, re-run the scenario you were *not* working on.
- **A bug that cannot fire is indistinguishable from a bug that does not exist.** When a gate upstream is known to fail closed, record the hypothesis as UNTESTED, never as refuted.

## A green run is not evidence unless something could have made it RED

**Prove your setup took effect by measuring a control — never by asserting the flag you set.** A scenario that never built the world it describes still runs to completion and writes `pass`. Ask of every green: **"what would have made this fail?"** If you cannot name it, you measured nothing. The shapes this has taken, each a real lost result:

- **The control that refused to go red.** A RED arm pinning the fix off (`PreemptScanInterval: 0`) also passed — the unaided behaviour beat the deadline, so the budget never isolated the mechanism.
- **The override that reverted to engine defaults.** A warhead override restated `Damage` and omitted `Penetration`; warhead overrides are constructed fresh rather than merged per-field, so `Penetration` fell to the engine default and the effect dropped an order of magnitude — with a plausible number and a `pass`. ([`conventions.md`](../reference/conventions.md) §Weapons live under `Weapons:`.)
- **A second mechanism satisfied the predicate.** "Are the riflemen near the tank?" passed with the transport fix disabled: `PoiOffensiveBotModule.StageFreePool` recruits armed infantry and `AttackMove`s them to the armour's staging anchor on foot.
- **The observable was attributable only for part of the run.** Peak passengers on a named carrier was exclusive to the ferry only while the ferry owned the carrier; once released, the frontline delivery path loaded riflemen into it. Caught only by reading `debug.log`.
- **The named actors were not the ones the system used.** At `DefaultCash: 7500` the bot bought and used its own carriers and infantry while the placed ones idled. `DefaultCash: 0` makes the placed force the whole force; generally, assert the named actor did the work.
- **The natural assertion sat upstream of the defect.** The unload menu adds every row and only then clips, so the row count is identical on broken and fixed builds; only the clip height separates them. **When a fix changes how much of a collection is DRAWN, every count in the widget tree is a false control** — assert the geometry (`Test.GetUnloadMenuGeometry()`, backed by `UnloadMenuGeometry.Measure`).
- **The setup was never in the state under test.** Two queued `Move`s never produce a corner arc — the first settles on the cell centre and the second turns in place — so a "dies mid-corner" scenario staged that way never cornered. **A queued order boundary is a state boundary.** ([`conventions.md` §"Engine behaviors that surprise"](../reference/conventions.md).)
- **The harness measured a shorter pipeline than the player uses.** A private copy of the order-resolution chain lacked the second, terrain-cell pass where the bug lived. `Test.ClickOrder` now delegates to the same `UnitOrderGenerator.OrderForUnit` the mouse uses. **A docstring saying "exactly as X does" is a claim about a copy** — delegate to X, or name what the copy omits. `ClickOrder` is the per-unit layer; a real click resolves for the whole selection (`Test.ClickOrderGroup`).
- **The log field was named after the thing you wanted.** `[exp-transport] delivered … pax=N` printed `task.SeatTarget`, a target, so raising the target read as a fivefold improvement. **`git grep` the format string and read the emit site.**

What to do about it, cheapest first:

1. **Run the control arm and require it to FAIL.** A passing control falsifies the test, not the hypothesis.
2. **Verify the pin was applied** — a one-line trace printing the effective value per arm is cheaper than a wasted run.
3. **Give the assertion a second, independent observable** — numbers that do not fit the setup's story expose a setup that did not happen.
4. **When you override anything, restate every field the consumer reads.**
5. **Ask who ELSE could satisfy the predicate.** Name every path in the sim that could make it true. On the bot, position is almost never attributable — many modules move units toward the same front. Prefer an observable only the mechanism can produce (a unit was **carried**: latch that it left the world into a `Cargo`, then measure where it reappears; or the timing gap between arrivals).
6. **Then ask WHEN it could satisfy it.** Carriers, squads and the free pool are claimed and reclaimed across a match; latch the measurement inside the ownership interval and stop it at release. Emit the asserted quantity from the code under test and read it back from `debug.log`, so verdict and mechanism are two observations.
7. **Ask what observable proves the run ENTERED the state.** Assert or log it — the corner scenario now detects its turn at runtime and prints the advance it fired on.
8. **Make the verdict self-diagnosing: print the whole board, not the first bad check.** Every failure string carries the state of every actor under test and any purse being asserted on; trace to `lua.log` on an interval. A tolerance band hides exactly this class; with the confound off, assert exactly. **Write attribution INTO the assertion**: a capture consumes the captor in this mod (`ConsumedByCapture`, `EnterBehaviour: Dispose`), so `actor.IsDead` says which unit did the work — assert it beside ownership.
9. **If the opening cannot be relied on to contain the state, CONSTRUCT it, and make the scaffold check itself.** A rules override that stops merging is silent, so read the overridden value back at runtime and **SKIP, not PASS,** unless it is what you asked for (e.g. `Test.GetBotOffenseAdvanceFloor`), and require evidence the feature was ASKED before "nothing happened" can pass. A binding returning `-1` ("not evaluated yet") is information, not zero. For "a module did NOT act", read the ledger (`Test.GetBotLedgerHeld`, `Test.GetBotOffenseFreePool`), never positions — no bot module logs which actor it sent where. **`DefaultCash: 0` on a bot map with a transport subtracts more than the purchased army**: `PassengerTypes` infantry are withheld for an empty carrier and excluded types never enter the pool, so the free pool is often 0 or 1, below the advance floors, and an "advanced N cells" clause times out having measured nothing. SKIP with `free=`/`floor=` when the pool never reaches the floor. `make lua-gate` is a RED you can run without a launch: sabotage one `Test.*` call name and it reports file and line, exit 2.

**A corpus-scanning guard must assert it measured something** before asserting it found no violations, or a rename turns it into a test that scans nothing (`StancePositioningFireStanceTest` asserts a non-zero count first). **And a guard over source must pin the CALL SITES, not only the helper**: a pure-math test can be green and uninformative when the defect is that a call site does not use the seam the test covers. `GridDescentGuardTest` walks every grid-descent call site under `Traits/BotModules/**`; carry it with an exact-site-count assertion so the scan's scope cannot silently shrink (precedent: `BotOrderGateCallerTest`). Explanatory comments did not stop three resolvers repeating the same mistake; the guard did.

### Two Lua traps that make a scenario lie about its own numbers

**The failure message is evaluated EAGERLY, at registration.** `AssertWithin(deadline, fn, msg)` takes `msg` as an ordinary argument, so a counter interpolated into it reports its initial value forever. Keep the string static and put live counters in a periodic `print` — or pass a **function** as `timeoutReason`, which `AssertWithin` evaluates once at timeout, so the note can carry end-of-run state. A string returned *from the predicate* is built when returned and is unaffected.

**`IsDead` is false for a passenger inside a `Cargo`.** `Actor.IsDead` is `Disposed || health.IsDead`; boarding calls `World.Remove(self)` from `RideTransport`, which clears `IsInWorld` without disposing the actor or touching its health. To latch "was carried", use `not r.IsInWorld` paired with a separate clause requiring the unit to return to the world. That form is permissive (a corpse also latches); add `and not r.IsDead` for an exact count.

### The converse: an UNCHANGED verdict is not evidence of safety unless the change was live in that run

**A test that fails to move is indistinguishable from a test the change never reached.** "Thirteen scenarios before and after, zero flips" is compatible with the change being inert in all thirteen. Name an observable that proves the change was **active** in at least one run — it need not be the thing under test. In one such sweep, a single unit's ammo reading `71/100/100/71/70` vs `71/100/100/70/70` at the same seed proved the simulation diverged, which is what made the unchanged verdicts a statement about the assertions. Cheapest sources: a telemetry line already logging a touched quantity (`[composition] census` logs `earned`/`spent`), an incidental number in a failure note, a one-line trace. **If every observable is byte-identical across the arms, the change did not run.**

### A before/after pair is not an experiment unless both arms carry the same explicit `--seed`

`run-test.sh` seeds from the clock unless `--seed N` is given (the seed is recorded in `result.json`), so two unseeded runs are **two different matches**, and in a long bot game the between-match variance easily exceeds a one-field rule change. On `test-escalation-full-match` an unseeded pair read a 73-tick delay and a doubled phase; the same seed showed zero delay and a *shorter* phase — wrong in magnitude and sign, and nothing in the output distinguishes the two. Before bisecting a flip, check whether the two runs shared a seed.

- **Take the seed from the FIRST run** (`result.json`) rather than inventing one — the finished baseline becomes the control for free.
- **`--seed 0` is rejected**: the engine treats `RandomSeed == 0` as unset and would fall back to the clock while the verdict stamped `"seed":0`.
- **A controlled pair buys the SIGN and SIZE of an effect, not its CAUSE.** Label any causal story off one run as an inference.
- **An end-of-run distance cannot select a failure message** — it cannot tell "never arrived" from "arrived and was left behind"; track the closest approach and the drift from the staged cell.

### A NUnit suite over the math does not cover the wiring that FEEDS it

`MissileStrikeApproach.For` is deliberately World-free, so its tests assume the caller passes the right home position, map size and aim points; the code that chooses them is `MissileStrikePower.ApproachFor`, which nothing in `OpenRA.Test` sees. **When a helper is pure by design, ask what supplies its arguments, and put *that* in a scenario.**

- **A degenerate layout makes a directional assertion unfalsifiable.** With home, aim point and entry on one row, every candidate bearing rule produces the same entry cell; only moving the aim point off the launching player's row makes the bearing readable.
- **When sweeping for affected scenarios, discriminate on the right property** — there, the power TYPE, not the scenario name or missile actor. Live power types: [`architecture.md`](../reference/architecture.md).

## The mirror: a RED is not evidence either, unless the branch under test is REACHABLE at shipped config

`test-supply-safe-front-keeps-cargo` asserts the supply doctrine's quiet branch, selected by `if (drop && Info.DropRequiresDanger && !Info.IgnoreDangerForDelivery && …)` in `SupplyFollowerBotModule`. Shipped `mods/ww3mod/rules/ai/ai.yaml` sets both `DropRequiresDanger: true` and `IgnoreDangerForDelivery: true`, which short-circuits it — so the mode was unreachable and the scenario could only go green by the truck failing entirely. **A test asserting a mode that configuration has switched off is unwired, and no amount of re-running distinguishes that from a real defect.** Grep the assertion's own gate for a **bypass flag**, not just the mechanism.

**Making an unreachable branch reachable inverts the false-PASS shape.** With the gate live, any drop decline (`NoDemand`, `Covered`, `LowLoad`, `NoAnchor`) also leaves `drop = false` and the truck serves from its aura — green and evidence of nothing. Lua cannot see the module's `reason`; the acceptance criterion becomes a log line (`[supply] drop-declined … reason=SafeFront`) plus proof the override merged (`[supply] init … ignore-danger=False`).

**A bypass flag over N sites cannot be cleared "just for the one you want", and the per-site arguments differ in strength.** `IgnoreDangerForDelivery` gates seven sites: some structurally equivalent to the branch they replace, one provably inert, one off-path, and the evac site inert only **by threshold** (`EvacDangerUnits: 50` against a field of 0) — the weakest, and the first to re-check. Record which kind each site rests on.

**Before deleting or retiring any scenario, grep `engine/OpenRA.Test/` for its name.** `SupplyDriftClauseTest` reads a constant out of this scenario's `.lua` (`ReadScenarioConstant`) and calls **`Assert.Ignore`, not `Assert.Fail`**, when the file is missing — retiring the directory would turn a passing test into a skipped one with the suite still green.

### A RED that PASSES certifies the fix — ask which line ARMS the defect

`test-garrison-unload-keeps-manned-owner` passed with the veto under test forced off, notes identical byte for byte: the defect lived in a frame-end task only `UnloadCargo` arms, and the scenario drained the shelter through `DeployToPort`, which arms nothing ([`architecture.md` §"The hold is not the building"](../reference/architecture.md)). Its PASS clause also named the same end state the regression produces. **When PASS names the state the regression would also produce, catch the TRANSITION, not the end state.** A setup gate must require the population to be SETTLED (`shelter + ports >= #men`), not merely non-empty.

**For a scenario asserting something FIRED, put the ammo count and `Test.ActivityChain` in the failure message from the start.** "Targets at full HP" is shared by at least four causes (out of `MinRange` under `allowMove=false`, activity dying on tick one, fired-but-no-missile, inert warheads). Gate list: [`architecture.md` §"An attack order that produces NOTHING"](../reference/architecture.md).

### A NEGATIVE limb ("no damage arrived") is not a test of a gate

"No damage" is satisfied by every reason a shot can fail to connect; an arc-gate scenario asserting it passed with the gate deleted. **`Actor.CanTarget` is `Target.FromActor(t).IsValidFor(Self)`** (`CombatProperties`), the same check the attack activity re-runs every tick, so it IS the instrument below the order layer — assert the predicate and keep the downstream effect as a second limb. The order layer (`Test.GetTargetOrder`, `Test.ClickOrder`) ends at `WeaponInfo.IsValidAgainst` over the victim's target-type union and cannot see a per-attacker gate. To tell "never fired" from "fired and discarded", `WW3_GUNTRACE=1` makes `TargetDamageWarhead.DoImpact` log `TargetDamage HIT` / `TargetDamage SKIP outsideSpread`.

### Reverting a PROBABILISTIC fix is not a RED arm — compute the overlap first

In `test-forward-deploy-clears-band` the motorized annulus is 68 cells, exactly one behind the DEFCON border; twenty units drawing from it miss that cell about three runs in four, so a green pre-fix run is the common case and proves nothing. **Assert the SHAPE of the overlap** (68 cells, one forbidden, at a named cell) deterministically — ideally in NUnit — and keep the probabilistic leg as a regression guard. An overlap count is cheap to compute and expensive to guess: a few lines of Python over the engine's bucket rule (`MapGrid.CreateTilesByDistance`, `ceil(sqrt(dx² + dy²))`) settles it.

### A guard is code, and gets the same scrutiny as an assertion

Both obvious spellings of a lobby-option guard were broken in opposite directions: `Map.LobbyOption(id) ~= expected` faults on every run, and `Map.LobbyOptionOrDefault(id, expected) ~= expected` can never fault. `Map.LobbyOption` resolves **`ScriptLobbyDropdown` traits only** (`MapGlobal`); every option this mod ships is an `ILobbyOptions`, invisible to it, and the miss goes to the Lua log while the call returns nil. Use **`Test.LobbyOption(id)`**, which reads `Session.Global.OptionOrDefault` like the consuming trait. **Exercise a new guard against a deliberately broken world once**, the same RED-before-green an assertion gets.

## The setup you wrote is not always the setup that ran — check the subject, not the config

A tournament config's `Matchup:` block is informational only (`TournamentConfig`); the bot that plays is each `PlayerReference`'s `Bot:` in the scenario's **`map.yaml`**, which `--config` cannot reach. To change the matchup, fork the scenario and edit `map.yaml`. The verdict JSON's `bot_type` comes from `player.BotType` at runtime, so the summary CSV's `p1_bot`/`p2_bot` are ground truth — **read them on every tournament run.** General rule: **when a harness lets you declare a subject in one file and select it in another, assume you edited the wrong one until the output proves otherwise.**

**Before concluding "the bot never did X", confirm X was observable.** `AIUtils.BotDebug` is default-off and routes to game chat, never to `debug.log`, so several procurement decisions leave no trace until someone adds an unconditional `Log.Write("debug", …)`.

### A query that drives setup must be asserted non-empty

`Map.ActorsInCircle` / `Map.ActorsInBox` **return nothing when called from `WorldLoaded`**: they read `ActorMap`'s position bins, which map-placed actors enter only via `ActorMap`'s tick function on the first world tick. Cell-keyed `ActorMap.GetActorsAt` is immune. Query from inside the polling predicate or behind a grace window. The silent shape is ordinary setup — "find the units near X and set `HoldFire`" — returning an empty list, setting nothing, and running to a confident verdict. **Assert a setup query RETURNED something before acting on it.**

### Logs: fixed paths, no run identity, and concurrent writers

The engine writes `debug.log` and `lua.log` to **one fixed global path** with no run identity (macOS `~/Library/Application Support/OpenRA/Logs/`, Linux `~/.config/openra/Logs/`, Windows `engine/Support/Logs/` if that override exists, else `%APPDATA%\OpenRA\Logs\` or `Documents\OpenRA\Logs\`), and each launch truncates them (`File.CreateText` in `Log`). `run-test.sh`'s single-instance lock protects the game, not the log. So:

- **An artefact at a fixed path with no run identity is not evidence about a particular run unless you emptied it first.** A stale `debug.log` has a plausible mtime, the right player names and the right format — a pre-fix census read from one sent three rounds into correct code.
- **Under concurrent workers, copy the log out while the run is in flight**: `tools/autotest/poll-copy-logs.sh <dest-dir>` in the background, stopped when the runner returns. Copying once after exit loses the race to the next launch's truncation. **Copy unconditionally** — an "only if the source grew" guard inverts on truncate-at-start and preserves the previous run's file.
- **The `debug.log` in a run directory can be a DIFFERENT GAME'S log.** Check it: max `tick=` against the scenario's end, the players named, actor coordinates against `Bounds`, mentions of the scenario's own actor names. A file ending mid-line on a bare `[` was copied while another process appended to it.
- **An erased log reads as a finding** — a missing `departing aboard=` line looks like "never departed". Before treating an empty grep as evidence, confirm the log's mtime falls inside your run's window, or grep the copy in your run dir. When a live result contradicts a solid offline one, suspect the log before the code.
- **For a `--hidden` run, `result.json`'s verdict is the only trustworthy artefact**: screenshots are listed but never written, and the logs may belong to someone else. Anything you need afterwards must be in the verdict string — which is why "print the whole board" and "write attribution into the assertion" are not stylistic.

**A tournament result dir two runners wrote does not look wrong.** `run-tournament.sh` derives its settings backup path from the result dir alone (`${RESULT_DIR}/.settings.yaml.bak`), so two runners on one `--result-dir` share it, the `[ -f ]`-then-`mv` is a race, and `set -e` kills one batch mid-ladder while the other keeps writing the same match indices. The wreckage passes every completeness check (`verdicts=10`, `git_dirty=false`). **Never point a run at a used result dir** (one containing `match_*.json`); check that the `match_*.json` mtimes form a single series and no `match_*_debug.log` is 0 bytes; and when a batch dies mid-ladder, stop the whole ladder, since the next batch inherits a live competitor for the one game slot.

### The instrument reads a state that LAGS the thing you just asked for

1. **`IsIdle` is TRUE both before an order lands and after it finishes.** `Actor.IsIdle` is `CurrentActivity == null`, and `Test.IssueMoveOrder` goes through `World.IssueOrder`, so the activity starts a tick or more later and a poll written to mean "wait until he has walked there" as `return unit.IsIdle` fires on its first check. **Wait on POSITION**: poll `Location` against the target with a cell of slack, then a settle beat, then act.
2. **A ticked accessor read from `WorldLoaded` returns its uninitialised default — usually a plausible number.** `Detectable.CurrentVisibility` is written only in `Detectable`'s `ITick.Tick`, and `World.LoadComplete` runs every `IWorldLoaded` before the first tick, so `Test.GetVisibilityLevel` read 0 there — a value the clamp under test (`ClampConcealment`, which returns 1 for any input below 1) cannot produce. **Learn each binding's sentinels**: for `GetVisibilityLevel`, **-1** = no `Detectable`, **0** = not ticked yet, **≥ 1** = a real level. Tier reads go behind `Trigger.AfterDelay`; per-cell map queries like `Test.GetDensity` are safe at `WorldLoaded`. Ask whether the code under test can produce the measured value at all before reading the assertion's prose.
3. **Three states, not two — and `Test.ConditionCount` cannot see the interesting one.** It returns 0 for an actor not in the world, and `Cargo.Load` removes the passenger, so a condition on a man inside a building or transport always reads 0 — including `Passenger.CargoCondition`, granted *because* he boarded.

   | state | what to ask | why |
   |---|---|---|
   | **inside** | `Test.IsLoadedInto(passenger, transport)` (reads `Cargo.Passengers`); `Test.IsAtGarrisonPort(soldier, building)` (reads `GarrisonManager.PortStates`) | true regardless of either flag |
   | **outside** | `IsInWorld` | sound in ONE direction: in-world and not loaded really is outside |
   | **gone** | `IsDead` | a passenger is not dead, so dead-and-out-of-world means genuinely killed |

   Ownership flipping (`DynamicOwnership`) is the best *aggregate* proof somebody got in.
4. **Ask the engine for a value it chooses at runtime; do not encode your prediction in the map.** `GarrisonManager` deploys to the first port confirming an in-arc, in-range target, so the winner depends on every enemy, weapon range and port order — and with `Cone: 140` on four diagonal ports the cones union to the full circle, so "behind the building" only means behind *this man's* port. Read the port (`Test.GarrisonPortOf`), derive shooters from its yaw, and re-read before the verdict so a mid-measurement swap SKIPs. `Test.TargetableReport` is the sibling instrument.

**The instrument that catches all four: print the same fact at two moments.** `canTarget=false` at order time against `canTarget=true` in the verdict names the order moment as the fault; neither line alone is a diagnosis.

**Staging: prefer `Test.ClickOrder` over the direct-activity helpers.** `MobileProperties.EnterTransport` queues `RideTransport` directly; `Test.ClickOrder` issues the order a player's click would, so a staging failure is also a finding, and the returned order string gives a cheap setup assertion that turns a silent no-op into a named SKIP.

### Timing an event-driven scenario: anchor on what you OBSERVED, never on a duration you looked up

**Do not encode a flight time.** `PowersLobbyOptionsInfo.SandboxRemovesLaunchDelay` defaults to true, and `MissileStrikePower` then takes `baseMissileDelay = 0`; the powers sandbox supplies the `powers.event` prerequisite no faction provides, so every scenario needing an event-tier power turns it on (`grep -rl "PowersSandboxCheckboxEnabled: true" tools/autotest/scenarios/*/rules.yaml`). On a 66x34 map a B61Low measured 110 ticks with the sandbox on against ~310 off. A phase built on the long figure had the victim still reloading when the escalation landed. Keep an event-driven watch, raise cooldowns so the natural situation holds, and have each phase check its own precondition and report `SCENARIO SETUP: … must be raised` rather than looking like a defect.

**Anchor on the event, and the anchor tightens the bound.** A support power's countdown does not run while the power is disabled: `SupportPowerInstance.Tick` pins `remainingSubTicks` back to the full interval each tick `instancesEnabled` is false and returns early when `!Active`. So a band's interval starts **at the rise**, not at match start; a scenario anchored on the tick the band was *seen* to leave `hidden` needs only one `ServicePendingReady` pass of slack (~12 ticks rather than more than `GrantRetryTicks`), and can assert "the level rose after the explosion and within `EscalationDelayTicks`" without any flight time.

**`Test.GetImpactEffectCount` is GLOBAL — which is why it works and why it expires mid-scenario.** It counts `CreateEffectWarhead` impacts past the validity gates, and every nuclear weapon carries exactly one, so a delta is a detonation you did not have to predict. It cannot say which warhead moved it, so a scenario that fires an unwatched shot must let it land before the next watch baselines. **Hence a per-phase observable SET**: a hold-fire scenario that asserts silence, then orders a shot, then asserts no retaliation cannot carry one set across all three — in phase 3 the counter, ammo and victim health move because phase 2 asked them to. Write each phase as its own function, commenting which quantities are legitimately moving by then.

---

## Gotchas

1. **Build cache lies.** `make` occasionally reports success without picking up an edit. If a trace does not fire, `touch <file>.cs` and rebuild.
2. **`AttackTurreted` overrides `CanAttack`** and short-circuits on `turretReady = FaceTarget()` before calling `base.CanAttack`. If a trace in `AttackBase.CanAttack` shows no fires, the override gated earlier.
3. **`Activity.IsCanceling` is false in `OnLastRun`** — the framework sets `State = Done` first. To detect "ended because something replaced me", check `NextActivity is X`.
4. **Window behaviour flags**: `--hidden` (no window, no PNGs), `--background` (default), `--visible`, `--minimized`, plus the `L`/`R`/`F`/`C` position shorthand.
5. **Lua force-attack vs UI force-attack are not always equivalent paths.** `Attack(…, forceAttack=false)` hard-codes `queued: true`; `AttackGround(...)` defaults `queued: false` to mimic Ctrl+click replace.
6. **Never read or `rm` `~/.ww3mod-tests/result.json`.** It is a `"status":"moved"` stub. Read the per-run `result.json` printed as `Run dir:`.
7. **The harness snapshots and restores the whole `settings.yaml`, so nothing the engine persists during a run survives it.** `run-test.sh` copies the file before launch and its `restore_settings` (called from the EXIT trap, so every exit path) moves it back; `screenshot-lobby.sh` does the same. A settings value therefore cannot carry state between runs, and a "show this once" feature keyed on one cannot be tested through this harness — its flag is written and discarded. Exclude the key from the restore or have `TestMode` suppress the save.
8. **Never put a visibility assertion on a vision-band boundary.** Vision is graded into concentric bands (`^StandardVision` in `mods/ww3mod/rules/defaults.yaml`: strength 10 to 4c0, 9 to 7c0, 8 to 10c0, …), and `Detectable` recomputes the reveal threshold every tick from `IDetectableAddativeModifier`s — `^DetectableInfantryStandard` (`mods/ww3mod/rules/ingame/infantry.yaml`) adds +1 prone, +1 dug in and up to +3 cover. The same rifleman flips on posture alone at a band edge. Pick a distance at least one band clear of the threshold.
9. **PITFALL: do not silence the unit-under-test with `Stance = "HoldFire"`** — silence the ENEMY (enemy on `HoldFire`, plus `Targetable: TargetTypes: NoAutoTarget` on it in the scenario's `rules.yaml`). `StancePositioningExecutor` opts out below `FireAtWill` (`FireStanceAllowsRepositioning`), so the convenience switches off the trait under test. `StancePositioningFireStanceTest` fails the build if a `test-stance-*` scenario does it. **Before using a unit property as setup convenience, check no gate reads it.**

## Engine integration points

| File | Role |
|---|---|
| `engine/OpenRA.Game/TestMode.cs` | Static class — `IsActive`, `Name`, `ResultPath`, the `Test.*` launch args |
| `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/TestModeLogic.cs` | In-game panel (title, description, RESTART button, End hotkey); nulls the render player |
| `engine/OpenRA.Mods.Common/Scripting/Global/TestGlobal.cs` | `Test.*` Lua bindings |
| `engine/OpenRA.Mods.Common/Scripting/Global/UserInterfaceGlobal.cs` | `UserInterface.Select` |
| `engine/OpenRA.Mods.Common/Scripting/Properties/CombatProperties.cs` | `AttackGround`, `CanTarget` |
| `engine/OpenRA.Mods.Common/Traits/World/TestModeSpeedMultiplier.cs` | `--speed` |
| `engine/OpenRA.Mods.Common/Traits/World/UnitLifecycleLogger.cs` | `--lifecycle` JSONL stream |
| `engine/OpenRA.Mods.Common/Widgets/ViewportControllerWidget.cs` | Edge-pan disabled in test mode + windowed |
| `engine/OpenRA.Platforms.Default/Sdl2PlatformWindow.cs` | `OPENRA_WINDOW_X/Y`, `OPENRA_WINDOW_HIDDEN` |
| `mods/ww3mod/chrome/ingame-testmode.yaml` | Panel layout |
| `mods/ww3mod/scripts/test-helpers.lua` | `TestHarness.*` |
| `tools/autotest/` | Runners and tools — see [`tools/autotest/README.md`](../../tools/autotest/README.md) |

## Existing tests

- `test-artillery-turret` — manual: does the Paladin's turret rotate before firing?
- `test-paladin-fires` — auto: the Paladin's primary ammo drops within 12 s of force-engaging a T-90 on HoldFire. The reference green path.
- `test-arty-force-attack-during-setup` — auto: force-attack-ground during setup ticks. Layer 1 was fixed in `51db91f7`; layer 2 (turret stalls mid-rotation) was left open. Its current verdict is unverified — one `run-test.sh --hidden test-arty-force-attack-during-setup` settles it — and it has no `expected-status` file, so if it is still red it reds every batch.
