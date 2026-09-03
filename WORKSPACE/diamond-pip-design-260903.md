# Graded visibility diamond — decisions and where each one reverses

Built on `wt/diamond-pip`, based on `main @ 925b5b82`. Build clean, NUnit 2348/2348.
**Not launched, not screenshotted, not YAML-linted** — those were withheld from this worker.

Replaces the binary red `!` (`WithSpottedDecoration`) with a diamond whose fill and colour say how
visible a unit currently is. The design calls were delegated; this is the record of which ones were
made, and the single line that undoes each.

---

## The one that matters: what is being graded

The grade reads the unit's **own posture**, not enemy observation. `Detectable.CurrentVisibility`
composes cover (`object-proximity` 1-3), prone, dug-in, firing, moving and rank
(`mods/ww3mod/rules/ingame/infantry.yaml:758-787`, `mods/ww3mod/rules/defaults.yaml:278-289`), and it
uses no information about where enemies are.

That is what keeps the **anti-wallhack asymmetry rule** intact rather than working around it. A
readout driven by who can currently see us would announce "someone you cannot see is watching you".
Own-posture is knowledge the soldier plainly has — he knows he is prone in a treeline, or standing in
a road firing. The single enemy-derived input is the top band, `Spotted`, which is the *existing*
predicate, unchanged, asymmetry gate and truth gate and all
(`WithSpottedDecoration.IsSpotted`, unmodified except for being moved behind a cache helper).

Because the top band is the only enemy-derived one, the anti-wallhack property is now a **property of
a pure function** and is asserted directly: `AnUnspottedObserverCannotRaiseTheGrade` walks every
concealment level with `spotted:false` and requires the result to stay at or below `High`.

## The ladder

Exposure is the inverse of concealment: `Exposure = 1 + 9 - CurrentVisibility`, so it climbs as the
unit becomes more visible. Bands, at the shipped ceilings 3 / 5 / 7:

| Grade | Exposure | Glyph | Colour | A standard rifleman gets here by |
|---|---|---|---|---|
| Concealed | 1-3 | `◊` hollow | `6E9E76` green | prone + dug in + heavy cover |
| Low | 4-5 | `◊` hollow | `ECC73C` yellow | prone in cover |
| Moderate | 6-7 | `♦` solid | `F0B232` amber | upright, stationary, not shooting |
| High | 8-9 | `♦` solid | `F09425` orange | moving, or firing, or both |
| Spotted | any | `♦` solid | `FF4A3C` red | an enemy we can see has eyes on him |

Worked from `^Infantry`'s `Detectable: Vision: 3` (`infantry.yaml:96-97`). The **hollow→solid step
falls exactly on "got into cover and stopped"**, which is the transition the player is actually
acting on. Snipers (`Vision: 5`, `:1673`) read one band calmer at every posture, which is correct.

`FF4A3C` for Spotted is deliberately the *same red the `!` used*, so the one state players already
recognise keeps its exact colour across the change.

---

## Calls made, and the line that reverses each

| # | Call | Reverse it by |
|---|---|---|
| 1 | **Graded, not binary** | `Graded: false` in `^UnitIndicators`. Restores the old `!` exactly — `Text:` and `Color:` are still the old glyph and colour and are untouched by the graded path. One line, no other edit anywhere. |
| 2 | **Drawn on every unit, always** — not only while spotted | `MinimumDrawnGrade: Spotted` restores today's density with the new glyph. `Moderate` draws only units that are becoming visible. **This is the call most likely to be wrong; see the caveat below.** |
| 3 | **`◊` / `♦`, not `◆` / `◇`** | `HollowText` / `SolidText`. Do not "fix" these to U+25C6/U+25C7 — see the font trap below. |
| 4 | **Fill carries the coarse signal, colour the fine one** | `SolidFromGrade`. Set it to `Concealed` for always-solid, `Spotted` for always-hollow-until-seen. |
| 5 | **Band boundaries 3 / 5 / 7** | `ConcealedExposureCeiling` / `LowExposureCeiling` / `ModerateExposureCeiling`. |
| 6 | **Five colours** | `ConcealedColor` … `SpottedColor`. |
| 7 | **Trait keeps its name `WithSpottedDecoration`** | Nothing to reverse. Renaming would have touched five autotest scenarios that carry `-WithSpottedDecoration:` (`test-detect-no-invisibility`, `test-unit-indicators-before` ×3, `test-visual-gauge-truth`) — they keep working untouched, and `Graded: false` is a cheaper off-switch than a rename would have left behind. |
| 8 | **Slot and margin unchanged** (`Position: Top`, `Margin: -8,0`) | The diamond is wider than `!` — roughly 6px against 2px at font size 10 — so it occupies x ∈ [5,11] where the damage pip holds [-3,3]. Still clear, but the gap narrows from 4px to 2px. If it reads as crowded, `Margin: -9,0` or `-10,0`. |

Every one of 1-6 is in `mods/ww3mod/rules/defaults.yaml` at its C# default value, **repeated in YAML
on purpose**: this pip has never been seen on screen, a tuning round is expected, and tuning in YAML
costs no rebuild.

---

## Caveat on call 2 — vehicles get almost nothing from this

`^UnitIndicators` is inherited by `^SelectableCombatUnit`, `^SelectableSupportUnit` and
`^SelectableEconomicUnit`, so **the diamond lands on vehicles and aircraft too, not just soldiers**.
The user asked for soldiers.

For vehicles the ladder is nearly flat. `^Vehicle` takes `Detectable`'s default `Vision: 2` and has
only two modifiers — stationary `+1`, firing `-1` (`vehicles.yaml:71-96`) — so a vehicle is
**Moderate when stopped and High otherwise, and never anything else**. It is not wrong, and "stop
moving to be harder to see" is the same lesson, but it is two states where infantry has four.

I shipped it globally anyway rather than scoping it, for one reason: scoping means a per-class YAML
override I cannot lint or launch, and an override placed wrongly relative to its `Inherits@` line
fails silently (CLAUDE.md, MiniYaml §"The override isn't taking effect"). A wrong scoping edit costs
a launch; a too-busy screen costs one line. If it does read as busy, the targeted fix is
`MinimumDrawnGrade: Spotted` under a `WithSpottedDecoration:` block in `^Vehicle` and `^Aircraft`,
placed **after** their `Inherits@` lines — infantry then keeps the full ladder and everything else
reverts to today's density.

---

## The font trap, which is the same trap as the missing-sequence one

`◆` U+25C6 BLACK DIAMOND and `◇` U+25C7 WHITE DIAMOND — the obvious pair, and the pair the earlier
code-truth note proposed (`WORKSPACE/notes/detectability-pip-code-truth.md:96-97`, "costs literally
one character — ◆ → ◇") — **are not in the shipped font.** `engine/mods/common/FreeSansBold.ttf`
carries no Geometric Shapes block at all: U+25A0, U+25B2, U+25C6, U+25C7 and U+25C8 all map to glyph
0. They would have rendered as nothing or as a notdef box, silently, exactly like a sequence naming a
`.shp` the mod does not ship.

The pair that does exist, verified in the font's own `cmap` and `glyf` tables:

- `◊` U+25CA LOZENGE — glyph 2144, 2 contours (hollow), bbox (16,-26)-(518,744)
- `♦` U+2666 BLACK DIAMOND SUIT — glyph 2152, 1 contour (solid), bbox (8,-56)-(587,748)

Rendered from that exact font file at that exact size:
`WORKSPACE/mockups/diamond-pip-font-proof.html`. Both glyphs are written as `\u25CA` / `\u2666`
escapes in the C# source rather than as literals, so a re-encoding of the file cannot quietly swap
them for something the font lacks.

## Cost — grading turned out to be free, not cheap

The recon expected grading to cost the loss of `IsSpotted`'s short-circuit
(`soldier-readout-recon-260902.md:45-48`). Grading on own posture instead means **the spotted query
is untouched** — same walk, same early return, same 7-tick cache — and the posture band is one field
read of `Detectable.CurrentVisibility`. There is no new spatial query and no additional work in the
existing one.

The posture band is deliberately **not** cached at `RecalculationInterval`: it is a field read, and
caching it would put a visible ~0.4 s lag on the one thing the player changes on purpose. The
enemy-derived half keeps the 7-tick cache exactly as before.

## Determinism

Nothing new is synced and nothing simulation-side reads any of this.
`Detectable.CurrentVisibility` is `[Sync]`ed simulation state and is only **read** here, which is
what every decoration already does. `World.RenderPlayer` is local, is read only, and is not written
back. No condition is granted — which is the point of the PITFALL at `Detectable.cs:218-220`.
`Detectability.Grade` takes ints and bools and returns an enum: no `World`, no `Actor`, no RNG.

## What I decided to leave out: the Ambush fire veto

The brief asked whether "blocked from acting because acting would reveal us" belongs in this readout.
**No, not in this glyph.** Four reasons: it is a different proposition from "how visible am I"; per
`detectability-pip-code-truth.md:36-49` being spotted is the veto's *exit* condition, so the two
states are near-mutually-exclusive and would be sharing a glyph they almost never both need; it needs
a tick-stamp written in `AutoTarget.AmbushTickIdle`, which is a simulation-side edit and outside a
render-path feature's blast radius; and there is already a shipped home for it — the holding-fire pip
at TopRight, driven by `AutoTarget.LastHeldFireTick`, which is exactly the shape it wants.

## No autotest scenario, and why

A decoration is drawn from `IDecoration.RenderDecoration` via `WorldRenderer` and is observable by
nothing in the simulation. There is no sim-side assertion that could distinguish a working diamond
from a missing one, so a scenario written for this would go green whether the feature worked or not.
The repo already agrees: the existing visibility-mark scenarios (`test-visual-gauge-truth`,
`test-unit-indicators-before`) are **screenshot** scenarios that carry no `Test.Pass`/`Test.Fail`
verdict on the mark itself. The pure grade function is covered by NUnit instead — 18 cases in
`engine/OpenRA.Test/DetectabilityGradeTest.cs`, both ends, every band boundary, band reachability,
monotonicity, and the anti-wallhack case.

## What to look at, and on which unit

One launch, any skirmish, own infantry, zoomed in:

1. **A rifleman standing still in the open** → solid amber `♦`. This is the resting state and it is
   what most of the screen will be; if it reads as an alarm rather than a readout, that is call 2 or
   the `ModerateColor` value.
2. **Order him to move** → solid orange `♦`. Watch the moment he stops: it should step back to amber
   within a frame or two, not after half a second. If it lags, the posture band got cached somewhere.
3. **Put him prone in cover** (`prone` + `object-proximity`) → hollow yellow `◊`. **The
   solid→hollow flip is the thing to judge** — it is the coarse channel and it has to be obvious at
   normal zoom without reading the colour.
4. **Dug in, in heavy cover** → hollow green `◊`.
5. **Walk an enemy you can see into line of sight** → solid red `♦`, the same red as today's `!`.
6. **A tank** → solid amber stopped, solid orange moving, and nothing else ever. Judge whether that
   is worth the pixels; see the caveat above.

The two things I could not check and would distrust first: whether the diamond is legible at 10px
against light terrain (snow, sand), and whether green vs yellow at the hollow end is separable at
that size — the hollow lozenge has thin strokes and thin strokes lose colour first.
