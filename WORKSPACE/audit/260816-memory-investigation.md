# Memory investigation — where memory is retained that should not be

**Date:** 2026-08-16
**Ref:** `main @ d5b52893` (clean; one untracked syncreport log)
**Method:** read-only. No game launched, no autotest, batch or tournament run. Live
process/disk state observed via `Get-Process` / `Get-CimInstance` / `du` only.
**Vanilla vs mod line:** the vendoring squash is `7362fbc6` ("Starting point"). Every
finding below was classified with `git log --oneline 7362fbc6..HEAD -- <file>`; a file with
no commits in that range is untouched upstream and is called out as such.

---

## Executive answer

The user's symptom — *the whole machine* degrades until reboot, and `git log` takes
minutes — is **mostly not the game process.** Three things are true at once and they are
being read as one bug:

1. **The machine is small and already full before the game starts.** 15.6 GB total,
   **4.4 GB free with no game running**, and **disk 94% full (29 GB free of 453 GB).**
2. **Build-server residue is ~1.4 GB of idle processes** that survive every build and are
   never reaped (§ N1). This is the single biggest *recoverable* chunk on the machine.
3. **The game itself is BOUNDED-BUT-LARGE, not leaking** in released builds — but it is
   large in a way that will hurt strangers on 8 GB machines (§ B1), and it briefly
   **doubles** that cost on every map change (§ B2).

`git log` taking minutes is the tell that this is not a heap leak. Memory pressure slows
things uniformly; a warm-cache `git log` is nearly pure page-cache reads and should be
instant even under heavy RAM use. Minutes-long `git log` is **I/O contention** — Defender
real-time scanning (`RealTimeProtectionEnabled: True`, exclusions not queryable without
admin; `MsMpEng` was the single largest process on the box at 792 MB) walking build output
and `.git` objects, on a disk with 6% headroom.

**The one unbounded-forever leak found (§ L1) is TestMode-gated and cannot reach a
released build.** It is real, it is a one-line fix, and it does inflate the user's own
autotest sessions — but it is not what a stranger will hit.

---

## LEAK — grows forever

### L1. [LEAK] `UnitLifecycleLogger` pins an entire `World` per map load via a static event

**Mechanism.** `engine/OpenRA.Mods.Common/Traits/World/UnitLifecycleLogger.cs:174` does
`Game.OnQuit += Flush;` inside `IWorldLoaded.WorldLoaded`. `Flush` is an *instance* method
(`:414`), so the delegate captures `this` → `World world` (`:119`) → the whole actor graph.
The only `Game.OnQuit -= Flush` lives **inside `Flush` itself** (`:420`), which runs only at
process quit. `Game.OnQuit` is static (`Game.cs:729`), so it survives every map load.

`tracks` (`:127`) is a `Dictionary<uint, UnitTrack>` and `UnitTrack.Actor` (`:110`) is a
**strong `Actor` reference**. It is correctly pruned on `ActorRemoved` (`:338`) *during* a
match, but world teardown does not fire `ActorRemoved` for survivors — so every unit alive
at match end is retained too.

**Magnitude.** One full retained `World` per map load in the process — map, ShadowLayer
(§ B1, 130–185 MB by itself), pathfinder graph, live actors. Conservatively **150–400 MB
per match**; four matches in one launch ≈ 0.6–1.6 GB never collected.

**Bound on severity — important.** Gated at `:144` on
`TestMode.IsActive && !string.IsNullOrEmpty(TestMode.UnitLifecycleLogPath)`, and
`--lifecycle` is **opt-in** in `run-test.sh` (`:181`). **It cannot fire in a released
build.** It bites only opt-in behaviour-lint runs, and only if one process loads >1 map.

**Also, unrelated to memory:** `writer.Flush()` at `:221` runs **unconditionally every
tick** whether or not a line was written — ~30,000 flush syscalls per 20-minute game, on a
94%-full disk.

**Confidence:** 95% on the mechanism (three independent passes agreed, code re-read
directly). ~5% that it contributes to the user's *machine-wide* symptom, because of the gate.
**WW3MOD-authored** (`25ab82d7`, `d39441a0`, `f7193cac`).

**Fix shape.** Unsubscribe on world disposal — implement `INotifyActorDisposing`/world-end
and call `Game.OnQuit -= Flush` there; or make the handler static over a weak reference.
Guard the per-tick `Flush()` behind "did we write anything this tick". **Risk: very low**,
test-path only, no gameplay effect.

### L2. [LEAK] `MissileTrace.WeaponNames` retains a per-map `Ruleset` forever

`engine/OpenRA.Mods.Common/Projectiles/MissileTrace.cs:172` — `static Dictionary<WeaponInfo,
string>`, never cleared. `WeaponInfo` instances are per-`Ruleset`, i.e. per map, so each map
load pins that map's weapon objects for process lifetime. `Completed` (`:170`) is likewise
never cleared between maps, though it *is* capped: `const int MaxRecords = 50000` (`:152`),
enforced at `:292`/`:383` with overflow counted in `droppedRecords`. Records hold `uint
ActorId` and strings — **deliberately no `Actor` references**, which is good design.

**Magnitude.** Hundreds of `WeaponInfo` per map; ceiling on `Completed` ≈ 35 MB. Gated off
by default (`Missile.cs:360` checks `MissileTrace.Enabled`). **Unbounded across map loads
but small.** Confidence 85%. **WW3MOD-authored** (`2b7a75a9`, `394473ed`).
**Fix:** clear both statics on world load. Risk: none.

### L3. [LEAK] `TestModeScreenshots.captured` — static list, no eviction, O(n²) disk writes

`engine/OpenRA.Game/TestModeScreenshots.cs:36` — `static readonly List<Entry> captured`, no
trim/cap. Entries are metadata only (strings, int, DateTime, ~120 B) — **no bitmaps** — so
500 captures ≈ 60 KB of RAM. The real cost is `SaveManifest()` (`:89`) **rewriting the whole
manifest after every capture**: 500 captures = 125,000 entry serializations and 500 full-file
writes. TestMode-gated. **Unbounded but negligible in RAM.** Confidence 95%.
**WW3MOD-authored** (`8d16288a`). **Fix:** append-only manifest. Risk: none.

### L4. [LEAK — latent, currently dead] `BotBlackboard` can never evict an `InProgress` task

`BotBlackboard.cs:115` — the stale-task predicate excludes
`Status != BotTaskStatus.InProgress`, so an `InProgress` task lives forever regardless of
age. **Currently harmless: `PostTask` has zero callers**, so `tasks` is always empty. Worth
recording so it is not wired up in this state. Confidence 99%. **WW3MOD-authored.**

---

## BOUNDED-BUT-LARGE — the release risk

### B1. [BOUNDED-BUT-LARGE] `Map.ShadowLayer` allocates a full-map array *per cell* — ~130–185 MB, ~4× larger than the data it holds

**This is the largest single number in the game process and the finding most likely to hurt
a stranger.**

`engine/OpenRA.Game/Map/Map.cs:253` declares
`CellLayer<CellLayer<(byte GroundShadow, byte AirborneShadow)>> ShadowLayer` — a cell layer
whose *value* is another cell layer. The load loop at `:483-485` is the problem:

```
foreach (var fromUV in AllCells.MapCoords)
{
    ShadowLayer[fromUV] = new CellLayer<(byte GroundShadow, byte AirborneShadow)>(this);
    foreach (var toUV in FindTilesInAnnulus(fromUV.ToCPos(this), 2, 32, true))
        ...
}
```

Every cell gets an inner layer, and `CellLayerBase` (`CellLayerBase.cs:34`) **always**
allocates `Entries = new T[size.Width * size.Height]` — the **full map**, not the annulus.
Only the radius-2–32 annulus is ever written. `SetShadowLayer()` (`:1005-1010`, the
no-`shadows.bin` path) has the same shape.

**Magnitude — measured, not estimated.** `river-zeta-ww3` `MapSize: 98,82` = **8,036 cells**.
Inner array = 8,036 × 2 B = **16,072 B**, plus `CellLayer` object + array header ≈ 16,144 B.
Times 8,036 inner layers = **~130 MB**.

| Map | Size | Cells | ShadowLayer in RAM | `shadows.bin` on disk |
|---|---|---|---|---|
| river-zeta-ww3 | 98×82 | 8,036 | **~130 MB** | 36.9 MB |
| woodland-warfare-ww3 | 98×98 | 9,604 | **~185 MB** | 44 MB |
| nuclear-winter-ww3 | 102×72 | 7,344 | **~108 MB** | *none — computed at load* |

The on-disk file stores only the annulus, which is why RAM is **~3.5–4× the file**. That
ratio *is* the waste.

**Second-order hazard.** 16,072 B is just *under* the 85,000-byte LOH threshold, so all
~8,000 of these medium arrays land on the Small Object Heap and get promoted to gen2 —
severe gen2 fragmentation, and the `GCLargeObjectHeapCompactionMode` set at `Game.cs:227`
does nothing for them. On a 15.6 GB machine already at 4.4 GB free, this is what makes the
process feel like it never gives memory back.

**Classification:** bounded per session, dies with the `Map`. **Not a leak** — but it is
~100 MB of pure overhead per map, and on a stranger's 8 GB machine it is the difference
between playing and swapping.

**Confidence: 95%** — allocation shape read directly at `Map.cs:483-485` and
`CellLayerBase.cs:34`, map size and file size confirmed on disk.
**WW3MOD-authored** (`80f7b5af`, "Shroud edges fixed… Airborne shadows"). Upstream OpenRA
has no `ShadowLayer`.

**Fix shape.** Make the inner representation sparse — the annulus is ~3,200 cells of a
~8,000-cell map, so a flat `byte[cells × annulusLen]` with an offset table, or a single
jagged array sized to the annulus, cuts 130–185 MB to ~35–45 MB *and* removes ~8,000 gen2
objects. **Risk: medium.** It touches the shadow read/write format, so `shadows.bin` would
need a version bump or regeneration for all maps, and rendering must be verified per
[`DOCS/recipes/SCREENSHOT.md`](../../DOCS/recipes/SCREENSHOT.md). Worth scheduling before
release, not hot-fixing.

### B2. [BOUNDED-BUT-LARGE] Map change holds two `World`s — and two ShadowLayers — at once

`engine/OpenRA.Game/Game.cs:186-195`:

```
worldRenderer?.Dispose();           // old renderer goes
...
OrderManager.World = new World(mapUID, ModData, OrderManager, type);   // old World still referenced here
```

`OrderManager.World` still points at the **old** `World` for the entire duration of the new
`World` constructor — which loads the new `Map` and its full `ShadowLayer`. So peak
residency on a map change is **2 × B1 = 260–370 MB of shadow data alone**, plus two full
actor graphs.

**Magnitude:** a transient spike of ~300–500 MB on every map change. Bounded, recovered
after GC. **Confidence 90%** (read directly). The `worldRenderer?.Dispose()` line is
upstream; the *cost* of the spike is WW3MOD's, because the ShadowLayer is what makes a
`World` expensive.

**Fix shape.** Null `OrderManager.World` (or move the old reference to a local and drop it)
before constructing the new one, so the old `Map` is collectible during the load.
**Risk: medium** — needs care that nothing reads `OrderManager.World` during load.

### B3. [BOUNDED-BUT-LARGE] `LobbyPresetLogic` retains exactly one stale `World`

`engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyPresetLogic.cs:128,129,134` assign
`SnapshotLastGame`, `ApplyLastGame`, `EnqueueBotFaction` (statics at `:35/:40/:44`) to
closures over `this`. The class holds `readonly OrderManager orderManager` and has **no
`Dispose` override at all**; `OrderManager.World` reaches the whole actor graph.

Assignment is `=`, not `+=`, so each new lobby **replaces** the previous instance —
**exactly one** stale World is retained, from lobby close until the next lobby opens. Not
cumulative. Confidence 90%. **WW3MOD-authored** (`766bd3cb`, `260a6e97`).

**Note the in-file PITFALL comment** anticipating a `List<Action>` if multiple bars ever
exist — *that* variant would be genuinely unbounded. Flag for whoever touches it.

**Fix:** null the three statics in a `Dispose`. Risk: low.

---

## NOT-THE-GAME — what is actually on the user's machine

### N1. [NOT-THE-GAME] ~1.4 GB of idle .NET build-server processes, never reaped

Observed live, **with no build running**:

| Process | Count | Each | Total |
|---|---|---|---|
| `MSBuild.dll /nodemode:1 /nodeReuse:true` | 7 | ~101–110 MB | **~756 MB** |
| `VBCSCompiler.dll` (Roslyn compiler server) | 1 | **653 MB** | **653 MB** |

**~1.4 GB, 9% of the machine, sitting idle.** `nodeReuse:true` is the .NET SDK default: every
`dotnet build` leaves worker nodes alive for ~15 min (and they can be re-spawned faster than
they retire when builds are frequent). All 7 nodes started within the same second, i.e. one
build fanned out to 7 and none have exited. `VBCSCompiler` at 653 MB was the **largest
non-Defender process on the box**.

**Confidence: 99%** — directly observed, command lines captured. **Not WW3MOD's code**, but
it *is* WW3MOD's build workflow that produces it.

**Fix shape.** Set `MSBUILDDISABLENODEREUSE=1` in the environment, or pass
`-nodeReuse:false` from `make.ps1`/`ww3-dev.ps1`; `dotnet build-server shutdown` reclaims it
on demand. **Risk: none** (marginally slower incremental builds). This is the cheapest
~1.4 GB the user can get back and requires no code change.

### N2. [NOT-THE-GAME] Disk at 94% + Defender real-time scanning ⇒ the `git log` symptom

`C: 453G total, 425G used, 29G avail (94%)`. `RealTimeProtectionEnabled: True`; exclusions
could not be enumerated without admin, so **assume the repo and build output are being
scanned**. `MsMpEng` (Defender) was the largest process on the machine at **792 MB**.

This, not the heap, is the best explanation for **`git log` taking minutes while two
build/test workers were active**: thousands of small `.git` object reads and multi-GB build
output writes, each intercepted by real-time scanning, on a volume with 6% headroom (which
also constrains pagefile growth exactly when RAM pressure peaks).

**Confidence: 80%** that this dominates the *machine-wide* slowdown.
**Fix shape.** Add Defender exclusions for the repo, `engine/bin/`, `%APPDATA%\OpenRA`, and
the `dotnet`/`MSBuild` processes; free disk. **Risk: none to the codebase** — a machine
config change, and the user's call to make.

### N3. [NOT-THE-GAME] 547 MB of replays, plus concurrent agent processes

`%APPDATA%\OpenRA\Replays` = **547 MB** (of 572 MB total support dir), never pruned — disk
only, no RAM cost, but it compounds N2 on a 94%-full disk. Separately, 5–6 concurrent
`claude` agent processes at ~330–350 MB each ≈ **1.9 GB** were resident during the
investigation; that is this workflow, not the mod.

**Logs are fine and should be ruled out:** `%APPDATA%\OpenRA\Logs` is **3 MB across 42
files** — rotation works.

---

## Harness verdict — ruled OUT early, as instructed

The brief flagged that "the harness spawns processes that never exit" would look exactly
like a memory leak and is far cheaper to fix. **It does not.** `tools/autotest/run-test.sh`
is disciplined:

- `TIMEOUT_SECS=300` **by default** (`:150`), not opt-in — a hung game is killed at 5 min.
- `kill_game()` (`:322-334`) translates to the Windows PID and uses `taskkill //PID //T //F`
  to reap the **whole tree**, with a POSIX fallback.
- `trap emit_verdict EXIT` (`:251`) plus `trap ... INT TERM` (`:710`) means no exit path
  skips cleanup — Ctrl-C and terminal close both reap.
- A PID-file lock (`:433-465`) detects and clears stale locks from previously killed runs.
- Per-run artifacts go to a dedicated `RUN_DIR`; `tournament-results/` is 83 MB total.

No orphaned OpenRA/game processes were present. **The only process residue on the machine is
the .NET build servers (§ N1), which the harness does not own.**

---

## Checked and cleared (so these can be ruled out without re-work)

**Influence/belief stack — clean.** `DangerFieldLayer` (`:637-638`, `ActiveCells`/`ActiveSet`
cleared every recompute; `fields` keyed by Player, ≤8). `SightingThreatLayer` (`:162,:183`,
same clear-per-pass). `ThreatMapManager` (`:45-51`, fixed `float[,]`/`int[,]`, `Array.Clear`ed).
`PoiMap` (`:173,:205`, `candidates` cleared per pass). `InfluenceMap` (`:58`,
`Dictionary<Player,int[,]>`, ≤8).

**`BeliefStore` — clean, and notably immune to the decoration problem.**
`Dictionary<uint, BeliefContact>` with removal at `:93/:112/:268` (verified-clear +
confidence decay, `removalScratch.Clear()` at `:100`). Only **enemy-owned** actors with
`HealthInfo` are inserted (`:212-217`) — river-zeta's 4,544 actors are all `Neutral`, so
**no decoration ever enters**.

**The 3,187 field decorations — cleared.** They enter only `ActorMap`, `ScreenMap` and
`Map.DensityLayer` (baked once at load), all of which have removal paths. They register
into **no** WW3MOD layer or bot cache. This was the brief's item 3 and it is a non-issue.

**Bot modules — disciplined, and this surprised all three passes.** All **45**
`Dictionary<Actor,…>` / `HashSet<Actor>` / `List<Actor>` fields across `BotModules/` have a
matching `Remove`/`RemoveWhere`/`Clear`: PoiOffensive (`stagedCells`, `standoffSince`,
`lastCohesion`, `lastFiresAnchor`, `bombardAssigned`, `evacuatingOutOfAmmo`), SupplyFollower
(nine dicts), Helicopter (eight), MountedTransport, Garrison, LaneAmbush, LayeredDefence,
CaptureCoordinator, Scout, Harvester. `SquadManagerBotModule.cs:244-249` `CleanSquads()`
runs `Squads.RemoveAll(s => !s.IsValid)` plus per-squad unit pruning every update.
`BotBlackboard.unitClaims` (`:84`) removes at `:131`/`:217`; `OrderArbitrationMath.standing`
(`:397`) at `:510`/`:665`; `EngineerRouteOpenBotModule` at `:319-320`;
`StarvingRecruitGate.held` at `:98`. `ModularBot.gateTargets` cleared per order.

**Bot telemetry — bounded, arithmetic done.** `UnitTypeTelemetry` (`PlayerStatistics.cs:203`)
is keyed by **actor type name**, not per event — bounded by ruleset (~200 types × ~40 B) =
**<10 KB per player forever**. `Cache<string, ArmyUnit>` (`:66`) likewise type-keyed, holds
an `Animation` per type but **no Actors**. `ArmySamples`/`IncomeSamples` (`:39,:43`) append
once per **30 s of game time** (`:90`) = **40 ints per player per 20-min game**;
`earnedSeconds` is an explicit 60-entry ring (`Dequeue` at `:113`).
`BotVsBotMatchWatcher.IncomeSamples` = 24 B × 1 per player per 25 ticks ≈ **58 KB** for a
20-min 2-bot game. `CaptureCoordinator.everOwnedStructures` is add-only but bounded by map
structure count (dozens × 4 B).

**Bot logging — cleared.** All 104 `Log.Write` sites in `BotModules/` are event-gated
(capture decided, transport assigned); **none per-tick**. `Log`'s unbounded channel drains
at 1 ms and keeps up.

**No static `World`/`Actor` anywhere — the jackpot pattern is absent.** The only statics of
those types are `Game.ModData` and `Game.worldRenderer`, both single-slot and replaced in
`StartGame` (`Game.cs:189`). WW3MOD's ~20k new trait/bot lines use OpenRA's `INotify*` trait
dispatch rather than C# events, so the whole event-leak class is structurally near-empty:
§ L1 and § B3 are the *only* two places mod code touches a process-lifetime delegate.
**No `manager.Register(this)` static-registry pattern exists in the mod at all.**

**Upstream infrastructure — cleared.** `Mediator`/`Ui.Subscribe` (`Widget.cs:164-171,:322`)
paired with `Ui.Unsubscribe` + `Dispose` in `Widget.Removed()` (`:585`). `ChromeProvider`
statics (`:56-60`) nulled by `Deinitialize()` (`Game.cs:1091`). `TextNotificationsManager`
chat history cleared per game (`Game.cs:92`). `TerrainSpriteLayer` has a matching `-=` in
`Dispose`; its `ConditionalWeakTable<World, …>` and `PathSearch.LayerPoolTable` are
**weak-keyed on World** — correct. `ScreenMap.partitionedMouseActorBounds` removes at `:245`.
`LobbyLogic` unsubscribes all six `Game.*` handlers in `Dispose`. `SequenceProvider`/
`SpriteCache` are `IDisposable`, owned by `Map`/`ModData`, not static.
`UnitLifecycleLogger.tracks` itself removes at `:338` — the leak is the subscription, not
the dictionary.

---

## NEEDS A RUN — nothing below was executed

The findings above are all static. These are the measurements that would confirm or refute
them, with the exact command and what each proves. **None should be run without a
deliberately scheduled slot** — simulation authority is the manager's.

**R1 — the decisive one. Does RSS return to baseline after a match?**
This separates "leak" from "large" and is the single measurement worth buying.

```powershell
# Baseline at main menu, then after each of 3 consecutive skirmishes on river-zeta,
# returning to the menu between each. Sample every 10s:
while ($true) {
  Get-Process OpenRA* | Select-Object @{n='t';e={Get-Date -f HH:mm:ss}},
    @{n='WS_MB';e={[math]::Round($_.WorkingSet64/1MB,0)}},
    @{n='Priv_MB';e={[math]::Round($_.PrivateMemorySize64/1MB,0)}}
  Start-Sleep 10
}
```

- **Flat sawtooth returning to baseline** ⇒ no leak in released builds; § B1 is the whole
  story and the fix is the sparse ShadowLayer.
- **Staircase, +150–400 MB per match that never comes back** ⇒ something pins the World in a
  non-TestMode path that this audit missed, and § L1's shape exists elsewhere. Escalate.

**R2 — confirm § B1's 130 MB directly.** Take a heap snapshot at main menu vs in-match:

```powershell
dotnet-counters monitor -p <pid> System.Runtime
# or: dotnet-gcdump collect -p <pid>   then inspect CellLayer<ValueTuple<Byte,Byte>> count
```
Expect **~8,036 live `CellLayer<(byte,byte)>` instances** on river-zeta and ~130 MB in their
arrays. Confirms the sparse-representation fix is worth its medium risk.

**R3 — confirm § L1 only under `--lifecycle`.** Two `run-test.sh` runs, one with and one
without `--lifecycle`, comparing peak RSS. Proves the gate holds and keeps the fix
correctly scoped to the test path. *(Requires goahead: two test runs.)*

**R4 — free 1.4 GB with no code change, no game run.** `dotnet build-server shutdown`, then
re-measure `Get-Process dotnet`. Confirms § N1 instantly and reversibly.

---

## Ranked recommendation

| # | Finding | Class | Size | Confidence | Risk to fix |
|---|---|---|---|---|---|
| 1 | § N1 build-server residue | NOT-THE-GAME | ~1.4 GB | 99% | none |
| 2 | § N2 disk 94% + Defender | NOT-THE-GAME | machine-wide | 80% | none (config) |
| 3 | § B1 ShadowLayer 4× overhead | BOUNDED-LARGE | 130–185 MB/map | 95% | medium |
| 4 | § B2 double World on map change | BOUNDED-LARGE | +300–500 MB peak | 90% | medium |
| 5 | § L1 UnitLifecycleLogger | LEAK (gated) | 150–400 MB/match | 95% mech. | very low |
| 6 | § B3 LobbyPresetLogic | BOUNDED-LARGE | 1 stale World | 90% | low |
| 7 | § L2/L3/L4 | LEAK (small) | <35 MB | 85–99% | none |

**For the user's machine today:** #1 and #2 — both free, neither touches the codebase.
**For the public release:** #3 and #4 — a stranger on 8 GB will feel the ~130 MB of shadow
overhead and the ~300–500 MB map-change spike, and that is the defect they would blame the
mod for. #5 should be fixed because it is one line, not because it ships.
