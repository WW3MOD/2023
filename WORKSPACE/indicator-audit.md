# Unit indicator audit — what is drawn over a unit, and whether it is telling the truth

**Ref: `main @ d37367aa`** (worktree `wt/indicator-audit`, branch base `d37367aa`, level with `origin/main` at audit time).
**Research only.** No behaviour changed, no YAML touched, no engine C# touched. Nothing was launched, no screenshot was
taken, no lint or test was run — those were withheld from this job.

**Method and its limits.** Every claim below is READ from source at this ref and carries a `file:line`. Where a number is
computed from read values rather than observed, it is labelled **DERIVED**. Where I could not establish something, it says
so. **Nothing here has been seen on screen** — and that matters more than usual, because the two commits that built the
detectability diamond say the same of themselves (`8308aa83`: *"Not launched and not screenshotted"*; `96612c37`: *"NOT
VERIFIED ON SCREEN … Expect one tuning round."*). This audit is the second reading of an unobserved feature, not a
confirmation of it.

---

## 0. Required: does each mark report a mechanic that actually does anything?

This is the section the rest of the document exists to support. The project has recorded itself making exactly one
mistake in this area — redesigning or adding a *readout* when the problem was the *behaviour underneath it*
(manager decision 08, §9). So for every mark: what it claims, what the mechanic verifiably does at this ref, and
whether those agree.

| Mark | What the mark claims | What the mechanic does at `d37367aa` | Agree? |
|---|---|---|---|
| **Detectability diamond** | "How exposed this unit is", 5 grades, 2 fill states | `Detectable.CurrentVisibility` is live, `[Sync]`, recomputed every tick (`Detectable.cs:127-138`), clamped to `[1,9]` (`:118-125`). Fed by real modifiers. | **PARTIAL.** Mechanic real; the mark is faithful. But per-chassis dynamic range collapses — **on vehicles only 2 of 5 grades are reachable and the diamond is never hollow** (§3.4). Self-flagged at authoring time: `WORKSPACE/diamond-pip-design-260903.md:71-88`. |
| **Stance — Fire axis** (`X` HoldFire, `A` Ambush) | "I deliberately set this unit to hold fire / to ambush" | `AutoTarget.Stance` is real and drives the state machine (`AutoTarget.cs:22`, `:369`). The glyph reads the trait directly (`WithStanceDecoration.cs:108`), **not** the dead `stance-*` tokens. | **DISAGREE, in implication — the dangerous row.** The glyph faithfully reports `Stance`, but `Stance` *means a different thing on a bot's unit than on the player's*: stages 2–4 of Ambush are gated on `enable-ambush-tactics`, which **no human opt-in path grants** (`PIPELINE.md:618`). A human's gold `A` promises a coordinated hold-and-spring they do not get. Separately **no stance modifies detectability** (`discovered.md:1033-1037`), so an `A` sitting beside a concealment diamond implies a link that does not exist. |
| **Stance — Engagement axis** (`H` HoldPosition, `>` Hunt) | "This unit is set to hold position / to hunt" | `EngagementStance` real (`AutoTarget.cs:24`, `:371`), honoured in C# by `StancePositioningExecutor.cs:318`. `Defensive` is the default and deliberately draws nothing. | **AGREE.** |
| **Suppression pips** (10 tiers) | A 10-step suppression level | **Real for both chassis.** 74 `Condition: suppressed` sites; the grants live in `weapons-effects.yaml` and include **13 `ValidTargets: Vehicle`** grants (`:140,145,150,299,305,311,362,368,374,425,431,437,443`) alongside the infantry ones. Consumed by live speed / vision / burst / burst-wait / inaccuracy ladders on infantry (`infantry.yaml:436-588`) and turret-turn on vehicles (`vehicles.yaml:368+`). | **AGREE on existence, DISAGREE on legibility.** `DOCS/reference/architecture.md:491`: *"It is ten colours of ONE glyph, not a bar that fills — do not design a readout around the ten tiers being individually legible"* and *"at 6×3 px adjacent tiers are not separable on screen."* |
| **Damage pips** | Health band | Real; tied to `^DamageStates` (`defaults.yaml:259-283`). **This is the mod's only health indicator** — the health bar is off. | **AGREE.** |
| **Rank chevrons** | Veterancy 1–4 | Real (`^GainsExperience`, `defaults.yaml:285-292`), with live combat modifiers. | **AGREE.** |
| **Holding-fire pip** | "This unit is declining a shot on purpose" | Real — reads `AutoTarget.LastHeldFireTick` (`WithHoldingFireDecoration.cs:53`). | **AGREE.** |
| **Concealment range circles** (10 rungs) | "You are seen from this far away", 10 tiers | Real, but **rung 10 can never be granted** and **rungs 1 and 2 share a radius** (§4). | **PARTIAL — a 10-rung ladder that presents at most 8 distinct radii, one of them unreachable.** |
| **Defense pips** (5 tiers) | A 5-step "defense" level | **DEAD.** `^DefensePips` is defined at `infantry.yaml:671` and **inherited by nothing** (repo-wide grep: the definition is the only hit). The `defense` condition is **granted nowhere** — only consumed, at `:678,685,692,699,706`. | **N/A — cannot draw. A complete 5-tier indicator that no actor carries and no condition feeds.** |
| **Cargo pips** | Passengers aboard | Real, but yields nothing on any actor that also has `WithGarrisonDecoration` (`WithCargoPipsDecoration.cs:70,:97`; `architecture.md:1340`). | **PARTIAL.** |
| **Health bar** | — | **Deliberately disabled**, `SelectionBarsAnnotationRenderable.cs:168-181`. *"Re-enabling this is a product decision, not a cleanup: it turns on an indicator the user switched off himself. Ask before uncommenting."* | **N/A — off on purpose.** |

**The two rows that need opposite treatment.** The diamond and the suppression pip are *display* problems — the mechanics
underneath them are real and working, so redrawing them is legitimate and is not the decision-08 trap. The **Fire-axis
stance glyph is not a display problem**: the glyph is already an accurate report of `Stance`, and what is wrong is that
`Stance` does not do for a human what the UI around it implies. Redesigning that glyph would be the decision-08 error
committed a second time, in the same subject area, against the same user complaint.

---

## 1. Complete inventory

Two families draw here, and they behave differently:

- **`IDecoration` traits** — drawn by `SelectionDecorationsBase.DrawDecorations` (`SelectionDecorationsBase.cs:129-132`).
  Anchored to a named corner/edge of the actor's decoration bounds. This is where almost everything lives.
- **`ISelectionBar` traits** — a stacked bar row along the bottom edge, drawn by
  `SelectionBarsAnnotationRenderable.DrawExtraBars` (`:51-64`), each 3px tall, stepping 4px up per bar (`:58`).

**Universal gates** (from `WithDecorationBase.ShouldRender`, `:150-173`):
- Hidden when `World.FogObscures(self)` (`:152`).
- **`ValidRelationships` defaults to `Ally`** (`:108`) — so *every* mark below is on your own and allied units only,
  unless a rule says otherwise. No mark in the list leaks information about enemy units.
- Optional `BlinkPattern` / `BlinkInterval`, wall-clock driven (`:157-162`).
- `RequiresSelection` (`:111`) splits the set into always-on and selected-only.

| # | Mark | Trait | Carried by | Renders when | Anchor + Margin | Shape / palette |
|---|---|---|---|---|---|---|
| 1 | Selection box (4 corner brackets) | `SelectionDecorations` → `SelectionBoxAnnotationRenderable` | `^Selectable` (`defaults.yaml:1051-1056`) | selected only (`SelectionDecorationsBase.cs:109-111`) | the bounds rect | 4 L-shaped corner brackets, 1px, `SelectionBoxColor` default White (`SelectionDecorationsBase.cs:22`). **Suppressed on infantry** — `ShowNever: true` at `infantry.yaml:56-57`, the only such line in the mod |
| 2 | Health bar | — | — | **never** (`SelectionBarsAnnotationRenderable.cs:181` commented out) | — | — |
| 3 | Extra bars (EMP timer, capture progress, …) | `ISelectionBar` implementers | `TimedConditionBar@EMP` on `^AffectedByEMP` (`defaults.yaml:1039-1041`); `CapturableProgressBar` on structures (`structures.yaml:227`) | selected, or `StatusBars != Standard`, or rollover (`SelectionDecorationsBase.cs:90-107`) | bottom edge, +4px per bar | 3 stacked 1px lines, RGBA, colour from the trait |
| 4 | **Detectability diamond** | `WithSpottedDecoration` | `^UnitIndicators` → `^SelectableCombatUnit/Support/Economic` (`defaults.yaml:1059,1065,1071`) → **infantry, vehicles, aircraft** (`infantry.yaml:12`, `vehicles.yaml:6`, `aircraft.yaml:6`) | **always** (`RequiresSelection: false`, `defaults.yaml:929`) | `Top`, `0,-10`; infantry override `0,-16` (`infantry.yaml:772-774`) | glyph `◊`/`♦` in TinyBold 10pt, 5 RGB colours. §3 |
| 5 | **Stance glyph — Fire** | `WithStanceDecoration@Fire` | same as #4 | **always**, and only for non-default stances | `Top`, `-8,-10` → **+8px RIGHT** (X is negated) | letter `X` (235,235,235) or `A` (255,210,70), TinyBold |
| 6 | **Stance glyph — Engagement** | `WithStanceDecoration@Engagement` | same as #4 | **always**, non-default only | `Top`, `-16,-10` → **+16px RIGHT** | `H` (105,205,255) or `>` (255,145,45) |
| 7 | **Suppression pips** | `WithDecoration@Suppression_1..10` | `^SuppressionPips` ← `^SuppressionEffects` (infantry, `infantry.yaml:423`) **and** `^VehicleSuppressionEffects` (vehicles, `vehicles.yaml:367`) | **selected only** (`RequiresSelection: true`, `infantry.yaml:591`) | `Top`, `0,-3` | `pip-suppression.shp`, 10 sequences (`sequences-misc.yaml:378-406`), `chrome` palette. 6×3px per `architecture.md:491` |
| 8 | Damage pip — vehicle | `WithDecoration@DamageVehiclePips_*` | `^DamageVehiclePips` → vehicles (`vehicles.yaml:18`), aircraft (`aircraft.yaml:13`) | always, per damage band | `Top`, `0,0` | `pip-damage-vehicle.shp`, **17×5 BAR**; yellow → orange → red → dark-red (`defaults.yaml:184-187`) |
| 9 | Damage pip — infantry | `WithDecoration@DamageInfantryPips_*` | `^DamageInfantryPips` → `^Infantry` (`infantry.yaml:18`) | always, per damage band | `Top`, `0,-5` | `pip-damage-infantry.shp`, **6×6 DOT**; yellow (255,255,85) → orange (255,138,0) → red (255,0,0) → dark red (134,0,0) (`infantry.yaml:713-717`) |
| 10 | Rank chevrons | `WithDecoration@Rank_1..4` | `^GainsExperience` | always, `ValidRelationships: Ally` | `Top`, `16,0` → **16px LEFT**; infantry override `10,0` (`infantry.yaml:749-757`) | `rank.shp`, `effect` palette |
| 11 | Cargo pips | `WithCargoPipsDecoration` | `^CargoPips` (`defaults.yaml:1024-1029`) | always while `loaded` | `Top`, `0,-10`, `PipStride 6,6` | `class` image; **scale 0.5 + alpha 0.05 when unselected**, 1.0/0.2 when selected (`WithCargoPipsDecoration.cs:101-102`) |
| 12 | Ammo-empty pips | `WithDecoration@Ammo*None` | `^AmmoDecoration` → vehicles `:11`, aircraft `:11` | always, blinking | `Bottom`, `0,-5` | `pip-ammo.shp`; `BlinkPattern: Off, On` |
| 13 | Ammo pips (per-pool) | `WithAmmoPipsDecoration` | named aircraft/defences | per rule | varies | `pips` image |
| 14 | Holding-fire pip | `WithHoldingFireDecoration` | `^HoldingFireMarker` ← `^AutoTarget` (`defaults.yaml:395`) | always, 15-tick linger (`:23`) | `TopRight`, `0,0` | `pip-orange` from `pips2.shp` (`sequences-misc.yaml:278`) |
| 15 | Control-group number | `WithSpriteControlGroupDecoration` | `^Selectable` (`defaults.yaml:1054-1055`) | **selected only** (`:51`) | `TopLeft`, `-2,0` | `groups` sequence, `pips` image, `chrome` |
| 16 | Class pictogram | `WithDecoration@Class` | `^Soldier` (`infantry.yaml:226-231`), crew | always | `Top`, `0,6` (**below** the top edge) | `class` image, `effect` palette |
| 17 | "Selected" pip | `WithDecoration@Selected` | `^Soldier` (`infantry.yaml:232-236`) | **selected only** | `Top`, `0,6` — **identical to #16** | `selected_infantry`/`pip-selected` (`sequences-infantry.yaml:61-62`) |
| 18 | Evacuating pip | `WithDecoration@Evacuating` | infantry `:162`, aircraft `:182,236` | always while `evacuating` | `TopRight` | `pip-orange` |
| 19 | Ammo-replenishing pip | `WithDecoration@AmmoReplenishing` | `^Soldier` (`infantry.yaml:219-224`) | always while replenishing | `Bottom`, `0,-8` | `pip-ammo-replenishing` |
| 20 | **Concealment range circles** | `WithRangeCircle@Detectable1..10` | `^DetectableRangeCircles` → infantry (`:22`), **vehicles** (`vehicles.yaml:17`) | **selected only** (`Visible: WhenSelected`) | ground circle centred on the unit | grey `888888`, `Alpha: 25`, `Width: 3`, `Type: concealment` (grouped rendering). §4 |
| 21 | Weapon range circles | `RenderRangeCircle` / `WithRangeCircle` | named aircraft, defences, SR | selected / shift | ground circle | per type |
| 22 | Target line | `DrawLineToTarget` | `^Selectable` (`defaults.yaml:1056`) | selected, on order | unit → target | line |
| 23 | Defense pips | `WithDecoration@Defense_1..5` | **nothing** | **never** | `Top`, `0,0` | dead — see §0 |
| 24 | X-ray occlusion ghost | `RenderSprites.XRayOverlayAlpha: 0.5` | infantry `:9`, vehicles `:21` | when occluded | on the sprite itself | alpha ghost — **the one existing precedent for modifying the unit sprite rather than adding a mark** |

### Z-order

There is **no z-ordering control on decorations.** `DrawDecorations` yields, in this fixed order
(`SelectionDecorationsBase.cs:109-132`): selection box → selection bars → dev-mode path line → decorations. The
decorations themselves are iterated in `self.TraitsImplementing<IDecoration>()` order, captured once at
`Created` (`:42-43`). Every decoration renderable reports `ZOffset = 0` / `IsDecoration = true`, and
`UITextRenderable.WithZOffset` is a no-op that discards the new offset (`UITextRenderable.cs:49`).

**Consequence: where two marks overlap, the one drawn on top is decided by trait resolution order, which nobody
authored.** I did not trace `TraitsImplementing` ordering to a deterministic YAML rule — *that is a gap, not a finding.*

---

## 2. How the anchor maths works (needed to read §5)

From `SelectionDecorations.GetDecorationPosition` (`:35-48`) and `GetDecorationMargin` (`:53-65`):

- `Position: Top` origin = **(horizontal centre of bounds, top of bounds)**.
- **`Margin.X` is NEGATED for `Top`** (`:61`). So `Margin: -8,-10` draws **8px to the RIGHT**. This sign trap put the
  diamond 8px off-centre in the shipped build until `96612c37` fixed it; `DecorationRowGeometry.cs:26-31` exists
  specifically to stop it recurring.
- `Margin.Y` is **not** negated and screen Y grows downward, so **negative Y moves a mark UP**.
- Sprites centre on the origin: `screenPos - size/2` (`WithDecoration.cs:106`).
- **Glyphs do not centre the way sprites do.** `SpriteFont.Measure` returns `rows * size` regardless of the character,
  and `DrawText` puts the baseline a further `size` below — so a baseline-sitting glyph's ink hangs
  **`GlyphFontSize / 2` = 5px below** its nominal origin (`DecorationRowGeometry.cs:48-54`).

---

## 3. The detectability indicator, in full

### 3.1 What it encodes

Read from `DetectabilityGrade.cs` and `WithSpottedDecoration.cs`:

1. `Detectable.CurrentVisibility` is the unit's **concealment** — the observer strength required to reveal it.
   Recomputed each tick from `^Vehicle`/`^Infantry`'s base `Vision` plus every `DetectableAddativeModifier`, then
   clamped to `[1, VisionLayers-2] = [1, 9]` (`Detectable.cs:118-131`; `MapLayers.VisionLayers = 11` at `MapLayers.cs:75`).
2. **The readout inverts it.** `Exposure = MinimumConcealment + MaximumConcealment − clamped = 10 − concealment`
   (`DetectabilityGrade.cs:64-71`). So **high concealment = low exposure = calm green**.
3. Bands are cut on *exposure* against three ceilings (`:78-96`), with `spotted` overriding everything (`:81-82`).
4. Fill: solid at `>= SolidFromGrade`, hollow below (`WithSpottedDecoration.cs:165`).

**Only the top band is enemy-derived.** Bands 0–3 read the unit's own posture and use no information about where
enemies are — that is what preserves the anti-wallhack rule (`WithSpottedDecoration.cs:29-36`). `Spotted` requires an
enemy that (a) we can ourselves see, (b) whose vision band both reaches us and carries enough strength, and (c) passes
the authoritative `CanBeViewedByPlayer` truth gate, checked last (`:208-246`).

### 3.2 The state→appearance table

Shipped values from `^UnitIndicators` (`defaults.yaml:928-964`), which repeats the C# defaults; YAML wins.
Ceilings: `Concealed ≤3`, `Low ≤5`, `Moderate ≤7`, `SolidFromGrade: Moderate`, `MinimumDrawnGrade: Concealed`.

| Grade | Exposure | ⇒ `CurrentVisibility` | Glyph | Fill | Colour | Meaning |
|---|---|---|---|---|---|---|
| Concealed | 1–3 | **7, 8, 9** | `◊` U+25CA | hollow | `6E9E76` desaturated green | well hidden |
| Low | 4–5 | **5, 6** | `◊` U+25CA | hollow | `ECC73C` yellow | becoming visible |
| Moderate | 6–7 | **3, 4** | `♦` U+2666 | **solid** | `F0B232` amber | exposed |
| High | 8–9 | **1, 2** | `♦` U+2666 | **solid** | `F09425` orange | very exposed |
| Spotted | *(override)* | any | `♦` U+2666 | **solid** | `FF4A3C` red | a known enemy can see it |

All five grades are **DERIVED** from the ceilings; the exposure↔concealment mapping is `10 − concealment`.

### 3.3 The glyph constraint is real and verified

`U+25C6`/`U+25C7` — the obvious diamond pair — **are absent from `FreeSansBold.ttf`**, which ships no Geometric Shapes
block at all; they would render as nothing, silently (`WithSpottedDecoration.cs:84-91`). `U+25CA` and `U+2666` are the
only hollow/solid diamond pair the font carries. This was proved by embedding the font in
`WORKSPACE/mockups/diamond-pip-font-proof.html` (§8). **Any redesign that changes the glyph must re-run that proof.**

### 3.4 The finding that most likely explains "it doesn't feel intuitive"

**Per-chassis reachable range. DERIVED** from the base `Vision` and the modifier set at this ref.

| Chassis | Base `Vision` | Modifiers | Reachable concealment | Grades actually reachable |
|---|---|---|---|---|
| **Infantry** | **3** (`infantry.yaml:98`) | cover +1/+2/+3 (`:780-788`), prone +1 (`:789-791`), dug-in +1 (`:792-794`), firing −2 (`:800-802`), moving −1 (`:803-805`), rank +1..+4 (`defaults.yaml:300-311`) | **1 … 9** (clamped) | **all five** |
| **Vehicles** | **2** (engine default `Detectable.cs:25`; `vehicles.yaml:71` is bare) | stationary +1 (`:92-94`), firing −1 (`:95-97`) | **1, 2, 3** | **Moderate and High only — and always SOLID** |
| **Aircraft** | **2** (`aircraft.yaml:66`, no `Vision:`) | on-ground +3 (`:70-72`) | **2** airborne, **5** landed | **High (solid)** airborne, **Low (hollow)** landed |

**So on every vehicle in the game the diamond is permanently a solid diamond that shifts only between `F0B232` and
`F09425` — two ambers roughly 14 units apart in one channel.** The hollow/solid step, which the code calls *"the coarse
channel — it survives low zoom and colour blindness, where the colour ramp alone does not"*
(`WithSpottedDecoration.cs:80-81`), **never fires on a vehicle at all.**

This lines up exactly with the user's *"when the diamond shaped indicator is hollow it is okay"*: on vehicles it never
is, and on infantry it is hollow only when stationary-and-dug-in or in cover. **DERIVED for infantry:** a plain rifleman
standing still is `3 + prone(1, granted on `!moving`, `infantry.yaml:317`) = 4` → Moderate → **solid**; he only goes
hollow after `dugin` lands at `TimeToBeStill: 200` (`:141-143`), reaching 5 → Low.

**This was known and shipped anyway.** `WORKSPACE/diamond-pip-design-260903.md:71-88`: *"vehicles get almost nothing
from this … a vehicle is **Moderate when stopped and High otherwise, and never anything else**"*, shipped globally
because *"A wrong scoping edit costs a launch; a too-busy screen costs one line."*

**Second intuition hazard: the scale is inverted relative to the number.** The grade runs on *exposure*; the underlying
`[Sync]` field is *concealment*; they run opposite ways. A green diamond corresponds to a **high** `CurrentVisibility`.
`DetectabilityGrade.cs:43-46` warns about precisely this. Nothing on screen discloses which posture term moved the grade.

---

## 4. Is there a rung that can never appear?

**Yes — but in the range circles, not in the diamond.** Established from code, not from the two notes agreeing:

1. `MapLayers.VisionLayers = 11` — READ, `MapLayers.cs:75`.
2. `ClampConcealment` ceilings at `VisionLayers - 2` = **9** — READ, `Detectable.cs:118-125`.
3. `CurrentVisibility = ClampConcealment(...)` every tick — READ, `Detectable.cs:129-131`.
4. The condition granted is `"visibility-" + CurrentVisibility` — READ, `Detectable.cs:228`.
5. ⇒ **only `visibility-1` … `visibility-9` can ever be granted.** DERIVED from 1–4.
6. `WithRangeCircle@Detectable10` requires `visibility-10` — READ, `infantry.yaml:923-924`.
7. ⇒ **the tier-10 / 4c0 circle can never render.** DERIVED.

`DetectableInfo.VisionDetectableConditions` declares `1..VisionLayers-1` = 1..10 (`Detectable.cs:53-54`) — a deliberate
superset so the ring survives a revert of the ceiling (`:50-51`). **This is intentional and must not be "cleaned up"**
(`PIPELINE.md:609`). `infantry.yaml:814-819` already says so in-tree.

**Additionally, rungs 1 and 2 are visually identical:** both `Range: 28c0` (`infantry.yaml:835`, `:845`), because band 1
can never reveal — `IsDetected` floors the threshold at 2 (`MapLayers.cs:600-603`, verified: `resolvedVisibility >=
(concealment < 2 ? 2 : concealment)`). So **10 authored rungs present at most 8 distinct radii, one of which is
unreachable.**

**The diamond itself has no unreachable grade on infantry** — all five are reachable. The unreachable rung is a
different indicator. But note the two are stacked on the same concept and disagree in resolution: 5 diamond grades
against 10 circle rungs against 9 real concealment levels, none of which map onto each other cleanly. **Inference, not
a read:** that mismatch is at least as plausible a source of "the granularity doesn't feel intuitive" as any single
rendering defect.

---

## 5. Contention map — what collides with what

Anchors converted to signed screen offsets from the `Top` origin (centre-x, top-y); **+X = right, −Y = up**.

### Vehicle / aircraft stack (`Position: Top`)

```
  y = -10   [cargo pips row]   ◊ diamond (x 0)   A (x +8)   > (x +16)
  y =  -5   (diamond glyph ink actually reaches down to here)
  y =  -3   suppression pip            [selected only]
  y =   0   ▬▬▬ damage bar 17x5        rank chevron (x -16)
  bottom    ammo pips, ISelectionBar stack
```

### Infantry stack

```
  y = -16   ◊ diamond          A (x +8)   > (x +16)
  y = -11   (diamond ink bottom)
  y =  -5   ● damage DOT 6x6
  y =  -3   suppression pip            [selected only]
  y =   0   rank chevron (x -10)
  y =  +6   class pictogram  AND  "selected" pip  -- exact overlap
```

**Collision 1 — the user's actual complaint, and it is a colour collision as much as a spatial one.**
On infantry the diamond sits **3px** above a **6×6 coloured dot** (`DiamondPipClearance = 3`,
`DecorationRowGeometry.cs:56-61`). The diamond glyph is ~6px. So two similarly-sized marks sit 3px apart — **and their
colour ramps are nearly the same ramp:**

| | damage pip | detectability diamond |
|---|---|---|
| yellow | `FFFF55` (`infantry.yaml:713`) | `ECC73C` Low |
| orange | `FF8A00` (`:714`) | `F09425` High |
| red | `FF0000` (`:715`) | `FF4A3C` Spotted |

**Two different quantities, 3px apart, in the same warm ramp, at similar size.** That is a precise mechanism for *"it
blends together too much with the health pip/circle"* — and the "circle" is literal: on infantry the damage pip **is**
a 6×6 dot. The only channel separating them today is the diamond's hollow state, which (§3.4) is unavailable exactly
when the unit is exposed and the damage pip is brightest.

**Collision 2 — diamond vs stance glyphs, ~2px.** They share row `y=-10` (`-16` on infantry). The in-tree comment states
the margin: *"-10 puts the diamond level with the two stance glyphs, which sit out at x +8 and +16 and so clear a centred
~6px glyph — **but only by about 2px, so widening the glyph is not free**"* (`defaults.yaml:941-943`).

**Collision 3 — diamond vs cargo pips, same origin.** `^CargoPips` is authored at `Margin: 0,-10` with `PipStride 6,6`
(`defaults.yaml:1024-1029`) — **the identical anchor the vehicle diamond uses**, centred on it and spreading sideways.
⚠️ **The comment block above `^UnitIndicators` describes a layout that is not the shipped one.** It claims
(`defaults.yaml:903-905`) *"Spotted takes y=0, the slot nearest the unit and the one clear of cargo even on a full
transport; the two stance glyphs sit side by side on the row above it."* The shipped values are diamond `0,-10` and
stance `-8,-10`/`-16,-10` — **the same row**, and `-10` is the cargo row. The comment predates `96612c37`; the code is
the truth. *I did not enumerate which actors carry both `^CargoPips` and `^UnitIndicators` — that set is a gap.*

**Collision 4 — class pictogram vs "selected" pip, exact.** Both `Position: Top, Margin: 0,6` on `^Soldier`
(`infantry.yaml:226-236`). Selecting a soldier draws `pip-selected` directly on top of the class pictogram, in
unspecified z-order (§1).

**Collision 5 — suppression vs damage.** Suppression at `0,-3` sits between the damage mark and the diamond. Its margin
was already moved once for exactly this reason: `ba2ff330` (2026-05-08) moved it `0,0 → 0,-3` because the class
pictogram occluded it.

---

## 6. Rendering budget — what the layer can and cannot do cheaply

**Can, today, with no new code:**
- **Text.** `UITextRenderable` → `font.DrawTextWithContrast` (`:57`), arbitrary RGB, automatic dark/light contrast
  outline from `ChromeMetrics`. Fonts must be declared in `mod.yaml` — `TinyBold` = `FreeSansBold.ttf` at size 10
  (`mod.yaml:315-318`), validated at load (`WithTextDecoration.cs:37-43`).
- **Arbitrary sprites,** any SHP in a declared sequence, animated (`WithDecoration` drives frames off wall-clock via
  `BlinkPhase`, `:79-92`).
- **Blink**, including condition-switched patterns and a health-ramped rate (`WithDecorationBase.cs:120-128`,
  `DecorationBlink.IntervalForHealth`).
- **Condition-switched screen offsets** — `Offsets: {condition: x,y}` (`WithDecorationBase.cs:116-118`). A mark can
  already move itself out of the way when a condition is set.
- **Ground circles** with `Alpha`, `Width`, `BorderColor`, `BorderWidth`, player colour, and grouped rendering by
  `Type` (`WithRangeCircle.cs:27-58`).
- **Alpha on the unit sprite itself** — `RenderSprites.XRayOverlayAlpha` already ships at 0.5 (`infantry.yaml:9`),
  and `WithAlphaCondition` is used for garrison ghosts (`infantry.yaml:~217`).

**Can in the renderable but is NOT plumbed through:**
- **Scale, alpha and rotation on a decoration sprite.** `UISpriteRenderable` takes all three
  (`UISpriteRenderable.cs:24-25`, `:60`), and `WithCargoPipsDecoration` uses scale **and** alpha to fade unselected
  pips (`:101-102,:124`). **`WithDecoration` passes none of them** — it constructs with defaults (`:106`). Exposing
  `Scale:`/`Alpha:` on `WithDecorationInfo` is a **small C# change**, and cargo pips are the working precedent.

**Cannot, or not cheaply:**
- **No z-order.** See §1. Overlap order is not authorable.
- **No alpha on the text path.** `UITextRenderable` takes a `Color` and no alpha parameter. Whether `SpriteFont`
  honours the colour's own alpha byte — **I did not determine this.** It needs a read of `SpriteFont.DrawTextWithContrast`.
- **No per-decoration scaling with zoom.** Marks are UI-space and do not shrink as you zoom out. Note
  `Viewport.cs:69 unlockMinZoom = true` means players routinely play below `MinZoom`
  (`SelectionDecorationsBase.cs:125-128`), so marks get *relatively larger* as the view widens.

**Palette constraints.** Decoration sprites default to the `chrome` palette (`WithDecoration.cs:32`); rank uses
`effect`; both are `PaletteFromFile` (`palettes.yaml:58-59`, `:68-69`). `IsPlayerPalette` gives team colour.
Per `architecture.md:283`, **indexed PNGs carry their own baked palette and ignore remap**, so team-coloured marks
need the SHP round-trip. Text is not palettised at all — it is direct RGB, which is why the diamond and the stance
glyphs can carry arbitrary colours that no `.shp` pip can.

**Existing decoration art** — `mods/ww3mod/bits/units/pips/`: `pip-ammo`, `pip-ammored`, `pip-damage-infantry`,
`pip-damage-vehicle`, `pip-defense`, `pip-disguised`, `pip-hazmat`, `pip-heal`, `pip-morale-boost`, `pip-numbers`,
`pip-seal`, `pip-selected`, `pip-skull`, `pip-suppression`, **`pip-visibility`**, `pip-visibility_old`, `pips2`,
`rank`, `rank_original`. Declared under `pips:` (`sequences-misc.yaml:233`) and `rank:` (`:571`); infantry-specific
`class:` / `selected_infantry:` at `sequences-infantry.yaml:61-67`.

⚠️ **There is an unused 12-step gradient sprite set already in the tree.** `pip-visibility.shp` with sequences
`pip-visibility-1..12` declared at `sequences-misc.yaml:304-337` — **and no rule references them.** This is the
abandoned `^VisibilityPips` gradient row, whose *rules* were deleted in `f5634522` (*"the 'how badly am I seen'
indicator the binary steer explicitly rejected"*, 85 lines) while the **art and sequences survived**. If the design
conversation wants a graduated visibility readout, a 12-frame sprite ladder for exactly that already exists and costs
no new art.

---

## 7. What the code makes easy vs hard

*Affordances only. No recommendation — the design call belongs to the manager and the user.*

| Direction | Cost | Why |
|---|---|---|
| **Retune the diamond** (thresholds, 5 colours, glyphs, density, off) | **Free — YAML only, no rebuild** | Every knob is an `Info` field mirrored in `defaults.yaml:948-964` at its C# default. `Graded: false` restores the old binary `!` in one line. `MinimumDrawnGrade: Moderate/Spotted` thins the screen instantly. |
| **Move any mark to a different anchor** | **Free — YAML** | 6 anchors × signed margin. Mind the X-negation (§2) and re-derive through `DecorationRowGeometry`, which owns the stacking arithmetic. |
| **Show a mark only when selected / on hover** | **Free — YAML** | `RequiresSelection: true`. Rollover already forces `displayHealth/displayExtra` (`SelectionDecorationsBase.cs:104-107`), but **that path drives bars, not `IDecoration`s** — hover-only *decorations* would need a small change. |
| **Replace shape-coding with colour-coding (or vice versa)** | **Free for the glyph marks** | They are text: colour is arbitrary RGB per frame, and the glyph is a string field. Only constraint: **the character must exist in `FreeSansBold.ttf`** (§3.3) — re-run the font proof. |
| **Consolidate several marks into one glyph** | **Small C# change** | A new `WithDecorationBase` subclass reading 2–3 traits and emitting one renderable. `WithSpottedDecoration` extending `WithTextDecoration` is the working pattern; `pip-explorer` already recommended one character as the cheapest second channel. |
| **Fade / shrink an unselected mark** | **Small C# change** | Capability exists in `UISpriteRenderable` and is used by cargo pips; just not exposed on `WithDecorationInfo` (§6). |
| **Tint or outline the unit sprite itself** | **Medium — but precedented** | `XRayOverlayAlpha` and `WithAlphaCondition` already modify the sprite. A *coloured outline* is new work: no outline shader path was found in this audit. **Gap — I did not audit the sprite shader.** |
| **Control which mark draws on top** | **Needs new rendering work** | No z-order exists on decorations at all (§1). |
| **A bar that fills, instead of 10 pip colours** | **Medium** | `ISelectionBar` gives a real filling bar for free — but it renders at the **bottom** edge and only when selected/rollover/`StatusBars != Standard`, and `StatusBars` defaults to `Standard` (`Settings.cs:285`). A top-anchored filling bar is new rendering. |
| **A 12-step graduated sprite readout** | **Free-ish — art already exists** | `pip-visibility.shp` + 12 sequences are in the tree and unreferenced (§6). |
| **Make the diamond meaningful on vehicles** | **NOT a display change** | It needs more `DetectableAddativeModifier`s on `^Vehicle` — i.e. a *mechanics* change, inside the user-gated block (§9). |

---

## 8. Prior art: the committed mockups

All five are committed under `WORKSPACE/mockups/`. I read each file's structure and its commit message in full; where I
say what a mockup *concluded*, that is from the commit message, which is detailed. **I did not read every caption inside
the HTML** — the files are inline-SVG-heavy and one is 384 KB.

| Mockup | Commit | Question it was built to answer | What it concluded | Still true? |
|---|---|---|---|---|
| `pip-states.html` | `0df3d671` 2026-09-02 | *"Detectability pip — state schematic"*: what states must the mark distinguish, drawn at diagram scale | Colour goes **wholly** to the detectability continuum; spotted-vs-vetoed is **a contradiction, not a collision** (being seen is the veto's exit condition, `AutoTarget.cs:774`), so precedence comparison dropped; the veto gets **one cheap second channel — a hollow glyph** ("recommended over ring and split-top") | **Standing, and largely implemented** — the shipped graded diamond (`8308aa83`, next day but one) is this recommendation. Its two "genuine coexistence" panels remain unaddressed, and it states the terminal `ambushTriggered` latch case is **unfixable by any visual scheme** |
| `pip-explorer.html` | `0df3d671` 2026-09-02 | Interactive sibling of the above — sliders over the same state space, with an artifact submit channel | Same conclusions; adds that **the orange "vetoed" state already ships, ungated** (`AutoTarget.cs:765-781`), so it is *"a readout for a live state, not a new one"* | Standing |
| `diamond-pip-font-proof.html` | `8308aa83` 2026-09-03 | Does the shipped font actually contain the diamond glyphs? (384 KB — it **embeds `FreeSansBold.ttf` as base64** to render the proof) | `U+25C6`/`U+25C7` **absent**; `U+25CA`/`U+2666` present with real outlines | **Still true** — the font has not changed. A *proof artifact*, not a design option. Re-run it before changing any glyph |
| `hunker-readout-static.html` | `cd23ea2e` 2026-09-02 | How should suppression be drawn? Five states at **real 1× size with 4× insets**, the full 11-row band ladder with a derived shots-per-minute column, and **three readout treatments side by side** | Comparison sheet — presents alternatives rather than picking one | **Standing and unconsumed.** Nothing in rules changed after it |
| `hunker-explorer.html` | `cd23ea2e` 2026-09-02 | Interactive two-axis explorer (suppression × hunker depth), cost/benefit curve, two open design questions wired to the artifact channel | Two premise corrections, **both verified true at this ref**: prone **does** carry a protection term — `HitShape@Cover` shrinks the hit circle 30→20 on prone (`infantry.yaml:144-151` ✓, a 56% smaller target); and a passive hunker already ships — `ConditionWhenStill: dugin`, `TimeToBeStill: 200` (`infantry.yaml:141-143` ✓). Also noted the speed ladder is gated `!panicking` but the vision/burst/inaccuracy ladders are not, so *"suppressed" is not one coherent state in the rules* | Standing; **its two open questions were never answered** |

**Best single artifact to put in front of the user: `pip-states.html`.** It is the state schematic of the exact mark the
user is complaining about, drawn at diagram scale and screenshot-verified across seven rounds, and it records *why* the
shipped diamond looks the way it does. That makes it the right anchor for "here is what we chose and the reasoning —
what do you want different", and it pre-empts re-proposing the ring and split-top options it already rejected.
**Second, if the conversation turns to suppression: `hunker-readout-static.html`** — the only artifact in the repo that
shows marks at real 1× size beside magnified insets, which is precisely the "is this too prominent / can you even read
it" question.

---

## 9. The gate, and the mistake it exists to prevent

### 9.1 What manager decision 08 actually records

`.maestro/managers/manager-850d8885-.../decisions/08-legibility-was-substituted-for-a-behaviour-the-u.md`, recorded
2026-08-19. The user said, in substance, *"it is really hard to stay hidden, and in ambush stance that should take care
of itself."* The agent researched it, correctly established that **no stance touches detectability** and that Ambush
actually disables the only automatic take-cover — and then **shipped the concealment gauge**, the grey ring showing how
far away a soldier can be seen. The decision's own words: *"That work is good and correct. **It is not what was asked
for.**"* … *"The user's line was a statement of DESIRED behaviour. It was processed as a claim about CURRENT behaviour,
refuted, and answered with visibility into the absence."* It records the same substitution happening **three times**,
including — directly relevant here — *"A white '!' for a soldier held up because he is hiding. → reported 'the state it
would report does not exist'. **That is the reason to BUILD the state, not to drop the indicator.**"* It explicitly
absolves the "legibility first" ruling: *"That ruling was about ordering … not about replacing a requested mechanic with
a readout of its absence. **The agent read a sequencing rule as a scope rule.**"* The rule to carry: *"When the user
describes behaviour the game does not have, that is a feature request, not a misconception to correct."*

### 9.2 Does the gate bind display work? — verbatim, then my reading

`PIPELINE.md:580`:
> **Nothing on stances, ambush, concealment or cover may be implemented until the user says so.** Verbatim: *"I will let
> you know when we are ready to implement, until then just ask me"* and *"it is my wish that you really get to the bottom
> of this before we start implementing."*

`PIPELINE.md:587`:
> **Deliberately NOT queued: a legibility / readout item.** That is precisely the mistake manager decision 08 records —
> shipping a readout *around* an absent behaviour and reporting it as the answer. Legibility is a sequencing rule, not a
> scope rule: it follows a behaviour fix, it does not substitute for one.

`bugs/discovered.md:271-272`:
> **None of these are fixed, and none may be fixed without the user's say-so** — their standing instruction is that
> nothing on stances, ambush, concealment or cover is implemented until they say.

**It is genuinely ambiguous for display-only work, and I will not pretend otherwise.** The gate's literal subject is
*"stances, ambush, concealment or cover"* — mechanics topics; it never says "the display of". But `:587` then refuses a
*readout* item in this subject area on a sequencing rule. Read together:

- The gate **does not forbid** redrawing a mark whose mechanic already works. Decision 08's error was *substitution* —
  answering a request for behaviour with a readout. **This request is not that**: the user is asking to redesign a
  readout they find messy, and is themselves the holder of the gate, so it can be lifted in the very conversation.
- The gate **does bite** the moment the conversation converts a §0 "DISAGREE" row into a drawing task. Specifically:
  *the Fire-axis stance glyph, and anything that would make the vehicle diamond meaningful.* Redrawing the `A` because
  it implies concealment it does not confer would be decision 08 repeated exactly — same subject, same user, same
  substitution.

**The one test that resolves any case:** *is the mechanic behind this mark working?* If yes (diamond, suppression,
damage, rank, holding-fire), redrawing it is legibility work on a live feature and the gate is not engaged. If no
(stance implications, vehicle detectability range, defense pips), redrawing it is a readout around an absence, and
that is what both `:587` and decision 08 forbid.

---

## 10. Live bug entries bearing on what is drawn

Separated as asked: **display** problems vs **mechanic** problems.

### About the DISPLAY

- **`discovered.md:120` — "gauge draws nothing at maximum concealment (2026-08-20) | FIXED — `WithRangeCircle@Detectable10`".**
  ⚠️ **Cross-checked against code: the fix is present but the tier it added is unreachable** (§4). The `@Detectable10`
  ring exists and can never render, deliberately, so the ladder is complete on paper and 9-of-10 in play. The entry is
  not wrong, but read alone it implies a working tier 10.
- **`discovered.md:1008-1032` — OPEN.** `test-visual-gauge-truth` and `test-visual-concealment-gauge` are **calibrated
  one tier low**: both omit `prone`, which is granted on `!moving` (`infantry.yaml:317`), so a stationary soldier is
  prone from spawn. Correct tiers are stopped 4 / dug-in 5, not 3 / 4. **Neither scenario has ever been run.** These are
  the two capture scenarios whose screenshots would be the evidence for gauge correctness — *so the existing visual
  evidence base for this indicator is both unrun and miscalibrated.*
- **`discovered.md:984-1007` — RESOLVED.** `WithSpottedDecoration.VisionCovers` accepting `Strength == required` is now
  correct, because reveal became non-strict. *"Do not 'fix' it back to `<=`."*
- **`architecture.md:491`** — the suppression pip is *"ten colours of ONE glyph, not a bar that fills"* and *"at 6×3 px
  adjacent tiers are not separable on screen."*

### About the MECHANIC the marks display

- **`discovered.md:1033-1049` — OPEN.** `stance-ambush` / `stance-holdfire` granted in five places, **consumed by
  nothing**; *"**No stance modifies detectability** — every `DetectableAddativeModifier` keys on `object-proximity`,
  `prone`, `dugin`, `firinganyweapon`, `moving`, `rank-veteran` or `!airborne`."* **Important non-defect it flags:** the
  Ambush *stance* is consumed heavily elsewhere — *"Do not delete the stance on the strength of the dead tokens."*
  **Note the glyph does not read these tokens** (`WithStanceDecoration.cs:108` reads the trait), so this is not why the
  glyph looks wrong — it is why the glyph's *implication* is wrong.
- **`discovered.md:1055-1069` — OPEN.** Vehicles carry a bare `Detectable:` while the Ambush button and its
  concealment-flavoured tooltip (`ingame-player.yaml:373`) remain on offer. ⚠️ **Partially superseded at this ref:**
  `vehicles.yaml:92-97` now *does* carry `@Stationary +1` and `@Firing −1`. The entry's "**zero**" is stale; its
  *substance* — that the implied concealment vastly exceeds what a vehicle's model can do — survives, and §3.4 quantifies
  it as a 3-value range.
- **`PIPELINE.md:618` (item 68) — OPEN, headline defect.** A human clicking Ambush gets plain hold-fire.
- **`discovered.md:571-622` — filed [high] OPEN, but its own TRIAGE block says PREMISE FALSE.** The `object-proximity`
  cover ladder is **reachable now**: `^TreeCover` emits from **living** trees at `Range: 1024`
  (`decoration.yaml:56-60`, verified at this ref), inherited by `^Tree`. This matters to the diamond — cover `+1/+2/+3`
  is the largest single lever into infantry concealment, and it works.
- **`discovered.md:657-703` — OPEN.** Two `dugin` timer bugs, and the −2 firing penalty applies to the `primary`
  armament only. Both are *inputs to the diamond*: they mean the grade can be wrong for reasons invisible on screen.

---

## 11. Honest gaps

1. **Nothing has been observed on screen.** Every layout claim is arithmetic over margins and sprite sizes.
2. **Z-order between overlapping marks is undetermined** — `TraitsImplementing<IDecoration>()` order was not traced.
3. **Whether `SpriteFont` honours a colour's alpha byte** was not established; it bounds any "fade the glyph" option.
4. **No outline/tint shader path was audited**, so "outline the unit sprite" is costed as *unknown-medium*, not medium.
5. **I did not enumerate which actors carry both `^CargoPips` and `^UnitIndicators`**, so Collision 3's blast radius is
   unquantified.
6. **Mockup conclusions come from commit messages and file structure**, not from reading every caption in the HTML.
7. `pip-suppression.shp`'s actual frame count was not decoded; the 10 sequences index `Start: 0..9`.

### The one capture that would settle the most

**I am not permitted to launch anything, so this is a request, not a plan.** One screenshot, at default zoom **and** at
a zoomed-out level, of a mixed group: (a) an undamaged stationary infantry squad, (b) the same squad at ~40% health, (c)
a stationary vehicle, (d) a moving vehicle, with one unit of each in Ambush and one in Hunt, and one unit selected.

It would answer, in a single frame, the four things arithmetic cannot: whether the 3px diamond↔damage-pip gap reads as
two marks or one; whether `ECC73C`/`F0B232`/`F09425` are separable from each other and from the damage ramp at 10px;
whether the 2px diamond↔stance-glyph clearance holds; and whether the vehicle diamond's permanent solid amber reads as
information or as noise. ⚠️ Note `test-visual-gauge-truth` and `test-visual-concealment-gauge` **should not** be used
for this without fixing their calibration first (§10).

---

## Appendix A — git history: when each mark landed, and what for

*Researched across the full history of each file. Verdicts distinguish debug aid from designed feature.*

**Detectability mark** — `f5634522` (2026-08-17) *"indicators: a binary spotted mark and a non-default-stance glyph,
both render-only"*; `8308aa83` (2026-09-03) *"Grade the visibility mark: a diamond that says how exposed a unit is"*;
`96612c37` (2026-09-03) *"Centre the visibility diamond and lift it clear of the damage pip"*; `e1553305` (2026-09-06,
doc-comment lint only). `Graded`, `DetectabilityGrade.cs` and `DetectabilityGradeTest.cs` all land in `8308aa83`;
`DecorationRowGeometry.cs` in `96612c37`.
**Verdict: designed, user-requested shipped feature.** *"The request was for 'how visible they are', which a boolean
cannot carry."* `Graded: false` is a tuning/reversal switch, not a debug flag. Both commits state they were never
launched or screenshotted.

**Stance glyphs** — `f5634522` (2026-08-17, the `.cs`); `6cb66e28` (2026-08-17, re-margined 13/21 → 8/16 *"tucks the
mark beside the 6px damage pip"*); `231dd0f7` (2026-08-17, scenario crash fixes only). **Nothing since 2026-08-17.**
**Verdict: designed shipped feature** — same commit and rationale as the diamond: *"Only NON-DEFAULT stances draw. A
glyph on every unit in FireAtWill/Defensive is a glyph on every unit on the map."*
⚠️ **This contradicts the brief's premise that the stance marks were "only meant as a temporary way of seeing better
what stance they are in".** No commit describes them as temporary; the trait `[Desc]` argues them as shipped design and
marks them RENDER-ONLY. *What is throwaway is not the implementation — it is render-only, trait-driven, and reusable.
What is questionable is the **vocabulary**: four Latin letters (`X`/`A`/`H`/`>`) chosen because "four similar chevrons
cannot be told apart" (`WithStanceDecoration.cs:29-31`).* Anything built on this trait keeps working if the glyphs
change; they are `Info` string fields.

**Suppression pip** — `1f4fff51` (2024-04-29, *"WIP: pips etc"*, sequences **and** the `WithDecoration` blocks);
`d9b7ae36` (2024-05-07); `6f9aa08d`; `05fb91fb`; **`ba2ff330` (2026-05-08) — last touch to the blocks**,
*"fix(ui): show suppression pip on regular infantry, not just crew"*; `97414046` (2026-08-17, extended the same ten
sequences to a garrison row).
**Verdict: designed shipped feature, original 2024 work, still live and still maintained.** **No commit disables,
narrows or abandons it** — both directional commits move the other way. **This settles the user's uncertainty: it is not
vestigial and not a deliberate subset of units.** It is on *both* infantry (`infantry.yaml:423`) and vehicles
(`vehicles.yaml:367`), and the reason it seems to appear on only some units is `RequiresSelection: true` — **you see it
on the units you have selected.** Its documented weakness is resolution, never wiring.

**`^UnitIndicators` block** — created in `f5634522`, re-margined `6cb66e28`, knobs added `8308aa83`, margin corrected
`96612c37`. The same commit **deleted the abandoned `^VisibilityPips` gradient row** (85 lines) — *"the 'how badly am I
seen' indicator the binary steer explicitly rejected"*. ⚠️ **Its art and sequences were left behind** (§6).

**Health bar** — ⚠️ **the in-source attribution is wrong.** `SelectionBarsAnnotationRenderable.cs:168` credits
`e670ab96` (2024-08-13), but that commit only *reformatted* an already-commented call
(`// if (DisplayHealth)\n// DrawHealthBar(...)` → one line). The actual comment-out is **`1f4fff51`, 2024-04-29,
*"WIP: pips etc"***, proved three ways: live at `7362fbc6`, commented at `1f4fff51`, and
`git log 7362fbc6..1f4fff51 -- <file>` returns `1f4fff51` alone. **The attributed SHA and date are off by one commit and
~3.5 months; the mechanism and the conclusion are right.** Complication: `c05bd4c0` (2026-03-24) later rewrote
`GetHealthColor` into a full gradient — work on an unreachable chain, which `ffef94a6` (2026-08-23) documented as
*"the second time in 2026 that real effort went into that chain believing it renders."*

**Damage pips** — upstream 2023 → `1f4fff51` (2024-04-29) → `c00db66c`/`fbbcbe93` (2026-05-03, garrison variants) →
**`3543ec54` (2026-08-23) last behavioural touch**: *"the pulse means critical, and critical is 50%"*, which drops the
orange rung and makes the ladder three rungs, not four.
**Verdict: designed shipped feature, and load-bearing** — with the health bar dead, this is the only health indicator,
and it is not selection-gated.

**Rank chevrons** — `46698502` (2023-04-04) → `1f4fff51` (2024-04-29). Later same-region commits touch neighbouring
`@Rank_*` modifier traits, not the chevrons.
**Verdict: designed shipped feature**, inherited from the OpenRA veterancy idiom and kept. The 2023–24 commit messages
state no intent at all, so **that verdict is inference from the code being live and wired, not a quote.**

**Cross-cutting:** every mark still on screen is a designed feature. The one thing switched off — the health bar — was
switched off in a commit whose message says only *"WIP: pips etc"*.

## Appendix B — `WORKSPACE/diamond-pip-design-260903.md` (172 lines)

The shipped diamond's design record, and **it is the fastest route into a redesign conversation** because it lists eight
numbered design calls each with the single line that undoes it (`:52-63`):

1. graded at all → `Graded: false` (*"One line, no other edit anywhere"*)
2. draw on everything → `MinimumDrawnGrade: Spotted` — **flagged by its own author as *"the call most likely to be wrong"***
3. glyph choice → `HollowText`/`SolidText` (*"Do not 'fix' these to U+25C6/U+25C7"*)
4. `SolidFromGrade` · 5. the three ceilings · 6. the five colours · 7. trait name (nothing to reverse)
8. margin — *advice overtaken the next commit* by `96612c37`, which found `Position: Top` negates `Margin.X`

Also: `:3-4` *"**Not launched, not screenshotted, not YAML-linted** — those were withheld from this worker."*
`:71-88` the vehicle weakness, self-flagged and shipped anyway. `:141-150` why there is no autotest — *"a scenario
written for this would go green whether the feature worked or not"*; 18 NUnit cases instead. `:153-171` an observation
recipe naming the two things to distrust first: **10px legibility on snow/sand, and green-vs-yellow separability at the
hollow end.**
