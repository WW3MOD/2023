# TECN Availability — Cycle 2 RECON (design-only, no code/builds/runs)

**Date:** 2026-07-20 · **Researched against:** `main @ b6a43460` (ahead 80 of origin,
0 behind upstream, tree clean). Read-only recon; deliverable is this doc + one commit.

**Premise (from cycle-1, N=10 markers — `runs/260720_capture_reliability_cycle1_n10.md`):**
capture rate stuck 4/10 vs bar ≥6/10. Pooled over 994 `no-idle-capturers` (M-2) scans,
**88% saw `total-tecns=0`; 5/10 matches fielded ZERO TECNs the entire match.** When a TECN
exists the coordinator captures promptly and holds (conditional gross $6,377 ≥ bar). **The
binding constraint is TECN production/availability, upstream of all coordinator logic.**
This recon finds the exact starve point and the smallest fix.

---

## 1. Root-cause trace: "bot has budget" → "TECN on map", and where it starves

### 1.1 Who produces the experimental bot's TECN

The experimental bot does **not** have its own ground unit-builder. Ground production
(including TECN) comes from the **shared** `UnitBuilderBotModule@america.normal` /
`@russia.normal` (`ai-america.yaml:3-4`, `ai-russia.yaml:3-4`), gated
`RequiresCondition: enable-ai-player && player.<faction>`. `enable-ai-player` is granted to
**normal, experimental, stable** (`ai.yaml:52-54`). So the *same* builder config drives the
Normal control AND experimental AND stable. This is the blast-radius trap: **you cannot retune
the TECN weight/limit in `ai-*.yaml` without also changing the frozen Normal control.**

TECN is buildable: `TECN.america`/`TECN.russia` inherit `^TECN`
(`infantry-america.yaml:109`, `infantry-russia.yaml:109`), Buildable `Queue: Infantry`,
`BuildAtProductionType: Soldier`, `Prerequisites: ~player.<faction>, ~techlevel.infonly`
(early tech, satisfiable in-match — TECNs *do* appear 12% of scans, so the prereq is not the
gate), `Valued.Cost: 250` (`infantry.yaml:2171-2178`). Generic `^TECN` itself is
`Prerequisites: ~disabled` (`infantry.yaml:2175`) — only the faction variants build.

### 1.2 The production loop (`UnitBuilderBotModule.cs`)

`IBotTick.BotTick` (`:78-97`) runs every `FeedbackTime = 30` ticks (`:49`):

1. **Queued-request path FIRST** (`:87-92`): pops one `queuedBuildRequests` entry and calls
   the single-name `BuildUnit(bot, name)` overload. **Note the entry is removed whether or not
   the build succeeds** (`:91` runs unconditionally after `:90`) — a request silently dropped
   if the queue was busy that cycle.
2. **Lottery path** per queue (`:94-95`): `BuildUnit(bot, q, buildRandom)` with
   `buildRandom = idleUnitCount < IdleBaseUnitsMaximum` (default **12**, `:25`).

**The lottery gives weight 500 no priority whatsoever.** Two sub-paths, both ≈uniform for TECN:

- **Weighted** (`ChooseUnitToBuild`, `:177-195`): shuffles `UnitsToBuild` (`:188`) and returns
  the **first** entry that is buildable in this queue AND satisfies the share test
  `count(unit)*100 < weight*total` (`:190`). Rearranged that is `count/total < weight/100`, i.e.
  weight/100 is a **share *ceiling* as a percent**. For `tecn:500` the ceiling is **500%** —
  it can never bind (a fraction is ≤1). So weight 500 makes TECN merely "always eligible,"
  **identical eligibility to every other early-game infantry**, and the shuffle+first-match
  picks uniformly among eligibles. TECN ≈ 1/(≈11 infantry entries) per infantry opportunity.
- **Random** (`ChooseRandomUnitToBuild`, `:167-175`, active while `idleBaseUnits < 12` — the
  experimental bot's *usual* state, since PoiOffensive/LayeredDefence/MountedTransport pull
  units forward off the base the moment they arrive): picks uniformly from the player's full
  buildable-infantry set, and if the pick isn't in `UnitsToBuild` the cycle is wasted
  (`:125-126`). Effective TECN rate ≤ the weighted path.

**Throughput throttle:** `BuildUnit` only starts a unit if the Infantry queue has **nothing
queued** (`FirstOrDefault(q => !q.AllQueued().Any())`, `:112`). One infantry at a time. So
per-match TECN attempts ≈ (cycles where the Infantry queue happens to be idle) × (~1/11), and
the queue is usually busy churning combat infantry. High variance on a small count ⇒ a whole
match can roll zero TECN. **That is the mechanical explanation of the 5/10 zero-TECN matches.**

### 1.3 Two compounding factors

- **`ConsumedByCapture: true`** (`infantry.yaml:903`, on `^CapturesNeutralBuildings` which
  `^TECN` inherits at `infantry.yaml:2164`): every *successful* capture removes the TECN. The
  pool can never accumulate — each capture returns the count to a state that needs another
  lottery win. Availability is a leaky bucket with a random, throttled inflow.
- **No demand feedback.** `CaptureCoordinatorBotModule.cs` never calls `RequestUnitProduction`
  (verified: it is *not* among the 5 files implementing/consuming `IBotRequestUnitProduction`;
  only `UnitBuilder`, `AdaptiveProduction`, `McvManager`, `Harvester` do). At the M-2 branch
  (`CaptureCoordinatorBotModule.cs:241-253`) the coordinator *knows* `total-tecns=0` and returns
  — it has the information "I have derricks to take and zero capturers" but exerts **zero pull**
  on production. The only production signal for TECN is the blind lottery in §1.2.

### 1.4 Why YAML-only cannot fix this

`UnitBuilderBotModuleInfo` exposes only `UnitsToBuild` (share *ceiling*), `UnitLimits`
(*ceiling*), `UnitDelays` (delay). **There is no "floor", "minimum", or "priority" field.**
Raising `tecn:500`→higher is a no-op (already non-binding). Lowering *other* units' weights
would throttle the whole army to force TECN shuffles — unacceptable and it hits the Normal
control anyway (§1.1). **A guaranteed keep-N-ready floor is not expressible in YAML; it needs
code.** (This is the load-bearing finding — logged to DISCOVERIES.)

---

## 2. Minimal mechanism — a keep-N-ready TECN floor via demand request

The clean seam is the **`IBotRequestUnitProduction` queue** the engine already provides.
`AdaptiveProductionBotModule.cs` is the working reference:
- resolves `IBotRequestUnitProduction[]` via `TraitsImplementing` in `Created` (`:62-65`),
- guards over-requesting with `RequestedProductionCount` (`:153-154`),
- calls `up.RequestUnitProduction(bot, name)` (`:157-162`).

A queued request is processed **first** each 30-tick cycle (`UnitBuilderBotModule.cs:87-92`)
and the single-name `BuildUnit` overload (`:142-165`) **bypasses the `UnitsToBuild` share test
AND `UnitLimits`** — it only needs the Infantry queue idle. So a request out-competes the
lottery for the queue slot whenever the queue is free. Re-requesting each coordinator scan
(every `ScanInterval=75` ticks) keeps pressure despite the drop-on-failure at `:91`.

### Recommended: extend `CaptureCoordinatorBotModule` (smallest diff, default-off)

The coordinator already owns the TECN pool and counts it: `capturingActors.Actors`
(`CaptureCoordinatorBotModule.cs:133,150,246`). Add:

- **Info field** `public readonly int TecnFloor = 0;` (default **0 = disabled**). Because the
  behavior is gated by this YAML *value*, the shared engine class is safe: set `TecnFloor: 1`
  **only on `@experimental.tecn`**; `@stable.tecn` omits it ⇒ 0 ⇒ byte-identical behavior to
  today. Controls use `CaptureManagerBotModule` (`ai.yaml:101,360`), which has no such field
  and is never touched.
- **Lazy resolve** (mirroring the existing `goalGuard`/`poiMap` lazy pattern, no `Created`
  override needed): `IBotRequestUnitProduction[] unitProducers`; `string tecnBuildType`
  resolved once by intersecting `Info.CapturingActorTypes` with the player's Infantry-queue
  `BuildableItems()` names (so nato→`tecn.america`, brics→`tecn.russia`, no hardcoding, and a
  wrong-faction request — which production would reject on prereqs anyway — can't happen).
- **Each capture scan**, if `TecnFloor > 0`:
  `alive = capturingActors.Actors.Count;`
  `pending = unitProducers.Sum(u => u.RequestedProductionCount(bot, tecnBuildType));`
  if `alive + pending < TecnFloor` and `tecnBuildType != null` → one `RequestUnitProduction`.
  (Counting `pending` prevents piling requests while one walks in. Floor 1–2 stays under the
  `UnitLimit tecn:3`, so bypassing the limit in the request path is harmless.)

**Rough size:** ~25–40 LOC in one existing file + 1 YAML line. YAML-only option: **none exists**
(§1.4).

**Floor value:** start **`TecnFloor: 1`** (keep ≥1 alive-or-pending). Cycle-1 shows one TECN
captures promptly, and with `ConsumedByCapture` the floor auto-re-requests after each consume.
If the N=5 diagnostic shows a gap between consume and arrival (edge→SR walk latency) still
zeroing the pool between captures, bump to **2**. Floor is the *single knob* this cycle.

### Alternative: new `TecnReserveBotModule` gated `enable-ai-experimental` only

Cleaner blast radius (new file, no shared-class edit, stable literally has no such trait so it
*cannot* be affected), self-contained ~80–120 LOC + wiring. Rejected as the primary only
because it duplicates the pool-count/type-resolve the coordinator already does. Keep as fallback
if reviewers prefer zero edits to the shared coordinator class.

---

## 3. Structural option — step toward roadmap item 3 (reinforcement packaging)

Roadmap item 3 = call-ins as **mission-tied combined-arms packages**. A capture *package* would
bundle `{1 TECN + EscortSize escorts}` as one call-in created **when a capture mission is
opened**, not left to a lottery. That solves availability at the root: TECN is produced because
a mission demands it, and it arrives with its escort.

The floor in §2 is the minimal *unconditional* version. The **first real step toward packaging**
is to make the request **demand-gated**: fire it precisely at the M-2 branch
(`CaptureCoordinatorBotModule.cs:241`) — "capturable targets exist ∧ no free capturer ⇒ request
a TECN (and optionally pre-stage the escort)." This is strictly better than a blind floor
because it never spends 250 budget on a TECN when there is nothing to capture, and it is the
natural seam that later grows into full packaging (attach the escort call-in alongside the TECN
request). Recommendation: ship the **unconditional floor as v1** for this one-behavior cycle
(simplest, lowest variance), and record demand-gating + escort-bundling as the **immediate next
structural increment** — it reuses the exact same request plumbing.

---

## 4. Risks & validation

### Risks

| Risk | Assessment / mitigation |
|---|---|
| **Shared `@poi` singleton trap** (PoiGoalGuard / MountedTransport are single-instance, fetched via `TraitOrDefault<T>()`) | **Not touched.** New request logic lives in the per-bot `CaptureCoordinatorBotModule` (multi-safe; `@experimental.tecn` and `@stable.tecn` are separate instances on different players). `unitProducers` resolved via `TraitsImplementing` (multi-safe), exactly like AdaptiveProduction. |
| **Shared engine class blast radius** (CaptureCoordinator instantiated by experimental **and** stable) | Neutralized by `TecnFloor` **default 0**; set the value **only** on `@experimental.tecn` (`ai.yaml:131`). `@stable.tecn` (`ai.yaml:620`) omits it → frozen. Controls use `CaptureManagerBotModule` → untouched. Per SPEC §13, sharing @stable *code* is acceptable; we keep stable's *value* frozen. |
| **MiniYaml blank-line merge** | The new `TecnFloor: 1` is a **child line inside** the existing `@experimental.tecn` block — do **not** introduce a blank line within the block; keep the blank-line separators between top-level trait entries intact. |
| **One-behavior-per-cycle scope** | This cycle changes **only** TECN availability (production request). Do NOT also touch targeting, escort size, TTL, or any unit stat. `TecnFloor` is the only new knob. |
| **Over-production** (request path bypasses `UnitLimits`) | Cap at floor and subtract `pending` via `RequestedProductionCount`; floor 1–2 < limit 3. Cost 250 each — cheap. |
| **Wrong-faction request** | `tecnBuildType` resolved from the player's own Infantry `BuildableItems()`; a stray wrong-faction name would be rejected by prereqs regardless. |

### Validation plan

1. **Build + unit tests:** `make.ps1 all` green; `dotnet test … OpenRA.Test` unchanged (287
   pass — additive logic removes no tests).
2. **N=5 diagnostic gate** (same as cycle-1: `tournament-s1-eco-river-zeta` +`-mirror`,
   `tournament-eco-5min.yaml`, 300s, hidden). Read the pooled M-2 `total-tecns` distribution
   from per-match `debug.log` (already preserved on `main @ b6a43460`): expect the **88%-zero
   share to drop sharply** and **≥1 TECN fielded in ≥4/5 matches**. Gate to proceed: capture
   **≥3/5**.
3. **N=10 verify vs bar:** capture **≥6/10 AND** conditional gross median **≥$5,000**. Markers
   M-1/M-2/M-3 + per-match `debug.log` are already on main for legibility.
4. **Autotest discipline (hard rule):** one `run-test.sh` at a time for the bug at hand; the
   N=5/N=10 batches (`run-tournament.sh`) require an explicit go-ahead in the turn that runs them.

---

## 5. Implementation checklist (exact targets)

1. **`engine/OpenRA.Mods.Common/Traits/BotModules/CaptureCoordinatorBotModule.cs`**
   - In `CaptureCoordinatorBotModuleInfo`, after `DefenseEnemyValueThreshold` (`:96-97`), add:
     `[Desc("Keep at least this many owned capturers (TECN) alive-or-pending by requesting production. 0 = disabled.")] public readonly int TecnFloor = 0;`
   - In `CaptureCoordinatorBotModule`, add fields: `IBotRequestUnitProduction[] unitProducers;`
     `string tecnBuildType; bool tecnResolved;` (resolve lazily like `goalGuard`/`poiMap`).
   - In `BotTick` (after the `goalGuard` lazy-resolve block, `:179-183`), when `Info.TecnFloor > 0`,
     lazily resolve `unitProducers` (`player.PlayerActor.TraitsImplementing<IBotRequestUnitProduction>()`)
     and `tecnBuildType` (first name in `Info.CapturingActorTypes` present in the player's
     Infantry-queue `BuildableItems()`), then call a new `MaintainTecnFloor(bot)`.
   - `MaintainTecnFloor`: `alive = capturingActors.Actors.Count;`
     `pending = unitProducers.Sum(u => u.RequestedProductionCount(bot, tecnBuildType));`
     `if (tecnBuildType != null && alive + pending < Info.TecnFloor) unitProducers[0].RequestUnitProduction(bot, tecnBuildType);`
     Add a `Log.Write("debug", "[exp-capture] tecn-floor-request …")` marker for legibility.
2. **`mods/ww3mod/rules/ai/ai.yaml`** — inside `CaptureCoordinatorBotModule@experimental.tecn`
   (block at `:131-162`), add a single child line `TecnFloor: 1`. **Do NOT** add it to
   `CaptureCoordinatorBotModule@stable.tecn` (`:620`). No blank line inside the block.
3. **No edits** to `ai-america.yaml` / `ai-russia.yaml` (shared UnitBuilder + controls) or any
   unit-stat YAML.
4. Build (`make.ps1 all`) + `dotnet test`.
5. Autotest N=5 gate → N=10 vs bar (explicit go-ahead required per hard rule).

---

### Appendix — key citations (all `main @ b6a43460`)

- Shared builder gate: `ai-america.yaml:3-4`, `ai.yaml:52-54` (enable-ai-player→normal/exp/stable).
- Lottery mechanics: `UnitBuilderBotModule.cs` — BotTick `:78-97`, request-first `:87-92`,
  `buildRandom` `:95` + `IdleBaseUnitsMaximum:25`, empty-queue gate `:112`, share test `:190`,
  single-name overload bypasses limits `:142-165`.
- Coordinator: no request path (grep), M-2 branch `CaptureCoordinatorBotModule.cs:241-253`,
  pool index `:133,150`.
- Request reference: `AdaptiveProductionBotModule.cs:62-65,153-162`.
- Consumable: `infantry.yaml:903` (`ConsumedByCapture`), `^TECN` inherits it `:2164`, cost/queue
  `:2171-2178`. Faction buildables `infantry-america.yaml:109`, `infantry-russia.yaml:109`.
- Interface: `TraitsInterfaces.cs:727-732`.
