# Tournament Harness — PITFALLS

> Traps already stepped on during Phase 1, recorded so the next agent doesn't
> step on them again. Each entry: what bit, why, how to avoid.

---

## 1. Engine cwd is `engine/`, not the repo root

**Bit:** smoke test wrote `Test.ResultPath=tools/autotest/.../match_1.json` —
expected to land at `<repo>/tools/autotest/.../match_1.json`. Actually landed at
`<repo>/engine/tools/autotest/.../match_1.json` because `launch-game.sh` does
`cd "${ENGINE_DIRECTORY}"` before running `dotnet bin/OpenRA.dll`.

**Why:** OpenRA's launcher takes paths verbatim from `Test.*` args.
Relative paths are resolved against the process cwd.

**Fix:** always pass **absolute paths** to `Test.ResultPath`,
`Test.TournamentConfig`, etc. `run-tournament.sh` resolves via `${REPO_ROOT}/...`.

---

## 2. `MapStartingLocations` crashes on empty `mpspawn` collection

**Bit:** my first scenario `map.yaml` had non-playable bot combatants + a
spectator Observer player but no `mpspawn` actors anywhere on the map. Engine
exception: `System.ArgumentException: Collection must not be empty. (Parameter 'ts')`
from `MapStartingLocations.AssignSpawnPoint:97` while assigning a spawn point to
the local human (the Observer client).

**Why:** even spectator clients go through
`CreateMapPlayersInfo.CreateServerPlayers` → `AssignSpawnPoint` → `mpspawns.Random(...)`,
which assumes at least one spawn exists.

**Fix:** every tournament scenario map needs **at least 1 `mpspawn` actor**.
Convention: place one near each combatant SR for symmetry, mark as
`Owner: Neutral`.

---

## 3. Non-playable map players DO work as bots — `pr.Bot` is honored

**Surprise:** my initial assumption was that bots only run for **playable** map
players (via `client.Bot` from the lobby). Wrong — `Player.cs:190–204` has a
distinct "Map player" path that sets `BotType = pr.Bot` for non-playable
players. So a `Playable: False, Bot: normal` PlayerReference becomes a real
bot, no lobby slot involved.

**Why we care:** this is what lets the tournament scenario have **two bots in
one match** without modifying lobby/skirmish logic. The lobby only holds the
local human (in the spectator Observer slot).

**Pitfall:** the `Bot` field on a **playable** PlayerReference is IGNORED — the
playable-slot path reads `client.Bot` instead. If you make the bot players
playable, you're back to needing slot-assignment plumbing (`slot_bot` orders,
spectator-izing the local client, etc.). **Don't make tournament combatants
playable** unless you're prepared to handle that.

---

## 4. `Player.BotType` is `readonly`

**Implication:** you cannot dynamically reassign a player's bot type after the
match starts. Matchup-without-changing-map (where one `map.yaml` serves
legacy-vs-legacy and legacy-vs-v2 by reading `tournament.yaml`) requires either
(a) an indirection trait between the slot's Player and the active bot modules,
or (b) making BotType writable + adding a swap method.

**Phase 1 workaround:** different matchups live in different scenario folders.
Easy to support N matchups by cloning the folder. The downside is
duplication — when the map itself changes, every clone needs the same edit.

**Phase 4 candidate:** a `BotTypeOverride` world trait that reads
`tournament.yaml` and remaps `enable-ai-*` conditions accordingly. Bigger
design pass; defer.

---

## 5. Pre-commit hook flags ALL modifications to Console.Write lines

**Bit:** I edited an existing `Console.WriteLine` line in `TestMode.cs` to add
new info. Pre-commit hook saw it as a "new Console.Write in tick-path code"
even though it was already there. The hook uses a `+`-prefix-line regex match
against the diff — it doesn't distinguish "added" from "modified".

**Fix:** when extending logging in a file that already has `Console.Write`:
- Don't modify the existing line. Add a `Log.Write("debug", ...)` call instead.
- For genuinely new logging in tick-path code, always use
  `Log.Write("debug", ...)` per the engine's "no Console.Write in tick-path"
  rule (CLAUDE.md → "Engine code rules").
- For tournament-related debug output that's NOT tick-path (init/teardown),
  `Log.Write` works too — `engine/Game.cs:355` already adds the "debug"
  channel.

---

## 6. Stale OpenRA processes leak from interrupted matches

**Bit:** if `run-tournament.sh` hits the wall-clock limit and tries to
`kill -TERM`, the dotnet process sometimes doesn't exit cleanly. On rebuild,
the running process holds mmap'd assemblies; subsequent launches can fail with
spurious assembly load errors (saw "Could not load file or assembly 'BeaconLib'").

**Fix:** before rebuilding the engine while debugging, check `pgrep -fl
'dotnet bin/OpenRA.dll'` and `pkill -KILL -f 'dotnet bin/OpenRA.dll'` if any
are stuck. The `engine/Directory.Build.targets` unlinks files before copy
specifically to avoid mmap corruption during in-place overwrite, but a stuck
process can still hold resources.

---

## 7. The static `MatchHarness` registry triggers on first call

**Surprise:** `MatchHarness` has a static constructor that registers default
scorers + win rules. Static constructors in .NET run **lazily** — only when
the class is first accessed. So if your scorer/win rule registration is in a
file that's *only* referenced by `BotVsBotMatchWatcher`, the registration runs
the moment the watcher's `WorldLoaded` calls `MatchHarness.CreateScorer(...)`.

**Implication:** don't add expensive registration work that would deadlock or
fail in `MatchHarness`'s static ctor — it runs on the game's main thread inside
`WorldLoaded`. Keep registration to a one-liner per factory.

---

## 8. `tournament.yaml` lives in the scenario, not at repo root

**Convention:** each tournament scenario folder owns its match config. Don't
look for a global `tournament.yaml` — there isn't one. To run a scenario with
different parameters (shorter time limit for smoke tests, different score
weights, different win rules), either:

- Edit the scenario's `tournament.yaml`, OR
- Pass `--config <path>` to `run-tournament.sh` pointing at an alternate file
  (e.g. `tournament-smoke.yaml` next to the main config).

The `--config` override is the right pattern when you want to keep the "real"
config canonical and run a smoke variant. See
`tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-smoke.yaml`.

---

## 10. `Log.Write` is buffered with a 5-second timer and lost on `Game.Exit()`

**Bit:** the watcher trait's `Log.Write("debug", ...)` calls in `WorldLoaded`
appeared in `debug.log` on long-running test runs but **disappeared** when
`Game.Exit()` was called soon after. OpenRA's `Log` class uses
`AutoFlush=false` plus a 5-second background flush timer
(`engine/OpenRA.Game/Support/Log.cs:50`). When `Game.Exit()` fires before that
timer ticks, buffered writes are abandoned.

Additionally, every game launch *truncates* `debug.log` via `File.CreateText`
(line 153) — so unwritten data from the previous launch is double-lost when the
next launch starts.

**Fix:** for diagnostic output that must survive a fast `Game.Exit()`, write
directly to a sibling file with `File.AppendAllText` (which flushes per call).
The watcher trait does this — see the `diag` lambda in
`BotVsBotMatchWatcher.cs`. Writes go to `<result>.watcher.log` next to the
verdict file. The Log.Write call is kept as a courtesy duplicate.

**Don't trust `Log.Write` alone for end-of-match diagnostics.**

---

## 11. `IWorldLoaded` fires BEFORE `SpawnMapActors` instantiates map actors

**Bit:** the watcher's WorldLoaded ran while `world.Actors` was still empty of
map-defined actors. My SR-discovery loop found 0 SRs. Diagnostic dump confirmed
`world has 6 actors total` (just the world/player actors) while map.yaml has
two SRs.

**Why:** `SpawnMapActors` is itself an `IWorldLoaded` trait
(`engine/.../World/SpawnMapActors.cs:23`). OpenRA's WorldLoaded ordering isn't
guaranteed alphabetically or by trait dependency — map actors may not exist
yet when another world trait's WorldLoaded fires.

**Fix:** defer "discover map actors" logic to the FIRST `ITick.Tick(self)` call,
which is guaranteed to run after all WorldLoaded handlers have completed. The
watcher's `srDiscoveryDone` flag gates this. Pattern:

```csharp
void ITick.Tick(Actor self)
{
    if (!srDiscoveryDone)
        DiscoverSrsOnFirstTick(self.World);
    ...
}
```

This applies to **any** trait that needs to enumerate map-spawned actors. If
your trait reads `world.Actors` in WorldLoaded and gets fewer than expected,
this is why.

---

## 12. Rebuild race: `make all` finishing before pending file saves are flushed

**Bit:** ran `make all && ./run-tournament.sh ...` immediately after a series
of `Edit` tool calls. The build completed in ~5s but my latest edits arrived
on disk AFTER the build started — the resulting DLL didn't contain the new
code, and the diagnostic output reflected the previous version.

**Why:** the harness writes files synchronously but C#'s `dotnet build` reads
sources at the start of its job. A fast-following invocation can race.

**Fix:** before kicking off a build that depends on a fresh edit, **always
verify** that the source file mtime is older than the DLL mtime before running:

```bash
ls -la engine/OpenRA.Mods.Common/Traits/World/MyTrait.cs \
       engine/bin/OpenRA.Mods.Common.dll
```

If source is newer than DLL after the build, rebuild. (Pattern that worked:
edit → `ls -la <source> <dll>` → if source-newer, `make all` again.)

---

## 13. `option gamespeed <key>` silently falls back to default on bad keys

**Bit:** while wiring `Test.GameSpeed=<key>` for the tournament harness, an
invalid key (typo, or one not in the mod's GameSpeeds dictionary) didn't
throw — it just used `default` speed silently.

**Why:** the lobby's gamespeed option is a dropdown with a fixed key set
(loaded from `engine/mods/ra/mod.yaml`'s `GameSpeeds`). The `option gamespeed
<key>` setup order is validated against the keys; mismatched ones produce no
error log and no behavior change.

**Fix:** the only valid WW3MOD GameSpeed keys are:
`slowest, slower, default, fast, faster, fastest`. Always use one of these.
If a tournament run is suspiciously slow despite `GameSpeed: fastest`, suspect
a typo or capitalization issue first.

**Speed values** (Timestep ms/tick → real-time multiplier vs default 40):
- slowest 80 (0.5×), slower 50 (0.8×), default 40 (1×), fast 35 (1.14×),
  faster 30 (1.33×), fastest 20 (2×).

The 2× cap is intrinsic to the mod's GameSpeeds config. Headless rendering
won't go faster than this — Phase 2 of the tournament plan needs to also raise
the Timestep ceiling (or skip-rendering frame-by-frame) for big speed-ups.

**Empirical:** smoke test of a 30-sec match dropped from 57s → 42s wall-clock
(~26% reduction). For a 12-min match, expect ~50% reduction (most overhead
is engine init at ~30s, which doesn't scale with game speed).

---

## 14. `PlayerStatistics.Income` is a 60-second rolling window, not cumulative

**Bit:** when wiring score components, used `PlayerStatistics.Income` for
"income earned" — got values that fluctuated up and down instead of monotonic
accumulation. Income field is actually `resources.Earned - lastEarned_60s_ago`.

**Fix:** for cumulative earnings, use `PlayerResources.Earned` directly —
that's the lifetime total. Scoring component "capture_income" reads that.

---

## 15. Server seed defaults to `DateTime.Now.ToBinary()` — same config = different matches

**Bit:** until Round 5 wired it explicitly, running the same tournament scenario
twice produced different scores. Looking like flakiness; actually the engine seeds
its `MersenneTwister` from current time, so every launch is its own roll.

**Why:** `engine/OpenRA.Game/Server/Server.cs:307` — `randomSeed =
(int)DateTime.Now.ToBinary();`. There's no override in stock OpenRA.

**Fix landed in Round 5:** `Test.RandomSeed=<int>` arg honored by Server.cs.
run-tournament.sh passes `seed_index × 1000 + 17` per match, so seed index N
always produces the same game given the same code + map. Reproducibility for
debugging a specific outlier match is now possible.

**Implication for statistical validity:** for batch averages, we want
*different* seeds across matches (which we have — N × 1000 + 17 varies per N).
For reproducing an *individual* match for debugging, fix the seed to that
match's index. The two needs are now both met.

---

## 16. `option gamespeed` caps at 2× in WW3MOD — use `world.Timestep` override for >2×

**Bit:** despite `Test.GameSpeed=fastest`, the actual wall-clock for a 30-sec
match dropped only 57s → 42s (~26% reduction). Expected closer to 50%.
Investigation: `fastest` in WW3MOD's GameSpeeds config sets `Timestep: 20` (=
20 ms/tick = 50 ticks/sec = 2× default). That's the hard cap from the lobby
option mechanism.

**Real acceleration mechanism:** the in-game cheat `SpeedControlButton`
(`engine/.../Widgets/Logic/Ingame/SpeedControlButtonLogic.cs`) goes up to 8× by
*directly setting `world.Timestep`* at runtime — no GameSpeeds lookup involved.

**Fix landed in Round 5 v2:** `Test.SpeedMultiplier=<int>` launch arg honored
by `BotVsBotMatchWatcher.WorldLoaded`. The watcher sets
`world.Timestep = max(1, world.Timestep / multiplier)` after the lobby setup
has applied any GameSpeed. Multiplier stacks on top of GameSpeed
(fastest × 8 → 20 / 8 = 2.5 → 2 ms/tick = 500 ticks/sec target). Cap 16×.

**Implication:** for tournament batches, set `SpeedMultiplier: 8` in
tournament.yaml. GameSpeed becomes a no-op once SpeedMultiplier ≥ 2; we keep
both for completeness and so the override path is explicit at every layer.

**Real-world cap:** the renderer may be the bottleneck — even with the
Timestep set very low, the engine can't go faster than the render pipeline
allows. Headless rendering (Phase 2) is the way past that ceiling. With
rendering enabled, expect 4-6× practical speed-up not the theoretical 8×.

---

## 17. Render-framerate cap is a cheaper "headless lite" than a Null renderer

**Discovery (Round 8 investigation):** building a true headless renderer
requires replacing `IPlatform` + `IGraphicsContext` with no-op stubs — days of
work, with real risk of breaking simulation determinism. NOT worth it for a
one-shot batch acceleration.

**Cheaper alternative:** `Graphics.CapFramerate=true` + `Graphics.MaxFramerate=5`
launch args. The engine still renders, but at 5 FPS instead of 60 FPS. That's
12× less render-side CPU; combined with `Test.SpeedMultiplier=8` the
simulation can hit higher tick rates without rendering being the bottleneck.

**Trade-off:** the game window looks janky during a tournament run (jumps
multiple sim seconds per render frame). For automated batches that's fine —
nobody's watching. For inspecting a specific match, drop the cap or remove
the flag.

**Engineering effort:** zero (no new code). Just launch args. Landed in
run-tournament.sh in Round 8 of the autonomous-overnight run.

---

## 9. Sound.Mute must be passed via launch arg, NOT toggled persistently

Already covered in `run-test.sh` comments but worth reinforcing for the
tournament case: every match launch backs up + restores
`~/Library/Application Support/OpenRA/settings.yaml` because the engine
auto-saves settings on exit, and a stuck `Sound.Mute=true` would carry over to
normal launches. `run-tournament.sh` mirrors the same backup/restore logic
per-match.

---

## Discoveries to add as I hit them

When something else bites: append here with the same template (what bit, why,
how to avoid). Cross-link to the file/line where the pitfall lives if relevant.
