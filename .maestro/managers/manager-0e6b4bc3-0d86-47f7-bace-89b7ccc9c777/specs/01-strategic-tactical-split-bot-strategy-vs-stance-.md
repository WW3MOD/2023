# Strategic/Tactical Split — bot strategy vs stance-driven unit micro on shared map layers

_spec · status: draft · authored 2026-07-21T11:02:47.664Z_

# Strategic/Tactical Split — Design Spec (draft, awaiting user ratification)

Goal (user's words, paraphrased): bots issue orders on a strategic level; **stances** own unit micro — responding to threats, seeking cover, treeline positioning — and those behaviors work identically for human players' units unless the human disables the stance. Discussion-only phase; no implementation until this spec is ratified.

Primary input: survey `WORKSPACE/plans/260722_stance_tactical_survey.md` (commit 9c94ce63) — all file:line claims below trace to it.

## 1. Current substrate (what already exists)

- **Stances**: all four WW3MOD families live on the single `AutoTarget` trait (AutoTarget.cs:20-26). Classic RA UnitStance is gone. Families: Fire-discipline {HoldFire, Ambush, FireAtWill}; **Engagement {HoldPosition, Defensive, Hunt}** (default Defensive); Cohesion {Tight, Loose, Spread}; Resupply {Hold, Auto, Evacuate}. UI buttons + hotkeys + Ctrl/Alt-click meta-system exist; per-type human defaults persisted by UnitDefaultsManager, applied at spawn for non-bot players only (AutoTarget.cs:358-388) — the human-toggleability seam is already wired.
- **Cover**: cover cells are queryable today (Map.DensityLayer + CohesionMoveModifier.CoverScore); LOS cover is real (ShadowLayer, BlocksSight, MissChancePerDensity).
- **Enemy memory**: FrozenActorLayer is a per-player, fog-correct last-seen record.
- **Existing autonomous micro**: AutoTarget idle-scan/return-fire (stance-gated), suppression→prone (always-on), medic auto-target, auto-resupply, SmartMove fire-on-move, heli autorotate/crew-eject.
- **Missing** (the actual gap, narrower than assumed): an enemy-**direction** field; anything joining cover to threat; a positioning executor; regroup; fog-respecting threat grid (both AI grids — InfluenceMap, ThreatMapManager — are omniscient).
- **Known bug**: cohesion over-spread. CohesionMoveModifier is always-on for any grouped move (mode only selects spacing, :588-590,626-627); ComputeBoxSlots offsets scale unbounded with spacing × count (only map.Clamp, :294); Spread spacing ~3× Tight; no regroup exists. Converges with the benchmark's ~−$1,500 dispersion credit. Stale debug log at :679-695.

## 2. Architecture — three layers, one contract

**L1 Strategic (bot-only)** — bot modules decide *where to fight*: POI selection, balance-of-power bias, capture targets, budget. Unchanged surface; over time it must *stop issuing micro that L3 owns*.

**L2 Operational (bot-only)** — squad FSMs turn strategy into destinations and grouped orders. Today they re-fire grouped orders ~every 75 ticks — the main collision source with any unit-level autonomy.

**L3 Tactical (SHARED unit traits)** — stance-conditioned micro that runs identically for bot- and human-owned units: threat response, cover-seeking, treeline positioning, regroup. Humans control it per unit type via the existing stance UI/defaults.

**The contract (binding on all future work):**
1. Upper layers command *intent* (destination, target area); L3 owns *execution* within a bounded leash radius of the commanded position (or of the current path while in transit).
2. L3 never **cancels** an explicit order — but it MAY act while one is in flight (user clarification 2026-07-21). In-transit reactions are stance-conditioned *detours*: the order remains the unit's commitment and resumes when the reaction resolves. Examples: one stance pushes through contact without deviating; another stops and seeks nearby cover when under fire, then continues. Arrived/idle units get the full positioning behavior; in-transit units get the bounded reaction subset.
3. Any L3 repositioning registers in a commitment ledger (the established pattern: PoiGoalGuard.Ledger / IsPassengerReserved / BotBlackboard.ClaimUnit) so squad FSM re-fires and other modules don't fight it; L3 must survive the 75-tick re-fire without oscillating.
4. A fresh explicit order instantly aborts L3's current adjustment/detour and re-arms L3 for the new order's transit + arrival.

## 3. Map layers

**3a. Sighting/threat layer (NEW, per-player, fog-respecting)** — the user's "where has the enemy been seen" layer. Built strictly from synced, per-player-legal sources (own Shroud visibility + FrozenActorLayer); decaying intensity per cell; exposes `ThreatDirection(cell)` (dominant bearing to recent sightings) and `ThreatIntensity(cell)`. Implementation: CellLayer<T> + staggered N-tick recompute (the established cheap pattern). Per-player instance ⇒ identical information rights for humans and bots at the unit level.

**3b. Terrain-affordance layer (NEW, static, computed at map load)** — per-cell cover quality plus **cover-edge orientation**: for forest/cover clusters, which cells are the edge and which outward facing they have. This is what makes the treeline example cheap at runtime: "edge cell of this cluster facing the threat direction" is a lookup, not a search.

**3c. Existing strategic grids (InfluenceMap, ThreatMapManager)** — **FULL FOG MIGRATION** (fork 3, user answered: full migration now). Bot strategic grids become fog-respecting as part of this project, sourced from the same per-player intel substrate as 3a — bots and humans reason on identical information rights at every layer. This absorbs ladder cycle 5 into Phase 4 rather than deferring to it. Consequences accepted: bots initially get *weaker* (they lose free intel) and every benchmark baseline shifts — Phase 4 is therefore an explicit, declared re-baseline event, and scouting/recon behavior becomes strategically meaningful for bots (a follow-on behavior cycle: bots must *spend* on intel).

**3d. Intel overlay (NEW, view-layer only)** — hold-Space overlay (Space already shows waypoints) rendering, for development AND live-game intel:
- Balance-of-power / influence as a semi-transparent color wash: green = friendly-dominant, red = enemy-dominant. **Grayzone**: computed, not stored — cells where neither side's share clears a threshold render neutral/gray; no third data channel needed.
- Last-seen enemies as **GPS dots**, reusing the original OpenRA satellite-power substrate (present in-repo and already referenced by ww3mod rules: `engine/OpenRA.Mods.Cnc/Traits/GpsDot.cs`, `GpsWatcher.cs`, `Effects/GpsDotEffect.cs`) driven from FrozenActorLayer sightings.
- Dev switch: temporarily always-visible during development for layer verification; ships as hold-Space.
- Strictly render-side (RenderPlayer is legal here — this is NOT sim code); reads the viewing player's own per-player layers only, so it leaks nothing through fog.

## 4. Stance mapping — reuse the Engagement family, add no new family

The user's aggressive/defensive treeline example maps directly onto the existing Engagement axis:

| Engagement stance | L3 positioning behavior in/near cover |
|---|---|
| **Hunt** (aggressive) | take the forward-most cover edge *toward* ThreatDirection — at/just beyond the treeline facing the enemy; may creep forward between covers within leash |
| **Defensive** (default) | take an in-cover cell **at the threat-facing edge** — concealed just inside the treeline with a line of fire toward ThreatDirection (hull-down equivalent); hold and return fire; never advances |
| **HoldPosition** | no autonomous repositioning at all — stand exactly where placed |

> **AMENDED 2026-07-21 (doctrine audit):** original Defensive wording ("back side of the trees") was inverted — no LOS through cover (BlocksSight/MissChancePerDensity) meant "return fire" was unachievable. Both Hunt and Defensive now use the threat-facing edge; distinction = edge position + advance permission (Hunt at/beyond + creeps; Defensive just inside + static). Ratified intent preserved.

Fire-discipline, Cohesion, Resupply families are orthogonal and unchanged (Cohesion gets the Phase-0 bug fix; Ambush + cover-back composes naturally into an ambush posture).

## 5. Determinism rules (hard, from survey Q6)

- LocalRandom is **not** in the sync hash (World.cs:543) — a client-divergent read desyncs silently. All L3 logic uses SharedRandom or deterministic tie-breaks (ActorID order).
- Never read RenderPlayer/LocalPlayer in sim. Per-player state lives in map-layer instances keyed by Player (the MapLayers model). Integer cell math only.

## 6. Benchmark governance

- L3 traits are shared ⇒ touching them changes @stable *and* the frozen controls. Therefore: new positioning micro lands **default-off** (@experimental profile or per-type YAML opt-in) and is priced in the benchmark before any promotion.
- Shipping a behavior to everyone (humans + all profiles) is a **deliberate re-baseline event**, declared in advance — the dispersion episode is the cautionary precedent, not to be repeated accidentally.
- No batch/tournament runs without explicit user goahead (standing rule); machine-hold currently in force.

## 7. Phasing

- **Phase 0 — Cohesion bug fix (RATIFIED: global fix + declared re-baseline).** Bound total formation extent (cap ComputeBoxSlots footprint by count-aware max radius), add regroup-on-arrival, remove stale debug log. Ships to everyone; folds into the queued dispersion re-verify cycle; ladder re-baselines afterward.
- **Phase 1 — Layers + overlay.** Sighting/threat layer (3a) + affordance/cover-edge precompute (3b) + the hold-Space intel overlay (3d, dev-mode always-on). Pure data + render; no sim behavior change; the overlay IS the verification tool — the user can eyeball layer correctness in-game before any behavior consumes it.
- **Phase 2 — Positioning executor.** New trait implementing §4 semantics under the §2 contract (including in-transit detours); default-off; autotest + playtest. *AMENDED 2026-07-22 (red-team `260722_phase2_redteam.md` @ 4a2c56f0):* v1 is idle-only (in-transit detours deferred to a later phase — user's transit clarification preserved in substance, deferred in timing); `tacpos:` ledger claims + GroundStates claim filter (B1 — the ledger alone doesn't gate FSM re-fires); executor re-Assigns the CohesionSlotMemory slot (B2); threat bearing from gated aggregate scan with fallback chain (B3); stance is L2-writable, executor re-reads every evaluation (S1); leash anchored at last commanded destination, default 4 cells (S7); suppression gate ~30 (S4); no LocalRandom ever (S8); `AdjustmentState` surface + `tacpos:` grammar reserved for ops layer (N4). Ships `enable-tactical-positioning || enable-ai-experimental`, default-off everywhere except experimental bots; @stable byte-identical.
- **Phase 3 — Human enablement (RATIFIED: default ON).** Wire executor to the Engagement stance; ships active on the default Defensive stance. Per-unit-type reasonable defaults authored in YAML; players adjust via the existing Ctrl-Alt-click per-type default mechanism (UnitDefaultsManager, AutoTarget.cs:358-388 — confirmed working substrate, persists per type: e.g. tanks default stance A, artillery stance B). Phase-2 playtest still tunes the leash before this ships, but the shipped default is ON.
- **Phase 4 — Bot consumption + full fog migration (RATIFIED: full migration).** Squad FSMs delegate micro to L3 (stop re-issuing positioning); InfluenceMap + ThreatMapManager rebuilt on per-player fog-respecting intel (absorbs ladder cycle 5). Declared re-baseline event; expect an initial bot-strength dip; opens a recon/scouting behavior cycle so bots buy back the lost intel. *AMENDED 2026-07-21 (doctrine audit):* a minimal scout link ships inside Phase 4 itself (cheapest fast unit tasked toward stale high-value intel cells) — not deferred to the recon cycle — to avoid a blind-and-dumb window; the full recon cycle still follows separately.
- **Phase 5 — Extended micro.** HP/threat flee (the TODO at AutoTarget.cs:472), panic reactivation, bounding movement between covers.

> *AMENDED 2026-07-21 (ops-layer adoption, on posted default — source `260722_bot_brain_architecture.md`, EXTEND verdict):* Phase 3 also carries the event bus + event-driven revision retrofit and the unit-role resolver (own flags, priced separately); Phase 4 may develop the Operation skeleton in parallel (promotion gates on fog-honest intel); Phase 5 becomes the operations layer proper (staging, same-tick synchronized launch, Pincer, role composition; PoiOffensive interior → proposal source); attention-scheduler commander after operations price positively.

Each phase is independently shippable and benchmark-priceable; Phases 1–2 are the critical path to the user's treeline scenario.

## 8. Forks — RESOLVED by the user (2026-07-21)

1. Cohesion fix scope — **Global bug fix + deliberate re-baseline** (= agent default).
2. Human default autonomy — **Default ON** (agent had defaulted to decide-after-playtest; user chose ON, backed by per-type Ctrl-Alt-click defaults).
3. Bot fog policy — **Full migration now** (agent had defaulted to hybrid; user chose the bigger move — Phase 4 absorbs ladder cycle 5 and its re-baseline).

## 9. User clarifications incorporated (2026-07-21)

- L3 in-transit semantics: stance layer may react during transit (detour), never cancels the order (§2 contract pt 2).
- Hold-Space intel overlay with BoP color wash + GPS-dot sightings; grayzone computed; dev always-on switch (§3d).
- Per-unit-type stance defaults via existing Ctrl-Alt-click / UnitDefaultsManager mechanism carry the Default-ON decision (§7 Phase 3).
