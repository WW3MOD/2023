# Autotester improvement backlog

> Issues / friction observed while using the AUTOTEST harness in real sessions.
> Append here as we hit them; consume in a focused improvement session later.
>
> Format: one short bullet per issue, **bold** the symptom, then context.

## Open

- **Fatal Lua errors leave the game window hung without writing a verdict.**
  The runner waits for `result.json`; a Lua exception throws via `Context.FatalError(e)`
  but doesn't reach the `Test.Fail` path. Window stays open until manually killed,
  agent has to wait for runner timeout instead of seeing the error immediately.
  Fix idea: hook `ScriptContext.FatalError` (or `LuaScript`'s error path) to call
  `TestMode.WriteResult("fail", e.Message)` + `Game.Exit()` when test mode is active.
  Found 2026-05-09.

- **`local function phaseN()` is a forward-reference trap.**
  Lua's `local function` doesn't hoist. If phase1 calls phase2 and both are `local`,
  phase2 resolves as a global lookup at runtime → nil → `Trigger.AfterDelay(t, nil)`
  crashes inside the engine with `NullReferenceException`. Stack trace is unhelpful
  (points at `func.CopyReference()`, not the user's bad call site).
  Mitigation: forward-declare `local phase1, phase2, ...` at top, then assign.
  Fix idea: add a defensive null-check + better error message in `TriggerGlobal.AfterDelay`.
  Found 2026-05-09.

- **Position-prefs key is per-TTY, not per-test.**
  When alternating between manual playtest (right window) and automated test
  (left window), the saved-position can fight you. Minor.

- **`run-batch.sh --all` includes manual tests + known-RED tests.**
  Hangs on manual tests waiting for human verdict; reports failures for tests
  intentionally left RED for tracking unfixed bugs (e.g. `test-arty-force-attack-during-setup`).
  Fix idea: tests have a category in `description.txt` (auto / manual / red-tracked),
  `--all` only runs auto. Or: separate batch flag like `--auto`.

- **No way to peek inside a running test.**
  When a 60-second multi-phase test is running, the only feedback is the result
  file at the end. If something's wrong mid-test, you wait it out. Could log
  `Media.DisplayMessage` lines to a file the runner tails, or have phases write
  intermediate verdicts.

- **Time-to-result for a hung test is the runner's full timeout.**
  Related to the Lua-error issue above. A faster path: `Test.Mode=true` plus a
  watchdog. If no `Trigger.AfterDelay` fires for N seconds and we're past
  WorldLoaded, assume the script is stuck and exit-fail.

## Open (more recent)

- **Multi-phase test snapshot drift.** Phase 5 of `test-evac-suite` measures
  a `before/after` crew delta, but earlier phases leave dozens of crew bodies
  on the map that die off during Phase 5's 18-second wait (cookoff residuals,
  far-edge SpreadDamage). Net delta becomes meaningless. Workaround in place:
  use husk-count as the primary assertion. Better fix: per-phase cleanup
  (kill all non-current-phase actors) or per-phase Player slots so each
  phase's crew is isolatable.

## Done

- **Fatal Lua errors now auto-fail in test mode** (260509). `ScriptContext.FatalError`
  checks `TestMode.IsActive`; if so, writes `fail` verdict + `Game.Exit()` instead of
  the usual EndGame-to-menu path. Window closes immediately, runner sees the fail.

- **Helicopters now refuse to safe-land on a blocked cell** (260509).
  `HeliEmergencyLanding.IsSuitableTerrain` previously only checked the terrain
  type; a heli could autorotate down right on top of a husk, tree, or
  structure with no consequences. It now scans `ActorMap.GetActorsAt(cell)`
  and treats the touchdown as unsafe (→ `OnUnsafeLanding`, fireball, total
  loss) if any non-self, non-aircraft actor is on the cell. Aircraft sharing
  the cell at altitude don't block — only ground-pinned obstacles do.
