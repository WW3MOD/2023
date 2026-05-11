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
