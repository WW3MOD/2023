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
| **Hunt** (aggressive) | take the cover edge *toward* ThreatDirection — treeline facing the enemy; may creep forward between covers within leash |
| **Defensive** (default) | take cover *away* from ThreatDirection — back side of the trees, hull-down equivalent; hold and return fire |
| **HoldPosition** | no autonomous repositioning at all — stand exactly where placed |

Fire-discipline, Cohesion, Resupply families are orthogonal and unchanged
(Cohesion gets the Phase-0 bug fix; Ambush + cover-back composes naturally into
an ambush posture).

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
- **Phase 5 — Extended micro.** HP/threat flee (the TODO at AutoTarget.cs:472),
  panic reactivation, bounding movement between covers.

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
