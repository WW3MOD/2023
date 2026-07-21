# Strategic/Tactical Split — bot strategy vs stance-driven unit micro on shared map layers

**Status: RATIFIED (design) — implementation starting.** Ratified by the user
2026-07-21. This is the durable, self-contained home of the vision; it survives
with zero external context.

- **Primary survey input:** `WORKSPACE/plans/260722_stance_tactical_survey.md`
  (commit `9c94ce63`). Every `file:line` citation below traces to it.
- **Phases run strictly in order 0 → 5.** Phase 0 (global cohesion fix) and
  Phase 4 (full fog migration) are both declared re-baseline events that shift
  every ladder baseline — see §7.
- Original spec authored 2026-07-21T11:02:47Z under the manager specs dir; copied
  here (adapted header) so the vision is not stranded in transient manager state.

---

Goal (user's words, paraphrased): bots issue orders on a strategic level;
**stances** own unit micro — responding to threats, seeking cover, treeline
positioning — and those behaviors work identically for human players' units
unless the human disables the stance.

## 1. Current substrate (what already exists)

- **Stances**: all four WW3MOD families live on the single `AutoTarget` trait
  (AutoTarget.cs:20-26). Classic RA UnitStance is gone. Families: Fire-discipline
  {HoldFire, Ambush, FireAtWill}; **Engagement {HoldPosition, Defensive, Hunt}**
  (default Defensive); Cohesion {Tight, Loose, Spread}; Resupply {Hold, Auto,
  Evacuate}. UI buttons + hotkeys + Ctrl/Alt-click meta-system exist; per-type
  human defaults persisted by UnitDefaultsManager, applied at spawn for non-bot
  players only (AutoTarget.cs:358-388) — the human-toggleability seam is already
  wired.
- **Cover**: cover cells are queryable today (Map.DensityLayer +
  CohesionMoveModifier.CoverScore); LOS cover is real (ShadowLayer, BlocksSight,
  MissChancePerDensity).
- **Enemy memory**: FrozenActorLayer is a per-player, fog-correct last-seen record.
- **Existing autonomous micro**: AutoTarget idle-scan/return-fire (stance-gated),
  suppression→prone (always-on), medic auto-target, auto-resupply, SmartMove
  fire-on-move, heli autorotate/crew-eject.
- **Missing** (the actual gap, narrower than assumed): an enemy-**direction**
  field; anything joining cover to threat; a positioning executor; regroup;
  fog-respecting threat grid (both AI grids — InfluenceMap, ThreatMapManager —
  are omniscient).
- **Known bug**: cohesion over-spread. CohesionMoveModifier is always-on for any
  grouped move (mode only selects spacing, :588-590,626-627); ComputeBoxSlots
  offsets scale unbounded with spacing × count (only map.Clamp, :294); Spread
  spacing ~3× Tight; no regroup exists. Converges with the benchmark's ~−$1,500
  dispersion credit. Stale debug log at :679-695.

## 2. Architecture — three layers, one contract

**L1 Strategic (bot-only)** — bot modules decide *where to fight*: POI selection,
balance-of-power bias, capture targets, budget. Unchanged surface; over time it
must *stop issuing micro that L3 owns*.

**L2 Operational (bot-only)** — squad FSMs turn strategy into destinations and
grouped orders. Today they re-fire grouped orders ~every 75 ticks — the main
collision source with any unit-level autonomy.

**L3 Tactical (SHARED unit traits)** — stance-conditioned micro that runs
identically for bot- and human-owned units: threat response, cover-seeking,
treeline positioning, regroup. Humans control it per unit type via the existing
stance UI/defaults.

**The contract (binding on all future work):**
1. Upper layers command *intent* (destination, target area); L3 owns *execution*
   within a bounded leash radius of the commanded position (or of the current
   path while in transit).
2. L3 never **cancels** an explicit order — but it MAY act while one is in flight
   (user clarification 2026-07-21). In-transit reactions are stance-conditioned
   *detours*: the order remains the unit's commitment and resumes when the
   reaction resolves. Examples: one stance pushes through contact without
   deviating; another stops and seeks nearby cover when under fire, then
   continues. Arrived/idle units get the full positioning behavior; in-transit
   units get the bounded reaction subset.
3. Any L3 repositioning registers in a commitment ledger (the established
   pattern: PoiGoalGuard.Ledger / IsPassengerReserved / BotBlackboard.ClaimUnit)
   so squad FSM re-fires and other modules don't fight it; L3 must survive the
   75-tick re-fire without oscillating.
4. A fresh explicit order instantly aborts L3's current adjustment/detour and
   re-arms L3 for the new order's transit + arrival.

## 3. Map layers

**3a. Sighting/threat layer (NEW, per-player, fog-respecting)** — the user's
"where has the enemy been seen" layer. Built strictly from synced,
per-player-legal sources (own Shroud visibility + FrozenActorLayer); decaying
intensity per cell; exposes `ThreatDirection(cell)` (dominant bearing to recent
sightings) and `ThreatIntensity(cell)`. Implementation: CellLayer<T> + staggered
N-tick recompute (the established cheap pattern). Per-player instance ⇒ identical
information rights for humans and bots at the unit level.

**3b. Terrain-affordance layer (NEW, static, computed at map load)** — per-cell
cover quality plus **cover-edge orientation**: for forest/cover clusters, which
cells are the edge and which outward facing they have. This is what makes the
treeline example cheap at runtime: "edge cell of this cluster facing the threat
direction" is a lookup, not a search.

**3c. Existing strategic grids (InfluenceMap, ThreatMapManager)** — **FULL FOG
MIGRATION** (fork 3, user answered: full migration now). Bot strategic grids
become fog-respecting as part of this project, sourced from the same per-player
intel substrate as 3a — bots and humans reason on identical information rights at
every layer. This absorbs ladder cycle 5 into Phase 4 rather than deferring to
it. Consequences accepted: bots initially get *weaker* (they lose free intel) and
every benchmark baseline shifts — Phase 4 is therefore an explicit, declared
re-baseline event, and scouting/recon behavior becomes strategically meaningful
for bots (a follow-on behavior cycle: bots must *spend* on intel).

**3d. Intel overlay (NEW, view-layer only)** — hold-Space overlay (Space already
shows waypoints) rendering, for development AND live-game intel:
- Balance-of-power / influence as a semi-transparent color wash: green =
  friendly-dominant, red = enemy-dominant. **Grayzone**: computed, not stored —
  cells where neither side's share clears a threshold render neutral/gray; no
  third data channel needed.
- Last-seen enemies as **GPS dots**, reusing the original OpenRA satellite-power
  substrate (present in-repo and already referenced by ww3mod rules:
  `engine/OpenRA.Mods.Cnc/Traits/GpsDot.cs`, `GpsWatcher.cs`,
  `Effects/GpsDotEffect.cs`) driven from FrozenActorLayer sightings.
- Dev switch: temporarily always-visible during development for layer
  verification; ships as hold-Space.
- Strictly render-side (RenderPlayer is legal here — this is NOT sim code); reads
  the viewing player's own per-player layers only, so it leaks nothing through fog.

## 4. Stance mapping — reuse the Engagement family, add no new family

The user's aggressive/defensive treeline example maps directly onto the existing
Engagement axis:

| Engagement stance | L3 positioning behavior in/near cover |
|---|---|
| **Hunt** (aggressive) | take the forward-most cover edge *toward* ThreatDirection — at/just beyond the treeline facing the enemy; may creep forward between covers within leash |
| **Defensive** (default) | take an in-cover cell **at the threat-facing edge** — concealed just inside the treeline with a line of fire toward ThreatDirection (hull-down equivalent); hold and return fire; never advances |
| **HoldPosition** | no autonomous repositioning at all — stand exactly where placed |

> **AMENDED 2026-07-21 (doctrine audit, `260722_doctrine_realism_audit.md`):** the
> original Defensive wording ("take cover *away* from ThreatDirection — back side of
> the trees") was inverted: LOS cover is real (BlocksSight / MissChancePerDensity),
> so a defender on the far side of a cover cluster has no line of sight through it and
> "hold and return fire" was unachievable as written. Corrected: **both** Hunt and
> Defensive use the threat-facing edge; the distinction is edge position + advance
> permission (Hunt at/beyond the edge and may creep forward; Defensive just inside it
> and static). Ratified intent (hull-down defenders that return fire) preserved.

Fire-discipline, Cohesion, Resupply families are orthogonal and unchanged
(Cohesion gets the Phase-0 bug fix; Ambush + concealed-at-edge Defensive posture
composes naturally into an ambush posture).

## 5. Determinism rules (hard, from survey Q6)

- LocalRandom is **not** in the sync hash (World.cs:543) — a client-divergent
  read desyncs silently. All L3 logic uses SharedRandom or deterministic
  tie-breaks (ActorID order).
- Never read RenderPlayer/LocalPlayer in sim. Per-player state lives in map-layer
  instances keyed by Player (the MapLayers model). Integer cell math only.

## 6. Benchmark governance

- L3 traits are shared ⇒ touching them changes @stable *and* the frozen controls.
  Therefore: new positioning micro lands **default-off** (@experimental profile or
  per-type YAML opt-in) and is priced in the benchmark before any promotion.
- Shipping a behavior to everyone (humans + all profiles) is a **deliberate
  re-baseline event**, declared in advance — the dispersion episode is the
  cautionary precedent, not to be repeated accidentally.
- No batch/tournament runs without explicit user goahead (standing rule);
  machine-hold has since been LIFTED (builds/tests allowed, serialized) —
  see the REVIEW board for the live governance state.

## 7. Phasing

- **Phase 0 — Cohesion bug fix (RATIFIED: global fix + declared re-baseline).**
  Bound total formation extent (cap ComputeBoxSlots footprint by count-aware max
  radius), add regroup-on-arrival, remove stale debug log. Ships to everyone
  (including frozen controls); folds into the queued dispersion re-verify cycle;
  ladder re-baselines afterward.
- **Phase 1 — Layers + overlay.** Sighting/threat layer (3a) + affordance/cover-edge
  precompute (3b) + the hold-Space intel overlay (3d, dev-mode always-on). Pure
  data + render; no sim behavior change; the overlay IS the verification tool —
  the user can eyeball layer correctness in-game before any behavior consumes it.
- **Phase 2 — Positioning executor.** New trait implementing §4 semantics under
  the §2 contract (including in-transit detours); default-off; autotest + playtest.

  > **AMENDED 2026-07-22 (pre-implementation red-team, `260722_phase2_redteam.md` @ 4a2c56f0)** — Phase 2 v1 is hardened as follows; the red-team doc's "Hardened implementation brief" is the binding starting point:
  > 1. **Idle-only in v1 (S5).** The executor evaluates only on `TickIdle` (idle/arrival + cooldown); it never touches a moving unit. §2 pt 2's in-transit detours are DEFERRED to a later phase as their own deliverable (a `ChildActivity` wrapper, likely shared with the operations layer's convoy needs). §2 pt 4 abort becomes free by construction: a fresh order replaces the queue. The user's transit clarification is preserved in substance, deferred in timing.
  > 2. **Ledger scoping (B1).** `PoiGoalGuard.Ledger` does NOT gate squad-FSM re-fires (grep-proven: zero ledger checks in SquadManagerBotModule/GroundStates/StateBase/LayeredDefence). Executor commits `tacpos:<actorId>` claims (bot owners only, TTL ~150t) AND the grouped-order build in GroundStates filters ledger-committed units when a PoiGoalGuard exists (behavior-inert on profiles with no executor claims). Executor ships gated `enable-tactical-positioning || enable-ai-experimental` — default-off everywhere except experimental bots; @stable byte-identical (UnloadOnArrival precedent).
  > 3. **Slot ownership (B2).** On repositioning, the executor re-`Assign`s the unit's `CohesionSlotMemory` slot to the chosen cell (return-to-slot then reinforces the choice instead of a 750-tick tug-of-war); slot cleared/released on abort.
  > 4. **Threat-bearing gates (B3).** Facing comes from an aggregate scan over `ActiveCells` near the anchor, never a single-cell read; gated on `MinThreatIntensity` + a direction-ambiguity ratio (opposite-axis cancellation ≡ no-data otherwise). Fallback chain: last accepted bearing (TTL) → bearing toward commanded destination → no repositioning. Frozen-ghost staleness is ACCEPTED for Phase 2 (fog-honest; the Phase-4 scout link is the mitigation).
  > 5. **Stance ownership (S1).** Stance is L2-writable for bot units (an FSM stance write is legitimate operational intent, same as a human toggling the button); the executor re-reads stance every evaluation, never caches.
  > 6. **Leash pinned (S7).** Anchor = last commanded destination (or assigned cohesion slot for grouped orders); radius YAML, default 4 cells (inside the Phase-0 footprint cap). Hunt's creep never re-anchors itself; only a new L2/human order re-anchors.
  > 7. **Edge derivation (S2).** Defensive target = the threat-facing edge cell itself; Hunt = the chosen edge cell stepped +1 along the aggregate threat bearing (as implemented — `WVec.FromSpeedAndAngle`; equivalent to `OutwardFacing` within the facing tolerance but stepping toward the *actual* threat and avoiding a WAngle→cell sign error), bounded by the leash + passability, fallback = the edge cell itself; candidates validated for passability/claims at decision time; tie-breaks CoverQuality desc → angular error asc → (Y,X) → ActorID.
  > 8. **Suppression gate (S4).** No repositioning above suppression ~30 (the prone trigger) — a move order breaks prone and crawls at up to −90% speed.
  > 9. **Determinism sharpening (S8, extends §5).** The executor must never call `LocalRandom` (it queues activities directly on every client — no bot-order laundering); SharedRandom stagger + integer tie-breaks only.
  > 10. **Ops compatibility (N4).** Reserved ledger key grammar `tacpos:` + a public `AdjustmentState`/`CurrentTarget` surface so the future operations layer/event bus can inspect and preempt without retrofit.
- **Phase 3 — Human enablement (RATIFIED: default ON).** Wire executor to the
  Engagement stance; ships active on the default Defensive stance. Per-unit-type
  reasonable defaults authored in YAML; players adjust via the existing
  Ctrl-Alt-click per-type default mechanism (UnitDefaultsManager,
  AutoTarget.cs:358-388 — confirmed working substrate, persists per type: e.g.
  tanks default stance A, artillery stance B). Phase-2 playtest still tunes the
  leash before this ships, but the shipped default is ON.
- **Phase 4 — Bot consumption + full fog migration (RATIFIED: full migration).**
  Squad FSMs delegate micro to L3 (stop re-issuing positioning); InfluenceMap +
  ThreatMapManager rebuilt on per-player fog-respecting intel (absorbs ladder
  cycle 5). Declared re-baseline event; expect an initial bot-strength dip; opens
  a recon/scouting behavior cycle so bots buy back the lost intel.
  *AMENDED 2026-07-21 (doctrine audit):* a **minimal scout link ships inside
  Phase 4 itself** (e.g. cheapest fast unit periodically tasked toward stale
  high-value cells of the per-player intel layer), not deferred to the follow-on
  recon cycle — otherwise the phase creates a blind-and-dumb window where bots
  march into ambushes on decayed intel. The full recon behavior cycle still
  follows as its own priced cycle.
- **Phase 5 — Extended micro.** HP/threat flee (the TODO at AutoTarget.cs:472),
  panic reactivation, bounding movement between covers.

> **AMENDED 2026-07-21 (operations-layer adoption, running on the posted default —
> source: `260722_bot_brain_architecture.md`, EXTEND verdict; RETHINK #2's revival
> rule met).** Riders on the phasing above, critical path (Phases 1–2) unchanged:
> **Phase 3** also carries the deterministic event bus + event-driven commitment
> revision retrofit and the unit-role resolver (each behind its own flag, priced
> separately on the ladder; the role resolver also cures the ai.yaml:349
> artillery/SHORAD-as-mainline conflation with no operations dependency).
> **Phase 4** may develop the Operation skeleton (object + lifecycle + single-force
> Assault) in parallel against omniscient scoring; its promotion gates on the
> fog-honest intel substrate. **Phase 5** becomes the operations layer proper —
> multi-force staging, same-tick synchronized launch, Pincer, role-based
> combined-arms composition; PoiOffensive's axis interior retires to a proposal
> source. The attention-scheduler commander (decisions-per-minute budget,
> difficulty knob) opens as the phase after, only if operations price positively.

Each phase is independently shippable and benchmark-priceable; Phases 1–2 are the
critical path to the user's treeline scenario.

## 8. Forks — RESOLVED by the user (2026-07-21)

1. Cohesion fix scope — **Global bug fix + deliberate re-baseline** (= agent
   default).
2. Human default autonomy — **Default ON** (agent had defaulted to
   decide-after-playtest; user chose ON, backed by per-type Ctrl-Alt-click
   defaults).
3. Bot fog policy — **Full migration now** (agent had defaulted to hybrid; user
   chose the bigger move — Phase 4 absorbs ladder cycle 5 and its re-baseline).

## 9. User clarifications incorporated (2026-07-21)

- L3 in-transit semantics: stance layer may react during transit (detour), never
  cancels the order (§2 contract pt 2).
- Hold-Space intel overlay with BoP color wash + GPS-dot sightings; grayzone
  computed; dev always-on switch (§3d).
- Per-unit-type stance defaults via existing Ctrl-Alt-click / UnitDefaultsManager
  mechanism carry the Default-ON decision (§7 Phase 3).
