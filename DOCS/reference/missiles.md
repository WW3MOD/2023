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
> leadTarget + offset`), so it and the miss test are no longer commensurable.
> Nothing bounds `offset` by `CloseEnough`, and `ATGM` rolls `Inaccuracy: 512`
> against the default `CloseEnough: 298`, so a missile can sit physically inside
> the proximity radius without fusing.
>
> **Open, not yet decided:** making the detonation test consistent with the miss
> test on physical separation. That is arguably a proximity-fuse defect in its
> own right — a missile 200 units from a tank that flies past is failing this
> section's closest-approach rule — but it changes when every missile in the game
> detonates and needs its own measurement.

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
> (`CheckAngle`) further caps it at 255. A raw value above 511 silently becomes a
> *downward* angle. `CheckAngle` validates that an angle is well-formed, **never
> that it points the right way** — a green lint is not evidence a launch angle is
> correct. This is how MANPAD/Stinger shipped clamped below the horizon.

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
  `TargetDamageWarhead.cs:67` scales it by
  `closestActiveShape.PercentFromEdge(victim, args.ImpactPosition)`, applied as a
  damage modifier. Despite the name, `Rectangle.PercentFromEdge`
  (`HitShapes/Rectangle.cs:118-122`) is
  `100 * (total - fromEdge.Length) / total` where `total` is the half-DIAGONAL
  (centre→corner) and the vector passed is the impact **relative to the
  hitshape centre**. So it is **100% at dead centre, falling linearly to 0% at
  the corner distance.** A hit is gated first by `closestDistance > Spread`
  (`:64`), i.e. it must be within `Spread` of the hull edge.
- **`SpreadDamage` (the splash warhead) falls off in steps**:
  `Falloff = { 100, 37, 14, 5, 0 }` at ranges `i * Spread`
  (`SpreadDamageWarhead.cs:26-28,53`). Note `Spread` is the distance **between
  steps**, so total reach is `4 × Spread`, measured from the hitshape EDGE
  (`DamageCalculationType` defaults to `HitShape`).

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
