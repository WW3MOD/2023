# The measuring apparatus was wrong more often than the code it measured

_Recorded 2026-09-02T01:25:48.801Z by 17dc66e4_

## The observation

Across this session, **six** distinct failures were in the *instrument* rather than the thing under test. That is more than the number of real defects found in the code tonight. Recording it because the pattern is invisible from inside any single incident — each one looked like a one-off.

1. **A sabotage that failed to apply** (earlier session, ~00:30). `perl ... if $. == 1875` matched a bare `{`; the real site was 1876. The edit changed nothing, the build succeeded, and the "RED" leg would have reported **PASS** — reading as *"the test is vacuous"* rather than *"the sabotage missed"*. A sabotage that fails to apply produces a **confident false result, not a null one**.

2. **A `cat` appended after the runner** (~01:47). `test-capture-dispatch-bottleneck` passed with `RUNNER_EXIT=0`; my chain then ran `cat "$RD/result.json"` with `$RD` globbed from a path that does not exist (run dirs live under `~/.ww3mod-tests/screenshots/`, not `tools/autotest/`). `cat` failed, and **its** exit code became the chain's. Maestro reported the task **failed**. The project's existing rule — *never pipe a verdict through `tail`* — is the same mechanism pointing the other way.

3. **My own guard, failing in both directions at once** (~02:05). Written *specifically* to prevent #1, it (a) tested with `git diff`, which cannot see the staged change `git checkout <ref> -- <path>` creates, producing a **false negative**, and (b) placed its restore after the run, so the abort at step 2 skipped it and left `Map.cs` sabotaged — the identical end state as the `TaskStop`/trap failure it was written to avoid.

4. **`TaskStop` does not run bash `EXIT` traps** (~01:22). Reported success while leaving a deliberately broken `Map.cs` in a worktree for two hours. Filed as maestro issue `7fb13404`.

5. **Exit code 2 read as failure, twice** (~02:35, ~02:41). `Test.Skip` exits 2 by design. Both capture runs were reported as **failed**; both had produced their frames correctly.

6. **A timer that would have measured itself** (~03:00). The proximity-perf worker's first cut timed every `UpdateProximityTrigger` call. `Stopwatch` pairs cost 173–190 ns on this machine; at 583 calls/tick that is **~101–110 µs/tick of pure instrument**, plausibly the same order as the signal. It caught this in calibration and said plainly it would otherwise *"have written it up as a result."*

## What generalises

- **Verify state with a command that can observe the state you changed.** `git checkout -- <path>` stages; `git diff` does not see staged. Use `git diff HEAD`.
- **The verdict-producing command must be LAST in its chain.** Anything appended after it — `cat`, `grep`, `echo` — can overwrite the exit code in either direction. Read `result.json` in a separate call. This is the general form of the existing `tail` rule, which is only its most famous instance.
- **Every early exit must undo what the run has already done.** An abort path is the most likely to be taken and the least likely to be tested. Inline cleanup is not enough if it sits after the abort.
- **Calibrate the instrument against the signal before trusting either.** Measure your measurement's own cost. #6 was caught only because the worker built `ProximityPerfOverheadTest` and looked.
- **A green with no description is not evidence.** Both crew scenarios returned `status: pass` with an empty `notes` and no screenshots. They were trusted **only** because each had produced a RED on 2026-09-01 and therefore demonstrably discriminates. `test-sr-spawn-candidate-set` needed the same argument: an empty-`notes` GREEN plus a RED reproducing the old set verbatim. **Neither half is sufficient alone.**

## Why this is worth a decision record

The countermeasure that worked, every time, was the same: **read `result.json`, never the exit status.** Five of the six were caught by that habit or by a worker's own calibration; none was caught by a check that existed beforehand.

The corollary is uncomfortable and is the actual finding: **tonight's harness would have reported a wrong answer at least three times to anyone who trusted its exit codes.** Before adding another gate, it is worth asking whether the existing ones can be believed.
