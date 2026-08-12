# Missile system: ground-truth audit, agreed spec, then repair

_plan · status: draft · authored 2026-08-10T12:22:57.066Z_

# Missile system: ground-truth audit, agreed spec, then repair

## Why this plan looks the way it does

Today's attempt at a quick Mi-28 fix produced a 489-line diagnosis with correct
file:line citations and a confidently wrong central conclusion. It was caught
only because the worker was required to record a falsifiable prediction and run
the pre-fix baseline first — the baseline passed where the report predicted
failure, so nothing shipped. Two separate arithmetic errors were also found in
it, and the manager's own follow-up audit wrongly called the Javelin's
top-attack cruise altitude a typo; only the user's question caught that.

Three process rules therefore bind every phase below:

1. **Observation beats integration.** Nobody hand-integrates missile flight
   again. We build a per-tick trace first and read what the missile actually
   did.
2. **A claim about behaviour requires every branch traced**, not the first
   plausible one. The failed diagnosis traced one arm of a two-arm branch and
   missed that the other arm used a strict `>` that silently corrected the
   problem.
3. **No fix without a failing repro first**, and a written prediction of what
   the repro will show, recorded before the fix is written.

## The structural observation that shapes the repair phase

`Missile.cs` is inherited OpenRA code built around **terrain height levels** —
cliffs the missile must climb over. WW3MOD's `mod.yaml` declares `MapGrid` with
no `MaximumTerrainHeight`, so the height layer is **uniformly zero and always
will be**. A large fraction of the guidance hot path — `InclineLookahead`, the
`predClf*` machinery, cliff avoidance, `allowPassBy` — can never do anything
useful in this mod.

It is not merely dead: it still *latches state*. `allowPassBy` is set true on
every single ground shot because `lastHt >= targetPosition.Z` evaluates `0 >= 0`,
and the mod has already absorbed two separate commits patching the fallout
(`000a3795`, `85374503`), each of which fixed one half and left the other. This
is the most likely single source of the "old code" the user is describing, and
excising it is the strongest candidate for a general improvement — but only
after the audit proves what actually depends on it.

## Phase 0 — Instrumentation (1 worker, not gated, starts immediately)

Build a **missile trace**: a per-tick diagnostic emitted under a debug flag,
recording for each missile — tick, position, target position, state
(Freefall/Homing/Hitting), `vFacing`/`hFacing` vs desired, `allowPassBy`,
`flyStraight`, `lockOn`, `relTarDist`/`relTarHorDist`, `loopRadius`,
`distanceCovered` vs `rangeLimit` — plus one summary line per missile:
launcher, weapon, launch position/altitude, target, and the **exact termination
reason** (which detonation clause fired, or removed-without-exploding).

Also a small autotest helper so scenarios can assert on trace outcomes rather
than only on damage totals.

Rationale: every question in Phase 1 and every scenario in Phase 3 becomes an
observation instead of an argument, and one authorised test run yields answers
to a dozen questions instead of one. This is the highest-leverage item in the
plan and everything downstream is cheaper for it.

## Phase 1 — Ground truth (4 parallel read-only workers, not gated)

Split by axis so no worker has to hold the whole system in context. All cite
`file:line`, all stamp the ref+SHA, all are explicitly required to write "could
not determine statically" rather than compute a number they cannot verify.

- **W1 Flight & guidance** — `Missile.cs` end to end: every state transition,
  every branch of `HomingInnerTick` with the conditions that reach it, launch
  speed/angle determination, the terminal turn-rate boost, `ManualGuidance`,
  operator retargeting. Deliverable: a state machine and a branch-reachability
  table.
- **W2 Detonation & lifetime** — every path by which a missile ends: all
  `shouldExplode` clauses, the arming delay, the segment closest-approach check,
  Freefall, off-map, blocked. Must answer directly: **can a missile be removed
  without ever exploding?** (the user reports this happens.) Extends into
  warhead application: `TargetDamage` vs `SpreadDamage`, Spread radii,
  Penetration vs armour Thickness, the armour facet distribution and how
  `TopAttack` selects it.
- **W3 Fire control & targeting** — everything *before* the projectile exists:
  `Armament`, `AttackBase`/`AttackAircraft`/`AttackFrontal`, Range/MinRange
  gating, `FacingTolerance`, the foliage LOS gate and its miss roll, lead-target
  computation, `AutoTarget` stance. The AA-soldier-at-2-cells failure most
  likely lives here or at the launch/arming boundary.
- **W4 Weapon data sweep** — every weapon on the `Projectile: Missile` path:
  full field table, which units field them, which are reachable in play vs dead
  entries. An earlier sweep exists but its raw table must be re-verified, since
  its conclusions were unreliable.

## Phase 1.5 — Adversarial review of the audit (1 worker)

One reviewer whose only mandate is to attack the four reports: find claims not
supported by the cited code, branches not traced, arithmetic not checked. Given
that this session has already produced two confidently wrong analyses, this is
not optional.

## Phase 2 — The spec, agreed with the user (manager-authored)

Draft `DOCS/reference/missiles.md`: how missiles are **intended** to work.
Per-class guidance profiles (SACLOS wire-guided / fire-and-forget / top-attack /
anti-air / cruise), the arming and minimum-range rule, the miss-and-detonate
rule, which forms of tracking loss are legitimate realism and must be preserved,
and the intended damage model including the hit vs near-miss relationship.

This is the "no more misunderstandings" deliverable — the ATGM top-attack
episode is exactly what it exists to prevent. Presented to the user for
agreement **before any fix is written**, and thereafter the reference that
settles what counts as a bug.

## Phase 3 — Gap list and repro scenarios (not gated)

Cross the audit against the spec into a ranked defect list. For each defect,
author a scenario with ONE measurable bar, per the existing case model. Authored
but **not run**. At minimum:

- Mi-28 at its real 3840 cruise altitude, not the 1280 the current test uses
- AA soldier vs Littlebird at 2, 3 and 4 cells
- a missile that misses — does it detonate, and where?
- the Javelin top-attack dive, and whether the top armour facet takes the hit
- moving targets, and foliage-dense lines vs clear ones

## Phase 4 — One measurement grant

Autotest runs are user-gated and the harness is single-instance, so grants are
the scarce resource. Everything above is deliberately non-gated so that a
**single** goahead runs the whole batch with tracing on, rather than dribbling
out one gated run at a time. Ask once, measure everything.

## Phase 5 — Repair

One defect at a time, each on an isolated worktree, each with a failing repro
and a recorded prediction before the fix, each independently reviewed, merged by
the manager on green. The terrain-height excision from the observation above is
sized here, once the audit has established what depends on it.

## Open decisions with the user

Posted: audit scope; where a missing missile should detonate; what should happen
at 2–4 cells for AA. Deferred to Phase 2, once the audit gives it a factual
basis: how far to go on "the code is old" — targeted repair versus replacing the
guidance core with a modern proportional-navigation model.
