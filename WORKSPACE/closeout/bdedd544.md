# Close-out — manager `bdedd544` (supply-truck delivery doctrine, danger-scale measurement, autotest harness)

Validated against `main @ 35876332`.

## 1. Open work, and the next concrete step

**a) `@stable` benchmark re-baseline — STILL OPEN, and the most consequential item here.**
Last `WORKSPACE/ai-bench` activity is `5dc14934`, which predates everything since. The control has now moved under **five change sets from this session** (danger fire-cycle merge, aircraft rearm, supply delivery commitment, demand-driven fleet sizing, the dry-infantry recruitment gate) **plus the second machine's seven merges today**. Every one reaches `@stable` deliberately — settled policy is that it inherits improvements — and each commit says so. Consequence: **any A/B against `@stable` right now is measuring a control that has shifted at least twelve times underneath it.**
*Next step:* recon first — the `ai-bench` worktree was 638 commits behind `main` when last checked, so settle whether the ladder runs from `main` or from that worktree **before** spending an hour of matches on stale code. `8a193c41` already stopped `SPEC.md` pointing benchmarks at a worktree; confirm that landed as intended, then run the ladder.

**b) Harden `test-supply-safe-front-keeps-cargo` — STILL OPEN.**
Confirmed by grep at `35876332`: the scenario has no drift clause. It passes for the right reason (verified from the log — truck reached x=39 against a platoon at x=44, served from its aura, kept its cargo, platoon held) but it does not *assert* the platoon held position, so the front-collapse failure that fooled the danger scenario would slip straight through it.
*Next step:* copy the peak-drift clause from `test-supply-under-danger`, allowance **6 cells** (5-cell crate walk + 1 tolerance), tracked as the **peak over the run** rather than the value at verdict.
*Note:* the second machine independently hit this same class at `74e220f5` — *"a matched pair green on both sides still did not protect a third doctrine"* — which strengthens the case rather than duplicating it.

**c) Three stance scenarios — SOLVED UPSTREAM at `a4d85b0c`** (the scenarios were disabling the trait they test). This session's reading — "units never leave the spawn cell, so the cover-seek order is never issued" — was right about the symptom and wrong about the cause. No action.

## 2. Uncommitted or unmerged artifacts created by this manager

**None.** All work is committed and on `main`. No branches, no worktrees, no stashes. Scratch logs this session created (`batch-*.log`, `user-session-*.log`, `tourney-real.log`) were deleted at close-out.

One item that is **not** this manager's to commit: the **user's own `river-zeta` map edits** were uncommitted at close-out and were deliberately left untouched (their work, unknown whether finished). `nav-guard` measured them as strictly *opening the map up* — wheeled passable 5,237 → 5,511, largest component 4,785 → 5,396, pocketed cells 452 → 115, components 38 → 30. Nothing sealed. `nav_guard.py bless` re-records the baseline once they are happy. If those edits are still dirty in a later session, this is the evidence they are safe.

## 3. Questions asked of the user, never answered

Both were **proceeded past** and stand as overridable records. **Do not re-ask or reword.**

1. **How the truck decides a delivery is dangerous enough to drop short rather than drive in.** Resolved in code as a two-limb classifier — stands out against the player's own live median **OR** exceeds an absolute figure derived from the danger unit's own definition (100 units = one believed contact at point-blank) — plus a floor so a quiet field reads quiet. Both limbs are required: purely relative was tried and classified a cell reading 462,272 as *safe* because a saturated field has an enormous median; purely absolute is how the original thresholds broke.
2. **Spending one autotest run on the instrumented order log** — posted two days ago, still unanswered, agent proceeded.

## 4. Transcript-only knowledge

Persisted at close-out to the manager log and to `WORKSPACE/DISCOVERIES.md` (`d53779d9`). Recorded here because it is the single most re-discoverable "bug" in this area:

**USER RULING: burning ejected crew is INTENDED. Do not "fix" it.** Asked as an approval question and **denied**, verbatim: *"The crew is supposed to burn sometimes, when the vehicle is heavily damaged. I see no need to change any of that, sometimes it just looks cool (in a dark way) to see your enemies crawling out of the vehicle only to burn and die."* The mechanism looks exactly like a defect and will be re-diagnosed as one: `VehicleCrew.cs:358-362` grants `onfire` with **no duration**, unlike `VehicleCookoff`'s `Duration: 100`, against `ChangesHealth@BurnDamage_3` at −1% MaxHP every 8 ticks. A fix was built and **reverted at `36ad9865`**, with `CrewFireDurationTicks` removed rather than left dormant.

**Binding consequence:** every phase of `test-evac-suite` must assert **who got out**, never **who is still alive**. Post-ejection survival is not something the game guarantees, so a survivor-count assertion is a coin flip no threshold can stabilise — the 12 → 8 → 6 threshold walk of 2026-05-09 was three attempts at exactly that.

Noted without reopening: the user said "burn *sometimes*" while the mechanism kills every ejected crewman eventually — the variance is only in *when*.

**Two method lessons, also in the manager log:**
- **A bug that cannot fire is indistinguishable from a bug that does not exist.** The evac-vs-delivery defect looked refuted until the anchor fix gave it something to interrupt; fixing the one in front promoted it from refuted to confirmed.
- **Green scenarios did not predict the real match.** Both supply scenarios were green while the user's own game showed trucks never delivering, because commitment gated on a dispatch record the truck never obtained and the scenarios always reached that state early. A test bed that always reaches a state cannot reveal a broken transition into it. **Standard raised: a full bot-vs-bot match on a real map is the bar before claiming bot behaviour works, and `--seed` must be pinned when comparing runs across code changes.**
