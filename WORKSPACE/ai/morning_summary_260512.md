# Morning Summary — Autonomous Overnight Run (260511 → 260512)

> **Read this first.** Live log of what I tried while you slept. 11 commits
> with `ai: tournament` prefix landed.

**Status:** COMPLETE. Sanity batch ran, findings written to
`sanity_findings_260512.md`. Headline: USA-bot 67% / Russia-bot 33% over
6 valid matches (4 of 10 killed by mid-batch engine rebuilds). The
harness works end-to-end; the 67/33 winrate is a mild-bias signal to
investigate with a larger sample (recommended n=30) before reporting AI
changes.

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

### Round 10 — Parallel-batches bug discovery
*Status: caught and corrected.*

**Bit:** my `pkill -KILL -f 'run-tournament|dotnet bin/OpenRA'` between
batch restarts didn't reliably kill the parent shell scripts. **Three
batches accumulated** running in parallel, competing for CPU. That's why
matches were so slow — 14 ticks/sec wall-clock when 8× speed should
deliver 100-200+. Once cleaned up (single batch), CPU per process climbed
to 100%+ and matches finished much faster.

**Lesson:** before restarting a batch, run
```bash
pgrep -fl 'run-tournament|dotnet bin/OpenRA'
```
and confirm only the expected processes remain. Documented in
PITFALLS.md addition (not yet committed).

### Round 11 — Quick batch with single-process CPU
*Status: running with cleaned state. Findings in `sanity_findings_260512.md`.*

10 seeds, 60-sim-second matches at 8× speed, framerate cap on. Started
fresh in `260512_0024_*` after killing all parallel batches. Wall-clock
per match should drop substantially compared to the 3-batch parallel
state.

## Files in this session (overnight 260511→260512)

### Engine changes
- `engine/OpenRA.Mods.Common/Tournament/*` — match harness module (10 files)
- `engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs` — world trait
- `engine/OpenRA.Game/TestMode.cs` — `Test.{TournamentConfig,GameSpeed,RandomSeed,SpeedMultiplier}` launch args
- `engine/OpenRA.Game/Game.cs` — `Test.GameSpeed` honored in `LoadMap`
- `engine/OpenRA.Game/Server/Server.cs` — `Test.RandomSeed` overrides DateTime-based seed

### YAML
- `mods/ww3mod/rules/ai/ai.yaml` — `ModularBot@v2` + swap conditions
- `mods/ww3mod/rules/world.yaml` — `BotVsBotMatchWatcher` registration

### Shell harness
- `tools/autotest/run-tournament.sh` — batch runner
- `tools/autotest/aggregate-tournament.sh` — CSV + summary stats
- `tools/autotest/loop-tournament.sh` — autonomous-loop scaffold (Phase 4)

### Scenarios
- `tools/autotest/scenarios/tournament-arena-skirmish-2p/` — first scenario
  (map.yaml, rules.yaml, tournament.yaml, tournament-smoke.yaml,
   tournament-sanity.yaml, tournament-quick.yaml)
- `tools/autotest/scenarios/tournament-arena-diagonal-2p/` — second scenario
  (clone with diagonal SR placement)

### Docs
- `WORKSPACE/ai/foundation_260511.md` — basics doc (Phase 1 day)
- `WORKSPACE/ai/tournament_swap_guide.md` — how to swap every piece
- `WORKSPACE/ai/PITFALLS.md` — 17 traps already hit (and growing)
- `WORKSPACE/ai/phase1_status_260511.md` — Phase 1 snapshot
- `WORKSPACE/ai/morning_summary_260512.md` — this doc
- `WORKSPACE/ai/WAKEUP_CHECKLIST_260512.md` — read-on-wake guide
- `WORKSPACE/ai/sanity_findings_260512.md` — batch findings (filled when complete)
- `WORKSPACE/plans/260511_ai_tournament_harness.md` — full plan
- `DOCS/reference/supply-route.md` — SR mental model (corrected per user)
- `WORKSPACE/HOTBOARD.md` — entry pointing here
- Cross-session memory: `~/.claude/.../feedback_supply_route_model.md`

## Recommendation for what to try first when you wake up

See `WORKSPACE/ai/WAKEUP_CHECKLIST_260512.md`. TL;DR:

1. `git log --oneline -25` — see the commit chain
2. Read `WORKSPACE/ai/morning_summary_260512.md` (this doc)
3. Check `tools/autotest/tournament-results/<latest>/summary.json`
4. Decide whether to keep or revert any rounds (most are isolated)
5. If sanity batch passes (40-60% winrate per side), the harness is ready
   for the actual AI overhaul work (Phase 2 of `foundation_260511.md`).
