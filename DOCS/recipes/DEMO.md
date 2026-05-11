# DEMO — Stage a scenario for the human to look at

**Trigger:** the word `DEMO` in a user message — `DEMO <topic>`, or any phrasing where the user asks to "set up a scene", "show me X in game", "load this so I can see", etc. If the request is "I want to look at it myself," it's a DEMO, not an AUTOTEST.

**Gives you:** a staged scenario the agent launches for the user to explore — units pre-placed, camera framed, optional pre-selection. No `AssertWithin`, no JSON verdict, no autonomous re-runs. The agent builds the folder **and runs it** (run-demo.sh in the background); the user looks around and closes the window when done.

**When *not* to use it:**
- Behavioral fix you want verified — that's **AUTOTEST**.
- Real game on a full map with a focus brief — that's **PLAYTEST**.
- One-line YAML check the user can eyeball without launching — just describe the change.

---

## How DEMO differs from AUTOTEST

Same harness plumbing (`Test.Mode=true`, in-game panel, RESTART button, edge-pan disabled in windowed). What changes is **agent stance and verdict expectation**:

| | AUTOTEST | DEMO |
|---|---|---|
| Folder prefix | `test-*` | `demo-*` |
| Lua calls `AssertWithin` / `Test.Pass` | yes | **no** |
| Writes `result.json` | yes | no |
| Agent runs it | yes, until verdict | **yes — agent launches in background; user closes the window when done** |
| Picked up by `run-batch.sh --all` | yes | no |
| Discovery script | `list-tests.sh` | `list-demos.sh` |
| Runner | `run-test.sh` | `run-demo.sh` |

If a question with a yes/no answer is buried in there ("does the turret rotate?"), it's a manual *test*, not a demo. Demos are open-ended viewing.

## The loop

1. **Confirm scope.** What units / changes / situations should be staged? If the user said "show me all changes lately," look at recent commits and propose a list before building.
2. **Build the demo folder** under `tools/autotest/scenarios/demo-<name>/`.
3. **Smoke-verify.** Inject a temporary `Trigger.AfterDelay(25, function() Test.Pass() end)` in `WorldLoaded`, run via `./tools/autotest/run-test.sh demo-<name>` (the test runner accepts any folder), confirm the verdict is `pass`, then strip the trailer. Catches rules.yaml typos, missing actors, broken Lua bindings before the user sees a black screen.
4. **Launch for the user.** Call `./tools/autotest/run-demo.sh demo-<name>` in the **background** (`run_in_background: true`). The window opens visible (run-demo.sh forces `--no-minimize`); the user explores; when they close the window the background task completes and you get a notification. If staging multiple demos, queue the next one only after the previous one finishes — running two game instances at once is jarring.
5. **Commit** the demo folder. Smoke-test trailer must be stripped before committing.

If the user explicitly says "don't launch — just give me the command" (rare, e.g. they want to run on a different machine), respect that and print the command instead.

## Folder layout

```
tools/autotest/scenarios/demo-<name>/
├── description.txt        # one-line panel description (recommended)
├── map.yaml               # actor placement + player slots (same gotchas as tests)
├── rules.yaml             # LuaScript: test-helpers.lua, demo-<name>.lua
├── demo-<name>.lua        # staging only — camera, selection, optional UI hints
├── map.bin                # copy from a sibling test/demo
└── map.png                # copy from a sibling test/demo
```

`map.yaml` follows the same rules as test scenarios — see [`AUTOTEST.md` § Launch.Map quirks](AUTOTEST.md). One `Playable: True` slot, lowercase actor names, `LockColor`/`LockFaction`, `Visibility: MissionSelector`, `Categories: Test`.

## Lua skeleton

```lua
-- demo-<name>.lua
WorldLoaded = function()
    TestHarness.FocusBetween(Abrams, Tank2, Tank3)   -- center camera on the group
    TestHarness.Select(Abrams)                        -- pre-select for convenience

    -- That's it. No AssertWithin. No Test.Pass.
    -- The user looks around; closes the window when done; presses End to restart.
end
```

If the demo needs scripted enemy behavior to show off a feature (e.g., enemy attack-moves so the user can watch the response), stage it here — but keep it loose; this isn't a test.

## Running

```bash
./tools/autotest/list-demos.sh                # discovery
./tools/autotest/run-demo.sh demo-<name>      # launch (centered, visible)
./tools/autotest/run-demo.sh L demo-<name>    # left half (also R, F, C)
```

Same flags as `run-test.sh`, but the demo runner injects `--no-minimize` so the window stays visible. If the user includes `L`, `R`, or `F` in the trigger ("DEMO L", "DEMO <topic> R"), pass that letter through as the first positional arg. Exit code is `0` whether the user closes cleanly or just clicks the X — the runner doesn't check for a result file.

## Conventions

- **Stay in `demo-*` prefix.** Don't sneak demos into `test-*` — `run-batch.sh --all` will try to run them and report "error: no result file."
- **No `Test.Pass`/`Fail`/`Skip` calls.** If you find yourself writing one, the thing is a test — move it to `test-*` and use AUTOTEST.
- **Commit demo folders.** They double as a record of "what we showed the user when X landed" and stay useful for the next time someone wants to look at the same area.
- **Demo names should describe the *subject*, not the question** — `demo-changed-vehicles-260509`, not `demo-do-vehicles-look-right`.

## Pack the map: many scenarios, ongoing context

User preference (260510): when the demo subject can be illustrated in
several variations or angles, **don't pick one — pack them all into a
single map**. The user has pause / fast-forward / restart and would
rather see more at once than chase the agent back-and-forth between
small focused demos.

What "pack" means in practice:

- **Multiple lanes / rows** showing the same mechanic across parameter
  axes (e.g., 0/1/2/3 trees on the firing line, or
  rookie-vs-veteran-vs-elite, or stationary-vs-moving target). Each
  lane is its own self-contained mini-scenario.
- **Continuous loop on each lane.** When a target dies, respawn it in
  place via `Trigger.OnKilled` so the user sees the mechanic exercised
  many times, not just once. Same for ammo — re-arm or trickle ammo
  back periodically.
- **Background skirmish.** Reserve a slice of the map (often the
  bottom or one side) for a 4v4 ongoing fight with mixed unit types
  using the same change. Spawn replacements as units die. Lets the
  user sanity-check "does this still feel right inside a real game,
  not just in a controlled lane?"
- **Don't compete with the focused lanes.** The skirmish is for
  context, not for measurement. Keep it visually separated and don't
  let it bleed into the controlled rows (own player slot if needed,
  pathing barriers, distance).
- **Messy is fine.** The user uses pause/speed/restart to make sense
  of overlap. Don't strip back to be tidy — strip back only when
  something actively obscures the answer (e.g. effects covering the
  controlled rows).

Lua patterns for the above:

```lua
-- Respawn a downed actor in place
local function respawn(slot, actorType, owner, location, facing)
    Trigger.OnKilled(slot, function()
        Trigger.AfterDelay(50, function()       -- 2s respawn delay
            local fresh = Actor.Create(actorType, true,
                { Owner = owner, Location = location, Facing = facing })
            respawn(fresh, actorType, owner, location, facing)
        end)
    end)
end

-- Background skirmish: spawn an even mix of units on each side,
-- target nearest enemy, respawn on death.
```

Spell out the **layout map** at the top of the Lua file as a comment so
future-you (or another agent) can pattern-match without re-reading the
map.yaml — e.g. "rows 4..28 are tree-density lanes 0..6; rows 30..32
are the moving-target sub-scenario; bottom half is the 4v4 skirmish."

## Multi-variation comparison (the "pick one" pattern)

Use this when the user says "show me N versions side-by-side and tell me which looks best." See `tools/autotest/scenarios/test-burn-compare/` for the worked example (11-variant burn-ramp comparison that produced the production V1 settings on 260509).

**Shape:**
- N variant *templates* (`^Variant_VN`) with the same trait set, varying one or two parameters between them. Templates ONLY contain new traits — never `-Trait:` removals (see Gotcha #1 below).
- M base actor types (humvee, m113, …). For each base × each variant, generate a derived actor (`humvee.v1`, `humvee.v2`, …) inheriting from both. Trait removals live on the actor, not the template.
- Lua spawns the M×N grid in N columns × M rows so the user can read variants left-to-right and base actors top-to-bottom.
- Same scripted damage applied to all so the only variable is the variant config.
- No `Test.Pass` — runs until the user closes (or temporarily add `Test.Pass()` for verifier mode while iterating, then strip it before handing to the user).

**Generator:** keep the variant table in a Python script that prints the rules.yaml. With N×M ≈ 50 actor entries, hand-writing is brittle; a generator makes it cheap to add a 12th variant or swap a sprite. See `/tmp/burn-compare-gen.py` from session 260509 for a template.

**Verifying without burning the user's eyes:** add a temporary `Test.Pass()` trailer in the Lua so each iteration writes a JSON verdict. Confirm it passes (catches bad variant YAML, missing sprites, conditions that error). Then strip the `Test.Pass` and hand the live demo to the user. Saves dozens of "did it crash?" round-trips.

### Gotchas (every one of these bit during the burn-compare iteration)

1. **`-Trait:` removals can't sit inside a template.** Templates have no traits to remove yet. Engine errors with `rules.yaml:NN: There are no elements with key X to remove`. Put removals on the *actor entry* where the inherited parent has the trait, not in `^Variant_VN`.
2. **New actor types need `RenderSprites: Image:` pinned.** Default `Image` is the actor name (`humvee.v1`), which isn't a valid sprite key. Override:
   ```yaml
   humvee.v1:
       Inherits: humvee
       RenderSprites:
           Image: humvee
   ```
3. **`-Trait:` at actor level removes BOTH the inherited and the variant template's version** if both have the same `@suffix`. Don't remove a trait you also want to override — let inheritance order (later wins) handle the merge instead.
4. **`smoke_mtd` + `WithIdleOverlay`:** use `StartSequence: start, Sequence: loop` for proper animation. `Sequence: idle` plays a static-looking single frame on some sprites and reads as janky.
5. **`Image: fire, Sequence: 5`** (the small-fire-bridge attempt) renders with an opaque black background — palette/alpha mismatch. Stick to `Sequence: 1` for the husk-style big fire; bridge the gap by *layering* the prior tier's overlay (e.g. `infantry-burn-5` on `onfire >= 7` overlapping with `infantry-burn-4` at 7-8).
6. **Cargo loading from Lua:** create the passenger with `addToWorld=false`, then `vehicle.LoadPassenger(soldier)`. With `addToWorld=true`, on death the vehicle's `UnloadCargo` activity calls `World.Add` for an actor already in the world → `ArgumentException: An item with the same key has already been added`.
7. **`StartingUnits@*` spawns an SR even with `-SpawnStartingUnits:`** in some configurations. Easier to give the player a token actor in a corner so the trait is satisfied silently than to enumerate every `-StartingUnits@<class>:`.
8. **`ConquestVictoryConditions` triggers "Mission accomplished" instantly if no enemy player has units.** Either `-ConquestVictoryConditions:` in the test rules.yaml, or place a token enemy actor far enough away that scan range can't see them.
9. **`-Trait:` of a non-existent trait** silently breaks the entire rules.yaml. If you see symptoms like "test rules ignored" (Lua doesn't run, default smoke shows, SR auto-spawns), check that every `-Trait:` matches an inherited trait that actually exists.

### Diagnostic discipline

When variant rules.yaml looks dead but the game still launches, the failure path is:
1. Read `~/Library/Application Support/OpenRA/Logs/debug.log` and grep for `rules.yaml`, `to remove`, `InvalidOperation`, `Image \``.
2. Read `~/Library/Application Support/OpenRA/Logs/exception-*.log` (most recent) for crashes after WorldLoaded.
3. Add a smoke-test Lua announce at the top of `WorldLoaded` to confirm the test rules and Lua are even being loaded — if you don't see your `[LUA]` line in chat, the rules block is wrong.
4. Strip the rules.yaml back to a single trivial variant (one `humvee.v1: Inherits: humvee`) and re-add complexity until it breaks.
