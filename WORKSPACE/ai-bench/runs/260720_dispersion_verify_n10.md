# Dispersion Doctrine — VERIFY N=10 (result: PASS — activation + non-regression)

**Date:** 2026-07-20 · **Branch:** `exp-dispersion` @ `ad71ed54` (merge of main
`b6a43460` into the parked dispersion branch `e51e1c3f`)
· **Run mode:** B (hidden, `OPENRA_WINDOW_HIDDEN=1`, `SpeedMultiplier: 8`)
· **Scenario:** `tournament-s1-eco-river-zeta` (+ `-mirror`), `tournament-eco-5min.yaml`
(300s / 7500t) · **Metric:** `capture_income_gross` (verdict_version 4).

Raw dir: `tools/autotest/tournament-results/260720_dispersion_verify_n10`. 10 matches
(5 primary + 5 mirror via `--mirror`), all `verdict_version: 4`, `win_reason: time_limit`,
full 7500 ticks, **0 no-verdict**, no crashes. `git_dirty: false`.

**This is a NON-REGRESSION + ACTIVATION verify, not a capture-improvement claim.** S1 is an
*economy* scenario; the dispersion doctrine is a *combat-movement* mechanic, so S1 can only
show that dispersion did not break the validated economy behavior (non-regression) and that
the switch is firing on live axes (activation). Any capture-rate delta here is **confounded**
(see §3) and is *not* attributed to dispersion.

Pre-merge gates: build green (0 errors); NUnit **291/291** (287 from main + 4 new
`PoiOffenseTest` cases: `Chebyshev_…`, `CellCentroid_…`, `MaxChebyshev_…`,
`AssaultGate_…`). Kill-switch gating verified: `CohesionSwitchEnabled` defaults **false**
in `PoiOffensiveBotModuleInfo` (`PoiOffensiveBotModule.cs:87`), set `true` only on
`PoiOffensiveBotModule@experimental` (`ai.yaml:188`); `@stable` and Normal carry none of the
cohesion fields, so their controls are untouched.

---

## 1. ACTIVATION — the AssaultRadiusCells=15 switch fires cleanly ✅

The dispersion order path emits `[exp-offense] order … cohesion={mode} clumpRadius={r}
distToTarget={d}` only when `CohesionSwitchEnabled && axis.Units.Count > 0` — i.e. the
`cohesion=` token itself is proof the experimental switch is live.

**Pooled over all 10 matches' preserved `match_<i>_debug.log`:**

- **130 `[exp-offense] order` lines carry `cohesion=`** — 52 `Spread`, 78 `Tight`.
- **Gate invariant holds with ZERO violations:**
  - Every `cohesion=Spread` order fired at `distToTarget` **16–61** (all > 15).
  - Every `cohesion=Tight` order fired at `distToTarget` **3–15** (all ≤ 15).
  - 0 Spread-lines with dist ≤ 15; 0 Tight-lines with dist > 15.

The distance→mode gate is provably firing exactly on the `AssaultRadiusCells = 15`
threshold: axes spread while their centroid is far, then mass for the final push.

**Representative lines (`cohesion=<mode> clumpRadius=<r> distToTarget=<d>`):**

```
Spread   clumpRadius=19  distToTarget=61     # far approach, army fanned out
Spread   clumpRadius=21  distToTarget=37
Spread   clumpRadius=11  distToTarget=43
Tight    clumpRadius=12  distToTarget=6      # closing on the objective, massing
Tight    clumpRadius=13  distToTarget=4
Tight    clumpRadius=14  distToTarget=3
```

**Caveat on `clumpRadius`:** it is the *observed current* max-Chebyshev spread of the axis
at order time, which **lags** the commanded spacing (units take ~5 ticks to re-slot) and is
scattered by combat *at* the objective. So Tight-near-target lines still show clumpRadius
10–14 (units converging/engaged, not yet compressed) and Spread lines span 4–21. The crisp,
unambiguous activation signal is therefore the **mode-selection gate** (0 violations above),
not the raw clumpRadius magnitude. clumpRadius confirms the telemetry is wired and non-trivial;
the gate confirms the doctrine is active.

---

## 2. NON-REGRESSION — all three bars met ✅

Bars (from the cycle mandate): win split experimental **≥ 7/10**, in-window capture
**≥ 4/10**, conditional gross median **≥ $5,000**. Baseline (`f8052ecf`,
`260720_s1_baseline_n10.md`): 8–2, 4/10, $6,047.

| Metric | Bar | This batch | Baseline | Verdict |
|---|---|---|---|---|
| Win split (experimental) | ≥ 7/10 | **9/10** (9–1) | 8–2 | ✅ |
| In-window capture rate | ≥ 4/10 | **9/10** | 4/10 | ✅ |
| Conditional gross median (gross>0) | ≥ $5,000 | **$11,191** (n=9) | $6,047 (n=4) | ✅ |

### Per-match (experimental vs normal, N=10)

| m | scen | exp side | exp gross | exp score (a/cap/k) | ctl gross | winner | reason |
|---|---|---|---|---|---|---|---|
| 1 | mirror | russia | 6423 | 1000/12846/1000 | 0 | **exp** (russia) | time_limit |
| 2 | primary | america | 6246 | 550/12492/1500 | 0 | **exp** (america) | time_limit |
| 3 | mirror | russia | 14610 | 2600/29220/900 | 0 | **exp** (russia) | time_limit |
| 4 | primary | america | 6547 | 3500/13094/6500 | 0 | **exp** (america) | time_limit |
| 5 | mirror | russia | 13706 | 0/27412/2700 | 0 | **exp** (russia) | time_limit |
| 6 | primary | america | 11214 | 5800/22428/3600 | 0 | **exp** (america) | time_limit |
| 7 | mirror | russia | 11191 | 3400/22382/2600 | 0 | **exp** (russia) | time_limit |
| 8 | primary | america | 6492 | 750/12984/4850 | 0 | **exp** (america) | time_limit |
| 9 | mirror | russia | 11530 | 300/23060/5450 | 0 | **exp** (russia) | time_limit |
| 10 | primary | america | 0 | 1500/0/1300 | 0 | ctl (russia) | time_limit |

- Experimental gross median ALL: **$8,869** | primary (america) $6,492 (n=5) | mirror
  (russia) $11,530 (n=5). Normal (control) captured **0/10** → control gross median $0.
- Win split **9–1**, symmetric by spawn: exp won 5/5 as russia (mirror) and 4/5 as america
  (primary); the lone loss (m10) is a no-capture combat loss. No spawn artifact.
- No regression on any axis: the validated economy + win behavior is intact with dispersion on.

---

## 3. On the capture-rate jump (4/10 → 9/10) — CONFOUNDED, not credited to dispersion

The capture rate is far above baseline, but this cycle changed **two** things vs the
`f8052ecf` baseline and **must not** attribute the delta to dispersion:

1. **Main's cycle-1 merge (`b6a43460`):** `DefaultCommitmentTicks` 300→600 + the
   `CaptureCoordinator` `INotifyKilled` scan-reset + M-markers. These directly target the
   capture pipeline. (Note the tension: cycle-1's *own* N=10 measured 4/10 *with* these
   changes on `exp-capture-reliability` — so TTL alone did not move the rate there. Either
   the merged state at `b6a43460` differs, or the earlier 4/10 was partly variance.)
2. **The dispersion doctrine itself** — but it excludes `tecn`/`e6` from axes
   (`ai.yaml:183`) and touches only how the *offense* pool moves, with **no path to TECN
   availability**, which cycle-1 identified as the binding constraint (88% of scans see zero
   TECNs). A second-order "spread army wins more engagements → more surviving economy → more
   TECN production" story is *plausible but unproven* and not claimed here.

Seeds are run labels, not reproducibility guarantees (bots draw from an unseeded
`LocalRandom`), so run-to-run variance is also live. **Bottom line:** the improvement is
real in this batch but unattributable; the honest reading is non-regression pass + a
confounded upside. A clean causal test would A/B dispersion alone (`CohesionSwitchEnabled:
false` vs `true`) on identical merged code — worth a follow-up if the capture signal matters.

---

## 4. Decision

**PASS on BOTH activation and non-regression → merge `exp-dispersion` into `main`.**

The dispersion switch is live and correct, unit tests and build are green, and the S1
economy behavior did not regress. **S1 cannot exhibit dispersion's real value** — spread-to-
move / mass-to-assault is a *combat-survival* mechanic, and S1 is scored on capture income
with the control fielding no army pressure worth dispersing against. Dispersion's real combat
signal awaits an **S2-class (combat-efficiency) rung** where mean pairwise spacing en route
vs at assault, and units-lost-on-approach, are the metrics that matter (design §3b).

SHA verified against: `ad71ed54` (exp-dispersion HEAD = batch `git_sha`).
