# Session — AI benchmark substrate: hidden-window flag + widened verdict

Started: 2026-07-19 15:50
Mode: EXPERIMENTAL
Context: WORKSPACE/DISCOVERIES.md (2026-07-19 substrate entry),
         WORKSPACE/plans/260719_ai_benchmark_substrate_findings.md,
         WORKSPACE/plans/260511_ai_tournament_harness.md

## Deliverables (both shipped)

**D1 — `OPENRA_WINDOW_HIDDEN=1` engine flag.** `Sdl2PlatformWindow.cs`: OR
`SDL_WINDOW_HIDDEN` into window creation flags when the env var is `1`. A
hidden SDL window is never mapped and never receives focus. Guarded every
downstream re-show/raise path under the flag: mouse-focus grab (`GrabWindowMouseFocus`),
Fullscreen + PseudoFullscreen application, and the existing `OPENRA_WINDOW_MINIMIZED`
handler. Pure opt-in; zero behavior change when unset. Commit `d716eade`.
Runtime window-raise paths elsewhere are settings-UI only (never reached in a
headless bot run), so creation-time guards suffice.

**D2 — widened tournament verdict JSON.** `BotVsBotMatchWatcher.SerializeVerdict`:
bumped `verdict_version` 1→2 (additive, schema-stable) and added a per-player
`stats` object: units_killed, units_dead, buildings_killed, buildings_dead,
kills_cost, deaths_cost, army_value, assets_value, order_count, experience,
resources_earned (from `PlayerResources.Earned`, cumulative — PITFALL §14).
Existing fields/shape unchanged. Commit `7fd793d8`.

## Build + tests
- `./make.ps1 all` — green (engine + mod).
- NUnit full suite — **261/261 pass, 0 fail** (baseline note said 243; tree is ahead).

## Verification (2 bounded runs, same seed 1017, scenario tournament-arena-diagonal-2p / tournament-smoke.yaml, 8× speed)
Ran via a self-contained PowerShell harness replicating run-tournament.sh's exact
launch args (did NOT edit any tools/autotest/*.sh). Control = windowed, Hidden =
`OPENRA_WINDOW_HIDDEN=1`.

| Signal | Control (windowed) | Hidden |
|---|---|---|
| Visible window (proc MainWindowHandle) | yes, title "OpenRA" | **none** |
| Stole foreground focus | no | no |
| Completed + wrote verdict JSON | yes | yes |
| verdict_version | 2 | 2 |
| `stats` object present | yes | yes |
| duration_ticks | 750 | 750 |

**(a) no visible window + (b) verdict written = both confirmed for hidden run.**

**(c) verdicts NOT identical** — winner/scores/order_count differ (control:
Russia wins 4300 vs 4050; hidden: USA wins 4650 vs 2850). Divergence begins in
the first 125 ticks from identical initial state.

### Root cause (static, definitive — no 3rd run needed): pre-existing AI nondeterminism, NOT the hidden flag
- `World.cs:213` seeds `SharedRandom` from `RandomSeed` (deterministic, synced).
  `World.cs:214` creates `LocalRandom = new MersenneTwister()` **unseeded**.
- The bots use `world.LocalRandom` pervasively for *decisions*, notably
  `UnitBuilderBotModule.cs:173/188` (which unit to call in) and squad/scan
  modules (LayeredDefence, HelicopterSquad, BaseBuilder, Minelayer, SupportPower).
  Unseeded → different unit picks every run → army_value diverges from the first
  call-ins. Exactly the observed tick-125 split.
- The hidden flag only touches SDL window creation + rendering, which is
  decoupled from the lockstep sim and cannot influence `LocalRandom`. The
  deterministic machinery that IS honored matches both runs (tick count /
  time-limit = 750; SharedRandom-seeded sim). Two *windowed* runs would diverge
  identically.

**Implication for the substrate:** the hidden-window determinism assumption is
retired — hidden runs do NOT alter the sim. But per-seed *reproducibility* of the
tournament does not hold today because the AI bypasses the seed via `LocalRandom`.
Fix is separable: seed `LocalRandom` from `RandomSeed` under Test.Mode (or route
AI decision randomness through `SharedRandom`). Recorded in DISCOVERIES.

## Notes for the parallel worker (script portability)
Did not touch tools/autotest/*.sh. Observed run-test.sh / run-tournament.sh /
loop-tournament.sh already modified in the tree by that worker; left untouched.
