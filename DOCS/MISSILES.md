# WW3MOD Missile System — Analysis & Improvement Plan

## How Missiles Work (Engine)

All missiles use `Projectile: Missile` (Missile.cs). The engine models a physically simulated projectile with:

### Launch & Homing
1. Missile spawns at the weapon muzzle, aimed at the target
2. An **inaccuracy offset** is computed at launch — a random displacement from the target center
3. Missile enters **Homing** state, steering toward `targetPosition + offset`
4. Every `RetargetTicks` (default 5), the offset is **re-rolled** — the missile's aim point jitters throughout flight
5. **Lead targeting**: missile predicts target velocity and aims ahead (`WVec.CalculateLeadTarget`)
6. Near the target (within 3 loop radii), state switches to **Hitting** — turn rate gets boosted up to 3x

### Hit Detection
A missile "hits" when **any** of these are true:
- Distance to target < `CloseEnough` (default: 298 WDist = ~0.29 cells)
- Missile hits the ground (height < 0)
- Missile runs out of fuel (distanceCovered > RangeLimit)
- Airburst: altitude < AirburstAltitude AND horizontal distance < CloseEnough

**Critical**: The hit check is `relTarDist < CloseEnough` — this is distance from the missile to the **offset target point**, NOT the actual unit center. So a missile must pass within CloseEnough of its jittered aim point, not the unit itself. The warhead's `Spread` then determines if the explosion actually damages nearby actors.

### Miss Behavior
`FlyStraightIfMiss: true` (default for all our missiles): If the missile's distance starts increasing after it was close, it stops homing and flies straight. It will NOT loop around for another pass. Most misses are permanent.

### InaccuracyType
- **Absolute** (default for missiles): Inaccuracy is a fixed radius regardless of range. A missile at 5 cells and 25 cells has the same scatter radius
- **Maximum**: Scales linearly from 0 at point-blank to full value at max range
- **PerCellIncrement**: Scales per cell of distance

### ManualGuidance
`ManualGuidance: true` (used by WGM): If the firing unit dies, the missile loses guidance and enters freefall. This simulates wire-guided missiles that need an operator.

### LockOnProbability
Default 100% — all our missiles always lock on. Could be used for flares/countermeasures later.

---

## Current Missile Inventory

### Anti-Tank Missiles (Ground-to-Ground)

| Weapon | Used By | Speed | Inaccuracy | Type | HorizTurn | CloseEnough | TopAttack | Damage | Penetration |
|--------|---------|-------|------------|------|-----------|-------------|-----------|--------|-------------|
| ATGM | AT Specialist (infantry) | 300 | 512 | Absolute | 20 | 298 (default) | Yes | 10000 | 100 |
| WGM | BMP-2 | 300 | 768 | Absolute | 10 | 298 (default) | No | 10000 | 800 |
| WGM.bradley | Bradley | 300 | 768 | Absolute | 10 | 298 (default) | No | 10000 | 800 |
| Hellfire | Apache, Havoc, MI28, Littlebird | 500 | 512 | Absolute | 30 | 298 (default) | No | 10000 | 800 |
| Hellfire.strykershorad | Stryker SHORAD | 400 | 512 | Absolute | 30 | 298 (default) | No | 10000 | 800 |

### Anti-Air Missiles

| Weapon | Used By | Speed | Inaccuracy | Type | HorizTurn | CloseEnough | Damage |
|--------|---------|-------|------------|------|-----------|-------------|--------|
| AirToAirMissile | F-16, SU-27 (fighter jets) | 800 | 400 | Absolute | 25 | 400 | 1000 + rand(1000) |
| SurfaceToAirMissile | SAM sites | 800 | 400 | Absolute | 35 | 400 | 2000 + rand(1000) |
| SurfaceToAirMissile.double | Tunguska, etc. | 800 | 400 | Absolute | 35 | 400 | 2000 + rand(1000) |
| MANPAD | Stinger infantry | 450 | 256 | Absolute | 20 | 192 | 3000 |
| Stinger | Stinger vehicle | 600 | 300 | Absolute | 20 | 256 | 5000 |
| 9M311 | Tunguska-like | 600 | 300 | Absolute | 20 | 256 | 5000 |

### Multi-Role (Ground + Air)

| Weapon | Targets | Speed | Inaccuracy | HorizTurn | CloseEnough | Notes |
|--------|---------|-------|------------|-----------|-------------|-------|
| Hellfire | Vehicle, Air, Defense | 500 | 512 | 30 | 298 | Used by helicopters vs ground AND air |
| TimerWolf_Missiles | Vehicle, Air, Defense | 850 | 1024 | 5 | 298 | Very fast, very low turn rate, 4-burst |

---

## Target HitShapes (Why Missiles Miss)

This is the core problem. The hit check (`relTarDist < CloseEnough`) compares distance to the jittered offset point. But the warhead `Spread` determines actual damage radius. The **HitShape** determines whether a warhead explosion at a given point can "reach" the unit.

### Current HitShape Sizes

| Target Type | HitShape | Effective Size | Notes |
|-------------|----------|----------------|-------|
| **Helicopters** | Circle, Radius: **32** | ~0.03 cells | **EXTREMELY tiny** — intentional workaround to avoid bullet shredding |
| **Drones** | Circle, Radius: **1** | ~0.001 cells | Effectively unhittable by area damage |
| **Fixed-wing** | Circle, Radius: **32** | ~0.03 cells | Same as helicopters |
| **Infantry (standing)** | Circle, Radius: **30** | ~0.03 cells | Comparable to aircraft |
| **Infantry (prone)** | Circle, Radius: **20** | ~0.02 cells | Even smaller |
| **Vehicles** | Rectangle | ~0.6-1.3 cells wide, ~1.0-1.9 cells long | Properly sized |

### The Helicopter Problem Explained

Helicopters have a Circle HitShape with **Radius: 32** (0.03 cells). This was a deliberate workaround: without it, autocannons and machine guns were shredding helicopters instantly because each bullet in a burst would individually check against the hitshape.

But this creates a catastrophic side effect for missiles:
1. Missile aims at target center + random offset (up to 512 WDist for Hellfire)
2. Missile must pass within CloseEnough (298 WDist) of that offset point to detonate
3. Even if it detonates, the warhead Spread (64-128 WDist) must overlap the HitShape (32 WDist radius)
4. Against a moving helicopter at cruise altitude, the offset can easily put the detonation point 300-500 WDist from the actual unit center
5. With a 32 WDist hitshape, the explosion needs to land within ~96 WDist (Spread 64 + Radius 32) of center to deal damage
6. **Result**: missiles detonate "near" the helicopter but the explosion doesn't reach the tiny hitbox

For comparison, a vehicle HitShape rectangle is typically 600-700 WDist wide — about **20x larger** than a helicopter's. Missiles have a much bigger target to "splash into."

---

## Problem #1: Helicopter-vs-Helicopter Missiles

**Symptoms**: Attack helicopters fire Hellfire missiles at each other but rarely score hits. Multiple missiles wasted.

**Root causes**:
1. **Tiny HitShape (32)** — Helicopter hitbox is microscopic. Even a close detonation misses
2. **Absolute Inaccuracy (512)** — Hellfire has 512 WDist (~0.5 cell) random offset, same at any range. Against a 32-radius target, this is a ~6% geometric hit probability per detonation
3. **Target movement** — Helicopters move at Speed 195-245. Lead targeting helps but the offset re-rolls every 5 ticks, jerking the aim point around
4. **CloseEnough (298)** — Default value. Missile detonates 298 WDist from the offset point, which could be 800+ WDist from actual target center
5. **Warhead Spread (64-128)** — The explosion radius is tiny relative to the scatter

**Key insight**: The problem isn't that missiles fly past helicopters — it's that they detonate "close enough" to the offset point but the resulting explosion doesn't reach the tiny hitbox.

## Problem #2: WGM (Wire-Guided Missile) Misses

**Symptoms**: BMP-2 and Bradley WGMs miss tanks at short/medium range despite being guided.

**Root causes**:
1. **Highest Inaccuracy of all ATGMs (768)** — WGM has 768 WDist scatter vs ATGM's 512 and Hellfire's 512
2. **Lowest turn rate (10)** — Half of ATGM (20) and a third of Hellfire (30). The missile struggles to correct course
3. **Absolute InaccuracyType** — A wire-guided missile at 3 cells and 25 cells has identical 768 WDist scatter. Real wire-guided missiles are MORE accurate at short range, LESS at long range
4. **ManualGuidance: true** — If the BMP/Bradley dies or the operator is suppressed, guidance is lost entirely. This is realistic but compounds the accuracy issue
5. **Low CruiseAltitude (100)** — Hugs the ground, which is realistic for WGMs but can cause terrain-related flight issues

**The accuracy-vs-range paradox**: WGMs currently have the same accuracy at 3 cells as at 25 cells. In reality, a wire-guided missile (TOW, Kornet) is nearly 100% accurate at short range and degrades with distance due to wire slack, operator tracking difficulty, and longer flight time for the target to move.

## Problem #3: ATGM / Javelin Lethality

**Symptoms**: The Javelin (ATGM weapon on AT Specialist infantry) doesn't feel deadly enough against tanks.

**Current stats**:
- Damage: 10000, Penetration: 100
- Ammo: 3 missiles
- TopAttack: true (hits top armor, which is typically weakest)
- Inaccuracy: 512 (Absolute)

**Analysis**: The Javelin's damage (10000) with Penetration 100 is actually lower pen than WGM/Hellfire (800). The TopAttack flag helps because top armor is usually the weakest facing (Distribution index 4, typically 60% of base). But:
- Penetration 100 vs Heavy armor (Thickness 50+) may not be enough for reliable damage
- With Absolute inaccuracy of 512, some missiles miss entirely
- 3 missiles is generous if they actually hit — the problem is they don't always connect

---

## Recommendations

### R1: Separate Aircraft HitShape for Missiles vs Bullets (ENGINE CHANGE)

**The real fix.** The current Radius: 32 is a hack that breaks missiles to fix bullets. We need:
- Option A: **TargetSize property on weapons/warheads** — missiles use a larger effective target radius than bullets. Warhead `Spread` or a new property could specify "this explosion treats aircraft hitshapes as if they were larger"
- Option B: **Conditional HitShape** — Aircraft get a second HitShape active only for `ExplosionDeath` damage type (what missiles deal). `HitShape@MissileTarget: Radius: 256` with `RequiresCondition: airborne`. Bullets use DamageType `BulletDeath` and check against the small hitshape
- Option C: **Increase helicopter HitShape + reduce bullet damage** — Simpler but requires rebalancing all anti-air bullet weapons

**Recommended: Option B** — Cleanest separation, no engine changes needed (conditions + DamageTypes already exist). Just YAML.

Actually, HitShape doesn't filter by DamageType. We'd need two shapes and have the warhead check differently. The simplest real approach:

**Recommended: Increase helicopter HitShape to ~200-256 and reduce bullet weapon damage to aircraft via armor/DamageMultiplier.** This makes missiles reliably hit while rebalancing bullet lethality through damage modifiers rather than impossible-to-hit hitboxes.

### R2: Make WGM Use Range-Scaled Inaccuracy (YAML CHANGE)

Change WGM from `InaccuracyType: Absolute` to `InaccuracyType: Maximum`:
```yaml
WGM:
    Projectile: Missile
        Inaccuracy: 1536        # Max scatter at max range (25 cells)
        InaccuracyType: Maximum  # Scales from 0 at point-blank to 1536 at 25c0
```

This means:
- At 5 cells: ~307 WDist scatter (was 768) — very accurate, realistic for wire-guided
- At 12 cells: ~737 WDist scatter — moderate, operator still tracking well
- At 25 cells: 1536 WDist scatter — hard to control at extreme range

Also increase `HorizontalRateOfTurn` from 10 to 15-18 — a wire-guided missile has continuous operator correction, it should steer better than an ATGM.

### R3: Javelin Rebalance — 2 Missiles, Higher Lethality (YAML CHANGE)

Per user request, reduce ammo from 3 to 2, increase lethality:
```yaml
ATGM:
    Projectile: Missile
        Inaccuracy: 256          # Javelin is fire-and-forget with IR seeker — very accurate
        CloseEnough: 400         # Larger detonation radius (top-attack helps)
    Warhead@Target: TargetDamage
        Damage: 14000            # Up from 10000
        Penetration: 600         # Up from 100 — Javelin penetrates top armor easily
```

And on the AT Specialist:
```yaml
AmmoPool@1:
    Ammo: 2                      # Down from 3
    SupplyValue: 35              # Up from 25 — more expensive per missile
```

### R4: Increase CloseEnough for Anti-Air Missiles (YAML CHANGE)

Anti-air missiles should have larger proximity fuse radii — they're designed to detonate near fast targets:
```yaml
AirToAirMissile:
    CloseEnough: 600             # Up from 400 — larger proximity fuse
    
Hellfire (when targeting air):
    CloseEnough: 500             # Up from 298 default
```

Problem: CloseEnough is per-weapon, not per-target-type. A Hellfire with CloseEnough: 500 would also detonate early against ground targets. Options:
- Accept the tradeoff (ground targets are big enough that it doesn't matter)
- Create `Hellfire.air` variant with larger CloseEnough, used by a separate `Armament@secondary-air`

### R5: Reduce Missile Inaccuracy Re-rolling Frequency (ENGINE CONSIDERATION)

Currently, the offset re-rolls every `RetargetTicks` (default 5) ticks. This means the missile's aim point jumps around during flight. For a wire-guided missile flying for 50+ ticks, that's 10+ random offset changes.

**Suggestion**: Set `RetargetTicks: 0` (or a very high value) for guided missiles to stop mid-flight jitter. The initial offset is enough randomness. Or better: increase RetargetTicks for missile types that should be more stable:
```yaml
WGM:
    RetargetTicks: 15            # Less jitter — operator maintains steady aim
ATGM:
    RetargetTicks: 10            # Moderate jitter — fire-and-forget has some drift
Hellfire:
    RetargetTicks: 8             # Semi-active laser, reasonably stable
AirToAirMissile:
    RetargetTicks: 3             # Active radar seeker, updates rapidly
```

### R6: Increase Warhead Spread for Missile Explosions (YAML CHANGE)

Most missiles have `Spread: 64` (0.06 cells). This is appropriate for direct-hit damage but means near-misses deal zero damage. Real missiles have significant blast radius.

```yaml
# Anti-tank missiles — shaped charge, small blast
ATGM/WGM/Hellfire Warhead@Spread:
    Spread: 128                  # Up from 64 — still small, but catches near-misses

# Anti-air missiles — proximity fuse, designed to fragment
AirToAirMissile/SAM Warhead@Spread:
    Spread: 256                  # Up from 128 — fragmentation warhead
```

### R7: Helicopter Air-to-Air Specific Fixes (YAML CHANGE)

For helicopter-vs-helicopter combat, the Hellfire is being used as an air-to-air weapon (it's also anti-tank). Consider:
- A dedicated `Hellfire.air` weapon variant with lower Inaccuracy (256 vs 512) and higher CloseEnough (500)
- Or: give attack helicopters a separate light air-to-air missile (like Stinger mounted on Apache) that's faster and more agile than Hellfire

---

## Priority Order

1. **R1 (HitShape)** — Root cause fix. Without this, missiles will always struggle against helicopters no matter what else we tune
2. **R2 (WGM range-scaling)** — Biggest impact for ground combat feel. Simple YAML change
3. **R3 (Javelin 2-missile rebalance)** — User-requested, straightforward
4. **R5 (RetargetTicks)** — Reduces random jitter, makes all missiles feel more reliable
5. **R4 (CloseEnough for AA)** — Improves anti-air missile reliability
6. **R6 (Warhead Spread)** — Safety net for near-misses
7. **R7 (Helicopter A2A)** — Polish for helicopter dogfights

## Quick Reference: Key Engine Constants

| Property | Default | What It Does |
|----------|---------|-------------|
| `Inaccuracy` | 0 | Random offset radius from target center (WDist) |
| `InaccuracyType` | Absolute | How inaccuracy scales: Absolute/Maximum/PerCellIncrement |
| `CloseEnough` | 298 | Proximity fuse radius — missile detonates within this distance of offset point |
| `HorizontalRateOfTurn` | 20 | Steering agility (WAngle facings/tick). Higher = tighter turns |
| `VerticalRateOfTurn` | 24 | Vertical steering agility |
| `RetargetTicks` | 5 | How often the offset re-rolls during flight |
| `FlyStraightIfMiss` | true | If missile passes target, stop homing and fly straight |
| `LockOnProbability` | 100 | % chance of locking on (100 = always) |
| `ManualGuidance` | false | Missile loses guidance if shooter dies |
| `AllowSnapping` | false | If true, missile teleports to target when very close |
| `Speed` | 384 | Max speed in WDist/tick |
| `Acceleration` | 5 | Speed increase per tick |
| Warhead `Spread` | varies | Explosion damage radius from impact point |
| HitShape `Radius` | varies | Target's physical hitbox size |
