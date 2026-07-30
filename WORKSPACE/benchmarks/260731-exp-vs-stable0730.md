# Streak-campaign measurement — Experimental vs Stable AI 0730 (2026-07-31)

**Streak goal:** Experimental wins **10 games in a row** vs Stable AI 0730.
**This run measures where the wave-1 improvements landed at the shipped HEAD.**

## Headline

| | |
|---|---|
| **Experimental win rate** | **30% (3 / 10)** |
| Stable AI 0730 win rate | 70% (7 / 10) |
| Longest Experimental win streak | **3** (matches 2–4) — streak goal **not met** |
| Voids / crashes | **0** (10 / 10 clean verdicts) |
| Decision mode | 10 / 10 by **score at time limit** (0 SR captures) |

Experimental still **underperforms** Stable on this rung. Wave-1 (escort
right-sizing, supply logistics, echelon, transport shuttle — merged `3975b012`)
did **not** measurably close the Exp-vs-Stable deficit here; the 30% sits inside
the re-baseline's ±2/10 noise band (see comparison below).

## Instrument

| | |
|---|---|
| **SHA** | main `@ 3975b012` (`git status`: clean of `engine/**` + `mods/ww3mod/rules/**`; only untracked non-code files present). No rebuild — main already built at this SHA. |
| Bots | `experimental` = "Experimental AI" vs `stable` = "Stable AI 0730" (`mods/ww3mod/rules/ai/ai.yaml:29-34`). |
| Scenario | `tournament-s2-combat-river-zeta` (S2 combat rung, "US mirror" — **both bots play America**, so this rung isolates spawn/slot bias, not faction). |
| Config | `tournament-combat-12min.yaml` — `TimeLimitSeconds: 720`, `SpeedMultiplier: 8`, `GameSpeed: fastest`, `WinRule: score_or_sr_capture`. |
| Sample | N=10, paired **`--mirror tournament-s2-combat-river-zeta-mirror`** (odd seeds = mirror map / swapped slots; even = primary) to cancel P1/P2-slot bias. |
| Seeds | deterministic, `Test.RandomSeed = i*1000+17` per match (reproducible). |
| Profile | `--hidden` (SDL_WINDOW_HIDDEN), sequential, muted. |
| Raw | `tools/autotest/tournament-results/260731_streak_exp_vs_stable0730_s2combat/` (git-ignored). |

**Scoring protocol applied** (user-confirmed): timeout → higher-score side wins
(this is exactly `WinRule: score_or_sr_capture` at the clock); crash → void +
rerun; only outright Experimental losses count against the streak. No crashes
occurred, so no reruns were needed.

## Per-game table (bot-attributed)

> **Attribution caveat:** under `--mirror` the bot assigned to each player *slot*
> swaps between the primary and mirror maps, so the aggregator's
> `side_winrate_pct` (keyed on player name `USA-bot` = 60%) is the **P1-slot**
> win rate, **not** Experimental's. Wins below are attributed by `bot_type`.

| # | Map | Exp plays | Winner | Exp score | Stable score | Result (Exp) |
|---|-----|-----------|--------|----------:|-------------:|:---:|
| 1 | mirror  | Russia-slot | Stable       |  2,550 |  6,050 | **L** |
| 2 | primary | USA-slot    | Experimental | 36,704 |  5,550 | **W** |
| 3 | mirror  | Russia-slot | Experimental |  6,550 |  5,550 | **W** |
| 4 | primary | USA-slot    | Experimental | 72,012 |  4,350 | **W** |
| 5 | mirror  | Russia-slot | Stable       |    750 |  8,550 | **L** |
| 6 | primary | USA-slot    | Stable       | 41,700 | 68,518 | **L** |
| 7 | mirror  | Russia-slot | Stable       |  2,700 | 36,910 | **L** |
| 8 | primary | USA-slot    | Stable       | 37,754 | 64,524 | **L** |
| 9 | mirror  | Russia-slot | Stable       |  6,600 | 37,468 | **L** |
| 10| primary | USA-slot    | Stable       |  2,300 |  5,600 | **L** |

Sequence (m1→m10): **L W W W L L L L L L**. All games ran the full clock
(18,000 ticks = 12 in-game min); every result is a `time_limit` verdict.

## Aggregate

- **Experimental 3 / Stable 7** (Exp 30%).
- Mirror split (bias check): **primary map** Exp 2 / Stable 3; **mirror map** Exp 1 / Stable 4. Slot bias present but symmetric — the P1/USA slot won 6/10; the mirror cancels it, leaving the bot signal **Exp 3 / Stable 7**.
- Mean score: **Exp ≈ 20,962** vs **Stable ≈ 24,307**. Median: Exp ≈ 6,575 vs Stable ≈ 7,300.
- Score-ratio (winner/loser) median **4.06** — matches are **decisive blowouts**, not close.

## Qualitative

- **Experimental is bimodal / high-variance.** Its 3 wins are large blowouts
  (36.7k, 6.6k, 72.0k) but its 7 losses include near-collapses (750, 2,300,
  2,550, 2,700, 6,600). Stable is more consistent. This reproduces the
  re-baseline note that the aggressive Exp bot "trades poorly" — it either
  snowballs or gets out-traded, rarely lands in the middle.
- No SR captures in any game — all decided on accumulated score (army + capture
  income + kills) at the clock.

## Comparison to the re-baseline (same rung)

Re-baseline `260728_rebaseline_result.md` (SHA `e5b7bbcc`), S2 combat, N=10, mirror:

| Config | Exp win split |
|---|---|
| Re-baseline Arm A (item-25 ON, item-24 OFF, ambush ON) | 4–6 (**40%**) |
| Re-baseline Arm B (ambush OFF) | 3–7 (**30%**) |
| **This run — `3975b012` shipped HEAD (item-24 gates ON + wave-1)** | **3–7 (30%)** |

The re-baseline established a win-rate noise floor of **±2/10**. This run's 30% is
**within noise** of both re-baseline arms — i.e. **no measurable movement** from
wave-1 on this rung. The Exp-vs-Stable deficit is unchanged.

## Caveats / scope

- **Single rung, N=10, one map pair.** This is the S2 combat river-zeta rung only
  (the primary combat decision rung, chosen for comparability with the
  re-baseline). It is *not* a full-ladder re-baseline; S1 eco and the
  polar-disorder / woodland-warfare maps were not run in this batch.
- HEAD config note: at `3975b012` the item-24 belief-repoint gates ship **ON**
  (`ai.yaml:180`/`:309`), unlike the re-baseline which measured them temporarily
  **OFF**. So this is the honest shipped-HEAD number, but the two are not a
  perfectly clean A/B on wave-1 alone.
- Bottom line for the streak campaign: **at this SHA Experimental cannot string
  together 10 wins vs Stable on this rung — it wins ~3/10.** Closing the
  Exp-vs-Stable deficit is the prerequisite work before a 10-streak is plausible.
