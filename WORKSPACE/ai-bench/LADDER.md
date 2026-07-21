# AI Benchmark — Scenario Ladder

The **ladder** is the ordered set of standardized tests the Experimental AI
(`ModularBot@v2`) must beat to demonstrate improvement. Governed by
[`SPEC.md`](SPEC.md) (advancement §6, run modes §3, data §8). This file is the
**definition** of the rungs and scenarios; the **live standing** (current medians
vs control) lives in [`REVIEW.md`](REVIEW.md) §Ladder Status.

> **Terminology — v2 → Experimental (2026-07-20, `ai-bench` rename commit).** The
> Experimental AI is now `ModularBot@experimental` (not `@v2`), joined by a frozen
> `ModularBot@stable` (SPEC §13). Narrative "v2" mentions below (e.g. "P1 Experimental
> (v2)", "median(v2 earned)") read as **Experimental** and are left as written; only
> the machine-actionable **scenario folder paths** in the registry were repointed to
> their new names: `tournament-v2-vs-normal-2p` → `tournament-experimental-vs-normal-2p`,
> `tournament-v2-vs-normal-mirror-2p` → `tournament-experimental-vs-normal-mirror-2p`.

---

> # ⚠ REGIME CHANGE — 2026-07-21 (user-directed). ALL NUMBERS BELOW ARE `[pre-regime]`.
>
> The tournament instrument was changed in three ways at once. Every S1/S2 baseline,
> bar, median, and win-split recorded **anywhere below this banner** was measured
> under the **OLD regime** — `startingunits=none` (bots start with only the two
> `supplyroute` beachheads), **mixed factions** (US vs Russia), and **opponent =
> `@normal`**. Those numbers are `[pre-regime]`: **not comparable** to the new
> regime and **must not be silently reused as bars**. Every bar awaits **re-baseline**.
>
> **The new regime (what every S1/S2 scenario now measures):**
> 1. **Starting units = Motorized** on both sides (`StartingUnitsClass: motorized`
>    on each bot `PlayerReference`; the Motorized set is `world.yaml:404-419`).
>    Rationale (user): "how most players will play" + gives early AA against
>    helicopter cheese. Note the Motorized set is **faction-asymmetric** (America:
>    `abrams,bradley,humvee`; Russia: `t90,bmp2,bmp2`) — see change 2.
> 2. **Same faction both sides (US vs US)** — both bots `Faction: america`, so both
>    field the identical `@Motorized_america` starting force. This removes faction
>    imbalance (and the Motorized composition asymmetry) from the measurement.
>    Faction balancing is explicitly **later work**.
> 3. **Primary opponent = `@stable`** (the frozen post-S1 PROMOTE snapshot), not
>    `@normal`. Normal is **demoted to a single sanity-floor scenario**
>    (`tournament-s1-eco-floor-vs-normal`, Exp-vs-Normal, same new regime) — a
>    "has Experimental regressed below the frozen control?" check, **not** part of
>    the composite gate. Old Exp-vs-Normal history is kept, not deleted.
>
> **Consequence for the mirror twins:** with both sides now `america`, the S1 **and**
> S2 bot-swap mirrors are pure **spawn** swaps (previously S2's swap also flipped
> faction). The calibration (`-cal-nn`) scenarios are now **Stable-vs-Stable**, not
> Normal-vs-Normal. Re-run the CALIBRATE (side-fairness + S2 min-engagement) and the
> S1/S2 BASELINEs on the new regime before trusting any bar. Scenario tables + registry
> below are updated to the new regime; the historical result blockquotes are left in
> place as `[pre-regime]` record (this banner marks them all).

---

> # ✅ REGIME RE-BASELINE COMPLETE — 2026-07-21 (`main` @ `60b93501`, N per SPEC)
>
> First full re-baseline on the new regime (Motorized / same-faction US-US / vs
> `@stable`). 43 matches, 5 phases, **0 crashes / 0 no-verdict**, ~78 min batch
> wall-clock. Full analysis: [`runs/260721_regime_rebaseline.md`](runs/260721_regime_rebaseline.md).
>
> | Rung | Calibration (Stable-v-Stable) | Baseline (Exp-v-Stable) | Verdict |
> |---|---|---|---|
> | **S1 eco** | side lean **7–3 Russia-slot(80,35)**; both slots capture (USA gross med 6113, Russia 2976) | win **5–5**; capture **6/10 vs 6/10** | Exp **≈** Stable — no economy edge |
> | **S2 combat** | side **even 5–5**, swing 0 / −725; **engagement collapsed ~5–6×** (1200/1925 vs pre-regime 7475/5950), **3/10 zero-combat** | win **5–5**; swing edge **−350** | Exp **≈** Stable — no force edge |
>
> **Core finding:** Experimental ≈ Stable on BOTH rungs. Stable is the frozen
> snapshot of the last validated Exp config; the only Exp/Stable delta today is the
> SR-contestation axis, which helped vs Normal but is **neutral-to-negative vs a
> competent same-faction Stable**. The benchmark has *caught up to the bot* — the
> next behavior cycle needs a genuinely new lever. Floor (Exp-v-Normal, N=3) = weak
> **2–1** pass (Motorized start equalizes early armies + single-spawn handicap).
>
> **PROPOSED BARS (derived from this data — PROPOSED, awaiting ratification; the
> `[pre-regime]` bars are void):**
> - **S1:** paired win-rate **≥ 0.60** AND Exp capture-rate ≥ Stable **+2/10**;
>   non-regression floor win-rate ≥ 0.40 + capture parity. *(Current: 0.50, 6/10 vs
>   6/10 → not passing; floor holds.)*
> - **S2:** paired-relative median(Exp swing) ≥ median(Stable swing) **+ $1,000**
>   (lowered from $1,400 — the attrition offset that inflated it is gone) AND
>   sign-delta ≥7/10 AND both-spawn ≥3/5. **Blocking batch-validity gate:** ≥6/10
>   matches with real engagement, else re-scope S2 (forced contact / `@rush`).
>   *(Current: edge −350, sign 3/10 → not passing; batch 7/10 engaged, barely valid.)*
> - **Open decision (user):** keep S2 on the quiet same-faction Stable regime, or
>   move to a forced-contact / `@rush` variant to restore combat signal?

---

> # ✅ `[cohesion-cap]` RE-BASELINE — 2026-07-21 (`main` @ `1eb644de`, N per SPEC)
>
> Declared re-baseline after Phase 0 global cohesion cap (`1eb644de`: bound
> `ComputeBoxSlots` footprint + regroup-on-arrival), which ships to **everyone incl.
> the frozen controls** — so the `60b93501` regime numbers above are void and
> re-measured here. 43 matches, 5 phases, **0 crashes / 0 no-verdict**. Same regime
> (Motorized / same-faction US-US / vs `@stable`); deltas vs `60b93501` isolate the
> **cohesion cap alone**. Full analysis:
> [`runs/260721_cohesion_cap_rebaseline.md`](runs/260721_cohesion_cap_rebaseline.md).
>
> | Rung | Calibration (Stable-v-Stable) | Baseline (Exp-v-Stable) | vs `60b93501` |
> |---|---|---|---|
> | **S1 eco** | 7–3 Russia-slot; USA gross med 6113 / Russia 2976 | win **5–5**; capture **6/10 vs 6/10** | **byte-identical** (cap inert) |
> | **S2 combat** | even **5–5**, swing 0 / −725; eng 1200 / 1925; 3/10 zero-combat | win **5–5**; swing edge **−100** (was −350); Exp eng med **1775** (was 1300) | S2-cal identical; **S2-base MOVED** (cap binds) |
>
> **What the cap moved:** exactly one rung — **S2 Exp-vs-Stable**. All three S1
> rungs and the S2 Stable-vs-Stable calibration are byte-identical to `60b93501`
> (small/passive formations never reach the cap → deterministic replay unchanged).
> On S2-base the cap **binds only** on Experimental's SR-contestation assault groups
> (large enough to exceed the footprint cap): engagement rises **+37%** (Exp
> 1300→1775, Stable 2800→3175) and Exp's swing edge improves **−350 → −100** — the
> predicted direction (tighter formations → more concentrated fights). Effect is
> seed-dependent (seed 8017 identical; 6017 shifted).
>
> **Nothing flips.** Win still **5–5**; edge −100 still fails the PROPOSED **+$1,000**
> S2 bar; sign-delta **3/10** and both-spawn **2/5 / 1/5** unchanged; validity gate
> **7/10 engaged** (VALID). **Core conclusion unchanged:** Experimental ≈ Stable on
> both rungs; the cap is a human-play correctness fix that is benign on the benchmark.
>
> **PROPOSED bars now bind against `1eb644de`** (not `60b93501`) — still PROPOSED /
> unratified, unchanged values (S1 win ≥0.60 + capture ≥ Stable +2/10; S2 edge
> ≥ +$1,000 + sign ≥7/10 + both-spawn ≥3/5 + validity gate ≥6/10 engaged). Current:
> S1 0.50 / 6-vs-6, S2 edge −100 / sign 3/10 / engaged 7/10 → both not passing (correct).

---

> # 🧪 PHASE-2 EXECUTOR PRICING — 2026-07-22 (`main` @ `1a65ddf1`, executor merge `a88ef596`)
>
> Prices `StancePositioningExecutor` (idle-only stance-conditioned repositioning to
> threat-facing cover edges; `@experimental`-only, inert for `@stable`/`@normal`/
> humans) — the only behavior-bearing Experimental delta since the `[cohesion-cap]`
> re-baseline (`1eb644de`) + Phase 1 data layers. 20 matches, S1 + S2 vs `@stable`,
> **0 crashes / 0 no-verdict**. Paired per-seed deltas isolate the executor. Full
> analysis: [`runs/260722_phase2_executor_pricing.md`](runs/260722_phase2_executor_pricing.md).
>
> | Rung | Result (Exp-v-Stable) | vs `1eb644de` | Verdict |
> |---|---|---|---|
> | **S1 eco** | win **5–5**; capture **6/10 vs 6/10** | win/capture **identical**; order-drift on ~6 seeds, 1 outcome-neutral dip (6017) | **non-regression PASS**; promote bar not met |
> | **S2 combat** | median Exp swing **−350** (edge −350); sign **2/10**; both-spawn **1/5+1/5**; Exp eng med **675**; engaged **6/10** (VALID, at floor); win **6–4** | S2-base **MOVED**: eng ↓ (Exp 1775→675, Stable 3175→450), median swing ↓ (−100→−350), win split 5–5→**6–4** (8017 flip) | not passing; executor **≠** force edge |
>
> **What the executor moved:** exactly one rung visibly — **S2 Exp-vs-Stable**. On
> S1 the floor is byte-stable at the aggregate level (some intra-match order drift,
> no flips). On S2 it perturbs 7/10 seeds in the **less-engagement** direction (idle
> units hold cover instead of pressing contact → both sides' engagement volume
> collapses), yielding a slightly worse median swing and a marginal +1 win (rescued
> seed 8017, −4950 loss → −700 win). **Nothing flips a bar.** Both S1 and S2 remain
> non-passing against the PROPOSED bars — the executor is neutral-to-slightly-negative
> on force-efficiency, consistent with the standing "Experimental ≈ Stable" read.
>
> **Instrument watch:** the executor's engagement suppression pulled S2 to the
> **validity floor (6/10 engaged)** — reinforces the open question of moving S2 to a
> forced-contact / `@rush` variant to keep combat signal robust.
>
> **PROPOSED bars unchanged / still unratified** (S1 win ≥0.60 + capture ≥ Stable
> +2/10; S2 edge ≥ +$1,000 + sign ≥7/10 + both-spawn ≥3/5 + validity ≥6/10 engaged).
> Current: S1 0.50 / 6-vs-6, S2 edge −350 / sign 2/10 / engaged 6/10 → both not
> passing (correct). No promotion claimed.

---

## Ladder structure

- A **rung** is one map. The first (and currently only) rung is **River Zeta WW3**
  (`mods/ww3mod/maps/river-zeta-ww3/`, Title "River Zeta WW3"). **S1 (economy)**
  runs on `tournament-s1-eco-river-zeta` — a scenario whose `map.yaml` is derived
  from the canonical River Zeta terrain (full 98×82, all **12 neutral OILB income
  derricks kept**) with the harness overlay (2 SRs + 2 bot spawns). **S2 (combat)**
  runs on `tournament-s2-combat-river-zeta` — a **byte-identical copy of the S1
  River Zeta map** with a 720s combat clock (built 2026-07-20 per
  [`../plans/260720_s2_expand_design.md`](../plans/260720_s2_expand_design.md)); the
  central OILB cluster is the contested middle that forces the fight. **S3 (win-rate)**
  will reuse the same River Zeta rung (scenario TBD at S3 standup). The **old 66×34
  combat stub `tournament-experimental-vs-normal-2p` is RETIRED from the ladder** — it
  had zero capturables and sat on a *different* map than S1, contradicting the
  "one rung = one map" composite-gate model (§ below). Each scenario has a
  mirror twin for bias control (SPEC §9.4). (S1's mirror,
  `tournament-s1-eco-river-zeta-mirror`, was **built 2026-07-20** — bot-assignment
  swap, not faction swap, since S1's bias concern is derrick *distance* per spawn;
  S2's mirror `tournament-s2-combat-river-zeta-mirror` follows the same bot-swap
  pattern. **Regime update 2026-07-21:** with both sides now `america` (same-faction
  US-US), *both* mirrors are pure **spawn** swaps — S2's mirror no longer also flips
  faction (see the top regime-change banner).)
- Each rung holds **three scenarios**, each probing a different facet of "is the
  AI actually better": **economy**, **combat efficiency**, **decisiveness**.
- A rung is **cleared** by the **composite gate** (§ below): one single commit
  passes all three, re-verified together.
- Clearing a rung → advance by adding a second map and/or tightening margins
  (SPEC §10, §11).

### Metric extraction (applies to all scenarios)

Every metric below is read **post-hoc from the per-match verdict JSON**
(`match_<i>.json` → `notes` blob, SPEC §8.2) — no new engine scorer required. The
in-engine `WinRule` only decides the match's own winner; the **ladder metric is
whatever field the scenario names**, medianed over N. This keeps new scenarios
cheap (docs + a config, no engine work).

Common run knobs (in each scenario's `tournament.yaml`): `TimeLimitSeconds`,
`SpeedMultiplier` (8 in Mode B / 6 in the Mode A fallback, SPEC §3), `Scorer`,
`WinRule`, `Score` weights. The harness assigns `MATCH_SEED = i*1000+17` for
`i = 1..N` (`run-tournament.sh:283`). **UPDATE (2026-07-20, `2d3c8fe0`): per-seed
replay is now DETERMINISTIC.** `LocalRandom` is seeded from the lobby `RandomSeed`
via a decorrelating PCG transform (`World.cs:213-214`), and same-seed→byte-identical
verdicts (incl. the tick-by-tick score log) were **verified** (DISCOVERIES 2026-07-20;
the 2026-07-19 "seeds are run labels, replay broken" entries are **superseded**). So
the fixed per-index seed set makes every cycle reuse the **same battlefields** →
cross-cycle and control-vs-experimental comparisons are **paired** (a large variance
reduction the S2 bar exploits). Each run is still a valid independent-across-seeds
sample for N-run medians; "re-verification" re-runs the N matches (now reproducibly).
**Caveat:** don't overfit behaviors to the fixed 10 seeds — rotate/expand the set at
BASELINE if a behavior only wins on the standard set. The even/odd index split
selects primary vs mirror *scenario* per match (index parity, not RNG).

---

## Scenario 1 — Economy Race (the user's sketch)

**Question:** does the Experimental AI's POI-capture layer actually convert into
*more money earned* than Normal in a fixed window?

| Field | Value |
|---|---|
| Scenario | `tournament-s1-eco-river-zeta` (**genuinely on River Zeta terrain** — 98×82, all 12 neutral OILB derricks), + `tournament-s1-eco-river-zeta-mirror` for bias (built 2026-07-20) |
| **Regime (2026-07-21)** | **Motorized start both sides** (`StartingUnitsClass: motorized`), **same faction US-US** (both `america`), **opponent = `@stable`**. Mirror = pure spawn swap. Cal analog `tournament-s1-eco-cal-nn` is now **Stable-vs-Stable**. Sanity floor `tournament-s1-eco-floor-vs-normal` keeps one Exp-vs-Normal check (same regime). All numbers in this section are `[pre-regime]` (see top banner) → **re-baseline pending**. |
| Contestants | P1 Experimental (`@experimental`) vs P2 **Stable** (`@stable`, frozen control) — *was* P2 Normal `[pre-regime]` |
| Match length | **5 minutes** — `TimeLimitSeconds: 300` (candidate bump to 420–600s pending — see finding below) |
| **Metric** | **`capture_income_gross`** (cumulative GROSS building income, pre-upkeep, verdict `stats.capture_income_gross`, verdict_version 4 — integrated read-only from `PlayerResources.TotalBuildingIncome`, `GrossIncomeIntegrator`). `resources_earned` (net `PlayerResources.Earned`) stays in the verdict as **context only**, not the metric. As of verdict_version 4 the *scorer* economy term (`score_components.capture_income`, which feeds the WinRule) also reads this gross value (follow-up 1a). |
| N runs | **10** (5-min matches are cheap: ~1–2 wall-min each hidden at 8×) |
| Seeds | `1017, 2017, … 10017`; even = primary, odd = mirror (`--mirror`) |
| Advancement | `[pre-regime]` `median(v2 earned) ≥ median(Normal earned) × 1.15` (15% margin) — **await re-baseline vs `@stable` on the Motorized/same-faction regime** before this (or the recommended reliability bar below) is trusted |
| WinRule | `score_or_sr_capture` (irrelevant to the metric; keep for a valid match end) |

> **RESCOPE (2026-07-19, `86aa2db` branch):** S1 was moved off the old
> `tournament-v2-vs-normal-2p` stub, which ran on a bare 66×34 inline map with
> **zero capturable POIs** — pinning `resources_earned` to 0/0 *by construction*
> (diagnosis: `WORKSPACE/ai-bench/runs/260719_s1_diagnosis.md`). The prior "runs
> on River Zeta" premise was **false**; it is now **true**. The new scenario keeps
> the 12 OILB derricks, which are confirmed capturable (OILB → `^TechBuilding` →
> `^BasicBuilding` → `^NeutralOrOccupiedCapturable`). SRs at **14,45** (v2) and
> **80,35** (normal), each with a derrick ~3–4 cells away.
>
> **Smoke finding (N=1, hidden):** verdict written, full 7500-tick match, **but
> `resources_earned` still 0/0** — for a *new* reason. The map-content wall is
> gone; the smoke exposed that **neither bot captures a derrick in 5 game-minutes**
> (score curve is pure combat, `capture_income` stays 0). Normal earning $0 is
> correct (no capture logic — the control won't game the eco axis). **v2 earning $0
> is the live finding:** its `PoiMap → CaptureCoordinatorBotModule` layer did not
> convert the now-present POIs into a capture in-window. S1 is therefore now a
> **live, discriminating** test (can read non-zero once v2 improves) rather than
> structurally dead.
>
> **UPDATE (2026-07-19, `2d5433a` branch — first AI cycle):** the "v2 doesn't
> capture" framing above is now **resolved and superseded**. Root cause of the
> no-capture was AI mis-scoring: `PoiMap.IncomeWeights` weighted the `$0`
> `logisticscenter: 200` (highest of all) so v2's sole TECN was sent cross-map to
> a no-income depot. **Fixed** by delisting `logisticscenter` from both v2 income
> tables (`world.yaml` PoiMap + `ai.yaml` CaptureCoordinator@v2). Hidden N=1 smoke
> **confirms the fix works**: v2's top target flipped to the nearest OILB
> (`oilb@17,44`, `targets 15→13`), and **v2 captured it (~t1550) and still owns it
> at match end** (`buildings_killed/dead=0` → not destroyed; USA `assets_value 1450
> = army 1250 + 200` building). **But `resources_earned` is STILL 0** — for a *new,
> metric-side* reason: it reports `PlayerResources.Earned`, which in this SR-budget
> economy only rises on a **net-positive** periodic tick (`PassiveIncome +
> TotalBuildingIncome − Upkeep`, `PlayerResources.cs:199-211`) and is never
> credited via the (unused) harvester path. A lone captured `$50` derrick doesn't
> net positive, so `Earned` is **structurally blind to owned-derrick income**
> (both bots read 0 the whole match, before AND after the capture). The S1 metric
> itself, not v2, is now the blocker. Finding: `runs/260719_s1_earned_metric_finding.md`.
>
> **Required follow-ups:**
> 1. ~~**NEXT CYCLE = metric fix (harness-side, no AI change, no re-roll):** replace
>    `resources_earned` with a **gross** derrick-income signal.~~ **DONE (verdict_version 3):**
>    `BotVsBotMatchWatcher` now integrates `PlayerResources.TotalBuildingIncome` per tick
>    into a read-only `GrossIncomeIntegrator` and emits `stats.capture_income_gross`
>    (additive; `resources_earned` unchanged for context). S1 metric repointed above.
> 1a. ~~**WIN-RULE ECONOMY TERM — deserves loop-manager review (NOT changed here, by design):**
>    `WeightedComponentMatchScorer` still feeds its `capture_income` component (and thus
>    the `TimeOrSrCaptureWinRule` outcome for S2/S3) from **net** `PlayerResources.Earned`.~~
>    **RESOLVED (2026-07-20, user-approved) — verdict_version 3→4.** The scorer's
>    `capture_income` component now reads the **GROSS** integral (`state.GrossCaptureIncomeFor`,
>    the same value emitted as `capture_income_gross`) instead of net `Earned`, so the economy
>    axis counts held-derrick income in match *outcomes* (this scorer feeds the win rule).
>    Emitted JSON fields are unchanged (`capture_income_gross`, `resources_earned` both stay);
>    `verdict_version` bumped 3→4 to flag the changed *meaning* of the emitted
>    `score_components.capture_income`. Approved **now** precisely because S2/S3 have **no
>    recorded baselines yet** — the redefinition is free (nothing to re-baseline). Extracted
>    the weighting math to `WeightedComponentScoring` + `WeightedComponentScoringTest`
>    (5 cases); NUnit 282→287, build green. See
>    `runs/260720_0120__tournament-s1-eco-river-zeta__<sha>.json` and the mirror cycle card.
> 2. **POI symmetry / calibration (after the metric can see income):** build
>    `tournament-s1-eco-river-zeta-mirror` and gate S1 on a **Normal-vs-Normal batch
>    landing ~even** on the new metric (SPEC §9.4) — otherwise an earned gap could be
>    spawn-side derrick luck, not AI skill. **DONE (2026-07-20, `f8052ec`):** mirror +
>    Normal-vs-Normal calibration (`tournament-s1-eco-cal-nn`) both run at N=10 — map is
>    mostly side-fair with a **mild russia/80,35 lean** (6–4 wins, score median 2875 vs 3675),
>    neutralised by the mandatory mirror. Baseline: `runs/260720_s1_baseline_n10.md`.

> **BASELINE + BAR-REFORM RECOMMENDATION (2026-07-20, `f8052ec`) — not yet applied,
> pending user ratification.** N=10 Experimental-vs-Normal (5 primary + 5 mirror): Experimental
> beats Normal **8–2, symmetric by spawn** (won 4/5 from each side — real skill, not spawn
> luck). But **in-window capture rate is only 4/10** (6/12 incl. diagnostics), so
> `capture_income_gross` **median is 0** (below 50% capture the median sits in the zero mass),
> while gross-**when-captured** median is **6047**. The Normal control captures ~1/20 → its
> gross median is ~0. **The `median ≥ control ×1.15` bar is therefore degenerate** (0 ≥ 0
> trivially; ×1.15 of ~0 is still ~0 — it can neither fail a bad bot nor pass a good one).
> **Recommended replacement (SPEC §6.3 fixed-target):** gate on **reliability** — in-window
> **capture rate ≥ 6/10** (`gross > 0`) as the primary bar, plus **conditional gross median
> ≥ $5000** over captured runs; once capture rate > 50%, collapse to a single **median gross
> ≥ $3000**. This is a *recommendation logged here*; the Advancement row above is left
> unchanged until the user ratifies (flagged in REVIEW Open Questions). The binding constraint
> on S1 is **capture reliability**, not scoring — next behaviour cycle targets that.

> **CYCLE 1 RESULT (2026-07-20, merged `4dc3939d`) — bar FAILED, diagnostic success.**
> Capture-reliability cycle 1 (TTL 300→600 + `INotifyKilled` scan-reset + M-1/M-2/M-3 capture
> markers + per-match `debug.log` preservation) run N=10 hidden Mode-B (5 primary + 5 mirror):
> **in-window capture rate 4/10 → FAILS the reliability bar (≥6/10)**; conditional gross median
> **$6,377 ✅ (≥$5000)**; **win split 8–2** (all unchanged from baseline — the change measured
> behavior-neutral). **Marker verdict:** the binding constraint is **TECN
> production/availability**, not survival — **88% of 994 `no-idle-capturers` scans had
> `total-tecns=0`**, and **5/10 matches fielded zero TECNs the entire match** (0 capture orders);
> `tecn-killed` fired only twice, both on **uncommitted** TECNs not pursuing a derrick (so F-1
> "killed en route" and F-4 escort-screen are **not** what the runs show). F-2 (TTL expiry) is
> minor: 2 expirations, both on ~25-cell outlier targets beyond even TTL=600. **F-5 (failure
> break-points invisible) is now CLOSED** — that observability is the cycle's landed value.
> **Cycle 2 targets TECN call-in/availability** (build cadence, `ConsumedByCapture` pool drain,
> a "keep N ready" floor — `tecn.*: 3` is a ceiling, not a floor), upstream of the capture loop.
> Analysis: [`runs/260720_capture_reliability_cycle1_n10.md`](runs/260720_capture_reliability_cycle1_n10.md).

> **CYCLE 2 RESULT (2026-07-20, merged `c6a71c14`) — bar PASSED.**
> TECN availability floor: a default-off `TecnFloor` field on `CaptureCoordinatorBotModule`
> that, at the M-2 `no-idle-capturers` branch, pulls one capturer via the shared UnitBuilder's
> `IBotRequestUnitProduction` queue when `alive+pending < floor` AND a derrick is still
> capturable. That request path bypasses the `UnitsToBuild` share-ceiling AND `UnitLimits`,
> so it out-competes the blind lottery that starved the pool. `TecnFloor: 1` set **only** on
> `@experimental.tecn` (`@stable.tecn` default 0 → byte-identical). N=10 hidden Mode-B
> (5 primary + 5 mirror): **in-window capture rate 4/10 → 8/10 ✅ (≥6/10)**; conditional gross
> median **$7,726 ✅ (≥$5000)**; **win split 8–2 → 10–0** (no collapse — captures now feed the
> gross-income scorer axis). **Marker proof:** `tecn-floor-request` fired 298× (faction-correct
> build-type resolve), M-2 `total-tecns=0` scan share **88% → 76%**, and **matches fielding zero
> TECNs all-match 5/10 → 0/10**. Residual 2/10 misses are upstream production-throughput /
> dispatch latency (m2 america floor-goes-quiet after first capture since the gate is M-2-only;
> m7 russia requested 82× but never converted) — **next cycle:** extend the floor past the M-2
> gate + escort-bundled reinforcement packaging.
> Analysis: [`runs/260720_tecn_floor_cycle2_n10.md`](runs/260720_tecn_floor_cycle2_n10.md).

> **DISPERSION DOCTRINE VERIFY (2026-07-20, merged `exp-dispersion` → main) — activation +
> non-regression PASS on S1.** Spread-to-move / mass-to-assault (`PoiOffensiveBotModule`
> distance-gated `SetCohesion`, `@experimental`-only, kill-switch defaults off so `@stable`
> is untouched) run N=10 hidden Mode-B (5 primary + 5 mirror): **win 9/10 ✅ (≥7), capture
> 9/10 ✅ (≥4), conditional gross median $11,191 ✅ (≥$5000)** — no regression on the
> validated S1 economy behavior. **Activation proven:** 130 `[exp-offense] order` lines carry
> the `cohesion=` token and the `AssaultRadiusCells=15` gate fired with **0 violations** (all
> 52 Spread orders at distToTarget 16–61, all 78 Tight at 3–15). **Caveat:** S1 is an
> *economy* scenario and cannot exhibit dispersion's real value — it is a *combat-survival*
> movement mechanic, so **dispersion's real combat signal awaits an S2-class (Force
> Efficiency) rung** (mean pairwise spacing en route vs at assault; units-lost-on-approach).
> The capture-rate jump vs baseline (4/10 → 9/10) is **confounded** with main's cycle-1 merge
> and not credited to dispersion. Analysis:
> [`runs/260720_dispersion_verify_n10.md`](runs/260720_dispersion_verify_n10.md).

> **SEEDED REFERENCE BASELINE (2026-07-20, ran at `main` @ `e5a1c967`) — bar PASS; first
> paired-comparison reference set.** First fully-seeded N=10 of the merged bot on S1 (5 primary +
> 5 mirror, seeds 1017…10017), run on the first build with seeded `LocalRandom` (commit `2d3c8fe0`,
> **all verdicts v5 with `seed` stamped**). **In-window capture 8/10 ✅ (≥6); conditional gross
> median $6,457 ✅ (≥$5000); win split 10–0 ✅** — reproduces cycle-2's merged-bot tier (8/10, 10–0;
> cond median $7,726 there, this batch $6,457). 10/10 full 7500t matches, 0 no-verdict, 0 crashes.
> **Both residual misses are america/primary side** (seeds 2017, 8017) — same upstream
> production/dispatch-throughput constraint cycle-2 flagged (floor is M-2-gated, goes quiet after
> america's first capture). Because seeds now replay identically, **future S1 cycles diff against
> this per-seed table seed-by-seed** (paired comparison), not as independent samples; seeds
> 2017/8017 are the natural regression/improvement watch cells. Analysis:
> [`runs/260720_seeded_baseline_n10.md`](runs/260720_seeded_baseline_n10.md).

> **PROMOTE — Stable ← Experimental post-S1 snapshot (2026-07-20, SPEC §13).** The frozen
> **Stable** control is now the post-S1 Experimental config: the two `@experimental`-vs-`@stable`
> config deltas were copied down into the `@stable` blocks in `mods/ww3mod/rules/ai/ai.yaml` —
> (1) `CaptureCoordinatorBotModule` **`TecnFloor: 1`** (cycle-2 availability floor, merged
> `c6a71c14`, capture 4/10→8/10) and (2) the `PoiOffensiveBotModule` **dispersion doctrine**
> (`CohesionSwitchEnabled: true`, `AssaultRadiusCells: 15`, `ApproachCohesion: Spread`,
> `AssaultCohesion: Tight`; merged `56a57349`, S1 non-regression win 9/10). All other module
> pairs were already field-identical; the two shared single-instance modules
> **`PoiGoalGuard@poi`** and **`MountedTransportBotModule@poi`** (gated
> `enable-ai-experimental || enable-ai-stable`) are **not** twinned and were left untouched
> (a second instance throws — SPEC §13). Normal/Rush/Turtle byte-untouched. Evidence for the
> promotion: the seeded reference baseline above (`runs/260720_seeded_baseline_n10.md`,
> Experimental 8/10 capture / 10-0 vs the pre-cycles Stable-tier 4/10 / 8-2); §13 user-acceptance
> AUQ posted default-promote. Build green + NUnit 291/291. Commit
> `PROMOTE: Stable <- Experimental post-S1 snapshot (TecnFloor + dispersion)`.

> **SR-CONTESTATION CYCLE 1 — S1 NON-REGRESSION PASS (2026-07-20, merged `exp-sr-contestation` → main).**
> The enemy-Supply-Route Pressure axis is now score-boosted on `@experimental` only
> (`PoiOffensiveBotModule.SrPressureScoreMultiplier: 260`, default 100 = inert/frozen). N=10 hidden
> Mode-B (5 primary + 5 mirror, seeds 1017…10017): **in-window capture 8/10 ✅ (≥6), conditional gross
> median $6,457 ✅ (≥$5000), win split 10–0** — **identical to the seeded reference baseline** (same two
> $0 misses on america seeds 2017/8017), so the SR Pressure axis diverting combat units did **not** starve
> the TECN capture layer. **SR axis proven live in-window:** 8/10 matches open an `action=Pressure` axis at
> tick ~1600–2150 (mid-game, both spawns) — the 2 no-axis matches are the army-starved 2017/8017 cells.
> Analysis: [`runs/260720_sr_contestation_cycle1_n10.md`](runs/260720_sr_contestation_cycle1_n10.md).

> **PITFALL:** the S1 metric is **`capture_income_gross`** (verdict_version 4), NOT
> `resources_earned` and NOT `PlayerStatistics.Income`. `Income` is a rolling 60-second
> figure and `resources_earned` (net `Earned`) is blind to held-derrick income (below) —
> using either silently measures the wrong thing (SPEC §8.2).
>
> **PITFALL (deeper, found `2d5433a`, RESOLVED verdict_version 3):** `resources_earned` =
> `PlayerResources.Earned` is a **net** figure that only increments on a net-positive
> periodic economy tick and via the harvester path (unused in WW3MOD). It is **blind to a
> captured derrick's gross CashTrickler income** when that income doesn't overcome standing
> costs — so it reads `0` even when v2 genuinely captures and holds an income structure.
> S1 therefore uses **`capture_income_gross`** (gross `TotalBuildingIncome` integrated by
> `GrossIncomeIntegrator`); `resources_earned` is kept in the verdict for context only.

**Why 5 minutes:** long enough for the AI to call in TECNs (travel time from map
edge, game-model.md) and start capturing income structures; short enough to
isolate *economy* before combat attrition dominates. This scenario directly
exercises the POI capture pipeline (`PoiMap` → `CaptureCoordinatorBotModule`).

**Rationale for the 15% margin:** the capture layer is the Experimental AI's
clearest current advantage over Normal; a real economic edge should clear 15%
comfortably. If Normal's baseline earnings turn out noisy (median swings > 15%
run-to-run), switch to a **fixed target** (SPEC §6.3) — an absolute
"≥ $X earned" bar calibrated from a Normal-vs-Normal baseline batch.

---

## Scenario 2 — Force Efficiency (proposed)

**Question:** when the two AIs actually fight, does the Experimental AI's
score-floating spread offense (Phase 3, `PoiOffensiveBotModule`) **trade better**
— kill more value than it loses — instead of death-balling into a bad exchange?

| Field | Value |
|---|---|
| Scenario | `tournament-s2-combat-river-zeta` (**built 2026-07-20** — byte-identical River Zeta map as S1, 720s combat clock), + `tournament-s2-combat-river-zeta-mirror` (bot-swap twin) |
| **Regime (2026-07-21)** | **Motorized start both sides**, **same faction US-US** (both `america`), **opponent = `@stable`**. Now-same-faction map makes the bot-swap mirror a pure **spawn** swap (previously spawn+faction). Cal analog `tournament-s2-combat-river-zeta-cal-nn` is now **Stable-vs-Stable** (re-run CALIBRATE: side-fairness + min-engagement on the new regime). All S2 numbers below are `[pre-regime]` (top banner) → **re-baseline pending**. |
| Contestants | P1 Experimental (`@experimental`) vs P2 **Stable** (`@stable`, frozen control) — *was* P2 Normal `[pre-regime]`. (The old Rush-fallback question is re-opened by the new opponent + regime; re-CALIBRATE decides it.) |
| Match length | **12 minutes** — `TimeLimitSeconds: 720` (guarantees sustained contact) |
| **Metric** | **net combat budget swing** = `stats.kills_cost − stats.deaths_cost` (already emitted per player — read post-hoc, no scorer change, no S1 re-BASELINE) |
| N runs | **10** (paired seeds cut variance vs the draft's 15 — det. dividend; bump to 20 only if CALIBRATE shows control swing noisy) |
| Seeds | `1017 … 15017`; even primary, odd mirror |
| Advancement | `[pre-regime]` `median(v2 net swing) ≥ median(Normal net swing) + margin`, margin = **1× a single mid-tier unit's cost** — now measured **vs `@stable`** on the Motorized/same-faction regime; **await re-baseline** (the paired-relative form still applies, but both the offset and the control changed) |
| WinRule | `score_or_sr_capture` |

**Why this metric:** `kills_cost − deaths_cost` is the cleanest force-efficiency
signal already in the verdict — value destroyed minus value lost, both in budget
terms (which is what units *are* in WW3MOD: cost = budget allocation,
game-model.md). A death-ball that wins fights but overcommits shows a **low or
negative** swing; good trading and army preservation show a **high positive**
swing. This is precisely the behavior decision #3 (score-floating axes, spread
instead of death-ball) is meant to produce, so it's the scenario that will tell
us whether the spread offense is *paying off* rather than merely *existing*.

**Note on the margin form:** because a net-swing difference can be small in
absolute terms yet meaningful, this scenario uses an **additive absolute margin**
(one unit's cost) rather than a percentage — a percentage margin on a
near-zero-or-negative control baseline is ill-defined. If the Normal control's
swing is reliably positive and large, the manager may switch to a percentage
margin and record the change (SPEC §6.3).

**Anti-degenerate guard:** a passive AI that never fights also has a ~0 swing and
could "tie" a cautious control. Pair the pass with a **minimum-engagement floor**:
require `median(v2 kills_cost + deaths_cost) > 0` (i.e. combat actually happened)
so "efficiency" isn't achieved by refusing to fight. If both AIs turtle to a
non-engagement, the scenario is invalid → re-scope (shorter map, forced contact)
rather than score it.

> **SCENARIO LIVE + CALIBRATE VERDICT (2026-07-20, `main @ 21510e05`).** S2 scenario
> set built (`tournament-s2-combat-river-zeta` + `-mirror` bot-swap twin + `-cal-nn`);
> byte-identical River Zeta map as S1, 720s combat clock, scorer/win-rule frozen (no S1
> re-BASELINE). **Normal-vs-Normal CALIBRATE (N=10, hidden Mode-B, all 18000t/time_limit,
> 0 crashes)** — analysis [`runs/260720_s2_calibrate_nn.md`](runs/260720_s2_calibrate_nn.md):
> - **MIN-ENGAGEMENT: PASS → GO for Normal.** Engagement-volume median 7475/5950,
>   deaths_cost median 5725/4400, units dying every match both sides — Normal fights hard at
>   720s. **Opponent = `@normal` confirmed; the `@rush` fallback is NOT needed.**
> - **SIDE LEAN: moderate russia/80,35** (win split **7-3**, score median 4525 vs 2400,
>   net-swing median -2400 vs -3575) — stronger than S1's economy lean. **Mirror policy: mandatory
>   5 primary + 5 mirror, and the S2 pass must hold from BOTH spawns** (≥3/5 each).
> - **BAR (proposed, pending ratification):** the data shows **both** sides net-swing-**negative**
>   (structural attrition offset: `deaths_cost` counts all losses, `kills_cost` only enemy kills), so
>   the *absolute* "≥ +$1,400" form is biased against passing. **Use the PAIRED-RELATIVE bar instead:
>   `median(Exp net swing) ≥ median(Normal net swing) + $1,400`** (one IFV) on the same deterministic
>   seed set (cancels the shared offset), + ≥7/10 positive-delta sign robustness + both-spawn symmetry.
>   Flagged for user ratification (DOCTRINE.md:26); loop proceeds on it.
> - **S3 watch:** the 7-3 combat lean sits outside S3's 0.40–0.60 win-rate band — S3 must lean on
>   the mirror or a larger-N win-rate calibration before a win-rate is trusted.
> First Experimental-vs-Normal S2 batch is the next S2 step (not run here).

> **S2 MEASUREMENT RESULT (2026-07-20, `main @ 1594ffa1`) — BAR PASS; dispersion causal credit NEGATIVE.**
> First Experimental-vs-Normal S2 batch (N=10, 5 primary + 5 mirror, hidden Mode-B, all 18000t/time_limit,
> 0 crashes, verdict v5) + the deferred dispersion ON/OFF A/B on the identical seed set. Analysis
> [`runs/260720_s2_exp_vs_normal_n10.md`](runs/260720_s2_exp_vs_normal_n10.md).
> - **PAIRED-RELATIVE BAR: PASS.** median Exp swing **-200** ≥ median Normal swing **-5050** + $1,400
>   → **relative edge +$4,850** (margin +$3,450 over bar); **sign-delta 8/10** (Exp swing > Normal swing,
>   ≥7 ✅); **both-spawn 5/5 primary + 3/5 mirror** (≥3/5 each ✅); **min-engagement PASS** (Exp eng 5725
>   in the NN band 5950–7475 — not winning by avoiding combat). **Win split 10–0.** Watch cells: mirror
>   seeds 1017 (-500) / 3017 (-4850), both exp=russia (the only negative deltas).
> - **DISPERSION A/B (diagnostic, not the bar): cohesion does NOT improve combat economy.** ON vs OFF on
>   identical seeds: **median paired delta (ON−OFF) = −$1,500** (positive on only 5/10), median Exp swing
>   **ON -200 vs OFF +2325**, engagement volume **ON 5725 vs OFF 7125** (~20% less). Win split **ON 10-0 vs
>   OFF 9-1** — cohesion's sole gain is flipping seed 8017 (+$7,300, loss→win) at high variance. **The
>   force-efficiency payoff the dispersion VERIFY deferred comes back NEGATIVE**; dispersion's value (if any)
>   is decisiveness (S3), not exchange efficiency. Advancement unaffected — S2 passes with dispersion ON,
>   and would pass more strongly with it off. Grades the doctrine *as tuned* (`AssaultRadiusCells: 15`,
>   `ApproachCohesion: Spread`). Toggle was `@experimental`-only + reverted (never committed).
> - **NEXT:** recommended combat-focused **dispersion re-tune** cycle (S2 now gives it a causal score to
>   move, currently negative); or the parked SR-contestation cycle for ladder breadth.

> **S2 SR-CONTESTATION RESULT (2026-07-20, merged `exp-sr-contestation` → main) — BAR PASS, stronger than
> the pre-change reference.** `SrPressureScoreMultiplier: 260` on `@experimental` only. N=10 (5 primary +
> 5 mirror, hidden Mode-B, all 18000t/time_limit, 0 crashes, v5). **All four gates PASS:** median Exp swing
> **+1125** ≥ median Normal **-5175** + $1,400 → **relative edge +$6,300** (margin +$4,900); **sign-delta
> 8/10** (≥7); **both-spawn 4/5 primary + 4/5 mirror** (≥3/5 each); **min-engagement PASS** (Exp eng median
> 6025 inside NN band 5950–7475). **Win split 10–0.** vs the dispersion-ON reference (`1594ffa1`, median Exp
> swing **-200**, edge +$4,850): SR contestation **lifts median Exp swing to +1125** (+$1,325, Normal control
> stable -5050→-5175) and rescues the reference's worst cell (seed 3017 delta −$4,850 → **+$7,650**). On the
> same S2 metric where dispersion's causal credit came back **negative**, SR contestation is **positive**.
> SR Pressure axes live in-window 8/10 matches (89–180 lines each, sustained over 720s). Watch cells:
> negative deltas only on 1017 (-1150) / 4017 (-250), both small. Analysis:
> [`runs/260720_sr_contestation_cycle1_n10.md`](runs/260720_sr_contestation_cycle1_n10.md).

---

## Scenario 3 — Decisive Outcome / Win-rate (proposed)

**Question:** the bottom line — across many games, does the Experimental AI
*actually win* more than the Normal control on even terms?

| Field | Value |
|---|---|
| Scenario | River Zeta rung, scenario TBD at S3 standup (reuse the S2 `tournament-s2-combat-river-zeta` map + 720s clock, or a dedicated `tournament-s3-*-river-zeta`), + mirror (essential here) |
| **Regime (2026-07-21)** | Same new regime as S1/S2 when S3 stands up: **Motorized start, same faction US-US, opponent = `@stable`**. The map-bias gate below becomes **Stable-vs-Stable ~0.50** (not Normal-vs-Normal). |
| Contestants | P1 Experimental (`@experimental`) vs P2 **Stable** (`@stable`, frozen control) — *was* P2 Normal `[pre-regime]` |
| Match length | **12 minutes** — `TimeLimitSeconds: 720` (the committed default config) |
| **Metric** | **Experimental win-rate** (fraction of matches where `winner_name` is the Experimental player), from `summary.json` |
| N runs | **20** (a win-rate needs statistical power; 20 is the harness's canonical sanity size) |
| Seeds | `1017 … 20017`; even primary, odd mirror — **mandatory**, so win-rate isn't spawn-side artifact |
| Advancement | **win-rate ≥ 0.55** (beats the 50/50 even-match null by a 5-point margin), AND **Stable-vs-Stable** on this scenario is verified ~0.50 (map isn't biased) — `[pre-regime]` map-bias findings do not carry over |
| WinRule | `score_or_sr_capture` (the committed `tournament.yaml` — score at 720s, or instant win on SR capture) |

**Why a win-rate and why 0.55:** scenarios 1–2 measure *facets*; this measures
the *whole*. 0.55 is deliberately modest — the user explicitly accepts early
passive/suboptimal games (POI plan decision #3), so the first bar is "detectably
better than the control," not "dominant." Raise it (0.60, 0.65…) as the AI
improves; that tightening is itself a valid advancement lever (SPEC §6.3, §11).

**Bias control is mandatory here** because a win-rate is the metric most easily
corrupted by spawn/faction asymmetry. Always run half the seeds mirrored, and
**gate the whole scenario** on a Normal-vs-Normal baseline landing in the 0.40–0.60
band (POI plan / harness plan §1.5). If Normal-vs-Normal is skewed, fix the map
or the matchup before trusting any Experimental win-rate on it.

**Handicap escalation (future):** once v2 clears 0.55 comfortably, keep the
scenario discriminating with a **handicap variant** (SPEC §6.3) — e.g.
Experimental with a reduced reserve budget — rather than only ratcheting the
win-rate threshold. A handicapped win is stronger evidence than a bigger margin
on an even start.

---

## The composite gate (clearing the River Zeta rung)

Scenarios 1–3 are passed *individually* during normal cycling (each merges its
improvements early, SPEC §5.2). But a rung is **cleared** only by the combine
step the user described:

> **One single commit passes Scenario 1 AND Scenario 2 AND Scenario 3, all
> re-verified in one sitting on that same build.**

This is the no-cherry-picking guarantee: it forbids clearing the rung with three
different commits that each win one facet while quietly regressing another
(SPEC §6.2, §6.4). Procedure:

1. Take the current `main` (or the candidate worktree build).
2. Run all three scenarios at their defined N + seeds.
3. All three must pass their advancement criteria **simultaneously**.
4. Pass → log a `LADDER` milestone in REVIEW.md ("River Zeta rung CLEARED @
   <sha>"), ping the user, and advance:
   - add a **second map** rung and re-run these three scenarios there (anti-overfit,
     SPEC §10), and/or
   - **tighten** the margins / raise the win-rate / introduce a **handicap**
     variant on River Zeta, recording the new bars here.

Until the composite gate passes, River Zeta is the **active rung** and all three
scenarios stay live.

---

## Scenario registry (files)

| Scenario | Folder | Config for this ladder |
|---|---|---|
| S1 Economy Race | `tools/autotest/scenarios/tournament-s1-eco-river-zeta/` | `tournament-eco-5min.yaml` (`TimeLimitSeconds: 300`, `SpeedMultiplier: 8`). **Regime 2026-07-21:** Motorized start both sides, same faction US-US (both `america`), **Exp vs `@stable`**. |
| S2 Force Efficiency | `tools/autotest/scenarios/tournament-s2-combat-river-zeta/` | **BUILT (2026-07-20)** — byte-identical River Zeta map as S1 (98×82, 12 OILB derricks), `tournament-combat-12min.yaml` (`TimeLimitSeconds: 720`, `SpeedMultiplier: 8`); scorer/win-rule frozen identical to S1. **Regime 2026-07-21:** Motorized start, same faction US-US, **Exp vs `@stable`**. |
| S3 Win-rate | River Zeta rung — scenario TBD at S3 standup (reuse S2's map + 720s config, or a dedicated `tournament-s3-*-river-zeta`) | `tournament-combat-12min.yaml` (`720s`). At standup: same new regime (Motorized / US-US / **Exp vs `@stable`**). |
| S1 bias twin | `tools/autotest/scenarios/tournament-s1-eco-river-zeta-mirror/` | **BUILT (2026-07-20; regime 2026-07-21)** — spawn assignments SWAPPED (`@stable` on USA/14,45, Experimental on Russia/80,35). With both sides now `america` this is a **pure spawn swap**. Same `tournament-eco-5min.yaml`. |
| S1 calibration (Stable-vs-Stable) | `tools/autotest/scenarios/tournament-s1-eco-cal-nn/` | **BUILT 2026-07-20; regime 2026-07-21 → now Stable-vs-Stable** — both bots `@stable`, Motorized start, same faction. Identical bots make the mirror swap a no-op, so a single N batch measures pure spawn/side bias under the new regime. `[pre-regime]` `f8052ec` N-vs-N result superseded — re-run. |
| S2 bias twin | `tools/autotest/scenarios/tournament-s2-combat-river-zeta-mirror/` | **BUILT (2026-07-20; regime 2026-07-21)** — spawn assignments SWAPPED. Previously bot-swap = spawn+faction; with same-faction US-US it is now a **pure spawn swap**. Same `tournament-combat-12min.yaml`. |
| S2 calibration (Stable-vs-Stable) | `tools/autotest/scenarios/tournament-s2-combat-river-zeta-cal-nn/` | **BUILT 2026-07-20; regime 2026-07-21 → now Stable-vs-Stable** — both bots `@stable`, Motorized start, same faction. Single N batch measures side/faction bias + min-engagement at the 720s clock (re-run CALIBRATE; the `[pre-regime]` N-vs-N go/no-go on Normal is superseded). |
| S1 sanity floor (Exp-vs-Normal) | `tools/autotest/scenarios/tournament-s1-eco-floor-vs-normal/` | **NEW 2026-07-21** — copy of the S1 primary under the new regime (Motorized / same-faction US-US) but with **P2 = `@normal`** instead of `@stable`. The single retained Exp-vs-Normal scenario: a periodic "has Experimental regressed below the frozen control?" floor check. **NOT** part of the composite gate. Same `tournament-eco-5min.yaml`. |

S1 now uses a **River-Zeta-derived** scenario (`tournament-s1-eco-river-zeta`):
its `map.yaml` keeps the full River Zeta terrain + all 12 neutral OILB derricks
and overlays the harness's 2 SRs + 2 bot spawns; `rules.yaml` and
`tournament-eco-5min.yaml` are byte-identical to the `tournament-v2-vs-normal-2p`
originals. This is a **harness** change (allowed, SPEC §4.1) — map/scenario only,
no unit stat / balance / engine edit (derrick income stays `$50`). **S2 now uses a
River-Zeta-derived combat scenario too** (`tournament-s2-combat-river-zeta`, a
byte-identical copy of the S1 map with a 720s clock) — the old 66×34
`tournament-experimental-vs-normal-2p` combat stub is **retired from the ladder** so
all three facets sit on one map (the rung/composite-gate model). S3 will reuse the
same River Zeta rung at standup.
