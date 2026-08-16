# Javelin (`ATGM`) terminal geometry — under what conditions does it miss and survive?

Read-only audit, 2026-08-13. Sources: `engine/OpenRA.Mods.Common/Projectiles/Missile.cs`
(shipped, as of `main` @ f30d95c8), `mods/ww3mod/rules/weapons/weapons-missiles.yaml:2-47`,
and the retained trace corpus at `C:\Users\fredr\.ww3mod-tests\screenshots\*\result.missiles.jsonl`
(640 ATGM records across 23 variant builds; 39 of them shipped-configuration).

Every claim below is tagged **[code]** (read directly from source), **[trace]** (measured
from retained JSONL), **[calc]** (arithmetic on the two), or **[spec]** (speculation).

Angles are given as **raw WAngle / facing / degrees** throughout. `WAngle.Facing == Angle / 4`
(`engine/OpenRA.Game/WAngle.cs:67`); 256 facings = 360°.

---

## 0. Answer, up front

**A Javelin can miss and survive, and the corpus contains one that did.** But the mechanism is
not the one the investigation has been looking for. Survival is **not** produced by the missile
failing to reach its aim point in some exotic geometry. It is produced by a two-part condition:

> **(A)** The missile's swept path must stay ≥ `CloseEnough` (298) from its *aim point* on every
> tick — i.e. it must fail to arrive at the point it is steering at. This is necessary but not
> sufficient.
>
> **(B)** At the tick the resulting `flyStraight` latch fires, **`vFacing` must be ≥ 0**. The latch
> freezes both facings permanently; if the frozen `vFacing` is negative the missile is committed to
> a descending straight line and eats the ground within a handful of ticks. Only a latch taken at a
> level-or-climbing attitude produces a missile that is still in the world seconds later.

(A) alone gives you a Javelin that flies a few cells past the tank and detonates on the dirt.
(A)+(B) gives you a Javelin that sails on level for tens of ticks and dies at fuel-out with zero
damage — which is what the user is describing the front half of.

**And (B) has a hard, measurable, one-tick window in shipped configuration.** All 39 shipped
flights pass through `vFacing == 0` for **exactly one tick** — the homing→hitting transition — and
are negative on every tick thereafter **[trace]**. See §4.

**On the loop specifically: a latched Javelin cannot loop** (§5). A *non*-latched Javelin can
only loop if it enters a turn-radius-limited orbit whose radial excursion stays under 298 wdist,
which I can bound but not confirm (§5.3, marked speculation). If the user's missile visibly
*turned back*, the only code path that produces it is that orbit — and the `flyStraight` latch is
specifically designed to prevent it. That makes the orbit the single highest-value thing to go
looking for.

---

## 1. Configuration facts — three of them are not what the item has been assuming

### 1.1 The turn rates are 4× smaller than the YAML numbers look

`HorizontalRateOfTurn` and `VerticalRateOfTurn` are `WAngle`, not facings
(`Missile.cs:98-102`), and `FieldLoader.ParseWAngle` stores the YAML integer as the **raw Angle**
(`engine/OpenRA.Game/FieldLoader.cs:250-253`). Every consumer then calls `.Facing`
(`Missile.cs:390, 402, 541, 610, 626, 705, 770, 818, 834, 949-950`). **[code]**

| field | YAML | raw WAngle | `.Facing` | degrees/tick |
|---|---|---|---|---|
| `HorizontalRateOfTurn` | `20` | 20 | **5** | 7.03° |
| `VerticalRateOfTurn` | *(default)* | 24 | **6** | 8.44° |
| boosted `hRot` (max) | — | 60 | **15** | 21.09° |
| boosted `vRot` (max) | — | 72 | **18** | 25.31° |
| `MinimumLaunchAngle` | *(default)* | −64 | −16 | −22.5° |
| `MaximumLaunchAngle` | *(default)* | 128 | 32 | +45° |

Confirmed against trace: `lr` (loopRadius) reads exactly **2038** at `spd=300` in every shipped
ATGM record **[trace]**, and `LoopRadius(300, 6) = 300*6400/(157*6) = 2038` **[calc]**
(`Missile.cs:374`). With `.Facing == 24` it would have been 509. The traces settle it.

The boost block (`Missile.cs:951-957`) is worth stating exactly, because it is integer division:

```
if (state == Hitting && relTarHorDist < 3 * loopRadius)
    boost = min(3 * loopRadius / max(relTarHorDist,1), 3)   // 6114 / rthd, capped 3
    hRot  = min(5 * boost, 20)   →  5 / 10 / 15
    vRot  = min(6 * boost, 20)   →  6 / 12 / 18
```
**[code]** The 20-cap is never reached; the `min(...,3)` on `boost` binds first. **[calc]**

**The governing number for this whole audit:** minimum horizontal turn radius at full speed
= `300*6400/(157*15)` = **815 wdist**, against `CloseEnough` = **298**. The Javelin's tightest
possible turn circle is **2.74 fuse radii** wide. **[calc]**
Unboosted (outside `relTarHorDist` 6114) it is `300*6400/(157*5)` = **2446**. **[calc]**

### 1.2 `CruiseAltitude: 10c0` is unreachable — the Javelin flies at ~0.5–2 cells, not 10

In cruise the missile takes `Missile.cs:823-837`: `desiredVFacing` is clamped to
`±VerticalRateOfTurn.Facing` = **±6 facings = ±8.44°**. Climb rate = `300 · sin(8.44°)` = **44
wdist/tick** **[calc]**. Reaching 10240 therefore needs **~238 ticks = 71,400 wdist of travel**,
against `RangeLimit: 21c0` = **21,504** — a factor of 3.3 short. **[calc]**

Trace confirms with no ambiguity: `vf` is pinned at exactly **6** for the entire cruise phase of
every shipped flight, `dat` climbs in 43-wdist steps, and **peak altitude across all 39 shipped
flights is 100…2068 wdist** **[trace]**. Not one gets above two cells.

> **This does not make `CruiseAltitude: 10c0` a typo, and I am not flagging it as one.** It is
> the declared intent. It is simply not achieved, and any reasoning that assumed a 10-cell
> plunging dive — including "the missile arrives vertical and has no lateral authority" — is
> reasoning about a trajectory that does not occur. The real terminal attitude is a **7–8° glide**
> (`vFacing` −5/−6 facings), not a dive.

`TopAttack: true` is a **damage** flag, not a flight flag: its only consumer is
`engine/OpenRA.Mods.Common/Warheads/DamageWarhead.cs:131` (`WeaponInfo.cs:162`). It has zero
effect on the trajectory. **[code]**

### 1.3 `Inaccuracy: 512` is `Absolute`, and the offset is purely horizontal

ATGM declares no `InaccuracyType`; this codebase's default is **`Absolute`**
(`Missile.cs:87` — note upstream OpenRA defaults to `Maximum`), so
`GetProjectileInaccuracy` returns a flat 512 at every range
(`engine/OpenRA.Mods.Common/Util.cs:411-412`). **[code]**

`offset = WVec.FromPDF(rng, 2) * 512 / 1024` (`Missile.cs:325, 1098`). `FromPDF` is
triangular per axis over [−1024, 1023] and **sets Z to `WDist.Zero`**
(`engine/OpenRA.Game/WVec.cs:105-108`, `WDist.cs:56-60`). **[code]**

- per-axis range after scaling: **[−512, +511]**
- max magnitude: **724** (both axes at the corner)
- mean magnitude: **≈ 214** **[calc]**
- **`offset.Z` is always 0** — the aim point is never displaced vertically by inaccuracy.

Re-roll: `Missile.cs:1092` — every `RetargetTicks` (default **5**, `Missile.cs:96`) **while
`(targetPosition - pos).Length > 1536`**. Inside 1536 the offset is **frozen** for the rest of the
flight. **[code]**

*(Incidental, not load-bearing here: `:1097` passes `info.Inaccuracy.Length` where `:1094`
computed a lockOn-adjusted `inaccuracy` local it then ignores. Harmless for ATGM —
`LockOnInaccuracy` is −1 so the two are equal — but the two lines disagree by construction. Worth
a line in `WORKSPACE/bugs/discovered.md`; out of scope here.)*

### 1.4 `LockOnProbability` makes `lockOn` unconditionally true

`if (world.SharedRandom.Next(100) <= info.LockOnProbability) lockOn = true;` with the default
100 (`Missile.cs:93, 318-319`). `Next(100)` yields 0…99, always ≤ 100. **`lockOn` is true on
every missile in the mod.** **[code]** So the "target position frozen at launch" branch
(`:1080`) is unreachable and cannot be a source of the loop.

---

## 2. Every termination clause, and exactly what defeats it

Nine call sites can end an ATGM. Listed in evaluation order.

| # | clause | site | what must be true for it NOT to fire |
|---|---|---|---|
| 1 | **JammedAps** | `:919-929` | **Permanently dead.** The only `JamsMissiles` declaration in the mod is commented out (`vehicles-america.yaml:491-492`). **[code]** |
| 2 | **Blocked** | `:1138-1145` | **Permanently dead.** `Blockable: false` (`weapons-missiles.yaml:18`). **[code]** |
| 3 | **Ground** | `:1162` | `Map.DistanceAboveTerrain(pos).Length >= 0` — the missile must not descend into terrain. Defeated only by a non-negative pitch, or by exhausting fuel before the descent completes. |
| 4 | **CloseEnough** | `:1163` | `relTarDist >= 298`, where `relTarDist = |targetPosition + leadTarget + offset − pos|` — distance to the **aim point, offset included**. |
| 5 | **FuelOut** | `:1164` | `distanceCovered <= 21504`. `ExplodeWhenEmpty` defaults true (`:120`) and ATGM does not override. This is a **hard cap on total path length**, not on time — a decelerated missile lives longer in ticks. |
| 6 | **OffMap** | `:1165` | missile stays on the map. |
| 7 | **TerrainBound** | `:1166` | **Permanently dead.** `BoundToTerrainType` is empty. **[code]** |
| 8 | **Airburst** | `:1171` | `flyStraight` is true **OR** `DistanceAboveTerrain >= 32` **OR** `relTarHorDist >= 298`. |
| 9 | **SegmentClosest** | `:1188-1214` | closest approach of this tick's **swept segment** to `targetPosition + leadTarget` (**offset excluded**, `:1194`) is ≥ 298. |

Two structural notes that matter:

**Clause 9 is live on every ATGM tick.** It is gated `state != States.Freefall`. `States.Freefall`
is the enum default, but tick 1 sets `state = Homing` (`:985-987`, `HomingActivationDelay` = 0), and
the only re-entry is `:998` on `distanceCovered > rangeLimit` — which is the *same* predicate that
fires clause 5 later in the very same tick, ending the missile. `ManualGuidance` is false for ATGM.
So Freefall is unreachable and the segment check never switches off. **[code+calc]**
Trace agrees: **377 of 640** ATGM records end on `segment_closest`. **[trace]**

**Clauses 4 and 9 are centred on two *different* points, 0–724 wdist apart.** Clause 4 tests the
offset aim point (what the missile steers at); clause 9 tests the true lead point (where the tank
actually is). Survival requires missing **both** 298-radius spheres. The offset therefore cannot
buy survival on its own — pushing the trajectory clear of sphere 9 pushes it straight into
sphere 4. This is exactly the manager's record-82 observation (`min_dist` 581, `reason:
close_enough`, 20 damage): the missile physically missed the tank by nearly two fuse radii and
detonated anyway, because it arrived at its aim point.

---

## 3. Q1 — Can a Javelin physically pass its target without any clause firing?

**Yes. Once in the retained corpus, and the record is unambiguous.**

`260813_160522_p*_test-missile-latch-probe`, ATGM record **id 34**, `at` → `t90`,
launch range 12376: **[trace]**

```
 tk 14  st=homing   spd=300  vf=  +6  hf=190  rtd= 6410  mdt=4875  dat=520  fs=0
 tk 15  st=hitting  spd=300  vf=   0  hf=190  rtd= 5880  mdt=4875  dat=520  fs=0
 tk 16  st=hitting  spd=300  vf=   0  hf=190  rtd= 5582  mdt=4875  dat=520  fs=1   <-- LATCH
 ...    (hf and vf frozen; dat pinned at 520 for 58 consecutive ticks)
 tk 35  st=hitting  spd=300  vf=   0  hf=190  rtd=  669  mdt= 458  dat=520  fs=1   <-- closest approach 583
 tk 36  st=hitting  spd=300  vf=   0  hf=190  rtd=  727  mdt= 458  dat=520  fs=1   <-- opening again
 ...
 tk 74  end  reason=fuel_out  min_dist=583  min_aim_dist=665  damage=0  distance_covered=21630
```

Read it straight: the missile latched `flyStraight` at tick 16 while still 5.5 cells out, froze
`hFacing` at 190 and **`vFacing` at 0**, flew a dead-level straight line at 520 above terrain for
58 ticks, passed the T-90 at **583 wdist** — outside both 298 spheres — and finally detonated on
fuel exhaustion 39 ticks later, doing **zero damage**. That is a Javelin that reached its target,
missed, and survived.

**Caveat, and it is a large one.** `260813_160522` is the synthetic isolation build that never
shipped. Its `mdt` (`minDistanceToTarget`) reads **4875 while the missile is still ~11,700 wdist
out** — that is the collapsed lead-inflated distance the shipped fix specifically removed
(`Missile.cs:858-877`). So **the latch *cause* in this record is the fixed bug and is not
reachable in shipped code.** What the record proves is the *post-latch dynamics*, which are
shipped code and unchanged: once latched level, nothing stops the missile.

For contrast, the shipped build's `mdt` tracks true physical separation to the wdist
(id 12, `260813_160700`): `tk35 phys=957 mdt=1256`, `tk36 phys=658 mdt=956`, `tk37 phys=359
mdt=657` — a one-tick-lagged running minimum of the real range. **[trace]** The fix is sound.

### 3.1 The geometry, stated as a condition

The trajectory must satisfy, on **every** tick:

```
closestApproachOfSweptSegment(targetPosition + leadTarget)        >= 298      (clause 9)
|targetPosition + leadTarget + offset − pos|                      >= 298      (clause 4)
DistanceAboveTerrain(pos)                                         >= 0        (clause 3)
distanceCovered                                                   <= 21504    (clause 5)
```

Since the missile *steers* at `targetPosition + leadTarget + offset`, clause 4 is satisfied only
if the missile **fails to arrive at the point it is aiming at**. The lateral-correction budget is
the binding constraint:

> Over a remaining distance **D**, a Javelin turning at its tightest can displace itself laterally
> by about **D² / (2·R_h) = D² / 1630**. Setting that equal to 298 gives **D = 697**. **[calc]**

So an aim-point lateral shift of magnitude **X** is nullable only if it happens at remaining
range **D ≥ √(1630·X)**:

| lateral shift X | minimum remaining range to correct it |
|---|---|
| 298 (one fuse radius) | 697 |
| 724 (max single offset) | 1086 |
| **1448** (maximal opposed re-roll) | **1537** |

**The offset-freeze radius hardcoded at `Missile.cs:1092` is 1536.** **[calc]** The last permitted
re-roll therefore happens at almost exactly the range at which a worst-case re-roll becomes
uncorrectable. I read this as coincidence rather than design, but it is the sharpest number in the
audit: **a maximal opposed offset re-roll taken on the last eligible tick is right on the edge of
producing a > 298 miss against a completely stationary target.** It requires both the outgoing and
incoming offsets to land near opposite ±(512,512) corners of a triangular distribution — rare
enough that 39 flights would not be expected to show it, which is consistent with the corpus.

### 3.2 What the *other* candidates do — checked, and mostly negative

**`AirburstAltitude: 32` — does it place the aim point somewhere unreachable?**
For the terminal phase, no: it lifts the fuse-sphere centre 32 wdist above target centre
(`:1080-1082`), which is 11% of the 298 radius. Trace shows `tgt.Z == 32` on every tick. **[trace]**

But it has a real, previously-unreported side effect on **tick 1**. `lastTargetPosition` is seeded
to `args.PassiveTarget` (`:304`), which carries **no** airburst offset; tick 1's `targetPosition`
(`:1082`) carries **+32**. `CalculateLeadTarget` therefore reads a spurious one-tick target
velocity of `(0,0,32)` and multiplies it by `ticksToReachTarget = horizontalRange / speed`
(`WVec.cs:170-173`), with `speed` still at the launch value of 100.

Predicted tick-1 aim altitude = `32 + 32 · (launchHorRange / 100)`. **[calc]**
Trace, id 12, `launch_range` 10764: predicted `32 + 32·107 = 3456`; **measured `aim.Z = 3456`.**
Exact, and it drops to 32 on tick 2. **[trace]** At ATGM max range the spike is
`32 + 32·204 = 6560` — **6.4 cells of phantom altitude**.

It is currently harmless *only* because tick 1 is always in the cruise branch, which ignores the
aim point's Z and clamps `desiredVFacing` to ±6 anyway. It becomes live the moment a shot starts
in `Hitting` (needs `relTarHorDist ≤ 3·loopRadius(100) = 2038`, and `MinRange: 3c0` = 3072 keeps
it out today). **Lower `MinRange` or lower `MaximumLaunchSpeed` and the Javelin will pitch up hard
at a phantom point several cells overhead on its first tick.** Latent, not active. **[code+trace]**

**The dive geometry — does the aim point become unreachable at some dive angle?**
No, and the premise was wrong: there is no dive. Terminal `vFacing` across the 39 shipped flights
is **−2 … −15 facings (−2.8° … −21°)**, median −5/−6, i.e. a 7–8° glide **[trace]**. Vertical
authority (18 facings/tick boosted = 25.3°) exceeds the required pitch change by a wide margin.
The vertical axis is not a failure mode.

**A moving target.** This is the one live candidate, and it works through the **lead** term rather
than through raw turn saturation. `leadTarget = targetVelocityPerTick · floor(D_horiz / speed)`
(`WVec.cs:168-175`). Against the mod's fastest ATGM-valid target — the **Humvee**, `Speed: 150`,
`TargetTypes: Ground, Vehicle, Light` (`vehicles-america.yaml:53, 61`) — the aim point sits
`150 · D/300 = D/2` off the target. A **direction reversal** swings it by `2·Vt·D/speed`:

| reversal at remaining D | aim-point swing | correctable? (needs D ≥ √(1630·X)) |
|---|---|---|
| 2000 | 2000 | 1805 → **yes**, marginally |
| 1500 | 1500 | 1564 → **no**, by 64 wdist |
| 1000 | 1000 | 1277 → **no**, comfortably |

**[calc]** So a Humvee that reverses inside ~1.5 cells of intercept generates an aim-point shift
the Javelin cannot null, producing a miss of several hundred wdist on both spheres. **This is the
most reliable way to force condition (A).**

---

## 4. Q2 — the role of `!flyStraight` gating the airburst, and what a latched Javelin does

### 4.1 The airburst gate is not the interesting part

`:1171` and `:1245`: `!flyStraight && height < 32 && relTarHorDist < 298`. Losing it on latch
costs the missile very little, because the clause was already near-unreachable for a Javelin: it
requires the missile to be within **32 wdist of the ground** *and* within 298 horizontally of the
aim point. At a 7° glide the missile crosses the 32-wdist band in one tick while covering 300
horizontally. Corpus: **3 airburst terminations out of 640** ATGM records. **[trace]** The gate
removes a clause that fires 0.5% of the time.

### 4.2 What a latched Javelin actually does — the part that matters

Latch predicate, `Missile.cs:883`:
```
FlyStraightIfMiss && !flyStraight && state == States.Hitting
   && currentDistance > minDistanceToTarget + 298 && currentDistance > 298
```
where `currentDistance = |targetPosition − pos|` — **true physical separation**, no lead, no
offset (`:878`). `minDistanceToTarget` is a running minimum that is **never reset** for ATGM.

**For ATGM the latch is irreversible.** The upstream recovery clause is deleted and must stay
deleted (`:886-902`, invariant I2b). The only other writer of `flyStraight = false` is the operator-
retarget block at `:1068`, gated on `OperatorRetargetTicks > 0` — ATGM does not declare it and the
default is **0** (`:117`), so the entire block at `:1014-1076` never executes for a Javelin.
**[code]** A Javelin has no re-acquisition path of any kind.

From latch to termination:

1. `desiredHFacing = hFacing`, `desiredVFacing = vFacing` (`:905-908`) → `Util.TickFacing` is a
   no-op → **both facings frozen forever**. The trajectory is a straight line in 3D.
2. `ChangeSpeed()` is forced every tick (`:916-917`) → speed ramps to and holds **300**.
3. `HomingInnerTick` is never called again → `state` freezes at `Hitting`, speed control stops.
4. Surviving termination routes: **ground** (clause 3), **fuel-out** (5), **off-map** (6), and —
   this is easy to miss — **clauses 4 and 9 are still armed**, so a frozen heading that happens to
   thread within 298 of the still-updating aim point will still fuse. Corpus contains exactly that:
   3 ATGM records ending `segment_closest` *after* latching. **[trace]**

**The frozen `vFacing` decides everything.** Descent rate is `300 · sin(vFacing)`:

| frozen `vFacing` (facings / °) | descent | ticks to ground from dat=520 | outcome |
|---|---|---|---|
| −6 / −8.44° | 44/tick | 12 | ground detonation ~3.5 cells past |
| −5 / −7.03° | 37/tick | 14 | ground detonation ~4 cells past |
| −1 / −1.41° | 7/tick | 71 | fuel-out (barely) |
| **0** | **0** | **∞** | **level — survives to fuel-out** |
| > 0 | climbs | ∞ | **climbs away — survives to fuel-out** |

**[calc]** Corpus agrees: of the 15 latching ATGM records, **12 end `ground`**, 2 `fuel_out`, 3
`segment_closest`; the two `fuel_out` cases are the ones latched at `vFacing == 0`. **[trace]**

### 4.3 The `vFacing == 0` window is exactly one tick wide

I extracted the `vFacing` sequence spanning the homing→hitting transition for all 39 shipped
flights. **Every single one is `(+6, 0, −N, …)`** — the missile is clamped at +6 through the whole
cruise, `HomingInnerTick`'s terminal aim drives it through **0 for precisely one tick**, then
negative for the rest of the flight: **[trace]**

```
(6, 0, -6, -6,  -6) x12     (6, 0, -6, -12, -13) x2
(6, 0, -5, -5,  -5) x 6     (6, 0, -6,  -7,  -6) x2
(6, 0, -6, -12, -15) x2     (6, 0, -1,  -1,  -1) x2   <-- the long-window case
```

Note the `(6, 0, -1, -1, -1)` flights. A terminal `vFacing` of **−1 (−1.41°)** descends 7 wdist
per tick — from a typical 500-wdist cruise altitude that is **71 ticks to ground**, longer than
the missile's entire fuel budget. **Condition (B) is effectively satisfied for the whole terminal
run on a shallow-approach shot, not just for one tick.** That is the geometry to engineer, and §6
does.

Terminal `vFacing` distribution across the 39 shipped flights: `−15 … 0`, with **2 flights ending
at exactly 0 and one at +5**. **[trace]** The window is narrow but it is not empty.

### 4.4 Bottom line for Q2

A latched Javelin flies a frozen straight line at 300 wdist/tick with the airburst disabled,
retaining ground / fuel-out / off-map / opportunistic-proximity as its only exits. **It cannot
turn, so it cannot loop.** Whatever the user saw, if it curved back it had not latched.

---

## 5. Can it loop? — bounding the only remaining path

### 5.1 Looping requires an overshoot with `flyStraight` still false

Established above: latched ⇒ frozen heading ⇒ no loop. So a loop needs the miss test to *not* fire
across the overshoot. Two ways:

**(i) `state != States.Hitting` at the pass.** `state` flips at `relTarHorDist ≤ 3·loopRadius`
(`:676`) and is sticky. Trace: transitions occur at `rthd` **5860–5995**, against the predicted
6114 **[trace+calc]** — a 6-cell net. `relTarHorDist` measures to the aim point, so staying
outside it while physically at the tank needs `|leadTarget + offset|` > 6114 at near-zero range —
and `leadTarget` scales with range, so it collapses to zero exactly when you need it large.
**I could not construct this and I believe it is unreachable.** *(inferred)*

**(ii) The excursion stays under 298.** `currentDistance` must never exceed
`minDistanceToTarget + 298`. A missile that flies out to turn around trips this on the way out —
unless it is in a **near-constant-radius orbit**.

### 5.2 The orbit — bounded, not confirmed

Pure pursuit with a minimum turn radius **R = 815** against a *stationary* aim point has a
limit cycle: a circle of radius R about the point. On that circle `currentDistance` is constant, so:

- clause 4: `relTarDist ≈ 815 > 298` — no fuse
- clause 9: same — no fuse
- clause 8: `flyStraight` false, but the missile is at cruise altitude, not within 32 of ground
- the latch: `currentDistance ≈ minDistanceToTarget` — **never exceeds min + 298, so it never fires**

The missile would circle until fuel-out. Entering the cycle requires arriving turn-saturated with
the aim point inside the turn circle and the first-pass radial excursion under 298, i.e. the aim
point within ~149 wdist of the turn circle's centre. That is a knife-edge, but it is a *stable
attractor* once reached, which is what makes it worth hunting.

**One thing I checked that does *not* rescue it:** I expected the terminal deceleration to shrink
R and spiral the missile in. It does not. The deceleration predicate (`:720`) evaluates to
"decelerate" only inside `relTarHorDist ≈ 343` for a typical shipped geometry — I computed the
threshold as `tarDist − sign(relTarHgt)·missDist` = `592 − 249 = 343` at tick 37 of shipped record
id 12, and the trace shows `spd` holding **300 at tk37 (rthd 331→ under threshold) and dropping to
270 at tk38** — the model predicts the deceleration tick exactly **[calc+trace]**. So R stays at
815 essentially all the way in; there is no self-correcting spiral.

**Marked [spec]:** I have not shown the orbit is *reachable*, only that nothing in the code
prevents it and that the one damping mechanism I expected to kill it does not operate. A moving
target defeats it (orbit period `2πR/300` ≈ 17 ticks; a 150-speed target translates ~1275 wdist in
half a period, which trips the latch immediately **[calc]**) — so if it exists it exists against a
target that is **stationary or nearly so at the moment of arrival**.

### 5.3 What this means for the user's report

Two distinguishable things could have been seen, and the scenario in §6 separates them:

- **"flew past and didn't explode, then blew up somewhere else"** — condition (A)+(B). Fully
  explained, code-grounded, and demonstrated by record id 34. Straight line, no turn.
- **"turned around and came back at it"** — requires the §5.2 orbit. Speculative. If a granted run
  reproduces a *turn*, the orbit is real and `minDistanceToTarget` needs a monotonic-opening guard
  rather than a delta guard.

---

## 6. Q3 — scenario parameters that maximise the chance of a surviving miss

Designed to satisfy (A) and (B) simultaneously. Every parameter below is chosen against a
specific number established above.

### 6.1 Primary scenario — "shallow Javelin vs. reversing Humvee"

| parameter | value | why |
|---|---|---|
| **Weapon / launcher** | `ATGM` on the `at` actor, shipped config, unmodified | the item is about the shipped Javelin |
| **Engagement range** | **4c0 – 6c0 (4096–6144)** | keeps the missile in cruise for only ~5–10 ticks, so it enters `Hitting` at **dat ≈ 150–400** instead of 500–800. At 5000 horizontal and 250 altitude the terminal pitch is `atan(250/5000)` = 2.9° → **`vFacing` −2 or −1**, which descends 7–15 wdist/tick and **cannot reach the ground before fuel-out**. This is condition (B), bought by geometry rather than by a one-tick window. |
| **Target** | **Humvee** (`vehicles-america.yaml:53,61`) — `Speed: 150`, `TurnSpeed: 19`, `TargetTypes: Ground, Vehicle, Light` | fastest ATGM-valid actor in the mod; `Vehicle` is in `ValidTargets` |
| **Target movement** | **beam/crossing** relative to the launcher, then a **direction reversal** ordered so it lands ~1000–1500 wdist before intercept | swings the aim point by `2·150·D/300` = 1000–1500, against a correction budget of `D²/1630` = 613–1380. **Uncorrectable at D ≤ 1500.** This is condition (A). |
| **Terrain** | **flat, single height level, no cliffs, well inside map bounds** | ATGM has **no** `TerrainHeightAware` (`Missile.cs:75` default false; not declared in YAML), so any rising terrain on the outbound line is a guaranteed ground detonation that masks the result. Flat also removes clause 3 and clause 6 as confounders. |
| **Launcher altitude** | **same height level as the target** | an elevated launcher steepens the terminal pitch and re-arms the ground clause |
| **Downrange clearance** | **≥ 20 cells of flat, empty, in-bounds map beyond the target**, along the missile's approach axis | `RangeLimit` 21504 minus ~5000 consumed = **~55 ticks × 300 = 16,500 wdist of post-miss flight**. If the map edge is closer, clause 6 terminates it early and the run reports `off_map` instead of the survival. |
| **Repetitions** | **≥ 40 shots**, varying reversal timing over 800–2000 wdist in ~200-wdist steps | the `offset` roll is stochastic; the reversal-timing sweep is the deterministic axis |

**Predicted signature of success in `result.missiles.jsonl`:**
`flystraight_latches ≥ 1`, `flystraight_state == "hitting"`, `end_tick − min_dist_tick` **> 5**,
`min_dist` and `min_aim_dist` both **> 298**, `reason == "fuel_out"` (or `off_map`), `damage == 0`.
That is precisely the record-id-34 fingerprint, reproduced from shipped code.

### 6.2 Secondary scenario — "stationary tank, maximal offset re-roll"

Cheap to run alongside; tests the §3.1 coincidence at 1536.

- Same launcher and flat terrain; target a **stationary `t90`** (matches the existing rig, so it
  is directly comparable to the 39 shipped records).
- **Range 6c0–8c0**, so the missile crosses the 1536 offset-freeze boundary at full speed on a tick
  divisible by 5.
- **Many repetitions (≥ 100)** — this is a tail event in the offset distribution and cannot be
  forced deterministically without editing the weapon, which is out of scope.
- Success signature: any ATGM record with `min_aim_dist > 298`. In the entire shipped corpus the
  maximum is 6 (excluding pre-fix and truncated records), so a single one is significant. **[trace]**

### 6.3 The loop-specific probe

If §6.1 produces survival but no *turn*, the orbit (§5.2) has not been excluded. To probe it:

- Target **stationary at the moment of arrival** but with the aim point displaced — i.e. a Humvee
  ordered to **stop** ~5 ticks before intercept, after having established a large lead term while
  moving. The lead collapses to zero on the stop, throwing the aim point laterally by `Vt·D/300`
  while removing the target motion that would otherwise destroy the orbit.
- Success signature: a record with **`flystraight_latches == 0`**, `end_tick` at or near the
  71–74-tick fuel ceiling, and a `hf` series in the tick stream that **rotates through more than
  128 facings (180°)**. That last one is the only unambiguous machine-readable evidence of a loop,
  and no existing record in the corpus shows it.

### 6.4 What the run must NOT do

- Do not raise `MaximumLaunchSpeed` or lower `MinRange` — either unmasks the §3.2 tick-1 airburst
  lead spike and contaminates the result with a different defect.
- Do not use an elevated or cliff-adjacent target: no `TerrainHeightAware` means the missile will
  clip terrain and every miss will be reported as `ground`.
- Do not use `t90` for the primary scenario. At `Speed: 100` (`vehicles-russia.yaml:173`) its
  reversal swings the aim point by only `2·100·D/300` = 667–1000, which is inside the correction
  budget at D ≥ 1050. The existing rig's exclusive use of `t90` is, I believe, the whole reason
  39 flights produced 39 detonations.

---

## 7. What I could not determine

- **Whether the §5.2 orbit is reachable.** I bounded it and removed the one damping mechanism I
  expected to kill it, but I did not demonstrate an entry trajectory. If the user's missile
  genuinely turned back, this is the only code path that produces it and it deserves the §6.3
  probe. If §6.3 comes back negative, the loop is **not** in `Missile.cs` and the investigation
  should move to the *launcher* — repeat fires from `AttackFrontal`/`Armament` producing a second
  missile that a player reads as the first one coming back — which I did not examine at all.
- **The user's actual observation.** I have no recording. "Loops back around and re-homes" is
  consistent with (A)+(B) survival plus a *second* missile from the same launcher, and I cannot
  distinguish that from a single looping missile without the run.
- **Sub-tick rendering.** `renderFacing` is derived from the move vector (`:1127`) and the contrail
  persists for 5 ticks (`ContrailLength: 5`). I did not evaluate whether a latched missile's frozen
  heading combined with the contrail could *look* like a curve. Worth ten seconds of thought before
  spending a measurement grant.
