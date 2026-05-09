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

- **AUTOTEST + DEMO might want to merge.** They share the harness completely;
  the only difference is whether Lua calls `Test.Pass` and whether the runner
  expects a verdict. Convergence path: one skill (`TEST <name>`) with two
  modes (`--auto` / default `--demo`) and one runner that reads a verdict-mode
  flag. Worth doing the next time someone refactors the harness.

- **No screenshot capability for visual verification.** The agent can't see
  the game window. `Game.TakeScreenshot()` exists in the engine
  (`engine/OpenRA.Game/Renderer.cs:528`); exposing it via a `Test.Screenshot()`
  Lua binding plus runner-side image diffing would let the agent verify
  visual variants without the user. Tracked but not built.

- **`-Trait:` of a non-existent trait silently breaks the entire rules.yaml.**
  Test rules don't apply at all, no Lua errors, no obvious failure mode —
  just default behaviour everywhere. Found while debugging
  `test-burn-compare`. Defensive fix idea: surface a `debug.log` warning
  rather than silently dropping the override. Found 2026-05-09.

- **Variant inheritance lessons (260509)** captured in
  `DOCS/skills/DEMO.md` § "Multi-variation comparison". Summary:
  templates can't contain `-Trait:` removals; new actor types need
  `RenderSprites: Image:` pinned to the parent; `smoke_mtd` needs
  `StartSequence: start, Sequence: loop` for proper animation; cargo
  passengers must be created with `addToWorld=false`; `ConquestVictoryConditions`
  fires "Mission accomplished" instantly when no enemy player has units.

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

- **Multi-variation `test-burn-compare` shipped** (260509). 11 variants ×
  5 vehicles = 55 actor types in a single demo, generated by Python script
  (`/tmp/burn-compare-gen.py`). Pattern documented in `DOCS/skills/DEMO.md`.
  Selected variant V1 (StartFraction=50, EndFraction=0) locked into
  production `^EffectsWhenDamagedVehicles` and `^Helicopter` rules.
  Per-vehicle cookoff weapons added: humvee/m113/btr → `VehicleCookoffTiny`,
  m270/grad/tos → `VehicleCookoffLarge`, others use the default `VehicleCookoff`.

- **`EjectionDamageState` default Heavy** (was Critical, 260509). Crew
  bails out when HP first drops below 50%, not 25%. Fast first-crew
  eject (~1s) without waiting for a bleed into Critical state. Per-actor
  override available if a particular vehicle should bail later.

- **Crew inherits wreck's onfire intensity at eject** (260509). New
  `CrewFireStackOffset` field on `VehicleCrewInfo` (default `-3`): crew
  spawns with the vehicle's current stack count plus the offset. A wreck
  at stack 8 spawns crew already burning at stack 5 — they bleed out
  fast, often die in the open. Realistic; loud signal that "the tank
  was already a fireball when they got out".

- **Burning system: `GrantStackingConditionOnHealthFraction`** (260509).
  New trait that grants N stacks of an external condition based on HP
  fraction. Maps `StartFraction` → 1 stack, `EndFraction` → MaxStacks
  linearly. Math extracted to `CalculateStacks` static method, pinned
  by 10 unit tests in `engine/OpenRA.Test/OpenRA.Mods.Common/`.
