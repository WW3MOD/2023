# The nuclear light: sized against its own cloud, not against its yield

`wt/light-ratio`, base `main @ 4004723d`. Companion to
[`nuclear-cloud-law-260908.md`](nuclear-cloud-law-260908.md) — same ladder, the other half of the
draw. Visual: [`../mockups/nuke-light-ratio.png`](../mockups/nuke-light-ratio.png).

**The user's spec:** *"the Light for the 6Mt warhead is really nice. I would like that same light for
all nukes, just that the size and length/duration of the flash is smaller/shorter for the smaller
weapons, but around the nuke it should still have that same intense glow. Currently the small nukes
gives almost no light at all it looks like to my eyes, and I want them to just as bright at the core,
but smaller of course, and the light spreads out less."*

## The diagnosis, confirmed by measurement

**"Almost no light" was never a brightness problem, and nothing could have been raised to fix it.**
Peak intensity was already flat at **7.0 on all fourteen weapons** — the core was as bright as the
6 Mt one on every single nuke. What differed was where the light *ended* relative to the sprite
sitting on top of it.

The light radius grew as `Y^0.41` (the third-degree burn law) while the mushroom cloud that covers it
grows as `Y^0.214`. The exponents differ by nearly 2×, so the ratio between them swung **10.7× across
the ladder** — and every weapon at or below 100 kt had a light radius *smaller than its own cloud*.
The entire lit area sat under the sprite. Full brightness, nowhere to see it.

The two weapons the user singled out as good are the two with the largest ratios.

## The ladder, before and after

Light radius, in cells. Cloud radius is `ScalePercent × 310 / (100 × 24) / 2`.

| weapon | yield | cloud R | old light R | **new light R** | old ratio | **new ratio** | peak |
|---|---:|---:|---:|---:|---:|---:|---:|
| B61-12 dial 1 | 0.3 kt | 7.9 | 2.2 | **14.9** | 0.28× | **1.89×** | 7.0 |
| 9M729 | 1 kt | 10.2 | 3.7 | **19.3** | 0.36× | **1.89×** | 7.0 |
| B61-12 dial 2 | 1.5 kt | 11.1 | 4.3 | **21.0** | 0.39× | **1.89×** | 7.0 |
| B61-12 dial 3 | 10 kt | 16.7 | 9.4 | **31.6** | 0.56× | **1.89×** | 7.0 |
| 9M723 Iskander-M | 10 kt | 16.7 | 9.4 | **31.6** | 0.56× | **1.89×** | 7.0 |
| **Atomic** (Tactical) | 20 kt | 19.4 | 12.0 | **36.6** | 0.62× | **1.89×** | 7.0 |
| B61-12 dial 4 | 50 kt | 23.6 | 18.2 | **44.5** | 0.77× | **1.89×** | 7.0 |
| Kh-47M2 Kinzhal-N | 50 kt | 23.6 | 18.2 | **44.5** | 0.77× | **1.89×** | 7.0 |
| W76-1 Trident | 100 kt | 27.3 | 24.2 | **51.6** | 0.89× | **1.89×** | 7.0 |
| 3M14 Kalibr | 100 kt | 27.3 | 24.2 | **51.6** | 0.89× | **1.89×** | 7.0 |
| RS-28 Sarmat RV | 750 kt | 42.0 | 55.2 | **79.4** | 1.31× | **1.89×** | 7.0 |
| B83-1 | 1.2 Mt | 46.5 | 67.0 | **87.8** | 1.44× | **1.89×** | 7.0 |
| **AtomicHighYield** (Strategic) | 6 Mt | 65.7 | 124.0 | **124.0** | 1.89× | **1.89×** | 7.0 |
| Tsar Bomba | 50 Mt | 103.3 | 309.1 | **195.1** | 2.99× | **1.89×** | 7.0 |

**`AtomicHighYield` comes out byte-identical** — it is the anchor, and the generator reads the ratio
off it rather than being told what it is, so it is a fixed point by construction (asserted, not
assumed). **Tsar Bomba is the one weapon whose light shrinks**, 309 → 195 cells: it was the only
weapon above the anchor's ratio. It is still by far the largest light in the mod, at 1.5× the width
of the largest shipped map.

Duration was already monotone 119 → 510 ticks and is **untouched**, so small weapons still flash
shorter — which the user asked for and is already happy with.

## Why a constant ratio is the right shape, not just a convenient one

`TerrainLighting`'s InverseSquare falloff is `w(f) = (1/(1 + 24(1−f)²) − 1/25) × 25/24` with
`f = 1 − r/R` (`TerrainLighting.cs:48-50`, `:255-257`). Hold `R / cloudRadius` constant and **every
weapon has the same fraction of peak brightness at its own cloud's edge — 9.3% of 7.0.** That is
exactly "around the nuke it should still have that same intense glow", at every yield, as arithmetic
rather than as tuning. It is also why the falloff constant K was left alone: `AtomicHighYield`'s look
*is* K=24 at ratio 1.888, so reproducing the ratio everywhere is what reproduces the look everywhere.

## The tradeoff, stated rather than buried

**This abandons the physical grounding of the light radius.** `12.5 × (kt/20)^0.41` is the
third-degree burn radius, and the shipped envelopes sat within 0.4% of it. The new law is "however
big the sprite is". That is a real loss and it should be a look-first decision, not a settled one.

Two things make it defensible rather than arbitrary:

1. **Physics already lost this same argument on the same ladder, the same day.** The cloud's own
   exponent is the fireball's 0.40 and it ships at 0.214, because 0.40 produced an 848-cell sprite on
   a 130-cell map. The precedent is real.
2. **The burn radius itself is untouched.** `Warhead@ThermalRadiation`'s `Spread` still follows
   `Y^0.41` and `NuclearYieldTest` still asserts it (`:265-267`). What moved is a *rendering* radius;
   what burns you is unchanged. The two were never the same field — they only agreed by construction.

## Nothing but the renderer reads this — checked before changing it

`LightEventDefinition.Radii` reaches exactly two places: `TerrainLighting.AddLightSource` and
`FogPiercingLightRenderable` (`LightEventManager.cs:148`, `:290`). **No damage, no vision, no shroud
reveal**, and no clamp but a 1-unit floor to stop `SpatiallyPartitioned` rejecting a zero-width
rectangle (`:202-205`). The thermal and vaporize warheads carry their own independent `Spread`. This
is a purely visual retune and it moves nothing else.

## Terrain-refresh cost, since the small lights grew up to 6.8×

The generator's own throttle bumped nine weapons a tier (2→3, 3→5), which partly offsets the larger
area. Load, as cells refreshed per tick (`πR² / interval`):

| | before | after |
|---|---:|---:|
| 0.3 kt | 8 | 232 |
| 20 kt | 226 | 1 403 |
| 6 Mt | 9 661 | 9 661 *(unchanged)* |
| 50 Mt | 18 760 | **7 474** |

The worst new number is 1 403, which is **15% of what the shipped 6 Mt already costs**, and Tsar
Bomba's load halves. No new ceiling is approached.

## Enforcement

- `NuclearYieldTest.EveryNuclearLightReachesTheSameMultipleOfItsOwnCloud` — new. Reads the anchor's
  ratio from `AtomicHighYield`, asserts all fourteen match it within 0.02, and asserts peak intensity
  is 7.0 on every one. **Verified non-vacuous**: restoring the 0.3 kt weapon's old 2.2-cell reach
  fails it with the user's own complaint as the message.
- `EveryNuclearFireballIsATwoStageFlash…` assertion 7 **rewrote**, not loosened. It pinned
  `ThermalCells(kt)` — the law being abandoned. It now asserts the weaker but load-bearing property
  that a light reaches past its own cloud, so the thing the user cares about fails inside the
  envelope test too, where someone editing envelopes is looking. Also fails on the injected revert.
- `YieldScalesTheFireballsArea…` radius-ratio assertion **rewrote** from `Y^0.41` to the cloud ratio.
- The generator asserts the anchor is a fixed point of itself and exits non-zero otherwise.

## Files touched, and what lint would have said

Lint not run (worker constraint). Every key in both edited YAML files was checked mechanically for
indent depth and parent, and separately every `Radii` / `Times` / `Intensities` / `Tints` /
`TerrainRefreshInterval` was checked to sit at exactly depth 3 under `Light:` under
`Warhead@FireballLight`. A wrong-depth key here is **silently ignored** — and `LightEventDefinition`
would then load a weapon with no `Radii` at all.

| file | change | what lint would catch |
|---|---|---|
| `weapons/weapons-nuclear-arsenal.yaml` | 13 `Radii` arrays + 9 `TerrainRefreshInterval`, all generated | `LightEventDefinition.Validate` throws a `YamlException` on a `Radii` count that is neither 1 nor `Times.Length`, and on any entry ≤ 0 (`:222-229`) — so a malformed array **would** be caught. A *wrong but well-formed* array would not be, which is what the test is for. |
| `weapons/weapons-superweapons.yaml` | `Atomic`'s `Radii` + refresh (generated). `AtomicHighYield` byte-identical | same |
| `tools/nuke-light/gen_fireball_light.py` | reads `ScalePercent`; radius law; anchor fixed-point check | n/a |
| `engine/…/NuclearYieldTest.cs` | 1 test added, 2 assertions rewritten | n/a |
| `WORKSPACE/mockups/nuke-light-ratio.{py,png}` | new | n/a |

## What would count as the answer, if this gets a capture slot

1. **A 0.3 kt strike, camera close.** Before: a cloud with no glow around it. After: lit ground
   reaching 7 cells past the cloud edge, at the same core brightness as the 6 Mt one. This is the
   whole complaint.
2. **0.3 kt and 6 Mt back to back.** They should now look like the same effect at two sizes — which
   is the literal ask — with the small one shorter as well as smaller.
3. **Tsar Bomba.** The only weapon whose light *shrank*. If its flash now reads as too small for the
   cloud, the anchor is the thing to revisit, not this weapon.
