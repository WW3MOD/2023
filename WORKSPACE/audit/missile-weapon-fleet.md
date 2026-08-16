# Missile weapon fleet — authoritative data sweep

**Part 4 of 4 of the missile audit. READ-ONLY sweep — no files edited except this one.**

Checkout: `main @ dc899995` (`git status -sb` → `## main...origin/main`, no divergence;
only unrelated dirt: modified `WORKSPACE/closeout/missiles-e2475f8d.md` and an untracked
temp file). All values below were re-read from source at this SHA. Nothing here is quoted
from `WORKSPACE/missile-diagnosis.md` or from the task brief without independent
verification.

## Scope and count

Enumeration command: `grep -rn "Projectile: Missile" mods/ww3mod/rules/`
→ **14 hits.** But that undercounts and overcounts in opposite directions, so the real
fleet is:

| Bucket | Count | Notes |
|---|---|---|
| `Projectile: Missile` declared, live | **10** | all in `rules/weapons/weapons-missiles.yaml` |
| Inherit the Missile projectile without redeclaring it | **4** | `WGM.bradley`, `SurfaceToAirMissile.double`, `Stinger.quad`, `9M311` |
| **Live missile weapons total** | **14** | the fleet audited below |
| `Projectile: Missile` inside commented-out blocks | 4 | `rules/ingame/naval.yaml` — dead, see §Deliverable B |

No missile weapon is defined outside `weapons-missiles.yaml`. Confirmed by grepping every
`Inherits:` in `mods/ww3mod/rules/` that names a missile weapon — the only five hits are
`weapons-missiles.yaml:94, 239, 342, 444, 452`, all inside the same file.

Two entries in `weapons-missiles.yaml` look like missiles by name but are **not** on the
Missile code path and are excluded: `IskanderTargeter` (`:284`) and `HIMARSTargeter`
(`:303`, inherits it) both use `Projectile: InstantHit`.

---

## Defaults table (engine source of truth)

Every default cited in the per-weapon rows below resolves to one of these lines. Read from
`engine/OpenRA.Mods.Common/Projectiles/Missile.cs` (`MissileInfo`) unless stated otherwise.

### `MissileInfo` — projectile defaults

| Field | Default | `file:line` |
|---|---|---|
| `Speed` | `WDist(384)` | `Missile.cs:60` |
| `Acceleration` | `WDist(5)` | `Missile.cs:63` |
| `MinimumLaunchSpeed` | `WDist(-1)` (= unset/no clamp) | `Missile.cs:54` |
| `MaximumLaunchSpeed` | `WDist(-1)` (= unset/no clamp) | `Missile.cs:57` |
| `MinimumLaunchAngle` | `WAngle(-64)` → facing **-16** | `Missile.cs:48` |
| `MaximumLaunchAngle` | `WAngle(128)` → facing **32** | `Missile.cs:51` |
| `HorizontalRateOfTurn` | `WAngle(20)` → facing **5** | `Missile.cs:99` |
| `VerticalRateOfTurn` | `WAngle(24)` → facing **6** | `Missile.cs:102` |
| `RangeLimit` | `WDist.Zero` (= no limit) | `Missile.cs:108` |
| `CloseEnough` | `WDist(298)` | `Missile.cs:203` |
| `AllowSnapping` | `false` | `Missile.cs:198` |
| `Inaccuracy` | `WDist.Zero` | `Missile.cs:81` |
| `InaccuracyType` | `Absolute` | `Missile.cs:87` |
| `LockOnInaccuracy` | `WDist(-1)` (= unset → falls back to `Inaccuracy`) | `Missile.cs:90` |
| `LockOnProbability` | `100` | `Missile.cs:93` |
| `Arm` | `0` | `Missile.cs:66` |
| `HomingActivationDelay` | `0` | `Missile.cs:129` |
| `CruiseAltitude` | `WDist(512)` | `Missile.cs:126` |
| `AirburstAltitude` | `WDist.Zero` | `Missile.cs:123` |
| `TerrainHeightAware` | `false` | `Missile.cs:75` |
| `Blockable` | `true` | `Missile.cs:72` |
| `ManualGuidance` | `false` | `Missile.cs:111` |
| `OperatorRetargetTicks` | `0` (= disabled) | `Missile.cs:117` |
| `RetargetTicks` | `5` | `Missile.cs:96` |
| `ExplodeWhenEmpty` | `true` | `Missile.cs:120` |
| `FlyStraightIfMiss` | `true` | `Missile.cs:69` |
| `Gravity` | `10` | `Missile.cs:105` |
| `Jammable` | `true` | `Missile.cs:188` |
| `BoundToTerrainType` | `""` | `Missile.cs:194` |
| `Width` | `WDist(1)` | `Missile.cs:78` |

> **`WAngle` → facing conversion.** `public int Facing => Angle / 4;` —
> `engine/OpenRA.Game/WAngle.cs:67`. So a raw `WAngle(24)` is facing **6**, and a raw
> `WAngle(20)` is facing **5**. Both raw and facing values are stated in every row below.
>
> **Correction to the audit brief:** the brief states the unset default for both turn
> rates is `WAngle(24)`. That is true only for `VerticalRateOfTurn`. The
> `HorizontalRateOfTurn` default is `WAngle(20)` / facing 5 (`Missile.cs:99`). Since no
> weapon in the fleet leaves `VerticalRateOfTurn` declared, and several leave
> `HorizontalRateOfTurn` unset, the distinction is load-bearing — see the `Hellfire
> .strykershorad`, `SurfaceToAirMissile.double`, `Stinger.quad` and `9M311` rows.

### Weapon-level defaults

From `engine/OpenRA.Game/GameRules/WeaponInfo.cs`.

| Field | Default | `file:line` |
|---|---|---|
| `Range` | `WDist.Zero` | `WeaponInfo.cs:77` |
| `MinRange` | `WDist.Zero` | `WeaponInfo.cs:141` |
| `ValidTargets` | `Ground, Water` | `WeaponInfo.cs:122` |
| `ClearSightThreshold` | `5` | `WeaponInfo.cs:146` |
| `FreeLineDensity` | `0` | `WeaponInfo.cs:151` |
| `MissChancePerDensity` | `0` | `WeaponInfo.cs:156` |
| `TopAttack` | `false` | `WeaponInfo.cs:162` |
| `BottomAttack` | `false` | `WeaponInfo.cs:165` |

`ClearSightThreshold`, `FreeLineDensity`, `MissChancePerDensity`, `TopAttack` and
`BottomAttack` are WW3MOD additions to `WeaponInfo` (not stock OpenRA fields).

### Warhead defaults

| Field | Default | `file:line` |
|---|---|---|
| `SpreadDamage.Spread` | `WDist(43)` | `SpreadDamageWarhead.cs:25` |
| `SpreadDamage.Falloff` | `100, 37, 14, 5, 0` | `SpreadDamageWarhead.cs:28` |
| `SpreadDamage.DamageCalculationType` | `HitShape` | `SpreadDamageWarhead.cs:34` |
| `TargetDamage.Spread` | `WDist(1)` | `TargetDamageWarhead.cs:24` |
| `Penetration` | `1` | `DamageWarhead.cs:24` |
| `Damage` | `0` | `DamageWarhead.cs:30` |
| `RandomDamageAddition` | `0` | `DamageWarhead.cs:39` |
| `DamageTypes` | *(empty set)* | `DamageWarhead.cs:51` |
| `Warhead.ValidTargets` | `Ground, Water` | `Warhead.cs:30` |
| `Warhead.AirThreshold` | `WDist(128)` | `Warhead.cs:45` |

`AirThreshold` matters for reading the `Warhead@EffectAir` entries: a detonation position
below 128 is never promoted to the `Air` target type, so an `Air`-gated `CreateEffect`
cannot fire. This is why the `Ataka` row is marked dormant and the `Hellfire` one live.

### Inherited warhead templates

Two templates are pulled in by `Inherits@ExplosionEffects` and supply warheads that the
per-weapon rows below do not repeat.

`^MediumExplosionEffects` (`weapons-effects.yaml:544`) — also inherits
`^MediumSuppressionEffects`:

| Warhead | Type | Key values |
|---|---|---|
| `@Shrapnel` | `SpreadDamage` | `Spread: 256`, `Damage: 200`, `Delay: 5`, `RandomDamagePercentFrom: 0`, `ValidTargets: Infantry, Unarmored`, `DamageTypes: BulletDeath` |
| `@Effect` | `CreateEffect` | `explosion_medium`, `kaboom12.aud`, `ValidTargets: Ground, Ship, Trees, Mine` |
| `@EffectShrapnel` | `CreateEffect` | `shrapnel_medium`, same targets |
| `@Smudge` | `LeaveSmudge` | `Crater`, `InvalidTargets: Vehicle, Structure, Wall, Husk, Trees` |
| `@EffectWater` | `CreateEffect` | `splash_medium`, `ValidTargets: Water, Underwater` |

`^MediumExplosionEffectsAir` (`weapons-effects.yaml:702`) — one warhead only:

| Warhead | Type | Key values |
|---|---|---|
| `@AirEffect` | `CreateEffect` | `explosion_air_medium`, `kaboom25.aud`, `ValidTargets: Air, ICBM`, `ImpactActors: true` |

Note the `@Shrapnel` warhead carries real damage (200 vs infantry). Every ATGM-family
weapon therefore has an undeclared anti-infantry component it did not ask for.

---

# Deliverable A — the weapon table

All 14 live weapons, ordered live-and-frequently-fielded first (see Deliverable B for the
reachability reasoning behind the order). Source column: **D** = declared on this weapon,
**I** = inherited from a parent weapon (parent named), **def** = engine default (not
declared anywhere in the chain).

All 14 live in `mods/ww3mod/rules/weapons/weapons-missiles.yaml`; the `file:line` in each
heading is that file.

---

## 1. `ATGM` — `:2`

Infantry Javelin. Inherits `^MediumExplosionEffects` (effects only, no missile stats).

| Field | Value | Source |
|---|---|---|
| `Range` | `20c0` | D `:7` |
| `MinRange` | `3c0` | D `:8` |
| `RangeLimit` | `21c0` | D `:15` — comfortably **above** `Range` ✅ |
| `Speed` | `300` | D `:13` |
| `Acceleration` | `30` | D `:14` |
| `MinimumLaunchSpeed` | `-1` (unset) | def `Missile.cs:54` |
| `MaximumLaunchSpeed` | `100` | D `:19` |
| `HorizontalRateOfTurn` | raw `20` → **facing 5** | D `:16` |
| `VerticalRateOfTurn` | raw `24` → **facing 6** | def `Missile.cs:102` |
| `CloseEnough` | `298` | def `Missile.cs:203` |
| `AllowSnapping` | `false` | def `Missile.cs:198` |
| `Inaccuracy` | `512` | D `:12` |
| `InaccuracyType` | `Absolute` | def `Missile.cs:87` |
| `LockOnInaccuracy` | `-1` (unset → uses `Inaccuracy`) | def `Missile.cs:90` |
| `LockOnProbability` | `100` | def `Missile.cs:93` |
| `Arm` | `0` | def `Missile.cs:66` |
| `HomingActivationDelay` | `0` | def `Missile.cs:129` |
| `CruiseAltitude` | **`10c0`** | D `:20` — **INTENTIONAL, see note** |
| `AirburstAltitude` | `32` | D `:17` |
| `TerrainHeightAware` | `false` | def `Missile.cs:75` |
| `Blockable` | `false` | D `:18` |
| `ManualGuidance` | `false` | def `Missile.cs:111` |
| `OperatorRetargetTicks` | `0` (disabled) | def `Missile.cs:117` |
| `RetargetTicks` | `5` | def `Missile.cs:96` |
| `ExplodeWhenEmpty` | `true` | def `Missile.cs:120` |
| `FlyStraightIfMiss` | `true` | def `Missile.cs:69` |
| `ValidTargets` | `Vehicle, Defense, Water` | D `:5` |
| `TopAttack` | **`true`** | D `:6` |
| `BottomAttack` | `false` | def `WeaponInfo.cs:165` |
| `ClearSightThreshold` | `3` | D `:4` |
| `FreeLineDensity` | `0` | def `WeaponInfo.cs:151` |
| `MissChancePerDensity` | `0` | def `WeaponInfo.cs:156` |

Warheads: `@Target` `TargetDamage` — `Spread` `1` (def `TargetDamageWarhead.cs:24`),
`Damage: 10000`, `Penetration: 100`, `DamageTypes: ExplosionDeath`, `ValidTargets`
`Ground, Water` (def `Warhead.cs:30`). `@Spread` `SpreadDamage` — `Spread: 64`,
`Damage: 2000`, `Penetration` `1` (def `DamageWarhead.cs:24`), `DamageTypes:
ExplosionDeath`. Plus the five `^MediumExplosionEffects` warheads.

> **`CruiseAltitude: 10c0` is DELIBERATE — do not flag it.** It is ~20× its peers, but the
> AT infantry Javelin is intended to be top-attack, the weapon declares `TopAttack: true`
> (`:6`), and the user has explicitly confirmed the value. A previous audit called it a
> typo and was **wrong**. Recorded here as intentional so the next reader does not
> re-flag it.

---

## 2. `WGM` — `:34`

Wire-guided ATGM (Konkurs/Kornet-class). Inherits `^MediumExplosionEffects`.

| Field | Value | Source |
|---|---|---|
| `Range` | `25c0` | D `:45` |
| `MinRange` | `3c0` | D `:46` |
| `RangeLimit` | `25c0` | D `:57` — **equal to `Range`** ⚠ |
| `Speed` | `300` | D `:58` |
| `Acceleration` | `30` | D `:59` |
| `MinimumLaunchSpeed` | `-1` | def `Missile.cs:54` |
| `MaximumLaunchSpeed` | `50` | D `:60` |
| `HorizontalRateOfTurn` | raw `8` → **facing 2** | D `:65` |
| `VerticalRateOfTurn` | raw `24` → **facing 6** | def `Missile.cs:102` |
| `CloseEnough` | `298` | def `Missile.cs:203` |
| `AllowSnapping` | `false` | def `Missile.cs:198` |
| `Inaccuracy` | `20` | D `:55` |
| `InaccuracyType` | **`PerCellIncrement`** | D `:56` |
| `LockOnInaccuracy` | `-1` | def `Missile.cs:90` |
| `LockOnProbability` | `100` | def `Missile.cs:93` |
| `Arm` | `2` | D `:67` |
| `HomingActivationDelay` | `0` | def `Missile.cs:129` |
| `CruiseAltitude` | `100` | D `:77` |
| `AirburstAltitude` | `0` | def `Missile.cs:123` |
| `TerrainHeightAware` | `true` | D `:76` |
| `Blockable` | `false` | D `:50` |
| `ManualGuidance` | `true` | D `:68` |
| `OperatorRetargetTicks` | `50` | D `:72` |
| `RetargetTicks` | `2` | D `:66` |
| `ExplodeWhenEmpty` | `true` | def `Missile.cs:120` |
| `FlyStraightIfMiss` | `true` | def `Missile.cs:69` |
| `ValidTargets` | `Vehicle, Defense` | D `:44` |
| `TopAttack` / `BottomAttack` | `false` / `false` | def `WeaponInfo.cs:162,165` |
| `ClearSightThreshold` | `3` | D `:41` |
| `FreeLineDensity` | `1` | D `:42` |
| `MissChancePerDensity` | `15` | D `:43` |

Warheads: `@Target` `TargetDamage` — `Spread` `1` (def), `Damage: 10000`,
`Penetration: 800`, `DamageTypes: ExplosionDeath`. `@Spread` `SpreadDamage` —
`Spread: 192`, `Damage: 2000`, `Penetration` `1` (def), `DamageTypes: ExplosionDeath`.
Plus `^MediumExplosionEffects`.

---

## 3. `WGM.bradley` — `:93`

`Inherits: WGM` (`:94`). Declares **only** burst behaviour; every projectile and warhead
value is inherited from `WGM` unchanged.

| Field | Value | Source |
|---|---|---|
| `Burst` | `2` | D `:95` |
| `BurstDelays` | `100` | D `:96` |
| `BurstWait` | `1000` (vs `WGM`'s 500) | D `:97` |
| *all projectile fields* | identical to `WGM` above | I ← `WGM` |
| *all warheads* | identical to `WGM` above | I ← `WGM` |

Inherits `WGM`'s `RangeLimit` == `Range` (`25c0`) condition ⚠.

---

## 4. `Hellfire` — `:169`

US heli ATGM, laser-guided. Inherits `^MediumExplosionEffects`.

| Field | Value | Source |
|---|---|---|
| `Range` | `25c0` | D `:176` |
| `MinRange` | `5c0` | D `:177` |
| `RangeLimit` | `27c0` | D `:196` — above `Range` ✅ |
| `Speed` | `500` | D `:186` |
| `Acceleration` | `30` | D `:188` |
| `MinimumLaunchSpeed` | `-1` | def `Missile.cs:54` |
| `MaximumLaunchSpeed` | `100` | D `:187` |
| `HorizontalRateOfTurn` | raw `60` → **facing 15** | D `:190` |
| `VerticalRateOfTurn` | raw `24` → **facing 6** | def `Missile.cs:102` |
| `CloseEnough` | `298` | def `Missile.cs:203` |
| `AllowSnapping` | `false` | def `Missile.cs:198` |
| `Inaccuracy` | `0c256` (= 256) | D `:189` |
| `InaccuracyType` | `Absolute` | def `Missile.cs:87` |
| `LockOnInaccuracy` | `-1` | def `Missile.cs:90` |
| `LockOnProbability` | `100` | def `Missile.cs:93` |
| `Arm` | `0` | def `Missile.cs:66` |
| `HomingActivationDelay` | `0` | def `Missile.cs:129` |
| `CruiseAltitude` | `512` | **def** `Missile.cs:126` |
| `AirburstAltitude` | `0` | def `Missile.cs:123` |
| `TerrainHeightAware` | `true` | D `:193` |
| `Blockable` | `false` | D `:181` |
| `ManualGuidance` | `false` | def `Missile.cs:111` |
| `OperatorRetargetTicks` | `50` | D `:195` |
| `RetargetTicks` | `2` | D `:191` |
| `ExplodeWhenEmpty` | `true` | def `Missile.cs:120` |
| `FlyStraightIfMiss` | `true` | def `Missile.cs:69` |
| `ValidTargets` | `Vehicle, Air, Defense` | D `:175` |
| `TopAttack` / `BottomAttack` | `false` / `false` | def |
| `ClearSightThreshold` | `3` | D `:172` |
| `FreeLineDensity` | `1` | D `:173` |
| `MissChancePerDensity` | `15` | D `:174` |

Warheads: `@Target` `TargetDamage` — `ValidTargets: Ground, Water, Air` (D `:204`),
`Spread` `1` (def), `Damage: 10000`, `Penetration: 800`, `DamageTypes: ExplosionDeath`.
`@Spread` `SpreadDamage` — `ValidTargets: Ground, Water, Air` (D `:210`), `Spread: 192`,
`Damage: 2000`, `Penetration: 20` (D `:223`), `DamageTypes: ExplosionDeath`.
`@EffectAir` `CreateEffect` — `ValidTargets: Air`, `ImpactActors: true`,
`explosion_air_medium`, `kaboom25.aud` (D `:232-236`). Plus `^MediumExplosionEffects`.

`ManualGuidance` is **`false` by default** here while its two SACLOS siblings (`WGM`,
`Ataka`) set it `true` — consistent with the laser-guided fiction in the comment at
`:182-185`. Note `OperatorRetargetTicks: 50` is still declared even though
`ManualGuidance` is false; whether operator retargeting is gated on `ManualGuidance` in
`Missile.cs` was **not** resolved by this sweep (see Unresolved).

---

## 5. `Hellfire.strykershorad` — `:238`

`Inherits: Hellfire` (`:239`) **and** re-inherits `^MediumExplosionEffects` (`:240`,
redundant — `Hellfire` already pulls it in).

| Field | Value | Source |
|---|---|---|
| `ValidTargets` | `Vehicle, Defense` — **drops `Air`** | D `:241` |
| `Range` | `25c0` (same as parent) | D `:242` (redundant) |
| `MinRange` | `5c0` (same as parent) | D `:246` (redundant) |
| `Burst` | `2` | D `:243` |
| `BurstDelays` | `65` | D `:244` |
| `BurstWait` | `1000` | D `:245` |
| `Speed` | `400` (parent 500) | D `:250` |
| `MaximumLaunchSpeed` | `50` (parent 100) | D `:249` |
| `RangeLimit` | `27c0` | I ← `Hellfire` — above `Range` ✅ |
| `HorizontalRateOfTurn` | raw `60` → facing 15 | I ← `Hellfire` |
| `VerticalRateOfTurn` | raw `24` → facing 6 | def `Missile.cs:102` |
| `CloseEnough` | `298` | def `Missile.cs:203` |
| `Inaccuracy` / type | `0c256` / `Absolute` | I / def |
| `Acceleration` | `30` | I ← `Hellfire` |
| `CruiseAltitude` | `512` | def `Missile.cs:126` |
| `TerrainHeightAware` | `true` | I ← `Hellfire` |
| `Blockable` | `false` | I ← `Hellfire` |
| `ManualGuidance` | `false` | def |
| `OperatorRetargetTicks` / `RetargetTicks` | `50` / `2` | I ← `Hellfire` |
| `ExplodeWhenEmpty` / `FlyStraightIfMiss` | `true` / `true` | def |
| `Arm` / `HomingActivationDelay` / `AirburstAltitude` / `AllowSnapping` | `0` / `0` / `0` / `false` | def |
| `ClearSightThreshold` / `FreeLineDensity` / `MissChancePerDensity` | `3` / `1` / `15` | I ← `Hellfire` |
| `TopAttack` / `BottomAttack` | `false` / `false` | def |

Warheads: all inherited from `Hellfire` unchanged — including `@EffectAir` and the
`Ground, Water, Air` warhead `ValidTargets`. Because the **weapon-level** `ValidTargets`
drops `Air`, that air machinery is **dormant** on this variant, exactly as on `Ataka`.

---

## 6. `Ataka` — `:105`

Mi-28 SACLOS ATGM (9M120). Inherits `^MediumExplosionEffects`.

| Field | Value | Source |
|---|---|---|
| `Range` | `22c0` | D `:112` |
| `MinRange` | `3c0` | D `:113` |
| `RangeLimit` | `22c0` | D `:133` — **equal to `Range`** ⚠ |
| `Speed` | `400` | D `:123` |
| `Acceleration` | `30` | D `:124` |
| `MinimumLaunchSpeed` | `-1` | def `Missile.cs:54` |
| `MaximumLaunchSpeed` | `80` | D `:125` |
| `HorizontalRateOfTurn` | raw `20` → **facing 5** | D `:126` |
| `VerticalRateOfTurn` | raw `24` → **facing 6** | def `Missile.cs:102` |
| `CloseEnough` | `298` | def `Missile.cs:203` |
| `AllowSnapping` | `false` | def `Missile.cs:198` |
| `Inaccuracy` | `22` | D `:121` |
| `InaccuracyType` | **`PerCellIncrement`** | D `:122` |
| `LockOnInaccuracy` / `LockOnProbability` | `-1` / `100` | def |
| `Arm` | `2` | D `:131` |
| `HomingActivationDelay` | `0` | def `Missile.cs:129` |
| `CruiseAltitude` | `100` | D `:132` |
| `AirburstAltitude` | `0` | def `Missile.cs:123` |
| `TerrainHeightAware` | `true` | D `:130` |
| `Blockable` | `false` | D `:117` |
| `ManualGuidance` | `true` | D `:128` |
| `OperatorRetargetTicks` | `50` | D `:129` |
| `RetargetTicks` | `2` | D `:127` |
| `ExplodeWhenEmpty` / `FlyStraightIfMiss` | `true` / `true` | def |
| `ValidTargets` | `Vehicle, Defense` | D `:111` |
| `TopAttack` / `BottomAttack` | `false` / `false` | def |
| `ClearSightThreshold` / `FreeLineDensity` / `MissChancePerDensity` | `3` / `1` / `15` | D `:108-110` |

Warheads: `@Target` `TargetDamage` — `Spread` `1` (def), `Damage: 10000`,
`Penetration: 900`, `DamageTypes: ExplosionDeath`. `@Spread` `SpreadDamage` —
`Spread: 192`, `Damage: 2000`, `Penetration: 20` (D `:152`), `DamageTypes:
ExplosionDeath`. `@EffectAir` `CreateEffect` — `ValidTargets: Air`, `ImpactActors: true`
(D `:163-167`), **dormant**: the weapon cannot target `Air`, and `CruiseAltitude 100` is
below the `AirThreshold` of `128` (`Warhead.cs:45`), so no detonation is promoted to
`Air`. The in-file comment at `:154-162` states this explicitly and is accurate.
Plus `^MediumExplosionEffects`.

---

## 7. `MANPAD` — `:377`

Infantry shoulder-launched SAM. Inherits `^MediumExplosionEffectsAir`.

| Field | Value | Source |
|---|---|---|
| `Range` | `23c0` | D `:380` |
| `MinRange` | `0` | def `WeaponInfo.cs:141` |
| `RangeLimit` | `24c0` | D `:390` — above `Range` ✅ |
| `Speed` | `450` | D `:387` |
| `Acceleration` | `25` | D `:386` |
| `MinimumLaunchSpeed` | `-1` | def `Missile.cs:54` |
| `MaximumLaunchSpeed` | `20` | D `:384` |
| `MaximumLaunchAngle` | raw `1000` → facing 250 | D `:385` (def would be `128`) |
| `HorizontalRateOfTurn` | raw `20` → **facing 5** | D `:391` |
| `VerticalRateOfTurn` | raw `24` → **facing 6** | def `Missile.cs:102` |
| `CloseEnough` | `192` | D `:389` |
| `AllowSnapping` | `false` | def `Missile.cs:198` |
| `Inaccuracy` | `256` | D `:388` |
| `InaccuracyType` | `Absolute` | def `Missile.cs:87` |
| `LockOnInaccuracy` / `LockOnProbability` | `-1` / `100` | def |
| `Arm` | `5` | D `:392` |
| `HomingActivationDelay` | `0` | def `Missile.cs:129` |
| `CruiseAltitude` | `512` | **def** `Missile.cs:126` |
| `AirburstAltitude` | `0` | def `Missile.cs:123` |
| `TerrainHeightAware` | `false` | def `Missile.cs:75` |
| `Blockable` | `false` | D `:393` |
| `ManualGuidance` | `false` | def `Missile.cs:111` |
| `OperatorRetargetTicks` | `0` | def `Missile.cs:117` |
| `RetargetTicks` | `5` | def `Missile.cs:96` |
| `ExplodeWhenEmpty` | `true` | D `:394` (matches def) |
| `FlyStraightIfMiss` | `true` | def `Missile.cs:69` |
| `ValidTargets` | `Air` | D `:379` |
| `TopAttack` / `BottomAttack` | `false` / `false` | def |
| `ClearSightThreshold` | `5` | **def** `WeaponInfo.cs:146` |
| `FreeLineDensity` / `MissChancePerDensity` | `0` / `0` | def |

Warheads: `@Spread` `SpreadDamage` — `Spread: 192`, `Damage: 3000`, `Penetration: 15`,
`ValidTargets: Air` (D `:400-404`), `DamageTypes` **empty** (def `DamageWarhead.cs:51`).
`@EffectGround` `CreateEffect` — `explosion_small`, `kaboom12.aud`, `ValidTargets:
Ground, Water, Trees`. Plus `^MediumExplosionEffectsAir`'s `@AirEffect`.
No `TargetDamage` warhead at all — unlike every ATGM-family weapon.

---

## 8. `Stinger` — `:410`

Base SAM entry. Inherits `^MediumExplosionEffectsAir`. **Not fielded directly by any live
actor** — it exists as the parent of `Stinger.quad` and `9M311` (see Deliverable B).

| Field | Value | Source |
|---|---|---|
| `Range` | `28c0` | D `:413` |
| `MinRange` | `0` | def `WeaponInfo.cs:141` |
| `RangeLimit` | `30c0` | D `:419` — above `Range` ✅ |
| `Speed` | `600` | D `:420` |
| `Acceleration` | `35` | D `:421` |
| `MinimumLaunchSpeed` | `-1` | def `Missile.cs:54` |
| `MaximumLaunchSpeed` | `50` | D `:422` |
| `MaximumLaunchAngle` | raw `1000` → facing 250 | D `:423` |
| `HorizontalRateOfTurn` | raw `20` → **facing 5** | D `:424` |
| `VerticalRateOfTurn` | raw `24` → **facing 6** | def `Missile.cs:102` |
| `CloseEnough` | `256` | D `:418` |
| `AllowSnapping` | `false` | def `Missile.cs:198` |
| `Inaccuracy` | `300` | D `:417` |
| `InaccuracyType` | `Absolute` | def `Missile.cs:87` |
| `LockOnInaccuracy` / `LockOnProbability` | `-1` / `100` | def |
| `Arm` | `5` | D `:425` |
| `HomingActivationDelay` | `0` | def `Missile.cs:129` |
| `CruiseAltitude` | `512` | **def** `Missile.cs:126` |
| `AirburstAltitude` | `0` | def `Missile.cs:123` |
| `TerrainHeightAware` | `false` | def `Missile.cs:75` |
| `Blockable` | `false` | D `:426` |
| `ManualGuidance` | `false` | def `Missile.cs:111` |
| `OperatorRetargetTicks` | `0` | def `Missile.cs:117` |
| `RetargetTicks` | `5` | def `Missile.cs:96` |
| `ExplodeWhenEmpty` | `true` | D `:427` (matches def) |
| `FlyStraightIfMiss` | `true` | def `Missile.cs:69` |
| `ValidTargets` | `Air` | D `:412` |
| `TopAttack` / `BottomAttack` | `false` / `false` | def |
| `ClearSightThreshold` | `5` | **def** `WeaponInfo.cs:146` |
| `FreeLineDensity` / `MissChancePerDensity` | `0` / `0` | def |

Warheads: `@Spread` `SpreadDamage` — `Spread: 256`, `Damage: 5000`, `Penetration: 20`,
`ValidTargets: Air` (D `:433-437`), `DamageTypes` **empty** (def). `@EffectGround`
`CreateEffect` — `explosion_small`, `kaboom12.aud`, `Ground, Water, Trees`. Plus
`^MediumExplosionEffectsAir`'s `@AirEffect`. No `TargetDamage` warhead.

---

## 9. `Stinger.quad` — `:443`

`Inherits: Stinger` (`:444`). Stryker SHORAD's AA armament.

| Field | Value | Source |
|---|---|---|
| `Magazine` | `4` | D `:445` |
| `Burst` | `2` | D `:446` |
| `BurstDelays` | `30` | D `:447` |
| `BurstWait` | `60` (vs `Stinger`'s 250) | D `:448` |
| `ReloadDelay` | `1000` | D `:449` |
| *all projectile fields* | identical to `Stinger` above | I ← `Stinger` |
| *all warheads* | identical to `Stinger` above | I ← `Stinger` |

---

## 10. `9M311` — `:451`

`Inherits: Stinger` (`:452`). Tunguska's SAM. Declares **one** field.

| Field | Value | Source |
|---|---|---|
| `BurstWait` | `40` (vs `Stinger`'s 250) | D `:453` |
| *everything else* | identical to `Stinger` above | I ← `Stinger` |

Despite the real-world 9M311 being a distinct missile from the Stinger, this entry is
mechanically a Stinger with a faster fire cycle. Recorded as fact, not criticism.

---

## 11. `SurfaceToAirMissile.double` — `:341`

`Inherits: SurfaceToAirMissile` (`:342`). Fielded by the `SAM` structure, which is
`~disabled` — see Deliverable B.

| Field | Value | Source |
|---|---|---|
| `Burst` | `2` | D `:343` |
| `BurstDelays` | `20` | D `:344` |
| `BurstWait` | `80` (vs parent's 100) | D `:345` |
| *all projectile fields* | identical to `SurfaceToAirMissile` below | I |
| *all warheads* | identical to `SurfaceToAirMissile` below | I |

---

## 12. `SurfaceToAirMissile` — `:308`

Inherits `^MediumExplosionEffectsAir` (`:309`). **No live actor references it directly**
(its one direct reference is a commented-out Patriot block) — it survives as the parent of
`.double`.

| Field | Value | Source |
|---|---|---|
| `Range` | `35c0` | D `:311` |
| `MinRange` | `0` | def `WeaponInfo.cs:141` |
| `RangeLimit` | `35c0` | D `:323` — **equal to `Range`** ⚠ |
| `Speed` | `800` | D `:320` |
| `Acceleration` | `35` | D `:319` |
| `MinimumLaunchSpeed` | `-1` | def `Missile.cs:54` |
| `MaximumLaunchSpeed` | `50` | D `:318` |
| `HorizontalRateOfTurn` | raw `35` → **facing 8** | D `:324` |
| `VerticalRateOfTurn` | raw `24` → **facing 6** | def `Missile.cs:102` |
| `CloseEnough` | `400` | D `:322` |
| `AllowSnapping` | `false` | def `Missile.cs:198` |
| `Inaccuracy` | `400` | D `:321` |
| `InaccuracyType` | `Absolute` | def `Missile.cs:87` |
| `LockOnInaccuracy` / `LockOnProbability` | `-1` / `100` | def |
| `Arm` | `5` | D `:325` |
| `HomingActivationDelay` | `0` | def `Missile.cs:129` |
| `CruiseAltitude` | `512` | **def** `Missile.cs:126` |
| `AirburstAltitude` | `0` | def `Missile.cs:123` |
| `TerrainHeightAware` | `false` | def `Missile.cs:75` |
| `Blockable` | `false` | D `:326` |
| `ManualGuidance` | `false` | def `Missile.cs:111` |
| `OperatorRetargetTicks` | `0` | def `Missile.cs:117` |
| `RetargetTicks` | `5` | def `Missile.cs:96` |
| `ExplodeWhenEmpty` | `true` | D `:327` (matches def) |
| `FlyStraightIfMiss` | `true` | def `Missile.cs:69` |
| `ValidTargets` | **declared twice**: `Air` (`:310`) then `Air, ICBM` (`:316`) ⚠ | D |
| `TopAttack` / `BottomAttack` | `false` / `false` | def |
| `ClearSightThreshold` | `5` | **def** `WeaponInfo.cs:146` |
| `FreeLineDensity` / `MissChancePerDensity` | `0` / `0` | def |

Warheads: `@Spread` `SpreadDamage` — `Spread: 128`, `Damage: 2000`,
`RandomDamageAddition: 1000`, `ValidTargets: Air, ICBM` (D `:331-335`), `Penetration` `1`
(def), `DamageTypes` **empty** (def). `@EffectGround` `CreateEffect` — `explosion_small`,
`kaboom12.aud`, `Ground, Water, Trees`. Plus `^MediumExplosionEffectsAir`'s `@AirEffect`.
No `TargetDamage` warhead.

---

## 13. `AirToAirMissile` — `:347`

Inherits `^MediumExplosionEffectsAir` (`:348`). Fielded only by `F16` and `MIG`, both
`~disabled` — see Deliverable B.

| Field | Value | Source |
|---|---|---|
| `Range` | `30c0` | D `:351` |
| `MinRange` | `10c0` | D `:352` |
| `RangeLimit` | `35c0` | D `:361` — above `Range` ✅ |
| `Speed` | `800` | D `:358` |
| `Acceleration` | `35` | D `:357` |
| `MinimumLaunchSpeed` | `-1` | def `Missile.cs:54` |
| `MaximumLaunchSpeed` | `400` — highest in the fleet | D `:356` |
| `HorizontalRateOfTurn` | raw `25` → **facing 6** | D `:362` |
| `VerticalRateOfTurn` | raw `24` → **facing 6** | def `Missile.cs:102` |
| `CloseEnough` | `400` | D `:360` |
| `AllowSnapping` | `false` | def `Missile.cs:198` |
| `Inaccuracy` | `400` | D `:359` |
| `InaccuracyType` | `Absolute` | def `Missile.cs:87` |
| `LockOnInaccuracy` / `LockOnProbability` | `-1` / `100` | def |
| `Arm` | `0` | def `Missile.cs:66` |
| `HomingActivationDelay` | `0` | def `Missile.cs:129` |
| `CruiseAltitude` | `2c0` (= 2048) | D `:363` |
| `AirburstAltitude` | `0` | def `Missile.cs:123` |
| `TerrainHeightAware` | `false` | def `Missile.cs:75` |
| `Blockable` | `false` | D `:365` |
| `ManualGuidance` | `false` | def `Missile.cs:111` |
| `OperatorRetargetTicks` | `0` | def `Missile.cs:117` |
| `RetargetTicks` | `5` | def `Missile.cs:96` |
| `ExplodeWhenEmpty` | `true` | D `:366` (matches def) |
| `FlyStraightIfMiss` | `true` | def `Missile.cs:69` |
| `ValidTargets` | `Air, ICBM` | D `:350` |
| `TopAttack` / `BottomAttack` | `false` / `false` | def |
| `ClearSightThreshold` | **`15`** | D `:349` |
| `FreeLineDensity` / `MissChancePerDensity` | `0` / `0` | def |

Warheads: `@Spread` `SpreadDamage` — `Spread: 128`, `Damage: 1000`,
`RandomDamageAddition: 1000`, `ValidTargets: Air, ICBM`, `Penetration` `1` (def),
`DamageTypes` **empty** (def). `@EffectGround` `CreateEffect` — `explosion_small`,
`kaboom12.aud`, `Ground, Water, Trees`. Plus `^MediumExplosionEffectsAir`'s `@AirEffect`.
No `TargetDamage` warhead.

---

## 14. `TimerWolf_Missiles` — `:252`

Inherits `^MediumExplosionEffects` (`:253`). **Dead** — its only reference is commented
out (see Deliverable B).

| Field | Value | Source |
|---|---|---|
| `Range` | `25c0` | D `:255` |
| `MinRange` | `5c0` | D `:259` |
| `RangeLimit` | `27c0` | D `:267` — above `Range` ✅ |
| `Speed` | **`850`** — fastest in the fleet | D `:264` |
| `Acceleration` | `30` | D `:263` |
| `MinimumLaunchSpeed` | `-1` | def `Missile.cs:54` |
| `MaximumLaunchSpeed` | `50` | D `:262` |
| `HorizontalRateOfTurn` | raw `5` → **facing 1** — lowest in the fleet | D `:266` |
| `VerticalRateOfTurn` | raw `24` → **facing 6** | def `Missile.cs:102` |
| `CloseEnough` | `298` | def `Missile.cs:203` |
| `AllowSnapping` | `false` | def `Missile.cs:198` |
| `Inaccuracy` | `1c0` (= 1024) — largest in the fleet | D `:265` |
| `InaccuracyType` | `Absolute` | def `Missile.cs:87` |
| `LockOnInaccuracy` / `LockOnProbability` | `-1` / `100` | def |
| `Arm` | `0` | def `Missile.cs:66` |
| `HomingActivationDelay` | `0` | def `Missile.cs:129` |
| `CruiseAltitude` | `2c0` (= 2048) | D `:268` |
| `AirburstAltitude` | `0` | def `Missile.cs:123` |
| `TerrainHeightAware` | `false` | def `Missile.cs:75` |
| `Blockable` | **`true`** | **def** `Missile.cs:72` — the ONLY missile in the fleet that does not declare `false` ⚠ |
| `ManualGuidance` | `false` | def `Missile.cs:111` |
| `OperatorRetargetTicks` | `0` | def `Missile.cs:117` |
| `RetargetTicks` | `5` | def `Missile.cs:96` |
| `ExplodeWhenEmpty` | `true` | def `Missile.cs:120` |
| `FlyStraightIfMiss` | `true` | def `Missile.cs:69` |
| `ValidTargets` | `Vehicle, Air, Defense` | D `:254` |
| `TopAttack` / `BottomAttack` | `false` / `false` | def |
| `ClearSightThreshold` | `5` | **def** `WeaponInfo.cs:146` |
| `FreeLineDensity` / `MissChancePerDensity` | `0` / `0` | def |

Warheads: `@Spread` `SpreadDamage` — `Spread: 64`, `Damage: 1500`, `ValidTargets: Ground,
Water, Air`, `DamageTypes: ExplosionDeath`, `Penetration` `1` (def). `@EffectAir`
`CreateEffect` — `ValidTargets: Air`, `ImpactActors: true`, `explosion_air_medium`.
Plus `^MediumExplosionEffects`. No `TargetDamage` warhead.

---

# Deliverable B — reachability

## How reachability actually works here — read this before using the table

The brief's heuristic ("a weapon on a unit with `Buildable.Prerequisites: ~disabled` is
dead weight") is **correct but must not be applied naively**, because `~disabled` is used
in two different ways in this mod:

1. **On faction-neutral base actors it means "this is a template, buy the faction
   variant."** `infantry.yaml` defines `^AT` (`:1680`) and its bare actor `AT:` (`:1748`)
   with `Prerequisites: ~disabled` (`:1689`) — yet the AT Specialist is unquestionably a
   live, fielded unit. The buyable actors are `AT.america`
   (`infantry-america.yaml:67`, prereq `~player.america, ~techlevel.infonly` at `:70`) and
   `AT.russia` (`infantry-russia.yaml:67`, prereq at `:70`). Reading `infantry.yaml` alone
   would wrongly condemn every infantry weapon in the mod.
2. **On vehicles, aircraft and structures it is genuine gating.** Those files use real
   prerequisites (`~player.X, ~vehicles.X, ~techlevel.Y`) as the norm, so `~disabled`
   there is a deliberate switch-off.

Also note `SUPPLYROUTE` itself carries `Prerequisites: ~disabled`
(`structures.yaml:247`) — consistent with `CLAUDE.md` describing it as a non-buildable
beachhead, and further confirmation that `~disabled` alone does not mean "does not exist
in play."

**Whole-category finding:** every one of the 19 uncommented defense structures in
`structures-defenses.yaml` is `~disabled`. Static defenses as a category are not
purchasable. That is consistent with the WW3MOD model (no tech tree, no base building
beyond the Supply Route) rather than being a per-actor oversight — but it does mean the
SAM entries below are dead.

## Ranked table — live and frequently fielded first

| # | Weapon | Fielded by | Prereq of that actor | Verdict |
|---|---|---|---|---|
| 1 | `ATGM` | `AT.america` (`infantry-america.yaml:67`), `AT.russia` (`infantry-russia.yaml:67`); armament `infantry.yaml:1701` | `~player.X, ~techlevel.infonly` (`:70` both) | **LIVE** — core infantry, both factions |
| 2 | `MANPAD` | `AA.america` (`infantry-america.yaml:74`), `AA.russia` (`infantry-russia.yaml:74`); armament `infantry.yaml:1771` | `~player.X, ~techlevel.infonly` (`:77` both) | **LIVE** — core infantry, both factions |
| 3 | `WGM` | `bmp2` (`vehicles-russia.yaml:117`), armament `:215` | `~player.russia, ~vehicles.russia, ~techlevel.low` (`:154`) | **LIVE** — lowest techlevel of any missile platform, so the most-fielded vehicle ATGM |
| 4 | `WGM.bradley` | `bradley` (`vehicles-america.yaml:278`), armament `:379` | `~player.america, ~vehicles.america, ~techlevel.medium` (`:318`) | **LIVE** |
| 5 | `Hellfire` | `littlebird` (`aircraft-america.yaml:99`), armament `:190`; `HELI`/Apache (`:267`), armament `:355` | `~techlevel.medium` (`:130`), `~techlevel.high` (`:296`) | **LIVE** — 2 live mounts. A third mount on `A10` (`:424`, armament `:485`) is dead: `A10` is `~disabled` (`:431`) |
| 6 | `Ataka` | `MI28` (`aircraft-russia.yaml:268`), armament `:369` | `~player.russia, ~aircraft.russia, ~techlevel.high` (`:297`) | **LIVE** |
| 7 | `Stinger.quad` | `strykershorad` (`vehicles-america.yaml:795`), armament `:894` | `~player.america, ~vehicles.america, ~techlevel.medium` (`:831`) | **LIVE**. Also map-placed (`strykershorad` appears in a map actor list) |
| 8 | `Hellfire.strykershorad` | `strykershorad` (same actor), armament `:927` | same as above | **LIVE** |
| 9 | `9M311` | `tunguska` (`vehicles-russia.yaml:745`), armament `:855` | `~player.russia, ~vehicles.russia, ~techlevel.medium` (`:779`) | **LIVE**. Also map-placed |
| 10 | `Stinger` | *no live direct mount* | — | **LIVE ONLY AS A PARENT** of #7 and #9. Its three direct references (`naval.yaml:340, 410, 480`) are all commented out |
| 11 | `SurfaceToAirMissile` | *no live direct mount* | — | **LIVE ONLY AS A PARENT** of #12. Its one direct reference (`structures-defenses.yaml:878`, a Patriot block) is commented out |
| 12 | `SurfaceToAirMissile.double` | `SAM` (`structures-defenses.yaml:760`), armament `:799`; `HSAM` (`:815`) inherits `SAM` | `~disabled` (`:783` and `:825`) | **DEAD** — not purchasable and not map-placed. `HSAM` additionally strips `-AttackTurreted` (`:820`), so it could not fire even if enabled |
| 13 | `AirToAirMissile` | `F16` (`aircraft-america.yaml:547`), armament `:580`; `MIG` (`aircraft-russia.yaml:559`), armament `:596` | `~disabled` (`:551` and `:564`) | **DEAD** — both and only mounts are disabled. The entire air-to-air layer is switched off |
| 14 | `TimerWolf_Missiles` | `TimerWolf` actor — **commented out** | — | **DEAD** — sole reference is `vehicles.yaml:766`, inside a fully commented-out actor |

**Split: 9 live, 2 live-only-as-parent, 3 dead.** Counting the two parents as live (they
are reachable in play through their children) gives **11 reachable / 3 dead**.

## The two claims I was asked to verify rather than repeat

| Prior claim | Verdict |
|---|---|
| "Four naval missile entries are entirely commented out." | **TRUE.** `naval.yaml` `:843 ^AntiGroundMissile`, `:875 CruiseMissile`, `:892 ^SubMissileDefault`, `:933 TorpTube` — every line of all four, including their `Projectile: Missile` at `:848, :882, :897, :940`, is `#`-prefixed. These are the 4 grep hits outside `weapons-missiles.yaml` and are excluded from the fleet of 14. |
| "`TimerWolf_Missiles`'s only reference is commented out at `vehicles.yaml:736`." | **Claim TRUE, line number WRONG.** It is the only reference and it is commented out — but at **`vehicles.yaml:766`**, not 736. Anyone re-checking at 736 will find unrelated commented ammo-pip config and may conclude the claim is false. |

---

# Deliverable C — flagged inconsistencies

Posed as questions, per the brief. I did not read the `Missile` tick/detonation logic —
only `MissileInfo`'s field declarations — so none of these are verdicts about in-game
behaviour.

### C1. `RangeLimit` equal to `Range` on 5 of 14 entries, comfortably above on the rest

| Equal (⚠) | Above ✅ |
|---|---|
| `WGM` 25c0 / 25c0 · `WGM.bradley` (inherited) · `Ataka` 22c0 / 22c0 · `SurfaceToAirMissile` 35c0 / 35c0 · `.double` (inherited) | `ATGM` 21/20 · `Hellfire` 27/25 · `Hellfire.strykershorad` 27/25 · `TimerWolf` 27/25 · `MANPAD` 24/23 · `Stinger` 30/28 (+ `.quad`, `9M311`) · `AirToAirMissile` 35/30 |

**Question:** a missile fired at a target at maximum range travels *at least* `Range`, and
more than that as soon as its path is not perfectly straight (`Inaccuracy` offset, target
movement, `OperatorRetargetTicks` swing). With `RangeLimit == Range` it should hit the
limit and self-destruct short. Is the equality deliberate on these five, or should they
carry the ~1–2 cell margin their siblings do? `WGM` and `Ataka` are the ones that matter —
both are live, and both additionally have the *lowest* turn rates in the ATGM family
(facing 2 and facing 5), which is exactly the condition that lengthens the flight path.

### C2. `Speed` exceeds `CloseEnough` on all 14 — systemic, not an outlier

| Weapon | Speed | CloseEnough | Ratio |
|---|---|---|---|
| `TimerWolf_Missiles` | 850 | 298 (def) | 2.85× |
| `MANPAD` | 450 | 192 | 2.34× |
| `Stinger` / `.quad` / `9M311` | 600 | 256 | 2.34× |
| `SurfaceToAirMissile` / `.double` | 800 | 400 | 2.00× |
| `AirToAirMissile` | 800 | 400 | 2.00× |
| `Hellfire` | 500 | 298 (def) | 1.68× |
| `Ataka` / `Hellfire.strykershorad` | 400 | 298 (def) | 1.34× |
| `ATGM` / `WGM` / `.bradley` | 300 | 298 (def) | 1.01× |

**Question:** because this holds for *every* weapon in the fleet including the ones nobody
reports problems with, I do not think it can be read as a per-weapon authoring error. Either
the engine's proximity check is swept across the tick (in which case this is a non-issue and
the audit should stop looking at it) or the whole fleet shares a latent overshoot. Someone
needs to read the detonation logic in `Missile.cs` to settle it — see Unresolved #1. Note
the ATGM trio sit at 1.01×, i.e. `Speed: 300` against the *default* `CloseEnough: 298`;
if that near-equality is intentional it is an odd thing to leave implicit.

### C3. `CruiseAltitude` — the ground-launched Hellfire variant is the real outlier

| Weapon | CruiseAltitude | Declared? |
|---|---|---|
| `WGM` / `.bradley` | 100 | D |
| `Ataka` | 100 | D |
| `Hellfire` | 512 | **def** |
| `Hellfire.strykershorad` | 512 | **def** |
| `SurfaceToAirMissile` / `.double`, `MANPAD`, `Stinger` family | 512 | **def** |
| `AirToAirMissile`, `TimerWolf_Missiles` | 2c0 (2048) | D |
| `ATGM` | 10c0 (10240) | D — **INTENTIONAL, see below** |

**Question:** `Hellfire.strykershorad` is a **ground vehicle** firing at ground targets
(`ValidTargets: Vehicle, Defense`), yet it silently inherits the airborne `Hellfire`'s
default 512 while the directly comparable ground-launched ATGM, `WGM.bradley`, cruises at
100. Was 512 chosen for it, or just inherited? The parent `Hellfire` at 512 is defensible
and in fact load-bearing — its own comment (`:227-230`) relies on 512 clearing the 128
`AirThreshold` so air kills render — but that reasoning does not transfer to a variant
that cannot target air at all.

> **`ATGM`'s `CruiseAltitude: 10c0` is NOT flagged.** It is ~20× its peers and that is
> **deliberate**: the AT infantry Javelin is intended to be top-attack, the weapon
> declares `TopAttack: true` (`:6`), and the user has explicitly confirmed it. A previous
> audit called it a typo and was wrong. Do not re-flag it.

### C4. `TimerWolf_Missiles` is the only missile that does not declare `Blockable: false`

All 13 other entries either declare `Blockable: false` or inherit it. `TimerWolf_Missiles`
takes the engine default `true` (`Missile.cs:72`). **Question:** deliberate, or an
omission? Low urgency — the weapon is dead — but it would ship the inconsistency the day
the Timber Wolf actor is uncommented, which is exactly the failure mode its own in-file
comment (`:276-277`) warns about for a *different* field.

### C5. `ClearSightThreshold` declared without the fields that make it do anything

`ATGM` sets `ClearSightThreshold: 3` (`:4`) and `AirToAirMissile` sets `15` (`:349`), but
neither declares `FreeLineDensity` or `MissChancePerDensity`, both of which default to
`0` (`WeaponInfo.cs:151,156`). The field's own `[Desc]` says it is "used together with
`MissChancePerDensity`" (`WeaponInfo.cs:149`), and with a 0 miss chance no per-shot miss
can ever be rolled. **Question:** is the foliage gating inert by design on these two, or
were the companion fields meant to come along? It is most puzzling on `AirToAirMissile` —
an air-to-air weapon with a foliage threshold at all, and set to 15 (3× the default of 5)
rather than the 3 the ground ATGMs use. The three weapons that declare the full trio
(`WGM`, `Ataka`, `Hellfire` — all `3 / 1 / 15`) are self-consistent.

### C6. `SurfaceToAirMissile` declares `ValidTargets` twice

`:310` `ValidTargets: Air`, then `:316` `ValidTargets: Air, ICBM`, both directly on the
weapon node with the projectile block between them starting at `:317`. **Question:** which
one wins, and was `:310` meant to be deleted when ICBM interception was added? Whichever
resolution MiniYaml applies, having both is a trap for the next editor. Flagged, not
resolved — see Unresolved #3.

### C7. `Hellfire` declares `OperatorRetargetTicks: 50` while `ManualGuidance` stays false

`WGM` (`:68,72`) and `Ataka` (`:128,129`) pair `ManualGuidance: true` with
`OperatorRetargetTicks: 50`. `Hellfire` declares the retarget delay (`:195`) but leaves
`ManualGuidance` at its default `false` (`Missile.cs:111`) — deliberately, per its
laser-guided comment at `:182-185`. **Question:** does `OperatorRetargetTicks` function at
all when `ManualGuidance` is false, or is it inert on `Hellfire` (and therefore on
`Hellfire.strykershorad`, which inherits both)? See Unresolved #2.

### C8. `Hellfire.strykershorad` drops `Air` but keeps the inherited air warheads

Weapon-level `ValidTargets: Vehicle, Defense` (`:241`) removes `Air`, while the inherited
`@Target`/`@Spread` warheads keep `ValidTargets: Ground, Water, Air` and the `@EffectAir`
`CreateEffect` comes along too. That is the same dormant-air-machinery shape that `Ataka`
documents at `:154-162`. **Question:** intentional? It looks it — the Stryker SHORAD
carries `Stinger.quad` as its actual AA weapon and `Hellfire.strykershorad` as its ground
armament — but unlike `Ataka` this one carries no comment saying so, so the next reader has
to re-derive it.

### C9. Redundant redeclarations on `Hellfire.strykershorad`

It re-inherits `^MediumExplosionEffects` (`:240`) which `Hellfire` already pulls in
(`:170`), and redeclares `Range: 25c0` (`:242`), `MinRange: 5c0` (`:246`) and `Report`
(`:247`) at values identical to the parent. Harmless, but it buries what actually differs
(`Speed` 500→400, `MaximumLaunchSpeed` 100→50, `ValidTargets`, burst). Not a bug —
recorded so a future diff-reader is not misled into thinking those are overrides.

### C10. Category difference: no `TargetDamage` warhead on any AA missile

Every ATGM-family weapon (`ATGM`, `WGM`, `.bradley`, `Ataka`, `Hellfire`,
`.strykershorad`) carries a `Warhead@Target: TargetDamage` with `Damage: 10000`. None of
the AA weapons (`MANPAD`, `Stinger` family, `SurfaceToAirMissile` family,
`AirToAirMissile`) nor `TimerWolf_Missiles` do — they rely entirely on `SpreadDamage`.
Almost certainly a deliberate category split rather than an inconsistency; recorded for
completeness so nobody "fixes" it.

---

# Values I could not resolve

1. **Whether `Speed > CloseEnough` actually causes overshoot.** I read `MissileInfo`'s
   field declarations only, not the per-tick movement/detonation code in `Missile.cs`.
   C2 is therefore an open question, not a finding. This is the single most valuable
   thing for the next worker to settle, because it decides whether C2 is a fleet-wide bug
   or a non-issue.
2. **Whether `OperatorRetargetTicks` is gated on `ManualGuidance`** (C7) — same reason.
3. **MiniYaml duplicate-key resolution** for `SurfaceToAirMissile`'s two `ValidTargets`
   lines (C6). I did not trace the parser.
4. **Map pre-placement outside `mods/ww3mod/maps/*/map.yaml`.** I grepped that glob for
   `sam`/`hsam`/`strykershorad`/`tunguska` and found only `strykershorad` and `tunguska`.
   Campaign or mission files elsewhere could in principle place a `SAM`; if so, entry #12
   would move from dead to live. My "not map-placed" claim is scoped to that glob.
5. **Whether `~disabled` actors can still reach play by another route** (crates, campaign
   scripts, AI-only spawn). The Deliverable B verdicts are about *purchasability and map
   placement* only.

Fields confirmed valid and correctly paired while checking the above: `Magazine`
(default `1`, `WeaponInfo.cs:104`) and `ReloadDelay` (default `0`, `:107`, and its `[Desc]`
says it "must be set if Magazine is set") — `Stinger.quad` sets both (`:445`, `:449`), so
that pairing is correct.

---

*Sweep complete: all 14 live weapons covered, plus the 4 dead commented naval entries
accounted for. No files were modified except this report; nothing was staged or committed.*
