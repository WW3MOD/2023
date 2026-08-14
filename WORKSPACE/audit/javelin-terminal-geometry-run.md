# Javelin terminal geometry — the measurement run

Companion to [`javelin-terminal-geometry.md`](javelin-terminal-geometry.md) (the read-only audit,
committed at `68e7c09f`). That document specified three scenarios in its §6; this one reports what
they did. Branch `wt/javelin-probe`, worked against `main` @ `68e7c09f`. Run 2026-08-14.

Every number below is from a `result.missiles.jsonl` produced by the shipped `Missile.cs` with no
weapon, projectile or actor rule altered. §6.4's prohibitions were honoured: `MaximumLaunchSpeed`
and `MinRange` untouched, no elevated or cliff-adjacent target, `t90` used only for §6.2.

---

## 0. Verdict, up front

| scenario | verdict |
|---|---|
| **§6.1 shallow Javelin vs. reversing Humvee** | **survival NOT reproduced** |
| **§6.2 stationary tank, offset tail** | **signature met 35 times, but it does not imply a miss** |
| **§6.3 turn-radius orbit** | **orbit NOT demonstrated** |

**556 shipped-configuration ATGM flights across four scenarios produced zero survivals, zero
usable `flyStraight` latches, zero fuel-outs, and a widest horizontal-facing excursion of 47
facings (66°) against the 128 (180°) a loop requires.** Every single missile terminated at or near
closest approach, within 45 ticks, against a fuel ceiling of 71–74.

**The loop is not in `Missile.cs`.** The audit's §7 named the fallback explicitly and it is now the
live hypothesis: repeat fires from `AttackFrontal`/`Armament` producing a second missile that a
player reads as the first one coming back. Neither this run nor the audit examined the launcher.

---

## 1. What was run

Four scenarios, all under `tools/autotest/scenarios/`, all run with
`run-test.sh --missile-trace --timeout 330`:

| scenario | geometry | shots | verdict file |
|---|---|---|---|
| `test-javelin-reversal-sweep` | §6.1: 8 lanes, Humvee crossing at 4.6–5.7 cells, reversal ordered at 800…2000 wdist in 200-wdist steps, lane 1 an unperturbed control | 171 | `260814_165152_p5893` |
| `test-javelin-stationary-tail` | §6.2: 8 lanes, stationary `t90` at 6.3–7.8 cells, ranges chosen so the 1536 offset-freeze boundary is crossed at eight different phases | 152 | `260814_165703_p6911` |
| `test-javelin-loop-probe` | §6.3: 8 lanes, Humvee driven at speed then **stopped** at 900…2700 wdist, lane 1 an unperturbed control | 149 | `260814_170106_p7613` |
| `test-javelin-latch-control` | RED control: the §6.1 rig moved to ~11.9 cells, the range band where the retained corpus shows latches | 84 | `260814_170718_p8495` |

The perturbation is keyed to the missile's **measured remaining range**, not to a delay after
launch, via two new test-only Lua bindings (`Test.GetLiveMissileRange` /
`Test.GetLiveMissileNearestId`). Those are not interchangeable: the ATGM launches at 100 and
accelerates by 30/tick to 300 while climbing, so ticks-to-intercept is range-dependent, and the
audit's correction-budget arithmetic is written in remaining distance.

## 2. Results

```
scenario              n    latch  fuel_out  aim>298  both>298  maxEndTick  widest turn   launch range
6.1 reversal sweep   171     0        0        44        6         22      47f (66 deg)   3724..5885
6.2 stationary tail  152     0        0        35        0         29      18f (25 deg)   6452..7959
6.3 loop probe       149     0        0        29        2         23      41f (57 deg)   3872..5726
RED control 11.9c     84     0        0        21        1         45      33f (46 deg)  11788..12181
```

- `latch` — records with `flystraight_latches >= 1`
- `aim>298` — `min_aim_dist > 298`, the §6.2 success signature
- `both>298` — missed the tank **and** its own aim point by more than a fuse radius
- `widest turn` — peak-to-trough excursion of the cumulative `hf` series, the §6.3 loop signature

### §6.1 — survival not reproduced

Zero records matched the fingerprint. Not one flight even reached its precondition: no missile
latched `flyStraight`, none ran to fuel-out, and the longest flight was 22 ticks against a ceiling
of 71–74.

The control lane worked and matters: it took **zero** perturbations by construction, and it
produced the same outcome distribution as the seven swept lanes — including a `min_aim_dist` of
1824, higher than five of them. **The wide aim-point misses in this scenario are produced by the
crossing motion itself, not by the reversal.** A positive result in a swept lane would therefore
have needed to beat the control, not merely to exist.

The perturbation was real and measured, not assumed: 69 reversals fired, and the target's speed
across them went **103 → 63 wdist/tick** over the eight ticks following the order. So the lead term
did collapse; it simply did not collapse enough, or early enough, to open the range.

### §6.2 — the signature fires, and it does not mean what it was meant to mean

35 of 152 records had `min_aim_dist > 298`, against a shipped-corpus maximum of 6. Taken at face
value that is a large positive. It is not, and the audit's own §2 says why.

**Every one of those 35 detonated on the target, most for full damage.** Clause 4 tests the offset
aim point; clause 9 tests the true lead point; against a stationary tank the lead term is zero, so
clause 9 collapses onto the tank itself and catches the missile the offset pushed clear of. The
signature detects "the missile failed to arrive where it was steering", which is necessary for
survival and nowhere near sufficient.

The reason the shipped corpus maxes out at 6 while this run reaches 655 is engagement range, not
any behavioural difference: the corpus fired from 10–12 cells, where the missile has ample range to
null a ≤724 offset, and this scenario fired from 6.3–7.8, where it does not.

### §6.3 — orbit not demonstrated

Zero records matched. `flystraight_latches == 0` was satisfied by all 149 — trivially, since
nothing latched anywhere — but the other two clauses failed by a wide margin: the longest flight was
23 ticks against the 71–74 ceiling, and the widest cumulative facing excursion was 41 facings
(57°), less than a third of the 128 the audit requires.

The stop perturbation did what it was supposed to. It fired on a target moving at ≥80 wdist/tick,
so the lead term was large when it collapsed. The missile still arrived and fused.

### The blocker, named

Nine records across the four scenarios cleared **both** spheres the audit's condition (A) names —
`min_dist > 298` and `min_aim_dist > 298`, i.e. they missed the tank by more than a fuse radius and
also missed the point they were steering at. **All nine still detonated, every one of them on
`segment_closest`**, several for four-figure damage.

That is the finding. Clause 9 is centred on `targetPosition + leadTarget` — a **third** point,
distinct from both of the audit's, sitting between them and moving with the target's velocity. A
missile can clear the tank and clear its offset aim point and still be swept up by the lead point.
Condition (A) as the audit states it is therefore not sufficient to reach the latch, and reaching
the latch is what everything downstream — survival and the orbit alike — depends on.

## 3. Falsifiability — what would have made this red

A negative result from a detector that cannot fire is worth nothing, so the detector was run
against a case known to be positive. `tools/autotest/analyze-javelin-probe.py`, pointed at the
retained corpus run `260813_160522_p72370_test-missile-latch-probe`, reports:

```
6.1 survival fingerprint : 1
  [6.1] id=34 min_dist=583 min_aim_dist=665 fs_tick=16 end_tick=74 reason=fuel_out dmg=0
```

That is the audit's §3 record, recovered independently by the scoring script. **The §6.1 detector
demonstrably goes positive when a survival is present in the file**, so the zeros above are
measurements rather than a broken test.

The `flyStraight` latch was likewise shown to be reachable by this rig rather than structurally
excluded: an earlier iteration of the §6.1 sweep (`260814_163731_p4767`, 148 shots) produced one
latch — `id=43`, latched at tick 17 in state `hitting` with `minDistanceToTarget` 312. It fused on
`close_enough` the same tick, so it never flew latched, but the predicate did fire under this rig's
own geometry.

Corpus-wide, across 1580 ATGM records from 21 runs, 16 latched and exactly 2 carry the survival
fingerprint — **both in pre-fix builds** (`260813_150134` and `260813_160522`), whose collapsed
`minDistanceToTarget` the shipped fix at `Missile.cs:858-877` specifically removed. No
shipped-configuration flight in the entire retained corpus has ever survived a miss.

## 4. What I could not determine

- **Whether survival is reachable in shipped code at all.** 556 flights is not a proof of
  impossibility, and the audit's §3.1 tail event — a maximal opposed offset re-roll on the last
  eligible tick — is rare enough that it would not be expected to appear in this many samples. What
  the run establishes is narrower and worth stating exactly: *the two mechanisms the audit proposed
  for forcing it (a reversing Humvee, a stopping Humvee) do not force it.*

- **Whether the audit's §3.2 aim-swing table is reachable by any target.** It is computed from
  `Speed: 150`, and the Humvee's measured cap on clear terrain is **105 wdist/tick** — every swing
  figure in that table is 30% optimistic. Worse, the swing assumes an instantaneous velocity
  reversal, and the measured collapse is 103 → 63 over eight ticks. Whether a genuinely
  instantaneous lead-vector change exists anywhere in the mod, I did not investigate.

- **The launcher.** Not examined, by this run or by the audit. It is now the leading explanation for
  the user's report and the obvious next move.

- **Sub-tick rendering.** Still untested, exactly as the audit's §7 left it. `renderFacing` derives
  from the move vector and the contrail persists five ticks; whether that can *look* like a curve is
  unevaluated, and it is cheap to evaluate.

- **A one-tick sampling lag.** `Test.GetLiveMissileRange` reads the traced missile's most recent
  tick, so a perturbation ordered at a nominal 1500 wdist may have been ordered anywhere in
  1500–1800. That blurs adjacent 200-wdist sweep steps into each other. It does not affect the
  verdicts — no step produced anything — but it would matter to anyone reading the per-lane columns
  as a fine-grained response curve.

- **Engagement range dipped below the specified floor on a handful of shots** (minimum 3724 against
  a 4096 floor) when a replacement Humvee pathed around debris before the sweep cleared it. Fewer
  than ten records; not filtered out.
