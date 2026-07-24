# DECLARED Re-Baseline — Influence Stage F strategic repoint (2026-07-24)

**Status: DECLARED, NOT RUN.** This is an instrument-change declaration, following the
convention of `260721_regime_rebaseline.md` / `260721_cohesion_cap_rebaseline.md`. The
benchmark batch itself requires the user's explicit goahead (CLAUDE.md hard rule: no
autonomous batch/tournament runs). No matches were run for this card.

**Build:** `wt/stage-f` off `main` @ `f20d2798`. `make.ps1 all` clean (0 errors),
`make.ps1 test` 334 lint errors == the `main` baseline (all pre-existing gtwr/pbox/hbox
"being-captured" + map-cordon lint, none from this change), NUnit 362/362 green.

---

## Why this exists — the instrument changed for @experimental offense

Stage F (PIPELINE item 9) completes the @experimental fog migration. Before it, the
experimental attack-axis / expansion selection scored targets with a threat term read
from the **omniscient** `InfluenceMap` enemy grid (`InfluenceMap.Recompute` scans
`world.Actors` with no fog check). After it, when `PoiOffensiveBotModule@experimental`
runs with `StrategicRepointEnabled: true`, that omniscient threat is **dropped from the
base score** and re-derived from the **believed** control + anti-ground danger fields:

- **Balance-of-power (terr-bias revival)** reads the believed `ControlField` control of the
  RING around each target (excluding the target's own cell — every enemy target is a
  site-anchor structure the field floors deeply enemy, so its own cell always reads Enemy):
  an enemy structure encircled by ground we believe we hold is pressed (×150), one deep in
  believed-enemy ground is damped (×60), a contested front is left neutral.
- **Believed danger** reads `DangerFieldLayer.GroundDanger` per target cell: safe ×100 /
  mild ×60 / hostile ×20 — the fog-legal stand-in for the old omniscient threat buckets.

**Consequence for the ladder:** every prior `@experimental` S1/S2 number is measured on
the *omniscient-threat* instrument and is **not comparable** to post-Stage-F @experimental.
`@stable`, Normal, Rush, Turtle are byte-identical (the flag is unset on every one of them
and the shared PoiMap path defaults to the omniscient behaviour), so their numbers carry
over unchanged and remain the fixed yardstick.

## What the batch (once user-approved) must read

Run the standard paired-seed batch vs `@stable` (the terr-bias plan §5.4 bars are the
closest precedent, since Stage F revives that lever on the intended substrate):

- **S2** (`tournament-s2-combat-river-zeta` + mirror, N=10): does believed-field axis
  selection turn the relative swing positive **without** dropping engagement
  (engaged-count ≥ 6/10)? The old omniscient terr-bias (4adf867c) failed here because the
  per-POI InfluenceMap factor was a near-pure damper; the hypothesis is that the control
  field (real territorial substrate) presses where it used to only damp.
- **S1** (`tournament-s1-eco-river-zeta` + mirror, N=10): non-regression only (win-rate
  ≥ 0.40 floor, capture parity ±2/10). Repoint is a contact/combat lever.
- **Firing proof:** grep the preserved `debug.log` for `[exp-terr] repoint …` lines
  (per-target control/danger/mul) and `[exp-terr] reeval … boosted/damped/neutral` +
  `[exp-terr] axis-shift` — same marker idiom the SR-contestation cycle used.

**A/B confound note:** this is the FIRST @experimental cycle where the base offensive
score carries no omniscient threat at all; if the batch reads worse, A/B `StrategicRepointEnabled`
off (reverts to the omniscient path in-place) before blaming the balance/danger multipliers.
