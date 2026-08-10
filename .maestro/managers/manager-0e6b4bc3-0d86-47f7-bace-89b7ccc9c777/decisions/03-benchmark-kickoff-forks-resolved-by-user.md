# Benchmark kickoff forks resolved by user

_Recorded 2026-07-19T15:46:37.143Z by ee31feaf_

User answered the 4 benchmark kickoff questions:

1. **Run policy** — pragmatic hybrid. Fix focus-stealing if possible (hidden-window worker in flight), but do NOT gate the project on it: user can run benchmark batches overnight. System design must support both modes (headless when proven; declared night windows otherwise). "Try to solve it but we move on either way."

2. **Mutable scope** — "Anything except unit stats." Widest latitude incl. game-model traits and engine fixes. Hard anti-cheat rule: never alter the engine/game to fit the benchmark (reward hacking); balance numbers/unit stats are off-limits. Non-blocking issues need not be fixed. NEW DELIVERABLE implied: a categorized change-log / overview document targeting a ~1-minute user review, bidirectional — the user writes thoughts in it, the manager continuously reads and clears reviewed items. This review board is a core part of the system, not an afterthought.

3. **Isolation** — dedicated worktree `C:\Users\fredr\worktrees\ww3mod\ai-bench`. Merge to main EARLY and OFTEN: "as soon as it is stable and can be run"; anything assumed an improvement (a few positive test results, soft rule) merges so the user can run from main and watch progress. Not a hard statistical gate for merging — merging is cheap, only crashes are unacceptable.

4. **Advancement thresholds** — statistical vs control + no-regression as the default (median over N runs beats Normal-bot control by per-scenario margin; earlier scenarios re-checked). Explicitly FLUID: manager may adjust per-case autonomously if needed; fixed-number targets, beat-Normal-within-X-minutes, and handicap variants are all acceptable per-scenario tools.

Alternatives considered were the other options in each question (headless-hard-gate, AI-only scope, main-tree, absolute targets / manual sign-off) — user chose latitude + trust + logging over hard walls in every case. Consequence: the spec (task e128b270) is unblocked and must center the review-board doc and the fluid per-scenario threshold spec format.
