# Hold wt/drone-guard rather than pick a direction on a mechanism the other session owns

_Recorded 2026-09-01T23:54:36.378Z by 17dc66e4_

## The finding

Adversarial review `1cfa6876` returned **MERGE WITH FIXES**, one blocking, and the blocking one is the most dangerous shape of defect this repo has produced tonight:

`main` grew the `expected-status` mechanism at `575e48c8` and used **this very scenario** as its worked example, declaring it **`fail`**. `wt/drone-guard` flips one Lua verdict call to `Test.Skip`. The branch predates that commit by eleven minutes and could not have known.

`git merge-tree main wt/drone-guard` is **textually clean** — no conflict, nothing flagged. The merged tree carries main's `expected-status` blob (`840efd02`, declaring `fail`) alongside the branch's Lua blob (`4e11b832`, emitting `Test.Skip`). The reviewer then **executed the grader**: `expected_status_grade "fail" "SKIP"` → **RED**.

So the merge converts a scenario that is green-by-declaration today into a permanent red in every batch — which is the precise disease the branch was written to cure — through a merge that no check we run would flag. **Every gate we have is blind to it.** Build, NUnit, YAML lint and `merge-tree` all pass; only running the grader finds it.

## Options

- **A — apply fix 1 (flip `expected-status` to `skip`) and merge.** The reviewer's recommendation, argued on merit: under `skip` the asymmetry holds in both directions (`SKIP`→GREEN, `PASS`→red, `FAIL`→RED), and it closes a gap `expected-status.sh` documents about **itself** — a watchdog timeout writes `status:"fail"` and exits 1, so a `fail` declaration grades a hung run GREEN, "the one outcome a by-merit declaration must never absorb."
- **B — revert the branch's `Test.Skip` to `Test.Fail`** to match main's existing declaration. Smaller, concedes the branch's central argument.
- **C — hold the branch.** Chosen.

## Why C

Not because of anything in the review, which is the most rigorous this project has received — it independently recomputed every number on the branch and found no error, verified the pre-registration was committed **before** the measurement with its text unchanged across it, and confirmed `IntelSquares` is **uniquely 247** (the only integer satisfying both `floor(sq·4/29)=34` and `floor(sq·21/29)=178`, which is a strong check and hard to fake by accident).

The reason is coordination. **`575e48c8` was authored today by the second Claude session, which is still actively working in this repository, and I cannot message it.** The fork the reviewer identifies is a direction call on that session's own mechanism, and its own Watch says so plainly: *"its author may already have a different fix in flight. That is a judgement call about direction, not a fact I measured."*

Picking a direction on someone else's live mechanism, blind, at 02:00, is how tonight's other two errors happened. Both were the same shape — acting on a stale or unverified reading of state another party controls.

**The cost of holding is zero.** The reviewer verified the branch changes **no game behaviour at all**: `LostTrackIntelSquares` stays 250, the 19-line `ai.yaml` diff is comments only (0 non-comment added lines), and the sole executable change is a log string printing `none` instead of a falsely-confident `0,0`. There is nothing time-sensitive to lose.

## Carried forward

The three fixes stand as written and are actionable, not pointers — the reviewer computed the replacement numbers. Fix 2 in particular is independent of the direction call: the stale 11-cell derivation still sits at `CONTROL-ARM.md:128-137`, **sixteen lines above the branch's own correction and contradicting it**, and the corrected figures are `displacement 170`, `wins iff reveal_hunt ≥ 137`, multiplier strictly between **1.006× and 1.806×** — note the honest upper bound rises just past the pre-registered line and should not be quietly dropped.

The run that would settle A: one `run-test.sh test-drone-lost-track` **after** fix 1, predicting `SKIP` graded `OK(skip)`. A `FAIL` there means the by-merit branch is not being reached at all and the branch should not merge under any option.
