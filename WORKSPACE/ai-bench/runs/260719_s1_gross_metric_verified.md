# S1 metric fix — the gross capture-income instrument is VERIFIED (v2 reads $6,093, control $0)

**Cycle:** `260719_2017__tournament-s1-eco-river-zeta__2bb65d6` (N=1 hidden smoke, Mode B)
**Change under test:** commit `2bb65d6c` — add `GrossIncomeIntegrator` + `capture_income_gross`
to `BotVsBotMatchWatcher` (verdict_version 2→3), repoint the S1 metric. The metric fix
recommended by `260719_s1_earned_metric_finding.md`. **No AI change. No re-roll.**
**Boundary verdict:** the instrument now registers held-derrick income the old metric was
blind to. This closes the S1 measurement blocker.

---

## TL;DR

1. **The yardstick can now see the capture.** Prior cycle proved v2 captures and *holds* the
   nearest OILB derrick, but `resources_earned` (net `PlayerResources.Earned`) read `0` before
   AND after because a lone $50 derrick never nets positive against standing costs. New
   `capture_income_gross` integrates gross `TotalBuildingIncome` and reads **6093 for v2, 0 for
   the control** — matching the ~$5,900 predicted from $50/50-tick interval held ~t1550→7500.
2. **Observer-only, additive.** The integrator reads `TotalBuildingIncome` and writes to the
   watcher's own state dict; it never mutates sim state. Every prior verdict field is
   byte-compatible, `verdict_version` bumped 2→3, and no scorer/win-rule input changed — the
   `capture_income` *score component* still reads `Earned` (stayed 0 for both), so S2/S3
   outcomes are untouched.
3. **`resources_earned` kept for context.** Still 0/0 (net-blind, as root-caused). It is no
   longer the S1 metric but remains in the verdict as net-budget context.

Result: **winner USA-bot (v2) on time_limit @7500t; v2 `capture_income_gross` 6093 / control 0;
`resources_earned` 0/0; ~90s wall.**

---

## Evidence

`match_1.json` (`verdict_version:3`):
- v2 (USA-bot, america): `capture_income_gross:6093`, `resources_earned:0`, `capture_income`(score
  component)`:0`, `army_value:5500`, `assets_value:5800`, `kills_cost:3150`, `deaths_cost:1300`.
- control (Russia-bot, normal): `capture_income_gross:0`, `resources_earned:0`, `army_value:0`,
  `assets_value:0`, `deaths_cost:6350`.

**Why 6093 is right:** the sim pays `TotalBuildingIncome` every `PassiveIncomeInterval` (50)
ticks; a $50 derrick held from ~t1550 to the 7500 limit ≈ $50 × (5950/50) ≈ $5,950. The
per-tick integral lands at 6093 (capture completed slightly before t1550 in this draw). The
`GrossIncomeIntegratorTest` NUnit cases pin this math (275→282 tests, green).

**Why the winner flipped to v2 (vs the control winning last cycle):** independent RNG draw —
bots use an unseeded `LocalRandom`, so each run is a fresh sample. This is NOT an effect of the
observer-only change (win-rule/scorer inputs are byte-identical); v2 simply won on combat this
draw (score 8650 = army 5500 + kills 3150 vs 600).

---

## Boundary & follow-ups

- **Metric/harness fix only** — no AI, unit-stat, balance, map, or gameplay-affecting engine
  change. Instrument is observer-only.
- **Win-rule economy term (loop-manager decision, recorded in LADDER.md):** the win rule's
  `capture_income` component still reads net `Earned` — the same net-blind defect, but changing
  it would silently redefine S2/S3 winners, so it is **left untouched** by design. The loop
  manager should decide whether to move it to gross and re-baseline S2/S3 if so.
- **Next:** with the metric live, re-baseline S1 at N=10, build
  `tournament-s1-eco-river-zeta-mirror`, and run Normal-vs-Normal calibration so a gross-income
  gap is attributable to AI skill, not spawn-side derrick luck (SPEC §9.4).
