# AI/Bots Project — Home

> Project: "the best bots the OpenRA community has ever seen, rivaling top-tier games — within a feasible scope."
> Started: 2026-05-11. Owner: FreadyFish + Claude (rotating).

This folder holds **living docs** for the AI overhaul. The C# lives in
`engine/OpenRA.Mods.Common/BotModules/` (existing executors) and
`engine/OpenRA.Mods.Common/AI/` (new brain layer, created in Phase 1).

## Read first

- [`morning_summary_260512.md`](morning_summary_260512.md) — **READ FIRST IF JUST WAKING UP.** Live log of the autonomous overnight run; what was tried, what worked, what's still rough, recommended next moves.
- [`phase1_status_260511.md`](phase1_status_260511.md) — Phase 1 status snapshot of the tournament harness (the foundation for measuring AI work).
- [`foundation_260511.md`](foundation_260511.md) — survey of modern RTS AI techniques, WW3MOD-specific constraints, three-layer architecture, phasing. **The basics doc.** Read before any planning.
- [`../plans/260511_ai_tournament_harness.md`](../plans/260511_ai_tournament_harness.md) — AI-vs-AI tournament harness plan. **Lands before any new-brain code** so we can measure every change. Dual `ModularBot@legacy`/`@v2` in one binary, hybrid score-or-SR-capture win rule, headless + parallel runner, milestone-driven autonomous loop.

## Operational references

- [`tournament_swap_guide.md`](tournament_swap_guide.md) — how to swap any piece of the tournament harness (scorer, win rule, scenario, runner). Every modular point + the recipe to replace it.
- [`PITFALLS.md`](PITFALLS.md) — traps already hit during implementation. Read before touching the harness; this saves hours.

## Mandatory references

- [`../../DOCS/reference/supply-route.md`](../../DOCS/reference/supply-route.md) — **Read before writing AI/strategic code that mentions Supply Routes.** SRs are fixed sector beachheads near each player's spawn edge, not buildable factories. Misunderstanding this is the recurring trap.
- [`../../DOCS/reference/economy.md`](../../DOCS/reference/economy.md) — cash, ammo, supply pipeline.

## Reference (prior work)

- [`../archive/sessions/260321_ai_strategy.md`](../archive/sessions/260321_ai_strategy.md) — the 260321 strategy. Tiers 0–3.1 shipped; foundation is shallow but real.
- `engine/OpenRA.Mods.Common/Traits/BotModules/` — current bot module surface (~6.1k LOC).
- `mods/ww3mod/rules/ai/ai*.yaml` — current AI config (594 lines).

## Status

**Tournament harness Phase 1 + Rounds 2-9 complete (260511 + overnight 260512).**

- Engine plumbing: BotVsBotMatchWatcher + IMatchScorer/IWinRuleEvaluator plug-ins, dual ModularBot@normal/@v2 YAML, Test.* launch args for tournament config / game speed / deterministic seed / speed multiplier.
- Shell harness: run-tournament.sh, aggregate-tournament.sh, loop-tournament.sh (scaffold).
- Two tournament scenarios: arena-skirmish-2p (mid-row SRs), arena-diagonal-2p (corner SRs).
- Score formula: army_value + capture_income + kills_value via PlayerStatistics.
- Speed: ~3× practical wall-clock improvement (8× SpeedMultiplier + framerate cap).
- Sanity check: 20-seed legacy-vs-legacy batch running. Findings in morning summary.

**Not yet started:** Phase 2 (real headless renderer), Phase 4 (loop's metric-eval / milestone-bell logic), the AI overhaul itself (per `foundation_260511.md`). Phase 4 loop scaffold exists; eval logic is documented TODO.

## Workspace conventions

- New docs go here named `<topic>_<YYMMDD>.md` (e.g. `mapanalysis_260520.md`).
- One-off design questions can live in this folder; multi-session implementation plans go under `WORKSPACE/plans/` as usual and link back here.
- Update the **Status** block above whenever a phase changes state.
- Don't duplicate `RELEASE_V1.md` — when a phase is committed to v1, add a one-liner under "AI overhaul" there and link to the relevant phase doc here.
