# Fire-control audit — everything before a missile projectile exists

**Scope:** part 3 of 4. Target selection → launch decision → `ProjectileArgs`. The projectile's own
flight behaviour is a sister worker's half; where I cross that boundary I say so explicitly.

**Checkout:** `main @ dc899995`, `git status -sb` shows `## main...origin/main`, 0 commits behind
upstream. Working tree carries one unrelated modification (`WORKSPACE/closeout/missiles-e2475f8d.md`)
and one untracked temp file; neither is in scope. Static reading only — no build, no game, no tests,
nothing staged or committed.

**Evidence tags used throughout:** **[R]** read directly from code at the cited line; **[I]** inferred
by composing things I read; **[S]** speculation. Arithmetic is shown, not asserted.

`WORKSPACE/missile-diagnosis.md` was not consulted for any conclusion in this document.

---

## 0. The unit under investigation

The AA soldier is `AA.america` / `AA.russia`, from template `^AA`
(`mods/ww3mod/rules/ingame/infantry.yaml:1752`). **[R]**

| Property | Value | Source |
|---|---|---|
| Inherits | `^CamoSoldier` → `^Soldier` → `^Infantry` | `infantry.yaml:1753`, `:282`, `:167` |
| Attack trait | `AttackFrontal`, `FacingTolerance: 50`, `AlignBodyToTarget: true` | `infantry.yaml:182-184` |
| Targeting | `^AutoTargetAir` | `infantry.yaml:1754` |
| `AutoTarget.ScanRadius` | 25 (cells) | `infantry.yaml:288` (`^CamoSoldier`) |
| `AutoTarget.MinimumScanTimeInterval` / `Maximum` | 16 / 32 | `infantry.yaml:289-290` |
| `AutoTarget.PreemptScanInterval` | 25 | `defaults.yaml:659` |
| Armament | `Name: primary`, `Weapon: MANPAD`, `PauseOnCondition: !ammo-primary` | `infantry.yaml:1769-1772` |
| Ammo | 3, `SupplyValue: 65` | `infantry.yaml:1773-1780` |
| Turret | **none declared** | (no `Turreted:` anywhere in `^AA`/`^CamoSoldier`/`^Soldier`) |
| `Armament.LocalOffset` | **not set** | (absent from `^AA`) |
| `Armament.Recoil` | not set → `WDist.Zero` | `Armament.cs:58` |
| `Armament.FireDelay` | not set → **3** | `Armament.cs:42` |
| `Armament.AimingDelay` | not set → **15** | `Armament.cs:45` |
| `Armament.MovementInaccuracy` | not set → 30 | `Armament.cs:48` |

`MANPAD` (`mods/ww3mod/rules/weapons/weapons-missiles.yaml:377-408`): **[R]**

| Field | Value | Note |
|---|---|---|
| `ValidTargets` | `Air` | |
| `Range` | `23c0` = 23552 | |
| `MinRange` | **not set → `WDist.Zero`** | `WeaponInfo.cs:141` |
| `ClearSightThreshold` | not set → **5** | `WeaponInfo.cs:146` |
| `MissChancePerDensity` | not set → **0 (feature off)** | `WeaponInfo.cs:156` |
| `FreeLineDensity` | not set → 0 | `WeaponInfo.cs:151` |
| `TargetActorCenter` | not set → **false** | `WeaponInfo.cs:159` |
| `LockAimPerBurst` | not set → false | `WeaponInfo.cs:89` |
| `Burst` | not set → 1 | |
| `BurstWait` | 200 | |
| `FirstBurstTargetOffset` / `FollowingBurstTargetOffset` | not set → `WVec.Zero` | `WeaponInfo.cs:83` |
| Projectile | `Missile`, `MaximumLaunchSpeed: 20`, `Acceleration: 25`, `Speed: 450`, `Inaccuracy: 256`, `CloseEnough: 192`, `RangeLimit: 24c0`, `HorizontalRateOfTurn: 20`, **`MaximumLaunchAngle: 1000`**, `Arm: 5`, `Blockable: false`, `ExplodeWhenEmpty: true` | |

The helicopter side (verified by a sister search agent against the same checkout): **no WW3MOD
helicopter overrides `CruiseAltitude` or `MinAirborneAltitude`**, so every heli flies at the engine
default `CruiseAltitude = new(1280)` — `engine/OpenRA.Mods.Common/Traits/Air/Aircraft.cs:29` — i.e.
**1280 w-units = 1.25 cells above terrain**. **[R]**

---

## 1. The full chain, target selection → `ProjectileArgs`

### 1.1 Acquisition

An idle AA soldier reaches `AutoTarget.ScanAndAttack` via `INotifyIdle.TickIdle`
(`AutoTarget.cs:629-645`). `ScanForTarget` (`:1067`) either returns an override target or, every
16–32 ticks (`nextScanTime`, `:1094`), calls `ChooseTarget` (`:1240`). **[R]**

`ChooseTarget`'s per-candidate filter chain, **every branch**: **[R]**

1. `:1308` early-out if `attackStances == Enemy` and the actor is not hostile.
2. `:1314` `PreventsAutoTarget` / not viewable by owner → skip.
3. `:1337` `MatchesTargetPriority` — relationship + `ValidTargets`/`InvalidTargets`. For `^AA` the
   priority table is the merge of the **two** `^AutoTargetAir` top-level entries in `defaults.yaml`
   (`:502` with `Inherits: ^AutoTarget`, and `:654`). Merged result: `AutoTargetPriority@FireAtWill`
   Priority 1 (from `^AutoTarget`, `defaults.yaml:330-331`), `@Air` Priority 2 `ValidTargets: Air`, and
   `@Default` `ValidTargets: Air`. The duplicate key is **deliberate and documented in-file**
   (`defaults.yaml:656-658`) — it is how `PreemptScanInterval: 25` (`:659`) reaches this chain. Not a defect.
4. `:1344-1348` armament reachability. With `allowMove == false` the armament must satisfy
   `target.IsInRange(pos, arm.MaxRange())` **and** `!target.IsInRange(pos, arm.Weapon.MinRange)`.
5. `:1353` firing-arc test when `!allowTurn`.
6. `:1365` `FiringLOS.HasClearLOS(self, target, bestThreshold)` where `bestThreshold` is the **maximum**
   `ClearSightThreshold` across the reaching armaments (`:1360-1363`) — i.e. most permissive.
7. `:1373` overkill skip (`AverageDamagePercent >= OverkillThreshold`, default 100).
8. `:1382` `BreakOffCondition` skip (default `critical-damage`).
9. `:1389-1474` scoring. `priorityValue = targetRange − clusterBonus + softOverkillPenalty −
   effectivePriority × 2^24`. The bucket term (`PriorityBucketSize = 1L << 24`, `:1434`) dominates, so
   priority is categorical and range only tiebreaks within a band.

**Range enters target selection only as a tiebreaker and as the in/out-of-range filter at step 4.**
There is no "too close" rejection for MANPAD, because `MinRange` is `WDist.Zero`. **[R]**

`ScanAndAttack` → `AttackBase.AttackTarget` (`AttackBase.cs:657`) → `GetAttackActivity` →
`AttackFrontal.GetAttackActivity` (`AttackFrontal.cs:42-46`) → `Activities.Attack`. **[R]**

**Preemption** (`AutoTarget.TickPreemption`, `:973-1006`), relevant to the recent upstream change: it
fires only when `!self.IsIdle && PreemptionDue(self)`; `PreemptionDue` (`:1019-1026`) requires
`PreemptScanInterval > 0` (25 here) **and** `stance >= FireAtWill`, and gates on
`(WorldTick + ActorID % interval) % interval == 0` — zero RNG. It only switches on a **strictly
higher** `AutoTargetPriority` band (`TryFindHigherBandTarget`, `:1034-1057`, called with
`allowMove: false`). For `^AA` every valid target is `Air`, so all candidates sit in the same band and
**preemption can never fire on this unit**. **[I]** — follows from the merged priority table having
one `ValidTargets` entry class.

### 1.2 The per-tick attack loop

`Activities/Attack.cs:207` `TickAttack`, **every branch traced**: **[R]**

- `:209` invalid target → `UnableToAttack`.
- `:214-217` break-off if the target carries `critical-damage` and this is not a force-attack.
- `:241-243` no armaments for the target → drop.
- `:253` `AbandonWhenArmamentsPaused` (not set for `^AA`, default false — `AttackBase.cs:72`).
- `:257-258` `minRange = armaments.Max(a => a.Weapon.MinRange)` = 0; `maxRange = armaments.Min(a => a.MaxRange())` = 23552.
- `:265-272` `needsToMove = outOfRange || tooClose || cantInteract || losBlocked`. **`tooClose` is
  `minRange.Length != 0 && ...` — with `MinRange` zero it is hard-false, so an AA soldier is never
  "too close".** `losBlocked` uses `FiringLOS.GetBestThreshold`.
- `:301` `desiredFacing = (attack.GetTargetPosition(pos, target) - pos).Yaw`.
- `:303-316` firing-arc gate, then turn, then re-check.
- `:317-321` `AlignBodyToTarget` (true for `^Soldier`) keeps refining facing even when already in arc.
- `:324` `DoAttack` → `a.CheckFire(self, facing, target)` for each armament (`:329-334`).

**`GetTargetPosition` / `GetCurrentTarget` (`AttackBase.cs:541-549`)** return
`HasAnyValidWeapons(target, true) ? target.CenterPosition : target.Positions.ClosestToIgnoringPath(pos)`.
`checkForCenterTargetingWeapons: true` tests `armament.Weapon.TargetActorCenter`
(`AttackBase.cs:532`), which is **false** for MANPAD, so the `Positions` branch is taken. The heli's
only `ITargetablePositions` trait is a `HitShape` with default `TargetableOffsets = { WVec.Zero }`,
so `Positions` is the single 3D centre **including the 1280 altitude**. **[R]**

### 1.3 The firing-arc and LOS gates — both are 2D

`AttackBase.TargetInFiringArc` (`:243-264`): **[R]**

```
delta = GetTargetPosition(pos, target) - pos
if (delta.HorizontalLengthSquared == 0) return true;      // :252  — target directly overhead ALWAYS passes
if (target.Type == Invalid) return false;
if (!FiringLOS.HasClearLOS(self, target, GetBestThreshold(...))) return false;
return Util.FacingWithinTolerance(facing.Facing, delta.Yaw, facingTolerance);
```

`WVec.Yaw` (`engine/OpenRA.Game/WVec.cs:66-76`) is `WAngle.ArcTan(-Y, X) - WAngle(256)` — **X and Y
only; Z is discarded** (the only use of 3D `LengthSquared` is the null guard). **[R]**

`Target.IsInRange` (`engine/OpenRA.Game/Traits/Target.cs:196-203`) is explicitly horizontal:
`(t - origin).HorizontalLengthSquared <= range.LengthSquared`, with the in-code comment "Target ranges
are calculated in 2D, so ignore height differences". **[R]**

**`FacingTolerance: 50` arithmetic.** `WAngle` is 1024 units = 360° (`WAngle.cs:20-21`), and
`Util.FacingWithinTolerance` (`engine/OpenRA.Mods.Common/Util.cs:155-162`) compares
`delta = (desiredFacing - facing).Angle` against `delta <= 50 || delta >= 1024 - 50`. So the accepted
arc is **±50/1024 of a full turn = ±17.58°, a 35.16° total arc** — *not* 50 facing units and *not*
50 degrees. **[R]** (This is the class of error the previous report made; stated explicitly here.)

**So: every gate between target selection and trigger-pull is horizontal-only. Nothing anywhere in
fire control measures, bounds, or even represents the elevation angle to the target.** **[R]**

### 1.4 `Armament.CheckFire` → `FireBarrel`

`CheckFire` (`Armament.cs:345-384`), **every branch**: **[R]**

- `:347-354` target changed (`Target.Equals` is reference/generation equality —
  `Target.cs:279-281`, `other == this`) → reset `AimingDelay` to 15, clear `delayedActions`,
  `AimInitialTargetPosition`, `lockedAimCenter`. A continuously-tracked actor target does **not**
  re-trigger this, so `AimingDelay` is paid once per acquisition, not per tick. **[I]**
- `:356` `CanFire` (`:325-341`): not reloading / not burst-waiting / not aiming / not paused; turret
  facing (no turret here → skipped); `target.IsInRange(pos, MaxRange())` **and**
  `Weapon.MinRange != WDist.Zero && target.IsInRange(pos, Weapon.MinRange)` → **the MinRange clause is
  short-circuited off entirely when `MinRange` is zero**; `Weapon.IsValidAgainst`.
- `:364` **per-weapon** LOS gate `FiringLOS.HasClearLOS(self, target, Weapon.ClearSightThreshold)`.
- `:367-371` burst reset if idle longer than `BurstWait`.
- `:374-376` barrel selection. With `Burst == 1` it cycles `Barrels[currentBarrel % barrelCount]`;
  `^AA` declares no `LocalOffset`, so `Barrels` is the single synthesised
  `{ Offset: WVec.Zero, Yaw: WAngle.Zero }` (`Armament.cs:211-212`).
- `:378` `FireBarrel`.

**Muzzle geometry.** `CalculateMuzzleOffset` (`:675-689`): `localOffset = b.Offset + WVec(-Recoil,0,0)`
= `WVec.Zero` (barrel offset zero, `Recoil` zero); no turret, so rotate by the quantised body
orientation; `coords.LocalToWorld(WVec.Zero)` = `WVec.Zero`. **`MuzzlePosition() ==
self.CenterPosition` exactly — the missile is born at the soldier's own centre, at terrain level.**
**[R]/[I]** — [R] on each step, [I] on the composition.

`CalculateMuzzleOrientation` (`:696-699`): `WRot.FromYaw(b.Yaw).Rotate(self.Orientation)`;
`MuzzleFacing()` = `.Yaw`. **A yaw. There is no pitch anywhere in it.** **[R]**

### 1.5 The exact `ProjectileArgs` a MANPAD missile is constructed with

`Armament.cs:438-452`, evaluated **at `CheckFire` time**, i.e. `FireDelay` = 3 ticks *before* the
projectile is created (`ScheduleDelayedAction`, `:456`; spawn at `:590-592`): **[R]**

| Field | Value for MANPAD/AA-soldier | Line |
|---|---|---|
| `Weapon` | `MANPAD` | `:440` |
| `Facing` | `MuzzleFacing()` = body yaw. **Horizontal only.** | `:441` |
| `CurrentMuzzleFacing` | `MuzzleFacing` (live delegate) | `:442` |
| `Source` | `MuzzlePosition()` = `self.CenterPosition`, **z = terrain** | `:446` |
| `CurrentSource` | `MuzzlePosition` (live delegate) | `:447` |
| `SourceActor` | the soldier | `:448` |
| `PassiveTarget` | `target.Positions.ClosestToIgnoringPath(MuzzlePosition())` — the heli's 3D centre **including z = 1280**, sampled 3 ticks before launch | `:415`, `:449` |
| `TargetingVector` | `WVec.Zero` (both burst offsets unset) | `:417`, `:450` |
| `GuidedTarget` | the live `Target` (actor) | `:451` |
| `DamageModifiers` / `InaccuracyModifiers` / `RangeModifiers` | trait arrays | `:443-445` |

**`ProjectileArgs` has no elevation field at all.** `Facing` is a single `WAngle`
(`engine/OpenRA.Game/GameRules/WeaponInfo.cs`, `class ProjectileArgs`). **[R]** The only way the
projectile can learn where the target is vertically is by differencing `PassiveTarget - Source`
itself, or by reading `GuidedTarget`.

### 1.6 What the delayed action does — and does *not* — do for a missile

`Armament.cs:456-605`. **Both arms of the decisive conditional:** **[R]**

- `:473` re-validate the captured target; abort the spawn if a live actor target went invalid during
  the 3-tick `FireDelay`. (Ammo is already spent — `UpdateMagazine`/`UpdateBurst` ran back at
  `:380-381`.)
- `:480` **`if (Weapon.Projectile is BulletInfo bullet && ...)`** — this guards *everything*
  lead-related. Inside it: `LockAimPerBurst` reuse (`:482-489`) **and** the lead computation
  (`:492-521`) with `WVec.CalculateLeadTarget` and the `MovementInaccuracy` wobble (`:507-515`).
  **A `Missile` is not a `BulletInfo`, so for MANPAD this entire block is skipped.**
  Consequences, all **[R]** by absence:
  - **No lead computation.** `args.PassiveTarget` is never updated after `:415`.
  - **No `MovementInaccuracy`.** The `MovementInaccuracy: 30` field is dead for every missile weapon.
  - `AimInitialTargetPosition` accumulates (`:394`) and is only drained inside the bullet branch
    (`:487`, `:501`) — so for a missile weapon it is only ever cleared by `StoppedAiming` (`:224`) or
    a target change (`:352`). Unbounded growth is bounded in practice by those two, but it is
    accumulate-only on the missile path. **[I]** — low severity; noted, not ranked.
- `:529` the foliage miss roll, gated on `args.Weapon.MissChancePerDensity > 0`. **MANPAD does not set
  it → 0 → the whole block is skipped.** See §3.
- `:590` `args.Weapon.Projectile.Create(args)`.

**Net: for a missile, the delayed action is a re-validation gate and nothing else. The
`ProjectileArgs` handed to `Missile.Create` are byte-for-byte the ones built at `:438`, three ticks
stale.** **[I]** — composition of the two skipped branches above.

---

## 2. What fire control hands the projectile differently at 2 cells versus 15 cells

**Direct answer: almost nothing, and nothing that could explain a miss.** Enumerated exhaustively:

| Quantity | 2 cells | 15 cells | Differs? |
|---|---|---|---|
| `Source` | `self.CenterPosition` (terrain level) | same | **No** |
| `Facing` | body yaw, ±17.58° tolerance | same | **No** |
| `PassiveTarget` | heli 3D centre, 3 ticks stale | same rule | **No** (rule identical) |
| `GuidedTarget` | live actor target | same | **No** |
| `TargetingVector` | `WVec.Zero` | same | **No** |
| Lead applied | none (missile) | none | **No** |
| `MovementInaccuracy` | none (missile) | none | **No** |
| `MinRange` refusal | none (`MinRange` = 0) | none | **No** |
| Elevation information conveyed | **none** | **none** | **No** |
| Foliage miss roll | off (`MissChancePerDensity` = 0) | off | **No** |
| `FiringLOS` gate active? | **yes** at ≥2 cells | yes | see below |
| Angular staleness of `PassiveTarget` | **large** | small | **Yes** |

Two genuine range-dependencies exist, and only two:

**(a) The LOS gate switches on between 1 and 2 cells.** `FiringLOS.HasClearLOS`
(`FiringLOS.cs:71-78`) returns `true` unconditionally when `distSq < 4`, where
`distSq = dx² + dy²` in **cells**. At exactly 2 cells orthogonal, `dx=2, dy=0 → distSq = 4`, which is
**not** `< 4`, so the ShadowLayer lookup runs. At 1 cell diagonal, `1+1 = 2 < 4`, so it short-circuits
clear. `GetGroundShadowDensity` (`:144`) uses the identical `distSq < 4 || distSq > 1024` bound.
**[R]** The user's 2–4 cell engagement sits just inside the region where the lookup is live. Because
the target is airborne, `useAirborne` is true (`:115`) and the airborne channel is used, which the
in-code comment describes as carrying "much lower values" (`:113`). **[R]** This gate can only
*prevent* a shot, never spoil one — and the shots were observed to happen, so it did not fire.

**(b) `PassiveTarget` is 3 ticks stale, and 3 ticks of heli movement subtends a much larger angle up
close.** `FireDelay` = 3 (`Armament.cs:42`). Apache/Mi-28 `Speed: 245` per tick → 735 w-units of
travel. Slant range at 2 cells = `√(2048² + 1280²)` = 2415; at 15 cells = `√(15360² + 1280²)` = 15413.
Angular staleness = `atan(735/2415)` = **16.9°** versus `atan(735/15413)` = **2.7°**. **[I]** —
arithmetic mine, inputs [R]. This matters only insofar as the projectile actually uses
`PassiveTarget` rather than homing on `GuidedTarget`; `GuidedTarget` is valid and live here, so
**resolving whether this bites is the sister worker's call.**

**No fire-control quantity encodes elevation, so fire control cannot be range-sensitive in the
vertical axis at all.** The vertical asymmetry between 2 cells and 15 cells is entirely created
downstream, in the projectile. **[I]**

Geometry, for the sister worker (heli at 1280 above terrain, launcher muzzle at terrain level):

| Horizontal range | Elevation to target | in facing units (×256/360) |
|---|---|---|
| 1 cell (1024) | `atan(1280/1024)` = **51.34°** | 36.5 |
| 2 cells (2048) | `atan(1280/2048)` = **32.01°** | 22.8 |
| 3 cells (3072) | `atan(0.41667)` = **22.62°** | 16.1 |
| 4 cells (4096) | `atan(0.3125)` = **17.35°** | 12.3 |
| 8 cells (8192) | `atan(0.15625)` = **8.88°** | 6.3 |
| 15 cells (15360) | `atan(0.08333)` = **4.76°** | 3.4 |
| 23 cells (max range) | `atan(0.054348)` = **3.11°** | 2.2 |

---

## 3. The foliage layer — real achievable miss percentages

The mechanism is `Armament.cs:529-588`: `density = FiringLOS.GetGroundShadowDensity(self, target)`;
`excess = max(0, density − FreeLineDensity)`; `missPct = min(95, excess × MissChancePerDensity)`;
roll `SharedRandom.Next(100) < missPct`. **[R]**

**The 95 cap is unreachable for every weapon that ships, because the shot is gated first.**
`CheckFire:364` refuses the shot unless `HasClearLOS(self, target, Weapon.ClearSightThreshold)`, and
`HasClearLOS:119` returns `shadow <= threshold` reading **the same cell pair and the same
ground/airborne channel** that `GetGroundShadowDensity:164` reads. So any shot that reaches the roll
satisfies `density <= ClearSightThreshold`. **[R]** Therefore:

```
maxMissPct = min(95, (ClearSightThreshold − FreeLineDensity) × MissChancePerDensity)
```

Only three weapons in the mod enable the feature, and all three carry identical numbers —
`ClearSightThreshold: 3`, `FreeLineDensity: 1`, `MissChancePerDensity: 15`:
`WGM` (`weapons-missiles.yaml:41-43`), `Ataka` (`:108-110`), `Hellfire` (`:172-174`). **[R]**

**Achievable set for all three (exhaustive, integer densities only):** **[R]/[I]**

| Density on line | excess | miss % |
|---|---|---|
| 0 or 1 | 0 | **0%** (block skipped, `excess > 0` fails) |
| 2 | 1 | **15%** |
| 3 | 2 | **30%** |
| ≥4 | — | shot never taken (`HasClearLOS` refuses) |

**So the real achievable range is {0%, 15%, 30%} per shot — never 95%, and never anything between.**
`WGM.2shot` (`:94-97`, `Burst: 2`) rolls independently per shot, so its per-burst probability of at
least one clip is at most `1 − 0.70²` = **51%**. **[I]**

Two further conditions narrow it further, both **[R]**:

- The roll is a **no-op unless a tree actor is found on the line.** `:541-549` requires an actor
  within 512 w-units of the muzzle→target segment whose enabled `ITargetable.TargetTypes` contains
  `"Trees"`. If `candidates.Count == 0`, `args` is untouched (`:551`) and the shot proceeds
  normally. Shadow density that comes from anything other than a `Trees`-typed actor therefore
  produces a wasted roll and a clean shot.
- The redirect sets **`args.GuidedTarget = Target.Invalid`** (`:583`) as well as
  `args.PassiveTarget = treeOnLine.CenterPosition` (`:582`). This is the one place in fire control
  that deliberately disarms homing — and it is correctly documented as intentional at `:468-472`.

**Divergence found (real, but currently unexploited):** `HasClearLOS` returns `true` early for
`IndirectFire` units (`FiringLOS.cs:49-51`); `GetGroundShadowDensity` has **no such early-out** and
will happily read a density of up to 255. An `IndirectFire` unit carrying a `MissChancePerDensity`
weapon would therefore bypass the LOS gate and then take an **unbounded** roll that *can* reach the
95% cap. **[R]** Cross-checking the mod: the `IndirectFire` trait appears on
`infantry.yaml:1555`, `:2327`, `vehicles-america.yaml:611,743,1035`, `vehicles-russia.yaml:450,568,694,949`
— artillery/mortar units — and none of them carries `WGM`/`Ataka`/`Hellfire`. **So the divergence is
latent, not live.** **[I]** It becomes live the day an `IndirectFire` unit is given one of those
weapons.

**None of this touches the AA-soldier bug**: `MANPAD` leaves `MissChancePerDensity` at its default 0
(`WeaponInfo.cs:156`), so `Armament.cs:529` short-circuits and the foliage path never executes.
**Foliage is definitively not the cause of the reported miss.** **[R]**

---

## 4. Where the launch decision and the projectile's capability are inconsistent

This is the boundary with the sister worker's half. I am reporting it because it is exactly the
inconsistency the brief asked me to find, and because the fire-control side of it is unambiguous.

**Fire control's contract, restated from §1:** it will authorise a shot at any target from 0 to 23552
w-units horizontal, with no minimum range, no elevation gate, and no elevation datum in
`ProjectileArgs`. A helicopter at 2 cells is, to every gate in the chain, a 2-cell flat shot.

**The projectile's contract:** `Missile`'s constructor derives its own launch elevation and then
clamps it to `[MinimumLaunchAngle, MaximumLaunchAngle]`.

### 4.1 The arithmetic

`Missile.cs:394-436` `DetermineLaunchSpeedAndAngle`. **All three branches traced:** **[R]**

- `:414` incline branch — requires `info.TerrainHeightAware`. Default is **false**
  (`Missile.cs:75`), and MANPAD/Stinger do not set it (only `WGM`/`Ataka`/`Hellfire` do, at
  `weapons-missiles.yaml:76,130,193`). **Not taken.**
- `:416` `lastHt != 0` — `lastHt` is only written by `InclineLookahead`, which runs only
  `if (info.TerrainHeightAware)` (`:407-408`), so it stays 0. **Not taken.**
- `:421-435` **the branch that runs.** `vFacing = (sbyte)vDist.Yaw.Facing` where
  `vDist = new WVec(-tarDistVec.Z, -relTarHorDist, 0)` — the elevation toward the target — then:

```
vFacing = vFacing.Clamp((sbyte)(minLaunchAngle.Angle >> 2),
                        (sbyte)(maxLaunchAngle.Angle >> 2));
```

Now the numbers. `FieldLoader.ParseWAngle` (`engine/OpenRA.Game/FieldLoader.cs:250-253`) constructs
`new WAngle(res)` from the raw YAML integer — **no degree conversion**. `WAngle`'s constructor
(`WAngle.cs:26-32`) normalises into `[0, 1023]`. `WAngle.Facing` is `Angle / 4` (`WAngle.cs:67`).
**[R]**

- `MinimumLaunchAngle`, not set → default `new WAngle(-64)` (`Missile.cs:48`).
  `-64 % 1024 = -64`, negative → `+1024` → `Angle = 960`. `960 >> 2 = 240`. `(sbyte)240 = 240 − 256 =` **−16**.
  −16 facing units × 360/256 = **−22.5°**. (The `sbyte` cast is deliberate: it is how the engine
  encodes negative launch angles, and it round-trips the default correctly.)
- `MaximumLaunchAngle`, **set to 1000** by MANPAD (`weapons-missiles.yaml:385`) and Stinger (`:423`).
  `Angle = 1000`. `1000 >> 2 = 250`. `(sbyte)250 = 250 − 256 =` **−6**.
  −6 facing units × 360/256 = **−8.44°**.
- For every *other* missile in the mod (none set the field): default `new WAngle(128)`
  (`Missile.cs:51`). `128 >> 2 = 32`. `(sbyte)32 =` **+32** = **+45°**.

**`Exts.Clamp` (`engine/OpenRA.Game/Exts.cs:65-73`) is the standard `val < min → min; val > max → max`.**
So for MANPAD/Stinger the permitted launch-elevation band is **[−16, −6] facing units = [−22.5°,
−8.44°] — entirely below the horizon.** For every other missile it is **[−16, +32] = [−22.5°, +45°]**.
**[R]** for each step; **[I]** for the composed band.

The sign convention is confirmed from inside the same function: `:427-430` reads
*"Do not accept -1 as valid vertical facing since it is usually a numerical error and will lead to
premature descent and crashing into the ground"* — negative `vFacing` is descent. **[R]** It is
independently confirmed by the degenerate case: for a level target, `tarDistVec.Z = 0` gives
`vDist = (0, −d, 0)`, and `WVec.Yaw = ArcTan(d, 0) − WAngle(256) = 256 − 256 = 0`. **[I]**

### 4.2 The inconsistency, stated plainly

**Fire control authorises a shot at a target 32° above the horizon; the projectile is configured such
that it cannot be launched above −8.44°.** Desired-versus-permitted, using §2's geometry:

| Range | Desired `vFacing` | Clamped to | Error |
|---|---|---|---|
| 2 cells | +22.8 | **−6** | 28.8 units = **40.5°** |
| 4 cells | +12.3 | **−6** | 18.3 units = **25.7°** |
| 15 cells | +3.4 | **−6** | 9.4 units = **13.2°** |
| 23 cells | +2.2 | **−6** | 8.2 units = **11.6°** |

The clamp bites at **every** range — the target is always above the launcher, so the desired value is
always positive and always exceeds the −6 ceiling. **The error shrinks with range but never
vanishes.** **[I]**

This is almost certainly a `WAngle` wrap: `1000` reads as "a big upward angle" but
`1000 ≡ −24 (mod 1024)`, i.e. −8.44°. A genuinely near-vertical ceiling would be
`MaximumLaunchAngle: 252` (`252 >> 2 = 63` ≈ 88.6°) or `256` (= 64 = 90°). **[S]** on the author's
intent; **[R]** on the arithmetic. **I have not made this change — read-only audit.**

### 4.3 A prediction I could not close, and which contradicts the reported symptom

Composing the above with the projectile's own tick loop gives a **falsifiable prediction**, which I
record rather than smooth over because it does **not** cleanly match what the user described:

The missile spawns at `pos = args.Source` = the soldier's `CenterPosition`, i.e. at terrain level
(§1.4). `HomingActivationDelay` defaults to 0 (`Missile.cs:129`), so `state = Homing` on tick 1
(`:911`). Initial `velocity` is built from the constructor's `vFacing = −6` (`:313-315`), and
`vFacing` thereafter only walks toward `desiredVFacing` at `Util.TickFacing(..., vRot)` (`:897`) with
`VerticalRateOfTurn = new WAngle(24)` → `.Facing = 24/4 =` **6** units/tick = 8.44°/tick. **[R]**
(Recording that `WAngle(24).Facing == 6`, not 24, since misreading exactly this constant is what
broke the previous report.)

At `MaximumLaunchSpeed: 20`, the first tick's vertical displacement is
`−20 × sin(8.44°) ≈ −2.9` w-units. `Missile.Tick:1050` sets `shouldExplode` when
`world.Map.DistanceAboveTerrain(pos).Length < 0` — "Hit the ground". `Explode` (`:1139-1157`)
**always** removes the projectile (`:1144`), and then returns without applying any warhead when
`ticks <= info.Arm`, with `Arm: 5` for MANPAD (`:1147`, `weapons-missiles.yaml:392`). **[R]**

**Prediction [I]: a MANPAD/Stinger missile fired by a ground unit on flat terrain is removed from the
world on tick 1 with no explosion, no damage and no effect — at any range.**

**This is consistent with "all missed" and with "the tracking mechanism didn't arm at all", but it is
NOT obviously consistent with "they just fired straight and kept flying in a straight line" — a
missile removed on tick 1 should not be seen flying.** Something in my composition is therefore
either wrong or incomplete. The candidates I could not eliminate statically are listed in §6.
**This is precisely the boundary where the two halves of the audit must be reconciled, and I am
deliberately not resolving it from my side.**

### 4.4 Consequence for the user's ruling

The user's ruling — *"Should have the same hit chance regardless of distance… as long as the weapon
can fire the missile should be able to hit"* — rules out "make the launcher refuse the shot". My
audit supports that ruling on its own terms: **fire control is not where the range sensitivity lives.**
Fire control's handoff is range-invariant in every respect that matters (§2). Adding a `MinRange` to
MANPAD would suppress the symptom at close range while leaving the launch-angle clamp wrong at
*every* range, and would therefore be treating the wrong end of the chain. **[I]**

---

## 5. Ranked defect list

**D1 — `MaximumLaunchAngle: 1000` on `MANPAD` and `Stinger` wraps to −8.44°, forcing every launch
below the horizon.** `weapons-missiles.yaml:385` and `:423`; arithmetic in §4.1.
*Severity: critical.* *Confidence: arithmetic **[R]**, behavioural consequence **[I]**, exact
in-flight outcome unresolved (§4.3).* These are the **only two weapons in the entire mod that set the
field** (verified by exhaustive grep of `mods/`), and they are precisely the two anti-air missiles —
the weapons that most need an upward launch. Every other missile inherits the sane `+45°` default.

**D2 — Nothing in fire control represents elevation; `ProjectileArgs` has no vertical-angle field.**
`ProjectileArgs` in `engine/OpenRA.Game/GameRules/WeaponInfo.cs` carries a single `WAngle Facing`.
`Target.IsInRange` is 2D (`Target.cs:196-203`), `WVec.Yaw` discards Z (`WVec.cs:66-76`),
`TargetInFiringArc` returns `true` unconditionally for a target directly overhead
(`AttackBase.cs:252`), and `MuzzleOrientation` is yaw-only (`Armament.cs:696-699`).
*Severity: architectural.* *Confidence: **[R]**.* This is not a bug on its own — it is the reason D1
cannot be caught anywhere upstream, and the reason a "can this shot physically succeed?" gate does not
exist to be fixed.

**D3 — Missile weapons silently receive no lead and no movement inaccuracy.** `Armament.cs:480` gates
the entire lead/`MovementInaccuracy` block on `Weapon.Projectile is BulletInfo`.
*Severity: medium.* *Confidence: **[R]**.* Correct-by-design for a homing missile, but it means
`ArmamentInfo.MovementInaccuracy` (default 30, `Armament.cs:48`) is **dead config on every missile
armament**, and `PassiveTarget` is handed over 3 ticks stale with no compensation — 16.9° of angular
staleness at 2 cells versus 2.7° at 15 cells (§2b). Whether that matters depends entirely on whether
the projectile prefers `GuidedTarget`; **sister worker's call**.

**D4 — `FiringLOS.GetGroundShadowDensity` lacks the `IndirectFire` early-out that `HasClearLOS` has.**
`FiringLOS.cs:49-51` versus `:127-165`. *Severity: low (latent).* *Confidence: **[R]** for the
divergence, **[I]** for "currently unexploited".* An `IndirectFire` unit with a
`MissChancePerDensity` weapon would skip the density gate and then take an unbounded miss roll capable
of reaching the 95% cap. No shipped unit has both (§3).

**D5 — `AimInitialTargetPosition` is accumulate-only on the missile path.** Appended at
`Armament.cs:394` on every shot; drained only inside the bullet branch (`:487`, `:501`). Bounded in
practice by `StoppedAiming` (`:224`) and target change (`:352`). *Severity: cosmetic.*
*Confidence: **[R]**.*

**D6 — `FiringLOS.GetBestThreshold`'s doc comment contradicts its code.** The summary says
"best (lowest) ClearSightThreshold" (`FiringLOS.cs:168`) but the loop takes the **maximum**
(`:186-187`). The code matches the intent documented at `Armament.cs:359-363` ("most permissive
threshold across all armaments"), so **the comment is wrong, not the code**. Also: a unit with no
armament valid against the target returns `0` — the strictest possible threshold — rather than a
"no opinion" value. *Severity: documentation + a small edge case.* *Confidence: **[R]**.*

**Explicitly NOT defects, checked and cleared:**
- The duplicated `^AutoTargetAir` key in `defaults.yaml` (`:502` and `:654`) is intentional, merges as
  designed, and is documented in-file at `:656-658`. **[R]**
- Foliage/`MissChancePerDensity` plays no part in the AA-soldier bug — MANPAD leaves it at 0. **[R]**
- `MinRange` gating is not involved — MANPAD's `MinRange` is zero, and both enforcement sites
  (`Armament.cs:334`, `Attack.cs:266`) short-circuit on `!= WDist.Zero` / `.Length != 0`. **[R]**
- `AutoTarget` preemption cannot fire on `^AA` (single priority band). **[I]**

---

## 6. What I could NOT settle statically

1. **Whether a MANPAD missile actually reaches the ground on tick 1** (§4.3). This needs the
   projectile's `Tick` movement order and the exact value of
   `Map.DistanceAboveTerrain(soldier.CenterPosition)` for infantry on flat ground. I did not verify
   that a ground actor's `CenterPosition.Z` equals the terrain height exactly (as opposed to carrying
   any positive offset), and terrain-height quantisation could plausibly absorb a −2.9 w-unit dip.
   **This single fact decides whether D1 is instantly fatal or merely a large initial aim error.**
2. **Whether the in-flight homing prefers `GuidedTarget` over the stale `PassiveTarget`,** and
   therefore whether D3's 16.9°-at-2-cells staleness has any effect. Sister worker's half.
3. **Whether the observed missiles were MANPAD at all.** The user's description ("kept flying in a
   straight line") is in tension with my prediction; I cannot rule out that the observed projectiles
   came from a different armament, or that `FlyStraightIfMiss` (default **true**, `Missile.cs:70`)
   produced the straight-line appearance after an early homing failure. **[S]**
4. **The actual terrain height delta in the reported incident.** If the soldiers stood below the
   helicopter on sloped ground the elevation figures in §2 change, and `TerrainHeightAware` being
   false for MANPAD means the missile does no incline lookahead at all.
5. **The achievable ShadowLayer density distribution on real maps.** I bounded the *effective* miss
   percentage by the `ClearSightThreshold` gate (§3), which is airtight, but I did not read what
   writes `ShadowLayer` (it is produced under `engine/OpenRA.Game/Map/Map.cs` and
   `RegenShadowsCommand.cs`) and so cannot say how often density 2 or 3 actually occurs, nor what
   the airborne channel's realistic ceiling is.
6. **Whether `Stinger`'s users are affected identically.** I confirmed the weapon carries the same
   `MaximumLaunchAngle: 1000` but did not enumerate which actors mount it.

---

## 7. Handoff to the sister worker (projectile half)

The one question that decides everything: **starting from `vFacing = −6` (8.44° below horizontal) at
`pos = args.Source` with `pos` at terrain level and launch speed 20 w-units/tick, does a MANPAD
missile (a) get removed by the `height.Length < 0` check at `Missile.cs:1050` before `Arm: 5` elapses
and thus vanish without a warhead, or (b) recover via `Util.TickFacing` at 6 facing units/tick
(`:897`) and fly?**

If (a): D1 is the whole bug, it is range-independent, and it matches the user's ruling exactly — the
weapon is allowed to fire but the missile is not physically able to hit, at any range.
If (b): D1 is a large initial aim error whose recovery distance is what makes close range worse than
long range, and D3's stale `PassiveTarget` becomes the second half of the story.

**If your half concludes the projectile is fine as configured, we disagree, and the disagreement is
about `Missile.cs:433-434` reading `MaximumLaunchAngle: 1000` as −6 rather than as an upward
ceiling — check that arithmetic first.**
