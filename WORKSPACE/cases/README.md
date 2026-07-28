# CASES — user-authored scenarios with measurable acceptance

A **case** is the unit of autonomous work under the scenario-case model (adopted 2026-07-26, after the first autoburn retrospective): the user paints a situation and the outcome they want to see in game; the agent formalizes it into a scenario with a **single measurable bar**, then iterates — feature work, tuning, whatever it takes — until the bar reads GREEN.

## Why this exists (retrospective conclusion, 2026-07-26)

The first autoburn window (~2026-07-20 → 07-25) shipped well-reviewed work at high throughput, but **outcome measurement fell away**: later bot changes (influence Stages 0/A–F, ambush Stages 1–4) were verified by build/NUnit/scenario-logic, not by "did the bot actually get better" numbers. Ten tracks piled up in needs_review because acceptance was subjective ("play and tell me how it felt"). Cases fix both: a case gives autoburn a **self-verifiable win condition** and converts review-debt into a number the user can spot-check.

## Case file format

One file per case: `case-NN-<slug>.md`, containing:

- **Intent** — the user's own description of the situation and desired outcome. Preserve their wording; it is the authority when formalization drifts.
- **Setup** — the concrete scenario: map, forces, orders, what's scripted vs bot-driven. Prefer scripting the side NOT under test (isolate the variable).
- **Bar** — ONE measurable acceptance criterion, with a **ratification status**. Bars start `PROVISIONAL` (the user's first instinct, e.g. "3× casualties"); a calibration batch measures reality; then the bar is `RATIFIED` (possibly adjusted) before it gates anything. Cost-weighted losses over N aggregated runs is the default currency (same discipline as the tournament S-rung bars).
- **Dependencies** — pipeline items / features / recon that must land before the case is even buildable.
- **Status log** — dated entries, newest first: `RED`/`GREEN` + measured value + main SHA + scenario/seed refs. Never edit old entries.

## Lifecycle

`DRAFT` (intent captured, not yet buildable) → `BUILDABLE` (dependencies landed, scenario authored) → `CALIBRATING` (bar provisional, measuring) → `ACTIVE` (bar ratified — this is the state autoburn iterates against) → `GREEN` (bar met at a recorded SHA; regressions reopen it).

## Hard constraints (inherited, non-negotiable)

- **Measurement runs are user-gated.** The no-autonomous-multi-test rule (CLAUDE.md) applies to case batches exactly as to tournaments. A case cannot progress past CALIBRATING without an explicit grant in the current turn — or a **standing scoped grant** if the user issues one (e.g. "case-scenario batches are pre-approved during autoburn"). Managers: surface this dependency early, never assume it.
- Scenario authoring follows `DOCS/recipes/AUTOTEST.md`; the harness is `run-test.sh` / `run-batch.sh` (`--hidden` profile for batches, `--seed` for reproducibility).
- Zero-RNG / byte-identity invariants (`DOCS/reference/influence-stack.md` §Invariants) bind any engine change a case motivates.

## Index

| Case | Title | State |
|---|---|---|
| [case-01](case-01-forest-ambush.md) | Forest ambush — defenders win 3:1 from concealment | BUILDABLE |
