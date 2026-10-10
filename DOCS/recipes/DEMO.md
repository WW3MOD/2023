# DEMO — Stage a scenario for the human to look at

**Trigger:** the word `DEMO` in a user message — `DEMO <topic>`, or any phrasing where the user asks to "set up a scene", "show me X in game", "load this so I can see", etc. If the request is "I want to look at it myself," it's a DEMO, not an AUTOTEST.

**Gives you:** a staged scenario for the user to explore — units pre-placed, camera framed, optional pre-selection. No `AssertWithin`, no verdict, no autonomous re-runs. The user looks around and closes the window when done.

**When *not* to use it:**
- Behavioural fix you want verified — that's **AUTOTEST**.
- Real game on a full map with a focus brief — that's **PLAYTEST**.
- One-line YAML check the user can eyeball without launching — just describe the change.

**Who launches.** Opening a demo starts the game, so it follows CLAUDE.md §"Who runs what". A worker dispatched by a manager builds and commits the demo folder and hands up the exact launch command (e.g. `./tools/autotest/run-demo.sh --timeout 7200 demo-<name>`) plus what the viewer should look at; it does not launch. An agent working directly with the user launches it for them.

---

## How DEMO differs from AUTOTEST

Same harness plumbing (`Test.Mode=true`, in-game panel, RESTART button, edge-pan disabled in windowed). What changes is the stance and the verdict:

| | AUTOTEST | DEMO |
|---|---|---|
| Folder prefix | `test-*` | `demo-*` |
| Lua calls `AssertWithin` / `Test.Pass` | yes | **no** |
| Writes a verdict | yes | no |
| Ends when | the scenario answers | the user closes the window |
| Picked up by `run-batch.sh --all` | yes | no (`--all` globs `test-*` only) |
| Discovery script | `list-tests.sh` | `list-demos.sh` |
| Runner | `run-test.sh` | `run-demo.sh` |

If a yes/no question is buried in there ("does the turret rotate?"), it is a manual *test*, not a demo. Demos are open-ended viewing.

## The loop

1. **Confirm scope.** What units / changes / situations should be staged? If the user said "show me all changes lately," look at recent commits and propose a list before building.
2. **Build the demo folder** under `tools/autotest/scenarios/demo-<name>/`.
3. **Check it statically**: `make lua-gate` / `.\make.ps1 lua-gate` (names every unwired script and every Lua binding that does not exist), plus the geometry checks in [`AUTOTEST.md` §"Verify before you ask for a slot"](AUTOTEST.md#verify-before-you-ask-for-a-slot). A worker lists the YAML files it touched so the manager can lint them before the first launch.
4. **Optionally smoke-verify with a launch** *(a launch — see "Who launches")*: inject a temporary `Trigger.AfterDelay(25, function() Test.Pass("demo smoke") end)` in `WorldLoaded`, run `./tools/autotest/run-test.sh --hidden demo-<name>` (the test runner accepts any folder), confirm `PASS`, then strip the trailer. It catches `rules.yaml` typos, missing actors and broken bindings before the user sees a black screen. Each smoke run is a run of the same test, so the rerun limit in CLAUDE.md §"Who runs what" applies.
5. **Launch for the user** *(a launch)*: `./tools/autotest/run-demo.sh --timeout 7200 demo-<name>`, in the background. The window opens visible with sound on; when the user closes it the background task completes. Queue the next demo only after the previous one finishes — never two game instances at once.
6. **Commit** the demo folder, with any smoke trailer stripped.

If the user says "don't launch — just give me the command," print the command instead.

### Pass `--timeout`: the watchdog closes a demo at 5 minutes

`run-demo.sh` delegates to `run-test.sh --visible --audio "$@"` and forwards no `--timeout`, so `run-test.sh`'s 300 s wall-clock watchdog kills the demo window at five minutes and reports `TIMEOUT-FAIL` (exit 1), which `run-demo.sh` passes through. Flags before the demo name are forwarded, so pass a large `--timeout` yourself. Filed in `WORKSPACE/bugs/discovered.md`.

## Folder layout

```
tools/autotest/scenarios/demo-<name>/
├── description.txt        # one-line panel description (recommended)
├── map.yaml               # actor placement + player slots (same rules as tests)
├── rules.yaml             # LuaScript: test-helpers.lua, demo-<name>.lua
├── demo-<name>.lua        # staging only — camera, selection, optional UI hints
├── map.bin                # copy from a sibling test/demo
└── map.png                # copy from a sibling test/demo
```

`map.yaml` follows the test-scenario rules in [`AUTOTEST.md` §"`map.yaml` rules"](AUTOTEST.md): one `Playable: True` slot, lowercase actor types, `LockColor`/`LockFaction`, `Visibility: MissionSelector`, `Categories: Test`, one `supplyroute` per active faction, and **a top-level `Rules: rules.yaml` line preceded by a blank line** — without it the demo's rules and Lua are never loaded and it opens on stock rules (`demo-shake-profiles/map.yaml` is a working example).

## Lua skeleton

```lua
-- demo-<name>.lua
WorldLoaded = function()
    TestHarness.FocusBetween(Abrams, Tank2, Tank3)   -- center camera on the group
    TestHarness.Select(Abrams)                        -- pre-select for convenience
    Camera.Zoom = 0.5                                 -- frame it; do not ask the viewer to zoom

    -- That's it. No AssertWithin. No Test.Pass.
    -- The user looks around; closes the window when done; presses End to restart.
end
```

### Frame the shot yourself — the viewer should not have to

A demo that opens at the wrong zoom on the wrong part of the map shows less than you built: at default zoom on a 128x128 map, a 102-cell blast wave shows about a third of itself. Set the camera in `WorldLoaded` as part of staging, and never leave the viewer a `description.txt` instruction to zoom out by hand.

| Call | Effect |
|---|---|
| `Camera.Position = WPos.New(x * 1024 + 512, y * 1024 + 512, 0)` | Centre the view on a cell. |
| `Camera.Zoom = 0.5` | Zoom as a **multiple of the default level**: `1` default, below 1 further out, above 1 closer in. Clamped, so an unreachable value is applied as far as it goes. |
| `Camera.MinZoom` / `Camera.MaxZoom` | The clamp, same units. `Camera.Zoom = Camera.MinZoom` is "as far out as this display goes". |
| `Media.DisplayMessage(text, prefix)` | A chat-log line — best for narrating a sequence (`demo-shake-profiles` announces each profile this way). |
| `UserInterface.SetMissionText(text, color?)` | One persistent line across the top of the screen. |
| `Media.FloatingText(text, pos, duration?, color?)` | A label at a world position. |
| `Trigger.OnTick(func)` | Run `func()` every tick. Prefer it to a self-rescheduling `Trigger.AfterDelay(1, ...)` loop. |
| `Test.Screenshot(label, note?)` | Capture a PNG. Works in a demo — `run-demo.sh` runs through `run-test.sh`, which passes `Test.Mode=true`. See [`SCREENSHOT.md`](SCREENSHOT.md) for the one-frame-late trap. |

**`Camera.Zoom` is a multiple of the default, not the raw `Viewport.Zoom`**, which depends on the viewer's resolution and viewport-distance setting — the same raw number frames a different amount of map on a 1080p and a 1440p display, and demos are routinely watched on a different machine from the one that authored them.

**A demo that captures its own decisive frame is worth more than one that does not.** A `Test.Screenshot` at the beat that matters turns "did the fireball sit on the ground?" into a file that can be looked at next week. It does not make the demo a test — there is still no verdict.

If the demo needs scripted enemy behaviour to show off a feature (e.g. an enemy attack-move so the user can watch the response), stage it here — but keep it loose.

## Running

```bash
./tools/autotest/list-demos.sh                              # discovery
./tools/autotest/run-demo.sh --timeout 7200 demo-<name>     # launch (centered, visible, sound on)
./tools/autotest/run-demo.sh L --timeout 7200 demo-<name>   # left half (also R, F, C)
```

`run-demo.sh` takes the same flags as `run-test.sh` and injects `--visible --audio`. If the user puts `L`, `R`, `F` or `C` in the trigger ("DEMO L"), pass it through as the first positional arg.

### A demo has no verdict, so judge it by its MANIFEST and its FRAMES — never by an exit code

**A correct demo produces the artefacts of a failure:**

- **`run-test.sh` reports `NO-RESULT` (exit 3) when no verdict was written** — *"The game hung, was closed by hand, or never reached an assertion."* — which is the expected outcome of every correct demo. `run-demo.sh` maps exit 3 back to 0 (the call is written `if …; then rc=0; else rc=$?; fi` because the script runs under `set -e`). Any other non-zero code passes straight through — including the watchdog's `TIMEOUT-FAIL` above.
- **`lua.log` at 0 bytes is not a finding either.** It collects Lua `print` output only, so a demo that drives cameras, timers and captures without printing writes nothing. `debug.log` is the discriminator: scripted activity there (`Taking screenshot …` in scripted order) proves the timers fired. The three readings: [`AUTOTEST.md` §"An empty `lua.log` means nothing on its own"](AUTOTEST.md#an-empty-lualog-means-nothing-on-its-own).

Together they hand a healthy run a red-looking verdict **and** an empty log. **The acceptance evidence for a demo is its `manifest.json` and the frames it captured** — the strongest argument for a `Test.Screenshot` at the beat that matters.

## Conventions

- **Stay in the `demo-*` prefix.** A demo filed under `test-*` shows up in `list-tests.sh` as a test and is excluded from `run-batch.sh --all` with an announcement because it has no verdict call — noise in both places. (`test-burn-compare` and `test-artillery-turret` are older examples of exactly this.)
- **No `Test.Pass`/`Fail`/`Skip` calls** outside a temporary smoke trailer. If you find yourself writing one, the thing is a test — move it to `test-*` and use AUTOTEST.
- **Commit demo folders.** They record "what we showed the user when X landed" and are reusable.
- **Name the *subject*, not the question** — `demo-changed-vehicles-260509`, not `demo-do-vehicles-look-right`.

## Pack the map: many scenarios, ongoing context

User preference: when the subject can be illustrated in several variations or angles, **don't pick one — pack them all into a single map**. The user has pause / fast-forward / restart and would rather see more at once than chase separate small demos.

- **Multiple lanes / rows** showing the same mechanic across parameter axes (0/1/2/3 trees on the firing line, rookie/veteran/elite, stationary/moving target). Each lane is its own mini-scenario.
- **Continuous loop on each lane.** When a target dies, respawn it in place via `Trigger.OnKilled` so the mechanic is exercised many times. Same for ammo — re-arm or trickle it back.
- **Background skirmish.** Reserve a slice of the map for an ongoing fight with mixed unit types using the same change, spawning replacements — "does this still feel right inside a real game?"
- **Don't let the skirmish compete with the lanes.** Keep it visually separated (own player slot if needed, pathing barriers, distance).
- **Messy is fine.** Strip back only when something actively obscures the answer.

```lua
-- Respawn a downed actor in place
local function respawn(slot, actorType, owner, location, facing)
    Trigger.OnKilled(slot, function()
        Trigger.AfterDelay(50, function()       -- 50 ticks = 3 s at the 60 ms default
            local fresh = Actor.Create(actorType, true,
                { Owner = owner, Location = location, Facing = facing })
            respawn(fresh, actorType, owner, location, facing)
        end)
    end)
end
```

Spell out the **layout map** at the top of the Lua file as a comment ("rows 4..28 are tree-density lanes 0..6; rows 30..32 are the moving-target sub-scenario; bottom half is the skirmish") so the next reader need not re-read `map.yaml`.

## Multi-variation comparison (the "pick one" pattern)

Use this when the user says "show me N versions side-by-side and tell me which looks best." Worked example: `tools/autotest/scenarios/test-burn-compare/` (an 11-variant burn-ramp comparison; filed under `test-*` before the prefix convention, but a demo in substance).

- N variant *templates* (`^Burn_V1` … `^Burn_V11` there) with the same trait set, varying one or two parameters. Templates ONLY add traits — never `-Trait:` removals (Gotcha 1).
- M base actor types. For each base × variant, a derived actor (`humvee.v1`, …) inheriting from both. Trait removals live on the actor, not the template.
- Lua spawns the M×N grid in N columns × M rows, so variants read left-to-right and base actors top-to-bottom.
- The same scripted damage on all, so the variant config is the only variable.
- **Generate the rules.yaml from a variant table** in a small script committed beside the demo (or kept in the commit message) — with ~50 actor entries, hand-writing is brittle. Do not leave the generator in `/tmp`; it does not survive.

### Gotchas (each bit during a variant comparison)

1. **`-Trait:` removals can't sit inside a template.** Templates have no traits to remove yet; the engine errors with `rules.yaml:NN: There are no elements with key X to remove`. Put removals on the actor entry whose inherited parent has the trait.
2. **New actor types need `RenderSprites: Image:` pinned.** The default `Image` is the actor name (`humvee.v1`), which is not a sprite key:
   ```yaml
   humvee.v1:
       Inherits: humvee
       RenderSprites:
           Image: humvee
   ```
3. **`-Trait:` at actor level removes BOTH the inherited and the template's version** if both share the `@suffix`. Don't remove a trait you also want to override — let inheritance order (later wins) merge it.
4. **`smoke_mtd` + `WithIdleOverlay`:** use `StartSequence: start, Sequence: loop`. `Sequence: idle` plays a static-looking single frame on some sprites.
5. **`Image: fire, Sequence: 5`** renders with an opaque black background (palette/alpha mismatch). Use `Sequence: 1` for the husk-style big fire; bridge tiers by layering the prior tier's overlay.
6. **Cargo loading from Lua:** create the passenger with `addToWorld=false`, then `vehicle.LoadPassenger(soldier)`. With `addToWorld=true`, the vehicle's `UnloadCargo` on death calls `World.Add` for an actor already in the world → `ArgumentException: An item with the same key has already been added`.
7. **`StartingUnits@*` has been seen to spawn a Supply Route even with `-SpawnStartingUnits:`** in some configurations (not re-verified). Giving each player its own `supplyroute` (the map.yaml rule) sidesteps it.
8. **`ConquestVictoryConditions` triggers "Mission accomplished" instantly if no enemy player has units.** Either `-ConquestVictoryConditions:` in the scenario `rules.yaml`, or place a token enemy actor (the enemy's `supplyroute` serves).
9. **`-Trait:` of a non-existent trait** breaks the whole `rules.yaml`. If the demo's rules look ignored (Lua does not run, default smoke shows, an SR auto-spawns), check every `-Trait:` matches an inherited trait that exists.

### Diagnostic discipline

When a demo's rules look dead but the game still launches:
1. Read the `debug.log` copied into the run dir (printed as `Run dir:`) and grep for `rules.yaml`, `to remove`, `InvalidOperation`, `` Image ` ``. The live log is global and may already belong to another run — see [`AUTOTEST.md` §"Logs: fixed paths, no run identity, and concurrent writers"](AUTOTEST.md).
2. Read the newest `exception-*.log` from the same Logs directory for crashes after `WorldLoaded`.
3. Add a `Media.DisplayMessage` at the top of `WorldLoaded` — if it never appears in chat, the rules block is not being loaded (check the `Rules: rules.yaml` line first).
4. Strip `rules.yaml` back to a single trivial variant (`humvee.v1: Inherits: humvee`) and re-add complexity until it breaks.
