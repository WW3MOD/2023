# The guidance-latch fix — prediction, then measurement

Branch `wt/missile-latch-fix`, from `main @ 3f18551a`.

Everything above the "Baseline" heading was written **before any code was
changed**, and is left exactly as written whether or not it held. Five
hand-derived analyses in this programme have been wrong; a prediction recorded
after the fact is worth nothing.

## The fix under test

`Missile.HomingTick` feeds `minDistanceToTarget`, the miss predicate and the
recovery predicate a *lead-inflated* distance (`relTarHorDist`, derived from
`targetPosition + leadTarget + offset - pos`) and compares it against
`info.CloseEnough`, a *physical* constant. The two are not commensurable. Fix:
drive those three from the physical missile→target separation, leaving the
steering aim point (`tarDistVec`, `velVec`, `HomingInnerTick`) lead-corrected
and untouched.

Two further defects in the same lines, fixed together:
`minDistanceToTarget` is horizontal-only (a miss purely in Z never registers),
and `lastTargetPosition` is read uninitialised on tick 1.

## Predictions

**P1 — baseline replicates.** A fresh run of `test-missile-latch-probe` on
unmodified `main` reproduces the measured signature: latches concentrated in
`air_reverse` (≥5 of 18) and `air_approach` (≥2 of 18); **zero** latches in
`air_flee`, `air_hover`, `gnd_static`, `gnd_flee`. ≥75% of latches with tick
data fire on a tick where the *physical* separation shrank.

**P2 — the closing latches are an artefact of the lead term, not of geometry.**
Replaying the same predicate offline against physical distance, over the
baseline's own logged positions, fires **zero** times before the real latch tick
in `air_flee`, `air_hover`, `gnd_static`, `gnd_flee`. If a physical-distance
predicate would have fired in those lanes, the fix creates a new false-positive
class and 3D is the wrong choice.

**P3 — closing latches go to zero, and this is nearly tautological.** At a latch
*edge* the previous tick must have failed the same test, so
`p(t−1) ≤ min(t−1) + CE`; and `p(t) > min(t) + CE` with `min(t) ≤ min(t−1)`
forces `p(t) > p(t−1)`. So once the predicate runs on physical distance, a
first latch can only occur on a strictly opening tick, except across a
state-entry or retarget-reset boundary. **The informative half of P3 is the
count**: `air_reverse` drops from ≥5/18 to ≤2/18 and its hit rate rises from
~50–66% to ≥85%.

**P4 — no new false positives.** `air_flee`, `air_hover`, `gnd_static`,
`gnd_flee` stay at **0** latches after the fix.

**P5 — genuine overflights still latch.** Total latches across the three
scenarios stay **> 0**, and every surviving latch sits on an opening tick with
physical distance close to `min + CloseEnough`. A corpus-wide zero would mean
the latch was disabled rather than repaired, which is an explicit failure.

**P6 — I1 holds.** `test-missile-range-sweep` shows no systematic hit-rate
trend with range, for either weapon.

**P7 — tick 1 stops reading garbage.** The `rthd` values of order 1e6–1e15 that
every missile currently logs on tick 1 disappear; tick-1 `rthd` becomes the
honest launch distance.

---

# Baseline and result

Six runs, all `--hidden --mute --speed 4`. Pre and post share seed `20260813`
so the same launcher fires at the same tick against the same target; missile ids
therefore pair one-to-one between the two probe runs. A second post-fix probe on
seed `77712345` is the independent replication.

| run | scenario | build | seed | n | hits | latches |
|---|---|---|---|---|---|---|
| 1 | latch-probe | pre | 20260813 | 99 | 90 (90%) | **10** |
| 2 | user-reports | pre | 20260813 | 104 | 101 (97%) | 6 |
| 3 | latch-probe | post | 20260813 | 99 | 95 (95%) | **3** |
| 4 | user-reports | post | 20260813 | 104 | 102 (98%) | 2 |
| 5 | latch-probe | post | 77712345 | 99 | **99 (100%)** | **0** |
| 6 | range-sweep | post | 20260813 | 320 | 297 (92%) | 2 |

## The acceptance criterion

**Latches on a physically-closing tick: 10/10 → 0/0.**

Pre-fix run 1 is a cleaner instance of the defect than the 38/44 on record —
*every* latch fired while the missile was closing:

| id | lane | tk | rthd (tested) | min | physNow | physPrev | dPhys | reason | dmg |
|---|---|---|---|---|---|---|---|---|---|
| 2 | air_approach | 4 | 1129 | 232 | 2367 | 2477 | **−110** | fuel_out | 0 |
| 3 | air_approach | 4 | 785 | 260 | 1356 | 1461 | **−105** | fuel_out | 0 |
| 4 | air_approach | 4 | 1122 | 839 | 2453 | 2618 | **−165** | off_map | 0 |
| 24 | air_reverse | 11 | 1161 | 732 | **15599** | 15843 | **−244** | fuel_out | 0 |
| 25 | air_reverse | 11 | 1216 | 812 | 14511 | 14776 | **−265** | fuel_out | 0 |
| 26 | air_reverse | 11 | 2891 | 520 | 15514 | 15787 | **−273** | fuel_out | 0 |
| 40 | air_reverse | 9 | 1828 | 676 | 8153 | 8426 | **−273** | fuel_out | 0 |
| 41 | air_reverse | 10 | 1216 | 483 | 6878 | 7141 | **−263** | fuel_out | 0 |
| 42 | air_reverse | 9 | 1915 | 1300 | 8162 | 8475 | **−313** | fuel_out | 0 |
| 57 | air_reverse | 14 | 5981 | 5349 | 4265 | 4540 | **−275** | close_enough | 300 |

Missile 24 reproduces the recorded worked case: 15.6 cells of true range, the
predicate reading 1161, closing at 244/tick, both steering axes frozen, zero
damage.

After the fix, no latch anywhere in runs 3–6 is on a closing tick — 5 latches
total across 522 post-fix missiles, all 5 opening.

## Per lane, latch-probe, same seed

| lane | n | pre latches | post latches | pre hit% | post hit% |
|---|---|---|---|---|---|
| air_approach | 18 | 3 | **0** | 83% | **100%** |
| air_reverse | 18 | **7** | **0** | **66%** | **94%** |
| air_hover | 18 | 0 | 3 | 100% | 83% |
| air_flee | 6 | 0 | 0 | 100% | 100% |
| gnd_static | 18 | 0 | 0 | 100% | 100% |
| gnd_flee | 3 | 0 | 0 | 100% | 100% |
| gnd_reverse | 18 | 0 | 0 | 100% | 100% |

`air_reverse` — the lane that carried the defect — goes from 7 latches and a 66%
hit rate to zero latches and 94%.

## P4 failed as stated, and the failure is instructive

**`air_hover` went 0 → 3 latches, so P4 is falsified as written.** Three separate
lines of evidence say the miss detector is not what changed there:

1. **The three latches follow the miss, they do not cause it.** `min_dist_tick`
   is 34/36/36 against `flystraight_tick` 36/38/38 — closest approach came
   first, and it was 227/231/197 against a `CloseEnough` of 192. All three
   missiles had already physically missed, by 5 to 39 wdist, before the latch.
   Anti-air's rule is fly on to fuel-out; `reason` is `fuel_out` on all three.
   This is the detector doing its job, and it is the same evidence that
   satisfies **P5**.
2. **Replaying a physical predicate over the PRE-fix trajectories gives zero
   hover latches** (the counterfactual below). The detector change alone cannot
   produce them; the trajectory change can.
3. **The trajectory change is the `lastTargetPosition` fix, and it is a wash.**
   Paired by id, the same lane has three missiles that got worse
   (71→227, 28→231, 17→197) and three that got much better (232→144, 184→74,
   222→115). Corpus-wide the same run went 90→95 hits. Run 5, an independent
   seed, put `air_hover` back at **18/18 with zero latches**, and every other
   lane at 100%.

So P4's intent — the fix must not create a new false-positive class — holds. Its
letter did not, because the lane was at 18/18 pre-fix and had no honest miss for
the detector to catch.

## P2 — the counterfactual that chose 3D

Before writing any code, the miss predicate was replayed offline against the
logged positions of runs 1 and 2 (203 missiles), driven by physical distance in
both 3D and horizontal, and compared against where the real latch fired:

| lane group | 3D: latches earlier than the real one | horizontal: same |
|---|---|---|
| all 7 probe lanes | **0** | **0** |
| all user-reports lanes | **0** | **0** |

Neither metric fires anywhere in `air_flee`, `air_hover`, `gnd_static`,
`gnd_flee` or `gnd_reverse`, and neither fires earlier than the shipped
predicate anywhere at all. In particular the population the brief warned about —
ATGM diving from `CruiseAltitude: 10c0` onto a ground target — produced **0**
counterfactual 3D latches in 57 missiles (`gnd_static`, `gnd_reverse`,
`F_atgm_probe`). The measurement does not discriminate 3D from horizontal; it
establishes that neither creates a false-positive class, which is what freed the
choice to be made on correctness grounds.

**3D was chosen because `CloseEnough` is a 3D radius everywhere else in
`Missile.cs`** — the detonation test (`relTarDist < info.CloseEnough`) and the
segment closest-approach test both measure it in three dimensions. The
horizontal-only miss detector was the sole exception, and it cannot see a
missile that misses over the top of an aircraft, which §3 of `missiles.md`
names as a defect.

## P6 — I1 still holds

Post-fix sweep, 320 missiles, stationary targets. ATGM: 100% at cells 3–9, 87%
at 10–18. MANPAD: 100% at cells 1–10, 87% at 12–22 with 75% at 11 and 16. The
87% floor is the same rig artefact the baseline documents — exactly one
`unterminated` per 8-missile lane, i.e. 12.5% — not a range effect. **No
systematic trend in either weapon**, and close range improved against the
recorded baseline (MANPAD cell 1: 87%→100%, cell 2: 97%→100%). Sweep latches
fell from 12 to 2.

## P7 — confirmed

Tick-1 `rthd`, air_hover missiles 8/9/10/27: **19 002 713 / 17 375 845 /
19 089 282 / 18 015 263** before, **11 692 / 10 966 / 11 932 / 11 822** after —
the honest launch distance. Tick-1 `desiredHFacing` moves by ~6 facings (8°),
which is the steering error being removed.

## Scorecard

| | prediction | result |
|---|---|---|
| P1 | baseline replicates | **held** — 10 latches, all in air_approach/air_reverse, ≥75% closing (measured 100%) |
| P2 | no counterfactual false positives | **held** — 0/203 in either metric |
| P3 | closing latches → 0; air_reverse ≤2 latches and ≥85% hits | **held** — 0 closing; air_reverse 0 latches, 94% |
| P4 | flee/hover stay at zero latches | **falsified as written** — hover 0→3, all three honest post-miss declarations; 0 again on an independent seed |
| P5 | genuine overflights still latch | **held** — 5 post-fix latches, all opening, all after closest approach |
| P6 | I1 holds | **held** |
| P7 | tick-1 garbage gone | **held** |

## What this contradicts

- **The corpus split was 38 closing / 6 opening; run 1 measured 10 / 0.** The
  recorded 86% is if anything an understatement of how one-sided the defect is
  in the reversal and approach lanes; the 6 opening cases in the old corpus came
  from lanes this seed did not produce misses in.
- **The measurements say `gnd_reverse` latches rarely (0/18 run 6, 2/18 run 9).**
  This session saw 0/18 pre-fix, consistent with run 6.
- Nothing else in `missile-measurements.md` is contradicted.

## Left open

- **The hover-lane accuracy shift was not isolated by experiment.** The
  attribution to `lastTargetPosition` rests on the counterfactual plus the paired
  per-missile comparison, not on a build with only one of the two changes.
- **Only MANPAD and ATGM fly in these rigs.** Ataka appears in the user-reports
  Mi-28 lanes (100% both before and after); WGM, Hellfire and the SAMs were
  never fired. Hellfire is the one to watch — `Speed: 500` against a manoeuvring
  air target is the ratio that drove this defect on MANPAD.
- **`TerrainHeightAware` is set on WGM, Ataka and Hellfire**, so the incline
  branch can climb inside the `Hitting` state and open a 3D range while the
  missile is behaving correctly. No such case appeared in the Ataka lanes, but
  none of those lanes has terrain rising between launcher and target. This is
  the most likely place for a 3D false positive to hide.
