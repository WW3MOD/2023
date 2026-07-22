# Phase 2 red-team: stance-conditioned positioning executor (pre-implementation)

**Date:** 260722 · **Scope:** design/contract audit of SPEC §2/§4/§5/§7 Phase 2, before any code.
**Researched against:** main @ 060cac2b (Phase 0 + Phase 1 merged). Read-only pass — no builds, no tests.
**Inputs:** `260722_strategic_tactical_split_SPEC.md`, `260722_bot_brain_architecture.md`,
`260722_stance_tactical_survey.md`, merged Phase-1 code (SightingThreatLayer, TerrainAffordanceLayer),
engine substrate (AutoTarget, CohesionMoveModifier/SlotMemory, SquadManager/GroundStates/StateBase,
PoiGoalGuard, Actor/World internals, ai.yaml, infantry.yaml).

§4 stance model used throughout (the AMENDED wording): **both Hunt and Defensive occupy the
THREAT-FACING edge of cover.** Hunt = at/just beyond the treeline, may creep forward between covers.
Defensive = just inside the same edge, concealed, static. HoldPosition = no autonomous repositioning.
Any "Defensive hides on the back side of cover" reasoning below would be the old inverted wording and
was rejected on sight; none of the findings depend on it.

---

## Findings table (ranked)

| ID | Rank | Finding | One-line fix direction |
|----|------|---------|------------------------|
| B1 | **BLOCKING** | SPEC §2's commitment ledger (PoiGoalGuard.Ledger) does **not** gate the 75-tick squad-FSM grouped re-fires or LayeredDefence — the primary collision source is unprotected | Executor claims units in the ledger **and** the re-firing order paths get a claim check (or exclude claimed units at group time) |
| B2 | **BLOCKING** | CohesionSlotMemory's idle return-to-slot loop will tug-of-war with executor repositioning (slot refreshed by every grouped re-fire, remembered 750 ticks) | Executor must own the slot: re-`Assign` to its chosen cell (or a new `Clear()`), never leave a stale slot live |
| B3 | **BLOCKING** | ThreatDirection degeneracies: opposite-axis cancellation returns the same `WAngle.Zero` as "no data"; frozen re-injection keeps stale bearings alive indefinitely; per-cell locality reads zero a few cells away | Executor-side gating: intensity threshold + direction-magnitude/intensity ambiguity ratio + defined fallback per degenerate case; small aggregate scan over `ActiveCells` |
| S1 | SHOULD-FIX | Squad FSM rewrites `EngagementStance` mid-flight (re-engage forces Hunt) — L2 mutates the very axis L3 conditions on, unversioned | Spec must assign stance ownership; on experimental, gate FSM stance writes or declare them legitimate L2 intent |
| S2 | SHOULD-FIX | "Just beyond" (Hunt) vs "just inside" (Defensive) is not expressible in the Phase-1 layer API — both collapse to the same edge-cell set; thin strips/borders give unstable normals | Define the derivation: step ±1..2 cells along `OutwardFacing` from an edge cell, validate passability, deterministic tie-breaks |
| S3 | SHOULD-FIX | TerrainAffordanceLayer is computed once at map load: destroyed/changed density sources leave stale cover data; cover cells are not passability-checked | Accept staleness for Phase 2 (document it); executor must validate passability/occupancy at use time, never trust the layer alone |
| S4 | SHOULD-FIX | Suppression interaction: an executor move order breaks prone (`!moving` clause) and crawls at up to −90% speed — repositioning under fire is exactly when the executor wants to act | Suppression gate: don't issue repositioning to heavily-suppressed infantry; spec the threshold |
| S5 | SHOULD-FIX | §2 pt 2 in-transit detour has no activity-level semantics (start/end conditions, queue representation, cancellation identity for §2 pt 4) | Phase 2 v1: **no mid-transit detours** — reposition only from idle/arrival; detours deferred to a later phase as a child-activity wrapper |
| S6 | SHOULD-FIX | Same-tick `INotifyIdle` contention: all TickIdle handlers run in trait order even after an earlier one queued an activity — executor + SlotMemory + AutoTarget can fight within one tick | Executor's TickIdle must be a no-op when it (or SlotMemory) has just queued; resolve jointly with B2 |
| S7 | SHOULD-FIX | Leash is unpinned: SPEC says "within a leash" but not the anchor or magnitude; wrong anchor (current position) lets repeated adjustments random-walk units away | Anchor = last commanded destination / assigned cohesion slot; radius a YAML int in cells (default ≈ 4), ≤ Phase-0 footprint half-extent |
| S8 | SHOULD-FIX | §5 determinism needs one sharpening for Phase 2 specifically: the executor is a **sim-side unit trait**, where `LocalRandom` desyncs silently (bot modules get away with it; the executor cannot) | Amendment text: executor may use only `SharedRandom` (stagger) and ActorID/cell-order tie-breaks; no randomness in target-cell choice |
| N1 | NOTE | Human UX gaps: per-type stance defaults not reapplied on capture; HoldFire units never idle-scan but *should* still auto-position; Phase-3 default-ON needs the §2 pt 4 abort to be instant and visible | Decisions to record in spec, not code blockers |
| N2 | NOTE | Governance shape is validated: ConditionalTrait + `GrantConditionOnBotOwner` (experimental) + default-off YAML flag keeps @stable byte-identical, per the UnloadOnArrival precedent | Follow the precedent verbatim |
| N3 | NOTE | Host-trait shape: naval has no AutoTarget; the executor must be its own trait reading stance via `TraitOrDefault`, not an AutoTarget extension | Constrain Phase 2 to AutoTarget-bearing ground units |
| N4 | NOTE | Operations-layer compatibility: the executor contract composes cleanly with the planned Operation callers **if** its ledger keys and arrival/failure signals follow the event-bus grammar | Reserve key format + expose completion state now, cheap |

---

## BLOCKING findings

### B1 — The commitment ledger does not gate the code paths that actually collide

**Claim under test:** SPEC §2 pt 3 — the executor "registers in a commitment ledger" so higher
layers don't re-task units mid-adjustment. The named ledger is `PoiGoalGuard.Ledger`
(`GoalGuardLedger<Actor>`: Commit/IsCommitted/TryGetObjective/Release/Prune,
engine/OpenRA.Mods.Common/Traits/BotModules/PoiGoalGuard.cs).

**Evidence:**
- The primary re-tasking source is the squad FSM: grouped AttackMove re-issued `queued: false` every
  75 ticks — GroundStates.cs:67, :161, :174; cadence `AttackForceInterval = 75` and the
  `--attackForceTicks` loop at SquadManagerBotModule.cs:64, :254-256. Every re-fire replaces the unit's
  activity queue, aborting any in-progress executor adjustment.
- A decisive grep for `IsCommitted|PoiGoalGuard|\.Ledger` across `Traits/BotModules` hits **only**
  PoiGoalGuard.cs, CaptureCoordinatorBotModule.cs (:244, :263, :276, :538, :600-626, :762, :803),
  PoiGarrisonBotModule.cs (:160-377), PoiOffensiveBotModule.cs (:192-557). **Zero hits** in
  SquadManagerBotModule, GroundStates, StateBase, LayeredDefenceBotModule. The ledger gates
  *recruitment into the Poi stack* (e.g. PoiOffensiveBotModule.cs:406 skips committed units); it does
  not gate a single order-issuing re-fire path in the squad FSM.
- Mitigation exists but is profile-scoped: the experimental/stable managers are air-only
  (`IgnoreGroundUnits: true`, ai.yaml:556, :616, :790, :801), so on @experimental the ground pool is
  PoiOffensive/Garrison/Capture — which *do* respect the ledger. But legacy profiles (@normal etc.)
  still run ground squads through GroundStates, and LayeredDefence recruits with no ledger check on
  any profile.
- Known precedent that "registered somewhere" ≠ "protected": the escort-never-committed bug
  (CaptureCoordinatorBotModule.cs:486-502 recruits escorts without committing them, so other modules
  steal them).

**Why blocking:** as written, an implementer can satisfy the SPEC's letter (commit to the ledger) and
ship an executor whose adjustments are cancelled every ≤75 ticks by re-fires that never look at the
ledger. That converts the feature into pure order churn — the exact oscillation class the
architecture doc diagnosed (timer re-decides without memory).

**Fix / amendment:**
1. Executor (bot-owned units only) commits a claim in `PoiGoalGuard.Ledger` with a distinct key
   grammar (see brief), TTL ≈ one adjustment (e.g. 150 ticks), released on arrival/abort. Human-owned
   units have no ledger and need none — no bot layer re-tasks them.
2. **And** add the missing half: the grouped-order issuance path must exclude claimed units. Cheapest
   seam: where the squad FSM builds its grouped unit list (GroundStates re-fire sites), filter
   `!guard.Ledger.IsCommitted(unit)` when a PoiGoalGuard is present. On @experimental this is nearly
   free (air-only squads); doing it anyway future-proofs legacy profiles and LayeredDefence.
3. Alternative if touching the FSM is out of scope for Phase 2: scope the executor to @experimental
   bots only (where the ground pool already respects the ledger) and record the FSM gap as a rider on
   the Phase-4/5 operations work, which already plans unit-level claims. What is *not* acceptable is
   shipping the executor on profiles whose re-fire paths ignore the ledger.

### B2 — CohesionSlotMemory will drag executor-positioned units back to stale slots

**Evidence:**
- Every grouped Move/AttackMove assigns a formation slot: `subject.TraitOrDefault<CohesionSlotMemory>()
  ?.Assign(slots[idx], WorldTick)` — CohesionMoveModifier.cs:745. The 75-tick squad re-fire therefore
  *refreshes* the slot continuously.
- CohesionSlotMemory.cs:76-105: `INotifyIdle.TickIdle → TryReturnToSlot` queues `new Move(self,
  assignedSlot)` whenever the unit is idle off-slot within `ForgetAfterTicks = 750`
  (`ReturnCooldownTicks = 25` between attempts).

**Failure loop:** executor moves unit from slot to treeline cell → unit arrives, goes idle → SlotMemory
sees idle + off-slot + slot age < 750 → queues Move back to slot → unit arrives at slot, idle →
executor re-evaluates, moves it to the treeline again → repeat every ~25-50 ticks, forever. This is
not an edge case; it is the default interaction for any bot ground unit that ever received a grouped
order (i.e. nearly all of them) and for human units after any multi-select move.

**Fix / amendment:** the executor must take ownership of the slot when it repositions:
- Preferred: when the executor picks a destination cell, call `slotMemory.Assign(chosenCell,
  WorldTick)` — the return-to-slot behavior then *reinforces* the executor's choice (drift back to the
  cover cell after being bumped) instead of fighting it. This needs no new API.
- Also add a `Clear()`/release for the abort path (§2 pt 4 fresh explicit order): on abort the slot
  should revert to whatever the next grouped order assigns, not the executor's cell. (A fresh grouped
  order overwrites it anyway via :745, so `Clear()` is belt-and-suspenders — but a fresh *ungrouped*
  order does not, so it's worth having.)
- Trait-order note: both traits act in `INotifyIdle.TickIdle`; see S6 for the same-tick contention rule.

### B3 — ThreatDirection has three degeneracies the SPEC treats as always-valid

SPEC §4 conditions edge choice on "the threat direction". The Phase-1 field is honest but lossy, and
the SPEC never says what the executor does when the signal is degenerate.

**Evidence (SightingThreatLayer.cs):**
1. **Cancellation ≡ no-data.** Direction accumulates as a summed vector (`DirX += -dx * contribution`,
   :267-268). Threats on opposite sides cancel; `ThreatDirection` returns `WAngle.Zero` when
   `DirX == 0 && DirY == 0` (:304-306) — the *same* sentinel as "never saw anything". A surrounded
   unit and a blind unit read identically. Near-cancellation is worse: a tiny residual vector yields a
   confidently wrong bearing with high `ThreatIntensity`.
2. **Staleness never decays while fog-frozen.** Frozen snapshots re-inject `FrozenWeight = 60` on
   *every* 25-tick recompute (`FrozenActorsInRegion(AllCells, onlyVisible: true)`, :225-237). With
   `DecayPercent = 75`, the geometric series converges to ~4× a single injection — a long-gone enemy's
   ghost holds a steady-state intensity of ~240/cell (vs ~400 for a live contact) and a bearing locked
   on the ghost **until the player re-scouts the cell**. "Recent contacts dominate, stale ones fade"
   (header comment, :19) is only true for briefly-seen live contacts, not frozen ones. Positioning a
   defensive line to face a ghost for the rest of the game is a plausible outcome.
3. **Locality.** Contributions spread over a Manhattan radius of 4 (:242-249). A unit 6 cells from the
   nearest sighting footprint reads intensity 0 / direction Zero at its own cell. Per-cell lookup is
   the wrong query for "which way is the enemy, roughly" — the API anticipates this by exposing
   `ActiveCells(player)` (:334-338), but the SPEC never says to use it.

**Fix / amendment (executor-side; no Phase-1 code changes required for Phase 2):**
- Compute the facing from a small aggregate: sum `(cell − unitCell) * ThreatIntensity` over
  `ActiveCells(player)` within a scan radius (or over the leash neighborhood) rather than reading one
  cell. Integer math, deterministic iteration (ActiveCells order is insertion-order but the *sum* is
  order-independent — keep it additive).
- Gate on quality: require `ThreatIntensity ≥ threshold` AND `|DirVector|² ≥ k · intensity²`
  (ambiguity ratio) before trusting a bearing. Below threshold ⇒ **fallback, in order:** (a) last
  accepted bearing within a TTL, (b) bearing toward the unit's commanded destination / operation
  objective, (c) no repositioning (stay put). Never treat `WAngle.Zero` as a valid bearing without the
  intensity gate.
- Record the staleness limitation in the SPEC as accepted for Phase 2 (it is fog-honest by design —
  the player "believes" the ghost too) and note the Phase-4 scout link as the intended mitigation.
- Optional cheap Phase-1 patch *if* the manager wants it (SPEC amendment, not executor code): expose
  `bool HasDirection(player, cell)` or return the raw `(DirX, DirY, intensity)` tuple so callers can
  distinguish cancellation from absence. Not required if the aggregate scan is adopted.

---

## SHOULD-FIX findings

### S1 — L2 rewrites the stance axis the executor conditions on

GroundStates.cs:346 calls `SetSquadEngagementStance(owner, EngagementStance.Hunt)` on re-engage;
StateBase.cs:141-145 implements it as per-unit "SetEngagementStance" orders. So the squad FSM already
*uses stance as an actuator*: a bot unit the profile configured Defensive gets flipped to Hunt
mid-fight, silently changing which §4 behavior the executor runs — and it is never set back.
On @experimental (air-only squads) this only hits air units today, but any future ground use, or the
Poi stack copying the idiom, lands on the executor's input axis.

**Fix:** the SPEC must state stance ownership explicitly. Recommended wording: *stance is L2-writable
for bot-owned units (a stance write is legitimate operational intent, exactly like a human toggling
the stance button); the executor re-reads stance every evaluation and never caches it across
evaluations.* That makes the current FSM behavior legal-by-definition and removes the ambiguity —
at the cost that profile-level "this bot fights Defensive" is advisory. If the manager instead wants
profile stances sticky, the FSM writes must be gated off on experimental; pick one, in the SPEC.

### S2 — "Just beyond" / "just inside" is not a query the Phase-1 layers can answer

TerrainAffordanceLayer offers exactly: `CoverQuality(cell)` (8-neighbor density sum, passable cells
only — dense cells are skipped, TerrainAffordanceLayer.cs:98), `IsCoverEdge(cell)` (gradient
`magSq ≥ 1`, :129-134), `OutwardFacing(cell)` (negated gradient Yaw, :133). There is no
interior/exterior classification, no cluster identity, no "N cells beyond the edge" query.
Consequences the SPEC glosses over:
- Hunt's "at/just beyond the treeline" and Defensive's "just inside" both resolve, today, to *the same
  edge-cell set* — the distinction must be derived by stepping along `±OutwardFacing`.
- A 1-cell-wide tree strip produces passable edge cells on both sides with opposite normals; a unit on
  the wrong side sees an `OutwardFacing` pointing *away* from the threat and must reject it, not
  walk around blindly.
- Cells adjacent to map borders or inside dense blobs can have near-zero gradients (interior ⇒ not
  edge) — fine — but *diagonal-only* contact yields weak gradients that pass the `magSq ≥ 1` threshold
  with noisy normals.

**Fix (define in the SPEC, implement in the executor):**
- Candidate set = edge cells within the leash where the angular distance between `OutwardFacing(cell)`
  and the threat bearing is ≤ a tolerance (e.g. 256 WAngle units ≈ 90°): "this edge faces the threat".
- **Defensive target** = the edge cell itself (it is passable and cover-adjacent by construction —
  that *is* "just inside, concealed").
- **Hunt target** = edge cell stepped +1 cell along `OutwardFacing` (rounded to the dominant octant),
  falling back to the edge cell if the stepped cell is impassable/occupied. "Creep forward" = on
  re-evaluation Hunt may select a candidate strictly closer to the threat bearing than its current
  cell; Defensive may not.
- Every candidate is validated at use time: `Mobile.CanStayInCell` / pathability + not reserved by
  another executor claim. Tie-breaks: highest CoverQuality, then smallest angular error, then lowest
  (cell.Y, cell.X) — all integer, deterministic (§5).

### S3 — Static affordance layer vs a dynamic world

Computed once in `IWorldLoaded` (TerrainAffordanceLayer.cs:65-79) from `Map.DensityLayer`. If density
sources are destructible (trees crushed/burned) the cover data goes stale for the rest of the match;
and CoverQuality says nothing about whether the cell is *reachable* (a cover-adjacent cell across a
river scores identically). **Fix:** Phase 2 accepts staleness (document in SPEC — same trust level the
human player's eyeball has for map foliage), but the executor must never move to a cell it hasn't
passability-checked at decision time (covered by S2's validation step). If WW3MOD maps gain
significant destructible cover later, add a dirty-region recompute hook then — not now.

### S4 — Repositioning fights the suppression/prone system

infantry.yaml:252: `ProneCondition: deployed || suppressed > 30 || !moving || critical-damage` — the
`!moving` clause means **any executor move order stands the unit up out of prone**; and
SuppressionSpeedMultiplier (infantry.yaml:351-377) bands movement down to 10% speed at high
suppression. The executor's most tempting moment to act ("unit under fire in the open — get it to
cover!") is precisely when a move order maximizes exposure: unit stands, crawls at −90%, dies.
**Fix:** hard gate in the executor: skip repositioning for units whose suppression exceeds a YAML
threshold (default ≈ the prone trigger, 30); let the suppression system do its job. This also keeps
the executor out of the always-on suppression feedback loop (suppressed → move → more exposure → more
suppression).

### S5 — In-transit detours have no defined activity semantics

SPEC §2 pt 2 permits the executor to "detour in transit". Unanswered: when does a detour trigger
(threat field crosses a threshold along the path? at what sample rate?); how is it represented
(replace the Move? wrap it? queue after?); how does §2 pt 4 (fresh explicit order aborts instantly)
distinguish "cancel the detour, keep the original Move" from "cancel everything"? Any implementation
that replaces the activity queue loses the original destination; any that queues after doesn't detour.
The correct mechanism is a wrapper activity with the detour as `ChildActivity` — but that is real
activity-system surface area with its own cancellation-semantics bugs.
**Fix:** amend the SPEC to scope Phase 2 v1 to **arrival/idle-time positioning only** (evaluate on
becoming idle or on periodic re-check while idle; never touch a moving unit). In-transit detours move
to a later phase as their own deliverable, likely as the same wrapper activity the operations layer
will want for convoy behavior. This kills the whole §2 pt 4 mid-detour ambiguity for Phase 2: a fresh
order simply replaces the queue, and the executor's idle-only trigger can't fire during it.

### S6 — Same-tick INotifyIdle contention

Actor.cs:300-302: when idle, *all* `INotifyIdle` handlers run that tick, in trait construction order,
even if an earlier handler already queued an activity. Executor + CohesionSlotMemory (+ AutoTarget's
TickIdle scan) can each queue/act on the same tick; last writer wins the queue, and trait order in the
actor definition silently decides the winner. **Fix:** executor's TickIdle re-checks
`self.CurrentActivity == null` before acting *and* the B2 slot-Assign rule removes the substantive
conflict (both traits then want the same cell). Document the required trait ordering
(executor after CohesionSlotMemory) as a YAML convention regardless.

### S7 — Leash semantics are unpinned

SPEC §2 says adjustments stay "within a leash" but never defines the anchor or units. If the anchor is
*current position*, each adjustment re-anchors and the composition of many small adjustments walks the
unit arbitrarily far (Hunt "creep forward" makes this a feature — but unbounded). If it's the
*commanded destination*, creep is bounded and §2 pt 4 abort has a natural home to return to.
**Fix (spec text):** leash anchor = the unit's last commanded destination (explicit order target cell,
or the CohesionSlotMemory assigned slot when the order was grouped — CohesionMoveModifier.cs:745 keeps
that fresh); leash radius = YAML int in cells, default **4** (matches SightingThreatLayer
ContributionRadius; comfortably inside the Phase-0 Tight footprint cap of ~8×5,
CohesionMoveModifier.cs:53-73, so the executor can never strand a unit outside its formation
footprint). Hunt's creep is leashed the same way — creeping re-anchors **only** when L2 issues a new
order, never by the executor itself.

### S8 — §5 needs one Phase-2-specific determinism sentence

§5 is correct but was written with world-trait layers in mind. The executor is the first *unit trait*
in this stack that makes behavioral decisions in the sim on every client. The trap, verified:
`Sync.HashRandom` includes only `SharedRandom.Last` (World.cs:543); `LocalRandom` is deterministically
seeded (World.cs:219-228) but **unhashed** — a divergent LocalRandom call sequence desyncs silently.
Bot modules use LocalRandom freely (e.g. SquadManagerBotModule.cs:194 staggers with it) and get away
with it because bot decisions become net orders; the executor's decisions become *direct activity
queues on every client* and get no such laundering. **Fix (spec amendment):** "The Phase-2 executor
must not call LocalRandom, ever. Stagger via SharedRandom in WorldLoaded (SightingThreatLayer.cs:123
pattern); all choice among equals resolves by the S2 tie-break chain (CoverQuality, angular error,
(Y, X), ActorID)."

---

## NOTES

### N1 — Human-UX decisions to record (Phase 3 lands these on humans by default)

- **Capture resets stances wrong:** `AutoTarget.OnOwnerChanged` resets to `Initial*` stances
  (AutoTarget.cs:411-424); the per-type UnitDefaultsManager defaults are applied only in `Created()`
  and only for `Owner.Playable && !Owner.IsBot` (AutoTarget.cs:358-388). A captured unit therefore
  ignores the new owner's per-type defaults. Pre-existing wart, but Phase 3 makes stance
  behaviorally load-bearing, which promotes it from cosmetic to gameplay-visible. Log as a Phase-3
  checklist item.
- **HoldFire vs positioning:** AutoTarget's idle scan bails for `Stance < Ambush` (AutoTarget.cs:495),
  but fire stance and engagement stance are orthogonal axes. Decide and write down: the executor
  positions HoldFire units normally (recommended — "sneak to the treeline without shooting" is a
  legitimate and desirable combo); only `EngagementStance.HoldPosition` disables it (§4).
- **Order-cancel expectation:** §2 pt 4 must be *instant and visible* for humans — the S5 idle-only
  scoping makes this trivially true (a fresh order replaces the queue; the executor can't be
  mid-anything on a moving unit). Keep it that way; if detours ever land, revisit.

### N2 — Benchmark governance: the gating shape is already proven

- Bot-side: `GrantConditionOnBotOwner@experimental` grants `enable-ai-experimental` to bots on the
  experimental profile (ai.yaml:61-63) — hang the executor's bot-facing behavior on that condition via
  `ConditionalTrait`.
- Human-side: a plain YAML bool, default **false** through Phase 2; flipped to true (with UI) at
  Phase 3, which the SPEC already declares a re-baseline event.
- Byte-identical @stable promise: follow MountedTransportBotModule.UnloadOnArrival verbatim — "Kept
  default-off so @stable/controls stay byte-identical; only set on the @experimental twin"
  (MountedTransportBotModule.cs:81-86). Phase 2 as specced (default-off everywhere, no consumer)
  changes zero benchmark behavior; say so in the commit message.

### N3 — Host-trait shape constraint

`EngagementStance` lives on AutoTarget, and naval actors have no AutoTarget (survey HARD CONSTRAINTS).
The executor must be a **separate trait** that reads `TraitOrDefault<AutoTarget>()` — not an
AutoTarget extension — and Phase 2 scopes to ground units that carry both. Units without AutoTarget
simply don't get the trait in YAML.

### N4 — Operations-layer compatibility (architecture-doc riders)

The future Operation callers will command intent and expect: (a) unit-level ledger claims they can
inspect/preempt, (b) arrival/failure signals on the event bus (`BotEvent {Tick, Kind, Priority,
SubjectActorId, Where}`). The executor contract below is compatible if it (1) uses a reserved ledger
key grammar (`tacpos:<actorId>`) so Operations can recognize and, at higher priority, preempt tactical
claims; (2) keeps a queryable per-unit state enum (Idle/Adjusting/Arrived/Aborted) that a Phase-3+
event emitter can lift onto the bus without touching the executor's internals. Both are free at
design time and expensive to retrofit; adopt now.

---

## Hardened implementation brief (start here)

**Trait:** `StancePositioningExecutor : ConditionalTrait<StancePositioningExecutorInfo>,
INotifyIdle, INotifyCreated` (+ `ITick` only if the idle-recheck cadence needs it — prefer pure
TickIdle with an internal cooldown). Per-unit trait on ground combat actors that carry AutoTarget and
Mobile. Naval/air excluded in Phase 2 (N3).

**YAML (all defaults chosen for @stable byte-identity, N2):**
```yaml
StancePositioningExecutor:
    RequiresCondition: enable-tactical-positioning || enable-ai-experimental
    LeashRadius: 4              # cells, anchored per S7
    EvaluateCooldown: 30        # ticks between idle re-evaluations (coprime-ish with 25/50/75/100)
    MinThreatIntensity: 40      # B3 gate
    DirectionAmbiguityNum/Den   # B3 |Dir|²·Den ≥ intensity²·Num gate
    MaxSuppressionToMove: 30    # S4 gate, matches prone trigger
    FacingToleranceAngle: 256   # S2 edge-facing tolerance (WAngle units)
```
`enable-tactical-positioning` granted by nothing in Phase 2 (humans get it in Phase 3);
`enable-ai-experimental` comes from GrantConditionOnBotOwner (ai.yaml:61-63). Net: default-off
everywhere except experimental bots. @stable untouched ⇒ no re-baseline.

**Evaluation trigger (S5, S6):** only in `TickIdle`, only when `self.CurrentActivity == null`, only
when cooldown elapsed, never for `EngagementStance.HoldPosition` (§4) and never while suppression >
threshold (S4). No in-transit logic of any kind in Phase 2.

**Decision pipeline (per evaluation, all integer math, S8):**
1. Threat bearing: aggregate scan per B3 (ActiveCells within leash+scan radius of anchor), gated on
   intensity + ambiguity ratio; fallback chain per B3.
2. Candidate cells: threat-facing edge cells within leash of the S7 anchor, per S2 (Hunt steps +1
   along OutwardFacing; Defensive takes the edge cell; creep rule for Hunt only).
3. Validation: `CanStayInCell`/pathability + not claimed (`tacpos:` ledger check for bot owners).
4. Tie-breaks: CoverQuality desc, angular error asc, (Y, X) asc, ActorID asc.
5. Act: `slotMemory?.Assign(target, tick)` (B2), commit `tacpos:<actorId>` to the owner's
   PoiGoalGuard.Ledger if bot-owned (TTL ~150), then `self.QueueActivity(new Move(self, target))`.

**Ledger (B1):** `PoiGoalGuard.Ledger`, key grammar `tacpos:<actorId>`, bot owners only (humans skip —
no bot layer contests them). **Plus the B1 rider:** grouped-order issuance in GroundStates filters
ledger-committed units when a PoiGoalGuard exists — or, if deferred, the executor ships gated to
@experimental only and the FSM gap is logged as a Phase-4 rider. Do not ship the executor on profiles
whose re-fire paths ignore the ledger.

**Abort (§2 pt 4):** free by construction — a fresh explicit order replaces the activity queue; the
executor's next TickIdle re-reads everything (S1: never cache stance). Release the ledger claim and
`Clear()` the slot override when the queued Move is cancelled (implement via the Move's
`OnActorDispose`-safe cancellation or a cheap "did I arrive where I claimed" check on next TickIdle).

**Determinism checklist (§5 + S8):** no LocalRandom; SharedRandom only for one-time stagger; no
RenderPlayer/LocalPlayer; all inputs from SightingThreatLayer (per-player-legal by construction),
TerrainAffordanceLayer (static), own Mobile/AutoTarget state; additive aggregates + total-order
tie-breaks so iteration order never matters.

**State for N4:** expose `enum AdjustmentState { None, Adjusting, Arrived, Aborted }` +
`CPos? CurrentTarget` as public getters. Phase 3's event emitter turns transitions into BotEvents.

**Test hooks (for the implementer's autotest pass, not this doc):** the §3d overlay already
visualizes the threat field; add a debug overlay knob for chosen-target cells before tuning.
