# RED arm for test-frozen-tooltip-owner-hidden

This scenario has never been run. Run the RED first; a green whose failure arm has never
fired is not evidence.

## The sabotage (one token)

`engine/OpenRA.Mods.Common/Traits/Modifiers/FrozenUnderFog.cs`, in
`INotifyOwnerChanged.OnOwnerChanged`:

```diff
-		UpdateFrozenActor(frozen, oldOwnerIndex, refreshTooltipOwner: false);
+		UpdateFrozenActor(frozen, oldOwnerIndex, refreshTooltipOwner: true);
```

`refreshTooltipOwner: true` is exactly the pre-fix behaviour: before this branch,
`UpdateFrozenActor` had no such parameter and `RefreshState()` always wrote `TooltipOwner`.
Rebuild (`make all`) and run.

## Required RED text

The run must fail on **verdict arm 1**, and the message must open:

```
fail: USA's ghost of Box now prints tooltip owner 'Russia', but USA last observed the
building as 'USA' and has not seen the cell since it changed hands.
```

Anything else is not the RED. In particular:

- A message beginning `fail: SETUP` means the scenario never reached the state it tests.
  The sabotage cannot cause that — it changes one field that no setup control reads — so a
  SETUP failure here is a scenario bug, not a demonstrated RED. Read the printed
  `[tooltip-owner]` state line and fix the geometry or the rules before trying again.
- A failure on **arm 2** (`records snapshot owner ... where the live owner is 'Russia'`)
  would mean `FrozenActor.Owner` stopped following the capture. The sabotage does not touch
  `Owner`; if arm 2 fires, something else in the tree is wrong and this RED is invalid.
- A failure on **arm 3** (`resolves NO cursor`) likewise indicates an unrelated break.

## Why the sabotage lands on the verdict and not on a setup control

Every phase transition is gated on `Test.FrozenActorState` and, from phase 2, on
`Box.Owner.InternalName`. Neither reads `TooltipOwner`. `TooltipOwner` has exactly one
consumer in the whole engine — `WorldTooltipLogic.cs:82`, confirmed 2026-09-02 by putting
`[Obsolete("ZZCENSUS")]` on the property and reading `CS0618` across all eleven projects
(two hits: the write inside `RefreshState`, and that read). Nothing on the setup path can
therefore change value when the sabotage is applied, and the only assertion that can move
is arm 1.

The single `return true` sits inside phase 3, after all three arms, so a pass cannot be
reached without executing them.

## Restoring

Put the `false` back and rebuild. Confirm the same command now reports `pass`.
