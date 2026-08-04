# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> The ordered roadmap of what's next lives in [`PIPELINE.md`](PIPELINE.md) — the user steers by reordering it.
> Cap ~40 lines. Rotate stale entries out — once shipped or `[T]`, the tracker / commit history tells the story.

## Autoburn window ACTIVE (2026-08-04, Mac)
Continuous autoburn until this machine's weekly reset. Gates unchanged: no batch/tournament/multi-test runs without explicit user goahead; user-gated items stay parked in [`AWAITING-USER.md`](AWAITING-USER.md).

## Working on
- **Live-play fix campaign (PRIORITY — before anything else new)** — four parallel root-cause investigations from the user's River Zeta session vs @experimental: (1) OOA vehicles don't evacuate + Logistics-center capture, (2) supply-truck hunt mode + infantry auto-rearm (tactical layer shared human+bot, stance-gated default-on), (3) units pooling at SR instead of reinforcing the front, (4) idle transport helis — use or evac/sell. Fix waves follow each report; manager personally verifies solution + test soundness before marking done. Live board: [`STATUS.html`](STATUS.html).
- **Fires P2+P3 + brain 1c+1d batch** — implementer on worktree branch `auto/brain-fires-batch`: PreparatoryFires (screen holds until prep bombardment elapses), SuppressionCoordinatedAdvance (screen advances when observed suppression clears the bar), QuantizeAxisScore (brain 1c — stop believed-field bucket jitter defeating the reassign margin), Aggressiveness slider scaffold (brain 1d, byte-identical at 50). All default-off @experimental. Adversarial review + merge to follow. Specs: `plans/260803_fires_cycle_design.md`, `plans/260802_squad_brain_design.md`.
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
