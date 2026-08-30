# Route the Supply Route visibility change to the user rather than ruling on it

_Recorded 2026-08-27T00:41:29.541Z by 17dc66e4_

The fog consumer sweep (`a963fd50`) found that with `FrozenUnderFog.IsVisible` giving a real answer again, the **enemy Supply Route goes fully dark until scouted, under every lobby config**. `SUPPLYROUTE` carries `FrozenUnderFog:` (`structures.yaml:240`) with the stock `Ally` default and nothing in `mods/` overrides it; it is spawned as `BaseActor: supplyroute` by `StartingUnits` through `SpawnStartingUnits.cs:89`, and `SpawnedByMapInit` is added **only** at `SpawnMapActors.cs:47`, so `startsRevealed` (`FrozenUnderFog.cs:61`) is false for it. Explored-Map-ON does not help: with fog on, `IsVisibleInner` returns the per-player frozen state, not `AnyExplored`. The contestation bar and `WithRangeCircle@Contestation` are both selection-gated, so they go with it.

I posted `FSCUOiZdCAbYpMKv-AVNO` rather than deciding.

## Why this one goes to the user when almost nothing else today did

Every other review finding this session I ruled on directly, including reversing myself once. The distinguishing test is not importance or risk — it is whether I have any basis for preferring one answer. Here I do not, and the reason is structural: **this is a question about what the game should feel like, and the SR is the mod's central objective.** Contesting an enemy SR is the main pressure mechanic; hiding it until scouted changes the shape of every match. That is taste, and the user is the designer.

It also fails the "would I take this step anyway" test in a specific way. The fog fix is a *bug* fix and I am merging it on my own authority. Hiding the enemy SR is a *balance* change that would ride along inside it, undisclosed, and be discovered mid-match rather than read in a commit message. Those are two different decisions that happen to share a diff, and letting the second one travel silently on the first is precisely the failure the `@stable` disclosure rule exists to prevent.

## Default-on-skip: keep it visible

Deliberate, and the opposite of the "natural" outcome. If the user does not engage, the merge should be a **pure bug fix** with no gameplay change riding along — an exemption preserves exactly what they have been playing with, and leaves "should the SR be fogged" available as a clean, deliberate decision later. Shipping the larger change on silence would be exactly the emergent-consequence failure this question exists to prevent.

Both directions are one line and trivially reversible, which is what makes the default cheap to be wrong about.

## Confidence I did not inflate

I put the exempt option at 64 and the go-dark option at 47, and the honest position is that I think going dark is probably the *better game* — fog working consistently, scouting the beachhead as real gameplay. What separates them is not which is better but which is safe to arrive by accident. If the user picks go-dark, that is a decision; if it happens because nobody looked, it is a bug with a good excuse.

## Not blocking the merge on it

The branch is going back to `542d1157` for the disclosure and comment items regardless, and I told it explicitly to leave `SUPPLYROUTE` alone and not pre-empt the ruling. Whichever way the answer comes back is a one-line YAML application at merge time. I am parked on this specific point, not blocked on the branch.
