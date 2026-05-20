# auto/bugs-finish — autoburn 260521

## Summary

**Test runs GREEN.** The autotest scaffold from `auto/bugs-survey` (`test-dr-jams-drone/`) was carried over,
the engine built, and the test executed twice — **both runs PASS**. No engine fix shipped,
because the symptom the test was written to repro does not occur in the test scenario.

**Implication:** The RELEASE_V1 Phase B entry "Drone autotarget of other drones broken" may already be
resolved by commit `c0895592` (2026-05-06, "Setup/aim phase + DR auto-fire stabilization"), which
added `Armament.RequiresForceFire` to the DroneTargeter and `NoSelfDefenseInterrupt` to the
DroneJammer. The narrow autotest cannot prove the bug is gone in all situations — the user should
playtest before crossing the entry off the tracker.

## Test runs

Run 1 (260521_004255):
```
[TestMode] active — name=test-dr-jams-drone result=/Users/fredrik/.ww3mod-tests/result.json
[2026-05-21T00:43:46] Game started.
[TestMode] result written: pass
==> Result: {"name":"test-dr-jams-drone","status":"pass","notes":"","timestamp":"2026-05-20T22:44:17.3805320Z"}
```

Run 2 (260521_004614):
```
[TestMode] active — name=test-dr-jams-drone result=/Users/fredrik/.ww3mod-tests/result.json
[2026-05-21T00:46:40] Game started.
[TestMode] result written: pass
==> Result: {"name":"test-dr-jams-drone","status":"pass","notes":"","timestamp":"2026-05-20T22:47:03.9758400Z"}
```

Two-of-two PASS. Verdict is stable for this scenario.

## What the test actually proves

- USA DR (Drone operator), `Stance: FireAtWill`, idle at (12,17).
- Russian DR at (35,17) force-fires DroneTargeter at (22,17) — spawns a Russian-owned `quadcopterdrone`
  that flies toward the midpoint, passing well inside the USA DR's DroneJammer range (20 cells).
- Within 15 s the USA DR's AutoTarget picks up the enemy drone, fires the DroneJammer (secondary
  armament, `ValidTargets: Drone`, 3 dmg/shot, BurstWait 1), and drops the drone's HP below
  starting HP — Pass condition met.

Coverage limits — the test does NOT exercise:
- DR with its own deployed slave drone currently in the air (CarrierMaster + CarrierSlave state).
- Multiple competing targets (enemy infantry/vehicles in autotarget range alongside a drone).
- Moving DR (only the stationary case).
- DR engaged in another action when the drone enters range.
- Drones at non-default cruise altitude / approach angles.

Any of those could still reproduce the original bug.

## Hypothesis

Most likely, commit `c0895592` ("Setup/aim phase + DR auto-fire stabilization", 2026-05-06)
closed the autotarget path that was failing:

- `Armament.RequiresForceFire: True` was added to **DroneTargeter** (in `infantry.yaml`). Before
  the fix, the DroneTargeter weapon had `ValidTargets: Ground, Water` — `Ground` is shared with
  any ground actor's TargetTypes (Infantry/Vehicle), so the DR's *primary* armament would
  preferentially autotarget enemy infantry/vehicles and deploy a recon drone at them. The
  *secondary* DroneJammer never got a turn because the primary kept claiming the autotarget slot.
  With the primary now flagged `RequiresForceFire`, AutoTarget routes through the DroneJammer
  alone whenever a drone-typed target is in range.
- `NoSelfDefenseInterrupt: True` added to **DroneJammer** is orthogonal to the autotarget path
  (it gates SmartMoveActivity's stop-and-fire-on-fresh-enemy interrupt), but it confirms the
  DR/drone interaction was being actively triaged on May 6.

The enforcement points (`AttackBase.cs:416, 431`) make this airtight: `RequiresForceFire` on
the primary armament means AutoTarget won't even consider it for non-force orders, leaving the
DroneJammer as the only candidate when a Drone-typed actor enters range.

## Proposed next steps (for the user)

1. **Playtest** a real match where DRs are in active use against enemy drone scouts. Watch for
   the original symptom (DR stands idle while enemy drones loiter overhead).
2. If the bug doesn't reproduce in a real game, **mark RELEASE_V1 Phase B "Drone autotarget of
   other drones broken" as resolved** with a reference to `c0895592` + `test-dr-jams-drone`.
3. If it DOES reproduce, capture the exact circumstances (DR state, drone state, other units
   nearby) and we extend the test scenario to cover them — current scaffold is a stationary
   1v1 with no distractions, so it's plausibly under-stressed.

I am NOT touching `RELEASE_V1.md` myself — the test is too narrow to claim the bug is dead.

## Fix shipped

None. The test passes without any engine change on this branch.

## Files touched

```
tools/autotest/scenarios/test-dr-jams-drone/  (6 files carried over from auto/bugs-survey)
WORKSPACE/autoburn/bugs-finish.md             (this report)
```

## Commits on this branch

- `36ad6b79` carry over test-dr-jams-drone scaffold from auto/bugs-survey
- (this commit) autoburn: bugs-finish report — test GREEN, no fix needed
