# S1 Economy Race — Statistical Baseline (N=10 × 2 batches)

**Date:** 2026-07-20 · **Build:** `ai-bench` @ `f8052ecf` (v4 scorer + S1 mirror merged)
· **Run mode:** B (hidden, `OPENRA_WINDOW_HIDDEN=1`, `SpeedMultiplier: 8`)
· **Scenario:** `tournament-s1-eco-river-zeta` (+ `-mirror`), `tournament-eco-5min.yaml`
(300s / 7500t) · **Metric:** `capture_income_gross` (verdict_version 4).

Two batches, each N=10, run sequentially (one game process machine-wide):

1. **Batch 1 — Experimental vs Normal** (5 primary + 5 mirror via `--mirror`).
2. **Batch 2 — Normal vs Normal calibration** (`tournament-s1-eco-cal-nn`, N=10) —
   side-fairness probe with identical bots.

Plus **2 isolated N=1 diagnostics** on the primary (to preserve a per-match
`debug.log`, which the batch runner overwrites).

Raw dirs: `tools/autotest/tournament-results/260720_b1_s1_exp_vs_normal`,
`…/260720_b2_s1_cal_normal`, `…/260720_diag{1,2}_s1_primary`.
All 20+2 matches wrote verdicts (0 no-verdict); all `verdict_version: 4`,
`win_reason: time_limit`, full 7500 ticks. No crashes, no stacktraces.

---

## Batch 1 — Experimental vs Normal (N=10)

Experimental plays **america/14,45** on primary matches (even i) and
**russia/80,35** on mirror matches (odd i). `score(a/cap/k)` =
`score_components` army_value / capture_income / kills_value. `capture_income`
= `gross × 2` (CaptureIncomeWeight 2.0) — the v4 scorer term.

| m | scen | exp side | exp gross | exp score (a/cap/k) | ctl gross | ctl score (a/cap/k) | winner | 
|---|---|---|---|---|---|---|---|
| 1 | mirror | russia | 0 | 300/0/5200 | 0 | 600/0/1000 | **exp** (russia) |
| 2 | primary | america | 0 | 400/0/2050 | 0 | 1200/0/6000 | ctl (russia) |
| 3 | mirror | russia | 0 | 1900/0/5650 | 0 | 300/0/2200 | **exp** (russia) |
| 4 | primary | america | **6074** | 0/12148/100 | 0 | 1200/0/4500 | **exp** (america) |
| 5 | mirror | russia | **5882** | 3150/11764/2450 | 0 | 0/0/550 | **exp** (russia) |
| 6 | primary | america | 0 | 300/0/4000 | 0 | 1250/0/450 | **exp** (america) |
| 7 | mirror | russia | 0 | 1800/0/1100 | 0 | 2500/0/1850 | ctl (america) |
| 8 | primary | america | **6020** | 0/12040/1800 | 0 | 1750/0/4300 | **exp** (america) |
| 9 | mirror | russia | **11355** | 1000/22710/3150 | 0 | 600/0/2300 | **exp** (russia) |
| 10 | primary | america | 0 | 2150/0/3600 | 0 | 700/0/300 | **exp** (america) |

**Headline numbers (Batch 1):**

- **Experimental in-window capture rate: 4/10 (40%).** Captured on m4, m5, m8, m9.
- **Gross median (all 10): 0** — because 6/10 captured nothing, the median sits
  in the zero mass. Per side: **primary (america) median 0** (captures 2/5),
  **mirror (russia) median 0** (captures 2/5). Capture is **symmetric across
  spawns** (2/5 each).
- **Gross when captured** (n=4): {5882, 6020, 6074, 11355}, median **6047**.
  The 11355 is a double-derrick hold; the rest are single derricks (~$50/tick
  CashTrickler × 2 weight, held from mid-match).
- **Normal (control) captured 0/10** → control gross median **0**.
- **Win split: Experimental 8, Normal 2**, and **symmetric by spawn**: exp won
  **4/5 from america** (primary) and **4/5 from russia** (mirror). The 8–2
  dominance is therefore *not* a spawn artifact — the mirror cancelled it.
- When exp captures, `capture_income` (×2 gross) dominates its score and it wins;
  its two losses (m2, m7) are combat losses with no capture.

## Diagnostics (2× N=1 primary, debug.log preserved)

| run | exp gross | capture markers (issue / poimap-scan / pre-scan) | outcome |
|---|---|---|---|
| diag1 | **11236** (2 derricks) | 3 / 3 / 38 | exp win, income ramp from ~t2625 |
| diag2 | **6131** (1 derrick) | 1 / 1 / 12 | exp win |

Both isolated diagnostics captured → combined across all 12 experimental runs the
capture rate is **6/12 (50%)**. When the pipeline fires it issues 1–3 capture
orders and the score ramps linearly (~+500/125t scored) to $11k–$24k by t7500
(diag1 `watcher.log`). No `[exp-capture] … retire` events in the successes — the
TECN reaches and *holds* the derrick.

---

## Batch 2 — Normal vs Normal calibration (N=10, side-fairness)

Identical bots (both `@normal`) on the same terrain. With identical bots the
"mirror" swap is a **no-op** (swapping two identical bots yields a byte-identical
setup), so a single scenario at N=10 is the correct calibration: any win-rate or
score skew is **pure spawn/side bias**. Labelled by faction/slot —
america = USA/14,45, russia = 80,35.

| m | america gross | america score | russia gross | russia score | winner |
|---|---|---|---|---|---|
| 1 | 0 | 0 | 0 | 7950 | russia |
| 2 | 0 | 2500 | 0 | 2550 | russia |
| 3 | 0 | 3300 | 0 | 3750 | russia |
| 4 | 0 | 350 | 0 | 3600 | russia |
| 5 | 0 | 8200 | 0 | 550 | america |
| 6 | 0 | 5900 | 0 | 4500 | america |
| 7 | 0 | 1450 | 0 | 5950 | russia |
| 8 | **5819** | 15438 | 0 | 800 | america |
| 9 | 0 | 3250 | 0 | 1800 | america |
| 10 | 0 | 1700 | 0 | 4600 | russia |

**Side-fairness findings:**

- **Win split: russia (80,35) = 6, america (14,45) = 4.** For N=10 a 6–4 split is
  well within binomial noise (a fair coin gives a ≥6–4 imbalance ~75% of the
  time). No statistically meaningful win-rate bias.
- **Score median: america 2875 vs russia 3675** — russia ~**28% higher**. A
  *mild* russia-side lean in scoring, consistent with the 6–4 win tilt. Modest,
  not damning.
- **Normal captured once** (m8 america, gross 5819) → Normal's incidental capture
  rate is ~**1/20** across both batches (0/10 as control in Batch 1 + 1/10 here).
  Normal is **not strictly zero-capture** — it has weak, occasional capture
  behaviour, so "control ≈ 0" is *approximately* (not exactly) true.

**Verdict — is the map+harness side-fair?** *Mostly yes, with a mild
russia/80,35 lean.* The lean (6–4 wins, +28% median score) is small and within
N=10 noise, and — critically — it is **neutralised in the Experimental batch by
the mirror**: Experimental won 4/5 from *each* spawn, so its 8–2 result cannot be
spawn luck. Recommend keeping the mirror mandatory for all S1 reporting (already
LADDER policy) and, if the russia lean persists at larger N, treating primary and
mirror medians separately rather than pooling. No map fix required now.

---

## Verdict against the S1 advancement bar

**LADDER S1 bar (as written):** `median(exp gross) ≥ median(control gross) × 1.15`.

- control gross median = **0** (Normal captures ~1/20). Bar = 0 × 1.15 = **0**.
- exp gross median = **0** (40–50% capture rate → median in the zero mass).
- 0 ≥ 0 is a **trivial, meaningless "pass."**

**The bar as written is degenerate and non-discriminating.** A percentage margin
on a ~0 control is ill-defined (LADDER already hedged this): ×1.15 of ~0 is still
~0, so *any* experimental median — including 0 — formally satisfies it. It can
neither fail a bad bot nor distinguish a good one. **Do not report S1 as passed
on this bar.**

### Recommended better-formed bar (recommendation only — not applied to LADDER)

Capture income here is **bimodal** (a run yields ~0 *or* ~$6k+) and the rate is
~50%, so the **median is the wrong statistic** until capture rate exceeds 50%
(below that it is pinned at 0). Gate on **reliability first**:

1. **Primary gate — in-window capture rate ≥ 6/10** (`gross > 0`), vs Normal's
   ~1/20 floor. This measures the facet S1 actually exercises: does the
   PoiMap → CaptureCoordinator pipeline *reliably* convert reachable derricks
   into held income within the 5-min clock. 6/10 is a meaningful reliability bar
   an order of magnitude above the control floor.
2. **Secondary — conditional gross median ≥ $5000** over the captured runs
   (~one derrick held from mid-match; observed 6047). Confirms captures are real
   sustained income, not transient touches.
3. **Once capture rate > 50%**, the *absolute* gross median becomes nonzero and
   can replace (1)+(2) with a single **median gross ≥ $3000** bar.

Rationale: the percentage-vs-control form assumes a live, nonzero control
baseline; here the control is structurally ~0, so an **absolute** reliability +
income bar is the honest, discriminating replacement (SPEC §6.3 fixed-target).
Flagged in REVIEW for user ratification before any LADDER edit.

---

## Why capture reliability is only ~50% (failure characterization)

Capture rate 4/10 (batch) / 6/12 (with diagnostics) is **below the ~6/10
threshold**, so per the cycle mandate I characterized the failures.

- **Failing runs' `watcher.log` score curves are flat at match end** (m2, m3, m6,
  m7 all plateau — combat-only jumps, **no sustained income ramp**). The
  signature of a held derrick — a monotonic ~+500/125t climb — is simply absent.
  So the failure is "**no derrick ever held to term**," not "held then lost"
  (which would show a ramp that stops).
- **The pipeline is not broken** — the two diagnostics (and batch m4/5/8/9) show
  it issues 1–3 `[exp-capture] issue` orders and, when the capturer survives,
  ramps income linearly to $11k–$24k with **no `retire`**. The prior cycle's
  scoring fix (delisting the $0 logisticscenter) and the v3/v4 metric work all
  hold: when a TECN reaches a derrick, everything downstream works.
- **The variance is upstream, in TECN availability/survival.** Experimental
  fields essentially a *single* capture unit; across independent unseeded
  `LocalRandom` draws it sometimes never commits one in-window, or the lone TECN
  is lost to combat before/without reaching a derrick (the map's derricks sit
  ~3–4 cells from each SR but the run is a live 2-bot fight). This is a
  **single-point-of-failure / reliability** gap, not a scoring or map-content gap.

  *(Caveat: both isolated diagnostics happened to capture, so I have preserved
  `debug.log` for two **successes** but not a **failure** — the batch runner
  overwrites `debug.log` each match. The failure characterization above rests on
  the persistent per-match `watcher.log` score curves, not `[exp-capture]`
  markers of a failing run. A future capture-reliability cycle should preserve
  `debug.log` per match, or add per-match capture markers to `watcher.log`, to
  read the exact break point (never-dispatched vs died-en-route vs target-churn).)*

---

## Recommendation for the next cycle

**Make the next behaviour cycle capture-reliability, not SR-contestation.**

- S1 is the economy rung and it is currently gated by a ~50% capture rate. No
  amount of SR-contestation play (the parked `260720_sr_contestation_cycle1.md`
  plan) will let S1 pass while half the games convert zero income. Reliability is
  the binding constraint on this rung.
- Concrete levers for that cycle (hypotheses, in rough priority):
  1. **Dispatch a second/backup capturer** so a single TECN loss doesn't zero the
     run (removes the single-point-of-failure).
  2. **Escort the capturer** (CaptureCoordinator already has an escort path —
     `exp-capture escort dispatched`) or dispatch earlier, before contact.
  3. **Re-issue on capturer loss** — if the committed TECN dies before capturing,
     immediately re-task another (watch for `[exp-capture] retire` / churn).
- Instrument first: preserve per-match `debug.log` (or fold capture markers into
  `watcher.log`) so a failing run's break point is legible, then target the lever
  that the evidence implicates.
- **Also flag to the user:** re-form the S1 bar (above) before S1 is scored as a
  pass; and the mild russia/80,35 map lean — keep the mirror mandatory.

**Bottom line:** harness + metric + scoring are sound and side-fair (mirror
neutralises the mild spawn lean). Experimental clearly *out-plays* Normal (8–2,
symmetric) and *can* win the eco race decisively when it captures ($11k–$24k
scored). The one thing between here and an S1 pass is **capture reliability** —
lift the in-window capture rate from ~50% toward ≥6/10.
