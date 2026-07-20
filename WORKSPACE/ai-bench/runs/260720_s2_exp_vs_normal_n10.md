# S2 MEASUREMENT — Experimental-vs-Normal (N=10) + dispersion ON/OFF causal A/B

**Date:** 2026-07-20
**Scenario:** `tournament-s2-combat-river-zeta` (primary) + `-mirror` (bot-swap twin), River Zeta 98×82, 12 OILB derricks
**Config:** `tournament-combat-12min.yaml` — `TimeLimitSeconds: 720`, `SpeedMultiplier: 8`, hidden Mode-B (`OPENRA_WINDOW_HIDDEN=1`)
**Seeds:** 1017…10017 (i·1000+17), 5 primary (even) + 5 mirror (odd), verdict_version 5 (seeded/deterministic)
**Builds:** Batch A `main @ 1594ffa1` (`git_dirty: false`); Batch B `main @ 777c51e8` (`git_dirty: true` — the temporary `@experimental` dispersion toggle; **reverted immediately after, working tree clean**)
**Raw:** `tools/autotest/tournament-results/260720_s2_expA_disp_on/` (ON) · `…/260720_s2_expB_disp_off/` (OFF)
**Aggregators:** `parse-s2-batch.py` (per-bot swing/engagement/win) + `parse-s2-bar.py` (the paired-relative bar + A/B, added this cycle)
**Action type:** MEASUREMENT — first S2 Experimental-vs-Normal batch + the dispersion causal A/B the dispersion VERIFY deferred (`runs/260720_dispersion_verify_n10.md:118-121`). Bar per the CALIBRATE (`runs/260720_s2_calibrate_nn.md`).

---

## Validity

- **20/20 verdicts written, all `time_limit` @ 18000 ticks** (full natural end, no watchdog kill), **0 crashes** across both batches. Batch valid (100% ≥ 80% floor, SPEC §9.1).
- **A/B pairing is clean.** Batch A ran at `1594ffa1`, Batch B at `777c51e8`; the SHA drift is **docs-only** — the four intervening commits (from concurrent managers) touched only `WORKSPACE/DISCOVERIES.md`, `WORKSPACE/ai-bench/REVIEW.md`, and two `WORKSPACE/plans/*.md`. No engine `.cs`, no `ai.yaml` (bar the toggle), no scenario/config changed (`git diff --name-only 1594ffa1..777c51e8` verified). Binary + AI config were byte-identical, matches are deterministic → the ON/OFF deltas are pure cohesion effect.
- **The only ai.yaml delta between A and B** was `CohesionSwitchEnabled: true → false` on `PoiOffensiveBotModule@experimental` **only** (line 192); `@stable` (line 675), the shared `@poi` singletons, and Normal/Rush/Turtle were untouched. Reverted post-batch (`git checkout -- ai.yaml`, diff clean).
- **S1-context sanity (scorer untouched):** `capture_income_gross` present + populating in all 20 verdicts (nonzero on 8/20 player-slots each batch; max $32,538 ON / $46,202 OFF — higher than S1's ~$6–11k because the 720s clock gives longer derrick-hold time). No anomaly.

---

## Batch A — dispersion ON (current `main`) — the S2 advancement bar

Per-seed (A = Experimental, B = Normal; swing = `kills_cost − deaths_cost`):

| seed | spawn | Exp faction | Exp swing | Normal swing | delta (Exp−Nrm) | Exp eng | winner |
|---|---|---|---|---|---|---|---|
| 1017 | mirror | russia | -3650 | -3150 | **-500** | 5950 | experimental |
| 2017 | primary | america | 5350 | -6400 | +11750 | 8750 | experimental |
| 3017 | mirror | russia | -5800 | -950 | **-4850** | 6000 | experimental |
| 4017 | primary | america | -2400 | -5300 | +2900 | 5500 | experimental |
| 5017 | mirror | russia | -1300 | -4300 | +3000 | 5000 | experimental |
| 6017 | primary | america | 750 | -5000 | +5750 | 1650 | experimental |
| 7017 | mirror | russia | 3900 | -5400 | +9300 | 4900 | experimental |
| 8017 | primary | america | 1450 | -5050 | +6500 | 5950 | experimental |
| 9017 | mirror | russia | 3850 | -5050 | +8900 | 4950 | experimental |
| 10017 | primary | america | -1150 | -6500 | +5350 | 8350 | experimental |

**Paired-relative bar (ratified form, `runs/260720_s2_calibrate_nn.md` §3):**

1. **Relative edge:** median Exp swing **-200** ≥ median Normal swing **-5050** + $1,400 → **-200 ≥ -3650 → PASS** (relative edge **+4,850**, margin **+3,450** over the bar).
2. **Sign-delta (Exp swing > Normal swing):** **8/10 ≥ 7/10 → PASS**. (Note: `parse-s2-batch.py`'s "5/10 sign robustness" counts Exp swing **> 0**, which is the *absolute* form the CALIBRATE retired — the ratified paired delta is 8/10.)
3. **Both-spawn symmetry:** primary **5/5**, mirror **3/5** → both ≥ 3/5 → **PASS** (not carried by one spawn; no asterisk).
4. **Min-engagement floor:** Exp eng median **5725**, Normal **6450**, both > 0, units dying both sides → **PASS**. Exp 5725 sits inside the NN calibration band (america 7475 / russia 5950) → Experimental is **not** winning by refusing to fight.

**Win split: Experimental 10 – Normal 0.**

> **S2 ADVANCEMENT BAR: PASS on current `main` (1594ffa1)** — all four gates, both spawns.

**Watch cells:** the only two negative deltas are mirror seeds **1017 (-500)** and **3017 (-4850)**, both **exp=russia**. Consistent with the CALIBRATE's russia-spawn lean showing up as the harder side for Experimental *relative* to a russia-spawn Normal; the mirror still clears 3/5 so the pass holds.

---

## Dispersion ON/OFF causal A/B (diagnostic — NOT the advancement bar)

Same 10 seeds, Batch A (`CohesionSwitchEnabled: true`) vs Batch B (`false`), Experimental net swing, delta = ON − OFF:

| seed | spawn | Exp swing ON | Exp swing OFF | delta (ON−OFF) | winner ON | winner OFF |
|---|---|---|---|---|---|---|
| 1017 | mirror | -3650 | 400 | **-4050** | exp | exp |
| 2017 | primary | 5350 | 2700 | +2650 | exp | exp |
| 3017 | mirror | -5800 | 1550 | **-7350** | exp | exp |
| 4017 | primary | -2400 | 2650 | **-5050** | exp | exp |
| 5017 | mirror | -1300 | 2500 | **-3800** | exp | exp |
| 6017 | primary | 750 | 3850 | **-3100** | exp | exp |
| 7017 | mirror | 3900 | 3800 | +100 | exp | exp |
| 8017 | primary | 1450 | -5850 | **+7300** | exp | **normal** |
| 9017 | mirror | 3850 | 2150 | +1700 | exp | exp |
| 10017 | primary | -1150 | -2050 | +900 | exp | exp |

- **median Exp swing: ON -200 vs OFF +2325.**
- **median paired delta (ON−OFF) = -1500** (positive would mean cohesion improves combat economy).
- **delta positive on only 5/10 seeds.**
- **Exp win split: ON 10/10 vs OFF 9/10.**
- **Engagement volume: ON 5725 vs OFF 7125** — dispersion ON fights ~20% less by value.
- Batch B (OFF) also clears the S2 bar, *more strongly*: relative edge **+7,525**, sign-delta **9/10**, both-spawn 4/5 primary + 5/5 mirror.

### Verdict: the cohesion doctrine does **NOT** improve combat economy on identical worlds — it slightly **degrades** it.

Turning dispersion ON **reduces** Experimental's net swing (median −$1,500, positive on only 5/10 seeds) and **lowers engagement volume** (~20%). Its *only* measured benefit is a single flipped win — seed 8017 (primary/america), where cohesion turns a $5,850 net loss into a $1,450 net win (+$7,300 delta, normal-win → exp-win). So dispersion is **high-variance**: it rescues one bad world but bleeds efficiency on six others.

This is precisely the causal credit the dispersion VERIFY deferred, and on the **force-efficiency axis it comes back negative.** Dispersion's value, if it has one, is **decisiveness** (the +1 win), not exchange efficiency — which is an **S3 (win-rate)** question, not S2. The advancement decision is unaffected either way: **S2 passes on current `main` with dispersion ON**, and would pass even more strongly with it off.

**Caveat / honesty:** N=10 with a single high-leverage seed (8017) driving the entire win-count gain; the −$1,500 median delta is real and consistent (6/10 seeds negative) but not a landslide. This grades the doctrine *as currently tuned* (`AssaultRadiusCells: 15`, `ApproachCohesion: Spread`) — not the concept in principle.

---

## Outputs / decisions

- **S2 advancement bar: PASS** on `main @ 1594ffa1` (Experimental 10-0, relative edge +$4,850, sign-delta 8/10, both-spawn 5/5 + 3/5, min-engagement met). First S2 rung result recorded.
- **Dispersion causal credit: NEGATIVE on combat efficiency** (median ON−OFF delta −$1,500; engagement −20%; +1 win only). The behavior does not earn its keep on the S2 metric; its payoff is decisiveness/variance, an S3 concern. **This is a concrete, actionable signal on a shipped+promoted behavior.**
- **Recommended next routing:** a **combat-focused dispersion re-tune/re-evaluation cycle** is the highest-signal next step — S2 was built to grade this exact behavior and just returned a causal *negative* on efficiency, giving a specific score to move (re-tune `AssaultRadiusCells` / approach formation, or reframe dispersion as an S3-decisiveness lever). Secondary option: proceed to the parked **SR-contestation** cycle (`plans/260720_sr_contestation_cycle1.md`, implement-ready) for ladder breadth. The numbers point at dispersion, so a dispersion re-tune is the recommended lead.
- **S3 watch (carried from CALIBRATE):** the combat side-lean means S3 win-rate must lean on the mandatory mirror or a larger-N NN win-rate calibration before a win-rate is trusted.
