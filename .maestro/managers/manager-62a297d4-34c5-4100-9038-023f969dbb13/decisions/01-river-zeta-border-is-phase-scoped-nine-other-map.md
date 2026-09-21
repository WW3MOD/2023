# River Zeta border is phase-scoped; nine other maps deferred

_Recorded 2026-09-15T22:55:11.381Z by 3e6faf7d_

Two calls put to the user 2026-09-16 while scoping the per-map Positioning border. Both answered.

## Scope: River Zeta now, other nine later

Options offered: (a) agent derives a candidate line per map and the user approves from rendered previews; (b) the user marks all ten by hand after the agent builds authoring tooling; (c) build the override mechanism and author River Zeta only.

User chose (c). Consequence to carry: the remaining nine maps keep the runtime-derived border. That derived line is the thing the user objected to on River Zeta, so this is a known-unfixed state on nine maps, not a clean deferral — queued to the repo backlog so it is not mistaken for finished.

Note the authoring METHOD is still unsettled. The user did not reject (a) on its merits; they chose to defer the whole question. A future session must re-ask rather than assume the propose-from-renders workflow was accepted.

## Semantics: the block ends with the phase

The user's original wording — "no one can enter the water or bridges" — is absolute, and could have meant a permanent terrain rule. It does not. User chose "Positioning only — bridges reopen after".

This is load-bearing for implementation: the river-and-bridge region is a rule that READS the phase state, not a terrain or locomotor edit that outlives it. The reasoning the user accepted: water is already impassable to ground units, so the bridges are the whole question, and sealing them permanently would leave River Zeta with no ground crossing at all and plausibly unwinnable for a ground army.

Second consequence, to be handled in the implementer's brief rather than discovered in play: when the phase ends the seal drops instantly, so units may be massed at a bridge mouth the moment it opens. Flagged as a design consequence the user accepted, not a defect.
