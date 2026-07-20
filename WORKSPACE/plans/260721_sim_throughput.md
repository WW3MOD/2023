# Autotest simulation throughput — options report (2026-07-21)

> READ-ONLY recon. No code was changed, nothing was built, no game was run.
> Every claim is traced to `file:line`. Speedup figures are derived from the
> tick/render model below; the two marked **(measure)** need one timed run to
> pin an exact number and could not be measured under the no-run constraint.

## TL;DR

- **Single `run-test.sh` runs are 1× because the harness never passes a speed
  arg.** `TestMode.SpeedMultiplier` defaults to `1` (`engine/OpenRA.Game/TestMode.cs:80`)
  and `run-test.sh` launches `launch-game.sh` with no `Test.SpeedMultiplier`
  (`tools/autotest/run-test.sh:285-295`). The mod's default `Timestep` is 60 ms
  (`mods/ww3mod/mod.yaml:369-372`) → ~16.7 sim ticks/s of wall-clock.
- **The tournament harness already runs 8×** by passing `Test.SpeedMultiplier=8`
  from the scenario config (`tools/autotest/run-tournament.sh:298`,
  configs `SpeedMultiplier: 8`), applied by dividing `world.Timestep`
  (`BotVsBotMatchWatcher.cs:152-158`). But that apply-path is **tournament-only** —
  Lua single-tests get nothing even if you pass the arg.
- **Yes, it can go faster than 8× and effectively "uncapped."** The ceiling is
  CPU-per-tick, not a hard cap. The lever with the biggest payoff is killing the
  **forced render-per-tick coupling** (`Game.cs:1026-1027`), which today makes
  every logic tick drag a GPU frame.
- **The sim is tick-based and deterministic; speed and render changes are
  behavior-neutral by construction** (verified — see "Validity" section). Bots
  will not play differently at higher speed.

---

## How speed actually works (the model)

The main loop (`engine/OpenRA.Game/Game.cs:938-1064`) advances **logic** and
**render** on independent timestamps:

- `logicInterval = OrderManager.SuggestedTimestep` → `World.Timestep` ms
  (`Game.cs:990`, `OrderManager.cs:203-219`). This is the **only** knob that sets
  wall-clock sim speed. `world.Timestep` is ms-per-tick; halving it doubles ticks
  per second.
- `Test.SpeedMultiplier=N` lowers `world.Timestep` to `max(1, base/N)`
  (`BotVsBotMatchWatcher.cs:155-157`) — identical mechanism to the in-game
  cheat speed button (`SpeedControlButtonLogic.cs:58-62`, capped at 8× there).
- `Test.GameSpeed=fastest` only selects the `fastest` preset — `Timestep: 40`
  (`mod.yaml:381-384`), i.e. **1.5×** off the 60 ms default. That's why every
  config note says "GameSpeed capped ~2×, SpeedMultiplier dominates."
- **Render is forced 1:1 with logic during normal play:** after each `LogicTick`,
  `renderBeforeNextTick = true` (`Game.cs:1026-1027`), and the render block
  (`1032-1046`) then runs a full `RenderTick` (`863-936`) — BeginFrame, world
  draw, UI, EndFrame/flip. So at 8× you also render 8× as often. Rendering is the
  tax that stops 8× config from delivering 8× wall-clock (harness comment claims
  ~3-4× realized, `run-tournament.sh:286-289`).
- **Catch-up is bounded:** `MaxLogicTicksBehind = 250` (`Game.cs:970, 1010-1011`).
  If a tick costs more wall-time than `logicInterval`, the loop resets `nextLogic`
  and drops the catch-up. Net effect: the sim **never runs faster than one tick
  can be computed** — the true ceiling is CPU, and high multipliers on heavy
  battles quietly stop scaling. This is a safety property, not a bug.

**Suspended-window path (the latent "headless-ish" mode).** When the window is
minimized or hidden, SDL sets `IsSuspended = true`
(`Sdl2Input.cs:124-126`). The loop then **skips `RenderTick` entirely**
(`Game.cs:1032` guard) and only pumps input (`1049-1059`). BUT: while suspended,
the forced-render flag is cleared only at the *render cadence*
(`Game.cs:1058`, gated on `now >= nextRender`), so logic advances at most once per
`renderInterval`. Consequence that inverts intuition:

- minimized **+ uncapped framerate** (`CapFramerate=false`, the default →
  `renderInterval≈1 ms`, `Game.cs:994-998`, `Settings.cs:201`) → logic gate clears
  almost instantly → **sim runs near CPU-bound, no GPU cost**. Big win.
- minimized **+ 5 fps cap** (what the tournament sets today,
  `run-tournament.sh:301-302` → `renderInterval=200 ms`) → logic throttled to
  ~5 ticks/s. **Slower.** The current tournament combo works only because the
  window is *visible* at 5 fps, not suspended.

This interaction (minimize helps only with an *uncapped* framerate) is the key
non-obvious finding and drives several options below.

---

## Validity: is speed behavior-neutral? (verified — yes)

Determinism must be UNCHANGED. It is, by construction:

- `world.Timestep` is pure wall-clock pacing. It never enters a synced path — it
  is read only in the loop's scheduling (`Game.cs:990`) and the cheat button
  (`SpeedControlButtonLogic.cs`). The sync hash reads game state, not real time.
- **All rendering is `Sync.RunUnsynced`** and bot/world logic is in `world.Tick()`
  (`Game.cs:808`), so skipping `RenderTick` cannot alter simulation.
- Bot decisions are a documented pure function of tick number + seed
  (`BotVsBotMatchWatcher.cs:56-58`: `localSeed = seed*6364136223846793005+…`).
- **Lua test timers are tick-based, not wall-clock.** `Trigger.AfterDelay(ticks)`
  counts sim ticks (`test-helpers.lua:82-83, 108-109`); `TicksPerSecond` is a
  fixed constant (`test-helpers.lua:9`). Changing speed changes wall-clock, not
  the tick count a test waits, so assertions fire at the same sim state. The
  7500-tick S1 window shrinks cleanly in wall-clock with no logic change.
- `OrderLatency: 2` (`mod.yaml`) is 2 *ticks*, delivered by tick number, so it too
  is speed-invariant.

**One pre-existing inaccuracy to note (NOT a speed issue):** `TicksPerSecond = 25`
(`test-helpers.lua:9`) but the mod default `Timestep` is 60 ms → ~16.7 ticks/s.
Any `seconds→ticks` conversion in Lua is ~1.5× off in real seconds. It is constant
across speeds (tick-based), so it does not threaten validity, but it means "wait
10 s" waits ~15 real seconds at 1×. Worth a separate fix; flagged, out of scope.

---

## Options

### A. Pass a speed multiplier through `run-test.sh` (fix the "1×" directly)
- **Mechanism:** add a `--speed N` flag to `run-test.sh` that forwards
  `Test.SpeedMultiplier=N` into the `launch-game.sh` args block
  (`run-test.sh:285-295`). **Blocker:** the multiplier is currently applied ONLY
  in `BotVsBotMatchWatcher.WorldLoaded` (`BotVsBotMatchWatcher.cs:152-158`), which
  is tournament-only. For Lua tests it does nothing. So Option A must ship with
  Option B.
- **Expected speedup:** up to ~4-6× realized for compute-light tests, less for
  heavy ones (render coupling + CPU ceiling). **(measure)**
- **Cost:** S (shell) + depends on B.
- **Validity risk:** none (tick-based).

### B. Apply `TestMode.SpeedMultiplier` universally, not just in the tournament watcher
- **Mechanism:** apply the `world.Timestep` division once at map load for ALL
  test-mode runs. Cleanest home: a tiny world trait (mirroring the
  `BotVsBotMatchWatcher` apply block, `cs:152-158`) gated on
  `TestMode.IsActive && SpeedMultiplier>1`, added to `world.yaml`; or apply in
  `Game.LoadMap` next to the existing `GameSpeedOverride` hook
  (`TestMode.cs:62-65, 94`). The arg is already parsed and clamped to 1–16
  (`TestMode.cs:100-102`) — only the apply site is missing.
- **Expected speedup:** unlocks A for the 90% of tests that are Lua, not
  tournaments.
- **Cost:** S.
- **Validity risk:** none.

### C. Run hidden test games headless-ish: minimize + uncapped framerate
- **Mechanism:** for Mode-B (no-screenshot) runs, launch minimized so
  `IsSuspended` skips `RenderTick` (`Sdl2Input.cs:124-126`, `Game.cs:1032`) AND
  leave framerate uncapped so the logic gate clears fast (`Game.cs:1058, 994-998`).
  Engine already reads `OPENRA_WINDOW_MINIMIZED=1` (`run-test.sh:208-214`).
  **Critical:** do NOT combine with `CapFramerate=true/MaxFramerate=5` — that
  throttles a suspended run to ~5 ticks/s (see suspended-window analysis above).
  The current tournament profile (`run-tournament.sh:301-302`) should be revisited:
  it caps at 5 fps *visible*; switching to *minimized + uncapped* should beat it.
- **Expected speedup:** removes GPU frame cost from the per-tick loop → approaches
  the CPU-bound ceiling; stacks multiplicatively with B. **(measure)** — likely
  the single biggest lever short of a true headless client.
- **Cost:** S (launch flags) — but must be validated with one timed run because
  of the framerate-cap interaction.
- **Validity risk:** none (render is unsynced).

### D. Kill the forced render-per-tick coupling for test mode
- **Mechanism:** `Game.cs:1026-1027` sets `renderBeforeNextTick = true` after every
  logic tick "to force at least one render per tick during regular gameplay."
  Gate that on `!TestMode.IsActive` (or a `Test.MaxRenderFps` cap) so hidden test
  runs render on a slow cadence (or never) while logic free-runs. This is the
  root cause of 8× config only realizing ~3-4×.
- **Expected speedup:** lets logic decouple from render even when the window is
  visible; combined with C removes the coupling entirely.
- **Cost:** M (one guarded conditional, but touches the hot loop — needs care +
  a sync-hash regression check).
- **Validity risk:** low (render is unsynced; verify sync report unchanged).

### E. "Tick as fast as CPU allows" mode (true uncapped)
- **Mechanism:** the engine already has this exact pattern for save-loading:
  `Game.cs:1001-1005` sets `logicInterval = 1; renderInterval = 200` to "tick as
  fast as possible while restoring game saves." Add a `Test.MaxSpeed=true` branch
  that does the same for a live test world (logicInterval=1, render throttled),
  so the sim runs one tick immediately after the previous finishes — bounded only
  by CPU and `MaxLogicTicksBehind` (`Game.cs:970`). Supersedes A/B's fixed
  multiplier with "as fast as the box can go."
- **Expected speedup:** maximal single-process throughput; on compute-light maps
  many× beyond 8×. **(measure)**
- **Cost:** M.
- **Validity risk:** low — same unsynced-render + tick-based guarantees.

### F. Never steal focus
- **Mechanism:** window creation grabs foreground (SDL default); `run-test.sh`
  works around it on macOS by bouncing focus with osascript (`run-test.sh:236-254,
  309-311`) and on Windows via `OPENRA_WINDOW_MINIMIZED` (`:208-214`). The durable
  fix is to create the window hidden/without focus for test mode (or run headless
  per Option G so there is no window at all). Minimizing (Option C) already
  sidesteps focus theft as a side effect.
- **Expected speedup:** none directly — removes the focus-steal pain and is a
  prerequisite for comfortably running batches unattended.
- **Cost:** S (minimize path exists) / M (proper hidden-window create).
- **Validity risk:** none.

### G. Parallelism: isolated support dirs + per-instance port
- **Mechanism:** `launch-game.sh:60` sets no `Engine.SupportDir`, so every instance
  shares one support dir. Collisions that force serialization today:
  - `settings.yaml` backup/restore race (`run-test.sh:276-299`,
    `run-tournament.sh:275-278, 343-345`) — shared file, non-reentrant.
  - `Logs/debug.log` is overwritten each launch; the tournament copies it out
    *between sequential matches* (`run-tournament.sh:350-352`) — parallel runs
    would clobber it.
  - The local skirmish server binds a port; two instances would contend.
  Fix: pass `Engine.SupportDir=<per-instance temp>` (the dedicated launcher
  already threads `Engine.SupportDir`, `launch-dedicated.sh:98`) + a distinct
  server port per instance, and drop the shared-settings backup dance in favor of
  per-instance settings. Then N matches run concurrently (CPU/RAM permitting).
- **Expected speedup:** near-linear in core count for batch/tournament wall-clock
  (the "one game process machine-wide" rule is a *policy* driven by focus/window,
  not a hard technical limit once G+C land).
- **Cost:** M (support-dir plumbing + port allocation + harness rework).
- **Validity risk:** none (each instance is a fully independent deterministic sim).

### H. Trim fixed per-run overhead
- **Mechanism:** tests already deep-link to the map (`Launch.Map=`,
  `run-test.sh:286`) so there's no menu walk. Remaining fixed costs: engine
  cold-start/asset load, and `TimeLimitSeconds` padding. Smoke configs already use
  short limits (`…-smoke.yaml` `TimeLimitSeconds: 30`). Marginal further wins: map
  cache warmth, shorter warmup windows.
- **Expected speedup:** small, constant offset per run (matters most for many
  short runs).
- **Cost:** S.
- **Validity risk:** none for time-limit trims that stay above the phenomenon
  under test; **medium if** a shortened window truncates the behavior being
  asserted — scope per test.

---

## Structural option — a true headless simulation harness

**Aim-high target:** run bot-vs-bot matches with **no graphics device at all** and
logic free-running at CPU speed, so a match is bounded only by tick-compute.

- **Why not the dedicated server:** `launch-dedicated.sh` → `OpenRA.Server.dll` is
  a **lockstep order relay** — it does not run `world.Tick()` or bot modules
  (those live in the client's `Game.Loop`). So "headless via dedicated server"
  would not simulate a match. A headless harness must be a *client* with rendering
  disabled, not the server.
- **Shape:** a null/offscreen graphics + audio platform (the platform is already
  abstracted behind `PlatformInterfaces.cs:62` `IsSuspended` and the
  `Sdl2PlatformWindow`), selected by a `Test.Headless=true` launch arg, plus the
  Option-E `logicInterval=1` loop branch and the Option-D render-coupling removal.
  With no window there is no focus theft (Option F is subsumed) and no GPU cost.
- **Payoff:** this is the clean convergence of C+D+E+F. Combined with Option G
  (isolated support dirs), it enables a genuine parallel batch runner: M headless
  matches × N cores, each deterministic from its seed.
- **Cost:** L (null platform implementation + loop branch + harness runner). The
  incremental options below deliver most of the benefit first; this is the
  end-state.
- **Validity risk:** low by the same construction (render/audio unsynced, sim
  tick-based) — but a headless build must be pinned by a sync-hash equivalence
  test: same seed + map + speed must produce byte-identical verdicts headless vs
  windowed.

---

## Recommended adoption order

1. **B + A (S/S):** apply `TestMode.SpeedMultiplier` universally, then expose
   `--speed` on `run-test.sh`. Immediately fixes "single tests run at 1×." Zero
   validity risk, no hot-loop surgery.
2. **C (S, measure):** switch hidden Mode-B runs to **minimized + uncapped
   framerate**; re-profile the tournament's current 5 fps-visible profile against
   it. Likely the biggest single win for the least code — but validate with one
   timed run because of the framerate-cap interaction.
3. **D (M):** gate the forced render-per-tick coupling off for test mode so 8×
   config actually approaches 8×. Verify sync report unchanged.
4. **G (M):** isolated support dirs + per-instance ports → real parallel batches.
   Multiplies everything above by core count.
5. **E (M) → Structural headless (L):** "tick as fast as CPU allows," then a null
   graphics platform as the end-state. Do E first (reuses the existing
   save-load loop branch), then fold C/D/E/F into the headless client.

Steps 1-2 are same-day, low-risk, and directly answer the user's "why 1× / can it
be 8× or uncapped." Steps 3-5 are the throughput ceiling.
