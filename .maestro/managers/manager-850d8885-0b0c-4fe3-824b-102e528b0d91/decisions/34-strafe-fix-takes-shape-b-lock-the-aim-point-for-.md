# Strafe fix takes shape (B) — lock the aim point for the run, not per-tick caching

_Recorded 2026-09-02T04:27:12.186Z by 17dc66e4_

## The defect

No `AttackType: Strafe` airframe in WW3MOD can fire. Ever, with any weapon, at any range, on any map. Found 2026-09-02 by worker `7af17c61` after the discriminating scenario `test-strafe-engage` returned `STRAFE ZERO` (run `260902_061538_p86992`: subject `minDist0 shots0 ammoLeft30/30`, control `minDist0 shots9 firstShotTick20`).

The chain, each link cited:
1. `StrafeAttackRun.Tick` calls `SetRequestedTarget(Target.FromTargetPositions(target), true)` **every tick** — `FlyAttack.cs:323-325`.
2. `Target.FromTargetPositions` is `new Target(t.CenterPosition, t.Positions.ToArray())` — `Target.cs:86`. The `.ToArray()` allocates a **fresh `WPos[]` every call**.
3. `Armament.CheckFire` opens `if (!target.Equals(oldTarget))` — `Armament.cs:412`.
4. `Target`'s `operator ==` for `TargetType.Terrain` compares `terrainPositions == other.terrainPositions` — `Target.cs:233`. **A `WPos[]` compared by reference.** A new array is never reference-equal, so two consecutive strafe targets against a *motionless* actor never compare equal.
5. `AimingDelay = Info.AimingDelay` therefore runs every tick (`Armament.cs:415`). Default 15; nothing in `mods/` lowers it.
6. Decremented at most once per tick (`:354-355`) — undone by the reset in the same tick.
7. `CanFire` bails on `IsAiming` (`:392`, `IsAiming => AimingDelay > 0` at `:751`); `CheckFire` returns null at `:421`, before `FireBarrel` and before `UpdateMagazine`.

The corroboration that makes this hard to doubt: **`firstShotTick20` on the control is 15 ticks of `AimingDelay` plus 5 of approach.** The same constant appears on the working lane as a delay and on the broken lane as a permanent block. And `ammoLeft30` of 30 follows because `UpdateMagazine` sits downstream of the bail — not one round spent, not even a miss.

## The two candidate fixes

They differ **only on a moving target**:

- **(A) Cache the terrain target in `StrafeAttackRun`, rebuild only when the aim point moves.** Minimal, one call site. But a target moving every tick still yields a fresh aim point every tick, so `AimingDelay` still resets and strafe still cannot hit movers.
- **(B) Lock the aim point for the duration of one run.** Movers work. Costs the intra-run tracking that `FlyAttack.cs:321-322` explicitly asks for.

## Chose (B)

Two reasons, and the second is the stronger.

**It matches the physical thing.** A strafing run is a committed pass — pick a line, dive, fire along it, pull out. Nothing re-solves its aim point mid-dive. The intra-run tracking is a real loss and is accepted knowingly; a strafe pass that curves to follow a fleeing target is not a behaviour anyone has asked for, and if it turns out to matter it is a smaller problem to solve on top of a lane that can fire at all.

**(A)'s failure mode is invisible to the instrument that would check it.** By the worker's own analysis a stationary-target test cannot distinguish (A) from a whole fix. Shipping (A) means shipping something that reads correct on every check we would naturally run, and is silently half-broken for movers. That is precisely the class of error that has cost this project the most — it is the same shape as the five blind gates found last night, where an instrument returns a confident green about something it never examined.

The worker had already built the discriminator before being asked: `test-strafe-engage` lane 3 (`STRAFEMOVE`, a patrolling t90, deliberately outside the verdict). After a fix, lane 1 firing with lane 3 still zero means (A); both firing means (B). Asked for a prediction on all three lanes before spending the slot, explicitly including what lane 3 at `shots0` would mean — that is the outcome that would say (B) did not work.

## Deliberately NOT doing

**Leaving `Target.operator ==` alone.** Comparing a `WPos[]` by reference is the deeper defect and widening it to a sequence comparison would fix this site and any future one — but `operator ==` is on a hot path and engine-wide equality semantics is not a thing to change off an unmeasured hunch. Recorded in `DISCOVERIES.md` as the general defect with this site as its first known instance. If it bites a second time there will be evidence to justify touching it.

## Provenance worth keeping

The 2026-09-01 retraction pointed the next investigation at `FlyAttack.cs:183` on the strength of a trace showing "a flat 11-13 cell standoff for the whole window". That trace covered the last 250 of 626 ticks and nothing before, because `observeTick` samples a lane only once it has armed and lane 3 never armed. The measured trace now shows the airframe closing to 0 and climbing back out — **the "flat standoff" was the post-pass loiter**, and the previous instrument sampled it exclusively. Two candidate mechanisms (`CalculateStopPosition`, `FacingTolerance: 60`) were killed by this run; the worker marked its own `CalculateStopPosition` suspicion wrong in place.
