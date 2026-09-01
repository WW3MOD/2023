# AA overkill bound — run results

Against the pre-registered rule in `260901-aa-overkill-bound-protocol.md` §6. **Nothing in the
protocol's rule may be revised after seeing these numbers**; if the data does not fit, the answer is
one of its aborts, not a widened constant. Recorded as each run lands so a replaced session does not
have to re-take a slot.

Per run: `rMax = max(R)`, `rMin = min(R)`, `margin = observerFire - rMax`,
allowance = `rMax + max((rMax - rMin) * 3, 32)`.

## Run 1 — seed `-2058490156`, GREEN baseline, unmodified code

```
run:    260901_233200_p31636_test-aa-overkill-pump
status: skip (exit 2) — the declared-skip status this scenario is expected to report
notes:  LANE_R firedOf4=4 ticks[41,46,39,49] || LANE_S pumps615 pumpWindow5-600
        observerFire48 suppressedThroughPumpN pumperFire-1
```

| quantity | value |
|---|---|
| `firedOf4` | 4 |
| `rMin` / `rMax` | 39 / 49 (spread 10) |
| `observerFire` | 48 |
| allowance | 49 + max(30, 32) = **81** |
| `margin` | **-1** |
| `suppressedThroughPump` | N |
| `pumperFire` | -1 (never fired) |

Conditions 1, 2 and 4 all hold on this run. Condition 3 needs all four runs.

**The number that matters is 48.** The protocol chose this seed first precisely because the
2026-08-10 measurement recorded in the scenario header at `:35` was **818** at this same seed, and
§6-A pre-committed that a repeat of 818 falsifies H1. 818 -> 48, same seed, same scenario, only the
code differing, is the `AUTOTEST.md:320-326` control: **`27d25f1c` is live here and the pump's
suppression regime is gone.** That is positive proof rather than an absence of evidence.

Note `margin` is negative — the observer fires *before* the control lane's slowest shooter. The
pre-registered formula accommodates this; it is not an anomaly, it is what "no suppression at all"
looks like when the observer is not waiting on anything.

## Runs 2-4 — seeds 1017, 4241, -7723, GREEN baseline, unmodified code

```
run 2  seed 1017   LANE_R firedOf4=4 ticks[45,35,45,40] || observerFire40 suppressedThroughPumpN pumperFire-1
run 3  seed 4241   LANE_R firedOf4=4 ticks[46,47,49,49] || observerFire35 suppressedThroughPumpN pumperFire-1
run 4  seed -7723  LANE_R firedOf4=4 ticks[46,39,40,45] || observerFire40 suppressedThroughPumpN pumperFire-1
```

All four runs reported `skip` (exit 2), which is this scenario's declared-skip status and the
expected baseline outcome — not a failure to run. `pumps615 pumpWindow5-600` on every run.

## All four GREEN runs against §6's pre-registered conditions

| seed | firedOf4 | lane-R ticks | rMin/rMax | spread | obs | allowance | margin |
|---|---|---|---|---|---|---|---|
| -2058490156 | 4 | 41,46,39,49 | 39/49 | 10 | 48 | 81 | **-1** |
| 1017 | 4 | 45,35,45,40 | 35/45 | 10 | 40 | 77 | **-5** |
| 4241 | 4 | 46,47,49,49 | 46/49 | 3 | 35 | 81 | **-14** |
| -7723 | 4 | 46,39,40,45 | 39/46 | 7 | 40 | 78 | **-6** |

1. `suppressedThroughPump == N` on all four. **HOLDS** — abort A does not fire.
2. `obs <= rMax + max((rMax-rMin)*3, 32)` on all four: 48<=81, 40<=77, 35<=81, 40<=78. **HOLDS**,
   with the pre-registered constants unmodified. Abort D does not fire.
3. `max(margin) - min(margin) = -1 - (-14) = 13 <= 32`. **HOLDS** — abort B does not fire.
4. `firedOf4 == 4` on all four and lane-R spreads 10/10/3/7, all `<= 32`. **HOLDS** — abort C does
   not fire.

**All four conditions hold, so §6 authorises the guard** — subject to run 6, since abort E can fire
even when 1-4 all pass and concerns the guard's *value* rather than its *measurability*.

Every margin is negative: the observer fires BEFORE the control lane's slowest shooter, on every
seed. That is what "no suppression at all" looks like when the observer is not waiting on anything,
and it is the same fact the 818 -> 48 collapse reports from a different direction.

## Runs 5-6 — RED pair

Not started. Require the one-line `engine/OpenRA.Game/OverkillClaim.cs:52` edit (delete the leading
`Release();` from `Claim()`) plus `make all`, and **must be taken in an isolated worktree** — a
deliberately-broken build sitting in the shared checkout while another session runs against it is
its own hazard. Run 6 is the highest-information launch in the set and decides §3.3; abort E fires
if `test-aa-battery-volleys` also reds under the same edit, which flips the answer to retiring the
pump with no guard.
