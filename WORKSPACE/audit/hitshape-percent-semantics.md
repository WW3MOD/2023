# HitShape "percent" semantics — what the four shapes actually compute

**Date:** 2026-08-13 · **Branch:** `wt/hitshape-rename` · **Base:** `main @ 12a0d194`
**Status:** REPORT ONLY. No shape's arithmetic was changed by the work that produced this note.

## Why this exists

`TargetDamageWarhead.cs:67` scales point damage by what used to be called
`closestActiveShape.PercentFromEdge(victim, args.ImpactPosition)`. The name says "distance in from
the edge". It is not that. Every `WPos` overload passes the impact position **relative to the shape
origin**, rotated into the shape's local frame — an offset from the CENTRE. The name caused a real
misreading: an audit concluded the warhead was all-or-nothing and published a "3300× hit-vs-near-miss
cliff" figure that was wrong, and it propagated into two reports before the user caught it from
in-game experience.

The method has been renamed to `CenterProximityPercent` (see the rename commit on this branch). This
note records the semantics that the old name hid.

**Blast radius of the whole question is one call site.** `CenterProximityPercent` is consumed only by
`TargetDamageWarhead`. Nothing else in the engine reads it.

## The four implementations

All four take an offset `v` from the shape origin in the shape's local frame. All four are reached
through a `(WPos pos, WPos origin, WRot orientation)` wrapper that does the subtraction and the
`Rotate(-orientation)`.

| Shape | `file:line` | Formula | Normalises against | At centre | At boundary | Is it a percent? |
|---|---|---|---|---|---|---|
| `Circle` | `HitShapes/Circle.cs:51-57` | `100 * (Radius − ǀvǀ) / Radius` | `Radius` — the true boundary distance in every direction | 100 | **0** | Yes |
| `Rectangle` | `HitShapes/Rectangle.cs:118-127` | `100 * (total − ǀvǀ) / total`, `total = ǀ(quadrantSize.X, quadrantSize.Y)ǀ` | the **half-diagonal** (centre→corner), regardless of direction | 100 | 0 **only at the four corners**; substantial elsewhere | Yes, but mis-normalised |
| `Capsule` | `HitShapes/Capsule.cs:87-110` | `Math.Max(0, distanceToSegment − Radius)` | nothing | **0** | 0 inside, grows unbounded outside | **No — raw world units** |
| `Polygon` | `HitShapes/Polygon.cs:105-126` | inside: `ǀv.Zǀ`; outside: `ISqrt(minEdgeDist² + z²)` | nothing | **0** | 0 inside at ground level, grows unbounded outside | **No — raw world units** |

`ǀvǀ` is `WVec.HorizontalLength`, which is `Exts.ISqrt` of X²+Y² and therefore truncates
(`WVec.cs:45`). All the arithmetic is integer, and `100 *` happens before the divide, so the
truncation is only in the final result and in the length.

### Circle — correct, and the reason the old name was defensible

For a circle, "percent in from the edge" and "percent in from the centre" are the same quantity,
because the boundary is equidistant in every direction. `Circle` is the only implementation where the
old name told the truth.

One detail worth noting: `Circle.DistanceFromEdge` uses `v.Length` (3-D) while
`CenterProximityPercent` uses `v.HorizontalLength` (2-D). The vertical component is already handled
by the `VerticalTopOffset` / `VerticalBottomOffset` clamping in the wrapper, so this is defensible,
but the two methods on the same class do disagree about dimensionality.

### Rectangle — a percent, but normalised against the wrong thing

`quadrantSize = (BottomRight − TopLeft) / 2` (`Rectangle.cs:68`) is the half-extent vector.
`new WVec(quadrantSize.X, quadrantSize.Y, 0).HorizontalLength` is therefore the **centre-to-corner**
distance — the largest distance from the centre to any boundary point. The formula divides by that
number no matter which direction the impact came from.

Consequence: a point in the middle of a face is ON the boundary and should read 0, but reads whatever
fraction of the half-diagonal it happens to sit at. The longer and thinner the shape, the worse the
distortion.

Two secondary observations in the same method:

- **It does not subtract `center`.** `DistanceFromEdge` (`:109-116`) subtracts `center` from `v`
  before measuring; `CenterProximityPercent` does not. For the symmetric `TopLeft: -a, -b` /
  `BottomRight: a, b` shapes that all ww3mod vehicles use, `center` is `(0,0)` and this is harmless.
  For an asymmetric shape it is not — e.g. `civilian.yaml:244-245` (`TopLeft: -768, -597`,
  `BottomRight: 896, 683`) has `center = (64, 43)`, so the percentage there is measured from the
  actor origin rather than from the middle of the rectangle.
- The private overload's parameter was named `fromEdge`, reinforcing the same misreading. Renamed to
  `v` to match the other three shapes.

### Capsule and Polygon — not percentages at all

Both are **verbatim copies of their own `DistanceFromEdge` with the `new WDist(...)` wrapper
stripped**. They return a raw world-unit distance, and the sense is inverted relative to
`Circle`/`Rectangle`:

- `Capsule.CenterProximityPercent` returns `Math.Max(0, distance − Radius.Length)` — the distance
  *outside* the capsule. Any point inside the shape returns **0**.
- `Polygon.CenterProximityPercent` returns `ǀv.Zǀ` when `Points.PolygonContains(p)` — so a
  ground-level interior hit returns **0** — and the distance to the nearest edge otherwise.

Fed into `TargetDamageWarhead:67`, which uses the return value directly as a percentage damage
modifier, this means **a dead-centre hit on a Capsule or Polygon actor deals 0 damage**, while a hit
that grazes the outside is handed a modifier in the hundreds or thousands.

**Current exposure: none.** No active ww3mod actor uses either shape — the `Type: Capsule` blocks in
`rules/ingame/naval.yaml` are all commented out (`:291`, `:361`, `:431`, `:502`, `:574`, `:645`,
`:708`, `:790`, `:834`), and there is no `Type: Polygon` anywhere in `mods/ww3mod/`. This is a
landmine rather than a live bug: the first actor given a Capsule or Polygon hitshape becomes immune
to every `TargetDamageWarhead` in the game, with no error and no log line.

Provenance: introduced together in `11b9d344` ("Target Damage etc WIP 2"), which reads as an
unfinished copy-paste rather than an intentional design.

## Worked numbers — the Abrams

`mods/ww3mod/rules/ingame/vehicles-america.yaml:484-488`:

```
	HitShape:
		Type: Rectangle
			VerticalTopOffset: 480
			TopLeft: -365, -790
			BottomRight: 365, 790
```

So `quadrantSize = (365, 790)`, `center = (0, 0)`, and
`total = ISqrt(365² + 790²) = ISqrt(757325) = 870`.

The hull is 730 wide (X, lateral) by 1580 long (Y, longitudinal). "Long edge" below means the
1580-long **side** face; "short edge" means the 730-wide **front/rear** face.

| Impact point | local `v` | `ǀvǀ` | Current result | A consistent (Circle-like) result |
|---|---|---|---|---|
| Dead centre | `(0, 0)` | 0 | **100** | 100 |
| Middle of the long edge (broadside, hull side) | `(365, 0)` | 365 | **58** | 0 |
| Middle of the short edge (nose-on / tail-on) | `(0, 790)` | 790 | **9** | 0 |
| Corner | `(365, 790)` | 870 | **0** | 0 |

`100 * (870 − 365) / 870 = 50500 / 870 = 58`; `100 * (870 − 790) / 870 = 8000 / 870 = 9`.

Two things fall out of that table:

1. **Nothing on the hull boundary reads 0 except the corners.** A shell landing squarely on the flank
   armour — visually a clean hit — is treated as 58% of the way in from the edge.
2. **The current math already imposes an accidental armour-facing effect.** A broadside impact on the
   boundary scores 58 while a nose-on impact on the boundary scores 9, a 6.4× swing that comes purely
   from the hull's 2.16:1 aspect ratio and has nothing to do with the `ArmorDirection` system. For a
   long vehicle the front and rear are *already* the hard facings, by accident.

For contrast, a square shape distorts less but still does not reach 0: `^2x2Shape`
(`rules/defaults.yaml:1021-1027`, `TopLeft: -1024, -1024` / `BottomRight: 1024, 1024`) has
`total = 1448`, so the middle of any face reads `100 * (1448 − 1024) / 1448 = 29`.

## Which shapes disagree, and what a consistent definition would be

Two separate disagreements, of very different severity:

- **`Rectangle` vs `Circle`** — same sense, same units, same range; they differ only in what they
  normalise against. `Circle` uses the true boundary distance, `Rectangle` uses the maximum boundary
  distance. This is a *calibration* disagreement.
- **`Capsule` / `Polygon` vs everything** — different sense (0 at the centre, not 100), different
  units (world distance, not percent), unbounded range. This is a *category* disagreement.

A consistent definition, matching what `Circle` already delivers and what the name now claims:

> Let `d(θ)` be the distance from the shape's centre to its boundary along the direction of the
> impact offset `v`. Return `100 * (d(θ) − ǀvǀ) / d(θ)`, clamped to `[0, 100]`.

Under that definition all four shapes return 100 at the centre and 0 everywhere on the boundary.
`Circle` already satisfies it exactly (`d(θ) = Radius` for all θ). `Rectangle` would need `d(θ)`
computed per-direction instead of the fixed half-diagonal. `Capsule` and `Polygon` would need to be
written rather than copied.

## What would change in play if `Rectangle` were made consistent

**Direction: strictly downward. Every off-centre impact would deal less damage than it does now.**

The proof is short. Write the current result as `100·(1 − r/D)` where `r = ǀvǀ` and `D` is the fixed
half-diagonal, and the consistent result as `100·(1 − r/d(θ))`. Since `f(d) = 1 − r/d` increases with
`d`, and `D` is by construction the *maximum* of `d(θ)` over all directions, the consistent value is
≤ the current value everywhere, with equality only at the exact centre (`r = 0`) or along the corner
diagonals (`d(θ) = D`). There is no impact point anywhere in any rectangle that would gain damage.

**Magnitude: strongly direction-dependent, and it scales with the shape's aspect ratio.** Taking the
Abrams and comparing at the same offsets:

| Impact | Current | Consistent | Change |
|---|---|---|---|
| Halfway out toward the flank (`r = 182`, broadside) | 79 | 50 | −29 pp |
| Halfway out toward the nose (`r = 395`, along the hull) | 54 | 50 | −4 pp |
| On the flank boundary (`r = 365`) | 58 | 0 | −58 pp |
| On the nose boundary (`r = 790`) | 9 | 0 | −9 pp |

Perimeter-weighting the boundary case for the Abrams (68% of the hull perimeter is long side, where
the current value averages roughly 35; 32% is short side, averaging roughly 5) puts the mean
overstatement on a boundary impact at **roughly 25 percentage points**. Interior impacts move in the
same direction by less.

A rough overall figure: expect something in the order of a **20–40% relative reduction in
`TargetDamageWarhead` output against long ground vehicles**, concentrated on flank impacts and
smallest on nose/tail impacts. Near-square shapes — most buildings — would lose less, but would still
lose (the `^2x2Shape` face case above goes 29 → 0). Circle-shaped actors would be untouched.

Because `TargetDamageWarhead` is the workhorse for direct-fire hits, this is not a localised tweak:
it is a broad, direction-biased damage nerf across essentially every weapon in the game. **That is a
balance decision for the user, not a refactor.** It is deliberately not made here.

The secondary effect is worth stating separately, because it is the part that argues *for* a fix on
realism grounds rather than against it: consistency would also **remove the accidental
aspect-ratio armour-facing effect** described above, so front/rear vs flank damage would come from
the `ArmorDirection` system alone rather than from that system plus an undocumented geometric bias.
Whether that reads as better or worse in play depends on how the current bias interacts with the
armour values that were tuned on top of it — the tuning may already be compensating for it.

## Recommendation

Three findings, in descending order of how much they should worry anyone:

1. **`Capsule` / `Polygon` return the wrong quantity entirely.** Latent, zero current exposure,
   guaranteed to bite whoever first adds a naval or polygon-hulled actor. Cheapest real fix; no
   balance consequence today precisely because nothing uses them.
2. **`Rectangle` normalises against the half-diagonal.** Live, affects every ground actor in the
   game, but "fixing" it is a global damage change. Needs a user decision, and if taken, needs a
   balance pass behind it — not a silent correctness patch.
3. **`Rectangle` ignores `center`.** Harmless for every symmetric vehicle shape; wrong for the
   handful of asymmetric building shapes. Small, self-contained, and the only one of the three whose
   fix would change almost nothing in play.
