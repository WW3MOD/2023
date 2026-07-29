# RECON — @experimental ladder-deficit attribution (260729)

Read against `main` @ **6bc11710**. Read-only mining of the item-25 re-baseline
artifacts (60 matches, docs commit `5dc14934`; result card
`WORKSPACE/ai-bench/runs/260728_rebaseline_result.md`; standing doc
`WORKSPACE/ai-bench/LADDER.md`). No gameplay runs, builds, or edits were performed.

Register: impersonal ("the agent", "the user"). Every claim is tagged **MEASURED**
(read directly off a surviving artifact) or **INFERRED** (a reading the artifacts
suggest but do not prove).

---

## 0. Headline

**MEASURED — the deficit is a capture-contest deficit, not (primarily) a combat or
forest deficit.** Every match is decided at the time limit by a weighted score that is
dominated by `capture_income`; on the two *contested* Supply-Route/tecn seeds the
`@stable` bot extracts **~1.9×** the `@experimental` bot's capture income, while a
Stable-vs-Stable control on the *same seeds* splits that income **~symmetrically
(0.98–1.02×)**. The combat-trade shortfall the result card led with is real but
**~10–20× smaller** than the capture swings and is close to a wash on the no-capture
seeds. The item-26 forest hypothesis is **untestable** with surviving artifacts (zero
terrain/cover logging).

This reframes — it does not contradict — the card: the card measured on a combat-only
"net swing" metric (≈ −$1,425) and correctly reported capture *binary parity* (4/10 vs
4/10). The binary count masked a **capture-magnitude** asymmetry that the score_total
data exposes as the dominant determinant of the win split.

---

## 1. Data inventory — what survived vs what was clobbered

**MEASURED — surviving, primary (per-match, raw):**
`tools/autotest/tournament-results/rebase_{cal,armA,armB}_{s1,s2}/` — all six rungs
present, git-ignored, **not** clobbered. Each rung holds, for all 10 matches:

| File | Content |
|---|---|
| `match_N.json` | Verdict v5: seed, `duration_ticks`, `winner_name`, `win_reason`, and per-player `score_total` / `score_components{army_value,capture_income,kills_value}` / `stats{units_killed,units_dead,buildings_*,kills_cost,deaths_cost,army_value,assets_value,order_count,experience,resources_earned,capture_income_gross}`. `bot_type` field labels each player `stable`/`experimental` (clean in arm rungs; both `stable` in cal). |
| `match_N_debug.log` | 130–460 KB AI debug per match. **Experimental-side markers only** (`[exp-capture] [exp-offense] [exp-transport] [exp-garrison] [exp-poi] [exp-terr] [exp-ambush] [exp-layered-defence]`). Lines name **both** players, but the emitting trait is the experimental one. |
| `match_N.log`, `match_N.watcher.log`, `batch.meta.json` | Engine stdout, watchdog trace, and per-rung meta (scenario, config, stamped SHA, `git_dirty`, seeds, wall cap). |

**MEASURED — surviving, distilled:** the result card + runplan
(`WORKSPACE/ai-bench/runs/260728_rebaseline_{result,runplan}.md`) and `LADDER.md` /
`REVIEW.md`.

**MEASURED — clobbered / absent:**
- `~/.ww3mod-tests/screenshots/` holds only individual test-case + demo runs
  (`test-case01-forest-ambush`, `demo-territory-overlay`, …); **no ladder-match
  screenshots or per-match verdict dirs** were archived there.
- No `result.json` / verdict JSON for the ladder under `WORKSPACE/ai-bench/runs/`
  (those `*.verdict.json` are from the July-19/20 tournaments, a different instrument).
- **No per-unit-class loss breakdown anywhere** — `stats` carries aggregate counts
  (`units_killed`, `units_dead`) and aggregate costs (`kills_cost`, `deaths_cost`)
  only. Loss-by-class/by-cost-tier is **not recoverable**.
- **No terrain/cover/forest tagging** in any artifact (see §4).

Because the raw per-match JSON survived, attribution below is done on primary data, not
on the card's summary.

---

## 2. Where the deficit accrues

### 2.1 Every match is a score-at-time-limit result
**MEASURED.** All 40 arm matches (and cal) end `win_reason=time_limit`
(`duration_ticks` = 7500 for S1 eco, 18000 for S2 combat — constant within rung). No
eliminations; combat never ends a match. Outcome = weighted score at the buzzer, and
per `match_N.log` the scorer is `weighted_components`, winrule `score_or_sr_capture`.

### 2.2 Score is dominated by capture income
**MEASURED.** On capturing S2 seeds a single held income asset accrues **$33k–$64k**
of `capture_income`; combat `kills_cost`/`deaths_cost` are hundreds to low-thousands.
Example (armA_s2 seed 6017): stable `score_total` 70 664 = army 4 700 + **capture
64 814** + kills 1 150; exp 39 000 = army 4 950 + **capture 33 700** + kills 350.
Capture is ~90 % of the score on these seeds.

### 2.3 The seeds partition deterministically (same across arms)
**MEASURED.** The 10 seeds fall into four regimes, identical in armA and armB (same map,
mirrored spawns):

| Regime | Seeds | Outcome |
|---|---|---|
| No-capture (pure combat) | 1017, 3017, 5017, 10017 | small swings, ~wash (2 E / 2 S) |
| Exp-solo-capture | 2017, 4017 | Exp blowout win (+$33k–35k S2) |
| Stable-solo-capture | 7017, 9017 | Stable blowout win (−$33k–35k S2) |
| **Contested (both capture)** | **6017, 8017** | **Stable wins: ~1.9× Exp income** |

The win split (armA_s2 4–6, armA_s1 3–7, armB_s2 3–7, armB_s1 ~5–5) is **entirely** the
capture seeds. Exp banks its 2 solo-capture seeds; Stable banks all 4 of its (2 solo +
**both** contested). The net deficit is the **2 contested seeds breaking for Stable**,
plus a slight combat lean.

### 2.4 Contested-capture magnitude: treatment vs control
**MEASURED — this is the load-bearing number.**

| Seed | Rung | Exp capture_income | Stable capture_income | Stable ÷ Exp |
|---|---|---|---|---|
| 6017 | armA_s2 | 33 700 | 64 814 | **1.92×** |
| 8017 | armA_s2 | 33 504 | 63 722 | **1.90×** |
| 6017 | armA_s1 | 12 700 | 22 814 | **1.80×** |
| 8017 | armA_s1 | 12 504 | 21 722 | **1.74×** |

Control — **Stable-vs-Stable** cal on the *same* seeds (`rebase_cal_s2`):

| Seed | USA-bot (stable) | Russia-bot (stable) | ratio |
|---|---|---|---|
| 6017 | 64 768 | 64 842 | **1.00×** |
| 1017 | 33 738 | 32 902 | 1.03× |
| 5017 | 33 486 | 33 654 | 0.99× |
| 8017 | 32 878 | 33 280 | 0.99× |
| 10017 | 33 962 | 33 318 | 1.02× |

Stable-vs-Stable splits contested captures ~evenly; Exp-vs-Stable loses them ~1.9×. The
map does not favour one slot on these seeds — so the ~1.9× gap is a **treatment effect
(Exp under-performs at contested capture)**, not spawn bias.

### 2.5 Variance is blowout-driven, not uniform bleed
**MEASURED.** Per-match `score_total` swing (exp − stable), armA_s2:
`[-1550, +35354, +2000, +33300, +250, -31664, -34710, -32868, -33626, -2650]`.
Four capture-loss seeds at **−$31k…−$35k**, two capture-win seeds at **+$33k…+$35k**,
four combat seeds at **±$2k**. Median −$2 100, mean −$6 616 — the mean is a blowout
artifact. The deficit lives in a handful of capture seeds, not a steady per-match drip.

### 2.6 Combat trade is a real but secondary bleed
**MEASURED.** Aggregating the combat axis (`kills_cost − deaths_cost`, exp minus
stable) reconciles to the card's headline in **sign and rough magnitude**, not exactly:

| Rung | agent's combat-net-swing (median) | card's "net swing" |
|---|---|---|
| armA_s1 | −1 175 | −1 425 |
| armA_s2 | −1 300 | −1 425 |
| armB_s1 | −350 | −875 |
| armB_s2 | −1 625 | −1 825 |

(The ~$150–500 gap is a **definitional** difference — the card's harness "net" likely
folds in a capture or paired-seed term; flagged, not resolved.) Exp `deaths_cost`
medians run **2–3×** stable's (armA_s2: 2 150 vs 725), and Exp K/D on losing seeds is
**0.0–0.5**. But on the four no-capture seeds the combat result is ~even
(3017 +2000 E, 5017 +250 E, 1017 −1550 S, 10017 −2650 S). Combat is a slight,
noise-scale negative — **~10–20× smaller than the capture swings that decide matches.**

---

## 3. The item-26 (forest cover-damage) hypothesis

**MEASURED — untestable with surviving artifacts.** The working hypothesis (aggressive
Exp trades poorly in forest after item-26's `DensityModifiesDamage` change) cannot be
evaluated:
- AI debug logs contain **0** occurrences of `forest`, `density`, or `shadow`, and no
  cover-state tokens (`cover` appears 3× in unrelated context).
- `match_N.json` tags no engagement with terrain; there is no per-death record at all.
- The blowout losses are **capture** losses (§2.4–2.5), whose `score_total` swings are
  dominated by `capture_income`, not combat — so even the largest losses are not
  attributable to forest fighting.

There is **no evidence for or against** an item-26 forest effect in these artifacts.
Anything asserting a forest link would be unfounded.

**Logging a future diagnostic run would need (do NOT run — user-gated):**
1. **Two-sided capture instrumentation** — commit tick, capturer count, and target
   tecn/SR id for *both* bots (today only the experimental trait logs), so the
   capture-race timing in §5 becomes measurable rather than one-sided.
2. **Capture-income timeseries** — income-per-tick per player, to separate "captured
   later" from "held the asset a shorter time" (both produce a lower total; only a
   timeseries distinguishes them).
3. **Terrain-tagged death record** — per unit death: `(tick, cell, terrain_type,
   in_cover_flag, forest_density_modifier_applied, killer_weapon)` — the minimum to
   test the item-26 forest hypothesis at all.
4. **Per-class loss ledger** — cost and count of losses by unit class, absent from
   today's aggregate `stats`.

---

## 4. Firing / behaviour cross-check
**MEASURED.** `[exp-capture]` markers confirm the capture manager runs and tracks both
`player=Russia-bot` (exp) and `player=USA-bot` (stable). `[exp-terr]` repoint fires but
is behaviourally quiet (card §2: `boosted=0 damped=0`), consistent with the strategic
layer **not** tilting toward the contested capture. Ambush markers present in arm A,
absent in all 20 arm-B matches (clean A/B), per card §5.

---

## 5. Capture-timing reading (INFERRED — one-sided instrumentation)
On contested seed 6017 (armA_s2), `[exp-capture]` commitment markers show `USA-bot`
(stable) reaching `committed=2` by tick ~1047 while `Russia-bot` (exp) reaches
`committed=1` by tick ~1146 — i.e. **Stable commits more capturers, earlier.** This is
**INFERRED**: the markers are emitted by the experimental trait, so the stable-side
counts are not independently trustworthy, and a single seed is shown. It is consistent
with the measured 1.9× income gap (Stable holds the asset longer and/or captures
earlier) but does not prove the mechanism. Resolving it needs logging item 1–2 in §3.

---

## 6. Candidate "new levers" — options menu for the user

All five are **user-call; none is pre-approved or implemented.** Ordered by strength of
the supporting evidence.

1. **Contested-capture prioritisation (strategic layer).** Raise Exp's commit
   weight / capturer count toward a contested tecn/SR when the control/danger field is
   neutral. *Mechanism:* close the measured 1.9× contested-capture gap that decides the
   ladder. *Cost:* low–med (tune `[exp-capture]` commitment thresholds / poi weighting,
   no engine change likely). *Evidence:* **strongest** — targets the measured deficit
   directly; the Stable-vs-Stable control proves parity on these seeds is achievable.

2. **Capture-race speed (transport / escort).** Deliver capturers to contested tecns
   earlier (staging, escort). *Mechanism:* arrive first → capture earlier → hold longer.
   *Cost:* med. *Evidence:* **INFERRED** timing gap in §5 only — weaker, one-sided.

3. **Combat-trade discipline (stop feeding).** Cut Exp `deaths_cost` on no-capture
   seeds (disengage bad trades, avoid overcommit). *Mechanism:* flip the ≈ −$1,300
   combat-net-swing toward a wash. *Cost:* med. *Evidence:* measured combat bleed, but
   **secondary** — ~10–20× smaller payoff than the capture axis.

4. **Diagnostic-logging lever (not a gameplay change).** Add §3's two-sided capture +
   capture-income-timeseries + terrain-tagged-death logging, then re-run **one** gated
   S2 rung before committing to a gameplay lever. *Mechanism:* converts the untestable
   item-26 hypothesis and the one-sided timing inference into measurable signals.
   *Cost:* low (logging) + one gated run. *Evidence:* the enabling step — current
   artifacts cannot separate "captures later" from "holds shorter", nor test forest.

5. **Score-model sensitivity (reframe, not a bot change).** The ladder is dominated by
   `capture_income` weighting (~10–20× combat in `score_total`). If that over-represents
   capture relative to design intent, part of the "deficit" is a scorer artifact.
   *Mechanism:* re-weight, or report combat and capture as **separate** ladders, so a
   combat-focused lever is not graded mostly on capture. *Cost:* low. *Evidence:*
   measured score composition; a user decision on whether the current weighting is
   intended.

---

## 7. Artifact reference map

| Claim | Source |
|---|---|
| Per-match stats, all 60 matches | `tools/autotest/tournament-results/rebase_*/match_*.json` |
| Contested-capture 1.9× | `rebase_armA_s{1,2}/match_{6,8}.json` `score_components.capture_income` |
| Control ~1.0× | `rebase_cal_s2/match_*.json` (both players `bot_type=stable`) |
| All matches time_limit | `win_reason` across `rebase_arm*/match_*.json` |
| No forest/cover logging | `grep -ic forest\|density\|shadow rebase_*/match_*_debug.log` → 0 |
| Capture-timing reading | `rebase_armA_s2/match_6_debug.log` `[exp-capture] committed=` |
| Distilled prior read | `WORKSPACE/ai-bench/runs/260728_rebaseline_result.md` (`5dc14934`) |

Instrument SHA per card: functional `e5b7bbcc` (engine + rules byte-constant across the
run; HEAD drifted on docs/recon commits only). This recon read `main` @ **6bc11710**.
