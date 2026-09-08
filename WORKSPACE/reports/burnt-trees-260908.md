# Burnt trees: a nuke scorches the forest in place

2026-09-08, branch `wt/burnt-trees`. Built on `WORKSPACE/reports/scar-blending-260908.md`
(the husk art exists; smudges cannot be drawn over actors) and
`WORKSPACE/reports/vaporize-scope-260908.md` (removing trees is unaffordable).

**What ships:** a nuke grants a permanent `scorched` condition to every tree inside its
thermal radius. The tree swaps to frame 1 of its own SHP and takes a dark colour wash. It
is not killed, damaged, replaced or removed — cover, line of fire, pathing and the
map-load `ShadowLayer` bake are byte-identical before and after.

---

## The premise held, and the frame swap alone did not

`burnt` is frame 1 of the tree's own SHP: the exact frame each dormant `T##.Husk` actor
already reaches via its `idle: Start: 1`. Decoded from the shipped mixes, **all 22 species
have it**, in every tileset they can appear in:

| tileset | source | species | frames per SHP |
|---|---|---|---|
| TEMPERAT | `temperat.mix` | 20 (T01–T17 less T04/T09, TC01–TC05) | 10–11 |
| SNOW | `snow.mix` | same 20 | 10–11 |
| DESERT | `cnc/desert.mix` | T04, T09 | 10 |

No species lacks the art. T04/T09 are desert-only and no shipped map uses that tileset
(7 TEMPERAT, 3 SNOW, 0 DESERT, 0 INTERIOR).

**But the burnt frame is only *charred* on temperate.** Measured off the shipped art, body
pixels only, with palette indices 3/4 excluded as shadow (`rules/palettes.yaml:40-49`
declares them `ShadowIndex`, and `Palette.cs:98-99` maps them to alpha-140 black):

| | living tree | burnt frame | scar bands (ScarRim→Core) |
|---|---|---|---|
| **TEMPERAT** | lum 40–68 | **2–32** (mean −71%) | 6 / 13 / 16 / 21 |
| **SNOW** | lum 73–130 | **a flat 93–98** | 20 / 42 / 58 / 76 |

On snow the burnt frame is **brighter than every scar band it can stand on**, and on 7 of
20 species it is brighter than the *living* tree. That is the user's original complaint —
a pale tree on dark scorched ground — reproduced rather than fixed, on three of the ten
shipped maps, two of which are the nuke-themed ones (`nuclear-winter-ww3`,
`polar-disorder-ww3`, `siberian-pass-ww3`). Snow has no darker frame to reach for either:
frames 0–9 of every snow tree stay at 93–98.

So both traits ship, and each does a job the other cannot:

- **The frame swap** makes the tree a **leafless skeleton** — 34–78% of the living frame's
  opaque pixels. No tint can do that, and it is the strongest single cue.
- **The colour wash** makes it **charred**. `WithColoredOverlay` is `ReplaceColor` at a
  fixed alpha (`WithColoredOverlay.cs:50-56`), i.e. a contraction toward the tint, so one
  symmetric operation corrects only the tileset that needs it: tint `(24,18,12)` at 60%
  moves TEMPERAT **15 → 17** and SNOW **97 → 51**.

Pure black was rejected by looking at `WORKSPACE/mockups/_burnt-trees-candidates.png`: it
crushes the already-black temperate frame below its own scar while helping snow no more
than the warm char does.

## The radius is the thermal radius

Bound to each weapon's own `Warhead@Fire10 Range`, which **is** its thermal (third-degree
burn) radius, `2.0 * (kt/20)^0.41` km at 160 m/cell, on all 14 weapons.

1. Charring is a thermal effect. `Fire10` is the outermost ring of the pulse that already
   sets structures and infantry alight; a tree burning inside it is that same pulse
   reaching a third target class.
2. It **covers the ground scar at every yield** — the property that makes the reported
   defect unreachable. Margins: `+1.23` cells at 0.3 kt, `+5.41` at 10 kt, `+275` at Tsar
   Bomba. `Atomic` is exactly equal (12.00 vs 12), which is still sufficient: both sides
   measure centre-to-centre cell distance and both admit their boundary
   (`MapGrid.cs:201-210` buckets by `ceil(hypot)`, `WorldUtils.cs:83-84` admits `<= r²`).
3. **Not** the 1 psi blast radius. On Tsar Bomba blast (246.1 cells) is *smaller* than
   thermal (309.1), so binding to blast would leave a ring of untouched green forest
   inside the largest weapon's third-degree burn radius. On the other twelve, thermal is
   the smaller of the two — so this is the conservative choice everywhere it can be.

Corroboration found late: `weapons-superweapons.yaml` already ships
`Warhead@TreeFire_Singe` at `Range: 12c0` on `Atomic` — the same number. The file's own
tree chain was already anchored to the thermal radius.

## Cost

One `FindActorsInCircle` and one condition token per tree, once. This is why burning is
affordable where vaporising was not: the rejected option invalidated the `ShadowLayer`
bake, and `UpdateShadowForCells` expands every modified cell by a radius-32 annulus, so a
Tsar Bomba crater touches ~90% of the whole map-load bake serially in one tick. Nothing
here modifies a cell.

## What this does not do

It does **not** reverse the 2026-09-02 indestructibility ruling and does not need to. No
`Vaporizable` is added, `DamageMultiplier: Modifier: 0` is untouched, and the actor is
never killed or replaced. A burnt tree still blocks, still conceals, still cannot be
killed. `BurntTreeScopeTest` pins all of that.

The pre-existing `Warhead@TreeVaporize` and `Warhead@TreeFire_*` on `Atomic` /
`AtomicHighYield` are unchanged and remain inert against shipped trees for the reason
their own comments give (damage modifier 0). They grant `onfire`; this grants `scorched`.
Different conditions, no interaction.

## Incidental, not acted on

A scar warhead skips any cell holding an actor it cannot target
(`LeaveSmudgeWarhead.cs:67-68`), and trees are `TargetTypes: Trees` against a default
`ValidTargets: Ground, Water`. **Every tree cell therefore keeps unscorched ground under
it today** — visible as pale patches under the trunks in both mockups. Recorded in
`WORKSPACE/bugs/discovered.md`; out of scope here.

## Artefacts

- `WORKSPACE/mockups/burnt-trees.png` — before/after, both tilesets, 1× and 3×.
- `WORKSPACE/mockups/_burnt-trees-candidates.png` — the four treatments the choice was made from.
- `WORKSPACE/mockups/_husk-frames-audit.png` — frame 0 vs frame 1, all species, both tilesets.

All three are **Python composites, not engine screenshots** — `tools/burnt-trees/burntmock.py`,
which documents every piece of engine arithmetic it ports and every place it approximates.
