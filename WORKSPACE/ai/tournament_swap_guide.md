# Tournament Harness — How to Swap Any Part

> **Audience:** future agent (or future me) picking up the AI tournament harness
> and wanting to replace a piece without rewriting the rest.

The harness is built around **named swap points**. Every decision we made in
Phase 1 (which scorer, which win rule, how match config is loaded, how bots are
assigned, how results are aggregated) is reachable via one of these patterns.
This doc lists every swap point and the exact recipe to replace it.

If you find yourself asking "can I change X without touching the rest" — check
this list first.

---

## Map of swap points

```
Tournament/                             Engine — pluggable interfaces
├── IMatchScorer            (swap)      How are points computed each tick?
├── IWinRuleEvaluator       (swap)      When does the match end + who won?
├── MatchHarness            (registry)  String name → factory lookup
├── TournamentConfig        (parser)    What goes in tournament.yaml?
└── MatchTrackingState      (data)      Per-player cumulative counters

Traits/World/
└── BotVsBotMatchWatcher    (trait)     Drives everything; no game logic itself

ai.yaml conditions                      Bot module routing
├── enable-ai-any           (universal)
├── enable-ai-player        (normal+v2 baseline)
├── enable-ai-v2            (v2 only — for new modules)
├── enable-ai-legacy-only   (normal+rush+turtle, not v2 — for retired modules)
├── enable-ai-rush          (rush only)
└── enable-ai-turtle        (turtle only)

tools/autotest/
├── run-tournament.sh       (batch runner; iterates seeds, kicks off matches)
├── aggregate-tournament.sh (per-match JSON → summary.csv + summary.json)
└── scenarios/tournament-*/ (one map + one tournament.yaml per scenario)
```

---

## How to swap the **scorer**

The scorer is "how do we turn world state + cumulative events into points each
player?" The default (`weighted_components`) is army_value × weight +
capture_income × weight + kills_value × weight. To replace:

1. Create `engine/OpenRA.Mods.Common/Tournament/Scorers/MyNewScorer.cs`:

   ```csharp
   namespace OpenRA.Mods.Common.Tournament.Scorers
   {
       public class MyNewScorer : IMatchScorer
       {
           readonly TournamentConfig config;
           public MyNewScorer(TournamentConfig config) { this.config = config; }

           public MatchScoreSnapshot ComputeScore(Player player, World world, MatchTrackingState state)
           {
               var snap = new MatchScoreSnapshot();
               snap.Components["my_metric"] = ComputeMyMetric(player, world);
               snap.Total = snap.Components.Values.Sum();
               return snap;
           }
       }
   }
   ```

2. Register it in `MatchHarness.cs` static constructor:

   ```csharp
   RegisterScorer("my_new_scorer", c => new MyNewScorer(c));
   ```

3. Reference it from `tournament.yaml`:

   ```yaml
   Scorer: my_new_scorer
   ```

4. Build, run. **No changes to the watcher trait, the shell scripts, or other
   scorers.** Other scenarios using the old scorer still work.

---

## How to swap the **win rule**

Same pattern as the scorer.

1. Add `engine/OpenRA.Mods.Common/Tournament/WinRules/MyNewWinRule.cs`
   implementing `IWinRuleEvaluator`.
2. `MatchHarness.RegisterWinRule("my_new_rule", c => new MyNewWinRule(c));`
3. `tournament.yaml` → `WinRule: my_new_rule`

Return `null` to keep the match going, or a `MatchVerdict` to end it.

---

## How to add a **new scoring component**

Right now `MatchTrackingState` tracks `CaptureIncome` and `KillsValue` (both at
0 in Phase 1 — needs event wiring). To add e.g. "structures destroyed":

1. Add to `MatchTrackingState`:
   ```csharp
   public readonly Dictionary<Player, long> StructuresDestroyed = new ...;
   public long StructuresDestroyedFor(Player p) => ...;
   ```
2. Hook the event source: a `INotifyKilled` callback somewhere, or a world
   trait that subscribes to the engine event you need. Increment the state's
   counter when relevant.
3. Update `WeightedComponentMatchScorer` (or your custom scorer) to read it.
4. Optionally add a weight to `TournamentConfig.ScoreConfig` so the new
   component is configurable from `tournament.yaml`.

---

## How to change what `tournament.yaml` accepts

`TournamentConfig.cs` is the schema. To add a new top-level field:

1. Add a `public T MyField = default;` to `TournamentConfig`.
2. Add a `case "MyField":` to `LoadFromFile`.
3. Add the field to a sample `tournament.yaml`.
4. Update the comment block at the top of `TournamentConfig.cs` documenting
   the new field.

To add a nested config block (like `Score`):
- Add a `public class MyBlockConfig { ... }`.
- Add `public MyBlockConfig MyBlock = new MyBlockConfig();` on `TournamentConfig`.
- In `LoadFromFile`, `FieldLoader.Load(config.MyBlock, node.Value);`.

---

## How to use a **different map** for tournaments

1. Create a new folder under `tools/autotest/scenarios/tournament-<name>-2p/`.
2. Drop in a `map.bin` + `map.yaml` + optional `map.png`. Easiest: copy from
   an existing map in `mods/ww3mod/maps/` and modify the player section.
3. **Map requirements:**
   - At least 2 non-neutral non-spectator players, each owning an `supplyroute`
     actor (the SR-capture win condition needs both starting SRs).
   - At least 1 `mpspawn` actor (else `MapStartingLocations` crashes; see
     `WORKSPACE/ai/PITFALLS.md`).
   - 1 `PlayerReference@Observer` with `Playable: True, Spectating: True` so
     the local launch has a slot to occupy.
   - Bot combatants are non-playable map players with `Bot: <type>` directly
     in the PlayerReference (engine reads `pr.Bot` for non-playable players;
     see `engine/OpenRA.Game/Player.cs:198`).
4. Write a `tournament.yaml` next to the map.
5. Run `./tools/autotest/run-tournament.sh tournament-<name>-2p --seeds N`.

The shell harness and engine watcher don't know or care which map it is.

---

## How to add a **new matchup** (e.g. v2 vs rush)

Phase 1 hardcodes bots in `map.yaml`'s PlayerReference. To support
matchup-without-changing-the-map:

**Option A (simplest, what we have):** clone the scenario folder with different
`Bot:` values in `map.yaml`.

**Option B (better long-term):** add a `BotOverride` trait that reads
`tournament.yaml`'s `Matchup.P1Bot/P2Bot` at WorldLoaded and overrides the
players' `BotType`. **Blocker:** `Player.BotType` is `readonly`. Would need
either (a) a separate "active bot" indirection in the bot-tick path, or
(b) a setter for BotType. Worth the engineering when we get to Phase 4
personality differentiation.

---

## How to add a **new bot type** (e.g. v3, v2-aggressive)

1. In `mods/ww3mod/rules/ai/ai.yaml`:
   ```yaml
   ModularBot@v3:
       Name: V3 AI
       Type: v3
   GrantConditionOnBotOwner@v3:
       Condition: enable-ai-v3
       Bots: v3
   ```
2. Decide which baseline the new bot inherits. To clone v2's modules:
   - Add `v3` to `GrantConditionOnBotOwner@v2`'s `Bots` list (so v3 gets
     `enable-ai-v2` for free). This is the "baseline inheritance" pattern.
3. Add v3-specific modules with `RequiresCondition: enable-ai-v3`. They run
   only for v3, not for v2.

The condition map is documented in the header comment block of `ai.yaml`.

---

## How to swap **how match results are stored**

Currently `BotVsBotMatchWatcher` writes the verdict into
`TestMode.WriteResult("pass", <verdict-json>)` — i.e., the verdict JSON is
stuffed into the `notes` field of the existing test-result envelope.
`aggregate-tournament.sh` unwraps it via Python.

To switch to a different storage (separate file per match, sqlite db, etc.):

1. Modify `BotVsBotMatchWatcher.WriteVerdictAndExit`. Keep the
   `TestMode.WriteResult` call so test runners still see a verdict, but also
   write to your new sink.
2. Update `aggregate-tournament.sh` to read from the new sink.

The verdict shape itself is documented in `BotVsBotMatchWatcher.cs` header
comments — keep that doc in sync.

---

## How to swap **how the batch is run** (parallel, distributed, etc.)

`run-tournament.sh` is intentionally simple — sequential, one match at a time,
fork the engine per match. To replace:

- **Parallel** (Phase 3 of the plan): wrap multiple `run-test.sh`-style
  launches in `&` with per-instance profile dirs to avoid log/state collisions.
  Probably requires engine-side `--profiledir=` flag (not yet added).
- **Distributed** (across machines): wrap with a job dispatcher (each worker
  pulls one seed-index, runs locally, writes result to a shared mount). Same
  per-match interface, just orchestrated differently.

The script eats `tournament.yaml`, produces match_*.json files. Anything that
produces the same shape on the same filesystem layout works.

---

## How to swap **the autonomous tuning loop** (Phase 4)

Not yet built. Planned shape:

`tools/autotest/tournament/loop.sh <target.json>` —
- Reads a target (metric, goal, batch_size, budget_hours).
- Runs a batch, calls aggregate, evaluates the metric.
- Checks milestone triggers (winrate flips, perf regressions, target hit,
  outlier matches). On hit: write a `milestone_*.md` summary, bell the user.
- ScheduleWakeup between rounds.
- Stop when goal hit or budget exhausted.

Target JSON is the swap point — different schemas drive different optimization
strategies (winrate target, mean-score target, mean-game-length target). The
loop machinery doesn't change.

---

## Conventions that keep the swap points clean

These aren't enforced by tooling — they're discipline:

1. **Don't reach into another module's internals.** If a scorer needs new
   data, that data lives on `MatchTrackingState` (which is the contract
   between watcher and scorers). Don't subscribe to engine events from inside
   a scorer.
2. **Don't expand the watcher's responsibilities.** The watcher orchestrates,
   it doesn't compute. New logic goes in a new scorer/win-rule.
3. **Keep `MatchHarness` registrations local to the file the implementation
   lives in.** Tempting to put all registrations in `MatchHarness.cs` itself —
   that's the current pattern, but if the list grows past ~10 entries,
   consider a partial-class pattern with one register-self file per scorer.
4. **Names in `tournament.yaml` are string keys into a registry, not class
   names.** That's intentional — refactoring the C# class name doesn't break
   existing tournament configs. Keep the registry key stable; class names can
   change.
5. **Every new scorer/win-rule needs a one-line description in this doc.**
   Future-me skim-reads this file looking for "what's available."

---

## Currently registered scorers

| Key | File | Behavior |
|---|---|---|
| `weighted_components` | `Scorers/WeightedComponentMatchScorer.cs` | army_value × w₁ + capture_income × w₂ + kills_value × w₃ |

## Currently registered win rules

| Key | File | Behavior |
|---|---|---|
| `score_or_sr_capture` | `WinRules/TimeOrSrCaptureWinRule.cs` | SR capture = instant decisive (with bonus); otherwise highest total at time-limit |

When you add new ones, update these tables.
