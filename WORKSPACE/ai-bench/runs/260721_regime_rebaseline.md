# Regime Re-Baseline — Motorized / same-faction US-US / vs @stable (2026-07-21)

**Build:** `main` @ `60b93501` (regime-change commit), rebuilt clean (0 errors).
**Run mode:** Mode B (minimized + framerate-uncapped, `SpeedMultiplier: 8`), the
new fast harness (`bce9c3e6`). **Realized wall-clock: ~66–71 s/match at S1 (300 s
clock), see per-phase timing.** Zero OpenRA orphans between phases; every phase
exited 0.

> **Why this exists.** The 2026-07-21 regime change (`60b93501`) altered the
> instrument in three ways at once — **Motorized starting units** both sides (was
> `none`), **same faction US-US** (both `america`), **primary opponent `@stable`**
> (was `@normal`). Every prior S1/S2 number is `[pre-regime]` and **not comparable**.
> This document is the first re-baseline on the new regime. All deltas below vs
> `[pre-regime]` numbers are **regime-change deltas (instrument), NOT behavior deltas.**

---

## Compact result table

| Phase | Scenario | N | Verdicts | Wall/match | Headline |
|---|---|---|---|---|---|
| 1 CAL S1 | s1-eco-cal-nn (Stable-v-Stable) | 10 | 10/10 | 66 s | side lean **7–3 Russia-slot(80,35)**; both slots capture |
| 2 BASE S1 | s1-eco-river-zeta (+mirror) | 10 | 10/10 | 71 s | Exp-v-Stable **5–5**; capture **6/10 vs 6/10** |
| 3 FLOOR S1 | s1-eco-floor-vs-normal | 3 | 3/3 | 70 s | Exp-v-Normal **2–1**; Exp capture 1/3, Normal 0/3 |
| 4 CAL S2 | s2-combat-cal-nn (Stable-v-Stable) | 10 | 10/10 | 138 s | side **even 5–5**; **engagement collapsed ~5–6×**, 3/10 zero-combat |
| 5 BASE S2 | s2-combat-river-zeta (+mirror) | 10 | 10/10 | 157 s | Exp-v-Stable **5–5**; swing edge **−350** (no Exp edge) |

All matches ran the full clock (S1 = 7500 t / 300 s; S2 = 18000 t / 720 s),
`time_limit` termination, **0 crashes, 0 no-verdict** across every phase.

---

## Phase 1 — S1 CALIBRATE (Stable-vs-Stable), N=10

Both bots `@stable`, both `america`, Motorized start. Identical bots ⇒ the mirror
is a no-op, so a single N=10 on the primary map is the pure **spawn/side-fairness**
probe. Slots disambiguated by player **name** (`USA-bot` = spawn 14,45 ;
`Russia-bot` = spawn 80,35) — faction no longer labels the slot under same-faction.

- **Side lean: 7–3 toward the Russia slot (80,35).** `{Russia-bot: 7, USA-bot: 3}`.
  Moderate positional bias (identical bots, same faction ⇒ pure spawn). Same
  direction and magnitude as the `[pre-regime]` S2 calibration (7–3 russia) and
  stronger than the `[pre-regime]` S1 calibration (6–4). **Mirror stays mandatory;
  S1/S2 pass bars must hold from both spawns.**
- **Both slots capture under Motorized start.** USA-slot(14,45) gross median
  **6112.5** (captured 6/10); Russia-slot(80,35) gross median **2975.5** (~5/10).
  The USA slot captures *more income*, the Russia slot *wins more* (higher end
  army_value / better combat position) — score medians USA 16376 vs Russia 12101.
- **Regime-change delta:** `[pre-regime]` S1 calibration had Normal capturing
  ~1/20 (control gross ≈ 0). Under Motorized/same-faction/Stable, **the control
  now captures ~6/10** — a fundamentally different S1 (see Phase 2 consequence).

## Phase 2 — S1 BASELINE (Experimental vs Stable), N=10 (5 primary + 5 mirror)

- **Win split: Experimental 5 / Stable 5 — dead even.**
- **Capture parity: Exp 6/10, Stable 6/10.** Exp primary(USA slot) 4/5 (gross
  median 6167); Exp mirror(Russia slot) 2/5 (median 0). Stable gross median 6124.
  The spawn-dependent capture (USA slot reliable, Russia slot not) matches the
  calibration and is cancelled by the mirror.
- **Interpretation — S1 no longer discriminates Exp from Stable.** Stable is the
  frozen post-S1 PROMOTE snapshot: it already carries the TECN-availability floor
  and dispersion doctrine that S1 was built to reward. Experimental = Stable + the
  SR-contestation axis (a *combat/S2* lever, `SrPressureScoreMultiplier: 260`), so
  it has **no economy edge over Stable** — 5–5 is the expected result. **S1's role
  under the new regime is a non-regression floor vs Stable** (Exp must stay ~even),
  not a discriminator. The Exp-over-control *advantage* is now measured by the
  floor scenario (Phase 3, vs Normal) and the discriminating rung is S2.
- **Regime-change delta:** `[pre-regime]` S1 was Exp 8/10–10/10 capture, 8–2/10–0
  vs a non-capturing Normal (control gross ≈ 0, bar degenerate). The new control
  (Stable) captures as well as Exp ⇒ the old "capture reliability vs a zero
  control" framing is retired; S1 is now a paired even-match vs a strong control.

## Phase 3 — S1 FLOOR (Experimental vs Normal), N=3 (sanity)

Single-spawn (no mirror): Exp always on USA slot(14,45), Normal on Russia
slot(80,35) — i.e. Exp on the calibration's *losing* spawn every match.

- **Win split: Experimental 2 / Normal 1.** Exp captured 1/3; Normal 0/3.
- **Floor PASS (weak, noisy).** Exp ≥ Normal (majority + strictly out-captures),
  but far from the `[pre-regime]` 8–2/10–0 dominance. Two regime effects explain
  the compression: (1) **Motorized start** gives Normal a real starting army, so
  early combat is even rather than Exp-favoured; (2) N=3 with **no mirror** puts
  Exp on the disadvantaged USA spawn all three games. As a *sanity floor* (has Exp
  regressed below the frozen control?) it passes; **if it is ever used as a real
  gate it must run with the mirror at higher N.**

---

## Phase 4 — S2 CALIBRATE (Stable-vs-Stable), N=10

Both bots `@stable`/`america`, Motorized start, 720 s combat clock. 138 s/match
wall-clock, 10/10 verdicts, all 18000 t/time_limit, 0 crashes.

- **Side fairness: EVEN.** Win split `{USA-bot: 5, Russia-bot: 5}`; net-swing
  median USA-slot **0**, Russia-slot **−725** (mild). Much fairer than S1's 7–3
  lean — at the 720 s combat clock the spawn is balanced for *wins*. Mirror still
  recommended (the −725 Russia-slot swing lean is small but nonzero).
- **MIN-ENGAGEMENT: PASS but FRAGILE.** Both engagement + deaths medians > 0
  (USA eng 1200 / deaths 725; Russia eng 1925 / deaths 1275), so the floor passes.
  **But the distribution is alarming: 3/10 matches had literally zero combat**
  (seeds 3017, 7017, 10017 — 0/0 both sides), and 2 more (4017, 8017) were trivial
  (≤650). Only 5/10 matches had a real fight.
- **⚠ Regime-change delta — engagement collapsed ~5–6×.** `[pre-regime]` S2
  calibration (Normal-vs-Normal) had engagement-volume medians **7475 / 5950**;
  the new same-faction Stable-vs-Stable regime gives **1200 / 1925**. The
  same-faction, Motorized, frozen-vs-frozen matchup **frequently stalemates into a
  passive economy race** rather than fighting. This is the single biggest risk the
  re-baseline surfaced for S2: with the control that quiet, the net-swing metric
  has little signal *unless* Experimental's SR-contestation axis drives engagement
  up (tested in Phase 5). Score is decided by capture-income economics even in the
  zero-combat matches (score medians USA 37841 / Russia 20967 — dominated by 720 s
  of derrick income, not combat).

## Phase 5 — S2 BASELINE (Experimental vs Stable), N=10 (5 primary + 5 mirror)

157 s/match, 10/10 verdicts, all 18000 t/time_limit, 0 crashes.

- **Win split: Experimental 5 / Stable 5 — dead even** (same as S1).
- **Force-efficiency: Experimental has NO edge over Stable.** Paired-relative:
  median Exp swing **−350**, median Stable swing **0** → **relative edge −350**
  (the pre-regime `+$1,400` bar FAILS by −$1,750). Sign-delta Exp > Stable on only
  **3/10** (bar wants ≥7). Both-spawn primary 2/5, mirror 1/5 (bar wants ≥3/5 each)
  — FAIL. Exp engagement-volume median **1300** vs Stable **2800** (Exp actually
  fights *less* on aggregate, but loses the big fights it does take).
- **Where Exp loses: over-aggression against a competent defender.** The two worst
  cells are primary seeds 6017 (Exp swing −4200, k/d 2/10) and 8017 (−4950, 4/10)
  — Exp pushed in and got out-traded badly while Stable defended (Stable +1800 /
  +650). Exp's *only* behavioral delta from Stable is the SR-contestation axis
  (`SrPressureScoreMultiplier: 260`); that axis, which beat **Normal** by +$6,300
  `[pre-regime]`, is **neutral-to-negative vs Stable** — aggression that punished a
  passive control gets punished by a competent one.
- **Same passive-stalemate seeds recur:** 3017 / 7017 / 10017 were 0-combat here
  too (identical to the calibration), confirming those battlefields simply don't
  force contact under this regime — a seed-level, not bot-level, effect.

### The core re-baseline finding

Under the new regime **Experimental ≈ Stable on both rungs** — S1 5–5 with capture
parity (6/10 vs 6/10), S2 5–5 with a −350 swing edge. This is *exactly the signal
the regime change was designed to produce*: Stable is the frozen snapshot of the
last validated Experimental config, so measuring Exp against it asks "has anything
improved **since** the last promotion?" — and the honest answer today is **no**.
The current Exp/Stable delta (the SR axis) helped vs Normal but does not constitute
an improvement over Stable on either facet. **The benchmark has caught up to the
bot; the next behavior cycle needs a genuinely new lever to move these bars.**

---

## Realized wall-clock (the new fast harness)

| Rung | Game clock | Sim @ 8× | Observed wall/match | Effective speedup |
|---|---|---|---|---|
| S1 | 300 s | ~37.5 s | **66–71 s** | ~4.3–4.5× realtime (init-dominated) |
| S2 | 720 s | ~90 s | **138–157 s** | ~4.6–5.2× realtime |

The ~30 s fixed init (engine boot + map load) dominates the short S1 matches, so
the *effective* speedup is below the raw 8× sim multiplier — the shorter the match,
the more init overhead dilutes it. Full ladder re-baseline (43 matches across 5
phases) ran in **~78 min of batch wall-clock**. `[pre-regime]` runs at 6× windowed
were reported ~2 wall-min for a 12-min match (~6×); the new minimized-uncapped
profile lands ~2.3–2.6 wall-min for the 720 s S2 match — comparable, with the win
being *no window / no focus theft*, not a large raw-speed jump on these clocks.

---

## Proposed bars (awaiting ratification — do NOT silently adopt)

All `[pre-regime]` bars are void (measured vs a non-capturing Normal / mixed
factions). Proposed replacements, derived from the data above:

### S1 (economy) — Exp vs Stable
- **Pass:** paired **win-rate ≥ 0.60** over N=10 (mirror, both-spawn ≥3/5 each)
  **AND** Exp in-window capture-rate ≥ Stable capture-rate **+ 2/10**.
  *Rationale:* vs a control that now captures 6/10, mere reliability is table-stakes
  — Exp must *out-win and out-capture* Stable. Current: 0.50 win, 6/10 vs 6/10 →
  **does not pass** (correct; no edge exists yet).
- **Non-regression floor:** Exp win-rate **≥ 0.40** and capture parity (±2/10). If
  Exp drops below this vs Stable, flag a regression. Current: PASS (floor holds).

### S2 (force efficiency) — Exp vs Stable
- **Pass:** paired-relative **median(Exp swing) ≥ median(Stable swing) + $1,000**
  **AND** sign-delta ≥ 7/10 **AND** both-spawn ≥ 3/5 each. Margin lowered from the
  `[pre-regime]` $1,400 because the structural attrition offset that inflated it
  (control swing median −5050) is gone — the Stable control now sits at ~0. Current:
  edge −350, sign 3/10 → **does not pass** (correct).
- **⚠ Batch-validity gate (NEW, blocking):** a valid S2 batch needs **≥ 6/10
  matches with real engagement** (Exp eng > 0). Current batch had **7/10** engaged
  (3 zero-combat seeds), median eng only 1300 — *barely* valid and low-signal.
  **If this drops below 6/10, the batch is a passive stalemate and S2 must be
  re-scoped** (forced-contact map / shorter clock / a `@rush` opponent) before any
  swing number is trusted. This is the biggest instrument risk the re-baseline found.

### Open decision for the user (flagged in REVIEW + dashboard)
The same-faction Stable-vs-Stable S2 calibration fights only half the time
(engagement −5–6× vs pre-regime). **Does S2 stay Exp-vs-Stable on this quiet
regime, or move to a forced-contact variant / `@rush` control** to restore combat
signal? The loop will otherwise proceed on the batch-validity gate above by default.

## Harness note (parse tooling)

The three parse scripts were updated for the new regime (harness change, SPEC
§4.1 — no bar/stat/control touched): the control is auto-detected as "the other
bot" (`stable` for primary/mirror, `normal` for the floor) instead of hardcoded
`normal`, and the calibration slot disambiguation now keys on player **name**
(`USA-bot`/`Russia-bot`) instead of faction, since both sides are `america`.
`python -m py_compile` clean; validated against all live result dirs.
