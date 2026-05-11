# AI Tournament Harness — implementation plan

**Date:** 2026-05-11
**Parent project:** `WORKSPACE/ai/` (AI overhaul)
**Status:** approved shape, not started

## Goal

Build a reusable AI-vs-AI tournament harness on top of the existing
`tools/autotest/` infrastructure. Same binary runs legacy and new ("v2")
bots side by side; runs many seeded matches in parallel headless, aggregates
score-based outcomes per commit, and pings the user only at meaningful
milestones during long autonomous tuning sessions.

The harness must be **useful before any new-brain code exists** — a
legacy-vs-legacy run should produce a ~50/50 baseline as a sanity check.
Every later AI-overhaul commit can then be measured against the previous
baseline automatically.

Reusable beyond the AI overhaul: same harness validates balance changes,
difficulty tiers, personality differentiation (Rush vs Turtle), and any
future "X breaks the AI" regression.

## Locked decisions (from 260511 conversation)

| Decision | Choice | Rationale |
|---|---|---|
| Old-vs-new mechanism | Dual `ModularBot@*` types, same binary, feature-flagged in YAML | No git acrobatics, baseline always available, regression measurable per commit |
| Win condition | **Hybrid**: time-limited score is primary; enemy SR capture is an instant win with score bonus | Smooth signal in most games + captures the rare decisive outcomes |
| Headless + parallel | Build together upfront | Autonomous tuning needs hours-of-experiments-per-minute throughput from day one |
| Milestone UX | Milestone-driven pings (winrate flip, perf regression, target hit, budget exhausted) | Quiet by default; bell + summary only when interesting |

## Constraints

- **Must not break existing autotest flow.** `tools/autotest/run-test.sh` and `run-batch.sh` continue working unchanged for the existing 35+ `test-*` scenarios.
- **Must run on macOS** (user's platform). File paths and process spawning behave there.
- **Must respect CLAUDE.md no-push rule** — all commits stay local until the user pushes.
- **Single source of truth for the legacy bot.** When `ModularBot@v2` lands, `ModularBot@legacy` must keep working identically to today's `ModularBot@normal` — no silent drift. Achieve this by inheriting from `^LegacyBot` base templates.
- **Determinism.** Each match is deterministic per seed. Sample variation comes from seeded variation across N matches, not nondeterminism inside a single match.
- **Attribution.** Every result file stamps `git rev-parse HEAD` so changes between batches are traceable to commits.

## Architecture (four layers)

```
Layer 1 — Dual-bot YAML wiring + new brain feature-flag
   mods/ww3mod/rules/ai/ai.yaml
      ^LegacyBotModules         (template: current modules as they are today)
      ^V2BotModules             (template: new brain layer once it lands)
      ModularBot@legacy   →  Inherits ^LegacyBotModules
      ModularBot@v2       →  Inherits ^V2BotModules

Layer 2 — Tournament scenario type
   tools/autotest/scenarios/tournament-<name>/
      map.yaml + map.bin + map.png  (real-ish 1v1 map)
      rules.yaml                     (overrides for the match)
      tournament.json                (matchup, seeds, score formula, time limit)
   Engine reads tournament.json via Test.TournamentConfig launch arg.
   New trait BotVsBotMatchWatcher tracks state, writes verdict at game end.

Layer 3 — Batch runner with parallel + headless
   tools/autotest/run-tournament.sh <scenario> [opts]
   tools/autotest/tournament/aggregate.sh  (CSV summary across many results)

Layer 4 — Autonomous tuning loop
   tools/autotest/tournament/loop.sh <target.json>
   Drives the milestone-driven UX. Uses ScheduleWakeup between rounds.
```

## Phase 1 — MVP (1–2 sessions, sequential + windowed)

**Goal:** legacy-vs-legacy matchup runs, produces ~50/50 over 20 seeds, harness can be invoked from CLI.

### 1.1  Dual ModularBot in YAML

- Refactor `mods/ww3mod/rules/ai/ai.yaml`:
  - Extract the current bot module set into a YAML template `^LegacyBotModules`.
  - Create `ModularBot@legacy` and `ModularBot@v2`, both inheriting `^LegacyBotModules` initially. (V2 forks once new modules exist.)
  - Keep `ModularBot@normal/@rush/@turtle` aliasing to `@legacy` for now so nothing in production changes.
- Verify a 1v1 game in lobby with one player set to "Legacy AI" and one to "V2 AI" runs identically to today's Normal AI vs Normal AI.

### 1.2  Tournament scenario format

- New scenario folder: `tools/autotest/scenarios/tournament-river-zeta-2p/`.
- Strip the river-zeta map down to a clean 1v1 layout. **Do not edit the live `mods/ww3mod/maps/river-zeta-ww3/`** — copy it.
- `tournament.json` schema (committed example, the canonical reference):

```json
{
  "matchup": { "p1_bot": "legacy", "p2_bot": "legacy" },
  "seeds": [1, 2, 3, ..., 20],
  "time_limit_seconds": 720,
  "score": {
    "army_value_weight": 1.0,
    "capture_income_weight": 2.0,
    "kills_value_weight": 1.0,
    "sr_capture_bonus": 100000
  },
  "win_rule": "score_or_sr_capture",
  "score_margin_for_decisive": 0.20
}
```

### 1.3  Engine trait `BotVsBotMatchWatcher`

- World trait, conditional on Test.Mode + a Test.TournamentConfig arg pointing at the JSON.
- On `WorldLoaded`: read tournament.json, set seed, set each player's BotType to the matchup config.
- On `Tick`: check game-end conditions (time limit reached OR any SR captured). Compute final score. Write verdict via `TestMode.WriteResult("pass", <json-blob-of-result>)`.
- Verdict JSON shape (one match):

```json
{
  "scenario": "tournament-river-zeta-2p",
  "seed": 7,
  "git_sha": "b2fa4bd2...",
  "duration_ticks": 17280,
  "winner": "p2",
  "win_reason": "sr_capture",
  "score_p1": 38400,
  "score_p2": 142100,
  "score_components_p1": { "army_value": 12400, "capture_income": 26000, "kills_value": 0 },
  "score_components_p2": { "army_value": 18100, "capture_income": 24000, "kills_value": 0, "sr_capture_bonus": 100000 },
  "perf": { "avg_tickrate": 320, "max_frame_ms": 18.7 }
}
```

### 1.4  `run-tournament.sh`

- Calls `run-test.sh` (or its core path) once per seed, passing the seed and tournament config path.
- Collects per-match verdict JSON files into a timestamped result dir: `tools/autotest/tournament-results/<YYMMDD_HHMM>_<scenario>/`.
- After all seeds, emits a `summary.csv` (one row per match) + `summary.json` (aggregate stats: win rate per side, avg score, decisive %, perf p50/p99).

### 1.5  Sanity check

- Run `./tools/autotest/run-tournament.sh tournament-river-zeta-2p --seeds 20 --bot-a legacy --bot-b legacy`.
- Expectation: legacy-vs-legacy winrate within 40–60% over 20 games (statistical noise). If consistently skewed, the map has a positional bias that needs fixing before it's usable as a benchmark.

**Phase 1 ships when:** legacy-vs-legacy runs unattended, produces a CSV, and the winrate is in the noise band.

## Phase 2 — Headless renderer (1 session, engine work)

- Add `Game.Renderer=Null` launch arg in `engine/OpenRA.Game/Settings.cs`.
- New `engine/OpenRA.Game/Graphics/NullRenderer.cs` implementing the `IRenderer` interface as no-ops.
- Skip cursor / audio / video paths when Null. Verify game tick progresses to natural game-end without rendering.
- Verify identical results vs windowed mode for the same seed — headless must not change game state.
- `run-tournament.sh` gets `--headless` flag (default true once verified).

**Phase 2 ships when:** a 12-min tournament match completes in <30s wall-clock headless (vs ~3 min real-time at 1×, ~30s at 6× windowed today, target <10s headless).

## Phase 3 — Parallel runner (1 session)

- `run-tournament.sh --parallel N` spawns N instances simultaneously.
- Each instance gets an isolated profile dir (`tools/autotest/tournament-profiles/<pid>/`) so logs/configs don't collide.
- Wait for all to complete, aggregate.
- Investigate OpenRA reentrancy on macOS — may need an extra env var or `--game.profiledir=`.

**Phase 3 ships when:** N=4 parallel matches complete cleanly, results aggregate correctly, wall-clock is ~1/N of sequential.

## Phase 4 — Autonomous tuning loop (1 session)

- `tools/autotest/tournament/loop.sh <target.json>` — milestone-driven daemon.
- Target schema:

```json
{
  "scenario": "tournament-river-zeta-2p",
  "metric": "v2_winrate",
  "goal": 0.60,
  "batch_size": 50,
  "budget_hours": 8,
  "milestone_triggers": [
    { "name": "winrate_flip_above_50", "condition": "v2_winrate > 0.50" },
    { "name": "winrate_drop_below_30", "condition": "v2_winrate < 0.30" },
    { "name": "perf_regression", "condition": "avg_tickrate < 100" },
    { "name": "outlier_match", "condition": "score_ratio > 10" }
  ]
}
```

- Behavior: run a batch of 50, aggregate, evaluate milestones, write `milestone_<name>_<timestamp>.md` summary if any triggered, bell user, continue. Stop when goal hit or budget exhausted.
- Result files keep accumulating in `tournament-results/` so the user can scrub any past batch.
- Integrates with ScheduleWakeup: agent fires the loop, sleeps until completion notification, picks up next action.

**Phase 4 ships when:** agent can fire-and-forget a loop, walk away, the user gets bell + summary at each milestone, full timeline of every commit's batch is on disk.

## Phase 5 — Diagnostics & replay (later, opportunistic)

- Per-match diagnostics: army peaks, capture timings, SR contestation events, AI Plan transitions (depends on AI Phase 2 from `WORKSPACE/ai/foundation_260511.md`).
- Decisive games (margin > threshold) save their replay automatically to `tournament-results/<batch>/decisive-replays/` for user inspection.
- Replay-friendly Plan log: each AI's Plan-over-time recorded so the debug overlay can replay it.

Not blocking — added when we have a use for the data.

## Affected files

### New
- `mods/ww3mod/rules/ai/ai.yaml` — heavy refactor (template extraction + new bot types)
- `tools/autotest/scenarios/tournament-river-zeta-2p/` — first tournament scenario (map files + tournament.json)
- `tools/autotest/run-tournament.sh` — batch runner
- `tools/autotest/tournament/aggregate.sh` — CSV/summary writer
- `tools/autotest/tournament/loop.sh` — autonomous loop (Phase 4)
- `engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs` — verdict trait
- `engine/OpenRA.Game/Graphics/NullRenderer.cs` — Phase 2

### Modified
- `engine/OpenRA.Game/TestMode.cs` — accept Test.TournamentConfig extra arg
- `engine/OpenRA.Game/Settings.cs` — Renderer=Null + skip-audio/cursor wiring
- `engine/OpenRA.Game/Game.cs` — headless path

### Touched lightly
- `WORKSPACE/ai/README.md` — link to this plan
- `WORKSPACE/HOTBOARD.md` — add tournament harness in-flight entry

## Risks and open questions

### Risks

1. **OpenRA reentrancy on macOS.** Multiple game instances may collide on user dir / SDL state. Phase 3 might need engine plumbing (`--profiledir`). Mitigation: prototype with N=2 first.
2. **Game doesn't naturally end.** If both bots stalemate forever (no SR capture, units reach steady-state), the time-limit watcher must definitely fire. Mitigation: hard time-limit at engine-tick level, not game-second level.
3. **Map bias.** A single map will favor one spawn. Mitigation: alternate `p1_bot`/`p2_bot` assignments across seeds (half the seeds run mirrored), or build a multi-map tournament pool.
4. **Headless rendering changes game state.** OpenRA may have rendering side effects on simulation (it shouldn't but…). Mitigation: Phase 2 has a verification step — same seed in both modes must produce identical end state.
5. **Long-term double maintenance.** `ModularBot@legacy` + `ModularBot@v2` means two bot configs to keep alive. Mitigation: explicit policy in `WORKSPACE/ai/README.md` — once V2 is committed-to-ship, legacy enters frozen mode (no further changes, removed at v1.1 or once V2 stable for a release).

### Open questions

1. **Match length.** 12 minutes game time is a guess. Real games often run 20+ min. Decide after the first batch — too short = score noise, too long = wall-clock cost.
2. **Exact score formula.** The weights above are placeholders. Likely needs tuning so a clearly-winning game scores 2–3× a slight-edge game, not 100×.
3. **Tournament map pool.** Start with one map (river-zeta-2p), add `woodland-warfare-2p` after Phase 1 ships. Keep tournament maps separate from production maps (different folder?) so balance edits don't perturb the benchmark.
4. **Bell + summary block format for milestones.** Reuse the standard end-of-message glyph block from CLAUDE.md, embed in a `milestone_*.md` file under the result dir.
5. **Should personality differentiation tests share the harness?** Yes — same scenario format, different `matchup` field (`{ p1_bot: "rush", p2_bot: "turtle" }`). Plan accommodates this but Phase 1 only exercises legacy-vs-legacy.

## What this unlocks

Once Phase 1 ships:
- Every AI-overhaul commit can be measured against the previous baseline before merging.
- "Did this change actually help?" stops being playtester intuition.
- Long autonomous tuning runs become a normal collaboration mode — the user can ask "tune the strategic planner weights and bell me at 65% winrate" and walk away.

Once Phases 2–4 ship:
- Hours-of-experiments-per-minute throughput.
- Cross-version regression suite that runs nightly.
- Replays + diagnostics make the difference between "the new bot wins" and "the new bot wins *because* it flanked correctly."
