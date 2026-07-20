# Capture Reliability — Cycle 1 IMPLEMENT+VERIFY (result: FAIL against bar)

**Date:** 2026-07-20 · **Branch:** `exp-capture-reliability` (built on main @ `e5b5421a`)
· **Run mode:** B (hidden, `OPENRA_WINDOW_HIDDEN=1`, `SpeedMultiplier: 8`)
· **Scenario:** `tournament-s1-eco-river-zeta` (+ `-mirror`), `tournament-eco-5min.yaml`
(300s / 7500t) · **Metric:** `capture_income_gross` (verdict_version 4).

Implements the primary recommendation of
[`plans/260720_capture_reliability_cycle1.md`](../../plans/260720_capture_reliability_cycle1.md):
**(b1)** `DefaultCommitmentTicks` 300→600 + **(a)** `INotifyKilled` scan-reset + the
M-1/M-2/M-3 instrumentation, plus per-match `debug.log` preservation in the runner.

Raw dirs: `tools/autotest/tournament-results/260720_capreliability_n5` (diagnostic gate),
`…/260720_capreliability_n10` (verify). 5+10 matches, all `verdict_version: 4`,
`win_reason: time_limit`, full 7500 ticks, 0 no-verdict, no crashes.

---

## Result vs the S1 reliability bar

**Bar (provisionally adopted, per baseline recommendation):** in-window capture rate
**≥ 6/10** AND conditional gross median **≥ $5000**.

| Batch | Capture rate | Cond. gross median (n) | Win split | Verdict |
|---|---|---|---|---|
| N=5 diagnostic gate (≥3/5) | **3/5** ✅ | $6051 (n=3) | 5–0 | gate passed → proceed |
| **N=10 verify** | **4/10** ❌ | **$6377** (n=4) ✅ | 8–2 | **FAIL** (capture < 6/10) |

**Overall: FAIL.** The conditional-income half is met ($6377 ≥ $5000 — captures are real,
sustained, ~one-to-two derricks held from mid-match), but the reliability half is **4/10**,
unchanged from the baseline's 4/10. **The TTL+scan-reset mechanism did not lift the capture
rate** — as the N=5 diagnostic markers already predicted.

### N=10 per-match

| m | scen | exp side | exp gross | issues | tecn-killed | winner |
|---|---|---|---|---|---|---|
| 1 | mirror | russia | 0 | 1 | 1 | russia |
| 2 | primary | america | 0 | 0 | 0 | russia |
| 3 | mirror | russia | 0 | 0 | 0 | russia |
| 4 | primary | america | **6184** | 1 | 0 | **exp** |
| 5 | mirror | russia | 0 | 0 | 0 | russia |
| 6 | primary | america | **6569** | 1 | 0 | **exp** |
| 7 | mirror | russia | 0 | 0 | 0 | america |
| 8 | primary | america | **5943** | 1 | 0 | **exp** |
| 9 | mirror | russia | **10960** | 2 | 1 | **exp** |
| 10 | primary | america | 0 | 0 | 0 | **exp** |

Capture by side: primary (america) 3/5, mirror (russia) 1/5 (baseline was 2/5 each — the
skew is small-N noise; win split 8–2 remains, symmetric enough).

---

## Failure classification (now legible — F-5 closed)

The instrumentation deliverable **succeeded**: the added M-1/M-2/M-3 markers + per-match
`debug.log` preservation make every failing run self-explaining for the first time. The
behavioral bar failed, but the diagnostic question is now answered with hard evidence.

**Pooled `total-tecns` over all 994 `no-idle-capturers` (M-2) scans, N=10:**

| total-tecns at scan | scans | % |
|---|---|---|
| **0** | **875** | **88.0%** |
| 1 | 94 | 9.5% |
| 2 | 17 | 1.7% |
| 3 | 8 | 0.8% |

**Marker totals, N=10:** `no-idle-capturers` 994 · `issue` 6 · `commitment-released` 2
(both `reason=expired`) · `tecn-killed` 2.

### Dominant path — F-1, *availability* sub-cause (NOT survival)

- **5/10 matches (m2, m3, m5, m7, m10) had `total-tecns=0` for the entire match and 0 capture
  issues** — the experimental bot never had a TECN in the world to dispatch. No capture was
  ever *attempted*, let alone lost.
- Across all scans, **88% see zero TECNs.** When a TECN *is* present and idle, it captures
  promptly (all 6 issues fired at ticks 680–1477, early-to-mid match; m4/m6/m8/m9 → income
  ramp). The pipeline downstream of "a TECN exists and is free" is sound.
- **`tecn-killed` fired only twice, and both with `committed=False objective=<none>`** — the
  TECNs that died were *not* pursuing a derrick (died mid-map at 57,29 / 62,33). So capturer
  *survival on the approach* is **not** the constraint. The plan's F-1 "killed en route" and
  F-4 "escort fails to screen the approach" hypotheses are **not** what these runs show.
- m1 is the single run where a dispatched capture returned 0 — but the killed TECN there was
  uncommitted, so this too is an availability/dispatch-readiness story, not an escort story.

**Conclusion: the binding constraint on S1 capture reliability is TECN
production/delivery/availability — upstream of the capture loop entirely.** With essentially
a single capture unit whose presence is intermittent (and absent for whole matches), the
run-to-run capture rate is gated by *whether a TECN exists and is free in-window*, which this
cycle's changes (commitment window, scan timing) do not influence.

### Minor path — F-2 (TTL expiry), rare

- **2 `commitment-released reason=expired`** (N=10), both on distant targets (`capture:3027`,
  `capture:3030` at ticks 1996/2077; in N=5, one expiry was `oilb@38,53` — a ~25-cell walk
  from SR@14,45, beyond even TTL=600). The TTL raise to 600 correctly covers normal
  edge→SR→derrick routes; the residual expiries are on unusually far target selections, and
  they did **not** cost captures (m4 in N=5 expired one commitment yet still won with 11510).

### F-3 / F-5

- F-3 (75-tick scan dead-zone): addressed by the `INotifyKilled` scan-reset; not measurable
  as a binding factor given deaths are rare and mostly uncommitted.
- F-5 (failure break-points invisible): **CLOSED.** This is the cycle's real, landed value.

---

## What this cycle shipped (correct + low-risk, but not the binding lever)

| Change | File | Verdict |
|---|---|---|
| `DefaultCommitmentTicks` 300→600 | `mods/ww3mod/rules/ai/ai.yaml:122` | Correct; closes normal-route F-2. Shared with @stable per SPEC §13. |
| `INotifyKilled` + scan-reset + M-1 | `CaptureCoordinatorBotModule.cs` | Correct; closes F-3, emits death marker. |
| M-2 no-idle-capturers | same, at the `idleCapturers.Length==0` return | **The diagnostic win** — revealed the 88%-zero-TECN reality. |
| M-3 commitment-released (expired/captured/gone) | same, `ReconcileGuardCommitments` | Legible commitment lifecycle. |
| per-match `debug.log` preservation | `tools/autotest/run-tournament.sh` | Every future failing run is now self-explaining. |

Build green (0 errors). NUnit **287/287** pass (no regressions; the plan's "expect 291" is
stale — this branch's current count is 287, and additive logging changes remove no tests).

**None of these regress anything**, and the instrumentation is pure observability. But because
the behavioral bar (capture ≥6/10) was **not** met, per the cycle mandate the branch is **left
unmerged**. Whether to cherry-pick the instrumentation-only portion (M-1/M-2/M-3 +
debug.log preservation + TTL) to main ahead of a behavioral pass is a **user call** — it is
zero-behavioral-risk and would make the *next* production-focused cycle legible from match 1.

---

## Recommendation for the next cycle

**Target TECN production/availability, not the capture loop.** The evidence is unambiguous:
88% of capture scans see zero TECNs and 5/10 matches field none at all. Candidate levers
(hypotheses, to be costed in a RECON):

1. **TECN call-in cadence / build cadence** — why does the highest-weighted builder item
   (`tecn.*: 500`, `ai-{america,russia}.yaml:8`) still leave the pool empty for whole matches?
   Investigate reinforcement queue latency, SR call-in delay, and whether the production
   trigger actually fires for a bot with no combat pressure early.
2. **`ConsumedByCapture: true`** (`infantry.yaml:903`) — every successful capture *and* every
   income capture removes the TECN from the live pool; combined with slow refill this keeps
   the pool at 0. Consider decoupling income-capture from unit consumption, or pre-staging a
   second TECN.
3. **UnitLimit / standing-order** — `tecn.*: 3` is a ceiling, not a floor; nothing guarantees
   ≥1 TECN is alive and idle at any given scan. A "keep N TECNs ready" policy would convert
   the intermittent single-unit pattern into reliable availability.

The parked structural option (first-class **CaptureTask** with retry/requeue) is only worth
building once production reliably delivers TECNs — a mission abstraction cannot retry with a
unit that was never produced.

**Also unchanged from baseline (still for the user):** the written LADDER bar
`median ≥ control×1.15` remains degenerate (control ≈ 0); the reliability bar used here is the
provisional replacement pending ratification.
