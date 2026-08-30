# Hold the fog merge for one adversarial consumer sweep, despite green in-game verification

_Recorded 2026-08-27T00:23:06.593Z by 17dc66e4_

`wt/fog-leak` @ `1944fa11` is gate-green, verified in a running game with real control rungs, and the perf objection raised in decision 12 is now closed by measurement. Every condition the previous generation attached has been met. I held the merge anyway and spawned an adversarial reviewer (`a963fd50`) first.

## Options

**A — Merge and push now.** The user tests from another machine and asked for finished work to land as it ships. The fix removes a six-month regression; every hour it sits unmerged is another hour the game ships with every enemy structure right-clickable through unbroken shroud. Decisions 11 and 12 already litigated the design. The handoff's own instruction was "tell the user when this lands", which presumes landing.

**B — Hold for one read-only review pass, then merge.** Costs ~15–20 minutes and one worker.

**C — Ask the user.** Rejected immediately: this is a routine pipeline step, not a fork the user should own, and asking would be exactly the noise the question-routing rule exists to prevent.

## Chose B

The deciding argument is not doubt about the fix. It is that **the two scenarios prove the wrong thing well.** They prove buildings hide and reveal correctly, which was never really in question once the flag mechanism was restored. What no evidence touches is that for six months every line of code in this repo has been written and tuned against "every building is visible to everyone, always". The bot layer is the sharp end — the `@experimental` fog-respecting AI has been developed across that entire window and *claims* to respect fog; whether it has quietly been leaning on the leak is a question nobody has asked, and it is answerable statically in one pass.

Two further reasons this specific change earns the pass rather than a general excess of caution:

1. **MAESTRO.md mandates a full adversarial reviewer for behaviour/engine changes**, and reserves manager diff-inspection for test-only or byte-identical batches. Both merges earlier today were test-only and correctly took the cheap path. This is the largest-blast-radius engine change in the queue. Skipping review *here*, of all places, would invert the policy exactly where it was written to bite. Two of the day's five worker dispatches produced review findings that changed a merge decision; the reviewer cost is paid for.

2. **The implementer reviewed itself.** `542d1157` did the investigation, the diagnosis, the fix and the verification. That is not a criticism — its work is the best of the day — but a self-review cannot find the assumption the author never questioned, and the specific risk here *is* an unquestioned assumption held repo-wide.

Against A, the asymmetry settles it. The cost of B is twenty minutes. The cost of A being wrong is pushing a game-wide visibility change to a user who is testing on a different computer and would hit the breakage before I did.

## What would have made me choose A

If the change were reversible in place — but it is not cleanly: `97935007` and `e0afecd9` are an atomic pair, and the short-circuit removal alone makes every building in the game permanently invisible, so a partial revert is worse than either state. That coupling is precisely why the merge wants a second pair of eyes before it goes to origin rather than after.

## Standing note for whoever holds this next

Do not let the review become a re-litigation of the design. Decisions 11 and 12 settled that: minimal repair, do NOT finish the id-based dirty-tracking design. The reviewer's brief scopes it to the consumer sweep, the dead-code premise, the perf argument, and the husk comment. If it comes back arguing the architecture, that is out of scope and the merge should not wait on it.
