# Screenshot capture + autonomous visual evaluation

**Date:** 2026-05-12
**Author:** agent
**Status:** AWAITING APPROVAL — implementation will start after user sign-off

---

## Goal

Give the agent eyes. The game can already write PNGs to disk via `Game.TakeScreenshot()`; the multimodal `Read` tool can ingest PNGs and reason about them. The missing pieces are: (1) a deterministic way to *trigger* a screenshot at a named beat — from Lua during AUTOTEST scenarios, and from outside the game during menu/lobby work; (2) a contract that surfaces the resulting paths so the agent knows where to look without polling the filesystem; (3) a small evaluation protocol so screenshot-based verdicts are repeatable instead of vibes. End state: I can say "screenshot the lobby panel and confirm the system-line chat colour is muted yellow" or "this fixed the artillery setup bug — capture frame 90 and verify the turret is rotated 90° from chassis" and get a concrete answer.

## Non-goals (Phase 1 / Phase 2)

- **No full menu/lobby UI automation** (programmatic button-clicking, slot population, faction selection, host-start). That's Phase 3, scoped separately. For Phases 1–2 the user poses the UI manually; the agent screenshots and evaluates whatever's on screen.
- **No pixel-diffing / golden-image regression**. Visual evaluation is LLM-judged, not pixel-equal. Tests assert "what should be visible" semantically, not "this frame must equal `golden.png`".
- **No live screen-streaming**. One-shot screenshots only. Streaming is a 10× project for a 1.5× benefit.

## Constraints

1. **Zero impact when `Test.Mode=false`.** All new code paths gated on `TestMode.IsActive`. Normal game launches see nothing new — no extra threads, no extra files, no hotkey changes.
2. **Existing AUTOTEST flow stays green.** The verdict JSON schema is consumed by `tools/autotest/run-test.sh` and `run-batch.sh`. Any extension must be backward-compatible (new optional fields, no renames).
3. **Existing `Ctrl+P` screenshot hotkey unchanged.** Human-driven captures continue to work exactly as today; the new path is additive.
4. **Cross-platform parity.** macOS is the primary dev target but the engine still ships on Windows/Linux. File watcher must use `FileSystemWatcher` or polling — no platform-specific IPC.
5. **No new mandatory build steps.** `make all` must still build. Any new tooling lives under `tools/` and is opt-in.

## Affected files

### New (created in this plan)

| Path | Role |
|---|---|
| `engine/OpenRA.Game/TestModeScreenshots.cs` | Static class owning the per-run screenshot output dir, command-file watcher, and counter. Keeps `TestMode.cs` lean. |
| `engine/OpenRA.Mods.Common/Scripting/Global/TestScreenshotGlobal.cs` *or* extend `TestGlobal.cs` | `Test.Screenshot(label)` Lua API. (Likely just extend `TestGlobal.cs` — one method, no reason for a new file.) |
| `tools/autotest/screenshot.sh` | External CLI: `screenshot.sh <label>` writes a command file the running engine picks up. Used for menu/lobby. |
| `tools/autotest/list-screenshots.sh` | List last N screenshots from a test run, with timestamps. Convenience for debugging. |
| `DOCS/recipes/SCREENSHOT.md` | Recipe entry — trigger phrase `SCREENSHOT <topic>`, when to use, evaluation contract, examples. |

### Modified

| Path | Change |
|---|---|
| `engine/OpenRA.Game/TestMode.cs` | Extend `WriteResult()` to emit a `screenshots` array. Track captured screenshots in a static list during the run. |
| `engine/OpenRA.Mods.Common/Scripting/Global/TestGlobal.cs` | Add `Screenshot(string label, string note = "")` method. |
| `engine/OpenRA.Mods.Common/World/TestModeScreenshotWatcher.cs` (new world-trait OR a `IGameLoaded` hook) | Polls the command file each tick when `TestMode.IsActive`. Calls `Game.TakeScreenshot()` with controlled filename when a command is found. |
| `tools/autotest/run-test.sh` | Pass `Test.ScreenshotDir=...` and `Test.ScreenshotCmdFile=...` launch args. Pre-create the directory. Surface screenshot paths from the verdict JSON in the test panel summary. |
| `tools/autotest/run-batch.sh` | Same args. Aggregate screenshot counts in batch summary. |
| `mods/ww3mod/scripts/test-helpers.lua` | Add `TestHarness.Screenshot(label)` thin wrapper (logs to console too, for visibility in `--visible` runs). |
| `CLAUDE.md` | Add the `SCREENSHOT` trigger to the modes/recipes table. Add a one-line note under "Developer Test Harness". |
| `DOCS/recipes/AUTOTEST.md` | Mention `Test.Screenshot(label)` in the Lua API table. Cross-link to the new SCREENSHOT recipe. |

### Out of scope for Phases 1–2 (would be Phase 3)

- `engine/OpenRA.Mods.Common/Widgets/Logic/MainMenuLogic.cs`, `LobbyLogic.cs` — would need a widget-driving harness. Skipped.

---

## Phases

### Phase 1 — In-game screenshots via Lua (AUTOTEST integration)

**Scope.** Add `Test.Screenshot(label)` Lua. Captures land in a per-run dir. Paths emitted in the verdict JSON. Test runner surfaces them. Agent reads the PNGs.

**Steps (each is independently buildable & commit-able):**

1. **`TestModeScreenshots` static class.** Owns: per-run output dir (default `~/.ww3mod-tests/screenshots/<UTC-iso>/`, override via `Test.ScreenshotDir=...` launch arg); a `List<(string label, string path, int tickAtCapture)> Captured`; a `Capture(label)` method that picks the filename (`<NNN>_<label>.png` where NNN is zero-padded sequence), calls `Game.TakeScreenshot()` via a public hook, records the entry.
2. **Expose `Game.TakeScreenshotTo(path)`.** Currently `TakeScreenshotInner()` builds its own path. Refactor: extract a `TakeScreenshotInner(string explicitPath = null)` overload. If `explicitPath` provided, use it; otherwise existing behaviour. Plumb a public `Game.TakeScreenshot(string explicitPath)` static API that flips the `takeScreenshot` flag with a target path stashed in a private field. *Tiny, surgical engine change.*
3. **`Test.Screenshot(label, note)` Lua method.** No-op outside `TestMode.IsActive`. Validates label (lowercase-alnum-dash only; reject anything else with a hard error in test mode so typos surface). Calls `TestModeScreenshots.Capture(label, note)`.
4. **Verdict JSON extension.** `TestMode.WriteResult()` emits a `screenshots` array: `[{label, path, tick, note}]`. Backward-compatible — old consumers ignore unknown fields. Update `tools/autotest/run-test.sh` to read and print `Captured N screenshot(s): <paths>` to the panel summary.
5. **Test runner plumbing.** `run-test.sh` pre-creates `~/.ww3mod-tests/screenshots/<run-id>/`, passes its path as `Test.ScreenshotDir=`. `run-id` is `YYMMDD_HHMMSS_<test-name>` to keep multiple runs tidy.
6. **Helper + first usage.** Add `TestHarness.Screenshot(label)` to `test-helpers.lua` (thin wrapper, logs to console). Update `test-paladin-fires` to capture a screenshot at the moment of ammo-drop — single line — and confirm the verdict surfaces it. This is the smoke test that proves the pipeline.
7. **PITFALL anchor.** `Game.TakeScreenshot()` is async (`ThreadPool`). When verdict is written immediately after a capture, the PNG may not be flushed yet. Either (a) wait synchronously for the file to exist with a 1s timeout before `WriteResult()`, or (b) document that screenshot files appear post-exit and runner waits ~250 ms after the verdict before listing them. I lean (b) — simpler, doesn't block the render thread. The PITFALL comment lives at the `WriteResult()` call site in `TestGlobal.Pass/Fail`.
8. **Commit, regression sweep, FINALIZE.**

**Phase 1 acceptance:**
- `test-paladin-fires` runs green, verdict JSON contains a `screenshots: [{...}]` entry, the PNG exists and renders correctly.
- I can `Read` the PNG and describe what I see ("Paladin firing at T-90, muzzle flash visible, target ~6 cells away").
- A new test `test-screenshot-smoke` exists that takes 3 screenshots at known beats and asserts via Lua only — verdict pass — and the screenshots are inspectable.

---

### Phase 2 — Externally-triggered screenshots (menu / lobby / arbitrary game state)

**Scope.** The agent can request a screenshot of any running game — main menu, server lobby, in-match — without involving Lua. Works by writing a small command file the engine watches when `Test.Mode=true`.

**Steps:**

1. **Launch the game in "screenshot mode".** New launch arg pattern: `Test.Mode=true Test.Name=manual Test.ScreenshotCmdFile=~/.ww3mod-tests/cmd.txt`. No `Launch.Map`. The game opens at the main menu normally; the engine just knows it's allowed to listen for screenshot requests.
2. **Command-file watcher.** A small `Action`-on-tick (registered via `Game.RunAfterTick` or a `World` trait depending on context; the menu has no `World`, so the engine-level path is needed — likely a poll in `Game.LogicTick`). Checks `Test.ScreenshotCmdFile` mtime every ~250ms. If changed, reads the file, parses commands one per line, executes, then either truncates or appends a `done <label>` line.
3. **Command grammar (minimal).** Line 1: `screenshot <label>`. That's it for Phase 2. Future commands (Phase 3) might be `click <widget-id>`, `key F`, but those need real implementation work — Phase 2 stops at screenshot.
4. **CLI wrapper.** `tools/autotest/screenshot.sh <label>` writes `screenshot <label>\n` to the watched file. Returns immediately. Optionally `--wait` flag polls for the `done` line, then returns the resulting PNG path on stdout — useful when the agent needs to immediately `Read` the file.
5. **Result surfacing.** Same per-run dir as Phase 1. When no test is "running" (no Lua, no Pass/Fail expected), screenshots accumulate in `~/.ww3mod-tests/screenshots/manual_<run-id>/` and an index file `manifest.json` lists `{label, path, captured_at}`. Agent reads the manifest to know what's available.
6. **Recipe doc.** `DOCS/recipes/SCREENSHOT.md` documents both Phase 1 (in-test) and Phase 2 (manual) flows. Trigger phrase `SCREENSHOT <topic>` for the agent.
7. **First real use.** Apply to current lobby UI work: launch the game manually with `Test.Mode=true`, navigate to the server lobby, run `./tools/autotest/screenshot.sh lobby-system-chat-tone`, agent reads the PNG and verifies the system-line colour change vs. the prior tone.

**Phase 2 acceptance:**
- Game launched with `Test.Mode=true` + `Test.ScreenshotCmdFile=...` and NO `Launch.Map` reaches the main menu normally.
- `screenshot.sh main-menu` produces a PNG of the main menu within ~500ms.
- Same script works from the server lobby and from an in-match state.
- Manifest file accurately lists captures.
- Normal launches (no `Test.Mode`) are unaffected — file watcher is dormant, no thread spawned.

---

### Phase 3 — UI automation (future, NOT in this plan's commitment)

Sketched only to mark the seam. Would add `click <widget-id>` and `text <field-id> <value>` commands to the same command-file watcher; the engine resolves widget IDs against the active chrome tree and synthesises events. Would let the agent stage a known lobby state ("3 slots, 1 human, 2 bots, map = River Zeta") and screenshot it. Big enough that it needs its own plan. **Recommendation: don't start it until Phase 1+2 have been used for a week and we know what's actually missing.**

---

## Evaluation contract

How the agent decides whether a screenshot shows what it should:

1. **Declarative expectations (preferred for AUTOTEST regressions).** Test scenario passes a list of expectations to `Test.Screenshot`: `Test.Screenshot("paladin-firing", "expects: muzzle flash visible; T-90 in frame; no friendly fire indicators")`. Verdict JSON carries the expectations alongside the path. Agent reads the PNG, judges each expectation true/false, writes observations back into a post-run review note. Failures are logged but **don't auto-fail the test** in Phase 1–2 — visual judgment is too noisy for hard gating yet. They surface in the end-of-message block as `⚠️` for the user to act on.
2. **Freeform observation (preferred for menu/lobby work).** No expectations — agent just reads and describes ("Lobby chat box visible at bottom; system message reads 'Player 1 joined' in light-grey text; no settings panel open"). User reacts. Captures evolve into expectations as patterns stabilise.
3. **What I'm reliable for.** Coarse semantic checks: presence/absence of UI elements, colours obviously wrong (pure yellow vs muted gold), units rendered/not rendered, animations clearly playing (fire, smoke, muzzle flash), formations spread/bunched.
4. **What I'm NOT reliable for.** Pixel-perfect alignment, exact text in cluttered HUDs, counting >5 similar units, small font readouts at default zoom, frame-exact timing. For those, prefer `Test.GetX()`-style state queries.

---

## Risks / open questions

1. **Async screenshot flush timing.** Mitigated by Phase 1 step 7. Worst case if I'm wrong: the runner reports a `screenshots[].path` that doesn't exist for ~100ms. Recoverable.
2. **macOS background-window rendering.** AUTOTEST defaults to `--background` (visible, defocused). Confirmed in `DOCS/recipes/AUTOTEST.md` that the window is at full size with normal rendering — screenshots will work. If `--minimized` is used, the window may not render frames; screenshots could be black. **Recommendation: document that screenshots require `--visible` or `--background`, not `--minimized`.**
3. **Window resolution variance.** Different machines / multi-monitor setups will produce different-sized PNGs. Acceptable for semantic evaluation; problematic for any future pixel-diff regression. Logged as a Phase 3 concern.
4. **Per-test output directory ownership.** `~/.ww3mod-tests/screenshots/<run-id>/` — who cleans up? Recommendation: runner deletes runs older than 7 days at the start of each batch. Cheap and keeps the disk tidy.
5. **Will the agent over-screenshot?** Risk of every test producing 5 PNGs the agent then has to read. Mitigation: encourage one-per-test by default; multi-screenshot is opt-in for tests where intermediate state matters. Track via `git grep "Test.Screenshot"` counts during code review.
6. **Lobby ergonomics without UI automation.** Phase 2 requires the user to manually pose the lobby (navigate to it, configure slots) before each screenshot. For one-off checks like "verify the chat tone", that's a few clicks — fine. For a full lobby regression suite, it'd be tedious. That's the case for Phase 3 eventually.
7. **`Read` tool cost on PNGs.** Each PNG ~50–200 KB; reading several per turn is fine but a 30-screenshot test would be expensive. Encourages discipline in screenshot count, which is good anyway.

---

## Effort estimate

- **Phase 1:** ~3–4 hours. Surgical engine refactor + Lua binding + runner plumbing + one smoke test + docs. All within RELEASE mode discipline.
- **Phase 2:** ~2–3 hours. File-watcher in `Game.LogicTick` (or equivalent), CLI wrapper, manifest emission, recipe doc.
- **Total to "I can see the lobby":** ~half a day of focused work, split across 6–8 commits.

## Decision point

This plan locks in:
- File-watcher (not socket) for external triggers.
- Per-run output dir under `~/.ww3mod-tests/screenshots/`, paths echoed in verdict JSON.
- Both expectation-driven AND freeform evaluation, neither hard-gating the test verdict in Phases 1–2.
- Phase 3 (UI automation) explicitly punted.

Ready to implement on approval. If you want a different split (e.g. Phase 2 first because lobby work is current), say so — Phase 2 *can* land before Phase 1 since they share infrastructure but neither blocks the other.
