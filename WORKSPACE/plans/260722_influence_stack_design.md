# Influence stack — full-map commander's-view layers (design + staging)

**Status:** ratified direction from the user (2026-07-22, live design discussion). This document is the durable record of that direction and the staged implementation plan derived from it.
**Supersedes:** the Phase-4 "value channel" gating question in the split SPEC (§3c) and the thin-patch option in `260722_phase4_recon.md` — the answer is: build the full influence stack, not a weighting patch on the Phase-1 layer.
**Relation to Phase 4:** role-model consumption (the recon's "safe half") is unchanged and still goes first. The fog-migration half of Phase 4 is absorbed into this stack (Stages A/B/F below). Repoint-don't-rebuild for the shared omniscient `ThreatMapManager` still holds: control bots keep the old grids untouched; @experimental (and the human overlay) move onto the new stack.

---

## 1. The vision (user direction, faithfully recorded)

1. **Full-map coverage.** The layer is not stamps around units — essentially every cell has a color at all times. At game start, all cells are divided by proximity: each cell belongs to whoever is closest (Voronoi seed from the Supply Routes / starting positions). It then adjusts continuously as units move, fight, and capture ground.
2. **Commander's-view belief semantics — the layer shows what a commander would believe, not ground truth:**
   - A spotted enemy unit whose visual is lost is **assumed to still be there**.
   - If it is **seen driving away**, the vacated area is clearly safe.
   - An area **verified free of enemies** (currently observed, empty) becomes **grayzone immediately**.
   - Under fog, the estimate updates by whatever rule makes the layer most *useful* to bot decisions and tactical behavior — usefulness over epistemic purity.
3. **Threat-weighted auras, two independent parameters per unit:**
   - *Radius* ≈ how far the unit can project danger (weapon range foremost).
   - *Density/intensity* ≈ how dangerous it is inside that radius (damage output, durability, cost class).
   - Examples given: a tank has a stronger aura than a rifleman, and far stronger than a supply truck (~zero). A sniper may have a *bigger* aura than a humvee, but the humvee's is *smaller and denser*.
4. **A dedicated air-danger layer.** Helicopters/aircraft need their own safe/unsafe map: scout an area, see there is no anti-air, and keep helicopters inside the safe zone when attacking. Safe zones for both ground and air derive from **max range of enemy weapons that can hit that domain**.
   - Motivating defect (observed live): bots sacrifice helicopters straight into enemy territory — not even attack-move; they fly *over* enemies shooting opportunistically on the move instead of stopping at missile range. "This looks really dumb."
5. **Danger-gradient rear routing.** Units relocating along the front should not travel point-to-point through the danger zone: pull *back* from the frontline, travel *perpendicular/lateral* at a safer depth, then re-enter where needed. Extra important for non-combatants and high-value units (supply trucks, attack helicopters).
6. **Assumed threat projection through fog.** Wherever the enemy is believed to control ground, project their weapon envelopes outward — e.g. enemy artillery reaching ~40 cells (illustrative) makes everything within 40 cells of believed-enemy territory *slightly* dangerous, even with no current visual, because a drone/spotter can arrive at any time.
7. **Verdict on the naive model:** plain "who is closest" ownership will not suffice long-term if the bots are to be genuinely good.

---

## 2. Architecture — four components

The stack is per-player (fog-respecting by construction) and @experimental + human-overlay only. Control bots (Normal/Rush/Turtle) and @stable never read it; the benchmark byte-identity invariant is preserved by never touching their code paths.

### A. Belief store — per-player contact memory
The substrate everything else reads. A per-player table of **believed enemy contacts**:
- On sighting an enemy unit: record/update contact (position, type, timestamp, confidence=1).
- On losing visual: contact **persists at last-seen position** with decaying confidence.
- On observing the unit leave / die: contact moves or is removed.
- On observing a cell that a contact occupies and finding it empty: contact cleared (**verified-clear ⇒ gray immediately**).
- Static defenses / garrisoned structures: no decay — persist until verified gone (engine `FrozenActorLayer` already does this for structures; the belief store generalizes it to units).
- Mobile contacts: confidence decays faster; optionally position uncertainty grows (v1: fixed per-class decay half-life, no blur).
- Build on what Phase 1 already reads (`Shroud` + `FrozenActorLayer`, staggered recompute pattern from `SightingThreatLayer`).

### B. Danger fields — per-domain threat projection
Computed from the belief store + territory baseline, **two channels**:
- **Anti-ground** and **anti-air** — a contact contributes to a channel only if it has a weapon whose `ValidTargets` can hit that domain. PITFALL: WW3MOD discriminates `Air` vs `Helicopter` target types (only MANPADS/Stinger/9M311 list `Air`; ground MGs list `Helicopter`) — the air channel keys off "can hit Helicopter" for v1, with fixed-wing as a later third channel if needed.
- **Kernel per contact:** radius ← max weapon range vs that domain (+ small buffer; optional mobility term later), intensity ← (damage throughput × durability/cost class) × confidence. This yields exactly the sniper-vs-humvee shape: range sets width, lethality sets density.
- **Baseline territory threat:** believed-enemy-controlled cells (component C) project a generic low-intensity danger out to the longest *plausible* enemy weapon envelope (the artillery reach), so "40 cells from anywhere they hold" reads as slightly dangerous even with zero contacts — the user's drone-could-arrive clause.
- v1 kernels are radial. Terrain-aware (flow-around impassable ground, so a river genuinely splits the front) is a declared v2 upgrade — costlier, decided when the radial version's readability is judged in-game.

### C. Control field — full-map ownership
- **Seed:** Voronoi by proximity to each player's Supply Route / start at match start — every cell owned from tick 0.
- **Persistence with capture semantics:** presence (units surviving in an area, sites captured) paints ownership; ownership *lingers* when units leave (no flicker), erodes under enemy presence, and flips when the enemy demonstrably holds it.
- **Grayzone:** verified-clear cells (observed, no enemy) between the fronts read as gray/contested — immediately on verification, per the commander's-view rule.
- **Site anchors:** Supply Routes, derricks, captured POIs project fixed ownership auras so territory anchors to ground, not just to roaming armies.
- Rendering: color = owner, brightness/alpha = margin (how firmly held).

### D. Overlay — hold-Space commander view (extends Phase-1 overlay)
- Default mode: control field wash (green/red/gray) + danger-field brightness.
- Air mode (toggle or modifier): the anti-air channel isolated — where helicopters may operate. This is also the debugging window into Stage-D behavior.
- Dev always-on switch retained from Phase 1.

---

## 3. Consumers (the point of the whole stack)

1. **Helicopter doctrine — flagship.** (a) *Standoff micro*: attack helicopters stop and fire at max missile range instead of overflying targets — this half is engagement logic, **independent of the layers**, and ships first as Stage 0. (b) *Layer-driven safety*: route around anti-air danger cells, leash attacks to the AA-safe envelope, withdraw when the local air-danger reading spikes (new AA sighted).
2. **High-value / non-combatant routing.** Danger field as a pathfinding cost modifier for supply trucks, evacuating units, artillery repositioning, helicopters in transit — the rear-lateral-re-enter pattern *emerges* from cost-weighted routing rather than being scripted.
3. **Strategic repoint.** Attack-axis selection, expansion, and the revived territorial balance-of-power bias (parked `exp-terr-bias` @ ccd12c98 — its batch showed the per-POI factor was a near-pure damper; the control field is the substrate it actually needed) read the control + danger fields instead of the omniscient grids. Completes the fog migration for @experimental.

---

## 4. Stages

Each stage: @experimental-only (+ overlay), @stable and controls byte-identical, NUnit pins where the logic is table-like, autotest per AUTOTEST recipe, benchmark gate before merge. Worktrees under `C:\Users\fredr\worktrees\ww3mod\`.

| Stage | Content | Depends on | Verify |
|---|---|---|---|
| **0** | Heli standoff micro: stop-and-fire at missile range, no overflight; attack-move semantics for heli squads | nothing (pure engagement logic) | autotest: heli vs target line, assert standoff distance + no overflight; live-play look |
| **A** | Belief store (contact memory, decay, verified-clear) | Phase-1 sighting substrate | NUnit: contact lifecycle table (sight/lose/leave/verify) |
| **B** | Danger fields, ground + air channels, kernels from armament data + territory baseline | A (+ C for baseline; circular link resolved: B v1 uses contacts only, baseline lands with C) | NUnit: kernel table (sniper/humvee/tank/truck shapes); overlay eyeball |
| **C** | Control field (Voronoi seed, persistence, grayzone, site anchors) + overlay v2 (control wash, air mode) | A | autotest: seed partition + capture-flip scenario; screenshot recipe for overlay |
| **D** | Heli layer consumer: AA-avoidance routing, safe-zone leash, withdraw-on-spike | 0, B | autotest: heli refuses AA-covered approach, takes safe corridor; the "no more suicides" check |
| **E** | Danger-weighted routing for high-value/non-combatant units | B | autotest: truck relocates via rear-lateral path, not through the front |
| **F** | Strategic repoint: attack axes + terr-bias revival on control/danger fields; @experimental fully off omniscient grids | B, C | full ladder re-baseline (declared instrument change) |

**Ordering rationale:** Stage 0 is a user-visible quick win with zero coupling. A→B→C build the data spine. D delivers the flagship behavioral payoff early (helis stop dying stupidly). E is cheap once B exists. F is the big-bang strategic change and carries the declared re-baseline — last, so everything under it is stable.

**Phase-4 interleave:** role-model consumption (recon §role) runs before/parallel to Stage A — it is independent, already specced, and cures the artillery/SHORAD-on-the-line defect. The recon's fog-migration plan is superseded by Stages A/B/F.

## 5. Defaults chosen (overridable — flagged as assumptions)

- **Persistence over live-field:** ownership lingers with decay; verified-clear grays immediately. Decay half-lives: mobile contacts minutes-scale, static defenses none. Exact constants tuned in Stage A/C autotests.
- **Radial kernels v1**, terrain-aware flow v2.
- **Consumer order:** overlay + heli first, strategic repoint last (biggest risk, needs re-baseline).
- **Air channel = "can hit Helicopter"** v1; fixed-wing channel deferred.
- **Illustrative numbers** (arty ~40 cells) to be read from actual armament YAML at implementation time, never hard-coded.

## 6. Performance guardrails

Coarse cell grid (existing influence-map granularity), staggered per-player recompute (Phase-1 pattern), event-driven contact updates (sighting changes, deaths), budgeted full-field refresh (every N ticks, amortized). Two channels × per-player is the cost ceiling; if it bites, drop control-field precision before danger-field precision — danger drives behavior, control drives rendering + strategy.
