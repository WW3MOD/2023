# Proposal — one large centre decal for a blast scar (tier 2)

Written 2026-09-08 from `wt/impact-scarring`, base `main @ 30ba5681`.
**Not built.** Tier 1 (concentric smudge bands) shipped on that branch; this is
the part that was handed up instead of half-built, per the scope guard in the
brief.

## What tier 1 already fixed, and what it did not

Tier 1 replaced the three nested filled smudge discs on all 14 nuclear weapons
with five graded annuli. That gets the radial falloff and a soft rim, and the
before/after in `WORKSPACE/mockups/impact-scarring.html` is the evidence.

What it cannot fix is the **core at small radii**. A smudge is one 24×24 sprite
per cell, so the blast centre is always a cell-quantised shape. On `Atomic`
(`ScarCore` = radius 0–2) that is 13 cells, and 13 cells at 78–93% coverage
reads as a dark *plus sign*, not a disc. The bands make the outer scar circular;
they cannot make a 5-cell-wide core circular, because there is no sub-cell
geometry available to them at all.

That is the case for one large sprite drawn at the impact point.

## The render path — and why the obvious design is the wrong one

The brief sketched this as a sprite "above terrain and below actors, with a
`ZOffset` that lands in that gap". That is achievable but it is the harder of
the two options, and the reason is worth writing down.

**Verified draw order** (`WorldRenderer.Draw`, `engine/OpenRA.Game/Graphics/WorldRenderer.cs:373-384`):

1. `terrainRenderer.RenderTerrain(...)` — which is
   `TerrainRenderer.cs:108-114`: the terrain sprite layer, then **every**
   `IRenderOverlay` trait on the world actor, in trait order. All five
   `SmudgeLayer`s are in here.
2. `DrawBeyondMapFog()`
3. `preparedRenderables` — actors and effects, in one bucket, sorted by
   `Y + Z + ZOffset` (`WorldRenderer.cs:178-183`).

### Option A — an `IEffect` with a large negative `ZOffset` (the brief's sketch)

Workable, and `SpriteEffect` already takes `scale` and `zOffset`
(`Effects/SpriteEffect.cs:39-52`), so a persistent variant is ~40 lines. But the
`ZOffset` has to be chosen against the *decal's own half-height*, not against a
unit sprite. The sort key is `Y + Z + ZOffset`, so a decal at the origin with
offset `-N` draws under any unit less than `N` south of it — and **over** any
unit north of it once that unit is more than `N` away in the other direction.
`AtomicHighYield`'s scar is 47 cells in radius, so a decal covering it needs
roughly `-50000` before a unit standing at its northern edge stops being painted
over. The largest `ZOffset` anywhere in this tree today is `-8192`, on fields
(`ingame/civilian.yaml:168-175`), and that value already has a PITFALL comment
explaining that `-256` was silently wrong for exactly this reason.

So option A means picking a number an order of magnitude beyond anything the
tree has exercised, and being wrong about it is a decal drawn over tanks.

### Option B — a new `IRenderOverlay` layer (recommended)

Put the decal in pass 1 instead, alongside the smudge layers. Then it is above
terrain and below every actor **by construction** — there is no `ZOffset` to
choose, no interaction with actor sorting, and nothing to get wrong at scale.

Shape, modelled directly on `SmudgeLayer`:

```
[TraitLocation(SystemActors.World)]
class BlastDecalLayerInfo : TraitInfo   // Sequence, Palette, ZOffset-free
class BlastDecalLayer : IRenderOverlay, ITickRender, INotifyActorDisposing
{
    // list of (WPos centre, ISpriteSequence, int frame, float scale)
    void Add(WPos pos, int radiusInCells);
    void IRenderOverlay.Render(WorldRenderer wr);   // one sprite per decal
}
```

and a `LeaveBlastDecalWarhead` that calls `Add` — the same shape as
`LeaveSmudgeWarhead`, and it should reuse that warhead's terrain test verbatim
(`LeaveSmudgeWarhead.cs:59`) so a decal cannot land on water when the bands
around it correctly refuse to.

**The one thing option B costs** that option A does not: `SmudgeLayer` renders
through `TerrainSpriteLayer`, which is strictly one sprite per cell and cannot
hold a 512×512 decal. The new layer has to draw with the sprite renderer
directly, which no world-actor trait in this mod currently does. That is the
real work in this proposal, and it is why it was not attempted in the same pass
as the art.

## Art

Same generator, different size: `tools/impact-scar/gen_scars.py` already
produces palette-correct indexed SHPs per tileset and the LCW writer in
`racontent.py` has no size limit. A decal wants one 256×256 sprite per tileset
with radial falloff, quantised against the same harvested ramp — the harvesting
rule in `harvest_ramp()` transfers unchanged and is the part that took the
longest to get right.

Scale per yield with `Animation.Render(..., scale)` rather than one sprite per
weapon; `SpriteEffect` already passes a float scale through, so the plumbing is
proven even if option B does not use that class.

## Sizing it against tier 1

The decal should cover **`ScarCore` and `ScarCrater` only** (0 → 0.35 R), not
the whole scar. The bands outside that already read as a disc, and a decal wide
enough to cover them would just hide work that is already correct. Note the
consequence of the draw order above: a decal in pass 1 that is listed **after**
the smudge layers covers the bands underneath it, so the bands in the core are
wasted — either accept that, or list the decal layer first and let the core
bands texture over it, which is probably the better look.

## Open questions this proposal does not answer

- Whether a single decal sprite scaled 20× at `AtomicHighYield` still reads at
  all, or turns to mush. Nothing here was seen in-game.
- Whether the decal should fade over time. Smudges are permanent; a permanent
  decal is consistent, but it is also the largest single sprite on the map.
- Terrain lighting. `TerrainSpriteLayer` participates in `TerrainLighting`
  (`TerrainSpriteLayer.cs:66-70`); a hand-drawn overlay would not, so a decal
  may stay bright while the ground around it dims under a nuclear flash.
