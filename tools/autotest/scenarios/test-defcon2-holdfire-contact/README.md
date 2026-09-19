# `test-defcon2-holdfire-contact` and its discriminating twin

```bash
./tools/autotest/run-test.sh --hidden test-defcon2-holdfire-contact            # expect PASS
./tools/autotest/run-test.sh --hidden test-defcon2-holdfire-contact-skirmish   # expect OK(fail)
```

No `--speed` and no `--timeout`. The run is ~590 ticks of simulation, i.e. ~35 s of wall clock at
`Timestep: 60`, comfortably inside the watchdog's 300 s (which is wall clock and is **not** scaled by
`--speed`). **Both arms are required.** The treatment alone is not evidence — see below.

## What the pair proves

`DefconFireDiscipline.Permits(level, source, forceAttack)` is the DEFCON 2 rule: everything is
permitted at every level except the hold-fire rung, and there, only what somebody ordered. Only the
predicate has NUnit coverage (`DefconFireDisciplineTest`). The six sites that consult it are
per-actor trait methods and nothing in `OpenRA.Test` can construct a `World`, so they ship verified by
reading. This pair covers three:

| site | where | what this scenario does to reach it |
|---|---|---|
| 1 | `AutoTarget.ChooseTarget` (`AutoTarget.cs:1403`) | two enemy tanks on FireAtWill, in range, no orders |
| 2 | `AutoTarget.INotifyDamage.Damaged` (`:668`) | shoot one of them and watch whether it shoots back |
| 4 | `AttackFollow` persistent-opportunity fire (`AttackFollow.cs:234`) | nothing may be promoted into `OpportunityTarget` during the hold |

The ordered half is not decoration. DEFCON 2 **permits** a shot somebody asked for, and a rule that
refused ordered fire too would be a worse defect than the one this scenario was written for. Phase 2
is what separates "held correctly" from "silenced everything".

## Why the pair discriminates

The twin directory is byte-identical except for **one quantity**:

```
World:
	DefconEscalation:
		ModeDefault: Escalation      # treatment
		ModeDefault: Skirmish        # -skirmish twin
```

`DefconEscalationState`'s constructor sets `Level = NoLevel` (0) for `Skirmish` outright, and
`DefconFireDiscipline.HoldsFire(0)` is `false`, so in the twin **nothing is holding fire** — and two
enemy tanks on FireAtWill, ten cells apart on an unfogged map, engage each other within a couple of
autotarget scan intervals (3–8 ticks each). The twin therefore fails the hold window, loudly and
early, naming which observable moved.

That is the falsification the treatment needs. `test-autotarget-preempt-air` is the recorded case
where both required arms passed and the green arm was never evidence of anything; the specific way it
went wrong was that the *unaided* behaviour already satisfied the deadline. Here the unaided
behaviour is exactly what the twin measures, and it must **not** satisfy the assertion.

Three further things make a treatment green attributable rather than merely quiet:

1. **The hold window is ~40 scan intervals wide**, not one. `AutoTargetInfo.MinimumScanTimeInterval`
   is 3 ticks and `MaximumScanTimeInterval` is 8; `HOLD_TICKS` is 300. "It did not fire" cannot be
   "it had not looked yet".
2. **The setup is controlled before it is trusted.** `Test.GetTargetOrder(Probe, Contact)` must come
   back `Attack` — it walks the same targeter chain the mouse cursor walks and issues nothing — and
   the separation must sit inside both weapon envelopes. If the two tanks were not a live targeting
   pair the scenario fails at setup instead of passing at phase 1.
3. **Four independent observables**, so one of them quietly breaking does not turn this into a test
   that measures nothing: ammunition (latched, and it counts a miss), health, the engine's own
   `Test.GetImpactEffectCount`, and `Test.GetAutomaticTargetLineCells` — which is non-empty exactly
   when a unit acquired a target *by itself*, and which goes true one step **earlier** than a shot
   does.

## The one thing the shared body refuses to assume

Both arms run `mods/ww3mod/scripts/defcon2-holdfire-contact-lib.lua`, so it cannot hardcode the level
it expects. Instead it reads the opening level, requires it to be **2 or 0** and nothing else, and
requires it to stay there. DEFCON **3** is the case worth naming: that rung is the *cease-fire*, a
different rule (`DefconFireDiscipline.PermitsWeapon`, a function of the ARMAMENT rather than of
provenance) that silences ordered fire as well. A scenario that drifted to 3 would sail through the
hold window for a reason that has nothing to do with hold-fire. It fails at setup instead.

## What counts as the answer

* **Treatment PASS** — the hold held for 300 ticks against four observables, the ordered click was
  accepted as `Attack` and landed, and the tank that was hit did not return fire for 75 ticks
  afterwards. Read the `[defcon2-contact] PASS.` census line in `lua.log` and check `defcon=2` on it.
* **Twin `OK(fail)`** — the fail reason names the observable that moved and carries `defcon=0`. That
  is the arm working.
* **Twin PASS** — red, and it falsifies the treatment rather than confirming it. Two enemy tanks in
  mutual range on FireAtWill did not engage in an ordinary Skirmish; stop and find out why before
  reading the treatment at all.
* **Treatment fails phase 2** (`the ORDERED attack never landed`) — the hold is over-broad: it is
  refusing fire a player asked for. Not the same bug, and worse.

`diff -r` the two directories before believing any result that rests on the comparison. They should
differ in `map.yaml`'s `Title`, `rules.yaml`'s `ModeDefault`, `description.txt`, and this file.
