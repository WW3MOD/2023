# The launch restriction is lifted; the constraint is now serialization, not scarcity

_Recorded 2026-08-30T06:28:05.731Z by 17dc66e4_

## The grant

User, 2026-08-30, in response to a question asking which of five launch-gated items were worth funding. They declined to pick and instead removed the gate entirely:

> "Do as many launches as you need, at the end when all lands you can do one launch and try to experience the whole game experience, even menus etc, and start making a list of improvements, and put them in the pipeline. Some can be low priority, but anything you can think of to polish everything one last time. That will be the next task, AFTER everything else lands"

## What this changes, and what it does NOT

**Changes:** the `no-autonomous-multi-test` rule in CLAUDE.md requires "explicit goahead in the current turn" before batch runs, tournaments, or repeated `run-test.sh` invocations. This is that goahead, given as a standing grant for the current body of work. Workers may now be told to run RED *and* GREEN without coming back between them. Nine claimants were queued behind this; all are funded.

**Does NOT change:** launches still **serialize**. That is a physical constraint, not a policy one — one game at a time on the box, and `debug.log` / `lua.log` are global with no run identity, so a concurrent second run silently corrupts both readings. The manager continues to hand out the machine one branch at a time. A worker is told explicitly "you have the machine" and must report before the next gets it.

Also unchanged: **read the verdict from the run directory's `result.json` by path.** Never from `debug.log`, `lua.log`, a pipeline exit status, or `run-test.sh | tail` — that returns tail's exit code and has inverted a result in this repo twice. This grant increases the number of runs and therefore the number of chances to misread one.

## The ordering chosen, and why

Priority is **whatever unblocks a merge**, because the user's final polish pass is explicitly gated on "AFTER everything else lands." So:

1. `wt/evac-afford` — fully staged, RED sabotage specified, nothing between it and merge but the two runs.
2. `wt/cursor-honesty` — pathing RED; the rest of the branch is merge-ready pending the Passenger ruling.
3. `wt/truck-refills-lc` — three review defects outstanding; gets the machine once they land.
4. `wt/recon-battle-feedback` — its static analysis already bounds the answer; lowest value per slot.
5. The four backlog items (drone match, strafe lane, resupply bar, tournament verdict).

## The second half of the ruling — a new final task

The user has defined the closing task of this arc: **one launch to experience the whole game end to end, menus included, producing a polish list filed into `WORKSPACE/PIPELINE.md`** with items allowed to be low priority. It is explicitly sequenced last. Filed to the backlog so it cannot be started early — starting it before the branches land would produce a list against a game that is about to change.

## The threshold ruling, recorded in the same turn

For the truck/Logistics-Centre gesture, "empty" means **at or below the truck's existing `RestockThreshold: 50`**, not literally zero. Chosen over the literal reading because it reuses a number already tuned and shipping rather than introducing a second, and because it stops a nearly-dry truck dribbling 20 supply into a depot and immediately needing to refill. The transfer amount was explicitly not part of it: the transfer runs until the giver is dry or the receiver is full.
