# fp-determinism — cross-runtime probe for floating point on the synced path

Runs the **shipped** `CohesionIntentMath.Classify` (referenced from `engine/bin/OpenRA.Mods.Common.dll`,
not reimplemented) under two .NET runtimes and diffs the output bit-for-bit.

## Why this exists

`CohesionMoveModifier.ModifyGroupOrder` runs on **every client** (`UnitOrders.cs`) when a grouped Move
is split per actor, and its result picks each actor's destination cell. The classification step uses
double-precision covariance/eigenvalue math — including `a*b - c*d`, an FMA-contraction candidate — and
branches on double comparisons. OpenRA's determinism model is integer-only (`WDist`/`WAngle`/`WPos` are
all ints), so this is a standing hazard regardless of any specific bug.

It was built to test whether the 2026-08-16 two-human desync (host .NET CLR 8.0.27, friend 10.0.10;
one infantryman walking to a different cell with the shared RNG stream untouched) was caused by that
arithmetic differing between runtimes.

## Running

```bash
dotnet build -c Release --no-incremental          # incremental builds have been unreliable here
dotnet exec --fx-version 8.0.30  bin/FpDeterminism.dll 8.0.30
dotnet exec --fx-version 10.0.11 bin/FpDeterminism.dll 10.0.11
```

The argument is a **required-substring assertion** on `RuntimeInformation.FrameworkDescription`: the
harness exits 2 rather than print results if it is running on a runtime you did not ask for. A probe
that silently ran on the wrong runtime three times measures nothing.

## Before believing a negative, prove the instrument can go red

```bash
dotnet exec --fx-version 8.0.30 bin/FpDeterminism.dll 8.0.30                    # baseline digest
dotnet exec --fx-version 8.0.30 bin/FpDeterminism.dll 8.0.30 --perturb 87421    # must differ
```

`--perturb N` nudges case N's `Lambda1` by one ULP. If the digest does not move, the sweep is not
measuring what you think.

**PITFALL that already bit once:** zsh does *not* word-split an unquoted `"$var"`. Writing
`for p in "--perturb 0"; do run $p; done` passes `--perturb 0` as a single glued argument, the flag
never parses, and the sensitivity check "passes" while doing nothing. The harness echoes `ARGC`/`ARGS`
so this is visible — check it.

## Result, 2026-08-16 (macOS, ProcessArchitecture X64)

`.NET 8.0.30` vs `.NET 10.0.11`: **byte-identical** across 6 detail cases (raw bits of `cxx`, `cyy`,
`cxy`, `disc`, `lambda1`, `lambda2`), 58 near-threshold boundary cases, and a 200,000-case randomised
sweep digest (`868A37ABE86A82CD`). Sensitivity verified: perturbing case 0, 87421 or 199999 by one ULP
each changed the digest.

## Result 2, 2026-08-16 — the layout kernels (`CohesionLayoutMath`)

The functions that emit the actual destination **cells** — nearer the observed symptom (a differing
`Mobile.ToCell`) than the classifier is: `BoxColumns`, `TreelineForward`, `EdgeAnchorOffset`,
`LineSlotOffset`, `OpenLineOffset`, `ApproachWalk`, `ApproachStepOffset`, `NudgeOffset`.

`.NET 8.0.30` vs `.NET 10.0.11`: **byte-identical**. Detail cases with raw bits of every intermediate,
**896 half-integer boundary hits** — inputs where `(int)Math.Round` sits exactly on its flip point, so a
one-ULP disagreement there *would* become a different cell — and a 200,000-case sweep digest
`679639AA9896FA55`. Sensitivity verified: one ULP on `UnitX` of case 0, 123456 or 199999 each moved it.

Together the two results cover **the entire floating-point surface of `CohesionMoveModifier`**:
`grep -E "Math\.(Sqrt|Ceiling)|/ 1024\.0|Math\.Round"` on that file now returns nothing, because it all
lives in `CohesionIntentMath` and `CohesionLayoutMath`.

## What this still does NOT prove

Platform: this ran on macOS x64 while the players were on Windows x64 — same ISA family, different OS
and JIT host. Evidence, not proof, about their machines.

And a null result here does not make the code safe. 896 inputs sit exactly on a rounding boundary; on
those, the correct behaviour depends on two machines agreeing to the last bit of a `double`. They agree
today, on these two runtimes, on this hardware. That is luck holding, not a guarantee — which is why
this math wants replacing with fixed-point regardless. `Exts.ISqrt` (Floor/Nearest/Ceiling, int through
ulong) is the substrate, and `WVec.Length` already uses it.
