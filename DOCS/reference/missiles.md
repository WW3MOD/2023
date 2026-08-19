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
  `TargetDamageWarhead.cs:83` scales it by
  `closestActiveShape.CenterProximityPercent(victim, args.ImpactPosition)`,
  applied as a damage modifier. *(The method was called `PercentFromEdge` until it
  was renamed for exactly the misreading below; older notes and reports use the old
  name.)* `Rectangle.CenterProximityPercent` (`HitShapes/Rectangle.cs:123-126`) is
  `100 * (total - v.HorizontalLength) / total` where `total` is the half-DIAGONAL
  (centre→corner) and the vector passed is the impact **relative to the
  hitshape centre**. So it is **100% at dead centre, falling linearly to 0% at
  the corner distance.** A hit is gated first by `closestDistance > Spread`
  (`:76`), i.e. it must be within `Spread` of the hull edge.
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
  `HitShape: Type: Circle, Radius: 32` (`aircraft.yaml:65-67`). With any realistic
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
