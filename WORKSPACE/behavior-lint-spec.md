# Behavior-lint pipeline — spec

> Goal: make the AI's "strange unit behavior" (units idling in corners, transports
> parked at drop points, units never re-tasked) **detectable from sim logs
> automatically**, instead of requiring a human to play and watch.
>
> Two pieces: **(1) `UnitLifecycleLogger`** — an off-by-default engine trait that
> emits a per-unit JSONL event stream during a test/tournament run; **(2)
> `tools/behavior-lint/`** — a Python analyzer that reads that stream and prints
> WARN lines for anti-patterns, with per-`ActorID` drill-down.
>
> Research/spec only — **nothing here is implemented.** Grounded against
> `main @ 45210768`. Every referenced class/line was read on this pass.

---

## Part 1 — Inventory (what exists today)

### 1.1 Per-unit-type telemetry (commit 9392540c, verdict_version 7)

- **Module:** `UnitTypeTelemetry` — `engine/OpenRA.Mods.Common/Traits/Player/PlayerStatistics.cs:203-252`
  (per-type tally struct `UnitTypeTally` at `:183-191`), exposed as the field
  `PlayerStatistics.UnitTypeStats` (`:70`).
- **How it's fed:** the per-actor trait `UpdatesPlayerStatistics` (same file) calls into it on
  engine lifecycle callbacks — `Produced()` from `INotifyCreated.Created` (`:354`), `Lost()` +
  `RemoveFromAlive()` from `INotifyKilled.Killed` (`:314-315`), owner-change transfer
  (`:369-398`), non-combat dispose (`:400-417`). It mirrors the existing ArmyValue/DeathsCost
  accounting and is explicitly **observer-only** (no `[Sync]`, no RNG, no orders — comment `:193-202`).
- **Granularity:** **per-player × per-actor-type AGGREGATE counts** at end of match —
  `produced_count/cost`, `lost_count/cost`, `alive_count/value`. **No per-unit identity, no
  positions, no timestamps, no order data.** Deterministic key-sorted emission (`Sorted()` `:250`).
- **Where it lands:** serialized by `BotVsBotMatchWatcher.SerializeVerdict` into the `unit_types`
  block (`:586-611`, guarded by `verdict_version:7` at `:516`), embedded in the verdict JSON, which
  `TestMode.WriteResult` writes into the `notes` string of `~/.ww3mod-tests/result.json`
  (`TestMode.cs:131-177`; `run-test.sh:292-293`).
- **Reader:** `tools/autotest/parse-composition.py` — unwraps `notes`, aggregates `unit_types`
  per `bot_type` across a `match_*.json` batch.

**Takeaway:** this tells us *what* each side built/lost in aggregate. It has **zero** per-unit or
behavioral (idle/order/position) resolution — exactly the gap this pipeline fills.

### 1.2 What the autotest harness logs today

- `run-test.sh` produces **one** verdict file `~/.ww3mod-tests/result.json` (`:292-293`), schema
  `{name,status,notes,timestamp,seed,screenshots[]}` (`TestMode.WriteResult`, `TestMode.cs:131-185`).
  Batch runs archive a per-run copy at `${SCREENSHOT_DIR}/result.json` (`run-test.sh:558`);
  `RESULT_DIR=~/.ww3mod-tests`, `SCREENSHOT_DIR=~/.ww3mod-tests/screenshots/<RUN_ID>` (`:290-301`).
- The tournament watcher also writes a **human-readable diagnostic** `*.watcher.log` next to the
  result (`BotVsBotMatchWatcher.cs:146-159`): SR discovery + per-5s score lines. Not per-unit.
- Engine `debug.log` (located by `run-test.sh:219-232`) — general engine log; scanned only for
  "Failed to load rules" on timeout (`:498-509`).
- **No per-unit and no per-order data exists anywhere today.** The richest per-entity streams are
  the v6 POI `capture_events` and `income_samples` (`BotVsBotMatchWatcher.cs:636-646` / `:613-629`) —
  both keyed by **POI actor / player**, never by combat-unit `ActorID`.

### 1.3 Bot order issuance — logging & the single choke point

- **No order logging in BotModules today.** `bot.QueueOrder(...)` call sites (e.g.
  `UnitBuilderBotModule.cs:225/:310`, `BaseBuilderQueueManager.cs:120/:160`, and the squad-state
  files) issue orders with no telemetry.
- **THE funnel:** `ModularBot.QueueOrder(Order)` — `engine/OpenRA.Mods.Common/Traits/Player/ModularBot.cs:81-84`.
  **Every** bot-issued order for a `ModularBot` player passes through here (enqueue), then is dequeued
  and world-issued in `ModularBot.Tick` (`:101-112`, `world.IssueOrder(order)` at `:111`).
- **Issuing-module identity is recoverable at one spot with no per-call-site edits.** The tick loop
  runs each module's `BotTick(this)` at `:95-97`; the attack-response loop at `:124-126`. Wrapping
  those two loops so a `currentModuleTag = t.GetType().Name` field is set immediately before each
  module runs, and reading that field inside `QueueOrder`, tags **every** order with the module that
  produced it — a **single-file change to `ModularBot.cs`**. (Orders queued outside a module tick —
  none exist today — would tag as `""`.)
- Aside: `PlayerStatistics.ResolveOrder` (`:122-128`) increments a global `OrderCount` for all
  resolved orders (skipping `Dev*`), but carries no per-unit/module/target detail.

### 1.4 Off-by-default logging gates — the established patterns

- **Launch-arg via `TestMode`** (the harness precedent): `BotVsBotMatchWatcher` no-ops unless
  `TestMode.IsActive && !string.IsNullOrEmpty(TestMode.TournamentConfigPath)` (`:139`). `TestMode`
  parses `Test.*` args in `Initialize` (`TestMode.cs:90-129`); adding a new `Test.UnitLifecycleLog`
  arg is a 2-line addition there. **Inert in normal play** because `TestMode.IsActive` is false
  without `Test.Mode=true`.
- **`Game.Settings.Debug.*` booleans** (`Settings.cs:161 BotDebug`, `:179 SyncCheckBotModuleCode`) —
  the other gating style, but persisted in settings.yaml (worse for per-run control).

**Chosen gate:** launch arg `Test.UnitLifecycleLog` (details in §2a) — matches the harness, per-run,
zero footprint when absent.

### 1.5 Territory / danger classification seams (for "is this position enemy-controlled?")

- **Fog-legal per-player fields** (`ControlField`, world trait, `ControlField.cs:240/:297`):
  `OwnerAt(player, gx, gy)` → `ControlOwner {Own,Enemy,Contested}` (`:624`, enum `:48`);
  `ScoreAt` (`:617`); CPos→grid via `MapCellToGridCell(CPos)` (`:600`); `HasField(player)` (`:614`).
  `DangerFieldLayer.GroundDanger(player,cell)` / `AirDanger(...)` (`:543/:550`).
  **Caveat (load-bearing):** these are built **only for `InfluenceStack.Participates` players** =
  `@experimental` bots + human combatants (`influence-stack.md:17`, `InfluenceStack.cs:38-48`).
  Tournament bots are usually `@stable`/normal → **no ControlField** → `OwnerAt` returns the
  no-field default (`Contested`). So the belief fields are **not** a reliable territory source for
  arbitrary bots.
- **Omniscient seam, valid at LOG time** (the logger is diagnostics, not simulation — omniscience is
  fine and does not touch determinism): `InfluenceMap.GetEnemyInfluence(perspective)` →`int[,]`
  (`InfluenceMap.cs:156`), `GetFriendlyInfluence` (`:143`), `GetFrontline` (`:170`). These scan
  `world.Actors` with no fog check (`influence-stack.md:88`) and exist for the whole world, every
  profile. **This is the territory classifier the logger should use** (see §2a "terr" field).
- **Global spawn/despawn hooks:** `World.ActorAdded` / `World.ActorRemoved` events
  (`World.cs:436-437`, fired at `:389/:399`). Idle test: `Actor.IsIdle` (`Actor.cs:75` =
  `CurrentActivity == null`). **Death cause** needs `AttackInfo` (`INotifyKilled`) — not available
  from `ActorRemoved`, so cause attribution needs a companion per-actor tap (see §2a "death").

---

## Part 2 — Spec

### 2a. `UnitLifecycleLogger` (engine trait)

**Type & location.** New world trait
`engine/OpenRA.Mods.Common/Traits/World/UnitLifecycleLogger.cs`, `[TraitLocation(SystemActors.World)]`,
implementing `IWorldLoaded, ITick`. Mounted **unconditionally** in `mods/ww3mod/rules/world.yaml`
(the same way `BotVsBotMatchWatcher` is) and self-gating to a no-op when disabled — so its presence
never changes normal play.

**Gate (off by default).** In `IWorldLoaded.WorldLoaded`, return immediately unless
`TestMode.IsActive && enabled`, where `enabled` comes from a new `Test.UnitLifecycleLog` launch arg
parsed in `TestMode.Initialize` (`TestMode.cs:90-129`):
- `Test.UnitLifecycleLog=true` → write to `Path.ChangeExtension(TestMode.ResultPath, ".lifecycle.jsonl")`
  (mirrors the `.watcher.log` sibling convention, `BotVsBotMatchWatcher.cs:146-148`).
- `Test.UnitLifecycleLog=<path>` → explicit path.
- absent/empty → **trait is inert**, no file, no per-tick work.

Expose `TestMode.UnitLifecycleLogPath` (string, like `TournamentConfigPath`). `run-test.sh` passes it
through a new `--lifecycle` flag (§2c).

**What is tracked.** On first tick (deferred like the watcher's SR discovery — `IWorldLoaded` runs
before `SpawnMapActors`), subscribe to `world.ActorAdded/ActorRemoved` and seed the tracked set.
**Track only "interesting" actors:** `Owner` is a non-neutral combatant AND the actor has
`IPositionable`/`Mobile` **or** carries `UpdatesPlayerStatistics` (the same population the composition
telemetry counts) — i.e. real units, not projectiles/effects/smudges. Keep a
`Dictionary<uint, UnitTrack>` keyed by `ActorID` holding: type name, owner client index, spawn tick,
last order tick, order count, current idle-span start tick, last sampled cell.

**Events (one JSONL line each).** Common envelope on every line:

| field | type | meaning |
|---|---|---|
| `t`   | int    | `world.WorldTick` |
| `ev`  | string | event kind (below) |
| `aid` | int    | `ActorID` (uint, fits) |

Per-kind payloads:

- **`spawn`** — emitted from `ActorAdded` for a tracked actor.
  `{type, owner, x, y, cost}` — `type=self.Info.Name`, `owner=Owner.ClientIndex`,
  `x,y=self.Location` (CPos cell), `cost` from `ValuedInfo` (as `UpdatesPlayerStatistics` reads it,
  `PlayerStatistics.cs:287-288`).
- **`order`** — emitted from the `ModularBot.QueueOrder` funnel (§1.3) via a call into the logger:
  `LogOrder(Player owner, string moduleTag, Order o)`.
  `{owner, mod, ord, subj, tx, ty, tactor, queued}` —
  `mod=currentModuleTag` (issuing `IBotTick`/attack-response module, e.g. `"PoiOffensiveBotModule"`);
  `ord=o.OrderString`; `subj=o.Subject?.ActorID` (the ordered unit, `-1` if null);
  target from `o.Target` — `tx,ty` = target cell (`Order.cs:63`; `CenterPosition→cell` or
  `TargetLocation`), `tactor=o.Target.Actor?.ActorID` when the target is an actor;
  `queued=o.Queued` (`Order.cs:62`). Also bumps that subject's `last order tick` + `order count`
  in the track. (Production orders whose `Subject` is a queue actor, not a combat unit, are still
  logged — the analyzer filters by whether `subj` is a tracked unit.)
- **`sample`** — position sample every `SampleInterval` ticks (default **25** = 1s, matching the
  watcher's `PoiSampleInterval`), for each tracked live actor:
  `{x, y, idle, terr}` — `x,y=self.Location`; `idle=self.IsIdle` (`Actor.cs:75`);
  `terr` = territory class from the **omniscient** seam (§1.5):
  bucket `InfluenceMap.GetEnemyInfluence(owner)` at the actor's grid cell vs `GetFriendlyInfluence`
  → `"own" | "contested" | "enemy" | "neutral"`. (If the owner is an `@experimental`/human
  participant with a `ControlField`, prefer `ControlField.OwnerAt` and record `terr_src:"belief"`;
  else `terr_src:"omni"`. Analyzer treats both uniformly.) Sampling is round-robin-batched to avoid a
  per-tick spike; interval is the dominant size/perf knob.
- **`idle_start` / `idle_end`** — edge-triggered from the per-tick `IsIdle` transition of a tracked
  actor (cheaper and more precise than reconstructing spans from `sample`). `idle_start` carries
  `{x, y, terr}`; `idle_end` carries `{x, y, dur}` where `dur` = ticks idle. Gives exact longest-span
  and total-idle without depending on sample cadence.
- **`damage`** *(full build only)* — `{x, y, attacker, dmg}` from a companion tap (below). Optional;
  large volume. **Excluded from the first slice.**
- **`death`** — unit left play. Two sources:
  - minimal (slice 1): `ActorRemoved` → `{x, y, orders}` (`orders` = lifetime order count for that
    `aid`), **no attacker** (unavailable from `ActorRemoved`).
  - full: a tiny companion per-actor trait `UnitLifecycleTap : INotifyKilled` (attached in YAML
    alongside `UpdatesPlayerStatistics`) forwards `Killed(self, AttackInfo e)` so the death line can
    carry `{x, y, attacker=e.Attacker?.ActorID, cause=e.Attacker?.Info.Name, orders}`. Distinguish
    combat death vs. non-combat dispose (mirrors `UpdatesPlayerStatistics.Killed` vs `Disposing`,
    `PlayerStatistics.cs:293/:400`).

**Match end.** On the final tick (hook the same `Game.Exit` path the watcher uses, or flush in
`ITick` when the watcher has finished) emit one **`end`** line per tracked live actor:
`{ev:"end", aid, type, owner, x, y, idle, terr, orders, total_idle, longest_idle}` — an end-of-game
census so the analyzer needn't infer survivors. Flush with explicit `File.AppendAllText` /
buffered `StreamWriter.Flush()` (the watcher notes `Log.Write` buffering is unreliable before
`Game.Exit`, `:144-145`).

**File & format.** One JSONL file per game (append-only, one compact JSON object per line, no pretty
print — same manual-`StringBuilder` style as `SerializeVerdict` to avoid a `System.Text.Json`
dependency on the engine project, per `BotVsBotMatchWatcher.cs:512`). A **`meta` first line**
records `{ev:"meta", schema:1, scenario, seed, players:[{ci, bot_type, faction}], timestep}` so the
analyzer is self-describing (seed/scenario/bot types without cross-reading the verdict).

**Perf / size estimate.** Dominant cost is `sample` lines: N units × (match_ticks / SampleInterval).
For a 12-min match (~18 000 ticks @ 40ms) with ~40 live units and interval 25 → ~720 samples/unit →
~29k sample lines; orders add roughly `order_count` lines (commonly a few thousand); spawn/idle/death
are O(units). Estimate **~40–60k lines, ~4–8 MB** uncompressed per match at interval 25; linear in
1/interval. CPU: one `IsIdle` bool + one grid lookup per tracked actor per sample tick, plus a string
append — negligible against sim tick cost, and **entirely absent** when the gate is off. Knobs to cap
size: raise `SampleInterval`, or set `Test.UnitLifecycleLog=events` to drop `sample` lines and keep
only edge/spawn/order/death (idle spans still exact from `idle_*`).

**Determinism.** Logger only **reads** sim state and writes a file; it draws no RNG and issues no
orders, so it is byte-identical to a non-logged run (same discipline the watcher documents,
`:59-64`). The `ModularBot` order-tag field is set/read on the same thread inside the existing bot
tick and changes no control flow.

### 2b. `tools/behavior-lint/` analyzer (Python)

Mirror the existing `tools/autotest/parse-*.py` style (stdlib only, text + `--csv`, clean exit when
data is absent — `parse-composition.py` is the template). Layout:

```
tools/behavior-lint/
├── behavior_lint.py     # entry: reads one .lifecycle.jsonl, prints report, exits 0/1
└── README.md
```

**Load.** Stream the JSONL once, building per-`aid` records: spawn tick+cell, ordered list of
`order` events (tick, mod, ord, target), `idle_*` spans, `sample` track (for territory dwell), death
(tick, cause, combat?), and the `end` census. Also a per-type roll-up. "Game active for owner" =
between first spawn on that side and the owner's SR loss / match end (read from `meta` + `death`
of the owning SR if present; else whole match).

**Rule set** (thresholds are CLI-overridable; defaults below are starting points to tune against real
logs):

- **R1 — under-tasked unit.** Unit received **≤ 1** order over its whole lifetime while its side was
  at war. Flags call-in-and-forget. (Uses order events with `subj==aid`.)
- **R2 — excessive idle while at war.** `total_idle > IdleTotalTicks` (default 1500 ≈ 60s) **or**
  `longest_idle_span > IdleSpanTicks` (default 750 ≈ 30s), measured only while the owner is at war.
- **R3 — idle in enemy territory after last order.** An idle span with `terr=="enemy"` lasting
  `> EnemyIdleTicks` (default 500 ≈ 20s) that begins **after** the unit's last received order
  completed — a unit abandoned forward. (Territory from the `sample`/`idle_start` `terr` field, §2a.)
- **R4 — spawn→first-order latency (per type).** Report p50/p90/p99 of `first_order_tick − spawn_tick`
  per actor type; WARN a type whose **p90 > FirstOrderTicks** (default 250 ≈ 10s) — units that sit at
  the beachhead unassigned.
- **R5 — order churn / oscillation.** Per unit, count direction reversals: consecutive move/attack
  orders whose target bearing flips > 90° within `ChurnWindowTicks` (default 100). WARN if
  `reversals_per_min > ChurnRate` (default 6) — the "twitching between two goals" pathology.
- **R6 — died with zero orders.** Unit has a `death` event and **never** appears as an order `subj`.
  Called in and lost without ever being commanded.
- **R7 — transport parked after unload.** For carrier types (config list, defaulting to the
  `MountedTransportBotModule.CarrierTypes` names), detect an unload order (`ord` in the unload/deploy
  set) followed by `idle > TransportIdleTicks` (default 500) with no subsequent move order — the
  "transport standing at the drop point" report.
- **R8 — end-of-game idle census (per type).** From the `end` lines: per type, count and fraction of
  survivors with `idle==true` at match end, plus median `total_idle`. Pure descriptive census (no
  threshold) — the fast "who was standing around when the game ended" view.

**Output.** Compact text report:

```
== behavior-lint: <scenario> seed=<n>  (players: 0=<bot> 1=<bot>) ==
WARN R2  aid=413 type=abrams owner=0  idle_total=2100t longest=1400t terr=own
WARN R6  aid=502 type=bradley owner=1  died t=9001 orders=0
WARN R7  aid=88  type=chinook owner=0  unloaded t=3200 then idle 900t at (44,71) terr=enemy
...
R8 end-of-game idle census (owner 0):
  abrams     alive=6  idle=4 (67%)  median_total_idle=1800t
  ...
Summary: 5 WARN across 3 rules; 2 units flagged R6.
  drill:  ./tools/behavior-lint/behavior_lint.py <file> --actor 413
```

`--actor <aid>` prints that unit's full timeline (spawn, every order with module+target, idle spans
with territory, death) — the drill-down. Exit non-zero if any WARN fired (so CI/batch can gate),
`--warn-only`, `--json`, `--csv`, and `--<threshold> N` overrides. When the input file is missing or
predates `schema:1`, print a one-line note and exit 0 (the `parse-composition.py` "nothing to render"
convention).

### 2c. Integration with the autotest harness

- **`run-test.sh`:** add a `--lifecycle` flag (and `--lifecycle-interval N`) that (a) sets a
  `LIFECYCLE=1` and (b) appends `Test.UnitLifecycleLog=<RESULT_FILE .jsonl>` to the `launch-game.sh`
  arg list (next to `Test.ResultPath`, `run-test.sh:449`). Path derives from `RESULT_FILE` so it lands
  in `~/.ww3mod-tests/` and is archived into `${SCREENSHOT_DIR}` alongside `result.json`
  (extend the archive copy at `:557-561`). After the run, if `LIFECYCLE=1` and the file exists,
  invoke `tools/behavior-lint/behavior_lint.py "<file>"` and echo its report under a
  `==> Behavior lint:` header (non-fatal: never change the pass/fail exit — lint is advisory).
- **`run-batch.sh` / tournament:** since each `run-test.sh` archives its own `.lifecycle.jsonl` per
  `RUN_ID`, a batch-level `parse-behavior.py <batch-dir>` (thin wrapper over `behavior_lint.py`,
  mirroring `parse-composition.py`'s batch glob of `match_*.json`) can aggregate WARN counts per
  bot_type across the batch. Default OFF (opt-in `--lifecycle`) because of the per-match file size.
- **Report lands:** per-run report to stdout + the `.jsonl` archived in
  `~/.ww3mod-tests/screenshots/<RUN_ID>/`. No new top-level dirs.

### 2d. Phasing

**First slice (smallest useful):**
1. `ModularBot` order-funnel tagging (§1.3) + the `Test.UnitLifecycleLog` gate in `TestMode`.
2. `UnitLifecycleLogger` world trait emitting **`meta`, `spawn`, `order`, `idle_start/idle_end`,
   `death`(minimal, no attacker), `end`** — i.e. everything except `sample`, `damage`, and the
   `INotifyKilled` companion tap. Idle spans come from the edge events, so `sample` (the big cost)
   is deferred; territory `terr` is attached to `idle_start`/`end`/`death`/`end` via the omniscient
   `InfluenceMap` seam only.
3. Analyzer rules **R1, R2, R6, R8** (the ones that need only order-count + idle spans + census) plus
   the `--actor` drill-down.

This first slice already answers the three named symptoms: **units never re-tasked** (R1/R6),
**units idling** (R2/R8), and gives the drill-down to eyeball a suspect `aid`. It needs **one**
engine trait + a ~10-line `ModularBot.cs` change + a small Python file — no per-actor YAML, no
`sample` volume.

**Full build (follow-ups):** add `sample` position track + `terr` per sample (enables R3, R5, and
transport-parked R7 with movement confirmation), the `UnitLifecycleTap : INotifyKilled` companion for
death cause/attacker + `damage` events, R4 latency percentiles, the `ControlField` belief-territory
preference for participant players, and the batch aggregator `parse-behavior.py`.

---

## Real classes/files to touch (grounded)

| Concern | File:line to modify / add |
|---|---|
| Order-funnel module tag | `engine/OpenRA.Mods.Common/Traits/Player/ModularBot.cs:81-113` (add `currentModuleTag`, wrap the `BotTick`/`RespondToAttack` loops `:95-97`/`:124-126`, call logger in `QueueOrder`) |
| Gate arg | `engine/OpenRA.Game/TestMode.cs:90-129` (parse `Test.UnitLifecycleLog`, add `UnitLifecycleLogPath` prop) |
| Logger trait (new) | `engine/OpenRA.Mods.Common/Traits/World/UnitLifecycleLogger.cs` |
| Mount the trait | `mods/ww3mod/rules/world.yaml` (unconditional, beside `BotVsBotMatchWatcher`) |
| Death-cause tap (full build, new) | `engine/OpenRA.Mods.Common/Traits/UnitLifecycleTap.cs` + YAML beside `UpdatesPlayerStatistics` |
| Territory seam (read-only) | `InfluenceMap.GetEnemyInfluence/GetFriendlyInfluence` `InfluenceMap.cs:143/:156`; opt. `ControlField.OwnerAt` `ControlField.cs:624` |
| Global spawn/death hooks | `World.ActorAdded/ActorRemoved` `World.cs:436-437` |
| Analyzer (new) | `tools/behavior-lint/behavior_lint.py` (+ `README.md`), batch wrapper `parse-behavior.py` |
| Harness wiring | `tools/autotest/run-test.sh:444-457` (arg passthrough), `:557-561` (archive + invoke lint) |
