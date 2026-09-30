# `@stable` benchmark RE-BASELINE #2 — after PIPELINE item 86 (2026-09-22)

**Pipeline item 86.** Four batches, 40 measured matches, run from the main checkout
`/Users/fredrik/Desktop/WW3MOD` by `/tmp/queue36.sh` (runner log `/tmp/queue36.log`,
ends `QUEUE36 COMPLETE`). Recorded per SPEC §5.1
(`WORKSPACE/benchmarks/<YYMMDD>-<name>.md`), mirroring
[`260905-rebaseline.md`](260905-rebaseline.md).

> **Why this corpus exists: the control moved.** Item 86 merged at `main @ ef7362a7` and
> **MOVES `@stable`** — `LaneAmbushBotModule` is shared by both profiles, and
> `OffenseFloorReserveEnabled` is `true` on `@experimental` (`ai.yaml:1093`) *and* on
> `@stable` (`ai.yaml:3227`). The ambush lane may no longer recruit units below offense's
> own advance floor (`AmbushLaneMath.ReserveAllowance = max(0, offenseFree − offenseMin)`).
> **`@stable` is therefore not the same opponent it was on 2026-09-05, and the 260905
> corpus is no longer the control.** This card replaces it as the control. The 260905
> numbers remain valid *as a measurement of the bot that existed then*, and the side-by-side
> below is a comparison of two different opponents, not a before/after of one change in
> isolation — see [Watch](#watch--what-this-corpus-does-not-establish).

## Instrument

| | |
|---|---|
| **Stamped SHA** | `ef7362a7` — `batch.meta.json git_sha: ef7362a7c4c2f8f7c21f37f2ecfff81c32805393`, `git_dirty: false`, **identical in all four `batch.meta.json` and verified field-by-field**. The runner's own `START` line also records `dirty=0`. |
| **Code SHA under test** | **`ef7362a7`** — the item-86 merge itself. No docs-only offset this time: stamped SHA and code SHA are the same commit. |
| Bots | `ModularBot@experimental` vs `ModularBot@stable`; calibration batches are `@stable` v `@stable`. |
| Regime | Unchanged from 260905: `Faction: america` both sides, `StartingUnitsClass: motorized`, opponent = `@stable`. |
| Scenarios | S1 `tournament-s1-eco-river-zeta` (+`-mirror`), S1 cal `tournament-s1-eco-cal-nn`; S2 `tournament-s2-combat-river-zeta` (+`-mirror`), S2 cal `tournament-s2-combat-river-zeta-cal-nn`. |
| Configs | `tournament-eco-5min.yaml` (7,500 ticks) / `tournament-combat-12min.yaml` (18,000 ticks). |
| Sample | N=10 per batch. Exp batches paired `--mirror` (odd match index = Exp in the Russia slot). Cal batches unmirrored (both bots identical — a swap is a no-op). |
| Seeds | `Test.RandomSeed = i*1000+17`, i.e. 1017…10017, identical across all four batches and identical to 260905. |
| Profile | hidden, sequential, **`--max-wall-secs` 900 (S1) / 2200 (S2)** — raised from 260905's 300/600. See [Instrument caveat: different host](#instrument-caveat-a-different-host-and-a-3-slower-clock). |
| Verdict version | `8` in all 40 matches — **same verdict schema as 260905**, so the metrics are field-comparable. |
| Raw | `tools/autotest/tournament-results/260922_rebaseline_{s1_cal,s1_exp,s2_cal,s2_exp}/` (untracked; not in any worktree). |

**Attribution is strictly by `notes.players[].bot_type`**, never by slot or faction —
`--mirror` swaps slots on odd match indices, so slot-attribution silently inverts half the
sample. In the **cal** batches both rows carry `bot_type: stable`, so those two tables are
attributed by player name / faction instead, exactly as the 260905 card does. Ladder metrics
per RUNBOOK §6: **S1 = `stats.capture_income_gross`**, **S2 = `stats.kills_cost −
stats.deaths_cost`**.

## Batches

| # | Batch | Matchup | Verdicts | Win split | Wall clock | Status |
|---|---|---|---:|---|---|---|
| 1 | `260922_rebaseline_s1_cal` | Stable v Stable | 10 / 10 | USA 6 – Russia 4, 0 draws | 1 h 07 m 34 s (~6 m 45 s/match) | clean |
| 2 | `260922_rebaseline_s1_exp` | Exp v Stable, mirrored | 10 / 10 | **Exp 5 – Stable 5**, 0 draws | 1 h 03 m 38 s (~6 m 22 s/match) | clean |
| 3 | `260922_rebaseline_s2_cal` | Stable v Stable | 10 / 10 | USA 6 – Russia 4, 0 draws | 3 h 27 m 09 s (~20 m 43 s/match) | clean |
| 4 | `260922_rebaseline_s2_exp` | Exp v Stable, mirrored | 10 / 10 | **Exp 6 – Stable 4**, 0 draws | 3 h 35 m 27 s (~21 m 33 s/match) | clean |

Batch order as executed: 1 → 2 → 3 → 4, strictly sequential, no overlap. Wall clock measured
from `batch.meta.json started_at` to the `summary.csv` mtime. Total 07:38:47 → 16:52:36 UTC
= **9 h 13 m 49 s** (09:38:47 → 18:52:37 local, matching `/tmp/queue36.log`).

All 40 measured matches ran the **full clock** (`duration_ticks` = 7,500 / 18,000,
`win_reason = time_limit` in every one). **0 no-verdicts, 0 culls, 0 crashes, 0 draws, 0 SR
captures.** Engagement validity **10/10 on every batch** — every match has non-zero
`kills_cost` *and* `deaths_cost` on both sides, so no match is a no-contact void.

**Nothing came near a cap.** Longest match 534 s (S1, cap 900) and 1,431 s (S2, cap 2,200).

### Instrument caveat: a different host, and a ~3× slower clock

The 260905 corpus ran on the Windows checkout `C:/Users/fredr/Desktop/WW3MOD`. **This corpus
ran on macOS, from `/Users/fredrik/Desktop/WW3MOD`.** The observed throughput is roughly
**18–21 effective ticks/s on S1 and 13–15 on S2** (wall-clock per match including launcher
overhead, so a lower bound on sim speed) against the ~2 min/match the Windows host managed on
S1. That is why `/tmp/queue36.sh` raised the caps: **at the 260905 caps of 300 s / 600 s,
every one of these 40 matches would have been culled** (S1 min 332 s, S2 min 1,160 s).

**Claim to be checked, not a fact:** the sim is tick-deterministic and every match ran its
full tick budget, so wall-clock speed *should* not change outcomes — the cap governs only
whether a match is culled before its clock, and none was. **This corpus does not prove that.**
The cheap disproof, if anyone wants it, is to re-run one seed on the Windows host at
`ef7362a7` and compare `match_N.json` field-for-field; the engine was byte-deterministic per
seed on the 260905 build (that card's `s1_cal` / `s1_cal_b` pair demonstrates it), so a
divergence would be a real finding about host-dependence.

### Instrument caveat: S1 and S2 are the SAME GAME, read at two different ticks

**This is a property of the instrument, not of item 86, and the 260905 card does not record
it.** All four scenarios ship the **same `map.bin`** (md5 `ea8c479df8dbdfe7c5bd9c2bc8d85711`,
identical across all four), the `rules.yaml` of each cal/exp pair is **byte-identical**, and
the only functional difference between the S1 and S2 configs is `TimeLimitSeconds: 300` vs
`720`. Empirically:

- **All 40 `time_to_first_capture_tick` values match pairwise** between the S1 batch and its
  S2 partner, per player, per match — 20/20 rows in each pair.
- **Zero monotonicity violations** across nine cumulative stats (`capture_income_gross`,
  `captures_count`, `steals_count`, `losses_count`, `kills_cost`, `deaths_cost`,
  `units_killed`, `units_dead`, `resources_earned`): the S2 value is ≥ the S1 value in all
  360 comparisons.
- `s1_cal` and `s2_cal` produce the **identical winner-by-faction sequence** `U R U U U U U R R R`.
- `capture_income_gross == hold_ticks == poi_income_gross` in **80/80** player rows, so the S1
  ladder metric is a POI·tick count; the S1→S2 increment is capped at exactly
  **63,000 = 6 POI × 10,500 ticks**, and three cal rows hit that ceiling exactly.

**Consequence for reading this card: the corpus contains 20 distinct games, not 40.** S1 reads
them at t=7,500 and S2 reads the same games at t=18,000 under a different metric. **The two
rungs are not independent samples, and "Exp improved on both rungs" is one observation
reported twice, not two.** The 260905 corpus has the same structure — its cal tables show the
same +63,000 ceiling on three rows — so the two corpora are comparable in this respect and
neither gets independent-rung credit.

## Calibration — side bias and noise band

### S1 cal (`s1_cal`), Stable v Stable, unmirrored

| # | Seed | Winner | USA cap-gross | RUS cap-gross | USA swing | RUS swing | Swing Δ (USA−RUS) |
|---|------|--------|--------------:|--------------:|----------:|----------:|------:|
| 1 | 1017 | USA | 27,815 | 23,034 | −7,650 | −5,150 | −2,500 |
| 2 | 2017 | Russia | 21,089 | 29,404 | −5,400 | −4,150 | −1,250 |
| 3 | 3017 | USA | 26,662 | 26,690 | −4,300 | −4,000 | −300 |
| 4 | 4017 | USA | 29,259 | 26,998 | −800 | −8,250 | +7,450 |
| 5 | 5017 | USA | 27,093 | 25,598 | −4,125 | −9,525 | +5,400 |
| 6 | 6017 | USA | 27,863 | 25,928 | −2,200 | −8,300 | +6,100 |
| 7 | 7017 | USA | 28,956 | 25,895 | +1,825 | −9,075 | +10,900 |
| 8 | 8017 | Russia | 26,809 | 28,285 | −8,900 | −9,900 | +1,000 |
| 9 | 9017 | Russia | 23,577 | 27,301 | −6,875 | −4,525 | −2,350 |
| 10 | 10017 | Russia | 24,386 | 27,565 | −4,700 | −6,500 | +1,800 |

**Win USA 6 – Russia 4. Capture-gross median USA 26,951 / Russia 26,844 (mean 26,351 /
26,670); USA leads capture in 5/10. Swing Δ median +1,400, mean +2,625, 6/10 positive.** S1
remains side-fair on capture to within the sample. **The noise band on the swing is roughly
−2,500 … +10,900** between two copies of the same bot.

**The 6–4 is the yardstick, and it is the most important number on this page.** Two identical
bots split 6–4 here. 260905's cal batches split 5–5; under a fair coin P(≥6 of 10) = 0.377, so
a 6–4 is the ordinary case and a 5–5 is not evidence of tighter fairness. **Any Exp-v-Stable
win split of 6–4 or better on this instrument is indistinguishable from two copies of the same
bot.**

### S2 cal (`s2_cal`), Stable v Stable, unmirrored

| # | Seed | Winner | USA cap-gross | RUS cap-gross | USA swing | RUS swing | Swing Δ (USA−RUS) |
|---|------|--------|--------------:|--------------:|----------:|----------:|------:|
| 1 | 1017 | USA | 100,160 | 76,689 | −45,000 | −16,650 | −28,350 |
| 2 | 2017 | Russia | 73,406 | 103,087 | −39,450 | −18,250 | −21,200 |
| 3 | 3017 | USA | 89,662 | 89,690 | −13,950 | −35,350 | +21,400 |
| 4 | 4017 | USA | 94,675 | 87,582 | −3,425 | −39,975 | +36,550 |
| 5 | 5017 | USA | 95,361 | 83,330 | −46,350 | −17,800 | −28,550 |
| 6 | 6017 | USA | 95,181 | 74,110 | −18,775 | −32,225 | +13,450 |
| 7 | 7017 | USA | 104,040 | 74,527 | −27,250 | −30,500 | +3,250 |
| 8 | 8017 | Russia | 82,585 | 98,509 | −45,875 | −35,625 | −10,250 |
| 9 | 9017 | Russia | 79,476 | 95,830 | −40,700 | −26,150 | −14,550 |
| 10 | 10017 | Russia | 83,201 | 92,608 | −28,200 | −27,250 | −950 |

**Win USA 6 – Russia 4. Capture-gross median USA 92,169 / Russia 88,636 (mean 89,775 /
87,596); USA leads capture in 5/10. Swing Δ median −5,600, mean −2,920, 4/10 positive.**

Two things to carry forward:

1. **The 260905 S2 spawn asymmetry did NOT reproduce.** That card recorded Russia out-earning
   USA on capture in 8 of 10 identical-bot games (median gap ≈ 4,700) and warned that only the
   mirror cancels it. Here USA leads capture 5/10 with a median gap of +3,533 **in the other
   direction**. **Do not carry the 260905 "Russia spawn out-earns" warning forward as a
   standing fact about this map** — on this corpus the S2 capture spawn bias is a wash, and the
   mirror is still what makes the exp batch safe either way.
2. **The S2 noise band on the swing is very wide: −28,550 … +36,550** observed between two
   copies of the same bot. **Any Exp-v-Stable swing edge below ~$36,500 on S2 is inside the
   noise floor of this instrument at N=10.** (260905 measured this band as ±$46,300; it is
   narrower here but the same order, and the conclusion is unchanged.)

## Baseline — Experimental v Stable

### S1 eco (`s1_exp`), mirrored — ladder metric `capture_income_gross`

| # | Seed | Exp slot | Winner (bot) | Exp cap-gross | Sta cap-gross | Cap Δ | Exp swing | Sta swing | Swing Δ |
|---|------|----------|--------------|--------------:|--------------:|------:|----------:|----------:|--------:|
| 1 | 1017 | Russia | Stable | 24,031 | 27,246 | −3,215 | −7,475 | −6,725 | −750 |
| 2 | 2017 | USA | Stable | 27,403 | 27,968 | −565 | −7,850 | −3,850 | −4,000 |
| 3 | 3017 | Russia | Stable | 27,002 | 30,741 | −3,739 | −11,875 | −6,275 | −5,600 |
| 4 | 4017 | USA | **Exp** | 29,827 | 27,815 | +2,012 | −4,250 | −5,300 | +1,050 |
| 5 | 5017 | Russia | Stable | 26,724 | 26,588 | +136 | −5,550 | −2,350 | −3,200 |
| 6 | 6017 | USA | **Exp** | 28,571 | 25,206 | +3,365 | −4,625 | −6,075 | +1,450 |
| 7 | 7017 | Russia | **Exp** | 27,208 | 28,010 | −802 | +375 | −10,125 | +10,500 |
| 8 | 8017 | USA | Stable | 27,335 | 29,237 | −1,902 | −7,925 | −7,025 | −900 |
| 9 | 9017 | Russia | **Exp** | 26,600 | 26,506 | +94 | −4,700 | −7,550 | +2,850 |
| 10 | 10017 | USA | **Exp** | 27,206 | 27,627 | −421 | −2,725 | −9,225 | +6,500 |

**Exp win rate 5/10.** Win sequence (m1→m10): `L L L W L W W L W W`. **Ladder metric:
capture-gross median Exp 27,207 / Stable 27,721 (mean 27,191 / 27,694); Exp leads capture in
4/10, Cap Δ median −493, mean −504.** Swing Δ median +150, mean +790, 5/10 positive.

**Exp's five wins are split 3 USA-slot / 2 Russia-slot.** The 260905 corpus's sharpest
observation on this rung — *"Exp lost every USA-slot game"*, all three of its wins in the
Russia slot — **does not reproduce.** There is no slot concentration here at all.

### S2 combat (`s2_exp`), mirrored — ladder metric `kills_cost − deaths_cost`

| # | Seed | Exp slot | Winner (bot) | Exp cap-gross | Sta cap-gross | Cap Δ | Exp swing | Sta swing | Swing Δ |
|---|------|----------|--------------|--------------:|--------------:|------:|----------:|----------:|--------:|
| 1 | 1017 | Russia | Stable | 76,531 | 90,246 | −13,715 | −40,800 | −1,850 | −38,950 |
| 2 | 2017 | USA | **Exp** | 90,403 | 90,968 | −565 | −5,400 | −34,800 | +29,400 |
| 3 | 3017 | Russia | **Exp** | 96,314 | 87,429 | +8,885 | −33,200 | −32,200 | −1,000 |
| 4 | 4017 | USA | Stable | 90,365 | 93,277 | −2,912 | −40,950 | −32,200 | −8,750 |
| 5 | 5017 | Russia | Stable | 91,963 | 87,349 | +4,614 | −40,900 | −20,650 | −20,250 |
| 6 | 6017 | USA | **Exp** | 94,888 | 84,698 | +10,190 | −14,600 | −40,550 | +25,950 |
| 7 | 7017 | Russia | **Exp** | 98,463 | 82,755 | +15,708 | −12,350 | −43,150 | +30,800 |
| 8 | 8017 | USA | Stable | 82,083 | 100,489 | −18,406 | −17,750 | −27,450 | +9,700 |
| 9 | 9017 | Russia | **Exp** | 91,860 | 84,560 | +7,300 | −875 | −44,425 | +43,550 |
| 10 | 10017 | USA | **Exp** | 91,742 | 89,091 | +2,651 | −10,700 | −48,900 | +38,200 |

**Exp win rate 6/10.** Win sequence: `L W W L L W W L W W`, split 3 USA-slot / 3 Russia-slot.
**Ladder metric: mean swing Exp −21,752.5 / Stable −32,617.5 → mean Swing Δ +10,865; median
swing Exp −16,175 / Stable −33,500 → median Swing Δ +17,825; 6/10 positive, range −38,950 …
+43,550.** Capture-gross median Exp 91,801 / Stable 88,260; Exp leads capture 6/10, Cap Δ
median +3,633.

## Side by side against the 260905 corpus

**Read this as two different opponents, not as one change measured twice.** `@stable` moved
between the two corpora (item 86, and anything else that landed in the 16 days between
`bb89f9fd` and `ef7362a7`).

| | 260905 (`bb89f9fd`) | 260922 (`ef7362a7`) | |
|---|---|---|---|
| S1 cal win split | USA 5 – Rus 5 | USA 6 – Rus 4 | both ordinary for a fair coin |
| S2 cal win split | USA 5 – Rus 5 | USA 6 – Rus 4 | |
| S2 cal capture bias | **Russia leads 8/10**, median ≈ +4,700 | **USA leads 5/10**, median +3,533 | asymmetry did not reproduce |
| S1 cal swing noise band | −8,500 … +8,500 | −2,500 … +10,900 | comparable |
| S2 cal swing noise band | −17,050 … +46,300 | −28,550 … +36,550 | comparable |
| **S1 Exp win rate** | **3/10** | **5/10** | +2 |
| S1 cap median Exp / Sta | 26,689 / 27,482 | 27,207 / 27,721 | Exp still behind |
| S1 Exp leads capture | 3/10 | 4/10 | |
| S1 Swing Δ median / mean | +600 / −2,850 | +150 / +790 | |
| **S2 Exp win rate** | **2/10** | **6/10** | **+4** |
| S2 cap median Exp / Sta | 83,833 / 94,024 | 91,801 / 88,260 | **gap closed and crossed** |
| S2 Exp leads capture | 2/10 | 6/10 | |
| S2 mean swing Exp / Sta | −26,790 / −29,810 | **−21,752.5 / −32,617.5** | |
| S2 Swing Δ median / mean | +7,300 / +3,020 | **+17,825 / +10,865** | |

### What changed

**The S2 capture deficit is gone.** 260905's core finding was that `@experimental` loses on
S2 because it trails `@stable`'s gross capture income in 8 of 10 games by a median ≈ 10,200,
and that *capture decided every game in that batch*. On this corpus Exp **leads** capture in
6/10 with a median Cap Δ of +3,633, and the win rate on the rung where capture is the dominant
score component went 2/10 → 6/10. That is the single largest movement between the two cards,
and it is on the exact mechanism 260905 named.

**On S1 the picture is flat.** Exp went 3/10 → 5/10, but the ladder metric barely moved: Exp
is still behind Stable on median capture-gross (27,207 vs 27,721) and still leads capture in
under half the games (4/10). **Treat S1 as unchanged.**

### What cannot be concluded from this

**Not that item 86 caused any of it.** Item 86 is the *reason to re-measure*, not a variable
this corpus isolated. There is no arm in which item 86 is off — both bots carry the reserve.
16 days and an unknown number of merges separate `bb89f9fd` from `ef7362a7`, and the host
changed as well. **This card is a new control, not an A/B.**

**Not that `@experimental` is now ahead of `@stable`.** Every headline difference is inside
the instrument's own noise:

- **The S2 win split, 6–4, is exactly what two identical copies of `@stable` produced on both
  cal rungs of this same corpus.** That is the yardstick the 260905 card established and it is
  decisive here: a 6–4 is not a signal on this instrument.
- **The S2 swing edge, mean +10,865 / median +17,825, is inside the S2 calibration noise band
  of −28,550 … +36,550.** Per the 260905 rule of thumb, anything below ~$36,500 on S2 at N=10
  is a wash.
- **At N=10 a 6/10 is unremarkable under a fair coin** (P(≥6) = 0.377). 260905's 2/10 was the
  mildly unusual reading of the two (P(≤2) = 0.055), which is a reason to distrust the *size*
  of the 2/10 → 6/10 move, not to bank it.

**Not two independent confirmations.** Per the instrument caveat above, S1 and S2 are the same
20 games read at two ticks. The S1 and S2 results agree because they partly *are* each other.

**And the S2 swing metric is still biased.** PIPELINE item 87 stands: the doom model charges
the finishing blow to the victim and credits it to nobody, biasing the S2 swing ≈ $4,490/match
against whichever bot dies more. The +10,865 mean edge here is of the same order as that bias
and has not been corrected for.

## Core finding

**On the new control, `@experimental` is at parity with `@stable` on both rungs — 5/10 on S1
eco and 6/10 on S2 combat — and the S2 capture deficit that decided the 260905 corpus is
gone.** Exp now leads gross capture income in 6/10 S2 games (was 2/10) and its combat swing
edge is +10,865 mean, but **both sit inside this corpus's own calibration noise bands, and the
6–4 win split is the exact split two copies of `@stable` produced on both cal rungs.** The
honest statement is *parity, measured on an instrument that cannot resolve better than this at
N=10* — not an Exp lead.

**This corpus is the control for everything measured after `ef7362a7`.**

## Watch — what this corpus does not establish

- **`kills_cost − deaths_cost` is not zero-sum here, same as 260905.** Both players are
  net-negative in **38 of 40** matches (78 of 80 player rows; the exceptions are both m7 —
  `s1_cal` USA at +1,825 and `s1_exp` Exp at +375).
  Cause and consequences are settled: `WORKSPACE/audits/260906-baseline-deaths-audit.md`, and
  the open decision is PIPELINE item 87. **Absolute swing levels are not a combat scoreboard.**
- **No build is recorded for `ef7362a7` before the batches.** `/tmp/queue36.sh` *pre-flights*
  `engine/bin/OpenRA.dll` and refuses with exit 3 if absent, which is stronger than 260905's
  record — but it checks existence, not freshness. **HYPOTHESIS: the tree was built at
  `ef7362a7`.** Disproof is the same as before: rebuild and re-run one seed; deterministic sim,
  byte-identical `match_1.json` settles it.
- **One map for the whole corpus.** All four scenarios share one `map.bin`. Nothing here
  generalises across maps — and this is stronger than 260905's "one map pair per rung" caveat,
  because it is literally one map for all four batches.
- **N=10, and effectively 20 games rather than 40.** Most of the per-game numbers above are
  inside the calibration bands.
- **The host changed and the caps changed with it.** Tick-determinism says this is immaterial;
  it was not verified. See the instrument caveat.
- **`@stable` moved and nobody re-took a `@stable`-vs-old-`@stable` reading.** There is no
  measurement of how much item 86 changed `@stable` itself — only of `@experimental` against
  the new `@stable`. If that matters for a future comparison, it is an unrun batch.
- **The 260905 S2 spawn-asymmetry warning is contradicted here and neither reading is settled.**
  8/10 Russia-favouring then, 5/10 USA-favouring now, on the same map.bin. One of the two is
  noise; at N=10 this corpus cannot say which.

**Ref stamp:** stamped and code `ef7362a7` (`git_dirty: false`, verified in all four
`batch.meta.json`), run 2026-09-22 07:38:47 → 16:52:36 UTC (09:38 → 18:52 local). Recorded
from `/Users/fredrik/worktrees/ww3mod/rebaseline-260922` on branch `wt/rebaseline-260922`,
branched from `main @ ef7362a7`.
