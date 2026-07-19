# Spread-cone blocker gate (Phase 2 sketch)

**Author:** session 260510 — picked up later.
**Status:** sketch only. Not yet started.

## The pitch

Replace (or supplement) the per-cell density gate with a per-blocker
hit-chance check derived from the projectile's resolved inaccuracy.
The gate denies a shot if predicted P(hit any blocker on line) exceeds
a tunable threshold.

User's framing: "what's the chance of hitting this blocker, given the
projectile's spread at this distance? Sum across all blockers, deny if
above threshold."

## Why

- Naturally accuracy-aware. Veterancy lowers Inaccuracy → more shoot-
  through. Inexperienced units stay denied.
- Distance-aware. Trunk close to muzzle: small lateral spread there →
  easy to thread. Trunk near target: bigger spread → riskier.
- Single user-facing knob (`MaxAcceptablePerBlockerHitChance` per
  weapon) rather than the current pair of `FreeLineDensity` +
  `MissChancePerDensity`.
- Composes cleanly: `P(hit any) = 1 - prod(1 - P_i)`.

## Where it fits cleanly: dumb projectiles

Bullet, InstantHit, Railgun, AreaBeam — anything that has a fire-time
random offset applied to target position. The lateral error at any
intermediate distance d along the firing line is:

    σ(d) = resolvedInaccuracy × (d / d_target)        // for InaccuracyType.Maximum
    σ(d) = resolvedInaccuracy                         // for InaccuracyType.Absolute
    σ(d) = resolvedInaccuracy × (d / 1024)            // for InaccuracyType.PerCellIncrement

`resolvedInaccuracy = baseInaccuracy * Util.ApplyPercentageModifiers(args.InaccuracyModifiers)`
— veterancy etc. already feeds in via `InaccuracyModifiers` from
`Armament.FireBarrel`.

For a blocker at distance d_b with hitshape half-width w:

    P(hit) = ProbInRange(-w, +w | mean = 0, std ≈ σ(d_b) / 2)

We can approximate the PDF as uniform over `[-σ, +σ]` for cheapness
(the engine already uses `WVec.FromPDF` which is pyramid distribution,
so a triangular approximation is fine). Cheap closed-form: `P(hit) ≈
min(1, w / σ(d_b))`.

`w` derives from the actor's first `HitShape` perpendicular extent
(or a per-actor `BlockerHitWidth` override field).

## Where it doesn't fit: guided projectiles

WGM, Hellfire, ATGM — guided missiles aim dead-center via
`LockOnInaccuracy: 0`, so `σ = 0`. The cone model says "always threads
any tree-gap, never clips." That's the wrong answer for our gameplay
rule. Two options:

1. **Keep the density gate for guided.** Spread gate is opt-in
   per-weapon; guided missiles don't opt in.
2. **Add a synthetic minimum spread for guided** — `BasePathDeviation:
   N` modeling "the missile drifts ~N wdist in flight regardless." Adds
   a tunable but lets guided participate in the same model.

Recommendation: (1). Don't break guided to unify a model.

## Implementation steps

1. **Add `WeaponInfo` field:**
   ```yaml
   MaxAcceptablePerBlockerHitChance: 5   # percent. 0 = gate disabled.
   ```

2. **Add `IBlocksProjectiles` extension** for per-actor hit width
   (default: derive from first HitShape extent perpendicular to
   firing line).

3. **In `Armament.CheckFire`** (after the existing per-weapon LOS
   gate and before barrel selection): if `MaxAcceptablePerBlockerHitChance
   > 0`, walk all blocker actors on the line via
   `world.FindBlockingActorsOnLine(muzzle, target, hitWidth)`, compute
   per-blocker P(hit) using the formulas above, deny fire if any
   single P > threshold.

   Compounding choice: per-blocker threshold (deny if any single tree
   exceeds it) is more legible than total-line threshold (deny if
   compound exceeds). Start with per-blocker.

4. **YAML adoption:**
   - 25mm.Bradley, 30mm.BMP2, MBT main guns → opt in with threshold ~10%.
   - Sniper-class weapons → opt in with low threshold ~3% (very
     forgiving — snipers shoot through forests if accurate enough).
   - Artillery → don't opt in. Indirect fire ignores the LOS gate
     anyway.
   - WGM, Hellfire → don't opt in (keep density gate).

5. **Testing:**
   - New `test-bullet-spread-cone-gate-deny`: tank firing through
     trees with low Inaccuracy (rookie) → denied; same tank with
     veterancy buff (resolvedInaccuracy × 0.5) → fires.
   - Reuse `test-wgm-tree-density-ladder` pattern for sweeping the
     accuracy threshold across veterancy levels.

## Risks

- **Sniper-buff side effect.** A sniper fires through forests because
  he's accurate. Realistic but possibly unbalanced; needs playtest.
- **Per-blocker hit-width derivation.** HitShape extent depends on
  actor orientation — for trees this is fine (radial), but for
  rectangular structures it's directional. May need orientation-aware
  width.
- **Performance.** `FindBlockingActorsOnLine` runs per-fire — already
  used by the in-flight projectile check. Should be ~free.
- **Overlap with the density gate.** If both gates active on the same
  weapon, only one fires (whichever denies first). Be explicit about
  which model owns which weapons.

## What this DOES NOT replace

- The fire-time **density-based miss roll** for guided missiles. That
  redirects guided missiles to clip a tree on miss — still useful for
  visual feedback, doesn't conflict with the spread gate.
- The per-weapon LOS gate (`Armament.CheckFire` checks
  `Weapon.ClearSightThreshold`). The spread gate is layered on top.

## Quick playtest plan when this lands

1. Verify autotests pass.
2. Manual: spawn an Abrams in a forest, tell it to attack a t90 across
   3 trees. Veterancy 0 should refuse (cone too wide); maxed veterancy
   should fire (cone tight).
3. Manual: same with Bradley WGM (should still use density gate, refuse
   at 4+ trees regardless of veterancy).
