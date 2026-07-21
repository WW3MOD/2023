# 260722 — Bot Brain Architecture: Can We Keep the OpenRA Style?

**Mode:** EXPERIMENTAL (research/design assessment — doc only, no code changes)
**Question (owner):** "Can we really keep using the OpenRA style of handling the bots?" — specifically for three target behaviors: (a) find and exploit undefended/weakly-held map points, (b) multi-squad coordination with staging/meetup before assault, (c) synchronized/timed attacks including multi-axis pincers.
**Scope extensions (owner, mid-task):** (A) role/capability awareness — "the bots don't really know what different units are good at"; (B) human-like command model — decide what deserves focus, issue orders one at a time as persistent commitments instead of per-tick/fixed-interval re-issuing.
**Inputs:** code inventory with file:line verification (Part 1); industry survey with sources (Part 2); ratified split SPEC (`260722_strategic_tactical_split_SPEC.md`), mission-abstraction costing (`260720_mission_abstraction_costing.md`), RETHINK #2 record (`ai-bench/reports/260721_rethink2.md`), territorial offense-bias plan (`260721_terr_offense_bias.md`).
**Researched against:** main @ `0fce8bbd`.

---

## Executive summary — verdict first

**EXTEND, do not restructure.** Keep L1 (the PoiMap/InfluenceMap scoring substrate and the goal-guard ledger) and L3 (shared stance micro) exactly as the ratified split SPEC defines them. **Replace the interior of L2** — today a set of independent, timer-driven, greedy dispatch loops — **with an operations/task-force layer**: a persistent `Operation` object with participating forces, staging points, a launch condition, and an abort condition. This is the one abstraction every surveyed industry system has and we lack. It is additive: it slots into the existing module chassis (IBotTick modules, YAML config, profile gating) and reuses the already-costed Mission model from `260720_mission_abstraction_costing.md` as its seed.

The two scope extensions sharpen the verdict rather than change it:

- **Role/capability awareness (A):** confirmed absent. Unit selection everywhere is hand-maintained YAML type-string lists plus trait-presence checks; `ai.yaml:349` lumps artillery (`m109`, `paladin`, `grad`, `tos`, `m270`) and SHORAD (`strykershorad`, `tunguska`) into `MainLineUnitTypes` alongside tanks and rifle infantry. A minimal role model (derived from traits, YAML-overridable) is a **prerequisite** for the operations layer — combined-arms composition cannot be expressed without it — and is cheap (~1 day). It is worth doing under any verdict.
- **Human-like command model (B):** the near-term engineering win is **event-driven commitment revision** — orders persist until an event (contact, arrival, sighting, losses) warrants revisiting, replacing the timer-driven re-decide default. Part 1.6 shows the codebase already contains **at least six distinct anti-oscillation mechanisms** (ledger TTLs, assign cooldowns, sticky-selection thresholds, repath gates, regroup timeouts, re-fire intervals) that exist to suppress churn *created by* timer re-decides; industry precedent (F.E.A.R.'s plans-persist-until-invalidated, Killzone 2's squad feedback loop) says invert the default instead of patching it. The **attention-scheduler commander** (utility-scored event queue, pop one, make one deliberate decision, optional decisions-per-minute budget) is feasible, deterministic, and a natural difficulty knob — but it belongs **on top of** the operations layer, not before it, because without operations there is nothing durable for a scheduled decision to commit to.

**Answer to the owner's question, precisely:** the OpenRA *chassis* (bot modules as conditional traits, YAML-configured, ticked by the sim) is fine and worth keeping — it is deterministic, profile-gatable, and benchmark-friendly. The OpenRA *idiom* — every module independently re-scanning on its own timer and re-issuing greedy immediate orders — cannot express any of the three target behaviors and should be retired from the maneuver path. Keep the chassis, replace the idiom.

**RETHINK #2 status:** the mission abstraction was deferred on 2026-07-21 with an explicit revival rule — "when the territorial layer needs first-class retryable assault/garrison missions." The owner's target behaviors (b) and (c) *are* that need. The rule is now met; this doc is the revival, extended with staging/synchronization, role composition, and the command-model findings.

**Target-behavior scorecard (today → with operations layer):**

| Behavior | Today | Blocker | After |
|---|---|---|---|
| (a) exploit weak points | Partial (4/10) — PoiMap scores threat, but omnisciently, and only *at* POIs, never gaps between them | Fog-honest sightings (SPEC Phase 4); frontline-gap targeting | 8/10 |
| (b) staging/meetup | Absent (1/10) — no rendezvous primitive anywhere in the codebase | Operation staging state + ready condition | 8/10 |
| (c) synchronized/pincer | Absent (2/10) — closest is a 60%-random same-target squad split with no timing | Operation launch condition over multiple forces | 8/10 |

---

## Part 1 — Inventory: what the bot brain is today

All claims verified against code on main @ `0fce8bbd`; file:line cited throughout. Two sub-inventories (stock squad system; experimental Poi stack) were produced by scoped read-only agents and spot-verified directly (`SquadManagerBotModule.cs:232-380`, `PoiOffensiveBotModule.cs:400-437` read personally and matched).

### 1.1 The three-layer reality

The ratified split SPEC names L1 (strategic bot modules — intent), L2 (squad FSMs — grouped orders), L3 (shared stance micro — execution). In code today:

- **L1** = the Poi stack modules' *scoring* halves: `PoiMap` (world trait) discovers and scores POIs every 50 ticks (`PoiMap.cs:162`, staggered via SharedRandom `:187`), exposing `GetScoredPois` (`:234-253`), `GetCaptureTargets` (`:257`), `GetOffensiveTargets` (`:279-351`), `GetDefendTargets` (`:362-404`). `InfluenceMap` (CellSize=2, UpdateInterval=25, `InfluenceMap.cs:35`) provides `GetFriendlyInfluence`/`GetEnemyInfluence` (`:143-166`) and `GetFrontline` (`:170-175`). Both are **omniscient** (`PoiMap.SampleThreat` `:481-498` reads all actors regardless of fog) — the SPEC's Phase 4 migrates this to fog-honest intel.
- **L2** = two parallel dispatch systems (below) that convert scores into unit orders.
- **L3** = stance-level micro on the units themselves (out of scope here; contract ratified in the SPEC).

### 1.2 Stock squad system (SquadManagerBotModule + FSM)

- Cadences: `AssignRolesInterval=50` (`SquadManagerBotModule.cs:58`), `RushInterval=600` (`:61`), `AttackForceInterval=75` (`:64`), `SquadSize=8` (`:52`) + `SquadSizeRandomBonus=30`. The squad update loop re-fires every 75 ticks: `if (--attackForceTicks <= 0) { ... foreach (var s in Squads) s.Update(); }` (`:254-259`).
- FSM states in `GroundStates.cs`: Idle (`:31-99`), AttackMove (`:101-182`), Attack (`:184-258`), Flee (`:260-297`, WW3MOD threat-map retreat), Regroup (`:299-381`, WW3MOD addition: 750-tick timeout, 70% cohesion threshold). The grouped AttackMove order is **re-issued every state tick** (`GroundStates.cs:161, :174`) — i.e., every 75 ticks, the same order again.
- The **only multi-axis behavior in the codebase**: `CreateAttackForce` (`SquadManagerBotModule.cs:330-369`) rolls 60% on LocalRandom, asks `threatMap.FindAttackTargets(Player, 2, 12)` for two targets, and splits into two squads with `ApproachWaypoint = targets[0]/targets[1]`. Same decision tick, no rendezvous, no launch timing, no completion feedback — the squads never know about each other again. Default path is one squad (`:372-380`).
- On @experimental and @stable profiles, `IgnoreGroundUnits=true` (`:302-309`) — ground units are left unclaimed for the Poi stack; SquadManager runs **air squads only** (`AirUnitsTypes` per faction, `ai.yaml:533/551/599/611`).
- Units never transfer between squads; a squad is a bag of actors with one FSM.

### 1.3 Experimental Poi stack (the current L2 interior)

- **PoiGoalGuard ledger** (`PoiGoalGuard.cs`) — the one real cross-module coordination primitive. String objective keys (`capture:<id>` / `offense:<id>` / `defend:<id>`), TTL-based: `Commit` (`:60-77`, re-commit extends TTL), `IsCommitted` (`:81`), `TryGetObjective` (`:84-94`), `Release` (`:100`), `Prune` (`:104-116`). `DefaultCommitmentTicks=300` in code (`:129`), 600 in `ai.yaml:126`. Shared singleton gated on `enable-ai-experimental || enable-ai-stable` (`ai.yaml:121`). **Note what it is:** a mutual-exclusion lock with a lease, not a plan. It records *that* an objective is taken, not *what the plan is*, *who participates*, or *when anything should happen*.
- **PoiOffensiveBotModule** — `Reevaluate` every 100 ticks (`:55`, body `:179-334`). Builds a free pool by filtering ledger-committed units (`:403-407`), forms up to `MaxAxes=4` axes of `UnitsPerAxis=8`/`MinAxisSize=3` (`Axis` class `:124-135`), sticky target selection with `ReassignScoreThresholdPct=30` (`:363-396`), `CommitAndOrder` (`:473-550`) commits `offense:` keys with `AxisCommitmentTicks=250` (`:69`), sets cohesion Spread/Tight at `AssaultRadiusCells=15`, re-paths only if target moved > `RepathThresholdCells=3`. SR-contestation via `SrPressureScoreMultiplier=260` (`ai.yaml:210`). Axes are **independent greedy dispatches**: each axis picks its own best POI and marches; there is no relationship between axes, no wait, no combined launch.
- **PoiGarrisonBotModule** — mirrors offense with `defend:` keys; garrison size = POI value/50 clamped 1–3; `MaxGarrisons=4`.
- **CaptureCoordinatorBotModule** — `ScanInterval=75` (`ai.yaml:139`), `DefenseScanInterval=150`; dispatches TECN + escort (`DispatchEscort` `:667-683`); escort recruiting checks the ledger (`:762`) but **escorts are never themselves committed** (`:486-502` — known bug, costing doc §1.3), so other modules can steal them mid-escort. TECN ferry via `TryReserveCaptureFerry` (`:554/:567`) → `MountedTransportBotModule.cs:136-192`. `MaintainTecnFloor` (`:395-420`, `TecnFloor=1` `ai.yaml:165`) is the reference demand-queue production pattern.
- **LayeredDefenceBotModule** — `ScanInterval=75`, `AssignCooldownTicks=250`; `AssignPositions` (`:222-389`) scores slots (−FriendlyGapWeight×friendly − EnemyWeaknessWeight×enemy), snaps screens to cover, mainline to an 8-cell standoff toward the SR. Checks `transport.IsPassengerReserved` (`:283`) — a coordination check done *outside* the ledger, ad hoc.
- **MountedTransportBotModule** — `ScanInterval=50`; `CarrierTask` FSM Loading→Delivering→Unloading→Returning (`AdvanceTask:252-351`). This is the **only per-task persistent object with lifecycle in the whole bot** — a single-unit proto-operation. `DeliverBeforeContact=true` + `PreContactStagingPct=50` (experimental-only) is the only "staging" concept in the codebase, and it stages one transport, not a force. (@stable keeps `UnloadOnArrival=false`, a knowingly-frozen no-op documented at `:82-86`, `:294-298`.)
- **UnitBuilderBotModule** — `UnitsToBuild` weights are share *ceilings* via shuffle-lottery (`ChooseUnitToBuild`, `UnitBuilderBotModule.cs:177-195`); guaranteed production requires the `IBotRequestUnitProduction` demand queue (`:90-91`, `RequestUnitProduction:99`, `FeedbackTime=30`). Nothing connects production to operational need beyond the TECN floor.

### 1.4 Coordination-primitives audit — the direct answer

Explicitly, as of main @ `0fce8bbd`:

1. **Cross-squad/cross-force coordination primitive: NONE.** The closest artifacts: (i) the 60%-random two-squad split (§1.2) — same target list, same tick, no further relationship; (ii) the goal-guard ledger — prevents two modules grabbing one objective, but cannot express two forces *cooperating on* one objective.
2. **Persistent plan/operation object with preconditions: NONE.** The mission abstraction was designed and costed (`260720_mission_abstraction_costing.md`: `MissionKind`, lifecycle Staging→Executing→Retrying→Complete/Aborted, `MissionBoard` placement rules) but **deferred by RETHINK #2** (`260721_rethink2.md`: "its own decision rule is not met... while the territorial layer moves the bars that are actually stuck"). The only lifecycle object in shipped code is `CarrierTask` (one transport).
3. **Timing/synchronization/rendezvous mechanism: NONE.** No wait-until, no ready-check, no synchronized launch, no rendezvous point anywhere. `Regroup` (`GroundStates.cs:299-381`) is *intra*-squad cohesion recovery, not inter-force meetup. `PreContactStagingPct` stages a single transport.
4. **Two parallel coordination substrates, one half-dead.** `BotBlackboard.cs` carries a full task-market API — `PostTask/ClaimTask/UpdateTaskStatus/GetOpenTasks` (`:137-191`), task types AttackArea/DefendArea/Scout/Capture/SupplyRun/Retreat/Garrison, `TaskStaleTicks=1500` — with **zero callers** (verified by grep across all bot modules). Only its `ClaimUnit` (`:195-239`) and `PostIntel/GetIntel` (`:244-274`) channels are used, by five legacy support modules (HelicopterSquad `:162`, Garrison `:156`, Scout `:147/:275-282`, SupplyFollower `:140`, AdaptiveProduction `:93-95`). The maneuver stack (SquadManager, PoiOffensive, PoiGarrison, LayeredDefence, CaptureCoordinator) uses the PoiGoalGuard ledger instead and never touches the blackboard. Someone already felt the need for a task abstraction, built the API, and never wired the maneuver side to it.

### 1.5 Role/capability awareness audit (scope extension A)

The owner's observation — "the bots don't really know what different units are good at" — is **confirmed against code**. There is no capability or role model anywhere; every module answers "which units?" with one of two crude mechanisms:

- **Hand-maintained type-string lists in YAML**, matched by actor name: `ScreenUnitTypes` (`LayeredDefenceBotModule.cs:52`, list at `ai.yaml:346`), `MainLineUnitTypes` (`:56`, list at `ai.yaml:349`), name matching at `:175-176`, role check at `:265-266`; `CapturingActorTypes` (`CaptureCoordinatorBotModule.cs:35`), `SupportingUnitTypes` (`:79`); `ExcludeFromSquadsTypes` (`SquadManagerBotModule.cs:34`, `ai.yaml:557/617`), `AirUnitsTypes`/`NavalUnitsTypes` (`:388`).
- **Trait-presence eligibility**: `IsEligibleCombatUnit` (`PoiOffensiveBotModule.cs:410-426`) = has `IPositionableInfo` + `AttackBaseInfo`, is not Aircraft, name not in `ExcludeUnitTypes` (`:425`). That's it — a unit is "combat" or not.

The concrete cost, visible in one line: **`ai.yaml:349` puts `m109`, `paladin`, `grad`, `tos`, `m270` (tube/rocket artillery) and `strykershorad`, `tunguska` (SHORAD) in `MainLineUnitTypes`** alongside tanks and rifle infantry. The defence module will slot artillery into a front-line standoff position and SHORAD into an infantry line; the offense module will march artillery in an assault axis like a tank. The bot literally cannot know that artillery wants standoff range, SHORAD wants to overwatch the force, or recon wants to be ahead of it. Every axis today is a homogeneous "8 combat units" scoop (`UnitsPerAxis=8`), whatever happens to be free.

Maintenance cost compounds it: seven-plus per-module lists (per faction for some) that silently rot when units are added — a new unit is invisible to the AI until someone remembers every list.

### 1.6 The timer cadence map — and the machinery that fights it

Every decision loop in the brain is timer-driven:

| Loop | Cadence (ticks) |
|---|---|
| InfluenceMap update | 25 |
| PoiMap discovery | 50 |
| SquadManager role assign / MountedTransport scan | 50 |
| Squad FSM re-fire / CaptureCoordinator scan / LayeredDefence scan | 75 |
| ThreatMapManager update | 90 |
| PoiOffensive reevaluate | 100 |
| CaptureCoordinator defense scan | 150 |

And these are the mechanisms whose primary job is to stop those timers from thrashing their own decisions:

1. Ledger TTLs (`AxisCommitmentTicks=250`, `DefaultCommitmentTicks` 300/600)
2. `AssignCooldownTicks=250` (LayeredDefence)
3. Sticky selection `ReassignScoreThresholdPct=30` (PoiOffensive)
4. `RepathThresholdCells=3` (don't re-order unless the target moved)
5. Regroup timeout 750 ticks / 70% threshold (squad FSM)
6. The 75-tick re-fire interval itself, sized so grouped orders survive between re-issues (SPEC Phase 2 contract: "L3 survives 75-tick re-fire without oscillating")

Six mechanisms, one root cause: **the default is "re-decide on schedule," so every layer needs dampers.** This is the evidence base for Part 2.8/4.7 — event-driven revision inverts the default (persist until an event says otherwise) and most of the damper machinery becomes unnecessary rather than tuned.

---

## Part 2 — Industry survey: how coordinated operational AI is actually built

Selection criteria: systems with published detail, spanning platoon-based (SupCom), planner-based (KZ2 HTN, F.E.A.R. GOAP), squad-role (CoH), scripted/built-in (AoE2, SC2), open-source lockstep cousin (Spring/BAR), and utility-hybrid (IAUS). Sources listed in the appendix.

### 2.1 Supreme Commander — the platoon as first-class object

GPG's SupCom AI (and its long-lived community successors: Sorian AI, FAF's M27/M28) is built on **platoons**: engine-level objects with a *template* (unit categories + counts + formation) and a *plan* (a Lua behavior function owning the platoon's lifecycle). **Platoon formers gather units at a rally point until the template is satisfied, and only then does the plan start** — staging→launch is the core loop, not an advanced feature. Builders (production) are condition-gated and feed platoons by template need, closing the production↔operations loop we lack (§1.3 UnitBuilder). Fifteen-plus years of community AI work (Sorian, M27) kept the platoon abstraction and rewrote everything around it — evidence that the abstraction is the durable part.

### 2.2 Killzone 2/3 — three-layer HTN with feedback

Guerrilla's KZ2 AI (Straatman/Verweij/Champandard) is the cleanest published match for our split SPEC: **strategy layer** (commander assigns squads objectives on a territory graph) → **squad layer** (HTN planner turns an objective into ordered moves: advance along route, coordinated fire-and-maneuver) → **individual layer** (per-agent tactics). Two details matter for us: (1) squads **replan ~2×/second, but against a persistent plan** — replanning refines the current plan rather than re-deciding the objective; (2) there is an explicit **upward feedback channel — "order failed"** — so the commander revises only when execution reports it must. Our L2 has no plan to refine and no failure channel; every re-scan is a fresh greedy decision.

### 2.3 F.E.A.R. — plans persist until invalidated

Orkin's GOAP (GDC 2006) is individual-scale, but its transferable principle is the one we need: **a plan is a commitment that persists until a world-state change invalidates it.** Replanning is event-triggered (a precondition became false), not scheduled. F.E.A.R.'s celebrated squad behavior emerged from simple coordination atop that persistence. This is the direct industry precedent for scope-extension B.1.

### 2.4 Company of Heroes — stable roles inside a force

CoH squads assign members **stable internal formation roles** (core / left flank / right flank) that persist across moves — members don't reshuffle every order. Precedent for Part 4.5's rule that role assignment within an operation is done once at composition time, not re-derived every scan (contrast: our free-pool rebuild every 100 ticks).

### 2.5 Built-in bots: AoE2 DE and SC2

- **AoE2 (DE)**: rule-based (`defrule` + strategic numbers + `attack-now`), town-size attack grouping. This is the same architectural family as stock OpenRA — timer/rule-driven greedy dispatch — and its known ceiling is exactly our ceiling: attacks are streams, not operations; no staging, no synchronization. Useful as the negative pole of the survey.
- **SC2 built-in AI**: even Blizzard's non-ML editor AI expresses offense as **composed, scheduled attack waves** (editor-configured composition + timing + personality). The "wave" — a composed force launched as a unit at a chosen time — is the minimum viable operation object, and even the simplest modern commercial RTS bot has it.

### 2.6 Spring/BAR — CircuitAI: task hierarchy in a lockstep engine

CircuitAI (rlcevg; the `barbarian` branch is BAR's BARb) is the closest engine-cousin proof: an open-source, **lockstep-deterministic** Spring RTS AI built on a **task hierarchy** (`IUnitTask` / fighter/builder task families). Tasks own their units until completion or failure; attack tasks gate on **threat-map-vs-own-power comparisons** from JSON config (`behaviour.json`) before committing. It demonstrates that persistent task objects with ownership and preconditions work fine under lockstep determinism — the constraint that rules out per-client planners rules nothing out here.

### 2.7 Utility commander + influence maps (IAUS)

Dave Mark's Infinite-Axis Utility System, paired with influence maps, is the standard published pattern for the *strategic* layer: continuously score candidate actions/targets on multiple axes, with **"decision momentum"** — a hysteresis bonus to the currently-committed choice. Our L1 already is this (PoiMap multi-axis scoring; `ReassignScoreThresholdPct=30` *is* decision momentum). The survey validates keeping L1 unchanged: it is the one layer where we already match industry practice.

### 2.8 The human-like command model (scope extension B)

The owner's idea: the bot should decide **what deserves focus**, then issue orders **one at a time as persistent commitments**. Assessed seriously, this decomposes into two separable pieces with different maturity:

**(1) Event-driven commitment revision — strong precedent, near-term.** F.E.A.R. (§2.3) and KZ2 (§2.2) both ship the pattern: commitments persist; events (precondition invalidated, order failed, contact made) trigger revision. Our Part 1.6 table is the diagnosis: WW3MOD's oscillation/module-fighting pathology is substantially *downstream of timer-driven re-decides* — we built six damper mechanisms to protect decisions from our own schedulers. Inverting the default (persist until event) removes the root cause instead of tuning the dampers. This piece does not require the operations layer and can retrofit onto existing modules (Part 4.7).

**(2) Attention-scheduler commander — real but thinner precedent, later.** The pattern: a utility-scored priority queue of focus-worthy events; each think, pop the top item, make **one deliberate decision**; optionally cap decisions per unit time (an attention/APM budget). Precedents, honestly weighted:
- **AlphaStar** is the strong one: DeepMind deliberately imposed **~22 non-camera agent actions per 5-second window** and camera-based perception so the agent had to *choose what to attend to* — explicit human-attention modeling, and the agent stayed superhuman at strategy while order-issuing was human-scale. Proof that a decision-rate budget forces prioritization without destroying competence.
- **SC2 AI Arena (bot league) — corrected premise:** the community bot ladder does **not** enforce human-like APM caps; its ~120k figure is a technical stability ceiling only. Bot leagues are *not* precedent for attention budgets; AlphaStar and the utility-commander literature are. (Recorded honestly because the original hypothesis expected league caps.)
- **IAUS commanders** (§2.7) naturally produce "one best decision per evaluation" — the scheduler is an ordering discipline on top of utility scoring, not a new theory.

Verdict on B: **piece (1) is an engineering win we should take early; piece (2) is architecturally sound but needs something durable to decide *about*** — popping an event and issuing one more greedy AttackMove changes nothing; popping an event and revising a persistent Operation is a real command decision. Hence the phasing in Part 4.8. The attention budget doubles as a **difficulty knob** (fewer decisions/minute = a slower, more human commander) — a knob current WW3MOD difficulty tiers lack.

### 2.9 What transfers to lockstep OpenRA

The determinism filter (SharedRandom only; no per-client sim state; deterministic iteration) **excludes nothing surveyed**. HTN/GOAP planners, platoon formers, task hierarchies, utility queues — all are pure sim-side computation; CircuitAI proves it in a lockstep cousin engine. What the filter *does* demand: event queues ordered by (tick, priority, ActorID) tie-breaks; SharedRandom for stochastic choices; no wall-clock. The reason to prefer the operations layer over a full HTN planner is **cost and continuity** (Part 3.4), not feasibility.

---

## Part 3 — Gap analysis and verdict

### 3.1 Scoring the three target behaviors

**(a) Determine undefended/weakly-held points and exploit them — PARTIAL (4/10).**
Exists: PoiMap scores every POI with threat sampling (`SampleThreat`, `PoiMap.cs:481-498`) and biases (`OffensiveIncomeSecureBias=150`, `OffensiveEnemyAttackBias=80`, `:279-351`); the deferred-but-planned BoP bias (`260721_terr_offense_bias.md`) sharpens this. Missing: (i) it's **omniscient** — "weakly held" is read from ground truth, not from scouting, so the behavior can't look like reconnaissance-driven exploitation (SPEC Phase 4 fixes the substrate); (ii) it only evaluates *POIs* — the bot cannot notice an undefended **gap between** POIs or a weak frontline sector, even though `InfluenceMap.GetFrontline` (`:170-175`) already computes the data; (iii) exploitation is a single greedy axis, so a discovered weakness gets 8 units, never a composed force.

**(b) Multi-squad coordination — staging and meeting up before assault — ABSENT (1/10).**
Nothing in the codebase can express "force A and force B assemble at X, then go." No rendezvous point, no ready condition, no inter-force reference. The one point awarded is for `CarrierTask` + `PreContactStagingPct` proving the lifecycle/staging *pattern* already compiles and runs deterministically in this engine — for one transport.

**(c) Synchronized/timed attacks, pincers on multiple axes — ABSENT (2/10).**
The 60%-random squad split (`SquadManagerBotModule.cs:330-369`) sends two squads at two targets *on the same tick*, which is coincidence, not synchronization: no shared launch condition, no waiting for both to be ready, no abort if one dies. PoiOffensive's `MaxAxes=4` are four independent greedy errands. A pincer — two forces staging on distinct approach corridors and launching together — is inexpressible.

### 3.2 The role-awareness gap

Part 1.5 established there is no capability model. Consequence chain: no roles → no combined-arms composition → operations (if built) would still scoop "8 nearest combat units" → artillery charges, SHORAD strays, recon sits in line. **Role metadata is therefore on the critical path for the operations layer**, not an optional polish. It also pays off immediately outside operations: LayeredDefence slotting artillery correctly is a bug fix in itself.

### 3.3 The command-model gap

Part 1.6's six-dampers-one-cause analysis quantifies the owner's instinct: the modules "fight" and oscillate because each re-decides on its own clock against a world the others just changed. The ledger, cooldowns, and stickiness thresholds are *treatments*; event-driven revision is the *cure*. Notably, the split SPEC's Phase 2 contract ("L3 survives 75-tick re-fire without oscillating") is itself a damper requirement that shrinks to near-nothing once orders stop being re-fired on schedule.

### 3.4 Verdict: EXTEND — argued

**Restructure** would mean discarding the module chassis for a ground-up planner architecture (full HTN/GOAP over all bot behavior). **Extend** means keeping L1 + L3 + the chassis, and replacing the L2 interior with an operations layer. Extend wins on five arguments:

1. **The failure is one missing abstraction, not a rotten foundation.** Every surveyed system that achieves behaviors (a)–(c) has exactly one owning object between strategy and units — platoon (SupCom), squad-order/HTN plan (KZ2), task (CircuitAI), wave (SC2). Our L1 scoring already matches industry practice (§2.7); our L3 contract is ratified and sound. The gap is precisely the object in the middle.
2. **Determinism is not the discriminator.** §2.9: everything transfers. So the choice is economic, and a restructure re-derives L1/L3 value we already have.
3. **Benchmark continuity.** The ai-bench governance (@experimental vs frozen @stable, priced promotions, re-baselines) depends on incremental, kill-switchable changes. An operations layer ships default-off behind one flag (Part 4.10); a restructure would invalidate the control and the ladder for months.
4. **The mission abstraction is already designed and costed.** `260720_mission_abstraction_costing.md` gives the Operation seed (MissionKind, lifecycle, MissionBoard placement rules, ledger layering §2.4, the 90%-duplication table §1.2 showing offense/garrison collapse into one mechanism). RETHINK #2's revival rule — "when the territorial layer needs first-class retryable assault/garrison missions" — is met by the owner's own target list.
5. **Both scope extensions are extend-shaped.** Event-driven revision retrofits onto existing modules (no restructure needed to get the win); the attention scheduler is a pacing layer *on top of* operations; the role model is metadata + a resolver, consumable by old and new code alike.

**What "keeping the OpenRA style" means after this verdict:** keep — bot modules as conditional traits, YAML tunables, profile gating, `IBotRequestUnitProduction`, the ledger as the mutual-exclusion substrate, PoiMap/InfluenceMap as the world model. Retire from the maneuver path — per-timer greedy re-decides, per-module type lists, order re-issuing as a keep-alive. The stock SquadManager FSM remains untouched for legacy profiles and air squads (until an operations phase absorbs air).

---

## Part 4 — Operations layer: design sketch

Everything below honors the ratified L1/L2/L3 contract: L1 proposes and scores; the operations layer (new L2 interior) owns commitment and execution management; L3 owns within-leash execution. Default-off, @experimental only, one kill-switch flag.

### 4.1 The Operation object

```csharp
enum OperationKind { Assault, Pincer, Raid, GarrisonRelief, CaptureEscort, Probe }
enum OperationState { Proposed, Staging, Launched, Resolved, Aborted }

class Operation
{
    int Id;                       // per-player deterministic sequence
    OperationKind Kind;
    string ObjectiveKey;          // REUSES ledger grammar: "offense:<poiId>", "defend:<poiId>", ...
    CPos Objective;
    List<TaskForce> Forces;       // 1..MaxAxes
    OperationState State;
    int ProposedTick;
    int StagingDeadlineTick;      // anti-deadlock: launch or abort by this tick
    // Launch condition: all forces Ready, OR deadline hit with >= MinForcesReady
    // Abort conditions: objective invalid; force strength below AbortStrengthPct
    //                   of composition; TTL expired
}

class TaskForce
{
    Dictionary<UnitRole, int> Composition;  // role -> requested count
    List<uint> ActorIds;                    // assigned actors (ActorID, deterministic order)
    CPos StagingPoint;
    bool Ready;                             // >= ReadyPct of force within StagingRadius
}
```

Deliberately reuses: the ledger key grammar (costing doc §2.4 layering mandate — the Operation *is* the committer of its key, TTL refreshed while alive); the costed Mission lifecycle (Staging→Executing→Retrying maps to Staging→Launched→re-Proposed); `CarrierTask` as the in-repo lifecycle precedent. Placement per costing-doc rule R-1: an `OperationsBotModule` per bot player (never a single-instance world lookup — the shared-@poi trap).

### 4.2 Lifecycle

- **Proposed** — created by a proposal source (4.3), utility-scored against other proposals and against staying idle. Top proposal(s) accepted up to `MaxConcurrentOperations`.
- **Staging** — objective key committed to the ledger (suppression begins, 4.4); units recruited by role (4.5) and claimed; each force ordered (grouped move, L3 handles execution) to its staging point. Staging points chosen on the friendly side of `InfluenceMap.GetFrontline`, minimum threat sample, within `StagingStandoffCells` of the objective approach.
- **Launched** — when the launch condition fires (all forces Ready, or deadline with ≥ MinForcesReady — SupCom's former-gathers-then-plan-starts). All forces get their assault orders **on the same tick**: that single property is what makes (c) expressible. Fires roles take standoff positions; recon leads (4.5).
- **Resolved** — objective condition met (POI captured / threat cleared / garrison relieved). Ledger key released, units released back to pools.
- **Aborted** — strength below `AbortStrengthPct`, objective invalidated, or TTL. Units released; a Retry proposal may be emitted with a cooldown (retryable missions — the exact RETHINK-revival need).

State transitions happen only in the module's tick, evaluated in deterministic order (operations sorted by Id).

### 4.3 Proposal sources (L1 → operations)

- **PoiMap scores** (existing `GetOffensiveTargets`/`GetDefendTargets`) — Assault/GarrisonRelief proposals, inheriting the BoP bias (`260721_terr_offense_bias.md`) when it lands.
- **Sighting staleness** (Phase 4 intel substrate) — Probe/Raid proposals toward stale sectors; this is what turns behavior (a) from omniscient cheating into recon-driven exploitation.
- **Frontline gaps** — `InfluenceMap.GetFrontline` sectors with low enemy influence and no POI → Raid proposals (the "gap between POIs" miss in 3.1a).
- **CaptureCoordinator** — CaptureEscort proposals, absorbing the escort flow and fixing the never-committed-escort bug (`CaptureCoordinatorBotModule.cs:486-502`) structurally: escorts become force members, claimed like everyone else.

### 4.4 Ledger integration and squad suppression

- Operation commits its `ObjectiveKey` on entering Staging; PoiOffensive/PoiGarrison already skip ledger-committed objectives (`BuildFreePool`, `PoiOffensiveBotModule.cs:403-407`) — **suppression is free**, no changes to those modules needed while they coexist.
- Unit ownership: extend `PoiGoalGuard` with unit-level claims (Commit/Release by ActorID) rather than adopting `BotBlackboard.ClaimUnit` — one substrate for the maneuver stack; the blackboard's intel channel stays for legacy modules; its dead task API gets deleted, not adopted (it stores no lifecycle, no composition, no conditions — the Operation supersedes it).
- End state (a later phase): PoiOffensive's axis interior (`Reevaluate`/`CommitAndOrder`) is *replaced by* Assault operations; PoiOffensive shrinks to a proposal source. Until then both run, ledger-separated.

### 4.5 Role-based composition (scope extension A)

Minimal role model — one enum, one resolver, one override field:

```
UnitRole: Recon | MainLine | AntiArmor | Fires | AirDefence | Transport | Capture
```

- **Derivation (default):** from traits/armament already in YAML — `CaptureManager`+capture types → Capture; `Cargo` capacity → Transport; longest `Armament` range > FiresRangeThreshold → Fires; AA-only armaments → AirDefence; high speed + `RevealsShroud` range → Recon; AT-dominant armament → AntiArmor; else MainLine.
- **Override (YAML):** an `AiUnitRole` field on the actor (single line per unit) for cases derivation gets wrong. Derive-then-override gives new units a sane default (fixing the silent-rot problem of §1.5) while keeping curation possible.
- **Consumption:** `TaskForce.Composition` requests roles (`Assault ≈ {Recon:1, MainLine:6, AntiArmor:2, Fires:2, AirDefence:1}` per axis, YAML-tunable per kind); recruitment fills roles from the free pool by (role match, distance, ActorID) deterministic ordering. During Launched, roles shape orders: Fires hold `FiresStandoffCells` behind the force; Recon screens `ReconLeadCells` ahead; AirDefence stays with the main body. This — not more micro — is what makes an assault read as combined-arms.
- **Immediate side payoff:** the resolver replaces `MainLineUnitTypes`/`ScreenUnitTypes` name lists in LayeredDefence, curing the `ai.yaml:349` artillery/SHORAD conflation with no operations dependency.
- **Production link:** unmet `Composition` after recruitment emits `IBotRequestUnitProduction` requests (the `MaintainTecnFloor` pattern, `CaptureCoordinatorBotModule.cs:395-420`) — closing SupCom's builder↔platoon loop.

### 4.6 Expressing the pincer

`Kind=Pincer`, two+ forces, distinct approach corridors: corridor selection picks staging points on different sides of the objective (minimum angular separation `PincerMinAngle`, threat-sampled corridors via InfluenceMap). Launch condition = **both forces Ready** (deadline fallback demotes to single-axis Assault rather than deadlocking). Same-tick launch + separated approach vectors = a real pincer, six YAML tunables, no planner required.

### 4.7 Event-driven commitment revision (scope extension B.1 — the near-term win)

A small deterministic event bus, deliverable **before** the operations layer:

- `BotEvent { int Tick; EventKind Kind; int Priority; uint SubjectActorId; CPos Where; }` — kinds: ContactMade, ForceArrived, UnitLostThreshold (≥N% of a committed group), NewSighting, ObjectiveLost, ProductionComplete, OrderFailed.
- Sources are existing sim notifications (`INotifyDamage`, `INotifyKilled`, arrival = leash/radius checks already computed by L3/cohesion) — no new sensing.
- Queue ordered by `(Tick, Priority, SubjectActorId)` — fully deterministic; any stochastic tie uses SharedRandom.
- **Retrofit, not rewrite:** existing modules keep their scan timers as *heartbeats* (fallback, e.g. 10× current interval) but gate real re-decides on a dirty flag set by relevant events. PoiOffensive re-evaluates an axis when the axis makes contact, loses units, or its target changes hands — not every 100 ticks. Expected effect per §1.6/§3.3: the damper constants stop being load-bearing; measured effect is an ai-bench question (4.10).
- KZ2's upward channel comes with it: an OrderFailed event (force shattered, path blocked) is what lets an Operation retry or abort *for a reason* instead of on TTL.

### 4.8 Attention-scheduler commander (scope extension B.2 — later, on top)

Once operations exist, the commander loop becomes: score pending events + proposals with the existing utility machinery (PoiMap axes + decision momentum) → **pop one** → make one deliberate decision (accept/abort/revise an operation, retask a force) → spend one attention token. `DecisionsPerMinute` is the budget — and a **difficulty knob**: Easy ≈ 4 (slow, exploitable commander), Brutal ≈ 20 (sharp but still not omnipresent). AlphaStar precedent says budgets force prioritization without collapsing competence; SC2 bot leagues explicitly do *not* provide precedent (no human-like caps there — honesty note from §2.8). Not started until operations prove out: one deliberate decision needs a durable object to decide about.

### 4.9 Determinism notes

- All state in per-player bot-module fields; nothing keyed on RenderPlayer/LocalPlayer; no wall clock.
- SharedRandom only for new stochastic choices (per split-SPEC rule); note the stock 60% split uses LocalRandom — now lobby-seeded (`World.cs:213-214`, architecture.md:311-313) but still outside the sync hash; new code does not copy that pattern.
- Deterministic iteration everywhere: operations by Id, forces by index, actors by ActorID, events by (Tick, Priority, ActorID). No Dictionary iteration order reaches decisions.
- Same-tick multi-force launch is trivially safe: orders issued within one module tick.

### 4.10 Benchmarking and governance

- `OperationsEnabled=false` default; flipped on @experimental only (the `CohesionSwitchEnabled` pattern, `PoiOffensiveBotModule.cs:87/:424`). Event-bus retrofit gets its own flag (`EventDrivenRevision=false`) so the two levers are priced separately.
- Priced before promotion per ladder governance: S1/S2 vs frozen @stable, with the 260721 re-baseline lessons applied — batch-validity gate (≥6/10 engaged), mirrored spawns, name-keyed parsing. The re-baseline's headline ("Exp ≈ Stable on both rungs; the next cycle needs a genuinely different improvement") is exactly the opening operations are for: it is a different axis, not a bigger constant.
- Telemetry tag `[ops]` (mirroring `[exp-terr]`): operation lifecycle transitions, staging durations, launch-condition outcomes (all-ready vs deadline), abort reasons — enough to debug watchability claims from the bot-vs-bot observer work.

### 4.11 Phasing against the split SPEC (Phases 2–5)

| SPEC phase | Operations-layer work riding along |
|---|---|
| Phase 2 (L2 grouped orders/cohesion) | Unchanged; its "survive 75-tick re-fire" contract becomes belt-and-braces once 4.7 lands |
| Phase 3 | **Event bus + event-driven revision retrofit** (4.7) — independent engineering win, own flag, priced alone; role resolver (4.5) can land here too (LayeredDefence payoff needs no operations) |
| Phase 4 (intel substrate / fog migration + scout link) | **Prerequisite for honest operations**: sighting staleness feeds Probe/Raid proposals; weak-point detection stops being omniscient. Operation *skeleton* (object + lifecycle + single-force Assault) can develop against omniscient PoiMap in parallel, but promotion waits for fog-honest scoring |
| Phase 5 | **Operations layer proper**: multi-force, staging, Pincer, role composition consuming Phase-4 intel; PoiOffensive axis interior retired to proposal source. Attention scheduler (4.8) opens as the phase after, only if operations price positively |

Effort, anchored to the existing costing (which priced board+lifecycle at 1–1.5d and migration at 2–3d): event bus + retrofit ~1–2d; role resolver ~1d; operation skeleton (single-force) ~1.5–2d; staging/sync/pincer ~1–2d; PoiOffensive interior migration ~2–3d. Each step independently priceable on the ladder.

---

## Appendix — Part 2 sources

- SupCom platoons: Sorian AI mod (GitHub), FAF "What Makes AI" wiki, M27AI devlog (FAF forums)
- Killzone 2/3 HTN: Verweij master's thesis (guerrilla-games.com), Straatman et al., "Hierarchical AI for Multiplayer Bots in Killzone 3" (Game AI Pro, ch. 29)
- F.E.A.R. GOAP: Orkin, "Three States and a Plan" (GDC 2006 paper)
- CoH squad formation roles: Relic squad-formation paper (core/left/right flank slots)
- AoE2 DE scripting: airef.github.io AI-scripting encyclopedia (defrule/attack-now/SN)
- SC2 built-in AI: SC2Mapster AI-module docs (attack waves, personalities)
- Spring/BAR: CircuitAI (github.com/rlcevg, `barbarian` branch), BAR BARb behaviour docs
- Utility commander: Dave Mark, IAUS (GDC Vault "Architecture Tricks: Managing Behaviors in Time, Space, and Depth"; gameai.com)
- AlphaStar constraints: DeepMind AlphaStar blog + Vinyals et al., Nature 2019 (22 non-camera actions per 5s, camera interface)
- SC2 AI Arena APM: aiarena.net wiki (no human-like cap; ~120k technical ceiling)
