# Seeded Reference Baseline — S1, N=10 (result: PASS)

**Date:** 2026-07-20 · **Ran at:** `main` @ **`e5a1c967`** (clean, `git_dirty: false`)
· **Run mode:** B (hidden, `OPENRA_WINDOW_HIDDEN=1`, `SpeedMultiplier: 8`)
· **Scenario:** `tournament-s1-eco-river-zeta` (+ `-mirror`), config `tournament-eco-5min.yaml`
(300s / 7500t) · **Metric:** `capture_income_gross` · **Verdicts:** all **v5, `seed` stamped**.
· Raw dir: `tools/autotest/tournament-results/260720_seeded_baseline_n10`.

This is the loop's **REFERENCE BASELINE** and, because `e5a1c967` is the first build with
seeded `LocalRandom` (commit `2d3c8fe0`, verdict_version 5), it is the **first paired-comparison
reference set**. All 10 seeds now replay identical games, so **future cycles diff against THIS
per-seed table seed-by-seed** — a bot change re-run on seed *N* produces the same game modulo the
change under test, giving true paired deltas rather than independent-sample noise.

---

## Result vs the S1 reliability bar — PASS

**Bar (provisional, per the baseline recommendation ratified in cycle-2 practice):** in-window
capture rate **≥ 6/10** AND conditional gross median **≥ $5,000**.

| Metric | Value | Bar | Verdict |
|---|---|---|---|
| In-window capture rate (`gross > 0`) | **8/10** | ≥ 6/10 | ✅ |
| Conditional gross median (captured runs, n=8) | **$6,457** | ≥ $5,000 | ✅ |
| Win split (Experimental vs Normal) | **10–0** | — | ✅ decisive |
| All-runs gross median (incl. 2 zeros) | $6,378.5 | — | context |
| Normal control gross median | $0 | — | control captures ~never |

**Overall: PASS.** No crashes, no no-verdicts, no watchdog kills — 10/10 full 7500-tick matches,
all ending `time_limit`.

---

## Per-seed capture table (the paired-comparison reference)

Seed = `i*1000 + 17`; odd index → mirror (Experimental on russia/80,35), even → primary
(Experimental on america/14,45).

| seed | match | scen | exp side | exp gross | captured | winner |
|---|---|---|---|---|---|---|
| 1017 | 1 | mirror | russia | 6,185 | ✓ | exp |
| 2017 | 2 | primary | america | 0 | ✗ | exp |
| 3017 | 3 | mirror | russia | 10,315 | ✓ | exp |
| 4017 | 4 | primary | america | 11,538 | ✓ | exp |
| 5017 | 5 | mirror | russia | 6,342 | ✓ | exp |
| 6017 | 6 | primary | america | 6,499 | ✓ | exp |
| 7017 | 7 | mirror | russia | 11,446 | ✓ | exp |
| 8017 | 8 | primary | america | 0 | ✗ | exp |
| 9017 | 9 | mirror | russia | 6,265 | ✓ | exp |
| 10017 | 10 | primary | america | 6,415 | ✓ | exp |

**Capture by side:** primary/america **3/5** (misses on seeds **2017, 8017**), mirror/russia **5/5**.
Both residual misses are **america/primary side** — consistent with the cycle-2 diagnosis
(`260720_tecn_floor_cycle2_n10.md`): the america bot fires the TECN floor once, captures, then
retains an idle lottery-built capturer so the M-2-gated floor stops re-requesting; an unlucky
early-request miss (issues=0) then leaves it at $0 with nothing to re-pull. Russia-side empties
its pool via repeated captures, so the floor keeps firing and every mirror match captures.

Experimental **won all 10 regardless of derrick income** — the combat edge over Normal is already
decisive; held-derrick income (via the gross-income scorer axis, verdict v4) is additive on top.

---

## Relationship to cycle-2 (the merged bot this baseline measures)

| Batch | Build | Seeding | Capture | Cond. gross median | Win |
|---|---|---|---|---|---|
| Cycle-2 verify (`260720_tecn_floor_cycle2_n10.md`) | pre-determinism | labels only (DateTime.Now) | 8/10 | $7,726 | 10–0 |
| **This seeded baseline** | `e5a1c967` | **true replay (v5)** | **8/10** | **$6,457** | **10–0** |

Same tier: capture reliability **8/10** and win split **10–0** reproduce exactly; conditional
income lands a bit lower ($6,457 vs $7,726) but within the same regime. Cycle-2's seeds were
*labels* (the binary still drew from an unseeded `LocalRandom`), so its games are **not**
reproducible and its numbers are an independent sample; this batch is the first whose seeds are
reproducibility guarantees. The two residual misses shifted from {m2 america, m7 russia} (cycle-2)
to {m2, m8 both america}, but the **america-side production/dispatch throughput** weakness is the
same binding constraint in both — the documented next-cycle target (extend the floor past the
M-2 gate + escort-bundled reinforcement packaging).

---

## Verdict integrity

All 10 verdicts: `verdict_version: 5`, `seed` field present and equal to `i*1000+17`
(1017…10017), `git_sha: e5a1c967…`, `git_dirty: false`. Zero non-v5 / unseeded verdicts.

---

## Handoff / next

- **S1 bar:** PASS (8/10 capture, $6,457 conditional median, 10–0). Reference set locked.
- **Paired-comparison protocol:** future S1 cycles re-run seeds 1017…10017 and diff the per-seed
  `exp gross` + captured flag against this table; seeds 2017/8017 (america misses) are the
  natural regression/improvement watch cells.
- **PROMOTE:** see the SPEC §13 requirement summarized in the accompanying report — no
  Experimental-vs-Stable head-to-head is *required* by §13 (bar-cleared + user acceptance), but
  DOCTRINE's PROMOTE trigger wants head-to-head evidence, which would need a new scenario pair +
  one N=10 batch (not run here).
