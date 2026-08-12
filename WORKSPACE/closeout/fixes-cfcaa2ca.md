# Close-out — manager "Fixes" (session `cfcaa2ca`)

Validated against `main @ 35876332`. Scope of this manager: one track, `retarget-priority` — the Stryker SHORAD not prioritizing helicopters, generalized to "any unit should drop a low-priority target when a higher-priority one appears, driven by the existing `AutoTargetPriority` YAML data."

Delivered as `wt/autotarget-preempt`, merged by the lead at `68b627ce`.

## 1. Open work + next concrete step

**One item, still open at `35876332`.**

`tools/autotest/scenarios/test-autotarget-preempt-air/` has **never been run at its shipped 110-tick deadline**. The fix merged with zero behavioural evidence: nothing has demonstrated in-game that a SHORAD actually breaks off for a helicopter.

Checked against current main before reporting this as open — `git log 68b627ce..HEAD` on `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs`, `mods/ww3mod/rules/defaults.yaml` and the scenario directory returns **empty**. The seven upstream merges from the second machine are all supply / logistics / deploy / saved-game / crop work and touch none of this. The test's own header still says `NOT YET ESTABLISHED at 110 ticks` (`test-autotarget-preempt-air.lua:38`).

**Next concrete step — two runs, both required:**

1. RED control: pin `PreemptScanInterval: 0`, run `test-autotarget-preempt-air`. Must FAIL.
2. GREEN confirm: shipped default. Must PASS.

**Do not accept a lone GREEN as proof.** The persistent-target lock breaks on its own via the reload-driven decay of `opportunityTargetIsPersistentTarget`, so a passing run can measure nothing. The only control run ever performed was against an earlier 80-tick deadline and died on a since-removed scenario guard. This branch produced **three separate false-greens** during development — treat any single result here as suspect by default.

If the control does not go RED, the fix is not doing what the merge commit claims and that needs chasing before anything is built on top of it.

## 2. Uncommitted or unmerged artifacts

**None.**

- Branch `wt/autotarget-preempt` merged at `68b627ce`; worktree removed by the lead.
- This session's only commit is `35876332` (`WORKSPACE/bugs/discovered.md`), path-limited, already on main.
- No stray files, no other branches.

## 3. Unanswered questions to the user

**One.** Early in the track the user was asked for an autotest run grant — the repo's no-autonomous-multi-test rule puts the second run in their hands. It was posted, never answered, and the merge proceeded without it. That is the direct cause of item 1: everything else on the branch was closed, and the merge was held on this until the lead landed it.

## 4. Transcript-only knowledge (persisted before this report)

Both already written out; recorded here so the lead does not have to hunt for them.

**a. `PreemptScanInterval: 0` does NOT revert `@stable`.** → manager log.

The flag gates the preemption scan, but the `OnlyTargets` repair does not sit behind it. That was a pre-existing inverted clause (live since `8b9f6937`) which made `HasValidTargetPriority` return `false` for **every** target in the game, silently dropping targets on every stance-decrease path. Fixing it changes **twelve** stance-change and target-validity call sites regardless of the flag.

Consequences: the `@stable` benchmark baseline must be re-taken, and **it cannot be restored by flipping the flag off**. Anyone treating `PreemptScanInterval: 0` as a clean revert will be wrong.

**b. The lint error COUNT is not a usable signal.** → `WORKSPACE/bugs/discovered.md` @ `35876332`.

`--check-yaml` re-runs the entire rules lint once per map with custom rules and never deduplicates, so 3 errors in the default ruleset become 402 of the mod's 496. Any branch adding one autotest scenario raises the total by exactly 3 with no code involvement. **Diff the error LIST, never the count.**

Also recorded there: a scenario folder is registered as a map source from `tools/`, not `mods/` (`mod.yaml:96`), so an isolation experiment that swaps the `mods/` directory to test "YAML or C#?" **cannot** remove a scenario map and will exonerate it regardless. A clean result from an experiment with no power to produce a dirty one — it caused two misdiagnoses on this branch.

Fix shape for the underlying defect: override `Sellable.RequiresCondition` on GTWR/PBOX/HBOX to drop the `!being-captured` term. One line per actor; takes 496 → 94.

**c. Process note.** → manager log (correction to decision 02).

A manager ruling on this track was wrong and was caught only because it was issued as an explicit conditional — the premise to check was named, and the worker was told to report which way it came out rather than silently pick. It refuted the premise with evidence and took the opposite action. Issue judgment calls that way.
