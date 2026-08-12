# Autotarget priority lock is the persistent-target override, not the idle-only scan

_Recorded 2026-08-11T07:19:27.206Z by cfcaa2ca_

## Context

User reported the Stryker SHORAD failing to prioritize helicopters while engaging ground units, and asked for a generic priority-respecting retarget for all units.

## What was believed (recon, main @ bd7b6bb2)

`AutoTarget.ChooseTarget` is reachable only from `INotifyIdle.TickIdle`, so an engaged (non-idle) unit never re-evaluates priority. Fix: a periodic preemption scan that switches on a strictly higher priority band.

That premise is WRONG for the reported bug.

## What is actually true (implementer, verified statically)

`AttackFollow.ClearRequestedTarget` (:57-68) does not clear. Under `PersistentTargeting` (defaults **true**, :29; SHORAD does not opt out) it PROMOTES `RequestedTarget` into `OpportunityTarget` and sets `opportunityTargetIsPersistentTarget = true`. It runs from `OnLastRun` every time an attack activity ends.

So the steady state of a stationary engagement is: **actor idle, firing through a persistent opportunity target**. `TryGetAutoTargetOverride`'s persistent branch hands that target back to `ScanForTarget` BEFORE `ChooseTarget` runs, so the priority table is never consulted. The override also returns before the `nextScanTime` re-arm (:941), so the cycle repeats every tick.

The flag clears only at :161, gated on `!IsAiming` — a long reload gap (25mm reloads 250 ticks) eventually breaks the lock. **The bug is "stuck for a long time", not "stuck forever"**, which is exactly the user's "sometimes it is busy shooting at other ground units".

Ruled out: scenario mis-build (both SHORAD ground weapons reach the 8-cell separation, t90 is a valid target, stance is FireAtWill). Also: `IsValidFor` vs `Type != Invalid` on the override is worth tightening but is NOT this bug — the incumbent was valid throughout. The defect is that a valid-but-lower-priority incumbent is returned with no band comparison at all.

## Decision

**Reshape, not merge, not drop.** The override path is the mechanism the user hit and is fixed first. The band machinery already built in commit c061e1ba (`GetTargetPriorityBand`, `ChooseTarget` band out-param) is reused by the override fix rather than discarded. The preemption tick must then justify itself by naming a state the override fix does not cover, or be dropped — one correct mechanism beats two overlapping ones.

## Constraint that makes the obvious fix wrong

`TryGetAutoTargetOverride` also returns `RequestedTarget` for PLAYER orders (a plain right-click is `AttackSource.Default`, `forceAttack: false`, `AttackBase.cs:485`). Making the override yield on priority unconditionally would let autotarget walk over a player's explicit attack order — a worse regression than the bug. The promote must record its source first.

## Test-design correction

A 22-second assertion window passes with or without the fix, because the reload gap breaks the lock on its own. The autotest must assert the higher-priority target is engaged PROMPTLY (a few seconds), not eventually.
