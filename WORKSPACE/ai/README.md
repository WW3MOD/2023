# AI/Bots Project — Home

> Project: "the best bots the OpenRA community has ever seen, rivaling top-tier games — within a feasible scope."
> Started: 2026-05-11. Owner: FreadyFish + Claude (rotating).

This folder holds **living docs** for the AI overhaul. The C# lives in
`engine/OpenRA.Mods.Common/BotModules/` (existing executors) and
`engine/OpenRA.Mods.Common/AI/` (new brain layer, created in Phase 1).

## Read first

- [`foundation_260511.md`](foundation_260511.md) — survey of modern RTS AI techniques, WW3MOD-specific constraints, three-layer architecture, phasing. **The basics doc.** Read before any planning.

## Reference (prior work)

- [`../archive/sessions/260321_ai_strategy.md`](../archive/sessions/260321_ai_strategy.md) — the 260321 strategy. Tiers 0–3.1 shipped; foundation is shallow but real.
- `engine/OpenRA.Mods.Common/Traits/BotModules/` — current bot module surface (~6.1k LOC).
- `mods/ww3mod/rules/ai/ai*.yaml` — current AI config (594 lines).

## Status

Foundation survey written. Awaiting user pass on phasing and reset-aggressiveness.
No code work has started.

## Workspace conventions

- New docs go here named `<topic>_<YYMMDD>.md` (e.g. `mapanalysis_260520.md`).
- One-off design questions can live in this folder; multi-session implementation plans go under `WORKSPACE/plans/` as usual and link back here.
- Update the **Status** block above whenever a phase changes state.
- Don't duplicate `RELEASE_V1.md` — when a phase is committed to v1, add a one-liner under "AI overhaul" there and link to the relevant phase doc here.
