# Garrison destructibility: user ruled leave-as-is, and reframed the whole question as rubble

_Recorded 2026-09-01T06:36:36.716Z by 17dc66e4_

The user answered the garrison AUQ on 2026-09-01. Recording it because the *reasoning* he gave is worth more than the option he picked, and because a future audit will otherwise rediscover the dead code and file it again.

## What he picked

**"Leave it as-is for now."** All 41 garrisonable civilian buildings (plus GTWR/PBOX/HBOX) keep `Indestructible: true` with no occupancy gate.

Consequence to write down so nobody re-investigates it: `EjectOnDeath`, both `Explodes` traits, every `SpawnActorOnDeath` husk, and the garrison module's own `INotifyKilled` handler remain permanently unreachable. That is now **deliberate**, not a bug. Someone authored all of it and none of it can run.

## The alternatives, and why they were rejected for now

- *Gate `Indestructible` on occupancy* — an empty civilian building stays permanent terrain, a garrisoned one can be blown up. Reactivates the authored eject/explode/husk content and makes explosives a real answer to a garrison. Not chosen, but note this is the option the unreachable content suggests was once intended.
- *Drop `Indestructible` entirely* — simplest and most consistent with the authored content, but changes the shape of every map carrying civilian buildings, not only garrisoned ones.

Neither was rejected on merit. Both were deferred against release timing.

## The reframing, which is the actually valuable part

His words: *"in reality a building is rarely leveled with the ground, there is always rubble. So the fully damaged state is the 'destroyed' state of a building, where it is just rubble."* And the counter-pressure he supplied himself: *"if you drop enough bombs on a building eventually it will not be possible to occupy it safely."*

This dissolves the binary the question was posed in. The choice was framed as destructible-or-not; he is saying the interesting state is **terminal-damaged-but-standing**, which the existing art already gives us — healthy and damaged sprites both exist. If the engine can express a damage floor plus occupant attrition, that is potentially the whole feature with zero new art. Whether it *can* is what worker `e2f6de0a` is costing out.

## Long-horizon, explicitly post-release

Documented, not built: AI-generated intermediate damage-state sprites derived from the existing art; multi-block interconnected buildings where parts can be destroyed and soldiers are forced to relocate within the structure. His own framing — *"that is all a lot of work and now we are in a development phase of trying to get our initial release ready"*.

## Launch spend

Of six candidate runs he chose **Q3 only**: can a non-owner evacuate men from a building an enemy now owns? If not, men who garrison a building that flips ownership are permanently lost, which promotes the co-garrison oddity to a real bug. One map, no instrumentation, engine order-pipeline question.

## Standing instruction attached to all of it

*"the whole garrisoning system needs to be audited from the bottom up"* — but *"lets keep it real about what we should do to make it work well with minimal effort."* So the deliverable is a ranked, costed proposal against release, not a wish list. An honest costed "no" on the rubble path is worth more than an optimistic yes.

## Correction that must ride along

An earlier brief asserted the ~44 garrisonable buildings are Neutral-owned. **Garrisoning TRANSFERS ownership** — `DynamicOwnership` defaults true, and `DOCS/reference/architecture.md:200` already said so. The brief was written from the backlog item without reading the curated doc it was pointing the worker at. Anything downstream of the Neutral-ownership premise needs re-deriving.
