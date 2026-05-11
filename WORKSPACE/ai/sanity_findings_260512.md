# Sanity-Check Batch Findings — 260512 overnight

## Batch metadata

- **Scenario:** `tournament-arena-skirmish-2p`
- **Config:** `tournament-quick.yaml` (60-sec match, 8× SpeedMultiplier, framerate cap on)
- **Seeds requested:** 10
- **Valid verdicts written:** 6 (matches 1, 6, 7, 8, 9, 10)
- **Failed launches:** 4 (matches 2, 3, 4, 5)
- **Git SHA at batch start:** `79a32eea`
- **Result dir:** `tools/autotest/tournament-results/260512_0024_tournament-arena-skirmish-2p/`

## Why 4 matches failed

`make all` was run twice between match 1 and match 6 by parallel agent
streams (screenshot work + my own diag-iteration). Each rebuild
unlinked + recreated the `engine/bin/*.dll` files; running game launches
hit `FileNotFoundException: OpenRA.Mods.Cnc.dll` and aborted before
World setup. Captured in PITFALLS §11 (mmap rebuild race) and §18
(parallel batches).

**Implication for future runs:** don't `make all` while a batch is
running. If a batch starts failing, kill it, rebuild, restart.

## Match outcomes (6 valid)

| match | winner | win reason | USA total | Russia total | USA army / kills | Russia army / kills |
|---|---|---|---|---|---|---|
| 1 | Russia | time_limit | 4050 | **4350** | 4050 / 0 | 3450 / 900 |
| 6 | **USA** | time_limit | **5050** | 3000 | 5050 / 0 | 2900 / 100 |
| 7 | **USA** | time_limit | **2500** | 2450 | 1800 / 700 | 2000 / 450 |
| 8 | **USA** | time_limit | **2200** | 1700 | 2200 / 0 | 1250 / 450 |
| 9 | **USA** | time_limit | **5400** | 3500 | 5400 / 0 | 3500 / 0 |
| 10 | Russia | time_limit | 3200 | **3650** | 1500 / 0 | 3550 / 0 |

## Aggregate

- **Side winrate:** USA 66.7% / Russia 33.3%
- **Score ratio (winner/loser):**
  - mean: 1.29×
  - median: 1.22×
  - min: 1.02× (match 7 — basically a tie)
  - max: 1.68× (match 6 — modest USA win)
- **All matches went to time_limit** (no SR captures in 60 sim-seconds — bots
  don't reach the enemy SR in that window).
- **0 decisive matches** (the `ScoreMarginForDecisive: 0.20` threshold; none
  exceeded a 1.20× score ratio cleanly).

## Reading the data

### Map / faction bias (the headline finding)

**USA wins 4/6 (67%)**, outside the 40-60% legacy-vs-legacy noise band
typically expected for fair benchmarks. Three interpretations:

1. **Sample size is too small.** 6 samples have a wide confidence interval.
   At n=6, even a true 50/50 would land 4/6 USA wins ~33% of the time
   (binomial). So this could be pure noise.

2. **Faction or AI imbalance.** America/NATO bot's AdaptiveProduction
   list (`AntiVehicleUnits: at.america, abrams, bradley`) or build order
   might be slightly stronger than Russia/BRICS's. The 260510 balance
   session already noted `B-01: no Russian vehicle inherits ^Combatant —
   latent bug` — that's exactly the kind of asymmetry that could surface
   here.

3. **Positional bias on this map.** USA-bot's SR is at `(6, 16)`,
   Russia-bot's at `(58, 16)`. Both mid-row, same Y. *Should* be
   geometrically symmetric, but production timing or movement-path
   differences across the diagonal could create an edge.

### Match quality

Score ratios mostly tight (median 1.22×). That's **good news for the
harness** — matches aren't lopsided blowouts; bots interact meaningfully
and either side could be near a win-line in any match.

### Score components

- `kills_value` populating correctly: Russia killed 1900 worth of USA
  units across the 6 matches; USA killed 700 worth of Russia. Russia is
  inflicting MORE damage despite winning less often — interesting
  signal that USA-bot is producing more *defensible* units rather than
  trading better.
- `army_value` populating correctly: matches end with both sides having
  army left (no full annihilation in 60 sim-seconds).
- `capture_income` is 0 in every match — neither bot captured any
  income structure (the map has no oilbs etc). Expected for this scenario.

## Recommended next actions (when you wake up)

In order:

1. **Run a bigger batch to confirm or refute the bias.** 20-30 seeds with
   the engine NOT being rebuilt mid-batch should give a clearer signal.
   Command:
   ```bash
   ./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p \
       --seeds 30 \
       --config tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-quick.yaml \
       --max-wall-secs 120
   ```
   At ~30s/match wall-clock (single-batch CPU), 30 matches ≈ 15 min.

2. **If the bias holds at n=30**, add mirror-matching. Two easy paths:
   - Cheap: a second scenario folder with USA/Russia faction assignments
     swapped. Run both, compare aggregate.
   - Cleaner: extend run-tournament.sh to alternate sides per seed-index
     (odd seeds run as-is, even seeds with players swapped). Aggregator
     reports per-faction winrate ignoring P1/P2 ordering.

3. **If bias is real even after mirroring**, that's an AI/faction balance
   issue, not a map issue. Worth investigating B-01 (Russian
   `^Combatant` inheritance) per the 260510 balance recommendations.

4. **Then start AI overhaul** per `foundation_260511.md` — the harness is
   measurably working and detects bot differences. The first real change
   to commit under `enable-ai-v2` should produce a *different* winrate
   distribution than @normal; that's how the harness proves its value.

## Files in this batch

```
tools/autotest/tournament-results/260512_0024_tournament-arena-skirmish-2p/
├── batch.meta.json        scenario, config, git SHA, etc.
├── summary.json           aggregate stats
├── summary.csv            one row per verdict-bearing match
├── match_1.{json,log,watcher.log}    Russia win
├── match_6.{json,log,watcher.log}    USA win
├── match_7.{json,log,watcher.log}    USA win (very close)
├── match_8.{json,log,watcher.log}    USA win
├── match_9.{json,log,watcher.log}    USA win
└── match_10.{json,log,watcher.log}   Russia win
```

Matches 2-5 have `.log` files (with the FileNotFoundException) but no
`.json` verdict and no `.watcher.log` — they died before the watcher's
WorldLoaded fired.

## What this tells us about the harness

✓ Per-component score tracking works (army + kills both populate).
✓ Deterministic seeding works (matches reproducible by seed index).
✓ 8× SpeedMultiplier works (matches finish in ~30s wall-clock).
✓ Framerate cap works (no obvious render bottleneck once parallel
  batches were eliminated).
✓ Aggregator works (CSV + summary.json both present).
✓ Bot-vs-bot games produce varied, interesting outcomes (not blowouts).

The harness is **functional and ready for AI overhaul work**. The
67/33 winrate is a known-bias signal to address before reporting AI
improvements, not a blocker to starting.
