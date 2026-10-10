# SCREENSHOT — capture game state as PNGs for autonomous visual evaluation

**Trigger:** `SCREENSHOT <topic>` (e.g. `SCREENSHOT lobby tone`), or natural-language equivalents: "screenshot the lobby and tell me if X", "take a shot of the menu and check Y".

**Apply automatically (no trigger required) when** the work has a visual component:

1. Is the change/check **visual** — UI, colour, palette, sprite, animation, formation shape, layout, lobby/menu/HUD?
2. Would a screenshot at the right moment **verify** the change is visible, or catch a visual regression a state query would miss?
3. Is the cost reasonable — **one shot at a critical beat**, not ten?

Yes / yes / yes → plan `TestHarness.Screenshot(label, note)` into the scenario, or an external shot when iterating on lobby/menu. **Don't wait for the user to say SCREENSHOT.** Concrete patterns that mean "plan a capture":
- Editing a palette, colour, sprite or chrome `*.yaml`
- Touching `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/` or `chrome/lobby.yaml`
- A bug labelled "looks wrong / visual / palette / animation"
- Anything in `engine/OpenRA.Mods.Common/Traits/Render/`
- The user says "show me", "does this look right", "what's the lobby look like now"

**Who captures.** Every capture starts the game, so it is a launch, and launches follow CLAUDE.md §"Who runs what": a worker dispatched by a manager writes the capture into the scenario (or names the script and label) together with what the frame must show, and hands it up; it does not run the capture scripts itself. An agent working directly with the user may run the capture.

**Gives you:** an agent that can *see*. The game writes PNGs; the multimodal `Read` tool judges whether the frame matches expectations. Modes:

1. **In an autotest scenario** — Lua calls `Test.Screenshot(label, note)` at named beats; paths land in the verdict JSON's `screenshots[]`.
2. **External (menu / lobby / arbitrary state)** — the game is launched in screenshot mode with no `Launch.Map`, and a CLI writes "take a screenshot now" commands to a file the engine polls.
3. **Direct lobby capture** — one script launches straight into the skirmish lobby, snaps a PNG and exits.

The ordinary `Ctrl+P` hotkey still works outside any test context; this recipe does not touch it.

**When *not* to use it:** anything observable via game state — `unit.IsDead`, `unit.AmmoCount("primary-ammo")`, `Player.GetPlayers(filter)`, `Test.GetActiveMissileCount()` — query that directly; it is deterministic and cheap. Screenshots are for genuinely visual checks: tone/contrast, effects and animations present, formation shape, "did anything render at all". Reliable for coarse semantic checks; unreliable for pixel-perfect alignment or counting more than ~5 similar units.

**`--hidden` and `--minimized` runs write NO PNG.** Both suspend rendering (`OPENRA_WINDOW_HIDDEN` sets `IsSuspended` at window creation; a minimize event sets it from `Sdl2Input`), and `Game.TakeScreenshot` only sets a flag that the render loop consumes — the loop never runs, so no file is written. The capture is still recorded in `manifest.json` and `result.json`. **Run any scenario whose answer is a frame with `--background` (default) or `--visible`, and treat a listed screenshot as a claim until `ls` shows the file.**

---

## Mode 1 — In-test (Lua-driven)

```lua
-- Inside a test-<name>.lua WorldLoaded handler:
TestHarness.FocusBetween(Paladin, Target)
TestHarness.Screenshot("01-pre-attack",
    "expects: M109 facing east, T-90 visible, no muzzle flash yet")

Paladin.Attack(Target, true, false)

TestHarness.ScreenshotAfter(2, "02-firing",
    "expects: muzzle flash on M109, projectile or impact effects mid-flight")
```

The verdict JSON then carries:

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

Read each `path` and judge it against its `note`. Visual failures are reported as `⚠️` lines; they do not auto-fail the test (visual judgment is too noisy for hard gating).

### Lua API

| Call | Purpose |
|---|---|
| `Test.Screenshot(label, note?)` | Engine binding. **Arms** a capture — it does not sample pixels. Returns the planned path, or nil if TestMode is inactive. |
| `TestHarness.Screenshot(label, note?)` | Thin wrapper; prefer it for consistency. |
| `TestHarness.ScreenshotAfter(seconds, label, note?)` | Schedules a screenshot N seconds from now via `Trigger.AfterDelay`. |

**Label sanitisation.** Labels are lowercased; only `a-z 0-9 - _` survive; spaces become dashes. Filename: `<NNN>_<label>.png`, NNN a zero-padded sequence number.

**THE CAPTURE IS ONE FRAME LATE — PUT A DELAY BETWEEN A SHOT AND THE NEXT STATE CHANGE.** `Test.Screenshot` sets a flag and returns; the pixels are read at the end of the **next** `Game.RenderTick`, after `Ui.Draw()` has redrawn the HUD from whatever the state is by then (the binding's `[Desc]` says "Capture is async"). So this is a trap:

```lua
TestHarness.Screenshot("01-full", "expects: 10 passengers")
for _ = 1, 7 do Transport.UnloadPassenger() end   -- BUG: lands before the pixels are read
```

The shot passes its `PassengerCount == 10` assertion and photographs **3** passengers under the label of 10; the two frames differed by 166 pixels out of ~300k and the mislabel was caught only by diffing them. Give every capture its own `Trigger.AfterDelay` before anything touches the world, including before `Test.Pass`. Corollaries: a capture fired in `WorldLoaded` can land **blank** (no frame rendered yet), and a run that exits promptly after a shot could lose it, which is why `Test.Pass` goes through `ExitWhenCapturesFlushed`.

**What *is* synchronous is the PNG write.** `Renderer.SaveScreenshot` normally encodes on a `ThreadPool` worker, which `Game.Exit()` can kill mid-flush; under `TestMode.IsActive` it writes inline, ~100–300 ms per shot at 2k+ resolutions. Sync *write*, deferred *sample*.

**AN AUTOTEST CAPTURE HAS NO RENDER PLAYER, SO IT IS NOT A PICTURE OF WHAT A PLAYER SEES.** `TestModeLogic` sets `world.RenderPlayer = null` for every autotest with a real player slot, so the window shows the whole map — unless `Test.KeepRenderPlayer=true` is passed (matched case-insensitively against `"true"`; `1` does not work), which is how a capture of anything fog- or relationship-dependent is made. With the default, a capture *overstates* what is on screen:

- **Every `ValidRelationships` gate is off.** `WithDecorationBase.ShouldRender` applies its relationship filter only inside `if (self.World.RenderPlayer != null)`, so enemy units draw decorations declared `ValidRelationships: Ally` — the field's default, i.e. most pips in the mod.
- **No fog or shroud.** `World.FogObscures` / `ShroudObscures` return false on a null render player.

So such a capture cannot validate an indicator whose correctness depends on *who is looking*, and can make a leak-prevention rule look broken. Free check: sample mean terrain brightness at several distances from your units — under a real render player ground outside vision is darker; in the harness it is uniform. **Uniform brightness is the tell.**

---

## Mode 2 — External (menu / lobby / arbitrary state)

```bash
# Terminal 1: launch the game in screenshot mode (visible, foreground).
./tools/autotest/start-screenshot-mode.sh

# Terminal 2 (after the menu loads): trigger a capture.
./tools/autotest/screenshot.sh lobby-system-chat-tone --wait
# Prints: /Users/.../manual_<run-id>/001_lobby-system-chat-tone.png
```

`start-screenshot-mode.sh` launches `Test.Mode=true Test.ScreenshotCmdFile=<path>` with no `Launch.Map`. `TestModeScreenshots.PollCommands` runs from the logic tick — every 40 ms at the menu (`Ui.Timestep`), every world tick (60 ms at default speed) in a match — reads the command file, deletes it, and dispatches each line. A `screenshot <label>` line goes through the same `Game.TakeScreenshot` flag as Mode 1, so **the pixels are one frame late here too**, and the manifest entry is written before the PNG exists.

**`screenshot.sh --wait` waits for the manifest entry count to grow and prints the newest entry's path** — whatever its label — with a fixed 10 s deadline (exit 2 on timeout, 1 on error). Because the manifest is written first, the printed path can name a file that is not on disk yet: `ls` it (or wait a beat) before `Read`.

**NEVER FIRE AN EXTERNAL CAPTURE OFF A LOAD-COMPLETION LOG LINE.** World setup is logged before that world's first render pass, so a shot fired the instant `ApplyScenario: applying '<map>' …` appears comes back as menu widgets over a **completely black** background — indistinguishable from a map that failed to load. Wait a beat, or capture twice and compare.

**The tell for a blank frame is file size.** A near-flat PNG compresses to almost nothing (tens of KB against megabytes for a real frame). Check the byte size and re-shoot before believing a blank capture — it costs no `Read`. This is the one shape here that produces a false *positive* (a regression report against working code).

### Manifest format

`~/.ww3mod-tests/screenshots/manual_<run-id>/manifest.json`:

```json
{
  "output_dir": "...",
  "updated_at": "...",
  "screenshots": [
    {"label": "...", "path": "...", "tick": -1, "note": "phase 2 external trigger", "captured_at": "..."}
  ]
}
```

**External captures always record `tick: -1`**, at the menu and in a match alike. Only a Lua `Test.Screenshot` records the real world tick.

### Driving the UI from the command file

Besides `screenshot <label>`, the command file accepts (`TestModeScreenshots.PollCommands`):

| verb | effect | miss / log line |
|---|---|---|
| `click <widget-id>` | Runs the first widget whose `IsVisible()` is true through its own `OnClick` — the same handler a real click runs. | `[TestMode] external click: <id> → dispatched` / `→ NO SUCH VISIBLE WIDGET` |
| `type <widget-id> [text]` | Sets a text field and fires its `OnTextEdited` (where consumers do their filtering), so a list can be narrowed into frame. Empty text clears the field. | `→ typed "<text>"` / `→ NO SUCH VISIBLE TEXT FIELD` |
| `hover <actor-name>` | Arms a production-icon hover; send the screenshot on a later line so the tooltip has a frame to build in. | `[TestMode] external hover: <actor>` |
| `zone-paint` / `zone-erase <x>,<y>[,<size>]` | Arms one map-editor zone stroke, replayed by `EditorZoneBrush` through its own `PaintZoneEditorAction` — undoable, in the history, the same operation as a dragged stroke. Inert unless the zone brush is current (`Test.EditorTool=Zones`). | a stroke that hits nothing logs `zone stroke applied: … CHANGED NOTHING` |
| `quit` | `Game.Exit` via `RunAfterTick`, so the logic tick unwinds first. | `[TestMode] external quit` |

Worked drivers: `tools/autotest/watch-replay.sh`, `tools/autotest/screenshot-infopanel.sh`, `tools/autotest/screenshot-hotkeys.sh`.

**A miss is logged, never thrown, so a driver must grep for it.** A driver that does not will photograph whatever was on screen and call it a capture — one run returned two byte-identical frames of an error dialog as two captures. **Treat byte-identical captures as NO-RESULT.** Three details:

- **`NO SUCH VISIBLE WIDGET` is two failures in one string**: `ClickWidget` returns false both when no visible widget has the id and when the visible widget has no public `OnClick` field. A widget plainly on screen with no click handler reports as absent.
- **`click` matches on `IsVisible()`, not the raw `Visible` field.** They disagree whenever logic assigns the delegate at runtime (the info panel's `TAB_CONTAINER_N` are authored `Visible: False` and switched on by `GameInfoLogic`).
- **A click sent on a clock can land before the world exists.** Order the miss line against the world-load lines in `debug.log` before blaming the id, and retry against the `→ dispatched` line rather than "the cmd file was consumed", which happens either way.

**Not drivable:** key events, scrolling (`ScrollPanelWidget` exposes no clickable child), and dropdown items — `ScrollItemWidget.Setup` runs inside `ShowDropDown`, so no item widget exists until a human opens the dropdown. Launch-arg hooks cover the dropdowns that matter: `Test.EditorTool`, `Test.OpenIngameInfoPanel`.

---

## Mode 3 — Direct lobby capture (no human in the loop)

For iterating on the skirmish lobby YAML without clicking through Singleplayer → Skirmish each time. The game launches, lands in the lobby with a real map loaded, snaps one PNG, and exits.

```bash
./tools/autotest/screenshot-lobby.sh <label>
# Prints: /Users/.../manual_lobby_<run-id>/001_<label>.png
```

| Flag | Meaning |
|---|---|
| `--map=<id>` | Seed map. Resolves against MapPreview title, package folder, or Uid. Default `river-zeta-ww3`. |
| `--tab=<name>` | Land on `match` (default), `advanced` or `music` (`Test.OpenLobbyTab`). |
| `--set-options=<id>=<val>[,…]` | Move lobby options off their defaults once the lobby loads (`Test.SetLobbyOptions`) — needed to capture the ACTIVE CHANGES strip. |
| `--hover=<id>` | Hover a lobby option's checkbox so the capture shows its tooltip (`Test.HoverLobbyOption`). |
| `--window=<WxH>` | Capture at a fixed window size instead of the desktop resolution; the options panel is height-proportional, so the fold only shows on a small screen. |
| `--no-quit` | Leave the game running; fire follow-up shots with `screenshot.sh <label> --wait` against the same run. |
| `--timeout=<sec>` | Per-phase timeout (lobby-ready, manifest, quit). Default 30. |

It adds three launch args to the Mode 2 plumbing: `Test.OpenSkirmishLobby=true` (`MainMenuLogic` calls `StartSkirmishGame` once the menu loads), `Test.LaunchLobbyMap=<id>` (resolved by `MainMenuLogic.ResolveLobbyMapId`; a miss falls back to the normal initial map), and `Test.LobbyReadyFile=<path>` (`LobbyLogic` touches it once `MapIsPlayable`, so the wrapper polls a marker instead of sleeping). Capture and `quit` go through the command file.

### Opening the MAP EDITOR without a human

`Test.OpenEditorMap=<map directory | title | uid>` is the editor sibling of `Test.OpenSkirmishLobby`: `MainMenuLogic` resolves it through the same `ResolveLobbyMapId` and calls `Game.LoadEditor`. `Test.EditorTool=<name>` selects an entry of the Tools dropdown **and flips the right-hand panel to the Tools tab** — the panel is six containers gated by `MapEditorTabsLogic`, defaulting to `Tiles`, and selecting a tool says nothing about which tab is showing. **Unlike the lobby hook, a miss does NOT fall back**: an editor driver asking for one map and silently getting another would be a capture of the wrong thing. Misses log `[TestMode] OpenEditorMap NO SUCH MAP: '<id>'` and `[TestMode] editor tool NO SUCH TOOL: '<name>'`.

Worked driver: `tools/autotest/screenshot-editor-zones.sh` — two frames of the Zones panel from one launch (band intact, then cut). It waits on `zone panel shown: components=2` (then `components=1`), and still sleeps before capturing, because the editor's chrome is built before that world's first render pass. **The file is tracked mode `100644`; run it as `sh tools/autotest/screenshot-editor-zones.sh`** — the `./` form exits 126.

---

## A capture driver's markers must be about the thing IN THE PHOTOGRAPH

The most expensive shape in this pipeline, because every check passes and every check is true. A driver waited for `editor zone selected:`, got it, and returned two distinct, 120 KB+ frames of the **Tiles** tab under a green PASS while claiming the Zones panel. **Construction, selection, state changes and command consumption all happen whether or not the widget is visible**, so every marker of that kind is evidence about the engine and none is evidence about the frame.

**The fix that generalises: log from inside the widget's own `GetText` delegate.** For a label nothing resizes, `LabelWidget.Draw` is the delegate's only caller (`LabelWidget.IncreaseHeightToFitCurrentText` also calls it — check it is unused on your label), and `Widget.DrawOuter` returns early on `!IsVisible()` — so a line written there **cannot exist unless that label was rendered, with that text, in a real frame.** Emit a machine-readable field beside the text (`components=2`) so the driver greps a number rather than a Fluent string a reword would move. `MapZonesLogic.LoggedSplitText` is the worked instance.

**Grade a driver's two claims separately.** That same run did verify the data path — `map.yaml` → `Map.Zones` → overlay → scripted stroke → undo history — because those were visible in the frames. A driver can be right about everything it photographed and wrong about what it says it photographed.

---

## Evaluation contract

1. **Declarative (preferred for regressions).** The capture carries a `note` like `"expects: muzzle flash visible; T-90 in frame"`. Judge each clause true/false; failures are `⚠️` lines, not auto-fail.
2. **Freeform (preferred for menu/lobby work).** No expectations — describe what is on screen; the user reacts.

**Good at:** presence/absence of UI elements, obvious colour wrongness, animations visibly playing, formations bunched vs spread, "did the build break visually". **Not good at:** pixel-perfect alignment, exact text in cluttered HUDs, counting > 5 similar units, small fonts at default zoom, frame-exact timing — use state queries for those.

## Practical notes

- **One screenshot per test by default.** Every PNG has to be `Read`, so multi-shot is opt-in.
- **Reading PNGs costs context by pixels, not file size** — roughly `width × height ÷ 750` tokens:

  | Resolution | Tokens (by that formula) | Use case |
  |---|---|---|
  | 2560 × 1440 | ~4,900 | only if you need pixel detail |
  | 1920 × 1080 | ~2,700 | overkill for most checks |
  | 1280 × 720 | ~1,230 | **sweet spot for semantic checks** |
  | 800 × 450 | ~480 | "did it render at all" |

  The API downscales images whose long edge exceeds roughly 1568 px, so the top two rows probably overstate the real cost; that has not been measured here.
- **Downsize at Read time, not save time.** Menu-mode PNGs land at full desktop resolution. Shrink to ~1280 px wide first — `sips -Z 1280 "$SRC" --out /tmp/preview.png` (macOS) or `magick "$SRC" -resize 1280x /tmp/preview.png` — and skip it only when you need pixel detail.
- **Screenshots live under `~/.ww3mod-tests/screenshots/`**; `run-test.sh` deletes run dirs older than 7 days at the start of each run, `manual_*` included.
- **Window resolution varies by machine** — fine for semantic evaluation, a problem for pixel diffs. Golden-image diffing is a deliberate non-goal (`WORKSPACE/plans/260512_screenshot_evaluation.md`). A capture making a claim about layout must pin the size (`run-test.sh --size WxH`, `screenshot-lobby.sh --window=WxH`).

---

## Measuring a capture instead of describing it

Each error below yields a **plausible number rather than an obvious failure**.

### Sample at DEVICE pixels, not logical ones

`run-test.sh --size 1280x800` yields a 2560x1600 PNG on a 2x display, and Mode 2/3 captures land at the full desktop resolution. **The tell is a set of samples that all agree:** distinct predicted bands coming back identical is an instrument fault before it is a finding. A capture may also be at a non-integer display scale: `Camera.Zoom = 3` against `TileSize: 24,24` predicts 72 px per cell, and one frame's straight edges sat on a 108-px lattice (a 150% scale). **Derive the cell pitch from the frame before giving a feature a size in cells** — "hard rectangles several cells across" turned out to be one-cell decals magnified 4.5x.

### Linearise sRGB before comparing brightness

Raw byte values are gamma-encoded, so ratios on them read high and flatter any darkening change. Convert to linear light first.

### Count an EXACT colour, and expect ~60% of the sheet's opaque pixels

For "is this element highlighted / drawn at all", decode to raw RGBA and **count pixels exactly equal to the target colour**, bucketed by each element's derived rect (e.g. `COMMAND_BAR(14,760) + button.X + icon(5,1)`, 24x24, doubled for a 2x capture). Antialiasing blends everything else, so any tolerance turns the count into an opinion. Only the fully-opaque core survives as the exact colour, so expect about **60% of the sprite's opaque pixel count**; a count near 100% means the sprite is drawn without alpha blending. **Take the free check**: two elements drawing the same glyph must return the same count, which proves the rect mapping and the determinism of the render.

### A mockup that cannot express the failure always exonerates

Before using an offline render as evidence, check it has the degrees of freedom to show the defect. A contact sheet that modelled water as a half-plane drew a straight edge whatever the code did. **A simulation structurally incapable of the failure mode is not a control; it is a guaranteed pass.**

### Difference a control frame per cell before theorising

With a before and after at the same camera and zoom, difference them per cell first. A near-uniform luminance step with outliers only where something specific changed separates "the change did this" from "the change made this visible" (tileset `PickAny` variation revealed by darkening, not produced by it). A matched control frame costs one `Test.Screenshot`; `demo-highyield-nuke` stages one deliberately.

### A falloff as wide as the feature it falls off from erases the feature

**State the width of the smallest instance of the feature before choosing a falloff radius.** A 2-cell shore fade designed against open coastline was deployed against 2–3 cell rivers and 4-cell fords, holding the whole crossing below full strength. Check the metric too: Chebyshev iso-contours are axis-aligned squares, so a ramp in that metric around a bend unions into a rectangle with corners far from what it measured from.

---

## Integration points

| File | Role |
|---|---|
| `engine/OpenRA.Game/TestModeScreenshots.cs` | Per-run dir, sequence counter, captured list, manifest writer, command-file poller and verbs |
| `engine/OpenRA.Game/TestMode.cs` | `ScreenshotDir`, `ScreenshotCmdFile`, `KeepRenderPlayer` and lobby/editor launch args; serialises `screenshots[]` into the verdict |
| `engine/OpenRA.Game/Game.cs` | `TakeScreenshot(string explicitPath)`; `RenderTick` consumes the flag; the logic tick calls `PollCommands` |
| `engine/OpenRA.Game/Renderer.cs` | `SaveScreenshot`: inline under `TestMode.IsActive`, `ThreadPool` otherwise |
| `engine/OpenRA.Platforms.Default/Sdl2PlatformWindow.cs`, `Sdl2Input.cs` | Hidden/minimized windows set `IsSuspended`, which skips rendering |
| `engine/OpenRA.Mods.Common/Scripting/Global/TestGlobal.cs` | `Test.Screenshot`; `ExitWhenCapturesFlushed` |
| `mods/ww3mod/scripts/test-helpers.lua` | `TestHarness.Screenshot`, `TestHarness.ScreenshotAfter` |
| `tools/autotest/run-test.sh` | Passes `Test.ScreenshotDir=…`; lists captured PNGs post-run |
| `tools/autotest/screenshot.sh` | External CLI — write a command, optionally `--wait` for the path |
| `tools/autotest/start-screenshot-mode.sh` | Launches with no `Launch.Map`, watcher enabled |
| `tools/autotest/screenshot-lobby.sh` | Mode 3 one-shot lobby capture |
| `tools/autotest/screenshot-editor-zones.sh`, `screenshot-hotkeys.sh`, `screenshot-infopanel.sh`, `watch-replay.sh` | Worked command-file drivers |

## Existing scenarios using this

- `test-screenshot-smoke` exercises the pipeline: three captures at named beats, then `Test.Skip`. It asserts nothing — check the run directory yourself for three PNGs and three `screenshots[]` entries. Run it without `--hidden`, which lists captures it never writes.
