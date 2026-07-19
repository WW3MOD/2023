# AI Benchmark — Scenario Ladder

The **ladder** is the ordered set of standardized tests the Experimental AI
(`ModularBot@v2`) must beat to demonstrate improvement. Governed by
[`SPEC.md`](SPEC.md) (advancement §6, run modes §3, data §8). This file is the
**definition** of the rungs and scenarios; the **live standing** (current medians
vs control) lives in [`REVIEW.md`](REVIEW.md) §Ladder Status.

---

## Ladder structure

- A **rung** is one map. The first (and currently only) rung is **River Zeta WW3**
  (`mods/ww3mod/maps/river-zeta-ww3/`, Title "River Zeta WW3"), run via the
  existing scenario `tournament-v2-vs-normal-2p` (P1 = Experimental/v2,
  P2 = Normal), with its faction-swapped twin `tournament-v2-vs-normal-mirror-2p`
  for bias control (SPEC §9.4).
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
| Scenario | `tournament-v2-vs-normal-2p` (River Zeta), + mirror for bias |
| Contestants | P1 Experimental (v2) vs P2 Normal (control) |
| Match length | **5 minutes** — `TimeLimitSeconds: 300` |
| **Metric** | **`resources_earned`** (cumulative `PlayerResources.Earned`, verdict `stats.resources_earned`, `BotVsBotMatchWatcher.cs:308`) |
| N runs | **10** (5-min matches are cheap: ~1 wall-min each windowed at 6×) |
| Seeds | `1017, 2017, … 10017`; even = primary, odd = mirror (`--mirror`) |
| Advancement | `median(v2 earned) ≥ median(Normal earned) × 1.15` (15% margin) |
| WinRule | `score_or_sr_capture` (irrelevant to the metric; keep for a valid match end) |

> **PITFALL:** the metric is **`resources_earned`**, NOT `PlayerStatistics.Income`.
> `Income` is a rolling 60-second figure; using it silently measures the wrong
> thing (SPEC §8.2, `BotVsBotMatchWatcher.cs:292-294`).

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
| S1 Economy Race | `tools/autotest/scenarios/tournament-v2-vs-normal-2p/` | a `tournament-eco-5min.yaml` (copy of `tournament.yaml`, `TimeLimitSeconds: 300`) |
| S2 Force Efficiency | same | the committed `tournament.yaml` (`720s`) |
| S3 Win-rate | same | the committed `tournament.yaml` (`720s`) |
| bias twin (all) | `tools/autotest/scenarios/tournament-v2-vs-normal-mirror-2p/` | matching mirror configs |

Creating the `tournament-eco-5min.yaml` config variant (S1) is a **harness**
change (allowed, SPEC §4.1) — just a `TimeLimitSeconds` override on the existing
scenario; no map or engine edit. S2 and S3 reuse the committed 720s config as-is.
