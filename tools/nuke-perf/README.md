# nuke-perf — measuring what a nuclear detonation costs the CPU

Two scenarios and one analyser. The scenarios fire a nuclear order at a pinned tick onto a
populated 128×128 map and then do nothing; the analyser reads what the engine wrote.

| | |
|---|---|
| `tools/autotest/scenarios/demo-nuke-perf` | one full six-RV RS-28 Sarmat salvo |
| `tools/autotest/scenarios/demo-nuke-perf-single` | one `NukeSarmatRV`, the denominator |
| `tools/nuke-perf/analyse.py` | reads `perf.log` + the benchmark CSVs, prints the table |
| `tools/nuke-perf/selftest.py` | proves the parse against synthetic logs. No build, no launch |

---

## The one thing to get right

**Neither log this rig depends on is produced by a plain run, and both are destroyed by the
next launch.** A run with the wrong arguments looks exactly like a run in which the
detonation was cheap. `analyse.py` therefore leads with which instrument is missing rather
than with numbers — if it says "no benchmark CSVs found", the run told you nothing and the
slot is spent.

Three ways to get an empty result, all of which have a specific cause:

1. **`Launch.Benchmark` not passed** → no CSVs. Per-tick totals are unavailable.
2. **`Debug.EnableSimulationPerfLogging` not passed** → `perf.log` holds load timings only.
   No attribution: you will not know *which* effect cost the time.
3. **The run did not exit cleanly.** The CSVs are written from `World.Dispose` →
   `Game.FinishBenchmark` (`World.cs:726`), reached only on a normal shutdown. A watchdog
   kill writes nothing, and a killed process does not flush `Log`'s buffered writers either.
   **This is why the rig ends on `Test.Skip` at a pinned tick** rather than on no verdict —
   see the header of `demo-nuke-perf.lua`.

The run takes longer than the harness default allows. **Pass `--timeout 900`**; the default
is 300 s and the whole point of this scenario is ticks that take much longer than 60 ms.

---

## The two profiles, and why there are two

`PerfHistory.Sampling` is assigned inside `RenderTick` (`Game.cs:879`), and `--hidden`
suspends rendering (`Sdl2PlatformWindow.cs:372-373`). That one fact splits the measurement
in half, and it is *not* a defect — it gives two genuinely different and both-useful views.

### Profile A — `--hidden`. **The honest cost. Use this for every before/after pair.**

`RenderTick` never runs, so `PerfHistory.Sampling` stays false, so `TerrainLighting.TintAt`
takes its unsampled path — a 1.1 ns branch instead of a 53.5 ns sampled call
(`TerrainLighting.cs:269-279`). `tick_time` is still recorded, because its `PerfSample`
lives in `InnerLogicTick` (`Game.cs:798`), which always runs.

- ✅ `nukeperf-tick_time.csv` — per-tick simulation+UI cost, with no measurement tax.
- ✅ `perf.log` long ticks — attribution per trait and per effect.
- ❌ `nukeperf-terrain_lighting.csv` — **empty. Expected. Not a fault.**
- ❌ the GPU vertex-row upload — genuinely absent, because nothing is drawn. See *What this
  does not measure*.

### Profile B — `--visible`. **The attribution check. Run it once, not per iteration.**

`PerfHistory.Sampling` is true, so `terrain_lighting` and `render` are populated and you can
see what share of a tick the terrain relight is taking.

**Its `tick_time` is inflated and must not be quoted as a cost.** The sampling wrapper costs
about 15× what it measures on that path, and it is applied per `TintAt` call — which is the
single hottest call in the detonation window. Profile B answers *"is it the lighting?"*;
Profile A answers *"how much did that cost, and did the fix help?"*

---

## Running it

From the repository root. One scenario per launch; these are heavy.

```sh
# wipe the previous run's logs first -- every one of these files is overwritten per launch,
# and Log.AddChannel falls through to perf.log.1 / .2 when a file is still locked by a
# living instance (Log.cs:145-160), which is how a stale log gets read as a fresh result
rm -f "$APPDATA/OpenRA/Logs/"nukeperf-*.csv "$APPDATA/OpenRA/Logs/"perf.log* \
      "$APPDATA/OpenRA/Logs/"lua.log*

AUTOTEST_EXTRA_ARGS="Launch.Benchmark=nukeperf- Debug.EnableSimulationPerfLogging=true Debug.LongTickThresholdMs=1" \
  ./tools/autotest/run-test.sh --hidden --timeout 900 demo-nuke-perf

python tools/nuke-perf/analyse.py summary
```

Expect `AUTOTEST_VERDICT outcome=SKIP exit=2`. **`SKIP` is the success case here** — it is
the rig saying "I finished, the numbers are on disk". `TIMEOUT-FAIL` means the watchdog fired
and there are no CSVs.

To archive a run so it can be compared later:

```sh
mkdir -p /tmp/nukeperf/before
cp "$APPDATA/OpenRA/Logs/"nukeperf-*.csv "$APPDATA/OpenRA/Logs/"perf.log* \
   "$APPDATA/OpenRA/Logs/"lua.log* /tmp/nukeperf/before/

python tools/nuke-perf/analyse.py compare --before /tmp/nukeperf/before --after /tmp/nukeperf/after
```

The single-warhead arm is the same command with `demo-nuke-perf-single`.

### Where the settings come from

`Settings.cs:409-412` walks every settings section and loads any field named as
`Section.Field` on the command line, so `Debug.EnableSimulationPerfLogging=true` and
`Debug.LongTickThresholdMs=1` are launch arguments, not `settings.yaml` edits.
`Launch.Benchmark` is the same mechanism on `LaunchArguments`
(`LaunchArguments.cs:38-40`), read in `BlankLoadScreen.StartGame`.
`run-test.sh` passes `AUTOTEST_EXTRA_ARGS` straight through to the launcher (`:794`).

**Why launch arguments rather than `settings.yaml`:** the settings file is shared with the
user's real game and with every other scenario run on this machine. A perf flag left in it
silently taxes everything afterwards — `EnableSimulationPerfLogging` puts three
`GC.CollectionCount` calls and a `Stopwatch.GetTimestamp` around every trait tick
(`PerfTickLogger.cs:36-50`).

### Choosing `LongTickThresholdMs`

`1` is the recommended floor. The `ms` column is an **integer** format
(`PerfTimer.cs:26`), so sub-millisecond items print as `0 ms` and a threshold of `0`
produces an enormous log of rows reading zero. Against a 60 ms tick budget, a 1 ms item is
1.7% — fine as a floor. Raise it to `5` if the log is unwieldy; you will lose the long tail.

---

## Reading the output

- **`tick_time` detonation p50 vs baseline p50** is the headline: what a tick costs while the
  detonation is live against what it costs when the map is quiet. The baseline window is
  ticks 10–55 (before the order); the detonation window is the first impact to the end of
  the run.
- **The detonation window deliberately runs to the end of the run, not to the last impact.**
  `ShockwaveEffect` sweeps for ~400 ticks after its own impact, far outliving the flash.
  Clipping at the last impact measures the fireball and misses the sweep.
- **`max`** is the single worst tick. A tick over ~60 ms is one the simulation could not
  deliver on time, which is what a player feels as a freeze.
- **Long-tick attribution** names the offending trait or effect per tick via `DoTimed`
  (`WorldUtils.cs:107-120`) and `ApplyToAllTimed` (`TraitDictionary.cs:305-316`). `Effect:`
  rows come from `World.cs:510`, `Trait:` rows from `World.cs:508`.
- **The `GC` column** counts long ticks during which a garbage collection fired. A long tick
  attributed to trivial code is usually a GC pause charged to whatever was running — so a
  fix that removes allocations can show up as *other* things getting faster.
- **"attributed ms summed per tick" is a LOWER BOUND.** Anything under the threshold is
  invisible to it. Do not subtract it from `tick_time` and call the remainder anything.

---

## What this does not measure

- **The GPU upload.** `TerrainSpriteLayer.UpdateTint` marks a vertex row dirty; the row is
  re-uploaded at draw time. Under `--hidden` nothing is drawn, so that cost is absent from
  Profile A entirely. A change that reduces the number of *notified cells* also reduces the
  number of dirty rows, so Profile A **under-states** such a fix rather than over-stating it.
- **Frame rate.** This rig measures simulation ticks. It says nothing about whether the
  detonation looks smooth.
- **Anything about a real match.** There are no bots, no production, no combat: the 448
  Russian actors sit still and the single USA actor is a Supply Route 54 cells away. That is
  deliberate — it makes the detonation the only thing in the window — but it means the
  absolute numbers are a floor for what a live game would pay, not an estimate of it.
- **Variance.** One run is one run, on a machine that may be building something else at the
  time. Two runs of the same build that disagree by more than a few percent mean the machine
  was busy, not that the code changed. Prefer comparing p50 over comparing max.
