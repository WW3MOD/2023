# DOCS

Static project documentation. Four kinds:

- [`modes/`](modes/) — operating modes (RELEASE, EXPERIMENTAL). One in effect at a time; sets the stance for the session. Index in [`modes/README.md`](modes/README.md).
- [`recipes/`](recipes/) — workflow triggers (AUTOTEST, PLAN, DEMO, DOCUMENT, …). The agent follows these when the user types the trigger phrase. Index in [`recipes/README.md`](recipes/README.md).
- [`reference/`](reference/) — system architecture, game model, balance, project assessment. Engineering-side, read on demand when working on a specific area.
- [`gameplay/`](gameplay/) — how the game is intended to play, feel, and look, in player vocabulary. Steadily growing as discoveries land. Index in [`gameplay/README.md`](gameplay/README.md).

> Note: `modes/` and `recipes/` are project-convention docs the agent READS — they are not Claude Code's harness-registered Skills. The agent should never call the `Skill` tool for these.

`archive/` holds superseded reference material — read-only, occasionally cleanable.

## Quick map

| Looking for | Go to |
|---|---|
| AI design goal — realistic, doctrine-grounded bot behavior (primary AI goal) | `design/ai-realism.md` |
| How WW3MOD's gameplay model differs from Red Alert (no factories, reinforcements) | `reference/game-model.md` |
| Supply Route canonical mental model | `reference/supply-route.md` |
| Supply/ammo economy details | `reference/economy.md` |
| WDist / WAngle / YAML idioms / engine code rules | `reference/conventions.md` |
| Engine layout / scenario / suppression / aircraft / AI config / LOS shadow cache | `reference/architecture.md` |
| Adding a music track / sound formats / Ogg export settings | `reference/architecture.md` § Audio pipeline |
| The @experimental influence stack (belief store, danger/control fields, danger nav, strategic repoint) | `reference/influence-stack.md` |
| **How the bots work, start to finish** — architecture, a unit's whole life, what does not run, how to spot problems | [`bots/README.md`](bots/README.md) — read that first; `bots/02`–`06` are the technical set behind it |
| How the knowledge bank grows (curation rules) | `reference/README.md` |
| PITFALL comment system (full spec) | `reference/pitfalls.md` |
| Big-picture project state, engine-upgrade assessment | `reference/project-assessment.md` |
| Shadow / firing-LOS roadmap | `reference/shadow-los-plan.md` |
| What workflow runs when I type `AUTOTEST`? | `recipes/AUTOTEST.md` |
| What is RELEASE mode vs EXPERIMENTAL mode? | `modes/README.md` |
| Balance dashboard + combat-sim workflow | `recipes/BALANCE.md` |
| How a game-mechanic is intended to play | `gameplay/<topic>.md` |
| Which assets the repo redistributes, where they came from, what breaks if removed | `WORKSPACE/ASSET-LICENSING.md` |
| Live tracker / current focus / backlog | `WORKSPACE/` (separate top-level folder) |
| Agent instructions | `CLAUDE.md` (root) |
