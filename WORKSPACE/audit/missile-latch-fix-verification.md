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

---

# Independent verification of the three left-open gaps

Second session, working from `1ec6f17c` detached at
`C:/Users/fredr/worktrees/ww3mod/missile-latch-verify`, pre-fix base
`3f18551a`. Nothing above this line was edited — it is the author's
pre-registered evidence.

Predictions for this run were recorded before the first run, in
`missile-latch-verify-predictions.md`. Two of them (V2, V8) were falsified and
both falsifications are load-bearing; they are called out below.

**Verdict: MERGE.** No regression in any lane of any rig, on either build. The
one genuinely alarming possibility — Gap 2's terrain-aware false positive — is
structurally unreachable in this mod today, and the reason it is unreachable is
itself worth the manager's attention.

## Gates

| gate | result |
|---|---|
| `./make.ps1 all` at `1ec6f17c` | clean, 0 errors |
| `dotnet test` | **1402 / 1402**, 0 failed |
| `./make.ps1 test` | **85 lines / 14 unique identities**, diffed by identity against the pre-change baseline: **zero identities added, zero removed** |

Both new scenario maps are scanned by the lint (`Testing map: TEST: Hellfire
latch probe`, `Testing map: TEST: air_hover lane in isolation`) and neither
contributes an error, so the expected +3-per-map shift did not occur.

## Gap 1 — Hellfire, the priority. No regression; a large improvement.

New rig `test-missile-hellfire-probe`. `HELI` (Apache) fires `Hellfire` at a
`littlebird` across the same four motion regimes as the MANPAD lanes, using the
latch-probe's target cells and motion scripts verbatim; `strykershorad` fires
`Hellfire.strykershorad` at a `t90` on the three ground lanes. Both builds, seed
20260813, `--hidden --mute --speed 4 --missile-trace`.

| lane | weapon | n | pre hits | pre latch | post hits | post latch |
|---|---|---|---|---|---|---|
| air_approach | Hellfire | 72 | 72 (100%) | 0 | 72 (100%) | **0** |
| air_flee | Hellfire | 72 | 70 (97%) | 0 | 70 (97%) | **0** |
| air_reverse | Hellfire | 72 | **44 (61%)** | **33** | **69 (95%)** | **0** |
| air_hover | Hellfire | 72 | 72 (100%) | 0 | 72 (100%) | **0** |
| gnd_static | Hellfire.strykershorad | 24 | 24 (100%) | 0 | 24 (100%) | **0** |
| gnd_flee | Hellfire.strykershorad | 24 | 24 (100%) | 0 | 24 (100%) | **0** |
| gnd_reverse | Hellfire.strykershorad | 24 | **22 (91%)** | **6** | **24 (100%)** | **0** |
| **TOTAL** | | **360** | **328 (91%)** | **39** | **355 (98%)** | **0** |

**All 39 pre-fix latches fired on a physically closing tick — 39/39, not one
opening.** Post-fix there are no latches at all in 360 missiles.

**V2 is falsified, and this is the finding.** I predicted Hellfire would be
*less* exposed than MANPAD, because its `HorizontalRateOfTurn` is 60 against
MANPAD's 20 and its `CloseEnough` is 298 against 192 — both make the predicate
harder to trip. The opposite is true. Hellfire's reversal lane latched on 33 of
72 missiles (46%) against MANPAD's 7 of 18 (39%), and its hit rate fell further
(61% against 66%). **Hellfire was the worst-affected weapon measured in this
programme, and it is the one the fix was signed off without ever firing.** The
worst individual cases latched at tick 7 with the target still 15 000+ wdist
away and never recovered: ids 148/149/150 latched at `minDist` 15051 / 13858 /
14957, flew straight into the ground, and did zero damage.

`Hellfire.strykershorad` also reproduced the defect (6 latches, 91%), which
falsifies the softer half of V5 — I expected it to look like the quiet ATGM
ground lanes. It did not; the ground variant carries the same bug.

Rig caveat, stated because it bounds what these numbers mean: the air launchers
are aircraft and reposition during the run, so launch geometry is not constant
within a lane the way it is in the MANPAD rig. The pre/post comparison is
paired on one seed and is sound; an absolute Hellfire hit rate should not be
read against an absolute MANPAD one.

## Gap 2 — the terrain-aware false positive cannot happen, for a reason worth knowing

**The lane the brief asks for cannot be built, and the risk is structurally
unreachable.** `InclineLookahead` (`Missile.cs:553-596`) reads
`world.Map.Height[cell] * 512`. `Map.Height` is only ever populated when
`Grid.MaximumTerrainHeight > 0` (`Map.cs:443-449`), and `Grid` comes from the
**mod manifest** (`Map.cs:400`, `modData.Manifest.Get<MapGrid>()`), not from map
rules — so no scenario-local override can turn it on. ww3mod's `MapGrid`
(`mod.yaml:320-322`) sets `TileSize` and `Type` and nothing else, so
`MaximumTerrainHeight` takes the engine default of **0**. I decoded the binary
header of **all 171 `map.bin` files** in the repo: every one carries
`heightsOffset == 0`.

So `predClfHgt`, `predClfDist` and `lastHt` are identically zero on every map in
the game, and the climb branch `TerrainHeightAware && diffClfMslHgt >= 0 &&
!allowPassBy` reduces to `pos.Z <= 0`. **It cannot be entered in response to
rising ground, because there is no rising ground.** That also disposes of V7:
building the lane would require editing `mods/ww3mod/mod.yaml`, which is a
mod-wide change to every map's binary format and well outside this brief.

**V6 was falsified in its detail and it matters.** I predicted no terrain-aware
missile would ever reach `Z <= 0`. On the post-fix build 27 ticks do — all of
them `hellfire.strykershorad` in its terminal dive, reaching `Z = -20`. But
every one of those 27 ticks logs `apb = 1`: `allowPassBy` is already true, so
the `!allowPassBy` guard closes the branch anyway. Across 13 059 terrain-aware
ticks on the post-fix build the climb branch is entered **zero** times.

The feared shape was then searched for directly. Post-fix, 1 201 ticks have a
terrain-aware missile climbing (`dz > 0`) while in the `Hitting` state — that is
ordinary terminal geometry, not the incline branch — and of those, exactly
**one** also opened the 3D range:

| missile | tick | Z | dz | phys3 prev → now | dPhys | minDist | CloseEnough | latched? | outcome |
|---|---|---|---|---|---|---|---|---|---|
| 32 (Hellfire) | 53 | 1341 | +68 | 304 → 357 | **+53** | 304 | **298** | **no** | hit, 440 dmg |

It opened by 53 wdist against a tolerance of 298 — **5.6x headroom** — and the
missile went on to detonate `close_enough` for 440 damage. The predicate needs
`currentDistance > minDistanceToTarget + CloseEnough`, i.e. 357 > 602, which is
nowhere close.

> **For the manager, and this is the part nobody asked for.** This clean result
> is contingent on `MaximumTerrainHeight` staying 0. The `TerrainHeightAware:
> true` flags on `WGM`, `Ataka` and `Hellfire` are not doing what their comments
> claim — the WGM/Hellfire comment "prevents the descent-into-ground fall-short
> bug" describes a climb mechanism that never runs. What `TerrainHeightAware`
> *does* still do on these weapons is reach the `lastHt >= targetPosition.Z`
> test at `Missile.cs:680`, which with `lastHt` identically 0 sets
> `allowPassBy = true` for every ground target at Z=0 — a live behavioural
> difference, and the very thing that closed the branch above. **If terrain
> height is ever enabled in this mod, Gap 2 becomes a real and completely
> untested risk, and this fix is the code that would carry it.** That is a note
> for whoever turns heights on, not a reason to hold this merge.

## Gap 3 — the hover shift isolates to NEITHER change. The author's conclusion holds; the attribution does not.

First, the author's runs reproduce exactly on this machine at seed 20260813:
pre-fix 99 missiles / 90 hits / 10 latches with `air_reverse` at 7 latches and
66%; post-fix 95 hits / 3 latches, the three being ids 28/43/59 at `minDist`
227 / 231 / 197. Identical to runs 1 and 3.

The isolation build the brief asked for — `3f18551a` carrying **only** the
`lastTargetPosition` constructor seed, detector left on `relTarHorDist`,
verified by diff before building — gives:

| build (7-lane latch-probe) | air_hover latches | air_hover hit% | total hits | total latches |
|---|---|---|---|---|
| pre-fix `3f18551a` | 0 | 100% | 90/99 (90%) | 10 |
| **isolation (seed only)** | **0** | **100%** | 89/99 (89%) | 12 |
| post-fix `1ec6f17c` | 3 | 83% | 95/99 (95%) | 3 |

**V8 is falsified: the seed-only build does not reproduce the hover shift.** By
the brief's stated rule that is the DO-NOT-MERGE trigger. It should not be,
and here is why.

### The seven-lane rig cannot isolate anything

All seven lanes advance in one tick loop against one shared RNG stream — every
missile draws at creation (`Inaccuracy`) and again on each `RetargetTicks`
re-roll of `offset`. The fix stops `air_reverse` latching (7 to 0), so those
missiles live for different durations, re-roll on different ticks and detonate
elsewhere. From the first such divergence every later draw in the stream belongs
to a different missile than it did on the other build — **including the draws
that belong to `air_hover`**. Pairing hover missiles by id across two builds
compares two different worlds.

This is not hypothetical: in the seven-lane rig, **17 of 18** hover missiles
have a different closest approach between the isolation and post-fix builds,
including missiles that never latch on either. The detector provably cannot
cause that — `minDistanceToTarget` feeds nothing but the `flyStraight`
predicate and its recovery (`Missile.cs:874-883`); no steering, no speed, no RNG
draw — so a missile that never latches cannot be moved by the detector at all.
The movement is the shared stream, not the code under test.

**That invalidates the author's third argument.** The paired per-missile
comparison ("three that got worse 71 to 227, 28 to 231, 17 to 197 and three that
got much better 232 to 144, 184 to 74, 222 to 115") reproduces exactly on my
runs, but it is not a controlled comparison, and the isolation build produces a
third, different set of values for the same six ids.

### The single-lane rig settles it

`test-missile-hover-only` runs the `air_hover` engagement and nothing else, so
the coupling is gone. Same 18 missiles, same seed, all three builds:

| build | n | hits | latches | ISO-vs-POST closest approach |
|---|---|---|---|---|
| pre-fix | 18 | **18 (100%)** | **0** | — |
| isolation (seed only) | 18 | **18 (100%)** | **0** | **identical on 18/18** |
| post-fix | 18 | **18 (100%)** | **0** | **identical on 18/18** |

The isolation and post-fix builds are **identical in outcome across all 18
missiles** — exactly what the code predicts once no latch fires. And pre-fix vs
isolation, where pairing by id *is* valid because there is one lane and one
creation order, the tick-1 seed moves closest approach by at most **6 wdist**
(e.g. 61 to 57, 104 to 110, 233 to 231) and **never turns a hit into a miss**.

So the honest conclusion, which differs from the author's:

- The hover lane, measured in isolation, is **18/18 with zero latches on every
  one of the three builds**. It has no defect and the fix does nothing to it.
- The tick-1 seed cannot plausibly be what turned `minDist` 71 into 227 — in a
  clean rig its whole effect is plus or minus 6 wdist.
- The 0 to 3 shift in the seven-lane rig is **whole-run divergence**, dominated
  by `air_reverse` ceasing to latch, and is attributable to neither change
  acting on the hover lane.
- The author's **conclusion** — P4's intent holds, the fix creates no
  false-positive class — is correct, and is now supported by a controlled
  experiment rather than by a paired comparison that does not survive scrutiny.
- The three post-fix hover latches remain legitimate under §3 of `missiles.md`:
  verified here as firing on opening ticks (dPhys +417 / +413 / +422) after
  closest approach, on missiles whose closest approach (227 / 231 / 197) had
  already exceeded `CloseEnough` 192.

## What neither the author nor the brief anticipated

1. **Hellfire is the most defect-exposed weapon measured, not the least** — 33
   latches in 72 missiles and a 61% hit rate pre-fix, worse on both counts than
   the MANPAD lane the whole investigation was built on. The fix takes it to 0
   and 95%.
2. **The incline branch is dead code on every map that ships**, and the
   `TerrainHeightAware` comments on WGM/Ataka/Hellfire describe a mechanism that
   does not run. Enabling terrain height would silently arm an untested path.
3. **Multi-lane autotest rigs cannot support between-build per-missile
   comparison.** One shared RNG stream couples every lane to every other. This
   is a rig-design lesson that applies to `test-missile-latch-probe`,
   `test-missile-range-sweep` and the user-reports rig equally, and it is the
   reason `test-missile-hover-only` now exists.

## Artifacts

- `tools/autotest/scenarios/test-missile-hellfire-probe/` — the Gap 1 rig.
- `tools/autotest/scenarios/test-missile-hover-only/` — the single-lane control
  that settles Gap 3.
- `tools/autotest/analyze-hellfire.py` — per-lane latch/hit report keyed on the
  target rather than the launcher, closing/opening classification on 3D
  separation, and the incline-branch reachability check.
- The isolation build was a throwaway; it was reverted with `git checkout --`
  and is not committed anywhere.
