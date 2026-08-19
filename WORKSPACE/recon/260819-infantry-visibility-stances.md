# Infantry visibility & stances — what the game does today

**Date:** 2026-08-19 · **Branch:** `wt/visibility-doc` · **Base:** `main @ 66fd33d3` (level with `origin/main`)
**Status:** read-only research. No code changed. **Game never launched** — no launch budget exists for this
work, so every claim below is read from source. Anything that needs play to settle is marked
`[NEEDS RUN]` and collected in §9.

**Destination:** this is research, in `WORKSPACE/` deliberately. It is written in the shape of a
`DOCS/reference/` page so promotion is a straight move, but promotion is a separate verified step —
the curated tree is cited without re-verification, so nothing enters it unverified.

---

## 0. The short answer

The player's report is *"I can't really get them to hide, and in ambush stance that should take care of
itself."*

**Hiding works. Ambush stance is not how you do it, and nothing on screen tells you when it is working.**

Four findings, in the order they matter:

1. **No stance changes how visible a unit is. Not one.** Detectability is moved by five things —
   moving, firing, prone, dug-in, and nearby cover objects — and stance is absent from all five.
   (§3, verified by exhaustive grep of every `DetectableAddativeModifier` in the mod.)

2. **Ambush stance is the one stance that switches OFF the game's only automatic take-cover
   behaviour.** `StancePositioningExecutor` repositions idle units onto cover-edge cells and it *is*
   live for human players — but it refuses to manage any unit below FireAtWill, which means Ambush
   and HoldFire opt out (`StancePositioningExecutor.cs:318,587`). The user set the stance he
   reasonably believed was the "hide" stance, and thereby disabled the hiding. (§4.2)

3. **Ambush's actual response to being seen is to open fire**, which costs −2 detectability and makes
   the unit *more* visible (`AutoTarget.cs:749-762`, `infantry.yaml:727-729`). Being spotted is the
   ambush *trigger*, and it latches terminally. There is no code path anywhere that makes a unit
   break contact, go prone, or re-conceal on being spotted. (§4.3, §5)

4. **The lever that does work is invisible and is not a stance: stand still and don't shoot.** A
   moving rifleman is seen from 25 cells; the same rifleman standing still for 12 seconds is seen
   from 16 cells. That is a 9-cell improvement, applied automatically, reported by nothing. (§3.4)

The user's hedge — *"maybe I am missing how I am supposed to do it"* — is half right. He is missing
it, but not because he failed to find a control. **There is no control.** Concealment is entirely
automatic and entirely unreported, and the one stance he reached for actively works against it.

---

## 1. Two different things are both called "Vision". This is the trap.

Everything downstream is confusing until this is settled, and the codebase itself gets it wrong in
player-facing copy.

| Trait | Field | Meaning | Direction |
|---|---|---|---|
| `Vision` (`^StandardVision`, `defaults.yaml:47-84`) | `Strength`, `Range` | How far and how strongly **this unit sees others**. Observer side. | higher = better eyesight |
| `Detectable` (`Detectable.cs:22-25`) | `Vision` | How much observer strength is **required to see this unit**. Target side. | **higher = stealthier** |

`DetectableInfo.Vision` is documented in the source as *"What level of vision is required to detect
this actor"* (`Detectable.cs:24`). So a **positive** `VisionModifier` makes a unit **harder** to see,
and a **negative** one makes it **easier**. Firing is `−2`; prone is `+1`.

**This sign is already miswritten in shipped player-facing text.** The sniper's tooltip says
*"Low visibility (−2 detection)"* while its trait is the highest — i.e. stealthiest — value in the
mod. Any copy written for the player must be written against the field, not against that tooltip.

Throughout this document I write **`CV`** for `Detectable.CurrentVisibility` — the live, per-tick,
`[Sync]`ed value of "strength required to see me" (`Detectable.cs:62-63,78-93`).

---

## 2. How being seen is decided

### 2.1 The reveal rule is one comparison

`MapLayers.IsVisible` (`engine/OpenRA.Game/Traits/Player/MapLayers.cs:574-579`):

```csharp
return ResolvedVisibility.Contains(puv) && ResolvedVisibility[puv] > visibility;
```

`ResolvedVisibility[cell]` is, per player, **the highest vision strength any of that player's sources
carries on that cell** (`MapLayers.cs:242-256`). So:

> **You are seen when some enemy source puts a strength on your cell that is strictly greater
> than your CV.**

`Detectable` feeds its CV into that comparison at `Detectable.cs:102-118`, clamped to **[1, 10]**
(`:104-107`; the ceiling is `MapLayers.VisionLayers - 1`, `MapLayers.cs:75`).

### 2.2 Strength is distance, in ten bands

`^StandardVision` (`defaults.yaml:47-84`) is ten concentric annuli:

| Strength | 10 | 9 | 8 | 7 | 6 | 5 | 4 | 3 | 2 | 1 |
|---|---|---|---|---|---|---|---|---|---|---|
| Reaches out to | 4c | 7c | 10c | 13c | 16c | 19c | 22c | 25c | 28c | 32c |

Combining with §2.1 — you need strength `CV+1` to be revealed, so the outer edge of band `CV+1` is
the distance at which an enemy with standard vision first sees you:

| Your CV | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| **Seen from** | 28c | 25c | 22c | 19c | 16c | 13c | 10c | 7c | 4c | **never** |

At **CV 10 a unit is invisible to any standard-vision observer**, because no band carries strength 11.
*(INFERRED — derived from the two code sites above plus the YAML ladder; I did not observe it in play.)*

### 2.3 Terrain subtracts from strength along the sightline

`MapLayers.AddSource` (`MapLayers.cs:357-375`):

```csharp
var modifiedStrength = strength - shadowModify;
if (modifiedStrength < 1) modifiedStrength = 1;
```

`shadowModify` comes from a per-map shadow layer built from tree density between viewer and target
(`Map.cs:1126-1181`). **Note the floor of 1** — terrain can never take a sightline below strength 1.

The curve is superlinear past a knee at density 20 (`Map.cs:1083,1102-1121`):
`ceil(d/10)` below the knee, `2 + ceil((d−20)/5)` above it. Dense woodland is disproportionately
better than a treeline. Cells at each end are skipped — only density strictly *between* viewer and
target counts (`Map.cs:1152-1155`).

**Trees are actors carrying `Building: Density`, not painted terrain** (`decoration.yaml`). Density
per cell: ordinary trees 10, `T08` 5, `T15` **15**, tree clumps `TC01`–`TC05` 5–10 over 3–7 cells,
tank traps **20**, desert rocks **50**. No structure, no civilian building and no husk carries
`Density` — **buildings do not conceal.**

### 2.4 The shadow layer is frozen at map load — dead trees still block sight

Settled, having been an open question in the July recon. Dynamic recalculation is commented out in
three places with an explicit rationale:

- `Building.cs:372-383,391-397` — add and remove recalc both disabled: *"Dynamic shadow recalc
  disabled 260503 … Shadows are computed once at map load … and stay frozen. The recalc was too
  expensive to run mid-game (visible lag on building destruction)."*
- `World.cs:512-517` — the per-tick flush is commented out.
- `Map.cs:976-1001` — `SetDensityLayer` iterates the **map file's** `ActorDefinitions`, not live world
  actors.

**Consequences the player can feel:** burning down a forest does not open a sightline; and any tree
placed by a script or reinforcement never contributed shadow in the first place.

---

## 3. What actually changes how visible your infantry are

### 3.1 The complete list

Every `DetectableAddativeModifier` that applies to infantry. This list is exhaustive — a whole-repo
grep found no others.

| Modifier | Condition | Effect on CV | Player-controlled? | Site |
|---|---|---|---|---|
| Moving | `moving` | **−1** (easier to see) | **yes — indirectly**, by not ordering a move | `infantry.yaml:730-732` |
| Firing | `firinganyweapon` | **−2** (easier to see) | **yes — indirectly**, via HoldFire | `infantry.yaml:727-729` |
| Prone | `prone` | **+1** | **no — automatic** | `infantry.yaml:716-718` |
| Dug in | `dugin` | **+1** | **no — automatic** after 200 still ticks | `infantry.yaml:719-721` |
| In cover ×1/2/3 | `object-proximity == 1 / == 2 / >= 3` | **+1 / +2 / +3** | **no — positional** | `infantry.yaml:707-715` |
| Veterancy rank 1–4 | `rank-veteran == 1..4` | **+1 … +4** | no — earned | `defaults.yaml:211-222` |

Base `Detectable: Vision: 3` for all infantry (`infantry.yaml:95-96`), inherited via
`Inherits@Visibility: ^DetectableInfantryStandard` at `infantry.yaml:21`.

Cover is capped: `ExternalCondition@ObjectProximity` carries `TotalCap: 3` (`infantry.yaml:704-706`).

### 3.2 Prone and dug-in are automatic, and the trigger is "stopped"

`InfantryStates.ProneCondition` (`infantry.yaml:294`):

```
deployed || suppressed > 30 || !moving || critical-damage
```

**`!moving`** — so **infantry are prone whenever they are standing still.** Prone is not a command and
has no button; it is what a stopped soldier is.

`GrantConditionOnMovement` (`infantry.yaml:138-141`) grants `moving` while moving and
`ConditionWhenStill: dugin` after `TimeToBeStill: 200`. At the default `Timestep: 60` ms
(`mod.yaml:380-382`) that is **12.0 seconds** of standing still.

So stopping is worth `+1` immediately (prone), a further `+1` at twelve seconds (dug in), and it
removes the `−1` of `moving`. **A three-point swing, entirely automatic.**

### 3.3 Cover comes from wrecks, not from living trees

`object-proximity` — the `+1/+2/+3` cover bonus — has exactly one emitter in the whole mod:
`ProximityExternalCondition@ObjectProximity` on `^TreeHusk` (`husks/husks.yaml:118-121`,
`Range: 384`, tightened per-actor to `Range: 182` on some, e.g. `:128-130`).

**A living tree gives you shadow attenuation but no cover bonus. A burnt-out one gives you the cover
bonus.** *(READ: the grep for `object-proximity` across `mods/` returns exactly two lines — the husk
emitter and the infantry receiver.)* This is almost certainly not intended, and it inverts the
player's natural instinct: you hide *in* the forest for shadow, but the `+1..+3` only arrives after
something has burned.

### 3.4 The numbers a player would actually feel

A plain, unranked rifleman (`Vision: 3`) on open ground, against an enemy with standard vision:

| What the soldier is doing | CV | Enemy sees him from |
|---|---|---|
| Running, firing | 3 −1 −2 = 0 → **clamped to 1** | **28 cells** |
| Running | 3 −1 = **2** | **25 cells** |
| Stopped, under 12 s (prone) | 3 +1 = **4** | **19 cells** |
| Stopped 12 s+ (prone + dug in) | 3 +1 +1 = **5** | **16 cells** |
| Stopped 12 s+, then fires | 5 −2 = **3** | **22 cells** (for 12 ticks) |
| Stopped 12 s+, beside 3 husks | 3 +1 +1 +3 = **8** | **7 cells** |
| …and rank 4 | 12 → **clamped to 10** | **never** |

*(INFERRED throughout — arithmetic over §2.2 and §3.1, not observed. `[NEEDS RUN]` — see §9.1.)*

Two things fall out of this table:

- **Concealment is real and worth a lot.** Standing still is nine cells. Fighting from wrecks is
  another nine. The user's *"it is really hard to stay hidden"* is a fair description of the *open
  ground* rows, not of the mechanic as a whole.
- **Firing costs six cells for 12 ticks** (`RevokeDelay: 12`, `infantry.yaml:723-726`) — about
  0.7 s at default speed. A dug-in ambusher gives away nine cells' worth of concealment the instant
  it shoots, and that is the state the game puts it in automatically the moment it is spotted (§4.3).

### 3.5 Vehicles have no levers at all

`vehicles.yaml:66` declares a bare `Detectable:` with no `Vision:` override, so the default of **2**
applies (`Detectable.cs:25`) — seen from 25 cells, permanently. `^DetectableInfantryStandard` is
inherited only by `^Infantry` (`infantry.yaml:21`), so **no vehicle carries a single movement, firing,
prone or cover modifier.**

*(Caveat: `vehicles.yaml:12` carries `Inherits@Vision: ^StandardVision`, which is the **observer**
side — how far the vehicle sees. It does not touch its detectability. I did not audit every
individual vehicle actor for a per-actor `Detectable: Vision:` override.)*

**This is substantially an infantry mechanic**, but the new indicators are attached to all three
selectable classes (§6.1) — so a vehicle can show the red "!" while having nothing whatsoever the
player can do about it.

---

## 4. Stances

### 4.1 What the four axes are

All four live on `AutoTarget`, enums at `AutoTarget.cs:22-28`, all synced and order-driven, all
settable from the UI.

| Axis | Values | Default | Order string | UI panel |
|---|---|---|---|---|
| Fire | HoldFire, Ambush, **FireAtWill** | FireAtWill (`AutoTarget.cs:75`) | `SetUnitStance` | `ingame-player.yaml:353` |
| Engagement | HoldPosition, **Defensive**, Hunt | Defensive (`:167`) | `SetEngagementStance` | `:422` |
| Cohesion | Tight, **Loose**, Spread | Loose (`:189`) | `SetCohesion` | `:491` |
| Resupply | Hold, **Auto**, Evacuate | Auto (`:196`) | `SetResupplyBehavior` | `:560` |

### 4.2 Stance does not touch detectability — and Ambush disables the cover-seeking

**No stance appears in any `DetectableAddativeModifier` in the mod.** Every instance is keyed on
`object-proximity`, `prone`, `dugin`, `firinganyweapon`, `moving`, `rank-veteran` or `!airborne`
(§3.1). The `stance-ambush` and `stance-holdfire` conditions are granted
(`defaults.yaml:309-310,570-571,670-671,682-683`) and consumed by **nothing** — zero `RequiresCondition`
sites in `mods/`. Setting Ambush changes no number that decides whether you are seen.

Worse than neutral:

`StancePositioningExecutor` is the game's only automatic take-cover behaviour. Its header
(`StancePositioningExecutor.cs:1-14`) describes it as nudging an **idle** unit *"to a threat-facing
cover-edge cell within a bounded leash"*, choosing by `CoverQuality` (`:559-575`).

**It is live for human players.** `defaults.yaml:42-45` grants `enable-tactical-positioning` to every
human-owned combatant — `GrantConditionOnHumanOwner@tacpos`, commented *"Phase-3 human enablement
(RATIFIED default-ON)"*. (The prose two lines above it at `:24-25` still describes this as future
work; the grant is what ships.)

And it refuses to manage Ambush or HoldFire units (`:318`, predicate at `:587-590`):

```csharp
public static bool FireStanceAllowsRepositioning(UnitStance stance)
{
    return stance >= UnitStance.FireAtWill;
}
```

The rationale in the comment at `:305-317` is sound in its own terms — *"walking it off its chosen
cell silently defeats a human ambush placement (the un-ambush bug)"*. **But the net effect for a
player who has not read the source is exactly backwards from the label on the button: choosing
Ambush turns the automatic cover-seeking off.**

Two further opt-outs on the same path: `HoldPosition` (`:325-332`) and `deployed` (`:498-506`). And
it is idle-only by design (rule S5, `:29-31`) — **it never touches a moving unit**, so it cannot
produce the "running soldier stops and takes cover" behaviour the user described.

### 4.3 What Ambush actually does

Real, and all of it about *when to shoot*, not about being seen:

| Behaviour | Site |
|---|---|
| Idle ticks route to a pre-aim / hold-fire state machine instead of scan-and-attack | `AutoTarget.cs:681-684,694` |
| Turrets rotate onto the target **without firing** | `AutoTarget.cs:743-745` |
| **Being spotted springs the ambush — the unit attacks** | `AutoTarget.cs:749-762` |
| Springing wakes Ambush allies within `AmbushCoordinationRadius` (**10 cells**, `AutoTarget.cs:86`) | `AutoTarget.cs:758-760,970,977-978` |
| Taking damage also springs it | `AutoTarget.cs:666-669` |
| Garrisons hold their soldiers inside until sprung | `GarrisonManager.cs:701,704,1171-1176` |
| Ambush units never walk off to resupply (*"it would give away the position"*) | `SupplyHuntMath.cs:74-75` |
| Opted out of auto-repositioning | `StancePositioningExecutor.cs:318` |

The spring is this, at `AutoTarget.cs:749-762`:

```csharp
var isSpotted = self.CanBeViewedByPlayer(targetOwner);
...
if (isSpotted || ambushTriggered) { ambushTriggered = true; ... Attack(target, false, scanSource); }
```

**Being seen is the trigger to open fire.** And it is terminal — the SPRUNG latch is not cleared until
the stance is reset (`AutoTarget.cs:735-739,988`). So the sequence when an ambusher is spotted is:
spotted → fires → gains `firinganyweapon` → **−2 CV** → is now visible from six cells further away.

**The one genuine concealment benefit of Ambush**, and it is real: on a **group move order**, a
**human**-owned, **Ambush**-stance, **non-Tight**-cohesion squad has each formation slot re-seated onto
the most tree-dense nearby cell — `RefineSlotsForConcealment` (`CohesionMoveModifier.cs:1079,1182-1183`),
scored by summed tree density through the same superlinear curve (`ConcealmentScore`, `:330-346`).

Its own comment names its limits: *"bounded, order-time only, no autonomous follow-up"* (`:1177-1178`)
and *"degrades to identity on open terrain (ConcealmentScore is 0 everywhere → no slot moves)"*
(`:1076-1077`). So it fires once, when you click, and does nothing if there are no trees.

**The full recipe for the one working ambush-concealment feature is: be human, set Ambush, set cohesion
to Loose or Spread, select more than one unit, and issue a move order near woodland.** Nothing in the
game says any part of that. *(READ in code; `[NEEDS RUN]` to confirm it is perceptible — §9.2.)*

### 4.4 Halt-before-contact exists, and the player cannot have it

`AttackMoveActivity.cs:154-171` halts an advancing unit *before* it walks into contact, dropping it
into the idle ambush state instead of engaging. This is close to what the user described.

It requires the `enable-ambush-tactics` token (`:154-158`). The token ships and **is** granted
(`defaults.yaml:312-320,344-345`) — but the YAML comment states the reach precisely:

> *"The grant is PER-UNIT: only a unit an ambush module actually posted carries the token, so
> GetConditionCount is still 0 — and the halt branch still dead — on every unit no ambush module
> posted, and on humans / Normal / Rush / Turtle / legacy, which never instantiate the module at all."*

The sole granter is `LaneAmbushBotModule`. **There is no UI path.** And a second restriction applies
even to units that have it (`AttackMoveActivity.cs:153-154`): *"Plain player Move never enters this
activity (it is a bare Move) … a plain Move is always obeyed; only attack-move / bot auto-move can
halt."*

So the behaviour closest to the user's request is built, tested, live — **for the AI only.**

### 4.5 The other axes, briefly

**Engagement** decides whether a unit will *move* to fight: only Hunt grants `allowMove` when engaging
(`AutoTarget.cs:650,689,1049`); HoldPosition aborts any auto-attack needing movement (`Attack.cs:281`)
and disables repositioning (`StancePositioningExecutor.cs:325`). Hunt also returns fire at an attacker
it cannot see, which the lower stances refuse (`AutoTarget.cs:650-652`).

**Cohesion** is spacing (`CohesionMoveModifier.cs:264-292`), and matters here only because **Tight
disables the ambush concealment pass** (§4.3).

**Resupply** governs rearm behaviour (`AmmoPool.cs:242-266`) and is not a visibility control.

---

## 5. What changes in a unit's behaviour when it is spotted

Directly, because the user asked about behaviour rather than rendering. The honest answer is short.

| Reaction | Exists? | Site |
|---|---|---|
| Ambusher springs and opens fire | **yes** | `AutoTarget.cs:749-762` |
| Ambusher's neighbours spring too | **yes** | `AutoTarget.cs:758-760,977-978` |
| Advancing ambusher halts before contact | **yes — AI-only** (§4.4) | `AttackMoveActivity.cs:154-171` |
| Hunt unit returns fire at an unseen attacker | **yes** | `AutoTarget.cs:650-652` |
| Unit stops moving because it was seen | **no** | — |
| Unit goes prone because it was seen | **no** — prone keys on `!moving` only (`infantry.yaml:294`) | — |
| Unit seeks cover because it was seen | **no** — the cover executor is idle-only and threat-driven, never visibility-driven (`StancePositioningExecutor.cs:29-31`) | — |
| Unit retreats or breaks contact | **no** | — |
| Anything at all reads "am I about to be spotted" | **no** | — |

**Every reaction to being spotted is a decision to shoot. Not one is a decision to hide.**

The `Detectable` trait does broadcast the unit's stealth level as a condition every time it changes —
`visibility-0` … `visibility-12` (`Detectable.cs:157-163`). **Nothing consumes them.** The only
`RequiresCondition: visibility-N` sites in the mod are in `^DetectableRangeCircles`
(`infantry.yaml:734-840`), whose sole `Inherits` line is commented out at `infantry.yaml:285`.

---

## 6. What the new pips report

### 6.1 What shipped

Two commits: `f5634522` (the traits) and `6cb66e28` (margins). Wired at `defaults.yaml:811-827` on
`^UnitIndicators`, inherited by `^SelectableCombatUnit`, `^SelectableSupportUnit` and
`^SelectableEconomicUnit` (`:907,913,919`) — i.e. **all selectable units, not just infantry.**

| Mark | Glyph | Colour | Meaning |
|---|---|---|---|
| Spotted | **`!`** | red `FF4A3C` | an enemy **you are aware of** can see this unit |
| HoldFire | `X` | white | fire stance is HoldFire |
| Ambush | `A` | amber | fire stance is Ambush |
| HoldPosition | `H` | blue | engagement stance is HoldPosition |
| Hunt | `>` | orange | engagement stance is Hunt |

Both traits are **render-only** — they read their source trait directly, grant no condition and are
never read by any unit's decisions (`WithSpottedDecoration.cs:24-28`, `WithStanceDecoration.cs:33-36`).
`'!'` is deliberately reserved to the spotted mark; the stance glyphs avoid it
(`WithStanceDecoration.cs:59-60`). Only non-default stances draw.

### 6.2 What the red "!" actually means — three qualifications

`WithSpottedDecoration.IsSpotted` (`:82-120`) requires **all** of:

1. an enemy actor within `MaximumObserverRange` (32c, `:40`);
2. **that you can see yourself** — `observer.CanBeViewedByPlayer(viewer)` (`:105`). This is the
   asymmetry rule: an enemy watching you from inside your own fog does **not** light the mark, by
   design, because a true-visibility badge would be a wallhack (`:20-22`);
3. that observer's own vision bands carry strength ≥ your CV at your range (`VisionCovers`, `:136-157`);
4. and the truth gate `self.CanBeViewedByPlayer(owner)` (`:115`).

Three consequences the player should be told:

- **Absence of the "!" does not mean you are hidden.** It means no enemy *you can see* is seeing you.
  This is a deliberate, defensible choice — but it makes the mark unusable as the "am I concealed?"
  feedback the user is actually asking for.
- It is a **binary** — no severity, no distance, no "nearly spotted" (`:16-18`).
- It recomputes every 7 ticks (`:33`), so it can lag by ~0.4 s at default speed.

### 6.3 The measured number from the shipping run

`6cb66e28`'s message records the one piece of **observed** evidence in this whole area: units 5 cells
from a scout (its strength-9 band) were marked and units at 8 cells (strength-8) were not, so
**standard infantry needed strength 9 to be revealed** in that scenario — consistent with CV 8, i.e.
prone + dug-in + full cover. It is the only in-game data point available, and it corroborates §2.2's
derivation.

---

## 7. Gap analysis

Ranked with **legibility first**, per the user's steer. §7.1 is the group that can be answered by
showing the player what is already happening; §7.2 requires new behaviour.

### 7.1 Answerable by legibility alone — nothing about concealment need change

**G1 — Nothing reports whether a unit is concealed. This is the whole complaint.**
The player has no way to know that stopping moved him from 25 cells to 16, that firing cost him six
cells for 12 ticks, or that dug-in arrived at twelve seconds. He is operating a system with three
automatic levers and no gauge on any of them. The red "!" reports the *outcome*, late and
conditionally (§6.2), and never reports the *state*.

**G2 — A correct concealment gauge is already built and is one commented-out line from being live.**
`^DetectableRangeCircles` (`infantry.yaml:734-840`) draws, when the unit is selected, a circle at the
radius corresponding to its current visibility level, keyed on the `visibility-N` conditions
`Detectable` already grants. It is the direct answer to *"how close can they get before they see me"*,
and it would shrink visibly as a soldier stops, goes prone and digs in. Its only `Inherits` is
commented out at `infantry.yaml:285`.

> **It has an off-by-one and must not be shipped as-is.** The circles map `visibility-1 → 32c`,
> `visibility-3 → 25c`, `visibility-5 → 19c` — the outer edge of band `CV`. But reveal requires
> strength **strictly greater** than CV (§2.1), so the true radius is the outer edge of band `CV+1`
> — one band, roughly three cells, tighter. *(INFERRED from `MapLayers.cs:574-579` against the YAML
> ranges. `[NEEDS RUN]` — §9.1 settles both the table and this off-by-one at once.)*

**G3 — The stealth level is already synced and broadcast, and consumed by nothing.**
`Detectable` grants `visibility-0`…`visibility-12` on every change (`Detectable.cs:157-163`) with zero
live consumers (§5). Any indicator keyed on CV is a YAML change with no C# and no sync exposure.

**G4 — Two attempts at exactly this indicator have already been built and abandoned.**
`^VisibilityPips` (a 1–12 numeric badge) was deleted by `f5634522`; `^DetectableRangeCircles` survives
but is unreferenced. Whatever is built next should account for why both were dropped — the binary
steer explicitly rejected the numeric gradient, which leaves the **circle** as the surviving idea.

**G5 — Ambush's label promises what it does not deliver.** Even with no mechanical change, the
tooltip/description for Ambush should say that it governs *when the unit shoots*, and that it pins the
unit in place rather than concealing it.

### 7.2 Requires a behaviour change

**G6 — There is no react-to-contact anywhere.** Nothing stops, goes prone, seeks cover, or breaks
contact on spotting or being spotted (§5). The user's *"a running soldier that sees an enemy may stop
and take cover"* describes a behaviour that does not exist in any form. This is a feature gap, not a
tuning problem.

**G7 — Ambush disables the only automatic cover-seeking there is** (§4.2). Whatever else is decided,
this specific interaction is the mechanical core of the complaint, and it is a two-line predicate.

**G8 — The halt-before-contact behaviour is built and reachable only by the AI** (§4.4). This is the
cheapest possible route to G6 — the code exists, is tested, and is gated by a token with no human
grant path. *(Note the second gate: it only applies to attack-move, never a plain move.)*

**G9 — Cover comes from burnt husks, not living trees** (§3.3). The `+1..+3` bonus — the largest
single lever available — is emitted only by `^TreeHusk`. A player hiding in a live forest gets shadow
attenuation but no cover bonus. I believe this is a bug, but it is stated as a finding, not a
diagnosis.

**G10 — Vehicles have no levers** (§3.5) but do show the "!". Fixed CV 2, seen from 25 cells, nothing
the player can do.

**G11 — Killing trees does not open sightlines** (§2.4), and script-placed trees never concealed
anything. Frozen deliberately for performance; worth knowing before anyone designs around
deforestation.

### 7.3 The user's five candidate explanations, ranked by evidence

Not picking a favourite — here is where the evidence actually falls.

| # | Candidate | Verdict |
|---|---|---|
| 1 | **"It is automatic and there is no lever, so 'getting them to hide' is not a thing you can do"** | **Strongest, and directly evidenced.** Prone, dug-in and cover are all automatic; no stance touches detectability; the one player-facing concealment feature (§4.3) is an undocumented four-condition combination. §3.1, §4.2 |
| 2 | **"It works but nothing communicates it"** | **Equally strong, and independent of #1.** Zero live consumers of `visibility-N`; two abandoned indicators; the "!" reports the wrong thing for this purpose. §5, §6.2, §7.1 |
| 3 | **"It requires an action the user does not know about"** | **True but narrow.** The action exists (§4.3) and is genuinely undiscoverable — but it is one order-time pass near trees, not a general hiding mechanism. |
| 4 | **"Graded and too weak to feel"** | **Contradicted for stationary units, supported for moving ones.** 25c → 16c is not weak. But a unit under orders is permanently at CV 2, and the user plays by moving units. §3.4 |
| 5 | **"Genuinely broken"** | **No evidence.** Every link in the chain verified intact. The two things that behave unexpectedly — husk-only cover (G9) and frozen shadow (G11) — are real defects but neither explains the complaint. |

**#1 and #2 together explain the report completely, and they are the same finding seen from two
sides: the system works, operates without the player, and never says so.** That is precisely why the
legibility-first steer is the right call — it is not a workaround for an unfixable mechanic, it is
the correct primary fix.

---

## 8. The white "!" proposal, evaluated

The user proposes: red `!` = the enemy can see you; **white `!` = this soldier is deliberately not
acting normally, because it is hiding.**

**The semantic is good and the render cost is near zero. The state it wants to report does not
exist.**

There is no "blocked from acting normally in order to hide" state anywhere, because nothing hides
(§5). Building the indicator first would give a mark that never lights — or worse, one wired to a
proxy that means something else.

Three states **do** exist today and are renderable now:

| Candidate state | What it honestly means | Where | Fit |
|---|---|---|---|
| Ambush DORMANT — pre-aiming, holding fire, not yet sprung (`ambushTriggered == false` with a live pre-aim target) | *"holding fire on purpose"* | `AutoTarget.cs:694-762` | **Good.** Genuinely "not acting normally", genuinely deliberate, and it is the state a player in Ambush is most often in and least able to see. Not "hiding". |
| `StancePositioningExecutor.State` — public `AdjustmentState`, explicitly an ops-layer surface (`:46,177`) | *"repositioning itself to cover"* | `StancePositioningExecutor.cs:177` | **Good, and it is the closest thing to "taking cover" that exists.** Live for humans. Never for Ambush units — which is itself worth surfacing. |
| `haltedForAmbush` in `AttackMoveActivity` | *"stopped rather than walk into contact"* | `AttackMoveActivity.cs:163-168` | **Exactly the semantic requested — and unreachable by human players** (§4.4). |

**So the honest ordering is: the behaviour must exist before the indicator can.** Two routes:

- **If the answer is legibility-first (the ruled preference):** the white `!` should report the
  Ambush-DORMANT state, and be described as *"holding fire deliberately"* — not as *"hiding"*, which
  would be a promise the game does not keep.
- **If G6/G8 is ever taken up:** `haltedForAmbush` is already the exact state, already latched, and
  the white `!` reports it with no new state at all.

One rendering note carried forward: `'!'` in white would sit next to the white `X` HoldFire glyph at
8 px on the same lane (`defaults.yaml:812-822`, `WithStanceDecoration.cs:47`). The existing code
already reasoned about exactly this collision class (`WithStanceDecoration.cs:59-60`). Colour alone
may not separate them.

---

## 9. MANAGER: please run this

No launches were performed. Three runs, in priority order; **the first is worth more than the other
two together.**

### 9.1 Does the concealment table in §3.4 hold, and is the circle off-by-one real?

**Why:** §3.4 is the quantitative spine of this document and it is arithmetic, not observation. It is
also the number that decides whether "too weak to feel" (candidate #4) is alive.

**Setup:** one enemy observer with `^StandardVision`, stationary. One rifleman, unranked, on open
ground with no trees and no husks within 5 cells. Walk the rifleman toward the observer until the
"!" lights; note the distance. Then repeat in three states: (a) moving continuously, (b) stopped for
5 s, (c) stopped for 15 s.

**What counts as the answer:** three reveal distances. §3.4 predicts **25c / 19c / 16c**. Any result
within one band (±3c) confirms the model. A flat result across all three states would mean the
modifiers are not reaching `CurrentVisibility` at all, which would be a much bigger finding.

**Also free from the same run:** temporarily un-comment `infantry.yaml:285` and compare the drawn
circle against the measured reveal distance. If the circle sits one band (~3c) wide of where the "!"
lights, G2's off-by-one is confirmed.

### 9.2 Is the Ambush concealment pass perceptible?

**Why:** §4.3 is the only player-facing concealment feature Ambush has, and it is read from code only.

**Setup:** a woodland map. Select 5+ infantry, set **Ambush** and **Loose**, issue one group move
order to a spot beside a treeline. Compare against the same order in **Tight** (which disables it,
`CohesionMoveModifier.cs:1085-1090`).

**What counts as the answer:** whether the units visibly settle onto different, more tree-buried cells
in Loose than in Tight. If the two layouts are indistinguishable, the feature is inert in practice
and G3/#3 collapses to "there is no lever at all".

### 9.3 Does an ambusher become more visible the moment it springs?

**Why:** §4.3 predicts spotted → fires → −2 CV → visible from ~6 cells further. That is a
self-reinforcing loop and, if real, is a balance finding in its own right.

**Setup:** an Ambush rifleman dug in ≥12 s, an enemy scout approaching. Watch the moment of spring.

**What counts as the answer:** whether the unit remains revealed to observers that had lost it, for
~12 ticks after each shot.

---

## 10. Corrections to prior documents

Two prior recons are load-bearing here and **both contain claims that no longer hold.** Flagged
loudly, per the standing instruction.

### `WORKSPACE/recon/260728-trees-concealment.md` (against `main @ 33747425`)

- **Q4: *"Prone grants nothing"* — FALSE, and it was false at its own SHA.**
  `DetectableAddativeModifier@Prone` is present at `infantry.yaml:716-718` today and was present at
  `33747425` (introduced 2024-01-09). The same paragraph's *"Only additive-modifier user in the mod is
  veterancy"* misses five live modifiers. **This is the most damaging error of the set** — it points a
  reader away from the exact mechanic that answers the question.
- **The magnitude claim — *"~7 dense tree cells to hide a `Vision: 3` infantryman"* — BROKEN.**
  The shadow curve became superlinear past a knee at density 20 (`Map.cs:1083,1102-1121`), so the
  real figure is **3–5 cells** depending on the observer's band. The change landed the same day the
  recon was published; it was stale on arrival.
- **Its open question is now SETTLED, the unhelpful way.** Shadow is frozen at map load and dead trees
  still block sight (§2.4). The disabling pre-dated the recon by three months.
- Holds: trees as actors, densities, locomotor passability, `TerrainModifiesDamage` and `BlocksSight`
  both still dormant.

### `WORKSPACE/recon/260817-unit-indicators.md` (rounds one and two)

Substantially accurate — the graded quantity, the threshold, the action modifiers, the inverted sign
and the `RV == 1` explored-floor trap all verified. Four corrections:

- **§1.5 / §A.9: *"nothing grants `enable-ambush-tactics` by default"* is superseded.** Since
  `b8d2e601` (2026-08-02) `LaneAmbushBotModule` grants it per-unit on both bot profiles
  (`defaults.yaml:312-319`). The conclusion the player cares about is unchanged — **humans still never
  receive it** — but the reason is now "granted, never to you" rather than "granted to nobody".
- **§A.10: *"vehicles have `Detectable: Vision: 1`"* — vehicles declare a bare `Detectable:`
  (`vehicles.yaml:66`) and so take the default of 2** (`Detectable.cs:25`). The document's conclusion —
  that this is substantially an infantry mechanic — is correct and is reinforced here (§3.5).
- **§A.2 attributes the cover bonus to proximity generally; it is emitted by `^TreeHusk` only**
  (§3.3). Living trees do not grant it.
- Line drift only: the sniper firing modifier is at `infantry.yaml:1990-1992`, not `:2077-2079`.

---

## 11. What I did not verify

- **The game was never launched.** Every number in §3.4 and §2.2 is arithmetic over the YAML ladder
  and the reveal comparison. The single in-game data point available (§6.3) comes from `6cb66e28`'s
  commit message, not from me.
- **The off-by-one in `^DetectableRangeCircles` (G2) is inferred**, from `MapLayers.cs:574-579`
  against the YAML ranges. It is the kind of claim that should be measured before anyone acts on it,
  which is why §9.1 folds it into the primary run.
- **I did not audit every vehicle and aircraft actor** for a per-actor `Detectable: Vision:` override;
  §3.5 rests on the `^Vehicle` template and the absence of `^DetectableInfantryStandard` inheritance.
- **I did not verify that `prone` is granted with no delay** when a unit stops. `ProneCondition`
  contains `!moving` (`infantry.yaml:294`) and I assumed the condition system evaluates it on the
  tick `moving` is revoked. If there is a grant delay, the §3.4 "stopped, under 12 s" row is
  optimistic.
- **I did not check whether any map's `rules.yaml` or Lua grants `enable-ambush-tactics`** to human
  units, which would locally falsify §4.4. I checked `mods/rules/` only.
- **`Stage3EvaluateSpring`** (the gated widened-ambush state machine, `AutoTarget.cs:776+`) is
  AI-only and I read it only far enough to confirm humans never enter it. Its additional spring
  triggers are not documented here.
- **The claim that `StancePositioningExecutor` is live for humans rests on the YAML grant**
  (`defaults.yaml:42-45`) plus the trait's `RequiresCondition`. I did not confirm in play that human
  units actually reposition — and the prose comment immediately above the grant still describes it as
  future work, which is exactly the kind of contradiction that turns out to matter. **`[NEEDS RUN]`,
  and it is cheap to fold into §9.2:** watch whether idle human infantry in FireAtWill drift onto
  cover-edge cells at all.
