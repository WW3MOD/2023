# Gameplay documentation

> **How WW3MOD is intended to play** — written from the player's perspective, with the rules backing it up.

This folder is the **documentation** project: a steadily-growing set of `.md` files that describe how each mechanic in WW3MOD works, what it looks like in a match, and why it was designed that way. Each file is one topic.

Two audiences:

1. **A curious player** opening a doc to learn how the game works.
2. **A future agent** picking up work and needing to make game-aware decisions without grepping the code from scratch.

It is intentionally *not* a technical reference (that's `DOCS/reference/`), a balance spreadsheet (that's `WORKSPACE/balancing/`), or a tracker (that's `WORKSPACE/`).

The workflow for adding/updating docs is in [`../recipes/DOCUMENT.md`](../recipes/DOCUMENT.md).

## Index

| Topic | Covers |
|---|---|
| [`capturing.md`](capturing.md) | What can be captured, who captures (Technician, not Engineer in WW3MOD), capture timing, contestation, defending captured structures |

## Adjacent docs that are partly gameplay

These live in `DOCS/reference/` for historical reasons but also describe player-facing mechanics. They may migrate here later.

- [`../reference/supply-route.md`](../reference/supply-route.md) — the Supply Route as sector beachhead. The canonical mental model.
- [`../reference/economy.md`](../reference/economy.md) — supply, batches, drain. Player-facing economy.

## Conventions (quick version)

- Player vocabulary first, code names in parens: "Technicians (`tecn`)".
- Numbers cited from YAML or trait settings, with file:line if useful.
- Game-design intent stated alongside the rule.
- Cross-link liberally.
- Flag uncertainty explicitly. Better to say "I'm not sure whether X" than to write a confident wrong fact.

See [`../recipes/DOCUMENT.md`](../recipes/DOCUMENT.md) for the full recipe.
