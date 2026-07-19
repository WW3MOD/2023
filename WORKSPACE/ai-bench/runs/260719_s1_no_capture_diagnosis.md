# S1 diagnosis — why v2 captured ZERO derricks on `tournament-s1-eco-river-zeta`

**Cycle under diagnosis:** `260719_1844__tournament-s1-eco-river-zeta__86aa2db` (N=1 hidden smoke)
**Scenario:** `tournament-s1-eco-river-zeta` (real River Zeta 98×82, 12 neutral OILB derricks)
**Grounded against:** ai-bench @ `0eb71d49` (worktree clean at diagnosis time)
**Method:** static (YAML + engine C#) + the surviving engine debug log — no new match run.
**Boundary verdict:** **AI-CODE-SIDE.** No harness/map/AI code changed this cycle; recommended
fix recorded below for the optimization loop's first cycle.

---

## TL;DR (root cause)

v2's capturer **was built and did fire** — the earlier cycle card's "capture layer didn't
fire in-window" is **wrong**. The debug log shows v2 built a TECN and issued exactly **one**
capture order at tick 762: its sole TECN → a **Logistics Center** at `31,52`, **not** the
oil derrick 3 cells from its Supply Route.

The Logistics Center **has no `CashTrickler`** — capturing it yields **$0** of
`resources_earned`. But `PoiMap.IncomeWeights` lists `logisticscenter: 200` — the **highest
weight of any structure** (oilb=50, fcom=100, bio=150) — so PoiMap ranks it the #1 "income"
capture target (score `104,000,000`), the sole TECN is goal-guard-committed to it, walks ~20
cells cross-map into contested ground, and dies (or captures a no-income depot) around tick
1737. No second capturer was alive after that, so all **12 real oil derricks — including one
~3 cells from the SR — were never touched.** `resources_earned` = 0 by *mis-scoring*, not by
map, reachability, or a dead pipeline.

**The single defect:** a non-income structure (`logisticscenter`) is weighted as the
top-value income POI, hijacking the only capturer away from the derricks that actually pay.

---

## Evidence chain (file:line)

### 1. The TECN was built and issued a capture order — to the Logistics Center
Engine debug log (`%APPDATA%\OpenRA\Logs\debug.log`, from the 18:46 smoke):
```
[v2-capture] poimap-scan player=USA-bot idleCapturers=1 targets=15
             top=logisticscenter@31,52 action=Capture score=104000000 tick=762
[v2-capture] issue     player=USA-bot actor=tecn.america@13,46 → logisticscenter@31,52
             score=104000000 tick=762
```
- `pre-scan` fires 22× → v2 **owned a capturer** (`tecn.america`). The "no TECN in the
  call-in mix" suspect is **FALSE**: v2 is USA/nato and inherits
  `UnitBuilderBotModule@america.normal` (`ai-america.yaml:3-9`, gated
  `enable-ai-player && player.nato`; `enable-ai-player` is granted to `normal, v2` per
  `ai.yaml:45-47`), which builds `tecn.america: 500` (highest priority, limit 3).
- Exactly **one** `issue` line the entire match — and it targets the Logistics Center, never
  an OILB (`grep '→' → 1× logisticscenter@31,52`).

### 2. The TECN walked cross-map to the LC, then died / earned nothing
`pre-scan` trail (same log): `13,46` (t762) → `17,47` → `20,47` → … → `30,53` (t1737,
adjacent to the LC), `activity=CaptureActor committed=True→…` the whole way. After t1737 the
TECN **disappears from every subsequent scan** (out of 7500 total ticks) — killed in
contested mid-map, or it captured a depot that pays nothing. `resources_earned` stays 0
either way. `PoiGoalGuard` (`ai.yaml:103-105`, 300-tick commitment) correctly held it to its
objective — so it never diverted to the derrick it walked *past*.

### 3. The Logistics Center yields ZERO income
`mods/ww3mod/rules/ingame/structures.yaml:345-403` — `LOGISTICSCENTER` has **no
`CashTrickler`**. Its economic traits are `RepairsUnits` + `SupplyProvider` (vehicle
repair/rearm) only. In the SR budget model `PlayerResources.Earned`
(`BotVsBotMatchWatcher.cs:308` → `resources_earned`) moves **only** on capturing a
`CashTrickler` structure. Capturing the LC → **$0 earned.** It is not an income POI at all.

### 4. …yet it is weighted as the #1 income POI
- `mods/ww3mod/rules/world.yaml:296-303` — **`PoiMap.IncomeWeights: … logisticscenter: 200`**
  (highest of all; oilb 50 / fcom 100 / bio 150).
- `PoiMap.cs:212` — `var isIncome = Info.IncomeWeights.ContainsKey(name);` — an actor is
  discovered as an income capture POI **purely because it is listed** in `IncomeWeights`.
  The trait's own doc (`PoiMap.cs:91-92`) states the contract: *"Income structures use their
  CashTrickler-equivalent weight; only listed types are discovered as income POIs."*
  `logisticscenter` has **no** CashTrickler, so listing it **violates that contract.**
- `PoiMap.cs:223` gates income POIs on `HasTraitInfo<CaptureManagerInfo>()` — the LC passes
  (it's neutral-capturable), so it is discovered, scored value=200, and sorted to the top.
- `CaptureCoordinatorBotModule.cs:340-388` (`QueueCaptureOrdersFromPoiMap`) walks the PoiMap
  ranking top-down and assigns the **nearest free capturer to the top target** — so the sole
  TECN goes to the LC, not the nearer derrick.

### 5. Value weight overwhelms proximity — the near derrick can't compete
Scoring is `value × distFactor × …` (`PoiMap.cs` / `PoiScoring.Score`; distFactor =
`halfLife·100/(halfLife+distCells)`, `DistanceHalfLifeCells=20`):
- LC `31,52`, ~20 cells from SR: distFactor ≈ `20·100/40 = 50`; value 200 → base ≈ **10,000**.
- OILB `17,44`, ~3 cells from SR: distFactor ≈ `20·100/23 = 87`; value 50 → base ≈ **4,350**.

The LC outscores the adjacent derrick by ~2.3× **despite being ~7× farther** — the 4× value
gap swamps the distance advantage. There is no distance at which a value-50 derrick beats a
value-200 LC inside this half-life. The map (`map.yaml`: `Actor2300`/`Actor5166 =
logisticscenter`; 12× `oilb`) guarantees the LC is always the pick.

### Why this is invisible in the passing tests (the requested contrast)
`test-v2-poi-capture` (PASSES) and `test-v2-poi-harness` run on small stub maps whose only
capturables are derricks/BIO — **no Logistics Center**, so the top-scored target *is* an
income structure and the pipeline captures it. The bug only manifests when a **real map
contains a `logisticscenter`** (River Zeta has two). The tournament scenario is the first
S1 setup with one present. The capture stack is healthy; the **scoring table is wrong.**

---

## Boundary: this is AI-CODE-SIDE

The defect is a value in `PoiMap.IncomeWeights` (a v2 strategic-layer trait, SPEC §4.1
explicitly names `PoiMap` an Experimental-AI module) and its mirror in
`CaptureCoordinatorBotModule@v2.tecn.IncomeWeights` (`ai.yaml:120-127`, `enable-ai-v2`).
Both are v2 scoring config → **AI code**. Per the task boundary I did **not** change them.

- **Not harness-side:** the wiring is all present and worked — TECN built, PoiMap discovered
  the POIs (targets=15), CaptureCoordinator issued an order. Nothing in the scenario needed
  enabling.
- **Not a map fix:** the LC is legitimate River Zeta content. Deleting it to help the AI
  would shorten the yardstick (SPEC §4.3, forbidden). A competent AI must simply not send
  its only capturer to a no-income depot.
- **No stat/balance change involved:** the fix touches an AI *scoring weight*, not any unit
  stat, cost, or the LC's own income (which stays $0).

---

## Recommended fix (ONE concrete change — the loop's first AI cycle)

**Remove `logisticscenter` from `PoiMap.IncomeWeights` (`mods/ww3mod/rules/world.yaml:303`),
and from `CaptureCoordinatorBotModule@v2.tecn.IncomeWeights` (`mods/ww3mod/rules/ai/ai.yaml:126`)
for the no-PoiMap fallback path.**

Rationale: an actor with **no `CashTrickler` contributes $0 to `resources_earned`**, so per
PoiMap's own documented contract (`PoiMap.cs:91-92`) it must not be listed as an income POI.
With `logisticscenter` delisted, PoiMap stops discovering it as an income capture target
(`PoiMap.cs:212`), the top capture target becomes the **nearest OILB** (`17,44`, ~3 cells),
and v2's already-proven capture pipeline (green in `test-v2-poi-capture`) will take it —
lifting `resources_earned` off 0.

Notes for whoever runs the cycle:
- If LC-capture-to-**deny-enemy-resupply** is still wanted strategically, model it as a
  *separate, low-priority* objective (e.g. an offensive/deny target), **not** as the #1
  *income* POI. As currently weighted (200) it out-prioritizes every real money structure and
  starves the sole TECN — the opposite of the S1 objective.
- The `IncomeWeights` for genuine income structures (oilb 50 / fcom 100 / bio 150) already
  mirror their `CashTrickler` amounts — keep them; only the non-income `logisticscenter`
  entry is the error.
- Verify with one hidden S1 smoke that `resources_earned > 0` for v2 (nearest OILB captured
  inside 300s). Reachability is not the blocker (derrick ~3 cells from the SR).

## Collateral note (control, non-blocking)

Normal (Russia) also earned $0, but for a **different, benign** reason: its legacy
`CaptureManagerBotModule@tecn` (`ai.yaml:90-95`, `enable-ai-legacy-only`) lists
`CapturableActorTypes: oilb,bio,miss,fcom,hosp` — **no `logisticscenter`** — so the control
is *not* distracted by the LC. Its $0 is a separate question (Russia's TECN not built/arrived
in-window on its side of the 98×82 map) and is fine for the benchmark (the control shouldn't
game the eco metric). It should be confirmed by the pending Normal-vs-Normal S1 calibration
batch (SPEC §9.4) before any v2 S1 number is trusted, but it does **not** block the v2 fix
above.
