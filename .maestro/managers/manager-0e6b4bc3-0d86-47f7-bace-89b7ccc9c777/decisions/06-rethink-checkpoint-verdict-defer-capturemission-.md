# RETHINK checkpoint verdict: defer CaptureMission, route cycle 2 at TECN availability

_Recorded 2026-07-20T01:43:21.300Z by ee31feaf_

Context: the pre-committed decision rule (mission-abstraction costing e0ca8772) said: build CaptureMission step 1 at the next capture cycle IF cycle-1 patches miss 6/10 AND markers show lost-TECN-no-retry or escort desync; else defer.

Evidence (cycle-1 N=10, branch 62edde74): bar missed (4/10, unchanged from baseline) — but the marker data shows the failure mode is UPSTREAM of everything the mission abstraction would fix. Pooled over 994 no-idle scans, 88% had total-tecns=0; 5/10 matches fielded zero TECNs the entire match; tecn-killed fired only twice, neither on a committed capturer; all 6 capture orders that did fire were issued promptly (ticks 680–1477) and the conditional gross median passed ($6,377). Lost-TECN-no-retry: not observed. Escort desync: not observed in capture context.

Options considered:
1. Build CaptureMission step 1 now (aim-high leap, ~1–1.5 days) — rejected: a mission lifecycle cannot capture with zero capturers alive; it would sit idle on the same starved pool and the cycle would mis-attribute.
2. Route cycle 2 at TECN production/availability (call-in cadence, ConsumedByCapture pool drain, "keep N ready" floor vs the tecn:3 ceiling) — CHOSEN: directly attacks the measured binding constraint; small; attribution stays clean.
3. Do both in one cycle — rejected: violates one-behavior-per-cycle.

Mission abstraction stays live on the roadmap; its trigger condition now reads: revisit when capture failures are coordinator-shaped (retry/abort/escort) rather than supply-shaped, or at S2.
