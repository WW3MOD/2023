# Sync CPU cost — predictions registered BEFORE the measuring run

Registered 2026-08-17 on `wt/sync-audit @ 473928a2` (branched from `main @ d8727f63`).

Written after the static read but **before** any measuring run, so a wrong prediction is
reportable as a finding rather than quietly absorbed. The user's request framed the CPU
cost as an explicit hypothesis ("syncing is CPU intense work I think"), so it deserves a
pre-registered number rather than a post-hoc verdict.

## The model being predicted from

`World.SyncHash()` (`World.cs:545`) is two loops: one over every actor doing a field read
and two multiplies (`Sync.HashActor`), and one over every synced trait instance invoking an
IL-emitted delegate that does N field loads and XORs. The delegate is generated ONCE per
type and cached (`Sync.cs:30-31`, `ConcurrentCache`), so per-frame cost is compiled code,
**not** reflection. Expected per-item cost is therefore single-digit-to-low-tens of
nanoseconds.

Cadence is the other half: `ProcessOrders` is gated by `NetFrameInterval = 3`
(`Session.cs:221`) and WW3MOD's default speed is `Timestep: 60` = 16.7 ticks/s
(`mod.yaml:381-383`), so the hash runs **~5.6x/second**, not per rendered frame.

## Predictions

| # | Prediction | Confidence |
|---|---|---|
| C1 | Mean `World.SyncHash()` cost is **50-250 us** per call on River Zeta | 70% |
| C2 | That is **< 0.25% of one core** at the real 5.6 calls/s cadence, i.e. the "CPU intense" hypothesis is **refuted** for the hashing path | 80% |
| C3 | The **sync report** costs at least **5x** more per call than the hash, because `DumpSyncTrait` boxes every non-bool member, allocates a `Values` array past 4 members, and takes `lock (TypeInfoCache)` once per trait per net frame | 75% |
| C4 | Synced trait instances number in the **hundreds to low thousands**, well below the ~4,663 total actors, because most River Zeta actors are terrain props with no `ISync` trait | 70% |
| C5 | The run reaches tick 3000 and completes, given `--timeout 600` | 65% |

## What would falsify C1/C2

A mean above **~2 ms** per call. That is >10x my per-item cost model and would mean the
hashing path really is worth a toggle — at 5.6 calls/s it would be ~1.1% of a core and
scaling with unit count, which on a slower machine is a real cost. If that happens the
honest outcome is to BUILD the disable option, not to argue the number down.

## Predicate audit — what else could satisfy "it's fine"

"The frame rate was fine" and "the test passed" are both satisfied by a machine that is
merely fast enough, and neither says anything about what `SyncHash()` costs. They are
therefore **not** accepted as evidence here. The measurement must be absolute microseconds
per call **against the actor and synced-trait counts that produced it**, because only the
per-trait figure generalises to the slower machine the user is actually asking about — and
that machine is not this one.

Two known ways this run could mislead:

- **Accelerated speed would inflate calls/second.** If `--speed` is used, calls/s is NOT the
  real cadence; the per-second figure must still be computed from 5.6/s. Elevated load can
  only inflate per-call cost, which biases toward SUPPORTING the hypothesis, so a "cheap"
  result measured under load is robust while an "expensive" one would need re-checking at
  default speed.
- **The probe's own overhead.** Two `Stopwatch.GetTimestamp()` calls (~20-25 ns each) plus two
  counter increments per hash call. Against a predicted 50-250 us that is under 0.1%, but it
  is inside the measurement, not outside it.
