# Tournament Harness — Workflow Cookbook

> Reference for "how do I use the tournament harness to do X?"
> Each recipe is one bash command + one paragraph of why.
> See `tournament_swap_guide.md` for how to *modify* the harness; this doc is
> for using what's already built.

## Smoke test — verify the harness still works (30 sec)

```bash
./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p \
    --seeds 1 \
    --config tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-smoke.yaml
```

Single 30-sim-sec match. ~20 sec wall-clock. Use after any engine change to
confirm scoring + verdict writing still work end-to-end. Output:
`tools/autotest/tournament-results/<latest>/match_1.json` should have
`status: pass` and contain a `winner_name` field.

## Quick benchmark — small batch for AI A/B testing (5-10 min)

```bash
./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p \
    --seeds 10 \
    --config tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-quick.yaml
```

10 matches × 60 sim-sec each, ~30-60s wall-clock per match. Use to detect
mid-range AI changes. Statistical confidence is moderate (~10 matches has a
~20% CI on winrate).

## Full benchmark — strong statistical signal (20-30 min)

```bash
./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p \
    --seeds 30 \
    --config tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-sanity.yaml
```

30 matches × 3 sim-min each (longer matches let bots actually engage and
trade losses). Use to confirm a real AI change's effect.

## Mirror-paired benchmark — attribute bias to faction vs position

```bash
./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p \
    --seeds 20 \
    --config tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-quick.yaml \
    --mirror tournament-arena-mirror-2p
```

Even seeds run primary scenario (USA-bot=america, Russia-bot=russia).
Odd seeds run mirror (USA-bot=russia, Russia-bot=america).
Compare faction winrate against position (player name) winrate — the gap
between them tells you whether the bias is positional or factional.

## Compare two batches

```bash
./tools/autotest/compare-batches.sh <batch-dir-A> <batch-dir-B>
```

Side-by-side report of side_winrate, faction_winrate (when verdict has the
field), score ratios, durations. Auto-detects mirror-pair batches and
prints the position-vs-faction interpretation hint.

## Autonomous milestone-driven loop (overnight tuning)

```bash
# 1. Write your target.yaml (see tools/autotest/example-target.yaml)
# 2. Launch:
./tools/autotest/loop-tournament.sh tournament-arena-skirmish-2p \
    tools/autotest/example-target.yaml
```

Runs N rounds × M matches/round until your StopThreshold is met or
BudgetHours/MaxRounds is exhausted. Each round writes a per-round result
dir. `loop_progress.csv` shows the per-round winrate trajectory. Terminal
bells on goal-met or large winrate swings.

## Reproduce a specific match for debugging

```bash
# A match's seed is encoded in its launch args:
#   Test.RandomSeed = (seed_index × 1000 + 17)
# So match_7 in any batch uses RandomSeed=7017.
./launch-game.sh \
    "Launch.Map=tournament-arena-skirmish-2p" \
    "Test.Mode=true" \
    "Test.Name=repro-match7" \
    "Test.ResultPath=/tmp/repro.json" \
    "Test.TournamentConfig=$(pwd)/tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-quick.yaml" \
    "Test.GameSpeed=fastest" \
    "Test.SpeedMultiplier=8" \
    "Test.RandomSeed=7017" \
    "Graphics.Mode=Windowed" \
    "Sound.Mute=true"
```

Drop the `Test.SpeedMultiplier=8` to watch at real speed. Drop
`Graphics.CapFramerate=true` for full FPS rendering.

## Switch the scorer or win rule

Edit `tournament.yaml` in your scenario:

```yaml
Scorer: weighted_components       # or your new registered IMatchScorer name
WinRule: score_or_sr_capture      # or your new registered IWinRuleEvaluator name
```

Add new scorers/win rules per `tournament_swap_guide.md`. No watcher code
changes needed.

## Quickly run a single seed (debugging the harness, not the bots)

```bash
./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p \
    --seeds 1 \
    --config tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-smoke.yaml
```

Match completes in ~20s wall-clock. Verdict JSON in result dir. Useful to
verify watcher initialization, score format, etc.

## Common mistakes

- **Running `make all` during a batch.** Mid-launch matches die from
  `FileNotFoundException: OpenRA.Mods.Cnc.dll`. Wait for batch to finish
  before rebuilding. See PITFALLS.md §11.
- **Restarting a batch without verifying clean process state.** `pgrep -fl
  'run-tournament|OpenRA'` first. Multiple parallel batches halve each
  other's CPU. See PITFALLS.md §18.
- **Trusting absolute winrate.** Use deltas between batches (via
  `compare-batches.sh`). Map / faction bias means absolute winrate isn't
  the AI's fault.
- **Wall-clock budget too low.** `--max-wall-secs` should be at least 2-4×
  the per-match sim seconds. Late-game matches with many actors run
  slower than empty early-game ones.

## Files this workflow touches

- `tools/autotest/run-tournament.sh` — main batch runner
- `tools/autotest/aggregate-tournament.sh` — CSV + summary.json
- `tools/autotest/compare-batches.sh` — A/B diff
- `tools/autotest/loop-tournament.sh` — autonomous loop
- `tools/autotest/scenarios/tournament-*/` — scenario folders
- `tools/autotest/tournament-results/<batch>/` — per-batch output
  - `match_*.json` — per-match verdicts
  - `match_*.watcher.log` — tick-by-tick score progression
  - `summary.json` + `summary.csv` — aggregate
  - `batch.meta.json` — git SHA, scenario, config, etc.
