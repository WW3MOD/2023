# STOP note — TERR bias verify batch halted by user order (2026-07-21)

**User ordered:** stop the verify batch, no further builds/tests/game runs until further notice.

## State at stop

- **The batch had already completed before the stop order.** Both rungs ran to full
  completion: S2 N=10 (10/10 verdicts) and S1 N=10 (10/10 verdicts), 0 no-verdict, 0
  crashes, 0 orphans. Full analysis: [`260721_terr_bias_verify.md`](260721_terr_bias_verify.md).
  So "partial results" here = the **complete** 20/20 batch; nothing was mid-flight.
- **Process check at stop: no game (OpenRA/WW3) and no dotnet-runner processes running.**
  Nothing to kill — the batch script exited cleanly (`ALL_BATCHES_COMPLETE`, both rc=0).
- **Not merged to main.** `main` untouched at `ec097751`.
- **Worktree `C:\Users\fredr\worktrees\ww3mod\terr-bias` intact**, implementation committed
  on branch `exp-terr-bias`.

## Headline (unchanged from the verify doc)

- **S2: headline did NOT lift** — median Exp swing −350 (= baseline), engaged 7/10 (= baseline,
  not lifting). Root cause: raw-share BoP fired as a near-pure damper (mul=60 ×3455 vs mul=150 ×3).
- **S1: non-regression clean** — win 5–5, capture 6/10 vs 6/10.
- Decision stands: report-and-stop, do NOT retune; manager routes next.

## Known blocker for any future batch on this branch

The user hit a **fatal `NullReferenceException` at `Passenger.cs:187`** during a live
Exp-vs-Exp game — likely related to the **TECN-ferry merge `90a173c4`**, which is in this
branch's base. A separate worker is fixing it on `main`. **Before any future build/batch on
`exp-terr-bias`, rebase/merge that main-side fix.** The BoP verify above ran to completion
without hitting this crash (tournament matches, not the live-play path), but it is a live
risk for further runs.

## Standing by — no further runs until the user lifts the hold.
