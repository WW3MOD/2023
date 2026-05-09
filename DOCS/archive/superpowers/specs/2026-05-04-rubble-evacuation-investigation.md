# Rubble Building Evacuation — Investigation Notes (Phase 3.1)

Date: 2026-05-04
Topic: Why the player can't issue an Unload order on a 1HP "rubble" garrison building.

## Summary of relevant files

- `engine/OpenRA.Mods.Common/Traits/Cargo.cs` — implements `IIssueOrder` + `IResolveOrder` for "Unload".
- `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs` — also `IResolveOrder` for "Unload" (clears port state).
- `engine/OpenRA.Mods.Common/Activities/UnloadCargo.cs` — the activity Cargo queues.
- `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/GarrisonPanelLogic.cs:42-51` — the "Eject All" button.
- `mods/ww3mod/rules/ingame/civilian.yaml` — `^CivBuilding` template (no condition gate on Cargo / Unload).
- `mods/ww3mod/rules/defaults.yaml:146` — `^DamageStates` template (which conditions are granted at each damage state).

## What conditions are active at 1HP rubble?

`^DamageStates` grants a stack of conditions based on `Health.DamageState`. At 1HP the DamageState
is `Critical`, so the active conditions are:

- `damaged`
- `light-damage-attained`, `medium-damage-attained`, `heavy-damage-attained`
- `critical-damage`

(There is no separate "rubble" condition — 1HP is just the floor of the Critical state.)

## What gates the Unload button visibility / issuance?

There are two UI entry points and they share the **same gate**: `Cargo.IsEmpty()`.

1. `Cargo.Orders` (line 226–229 of Cargo.cs):
   ```cs
   if (!IsEmpty())
       yield return new DeployOrderTargeter("Unload", 10,
           () => CanUnload() ? Info.UnloadCursor : Info.UnloadBlockedCursor);
   ```
   When `IsEmpty()` is true (zero passengers in `cargo` list), the deploy targeter does
   not yield at all → no Unload order can be issued via right-click / Stop / deploy.

2. `GarrisonPanelLogic.cs:50`:
   ```cs
   ejectAllButton.IsDisabled = () => selectedGarrison == null || cargo == null || cargo.IsEmpty();
   ```
   Same gate. The "Eject All" button greys out when shelter is empty.

## What gates the Unload order resolution?

`Cargo.ResolveOrder` (line 247–254):
```cs
if (order.OrderString == "Unload")
{
    if (!order.Queued && !CanUnload())
        return;

    self.QueueActivity(order.Queued, new UnloadCargo(self, Info.LoadRange));
}
```

`Cargo.CanUnload()` (line 266–278):
```cs
return !IsEmpty() && (aircraft == null || aircraft.CanLand(...))
    && CurrentAdjacentCells != null && CurrentAdjacentCells.Any(c => Passengers.Any(...));
```

So even if the order is somehow issued, `CanUnload()` rejects it when `IsEmpty()` is true.

`GarrisonManager.ResolveOrder` (line 1255–1276) handles the "Unload" case for **port**
soldiers — revokes their `garrisoned-at-port` condition and clears `DeployedSoldier`. It
relies on Cargo's own handler running to eject **shelter** soldiers ("Shelter soldiers
will be ejected by Cargo's normal Unload handling" — comment at line 1274).

`UnloadCargo` activity has no HP / damage condition gate.

`AttackGarrisoned.cs` has no `RequiresCondition` involving damage states (verified by
ripgrep).

## Root cause hypothesis

**Cargo's `IsEmpty()` gate excludes garrison buildings whose soldiers are all currently
deployed at ports.** This is the lifecycle:

- 8 soldiers enter shelter → cargo.PassengerCount = 8.
- GarrisonManager auto-deploys soldiers to ports. For each deployment, GarrisonManager
  calls `cargo.Unload(self, soldier)` (line 317 of GarrisonManager.cs) which removes the
  soldier from `cargo.cargo` list. The soldier is then placed in-world at the port.
- Once all 8 soldiers are deployed, `cargo.PassengerCount == 0` → `Cargo.IsEmpty() == true`.

At this point, both the Unload deploy targeter AND the GarrisonPanel "Eject All" button
treat the building as "no passengers to eject". But the **port** soldiers still exist —
they're tracked by `GarrisonManager.PortStates[i].DeployedSoldier`. The Unload UI is blind
to them.

This becomes most painful at 1HP rubble because:
- The user wants to evacuate to save soldiers from the now-low-protection building.
- Combat triggers suppression, which can recall soldiers back to shelter (then cargo
  becomes non-empty and Unload works again briefly), or push them into a state where
  some are deployed and some are in shelter.
- The exact "all deployed, none in shelter" state is most likely to occur during an
  active engagement — exactly when evacuation is needed.

**This bug is NOT a damage-condition gate.** It's a structural mismatch between Cargo's
notion of "passengers" (only shelter occupants) and the player's notion of "everyone
still inside this building" (port + shelter).

## Fix plan (Task 3.2)

Smallest correct change: extend the Unload UI gate so it also yields / is enabled when
GarrisonManager has any DeployedSoldier. Two options:

A. **Engine change in Cargo.cs** — replace the `!IsEmpty()` check in `Orders` and the
   `IsEmpty()` check in `CanUnload()` with a hook that also asks GarrisonManager. This is
   ugly (Cargo would need to know about GarrisonManager) and would couple them.

B. **Engine change in GarrisonPanelLogic.cs + a sibling helper in GarrisonManager.cs** —
   add `GarrisonManager.HasAnyOccupants` (true if any port or shelter soldier exists) and
   use it in the UI gate. Then add a separate handling path so the right-click Unload
   targeter checks the same — easiest is a new `IIssueOrder/IOrderTargeter` on
   GarrisonManager that yields Unload when `HasAnyOccupants && CanUnload`-equivalent.

Plan opts for the **least invasive** thing: have GarrisonManager itself emit an "Unload"
order targeter when any port soldier is deployed, AND fix the Eject All button to also
check port state. Cargo's own gate is left as-is (still correct for non-garrison cargo).

For port soldiers, no actual cargo-eject is needed — they're already in-world. So
GarrisonManager's existing IResolveOrder Unload case (which clears port references) is
sufficient. The fix is purely about **letting the order get issued** in the first place.

Concretely:
1. `GarrisonManager.cs`: add public `HasAnyOccupants` property (true if any port has a
   deployed soldier OR shelter is non-empty).
2. `GarrisonManager.cs`: implement `IIssueOrder` so it emits an Unload deploy targeter
   when `HasAnyOccupants` is true. (Cargo will still emit its own when cargo is non-empty,
   but the framework dedupes by OrderID priority — easier alternative: only emit from
   GarrisonManager, and accept potential duplicate. Or guard against double-emission.)
3. `GarrisonPanelLogic.cs:50`: change `cargo.IsEmpty()` to also consider port soldiers.

Simplest possible fix path for 3.2: just fix the Eject All button (the primary UI for
evacuation), and add an `IIssueOrder` on GarrisonManager for the right-click path. Keep
the changes small and audit-able.
