# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> The ordered roadmap of what's next lives in [`PIPELINE.md`](PIPELINE.md) — the user steers by reordering it.
> Cap ~40 lines. Rotate stale entries out — once shipped or `[T]`, the tracker / commit history tells the story.

## Autoburn window ACTIVE (2026-08-04, Mac)
Continuous autoburn until this machine's weekly reset. **Push policy changed (user, 2026-08-04): continuous push — every verified merge goes to origin immediately** (workers still never push). **Pacing directive (user, 2026-08-04, reinforced ×2): 5h-window pace stays under 1.0 AT ALL TIMES** — last window burned 100% with >1h to reset, the exact failure to avoid. Stagger dispatches across the window instead of front-loading; ending a window at ~95% is fine; headroom above pace is the user's reserve for urgent tasks; let in-flight workers finish, snooze until pace recovers, re-snooze if needed; weekly reaches 100% only at the very end of the week, with margin. Other gates unchanged: no batch/tournament/multi-test runs without explicit user goahead; user-gated items stay parked in [`AWAITING-USER.md`](AWAITING-USER.md).

## Working on
- **Live-play fix campaign COMPLETE — all FIVE River Zeta issues CLOSED.** Live board: [`STATUS.html`](STATUS.html). (1) OOA evac + (3) SR pooling: Waves A+B **MERGED @ `f773d428`, NUnit 1051/1051 on merged main, pushed** — two-round adversarial review (Wave A round 1 NITs-only; Wave B 2 real blockers — NearRally-discriminated muster self-seed guard + per-episode sticky fill-hold budget — fixed in `d7c7fac3`; delta re-review MERGE-READY). (2) supply trucks: T1 @ `7fb41816` + **T2 MERGED @ `2dc5c8ee`, NUnit 1081/1081 on merged main, pushed** — idle trucks hunt starving infantry (@experimental-only via explicit `ShouldHunt` gate), aura-edge anti-stall clamp, **`AutoSeekSupplies` default ON for humans + ALL bot profiles incl. @stable (revert `f15cfbde` alone to undo)**, shadowing refactor byte-identical; two review rounds + two manager prose-proof gates (both false proof clauses caught at merge — the 2nd originated in the reviewer's own sketch). (4) idle transports @ `dadd8aee` (TransportMissionSlots slice + use-or-evac + demand-gated purchase). (5) composition @ `9fe22a11` (deficit-argmax live in @experimental). Remaining validation on all five: the user's next live game. Three fork records posted, proceeding on defaults (tactical ON for humans / terminal evac / forward-assemble). **Next lane: selection from `PIPELINE.md` top**, gated on `headroom(five_hour)` per the pacing directive (one dispatch per wake).
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
