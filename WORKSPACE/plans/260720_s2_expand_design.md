# S2 EXPAND — Combat-Quality Ladder Rung (design recon)

**Date:** 2026-07-20
**Mode:** READ-ONLY design recon. No engine/YAML edits, no builds, no game/test runs (a batch holds the run slot machine-wide).
**Grounded against:** `main @ e5a1c967` (local main is 96 commits ahead of `origin/main` — unpushed, expected). Every code claim below cites a file:line read at this SHA; if a cite drifts, trust the code and fix the cite.
**Action type:** EXPAND (DOCTRINE.md:16) — add the next ladder rung because the top rung (S1 economy) has cleared its bar and behaviors (dispersion/cohesion) have outgrown what it measures.
**Builds on:** LADDER.md §Scenario 2 (the existing draft), `runs/260720_dispersion_verify_n10.md` (the confounded-signal problem this rung exists to fix), `plans/260720_seeded_determinism.md` + commits `2d3c8fe0`/`f3a61d9d` (determinism now shipped — the statistical model changes), `plans/260720_mission_abstraction_costing.md` (structural option).

---

## 0. TL;DR

- **S1's blind spot is combat quality.** It scores `capture_income_gross` (BotVsBotMatchWatcher.cs:361) — a pure economy signal. The merged dispersion doctrine (spread-to-move / mass-to-assault) is a *combat-survival* mechanic whose value **could not be causally credited** on S1 (the 4/10→9/10 capture jump was confounded, `runs/260720_dispersion_verify_n10.md:101-121`). S2 exists to expose a combat-quality signal.
- **The primary metric needs ZERO engine change.** Net combat swing = `kills_cost − deaths_cost` is already emitted per player (BotVsBotMatchWatcher.cs:352-353). Read post-hoc per LADDER's metric-extraction model (LADDER.md:38-45) → no scorer change → **no re-BASELINE of S1** (DOCTRINE.md:11). This is the cheap, doctrine-aligned path.
- **Determinism reframes the statistics.** `LocalRandom` is now seeded (World.cs:213 + the seeding block at :214-onward; verdict stamps `seed`, BotVsBotMatchWatcher.cs:305-306). The fixed per-index seed set (`i*1000+17`, run-tournament.sh:282) makes cross-cycle and control-vs-experimental comparisons **paired on identical worlds** — a large power win, and it finally enables a clean **dispersion-on/off A/B** that S1 could not do.
- **Recommended scenario: keep the River Zeta rung.** Reuse the exact S1 map (`tournament-s1-eco-river-zeta`'s terrain + 12 OILB derricks) with a 12-minute combat clock. S1/S2/S3 then differ only by *clock + metric*, not map — which is what makes the "one rung = one map, composite gate" model (LADDER.md:33-36, §6.4) actually coherent. The mid-map derricks are the contested ground that forces the fight.
- **Recommended opponent: Normal primary, with a Rush fallback flagged.** Normal keeps continuity with S1/S3 and is the frozen yardstick, but it may under-fight (it lost S1 10-0 while capturing 0). CALIBRATE must confirm Normal generates real engagement; if it gets steamrolled without inflicting losses, the metric stops discriminating trade-quality and we switch the S2 control to `@rush` (also frozen, SPEC §4.2).
- **Bar (to ratify):** median Experimental net swing ≥ **+$1,400** (one IFV: bradley $1,500 / bmp2 $1,300, vehicles-america.yaml:320 / vehicles-russia.yaml:155), over the standard N=10 paired seed set (5 primary + 5 mirror), gated by a min-engagement floor. Ratify via question per DOCTRINE.md:26.

---

## 1. What S2 must measure that S1 cannot

### 1.1 S1 is economy-only, by construction

S1's ladder metric is `capture_income_gross` (LADDER.md:70; emitted at BotVsBotMatchWatcher.cs:361 from the read-only `GrossIncomeIntegrator`, MatchTypes.cs:75-92). Its scorer economy term also reads that gross integral (WeightedComponentMatchScorer.cs:68, :85). Nothing in S1's *pass criterion* reads a combat field. The control captures ≈0 (LADDER.md:145-148), so S1 measures whether Experimental converts POIs into held income — a supply/availability problem the TECN-floor cycle solved (LADDER.md:172-187).

### 1.2 The specific gap: dispersion/cohesion has no home

The dispersion doctrine (`SetCohesion` distance-gated in `PoiOffensiveBotModule.CommitAndOrder`, `plans/260720_dispersion_cycle_design.md:116-167`) is **spread-to-move, mass-to-assault** — its entire value is *losing fewer units crossing open ground, then winning the fight at the objective*. That is a **force-preservation-on-approach** signal. S1 cannot see it:

- The dispersion VERIFY explicitly says so: *"S1 is an economy scenario and cannot exhibit dispersion's real value … dispersion's real combat signal awaits an S2-class (Force Efficiency) rung"* (`runs/260720_dispersion_verify_n10.md:13-18, 131-135`).
- Its capture-rate jump was **confounded** with the cycle-1 TECN-pipeline merge and unseeded variance, and was *not credited* to dispersion (`runs/260720_dispersion_verify_n10.md:101-121`).

So S2's job, stated precisely: **expose a combat-quality signal that (a) discriminates massing vs death-balling, (b) is sensitive to force preservation, and (c) can causally credit a cohesion/engagement behavior change** rather than merely confirm it fired.

### 1.3 The three facets S2 must capture

| Facet | Why it matters for combat quality | Cheapest signal |
|---|---|---|
| **Exchange efficiency** | "Trade better" — destroy more value than you lose, don't death-ball into a bad fight | `kills_cost − deaths_cost` (net swing) — **already emitted**, BotVsBotMatchWatcher.cs:352-353 |
| **Force preservation** | Dispersion's specific payoff: fewer losses getting into position | `units_dead` / `deaths_cost` end-state (already emitted, :349,:353) + optional time-integrated `army_value` (§3.2) |
| **Engagement actually happened** | Guard against "efficiency by refusing to fight" (a passive AI also has ~0 swing) | `kills_cost + deaths_cost > 0` floor — already available (LADDER.md:263-268) |

The key realization: **the primary combat signal is already in the verdict.** S1's own metric-extraction rule (LADDER.md:38-45) — "read the metric post-hoc from the verdict JSON, no new engine scorer" — applies directly. S2 is therefore *cheap to stand up* (a map/config + a doc), and its optional richer signals (§3) stay additive observer stats, never scorer inputs, so they never trigger a re-BASELINE.

---

## 2. Scenario definition

### 2.1 Map — reuse the River Zeta rung (recommended)

**Recommendation: S2 runs on the same River Zeta terrain as S1** — a new scenario `tournament-s2-combat-river-zeta` whose `map.yaml` is a byte-copy of `tournament-s1-eco-river-zeta/map.yaml` (98×82, all 12 neutral OILB derricks, SRs at USA `14,45` / Russia `80,35`, map.yaml:54-67 players, :14056-14068 SR+spawn overlay), paired with a `-mirror` twin and a `-cal-nn` calibration copy — exactly the proven S1 pattern (LADDER.md:343-344).

**Why the same map, not the 66×34 combat stub the draft LADDER currently names (LADDER.md:238, :341-342):**

1. **The rung model requires it.** LADDER.md:33-36 defines a rung as *one map* holding three facet-scenarios; the composite gate (§6.4, LADDER.md:308-332) clears a rung only when *one commit passes all three on that map*. S2/S3 sitting on a *different* map than S1 makes "clear the River Zeta rung" incoherent. Putting all three on River Zeta means S1/S2/S3 differ only by **clock + metric**, which is what the rung concept wants.
2. **The stub has no contested ground.** The 66×34 stub has **zero capturables** (verified: `grep -c oilb|Capturable` on `tournament-experimental-vs-normal-2p/map.yaml` = 0) and is a bare inline map — the same construction that pinned S1's economy metric to 0/0 before the rescope (LADDER.md:76-93). It forces contact by being tiny, but gives the armies nothing to *contest*, so there is no "cross open ground to reach the objective" phase — which is exactly the phase dispersion is built for.
3. **River Zeta's mid derricks are the contested middle.** Derrick actor locations (map.yaml, `oilb` actors): `38,53` and `56,26` straddle map-center (~`49,41`); `44,0`, `51,79`, `25,22`, `74,58` ring the mid-field; the two near-SR derricks (`17,44`, `76,35`) are the safe economy pair. The central cluster is uncommitted ground both armies must cross and fight over — a natural **contested-mid** combat generator without inventing a new map. This directly realizes LADDER's own "S2 contested-mid" follow-up hypothesis, on real terrain.
4. **Fairness is already characterized.** S1's CALIBRATE found River Zeta *mostly side-fair with a mild russia/`80,35` lean, neutralised by the mandatory mirror* (LADDER.md:136-139, REVIEW.md:67). S2 inherits that knowledge (though a combat metric needs its own CALIBRATE, §5 — combat asymmetry ≠ economy asymmetry).

**Considered and rejected — a bespoke combat map** (e.g. `seventh-woods-ww3` 123×114, `siberian-pass-ww3` 97×67, or a stripped central-objective arena). Rejected for now: it fragments the rung (anti-overfit map rotation is a *post-clear* lever, SPEC §10 / LADDER.md:325-329), and it discards S1's fairness calibration. **Revisit at the second-map anti-overfit step** once the River Zeta rung is cleared.

### 2.2 Spawn / SR layout

Unchanged from S1: two home SRs (indestructible, `supplyroute`; supply-route.md:12) at the two designer-vetted start locations, each with a near-SR derrick and a shared contested middle. No neutral SR is added (SR capture is unimplemented — supply-route.md:65-72 — so a mid SR would be inert; that belongs to the S3 "SR-pressure" hypothesis via *contestation*, not capture). Units arrive from the map edge nearest each SR and march to the rally (game-model.md:22-27), so the approach-across-ground phase — dispersion's domain — is intrinsic.

### 2.3 Tick window

**12 minutes — `TimeLimitSeconds: 720`** (matches the LADDER S2 draft, LADDER.md:240; `TimeLimitTicks = 720×25 = 18000`, TournamentConfig.cs:101). Rationale: S1's 300s isolates *early* economy before attrition; S2 wants *sustained* contact so exchange efficiency and force preservation dominate. At `SpeedMultiplier: 8` (BotVsBotMatchWatcher.cs:152-158) a 720s match is ≈1.5–2 wall-min hidden — cheap enough for N=10. Determinism means a fixed seed reaches natural `time_limit` end reproducibly *provided the wall-clock watchdog doesn't kill it first* — set a generous `--max-wall-secs` for S2 (the watchdog is wall-clock, `plans/260720_seeded_determinism.md:153-158`).

### 2.4 Bot pairings

- **Primary control: `@normal`** (Experimental P1 / Normal P2, as S1). Keeps the yardstick continuous across S1/S2/S3 and honors the frozen-control invariant (SPEC §4.2, §11).
- **Mirror: faction/side-swapped twin** `tournament-s2-combat-river-zeta-mirror` — essential here because combat is faction-asymmetric (america bradley $1,500/abrams $2,500 vs russia bmp2 $1,300/t90 $2,400 — vehicles-america.yaml:320,477 / vehicles-russia.yaml:155,312), so a raw net-swing gap could be unit-roster luck. Even seeds primary, odd mirror (run-tournament.sh:253-255, SPEC §9.4).
- **Flagged alternative: `@rush` control.** Normal may under-fight — on S1 it captured 0/10 and lost 10-0 (LADDER.md:145). If a control gets steamrolled *without inflicting losses*, Experimental's net swing is huge-positive regardless of *trade quality* (a death-ball and a dispersed force both win big), and the metric stops discriminating dispersion. `@rush` forces early sustained contact and punishes bad approaches hardest — the strongest stress on force-preservation. **This is the single biggest open scenario question** (§7). Recommendation: run CALIBRATE + a pilot with Normal; if the min-engagement/competitiveness check (§5) shows Normal underfighting, switch S2's control to `@rush`. `@turtle` is a weaker third option (it stresses mass-to-assault against prepared positions but produces long low-tempo matches).

---

## 3. Scorer components

### 3.1 What already suffices (no engine change, no re-BASELINE)

The verdict `stats` block already carries every field the primary S2 metric and its guards need (BotVsBotMatchWatcher.cs:349-358):

| Field | Line | S2 use |
|---|---|---|
| `kills_cost` | :352 | net swing minuend; also engagement floor |
| `deaths_cost` | :353 | net swing subtrahend; force-loss proxy |
| `units_killed` / `units_dead` | :349-350 | unit-count exchange sanity; passivity check |
| `army_value` | :354 | end-state surviving force (preservation snapshot) |
| `assets_value` | :355 | includes buildings; secondary |

**Primary metric = `kills_cost − deaths_cost`, read post-hoc**, exactly as S1 reads `capture_income_gross` post-hoc (LADDER.md:38-45). Because the **scorer and win rule are untouched**, S1's baseline is not invalidated — the DOCTRINE re-BASELINE trigger ("after any scorer/map/win-rule change", DOCTRINE.md:11) does **not** fire. This is the decisive reason to keep S2's signal post-hoc rather than a scorer component.

Note on the near-zero-sum property: in a 2-player head-to-head, one player's `kills_cost` is (modulo neutrals/self-destructs) the other's `deaths_cost`, so Experimental's net swing ≈ −(Normal's). The bar "Exp median ≥ control median + margin" (LADDER.md:244) therefore reduces to roughly "Exp net swing ≥ +margin/2" — i.e. **Experimental trades favorably against the control**. That is the intended force-efficiency reading; the additive margin (LADDER.md:256-261) guards the near-tie.

### 3.2 New components worth adding — additive observer stats only (each costed)

All of these follow the `GrossIncomeIntegrator` precedent (MatchTypes.cs:75-92): a read-only per-player accumulator ticked from `AccumulateGrossIncome`'s sibling hook (BotVsBotMatchWatcher.cs:272-288), emitted as a **new `stats` field** — never a scorer/win-rule input. Additive stat fields do **not** change match outcomes, so (like `capture_income_gross` at v3, header note BotVsBotMatchWatcher.cs:41-45) they need **no re-BASELINE**.

| # | Component | What it measures | Mechanism | LOC | Re-BASELINE? |
|---|---|---|---|---|---|
| C1 | **Time-integrated army value** (`army_value_integral`) | Sustained force over the match, not just the endpoint — rewards *not losing units on approach*, dispersion's exact payoff | New `ArmyValueIntegrator` (clone of `GrossIncomeIntegrator`, MatchTypes.cs:75-92) ticked from `PlayerStatistics.ArmyValue`; emit one field in `SerializeVerdict` (BotVsBotMatchWatcher.cs:347-361); bump `verdict_version` 5→6 (additive) | ~25 (1 integrator + 1 hook line + 1 emit line) | **No** (additive stat, not scorer input) |
| C2 | **Losses-on-approach vs at-objective** | Directly credits dispersion: were deaths taken crossing open ground or at the assault? | `INotifyKilled` hook classifying each death's cell by distance to nearest enemy structure / contested derrick; emit two counters | ~50 (event hook + spatial classify + 2 fields) | **No** (additive) — but more invasive; **defer to a dispersion-A/B cycle** |
| C3 | **Mid-control integral** (`mid_control_ticks`) | Territory proxy — who holds the contested middle | Per-tick count of a player's combat units within R cells of map-center (or the central derrick cluster), integrated | ~35 (new integrator + center const + emit) | **No** (additive) |
| C4 | **Mean pairwise spacing telemetry** | *Activation* of dispersion (is it dispersing?), not outcome | Needs `PoiOffensiveBotModule.GetActiveAxes()` (NOT yet in engine — costed in `plans/260720_dispersion_cycle_design.md:254-270`) + watcher-side N²/2 compute | ~30 across 2 files | **No** (diagnostic log, not a metric) |

**Recommendation:** ship S2 with **only the existing fields (§3.1)** for the first cut — it is enough for the pass bar and costs nothing. Add **C1 (`army_value_integral`)** in the *same* or a fast follow scenario cycle because it is the cheapest signal that specifically rewards force preservation over time (the endpoint `army_value` alone misses a force that was preserved then spent decisively at t=700). Treat **C2** as the instrument for a dedicated **dispersion-on/off A/B** cycle (§4.3) — it is the field that would let a future cycle *causally* attribute a loss reduction to spreading. **C3/C4** are optional richness; add only if the pass bar proves non-discriminating.

### 3.3 Explicitly do NOT change the scorer/win rule for S2

The `WeightedComponentMatchScorer` (WeightedComponentScoring.Compute, WeightedComponentMatchScorer.cs:81-94) and `TimeOrSrCaptureWinRule` (TimeOrSrCaptureWinRule.cs:85-99) decide only the *in-match winner* (which S3's win-rate reads). S2's metric is post-hoc, so the winner is irrelevant to S2. Re-weighting kills into the score (tempting for a "combat" scenario) would (a) change S3's outcomes and (b) force a re-BASELINE (DOCTRINE.md:11) — **cost with no S2 benefit.** Keep it frozen. If a future cycle *does* want combat to drive the win rule, cost the re-BASELINE explicitly then.

---

## 4. Pass bar proposal (to ratify)

Per DOCTRINE.md:26, the bar is a recommendation flagged for user ratification via question; the loop proceeds on the recommendation.

### 4.1 Statistical model — paired, exploiting determinism

Determinism (World.cs:213-214 seeding; verdict `seed`, BotVsBotMatchWatcher.cs:305-306) changes the model from S1's "independent samples" to **paired**:

- **Fixed seed set** `{1017, 2017, … 10017}` (run-tournament.sh:282), even = primary, odd = mirror. Every cycle reuses this exact set → same battlefields → cross-cycle deltas are paired (huge variance reduction vs the old 4/10-vs-9/10 wobble, REVIEW.md:55).
- **Anti-overfit caveat (carry from the determinism finding):** do not tune behaviors to the fixed 10 — rotate/expand the seed set at BASELINE if a behavior only wins on the standard set (REVIEW.md:55, activity log).

### 4.2 Recommended bar

Over the standard **N=10** (5 primary + 5 mirror):

1. **Primary (force efficiency):** `median(Experimental net swing) ≥ +$1,400` — an **additive absolute margin** of one IFV-class unit (bradley $1,500 / bmp2 $1,300 → ~$1,400 faction-mean; vehicles-america.yaml:320, vehicles-russia.yaml:155). Additive (not %) because a control net swing near 0 makes a percentage margin ill-defined (LADDER.md:256-261).
2. **Min-engagement floor (anti-degenerate):** `median(Experimental kills_cost + deaths_cost) > 0`, and more strongly `median(control deaths_cost) ≥ one IFV` — i.e. the control actually *lost* real value in a fight. If the control isn't dying, the match wasn't a contest → invalid sample, re-scope opponent (→ `@rush`) rather than score it (LADDER.md:263-268).
3. **Sign robustness:** Experimental net swing positive on **≥ 7/10** seeds (a paired sign test — cheap power from determinism), so the median isn't carried by one blowout.
4. **Mirror symmetry:** the edge must hold from *both* spawns (as S1 required, LADDER.md:145) — Experimental net-swing-positive on ≥3/5 primary AND ≥3/5 mirror. Guards against reading a faction-roster advantage as skill.

N=10 (not the draft's N=15, LADDER.md:242) because paired seeds cut variance and reuse the S1 set; bump to N=20 only if §5's CALIBRATE shows the control net swing noisy run-to-run.

### 4.3 The determinism dividend — a clean dispersion A/B (diagnostic, not the bar)

Because a fixed seed now reproduces a whole match, S2 unlocks what S1 could not: run Experimental with `CohesionSwitchEnabled: false` vs `true` (the kill-switch, `PoiOffensiveBotModule.cs:87` default false per `runs/260720_dispersion_verify_n10.md:22-24`) **on the identical seed set**, and the per-seed *delta* in net swing (and, with C2, in losses-on-approach) is the **causal dispersion effect** — the credit the dispersion VERIFY explicitly deferred (`runs/260720_dispersion_verify_n10.md:118-121`). This is a *diagnostic cycle* S2 makes possible, not part of the advancement bar (advancement stays Experimental-vs-control per the rung model).

---

## 5. CALIBRATE plan (control-vs-control fairness)

Per DOCTRINE.md:12 (CALIBRATE fires on any new scenario) and SPEC §9.4, before any Experimental S2 number is trusted:

1. **Build** `tournament-s2-combat-river-zeta-cal-nn` — byte-identical to the S2 primary with the USA-bot `Bot:` line `experimental → normal` (both `@normal`), exactly the S1 cal-nn pattern (LADDER.md:344, REVIEW.md:67). With identical bots the mirror swap is a no-op, so a single N=10 batch measures pure side/faction bias.
2. **Measure, on the 720s combat config:**
   - **Side/faction fairness:** is `net swing` ≈ 0 and symmetric (neither spawn nor faction has a combat edge)? A large non-zero control-vs-control swing means the *map or roster* favors one side in a fight — must be understood (and neutralized by the mirror) before trusting Experimental.
   - **Min-engagement:** does Normal-vs-Normal actually fight (`kills_cost + deaths_cost` well above 0, `units_dead` > 0 both sides)? **This is the go/no-go on the Normal opponent** (§2.4). If Normal-vs-Normal is a low-contact stalemate, Normal will also under-fight Experimental → switch S2's control to `@rush` and re-CALIBRATE.
   - **Win-rate sanity (feeds S3 too):** Normal-vs-Normal win split in the 0.40–0.60 band (LADDER.md:296-298).
3. **Output:** a fairness verdict in `runs/` + the ratified mirror policy, before S2's first Experimental-vs-control batch (DOCTRINE.md:12).

CALIBRATE must run at 720s — economy fairness (S1's cal-nn) does not transfer to combat fairness.

---

## 6. Structural option (aim-high mandate, DOCTRINE.md:25)

**Does S2 argue for the missions/operations-layer roadmap — and what would S2 measure differently if it landed?**

### 6.1 The link: S2's metric is the operations layer's report card

The mission-abstraction costing (`plans/260720_mission_abstraction_costing.md`) diagnoses the experimental AI's core structural weakness as *"goals but no operations"* (§1.1, :26-32): each bot module knows *what* it wants this tick but nothing owns an *attempt over time* — no staging, no massing-before-commit, no retry, penny-packet commitment (§1.4, :63-67). The doctrine north-star is *"disperse under observation and mass only at the decisive point"* (DOCS/design/ai-realism.md:22-23). **S2's force-preservation + exchange-efficiency metric is precisely the yardstick for whether an operations layer works:** a `Mission` with an explicit `Staging → Executing` lifecycle (mission_abstraction_costing.md:111-128) that masses a force to `DesiredSize` before launching should, if the doctrine is right, *reduce deaths-on-approach and raise net swing* — which is exactly what S2 measures. Without S2, the operations layer would land as blind debt paydown (its §4.2 warning: step 2 "buys zero ladder points on its own"). **S2 is the rung that would give the operations layer a score to move.**

### 6.2 What S2 would measure differently if the operations layer landed

- **BUILDUP → PROBE → OFFENSIVE phases** would show up as a **shape change in the C1 army-value integral** (§3.2): a phased bot preserves force during BUILDUP/PROBE (high early integral) then spends decisively (a late dip with a matching enemy-loss spike) — distinguishable from a trickle-attacker's flat, slowly-bleeding curve. S2's endpoint `kills_cost − deaths_cost` alone can't see the *timing*; the integral + C2 (losses-on-approach) can.
- **Capture/assault as first-class missions** would let C2 attribute losses to a *named mission's* approach vs assault, turning S2 from "did it trade well" into "which operation traded well" — the seed of telemetry-driven diagnosis (mission_abstraction_costing.md:128, roadmap item 6).
- **Staging (mass-before-commit)** is the single behavior S2 is shaped to reward and S1 is blind to. If step-1 CaptureMission or the offense generator/executor split (mission_abstraction_costing.md:149-156) adds a "wait for min force, then go" gate, S2's net-swing + preservation signal is how we'd know it paid off.

### 6.3 Recommendation

**S2 does argue for the operations layer — as its measurement substrate, not its trigger.** Per the mission costing's own decision rule (§4.3, :226-233): build **step 1 (CaptureMission, kill-switch-gated) opportunistically** when a capture cycle needs retry/escort-lifecycle anyway; **defer the full offense/garrison unification (step 2) until S2 forces a new mission type** — and S2's contested-mid combat is plausibly that forcing function (a "screen the mid derrick" / "mass-then-assault" mission is the natural step-2 driver, :220). Sequence: **stand up S2 (cheap, post-hoc metric) → get a combat baseline → then let S2's signal justify and grade the operations layer.** Do not gate S2 on the operations layer; gate the operations layer's *credit* on S2.

---

## 7. Risks + open questions

| ID | Risk / question | Impact | Mitigation / recommendation |
|---|---|---|---|
| Q-1 | **Does Normal fight hard enough to discriminate trade quality?** (biggest open question) | If Normal gets steamrolled without inflicting losses, net swing is blowout-positive regardless of dispersion → metric doesn't discriminate | CALIBRATE min-engagement check (§5) is the go/no-go; switch S2 control to `@rush` if Normal underfights. **Ratify opponent via question.** |
| Q-2 | **Bar ratification** (net swing ≥ +$1,400, N=10, sign+mirror gates) | A degenerate bar can't fail a bad bot or pass a good one (S1's `×1.15` lesson, LADDER.md:141-154) | Flag in REVIEW Open Questions + assumption-question; proceed on the recommendation (DOCTRINE.md:26). |
| R-1 | **Zero-sum head-to-head** makes "Exp median ≥ control median + margin" nearly tautological (§3.1) | Bar may be weaker than it looks | Reframed bar as "Exp net swing ≥ +margin" (≈ trades favorably); add sign-robustness (≥7/10) + mirror symmetry so it isn't one blowout. |
| R-2 | **Wall-clock watchdog kills a 720s match before natural end** → non-reproducible, breaks the paired model | Divergent verdicts, lost power | Generous `--max-wall-secs` for S2 so matches reach `time_limit`/SR end, not the watchdog (`plans/260720_seeded_determinism.md:153-158`). |
| R-3 | **Combat fairness ≠ economy fairness.** S1's mild russia lean (LADDER.md:136-139) may not predict combat asymmetry (rosters differ) | Mis-read spawn/faction luck as skill | Dedicated S2 CALIBRATE at 720s (§5); mandatory mirror; require both-spawn symmetry in the bar (§4.2). |
| R-4 | **Map choice contested** — draft LADDER puts S2/S3 on the 66×34 stub (LADDER.md:341-342); this doc recommends River Zeta | Rung incoherence if unresolved | Recommend updating LADDER S2/S3 to River Zeta so the rung/composite-gate model holds (§2.1); note as a LADDER edit for the implementing cycle. |
| R-5 | **Overfitting to the fixed 10 seeds** now that they're deterministic | Behaviors that only win on the standard set | Rotate/expand seeds at BASELINE (REVIEW.md:55 caveat); keep N≥10 even though determinism tempts N=1. |
| R-6 | **C2 (losses-on-approach) spatial classify** needs an INotifyKilled hook + a "contested center" definition | Scope creep into the first S2 cut | Ship first cut with existing fields only (§3.1); C2 is a later dispersion-A/B cycle, not the S2 stand-up. |
| — | **Doc contradiction found** (logged to DISCOVERIES): LADDER.md still describes S2/S3 as `kills_cost − deaths_cost` on the *combat stub* map (LADDER.md:238-268, :341-342) and still calls per-seed replay "broken" in places (LADDER.md:48-56, SPEC §3.2, REVIEW.md:133-136) — both now stale after the determinism ship (`2d3c8fe0`). | Stale curated guidance | Flagged in DISCOVERIES (this commit); the S2-implementing cycle should reconcile LADDER's S2 row + the "seeds are labels not replays" wording with the shipped determinism. |

### Open questions for the user (to post as questions on implementation)

1. **Opponent:** Normal (continuity) vs Rush (harder combat stress)? Recommend Normal-with-Rush-fallback gated on CALIBRATE (§2.4).
2. **Bar:** ratify net swing ≥ +$1,400 / N=10 / ≥7-10 sign / both-spawn symmetry (§4.2)?
3. **Map:** confirm moving S2/S3 onto the River Zeta rung (this doc) vs keeping the 66×34 stub (draft LADDER) (§2.1 / R-4)?
4. **Scope of first cut:** existing fields only, or include C1 (`army_value_integral`) from the start (§3.2)?

---

## 8. Concrete build checklist (for the implementing cycle — NOT executed here)

1. Copy `tournament-s1-eco-river-zeta/{map.yaml,map.bin,shadows.bin,rules.yaml}` → new `tournament-s2-combat-river-zeta/`; add a `tournament-combat-12min.yaml` config (720s, `SpeedMultiplier: 8`, scorer/win-rule unchanged). Mind the MiniYaml blank-line rule (CLAUDE.md).
2. Build the `-mirror` (faction/side swap) and `-cal-nn` (both `@normal`) twins — S1 pattern (LADDER.md:343-344).
3. (Optional first cut) Add C1 `ArmyValueIntegrator` + emit `army_value_integral`, bump `verdict_version` 5→6 additive (BotVsBotMatchWatcher.cs:272-288, :347-361; MatchTypes.cs:75-92). Confirm the aggregator tolerates the new field.
4. CALIBRATE (§5) → then Experimental-vs-control N=10 → cycle card + LADDER S2 row.
5. Update LADDER.md §Scenario 2/3 to the River Zeta rung + reconcile the stale determinism wording (R-4, DISCOVERIES).

**No engine scorer/win-rule change in the first cut → no S1 re-BASELINE.** Any later scorer change (or C2/C3 if promoted to a scorer input) triggers a re-BASELINE per DOCTRINE.md:11 — cost it then.
