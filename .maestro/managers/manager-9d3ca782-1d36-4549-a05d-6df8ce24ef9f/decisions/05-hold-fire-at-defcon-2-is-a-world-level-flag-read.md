# Hold-fire at DEFCON 2 is a world-level flag read at six sites, not a global stance force

_Recorded 2026-09-09T11:12:56.772Z by ffb08fdc_

Recon at `main @ 214dc00c` enumerated every path by which a unit fires without being told to, and evaluated four levers against the code. Choosing **candidate 3: one synced field on a World trait, read at the six sites that actually decide.**

## Options

**1. Force every actor's `AutoTarget` stance to `HoldFire`.** The strongest per-unit lever that already exists, and it cancels in-flight engagements for free because `SetStance` fans notifications out to the running activity and its children. Rejected on four counts: it is N replicated orders per toggle; it does not survive `OnOwnerChanged`, which resets all four stance axes to Info defaults and fires often because garrisoning is an ownership transfer; a unit called in from the Supply Route mid-phase arrives at `FireAtWill` and has to be caught separately; and three bot modules write stance themselves, so a blanket restore would clobber the `FiresEvGate` hold. It also cancels legitimate player-ordered attacks, and reversing it does not restore them.

**2. A condition disabling `AutoTarget`, or pausing `AttackBase`/`Armament`.** The `AutoTarget` variant misses persistent-opportunity fire entirely and, having no `TraitDisabled` override, freezes new acquisition without stopping current shooting. The `Armament` variant catches everything including force-attacks — and forbids ordered fire too, which is the exact inverse of the rule. Both need per-unit delivery through the `ExternalCondition`-plus-order seam, so they carry candidate 1's cost with worse coverage.

**3. CHOSEN — a world-level trait consulted in the fire path.** One synced field, O(1) to toggle, survives ownership changes and new spawns automatically, and disturbs no stance state so there is nothing to remember and restore. Precedent is ample: several world traits are already fetched by per-actor code, and `DoomsdayStrike` is precedent for a match rule living on the World actor with lobby options.

**4. `IDisableTrait`-style plumbing.** Does not exist. The one interface that suppresses another actor's autotargeting is victim-side, per-target, advisory, and runs after the decision to attack.

## What the choice commits us to

The read sites are not one line. The honest minimum is six: `ChooseTarget` covers the idle rescan, ambush, preemption, both move-time paths and the fresh opportunity scan in a single test; return fire needs its own because it never routes through `ChooseTarget`; the `IOverrideAutoTarget` branch of `ScanForTarget` returns an incumbent target before `ChooseTarget` is ever reached; persistent-opportunity fire has no `AttackTarget` call to intercept; `GarrisonManager` runs a second, independent scanner; and ambush-by-proxy springs allies without consulting their stance.

The flag does **not** cancel engagements already running. That has to be done separately on the transition into DEFCON 2, and the existing machinery for it is exactly the stance notification fan-out — so the transition borrows candidate 1 once, as a one-shot, rather than holding it.

## What it does not cover, and that is correct

Explosions, mines, crushing, crash weapons and vaporisation consult no stance and never will. A unit dying to a mine at DEFCON 2 is not somebody's shot, and the rule is about fire, not about damage.

## The gift found on the way

`AutoTarget.IsAutoAcquiredSource` is already the engine's own provenance test, and its docstring answers the design question outright: a bot's named-target `Attack` counts as a direct order, its `AttackMove` does not, because the player ordered a move and not that target. "Only shots somebody gave" therefore has a definition already written into this codebase, and the rule should be expressed in its terms rather than a new one.
