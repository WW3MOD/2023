# Streak-campaign measurement — Experimental vs Stable AI 0730, both delivery fixes (2026-08-02)

**What this measures:** the two @experimental delivery fixes merged *after* the
TECN-null batch, both correcting the same class of bug (requests routed to a dead,
disabled `UnitBuilder` twin instead of the enabled one):

- `a3bceb5a` — TECN priority requests routed to the **ENABLED** `UnitBuilder`
  (was: dead disabled twin), peek-don't-pop drain, pending capped to `[0, floor]`.
- `cb93015c` — `AdaptiveProduction` counter-composition buys routed to the
  **ENABLED** `UnitBuilder` (was: ALL Exp NATO counter-buys landing in a dead
  queue + wedging the `alreadyRequested >= 2` gate).

Same rung, same seeds, same protocol as `260731-exp-vs-stable0730.md` (baseline) and
`260731-exp-vs-stable0730-tecn.md` (TECN-null) for direct comparability.

## Headline

| | |
|---|---|
| **Experimental win rate** | **100% (10 / 10)** — up from **3 / 10** in both prior batches |
| Stable AI 0730 win rate | 0% (0 / 10) |
| Win sequence (m1→m10) | **W W W W W W W W W W** (prior batches: `L W W W L L L L L L`) |
| **TECN-fired rate (Exp TECN>0)** | **10 / 10** — up from 4 / 10 (target was ~10/10) |
| TECN deadlock | **GONE** — `alive` 3–4 every game, `pending` capped at 4 ≤ floor 5 |
| oilb held (Exp / Stable, batch total) | **41 / 14** (prior: 6 / 6) |
| Voids / crashes | **0** (10 / 10 clean verdicts) |
| Decision mode | 10 / 10 by **score at time limit** (0 SR captures) |

**Verdict: a decisive +7/10 swing (3→10) that clears the ±2/10 noise floor by a
wide margin — this is a real, large effect, not noise.** The delivery fixes worked:
TECN now actually *complete* (`alive` 3–4 vs the prior `alive=0`), the
`pending=82`/`alive=0` deadlock is completely eliminated (`pending` now caps at 4),
and the resulting capture economy (41 oilb vs 14) buries Stable on score in every
game. **Caveat:** the win is capture-economy-driven, not combat-driven — see the
counter-composition and regression sections; the sweep's magnitude also warrants the
single-rung/single-map caveats below.

## Instrument

| | |
|---|---|
| **SHA** | main `@ cb93015c` (`batch.meta.json git_sha`, `git_dirty:false`). Rebuilt at HEAD before the run (`make.ps1 all`, 0 errors). |
| Bots | `experimental` = "Experimental AI" vs `stable` = "Stable AI 0730". |
| Scenario | `tournament-s2-combat-river-zeta` (S2 combat rung, US-mirror). |
| Config | `tournament-combat-12min.yaml` — `TimeLimitSeconds:720`, `SpeedMultiplier:8`, `GameSpeed:fastest`, `WinRule:score_or_sr_capture`. |
| Sample | N=10, paired `--mirror tournament-s2-combat-river-zeta-mirror` (odd seeds = mirror/swapped slots; even = primary). |
| Seeds | deterministic `Test.RandomSeed = i*1000+17` — **identical seeds to both prior batches.** |
| Profile | `--hidden` (`OPENRA_WINDOW_HIDDEN=1`, SDL_WINDOW_HIDDEN), sequential, muted. |
| Raw | `tools/autotest/tournament-results/260802_streak_exp_vs_stable0730_bothfixes_s2combat/` (git-ignored). |
| Parsers | `tools/autotest/parse-tecn-batch.py` (oilb/TECN/score) + `tools/autotest/parse-floor-traj.py` (pending/alive trajectory). Both validated to reproduce the prior batch exactly. |

**Attribution** strictly by `notes.players[].bot_type` (never slot/faction; `--mirror`
swaps slots on odd seeds). `oilb` = `oilb#` entries in the final `[exp-capture]
ownership-snapshot` per owner. `TECN` = `max(total-tecns + committed)` over
`no-idle-capturers` samples. Floor trajectory read from `tecn-floor-request` lines.

## Per-game table (bot-attributed, Exp perspective)

| # | Map | Seed | Exp slot | Winner | Exp oilb | Sta oilb | oilb Δ | Exp TECN | Sta TECN | Exp score | Sta score | Decided | Result |
|---|-----|------|----------|--------|---------:|---------:|:------:|---------:|---------:|----------:|----------:|:-------:|:---:|
| 1 | mirror  | 1017  | Russia | Experimental | 5 | 2 | +3 | 9  | 4 | 157,260 | 69,638 | time_limit | **W** |
| 2 | primary | 2017  | USA    | Experimental | 3 | 0 | +3 | 10 | 0 | 98,664  | 5,750  | time_limit | **W** |
| 3 | mirror  | 3017  | Russia | Experimental | 6 | 1 | +5 | 10 | 2 | 181,992 | 34,174 | time_limit | **W** |
| 4 | primary | 4017  | USA    | Experimental | 2 | 1 | +1 | 6  | 2 | 70,752  | 39,560 | time_limit | **W** |
| 5 | mirror  | 5017  | Russia | Experimental | 5 | 2 | +3 | 9  | 4 | 158,104 | 71,236 | time_limit | **W** |
| 6 | primary | 6017  | USA    | Experimental | 3 | 1 | +2 | 7  | 2 | 101,916 | 37,090 | time_limit | **W** |
| 7 | mirror  | 7017  | Russia | Experimental | 5 | 1 | +4 | 10 | 2 | 157,300 | 37,878 | time_limit | **W** |
| 8 | primary | 8017  | USA    | Experimental | 2 | 2 | 0  | 7  | 3 | 70,994  | 67,610 | time_limit | **W** |
| 9 | mirror  | 9017  | Russia | Experimental | 5 | 2 | +3 | 10 | 3 | 158,848 | 66,100 | time_limit | **W** |
| 10| primary | 10017 | USA    | Experimental | 5 | 2 | +3 | 8  | 3 | 158,594 | 66,604 | time_limit | **W** |

All 10 ran the full 18,000-tick clock; every result is a `time_limit` score verdict,
0 SR captures, 0 voids.

## TECN delivery — the deadlock is gone

The whole point of `a3bceb5a`. Prior TECN-null batch signature was `alive=0`,
`pending` climbing monotonically to **82**, floor 5, for the entire game in 6 of 10
games — zero TECN ever completed. This batch, **every** game:

| Metric | Prior (TECN-null, `f9da1860`) | This batch (`cb93015c`) |
|---|---|---|
| TECN-fired (Exp `alive`/`tecn`>0) | 4 / 10 | **10 / 10** |
| Exp `max_alive` (TECN completed) | 0 in 6 games, 1–2 in 4 | **3–4 in all 10** |
| Exp `max_pending` | **82** (monotonic climb) | **4** in all 10 |
| Exp `fin_pending` | 82 | 4 |
| Cap held (`pending ≤ floor=5`)? | **No** (82 ≫ 5) | **Yes** (4 ≤ 5, all 10) |

The `peek-don't-pop` drain + `[0, floor]` cap behave exactly as designed: the queue
now sits at a steady `pending=4` (one below the floor of 5, i.e. 3–4 alive + a small
pending buffer) instead of accumulating 82 dead requests. **The deadlock the prior
batch diagnosed as "the missing piece is production-budget preemption" is resolved —
routing to the enabled `UnitBuilder` was the fix; the priority seam now actually wins
a build slot.** Stable never uses the lever (`floor=1`, `priority=False`, `pending=0`
in all 10, as before) — a clean negative control.

## Counter-composition (`cb93015c`) — cannot be isolated from this batch

The AdaptiveProduction routing fix is the harder claim to substantiate. The engine's
`[exp-*]` debug telemetry emits capture/transport/offense/poi/garrison markers but
**no per-unit-type composition or counter-buy marker**, so counter-buy *delivery*
cannot be confirmed directly, and its combat effect cannot be separated from the
capture economy in the score.

What the numbers *do* say cuts against a large independent combat contribution:
Exp's **standing army value is at rough parity with — and often below — Stable's**
(aggregate Exp 25,250 vs Stable 28,250; Exp army is lower in 6 of 10 games, e.g. M1
1,250 vs 3,300; M7 700 vs 1,750; M10 800 vs 2,100). If counter-composition buys were
now dominating the fight, we'd expect Exp army value to *lead*; it does not. **The
100% win rate is therefore attributable to the capture-supply fix (TECN → oilb
economy), not to visible combat out-composition.** The counter-buy routing fix is
plausibly a contributor (and is a correct fix regardless), but this batch provides no
positive evidence isolating its effect. A dedicated composition-telemetry run would
be needed to measure it.

## Oilb differential — the capture race predicted every winner (again)

The rung is a capture race, and the thesis held for a third batch running: **Exp won
or tied the oilb race in all 10 games and won all 10.**

- Exp strictly led oilb in **9 / 10** games (Δ +1 to +5); tied in **1** (M8, 2–2).
- M8 — the only oilb tie and the closest game (70,994 vs 67,610, +3,384) — Exp still
  won, carried by a TECN edge (7 vs 3). Every other game was a blowout (Exp score
  1.5×–17× Stable).
- Batch oilb totals **41 (Exp) / 14 (Stable)** vs the prior batches' **6 / 6** — a
  ~7× swing in Exp's capture holdings, the direct mechanical consequence of TECN now
  fielding 3–4 live capturers per game instead of 0.

## Regression watch — no army collapse

The prior batch flagged the concern that a working priority seam might steal build
slots from combat and collapse Exp's army. **It did not collapse — army is at rough
parity** (Exp 25,250 vs Stable 28,250 aggregate; within ~10%). Exp simply wins on the
capture economy while fielding a comparable (slightly smaller) combat force. No new
pathology observed: all 10 games ran the full clock, 0 crashes, 0 voids, `pending`
steady at 4 (no runaway). The one honest asterisk is that Exp's army trails Stable's
in the majority of games — expected when build budget is (correctly) split toward
TECN capturers, and immaterial while the capture economy dominates scoring, but worth
watching if a future rung weights combat more heavily than territory.

## Comparison to the two prior 3/10 batches (identical seeds)

| Metric | Baseline (`3975b012`) | TECN-null (`f9da1860`) | **Both-fixes (`cb93015c`)** | Moved? |
|---|---|---|---|---|
| Exp win rate | 3 / 10 | 3 / 10 | **10 / 10** | **+7 — decisive** |
| Win sequence | `L W W W L L L L L L` | `L W W W L L L L L L` | **`W×10`** | Fully flipped |
| TECN-fired (Exp>0) | 4 / 10 | 4 / 10 | **10 / 10** | **+6** |
| Exp `max_pending` | — | 82 (deadlock) | **4 (capped)** | Deadlock resolved |
| Exp oilb total | 5 | 6 | **41** | ~7× |
| Stable oilb total | 6 | 6 | **14** | +8 (Stable also capturing more) |

Unlike the prior batch (7/10 byte-identical games — a mostly-inert change), this batch
diverges from the baseline in **all 10 games**: the enabled-`UnitBuilder` routing is a
live code path that engages every game, exactly as a real fix (not noise) should.

## Bottom line

- **The delivery fixes cleared the noise floor by a wide margin: 3/10 → 10/10 (+7),
  far beyond ±2/10.** This is the first measured movement in the streak campaign.
- **The TECN deadlock is definitively resolved** (`a3bceb5a`). `alive` 0→3–4,
  `pending` 82→4 (cap held), TECN-fired 4/10→10/10. Routing to the enabled
  `UnitBuilder` was the missing production-budget preemption the TECN-null batch
  predicted.
- **The win is capture-economy-driven, not combat-driven.** oilb 41 vs 14; army at
  parity. The counter-composition routing fix (`cb93015c`) is a correct change but
  its independent combat effect **cannot be isolated** from this batch's telemetry.
- **No regression.** No army collapse, no runaway pending, 0 voids.

## Caveats / scope

- Single rung, N=10, one map pair (river-zeta primary+mirror). A 10/0 sweep on one
  rung is a strong signal but not a full-ladder verdict — the capture economy
  compounds (each oilb yields ongoing score), which amplifies a real edge into a
  blowout on *this* capture-weighted rung specifically. Confirm on a combat-weighted
  rung before generalizing.
- Counter-composition delivery is unmeasured (no per-unit-type telemetry marker); its
  contribution to the sweep is unproven, and the honest read attributes the win to the
  TECN/capture fix.
- oilb/TECN telemetry is Experimental-emitted but snapshots the whole map, so Stable's
  holdings are observed, not inferred.

**Ref stamp:** batch ran at main `@ cb93015c` (`git_dirty:false`), 2026-08-02. Prior
batches for comparison: `@ f9da1860` (`260731-exp-vs-stable0730-tecn.md`, TECN-null)
and `@ 3975b012` (`260731-exp-vs-stable0730.md`, baseline), both 3/10 with a
bit-identical W/L sequence.
