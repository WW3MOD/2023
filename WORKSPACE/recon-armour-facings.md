# Recon — per-facing armour, and what a tooltip diagram could honestly show

**Branch** `wt/armour-diagram` · **base** `main @ 925b5b82` · **date** 2026-09-03
**Deliverable** `WORKSPACE/mockups/armour-facing-diagram.html` (mockup only — no engine or YAML change)

Prompted by: *"Armour for tank says 280mm, but all vehicles can define armour front, back, sides,
and even top and bottom (for top attack weapons and mines) I think, I wonder if we can have that
armour section illustrate this with a little diagram?"*

Everything below was established by **reading source**, not by running the game. No launch, no
autotest, no `--check-yaml` (nothing here touches mod YAML).

---

## 1. Which facings exist — all five, and the user was right about top and bottom

`ArmorInfo.Distribution` is a five-element `int[]`, documented
`{ Front, Side, Rear, Top, Bottom }` **in percent**:

- `engine/OpenRA.Mods.Common/Traits/Armor.cs:31-32` — the field and its `[Desc]`.
- `engine/OpenRA.Mods.Common/Warheads/DamageWarhead.cs:142-219` — `ArmorDirectionPercent`, the only
  consumer.

Top and bottom are **real, distinct facings selected by an explicit weapon flag**, not an
approximation of something else:

| facing | index | selected by | code |
|---|---|---|---|
| Top / roof | `[3]` | `Weapon.TopAttack: true` | `DamageWarhead.cs:152-155` |
| Bottom / belly | `[4]` | `Weapon.BottomAttack: true` | `DamageWarhead.cs:156-159` |
| Front | `[0]` | impact angle | `DamageWarhead.cs:209` |
| Side | `[1]` | impact angle | `DamageWarhead.cs:210-211` |
| Rear | `[2]` | impact angle | `DamageWarhead.cs:212` |

The user's hedge — *"and even top and bottom … I think"* — is **correct**, and their parenthetical
is correct too: `TopAttack` is authored on the Javelin/`ATGM` (`weapons-missiles.yaml:6`) and four
artillery rounds (`weapons-ballistics.yaml:880, 974, 1007, 1097`); `BottomAttack` is authored on
exactly one weapon, **`ATMine`** (`weapons-explosions.yaml:245`). So "top attack weapons and mines"
is precisely the shipped set.

### Two corrections to the user's mental model

**Left and right are not separately definable.** Five slots, but only **four distinct values** —
`distribution[1]` is read for *both* flanks (`DamageWarhead.cs:210-211`, where `leftDamage` and
`rightDamage` both multiply by `distribution[1]`). A diagram must show one mirrored side number.

**The authored numbers are percentages, not millimetres.** The per-facing mm figure is derived:

```
effectiveThickness = thickness * armorPercent / 100      // DamageWarhead.cs:249, integer division
damage             = ApplyPenetration(damage, Penetration, effectiveThickness)   // :250
```

So a diagram would display a quantity that is **real and load-bearing in the damage model but exists
as a number nowhere in the YAML**. That is fine — it is exactly the arithmetic the player cannot do
in their head — but it means the diagram is a *computed* view, not a display of authored fields.

### Is "mm" a fiction? — No, but it is loose

`Thickness` is declared `[Desc("Armor thickness in mm.")]` (`Armor.cs:28-29`) and is compared
directly against warhead `Penetration`, which is the same scale. The ladder is internally coherent
across the roster: aircraft skin 3–20, APC 10–19, MBT 280–700, hardened bunker 2000
(`structures-defenses.yaml:1130`). **The tooltip's `mm` is the engine's own unit, not something a
worker invented** — the earlier `700 thick` → `700mm` change (`Armor.cs:85-88`) only adopted the
unit the field already declared. Whether individual values match real armour is a *balance*
question and separate; the T-90's 280 is well under commonly-cited real frontal figures.

### One nuance a four-number diagram slightly overstates

The three horizontal facings are **not discrete zones**. `ArmorDirectionPercent` linearly
interpolates between neighbours by impact angle (`DamageWarhead.cs:177-214`): a nose-on hit gets
100% of front, a 45° hit gets a front/side blend. Verified by hand-evaluating the branch arithmetic
at the cardinals — `alignment = 0` yields pure rear, `alignment = 512` yields pure front. So the
authored numbers are **exact at the four cardinals and smooth between**. Roof and belly are hard
switches on the weapon flag with no blending.

---

## 2. What the mod actually authors — the crux, and it is worse than it looks

**Per-facing armour IS authored** — the "nothing authors this, so a diagram would show four copies
of one number" outcome the brief anticipated did **not** happen. But the data is thinner than the
feature deserves.

Sixteen vehicles author `Distribution`. **Thirteen carry the identical flat line
`100,80,80,80,60`.** Only two are differentiated:

| actor | Thickness | Distribution | front / side / rear / roof / belly (mm) | site |
|---|---|---|---|---|
| `abrams` | 700 | `100,40,15,10,10` | 700 / 280 / 105 / 70 / 70 | `vehicles-america.yaml:499-500` |
| `t90` | 280 | `100,60,40,15,15` | 280 / 168 / 112 / 42 / 42 | `vehicles-russia.yaml:321-322` |
| `t72` | 280 | `100,80,80,80,60` | 280 / 224 / 224 / **224** / 168 | `vehicles-ukraine.yaml:25-26` |
| `humvee` | 10 | `100,80,80,80,60` | 10 / 8 / 8 / 8 / 6 | `vehicles-america.yaml:59-60` |
| `bmp2`, `btr`, `bradley`, `m113`, `m109`, `m270`, `strykershorad`, `giatsint`, `grad`, `tos`, `tunguska`, `iskander` | 5–19 | `100,80,80,80,60` | flat | `vehicles-*.yaml` |
| `^Vehicle` (fallback) | — | `100,50,25,10,10` | — | `vehicles.yaml:27` |

**Consequence for the feature:** on 13 of 16 vehicles a diagram draws a careful picture of four
numbers within 20% of each other. The diagram is dramatic on the two MBTs and near-mute elsewhere.
This is the single most important input to the user's choice, and it is a *content* problem, not a
UI one — differentiating the roster would fix it, and that is the user's call.

### Scope: vehicles only

Every `Distribution` in the mod is in `vehicles*.yaml`. Aircraft, structures and defences author
`Thickness` but **no** `Distribution`, and `ArmorDirectionPercent` returns a flat 100% unless
`distribution.Length == 5` (`DamageWarhead.cs:150`) — so their armour genuinely is uniform and they
must keep today's single-value row. All four mockup options fall back correctly.

---

## 3. Incidental bug found — `t72` carries the APC boilerplate

`t72` is an MBT with `Thickness: 280`, identical to the T-90, but carries the generic
`100,80,80,80,60`. Its **roof is 224mm against the T-90's 42mm — 5.3×**, and its side is 224
against 168. Almost certainly a copy-paste rather than a decision.

It bites today. `ATGM` is `TopAttack: true` with `Penetration: 100` (`weapons-missiles.yaml:6`):

- vs T-90 roof 42 → `penetration >= thickness` → **full damage** (`DamageWarhead.cs:130-131`).
- vs T-72 roof 224 → **`100/224` ≈ 45% damage** (`:133`).

A top-attack missile is less than half as effective against the *cheaper, weaker* tank. **Not
fixed** — out of scope for a mockup and it is a balance call, not a typo to correct unilaterally.
Logged to `WORKSPACE/bugs/discovered.md`.

This is also the strongest argument *for* building the feature: any of the four options would have
surfaced this the first time someone hovered a T-72.

---

## 4. Layout constraints the options had to respect

| constraint | value | citation |
|---|---|---|
| Description column width | **exactly 350px, always** | `MaxTooltipWidth = 350` (`ProductionTooltipLogic.cs:63`); `leftWidth = Math.Clamp(…, 350, 350)` (`:165-167`) — a clamp with equal bounds is a constant |
| Column origin | `x = 7`, `y = 27` | `engine/mods/common/chrome/tooltips.yaml:276-277` |
| Stat row height | ~13px (font-measured, **not** the template `Height: 17`) | `AddStatRow` returns `max(keySize.Y, valueSize.Y)` (`:388,416`); in-code estimate at `:226-227` |
| Section gap above ARMOUR | 12px | `SectionGapHeight` (`:237`) |
| Layout model | absolute, `y` incremented per row | `LayOutElements` (`:244-305`) |

**Width is free; height is the only contested axis.** Every option lays out horizontally to exploit
the 350px. Current T-90 content height is **137px**, so the percentages below are against that.

---

## 5. The four options and what each costs to build for real

| option | block height | added | panel growth | build cost |
|---|---|---|---|---|
| Today — single row | 13px | — | — | ships |
| **A** plan-view silhouette | 87px | +74px | +54% | Highest. New `TooltipElementKind` + a vector-drawing widget. The renderer has no primitive for this — the closest precedent is the `Separator` case drawing a `ColorBlockWidget` at explicit `WidgetBounds` (`:254-261`), so it is a genuinely new widget, not a new arrangement of existing ones. |
| **B** abstract rosette | 60px | +47px | +34% | Medium. Same new-widget work as A, simpler geometry, no silhouette to art-direct. |
| **C** one line + legend | 25px | +12px | +9% | **Lowest by far.** Reuses the existing `Stat` and `Note` kinds unchanged — plausibly a change to `Armor.cs` alone, returning two elements instead of one. No renderer change at all. |
| **D** bar ladder | 70px | +57px | +42% | Medium-low. Five `ColorBlock` pairs at computed widths; no vector path work, so it stays inside the widget vocabulary the renderer already has. |

Heights are computed by the mockup itself from the same row constants the engine uses, then
cross-checked by hand; treat as ±2px because the true figures are font metrics only the game can
measure.

**My recommendation, stated in the mockup:** **C**, with **B** as the pick if C reads too plain.
The flat-distribution finding is why — C is the only option that degrades gracefully on the 13
vehicles where there is nothing interesting to draw. That inverts if the roster gets differentiated.

---

## 6. Verified vs assumed

**Verified by reading source:** every `file:line` above; the five-facing array and its two flag-
selected entries; the left/right sharing; the percent-of-thickness arithmetic; the 350px clamp; the
full inventory of `Distribution` sites and their actors; the `TopAttack`/`BottomAttack` authoring
sites; that `Heavy` appears in a `Versus` table (`weapons-explosions.yaml:559-562`), which is what
makes the row render `Heavy — 280mm` rather than a bare `280mm` (`Armor.cs:71-93`).

**Computed, not observed:** the ATGM-vs-T-72 45% figure is arithmetic from
`ApplyPenetration` (`DamageWarhead.cs:128-133`), not a measured hit.

**Assumed:** the ~13px stat-row height, taken from the in-code comment at
`ProductionTooltipLogic.cs:226-227` rather than measured — every pixel figure in section 5 inherits
that uncertainty. The mockup's fonts approximate OpenRA's pixel fonts with a system sans, so glyph
widths are indicative; **proportions and heights are authored in true game pixels and are exact.**

**Not done, deliberately:** no game launch, no screenshots (manager runs launches serially), no
engine or YAML edit.
