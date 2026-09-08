# Nuclear weapon × band audit — are the twelve arsenal nukes modelled like the two reference ones?

Measured at `main @ 3829aa77`; changes landed on `wt/nuke-harmonise`. Reference weapons are `Atomic`
(20 kt, "Tactical Nuclear Strike") and `AtomicHighYield` (6 Mt, "Strategic Nuclear Strike"), both in
`mods/ww3mod/rules/weapons/weapons-superweapons.yaml`. The other twelve, plus the `NukeSarmatMIRV`
bus, are in `mods/ww3mod/rules/weapons/weapons-nuclear-arsenal.yaml`.

**The user is right, and the cause is narrower than "modelled differently" suggests.** Eleven of the
fourteen bands are already shared and already yield-scaled. Two are not, and both of them are
*feel* bands rather than damage bands — which is exactly why the difference reads as "the effect is
wrong" rather than "the balance is wrong".

## Deliverable 1 — the band matrix

`•` = present, `—` = absent. Yields ascending. `*` marks the two reference weapons.

| weapon | kt | Flash | Shake | FireballLight | Fireball | Vaporize | ThermalRad | HeatRad2 | Fire×10 | Tree×4 | EMP | BlastWave | Suppr×5 | Scar |
|---|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| NukeB61Mod12Y003 | 0.3 | • | • | • | • | • | • | — | • | — | • | • | • | 2 |
| NukeRu9M729 | 1 | • | • | • | • | • | • | — | • | — | • | • | • | 3 |
| NukeB61Mod12Y015 | 1.5 | • | • | • | • | • | • | — | • | — | • | • | • | 3 |
| NukeB61Mod12Y10 | 10 | • | • | • | • | • | • | — | • | — | • | • | • | 4 |
| NukeRuIskander | 10 | • | • | • | • | • | • | — | • | — | • | • | • | 4 |
| **Atomic \*** | 20 | • | • | • | • | • | • | • | • | • | • | • | • | 5 |
| NukeB61Mod12Y50 | 50 | • | • | • | • | • | • | — | • | — | • | • | • | 5 |
| NukeRuKinzhalN | 50 | • | • | • | • | • | • | — | • | — | • | • | • | 5 |
| NukeW76 | 100 | • | • | • | • | • | • | — | • | — | • | • | • | 5 |
| NukeRuKalibr | 100 | • | • | • | • | • | • | — | • | — | • | • | • | 5 |
| NukeSarmatRV | 750 | • | • | • | • | • | • | — | • | — | • | • | • | 5 |
| NukeB83 | 1200 | • | • | • | • | • | • | — | • | — | • | • | • | 5 |
| **AtomicHighYield \*** | 6000 | • | • | • | • | • | • | • | • | • | • | • | • | 5 |
| NukeTsarBomba | 50000 | • | • | • | • | • | • | — | • | — | • | • | • | 5 |
| NukeSarmatMIRV | 5×750 | • | • | — | — | — | — | — | — | — | — | — | — | — |

`NukeSarmatMIRV` is a `FireCluster` bus: two `Warhead@Bus*` blocks that fire five `NukeSarmatRV`
rounds. It carries flash and shake of its own so the salvo is not silent, and nothing else. It is
counted separately everywhere below.

## What actually differs, band by band

### 1. Screen shake — THE defect. Twelve weapons on a scale that stopped existing on 2026-09-06

`f945a645` ("Rebuild screen shake as a configurable seismic model, **and retune every call site**")
changed `Intensity` from the numerator of an inverse-square falloff into **peak screen displacement
in pixels** (`ShakeScreenWarhead.cs:23-29`). It touched **zero lines** of
`weapons-nuclear-arsenal.yaml` — that file was created the same day in `cae070e6` and merged after
it, so it kept the pre-rework numbers, and `f917d721` copied them into four more Russian warheads the
next day. Neither commit is wrong on its own; they raced.

`ScreenShaker` soft-clamps the summed displacement at `MaxAmplitude = 22` px through `tanh`
(`ScreenShaker.cs:53`, `:412-421`). So:

| | before | after clamp | after this change | after clamp |
|---|---:|---:|---:|---:|
| B61-12 at 0.3 kt | 35 px | 20.2 px | 9 px | 8.6 px |
| B61-12 at 50 kt | 76 px | **22.0 px** | 14 px | 12.9 px |
| Tsar Bomba, 50 Mt | 150 px | **22.0 px** | 23 px | 17.1 px |
| Sarmat MIRV bus | 130 px | **22.0 px** | 19 px | 15.4 px |
| `Atomic` \* 20 kt | 13 px | 12.1 px | *unchanged* | 12.1 px |
| `AtomicHighYield` \* 6 Mt | 20 px | 15.9 px | *unchanged* | 15.9 px |

**The 0.3 kt tactical nuke was shaking the camera harder than the 6 Mt strategic nuke**, and
everything from 50 kt upward was pinned at the clamp, so eight weapons were indistinguishable from
each other and all of them were harder than the weapon the user likes. That is the "modeled
differently … even similarly sized ones seem different" report.

Structure, not just amplitude: the reference stacks are an *envelope* — `AttackTicks` (ramp),
`DecayHalfLife` (seismic coda), `FrequencyScale` (deeper roll vs sharper rattle) — plus a final
stage carrying `PropagationTicksPerCell`, so the air blast **arrives with the visible shockwave** at
every distance instead of landing with the flash. All twelve had none of those fields: bare
`Duration`/`Intensity`/`Delay`, instant onset, flat amplitude, stopping dead on the expiry tick.

### 2. Screen flash — a yield ramp that dims rather than shortens

`FlashPaletteEffect` lerps from `remainingFrames / Info.Length` toward the palette
(`FlashPaletteEffect.cs:68`), and `palettes.yaml` pins the `Nuke` effect's `Length` at 30. `Duration`
is therefore **peak whiteness as well as length**. The arsenal ramped it with yield — 8, 8, 8, 10,
10, 20, 20 below 100 kt — so the 0.3 kt weapon's screen flash *peaked at 27% white*. Both references
use 30.

It was also non-monotone against the references: `Atomic` at **20 kt** used 30 while
`NukeB61Mod12Y50` at **50 kt** used 20 — a bigger weapon with a dimmer flash.

This is the same failure the fireball light was rewritten for on 2026-09-07 (the arsenal header
records it: "The user reported it as the small nukes not feeling like nukes"), surviving in a second
band that the rewrite did not cover.

### 3. Mushroom cloud size — RESOLVED 2026-09-08, see nuclear-cloud-law-260908.md

**This section is superseded.** It recommended capping the arsenal at `AtomicHighYield`'s 700, which
was wrong: it would have dragged three weapons DOWN onto the outlier. The user chose to refit one
law through the whole ladder instead, and released `AtomicHighYield` to move. See
[`nuclear-cloud-law-260908.md`](nuclear-cloud-law-260908.md). The analysis below is kept because its measurements stand.

### 3a. (superseded) Mushroom cloud size — the two references disagree with each other

`Warhead@Fireball ScalePercent` at 300 draws 39 cells (`weapons-superweapons.yaml:145`), so
cells ≈ ScalePercent × 0.13. The arsenal follows `300 × (kt/20)^0.40` — the fireball-radius law —
capped at 1600. `AtomicHighYield` does **not**: it carries 700 against a law value of 2937, with a
stated reason ("~90 cells, which is 14 km and a fair early cap for a megaton-class burst",
`weapons-superweapons.yaml:181-182`).

| weapon | kt | cloud cells | blast cells | cloud ÷ blast |
|---|---:|---:|---:|---:|
| NukeB61Mod12Y003 | 0.3 | 7 | 4.5 | 1.6× |
| **Atomic \*** | 20 | 39 | 15.0 | **2.6×** |
| NukeW76 | 100 | 74 | 31.0 | 2.4× |
| NukeSarmatRV | 750 | 166 | 60.8 | 2.7× |
| NukeB83 | 1200 | 200 | 70.9 | 2.8× |
| **AtomicHighYield \*** | 6000 | 91 | 102.0 | **0.9×** |
| NukeTsarBomba | 50000 | 208 | 246.1 | 0.85× |

So the 1.2 Mt B83 throws a cloud **more than twice as wide** as the 6 Mt weapon beside it. This is
very likely the other half of "even similarly sized ones seem different".

**I did not change it, and the reason is that harmonising here means choosing which reference wins.**
The arsenal is exactly consistent with `Atomic`'s ratio; `AtomicHighYield` is the outlier, and it is
the one the user says looks good. Options, for the user:

- **A — cap the arsenal at `AtomicHighYield`'s 700.** Touches three weapons (SarmatRV, B83, Tsar);
  B83's cloud goes 200 → 91 cells. Makes the top of the ladder consistent with the liked weapon and
  leaves everything ≤ 100 kt untouched. Cheapest, and my recommendation.
- **B — put `AtomicHighYield` back on the law** (700 → 1600, the sprite-magnification ceiling).
  Changes the one effect the user praised. Not recommended without a look first.
- **C — refit the law through both references**, `300 × (kt/20)^0.15`. Internally consistent, but
  moves every weapon including `Atomic`, and shrinks the whole ladder.

### 4. Shockwave ring cosmetics — left at engine defaults on the small weapons. NOT CHANGED

`ShockwaveThickness` is `1c512` on everything at or below 50 kt — which is the engine default
(`ShockwaveDamageWarhead.cs:90`), written out explicitly rather than scaled. As a fraction of each
weapon's own `MaxRadius` that is 5.5% on every weapon from 100 kt up (and on `AtomicHighYield`), but
**33% at 0.3 kt** and 10% at 10 kt. The smallest nuke's expanding ring is a third as thick as it is
wide. Same story for `ShockwaveSegments` (64, the default) and `ShockwaveFadeInTicks` (a flat 4 below
750 kt against ~0.22 × MaxRadius above it).

Left alone because I could not derive the intended law for `ShockwaveFadeInTicks` from two points
that disagree (9/60.8 = 0.148 at SarmatRV against 22/102 = 0.216 at `AtomicHighYield`), and because
`ShockwaveFadeInTicks` was deliberately set to the length of the fast phase in a change dated
2026-09-07 (`WORKSPACE/DISCOVERIES.md`). Worth one pass with that note in hand.

`Atomic` omits all three fields entirely and takes the engine defaults, including
`ShockwaveFadeInTicks: 25` where every arsenal weapon is explicit. That is a gap in the *reference*,
not in the arsenal.

## What is NOT different — five claims checked and knocked down

These were on the list of suspicions and are wrong. Saying so is part of the answer.

1. **"`Warhead@ThermalVaporize` is missing from all twelve."** It is not missing, it is *renamed*.
   The twelve carry `Warhead@Vaporize`: same `SpreadDamage` trait, same `Falloff: 100, 100, 100, 50`,
   same `DamageTypes: ElectricityDeath`, same `InvalidTargets: Trees`. A warhead key's suffix after
   `@` is a label with no engine meaning. **Zero runtime difference.**

2. **"Suppression is banded semantically on the references and numerically on the arsenal."** True and
   entirely cosmetic — `Warhead@Suppression1..5` against `Warhead@SuppressionClose/Medium/Far/Wind/Outer`,
   with the same five-band `Amount: 10, 8, 5, 3, 1` structure and the same decreasing durations on
   both. Renaming them would be churn.

3. **"The twelve are missing the tree warheads."** They omit `TreeVaporize` and `TreeFire_Kill/Burn/Singe`
   **correctly**, and the arsenal header says so. Trees carry `DamageMultiplier: Modifier: 0`
   (`decoration.yaml:135-137`), and the burn overlays never reach their `MinimumDamageState` because
   the tree never leaves `DamageState.Undamaged` (`decoration.yaml:150-155`). **All four tree
   warheads on the two references are inert.** The arsenal is right and the references carry four
   dead blocks each.

4. **"`Warhead@Shake` stage counts vary 3–5 with no relation to yield."** They vary with yield, and
   monotonically: 3 at 0.3 kt, 4 through 100 kt, 5 from 750 kt up — which brackets both references
   (`Atomic` 4, `AtomicHighYield` 5). The threshold was already a deliberate decision and is kept.
   Only the 0.3 kt weapon's missing fourth stage was a real gap, and it was the *air blast* stage.

5. **"Scar band counts of 2/3/4 on the small weapons are dropped bands."** They are deliberate. Scars
   were harmonised across all fourteen weapons **today** (`0793564f`) onto band fractions
   0.15/0.35/0.58/0.80/1.00 R, and bands must tile exactly — at small radii two fractions land on the
   same cell ring, so one is dropped. `NoSmudgeRadiusCrossesTheTileSearchCeiling` asserts exactly
   that. **Untouched.**

Also already harmonised and left alone: `Warhead@FireballLight` (generated for all fourteen by
`tools/nuke-light/gen_fireball_light.py`, two-stage envelope, `Radii` on the thermal Y^0.41 law);
`DurationScalePercent` (exactly `round(100 × (kt/20)^0.12)` on all fourteen);
`Warhead@BlastWave` damage and falloff shape (600 → 6 on every weapon, resampled to whatever array
length makes `(len-1) × Spread == MaxRadius`, so the PITFALL is respected everywhere).

## The damage bands are flat, and mostly harmlessly so

`Warhead@Vaporize` ships `Damage: 800000 / Penetration: 20000` on all twelve — `AtomicHighYield`'s
numbers, not scaled. `Atomic` uses 200000/5000. Left alone: 800000 one-shots every actor in the mod,
so within each weapon's own (correctly scaled) `Spread` the difference is invisible.

`Warhead@ThermalRadiation` is the one worth a second look: all twelve carry `Damage: 9000`,
`Penetration: 1200`, `DamageInterval: 1`, against `Atomic`'s `3000 / 300 / 2`. That is 3× the damage
at 2× the tick rate — **6× the thermal DPS on a 0.3 kt weapon than on the 20 kt reference**, partly
offset by a much shorter `RadiationDuration` (4 ticks against 24). Flagged rather than changed: this
is a balance number, the brief is about the effect, and moving it wants a `test-balance-*` run.

## Files touched, and what lint would have said

Lint was not run (worker constraint). Every key in every edited file was checked mechanically for
indent depth and parent — a key at the wrong depth is *silently ignored*, not linted.

| file | change | what lint would catch if I got it wrong |
|---|---|---|
| `weapons/weapons-nuclear-arsenal.yaml` | 13 shake stacks regenerated, 7 flash durations raised | unknown field on `ShakeScreen` → `--check-yaml` error. **A field at depth 3 instead of 2 → silently ignored, no error.** All 8 emitted field names were checked against `ShakeScreenWarhead.cs` and `Warhead.cs:48`; all 3455 keys pass a depth audit. |
| `rules/powers.yaml` | 2 `Name:` values, header table, naming-rule paragraph | nothing — `Tooltip: Name:` is a free string |
| `rules/player.yaml` | 2 `Name:` values | nothing — same |

`weapons-superweapons.yaml` was **not** modified; it is listed above only because the depth audit ran
over it too as a control.

## What would count as the answer, if this gets a capture slot

The manager launches. Two fires, same map, camera parked at a fixed offset from the aim point:

1. **`NukeB61Mod12Y003` (0.3 kt) and `NukeB83` (1.2 Mt), same camera.** Before this change both
   pinned the shake clamp and were indistinguishable. The answer is: the 0.3 kt is a short sharp
   knock and the 1.2 Mt is a long low roll that keeps going after the flash has gone.
2. **`NukeB83` (1.2 Mt) against `AtomicHighYield` (6 Mt), same camera.** This is the user's
   "similarly sized ones seem different" pair. Shake and flash should now read as the same weapon
   family. **The clouds will still differ by 2.2×** — that is item 3 above, deliberately not changed,
   and this capture is what should decide it.
