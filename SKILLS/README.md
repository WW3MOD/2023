# SKILLS

Workflows and modes I (Claude) follow when triggered. Each entry has its own `<NAME>.md` with the trigger phrase, when to use it, and the procedure.

The point: instead of re-explaining a workflow each session, the user types the trigger word and I follow the documented steps. Humans skim this index to remember what's available.

## Modes

Operating context. Default is **RELEASE**.

| Trigger | Mode | One-liner |
|---|---|---|
| [`RELEASE`](RELEASE.md) | Release mode (default) | v1 methodology — scope-locked, phase-driven, every commit moves a status |
| [`EXPERIMENTAL`](EXPERIMENTAL.md) | Free exploration | Outside v1 scope — looser, idea-friendly |

## Skills

| Trigger | Skill | One-liner |
|---|---|---|
| [`PLAN <topic>`](PLAN.md) | PLAN | Design a feature or change before coding — ask, research, write a plan doc, wait for approval |
| [`PLAYTEST [topic]`](PLAYTEST.md) | PLAYTEST | Build, write a focus brief, hand back to user with eye-list. Pair with TRIAGE after |
| [`TRIAGE [findings]`](TRIAGE.md) | TRIAGE | Sort raw findings into v1 buckets, route to RELEASE_V1.md / BACKLOG / discovered.md |
| [`AUTOTEST <bug>`](AUTOTEST.md) | AUTOTEST | Test-driven debug loop — failing test → fix → green → regression-check → commit. User walks away |
| [`REVIEW [N]`](REVIEW.md) | REVIEW | Quality pass on last N commits — pitfalls, leftover traces, over-engineering |
| [`FINALIZE`](FINALIZE.md) | FINALIZE | Session wrap-up — bell, tracker, hotboard, session promote, commit |
| [`CONTEXT <area>`](CONTEXT.md) | CONTEXT | Quick orientation on an area — recent commits, open work, file pointers |
| [`BALANCE <a> <b>`](BALANCE.md) | BALANCE | Wrap combat-sim for data-driven tuning — duel matrices, tier consistency |
| [`TELEMETRY <events>`](TELEMETRY.md) | TELEMETRY | Per-tick JSON-line gameplay log channel for post-mortem analysis (not built yet — first invocation builds) |

## Conventions

- One file per skill, named `<NAME>.md` (UPPERCASE, matches the trigger).
- First section: trigger phrase + one-paragraph "gives you" + one-paragraph "when *not* to use it".
- Skills shorter is better — focused workflow, not exhaustive reference.
- Add new entries to the table above as skills land.
- If a workflow is one-shot or trivial, it doesn't need a skill doc — just do it.

## Adding a new skill

1. Create `SKILLS/<NAME>.md` following the format of an existing skill.
2. Add a row to the table above.
3. Mention in `CLAUDE.md` under "Skills" if the user should know about it.
