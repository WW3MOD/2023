# The DEFCON 2 cease-fire cancels by provenance, not by the stance fan-out decision 05 prescribed

_Recorded 2026-09-10T06:08:17.342Z by ffb08fdc_

**Amends decision 05.** Everything else in that decision stands — the chosen lever is still one synced World field read at six sites, and all six were confirmed present at `main @ 27a7f23a` with none needing correction. Only the ONE-SHOT clause changes.

## What decision 05 said, and why it cannot work

It said the flag does not cancel engagements already running, so the transition into DEFCON 2 should "borrow candidate 1 once" — fan out a stance notification, as a one-shot rather than a held state, and let the existing machinery cancel in-flight attacks.

The worker on `wt/defcon-holdfire` declined and said so plainly rather than quietly doing something else, which is the behaviour the brief asked for. **Verified independently before accepting:** `AutoTarget.HasValidTargetPriority` (`AutoTarget.cs:1211-1214`) is a thin wrapper over `GetTargetPriorityBand`, whose first line is

    if (owner == null || Stance <= UnitStance.HoldFire)
        return NoTargetPriorityBand;

That reads the **real `Stance` field**. So the fan-out's cancelling effect is a consequence of stance having actually been mutated. Fanning the notification out without mutating stance — which is what "borrow it once" means — is **inert**. Decision 05 prescribed a no-op.

## Why the obvious repair is worse than the deviation

Making the fan-out bite would mean gating `GetTargetPriorityBand` on the DEFCON flag as well. That reaches further than intended: at DEFCON 2, any stance change on a unit would then cancel that unit's non-force **player-ordered** attack. The whole feature is "every shot is one the player gave", so cancelling player-ordered attacks is its exact inverse. Rejected.

## What ships instead

`CeaseAutonomousFire` cancels **by provenance** — the same axis the rest of the feature runs on, rather than a second one:

- the auto-acquired top-level `IAttackActivity`, using the same top-level-only read and carrying the same recorded PITFALL as `TickPreemption`;
- a new `AttackBase.CancelAutonomousEngagement`, overridden on `AttackFollow`, dropping auto-acquired requested and opportunity targets.

This is more coherent than the original, not merely a workaround: the rule, the six guards and now the cease-fire all key on `AutoTarget.IsAutoAcquiredSource`, the engine's own definition of a shot somebody gave. There is one definition in play, not two.

## The gap this leaves, accepted knowingly

An in-flight `LeapAttack`, `AttackTesla` or `AttackPrism` `ChargeAttack` at the exact tick of the 3→2 transition will land its shot, because those activities do not implement `IAttackActivity`. New ones cannot start — acquisition is gated at site 1 — so it is a one-tick window. Widening the interface to catch it was considered and rejected as disproportionate. Recorded so nobody later reads it as a bug.

## The honest state of verification, which is the weakest part

**None of the six read sites has a test.** Only the predicate does: eight tests covering the level rule, the provenance mapping across every `AttackSource` value, force-attacks, Skirmish as a strict no-op driven through the real state machine, and the full 3→2→1 sequence. The six call sites are verified **by reading only**, because they live in per-actor trait methods and nothing in `OpenRA.Test` can construct a `World`. The worker considered a source-text assertion to at least pin that each guard is present and rejected it as brittle theatre; that judgement is endorsed.

What would actually settle it is an autotest — two units in contact at DEFCON 2, assert zero shots until an explicit attack order, then exactly one. That is a launch, so it is the manager's to run, and it is filed on the backlog rather than left implied.
