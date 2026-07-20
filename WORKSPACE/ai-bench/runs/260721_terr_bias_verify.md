# TERR balance-of-power bias — S2 + S1 verify batch (2026-07-21)

**Branch:** `exp-terr-bias` @ `4adf867c` (off `main` @ `ec097751`); batch ran against
`4adf867c` (dirty=false).
**Change under test:** `@experimental`-only territorial balance-of-power offense bias
(`PoiOffensiveBotModule.BalanceOfPowerBiasEnabled` + BoP rescale reading the shared
`InfluenceMap` friendly/enemy share at each offensive target cell; contact-dominated
≥60 → ×150, enemy-dominated ≤40 → ×60, even/empty → ×100). `@stable` byte-identical.
**Run mode:** Mode B (minimized + framerate-uncapped, `SpeedMultiplier: 8`), Exp-vs-Stable,
seeds i*1000+17 (even = primary, odd = mirror), N=10 each rung. 20/20 verdicts, 0 no-verdict,
0 crashes, 0 orphans.
**Plan:** `WORKSPACE/plans/260721_terr_offense_bias.md`. **Baseline:** `260721_regime_rebaseline.md`.
**Result dirs:** `tournament-results/260720_2027_tournament-s2-combat-river-zeta`,
`tournament-results/260720_2047_tournament-s1-eco-river-zeta`.

---

## Headline (vs re-baseline `@60b93501`)

| Metric | Baseline (Exp-v-Stable) | This batch | Δ |
|---|---|---|---|
| S2 median Exp swing | −350 | **−350** | flat |
| S2 median Stable swing | 0 | **0** | flat |
| S2 relative edge (Exp − Stable) | −350 | **−350** | flat |
| S2 sign-delta (Exp>Stable) | 3/10 | **3/10** | flat |
| S2 both-spawn | 2/5 primary, 1/5 mirror | **2/5, 1/5** | flat |
| S2 engaged-count (validity gate ≥6/10) | 7/10 | **7/10** | flat (not lifting) |
| S2 Exp engagement median | 1300 | **1300** | flat |
| S2 win split | 5–5 | **5–5** | flat |
| S1 win split | 5–5 | **5–5** | flat |
| S1 capture (Exp vs Stable) | 6/10 vs 6/10 | **6/10 vs 6/10** | flat |
| S1 Exp gross median | ~6167 | **5983** (Stable 6124) | ~even |

**S2 pass bar (proposed):** median(Exp) ≥ median(Stable) + $1,000 AND sign ≥7/10 AND
both-spawn ≥3/5 each; blocking gate ≥6/10 engaged. → **FAIL** (−350 < +1000; 3/10 < 7; 2/5,1/5 < 3/5).
**Cycle success:** relative swing turns positive without dropping engagement, engaged ≥6/10. → **NOT MET** (swing did not turn positive).
**S1 non-regression floor:** win ≥0.40 + capture parity (±2/10). → **PASS** (0.50, 6/10 vs 6/10).

**One bright spot:** the worst over-aggression cell moved in the intended direction.
Seed **6017** Exp swing **−4200 → −2800 (+$1,400)**, k/d **2/10 → 3/12** — the
damp-into-strength half ("stop lunging into strength") working on exactly the cell the
re-baseline flagged as Exp's over-aggression loss. Seed 8017 (−4950, 4/10) unchanged.
The +$1,400 on 6017 does not cross the median position, so aggregate headline is flat.

---

## Why flat: the bias is a near-uniform DAMPER, not a front-advancer

Preserved per-match `debug.log` grep across the S2 batch:

```
   3458 [exp-terr] bop        (per boosted/damped target)
   1800 [exp-terr] reeval     (per reeval)
      7 [exp-terr] axis-shift  (top-axis order changed by the rescale)
   3455 mul=60   (DAMP — enemy-dominated contact cell, share <= 40)
      3 mul=150  (BOOST — we dominate a contact cell, share >= 60)
```

**Boost fired 3 times in ~3458 ratings; damp fired 3455.** With raw `InfluenceMap` share
as the input, offensive targets (enemy SR, enemy base, contested derricks) almost always
sit in **enemy-dominated influence with no friendly presence** (f=0 → share=0 → damp ×60).
The "advance the front where we are comparatively strong" boost half essentially never
triggers, because a cell we dominate is no longer much of an *enemy* target. Net effect:
the bias uniformly damps offensive axis scores rather than *redirecting* the army toward
comparatively-weak sectors — so it slightly softens aggression everywhere (helping the one
over-aggression cell) without producing the contact-seeking advance the cycle wanted.
Engaged-count and swing are therefore unmoved.

This is the raw-share limitation the plan's slice-2 (cycle 5, fog-respecting `TerritoryMap`
classification) is meant to fix — the consumer plumbing here is unchanged, only the input
would upgrade. The boost trigger needs a different signal than "we already dominate the
target cell".

---

## S2 raw (parse-s2-batch.py)

```
| m | scen | seed | ticks | A=exp side/swing/eng/kd | B=stable side/swing/eng/kd | winner |
| 1 | mirror  | 1017 | america -2300 2700 2/3 | america 1600 2800 2/1 | stable |
| 2 | primary | 2017 | america  -500 1500 3/2 | america -800 2800 3/4 | experimental |
| 3 | mirror  | 3017 | america     0    0 0/0 | america    0    0 0/0 | experimental |
| 4 | primary | 4017 | america   150 1050 2/0 | america -600  600 0/2 | experimental |
| 5 | mirror  | 5017 | america  -200 1100 1/3 | america -2500 3800 3/1 | experimental |
| 6 | primary | 6017 | america -2800 6400 3/12| america 1600 6400 11/2 | stable |
| 7 | mirror  | 7017 | america     0    0 0/0 | america    0    0 0/0 | experimental |
| 8 | primary | 8017 | america -4950 7050 4/10| america  650 3550 10/4 | stable |
| 9 | mirror  | 9017 | america -2250 2450 1/4 | america    0 4600 5/2 | stable |
| 10| primary |10017 | america     0    0 0/0 | america    0    0 0/0 | stable |

exp net-swing median ALL: -350 | primary: -500 (n=5) | mirror: -200 (n=5)
exp positive on 1/10 (Exp>0); sign-delta Exp>Stable = 3/10 (m2,m4,m5)
exp engagement median: 1300 | stable: 2800
win split: exp=5 stable=5 | engaged (exp eng>0): 7/10 | zero-combat seeds: 3017,7017,10017
```

## S1 raw (parse-s1-batch.py)

```
exp capture rate (gross>0): 6/10 | stable: 6/10
exp gross median ALL: 5983 | primary 6167 (n=5) | mirror 0 (n=5)
stable gross median ALL: 6124
win split: exp=5 stable=5
```

---

## Firing proof (functional autotest, pre-batch)

`test-experimental-poi-harness` (worktree build, `--speed 8 --minimized`) verdict **pass**;
`debug.log` carried 33 `[exp-terr]` lines incl. `bop ... mul=60` damp on enemy-dominated
contact cells (e.g. `supplyroute@58,16` e=2 share=0 → score 26956800→16174080, ×0.6) and
`axis-shift` lines proving the rescale re-ordered the top axes.

---

## Decision

Per the plan / DOCTRINE decision rule: **S2 headline did not lift** — median swing −350
(= baseline, not worse but not better), **engaged-count 7/10 (= baseline, not lifting)**.
This is the "do NOT retune — report the numbers and stop; the manager routes the next step"
branch. **Not merged to main.** Branch `exp-terr-bias` retained for the manager to route:
the honest read is the raw-share BoP term is a near-pure damper (boost 3/3458), so the next
step is likely a **redirection signal** (boost trigger decoupled from "we dominate the
target cell") or the fog-respecting `TerritoryMap` input (slice 2), NOT a re-tune of the
×150/×60 thresholds. S1 non-regression clean.
