# RECIPES

Workflows I (Claude) follow when triggered. Each entry has its own `<NAME>.md` with the trigger phrase, when to use it, and the procedure.

The point: instead of re-explaining a workflow each session, the user types the trigger word and I follow the documented steps. Humans skim this index to remember what's available.

> **Note for the agent.** These are project-convention docs, **not** harness-registered Skills. Any step in a recipe that starts the game or runs the YAML lint follows CLAUDE.md §"Who runs what". When the user says the trigger word (with or without a `/`), READ the relevant `.md` here and follow the procedure. Never call the `Skill` tool for these — that's a different system and the call will fail.

| Trigger | Recipe | One-liner |
|---|---|---|
| [`PLAN <topic>`](PLAN.md) | PLAN | Design a feature or change before coding — ask, research, write a plan doc, wait for approval |
| [`PLAYTEST [topic]`](PLAYTEST.md) | PLAYTEST | Build, write a focus brief, hand back to user with eye-list. Pair with TRIAGE after |
| [`TRIAGE [findings]`](TRIAGE.md) | TRIAGE | Sort raw findings, route to RELEASE_V1.md (scope) / PIPELINE.md (queue) / BACKLOG / discovered.md |
| [`AUTOTEST <bug>`](AUTOTEST.md) | AUTOTEST | Test-driven debug loop — failing test → fix → green → regression-check → commit |
| [`DEMO <topic>`](DEMO.md) | DEMO | Stage a scenario for the human to look at. Same harness as AUTOTEST, but no verdict, no autonomous loop |
| [`REVIEW [N]`](REVIEW.md) | REVIEW | Quality pass on last N commits — pitfalls, leftover traces, over-engineering |
| [`FINALIZE`](FINALIZE.md) | FINALIZE | Session wrap-up — bell, trackers, hotboard, plan archive, commit |
| [`CONTEXT <area>`](CONTEXT.md) | CONTEXT | Quick orientation on an area — recent commits, open work, file pointers |
| [`BALANCE <a> <b>`](BALANCE.md) | BALANCE | Wrap combat-sim for data-driven tuning — duel matrices, tier consistency |
| [`TELEMETRY <events>`](TELEMETRY.md) | TELEMETRY | Turn on the JSONL event channels (unit lifecycle, missile trace) for post-mortem analysis |
| [`SCREENSHOT <topic>`](SCREENSHOT.md) | SCREENSHOT | Capture game state as PNGs (in-test or menu/lobby) and evaluate visually via the multimodal Read tool |
| [`DOCUMENT <topic>`](DOCUMENT.md) | DOCUMENT | Write/update a game-mechanic description in [`../gameplay/`](../gameplay/README.md) — player perspective, not technical. Always-on for discoveries |

For operating modes (RELEASE, EXPERIMENTAL) see [`../modes/`](../modes/README.md).

## Conventions

- One file per recipe, named `<NAME>.md` (UPPERCASE, matches the trigger).
- First section: trigger phrase + one-paragraph "gives you" + one-paragraph "when *not* to use it".
- Shorter is better — focused workflow, not exhaustive reference.
- Add new entries to the table above as recipes land.
- If a workflow is one-shot or trivial, it doesn't need a recipe doc — just do it.

## Adding a new recipe

1. Create `DOCS/recipes/<NAME>.md` following the format of an existing recipe.
2. Add a row to the table above.
3. CLAUDE.md's routing table already points trigger words at this README; add a routing row there only if the recipe also applies without a trigger word.
