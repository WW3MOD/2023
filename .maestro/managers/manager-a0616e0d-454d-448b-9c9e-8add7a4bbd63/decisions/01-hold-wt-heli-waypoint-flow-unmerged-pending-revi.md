# Hold wt/heli-waypoint-flow unmerged pending review and an in-game run

_Recorded 2026-09-03T07:59:26.428Z by e0a0826c_

## Context

`wt/heli-waypoint-flow` @ `a0bdd339` implements the user's early-waypoint-release / corner-arcing request. Build clean, 2288/2288 NUnit green, and the report is unusually good: it found the prior implementation (`02006314`, 2026-03-18) and the stated reason it was reverted, and explains why the new design is not vulnerable to that reason.

Every other branch today was merged on green.

## Options

**A. Merge on green like the others.** Consistent with the day's pipeline; the tests pass and the guards read carefully.

**B. Hold, review adversarially, merge only after the scenario has been run and the tuning calibrated.**

## Chose B

Three properties this branch has that the others did not:

1. **Blast radius.** Helicopter movement is shared by both bot profiles. Transit times drop and squad arrival timings shift on `@stable` as well as `@experimental` — the author says so and flags that the benchmark baseline needs re-taking. `@stable` is the benchmark CONTROL; changing it is allowed under settled policy but is not something to do silently on a green test run.
2. **The core value is unfalsified geometry.** The author is explicit: the model is cross-checked against the shipped controller (predicts braking at 2.93 cells; `02006314`'s author observed ~2.7) but nothing validates that a 4.1-cell lead on a right-angle corner LOOKS right. Unit tests pin the math, not the feel.
3. **The failure mode is expensive to the user.** The user tests from `main` and asked for `main` to always be stable. A helicopter that abandons a waypoint it should have stopped on costs them a play session, and this is precisely the defect class the reverted version shipped.

Reinforcing precedent from earlier today: the alt-attackmove fix had correct math and a wrong caller, which no unit test could see. This branch has the same shape — `ReleaseDistance` is pure and well-tested, and the risk sits entirely in the call-site guards deciding whether a waypoint is terminal.

## Consequence

Reviewer `wt/heli-review` dispatched read-only. Merge is gated on: reviewer verdict clean or FIX items landed, AND `test-heli-corner-flow` run green with the release distance calibrated by eye. The scenario run is queued behind the user's machine being free — their game is currently open, and a launch steals window focus.
