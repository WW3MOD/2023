# Sanity-Check Batch Findings — overnight 260512

> Findings from the legacy-vs-legacy 10-seed batch on
> tournament-arena-skirmish-2p with 60-sim-second matches at 8× speed.

**Status:** TBD — will be filled in as soon as the batch completes.
This doc auto-completes from `summary.json` of the latest batch
directory (`tools/autotest/tournament-results/<latest>/summary.json`).

## What to read

If this doc is still mostly blank when you wake up, the batch was
still running (or got killed without writing summary). Look at
`tools/autotest/tournament-results/<most-recent>/`:

- `match_*.json` — per-match verdicts (the ones that completed)
- `summary.csv` — one row per match if aggregate-tournament.sh ran
- `summary.json` — aggregate stats if the aggregator ran
- `match_*.log` — engine stdout per match (useful if a match crashed)
- `match_*.watcher.log` — tick-by-tick score progression for each match

## How to manually run aggregate if it wasn't called

```bash
./tools/autotest/aggregate-tournament.sh \
    tools/autotest/tournament-results/<batch-dir-name>
```

## Expected verdict shape

```json
{
  "total_matches": 10,
  "verdict_count": <int>,
  "fail_count": <int>,
  "side_winrate_pct": { "USA-bot": <pct>, "Russia-bot": <pct> },
  "score_ratio_stats": { "n": ..., "mean": ..., ... },
  "winner_counts": { "USA-bot": <n>, "Russia-bot": <n> },
  ...
}
```

## How to interpret

| Pattern | What it means |
|---|---|
| `side_winrate_pct` near 50/50 | map fair; harness ready |
| 60-70% one side | mild bias; useful but interpret with care |
| >75% one side | strong bias; need mirror-matching |
| `fail_count > 50%` | match length too long for wall-clock budget; reduce TimeLimitSeconds |
| `score_ratio_stats.mean > 5×` | matches consistently lopsided; AI behavior heavily favored one side |

## Findings (TBW)

*Wait for batch completion + read here.*
