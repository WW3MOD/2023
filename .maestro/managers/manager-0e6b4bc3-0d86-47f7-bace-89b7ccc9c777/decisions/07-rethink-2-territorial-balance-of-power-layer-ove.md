# RETHINK #2 — territorial balance-of-power layer over mission abstraction

_Recorded 2026-07-20T18:06:48.217Z by ee31feaf_

Context: re-baseline (bfa8e876) showed Exp ≈ Stable on both rungs — the SR-contestation lever is saturated vs a competent control; S2 engagement collapsed 5-6x into a passive economy race.

Options considered (rethink2, committed 5c07d1a8):
- **Territorial / balance-of-power map layer (CHOSEN, as smallest slice)** — offense bias in PoiOffensiveBotModule reading the existing InfluenceMap, advancing the front into comparatively-weak cells. Rationale: the loudest re-baseline signal (passive economy race, 3/10 zero-combat S2) is precisely the behavior gap the user's North Star "push where the enemy is comparatively weak" addresses; targets S2 swing + engagement floor directly; M-effort, reuses substrate, no big-bang rewrite. Also yields the TECN-ferry regime verdict for free.
- **Mission abstraction (DEFERRED)** — fully costed but its decision rule unmet: capture is at parity, residual is production throughput not lost-TECN-no-retry, so it buys retry/staging the bars don't reward.
- **More SR-multiplier tuning (REJECTED)** — dead end vs same-faction Stable (neutral-to-negative).

Ratified 5-cycle sequence: (1) terr offense bias + [exp-terr] telemetry, (2) dispersion re-verify + disable on @experimental (wrong-sign −$1,500, in @stable too → flag corrective PROMOTE), (3) early-packet granularity (UnitsPerAxis/MinAxisSize/MaxAxes), (4) EXPAND Polar Disorder as anti-overfit gate, (5) full fog-respecting territorial classification + Woodland Warfare.

Foundations: seeded determinism already landed (World.cs:217-228); telemetry rides cycle 1. S2 scoping: keep quiet regime + validity gate; cycle 1's engaged-count decides bot-passivity vs map-geometry before any forced-contact redesign.
