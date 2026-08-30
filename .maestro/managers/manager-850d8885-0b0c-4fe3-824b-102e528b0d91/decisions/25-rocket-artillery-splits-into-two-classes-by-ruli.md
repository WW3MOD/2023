# Rocket artillery splits into two classes by ruling, which may retire the mechanism a day of work just fixed

_Recorded 2026-08-30T04:21:44.575Z by 17dc66e4_

User ruling, 2026-08-30, verbatim: *"Rocket artillery should be possible to rearm at the LC? I thought they were, if not they should be."* and *"Iskander and HIMARS should not be rearmable, they must be evacuated"*. Plus: *"CRAM and AGUN are not used, never mind them for now."*

## What the ruling does

The economy audit found the class **split by accident**: `grad`, `m270` and `tos` have no rearm host at all, while `himars` and `iskander` rearm at the Logistics Centre. Nobody decided that. The ruling inverts it and makes it doctrine:

- **Tactical** (`grad` 680, `m270` 840, `tos` 960 per refill) rearms at the Centre — 3.3, 2.7 and 2.3 refills per full 2250 depot, already sensibly sized.
- **Strategic** (`himars`, `iskander`) is a single load and must leave. A full load is 3000 against a Centre's 2250, so **it was never possible to fully reload one** — the ruling makes that explicit design rather than a number to correct. Prices unchanged and already identical (`Cost: 6000` / `Ammo: 2` / `SupplyValue: 1500`).

## The consequence worth flagging before it is discovered

`himars` and `iskander` are believed to be the **only** actors declaring `replenish-vehicles` — the condition the Centre's push arm selects on. If that holds, making them non-rearmable leaves the Centre's **vehicle push arm with no possible client in the shipped game**, and the docked-double-serve problem that consumed most of 2026-08-27 becomes unreachable.

That is not a reason to resist the ruling — the ruling is coherent and the arithmetic supports it. But it must be **decided rather than discovered**: the worker is instructed to verify the claim, report it, and propose removal versus keeping the arm as a documented hook. Silently deleting it would erase the reasoning behind a hard-won fix. The infantry aura arm (`AuraRearmCondition: replenish-soldiers`) is separate and stays.

**Generalization worth keeping: a user ruling can retire a mechanism you recently invested in, and the investment is not an argument against the ruling.** The correct move is to surface the retirement explicitly so the decision is taken knowingly, not to defend the code.

## The part that may not be a YAML edit at all

"They must be evacuated" presumes something makes them evacuate. An `Evacuate` behaviour exists (command bar at `chrome/ingame-player.yaml:597`, an `Evacuate` tier in `SupplyHuntMath.DecideAutoDisposition`, an `InitialResupplyBehavior` field at `AmmoPool.cs:424`), but `AmmoPool.cs:543-570` reads `autoTarget?.ResupplyBehaviorValue ?? ResupplyBehavior.Auto` — **so the default decides it**. With no rearm host, a dry `himars` may simply stand still. The worker must establish this by reading before building; if it stands still, the ruling needs a default set or a behaviour built, which is a materially bigger change than removing two YAML lines.

Also instructed: removing rearm means **both** paths. `Rearmable.RearmActors` governs the pull path; the `ExternalCondition@VehicleReplenish` granting `replenish-vehicles` is what lets the push arm select the unit while docked. Removing only the first leaves the second live — the same could-own-versus-will-serve distinction that produced this week's wedge, in a different costume.

## Dropped

CRAM and AGUN having no rearm host — I raised it as a suspected defect; the user says they are unused. Closed, not fixed.
