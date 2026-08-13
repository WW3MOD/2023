# Missile behaviour spec — how missiles are intended to work

_spec · status: draft · authored 2026-08-13T08:56:33.459Z_

# Missile behaviour spec (DRAFT — needs the user's sign-off)

Destination once agreed: `DOCS/reference/missiles.md`.

This exists because the missile system has repeatedly been *misunderstood* rather
than merely broken. The Javelin's top-attack cruise altitude was nearly "fixed"
as a typo; three separate analyses of the guidance code reached confident wrong
conclusions. This document states what missiles are SUPPOSED to do, so that
"is this a bug?" has an answer that does not require re-deriving the engine.

Status of every claim here: **intent**, not current behaviour. Where the shipped
code violates it, that is a defect. Current behaviour is documented separately in
`WORKSPACE/audit/*.md` (audited against `main @ dc899995`).

## 1. Invariants — these bind every missile, no exceptions

**I1 — Hit probability is distance-invariant.** *(User ruling, verbatim: "Should
have the same hit chance regardless of distance. If we want to limit firing to a
min distance, we can set that on the weapon, but as long as the weapon can fire
the missile should be able to hit.")*

A weapon's hit chance must not vary systematically with engagement range across
its permitted envelope. Range limiting is the job of the weapon's declared
`MinRange`/`Range` — never an emergent consequence of projectile physics.

Consequences that follow and are binding:
- Any mechanism where geometry, arming delay, homing-activation delay or turn
  radius makes near or far shots systematically worse is a **defect, even where
  it is physically realistic.**
- "Make the launcher refuse the shot" is **not** an acceptable fix for a missile
  that cannot hit. If the weapon is allowed to fire, the missile must be able to
  hit. Narrowing `MinRange` is acceptable; leaving a permitted-but-hopeless band
  is not.

**I2 — A missile always resolves visibly.** Every missile ends in a detonation
the player can see and hear, at a place that explains what happened. A missile
that is removed silently, or that detonates with no sprite and no sound, is a
defect regardless of the damage it did or did not do.

**I3 — Randomness is legitimate; systematic failure is not.** Missiles are
*meant* to miss sometimes, unpredictably. What is not acceptable is a
deterministic mechanism that makes a whole class of shot fail. When diagnosing,
the question is never "did it miss?" but "does this miss have a *reason* that
scales with something other than luck?"

## 2. Weapon classes

Guidance behaviour is per-class. These are the classes; every missile weapon
belongs to exactly one.

| Class | Examples | Guidance | Shooter dies |
|---|---|---|---|
| **SACLOS wire-guided** | `Ataka`, `WGM`, `WGM.bradley` | Operator keeps the crosshair on target; shooter is committed while guiding | Missile goes ballistic |
| **Fire-and-forget** | `Hellfire`, `Hellfire.strykershorad` | Autonomous seeker after launch; shooter free to manoeuvre | Missile continues to target |
| **Top-attack** | `ATGM` (Javelin) | Climbs to a cruise altitude, dives onto the target's roof | Missile continues |
| **Anti-air** | `MANPAD`, `Stinger`, `Stinger.quad`, `9M311`, `SurfaceToAirMissile` | Proportional pursuit of a fast, manoeuvring target | Missile continues |
| **Cruise / strategic** | (as fielded) | Pre-programmed to a position | Unaffected |

## 3. The miss-detonation rule — per class

*(The user asked the agent to propose this rather than picking a single global
rule. This is the proposal; it is the main thing needing sign-off.)*

Baseline from the user: a missile that misses should generally still explode,
EXCEPT anti-air, which may fly on until fuel-out.

| Class | On a confirmed miss | Rationale |
|---|---|---|
| **SACLOS wire-guided** | **Self-destruct at closest approach.** | The operator sees it miss and cuts it. Gives a visible boom where the miss happened and lets splash still matter. |
| **Fire-and-forget** | **Self-destruct at closest approach.** | Same player legibility; a modern seeker knows it has missed. |
| **Top-attack** | **Self-destruct at closest approach.** | It is diving; continuing means hitting the ground a cell away, which reads as a dud. |
| **Anti-air** | **Fly on to fuel-out, then detonate.** *(User's explicit exemption.)* | A missile chasing an aircraft may reacquire; an airburst at fuel-out is realistic and legible. |
| **Cruise / strategic** | Detonate at its programmed position. | It has no target to miss. |

"Confirmed miss" means the missile is past its closest approach and increasing
range **in physical distance** — not in any lead-inflated or predicted measure.
*(The shipped code currently tests a lead-inflated distance against a physical
constant; see the audit review. That is the defect this wording exists to
prevent recurring.)*

## 4. Arming and minimum range

- A missile arms fast enough to be lethal at the weapon's declared `MinRange`.
  If a weapon may fire at 2 cells, its warhead arms before 2 cells. **A weapon
  whose arming distance exceeds its own `MinRange` is misconfigured** — fix the
  arming, or raise `MinRange`, never leave the gap.
- Guidance is active from launch, or from a delay short enough to be irrelevant
  at `MinRange`.
- Launch pitch must permit engaging targets in the weapon's actual envelope. An
  anti-air weapon must be able to launch upward. *(Currently violated — see the
  `MaximumLaunchAngle` wrap.)*

## 5. Tracking loss that is INTENDED — do not "fix" these

The user has been explicit that rare, situational tracking loss is desirable
realism. The following are correct and must be preserved:

- **SACLOS missiles going ballistic when the shooter dies.** The wire is cut.
- **Operator retargeting** onto a new enemy when the original dies mid-flight,
  with veterancy shortening the reaction delay.
- **Abandoning a target that reaches Critical damage** — the operator does not
  spend a warhead on a wreck. *(Note: this will become more visible once
  missiles reliably hit. Flagged for the user as the most likely thing to be
  mistaken for a bug.)*
- **Foliage clipping.** A wire-guided missile fired through canopy may clip a
  tree. Real per-shot rates are 0/15/30% and shots through dense canopy are
  refused outright.
- **Freezing on last-known position when the target is lost to fog.**

## 6. Damage model intent

- A **clean hit** on the intended target is decisive against what the weapon is
  meant to kill.
- A **near miss** should do meaningfully reduced but non-trivial damage. The
  hit/near-miss ratio should be a slope, not a cliff. *(Currently a cliff: 10057
  vs 3 against an Abrams for the Javelin — a 3300× step at half a cell. Whether
  to flatten it is a balance decision for after the guidance defects are fixed,
  because it is currently amplifying them.)*
- **Top-attack** delivers against the target's roof facet and is worth a large
  multiplier against heavy frontal armour. This works today (7× vs Abrams) and
  is load-bearing — do not remove it.

## 7. Open for the user

1. **Sign-off on §3**, the per-class miss rule — the one thing explicitly
   delegated to the agent to propose.
2. **§6's near-miss slope** — is a 3300× cliff intended severity, or should a
   near miss hurt? Deferred until the guidance fixes land and can be measured.
3. Whether **`Hellfire.strykershorad`**, a ground vehicle that cannot target
   air, should keep inheriting the airborne Hellfire's cruise altitude.
