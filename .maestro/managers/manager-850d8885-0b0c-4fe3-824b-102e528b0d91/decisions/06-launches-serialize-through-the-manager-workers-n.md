# Launches serialize through the manager; workers never start the game

_Recorded 2026-08-19T17:00:19.475Z by 17dc66e4_

## The change

2026-08-19. The user granted **full simulation/launch grants** — the long-standing "no autonomous multi-test runs / ask for a slot" bottleneck is lifted — but attached a hard operational constraint:

> "You have full grants to launch simulations but I suggest you do it from here so that multiple workers are not all starting simulations. That will crash my computer. So keep an eye on the load and make sure you dont completely overload the machine."

**The manager is now the only party that launches anything.** Workers must not run `launch-game.sh`, `run-test.sh`, `run-batch.sh`, `run-tournament.sh`, or any screenshot capture. They write down what they need run and hand it up; the manager runs it serially and feeds results back.

## Alternatives considered

- **Let workers launch, cap concurrency by convention** — rejected. Convention has already failed repeatedly in this project on cheaper matters; a worker that launches concurrently costs the user a machine crash, which is unrecoverable from the agent's side.
- **A lock file / semaphore in the repo** — rejected for now. Real, but it is engineering effort spent on the harness during a window meant for product work, and it does not remove the need to brief workers anyway. Revisit only if serialization through the manager proves to be the bottleneck.
- **Manager launches, workers request** — CHOSEN. One launch at a time, the manager holds the queue, machine load is observable from one place.

## Consequences to carry

- **Every worker brief from now on must carry the no-launch clause.** It contradicts `DOCS/recipes/AUTOTEST.md` and `SCREENSHOT.md`, which tell workers the test-driven and capture loops apply by default — so the brief has to override them explicitly or the worker will follow the recipe.
- The implement→verify loop is now split across two parties: worker writes the scenario, manager runs it, worker reads the result. Briefs should ask for the scenario file plus an explicit "what would count as the answer" so the manager can run it without re-deriving intent.
- The old worktree-build rule inverts in part: a fresh worktree still needs `make all` to compile-check, but no longer needs it "before the first launch", because the worker never launches. The manager's own launch must come from a tree that IS built.
- **Machine load is now a manager responsibility.** One launch at a time, and keep an eye on concurrent worker count while a launch is running.

## Second ruling in the same answer

Plan weighting is **legibility first**: make existing mechanics visible and understandable before changing them. Applies across all three research areas. Mechanic changes are not forbidden — they rank below, and are called out separately.

Also: *"you have 7h so I expect you to move on pretty soon"* — the research-only phase is closing. Read as a goahead to begin implementing the legibility work once the reports land, without a further sign-off gate for low-risk UI/indicator work.
