# SCREENSHOT — capture game state as PNGs for autonomous visual evaluation

**Trigger:** `SCREENSHOT <topic>` (e.g. `SCREENSHOT lobby tone`). Also fires on natural-language equivalents: "screenshot the lobby and tell me if X", "take a shot of the menu and check Y".

**Apply automatically (no trigger required) when** the work has a visual component. Quick checklist:

1. Is the change/check **visual** — UI, color, palette, sprite, animation, formation shape, layout, lobby/menu/HUD work?
2. Would a screenshot at the right moment **let me verify** the change is visible — or catch unrelated visual regressions a state-query test would miss?
3. Is the cost reasonable — **one shot at a critical beat**, not 10 shots spamming the verdict?

Yes / yes / yes → add `TestHarness.Screenshot(label, note)` to the autotest scenario, or take an external shot when iterating on lobby/menu. **Don't wait for the user to say SCREENSHOT.** Same auto-apply stance as AUTOTEST itself.

**Concrete trigger patterns** (any of these → screenshot without asking):
- Editing a `*.yaml` palette, color, sprite, or chrome file
- Touching `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/` or `chrome/lobby.yaml`
- Bug labeled "looks wrong / visual / palette / animation"
- Fixing anything in `engine/OpenRA.Mods.Common/Traits/Render/` (sprite/animation traits)
- User says "show me", "does this look right", "what's the lobby look like now"

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
| `Test.Screenshot(label, note?)` | Engine binding. **Arms** a capture — it does not sample pixels. Returns the planned path, or nil if TestMode inactive. |
| `TestHarness.Screenshot(label, note?)` | Thin wrapper around `Test.Screenshot`. Same behavior. Prefer this for consistency with other `TestHarness.*` calls. |
| `TestHarness.ScreenshotAfter(seconds, label, note?)` | Sugar: schedules a screenshot N game-seconds from now via `Trigger.AfterDelay`. |

**Label sanitization.** Labels are lowercased; only `a-z 0-9 - _` survive; spaces become dashes; everything else is dropped. Filename pattern: `<NNN>_<sanitized-label>.png` where NNN is a zero-padded sequence number.

**THE CAPTURE IS ONE FRAME LATE — PUT A DELAY BETWEEN A SHOT AND THE NEXT STATE CHANGE.** `Test.Screenshot` sets `Game.takeScreenshot` and returns. The pixels are read at the end of the **next** `RenderTick`, after `Ui.Draw()` has redrawn the HUD from whatever the state is *by then* (`Game.cs:926-930`); the binding's own `[Desc]` says "Capture is async". So this is a trap:

```lua
TestHarness.Screenshot("01-full", "expects: 10 passengers")
for _ = 1, 7 do Transport.UnloadPassenger() end   -- BUG: lands before the pixels are read
```

The shot passes its `PassengerCount == 10` assertion and then photographs **3** passengers — the state you were about to move to, under the label of the state you asserted. This happened on 2026-08-17 in `test-cargo-panel-full`; the two shots differed by 166 pixels out of 302,768 and the mislabelled one was only caught by diffing them. Give every capture its own `Trigger.AfterDelay` before anything touches the world, including before `Test.Pass`.

Two corollaries: a capture fired in `WorldLoaded` can land **blank**, because no frame has been rendered yet; and a run that exits promptly after a shot can lose it, which is why `Test.Pass` goes through `ExitWhenCapturesFlushed`.

**AN AUTOTEST CAPTURE HAS NO RENDER PLAYER, SO IT IS NOT A PICTURE OF WHAT A PLAYER SEES.** `TestModeLogic.cs:31` sets `world.RenderPlayer = null` for every autotest with a real player slot — deliberately, so the window shows the whole map. Two things follow, and both make a capture *overstate* what is on screen:

- **Every `ValidRelationships` gate is off.** `WithDecorationBase.ShouldRender` applies its relationship filter only inside `if (self.World.RenderPlayer != null)` (`WithDecorationBase.cs:101-105`), so enemy units happily draw decorations declared `ValidRelationships: Ally` — which is the **default** (`:44`), i.e. most pips in the mod. Marks a real player would never see appear on every unit on the map.
- **No fog or shroud is applied.** `World.FogObscures`/`ShroudObscures` all short-circuit to `false` on a null render player (`World.cs:109-115`).

So a capture cannot validate any indicator whose correctness depends on *who is looking*, and it can make a leak-prevention rule look broken when it is fine. Confirming it costs nothing and no extra run: sample mean terrain brightness at several distances from your units in the PNG — under a real render player, ground outside vision is visibly darker; in the harness it is uniform. **Uniform brightness is the tell.** Note also that when a decoration falls back to `self.Owner` for its viewer, the fallback is what the capture exercises, never the render-player path.

**What *is* synchronous is the PNG write.** `Renderer.SaveScreenshot` normally dispatches encoding to a `ThreadPool` worker, which `Game.Exit()` can kill mid-flush; under `TestMode.IsActive` it writes inline instead, so the file lands before teardown. Costs ~100–300 ms per shot at 2k+ resolutions. Sync *write*, deferred *sample* — do not read the first as the second.

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

**NEVER FIRE AN EXTERNAL CAPTURE OFF A LOAD-COMPLETION LOG LINE.** World *setup* is logged well before that world's first render pass, so a shot triggered the instant `ApplyScenario: applying '<map>' …` appears in `debug.log` comes back with the menu widgets drawn over a **completely black** background — indistinguishable from a map that failed to load, and the same one-frame-late sampling as Mode 1. This happened on 2026-08-16 while verifying a shellmap fix and was nearly reported as a regression from a correct change; a capture six seconds later showed the map rendering normally. Wait a beat, or capture twice and compare.

**The tell for a blank frame is file size, not the image.** An almost-flat PNG compresses to nothing: 59 KB for the black frame vs 1.6 MB for the real one. **Check the byte size and re-shoot before believing a blank capture** — it costs no context and no `Read` call. This is the one shape in this pipeline that produces a false *positive* (a regression report against working code) rather than a false green.

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

## Mode 4 — Direct lobby capture (no human in the loop)

For iterating on the skirmish lobby YAML — palette, layout, dropdowns, etc. — without clicking through Singleplayer → Skirmish each time. The game launches, lands straight in the lobby with a real map loaded, snaps one PNG, and exits cleanly.

```bash
./tools/autotest/screenshot-lobby.sh <label>
# Prints: /Users/.../manual_lobby_<run-id>/001_<label>.png
```

Round trip on a warm cache is ~10–15s; on a cold launch closer to 20s. The captured frame shows the same view a human gets after picking Skirmish: map preview, player rows, options grid, chat, and the green Start Game button.

### Options

| Flag | Meaning |
|---|---|
| `--map=<id>` | Override the seed map. Resolves against MapPreview title (`"River Zeta WW3"`), package folder (`river-zeta-ww3`), or Uid. Default: `river-zeta-ww3`. |
| `--tab=<name>` | Land on a non-default lobby tab. `match` (default), `advanced`, `music`. Wired through to `Test.OpenLobbyTab`. |
| `--no-quit` | Leave the game running after the capture. Useful while iterating: fire follow-up shots with `tools/autotest/screenshot.sh <next-label> --wait` against the same run dir. |
| `--timeout=<sec>` | Per-phase timeout (lobby-ready wait, manifest wait, quit wait). Default: 30. |

### How it works

`screenshot-lobby.sh` launches with three lobby-aware test args on top of the existing Mode 2 plumbing:

- `Test.OpenSkirmishLobby=true` — `MainMenuLogic` calls `StartSkirmishGame` straight after the menu loads (no Singleplayer click required).
- `Test.LaunchLobbyMap=<id>` — `MainMenuLogic.StartSkirmishGame` seeds the lobby with this map instead of whatever the user happens to have last-played. Resolves against `MapPreview.Title`, the package folder name, or the raw Uid.
- `Test.LobbyReadyFile=<path>` — `LobbyLogic.Tick` touches this file once `MapIsPlayable` becomes true. The wrapper polls for the marker instead of blind-sleeping, so slow machines don't trip the screenshot before the map preview has resolved.

Capture and exit go through the same cmd-file watcher Mode 2 uses; `quit` is a new verb that calls `Game.Exit` via `RunAfterTick`, so the active `LogicTick` unwinds cleanly before teardown.

### What got added

| Path | Role |
|---|---|
| `engine/OpenRA.Game/TestMode.cs` | `OpenSkirmishLobby`, `LaunchLobbyMap`, `LobbyReadyFile`, `OpenLobbyTab` launch-arg properties |
| `engine/OpenRA.Game/TestModeScreenshots.cs` | `quit` command handler in `PollCommands` |
| `engine/OpenRA.Mods.Common/Widgets/Logic/MainMenuLogic.cs` | Auto-clicks through to skirmish; `ResolveLobbyMapId` lookup |
| `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyLogic.cs` | Writes the `LobbyReadyFile` marker once per lobby load |
| `tools/autotest/screenshot-lobby.sh` | The wrapper script |

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
- **Reading PNGs costs context — *pixels* drive the cost, not file size.** Claude vision is roughly `width × height ÷ 750` tokens. Rough budget per shot:

  | Resolution | Tokens | Use case |
  |---|---|---|
  | 2560 × 1440 (desktop fullscreen) | ~4,900 | overkill — only if you need pixel detail |
  | 1920 × 1080 | ~2,700 | overkill for most checks |
  | 1280 × 720 | ~1,230 | **sweet spot for semantic checks** |
  | 800 × 450 | ~480 | fine for "did it render at all" |

- **Downsize at Read time, not save time** (recommended pattern). PNGs land on disk at the game's window resolution — for menu-mode that's the full desktop, 2560×1440 = ~5k tokens each. Before `Read`-ing, shrink to ~1280px wide with one of:

  ```bash
  # macOS (native, no install)
  sips -Z 1280 "$SRC" --out /tmp/preview.png

  # ImageMagick if installed
  magick "$SRC" -resize 1280x /tmp/preview.png
  ```

  Then `Read /tmp/preview.png`. ~4× context savings on the common case. Skip the downsize only when you actually need pixel detail — UI alignment, small font legibility, etc. Even then, prefer state queries; the agent isn't reliable at pixel work.

- **Screenshots survive between sessions** under `~/.ww3mod-tests/screenshots/`. `run-test.sh` cleans up runs older than 7 days at the start of each test. Manual-mode runs (`manual_*`) are subject to the same cleanup window.
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
| `tools/autotest/screenshot-lobby.sh` | Mode 4 — one-shot lobby capture, launches → lobby-ready → screenshot → quit |

---

## Existing scenarios using this

- `test-screenshot-smoke` — proves the pipeline. Three captures at named beats, pass.

## Future (Phase 3 — sketched, not built)

Programmatic UI driving — `click <widget-id>`, `text <field-id> <value>` commands added to the same command-file watcher. Would let the agent stage a known lobby state ("3 slots, 1 human, 2 bots, map = River Zeta") and screenshot it. Tracker: re-plan after Phase 1/2 have been used for real.
