# Restore strict fog visibility at the source, accepting that enemy buildings stop updating live

_Recorded 2026-08-26T23:08:47.597Z by 17dc66e4_

## The finding

Worker `542d1157` proved a real information leak. `FrozenUnderFog.IsVisible` (`Modifiers/FrozenUnderFog.cs:127`) ends in an unconditional `return true` at `:146`, with `IsVisibleInner` commented out beneath it. Every building carries the trait.

The rendering leak is **self-masking** — actors draw at `WorldRenderer.Draw:349`, the shroud overlay at `:368`, and `ShroudRenderer.Alpha(0)` is `1.0`, so unexplored cells are painted opaque over the leaked sprite. The minimap masks the same way. That part of the original framing was wrong and is worth remembering: *a leak that reaches the renderer is not automatically a leak the player sees.*

The **mouse path is not masked**, and that is the bug:

```
MouseTargetVisibility.IsRevealed:51
  return actorIsVisible && (isFrozenUnderFog || positionIsUnfogged || isRadarDetected);
```

`isFrozenUnderFog` is a bare `HasTraitInfo` check, true for every building. It was added at `22a1ec34` as a deliberate exemption from the cell-fog veto, and is sound *only* while `actorIsVisible` is a real answer — the exemption delegates "has this player earned sight of it" wholly to `IDefaultVisibility`. The quick fix destroyed that authority, so both operands became constants and the predicate is `true && true`. **Two individually defensible changes, jointly catastrophic.** That is the reusable shape.

Measured live (`test-unscouted-building-hidden`, `Test.KeepRenderPlayer=true`): far box at `cell=58:16 vis=0` reads `clickable=true`, with a near positive control passing — which rules out both wholesale vetoing and a nulled `RenderPlayer`.

## History

`12a9b91b` (2026-05-03) *"Quick fix: shroud off by default, force buildings visible"* — unblocking garrison playtesting, shipped paired with `ExploredMapCheckboxEnabled: true` in the same commit, which is what made it self-consistent. It is a **regression of `2d7603bf`** (2026-04-16), which had already fixed the identical hardcoded `return true` in the same method. The same defect has now been introduced twice.

## Options considered

1. **Fix at the source** — delete `:140-148`, restore `IsVisibleInner`. Chosen.
2. **Drop the `isFrozenUnderFog` exemption in `MouseTargetVisibility`** — rejected, and the worker was right to reject it unprompted. That file's PITFALL comment forbids widening the veto, and the exemption exists for the legitimate case of clicking a remembered building under fog.
3. **Leave it** — rejected. The entire `@experimental` AI programme is built on fog-legal belief; a game-wide visibility leak undermines the premise the influence stack is measured against.

## What this costs

Under the default lobby (Explored ON + Fog ON) the leak currently degrades to "live instead of remembered": enemy buildings pop in as they are constructed and update damage in real time. Restoring strict visibility takes that away — which is *correct* fog, but it is a visible change to how the user's own playtesting looks. Judged small enough to proceed without a user gate, since `ExploredMapCheckboxEnabled: true` stays and the severe never-scouted case requires unchecking Explored Map. **If the user dislikes it, it is one revert.**

Three maps pin `ExploredMapCheckboxEnabled: true` with `FogCheckboxEnabled: false` (`arena-tank-duel`, `shellmap-open-field`, `river-zeta-ww3`); with fog off `IsVisibleInner` returns `AnyExplored`, so the short-circuit is a no-op there and they are unaffected.

## Conditions attached to the fix

- **The t=0 risk becomes a permanent autotest, not a screenshot.** The May commit was chasing map-placed buildings not appearing at t=0; the worker could not reproduce it and suspects a conflation of "correctly hidden under shroud" with a bug, during a playtest that wanted everything visible. That guess must not be trusted. A second scenario asserts a map-placed enemy building is visible at t=0 under the shipped default. A capture would have proved nothing after the session ends, and the launch window is held all session.
- **`^CivBuildingHusk` is in the same work item.** The sibling worker's ruling that it needs no change is true *only* by virtue of this short-circuit, so the fix silently reverts civilian rubble to remembered-image semantics. `Ally` is not the answer there as it was for `^BuildingHusk` — civilian husks are neutral-owned.
- Adversarial review before merge. This is the most consequential engine change of the day.
