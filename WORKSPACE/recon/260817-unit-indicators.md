# Recon: spotted-state + stance indicators

**Date:** 2026-08-17 · **Branch:** `wt/indicator-recon` · **Base:** `main @ a7cc6c59`
**Status:** read-only recon. No code changed. Game never launched.

Scope: can we draw (a) a binary "an enemy can see me" mark and (b) a stance mark on
units, and can (a) also drive behaviour. Per the 2026-08-17 steer the spotted state is
**binary** — drawn or not drawn, no severity, no observer count, no distance.

---

## 0. The three findings that should shape the design

1. **The central tension in the brief is mis-framed, and dissolving it makes the
   feature much cheaper.** "Per-player" does not mean "client-local" in this engine.
   Every client simulates every player's shroud, so a knowledge-limited per-player
   quantity can still be synced. The desync boundary is not *whose knowledge* — it is
   whether you read **who is watching** (`RenderPlayer` / `LocalPlayer` / `LocalRandom`).
   Both halves of what the user wants are therefore available. §2.

2. **The real hazard is elsewhere, and this project has already been bitten by it
   twice.** Implementing "spotted" as a *granted condition* — the obvious YAML route —
   is the exact shape of two shipped desyncs, one of them in the visibility code
   specifically. §2.4.

3. **The gradient version of this feature was already built, and abandoned.**
   `^VisibilityPips` (`mods/ww3mod/rules/ingame/infantry.yaml:841-928`) draws a numeric
   1–12 badge of how detectable a unit is. It is dead — the sole reference is a
   commented-out `# Inherits: ^VisibilityPips` at `infantry.yaml:704`. Someone built the
   "how badly am I seen" indicator, wired it to nothing, and commented it out. The
   user's instinct today to make this binary matches what the codebase already
   concluded in practice.

---

## 1. What exists

### 1.1 The visibility primitive

`Actor.CanBeViewedByPlayer(Player)` — `engine/OpenRA.Game/Actor.cs:591-598`:

```csharp
public bool CanBeViewedByPlayer(Player player)
{
    foreach (var shouldHideModifier in shouldHideModifiers)
        if (shouldHideModifier.ShouldHide(this, player)) return false;
    return defaultVisibility.IsVisible(this, player);
}
```

It takes **any** player, not just the local one, and it accounts for stealth/cloak via
`shouldHideModifiers`. This is exactly the query the feature needs.

It is already used **to drive behaviour, with an enemy player as the argument**:

- `AutoTarget.cs:749` — `var isSpotted = self.CanBeViewedByPlayer(targetOwner);` is the
  Ambush spring trigger, re-evaluated every scan.
- `AttackMoveActivity.cs:207-231` `GroupDetectedBy` — group-level "are we seen", with an
  explicit determinism note at `:200`: *"CanBeViewedByPlayer is sim-legal and draws no
  RNG."*

So **"am I spotted" is not a new capability. It already exists and already changes what
units do.** The feature is largely a matter of surfacing it.

### 1.2 The knowledge-limited layer

`BeliefStore` (`engine/OpenRA.Mods.Common/Traits/World/BeliefStore.cs`, registered at
`mods/ww3mod/rules/world.yaml:359`) is a per-player table of believed enemy contacts,
built strictly from that player's own legal vision plus their `FrozenActorLayer`. Its
header (`:23-32`) states the two properties that matter here:

> *"built STRICTLY from per-player-legal, synced sources… DETERMINISM: pure integer
> confidence math, keyed by synced ActorID; no LocalRandom, never reads
> RenderPlayer/LocalPlayer, and ZERO RNG."*

**This is the thing the brief hoped existed: synced *and* knowledge-limited.** It is the
set of "enemies we are aware of" the user's definition needs.

Two caveats, both load-bearing:

- It refreshes **one player per sub-slot, round-robin** (`BeliefStore.cs:180-186`), so a
  given player's table is stale by up to a full `UpdateInterval`. Fine for a badge;
  worth knowing before anything twitchy reads it.
- It is declared **inert**: *"NOTHING consumes it for behaviour"* (`:33-37`). Making it
  the first behavioural consumer is an architectural step with its own gating recipe
  (`DOCS/reference/influence-stack.md:143`) — see §2.3.

### 1.3 Decorations are already per-viewing-player, and already write nothing

`WithDecorationBase.ShouldRender` (`engine/OpenRA.Mods.Common/Traits/Render/WithDecorationBase.cs:74-107`)
already calls `self.World.FogObscures(self)` and gates on
`self.Owner.RelationshipWith(self.World.RenderPlayer)`. The file's own comment at `:30`:
*"sim / [Sync] state and writes nothing — safe to call from the render path only."*

**Rendering a per-viewing-player indicator therefore carries zero desync risk by
construction.** That half of the feature is free.

### 1.4 Stances

Four orthogonal axes, all on `AutoTarget`, all enums at
`engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:22-28`:

| Axis | Values | Default | Conditions granted? |
|---|---|---|---|
| `UnitStance` | HoldFire, Ambush, **FireAtWill** | FireAtWill (`:72,:75`) | yes — `stance-*` |
| `EngagementStance` | HoldPosition, **Defensive**, Hunt | Defensive (`:164,:167`) | **no — never set in any YAML** |
| `CohesionMode` | Tight, **Loose**, Spread | Loose (`:186,:189`) | no |
| `ResupplyBehavior` | Hold, **Auto**, Evacuate | Auto (`:193,:196`) | no |

All four are synced and order-driven — `[Sync]` int projections at `AutoTarget.cs:350-360`,
mutated only through `ResolveOrder` (`:556-568`). Client-local `Predicted*` copies
(`:374-384`) exist for button highlighting only.

`assault-move` is **not** a stance — it is `AttackMove.AssaultMoveCondition`, granted only
while an assault-move activity runs (`mods/ww3mod/rules/defaults.yaml:589,677,699`).

**Two findings that change the cost of the stance badge:**

- `stance-holdfire` and `stance-ambush` **are granted** (`defaults.yaml:309,310,570,571,670,671,682,683`)
  but consumed by **nothing** — a whole-repo grep finds no `RequiresCondition` on them.
  Dead weight today, but it means a `WithDecoration` keyed on them **works right now with
  zero C#.**
- Engagement stance grants **no conditions at all** (`HuntCondition` / `HoldPositionCondition`
  never set in mod YAML), so badging Hunt/HoldPosition needs a YAML grant added first.
  That is cheap and safe — stance changes already travel as orders.

### 1.5 What varies by stance today

Fire stance is real and deep: idle scan disabled below Ambush (`AutoTarget.cs:965`),
retaliation disabled below Ambush (`:625`), opportunity fire requires FireAtWill
(`Attack/AttackFollow.cs:174`), plus the Ambush pre-aim/hold machine (`:696+`), garrison
fire discipline (`Garrison/GarrisonManager.cs:701,748,1172`), and attack-move halt
(`AttackMoveActivity.cs:156-165`).

Engagement stance is also real (`AutoTarget.cs:651,971`, `Activities/Attack.cs:263-284`,
`SmartMoveActivity.cs:68,166`, `SupplyProvider.cs:451,1061`).

Cohesion is **effectively inert** — single consumer at `CohesionMoveModifier.cs:1051`,
affecting slot spacing only at the instant a grouped move order is issued.

**Stance behaviour already consults visibility** (answering the brief's Q5 directly):
`AutoTarget.cs:749` (ambush trigger), `:651` (don't fire at an invisible enemy unless
Hunt), `:724` (pre-aim target must be viewable), `AttackMoveActivity.cs:207-231`.

---

## 2. What the spotted state would cost — the sync answer

### 2.1 The framing correction

The brief asks whether a per-player quantity can drive behaviour without desyncing, and
treats "per-player" as inherently client-local. **It is not.** In OpenRA every client
simulates every player's `MapLayers` (the shroud trait, `ISync` at
`engine/OpenRA.Game/Traits/Player/MapLayers.cs:73`, with `[Sync]` state at `:134-168`).
Player P's shroud is not private to P's client — it is replicated simulation state.

The influence-stack doc says this in as many words (`DOCS/reference/influence-stack.md:29`),
describing `Participates` as *"a **sim-legal proxy for 'the overlay viewer'** — it
deliberately never reads `world.RenderPlayer` (that would make simulation depend on the
render path and desync)"*.

So the rule is: **per-player data is safe; per-*client* data is not.** The forbidden reads
are `RenderPlayer`, `LocalPlayer`, `LocalRandom`, and wall-clock. Knowledge-limited
behaviour is therefore *possible* — the tension the brief names largely dissolves.

Two different, real problems replace it. Both are worth more attention than the one that
dissolved.

### 2.2 Problem A — the asymmetry rule needs attribution the shroud does not expose

The user's rule is *"the indicator does not work if we are spotted by a unit that we have
not yet spotted ourselves."* `CanBeViewedByPlayer(P)` cannot express this: it aggregates
**all** of P's vision sources and answers only "can P see me", with no attribution to a
specific observer.

`MapLayers` does keep per-source records — `readonly Dictionary<object, VisionSource> sources`
(`MapLayers.cs:116`, populated in `AddSource` at `:320-396`) — but the dictionary is
**private with no public accessor**, and `VisionSource` stores the source's location,
strength and covered cells, **not the owning Actor**. There is no existing query for
"which actors' vision covers this cell", and adding one means new engine API on a
hot, sync-critical path.

The tractable route is the other direction: iterate our **own** `BeliefStore` contacts,
and for each believed enemy check whether our unit lies within that actor type's reveal
range. That is deterministic integer math on already-synced data. But it is an
**approximation of the shroud, not the shroud** — it would not reproduce
`AddSource`'s height/shadow modifiers (`MapLayers.cs:360-372`), so it will disagree with
real visibility around terrain shadow. It also inherits BeliefStore's round-robin
staleness and its believed (possibly wrong) contact positions.

Worth saying plainly: **using the *believed* position is arguably the right answer, not a
defect.** "We think that scout is still on that ridge, so we assume it can see us" is
good commander's-view reasoning, and it degrades gracefully.

### 2.3 Problem B — knowledge-limited *behaviour* would mutate `@stable`

`BeliefStore` currently has no behavioural consumer (`:33-37`). Making it one is governed
by `DOCS/reference/influence-stack.md:143`, which warns that `@stable` and every human
`Participates` (`InfluenceStack.cs:42-52`), so a flag on the world trait would reach
`@stable` and break the benchmark control. The house pattern is a **per-player opt-in**
(`ControlField.RequestFrontlineProfile`). Doable, but this is a real chunk of work, not a
YAML line.

### 2.4 Problem C — the granted-condition trap. **This is the one that will bite.**

The natural way to drive a `WithDecoration` is `RequiresCondition: spotted`, which means
granting a condition when visibility changes. **That exact pattern is the cause of two
desyncs in this repo's recent history.**

- `Detectable.visionDetectableConditionToken` was `[Sync]`-annotated and revoked/re-granted
  on **every visibility change**. Write-up at `WORKSPACE/bugs/discovered.md:556`. The
  PITFALL comment now sits in the code at
  `engine/OpenRA.Mods.Common/Traits/Modifiers/Detectable.cs:152-155`:

  > *"never [Sync] a condition token — its value is an allocation handle counting how many
  > conditions the actor has been granted, so a grant-count skew desyncs clients whose
  > gameplay state agrees. The gameplay state here is the visibility level, synced on
  > CurrentVisibility above."*

  The fixed shape is visible at `Detectable.cs:62-63`: `[Sync] public int CurrentVisibility`
  — **sync the boolean, never the token.**

- `LaneAmbushBotModule.EnsureGatedAmbusher` granted a condition directly from a bot tick
  instead of issuing an order; the save/replay never re-granted it
  (`WORKSPACE/bugs/discovered.md:538`). Same class.

**The rule this implies for the spotted state:** if it is ever `[Sync]`, sync a `bool`;
grant the condition as an unsynced side effect; and if the grant originates anywhere a
bot tick can reach, route it through an order. If the indicator is render-only it should
not be a condition at all — read the trait directly, as `WithHoldingFireDecoration`
already does (`mods/ww3mod/rules/defaults.yaml:760-779`).

### 2.5 Recommendation — split the two uses, and accept that they differ

| | Definition | Cost | Desync risk |
|---|---|---|---|
| **Render** | knowledge-limited (BeliefStore-derived) | moderate; new render-side helper | **none** — decorations already read `RenderPlayer` and write nothing (§1.3) |
| **Behaviour** | true `CanBeViewedByPlayer(enemy)` | **~zero — already shipped** (`AutoTarget.cs:749`) | none; already sim-legal |

Behaviour should use the **true** synced boolean, because it already does. Looping the
enemy players is cheap (≤7 players, one shroud lookup each).

**I have to flag plainly that this contradicts part of the brief.** The user said *"We
will also use this indicator to control the unit behaviour"* — one notion, two uses. The
split means the badge and the behaviour can **disagree**: a unit can take cover from an
observer the player's badge does not show.

I think that is not only acceptable but correct, and the asymmetry rule is the reason.
The rule exists to stop the badge leaking information — a true-visibility badge is a
wallhack, lighting up to announce "an enemy you cannot see is watching you." That
argument is about **what the player learns**. It does not apply to what the *unit* does:
a soldier reacting to a sniper he hasn't identified is realism, not cheating, and it is
already how Ambush behaves today.

So: same word, two definitions, deliberately. If the user wants them unified, the honest
price is §2.3 (make BeliefStore behavioural, with a per-player opt-in gate, without
mutating `@stable`) — and that is a project, not a patch.

---

## 3. What the pip layer can and cannot do without new art

*(§3 rests substantially on a delegated decode of the SHP files; I independently verified
the reference/dead-code claims in 3.3 by grep, but did **not** personally re-decode the
sprite frames.)*

### 3.1 There is no automatic stacking — every row is hand-placed

Decorations are collected at `SelectionDecorationsBase.cs:42-43` and drawn in a flat loop
at `:129-132`. Position comes entirely from per-trait `Position` + `Margin`. Origin math
at `SelectionDecorations.cs:35-67`; note the sign flip for `Top`: **+Y moves down, +X
moves left** (`:50-62`).

`Top` is already contested — class icon and selected ring both at `0,6`, damage pips at
`0,0`, suppression at `0,-3`. A new row must pick unused Y (e.g. `0,-16`) or a free X lane
(rank uses `16,0`; the dead visibility row used `-15,0`). **`Bottom` is the crowded edge**,
not `Top`: `ISelectionBar` extras stack there at +4px per bar
(`SelectionBarsAnnotationRenderable.cs:57-60,165-166`). There is **no health bar** — it is
commented out at `:168`.

`WithGarrisonDecoration` is the worked example and it deliberately **does not** join the
shared stack: it renders a self-contained 4-row grid from one anchor, with row Y derived
relative to a fixed `ClassRow` (`:331-334`) and a fixed slot height (`:297`) so adding rows
never shifts the ones above. That is the pattern to copy for anything multi-row.

### 3.2 Text works, at 10px, and only as a fixed string per trait

`WithTextDecoration` (`engine/OpenRA.Mods.Common/Traits/Render/WithTextDecoration.cs:22-44`)
is real and compiled but **used nowhere in `mods/`** — this would be its first use.
Smallest font is `Tiny`/`TinyBold` at **Size 10** (`mods/ww3mod/mod.yaml:289-323`); nothing
smaller exists. `Color` is free RGB per instance (`:31-33,55`).

The catch: `Text` is a **static string per trait instance** — it cannot vary at runtime.
The working pattern is one instance per state gated by `RequiresCondition`, exactly as
`^SuppressionPips` does (`infantry.yaml:548-628`). Three stances = three trait entries.

### 3.3 Spare art exists, and binary means we may need none

`pip-visibility.shp` — 12 frames, sequences defined at
`mods/ww3mod/sequences/sequences-misc.yaml:260-283`, and referenced by **no live rule**
(verified: the only `Sequence:` references in `rules/` are to `pip-numbers-*`, in the dead
`^VisibilityPips` block). Frames 8-11 are reported as 12×12 **two-tone with a dark
outline** — which is what survives on varied terrain — and shaped like a downward
funnel/vision-cone, semantically apt for "spotted".

**Because the state is binary, the ask is one frame, and one plausible frame already
exists. This feature may need no new art at all.** The tradeoff: 12×12 is chunky next to
the 6×3 chevrons, so "discreet" may argue for a hand-drawn 6×3 anyway.

Other spares: `pip-defense.shp` (5 frames, 6×3 upward chevron, unreferenced beyond the
dead `^DefensePips`), `pip-numbers.shp` (digits 1-12, ~7×10 — real glyphs, usable as a
badge with no font at all), `pip-seal.shp` / `pip-skull.shp` (1 frame each, unreferenced).

**Two broken sequences found in passing** (worth a line in `discovered.md`, not this
feature's problem): `sequences-misc.yaml:184-188` documents entries with no filename
(`groups`, `medic`, `tag-*`) falling back to `pips.shp`, **which this mod does not ship** —
they draw nothing, silently. And `pip-cover` / `pip-dugin` (`:236-239`) reference a
`pip-cover` file that does not exist anywhere under `mods/`.

### 3.4 Palette recolouring will **not** save us

`WithDecorationInfo.Palette` (`WithDecoration.cs:30-35`, resolved at `:53-56`) exists, but a
palette swap remaps *all* indices rather than tinting one sprite. The two available
palettes, `chrome` and `effect`, are both `temperat.pal` differing only in `ShadowIndex`
(`palettes.yaml:58-72`) — switching between them changes essentially nothing. Player-colour
remap covers indices 80-95 (`palettes.yaml:130-138`), and **no pip sprite uses that range**
(suppression 209-218, visibility 162-230, numbers 15). So player-colour remap cannot
recolour any existing pip.

**Colour variation is cheap only via text** (`WithTextDecoration.Color`), or by authoring
extra frames at different palette indices — which is exactly what `pip-suppression` did
(all ten of its frames are the same chevron at different hues).

### 3.5 Fade on losing contact — available as a *blink*, not a fade

Per the steer, reporting rather than pursuing. `WithDecorationBase` has `BlinkPattern` /
`BlinkInterval` (`:56-60`), driven from wall-clock `Game.RunTime` and explicitly render-only
(`DecorationBlink.PhaseIndex`, `:24-36`). So a **blink is free** — no new state, no new art,
no sync exposure.

A true **fade is not available**: `WithDecoration` exposes no alpha/opacity field, and
"just lost contact" needs a timestamp of the transition, which is new state. **Per the
steer, drop it.** Recording the cheap version for later: if the just-lost-contact moment
ever proves worth marking, a few seconds of blink on the existing frame costs one YAML
field.

---

## 4. The art ask, if the user wants to draw

Deliberately short — binary made it cheap, and two of these may be unnecessary.

**Constraints for anything hand-drawn:** must be indexed into `temperat.pal` (not free
RGB); a **1px dark outline** is what makes a pip survive over varied terrain (the existing
`pip-visibility` frames 8-11 already do this, the 6×3 chevrons do not); match the existing
pip scale of **6×3**, or **10×10 with outline** if the shape needs the room.

| # | Frame | Size | Needed? |
|---|---|---|---|
| 1 | **Spotted** — an eye, or a downward vision-cone | 6×3 or 10×10 outlined | **Optional.** `pip-visibility` frame 8-11 can stand in today; hand-drawn only buys a smaller, more discreet mark |
| 2 | **Hold fire** | 6×3 outlined | Optional — `pip-orange` is already the hold-fire marker (`defaults.yaml:770-779`) |
| 3 | **Ambush** | 6×3 outlined | Optional — could be a `TinyBold` glyph instead |
| 4 | **Hold position** | 6×3 outlined | Optional |
| 5 | **Hunt** | 6×3 outlined | Optional |

**Honest summary of the ask: zero frames are strictly required.** Binary spotted can ship
on existing art, and all four stance states can ship as `WithTextDecoration` glyphs in
distinct colours. Hand-drawn art buys discretion and legibility at small sizes, not
capability.

---

## 5. Which stances to actually draw

An indicator on every unit in its default stance is pure noise. Recommended list:

**Draw:** `HoldFire` and `Ambush` (fire axis — both genuinely change whether and when the
unit shoots, and both are silent failure modes a player otherwise misreads as a broken
unit); `HoldPosition` and `Hunt` (engagement axis — "will never reposition" and "will chase
off-leash" are both real, verified branches).

**Do not draw:** FireAtWill and Defensive (the defaults). **All three cohesion modes** —
cohesion is inert except at the instant a grouped move order is issued (§1.5), so a badge
would tell the player nothing about what the unit is currently doing. Resupply `Hold`/`Auto`
— only meaningful when ammo is out, which the existing ammo pip already signals
(`defaults.yaml:794`). `Evacuate` is arguably worth a badge and already has one
(`infantry.yaml:160-165`).

That is **four states across two axes**, of which two (`stance-holdfire`, `stance-ambush`)
can be keyed off conditions that are **already granted today** with no C# at all.

---

## 6. What I did not verify

- **Never launched the game.** Every claim about clutter, collision and legibility is
  derived from the cited margin arithmetic, not observed. The one thing I would most want
  before committing to margins is a screenshot of a selected infantryman with suppression
  and cargo pips live.
- **I did not personally decode the SHP frames** (§3.3) — that came from a delegated pass.
  I did independently verify by grep that `pip-visibility` is unreferenced by any live
  rule and that `^VisibilityPips` is dead, which are the claims the "no new art"
  conclusion actually rests on. The frame *contents* (12×12, two-tone, funnel-shaped) are
  second-hand and worth one decode before anyone relies on them.
- **I did not read `BeliefStoreInfo.UpdateInterval`'s value**, so "stale by up to a full
  interval" is qualitative.
- **I did not measure** the per-tick cost of looping enemy players for the true-visibility
  boolean. It is one shroud lookup per enemy player per unit; I claim it is cheap by
  analogy with `AutoTarget.cs:749`, which already does one such lookup per scan, but I did
  not profile it.
- `CheckSyncAnnotations` (`engine/OpenRA.Mods.Common/Lint/CheckSyncAnnotations.cs:47-59`)
  emits **warnings, not errors**, and only checks that `ISync` and `[Sync]` are paired. I
  did not confirm whether this repo's `make test` promotes lint warnings to failures — so
  I would not rely on it to catch a bad sync annotation on this feature.

---

# Addendum — "almost spotted" and reactive hiding (round 2)

**Date:** 2026-08-17 · **Base:** `main @ e1e80ef2` (round-one recon merged)
**Status:** read-only. No code changed. Game never launched.

The headline: **most of what the user is speculating about is already built and
shipped.** The graded quantity exists, the threshold exists, the per-observer strength
ladder exists, and "actions increase your visibility" is live YAML with tuned numbers.
The request/response mechanism they propose is unnecessary — the value is a public field
on a synced trait, readable directly. What is genuinely missing is small: nobody *reads*
the margin, and nobody *reacts* to it.

There is also one inverted-sign trap that will produce a backwards indicator if it is
missed, and one "explored ≠ observed" trap that will produce a permanently-lit one. Both
are in §A.6.

---

## A.1 The graded quantity exists, and so does the threshold

Both sides of "requires 5 vision, enemy has 3 or 4" are real, shipped, and synced.

**Observer side.** `MapLayers.ResolvedVisibility` — `public ProjectedCellLayer<byte>`
(`engine/OpenRA.Game/Traits/Player/MapLayers.cs:131`) — holds, per projected cell, the
**highest vision strength any of that player's sources has on that cell**, resolved at
`:239-264`:

```csharp
for (var i = (byte)(visibilityCount[index].Length - 1); i > 0; i--)
    if (visibilityCount[index][i] > 0) { visibility = i; break; }
```

Scale is `VisionLayers = 11` (`:75`), so strengths run **0..10**.

**Actor side.** `Detectable.CurrentVisibility` — `[Sync] public int`
(`engine/OpenRA.Mods.Common/Traits/Modifiers/Detectable.cs:62-63`) — is the **required**
observer strength, recomputed every tick from `Vision` plus modifiers and clamped to
**[1, 10]** (`:80-87`).

**The threshold is a single comparison** (`MapLayers.cs:574-580`):

```csharp
public bool IsVisible(PPos puv, int visibility)
{
    if (!FogEnabled) return map.Contains(puv);
    return ResolvedVisibility.Contains(puv) && ResolvedVisibility[puv] > visibility;
}
```

So, for enemy player `E` and our unit at cell `c`:

```
margin = E.MapLayers.ResolvedVisibility[c] - myDetectable.CurrentVisibility
margin >= 1  →  spotted
margin <= 0  →  not spotted, and |margin| is how many strength steps of slack remain
```

**"Almost spotted" is that subtraction.** No new field, no new state, no accumulator.

**And the strength ladder is distance.** `^StandardVision`
(`mods/ww3mod/rules/defaults.yaml:47-84`) is ten concentric annuli — strength 10 within
4 cells, 9 from 4–7c, 8 from 7–10c, … 1 from 28–32c. So `ResolvedVisibility` is
effectively a **distance-quantised proximity-to-detection metric**, and one step of
margin is roughly **three cells of approach**. That makes "almost spotted" a tunable
warning distance rather than an abstraction.

Terrain is already folded in: `AddSource` subtracts a per-cell shadow term,
`modifiedStrength = strength - shadowModify` (`MapLayers.cs:355-378`), sourced from
`map.ShadowLayer`. So the number already accounts for forest/terrain shadow along the
real sightline.

**Answer to Q1: the quantity exists, the threshold exists, and "3 or 4 against a required
5" is a literal computable statement, not a metaphor. This is an afternoon, not a week.**

## A.2 Actions already modify detectability — with tuned numbers

Not aspirational. `DetectableAddativeModifier` (`Traits/Multipliers/DetectableAddativeModifier.cs`)
applies an additive `VisionModifier` to the required threshold, gated by a condition.
Live set:

| Modifier | Condition | `VisionModifier` | Effect | Site |
|---|---|---|---|---|
| Firing | `firinganyweapon` | **−2** | easier to see | `infantry.yaml:728-730` |
| Moving | `moving` | **−1** | easier to see | `infantry.yaml:731-733` |
| Prone | `prone` | +1 | harder | `infantry.yaml:717-719` |
| Dug in | `dugin` | +1 | harder | `infantry.yaml:720-722` |
| In cover ×3 | `object-proximity == 1/2/>=3` | +1/+2/+3 | harder | `infantry.yaml:708-716` |
| Rank ×4 | `rank-veteran == 1..4` | +1/+2/+3/+4 | harder | `defaults.yaml:211-222` |
| Landed aircraft | `!airborne` | +3 | harder | `aircraft.yaml:46-48` |
| Sniper firing | `firinganyweapon` | −1 | easier | `infantry.yaml:2077-2079` |

Base thresholds: standard infantry `Vision: 3` (`infantry.yaml:95-96`), sniper and
infiltrator `Vision: 5` (`:1625-1626`, `:2071-2072`), vehicles/husks `Vision: 1`.

Firing is transient and self-revoking: `GrantConditionOnAttack@Firing` grants
`firinganyweapon` with `RevokeDelay: 12` (`infantry.yaml:723-727`) — you glow for 12
ticks after shooting.

**The stop-to-hide loop already exists in one direction.** `GrantConditionOnMovement`
(`infantry.yaml:138-141`) grants `moving` while moving and — critically —
`ConditionWhenStill: dugin` after `TimeToBeStill: 200`. So standing still already
*removes* the −1 and eventually *adds* +1. A unit that stops genuinely becomes harder to
see. The user's "stop instead of move" is not a new mechanic; it is an existing mechanic
with nothing choosing to use it.

**Answer to Q2: modelled, tuned, and live. Range in practice is the full clamp** — a
firing, moving rifleman is 3−2−1 = 0 → clamped to 1 (near-certain detection); a dug-in
rank-4 veteran in heavy cover is 3+1+4+3 = 11 → clamped to 10 (effectively invisible).

## A.3 Q3 — the request mechanism: **refuted, and the manager's read is correct**

Confirmed on all three points.

1. **There is nothing to pass.** `ResolvedVisibility` is a **public field**
   (`MapLayers.cs:131`) on a trait that is `ISync` (`:73`) and lives on every player's
   `PlayerActor`, reachable as `player.MapLayers` (the idiom `BeliefStore.cs:190` already
   uses). Any unit can read any enemy player's vision at its own cell **directly**, this
   tick, with an array index. The information the user wants "requested" is already
   sitting in a synced array that every client maintains.

2. **It is cheaper than they think.** The per-unit cost is one `ProjectedCellLayer<byte>`
   index per enemy player — call it ≤7 array reads — against `Detectable`'s existing
   per-tick `ITick` recompute (`Detectable.cs:78-93`) which every actor already pays.

3. **Message-passing would add the exact hazard that has bitten this project twice.** A
   request/response between actors introduces **ordering**: who asks first, whose answer
   lands in which tick, and what happens when the responder dies between request and
   reply. Both logged desyncs here are ordering/mutation-path failures —
   `LaneAmbushBotModule`'s unordered condition grant and `Detectable`'s `[Sync]`ed token
   (`WORKSPACE/bugs/discovered.md:538,556`). A pull-model array read has no ordering at
   all: it is a pure function of state both clients already agree on.

**What the request mechanism buys that a direct query does not: nothing.** The user's
underlying instinct — that this is deterministic and can piggyback on work already being
done — is exactly right, and it is *more* right than they realised: the work is not just
already being done, its result is already public.

## A.4 Q4 — the interval is a good idea, for cost, and the phase source is settled

Correct on both counts: sampling every N ticks is a real saving, and the phase must come
from a deterministic per-actor source.

**The house idiom already exists**, `AutoTarget.cs:1072`:

```csharp
return (self.World.WorldTick + (int)(self.ActorID % (uint)interval)) % interval == 0;
```

`ActorID` is synced identity, `WorldTick` is synced time — every client agrees on which
tick a given unit samples. Four other traits use the same shape (`AutoSeekSupplies.cs:148-149`,
`AffectsMapLayer.cs:183`, `ExternalCondition.cs:212`, `DropsSupplyCache.cs:124`). **Never
seed the offset from spawn-time RNG**; `ActorID` is the answer and it is already the
convention.

**Staleness cost.** At N=15 (≈0.6 s at 25 fps) a unit can be spotted for up to 15 ticks
before it knows. Weigh that against the ladder: one strength step is ~3 cells, and a
rifleman crosses ~3 cells in appreciably more than 15 ticks, so **N=15 loses well under
one strength step of warning for ground units**. Aircraft cross rings far faster and
would want a shorter N or no interval at all. For the *indicator* alone, staleness is
nearly free. For *behaviour*, it is the difference between stopping in time and not.

## A.5 Q5 — the counterfactual, honestly costed

"Stop instead of move, if they would be spotted if they moved" is a counterfactual, and
the manager is right that it is a different question. But it decomposes into a cheap part
and an expensive part, and **the cheap part captures the user's own example almost
exactly.**

**The cheap version — one extra comparison, no probing.** The move penalty is a property
of the *actor*, not the destination: `moving` grants a flat −1. So "would moving expose me
right here" is:

```
ResolvedVisibility[myCell] > CurrentVisibility - 1
```

Same array read already being done, one subtraction. **Zero additional cost.** This
answers "I am one step from being seen and moving would cross it" — which is precisely
the user's scenario, because their example is about the *movement penalty* tipping the
balance, not about walking into a better-observed cell.

**The expensive version — a true per-destination test.** Two components, very different
prices:

- *Visibility at the destination cell*: cheap. One `ResolvedVisibility[destCell]` read per
  candidate per enemy player. Even a 9-cell neighbourhood is trivial.
- *Cover at the destination*: **expensive, and not a pure function.** The cover term is
  `object-proximity`, an `ExternalCondition` (`infantry.yaml:706-707`, `TotalCap: 3`)
  granted by `ProximityExternalCondition` emitters on *other* actors — husks and wrecks
  (`husks.yaml:118+`). There is no function to ask "what would my cover be at cell X"; you
  would have to scan emitters near the destination and replicate their granting logic.
  Likewise `dugin` depends on having been still for 200 ticks, which a hypothetical move
  destroys by definition.

**So the cost is dominated by cover re-evaluation, not by the visibility lookup** — which
is the opposite of what one would guess, and is the reason to prefer the cheap version.

**Recommendation: ship "don't start moving while almost-spotted" and do not build the
per-destination test.** It is free, it matches the user's stated example, and it fails
safe (a unit that stays put stays hidden). The expensive version buys "move *there*
instead, it's darker" — a genuinely different and more ambitious feature, and one that
`CohesionMoveModifier` already approximates from the terrain side (§A.7).

## A.6 Two traps that will produce a wrong indicator

**Trap 1 — the sign is inverted from the user's mental model.** `VisionModifier` is added
to the *required* threshold, so **higher `Vision` = harder to see**. "Taking actions
increases their visibility" is therefore a **negative** `VisionModifier` (firing is −2,
moving is −1). An implementer who reads `Vision` as "how visible I am" builds an indicator
that lights up when the unit is *safest*.

This is not hypothetical confusion: **the shipped unit description already uses the
opposite convention from the field.** The sniper's tooltip says *"Low visibility (−2
detection)"* (`infantry.yaml:1620`) while its trait is `Vision: 5` — the highest, i.e.
stealthiest, in the mod. Description and field disagree in sign. Player-facing copy for
this feature should be written against the *field*, and the tooltip convention should not
be copied.

**Trap 2 — `ResolvedVisibility == 1` means "explored", not "faintly observed".** In the
resolve loop (`MapLayers.cs:241-256`), any **explored** cell floors at 1 even with no live
observer:

```csharp
if (explored[index]) {
    for (...) { ... }          // no source found → visibility stays 0
    if (visibility <= 0) visibility = 1;
}
```

The spotted *boolean* is safe from this, because `CurrentVisibility` is clamped to ≥1
(`Detectable.cs:80-84`) so `RV > CV` needs `RV ≥ 2`. **But an "almost spotted" band is
not safe.** Standard infantry (CV = 3) with a two-step band would fire on `RV ∈ {1, 2}` —
and `RV == 1` is true of **every explored cell on the map, with no enemy anywhere near**.
That ships a permanently-lit indicator.

**The band must require `RV >= 2`** (or equivalently ignore the explored-floor). This is
the single most likely way to get this feature visibly wrong.

## A.7 Q6 — is "almost spotted" the abandoned gradient?

**No, and the code makes the distinction natural rather than awkward** — they are
different quantities, not different resolutions of one quantity.

- `^VisibilityPips` (dead, `infantry.yaml:841-928`) rendered `visibility-N`, the
  conditions `Detectable` grants from its **own** `CurrentVisibility`
  (`VisionDetectableConditionPrefix = "visibility-"`, `Detectable.cs:44,162`). That is a
  property of **self alone** — "how stealthy I am". It does not change when an enemy walks
  toward you. It is not actionable, which is very likely why it was abandoned.
- "Almost spotted" is `RV − CV`: a **relation between self and a specific enemy player's
  vision at this cell**. It moves as enemies move. It is actionable — that is the whole
  point.

So the user is not contradicting their earlier steer. "Spotted means spotted" governs the
*spotted* axis, and it still holds: one boolean, drawn or not. "Almost spotted" adds a
**distinct pre-detection state** on a different axis — the one where you can still do
something about it.

**But there is a real way to slide back into the abandoned design, and it should be said
plainly:** `margin` is an integer over a 10-step ladder. If more than one "almost" level
is ever rendered, that **is** a severity gradient, and it is the thing that was already
built and thrown away. The honest recommendation is **exactly one pre-detection state** —
three discrete states total: *hidden* (nothing drawn), *almost* (one mark), *spotted* (the
other mark) — with the band width a single tunable constant that nobody surfaces to the
player.

**One consequence worth flagging for the render implementer already working from round
one:** "almost spotted" is a *second* mark on the same axis. §3.1 of the original report
warned that `Top` is already contested and rows are hand-placed with no automatic
stacking. Two mutually-exclusive states can share one anchor (only one is ever drawn), so
this costs one lane, not two — but it should be one `WithDecoration` pair on a shared
margin, not two independently-placed rows.

## A.8 What already reacts, and where the gap actually is

Two existing behaviours sit right next to what the user is asking for.

**Ambushers already refuse to be repositioned.** `StancePositioningExecutor.cs:305-323` —
a unit in Ambush or HoldFire opts out of repositioning entirely, with the rationale that
walking it off its cell "silently defeats a human ambush placement (the un-ambush bug)".
So "an ambusher stays put" is already the shipped behaviour.

**Ambushers already seek concealment — but blind to the enemy.**
`CohesionMoveModifier.cs:1070-1080` enables `RefineSlotsForConcealment` for human Ambush
units on group moves ("hide my ambushers in the trees"), scored by `ConcealmentScore`
(`:338-346`). That function's own comment names the exact gap:

> *"Viewer-independent by necessity: at order time there is no enemy position, so we score
> 'how deep in shadow this cell sits' rather than shadow along one sightline."*

**That is precisely the limitation `ResolvedVisibility` removes.** The existing pass is
viewer-independent because it runs at *order* time; reading `ResolvedVisibility` at *tick*
time is viewer-dependent by construction, and it already has terrain shadow baked in
(§A.1). The gap is not capability — it is that nothing reads the number.

## A.9 Staged answer

**Free today — already built, nothing to write**
- The graded scale (`ResolvedVisibility`, 0–10, synced, public) and the required threshold
  (`Detectable.CurrentVisibility`, `[Sync]`, clamped 1–10).
- Firing (−2), moving (−1), prone/dugin (+1), cover (+1..+3), rank (+1..+4) — tuned and live.
- Stop-to-hide: `moving` drops on halt, `dugin` (+1) accrues after 200 still ticks.
- Ambushers already decline repositioning.
- The deterministic per-actor sampling idiom (`ActorID % interval`).

**An afternoon**
- Read the margin: `enemy.MapLayers.ResolvedVisibility[cell] − CurrentVisibility`, over
  enemy players, on an `ActorID`-phased interval. Render-only, zero desync exposure
  (decorations already read `RenderPlayer` and write nothing — §1.3 of round one).
- The "almost spotted" mark, as one extra mutually-exclusive state on the spotted lane.
- The cheap counterfactual — "don't start moving while within one step" — one extra
  comparison against `CurrentVisibility − 1`, no destination probing.

**A project**
- True per-destination hiding ("move *there*, it's darker"), because destination **cover**
  is not a pure function — `object-proximity` comes from external emitters and `dugin` from
  elapsed stillness. The visibility half is cheap; the cover half is not.
- Knowledge-limiting any of this to "enemies we are aware of" — still §2.3 of round one:
  `BeliefStore` has no behavioural consumer and adding one needs the per-player opt-in gate
  so `@stable` is not mutated.
- Anything that grants a **condition** from a visibility change. That is the
  `Detectable`/`LaneAmbush` desync shape (§2.4, round one). If behaviour must key on this,
  read the trait directly in C#, as `StancePositioningExecutor` deliberately does rather
  than taking a `RequiresCondition` on stance (`:314-319`).

## A.10 What I did not verify

- **Still never launched the game.** No visual confirmation of anything, including whether
  a second mark on the spotted lane reads clearly.
- **I did not measure the per-tick cost** of the margin read. I argue it is cheap by
  analogy — it is an array index against `Detectable`'s existing per-tick recompute — but
  I did not profile it, and `ResolvedVisibility` is a `ProjectedCellLayer` whose indexing
  I read but did not benchmark.
- **The N=15 staleness argument in §A.4 is arithmetic, not measurement.** I did not check
  actual unit speeds against ring widths; "well under one strength step" is a claim about
  3-cell rings and infantry pace that deserves one real check before it sets a constant.
- **I did not confirm the `moving` condition is granted on every actor class** that would
  want this — I verified infantry (`:138-141`), vehicles (`vehicles.yaml:315`) and aircraft
  (`aircraft.yaml:156`), but did not audit the full actor list, and `^DetectableInfantryStandard`
  is an infantry-only template. Vehicles have `Detectable: Vision: 1` and **no**
  firing/moving modifiers that I found — so on current YAML this feature is **substantially
  an infantry mechanic**, and the user should know that before designing around it.
