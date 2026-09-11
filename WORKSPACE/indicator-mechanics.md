# Indicator mechanics — twelve questions, answered from source

**Ref: `main @ d37367aa`**, worktree `wt/indicator-mechanics`, branched from `wt/indicator-audit` (`3ec60e54`).
**Research only.** No YAML rules edited, no engine C# edited, nothing launched, no screenshot, no `--check-yaml`, no `make test`.
Companion to [`WORKSPACE/indicator-audit.md`](indicator-audit.md), whose inventory and detectability tables are the starting
point and are not re-derived here.

**Method note.** Every claim carries a `file:line`. Where a number is computed rather than read, it says **DERIVED**. Where I
inspected a binary (the font, the pip sprites) I did so with a throwaway parser and the decoded output is reproduced, so the
reading can be checked. Where I could not settle something, it says so and Q9 specifies the capture that would.

---

## THE HEADLINE (Q1)

**Damage state does not modify suppression, in either direction, anywhere in the mod.** The pitch's central premise —
"low health also produces suppression" — describes behaviour the game does not have. **That makes the colour half of the
pitch a mechanics request, not a display change**, and it has to be built before it can be drawn.

The reverse coupling *does* exist and runs the other way: suppression is one of four independent triggers for `prone`, and
so *reduces* how visible an infantryman is.

---

## Q1 — Does damage state modify suppression at all?

**No. No such link exists.** Established three ways.

**1. Every grant site is a weapon warhead.** There are **139** `Condition: suppressed` lines under `mods/ww3mod`. Classifying
each by its owning block leaves no residue: each is either a `Warhead@…: GrantExternalCondition` in a weapons file
(`rules/weapons/weapons-effects.yaml:27-443`, `weapons-other.yaml`, `weapons-missiles.yaml`, `weapons-superweapons.yaml`,
`weapons-nuclear-arsenal.yaml`) or one of the two `ExternalCondition@` *receiver* declarations on the unit itself
(`infantry.yaml:429-433`, `vehicles.yaml:362-366`). Suppression is applied by being shot at, and by nothing else.

**2. `^DamageStates` grants only damage tokens.** The complete block is `defaults.yaml:259-283`: nine
`GrantConditionOnDamageState` entries granting `damaged`, `light-damage`, `light-damage-attained`, `medium-damage`,
`medium-damage-attained`, `heavy-damage`, `heavy-damage-attained`, `critical-damage`. None grants `suppressed`, and none
carries a modifier of any kind.

**3. No engine code grants it.** Three C# sites name the token, and all three are **readers** via `GetConditionCount`:
`StancePositioningExecutor.cs:112`, `GarrisonManager.cs:92`, `PoiOffensiveBotModule.cs:468`. Nothing writes it from C#.

The only damage-adjacent suppression edit in the tree is **commented out** and is keyed on rank, not health —
`defaults.yaml:296-299`:

```
# ConditionModifier@Rank_1:
# 	RequiresCondition: rank-veteran == 1
# 	Condition: suppressed
# 	Modifier: -1
```

Worth noting for cost: that dead block is the *shape* a health→suppression link would take (a `ConditionModifier` on the
stacked condition), so the pattern was considered once. Whether `ConditionModifier` is still a live engine trait I did not
verify — **gap**.

### The coupling that does exist, and it runs the opposite way

`infantry.yaml:317` (and `:342` for the amphibious variant):

```
ProneCondition: deployed || suppressed > 30 || !moving || critical-damage
```

Suppression above 30 makes a soldier prone; so does `critical-damage`, **independently and by a separate term of the same
disjunction**. And prone grants `DetectableAddativeModifier@Prone: VisionModifier: 1` (`infantry.yaml:789-791`) — i.e.
**+1 concealment**. So today, heavy suppression and heavy damage each make an infantryman *harder* to see, not easier, and
neither makes the other happen.

---

## Q2 — What suppression actually does today: the complete list

`suppressed` is a stacked `ExternalCondition` and the two chassis are configured differently, including the decay rate.

| | Infantry | Vehicles |
|---|---|---|
| Declared | `infantry.yaml:429-433` (`^SuppressionEffects`, inherited at `:15`) | `vehicles.yaml:362-366` (`^VehicleSuppressionEffects`, inherited at `:19`) |
| `TotalCap` | **100** | **50** |
| Decay | `ReduceTicks: 5`, `ReduceAmount: 1` → **1 per 5 ticks** | `ReduceTicks: 3` → **1 per 3 ticks (faster)** |
| Tiers | 10, each 10 points wide | 5, each 10 points wide |

**Infantry — five modifier ladders plus the pip row.** All tiers are `suppressed > N && suppressed <= N+10`.

| Tier | range | Speed `:434-464` | Vision `:465-495` | Burst `:496-526` | BurstWait `:527-557` | Inaccuracy `:558-588` |
|---|---|---|---|---|---|---|
| 1 | 1–10 | 90 | 90 | 90 | 110 | 120 |
| 2 | 11–20 | 80 | 80 | 80 | 120 | 140 |
| 3 | 21–30 | 70 | 70 | 70 | 130 | 160 |
| 4 | 31–40 | 60 | 60 | 60 | 140 | 180 |
| 5 | 41–50 | 50 | 50 | 50 | 150 | 200 |
| 6 | 51–60 | 40 | 40 | 40 | 160 | 220 |
| 7 | 61–70 | 30 | 30 | 30 | 170 | 240 |
| 8 | 71–80 | 20 | 20 | 20 | 180 | 260 |
| 9 | 81–90 | 10 | 10 | 10 | 190 | 280 |
| 10 | 91–100 | **0** | **0** | **0** | 200 | 300 |

**Only the speed ladder is gated `!panicking`** (`:436`–`:463`); the other four are not. That asymmetry is already recorded
as a design smell — the `hunker-explorer` mockup noted *"'suppressed' is not one coherent state in the rules"*
(audit §8). `panicking` itself comes from `PanicCondition: onfire && !heavy-damage-attained` (`infantry.yaml:332-333`).

Pips: `^SuppressionPips` (`infantry.yaml:589-670`), ten `WithDecoration@Suppression_N`, all `RequiresSelection: true`,
`Position: Top`, `Margin: 0,-3`.

**Vehicles — three ladders, five tiers, and two conspicuous absences** (`vehicles.yaml:369-415`):

| Tier | range | TurretTurnSpeed | Inaccuracy | BurstWait |
|---|---|---|---|---|
| 1 | 1–10 | 85 | 115 | 105 |
| 2 | 11–20 | 70 | 130 | 110 |
| 3 | 21–30 | 55 | 150 | 120 |
| 4 | 31–40 | 40 | 175 | 130 |
| 5 | 41–50 | 25 | 200 | 150 |

**Vehicles have NO speed ladder and NO vision ladder** — `^VehicleSuppressionEffects` inherits only `^SuppressionPips`
(`vehicles.yaml:367`), never `^SuppressionVisionModifier`. The in-file comment states the intent: *"No speed reduction
(armored vehicles keep moving), milder effects than infantry"* (`:359-360`).

**Four further consumers outside the ladders** (these are the ones a summary usually misses):

- **Prone trigger** at `suppressed > 30` — `infantry.yaml:317`, `:342`. See Q1.
- **`StancePositioningExecutor`** — `MaxSuppressionToMove = 30` (`:109`), read via a variable observer (`:224-231`). Gated
  off for `@stable`/`@normal` (`:247-248`), so experimental bots only.
- **`GarrisonManager`** — forced recall to shelter at `SuppressionRecallThreshold = 60` (`:100`, applied `:722-733`), plus a
  per-soldier redeploy hysteresis (`:561-563`). Its `[Desc]` at `:94-98` also carries a live PITFALL: do not add a
  garrison-side fire penalty, because `AttackGarrisoned` already fires the soldier's own armament and would double-apply.
- **`PoiOffensiveBotModule`** — suppression-coordinated advance reads the stack to release prep fire (`:465-468`,
  `PrepSuppressionRadius` `:463`); `SuppressionCoordinatedAdvance` defaults `false` (`:449`).

---

## Q3 — Does suppression reduce vision, and can it reach zero?

**Yes, and yes — genuinely zero, with no floor. On infantry only.**

The chain, all read:

1. `VisionModifier@Suppression_10: Modifier: 0` at `suppressed > 90 && suppressed <= 100` — `infantry.yaml:493-495`.
2. `VisionModifier` implements `IVisionModifier`, returning `Info.Modifier` when enabled — `VisionMultiplier.cs:24-32`.
3. `Vision` collects **every** `IVisionModifier` on the actor and applies them to both range fields —
   `Vision.cs:45`, `:58-80`: `Util.ApplyPercentageModifiers(Info.Range.Length, rangeModifiers)`.
4. `ApplyPercentageModifiers` is a bare product with **no clamp and no floor** — `Util.cs:238-246` (`a *= p / 100m`).
5. At `Modifier: 0` both `MinRange` and `Range` compute 0, and `AffectsMapLayer.ProjectedCells` **returns an empty cell
   buffer** when `maxRange <= minRange` — `AffectsMapLayer.cs:84-85`.

So all ten `^StandardVision` bands contribute zero cells and the unit sees nothing at all. **There is no floor to quote.**

Three qualifications that matter to the pitch:

- **It is not reachable on vehicles at all.** Cap 50, and no vision ladder inherited (Q2).
- **Nothing ties it to health.** Tier 10 requires `suppressed > 90`; the pitch's "dying, so no vision" is not the mechanism.
- **DERIVED:** an infantryman at 91+ suppression is blind *and* prone (`suppressed > 30`, Q1) *and* at speed 0
  (`infantry.yaml:463-464`) *unless* panicking.

---

## Q4 — The "Spotted" predicate, and how often it is false for a vehicle in the open

### The predicate

`WithSpottedDecoration.IsSpotted` (`:208-246`), recomputed every `RecalculationInterval = 7` ticks and cached
(`:50`, `:196-206`). For each actor within `MaximumObserverRange = 32c0` (`:57`), **all four** must hold:

1. Owner is not `NonCombatant` and is `Enemy` of the render player — `:226-228`.
2. **We can see the observer**: `observer.CanBeViewedByPlayer(viewer)` — `:231`. This is the anti-wallhack asymmetry.
3. `VisionCovers(observer, self, required)` — `:234`, where `required = detectable.CurrentVisibility` (`:219`).
4. **Truth gate, checked last**: `self.CanBeViewedByPlayer(owner)` — `:241`.
   `Actor.CanBeViewedByPlayer` = no `shouldHideModifier` hides us, then `defaultVisibility.IsVisible` — `Actor.cs:642-650`.

`VisionCovers` (`:262-283`) asks whether the observer has any `Vision` trait whose `Strength >= required` and whose annulus
`[MinRange, Range]` contains the distance.

### The vehicle case, concretely

`^StandardVision` (`defaults.yaml:115-154`) is carried by infantry (`infantry.yaml:19`), vehicles (`vehicles.yaml:12`),
aircraft (`aircraft.yaml:12`) and defences (`structures-defenses.yaml:5`). Its ten bands:

| Strength | 10 | 9 | 8 | 7 | 6 | 5 | 4 | 3 | 2 | 1 |
|---|---|---|---|---|---|---|---|---|---|---|
| outer range | 4c0 | 7c0 | 10c0 | 13c0 | 16c0 | 19c0 | 22c0 | 25c0 | 28c0 | 32c0 |

A vehicle's concealment is base `Vision = 2` (`Detectable.cs:25`) plus `@Stationary +1` / `@Firing −1`
(`vehicles.yaml:92-97`), clamped to `[1,9]` (`Detectable.cs:118-125`) — so **1, 2 or 3**, exactly as the audit found.

**DERIVED.** For a *stationary* vehicle, `required = 3`, and the bands with `Strength >= 3` tile the annuli contiguously from
0 out to **25 cells**. So:

> **A stationary vehicle in open ground with an enemy at medium range IS detected** — condition 3 is satisfied by any
> standard-vision enemy inside 25 cells, and "medium range" is comfortably inside that. Moving (`required = 2`) extends the
> qualifying radius to 28 cells; the worst case (`required = 1`) reaches 32.

**What would have to be true for it not to be detected**, in decreasing order of realism:

1. **We have not spotted the observer** (condition 2 fails) — the enemy sees us from behind our own fog. This is the
   deliberate asymmetry (`WithSpottedDecoration.cs:33-36`) and is the *realistic* false case.
2. No enemy within 25/28/32 cells at all (condition 1).
3. The observer's shroud does not actually reach us despite the band arithmetic — terrain shadow or height delta — caught by
   the truth gate at `:241`. `VisionCovers`' own comment concedes it is optimistic here and that the truth gate exists to
   stop that becoming a false positive (`:256-261`).

**Inference, flagged as such:** since conditions 3 and 4 are near-automatic for a vehicle in the open in contact, the
"detected" bit on a vehicle is essentially *"is there an enemy nearby that I can also see"*. In a firefight that is true
almost always. I am not drawing the design conclusion — that is the agent's and the user's — but the mechanical frequency
is: **near-always true whenever mutual visibility holds inside 25 cells.**

---

## Q5 — `critical-damage` versus `DamageState.Heavy`: both thresholds

Engine thresholds, `Health.cs:97-115`, in evaluation order:

| DamageState | Condition | Band |
|---|---|---|
| `Undamaged` | `HP == MaxHP` | 100% |
| `Dead` | `HP <= 0` | 0 |
| `Critical` | `HP * 100L < MaxHP * 25L` | **under 25%** |
| `Heavy` | `HP * 100L < MaxHP * 50L` | **25% – under 50%** |
| `Medium` | `HP * 100L < MaxHP * 75L` | 50% – under 75% |
| `Light` | (fallthrough) | 75% – under 100% |

Token mapping, `defaults.yaml:259-283`:

| Token | `ValidDamageStates` | Health band |
|---|---|---|
| `critical-damage` | `Critical` | **under 25%** |
| `heavy-damage` | `Heavy` alone | 25% – under 50% |
| `heavy-damage-attained` | `Heavy, Critical` | **under 50%** |

**So the two phrases in the pitch resolve as follows.** "Half health" = the `Heavy` onset = the `heavy-damage-attained`
token = **under 50%**. The project's vocabulary ruling — the user's "goes critical" means `DamageState.Heavy` — points at
**the same 50% boundary**. The `critical-damage` *token* is a different, lower band at **under 25%**.

**⇒ The design has a state it cannot show, if both phrases are taken in the user's vocabulary.** "Half health → constant
suppression → yellow/orange" and "critically damaged → blink" would fire at the identical threshold, so the blink would
start exactly where the colour step starts and nothing would distinguish them. If instead the blink is meant at the
`critical-damage` token (<25%) they are two distinct bands. **This is the ambiguity to resolve before anything is drawn**;
I am reporting the numbers, not choosing.

Two in-tree facts that bear directly on it:

- **The damage pip already blinks at <50%, and that was a deliberate 2026-08-23 change.** `defaults.yaml:181-182`:
  *"Flashing means critical and critical is 50%, so the pulse starts at the Heavy band and the solid red pip slides up to
  Medium."* `pip-damage-vehicle-critical` is drawn for **both** `heavy-damage` and `critical-damage`
  (`defaults.yaml:204-215`). So a second blink at <50% would duplicate a blink already on the same unit, ~10px away.
- **The 25% token is load-bearing elsewhere and is pinned.** `defaults.yaml:189-190`: the `RequiresCondition` lines are
  untouched on purpose because retuning `^DamageStates` or `Health.cs` *"would move `AutoTargetInfo.BreakOffCondition` off
  25% along with it"*; `infantry.yaml:720-722` says the same. Redefining `critical-damage` is not a display-local edit.

---

## Q6 — Can a decoration blink or animate, and does `SpriteFont` honour alpha?

### Blink: supported, free, and already shipping

`BlinkPattern` / `BlinkInterval` / `BlinkPatterns` are fields on **`WithDecorationBaseInfo`** (`:120-128`), i.e. on the base
of *every* decoration, and are applied in `ShouldRender` (`:155-163`) from `DecorationBlink.PhaseIndex(Game.RunTime, …)` —
wall-clock driven, so the cadence is constant across game speeds (`:26-35`, `:157-159`).

**This reaches the diamond.** `WithTextDecorationInfo` extends `WithDecorationBaseInfo` (`WithTextDecoration.cs:22`), and
`WithSpottedDecoration.ShouldRender` calls `base.ShouldRender` first (`:147`). So blinking the diamond is **two YAML lines,
no rebuild**.

In-tree precedent: three ammo pips, `defaults.yaml:989-1015` (`BlinkInterval: 8 / BlinkPattern: Off, On`, and one inverted
`On, Off` at `:1014-1015`).

`BlinkPatterns` — a `Dictionary<BooleanExpression, BlinkState[]>`, letting the pattern itself switch on a condition
(`:126-128`, resolved `:213-224`) — **is used nowhere in the mod's YAML** (repo-wide grep: only plain `BlinkPattern`/
`BlinkInterval` appear). It is a working, unused channel.

### Animation, including a health-ramped rate — but only on sprites

`WithDecoration` drives an animated sequence off wall-clock via `PlayFetchIndex(info.Sequence, FetchFrame)`
(`:64`, `:79-92`) and supports a **rate that accelerates as health falls**: sequence fields `Tick`, `HealthRampTick`,
`HealthRampStart`, resolved through `DecorationBlink.IntervalForHealth` (`WithDecorationBase.cs:43-50`), with phase carried
across rate changes so the frame does not jump (`BlinkPhase`, `:64-99`, and the PITFALL at `:55-63`).

Shipping example — `pip-damage-vehicle-critical` (`sequences-misc.yaml:460-467`): `Length: 2`, `Tick: 450`,
`HealthRampTick: 120`, `HealthRampStart: 50`, with the documented curve *"50% → 450ms/frame … 0% → 120 (240ms)"*
(`:426-434`). **"Blink faster as it dies" already exists and is already on screen.**

**But `WithSpottedDecoration` is a text decoration and does not go through `anim`/`FetchFrame` at all** — it constructs a
`UITextRenderable` directly (`:168-171`). So the health-ramped *rate* is not available to the diamond without C#; only the
fixed-interval `BlinkPattern` is.

### `SpriteFont` and the alpha byte — **NO. This closes audit gap #3.**

`SpriteFont.DrawText` (`:96-124`) builds its tint from **RGB only** and passes a hardcoded `1f` as alpha:

```
var tint = new float3(c.R / 255f, c.G / 255f, c.B / 255f);   // :103
Game.Renderer.RgbaSpriteRenderer.DrawSprite(g.Sprite, …, 1f / deviceScale, tint, 1f);   // :117-120
```

The trailing `1f` is the alpha parameter — `SpriteRenderer.DrawSprite(Sprite s, …, in float3 tint, float alpha, …)`
(`SpriteRenderer.cs:153-166`). The rotated overload does the same (`:140`, `:174`), and so does `DrawTextContrast`
(`:70`, `:83-89`). `UITextRenderable.Render` reaches all of this through `DrawTextWithContrast` (`UITextRenderable.cs:57`).

**⇒ A colour's alpha byte is discarded on every text path. Fading, ghosting or pulsing the opacity of a glyph is not
available today at any YAML cost; it is engine work.** This bounds every "fade the glyph" option, as the audit anticipated.

Sprites are the opposite: `UISpriteRenderable` takes `scale`, `alpha` and `rotation` (`:24-25`, ctor `:27-37`, used at
`:60`), and `WithGarrisonDecoration` passes real values for scale and alpha (`:263-264`, `:342`). The gap is only that
`WithDecoration` never passes them (`WithDecoration.cs:106`) — the audit's §6 finding, unchanged.

---

## Q7 — Is there a combined "effectiveness" scalar?

**No. None exists.** Nothing multiplies the suppression modifiers together, and nothing combines health with suppression
into a single number.

- Repo-wide search of engine C# for `Effectiveness`, `CombatEffectiveness`, `EffectiveStrength`, `combatPower`: **zero
  matches.**
- The five (infantry) / three (vehicle) modifiers are applied **independently, by different systems, at point of use** —
  `SpeedMultiplier` → `Mobile`; `VisionModifier` → `Vision.Range` (`Vision.cs:45`, `:70-80`); `BurstMultiplier`,
  `BurstWaitMultiplier`, `InaccuracyMultiplier` → `Armament`; `TurretTurnSpeedMultiplier` → `Turreted`. They never meet.
- 14 engine files mention both suppression and health, and each reads them for **separate** decisions:
  `HelicopterSquadBotModule` evacuates on health alone (`:2119`, `:2143`); `FiresEconMath.HealthPreferencePenalty` is
  health-only (`:103-108`); `GarrisonManager` recalls on suppression alone (`:722-733`).
- The only scalar the simulation *does* own and expose for this family is `Detectable.CurrentVisibility` — `[Sync]`,
  recomputed each tick from additive modifiers (`Detectable.cs:127-138`), which is what the diamond already reads.

**⇒ Any mark combining health and suppression would be displaying a formula invented for the display.** Stated as fact,
not as an argument for or against.

---

## Q8 — How the mark is rendered, and the font's real shape alphabet

**The audit is right: the diamond is text.** `WithSpottedDecoration.RenderDecoration` picks a string and a colour per frame
and emits a `UITextRenderable` (`:164-171`); `HollowText = "◊"` and `SolidText = "♦"` are plain `Info` string
fields (`:92`, `:96`). So shape and fill are *codepoints*, and the alphabet is whatever the font ships.

**The font.** `FreeSansBold.ttf`, at `engine/mods/common/FreeSansBold.ttf`, declared in `mods/ww3mod/mod.yaml:315-318` as
`TinyBold` → `Font: common|FreeSansBold.ttf`, `Size: 10`, `Ascender: 8`. Membership is validated at load
(`WithTextDecoration.cs:37-43`). I parsed the file directly: `unitsPerEm 1000`, 2287 glyphs, **2217 mapped codepoints**
(cmap format 4, platform 3/1).

### What the font actually contains

**Geometric Shapes (U+25A0–25FF): exactly ONE glyph — U+25CA LOZENGE.** 1 of 96. This both confirms and extends the audit's
finding: not only are U+25C6/U+25C7 absent, so is *every* triangle, square and circle in that block —
U+25B2/U+25B3 (triangles), U+25A0/U+25A1 (squares), U+25CB/U+25CF (circles) are **all absent**.

**Block Elements (U+2580–259F): ZERO of 32.** **Box Drawing (U+2500–257F): ZERO of 128.**

Usable shapes that *are* present, with ink size at `Size: 10` (**DERIVED** from the `glyf` bbox × 10/1000):

| Codepoint | Name | Ink px | Note |
|---|---|---|---|
| U+25CA | LOZENGE | 5.0 × 7.7 | current hollow |
| U+2666 | BLACK DIAMOND SUIT | 5.8 × 8.0 | current solid |
| U+2662 | WHITE DIAMOND SUIT | 5.8 × 8.0 | a *second* hollow diamond, exactly the solid's width |
| U+2660 / U+2664 | BLACK / WHITE SPADE | 6.0 × 8.0 | solid+hollow pair |
| U+2663 / U+2667 | BLACK / WHITE CLUB | 7.5 × 7.5 | solid+hollow pair |
| U+2665 / U+2661 | BLACK / WHITE HEART | 6.6 × 8.2 | solid+hollow pair |
| U+2206 | INCREMENT | 7.1 × 7.3 | up triangle |
| U+2207 | NABLA | 7.1 × 7.3 | down triangle |
| U+2023 | TRIANGULAR BULLET | 2.8 × 3.0 | tiny |
| U+2022 | BULLET | 2.5 × 2.5 | solid disc |
| U+00B0 | DEGREE SIGN | 3.0 × 3.0 | hollow ring — pairs with BULLET |
| U+204C / U+204D | BLACK LEFT/RIGHTWARDS BULLET | 5.2 × 5.2 | half-filled-*looking* discs, but directional |
| U+20DD / U+20DE / U+20DF | COMBINING ENCLOSING CIRCLE / SQUARE / DIAMOND | 10.0 × 10.1 | see caveat |

**So the shape alphabet is four card suits (each with a genuine hollow/solid pair), two triangles, one lozenge, and a
tiny ring/disc pair.** That is materially wider than "diamond only" — but note the *footprints differ*: a club is 7.5px
wide against the diamond's 5.8, and the diamond↔stance-glyph clearance is only ~2px (`defaults.yaml:941-943`, *"widening
the glyph is not free"*). A mixed-shape alphabet changes the mark's width per state.

**Caveat on U+20DD/DE/DF (inference, untested):** these are *combining* marks meant to draw over the preceding glyph.
`SpriteFont` advances `screen += g.Advance` for every character with no combining-mark handling (`:92`, `:122`), so they
would almost certainly draw in the next cell rather than enclosing anything. Treat as unavailable until tested.

### Partially filled glyphs — the direct answer

**There are NONE.** No half-filled, third-filled or quarter-filled geometric shape exists anywhere in the font: the Block
Elements shade/half-block range is entirely absent, and so is every U+25D0-family half-circle. The only glyphs whose *names*
contain a fraction are U+00BC/00BD/00BE (¼ ½ ¾), which are textual digit-and-slash composites at 8.1 × 7.3 px, not
partially-filled shapes.

**⇒ A three-step fill is NOT free from the font.** Two steps (hollow/solid) is all the glyph channel offers; a third step
needs new art, a different channel, or a non-glyph route.

---

## Q9 — What pixel size is the mark drawn at?

**DERIVED, and I am flagging the residual up front: this is an outline measurement, not a rasterised one.**

The chain, all read:

1. `TinyBold` is `Size: 10`, `Ascender: 8` — `mod.yaml:315-318`.
2. `Renderer` constructs the font as `new SpriteFont(platform, …, x.Value.Size, x.Value.Ascender,
   Window.EffectiveWindowScale, …)` — `Renderer.cs:138-140`.
3. `FreeTypeFont.CreateGlyph` rasterises at `FT_Set_Pixel_Sizes(size * deviceScale)` — `:79-82`.
4. `EffectiveWindowScale = windowScale * scaleModifier` — `Sdl2PlatformWindow.cs:77-84`; `Settings.UIScale` defaults to
   **1** (`Settings.cs:208`). On a non-HiDPI display at default settings **deviceScale = 1**, so the em box is 10px.

**Ink size of the two glyphs actually in use, from the `glyf` bounding box scaled by 10/1000:**

| Glyph | Ink |
|---|---|
| U+25CA hollow | **5.0 × 7.7 px** |
| U+2666 solid | **5.8 × 8.0 px** |

**So the diamond occupies roughly 5–6 px wide by 8 px tall.** For scale, the marks it sits between: the suppression pip is
**6 × 3** (verified from the sprite, Q10) — the one `architecture.md:491` says has non-separable adjacent tiers; the
infantry damage dot is **6 × 6**; the vehicle damage bar is **17 × 5**.

Layout note: `SpriteFont.Measure` returns `rows * size` for height regardless of the glyph (`:230-244`), i.e. a 10px box,
which is why centring is `screenPos - size / 2` (`WithSpottedDecoration.cs:166`, `:170`) and why the audit's §2 note about
ink hanging below the nominal origin holds. Marks are UI-space and do not shrink with zoom (audit §6).

Corroboration from an independent in-tree measurement: `ProductionIconMarks.cs:70-73` states *"every TinyBold glyph here
advances 6px"* — consistent with a ~5–6px ink width plus bearing.

### **This does not honestly settle the proposal, and here is the capture that would**

FreeType grid-fits and antialiases at 10px. Hinting snaps outlines to whole pixels, so the rendered ink can differ from the
outline bbox by ±1px per axis — **on a 5px-wide glyph that is a 20% error** — and the *legible core* of an antialiased
glyph is smaller than its bbox. A four-way shape alphabet and a three-step fill are claims about discriminability at
5–8px, and outline arithmetic cannot adjudicate them.

**The one capture: a screenshot at default zoom showing the diamond on one unit, with a 4× magnified inset beside it** —
exactly the treatment `hunker-readout-static.html` already uses for the suppression pip (audit §8), which is the only
artifact in the repo that shows marks at real 1× size beside magnified insets. Ideally the same frame carries U+25CA,
U+2666, U+2662, U+2206 and U+2663 side by side so the shape alphabet is judged rasterised rather than described. I am not
permitted to launch, so this is handed up.

---

## Q10 — Does `pip-visibility.shp` give a graded fill for free?

**No. It is a colour ramp, not a fill ramp.** Decoded straight from the binary.

Format confirmed against `ShpTSLoader.cs:33-85` (header `zero/width/height/count`, then 24-byte frame headers; `Format 3`
= RLE-zero scanlines). `pip-visibility.shp` is **12 frames on a 12×12 canvas**.

| Frame | Content |
|---|---|
| **0** | **EMPTY** — 0×0, no data. Sequence `pip-visibility-1` (`Start: 0`, `sequences-misc.yaml:304-306`) would draw **nothing**. |
| 1–6 | A 10×10 **outline** — a rectangle whose lower half tapers to a point (a shield / downward chevron). **Byte-identical shape in all six**; only the palette index changes: 175, 173, 171, 168, 165, 162. |
| 7 | The same outline, index 15 (white). |
| 8–11 | The same outline on the full 12×12 canvas with a **1px coloured halo** added around it — two indices per frame: (224 halo, 128 shape), (230, 15), (192, 15), (196, 128). |

So the ten-plus declared rungs (`sequences-misc.yaml:304-339`, twelve of them, `pip-visibility-1` … `-12`) are: one blank,
six identical shapes differing **only in hue/brightness**, one white, and four halo variants. **There is no fill
progression and no size progression.** It does not change the cost of a multi-step fill.

Colours (see the palette caveat below): frames 1→6 run `#081434 → #182C55 → #28386D → #45558A → #7D86AE → #B6BEDF` — a
dark-navy to pale-lavender **brightness ramp on one hue**. Frame 7 is `#FFFFFF`. The halos are teal `#5DC3A6`, red
`#FF0000`, pale blue `#B6DBFF` and blue `#2455CF`.

### The sibling file *is* a geometric ramp — and it is equally unreferenced

`pip-visibility_old.shp` — 12 frames, 8×8, all one colour (index 15, white) — is a genuine **growing-shape** ladder:
frames 0–7 grow the same chevron from 2×1 up to 8×8, frames 8–10 progressively close the top edge (2, then 4, then 8 pixels
of lid), frame 11 adds feet. That is a size/completeness channel rather than a colour one. It is the `_old` file and nothing
references it either.

### Bonus: `pip-suppression.shp` decoded, and it verifies two in-tree claims

**10 frames, 6×3, and all ten are byte-identical** — the same double-chevron `▚▞`-ish mark — differing only in palette
index 209…218. This confirms from the binary both `architecture.md:491` (*"ten colours of ONE glyph, not a bar that
fills"*) and the `[Desc]` at `WithGarrisonDecoration.cs:58-60`. It also closes audit gap #7 (frame count: ten, one per
sequence).

### Palette caveat — read this before quoting any hex

`chrome` is `PaletteFromFile … Filename: temperat.pal` (`palettes.yaml:58-62`). **ww3mod ships no `temperat.pal`** — it
loads `~temperat.mix` (`mod.yaml:30`), a Red Alert asset package that is **not in this repository**. I resolved indices
against the only copy on disk, `engine/mods/ra/maps/chernobyl/temperat.pal`, using OpenRA's own conversion
(`Palette.cs:83-91`: `v << 2`, then `|= >> 6` — equivalently `(v<<2)|(v>>4)`).

**Evidence that the proxy is the right palette:** that formula reproduces, *exactly*, both hex values the in-tree `[Desc]`
independently states for the suppression ramp — tier 1 `#FFD77D` and tier 10 `#9A2800` (`WithGarrisonDecoration.cs:59`).
The full ramp under the correct formula: `#FFD77D, #F7C771, #EFAE55, #EBA241, #E79228, #D77910, #C76100, #B64900, #A63800,
#9A2800` — pale yellow monotonically to dark red.

*(Correcting my own working: I first tested two wrong scalings and got a 2-of-10 match, which looked like a mismatched
palette. The real formula at `Palette.cs:83-91` matches both stated endpoints. The **shape** findings above need no palette
and are certain regardless.)*

---

## Q11 — Can shape, fill and colour vary independently on one decoration today?

**It depends entirely on which of the two families you mean, and the answer is opposite for each.**

### Sprite decorations (`WithDecoration`) — NO. Combinatorial.

`Sequence` is a single `readonly string` (`:28`) bound **once, in the constructor**:
`anim.PlayFetchIndex(info.Sequence, FetchFrame)` (`:64`). `Palette` is likewise one fixed field (`:32`). There is no
condition-switched sequence and no condition-switched palette. The only per-frame freedom is the frame *index* inside that
one sequence, and `FetchFrame` derives it from wall-clock and health (`:79-92`) — not from arbitrary game state.

**⇒ Every distinct appearance costs one more authored `WithDecoration@…` instance plus its own sprite frame.** That is
exactly how the mod does it today: ten suppression instances (`infantry.yaml:589-670`), four damage instances
(`defaults.yaml:191-215`), four rank, five dead defense (`infantry.yaml:671-711`). A shape × fill × colour matrix on this
path is a **product** of authored variants, not three free-running channels.

### Text decorations — colour is free per frame, shape is a codepoint per frame, fill has no third step

`WithTextDecoration` alone fixes both at construction (`:25`, `:30`, `:55`). But **`WithSpottedDecoration` overrides
`RenderDecoration` and chooses both per frame** (`:156-172`):

```
var text  = grade >= info.SolidFromGrade ? info.SolidText : info.HollowText;   // :165
… new UITextRenderable(gradedFont, …, ColorFor(grade), text)                    // :170
```

**⇒ The shipped diamond ALREADY varies two channels independently per frame — glyph identity and arbitrary RGB — with no
sprite and no palette.** Adding a third glyph or a sixth colour is an `Info` field plus a branch: small C#, zero art.

The structural constraint worth naming: on this path both channels are currently functions of **one** scalar
(`CurrentGrade(self)`, `:174-182`). Driving shape from stance while colour tracks suppression means one decoration reading
two traits — which the audit already costs as a *small C# change*, with `WithSpottedDecoration extends WithTextDecoration`
named as the working pattern (audit §7).

What the text path cannot do at any YAML cost: **alpha** (Q6 — discarded), **scale**, **rotation**, and **z-order**
(audit §1). Sprites have scale/alpha/rotation available in the renderable (`UISpriteRenderable.cs:24-27`) but unplumbed on
`WithDecoration` (`:106`).

---

## Q12 — Existing multi-channel marks (precedents)

**Yes — four, and the most relevant one is the mark under discussion.**

1. **`WithSpottedDecoration` itself — shape × colour, already shipping.** Hollow/solid glyph plus five RGB values, both
   driven per frame (`:164-171`, `:184-194`). Its author's reasoning for *why* fill is the second channel is on record and
   is the argument any redesign has to answer: the fill step is *"the coarse channel — it survives low zoom and colour
   blindness, where the colour ramp alone does not"* (`:80-81`).

2. **`WithGarrisonDecoration` — the most multi-channel thing in the mod.** Per soldier it composes **four stacked rows** in
   a fixed grid (damage / class / ammo / suppression, `:84-88`), each row selecting a different sequence from live state
   (`:159-190`), and it varies **size and opacity together** by selection: `scale = selected ? 1f : 0.7f`,
   `alpha = selected ? 0.8f : 0.35f` (`:263-264`), passed through to `UISpriteRenderable` (`:342`, `:352-355`, `:383-386`,
   `:401-404`). It also solves sub-pixel centring across sprite sheets with different intrinsic `Size`/`Offset`
   (`CenteredScreenPos`, `:318-323`) — a problem any mixed-shape alphabet inherits. **Size and opacity as channels are
   solved and on screen.**

3. **`WithCargoPipsDecoration`** — the same scale+alpha selection treatment (`:101-102`; audit §6), and the precedent the
   audit cites for exposing `Scale:`/`Alpha:` on `WithDecorationInfo`.

4. **`ProductionIconMarks`** (`Widgets/ProductionIconMarks.cs`) — a UI-side precedent that solved this exact legibility
   problem explicitly and wrote down its reasoning. Two marks on one 62px icon; it encodes **count in text and category in
   colour** (white half = hand-queued, lime half = recycling, `:28-53`). Three things in it are directly transferable:
   - **A pixel collision budget, computed not eyeballed** (`:70-80`): *"every TinyBold glyph here advances 6px, the count
     sits at x 16 and is 3 glyphs wide at two digits, and the badge is right-anchored at x 59 — so a badge of five glyphs
     or more … lands on it. Rare, reachable, and illegible when it happens."*
   - **A yield rule for when two marks collide** — one mark gives way rather than moving, with the rationale that *"a mark
     that moves as the queue grows is worse than a mark that yields"* (`:75-83`, `RankCountFits` `:81-84`).
   - **The same font trap, independently rediscovered**: *"FreeSansBold ships no U+21BB … so a missing glyph renders as
     nothing at all with the widget working perfectly"* (`:41-46`).
   It also records a channel being **deleted for redundancy**: the lime auto-build stripe *"was deleted outright because it
   carried the same single bit as the badge's lime half"* (`:22-25`).

---

## Honest gaps

1. **Q9's pixel figure is outline-derived, not rasterised.** ±1px per axis from hinting, ~20% on a 5px glyph. The capture
   that settles it is specified in Q9.
2. **Palette provenance (Q10).** The shipped `temperat.pal` lives in `~temperat.mix`, outside the repo. I used a proxy and
   corroborated it at the two indices the tree independently states. The shape findings are palette-independent.
3. **U+20DD/DE/DF combining enclosing marks (Q8)** — inferred unusable from `SpriteFont`'s advance loop; not tested.
4. **I did not verify that `ConditionModifier` is still a live engine trait** (Q1) — only that the block using it is
   commented out.
5. **I did not check whether U+2206/U+2207 and the suit glyphs remain visually distinct at 10px after hinting** (Q8/Q9).
   Their bboxes are 1.3–1.7px wider than the current diamond, which interacts with the ~2px stance-glyph clearance at
   `defaults.yaml:941-943`.
6. **Q4: I did not enumerate which enemy actor types lack `^StandardVision`**, so "any standard-vision enemy" is a
   statement about the four families that carry it (`infantry.yaml:19`, `vehicles.yaml:12`, `aircraft.yaml:12`,
   `structures-defenses.yaml:5`), not proof that no enemy is blind.
7. **No screenshot of any kind was taken**, so every layout and legibility statement here remains arithmetic — the same
   standing caveat the audit opens with.
