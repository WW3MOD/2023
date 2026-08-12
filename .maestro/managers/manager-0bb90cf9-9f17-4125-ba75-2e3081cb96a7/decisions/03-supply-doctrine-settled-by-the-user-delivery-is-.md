# Supply doctrine settled by the user — delivery is unconditional, danger sets distance not permission

_Recorded 2026-08-10T04:59:05.811Z by bdedd544_

The user stated the supply doctrine directly, which resolves the open threshold question in the post-measurement plan by **changing its shape rather than answering it**.

Their words: *"Supplies has to reach the front, one way or another... a platoon that is running low on ammo WILL DIE if they are not resupplied. Sometimes supply trucks needs to risk it to reach them, even if there is danger. They drive in fast, drop the supplies, and drive back out. Full commitment."*

## The three modes

- **Safe:** drive to the platoon, transfer supply **directly without unloading a crate**, retain cargo for the next customer.
- **Dangerous:** drive in, stop ~5 cells short, **unload a crate**, leave immediately. Infantry walk to the crate.
- **Never:** abort because of danger.

## Why this matters more than a threshold value

The plan asked the user to pick an evac-threshold percentile. That question is now **void**, and the reason is worth recording: the existing evac branch pulls a truck 12 cells *back toward its Supply Route* when danger exceeds `EvacDangerUnits` — i.e. danger currently buys **permission to abandon the delivery**. Under the doctrine, danger buys nothing of the sort; it only decides **how close the truck gets and how fast it leaves after dropping**. Evac becomes the egress leg of a completed delivery rather than an alternative to delivering.

## The consequence I did not expect, and which decides the near-term plan

**This decouples the supply-truck work from the unresolved danger-scale question.** A truck that always delivers does not need a calibrated abort threshold — there is no abort. The only danger read left is a much weaker one (drop short or drive in), and a *relative* test satisfies it without any absolute calibration.

That inverts the ordering constraint the plan carried. The plan said the durability-weight audit must land before any threshold is re-derived. That still holds **for the other twelve thresholds**, but the truck work no longer sits behind it. Truck work can proceed now; the scale work proceeds independently.

## Decision taken, posted to the user as a record

Drop-short is gated on a **relative** danger test (is this cell in the top slice of what the bot currently believes) rather than an absolute danger-unit threshold. Alternatives considered and rejected:

- **Absolute threshold after fixing the scale** — correct eventually, but re-imposes the exact blocking dependency the doctrine just removed, and lands nothing this session.
- **Count believed enemies near the customer** — most legible, but discards the belief layer's decay/confidence handling, and a naive implementation reads ground truth through fog, which is the one thing the fog-respecting bots may not do.

Known weakness of the choice, to be handled in the brief: a relative test is meaningless in a quiet opening when nothing is believed dangerous, so it needs a floor to stop a truck dropping short at an empty front.

## Operating mode for this autoburn window

The user set an explicit division of labour: **workers write code and never run simulations; the manager runs every simulation and verifies.** Workers stay alive until their change is verified by a run, then get archived. This is a departure from the usual implement-and-test-in-worktree pipeline and is deliberate — it puts every game launch behind one serialized owner, which the harness now enforces anyway via the single-instance lock added at `f3c7a29e`.
