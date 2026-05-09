# SKILLS

Reusable workflows I (Claude) know how to run when triggered. Each skill has its own `<NAME>.md` with the trigger phrase, when to use it, and what it does in detail.

The point: instead of explaining the workflow to me every time, you say the trigger word and I follow the documented steps. Humans can also skim the index here to remember what's available.

## Available skills

| Skill | Trigger | One-liner |
|---|---|---|
| [AUTOTEST](AUTOTEST.md) | `AUTOTEST <bug or feature>` | Test-driven debug loop. Write failing auto-test, fix, verify GREEN, regression-check, commit. You walk away; verdict comes back as JSON exit code. Best for behavioral / deterministic bugs. |

## Conventions

- One file per skill, named `<NAME>.md` (UPPERCASE, matches the trigger).
- First section of every skill doc: trigger phrase + one-paragraph "what it gives you" + one-paragraph "when *not* to use it".
- Add new entries to the table above as skills land.
- Keep skills focused — if a workflow is one-shot or trivial, it doesn't need a skill doc.

## Adding a new skill

1. Create `SKILLS/<NAME>.md` with the trigger / what / when-not framing first.
2. Add a row to the table above.
3. If the skill should also surface in CLAUDE.md's `## Commands` section (for visibility on first read), mention it there with a one-liner that points back here.
