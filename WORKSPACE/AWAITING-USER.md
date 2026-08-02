# AWAITING-USER — decisions and reviews parked on the user

> **Purpose:** the single place where everything **dependent on the user's decision, review, or grant** is parked so nothing is lost or forgotten. The manager adds items (dated, with context + exactly what is needed); the user resolves them in chat or by editing this file. Resolved items move to the bottom with the decision recorded. Complements `PIPELINE.md` (the work queue) — this file is the *user-gate* queue.

## OPEN — needs a user decision

### Balance / measurement
- **Faction balance audit — proposal sign-off flow** *(2026-08-02)* — Static US-vs-RU parity audit in flight (no game runs). Proposals will land in `WORKSPACE/balance/` as numbered proposal docs, each with: the imbalance evidence, the proposed stat change, expected effect. **Hard rule acknowledged: NO unit-stat changes without explicit user review + approval of each proposal.**
- **Cross-faction + RU-mirror test batches** *(2026-08-02)* — Mirror tests (US/US, RU/RU) isolate bot skill from faction imbalance; US-vs-RU probes measure the imbalance itself. Configs being authored now; **runs need a user grant** (sims deferred until weekly budget burned; conditional grant: leftover 5h-window time may be used for tests per user 2026-08-02).
- **Post-merge benchmark goahead** *(2026-08-02)* — Once the burn lanes merge, a fresh benchmark (new Experimental vs Stable **0802**) re-baselines the improvement campaign. Needs goahead (multi-test rule).
- **Case-01 bar ratification** *(2026-07-28)* — The 1:3 cost-ratio bar is ill-posed (÷0 when defender losses hit zero). Proposed reframe: "def casualties ≤ X AND att casualties ≥ Y over N seeds." Awaiting ratified numbers before iterating case-01 to GREEN.
- **Ambush gate (b) pricing** *(2026-07-29)* — Benchmark pricing of default-on ambush was lean-OFF but inconclusive at noise scale. Decision: price again with more seeds, or keep default-off and close.
- **Item-24 repoint gates disposition** *(2026-07-29)* — A/B showed byte-identical arms → KEEP OFF recommendation, but the gates are committed-ON at HEAD. Decide: flip to OFF or accept ON.

### Posted questions (in the dashboard, unanswered)
- **Streak protocol** *(2026-07-31)* — what counts as one game in the 10-win streak.
- **Non-wins** *(2026-07-31)* — do draws / timeouts / crashes break the streak.
- **Queue handling** *(2026-07-31)* — do the streak campaign + fixes supersede the parked pipeline items.

### Housekeeping
- **LANDED auto-branch disposition** *(2026-07-29)* — merged branches `auto/may-salvage`, `auto/spread-prefix`, `auto/b1-walkback` left intact on origin; delete or keep.
- **Standing sims/tests state** *(2026-08-02)* — deferred until weekly budget is burned; conditional grant for leftover 5h-window time; full max-burn resumes at 5h reset.

## RESOLVED
_Move items here with the decision + date._
