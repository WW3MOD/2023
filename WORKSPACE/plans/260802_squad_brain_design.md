# 260802 — Squad-Mission "Brain": architecture design

**Mode:** EXPERIMENTAL (design doc — no engine/YAML changes; no simulations run)
**Revision 2** — folds in the adversarial review (reviewed against `main @ 7df21413`; 8 FIX + 7 NOTE items). All file:line refs re-verified against `main @ 660a0ee2`. Two things moved under this revision and are folded in: (a) the `auto/stable-0802` parity merge rewrote `ai.yaml` (every yaml ref re-cited; `InfluenceStack.Participates` now admits `BotType=="stable"` too, `InfluenceStack.cs:48`); (b) **`auto/mission-commitment` MERGED to main mid-revision** (`1fec5070`, merge `6aff93c3`) — it implements this doc's Phase 1a (LayeredDefence ledger lock, flag name `RespectCommitmentLedger` exactly as specified) and a Phase-1b-equivalent axis hold (`MissionCommitmentMath` + `PartitionHeldAxes`), so §1.1/§1.2/§5-Phase-1/open-question-1 are updated from "planned" to "landed + residual gaps". Original research was at `main @ 493f76a4`.
**Researched against:** `main @ 660a0ee2` (the offense module has moved substantially since the `0fce8bbd` inventory in [`260722_bot_brain_architecture.md`](260722_bot_brain_architecture.md) — refs here are current).
**Problem (owner, live play):** units "get orders in steady intervals and go one way for one second, then stop and go the other way, and they get stuck in loops." Wanted: "a central **Brain** that decides the vectors of attack and the POI to defend and attack etc, based on the enemy movements and the Influence maplayer, and then coordinates the squads accordingly" — squads go on **missions** and aren't touched mid-mission without good reason.
**Folds in:** PIPELINE item 18 ("Should I attack?" endgame posture layer) — designed here as the Brain's top-level output, not a separate item.

**Relationship to prior design work.** [`260722_bot_brain_architecture.md`](260722_bot_brain_architecture.md) already ratified the *verdict* (EXTEND the chassis; add a persistent operations/task-force object between strategy and units; keep L1 scoring + L3 micro) and surveyed industry precedent. This document is its **code-anchored engineering successor**: a concrete per-player Brain module, the exact order-source forensics at current HEAD that justify it, a precise mission state machine + abort-trigger set, an executor-by-executor migration, and a phase plan whose Phase 1 aligns with the in-flight `auto/mission-commitment` branch. Where the two disagree on naming, this doc's `Mission`/`Brain` supersede the older `Operation`/`TaskForce` sketch (§4 there); the lifecycle is otherwise the same shape.

---

## Executive summary

The dithering is **not** one bug. It is the sum of several independent order sources ticking on their own clocks against a shared unit pool, plus one module that re-shuffles its *own* units every evaluation as jittering scores move the proportional allocation. Concretely:

1. **The `@experimental` ground pool is written by five-to-six modules that do not fully share a lock.** `PoiOffensiveBotModule` (100-tick), `PoiGarrisonBotModule` (100-tick) and `CaptureCoordinatorBotModule` (75/150-tick) all respect the `PoiGoalGuard` ledger. `LayeredDefenceBotModule` was the worst ledger-blind writer — **fixed on main mid-revision** by `auto/mission-commitment`: it now skips ledger-committed units (`LayeredDefenceBotModule.cs:331`) behind `RespectCommitmentLedger` (`:121`, default off; on for `@experimental`, `ai.yaml:566`). **Two writers still never consult the ledger at all** (each verified: no `Ledger`/`IsCommitted` reference in the file): `MountedTransportBotModule` (orders combat infantry via `EnterTransport`, `:517`; carriers via `Stop`, `:513`), and `GarrisonBotModule@defenses` (live for BOTH bots via `enable-ai-any`, `ai.yaml:412-413`; `IsIdle` gate `:122`, `EnterTransport` `:152`). Whenever an offense unit idles for a moment, a ledger-blind writer can pull it into a transport/garrison; offense re-grabs it on its next eval — a multi-second "forward, get pulled back, forward" loop with no single owner.
2. **PoiOffensive re-allocates its own units every 100 ticks on scores that wobble every 25 ticks.** `RescaleByBelievedFields` recomputes each axis score from the control/danger fields (which recompute on a 25-tick cadence) every evaluation, `AllocateProportional` re-derives per-axis sizes from those scores, then the module **sheds the units farthest from each axis target** and **tops up with the nearest free units** (`PoiOffensiveBotModule.cs:593-635`). A unit near an axis boundary is repeatedly shed from axis A (it is "farthest") into the pool and recruited onto axis B whose target lies the other way — it literally walks back. The ledger TTL does not prevent this because the module releases the commit itself when it sheds (`:608`). **Partially fixed on main mid-revision:** `auto/mission-commitment` now partitions HELD axes out of the reshuffle (`PartitionHeldAxes`, `:766`; released only on `MissionCommitmentMath` triggers — `PoiGoalGuard.cs:138`; on for `@experimental`, `ai.yaml:227`), so a committed axis is no longer re-sized, shed, or re-ordered. Residual: axes not yet held (fresh this eval) still reshuffle on raw jittering scores, and the rival-beats-commitment trigger compares **raw** scores against a percent margin — exactly the comparison the review showed is defeated by bucket crossings (see §3.3 trigger 3 / Phase 1c).
3. **The heli FSM churns strategic targets through fast state-transition flapping, not a slow heartbeat.** *(Revision 2 — the original "75-tick stock-FSM heartbeat" claim was refuted by review: `GroundStates.cs`'s Stop/AttackMove regroup toggle `:158-161` is dead code on the current roster — all four `SquadManagerBotModule` instances are `.fixedwing` with `IgnoreGroundUnits: true` — and heli squads are ticked by `HelicopterSquadBotModule` at `SquadUpdateInterval=5` (`HelicopterSquadBotModule.cs:49`, countdown `:179-183`), not by SquadManager's 75-tick `AttackForceInterval`.)* The real mechanism: the heli FSM evaluates its transition predicates every **5 ticks**, and those predicates flap — `IsTargetTooHot` triggers a soft-target swap of `TargetActor` or a Withdraw (`HelicopterStates.cs:395-413`), target-invalid drops to Idle which re-picks closest-enemy (`:388-391` → `:352-361`), and the Flee/AA-spike/AttackRun boundaries (`:384`, `:431`, `:448`) each hand off to a state that can route back. A squad near a predicate boundary cycles Approach→Withdraw→Idle→Approach and swings between comparable targets at 5-tick granularity — no mission-level object pins the strategic target.

The fix the owner intuits — "a central Brain that assigns missions and doesn't touch squads without good reason" — is exactly the missing abstraction. This doc specifies:

- A **`SquadBrainBotModule`** (per bot player, `@experimental`-gated) that each **strategic tick** (default 100t) reads the influence stack and friendly state and emits four outputs: a **posture** (Attack / Hold / Consolidate — the item-18 decision, driven by an **Aggressiveness** scalar rather than a bot archetype), a ranked set of **attack vectors** (objective + approach corridor), a ranked set of **defend POIs**, and a **force allocation** (how many units, by role, to each). It also detects **granted opportunities** — undefended sectors with a free corridor forward — and pushes into them (opportunistic advance) so the bot exploits openings instead of freezing on its captures. It does not issue unit orders directly.
- A **tunable-parameter architecture** (§2.7): Aggressiveness and future sliders (risk tolerance, capture-vs-combat priority) are integer `Info` scalars that shift the pure decision math via a `base ± slope` convention — swept per-match by the test harness to find the baseline, eventually a lobby slider. No discrete "Rush/Turtle" personalities.
- A **`Mission`** object as the single durable thing a group of units is committed to: an objective, a route hint, a commitment window, a role composition, and a small **abort-trigger set**. Squads execute a mission and are **not re-tasked mid-mission** unless an abort trigger fires. The Brain owns the set of live missions; executors own within-mission movement.
- A migration that turns the existing modules into **pure executors** (given a mission, drive the units; report status up) and makes the Brain the **only** thing that decides *which units pursue what* — closing the multi-writer gap that causes (1).

Phase 1's core **landed on main during this revision**: `auto/mission-commitment` merged (`1fec5070`), delivering 1a (LayeredDefence honors the ledger) and a 1b-equivalent (held axes are neither shed nor re-ordered; release only on explicit triggers). That should kill the worst of the visible loop for `@experimental`. What remains of Phase 1 is 1c (score quantization — the landed rival-margin trigger still compares raw bucketed scores) and 1d (slider scaffolding). Later phases introduce the Brain and Mission objects and retire the per-module re-decide loops.

---

## 1. Current-state forensic map — every ground order source at `660a0ee2`

This section is evidence. Each row is a place that issues movement/retarget orders to combat units, its cadence, whether it respects the shared `PoiGoalGuard` ledger, and its specific contribution to the observed dithering.

### 1.0 Which modules touch ground units, per profile

From `ai.yaml` post `stable-0802` (every module is now a per-profile twin — `@stable` has full parity twins of every `@experimental` module; the four `SquadManagerBotModule` instances are all `.fixedwing` with `IgnoreGroundUnits: true`, `ai.yaml:727,785,1109,1122`, so **ground is owned by the Poi stack**, not `GroundStates.cs`):

| Module | Gate | Cadence | Reads ledger? | Order emitted |
|---|---|---|---|---|
| `PoiOffensiveBotModule@experimental` / `@stable` (`ai.yaml:201/:966`) | exp / stable | `ReevaluateInterval=100` (`:57`) | **Yes** (`BuildFreePool` `:878-890`, ledger check `:886`, commits `:972`) — but does **NOT** check `IsPassengerReserved`: it can yank infantry mid-boarding (LayeredDefence checks it, `LayeredDefenceBotModule.cs:324`) | grouped `AttackMove` per axis (`:1104-1108`) |
| `PoiGarrisonBotModule@experimental` / `@stable` (`ai.yaml:324/:1008`) | exp / stable | `ReevaluateInterval=100` (`:56`) | **Yes** (`:403`, commits `:460`) | grouped `AttackMove` per garrison (`:471`) |
| `CaptureCoordinatorBotModule@experimental.tecn` / `@stable.tecn` (`ai.yaml:94/:911`) | exp / stable | `ScanInterval=75`, `DefenseScanInterval=150` (`:41,:44`) | **Checks** (`:448,:467,:926`) but commits ONLY the capturer (`:950`) — escorts (`:1160`) and defenders (`:1257`) are recruited without a commit (see §4) | `CaptureActor` (`:947`), escort/defender `AttackMove` (`:1160,:1257`) |
| `LayeredDefenceBotModule@experimental` / `@stable` (`ai.yaml:552/:1085`) | exp / stable | `ScanInterval=75`, `AssignCooldownTicks=250` (`:44,:48`) | **Yes since `1fec5070`** — skips ledger-committed units (`:331`) behind `RespectCommitmentLedger` (`:121`, default off; on `@experimental`, `ai.yaml:566`; `@stable` twin omits it ⇒ still ledger-blind there). Checks only — never commits its own line assignments (§4 audit) | per-unit `AttackMove` to a line slot (`:424`) |
| `MountedTransportBotModule@poi` / `@experimental` (`ai.yaml:595/:629`) | stable / exp | `ScanInterval=50` | **NO** (no `Ledger` reference in file) — the `IsPassengerReserved` seam (`:155`) is its own bespoke lock, honored by LayeredDefence but not by offense | `Stop` to carrier (`:513`), `EnterTransport` to combat infantry (`:517`); capture-ferry path `Stop`/`EnterTransport` (`:209-210`) |
| `GarrisonBotModule@defenses` (`ai.yaml:412`) | **enable-ai-any — live for BOTH bots** (`:413`) | `ScanInterval=200` (yaml `:414`) | **NO** (no `Ledger` reference in file); gates on `IsIdle` (`:122`) | `EnterTransport` into defense structures (`:152`) |
| `HelicopterSquadBotModule@stable` / `@experimental` (+ `HelicopterStates.cs`, `ai.yaml:825/:845`) | stable / exp | **`SquadUpdateInterval=5`** (`HelicopterSquadBotModule.cs:49`, countdown `:179-183`) — NOT SquadManager's 75-tick interval | via air squad membership only | `AttackMove`/`Attack`/`ReturnToBase` |
| `SquadManagerBotModule@*.fixedwing` ×4 + `GroundStates.cs` (`ai.yaml:716,774,1101,1114`) | exp / stable | `AttackForceInterval=75` (`SquadManagerBotModule.cs:72`, tick `:264-266`) | `ExcludeTacticallyCommitted` (ledger-aware) | grouped `AttackMove` — **ground path is dead code on the current roster** (`IgnoreGroundUnits: true` on all four instances) |
| `LaneAmbushBotModule@experimental` / `@stable` (`ai.yaml:367/:1034`) | exp / stable | own countdown | **Yes** (commits `ambush:<id>`) | single `AttackMove` + stance |

The shared lock is `PoiGoalGuard.Ledger` (`PoiGoalGuard.cs`), a per-unit `Actor → {Objective string, ExpiresAtTick, CommitCount}` table (`GoalGuardLedger<Actor>`, `:39`). It records **that** a unit is taken and **until when** — nothing about the plan, the route, the group, or the composition (`Commitment` struct `:41`). `DefaultCommitmentTicks=300` (`:223`). Since `1fec5070` the file also hosts `MissionCommitmentMath` (`:138`) — the pure release-trigger predicate for held offense axes (objective invalid / danger spike / rival beats margin / combat-ineffective), a direct ancestor of this doc's §3.3 trigger set.

### 1.1 Root cause A — LayeredDefence was a ledger-blind second writer *(fixed on main by `1fec5070`, `@experimental` only)*

`LayeredDefenceBotModule.AssignPositions` iterates `world.Actors`, filtering on `actor.IsIdle` (`:282`), an actor-type/role eligibility test (`:285-309`), its own per-unit cooldown `assignedAtTick[actor] > cooldownExpiresBefore` (`:311`), an out-of-ammo guard (`:318`), and a transport-reservation check (`transport.IsPassengerReserved`, `:324`). It then issues `AttackMove` to a computed line slot (`:424`) and stamps `assignedAtTick[actor] = world.WorldTick` (`:425`).

It **never checked `goalGuard.Ledger.IsCommitted` — it now does** (`:331`, landed mid-revision): behind `RespectCommitmentLedger` (`:121`, default off; on for `@experimental` at `ai.yaml:566`) it skips any ledger-committed unit, exactly the Phase 1a fix this doc specified (same flag name). The `@stable` twin omits the flag and remains ledger-blind. The steal window the fix closes is narrower than "any idle offense unit" *(revision 2, per review)*: the **on-the-line damper** (`:334-345`) skips any unit within `OnLineRadiusCells=8` (`:64`) of a contested cell, so units idling *at* the front — including most units that just reached a contested objective — were already protected. The real steal window was **mid-route idles** (a unit that momentarily goes `IsIdle` en route, out of the 8-cell contested bubble — order interrupted, path blip, waiting between the offense module's 100-tick `Reevaluate`s) and **post-fight quiet zones** (an objective taken and no longer contested — the contested-cell set moves on, the damper stops covering the unit, and it idles there until re-eval). In those windows LayeredDefence (75-tick, phase-offset) re-tasked the unit toward the defensive line; offense's next eval re-ordered it forward. Period ≈ `max` of the two cadences with the two phase offsets — a several-second oscillation, matching the owner's "one way, then the other, stuck in a loop."

*Note:* `assignedAtTick` and `AssignCooldownTicks=250` damp LayeredDefence against *itself*, and the transport check is an ad-hoc cross-module lock done outside the ledger (`:324`) — evidence the author already needed a lock and reached for a bespoke one. The ledger is the right lock; the landed fix wires the *read* side. The *write* side is still missing — LayeredDefence never commits its own line assignments, so the reverse steal channel (offense grabbing line units) stays open until the §4 commit-on-order audit (see N6, §5).

### 1.2 Root cause B — PoiOffensive reshuffles its own units on jittering scores

`Reevaluate` (`PoiOffensiveBotModule.cs:423`) runs this every 100 ticks:

1. Score targets; when Stage-F repoint is on, `RescaleByBelievedFields(targets, tick)` (`:521`) recomputes each axis score from `ControlField.ScoreAt` (balance-of-power ring) and `DangerFieldLayer.GroundDanger` — **both of which recompute every 25 ticks** (influence-stack `UpdateInterval`). So axis scores wobble at 25-tick granularity even when the battlefield is static.
2. `AllocateProportional(scores, total, minAxisSize)` (`:590`) re-derives each axis's target size from those wobbling scores.
3. **Shed surplus**: for an axis now over-size, take the units *farthest from the axis target* and return them to the free pool, releasing their ledger commit and setting `HasOrdered=false` (`:599-612`).
4. **Top up**: for an axis now under-size, recruit the *nearest free units* (`:615-635`), setting `HasOrdered=false`.

A unit sitting between two axis targets is, by construction, "farthest" from whichever axis it is on. When scores jitter and sizes shift by even one, that unit is shed from axis A and immediately re-recruited by axis B (nearest-free ordering) whose objective is in the opposite direction → a fresh `AttackMove` the other way (`HasOrdered=false` forces re-issue at `:1078`). The `AxisCommitmentTicks=250` TTL is powerless here because **the module releases the commit itself** during the shed (`:608`). The sticky-target hysteresis (`ReassignScoreThresholdPct=30`, `SelectStickyTargets` `:843`) protects *which targets* are chosen but not *which units go to which surviving target*.

This is a genuine within-module loop independent of LayeredDefence. It is worst with `StrategicRepointEnabled` on (believed-field score jitter) and with ≥2 axes of comparable score.

**Landed mitigation (`1fec5070`, mid-revision):** `PartitionHeldAxes` (`:766`) now pulls every HELD axis out of steps 2–4 *before* the reshuffle — a held axis keeps its units, target, and in-flight order, and its ledger TTL is refreshed (`:807`) so `BuildFreePool` excludes its units. An axis is released only when `MissionCommitmentMath` (`PoiGoalGuard.cs:138`) fires one of four triggers: objective invalid, believed-danger spike at the objective, a rival outscoring the commitment by `MissionBetterOppMarginPct` (50%, above the 30% sticky margin), or force below half commit-time strength — the same shape as this doc's §3.3 triggers 1/2/3/4. Enabled for `@experimental` (`MissionCommitmentEnabled: true`, `ai.yaml:227`); flag off ⇒ byte-identical pre-change path. **Residual root-cause-B surface:** the rival trigger compares **raw** scores against a percent margin — susceptible to the bucket-crossing defeat FIX 7 documents (a single believed-field bucket step is up to 3×, always > 50%), which is exactly what Phase 1c's `QuantizeAxisScore` closes; and fresh (not-yet-held) axes still reshuffle each eval.

### 1.3 Root cause C — heli FSM transition-predicate flapping at 5-tick cadence

*(Revision 2 — this section is rewritten. The original claim — "the stock 75-tick FSM heartbeat with its Stop→AttackMove regroup toggle drives heli dithering" — was refuted by review, on three counts, all re-verified at `5d17623f`:)*

1. **`GroundStates.cs` is dead code on the current roster.** The Stop/AttackMove regroup toggle (`:158-161`) and the per-heartbeat re-issue (`:130-134,:161,:174`) only run for ground squads owned by `SquadManagerBotModule` — and all four instances (`ai.yaml:666,720,1042,1055`) are `.fixedwing` with `IgnoreGroundUnits: true` (`:677,:731,:1050,:1063`). No ground squad exists to toggle. (The 63-tick stuck→Idle safety at `GroundStates.cs:145` is likewise dormant — relevant to §3.3's stall trigger, which reinstates that idea at mission level.)
2. **Heli squads do not tick on the 75-tick heartbeat.** They are owned by `HelicopterSquadBotModule`, whose FSM updates on **`SquadUpdateInterval=5`** (`HelicopterSquadBotModule.cs:49`, countdown `:179-183`) — 15× faster than SquadManager's `AttackForceInterval=75`.
3. **`HelicopterApproachState` does NOT re-pick its target each update.** The target changes only on (a) target-invalid → Idle (`HelicopterStates.cs:388-391`), where the Idle state re-picks closest-enemy (`:352-361`); or (b) the too-hot **soft-target swap** (`:395-413`): when `IsTargetTooHot` fires, the squad swaps `TargetActor` to the nearest not-too-hot enemy within 20 cells (`:399-407`) or, if none, transitions to Withdraw (`:410`).

The **actual** dithering mechanism is transition-predicate flapping at 5-tick granularity. The FSM's boundary predicates — `ShouldFlee` → Return (`:384`), target-invalid → Idle (`:390`), too-hot soft-swap-or-Withdraw (`:395-413`), AA-danger-spike → Withdraw (`:429-433`), and the legacy `dist<8` → AttackRun hand-off (`:442-450`) — are each re-evaluated every 5 ticks against believed-danger and proximity inputs that jitter. A squad hovering at a predicate boundary cycles Approach→Withdraw→Idle→Approach, and the soft-swap re-aims `TargetActor` sideways whenever the current target's cell reads momentarily too hot — with two comparable targets the squad swings between them. There is no mission-level object pinning the *strategic* target across these micro-transitions; the FSM's own `TargetActor` is both the strategic and the micro target, so micro-level churn IS strategic churn.

### 1.4 Why the existing dampers are not enough

The codebase already carries six anti-oscillation dampers (ledger TTLs, LayeredDefence cooldown, sticky-target threshold, repath-cells gate, regroup timeout, the 75-tick heartbeat sized to survive re-issue — catalogued in [`260722_bot_brain_architecture.md`](260722_bot_brain_architecture.md) §1.6). They fail against the three root causes above because:

- **A** is a *missing* lock, not a mis-tuned one. As of `1fec5070` LayeredDefence *reads* the offense ledger behind `RespectCommitmentLedger` (@experimental only) — closing the defence-steals-from-offense direction — but MountedTransport and GarrisonBotModule still reference no commitment, LayeredDefence never *writes* its own assignments (offense can still steal from defence), and the @stable twin remains fully ledger-blind.
- **B** is the module defeating its own damper (it releases the commit to reshuffle).
- **C** is boundary predicates evaluated 15× per heartbeat with no strategic-target pin — the `BusyAttackMove` guard (`HelicopterStates.cs:213`) protects the in-flight *activity*, but nothing protects the *target choice* across transitions.

The Brain removes the causes rather than adding a seventh damper: one writer for the combat pool (kills A), missions that are not resized mid-flight (kills B), a mission objective that outlives FSM state transitions (kills C).

---

## 2. Brain architecture

### 2.1 Placement and shape

`SquadBrainBotModule` — a `[TraitLocation(SystemActors.Player)] ConditionalTrait, IBotTick`, one instance per bot player, `RequiresCondition: enable-ai-experimental` (never a single world-actor lookup — the shared-`@poi` twin trap from influence-stack.md applies). It ticks on a `StrategicInterval` countdown (default 100, staggered at `TraitEnabled` via `world.LocalRandom.Next` exactly like `PoiOffensiveBotModule.cs:408`). It owns:

```
List<Mission>            liveMissions;     // the durable commitments (see §3)
BrainState               state;            // posture + last-derived vectors/POIs (below)
GoalGuardLedger<Actor>   (shared)          // via PoiGoalGuard — unit ownership
```

Each strategic tick the Brain runs a pure **derive → reconcile → allocate** pass and touches unit orders **only** through creating/aborting missions. It reads, never writes, the influence stack.

### 2.2 Sensory input — the influence stack (already built, fog-legal)

The Brain consumes the existing `@experimental` substrate verbatim (all per-player, fog-legal, zero-RNG — see [`influence-stack.md`](../../DOCS/reference/influence-stack.md)):

| Signal | Source | Used for |
|---|---|---|
| Believed enemy contacts (position, **identity**, confidence) | `BeliefStore.Contacts(player)` | enemy-movement read; where the mass is; posture force-ratio |
| Anti-ground / anti-air danger fields | `DangerFieldLayer.GroundDanger` / `AirDanger`, `ActiveCells` | vector safety; abort danger-spike; corridor scoring |
| Believed territory control (± score, frontier BFS, contour) | `ControlField.ScoreAt` / `OwnerAt` / `DistanceToEnemyFrontier` / `IsFrontlineEdge` | vector direction; defend-POI selection; staging on the friendly side of the front |
| Scored POIs (attack / pressure / capture / defend) | `PoiMap.GetOffensiveTargets(player, suppressOmniscientThreat: true)`, `GetDefendTargets`, `GetCaptureTargets`, `OwnSupplyRoute(player)` | objective candidates (fog-legal via the suppression seam) |
| Own force | `world.Actors` filtered `IsEligibleCombatUnit` + `UnitRoleResolver.GetRole` | allocation; posture; composition |

Enemy *movement* (the owner's phrase) is the belief store's contact set delta plus the danger-field `ActiveCells` shift — the Brain does not need a new sensor.

### 2.3 Top-level output — posture (PIPELINE item 18), driven by an Aggressiveness scalar

The Brain's first decision each tick is a **posture**, computed by a pure `BrainPosture.Decide(...)` (NUnit-pinnable, integer-only):

```
enum Posture { Attack, Hold, Consolidate }
Posture Decide(int forceRatioPct, int srThreat, bool enemyInSrRing,
               int territoryTrend, bool economyMet, int aggressiveness /*0..100*/,
               Posture currentPosture, int ticksInPosture /* hysteresis, see below */)
```

Battlefield inputs (all integers already available):
- **Force ratio** `R` = own committed+free combat value ÷ believed enemy value ×100 (`BeliefStore` value sum, identity-weighted like `AmbushThreatValue`). **Zero-denominator rule (integer division would throw on tick 1):** when believed enemy value is 0 — early game before first contact, or after all mobile contacts decayed — `R` is defined as `RMaxPct` (a large integer sentinel, e.g. 10000, an `Info` field). Rationale: "no believed enemy" is the best force ratio a fog-legal commander can report, so it clamps toward maximum aggression exactly as the review recommends; posture still passes through the `economyMet` gate and the SR-threat override, so a blind early rush is bounded by the other inputs, and the moment a real contact lands `R` becomes finite again.
- **SR threat** `H` = max `DangerFieldLayer.GroundDanger` inside our own SR contestation ring, and whether any believed enemy contact sits inside it (existential — see [`supply-route.md`](../../DOCS/reference/supply-route.md)).
- **Territory trend** `T` = signed sum of `ControlField.ScoreAt` over the map (are we gaining or losing ground), sampled at the coarse grid.
- **Economy** `E` = derrick/income count and whether a supply-truck/economy floor is met (proxy: own income structures captured).

**Aggressiveness is a first-class input, not a bot archetype.** Rather than discrete "Rush/Turtle" personalities, a single `Aggressiveness` scalar (0..100) **shifts the posture thresholds** so the same code produces a cautious bot at 20 and a reckless one at 80. It enters the pure math as the two ratio cutoffs, each with **its own `*BasePct`/`*SlopePct` pair** per the §2.7 convention (revision 2: the original single shared `AggressionRatioSlopePct` contradicted §2.7's per-knob `base ± slope` rule — per-cut pairs win, so the two cuts can be tuned to converge or diverge independently):

```
attackCut = max(holdCut, AttackRatioBasePct - (aggressiveness - 50) * AttackRatioSlopePct / 100)
holdCut   = max(0,       HoldRatioBasePct   - (aggressiveness - 50) * HoldRatioSlopePct   / 100)
```

So higher aggressiveness lowers the force-ratio required to commit to Attack (attacks at a disadvantage) and lowers the floor below which it retreats to Consolidate (holds ground longer before falling back). At `Aggressiveness = 50` the cuts equal their base values → the tuned neutral bot; the whole formula is integer-only and swept in testing (§2.7). **Clamps (revision 2):** `holdCut` is floored at 0 and `attackCut` at `holdCut`, so the cut ordering `attackCut ≥ holdCut ≥ 0` is an invariant at every slider value. `attackCut = 0` at aggressiveness 100 is thereby explicitly **blessed**, not accidental: it means "Attack whenever `economyMet`" — the intended reckless extreme — and the math stays well-defined instead of producing a negative cut that would silently invert the table rows.

**Posture hysteresis (revision 2 — required, not optional).** A posture flip is abort trigger 5 (§3.3), so an input hovering at a cut boundary would flip posture and **mass-abort every live mission each strategic tick** — re-introducing the dithering at the top of the stack. Two integer guards, both `Info`-tunable:
- **Enter/exit bands:** each cut is split into an enter and an exit threshold (`cut + PostureBandPct` to enter the higher posture, `cut - PostureBandPct` to leave it, default band ±10). Between the bands the current posture persists — the same enter/exit asymmetry as the Stage-E/F "strict improvement" rule.
- **Minimum dwell:** a posture, once entered, holds for at least `PostureMinDwellTicks` (default 300 — three strategic ticks) before any non-existential transition. The **sole exception** is the SR-existential row (row 1): `H` high / `enemyInSrRing` may force Consolidate immediately at any time, because match loss outranks smoothness.

`Decide` therefore takes `(currentPosture, ticksInPosture)` as inputs and is still a pure integer function — the truth table in the NUnit pin covers the band and dwell edges.

Decision table (evaluated top-down; cuts are the aggressiveness-shifted values above):

| Condition | Posture | Meaning |
|---|---|---|
| `H` high OR `enemyInSrRing` | **Consolidate** | pull the nearest sufficient force home to clear the SR — overrides everything (match-loss risk). *Not* aggressiveness-shifted: SR loss is existential at any setting. |
| `R ≥ attackCut` AND `economyMet` | **Attack** | commit decisive force to enemy income / SR pressure; the "go for the kill" gear |
| `R ≥ holdCut` | **Hold** | maintain the line at the frontier contour, keep capturing income, do not lunge |
| else (`R < holdCut`) | **Consolidate** | fall back to a tighter defensive envelope around own territory + SR; rebuild |

The posture **gates which mission kinds the Brain may create** this tick (Attack ⇒ Assault/Pincer/Raid/Advance enabled; Hold ⇒ Defend/Capture + shallow Advance/Probe; Consolidate ⇒ Defend/SR-relief only). It is the single lever that makes the bot "shift gears" instead of drifting — item 18, satisfied as a first-class output rather than a bolt-on, and now the primary consumer of the Aggressiveness slider.

### 2.4 Deriving vectors, attack POIs, defend POIs

Within the posture-permitted set, each strategic tick:

- **Attack vectors** = the top-`k` `PoiMap` offensive targets (Attack income + Pressure enemy-SR), re-scored by the *believed* fields exactly as `PoiOffensiveBotModule.RescaleByBelievedFields` does today (balance-of-power ring at `AnchorRadiusCells+1`, believed-danger damp) — but computed **once, in the Brain**, and turned into a vector = `{ objective cell, approach corridor }`. The corridor is the `GroundDangerNav.DetourWaypoint` lane (Stage E) plus, for `Pincer`, a second corridor on the opposite side of the objective (min angular separation). `k` is bounded by posture and by force: `PoiOffenseMath.DesiredAxisCount` reused.
- **Defend POIs** = `PoiMap.GetDefendTargets` (own income + own SR) raised by believed danger (the mirror `BelievedDefendFactor` already in `PoiGarrisonBotModule`), plus any coarse `ControlField` cell that flipped from ours toward enemy since last tick (territory loss) and any frontier-contour sector with rising `GroundDanger`.
- **Force allocation** = a single proportional split of the whole combat pool across {consolidate-home, defend-POIs, attack-vectors, advance-sectors (§2.6)} by posture-weighted score, then within each destination a **role composition** target (`Recon / MainLine / AntiArmor / Fires / AirDefence` from `UnitRoleResolver`) — so an assault vector requests a combined-arms mix, not "8 nearest bodies" (the §1.5 gap in the 260722 doc). The Defend/Capture vs Attack/Advance balance is shifted by the `CaptureVsCombatPriority` slider (§2.7). Allocation is computed once and realized as mission create/abort, **not** as a per-tick unit reshuffle.

The key difference from today: the Brain derives the *plan* (vectors/POIs/allocation) and then reconciles it against the *existing live missions* with hysteresis — it does not re-issue orders. A vector that already has a healthy mission is left alone.

### 2.5 Reconcile with hysteresis (the anti-dither core)

```
for each desired destination (vector or defend-POI):
    if a live Mission already serves it and is healthy → keep it (refresh commitment TTL only)
    else if a live Mission serves a now-dropped destination → mark it for abort (release units)
    else if spare allocation exists → create a new Mission (§3), recruit by role from the free pool
```

Hysteresis lives at the **mission** granularity, not the unit granularity:
- A destination is only dropped when a challenger outscores it by `ReassignScoreThresholdPct` (reuse of the sticky rule).
- Units are recruited into a mission **once, at composition**, and are not shed while the mission is healthy (removing root cause B). Surplus/shortfall is corrected by *creating or retiring whole missions*, or by a bounded top-up that never *removes* an in-flight committed unit.
- The free pool is `world.Actors` minus every ledger-committed unit — and after Phase 2, **every** combat order source recruits only from this pool, so there is exactly one writer at a time per unit (removing root cause A).

### 2.6 Opportunistic advance — exploit a free path, keep pressure up

**Observed failure (owner, live play):** bots fan out capturing POIs but never *exploit* the openings that fanning-out creates — a sector goes undefended, a corridor opens forward, and the bot just sits on its captures instead of pushing. The influence stack already encodes exactly the signal needed; the Brain must turn it into a mission. This is a core behavior, not polish.

**Detection — "free path / undefended sector" is a fog-legal read of the existing fields.** Each strategic tick, over the coarse control grid, the Brain scores candidate **advance sectors** ahead of our current front:

- **No believed enemy presence:** `ControlField.OwnerAt(cell) != Enemy` for the sector, and no `BeliefStore` contact within the sector — the enemy is not *believed* to hold or occupy it (not "is empty" — a fog-legal belief read).
- **Low believed danger:** `DangerFieldLayer.GroundDanger` along the corridor from our frontier to the sector stays below `AdvanceDangerCeiling` (on the danger-field throughput scale, set above the territory baseline like the Stage-E/F thresholds, so ambient "deep enemy ground" does not qualify — only a genuinely clear lane does).
- **Forward of our line, into contested/neutral ground:** the sector sits across `ControlField.IsFrontlineEdge` from us, in the no-man's-land the verified-clear rule opens up — i.e. ground we could *take* by simply moving into it, not a defended core.
- **Reachable:** a `GroundDangerNav`-clear corridor exists (the same two-leg detour sampler), so the advance is a real path, not a wall.

A sector passing all four is a **granted opportunity**: undefended ground with a free path forward. The Brain emits an **`Advance` mission** (a new `MissionKind`, §3.1) toward the deepest such sector along the corridor — objective is the forward frontier cell, not an enemy structure — recruiting a light MainLine+Recon force from the free pool. An Advance mission that reaches its sector and still reads clear **extends** (re-derives the next sector forward) rather than stopping — this is what "keeps pressure up" instead of freezing on the capture. It aborts the instant a believed contact or danger spike appears in the corridor (abort triggers 1–2), degrading gracefully to a Hold/Defend at the newly-reached line.

**Aggressiveness scales eagerness** (§2.3/§2.7): aggressiveness is the multiplier on how readily an Advance mission is created and how deep it commits —
- it lowers `AdvanceDangerCeiling`'s effective bar less at high settings (a bold bot advances through more marginal danger),
- it raises the share of the free pool the allocation is willing to spend on Advance vs. Defend,
- and it deepens `AdvanceMaxSectors` (how many sectors forward an extending advance will chain before consolidating).

At low aggressiveness the bot only advances into *totally* clear ground with a small screen; at high aggressiveness it pushes opportunistically into thinly-held sectors with a larger force. Posture gates it: **Attack** enables deep Advance, **Hold** allows shallow single-sector Advance (keep nibbling the front), **Consolidate** disables it. This directly answers "when a sector is undefended and a free path exists, generally advance."

### 2.7 Tunable-parameter architecture — sliders, not archetypes

The owner wants **configurable slider-style parameters** (Aggressiveness, and later risk tolerance, capture-vs-combat priority, …) that can be set programmatically and swept during testing — *not* discrete bot personalities. The Brain is structured so every such knob flows the same way:

**Where the knobs live (YAML → Info fields).** Each slider is a plain `public readonly int` on `SquadBrainBotModuleInfo`, 0..100, defaulting to the neutral value (50 for symmetric knobs) that reproduces the tuned baseline:

```yaml
SquadBrainBotModule@experimental:
    Aggressiveness: 50            # 0 cautious … 100 reckless — shifts posture cuts + advance eagerness
    RiskTolerance: 50             # 0 hug cover/standoff … 100 accept exposed routes — shifts abort danger deltas
    CaptureVsCombatPriority: 50   # 0 all combat … 100 prioritise income capture — shifts allocation weights
```

Per-profile: `@experimental` carries the swept values; `@stable` (if it ever runs the Brain) carries the frozen ones; humans never instantiate it.

**How a knob flows into the decision math (integer-only, pure).** A slider is **never** read inside branching logic directly — it is passed as a scalar argument into a pure static math function so the whole decision is NUnit-pinnable without a game:

```
knob (0..100) ──Info field──► SquadBrainBotModule tick ──arg──► pure BrainPosture.Decide(... aggressiveness)
                                                          └──arg──► pure AdvancePolicy.Eagerness(... aggressiveness)
                                                          └──arg──► pure Allocate(... captureVsCombat, riskTolerance)
```

The convention is a **base ± slope** shift, integer-only, as shown for the posture cuts in §2.3: `effective = base + (knob - 50) * SlopePct / 100`. Each knob names its own `*BasePct` + `*SlopePct` pair (also Info fields), so the *range* a slider spans is itself tunable without code. No knob mutates state or draws RNG; a knob only shifts a threshold or a weight. This keeps the determinism contract (§6): a fixed knob set + fixed seed = byte-identical match.

**How a test harness sets them per-match.** Because the knobs are ordinary `Info` fields, the existing autotest scaffolding sets them the same way it sets any AI YAML — a scenario/map `rules.yaml` override block on the `@experimental` bot, or a swept value injected by the batch harness per match. A sweep is: run the S1/S2 ladder with `Aggressiveness ∈ {20,35,50,65,80}` (seed-matched), read the win-rate/territory curve, pick the baseline — exactly the "find the right baseline by sweeping" the owner asked for. Because a fixed knob is a reproduction (`World.cs` seed determinism), each point on the sweep is a stable mean over the seed set, not noise. The eventual lobby slider is then just a UI that writes the same `Info` field at match start — no decision-code change.

**Extensibility.** Adding a new slider is: one `Info` field + its `*BasePct`/`*SlopePct` pair, one argument threaded into the relevant pure function, one line in the sweep grid. `RiskTolerance` (shifts abort-trigger danger deltas §3.3 and staging standoff), `CaptureVsCombatPriority` (shifts the §2.4 allocation weights between Defend/Capture and Attack/Advance), and difficulty-as-a-slider all follow this pattern without touching the Brain's control flow.

---

## 3. Mission semantics

A `Mission` is the durable thing a group of units is committed to. It is the object the owner means by "squads go on missions and aren't touched mid-mission without good reason."

### 3.1 Data

```csharp
enum MissionKind  { Assault, Pincer, Raid, Advance, Defend, SrRelief, CaptureEscort, Probe }
// Advance = opportunistic push into an undefended sector along a free corridor (§2.6);
//           objective is a forward frontier cell, not an enemy actor. Extends while clear.
enum MissionState { Proposed, Staging, Committed, Aborting, Resolved }

sealed class Mission
{
    int    Id;                       // per-player deterministic sequence (not RNG)
    MissionKind Kind;
    string ObjectiveKey;             // reuses ledger grammar: "offense:<poiId>", "defend:<poiId>", ...
    CPos   Objective;
    uint   ObjectiveActorId;         // for validity checks (still alive / still enemy);
                                     // 0 for Advance missions — their objective is a cell, not an
                                     // actor, so abort trigger 1 NEVER fires for them (see §3.3)
    CPos?  RouteHint;                // Stage-E lateral waypoint / corridor (null = direct)
    Dictionary<UnitRole,int> Composition;   // requested role → count
    List<Actor> Units;               // assigned (iterate by ActorID)
    MissionState State;
    int    CreatedTick;
    int    CommitmentExpiresTick;    // TTL; refreshed while healthy — the ledger lease
    int    StagingDeadlineTick;      // anti-deadlock: launch or downgrade by here
    // cached for abort math:
    int    InitialForceValue;        // for combat-ineffective threshold
}
```

The Brain owns `List<Mission>`. Each mission commits its `ObjectiveKey` **and** its member units to the shared `PoiGoalGuard` ledger; because PoiOffensive/PoiGarrison/Capture already skip ledger-committed units, mission suppression of those modules is free during coexistence.

### 3.2 State machine

```
Proposed ──accept──► Staging ──launch cond──► Committed ──objective met──► Resolved
    │                   │                          │
    │                   └── deadline, downgrade ───┤
    └── rejected ─► (discarded)                    │
                                                   ▼
                              any abort trigger ► Aborting ─► (units released, Resolved)
```

- **Proposed** — created by the Brain's reconcile pass (§2.5). Utility-scored against other proposals and against "stay idle." Accepted up to posture/allocation budget.
- **Staging** — objective key + units committed to the ledger; each force ordered (grouped `AttackMove`) to a **staging point** on the friendly side of `ControlField`'s frontier, `StagingStandoffCells` back from the objective approach, minimum `GroundDanger`. Ready = `≥ReadyPct` of the force within `StagingRadius`. For single-force `Assault`/`Defend` staging can be a zero-tick pass-through (`StagingStandoffCells=0`) so simple missions launch immediately — staging only materially matters for `Pincer`/`Raid`.
- **Committed** — launch condition fires (all forces Ready, **or** `StagingDeadlineTick` reached with `≥MinForcesReady` — the anti-deadlock downgrade to single-axis). **Deadline with `<MinForcesReady` (revision 2, N2): the mission ABORTS** — units release to the free pool and a `Retry` proposal is emitted with the standard cooldown. Why abort rather than wait or downgrade-to-Probe: waiting is exactly the wedge the deadline exists to prevent (and FIX-4's stall trigger would eventually kill it anyway, just slower); launching an understrength Probe would feed units piecemeal into the objective — the dribble failure abort trigger 4 exists to stop — and would do it *by design*. Aborting returns the units to productive allocation on the very next Brain tick, and if the objective is still the best use of force the reconcile pass re-proposes it with (by then) a larger free pool. The assault order is issued **once**; `Fires` role units take standoff (reuse `OrderFiresStandoff`), `Recon` leads, `AirDefence` stays with the body. **This is the only tick the objective order is issued** — no heartbeat re-issue. Re-issue happens only on an abort→re-plan or a route-hint change past `RepathThresholdCells`.
- **Aborting** — an abort trigger fired (§3.3). Units released from the ledger back to the free pool; a `Retry` proposal may be emitted with a cooldown (retryable missions).
- **Resolved** — objective condition met (POI captured / threat cleared / SR ring clear). Ledger released.

State transitions happen only in the Brain tick, evaluated in deterministic mission-Id order.

### 3.3 Abort-trigger set — the ONLY reasons a Committed mission is touched

A `Committed` mission is otherwise left entirely alone (this is the whole point). It transitions to `Aborting` iff **one** of these fires (checked cheaply each strategic tick):

1. **Objective invalid** — `ObjectiveActorId` dead, no longer enemy (captured/neutralized), or `PoiMap` no longer lists it. (Structures are public facts; no fog leak.) **Does not apply to `Advance` missions** (revision 2, N1): their objective is a frontier *cell*, `ObjectiveActorId = 0`, so this trigger never fires for them — an Advance that walks into trouble is caught by trigger 2 (danger spike) and, as its primary safety against silent wedging, by trigger 6 (stall).
2. **Danger spike beyond threshold** — max `DangerFieldLayer.GroundDanger` sampled along the remaining route or at the objective rose above `AbortDangerThreshold` **and** by at least `AbortDangerDeltaPct` over the value when the mission committed (a rising believed AA/AT envelope the mission would grind into). Hysteresis in the *delta* prevents baseline jitter from tripping it — the Stage-E/F lesson that the territory baseline stacks additively.
3. **Materially better opportunity** — a new proposal outscores this mission's objective by **more than `AbortReassignThresholdPct`** (a wider margin than the keep-sticky threshold, so only a clearly better target pulls a committed force). Enemy-movement-driven: e.g. the enemy SR ring became contestable, or an undefended income POI appeared behind a collapsing front. **Two hardening rules (revision 2, FIX 7):**
   - **Compare quantized scores, never raw scores.** The believed-field factors are *bucketed*, not continuous: `BelievedDangerFactor` steps 100/60/20 and `BalanceOfPowerFactor` steps 150/100/60 (`RescaleByBelievedFields`, `PoiOffensiveBotModule.cs:699-759`; tuned values `ai.yaml:275-281`). A single bucket crossing multiplies a score by up to 3× — larger than **any** percent hysteresis margin — so a raw-score comparison ping-pongs abort/re-propose every time a cell hovers at a bucket edge, reinstating the dither one level up. Trigger 3 therefore compares scores passed through the same `QuantizeAxisScore` banding that Phase 1c introduces (§5): both sides are snapped to coarse bands *before* the threshold test, so a bucket-edge wobble moves both scores inside one band and cannot clear the margin.
   - **Same-kind comparisons only.** Defend and Assault scores are built from *different factor stacks* (garrison raises on believed danger, offense damps on it — mirror-image multipliers), so their magnitudes are not commensurable and "Defend 900 vs Assault 700" is meaningless. Trigger 3 compares a challenger **of the same `MissionKind` family** (Assault/Pincer/Raid/Probe against offensive missions; Defend/SrRelief against defensive ones) scored under that family's own factor stack. Cross-kind force rebalancing — "we should be defending, not attacking" — is exclusively the **posture's** job (§2.3, via trigger 5 and the per-tick allocation), never trigger 3's.
4. **Combat-ineffective** — the mission's live force value fell below `AbortStrengthPct` of `InitialForceValue` (losses), or `<MinAxisSize` units remain. The remnant retreats/merges rather than dribbling into the objective.
5. **Posture override** — the Brain flipped to **Consolidate** with SR threat (§2.3 row 1): all non-defensive missions abort and the nearest sufficient force is redirected home. This is the existential interrupt and is allowed to override even a healthy Committed mission. (Ordinary posture changes are damped by the §2.3 enter/exit bands + minimum dwell, so this trigger cannot fire on boundary-hovering inputs — revision 2, FIX 5.)
6. **Stalled — no progress** *(revision 2, FIX 4)*. The squad **centroid displacement** over the last `StallWindowTicks` (default 200) is below `MinProgressCells` (default 2) while the mission is Committed and not yet at its objective → Aborting. Integer-only: centroid = component-wise sum of member `CPos` divided by count; displacement compared squared against `MinProgressCells²` — no roots, no floats. This closes the wedge the review found: without it, a "healthy" mission (units alive, danger flat, no better offer) that is stuck on terrain, a destroyed bridge, or a pathfinding failure refreshes its TTL forever, because "healthy" nowhere included *making progress*. The retired FSM paths carried exactly this safety — `GroundStates.cs:145` (63-tick no-move → Idle) and `HelicopterStates.cs:513` (`stuckTicks > 200` → Idle) — and the Mission layer must not lose it. It is also the **primary abort safety for `Advance` missions** (trigger 1 can never fire for them, per above). Units engaged in combat at the objective are exempt via the same "actively engaging ≠ stuck" test the heli path uses (`HelicopterStates.cs:507-511`).

**Heli too-hot soft-swap — executor-local liberty, bounded (revision 2, N5).** The `HelicopterApproachState` soft-target swap (`HelicopterStates.cs:395-413`) survives under missions as an **executor-local** micro decision, NOT a mission abort: swapping to a not-too-hot enemy within the existing 20-cell scan of the squad is transient target servicing on the way to the strategic objective, and aborting a whole mission because one cell read hot for one 5-tick window would reintroduce exactly the churn §1.3 documents. The bound: the executor may engage soft targets but the mission's `Objective` is unchanged and the squad resumes toward it; if **no** soft target exists and the *objective itself* stays too hot, that surfaces to the Brain as trigger 2 (danger spike at the objective) — the mission aborts by the strategic rule, not by FSM state.

TTL (`CommitmentExpiresTick`) is a **backstop**, not a trigger — a healthy mission refreshes it each tick, so it only expires if the Brain stopped ticking the mission (dead/stuck), where the ledger prune reclaims the units anyway. This inverts today's model where TTL expiry *is* the re-decide clock. ("Healthy" now explicitly means *no abort trigger fired* — including trigger 6's progress test, so a stalled mission cannot refresh its own lease.)

Triggers 2–4 each carry a hysteresis margin (trigger 3's operating on quantized bands, per above) so that the enemy jitter that causes today's loop cannot trip them; that is the design's contract against re-introducing the dither.

---

## 4. Integration + migration — modules become executors

The end state: the Brain is the **only** decider of which units pursue what; the other modules become **executors** that, given a mission (or a stance from the Brain), drive units and report status up. During transition, coexistence is safe because everything routes through the one ledger.

| Module | Today | Under the Brain | How it becomes an executor |
|---|---|---|---|
| `PoiOffensiveBotModule` | scores + allocates + orders offense axes (root cause B lives here) | **Assault/Pincer/Raid executor**: given a mission's objective + route + unit set, issue the grouped `AttackMove` **once** and hold; the Brain owns axis count/sizing/target selection | strip `Reevaluate` steps 2–8 (scoring, `SelectStickyTargets`, `AllocateProportional`, shed/top-up); keep `CommitAndOrder`, `OrderFiresStandoff`, Stage-E detour, cohesion. Reads its mission list from the Brain. |
| `PoiGarrisonBotModule` | scores + orders garrisons | **Defend executor**: drive units to a Brain-chosen defend POI, hold | same shrink; keep the grouped move + garrison hold |
| `LayeredDefenceBotModule` | ledger-*reading* line dispatcher (read-side landed `1fec5070`; still never *writes* its own assignments — root cause A half-closed) | **Line/screen executor for Defend missions** — and, critically, recruits **only from the free pool** (ledger-checked) | **Landed (Phase 1a, `1fec5070`)**: ledger `IsCommitted` check in the reserve filter (`:331`, inside the `:282-:345` eligibility chain) behind `RespectCommitmentLedger` (`:121`; on for `@experimental`, `ai.yaml:566`). Still to do: commit its own line assignments (Phase 2 audit); later, line slots come from the Brain's defend allocation, not its own scan. |
| `CaptureCoordinatorBotModule` | own capture/defense scans | **CaptureEscort executor**: the Brain proposes CaptureEscort missions; escorts become mission members (fixing the never-committed-escort bug structurally) | capture ordering stays; escort recruitment reads the mission composition |
| `HelicopterSquadBotModule` / `HelicopterStates` | strategic target churned by 5-tick transition flapping + soft-swap (root cause C, §1.3) | **Air-strike executor**: the Brain assigns a heli mission an objective; the FSM keeps its standoff/danger-nav micro AND the bounded too-hot soft-swap (§3.3 N5 — executor-local liberty), but the strategic objective is pinned by the mission | gate the Idle-state re-pick (`HelicopterStates.cs:352-361`) behind "no Brain mission assigned"; when a mission exists, transitions may service soft targets but always resume toward the mission `Objective` until abort |
| `MountedTransportBotModule@poi`/`@experimental` | ledger-blind writer: orders combat infantry (`EnterTransport` `:517`) and carriers with zero ledger refs; its `IsPassengerReserved` seam (`:155`) is a bespoke lock only LayeredDefence honors (`LayeredDefenceBotModule.cs:324`) — offense's `BuildFreePool` does **not** (`PoiOffensiveBotModule.cs:878-890`), so offense can yank infantry mid-boarding | **Ferry executor**: passengers it loads are ledger-committed (`transport:<carrierId>`) for the ride duration, replacing the bespoke `IsPassengerReserved` cross-check; recruits passengers only from the free pool | commit-on-load, release-on-unload; `BuildFreePool`-side fix (respect the commitment) lands with the same phase |
| `GarrisonBotModule@defenses` (`enable-ai-any` — live for BOTH bots) | ledger-blind writer: `IsIdle`-gated (`:122`), `EnterTransport` into defenses (`:152`), zero ledger refs | **Garrison-fill executor**: recruits infantry only from the ledger-checked free pool; commits garrisoned units (`garrison:<buildingId>`) | add the ledger check + commit; being a **shared** `enable-ai-any` module, its flag must double-gate (`Info.Flag && InfluenceStack.Participates(player)` is no longer sufficient to confine to `@experimental` — see §6 gating note) |
| `SquadManagerBotModule` (air) | air squad former | unchanged for now (air squads); a later phase folds air into missions | — |
| `UnitBuilderBotModule` / `AdaptiveProductionBotModule` | production | consume unmet mission `Composition` via `IBotRequestUnitProduction` (the `MaintainTecnFloor` pattern) | closes the composition↔production loop; no restructure |

**Coexistence invariant during migration (revision 2, FIX 3 — strengthened):** at every phase there is exactly one writer per unit *per tick* because every writer **HOLDS a ledger commitment for every unit it orders — commit-on-order, not check-before-order.** The original wording ("holds-or-checks") was unsound: checking without holding gives no exclusion — two checkers can both find a unit free on the same tick and both order it, and a checker's recruit is invisible to every other writer until *something* commits it. The live proof is already in the tree: `CaptureCoordinatorBotModule` *checks* the ledger (`:448,:467,:926`) but commits only the capturer (`:950`) — its escorts (`:1160`) and defenders (`:1257`) are recruited, ordered, and left uncommitted, free to be stolen by offense's next `BuildFreePool` on the very next eval. So the invariant each phase must establish is: **every executor commits every unit it orders, at order time, under its own objective key** (`offense:<id>`, `defend:<id>`, `capture-escort:<id>`, `transport:<carrierId>`, `garrison:<buildingId>`, …), and recruits only units not currently committed. The migration plan therefore carries an **executor-by-executor commit audit** (folded into Phase 2, §5): for each row of the table above, verify (a) recruits come only from the ledger-checked free pool, (b) *every* ordered unit is committed at order time, (c) the commitment is released exactly once (resolve/abort/unload), and (d) the objective-key grammar is disjoint across executors. Known audit findings to fix, from §1.0: CaptureCoordinator escorts/defenders (never committed), MountedTransport (no ledger at all — bespoke `IsPassengerReserved` seam), GarrisonBotModule@defenses (no ledger at all), LayeredDefence (reads but never writes — Phase 1a landed the read side, the write side is this audit's item), PoiOffensive's `BuildFreePool` ignoring `IsPassengerReserved` (`:878-890`). The Brain and a not-yet-migrated module can both run safely **only after** that module passes the audit; the migration order is chosen so the highest-traffic ledger-blind writer (LayeredDefence) is fixed **first** (Phase 1 — its read side landed with `1fec5070`).

---

## 5. Phased implementation plan

Each phase is independently shippable, default-off, `@experimental`-gated, byte-identical for `@stable`/legacy when its flag is off, and priced on the ai-bench ladder before promotion.

### Phase 1 — kill the dither with commitment + hysteresis (**1a/1b LANDED** via `auto/mission-commitment`, merged `1fec5070`/`6aff93c3` mid-revision)

Smallest change that removes the visible loop. The "commitment ledger extension of `PoiGoalGuard`" branch **merged to main during this revision**: 1a landed verbatim, 1b landed in an equivalent (stronger) form. Refs below cite the landed code at `660a0ee2`.

- **1a. Make LayeredDefence honor the ledger — LANDED (`1fec5070`).** Exactly as designed: `goalGuard.Ledger.IsCommitted(actor, tick)` in the reserve filter (`LayeredDefenceBotModule.cs:331`, inside the `:282-:345` eligibility chain), behind `RespectCommitmentLedger` (default-off, `:121`; on only for `@experimental`, `ai.yaml:566`). Kills the defence-steals-from-offense half of root cause A. **One-directional, acknowledged (revision 2, N6):** the reverse channel stays open — LayeredDefence never commits its line assignments to the ledger (its `assignedAtTick` stamp `:425` is private), so offense's `BuildFreePool` (`PoiOffensiveBotModule.cs:878-890`) can still strip an uncommitted defense-line unit on any eval. That residual loop is expected to be much rarer (offense recruits nearest-free toward *its* objectives, and line units usually sit rearward), but it is not closed until the commit-on-order audit (§4) lands in Phase 2 — Phase 1's success criterion below is scoped accordingly.
- **1b. Stop PoiOffensive shedding committed units mid-approach — LANDED, stronger form (`1fec5070`).** The design asked for a per-unit shed veto; the landed mechanism holds the whole *axis*: `PartitionHeldAxes` (`PoiOffensiveBotModule.cs:766`) pulls axes with fresh commitments out of the reshuffle entirely — held axes skip free-pool rebuild, scoring, and shed/top-up (`:599-635`), their TTL refreshed while healthy (`:807`) — and release is decided by the pure predicate `MissionCommitmentMath.ShouldRelease` (`PoiGoalGuard.cs:138`: objective invalid / danger spike ≥ `MissionDangerSpikePct` / rival beats by ≥ `MissionBetterOppMarginPct` / below half strength). Flag `MissionCommitmentEnabled` (default-off `:291`; on for `@experimental`, `ai.yaml:227`). This is trigger-1/2/3/4 of §3.3 in embryo. Blunts root cause B without the full Brain.
- **1c. Widen the believed-field score damping — REMAINING (and now the sharpest residual gap).** Quantize scores (`QuantizeAxisScore`) so 25-tick field jitter cannot flip proportional sizes every eval, and so commitment-release comparisons operate on bands. **The landed 1b makes this urgent rather than optional:** `ShouldRelease`'s rival test compares *raw* scores against the 50% margin, and a single believed-field bucket crossing (§3.3 FIX 7: factor steps 100/60/20, 150/100/60) multiplies a score by up to 3× — clearing any percent margin and re-opening the abort/re-propose ping-pong one level up. Verified **not** in the landed code at `660a0ee2`.
- **Touched files (1c/1d):** `PoiOffensiveBotModule.cs`, `PoiGoalGuard.cs` (quantize before the rival compare), `ai.yaml` (`@experimental` flags only). **Byte-identity gate:** flags default off → `@stable`/legacy unchanged; per-profile trait instances make the single flag sufficient (no `!enable-ai-experimental` needed here). **NUnit-pinnable seams:** a pure `QuantizeAxisScore`; `MissionCommitmentMath.ShouldRelease` is already pure and pinnable. **Size of remainder:** small (~0.5 day).
- **Success criterion (revision 2, N9 — measure the real steal window, not the front line).** §1.1's on-line damper (`OnLineRadiusCells=8`, `LayeredDefenceBotModule.cs:334-345`) already protects units idling near contested cells, so a test that watches the front will show little change from 1a. The scenario 1a actually fixes is **mid-route idles and post-fight quiet zones**: a unit committed to `offense:<id>`, momentarily `IsIdle` en route (or idling at a taken, no-longer-contested objective), outside the 8-cell contested bubble. The autotest assertion is therefore telemetry-shaped around that: count, per match, order-source flips on ledger-committed units — i.e. a unit holding a fresh offense commitment receiving a LayeredDefence `AttackMove` (`:424`) — expecting **zero** with the flag on versus a nonzero baseline off; plus the coarse dither proxy (direction reversals per unit-minute) as the behavioral echo. Front-line units are excluded from the primary count since the damper already covers them. This criterion now doubles as the **pricing test for the landed 1a/1b** before any promotion to `@stable`.

**1d. Slider infrastructure (cheap, unlocks testing — do it here).** Stand up the tunable-parameter plumbing (§2.7) even before the Brain exists: add `Aggressiveness` (and the empty `*BasePct`/`*SlopePct` scaffolding) as `Info` fields wherever the first consumer lands, and thread it through one pure function so a sweep harness can vary it per match. This is a few `Info` fields + one pure helper; it costs almost nothing and lets the owner start sweeping the aggressiveness baseline immediately (even against the Phase-1 offense tuning), rather than waiting for Phase 3. **NUnit-pinnable:** the `base ± slope` shift helper. **Size:** trivial (~half a day), bundled into Phase 1.

### Phase 2 — the Mission object + single-writer free pool

- Introduce `Mission` (§3) and a thin `SquadBrainBotModule` that, for now, only wraps the *existing* offense/garrison/defend allocation as missions (no new posture logic yet) and enforces the **single-writer** rule: every combat order source recruits from the ledger-checked free pool, and the Brain is the only creator/aborter of missions.
- Migrate `PoiOffensiveBotModule` and `PoiGarrisonBotModule` interiors to **executors** driven by mission objects. Their independent scoring/allocation is **bypassed when `BrainEnabled` is on, not deleted** (revision 2, N4): the self-scoring path stays intact as the flag-off branch until the Brain-driven path has been priced on the ai-bench ladder and promoted — only then is the dead branch removed. This keeps the byte-identity-off proof trivial (the off branch *is* the current code) and keeps a one-line rollback available throughout pricing.
- **Run the executor-by-executor commit audit (§4, FIX 3)** across every row of the §4 table: recruits-from-free-pool, commit-on-order for *every* ordered unit, exactly-once release, disjoint objective-key grammar. Fix the known findings in the same phase: CaptureCoordinator escort/defender commits (`:1160,:1257`), MountedTransport commit-on-load (`transport:<carrierId>`), GarrisonBotModule@defenses commit-on-garrison (`garrison:<buildingId>`), and `BuildFreePool` respecting `IsPassengerReserved`/the transport commitment (`PoiOffensiveBotModule.cs:878-890`). Each fix behind its executor's default-off flag.
- **Touched files:** new `SquadBrainBotModule.cs`, `Mission.cs` (+ pure `MissionAbort` math), `PoiOffensiveBotModule.cs`, `PoiGarrisonBotModule.cs`, `CaptureCoordinatorBotModule.cs`, `MountedTransportBotModule.cs`, `GarrisonBotModule.cs` (audit fixes), `ai.yaml`. **Byte-identity gate:** `BrainEnabled=false` default; when off the executors fall back to their current self-scoring path (bypassed-not-deleted, per above). **NUnit-pinnable:** mission state machine transitions; abort-trigger predicates (`ObjectiveInvalid`, `DangerSpike`, `Ineffective`, `Stalled`) as pure functions over integers. **Size:** medium (~2.5–3.5 days, audit included).

### Phase 3 — posture (item 18) + opportunistic advance + role composition + full slider set

- Add `BrainPosture.Decide` (§2.3) driven by the `Aggressiveness` scalar stood up in 1d, plus the remaining sliders (`RiskTolerance`, `CaptureVsCombatPriority`, §2.7). Posture gates mission kinds; composition (§2.4/§4) requests roles from `UnitRoleResolver` and emits production demand for shortfalls.
- Add **opportunistic advance** (§2.6): the free-path/undefended-sector detector + `Advance` mission kind + the extend-while-clear behavior, with aggressiveness scaling eagerness/depth. This is the direct fix for "bots capture POIs but never exploit the opening."
- Migrate `LayeredDefence` line slots and `CaptureCoordinator` escorts to Brain-issued Defend/CaptureEscort missions (removes their independent scans).
- **Touched files:** `SquadBrainBotModule.cs`, `LayeredDefenceBotModule.cs`, `CaptureCoordinatorBotModule.cs`, `ai.yaml`. **Byte-identity gate:** posture defaults to a permissive "Attack-always" table + `Aggressiveness=50`/neutral sliders reproducing today when off; Advance behind its own default-off flag. **NUnit-pinnable:** `BrainPosture.Decide` truth table over `(R,H,T,E,aggressiveness)`; `AdvancePolicy` sector-scoring + eagerness; the `base ± slope` shift; composition-fill ordering. **Size:** medium (~2–2.5 days). **Sweep on promotion:** run the aggressiveness grid (§2.7) to pick the baseline before default-on.

### Phase 4 — staging, Pincer, air missions, event-driven re-tasking

- Real staging + launch conditions (Pincer over two corridors, same-tick launch), heli/air missions, and the deterministic event bus so abort/re-plan is event-driven rather than tick-swept.
- **Heli missions target the real root cause C mechanisms (revision 2, FIX 8 — re-grounded).** The work is *not* "slow down a 75-tick heartbeat" (refuted, §1.3); it is pinning the strategic target across the FSM's fast micro-transitions:
  - **Pin the strategic target in the mission, not in `TargetActor`.** Today the FSM's `TargetActor` is simultaneously the strategic and the micro target, so 5-tick predicate churn IS strategic churn. Under a mission, the mission `Objective` is the strategic target; `TargetActor` becomes purely tactical.
  - **Gate the Idle re-pick** (`HelicopterStates.cs:352-361`) behind "no Brain mission assigned" — with a mission, Idle routes back toward the mission objective instead of re-picking closest-enemy.
  - **Bound the too-hot soft-swap** (`:395-413`) as executor-local liberty per §3.3/N5: swap serves transient targets, the squad resumes toward the mission objective; objective-itself-too-hot surfaces as trigger 2.
  - **Predicate flapping across the Flee/AA-spike/AttackRun boundaries** (`:384,:429-433,:442-450`) is tolerated at the micro level (5-tick `SquadUpdateInterval` stays); what changes is that no flap can change the *strategic* destination — the abort-trigger set (§3.3, with its hysteresis margins) is the only path that can.
- **Touched files:** `SquadBrainBotModule.cs`, `HelicopterStates.cs`/`HelicopterSquadBotModule.cs`, corridor math. **Byte-identity gate:** each behind its own flag. **NUnit-pinnable:** corridor angular-separation math; launch-condition evaluation. **Size:** medium-large (~2–3 days), only if Phases 1–3 price positively.

Effort total is consistent with the 260722/260720 costing (board+lifecycle ~1–1.5d, migration ~2–3d). Phase 1 is the highest-leverage, lowest-risk step and should ship first regardless of whether the later phases proceed.

---

## 6. Determinism constraints (load-bearing — same contract as the influence stack)

Every part of the Brain must satisfy the invariants in [`influence-stack.md`](../../DOCS/reference/influence-stack.md) §Invariants:

- **Zero `SharedRandom`/`LocalRandom` draws in decision logic.** Mission Ids are a per-player integer sequence, not RNG. The only permitted draw is the `TraitEnabled` stagger offset (already the established pattern, `PoiOffensiveBotModule.cs:408`, drawn from `LocalRandom`), which does not affect decisions. *(Revision 2, FIX 1 — the original guidance here was inverted.)* Ties are broken **zero-RNG, by lowest `ActorID`** (or lowest mission Id) — that is the stack's invariant and needs no random stream at all. If a genuinely stochastic choice were ever unavoidable, it must draw from **`LocalRandom`** — the deterministically-derived bot-decision stream (`World.cs:226-228`, seed derivation `:283-286`; the engine comment at `:219` says exactly this: "LocalRandom drives bot *decisions*") — and **never `SharedRandom`**, the lobby-seeded synced *simulation* stream (`World.cs:217`): a flag-gated `SharedRandom` draw would advance the synced stream only when the flag is on, shifting every subsequent sim draw and destroying the byte-identity-off proof outright.
- **Integer-only math.** Force ratios, posture thresholds, allocation, danger/control sampling are all integer (percent-scaled `/100`), matching `PoiOffenseMath`/`ControlFieldMath`.
- **Deterministic iteration.** Missions iterate by `Id`; forces by index; units by `ActorID`; proposals/events ordered by `(tick, priority, ActorID)`. No `Dictionary` iteration order reaches a decision (the `firesHeldFire`/`lastCohesion` maps in PoiOffensive are already handled this way).
- **`@experimental`-gated via `RequiresCondition`.** `SquadBrainBotModule` is `enable-ai-experimental`; every new consumer flag defaults to the frozen behavior. Per-profile trait instances mean a single default-off flag suffices (first gating pattern). **Caution on the second pattern (revision 2):** post `stable-0802`, `InfluenceStack.Participates` admits **both** `BotType=="experimental"` and `"stable"` (`InfluenceStack.cs:48`), so `Info.Flag && Participates(player)` no longer confines a shared module's behavior to `@experimental` — it would fire for the stable bot too. A consumer bolted onto a **shared** `enable-ai-any` module (e.g. `GarrisonBotModule@defenses`, §4) must gate on the bot type explicitly (per-profile flag, or an explicit `BotType == "experimental"` check), not on `Participates` alone.
- **No `RenderPlayer`/wall-clock/off-sim state.** All Brain state lives in per-player module fields; nothing reads `world.RenderPlayer` or `DateTime`. Same-tick multi-force launch is trivially safe (issued within one module tick).
- **Byte-identity when off.** With every flag off, `@stable`/Normal/legacy and the frozen `@stable` twin are byte-identical — proven the way Stage-F was: the suppressed/disabled branch must collapse verbatim to the current expression, and no new RNG draw shifts the stream.

---

## Open questions for the owner

1. **Phase 1 scope split with `auto/mission-commitment` — RESOLVED mid-revision.** The branch merged (`1fec5070`) and covers 1a verbatim plus a stronger 1b (axis-hold via `PartitionHeldAxes` + `MissionCommitmentMath`). Phase 1's remainder is exactly **1c + 1d** (score quantization + slider scaffolding). The residual question for the owner: 1c's `QuantizeAxisScore` must also be threaded into `MissionCommitmentMath.ShouldRelease`'s rival compare (§5 Phase 1c) — OK to touch `PoiGoalGuard.cs` for that, or keep the quantization caller-side in `PoiOffensiveBotModule`?
2. **Aggressiveness baseline + difficulty coupling.** §2.7 makes Aggressiveness a swept `Info` scalar; after the sweep picks a neutral baseline, do we also want it wired to difficulty tiers (Easy = low, Brutal = high) in the same pass, or keep one tuned value until the Brain proves out? Same question for `RiskTolerance` / `CaptureVsCombatPriority`.
3. **Opportunistic advance vs. over-extension.** Advance keeps pressure up (the requested behavior) but at high aggressiveness can over-extend into a re-formed enemy line. The abort triggers (danger spike / combat-ineffective) are the safety net — is that enough, or do we want an explicit "don't advance more than N sectors past the nearest defended POI" leash beyond `AdvanceMaxSectors`?
4. **Heli missions (Phase 4) vs leaving air on its FSM.** Air dithering (root cause C — 5-tick transition-predicate flapping plus the too-hot soft-swap churning the strategic target, §1.3) is less visible than ground; is pinning the strategic target under a mission worth Phase 4, or park it and keep the FSM's `TargetActor` as-is?
