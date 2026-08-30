# The free trickle is one site, not a class — and the fix is scoped by a grep, not a run

_Recorded 2026-08-27T16:27:25.368Z by 17dc66e4_

Worker `376eaf7c` closed both measurement questions at `adb221ca` and left one open item it correctly called "a grep, not a run": whether `truk` and `supplycache` carry the same ungated proximity grant as the Logistics Centre. I ran it rather than dispatching it, because the answer decides the fix's scope and cost seconds.

## What the numbers settled

**Infantry trickle is free.** Rifleman three cells from a Centre held at zero gained **14 rounds in 700 ticks** — one per 50, exactly `ReloadAmmoPool`'s default `Delay: 50` / `Count: 1`. The *rate* identifies the mechanism, not merely the effect; `Test.GetSupply` read 0 at all 28 samples. RED: deleting `-ProximityExternalCondition@ReplenishSoldiers:` and nothing else held him at 0 all run with the guard's specific text.

**A docked himars is served by both arms.** Docked t100. t150: ammo 0→1, supply 2250→750 (the one affordable 1500 batch). t225: ammo 1→2, supply **still 750** — below a batch, so unpayable. Two arms, one unit, 75 ticks apart, both docked.

The worker's own Watch — that `SelfAssignedErrandIsOver` might end the errand before the arms could overlap, yielding a null indistinguishable from "could not have happened" — was **refuted by the trace rather than dodged**. Worth generalizing: promoting a worker's Watch into a pre-committed gate before the run is what converted an ambiguous negative into a positive.

The RED's fail text carried a second clause, `docked double-serve: true`, making the himars half a control on the control: it uses `replenish-vehicles`/`unit.docked`, neither granted by the deleted trait, so it had to come out byte-identical — and did. One variable removed, no others. The worker invented that; I had not asked for it.

## What the grep settled

`ProximityExternalCondition@ReplenishSoldiers` exists at **exactly one site in the mod**: `structures.yaml:455`, the Logistics Centre. Every other `ProximityExternalCondition` grants `unit.docked` or `onground`. `truk` (`vehicles.yaml:572`) and `supplycache` (`vehicles.yaml:684`) reach soldiers through `SupplyProvider`'s **metered** arm — `RearmCondition: replenish-soldiers`, 5c0, `RearmDelay: 6` — which passes the `SupplyProvider.cs:968` affordability skip and is already paid for.

**So there is no free trickle in the field; there is one, and it is at the Centre.** The LC is the anomaly: it alone carries both a bare 4c0 proximity grant (free) and a metered aura arm (`AuraRearmCondition`, `:488`) serving the same clientele, and the free one wins — which is why the metered one has never been observed to matter for infantry.

## The decision

Greenlit the build without another user question. The ruling — *"all supply always costs, nothing is free ever… when rearming from anywhere supply is always drawn from the rearming actor"* — reaches the infantry trickle through "from anywhere", so closing it **implements the stated intent rather than choosing between intents**. Same test as decisions 18/19. Alternative considered and rejected: ask whether the infantry site is in scope. Rejected because the ruling's wording already answers it, and asking would have parked a settled question.

## The gap I handed back

The RED ran at a **drained** Centre, so zero rounds is consistent with both "the metered arm still works" and "the metered arm is also broken". Before the proximity grant is removed, the worker must establish by reading that `SupplyProvider` grants `replenish-soldiers` itself on the metered path (`SupplyProvider.cs:850-853`), so a soldier at a *stocked* Centre still refills and pays — with instruction to ask for a slot rather than assume if reading cannot settle it.

Also told it not to re-derive the orphaned-token leak at `SupplyProvider.cs:855-885` (`ExternalCondition.permanentTokens` has no source-death sweep): already closed by the Killed / Disposing / RemovedFromWorld trio and covered by `SupplyProviderExitTest.cs`. It reads like a third free-ammo route and is not one.
