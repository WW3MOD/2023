# Tournament Harness Phase 1 — Status (260511)

> Snapshot at end of the first implementation session. For the canonical plan, see
> [`../plans/260511_ai_tournament_harness.md`](../plans/260511_ai_tournament_harness.md).
> For traps already hit, see [`PITFALLS.md`](PITFALLS.md). For how to swap any
> piece, see [`tournament_swap_guide.md`](tournament_swap_guide.md).

## What's working

**All five Phase 1 building blocks are landed and exercised:**

1. **Dual `ModularBot@*` in YAML** (`mods/ww3mod/rules/ai/ai.yaml`) — `@normal`
   and `@v2` coexist. v2 inherits normal's module set today via the
   `enable-ai-player` condition's expanded `Bots:` list. New swap points
   (`enable-ai-v2`, `enable-ai-legacy-only`) are pre-declared so future
   module-by-module replacement doesn't touch the YAML structure each time.

2. **Engine plug-in interfaces** in `engine/OpenRA.Mods.Common/Tournament/`:
   `IMatchScorer`, `IWinRuleEvaluator`, `MatchHarness` (static registry).
   Default implementations: `WeightedComponentMatchScorer` +
   `TimeOrSrCaptureWinRule`. Adding a new variant = one file + one
   `RegisterScorer` line. No watcher changes required.

3. **`BotVsBotMatchWatcher` world trait** — reads `tournament.yaml` via
   `Test.TournamentConfig=<path>` launch arg, discovers SR ownership on
   first tick (PITFALL: not on WorldLoaded — see §11), scores every 25 ticks,
   writes JSON verdict via `TestMode.WriteResult` and calls `Game.Exit()`.

4. **Tournament scenario format** — first scenario is
   `tools/autotest/scenarios/tournament-arena-skirmish-2p/`. 66×34 mirror
   map, two non-playable bot combatants (USA-bot, Russia-bot — both
   `Bot: normal`), one spectator-intent Observer player for the local launch
   slot. Includes `tournament.yaml` (12-min canonical config) and
   `tournament-smoke.yaml` (30s smoke-test variant).

5. **Shell harness** — `run-tournament.sh` (seeds × matchup runner) and
   `aggregate-tournament.sh` (CSV + summary.json). Both stamp git SHA into
   `batch.meta.json` so any future change to the scorer/win-rule is
   traceable to the commit that produced its result row.

## What's still rough

**Smoke test runs end-to-end** as of commit chain `c8ea68d4..718e79e7` plus
the trait edits being iterated on. Each individual match completes in ~30s
wall-clock at default game speed (no time acceleration yet — Phase 2 of the
plan).

**Score formula is partly stubbed.** `WeightedComponentMatchScorer` computes
`army_value` from `Valued.Cost` (working), but `capture_income` and
`kills_value` read from `MatchTrackingState` counters that nothing
increments yet. The next polish pass adds event hooks:
- Capture income tracking: `INotifyOwnerChanged` callback on income-
  providing structures (oilb, bio, miss, fcom, hosp).
- Kills value tracking: `INotifyKilled` callback that walks the killer's
  player chain and adds `Valued.Cost` of the dead actor.

**Map has a bias risk.** A single tournament map favors one spawn over the
other if the map isn't truly symmetric. Phase 1 sanity check (20 seeds
legacy-vs-legacy → 40–60% winrate noise band) is the validation we need to
do once batches become tolerable wall-clock.

**Game speed is 1× real time.** A 12-minute match takes 12 wall-clock
minutes. 20 seeds = 4 hours. Phase 2's headless renderer is the unlock for
batches at scale. Until then: short time-limit smoke tests only.

**One Observer SR is auto-spawned** by `world.yaml`'s `StartingUnits@none`
(BaseActor: supplyroute). The watcher filters it out via the `IsBot`
discriminator, but the actor exists on the map at runtime — visible
clutter. **Future cleanup**: a scenario-level override that suppresses
`StartingUnits@none`'s BaseActor for the Observer slot, or marks Observer
as truly non-spawning. Low priority for Phase 1.

## How to run it

```bash
# Smoke test (30 seconds in-game, ~30s wall-clock + ~10s engine init):
./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p \
    --seeds 1 \
    --config tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-smoke.yaml \
    --max-wall-secs 60

# Canonical 12-min match, 20 seeds (~3-4 hours, large windowed):
./tools/autotest/run-tournament.sh tournament-arena-skirmish-2p --seeds 20

# Each batch writes to: tools/autotest/tournament-results/<YYMMDD_HHMM>_<scenario>/
#   match_*.json     — per-match verdict (TestMode envelope + verdict in notes)
#   match_*.log      — engine stdout
#   match_*.watcher.log — explicit-flush diagnostic from the watcher trait
#   summary.csv      — one row per match
#   summary.json     — aggregate stats
#   batch.meta.json  — git SHA, scenario, config used, max-wall, etc.
```

## Decision points still open

1. **Score formula tuning.** The default weights
   (`ArmyValueWeight=1, CaptureIncomeWeight=2, KillsValueWeight=1,
   SrCaptureBonus=100000`) are placeholders. Once capture/kills events are
   wired and we have real numbers, tune so a clearly-winning match scores
   2-3× a slight-edge match, not 100×.

2. **Map pool.** Single map (`tournament-arena-skirmish-2p`) is enough to
   validate the harness. To make AI comparisons statistically meaningful,
   add 1-2 more maps with different terrain shapes. Candidates: a wide-open
   field (favors mobility), a chokepoint-heavy map (favors positioning).
   Probably Phase 2-3.

3. **Match length canonical.** 12 minutes was a guess. Real WW3MOD games
   often run 20+ min. The right answer comes from the first real batch's
   winner-by-match-length curve. Adjust then.

4. **Determinism.** Engine seeds via `DateTime.Now.ToBinary()`. For
   statistical validity over N matches it's fine; for reproducing a
   specific bug it isn't. Phase 2 candidate: `Tournament.RandomSeed`
   launch arg that overrides the server's seed.

## What this unlocks

- Every AI-overhaul commit can be measured against `@legacy` (which is
  identical to `@normal` today) by running a legacy-vs-v2 batch. Once we
  have new modules behind `enable-ai-v2`, the harness reports whether they
  actually improve play.
- Same harness validates personality differentiation (rush vs turtle is
  another matchup), and any future "did this YAML edit break the bot tier"
  regression.
- The autonomous tuning loop (Phase 4 of the plan) sits on top of this
  unchanged — it just calls `run-tournament.sh` with different configs
  between rounds.

## What to do next

In rough priority order:

1. **Validate Phase 1 by running the canonical 20-seed legacy-vs-legacy
   batch.** Expectation: winrate in the 40–60% noise band. If it skews
   harder, fix the map's symmetry before any AI work proceeds.
2. **Build Phase 2 (headless renderer)** to make the canonical batch
   tolerable. Engine work, ~1 session.
3. **Wire capture/kills event tracking** in `MatchTrackingState`. ~1
   session, makes the score formula meaningful.
4. **Multiple maps** in the tournament pool. ~1 session.
5. **Phase 4 (autonomous loop)** once we have real AI work to tune.

## Pointer for the next agent

`WORKSPACE/ai/README.md` is the project home. Start there, then read
`foundation_260511.md` (the architecture), this doc (where we are), and
the plan (`../plans/260511_ai_tournament_harness.md`). The swap-guide and
PITFALLS.md are the operational references for any future code work.
