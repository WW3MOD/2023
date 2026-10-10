# TELEMETRY — gameplay event log channels

**Trigger:** `TELEMETRY <event-classes>` — e.g. `TELEMETRY orders deaths`, `TELEMETRY missiles`, or `TELEMETRY all`.

**Gives you:** a machine-readable record of what actually happened in a run, written to disk, so a "why did that happen?" question can be answered by grepping a file instead of adding a trace, rebuilding, rerunning and stripping it. Pairs with AUTOTEST.

**When *not* to use it:** for a trivial bug where one trace line in the relevant file is faster.

---

## What exists

All channels are off by default and cost nothing in a normal game. Each is switched on per run by a `run-test.sh` flag (which sets a `Test.*` launch arg) or an environment variable, and writes beside the run's `result.json` in its run dir.

| Channel | Turn it on | Writes | Covers |
|---|---|---|---|
| Unit lifecycle (`UnitLifecycleLogger`, world trait) | `run-test.sh --lifecycle` (`Test.UnitLifecycleLog=<path>`) | `<run-dir>/result.lifecycle.jsonl` | `meta`, `spawn`, `order`, `idle_start`/`idle_end` (edge-triggered), `death` (no attacker), end-of-game census. Requires `Test.Mode`. After the match `run-test.sh` runs `tools/behavior-lint/behavior_lint.py` over it and prints its WARN report — advisory, never changes the verdict. |
| Missile trace (`MissileTrace`) | `run-test.sh --missile-trace` (per-tick lines) or `--missile-trace-summary` (one summary line per missile) | `<run-dir>/result.missiles.jsonl` | one line per missile per tick, plus a summary naming the code path that ended each missile |
| Gun trace (`GunTrace`) | environment `WW3_GUNTRACE=1` | `[GUNTRACE]` lines in `debug.log` | the gun → bullet → warhead → health chain, e.g. `TargetDamage HIT` / `TargetDamage SKIP outsideSpread` |
| Sync reports | `run-test.sh --sync-reports` | `syncdiag-recorded-frame*.log` beside the desync report | saved-game-restore desyncs only; expensive per net frame |
| Module traces | always on | tagged lines in `debug.log` (`[composition] census`, `[supply] …`, `[drone] …`) | whatever each bot module chose to log |

`debug.log` is a single global file truncated by the next launch; `run-test.sh` copies it into the run dir at exit, and that copy is the one to read — the caveats are in [`AUTOTEST.md` §"Logs: fixed paths, no run identity, and concurrent writers"](AUTOTEST.md). Bracketed tags need `grep -F` (`grep` on the macOS host is ugrep).

Every channel is observation only: it reads sim state and writes a file, draws no RNG and issues no orders, so a traced run plays out identically to an untraced one at the same seed.

## What I do when triggered

1. **Map the question to a channel.** Orders, idle spans, spawns and deaths → `--lifecycle`. Missile behaviour → `--missile-trace`. "It fires and nothing dies" → `WW3_GUNTRACE=1`.
2. **Hand up or run the launch** with the flag set, at a fixed `--seed` so the run can be repeated. Who runs it is CLAUDE.md §"Who runs what"; a worker hands up the exact command and the question the log must answer.
3. **Grep / parse the JSONL** in the run dir to answer the question. Each lifecycle line is `{"t":<tick>,"ev":"<event>",…}`.

## When the question needs an event class that does not exist

Not covered today: weapon fire events in JSONL, attacker on `death`, condition grants/revocations, ownership transfers, suppression tier transitions, a per-tick sample.

1. **Extend `UnitLifecycleLogger`** (or add a sibling world trait) with the new event class, keeping its discipline: active only when `TestMode.IsActive` and its path arg is set; reads sim state and writes a file only; no RNG, no orders, no actor/player/trait mutation. Add a `run-test.sh` flag if a new launch arg is needed.
2. **Update `tools/behavior-lint/`** if the analyzer should read the new event, and its fixture.
3. **Document it** in the table above.

**Volume:** at the default 16.67 ticks/s (`Timestep: 60`; never 25 — see [`conventions.md`](../reference/conventions.md)) a per-tick event for every unit in a busy battle is thousands of lines per second. Prefer edge-triggered events (as `idle_start`/`idle_end` are) and buffered writes over per-tick samples.
