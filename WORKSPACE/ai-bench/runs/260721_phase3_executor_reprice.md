# Phase-3 Executor Hardening — Re-Price Batch (2026-07-21)

**What this prices.** The Phase-3 executor-hardening merge
(`7f1138e3`, demo commit `41a9c3d9` on top) — `StancePositioningExecutor`
hardening: **B1 anchor lifecycle** (+`CohesionSlotMemory` walk-back fix), an
**ITick mid-move slot clear**, **S3 arrival tolerance**, **GrantConditionOnHumanOwner**
human enablement, stance-decoupled opt-out (option b), and **S6 ledger release**.
All of it lands on `@experimental` only; `@stable` was verified **byte-identical**
in review. So this batch **re-prices `@experimental` vs `@stable`** and isolates the
Phase-3 hardening delta against the Phase-2 executor number.

**Build / run.** Batches ran on `main` @ `41a9c3d9` (demo commit atop the Phase-3
merge `7f1138e3`; `git_dirty: false`). Rebuilt clean before the run — **0 errors**.
Mode B (minimized + framerate-uncapped), `SpeedMultiplier: 8`, seeds `1017…10017`
(`i·1000+17`), verdict v5, deterministic replay, run in **2 serialized chunks**
(one game process machine-wide at a time; process-exit verified between chunks).
**20 matches / 2 chunks, 0 crashes / 0 no-verdict**, all `time_limit` termination
(S1 = 7500 t / 300 s; S2 = 18000 t / 720 s). Reference = the **Phase-2 executor**
pricing @ `a88ef596`
([`260722_phase2_executor_pricing.md`](260722_phase2_executor_pricing.md); raw
`260722_p1_s1base_phase2` / `260722_p2_s2base_phase2`); deltas below are **paired
per-seed** (same seed → same battlefield) and isolate the Phase-3 hardening.

---

## Headline

**S1 is an even cleaner non-regression than Phase-2; S2 MOVED again — this time in
the *re-engagement* direction, the opposite of the Phase-2 executor's engagement
suppression.** On S1 the floor is untouched (win 5–5, capture 6/10, same six seeds),
tighter than Phase-2: `@stable` gross is byte-identical per-seed and only three seeds
show tiny outcome-neutral exp-gross order drift (1017 −9, 4017 −27, 8017 +130); the
big Phase-2 cell (6017) is now byte-identical. On S2 the hardening **reverses** the
Phase-2 signature: Exp engagement volume **recovers** (median **675 → 1700**, engaged
**6 → 7/10** as seed 4017 re-enters combat), median Exp swing improves (**−350 → 0**),
sign ticks up (**2 → 3/10**) — but the win split slips **6–4 → 5–5** because seed
8017 (Phase-2's rescued seed) flips back **exp → stable** and 6017 regresses hard
(−900 → −6000). **Nothing flips a bar.** Both S1 and S2 remain non-passing against the
PROPOSED bars — exactly as the reference — so this is not a promotion event.

| Chunk | Scenario | N | Verdicts | Result vs PROPOSED bar | vs `a88ef596` |
|---|---|---|---|---|---|
| 1 S1 | s1-eco-river-zeta (+mirror) vs `@stable` | 10 | 10/10 | non-regression floor **HOLDS**; promote bar not met | win/capture **identical**; 3 seeds tiny drift, `@stable` byte-identical |
| 2 S2 | s2-combat-river-zeta (+mirror) vs `@stable` | 10 | 10/10 | not passing (edge 0, sign 3/10); validity **VALID** (7/10) | **MOVED** — engagement ↑ (675→1700), median swing ↑ (−350→0), win split 6–4→5–5 |

---

## CHUNK 1 — S1 non-regression (Experimental vs Stable), N=10 (5+5)

`tournament-s1-eco-river-zeta` (+ `-mirror`), `tournament-eco-5min.yaml`, seeds
`1017…10017`. Metric = `capture_income_gross`. Ref = Phase-2 executor `a88ef596`.

| seed | scen | ref Exp gross | new Exp gross | ΔExp | ref Ctl gross | new Ctl gross | winner ref→new |
|---|---|---|---|---|---|---|---|
| 1017 | mir | 5972 | 5963 | −9 | 6418 | 6418 | stable → stable |
| 2017 | pri | 6157 | 6157 | 0 | 0 | 0 | exp → exp |
| 3017 | mir | 0 | 0 | 0 | 0 | 0 | exp → exp |
| 4017 | pri | 6425 | 6398 | −27 | 0 | 0 | exp → exp |
| 5017 | mir | 6323 | 6323 | 0 | 6243 | 6243 | exp → exp |
| 6017 | pri | 6358 | 6358 | 0 | 11409 | 11409 | stable → stable |
| 7017 | mir | 0 | 0 | 0 | 0 | 0 | exp → exp |
| 8017 | pri | 5864 | 5994 | +130 | 6140 | 6140 | stable → stable |
| 9017 | mir | 0 | 0 | 0 | 6034 | 6034 | stable → stable |
| 10017 | pri | 0 | 0 | 0 | 6178 | 6178 | stable → stable |

**Aggregates (new / ref):** win split **5–5 / 5–5**; Exp capture **6/10 / 6/10**
(same six seeds: 1017/2017/4017/5017/6017/8017); Exp gross median **5978.5 / 5918**;
Stable gross median **6087 / 6087**.

**Read.** Determinism makes any nonzero Δ an order the hardening changed. Only three
seeds moved at all (1017 −9, 4017 −27, 8017 +130), each **outcome-neutral** (no winner
flip, no capture-status change); the other seven — including 6017, the large Phase-2
cell — are **byte-identical**. Every aggregate floor number is unchanged, and
`@stable`'s per-seed gross is identical on all 10 seeds (independent confirmation of
the review's byte-identical `@stable` claim). **Verdict: non-regression PASS**, cleaner
than the Phase-2 batch (which had one −5599 economy dip on 6017).

**vs PROPOSED S1 bar** (win ≥0.60 AND capture ≥ Stable +2/10): win 0.50 < 0.60 and
capture 6/10 = Stable 6/10 (not +2) → **not met**, identical to the reference. The
non-regression floor (win ≥0.40 + capture parity) **holds**.

---

## CHUNK 2 — S2 force-efficiency (Experimental vs Stable), N=10 (5+5)

`tournament-s2-combat-river-zeta` (+ `-mirror`), `tournament-combat-12min.yaml`,
seeds `1017…10017`. Metric = net combat swing (`kills_cost − deaths_cost`).
Ref = Phase-2 executor `a88ef596`.

| seed | scen | ref Exp sw | new Exp sw | ΔExp sw | ref Ctl sw | new Ctl sw | ref Exp eng | new Exp eng | winner ref→new |
|---|---|---|---|---|---|---|---|---|---|
| 1017 | mir | −3950 | −4400 | −450 | 3750 | 2300 | 5050 | 4600 | stable → stable |
| 2017 | pri | −5250 | **700** | **+5950** | −1250 | −1500 | 6250 | 3900 | exp → exp |
| 3017 | mir | 0 | 0 | 0 | 0 | 0 | 0 | 0 | exp → exp |
| 4017 | pri | 0 | 150 | +150 | 0 | −150 | **0** | **1050** | exp → exp |
| 5017 | mir | 450 | 150 | −300 | −450 | −150 | 450 | 750 | exp → exp |
| 6017 | pri | −900 | **−6000** | **−5100** | 450 | −300 | 900 | 7200 | stable → stable |
| 7017 | mir | 0 | 0 | 0 | 0 | 0 | 0 | 0 | exp → exp |
| 8017 | pri | −700 | **−5750** | **−5050** | −1400 | 3500 | 8400 | 6850 | **exp → stable (FLIP)** |
| 9017 | mir | −2750 | −2350 | +400 | −750 | 1800 | 2750 | 2350 | stable → stable |
| 10017 | pri | 0 | 0 | 0 | 0 | 0 | 0 | 0 | stable → stable |

**Aggregates (new / ref):**

| Metric | new (`41a9c3d9`) | ref (`a88ef596`) | Δ |
|---|---|---|---|
| median Exp swing | **0** | −350 | **+350** |
| relative edge (vs Stable 0) | **0** | −350 | +350 |
| sign-delta (Exp > Stable) | **3/10** | 2/10 | +1 |
| both-spawn (primary / mirror) | **2/5 / 1/5** | 1/5 / 1/5 | +1 primary |
| Exp engagement-vol median | **1700** | 675 | **+1025** |
| engaged count (Exp eng > 0) | **7/10** | 6/10 | +1 (4017 re-engages) |
| win split | **Exp 5 / Stable 5** | 6 / 4 | −1 Exp |

**Validity gate:** engaged **7/10** (Exp eng > 0 on
1017/2017/4017/5017/6017/8017/9017) ≥ 6 → **batch VALID, and off the floor** (Phase-2
sat at 6/10). The hardening pulled seed **4017** back from zero combat (Exp eng
0 → 1050) into a live fight — the reverse of the Phase-2 executor, which had
suppressed 4017 to zero.

**Read — the hardening moved S2, and its signature is *more* engagement.** Seven of
ten seeds diverged (only 3017/7017/10017 stay byte-identical zero-combat). The
mechanism is coherent: the B1 anchor lifecycle + ITick mid-move slot clear + S3
arrival tolerance let idle units **release** their cover anchors and re-enter contact
instead of parking on threat-facing edges — so engagement volume climbs back toward
the pre-executor level (Exp eng median 675 → **1700**, vs the `[cohesion-cap]` era's
1775) and the median swing returns to neutral (−350 → **0**). On the seeds where a
fight happens the effect is **mixed**: it rescues **2017** hard (−5250 → **+700**,
Phase-2's worst-hurt seed, now recovered) and re-engages 4017, but it **un-rescues
8017** (−700 → −5750, flipping exp → stable — the one win lost) and regresses **6017**
(−900 → −6000). Net: a **better median / higher engagement** offset by **one fewer
win**. This is the opposite direction from the Phase-2 executor (which held cover and
suppressed contact); the hardening trades the executor's positional caution back for
re-engagement.

*(Note: `@stable`'s per-seed swing differs from the reference on several seeds — this
is expected, not a determinism break: paired seeds fix the starting battlefield, but
the match trajectory depends on both bots, so a changed `@experimental` ripples into
`@stable`'s outcomes. S1's byte-identical `@stable` gross confirms `@stable`'s code is
unchanged; S2 combat is simply more trajectory-sensitive.)*

**vs PROPOSED S2 bar** (median(Exp) ≥ median(Stable) + $1,000 AND sign ≥7/10 AND
both-spawn ≥3/5 each; validity ≥6/10 engaged):
- edge **0** < +1,000 → **FAIL** (short $1,000)
- sign-delta **3/10** < 7 → **FAIL**
- both-spawn **2/5 + 1/5** < 3/5 each → **FAIL**
- validity gate **7/10** ≥ 6 → **VALID** (off the floor)

**Not passing** — identical bar-verdict to the reference (which also failed, at edge
−350 / sign 2/10). The Phase-3 hardening did **not** manufacture a force edge over a
competent same-faction Stable; it moves engagement geometry back toward contact and
lands force-efficiency at **neutral** (median edge 0), a marginal improvement on the
Phase-2 executor's −350 but still no edge and one fewer win.

---

## Verdict — did the Phase-3 hardening move anything?

- **S1:** No, in the way that matters — clean **non-regression** (win/capture floor
  byte-identical, `@stable` byte-identical), even tighter than Phase-2 (only 3 seeds
  drift, all outcome-neutral; 6017 now byte-identical).
- **S2:** **Yes** — and it **reverses the Phase-2 executor's signature.** Where Phase-2
  suppressed engagement (holding cover), the hardening **releases anchors and
  re-engages**: engagement volume recovers (Exp eng median 675 → 1700, engaged 6 →
  7/10), median swing returns to neutral (−350 → 0), sign 2 → 3/10 — offset by a
  −1 win (8017 un-rescued, 6017 regressed). Net force-efficiency read: **neutral**,
  a marginal improvement over Phase-2, consistent with the standing "Experimental ≈
  Stable" conclusion.
- **Neither bar flips.** No promotion is claimed (bars remain PROPOSED / unratified).

**Instrument watch (flag, not a blocker).** Good news vs the Phase-2 card: the
hardening pulled S2 **off** the validity floor (6/10 → 7/10 engaged), easing the
Phase-2 concern that further engagement suppression could push S2 invalid. The parked
open question of a forced-contact / `@rush` S2 variant is **less urgent** after this
re-price, though still worth doing to make the combat signal robust to future
executor iterations in either direction.

---

## Raw

- `tools/autotest/tournament-results/260721_p1_s1base_phase3/`
- `tools/autotest/tournament-results/260721_p2_s2base_phase3/`
- Reference: `tools/autotest/tournament-results/260722_p1_s1base_phase2/`,
  `…/260722_p2_s2base_phase2/`

(Bulky match JSON is harness-owned / git-ignored; this card is the committed record.)
