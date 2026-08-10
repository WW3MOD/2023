# Loop standup: dedicated ai-bench manager on autoburn

_Recorded 2026-07-19T17:20:25.149Z by ee31feaf_

User resolved the loop-standup fork: a DEDICATED manager (spawned by root, attached to the ai-bench worktree, charter seeded from SPEC.md) runs the autonomous optimization loop on autoburn; this manager stays coordinator for exp-ai-poi and other work.

Alternatives considered: this manager running the loop itself on autoburn (rejected — shares finite context with two existing tracks and their handoff cadence); holding for a user spec review first (rejected — user chose to proceed).

Consequences:
- This manager cannot spawn orchestrators; root/user executes the spawn. Spawn packet delivered via file_recommendation + track note.
- Autoburn is granted by the user directly to the NEW manager — it cannot self-start the loop.
- Run-slot handover: this manager stops dispatching game-running workers to the ai-bench worktree once the in-flight LC-delist fix worker (e288ce7f) completes; the loop manager owns the machine's single game-run slot thereafter.
- The loop's first cycle should start from `git log` on ai-bench — the LC delist + S1 verify may already be landed by then.
