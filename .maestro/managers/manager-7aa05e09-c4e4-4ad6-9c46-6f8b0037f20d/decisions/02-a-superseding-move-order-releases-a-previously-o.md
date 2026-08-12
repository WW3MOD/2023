# A superseding move order releases a previously-ordered target for autotarget re-evaluation

_Recorded 2026-08-11T10:57:00.614Z by cfcaa2ca_

## Context

`wt/autotarget-preempt` introduces an invariant (`TraitsInterfaces.cs:464-467`): a target a player, Lua or bot explicitly ordered may never be yielded away by autotargeting. Three laundering routes that violated it were found and closed (`ScanAndAttack`, `AmbushTickIdle` ×2). The final review found two more: `SmartMoveActivity.cs:73→:117` and `AttackMoveActivity.cs:116→:173`.

Emergent detail worth preserving: **FIX 4 activated the attack-move route.** Widening the yieldable set to include `AttackSource.AttackMove` (correct on its own terms — attack-move engagements are auto-acquired, not deliberate) converted a previously-inert re-stamp into a live laundering route. Neither change was wrong in isolation; the interaction is invisible from either one alone.

## Options

(a) Thread `fromProtectedOverride` at both sites, mirroring the three already-closed routes. Preserves the invariant as literally stated.
(b) Narrow the invariant: the guarantee covers the idle and ambush re-issue paths; an engagement acquired opportunistically *during a superseding move order* is autotarget's to re-evaluate.

## Decision: (b), conditional on a reachability check

The laundering sequence requires at step 3 that a NEW move / attack-move order be issued. By that point the player's earlier attack order has already been cancelled by the newer order — what survives is only persistent-target residue in the trait. Treating that residue as authoritative makes the unit cling to a target the player has explicitly moved on from. That is worse than the bug and contrary to the user's stated goal ("units should always stop and retarget when a high value target is available").

Option (a) would be *safer-looking* while producing worse behaviour: the classic failure of protecting a ghost order.

**The ruling is conditional.** It stands only if `SmartMoveActivity` / `AttackMoveActivity` cannot be reached without a fresh superseding order — i.e. nothing queues either internally (another activity, bot module, Lua, scripted mission). The implementer must establish that before the doc is narrowed; if such a path exists, the premise fails and option (a) is correct instead.

## Requirement either way

The doc must state WHY, not just what, or the next reader "fixes" the two sites back — and the FIX-4 interaction must be recorded before anyone widens the yieldable set again.
