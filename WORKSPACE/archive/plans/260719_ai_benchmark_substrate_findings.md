# AI Benchmark Substrate — Feasibility Findings

**Date:** 2026-07-19
**Type:** research findings (no behavior changes; substrate feasibility for an autonomous bot-vs-bot benchmark)
**Researched against:** `main` @ `06afb643` (working tree clean apart from untracked `.maestro/`). All file:line citations are from this checkout.
**Related:** [`260511_ai_tournament_harness.md`](260511_ai_tournament_harness.md) (the harness this builds on), `WORKSPACE/ai/archive/PITFALLS.md` (tournament pitfalls), `WORKSPACE/DISCOVERIES.md` (pointer entry).

## Question

What is the best technical foundation for an **autonomous AI benchmark system** — many unsupervised bot-vs-bot games, metrics extracted from logs — runnable on **Windows** (the user's platform)?

## Headline

The substrate **mostly already exists**: a mature bot-vs-bot tournament harness (deterministic seeds, JSON verdicts, milestone loop) was built over a multi-round autonomous run. Two things stand between it and the goal:

1. It is **macOS/Linux shell + `osascript`**; the user is on **Windows** (unhandled).
2. There is **no headless mode** — every match opens a real SDL window that **grabs OS focus on Windows**, with no mitigation (the existing focus fix is macOS-only `osascript`).

Everything else — speed, metrics, seeds, orchestration — is solved or nearly so.

---

## Q1 — HEADLESS (no window, no focus theft on Windows)

**Verdict: NOT possible today. This is the crux.** Confidence: high.

- **Only one `IPlatform`** — `DefaultPlatform` (`engine/OpenRA.Platforms.Default/DefaultPlatform.cs:17`), always returns a real `Sdl2PlatformWindow`. `Settings.Platform = "Default"` (`engine/OpenRA.Game/Settings.cs:256`) is an assembly name to load, **not** a null toggle; `Game.cs:373-374` always constructs a real `Renderer`. No `Graphics.Renderer=Null`.
- **Window is always shown.** `Sdl2PlatformWindow.cs:227` creates with `SDL_WINDOW_OPENGL | SDL_WINDOW_ALLOW_HIGHDPI` — **no `SDL_WINDOW_HIDDEN`**. The only hidden window is a 1×1 GL-probe, immediately destroyed (`:576`). On Windows, `SDL_CreateWindow` of a shown window **grabs OS focus**; `OPENRA_WINDOW_MINIMIZED=1` (`:347`) calls `SDL_MinimizeWindow` *after* creation → still a brief focus grab.
- **Dedicated server can't do it.** OpenRA is classic lockstep — the *client* runs the simulation and ticks bots (`engine/OpenRA.Mods.Common/Traits/Player/ModularBot.cs:86` is an `ITick` World trait; `engine/OpenRA.Game/World.cs:467` drives it). The dedicated server is pure order-relay: its only loop is `Thread.Sleep(1000)` monitoring connections (`engine/OpenRA.Server/Program.cs:100-109`); it never creates a `World`. **A match cannot conclude with only a server process.**
- **Existing focus fix is macOS-only.** `tools/autotest/run-test.sh:215-282` captures/restores the frontmost app via `osascript` (pure Cocoa). The settings-backup logic (`run-tournament.sh:193-196`) only branches `Darwin`/`Linux` — **Windows isn't handled at all**.
- The team already weighed and **rejected** a true headless renderer (`WORKSPACE/ai/archive/PITFALLS.md §17`): *"replacing IPlatform + IGraphicsContext with no-op stubs — days of work, with real risk of breaking simulation determinism. NOT worth it"* — choosing a 5-FPS framerate cap instead. **That trade was made for macOS, where `osascript` already tamed focus. On Windows the calculus flips** — there is no focus mitigation.

**Cheapest viable Windows path:** add `OPENRA_WINDOW_HIDDEN=1` support in `Sdl2PlatformWindow` (add `SDL_WINDOW_HIDDEN` at creation). SDL never focuses a window created hidden → solves no-window **and** no-focus-theft together. ~10-line engine change; the GL context still exists (renders to an unmapped surface), sidestepping the "days of work" null renderer. **Highest-value new work.**

## Q2 — SPEED (faster than realtime)

**Verdict: Yes, ~4–6× practical today; init cost dominates short matches.** Confidence: high.

- **`GameSpeed`** (lobby dropdown) — **hard-capped at 2×** in WW3MOD (`fastest` = Timestep 20ms). `PITFALLS §13/§16`.
- **`Test.SpeedMultiplier`** (1–16) — the real lever. `BotVsBotMatchWatcher.cs:121-127` sets `world.Timestep = max(1, Timestep / multiplier)` at `WorldLoaded` (same mechanism as the in-game 8× cheat). Exposed via `tournament.yaml` (`TournamentConfig.cs:59`).
- **Renderer is the ceiling.** Even at 8×, wall-clock ≠ 8× because the render pipeline can't keep up (`PITFALLS §16`). Mitigation shipped: `Graphics.CapFramerate=true Graphics.MaxFramerate=5` (`run-tournament.sh:224-226`) — "headless lite" (`PITFALLS §17`). Combined: **4–6× practical**.
- **Fixed init (~30s) dominates short matches** (`PITFALLS §13`). Committed smoke config's own comment: a 60-sim-second match at 8× still runs **~90s wall-clock** (20 seeds ≈ 30 min). A 720s (12-min) match ≈ ~2 wall-clock min windowed at 6×.

**Speed strategies, best→worst:**
1. **Headless window + high SpeedMultiplier** (needs Q1 fix) — removes render ceiling; likely 8–12×; kills focus problem too. Best ROI.
2. Framerate-cap + SpeedMultiplier:8 (works today) — 4–6×, but window visible/focus-stealing on Windows.
3. Parallelism (Phase 3, unbuilt) — N instances ≈ 1/N wall-clock; multiplies per-match speed. Init cost makes this very attractive.
4. GameSpeed alone — 2× ceiling, ignore.

The ~30s init floor means **throughput is dominated by matches/hour, not speedup/match** for short matches → **parallelism is the bigger long-term lever than raw speed.**

## Q3 — METRICS (machine-readable per-player stats at game end)

**Verdict: Solved and clean for end-of-match. Time-series is the one gap.** Confidence: high.

- `BotVsBotMatchWatcher.cs:234-286` serializes a JSON verdict to `TestMode.ResultPath` at match end: `winner_client_index`, `winner_name`, `win_reason` (`sr_capture`/`time_limit`), `duration_ticks`, per-player `{name, client_index, bot_type, faction, score_total, score_components{...}}`.
- Scorer (`engine/OpenRA.Mods.Common/Tournament/Scorers/WeightedComponentMatchScorer.cs`) reads live engine stats — **all three axes populated now** (the `tournament.yaml` "only army_value" comment is stale Phase-1 text):
  - `army_value` ← `PlayerStatistics.ArmyValue`
  - `capture_income` ← `PlayerResources.Earned` (cumulative; `PITFALLS §14` warns off the 60s-rolling `Income`)
  - `kills_value` ← `PlayerStatistics.KillsCost`
- **Full per-player menu already tracked** (`engine/OpenRA.Mods.Common/Traits/Player/PlayerStatistics.cs`), attached to every combatant via `^Combatant` templates, free to dump: `UnitsKilled`, `UnitsDead`, `BuildingsKilled`, `BuildingsDead`, `KillsCost`, `DeathsCost`, `ArmyValue`, `AssetsValue`, `OrderCount`, `Experience`, `Earned`. Covers "cash earned / units built / lost / captured" with no new instrumentation — just widen `SerializeVerdict`.
- **Gap: no timeseries.** "POIs held over time" / per-tick curves are **not** available — the watcher scores each tick but writes only the final snapshot. The `TELEMETRY` recipe (`DOCS/recipes/TELEMETRY.md`) is the designed home for a per-tick JSON-lines channel but is **not built yet**. POI ownership is also unresolved at the game level (SR capture→Neutral is unwired — see `DISCOVERIES.md` 2026-07-19 SUPPLYROUTE entry / commit `86d36fd5`).
- **Aggregation exists:** `aggregate-tournament.sh` → `summary.csv` + `summary.json` (winrate per side/faction, score-ratio distribution, decisive %, duration stats).

## Q4 — DETERMINISM / VARIANCE

**Verdict: Both fixed-seed reproducibility AND across-run variance achievable — already implemented.** Confidence: high.

- Stock OpenRA seeds `MersenneTwister` from `DateTime.Now.ToBinary()` (`engine/OpenRA.Game/Server/Server.cs:307`). **Overridden**: `Test.RandomSeed=<int>` is honored (`PITFALLS §15`, "Round 5").
- `run-tournament.sh:206` sets `MATCH_SEED = i*1000 + 17` per match: index N reproduces the same game (same code+map); varying N gives an independent sample. **Reproduce an outlier by fixing the seed; sample the distribution by varying it — both met.**
- Determinism holds *given identical engine build + map*; any code/YAML change is a new distribution — exactly the per-commit benchmark signal.
- **Recommendation: embrace variance across N fixed-but-varying seeds** (current model); keep single-seed reproduce for debugging. Don't chase bit-identical cross-machine determinism.

## Q5 — ORCHESTRATION SURFACE

**Verdict: Substantial, reusable infra exists.** Confidence: high.

- **A "scenario" controls:** map (`map.yaml`/`.bin`/`.png`), bot combatants (via **non-playable** `PlayerReference` with `Bot:` field — `PITFALLS §3`; local human sits in a spectator Observer slot), and via `tournament.yaml`: duration (`TimeLimitSeconds`), scorer, win rule, score weights, `GameSpeed`, `SpeedMultiplier` (`TournamentConfig.cs`). Win rules pluggable (`IWinRuleEvaluator`); scorers pluggable (`IMatchScorer`, registered in `MatchHarness`).
- **7 tournament scenarios committed**, incl. `tournament-v2-vs-normal-2p` and mirror variants (mirror = faction-swapped to separate side-bias from faction-strength).
- **Multi-run infra is real and reusable:**
  - `run-tournament.sh` — N seeded matches, per-match JSON+log, `--mirror`, wall-clock watchdog, git-SHA stamping.
  - `loop-tournament.sh` — autonomous milestone loop: batches → aggregate → stop-condition (winrate threshold / budget-hours / max-rounds), `loop_progress.csv`, terminal bell on swings.
  - `aggregate-tournament.sh` — CSV/JSON rollups.
- **`tools/combat-sim` is NOT a substitute.** Standalone TypeScript **tick-by-tick duel** simulator (`package.json`: "Combat balance simulator … tick-by-tick combat analysis"; `src/` = `data.ts`, `index.ts`, `wdist.ts`). Unit-vs-unit damage/armor only — **no economy, production, capture, POIs, or bot AI**. Economy/strategic benchmarks **must** run in the full engine.
- **Reuse verdict:** shell orchestration, scenario format, scorer/winrule plugin points, verdict schema, aggregator all directly reusable. Gap is **portability** — all `.sh`, `uname` Darwin/Linux branches, `osascript`, `pkill`/`pgrep`. On Windows: run under Git-Bash/WSL or port the loop to PowerShell.

## Q6 — maestro (orchestration layer)

Handled separately per the mandate; glanced only. The game-side substrate is what a maestro-style manager would *drive* (fire runs, read `summary.json`, decide next batch). The JSON-verdict + exit-code contract is exactly what an external orchestrator wants; nothing game-side blocks integration.

---

## Recommended architecture

| Component | Recommendation |
|---|---|
| **Launcher / headless** | Add `OPENRA_WINDOW_HIDDEN=1` in `Sdl2PlatformWindow.cs` (`SDL_WINDOW_HIDDEN` at creation). Solves no-window + no-focus-theft together. Keep 5-FPS cap as fallback. |
| **Speed** | `SpeedMultiplier: 8` in `tournament.yaml`; drop framerate cap once hidden. Expect 8–12× once render isn't blocking a visible surface. |
| **Metrics (end-of-match)** | Widen `BotVsBotMatchWatcher.SerializeVerdict` to emit the full `PlayerStatistics` menu. ~30 min, no new tracking. |
| **Metrics (timeseries/POI)** | Build the `TELEMETRY` JSON-lines channel (per-tick economy/army/POI ownership). One genuine new build for "held over time." |
| **Seeds** | Keep `i*1000+17` varying seeds; keep single-seed reproduce. Git-SHA stamp per batch (already done). |
| **Orchestration** | Port `run-tournament.sh`/`loop-tournament.sh` to Git-Bash/WSL, or reimplement the loop in PowerShell. Fix `uname`-gated settings-backup + process-kill for Windows (`taskkill` vs `pkill`). |
| **Parallelism** | Highest throughput lever given the ~30s init floor. Phase 3 (unbuilt): N hidden instances with isolated support dirs. After headless lands. |

## Rough effort estimates

- **Windows-portable orchestration** (shell→Git-Bash/WSL or PowerShell; process-kill; settings-backup): **~1 session.** Lowest-risk, unblocks everything.
- **`OPENRA_WINDOW_HIDDEN` engine flag + determinism verification**: **~1 session** (incl. one run: same seed hidden vs windowed → identical verdict).
- **Widen verdict JSON to full stats menu**: **~0.5 session.**
- **TELEMETRY per-tick channel (economy/army/POI timeseries)**: **~1–1.5 sessions** (build-on-first-use; POI ownership may need the SR capture→Neutral wiring finished first).
- **Parallel runner (Phase 3)**: **~1–2 sessions**, engine reentrancy risk (isolated profile dirs).

**Total to a solid Windows headless benchmark loop with rich end-of-match metrics: ~3–4 focused sessions,** most of it low-risk reuse.

## Single riskiest assumption

**That a hidden/off-screen SDL window actually runs the simulation to completion on Windows without a mapped GL surface — and without perturbing determinism.** OpenRA's sim is *supposed* to be render-independent, but `PITFALLS §17` flags exactly this ("real risk of breaking simulation determinism") as why a null renderer was avoided. `SDL_WINDOW_HIDDEN` keeps a real GL context (unlike a full null platform), making breakage *unlikely* — but it's unverified on this codebase + Windows. **Retire this first** with a single bounded run: launch one existing tournament scenario with the hidden flag and confirm (a) no window/focus, (b) a verdict is written, (c) it matches the windowed run for the same seed. Everything else is low-risk reuse; this one assumption gates the whole "unsupervised on Windows" premise.

## Note on method

No game run was spent — code reading answered every question conclusively. The one place a run adds value is retiring the riskiest assumption above, which requires the ~10-line engine change first (implementation, not research).
