# 260802 — Squad-Mission "Brain": architecture design

**Mode:** EXPERIMENTAL (design doc — no engine/YAML changes; no simulations run)
**Researched against:** `main @ 493f76a4` (all file:line below verified at this ref; the offense module has moved substantially since the `0fce8bbd` inventory in [`260722_bot_brain_architecture.md`](260722_bot_brain_architecture.md) — refs here are current).
**Problem (owner, live play):** units "get orders in steady intervals and go one way for one second, then stop and go the other way, and they get stuck in loops." Wanted: "a central **Brain** that decides the vectors of attack and the POI to defend and attack etc, based on the enemy movements and the Influence maplayer, and then coordinates the squads accordingly" — squads go on **missions** and aren't touched mid-mission without good reason.
**Folds in:** PIPELINE item 18 ("Should I attack?" endgame posture layer) — designed here as the Brain's top-level output, not a separate item.

**Relationship to prior design work.** [`260722_bot_brain_architecture.md`](260722_bot_brain_architecture.md) already ratified the *verdict* (EXTEND the chassis; add a persistent operations/task-force object between strategy and units; keep L1 scoring + L3 micro) and surveyed industry precedent. This document is its **code-anchored engineering successor**: a concrete per-player Brain module, the exact order-source forensics at current HEAD that justify it, a precise mission state machine + abort-trigger set, an executor-by-executor migration, and a phase plan whose Phase 1 aligns with the in-flight `auto/mission-commitment` branch. Where the two disagree on naming, this doc's `Mission`/`Brain` supersede the older `Operation`/`TaskForce` sketch (§4 there); the lifecycle is otherwise the same shape.

---

## Executive summary

The dithering is **not** one bug. It is the sum of several independent order sources ticking on their own clocks against a shared unit pool, plus one module that re-shuffles its *own* units every evaluation as jittering scores move the proportional allocation. Concretely:

1. **The `@experimental` ground pool is written by three-to-four modules that do not fully share a lock.** `PoiOffensiveBotModule` (100-tick), `PoiGarrisonBotModule` (100-tick) and `CaptureCoordinatorBotModule` (75/150-tick) all respect the `PoiGoalGuard` ledger — but **`LayeredDefenceBotModule` does not consult the ledger at all** (`LayeredDefenceBotModule.cs`; no `Ledger`/`IsCommitted` reference in the file — verified). It gates only on `actor.IsIdle` + its own 250-tick `assignedAtTick` cooldown. So whenever an offense unit reaches its objective and idles for a moment, LayeredDefence can pull it back toward the line; offense re-grabs it on its next eval. That is a multi-second "forward, get pulled back, forward" loop with no single owner.
2. **PoiOffensive re-allocates its own units every 100 ticks on scores that wobble every 25 ticks.** `RescaleByBelievedFields` recomputes each axis score from the control/danger fields (which recompute on a 25-tick cadence) every evaluation, `AllocateProportional` re-derives per-axis sizes from those scores, then the module **sheds the units farthest from each axis target** and **tops up with the nearest free units** (`PoiOffensiveBotModule.cs:535-571`). A unit near an axis boundary is repeatedly shed from axis A (it is "farthest") into the pool and recruited onto axis B whose target lies the other way — it literally walks back. The ledger TTL does not prevent this because the module releases the commit itself when it sheds (`:544`).
3. **The stock squad FSMs re-issue on a 75-tick heartbeat and contain an explicit Stop→AttackMove regroup toggle** (`GroundStates.cs:158-161`) and target re-selection each update — the `@stable` profile and every heli squad still run this. Even on `@experimental` (ground handed off), the **heli** FSM re-selects its closest-enemy target every 75-tick `Update()` and swings between them.

The fix the owner intuits — "a central Brain that assigns missions and doesn't touch squads without good reason" — is exactly the missing abstraction. This doc specifies:

- A **`SquadBrainBotModule`** (per bot player, `@experimental`-gated) that each **strategic tick** (default 100t) reads the influence stack and friendly state and emits four outputs: a **posture** (Attack / Hold / Consolidate — the item-18 decision, driven by an **Aggressiveness** scalar rather than a bot archetype), a ranked set of **attack vectors** (objective + approach corridor), a ranked set of **defend POIs**, and a **force allocation** (how many units, by role, to each). It also detects **granted opportunities** — undefended sectors with a free corridor forward — and pushes into them (opportunistic advance) so the bot exploits openings instead of freezing on its captures. It does not issue unit orders directly.
- A **tunable-parameter architecture** (§2.7): Aggressiveness and future sliders (risk tolerance, capture-vs-combat priority) are integer `Info` scalars that shift the pure decision math via a `base ± slope` convention — swept per-match by the test harness to find the baseline, eventually a lobby slider. No discrete "Rush/Turtle" personalities.
- A **`Mission`** object as the single durable thing a group of units is committed to: an objective, a route hint, a commitment window, a role composition, and a small **abort-trigger set**. Squads execute a mission and are **not re-tasked mid-mission** unless an abort trigger fires. The Brain owns the set of live missions; executors own within-mission movement.
- A migration that turns the existing modules into **pure executors** (given a mission, drive the units; report status up) and makes the Brain the **only** thing that decides *which units pursue what* — closing the multi-writer gap that causes (1).

Phase 1 is deliberately small and matches the parallel `auto/mission-commitment` work: **make the commitment a real lock and add hysteresis to the existing modules** (extend `PoiGoalGuard` so every ground order-source — including LayeredDefence — must hold a unit's commitment before ordering it, and stop PoiOffensive shedding committed units mid-approach). That alone should kill the visible loop. Later phases introduce the Brain and Mission objects and retire the per-module re-decide loops.

---

## 1. Current-state forensic map — every ground order source at `493f76a4`

This section is evidence. Each row is a place that issues movement/retarget orders to combat units, its cadence, whether it respects the shared `PoiGoalGuard` ledger, and its specific contribution to the observed dithering.

### 1.0 Which modules touch ground units, per profile

From `ai.yaml` (both `@experimental` and `@stable` bots instantiate all of these; the air `SquadManagerBotModule` sets `IgnoreGroundUnits: true` for both, `ai.yaml:665,719`, so **ground is owned by the Poi stack**, not `GroundStates.cs`):

| Module | Gate | Cadence | Reads ledger? | Order emitted |
|---|---|---|---|---|
| `PoiOffensiveBotModule@experimental` / `@stable` | exp / stable | `ReevaluateInterval=100` (`:57`) | **Yes** (`BuildFreePool` `:737`, commits `:823`) | grouped `AttackMove` per axis (`:944-946`) |
| `PoiGarrisonBotModule@experimental` / `@stable` | exp / stable | `ReevaluateInterval=100` (`:56`) | **Yes** (`:403`, commits `:460`) | grouped `AttackMove` per garrison (`:471`) |
| `CaptureCoordinatorBotModule@*.tecn` | exp / stable | `ScanInterval=75`, `DefenseScanInterval=150` (`:41,:44`) | **Yes** (`:448,:467,:926`) | `CaptureActor` (`:947`), escort/defender `AttackMove` (`:1160,:1257`) |
| `LayeredDefenceBotModule@experimental` / `@stable` | exp / stable | `ScanInterval=75`, `AssignCooldownTicks=250` (`:44,:48`) | **NO** (no `Ledger` reference in file) | per-unit `AttackMove` to a line slot (`:400`) |
| `HelicopterSquadBotModule` (+ `HelicopterStates.cs`) | any & (exp / !exp) | squad `AttackForceInterval=75` (`SquadManagerBotModule.cs:72`) | via air squad membership only | `AttackMove`/`Attack`/`ReturnToBase` (`HelicopterStates.cs:501,504,599`) |
| `SquadManagerBotModule@*.fixedwing` + `GroundStates.cs` | exp / stable | `AttackForceInterval=75` | `ExcludeTacticallyCommitted` (ledger-aware) | grouped `AttackMove` (only ground on `@stable`-style profiles; `IgnoreGroundUnits` removes it on both current bots) |
| `LaneAmbushBotModule@experimental` | exp | own countdown | **Yes** (commits `ambush:<id>`) | single `AttackMove` + stance |

The shared lock is `PoiGoalGuard.Ledger` (`PoiGoalGuard.cs`), a per-unit `Actor → {Objective string, ExpiresAtTick, CommitCount}` table (`GoalGuardLedger<Actor>`, `:39-117`). It records **that** a unit is taken and **until when** — nothing about the plan, the route, the group, or the composition (`Commitment` struct `:41-51`). `DefaultCommitmentTicks=300` (`:129`).

### 1.1 Root cause A — LayeredDefence is a ledger-blind second writer

`LayeredDefenceBotModule.AssignPositions` iterates `world.Actors`, filtering on `actor.IsIdle` (`:265`), an actor-type/role eligibility test (`:271-292`), its own per-unit cooldown `assignedAtTick[actor] > cooldownExpiresBefore` (`:294`), a transport-reservation check (`transport.IsPassengerReserved`, `:307`), and an out-of-ammo guard (`:301`). It then issues `AttackMove` to a computed line slot (`:400`) and stamps `assignedAtTick[actor] = world.WorldTick` (`:401`).

It **never checks `goalGuard.Ledger.IsCommitted`**. The only thing preventing it from stealing an offense unit is the `actor.IsIdle` gate — but an offense unit is idle precisely at the moments the offense module is *between* evals (order completed, waiting for the next 100-tick `Reevaluate`). During that window LayeredDefence (75-tick, phase-offset) can and does re-task it toward the defensive line. When offense next evaluates, the unit is committed to `offense:<id>` in the ledger but is now standing on the line; `PruneAxes`/rebalance re-orders it forward. Period ≈ `max` of the two cadences with the two phase offsets — a several-second oscillation, matching the owner's "one way, then the other, stuck in a loop."

*Note:* `assignedAtTick` and `AssignCooldownTicks=250` damp LayeredDefence against *itself*, and the transport check is an ad-hoc cross-module lock done outside the ledger (`:307`) — evidence the author already needed a lock and reached for a bespoke one. The ledger is the right lock; LayeredDefence simply predates being wired to it for the combat pool.

### 1.2 Root cause B — PoiOffensive reshuffles its own units on jittering scores

`Reevaluate` (`PoiOffensiveBotModule.cs:371-594`) runs this every 100 ticks:

1. Score targets; when Stage-F repoint is on, `RescaleByBelievedFields(targets, tick)` (`:468-469`) recomputes each axis score from `ControlField.ScoreAt` (balance-of-power ring) and `DangerFieldLayer.GroundDanger` — **both of which recompute every 25 ticks** (influence-stack `UpdateInterval`). So axis scores wobble at 25-tick granularity even when the battlefield is static.
2. `AllocateProportional(scores, total, minAxisSize)` (`:526`) re-derives each axis's target size from those wobbling scores.
3. **Shed surplus**: for an axis now over-size, take the units *farthest from the axis target* and return them to the free pool, releasing their ledger commit and setting `HasOrdered=false` (`:535-549`).
4. **Top up**: for an axis now under-size, recruit the *nearest free units* (`:559-571`), setting `HasOrdered=false`.

A unit sitting between two axis targets is, by construction, "farthest" from whichever axis it is on. When scores jitter and sizes shift by even one, that unit is shed from axis A and immediately re-recruited by axis B (nearest-free ordering) whose objective is in the opposite direction → a fresh `AttackMove` the other way (`HasOrdered=false` forces re-issue at `:916`). The `AxisCommitmentTicks=250` TTL is powerless here because **the module releases the commit itself** during the shed (`:544`). The sticky-target hysteresis (`ReassignScoreThresholdPct=30`, `:717`) protects *which targets* are chosen but not *which units go to which surviving target*.

This is a genuine within-module loop independent of LayeredDefence. It is worst with `StrategicRepointEnabled` on (believed-field score jitter) and with ≥2 axes of comparable score.

### 1.3 Root cause C — the stock FSM heartbeat re-issues and toggles Stop/Go

`GroundStates.cs` (the `@stable` ground path and the shape the heli path mirrors) is driven by `Squad.Update()` → `FuzzyStateMachine.Update()` on the 75-tick `AttackForceInterval` (`SquadManagerBotModule.cs:264-268`). Within:

- `GroundUnitsAttackMoveState` re-issues the grouped `AttackMove` whenever the leader's cell changed since last update (`lastLeaderLocation`, `:130-132,:161,:174`) — i.e. essentially every heartbeat while advancing.
- An explicit regroup toggle: if too few squadmates are near the leader it issues `Stop` to the leader (`:158`), otherwise `AttackMove` (`:161`). As the trailing units catch up and fall behind across heartbeats, the leader alternates Stop/Go — the literal "go for a second, stop, go" the owner describes.

On the current two-bot roster ground is handed to the Poi stack (`IgnoreGroundUnits`), so this path is live only for **heli** squads (`HelicopterStates.cs`) and any future profile that returns ground to SquadManager. The heli FSM re-picks its closest-enemy target each 75-tick update and transitions Approach→Attack→Withdraw→Return (`:361,:448,:410,:384`); with two comparable targets it swings between them. The `BusyAttackMove` guard (`:255,:495`) prevents *cancelling an in-flight FlyAttack* each tick but does not prevent a *target switch* on the next heartbeat.

### 1.4 Why the existing dampers are not enough

The codebase already carries six anti-oscillation dampers (ledger TTLs, LayeredDefence cooldown, sticky-target threshold, repath-cells gate, regroup timeout, the 75-tick heartbeat sized to survive re-issue — catalogued in [`260722_bot_brain_architecture.md`](260722_bot_brain_architecture.md) §1.6). They fail against the three root causes above because:

- **A** is a *missing* lock, not a mis-tuned one — no damper on LayeredDefence's side references the offense commitment.
- **B** is the module defeating its own damper (it releases the commit to reshuffle).
- **C** is the heartbeat *being* the re-issue mechanism.

The Brain removes the causes rather than adding a seventh damper: one writer for the combat pool (kills A), missions that are not resized mid-flight (kills B), event/status-driven re-tasking instead of a heartbeat (kills C).

---

## 2. Brain architecture

### 2.1 Placement and shape

`SquadBrainBotModule` — a `[TraitLocation(SystemActors.Player)] ConditionalTrait, IBotTick`, one instance per bot player, `RequiresCondition: enable-ai-experimental` (never a single world-actor lookup — the shared-`@poi` twin trap from influence-stack.md applies). It ticks on a `StrategicInterval` countdown (default 100, staggered at `TraitEnabled` via `world.LocalRandom.Next` exactly like `PoiOffensiveBotModule.cs:356`). It owns:

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
               int territoryTrend, bool economyMet, int aggressiveness /*0..100*/)
```

Battlefield inputs (all integers already available):
- **Force ratio** `R` = own committed+free combat value ÷ believed enemy value ×100 (`BeliefStore` value sum, identity-weighted like `AmbushThreatValue`).
- **SR threat** `H` = max `DangerFieldLayer.GroundDanger` inside our own SR contestation ring, and whether any believed enemy contact sits inside it (existential — see [`supply-route.md`](../../DOCS/reference/supply-route.md)).
- **Territory trend** `T` = signed sum of `ControlField.ScoreAt` over the map (are we gaining or losing ground), sampled at the coarse grid.
- **Economy** `E` = derrick/income count and whether a supply-truck/economy floor is met (proxy: own income structures captured).

**Aggressiveness is a first-class input, not a bot archetype.** Rather than discrete "Rush/Turtle" personalities, a single `Aggressiveness` scalar (0..100) **shifts the posture thresholds** so the same code produces a cautious bot at 20 and a reckless one at 80. It enters the pure math as the two ratio cutoffs:

```
attackCut = AttackRatioBasePct - (aggressiveness - 50) * AggressionRatioSlopePct / 100
holdCut   = HoldRatioBasePct   - (aggressiveness - 50) * AggressionRatioSlopePct / 100
```

So higher aggressiveness lowers the force-ratio required to commit to Attack (attacks at a disadvantage) and lowers the floor below which it retreats to Consolidate (holds ground longer before falling back). At `Aggressiveness = 50` the cuts equal their base values → the tuned neutral bot; the whole formula is integer-only and swept in testing (§2.7).

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
    uint   ObjectiveActorId;         // for validity checks (still alive / still enemy)
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
- **Committed** — launch condition fires (all forces Ready, **or** `StagingDeadlineTick` reached with `≥MinForcesReady` — the anti-deadlock downgrade to single-axis). The assault order is issued **once**; `Fires` role units take standoff (reuse `OrderFiresStandoff`), `Recon` leads, `AirDefence` stays with the body. **This is the only tick the objective order is issued** — no heartbeat re-issue. Re-issue happens only on an abort→re-plan or a route-hint change past `RepathThresholdCells`.
- **Aborting** — an abort trigger fired (§3.3). Units released from the ledger back to the free pool; a `Retry` proposal may be emitted with a cooldown (retryable missions).
- **Resolved** — objective condition met (POI captured / threat cleared / SR ring clear). Ledger released.

State transitions happen only in the Brain tick, evaluated in deterministic mission-Id order.

### 3.3 Abort-trigger set — the ONLY reasons a Committed mission is touched

A `Committed` mission is otherwise left entirely alone (this is the whole point). It transitions to `Aborting` iff **one** of these fires (checked cheaply each strategic tick):

1. **Objective invalid** — `ObjectiveActorId` dead, no longer enemy (captured/neutralized), or `PoiMap` no longer lists it. (Structures are public facts; no fog leak.)
2. **Danger spike beyond threshold** — max `DangerFieldLayer.GroundDanger` sampled along the remaining route or at the objective rose above `AbortDangerThreshold` **and** by at least `AbortDangerDeltaPct` over the value when the mission committed (a rising believed AA/AT envelope the mission would grind into). Hysteresis in the *delta* prevents baseline jitter from tripping it — the Stage-E/F lesson that the territory baseline stacks additively.
3. **Materially better opportunity** — a new proposal outscores this mission's objective by **more than `AbortReassignThresholdPct`** (a wider margin than the keep-sticky threshold, so only a clearly better target pulls a committed force). Enemy-movement-driven: e.g. the enemy SR ring became contestable, or an undefended income POI appeared behind a collapsing front.
4. **Combat-ineffective** — the mission's live force value fell below `AbortStrengthPct` of `InitialForceValue` (losses), or `<MinAxisSize` units remain. The remnant retreats/merges rather than dribbling into the objective.
5. **Posture override** — the Brain flipped to **Consolidate** with SR threat (§2.3 row 1): all non-defensive missions abort and the nearest sufficient force is redirected home. This is the existential interrupt and is allowed to override even a healthy Committed mission.

TTL (`CommitmentExpiresTick`) is a **backstop**, not a trigger — a healthy mission refreshes it each tick, so it only expires if the Brain stopped ticking the mission (dead/stuck), where the ledger prune reclaims the units anyway. This inverts today's model where TTL expiry *is* the re-decide clock.

Triggers 2–4 each carry a hysteresis margin so that the enemy jitter that causes today's loop cannot trip them; that is the design's contract against re-introducing the dither.

---

## 4. Integration + migration — modules become executors

The end state: the Brain is the **only** decider of which units pursue what; the other modules become **executors** that, given a mission (or a stance from the Brain), drive units and report status up. During transition, coexistence is safe because everything routes through the one ledger.

| Module | Today | Under the Brain | How it becomes an executor |
|---|---|---|---|
| `PoiOffensiveBotModule` | scores + allocates + orders offense axes (root cause B lives here) | **Assault/Pincer/Raid executor**: given a mission's objective + route + unit set, issue the grouped `AttackMove` **once** and hold; the Brain owns axis count/sizing/target selection | strip `Reevaluate` steps 2–8 (scoring, `SelectStickyTargets`, `AllocateProportional`, shed/top-up); keep `CommitAndOrder`, `OrderFiresStandoff`, Stage-E detour, cohesion. Reads its mission list from the Brain. |
| `PoiGarrisonBotModule` | scores + orders garrisons | **Defend executor**: drive units to a Brain-chosen defend POI, hold | same shrink; keep the grouped move + garrison hold |
| `LayeredDefenceBotModule` | ledger-blind line dispatcher (root cause A) | **Line/screen executor for Defend missions** — and, critically, recruits **only from the free pool** (ledger-checked) | **Phase 1 change**: add the ledger `IsCommitted` check to its reserve filter (`:265` region) so it can never touch a committed unit. Later: line slots come from the Brain's defend allocation, not its own scan. |
| `CaptureCoordinatorBotModule` | own capture/defense scans | **CaptureEscort executor**: the Brain proposes CaptureEscort missions; escorts become mission members (fixing the never-committed-escort bug structurally) | capture ordering stays; escort recruitment reads the mission composition |
| `HelicopterSquadBotModule` / `HelicopterStates` | own target re-selection each 75t (root cause C for air) | **Air-strike executor**: the Brain assigns a heli mission an objective; the FSM keeps its standoff/danger-nav micro but does not re-pick the strategic target | gate the FSM's target re-selection behind "no Brain mission assigned"; when a mission exists, the objective is fixed until abort |
| `SquadManagerBotModule` (air) | air squad former | unchanged for now (air squads); a later phase folds air into missions | — |
| `UnitBuilderBotModule` / `AdaptiveProductionBotModule` | production | consume unmet mission `Composition` via `IBotRequestUnitProduction` (the `MaintainTecnFloor` pattern) | closes the composition↔production loop; no restructure |

**Coexistence invariant during migration:** at every phase there is exactly one writer per unit *per tick* because every writer holds-or-checks the ledger before ordering. The Brain and a not-yet-migrated module can both run; the ledger keeps them from fighting. The migration order is chosen so the ledger-blind writer (LayeredDefence) is fixed **first** (Phase 1), because it is the one that can currently violate the invariant.

---

## 5. Phased implementation plan

Each phase is independently shippable, default-off, `@experimental`-gated, byte-identical for `@stable`/legacy when its flag is off, and priced on the ai-bench ladder before promotion.

### Phase 1 — kill the dither with commitment + hysteresis (aligns with `auto/mission-commitment`)

Smallest change that removes the visible loop, matching the in-flight "commitment ledger extension of `PoiGoalGuard`" on branch `auto/mission-commitment`.

- **1a. Make LayeredDefence honor the ledger.** Add `goalGuard.Ledger.IsCommitted(actor, tick)` to the reserve filter (`LayeredDefenceBotModule.cs:263-327`), behind a default-off `RespectCommitmentLedger` flag set only on `@experimental`. Kills root cause A directly.
- **1b. Stop PoiOffensive shedding committed units mid-approach.** In the surplus-shed step (`:535-549`), do not shed a unit whose axis commitment is still fresh and whose distance-to-target is decreasing (it is en route); prefer shedding genuinely idle/rear units. Gate on a default-off `StickyAxisMembership` flag. Blunts root cause B without the full Brain.
- **1c. Widen the believed-field score damping / raise `ReevaluateInterval` coupling** so 25-tick field jitter cannot flip proportional sizes every eval (quantize scores, or only re-allocate when a score crosses a band). Blunts the remaining root cause B jitter.
- **Touched files:** `LayeredDefenceBotModule.cs`, `PoiOffensiveBotModule.cs`, `ai.yaml` (`@experimental` flags only). **Byte-identity gate:** all three flags default off → `@stable`/legacy unchanged; per-profile trait instances make the single flag sufficient (no `!enable-ai-experimental` needed here). **NUnit-pinnable seams:** a pure `ShouldShedUnit(distTrend, committed, idle)` predicate; a pure `QuantizeAxisScore` for 1c. **Size:** small (~1 day); this is the quick-win the owner wants fastest.

> Coordinate with `auto/mission-commitment`: if that branch already extends `PoiGoalGuard` with the LayeredDefence lock (1a) or a per-unit "sticky" flag, Phase 1 here should *consume* that extension rather than duplicate it — Phase 1 is the same commitment-ledger idea. Rebase onto it; keep 1b/1c as the additions it may not cover.

**1d. Slider infrastructure (cheap, unlocks testing — do it here).** Stand up the tunable-parameter plumbing (§2.7) even before the Brain exists: add `Aggressiveness` (and the empty `*BasePct`/`*SlopePct` scaffolding) as `Info` fields wherever the first consumer lands, and thread it through one pure function so a sweep harness can vary it per match. This is a few `Info` fields + one pure helper; it costs almost nothing and lets the owner start sweeping the aggressiveness baseline immediately (even against the Phase-1 offense tuning), rather than waiting for Phase 3. **NUnit-pinnable:** the `base ± slope` shift helper. **Size:** trivial (~half a day), bundled into Phase 1.

### Phase 2 — the Mission object + single-writer free pool

- Introduce `Mission` (§3) and a thin `SquadBrainBotModule` that, for now, only wraps the *existing* offense/garrison/defend allocation as missions (no new posture logic yet) and enforces the **single-writer** rule: every combat order source recruits from the ledger-checked free pool, and the Brain is the only creator/aborter of missions.
- Migrate `PoiOffensiveBotModule` and `PoiGarrisonBotModule` interiors to **executors** driven by mission objects; delete their independent scoring/allocation once the Brain reproduces it.
- **Touched files:** new `SquadBrainBotModule.cs`, `Mission.cs` (+ pure `MissionAbort` math), `PoiOffensiveBotModule.cs`, `PoiGarrisonBotModule.cs`, `ai.yaml`. **Byte-identity gate:** `BrainEnabled=false` default; when off the executors fall back to their current self-scoring path (keep it behind the flag until priced). **NUnit-pinnable:** mission state machine transitions; abort-trigger predicates (`ObjectiveInvalid`, `DangerSpike`, `Ineffective`) as pure functions over integers. **Size:** medium (~2–3 days).

### Phase 3 — posture (item 18) + opportunistic advance + role composition + full slider set

- Add `BrainPosture.Decide` (§2.3) driven by the `Aggressiveness` scalar stood up in 1d, plus the remaining sliders (`RiskTolerance`, `CaptureVsCombatPriority`, §2.7). Posture gates mission kinds; composition (§2.4/§4) requests roles from `UnitRoleResolver` and emits production demand for shortfalls.
- Add **opportunistic advance** (§2.6): the free-path/undefended-sector detector + `Advance` mission kind + the extend-while-clear behavior, with aggressiveness scaling eagerness/depth. This is the direct fix for "bots capture POIs but never exploit the opening."
- Migrate `LayeredDefence` line slots and `CaptureCoordinator` escorts to Brain-issued Defend/CaptureEscort missions (removes their independent scans).
- **Touched files:** `SquadBrainBotModule.cs`, `LayeredDefenceBotModule.cs`, `CaptureCoordinatorBotModule.cs`, `ai.yaml`. **Byte-identity gate:** posture defaults to a permissive "Attack-always" table + `Aggressiveness=50`/neutral sliders reproducing today when off; Advance behind its own default-off flag. **NUnit-pinnable:** `BrainPosture.Decide` truth table over `(R,H,T,E,aggressiveness)`; `AdvancePolicy` sector-scoring + eagerness; the `base ± slope` shift; composition-fill ordering. **Size:** medium (~2–2.5 days). **Sweep on promotion:** run the aggressiveness grid (§2.7) to pick the baseline before default-on.

### Phase 4 — staging, Pincer, air missions, event-driven re-tasking

- Real staging + launch conditions (Pincer over two corridors, same-tick launch), heli/air missions (fix root cause C for air by fixing the strategic target under a mission), and the deterministic event bus so abort/re-plan is event-driven rather than tick-swept.
- **Touched files:** `SquadBrainBotModule.cs`, `HelicopterStates.cs`/`HelicopterSquadBotModule.cs`, corridor math. **Byte-identity gate:** each behind its own flag. **NUnit-pinnable:** corridor angular-separation math; launch-condition evaluation. **Size:** medium-large (~2–3 days), only if Phases 1–3 price positively.

Effort total is consistent with the 260722/260720 costing (board+lifecycle ~1–1.5d, migration ~2–3d). Phase 1 is the highest-leverage, lowest-risk step and should ship first regardless of whether the later phases proceed.

---

## 6. Determinism constraints (load-bearing — same contract as the influence stack)

Every part of the Brain must satisfy the invariants in [`influence-stack.md`](../../DOCS/reference/influence-stack.md) §Invariants:

- **Zero `SharedRandom`/`LocalRandom` draws in decision logic.** Mission Ids are a per-player integer sequence, not RNG. The only permitted draw is the `TraitEnabled` stagger offset (already the established pattern, `PoiOffensiveBotModule.cs:356`), which does not affect decisions. Any genuinely stochastic tie (there should be none) uses `SharedRandom`, never `LocalRandom`.
- **Integer-only math.** Force ratios, posture thresholds, allocation, danger/control sampling are all integer (percent-scaled `/100`), matching `PoiOffenseMath`/`ControlFieldMath`.
- **Deterministic iteration.** Missions iterate by `Id`; forces by index; units by `ActorID`; proposals/events ordered by `(tick, priority, ActorID)`. No `Dictionary` iteration order reaches a decision (the `firesHeldFire`/`lastCohesion` maps in PoiOffensive are already handled this way).
- **`@experimental`-gated via `RequiresCondition`.** `SquadBrainBotModule` is `enable-ai-experimental`; every new consumer flag defaults to the frozen behavior. Per-profile trait instances mean a single default-off flag suffices (first gating pattern); any consumer bolted onto a **shared** module (none planned, but if LayeredDefence's fix rides a shared instance) must double-gate `Info.Flag && InfluenceStack.Participates(player)` (second pattern).
- **No `RenderPlayer`/wall-clock/off-sim state.** All Brain state lives in per-player module fields; nothing reads `world.RenderPlayer` or `DateTime`. Same-tick multi-force launch is trivially safe (issued within one module tick).
- **Byte-identity when off.** With every flag off, `@stable`/Normal/legacy and the frozen `@stable` twin are byte-identical — proven the way Stage-F was: the suppressed/disabled branch must collapse verbatim to the current expression, and no new RNG draw shifts the stream.

---

## Open questions for the owner

1. **Phase 1 scope split with `auto/mission-commitment`.** If that branch already adds the LayeredDefence ledger lock, should this Phase 1 be *only* items 1b/1c/1d (the PoiOffensive self-shuffle fixes + slider scaffolding)? Recommend: yes — consume the branch's ledger work, add the shed/jitter fixes and slider plumbing on top.
2. **Aggressiveness baseline + difficulty coupling.** §2.7 makes Aggressiveness a swept `Info` scalar; after the sweep picks a neutral baseline, do we also want it wired to difficulty tiers (Easy = low, Brutal = high) in the same pass, or keep one tuned value until the Brain proves out? Same question for `RiskTolerance` / `CaptureVsCombatPriority`.
3. **Opportunistic advance vs. over-extension.** Advance keeps pressure up (the requested behavior) but at high aggressiveness can over-extend into a re-formed enemy line. The abort triggers (danger spike / combat-ineffective) are the safety net — is that enough, or do we want an explicit "don't advance more than N sectors past the nearest defended POI" leash beyond `AdvanceMaxSectors`?
4. **Heli missions (Phase 4) vs leaving air on its FSM.** Air dithering (root cause C) is less visible than ground; is folding air into missions worth Phase 4, or park it?
