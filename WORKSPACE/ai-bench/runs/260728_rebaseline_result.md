# RESULT — Stage-F re-baseline + item-8 gate (b) ambush pricing (RUN 2026-07-29)

**Status: RUN COMPLETE.** Executes the plan `260728_rebaseline_runplan.md` (`7fa0b046`).
40 measured matches + 20 calibration matches = 60 matches, 0 crashes, 1 wall-cull
(cal-S1 retry, resolved). This card is the distilled record (SPEC §8.3); raw per-match
JSON/debug logs live under `tools/autotest/tournament-results/rebase_*` (git-ignored).

---

## Instrument (the "zero")

| | |
|---|---|
| **Functional SHA** | `e5b7bbcc` — last commit touching `engine/**` or `mods/ww3mod/rules/**` before the run. **Verified constant**: `git log e5b7bbcc..HEAD -- engine mods/ww3mod/rules` is EMPTY across the whole run; HEAD drifted `e5b7bbcc → 44c2b513` on **docs/recon commits only** (concurrent workers in this shared checkout). Per-rung stamped SHAs differ only by those docs commits. |
| Engine binary | current (`OpenRA.Mods.Common.dll`, 0 `.cs` newer). No rebuild during the ladder. |
| NUnit | 524/524 baseline (context; not re-run — this is a benchmark, not a code change). |
| Includes (global, both bots) | item 26 (`fc9fe396`, forest `DensityModifiesDamage` + superlinear ground-shadow) + item 28 (`1f036ecb`, path string-pulling). **These are why the re-baseline was required** — they broke `@stable` byte-identity globally, so old `@stable`/cal numbers do not carry over. |
| Profile | `--hidden` (SDL_WINDOW_HIDDEN, no focus steal) on every match — the run-tournament default path. |

### Deviations from the plan (all recorded, none change the measurement)

1. **⚠ Item-24 gates found ON in shipped `main`** — the plan (authored `0b0783be`, PRE
   item-24) and PIPELINE/board both record `StrategicCaptureRepointEnabled` /
   `DefendRepointEnabled` as **default-off**, but item-24 (`646515bd`, merged AFTER the
   plan) left them **`true`** in the `@experimental` YAML (`ai.yaml:180`, `:309`).
   PIPELINE item-24 itself says *"Gate enablement awaits the item-25 re-baseline."* Per
   the user's decision (dashboard, 2026-07-29 — *"the new zero must exclude item-24 so
   the later item-24 A/B measures against a clean baseline"*), both gates were
   **temporarily set `false`** for BOTH Exp arms (A and B) and **reverted** afterward
   (`git checkout`; verified `true`/`true`/`enable-ai-experimental` restored, tree clean).
   `StrategicRepointEnabled` (item-25, `ai.yaml:253`) was kept **ON** — it is the subject
   of this re-baseline. **This discrepancy (gates shipped ON while docs say off) is
   flagged for the user.**
2. **`--config` added to every rung** — the plan's §3 commands omit it, but no scenario
   has a default `tournament.yaml`; they use `tournament-eco-5min.yaml` /
   `tournament-combat-12min.yaml`. Without `--config` the harness exits 3 (config not
   found). Added the correct per-scenario config path. Mirror matches share the primary's
   config (harness resolves config once; only the map swaps) — configs are byte-identical
   anyway. No measurement change.
3. **Wall-cap bumped 150→300 (S1) / 400→600 (S2)** — the plan's 150s cap culled cal-S1
   match 6 at ~tick 7000/7500 (sim was advancing normally; transient contention from
   concurrent workers, not a hang). The plan's stated intent is "generous enough the
   watchdog never kills a natural-length match"; 150/400 proved too tight under this
   shared checkout's load. Re-ran cal-S1 at 300s → clean 10/10. All 5 subsequent rungs
   10/10, 0 culls. Cap only affects whether slow matches survive (watchdog breaks
   immediately on PID death) — the sim is identical.

---

## 1. Calibration re-zero (the new yardstick) — Stable-vs-Stable, River Zeta, N=10 each

Same map as the live rungs; no mirror scenario exists for cal (all 10 = same map, varied seed).

| Rung | Win split (P1–P2) | Key noise-floor numbers |
|---|---|---|
| **Cal S1 eco** | 4–6 | capture gross **P1 med 6188.5 / P2 2975.5** (≈2× P1-slot spawn-capture bias — the bias the live `--mirror` cancels); resources_earned 0; combat ~0, engaged **3/10** (eco = little fighting) |
| **Cal S2 combat** | 4–6 | net swing median **−225** (spread [−4150, +2000]); sign 2/10; **engaged 7/10**; engagement median 1525 |

**Yardstick read:** combat noise band ≈ **±$2000** per match with a slight P2-slot lean
(median −225); win-rate noise ≈ ±2/10; the natural Stable-vs-Stable engaged rate is
**7/10**, i.e. the gate's 6/10 validity floor sits right at the organic rate. Cal S1's
capture bias (P1 ≫ P2) reproduces the 2026-07-21 baseline cal (6113/2976) — the stable
side of the instrument is consistent.

---

## 2. Arm A re-baseline — item-25 repoint ON + **item-24 OFF** + ambush ON — Exp-vs-Stable, N=10, mirror

| Rung | Win | Net swing (Exp) | Capture (Exp/Opp) | Engaged | Notes |
|---|---|---|---|---|---|
| **S1 eco** | **3–7** (0.30) | median −1425 | 4/10 vs 4/10 (parity); Exp gross mean 2520 (bimodal 0 / ~6300) | 10/10 | eco rung erupts into combat when Exp plays; Exp out-traded (deaths ≫ kills) |
| **S2 combat** | **4–6** | median **−1425** (spread [−3950, +800]); sign 2/10 | 4/10 vs 4/10 | **10/10** (valid) | engagement median 2600; both-spawn swing primary −2350 / mirror −1050 |

**This is the new Exp-vs-Stable zero on the post-26/28 instrument.** Exp **underperforms
Stable** on both rungs — worse than the pre-26/28 `260721` baseline (which was Exp ≈
Stable, S2 edge −350). Items 26+28 (both global) moved the instrument so the aggressive
Exp bot (engaged 10/10, high engagement) trades poorly. `[exp-terr]` repoint fires but is
behaviorally quiet here (`boosted=0 damped=0 neutral=…` — control/danger fields rarely
tip axis selection on these maps).

---

## 3. Arm B — ambush OFF (item-25 ON, item-24 OFF) — Exp-vs-Stable, N=10, mirror

| Rung | Win | Net swing (Exp) | Capture (Exp/Opp) | Engaged | Notes |
|---|---|---|---|---|---|
| **S1 eco** | **5–5** (0.50) | median −875 | 4/10 vs 4/10; Exp gross mean 3068 | 10/10 | — |
| **S2 combat** | **3–7** (0.30) | median **−1825** (spread [−4800, −50]); sign 0/10 | 4/10 vs 4/10 | 10/10 | engagement median 4000 |

---

## 4. Gate (b) — ambush default-on pricing (paired A−B, same seeds)

| Rung | ArmA(ON) win | ArmB(OFF) win | Swing med A / B | Paired swing Δ (A−B) | Read |
|---|---|---|---|---|---|
| **S1 (guard)** | 0.30 | 0.50 | −1425 / −875 | median **−575**, **mean 0** (one +3600 outlier), +4/10 | ambush ON **worse** (−2 wins; win-rate 0.30 **< 0.40 floor**), swing Δ noise (mean 0) |
| **S2 (primary)** | 0.40 | 0.30 | −1425 / −1825 | median **+425**, mean +630, +6/10 | ambush ON **mildly better** (+1 win, +$425 median swing); engaged 10/10 valid |

Capture parity identical across all four arm-rungs (Exp 4/10 vs Opp 4/10). Both A−B
effects sit **inside the calibrated noise band** (±$2000 swing, ±2/10 win-rate), and the
**two rungs disagree in sign**.

### RECOMMENDATION (for the user's decision — not applied; ai.yaml left default-on)

> **Default-on NOT SUPPORTED by the benchmark → lean default-OFF, but the signal is
> within noise / inconclusive at N=10.**

Reasoning, against the plan's decision rule (tie → OFF; keeping complexity default-on
requires *measured non-harm on BOTH rungs*):
- **KEEP-on fails**: it needs non-harm on both rungs, but **S1 regresses** — ambush ON
  drops Exp win-rate to **0.30, below the 0.40 non-regression floor** (and −2 wins vs OFF).
- **S2 (primary combat rung) mildly favors ambush** (+$425 median paired swing, +1 win,
  6/10 positive deltas, engaged 10/10) — a real but noise-scale pro-ambush signal.
- Because the rungs disagree and both effects are noise-scale (1–2 match / sub-$500-median
  swings against a ±$2000 floor), this is closer to **inconclusive** than a decisive OFF.
  Per the plan's tie-break ("a pure wash does not clear the bar for keeping complexity on
  by default"), inconclusive ⇒ **lean OFF**.

**Two caveats that belong with the user, not the benchmark:**
1. The **dominant** result is that Exp is **~$1,400–1,800 underwater vs Stable on S2
   regardless of ambush** (and loses/ties the win split). The ambush A/B is a second-order
   wobble on a bot that is losing; fixing the Exp-vs-Stable deficit matters far more than
   this default.
2. Ambush's value was always qualitative ("units feel alive"). If the user weights that
   over a noise-scale, mixed benchmark result, **keeping default-on is a legitimate user
   call** — record it; the benchmark neither earns nor forbids it decisively.

---

## 5. Firing proofs (from preserved per-match debug logs)

| Check | Result |
|---|---|
| item-25 repoint fired (Arm A) | ✅ `[exp-terr] reeval` present — 2710 lines in armA-S2 (behaviorally quiet: mostly neutral) |
| ambush fired (Arm A) | ✅ `[exp-ambush]` — 1263 lines (S1) + 3083 lines (S2) |
| ambush ABSENT (Arm B) | ✅ **0** `[exp-ambush]` in **every one of the 20 arm-B matches** |
| item-24 belief-repoint OFF (Arm A) | ✅ 0 capture-path belief-repoint markers |

The A/B is clean: the only behavioral difference between arms A and B is ambush on/off.

---

## 6. Result-dir map (git-ignored raw)

| Rung | dir | stamped SHA |
|---|---|---|
| Cal S1 | `rebase_cal_s1` | 64b39f50 |
| Cal S2 | `rebase_cal_s2` | 64b39f50 |
| Arm A S1 | `rebase_armA_s1` | 4efe523f (dirty = item-24-OFF edit) |
| Arm A S2 | `rebase_armA_s2` | c11ce511 (dirty) |
| Arm B S1 | `rebase_armB_s1` | 77dbfb7d (dirty = +ambush-OFF edit) |
| Arm B S2 | `rebase_armB_s2` | b3d5d7e1 (dirty) |

All stamped SHAs differ only by docs/recon commits; the gameplay instrument (`engine/**` +
`mods/ww3mod/rules/**`) was byte-constant, git-log-verified.
