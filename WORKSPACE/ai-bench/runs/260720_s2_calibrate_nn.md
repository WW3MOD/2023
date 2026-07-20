# S2 CALIBRATE — Normal-vs-Normal side-fairness + min-engagement (N=10)

**Date:** 2026-07-20
**Scenario:** `tournament-s2-combat-river-zeta-cal-nn` (both bots `@normal`, River Zeta 98×82, 12 OILB derricks)
**Config:** `tournament-combat-12min.yaml` — `TimeLimitSeconds: 720`, `SpeedMultiplier: 8`, hidden Mode-B
**Build:** `main @ 21510e05` (post-PROMOTE; `git_dirty: false` at launch — new S2 scenarios untracked, controls unchanged)
**Seeds:** 1017…10017 (i·1000+17), verdict_version 5 (seed-stamped, deterministic)
**Raw:** `tools/autotest/tournament-results/260720_0550_tournament-s2-combat-river-zeta-cal-nn/`
**Action type:** CALIBRATE (DOCTRINE.md:12) — fires on any new scenario, per plan `260720_s2_expand_design.md` §5.

---

## Validity

10/10 verdicts written, **all `time_limit` @ 18000 ticks** (full natural end, no watchdog kill),
0 crashes. The `*_debug.log` "InvalidDataException: FileSystem section is not defined" lines are
benign OpenRA startup noise (the engine probes `engine/mods/{all,cnc,d2k,ts}` template dirs that
have no `FileSystem` section) — present in every run, not a match crash; all 10 main logs wrote
`result written: pass`. Batch valid (100% ≥ the 80% floor, SPEC §9.1).

## Per-match table (A = america/USA 14,45 ; B = russia/80,35)

| m | seed | A swing | A eng | A k/d | B swing | B eng | B k/d | winner | reason |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 1017 | -6850 | 9250 | 7/13 | -500 | 8700 | 10/4 | russia | time_limit |
| 2 | 2017 | -4350 | 7150 | 3/4 | -3700 | 5900 | 6/5 | russia | time_limit |
| 3 | 3017 | 0 | 5800 | 16/2 | -5600 | 6000 | 2/16 | america | time_limit |
| 4 | 4017 | -5650 | 6250 | 2/15 | 3450 | 4850 | 14/1 | russia | time_limit |
| 5 | 5017 | -3700 | 7700 | 4/5 | -3600 | 4800 | 3/2 | russia | time_limit |
| 6 | 6017 | -3200 | 10400 | 4/9 | 2800 | 9800 | 8/3 | russia | time_limit |
| 7 | 7017 | 4800 | 8500 | 15/3 | -6800 | 8100 | 3/15 | america | time_limit |
| 8 | 8017 | -3450 | 7250 | 5/10 | -3500 | 8600 | 10/5 | america | time_limit |
| 9 | 9017 | -3850 | 5450 | 3/6 | -250 | 3950 | 6/3 | russia | time_limit |
| 10 | 10017 | -3350 | 8750 | 9/10 | -1300 | 4000 | 5/4 | russia | time_limit |

(swing = `kills_cost − deaths_cost`; eng = `kills_cost + deaths_cost`; k/d = `units_killed/units_dead`.)

Aggregate via `python tools/autotest/parse-s2-batch.py calib:<dir>`.

---

## 1. MIN-ENGAGEMENT — the go/no-go on Normal as the S2 opponent → **GO (keep Normal)**

The decisive calibration question (plan §5, §2.4, Q-1): does Normal-vs-Normal actually *fight* at
the 720s clock, or does it stalemate so a steamroll would read as "efficiency" regardless of trade
quality?

- **Engagement-volume median** (`kills_cost + deaths_cost`): america **7475**, russia **5950** — well above 0.
- **deaths_cost median**: america **5725**, russia **4400** — both sides lose real value in a fight.
- **Units died every match, both sides** (`units_dead` 4–16 per side per match; no zero-contact match).

Verdict: **PASS — Normal generates sustained, mutual combat at 720s.** The net-swing metric will
therefore discriminate trade quality against a Normal control. **Recommendation: keep `@normal` as
the S2 opponent; the `@rush` fallback (plan §2.4) is NOT needed.** (Not swapped unilaterally — this
is a numbers-backed recommendation per the task; the loop proceeds on it unless the user overrides.)

## 2. SIDE / FACTION FAIRNESS — moderate russia/80,35 lean → **mandatory mirror**

- **Win split:** russia(80,35) **7** / america(14,45) **3**, 0 draws (russia won 1017,2017,4017,5017,6017,9017,10017).
- **Score median:** russia **4525** vs america **2400**.
- **Net-swing median:** russia **-2400** vs america **-3575** (russia less-negative → trades better from that spawn).

River Zeta favours the russia/80,35 spawn in *combat*, somewhat **stronger than S1's economy lean**
(S1 cal-nn was 6-4 wins / mild). At N=10 the 7-3 split is not statistically separable from 50/50
(binomial p(≥7|0.5) ≈ 0.17), but the direction is consistent with S1 and with the score/swing
medians, so it is a real mild-to-moderate lean, not noise.

**Mirror policy (ratified here):** run S2 Experimental-vs-Normal as **5 primary + 5 mirror** (even
seeds `tournament-s2-combat-river-zeta`, odd seeds `tournament-s2-combat-river-zeta-mirror`, via
`run-tournament.sh --mirror`), and **require the S2 pass to hold from BOTH spawns** (plan §4.2:
Experimental net-swing-positive on ≥3/5 primary AND ≥3/5 mirror). The mirror cancels the spawn/faction
lean; the both-spawn requirement stops a roster advantage reading as skill.

## 3. Bar interpretation caveat — the zero-sum assumption does NOT hold; use the PAIRED-RELATIVE bar

Plan §3.1 assumed a near-zero-sum head-to-head (Exp swing ≈ −control swing). **The data refutes the
absolute-offset half of that:** *both* sides' net-swing medians are **negative** (-2400, -3575), i.e.
each side loses more value than it is credited with destroying. Cause: `deaths_cost` counts every unit
lost (attrition, neutral-defender fire, expiry) while `kills_cost` only credits enemy units you killed,
so a real fight carries a **structural negative offset** on both sides.

Implication for the S2 pass bar (Q-2): an **absolute** "median Exp net swing ≥ +$1,400" (plan §4.2)
is biased *against* passing by roughly this offset (~ −$2,400 to −$3,600 on a Normal mirror), so it is
harder than intended. **Recommendation: use the RELATIVE, PAIRED bar the LADDER S2 row already states —
`median(Exp net swing) ≥ median(Normal net swing) + margin` on the SAME seed set** (determinism makes
this paired, cancelling the shared attrition offset), with margin = one IFV ≈ **+$1,400**. Equivalent
reading: on identical battlefields, Experimental must out-trade the Normal control by ≥ one IFV of net
value. Keep the sign-robustness (≥7/10 positive *delta*) and both-spawn-symmetry guards. This is a
recommendation flagged for ratification (DOCTRINE.md:26); the loop proceeds on it.

---

## Outputs / decisions

- **Opponent:** Normal confirmed viable (min-engagement GO). Rush fallback shelved.
- **Mirror:** mandatory 5+5, both-spawn symmetry required (moderate russia/80,35 lean).
- **Bar:** paired-relative `median(Exp swing) ≥ median(Normal swing) + $1,400` (not the absolute form —
  structural negative attrition offset); ≥7/10 sign delta + both-spawn. Pending user ratification.
- **S3 watch item:** the 7-3 combat win-lean is outside the 0.40–0.60 win-rate band S3 wants; before an
  S3 win-rate is trusted, either lean on the mandatory mirror or run a larger-N Normal-vs-Normal
  win-rate calibration.
