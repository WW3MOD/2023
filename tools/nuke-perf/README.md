# nuke-perf — measuring what a nuclear detonation costs the CPU

Three scenarios and one analyser. The first two fire a nuclear order at a pinned tick onto a
populated but INERT 128×128 map and then do nothing; the analyser reads what the engine wrote.
The third fires into a live bot-vs-bot match and reports its own readings — see
[The third scenario](#the-third-scenario-a-nuke-inside-a-real-match).

| | |
|---|---|
| `tools/autotest/scenarios/demo-nuke-perf` | one full six-RV RS-28 Sarmat salvo |
| `tools/autotest/scenarios/demo-nuke-perf-single` | one `NukeSarmatRV`, the denominator |
| `tools/nuke-perf/analyse.py` | reads `perf.log` + the benchmark CSVs, prints the table |
| `tools/nuke-perf/selftest.py` | proves the parse against synthetic logs. No build, no launch |
| `tools/autotest/scenarios/demo-nuke-perf-populated` | the same question inside a REAL two-bot match — see below for THE POPULATED-MAP READING |
| `tools/nuke-perf/drive-populated.lua` | runs that scenario's Lua offline against stubbed bindings. No build, no launch |

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
3. **The server refused the client at join.** Since 2026-09-20 the runner catches this and
   reports `outcome=LAUNCH-FAIL exit=3` within a second or two, echoing the server's own
   exception. If you see it, the answer is in `server.log` — the two most likely causes are
   two traits registering the same `ILobbyOptions` id, and a trait declared under the wrong
   system actor (which *adds* a second instance rather than overriding the first). Before
   that fail-fast existed this cost a full watchdog timeout and reported as `TIMEOUT-FAIL`,
   i.e. as a hang. See `WORKSPACE/DISCOVERIES.md`, 2026-09-20.
4. **The run did not exit cleanly.** The CSVs are written from `World.Dispose` →
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
      "$APPDATA/OpenRA/Logs/"lua.log* "$APPDATA/OpenRA/Logs/"server.log* \
      "$APPDATA/OpenRA/Logs/"client.log* "$APPDATA/OpenRA/Logs/"debug.log*

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

---

## The two payload arms of `demo-nuke-perf`

`demo-nuke-perf` fires the same six RVs either way; the arm decides what each RV *is*.

| arm | payload | what differs |
|---|---|---|
| `salvo` (default) | `NukeSarmatRV` | the shipped warhead: thermal, ten fire rings, EMP, five suppression rings, fireball light re-tinting the terrain every **5** ticks |
| `exchange` | `NukeSarmatRVExchange` | the final-exchange variant: those seventeen warheads gone, `TerrainRefreshInterval` **16**. Flash, shake, fireball, light radii/curve/duration, Vaporize radius, blast wave, scars and scorched trees all unchanged |

### Run this pair WITHOUT `Launch.Benchmark`

**This is the one measurement in this rig that must drop the benchmark CSVs**, and it is not a
preference. `Launch.Benchmark` sets `PerfHistory.Sampling` (`Game.cs:879`), and since lever 6
that flag forces the terrain relight down its **serial** path — so a benchmarked run measures a
build nobody ships and understates what the parallel sweep already bought. The general warning
at the top of this file ("`Launch.Benchmark` not passed → the run told you nothing") is about
the per-tick CSVs; for THIS pair the instrument is `perf.log`'s long-tick attributions, which
`Debug.EnableSimulationPerfLogging` alone produces.

```sh
# arm=salvo
AUTOTEST_EXTRA_ARGS="Debug.EnableSimulationPerfLogging=true Debug.LongTickThresholdMs=1"   ./tools/autotest/run-test.sh --hidden --timeout 900 demo-nuke-perf

# arm=exchange -- identical but for the last argument
AUTOTEST_EXTRA_ARGS="Debug.EnableSimulationPerfLogging=true Debug.LongTickThresholdMs=1 Test.ForceEscalationVariant=true"   ./tools/autotest/run-test.sh --hidden --timeout 900 demo-nuke-perf
```

`analyse.py` reads the arm out of `lua.log`, which is written either way, so `summary` still
prints `arm:` and `compare` still refuses to let a mismatched pair pass unremarked. Expect the
`!! no benchmark CSVs found` line: under this profile it is correct, not a failed run. The
`long-tick attributions` table is the whole reading, and it now carries **hits, total and max**
side by side for exactly this comparison.

**One scenario, one schedule, one binary.** The arm is a launch argument, not a second
scenario and not a rules override, so both arms run the same map, the same 665 actors, the
same aim points, the same order tick and — this is the part that makes the pair subtractable
— the same impact ticks. `analyse.py` prints `arm:` in every report and flags a `compare`
whose two sides disagree, because the arms are otherwise indistinguishable in the logs and a
pair that accidentally compared `exchange` against `exchange` would report a delta of zero.

**What this arm does NOT prove.** In a real match the variant is selected by
`DoomsdayStrike.IsExchangeLaunch` — is this launch on the final-exchange cascade. The rig
cannot use that path: a running exchange reserves a cascade slot for every warhead, which
moves the impact ticks onto a shared sequence and destroys the schedule the two arms must
share. `Test.ForceEscalationVariant` therefore forces the **actor choice alone**
(`MissileStrikePower.cs`, the `missileActor` local) and leaves the cascade untouched. The
cascade wiring is pinned by `ExchangeVariantTest`, not by this rig.

### What to expect from the pair

The dropped warheads are **under 1% of the salvo's detonation cost** — they are dropped
because nothing survives a cascade to be burned or suppressed, not for speed. The number
that moves is the relight **cadence**: at interval 5 a 199-tick light refreshes the terrain
~40 times, at 16 about 13. So on an A/B of `salvo` → `exchange`:

- `Trait LightEventManager` **hits** should fall by roughly 3× (16/5);
- its **max ms** should *not* move — the cost of one refresh is unchanged, only how often
  one happens, and the worst single refresh is still a full-map sweep;
- its **total ms** should fall with the hits;
- `Effect ThermalRadiationEffect` should disappear entirely.

Hits down with max flat is the signature to look for, and the two halves check each other:
hits alone could be a run that fired fewer warheads, and max alone says nothing about
cadence. A run where `max ms` fell too is measuring something else — most likely background
load on the machine, which moves these numbers by 3× on its own
(`WORKSPACE/DISCOVERIES.md`, 2026-09-20). Take the pair back to back.

There is no `tick_time` row under this profile: that series comes from the benchmark CSVs,
which this pair deliberately does not produce.

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

**CORRECTED 2026-09-20 — a launch argument does NOT leave `settings.yaml` alone, and this
section used to imply it did.** `Settings.cs:409-412` loads every `Section.Field` override
into the live section objects, and `Game.Settings.Save()` at
`engine/OpenRA.Game/Network/UnitOrders.cs:266` — the `HandshakeRequest` handler, which runs
on *every local client join* — writes every section straight back out, overrides included.
So `Debug.EnableSimulationPerfLogging=true` on the command line is **persisted**, and every
launch afterwards on that machine, including the user's real game, carries the tax until
somebody notices. This was observed rather than deduced: `EnableSimulationPerfLogging: True`
on line 28 of `%APPDATA%\OpenRA\settings.yaml`, with the file's mtime at the exact exit
second of a perf run.

`run-test.sh` snapshots `settings.yaml` before the launch and restores it afterwards, and
since 2026-09-20 that restore runs from the **EXIT trap** rather than from the happy path,
so Ctrl-C, `LAUNCH-FAIL`, `HARNESS-ERROR` and a `set -e` abort all restore it too. The
argument for using launch arguments over editing the file by hand still stands — the runner
undoes them for you — but if you ever launch the engine directly with these arguments,
outside the runner, **you will have to reset the flag yourself.**

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

## The third scenario: a nuke inside a real match

`tools/autotest/scenarios/demo-nuke-perf-populated` answers the question the two rigs above
deliberately do not: **what does a tick cost when a salvo lands in a populated late game.**
Two `@experimental` bots play a real Skirmish match on the shipped Polar Disorder map with
production, movement, combat and fog all running; Russia fires one Sarmat salvo at tick 8000
and a back-to-back pair once the target army has rebuilt; `tick_time` p50/p90/p99/max is
recorded for nine windows either side of each. Asked for by
`WORKSPACE/audit/260921-release-readiness.md` P1/P3 and Part 3 row 11, which quote *What this
does not measure* below back at this file.

```sh
./tools/autotest/run-test.sh --hidden --speed 4 --timeout 2700 demo-nuke-perf-populated
grep -F 'NUKEPOP window' "$APPDATA/OpenRA/Logs/lua.log"
grep -F 'NUKEPOP detonation' "$APPDATA/OpenRA/Logs/lua.log"   # where the windows were keyed
```

**THE SALVO IS FOUR WARHEADS ON THAT MAP, NOT SIX**, and the windows are keyed on an
**observed** arrival, not on a computed one. Both of those are corrections; the run that
forced them is below.

**IT TAKES NO `AUTOTEST_EXTRA_ARGS`, AND `Launch.Benchmark` MUST NOT BE ADDED.** Same reason
the salvo/exchange pair above drops it, applied to the headline number rather than to the
attribution: `Launch.Benchmark` sets `PerfHistory.Sampling`, which forces the terrain relight
down its serial path, so a benchmarked `tick_time` is a measurement of a build nobody ships.
That scenario reads the same `PerfHistory` item the CSV is written from via
`Test.GetTickTimeMs()` — no flag, no sample — so its numbers are taken against the shipped
configuration. `analyse.py` does not read it and is not meant to: there are no CSVs.

**Its numbers are NOT comparable to this rig's in absolute terms** — different map, 96x96
against 126x126, fog on rather than off, bots rather than statues. Compare the *shape* of the
before/during/after delta, not the milliseconds. This rig stays the A/B instrument, because
its aim point, actor count and impact ticks are pinned and that one's ground zero follows a
moving army.

Its Lua can be run to completion **without a launch slot**: `lua tools/nuke-perf/drive-populated.lua`
stubs the engine bindings and exercises the windows, the statistics and the validity checks.
Two real bugs were caught there before the first run was ever requested — and one was not,
which is the more useful half of the story: the driver was synthesising its fake detonations
at the same derived tick the scenario was looking for them at, so a stub that agreed with the
code's assumption could not test the assumption. `SALVO_AT=` now sets the arrival offset
independently, and `SALVO_AT=158` reproduces the geometry the first draft assumed.

### THE POPULATED-MAP READING

Run `260923_090904_p22486_demo-nuke-perf-populated`, taken at **`5062a755`**, `--hidden --speed 4`,
finished at tick 12140 in ~6 min, seed `-1373942252`. Verdict line: *"valid yes — three salvos into
a populated match, every window keyed on an observed detonation."* This supersedes the provisional
table that stood here: the earlier run (`260923_084012`) had every window mis-keyed by 60 ticks and
its numbers should not be quoted.

| window | census at open (USA + RUS attackers) | p50 | p90 | p99 | max | over 60 ms |
|---|---|---|---|---|---|---|
| `build` 300–7699 | 7 + 6, rising to 74 + 62 | 11 | 24 | 34 | 76.9 | 3 |
| `prefire1` 7700–7999 | **74 + 62** — a live battle, nothing incoming | **30** | 38 | 44 | 59.9 | **0** |
| `flight1` 8000–8104 | 77 + 61 | 29 | 38 | 46 | 96.3 | 2 |
| `deton1` **8105**–8505 | **76 + 61** → closes on **2 + 11** | 8 | 23 | 38 | **125.5** | **3** |
| `recover1` 8506–8805 | 2 + 11 | 2 | 5 | 10 | 13.8 | 0 |
| `prefire2` 11000–11299 | 42 + 53 | 13 | 17 | 22 | 30.1 | 0 |
| `flight2` 11300–11404 | 50 + 52 | 15 | 20 | 27 | 35.1 | 0 |
| `pair` **11405**–11835 | **52 + 53** → closes on **6 + 16** | 10 | 46 | 62 | **81.1** | **6** |
| `recover2` 11836–12135 | 6 + 16 | 4 | 7 | 10 | 14.5 | 0 |

**The audit's question (`260921-release-readiness.md` P1/P3, Part 3 row 11 — "we learn whether a
nuke in a real late game stutters") is answered, and the answer is: a hitch, not a stall.**

- **One four-warhead 750 kt salvo into ~137 ground attackers costs one ~125 ms tick** — a little
  over 2× the 60 ms budget, i.e. roughly two frames of a 16.67 tps simulation — **plus two more
  ticks over budget in the same 401-tick window.** Three over-budget ticks in total.
- **Then the tick cost FALLS, hard and for the obvious reason.** `deton1`'s p50 is **8 ms** against
  `prefire1`'s **30 ms**, and `recover1` settles at 2 ms. That is not the detonation being cheap:
  the salvo took the map from 137 ground attackers to 13, and the simulation it was paying for went
  with them. **A cheap-looking window after a nuke is the strongest evidence the nuke worked.**
- **Two salvos back to back — 8 warheads into ~105 attackers — cost max 81.1 ms and six
  over-budget ticks.** Twice the warheads did NOT cost twice the worst tick: the single salvo's
  125.5 ms is the higher peak, on a fuller map. Cost tracks the actors under the blast, not the
  warhead count. The pair's p90 (46 ms) and p99 (62 ms) are the run's worst sustained stretch, so
  the pair spreads its cost where the single salvo spikes.
- **The live-battle baseline is itself the expensive thing.** `prefire1` — no warhead anywhere,
  just two bots fighting with 137 ground attackers — runs p50 30 ms, p99 44 ms, max 59.9 ms. It has
  **zero** over-budget ticks, but its max sits 0.1 ms under the budget. The `build` window has 3
  over-budget ticks of its own with no nuke in the run at all. **Over-budget ticks are not unique
  to nuclear detonations on this map**, and a reading that ignored the baseline would have credited
  all of them to the weapon.

#### Measured incidentally, and worth having

- **Order-to-impact is `order + 105`, not `order + 98`.** `derived_minus_observed` was **−7 on all
  three shots** (−6 on shot 3, which is ordered one tick later and shares the pair's arrival). The
  98 is the arc alone (`standoff / Speed`, `Acceleration: 0`); the extra **7 ticks is order
  latency** — `Test.ActivateSupportPower` issues an `Order` and the power activates when the order
  is processed, not on the tick the Lua call is made. Stable across three independent shots 3300
  ticks apart, so it is a fixed cost of the order path rather than load-dependent.
- **Salvo span is 37 ticks, not the 36 that `(4-1) * AimPointInterval` predicts** — identical on
  both groups. One tick, consistently, in the same place as the latency above.
- **The gated-impact cross-check is not merely weak on this map, it is systematically NEGATIVE for
  a salvo that works**, and the run shows why. `combat_per_tick` is estimated over the flight
  window — 4.09/tick with both armies in contact — and the salvo then *removes the combatants
  generating it*, so the post-impact stretch carries far fewer impacts than the baseline predicts
  (`rise=-225.3` for group 1, on a salvo whose 4 warheads demonstrably arrived). It is printed on
  the `NUKEPOP detonation` line and is **not** an invalidation, which is the whole point of the
  2026-09-23 rework. Do not reach for it as a landing check; the arrival counter is the landing
  check.

#### What one run on one host can and cannot say

- **One seed, one map, one machine.** Seed `-1373942252`, Polar Disorder (96×96 bounds, 4-warhead
  package), this developer host. Nothing here is a distribution. A second seed would move the army
  sizes and therefore every number in the table.
- **`--speed 4`, and the host was saturated.** The run managed ~35 ticks/s against the 66.7 that
  `--speed 4` nominally asks for, so the engine was running flat out the whole time. `--speed` does
  not change the *work* inside a tick, but it does change everything around it — cache residency,
  thermal state, how much real time passes between ticks. **The direction of that bias is not
  established**, and nothing here has been compared against a real-time run.
- **`--hidden`, so no rendering.** The GPU upload of the fireball lighting is absent from every
  number above. See *What this does not measure* below.
- **The ms figures are per-tick simulation cost, not frame time.** "Two frames" above is arithmetic
  against the 60 ms timestep, not an observation of a dropped frame.
- **Not comparable to this rig's numbers in absolute terms.** Different map, fog on rather than
  off, bots rather than statues. Compare shapes.

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
- **Variance, and it is far bigger than "a few percent" — this bullet understated it until
  it was measured.** Background load on this machine moves `tick_time` by about **3x**.
  Measured 2026-09-20 between the two arms of one rig, same map, same 665 static actors,
  **nothing detonating in either** — a YAML lint started in between:

  ```
  salvo arm     tick_time p50  8.0 ms   p95 18 ms
  single arm    tick_time p50 23.1 ms   p95 56 ms
  ```

  Same code, same scenario, no detonation: **2.9x in p50 and 3.1x in p95, entirely from
  contention.** So an absolute per-tick timing from this machine is comparable ONLY inside a
  back-to-back pair taken while no build, lint or merge gate is running, and **a before/after
  pair split across a build is not evidence of anything.**

  **ATTRIBUTIONS SURVIVE THE NOISE AND ABSOLUTE TIMINGS DO NOT** — which trait or effect
  dominates, and in what ratio to the others *in the same run*, holds up because every item in
  a run is taxed by the same contention. Prefer them, and prefer p50 over max. (Worth knowing
  what that buys you: on a QUIET map with nothing fired, the largest single trait in one such
  reading was `DangerFieldLayer` — 40 hits, 1281 ms total, 85 ms max, i.e. past the 60 ms
  budget in a single tick on an idle map. That is a lead, not a mechanism, and it is recorded
  here only as an example of a finding the attribution column can carry and the absolute
  column cannot.)
