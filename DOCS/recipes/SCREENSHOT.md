# SCREENSHOT — capture game state as PNGs for autonomous visual evaluation

**Trigger:** `SCREENSHOT <topic>` (e.g. `SCREENSHOT lobby tone`). Also fires on natural-language equivalents: "screenshot the lobby and tell me if X", "take a shot of the menu and check Y".

**Gives you:** an agent that can *see*. The game writes PNGs to disk; I read them with the multimodal `Read` tool and judge whether what's on screen matches expectations. Works in three modes:

1. **In an AUTOTEST scenario** — Lua calls `Test.Screenshot(label, note)` at named beats. Paths are emitted into the verdict JSON's `screenshots[]` array. I read the PNGs after the run.
2. **In the menu / lobby / arbitrary game state** — game is launched in "screenshot mode" with no `Launch.Map`. The user (or me) drives the UI manually; a tiny CLI sends "take a screenshot now" commands. PNGs land in a per-run dir with a `manifest.json`.
3. **Outside any test context** — the OS-level `Ctrl+P` hotkey still works exactly as it always did. This recipe doesn't touch that path.

**When *not* to use it:** anything observable via game state (`unit.IsDead`, `unit.AmmoCount`, `world.Players`, etc.) — query that directly, it's deterministic and cheap. Use screenshots for genuinely visual checks: UI tone/contrast, presence of effects/animations, formation shape, "did anything render at all". I'm reliable for coarse semantic checks, unreliable for pixel-perfect alignment or counting more than ~5 similar units.

---

## Mode 1 — In-test (Lua-driven, automatic verdict)

Add screenshots to any autotest scenario. Captures end up in the verdict JSON automatically.

```lua
-- Inside a test-<name>.lua WorldLoaded handler:
TestHarness.FocusBetween(Paladin, Target)
TestHarness.Screenshot("01-pre-attack",
    "expects: M109 facing east, T-90 visible, no muzzle flash yet")

Paladin.Attack(Target, true, false)

TestHarness.ScreenshotAfter(2, "02-firing",
    "expects: muzzle flash on M109, projectile or impact effects mid-flight")
```

After the run, the verdict JSON looks like:

```json
{
  "name": "...", "status": "pass", "notes": "...",
  "screenshots": [
    {"label": "01-pre-attack", "path": "/Users/.../001_01-pre-attack.png",
     "tick": 0, "note": "expects: ...", "captured_at": "..."},
    ...
  ]
}
```

The agent reads the `path` entries and judges each against the `note`. Failures surface as `⚠️` in the end-of-message block — they don't auto-fail the test (visual judgment is too noisy for hard gating).

### Lua API

| Call | Purpose |
|---|---|
| `Test.Screenshot(label, note?)` | Engine binding. Captures synchronously when `Test.Mode=true` (the PNG is on disk before the next Lua line runs). Returns the path. Returns nil if TestMode inactive. |
| `TestHarness.Screenshot(label, note?)` | Thin wrapper around `Test.Screenshot`. Same behavior. Prefer this for consistency with other `TestHarness.*` calls. |
| `TestHarness.ScreenshotAfter(seconds, label, note?)` | Sugar: schedules a screenshot N game-seconds from now via `Trigger.AfterDelay`. |

**Label sanitization.** Labels are lowercased; only `a-z 0-9 - _` survive; spaces become dashes; everything else is dropped. Filename pattern: `<NNN>_<sanitized-label>.png` where NNN is a zero-padded sequence number.

**Why captures are sync in TestMode.** The default `Renderer.SaveScreenshot` dispatches PNG encoding to a `ThreadPool` worker. If `Game.Exit()` runs before the worker finishes — and `Test.Pass` calls `Exit` shortly after the last capture — the worker is killed mid-flush and the file is lost. Sync save guarantees the PNG lands before the next line runs. Costs ~100–300 ms per shot at 2k+ resolutions; acceptable for tests.

---

## Mode 2 — External (menu / lobby / arbitrary state)

For screenshots outside an autotest scenario — main menu, server lobby, mid-match without scripting, etc.

```bash
# Terminal 1: launch the game in screenshot mode (visible, foreground).
./tools/autotest/start-screenshot-mode.sh

# Terminal 2 (after the menu loads): trigger a capture.
./tools/autotest/screenshot.sh lobby-system-chat-tone --wait
# Prints: /Users/.../manual_<run-id>/001_lobby-system-chat-tone.png
```

With `--wait`, the CLI polls `manifest.json` until the new entry appears and prints the resulting PNG path on stdout — pipe directly into a `Read` call.

### How it works

`start-screenshot-mode.sh` launches `Test.Mode=true Test.ScreenshotCmdFile=<path>` with no `Launch.Map`. The engine's `LogicTick` polls the command file each tick (~40 ms) when this arg is set. `screenshot.sh` writes a `screenshot <label>` line; the engine reads, deletes the file, captures synchronously, appends to `manifest.json`. Zero overhead when `Test.Mode=false`.

### Manifest format

`~/.ww3mod-tests/screenshots/manual_<run-id>/manifest.json`:

```json
{
  "output_dir": "...",
  "updated_at": "2026-05-12T...",
  "screenshots": [
    {"label": "...", "path": "...", "tick": -1, "note": "phase 2 external trigger", "captured_at": "..."}
  ]
}
```

`tick: -1` is the sentinel for "no World loaded" (the game was at the menu). In-match captures carry the real `WorldTick`.

---

## Evaluation contract

How I decide whether a screenshot shows what it should:

1. **Declarative (preferred for regressions).** The test/CLI passes a `note` like `"expects: muzzle flash visible; T-90 in frame"`. I read the PNG, judge each clause true/false, write observations into the end-of-message block. Failures = `⚠️` lines, not auto-fail.
2. **Freeform (preferred for menu/lobby work).** No expectations — I just describe what I see ("Lobby chat box bottom-left; system message in light-grey; no settings panel open"). User reacts.

**What I'm good at:** presence/absence of UI elements, obvious colour wrongness (pure yellow vs muted gold), animations visibly playing (fire, smoke, muzzle flash), formations bunched vs spread, "did the build break visually".

**What I'm not good at:** pixel-perfect alignment, exact text in cluttered HUDs, counting > 5 similar units, small font readouts at default zoom, frame-exact timing. Use state queries (`unit.IsFiring`, `Test.GetActiveMissileCount`) for those.

---

## Practical notes

- **One screenshot per test by default.** Multi-shot is opt-in for tests where intermediate state matters. The agent has to `Read` every PNG, so 30-shot tests get expensive in context.
- **Reading PNGs costs tokens.** ~50–200 KB per shot at typical resolutions; larger at 2k+ desktop res. Budget accordingly.
- **Screenshots survive between sessions** under `~/.ww3mod-tests/screenshots/`. `run-test.sh` cleans up runs older than 7 days at the start of each test.
- **`--minimized` autotest runs may produce blank PNGs.** macOS doesn't redraw minimized windows. Use `--background` (default) or `--visible` if screenshots matter.
- **Window resolution varies by machine.** Acceptable for semantic evaluation, problematic for any future pixel-diff regression. The current pipeline does *not* support golden-image diffing — that's a deliberate non-goal (see the plan doc).

---

## Integration points

| File | Role |
|---|---|
| `engine/OpenRA.Game/TestModeScreenshots.cs` | Per-run dir, sequence counter, captured list, manifest writer, command-file poller |
| `engine/OpenRA.Game/TestMode.cs` | `ScreenshotDir`, `ScreenshotCmdFile` launch args; serializes `screenshots[]` into the verdict JSON |
| `engine/OpenRA.Game/Game.cs` | `TakeScreenshot(string explicitPath)` overload; `LogicTick` calls `PollCommands` |
| `engine/OpenRA.Game/Renderer.cs` | `SaveScreenshot` sync when `TestMode.IsActive`, async (ThreadPool) otherwise |
| `engine/OpenRA.Mods.Common/Scripting/Global/TestGlobal.cs` | `Test.Screenshot` Lua binding; `ExitWhenCapturesFlushed` polling loop |
| `mods/ww3mod/scripts/test-helpers.lua` | `TestHarness.Screenshot` and `TestHarness.ScreenshotAfter` wrappers |
| `tools/autotest/run-test.sh` | Passes `Test.ScreenshotDir=...`; lists captured PNGs post-run |
| `tools/autotest/screenshot.sh` | External CLI — write a command, optionally `--wait` for the path |
| `tools/autotest/start-screenshot-mode.sh` | Launches the game with no `Launch.Map`, watcher enabled |

---

## Existing scenarios using this

- `test-screenshot-smoke` — proves the pipeline. Three captures at named beats, pass.

## Future (Phase 3 — sketched, not built)

Programmatic UI driving — `click <widget-id>`, `text <field-id> <value>` commands added to the same command-file watcher. Would let the agent stage a known lobby state ("3 slots, 1 human, 2 bots, map = River Zeta") and screenshot it. Tracker: re-plan after Phase 1/2 have been used for real.
