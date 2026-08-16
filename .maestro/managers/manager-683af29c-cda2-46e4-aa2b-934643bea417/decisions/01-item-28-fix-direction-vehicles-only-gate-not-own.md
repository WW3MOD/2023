# Item 28 fix direction: vehicles-only gate, not owning infantry inclusion

_Recorded 2026-07-28T20:26:53.247Z by 6a407ed1_

Adversarial review of `auto/path-string-pulling` @ 71150c8d returned DO-NOT-MERGE: the `!AlwaysTurnInPlace` gate is a no-op (no WW3MOD unit sets it uncommented — infantry.yaml:48 is commented out), so infantry (SharesCell + CanRedirectMidCell) were silently included in string-pulling with zero test coverage, default-ON. Root cause traced to a latent recon error (260728-movement-locomotion.md:22).

Options considered:
- (a) **CHOSEN — exclude infantry properly** (gate on `SharesCell`/`CanRedirectMidCell` predicate), restoring the original PIPELINE item-28 design intent ("gated to … vehicles"). Minimal, matches the reviewed-and-passed safety argument, keeps default-ON scoped to the autotest-covered class.
- (b) Own infantry inclusion: add subcell-formation + mid-cell-redirect autotests and fold infantry into the re-baseline. Rejected for this window: expands scope of a rung-(b) item toward rung-(c) territory, burns game-run budget, and infantry visual zig-zag was never the complaint driving item 28.

Infantry smoothing, if wanted later, is a separate BACKLOG candidate with its own coverage. Reviewer kept idle for re-review of the fix.
