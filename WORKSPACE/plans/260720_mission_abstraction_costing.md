# Mission Abstraction — Full Structural Costing (RETHINK prep)

**Date:** 2026-07-20
**Researched against:** main @ `d0be62ec` (HEAD at time of recon; local main is 74 commits ahead of origin/main — unpushed, expected).
**Mode:** READ-ONLY design recon. No code changes, no builds, no runs.
**Builds on:** `WORKSPACE/plans/260720_capture_reliability_cycle1.md` §3 (structural option, judged premature for cycle 1) and `WORKSPACE/ai-bench/reports/260720_system_report.md` §6 (structural roadmap, item 1).
**Question for the RETHINK:** should we build a first-class *Mission* abstraction now, and if so, what is the smallest first step and what does it cost?

---

## 0. TL;DR verdict

**Do step 1 (CaptureMission, internal refactor of `CaptureCoordinatorBotModule`, kill-switch–gated) at the next capture cycle IF the cycle-1 patches (TTL raise + `INotifyKilled`) land the S1 capture bar at ≥6/10 but the instrumented failure logs still show escort desync / lost-TECN-no-retry as the residual failure mode.** That is the exact scenario the Mission model is shaped to fix, and step 1 is ~1–1.5 days including tests.

**Defer the full unification (offense + garrison → generic missions) until S2 is on the ladder.** The offense/garrison duplication is real (~90% structurally identical, quantified in §1.2) but it is *stable, working, unit-tested* duplication — refactoring it now is pure debt paydown with no ladder-score upside, and it risks the frozen `@stable` control. Unify it opportunistically when S2 (contested-mid / SR-pressure) forces a new mission *type* anyway.

Decision rule in full: §4.3.

---

## 1. PROBLEM RESTATEMENT — what the per-module pattern costs today

The experimental AI is five `IBotTick` player traits that each independently (a) pick targets, (b) claim units, (c) issue orders, (d) reconcile on the next scan. Three of them (`CaptureCoordinator`, `PoiOffensive`, `PoiGarrison`) coordinate unit ownership through the one shared `PoiGoalGuard.Ledger`; two (`LayeredDefence`, `MountedTransport`) coordinate *outside* the ledger by actor-type exclusion and their own reservation sets. The costs below are what that shape imposes.

### 1.1 No mission lifecycle: no retry, no abort, no stall detection

There is no object anywhere that represents "attempt objective O with force F; if it fails, retry N times, else abort." Re-attempts are an emergent side effect of the scan loop plus unit availability:

- **Capture.** `QueueCaptureOrders` re-runs every `ScanInterval: 75` (`CaptureCoordinatorBotModule.cs:185-189`). If a committed TECN dies, `ReconcileGuardCommitments` (`:429-448`) prunes it via `Prune`'s keep-predicate (`:432`) and the target's commitment is released (`:446`); the *next* scan re-dispatches **only if a fresh idle TECN exists** (`:241-242`). There is no `RetryCount`, no "this target has failed twice, escalate the escort," no abort. Cycle-1 recon diagnosed exactly this as failure mode **F-1** (`260720_capture_reliability_cycle1.md:17-45`): the lone-TECN loss is invisible and un-retried until production refills the pool.
- **Offense / Garrison.** Axes/garrisons "retire" (`ReleaseAxis` `PoiOffensiveBotModule.cs:397-408`; `ReleaseGarrison` `PoiGarrisonBotModule.cs:373-384`) when their target drops out of score selection or the group falls below `MinAxisSize`. A group that walks into a target and gets wiped is simply re-pooled on the next reeval — **silently**. No "mission failed" signal exists to log, escalate, or feed telemetry (roadmap item 6 wants exactly this).

**Cost:** the diagnosed core weakness — "goals but no operations" (system report §6 item 2) — is structural. Each module knows *what* it wants this tick but nothing owns the *attempt over time*.

### 1.2 Duplicated target-selection / commit / order machinery

`PoiOffensiveBotModule` (504 LOC) and `PoiGarrisonBotModule` (445 LOC) are ~90% structurally identical. The only real differences are the scoring source (`GetOffensiveTargets` vs `GetDefendTargets`), the sizing math (`PoiOffenseMath.AllocateProportional` vs `PoiGarrisonMath.AllocateGarrisons`), and the ledger key prefix (`offense:` vs `defend:`). Line-for-line the shared skeleton is:

| Concern | PoiOffensive | PoiGarrison | Identical? |
|---|---|---|---|
| Live-mission inner class (`Axis`/`Garrison`: TargetId, Cell, Pos, Score, Name, OrderedCell, HasOrdered, Units) | `:91-102` | `:98-109` | yes, field-for-field |
| Lazy `poiMap`/`goalGuard` resolution | `:142-155` | `:149-162` | yes |
| Prune dead/lost units + sweep orphan commitments | `PruneAxes :344-366` | `PruneGarrisons :321-342` | yes (differs only in `offense:`/`defend:` prefix at `:360` / `:336`) |
| `BuildFreePool` (eligible + unclaimed + uncommitted) | `:320-330` | `:297-307` | **byte-identical logic** |
| `IsEligibleCombatUnit` (positionable + attack + not-aircraft + not-excluded) | `:332-341` | `:309-318` | **byte-identical** |
| `CommitAndOrder` (recommit loop + repath gate + grouped `AttackMove`) | `:368-394` | `:344-370` | yes |
| `ReleaseAxis`/`ReleaseGarrison` + `RetireAll` | `:397-415` | `:373-391` | yes |
| Objective-key helper | `OffenseObjectiveKey :417` | `DefendObjectiveKey :393` | yes |
| 7-step `Reevaluate` pipeline (prune → score → free-pool → size → balance-shed → balance-topup → order) | `:140-280` | `:147-287` | yes, step-for-step |

`CaptureCoordinatorBotModule` carries a **third** variant of the same commit/reconcile idiom (`IssueCaptureOrder :392-408`, `ReconcileGuardCommitments :429-448`) — different enough (it drives a consumable single unit + escort, not a sized group) that it's not a clean copy, but it re-implements the same "commit on issue, release on death/expiry/invalid" contract by hand.

**Cost:** every change to the claim/order contract (e.g. "also commit escorts," "add a stall timer," "log mission lifecycle events") must be made in 2–3 places and kept in sync. This is latent bug surface, not a current outage.

### 1.3 Escort mis-coordination — escorts are not a durable sub-force

`DispatchEscort` (`CaptureCoordinatorBotModule.cs:486-502`) is fire-and-forget in two distinct ways:

1. It `AttackMove`s the escorts to `target.Location` — the derrick cell (`:495`) — **not** to the TECN. If the TECN approaches from another angle the escorts screen the wrong place (cycle-1 **F-4**, `260720_capture_reliability_cycle1.md:71-82`).
2. **The escorts are never committed to the ledger.** `DispatchEscort` issues the `AttackMove` and adds them to a *per-tick* `escortsRecruitedThisTick` set (`:497-498`) — but never calls `goalGuard.Ledger.Commit`. So ~100 ticks later `PoiOffensiveBotModule.BuildFreePool` (`:320-330`) sees them as uncommitted and can pull them onto an attack axis, abandoning the escort mid-approach. (The `IssueCaptureOrder` path commits the *TECN* at `:395-396`, but not its escort.)

**Cost:** the escort is not a lifecycle-owned force — it's a one-shot nudge that any other module can immediately steal. This is both the F-4 approach-screen bug *and* a "penny-packet" leak.

### 1.4 Penny-packet commitment, no massing/staging

The capture escort is `EscortSize: 2` fire-and-forget (`ai.yaml:151`); defense summons `DefenseSummonCount: 3` (`ai.yaml:156`); offense axes dribble at `MinAxisSize: 3` with no staging/rendezvous before commitment. Nothing stages a force to full strength before it moves. This is roadmap item 2 (operations layer) surfacing inside the capture/offense modules: forces are committed as they trickle in, not massed then launched.

**Cost:** the AI reads as continuous trickle rather than deliberate operations — directly against the doctrine goal (`DOCS/design/ai-realism.md`: "disperse under observation and mass only at the decisive point"). A Mission with an explicit `Staging → Executing` lifecycle is the natural home for a "wait for min force, then go" gate.

---

## 2. DESIGN — a concrete Mission model

### 2.1 Shape: one unified Mission, typed by objective

Use **one `Mission` class** with a `MissionKind` enum, not a class hierarchy. Rationale: §1.2 shows the machinery is already identical across kinds; the *only* things that vary are (a) the scoring source that generates the mission, (b) the sizing rule, and (c) a few per-kind execution knobs (escort? garrison-on-cell vs attack-move? consumable capturer?). Those are data + small strategy hooks, not a type tree. A hierarchy would re-introduce the per-module duplication we're trying to kill.

```
enum MissionKind { Capture, Assault, Pressure, Garrison, Escort }   // Secure folds into Assault; DenyCapture into Pressure

enum MissionState { Staging, Executing, Retrying, Complete, Aborted }

class Mission
{
    uint        Id;                 // stable, monotonic
    MissionKind Kind;
    uint        TargetActorId;      // POI / enemy structure / SR / own POI
    CPos        TargetCell;
    WPos        TargetPos;
    long        Score;              // last scoring pass
    int         Value;
    string      TargetName;

    MissionState State;
    int          DesiredSize;       // from the kind's sizing math
    List<Actor>  Units;             // committed force (INCLUDING escorts for Capture)
    Mission      Parent;            // Escort mission → its Capture mission (null otherwise)

    // lifecycle bookkeeping
    int  CreatedTick;
    int  LastProgressTick;          // updated when the force closes distance to target
    int  RetryCount;
    CPos OrderedCell;               // repath gate (moved verbatim from Axis/Garrison)
    bool HasOrdered;

    string ObjectiveKey => $"{Kind.ToKeyPrefix()}:{TargetActorId}";  // "capture:4721", "assault:88", ...
}
```

`ObjectiveKey` is deliberately the **same namespaced-string form already in the ledger** (`capture:` `offense:` `defend:` today — `CaptureCoordinatorBotModule.cs:413`, `PoiOffensiveBotModule.cs:417`, `PoiGarrisonBotModule.cs:393`). Keeping the string form means the ledger contract is unchanged and logs stay greppable (the whole `[exp-*]` marker ecosystem keeps working). Only the prefix set grows (`assault:`/`pressure:`/`escort:` alongside the existing three).

### 2.2 Lifecycle

```
                 generator emits/refreshes
   (none) ─────────────────────────────────► Staging
                                                 │  force reached DesiredSize (or MinViable + stage-timeout)
                                                 ▼
                                             Executing ──── target captured/destroyed/held-to-term ───► Complete
                                                 │  ▲                                                       │
              force wiped / no progress in       │  │ progress resumes                                      │ release units,
              StallTicks / target still valid    ▼  │                                                       │ drop mission
                                             Retrying ─── RetryCount > MaxRetries OR target invalid ──► Aborted
```

- **Staging** solves §1.4: recruit toward `DesiredSize`, hold near a rally, don't launch until the force is viable (or a stage-timeout fires so we don't stall forever). Garrison/Capture can set `stage-timeout = 0` (launch immediately) to preserve today's behavior exactly where staging isn't wanted.
- **Executing** is today's behavior: `CommitAndOrder`-equivalent issues the grouped `AttackMove`, repath-gated.
- **Retrying** solves §1.1: on force-wipe or `LastProgressTick` stalling past `StallTicks`, bump `RetryCount`, re-recruit, re-issue. For Capture this is the explicit "TECN died → request+redispatch" loop that F-1 lacks today.
- **Complete / Aborted** are the missing clean exit events (§1.1) — they emit one structured log line each, which is the seed of roadmap item 6 (telemetry-driven diagnosis).

### 2.3 Where it lives

**A plain `MissionBoard` class owned by a coordinator module — NOT a new player trait fetched by single-instance lookup.** This is the single most important design decision and it is dictated by the shared-`@poi` trap (§5). `PoiGoalGuard` is fetched via `player.PlayerActor.TraitOrDefault<PoiGoalGuard>()` (single-instance) at `PoiOffensiveBotModule.cs:153`, `PoiGarrisonBotModule.cs:160`, `CaptureCoordinatorBotModule.cs:181`; because two instances on one player throw, it is gated `enable-ai-experimental || enable-ai-stable` and shared, not twinned (`ai.yaml:120-122`, note `:601-607`). A `MissionBoard` **trait** would inherit that exact hazard.

Two placements, matching the two migration steps:

- **Step 1 (CaptureMission only):** the `MissionBoard` is a private field inside `CaptureCoordinatorBotModule` — the capture module owns its own missions. No new trait, no ledger change, no cross-module coupling. This is the low-risk beachhead.
- **Full unification:** promote `MissionBoard` to a plain class held by a new `MissionCoordinatorBotModule` (`IBotTick`), and demote `PoiOffensive`/`PoiGarrison` to **generators** that push mission *intents* onto the board. If the board must be reachable by more than one trait, expose it via `TraitsImplementing<T>()` (multi-instance-safe, the pattern SquadManager already uses — `ai.yaml:606-607`) or keep it single and gate on both bot conditions like `PoiGoalGuard`. **Do not** add a second single-instance-lookup trait without that gate.

### 2.4 How missions claim units — layer ON the ledger, never fork it

The Mission model is **a lifecycle owner that commits/releases through the existing `PoiGoalGuard.Ledger`.** The ledger stays the single source of unit-claim truth. Concretely:

- `Mission.Units` is the force; when a unit joins, `Ledger.Commit(unit, mission.ObjectiveKey, tick, ttl)`; when it leaves/mission ends, `Ledger.Release(unit)`. This is exactly what the three modules already do (`CaptureCoordinatorBotModule.cs:395-396`, `PoiOffensiveBotModule.cs:371-376`, `PoiGarrisonBotModule.cs:347-352`) — the Mission just *names* the pattern.
- `BuildFreePool` (already identical in offense/garrison, §1.2) becomes one shared method: "eligible units not committed to any mission." Unchanged semantics.
- **Escorts get committed too** (fixes §1.3 for free): the `Escort` sub-mission commits its units under `escort:<captureId>`, so `BuildFreePool` no longer re-steals them. The `Parent` link lets the escort follow the *capturer's* live position (fixes F-4) instead of the static target cell.

Because objective keys are unchanged strings and the ledger API (`Commit`/`Release`/`IsCommitted`/`Prune`/`TryGetObjective` — `PoiGoalGuard.cs:60-116`) is untouched, **the ledger is byte-compatible** and its unit tests (`PoiGoalGuardTest.cs`, 190 LOC) still pass verbatim.

### 2.5 How the score-floating axes become mission generators

Today `PoiOffensiveBotModule.Reevaluate` (`:140-280`) inlines selection + sizing + assignment + order. Split it:

- **Generator (stays in `PoiOffensiveBotModule`, thin):** read `poiMap.GetOffensiveTargets` (`PoiMap.cs:279`), run the *pure* `PoiOffenseMath.DesiredAxisCount` + `AllocateProportional` (`PoiOffensiveBotModule.cs:428-495`) and the sticky-selection hysteresis (`SelectStickyTargets :285-318`), and emit "there should be a `Assault`/`Pressure` mission on target T with desired size N." The generator owns *strategy* (which missions, how big).
- **Executor (shared `MissionBoard`/coordinator):** reconcile emitted intents against live missions, recruit from the shared free pool, commit, stage, order, retry, prune. The executor owns *mechanics* (the §1.2 skeleton, written once).

Crucially **the pure math classes do not move** — `PoiOffenseMath`, `PoiGarrisonMath`, `PoiScoring` stay engine-free and keep their existing unit tests (`PoiOffenseTest.cs` 170 LOC, `PoiGarrisonTest.cs` 187 LOC, `PoiMapTest.cs` 225 LOC). Only the *plumbing* is deduplicated. This preserves the project's "decision math is pure + unit-tested, plumbing is engine-specific" invariant (stated in every module header, e.g. `PoiOffensiveBotModule.cs:30-36`).

### 2.6 Reinforcement call-ins tied to missions (roadmap item 3 — DESIGN FOR, don't build)

Add two dormant fields to `Mission` now so item 3 is a fill-in later, not a re-architecture:

```
CompositionTemplate DesiredComposition;   // e.g. {2×AT, 3×rifle, 1×IFV} — null today
bool                RequestedReinforcement;
```

When a mission is `Staging`/`Retrying` and the free pool can't fill `DesiredSize`/`DesiredComposition`, it would call a (future) `UnitBuilderBotModule` hook to *call in* the missing units from the SR budget, tagged to the mission's objective key, so they commit to the mission the moment they walk in from the map edge (recall: units arrive at the map edge nearest the SR and march to a rally — `game-model.md:22-27`, `supply-route.md:10`). **Do not implement the hook now** — just reserve the fields and the objective-key tagging convention so the call-in can find its mission. This is the seam that makes "reinforcements arrive as combined-arms packages tied to missions" (roadmap 3) a small addition rather than another leap.

---

## 3. MIGRATION PATH

### Step 1 — CaptureMission only (internal refactor, kill-switch–gated) — SMALLEST VIABLE

**Goal:** replace `CaptureCoordinatorBotModule`'s ad-hoc capture+escort logic with a `Mission`/`MissionBoard` scoped to capture, gaining retry-on-TECN-loss, committed follow-escort, and stall detection — **without touching offense, garrison, the ledger, `PoiMap`, or `@stable` behavior.**

**Files touched:**
- **NEW** `engine/OpenRA.Mods.Common/Traits/BotModules/Mission.cs` — `Mission`, `MissionKind`, `MissionState`, `MissionBoard` (pure-ish; board holds the list + lifecycle transitions, takes the ledger as a dependency). ~200 LOC.
- `CaptureCoordinatorBotModule.cs` — behind a new `MissionModeEnabled` bool (default **false**): route capture through a `MissionBoard` (Capture mission + child Escort mission) instead of `IssueCaptureOrder`/`DispatchEscort`/`ReconcileGuardCommitments`. Old path stays intact for the `false` branch. Net ~+150 LOC (new path) with the old path retained.
- `mods/ww3mod/rules/ai/ai.yaml` — add `MissionModeEnabled: true` to `CaptureCoordinatorBotModule@experimental.tecn` only; `@stable.tecn` and the legacy `CaptureManagerBotModule` stay untouched (default false). **Watch the blank-line rule (§5).**
- **NEW** `engine/OpenRA.Test/OpenRA.Mods.Common/MissionBoardTest.cs` — unit-test the lifecycle with plain fakes (no Actor): Staging→Executing on force-fill, Executing→Retrying on wipe, Retry cap → Aborted, Complete on target-invalid. ~150 LOC. This is the `PoiOffenseTest`/`PoiGoalGuardTest` precedent — the board's transition logic must be pure enough to test without the engine (same discipline as `GoalGuardLedger<TKey>` being generic — `PoiGoalGuard.cs:39-117`).

**What breaks:** nothing, if the kill-switch is honored. `@experimental` capture behavior *changes on purpose* (that's the cycle); `@stable`, Normal/Rush/Turtle, and the legacy capture module are byte-identical because they never set `MissionModeEnabled: true`.

**Rough size:** ~350 new LOC + ~150 test LOC; ~1–1.5 days including a diagnostic N=5 verify batch.

### Step 2 — Unify offense + garrison onto the board

**Goal:** delete the §1.2 duplication. Introduce `MissionCoordinatorBotModule` (executor) + demote `PoiOffensive`/`PoiGarrison` to generators. Extract the shared `BuildFreePool`/`IsEligibleCombatUnit`/prune/commit/order/release skeleton into the board/executor, written once.

**Files touched:** `PoiOffensiveBotModule.cs` and `PoiGarrisonBotModule.cs` shrink to generators (~150 LOC each, down from 504/445); **NEW** `MissionCoordinatorBotModule.cs` (~250 LOC absorbing the shared skeleton); `ai.yaml` gains the coordinator wiring for `@experimental` **and a `@stable` twin** (or the offense/garrison twins fold into it). Pure math untouched.

**What breaks (risk):** `@stable` must stay byte-compatible. Because `@stable` runs `PoiOffensiveBotModule@stable`/`PoiGarrisonBotModule@stable` (`ai.yaml:643-664`), you cannot refactor those classes' behavior without either (a) keeping the old classes for `@stable` and adding new generator classes for `@experimental`, or (b) gating the new path with a `MissionModeEnabled`-style switch on the *same* classes (default false → `@stable` runs the old code path verbatim). Option (b) matches step 1 and is preferred. Either way this is where the frozen-control risk concentrates — hence "defer to when S2 forces it."

**Rough size:** ~2–3 days including keeping `@stable` provably identical and re-greening all four POI unit-test files.

### Step 3 (later, roadmap item 3) — call-in composition on missions

Fill in `DesiredComposition` + the `UnitBuilderBotModule` reinforcement hook (§2.6). Small once steps 1–2 exist. Not costed here beyond "the seam is reserved."

---

## 4. COST / BENEFIT

### 4.1 Honest effort

| Step | Effort | Deliverable |
|---|---|---|
| 1 — CaptureMission, gated | **1–1.5 days** | retry-on-loss + committed follow-escort + stall detection for capture; new `Mission`/`MissionBoard` + unit tests |
| 2 — offense/garrison unification | **2–3 days** | −~600 LOC duplication; one executor; `@stable` provably unchanged |
| 3 — call-in composition | ~0.5 day on top | combined-arms reinforcement tied to missions |

### 4.2 What it buys vs incremental patching

**Step 1 vs the cycle-1 patches.** The cycle-1 minimal mechanism (`260720_capture_reliability_cycle1.md:157-171`: TTL raise to 600 + `INotifyKilled` scan-reset + logging, ~1.5 hrs) closes F-2/F-3/F-5 and *narrows* F-1. It does **not** give retry-with-escalation, committed follow-escort (F-4), or stall detection — those are exactly the Mission model's Retrying state + Escort sub-mission + `LastProgressTick`. So:

- If the ~1.5 hr patches get capture to ≥6/10, **Mission step 1 is not needed for S1** — it's a nice-to-have that also happens to be the roadmap-1 beachhead.
- If the patches land at, say, 5/10 with logs showing escort desync / repeated single-TECN losses, **step 1 is the targeted fix**, and it doubles as the structural investment.

**Step 2** buys **zero ladder points** on its own — it's debt paydown. Its value is *leverage on future work*: once S2 needs a new mission type (e.g. "screen the contested-mid POI" or "siege the enemy SR circle"), having one executor means the new type is a generator + a sizing rule, not a fourth ~450-LOC copy of the skeleton. Pay for it when S2 makes you write that copy anyway.

**Step 3** buys the doctrine goal directly (combined-arms packages, force preservation) and is cheap once 1–2 land.

### 4.3 Recommendation + decision rule for the RETHINK

**Recommendation:** approve the Mission model as the roadmap-1 target; **build step 1 opportunistically at the next capture cycle**, gated by the decision rule below; **defer step 2 to the S2 milestone.**

Decision rule:

- **Do step 1 now IF** the cycle-1 capture patches have landed AND either (a) capture is still <6/10 with instrumented logs pointing at lost-TECN-no-retry (F-1) or escort desync (F-4) as the residual mode, OR (b) capture is ≥6/10 but we're about to touch escort/retry logic anyway for a doctrine cycle (force-preservation, fires-first) — in which case do it right the first time as a Mission.
- **Defer step 1 IF** the cheap patches already put capture comfortably ≥6/10 and the next cycle's highest-value target is elsewhere (dispersion verify, SR-contestation). Step 1 is then a scheduled roadmap item, not a fire.
- **Do step 2 only WHEN** a new ladder rung (S2) requires a mission type that doesn't exist yet — unify *as* you add it, never as a standalone refactor.
- **Never** build a `MissionBoard` as a single-instance-lookup player trait (§5, R-1). **Never** flip `@experimental` onto the new path without the kill-switch defaulting `@stable` to the old path.

---

## 5. RISKS

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R-1 | **Shared-`@poi` trap.** Adding `MissionBoard` as a player trait fetched via `TraitOrDefault<T>()` throws at runtime if twinned for `@stable` (two instances / one player). | High if done naively | Runtime crash for `@stable` matches | Own the board *inside* a module (step 1), or gate one shared instance on `enable-ai-experimental \|\| enable-ai-stable` like `PoiGoalGuard` (`ai.yaml:120-122`), or expose via `TraitsImplementing<T>()` (multi-safe, SquadManager precedent `ai.yaml:606-607`). Never a second single-instance-lookup trait un-gated. |
| R-2 | **Frozen `@stable` control drifts.** Refactoring `CaptureCoordinator`/`PoiOffensive`/`PoiGarrison` internals changes `@stable` behavior (it runs the same classes, `ai.yaml:616-664`), invalidating the A/B baseline. | High if ungated | Benchmark control corrupted → all ladder comparisons suspect | Kill-switch bool (`MissionModeEnabled`, default false) so `@stable` runs the *old* code path verbatim — the proposed `CohesionSwitchEnabled` pattern (`WORKSPACE/plans/260720_dispersion_cycle_design.md:128`, not yet in engine). `@experimental` sets true; `@stable` omits it. Verify `@stable` byte-identical before merge. |
| R-3 | **MiniYaml blank-line merge.** Editing `ai.yaml` to add `MissionModeEnabled`/coordinator wiring: a missing blank line between top-level module entries silently merges them (project hard rule; CLAUDE.md). | Medium | Wrong/half-applied config, silent | After any `ai.yaml` edit, confirm one blank line between every adjacent top-level `Module@name:` block. If a change "isn't taking effect," check blank lines first. |
| R-4 | **Ledger fork.** Someone implements missions with their own claim map instead of the ledger, so capture/offense/garrison stop seeing mission-held units → double-claim, unit tug-of-war. | Medium | Regression of the original "orders overwritten" bug class the ledger exists to kill | Design mandate (§2.4): missions commit/release *through* `PoiGoalGuard.Ledger`; `BuildFreePool` stays the one "not committed to anyone" query. No parallel ownership map. |
| R-5 | **Escort follow uses live capturer position → churn.** Making the escort track the capturer (fix F-4) could re-path every tick as the TECN moves. | Medium | Order spam, pathing cost | Reuse the existing repath gate (`RepathThresholdCells`, `CommitAndOrder` `PoiOffensiveBotModule.cs:380-383`) keyed off the capturer's cell delta; only re-issue past the threshold. |
| R-6 | **Scope creep into LayeredDefence/MountedTransport.** These two coordinate *outside* the ledger (actor-type exclusion + `IsPassengerReserved` — `MountedTransportBotModule.cs:99-105`; confirmed: neither references the ledger). Folding them into missions is a much larger job. | Medium | Blown estimate, destabilized working modules | Explicitly out of scope for steps 1–2. Missions cover ledger-coordinated combat units only. Absorb the transport/defence layer later, separately, if ever. |
| R-7 | **Unseeded `LocalRandom` masks regressions.** Bot decisions aren't seed-reproducible (`architecture.md:291-293`), so a single before/after match can't prove the refactor is behavior-neutral. | High (existing condition) | False confidence in "no behavior change" | Rely on aggregate N-batch benchmarking (statistically valid), not single-match diff. For `@stable` neutrality, prefer *code-path proof* (kill-switch default false → old path literally runs) over empirical match comparison. |

---

## 6. Non-goals

- **Not** absorbing `LayeredDefenceBotModule` or `MountedTransportBotModule` (R-6).
- **Not** changing `PoiMap` scoring, the pure math classes, or the ledger API — the Mission layer sits *above* them.
- **Not** implementing the call-in reinforcement hook (§2.6) — only reserving its seam.
- **Not** touching Normal/Rush/Turtle or the legacy `CaptureManagerBotModule` (`ai.yaml:101-106`, `:356-361`).
- **Not** a class hierarchy of mission types (§2.1) — one typed class.

---

## 7. New incidental insight → DISCOVERIES

Captured to `WORKSPACE/DISCOVERIES.md` (dated, with refs): capture escorts are dispatched but **never committed to the goal-guard ledger** (`CaptureCoordinatorBotModule.cs:486-502`), so `PoiOffensiveBotModule.BuildFreePool` (`:320-330`) can re-steal them ~100 ticks later — an escort desync distinct from, and compounding, the F-4 wrong-cell bug.
