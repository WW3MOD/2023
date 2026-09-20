# Nuke perf: revert lever 2, build lever 3, keep the exchange-only variants parked pending the in-game check

_Recorded 2026-09-20T13:19:00.690Z by 60a95888_

## Context (main @ b2602fb4; branches wt/nuke-perf-fixes 916ffca1, wt/nuke-perf-levers 2712e7fc + aab7c588)

Clean A/B on an idle machine (15:00–15:11, six runs, archives `Temp/nukeperf/`): per 6-RV salvo `LightEventManager` 57151 → 17099 ms (fixes) → 10124 ms (levers), worst tick 755 → 213 → 136 ms; single RV 4811 → 2479 → 826 ms, worst tick 113 → 78 → 30 ms.

## Options considered

1. **Ship levers 1+2 as built.** Rejected: W4 proved from the archived perf.logs that all three trees refresh on byte-identical tick sets (identical gap histograms), so lever 2 (`TerrainRefreshTailInterval`, opted in at 20 on NukeSarmatRV/NukeB83) never engaged. Its derived switch fires only while `rate < threshold/tailInterval` = 0.002 intensity/tick, and the Sarmat envelope's slowest segment is 0.00265/tick. Shipping it would leave a live-looking number on two shipped weapons that does nothing — the "believed made, documented as made, inert" shape this repo keeps paying for.
2. **Loosen lever 2's bound so it does engage.** Rejected for now: the shipped staleness is `interval × rate` (~2.6 units at the peak, 65× the threshold), so any engaging bound accepts MORE staleness than shipped — a visible-stepping judgement that needs a `--visible` eyeball, not a derivation. Can be revisited after the in-game check.
3. **Revert lever 2, keep lever 1 (owns the full −41 %), build lever 3.** CHOSEN. Lever 3 (shrink the notified radius as the light decays) qualifies under the rule set before the A/B — build it only if the tail dominates — because the peak/tail split of the remaining salvo cost is 39 % / 61 %. It carries a real trap (cells outside `NotifyCells` keep their last, brightest tint → a stale ring), so it ships only with a residue sweep and an NUnit test proving the tint is identical with and without the clamp.
4. **Build the exchange-only weapon variants (deliverable 3) now.** Deferred: on the rig map `ThermalRadiationEffect` is 68–78 ms per salvo and fire/EMP/suppression are absent from the top 20, so the variant's measurable benefit is <1 % of what the general fixes already removed. It stays queued because a full match with hundreds of units may behave differently; the user's requested in-game verification decides whether it is still wanted.

## Merge order
rig + fixes + lever 1 + runner fix + revert → main on its own gate right after the #2 push; W3's final-exchange branch on its own gate; lever 3 with whichever gate is next.
