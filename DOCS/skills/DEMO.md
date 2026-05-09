# DEMO — Stage a scenario for the human to look at

**Trigger:** the word `DEMO` in a user message — `DEMO <topic>`, or any phrasing where the user asks to "set up a scene", "show me X in game", "load this so I can see", etc. If the request is "I want to look at it myself," it's a DEMO, not an AUTOTEST.

**Gives you:** a staged scenario the user can launch and explore — units pre-placed, camera framed, optional pre-selection — without the agent looping on it. No `AssertWithin`, no JSON verdict, no autonomous re-runs. The agent builds the folder, hands off the run command, and stops.

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
| Agent runs it | yes, until verdict | **no — user runs it** |
| Picked up by `run-batch.sh --all` | yes | no |
| Discovery script | `list-tests.sh` | `list-demos.sh` |
| Runner | `run-test.sh` | `run-demo.sh` |

If a question with a yes/no answer is buried in there ("does the turret rotate?"), it's a manual *test*, not a demo. Demos are open-ended viewing.

## The loop

1. **Confirm scope.** What units / changes / situations should be staged? If the user said "show me all changes lately," look at recent commits and propose a list before building.
2. **Build the demo folder** under `mods/ww3mod/maps/demo-<name>/`.
3. **Hand off.** Print the run command (`./tools/test/run-demo.sh demo-<name>`) and stop. Do not launch yourself — launching is foreground and the user wants to drive.
4. **Commit** the demo folder.

## Folder layout

```
mods/ww3mod/maps/demo-<name>/
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
./tools/test/list-demos.sh                # discovery
./tools/test/run-demo.sh demo-<name>      # launch
./tools/test/run-demo.sh --position=left demo-<name>
```

Same flags as `run-test.sh`. Exit code is `0` whether the user closes cleanly or just clicks the X — the runner doesn't check for a result file.

## Conventions

- **Stay in `demo-*` prefix.** Don't sneak demos into `test-*` — `run-batch.sh --all` will try to run them and report "error: no result file."
- **No `Test.Pass`/`Fail`/`Skip` calls.** If you find yourself writing one, the thing is a test — move it to `test-*` and use AUTOTEST.
- **Commit demo folders.** They double as a record of "what we showed the user when X landed" and stay useful for the next time someone wants to look at the same area.
- **Demo names should describe the *subject*, not the question** — `demo-changed-vehicles-260509`, not `demo-do-vehicles-look-right`.
