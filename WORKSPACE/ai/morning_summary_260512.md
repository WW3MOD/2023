# Morning Summary — Autonomous Overnight Run (260511 → 260512)

> **Read this first.** Live log of what I tried while you slept. Updated after
> each round.

**Status:** in progress — round 1 (score formula completion) started.

**Starting point:** commit `79d0dff5` (`ai: tournament Phase 1 GREEN`). Phase 1
end-to-end working. Score formula has only `army_value` populated; capture
and kills components present-but-zero. Game runs at 1× real time.

**Plan executing:**
1. Score formula completion (capture_income, kills_value events)
2. Game-speed acceleration
3. Sanity-check 20-seed batch
4. Additional tournament map
5. Phase 4 autonomous loop scaffolding
6. Headless renderer investigation
7. This summary doc, finalized

## Round log

### Round 1 — Score formula completion + game-speed acceleration
*Status: complete pending smoke validation.*

**What was done.** Discovery: `PlayerStatistics` (already attached to every
player) tracks `KillsCost`, `ArmyValue`, and `Income`. `PlayerResources.Earned`
is the cumulative cash earned. The scorer was rewired to **read these directly**
instead of hooking my own `INotifyKilled` / `INotifyOwnerChanged` traits —
saves a YAML pass on every actor + removes a parallel-tracking burden.

**Side-effect:** `MatchTrackingState.CaptureIncome` and `KillsValue` dicts in
`MatchTypes.cs` are now unused. Left in place for now; a follow-up commit can
remove them if no future scorer wants them.

**Game-speed acceleration:** added `Test.GameSpeed=<key>` launch arg. The mod
defines six speeds in `engine/mods/ra/mod.yaml` GameSpeeds — `fastest` is
20 ms/tick = 2× default. `Game.LoadMap` now honors the override on the initial
`option gamespeed` setup order. Tournament config gains a `GameSpeed:` field;
`run-tournament.sh` extracts it and threads it through.

**Smoke `tournament-smoke.yaml` updated** to use `GameSpeed: fastest`.
Theoretical wall-clock for a 30-sec match: 15 wall-clock-seconds + ~10s
engine init.

**PITFALL discovered:** invalid gamespeed keys silently fall back to default
with no error (engine quirk). Documented inline at the LoadMap call site.

### Round 2 — game-speed validation
*Status: validated.* 30-sec match wall-clock dropped 57s → 42s with
`GameSpeed: fastest`. ~26% reduction; ~50% expected for 12-min matches once
engine init (~30s) becomes a smaller fraction. Cap is 2× — that's intrinsic
to the mod's GameSpeeds Timestep config. Headless rendering (Phase 2) would
let the engine SATURATE the timestep without slowing for rendering but won't
break the 2× ceiling.

### Round 3 — 20-seed sanity-check batch
*Status: running in background.*

`tournament-sanity.yaml` config: 3-min matches at fastest game speed; 20 seeds
legacy-vs-legacy. Expected wall-clock ≈ 40 min. Validates that the map isn't
positionally biased before any AI work measures against this benchmark.

Output: `tools/autotest/tournament-results/<timestamp>_tournament-arena-skirmish-2p/`
with `summary.csv` + `summary.json`. Round 3 deliverable: a findings doc that
reports winrate, decisive %, and recommends whether the map is fit for use.

### Round 4 — Autonomous loop scaffold
*Status: scaffold landed; condition-evaluation deferred to a future session.*

`tools/autotest/loop-tournament.sh` — orchestrates multi-round runs from a
target.yaml config. Reads scenario, config, BatchSize, BudgetHours; runs
rounds in sequence; writes a per-round result dir. Stop-condition and
milestone-trigger evaluation is documented but not yet implemented (the
shell scaffold just runs N rounds until budget exhausted).

Example target file at `tools/autotest/example-target.yaml`.

Phase 4 v2 (a future session) wires in the metric-comparison logic to
actually stop on goal-met and to bell-the-user on milestone hits.

### Round 5 — Deterministic seeding + REAL game-speed (8×)
*Status: complete.* Big win.

**Discovery** (mid-Round 3): `option gamespeed fastest` only delivers 2× —
that's the cap in WW3MOD's GameSpeeds.Timestep config. The in-game
`SpeedControlButton` (cheat-mode button) goes up to 8× by setting
`world.Timestep` directly.

**Fix:** new `Test.SpeedMultiplier` launch arg honored by
`BotVsBotMatchWatcher.WorldLoaded`. Same mechanism as the cheat button.
Range 1..16; 8× recommended (caps at the SpeedControlButton's range).

**Empirical:** 30-sec match wall-clock 57s → 19s at 8× = **3× practical
speedup**. The renderer is the bottleneck even with 8× sim target, so real
gains are 3-4× not 8×. Headless rendering (Phase 2) would push this further.

**Also:** `Test.RandomSeed=<int>` arg honored by Server.cs. Each match's
seed = `index × 1000 + 17`. Same seed + same code + same map → reproducible
match. Debug-an-outlier-match path now exists.

### Round 6 — 20-seed sanity batch
*Status: running (restarted with framerate cap, --max-wall-secs 200).*

20 seeds of legacy-vs-legacy on tournament-arena-skirmish-2p, 3-min matches
at 8× SpeedMultiplier + framerate cap. Expected wall-clock ≈ 30-50 min.

Key sub-question: **the very first match (default speed) showed USA=12150 vs
Russia=550** — extreme bias. Either this map has positional bias OR the AI's
faction differs significantly. The 20-seed batch tells us.

The first attempt (8× speed only, no framerate cap, --max-wall-secs 90) hit
the wall-clock kill on match 2 — the simulation was getting bottlenecked by
the 60-FPS renderer. Restarted with the new framerate cap + 200s budget.

If consistent bias persists, the right fix is mirror-matching: each seed
runs twice with sides swapped, then aggregate.

### Round 7 — second tournament map (diagonal layout)
*Status: complete.*

`tournament-arena-diagonal-2p` — clone of arena-skirmish-2p with SRs at NW
(6,4) and SE (58,28) corners instead of mid-rows. Same terrain
(map.bin reused), different SR placement. Useful for cross-map AI
validation once Phase 2+ AI work starts.

### Round 8 — render-framerate cap ("headless lite")
*Status: complete; NOT yet empirically validated.*

Realisation during Round 6's wall-clock-kill failure: the renderer is the
practical bottleneck. A full headless renderer is days of work; the
**cheap 90% solution** is just to cap render FPS:

  Graphics.CapFramerate=true Graphics.MaxFramerate=5

These launch args are now baked into every run-tournament.sh launch. 5 FPS
instead of 60 = 12× less render-side CPU drag. Tournament-mode game windows
look janky during run (multi-second sim jumps per frame), which is fine for
batches and trivially reverted by dropping the args when inspecting a
specific match.

True headless (no rendering at all) is documented in PITFALLS §17 as a
much-bigger future investment.

### Round 9 — aggregator: side winrate + score-ratio distribution
*Status: complete.*

`aggregate-tournament.sh` now writes:
- `side_winrate_pct`: per-player-name winrate across the batch.
  Should be 40-60% per side under legacy-vs-legacy for a fair map.
- `score_ratio_stats`: distribution of winner_score / loser_score.
  High mean + low variance = decisive matches (bias suspected).

Both immediately visible in `summary.json` without rummaging through
match_*.json files.
