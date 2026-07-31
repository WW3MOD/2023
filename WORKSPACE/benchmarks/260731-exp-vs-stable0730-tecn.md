# Streak-campaign measurement — Experimental vs Stable AI 0730, post-TECN-lever (2026-07-31)

**What this measures:** the two @experimental levers merged *after* the prior batch —
frontier-standoff placement (`9136368e`) and the TECN capture-supply lever
(`f9da1860`: floor un-deadlock + opportunity-scaled floor cap 5 + TECN priority
over combat buys + capture-target fan-out). Same rung, same seeds, same protocol as
`260731-exp-vs-stable0730.md` for direct comparability.

## Headline

| | |
|---|---|
| **Experimental win rate** | **30% (3 / 10)** — *unchanged* from prior batch |
| Stable AI 0730 win rate | 70% (7 / 10) |
| Win sequence (m1→m10) | **L W W W L L L L L L** — *bit-identical* to prior batch |
| **TECN-fired rate (Exp TECN>0)** | **4 / 10** — *unchanged* (target was ~10/10) |
| oilb held (Exp / Stable, batch total) | **6 / 6** (prior: 5 / 6) |
| Voids / crashes | **0** (10 / 10 clean verdicts) |
| Decision mode | 10 / 10 by **score at time limit** (0 SR captures) |

**Verdict: both levers are within the ±2/10 noise floor — no measurable movement.**
The TECN lever's *demand* side fired (floor 1→5, `priority=True`, continuous
re-request) but its *supply* side did **not**: in the 6 games where Exp fielded no
TECN before, it **still fields zero** — the priority-production seam never actually
preempts combat buys, so the floor request piles up to **`pending=82`** while
`alive` stays **0** for the entire game. The un-deadlock turned "one stuck request"
into "82 stuck requests," but not one TECN completes.

## Instrument

| | |
|---|---|
| **SHA** | main `@ f9da1860` (`batch.meta.json git_sha`, `git_dirty:false`). Rebuilt at HEAD before the run. |
| Bots | `experimental` = "Experimental AI" vs `stable` = "Stable AI 0730". |
| Scenario | `tournament-s2-combat-river-zeta` (S2 combat rung, US-mirror — both bots play America). |
| Config | `tournament-combat-12min.yaml` — `TimeLimitSeconds:720`, `SpeedMultiplier:8`, `GameSpeed:fastest`, `WinRule:score_or_sr_capture`. |
| Sample | N=10, paired `--mirror tournament-s2-combat-river-zeta-mirror` (odd seeds = mirror/swapped slots; even = primary). |
| Seeds | deterministic `Test.RandomSeed = i*1000+17` — **identical seeds to the prior batch.** |
| Profile | `--hidden` (SDL_WINDOW_HIDDEN), sequential, muted. |
| Raw | `tools/autotest/tournament-results/260731_streak_exp_vs_stable0730_tecn_s2combat/` (git-ignored). |
| Parser | `tools/autotest/parse-tecn-batch.py` — validated to reproduce the loss-analysis table exactly against the prior batch. |

**Attribution** strictly by `notes.players[].bot_type` (never slot/faction; `--mirror`
swaps slots on odd seeds). `oilb` = `oilb#` entries in the final `[exp-capture]
ownership-snapshot` per owner. `TECN` = `max(total-tecns + committed)` over
`no-idle-capturers` samples — the same metric the loss analysis used.

## Per-game table (bot-attributed)

| # | Map | Exp slot | Winner | Exp oilb | Sta oilb | Exp TECN | Sta TECN | Exp score | Sta score | Result |
|---|-----|----------|--------|---------:|---------:|---------:|---------:|----------:|----------:|:---:|
| 1 | mirror  | Russia | Stable       | 0 | 0 | 0 | 0 |  2,550 |  6,050 | **L** |
| 2 | primary | USA    | Experimental | 1 | 0 | 2 | 0 | 36,704 |  5,550 | **W** |
| 3 | mirror  | Russia | Experimental | 0 | 0 | 0 | 0 |  6,550 |  5,550 | **W** |
| 4 | primary | USA    | Experimental | 2 | 0 | 3 | 0 | 72,088 |  5,550 | **W** |
| 5 | mirror  | Russia | Stable       | 0 | 0 | 0 | 0 |  3,500 |  6,500 | **L** |
| 6 | primary | USA    | Stable       | 2 | 2 | 4 | 4 | 68,352 | 69,768 | **L** |
| 7 | mirror  | Russia | Stable       | 0 | 1 | 0 | 2 |  2,700 | 36,910 | **L** |
| 8 | primary | USA    | Stable       | 1 | 2 | 2 | 3 | 37,754 | 64,524 | **L** |
| 9 | mirror  | Russia | Stable       | 0 | 1 | 0 | 2 |  6,600 | 37,468 | **L** |
| 10| primary | USA    | Stable       | 0 | 0 | 0 | 0 |  2,300 |  5,600 | **L** |

All 10 ran the full 18,000-tick clock; every result is a `time_limit` verdict.

## Direct comparison to the prior batch (`3975b012`, same seeds)

| Metric | Prior batch | This batch (`f9da1860`) | Moved? |
|---|---|---|---|
| Exp win rate | 3 / 10 | **3 / 10** | No (identical sequence) |
| TECN-fired rate (Exp>0) | 4 / 10 | **4 / 10** | No — same 4 games (M2/M4/M6/M8) |
| Exp oilb total | 5 | 6 | +1 (only M6: 1→2) |
| Stable oilb total | 6 | 6 | No |
| Requested TECN floor | **1** (always) | **5** (`priority=True`) | **Yes — lever is active** |

Per-game scores are **byte-identical** in 7 of 10 games (M1/M2/M3/M7/M8/M9/M10). Only
three diverged: M4 (72,012→72,088, noise), **M5** (750→3,500; army 550→2,100 — the
standoff fix preserving more army, but still a loss), and **M6** (41,700→68,352; Exp
captured a 2nd oilb and closed to a near-tie, but still lost by 1,416). The determinism
confirms the exp code path changed only where the new levers actually engaged — and in
the 6 losing zero-TECN games, they did not change the outcome at all.

## Why the TECN lever did not fire — the supply-side deadlock

The lever is demonstrably **active on the demand side**: across the batch the floor
request is now `floor=5 priority=True` (828 lines) vs the prior `floor=1` (always), and
the un-deadlock removed the `pending ≥ floor` gate so it re-requests every scan.

But it **fails on the supply side**. In **all six** zero-TECN games the signature is
uniform and damning:

```
tecn-floor-request player=<exp> type=tecn.america alive=0 pending=1  floor=5 priority=True tick=94
...
tecn-floor-request player=<exp> type=tecn.america alive=0 pending=82 floor=5 priority=True tick=17869
```

`pending` climbs monotonically to **82** while `alive` stays **0** for 18,000 ticks —
**zero TECN ever complete.** The priority flag is emitted but the production allocator
still spends every build slot on combat units; the TECN requests just accumulate as
`pending`. This is exactly the production-saturation deadlock the loss analysis
predicted (`260731-loss-analysis §"floor deadlocks under production pressure"`), now
made *louder* (82 stuck requests instead of 1) but no more *effective*. **The
priority-production seam does not actually reserve or win the build slot.**

The lever *does* work in the 4 games (M2/M4/M6/M8) where the production queue had slack
— but those are the same 4 games that already fielded TECN in the prior batch, so the
lever added nothing there either. Its one visible win is **M6**, where Exp grabbed a
2nd oilb (1→2) and turned a 41.7k-vs-68.5k blowout into a 68.4k-vs-69.8k near-tie — a
genuine improvement that still lost by ~1,400.

## Frontier-standoff (`9136368e`) — marginal

This batch is the first to include the standoff fix (prior batch predates it). Effect
is confined to army preservation in one collapse game: **M5** exp army 550→2,100 (score
750→3,500), still a loss. M1 (2,050) and M10 (1,400) — the other two collapse losses —
are byte-unchanged. **Flipped 0 outcomes; within noise.**

## Bottom line

- **Neither lever clears the ±2/10 noise floor.** Win rate is dead-center unchanged at
  **3/10**, with a bit-identical W/L sequence.
- **The TECN capture-supply lever's headline promise did not materialize.** Expected
  signature was "Exp fields 3–5 TECN nearly every game, holds 2+ oilbs"; observed is
  "TECN-fired unchanged at 4/10, `pending=82`/`alive=0` deadlock in the other 6." The
  demand plumbing (floor 5, priority, fan-out, re-request) is all in place — **the
  missing piece is production-budget preemption**: the priority seam must actually take
  a build slot away from combat buys, which it currently does not.
- **No regression from the priority seam.** The concern that stealing build slots would
  collapse Exp's army did not appear — because no slots were actually stolen (TECN never
  built). Army scores are unchanged or slightly better (M5).
- **Next lever must be supply-side, not demand-side.** Raising the floor further or
  widening fan-out will only grow `pending`; the fix has to make the production
  allocator honor `priority=True` and reserve a slot for the TECN before combat units
  drain the budget.

## Caveats / scope

- Single rung, N=10, one map pair (river-zeta primary+mirror). Not a full-ladder run.
- oilb/TECN telemetry is Experimental-emitted but snapshots the whole map, so Stable's
  holdings are observed, not inferred.
- Determinism note: identical seeds + a mostly-inert code change → 7/10 byte-identical
  games. This is expected and is what makes the 3-game divergence interpretable.

**Ref stamp:** batch ran at main `@ f9da1860` (`git_dirty:false`), 2026-07-31. Prior
batch for comparison: `@ 3975b012` (`260731-exp-vs-stable0730.md`). Loss analysis that
motivated the lever: `WORKSPACE/recon/260731-loss-analysis-exp-vs-stable0730.md`.
