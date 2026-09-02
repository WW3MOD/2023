# Soldier readout — recon, 2026-09-02

Read-only survey for the per-soldier status pip rework. No feature code written.
Every claim below carries a `file:line` against `wt/soldier-readout` @ `6a7e1839`.

Three owner recollections were tested. **One was wrong in a way that changes the plan**
(the suppression pip is live, not disabled), one was right for the wrong reason
(force-move does bypass Ambush), one was right (no feedback when Ambush holds a unit).

---

## Q1 — the "!" spotted indicator

- Trait: `engine/OpenRA.Mods.Common/Traits/Render/WithSpottedDecoration.cs`, a
  `WithTextDecoration` subclass.
- Attached via `^UnitIndicators` — `mods/ww3mod/rules/defaults.yaml:888-894`
  (`Text: !`, `Color: FF4A3C`, `Position: Top`, `Margin: -8,0`,
  `RequiresSelection: false`). Inherited by `^SelectableCombatUnit` (`:989`),
  `^SelectableSupportUnit` (`:995`), `^SelectableEconomicUnit` (`:1001`).
- **It is text, not art, deliberately.** `defaults.yaml:865-869`: the 4x4 dot and 6x3
  chevron pip idioms have every colour spoken for, so a new pip would read as one of
  those; `WithTextDecoration` is used nowhere else in the mod.
- Recompute every 7 ticks, cached between (`WithSpottedDecoration.cs:33`); observer
  query bounded at 32c0 (`:40`).

**Binary by explicit design**, `WithSpottedDecoration.cs:16-18`:
> "Binary: drawn or not drawn. It deliberately encodes no observer count, distance or
> severity — spotted is spotted."

**Asymmetry rule** (`:20-22`): an enemy that sees us but that *we* have not spotted does
NOT light the mark — "a badge driven by true visibility alone would be a wallhack".

**Is richer information computed and thrown away?** Partly — and the distinction matters.
`IsSpotted` (`:82-120`) does **not** compute a numeric margin. It short-circuits:
`return true` on the first qualifying observer (`:116`), and `VisionCovers` (`:136-157`)
short-circuits per vision band (`:153`). So no margin is computed *and then* discarded.

But every **ingredient** of a graded readout is already in hand inside that same loop,
at no additional spatial-query cost:
- strength headroom — `visionInfo.Strength` vs `required` (`:142`), where `required` is
  `detectable.CurrentVisibility` (`:93`); integers, and `^StandardVision` runs ten bands
  from Strength 10 @ 4c0 down to Strength 1 @ 32c0 (`defaults.yaml:96-130`).
- distance headroom — `distanceSquared` vs `range` (`:146`).
- observer count — the loop already visits every observer; it merely returns early.

Grading would therefore cost the **loss of the short-circuit** (walk all observers rather
than stop at the first), not a new query. The 7-tick cache (`:33`) already absorbs most of
that. **No new detection layer is required for a graded red.**

Note the code has no notion of "engaged" — it knows *how much vision is on us* and *how
many enemies hold us*, which is not the same thing. (Owner has dropped engaged.)

**Separately, and useful:** `Detectable.CurrentVisibility` is a public int
(`engine/OpenRA.Mods.Common/Traits/Modifiers/Detectable.cs:73`) **and is already exposed
to YAML as a granted condition** `visibility-<N>`
(`VisionDetectableConditionPrefix = "visibility-"`, `:44`; granted `:228`). A unit's own
concealment tier is band-able in pure YAML today, with zero C#.

---

## Q2 — the suppression pip: NOT removed, and never was

The owner's recollection is refuted on the central point.

**It is live, on every infantryman and every vehicle.**
- `^SuppressionPips` — `mods/ww3mod/rules/ingame/infantry.yaml:588-668`, ten
  `WithDecoration@Suppression_1..10` blocks, `Position: Top`, `Margin: 0,-3`, banded on
  `suppressed > X && <= Y`.
- Reached by `^SuppressionEffects` (`infantry.yaml:421-422`) ← `^Infantry` (`:15`).
- Vehicles: `^VehicleSuppressionEffects`, `mods/ww3mod/rules/ingame/vehicles.yaml:357`
  ← `:19`.
- Art `mods/ww3mod/bits/units/pips/pip-suppression.shp`; sequences
  `mods/ww3mod/sequences/sequences-misc.yaml:334-363`.

**Why he thinks it is off / "not for all":** every one of the ten blocks carries
`RequiresSelection: true` (`infantry.yaml:590`, `:598`, …). The pip draws **only on the
soldier you currently have selected**. Corroborated in `WORKSPACE/garrison-proposals.md:73-75`:
> "The soldier's own suppression pips exist but carry `RequiresSelection: true` … so they
> render only when that individual soldier is selected."

**"It didn't really work out" is real, but it is a legibility verdict, not a removal.**
`DOCS/reference/architecture.md:318`, promoted from `WORKSPACE/DISCOVERIES.md:7466-7501`:
> "`pip-suppression` IS TEN COLOURS OF ONE GLYPH, NOT A BAR THAT FILLS … at 6×3 px this is
> a two-or-three-state readout, not a ten-state one … the pip **cannot** signal 'about to
> hit the recall threshold' … Don't plan around the ten tiers being individually legible."

And `WORKSPACE/cargo-garrison-status-260819.md:131`:
> "the grid conveys severity but not direction or proximity-to-recall. A filling bar would
> predict the recall a beat before it happens; **that prediction is most of the value the
> readout was supposed to deliver**."

**Memory fusion — two pips genuinely were killed:**
1. The **medic treatment pip**, by explicit user ruling, `edb4e7bf` 2026-08-23
   "medic: the treatment pip goes" — *"Drop it — faint flash only."* Also recorded at
   `mods/ww3mod/rules/defaults.yaml:12`.
2. **`^VisibilityPips`** — described at `defaults.yaml:869` as "the abandoned
   `^VisibilityPips` **gradient row**", whose deletion is what freed the Top-right lane the
   spotted `!` now uses. **This is the closest prior art to what is being proposed now,
   and it was abandoned.** Recovering *why* is the single highest-value follow-up.

Relevant history: `1f4fff51` 2024-04-29 "WIP: pips etc"; `83d9f34e` 2025-04-18 "Remove
testing pips" (a rename, `^PinsDown` → `^SuppressionEffects`, not a removal);
`ce42ed71` 2026-03-17 vehicle suppression; `ba2ff330` 2026-05-08 "fix(ui): show suppression
pip on regular infantry, not just crew" — the "not for all" episode, **fixed, not
abandoned**; `97414046` 2026-08-17 garrison suppression row.

Came back empty: `git log -S` across `--all` for `SuppressionDecoration`, `SuppressedPip`,
`WithSuppressionPip`, `WithSuppressionDecoration`; `--diff-filter=D` for `*uppress*` assets;
any `-WithDecoration@Suppression*` override-removal in `mods/`. No dead suppression trait
exists — the mechanism is stock `WithDecoration` driven entirely by WW3MOD YAML.

---

## Q3 — what suppression IS today

State: a plain **`ExternalCondition`**, not a bespoke trait.
`mods/ww3mod/rules/ingame/infantry.yaml:428-432` —
`Condition: suppressed`, `TotalCap: 100`, `ReduceTicks: 5`, `ReduceAmount: 1`.
Vehicles mirror it at `vehicles.yaml:352-356`.

**Applied by warheads, graded by range** — `^SmallCaliberEffects`,
`mods/ww3mod/rules/weapons/weapons-effects.yaml:27-62`: `Amount` 50 @ Range 2, 25 @ 4,
12 @ 8, 6 @ 16, 3 @ 32, 2 @ 64, 1 @ 128 (`ValidTargets: Infantry`).
`^MediumCaliberEffects` (`:64+`): 50 @ 5, 25 @ 10, …

**Decay:** 1 point per 5 ticks (`ExternalCondition.cs:212-226`, staggered by `ActorID` to
avoid a map-wide spike). Full 100 → 0 is 500 ticks ≈ **30 s** — consistent with the
tick-rate correction in CLAUDE.md (`MinTicks: 500` = 30 s, i.e. ~16.7 tps, **not** 25).

**Effects — five multiplier families, each in ten 10-point bands** (`infantry.yaml:433+`):

| Family | Band 1 (1-10) | Band 10 (91-100) |
|---|---|---|
| `SpeedMultiplier` (gated `!panicking`) | 90 | — |
| `VisionModifier` | 90 | **0 — blind** |
| `BurstMultiplier` | 90 | **0 — cannot fire a burst** |
| `BurstWaitMultiplier` | 110 | — |
| `InaccuracyMultiplier` | — | **300** |

Also consumed by the AI: `StancePositioningExecutor.MaxSuppressionToMove = 30`
(`engine/OpenRA.Mods.Common/Traits/StancePositioningExecutor.cs:109`, tested `:300`;
set `defaults.yaml:86`) — a suppressed bot unit above 30 will not reposition.

**Player-visible surface: the selection-gated pip and nothing else.**

Related but distinct state: `panicking`, from `PanicGrantsCondition`
(`infantry.yaml:332`), gated `onfire && !heavy-damage-attained` (`:331`),
`PanicSpeedModifier: 140`.

---

## Q4 — where a new pip can live

**There is no health bar.** `SelectionBarsAnnotationRenderable.cs:181` — the
`DrawHealthBar` call is commented out, switched off deliberately in 2024, with the damage
**pip** named as the health indicator in its place; `DrawHealthBar`/`GetHealthColor` are
dead code. `^Infantry` also sets `SelectionDecorations: ShowNever: true`
(`infantry.yaml:55-56`), so infantry have no selection box either. **"Above the health
indicator" therefore means "above the damage pip at `Top 0,-5`".**

Occupied slots on a soldier (traced through `^E1` → `^CamoSoldier` → `^Soldier` → `^Infantry`):

| Margin | What | Where | Gate |
|---|---|---|---|
| `0,6` | class icon / selected marker | `infantry.yaml:225-236` | always / selected |
| `0,0` | `^DefensePips` — **defined, never inherited, dead** | `infantry.yaml:670-705` | — |
| `0,-3` | suppression 1..10 | `infantry.yaml:588-668` | selected only |
| `0,-5` | damage pips, all four bands | `infantry.yaml:722-746` | always |
| `10,0` | rank chevrons | `defaults.yaml:339-370`, override `infantry.yaml:748-756` | always |
| `-8,0` | spotted `!` | `defaults.yaml:889-894` | always |
| `-8,-10`, `-16,-10` | stance glyphs | `defaults.yaml:895-904` | always |
| TopLeft | `WithSpriteControlGroupDecoration` | `infantry.yaml:57` | always |
| TopRight | holding-fire pip, evacuating pip | `defaults.yaml:848-856`, `infantry.yaml:161-166` | always / condition |
| Bottom | ammo pips, `ISelectionBar` stack | `defaults.yaml:919-945`, `infantry.yaml:1243-1248` | — |

**`Top 0,-8` and upward is free on infantry.** `^CargoPips` (`defaults.yaml:946-959`) is
inherited only by vehicles, aircraft, civilian and defences — verified by enumerating all
15 `Inherits@CargoPips` sites; none is an infantry file. The slot-map comment at
`defaults.yaml:869-881` warns that `-10 and upward` is cargo's lane, but that is the
general/vehicle case.

**Mechanism.** `WithDecorationBaseInfo.Position` (default `"TopLeft"`,
`WithDecorationBase.cs:105`) resolved by `SelectionDecorations.GetDecorationPosition`
(`SelectionDecorations.cs:35-48`); `"Top"` = `(bounds.Left + Width/2, bounds.Top)` (`:44`).
`Margin` (`WithDecorationBase.cs:114`) is added in **view pixels, unscaled by zoom**
(`SelectionDecorations.cs:64-67`); for `"Top"` it returns `(-margin.X, margin.Y)` (`:58`),
so **positive X moves LEFT** and **negative Y moves UP**.

**There is no z-ordering.** Every `WithDecoration` emits `zOffset: 0`
(`WithDecoration.cs:106`); `SelectionDecorationsBase.Created` snapshots
`TraitsImplementing<IDecoration>()` (`:42-44`) and `DrawDecorations` iterates in **trait
construction order** (`:129-132`). Two decorations sharing a slot simply overdraw.
The only stacking logic in the codebase is for `ISelectionBar`, `+4px` per bar anchored at
`decorationBounds.Bottom` (`SelectionBarsAnnotationRenderable.cs:51-64`, `:165-166`) — the
bottom edge, not above.

**Cost of a new element:** if the state is already a condition, this is **YAML + art only**
— one `WithDecoration@X` block, one `.shp` beside `pip-suppression.shp`, one sequence in
the `pips:` set (`sequences-misc.yaml:189+`; `:364-390` shows `Tick`/`HealthRampTick` for
animated pips). If it is not a condition, it needs a small C# trait on the
`WithStanceDecoration.cs` / `WithSpottedDecoration.cs` pattern — subclass
`WithDecorationBase<>`, read state on the render path only, write nothing.
The "tapers toward the bottom" is pure art: the sprite is centred on the anchor
(`WithDecoration.cs:106`, `-0.5 * Image.Size`), so a tail is extra frame rows plus a
compensating `Margin`.

**Doc divergence found:** the slot comment at `defaults.yaml:869-881` places damage at
`y=0` and calls `-5` "critical". In fact `y=0` is the dead `^DefensePips` and all four
damage bands sit at `-5`. Worth correcting.

---

## Q5 — Ambush stance and the orange state

`public enum UnitStance { HoldFire, Ambush, FireAtWill }` —
`engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:22`. **Ordered**, and most consumers test
`>=`, so Ambush is one notch below normal. **Per-unit**, on `AutoTarget`; no group object.

Entered/left by: YAML `InitialStance`/`InitialStanceAI` (`:72`, `:75`, both `FireAtWill`);
map-editor `StanceInit` (`:312-320`); the player's `SetUnitStance` order (`:578-579`) issued
per selected actor by `StanceSelectorLogic.cs:87`, bound at
`mods/ww3mod/chrome/ingame-player.yaml:361-373`; `LaneAmbushBotModule` for bots.
`SetStance` (`:440-459`) calls `ResetAmbushState()` on any Ambush→other transition (`:449-451`)
and notifies the running activity **synchronously, same tick**.

**What it suppresses:**

| Suppressed | Predicate | Kind |
|---|---|---|
| Idle firing | `if (isSpotted \|\| ambushTriggered) Attack(...)` — `AutoTarget.cs:774-783` | **delay**, released by `AmbushTactics.EvaluateSpring` (`AmbushTactics.cs:161-191`) |
| Opportunity fire during an activity | `autoTarget.Stance >= UnitStance.FireAtWill` — `AttackFollow.cs:216-217` | **hard veto** |
| Auto supply run | `if (fire == UnitStance.Ambush) return false;` — `SupplyHuntMath.cs:74-75`, consumed `AutoSeekSupplies.cs:429`, call sites `:183`, `:278` | **hard veto**, silent early return |
| Autonomous repositioning | `FireStanceAllowsRepositioning(stance) => stance >= FireAtWill` — `StancePositioningExecutor.cs:599-602`, used `:330-335` | **hard veto** |
| Garrison firing from ports | `buildingStance == Ambush && !ambushTriggered` — `GarrisonManager.cs:789-792` | delay |
| Attack-move march | `AttackMoveActivity.cs:190-206` — cancels permanently (`:119-120`) | hard veto, **bot-only** (below) |
| Move *destination* | `applyAmbushConcealment = isHuman && stance == Ambush && mode != Tight` — `CohesionMoveModifier.cs:1078-1079`, applied `:1182-1183` | **preference** — order obeyed, different cell |

**Scoping that matters:** the attack-move halt requires condition `enable-ambush-tactics`,
granted **only** by `LaneAmbushBotModule`, never to human-owned units
(`mods/ww3mod/rules/defaults.yaml:378-387`). For a human player, Ambush means: hold fire
while idle, no opportunity fire, no supply run, no auto-reposition, and concealment-shifted
move slots. **A plain `Move` is always obeyed** (`AttackMoveActivity.cs:188-189`).

### The crux — is "currently held back" queryable? **No.**

Nothing carries a flag, token or property meaning "suppressed by Ambush right now".

- `ambushPreAimTarget` (`AutoTarget.cs:407`) is the state that *would* mean it, and is
  genuinely **durable** (written `:764`, cleared `:749`, survives between scans `:741-745`)
  — but it is a **private field with no accessor**. Refs: `:407, 742-745, 749, 764, 1012`.
- `AutoTarget.AmbushSprung` (`:389`) is public and durable but is the wrong signal: false
  both while holding a target and while nothing is in sight. Sole consumer
  `LaneAmbushBotModule.cs:432`.
- `haltedForAmbush` (`AttackMoveActivity.cs:36`) is private, on an activity that terminates
  within ticks, and unreachable for humans.
- The supply and reposition vetoes record **nothing** — bare `return`
  (`AutoSeekSupplies.cs:183`, `:278`; `StancePositioningExecutor.cs:330-335`).
- `stance-ambush` is granted (`defaults.yaml:376, 637, 737, 749`) and consumed by nothing
  in `mods/` — a free durable token, but it means "in Ambush", not "held".

**The cheap path, and it is a real precedent.** `AutoTarget.LastHeldFireTick` (`:350`) is a
public durable tick-stamp read by `WithHoldingFireDecoration.cs:53-55` with a linger window
— exactly the render-safe shape orange needs, already shipped and working. It is stamped
only from `ChooseTarget`'s overkill/break-off declines (`:1460`, `:1470`, `:1584-1585`),
never from the Ambush hold. So orange is **one stamp added in `AmbushTickIdle`** where the
target is valid and `!ambushTriggered`, plus a decoration — not a new state machine.

### Current feedback when Ambush holds a unit: **none**

Zero matches for "ambush" in `mods/ww3mod/languages/en.ftl`, `mods/ww3mod/rules/sound/`,
`cursors.yaml`, `hotkeys.yaml`; zero `Notification|PlayVoice|Sound.Play|TextNotification`
in `AutoTarget.cs`, `AttackMoveActivity.cs`, `StancePositioningExecutor.cs`,
`AutoSeekSupplies.cs`. `WithStanceDecoration` draws "A" whenever in Ambush
(`WithStanceDecoration.cs:111`) — that is *stance*, not *veto*. The only implicit cue is
`PreAimAtTarget` silently turning the soldier to face a target the player cannot see.

### Override

- **Plain move needs no force** — always obeyed.
- **Force-move does bypass the one thing that alters a player order**, but for a different
  reason than assumed: `CohesionMoveModifier` handles only `"Move"`/`"AttackMove"`
  (`:1013-1014`), and force-move issues order string `"ForceMove"`
  (`Mobile.cs:1071, 1222, 1233`).
- **No force bypass exists** in the attack-move halt, the supply veto or the reposition
  veto — all three test stance alone.
- **Stance change releases immediately** (`SetStance` `:446-458`; opportunity fire re-reads
  `Stance` every tick, `AttackFollow.cs:216`). `Alt+Click` on the stance button also issues
  `Stop` (`StanceSelectorLogic.cs:108-115`). Caveat: an attack-move already cancelled by the
  halt is **not** resumed.

---

## Other per-soldier state that could be surfaced

Already visible somewhere: suppression (selected-only pip), damage band (pip), rank
(chevrons), stance (glyphs), spotted (`!`), holding fire (pip), control group, ammo
(bottom pips + reload bar), evacuating (pip).

Computed and **not** surfaced: `Detectable.CurrentVisibility` / `visibility-<N>` condition
(own concealment tier, 1-10 — YAML-band-able today); `panicking` (`infantry.yaml:332`);
`onfire`; `inwater` (`infantry.yaml:~330`); `AutoTarget.LastHeldFireTick` (surfaced only for
overkill declines, not ambush); `ambushPreAimTarget` (private); veterancy XP progress
(`GainsExperience`); `AmbushSprung`. Nothing anywhere computes **"this unit can see an
enemy"** — searched for `HasEnemyInSight`, `EnemiesInRange`, `SeesEnemy`, `EnemyInSight`,
`enemy-in-sight` across `engine/` and `mods/`: zero hits.

---

## Verdict on the three states

| State | Cost | Why |
|---|---|---|
| **Red** — definitely spotted | **free** | Ships today as the `!`. Repainting it as a diamond is YAML + art. Grading it toward red costs the short-circuit in `IsSpotted`, no new query. |
| **Orange** — held back by Ambush | **cheap, but needs C#** | ~5 lines: stamp a tick in `AmbushTickIdle`, following the shipped `LastHeldFireTick` → `WithHoldingFireDecoration` pattern. The state does not exist today. |
| **Yellow** — we see any enemy | **most expensive of the three** | Nothing computes it. Needs a new render-path query — but the *easy* direction: no truth gate, no asymmetry problem, and it can reuse `WithSpottedDecoration`'s `FindActorsInCircle` + cache shape. |

Two standing cautions for whoever designs this:
1. `^VisibilityPips`, an **abandoned gradient pip row**, previously occupied this exact
   design space (`defaults.yaml:869`). Find out why it was abandoned before rebuilding it.
2. The shipped legibility verdict — ten states in one 6x3 glyph read as two or three
   (`DOCS/reference/architecture.md:318`). A three-state design respects that; a *graded*
   three-state one starts walking back toward what already failed once.
