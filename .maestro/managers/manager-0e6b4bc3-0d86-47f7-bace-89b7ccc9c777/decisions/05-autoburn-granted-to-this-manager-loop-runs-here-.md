# Autoburn granted to this manager — loop runs here, dedicated-manager plan superseded

_Recorded 2026-07-19T22:00:18.910Z by ee31feaf_

User granted autoburn (8h) directly to this manager with "prioritize and execute freely", plus: keep WORKSPACE/ai-bench/REVIEW.md updated as the follow-along surface, and post questions as assumed-answer AUQs (user overrides asynchronously; silence = assumption stands).

**Decision:** this manager runs the optimization loop itself. The earlier plan (decision 04: root spawns a dedicated ai-bench manager, recommendation faabb3ec) is superseded but the recommendation stays parked — it can be revived if this manager's context fills or the user wants separation later.

**Standing assumptions posted as AUQ (all kind=assumption, defaults active):**
1. This manager owns the loop (85%).
2. Autoburn grant = standing goahead for hidden Mode-B runs including N-run batches; windowed runs still need explicit goahead (90%).
3. Cycle order: rename merge → win-rule gross repoint → S1 N=10 baseline + mirror + Normal-vs-Normal calibration (SPEC §9.4) → behavior cycle 1 = enemy-SR contestation scoring → dispersion cohesion (75%).
4. Repoint win-rule capture_income net→gross NOW while S2/S3 are unmeasured (85%).

Alternatives considered: waiting for root spawn (rejected — user said "now"; handover friction, no benefit while context is healthy); per-batch proposals (rejected — Mode B verified no-focus-steal, REVIEW already records "unlimited unattended runs", and per-batch gating defeats autoburn).

State at decision: main @ f16db81e (realism doc), ai-bench @ 674d99e2 + rename worker uncommitted work in flight; branches diverged, regular merge required. Run slot: rename worker holds 1 authorized run; loop runs begin after it lands.
