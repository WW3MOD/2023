# Worktree removal becomes part of the merge routine, not an occasional cleanup

_Recorded 2026-08-30T10:18:16.442Z by 17dc66e4_

## What happened

On 2026-08-30 the machine's data volume hit 100% (2.1 GB free of 466 GB). Two workers — `25bf124c` (lobby chip) and `cc6a3687` (heli fixes) — independently stopped mid-task because `make all` could not run, and both correctly escalated rather than deleting directories they did not own.

Cause: **209 git worktrees under `~/worktrees/ww3mod`, totalling 164 GB.** Essentially every worker dispatched over the past weeks created one, and none were ever removed. Each carries its own `engine/bin` build output, which is what makes them enormous.

## The decision

**Removing the worktree is part of merging the branch, in the same manager turn.** Not a periodic sweep, not a backlog item — the merge is not finished until the worktree is gone. This is the same discipline already mandated for archiving finished child sessions, and it failed for the same reason: the work felt done at merge, so the cleanup step had no forcing function.

## Why the workers could not decide it themselves

Both proposed the identical, well-reasoned filter: remove worktrees whose branch is an ancestor of `main` and whose `git status` is clean. **That filter would have destroyed live work.** Five workers were holding worktrees at that moment, and a worktree created twenty minutes earlier by an active session is merged-and-clean exactly like a dead one. The distinguishing fact — which sessions are alive — exists only at the manager. Neither worker had any way to price the residual risk it correctly identified.

This generalises: a safety check a worker cannot run is a decision the worker must not make. Both escalated; that was right, and the answer they got was "wait", not "go ahead".

## How the sweep was made safe

- The four live worktrees (`shockwave`, `heli-doctrine`, `frozen-enter`, `medic-follow`) plus `lobby-chip` explicitly excluded by name.
- The main checkout and `WW3MOD-lobby` (a user branch, not agent work) excluded.
- **No `--force`.** `git worktree remove` refuses a worktree with uncommitted changes, so dirty trees survive rather than being destroyed.
- Branch refs are unaffected either way: removing a worktree deletes the working directory, not the branch. Every commit on every removed worktree's branch is still in the repository, and any of them is one `git worktree add` from being restored.

## Consequence to accept

Restoring a removed worktree requires a rebuild, because `engine/bin` is not in git. That is the correct trade — a rebuild is minutes, and 164 GB of stale build output is what stopped six workers at once.
