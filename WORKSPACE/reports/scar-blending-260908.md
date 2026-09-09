# Blast-scar blending — recon

> **PARTLY SUPERSEDED 2026-09-09.** Everything below is still an accurate record of what the code
> did on 2026-09-08 and why, and its tree/beach/annuli findings all shipped. But its central ruling
> about buildings — that `InvalidTargets: Vehicle, Structure, Wall` is a deliberate look to be kept,
> stated here as *"The hole is deliberate YAML"* and carried into section 6's recommendation to
> touch only `ValidTargets` — **was reversed by the user on 2026-09-09**. A destroyed building must
> not mean the ground under it was undisturbed. The 61 Scar warheads now carry `IgnoreActors: true`
> instead, and the `InvalidTargets` line is gone from all of them (`wt/scar-under-actors`). Read the
> analysis below as history; do not act on its building recommendation.

Branch `wt/scar-blending`, forked from `main @ 87728dc7` (`git status -sb` clean at time of
writing). No game was launched; no shipped rendering behaviour was changed. Companion render:
[`WORKSPACE/mockups/scar-blending-options.png`](../mockups/scar-blending-options.png), generated
by `tools/scar-blend/blendmock.py`.

Every claim below is marked **VERIFIED** (read from code or measured from the shipped assets) or
**HYPOTHESIS** (not established). Where I am reporting from art rather than from code I say so.

---

## 0. Read this first: which build is the screenshot from?

**VERIFIED.** The five graded annuli landed in `0793564f`, *"impact scars: five graded annuli,
because `Size: N` was a filled disc all along"*, committed **2026-09-08 03:35:18 +0200** — the same
day as the report, and only hours before it. Before that commit every nuclear weapon fired three
`LeaveSmudge` warheads with a single-value `Size`, which is a filled disc, so the discs were nested
and the result was one flat tone from 0.35 R outward. `NukeW76` specifically went from
`Crater 3 / Scorch1 5 / Scorch2 9` (three nested discs) to five true annuli over the same outer
radius of 9.

I cannot tell from here which build produced the screenshot, and it changes what is worth doing.
Figure 0 of the render puts the two side by side for exactly this reason. **Worth asking the user
before any of section 6 is scheduled.**

That said, three of the user's four specific complaints — stopping at buildings, stopping at the
waterline, and trees standing untouched — are **unaffected by `0793564f` and are all still true
today**. Only the generic word "blocky" is ambiguous, and section 1 argues it now means something
different from what it meant yesterday.

---

## 1. Why it is blocky — the actual mechanism

### It is one opaque sprite per cell, drawn into a terrain layer

**VERIFIED.**

- Each `SmudgeLayer` trait keeps its own `Dictionary<CPos, Smudge>` (`SmudgeLayer.cs:97`), so a
  cell carries at most one smudge *per layer*, and this mod declares seven layers
  (`rules/world.yaml:459-508`).
- Rendering is a `TerrainSpriteLayer` (`SmudgeLayer.cs:131`) — one quad per cell, sprite centred
  on the cell.
- Band membership comes from `Map.FindTilesInAnnulus` (`LeaveSmudgeWarhead.cs:54`), which buckets
  an offset by the **ceiling** integer square root of its squared length
  (`MapGrid.cs:201-210`, consumed at `Map.cs:1988-2011`). So a band is a cell-quantised circle:
  every boundary in the system lands on a cell edge.

### Can a smudge carry per-cell alpha? Almost — and this is the useful finding

**VERIFIED.** Alpha exists at every level *except* the one that would make it per-cell.

- `SmudgeLayer` calls the four-argument `render.Update(cell, sequence, palette, depth)`
  (`SmudgeLayer.cs:149` for map smudges, `:221` for runtime ones).
- That overload derives alpha from `sequence.GetAlpha(frame)`
  (`TerrainSpriteLayer.cs:92-95`).
- `GetAlpha` returns `alpha?[frame] ?? 1f` (`DefaultSpriteSequence.cs:769-772`), backed by an
  `Alpha` **sequence** field (`DefaultSpriteSequence.cs:218`). It is per sequence-frame, identical
  for every cell drawing that frame.
- No smudge sequence in this mod sets `Alpha:` or `AlphaFade:` at all (grep over
  `mods/ww3mod/sequences/*.yaml` returns nothing), so **every smudge currently draws at alpha 1.0**.

But the layer underneath already takes an arbitrary per-cell alpha:
`TerrainSpriteLayer.Update(MPos uv, Sprite, PaletteReference, in float3 pos, float scale,
float alpha, bool ignoreTint)` is public (`TerrainSpriteLayer.cs:168`) and multiplies it into the
vertex tint (`:195`). The fragment shader applies that tint to **paletted** sprites too — `c *= vTint`
(`glsl/combined.frag:216-220`) runs after the palette lookup, so this is not an RGBA-only path.

**So: per-cell alpha is a SmudgeLayer change of a few lines. Not a layer change, not a renderer
change, not a shader change.** The same overload also takes an explicit screen position, so a
per-cell *offset* needs no new plumbing either.

### But the radial gradient is already smooth — measured

**VERIFIED (measured, not judged by eye).** I took the mean luminance of the rendered scar in
half-cell radial bins over accepting terrain only. For the shipped art on `NukeW76`:

| r (cells) | 0.0 | 1.0 | 2.0 | 3.0 | 4.0 | 5.0 | 6.0 | 7.0 | 8.0 | 9.0 | 10.0 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| current | 32.2 | 33.7 | 36.4 | 39.9 | 41.5 | 43.4 | 44.8 | 46.2 | 46.5 | 46.8 | 47.0 |
| + per-cell alpha | 32.2 | 33.7 | 36.4 | 39.9 | 41.5 | 43.4 | 45.2 | 46.5 | 46.9 | 46.9 | 47.0 |
| one scaled decal | 33.3 | 35.8 | 38.5 | 40.1 | 42.4 | 43.9 | 45.7 | 46.1 | 47.0 | 47.0 | 47.0 |

It is monotone with no step. There is **no ring banding left to fix**: the gradient is carried by
per-band *coverage* in the generated art (78 / 62 / 46 / 30 / 14 % at depth 0,
`tools/impact-scar/gen_scars.py:80-86`), and the outermost band at 14 % dissolves on its own.
Per-cell alpha moves the profile by under 1 luminance point; a full decal rewrite by about the same.

**Conclusion, and it is the opposite of what I expected going in: the internal tile grid and the
outer rim are no longer the two problems. Both were substantially solved by `0793564f`.** What is
still cell-shaped is every *boundary the warhead draws around something* — the holes at actors, and
the cut-off at the terrain type change. Those are sections 2 and 3, and neither is a rendering
problem.

---

## 2. Why it stops at buildings — the leading hypothesis is wrong

**The brief's hypothesis was: smudges are applied while the building is alive, so its cells are
excluded, and the building then dies leaving a hole — so the fix may be ordering. That is not what
happens.** It is not a race and not an ordering accident. It is an authored target-type list,
evaluated per cell.

**VERIFIED.**

- `LeaveSmudgeWarhead.DoImpact` walks the annulus and, per cell, does
  `var cellActors = world.BlockingActorsAt(sc); if (cellActors.Any(a => !IsValidAgainst(a, firedBy))) continue;`
  (`LeaveSmudgeWarhead.cs:67-68`).
- `Warhead.IsValidAgainst` calls `IsValidTarget(victim.GetEnabledTargetTypes())`
  (`Warhead.cs:64-79`), which is
  `ValidTargets.Overlaps(t) && !InvalidTargets.Overlaps(t)` (`Warhead.cs:57`).
- **All 51** `LeaveSmudge` warheads in `weapons-nuclear-arsenal.yaml` carry
  `InvalidTargets: Vehicle, Structure, Wall` — verified exhaustively, every one identical
  (`grep -A6 "LeaveSmudge$" … | grep Targets | sort | uniq -c` → `51`).
- A building's `Targetable` is `Ground, C4, DetonateAttack, Structure`
  (`rules/ingame/structures.yaml:28`, `^BasicBuilding`); a civilian one adds `Defense`
  (`rules/ingame/civilian.yaml:19`, `^CivBuilding`).
- `Structure` is in `InvalidTargets`, so the actor is invalid, so the cell is skipped. **The hole is
  deliberate YAML.**

Two corrections to the framing worth keeping:

1. **Trees are excluded by a different mechanism, and fixing buildings would not fix them.**
   `^Tree` is `Targetable: TargetTypes: Trees` (`rules/ingame/decoration.yaml:186`) — a single
   custom type. The scar warheads never name `Trees` in `InvalidTargets`; they do not need to,
   because the warhead default `ValidTargets` is `Ground, Water` (`Warhead.cs:30`) and
   `{Ground, Water}` does not overlap `{Trees}`. The `Overlaps` test fails, the actor is invalid,
   the cell is skipped. **Removing `Structure` from `InvalidTargets` would leave every tree hole
   exactly where it is; `Trees` must also be added to `ValidTargets`.**
   Trees do occupy their cell for this purpose: `^Tree` has `Passable: PassClasses: tree` but
   `GroundCover` defaults to `false` (`Passable.cs:33`), so `IsGroundCover` is false
   (`CellOccupancy.cs:48-53`) and the tree is returned by `BlockingActorsAt` (`:60-65`).
2. **In a forest this is the dominant artefact, not a curiosity.** The user nuked woodland. At
   forest density most cells inside the blast hold a tree, so most cells are skipped and the scar
   survives only in the gaps — a patchwork of whole-cell rectangles. Figure 0 (right) and figure 2
   of the render show this; it matches "the smudges here are blocky … in some places they stop
   abruptly" more closely than any band-quantisation effect does.

**HYPOTHESIS, secondary and untested:** `Delay` on these warheads ranges 0–26 ticks
(e.g. `weapons-nuclear-arsenal.yaml:1965-1993`), and the test runs when the warhead fires. So an
actor that dies before its delay elapses leaves an unblocked cell that *does* get a smudge, and one
that survives leaves a permanent hole. That would make hole persistence depend on what died and how
fast — plausible from the code, but I did not test it, and it is a second-order effect next to the
target-type list.

Related, for context: the non-nuclear path already excludes trees *explicitly* —
`weapons-defaults.yaml:8-10` (`^Explosion`) uses `InvalidTargets: Structure, Wall, Trees`, and
`weapons-explosions.yaml:305, :392` (`CrateNuke`, `MiniNuke`) use
`Vehicle, Structure, Wall, Trees`. The arsenal's warheads omit `Trees` from that list while still
excluding trees in practice, which reads like the arsenal authors did not realise trees were being
skipped.

---

## 3. The beach — the user's instinct is right, and nothing in the data expresses it

**VERIFIED: the engine has no sub-cell land/water information of any kind.**

- A tile carries exactly one terrain type: `TerrainTileInfo` is
  `{ byte TerrainType, byte Height, byte RampType, Color MinColor, Color MaxColor }`
  (`TerrainInfo.cs:42-47`). There is no coverage, no fraction, no mask.
- A shoreline is composed of **whole** Beach and Water cells inside a template. `Template@6`
  (`sh04.tem`, 3×3) is tiles 0-5 `Beach` over tiles 6-8 `Water`
  (`mods/ww3mod/tilesets/temperat.yaml:2246-2258`); `Template@8` and `@9` are the same shape.
  The half-land-half-water the player *sees* is baked into the 24 px tile image, and nothing reads
  it back.

**VERIFIED: a smudge cannot be placed on a beach or water cell today, and the reason is one line.**

- `LeaveSmudgeWarhead.cs:59-61` looks the cell's terrain type up in `AcceptsSmudgeType` and
  `continue`s when the requested type is not listed.
- `AcceptsSmudgeType` is a per-terrain-type `HashSet<string>` (`TerrainInfo.cs:63`).
- In `temperat.yaml`, `Clear` (`:20-25`) lists all five `Scar*` types. **`Beach` (`:60-63`) and
  `Water` (`:64-68`) list none at all** — and neither do `Rock` (`:36`), `Cliffs` (`:40`),
  `Tree` (`:44`), `Field` (`:48`), `River` (`:52`) or `RiverShallow` (`:56`).

So the scar stops at the **Clear/Beach boundary — a full cell short of the water**, not at the
waterline. That is the hard line in the screenshot. This is pre-existing and is not new with the
banding work.

Two ways forward, and they are not equivalent:

- **(a) One line per tileset.** Add the `Scar*` types to `Beach`'s `AcceptsSmudgeType`. The scar
  then covers the sand — but it still ends on a cell boundary, one cell further out. Cheap;
  does not deliver the easing-off the user asked for.
- **(b) Derive a sub-cell coverage mask from the tile art.** **VERIFIED as feasible, from the
  assets:** classifying an extracted temperate tile pixel as water by `b > r + 6` marks **99.7 %**
  of open-water pixels (`w1.tem`) and **99.8 %** of the beach templates' surf tiles, against
  **3.3–6.2 %** of grass (`clear1.tem`) and **4.9 %** of a dry beach tile. That is a clean
  separation, measured over the real art, and it means a mask can be *generated* rather than
  hand-authored. Blurred slightly it gives exactly the "easing off" the user described. This is
  what figure 4 of the render shows.

  Caveat: a mask is per-tile-image, so it wants baking into a lookup at load or as a generated
  asset, and it only makes sense in combination with a smudge that is not cell-locked (section 6).

---

## 4. Drawing over actors, and the trees

### The smudge layer draws strictly under every actor

**VERIFIED.** `SmudgeLayer` implements `IRenderOverlay` (`SmudgeLayer.cs:87, :232-235`).
`IRenderOverlay.Render` is invoked from inside the terrain pass —
`TerrainRenderer.RenderTerrain` (`TerrainRenderer.cs:108-114`) — and `WorldRenderer.Draw` calls
`terrainRenderer?.RenderTerrain(this, Viewport)` at `WorldRenderer.cs:373`, *before* it walks
`preparedRenderables` at `:383-384`. There is no ordering knob here; a smudge cannot be drawn on top
of an actor by this layer at all.

**So "draw the scorch over the tree" is not reachable by changing the smudge layer. The route is to
tint the actor.**

### The mechanism for tinting actors already exists and is already live in this mod

**VERIFIED.** `WithColoredOverlay` is a `ConditionalTrait` + `IRenderModifier` that overlays a
colour on an actor's sprites while a condition holds
(`engine/OpenRA.Mods.Common/Traits/Modifiers/WithColoredOverlay.cs:19-31`). It is in live use:
`rules/defaults.yaml:1032` and `:1037` (EMP), `rules/ingame/aircraft.yaml:379` (drone disable),
`rules/ingame/structures.yaml:259`, `rules/husks/husks-vehicles.yaml:24`. The nuclear arsenal
already grants conditions by warhead all over (`GrantExternalCondition` suppression warheads
throughout `weapons-nuclear-arsenal.yaml`), so "grant a `scorched` condition in the blast radius,
tint on it" uses two things the mod already does.

### Burnt tree art: it exists. I am reporting this from the ASSETS, not from comments

The brief warned that a curation pass corrected *false* tree-husking comments on the Atomic warhead.
I checked the files.

**VERIFIED from the sequence definitions and the decoded art:**

- `t01.husk` is `Defaults: t01` with `idle: Start: 1` and `dead: Start: 2, Length: 8`
  (`mods/ww3mod/sequences/sequences-decorations.yaml:404-414`). The husk is not separate art — it is
  **frame 1 of the tree's own shipped SHP**, and frames 2-9 are a falling animation.
- Decoded `t01.tem` and measured: frame 0 has **490** opaque pixels, mean RGB **(63, 119, 60)**;
  frame 1 has **219** opaque pixels, mean RGB **(32, 65, 29)**. Frame 1 is a smaller, much darker
  tree. It is real burnt-tree art and it ships today. The same shape holds for `t02`, `t03`,
  `tc01` and the rest (`sequences-decorations.yaml:85-96, :375-394`).
- The husk **actors** exist and are complete: `^TreeHusk` with `Tooltip: Name: Tree (Burnt)`
  (`rules/husks/husks.yaml:128-158`), and `T01.Husk` … `TC0#.Husk` at `:159` onward. Every tree
  carries `SpawnActorOnDeath: Actor: T##.Husk` (`rules/ingame/decoration.yaml:256` and siblings).

**VERIFIED: the path is switched off deliberately, and it is not a bug.** `^TreeIndestructible` sets
`DamageMultiplier@Indestructible: Modifier: 0` (`rules/ingame/decoration.yaml:135-137`); every
`T##`/`TC0#` opts in individually. A tree never loses HP, so it never dies, so `SpawnActorOnDeath`
never fires and the husk is dormant. This is an owner ruling of 2026-09-02 —
*"stop all burnt trees from appearing" / "same for living or burnt trees, for now"* — documented at
`rules/ingame/decoration.yaml:62-73`, with a one-line revert (`Modifier: 100`).

**This is the important part: showing a nuked forest as burnt does not require reversing that
ruling.** The ruling stops trees *dying and being replaced by husk actors*. Making a living,
indestructible tree *look* burnt inside a blast is a different operation — the art is frame 1 of a
sprite the tree already has.

**HYPOTHESIS (not verified — flagging rather than asserting):** the cheapest route is probably a
condition-gated body sequence swap, so a tree inside the blast draws its own frame 1 while staying
alive, indestructible and in place. I did **not** confirm that `WithSpriteBody` can be swapped by
condition in this engine version, and it should be checked before anyone plans on it. What I did
verify is the fallback: `WithColoredOverlay` will darken the existing tree sprite on a condition,
today, with no new art and no new trait. Figure 2 panel 3 of the render shows the frame-1 version
because that is the better-looking of the two, and it should be read as *the art exists*, not as
*this wiring is proven*.

---

## 5. What the render shows, and what it is worth

`WORKSPACE/mockups/scar-blending-options.png` — **Python composites, not engine screenshots**, and
labelled as such on the sheet itself. Every sprite is decoded from files this branch ships
(terrain templates, trees, the civilian building, the five scar bands, and the stock `cr*`/`sc*`
art out of `temperat.mix`); the arrangement is a hand port of the engine's arithmetic, cited line by
line in `tools/scar-blend/blendmock.py`'s docstring. What is ported exactly: annulus membership,
the filled-disc-vs-annulus reading of `Size`, the `AcceptsSmudgeType` gate, the `BlockingActorsAt`
+ target-type gate, variant selection and layer Z order. What is **approximate**: the anchoring of
tree and building sprites over their footprint cells is taken as "sprite box == footprint box",
which is right for these actors but is not a port of `RenderSprites`. The smudge itself is exact;
the decoration around it may be a pixel or two out. Nothing in the comparison turns on it.

| Figure | Shows |
|---|---|
| 0 | Pre-`0793564f` vs today, same weapon, same footprint — so the build question in section 0 can be settled by looking |
| 1 | Six rendering treatments, with each one's honest verdict in its caption |
| 2 | The holes, and what removing them looks like — the figure that matches the screenshot |
| 3 | The outer contour at 4×: the evidence that there is no staircase left to fix |
| 4 | The beach at 6×: hard cell cut-off vs a derived sub-cell mask |

---

## 6. What is worth doing, in value order

1. **Let the scar cover tree and building cells.** Add `Trees` to `ValidTargets` and drop
   `Structure` from `InvalidTargets` on the arsenal's 51 `LeaveSmudge` warheads. Pure YAML, no
   engine change, no art. This is the user's "stops abruptly" complaint and, in woodland, the
   dominant visual defect. Note the two edits are independent and both are needed (section 2).
2. **Make the trees look burnt inside the blast.** The art ships already (section 4).
   `WithColoredOverlay` on a warhead-granted condition is the verified-available route; a body
   sequence swap to frame 1 would look better and needs checking first. Does not touch the
   indestructibility ruling.
3. **The beach.** Cheap version: add the `Scar*` types to `Beach`'s `AcceptsSmudgeType`, four
   tilesets. Proper version: sub-cell coverage mask derived from tile art, which is feasible
   (99.7 % / 3-6 % separation, measured) but only pays off with a smudge that is not cell-locked.
4. **Per-cell alpha, jitter, or a scaled decal — I recommend none of them on the current evidence.**
   Measured, they move the radial profile by under one luminance point (section 1). Per-cell alpha
   is genuinely cheap and the option is worth *knowing about*; it is not worth spending on now. The
   decal only becomes interesting as the vehicle for item 3's sub-cell mask, and should be costed
   as part of that rather than as a fix for blockiness.

---

## 7. What I did not verify

- **Which build the user played.** Section 0. This is the one thing that could change the priority
  order, and I cannot settle it from here.
- **Whether `WithSpriteBody` supports a condition-gated sequence swap in this engine version.**
  Section 4, flagged as hypothesis.
- **The `Delay` interaction with actor death.** Section 2, flagged as hypothesis; plausible from the
  code, untested.
- **Anything about non-temperate tilesets.** All measurements are temperate. `snow.yaml`,
  `desert.yaml` and `interior.yaml` carry the same `AcceptsSmudgeType` lines for `Clear`/`Road`/etc.
  and the same omissions for `Beach`/`Water`, so section 3 should hold — but the water-pixel
  classifier in section 3(b) was measured on temperate art only and would need re-measuring on snow,
  where the palette is mid-grey rather than blue.
- **How any of this looks in the actual renderer.** Everything visual here is a Python composite.
  The numbers in section 1 are measurements of *my* render, not of the game; they are only as good
  as the port described in section 5. They are sufficient to say "the gradient is smooth and the
  boundaries are not", which is the conclusion they are used for; they are not a substitute for
  looking at the game.
