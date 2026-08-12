# Batch items 1-4 into one worker; keep the FX audit gated behind user review

_Recorded 2026-08-10T13:19:55.258Z by 6361c2be_

## Context

The user gave six gameplay items at once and explicitly left the parallel-vs-sequential decomposition to the manager.

## Options considered

1. **Six workers, one per item.** Maximum parallelism, matches the user's track framing 1:1.
2. **Three workers: items 1–4 batched, 5 alone, 6 alone.** Batched by subsystem.
3. **Sequential.** Safest for conflicts, far too slow for six items.

## Chosen: option 2

Items 1, 2, 3 and 4 all read and write the vehicle-occupant path — `VehicleCrew.cs` plus the passenger unload and damage-sharing code. Six separate worktrees would have produced four branches editing one file, and the merge would have been four-way conflict resolution on logic none of the four implementers understood in combination. Item 3 in particular *contradicts* item 2 (emergency bail must be fast; ordered dismount must be slowed), which is a design decision that has to be taken by one mind holding both requirements — split across two workers, each would have silently overwritten the other's pacing.

This follows the folder's own recorded finding (`.maestro/MAESTRO.md`, 2026-07-22 autoburn): larger per-worker batches win when the items share one subsystem, and the cap is "one clean brief" — four items with a heading each has worked before.

Items 5 and 6 touch entirely different code (weapon/sequence YAML; ammo + selection + bot tasking) and went to their own workers, in parallel, with no conflict surface.

## Chosen: FX audit is research-only, gated

The user asked explicitly to review a list before the code changes. So that worker is forbidden from touching game code and produces `WORKSPACE/fx-audit.md` as an artifact. Implementation is a separate, later dispatch conditioned on the user's approval — do not let a future session collapse these into one step.

## Consequence to watch

The batched worker owns four user-visible behaviours in one branch. Review must be per-item, not per-branch, or a defect in item 4's damage curve rides in on item 1's green. The reviewer brief must say so.
