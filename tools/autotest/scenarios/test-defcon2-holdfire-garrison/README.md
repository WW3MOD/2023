# `test-defcon2-holdfire-garrison` and its discriminating twin

```bash
./tools/autotest/run-test.sh --hidden test-defcon2-holdfire-garrison            # expect PASS
./tools/autotest/run-test.sh --hidden test-defcon2-holdfire-garrison-skirmish   # expect OK(fail)
```

No `--speed` and no `--timeout`. The treatment is ~700 ticks — settle 15, walk-in ~150, hold 300,
ordered phase a few dozen — i.e. ~45 s of wall clock at `Timestep: 60`, inside the watchdog's 300 s
(wall clock, **not** scaled by `--speed`). The twin ends far earlier, as soon as the garrison acts.
**Both arms are required.**

## Why this needs its own scenario

Read site 5 is `GarrisonManager.ScanForTarget` (`GarrisonManager.cs:1028`), and the trait's own
comment is the argument for a separate rig: it is *the garrison's second, independent scanner*. It
does not go through `AutoTarget` at all — it picks its own target per port and `AttackGarrisoned`
fires at `PortState.CurrentTarget` directly — so **none of AutoTarget's guards can see it**, and no
amount of tank-versus-tank coverage reaches it. All four autonomous garrison paths (the empty-port
deploy, `PromoteFromShelter`, `UpdatePortTarget`'s re-target, `TriggerAmbushDeploy`) reach a target
only through that one method.

The ordered half matters for the same reason it does in the contact scenario, and here it also
exercises a *different* code path rather than the same one with a different source: `SetForceTarget`
(`:1248`) deploys a shelter soldier to every port whose arc contains the target and marks it
`PlayerOverride`, and `UpdatePortTarget` honours that **before any scan**. So phase 2 proves the
hold refuses acquisition without silencing the building.

## Why the pair discriminates

One quantity differs, `DefconEscalation.ModeDefault` (`Escalation` → `Skirmish`). In the twin
`DefconEscalationState`'s constructor sets `Level = NoLevel` (0), `HoldsFire(0)` is false, and the
garrison's own scanner runs normally. An empty port then deploys as soon as `ScanForTarget` returns a
target that survives `TargetConfirmTicks` (10) across `TargetScanInterval` (8) — call it **~30
ticks** from the house flipping to Russia. The hold window is **300**, ten times that, so the twin
cannot fail merely for want of time and the treatment cannot pass for want of a scan.

Two setup choices are what make the control able to go red at all, and both are easy to undo by
accident:

* **The humvee is INSIDE the garrison's envelope.** An empty port derives its search radius from the
  **shelter** soldiers' armaments (`:984-998`), and `5.56mm.E3` is `Range: 10c0`; the probe sits
  ~6.5 cells out. This is the exact inverse of `test-human-autoacquires-garrisoned-house`, which puts
  its abrams *sixteen* cells out precisely so no port can ever deploy. Move the humvee out past ten
  cells and the twin stops failing while the treatment still passes — the pair silently stops
  discriminating.
* **No `AutoTarget` is added to `V01`.** `^CivBuilding` ships none, so
  `autoTarget?.Stance ?? UnitStance.FireAtWill` leaves the building on FireAtWill in both arms. The
  sibling scenario *adds* one at `HoldFire` for the opposite purpose. Adding one here has the same
  effect as moving the humvee: the control goes quiet for a reason that is not the rule.

A humvee and not a tank because `^5.56mm` carries `InvalidTargets: Medium, Heavy` — a rifle cannot
engage an abrams or a t90 at all, so a silent garrison would have proved nothing. The humvee is
`Armor: Light` / `TargetTypes: Ground, Vehicle, Light`, which `5.56mm.E3`'s `ValidTargets: Infantry,
Vehicle, AirLight` matches. `Test.GetTargetOrder(House, Probe)` re-checks that live, after the
garrison forms, rather than leaving it to this paragraph.

## What counts as the answer

* **Treatment PASS** — census line `[defcon2-garrison] PASS.` in `lua.log` with `defcon=2`, `4
  shelter`, `0 ports` throughout the hold, and then at least one port manned and at least one rifle
  having fired after the click.
* **Twin `OK(fail)`** — the reason names a manned port (usually) or a rifleman's ammunition, with
  `defcon=0` in the census. That is the arm working.
* **Twin PASS** — red, and it falsifies the treatment. A garrison on FireAtWill, with a valid target
  inside its own envelope, in an ordinary Skirmish, did nothing for 300 ticks. Stop and find out why.
* **Either arm reporting `fail: SETUP` or `fail: STAGING`** — a staging fault, not a finding, and it
  applies to both directories: the house did not start Neutral, the riflemen were not offered an
  `EnterTransport` order, they did not all reach the shelter, or the house and humvee are not a live
  targeting pair. Fix the scenario before reading either verdict.
* **Treatment reporting `the ORDERED attack produced nothing`** — the hold is over-broad: it is
  refusing fire a player asked for. Not the same bug, and worse.

## What the primary observable is, and why it is not damage

`Test.IsAtGarrisonPort` — is a port manned. `DeployToPort` does `SetPosition(soldier, self.Location)`,
so a manned port is readable state, it is **upstream of every shot**, and it is the specific thing
`ScanForTarget` gates. Ammunition (latched per man), the humvee's HP and `Test.GetImpactEffectCount`
ride alongside as independent confirmations. Note the sibling's finding on why damage alone is a poor
observable here: a port soldier stands on the **building's own cell**, so splash aimed at him moves
the building's HP too, and warhead damage never consults the `AttackBase` targeting gates.

`diff -r` the two directories before believing any result that rests on the comparison.
