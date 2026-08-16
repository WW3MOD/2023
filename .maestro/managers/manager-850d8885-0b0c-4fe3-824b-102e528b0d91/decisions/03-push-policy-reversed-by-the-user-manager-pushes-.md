# Push policy reversed by the user — manager pushes verified merges, workers still never push

_Recorded 2026-08-11T20:45:33.076Z by 17dc66e4_

## The instruction

User, 2026-08-11: *"Push everything when it is finished going forward, I will be testing from a different computer."*

## Why this needs a decision record rather than just doing it

It overrides two things, one of them recently and explicitly litigated:

1. `CLAUDE.md`'s hard rule **"NEVER push to remote. The user pushes manually."** — read by every worker on every task.
2. The HOTBOARD entry of 2026-08-06, which resolved a direct conflict between a hand-off note advocating continuous push and `CLAUDE.md`, and **ruled for `CLAUDE.md`**: "the user pushes manually, no agent ever pushes." That note explicitly warns a future manager not to revive the continuous-push directive.

Silently pushing against a rule in that state would be indistinguishable from an agent ignoring its instructions. The user has now reversed it themselves, with a concrete reason — they are testing from another machine, so work that sits unpushed is work they cannot reach.

## What changed and what did NOT

**Changed:** the manager pushes `main` to origin once a merge is verified.

**Unchanged, and deliberately so:** workers still never push. That half was never about protecting the remote from the user's own agents in general — it is what guarantees nothing reaches origin without passing through a merge gate where the build and the test suite are checked. Keeping it means the new policy costs no safety.

**The gate is explicit: build clean and NUnit green, then push. Never before.** A push of a broken tree is worse than no push at all now that the user is pulling it onto a machine they will test from and where a failure is far more expensive to diagnose.

## Also worth noting

This machine already diverged from origin once tonight — the other machine pushed 8 commits (the net-stack desync-guard line) while this one worked, and the push was rejected until it was merged. Pushing promptly as work lands makes that divergence smaller and more frequent rather than large and rare, which is the better failure mode. The standing obligation to state plainly, at the end of any turn that lands a merge, whether `main` is ahead of origin becomes less load-bearing but should be kept — it is now the tell that a push failed rather than that one is pending.

`CLAUDE.md` updated in place with the supersession recorded inline rather than the old rule deleted, so the reversal is visible to anyone who remembers the old one.
