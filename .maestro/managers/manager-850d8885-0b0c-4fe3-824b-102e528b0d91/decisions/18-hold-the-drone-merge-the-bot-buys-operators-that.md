# Hold the drone merge: the bot buys operators that cannot ever launch

_Recorded 2026-08-27T02:02:20.464Z by 17dc66e4_

The live match returned a **third** outcome. The settling observation was "distinct `ForceAttack` target cells per operator — one means the fix did not take, two or more means it works." The answer was **zero**. Four operators built, not one drone launched, all 31 `[drone]` lines reading `no-eligible-cell`. `ChooseTargetCell` returned null on every evaluation.

So the retask fix was never reached: **FIX 1 is neither confirmed nor refuted**, and the worker said so rather than letting a clean-looking result stand in for one.

## The suspected cause, and why it is worse than a tuning miss

Drone reach is `min(weapon 25, leash 25 − margin 3)` = **22 cells**. Every unit inherits `^StandardVision` (`defaults.yaml:80`), a graded ladder from `4c0` at strength 10 out to **`32c0`** at strength 1. So the operator's own bubble strictly contains everything its drone could reach: every candidate cell is continuously verified, `TicksSinceVerified ≈ 0`, fails `MinStalenessTicks: 500`, refused forever. The ledger claim compounds it — nothing moves the operator, so the bubble never goes stale. **The state is absorbing.**

The worker's own reading is that it built the launch leg and omitted the reposition leg. **I think that reading is wrong in an important way, and told it so:** the 22-cell disc is centred on the operator, who sees 32 in every direction, so walking forward moves both together and the disc stays strictly inside the bubble. If the mechanism is as described, the reposition leg is *insufficient*, not missing — a materially different and more interesting finding.

Which relocates the real question: **should grazing vision at the weakest band count as verification at all?** If `TicksSinceVerified` treats a strength-1 graze at 32 cells identically to a drone hovering overhead, a drone is redundant by construction and no positioning saves it. That is a `ControlField` question, not a drone-module one, and the answer decides whether this is tuning, module, or field.

## Merge held

The branch is gate-green including `make test`, carries every review fix, and has no player-visible change. Merging is still wrong: it makes `@experimental` spend 300 supply on up to two operators per player that **never perform the function they were bought for**. That is a real quality regression on a benchmark-relevant profile, dressed as a feature. The alternative — merge and file the launch gap — would ship a bot that pays for recon it cannot do.

Not merging also costs something: the branch sits, and the production fix the user asked for ("now they make none") stays unshipped. I judge the supply waste worse, because a bot buying useless units is harder to notice later than an unmerged branch is.

## Measure before designing

Granted one more match rather than a design round, for reasons the worker itself supplied: it **cannot prove which gate refused** (no per-gate counters — staleness vs `MaxPoiDistanceCells` vs `MaxAirDanger` is not isolated), and the match was **truncated** at 310 s against a 720 s tournament clock, so late-game state — dispersed units, stale bubble, moved frontier — is untested and is exactly where the absorbing state might break on its own.

The arithmetic is clean and I find it convincing, **which is the precise condition under which this project has been wrong repeatedly today** — twice in the last few hours, once by me. Convincing structural arguments are what the runs keep overturning.

Also asked it to fix its own flagged provenance gap: the `[drone]` lines came from the global `debug.log`, which has no run identity, with provenance inferred from ID and timing alignment. It walked up to this project's standing trap and named it; finishing the job means removing the inference.

## The design call is the user's

Their request was explicit — the experimental bot should use drones. Telling them today that it builds operators and launches nothing is owed regardless of what the next run says. **Do not let a worker build the fix before that conversation**; what a drone is *for* in this game is upstream of any of these numbers.
