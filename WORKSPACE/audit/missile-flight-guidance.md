# Missile flight & guidance — ground-truth audit (part 1 of 4)

**Audited ref:** `main @ dc899995` (`dc89999571dd7057e677e22145aed395419c303b`), 0 commits behind `origin/main`.
Working tree carried two unrelated dirty paths (`WORKSPACE/closeout/missiles-e2475f8d.md`, one untracked temp file); neither is in scope and nothing was staged, committed or edited outside this file.

**Scope:** `engine/OpenRA.Mods.Common/Projectiles/Missile.cs`, flight & steering half. Static reading only — no build, no test run, no game launch.

**Provenance of every claim is tagged:**

| Tag | Meaning |
|---|---|
| **[R]** | Read directly from code/YAML at the cited `file:line`. |
| **[A]** | Arithmetic on **[R]** constants. The arithmetic is shown so it can be checked. |
| **[I]** | Inferred — a multi-step consequence of **[R]**/**[A]** that I did not observe executing. |
| **[S]** | Speculation — plausible, not established. Treat as a hypothesis only. |

`WORKSPACE/missile-diagnosis.md` was read only to know which ground had been walked. It is not cited and nothing here rests on it.

---

## 0. Correction received mid-audit, and why nothing moved

The brief's rigor block implied a single turn-rate default. The correction is right and I record it:

| Field | Declaration | `.Angle` | `.Facing` (`Angle / 4`) |
|---|---|---|---|
| `HorizontalRateOfTurn` | `Missile.cs:99` `new(20)` | 20 | **5** **[R]** |
| `VerticalRateOfTurn` | `Missile.cs:102` `new(24)` | 24 | **6** **[R]** |

`WAngle.Facing => Angle / 4` — `engine/OpenRA.Game/WAngle.cs:67` **[R]**.

**No figure in this report moved as a result**, for a reason that is itself a finding: `LoopRadius` is called at exactly four sites — `Missile.cs:377, 397, 516, 917` — and **every one passes `info.VerticalRateOfTurn.Facing`** **[R]**. `loopRadius` is therefore a purely *vertical* quantity. I used 6 for it throughout and 5 for horizontal turn authority, which is correct. See **D6** for the defect this exposes: that vertical-derived radius is then used as a *horizontal* distance threshold.

---

## 1. Configuration ground truth

### 1.1 Terrain is a flat plane at Z=0 — established, not assumed

- `mods/ww3mod/mod.yaml:320-322` declares `MapGrid: TileSize: 24,24 / Type: Rectangular`, with **no `MaximumTerrainHeight`** **[R]**.
- `MapGrid.MaximumTerrainHeight` defaults to `0` — `engine/OpenRA.Game/Map/MapGrid.cs:110` **[R]**.
- Height data is clamped on load: `Height[new MPos(i, j)] = s.ReadUInt8().Clamp((byte)0, Grid.MaximumTerrainHeight)` — `Map.cs:449` **[R]**. With max 0, **every cell's height is 0** **[A]**.
- `DistanceAboveTerrain(pos)` returns `new WDist(pos.Z)` verbatim when `Grid.Type == Rectangular` — `Map.cs:1464-1465` **[R]**. ww3mod is Rectangular, so **altitude ≡ `pos.Z`** and ground is the plane Z=0 **[A]**.

Consequence for `InclineLookahead` (`Missile.cs:533-576`): `ht` is always 0, so `ht > predClfHgt` (line 563) is never true and `prevHt != ht` (line 569) is never true. **All four outputs stay at their initialised zeros: `predClfHgt=0, predClfDist=0, lastHtChg=0, lastHt=0`** — for *every* missile, including `TerrainHeightAware: true` ones **[A]**.

### 1.2 The weapon under investigation

The AA soldier is `^AA` (`rules/ingame/infantry.yaml:1752`), faction variants `AA.america` / `AA.russia` (`infantry-america.yaml:74`, `infantry-russia.yaml:74`), firing **`MANPAD`** (`infantry.yaml:1771`) **[R]**.

`MANPAD` — `rules/weapons/weapons-missiles.yaml:377-408` **[R]**, with engine defaults filled in from `Missile.cs`:

| Field | Value | Source |
|---|---|---|
| `MaximumLaunchSpeed` | **20** | yaml:384 |
| `Speed` (max) | 450 | yaml:387 |
| `Acceleration` | 25 | yaml:386 |
| `MaximumLaunchAngle` | **1000** | yaml:385 |
| `MinimumLaunchAngle` | *default* `new(-64)` | `Missile.cs:48` |
| `HorizontalRateOfTurn` | 20 → `.Facing` **5** | yaml:391 |
| `VerticalRateOfTurn` | *default* `new(24)` → `.Facing` **6** | `Missile.cs:102` |
| `CruiseAltitude` | *default* **512** | `Missile.cs:126` |
| `HomingActivationDelay` | *default* **0** | `Missile.cs:129` |
| `FlyStraightIfMiss` | *default* **true** | `Missile.cs:69` |
| `TerrainHeightAware` | *default* **false** | `Missile.cs:75` |
| `RetargetTicks` | *default* 5 | `Missile.cs:96` |
| `Inaccuracy` / type | 256 / *default* `Absolute` | yaml:388, `Missile.cs:87` |
| `CloseEnough` | 192 | yaml:389 |
| `RangeLimit` | 24c0 = 24576 | yaml:390 |
| `Arm` | 5 | yaml:392 |
| `OperatorRetargetTicks` | *default* **0** (disabled) | `Missile.cs:117` |

A grep for `HomingActivationDelay`, `FlyStraightIfMiss`, `VerticalRateOfTurn`, `AllowSnapping`, `LockOnProbability` across `mods/ww3mod/` returns **no hits** — no weapon in the mod overrides any of them **[R]**.

`TerrainHeightAware: true` is set on exactly three weapons: `WGM` (yaml:76), `Ataka` (yaml:130), `Hellfire` (yaml:193) **[R]**. All AA weapons (`MANPAD`, `Stinger`, `Stinger.quad`, `9M311`, `SurfaceToAirMissile`, `SurfaceToAirMissile.double`, `AirToAirMissile`) leave it **false** **[R]**.

### 1.3 The target

Helicopters (`TRAN`, `littlebird`, `HELI`, `HALO`, `HIND`, `MI28`) inherit `^Helicopter` (`aircraft.yaml:136`) and **none override `CruiseAltitude`** **[R]**, so they take the engine default `AircraftInfo.CruiseAltitude = new(1280)` — `engine/OpenRA.Mods.Common/Traits/Air/Aircraft.cs:29` **[R]**.
`HELI` and `MI28` declare `Speed: 245` **[R]** (`aircraft-america.yaml:315`, `aircraft-russia.yaml:994`).

**So: helicopter sits at Z=1280; the missile's own `CruiseAltitude` is 512.** The missile's cruise phase deliberately levels off **768 below the target** **[A]**.

---

## 2. The state machine

Three states — `Missile.cs:211-216`. `state` is a plain field (`:238`) with no initialiser, so it starts at enum value 0 = **`Freefall`** **[R]**.

```
                    ticks == HomingActivationDelay + 1      (Tick:911, EQUALITY — fires exactly once)
     ┌───────────┐  ──────────────────────────────────────►  ┌──────────┐
     │ Freefall  │                                           │  Homing  │
     │ (initial) │                                           └──────────┘
     └───────────┘                                                │
           ▲                                                      │ relTarHorDist <= 3*loopRadius
           │                                                      │ (HomingInnerTick:651 → sets :654)
           │  distanceCovered > rangeLimit                        ▼
           │  && rangeLimit >= 0                             ┌──────────┐
           ├──────────────────────────────────────────────── │ Hitting  │ ◄──┐
           │  (Tick:921, re-evaluated EVERY tick)            └──────────┘    │
           └───────────────────────────────────────────────────────┘        │
                                                                   state == Hitting
                                                                   re-enters :651 forever
                                                                   (NOTHING resets it)
```

**Transition facts:**

1. `Freefall → Homing` — `Tick:911` `if (ticks == info.HomingActivationDelay + 1)`. This is an **equality**, so it can fire only once in a missile's life **[R]**. `ticks` is incremented at the top of `Tick` (`:907`), so with `HomingActivationDelay = 0` — true for **all 14 live missile weapons** **[R]** — the condition is `ticks == 1` and fires on the **first tick, before any movement is computed** (the move happens at `:1013-1025`). **The initial `Freefall` state is occupied for zero moves. `HomingActivationDelay` is inert in this mod and contributes nothing to the short-range problem.** **[A]**
2. `Homing → Hitting` — set at `HomingInnerTick:654`, reached via the `:651` guard `relTarHorDist <= 3 * loopRadius || state == States.Hitting`. **This is a one-way latch: no code path anywhere in the file assigns `States.Homing` outside `:913`, and `:913` cannot fire twice.** Once `Hitting`, the `|| state == States.Hitting` disjunct keeps the branch selected even after the missile has flown far past the target **[R]**.
3. `* → Freefall` — `Tick:921`, `rangeLimit >= WDist.Zero && distanceCovered > rangeLimit`. Re-evaluated every tick and can override `Homing` or `Hitting`. In practice **invisible**: the same condition at `:1052` sets `shouldExplode` when `ExplodeWhenEmpty` is true, which every AA weapon leaves at its `true` default (`Missile.cs:120`) **[R]**. It becomes observable only for a weapon with `ExplodeWhenEmpty: false`.
4. Once in `Freefall` via (3), **`Homing` is unreachable** — the `:911` equality has already passed. One-way **[A]**.
5. **A fourth, state-less path:** `Tick:1013` `if (state == States.Freefall || (info.ManualGuidance && args.SourceActor.IsDead)) move = FreefallTick();`. A `ManualGuidance` missile whose shooter dies freefalls **without `state` ever changing** — it stays nominally `Homing`/`Hitting` while ballistic **[R]**. Affects `WGM`/`Ataka` only (`ManualGuidance: true`, yaml:68/128); no AA weapon sets it **[R]**.

---

## 3. Launch

### 3.1 Initial horizontal facing — correct

`Missile.cs:280-284`: `toTarget = args.PassiveTarget - args.Source; if (toTarget.HorizontalLengthSquared != 0) hFacing = toTarget.Yaw.Facing;` **[R]**. This is a clean, un-truncated test (contrast §5.3) and points the missile at the target. **The missile does launch pointed correctly in the horizontal plane** — so "fired straight" is not a horizontal-aiming failure **[A]**.

### 3.2 Launch speed

`DetermineLaunchSpeedAndAngle` assigns `speed = maxLaunchSpeed` at `:396` before any branching **[R]**. `maxLaunchSpeed = info.MaximumLaunchSpeed.Length` when `> -1` (`:291`) = **20** for MANPAD **[R]**.

Then `Tick:914` overwrites it: `speed = velocity.Length` — `velocity` was built at `:313-315` with magnitude `speed`, so this round-trips to ≈20 **[A]**.

**The missile leaves the tube at 20 WDist/tick and accelerates at 25/tick to 450 — reaching max speed at tick ≈18** **[A]** (`ChangeSpeed`, `:511-517`).

### 3.3 Launch pitch — the `MaximumLaunchAngle: 1000` defect

Which branch of `DetermineLaunchSpeedAndAngle` runs? All three tested:

- `:414` `if (info.TerrainHeightAware && diffClfMslHgt >= 0 && predClfDist > 0)` → **false**, `TerrainHeightAware` is false for MANPAD. *(Note it also demands `predClfDist > 0`, which §1.1 proves is never true in this mod — so this branch is dead for every weapon, including the three terrain-aware ones.)* **[A]**
- `:416` `else if (lastHt != 0)` → **false**, `lastHt` is 0 (§1.1) **[A]**.
- `:421` `else` → **taken, always, for every missile in this mod** **[A]**.

The `else` body (`:423-434`):

```csharp
var vDist = new WVec(-tarDistVec.Z, -relTarHorDist, 0);
vFacing = (sbyte)vDist.Yaw.Facing;
if (vFacing == -1) vFacing = 0;
vFacing = vFacing.Clamp((sbyte)(minLaunchAngle.Angle >> 2), (sbyte)(maxLaunchAngle.Angle >> 2));
```

Now the arithmetic, shown in full:

- `MinimumLaunchAngle = new WAngle(-64)`. `WAngle(int a)` does `Angle = a % 1024; if (Angle < 0) Angle += 1024` (`WAngle.cs:28-33`) **[R]** → `Angle = 960`. `960 >> 2 = 240`. `(sbyte)240 = **-16**` **[A]**.
- `MaximumLaunchAngle = 1000` → `Angle = 1000`. `1000 >> 2 = 250`. `(sbyte)250 = **-6**` **[A]**.

**So the clamp is `Clamp(-16, -6)`.** `Exts.Clamp` (`Exts.cs:65-73`) is a plain min/max with `min <= max` here, so it is well-formed and simply saturates **[R]**.

The desired pitch for a target *above*: `WVec.Yaw => WAngle.ArcTan(-Y, X) - new WAngle(256)` (`WVec.cs:66-76`) **[R]**. For `vDist = (-h, -d, 0)` with `h,d > 0`: `x = -h < 0`, `-Y = d > 0`, so `ArcTan` lands in quadrant `512 - θ` ∈ (256, 512], and after `-256` the result is in (0, 256] — **positive = pitch up** **[A]**. Sanity check with a level target (`h=0`): `vDist = (0,-d,0)` → `ArcTan(d, 0) = WAngle(256)` → `-256` → 0 **[A]**, matching the `new WVec(0, -speed, 0)` forward convention at `:313`.

For a helicopter at Z=1280, 2 cells (2048) out from a ground launcher: elevation `atan(1280/2048) = 32.0°` → `32.0/360 × 256 = **+23 facing units**` **[A]**.

**+23 is clamped to −6.** The missile is forced to leave the tube pointing **8.4° downward at a target 32° above it** **[A]**.

`MaximumLaunchAngle: 1000` is a WAngle of 1000/1024 of a turn = **−8.4°**, not a steep upward angle. The `>> 2` and `(sbyte)` cast are *faithfully* reproducing that. The bug is the YAML value, not the engine arithmetic. Affected: `MANPAD` (yaml:385) and `Stinger` (yaml:423) — and by inheritance `Stinger.quad` (yaml:443) and `9M311` (yaml:451) **[R]**. `SurfaceToAirMissile` and `AirToAirMissile` do **not** set it and get the `new(128)` default → `128>>2 = 32` → `(sbyte)32 = +32` (+45°) **[R][A]**.

**Severity is moderated, and I want to be honest about that:** because `state` becomes `Homing` on tick 1 *before* the first move (§2.1), `HomingTick` immediately overwrites `vFacing` via `TickFacing` at `:897`. The −6 therefore survives as an *initial condition*, not as a sustained attitude — it costs roughly one tick of vertical turn plus a 12-unit head start in the wrong direction (from −6 rather than a permitted +6) **[A]**. It is a real handicap, not a catastrophe, and it is **not** the primary cause of the reported miss.

---

## 4. `HomingInnerTick` — branch-reachability table

Structure of `Missile.cs:634-815`, labels mine:

```
B1  :649  if (info.TerrainHeightAware && diffClfMslHgt >= 0 && !allowPassBy)
B2  :651  else if (relTarHorDist <= 3*loopRadius || state == States.Hitting)
    :654      state = States.Hitting;
    :660      if (info.TerrainHeightAware && lastHt >= targetPosition.Z) allowPassBy = true;
B2a :663      if (!allowPassBy && (!info.TerrainHeightAware || lastHt < targetPosition.Z || targetPassedBy))
B2b :701      else if (allowPassBy || (lastHt != 0 && relTarHorDist - lastHtChg < loopRadius))
B2c :782      else
B3  :798  else
```

Governing facts, all from §1.1: **`predClfDist = lastHtChg = lastHt = predClfHgt = 0` always**; therefore `diffClfMslHgt = predClfHgt - pos.Z = **-pos.Z**` (`:830`) **[A]**. And `targetPassedBy` is passed as the **literal `false`** at `:850` **[R]**.

### 4.1 AA weapons (`TerrainHeightAware: false`) — MANPAD, Stinger, SAMs, AAM

| Branch | Condition to reach | Live in WW3MOD? | Why |
|---|---|---|---|
| **B1** `:649` | `TerrainHeightAware && diffClfMslHgt>=0 && !allowPassBy` | **DEAD** | First conjunct is false by config **[A]** |
| **B2** `:651` | `relTarHorDist <= 3*loopRadius` **or** `state==Hitting` | **LIVE** | Primary terminal path; second disjunct makes it sticky **[R]** |
| `:660` | `TerrainHeightAware && lastHt >= targetPosition.Z` | **DEAD** | First conjunct false → **`allowPassBy` can never be set** **[A]** |
| **B2a** `:663` | `!allowPassBy && (!TerrainHeightAware ‖ …)` | **LIVE — and unconditional inside B2** | `allowPassBy` is permanently false, `!TerrainHeightAware` is true ⇒ condition reduces to `true` **[A]** |
| ↳ `:679` `if (targetPassedBy)` | — | **DEAD** | literal `false` at `:850` **[R]** |
| ↳ `:681` `else if (lastHt == 0)` | — | **ALWAYS TRUE** | `lastHt` ≡ 0 ⇒ speed control always runs **[A]** |
| ↳ `:671` `if (desiredVFacing == -1)` | — | **DEAD** | see §5.3 — value is in [0,255] **[A]** |
| **B2b** `:701` | `allowPassBy ‖ (lastHt != 0 && …)` | **DEAD** | both disjuncts permanently false **[A]** |
| **B2c** `:782` | else-of-B2a/B2b | **DEAD** | B2a is unconditionally true **[A]** |
| **B3** `:798` | `relTarHorDist > 3*loopRadius` **and** `state != Hitting` | **LIVE** | cruise-altitude phase **[R]** |

**Net: an AA missile in this mod executes exactly two branches its whole life — B3 (cruise) until it latches, then B2a (terminal aim) forever.** Everything else in this 180-line function is dead **[A]**.

### 4.2 Terrain-aware weapons (`WGM`, `Ataka`, `Hellfire`) — the degenerate-but-live cases

Here `TerrainHeightAware` is true while the height data is still uniformly zero, which un-deadens two branches in a way that does nothing useful:

- **B1 becomes reachable** whenever `diffClfMslHgt >= 0`, i.e. `-pos.Z >= 0`, i.e. **`pos.Z <= 0`** **[A]**. Note `:649` has **no `predClfDist > 0` guard**, unlike its sibling at `:414` which does. `pos.Z < 0` triggers `height.Length < 0 → shouldExplode` at `:1050` the same tick, so the surviving case is the knife-edge **`pos.Z == 0` exactly**, where `IncreaseAltitude` is called with `predClfDist = 0` — a "cliff" of zero distance and zero height. **Reachable only degenerately; it latches nothing but wastes the tick's aiming** **[A]**.
- **`:660` `allowPassBy = true` becomes reachable and is a latch.** With `lastHt = 0` the test is `0 >= targetPosition.Z`. For a ground target on flat terrain whose aim point sits at Z ≤ 0 this is **true on the first `Hitting` tick** **[A]**. Once latched: B1 (`!allowPassBy`) dies, B2a (`!allowPassBy`) dies, and **B2b runs for the rest of the flight**. Inside B2b with `lastHtChg = 0`, `d1 = relTarHorDist`, so `:722` `if (d1 > 2*loopRadius) { ChangeSpeed(); return 0; }` fires — **`desiredVFacing = 0`, i.e. dead-level flight ignoring the target's height** — until `relTarHorDist <= 2*loopRadius` **[A]**.

  This is exactly the failure the WW3MOD comment at `:656-659` says it is fixing. **The fix is incomplete:** gating on `info.TerrainHeightAware` protects the non-terrain-aware missiles, but the terrain-aware ones get `lastHt = 0` on this mod's flat terrain for the identical reason, and still latch **[A]**. See **D7**.

---

## 5. Steering authority

### 5.1 `LoopRadius`

`Missile.cs:343-350`: `return speed * 6400 / (157 * rot);` **[R]** — with `rot = VerticalRateOfTurn.Facing = 6` at all four call sites:

`loopRadius = speed × 6400 / 942 = **speed × 6.794**` **[A]**

| speed | 20 | 95 | 145 | 270 | 450 |
|---|---|---|---|---|---|
| `loopRadius` **[A]** | 135 | 645 | 985 | 1834 | 3057 |
| `3 × loopRadius` (Hitting threshold) | 405 | 1936 | 2955 | 5502 | **9171** |

### 5.2 Actual per-tick turn authority

`Missile.cs:886-897` **[R]**:

```csharp
var hRot = info.HorizontalRateOfTurn.Facing;   // MANPAD: 5
var vRot = info.VerticalRateOfTurn.Facing;     // default: 6
if (state == States.Hitting && relTarHorDist < 3 * loopRadius)
{
    var closeness = System.Math.Max(relTarHorDist, 1);
    var boost = System.Math.Min(3 * loopRadius / closeness, 3);   // INTEGER division
    hRot = System.Math.Min(hRot * boost, 20);
    vRot = System.Math.Min(vRot * boost, 20);
}
hFacing = Util.TickFacing(hFacing, desiredHFacing, hRot);
vFacing = Util.TickFacing(vFacing, desiredVFacing, vRot);
```

`Util.TickFacing(int,int,int)` (`Util.cs:30-44`) is a 256-unit `& 0xFF` shortest-arc step **[R]**.

Phase-by-phase authority for MANPAD **[A]**:

| Phase | `state` | `boost` | `hRot` | `vRot` | Degrees/tick (vert) |
|---|---|---|---|---|---|
| Cruise (B3) | Homing | *not applied* | 5 | 6 | 8.4° |
| Terminal, `relTarHorDist` ∈ [1.5·lr, 3·lr) | Hitting | **1** | 5 | 6 | 8.4° |
| Terminal, ∈ [1·lr, 1.5·lr) | Hitting | 2 | 10 | 12 | 16.9° |
| Terminal, < 1·lr | Hitting | 3 | **15** | **18** | 25.3° |

`boost` cannot be 0: the guard `relTarHorDist < 3*loopRadius` forces `3*loopRadius/closeness >= 1` **[A]**. The `min(…, 20)` caps bite only at `boost = 3` for `vRot` (18 < 20, so it does not bite) and never for `hRot` (15 < 20) **[A]** — **the `20` cap is inert at current values**.

**Note the boost applies only in `Hitting`, and `Hitting` is keyed on *horizontal* distance.** Against a target high overhead, `relTarHorDist → 0` while true 3D range is still large, so maximum boost arrives at the moment it is least useful **[A]**.

### 5.3 `(sbyte)vDist.HorizontalLengthSquared` — a truncation bug

`WVec.HorizontalLengthSquared` is declared **`long`**: `(long)X * X + (long)Y * Y` — `WVec.cs:44` **[R]**.

At `:667`, `:763`, `:775`, `:787`, `:803` the code reads **[R]**:

```csharp
desiredVFacing = (sbyte)vDist.HorizontalLengthSquared != 0 ? vDist.Yaw.Facing : vFacing;
```

The `(sbyte)` binds to the **squared length**, not to the ternary result. It keeps the low 8 bits. So whenever `X² + Y² ≡ 0 (mod 256)`, a perfectly well-defined direction vector is read as "zero length" and `desiredVFacing` falls back to the **current** `vFacing` — the missile stops steering vertically for that tick **[A]**.

Two consequences worth separating:

1. The intermittent fallback above.
2. **`vDist.Yaw.Facing` is `WAngle.Facing => Angle / 4` with `Angle ∈ [0,1023]`, so it returns [0,255] and is *never* −1** **[R]**. Therefore the guard at `:671` `if (desiredVFacing == -1) desiredVFacing = 0;` is **dead code** **[A]**. (Contrast `:425`, where an explicit `(sbyte)` *is* applied to `.Facing`, so −1 is genuinely producible and the twin guard at `:429` is live.) The dead guard is benign — `TickFacing` works mod 256, so a downward desire arriving as 250 rather than −6 still steers correctly — but it means the "premature descent" protection the comment at `:669-670` describes **is not in force in the homing path**.

This is upstream OpenRA code, not WW3MOD-introduced.

---

## 6. Target tracking

- **`lockOn`** — `:301` `if (world.SharedRandom.Next(100) <= info.LockOnProbability) lockOn = true;`. `Next(100)` yields 0..99 and the default is 100, so **`lockOn` is always true** **[A]**. The `<=` is an off-by-one (should be `<`): even `LockOnProbability: 0` would lock on 1% of shots. **Latent** — no weapon in the mod sets it **[R]**.
- **Target position refresh** — `:982` updates `targetPosition` only while `args.GuidedTarget.IsValidFor(...) && lockOn` **[R]**. On target death/fog the position **freezes at last-known** and the missile keeps homing on it; the comment at `:874-881` documents the deliberate removal of a heading-freeze that used to happen here **[R]**.
- **Lead** — `:1005` `WVec.CalculateLeadTarget(pos, lastTargetPosition, targetPosition, 1, speed)`. Body at `WVec.cs:168-176`: `ticksToReachTarget = distanceToTarget / projectileSpeed` with **integer division**, and `distanceToTarget` is `HorizontalLength` **[R]**. At short range, `distanceToTarget < speed` ⇒ `ticksToReachTarget = 0` ⇒ **zero lead** **[A]**. That is arguably right (no flight time, no lead needed) but it is a *horizontal* range divided by *total* speed, and it ignores the turn time entirely **[A]**.
- **Inaccuracy re-roll** — `:994` `if (ticks % info.RetargetTicks == 0 && (targetPosition - pos).Length > 1536)`. Two problems **[R]**:
  - `:996` computes a lockOn-aware `inaccuracy` local, but `:999` passes **`info.Inaccuracy.Length`** to `GetProjectileInaccuracy` instead. If `LockOnInaccuracy` were set, the initial offset (`:304-308`) would use it and the in-flight re-rolls would silently not. **Latent** — no mod weapon sets `LockOnInaccuracy` **[R]**.
  - `RetargetTicks` is a modulus with no zero guard → `DivideByZeroException` if ever set to 0. **Latent** **[A]**.
  - Offset is horizontal-only (`WVec.FromPDF(r,2)` sets Z=0, `WVec.cs:105-108`) and for MANPAD scales to ±256 per axis, `Absolute` (range-independent) **[R]**.
- **`ManualGuidance`** — §2.5. AA weapons unaffected.
- **Operator retargeting** (`:937-978`) — gated on `OperatorRetargetTicks > 0`, which is **0 for every AA weapon** ⇒ the whole block is skipped for MANPAD **[R]**. For WGM/Ataka/Hellfire (50) it is live; `FindRetargetCandidate` (`:1099-1137`) filters correctly on relationship, `IsValidAgainst`, and `DamageState.Critical/Dead`, and picks nearest-by-3D-distance **[R]**. The `flyStraight`/`minDistanceToTarget` reset at `:970-971` is present and correct **[R]**.
- **`FlyStraightIfMiss`** — see §7. This is the load-bearing one.
- **Jamming** — `:861-872`. `Explode(world)` is called at `:866` on `ActiveProtection` but execution **continues**; `Tick` may call `Explode` again at `:1096`, producing two `Weapon.Impact` calls and two `Remove` tasks **[A]**. **Latent** — I did not verify whether any WW3MOD actor carries `JamsMissiles`.

---

## 7. Distance-invariance — the answer

**No. Hit probability is strongly distance-dependent, and short range is markedly worse. There are two independent mechanisms, and the first one alone accounts for the user's report.**

### 7.1 Primary: `flyStraight` latches at the instant of `Hitting` entry

`Missile.cs:833-844` **[R]**:

```csharp
var currentDistance = new WDist(relTarHorDist);         // HORIZONTAL only
if (currentDistance < minDistanceToTarget)
    minDistanceToTarget = currentDistance;              // updated EVERY tick from tick 1

if (info.FlyStraightIfMiss && !flyStraight && state == States.Hitting
    && currentDistance > minDistanceToTarget + info.CloseEnough
    && currentDistance > info.CloseEnough)
    flyStraight = true;
```

The asymmetry: **`minDistanceToTarget` accumulates from tick 1, but the test is gated on `state == States.Hitting`.** During the whole pre-`Hitting` cruise the missile is climbing out of `MaximumLaunchSpeed: 20` at +25/tick while a helicopter moves at 245/tick. Against a receding or crossing target the missile **loses ground for the entire pre-`Hitting` window**, and `minDistanceToTarget` stays pinned at the launch distance — a distance it can never recover **[A]**.

Worked trace, MANPAD vs a helicopter receding at 245/tick from 2048 (2 cells) **[A]** (arithmetic on §1.2/§5.1 constants; `speed` is the value in force when `:651` is evaluated):

| tick | speed | `3·loopRadius` | `relTarHorDist` | `state` |
|---|---|---|---|---|
| 1 | 20 | 405 | 2048 ← `min` pinned here | Homing |
| 2 | 45 | 915 | 2273 | Homing |
| 3 | 70 | 1426 | 2473 | Homing |
| 4 | 95 | 1936 | 2648 | Homing |
| 5 | 120 | 2445 | 2798 | Homing |
| 6 | 145 | **2955** | **2923** ≤ 2955 | **→ Hitting** |

At tick 6 the `Hitting` latch closes and the `flyStraight` test is evaluated **for the first time**:
`2923 > 2048 + 192 = 2240` ✓ and `2923 > 192` ✓ → **`flyStraight = true` on the same tick** **[A]**.

From then on `:847-849` set `desiredHFacing = hFacing` and `desiredVFacing = vFacing`; `TickFacing(x, x, rot)` returns `x` **[R]** — **both axes frozen, a perfectly straight line**, while `:858-859` keeps accelerating it to 450. Recovery requires `currentDistance < CloseEnough` (192), which cannot occur once it has stopped steering **[A]**. The missile flies straight until `distanceCovered > 24576` and detonates at fuel-out far away **[A]**.

**This reproduces the report exactly: "as if the tracking mechanism didn't arm at all — they just fired straight and kept flying in a straight line."** The tracking *did* arm (tick 1); it **disarmed** at tick 6 and never re-armed **[I]**.

Repeating at 4 cells (4096): latch fires at tick 11, `5221 > 4096 + 192` **[A]**. **The whole 2–4 cell band the user reported is covered.**

**Why long range escapes it:** at 20 cells the missile reaches 450 long before `3·loopRadius = 9171` is crossed, so it is closing at 450−245 = 205/tick when `Hitting` engages; `currentDistance` is then *at* its running minimum and the test fails **[A]**. The missile homes normally.

**The discriminator is not range per se — it is whether the missile is still below the target's speed when `Hitting` engages.** At short range `3·loopRadius` is crossed early (while slow); at long range it is crossed late (at full speed) **[A]**.

### 7.2 Secondary: the cruise-altitude notch

The missile levels at `CruiseAltitude` **512** while the helicopter sits at **1280** (§1.3), and does not begin climbing at the target until `Hitting` **[A]**. Against a *hovering* helicopter at 2 cells my tick-by-tick reconstruction has the missile arriving at Z≈1258 vs target 1280 with horizontal distance ≈0 — inside `CloseEnough` 192 via the segment check at `:1070-1093`, i.e. **a hit** **[I]**. So the cruise notch alone is survivable at 2 cells; it is the `flyStraight` latch that converts a moving target into a guaranteed miss **[I]**.

That reconstruction is **[I]**, not **[R]** — it is ~13 ticks of hand arithmetic and is the single result here I would most want the Phase 0 trace to confirm.

### 7.3 Also anti-invariant at the far end

`Inaccuracy: 256` with the default `InaccuracyType: Absolute` (`Missile.cs:87`) is **range-independent**, so it does not itself break invariance — but `GetProjectileInaccuracy` (`Util.cs:401-416`) shows the `Maximum` and `PerCellIncrement` types **do** scale with range **[R]**. `WGM` (`PerCellIncrement 20`) and `Ataka` (`22`) are deliberately range-scaled **[R]** — an explicit, commented design choice (yaml:51-54) that **is a defect under the user's stated invariant**, though a knowing one.

---

## 8. Ranked defects

| # | Defect | Tag | Severity |
|---|---|---|---|
| **D1** | **`flyStraight` latches on the first `Hitting` tick at short range.** `minDistanceToTarget` accumulates from tick 1 but the test is gated on `state == Hitting` (`:839`); the pre-`Hitting` acceleration window pins `min` at launch distance. Against a moving target inside ~4 cells the missile stops steering permanently the moment it enters terminal guidance. | **[R]** mechanism, **[A]** trace, **[I]** that this is the user's bug | **Critical — this is the reported bug** |
| **D2** | **`MaximumLaunchAngle: 1000` → `(sbyte)(1000>>2)` = −6.** Launch pitch clamped to [−16,−6] — always *downward* — on `MANPAD`, `Stinger`, `Stinger.quad`, `9M311`. Target is always above. Costs ~1 tick of turn plus a 12-unit wrong-way head start. | **[R]** + **[A]** | High (config, trivially fixable) |
| **D3** | **All SAMs cruise at the default `CruiseAltitude` 512, helicopters fly at 1280.** The missile deliberately levels 768 *below* its only valid target class and defers the climb to the terminal phase. `AirToAirMissile` sets 2c0; the surface launchers do not. | **[R]** + **[A]** | High |
| **D4** | **Terminal speed control always accelerates in AA geometry.** In B2a `:695`, a large `|relTarHgt|` clamps `tarHgt` to 0 ⇒ `tarDist = loopRadius`, and `missDist → loopRadius` as `vFacing` rises, so the threshold collapses toward 0 and `ChangeSpeed()` (accelerate) is taken. Faster ⇒ larger `loopRadius` ⇒ wider turn, exactly when a tighter one is needed. | **[A]** | Medium |
| **D5** | **`(sbyte)vDist.HorizontalLengthSquared != 0`** (`:667,763,775,787,803`) truncates a `long` to 8 bits; when `X²+Y² ≡ 0 (mod 256)` the missile skips vertical steering for that tick. Upstream OpenRA. | **[R]** + **[A]** | Medium |
| **D6** | **`loopRadius` is vertical-derived but used as a horizontal threshold.** All four `LoopRadius` calls pass `VerticalRateOfTurn` (`:377,397,516,917`), yet the result gates `relTarHorDist <= 3*loopRadius` (`:651`), `relTarHorDist > 2*loopRadius` (`:716`) and the horizontal boost (`:888`). With MANPAD's `hRot=5` vs `vRot=6` the true horizontal radius is `speed×8.153` — **20% larger** than the value used, so the missile commits to terminal guidance later than its horizontal agility warrants. | **[R]** + **[A]** | Medium |
| **D7** | **The `allowPassBy` gate at `:660` is incomplete on flat terrain.** Gating on `info.TerrainHeightAware` fixes the non-aware missiles, but `WGM`/`Ataka`/`Hellfire` also get `lastHt = 0` here, so `0 >= targetPosition.Z` latches `allowPassBy` against any ground target at Z ≤ 0 — permanently killing B1/B2a and forcing dead-level flight via `:722` until `relTarHorDist <= 2*loopRadius`. | **[A]** | Medium (ATGMs, not AA) |
| **D8** | **`state == Hitting` is a one-way latch** with no distance-based exit (`:651`), so an overshooting missile stays in terminal mode forever. Compounds D1. | **[R]** | Medium |
| **D9** | **`:671` `desiredVFacing == -1` is dead code** — `.Facing` returns [0,255]. The documented premature-descent guard is not in force in the homing path. | **[R]** + **[A]** | Low |
| **D10** | **`targetPassedBy` hardcoded `false` at `:850`** ⇒ all overshoot handling (`:679-681`, `:771-780`) is dead. | **[R]** | Low (superseded by `flyStraight`) |
| **D11** | **`lockOn` off-by-one** (`:301`, `<=` on `Next(100)`) — `LockOnProbability: 0` would still lock 1% of the time. Latent. | **[A]** | Low |
| **D12** | **Inaccuracy re-roll ignores `LockOnInaccuracy`** — `:996` computes it, `:999` passes `info.Inaccuracy.Length`. Latent. | **[R]** | Low |
| **D13** | **`RetargetTicks` used as modulus with no zero guard** (`:994`). Latent. | **[R]** | Low |
| **D14** | **Jam + `ActiveProtection` can double-detonate** — `Explode` at `:866` does not return; `:1096` may fire again. Latent, unverified whether any actor has `JamsMissiles`. | **[A]** | Low |

**Minimal fix for the reported bug (D1)** — not implemented, this is a read-only audit: reset `minDistanceToTarget` at the `Homing → Hitting` transition, so the miss detector measures only the terminal phase it was written for. That is a one-line change at `:654` and is distance-symmetric by construction **[S]**.

---

## 9. What I could NOT determine statically — for the Phase 0 trace

1. **The §7.2 hovering-target reconstruction.** ~13 ticks of hand arithmetic; the *conclusion* (hover at 2 cells = hit, mover at 2 cells = miss) is the highest-value thing to confirm, and my confidence is materially lower than for D1's latch mechanism.
2. **Intra-tick ordering** between the helicopter's move and the missile's `Tick` within one world tick. This shifts the D1 latch by ±1 tick; it does not change *whether* it fires (the margin at tick 6 is 2923 vs 2240 — 683 wdist of slack).
3. **`args.Source.Z` for the AA infantry.** `^AA`'s `Armament@1` declares no `LocalOffset` (`infantry.yaml:1769-1772`), so I take muzzle Z ≈ 0, but I did not trace `Armament.CalculateMuzzleOffset`. Shifts launch geometry slightly; does not affect D1.
4. **The exact rate of the D5 truncation.** `X²+Y² ≡ 0 (mod 256)` is not a uniform-1/256 event and I did not enumerate it. I deliberately give no number rather than a wrong one.
5. **`targetPosition.Z` for ground targets** via `Positions.ClosestToIgnoringPath` (`:983`) — decides whether D7's `0 >= targetPosition.Z` latch actually closes. If any targetable offset lifts the aim point above Z=0, D7 is dormant.
6. **Whether any actor carries `JamsMissiles`** (D14) and whether `ActiveProtection` is used anywhere.
7. **Helicopter `Speed: 245` units.** I read it as WDist/tick per `Aircraft`; I did not confirm against the movement code. If helicopters are materially slower than the missile during its first ~6 ticks, D1's trigger threshold moves — though the missile still starts at 20/tick, so any target motion away from the launcher reproduces it.
8. **Whether `^AA`'s `Prerequisites: ~disabled` (`infantry.yaml:1759`) is overridden** for the faction variants — `AA.america`/`AA.russia` do set their own `Buildable.Prerequisites` (`infantry-america.yaml:76-77`), and the user evidently has the unit, so this is almost certainly moot.
