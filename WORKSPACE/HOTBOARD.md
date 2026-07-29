# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> The ordered roadmap of what's next lives in [`PIPELINE.md`](PIPELINE.md) — the user steers by reordering it.
> Cap ~40 lines. Rotate stale entries out — once shipped or `[T]`, the tracker / commit history tells the story.

## Autoburn window ACTIVE (2026-07-29)
The standing test-ladder grant is live (see PIPELINE "STANDING GRANT").

## Working on
- **Wobble regression investigation** — a movement-feel wobble surfaced; prime suspects are item-27 (vehicle turn-feel, `aab56954`) and item-28 (path string-pulling, `1f036ecb`), both of which changed vehicle trajectories. Bisecting which change (or their interaction) introduced it.
- **Lever-4.D instrumentation build** — two-sided capture + income-timeseries instrumentation; recon spec implementation-ready (`c4ba0eee`).
- **Heli forward-staging (Option A) build** — forward-staging attack helis; recon spec implementation-ready (`c61d1011`).
- **Item 22 — case-01 forest ambush: CALIBRATING, awaiting user bar.** Scenario authored + calibration batch run; the provisional cost-weighted ratio is ill-posed (holding concealment → defender losses 0 → ÷0). Reframe to "def ≤ X AND att ≥ Y over N seeds" awaits user ratify.

## Recently shipped / landed
- **FiringLOS off-map crash fix** (`99a58363`, pushed) — guarded `ShadowLayer` lookup against off-map cells in `HasClearLOS` + `GetGroundShadowDensity`; crash reported from live play on two machines.
- **Harness merge complete (2026-07-29)** — auto/may-salvage (`ae7ca6d8`), auto/spread-prefix (`23398408`), auto/b1-walkback (`2bf335cf`) all merged to main. Branch disposition is an open user question.
- **Item 25 re-baseline DONE + item-24 A/B CLOSED** (`af95e178`, `db1cff01`) — @experimental offense instrument re-zeroed; item-24 belief-repoint gate priced **KEEP OFF** (A≡B byte-identical).

## Pending user sign-off
- **Item 27 vehicle turn feel** (merged `aab56954`) — feel A/B: `./tools/autotest/run-demo.sh demo-vehicle-turns` (also a wobble suspect, above).
- **Item 24 gate enablement** — fog-legal @experimental reads committed ON (`ba387afa`); default-on A/B recommendation is KEEP OFF pending user call.
- **Item 8 ambush gate (b)** — @experimental benchmark pricing owed before any default-on.

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
