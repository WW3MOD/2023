# Intent-aware movement (cover-resolving cohesion upgrade)

**Author:** session 260512 — design sketch, not yet started.
**Status:** plan draft pending user approval. Staged in WORKSPACE; promote to `RELEASE_V1.md` once stable.
**Scope target:** v1 (hopefully). Lives in WORKSPACE until proven.

## The pitch

Every move/attack-move order is rewritten by an **intent interpreter** that
sits between order issuance and pathfinding. The interpreter reads a
per-cell **cover-density field** (built from `shadows.bin` LOS data in v1,
modular for more signals later), classifies the click relative to nearby
cover, and produces a **role-aware formation** with **per-stance leash
behavior** on the resulting cover zone.

In plain language: click the edge of a forest, the squad forms a line at
the tree line facing out. Click deep in the forest, they spread inside.
Click open ground, they form a default line at the click. Once placed,
the default-stance leash keeps them inside (or just at the edge of) the
cover zone — they don't drift forward into open ground as a side effect
of auto-engage or blocking.

## Why

- Infantry movement is the highest-friction UX in WW3MOD. Hand-placing
  squads into tree lines, around buildings, and into staggered defensive
  formations is fiddly today.
- WW3MOD already has a partial cohesion system (`CohesionMoveModifier`)
  that distributes grouped moves into a box formation but is
  terrain-blind. This is the natural place to upgrade.
- Bots inherit the tactical uplift for free (interpreter sits at the
  order-issuance layer, not the UI layer).
- It collapses several latent feature requests — "garrison this
  building", "form a defensive line here", "take cover" — into a single
  natural interaction: click where you want.

## How it differs from what exists today

`engine/OpenRA.Mods.Common/Traits/AutoTarget.cs` lines 24, 286–292 define
a `CohesionMode` enum (`Tight`/`Loose`/`Spread`) bound to Ctrl+Alt+1/2/3.
`engine/OpenRA.Mods.Common/Traits/CohesionMoveModifier.cs` intercepts
grouped Move/AttackMove orders and applies a box offset
(`1024×1024`/`2048×1536`/`3072×2560` wdist).

Three problems with the current implementation, all of which this plan
also fixes:

1. The `SetCohesion` order has no `IResolveOrder` handler. The mode is
   set locally on the trait but never synced.
2. No `INotifyCohesionChanged` (or equivalent) — stationary, individually
   moving, or attacking units ignore cohesion entirely.
3. Box-formation distribution is terrain-blind.

This plan **completes (1) and (2)** as foundational wiring, then
**replaces (3)** with the cover-aware slot bidder.

## Architecture

### Layer 1 — Cover-density field (modular)

```
CoverField(cell) = Σ wᵢ · Signalᵢ(cell)
```

- v1 ships with **one** signal: `Signal_LOS` derived from the saved
  `shadows.bin` cache. Weight = 1.0.
- Future signals plug in as read-only functions `(cell, world) → float`:
  `Signal_Walls`, `Signal_Buildings`, `Signal_Sandbags`,
  `Signal_RidgeLOS`, etc.
- Field is precomputed once per map, invalidated only when terrain or
  building footprints change. ~256 KB for a 256×256 map.

Source `shadows.bin` already invalidates correctly when terrain changes;
the existing `--regen-shadows` flow doubles as cover-field regen.

### Layer 2 — Intent interpreter (order-issuance layer)

Sits between the player/bot click and the unit's move activity. Single
entry point: `Move`/`AttackMove`/`Attack` order → rewritten into N
per-unit sub-orders.

Click classification:

1. Sample `CoverField` at click cell + within radius R cells.
2. Compute local **gradient** of the field at the click.
3. Resolve to an **intent**:
   - Strong gradient near click → "form line at cover edge facing out"
     (line perpendicular to gradient, anchored on high-density side).
   - Weak gradient, high local density → "take cover here, spread"
     (cluster on local maxima).
   - Click on garrisonable building → "enter as occupants".
   - Weak gradient, low density (open ground) → "default line at click"
     (line oriented along approach vector or threat estimate).
   - Click on unwalkable cell, water, or far-away enemy out of LOS →
     **literal fallback** to today's behavior.

### Layer 3 — Role-aware slot bidder

For each unit-type sub-order:

1. Build a candidate slot pool: top K cover cells in the zone, K ≈ 1.5×
   units in that sub-order.
2. Each slot carries a **profile**: front-arc, flank, overwatch, rear,
   interior.
3. Each unit carries a **preferred profile** (AT → front-arc with line
   of sight to vehicle approach; MG → wide-arc front; sniper →
   overwatch/depth; rifle → fill).
4. Greedy Hungarian-ish assignment matching units to slots, weighting by
   profile-fit + travel distance + min-spacing constraint.
5. **Over-supply** (more units than slots): build a second tier deeper
   into cover, with slots offset between first-tier slots (staggered).
6. **Spacing constraint** comes from `CohesionMode`:
   - Tight: min slot distance ~0.75 cell
   - Loose: ~1.5 cells
   - Spread: ~2.5 cells

Single-unit sub-orders **short-circuit the interpreter** entirely and go
literal. This covers the "place sniper at exact window" case without
needing a modifier key.

### Layer 4 — Cover-zone leash

When the interpreter resolves a formation, each unit receives a
**cover-zone reference** stored on its move activity. Leash semantics:

- **Lateral free**: a unit may freely move to any slot in its zone
  (e.g. AT soldier shuffling to where it's needed for an angle).
- **Forward gated**: stepping *out of* the zone is gated by a
  **forward-step budget** per engagement stance. Default stance: 1–2
  cells of slack for opportunistic fire, with snap-back. Ambush stance:
  larger budget. Hold stance: zero.
- **Blocked behavior**: if a unit can't reach its slot, re-slot to
  nearest unoccupied cover cell in the same zone. If the zone is full,
  spawn an additional tier behind.
- **Lifetime**: leash persists until the next non-trivial player order
  (move/attack-move/stop). Auto-engage, displacement, brief retreats do
  not clear it.

Per-stance forward-step budgets and leash strengths are deferred to
implementation tuning — captured here as variables, not values.

### Layer 5 — Waypoint chain (shift-click)

- Intermediate waypoints: **travel-only** (column path through, no
  formation). Final waypoint: formation resolution.
- v1 ships **naïve forward path** through intermediates. Backward
  rewriting (plan from final formation back, choose intermediate
  approach side to avoid awkward through-cover crossings) is deferred
  unless it bites in playtest.

### Layer 6 — Visualization

- **Hover preview**: faint ghost formation under cursor, throttled to
  4–8 Hz. Shows what would happen on click.
- **Group-level intent line**: thick line from group centroid to each
  player-click point. Thickness scales with group size.
- **Per-unit slot lines**: existing thin lines from each unit to its
  resolved slot.
- **Voice cue on commit**: "Taking cover" / "Forming line" / "Holding
  position" tied to resolved intent. Cheap, sells the system before
  the visualization is polished.

## Attack semantics

- **Attack-move on contact**: when enemies enter LOS during attack-move,
  units snap to a cover line in the nearest cover zone toward the
  threat. They engage from cover, not from the road.
- **Attack-click on enemy**: resolved as a move to a cover zone within
  weapon range *on the cover-best side*, then attack from cover. Charge
  behavior only if no usable cover is in range.

## v1 scope (in)

- LOS-only cover signal from `shadows.bin`
- Order-issuance interpreter for Move/AttackMove/Attack
- Role-aware slot bidder with min-spacing and staggered second tier
- Per-unit-type sub-orders for mixed selections
- Cover-zone leash with engagement-stance-tunable forward-step budget
- Garrison-aware buildings (enter if garrisonable)
- Hover preview + group-level thick line + voice cue
- Single-unit short-circuit (literal placement)
- Fixes to existing cohesion: `IResolveOrder` handler, `NotifyCohesionChanged`
  pattern, UI state sync

## v1 scope (out — deferred)

- Additional cover signals (walls, sandbags, ridges)
- Backward waypoint rewriting
- Literal-override modifier key (re-evaluate if players miss it)
- Strict stance toggle
- Ridge / LOS-conditional cover
- Per-stance leash UI (use sensible defaults baked in)
- Threat estimation for default-line orientation in open ground
  (start with: face the click vector from group centroid)

## Phased implementation

### Phase 1 — Wire the existing cohesion system (foundation)
- Add `IResolveOrder` handler for `SetCohesion` in `AutoTarget`
- Add `INotifyCohesionChanged` interface; wire `CohesionMoveModifier` to
  it
- Fix `PredictedCohesion` UI divergence in `CohesionSelectorLogic`
- Add YAML completeness check for `CohesionMoveModifier` in `world.yaml`
- Tests: unit test for the resolve/notify path
- **Outcome:** cohesion mode actually persists and propagates. No
  behavior change yet beyond what exists.

### Phase 2 — Cover-density field
- Add a per-map `CoverField` cache built from `shadows.bin`
- Modular signal API: `ICoverSignal { float Sample(cell, world) }`
- v1 ships with `LosCoverSignal` (weight 1.0)
- Invalidation hooks tied to existing terrain/building lifecycle
- Tests: hand-built map snippet → expected field values

### Phase 3 — Intent interpreter (replaces box offset)
- Replace `CohesionMoveModifier`'s box offset with the slot bidder
- Click classification (edge / interior / open / garrison / fallback)
- Role-aware bidding with min-spacing and second tier
- Per-unit-type sub-order routing
- Single-unit short-circuit

### Phase 4 — Cover-zone leash
- Cover-zone reference attached to move activities
- Lateral-free / forward-gated logic in AutoTarget
- Blocked → re-slot in zone
- Per-stance forward-step budget hookup (use defaults)

### Phase 5 — Visualization
- Hover preview rendering (throttled)
- Group-level thick line + per-unit thin lines
- Voice cue trigger on commit

### Phase 6 — Attack semantics
- Attack-move on-contact cover snap
- Attack-click cover-side approach

Each phase is independently shippable and provides visible value. Phase
1 is pure plumbing — should land first regardless.

## Open risks

- **LOS-as-cover is a coarse proxy.** A cell that blocks LOS isn't
  necessarily good infantry cover (e.g. open ground with a single tall
  obstacle). Mitigation: shipping Tight as the conservative default and
  watching playtest. The modular field design makes adding refinements
  cheap.
- **Slot bidding cost.** Hungarian-ish matching is O(N×K). With K ≈
  1.5N and typical N ≤ 12, this is fine. Watch for very large group
  orders.
- **Bot integration surprises.** Some bot modules issue moves through
  unusual paths (e.g. squad-fsm tactical retreats). Audit
  `BotModule` derivatives to ensure all moves flow through the same
  hook.
- **Voice cue performance.** If voice clips collide on every order,
  it'll be annoying. Need a per-group cooldown.
- **Hover preview throttling vs feel.** 4 Hz might feel laggy. Plan to
  try 8 Hz first, drop if it's expensive.

## Open questions for later

- Should the interpreter run on Stop orders too (e.g. "Stop" near cover
  → reposition into nearest cover)? Probably not in v1 — too magical,
  too easy to misread.
- Should garrison eviction (your building got blown up) re-resolve into
  the original cover zone or default literal?
- Should `Spread` cohesion mode use a different bidder (e.g. one slot
  per cover *patch*, spread across multiple patches) vs same bidder
  with wider spacing?

## Telemetry hooks worth adding

To tune the system post-launch, add per-event log lines via the
TELEMETRY recipe:

- `intent_resolved`: click cell, classification (edge/interior/open/
  garrison/fallback), gradient magnitude, unit count
- `slot_assigned`: unit id, slot profile, travel distance
- `leash_displacement_prevented`: unit id, zone id, attempted move
  vector
- `leash_step_out`: unit id, zone id, budget consumed, target

These let us replay playtest sessions and see exactly when the
interpreter fired, what it inferred, and how the leash behaved.

## Related work / cross-references

- `WORKSPACE/RELEASE_V1.md` line 35 — "Stance rework (4 phases)" — this
  plan completes part of that work.
- `WORKSPACE/archive/sessions/foundation_260511.md` line 292 —
  "Retreat-with-cohesion" (Phase 3 AI tactic) is downstream of Phase 1
  cohesion plumbing landing.
- `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/Hotkeys/GroupScatterHotkeyLogic.cs`
  (modified on current branch) — overlaps with the cohesion hotkey
  surface; coordinate UI buttons.
