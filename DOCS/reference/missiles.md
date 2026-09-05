# Missiles — how they are intended to work

**Status: agreed with the user 2026-08-13.** This is the reference that settles
whether a given missile behaviour is a bug. It states INTENT. Where the shipped
code disagrees with this document, the code is wrong.

Current *behaviour* — as opposed to intent — is documented in
`WORKSPACE/audit/*.md` (audited against `main @ dc899995`). **Read
`missile-audit-review.md` before trusting any of the other four**: it falsified
load-bearing claims in two of them. Treat every number in those reports as
provisional.

This document exists because the missile system has repeatedly been
*misunderstood* rather than merely broken — the Javelin's top-attack cruise
altitude was nearly "fixed" as a typo, and four separate hand-derived analyses
of the guidance code reached wrong conclusions.

## 1. Invariants — these bind every missile, no exceptions

### I1 — Hit probability is distance-invariant

> *User ruling, verbatim:* "Should have the same hit chance regardless of
> distance. If we want to limit firing to a min distance, we can set that on the
> weapon, but as long as the weapon can fire the missile should be able to hit."

A weapon's hit chance must not vary systematically with engagement range across
its permitted envelope. Range limiting is the job of the weapon's declared
`MinRange`/`Range` — never an emergent consequence of projectile physics.

Binding consequences:

- Any mechanism where geometry, arming delay, homing-activation delay or turn
  radius makes near or far shots systematically worse is a **defect, even where
  it is physically realistic.**
- **"Make the launcher refuse the shot" is NOT an acceptable fix** for a missile
  that cannot hit. If the weapon is allowed to fire, the missile must be able to
  hit. Narrowing `MinRange` is fine; leaving a permitted-but-hopeless band is not.

### I2 — A missile always resolves visibly

Every missile ends in a detonation the player can see and hear, at a place that
explains what happened. A missile removed silently, or detonating with no sprite
and no sound, is a defect regardless of the damage it did.

*(Two real instances of this, both now fixed: the Javelin had no air-effect
warhead, so any detonation above the render threshold produced nothing at all;
and `Explode()` can remove a missile before its arming tick without applying a
warhead, which from outside is identical to a dud.)*

### I2b — A missile that has missed NEVER reacquires that target

> *User ruling 2026-08-13, verbatim:* "After a missile misses it's target they
> should never try to reaquire" — and, asked to scope it, *"For any missile I
> mean."*

Fleet-wide, no exceptions. Once a missile has genuinely missed, it must not
re-home on that target under any circumstance. A missile looping back around a
target it has passed is a bug, always.

What it does INSTEAD is §3's per-class miss rule — self-destruct at closest
approach, or fly on to fuel-out. This invariant only forbids reacquisition; it
does not decide the disposal behaviour.

Note this does **not** forbid operator retargeting onto a DIFFERENT enemy after
the original target dies — that is intended and specified in §5. The rule is
about re-homing on the target that was missed.

### I3 — Randomness is legitimate; systematic failure is not

Missiles are *meant* to miss sometimes, unpredictably. What is unacceptable is a
deterministic mechanism that makes a whole class of shot fail. When diagnosing, the
question is never "did it miss?" but **"does this miss have a reason that scales
with something other than luck?"**

## 2. Weapon classes

| Class | Examples | Guidance | If the shooter dies |
|---|---|---|---|
| **SACLOS wire-guided** | `Ataka`, `WGM`, `WGM.bradley` | Operator holds the crosshair on target; shooter is committed while guiding | Goes ballistic |
| **Fire-and-forget** | `Hellfire`, `Hellfire.strykershorad` | Autonomous seeker after launch | Continues to target |
| **Top-attack** | `ATGM` (Javelin) | Climbs to cruise altitude, dives onto the roof | Continues |
| **Anti-air** | `MANPAD`, `Stinger`, `Stinger.quad`, `9M311`, `SurfaceToAirMissile` | Pursuit of a fast, manoeuvring target | Continues |
| **Cruise / strategic** | as fielded | Pre-programmed to a position | Unaffected |

`ATGM`'s `CruiseAltitude: 10c0` is **deliberate** — it is the top-attack climb
profile, paired with `TopAttack: true` which routes damage to the target's roof
facet. It is 20× its peers *on purpose*. Do not "fix" it.

## 3. The miss-detonation rule — per class

**Agreed 2026-08-13.**

| Class | On a confirmed miss |
|---|---|
| SACLOS wire-guided | **Self-destruct at closest approach** |
| Fire-and-forget | **Self-destruct at closest approach** |
| Top-attack | **Self-destruct at closest approach** |
| Anti-air | **Fly on to fuel-out, then detonate** |
| Cruise / strategic | Detonate at its programmed position |

Rationale: the three ground classes self-destruct so the player gets a visible
boom where the miss happened, and so splash can still matter. Anti-air is the
user's explicit exemption from self-destructing — an airburst at fuel-out is
both realistic and legible.

> **The anti-air exemption is about DISPOSAL only, never about reacquisition.**
> An earlier draft of this section said a missile chasing an aircraft "may
> reacquire". That was an over-reading of the user's original wording ("anti air
> missiles that can keep flying if they miss entirely, until they run out of
> fuel"), which grants continued *flight*, not renewed *homing*. **I2b overrides
> it and is fleet-wide with no exceptions**, per the user's later and explicit
> ruling. An AA missile that has missed flies on ballistically to fuel-out; it
> does not turn back onto the target it missed.

**"Confirmed miss" means the missile is past its closest approach and increasing
range IN PHYSICAL DISTANCE** — not in any lead-inflated or predicted measure.

> This wording is load-bearing, and the history is worth knowing. The shipped
> code originally tested a *lead-inflated* distance against `CloseEnough`, a
> *physical* constant — a unit error under which a target reversing course
> multiplied the measured distance with no change in real range. Measured: 38 of
> 44 traced latches fired while the missile was still physically CLOSING. Fixed
> in `1ec6f17c`; miss detection now runs on physical 3D separation.
>
> **That fix then exposed a latent defect it had been masking** — see I2b. The
> detonation test still measures to the *aim point* (`targetPosition +
> leadTarget + offset`, `Missile.cs:1104-1105`), so it and the miss test are no
> longer commensurable. Nothing bounds `offset` by `CloseEnough` — it is a PDF
> roll of `Inaccuracy` taken at launch (`:325`) and **re-rolled mid-flight** by
> the retarget block (`:1098`) — and `ATGM` rolls `Inaccuracy: 512`
> (`weapons-missiles.yaml:12`) against the default `CloseEnough: 298`
> (`Missile.cs:203`), so a missile can sit physically inside the proximity radius
> without fusing.
>
> **Open, not yet decided:** making the detonation test consistent with the miss
> test on physical separation. That is arguably a proximity-fuse defect in its
> own right — a missile 200 units from a tank that flies past is failing this
> section's closest-approach rule — but it changes when every missile in the game
> detonates and needs its own measurement.

### The fuse is THREE spheres, not two

Any reasoning about "did this shot miss and survive?" has to carry **three
different centres**, all radius `CloseEnough`, none of them the same point:

| Centre | Where | Used by |
|---|---|---|
| `targetPosition + leadTarget + offset` — the **aim point** | `Missile.cs:1104-1105` | the `relTarDist < CloseEnough` detonation clause (`:1163`) |
| `targetPosition + leadTarget` — the **lead point**, no `offset` | `Missile.cs:1194` | the swept-segment fallback fuse (`:1188-1214`) |
| `targetPosition` — the **actual target** | `Missile.cs:878` | the miss latch / `minDistanceToTarget` (`:879-884`) |

The middle row is the one people forget: the segment test deliberately drops
`offset` (see the PITFALL at `:1179-1187`, which justifies the lead term and is
silent about the offset), so it is centred between the tank and the aim point and
moves with the target's velocity.

The code above is verified; the numbers below are **measured, not re-derived
here** — a 556-flight shipped-configuration ATGM probe (`wt/javelin-probe`,
2026-08-14):

- **Missing the tank and missing the aim point is nowhere near sufficient to
  survive.** Nine flights were more than `CloseEnough` from *both* — and all nine
  still detonated, every one on the segment clause, several for four-figure damage.
- **`Inaccuracy` cannot buy a miss on its own.** Pushing the trajectory clear of
  the lead point pushes it toward the aim point, and vice versa. In 152 shots at a
  *stationary* `t90` — where the lead term is zero and the segment sphere collapses
  onto the tank — 35 records missed the aim point by more than `CloseEnough` and
  every one of the 35 hit the tank anyway.
- **Distance-to-aim-point is a proxy for engagement RANGE, not for miss distance.**
  It scales with how little flight the missile had to null its launch offset. Do
  not read it as "how badly this shot missed".

## 4. Arming and minimum range

- A missile arms fast enough to be lethal at the weapon's declared `MinRange`.
  **A weapon whose arming distance exceeds its own `MinRange` is misconfigured** —
  fix the arming or raise `MinRange`; never leave the gap.
- Guidance is active from launch, or after a delay short enough to be irrelevant
  at `MinRange`.
- **Launch pitch must permit engaging the weapon's actual envelope.** An anti-air
  weapon must be able to launch upward.

> `MaximumLaunchAngle` is a `WAngle` (1024 = 360°) that `Missile` re-decodes as
> `(sbyte)(Angle >> 2)`, so its usable raw range is `[0, 511]`, and the lint
> (`CheckAngle`) further caps it at 255 (`Lint/CheckAngle.cs:56-58`,
> `InvalidAngle` = `value > 255 && value < 769`). A raw value above 511 silently
> becomes a *downward* angle. `CheckAngle` validates that an angle is well-formed,
> **never that it points the right way** — a green lint is not evidence a launch
> angle is correct. This is how MANPAD/Stinger shipped clamped below the horizon.
> Both now carry `MaximumLaunchAngle: 252` (`weapons-missiles.yaml:496`, `:537`),
> the clean `WAngle.FromFacing(63)` encoding one facing unit short of vertical —
> the geometric ceiling is facing `+64` (`Missile.cs:449-450` derives pitch from
> `WVec(-tarDistVec.Z, -relTarHorDist, 0).Yaw`, whose `-Y` term is a length and so
> never negative), but raw `256` fails the lint.
>
> **`Missile` reads that one field at three sites and decodes it three different
> ways.** `:384` is `maxLaunchAngle.Angle >> 2` with **no `sbyte` cast** (so a raw
> value above 511 stays large and positive here while it is negative everywhere
> else); `:412-413` passes the cast value as the *upper* bound of a
> `BisectionSearch` whose lower bound is floored at 0, which inverts the interval
> if the cast went negative; `:458-459` is the clamp. The first two live inside
> `DetermineLaunchSpeedAndAngleForIncline`, reachable only with
> `TerrainHeightAware` (`:432`, `:439`). **No shipped weapon sets both**
> `TerrainHeightAware` and `MaximumLaunchAngle` — those two decodings are
> *dormant, not correct*, and the first weapon that sets both inherits two further,
> differently-shaped failures.

**A weapon's `MinRange` standoff can be silently nullified by a DIFFERENT
armament on the same actor.** `AttackBase.GetMinimumRangeVersusTarget`
(`AttackBase.cs:597-621`) returns the **minimum** `MinRange` across every armament
valid against that target — `if (min > range) min = range;` at `:616-617` is the
line that does it — and that is what the approach activities use to decide when to
back off (`FlyAttack.cs:180`, `AttackFollow.cs:323`). Pair a `MinRange: 0` gun with
a `MinRange: 5c0` missile and, against any target *both* can engage, nothing ever
pushes the shooter out of the missile's own minimum; `Armament.CanFire`
(`Armament.cs:333-335`) then refuses every missile shot **silently and
indefinitely**. This is what made the littlebird's Hellfire rack read as "the
missiles never do damage". Narrowing one weapon's `ValidTargets` so the pair no
longer overlaps is enough to restore the standoff (`weapons-missiles.yaml:306-332`).

## 5. Tracking loss that is INTENDED — do not "fix" these

Rare, situational tracking loss is desired realism. The following are correct:

- **SACLOS missiles going ballistic when the shooter dies.** The wire is cut.
- **Operator retargeting** onto a new enemy when the original dies mid-flight,
  with veterancy shortening the reaction delay.
- **Abandoning a target that reaches Critical damage** — no point spending a
  warhead on a wreck. *(This becomes more visible as missiles get more reliable,
  and is the single most likely intended behaviour to be mistaken for a bug.)*
- **Foliage clipping.** Wire-guided missiles fired through canopy may clip a
  tree. Real per-shot rates are 0/15/30%; shots through dense canopy are refused
  outright before the roll.
- **Freezing on last-known position** when the target is lost to fog.

## 6. Damage model

- A **clean hit** on the intended target is decisive against what the weapon is
  meant to kill.
- **Top-attack** delivers against the roof facet and is worth a large multiplier
  against heavy frontal armour (≈7× vs an Abrams). This works today and is
  load-bearing — do not remove it.
- **Near-miss falloff is gradual, by design, and is NOT a defect.** The user
  ruled it acceptable: *"The damage falloff is gradual and from in game tests it
  behaves okay, we might rebalance that later but not now."* Possible future
  rebalance, not a bug.

### How the falloff actually works — verified 2026-08-13

Two warheads with different shapes, and BOTH are graduated:

- **`TargetDamage` (the large point-damage warhead) is NOT all-or-nothing.**
  `TargetDamageWarhead.cs:93` scales it by
  `closestActiveShape.CenterProximityPercent(victim, args.ImpactPosition)`,
  applied as a damage modifier. *(The method was called `PercentFromEdge` until it
  was renamed for exactly the misreading below; older notes and reports use the old
  name.)* `Rectangle.CenterProximityPercent` (`HitShapes/Rectangle.cs:123-126`) is
  `100 * (total - v.HorizontalLength) / total` where `total` is the half-DIAGONAL
  (centre→corner) and the vector passed is the impact **relative to the
  hitshape centre**. So it is **100% at dead centre, falling linearly to 0% at
  the corner distance.** A hit is gated first by `closestDistance > Spread`
  (`:76`), i.e. it must be within `Spread` of the hull edge.
  **The two radii disagree, and the percentage is now floored at zero.** `Spread`
  admits a victim by distance from the hitshape **edge** while
  `CenterProximityPercent` normalises against the **centre-to-corner** distance,
  so on a long thin hull a victim can be admitted at a *negative* percentage —
  and a negative damage number is a **heal**, not a rounding artefact
  (`Health.InflictDamage` clamps into `[0, MaxHP]`, `Health.cs:189`).
  `TargetDamageWarhead.ProximityDamagePercent` (`:31`) floors it since
  2026-08-27. See [§10](#10-a-warhead-delivered-by-explodes-is-a-different-weapon-three-defaults-change-meaning)
  for the delivery path on which `args.ImpactPosition` itself was wrong.
- **`SpreadDamage` (the splash warhead) is piecewise-LINEAR, not stepped.**
  `Falloff = { 100, 37, 14, 5, 0 }` is tabulated at ranges `i * Spread`
  (`SpreadDamageWarhead.cs:28`, `:52`) and `GetDamageFalloff` **interpolates
  between adjacent entries** with `int2.Lerp` (`:141`). So the table gives the
  knee points of a continuous ramp, not five plateaus. `Spread` is the distance
  **between** knees (default `43`, `:25`), so total reach is `4 × Spread`,
  measured from the hitshape EDGE (`DamageCalculationType` defaults to `HitShape`,
  `:34`).
- **Against aircraft, `TargetDamage` almost never applies at all.** Its `Spread`
  defaults to `WDist(1)` (`TargetDamageWarhead.cs:23`) and every helicopter uses
  `HitShape: Type: Circle, Radius: 32` (`aircraft.yaml:89-91`). With any realistic
  scatter the impact lands outside that 1-wdist window, the big warhead is skipped
  at `:76`, and the weapon falls through to whatever `SpreadDamage` it also
  carries — at whatever `Penetration` that fallback happens to declare. **A
  `TargetDamage`-only weapon has essentially no effect on an aircraft, whatever its
  damage number says.** This is why `Hellfire` carries a deliberate `Penetration:
  20` on its spread warhead (`weapons-missiles.yaml:276-292`) rather than relying on
  the 10000-damage point warhead; the symptom without it was "the missile silently
  vanished".

Because the point-damage ramp is scaled against the corner distance, the
percentage is already low at the hull boundary — so the transition from just
inside the hull to just outside is close to continuous, not a step.

> **`missile-detonation-warheads.md` §5's ~3300× hit-vs-near-miss "cliff" is an
> ARTEFACT** of assuming `TargetDamage` is binary. The adversarial review
> re-derived the figure and called it exact, so it is wrong too. Do not build
> reasoning on the audit's warhead arithmetic.
>
> Separately and still open: scaling against the corner distance means a hit
> dead-on the nose of a long vehicle is treated almost as a near-miss (~10% of a
> centre hit) purely from geometry, before any armour-facing modifier. Whether
> that is intended has not been decided.

## 7. Reading `Missile.cs` — traps that have each cost a session

Four hand-derived analyses of this file reached wrong conclusions before it was
instrumented. These are the specific places they went wrong. **Verified against
current `main` on the dates given; re-derive line numbers before quoting them.**

### Units

- **`HorizontalRateOfTurn` / `VerticalRateOfTurn` are raw `WAngle`s, not
  facings.** `HorizontalRateOfTurn = new(20)` (`Missile.cs:99`) is **20 raw angle
  units**, and every consumer reads `.Facing` = `Angle / 4` (`WAngle.cs:67`). So
  `HorizontalRateOfTurn: 20` means **5 facings/tick = 7.03°/tick**, not 28°/tick.
  This unit error has been made three separate times in the missile programme,
  inflating the predicted turn rate by 4× each time. One facing is 360/256 =
  1.40625°.
- **The terminal turn boost is capped by the boost factor, not by the literal.**
  In `Hitting` state inside `3 * loopRadius`, `boost = min(3 * loopRadius /
  closeness, 3)` and `hRot = min(hRot * boost, 20)` (`Missile.cs:951-957`). With
  ATGM's `hRot = 5` the `boost ≤ 3` term binds first, so the real ceiling is
  **15 facings = 21.1°/tick** — the literal `20` in the clamp is never reached, and
  the comment above it naming "20 facings/tick" describes the literal, not the
  binding constraint. Confirmed against a 1071-flight trace corpus: the largest
  single-tick heading change anywhere is exactly 21.1°, repeatedly, never exceeded.
- **`Mobile.Speed` in YAML is not wdist/tick at the point of use.** Any lead or
  correction-budget arithmetic of the form `2 * Speed * D / missileSpeed` that
  reads the YAML number overstates the target's velocity. `Mobile.MovementSpeedForCell`
  (`Mobile.cs:826-831`) runs `Info.Speed` through `Util.ApplyPercentageModifiers`
  with the **locomotor's per-terrain percentage** appended, so a `humvee`
  (`Speed: 150`, `Locomotor: lightwheeled`, `vehicles-america.yaml:68-70`) on
  `Clear` terrain moves `150 × 70% = 105` (`world.yaml:94-103`) — matching the
  105 wdist/tick maximum measured over 2486 traced per-tick deltas. Second-order
  and worth knowing before designing a scenario around a velocity change: the
  *instantaneous* reversal those formulas assume is not available either. A traced
  `Stop`/reverse order collapses a Humvee from 103 to 63 wdist/tick over **eight**
  ticks; at missile `Speed: 300` that is 2400 wdist of missile travel, longer than
  the whole terminal phase the swing is supposed to act on.

### Code that looks live and is not

- **`LocalYaw` does nothing to a missile.** `Missile` recomputes launch facing from
  source→target (`Missile.cs:292-296`) and consults `args.Facing` — the only
  consumer of `Barrel.Yaw`, itself the only consumer of `LocalYaw`
  (`Armament.cs:207`, `:709`) — *only* when that vector is zero-length. An asymmetric
  or "corrected" `LocalYaw` on a missile rack is decorative; it is never the
  explanation for a missile going somewhere unexpected.
- **The whole "I have overshot, loop back" guidance path is dead.**
  `HomingInnerTick` takes a `targetPassedBy` parameter (`Missile.cs:660`) that its
  **only** call site passes as a hardcoded `false` (`:908`). Dead as a result: the
  `|| targetPassedBy` disjunct at `:688`, the vertical-facing clamp at `:704-705`,
  and the entire `else` arm at `:796-805`. Anyone reading `HomingInnerTick` to
  reason about overshoot behaviour is reading code that never runs — what actually
  handles an overshoot today is the WW3MOD-added `flyStraight` latch (`:883-884`),
  which does the opposite and commits to a straight line. Wiring `targetPassedBy`
  up would light three untested branches in one commit.

### Live traps

- **The active-protection `Explode()` does not `return`, so a missile can detonate
  TWICE in one tick.** `HomingTick` calls `Explode(world)` at `Missile.cs:928` and
  then falls through: it still computes turn rates, still returns a move vector,
  and `Tick` still moves the missile and still evaluates `shouldExplode`, reaching
  `Explode` again at `:1220`. Both calls run `args.Weapon.Impact(...)`, so the
  warhead lands twice at two different positions. **Currently unreachable in
  shipped content** — the mod's only `ActiveProtection` reference is commented out
  (`vehicles-america.yaml:499`) — but it is a live trap for whoever enables APS.
- **A latched missile cannot airburst.** Both `AirburstAltitude` sites are gated on
  `!flyStraight` (`Missile.cs:1171`, `:1245`). `ATGM` is the only `Missile`-projectile
  weapon in the mod that sets a non-zero `AirburstAltitude` (`32`,
  `weapons-missiles.yaml:17`; the other two `AirburstAltitude` declarations are on a
  `LaserZap` and a `Bullet`). So once a Javelin declares a miss it also loses its
  proximity airburst, and flies on to a ground hit or fuel-out — which satisfies
  I2b but not §3's top-attack rule of self-destructing at closest approach.
- **A missile removed inside its arming window is indistinguishable from a dud.**
  See I2 — `Explode` queues `w.Remove(this)` (`Missile.cs:1385`) *before* the
  `ticks <= info.Arm` early return (`:1397-1404`), so no warhead, no effect, no
  sound. Any test asserting on "the missile is gone" cannot tell this apart from a
  real hit.
- **Per-tick point sampling is not a valid measure of closest approach, and
  neither is the engine's own `minDistanceToTarget`.** The standing PITFALL at
  `Missile.cs:1179-1187` records that a missile with `Speed > CloseEnough` can
  straddle the proximity sphere between two ticks; the same sampling error corrupts
  any *measurement* of how near a missile got, because the tick before and the tick
  after can both read as wide misses through the middle of the target. Compute
  closest approach on the swept segment instead (`ClosestApproachThisTick`,
  `:1318-1334`).

*(Superseded, so you do not re-derive it: the miss detector used to be
horizontal-only. It is not — `minDistanceToTarget` has been fed the 3D physical
separation `(targetPosition - pos).Length` since `1ec6f17c`, `Missile.cs:878`.)*

## 8. Launch timing: the report fires when the missile SPAWNS, not when it LAUNCHES

`Armament.FireBarrel` adds the projectile, plays the weapon's `Report`, and calls
`INotifyAttack.Attacking` — which is what spawns a `MissileSpawnerMaster`'s missile actor
— as **consecutive statements inside one delayed action** (`Traits/Armament.cs:618-628`).
So a weapon `Report` on a missile-spawner launcher is simultaneous with the missile
*appearing*, never with it *igniting*.

For a `BallisticMissile` with `LaunchRiseTicks > 0` the missile then sits on the rail
erecting, and `BallisticMissileFly` does not reach Phase 2 / `Ignite()` until
`LaunchRiseTicks + PostErectionWaitTicks` ticks later. That quantity is exposed as the
pure `BallisticMissileInfo.PreLaunchTicks` (`Traits/BallisticMissile.cs:85`) and pinned
without a `World` in `engine/OpenRA.Test/MissileLaunchTimingTest.cs`. **The report is early
by exactly that figure.**

**The Iskander is the only affected actor** — the sole `LaunchRiseTicks` user in the mod
(`vehicles-russia.yaml:1076`, `:1079`), at 60 + 20 = **80 ticks = 4.8 s** at `Timestep: 60` (`mod.yaml:382`)
(16.67 tps, *not* the 25 tps several of these YAML comments were written against). Long
enough that the sound reads as belonging to the tilt animation, which is exactly how it was
reported. HIMARS shares the weapon by inheritance but `HIMARSMissile` sets no
`LaunchRiseTicks`, so it ignites on its first tick in the world and a weapon `Report` is
correct for it to within one tick — `HIMARSTargeter` therefore declares its own `Report`
rather than inheriting one.

Use `BallisticMissileInfo.IgnitionSound` (`:77`), played once from `Ignite()` (`:186-187`).
**It needs its own `bool ignited` latch (`:167`) and the pre-existing condition-token guard
could not be reused**: when `IgnitionCondition` is null the token stays invalid forever, and
`Ignite()` is called on *every* arc-flight tick, so the sound would replay for the whole
flight. Guarded going forward by the `CheckMissileLaunchReport` lint rule
(`engine/OpenRA.Mods.Common/Lint/CheckMissileLaunchReport.cs`), which walks every
`MissileSpawnerMaster`, computes `PreLaunchTicks` over its slaves from the real rules tree,
and errors if an armament it drives fires a weapon carrying a `Report` — stating the actual
lateness rather than a hard-coded number.

**NO AUTOTEST CAN VERIFY THIS, and that is why it needed a lint rule.** `run-test.sh:157`
defaults `AUDIO_MUTE=1` (`--audio` opts out), and more fundamentally **there is no
sound-logging or trace surface anywhere in the engine** — `Game.Sound.Play` records nothing.
No scenario can ever produce a verdict on *which* sound played *when*. Audio-timing work has
to be pinned on tick arithmetic and data wiring, with a human listening test as the only
end-to-end confirmation. Anyone reaching for `run-test.sh` to verify a sound bug should stop
here.

## 9. Sizing a burst interval: use the missile's maximum LIFETIME, not its flight time

When a launcher fires wasteful pairs at one target, the fix is to space the shots past the
first missile's resolution. **Flight time to a *target* is the obvious quantity and the wrong
one** — it depends on range, on target motion, and on how much the missile weaves, so any
number derived from it is a guess with an unbounded tail.

**There is a bounded quantity next to it.** `Missile.cs:1159` accumulates
`distanceCovered += speed` every tick and `:1164` detonates the tick that total exceeds
`RangeLimit` (`ExplodeWhenEmpty` defaults to **true**, `:120`; `:305` falls back to the
weapon's `Range` when `RangeLimit` is unset). Speed is fully determined: `ChangeSpeed`
(`:536-538`) adds `Acceleration` per tick clamped to `Speed`, starting from
`MaximumLaunchSpeed` (`:308`, `:421`). `HomingTick` runs before the accumulation, so tick *n*
adds the post-acceleration speed. Maximum lifetime is therefore arithmetic:

```
speed(n)  = min(Speed, MaximumLaunchSpeed + Acceleration*n)
lifetime  = smallest n where sum(speed(1..n)) > RangeLimit
```

For `Stinger` (launch 50, accel 35, cap 600, `RangeLimit: 30c0` = 30720): 4950 by tick 15,
5550 by tick 16, +600/tick after; `5550 + 600*42 = 30750 > 30720` at **tick 58** (tick 57 is
30150, under). Every Stinger is resolved — impact, ground or fuel-out — by tick 58 *whatever
path it flew*. Computed the same way: 9M311 58, AirToAirMissile 48, SurfaceToAirMissile 55,
Ataka.AA 61, Hellfire 61, MANPAD 63, TimerWolf_Missiles 45. At 16.67 tps, 58 ticks is 3.48 s.

**Caveat that bounds the claim:** the ceiling is on *distance*, not ticks, so a missile that
decelerates outlives it. `HomingInnerTick` calls `ChangeSpeed(-1)` on its `slowDown` branch
(`:653-654`) once the target is inside `3 * loopRadius`. That happens in the `Hitting` state,
metres from a target it is about to reach — but a weapon that orbits rather than hits is
outside this arithmetic.

### Resolve `Burst` FIRST; it decides which field you are even allowed to read

`WeaponInfo.cs:113` defaults `Burst` to **1**. `Armament.UpdateBurst` (`Armament.cs:715-738`,
whole body gated on `Weapon.BurstWait > 0` at `:717`) delegates the counter step to
`BurstSequence.Advance` (`:74-81`), which runs `--burst < 1` after every shot — always true at
`Burst: 1` — so it always returns the completed step carrying `burstWait`, and the
`InterShotDelay` branch is **unreachable**. **At `Burst: 1`, `BurstWait` is the inter-shot
interval and `BurstDelays` is dead code.**

> *Line citations refreshed 2026-08-27.* The arithmetic is unchanged, but the counter step moved
> out of `UpdateBurst` into the pure `BurstSequence` helper so it could be unit-tested without a
> `World`; older notes cite `Armament.cs:651-679` / `:653` / `:669-672` / `:655`, all superseded. Two AA
double-launch bugs found a night apart came through different fields for exactly this reason:
the Stryker SHORAD had `Burst: 2` and was spaced by `BurstDelays` (intra-burst), the Tunguska
has `Burst: 1` and was spaced by `BurstWait` (inter-burst). Same cause, different knob.

### `BurstDelays` vs `BurstWait`: the constraint that used to exist, and no longer does

**Current rule (since 2026-08-27): there is none. `BurstDelays` may exceed `BurstWait` freely.**
The stale-burst reset is keyed off a deadline rather than off the raw gap since the last shot —
`BurstSequence.StaleTick(worldTick, interShotDelay, burstWait)` returns
`worldTick + interShotDelay + burstWait` (`Armament.cs:61-64`), so the clock starts when the next
shot **fails to arrive on schedule** and then runs for one full `BurstWait`. An inter-shot delay of
any length is now inside the deadline by construction. `IsStale` is consulted once per fire
attempt (`:435`) and resets to a full burst (`:436`).

**What that replaced, and why it is worth knowing.** The old check compared
`WorldTick - lastFiredTick > Weapon.BurstWait` — the raw gap — which a *healthy* burst trips on any
weapon whose inter-shot delay is not shorter than its between-bursts wait. `Mandible` (delays 14,
wait 10) and `MandibleHeavy` (20/15) are both that shape, so **those two weapons could never
complete a burst at all**: the reset fired between their own two shots, forever. `Stinger.quad`
(`BurstDelays: 58` against `BurstWait: 60`, `weapons-missiles.yaml`) escaped only by its margin,
and a dated comment on that weapon shows someone had noticed the hazard for it specifically without
generalising it. **Two of the mod's weapons were silently broken by a rule the third had a comment
about.**

> *Superseded 2026-08-27.* This subsection previously stated the trip condition as
> `BurstDelays > BurstWait` with safety at `<=`, correcting an earlier `+1` off-by-one reading. That
> analysis was right about the mechanism as it then stood and is now historical: the mechanism it
> analysed no longer exists. Two consequences for anyone reading older material — the YAML comments
> on `Stinger.quad` and `9M311` (`weapons-missiles.yaml`) still describe the raw-gap check and cite
> `Armament.cs:367`, a line that has moved and a rule that has gone; and **`Stinger.quad`'s
> `BurstDelays` is 58 against a `BurstWait` of 60, not 58 against 58** — the 58-tick *wait* belongs
> to `9M311`, the neighbouring weapon, and the two have been conflated more than once.

**The raw-gap shape still exists in one place.** `Armament.cs:478` computes
`idleLongerThanBurstWait = lastFiredTick - previousLastFiredTick > Weapon.BurstWait` to detect the
first shot of a fresh burst for `LockAimPerBurst`. It is the same comparison and carries the same
latent defect — but it is **currently inert**, because all five weapons setting `LockAimPerBurst`
have `BurstDelays` far below `BurstWait` (`GradRockets` 4/100, `TosRockets` and `M270Rockets`
10/200, `Flamespray` and `Flamespray.heavy` 1/30). Authoring a locked-aim weapon with a long
inter-shot delay would wake it up.

### Widening an interval is only correct if the FIRST missile is lethal

Half the mod's AA missiles are exempt from the double-launch fix, and the reason is an unset
`Penetration`. `DamageWarhead.cs:24` defaults `Penetration` to **1**, and `:219-233` scales
`damage = damage * penetration / thickness` whenever `penetration < Armor.Thickness` —
**skipped entirely when `Thickness` is 0**. Against a Heavy airframe (800 HP, Thickness 20):

| weapon | Damage | Pen | effective | verdict |
|---|---|---|---|---|
| `9M311` / `Stinger` | 5000 | **20** | full 5000 (6× margin) | one-shot — interval must exceed lifetime |
| `Ataka.AA` | 2000 | **20** | full 2000 | one-shot on a direct hit only |
| `AirToAirMissile` | 1000+rand | *unset → 1* | 50–99 | needs 8–16 hits |
| `SurfaceToAirMissile` | 2000+rand | *unset → 1* | 100–149 | needs 6–8 hits |

`AirToAirMissile` and `SurfaceToAirMissile` were deliberately left alone: **their short
intervals are load-bearing, not a bug.** That two AA missiles silently do ~1/20th of their
printed damage is a separate balance finding, logged rather than fixed — raising their
`Penetration` is a large unmeasured combat change. Related authoring omission from the same
root: the MiG-29 has `Armor: Type: Medium` with **no `Thickness`**
(`aircraft-russia.yaml:600-601`) while the F-16 it duels has `Thickness: 10`
(`aircraft-america.yaml:578-580`) — Thickness 0 skips the scaling block outright, so the MiG
takes 10–20× more damage from every Pen-1 weapon than its counterpart.

## 9b. Detonation altitude: two engine facts that make an airburst behave nothing like you would guess

*(Promoted 2026-09-05 from DISCOVERIES; every citation re-read at `main @ 95bdffb2`, and the arithmetic is
pinned by `engine/OpenRA.Test/OpenRA.Mods.Common/MissileStrikeArrivalTest.cs`. Neither fact is stated in any
`[Desc]`.)*

### 9b.1 Whether altitude costs a warhead damage depends on the VICTIM'S HITSHAPE TYPE

`SpreadDamageWarhead` and `ShockwaveDamageWarhead` both take their falloff distance from
`HitShape.DistanceFromEdge`, which dispatches to the shape implementation — and the four disagree about
whether the vertical leg exists at all:

| Shape | Vertical leg counted? | Code |
|---|---|---|
| `Circle` | **YES** — `v.Length`, 3-D | `HitShapes/Circle.cs:46-49` |
| `Polygon` | **YES** — `ISqrt(min2 + z*z)` | `HitShapes/Polygon.cs:87-103` |
| `Rectangle` | **NO** — the result vector is built with a hardcoded `0` for Z and returned as `HorizontalLength` | `HitShapes/Rectangle.cs:109-116` |
| `Capsule` | **NO** — projects to `int2(v.X, v.Y)` immediately | `HitShapes/Capsule.cs:67-85` |

In this mod vehicles and buildings are `Rectangle` and infantry are `Circle`
(`rules/ingame/infantry.yaml:148-151`, `HitShape@Standing: Type Circle, Radius 30`). So **an airburst is
completely free against vehicles and buildings and fully discounted against infantry** — exactly backwards
from the physics an airburst is imitating, where the anti-personnel effect is the whole point.

Measured on the shipped `Atomic` warhead at `DetonationAltitude: 6c256` (6400), against a victim standing
directly under the burst:

- **vehicles and buildings:** falloff distance **0**, identical to a ground burst — no change of any kind.
- **infantry:** falloff distance **6370** (6400 − the 30 radius), and then
  `Warhead@ThermalVaporize` (`Spread 3c0`, `Falloff 100,100,100,50`) still delivers **96%**, because that
  table is flat at 100 all the way out to 6144; `Warhead@ThermalRadiation` (`Spread 1c0`, 15 steps) drops to
  **4%**. That one warhead is the entire cost of bursting at 6c256.
- **fire, EMP, suppression and tree-fire: no change at all.** `GrantExternalConditionWarhead` does
  `FindActorsInCircle(target, Range)` with no falloff and no shape query
  (`Warheads/GrantExternalConditionWarhead.cs:60-61`), and that search is horizontal.

### 9b.2 An airburst on the wrong weapon detonates silently and invisibly, with no error and no lint

`Warhead.ValidTargets` defaults to `Ground, Water` **per warhead** — it is not inherited from the weapon's
own `ValidTargets` — and `Warhead.AirThreshold` defaults to `128`, one eighth of a cell
(`Warheads/Warhead.cs:30`, `:45`). Above that threshold `CreateEffectWarhead.IsValidAgainstTerrain` stops
asking the terrain what it is and substitutes the `Air` target type (`Warheads/CreateEffectWarhead.cs:166`);
a warhead not listing `Air` returns from `DoImpact` before spawning anything.

`^HugeExplosionEffects` (`rules/weapons/weapons-effects.yaml:596`) — inherited by `IskanderExplosion` (the
Kinzhal) and `MOPPenetration` (the GBU-57) — writes `ValidTargets: Ground, Ship, Trees, Mine` on every
`CreateEffect` row and never `Air`. **So giving either of those powers a `DetonationAltitude` above 128
would delete the explosion sprite, the impact sound and the crater while the `SpreadDamage` rows — which
test the VICTIM, not the terrain — went on working: a strike that damages things with no visible
explosion.** `Atomic` is the one warhead in the mod written for an airburst, and it says so twice: an
explicit `ValidTargets: Ground, Water, Air` on `Warhead@Fireball`
(`rules/weapons/weapons-superweapons.yaml:65`) and `AirThreshold: 10c0` on all ~30 of its damage, fire, EMP,
suppression and smudge rows. **10c0 = 10240 is therefore a hard ceiling on the nuke's burst height** — at
10241 it would still fly, still be aimed, still be announced, and do nothing to the ground.

### 9b.3 Not a compensation: `Warhead@Fireball`'s `Offset: 0,-1900,0`

Easy to misread as airburst compensation, and it is not. Screen y is `TileSize.Height * (Y - Z) / TileScale`
(`Graphics/WorldRenderer.cs:749`), so a negative world-Y offset and a positive world-Z offset are the *same*
vertical screen displacement. That offset — 5700 after the warhead's own `ScalePercent: 300`
(`weapons-superweapons.yaml:58-59`) — lifts the scaled mushroom-cloud sprite off its anchor, and it applied
identically to the ground burst. Which is why the ground-burst nuke looked *low* rather than looking
*broken*.

## 10. A warhead delivered by `Explodes` is a different weapon: three defaults change meaning

A ballistic missile does not detonate as a projectile. `BallisticMissileFly` queues
`self.Kill(self)` (`BallisticMissileFly.cs:209`) and the actor's `Explodes` trait calls
`weapon.Impact(Target.FromPos(self.CenterPosition + Offset), source)` (`Explodes.cs:133`, which
comments *"Cannot use Target.FromActor"* because the actor is already dead). That reaches
`WeaponInfo.Impact(in Target, Actor firedBy)` — the **projectile-less** overload — and three
warhead fields that behave normally everywhere else change meaning on it. All three bit the
Iskander and HIMARS simultaneously, reported as *"the Iskander hit a tank directly and it didn't
get destroyed"*.

**0. The `airborne` condition is already REVOKED by the time the kill lands, so an `Explodes` gated on it
never fires on a successful strike.** *(Promoted 2026-09-04 from DISCOVERIES; **derived** from the code path,
not observed in a run.)* `BallisticMissileFly`'s final act is `sbm.SetPosition(self, targetPos)` and *then*
the queued `self.Kill(self)`. For a ground strike `targetPos` is a cell centre at terrain height, so
`BallisticMissile.SetPosition` computes an altitude below `MinAirborneAltitude` and calls
`OnAirborneAltitudeLeft()` — **on an earlier tick than the queued kill.** So on `IskanderMissile` and
`HIMARSMissile` the warhead that actually detonates on arrival is the `SpawnedExplodes` gated
`RequiresCondition: !airborne`; the `airborne` one is the shot-down-in-flight branch. Copying that pair onto
a masterless missile silently produces a missile that lands and does nothing — and `SpawnedExplodes` cannot
be used without a master at all (`Traits/SpawnedExplodes.cs:61` calls `self.Trait<BaseSpawnerSlave>().Master`
unconditionally), so the correct shape there is a single **ungated** plain `Explodes`. The check that would
confirm this from a run: fire the Kinzhal with its `Explodes` gated `airborne` and observe the target
survive.

**1. `ImpactPosition` is not set for you.** It is assigned by seven projectile types (`Bullet`,
`Missile`, `GravityBomb`, `LaserZap`, `Railgun`, `AreaBeam`, `InstantHit`) plus the
`WarheadArgs(ProjectileArgs)` constructor (`WeaponInfo.cs:51`) — and, before 2026-08-27, by nothing
on the projectile-less path, where it stayed `WPos.Zero`, **the map origin**. `TargetDamageWarhead`
scales by `CenterProximityPercent(victim, args.ImpactPosition)`; measured from the map corner
against a T-90's 1030-unit half-diagonal that is **−2782%**, and negative damage **heals**
(`Health.cs:189`). The direct-hit warhead was a repair beam. `SpreadDamageWarhead` reads the same
field for impact *orientation*, so every `Explodes` splash was computing hit direction from the
origin too. Now set at `WeaponInfo.cs:307`.

**2. `DamageAtMaxRange` is unusable — `100` is the only safe value.** `RangeDamageFactor` divides by
`args.Weapon.Range` (`DamageWarhead.cs:138`), which is **0** for a weapon never fired from an
armament, making `ofMax` non-finite and the cast to int not a percentage. It is meaningless in
principle here as well: `args.Source` is the missile's own position at detonation, so the "range" is
always zero however far the launcher stood. `TargetDamageWarhead` only consults the field when it is
not 100 — which is why Iskander's `100` was inert while HIMARS's `80` was live and wrong.

**3. `Penetration` still defaults to 1**, and on a large anti-armour warhead that is the difference
between a kill and a scratch — see [§9](#9-sizing-a-burst-interval-use-the-missiles-maximum-lifetime-not-its-flight-time)
and `conventions.md` instance 7. Both ballistic missiles omitted it.

**Authoring rule.** A warhead reached through `Explodes` / `SpawnedExplodes` has no projectile
behind it, so **every `WarheadArgs` field a projectile would have populated is at its default**.
Before tuning such a weapon, list the fields its warheads read and check each one is actually
supplied on this path. Do not assume a field is set because it is set for every other weapon you
have looked at.

> **Not a general convention:** the sibling `Warhead@Spread: SpreadDamage` does **not** uniformly
> omit `Penetration`. `ATGM` and `RPG` leave it unset, but `Ataka` and `Hellfire` set 20, and both
> ballistic missiles set 2500/1800. Treat the two warheads as independently tuned rather than
> inferring one from the other.
