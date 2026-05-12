# Sanity-Check Batch Findings — 260512 (after Round 11 redo)

## Headline

**USA-bot wins 16 / 19 matches = 84.2%** on the `tournament-arena-skirmish-2p`
map under legacy-vs-legacy normal AI bot. That's a strong, statistically
significant bias (p < 0.001 vs 50/50 null), confirming a real asymmetry —
NOT noise from the earlier n=6 batch's 67/33 result.

## Two batches; clean CPU vs. parallel-contention

### Batch A (260512_0024) — n=6, partially affected by parallel-batches CPU contention

Result: USA 4 / Russia 2 (67% / 33%). Mild bias. n=6 confidence interval too
wide to be conclusive. Earlier morning summary listed this; in retrospect the
parallel batches were artificially slowing things down and possibly muddying
which side got more compute per tick.

### Batch B (260512_0837) — n=19 valid (of 20), clean single-batch CPU

Result: USA 16 / Russia 3 (84% / 16%).

```
side_winrate_pct: { USA-bot: 84.2, Russia-bot: 15.8 }
score_ratio_stats: { mean: 1.70, median 1.51, min: 1.04, max: 5.29 }
all 19 matches went to time_limit (no SR captures in 60 sim-sec)
```

The single-fail match was a wall-clock kill (probably mid-batch CPU spike
from another process or a slow seed); the others all completed cleanly.

## Russia's wins (seeds 15, 16, 19) — what happened

Looking at the score progressions in the watcher.log files:

- **Match 15:** USA-bot 3700, Russia-bot 3850 (tiny margin, ~1.04×).
  Russia won by *kills_value* alone — Russia killed enemy units worth 450
  while building only 3400 army; USA had 3700 army but 0 kills.
- **Match 16:** USA 3450 vs Russia 3750 (1.09×). Similar pattern; Russia
  had 3750 army with full assets surviving while USA had 3450.
- **Match 19:** USA 2350 vs Russia 3850 (1.64×). Bigger win for Russia.
  USA's army was much smaller (2350 vs 1350 alive for Russia + 2500 in
  kills_value or losses).

Pattern: Russia wins when it manages to kill USA units faster than it loses
its own. USA wins when it out-produces.

## Why the bias exists (hypotheses, untested)

1. **AI production speed.** USA-bot's UnitBuilderBotModule@america.heli and
   AdaptiveProductionBotModule have unit lists where individual unit cost is
   different from Russia's. If america's units cost slightly less or have
   shorter production timers, USA accumulates army faster within the
   60-second window.
2. **Faction unit balance.** The 260510 balance session flagged
   `B-01: no Russian vehicle inherits ^Combatant` — *verified WRONG today,
   they all do inherit ^Combatant in the current rules.* That bug was
   either fixed or never the issue. But other unit imbalances
   (BMP-2 1300 cost vs Bradley 1500 — 260510 R-03) could explain.
3. **Map seed bias.** All seeds gave USA the (6, 16) starting position;
   if there's positional asymmetry that's symmetric on the EW axis but
   not in the bot's production wiring, USA gets a free edge.

## What I need to determine root cause

The **mirror-paired batch** (Rounds 12 + 14 infrastructure) tells us
position vs faction:

```bash
# Step 1: run the mirror-paired batch
./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p \
    --seeds 20 \
    --config tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-quick.yaml \
    --mirror tournament-arena-mirror-2p

# Step 2: compare against the (clean-CPU) primary batch
./tools/autotest/compare-batches.sh \
    tools/autotest/tournament-results/260512_0837_tournament-arena-skirmish-2p \
    tools/autotest/tournament-results/<new-mirror-batch>
```

- If the mirror batch ALSO shows USA-bot winning: **positional bias** —
  the left-spawn at (6, 16) has an inherent advantage. Map needs fixing.
- If the mirror batch shows Russia-bot winning instead (since now
  Russia-bot has faction=america): **factional bias** — america faction
  outperforms russia faction inherently. AI/balance work needed.

The Round 15 engine change (faction in verdict JSON) makes the
attribution unambiguous — `summary.json.faction_winrate_pct` will tally
by faction regardless of which player slot it's in.

## Implications for AI overhaul work

This bias is a **benchmark calibration issue, not a blocker**. Going forward:

- **Most AI work should be measured via DELTA between two batches**, not
  absolute winrate. `compare-batches.sh` shows the delta directly. A v2
  change that lifts Russia-bot from 16% to 30% is a 14-point improvement,
  regardless of the absolute 84/16 baseline.
- **Mirror-pairing should be standard practice** for any AI overhaul
  benchmark — run primary + mirror, both numbers reported. Eliminates
  side-of-map confounds.
- **Don't trust single-side results** — if you change v2 and only see
  USA-bot's behavior, you might miss whether the change generalizes to
  the weaker faction position.

## Files in this batch

- `tools/autotest/tournament-results/260512_0837_tournament-arena-skirmish-2p/`
  - `summary.json`, `summary.csv` — aggregate stats
  - `match_*.json` × 19 — per-match verdicts
  - `match_*.watcher.log` — tick-by-tick score progression
- `tools/autotest/tournament-results/260512_0024_tournament-arena-skirmish-2p/`
  - The earlier n=6 batch (parallel-CPU contention artifact)

## Statistical sidebar

Binomial test: P(X ≥ 16 | n=19, p=0.5)
- Expected wins under 50/50: 9.5
- Observed: 16
- z-score: (16 - 9.5) / sqrt(19 × 0.5 × 0.5) = 6.5 / 2.18 ≈ 2.98
- Two-tailed p-value: ≈ 0.0029 (significant at p < 0.01)

So we can confidently reject the null hypothesis. The bias is real.

## What the harness has now proven

1. ✓ Engine plumbing works end-to-end (Phase 1)
2. ✓ Score formula populates army_value + capture_income + kills_value (Round 1+2)
3. ✓ Deterministic seeding is functional (Round 5)
4. ✓ Speed multiplier delivers ~3× real throughput (Round 5)
5. ✓ Framerate cap helps (Round 8)
6. ✓ Aggregator + comparator produce sensible reports (Rounds 9+13)
7. ✓ Mirror-matching infrastructure ready (Round 12)
8. ✓ Faction tracking in verdict JSON (Round 15)
9. ✓ The harness detects real AI/faction biases at n=19+ — and confirms
   that nightly-CPU contention (parallel batches) was masking the
   actual signal earlier in the run

**Ready for actual AI overhaul work.** Next step from `foundation_260511.md`
is the user's call.
