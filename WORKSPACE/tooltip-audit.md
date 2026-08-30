# Tooltip audit — what a player actually sees, and a typed-element replacement

**Date:** 2026-08-30 · **Base:** `main @ b3a7564d` (clean worktree `wt/tooltip-standard`)
**Brief:** is the information there, is it consistent, can the presentation be given real structure.

Every figure below was computed by resolving `Inherits:` chains against the files actually listed
in `mod.yaml`'s `Rules:` block, not read off comments. Where I am making a **taste** call rather
than a correctness one, it is tagged **[TASTE]** so it can be overruled independently.

---

## 0. Headline corrections to the brief

Two premises I was given turned out to be wrong, and both change the shape of the work.

**"Supply cost is invisible in the UI."** It is not. It has been rendered in the production
tooltip since `da503233` ("P2: auto-generated weapon block in production tooltip"), via a WW3MOD-only
interface `IProvideTooltipDescription` (`engine/OpenRA.Mods.Common/Traits/IProvideTooltipDescription.cs`)
implemented by `AmmoPoolInfo` (`engine/OpenRA.Mods.Common/Traits/AmmoPool.cs:172-198`) and assembled
by `BuildDescriptionWithAutoBlocks` (`engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/ProductionTooltipLogic.cs:188-223`).
A player hovering a rifleman today sees `Ammo: 1 × 50 supply = 50`.

The real problem is not absence. It is that **the same number is printed twice, in two different
bases, in two different notations, adjacent to each other** — and that neither printed number is
the one the player needs to budget against. See §3.

**"A change about to ship makes all ammunition cost supply."** That change is *already on main*:
`f8b424f6` ("economy: all supply costs — meter the dock path and close the free trickle"), merged
at `9e46f141`. Both are ancestors of `b3a7564d`. What is *not* merged is `wt/supply-economics`
(`1b1e1d9d`), which prices every pool under that assumption and is a report, not a behaviour change.

The urgency is therefore real but differently located: the prices are **already live**, and the
interface is already talking about them — just badly.

---

## 1. The path, end to end

| Stage | Location |
|---|---|
| Chrome template | `engine/mods/common/chrome/tooltips.yaml:253-311` — `Background@PRODUCTION_TOOLTIP` |
| Logic | `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/ProductionTooltipLogic.cs` |
| Static text | `BuildableInfo.Description` (`engine/OpenRA.Mods.Common/Traits/Buildable.cs:60`) |
| Unit name | `TooltipInfo.Name`, first `EnabledByDefault` (`ProductionTooltipLogic.cs:71-72`) |
| Cost | `ProductionQueue.GetProductionCost`, else `ValuedInfo.Cost` (`:75-83`) |
| Auto blocks | `IProvideTooltipDescription` contributors, priority-ordered (`:194-200`) |
| Grand total | inline in the renderer, **2+ pools only** (`:205-212`) |

**WW3MOD ships no `tooltips.yaml` of its own.** `mods/ww3mod/chrome/` has 13 files and none is a
tooltip; `mod.yaml:222` loads `common|chrome/tooltips.yaml`. So the tooltip is stock OpenRA
*layout* driving WW3MOD-modified *logic*. That asymmetry is the single most important fact for
costing the work in §5: the layout is untouched and therefore free to replace.

### The widget budget, precisely

The template has exactly **nine** child widgets: `NAME`, `HOTKEY`, `REQUIRES`, `DESC`, and three
icon/label pairs (`COST`, `TIME`, `POWER`). Of these:

- `DESC` is **one `LabelWidget`, font `TinyBold`, one colour, no per-line styling**
  (`tooltips.yaml:275-280`). Everything below the unit name — prose, weapon lists, ammo maths,
  armour, the grand total — is concatenated into that single label's `Text` and wrapped by
  `WidgetUtils.WrapText` at `MaxTooltipWidth = 350` (`ProductionTooltipLogic.cs:57, 140-141`).
  **This is the whole of the "one blob of free text" problem.** There is no element vocabulary
  because there is no element — there is a string.
- `REQUIRES` renders prerequisites, filtered to drop `~` and `!` prefixes (`:100-101`). In WW3MOD
  essentially every prerequisite is `~player.*`/`~techlevel.*`, so **this label is almost always
  empty** and the row collapses.

### Live bug: every production tooltip draws an empty power row

`PowerManager:` is **commented out** at `mods/ww3mod/rules/player.yaml:163`, so `pm` is null
(`ProductionTooltipLogic.cs:29`) and the entire `if (pm != null)` block at `:118-127` never runs.
That block is the only thing that ever assigns `powerLabel.Visible` / `powerIcon.Visible`.
`Widget.Visible` defaults to `true` (`engine/OpenRA.Game/Widgets/Widget.cs:222`) and the template
sets no `Visible:` on either, unlike `HOTKEY` which explicitly sets `Visible: false`
(`tooltips.yaml:265`).

Consequence: the `production-tooltip-power` sprite — which does exist, at `chrome.yaml:144` — is
drawn on **every** production tooltip in the mod, beside a permanently empty label.
`powerIcon.Bounds.X` is assigned unconditionally at `:151`, so it is positioned on the right rail
whether or not the power branch ran.

> **Correction, made before fixing (2026-08-30).** The first version of this section also claimed
> every tooltip was "20px taller than its content needs", from `rightHeight` at `:159` taking the
> `powerIcon.Bounds.Bottom` branch. **That part is wrong.** The height is
> `Math.Max(leftHeight, rightHeight)` where `leftHeight = 36 + descSize.Y` (`:156`) and
> `rightHeight = 67`. The power branch only inflates anything when `descSize.Y < 31` — under about
> three rendered lines — and every WW3MOD description plus its auto-blocks is far longer. **The
> tooltip is never actually taller.** Only the stray sprite was real. Recorded rather than quietly
> edited, because the overclaim came from reading the layout arithmetic without asking what values
> actually reach it.

**Fixed on this branch** in `ProductionTooltipLogic`, by giving the null-`pm` case an `else` that
hides both widgets. The bug is generic to any mod without a `PowerManager` — the logic simply never
handled that case — so it belongs in the logic, not in a per-mod chrome override.

---

## 2. Census — the information IS there

Restricted to the 35 rule files actually loaded by `mod.yaml:109-144`, excluding actors gated
`Buildable.Prerequisites: ~disabled`:

| Metric | Count |
|---|---|
| Live buildable actors | **54** — 28 infantry, 20 vehicles, 6 aircraft |
| …structures among them | **0** (see below) |
| …with a resolved `Tooltip: Name:` | **54** (100%) |
| …with a resolved `Buildable: Description:` | **54** (100%) |
| …with a resolved `Valued: Cost:` | **54** (100%) |
| Distinct description strings behind those 54 | 43 (40 live) |

**Nothing is missing.** An earlier pass of this audit reported ~35% of actors lacking descriptions;
that was an artifact of reading `infantry-america.yaml` / `infantry-russia.yaml` literally and not
resolving `Inherits@BaseUnit:`. The faction variants are thin overlays — `E1.russia`
(`infantry-russia.yaml:2-8`) sets only `Buildable.Prerequisites` and `RenderSprites.Image` — and
they inherit name, cost and description correctly. **Do not re-open this as a data-completeness
problem; it is a presentation problem.**

### There is no such thing as a structure production tooltip

**All 16 `Buildable:` blocks in `structures-defenses.yaml` carry `Prerequisites: ~disabled`**, and
`logisticscenter` likewise (`structures.yaml:367`). No structure ever appears in the sidebar, which
is exactly what the no-base-building model in `game-model.md` implies. So "sample structures, both
factions" has no production-tooltip answer to give.

Structures do have a tooltip — the **world tooltip**, shown on map hover:
`Background@WORLD_TOOLTIP` (`tooltips.yaml:61-89`), driven by `WorldTooltipLogic.cs`. It has three
labels: `LABEL` (the `Tooltip.Name`), `OWNER` (+ flag), and `EXTRA`, which is fed by a *different*
interface, `IProvideTooltipInfo` — implemented only by `PowerTooltip`, `Sellable` and
`TooltipDescription`. **It carries no description, no stats, and no cost.** Hovering a Logistics
Centre tells the player its name and whose it is; the **2250 supply it holds is surfaced nowhere in
the game**.

This is a second, poorer surface with its own widget and its own interface. The design in §4 applies
to it cleanly, but reaching it is additional work and is **excluded** from the smallest-first-step
costing in §5. Flagged because "standardise the tooltip" naturally reads as covering both, and it
does not.

Three prose-only descriptions with RA-era vocabulary do exist — `'Deploys into Command Center'`
(`old.yaml:2`), `'Deploys into Field Base'` (`old.yaml:42`), `'Main Battle Tank'`
(`vehicles-ukraine.yaml:2`) — but **neither file is in the `Rules:` list**, so they are dead and no
player can see them. Noted so nobody "fixes" them.

### Shape consistency of the 40 live strings

| Convention | Conforming |
|---|---|
| Opening prose sentence, then `\n\n`, then ` - ` bullets | **40 / 40** |
| Closes on an armour bullet (`Armor: X` / `No armor`) | **40 / 40** |

The hand-written descriptions are **already standardised** and follow one house format precisely.
Shortest live: `'Mobile anti-aircraft platform.\n\n - AA autocannon\n - Armor: Medium'` (68 chars,
`vehicles-russia.yaml:784`). Longest: `tos` at 201 chars (`vehicles-russia.yaml:659`). That is a 3×
spread, which is normal editorial variance, not inconsistency.

**This is the finding that should most change the plan.** The author-written half of the tooltip is
in good shape. The inconsistency is entirely in the seam between it and the machine-written half.

---

## 3. The actual inconsistency: two authors, one label

Ten live actors hand-write a supply figure *inside* the description prose, while the engine
independently generates the same figure from `AmmoPoolInfo`. They disagree about notation and about
base.

| Actor | Prose in `Description` | Engine auto-block for the same pool | file:line (prose) |
|---|---|---|---|
| `E3` rifleman | `- Disposable anti-tank rocket (supply: 50)` | `Ammo: 1 × 50 supply = 50` | `infantry.yaml:1192` |
| `TL` team leader | `- 40mm grenade launcher (supply: 8/rnd)` | `Ammo: 6 × 8 supply = 48` | `infantry.yaml:1452` |
| `AT` specialist | `- ATGM launcher (supply: 65/missile)` | `Ammo: 3 × 65 supply = 195` | `infantry.yaml:1688` |
| `AA` specialist | `- MANPAD launcher (supply: 65/missile)` | `Ammo: 3 × 65 supply = 195` | `infantry.yaml:1761` |
| `SF` special forces | `- 3 C4 charges (supply: 33 each)` | `Ammo: 3 × 33 supply = 99` | `infantry.yaml:2075` |
| `E6` combat engineer | `- 3 explosives (supply: 50 each)` | `Ammo: 3 × 50 supply = 150` | `infantry.yaml:1831` |
| `SN` sniper | `- 7.62mm sniper rifle (supply: 20 per 5-round batch)` | `Ammo: 50 (10 batches × 5 rounds × 20 supply = 200)` | `infantry.yaml:1615` |
| `MT` mortar | `- 60mm mortar (supply: 40 per 5-round batch)` | `Ammo: 25 (5 batches × 5 rounds × 40 supply = 200)` | `infantry.yaml:1542` |
| `E2` grenadier | `- Grenade launcher (supply: 10 per 6-round batch)` | `Ammo: 30 (5 batches × 6 rounds × 10 supply = 50)` | `infantry.yaml:1381` |
| `DR` drone operator | `- Reconnaissance drone (supply: 25)` | `Ammo: 1 × 25 supply = 25` | `infantry.yaml:2351` |

**Five notations for one quantity:** `/missile`, `/rnd`, `per N-round batch`, `each`, and bare
`(supply: N)`. Three of them are unit prices; two are pool totals; the bare form is ambiguous and is
used for both (`E3`'s 50 is a total, `DR`'s 25 is a total, but `TL`'s `8/rnd` is not).

**Worked example — the rifleman, `E3.america`, as rendered today.** `Description` at
`infantry.yaml:1192` plus two auto-blocks plus the grand total, all in one `TinyBold` label:

```
Standard infantry rifleman and backbone of the fire team.

 - 5.56mm rifle
 - Disposable anti-tank rocket (supply: 50)
 - No armor

5.56mm Rifle
  Ammo: 100 (5 batches × 20 rounds × 1 supply = 5)
AT Rocket
  Ammo: 1 × 50 supply = 50
Total ammo cost: 55
```

The number `50` appears twice. The rifle's `5` appears once and was never mentioned in the prose.
The number the player must actually budget — **55** — appears once, last, in the same weight and
colour as everything above it, on a line whose label (`Total ammo cost`) does not contain the word
*supply* at all.

### The grand total only exists for multi-pool units

`ProductionTooltipLogic.cs:208` gates the total on `pools.Length >= 2`. So:

- `E3` (2 pools) shows `Total ammo cost: 55`.
- `abrams` (1 pool) shows **no total line at all** — its 240 appears only inside
  `Ammo: 40 (8 batches × 5 rounds × 30 supply = 240)`, where it reads as the tail of an arithmetic
  expression rather than as a price.

Two units, two different answers to "what does refilling this cost", one of which is not stated.
This is the single clearest argument for a typed element: **a `cost` row should be a cost row on
every unit, whether or not the arithmetic behind it happened to need two terms.**

### The same weapon has two names, four lines apart

`FormatWeaponLabel` (`AmmoPool.cs:200-209`) builds the auto-block heading from the **raw YAML weapon
key**, stripping only `^`, `-` and `_`. It does not touch `.`. So on the rifleman, the prose says
`5.56mm rifle` and the machine block immediately below says **`5.56mm.DMR`** — the literal ruleset
key, dot included. On the abrams the heading is `TankRound.Abrams`; on the HIMARS it is
`HIMARSTargeter`, which is an internal targeting weapon name.

This is a presentation bug with no data behind it: the human-readable name already exists two lines
up, in the prose the same author wrote. A typed `ListItem` emitted by the armament trait removes the
duplication and the leak together.

### 32 of 54 descriptions state an armour class the actor does not have

Cross-checking each live description's armour bullet against the resolved `Armor.Type`:

| Group | Description says | `Armor.Type` is | Count | Material? |
|---|---|---|---:|---|
| All infantry | `- No armor` | `Kevlar` | 28 | **No** — see below |
| `heli`, `hind`, `mi28` | `- Armor: Medium` | `Heavy` | 3 | **Yes** |
| `lccv`, `mnly` | `- Armor: None` | `Light` | 2 | **Yes** |
| `truk` | `- Armor: None` | `Unarmored` | 1 | No |

**The infantry case inverts the obvious conclusion, and this is the most important thing in this
section.** `Kevlar` is set exactly once — `infantry.yaml:175`, on the shared infantry template — and
it appears in **zero** warhead `Versus:` tables. The tables name only `Concrete`, `Light`, `Medium`,
`Heavy`, `None`, `Wood`, `Brick`. An armour type absent from `Versus` takes the warhead's default,
i.e. **100% damage**. So infantry genuinely have no damage reduction: the prose `No armor` is
**mechanically correct**, and `Kevlar` is a phantom string that protects nothing. `Unarmored` is
likewise absent from every table, so `truk` is fine too.

**Design consequence, and it is a trap worth stating loudly: do not auto-bind the armour `StatRow`
to `Armor.Type`.** Doing so would print `Armour: Kevlar` on 28 infantry and replace a true statement
with one that implies protection the damage model does not grant. The armour row must resolve
through the `Versus` tables — "does any warhead treat this type specially?" — or stay author-written.
This is precisely the class of error the brief warned about: trusting engine data because it is
structured.

The **five material mismatches** (`heli`, `hind`, `mi28` under-stating Heavy as Medium; `lccv`,
`mnly` under-stating Light as None) are a genuine content bug and are filed to
`bugs/discovered.md`. Not fixed here — they are balance-visible text and belong to whoever owns
those units.

### What is never shown at all

`Full refill cost as a fraction of a Logistics Centre.` The provider capacities are
`LOGISTICSCENTER.TotalSupply: 2250` (`structures.yaml:466`) and `truk: 750` (`vehicles.yaml:569`),
and nothing in any tooltip relates a unit's refill to either. Figures below are from the computed
table in `1b1e1d9d:WORKSPACE/supply-cost-audit.md`:

| Unit | Cost | Full refill | % of purchase | Refills per LC (2250) |
|---|---:|---:|---:|---:|
| `HIMARS` | 6000 | **3000** | 50.0% | **0.8** |
| `abrams` | 2500 | 240 | 9.6% | 9.4 |
| `E3` rifleman | 100 | 55 | 55.0% | 40.9 |
| `AR` auto rifleman | 100 | 10 | 10.0% | 225.0 |

**One HIMARS reload costs more than an entire Logistics Centre holds.** A player can currently
learn that only by draining one. Two units at identical purchase price (`E3` and `AR`, both 100)
differ 5.5× in sustain cost, and the interface presents that difference as two similar-looking
arithmetic lines in identical styling.

---

## 4. Proposed element vocabulary

The design goal is that the *renderer* stops receiving a string and starts receiving a list of typed
rows, so that styling is a property of the row's type rather than of characters the author typed.

### The elements

| Element | Renders as | Why it exists |
|---|---|---|
| `Header` | `Bold` 14, ink `d4d4d4` | The unit name. Already exists as `NAME`. |
| `Subhead` | `TinyBold` 10, ink-3 `686868`, uppercase | Class/role line (`NATO · MAIN BATTLE TANK`). Separates identity from stats so the eye lands once. |
| `Prose` | `Small` 12, ink-2 `969696`, wrapped | The existing opening sentence. One paragraph, never a list. |
| `StatRow` | label ink-3 left, value ink right, dot leaders | A **named quantity**: Armour, Speed, Sight. Two-column so values align down the card and can be compared across two tooltips. |
| `CostRow` | as `StatRow` + amber value, optional context suffix | Distinct from `StatRow` because a price is the one number the player is *deciding* on. Carries the `≈ 0.8 Logistics Centres` context. |
| `ListItem` | ` - ` bullet, `Small` 12, ink-2 | Armaments and capabilities. What the prose bullets already are. |
| `Separator` | 1px rule at `line 262626` | Group boundary. Replaces the `\n\n` that currently does this job invisibly. |
| `Note` | `Tiny` 10, ink-3, italic-substitute | Caveats: *"Cannot be resupplied in the field."* Distinct from `Prose` so it can be visually demoted. |

Eight elements. I deliberately did **not** include a `Table`, a `ProgressBar`, or an `Icon+Text`
row: none of the data in §2–§3 needs them, and each would be a widget to build and maintain.
**[TASTE]** — the split of `StatRow` from `CostRow`, and of `Note` from `Prose`, is a judgement.
Both pairs could collapse to one element with a colour parameter; I split them because the
distinction is semantic (a price is decided on; a caveat is demoted) and semantics are what survive
a restyle.

### Order, identical for every unit

```
Header
Subhead
Separator
Prose
ListItem ×n          — armaments and capabilities
Separator
StatRow ×n           — Armour, Speed, Sight
CostRow  Call-in     — purchase price
CostRow  Full refill — supply, with LC fraction
Note ×n              — only when true of this actor
```

A rifleman, an abrams, a HIMARS and a structure all render through that with **no special-casing**.
The variation is in which rows are *present*, never in which order they appear:

- **Rifleman** — 3 `ListItem` (rifle, AT rocket, no armour → moves to `StatRow`), both `CostRow`s.
- **Abrams** — 1 `ListItem`, both `CostRow`s. Single-pool, so it gets a `Full refill` row for the
  first time (today it gets no total at all).
- **HIMARS** — 1 `ListItem`, both `CostRow`s, plus a `Note`: the LC fraction exceeds 1.0.
- **Structure** (`LOGISTICSCENTER`) — no armament `ListItem`s, no refill `CostRow`; instead a
  `StatRow` for `Supply held 2250`. Absent rows simply do not emit; nothing is special-cased.

### Where the data lives

**No new per-actor trait, and no schema change to `Buildable`.** Three sources already exist:

1. `Buildable.Description` keeps the opening prose — but the ` - ` bullets are **removed from the
   string** and re-emitted as `ListItem`s by the traits that own the facts. That deletes the
   §3 duplication at its root: the rifleman's rocket price stops being authored in two places.
2. `IProvideTooltipDescription` already exists and is already priority-ordered with a documented
   scale (weapons 100 / armour 200 / speed 300 / capabilities 400 —
   `IProvideTooltipDescription.cs:23-27`). It is the correct extension point and it is **already
   the mechanism WW3MOD chose**.
3. New contributors implement it on traits that already hold the data: `MobileInfo` (speed),
   `HealthInfo` (HP), `ValuedInfo` (call-in cost), `SupplyProviderInfo` (supply held).

**With one exception, restated because it is the trap in this whole design: `ArmorInfo` is NOT a
safe source.** Binding an armour row to `Armor.Type` prints `Kevlar` on 28 infantry who take full
damage from everything (§3). Either resolve the row through the `Versus` tables, or leave armour
author-written. A structured field is not automatically a true one.

The only genuinely new datum is the LC-fraction context, and it is derived, not authored:
`refill / 2250`, both terms already in the ruleset.

---

## 5. What it costs to build

The interface returns `string` today (`IProvideTooltipDescription.cs:35`). Typed elements need it
to return rows. That is the one unavoidable engine change.

**Yaml-only is not sufficient** and I want to be unambiguous about why: `DESC` is a single
`LabelWidget` with one `Font:` field. No amount of chrome yaml gives two fonts inside one label.
Typed styling requires at least one new widget.

### Smallest viable first step

Change `IProvideTooltipDescription` to return `IEnumerable<TooltipElement>` (a small
`readonly record struct` of `Kind`, `Label`, `Value`), and replace the single `Label@DESC` with a
vertical container that instantiates one pre-styled `LabelWidget` per element kind. Concretely:

| Change | Size |
|---|---|
| `TooltipElement` record + `TooltipElementKind` enum | new file, ~40 lines |
| `IProvideTooltipDescription` signature | 1 method, 1 existing implementor (`AmmoPoolInfo`) |
| `ProductionTooltipLogic` — emit rows instead of concatenating | rewrite of `BuildDescriptionWithAutoBlocks` + the `DESC` measure/layout at `:140-144` |
| `tooltips.yaml` — `DESC` becomes a container with per-kind child templates | ~40 lines, additive |
| New contributors (armour, speed, sight, costs) | one ~15-line method each, on traits that already exist |

**It composes existing widgets.** `LabelWidget` and `ColorBlockWidget` (the bevel/rule primitive the
lobby already uses per `_lobby-palette.yaml`) cover all eight elements. No new widget *class* is
required — only a layout container and a per-kind style table. There is no rewrite of the widget
layer, which was the explicit bar set in the brief.

**Staging, if you want a first step smaller still:** ship the `CostRow` alone. Give `ValuedInfo` a
contributor emitting `Full refill  55 supply  ≈ 1/41 Logistics Centre`, and unconditionally — fixing
the single-pool gap in §3 without touching the interface signature at all. That is ~30 lines,
yaml-free, and it is the change that carries the urgency. Everything else in this document can
follow at leisure.

---

## 6. Mockups

Static HTML, constraint-faithful in the manner of `WORKSPACE/lobby/mockups/full-page-realistic.html`
— only what OpenRA can render (flat rectangles, 1px `ColorBlock` rules, the mod's real font sizes
from `mod.yaml:290-316`, the grayscale palette from `chrome/_lobby-palette.yaml`). No radii, no
shadows, no gradients.

- `WORKSPACE/tooltip-mockups/before-after.html` — `E3` rifleman, today vs proposed, side by side.
- `WORKSPACE/tooltip-mockups/units.html` — rifleman, abrams, HIMARS, Logistics Centre through the
  identical template.
- `WORKSPACE/tooltip-mockups/elements.html` — the eight elements in isolation, as a style reference.

All figures in the mockups are the computed ones from §3, not invented.

---

## 7. Taste calls, collected

Flagged so they can be overruled without disturbing the correctness findings.

1. **[TASTE]** Splitting `StatRow`/`CostRow` and `Prose`/`Note` (§4). Could be 6 elements, not 8.
2. **[TASTE]** Showing the refill as a fraction of a Logistics Centre. The alternative is an
   absolute number only. I think the fraction is the decision the player is making, but it hard-codes
   a comparison to one provider and reads oddly for units that cannot use an LC at all.
3. **[TASTE]** Moving armour out of a prose bullet into a `StatRow`. It is currently the last bullet
   on all 40 live strings and that is a genuine convention being broken.
4. **[TASTE]** The `Subhead` (`NATO · MAIN BATTLE TANK`) is new information not in any current
   tooltip, and would need authoring for 54 actors.
5. **[TASTE]** The supply amber `#c8a45a`. `_lobby-palette.yaml` is grayscale-strict with a listed
   set of sanctioned exceptions; this proposes one more, in the same "informative coding" class as
   the green/red option chips. Setting it to `ink` costs the design nothing structural.

**Correctness, not taste** — these are wrong regardless of how the design lands:

- The empty power row on every production tooltip (§1) — `player.yaml:163`.
- Five notations for one quantity, and the rifleman's rocket price printed twice (§3).
- The raw YAML weapon key leaking into the UI as `5.56mm.DMR` / `HIMARSTargeter` (§3).
- No refill total at all on single-pool units such as the abrams (§3).
- Five actors whose stated armour class is materially wrong (§3) — `heli`, `hind`, `mi28`,
  `lccv`, `mnly`.

## 8. What I did not verify

**No build, no launch, no screenshots** — the brief excluded them, so every statement about
*rendering* is read from the widget code and chrome yaml, not observed. Specifically:

- The empty power row is derived from `Visible = true` (`Widget.cs:222`) plus the null-`pm` branch.
  I did not see it on screen. It is the one finding I would most want confirmed by a launch,
  because a hidden default somewhere in `Background`/`Container` handling could suppress it.
- Build **times** in the mockups (`0:04`, `0:25`, `1:00`) are illustrative. Cost, refill, armour,
  speed and health are computed; build time is not.
- The `Versus`-table reasoning assumes OpenRA's standard "armour type absent from `Versus` takes the
  warhead default" semantics. I read the YAML tables, not `Warhead.cs`.
- I *did* check that no map ruleset re-enables a structure's `Buildable` (no `Buildable` or
  `Prerequisites` key anywhere under `mods/ww3mod/maps/`), so "no structure production tooltip"
  holds for shipped maps. I did not check mission/campaign rulesets outside the `Rules:` list.
