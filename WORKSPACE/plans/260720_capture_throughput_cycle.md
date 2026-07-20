# Capture-Throughput Cycle — Design Study (READ-ONLY recon, no code touched)

**Date:** 2026-07-20 · **Researched against:** `main` @ **`e6d5627e`** (`git status -sb`: ahead 106, clean
except untracked `.maestro/`). Expected `e6d5627e`-or-later per the recon brief — confirmed.
· **Author:** capture-throughput recon worker · **Type:** design study, two options + recommendation.
· **Predecessor:** [`260720_tecn_floor_cycle2_n10.md`](../ai-bench/runs/260720_tecn_floor_cycle2_n10.md)
(cycle 2, merged `c6a71c14`, capture 4/10→8/10) · **Watch cells:**
[`260720_seeded_baseline_n10.md`](../ai-bench/runs/260720_seeded_baseline_n10.md) seeds **2017 & 8017**
(both america/primary, `exp gross = 0`).

**One-behavior-per-cycle law:** A and B are **separate cycles**. This study picks the next one and
stages the other.

---

## 0. TL;DR

- **The residual 2/10 ($0 on 2017/8017) is a *conversion/throughput* failure, not an availability
  failure.** By the time those runs sit at $0 there is already ≥1 TECN alive — the floor is
  *satisfied*, so neither "move the check off the M-2 gate" nor "raise the floor" is guaranteed to
  flip them.
- **m7-class request-death point found:** the request dies at
  `UnitBuilderBotModule.BuildUnit(name)` **`:155`** — it only starts production on a queue with
  `!q.AllQueued().Any()` (a *free* queue). With one busy Infantry queue, the popped request finds no
  free queue and is silently dropped (`:90-91`). 82 re-requests = 82 popped-and-dropped cycles. **The
  minimal floor cannot cure this** — re-requesting faster can't help if the queue is never free at
  pop time.
- **Recommendation: ship Option A first** (every-scan placement + `TecnFloor: 2` on `@experimental`
  only) as a cheap redundancy lever, **then Option B** (escort reservation) as the next cycle. A is
  ~8 LOC / LOW risk and — critically — **byte-identical for `@stable` at its promoted `TecnFloor: 1`**
  (proof in §2.3). Honest watch-cell expectation: A gives 2017/8017 a *second independent attempt*
  (partial-flip likely, not certain) and reliably raises america conditional gross; if a watch cell
  stays $0, the marker cadence will say "queue-starved" → that is the signal to prioritise B / a queue
  lever.
- **Structural flag:** Option B touches escort/follow logic, which **re-triggers the parked
  mission-abstraction step-1 decision rule** ([`260720_mission_abstraction_costing.md:230`](260720_mission_abstraction_costing.md)).
  Flagged in §4, not designed.

---

## 1. Shared plumbing (both options build on this)

The request seam is settled and correct — cycle 2 proved it and
[`architecture.md:291-305`](../../DOCS/reference/architecture.md) documents it:

- `CaptureCoordinatorBotModule.MaintainTecnFloor` (`CaptureCoordinatorBotModule.cs:380-405`) requests
  one capturer via `IBotRequestUnitProduction` when `alive + pending < TecnFloor` (`:393-395`) and a
  target exists (`:399-400`).
- The request is popped **first** each 30-tick build cycle (`UnitBuilderBotModule.cs:85-91`,
  `FeedbackTime = 30` `:49`) and routed through the single-name `BuildUnit` overload (`:142-165`),
  which **bypasses `UnitsToBuild`/`UnitLimits`/`UnitDelays`** (contrast the share-lottery overload's
  `:125-126`, `:133-136`). Drop-on-failure is real (`:90-91`), so the floor re-requests each scan and
  subtracts `pending` via `RequestedProductionCount` (`:104-107`).
- Escort/defender recruitment and offense/garrison contention are mediated by the **one shared ledger**
  `PoiGoalGuard.Ledger` (`PoiGoalGuard.cs:39-117`): `Commit`/`IsCommitted`/`Release`/`Prune`
  (`:60-116`). `PoiOffensiveBotModule.BuildFreePool` (`:351-361`) only pulls units **not** committed to
  anyone (`:359`). This is the reservation substrate Option B extends.

---

## 2. OPTION A — Minimal mechanism: floor past the M-2 gate

### 2.1 Why the m2-class stall happens (traced)

The floor is called **only** inside the M-2 branch (`CaptureCoordinatorBotModule.cs:271-272`), which is
reached only when `idleCapturers.Length == 0` (`:254`). `idleCapturers` excludes any TECN that is
committed in the ledger (`:245-252`, guard check `:248`). A dispatched TECN is committed for
`DefaultCommitmentTicks = 600` (`IssueCaptureOrder :516`; ai.yaml shared PoiGoalGuard `:126`), released
early only when its target is captured/gone or the commitment expires
(`ReconcileGuardCommitments :549-589`).

So for an america run holding one TECN:

- **TECN committed (walking / stalled mid-order):** `idleCapturers = 0` → M-2 reached → floor checks
  `alive + pending`. `alive = capturingActors.Actors.Count = 1` → `1 ≥ 1` → **no request**. The single
  TECN cycles 600-tick-committed → brief idle → re-ordered, holding the floor at 1 forever.
- **A retained idle lottery-TECN exists** (`ChooseRandomUnitToBuild` can build a `tecn` while
  `idleBaseUnits < 12`): `idleCapturers ≥ 1` → **M-2 never reached** → floor never even consulted. But
  in this state the main scan (`QueueCaptureOrdersFromPoiMap :460-508` or the legacy scan) is *already*
  trying to dispatch that idle TECN each scan.

**Conclusion — the honest one:** in *both* sub-cases the pool already holds ≥1 TECN, so the loss on a
$0 run is **not** the floor going silent — it is that the available/dispatched TECN never lands a
capture in-window (`issues = 0` in the cycle-2 m2 row). That is a **conversion stall**, not an
availability gap. **Moving the check to every scan at `TecnFloor: 1` is therefore a no-op for the watch
cells** (`alive = 1 ≥ 1` regardless of placement).

### 2.2 What actually flips a conversion-stalled cell: redundancy (floor = 2)

The only availability lever that bites a single-TECN stall is a **second, independent** unit —
`TecnFloor: 2`. With floor 2 and one committed-but-stalled TECN: `alive + pending = 1 < 2` → request a
second TECN whose different spawn timing / path may convert where the first stalls. Note floor 2 fires
**even M-2-gated** during the committed windows (the stalled TECN makes `idleCapturers = 0`), so the
value bump does most of the work; the placement move is what makes floor 2 robust to the
*retained-idle-lottery-TECN* masking case (where `idleCapturers ≥ 1` hides M-2 yet `alive = 1 < 2`
should still pull a second).

**So the minimal mechanism is the pair, and they are one coherent behaviour** ("keep 2 capturers topped
up, every scan"):

1. **Code:** move `if (Info.TecnFloor > 0) MaintainTecnFloor(bot);` out of the M-2 branch
   (`:271-272`) to run once per `QueueCaptureOrders` scan (`:215`) regardless of `idleCapturers`. Keep
   the M-2 `no-idle-capturers` log (`:263-264`) where it is.
2. **YAML:** `TecnFloor: 2` on `@experimental.tecn` (`ai.yaml:165`) **only**.

### 2.3 `@stable` safety proof (why the code move is free)

`@stable.tecn` keeps its **promoted** `TecnFloor: 1` (`ai.yaml:660`) — the recon brief is explicit: do
**not** touch it. At floor 1 the every-scan placement is **behaviourally identical** to the M-2-gated
placement:

> every-scan fires only when `alive + pending < 1`, i.e. `alive = 0 ∧ pending = 0`. `alive = 0` ⇒ no
> capturers exist ⇒ `idleCapturers = 0` ⇒ M-2 is already reached. The two placements fire on exactly
> the same states at floor 1. ∎

So the shared-engine-class blast radius (`architecture.md:307-309`) is **neutralised without a new
field**: the only observable difference lives at `TecnFloor ≥ 2`, which only `@experimental` sets. No
new default-off bool is needed for Option A. (Controls run `CaptureManagerBotModule`, no field —
untouched.)

### 2.4 The m7-class conversion failure — where the request dies

m7 (russia mirror) logged `floor-req = 82`, `tecn-killed = 0`, `capture-issues = 0`. Trace:

- Each `MaintainTecnFloor` fire adds to `queuedBuildRequests` (`UnitBuilderBotModule.cs:99-102`).
- `BotTick` pops **one** per 30 ticks (`:87-91`) and calls `BuildUnit(bot, name)` (`:142-165`).
- `BuildUnit(name)` finds a queue in `buildableInfo.Queue` with `!q.AllQueued().Any()` — a **free**
  queue with nothing already queued (`:155`). **If the single Infantry queue is busy that cycle,
  `queue == null` and no `StartProduction` order is issued** (`:160-164` skipped). The pop already
  removed the request (`:90-91`) → silently dropped.
- `RequestedProductionCount` (`:104-107`) then reads `pending = 0` again, so the next M-2 scan
  re-requests. **82 re-requests = ~82 popped-and-dropped cycles against a persistently busy Infantry
  queue.** With `tecn-killed = 0`, the TECN was almost certainly **never produced** that match — a
  production-starvation tail, not a survival problem.

**Does the minimal fix (A) address m7? No.** Re-requesting more often / topping the floor to 2 cannot
help when the request dies at a busy-queue check every pop. m7 needs a **separate lever**: reserve/idle
the Infantry queue for the request, a dedicated capturer production path, or reduce competing infantry
share so the queue is free more often. That lever is out of scope for a one-behaviour cycle and is *not*
Option B either (B screens a *produced* TECN; it does not produce one). **Record as a distinct future
cycle: "capturer production-queue reservation."**

### 2.5 Cost & risk (Option A)

| | |
|---|---|
| **LOC** | ~8 net: move the 2-line floor call out of the M-2 branch to the scan top/bottom; +1 YAML value. |
| **Risk** | **LOW.** `@stable` provably unchanged at floor 1 (§2.3). Over-production bounded: `alive+pending<2` gate + `pending` subtraction cap at 2 (< `UnitLimits` 3). Cost 250/unit — cheap. |
| **One-behaviour scope** | Single behaviour: "experimental maintains 2 capturers, checked every scan." Do **not** also touch targeting, EscortSize, TTL, or unit stats. |
| **MiniYaml** | `TecnFloor: 2` is a child line inside the existing `@experimental.tecn` block — **no blank line inside the block**; keep blank separators between top-level trait entries (CLAUDE.md hard rule). |

### 2.6 Watch-cell expectation (Option A)

Seeds 2017 & 8017 (america, single-TECN conversion stalls): floor 2 gives each a **second independent
capturer attempt**. Realistic prediction: **partial flip** (≥1 of the 2 flips to captured; both possible
if the stall was pathing/contention-luck) **plus** a lift in america conditional gross on the runs that
today stop at one derrick. If a watch cell **stays $0**, the `tecn-floor-request` cadence + whether a
2nd TECN ever produces (`issue` marker) diagnoses it as **queue-starvation** (§2.4) → escalate to the
production-queue-reservation cycle, and to B for the screen. This falsifiable prediction is the point:
A is cheap enough to run as the diagnostic that partitions "availability" from "queue-starvation."

---

## 3. OPTION B — Escort-bundled reinforcement packaging (roadmap item 3)

### 3.1 The problem it fixes (a filed bug + a conversion lever)

Today `DispatchEscort` (`CaptureCoordinatorBotModule.cs:627-643`) issues an `AttackMove` to escorts and
adds them to a **per-tick** `escortsRecruitedThisTick` set (`:638-639`) but **never commits them to the
ledger** — only the TECN is committed (`IssueCaptureOrder :516`). Filed as the **escort-desync bug**
([`DISCOVERIES.md` 2026-07-20](../DISCOVERIES.md), refs there cite the pre-refactor
`:486-502`/`:395-396`; **current** lines are `:627-643`/`:514-516`). Consequence: ~100 ticks later
`PoiOffensiveBotModule.BuildFreePool` (`:351-361`, uncommitted check `:359`) sees the escorts as free and
pulls them onto an attack axis — the escort abandons the capturer mid-approach. Compounds the F-4 bug
(escorts `AttackMove` the derrick **cell**, not the capturer, so they arrive and sit;
[`260720_capture_reliability_cycle1.md`](260720_capture_reliability_cycle1.md)).

A **capture package** = `{1 TECN + EscortSize escorts}` requested/reserved **together** and kept bound
until the capture resolves. It attacks *conversion* directly (a screened, reserved escort keeps the TECN
alive and moving through contested approaches — the m7-type "produced but never landed" and any
contested-derrick loss) and **supersedes the escort-desync bug** as a side effect.

### 3.2 Where the bundle lives — the ledger *is* the package (no new shared trait)

Three candidate homes, evaluated:

1. **Extend `IBotRequestUnitProduction` usage** — covers only the *TECN production* half. Escorts are
   pulled from the existing free pool, not produced. **Insufficient alone.**
2. **A `CapturePackage` struct on the coordinator** (`{Actor Tecn; List<Actor> Escorts; uint TargetId}`
   in a dict) — coordinator-local, avoids the shared-singleton trap
   ([`mission_abstraction_costing.md:241` R-1](260720_mission_abstraction_costing.md)). Viable but
   arguably redundant with (3).
3. **The goal-guard ledger as the reservation home** — **recommended MVV.** The TECN is already
   committed under `capture:<targetId>` (`CaptureObjectiveKey :533`). Commit each escort under
   `escort:<targetId>` at dispatch. **The package is then just the set of ledger entries sharing a
   target id** — no new abstraction, and `BuildFreePool`'s "not committed to anyone" query (`:359`)
   automatically stops offense/garrison poaching the escort.

### 3.3 Binding & lifecycle (MVV, reservation-only)

- **Bind:** in `DispatchEscort` (`:627-643`), after the `AttackMove`, add
  `goalGuard.Ledger.Commit(escort, "escort:"+target.ActorID, world.WorldTick, ttl)` per recruit
  (mirrors `PoiOffensiveBotModule.CommitAndOrder :402-406`).
- **Release:** extend `ReconcileGuardCommitments` (`:549-589`) to `Release` any `escort:<id>` whose
  target is captured/gone (the same `stillCapturable` test that releases the TECN at `:581-587`), and
  handle **consumed-by-capture TECN death**: when the TECN vanishes on capture, its escorts must be
  released so the offense pool reclaims them (else they idle-loiter committed for `ttl`). `Prune`
  (`:566`) already drops dead-unit keys; the target-captured path covers the success case.
- **Follow (second increment, NOT in the reservation-only MVV):** rebind the escort `AttackMove` from
  the derrick cell to the **capturer**, repath-gated by the capturer's cell delta (reuse
  `RepathThresholdCells` per [`mission_abstraction_costing.md:245` R-5](260720_mission_abstraction_costing.md))
  to avoid per-tick order spam.

### 3.4 `@stable` gating (the catch)

`EscortSize: 2` is set on **both** `@experimental.tecn` (`ai.yaml:155`) **and** `@stable.tecn`
(`ai.yaml:651`). Committing escorts to the ledger changes escort behaviour on the **shared class**, so it
would leak into the `@stable` control. Per the shared-trait-defaults rule
([`architecture.md:307-309`](../../DOCS/reference/architecture.md)), B **requires a new default-off bool**
(e.g. `ReserveEscorts`, default `false`) gated on `@experimental.tecn` only — unlike A, which needs no
new field.

### 3.5 Interaction with MountedTransport

`MountedTransportBotModule` owns IFV carriers (`bradley`/`bmp2`/`m113`) and coordinates **outside** the
ledger via actor-type exclusion + `IsPassengerReserved` (`MountedTransportBotModule.cs:99-105`, confirmed
by [`mission_abstraction_costing.md:246` R-6](260720_mission_abstraction_costing.md)). PoiOffensive
already excludes carriers by type (`ai.yaml:187`). **MVV keeps escorts as walking, ledger-committed
combat units and leaves transport untouched** — a package must not double-claim a passenger the transport
reserved. Folding "escort rides an IFV to the derrick" into packages is a much larger, separate job
(explicit non-goal here).

### 3.6 Metrics that would show B works

- **S1:** watch-cell flip (2017/8017) + **conditional gross** (screened TECN converts a *second*
  derrick), plus a new **escort-retained** marker: escorts still committed to `escort:<id>` at capture
  time vs poached-by-offense (the desync-fix signal).
- **S2 (combat rung):** does the screen **lower `tecn-killed`** and raise in-window capture under
  contested approaches; and does committing escorts **starve offense axes** (measure free-pool size
  delta / axis count — the cost side).

### 3.7 Cost & risk (Option B)

| | |
|---|---|
| **LOC** | Reservation-only MVV: `Commit` in `DispatchEscort` + `Release` lifecycle in `ReconcileGuardCommitments` + the `ReserveEscorts` gate ≈ **30–50 LOC**. Follow-rebinding (2nd increment) +~20. |
| **Risk** | **MEDIUM.** Touches the shared ledger + escort behaviour; must gate `@stable` (§3.4) and verify byte-identical. Escort loitering if release lifecycle misses the consumed-by-capture path. |
| **Implement-ready?** | Reservation-only MVV is **implement-ready from this study** (commit + release + gate = one clean behaviour that also closes the filed desync bug). The **follow-rebinding** half warrants a **short focused recon** (escort-follows-capturer churn + consumed-by-capture re-home). Recommend shipping reservation-only as the B cycle; follow-binding as a later increment. |

---

## 4. RECOMMENDATION

**Next capture-throughput cycle = Option A. Then Option B. Separate cycles (one-behaviour law).**

Rationale:

- **A is the cheapest lever and doubles as a diagnostic.** ~8 LOC, LOW risk, no new field, `@stable`
  provably frozen (§2.3). It adds the only availability redundancy that *can* touch a single-TECN
  conversion stall (a second independent attempt) and its markers cleanly partition the residual into
  "flipped by redundancy" vs "queue-starved" (§2.6). Cheap enough to run as the experiment that tells us
  which lever the watch cells actually need.
- **B is the higher-ceiling structural lever** and reuses the *ledger* plumbing (as A reuses the
  *request* plumbing), so staging loses nothing. B directly attacks conversion + closes a filed bug, but
  is MEDIUM risk and needs a `@stable` gate.
- **A→B staging makes sense:** independent behaviours, independent plumbing, and A's diagnostic output
  sharpens B's target (if A leaves 2017/8017 at $0 via queue-starvation, that also tells us B alone won't
  flip them without the production-queue-reservation cycle — a *third* item, §2.4).

**Expected watch-cell outcome (recommended = A):** partial flip of {2017, 8017} — I predict **at least
one** flips to captured on the second-attempt redundancy, with a reliable rise in america conditional
gross; a cell that stays $0 is the queue-starvation signal, not a floor failure. Stated as a falsifiable
paired-comparison prediction against the seeded reference table.

**Honest caveat carried forward:** none of A/B produces a TECN when the Infantry queue is saturated at
pop time (§2.4, `BuildUnit :155`). If the seeded re-run shows the second TECN never *produces*, the
binding constraint is the **capturer production-queue reservation** cycle, which must precede or accompany
B for russia-mirror m7-type runs.

---

## 5. Structural note — mission-abstraction re-trigger (flag, not design)

The parked mission abstraction ([`260720_mission_abstraction_costing.md`](260720_mission_abstraction_costing.md),
deferred to the ~5-cycle RETHINK) has an explicit re-trigger in its decision rule (`:230`): *"Do step 1
now IF capture ≥6/10 but we're about to touch escort/retry logic anyway for a doctrine cycle — do it right
the first time as a Mission."*

**Option B is exactly that trigger.** B's escort reservation + follow-binding is the Mission model's
committed-follow-escort sub-mission (`:209`, `:215`). **Flag:** if B is greenlit, first decide whether to
implement it as the **CaptureMission step-1 beachhead** (the ledger-committed `escort:<id>` sub-force is
the mission's escort component) rather than a bolt-on patch — the ~1 day step-1 estimate (`:209`) buys
retry-on-loss + stall detection on top. Constraints from that plan still bind: never a single-instance
`MissionBoard` player trait (`:233` R-1); kill-switch default-off so `@stable` runs the old path (`:242`
R-2). **Not designed here — surfaced for the RETHINK owner.** Option A does **not** touch this seam.

---

## 6. Provenance

Every claim above is cited file:line against `main @ e6d5627e`. Primary sources read:
`CaptureCoordinatorBotModule.cs`, `UnitBuilderBotModule.cs`, `PoiGoalGuard.cs`,
`PoiOffensiveBotModule.cs`, `MountedTransportBotModule.cs`, `mods/ww3mod/rules/ai/ai.yaml`,
`DOCS/reference/architecture.md`, `WORKSPACE/DISCOVERIES.md`, and the two run reports + the
mission-abstraction costing. No code, YAML, build, or test was run (benchmark batch in progress).
