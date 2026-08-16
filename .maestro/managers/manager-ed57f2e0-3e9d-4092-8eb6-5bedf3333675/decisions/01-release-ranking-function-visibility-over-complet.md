# Release ranking function — visibility over completeness, public-stranger bar

_Recorded 2026-08-16T16:22:37.759Z by 2de8224d_

## Decision

Adopt a single ranking function for the 2026-08-16 release push, derived from four user answers, and apply it to every audit finding and every existing PIPELINE item.

**Rank by: what a stranger encounters, how early, and how visibly — NOT by how incomplete a system is internally.**

## Options considered

**A. Rank by system completeness** (finish Phase A big systems in tracker order). Rejected: the tracker's phase ordering predates the release decision and would put weeks of garrison/stance/cargo work ahead of a missing firing sound a stranger hears in the first thirty seconds. The user explicitly reframed the axis as visibility.

**B. Rank by cheapness (quick wins first).** Rejected as a primary key: it optimises for item count, not for release readiness, and would leave the multiplayer desync — expensive, and now a hard blocker — permanently at the bottom.

**C. Rank by first-session encounter probability × visibility, with cost as a tiebreak.** ADOPTED.

## Why

The user chose **public release to strangers**, which collapses the usual polish/correctness debate: for a stranger, a silent weapon and a crash are the same event — the moment they decide this is unfinished. That makes "would a first-time player hit this, and would they notice" the correct primary key, and it is also the key the user's own answers all point at (identity strings promoted to blocker; already-hidden content dropped to zero effort; bot judged on what a player would *screenshot*).

Cost enters only as a tiebreak between findings at the same encounter-probability, which keeps the expensive-but-blocking items (desync) from being crowded out by cheap ones.

## Consequences accepted

- **Item 42 (2-human multiplayer desync) becomes a hard blocker.** Public multiplayer that desyncs is not shippable. It was explicitly tolerable under the rejected friends-and-testers reading.
- **Item 40 (danger-scale stage (c)) and item 43 (benchmark re-baseline) drop out of the release-gating set.** They remain the principled versions of real problems and stay in the queue, but they no longer block. This reverses their long-standing position at the top of the queue, and it is the single biggest reordering this decision causes.
- **Bot items 63/64/66 keep their high rank** — but on the "visibly stupid" test, not on the "plays well" test. Acceptance shifts accordingly: a bot that stops doing the screenshot-worthy dumb thing passes, even if it still plays weakly.
- **Already-disabled content is out of scope entirely.** No audit effort, no re-enablement, no polish.

## Standing operating rule attached to this decision

Simulation/compute authority is **centralised in the manager**. Workers never self-authorise a run. This is additive to the project's no-autonomous-multi-test rule, not a replacement for it, and it exists because the user's stated failure mode is *"if every worker/submanager starts running simulations then it will be chaos."*
