# Audit W5 — How a missile ENDS, and what damage it does

**Scope:** every termination path in `Missile.cs`; whether a missile can vanish silently; what
warheads actually deliver on hit vs near-miss.

**Ref:** `main @ dc899995`, `git status -sb` = `## main...origin/main`, 0 commits behind upstream.
Working tree carries one unrelated modification (`WORKSPACE/closeout/missiles-e2475f8d.md`) and one
untracked temp file; neither touches engine or mod rules. Static reading only — no build, no test
run, no game launch.

**Evidence tags** used throughout:
- **[R]** read directly from code, with `file:line`.
- **[I]** inferred by composing two or more read facts. The composition is shown.
- **[S]** speculation. Not acted on.

`WORKSPACE/missile-diagnosis.md` was not used as a source.

---

## 0. Constants, established first

The prior report's central arithmetic error was reading `WAngle` YAML values as facing units. Pinning
this down before anything else:

| Fact | Source |
|---|---|
| `WAngle.Facing => Angle / 4` | `engine/OpenRA.Game/WAngle.cs:67` |
| A bare integer in YAML for a `WAngle` field loads as the **raw angle** (1024 = full circle) | `engine/OpenRA.Game/FieldLoader.cs:250-253` |
| `Util.ApplyPercentageModifiers(n, ps)` = `(int)(n * Π(p/100))`, decimal math, truncated once at the end | `engine/OpenRA.Mods.Common/Util.cs:238-246` |
| `int2.Lerp(a,b,mul,div)` = `a + (b-a)*mul/div`, C# integer division (truncates toward zero) | `engine/OpenRA.Game/Primitives/int2.cs:71-74` |
| `Exts.Clamp(val,min,max)`: `val<min→min`, `val>max→max`, else `val` | `engine/OpenRA.Game/Exts.cs:65-73` |

So `MissileInfo.VerticalRateOfTurn = new(24)` (`Missile.cs:102`) is **6 facing units/tick**, not 24.
`ATGM`'s `HorizontalRateOfTurn: 20` is **5**. `WGM`'s `8` is **2**. `Hellfire`'s `60` is **15**. **[R]**

**`LoopRadius(speed, rot) = speed * 6400 / (157 * rot)`** (`Missile.cs:349`), `rot` =
`VerticalRateOfTurn.Facing` = 6 for every missile in the mod (no weapon overrides
`VerticalRateOfTurn`; read across all of `weapons-missiles.yaml`). **[R]**

- ATGM @ Speed 300 → `300*6400/(157*6)` = `1920000/942` = **2038**; `3*loopRadius` = 6114 ≈ 5.97 cells.
- Hellfire @ Speed 500 → `3200000/942` = **3397**; `3*loopRadius` = 10191 ≈ 9.95 cells.

### The single most load-bearing fact in this audit

`mods/ww3mod/mod.yaml:320-322` sets `MapGrid: Type: Rectangular`. Therefore:

```
Map.CenterOfCell(cell)      → new WPos(1024*x+512, 1024*y+512, 0)   // Z is ALWAYS 0
Map.DistanceAboveTerrain(p) → new WDist(p.Z)                        // literally the raw Z
```
`engine/OpenRA.Game/Map/Map.cs:1425-1426` and `:1464-1465`. **[R]**

`MapGrid.SubCellOffsets` all carry `Z = 0` (`engine/OpenRA.Game/Map/MapGrid.cs:117-125`). **[R]**

**Consequences, all [I] from the above:**
1. "The ground" is the global plane `Z = 0`. There is no terrain height in the collision test.
2. Every ground actor's `CenterPosition.Z` is exactly **0**.
3. `Map.Height[cell]` *is* still read — by `InclineLookahead` (`Missile.cs:560`) — but it feeds only
   `TerrainHeightAware` guidance, never `DistanceAboveTerrain`. **The height field and the collision
   plane disagree.** On a rectangular-grid map, `TerrainHeightAware: true` (set on `WGM`, `Ataka`,
   `Hellfire`) makes the missile climb over cliffs that do not exist as far as the ground check is
   concerned. Flagged as defect **D6** below.

---

## A. Exhaustive termination-path table

**The missile is removed from the world in exactly one place:** `world.AddFrameEndTask(w =>
w.Remove(this))` at `Missile.cs:1144`, inside `Explode()`. There is no other `Remove` in the file,
and `IProjectile`/`IEffect` has no other self-removal mechanism. **There is no path that removes a
missile without calling `Explode()`.** **[R]**

`Explode()` has exactly two call sites: `Missile.cs:866` (jam) and `Missile.cs:1096` (`shouldExplode`).

| # | Condition | Line | Detonation position | Warhead applied? |
|---|---|---|---|---|
| **T1** | `Blockable` and a `BlocksProjectiles` actor lies between `lastPos` and `pos` | `1029-1033` | `blockedPos` | yes, unless `ticks<=Arm` |
| **T2** | `height.Length < 0` — below the Z=0 plane | `1050` | post-move `pos` | yes, unless `ticks<=Arm` |
| **T3** | `relTarDist < CloseEnough` — proximity to the *offset, lead-corrected* aim point | `1051` | post-move `pos` | yes, unless `ticks<=Arm` |
| **T4** | `ExplodeWhenEmpty && rangeLimit>=0 && distanceCovered > rangeLimit` — fuel out | `1052` | post-move `pos` | yes, unless `ticks<=Arm` |
| **T5** | `!world.Map.Contains(cell)` — left the map | `1053` | post-move `pos` | yes, unless `ticks<=Arm` |
| **T6** | `BoundToTerrainType` set and current terrain differs | `1054` | post-move `pos` | yes, unless `ticks<=Arm` |
| **T7** | Airburst: `!flyStraight && height < AirburstAltitude && relTarHorDist < CloseEnough` | `1059` | post-move `pos` | yes, unless `ticks<=Arm` |
| **T8** | Segment closest-approach to `targetPosition+leadTarget` < `CloseEnough`; gated `!shouldExplode && state != Freefall` | `1070-1092` | `closestPos` (snapped onto the move segment) | yes, unless `ticks<=Arm` |
| **T9** | Jammed by `JamsMissiles` with `ActiveProtection` | `866` | *pre*-move `pos` | yes — **and execution continues**, see D7 |

Notes on the table, all **[R]**:

- **T6 is dead content.** No weapon in `mods/ww3mod/rules/weapons/` sets `BoundToTerrainType`.
- **T9 is dormant.** The only `JamsMissiles` in the mod is commented out
  (`vehicles-america.yaml:491`).
- **`Freefall` is not a termination.** `state = States.Freefall` is set at `Missile.cs:921-927` when
  fuel runs out. But `ExplodeWhenEmpty` defaults `true` (`Missile.cs:120`) and **no missile in the
  mod sets it false** — several set it explicitly true. Line 921 (set Freefall) and line 1052 (fuel-out
  explode) fire on the *same tick*, so a missile spends exactly one tick in `Freefall` and then
  detonates. T8's `state != Freefall` gate is therefore only ever suppressed on that single terminal
  tick. **[I]**
- **A missile can never fly forever.** With `ExplodeWhenEmpty` true everywhere, T4 always fires.
  Even hypothetically with it false, `FreefallTick` applies gravity (`Missile.cs:519-529`) so T2
  follows. `RangeLimit` is never negative in the mod, and defaults to weapon `Range` when zero
  (`Missile.cs:288`). **[I]**
- **`ManualGuidance` does NOT set `state = Freefall`.** `Missile.cs:1013` selects `FreefallTick()`
  when `info.ManualGuidance && args.SourceActor.IsDead`, but leaves `state` alone. So T8 still runs
  for a guidance-lost missile. See D5 for the separate bug this exposes.

### The one no-warhead path

```csharp
void Explode(World world) {
    if (info.ContrailLength > 0) ... ContrailFader ...
    world.AddFrameEndTask(w => w.Remove(this));   // :1144  — ALWAYS removes
    if (ticks <= info.Arm) return;                // :1147  — NO warhead, NO effect, NO sound
    ...
    args.Weapon.Impact(Target.FromPos(pos), warheadArgs);   // :1156
}
```

`ticks++` is the first statement of `Tick` (`Missile.cs:907`), and both `Explode()` call sites are
inside `Tick`, so the minimum `ticks` at detonation is **1**. `Arm: 0` (the default,
`Missile.cs:66`) is therefore a complete no-op — `1 <= 0` is false. **[R]**

The early return precedes `args.Weapon.Impact`, so it suppresses **every** warhead including the
`CreateEffect` ones. This is a genuinely, totally silent removal. **[R]**

---

## B. Verdict — can a missile vanish without exploding?

**Yes, by exactly one path (`Missile.cs:1147`) — but that path is very close to unreachable on the
shipped content, and it is NOT what the user is seeing.**

### B.1 Which weapons even have `Arm > 0`

Read across `weapons-missiles.yaml`: `WGM` (`:67`) and `Ataka` (`:131`) set `Arm: 2`;
`SurfaceToAirMissile` (`:325`), `MANPAD` (`:392`) and `Stinger` (`:425`) set `Arm: 5`. Everything
else — `ATGM`, `Hellfire`, `AirToAirMissile`, `TimerWolf_Missiles` — takes the default 0 and can
never be silently removed at all. `Stinger.quad` and `9M311` inherit `Stinger`; `WGM.bradley`
inherits `WGM`; `SurfaceToAirMissile.double` inherits its parent. **[R]**

### B.2 Reachability of each termination inside the arming window

| Path | Reachable at `ticks <= Arm`? | Why |
|---|---|---|
| T1 blocking | **No** | All five `Arm>0` weapons set `Blockable: false` (`:50, :117, :326, :393, :426`) **[R]** |
| T2 ground | **No** — see B.3 | Requires `pos.Z < 0`; proven below it cannot go negative |
| T3 proximity | **Narrowly yes** | `WGM`/`Ataka` have `MinRange: 3c0` = 3072 ≫ `CloseEnough` 298 → unreachable. The three AA weapons declare **no `MinRange`**, so a target already inside `CloseEnough` (400 / 192 / 256) at launch trips this on tick 1 **[R]** |
| T4 fuel-out | **No** | 5 ticks covers a few hundred w-units against a `RangeLimit` of 24c0–35c0 **[I]** |
| T5 off-map | **Marginally yes** | Only when firing outward from within a few hundred w-units of the map edge **[I]** |
| T6 terrain | **No** | Never configured **[R]** |
| T7 airburst | **No** | All five leave `AirburstAltitude` at 0, which reduces T7 to `height < 0`, i.e. T2 **[R]** |
| T8 segment | **Narrowly yes** | Same geometry as T3 **[I]** |
| T9 jam | **No** | Dormant **[R]** |

### B.3 The handoff question, settled: the launch clamp is NOT fatal

The handoff (via W3) is right about the clamp and right about the arithmetic, and I confirm both:

`MANPAD` (`weapons-missiles.yaml:385`) and `Stinger` (`:423`) are the only two weapons in the mod
that set `MaximumLaunchAngle`, both to `1000`. `1000 >> 2` = 250; `(sbyte)250` = **−6**. Default
`MinimumLaunchAngle = WAngle(-64)` → `(sbyte)(−16)`. `Missile.cs:433-434` therefore evaluates
`Clamp(vFacing, −16, −6)`, a band **entirely below the horizon** (−22.5° to −8.44°). `Exts.Clamp`
does not throw here because `min < max`. **[R] — confirmed independently.**

I also confirm the spawn height. `^AA`'s `Armament@1` (`infantry.yaml:1769-1772`) declares no
`LocalOffset` and no `Recoil`. With `info.LocalOffset` empty, `Armament` builds a single barrel at
`Offset = WVec.Zero` (`Armament.cs:52`, `:210-211`); `CalculateMuzzleOffset` then rotates zero and
returns zero (`Armament.cs:676-689`), so `Source = MuzzlePosition() = self.CenterPosition` exactly
(`Armament.cs:411`, `:446`). Combined with §0, **`args.Source.Z == 0` exactly, and
`DistanceAboveTerrain(pos) == pos.Z`.** **[R] + [I]**

**But the predicted tick-1 descent never happens.** The launch `vFacing` of −6 is used for two things
only: to build `velocity` (`Missile.cs:313-315`, which is read *only* by `FreefallTick`), and as the
*starting value* for the per-tick turn. The first actual displacement is built from the **post-turn**
`vFacing`, because `Missile.cs:897` executes before `Missile.cs:899`:

```csharp
vFacing = Util.TickFacing(vFacing, desiredVFacing, vRot);          // :897
return new WVec(0, -1024 * speed, 0)
    .Rotate(new WRot(WAngle.FromFacing(vFacing), ...))             // :899-902  ← uses the NEW vFacing
```

Tracing tick 1 for a MANPAD at `pos.Z = 0` against an airborne target: `state` becomes `Homing` and
`speed = velocity.Length = 20` at `Missile.cs:911-918`; `loopRadius = LoopRadius(20,6)` = 135, so
`3*loopRadius` = 405, and any real AA engagement has `relTarHorDist ≫ 405`. Control therefore falls
to the cruise-altitude branch at `Missile.cs:798-812`, where with `diffClfMslHgt = 0` and
`CruiseAltitude = 512` (default, `Missile.cs:126`):

```
vDist = new WVec(-0 - 512, -20, 0) = (-512, -20, 0)
desiredVFacing = vDist.Yaw.Facing = 62   →  Clamp(62, -6, +6) = +6
```

`Util.TickFacing(-6, +6, 6)` (`Util.cs:30-45`, modular in `& 0xFF`): `leftTurn = (−6−6)&0xFF = 244`,
`rightTurn = (6−(−6))&0xFF = 12`; neither is `< 6`; `rightTurn < leftTurn` so it returns
`(−6+6)&0xFF` = **0**. With `vFacing = 0` the rotation contributes **no Z**, so `move.Z = 0`,
`pos.Z` stays 0, and the check at `Missile.cs:1050` is `0 < 0` — **false**. **[R], every step.**

Tick 2: `TickFacing(0, +6, 6)` = **+6**, and it holds there — the missile climbs. It never goes
below Z=0.

> **Verdict on handoff Q1: the clamp is a bad aim angle, not a self-destruct.** W3's ~2.9 w-unit
> figure is arithmetically correct (`20 * sin(8.44°) ≈ 2.93`) but describes a displacement that is
> never applied, because `vFacing` is corrected to 0 *before* the move vector is constructed. Two
> independent facts each block the vanish on their own: the Z never goes negative, and the check is
> strict `< 0` rather than `<= 0`.

**Handoff Q2 — recovery time and distance:** one tick, with **zero** altitude loss. The cost is one
tick of *foregone climb* (≈ 6 w-units at MANPAD's tick-2 speed), not a descent. Over a 23-cell
engagement this is negligible. The `MaximumLaunchAngle: 1000` bug is real but its actual effect is
different from the predicted one — see **D2**.

**Handoff Q4 — expected frequency of the pre-`Arm` return:** I checked the muzzle Z of every launcher
firing an `Arm>0` weapon. All the vehicle- and structure-mounted ones spawn *above* the ground plane:
`WGM.bradley` `LocalOffset: 100,90,90` (`vehicles-america.yaml:381`), `Stinger.quad` `100,90,90`
(`:895`), `WGM` on bmp2 `100,90,90` (`vehicles-russia.yaml:217`), `9M311` on tunguska
`500,240,120 / 500,-240,120` (`:856`), `SurfaceToAirMissile.double` on the SAM site `0,0,320`
(`structures-defenses.yaml:801`). The **only** launcher in the mod that spawns a missile at exactly
`Z = 0` is the AA Specialist with MANPAD — and §B.3 proves it does not go negative. **[R]**

> **Predict the Phase 0 trace will show zero or near-zero pre-`Arm` terminations.** The only live
> window is T3/T8 with an aircraft already inside 192–400 w-units (0.19–0.39 cells) of an AA launcher
> at the instant of fire. If the trace shows many, the premise most likely to be wrong is my
> reachability analysis of T5 (off-map) — I did not check how close AI units actually fire from the
> map edge.

### B.4 What IS happening — the three cases, separated

The brief asks to distinguish "no explosion happened" / "explosion happened out of view" /
"explosion happened but applied no damage." All three exist, and the dominant one is a **fourth**:
*the explosion happened, in view, and rendered nothing.*

`CreateEffectWarhead.DoImpact` (`CreateEffectWarhead.cs:105-146`) draws nothing when
`ActorTypeAtImpact == None` **and** `IsValidAgainstTerrain` is false. `IsValidAgainstTerrain`
(`:149-157`) resolves the impact to the target type `Air` whenever
`DistanceAboveTerrain(pos) > AirThreshold`, and `Warhead.AirThreshold` defaults to **128**
(`Warhead.cs:45`). **[R]**

The mod's shared explosion template `^MediumExplosionEffects` declares
`Warhead@Effect: CreateEffect` with `ValidTargets: Ground, Ship, Trees, Mine`
(`weapons-effects.yaml:553-556`) — no `Air`. **[R]**

So: **any detonation more than 128 w-units above the Z=0 plane, not inside a ground actor's hitshape,
draws no sprite and plays no sound.** `Hellfire` (`:232-236`), `Ataka` (`:163-167`),
`TimerWolf_Missiles` (`:278-282`) each add a `Warhead@EffectAir` and are covered; the AA missiles
inherit `^MediumExplosionEffectsAir`, which carries an `Air, ICBM` effect
(`weapons-effects.yaml:702-709`), and are covered.

**`ATGM` and `WGM` have no air-valid effect warhead at all.** Read in full: `ATGM` at
`weapons-missiles.yaml:2-32` and `WGM` at `:34-91` declare only `Warhead@Target` and `Warhead@Spread`
on top of `^MediumExplosionEffects`. **[R]**

`ATGM` is the AT Specialist's Javelin (`infantry.yaml:1701`); `WGM`/`WGM.bradley` arm the Bradley
(`vehicles-america.yaml:379`) and the BMP-2 (`vehicles-russia.yaml:215`). **That is the entire
ground ATGM layer on both sides, and every one of its airborne detonations is completely invisible
and silent.** **[I], composed from the four [R] facts above.**

> **This is the defect behind "plenty of missiles miss and never explode."** The missile always
> explodes — T1–T9 are exhaustive and `Explode()` always runs — but on a near-miss the sprite is
> suppressed, the sound is suppressed, and (§C) the surviving splash delivers 0.01%–0.26% of a
> tank's HP. Invisible, inaudible, and materially harmless is indistinguishable from "vanished."

**Handoff Q3 — does `FlyStraightIfMiss` explain "fired straight and kept flying"?** **Yes, and it is
the reading that reconciles both halves of the user's account.** `FlyStraightIfMiss` defaults true
(`Missile.cs:69`) and no weapon in the mod overrides it. At `Missile.cs:839-840`, once the missile is
in `Hitting` state and its distance has grown past `minDistanceToTarget + CloseEnough`, `flyStraight`
latches. From then on `desiredHFacing = hFacing` and `desiredVFacing = vFacing` (`:847-850`) — the
missile holds its heading *exactly* — while `:858-859` keeps accelerating it to `maxSpeed`. Latching
also disables the airburst fuse (`:1059` is gated on `!flyStraight`). The missile then flies dead
straight at top speed until T4 fuel-out, which for `ATGM` (`RangeLimit: 21c0`) and `WGM`
(`25c0`) is a long way downrange — and detonates there, invisibly if it is above 128. **[R]**

So the user's two observations are one mechanism: overshoot → `flyStraight` latches → long straight
run → terminal detonation that renders nothing.

---

## C. Warhead application and the damage numbers

### C.1 What `Target.FromPos` means downstream

`Missile.Explode` calls `args.Weapon.Impact(Target.FromPos(pos), warheadArgs)` (`Missile.cs:1156`).
`Target.FromPos` builds a `TargetType.Terrain` target (`engine/OpenRA.Game/Traits/Target.cs:85`).
`WeaponInfo.Impact` (`WeaponInfo.cs:274-288`) applies **every** warhead with no weapon-level
validity gate — `IsValidAgainst` is never consulted here. **[R]**

In `DamageWarhead.DoImpact(in Target, WarheadArgs)` (`DamageWarhead.cs:65-94`) a `Terrain` target is
neither `Actor` nor `Invalid`, so it falls to the position overload `DoImpact(pos, ...)`. Both
`TargetDamage` and `SpreadDamage` therefore run their area search. **No missile ever takes the
single-actor branch.** **[R]**

Warhead-level `ValidTargets` is then checked against each **victim actor's** target types
(`Warhead.cs:64-78`), not against the terrain. So a `SpreadDamage` with the default
`ValidTargets: Ground, Water` *does* damage an Abrams (`Targetable: TargetTypes: Ground, Vehicle,
Heavy`, `vehicles-america.yaml:489-490`) even when the detonation is high in the air. **Damage and
visuals are gated differently** — the damage lands, the explosion does not. **[I]**

### C.2 `TargetDamage` — the 1-w-unit gate, and a second cliff inside the hull

`TargetDamageWarhead.Spread` defaults to `new WDist(1)` (`TargetDamageWarhead.cs:24`), and **none of
`ATGM`, `WGM`, `Ataka` or `Hellfire` overrides it** (read in full at `weapons-missiles.yaml:25-28`,
`:83-86`, `:139-143`, `:203-207`). The gate is `if (closestDistance > Spread.Length) continue`
(`:64-65`), where `closestDistance` is `HitShape.DistanceFromEdge`. So the detonation must be inside
the hitshape or within **1/1024 of a cell** of it. **[R]**

Two properties of `RectangleShape` matter:

1. **`DistanceFromEdge` is horizontal-only.** `Rectangle.cs:108-115` forces `r.Z = 0` and returns
   `r.HorizontalLength`. Altitude is discarded. A missile detonating directly above a tank at any
   height still reads distance 0. (`CircleShape` by contrast uses `v.Length`, fully 3-D —
   `Circle.cs:45-48`. Rectangles and circles disagree about whether altitude counts.) **[R]**
2. **`PercentFromEdge` is measured from the CORNER, not the edge.** `Rectangle.cs:117-121`:
   `total = |(quadrantSize.X, quadrantSize.Y)|`, and it returns `100*(total − dist_from_centre)/total`
   — despite the parameter being named `fromEdge`, callers pass the raw relative position
   (`Rectangle.cs:135-137`). **[R]**

For the **Abrams** (`vehicles-america.yaml:478-488`: HP 28000, Armor Heavy Thickness 700,
Distribution `100,40,15,10,10`, HitShape Rectangle `TopLeft -365,-790` / `BottomRight 365,790`,
`VerticalTopOffset 480`):

`quadrantSize = (365, 790)`; `total = isqrt(365² + 790²) = isqrt(757325) = **870**`.

| Impact point (inside the hull) | dist from centre | `PercentFromEdge` |
|---|---|---|
| dead centre | 0 | **100** |
| mid-side (±365, 0) | 365 | `100*(870−365)/870` = **58** |
| nose / tail (0, ±790) | 790 | `100*(870−790)/870` = **9** |
| corner (365, 790) | 870 | **0** |

**A visually perfect hit on the Abrams' nose delivers 9% of `TargetDamage`. A corner hit delivers
0%.** That is an 11×–∞ swing *within what the player sees as a direct hit*, before any near-miss is
involved. **[I]** — this is defect **D3**.

### C.3 `Penetration` vs `Thickness`

`DamageWarhead.InflictDamage` (`DamageWarhead.cs:200-244`):

```csharp
var thickness = victim.Trait<Armor>().Info.Thickness;      // :216
if (thickness != 0) {
    var armorPercent = ArmorDirectionPercent(victim, shape, args);   // :219
    thickness = thickness * armorPercent / 100;                      // :220
    var diff = Penetration - thickness;
    if (diff < 0) damage = damage * Penetration / thickness;         // :229
}
```

So penetration is **binary-then-linear**: at or above the facet thickness, full damage; below it,
damage scales as `Pen/Thickness` with no floor. `armorPercent` of 0 would zero the thickness, but the
`diff < 0` test then fails and no division occurs — no divide-by-zero, given `Penetration >= 0`. **[R]**

`Versus` is empty on every missile warhead in the mod, so `DamageVersus` returns 100 immediately
(`DamageWarhead.cs:101-102`) and contributes nothing. **[R]**

### C.4 Worked numbers — Hellfire vs Abrams

`Hellfire` (`weapons-missiles.yaml:169-236`): `TargetDamage` Damage 10000 / Pen 800;
`SpreadDamage` Spread 192 / Damage 2000 / Pen 20. No `TopAttack`, so `ArmorDirectionPercent` takes the
directional branch (`DamageWarhead.cs:140-194`).

`SpreadDamage` falloff: `Falloff = {100,37,14,5,0}` over `effectiveRange[i] = i*Spread`
(`SpreadDamageWarhead.cs:28`, `:52`) → `{0, 192, 384, 576, 768}`.

**A. Clean frontal centre hit.**
- `TargetDamage`: edge distance 0 ≤ 1 → passes. `PercentFromEdge` = 100. Frontal → `distribution[0]` =
  100 → thickness `700*100/100` = 700. `diff = 800 − 700 = +100 ≥ 0` → **no reduction**.
  `ApplyPercentageModifiers(10000, [100, 100])` = **10000**.
- `SpreadDamage`: falloff distance 0 → `GetDamageFalloff(0)` returns `Lerp(100,37,0,192)` = 100.
  `2000 * 20 / 700` = **57**. → 57.
- **Total ≈ 10057 = 35.9% of 28000 HP.**

**B. Near miss, 512 w-units (half a cell) from centre, broadside.**
- Edge distance = `512 − 365` = **147**.
- `TargetDamage`: `147 > 1` → `continue`. **ZERO.**
- `SpreadDamage`: `GetDamageFalloff(147)`: `i=1`, `outer=192 > 147` → `Lerp(100, 37, 147, 192)` =
  `100 + (−63*147)/192` = `100 + (−9261/192)` = `100 − 48` = **52**.
  Side facet → `distribution[1]` = 40 → thickness `700*40/100` = 280. `2000*20/280` = **142**.
  `ApplyPercentageModifiers(142, [52,100])` = `(int)(142 * 0.52)` = **73**.
- **Total ≈ 73 = 0.26% of HP.**

> **Hellfire hit-vs-half-cell-miss ratio: 10057 / 73 ≈ 138×.**

### C.5 Worked numbers — ATGM (the Javelin) vs Abrams

`ATGM` (`weapons-missiles.yaml:2-32`): `TopAttack: true` (`:6`); `TargetDamage` Damage 10000 / Pen 100;
`SpreadDamage` Spread 64 / Damage 2000 / **Pen unset → default 1** (`DamageWarhead.cs:24`).
Ranges `{0, 64, 128, 192, 256}`.

**A. Clean centre hit, TopAttack.** `distribution[3]` = **10** → thickness `700*10/100` = **70**.
`diff = 100 − 70 = +30 ≥ 0` → no reduction. `TargetDamage` = **10000**.
`SpreadDamage`: `2000*1/70` = 28, falloff 100 → **28**. **Total ≈ 10028 = 35.8% of HP.**

**B. Near miss, 512 from centre, broadside.** Edge distance 147.
- `TargetDamage`: **ZERO**.
- `SpreadDamage`: `GetDamageFalloff(147)` walks past `outer=64` and `outer=128`, then `i=3`,
  `outer=192 > 147` → `Lerp(14, 5, 147−128, 192−128)` = `14 + (−9*19)/64` = `14 + (−171/64)` =
  `14 − 2` = **12**. TopAttack still applies (it is a weapon-level flag) → thickness 70 →
  `2000*1/70` = 28 → `(int)(28 * 0.12)` = **3**.
- **Total ≈ 3 damage = 0.011% of HP.**

> **ATGM hit-vs-half-cell-miss ratio: 10028 / 3 ≈ 3300×.** A half-cell miss with the Javelin is
> functionally a dud — and it is invisible and silent (§B.4).

**And a half-cell miss is the expected case for ATGM.** `Inaccuracy: 512` with the default
`InaccuracyType: Absolute` (`Missile.cs:87`) gives
`offset = WVec.FromPDF(rng, 2) * 512 / 1024` (`Missile.cs:307-308`).
`WDist.FromPDF(r, 2)` averages two uniform draws on `[−1024, 1024)`
(`engine/OpenRA.Game/WDist.cs:56-60`), i.e. a triangular distribution — so each of X and Y is
triangular on `[−512, 512]` with σ ≈ 512/√6 ≈ 209, and Z is always 0 (`WVec.cs:105-107`). The
Abrams' half-width is 365. **[I]**

### C.6 Same numbers for the T-90

`vehicles-russia.yaml:314-324`: HP 24000, Thickness 280, Distribution `100,60,40,15,15`, Rectangle
`-400,-950 / 400,950`. `quadrantSize = (400,950)`, `total = isqrt(1062500)` = **1030**.

- ATGM TopAttack: `distribution[3]` = 15 → thickness `280*15/100` = **42**; Pen 100 → `diff = +58` →
  **10000** (41.7% of HP).
- Hellfire frontal: thickness 280, Pen 800 → `diff = +520` → **10000**.
- Nose hit `PercentFromEdge` = `100*(1030−950)/1030` = **7** → 700 damage.

---

## D. Does `TopAttack` actually function? — **Yes.**

`ArmorDirectionPercent` returns `distribution[3]` for `TopAttack` **only if** `distribution.Length == 5`
(`DamageWarhead.cs:129-134`); otherwise it falls through to a flat 100 (`:197`). And it is only
reached at all when `Thickness != 0` (`:217-219`). Both preconditions hold for the relevant actors:

- **Every combat vehicle declares a 5-element `Distribution`.** Verified by direct read, not by the
  subagent's summary: `^Vehicle` supplies `Distribution: 100,50,25,10,10` (`vehicles.yaml:21-22`,
  no `Type`, no `Thickness`); `abrams` `100,40,15,10,10` + Thickness 700
  (`vehicles-america.yaml:480-483`); `t90` `100,60,40,15,15` + Thickness 280
  (`vehicles-russia.yaml:316-319`); `bradley`, `bmp2`, `humvee`, `m113`, `btr`, `m109`, `m270`,
  `shilka`, `tunguska`, `strykershorad`, `grad`, `giatsint` all 5-element with nonzero Thickness. **[R]**
- **No actor declares more than one `Armor` trait.** Every declaration in the mod uses the bare key
  `Armor:` (grep across `mods/ww3mod/`), so MiniYaml *merges* a child's values into the parent's
  single trait rather than creating a second one. The potential mismatch between `victim.Trait<Armor>()`
  at `DamageWarhead.cs:216` and `TraitsImplementing<Armor>().First(enabled)` at `:125-126` therefore
  cannot bite today. **[R]**
- **The AT Specialist really does fire the top-attack weapon.** `^AT`'s `Armament@1` is
  `Weapon: ATGM` (`infantry.yaml:1700-1701`), and `ATGM` sets `TopAttack: true`
  (`weapons-missiles.yaml:6`). **[R]**

**Magnitude, so "it functions" is not just a boolean:** ATGM `TargetDamage` (Pen 100) against an
Abrams delivers **10000** with TopAttack (thickness 70) versus **`10000*100/700` = 1428** frontally
without it — a **7.0× multiplier**. Against a T-90 (thickness 42 vs 280): **10000** vs
**`10000*100/280` = 3571**, a **2.8× multiplier**. TopAttack is doing real, large work. **[I]**

`^Infantry` declares `Armor: Type: None` with **no `Thickness`** (`infantry.yaml:34-35`), so
Thickness is 0, the whole penetration block at `DamageWarhead.cs:217` is skipped, and both
`Penetration` and `TopAttack` are irrelevant against infantry. That is coherent, not a bug. **[R]**

Other `TopAttack` weapons found by the sweep (`^ArtilleryRound`, `GradRockets`, `TosRockets`,
`M270Rockets`, all in `weapons-ballistics.yaml`; `ATMine` uses `BottomAttack`) are outside this
audit's scope and were not verified beyond their existence. **[S]** as to whether they behave as
intended.

---

## E. Ranked defect list

| # | Defect | Severity | Evidence |
|---|---|---|---|
| **D1** | **`ATGM` and `WGM` have no air-valid `CreateEffect`.** Every detonation above 128 w-units that is not inside a ground actor's hitshape draws no sprite and plays no sound. This covers the AT Specialist's Javelin and both IFVs' ATGM — the whole ground ATGM layer. Directly produces "missiles that never explode." | **Critical** | [R] `weapons-missiles.yaml:2-32`, `:34-91`; `weapons-effects.yaml:553-556`; `CreateEffectWarhead.cs:121-122`, `:149-157`; `Warhead.cs:45` |
| **D2** | **The near-miss damage cliff.** `TargetDamage` has a hard 1-w-unit gate and contributes exactly zero outside the hull; the fallback `SpreadDamage` is divided by armour thickness with `Penetration: 1` on ATGM. Half-cell miss = 3 damage on an Abrams vs 10028 for a hit — **~3300×** for ATGM, **~138×** for Hellfire. With `Inaccuracy: 512`, a half-cell miss is the *expected* outcome for ATGM. | **Critical** | [R]+[I] §C.4, §C.5 |
| **D3** | **`RectangleShape.PercentFromEdge` measures from the corner.** A hit on the Abrams' nose delivers 9% of `TargetDamage` (900), a corner hit 0%, a centre hit 100%. An 11×+ swing inside what the player sees as a clean hit. | **High** | [R] `Rectangle.cs:117-121`, `:135-137`; arithmetic §C.2 |
| **D4** | **`MaximumLaunchAngle: 1000` on MANPAD and Stinger** clamps launch pitch into `[−22.5°, −8.44°]` — the intended steep AA loft is silently disabled and these launch like flat-trajectory ATGMs. **Not** a self-destruct: costs one tick of foregone climb, zero altitude loss. | **Medium** | [R] `weapons-missiles.yaml:385`, `:423`; `Missile.cs:433-434`; `WAngle.cs:67`; `Util.cs:30-45`; §B.3 |
| **D5** | **`ManualGuidance` shooter-death reverts the missile to its *launch* velocity.** `velocity` is written only at `Missile.cs:313` (construction), `:924` (fuel-out) and inside `FreefallTick`. `HomingTick` never updates it. So when a `WGM`/`Ataka` launcher dies mid-flight, `FreefallTick` resumes from the launch vector — snapping speed from ~300 back to `MaximumLaunchSpeed` (50 / 80) and heading back to the original launch direction. | **Medium** | [R] `Missile.cs:313-315`, `:519-529`, `:924-926`, `:1013-1016`; `weapons-missiles.yaml:68`, `:128` |
| **D6** | **On a `Rectangular` grid, `Map.Height` and `DistanceAboveTerrain` disagree.** The ground is globally `Z=0`, but `InclineLookahead` still reads `Map.Height[cell]*512`. `TerrainHeightAware: true` (WGM, Ataka, Hellfire) therefore makes missiles climb over cliffs the collision test does not believe in. | **Medium** | [R] `mod.yaml:320-322`; `Map.cs:1425-1426`, `:1464-1465`; `Missile.cs:560` |
| **D7** | **The `ActiveProtection` jam path can double-detonate.** `Explode()` at `Missile.cs:866` is not followed by a return; `HomingTick` runs to completion, `Tick` moves the missile and may call `Explode()` again in the same tick — two full warhead applications and two `Remove` calls. Currently **dormant** (the only `JamsMissiles` is commented out at `vehicles-america.yaml:491`). | **Low (latent)** | [R] |
| **D8** | **`(sbyte)vDist.HorizontalLengthSquared != 0` is a broken zero-test.** Used at `Missile.cs:667`, `:763`, `:775`, `:787`, `:803`. Casting a squared length to `sbyte` takes it mod 256, so any length-squared that is a multiple of 256 tests as zero and the code silently falls back to `desiredVFacing = vFacing`. | **Low (latent)** | [R] |
| **D9** | **`victim.Trait<Armor>()` throws if the victim has no `Armor` trait**, and `TraitsImplementing<Armor>().First(enabled)` throws if all are disabled. A crash, not a silent vanish. I did **not** enumerate every damageable actor to confirm all have `Armor`. | **Unknown** | [R] `DamageWarhead.cs:216`, `:125-126`; `TraitDictionary.cs:158-165` |

**Not defects, checked and cleared:**
- No path removes a missile without calling `Explode()`. **[R]**
- `Arm: 0` is a true no-op; `ATGM` and `Hellfire` can never be silently removed. **[R]**
- No missile can fly forever — `ExplodeWhenEmpty` is true everywhere. **[R]**
- `TopAttack` functions, at 2.8×–7.0×. **[R]+[I]**
- `armorPercent == 0` cannot cause a divide-by-zero. **[R]**

---

## F. What I could NOT determine statically

1. **The Z of `pos` at `Explode()` — the single most important unknown.** D1's severity depends
   entirely on how often the terminal detonation sits above 128 w-units. That depends on the dive
   profile: `ATGM` has `CruiseAltitude: 10c0` (10240) and climbs at up to 8.44°/tick, then dives once
   `relTarHorDist <= 3*loopRadius` (6114). Whether it gets below 128 before T3/T8 trips is a function
   of the whole trajectory and cannot be resolved by reading. **Phase 0 must log
   `DistanceAboveTerrain(pos)` at every `Explode()`, bucketed `<0 / 0–128 / >128`.** If the `>128`
   bucket is large for ATGM and WGM, D1 is confirmed as the headline defect.
2. **Which termination path each detonation took.** T1–T9 are indistinguishable in play. Phase 0
   should tag each `Explode()` with its trigger, plus `ticks`, `info.Arm`, and whether the pre-`Arm`
   return fired.
3. **The real distribution of miss distances.** I can compute the inaccuracy PDF, but the detonation
   point is where the proximity check first trips along a discretised path, not the aim point.
   Phase 0 should log `(pos − victim.CenterPosition)` and the resulting `DistanceFromEdge`.
4. **How often `flyStraight` latches, and how far the missile then travels.** This is the mechanism
   behind the user's "kept flying in a straight line," but the trigger rate is trajectory-dependent.
   Log `flyStraight` transitions with the tick and the distance remaining.
5. **Whether every damageable actor has an `Armor` trait** (D9). I checked vehicles, aircraft and
   infantry templates but did not enumerate trees, walls, husks, bridges and civilian structures.
6. **Muzzle Z for launchers I did not read.** I verified every launcher firing an `Arm>0` weapon, but
   not the full fleet. If Phase 0 reports pre-`Arm` terminations, start here.
7. **Aircraft cruise altitudes**, needed to close the T3/T8 reachability question for AA missiles
   against a very low-flying target. Not read.
