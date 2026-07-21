# Phase-2 Positioning Executor — Pricing Batch (2026-07-22)

**What this prices.** `StancePositioningExecutor`
(`engine/OpenRA.Mods.Common/Traits/StancePositioningExecutor.cs`, merged
`a88ef596` via `phase2-executor`) — idle-only, stance-conditioned repositioning of
`@experimental` bot units to threat-facing cover edges. ACTIVE for `@experimental`
(via `GrantConditionOnBotOwner@tacpos` + `enable-ai-experimental`); inert-by-
construction for `@stable`/`@normal`/humans. Since the `[cohesion-cap]` re-baseline
(`1eb644de`), the only behavior-bearing delta on Experimental is Phase 1 (pure data
layers, expected inert) + this executor — so this batch **prices the executor**.

**Build / run.** Batches ran on `main` @ `1a65ddf1` (a docs-only review commit atop
the executor merge `a88ef596`; `git_dirty: false`). Rebuilt clean before the run —
**0 errors**. Main has since advanced to `56e953b7` (a docs curation pass touching
only `DOCS/reference/*` + `DISCOVERIES.md` — behavior-inert; the executor code and
both S1/S2 scenario configs are byte-identical across `1a65ddf1..56e953b7`), so this
card is committed on `56e953b7` while the measurements are stamped to `1a65ddf1`.
Mode B (minimized + framerate-uncapped), `SpeedMultiplier: 8`, seeds `1017…10017`
(`i·1000+17`), verdict v5, deterministic replay. **20 matches / 2 chunks, 0 crashes /
0 no-verdict**, all `time_limit` termination (S1 = 7500 t / 300 s; S2 = 18000 t /
720 s). Reference = `[cohesion-cap]` re-baseline @ `1eb644de`
([`260721_cohesion_cap_rebaseline.md`](260721_cohesion_cap_rebaseline.md)); deltas
below are **paired per-seed** (same seed → same battlefield) and isolate the
executor.

---

## Headline

**The executor is a clean non-regression on S1 and it DOES move S2 — but in the
"less-engagement" direction, not the "better-trade" direction.** On S1 the floor is
untouched (win 5–5, capture 6/10, same six capturing seeds). On S2 it perturbs
engagement geometry substantially: total engagement volume **drops** (Exp eng median
1775 → **675**, Stable 3175 → **450**) as idle Experimental units hold cover edges
instead of pressing contact; median Exp swing edges **worse** (−100 → **−350**),
while the win split ticks up **5–5 → 6–4** on a single rescued seed (8017, a −4950
loss → −700 win). **Nothing flips a bar.** Both S1 and S2 remain non-passing against
the PROPOSED bars — exactly as the reference — so this is not a promotion event.

| Chunk | Scenario | N | Verdicts | Result vs PROPOSED bar | vs `1eb644de` |
|---|---|---|---|---|---|
| 1 S1 | s1-eco-river-zeta (+mirror) vs `@stable` | 10 | 10/10 | non-regression floor **HOLDS**; promote bar not met | win/capture **identical**; some intra-match drift |
| 2 S2 | s2-combat-river-zeta (+mirror) vs `@stable` | 10 | 10/10 | not passing (edge −350, sign 2/10); validity **VALID** (6/10, at floor) | **MOVED** — engagement ↓, median swing ↓, win split 5–5→6–4 |

---

## CHUNK 1 — S1 non-regression (Experimental vs Stable), N=10 (5+5)

`tournament-s1-eco-river-zeta` (+ `-mirror`), `tournament-eco-5min.yaml`, seeds
`1017…10017`. Metric = `capture_income_gross`.

| seed | scen | ref Exp gross | new Exp gross | ΔExp | ref Ctl gross | new Ctl gross | winner ref→new |
|---|---|---|---|---|---|---|---|
| 1017 | mir | 5972 | 5972 | 0 | 6418 | 6418 | stable → stable |
| 2017 | pri | 6167 | 6157 | −10 | 0 | 0 | exp → exp |
| 3017 | mir | 0 | 0 | 0 | 0 | 0 | exp → exp |
| 4017 | pri | 6398 | 6425 | +27 | 0 | 0 | exp → exp |
| 5017 | mir | 6323 | 6323 | 0 | 6243 | 6243 | exp → exp |
| 6017 | pri | 11957 | **6358** | **−5599** | 11406 | 11409 | stable → stable |
| 7017 | mir | 0 | 0 | 0 | 0 | 0 | exp → exp |
| 8017 | pri | 5994 | 5864 | −130 | 6140 | 6140 | stable → stable |
| 9017 | mir | 0 | 0 | 0 | 6108 | 6034 | stable → stable |
| 10017 | pri | 0 | 0 | 0 | 6178 | 6178 | stable → stable |

**Aggregates (new / ref):** win split **5–5 / 5–5**; Exp capture **6/10 / 6/10**
(same six seeds: 1017/2017/4017/5017/6017/8017); Exp gross median **5918 / 5983**;
Stable gross median **6087 / 6124**.

**Read.** Determinism makes any nonzero Δ an order the executor changed. Six of ten
seeds diverged at the order level (2017, 4017, 6017, 8017, 9017, plus 1017's combat
kills differ while gross is unchanged); 3017/5017/7017/10017 are byte-identical. But
**every aggregate floor number is unchanged** — no winner flipped, no seed changed
capture status. The one large cell is **6017** (Exp gross 11957 → 6358, a halved
capture-income score: `cap 23914 → 12716`), i.e. on that seed the executor's idle
repositioning cost Experimental a second held derrick — but 6017 was *already a
Stable win* in the reference, so it changes no outcome. **Verdict: non-regression
PASS.** No capture-rate or win drop.

**vs PROPOSED S1 bar** (win ≥0.60 AND capture ≥ Stable +2/10): win 0.50 < 0.60 and
capture 6/10 = Stable 6/10 (not +2) → **not met**, identical to the reference. The
non-regression floor (win ≥0.40 + capture parity) **holds**.

---

## CHUNK 2 — S2 force-efficiency (Experimental vs Stable), N=10 (5+5)

`tournament-s2-combat-river-zeta` (+ `-mirror`), `tournament-combat-12min.yaml`,
seeds `1017…10017`. Metric = net combat swing (`kills_cost − deaths_cost`).

| seed | scen | ref Exp sw | new Exp sw | ΔExp sw | ref Ctl sw | new Ctl sw | ref Exp eng | new Exp eng | winner ref→new |
|---|---|---|---|---|---|---|---|---|---|
| 1017 | mir | −2300 | −3950 | −1650 | 1600 | 3750 | 2700 | 5050 | stable → stable |
| 2017 | pri | 1300 | **−5250** | **−6550** | −2300 | −1250 | 3500 | 6250 | exp → exp |
| 3017 | mir | 0 | 0 | 0 | 0 | 0 | 0 | 0 | exp → exp |
| 4017 | pri | 150 | 0 | −150 | −600 | 0 | 1050 | **0** | exp → exp |
| 5017 | mir | −200 | 450 | +650 | −2500 | −450 | 1100 | 450 | exp → exp |
| 6017 | pri | −4500 | −900 | +3600 | 3550 | 450 | 4900 | 900 | stable → stable |
| 7017 | mir | 0 | 0 | 0 | 0 | 0 | 0 | 0 | exp → exp |
| 8017 | pri | −4950 | **−700** | **+4250** | 650 | −1400 | 7050 | 8400 | **stable → exp (FLIP)** |
| 9017 | mir | −2250 | −2750 | −500 | 0 | −750 | 2450 | 2750 | stable → stable |
| 10017 | pri | 0 | 0 | 0 | 0 | 0 | 0 | 0 | stable → stable |

**Aggregates (new / ref):**

| Metric | new (`a88ef596`) | ref (`1eb644de`) | Δ |
|---|---|---|---|
| median Exp swing | **−350** | −100 | **−250** |
| relative edge (vs Stable 0) | **−350** | −100 | −250 |
| sign-delta (Exp > Stable) | **2/10** | 3/10 | −1 |
| both-spawn (primary / mirror) | **1/5 / 1/5** | 2/5 / 1/5 | −1 primary |
| Exp engagement-vol median | **675** | 1775 | **−1100** |
| Stable engagement-vol median | **450** | 3175 | **−2725** |
| engaged count (Exp eng > 0) | **6/10** | 7/10 | −1 (4017 → 0) |
| win split | **Exp 6 / Stable 4** | 5 / 5 | +1 Exp |

**Validity gate:** engaged **6/10** (Exp eng > 0 on 1017/2017/5017/6017/8017/9017)
≥ 6 → **batch VALID, but at the floor.** The executor pushed seed **4017** from a
small live fight (Exp eng 1050) to **zero combat**, costing one engaged seed vs the
reference's 7/10.

**Read — the executor moved S2, and its signature is *less* engagement.** Seven of
ten seeds diverged (only 3017/7017/10017 stay byte-identical zero-combat). The
mechanism is coherent: idle Experimental units relocate to threat-facing cover edges
rather than pressing forward, so **both** sides' engagement volume collapses (Exp
1775 → 675, Stable 3175 → 450 — Stable moves only because the match diverges around
it). On the seeds where a fight still happens the effect is **mixed**: the executor
rescues **8017** hard (−4950 → −700, a loss → win) and improves **6017** (+3600) and
**5017** (+650), but hurts **2017** badly (+1300 → −5250) and **1017** (−1650). The
net is a *slightly worse* median swing (−100 → −350) with a *marginally better* win
split (5–5 → 6–4, entirely the 8017 flip). This is the opposite direction from the
cohesion cap (which *raised* engagement +37%): the cap concentrated assaults, the
executor holds cover.

**vs PROPOSED S2 bar** (median(Exp) ≥ median(Stable) + $1,000 AND sign ≥7/10 AND
both-spawn ≥3/5 each; validity ≥6/10 engaged):
- edge **−350** < +1,000 → **FAIL** (short $1,350)
- sign-delta **2/10** < 7 → **FAIL**
- both-spawn **1/5 + 1/5** < 3/5 each → **FAIL**
- validity gate **6/10** ≥ 6 → **VALID** (at floor)

**Not passing** — identical bar-verdict to the reference (which also failed at edge
−100 / sign 3/10). The executor did **not** manufacture a force edge over a
competent same-faction Stable; if anything it trades slightly worse on the median
while winning one more match by holding position.

---

## Verdict — did the executor move anything?

- **S1:** No, in the way that matters — clean **non-regression** (win/capture floor
  byte-identical), with harmless intra-match order drift on ~6 seeds and one
  outcome-neutral economy dip (6017).
- **S2:** **Yes** — this is where it shows up, exactly as predicted. It changes
  engagement *geometry* on 7/10 seeds, but the aggregate direction is **lower
  engagement volume** and a **slightly worse median swing**, offset by a marginal
  win-split gain (one rescued seed). Net force-efficiency read: **neutral-to-slightly-
  negative**, consistent with the standing "Experimental ≈ Stable" conclusion.
- **Neither bar flips.** No promotion is claimed (bars remain PROPOSED / unratified).

**Instrument watch (flag, not a blocker).** The executor's engagement-suppressing
tendency pulled the S2 batch to the **validity floor (6/10 engaged)**. If a future
executor iteration suppresses contact further, S2 risks going **invalid** (< 6/10) —
this strengthens the parked open question of moving S2 to a forced-contact / `@rush`
variant to keep combat signal robust.

---

## Raw

- `tools/autotest/tournament-results/260722_p1_s1base_phase2/`
- `tools/autotest/tournament-results/260722_p2_s2base_phase2/`
- Reference: `tools/autotest/tournament-results/260721_p2_s1base_cohesioncap/`,
  `…/260721_p5_s2base_cohesioncap/`

(Bulky match JSON is harness-owned / git-ignored; this card is the committed record.)
