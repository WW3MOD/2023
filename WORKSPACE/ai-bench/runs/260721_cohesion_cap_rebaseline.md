# `[cohesion-cap]` Re-Baseline — Phase 0 global cohesion cap (2026-07-21)

**Build:** `main` @ `1eb644de` (Phase 0: bound cohesion box footprint + regroup),
rebuilt clean (0 errors, DLLs 16:04 local). `git_dirty: false` at every batch launch.
**Run mode:** Mode B (minimized + framerate-uncapped, `SpeedMultiplier: 8`), fast
harness. **43 matches / 5 phases, 0 crashes / 0 no-verdict**, all `time_limit`
termination (S1 = 7500 t / 300 s; S2 = 18000 t / 720 s). Verdict_version 5,
seeded (1017…10017, `i·1000+17`).

> **Why this exists.** Commit `1eb644de` (Phase 0 of the strategic/tactical split
> spec) bounds `CohesionMoveModifier.ComputeBoxSlots` — previously a large Spread
> group fanned into a map-spanning scatter line. It fires on **every** grouped
> Move/AttackMove for humans **and bots alike**, including the frozen `@stable` and
> `@normal` controls, so it is a **declared benchmark re-baseline event**: every
> prior baseline number (including the 2026-07-21 regime re-baseline @ `60b93501`)
> is void until re-measured. This is that re-measurement. Deltas below are vs the
> `60b93501` regime re-baseline (`runs/260721_regime_rebaseline.md`), same regime
> (Motorized / same-faction US-US / vs `@stable`) — so they isolate the **cohesion
> cap** alone.

---

## Headline

**The cap moved exactly one rung — S2 Exp-vs-Stable — and only in the predicted
direction (tighter formations → more concentrated fights). Every frozen /
calibration rung is byte-identical to `60b93501`.** No pass/fail conclusion
changed: Experimental ≈ Stable on both rungs still holds, all PROPOSED bars still
do not pass, the S1 floor still holds.

| Phase | Scenario | N | Verdicts | vs `60b93501` (pre-cap) | Cap effect |
|---|---|---|---|---|---|
| 1 S1 CAL | s1-eco-cal-nn (Stable-v-Stable) | 10 | 10/10 | **byte-identical** | inert |
| 2 S1 BASE | s1-eco-river-zeta (+mirror) | 10 | 10/10 | **byte-identical** | inert |
| 3 S1 FLOOR | s1-eco-floor-vs-normal | 3 | 3/3 | **byte-identical** | inert |
| 4 S2 CAL | s2-combat-cal-nn (Stable-v-Stable) | 10 | 10/10 | **byte-identical** | inert |
| 5 S2 BASE | s2-combat-river-zeta (+mirror) | 10 | 10/10 | **MOVED** (below) | **binds** |

The determinism guarantee makes this unambiguous: identical seed → byte-identical
verdict **iff the cap changed zero orders** in that match. Four of five phases
reproduce `60b93501` to the value, so the cap altered not a single grouped move in
any S1 scenario or in the passive Stable-vs-Stable S2 calibration. It bound **only**
when Experimental's SR-contestation axis (`SrPressureScoreMultiplier: 260`) drove
an assault group large enough to exceed the new footprint cap.

---

## Phase 1 — S1 CALIBRATE (Stable-v-Stable), N=10 — INERT

Byte-identical to `60b93501`. Side lean **7–3 Russia-slot(80,35)** (`{Russia-bot:7,
USA-bot:3}`); USA-slot(14,45) gross median **6112.5**, Russia-slot **2975.5**; score
medians USA **16376** / Russia **12101**. Small Motorized economy groups never reach
the cap → deterministic replay unchanged.

## Phase 2 — S1 BASELINE (Experimental vs Stable), N=10 (5+5) — INERT

Byte-identical. Win **Exp 5 / Stable 5**; Exp capture **6/10** (primary gross median
6167, mirror 0); Stable gross median **6124**. S1 remains a non-regression floor vs
Stable, not a discriminator — and the cap does not touch it.

## Phase 3 — S1 FLOOR (Experimental vs Normal), N=3 — INERT

Byte-identical. Win **Exp 2 / Normal 1**; Exp capture **1/3**, Normal **0/3**. Weak
sanity PASS holds (single-spawn, no mirror — if ever used as a real gate, run with
the mirror at higher N).

## Phase 4 — S2 CALIBRATE (Stable-v-Stable), N=10 — INERT

Byte-identical. Side **even 5–5**; net-swing median USA **0** / Russia **−725**;
engagement-volume median USA **1200** / Russia **1925**; deaths median 725 / 1275;
score median USA **37841** / Russia **20967**. MIN-ENGAGEMENT **PASS but FRAGILE** —
the same **3/10 zero-combat** seeds (**3017, 7017, 10017**, 0/0 both sides) recur, 2
more trivial (4017, 8017 ≤ 650). The same-faction frozen-vs-frozen stalemate that
collapses engagement ~5–6× vs pre-regime is a **seed-geometry** effect the cap does
not touch (both bots passive → groups stay small → cap never binds).

## Phase 5 — S2 BASELINE (Experimental vs Stable), N=10 (5+5) — **THE CAP BINDS**

The one rung that moved. Deltas vs `60b93501`:

| Metric | pre-cap (`60b93501`) | post-cap (`1eb644de`) | Δ |
|---|---|---|---|
| Win split | Exp 5 / Stable 5 | **Exp 5 / Stable 5** | — |
| median Exp swing | −350 | **−100** | **+250** |
| relative edge (vs Stable 0) | −350 | **−100** | **+250** |
| Exp engagement-vol median | 1300 | **1775** | **+475 (+37%)** |
| Stable engagement-vol median | 2800 | **3175** | +375 |
| sign-delta (Exp > Stable) | 3/10 | **3/10** | — |
| both-spawn (primary / mirror) | 2/5 / 1/5 | **2/5 / 1/5** | — |
| engaged count (Exp eng > 0) | 7/10 | **7/10** | — |
| worst cell 6017 | Exp −4200, k/d 2/10 | Exp −4500, k/d 2/11 | shifted |
| worst cell 8017 | Exp −4950, k/d 4/10 | Exp −4950, k/d 4/10 | **identical** |

**Mechanism.** In Stable-vs-Stable (Phase 4) both bots are passive and groups stay
small, so the cap is inert and the match replays identically. In Exp-vs-Stable,
Experimental's SR-contestation axis pushes a concentrated assault at the enemy SR;
those Spread groups are the ones large enough to exceed the new `MaxWidth`/`MaxDepth`
cap, so the cap tightens them → Exp's orders diverge → the whole match diverges
(including Stable's responses). The direction is exactly what the cap predicts:
**engagement rises (Exp +37%, Stable +13%) and Exp trades slightly less badly**
(swing edge −350 → −100). Note the effect is **seed-dependent** — seed 8017 is
byte-identical (no oversized group formed there) while 6017 shifted.

**But nothing flips.** Edge −100 still fails the PROPOSED **+$1,000** S2 bar; win
still 5–5; sign-delta 3/10 and both-spawn 2/5+1/5 unchanged. The concentration
helps Exp's exchange marginally but does not manufacture a force edge over a
competent Stable defender that fights the same concentrated way.

**S2 validity gate:** engaged **7/10** (Exp eng > 0 on 1017/2017/4017/5017/6017/
8017/9017), ≥ 6 → **batch VALID**. Same three zero-combat seeds (3017/7017/10017) —
a seed-level, not bot-level or cap-level, effect. Median Exp eng 1775 (low-signal
but valid).

---

## Verdict on the cap

**Did the cohesion cap visibly change ladder behavior? — Yes, but narrowly and
harmlessly.** It bound on a single rung (S2 Exp-vs-Stable), lifting engagement ~37%
and Exp's swing edge +$250 in the predicted direction, while leaving every frozen
control and calibration byte-identical. The re-baseline's core conclusion is
**unchanged**: Experimental ≈ Stable on both rungs, all PROPOSED bars still not
passing, S1 floor holds. The cap is a human-play correctness fix (map-spanning
scatter) that, on bot-vs-bot benchmark formations at these unit counts, is inert
except on Experimental's aggressive SR-pressure pushes — where it does what it is
supposed to do (concentrate the assault) without changing the outcome.

## Proposed bars — bind against THIS build

The PROPOSED bars (LADDER re-baseline banner) remain **PROPOSED / unratified**. They
now bind against `1eb644de` (post-cap), not `60b93501`:
- **S1:** paired win-rate ≥ 0.60 AND Exp capture ≥ Stable +2/10. Current: 0.50,
  6/10 vs 6/10 → not passing; non-regression floor holds.
- **S2:** median(Exp swing) ≥ median(Stable swing) + $1,000 AND sign ≥7/10 AND
  both-spawn ≥3/5 each, + blocking validity gate ≥6/10 engaged. Current: edge −100,
  sign 3/10, both-spawn 2/5+1/5, engaged 7/10 → not passing (batch valid).

## Raw

- `tools/autotest/tournament-results/260721_p1_s1cal_cohesioncap/`
- `tools/autotest/tournament-results/260721_p2_s1base_cohesioncap/`
- `tools/autotest/tournament-results/260721_p3_s1floor_cohesioncap/`
- `tools/autotest/tournament-results/260721_p4_s2cal_cohesioncap/`
- `tools/autotest/tournament-results/260721_p5_s2base_cohesioncap/`

(Bulky match JSON is harness-owned / git-ignored; this card is the committed record.)
