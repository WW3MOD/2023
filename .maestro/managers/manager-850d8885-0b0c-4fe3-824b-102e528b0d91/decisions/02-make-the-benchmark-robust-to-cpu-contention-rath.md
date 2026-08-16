# Make the benchmark robust to CPU contention rather than demanding a quiet machine

_Recorded 2026-08-11T18:07:51.441Z by 17dc66e4_

## Context

The first baseline attempt was voided by the wall-clock watchdog killing 8 of 10 matches under load 16.75. The rule adopted at the time was "no manager simulation while any worker runs."

That rule is insufficient, and the reason is visible in a `ps` snapshot taken at load 48.03: the largest consumer was **`Virtualization.framework` at 168%**, with Chrome at 60% — neither of which is agent work. This is the user's own machine and they are using it. A benchmark policy that requires the user to stop working is not a policy anyone will honour, including future sessions that have no way to check.

## Decision

Stop treating a quiet machine as the precondition. Instead make the run **robust to contention** by raising `--max-wall-secs` substantially above its default of 4x `TimeLimitSeconds`.

The justification is that the watchdog measures the wrong thing for this purpose. The simulation is deterministic and seeded (`Test.RandomSeed`, `World.DeriveLocalSeed`), so wall-clock duration has **no effect on the result** — it only decides whether the watchdog kills the match before it can write a verdict. A generous wall limit therefore buys robustness at zero cost to validity. The only real cost is that a genuinely hung match takes longer to be reaped, which is a tolerable trade for not voiding entire batches.

## What still holds from the earlier rule

Manager simulations should still avoid running *concurrently with a worker's build* where that is easy to arrange, because it is free to avoid and it keeps total wall time down. But it is now an optimisation, not a correctness requirement.

## What must NOT be inferred

This does not make contended results comparable to uncontended ones in any timing-sensitive metric. Nothing in the current S1/S2 rungs scores on wall-clock — they score on in-game ticks, army value and income — so this is safe today. **If a future rung ever scores on real time, this decision must be revisited**, and that rung must demand a quiet machine explicitly.

## Follow-up worth doing

The harness could report contention itself: capturing load average per match into the verdict JSON would let a later reader see which runs were taken under pressure, instead of inferring it from a no-verdict count. Filed as an idea, not built.
