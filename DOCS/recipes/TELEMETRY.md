# TELEMETRY — gameplay event log channel

**Trigger:** `TELEMETRY <event-classes>` — e.g. `TELEMETRY orders fires deaths` or `TELEMETRY all` for everything wired up.

**Gives you:** a per-tick JSON-line log of what's actually happening in the simulation, written to disk. I can grep it post-mortem without re-instrumenting the engine for every "why did that happen?" question. Pairs perfectly with AUTOTEST.

**When *not* to use it:** for trivial bugs where a single trace line in the relevant file is faster.

---

## Status

The channel is the **"Developer Logging" pending decision** in `WORKSPACE/RELEASE_V1.md`. It's not built yet. First invocation kicks off the build; subsequent invocations just enable/configure event classes.

## What I do (build, first time)

1. **Engine-side hook** — add a `DevLog` static (or extend `TestMode`) with `Write(string category, object payload)`. Output to `~/.ww3mod-tests/gameplay.log` (or `Platform.SupportDir/Logs/gameplay.log` for non-test runs).
2. **Wire event classes**:
   - `orders` — every order issued (move, attack, force-fire, deploy, …) with tick + actor + target
   - `fires` — every armament fire with tick + actor + weapon + target + position
   - `deaths` — actor death with tick + cause + killer
   - `condition-grants` — granted/revoked conditions with token + actor
   - `owner-changes` — actor ownership transfers
   - `suppression` — tier transitions on suppression-tracked actors
   - More as needed
3. **Lua API** — `DevLog.Mark("note", payload)` so test scripts can drop landmarks into the log.
4. **Gating** — channel is fully off unless explicitly enabled at launch (`DevLog.Channels=orders,fires`). No perf or disk cost in normal play.
5. **Document in `DOCS/recipes/AUTOTEST.md`** — auto-tests can tail the log for richer assertions.

## What I do (subsequent invocations)

1. **Configure**: pass `DevLog.Channels=<list>` to the launch.
2. **Run** the test or scenario.
3. **Grep / parse** `~/.ww3mod-tests/gameplay.log` to answer the user's question.

## Why this is the highest-leverage skill to build

Looking at recent debug sessions: 30–50% of time is spent on add-trace → build → run → strip cycles. A persistent channel collapses that to a one-time build, then just `grep "fires" gameplay.log` per question. Also resolves a v1 pending decision — useful both for development and as a v1 dev-mode feature.

## Decision points before building

- **Ship in v1, or dev-only?** Pending in `RELEASE_V1.md`. Recommendation: dev-only initially (single-line gate), promote to v1 if the dev experience proves the channel is also useful for end-user bug reports.
- **Format?** JSON-lines (`{"tick": 123, "category": "fires", ...}`) — easy to grep, easy to parse, easy to tail.
- **Volume?** Per-tick events at 25 ticks/sec for a busy battle could hit 10k+ lines/sec. Buffer + flush per second; truncate file at session start.
