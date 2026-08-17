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
  _(Resolved before the run rather than caveated: the per-item counters were moved OUT of the
  timed region into a separate untimed counting pass that runs only on flush frames, leaving the
  timed span byte-identical to the unprobed body. So the measured mean is an estimate of
  unprobed cost, not an upper bound.)_

---

# VERDICTS — measured 2026-08-17

One run: `run-test.sh --hidden --sync-reports --timeout 600 test-savegame-resume-riverzeta`,
verdict `PASS`, 1000 timed `SyncHash()` calls. Run dir
`260817_144511_p32548_test-savegame-resume-riverzeta`, `result.synccost.txt`.

**Four of five predictions were WRONG, and the user's hypothesis was RIGHT.** This is the
entry's reason for existing, so it is recorded plainly rather than softened.

| # | Predicted | Measured | Verdict |
|---|---|---|---|
| C1 | mean 50-250 us | **4300 us** | **REFUTED** — ~20x under |
| C2 | < 0.25% of a core; hypothesis refuted | **2.4% of a core**; hypothesis **SUPPORTED** | **REFUTED** |
| C3 | report >= 5x the hash | report **2.4x** the hash (10430 us) | **REFUTED** — expensive, but the ratio was wrong |
| C4 | hundreds-to-low-thousands synced traits, below actor count | **57,000** synced traits vs **4,738** actors | **REFUTED** — 12x MORE than actors, not fewer |
| C5 | run reaches tick 3000 and completes | `PASS` | **CONFIRMED** |

**Why it was wrong, since the error compounded.** Per-item cost was ~76 ns per synced trait,
not the 5-20 ns predicted (~5x), and the instance count was ~57,000, not ~1,200 (~50x). The
static model got both factors wrong in the same direction. No amount of reading would have
caught this; only the run did.

**A separate hypothesis, also refuted.** I suspected much of the 4.3 ms was wasted on traits
that are `ISync` only by inheriting `ConditionalTrait` while declaring no `[Sync]` members
(their hash function returns a constant 0). Measured by reflection over the mod assembly:
**319 `ISync` types, 319 with at least one `[Sync]` member, 0 empty.** There is no free
optimisation there; the cost is genuine work and cannot be reduced without reducing coverage.

**Derived per-second cost** at the design cadence of 5.56 net frames/s (`NetFrameInterval = 3`
at `Timestep: 60`):

| Layer | Per net frame | Per second | Share of one core |
|---|---|---|---|
| Hash only (every game, no toggle exists) | 4.30 ms | 23.9 ms | **2.4%** |
| Sync report only (armed in 2-human games by default) | 10.43 ms | 58.0 ms | **5.8%** |
| Both (what a 2-human WW3MOD game actually pays) | 14.73 ms | 81.9 ms | **8.2%** |

Maxima were 41.7 ms (hash) and 172 ms (report) — single frames, consistent with GC pauses on
the report's per-trait boxing, and the likelier cause of felt stutter than the mean.

**Caveats on these numbers, stated because they bound what they can be used for.**

- Measured on THIS machine. The per-trait figures (76 ns hash, ~185 ns report) are the part
  that transfers; the percentages do not.
- The timed region was byte-identical to the unprobed body (counting was a separate untimed
  pass), so these are estimates of unprobed cost rather than upper bounds inflated by probing.
- The observed net-frame rate in this run was ~2.9/s, roughly half the 5.56/s design cadence —
  River Zeta with 4,738 actors and two bots does not sustain `Timestep: 60` on this machine at
  all. The per-second figures above therefore use the DESIGN cadence, which is the higher
  frequency and so the less favourable assumption for the "it's cheap" conclusion.
- `--sync-reports` forced the report path on. That is not the default for a bot game, so the
  2.4% hash figure is what a single-player or autotest run pays; the 8.2% applies to 2-human
  games, where `ServerSettings.EnableSyncReports = true` arms reports automatically.
