# WW3MOD v1 Release Tracker

> Single source of truth for v1 scope. Update continuously as items are tested, fixed, deferred, or cut.
>
> **Status legend:** `[ ]` open · `[~]` in-progress · `[T]` testing · `[x]` done · `[v1.1]` deferred · `[cut]` won't-fix v1
>
> **Scope is locked.** New features need explicit "yes, add to v1" from the user. Otherwise → `BACKLOG.md` or `Pending decisions` below.

## Phase

**Currently in: Phase A — Stabilize**

- **Phase A — Stabilize** — get every "needs playtesting" system verified or fixed. No new features.
- **Phase B — Tier-1 fixes** — bugs and gameplay gaps that block release.
- **Phase C — Polish** — sounds, icons, descriptions, open polish threads.

---

## Phase A — Stabilize

Systems built but not verified end-to-end. Each needs a focused playtest pass to confirm working / surface bugs.

### Big systems needing playtest
- [ ] **Garrison overhaul** (Phases 1–6) — indestructible buildings, dynamic ownership, directional targeting, suppression integration, visuals
- [ ] **Cargo system** (Phases 2A–E) — TRUK auto-rearm, mark+unload, rally points, supply drop, merge
- [ ] **Helicopter crash + crew overhaul** — critical=total loss, safe land=neutral+repairable, capture-by-pilot-entry
- [ ] **Stance rework** (4 phases) — modifiers (Click/Ctrl/Ctrl+Alt/Alt), resupply behavior, cohesion, patrol
- [ ] **AI overhaul** (Tiers 0–3.1) — bot modules, multi-axis attacks
- [ ] **Supply Route contestation** — graduated control bar, production slowdown, notifications
- [ ] **Three-mode move system** — Move/Attack-Move/Force-Move, SmartMove wrapping
- [ ] **Vehicle crew system** — slot ejection, re-entry, commander substitution
- [ ] **Infantry mid-cell redirect** — tune `RedirectSpeedPenalty` (currently 50%)

### Known design issues
- [ ] **Tank frontal armor stalemate** — sim shows pen 50 vs 700 thickness = 7% dmg per hit. Either rework armor model or rebalance pen values.

---

## Phase B — Tier-1 Fixes

### Active bugs
- [ ] Artillery fires all ammo at once when critically damaged
- [ ] Aircraft can't spawn if waypoint is blocked
- [ ] Helicopter husks on water don't sink
- [ ] ATGM units can't unload while shooting (attack lock)
- [ ] Parallel queues build paused units
- [ ] Walking sequence speed mismatches locomotor on different terrains
- [ ] Aircraft returns to base prematurely (with ammo remaining)
- [ ] Helicopter rearm blocked when helipad occupied
- [ ] Mobile sensor (CounterBatteryRadar) doesn't work
- [ ] Flying Fortress range hits short
- [ ] Units advance into minrange when attacking
- [ ] TECH/DR palette issue
- [ ] Projectiles disappear at north edge
- [ ] Russian scout helicopter husk on water (visuals remain)
- [ ] River Zeta: neutral SAM capturable, broken capturable building

### Drone fixes
- [ ] DR animations — prepare runs idle, drone launches before prep finishes
- [ ] Drone autotarget of other drones broken
- [ ] Anti-drone weapon too effective — freeze mid-air, fall when battery dies?
- [ ] Drone death needs crash animation

### Aircraft polish
- [ ] Edge spawn/leave for planes
- [ ] Helicopter landing refinement (slow before landing, faster turn to avoid overshoot)
- [ ] Apache shouldn't shoot guns at structures
- [ ] Ballistic missile tilt fix — Iskander/HIMARS missiles don't pitch properly on arc

### Combat / suppression / bypass
- [ ] Suppression tuning — playtest vehicle values, per-weapon fine-tuning
- [ ] Bypass system refinement (ATGM tree handling, range-based hit chance)
- [ ] Flametrooper effective vs unarmored
- [ ] Units out of ammo reject attack orders (don't freeze aiming)
- [ ] Shoot at last known location for stationary targets
- [ ] WGM should not fire if it won't hit
- [ ] Ballistics deprioritize targets if hit chance too low

### Supply Route
- [ ] Captured SR handling — what spawns link, neutral SRs between players
- [ ] Primary SR selection UI

### AI
- [ ] AI builds Logistics Centers, rearms
- [ ] AI conscripts don't abandon capture for squad orders
- [ ] AI stops firing at buildings marked for capture
- [ ] AI garrisons defense buildings
- [ ] AI uses attack-move for aircraft

### Misc gameplay
- [ ] Disable Tesla Trooper & futuristic units (small task)
- [ ] Helicopter force-land tuning + crew bloat fix + crew vehicle re-entry testing

---

## Phase C — Polish

### Sounds (the big gap)
- [ ] Unit firing sounds
- [ ] Explosion sounds
- [ ] Unit voice responses

### Visuals
- [ ] Unit icons
- [ ] Per-unit rot/bleedout sprites (currently uses generic e1)
- [ ] Unit description box sizing

### Open development threads
- [ ] **Garrison Phase 4** — sidebar icon panel rewrite
- [ ] **Cargo Phase 3** — template sidebar for pre-loaded transport purchasing

---

## Pending decisions

> Items raised during work that need a "yes / no / defer" call before they're scoped into v1 or sent to backlog.

(none yet)

---

## Deferred to v1.1 / Won't fix v1

- [v1.1] Per-Supply-Route production queues (needs engine changes)
- [v1.1] Ukraine as third faction
- [v1.1] Ammo costs money (full economy rework)
- [v1.1] Tier 2 hotkey overhaul (Alt/Ctrl modifier polish)
- [v1.1] Lobby option dropdowns (army upkeep, kill bounties, short-game threshold)
- [v1.1] Map editor improvements (more civilian structures, road tiles)
- [v1.1] Engine upgrade to release-20250330 (12–22 sessions)
- [v1.1] River Zeta shellmap overhaul
- [v1.1] Unit description overhaul & auto-generated stats
- [v1.1] Rename tech levels to "DEFCON"
- [v1.1] Move widgets to edges, free up UI space

---

## Recently completed

> Items move here as they ship. Keep the most recent ~10; older entries fall off.

(empty — populated as Phase A items resolve)
