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

**Read the verdict from the banner, not just the exit code.** Every run ends with a line

```
AUTOTEST_VERDICT outcome=<OUTCOME> exit=<n> test=<name> run=<run-id>
```

where OUTCOME is one of `PASS`, `FAIL`, `SKIP`, `TIMEOUT-FAIL`, `CRASH`, `NO-RESULT`, `BAD-VERDICT`, `INTERRUPTED`, `HARNESS-ERROR`. It distinguishes what the exit code collapses: `CRASH` (the game threw — the exception log is named, and a crash is sometimes the *finding*, as when a sync guard fires) vs `NO-RESULT` (hung or closed by hand) vs `HARNESS-ERROR`; and `TIMEOUT-FAIL` (never answered) vs `FAIL` (answered no).

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
5. **One `supplyroute` per active faction.** Always include a Supply Route per side (e.g. `OwnSR: supplyroute / OpponentSR: supplyroute`), even if the test's units never interact with it. Reasons: (a) WW3MOD's gameplay model is "every player has an SR"; tests should reflect that. (b) Faction elimination triggers a Mission-Accomplished overlay that ends the game before the Lua poller writes a verdict — the runner reports "no result file written". A single SR keeps the faction alive when all its actors die. (c) `supplyroute` has `Targetable.TargetTypes: NoAutoTarget`, so it won't be picked up by AutoTarget scans or operator-retarget hunts — safe to drop anywhere on the map.

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

Three instances have now landed, by completely different mechanisms (the first two on 2026-08-12 alone):

- **The control that refused to go red.** `test-autotarget-preempt-air` was written to prove air-target preemption works, with a RED control pinning `PreemptScanInterval: 0` so the fix is switched off. Both arms were finally run — **and both passed** (`f910ac7d`). The unaided behaviour beat the 110-tick deadline on its own, so the tick budget never isolated the mechanism under test and the green arm was never evidence of anything. The fix had shipped on the strength of it.
- **The setup that silently reverted to engine defaults.** A scenario overrode a warhead to lower its damage, restating `Damage` and omitting `Penetration: 15`. Warhead overrides are constructed fresh rather than merged per-field, so `Penetration` fell back to the **engine** default of 1, took the armour-reduction branch, and the intended effect was cut by an order of magnitude. The run **completed and reported `pass`**, and the number it produced was plausible. (Mechanism: [`conventions.md`](../reference/conventions.md) §Weapons live under `Weapons:`.)
- **The observable the rest of the system could satisfy on its own** (2026-08-15, `DISCOVERIES.md` same date). `test-combined-arms-rendezvous` was written to prove a transport change — that ferried infantry are set down with the armour rather than at a cell the armour was never going to — and asserted the obvious thing: *are the riflemen near the tank?* It **passed with the fix disabled**. Infantry are armed, so `PoiOffensiveBotModule.StageFreePool` recruits them into the free pool and `AttackMove`s them to the **same staging anchor as the armour**, on foot, from tick 3. They arrive next to the tank under their own feet and the predicate goes true without a transport being involved at all. Nothing was misconfigured and no default silently reverted; the assertion was simply satisfiable by a second, unrelated mechanism. (The same scenario also spent two earlier runs on a `map.yaml` missing its `Rules: rules.yaml` line, so `rules.yaml` — Lua and all — was never loaded and the match ran on stock mod rules with a 0-byte `lua.log`. When a scenario times out with no verdict, **check `lua.log` is non-empty first**: that one fact separates "my predicate never went true" from "my script never ran".)

What to do about it, in order of cheapness:

1. **Run the control arm, and require it to FAIL.** A control that passes has falsified your test, not your hypothesis. Stop and rebuild the scenario before reading the green arm.
2. **Verify the pin was applied, do not assume it.** The preempt-air run did this correctly: a temporary trace in `AutoTarget.Created` printed the effective `PreemptScanInterval` per arm, so "the control really was switched off" was observed rather than inferred. A one-line trace is cheaper than a wasted run.
3. **Give the assertion a second, independent observable.** The warhead run was caught by arithmetic that did not fit its own story — the target survived 15 hits it should not have, and a third unit joined far earlier than the intended mark allowed. Neither was the thing under test; both were incompatible with the setup having worked.
4. **When you override anything, restate every field the consumer reads.** See the same `conventions.md` section — this is where "the scenario keeps running and returns a confident number" comes from.
5. **Ask who ELSE could satisfy your predicate.** Steps 1–4 all assume the failure is that your setup did not happen. The third instance above is the other shape: the setup was fine and the assertion was *reachable by another mechanism entirely*. Before running, name every path in the sim that could make the predicate true, and confirm the one under test is the only one. On the bot this bites hardest with **position**: a great many modules move units toward the same believed front, so "unit A ended up near unit B" is almost never attributable. Prefer an observable that only the mechanism under test can produce — for a transport change, that a unit was **carried** (latch that it left the world into a `Cargo`, then measure where it reappears), or the **timing gap** between armour and infantry arrival, rather than the distance between them once both are there.

Related: a corpus-scanning guard should assert it **measured something** before it asserts it found no violations, or a rename silently converts it into a test that passes by scanning nothing. `StancePositioningFireStanceTest` does this — it asserts it resolved more than zero stance assignments first.

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
personally emptied it first.** A timeout or crash makes this worse, not better — the harness gives up
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
7. **PITFALL: do not silence the unit-under-test with `Stance = "HoldFire"`** — silence the ENEMY instead (enemy on `HoldFire`, plus `Targetable: TargetTypes: NoAutoTarget` on the enemy in the scenario's `rules.yaml`, so your unit never acquires it and never leaves idle). Fire stance is not inert setup: `StancePositioningExecutor` opts out entirely below `FireAtWill` (`StancePositioningExecutor.cs:318`), so the convenient "no shots ⇒ no suppression, no chase" trick can switch off the very trait you are testing. This silently killed three stance scenarios for six weeks (2026-08-11 in `WORKSPACE/DISCOVERIES.md`); `StancePositioningFireStanceTest` now fails the build if a `test-stance-*` scenario does it. **Generally: before using a unit property as setup convenience, check that no gate reads it.**

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
| `tools/autotest/list-tests.sh` | Discovery |

## Existing tests

- `test-artillery-turret` — manual: does the Paladin's turret rotate before firing?
- `test-paladin-fires` — auto: Paladin's primary ammo drops within 12 s of force-engage on t90 (HoldFire). Demonstrates the green path.
- `test-arty-force-attack-during-setup` — auto, currently RED: force-attack-ground during setup-ticks. Layer 1 of the bug fixed (commit 51db91f7); Layer 2 (turret stalls mid-rotation) still open.
