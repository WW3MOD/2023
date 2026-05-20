# Autoburn — 260520

Autonomous 4-hour orchestration run kicked off by the user 260520
~17:00 local. Single conductor (this doc's author) spawning ≤10
concurrent workers, each on its own `auto/<topic>` branch in a
dedicated worktree under `/Users/fredrik/Desktop/WW3MOD-<topic>`.

## Goal

User's brief, paraphrased: spend tokens on code quality, performance,
obvious fixes, consistency. Each branch is **for user review** — none
will be merged to main by an agent. The user opens each branch
afterwards, judges, merges what's good, drops what's not.

## How to read this

1. Open the per-branch report under this folder
   (`<branch-name>.md`) — each worker writes one before terminating.
2. Check the branch out with
   `git worktree add /tmp/peek auto/<branch>` (or just
   `git log auto/<branch>` for the commit list).
3. Reports are honest about scope, findings, and uncertainty —
   workers are instructed to flag risky judgements rather than ship
   them.

## Branches dispatched

> Updated by the conductor as workers spawn / terminate. Each branch
> exists ONLY if the worker actually shipped something. Branches with
> nothing useful to commit just terminate without leaving a branch.

(populated as work completes)

## Preserved WIP from before the run

- `auto/preserved-wip-260520` — captured uncommitted changes that were
  sitting on `main` at the start: GroupScatter waypoint refactor,
  river-zeta map tree-thinning, EXPERIMENTAL_NOTES.md. User can
  cherry-pick from this branch if they want any of those back.

## Conductor's own log

Free-form running notes from the conductor — what decisions were made
and why. Per-branch detail lives in the branch report files.

### 17:00 — start
- Cleaned main: preserved 3 uncommitted files on `auto/preserved-wip-260520`.
- Tracking scaffold (this file) committed on main so all branches inherit it.
- Spawning first wave of workers next.
