# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> The ordered roadmap of what's next lives in [`PIPELINE.md`](PIPELINE.md) — the user steers by reordering it.
> Cap ~40 lines. Rotate stale entries out — once shipped or `[T]`, the tracker / commit history tells the story.

## Autoburn window ACTIVE (2026-08-04, Mac)
Continuous autoburn until this machine's weekly reset. **Push policy changed (user, 2026-08-04): continuous push — every verified merge goes to origin immediately** (workers still never push). **Pacing directive (user, 2026-08-04, reinforced ×2): 5h-window pace stays under 1.0 AT ALL TIMES** — last window burned 100% with >1h to reset, the exact failure to avoid. Stagger dispatches across the window instead of front-loading; ending a window at ~95% is fine; headroom above pace is the user's reserve for urgent tasks; let in-flight workers finish, snooze until pace recovers, re-snooze if needed; weekly reaches 100% only at the very end of the week, with margin. Other gates unchanged: no batch/tournament/multi-test runs without explicit user goahead; user-gated items stay parked in [`AWAITING-USER.md`](AWAITING-USER.md).

## Working on
- **2026-08-05 merge batch — THREE lanes landed on main, all PUSHED (main @ `af8bca1f`, NUnit 1136/1136)**: (1) **SR spawn-flow** `e78e7558` — fork-3 answer "advance immediately, singly" → `ImmediateReinforcementCommit: true` @experimental-only, fill-completion massing hold suppressed, @stable byte-identical; (2) **composition baseline** `2eb79262` — the "zero riflemen" report was an `AddToArmyValue` display-filter artifact + two real purchase leaks (broke-cycle lottery, uncapped external FIFO); five templates flagged + FIFO composition ceiling. **AddToArmyValue feeds win-rule scoring → pre/post ladder scores AND assets_value/unit_type_stats non-comparable**; (3) **item 31 aggro slider + opportunistic advance** `af8bca1f` — gates default OFF, zero behavior change until enabled; priced sweep user-gated. **check-yaml backstop GREEN 2026-08-05**: 447 errors = pre-existing baseline exactly (442 documented 2026-07-30; 447 on both observations since — stable), zero errors/warnings name any new key from the three lanes. Live validation of (1)+(2): the user's next live game.
- **Item 22 — case-01 forest ambush: CALIBRATING, awaiting user bar.** The provisional cost-ratio bar is ill-posed (÷0); reframe awaits user ratify (`AWAITING-USER.md`).

## Recently shipped / landed
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
