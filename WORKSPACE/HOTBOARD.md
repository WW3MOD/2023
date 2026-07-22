# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> The ordered roadmap of what's next lives in [`PIPELINE.md`](PIPELINE.md) — the user steers by reordering it.
> Cap ~40 lines. Rotate stale entries out — once shipped or `[T]`, the tracker / commit history tells the story.

## GATE — user in-game test of the current build
Everything below is gated on this. Sit down and confirm the last three shipped things feel right:
- **Heli standoff** — attack helis hold at missile range instead of overflying into AA (Stage 0, `090ad9d0`).
- **/danger overlay** — hold-Space reads green safe / red unsafe / gray unknown (Stage C, `0833b376`).
- **Phase-4a role tasking** — artillery + SHORAD sit *behind* the line, don't drive up to trade (`acc42ad7`).

If any of the three feels wrong, that fix jumps to the top of PIPELINE before new work starts.

## Working on
- **Ambush / undetected-unit behavior — DESIGN shipped, IMPLEMENTATION gated on user review.** Design doc `plans/260722_ambush_undetected_design.md` landed (`1a3f81f1`) — prone-and-hidden Ambush stance holding fire until spotted or springing the trap; 4 open forks await user review (prone semantics, moving-ambush scope, spring-timing doctrine, bot-only vs human-first). Implementation is next once reviewed (PIPELINE #3).
- **Influence stack — Stages D/E/F queued** (gated on the GATE above). D = helicopter air-danger consumer (helis route around AA); E = danger-weighted ground routing (attacks flow around kill zones); F = strategic repoint + territorial balance-of-power revival (revives parked `exp-terr-bias` @ ccd12c98). Design: `plans/260722_influence_stack_design.md`. Stages 0/A/B/C shipped.
- **Autonomous AI-improvement loop (260719–, autoburn).** Benchmark substrate live (`WORKSPACE/ai-bench/`). Governing spec: `plans/260722_strategic_tactical_split_SPEC.md` (3-layer split). Phases 0→4a shipped; Phase-4b role migration (air/capture/production consume `UnitRoleResolver`) queued.

## Queued (not started — see PIPELINE.md for exact order)
- Phase-4b role migration · fires / artillery doctrine cycle · early-game tuning (idle trucks / AA / spread) · EXPAND benchmark maps · AoE cluster targeting · SharedRandom→LocalRandom migration.

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
