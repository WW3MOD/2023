# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> The ordered roadmap of what's next lives in [`PIPELINE.md`](PIPELINE.md) — the user steers by reordering it.
> Cap ~40 lines. Rotate stale entries out — once shipped or `[T]`, the tracker / commit history tells the story.

## Machine hand-off (2026-08-06, Mac → other machine)
Spend PAUSED on the Mac (account headroom reserve, user directive 2026-08-06). Work continues on the user's other machine after `git pull` — the manager there gets a continuation prompt. **Push policy stands: continuous push — every verified merge goes to origin immediately** (workers still never push). **Pacing: 5h-window pace stays under 1.0 AT ALL TIMES.** Other gates unchanged: no batch/tournament/multi-test runs without explicit user goahead; user-gated items stay parked in [`AWAITING-USER.md`](AWAITING-USER.md).

## Working on
- **2026-08-06 user-report batch — THREE fixes landed on main, all PUSHED (main @ `e15e986b`, NUnit 1148/1148)**: (1) **game clock** `1abb62e1` — clock/time-limit/strategic-progress readouts rescaled under the debug speed button; now format elapsed `WorldTick` with the invariant `GameSpeed.Timestep` (never mutable `world.Timestep`); hover tooltip shows real time. Reported-not-fixed: `TimeLimitManager.cs:110` integer division (limits ~4% long), `PlayerStatistics.cs:89` speed-dependent graph sampling; (2) **transport-idle** `c89d20bb` — humvee overbuy (affordability-filtered composition argmax degenerated to cheapest-type pump) + transport helis parked forever (`Actor.IsIdle` never true for hovering aircraft → evac timer unreachable). **STILL OPEN: heli lift missions have NEVER functioned** (same IsIdle misuse at 4 ungated shared-code sites) — scope question posted to user, churn-risk finding says any lift fix must include `ShouldBuyTransport:517`; (3) **pips zoom** `e15e986b` — spectator-era sub-MinZoom decoration cull dropped; pips now render at every zoom. **Live validation of all three: the user's next game** (clock true under 2x/4x/8x + hover tooltip; no humvee pile-up, transports evac; pips at all zooms).
- **Item 22 — case-01 forest ambush: CALIBRATING, awaiting user bar.** The provisional cost-ratio bar is ill-posed (÷0); reframe awaits user ratify (`AWAITING-USER.md`).

## Recently shipped / landed
- **2026-08-05 merge batch (SR spawn-flow `e78e7558` @experimental-only; composition baseline `2eb79262` — AddToArmyValue feeds win-rule scoring → pre/post ladder scores non-comparable; item-31 aggro slider `af8bca1f` gates OFF)**. check-yaml backstop GREEN: 447 = pre-existing baseline.
- **Live-play fix campaign COMPLETE (2026-08-04/05) — all FIVE River Zeta issues closed**: OOA evac + SR pooling `f773d428`; supply trucks T2 `2dc5c8ee` (**AutoSeekSupplies default ON for humans + all bots incl. @stable — revert `f15cfbde` alone to undo**); idle transports `dadd8aee`; composition deficit-argmax `9fe22a11` → baseline fix `2eb79262`. Board: [`STATUS.html`](STATUS.html). Validation: user's next live game.
- **Fires P2+P3 + brain 1c+1d** (merged `ea0d0f89`) — flags OFF in @experimental → behaviorally neutral; enablement waits on the priced sweep (user-gated).
- **DISCOVERIES curation (2026-08-04)** — `a313b306`: unpromoted tail promoted, 5 doc contradictions fixed; item-24 gates found ON in @stable too → sharpened in AWAITING-USER (`b751695a`).
- **Frontline arc complete (2026-08-03)** — posture fix (merged `7baa3885`) + Phase 7 lateral spread / forward muster (merged `4e53d428`). Lateral-spread live validation with the user.
- **Fires Phase 1** (merged `c2ed0c67`) · **Lever-4.D instrumentation** (merged `b00b1b44`) · **Heli forward-staging** (merged `ec61df58`, gate OFF).
- **Wobble RESOLVED (2026-07-30)** — root cause was item-28 string-pulling, not item-27 turn tuning; `StringPullMovement` default OFF (item-27 gains kept). Rework parked as its own pipeline item.

## Pending user sign-off
See [`AWAITING-USER.md`](AWAITING-USER.md) — the full user-gate queue. Highlights: balance proposals 001–003, parity batch runs (configs authored, runs need grant), post-merge benchmark goahead, case-01 bar, item-24 gate disposition (KEEP OFF recommended, committed ON), item-27 turn-feel A/B, item-31 aggro sweep (grant + gate flip), fires/brain enablement sweep.

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
