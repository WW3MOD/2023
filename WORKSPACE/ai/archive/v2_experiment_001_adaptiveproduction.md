# v2 Experiment #1 — AdaptiveProduction EvaluationInterval 600→300

> First real Phase 2 experiment per `WORKSPACE/ai/NEXT_STEPS.md`. Lazy
> path: tweak one YAML knob under `enable-ai-v2`, measure via
> mirror-paired batch. Goal is to validate the measurement loop and
> establish whether a single counter-build cadence change is detectable
> at n=20.

## Hypothesis

Halving `AdaptiveProductionBotModule.EvaluationInterval` (600 → 300)
makes v2 react to scouted enemy composition twice as fast. Counter-build
cycles fire every 12 sim-sec instead of every 24, so when a scout
spots heavy armor, v2 starts queueing AT units sooner.

**Predicted outcome:** v2 winrate > 50% across the mirror pair,
controlling for the baseline mild russia-side edge (~60/40 at n=20).
Specifically v2 should beat normal on the same faction.

## Change

In `mods/ww3mod/rules/ai/ai.yaml`:

```yaml
# Old AdaptiveProductionBotModule@america / @russia — gated to legacy only
AdaptiveProductionBotModule@america:
    RequiresCondition: enable-ai-legacy-only && player.nato   ← was enable-ai-any
    EvaluationInterval: 600
    ...

# New v2-only modules — same units list, halved interval
AdaptiveProductionBotModule@v2.america:
    RequiresCondition: enable-ai-v2 && player.nato
    EvaluationInterval: 300                                    ← was 600
    ...
```

Same for russia/brics. Other v2 modules unchanged (v2 still inherits
the `enable-ai-player` baseline normal modules: scout, garrison,
supply-follower, etc.).

## Methodology

Mirror-paired batch using new scenarios:

| Scenario | Position | Faction | Bot |
|---|---|---|---|
| `tournament-v2-vs-normal-2p` (primary) | USA-bot @ 6,16 | america | v2 |
|  | Russia-bot @ 58,16 | russia | normal |
| `tournament-v2-vs-normal-mirror-2p` | USA-bot @ 6,16 | russia | v2 |
|  | Russia-bot @ 58,16 | america | normal |

v2 stays in LEFT position both runs; only the faction it plays
swaps. So the v2 winrate averaged across both batches is "v2 winrate
across both factions, from the same position."

Command:

```bash
./tools/autotest/run-tournament.sh tournament-v2-vs-normal-2p \
    --seeds 20 \
    --config tools/autotest/scenarios/tournament-v2-vs-normal-2p/tournament-quick.yaml \
    --mirror tournament-v2-vs-normal-mirror-2p \
    --max-wall-secs 240
```

`tournament-quick.yaml`: TimeLimitSeconds=60, SpeedMultiplier=8,
scorer=weighted_components, winrule=score_or_sr_capture.

## Decision rule

After the batch lands:

- **v2 winrate ≥ 60%** → keep the change, commit, write next experiment.
- **v2 winrate 45-59%** → ambiguous; n=20 has wide CI. Either expand to
  n=50 or pick a stronger knob and re-test.
- **v2 winrate < 45%** → revert the change. Document why counter-build
  faster might hurt (resource thrash? overshooting comp targets?). Pick a
  different knob.

`compare-batches.sh` against baseline `260512_0849_tournament-arena-skirmish-2p`
will also give a direct delta interpretation.

## Findings (260512_1729 batch, n=20, mirror-paired)

**Decision: KEEP the change.** v2 winrate 65% is above the decision rule's
60% keep threshold. The harness detected the shift cleanly (the baseline
harness sanity at n=20 showed russia 60% / america 40% — without v2, the
USA-bot slot won 30%; with v2, it wins 65%).

### Headline numbers

| Metric | Baseline (260512_0849) | v2 batch (260512_1729) | Delta |
|---|---|---|---|
| USA-bot slot winrate | 30.0% | 65.0% | **+35 pp** |
| Score ratio (mean) | 1.96 | 2.27 | +0.31 |
| Score ratio (max) | 4.13 | 7.65 | +3.52 |

v2 is in the USA-bot slot 100% of the time, so USA-bot winrate IS v2
winrate.

### Faction breakdown (interesting asymmetry)

| v2 faction | Wins | Winrate | vs baseline faction winrate |
|---|---|---|---|
| v2-as-america (primary scenario) | 8/10 | **80%** | baseline america = 40% → +40 pp |
| v2-as-russia (mirror scenario) | 5/10 | **50%** | baseline russia = 60% → -10 pp |

v2's improvement is almost entirely on the america side. The russia
side is slightly **worse** at this knob change — small enough to be
noise at n=10, but worth investigating. Hypothesis: russia's
counter-build list (`at.russia, t90, bmp2` for anti-vehicle) may be
weaker per-unit, so building it faster doesn't compound as much. Or
america's faster counter-build catches russia's BMP-2 spam before it
runs away with army value.

### Statistical caveats

- z = (13-10) / sqrt(20·0.5·0.5) = 1.34, two-tailed p ≈ 0.18.
- n=20 is enough to detect ~15+ pp shifts but **cannot prove** the
  effect is real at conventional significance levels.
- The +35pp side-winrate delta is much more dramatic than the +15pp
  v2 winrate vs the 50% null — because the baseline favored russia,
  putting v2 on the previously-losing america side amplifies the
  visible shift.
- The asymmetric faction finding (80% america, 50% russia) deserves
  a follow-up at n=50 if it matters to overall v2 direction. Not
  blocking; flagging for v2 experiment #2 if needed.

### Conclusion

1. The harness works end-to-end for v2 vs normal A/B testing. ✓
2. The change is net-positive at the matchup level. ✓
3. The asymmetric per-faction result is suspicious — investigate
   before assuming "halve all evaluation intervals = win".

## Raw outputs

- Batch dir: `tools/autotest/tournament-results/260512_1729_tournament-v2-vs-normal-2p/`
- Mirror dir: same; primary (even seeds) and mirror (odd seeds) match
  results interleaved.
- `summary.json` has aggregate stats; `summary.csv` is one row per
  match with `p1_bot`/`p2_bot` columns for verification.

## Decision for follow-up experiments

- **Keep this change.** v2's `AdaptiveProductionBotModule@v2.*` stays
  at `EvaluationInterval: 300`.
- **Next experiment candidate (v2 experiment #2):** investigate the
  asymmetric faction finding. Either (a) n=50 to confirm, or (b) tweak
  another knob (e.g. `MaxRequestsPerCycle` or `MinEnemySightings`) that
  might level out the faction asymmetry.
- **Or jump to architecture work:** the harness has now proven it
  detects shifts. Could move to MapAnalyzer (Phase 1 architecture from
  foundation_260511.md) instead of further knob-tuning.
