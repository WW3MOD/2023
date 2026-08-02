# Combat-weighted scorer — Experimental vs Stable AI 0730 (2026-08-02)

**What this measures:** the single load-bearing caveat left open by the 30-0
Experimental sweep — *does the win survive when the scorer weights COMBAT over
territory capture?* All three prior rungs (river-zeta, polar, woodland; see
`260802-exp-vs-stable0730-bothfixes.md` and `260802-exp-vs-stable0730-variedmaps.md`)
share a byte-identical **capture-weighted** scorer (`CaptureIncomeWeight: 2.0`). With
~14 neutral oilbs a single held oilb integrates to ~33k of score, so the capture term
structurally dwarfs army (~2-8k) and kills — the sweep could in principle be pure
capture-race dominance rather than combat superiority. This rung re-scores the same
map/bots with capture **zeroed**.

- Rung: `tournament-s2-combat-river-zeta` (+ `-mirror`), N=10 mirrored.
- Config: **`tournament-combat-12min-combatweighted.yaml`** — a single-knob variant of
  the capture-weighted config: `CaptureIncomeWeight: 2.0 -> 0.0`, everything else
  byte-identical (720s clock, SpeedMultiplier 8, `weighted_components` scorer,
  `score_or_sr_capture` WinRule, `SrCaptureBonus 100000`, `ArmyValueWeight 1.0`,
  `KillsValueWeight 1.0`). Score is now **combat only**: `army_value + kills_value`.
- Fresh seed base `i*1000+47` (1047…10047) — disjoint from river-zeta's `+17` and the
  varied-maps `+31`.

## Headline

| | Capture-weighted (prior, `+17`) | **Combat-weighted (this, `+47`)** |
|---|:---:|:---:|
| **Experimental W-L** | **10 – 0** | **3 – 7** |
| Win sequence (m1→m10) | `W×10` | `L L L L L L W W L W` |
| `capture_income` score component | dominant (~120–190k) | **0 in all 10** (verified) |
| agg `score_total` (Exp / Sta) | Exp ≫ Sta | **30,350 / 38,550** (Sta +27%) |
| agg `kills_cost` (Exp / Sta) | — | **8,350 / 8,450** (dead even) |
| agg `deaths_cost` (Exp / Sta) | — | **29,850 / 14,450** (Exp dies 2×) |
| agg net combat swing (Exp / Sta) | — | **−21,500 / −6,000** |
| agg `army_value` (Exp / Sta) | ~parity | **22,000 / 30,100** (Exp −27%) |
| `capture_income_gross` (Exp / Sta, per game) | Exp ≫ | **Exp ~60–79k / Sta ~16k** (unchanged) |
| Voids / crashes | 0 | **0** |
| Decision mode | 10× score@limit | **10× score@limit** (0 SR captures) |

**Verdict: the win does NOT survive. When combat is what counts, Experimental loses
3 – 7.** The 30-0 sweep was capture-economy dominance, exactly as every prior
benchmark's caveat suspected. Zeroing the capture term — changing nothing else, not one
line of bot/mod/engine — flips the rung from 10-0 Exp to 7-3 Stable. Experimental *still*
wins the capture race just as hard (gross capture income ~60–79k/game vs Stable's ~16k),
but that no longer scores, and on the fight itself Experimental is **worse**: it kills
the same amount (8,350 vs 8,450) yet **dies for twice the army value** (deaths_cost
29,850 vs 14,450) and ends with less standing army (22,000 vs 30,100). This is a
**finding, not a failure** — the capture/TECN build-budget split that wins territory
demonstrably costs Experimental the combat exchange.

## Instrument

| | |
|---|---|
| **SHA** | main `@ 12dfc239` (`batch.meta.json git_sha`, `git_dirty:false`). No engine delta since the `@ 8bd77ae9` varied-maps build (intervening commits are docs/bench only), so the existing bin is current at HEAD; config change is pure MiniYaml read at launch, no rebuild needed. |
| Bots | `experimental` = "Experimental AI" vs `stable` = "Stable AI 0730" (config `P1Bot:experimental P2Bot:stable`). |
| Scenario | `tournament-s2-combat-river-zeta` (S2 combat rung, US-mirror — both factions `america`). |
| Config | **`tools/autotest/tournament-combat-12min-combatweighted.yaml`** — single-knob variant, `CaptureIncomeWeight:0.0` (vs `2.0`). All other fields byte-identical to `tournament-combat-12min.yaml`. Shared across primary + mirror (one config, as the capture-weighted rungs were). |
| Sample | N=10, paired `--mirror tournament-s2-combat-river-zeta-mirror` (odd seeds = mirror/swapped slots; even = primary). |
| Seeds | deterministic `Test.RandomSeed = i*1000+47` (1047…10047) — **fresh base, disjoint from `+17`/`+31`.** |
| Profile | hidden (`OPENRA_WINDOW_HIDDEN=1`), sequential, muted. `--max-wall-secs` auto = 360s/match. |
| Raw | `tools/autotest/tournament-results/260802_combatweighted_exp_vs_stable0730_riverzeta/` (git-ignored). Sanity single game: `…/260802_sanity_combatweighted_riverzeta/`. |
| Runner | private seed-47 copy of `run-tournament.sh` (only the `MATCH_SEED` line changed, `+17`→`+47`; removed after the run). |

**Attribution** strictly by `notes.players[].bot_type` (never slot/faction; `--mirror`
swaps slots on odd seeds). Verified programmatically: all 10 winners resolve via
`winner_name → name → bot_type`; 0 voids, 0 unattributed. `score_total` and
`score_components` (`army_value`/`capture_income`/`kills_value`) read directly from the
verdict JSON; combat stats (`kills_cost`, `deaths_cost`, `army_value`) from
`players[].stats`. Net combat swing = `kills_cost − deaths_cost` (the S2 ladder metric).

## Sanity check — the scorer knob does what it says

One game first (primary river-zeta, seed 1047, `--config` the variant) before trusting
the batch. Result JSON `score_components` for **both** players showed
`capture_income: 0`, while `stats.capture_income_gross` still read ~46–61k (the
underlying capture data is untouched — the bots played identically; only the *weight*
is 0). `score_total` reduced to `army_value + kills_value`. The knob exists and behaves
exactly as designed; capture income no longer contributes to the outcome. Proceeded to
the batch. (Batch-wide re-verification below: `capture_income == 0` in all 10 games.)

## Per-game table (bot-attributed, Exp perspective)

Score shown as `total (army + kills)`; `cap` component is 0 in every row so omitted.
Swing = `kills_cost − deaths_cost`. `Exp capgross` = Exp's gross capture income (what
*would* have scored under the old weight — see counterfactual).

| # | Map | Seed | Winner | Exp total (army+kills) | Sta total (army+kills) | Exp swing | Sta swing | Exp capgross | Decided | Result |
|---|-----|------|--------|-----------------------:|-----------------------:|----------:|----------:|-------------:|:-------:|:---:|
| 1 | mirror  | 1047  | **Stable** | 2,600 (2,500+100)   | 5,200 (4,900+300)   | −3,100 | −850  | 78,472 | time_limit | L |
| 2 | primary | 2047  | **Stable** | 2,800 (2,100+700)   | 3,850 (2,500+1,350) | −3,450 | −2,050 | 63,265 | time_limit | L |
| 3 | mirror  | 3047  | **Stable** | 950 (850+100)       | 2,650 (1,950+700)   | −2,950 | +400  | 77,392 | time_limit | L |
| 4 | primary | 4047  | **Stable** | 650 (650+0)         | 4,600 (2,000+2,600) | −2,900 | +2,600 | 78,872 | time_limit | L |
| 5 | mirror  | 5047  | **Stable** | 5,300 (4,400+900)   | 5,900 (4,500+1,400) | −600  | +500  | 62,139 | time_limit | L |
| 6 | primary | 6047  | **Stable** | 3,050 (2,400+650)   | 4,500 (4,400+100)   | −3,700 | −750  | 63,515 | time_limit | L |
| 7 | mirror  | 7047  | **Experimental** | 4,300 (4,000+300) | 3,600 (2,800+800) | −1,150 | +500 | 62,974 | time_limit | **W** |
| 8 | primary | 8047  | **Experimental** | 4,400 (2,500+1,900) | 4,250 (3,650+600) | −1,500 | −1,300 | 48,772 | time_limit | **W** |
| 9 | mirror  | 9047  | **Stable** | 2,350 (1,850+500)   | 2,400 (2,000+400)   | −2,400 | −200  | 63,859 | time_limit | L |
| 10| primary | 10047 | **Experimental** | 3,950 (750+3,200) | 1,600 (1,400+200) | +250 | −4,850 | 63,278 | time_limit | **W** |

All 10 ran the full 18,000-tick clock; every result is a `time_limit` score verdict, 0
SR captures, 0 voids. **Experimental 3 / Stable 7.**

## The scorer is the sole lever — counterfactual on the SAME games

Because the seed base differs (`+47` vs the capture-weighted `+17`), these are not the
identical games as the 10-0 batch. But the control is tighter than a re-run: apply the
**old** `CaptureIncomeWeight=2.0` to *these very 10 games* (add `2 × capture_income_gross`
to each combat total — every other term is already present) and the winner flips back in
every game:

| Scorer applied to the identical 10 games (`+47`) | Experimental | Stable |
|---|:---:|:---:|
| **Combat-weighted** (`CaptureIncomeWeight 0.0`, as run) | **3** | **7** |
| **Capture-weighted** (`CaptureIncomeWeight 2.0`, counterfactual) | **10** | **0** |

Same map, same bots, same play, same seeds — **only the scorer weight changes, and it
fully determines the outcome.** Under capture weighting Exp wins all 10 by 2–34× (e.g.
M4: 158,394 vs 95,948; M2: 129,330 vs 3,850). Under combat weighting Exp loses 7. This
is the cleanest possible demonstration that the 30-0 sweep is a capture-race artifact:
the capture dominance is real and large, it simply is not combat.

## Why Experimental loses the fight — the combat deficit

Capture no longer scoring is only half the story; the other half is that Experimental is
genuinely *worse in the fight* on this rung:

- **Kills are dead even** (agg `kills_cost` 8,350 Exp vs 8,450 Sta) — Experimental is not
  out-killed.
- **Experimental dies for 2× the value** (agg `deaths_cost` 29,850 vs 14,450). Net combat
  swing −21,500 (Exp) vs −6,000 (Sta): both bleed against the clock, but Exp bleeds 3.6×
  as much net army value.
- **Standing army trails** (agg `army_value` 22,000 vs 30,100, −27%). Exp finishes most
  games with less force on the board.
- Its **only** wins come from combat, as expected: M10 the single positive-swing game
  (+250, a 3,200-kills blowout); M7/M8 carried by army preservation (M7 army 4,000 vs
  2,800). Two of the three wins (M8 +150, M10 aside) are narrow.

This is the mechanism every prior benchmark flagged and it now has a number: the
TECN/capture build-budget split (build slots correctly diverted to capturers) fields a
smaller, more expendable combat force. Under capture scoring that trade is dominant;
under combat scoring it is a **net loss**. The prior "regression watch — army trails
Stable, worth watching if a future rung weights combat" concern is confirmed as a real
combat deficit, not a cosmetic one.

## Comparison vs the capture-weighted river-zeta 10-0

| Metric | Capture-weighted river-zeta (`@cb93015c`, `+17`) | **Combat-weighted (this, `@12dfc239`, `+47`)** |
|---|---|---|
| Exp win rate | **10 / 10** | **3 / 10** |
| Win sequence | `W×10` | `L L L L L L W W L W` |
| What decides | capture_income (~120–190k, dominant) | army + kills (capture = 0) |
| Exp capture dominance | oilb 41 vs 14; huge score edge | **still huge** (capgross ~60–79k vs ~16k) — but 0 score |
| Exp combat (kills / deaths / army) | army ~parity; deaths not the story | **kills even, deaths 2×, army −27%** |
| Attribution / voids | bot_type, 0 voids | bot_type, 0 voids |

The two batches are consistent, not contradictory: Experimental is a **capture bot**. It
wins overwhelmingly when territory scores and loses when it does not, because its combat
force is the weaker of the two. Both facts are true simultaneously.

## Bottom line

- **The last caveat closes negative: Experimental's win does NOT generalize across
  scoring.** Combat-weighted river-zeta is **3 – 7 Stable**, a full reversal of the
  capture-weighted 10 – 0.
- **The scorer weight is the sole lever** — proven by counterfactual on the identical 10
  games (10-0 under capture weight, 3-7 under combat weight). No bot/mod/engine change.
- **The 30-0 sweep is a capture-race artifact.** Experimental still capture-dominates
  identically here (gross income ~4–5× Stable's) — it just doesn't count. On the fight,
  Experimental kills evenly but dies twice as much and fields less army.
- **This is a finding, not a failure.** The capture/TECN investment that wins territory
  is a measured combat liability. If the ladder ever weights combat over territory,
  Experimental as currently tuned loses this rung.
- **No harness pathology:** 0 crashes, 0 voids, all 18,000-tick score verdicts; the
  `capture_income == 0` shift verified in the sanity game and all 10 batch games.

## Caveats / scope

- **Single rung, one map pair, N=10.** This closes the *scoring* caveat on river-zeta
  specifically. Whether combat-weighting flips Exp on polar/woodland too is untested
  (those were only ever run capture-weighted) — but the mechanism (capture build-budget
  split → weaker combat force) is map-agnostic and the varied-maps army/death signature
  already pointed the same way.
- **Seed base differs from the 10-0 batch** (`+47` vs `+17`), so this is not a byte-for-byte
  A/B on the *same* games. The counterfactual (same-games re-scoring) is the controlled
  comparison and is unambiguous; the cross-batch W-L comparison additionally holds the
  map/bots/build fixed and varies only scorer+seed, and seed base does not
  systematically favor either bot.
- Combat scorer = `army_value + kills_value` (both combat terms at 1.0). A different
  combat emphasis (e.g. kills-only, or net-swing which debits deaths directly) could
  shift margins, but Exp's 2× death deficit makes a kills/swing-weighted rung *more*
  adverse to Exp, not less — 3-7 is if anything the optimistic combat read for
  Experimental.
- Capture telemetry (`capture_income_gross`) is emitted by both players' stats, so
  Stable's ~16k is observed, not inferred.

**Ref stamp:** batch ran at main `@ 12dfc239` (`git_dirty:false`), 2026-08-02, seeds
1047…10047, config `tournament-combat-12min-combatweighted.yaml`. Compared against the
capture-weighted river-zeta 10-0 (`260802-exp-vs-stable0730-bothfixes.md`, `@ cb93015c`,
seeds …17) and the varied-maps 20-0 (`260802-exp-vs-stable0730-variedmaps.md`,
`@ 8bd77ae9`, seeds …31).
