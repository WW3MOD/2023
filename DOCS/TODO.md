# WW3MOD v1 Release TODO

Prioritized task list for reaching a v1 release. Items marked [DONE] have been completed.

---

## Tier 1: Must-Have for v1

These are blockers or core gameplay features that define WW3MOD's identity.

### Suppression & Combat
- [ ] Suppression tuning -- playtest and balance vehicle suppression values, per-weapon fine-tuning
- [ ] Bypass system refinement
  - ATGMs hit trees too much; make immune but require clear LoS (no trees within 1-2 tiles)
  - Tanks should lose some damage per tree passed, not be fully blocked
  - Check distance: close to shooter = higher bypass chance, close to target = higher hit chance
  - WGM should not fire unless it's going to hit
  - Ballistics deprioritize targets if hit chance is too low
- [ ] Flametrooper should be more effective vs unarmored (like Grad)
- [ ] Units out of ammo should reject attack orders (not freeze while aiming)
- [ ] Shoot at last known location if losing track of stationary target

### Units & Balance
- [ ] DR (Drone) fixes
  - Animations wrong: prepare drone runs periodically when idle; drone launches before prep animation finishes
  - Autotarget of other drones not working
  - Anti-drone weapon too effective -- freeze mid-air, falls when battery dies?
  - Drone death: just disappears, needs crash animation
  - EMP drift behavior (kinda cool, keep but refine?)
- [ ] Aircraft edge spawn/leave -- planes should enter and exit at map edges
- [ ] Helicopter landing refinement -- slow down before landing, increase turn speed to avoid overshoot/circling
- [ ] Apache shouldn't use guns vs structures
- [ ] Aircraft returns to base when target is killed even with ammo remaining -- should continue mission
- [ ] Helicopters don't rearm when told to "repair" if helipad occupied

### Supply Route
- [ ] Per-Supply-Route production queues (each SR has independent build queue) -- requires engine changes
- [ ] Captured SR handling -- determine linked spawns, neutral SRs between players
- [ ] Primary SR selection UI

### Sounds & Visuals
- [ ] Unit firing sounds (major gap)
- [ ] Explosion sounds
- [ ] Unit voice responses
- [ ] Per-unit rot/bleedout sprites (currently uses generic e1 frames)
- [ ] Unit description box sizing
- [ ] Unit icons

### AI
- [ ] AI needs to build Logistics Centers and rearm
- [ ] AI conscripts abandon capture to follow squad orders -- decouple during capture
- [ ] AI should stop firing at buildings marked for capture
- [ ] AI should garrison defense buildings
- [ ] AI should use attack-move for aircraft attacks

### Bugs (Active)
- [ ] Artillery fired all ammo at once when critically damaged
- [ ] Air bug: units unable to spawn if another unit blocks waypoint
- [ ] Helicopter husks on water should sink and disappear
- [ ] Walking sequence speed doesn't match locomotor speed on different terrain layers
- [ ] TECN -- check if infiltration vs cargo issue is fully resolved
- [ ] Parallel queues keep building paused units
- [ ] ATGM units can't unload while shooting (attack order locks until complete)

### Bugs (Fixed)
- [x] Nuclear Winter crash -- null check in EnterAlliedActorTargeter
- [x] River Zeta crash -- disabled SeedsResource on mine actors
- [x] TECN infiltrates cargo instead of entering neutral structures
- [x] Bleedout animation -- now uses e1 sprite frames
- [x] Vehicle reverse sliding
- [x] Supply Route defeat behavior -- turns neutral instead of killed
- [x] Helicopter movement precision -- physics formulas corrected
- [x] Rotate/sell to map edge -- units walk to map edge, not SR building

---

## Tier 2: Important for v1 Polish

These make the game feel complete but aren't core identity features.

### Gameplay
- [ ] Default move order: stop and return fire periodically (unless force move)
- [ ] Engineer repairs (bridge repair delay, general repair functionality)
- [ ] Multiple soldiers capturing = faster capture; some buildings require multiple capturers
- [ ] Infantry should deploy immediately, not wait until next position
- [ ] Enter civilian buildings: units inside take damage when building hit; firing unit shows as pip for enemy
- [ ] Civilian buildings trigger "base under attack" notification
- [ ] Vehicles disabled at 50% HP (add recovery/tow vehicle?)

### Hotkeys & Controls
- [ ] Alt = attack-move mode with cursor change
- [ ] Ctrl = force-move
- [ ] Ctrl+Alt = force attack
- [ ] Shift = show small dot on closest targetable tile
- [ ] Group Scatter polish -- test with mixed types, UI feedback (target lines)

### Options & Lobby
- [ ] Load previous game settings, quick-load button
- [ ] Short game: dropdown for % army value loss threshold
- [ ] Kill bounties as dropdown
- [ ] Army upkeep as dropdown
- [ ] Fix tech levels
- [ ] Rename tech levels to something like "DEFCON"

### Maps
- [ ] Map editor: more civilian structures, more road tiles
- [ ] Update/create new maps (need 6-8 solid competitive maps)
- [ ] Generate shadows on first run (checkbox to deactivate shadow caster)
- [ ] Tree sprites off-centre for shadows -- fix alignment

### UI
- [ ] Move widgets to edges, remove borders, free up space
- [ ] Production queue hotkeys: Ctrl=priority, Ctrl+Shift=build 5, Alt=infinite, Alt+Shift=cancel all
- [ ] Smoke tick duration too long -- reduce, but make more SHPs

---

## Tier 3: Post-v1 / Future Content

Great ideas that should wait until core game is solid.

### Factions
- [ ] Ukraine as third faction

### Economy & Logistics
- [ ] Ammo costs money; trucks must deliver to Logistics Centers
- [ ] Dynamic income based on structure health
- [ ] Nuclear powerplant (releases radiation if destroyed)
- [ ] Production shortage mechanic (building same unit increases price, decays over time)
- [ ] Build loaded vehicles (template system)

### Advanced AI
- [ ] Unit Builder Module -- analyze enemy composition, build counters based on capabilities
- [ ] Strategic Module -- designate targets, areas of interest, oil, defense/attack zones
- [ ] Tactical Module -- coordinate units per point of interest, maintain formations
- [ ] Operational Module -- small squads as one, individual high-value target hunters
- [ ] AutoAssign Actor method (usable by AI and player as "auto-assign on idle" stance)

### Stances & Controls
- [ ] Cohesion bar: implement Tight/Loose/Spread spacing behavior (UI done as dummy)
- [ ] Resupply behavior bar: implement Hold/Seek/Rotate ammo-out behaviors (UI done as dummy)
- [ ] Click-modifier meta-system: Click=immediate, Ctrl+Click=per-unit default, Alt+Click=per-type default (persisted)
- [ ] Veterancy persistence on rotate: when a veteran unit rotates out via Supply Route, the next purchased unit of that type inherits the veterancy rank. Applies to infantry, vehicle crews, and aircraft pilots
- [ ] Authorized Weapons toggles (primary/secondary/mg/tow per button)
- [ ] Formation presets: Line / Box / Column
- [ ] Formation structure: Infantry front / Armor front / Current
- [ ] Ammo preservation modes: Max firepower / Preserve / Make every shot count
- [ ] Auto ammo mode (reduce consumption when low, factor resupply distance)
- [ ] Sweeping fire (shoot at multiple enemies in one burst for max suppression)
- [ ] Don't overkill (MLRS sweeps if only one target)
- [ ] Retarget to more valid target dynamically
- [ ] Fire at furthest/closest toggle (avoid front-line overkill)

### Movement & Formations
- [ ] Formation move (maintain relative spacing)
- [ ] "Stay in formation" + pre-defined formations
- [ ] Control group hierarchy with leader units (Fire Team 4, Squad 12, Platoon 30, Company 100, Battalion 500)
- [ ] Team Leaders rank up to Sergeant, Lieutenant, etc. with stacking command bonuses
- [ ] Leapfrog advance (alternating cover/move between two groups)

### Advanced Mechanics
- [ ] Binoculars / Laser designator for team leaders
  - Force-attack ground = reveal area
  - Force-attack vehicle = laser mark (extends missile range, enables tracking for unguided)
- [ ] Morale system: enemy proximity adds condition, friendlies subtract; affects behavior
- [ ] Experienced recruits: building slowly = higher starting rank (up to level 2)
- [ ] Surrender mechanic: surrounded + low XP units may surrender; defect if out of money + high upkeep
- [ ] Helicopters at critical damage: slow descent, pilot evacuates, helicopter burns until dead
- [ ] Indirect fire gains accuracy for each shot while target is stationary and impact site is visible
- [ ] Inaccuracy scales logarithmically with distance
- [ ] Pontoon bridges
- [ ] Dogs: can't move, only guard + wander if not guarding
- [ ] Don't reveal shroud behind blocking actors

### End Game
- [ ] Flag capture starts countdown to victory (prevents hidden-unit stalemates)
- [ ] "Quittable" mode: after X minutes any player can quit, score counted
- [ ] Nuclear Armageddon mode: time-limited, ends with nuke waves; "Nuclear Escalation" power
- [ ] Entrance Force: pre-game build phase, units restricted to small circle until timer ends
- [ ] Set "tech increase" timers when higher tech unlocks

### Naval
- [ ] Scale up naval units
- [ ] Cruise missiles (can be shot down)
- [ ] New landing craft model

### Support Powers
- [ ] Nuke flash palette effect
- [ ] Aircraft support powers arrive from own side of map (realistic, predictable)
- [ ] B2 Bomber support power
- [ ] Missile strike power

### Vehicles
- [ ] Tesla tank returns to base when out of ammo
- [ ] Kamikaze drones
- [ ] GDRN tracked drone vehicle
- [ ] MCV as construction/transport vehicle (load at ConYard, deploy at location)
- [ ] Recovery/tow vehicle for disabled vehicles

### Misc
- [ ] Camouflage colors instead of silly faction colors (select from list, possibly two-color)
- [ ] Allow infantry subcell stopping (fire faster from intermediate positions)
- [ ] Pathfinder: ignore allied unit blockage, allow overlap with repulsion
- [ ] Multi-threading exploration (LogicTick / RenderTick on separate threads?)
- [ ] ShouldExplode optimization (check once on fire, not every tick)
- [ ] Rename "Build tree" to "Reservists", buildtime to "callup time"
- [ ] Shellmaps: "Bradley Square", "Piatykhatky Ukrainian offensive breakthrough"
- [ ] Resupply hotkey (R), leave battlefield (double-click), leave even if not low (triple-click)
- [ ] Add scores.mix
