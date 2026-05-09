# Known Bugs

## Active

### Gameplay
- **Artillery burst fire on critical damage** -- Artillery fired all ammo at once when reaching critical damage state
- **Aircraft spawn blocking** -- Units unable to spawn if another unit occupies the waypoint
- **Helicopter husks on water** -- Should sink and disappear, currently remain visible
- **ATGM unload lock** -- Units can't unload while shooting ATGM; attack order locks until firing completes
- **Parallel queue builds paused units** -- Production continues for units that should be paused
- **Walking speed mismatch** -- Walk sequence doesn't match locomotor speed on different terrain layers
- **Units advance into minrange** -- Units move toward enemy when attacking while already within their minimum range
- **Aircraft returns to base prematurely** -- Returns when target dies even with remaining ammo
- **Helicopter rearm blocked** -- Won't rearm when told to "repair" if helipad is occupied
- **Mobile sensor** -- Doesn't work (CounterBatteryRadar)
- **Flying Fortress range** -- Hits short, possibly launch angle issue

### Visual
- **TECH/DR palette issue** -- Wrong palette for some animation (possibly death anim)
- **Projectiles disappear at north edge** -- Projectiles visually vanish outside northern map boundary
- **Russian scout helicopter husk on water** -- Visuals remain after death over water

### Engine
- **ShouldExplode blocking check** -- Runs every tick for some bullets; should check once on fire
- **Check blocking actors from weapon offset** -- Currently checks from center ground position, should use weapon offset

## Fixed (for reference)
- ~~Nuclear Winter crash~~ -- Null check added in EnterAlliedActorTargeter (FrozenActor.Actor null)
- ~~River Zeta crash~~ -- SeedsResource on maps without IResourceLayer disabled
- ~~TECN infiltrates cargo~~ -- CapturesNeutralBuildings removed from TECN
- ~~Bleedout animation missing~~ -- Now uses e1 sprite frames
- ~~Vehicle reverse sliding~~ -- Re-evaluates reverse at each cell transition
- ~~Supply Route defeat capture~~ -- Turns neutral instead of being killed
- ~~Helicopter double movement~~ -- Fixed physics formulas, velocity zeroing
- ~~Supply Route sell destination~~ -- Units now walk to map edge, not SR building

## Engine Notes
- `FrozenActor.Actor` can be null -- always null-check before accessing (especially after superweapons)
- `SeedsResource` on maps without `IResourceLayer` causes crashes -- disable or remove
- Armament.Name must match one of AttackBase.Armaments: `{ "primary", "secondary", "tertiary", "quaternary", "repair", "clearmines" }`
