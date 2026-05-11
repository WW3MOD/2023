# Session: AI Foundation Kickoff

**Date:** 2026-05-11 21:29
**Status:** in-progress (handoff to user for review of foundation doc)
**Task:** Lay out the basics of an AI overhaul project — survey of techniques, WW3MOD-specific constraints, three-layer architecture, phased plan.

## Intended files

- `WORKSPACE/ai/README.md` — project home
- `WORKSPACE/ai/foundation_260511.md` — the basics doc
- This session log

## What's been done

- Surveyed existing bot code (~6.1k LOC across `engine/.../BotModules/`)
- Read prior 260321 AI strategy session (Tiers 0–3.1 shipped)
- Asked user 4 framing questions; got answers
- Wrote foundation doc covering: existing scaffolding, modern AI techniques, WW3MOD-specific constraints, three-layer brain (Perception/Strategy/Tactics) architecture, 5-phase plan with stretch Phase 6
- Open questions left in the doc for the user to chew on

## Next session

- User reviews `foundation_260511.md`, answers open questions in §7
- We re-decide the foundation-reset aggressiveness (the original "Q1" that was deferred)
- Likely first concrete step: Phase 1 scoped to map analyzer + debug overlay (estimate 1–2 sessions)

## Files NOT touched yet

No C# changes. No YAML changes. No engine modifications. Pure design phase.
