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

## Runs 2-4 — seeds 1017, 4241, -7723

Pending.

## Runs 5-6 — RED pair

Not started. Require the one-line `engine/OpenRA.Game/OverkillClaim.cs:52` edit (delete the leading
`Release();` from `Claim()`) plus `make all`, and **must be taken in an isolated worktree** — a
deliberately-broken build sitting in the shared checkout while another session runs against it is
its own hazard. Run 6 is the highest-information launch in the set and decides §3.3; abort E fires
if `test-aa-battery-volleys` also reds under the same edit, which flips the answer to retiring the
pump with no guard.
