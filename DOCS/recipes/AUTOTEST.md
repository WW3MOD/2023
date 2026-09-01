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
./tools/autotest/run-test.sh --help                     # flag list
```

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

This is strictly better than an opt-out marker, which is why there isn't one: a scenario excluded
from `--all` stops reporting, so if it later breaks in a new way nobody hears.

**Read the verdict from the banner, not just the exit code.** Every run ends with a line

```
AUTOTEST_VERDICT outcome=<OUTCOME> exit=<n> test=<name> run=<run-id>
```

where OUTCOME is one of `PASS`, `FAIL`, `SKIP`, `TIMEOUT-FAIL`, `CRASH`, `NO-RESULT`, `BAD-VERDICT`, `INTERRUPTED`, `HARNESS-ERROR`. It distinguishes what the exit code collapses: `CRASH` (the game threw — the exception log is named, and a crash is sometimes the *finding*, as when a sync guard fires) vs `NO-RESULT` (hung or closed by hand) vs `HARNESS-ERROR`; and `TIMEOUT-FAIL` (never answered) vs `FAIL` (answered no).

**`NO-RESULT` also covers "the game never launched", and a fresh worktree hits this on its first run.** `launch-game.sh:42` aborts with `Required engine files not found.` when `engine/bin/OpenRA.dll` is missing — and build output is neither shared between worktrees nor tracked in git, so a new `git worktree add` fails this and burns a granted run slot. **Run `make all` in a new worktree before the first `run-test.sh`, even when the diff contains no compiled code** — being built is a property of the worktree, not of the change. Tells: `lua.log` 0 bytes, run dir empty, `test -f engine/bin/OpenRA.dll` fails. (Related, and launch-free: `./utility.sh --check-yaml <MAPDIR>` lints a single map without starting the game, but `utility.sh:61` `cd`s into `engine/` first, so the path you pass is `../tools/autotest/scenarios/<name>`.)

**PITFALL: `run-test.sh <test> | tail` reports `tail`'s exit status, so a FAIL arrives as exit 0.** This has inverted a result twice. The harness defends what it can — the verdict line is last, so a tail-truncating filter still shows it, and non-PASS is also written to stderr whenever stdout is redirected — but the exit code itself is the **caller's** to preserve:

```bash
./tools/autotest/run-test.sh <test>; rc=$?      # capture first, filter after
```

**Results are per-run.** Each invocation writes to `~/.ww3mod-tests/screenshots/<timestamp>_p<pid>_<test>/result.json` — printed as `Run dir:` at the top of the run — alongside that run's screenshots and lifecycle log. Two runners cannot share a destination.

`./tools/autotest/selftest.sh` proves all of the above without launching a game (~1 min). Run it after touching `run-test.sh`.

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

**PITFALL — the decoder over-blocks map markers, so an unreachable start cell is often the tool's
error, not yours.** `mpspawn`, `spawnarea`, `waypoint` and the two `camera.*` actors all carry
`Immobile: OccupiesSpace: false` and occupy **nothing** in game (`ImmobileInfo.OccupiedCells` returns
an empty dictionary, `Immobile.cs:23-27`), but `modload.actor_shape` gives every non-`Building` actor
a 1-cell footprint and never reads that flag — so nav-guard models all five as solid walls. A unit
sharing its cell with an `mpspawn` therefore reads as standing on impassable ground and reaches
nothing. **If a cell reads blocked and the only thing on it is one of those five, that is a nav-guard
artefact — do not move your actor to satisfy it.** The error is conservative (it can only invent a
wall, never delete one), so the `check` gate cannot have passed a real sealing-off because of it; only
manual inspection is affected. Measured 2026-09-01, with the blast radius, in `WORKSPACE/DISCOVERIES.md`.

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
| `AssertWithin(seconds, predicate, failReason)` | Poll predicate every tick. `true`→Pass, `"fail: <reason>"`→Fail immediately, timeout→Fail with reason. |
| `AssertAfter(seconds, predicate, failReason)` | Wait `seconds`, then assert once |
| `Screenshot(label, note?)` | Capture a PNG now. Wrapper around `Test.Screenshot`. See [`SCREENSHOT.md`](SCREENSHOT.md). |
| `ScreenshotAfter(seconds, label, note?)` | Schedule a screenshot N game-seconds from now |

**The `seconds` argument is not seconds. `TestHarness.TicksPerSecond = 25` (`test-helpers.lua:26`, consumed at `:69`/`:99`/`:203`) while the mod runs at `Timestep: 60` (`mod.yaml:382`) = 16.67 ticks/second — so every window is `N × 1.5` real seconds.** `run-test.sh` sets neither `Test.GameSpeed` nor `Test.SpeedMultiplier`, and `Game.LoadMap` hardcodes the `"default"` speed unless `Test.GameSpeed` overrides it (`Game.cs:1184`), so this applies to every scenario in the suite: `AssertWithin(10, …)` waits 15 seconds, and every "within Ns" string in a failure message overstates the time actually allowed by half again.

**There are THREE tick bases in play, not two, and the third is the one that breaks scenarios.** `DateTime.Seconds(n)` — the engine's own Lua converter — computes `1000 / Timestep` in **integer** arithmetic (`DateTimeGlobal.cs:31`), so it yields **16**, not 16.67. Against wall clock the harness is 1.5× lenient; against `DateTime.Seconds` it is 25/16 = **1.5625×**. A scenario that mixes the two — an outer `AssertWithin` timeout sized to cover an inner `DateTime.Seconds` delay — has a margin that exists only because 25 > 16, and correcting the constant *inverts* it. `test-autotarget-preempt-air` is exactly that shape and is provably unpassable at either 16 or 16.67; see its comment at `:70-77`.

The error is in the lenient direction, which is why nothing has broken and nobody noticed — and that is exactly what makes it dangerous now. Two consequences to carry:

- **A scenario tuned to "just barely times out" has 50% more slack than its author believed**, so it is a weaker gate than it looks. Any *duration* a scenario reports in seconds is overstated by the same factor.
- **Anyone TIGHTENING a deadline to a value that looks adequate in seconds is really setting two-thirds of it.** This is where the constant bites: the direction of the error flips from harmless to test-breaking the moment you shrink a window.

**Do not "fix" the constant — and this is now enforced, not merely advised.** Correcting it would tighten every existing deadline in the suite by a third in one step, turning currently-green scenarios red all at once; that is a fleet-wide retune, filed separately. A 2026-08-27 static audit sized the blast radius: **91 deadlines across 137 scenario files scale with this constant**, 8 more round-trip through it and are immune, and at least two provably stop passing the moment it moves (`test-autotarget-preempt-air`, `test-critical-no-panic`). Several scenarios bank the slack on purpose and say so (`test-tunguska-missile-standoff:25` "Left alone deliberately", `test-depot-vacate-phantom:32` "Generous on purpose"). `AutotestTickRateTest.cs` pins the constant, the mod's default `Timestep`, and those two scenarios' arithmetic, so editing any of them fails `dotnet test` with the specific casualty named rather than failing invisibly in a game nobody reran. **Size new deadlines in ticks and divide** — prefer expressing tick-domain quantities (burst delays, reload times, projectile flight) in ticks and polling with `Trigger.AfterDelay(1, …)`, which is immune both to this constant and to whatever game speed a run happens to use. The underlying `world.Timestep`-vs-`GameSpeed.Timestep` mechanism, and a second consequence of the same baseline in `TimeLimitManager`, are in [`conventions.md` §Engine behaviors that surprise](../reference/conventions.md). This is one instance of a mod-wide pattern — comments and constants asserting a duration the code does not produce — collected in [`conventions.md` §A change believed made, documented as made, and inert](../reference/conventions.md#a-change-believed-made-documented-as-made-and-inert).

### `Test.*` (engine global, gated on TestMode.IsActive)

| Function | Effect |
|---|---|
| `Test.Pass()` | Write `pass` verdict, `Game.Exit()` (deferred until pending screenshots are flushed to disk) |
| `Test.Fail(reason)` | Write `fail` verdict + reason, exit |
| `Test.Skip(reason)` | Write `skip` verdict + reason, exit |
| `Test.Screenshot(label, note?)` | Capture a PNG tagged `label`. Path is emitted into the verdict JSON's `screenshots[]` array; agent reads + evaluates. See [`SCREENSHOT.md`](SCREENSHOT.md). |
| `Test.IssueEnterTransport(passenger, transport, queued?)` | Issue a real EnterTransport order through Passenger.ResolveOrder. Use this rather than `unit.EnterTransport(t)` when the test needs the resulting RideTransport activity to be visible to target-line scans (e.g. spread / Shift-G logic). |
| `Test.GroupScatter({actors})` | Run the Group Scatter (Shift-G) spread on the given actors. Mimics the hotkey path without needing a key press / live selection. |

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

What to do about it, in order of cheapness:

1. **Run the control arm, and require it to FAIL.** A control that passes has falsified your test, not your hypothesis. Stop and rebuild the scenario before reading the green arm.
2. **Verify the pin was applied, do not assume it.** The preempt-air run did this correctly: a temporary trace in `AutoTarget.Created` printed the effective `PreemptScanInterval` per arm, so "the control really was switched off" was observed rather than inferred. A one-line trace is cheaper than a wasted run.
3. **Give the assertion a second, independent observable.** The warhead run was caught by arithmetic that did not fit its own story — the target survived 15 hits it should not have, and a third unit joined far earlier than the intended mark allowed. Neither was the thing under test; both were incompatible with the setup having worked.
4. **When you override anything, restate every field the consumer reads.** See the same `conventions.md` section — this is where "the scenario keeps running and returns a confident number" comes from.
5. **Ask who ELSE could satisfy your predicate.** Steps 1–4 all assume the failure is that your setup did not happen. The third instance above is the other shape: the setup was fine and the assertion was *reachable by another mechanism entirely*. Before running, name every path in the sim that could make the predicate true, and confirm the one under test is the only one. On the bot this bites hardest with **position**: a great many modules move units toward the same believed front, so "unit A ended up near unit B" is almost never attributable. Prefer an observable that only the mechanism under test can produce — for a transport change, that a unit was **carried** (latch that it left the world into a `Cargo`, then measure where it reappears), or the **timing gap** between armour and infantry arrival, rather than the distance between them once both are there.
6. **Then ask WHEN it could satisfy it.** The fourth instance is step 5 done right and still wrong: the observable genuinely was exclusive to the mechanism under test, but only during the window in which that mechanism **owned the actor**. Shared resources on this bot — carriers, squads, the free pool — are claimed, released and reclaimed by different modules across a match, so exclusivity is a property of an interval, not of an actor. Latch your measurement inside the interval and **stop it at the release** (here: freeze the peak at dismount), or you are reading someone else's later use of the same unit. Cheapest guard: emit the quantity you are asserting on from the code under test and read it back from `debug.log`, so the verdict and the mechanism are two independent observations rather than one.

Related: a corpus-scanning guard should assert it **measured something** before it asserts it found no violations, or a rename silently converts it into a test that passes by scanning nothing. `StancePositioningFireStanceTest` does this — it asserts it resolved more than zero stance assignments first.

### Two Lua traps that make a scenario lie about its own numbers

Both found on 2026-08-15, both cost a run, both look completely normal on the page.

**The failure message is evaluated EAGERLY, at registration.** `TestHarness.AssertWithin(deadline, fn, msg)` takes `msg` as an ordinary third argument, so Lua concatenates it **before the predicate runs even once**. Any counter interpolated into it therefore reports its **initial** value forever — usually zero. Measured: a verdict read `everCarried=0 peakPax=0` while a trace printed from inside the same closure, in the same run, read `everCarried=3 peakPax=2`. The message was not describing the run; it was describing the moment the test was registered. **Put live counters in a periodic `print` to `lua.log`, and keep the failure string static** — or you will diagnose from numbers that were never true.

**`IsDead` is true for a passenger inside a `Cargo`.** The natural idiom for "this unit was carried" is `not r.IsDead and not r.IsInWorld`, and it is **unsatisfiable for exactly the units it is meant to catch**: a boarded passenger is out of world AND reports dead, so the latch never fires. Measured: `peakPax=2` (carriage demonstrably happened) with `everCarried` stuck at 0 all match; dropping the `IsDead` term made it read 3 immediately. Latch on `not r.IsInWorld` alone — being permissive is safe when a separate clause requires the unit to **return** to the world, which a genuinely dead one never does. `test-combined-arms-rendezvous` carries the unfixed idiom and is likely mis-counting for the same reason.

### The converse: an UNCHANGED verdict is not evidence of safety unless you can show the change was live in that run

The rule above is about a green that proves nothing. This is its mirror, and it bites when you run a regression sweep to show a change is harmless: **a test that fails to move is indistinguishable from a test the change never reached.** "Ran 13 scenarios before and after, zero flips" sounds like evidence of safety and is compatible with the change having been completely inert in all 13 — wrong scenario set, a flag that did not apply, a build that did not get picked up. The verdict column looks identical in both worlds, exactly as it does for the false green.

So a no-flip sweep needs its own falsification control: **name the observable that proves the change was ACTIVE inside at least one of those runs.** It does not need to be the thing under test, and smaller is better — you want something the change could touch but the assertion does not read.

Worked example, 2026-08-14 (the `PlayerResources` economy gate, `DISCOVERIES.md` same date). Thirteen graded scenarios were run with and without the gate change at identical seeds; all thirteen returned identical verdicts, and the two failures failed identically on both sides. What made that a safety result rather than a blind one was a single number: in `test-supply-safe-front-keeps-cargo`, same seed and same scenario with only the gate differing, one unit's ammo read `71/100/100/71/70` before and `71/100/100/70/70` after. **One round.** That is worthless as a behavioural finding and decisive as a control — it proves the simulation diverged, so the change was live in that scenario, so the unchanged verdict is a real statement about the assertion rather than an artefact of the change never arriving.

Cheapest sources of such a control, in order: a per-tick telemetry line that already logs a quantity the change touches (`[composition] census` logs `earned`/`spent`); any incidental numeric in a failure note; or a one-line temporary trace. **If every observable in the sweep is byte-identical across the two arms, you have not shown the change is safe — you have shown it did not run.**

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
