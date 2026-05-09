# AUTOTEST — Automated test-driven debug loop

**Trigger:** type `AUTOTEST <bug or feature description>` (or `AUTOTEST <existing-test-name>` to extend an existing scenario). I run the loop below without you having to watch the game.

**Gives you:** I write a deterministic test, run it before the fix to confirm RED, apply the fix, run it again to confirm GREEN, regression-check other tests, and commit. You can walk away — verdict comes back as a JSON exit code I can read.

**When *not* to use it:** visual / "feels off" / tuning bugs. Your eyes are faster than my trace dumps for those — use **PLAYTEST** instead.

---

## What the harness is

The game can be launched into a small, deterministic scenario; the verdict (pass/fail/skip) is written to a JSON file and exit-coded back, so I can iterate without supervision. Activated only by the `Test.Mode=true` launch arg — normal launches are completely unaffected.

## Quick reference

```bash
./tools/test/list-tests.sh                          # what's available
./tools/test/run-test.sh <test-folder>              # run one
./tools/test/run-batch.sh <t1> <t2> ...             # run several
./tools/test/run-batch.sh --all                     # run every test-* folder
./tools/test/run-test.sh --position=left <test>     # force window left half
./tools/test/run-test.sh --fullscreen <test>        # skip windowing
./tools/test/run-test.sh --help                     # flag list
```

Exit codes: `0` pass, `1` fail, `2` skip, `3` error/no-result.

## The loop (what I run when you trigger AUTOTEST)

1. **Frame the assertion**: "X must happen within N seconds when Y is set up". Confirm with user if ambiguous.
2. **Write a failing test**: copy a `test-*` folder, set up the actors and a Lua `TestHarness.AssertWithin(...)` predicate. Use the `description.txt` to surface intent in the panel.
3. **Verify RED**: run the new test pre-fix. Must fail with the expected timeout / failure reason. If it passes accidentally, the test isn't measuring the right thing.
4. **Investigate + fix**: read code, apply changes. If diagnosis needs more data, add temporary `Console.WriteLine` traces gated on `TestMode.IsActive`.
5. **Verify GREEN**: re-run the new test. Must pass within reasonable time.
6. **Regression check**: `./tools/test/run-batch.sh --all` or at least the closest existing tests, to make sure the fix didn't break anything.
7. **Strip diagnostics**: remove any temporary trace lines I added.
8. **Commit**: test scenario + fix + tracker update in a single commit. Test stays committed so the bug can't silently regress.

If the bug has multiple layers, fix what I can, leave the test RED for the unfixed parts, and document in `WORKSPACE/RELEASE_V1.md` what's left. The red test becomes the next session's gateway.

## Writing a test scenario

```
mods/ww3mod/maps/test-<name>/
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

- **Manual** — Lua only stages (camera, selection); user watches and types verdict in chat. Example: `test-artillery-turret` (the original "did the turret rotate?" test). Best when the bug is visual or hard to assert numerically.
- **Auto-asserting** — Lua uses `TestHarness.AssertWithin(...)` to verdict itself. Game writes JSON and exits; runner exit-codes back. Example: `test-paladin-fires`. Pair with `--all` for unattended regression sweeps.

## Lua API

### `TestHarness.*` (in `mods/ww3mod/scripts/test-helpers.lua`)

| Function | Purpose |
|---|---|
| `FocusBetween(a, b, ...)` | Center camera on the midpoint of N actors |
| `Select(actor)` | Pre-select unit-under-test (no manual click needed) |
| `AssertWithin(seconds, predicate, failReason)` | Poll predicate every tick. `true`→Pass, `"fail: <reason>"`→Fail immediately, timeout→Fail with reason. |
| `AssertAfter(seconds, predicate, failReason)` | Wait `seconds`, then assert once |

### `Test.*` (engine global, gated on TestMode.IsActive)

| Function | Effect |
|---|---|
| `Test.Pass()` | Write `pass` verdict, `Game.Exit()` |
| `Test.Fail(reason)` | Write `fail` verdict + reason, exit |
| `Test.Skip(reason)` | Write `skip` verdict + reason, exit |

### Useful actor methods (existing OpenRA Lua API + WW3MOD additions)

| Method | What it does |
|---|---|
| `Paladin.Attack(target, allowMove?, forceAttack?)` | Issue attack on actor (existing API). `queued: true` internally. |
| `Paladin.AttackGround(cell, allowMove?, queued?)` | Ctrl+click on terrain. WW3MOD addition. |
| `Paladin.AmmoCount("primary-ammo")` | Returns int. Note: pool name is `primary-ammo`, not `primary`. |
| `Paladin.Stance = "HoldFire"` | Force a unit into HoldFire (or "Ambush"/"FireAtWill") |
| `UserInterface.Select(actor)` | Replace local player's selection. WW3MOD addition. |

## Gotchas

These bit during development. Documenting so they don't bite again.

1. **Build cache lies**. `make` reports success without picking up edits to a single file occasionally. If a trace doesn't fire, `touch <file>.cs && make` to force rebuild.
2. **`AttackTurreted` overrides `CanAttack`** — short-circuits on `turretReady = FaceTarget()` *before* calling `base.CanAttack`. If you trace `AttackBase.CanAttack` and see no fires, your override is gating earlier.
3. **`Activity.IsCanceling` is false in `OnLastRun`**. The framework sets `State = Done` *before* calling OnLastRun, so the cancel flag is already cleared. To detect "ended because something replaced me", check `NextActivity is X` instead.
4. **Multi-window terminals**: with multiple Ghostty / iTerm2 windows, AXMain can lie about which one is active. Use `./tools/test/run-test.sh --position=left|right <test>` once and the choice is saved per-TTY at `~/.ww3mod-tests/position-prefs/<tty-key>` for future runs.
5. **Lua force-attack vs UI force-attack** are *not* always equivalent paths. `Paladin.Attack(t90, ..., forceAttack=false)` hard-codes `queued: true` (existing OpenRA API quirk); `Paladin.AttackGround(...)` defaults `queued: false` to mimic Ctrl+click replace.
6. **`Result.json` is sticky**. Always `rm -f $HOME/.ww3mod-tests/result.json` before a run, or wait for the runner to do it. Otherwise an old verdict can be misread.

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
| `tools/test/run-test.sh` | Single-test runner |
| `tools/test/run-batch.sh` | Batch runner |
| `tools/test/list-tests.sh` | Discovery |

## Existing tests

- `test-artillery-turret` — manual: does the Paladin's turret rotate before firing?
- `test-paladin-fires` — auto: Paladin's primary ammo drops within 12 s of force-engage on t90 (HoldFire). Demonstrates the green path.
- `test-arty-force-attack-during-setup` — auto, currently RED: force-attack-ground during setup-ticks. Layer 1 of the bug fixed (commit 51db91f7); Layer 2 (turret stalls mid-rotation) still open.
