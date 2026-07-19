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

## Ladder structure

- A **rung** is one map. The first (and currently only) rung is **River Zeta WW3**
  (`mods/ww3mod/maps/river-zeta-ww3/`, Title "River Zeta WW3"). **S1 (economy)**
  runs on `tournament-s1-eco-river-zeta` — a scenario whose `map.yaml` is derived
  from the canonical River Zeta terrain (full 98×82, all **12 neutral OILB income
  derricks kept**) with the harness overlay (2 SRs + 2 bot spawns). **S2 (combat)
  and S3 (win-rate)** run on the combat stub `tournament-v2-vs-normal-2p`
  (66×34, no POIs — fine, those facets don't need capturables). Each has a
  faction-swapped mirror twin for bias control (SPEC §9.4). (S1's mirror,
  `tournament-s1-eco-river-zeta-mirror`, is a required follow-up — not yet built.)
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
`i = 1..N` (`run-tournament.sh:206`), but **seeds are run labels, not
reproducibility guarantees** — bots draw from an unseeded `LocalRandom`, so a
seed does **not** replay the same game (SPEC §3.2, DISCOVERIES 2026-07-19). Every
run is an independent sample, which is all the N-run statistics need.
"Re-verification" therefore means **re-running the N matches** (a fresh
independent batch), not replaying identical games; a larger N narrows the median.
The even/odd index split still deterministically selects primary vs mirror
*scenario* per match (that's index parity, not RNG) — bias control is unaffected.

---

## Scenario 1 — Economy Race (the user's sketch)

**Question:** does the Experimental AI's POI-capture layer actually convert into
*more money earned* than Normal in a fixed window?

| Field | Value |
|---|---|
| Scenario | `tournament-s1-eco-river-zeta` (**genuinely on River Zeta terrain** — 98×82, all 12 neutral OILB derricks), + mirror for bias (mirror not yet built) |
| Contestants | P1 Experimental (v2) vs P2 Normal (control) |
| Match length | **5 minutes** — `TimeLimitSeconds: 300` (candidate bump to 420–600s pending — see finding below) |
| **Metric** | **`capture_income_gross`** (cumulative GROSS building income, pre-upkeep, verdict `stats.capture_income_gross`, verdict_version 3 — integrated read-only from `PlayerResources.TotalBuildingIncome`, `GrossIncomeIntegrator`). `resources_earned` (net `PlayerResources.Earned`) stays in the verdict as **context only**, not the metric. |
| N runs | **10** (5-min matches are cheap: ~1–2 wall-min each hidden at 8×) |
| Seeds | `1017, 2017, … 10017`; even = primary, odd = mirror (`--mirror`) |
| Advancement | `median(v2 earned) ≥ median(Normal earned) × 1.15` (15% margin) |
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
> 1a. **WIN-RULE ECONOMY TERM — deserves loop-manager review (NOT changed here, by design):**
>    `WeightedComponentMatchScorer` still feeds its `capture_income` component (and thus
>    the `TimeOrSrCaptureWinRule` outcome for S2/S3) from **net** `PlayerResources.Earned`,
>    which is blind to a held derrick's gross income in this SR-budget economy (same defect
>    the S1 metric just fixed). Repointing that term at `capture_income_gross` would make the
>    economy axis actually count captured income in match *outcomes* — but it would **silently
>    redefine S2/S3 winners**, so it was left untouched. The loop manager should decide whether
>    the win-rule economy weight should move to gross, and re-baseline S2/S3 if so.
> 2. **POI symmetry / calibration (after the metric can see income):** build
>    `tournament-s1-eco-river-zeta-mirror` and gate S1 on a **Normal-vs-Normal batch
>    landing ~even** on the new metric (SPEC §9.4) — otherwise an earned gap could be
>    spawn-side derrick luck, not AI skill.

> **PITFALL:** the S1 metric is **`capture_income_gross`** (verdict_version 3), NOT
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
| Scenario | `tournament-v2-vs-normal-2p` (River Zeta), + mirror |
| Contestants | P1 Experimental (v2) vs P2 Normal (control) |
| Match length | **12 minutes** — `TimeLimitSeconds: 720` (guarantees sustained contact) |
| **Metric** | **net combat budget swing** = `stats.kills_cost − stats.deaths_cost` |
| N runs | **15** |
| Seeds | `1017 … 15017`; even primary, odd mirror |
| Advancement | `median(v2 net swing) ≥ median(Normal net swing) + margin`, margin = **1× a single mid-tier unit's cost** (an absolute floor, so a positive-but-tiny edge doesn't count as "better") |
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

---

## Scenario 3 — Decisive Outcome / Win-rate (proposed)

**Question:** the bottom line — across many games, does the Experimental AI
*actually win* more than the Normal control on even terms?

| Field | Value |
|---|---|
| Scenario | `tournament-v2-vs-normal-2p` (River Zeta), + mirror (essential here) |
| Contestants | P1 Experimental (v2) vs P2 Normal (control) |
| Match length | **12 minutes** — `TimeLimitSeconds: 720` (the committed default config) |
| **Metric** | **Experimental win-rate** (fraction of matches where `winner_name` is the v2 player), from `summary.json` |
| N runs | **20** (a win-rate needs statistical power; 20 is the harness's canonical sanity size) |
| Seeds | `1017 … 20017`; even primary, odd mirror — **mandatory**, so win-rate isn't spawn-side artifact |
| Advancement | **win-rate ≥ 0.55** (beats the 50/50 even-match null by a 5-point margin), AND Normal-vs-Normal on this scenario is verified ~0.50 (map isn't biased) |
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
| S1 Economy Race | `tools/autotest/scenarios/tournament-s1-eco-river-zeta/` | `tournament-eco-5min.yaml` (`TimeLimitSeconds: 300`, `SpeedMultiplier: 8`) |
| S2 Force Efficiency | `tools/autotest/scenarios/tournament-experimental-vs-normal-2p/` | the committed `tournament.yaml` (`720s`) |
| S3 Win-rate | `tools/autotest/scenarios/tournament-experimental-vs-normal-2p/` | the committed `tournament.yaml` (`720s`) |
| S1 bias twin | `tools/autotest/scenarios/tournament-s1-eco-river-zeta-mirror/` | **not yet built** (required follow-up) |
| S2/S3 bias twin | `tools/autotest/scenarios/tournament-experimental-vs-normal-mirror-2p/` | matching mirror configs |

S1 now uses a **River-Zeta-derived** scenario (`tournament-s1-eco-river-zeta`):
its `map.yaml` keeps the full River Zeta terrain + all 12 neutral OILB derricks
and overlays the harness's 2 SRs + 2 bot spawns; `rules.yaml` and
`tournament-eco-5min.yaml` are byte-identical to the `tournament-v2-vs-normal-2p`
originals. This is a **harness** change (allowed, SPEC §4.1) — map/scenario only,
no unit stat / balance / engine edit (derrick income stays `$50`). S2 and S3 keep
the combat stub + committed 720s config as-is.
