# Wake-Up Checklist — overnight 260511→260512

> Read order when you wake up. Each step is ~2 min.

## 1. `git log --oneline -25` (1 min)

You should see ~9 commits prefixed `ai: tournament Round N`. Plus a parallel
stream of `screenshot: phase 1.x` commits from another agent. Both streams
are additive; no conflicts.

If the count is much lower than 9, something blew up and I bailed out early —
read `WORKSPACE/ai/morning_summary_260512.md` for what happened.

## 2. `WORKSPACE/ai/morning_summary_260512.md` (5 min)

Live log of the overnight run. Each round documents: what was tried,
empirical results, what works, what doesn't.

Key rounds to skim:
- **Round 1+2** — score formula completion (army + income + kills) via
  `PlayerStatistics` reads. Already validated working in a smoke.
- **Round 5** — deterministic seeding + 8× SpeedMultiplier (the real
  game-speed mechanism). Empirical 3× wall-clock improvement.
- **Round 6** — 20-seed sanity batch (status when I stopped is in the doc).
- **Round 8** — render-framerate cap; "headless lite" launch args.

## 3. `tools/autotest/tournament-results/<most-recent>/summary.json` (2 min)

If the sanity batch completed, this file holds the verdict per-side winrate
distribution. Look at `side_winrate_pct` —

- **40-60% per side** = map is fair. Harness ready to measure AI changes.
- **70%+ skew to one side** = map (or faction) is biased. Mirror-matching
  is the fix (run each seed twice with sides swapped).
- **fail_count > 0** = some matches hit wall-clock kill (rendering bottleneck
  in mid-late game). Acceptable for sanity-check purposes; for real
  benchmarking, raise `--max-wall-secs` or shorten match length.

## 4. Spot-check a watcher.log (3 min)

Pick any match_*.watcher.log in the latest result dir. Read the score
progression. You should see USA-bot and Russia-bot scores diverging,
recombining, sometimes one totally beating the other. **The matches feel
like matches** — not deterministic blowouts.

## 5. Decide whether to revert anything (5-10 min)

Likely candidates for revert / rework if something's wrong:

- **Render-framerate cap (Round 8, commit `8957ad3a`)** — if mid-late
  matches still hit wall-clock kills with this cap, the cap isn't helping
  enough. Roll back and try a different approach (more sim threads?
  drop watcher diag-write frequency?).
- **8× SpeedMultiplier (Round 5, commit `d77663a3`)** — if matches behave
  unexpectedly (e.g., AI decisions feel off), the underlying simulation
  might not handle 5ms timesteps. Roll back to 2-4× and remeasure.
- **Diagonal second map (Round 7, commit `cf76049a`)** — purely additive,
  zero risk; reversion only if you don't want it in the tournament pool.

The morning summary's "what works / what doesn't" lines should make
revert/keep decisions obvious.

## 6. What to do next

In order of payoff:

- **(a) If sanity batch hit 40-60% winrate** → harness is ready. Start the
  first real AI work: pick something from `foundation_260511.md`'s phasing
  and implement it under `enable-ai-v2` condition. Measure it.
- **(b) If batch is biased** → fix the map (mirror seeds, swap sides, or
  tweak SR positions) before doing AI work.
- **(c) If wall-clock per match is still too slow** → either Phase 2
  (real headless renderer) is unavoidable, or the framerate cap needs
  to go lower. Try `Graphics.MaxFramerate=1` first.

## 7. The parallel stream (screenshot evaluation)

The other agent has been landing `screenshot: phase 1.x` commits. That
work is *complementary* — it builds infrastructure to take screenshots of
bot matches for visual evaluation. Their commits don't conflict with
tournament work. Don't revert them by accident; they're separate scope.

Plan doc for the screenshot stream is in commit `7785bb83`. Read that if
you want context on what's being built there.

## 8. Pending decisions still on the table

From `foundation_260511.md` §7 — these blocking questions weren't resolved
during the overnight run:

1. Foundation reset aggressiveness (re-ask once you've seen what the
   harness measures).
2. Difficulty levels for the lobby (N tiers? Naming?).
3. Honest fog by default vs. omniscient AI.
4. Waypoint/Planning mode coupling.
5. Per-map opening books.
6. Allied AI behavior in coop.

The harness is built so these decisions can be deferred — none block
starting Phase 2 AI work.
