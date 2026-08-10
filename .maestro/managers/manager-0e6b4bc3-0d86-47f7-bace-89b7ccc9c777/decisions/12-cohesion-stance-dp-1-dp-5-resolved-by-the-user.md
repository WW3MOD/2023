# Cohesion stance DP-1..DP-5 resolved by the user

_Recorded 2026-07-24T23:43:44.643Z by ee31feaf_

User answered the five decision points from WORKSPACE/cohesion/illustrations/260722_stance_proposals.html (2026-07-25):

- **DP-1 (Tight = column?)** — REDEFINED, not picked from the proposed pair: Tight becomes **exactly original OpenRA** — all cohesion adjustments off, for players who prefer vanilla or want simplicity. The column-of-2 travel identity is dropped from v1.
- **DP-2 (Spread spacing from AoE radius?)** — Rejected as too complex. Use a **sensible hand-tuned constant**.
- **DP-3 (Cover beats line-shape in Loose?)** — YES, definitively: better positioning beats a perfect line every time. Additional signal: the user finds perfect formations unrealistic-looking even absent cover, but does NOT want them degraded on purpose — perfect formations are acceptable for now; realism ideas are welcome.
- **DP-4 (Preview ghosts in scope?)** — Deferred. Hold-space line display is sufficient for now.
- **DP-5 (Ordering)** — Agent's choice. Rider request: keep thinking about how this works in-game so it does not feel disruptive; the user wants more ideas ("let me know, or put it in the pipeline directly") — an ideation pass is dispatched alongside implementation.

Consequences: PIPELINE item 5 ungated (updated + committed); stance identity set is now Tight=classic/off, Loose=cover-first combat interval, Spread=hand-tuned dispersion. Build order chosen by the agent: fix #1 (assignment) → stance identities → leash #4 → travel #3; preview #2 skipped per DP-4.
