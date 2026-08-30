# The second pass paid for the launch slot it was meant to size — the test would have blamed the fix

_Recorded 2026-08-27T17:33:07.861Z by 17dc66e4_

Second-pass review of `f8b424f6..71fb97cf` returned **MERGE WITH FIXES**. The C# is clean; the tests are not. Withdrew the pending slot request (`gMMZO66LLL6F6Eh1HGpmd`) with the finding attached rather than let a met condition trigger a wasted launch.

## Why the second pass existed, and why it was right

`f8b424f6` shipped a fatal wedge its author had read twice and flagged the right code path for while naming the wrong cause. Three more commits to that same predicate's plumbing followed, each verified by the same reading that had already failed once. The bet was that an independent reader would find something; it found the most expensive thing available.

## The finding that saved the slot

Both re-pointed scenarios gate PASS on the vehicle *moving away from the depot* (`dist > 2`). **Nothing moves an undocked ground vehicle away from a Logistics Centre.** `Resupply.OnResupplyEnding` takes the rally-point branch only if `rp.Path.Count > 0`; `LOGISTICSCENTER` declares a bare `RallyPoint:` and `RallyPointInfo.Path` defaults to `Array.Empty<CVec>()`, so that branch is dead and it falls through to `QueueChild(move.MoveToTarget(self, host))` — **toward** the host. Vehicles carry `Repairable` not `RepairableNear`, so that guard does not suppress it. `dispatchedBecauseDry: false` means `BeginReturnHome` is unreachable. The himars ends at 1 of 2 with `Essential: true` ammo, so `AutoRearmIfDry` early-returns on `!OutOfEssentialAmmo`.

Result: every real assertion passes, and the `why` chain falls to the final else — *"took its affordable round and NEVER UNDOCKED — it is wedged… This is the CanSelect-without-affordability defect, or its return."* **A false RED naming the fixed defect, on a run proving the fix works.** The empty-depot scenario fails one link further along: the tank correctly undocks and correctly holds via `HoldAndFlag`, whose own comment says this is "the case this must not fire on", and the scenario calls that the wedge.

Fix ordered: discriminate on whether the `Resupply` activity ended (`Actor.IsIdle`, already bound to Lua), not on distance.

## The reusable lessons

**A test's discriminator can be wrong in the direction that indicts the fix.** This is worse than a test that simply fails to discriminate, because the failure text is *specific and confident* and points at the change under test. Had the slot been granted an hour earlier, the run would have produced an authoritative-looking wedge report, and the obvious response — revert the fix — would have been exactly wrong.

**"Verify the mechanism the test relies on, not just the mechanism under test."** Nobody checked that anything moves the vehicle. The assertion was written from a mental model of what *should* happen after undocking.

**De-duplication finds copies in proportion to how hard you look.** The extraction removed three copies of the accept test and the same commit **wrote a fourth copy of a different predicate** — `CanSelect` re-states `CanServeNow` verbatim, a public property whose own doc says it exists "so a unit deciding whether to walk here can ask instead of reproducing the rule." Fix is `if (!CanServeNow) return false;`. Worth recording that the ladder is load-bearing, not padding: `LOGISTICSCENTER` between 1 and 49 supply reserves its remainder and serves nobody, so dropping the `ReservesRemainderForRestock` clause is a wedge on shipped config.

**A guard that blocks one member of a hazard family advertises more safety than it has.** `AcceptTestCannotSeeAPosition` rejects `typeof(Actor)`; `AcceptClient(Rearmable, WPos, out float)` passes it and does the full damage. Pin the parameter list, not one forbidden type.

**A documented residual exposure that does not exist is itself a hazard.** The fixture and `71fb97cf`'s message both name `rearmable.Self.CenterPosition` as reachable in principle. `Rearmable` has no `Self` and no actor reference. A recorded hole invites the next reader to widen the signature in order to close it — the exact damage the guard prevents.

## The open residual I chose not to paper over

**Contention starvation.** `CanSelect` deliberately ignores `currentTarget` (correct — reading it would make a client abandon the dock because someone else is mid-batch). But a docked client defers while a needier aura client is served, and on a Centre kept topped up by `AbsorbsSupplyCache` with a permanently needier neighbour, the reviewer could not prove termination by reading. Same shape as the wedge, narrower. It entered with the deferral in `f8b424f6`, so it is this branch's to own.

Instructed the implementer to bound it by reading and, **failing that, to say so rather than invent a guard** — an honest known limit told to the user beats a speculative fix changing behaviour nobody has measured. That is the same judgement as decision 22's "record, do not fix", applied to a case where the unknown is termination rather than magnitude.
