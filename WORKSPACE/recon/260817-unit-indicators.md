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
