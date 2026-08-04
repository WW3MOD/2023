# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> The ordered roadmap of what's next lives in [`PIPELINE.md`](PIPELINE.md) — the user steers by reordering it.
> Cap ~40 lines. Rotate stale entries out — once shipped or `[T]`, the tracker / commit history tells the story.

## Autoburn window ACTIVE (2026-08-04, Mac)
Continuous autoburn until this machine's weekly reset. **Push policy changed (user, 2026-08-04): continuous push — every verified merge goes to origin immediately** (workers still never push). **Pacing directive (user, 2026-08-04): 5h-window pace stays at/under 1.0** — headroom above pace is the user's reserve for urgent tasks; let in-flight workers finish, snooze until pace recovers, re-snooze if needed, then next round; weekly reaches 100% only at the very end of the week. Other gates unchanged: no batch/tournament/multi-test runs without explicit user goahead; user-gated items stay parked in [`AWAITING-USER.md`](AWAITING-USER.md).

## Working on
- **Live-play fix campaign (PRIORITY)** — all FIVE River Zeta issues root-caused and manager-verified; implementation phase running. Live board: [`STATUS.html`](STATUS.html). Both implementer lanes DELIVERED (un-merged), reviews held for pace recovery: (2) `auto/supply-hunt` — T1 infantry auto-seek, stance-gated, default-off; B3 audit found the suspected distance defect does NOT exist. (5) `auto/composition` — deficit-directed purchasing vs per-mille targets + counter-bias, default-off @experimental. Next round on wake: verify → adversarial review → merge → push, then Waves A/B. Ready, held for 5h-pace recovery: Wave A = (1) OOA evac sweep + LC capture tier, Wave B = (3) SR-pooling fixes (PoiOffensive overlap now clear — batch merged); (4) transport-employment wave after composition merge. Three fork records posted, proceeding on defaults (tactical ON for humans / terminal evac / forward-assemble).
- **Fires P2+P3 + brain 1c+1d batch — MERGED @ `ea0d0f89`, pushed** — all FIX ×7 verified fixed (independent NUnit 919/919 + manager full-diff review); new features **flags OFF** in @experimental → behaviorally neutral merge. Enablement waits on the priced sweep (user-gated).
- **Item 22 — case-01 forest ambush: CALIBRATING, awaiting user bar.** The provisional cost-ratio bar is ill-posed (÷0); reframe awaits user ratify (`AWAITING-USER.md`).

## Recently shipped / landed
- **DISCOVERIES curation (2026-08-04)** — `a313b306`: unpromoted tail promoted, 5 doc contradictions fixed; item-24 gates found ON in @stable too → sharpened in AWAITING-USER (`b751695a`).
- **Frontline arc complete (2026-08-03)** — posture fix (`82779a2e`, merged `7baa3885`: contact-sector evaluation + own-strength floor) + Phase 7 lateral spread / forward muster (`97db9a15`, merged `4e53d428`). Live-game validation of lateral-spread is with the user.
- **Fires Phase 1** (`c6634d24`, merged `c2ed0c67`) — continuous bombardment of believed-static positions, @experimental.
- **Lever-4.D instrumentation** (`159a2204`, merged `b00b1b44`) — two-sided capture-contest + income-timeseries, observation-only.
- **Heli forward-staging Option A** (`c417ca63`, merged `ec61df58`) — idle attack helis stage forward, gate default OFF @experimental.
- **Wobble RESOLVED (2026-07-30)** — root cause was item-28 string-pulling, not item-27 turn tuning; `StringPullMovement` flipped default OFF (item-27 gains kept). Rework parked as its own pipeline item.

## Pending user sign-off
See [`AWAITING-USER.md`](AWAITING-USER.md) — the full user-gate queue. Highlights: balance proposals 001–003, parity batch runs (configs authored, runs need grant), post-merge benchmark goahead, case-01 bar, item-24 gate disposition (KEEP OFF recommended, committed ON), item-27 turn-feel A/B.

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
