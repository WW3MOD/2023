# The no-fly line is three layers on one chokepoint, and the line itself is written only once

_Recorded 2026-09-09T11:28:13.038Z by ffb08fdc_

The user chose the strictest wall — nothing crosses, aircraft included. The air sweep at `364ea190` found a genuine chokepoint and, more usefully, found that the ground half's work already carries the air half most of the way.

## The line is drawn once, not twice

`GrantConditionOnTerrain` reads `self.Location` with **no altitude gate**, and for an aircraft `self.Location` is the cell containing its centre position. It resolves through `Map.GetTerrainInfo` to `CustomTerrain`. So the `CustomTerrain` write that makes the wall solid to ground units is **already visible to an aircraft flying over it, today, with no new engine code**, and there is live precedent in the mod for that trait. There is no second geometry to author, no second data path, and no risk of the two drifting apart.

## Three layers, in the order the player meets them

**1. Refuse the order.** `Aircraft.ResolveOrder` already refuses on two grounds and the move targeter already paints a blocked cursor for out-of-bounds destinations, so the hook exists. This is the layer that does the work: nothing starts, nothing looks broken, and it costs no in-flight state to unwind when the phase ends. It leaks a lot on its own — attack standoffs, attack-move, return-to-base, idle drift, rally replay from the Supply Route, and any straight-line flight that merely cuts a corner across the line on the way to a legal destination.

**2. Turn back on approach.** A trait modelled on `CarrierSlave.ReturnWithinDistance`, which is the same problem already solved for the drone leash and is live on the quadcopter. Being trait-level and ticked, it catches everything regardless of which activity is running, and it looks right — the airframe banks and flies home using ordinary movement. Two costs, both accepted: it overshoots by check-interval times speed, so it is soft by construction, and it cancels the player's order, which reads as the unit disobeying. That is tolerable at DEFCON 3, where the order was illegal anyway.

**3. Refuse the write.** A guard in `Aircraft.SetPosition`, which is a true chokepoint — every horizontal, vertical and teleporting position change of anything carrying the `Aircraft` trait passes through it, verifiable by exhaustion rather than inference. This is the hard guarantee, and it should almost never fire, because layers 1 and 2 sit in front of it. **It must zero `CurrentVelocity` when it rejects a write**, or a helicopter keeps a non-zero synced velocity while its movement type computes as stationary, and the animation stops agreeing with the simulation.

Layer 3 also catches things that are not flight and each needs an explicit decision rather than inheriting the rule: a falling husk, a crash-landing helicopter, the repulsion nudge, the arrival snap teleports, and creation placement.

## Rejected

**Pausing the aircraft on a condition.** It lands the aircraft on the line where the terrain allows, which reads as a shootdown rather than a boundary, and it does not stop a helicopter at all — the sliding velocity path never reads the modified speed.

**Damage on entering the marked cells.** Not a barrier, a deterrent; it kills rather than prevents, and a fast aircraft crosses before it dies.

**Generalising the map-edge repulsion.** Mechanically a one-predicate change, and it inherits every weakness: it is a force rather than a barrier so a faster aircraft simply beats it, it only runs while cruising so anything climbing or landing is exempt, and it is switched off outright on most of this mod's airframes.

## Recorded so it is not later read as a hole

**Missile bodies are not aircraft.** The live nuclear and cruise powers spawn a `BallisticMissile`, a separate trait with its own position chokepoint, and nothing keyed on `Aircraft` touches them. That is moot rather than a gap: DEFCON 3 is the positioning phase and nobody may strike during it, so no missile should be in the air. If strikes ever become legal while a wall is up, this is where it leaks.
