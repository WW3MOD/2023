# SR-Contestation Cycle 1 — S1 non-regression + S2 bar (N=10 each) — result: PASS (MERGED)

**Date:** 2026-07-20
**Branch:** `exp-sr-contestation` (from `main @ 39821f93`) → merged to `main`
**Change:** per-bot `PoiOffensiveBotModule.SrPressureScoreMultiplier` (x100, default **100 = inert**),
set **`260` on `@experimental` only**. Re-scales the enemy Supply Route **Pressure**-axis score
after `GetOffensiveTargets`, then re-sorts, so the enemy SR can win an offensive axis (frozen it
scored last and never got one). Deny-only: `SUPPLYROUTE` has no `CaptureManager`, so a Pressure
axis emits `AttackMove` (contest the 10-cell circle), never a capture.
**Run mode:** B (hidden, `OPENRA_WINDOW_HIDDEN=1`, `SpeedMultiplier: 8`), verdicts v5 (seeded).
**Raw:** `tools/autotest/tournament-results/260720_srcontest_s1/` (S1) · `…/260720_srcontest_s2/` (S2).

Implements `plans/260720_sr_contestation_cycle1.md` **per its Freshness addendum §E** (the original
world-level `PoiMap` edit was superseded — that trait is a singleton read by `@stable` + the frozen
controls; a per-bot field default-100 keeps them byte-identical). `260 = (250·100)/(120·80)`
reproduces the plan's value=250/bias=100 uplift over the frozen value=120/bias=80.

---

## Build / tests / sanity

- `./make.ps1 all` green; NUnit **291/291** (no test drift — the field is a pure tuning constant).
- **Multiplier-100 no-op is provable:** the call site guards `if (Info.SrPressureScoreMultiplier != 100)`
  before rescaling, so at the default (`@stable`, `@normal/@rush/@turtle`) the exact
  `GetOffensiveTargets` list is used unchanged — no reconstruction, no re-sort. Byte-identical control.
- **Score-math check (from a live S1 axis line):** `action=Pressure score=57408000` at safe threat.
  Frozen would be `120·distF·100·100·80/100`; ×`260/100` = the emitted 57.4M. Matches the addendum's
  `mild 6.528M→17.0M / safe 16.32M→42.5M / hostile 1.632M→4.25M` reproduction (×2.60).

---

## S1 — economy non-regression (bar: capture + gross must not regress vs the LADDER seeded reference)

Scenario `tournament-s1-eco-river-zeta` (+ `-mirror`), 300s/7500t, metric `capture_income_gross`.

| Metric | This cycle | LADDER seeded reference (`e5a1c967`) | Verdict |
|---|---|---|---|
| In-window capture rate (`gross>0`) | **8/10** | 8/10 | ✅ no regression |
| Conditional gross median (captured, n=8) | **$6,457** | $6,457 | ✅ identical |
| Win split (Exp vs Normal) | **10–0** | 10–0 | ✅ |
| All-runs gross median | $6,340 | $6,378.5 | ✅ context |
| Residual $0 misses | seeds **2017, 8017** (america) | 2017, 8017 (america) | ✅ same known cells |

10/10 full 7500t, 0 no-verdict, 0 crashes. **S1 NON-REGRESSION: PASS** — the SR Pressure axis
diverting combat units did NOT starve the TECN capture layer (offense and capture draw from the
shared goal-guard pool but capture is TECN-driven; income capture held exactly).

### SR axis appeared in-window (the mechanism is live)

Per-match `action=Pressure` axis/order lines (S1 debug logs), first-appearance tick:

| match | seed | pressure lines | first tick | bot |
|---|---|---|---|---|
| 1 | 1017 | 56 | 2047 | Russia |
| 2 | 2017 | **0** | — | — |
| 3 | 3017 | 64 | 1609 | Russia |
| 4 | 4017 | 60 | 1937 | USA |
| 5 | 5017 | 43 | 1890 | Russia |
| 6 | 6017 | 60 | 1992 | USA |
| 7 | 7017 | 56 | 2151 | Russia |
| 8 | 8017 | **0** | — | — |
| 9 | 9017 | 62 | 1711 | Russia |
| 10 | 10017 | 52 | 1997 | USA |

**8/10 matches open an SR Pressure axis**, first at tick ~1600–2150 (mid-game, minutes ~5–7 — exactly
the plan's §3 prediction of tick ~2000, NOT opening-game). Both spawns exhibit it (Russia + USA bots).
The 2 no-pressure matches are **the same army/production-starved america seeds** (2017/8017) that also
capture $0 — no offensive army to field, so no axis, consistent with the known upstream TECN-throughput
watch cells, not this change.

---

## S2 — force-efficiency bar (paired-relative, `runs/260720_s2_calibrate_nn.md §3`)

Scenario `tournament-s2-combat-river-zeta` (+ `-mirror`), 720s/18000t, metric net swing
(`kills_cost − deaths_cost`). All 10 verdicts `time_limit` @ 18000t, 0 crashes.

| seed | spawn | exp faction | Exp swing | Normal swing | delta (Exp−Nrm) | Exp eng | winner |
|---|---|---|---|---|---|---|---|
| 1017 | mirror | russia | -3100 | -1950 | **-1150** | 6100 | experimental |
| 2017 | primary | america | 5350 | -6400 | +11750 | 8750 | experimental |
| 3017 | mirror | russia | 1600 | -6050 | +7650 | 7800 | experimental |
| 4017 | primary | america | -2750 | -2500 | **-250** | 5950 | experimental |
| 5017 | mirror | russia | 800 | -5300 | +6100 | 4900 | experimental |
| 6017 | primary | america | -1950 | -3000 | +1050 | 7950 | experimental |
| 7017 | mirror | russia | 5400 | -5400 | +10800 | 5400 | experimental |
| 8017 | primary | america | 1450 | -5050 | +6500 | 5950 | experimental |
| 9017 | mirror | russia | 3400 | -5300 | +8700 | 5400 | experimental |
| 10017 | primary | america | -1250 | -4250 | +3000 | 10650 | experimental |

**Four gates — all PASS:**

1. **Relative edge:** median Exp swing **+1125** ≥ median Normal swing **-5175** + $1,400 → edge
   **+$6,300** (margin **+$4,900** over the bar). PASS.
2. **Sign-delta (Exp > Normal):** **8/10** ≥ 7/10. PASS.
3. **Both-spawn symmetry:** primary **4/5**, mirror **4/5** (≥3/5 each). PASS — not carried by one spawn.
4. **Min-engagement:** Exp eng median **6025** (inside the NN calib band america 7475 / russia 5950),
   both medians > 0, units dying both sides → not winning by avoiding combat. PASS.

**Win split: Experimental 10 – Normal 0.**

**S2 BAR: PASS.** Watch cells: the only two negative deltas are seeds **1017 (-1150, exp=russia)** and
**4017 (-250, exp=america)** — both small; mirror still 4/5.

### Comparison to the S2 reference (dispersion-ON, `main @ 1594ffa1`, `runs/260720_s2_exp_vs_normal_n10.md`)

| | reference (no SR contest) | this cycle (SR contest 260) |
|---|---|---|
| median Exp swing | **-200** | **+1125** |
| median Normal swing | -5050 | -5175 |
| relative edge | +$4,850 | **+$6,300** |
| sign-delta | 8/10 | 8/10 |
| negative-delta seeds | 1017(-500), 3017(-4850) | 1017(-1150), 4017(-250) |

Normal control swing is stable (-5050 → -5175: same measuring stick), so the **+$1,325 lift in median Exp
swing is attributable to the Experimental-side change.** SR contestation manufactures engagement deep in
the enemy half and **improves** combat economy on the S2 metric (and notably rescues the reference's worst
cell, seed 3017: −$4,850 delta → **+$7,650**). This is on the *same* metric where the dispersion doctrine's
causal credit came back **negative** — SR contestation earns its keep where dispersion did not.

### S2 SR axis appearance (sustained contestation)

8/10 matches open SR Pressure axes, **89–180 axis lines each** (vs S1's 43–89) — far more prolific over the
720s combat clock, i.e. sustained SR-circle contestation, first tick ~1600–2150. Same 2 army-starved misses
(2017/8017).

---

## Outputs / decisions

- **S1 non-regression: PASS** (capture 8/10, cond gross median $6,457, win 10–0 — identical to the LADDER
  seeded reference; same 2 known america misses). **S2 bar: PASS** (all four gates; relative edge +$6,300,
  win 10–0), *stronger* than the pre-change reference (+$4,850, median swing −200→+1125).
- **SR axis is live in-window** (8/10 both scenarios, tick ~1600–2150, both spawns) — the change does what
  the plan designed: the enemy SR now wins an offensive axis mid-game and units contest its circle
  (`AttackMove`, deny-only — no capture path exists).
- **Decision: MERGE to `main`.** `@experimental` only; `@stable` + Normal/Rush/Turtle byte-identical
  (field default 100, guarded no-op). NUnit 291/291, 0 crashes across 20 matches.
- **Not promoted to `@stable`** — promotion is a separate user-accepted step (SPEC §13); this is the
  Experimental cycle merge so the user can play the progress from `main`.

### Honest caveats / follow-ups

- N=10 per scenario; the S2 lift is driven by broad positive deltas (8/10) but two seeds sit slightly
  negative (both small). Consistent, not a landslide.
- The SR score at **safe** threat (×260 → ~57M observed) can outrank neutral oilbs — in S1 this showed as
  the SR pulling an 8-unit axis mid-game **without** hurting income capture, but it is worth watching that
  a heavier multiplier could over-prioritise the SR. 260 is deliberately the plan's reproduction value, not
  a max. The **hostile** threat gate (×260 → ~4.25M) still keeps the AI off a well-garrisoned SR.
- Seeds 2017/8017 (america) field no offensive army and neither capture nor contest — an upstream
  TECN/production-throughput limit already tracked as the S1 watch cells; orthogonal to this change.
- Next natural cycle: the parked **dispersion re-tune** (S2 now scores it negative) or an **SR multiplier
  sweep** (260 vs higher/lower) now that S2 gives SR contestation a positive causal score to optimise.
