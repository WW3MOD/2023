# Hold the offense-side lever behind the top-up worker instead of running both

_Recorded 2026-08-15T17:02:44.278Z by 3e3b90b7_

The user answered the long-open lone-tank question, picking **"stop the offensive layer walking infantry a transport could carry"** — with the note *"I am not quite understanding but I chose to trust you."* Treating that as delegated judgement, not a mandate to move fast.

## The collision that forced a sequencing choice

The chosen lever and the already-dispatched top-up worker are **the same contest from opposite sides**:

- Top-up worker: the carrier issues one boarding order per passenger and never re-issues it, so it loses every subsequent contest for that soldier. Fix = make the carrier re-offer.
- Chosen lever: `StageFreePool` claims armed infantry at **tick 3** and walks them to the attack anchor. Fix = make offense stop claiming what a carrier could take.

They are the *same soldiers*. Running both at once means two workers editing the boarding path concurrently, and — worse — two fixes whose individual effects cannot be attributed, on a task whose entire acceptance criterion is a seat-count number.

## Options considered

1. **Spawn the offense-side worker now, in parallel.** Rejected: file collision in the transport/offense boarding path, and it would corrupt the top-up's before/after measurement, which is the only thing that closes the user's actual complaint. It also has no run grant — the three granted runs are committed.
2. **Redirect the running worker onto the offense side instead.** Rejected: it is mid-task with a briefed budget, and the top-up may be independently correct. Also throws away a baseline about to be taken.
3. **Hold the offense lever in the backlog until the top-up reports.** Taken.

## What made this cheap rather than a delay

Sent the running worker the offense-side finding as **context, explicitly not a task**, with one instruction that changes its incentives: if its baseline shows passengers being claimed and walked before the carrier can top up, report plainly that re-offering cannot win — *a clean negative is a fully successful outcome here* — rather than escalating the re-offer to force a green.

That converts the sequencing hold into an information gain: the top-up baseline is now also the measurement that tells us whether the offense-side lever is the only one that can move the seat count. If it is, the queued work starts with its premise already proven and the user's run grant buys a fix rather than a diagnosis.

## Explicitly not doing

Making a mixed soldiers-plus-technician load expressible — which is what the user *literally described* when they raised this. The technician's exclusion from the general passenger pool is deliberate: an earlier commit built a directed reservation path so the capture layer would not compete with the frontline pool for the same unit. Undoing it re-opens a bug that was closed once already. Recorded in the backlog item so a future session does not quietly re-scope it in.
