# Playtest report — Stage B.1 demo, 2026-05-13

> User feedback from running `demo-layered-defence` after the
> reserve-driven B.1 revision. Verbatim observations below + my read
> + the fix priority I picked.

## Observations from the user

### O1 — TECN gets re-tasked off captures

> "TECN gets the order to capture structures, but then gets attack move
> orders after that cancels it. So they end up on the frontline with
> the other soldiers. Not good."

**My read.** TECN is not in `ScreenUnitTypes` or `MainLineUnitTypes` —
my code's type filter should skip it. But the user is observing
otherwise. Possible causes:

1. A name-case mismatch in the HashSet `Contains` (actor.Info.Name vs
   YAML strings). OpenRA generally lowercases actor IDs but defensive
   coding here would use `ToLowerInvariant()` consistently.
2. The cooldown isn't enough — TECN goes idle mid-capture chain (e.g.
   between Move and Enter activities), my module picks it up, attack-
   move overrides the Capture activity.
3. Some other module (SquadManagerBotModule? legacy?) is involved.

Safest fix: explicit `ExcludedActorTypes` HashSet on
LayeredDefenceBotModule, defaulted to TECN + Engineer + supply truck +
scouts. Belt and braces.

### O2 — Head-on grind, no tactics, no cover

> "it still feels like they just attack head on and do nothing to
> really capture defensable positions or areas of interest. They do
> not seem to seek cover. What I would like is for them to fight
> efficiently, like hold positions with cover, make the front line
> static and stable first, then we can add later more aggressive
> behaviours like breaking formation to storm enemy positions."

**My read.** This is exactly Stage B.2 in the existing spec —
treeline/garrison preference for the screen. B.1 sends infantry to
the contested cell directly; they end up standing in the open. The
doctrine says **screen** = "hidden in treelines, garrisoned to
buildings." This is the next concrete piece of the doctrine to land.

Sequencing per user: "static and stable first, then later more
aggressive." So cover/holding comes before any flanking/storming
behaviour. My current "fill the line + flank weak" emerges from
scoring, but the cover piece is needed to actually *hold* the line —
otherwise units take fire from buildings/treelines they should be
inside.

### O4 — Out-of-ammo units stay in the spearhead

> "Units out of ammo ends up getting the same attack orders as the
> others, they should retreat to a supply truck, or evacuate if that
> is not feasible. At least go backwards to take cover and not be
> part of the spearhead as useless cannon fodder."

**My read.** No bot module today checks ammo state when assigning
orders. An empty rifleman with 0 primary ammo gets the same
`AttackMove` to a contested cell as a full one, and dies useless.

The proper fix is a **rearm/retreat behaviour**:

1. If unit has `AmmoPool` and `CurrentAmmoCount == 0`, mark as "out".
2. Find nearest friendly SupplyProvider (TRUK, LC, possibly SR depending
   on Rearmable.RearmActors per unit).
3. Issue `Move` (or queued capture-style entry into the provider) to
   refill. Once refilled, unit goes idle and re-enters the assignment
   pool.

Minimum viable fix today: in `LayeredDefenceBotModule`, **skip** units
with primary AmmoPool empty. They won't get a forward order. They'll
sit where they are (still bad — they're cannon fodder in place — but
better than running into death). Active retreat logic is a follow-up
module.

### O5 — Empty supply trucks get the same forward orders

> "Supply trucks ends up where they are needed, but when they run out
> of supplies they need to go resupply but they are still ordered
> around as if they have supplies left."

**My read.** The existing `SupplyFollowerBotModule` tells TRUKs to
follow the army. It doesn't check whether the TRUK still has supplies
(`SupplyProvider.CurrentSupplyValue > 0`). An empty TRUK gets sent
forward, arrives at the screen, has nothing to give.

Fix: read `SupplyProvider` value from each TRUK; if 0, instead send
to the nearest `LogisticsCenter` to refill. Probably an extension of
the existing module rather than a new one.

**Not today** — needs to read the existing SupplyFollower carefully
and decide whether to extend or replace.

### O3 — Vehicles outrun infantry, infantry should be the baseline

> "Now in the early game all vehicles ends up on the front kind of
> waiting for the infantry to catch up. A priority in modern wars is
> to have soldiers on the front. They are the ones that holds the
> front stable. Vehicles are used to quickly provide fire superiority
> to where it is needed, but they dont sit on the front and hold it,
> they are used for their mobility more than their firepower. The
> firepower they provide is low compared to what they cost, in
> comparison to infantry. So the AI should prioritize infantry as the
> baseline and use vehicles to strengthen where it is needed at short
> notice"

> "Like if they fill vehicles with soldiers and carry them to the
> front to more quickly deploy them on the field."

**My read.** Two distinct asks:

1. **Mounted transport** — IFVs/APCs (Bradley, BMP-2, M113) should
   pick up infantry, drive to the front, drop them off, return for
   another load. This makes the infantry-vs-vehicle speed mismatch
   work *for* the doctrine instead of against it: vehicles deliver
   infantry faster, then go back to being mobile fire support.
2. **Doctrinal role**: vehicles **don't sit on the front holding it**
   — they're fire-superiority reserves that move to where pressure is
   highest. Infantry holds; vehicles answer.

Current code: vehicles are part of "Main Line" and sit at the
standoff position behind the screen. That's close to right per (2)
but they currently sit STATIC waiting for engagement, not
*mobile-reserve*. And they don't carry infantry forward.

This is a significant addition — call it **Stage B.4 — Mounted
transport** (sequenced after cover B.2 and possibly fields-of-fire
B.3). Write the spec; defer implementation.

## What's working

Worth acknowledging:

- The doctrine document itself (defence in depth + 3:1 + honest fog)
  reads correctly. The user's feedback is "we're not there yet," not
  "wrong direction."
- The InfluenceMap + frontline overlay is solid foundation — the user
  isn't questioning what the AI *sees*, only what it *does*.
- The reserve-driven assignment logic (B.1 revision) IS doing the
  spread-along-the-line thing; the issue is that the *targets*
  (contested cells, in the open, no cover) are wrong, not that the
  distribution math is wrong.

## Priorities I picked (in order)

### P1 — Fix TECN bug + ammo-out skip

Two-part defensive filter on `LayeredDefenceBotModule`:

1. Add `ExcludedActorTypes` HashSet defaulting to `{ tecn,
   tecn.america, tecn.russia, e6, e6.america, e6.russia, truk,
   humvee, btr }`. ToLowerInvariant the comparison.
2. If a unit has any `AmmoPool` with `CurrentAmmoCount == 0`, skip
   it. Don't send empty rifles into the spearhead.

Both are small filters that ship today. (O5 — empty supply trucks —
needs a follow-up because it lives in a different module.)

### P2 — Stage B.2: Cover preference for the screen

Modify the slot-targeting step:

1. For each screen-eligible reserve, find the highest-scoring
   contested cell as before.
2. Search cells within `CoverSearchRadiusCells` (~6) for cells whose
   terrain type matches `CoverTerrainTypes` (default: `Tree`, `Rough`).
3. If found, target the nearest cover cell (use map `GetTerrainInfo`).
4. Else target the contested cell directly (fallback).

This addresses the user's "make the front line static and stable
first" — infantry hide in treelines along the line. The contested
band still sits in the open between forces, but the screen now hugs
the cover available near it.

[planned] Garrisonable building variant: if a capturable / garrison-
ready building is within K cells of the slot, prefer garrison-enter
over move-to-cell. Possibly its own slice — depends on the
Garrisonable trait API.

Ship today after P1.

### P3 — Document Stage B.4: Mounted transport (spec only, defer implementation)

Substantial new feature; needs its own spec doc. Write
`WORKSPACE/ai/stage_b4_mounted_transport.md` describing the
load/transport/unload loop, eligible carrier/passenger pairs,
return-to-reserve behaviour, and the cargo trait API surface to
build on.

Don't implement today — too much surface area for one session and
P2 is the immediate user-visible win.

### P4 — Build-order tuning (NOT today)

The user wants infantry-heavy production. Existing UnitsToBuild
already favours infantry by raw priorities but vehicles are at 20-30,
infantry at 50-120. Could differentiate v2 with a heavier infantry
bias. **Holding off** because the user's actual frustration is
*positioning*, not *count*. Mounted transport (B.4) addresses the
speed-mismatch the bias would also address — fix the right thing.

### P5 — Engagement stance for the main line (NOT today)

Tanks should be mobile fire-support, not static holders. Per
doctrine: they reposition to where pressure is highest. Currently
they sit at the standoff position. **Holding off** because this
needs Stage C (reserve/pressure response) work — explicit
re-positioning module reading pressure deltas off the InfluenceMap.
B.4 mounted transport is closer in scope; do that next.

### P6 — Active rearm/retreat behaviour (deferred)

Beyond skipping empty units: a proper RearmBotModule that finds the
nearest friendly supply source and routes empties to it. Needs the
Rearmable trait info per unit (`RearmActors`), an iteration over
candidate sources, and probably its own scan loop. Defer with a spec
once cover (P2) is in.

### P7 — Empty supply truck redirect (deferred)

Extension of SupplyFollowerBotModule or a new module. Defer.

## Today's commits (target)

1. `ai: B.1 — exclude TECN/E6/TRUK/scouts + skip empty-ammo units`
2. `ai: B.2 — screen units snap to nearby treeline / cover cells`
3. `ai: B.4 spec — mounted infantry transport (deferred implementation)`
4. Demo update if needed.

Tournament batch not required for these — visual demo verification is
the acceptance per user (and the contested-arena map doesn't have
enough trees for B.2 cover to register clearly anyway; may need a
new demo-cover-defence with deliberate treeline placement).
