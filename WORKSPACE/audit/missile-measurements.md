# Missile programme — measured, not derived

**Measured against `main @ 12a0d194`** (0 commits behind `origin/main` at the time of
the runs), engine built from worktree `wt/missile-measure`, which contains **no engine
or weapon-YAML change** — only new autotest scenarios and two analysis scripts. Every
number below comes from `MissileTrace` JSONL produced by
`tools/autotest/run-test.sh --missile-trace`.

`origin/main` has since advanced to `26d9ae19`, which renames `PercentFromEdge` to
`CenterProximityPercent` (`3aa5d901`). I checked the diff: it is a pure rename plus
documentation, with no change to the arithmetic, so **every number here remains valid at
`26d9ae19`.** The new name is used below.

**841 missiles across 9 runs**, including an independent-seed replication of the decisive one. Where this disagrees with the audit reports, the review,
or the brief, the data wins and it is called out explicitly.

| Run | Scenario | Missiles | What it settles |
|---|---|---|---|
| 1 | `test-mi28-fires-ataka` (existing) | 1 | trace pipeline validation |
| 2 | `test-missile-latch-probe` | — | FAILED to load (case-variant actor key); no data |
| 3 | `test-missile-latch-probe` | 60 | air lanes only — ground lanes silently fired nothing |
| 4 | `test-missile-user-reports` | 54 | AA 2/3/4 cells; ground lanes silently fired nothing |
| 5 | `test-missile-range-sweep` | 320 | I1 distance invariance, both weapons |
| 6 | `test-missile-latch-probe` | 99 | all 7 lanes — **the decisive latch data** |
| 7 | `test-missile-user-reports` | 104 | Mi-28 3840 vs 1280, ATGM probe |
| 8 | `test-missile-user-reports` (crossover) | 104 (32 Ataka) | Mi-28 altitude control |
| 9 | `test-missile-latch-probe` (replication) | 99 | independent seed, final scenario files |

---

## Measurement 1 — why `flyStraight` latches. **Hypothesis 3 (the review) confirmed, and it is worse than the review predicted.**

### The three predictions, scored

| | Prediction | Result |
|---|---|---|
| **H1 flight audit** | `flystraight_min_dist == flystraight_hor_dist` at the latch | **0 / 56. Falsified.** |
| **H2 trace worker** | `flystraight_min_dist + close_enough < flystraight_hor_dist` always | **56 / 56. True — but it is a tautology, see below.** |
| **H3 adversarial review** | a lead-inflated distance is compared against a physical constant, so measured range can jump with no physical change | **CONFIRMED. 38 of the 44 latches with tick data fired while the missile was physically CLOSING on its target.** |

**H2 is true but carries no information.** `minDistanceToTarget` is updated on the two
lines immediately *above* the latch test (`Missile.cs:847-849`, then the test at `:852`), so after the
update `min <= currentDistance` always holds, and the predicate `currentDistance > min +
CloseEnough` can only fire when `min` is a strictly earlier, smaller value. H2 is therefore
entailed by the source and could never have come out any other way. It is worth recording
that it was confirmed, but it does not discriminate anything — and H1, which the flight
audit staked its timeline on, is arithmetically **impossible** for the same reason.

### The latch events of run 6, with the physical distance the code never looks at

`rthd` is `relTarHorDist`, the quantity the code actually tests — the distance to the
**lead point**. `physNow`/`physPrev` are recomputed from the logged positions as the true
missile→target horizontal separation. `dPhys` negative means the missile was getting closer.

| id | lane | tk | rthd | min | ce | physNow | physPrev | **dPhys** | dRthd | reason | dmg |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | air_approach | 4 | 1144 | 240 | 192 | 2367 | 2477 | **−110** | +904 | fuel_out | 0 |
| 3 | air_approach | 6 | 712 | 225 | 192 | 1232 | 1300 | **−68** | +391 | fuel_out | 0 |
| 4 | air_approach | 5 | 1381 | 1116 | 192 | 2268 | 2446 | **−178** | +123 | off_map | 0 |
| 24 | air_reverse | 11 | 1359 | 904 | 192 | 15602 | 15845 | **−243** | +455 | fuel_out | 0 |
| 25 | air_reverse | 11 | 1165 | 890 | 192 | 14580 | 14822 | **−242** | +275 | fuel_out | 0 |
| 26 | air_reverse | 11 | 3162 | 699 | 192 | 15510 | 15785 | **−275** | +2463 | fuel_out | 0 |
| 38 | air_approach | 5 | 1459 | 1250 | 192 | 1171 | 1280 | **−109** | +209 | segment_closest | 1710 |
| 40 | air_reverse | 9 | 1840 | 661 | 192 | 8153 | 8426 | **−273** | +1179 | fuel_out | 0 |
| 41 | air_reverse | 10 | 1183 | 337 | 192 | 6931 | 7166 | **−235** | +846 | fuel_out | 0 |
| 42 | air_reverse | 9 | 2273 | 1464 | 192 | 8007 | 8369 | **−362** | +809 | fuel_out | 0 |
| 56 | air_reverse | 15 | 6621 | 6392 | 192 | 4997 | 5288 | **−291** | −376 | close_enough | 330 |

**All eleven closed on the target during the tick they declared a miss.**

Across the whole corpus the split is **38 closing / 6 opening** out of the 44 latches that
have tick data (the remaining 12 come from the summary-only sweep run, which logs no
positions). The 6 opening cases are the detector working as designed — a genuine
overflight, correctly declared. The 38 closing cases are the defect, and they are 86% of
every latch this session could examine.

### The worked example — missile 24, the whole flight

| tk | state | fs | **phys (true)** | **rthd (tested)** | mdt | ratio | tgt speed |
|---|---|---|---|---|---|---|---|
| 1 | homing | 0 | 18577 | **2 968 118 010 240 000** | same | — | 0 |
| 5 | homing | 0 | 17446 | 11818 | 11818 | 0.68 | 200 |
| 8 | hitting | 0 | 16381 | 3077 | 3077 | 0.19 | 230 |
| 10 | hitting | 0 | 15845 | **904** | **904** | **0.06** | 250 |
| 11 | hitting | **1** | 15602 | 1359 | 904 | 0.09 | 260 |
| 20 | hitting | 1 | 13715 | 6290 | 904 | 0.46 | 265 |

At tick 10 the missile is **15 845 wdist — 15.5 cells — from its target**, and the quantity
the code uses as "distance to target" reads **904**. The target is accelerating toward the
missile, so the lead vector points back past the missile and collapses the aim point onto
it. `minDistanceToTarget` is seeded from that collapsed value. One tick later the ratio
shifts slightly, `rthd` reads 1359 > 904 + 192, and `flyStraight` latches — **at 15.6 cells
range, closing at 240 wdist/tick.** Both steering axes freeze, and the missile flies
straight for the remaining 13 cells to fuel-out, doing zero damage.

### What this changes about the diagnosis

- **The review's root cause is right and its severity is understated.** The review modelled
  collinear constant velocity and derived inflation ratios of 0.456× / 1.544×, predicting a
  latch beyond a physical range of ~177. The measured ratios reach **0.06×**. The dominant
  mechanism is not "reversal multiplies the measured distance" — it is "**approach collapses
  the measured distance to near zero, poisoning `minDistanceToTarget`**", after which almost
  any change in target velocity clears the threshold.
- **The trigger is target motion, and specifically approach and reversal — NOT fleeing, and
  NOT range.** Per-lane, run 6:

  | lane | weapon | n | latched | hit% |
  |---|---|---|---|---|
  | air_hover | manpad | 18 | **0** | 100% |
  | air_flee | manpad | 6 | **0** | 100% |
  | air_approach | manpad | 18 | 4 | 83% |
  | air_reverse | manpad | 18 | **7** | **66%** |
  | gnd_static | atgm | 18 | 0 | 100% |
  | gnd_flee | atgm | 3 | 0 | 100% |
  | gnd_reverse | atgm | 18 | **0** | 100% |

  A **fleeing** target — W1's nominated trigger — produced **zero** latches in 6+3 shots.
  A **hovering** target produced zero in 18. This is direct evidence against W1's framing,
  independent of the arithmetic errors the review already found.

  **Run 9 replicates this on an independent seed, with the final scenario files:**

  | lane | n | latched | hit% |
  |---|---|---|---|
  | air_hover | 18 | **0** | 100% |
  | air_flee | 6 | **0** | 100% |
  | gnd_static | 18 | **0** | 100% |
  | gnd_flee | 3 | **0** | 100% |
  | air_approach | 18 | 3 | 83% |
  | air_reverse | 18 | **9** | **50%** |
  | gnd_reverse | 18 | 2 | 94% |

  Same ordering, same zero-latch set. **13 of the 14 latches were again physically closing.**
  The one difference worth recording: `gnd_reverse` latched twice here against zero in run 6,
  so **ATGM does latch under target reversal**, just rarely — my run-6-only statement that it
  never does was an artefact of a single sample.
- **It scales with target speed relative to missile speed, so anti-air is worst — but it is
  NOT AA-only.** Corpus latch rate: **MANPAD 44/534 = 8.2%**, **ATGM 11/242 = 4.5%**,
  **Ataka 1/65 = 1.5%**. In run 6 the ATGM lanes produced 0 latches in 39 shots even with the
  target oscillating, because a t90 moves at ~85 wdist/tick against a missile doing 300 and
  the lead term stays small. The ATGM latches in the corpus come from the sweep's stationary
  lanes, where zero lead makes `rthd` equal the physical distance and a latch is an honest
  overflight. MANPAD faces a Littlebird at 265 against a missile accelerating to 450, and
  that ratio is what breaks it.
- **A latch is usually fatal.** 41 of 56 latched missiles across all runs did **zero** damage
  to their target; the rest did reduced damage.
- **M1 is live and visible.** Tick 1 of every missile reads `lastTargetPosition` uninitialised:
  missile 24 records `rthd = 2.97e15`, missile 2 records `2 031 496`. Confirmed empirically,
  exactly as the review predicted and as no audit report noticed.

---

## Measurement 2 — the user's two reports

### Mi-28 at the real altitude: **it does not fail at 3840. It is BETTER at 3840 than at the 1280 the existing test uses.**

The brief's premise was "the existing test PASSES at 1280, so a fail at 3840 localises the
bug." **The data inverts it.** Run 7 (two Mi-28 per lane, small lateral offset):

| lane | launch alt | n | median min_dist | median dmg | reasons |
|---|---|---|---|---|---|
| A | **3755** (spawn 3840) | 16 | **259** | **8157** | ground 10, segment_closest 6 |
| B | **1195** (spawn 1280) | 16 | **969** | **336** | ground 16 |

Because that first cut confounded altitude with map row, run 8 is a **crossover**: each
altitude run twice, on widely separated rows, with a single Mi-28 sitting on its target's own
row so every shot is dead ahead.

| lane | row | launch alt | n | median min_dist | spread | mean dmg |
|---|---|---|---|---|---|---|
| A | 4 | 3755 | 8 | **243** | 214–467 | 7485 |
| H | 22 | 3755 | 8 | **255** | 202–355 | 8091 |
| B | 10 | 1195 | 8 | **533** | 106–562 | 6290 |
| G | 16 | 1195 | 8 | **535** | 120–946 | 5777 |

**Altitude tracks; row does not.** Terrain is identical under both lanes (terrain height 85
under every launcher, target `z` 0 in all four lanes), so terrain is excluded as a confound.
The 1280 launch is roughly **twice as dispersed** at the target and its degradation gets much
worse once the shot is not dead ahead (run 7's offset geometry collapsed it to a median
969 miss).

**Consequence for the test suite: `test-mi28-fires-ataka` is pinned to the pessimistic
altitude, not the shipped one.** It is not measuring what a real Mi-28 does. That is a
defect in the test, not in the missile — and the fix is to raise its spawn to 3840, which
will make it pass by a wider margin.

### AA infantry vs a Littlebird at 2, 3, 4 cells: **resolved.**

Run 7, three AA per range, hovering Littlebird at the real 3840 cruise altitude:

| range | n | hit% | median dmg | median min_dist | latched |
|---|---|---|---|---|---|
| 2 cells | 18 | **94%** | 1620 | 149 | 2 |
| 3 cells | 18 | **100%** | 1830 | 158 | 0 |
| 4 cells | 18 | **100%** | 2040 | 134 | 0 |

Run 4 independently measured the same three ranges: **51/54 hits (94%)**. The user's report
was three soldiers all missing; the measured rate after the `MaximumLaunchAngle` fix
(`a629fee7`) is 94–100%. **Verdict: the close-range AA case is RESOLVED**, and the launch-angle
wrap was its cause. What remains at close range is the ordinary latch behaviour, which needs a
manoeuvring target to fire and is not range-gated.

---

## Measurement 3 — I1, distance invariance. **Holds against stationary targets.**

320 missiles, one lane per range, stationary targets so any trend is attributable to range
alone. Target motion is measured separately in Measurement 1 and deliberately excluded here.

**MANPAD vs a hovering Littlebird** (weapon `Range: 23c0`):

| cells | 1 | 2 | 3 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18 | 19 | 20 | 21 | 22 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| hit% | 87 | 97 | 100 | 100 | 100 | 100 | 100 | 87 | 87 | 87 | 87 | 87 | 75 | 87 | 87 | 87 | 87 | 87 | 87 |
| median min_dist | 182 | 144 | 176 | 168 | 170 | 177 | 164 | 83 | 91 | 105 | 105 | 90 | 105 | 180 | 144 | 84 | 133 | 144 | — |

**ATGM vs a stationary t90** (`Range: 20c0`, `MinRange: 3c0`):

| cells | 3 | 4 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| hit% | 100 | 100 | 100 | 87 | 100 | 100 | 87 | 87 | 87 | 87 | 87 | 87 | 87 | 87 | 87 |
| median dmg | 7347 | 7447 | 7347 | 9147 | 7747 | 6447 | 7547 | 9047 | 7447 | 7747 | 7647 | 8547 | 7347 | 7247 | 8547 |

**No systematic trend in either weapon.** The apparent 100%→87% step is an artefact of the
rig, not of range: it is one `unterminated` record per lane — a missile still in flight when
the 60-second clock stopped — and 1/8 = 12.5%. Excluding those, every lane is at or near
100%. **I1 holds for both weapons across their full permitted envelope against a stationary
target.** The distance-invariance violation the user reported is not a function of range;
Measurement 1 shows it is a function of target motion.

---

## Measurement 4 — the by-products

### Detonation altitude vs the weapon's own `AirThreshold`

| weapon | AirThreshold | n | ground | subterrain | **air** | air share |
|---|---|---|---|---|---|---|
| atgm (Javelin) | 128 | 242 | 211 | 16 | **15** | **6.2%** |
| ataka (Mi-28) | 128 | 65 | 17 | **41** | 7 | **10.8%** |
| manpad | 128 | 534 | 0 | 0 | 534 | 100% |

**This settles what the audit could not resolve statically.** ATGM detonates above the
render threshold **6.2% of the time — not "every airborne detonation"**. W2's §F.1 called
this "the single most important unknown"; the answer is that the overwhelming majority of
Javelin detonations happen at the target near ground level, and only about 1 in 16 sits in
the silent bucket. The `Warhead@EffectAir` added at `e7504a9f` was still the right fix — 15
detonations in 242 that previously drew and played nothing is a real defect — but its scope
is an order of magnitude smaller than the audit implied.

**Ataka's distribution is the interesting one and nobody predicted it: 63% of Ataka
detonations end BELOW terrain** (41 of 65 `subterrain`), and 10.8% are in the `air` bucket —
twice ATGM's rate. MANPAD is at 100% `air` by construction (it only shoots aircraft) and
correctly inherits `^MediumExplosionEffectsAir`.

Also measured: **16 ATGM detonations ended below terrain** (`reason: ground`, `end_dat` −8 to
−80). Most still did full damage — they buried into the ground inside the tank's hitshape.

### `dud_prearm` and `unterminated`

| outcome | count | share |
|---|---|---|
| detonated | 819 | 97.4% |
| **dud_prearm** | **0** | **0%** |
| unterminated | 22 | 2.6% |

**The trace worker's reachability analysis is confirmed.** `dud_prearm` is zero in 841
missiles — the pre-arm removal path is not reached in normal play. `explode_calls > 1` is
also **zero** across all 841, confirming the jammed-APS double-detonation is dormant (the review's M4
reached the same conclusion statically). The 22 `unterminated` are all from one run and are
purely run-end truncation: missiles still in flight when the fixed 60-second clock stopped.
That is the code path doing exactly its job, not a failure to terminate.

### Damage vs impact position — **the corrected model is confirmed to within ~1 point; the audit's step model is refuted**

The manager's correction arrived mid-session and is **exactly right**. Measured, per ATGM
missile, against `CenterProximityPercent` = `100 × (halfDiagonal − distFromCentre) / halfDiagonal` with the t90's own
hitshape (`Rectangle TopLeft -400,-950 / BottomRight 400,950` → half-diagonal
`isqrt(400² + 950²) = 1030`), and with the flat 47 from `SpreadDamage` subtracted:

| min_dist | 43 | 73 | 96 | 128 | 155 | 180 | 197 | 205 | 218 |
|---|---|---|---|---|---|---|---|---|---|
| **measured %** | 95.0 | 92.0 | 90.0 | 87.0 | 85.0 | 82.0 | 80.0 | 80.0 | 78.0 |
| **model %** | 95.8 | 92.9 | 90.7 | 87.6 | 85.0 | 82.5 | 80.9 | 80.1 | 78.8 |

Agreement within ~1 percentage point across 44 samples. **The falloff inside the hull is
continuous and linear in distance-from-centre, exactly as the user observed in game.** The
audit's ~3300× step and the review's "CONFIRMED exactly" are both wrong.

**One measured detail neither the audit nor the correction states.** There *is* a
discontinuity, but it is at the **hull boundary**, and its size is asymmetric because the
ramp is scaled on the half-diagonal (1030) while the hull's short half-axis is only 400.
Samples at comparable `min_dist` split cleanly into two populations:

| min_dist | 400 | 405 | 410 | 418 | 418 | 425 | 435 | 475 | 475 | 493 | 516 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| damage | 6147 | **17** | 6047 | 6047 | **44** | **45** | 5747 | **17** | 5347 | 5247 | **13** |

Inside the hull, ~6000; one wdist outside it, ~17 — because `TargetDamage` gates on
`closestDistance > Spread` measured from the hull **edge** (the gate is a true edge distance;
`CenterProximityPercent` next to it is not, which is exactly the confusion the rename fixes), and ATGM's `TargetDamage.Spread`
is the default `WDist(1)`. So a shot leaving across the **long** face has already decayed to
~8% and the step is trivial; a shot leaving across the **short** face still had ~61%
remaining and loses ~6000 damage in one wdist. **The audit's error was conflating "512 from
centre" with "outside the hull"** — for a t90, 512 from centre along the long axis is still
comfortably inside. This is reported for the record only; per the standing ruling the falloff
is out of scope for fixing.

The corner-scaling oddity the correction flagged is consistent with the fit but I have no
clean nose-on sample to quote, so I am not claiming to have measured it.

---

## What the data contradicts

1. **W1's `flyStraight` trigger is wrong in both directions.** Not short range (the sweep
   shows range has no effect) and not a fleeing target (**zero** latches in the flee lanes).
   It is approach and reversal. W1's proposed `minDistanceToTarget` reset would not fix the
   measured cases, which is what the review already argued from source.
2. **H1 — "`min` pinned at the launch distance" — is impossible, not merely unobserved.**
   The min-update sits two lines above the test.
3. **The brief's Mi-28 premise is backwards.** 3840 is the *good* altitude; 1280, which the
   shipped test uses, is the degraded one. A "fail at 3840" was never going to localise
   anything.
4. **The review understates the latch severity by an order of magnitude.** Predicted worst
   inflation 1.544×/0.456×; measured 0.06×.
5. **W2's invisible-explosion scope is 6.2% of ATGM detonations, not all airborne ones.**
   Real, but an order of magnitude smaller than presented. Ataka's rate is 10.8%.
6. **The ~3300× damage step does not exist** (already retired by the manager's correction;
   the data confirms the retirement and locates the real, smaller, hull-boundary step).
7. **The AA close-range report is closed.** 94–100% at 2–4 cells.

## What I could not measure, and where I may be wrong

- **The closing/opening split rests on 44 of the 56 latches.** The other 12 are from the
  summary-only sweep run, which logs no positions, so I cannot say which kind they were. If
  all 12 were honest overflights the defect share drops from 86% to 68% — still the large
  majority, but I did not measure it.
- **I never found the ATGM threshold.** ATGM latches rarely against a slow ground target
  (0/18 in run 6, 2/18 on replication; 11/242 corpus-wide), so "slow ground targets rarely trigger it" is
  supported, but I did not sweep target speed to find where the rate climbs. A faster ground
  target, or a Hellfire-armed aircraft engaging a moving vehicle, could latch and I did not
  test it.
- **63% of Ataka detonations landing below terrain is unexplained.** It correlates with the
  low-altitude launch being less accurate, but I did not trace the mechanism and it may be
  entirely normal for a weapon with `CruiseAltitude: 100` against a ground target.
- **The sweep used stationary targets by design.** I1 is therefore verified only for the
  static case. A moving-target range sweep was not run; on the strength of Measurement 1 I
  expect hit rate there to depend on target velocity and not on range, but that is a
  prediction, not a measurement.
- **`damage_to_target` for ATGM is flagged `damage_unattributed` on all 167 records.** The
  deferred warhead is `^MediumExplosionEffects`'s `Warhead@Shrapnel` (`Delay: 5`,
  `ValidTargets: Infantry, Unarmored`), which cannot apply to a t90 at all, so the figures
  are complete for these targets. Against infantry they would be understated.
- **Ataka records carry the same flag and it matters there too** — the Mi-28 comparison is
  based on relative damage between lanes, which the flag does not bias, but the absolute
  Ataka figures are lower bounds.
- **`littlebird` and `t90` were given 900 000 HP** so a lane could not run out of target
  mid-volley. That keeps every target permanently `Undamaged`, which suppresses the
  "abandon a target that reaches Critical" behaviour. Anything that behaviour would have
  changed is invisible here.

---

## Rig notes for whoever runs this next

Three scenarios are added, all measurement rigs rather than pass/fail tests — their verdicts
are **controls on sample count**, deliberately so, because a rig that quietly fires nothing
still writes `pass` and would be read as "no defect observed". That failure mode bit twice
during this session and both are now guarded:

- **A ground actor must be created from Lua with `Location`, not `CenterPosition`.** With
  `CenterPosition` it spawns, renders and reports alive, but **no ground weapon will engage
  it**. Runs 3 and 4 lost every ATGM and Ataka lane to this, and the only symptom was
  launchers sitting on untouched ammo. Aircraft still need `CenterPosition` — it is the only
  way to set an exact altitude.
- **A scenario `rules.yaml` actor key must match the source casing exactly** (`t90`, not
  `T90`) or the mod fails to load with `duplicate values found for the following keys`.

- **Do not pair `Health: HP:` with `-HeliEmergencyLanding:`/`-ChangesHealth@CrashBurn:` on
  `littlebird`.** Removing those traits strips conditions other littlebird traits consume and
  adds a genuinely new `make test` error identity. They cannot fire at 900k HP anyway. With
  that corrected, **all three new maps contribute zero lint errors** (verified by extracting
  their own `Testing map:` sections, since three added maps shift the total).

Analysis: `tools/autotest/analyze-missiles.py` (latch/tick analysis, takes an optional
lane map) and `tools/autotest/analyze-sweep.py` (range vs outcome).

**Runs 1–8 were taken with those two trait removals still present; run 9 is the replication
without them and reproduces every headline.** At 900k HP neither trait can fire, so the two
rulesets are behaviourally identical, and run 9 is the evidence for that rather than the
argument for it.
