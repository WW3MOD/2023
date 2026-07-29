# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> The ordered roadmap of what's next lives in [`PIPELINE.md`](PIPELINE.md) — the user steers by reordering it.
> Cap ~40 lines. Rotate stale entries out — once shipped or `[T]`, the tracker / commit history tells the story.

## Autoburn window ACTIVE (2026-07-29)
The standing test-ladder grant is live (see PIPELINE "STANDING GRANT"). Two measurement threads in motion.

## Working on
- **Item 25 — Stage-F benchmark re-baseline: RUNNING now** (run plan `7fa0b046`). Re-zeroing the @experimental offense instrument so future "did the bot get better?" claims are trustworthy again; also carries item-8 gate (b) ambush pricing. Long run — steals window focus. Its numbers unblock the item-24 gate-enablement A/B (`plans/260729_item24_ab_plan.md`, `77dbfb7d`).
- **Item 22 — case-01 forest ambush: CALIBRATING, awaiting user bar.** Scenario authored + calibration batch run; the provisional 1:3 cost-weighted ratio is ill-posed (a holding concealment drives defender losses to 0 → ÷0). Reframe to "def ≤ X AND att ≥ Y over N seeds" awaits user ratify before iterating to GREEN.

## Reviewed — awaiting harness merge
- **auto/may-salvage** (`ec757ad4`) — tunguska AA ammo-pool ownership fix (+ m113 dead-ref cleanup); ship-as-is · **auto/spread-prefix** (`a55c8b6a`) · **auto/b1-walkback** (`864fdb39`).

## Pending user sign-off
- **Item 27 vehicle turn feel** (merged `aab56954`) — feel A/B: `./tools/autotest/run-demo.sh demo-vehicle-turns`.
- **Item 24 gate enablement** — fog-legal @experimental reads committed ON (`ba387afa`); default-on A/B awaits item-25 numbers.
- **Item 8 ambush gate (b)** — benchmark pricing owed before any default-on (folded into item 25).

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
