# PLAN — Experimental AI point-of-interest (POI) strategy

> Status: **APPROVED — Phase 0 + Phase 1 in progress (2026-07-19).**
> Mode: EXPERIMENTAL. Scope: the **Experimental AI only** (`ModularBot@v2`,
> lobby name "V2 AI (experimental)"). Normal / Rush / Turtle stay untouched as
> the control.
> Author date: 2026-07-19.

---

## Approved decisions (2026-07-19, user)

The three open questions in §7 are now decided. These override any conflicting
framing later in the doc:

1. **Architecture — Path A (see §4).** Bolt the POI layer onto the live v2 bot
   plus a per-unit **goal-guard** component, designed so it ports cleanly into a
   future v3 brain. Path B (POI as first slice of a fresh v3 brain) is deferred.

2. **Neutral Supply Route — deny-only is PERMANENT DESIGN, not a deferred fix.**
   Realism rule: capturing an enemy Supply Route flips it **Neutral**,
   symbolizing *cutting the enemy's reinforcement route*. The capturer must
   **never** be able to reinforce through a captured SR — there is no
   "capture-to-own-side" follow-up to build. Desired AI behavior (later phases):
   **attack the enemy SR → neutralize it → hold with a SMALL garrison** so the
   defeated player's allies can't recapture it (recapture would drag a defeated
   player back into a team game). Every "capture the neutral SR as a new
   reinforcement lane" phrasing below is superseded by this: the value of an SR
   POI is **denial + a forward strongpoint to garrison**, never a lane for us.

3. **Phase 3 offense — fully score-floating axes (see §5.3, §5.5).** There is
   **no dedicated enemy-base axis**. The enemy base competes on POI score like
   every other objective; if derricks or the enemy SR circle score higher, the
   AI may ignore the enemy base entirely. The user explicitly accepts early
   passive / suboptimal games — the goal is a foundation for a genuinely
   decision-making AI, tuned over time, not an immediately strong bot.

---

## 0. TL;DR for the reviewer

Three things you should know before reading further, because they reframe the
request:

1. **"Experimental AI" = the `v2` bot.** It is not a fresh clean slate — it
   already ships three custom WW3MOD modules: `CaptureCoordinatorBotModule`
   (income-weighted capture + escort + defense), `LayeredDefenceBotModule`
   (reserve-driven line filling), `MountedTransportBotModule` (IFV infantry
   ferry), on top of the shared `InfluenceMap` + `FrontlineOverlay` perception.

2. **Parts 1, 3, 4 of your request are already *coded* — they just don't
   *work* reliably.** v2 already tries to capture oil derricks with an escort
   and defend what it holds (`CaptureCoordinatorBotModule`, wired in
   `ai.yaml:100-129`, income weights OILB=50/FCOM=100/BIO=150). The reason you
   saw it ignore derricks and death-ball is that these modules are built on
   `IsIdle` (which flickers) and fight each other for units — the whole class
   of bugs catalogued in `WORKSPACE/ai/02_problem_statement.md §3`. The team
   already diagnosed this and wrote up a decision to **abandon v2 and rebuild
   as a "v3 brain"** (`WORKSPACE/ai/README.md`, `01/02/03_*.md`).

3. **Part 2 (spread out instead of death-ball) is genuinely missing.** v2 has
   *no offensive targeting at all* — no ground `SquadManagerBotModule`
   (gated `enable-ai-legacy-only`), and `LayeredDefenceBotModule` is purely
   reactive ("when the frontline is empty, this module does nothing"). There is
   no code anywhere that scores map POIs, picks spread-out objectives, or
   reasons about chokepoints / neutral Supply Routes.

So this plan has to answer an **architecture fork first** (§4), then proposes a
POI system (§5) delivered in independently-shippable phases (§6).

---

## 1. Goal

Give the Experimental AI a **point-of-interest layer**: discover the valuable
points on a map (money structures, capturable buildings, neutral Supply Routes,
chokepoints), score them by value/distance/threat, and drive behaviour off that
score — capture the income POIs with escorted technicians, **spread offensive
pressure across multiple scored objectives instead of one death-ball at the
enemy spawn**, and garrison what it captures. Normal AI is the untouched
control so we can A/B whether the POI layer is actually better.

---

## 2. Hard constraints (WW3MOD game model — do not design around RA)

Read `DOCS/reference/game-model.md` + `DOCS/reference/supply-route.md`. The POI
design must respect:

- **No factories / no tech tree.** Units are called in from off-map reserves
  via the Supply Route and **walk in from the nearest map edge** to the rally.
  "Produce a technician" = call in a reinforcement that then has travel time.
  A POI-capture squad is not available the instant it's ordered.
- **The Supply Route is a fixed, indestructible beachhead**, one per player.
  The AI must never try to build one. Capturing an **enemy SR flips it to
  Neutral** (per decision #2) — this is **denial**, never a new lane for the
  capturer: the SR POI's value is cutting the enemy's reinforcements + leaving a
  forward strongpoint to hold with a small garrison. Neither AI currently
  targets SRs (SR is absent from every `CapturableActorTypes` list). Pressuring
  the *enemy* SR's contestation circle is a high-value offensive objective per
  the SR doc — a POI in its own right, distinct from "attack the enemy base".
- **Cost = budget allocation.** Losing an escorted TECN to a contested derrick
  is a permanent budget loss; anti-suicide gating matters.
- **Engine still carries RA assumptions** (airpad-per-aircraft, MCV/harvester
  modules that no-op). Don't trust legacy logic without checking WW3MOD usage.

---

## 3. Current-state findings (what the Experimental AI does today)

### 3.1 Which modules the `v2` bot actually runs

From `mods/ww3mod/rules/ai/ai.yaml` + `ai-america.yaml` / `ai-russia.yaml`,
resolving the condition gates (`v2` gets `enable-ai-any`, `enable-ai-player`,
`enable-ai-v2`, but **not** `enable-ai-legacy-only`):

| Concern | Module the v2 bot runs | Notes |
|---|---|---|
| Production | `UnitBuilderBotModule@{fac}.normal` (shared via `enable-ai-player`) | Static `UnitsToBuild` fractions; calls in `tecn` (limit 3) |
| Adaptive prod | `AdaptiveProductionBotModule@v2.{fac}` | Counter-comp, 300-tick eval |
| Capture | **`CaptureCoordinatorBotModule@v2.tecn`** | income-weighted + escort + defense (the good one) |
| Ground positioning | **`LayeredDefenceBotModule@v2`** | reactive, frontline-only |
| Infantry ferry | **`MountedTransportBotModule@v2`** | IFVs carry infantry to gaps |
| Air | `HelicopterSquadBotModule`, `SquadManagerBotModule@{fac}.fixedwing` | shared, `enable-ai-any` |
| Scout / Garrison / Repair / SupplyFollower | shared `enable-ai-any` variants | |
| **Ground offense (`SquadManagerBotModule@{fac}.normal`)** | **NOT run** — gated `enable-ai-legacy-only` | this is why v2 has no death-ball squad… |

### 3.2 Why it *looks* like a death-ball anyway

Honest answer: **partly diagnosed, partly needs a runtime confirm** (see risks).
The mechanisms I can see in code:

- **No offensive brain.** With the legacy ground `SquadManager` gated off, the
  only things that move v2's ground units forward are (a) `LayeredDefence` once
  a frontline exists, (b) `CaptureCoordinator` escort/defense `AttackMove`
  orders, and (c) each unit's own `AutoTarget` drift. There is no code that
  says "split the army across three objectives." So units pool near the SR /
  rally and then flow to wherever the first contact makes a frontline — which,
  on a 2-player map, is the straight line to the enemy. That reads as a
  death-ball even without a squad manager.
- **The capture layer under-fires.** `CaptureCoordinator` filters candidate
  TECNs by `a.IsIdle` (line 197) and re-scores every 75 ticks. Per
  `02_problem_statement.md §3.1`, a TECN mid-walk flickers idle → gets a new
  `CaptureActor` order → restarts → never arrives. Net effect the user saw:
  derricks ignored. TECN unit-limit is only **3**, and they're also the escort
  pool's exclusion, so capture throughput is low even when it works.
- **`LayeredDefence` explicitly no-ops with no frontline** (file header:
  "When the frontline is empty… this module does nothing"). It was written
  assuming the legacy SquadManager handles opening play — but for v2 that
  manager is gated off, so nothing handles opening play.

### 3.3 What money / capture POIs look like in YAML

`mods/ww3mod/rules/ingame/structures-neutral.yaml`:

| Actor | Name | Income (`CashTrickler`) | Capturable | Other value |
|---|---|---|---|---|
| `oilb` | Oil Derrick | **$50** | yes (`OwnerLostAction: ChangeOwner`, `EngineerRepairable`) | `UpdatesDerrickCount` |
| `fcom` | Expansion Post | **$100** | yes | `GivesBuildableArea`, `BaseProvider` range 8c0, prereq |
| `bio` | Nuclear Reactor | **$150** | yes | prereq |
| `miss` | Communications Center | $0 | yes | radar (50c0) |
| `hosp` | Hospital | $0 | yes | healing |
| `logisticscenter` | (enemy) | — | yes | resupply denial (v2 captures it) |
| `supplyroute` (neutral) | Supply Route | — | yes (engineer chain) | **new reinforcement lane — currently un-targeted** |

Capturer: **`tecn`** only (technician; engineers/`e6` have a dead capture trait
by design — `DOCS/gameplay/capturing.md`). AI can call TECN in via the SR
production queue. Structures are captured, not built; captured neutral SR flips
to Neutral first on the *previous* owner losing it (SR doc).

### 3.4 Perception primitives that already exist (reusable)

- `InfluenceMap` (world trait) — friendly/enemy density grid, perspective-aware.
- `FrontlineOverlay` (world trait) — derived contested band, `/frontline`.
- `ThreatMapManager` — exploration age + per-cell threat (scout/heli use it).
- `BotBlackboard` — unit-claim mutex (opt-in; SquadManager ignores it).
- **Not built:** `ResourceMap`, `TerrainCache` (chokepoints/cover),
  `SectorMap`, `GoalLedger` — all proposed in `03_substrate.md`, none exist.

---

## 4. THE FORK — where does the POI layer live? (top decision for the user)

The team has a written decision (`WORKSPACE/ai/README.md`) to **retire v2** and
rebuild as a "v3 brain" on a proper substrate (`GoalLedger` replacing `IsIdle`,
a single central decider). The POI work can attach in two ways:

**Path A — Bolt the POI layer onto the existing v2 modules (incremental).**
Add a new `PoiMap` world trait + a new `PoiOffensiveBotModule`, and *extend*
`CaptureCoordinatorBotModule` to consult it. Ships fast, testable now, keeps
"Experimental AI" improving for players today. Risk: builds on the `IsIdle`
codebase the team already decided to replace — some effort is throwaway, and we
inherit the order-overwriting bug class unless we also add a lightweight
per-unit goal guard.

**Path B — Make the POI layer the first slice of the v3 brain.**
Build `PoiMap` + a minimal `GoalLedger` (per-unit goal record, `03_substrate.md
§5.2`) and drive capture/offense/defense off goals from day one. Aligns with the
long-term plan, kills the order-overwriting bug at the root, but it's a bigger
lift and the "Experimental AI" the user is playing wouldn't improve until the
v3 gate is switched on.

**My recommendation: Path A, but with the one v3 idea that pays for itself now
— a minimal `GoalLedger`-style "commitment" guard** so TECNs and capture squads
don't thrash. Concretely: a small per-unit `{objective, expiresAtTick}` map the
POI modules check before re-issuing an order. That single primitive fixes the
"derricks ignored" symptom, is ~100 lines, and ports directly into v3 later
(substrate §9 explicitly says the scoring logic is reusable and only the
assignment mechanism moves to the brain). This keeps every phase shippable to
the live Experimental AI while not digging the v2 hole deeper.

**This fork is question #1 for you.** The rest of the plan is written for
Path A + goal-guard; if you pick B, the phases are the same but land as v3
brain methods instead of new `IBotTick` modules.

> **DECIDED: Path A + goal-guard.** Phase 0/1 implement the per-unit goal-guard
> as a reusable component (pure ledger + thin player trait) explicitly designed
> to port into the v3 brain — only the *assignment mechanism* (IBotTick module
> vs brain method) moves later; the commitment/expiry logic is reused verbatim.

---

## 5. POI system design

### 5.1 What is a POI

A `Poi` is any map location worth reasoning about strategically, beyond
"the enemy base". Types, in rough value tiers:

| POI type | Source | Base value driver | Action |
|---|---|---|---|
| Income structure | `oilb/fcom/bio` neutral or enemy | `CashTrickler` amount | **Capture** (escorted TECN) → then **Defend** |
| Enemy Supply Route circle | enemy `supplyroute` | production-denial (contestation) | **Pressure** (park units in circle) |
| Enemy Supply Route (capture) | enemy `supplyroute` | denial — flips it Neutral, cuts enemy reinforcements | **Capture-to-neutralize** (escorted TECN) → **Hold** w/ small garrison (never a lane for us — decision #2) |
| Utility structure | `miss` (radar), `hosp` (heal), enemy `logisticscenter` | tactical, not $ | **Capture** opportunistically |
| Chokepoint | `TerrainCache` adjacency analysis | controls a lane | **Hold / screen** (defensive garrison) |
| Enemy base center | existing intel | the death-ball target today | **Attack** — now *one* POI among several, not the only one |

### 5.2 Scoring (reuse CaptureCoordinator's proven shape)

`CaptureCoordinatorBotModule.ScoreTarget` already computes
`income × distanceFactor × safetyFactor` and works. Generalise it to all POI
types:

```
score(poi) = valueWeight(poi.type)          // config, e.g. bio 150 > oilb 50 > choke 40
           × distanceFactor(poi, ownSR)      // halflife decay, closer = higher
           × safetyFactor(poi)               // enemies-within-R buckets (safe/mild/hostile)
           × ownershipMultiplier(poi)        // unowned > enemy-owned > already-mine(=defend track)
```

Distance is measured from the **own SR / rally**, and — because units walk in
from the map edge — a second-order term can prefer POIs near the reinforcement
lane. Keep it simple in Phase 1 (straight-line distance); add path-cost later.

### 5.3 Capture vs Attack vs Defend decision

- **Capture** an income/SR POI when: it's unowned-or-enemy, `safetyFactor` says
  not hostile (< 3 enemies within 6 cells — reuse existing thresholds), and a
  TECN + escort can be spared. Anti-suicide: never send a lone TECN into a
  contested POI (already the intent in `SafetyMultiplierHostile`; enforce it as
  a hard gate, not just a score penalty).
- **Attack / pressure** the top-scored *offensive* POIs (enemy SR circle,
  enemy income structures, enemy base center) — **spread**: assign the
  offensive pool across the top *K* scored POIs weighted by score, instead of
  all-in on base center. K scales with army size (e.g. 1 axis per ~8 units).
  **Per decision #3, axes are fully score-floating** — the enemy base center is
  just another scored POI with no guaranteed axis; it may be ignored entirely
  when other POIs outscore it.
- **Defend** a POI the moment we own it: register it as a garrison target,
  assign a minimum garrison (reuse `CaptureCoordinator`'s defense pass, which
  already summons defenders when enemy value nearby > friendly value).

### 5.4 Escorted capture squads

Already exists (`DispatchEscort`, `EscortSize: 2`). Improvements:
- Pull escort from the **offensive pool near the TECN's path**, not just near
  the TECN's current position, so the escort actually screens the walk-in.
- Hold the TECN at a staging cell just outside the POI until the escort arrives
  (a `HoldFireAtAnchor`-style goal) — prevents the lone-TECN suicide.

### 5.5 Spreading the offense (the core of "part 2")

New `PoiOffensiveBotModule` (or v3 brain method) that:
1. Reads `PoiMap` for the top-K offensive POIs by score.
2. Reads the available offensive pool (units not claimed by capture / defense /
   ferry / already on a held line).
3. Allocates the pool across POIs proportional to score, each axis getting a
   minimum viable size (don't dribble single units).
4. Issues one `AttackMove` per axis to the POI cell, refreshed on a slow cadence
   with a per-squad goal guard so it doesn't re-path every scan.

This is what replaces the implicit death-ball: the enemy base is **not**
privileged — it competes on score like every other POI (decision #3), and
derricks / chokepoints / the enemy SR circle pull their share of units. If they
outscore the base, the base gets no axis at all.

### 5.6 Coexistence with existing modules (don't lose offense or break capture)

- Offensive module and `CaptureCoordinator` share the **unit-claim** discipline
  (extend `BotBlackboard.ClaimUnit` usage or the new goal-guard) so they don't
  both grab the same unit — the exact failure `02_problem_statement.md §3.2`
  describes.
- `LayeredDefence` keeps owning frontline reserves; the offensive module only
  claims units *not* on a held line. Carriers stay excluded (the B.4 PITFALL).
- Normal/Rush/Turtle: **zero changes** — everything new is gated
  `enable-ai-v2`.

---

## 6. Phased delivery (each phase independently shippable + autotest-able)

### Phase 0 — Diagnose + goal-guard (foundation, small)
- **Do:** add a runtime confirm of *why* v2 death-balls (one skirmish with the
  `[v2-capture]` / a new `[v2-poi]` log channel, read the log — **not** an
  autotest sweep). Add the minimal per-unit **goal-guard** primitive
  (`{objective, expiresAtTick}`) and route `CaptureCoordinator`'s TECN orders
  through it so capture stops thrashing.
- **Files:** new `BotModules/PoiGoalGuard.cs` (~120 lines, player trait);
  edit `CaptureCoordinatorBotModule.cs` (consult guard before re-issuing);
  `ai.yaml` wire under `enable-ai-v2`.
- **Test:** autotest — one TECN, one derrick, assert it captures within N ticks
  and receives no second `CaptureActor` order (mirrors success criterion S-E).
- **Ships:** derricks actually get captured. Visible win on its own.

---

#### Phase 0 FINDINGS (2026-07-19) — death-ball root cause: CONFIRMED

**Verdict: CONFIRMED.** The death-ball is a *structural absence of any offensive
brain for v2*, exactly as §3.2 inferred. The original plan flagged this as
"partly diagnosed, partly needs a runtime confirm" because the load-bearing
claim (LayeredDefence no-ops without a frontline) rested on the file's *header
comment*. It has now been confirmed against the **actual code**, which is more
decisive than the runtime symptom would be:

1. **LayeredDefence gates on an existing frontline.**
   `LayeredDefenceBotModule.AssignPositions` (engine
   `.../BotModules/LayeredDefenceBotModule.cs:161-164`) pulls the contested
   grid and **`if (contestedCells.Count == 0) return;`** — before contact there
   are zero contested cells, so the module does *nothing*. Its own header
   (line 28) says "existing SquadManagerBotModule handles opening play."

2. **The SquadManager that would handle opening play never runs for v2.**
   Ground `SquadManagerBotModule@{fac}.normal` is gated
   `RequiresCondition: enable-ai-legacy-only && enable-ai-player && player.nato`
   (`mods/ww3mod/rules/ai/ai-america.yaml:51-52`). v2 does **not** get
   `enable-ai-legacy-only`, so the module is never instantiated for v2.

3. **No other v2 module issues offensive movement for the general pool.**
   CaptureCoordinator only moves TECNs + their escorts; MountedTransport only
   ferries infantry to frontline gaps (which require a frontline);
   HelicopterSquad/fixed-wing move only air units. So before contact, the v2
   ground army has **no order source at all** → it pools at the SR rally. Once
   any contact creates a frontline, LayeredDefence fills the contested band —
   and on a 2-player map that band is the single straight-line contact point,
   so the whole reserve pool flows there. That reads as a death-ball even though
   no squad manager exists.

**Runtime instrumentation shipped:** `LayeredDefenceBotModule` now emits a
`[v2-poi] disperse` line every scan (before the frontline gate) with
`pool`, `centroid`, `clumpRadiusCells`, `centroidDistFromSRCells`, `contested`.
This is the log channel to capture the live trace in one bounded skirmish:
a death-ball reads as a small `clumpRadiusCells` that stays small while `pool`
grows, `centroid` marching from near-SR (`centroidDistFromSRCells` low, then
rising) toward the single contested band. Logs land in
`AppData/Roaming/OpenRA/Logs/debug.log` on Windows.

**Implication for Phase 2:** the fix is not "tune the death-ball" — it's to
*add the missing offensive brain* (`PoiOffensiveBotModule`, plan §5.5) that
issues multi-axis orders to the pre-contact pool. Confirmed that there is
nothing to disable/re-tune; Phase 2 is net-new behaviour, and the score-floating
axes (decision #3) become the sole driver of pre-contact ground offense.

**Runtime corroboration (1 bounded run, `test-v2-poi-observe`, 55s):** the
`[v2-poi]` pipeline works and emitted 18 clean lines for USA-bot (v2).
`contested=0` for the *entire* run — a frontline never formed, exactly matching
finding #1 (nothing drives the pre-contact pool). BUT the run could not show a
*populated* death-ball: `pool=0` throughout and **zero** `[v2-capture]` lines
(no TECNs ever existed), and the engine logged `Scenario selection: 'none',
available scenarios: []`. **Discovery:** on this scenario-map the SR
reinforcement system fed the bots nothing in the window, so neither bot built a
ground army. A live army/death-ball trace needs a runtime harness with the
scenario/production system actually feeding the SR queue (and a longer window).
The code evidence above remains the decisive confirmation; the run corroborates
the "no frontline / no pre-contact order source" half and validates the
instrumentation. Did **not** spend a second run chasing a longer window
(single-run discipline).

### Phase 1 — `PoiMap` + income-POI capture (medium)
- **Do:** new `PoiMap` world trait enumerating income/utility/SR POIs with
  derived `{owner, value, nearbyEnemies, distFromOwnSR, contested}` (the
  `ResourceMap` from `03_substrate.md §4.3`, scoped to what we need now). Add
  **neutral `supplyroute`** and `fcom` to the capture target set. TECN capture
  driven off `PoiMap` scores + hard anti-suicide gate + staged escort.
- **Files:** new `Traits/World/PoiMap.cs`; edit `CaptureCoordinatorBotModule.cs`
  to read `PoiMap`; `world.yaml` register trait; `ai.yaml` add `supplyroute` to
  `CapturableActorTypes`; optional `/poi` overlay (new diagnostics trait).
- **Test:** autotest — 3 derricks + 1 neutral SR at varying distance/threat;
  assert capture order matches score ranking (S-B analogue).
- **Ships:** AI captures the *right* money POIs first, including neutral SRs.

### Phase 2 — POI-scored spread offense (medium-large, the headline)
- **Do:** new `PoiOffensiveBotModule@v2` — allocate offensive pool across top-K
  scored offensive POIs (enemy income, enemy SR circle, base center), min axis
  size, goal-guarded refresh. Enemy SR *pressure* objective = park inside the
  10-cell contestation circle (SR doc) rather than attack-move onto the
  indestructible building.
- **Files:** new `Traits/BotModules/PoiOffensiveBotModule.cs`; `ai.yaml` wire
  `enable-ai-v2`; extend `PoiMap` with enemy/offensive POIs + SR-circle POIs.
- **Test:** autotest / scripted skirmish — assert the army splits across ≥2
  axes (no single cell holds the whole pool) and at least one axis targets a
  non-base POI. Use `/poi` + `/frontline` overlays for the visual check.
- **Ships:** the death-ball becomes multi-axis. This is the behaviour the user
  asked for in part 2.

#### Phase 2 FINDINGS (2026-07-19) — PoiMap shipped; capture-steal root-caused + fixed

**Delivered this session (Path A):**
- **PoiMap** (`Traits/World/PoiMap.cs`, ~330 LOC incl. pure `PoiScoring`): discovers
  income capturables + neutral/enemy SRs, scores per-player by value×distance×threat
  (threat sampled from `InfluenceMap`). Pure scoring separated (v3-portable, like
  `GoalGuardLedger`). Exposes `GetScoredPois` / `GetCaptureTargets` with a per-POI
  `PoiAction`+score so Phase 3 axis assignment consumes it without rework.
- `CaptureCoordinatorBotModule` now takes capture-target ORDERING from PoiMap
  (legacy scan kept as fallback). Live-verified: `targets=6, top=bio score=…`,
  order issued off PoiMap score, correct value-weighting (BIO $150 > near OILB $50).
- **Observation harness** (`test-v2-poi-harness`) + **capture assertion**
  (`test-v2-poi-capture`, PASS) + 14 NUnit `PoiScoring` cases (suite 243 green).

**MAJOR finding #1 — corrects the Phase 0 death-ball diagnosis.** Phase 0
concluded "v2 has no offensive brain." **That was wrong.** `SquadManagerBotModule`
is *not* air-only — `FindNewUnits` recruits EVERY `IPositionable` unit not in
`ExcludeFromSquadsTypes` and files non-air/non-naval ones into GROUND attack
squads. The `@{fac}.fixedwing` SquadManagers run for v2 (`enable-ai-any`) with an
EMPTY exclude list, so **they ARE v2's ground offensive brain** — they form ground
squads from the whole pool and attack-move them at the enemy. The death-ball is
this SquadManager, not an absence of one.
- **Implication for Phase 3 (offense):** `PoiOffensiveBotModule` does NOT slot into
  a vacuum — it must COMPETE WITH or REPLACE the fixed-wing SquadManager's ground
  behaviour. Options: (a) broaden the v2 SquadManager's `ExcludeFromSquadsTypes` to
  hand the ground pool to a PoiMap-scored axis module, or (b) drive the ground
  SquadManager's targets from PoiMap. Decide before building the offense module.

**MAJOR finding #2 — v2 capture NEVER completed (now fixed).** The harness exposed
that a committed TECN's `CaptureActor` was consistently replaced by the fixed-wing
SquadManager's ground-squad AttackMove — the TECN marched at the enemy *past* the
target and never captured, even close/safe/uncontested. The goal-guard is
INTRA-module (stops CaptureCoordinator re-thrash) but did not stop this INTER-module
steal — exactly the unit-claim gap §5.6 flags. **Fix (v2-only):** re-gated the
fixed-wing SquadManagers to `enable-ai-legacy-only` + added `enable-ai-v2` variants
that exclude the capturer + non-combat support (`tecn/e6/truk`). Capture now runs
to completion (verified: close derrick captured ~20s, `activity=CaptureActor`,
`commitN=1`). Normal/Rush/Turtle byte-identical.
- **Implication for Phase 3:** a *general* unit-claim mechanism is still wanted —
  `ExcludeFromSquadsTypes` is a blunt per-type exclusion; offense/capture/defense
  contention over the same *general* units (not just TECNs) needs the goal-guard
  (or `BotBlackboard`) consulted by every module, per §5.6.

**Finding #3 — the SR is NOT capturable today.** `SUPPLYROUTE` has no
`CaptureManager`/`Capturable` (contradicts §3.3's "yes (engineer chain)"). PoiMap
still DISCOVERS + SCORES neutral/enemy SRs as deny/pressure POIs (the capture
consumer harmlessly skips a non-capturable target), so Phase 3's SR *pressure*
objective is ready. But decision #2's "capture → neutralize → hold" needs a
`CaptureManager` added to `SUPPLYROUTE` first — a game-model change (own the
Neutral-on-capture semantics) to schedule before Phase 3 SR work.

**Note — TECN limit 3:** not observed as a bottleneck now that capture completes
reliably; left as-is (⚠️ revisit if Phase 3 wants more concurrent captures).

#### Phase 3 FINDINGS (2026-07-19) — spread offense SHIPPED; death-ball replaced

**Delivered (Path A, v2-gated):**
- **PoiOffensiveBotModule** + pure **PoiOffenseMath** (`Traits/BotModules/
  PoiOffensiveBotModule.cs`, ~330 LOC). Reads `PoiMap.GetOffensiveTargets`
  (new), opens score-floating axes via `DesiredAxisCount` → sticky top-k
  (hysteresis) → `AllocateProportional`, issues one AttackMove per axis, and
  commits every unit through the shared `PoiGoalGuard.Ledger`. All constants are
  Info fields (YAML-tunable). Emits `[v2-offense]` reeval/axis/order/retire lines.
- **PoiMap.GetOffensiveTargets** — enemy-owned POIs as army objectives (enemy
  income = Attack, enemy SR = Pressure), scored value×distance×threat from own
  SR. Added `PoiAction.Attack`; enemy SR reclassified Deny → Pressure.
- 15 NUnit `PoiOffenseMath` cases; full suite **258 green** (was 243).

**Unit-claim conflict (Phase 2 finding #1) — RESOLVED with a shared claim.**
The fixed-wing SquadManager was v2's ground brain (the death-ball). Fix: new
engine `SquadManagerBotModuleInfo.IgnoreGroundUnits` (default false → legacy
byte-identical); both v2 fixed-wing managers set it true, so they keep only air
squads and the ground pool is handed to PoiOffensiveBotModule. The **single
`PoiGoalGuard.Ledger` is the shared blackboard** (minimal §5.6): capture commits
"capture:<id>", offense commits "offense:<id>", each module skips anyone-committed
units; CaptureCoordinator escort/defender recruit now also skips committed units.
No two modules fight over a unit.

**Live proof (1 run, `test-v2-poi-harness`, PASS).** Extended the harness to a
25-unit offensive pool + 3 enemy POIs. Log shows the spread: 2 axes at pool=20,
a 3rd opened as the pool grew to 25. Three concurrent axes, proportional by score:
`fcom` (21.7M) → 11 units, enemy `supplyroute` Pressure (12.96M) → 7, `oilb`
(10.85M) → 7; `free=0` (all claimed). Stable ticks 784–1084 (hysteresis, no
thrash). **Decision #3 confirmed live:** the enemy fcom OUTSCORES the enemy SR —
the base is not privileged; the derrick pulls the biggest axis.

**Phase 4 hooks:** LayeredDefence + Garrison are NOT yet ledger-aware — pre-contact
offense owns the pool cleanly, but once a frontline forms they may contend. Phase 4
(defense/garrisons) should route those modules through the same ledger and add the
"neutralize + hold SR with a small garrison" behaviour (needs SR CaptureManager,
still absent — finding #3). TECN limit 3 unchanged.

### Phase 4 — Defend held POIs (medium) [next]
- **Do:** promote every owned POI to a garrison target with a minimum garrison
  sized by POI value; reuse + tighten `CaptureCoordinator`'s defense pass;
  optionally place a defensive structure (`gtwr`/`pbox`) near high-value held
  POIs via the existing `BaseBuilder` defense queue.
- **Files:** edit `CaptureCoordinatorBotModule.cs` (defense pass reads `PoiMap`
  ownership + value); possibly a small `PoiGarrisonBotModule`; `ai.yaml`.
- **Test:** autotest — capture a derrick, spawn an enemy raid, assert defenders
  are summoned and the derrick survives / is recaptured.
- **Ships:** captured income is actually held, closing the loop.

### Phase 5 (optional / later) — Chokepoints via `TerrainCache`
- Chokepoint detection needs the un-built `TerrainCache` (adjacency analysis at
  map load, `03_substrate.md §4.4`). Defer unless chokepoint holding proves
  necessary after Phases 2-3. Flagged so scope is explicit, not forgotten.

Rough total for Phases 0-3: ~3-5 focused sessions, 2 new world/player traits,
1-2 new bot modules, edits to `CaptureCoordinatorBotModule`, plus autotests.
All gated `enable-ai-v2`; **no engine-fork, no normal-AI change.**

---

## 7. Risks / open questions

**For the user (ALL THREE DECIDED 2026-07-19 — see "Approved decisions" at top):**
1. ~~The §4 fork~~ → **Path A** (bolt onto v2 + goal-guard). ✅ DECIDED.
2. ~~Neutral SR capture behaviour~~ → **deny-only is permanent design**; the SR
   flipping to Neutral on capture is *intended* (cut the enemy's reinforcements),
   not a bug to fix. No capture-to-own-side work, ever. Later: neutralize + hold
   with a small garrison. ✅ DECIDED.
3. ~~How aggressive should the spread be?~~ → **fully score-floating axes**, no
   guaranteed base-center axis; early passive games accepted. ✅ DECIDED.

**Technical risks I'll manage:**
4. **Engine `CaptureManager`/capture pathing quality is unknown at scale** — the
   legacy module's random-target flaw is documented; our income-weighting is
   proven in code but the *pathfinding* to distant contested POIs may still
   strand TECNs. Phase 0's goal-guard + staging mitigates but doesn't eliminate.
5. **Deterministic AI testing is hard.** Autotests can assert "order issued /
   POI captured within N ticks" and read `[v2-poi]` logs, but emergent
   spread-behaviour is fuzzy. I'll lean on single-POI deterministic tests per
   phase + overlay screenshots for the fuzzy multi-axis check, and will **not**
   run multi-test sweeps without an explicit goahead.
6. **`IsIdle` inheritance.** Any new module that reuses `IsIdle` re-imports the
   bug class. The goal-guard is the mitigation; I'll avoid `IsIdle` as an
   availability signal in new code.
7. **The death-ball root cause is only partly confirmed from code** (§3.2) —
   Phase 0's diagnostic run pins it down before I build on an assumption.

---

## 8. Files this plan would touch (summary)

New:
- `engine/OpenRA.Mods.Common/Traits/World/PoiMap.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/PoiOffensiveBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/PoiGoalGuard.cs`
- (optional) `PoiGarrisonBotModule.cs`, a `/poi` diagnostics trait
- autotests under `tools/autotest/` per phase

Edited:
- `engine/OpenRA.Mods.Common/Traits/BotModules/CaptureCoordinatorBotModule.cs`
- `mods/ww3mod/rules/ai/ai.yaml` (v2 wiring + `supplyroute` capturable)
- `mods/ww3mod/rules/world.yaml` (register `PoiMap`)

Untouched (control): all Normal / Rush / Turtle wiring, engine core, other mods.

---

## 9. Alignment notes

- Consistent with `03_substrate.md §9` — POI scoring logic is designed to port
  into the v3 brain; only the assignment mechanism (IBotTick module vs brain
  method) differs by fork.
- Reuses, does not replace, `InfluenceMap` / `FrontlineOverlay` (both listed as
  "keep" in the v3 non-negotiables).
- Prior AI plans indexed in `WORKSPACE/ai/README.md`; this plan slots as the
  first *behaviour* deliverable, whichever fork is chosen.
