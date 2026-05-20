# auto/tests-math — autoburn 260520

## Status

SALVAGED — original worker was killed when the Maestro daemon was terminated under CPU pressure. The 6 test-file commits below are shipped; this report is the conductor's post-mortem.

## Summary

6 new NUnit test fixtures covering WW3MOD-specific math/utility code that previously had zero coverage. ~1300 lines of test code, ~94 test cases.

## Commits

- `95fcf6b0` — `SupplyRouteContestationMathTest.cs` — 25 cases. CalculateTickRate, NetSurplus clamp, IProductionSpeedModifier.GetProductionSpeedModifier, recovery boost, ControlBarFraction. **Worker explicitly claimed "all green"** on this one.
- `0fa18e13` — `AbsorbsSupplyCacheMathTest.cs` — 9 cases. Per-tick transfer math: headroom/toTransfer/available clamp chain, iterated drain.
- `4deb9952` — `HuskDecayMathTest.cs` — 11 cases. `Waiting→Fading→Done` state machine + `1 − t/FadeDuration` alpha curve.
- `9603197e` — `ThreatMapMathTest.cs` — 16 cases. Ceil-division grid sizing, 8-neighbour spread with <1 cutoff, grid↔map cell conversion, `ToGridPos` clamp.
- `69601b7c` — `CaptureCoordinatorMathTest.cs` — 14 cases. Distance-decay, 3-tier safety multiplier, combined long-score math driving v2 AI capture priority.
- `d02604c6` — `SupplyProviderConditionsTest.cs` — 19 cases. High/medium/low/empty tier thresholds at 66/33% boundaries, `SupplyAnyCondition` gating, `CalculateNeed` SV-weighted across pools, `SetSupply [0, TotalSupply]` clamp.

## Verification (CRITICAL — please run before merging)

Only the first commit explicitly claims tests green. The other 5 do NOT confirm pass. Run:

```bash
cd engine && dotnet test OpenRA.Test/OpenRA.Test.csproj -c Release
```

Before merging this branch:
1. Confirm all 94 cases compile.
2. Confirm all 94 cases pass.
3. If any fail, the branch is still valuable as a near-complete starting point — the math being asserted matches the source-of-truth implementations, so red tests are likely revealing real bugs OR mis-modelled formulas in the tests.

## Skipped / not done

Original prompt also asked the worker to flag any bugs surfaced by the tests. Worker was killed before reaching that triage step.

## Files touched

```
engine/OpenRA.Test/OpenRA.Mods.Common/SupplyRouteContestationMathTest.cs
engine/OpenRA.Test/OpenRA.Mods.Common/AbsorbsSupplyCacheMathTest.cs
engine/OpenRA.Test/OpenRA.Mods.Common/HuskDecayMathTest.cs
engine/OpenRA.Test/OpenRA.Mods.Common/ThreatMapMathTest.cs
engine/OpenRA.Test/OpenRA.Mods.Common/CaptureCoordinatorMathTest.cs
engine/OpenRA.Test/OpenRA.Mods.Common/SupplyProviderConditionsTest.cs
```
