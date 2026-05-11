# Morning Summary — Autonomous Overnight Run (260511 → 260512)

> **Read this first.** Live log of what I tried while you slept. Updated after
> each round.

**Status:** in progress — round 1 (score formula completion) started.

**Starting point:** commit `79d0dff5` (`ai: tournament Phase 1 GREEN`). Phase 1
end-to-end working. Score formula has only `army_value` populated; capture
and kills components present-but-zero. Game runs at 1× real time.

**Plan executing:**
1. Score formula completion (capture_income, kills_value events)
2. Game-speed acceleration
3. Sanity-check 20-seed batch
4. Additional tournament map
5. Phase 4 autonomous loop scaffolding
6. Headless renderer investigation
7. This summary doc, finalized

## Round log

### Round 1 — Score formula completion + game-speed acceleration
*Status: complete pending smoke validation.*

**What was done.** Discovery: `PlayerStatistics` (already attached to every
player) tracks `KillsCost`, `ArmyValue`, and `Income`. `PlayerResources.Earned`
is the cumulative cash earned. The scorer was rewired to **read these directly**
instead of hooking my own `INotifyKilled` / `INotifyOwnerChanged` traits —
saves a YAML pass on every actor + removes a parallel-tracking burden.

**Side-effect:** `MatchTrackingState.CaptureIncome` and `KillsValue` dicts in
`MatchTypes.cs` are now unused. Left in place for now; a follow-up commit can
remove them if no future scorer wants them.

**Game-speed acceleration:** added `Test.GameSpeed=<key>` launch arg. The mod
defines six speeds in `engine/mods/ra/mod.yaml` GameSpeeds — `fastest` is
20 ms/tick = 2× default. `Game.LoadMap` now honors the override on the initial
`option gamespeed` setup order. Tournament config gains a `GameSpeed:` field;
`run-tournament.sh` extracts it and threads it through.

**Smoke `tournament-smoke.yaml` updated** to use `GameSpeed: fastest`.
Theoretical wall-clock for a 30-sec match: 15 wall-clock-seconds + ~10s
engine init.

**PITFALL discovered:** invalid gamespeed keys silently fall back to default
with no error (engine quirk). Documented inline at the LoadMap call site.

### Round 2 — game-speed validation
*Status: smoke test in progress*

Validating `Test.GameSpeed=fastest` actually drops wall-clock by ~2× before
moving on.
