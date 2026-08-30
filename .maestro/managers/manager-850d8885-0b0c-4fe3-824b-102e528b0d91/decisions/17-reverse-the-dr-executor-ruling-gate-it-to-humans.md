# Reverse the ^DR executor ruling: gate it to humans, because the mechanism I justified it with cannot occur

_Recorded 2026-08-27T01:01:37.406Z by 17dc66e4_

I ruled that `-StancePositioningExecutor:` on `^DR` should stay as a blanket removal and be disclosed as a **human-facing fix**, on the stated mechanism that repositioning a human's drone operator destroys the sortie it just launched. I attached an explicit out: stop and report if writing that paragraph makes you doubt the reasoning. Worker `8091c81e` wrote it, doubted it, and stopped.

**The mechanism is false.** The executor's only mover sits inside `TickIdle`, and a drone operator is never idle while its drone is airborne. It could not have been destroying anyone's sortie. So the blanket removal costs a human something real (cover-seeking between sorties) while protecting nothing.

## Ruling

**Gate the executor to humans**: narrow `RequiresCondition` on `^DR` to `enable-tactical-positioning`, drop `GrantConditionOnBotOwner@tacpos`, keep `GrantConditionOnHumanOwner@tacpos`.

The defect FIX 2 exists to close is bot-side and *only* bot-side — the unconditional `Ledger.Release` deletes the drone claim, and what then steals the operator is `PoiOffensiveBotModule` and `LaneAmbushBotModule`, neither of which touches a human-owned unit. A bot-side defect gets a bot-side fix, and the branch stops carrying any player-visible change at all — which is the property I was reaching for with the wrong argument.

Rejected **keep-and-re-justify** (as insurance against the unobserved wedge derivation): unfalsifiable, and it would change human behaviour to buy protection against a mechanism now shown impossible. That is the exact shape this session has punished repeatedly — and I would have been asking a worker to write it into a commit message.

Rejected **drop the removal entirely**: the per-tick refresh narrows the window without closing it, and the reviewer's point stands that `GoalGuardLedger.Release` keyed on the actor rather than the objective is a general hazard rather than one module's bug. Keep both layers; aim the second at bots.

## A second thing this retires

The reviewer's warning that fixing FIX 1 would **unmask** FIX 2 assumed the fix would make the operator idle mid-loiter. The shipped fix did not — it changed the module's gate to `IsStationary` and left the activity alone — so the unmasking never happened and the operator is still never idle while its drone is airborne. The two fixes did not have to be coupled after all. That goes in the commit message, or the next reader inherits the coupling as received wisdom.

## What I should have caught, and the general form

Both the reviewer and I reasoned at length about what this trait does to a drone operator without either of us establishing **where its mover runs**. That single fact — `TickIdle`, therefore cannot touch an actor mid-activity — determines the answer and neither of us looked it up. It also generalises past drones: it constrains what the trait can do to *any* unit holding a long activity.

The general form, and the third instance today: **a trait's gating condition tells you when it is enabled; it does not tell you when it acts.** I reasoned from `RequiresCondition` and the grant traits, which is the enable question, and drew a conclusion about the act question. Check the tick hook before reasoning about effects.

## What kept this from shipping

Not review, and not my judgement — I had already ruled. It was attaching a falsification clause to the ruling: *if writing the justification makes you doubt it, stop.* The worker used it. Worth attaching routinely to any ruling whose support is a mechanism nobody has observed, because it costs one sentence and it is the only thing in the pipeline that catches a confidently wrong manager.
