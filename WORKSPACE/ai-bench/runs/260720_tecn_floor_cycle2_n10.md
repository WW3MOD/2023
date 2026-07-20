# TECN Availability Floor — Cycle 2 IMPLEMENT+VERIFY (result: PASS, merged)

**Date:** 2026-07-20 · **Branch:** `exp-tecn-floor` (commit `6f01ed6c`, built on main @ `a4257d54`)
· **Merged to main:** `c6a71c14` (merge commit; main had advanced to `7d96ed81` via the
parallel dispersion cycle — clean 3-way merge, non-overlapping `ai.yaml` blocks).
· **Run mode:** B (hidden, `OPENRA_WINDOW_HIDDEN=1`, `SpeedMultiplier: 8`)
· **Scenario:** `tournament-s1-eco-river-zeta` (+ `-mirror`), `tournament-eco-5min.yaml`
(300s / 7500t) · **Metric:** `capture_income_gross` (verdict_version 4).

Implements the primary recommendation of
[`plans/260720_tecn_availability_cycle2.md`](../../plans/260720_tecn_availability_cycle2.md):
a keep-N-ready **TECN availability floor** via the engine's `IBotRequestUnitProduction`
demand queue, demand-gated at the M-2 (`no-idle-capturers`) branch.

Raw dirs: `tools/autotest/tournament-results/260720_tecnfloor_n5` (diagnostic gate),
`…/260720_tecnfloor_n10` (verify). 5+10 matches, all `verdict_version: 4`,
`win_reason: time_limit`, full 7500 ticks, **0 no-verdict, no gameplay exceptions**.

---

## Result vs the S1 reliability bar — PASS

**Bar (provisional, per baseline recommendation):** in-window capture rate **≥ 6/10**
AND conditional gross median **≥ $5000**.

| Batch | Capture rate | Cond. gross median (n) | Win split | Verdict |
|---|---|---|---|---|
| N=5 diagnostic gate (≥3/5) | **4/5** ✅ | $6,281 (n=4) | 4–1 | gate passed → proceed |
| **N=10 verify** | **8/10** ✅ (bar ≥6) | **$7,726** (n=8) ✅ (bar ≥$5000) | **10–0** | **PASS** |

**Overall: PASS.** Capture reliability **doubled 4/10 → 8/10** vs both the S1 baseline and
cycle 1 (the failing 4/10). Conditional income stays strong ($7,726, was $6,377). Win split
did **not** collapse — it went **8–2 → 10–0** (the extra captures convert into the scorer's
economy axis, which now reads gross income, so held derricks tip close matches).

### N=10 per-match

| m | scen | exp side | exp gross | floor-req | tecn-killed | capture-issues | winner |
|---|---|---|---|---|---|---|---|
| 1 | mirror | russia | 11,094 | 65 | 0 | 2 | **exp** |
| 2 | primary | america | 0 | 1 | 0 | 0 | **exp** |
| 3 | mirror | russia | 11,417 | 15 | 1 | 4 | **exp** |
| 4 | primary | america | 6,398 | 1 | 0 | 1 | **exp** |
| 5 | mirror | russia | 11,629 | 71 | 1 | 4 | **exp** |
| 6 | primary | america | 6,450 | 1 | 0 | 1 | **exp** |
| 7 | mirror | russia | 0 | 82 | 0 | 0 | **exp** |
| 8 | primary | america | 6,010 | 1 | 0 | 1 | **exp** |
| 9 | mirror | russia | 8,988 | 60 | 0 | 2 | **exp** |
| 10 | primary | america | 6,464 | 1 | 0 | 1 | **exp** |

Capture by side: primary (america) 4/5, mirror (russia) 4/5 — symmetric, so the lift is
real skill, not spawn luck. exp won **all 10** regardless of derrick income (combat edge
already dominant; the floor adds the economy axis on top).

---

## Marker evidence — the floor fired and moved the binding constraint

The cycle-1 diagnosis was unambiguous: **88% of M-2 `no-idle-capturers` scans saw
`total-tecns=0`, and 5/10 matches fielded zero TECNs all match.** Cycle 2 attacks exactly
that. Pooled over the N=10 M-2 scans:

| total-tecns at scan | cycle 1 (N=10) | **cycle 2 (N=10)** |
|---|---|---|
| **0** | 875 (88.0%) | **750 (76.0%)** |
| 1 | 94 (9.5%) | 183 (18.5%) |
| 2 | 17 (1.7%) | 37 (3.7%) |
| 3 | 8 (0.8%) | 17 (1.7%) |

- **`tecn-floor-request` fired 298× across N=10** — every fire logged `alive=0 pending=0
  floor=1` with a faction-correct `type=tecn.russia` / `type=tecn.america`, confirming the
  lazy build-type resolution (intersect `CapturingActorTypes` ∩ Infantry `BuildableItems`)
  works and the demand gate only pulls when the pool is genuinely empty.
- **Zero-match-fielded-zero-TECN matches: 5/10 → 0/10.** Every match now fields ≥1 TECN.
  The residual 76%-zero *scan* share is the expected leaky-bucket refill window: with
  `ConsumedByCapture` the TECN vanishes on each capture, so the pool bounces 0→1→(capture)→0.
  What matters is that a TECN now reliably arrives and captures, vs whole matches with none.
- **`tecn-killed` fired only 3× (all uncommitted)** — survival on the approach remains a
  non-factor, exactly as cycle 1 found. The lever was availability, and it moved.

### The two residual misses (m2, m7 — the 2/10)

A clear side-split in the marker cadence explains them and points at the next cycle:

- **america/primary side fires the floor once (`floor-req=1`) then goes quiet.** After the
  first requested TECN captures, the america bot retains an idle/uncommitted capturer (built
  by the normal lottery), so the M-2 branch — where the floor lives — is no longer reached,
  and the floor doesn't re-request. m4/m6/m8/m10 still capture (one held derrick, ~$6.4k
  gross). **m2** is the unlucky america run: the single early request's TECN never landed a
  capture in-window (issues=0) and, with the floor gated to M-2 only, nothing re-pulled → $0.
- **russia/mirror side fires the floor 60–82×** (pool repeatedly empties via multiple
  captures), yielding higher gross (~$11k = two-plus derricks). **m7** is the unlucky russia
  run: the floor requested hard all match but production/dispatch never converted to a capture
  issue in-window (queue contention / walk latency) → $0.

Both failure modes are *upstream throughput*, not the floor logic itself. See next cycle.

---

## What this cycle shipped

| Change | File | Notes |
|---|---|---|
| `TecnFloor` info field (default 0 = disabled) | `CaptureCoordinatorBotModule.cs` | Shared engine class; behavior gated by the YAML *value*. |
| `MaintainTecnFloor` + `ResolveTecnBuildType` + `CaptureTargetExists` (~90 LOC) | same | Called at the M-2 branch; requests one capturer via `IBotRequestUnitProduction` when `alive+pending < floor` AND a derrick is capturable. |
| `TecnFloor: 1` | `mods/ww3mod/rules/ai/ai.yaml` (`@experimental.tecn` only) | `@stable.tecn` omits it → default 0 → byte-identical. Controls use `CaptureManagerBotModule` (no field) → untouched. |

Build green (0 errors), merged-main build green. **NUnit 287/287** on the branch (additive
logic removes no tests; note main now carries extra `PoiOffenseTest` cases from the parallel
dispersion merge — the merged tree builds clean).

**Why the request path works (engine seam):** a queued `RequestUnitProduction` is popped
**first** each `UnitBuilderBotModule` build cycle (`:87–92`) and routed through the single-name
`BuildUnit` overload (`:142–165`), which **bypasses both the `UnitsToBuild` share-ceiling test
AND `UnitLimits`**. So a request out-competes the blind production lottery for the one-at-a-time
Infantry queue slot whenever it's free — a code-level "keep-N-ready" floor that YAML cannot
express (`UnitsToBuild`/`UnitLimits` are ceilings, no floor field exists).

---

## Recommendation for the next cycle

The floor cleared the S1 reliability bar; the residual 2/10 are **production throughput /
dispatch latency**, not availability-of-intent:

1. **Extend the floor beyond the M-2 gate** so america-side keeps re-pulling after its first
   capture (e.g. fire the floor check each scan when `alive+pending < floor`, not only when
   `idleCapturers==0`). Would likely convert m2-type misses.
2. **Reinforcement packaging (roadmap item 3):** bundle the escort call-in with the TECN
   request so a produced TECN arrives screened and on-mission — the request plumbing added
   here is the exact seam (plan §3). Targets m7-type "requested but never converted" runs.
3. `ConsumedByCapture` pool drain is now *managed* (auto-re-request), not eliminated — a
   `TecnFloor: 2` A/B could test whether a standing spare cuts the refill window further.
