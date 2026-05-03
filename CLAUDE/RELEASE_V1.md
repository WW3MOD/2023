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
- [T] **Garrison overhaul** (Phases 1–6) — indestructible buildings, dynamic ownership, directional targeting, suppression integration, visuals · *playtest 260503_1241*
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
- [~] **Buildings invisible / fog visibility model** — quick-fixed 260503 by short-circuiting `FrozenUnderFog.IsVisible` to `return true` (see TODO in `engine/OpenRA.Mods.Common/Traits/Modifiers/FrozenUnderFog.cs`). Symptom: buildings rendered invisible but still blocked sight; not clickable. Root cause appears to be the strict `IsVisibleInner` path landed in commit `2d7603bf` ("Fix buildings visible through fog") — `frozen.Visible` defaults to `true` and `state.IsVisible = !frozen.Visible` is `false` for all newly-spawned buildings, so they hide on first render before any sight pass. **Proper fix needed:** investigate `FrozenActor.Visible` initial state when shroud is off / map starts revealed; figure out whether buildings should ever go to fogged state at all in WW3MOD.
- [ ] **Visibility / fog design decisions for v1** — Open questions raised during garrison playtest:
  - Should buildings block line of sight at all? Old solution: only trees & static cover blocked sight. With buildings now indestructible (1 HP minimum), it might be fair for them to block — but hiding units behind a building is micro-intense and unintuitive, bad gameplay.
  - Should "fog" be a visibility *modifier* (weather-style, partial reduction) on top of shroud/sight, vs a binary "in fog or not"?
  - What lobby options ship with v1: just shroud/fog toggles, or richer fine-tuning (sight range modifiers, weather modes)?
  - **Decision needed before:** Phase A garrison playtest can fully complete; SR contestation depends on visibility working too.

---

## Phase B — Tier-1 Fixes

### Active bugs
- [ ] Artillery fires all ammo at once when critically damaged
- [ ] Aircraft can't spawn if waypoint is blocked
- [ ] **Ground unit production stuck at 100%** — vehicle queued behind infantry batch reached 100% but never spawned; cancel + rebuild worked. Probably same root cause as the aircraft-spawn-blocked bug. `ProductionFromMapEdge.cs:138–164` returns false silently when path/candidates fail; queue retries but can apparently get stuck. No diagnostic logging. *Reported 260503*
- [ ] **Bridge pathing — units walk off the bridge** — infantry (and possibly vehicles) move outside the bridge footprint into water/shore cells. Likely cause: locomotor permits the shore/water cells flanking the bridge, OR bridge sprite art is wider than its passable footprint. `engine/OpenRA.Mods.Common/Traits/Buildings/Bridge.cs` + locomotor terrain weights. *Reported 260503, screenshot in conversation*
- [ ] **Garrison: only first soldier of a batch enters** — when 2+ soldiers ordered to enter a building together, only the first completes; later soldiers approach, then go idle near the building. Strong hypothesis: building's `ChangeOwnerInPlace` on first entry (`GarrisonManager.cs:203`) triggers `World.Remove/Add + shroud recalc` (`Cargo.cs:466` comment confirms this is expensive) which invalidates the second soldier's Enter activity targeting the actor. Workaround: order each soldier individually after the first is in. *Reported 260503*
- [ ] **Stop order doesn't cancel garrisoned firing** — soldiers inside a building keep firing after `S` (stop) is pressed; the stop order isn't reaching the garrisoned soldier or the building's AttackGarrisoned activity. *Reported 260503*
- [ ] **Soldiers under fire abandon Enter-building order** — when ordered to enter a building while enemies are firing, most soldiers pause to return fire instead of completing the entry. Almost certainly SmartMove (`IWrapMove`) wrapping the move-to-building portion of the Enter activity — the move-portion fires SmartMove behavior, soldiers stop to engage. Needs SmartMove to skip wrapping when the inner move is part of an Enter/Garrison activity, OR Enter should bypass SmartMove. *Reported 260503*
- [ ] **Missiles disappear at expected impact instead of flying past** — helicopter→helicopter missiles fly to the side and silently vanish at the impact point with no explosion, when they should `FlyStraightIfMiss` past the target. Probably target-loss bug or `FlyStraightIfMiss` not gated correctly. `engine/OpenRA.Mods.Common/Projectiles/Missile.cs`. *Reported 260503*
- [x] **Lobby category headers showed as broken boxes** — fixed 260503 in `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyOptionsLogic.cs`. Per user, dashes dropped entirely — labels now render as plain `INFANTRY`, `VEHICLES`, `AIRCRAFT`.
- [x] **C4 destroyed indestructible buildings** — fixed 260503 in `engine/OpenRA.Mods.Common/Traits/Demolishable.cs`. Root cause: `Demolishable.Tick` called `self.Kill` directly, bypassing `IDamageModifier`. Replaced with `health.InflictDamage(self, attacker, new Damage(health.HP, ...), false)` so GarrisonManager's clamp leaves indestructible buildings at 1 HP (rubble / damaged sprite). Normal Demolishables still die from HP-worth of damage. Side effect: avoids one trigger of the expensive shadow recalc.
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

### Performance pass
- [ ] Pre-release perf pass (see "Pending decisions" → Performance pass for approach)

---

## Pending decisions

> Items raised during work that need a "yes / no / defer" call before they're scoped into v1 or sent to backlog.

- [x] **Buildings & line of sight in v1** — RESOLVED 260503: buildings no longer block line of sight. Trees and static decorations (rocks, ice, tank traps) still block. `Density: 100` removed from `^BasicBuilding` template, so no building/wall/defense actor contributes to `DensityLayer`. Side effects: (1) destroying a building no longer triggers `QueueShadowUpdate` because `Info.Density.Count == 0` short-circuits in `Building.AddedToWorld/RemovedFromWorld`, eliminating the runtime recalc lag the user reported with C4; (2) added a fallback in `Map.cs` so missing `shadows.bin` triggers `SetDensityLayer + SetShadowLayer` on load instead of leaving null layers; (3) deleted the two existing `shadows.bin` files (`river-zeta-ww3`, `woodland-warfare-ww3`) so they regenerate fresh on next load — first-load cost is one-time per map.
- [decision] **Fog richness in v1** — ship just shroud-on/off and fog-on/off lobby toggles, or invest in finer modes (weather fog, sight-range modifiers, per-faction sensor differences)? Probably v1 should be simple, richer modes go to v1.1.
- [decision] **Infantry self-defense baseline + AT soldier rebalance** — proposal from user 260503: every (or most) infantry should carry a basic firearm so they aren't helpless against other infantry. Specialists keep their specialist weapons but also have the firearm. Specifically: AT soldiers carry a rifle + 2 missiles (down from 3, to balance the firearm addition). Open questions before implementing: which specialists become hybrids vs which stay pure specialist? What's the damage/range gap between "real" riflemen and a hybrid's secondary firearm — is the hybrid's pistol/SMG meaningfully weaker so riflemen still have a role? Do engineers/medics also get a sidearm, or stay defenseless (gameplay risk-vs-realism tradeoff)? Does this change AI compositions?
- [decision] **Playtest session logging (developer mode)** — current logs are startup warnings only (`debug.log`, `perf.log`, `client.log` total ~10 KB after a session, none of it useful for following gameplay). Proposal: add a "Developer Logging" lobby/settings checkbox that opens a `gameplay.log` channel and instruments key events: player orders (build/move/attack/cancel), production state changes (queued/started/completed/failed/blocked with reason), unit lifecycle (spawn/death/capture), and a per-tick frame budget summary. Lets me read the file post-session and reconstruct what happened. **Decide:** ship in v1 (so I can keep using it through release), or build it, use it during dev, gate it as dev-only and not ship in v1?
- [decision] **Performance pass before v1** — options: A) you run VS profiler like before (thorough, slow), B) I add a tick-budget log channel (cheap, gives me data to read offline, less detail than profiler), C) `dotnet-trace` + PerfView snapshot during a heavy battle (free, gives flame graphs without VS), D) all three. Recommendation: B + C — B during normal play to spot regressions, C for the deep dive once. VS only if B/C miss something.
- [decision] **Garrison entry flow + visuals** — current behavior: soldier walks to *center* of building, plays prone animation on top of the roof, ~1s later pip appears at the bottom and sprite hides. User wants: (a) consider soldier "inside" once they reach the building footprint (not the center); (b) visual feedback on the transfer (building flash or similar); (c) the green chevron / protection % overlay redesigned — replace with vehicle-health-style pips, where building damage state ↔ protection level granted to occupants. Touches `EnterGarrison` activity, `GarrisonManager`, `WithGarrisonDecoration`. Needs design pass before implementing.
- [decision] **Targeting code review session** — user wrote custom advanced targeting that scores all candidates by type/distance/specialist priority (e.g. snipers prefer high-value), some AI-made changes since. Not broken, but worth a dedicated session to walk through scenarios end-to-end and possibly restructure. **Decide:** schedule a session in v1, or defer to v1.1 polish.
- [decision] **Shadow / visibility recalc cost vs. dynamic obstacles** — destroying buildings or trees triggers a full shadow/visibility recalc that causes noticeable lag (~1 sec). User raised: ideally damaged buildings (and trees) would have *reduced* density / partial visibility cost rather than binary block-or-not, but the recalc is too expensive to run mid-game. **Three branches:** A) drop "buildings/trees affect visibility" entirely (eliminates the perf concern AND the user's "hiding behind buildings is bad gameplay" concern in one move) · B) keep static-only (current static caching, accept that destroyed buildings keep blocking sight, document as known limit) · C) optimize the recalc (incremental, partial, deferred) so dynamic density becomes feasible. C is the most expensive engineering work. Decision intersects with the visibility-design pending decisions above. C4 indestructibility fix already removes one major recalc trigger.

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

- [x] **Shroud OFF by default** (260503) — `ExploredMapCheckboxEnabled: true` in `mods/ww3mod/rules/player.yaml`
