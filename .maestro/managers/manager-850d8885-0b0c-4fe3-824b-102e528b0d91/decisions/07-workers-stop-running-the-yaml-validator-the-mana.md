# Workers stop running the YAML validator; the manager runs it once at merge

_Recorded 2026-08-19T19:45:56.788Z by 17dc66e4_

## The problem

At ten concurrent workers, `./utility.sh --check-yaml` became a hard serialization point. Measured evidence, 2026-08-19:

- The bot-naming worker's validator **never got a turn** — its background output file was 0 bytes and it sat idle long enough to trigger a stall warning.
- The cargo-parity worker measured **eight concurrent lint jobs** from other agents' worktrees and reported its own had been waiting **~35 minutes**.

The queue does not drain while the fleet is running, so "wait it out" is not a strategy. Two workers independently blocked on this, and one nearly reported a false stall.

## The rule

**Workers do NOT run `./utility.sh --check-yaml` or `make test`. The manager runs the YAML gate serially at merge time.**

Rationale: the merge gate is the one that actually protects `main`. A worker's local run is redundant with it and its only marginal effect at this fleet size is queue depth. `make all` and `dotnet test` stay with the worker — neither is contended.

Compensating requirement, so nothing is lost: **each worker must list in its report which YAML files it touched and what it would expect lint to say if it got it wrong.** The manager checks the single gate run against those statements. That preserves the worker's intent as a checkable claim rather than discarding it.

## Alternatives considered

- **A lock file / semaphore around the validator** — real, but it is harness engineering during a window meant for product work, and it does not reduce total wall-clock; it only makes the waiting orderly.
- **Fewer workers** — directly contradicts the user's explicit instruction to raise the burn rate to pace 1.0.
- **Let workers wait** — rejected on evidence: 35 minutes of a worker's life for a check the manager repeats anyway.

## Related hazard, restated

**Never `pkill -f OpenRA.Utility`.** With eight concurrent jobs that kills seven siblings' work. Resolve the cwd with `lsof` and kill only your own pid. Both affected workers were told this explicitly.

## Consequence to carry

Merges now arrive YAML-unvalidated by their author. The manager's merge sequence is therefore: merge → `make all` → `dotnet test` → `make test` (YAML gate) → push. A gate failure after merge is now the expected place to catch a MiniYaml slip, which makes reading the gate output properly — and never through a pipe — more load-bearing than before.
