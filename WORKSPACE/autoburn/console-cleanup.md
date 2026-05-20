# Autoburn — Console.Write[Line] cleanup in tick-path engine code

**Branch:** `auto/console-cleanup`
**Date:** 2026-05-20
**Driver rule:** CLAUDE.md → "Engine code rules (universal anti-patterns)":
no `Console.Write`/`WriteLine` in tick-path code; use `Log.Write(channel, ...)`.

## Summary

Starting state: 65 `Console.Write[Line]` / `Console.Error.WriteLine` hits in
`engine/` outside the existing pre-commit allowlist (`UtilityCommands/`,
`UpdateRules/`, `/Lint/`, `OpenRA.Server/`, `OpenRA.Test/`, `OpenRA.Utility/`,
`tools/`, plus the `TestMode*.cs` explicit allowlist entries).

Result after this run: 31 remaining, all in one of three categories that are
deliberately not on tick paths (see "Skipped" below).

Total: **9 files touched**, **7 commits**, build passes for both
`OpenRA.Mods.Common` and `OpenRA.Platforms.Default`.

## What I did

| # | Commit | File(s) | What changed |
|---|---|---|---|
| 1 | `e121ba63` | `OpenRA.Game/Map/MapDirectoryTracker.cs` | UpdateMaps (drained per game tick) routes map add/update/delete events to `Log.Write("debug", …)` instead of stdout. Three sites. |
| 2 | `3d947676` | `OpenRA.Platforms.Default/OpenGL.cs` | `DebugMessageHandler` GL_DEBUG_SEVERITY_MEDIUM branch (un-rate-limited GL callback) routes to `Log.Write("graphics", …)` to match the existing graphics channel usage in this file. |
| 3 | `69b0dd2d` | `OpenRA.Game/Server/TraitInterfaces.cs` | `DebugServerTrait` per-event handlers (InterpretCommand, GameStarted, LobbyInfoSynced, ServerStarted, ServerShutdown, GameEnded) route to `Log.Write("server", …)`. Dropped unused `using System` after the conversion. Six sites. |
| 4 | `8da3bca2` | `OpenRA.Platforms.Default/Sdl2PlatformWindow.cs` | Dropped two Console.WriteLine calls (KDE desktop-file failure, hardware cursor failure) that were already duplicated by `Log.Write("debug", …)` on the previous line. |
| 5 | `b19a7ee2` | `OpenRA.Game/CryptoUtil.cs`, `OpenRA.Game/LocalPlayerProfile.cs`, `OpenRA.Game/Map/MapCache.cs`, `OpenRA.Game/Scripting/ScriptContext.cs` (LogDebugMessage only), `OpenRA.Mods.Cnc/Traits/World/VoxelCache.cs` | Removed Console.WriteLine duplicates of existing Log.Write calls. The "Lua debug:" prefix in ScriptContext.LogDebugMessage was redundant since the log channel is already `"lua"`. 12 lines removed across 5 files. |
| 6 | `94691ea0` | `OpenRA.Game/Graphics/CursorManager.cs`, `OpenRA.Mods.Common/Widgets/Logic/GameSaveBrowserLogic.cs` | Removed two more Console.WriteLine duplicates of existing Log.Write calls. |
| 7 | `014cc210` | `OpenRA.Game/Activities/Activity.cs` | `PrintActivityTree` (dev debug helper, documented to be called from `Tick()` / `BeforeRun()`) rewritten to compose the indented tree line and emit it via `Log.Write("debug", …)` instead of three Console calls. Updated doc-comment to mention the debug log channel. |

## Skipped (with reasons)

These remain on stdout intentionally — they are **not** tick-path code, and
the user (or a dedicated-server operator) explicitly relies on terminal
visibility.

### One-shot startup output

Pre-game launch banner; runs once at process start. Skipping these would
silently change the launch-time terminal output the user is used to seeing
in support tickets.

- `OpenRA.Game/Game.cs:328` — `Platform is …`
- `OpenRA.Game/Game.cs:340-341` — `Engine version is …`, `Runtime: …`
- `OpenRA.Game/Game.cs:382` — renderer fallback message inside the platform-loader try/catch loop (also has Log.Write("graphics") alongside; left for terminal visibility since the user must see which platform was chosen).
- `OpenRA.Game/Game.cs:398-425` — `Internal mods:` / `External mods:` listings
- `OpenRA.Game/Game.cs:480` — `Loading mod: …`
- `OpenRA.Game/InstalledMods.cs:50` — startup-time mod-search-path enumeration error (one-shot per launch)
- `OpenRA.Platforms.Default/OpenAlSoundEngine.cs:120-127` — sound device init (one-shot)
- `OpenRA.Platforms.Default/OpenGL.cs:563-564` — GL renderer/version init
- `OpenRA.Platforms.Default/Sdl2PlatformWindow.cs:171,216,219,225,339,350` — display init (one-shot per platform creation)
- `OpenRA.Mods.Common/LoadScreens/BlankLoadScreen.cs:57` — benchmark-completion message (one-shot)

### Exit-fatal handlers

Per task instructions: leave but note.

- `OpenRA.Game/Scripting/ScriptContext.cs:255-256` — `FatalError(Exception)` exit-fatal path (Game.Exit() in test mode, World.EndGame() otherwise). Log.Write("lua") already alongside; Console output is the user-visible "the game crashed" line.
- `OpenRA.Game/Scripting/ScriptContext.cs:280-281` — `FatalError(string)` mirror of the above.
- `OpenRA.Game/Support/ExceptionHandler.cs:52` — `HandleFatalError` writes the exception report to `Console.Error` AFTER writing to the exception log. This is the last thing the process does before dying.

### FieldLoader settings-load callbacks

- `OpenRA.Game/Settings.cs:375` — `UnknownFieldAction` for ignored YAML fields during settings load.
- `OpenRA.Game/Settings.cs:503` — `InvalidValueAction` for unparsable YAML values during settings load.

These fire during the one-shot settings load at startup. Per unknown field
in `settings.yaml` they could fire multiple times, but never on a tick. Kept
on Console so users see them in the launch terminal where the misconfigured
field can be fixed before next launch.

### Dedicated-server logging primitive

- `OpenRA.Game/Server/Server.cs:975` — `WriteLineWithTimeStamp` is only called when `Type == ServerType.Dedicated` (CLI server process). For dedicated servers stdout IS the log channel: it's what gets piped to journalctl, docker logs, etc. The file isn't in the `OpenRA.Server/` allowlist directory but the **method is the dedicated-server output primitive**. Recommend extending the pre-commit allowlist with `OpenRA\.Game/Server/Server\.cs` if a future edit triggers the hook here.

### Dead / disabled

- `OpenRA.Mods.Common/Traits/Attack/AttackBase.cs:314` — already inside a `/* … */` block comment. The grep matches because the pattern hits the line, but the code is disabled. Left as-is; commented-out code is a separate cleanup.

## Open questions

1. **Server.cs:975** — should the allowlist be extended to cover the dedicated-server logging primitive explicitly? See the recommendation under "Dedicated-server logging primitive" above. Suggested addition to `tools/git-hooks/pre-commit` `ALLOWLIST_PATTERNS`: `'OpenRA\.Game/Server/Server\.cs'`. Not done in this run since it touches policy.
2. **OpenAlSoundEngine.cs:120-127, OpenGL.cs:563-564, Sdl2PlatformWindow.cs init messages** — these are platform-init banners. If you want stricter cleanliness here, they could all be routed to `Log.Write("graphics", …)` / `Log.Write("sound", …)` (those channels already exist and Sdl2 already uses graphics for the cursor-failure case). I left them because launch-terminal visibility seemed worth more than the noise reduction. Easy follow-up.
3. **Game.cs startup banner** — same trade-off as #2. If the project decides launch output should be log-only, these are the obvious next batch.

## Verification

Build status after each commit and after the final commit:
- `OpenRA.Mods.Common/OpenRA.Mods.Common.csproj -c Release` — ✅ 0 warnings, 0 errors
- `OpenRA.Platforms.Default/OpenRA.Platforms.Default.csproj -c Release` — ✅ 0 warnings, 0 errors
- `OpenRA.Game/OpenRA.Game.csproj -c Release` — ✅ 0 warnings, 0 errors

Each file edit was followed by a build before committing.

Pre-commit hook (`tools/git-hooks/pre-commit`) only flags **new** Console.Write
additions on staged diffs, so the 7 commits in this batch did not require
hook adjustments — every change either removes Console.Write or replaces
it with Log.Write.

## Files touched

```
engine/OpenRA.Game/Activities/Activity.cs
engine/OpenRA.Game/CryptoUtil.cs
engine/OpenRA.Game/Graphics/CursorManager.cs
engine/OpenRA.Game/LocalPlayerProfile.cs
engine/OpenRA.Game/Map/MapCache.cs
engine/OpenRA.Game/Map/MapDirectoryTracker.cs
engine/OpenRA.Game/Scripting/ScriptContext.cs
engine/OpenRA.Game/Server/TraitInterfaces.cs
engine/OpenRA.Mods.Cnc/Traits/World/VoxelCache.cs
engine/OpenRA.Mods.Common/Widgets/Logic/GameSaveBrowserLogic.cs
engine/OpenRA.Platforms.Default/OpenGL.cs
engine/OpenRA.Platforms.Default/Sdl2PlatformWindow.cs
```

Net diff: 11 files changed, 7 commits, 65 → 31 non-allowlisted Console
sites in engine/ tree. Every remaining site is documented above.
