# Missile diagnosis — Mi-28 / Ataka misses, plus fleet-wide missile audit

Researched against `main @ bf54c71d` (`git status -sb`: `main...origin/main [ahead 112]`,
only untracked files dirty — no source drift). Static analysis only; the one
autotest I intended to run was blocked (see §8).

---

## 1. Verdict in one paragraph

**Every `TerrainHeightAware: true` missile in WW3MOD flies straight over the top of
every ground target it is fired at, and detonates several cells past it at fuel-out.**
This is a hard, unconditional bug — not a probabilistic one — caused by a
false-triggering cliff-avoidance branch in `Missile.cs`. It affects Ataka (Mi-28),
WGM/WGM.bradley (BMP-2, Bradley) and Hellfire/Hellfire.strykershorad (Apache,
Littlebird, Stryker SHORAD). It does **not** affect air targets, which is why the
mod's existing heli-vs-heli missile coverage never caught it. This single defect
accounts for the user's symptom (2) and for the great majority of "consistently
misses". Symptom (1) — explodes a cell short — has a separate and *intended* cause
(the foliage miss roll), plus a latent second cause that will surface the moment
the primary bug is fixed.

---

## 2. Primary root cause — `allowPassBy` latches on every ground shot

### 2.1 The trigger

`engine/OpenRA.Mods.Common/Projectiles/Missile.cs:660`

```csharp
if (info.TerrainHeightAware && lastHt >= targetPosition.Z)
    allowPassBy = true;
```

Both operands are provably zero in WW3MOD:

**`lastHt` is always 0.** It is only written inside `InclineLookahead` when the
probe sees a *height change* (`Missile.cs:569-574`), and `prevHt` starts at 0
(`:551`). The probe reads `world.Map.Height[cell] * 512` (`:560`). WW3MOD's
`mod.yaml:317-319` declares `MapGrid: TileSize: 24,24 / Type: Rectangular` and
**omits `MaximumTerrainHeight`**, whose default is `0` (`engine/OpenRA.Game/Map/MapGrid.cs:110`).
So the height layer is uniformly zero on every map, `ht` never changes, and
`lastHt` (and `lastHtChg`) stay at their initialised 0 (`:535-538`).

**`targetPosition.Z` is always 0 for a ground actor.** For `MapGridType.Rectangular`,
`Map.CenterOfCell` returns `new WPos(1024*x+512, 1024*y+512, 0)` — no height term at
all (`engine/OpenRA.Game/Map/Map.cs:1425-1426`). Ataka does not set
`TargetActorCenter` (default `false`, `engine/OpenRA.Game/GameRules/WeaponInfo.cs:159`),
so `Missile.cs:983` uses `GuidedTarget.Positions.ClosestToIgnoringPath(...)`; those
come from `HitShape`, whose `TargetableOffsets` defaults to `{ WVec.Zero }`
(`engine/OpenRA.Mods.Common/Traits/HitShape.cs:29`) and which no WW3MOD vehicle
overrides. `AirburstAltitude` is `WDist.Zero` for Ataka, so nothing is added.

Therefore `0 >= 0` → **`allowPassBy = true` on the first `Hitting` tick of every
shot at a ground target**, and it is a sticky field — it never clears.

Note this is *stronger* than "flat maps": since `targetPosition.Z` is structurally 0
for the Rectangular grid and `lastHt` is a non-negative height, `lastHt >= targetPosition.Z`
would still hold even if the mod later enabled terrain height.

### 2.2 What the missile then does

With `allowPassBy` set, `Missile.cs:663` (`if (!allowPassBy && …)` — the *aim at the
target* branch) is skipped, and `:701` (`else if (allowPassBy || …)`) is taken. That
branch is the cliff-crossing manoeuvre. `targetPassedBy` is always passed as `false`
(`:850`), so:

- While `relTarHorDist > 2 * loopRadius`: `d1 = relTarHorDist - lastHtChg` = the full
  distance (because `lastHtChg == 0`), so `d1 > 2*loopRadius` and the code does
  `ChangeSpeed(); return 0;` (`:722-726`) — **`desiredVFacing = 0`, i.e. dead level flight.**
- Below `2 * loopRadius`: it computes a pop-up over a zero-height "cliff" located at
  the missile's own position (`h1 = loopRadius - ISqrt(d1*(2*loopRadius-d1)) - (pos.Z - lastHt)`,
  `:730`) and commands a **climb** (`:733`).

The missile therefore never pitches down onto the target. Worked example, Mi-28
(`Aircraft.CruiseAltitude: 3c768` = 3840, `aircraft.yaml:368`) at 18 cells against a
stationary t90, using Ataka's values (`Speed: 400`, `Acceleration: 30`,
`MaximumLaunchSpeed: 80`, `CruiseAltitude: 100`, default `VerticalRateOfTurn` =
`WAngle(24)` → 6 facing units/tick = 8.4°):

| phase | what happens |
|---|---|
| launch → +11 ticks | speed ramps 80→400 over ~2850 wdist; cruise branch (`:798-812`) commands descent, but `desiredVFacing` is clamped to ±6 facing = **8.4°** (`:809`) |
| descent | 8.4° descent needs 25 300 wdist of horizontal travel to fall 3740 — longer than the whole shot at most ranges |
| `relTarHorDist ≤ 3*loopRadius` (8151 at speed 400) | `state = Hitting`, `allowPassBy = true` |
| Hitting, `d1 > 5434` | `desiredVFacing = 0` → **level** |
| Hitting, `d1 ≤ 5434` | pop-up climb to ≈600–680 wdist, then levels |
| over the target | vertical miss ≈ **600–3400 wdist** depending on how much altitude it shed |

`CloseEnough` is 298 (default, `Missile.cs:203`; Ataka does not override it), so:

- the proximity fuse `relTarDist < CloseEnough` (`:1051`) never fires — the 3-D
  distance never drops below ~600;
- the terminal segment closest-approach check (`:1070-1092`) never fires either — its
  minimum-distance test is also against `CloseEnough`;
- `height.Length < 0` (ground hit, `:1050`) never fires — the missile is above ground.

So `flyStraight` latches (`:839`), the missile sails on, and `ExplodeWhenEmpty`
(default `true`, `:120`) detonates it at fuel-out (`:1052`). For an 18-cell shot with
`RangeLimit: 22c0` that is **≈3.5–4 cells beyond the target**, in empty air.

### 2.3 Why this reads as the user's symptom (2)

"The missile appears to never reach the target and there is no explosion at all" — the
explosion happens, but 3–4 cells past the target and several hundred wdist up, outside
where the player is looking. When the shot is taken toward a map edge the missile can
also leave the map (`!world.Map.Contains(cell)`, `:1053`) and detonate off-screen —
genuinely no visible explosion.

### 2.4 Why air targets are fine

A helicopter target has `targetPosition.Z` ≈ 3840, so `lastHt(0) >= 3840` is **false**,
`allowPassBy` is never set, and `:663` takes the correct aim-at-target branch. This is
exactly why `test-heli-vs-heli-missile` and `test-balance-heli-1v1` pass while
air-to-ground is broken, and why the defect survived.

### 2.5 Regression history

- `000a3795` "Fix missile speed freeze: allowPassBy falsely triggered for
  non-terrain-aware missiles" — correctly identified *this same* `lastHt == 0` /
  `targetPosition.Z == 0` collision, but fixed only the `TerrainHeightAware: false`
  half by adding the `info.TerrainHeightAware &&` guard.
- `85374503` "Fix guided missiles aiming at cruise altitude instead of target" — patched
  the fallthrough that `000a3795` created, again only for non-terrain-aware missiles
  (`!info.TerrainHeightAware` added at `:663`).

Both commits reasoned as if `lastHt` were meaningful whenever `TerrainHeightAware` is
true. The in-code comment at `:656-659` states that premise explicitly ("Only allow
pass-by for terrain-aware missiles where lastHt is meaningful") and it is wrong:
`lastHt` is meaningful only when the lookahead actually crossed a height transition,
which in this mod is never.

---

## 3. Symptom (1) — explodes a cell short

Two causes, one intended and one latent.

### 3.1 Intended: the foliage miss roll (working as designed, but bounded)

`engine/OpenRA.Mods.Common/Traits/Armament.cs:529-588`. On a failed roll the shot is
redirected to a tree on the firing line (`:582`) and `GuidedTarget` is set to
`Target.Invalid` (`:583`) — the missile then detonates at the tree, i.e. **short of the
target**, which is precisely what the user describes.

Magnitude is **capped at 30%**, not the 30–60% the manager suspected. The shot is only
allowed at all when `FiringLOS.HasClearLOS` passes, which is `shadow <= threshold`
(`FiringLOS.cs:119`) with Ataka's `ClearSightThreshold: 3`. So density ∈ {0..3} at fire
time, `excess = density - FreeLineDensity(1)` ∈ {0,1,2}, and
`missPct = excess * MissChancePerDensity(15)` ∈ {0%, 15%, 30%} (`Armament.cs:532-535`).

For an airborne firer the density used is the `airborneShadow` channel
(`FiringLOS.cs:147-164`), which only accumulates from tiles in the **last 25%** of the
line (`obstacleHeight 512 > z_los = 2048*(1-t)` ⇒ `t > 0.75`) and at **⅕ weight**
(`Map.cs:1167-1169`). So a Mi-28 is much less foliage-sensitive than a ground ATGM —
30% is the ceiling and requires meaningful canopy right on top of the target.

**Verdict: leave alone.** 15–30% in woods is defensible realism. It is however visually
illegible (see §7).

### 3.2 Latent: the short ground-hit that `TerrainHeightAware` was added to mask

`mods/ww3mod/rules/weapons/weapons-missiles.yaml:73-76` documents it:

> Terrain-aware homing keeps the missile above ground during the descent from
> CruiseAltitude — without it the dive into target overshoots ground 1.7 cells short
> of target, and the missile detonates as a "ground hit".

That is symptom (1) verbatim. The mechanism: in the correct aim-at-target branch
(`:663-700`) `desiredVFacing` is rate-limited by `VerticalRateOfTurn`, which none of
Ataka / WGM / Hellfire override — so it is the default `WAngle(24)` = **6 facing units
= 8.4°/tick** (`Missile.cs:102`). A missile still 2500–3400 wdist high at 3 cells
out needs about −45° (−32 facing); at 6/tick that takes 5+ ticks and 2000+ wdist of
travel, during which it descends into the ground short of the target
(`height.Length < 0`, `:1050`).

**This matters for the fix**: repairing `allowPassBy` puts these missiles back into the
aim-at-target branch, so §3.2 will resurface unless `VerticalRateOfTurn` is raised in
the same change. Do not ship one without the other.

---

## 4. Amplifier — why a near-miss looks like nothing happened (H3: confirmed)

`Missile.Explode` always calls `args.Weapon.Impact(Target.FromPos(pos), warheadArgs)`
(`Missile.cs:1156`) — never the actor. So `TargetDamageWarhead` is purely positional:
`DoImpact` does `FindActorsOnCircle(pos, Spread)` and requires
`hitshape.DistanceFromEdge(victim, pos) <= Spread`
(`engine/OpenRA.Mods.Common/Warheads/TargetDamageWarhead.cs:38-66`). Ataka does **not**
set `Spread` on its `Warhead@Target`, so it is the default **`WDist(1)`**
(`TargetDamageWarhead.cs:24`) — the detonation must be within 1 wdist of the hull
surface, i.e. a literal hit.

Everything else falls back to `Warhead@Spread` (`Spread: 192`, `Damage: 2000`,
`Penetration: 20`, `weapons-missiles.yaml:144-150`). Against a t90
(`Armor Type: Heavy, Thickness: 280, HP: 24000`, `vehicles-russia.yaml:319-321` and `:318`), `DamageWarhead` applies `damage = damage * penetration / thickness` when
penetration < thickness (`DamageWarhead.cs:224-230`):

| outcome | damage to a t90 | missiles to kill |
|---|---|---|
| direct hit (`TargetDamage`, Pen 900 > 280) | 10 000 | ~2.4 |
| near-miss on the hull edge (`SpreadDamage`, 2000 × 20/280) | **≈142** | ~169 |
| miss at 484 wdist (max inaccuracy offset) | ≈13 after falloff | ~1800 |

A ~70× cliff between hit and near-miss. So even a *modest* guidance error reads as a
total whiff. This is an amplifier, not a root cause — with guidance fixed, direct hits
become the norm. **Recommend re-measuring after the guidance fix before touching it**;
raising `TargetDamage.Spread` to 192 would hand the 10 000-damage Pen-900 warhead to
everything within 0.19 cells, which is a significant unpriced buff.

For comparison, Ataka's own max inaccuracy at max range is `22 × 22 = 484` wdist
(`Util.GetProjectileInaccuracy`, `InaccuracyType.PerCellIncrement`,
`engine/OpenRA.Mods.Common/Util.cs:409-410`) — 2.5× the `SpreadDamage` Spread. So the
weapon's own designed scatter already exceeds its own splash radius.

---

## 5. Hypothesis verdicts

| # | Hypothesis | Verdict |
|---|---|---|
| **H1** | Fuel-out (`RangeLimit == Range`) | **Real but second-order for Ataka.** *The manager's premise that the launch-speed ramp burns fuel is wrong*: `distanceCovered += new WDist(speed)` (`:1047`) and the homing move vector has length exactly `speed` (`:899-902`), so fuel is spent per unit **distance**, not per tick — the 80→400 ramp costs time, not range. What does cost range is the 8.4°-clamped descent from 3840: path ≈ D/cos(8.4°) = 1.0107·D, so 22 528 wdist of fuel reaches only ≈21.8 cells of the 22-cell band. Bites the top ~0.2–1 cell. Bigger for `SurfaceToAirMissile` (`RangeLimit 35c0 == Range 35c0` against targets at 3840 altitude) and `WGM` (`25c0 == 25c0`). |
| **H2** | Terminal segment check gated off in Freefall | **Real, but not what the user is seeing.** `:1070` gates on `state != States.Freefall`. However Freefall only latches at fuel-out, by which time the missile is already past the target. Worth fixing defensively; it is not the cause. |
| **H3** | Warheads make near-misses invisible | **Confirmed and quantified** — see §4. Amplifier, not root cause. |
| **H4** | SACLOS `SpeedMultiplier` / Hover / `FacingTolerance` / `MinRange` | **Killed.** `SpeedMultiplier@FiringAtaka` modifies the *helicopter's* speed, not the missile's. And launch heading is computed directly from source→`PassiveTarget`, explicitly bypassing turret alignment (`Missile.cs:278-284`), so `FacingTolerance`/Hover cannot produce a stale aim. `ManualGuidance` only checks `args.SourceActor.IsDead` (`:1013`) — losing *sight* does not drop guidance; `targetPosition` simply freezes at last-known (`:982`), which is correct behaviour. |
| **H5** | Foliage roll firing too often | **Real, bounded at 30%, working as designed** — see §3.1. Not "consistent". |
| **H6** | Operator retargeting misfiring | **Working as designed, with one genuine defect** — see §7. |

---

## 6. Proposed fixes

### FIX 1 (required) — stop `allowPassBy` triggering without a real incline

`engine/OpenRA.Mods.Common/Projectiles/Missile.cs`, in `HomingInnerTick`.

The gate must depend on the lookahead having actually *seen* a height transition, not
on `TerrainHeightAware` alone. `lastHtChg` is the honest signal: it is only non-zero
when `InclineLookahead` recorded a change (`:571`).

Replace `:660` and `:663`:

```csharp
// An incline is only "known" when the lookahead actually crossed a height
// transition. On a mod with MaximumTerrainHeight 0 the height layer is
// uniformly zero, so lastHt/lastHtChg stay at their initialised 0 and the
// old `lastHt >= targetPosition.Z` test was 0 >= 0 — true on every ground shot.
var inclineKnown = info.TerrainHeightAware && lastHtChg > 0;

if (inclineKnown && lastHt >= targetPosition.Z)
    allowPassBy = true;

if (!allowPassBy && (!inclineKnown || lastHt < targetPosition.Z || targetPassedBy))
{
    // ... existing aim-at-target body, unchanged
}
```

**Why both lines.** Changing only `:660` is *not* enough and would reproduce the
`85374503` bug: with `allowPassBy` false, `TerrainHeightAware` true, `lastHt == 0` and
`targetPosition.Z == 0`, the `:663` condition is also false, and the missile falls
through to the final `else` (`:782-796`) which aims for **cruise altitude** rather than
the target. Routing `:663` through the same `inclineKnown` flag sends flat-terrain
shots into the aim-at-target branch, which is the correct destination and the one
`85374503` already chose for non-terrain-aware missiles.

Behaviour on mods that *do* have real height levels is unchanged, because there
`lastHtChg > 0` whenever an incline is in the lookahead window.

This also subsumes `000a3795` — the `TerrainHeightAware` term becomes redundant but is
kept for clarity and zero risk.

Add a `// PITFALL:` at the gate per `DOCS/reference/conventions.md` §PITFALL comments —
this is exactly the class of trap that has now caused three commits.

### FIX 2 (required, ships with FIX 1) — let the missiles actually pitch down

Without this, FIX 1 resurfaces the "ground hit 1.7 cells short" of §3.2. The cause is
`VerticalRateOfTurn` defaulting to `WAngle(24)` = 6 facing = 8.4°/tick, against a
launch altitude of 3840.

This is a **judgement call — option space, not a recommendation**:

| option | change | effect | cost |
|---|---|---|---|
| **2a** (narrowest) | Add `VerticalRateOfTurn: 60` (→15 facing, 21°/tick) to Ataka and Hellfire in `weapons-missiles.yaml` | descent from 3840 to cruise takes ~9.5 cells instead of ~25; terminal dive corrections resolve in ~2 ticks | slightly "snappier" than a real ATGM looks |
| **2b** | `VerticalRateOfTurn: 96` (→24 facing, 33.75°/tick) | descent in ~5.5 cells; effectively removes the geometry problem | most arcade-looking |
| **2c** | Leave turn rate alone; instead raise `CruiseAltitude` on Ataka/Hellfire from 100 / 512 to ~2c0 so the missile flies a flat, high path and dives only at the end | preserves the slow-pitch feel | the terminal dive is then *steeper*, so it needs 2a anyway — probably not viable alone |
| **2d** | Lower the Mi-28's `Aircraft.CruiseAltitude` | fixes the geometry at the source | changes heli survivability and silhouette everywhere; large blast radius, not recommended |

My read: **2a** is the smallest change that works and keeps the "heavy, deliberate
ATGM" feel. Worth verifying in-game that the missile visibly *arcs* rather than snaps.

Note WGM (BMP-2/Bradley) launches from ground level, so its descent geometry is benign
and 2a is likely unnecessary there — but see the fleet table.

### FIX 3 (cheap, defensive) — H2

`Missile.cs:1070`: drop the `&& state != States.Freefall` gate on the segment
closest-approach check. The check only fires when the swept segment passes within
`CloseEnough` of the aim point, which is precisely when a detonation is wanted; there
is no reason a fuel-starved missile should be allowed to pass *through* a target
without functioning. Low risk.

### FIX 4 (judgement call) — fuel margin

`RangeLimit` should exceed `Range` because the flown path always exceeds the straight
line. Hellfire already sets the precedent at `27c0` vs `Range 25c0` (a 2-cell, 8% margin).
Applying that convention:

| weapon | now | proposed | why |
|---|---|---|---|
| `Ataka` | `RangeLimit 22c0` = `Range 22c0` | `24c0` | 8% margin; covers the descent path premium and the terminal weave |
| `WGM` | `25c0` = `25c0` | `27c0` | same |
| `SurfaceToAirMissile` | `35c0` = `35c0` | `38c0` | worst case in the mod: engages targets 3840 up at 35 cells out, so the 3-D path genuinely exceeds the horizontal range |

The exact margin is the user's call. 8% matches the existing Hellfire convention; the
alternative is `RangeLimit: -1` (unlimited fuel, `Missile.cs:107`) which removes
the failure mode entirely at the cost of missiles that never self-destruct.

### FIX 5 (defer) — warhead cliff, §4

Do **not** change until FIX 1+2 are measured. If near-misses still dominate after the
guidance fix, the option space is: (a) raise `Warhead@Target.Spread` from the default 1
to ~128, (b) raise `Warhead@Spread.Penetration` from 20 toward the target thickness
band, or (c) reduce Ataka's `Inaccuracy` from 22/cell so its own scatter no longer
exceeds its own splash radius. Each is a balance change, not a bug fix.

---

## 7. Working as designed — leave alone

The user asked which "lost tracking" behaviours are intended. These are:

1. **Operator retargeting when the target dies mid-flight** (`Missile.cs:929-978`).
   Deliberate, documented, veterancy-scaled. Keep.
2. **Abandoning a target in `Critical` damage state** (`:942-945`, and the same filter in
   `FindRetargetCandidate` `:1119-1121`). *Flagging for a user decision rather than
   calling it a bug*: it means a missile in flight toward a tank you have been chewing on
   will visibly swing off to something else. That is the "lost tracking for no reason"
   the user may be seeing, and it will happen *more* once missiles start hitting. It is
   intentional ("no point spending the warhead on a wreck"), but it is the single most
   likely candidate for "this looks like a bug but isn't". Worth confirming the user
   wants it.
3. **`ManualGuidance` going ballistic when the shooter dies** (`:1013`). Correct for
   SACLOS/wire-guided fiction.
4. **Losing the target to fog** — `targetPosition` freezes at last-known (`:982`) and the
   missile keeps homing there. Correct; the comment at `:874-881` explains why the older
   heading-freeze behaviour was removed.
5. **The foliage miss roll**, §3.1 — bounded at 30%, keep.
6. **`FlyStraightIfMiss`** overshoot behaviour (`:839-844`). Correct.

### One genuine defect in this area (small)

The foliage redirect sets `args.GuidedTarget = Target.Invalid` (`Armament.cs:583`).
Operator retargeting then sees an invalid target on the very next tick; for a
**max-veterancy shooter** `veterancyScalePct` is 0, so `retargetCountdown = 0`
(`Missile.cs:950-954`) and the `retargetCountdown == 0` branch runs **in the same tick**
(`:957`) — the missile immediately re-acquires a live enemy and the intended foliage
miss is silently cancelled. Rookies wait the full 50 ticks and usually reach the tree
first. Net: **the foliage system quietly stops applying to veteran units.** Suggested
fix: mark foliage-redirect shots so operator retargeting skips them (e.g. a
`ProjectileArgs` flag, or set `GuidedTarget` to the tree actor rather than `Invalid` so
`IsValidFor` stays true). Low priority, but it makes veterancy silently bypass a
designed drawback.

### Cosmetic: the foliage miss is illegible

The manager asked whether the tree redirect reads as "hit a tree". It does not.
`Ataka` inherits `^MediumExplosionEffects` (`weapons-effects.yaml:544`), whose
`Warhead@Effect: CreateEffect` lists `ValidTargets: Ground, Ship, Trees, Mine`
(`:553-556`) — the same `explosion_medium` plays whether it clipped a tree or hit dirt.
So the player sees "missile blew up in mid-air a cell short", i.e. exactly the bug they
are reporting. If the foliage system is to stay, consider a distinct impact effect or a
tree-shake/leaf-burst on the redirect path so the mechanic is self-explaining.

---

## 8. Fleet audit

All ten live `Projectile: Missile` weapons are in
`mods/ww3mod/rules/weapons/weapons-missiles.yaml`. `mods/ww3mod/rules/ingame/naval.yaml`
contains four more (`^AntiGroundMissile`, `CruiseMissile`, `^SubMissileDefault`,
`TorpTube`) but **every line is commented out** (`naval.yaml:843-970`) — inert.

Derived columns: `hRoT facing = WAngle/4` (`engine/OpenRA.Game/WAngle.cs:67`);
turn radius `= speed*6400/(157*facing)` (`Missile.cs:343-350`); inaccuracy at max range
per `Util.cs:401-416`. `CloseEnough` is 298 wherever unset (`Missile.cs:203`).

| weapon | carried by | Range / RangeLimit | Speed vs CloseEnough | hRoT → turn radius | Inacc @ max | warhead Spread | **defects** |
|---|---|---|---|---|---|---|---|
| **Ataka** | MI28 (`aircraft-russia.yaml:261`, armament `:362`) | 22c0 / **22c0** | 400 vs 298 (1.34×) | 20→5f, 3.2c | 484 (0.47c) | 192 | **overfly (THA)**, no fuel margin, launched from 3840 |
| **WGM** | bmp2 (`vehicles-russia.yaml:118`, armament `:217`) | 25c0 / **25c0** | 300 vs 298 (1.01×) | **8→2f, 6.0c** | 500 (0.49c) | 192 | **overfly (THA)**, no fuel margin, very wide turn |
| **WGM.bradley** | bradley (`vehicles-america.yaml:280`, armament `:382`) | inherits WGM | inherits | inherits | inherits | inherits | same as WGM |
| **Hellfire** | littlebird (`aircraft-america.yaml:99`, armament `:190`), HELI/Apache (`:260`, armament `:348`), A10 (`:410`, armament `:469`) | 25c0 / 27c0 ✅ | 500 vs 298 (1.68×) | 60→15f, 1.3c | 256 (0.25c) | 192 | **overfly (THA) vs ground only**; air targets fine |
| **Hellfire.strykershorad** | strykershorad (`vehicles-america.yaml:798`, armament `:930`) | 25c0 / 27c0 ✅ | 400 vs 298 | 60→15f, 1.1c | 256 | 192 | **overfly (THA)** — and its `ValidTargets` is `Vehicle, Defense`, i.e. *ground only*, so it is broken against 100% of its targets |
| **ATGM** | ^AT infantry (`infantry.yaml:1654`, armament `:1675`) | 20c0 / 21c0 ✅ | 300 vs 298 | 20→5f, 2.4c | 512 (0.5c) | 64 | not THA → **guidance OK**. But `CruiseAltitude: 10c0` (`:20`) is 20× every other ATGM — almost certainly a typo for `1c0`; makes the missile lob high for no reason. Spread 64 is the tightest in the fleet |
| **SurfaceToAirMissile(.double)** | SAM (`structures-defenses.yaml:754`, armament `:793`) | 35c0 / **35c0** | 800 vs 400 (2.0×) | 35→8f, 4.0c | 400 | 128 | no fuel margin **and** engages targets 3840 up at 35 cells — the strongest genuine H1 case in the mod |
| **AirToAirMissile** | F16 (`aircraft-america.yaml:525`, armament `:558`), MIG (`aircraft-russia.yaml:535`, armament `:572`) | 30c0 / 35c0 ✅ | 800 vs 400 | 25→6f, **5.3c** | 400 | 128 | turn radius 5.3 cells against a manoeuvring jet is marginal |
| **MANPAD** | ^AA infantry (`infantry.yaml:1726`, armament `:1745`) | 23c0 / 24c0 ✅ | 450 vs **192 (2.34×)** | 20→5f, 3.6c | 256 | 192 | worst Speed/CloseEnough ratio in the fleet — depends entirely on the segment check (FIX 3 matters here) |
| **Stinger / .quad / 9M311** | strykershorad quad (`vehicles-america.yaml:798`, armament `:898`), tunguska (`vehicles-russia.yaml:747`, armament `:860`) | 28c0 / 30c0 ✅ | 600 vs 256 (2.34×) | 20→5f, 4.8c | 300 | 256 | same straddle ratio; 4.8-cell turn radius vs helicopters |
| **TimerWolf_Missiles** | **orphaned** — its only reference is commented out (`vehicles.yaml:736`) | 25c0 / 27c0 | 850 vs 298 (2.85×) | **5→1f, 33.8c** | 1c0 | 64 | unreachable; a 33.8-cell turn radius means it cannot turn at all. Dead weight — delete or fix if ever wired up |

### Defect groupings

- **Overfly bug (FIX 1):** Ataka, WGM, WGM.bradley, Hellfire, Hellfire.strykershorad —
  i.e. **the entire anti-tank guided-missile fleet on both sides**. Hellfire is masked
  air-to-air; Hellfire.strykershorad and WGM are not masked at all.
- **No fuel margin (`RangeLimit <= Range`, FIX 4):** Ataka, WGM(+bradley),
  SurfaceToAirMissile(+.double).
- **`Speed > CloseEnough` straddle (FIX 3 relevance):** all ten. Handled while Homing by
  the existing segment check; MANPAD and Stinger (2.34×) and TimerWolf (2.85×) are the
  most exposed if that check is ever skipped.
- **Arm vs flight time:** no problems found. `Arm: 2` (Ataka/WGM) and `Arm: 5`
  (SAM/Stinger/MANPAD) are all far below any realistic time-to-target; `Explode` returns
  early only while `ticks <= Arm` (`:1147`).

### Ranking by real-world impact

1. **Ataka / Mi-28** — the reported bug; a 6000-cost unit whose main weapon never works.
2. **Hellfire.strykershorad** — broken against 100% of its valid targets, and nobody has
   noticed.
3. **WGM / WGM.bradley** — BMP-2 and Bradley ATGMs, both sides' mainline IFV.
4. **Hellfire on Apache/Littlebird** — broken air-to-ground, works air-to-air.
5. **SurfaceToAirMissile** — fuel-out at long range against high fliers (H1 only).
6. **ATGM `CruiseAltitude: 10c0`** — probably a typo; cosmetic/behavioural oddity.
7. **TimerWolf_Missiles** — orphaned, ignore or delete.

---

## 9. Unrelated bug noticed in passing

The Mi-28 references an armament named `secondary-air` in three places —
`aircraft-russia.yaml:312` (`AttackAircraft.Armaments`), `:322`
(`GrantConditionOnPreparingAttack.ArmamentNames`), `:367` (`AmmoPool@2.Armaments`) — but
**no `Armament` with `Name: secondary-air` is defined on the actor** (only
`Armament@1` = `primary` at `:328` and `Armament@2` = `secondary` at `:360`). The
references dangle silently. Meanwhile the Buildable description claims "Can engage
aircraft" (`:291`) while `Ataka` is `ValidTargets: Vehicle, Defense` — no `Air`
(`weapons-missiles.yaml:111`). This is already written up as
`WORKSPACE/balance/003-mi28-secondary-air.md` (status PROPOSED) and is still unfixed at
`bf54c71d`. Out of scope here; noting that it is real and current.

---

## 10. What I could not verify statically

1. **Empirical confirmation of the overfly.** `tools/autotest/scenarios/test-mi28-fires-ataka/`
   is the exact instrument: an 18-cell shot at a stationary t90 on a **treeless** map
   (`map.yaml:41-56` places only two supplyroutes and the t90), which isolates guidance
   from the foliage roll. I attempted the single permitted run and the harness refused —
   `another autotest run is already in flight (pid 97572, test-burn-demo)`; it is
   single-instance. **My run was never consumed.**

   **Recorded prediction, made before any run:** the Mi-28 fires (secondary-ammo
   decrements), the missile overflies the t90 at roughly 0.6 cells altitude and detonates
   ≈3.5–4 cells past it at fuel-out, and the t90 takes **0** damage — so the test fails on
   `Ataka fired but t90 took only 0 damage (need >= 2000)`. If it instead passes, my §2
   analysis is wrong and everything downstream of it should be discarded.

2. **Actual `airborneShadow` values on the woodland maps.** I derived the *mechanism* and
   the 30% ceiling from the LOS gate, which is airtight, but the real distribution of
   density values on `seventh-woods-ww3` / `woodland-warfare-ww3` (and hence whether the
   typical Mi-28 shot rolls 0%, 15% or 30%) needs an in-game probe or a read of the baked
   `shadows.bin`. This only affects how much of symptom (1) is foliage.

3. **The precise altitude at which the missile crosses the target.** My 600–3400 wdist
   range comes from hand-integrating the `HomingInnerTick` branches at 25 ticks/s; the
   integer trigonometry (`WAngle.Sin/Cos`, `Exts.ISqrt`) will shift it. The *sign* of the
   result — always well above `CloseEnough` 298 — is robust; the magnitude is not.

4. **Whether FIX 2 alone is sufficient** to prevent the §3.2 short ground-hit once FIX 1
   lands, and which of options 2a–2c looks right on screen. That is a
   `DOCS/recipes/SCREENSHOT.md` + autotest question, not a static one.

5. **Whether WGM (ground-launched) needs FIX 2.** Its launch geometry is much gentler, so
   FIX 1 alone may be enough — but its `HorizontalRateOfTurn: 8` (2 facing/tick, 6-cell
   turn radius) is the worst in the fleet and may produce its own terminal miss against
   movers. Needs a test.

### Suggested test plan (needs the user's goahead)

1. `run-test.sh test-mi28-fires-ataka` — baseline, expected FAIL. *(Confirms §2.)*
2. Apply FIX 1 + FIX 2a; rerun the same test — expected PASS.
3. `run-test.sh test-heli-vs-heli-missile` — regression guard: air-to-air must not change
   (it never took the `allowPassBy` branch, so byte-identity is the expectation).
4. A new scenario for a ground-launched ATGM (bmp2/bradley vs t90) — currently uncovered,
   and the reason WGM's breakage went unnoticed.
