# AI/Bots Project — Home

> Project: "the best bots the OpenRA community has ever seen, rivaling top-tier games — within a feasible scope."
> Started: 2026-05-11. Owner: FreadyFish + Claude (rotating).

This folder holds **living docs** for the AI overhaul. The C# lives in
`engine/OpenRA.Mods.Common/BotModules/` (existing executors) and
`engine/OpenRA.Mods.Common/AI/` (new brain layer, created in Phase 1).

## Read first

- [`NEXT_STEPS.md`](NEXT_STEPS.md) — **READ FIRST.** For a fresh agent told "continue working on the bots": this is the entry point. Required reading order + first two concrete steps + standard A/B loop + things not to redo.
- [`WAKEUP_CHECKLIST_260512.md`](WAKEUP_CHECKLIST_260512.md) — quick orientation for someone who already knows the project context.
- [`morning_summary_260512.md`](morning_summary_260512.md) — Live log of the autonomous overnight run; what was tried, what worked.
- [`sanity_findings_260512.md`](sanity_findings_260512.md) — Statistical findings from the sanity batches. Authoritative baseline: russia 60% / america 40% at n=20 mirror-paired (mild, borderline-significant).
- [`foundation_260511.md`](foundation_260511.md) — survey of modern RTS AI techniques, WW3MOD-specific constraints, three-layer architecture, phasing. **The basics doc.** Read before any planning.
- [`../plans/260511_ai_tournament_harness.md`](../plans/260511_ai_tournament_harness.md) — AI-vs-AI tournament harness plan.

## Operational references

- [`tournament_workflow.md`](tournament_workflow.md) — **Usage cookbook.** "How do I run a smoke test / full batch / mirror-paired benchmark / autonomous loop?" One bash command per recipe.
- [`tournament_swap_guide.md`](tournament_swap_guide.md) — how to swap any piece of the harness (scorer, win rule, scenario, runner). Every modular point + the recipe to replace it.
- [`PITFALLS.md`](PITFALLS.md) — 18 traps already hit during implementation. Read before touching the harness; this saves hours.
- [`phase1_status_260511.md`](phase1_status_260511.md) — Phase 1 status snapshot (slightly stale; morning_summary is the current truth).

## Mandatory references

- [`../../DOCS/reference/supply-route.md`](../../DOCS/reference/supply-route.md) — **Read before writing AI/strategic code that mentions Supply Routes.** SRs are fixed sector beachheads near each player's spawn edge, not buildable factories. Misunderstanding this is the recurring trap.
- [`../../DOCS/reference/economy.md`](../../DOCS/reference/economy.md) — cash, ammo, supply pipeline.

## Reference (prior work)

- [`../archive/sessions/260321_ai_strategy.md`](../archive/sessions/260321_ai_strategy.md) — the 260321 strategy. Tiers 0–3.1 shipped; foundation is shallow but real.
- `engine/OpenRA.Mods.Common/Traits/BotModules/` — current bot module surface (~6.1k LOC).
- `mods/ww3mod/rules/ai/ai*.yaml` — current AI config (594 lines).

## Status

**Tournament harness Phase 1 + Rounds 2-16 complete (260511 + overnight 260512).**

- Engine plumbing: BotVsBotMatchWatcher + IMatchScorer/IWinRuleEvaluator plug-ins, dual ModularBot@normal/@v2 YAML.
- Test.* launch args: TournamentConfig, GameSpeed, RandomSeed, SpeedMultiplier (Rounds 1, 3, 5, 5).
- Shell harness: run-tournament.sh, aggregate-tournament.sh, loop-tournament.sh (Phase 4 v2 stop-condition + bell), compare-batches.sh, tournament-report.sh.
- Three tournament scenarios: arena-skirmish-2p, arena-diagonal-2p, arena-mirror-2p (factions swapped).
- Score formula: army_value + capture_income (PlayerResources.Earned) + kills_value (PlayerStatistics.KillsCost).
- Per-player faction in verdict JSON (Round 15) → faction_winrate_pct in summary.json.
- Speed: ~3× practical wall-clock improvement (8× SpeedMultiplier + Graphics.MaxFramerate=5 cap).
- Sanity check at n=19 clean-CPU: USA-bot 84.2% / Russia-bot 15.8% — strong bias signal.
- Mirror-paired batch in progress: separates faction vs position bias.

**Not yet started:** Phase 2 (real headless renderer — would unlock >3× speedup but days of work), the AI overhaul itself (per `foundation_260511.md`). The harness is functional and ready for measuring real AI changes.

## Workspace conventions

- New docs go here named `<topic>_<YYMMDD>.md` (e.g. `mapanalysis_260520.md`).
- One-off design questions can live in this folder; multi-session implementation plans go under `WORKSPACE/plans/` as usual and link back here.
- Update the **Status** block above whenever a phase changes state.
- Don't duplicate `RELEASE_V1.md` — when a phase is committed to v1, add a one-liner under "AI overhaul" there and link to the relevant phase doc here.
