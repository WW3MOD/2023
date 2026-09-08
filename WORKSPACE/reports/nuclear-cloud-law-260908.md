# The mushroom cloud law — one curve across the whole arsenal

`wt/cloud-law`, base `main @ 7e5364b7`. Supersedes item 3 of
[`nuclear-band-audit-260908.md`](nuclear-band-audit-260908.md), whose recommendation was wrong: it
proposed capping the arsenal at `AtomicHighYield`'s 700, which would have dragged three weapons DOWN
onto the outlier.

**The user's spec:** *"Obviously a 50mt monster bomb should be larger than a 6Mt, and that should be
larger than a 1.2Mt and so on … We just need to make them all consistent with their yield. Larger
yield means larger cloud, more light etc. And it should follow one law. If the Strategic nuke changes
that is fine, as long as they are consistent with the yield."*

## The ladder, before and after

On-screen width is `ScalePercent × 310 / (100 × 24)` cells — `nuke_large` is a 310 px sprite and a
cell is 24 px. For scale, the largest shipped map is **130 × 130 cells**.

| weapon | yield | old | new | old width | new width | |
|---|---:|---:|---:|---:|---:|---|
| B61-12 dial 1 | 0.3 kt | 56 | **122** | 7 cells | **16 cells** | |
| 9M729 | 1 kt | 90 | **158** | 12 | **20** | |
| B61-12 dial 2 | 1.5 kt | 106 | **172** | 14 | **22** | *fired by nothing* |
| B61-12 dial 3 | 10 kt | 227 | **259** | 29 | **34** | |
| 9M723 Iskander-M | 10 kt | 227 | **259** | 29 | **34** | |
| **Atomic** (Tactical) | 20 kt | 300 | **300** | 39 | **39** | **anchor — unchanged** |
| B61-12 dial 4 | 50 kt | 432 | **365** | 56 | **47** | |
| Kh-47M2 Kinzhal-N | 50 kt | 432 | **365** | 56 | **47** | |
| W76-1 Trident | 100 kt | 571 | **423** | 74 | **55** | |
| 3M14 Kalibr | 100 kt | 571 | **423** | 74 | **55** | |
| RS-28 Sarmat RV | 750 kt | 1277 | **651** | 165 | **84** | |
| B83-1 | 1.2 Mt | 1542 | **720** | 199 | **93** | |
| **AtomicHighYield** (Strategic) | 6 Mt | 700 | **1017** | 90 | **131** | **was the break** |
| Tsar Bomba | 50 Mt | 1600 ×5 | **1600 ×1** | 207 | **207** | **top — unchanged** |

Monotone breaks: **1 before, 0 after.** Visible spread: **28.6× before, 13.1× after.**

Both endpoints are unchanged. Everything between them moved onto the curve joining them.

## The law, and why neither number in it was free

    ScalePercent = round(300 × (kt/20)^0.214)

- **300 at 20 kt** is `Atomic`'s shipped value, documented at `weapons-superweapons.yaml:248` as
  "39 cells and … a defensible early cap for a 20 kt burst". The user released the *Strategic* nuke,
  not the tactical one — and this is also the anchor every other band in the arsenal is written
  against (`111 × (kt/20)^0.40`, `2.0 × (kt/20)^0.41`, `100 × (kt/20)^0.12`).
- **1600 at 50 Mt** is Tsar Bomba's shipped value — 207 cells, already 1.6× the largest map.
- Fix those two and the exponent is `ln(1600/300) / ln(2500) = 0.214`. Nothing was picked.

**Why not the old 0.40.** It was borrowed from the fireball-radius law. Across 0.3 kt → 50 Mt that is
166,667× of yield and **123× of sprite**: anchored at 300 it puts Tsar Bomba at ScalePercent 6561, a
cloud **848 cells wide — more than six times the largest map**. That is why two weapons had been
pulled off the curve by hand to stay on the map, and why one of the pulls inverted the ladder. The
cloud is not the fireball and never had to share its exponent; a real mushroom cap stops growing
spherically at the tropopause and spreads sideways, so a lower visual exponent is the expected shape
as well as the one that fits on a map. *(Offered as why 0.214 is not absurd, not as its derivation —
the derivation is the two anchors.)*

**The cost, stated plainly:** the visible spread narrows from 28.6× to 13.1×, which is the tradeoff
you flagged. Every step is still an increase — the tightest is 50 kt → 100 kt at +16% (47 → 55
cells) — but the ladder is less dramatic than it was. It is also the *most* spread any single law can
give while keeping both endpoints, so buying more means moving an endpoint: a bigger Tsar Bomba (it
can be — see below) or a smaller `Atomic`.

## 1600 is NOT a sprite-magnification ceiling — my last report was wrong about this

I asserted it from the arsenal header's comment rather than from code. **There is no ceiling.**
`scale` is a plain float multiplied into the quad at `SpriteRenderable.cs:106`, handed to
`SpriteRenderer.DrawSprite` as `scale * s.Size` (`SpriteRenderer.cs:135`, `:143`, `:157`), and
`CreateEffectWarhead` passes `(float)ScalePercent / 100` (`:169`). Nothing in that path clamps
anything. 1600 is where the numbers landed, not a wall. The header claim is corrected in place.

**And the ring it justified never bought size.** Tsar Bomba drew *five* copies of `nuke_large`,
offset ±558 and staggered 5/8/11/14 ticks. `CreateEffectWarhead` applies the offset as
`pos + Offset * ScalePercent / 100` (`:168`), so ±558 became **±8928 — 8.7 cells on a 207-cell
sprite, under 5% of its radius.** What the four extra copies bought was *brightness*: `nuke_large` is
`BlendMode: Additive` (`sequences-ingame.yaml:237`), so five near-coincident copies composited about
**five times** the intended luminance over almost the whole cloud. Collapsed to one sprite.

## The light ladder — checked as asked, and it is clean

| yield | light radius | light duration | peak intensity |
|---:|---:|---:|---:|
| 0.3 kt | 2.2 cells | 119 ticks | 7.0 |
| 20 kt | 12.0 | 199 | 7.0 |
| 1.2 Mt | 67.0 | 325 | 7.0 |
| 6 Mt | 124.0 | 394 | 7.0 |
| 50 Mt | 309.1 | 510 | 7.0 |

**No break in either radius or duration.** Radius follows the thermal law `12.5 × (kt/20)^0.41` to
within **0.4% on all twelve arsenal weapons**; the only deviations are the two reference weapons,
`Atomic` 4.0% low and `AtomicHighYield` 5.8% low — the same shape as the cloud problem but two orders
of magnitude smaller, monotone, and invisible. Duration is `199 × DurationScalePercent/100` with
`DurationScalePercent = round(100 × (kt/20)^0.12)` on all fourteen. **Peak intensity is deliberately
flat at 7.0** — the file's stated rule is that a fireball's surface is ~7000 K whatever set it off,
so yield buys area and length, not brightness.

**Nothing changed here**, per the instruction to leave intensity for the user to look at first. So
the original complaint was the cloud alone; the light was already doing what the user asked for.

## Enforcement, and what I did *not* touch

Pinned by `NuclearYieldTest.EveryNuclearCloudIsOneSpriteOnTheYieldLaw`, which derives the exponent
from the two anchor constants rather than copying it, checks all fourteen weapons against the law,
asserts the ladder is monotone, and asserts **exactly one `CreateEffect` cloud per weapon** — a
correct `ScalePercent` says nothing about how many times the sprite is drawn. A test rather than a
generator on purpose: the last nuclear band that drifted did so because a branch never re-ran
anything. **Verified non-vacuous both ways** — restoring B83's 1542 fails it, and adding a second
cloud warhead to Tsar Bomba fails it.

**`DetonationAltitude` was not touched, and the two comments saying it must move with `ScalePercent`
are corrected.** The stated rule held the lift-to-sprite proportion; what that protects is the cloud
not detaching from the ground, and the test for that is `lift − spriteHalfHeight > 0`. Measured
across all fourteen weapons the ratio spans **1.49× after against 1.41× before** — it was never
constant, it was constant for the one `Atomic`↔`AtomicHighYield` pair — and every cloud base sits
below the aim point before and after. `AtomicHighYield`'s sits **31.8 cells** under, against 17.2
before: more firmly planted, not less. Chasing the sprite with it would have pushed the burst height
from 15c0 to ~21c2, against an `AirThreshold` of 24c0 above which every warhead silently does
nothing.

## Files touched, and what lint would have said

Lint not run (worker constraint). Every key in every edited file was checked mechanically for indent
depth and parent, and separately every `ScalePercent` was checked to sit at exactly depth 2 under a
`Warhead@` at depth 1 — a wrong-depth key is silently ignored, not linted.

| file | change | what lint would catch |
|---|---|---|
| `weapons/weapons-nuclear-arsenal.yaml` | 11 `ScalePercent` values; Tsar's 5 cloud warheads → 1; header law + corrected ceiling claim | **Nothing useful.** `ScalePercent` is a valid int field at any value, and a *deleted* warhead is not a lint error at all. This is exactly the shape only a test catches, which is why one was added. |
| `weapons/weapons-superweapons.yaml` | `AtomicHighYield` 700 → 1017, plus its comment | same |
| `rules/player.yaml` | comment only | nothing |

`gen_fireball_light.py --write` was re-run in the same change and rewrote all 14 `Light:` blocks
**byte-identically** — zero changed data lines — confirming the cloud edits did not perturb it.

## What would count as the answer, if this gets a capture slot

1. **The three biggest, same camera: 1.2 Mt → 6 Mt → 50 Mt.** They should now read 93 / 131 / 207
   cells, clearly ordered. Before, the middle one was the smallest of the three.
2. **Tsar Bomba alone.** It lost four additive copies of its own sprite. Expect the same size and a
   *dimmer, less blown-out* cloud. If it now looks weak, that is the 5× luminance going away, and the
   fix is its own alpha rather than more sprites.
3. **The purchasable ladder, 0.3 → 100 kt** (16 / 20 / 34 / 47 / 55 cells). This is where the
   narrowed spread will be judged, and the 50 → 100 kt step at +16% is the weakest link.
