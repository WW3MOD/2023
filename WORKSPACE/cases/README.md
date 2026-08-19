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

## A case whose scenario cannot fail is not a case

Learned expensively on case-01, which spent three weeks calling `Test.Pass` unconditionally (`WORKSPACE/audit/260819-case-corpus-audit.md` §3). The whole reason cases are the preferred unit of autonomous work is that a green bar self-certifies — which holds only while the bar can go red. Two rules follow, and both are cheap:

- **A bar stated as an aggregate needs a checker at that level.** "Mean X over ≥6 seeds" is not decidable from one run, and collapsing it into a per-run threshold silently authors a *stricter, different* bar — on case-01 the per-seed version would have failed the very batch the bar was mined from. Assert the per-seed clauses in the scenario, the aggregate clauses in a parser over the batch (`tools/autotest/parse-case01-bar.py`, `parse-s2-bar.py`). Make the parser report an under-sized or seed-duplicated batch as UNEVALUABLE, never as a pass.
- **Check setup validity before the bar, or a zero means nothing.** Most case bars are floors on losses, and a world that never happened produces the same numbers as a world the defenders won. Assert the mechanism ran and the fight occurred *first*, then assert the bar — and certify it with a sabotage run per `DOCS/recipes/AUTOTEST.md`, confirming the **specific** failure text rather than merely that something failed.

## Lifecycle

`DRAFT` (intent captured, not yet buildable) → `BUILDABLE` (dependencies landed, scenario authored) → `CALIBRATING` (bar provisional, measuring) → `ACTIVE` (bar ratified — this is the state autoburn iterates against) → `GREEN` (bar met at a recorded SHA; regressions reopen it).

## Hard constraints (inherited, non-negotiable)

- **Measurement runs are user-gated.** The no-autonomous-multi-test rule (CLAUDE.md) applies to case batches exactly as to tournaments. A case cannot progress past CALIBRATING without an explicit grant in the current turn — or a **standing scoped grant** if the user issues one (e.g. "case-scenario batches are pre-approved during autoburn"). Managers: surface this dependency early, never assume it.
- Scenario authoring follows `DOCS/recipes/AUTOTEST.md`; the harness is `run-test.sh` / `run-batch.sh` (`--hidden` profile for batches, `--seed` for reproducibility).
- Zero-RNG / byte-identity invariants (`DOCS/reference/influence-stack.md` §Invariants) bind any engine change a case motivates.

## Index

| Case | Title | State |
|---|---|---|
| [case-01](case-01-forest-ambush.md) | Forest ambush — defenders win 3:1 from concealment | CALIBRATING |
