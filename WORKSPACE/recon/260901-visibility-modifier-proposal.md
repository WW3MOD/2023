# Items 7 & 8 — infantry/vehicle visibility: premise check and costed proposal

**Date:** 2026-09-01 · **Branch:** `wt/visibility-mods` · **Base:** `main @ 3248605a` (level with `origin/main`)
**Status:** recon + design. **No rule change made.** One comment-only correction (see §7).
**Game never launched; `--check-yaml`/`make test` never run** — both are manager-serialized.

---

## 0. Premise check — the answer, first

The handoff (`WORKSPACE/HANDOFF-260901.md` §E) reads the YAML gate's *"every vehicle grants unconsumed
`visibility-1 … visibility-10`"* as evidence that **a visibility-modifier scaffold already exists and is
wired to nothing**, and asks whether the task collapses to tuning it.

**That inference is wrong, and the truth is better in one half and worse in the other.**

The `visibility-N` conditions are not a modifier *input*. They are the engine's **output broadcast of the
level the unit has already reached** — `Detectable.DetectableVisionChanged` grants
`"visibility-" + CurrentVisibility` every time the level changes (`Detectable.cs:223-229`), declared for the
lint at `Detectable.cs:52-54`. Nothing computes *from* them.

Their one consumer in the whole repo is a **concealment gauge**, not a modifier:

- `^DetectableRangeCircles` (`mods/ww3mod/rules/ingame/infantry.yaml:808-908`) — ten `WithRangeCircle`
  entries, one per level, drawing a grey ring at the distance the unit is first seen from.
- **It is LIVE.** `Inherits@DetectableRangeCircles: ^DetectableRangeCircles` at `infantry.yaml:22` is
  uncommented and inherited by `^Infantry`. The 2026-08-19 recon recorded this line as commented out; it
  has since been enabled, and the off-by-one that recon flagged has been fixed.
- There is **no C# consumer at all** — the only three hits for the prefix across `engine/` are the
  declaration, the lint superset and the grant itself (`Detectable.cs:44,54,228`).

So the gate message is accurate and benign: **vehicles carry `Detectable` (`vehicles.yaml:66`), so the engine
grants them `visibility-N`, but vehicles do not inherit the circles, so nothing consumes them there.** Infantry
consume them; vehicles do not. That is the entire story behind the lint line.

**Consequence for this task:** there is **no modifier scaffold to tune.** Item 7 and item 8 are real work, not
a settings change. What *does* already exist and is worth knowing is a working concealment **gauge** on
infantry, and — the finding that reframes the whole request — a working **hard-concealment mechanism** in
forest (§3).

**One correction the handoff could not have known:** `mods/ww3mod/lint-baseline.txt` contains **zero**
`visibility` lines (`grep -c` → 0). Whatever the manager saw was live gate output, not a recorded baseline
entry.

---

## 1. How being seen is actually decided — re-verified at `3248605a`

Every number below is read from the cited line at this SHA. **Two of the three load-bearing facts changed
after the 2026-08-19 recon was written**, so that document's arithmetic must not be reused.

### 1.1 The observer ladder — unchanged

`^StandardVision`, `mods/ww3mod/rules/defaults.yaml:95-134`. Ten concentric annuli:

| Strength | 10 | 9 | 8 | 7 | 6 | 5 | 4 | 3 | 2 | 1 |
|---|---|---|---|---|---|---|---|---|---|---|
| Outer `Range` | 4c | 7c | 10c | 13c | 16c | 19c | 22c | 25c | 28c | 32c |

**The rungs are 3 cells apart.** This is the single most important number in this document (§4).

### 1.2 Reveal is NON-STRICT — changed 2026-08-20, one day after the recon

`MapLayers.IsDetected` (`engine/OpenRA.Game/Traits/Player/MapLayers.cs:600-603`):

```csharp
public static bool IsDetected(int resolvedVisibility, int concealment)
{
    return resolvedVisibility >= (concealment < 2 ? 2 : concealment);
}
```

Landed in `1ff73ae5` *"Reveal is non-strict: a matching observer detects, top of the ladder included"*
(2026-08-20). Previously the comparison was strict (`>`).

**Every reveal distance in the game moved 3 cells OUTWARD on that date.** The mod's own YAML comment states
it plainly (`infantry.yaml:799-801`): *"this ladder reused band N+1's Range while the comparison was strict,
and every circle moved out one band (~3 cells) when the comparison did."*

Note the second clause: **level 1 can never detect**, because `ResolvedVisibility` stamps 1 on every merely
*explored* cell, so the threshold floors at 2 (rationale inline at `:594-599`). Concealment levels 1 and 2
are therefore indistinguishable.

### 1.3 The concealment ceiling is 9, not 10 — also changed since the recon

`Detectable.ClampConcealment` (`engine/OpenRA.Mods.Common/Traits/Modifiers/Detectable.cs:118-125`) clamps to
`[1, VisionLayers - 2]`. `MapLayers.VisionLayers = 11` (`MapLayers.cs:75`), so the range is **[1, 9]**.

The gap is deliberate and unit-tested (`engine/OpenRA.Test/OpenRA.Mods.Common/DetectableCeilingTest.cs`): a
unit at the top of the ladder could be *matched* but never *exceeded*, making it undetectable at every range.
The tests pin `ClampConcealment(int.MaxValue) < VisionLayers - 1`.

**`visibility-10` is therefore unreachable, and the 4c ring at `infantry.yaml:900-908` is dead YAML.** That is
known and intentional — `Detectable.cs:49-51` keeps the level declared so the ring survives a revert of the
ceiling.

### 1.4 The resulting table — this supersedes recon §2.2/§3.4

Seen-from distance = outer `Range` of the band at strength `max(CV, 2)`:

| CV | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|
| **Seen from** | 28c | 28c | 25c | 22c | 19c | 16c | 13c | 10c | **7c** |

There is no "never". At the ceiling a unit is still seen from 7 cells on a bare sightline.

---

## 2. What the units carry today

### 2.1 Infantry — base 3, six modifiers

`Detectable: Vision: 3` at `infantry.yaml:96-97`. Modifiers via
`Inherits@Visibility: ^DetectableInfantryStandard` (`:21`), defined `:758-787`:

| Modifier | Condition | `VisionModifier` | Site |
|---|---|---|---|
| In cover ×1/×2/×3 | `object-proximity == 1 / == 2 / >= 3` | **+1 / +2 / +3** (`TotalCap: 3`, `:759-761`) | `:762-770` |
| Prone | `prone` | **+1** | `:771-773` |
| Dug in | `dugin` | **+1** | `:774-776` |
| Firing | `firinganyweapon` (`RevokeDelay: 12`) | **−2** | `:777-784` |
| Moving | `moving` | **−1** | `:785-787` |
| Veterancy 1–4 | `rank-veteran == 1..4` | **+1 … +4** | `defaults.yaml:278-287` |

Sign trap, and it is the one that bites: **positive = harder to see.** The owner's "reduce visibility by 2"
is a *positive* field change.

Prone keys on `!moving` (`infantry.yaml:294`) — standing still *is* prone. `dugin` arrives after
`TimeToBeStill: 200` (`:139-142`) = **12.0 s** at `Timestep: 60` (`mod.yaml:382`).

**Exhaustive** — a whole-`mods/` grep for `DetectableAddativeModifier` returns 13 sites: the six above, the
sniper's own firing modifier (`infantry.yaml:2148`), and `aircraft.yaml:70` (`+3` when not airborne). **No
vehicle carries one.**

### 2.2 A rifleman's actual states

| Doing | CV | Seen from |
|---|---|---|
| Moving | 3−1 = **2** | **28c** |
| Moving **and firing** | 3−1−2 = 0 → clamp **1** | **28c** — *identical* |
| Stopped, <12 s (prone) | 3+1 = **4** | **22c** |
| Stopped, ≥12 s (prone+dugin) | **5** | **19c** |
| Stopped, then fires | 5−2 = **3** | **25c** |
| Stopped ≥12 s, 3 cover objects | **8** | **10c** |
| …and rank 4 | 12 → clamp **9** | **7c** |

Two defects fall out, and both are new findings:

- **The firing penalty is free while moving.** A moving rifleman is at CV 2; firing takes the sum to 0, which
  clamps to 1, which the threshold floors back to 2. **A moving soldier who opens fire is not one cell more
  visible than one who does not.** The −2 is entirely swallowed by the floor.
- **Moving pins infantry at the bottom of the ladder.** CV 2 is the worst reachable value. The state you
  sneak in is the state with no concealment at all.

### 2.3 Vehicles — nothing at all

`^Vehicle` carries a bare `Detectable:` (`vehicles.yaml:66`) → `Vision` defaults to **2**
(`Detectable.cs:25`) → **seen from 28c, permanently, in every state.**

`^DetectableInfantryStandard` is inherited only by `^Infantry`, so no vehicle has a movement, firing, prone or
cover modifier. `GrantConditionOnMovement` does not appear on `^Vehicle` at all (the only match in
`vehicles.yaml` is inside a fully commented-out actor at `:793`), and `firinganyweapon` is granted at exactly
three sites, all in `infantry.yaml` (`:778,783,2149`).

**So item 8 needs the conditions granted before any modifier can key on them.** Per-actor `Detectable:` blocks
on artillery (`vehicles-america.yaml:615,782,1085`; `vehicles-russia.yaml:440,592,732,1004`) set
`CounterBatteryRadar` only — no `Vision` override anywhere in the vehicle tree.

---

## 3. The finding that reframes the request: forest is hard concealment, and it already ships

`MapLayers.AddSource` (`MapLayers.cs:357-375`) subtracts the sightline's forest shadow from the observer
**before** stamping its strength:

```csharp
var modifiedStrength = strength - shadowModify;
if (modifiedStrength < 1)
    modifiedStrength = 1;
```

Combine that floor of **1** with §1.2's threshold floor of **2**: an observer whose sightline is attenuated to
1 **can never detect anything, at any range, against any concealment level.**

The curve (`Map.ForestGroundShadow`, `Map.cs:1171-1189`) is `ceil(d/10)` up to crossed density 20, then
`2 + ceil((d−20)/5)`. An authored tree cell is density 10 (`decoration.yaml:104` etc.), so crossing ~5 dense
cells subtracts 8 and blanks a strength-8 observer at 10 cells.

**This is the mechanism that delivers "infantry can be sneaked through", it is terrain-driven, and it works
while moving** — the one state every modifier fails in (§2.2). It does not depend on CV at all.

The maps have the forest. Summed authored density inside `Bounds`:

| map | density objects | summed density | bounds |
|---|---|---|---|
| woodland-warfare-ww3 | 1202 | **29 245** | 96×96 |
| river-zeta-ww3 | 1231 | 18 545 | 96×80 |
| twin-rivers-ww3 | 400 | 8 160 | 126×126 |
| siberian-pass-ww3 | 230 | 5 215 | 95×65 |
| nuclear-winter-ww3 | 341 | 5 645 | 100×70 |
| seventh-woods-ww3 | 170 | 4 140 | 121×112 |
| x-lake / polar-disorder | 214 / 142 | 3 545 / 3 125 | 128×128 / 96×96 |
| shellmap-open-field | 35 | 420 | 90×60 |
| arena-tank-duel | 0 | 0 | 64×32 |

**But the modifier that should reward walking through those woods is attached to the wrong actor.**
`object-proximity` — the `+1/+2/+3`, the largest lever in the table — has **exactly one emitter in the mod**:
`ProximityExternalCondition@ObjectProximity` on **`^TreeHusk`**, i.e. a *burnt* tree
(`mods/ww3mod/rules/husks/husks.yaml:155-158`), at `Range: 384`.

`ProximityExternalConditionInfo.Range` is a `WDist` (`ProximityExternalCondition.cs:27`), so **384 = 0.375
cells.** Two consequences:

- **A living tree gives shadow but zero cover bonus. Only a burnt one gives the bonus** — which inverts the
  player's instinct exactly.
- **Even on husks the radius is about a third of a cell**, so the +1..+3 requires standing essentially on top
  of the wreck.

This is the cheapest large lever available and it is one YAML block (§5, Option A).

---

## 4. The two questions the brief asked me to reason about, not assume

### 4.1 "−2 cells" is not expressible on the target side, and the nearest step is −3

The ladder rungs are 3 cells apart (§1.1). Every `Detectable.Vision` change therefore moves reveal distance in
**3-cell quanta**. There is no 2-cell edit.

Three different edits could be called "reduce infantry visibility by 2 cells", and they are not
interchangeable:

| Reading | Edit | Verdict |
|---|---|---|
| Drop the outermost rungs | delete `Vision@1`/`Vision@2` from `^StandardVision` | **Reject** — observer side; blinds every unit in the game, not just enemies |
| Compress the ladder | retune `Range`/`MinRange` across `defaults.yaml:95-134` | **Reject** — observer side, global, and the circle radii at `infantry.yaml:808-908` mirror the ladder **by hand**; the two must move in lockstep or the gauge lies |
| Move the detection threshold | `Detectable: Vision:` on infantry | **Accept** — target side, one line, gauge follows automatically |

So: **read "−2 cells" as "one rung in", i.e. `Vision: 3 → 4`.** But see §4.3 — that alone does not deliver the
stated end.

### 4.2 The symmetry problem — which side I am proposing to touch

Two traits are both loosely called "vision" and they point opposite ways:

| Trait | Field | Meaning | Direction |
|---|---|---|---|
| `Vision` (`^StandardVision`, `defaults.yaml:95-134`) | `Strength`, `Range` | how far **this unit sees others** — **observer** side | higher = better eyesight |
| `Detectable` (`Detectable.cs:24-25`) | `Vision` | strength **required to see this unit** — **target** side | higher = stealthier |

**Every option in §5 changes only `Detectable` (target side). None touches `^StandardVision`.** Changing the
ladder to make infantry stealthier would instead make your own army blind, and would do it to both sides
symmetrically — the exact error the brief asked me to avoid.

The one asymmetry worth stating: infantry and vehicles both inherit `^StandardVision`
(`infantry.yaml:19`, `vehicles.yaml:12`), so **eyesight is uniform across the army today** and stays uniform
under every proposal here.

### 4.3 Does the owner's proposed means serve his stated end? No — and here is the arithmetic

The goal is *"infantry can actually be sneaked through instead of being spotted and pinned."* To walk past an
observer at distance *d* undetected you need `max(CV, 2) > strength(d)`. Moving CV = base − 1.

| infantry base | moving CV | seen from | closest you can walk past undetected |
|---|---|---|---|
| **3 (today)** | 2 | 28c | nothing inside 28c |
| 4 (`−3` cells) | 3 | 25c | 28c |
| 5 | 4 | 22c | 25c |
| 6 | 5 | 19c | 22c |

**Sneaking *through* a line on open ground is arithmetically out of reach of any modifier tune.** The observer
ladder reaches 32c and concealment is capped at 9; even +3 to base only buys a pass at 19 cells. A one-rung
change buys 3 cells and changes nothing qualitatively.

**The end is reachable only through terrain (§3).** That is why my recommendation leads with the cover
emitter rather than with the base number the owner proposed.

---

## 5. Ranked options, with costs

Effort is stated as **change-surface** (files/lines, and whether it is YAML or C#) plus **verification cost**
(runs I would ask the manager for). **Every option below is pure YAML on existing engine traits. None needs new
engine code.**

### Option A — living trees emit `object-proximity` · **recommended #1**

**What:** add a `ProximityExternalCondition@ObjectProximity` block to `^Tree`
(`mods/ww3mod/rules/ingame/decoration.yaml:2`), mirroring the husk emitter at `husks.yaml:155-158` but at a
useful radius (~`1c0` rather than `384`).

**Surface:** **one block, one file.** `^Tree` is inherited by **30** tree actors (`grep -c "Inherits: \^Tree$"`),
so a single edit covers every tree in the mod. The receiver already exists and is already capped
(`TotalCap: 3`, `infantry.yaml:759-761`), so the bonus is bounded at +3 by construction.

**Effect:** a rifleman moving through woodland goes CV 2 → up to CV 5, i.e. **28c → 19c while moving**, and it
**stacks with the shadow attenuation that already blanks observers entirely (§3)**. This is the only option
that changes the *moving* case enough to feel like sneaking.

**Verification:** 1 run on `woodland-warfare-ww3` — walk infantry through trees past a stationary observer,
compare reveal distance against open ground. Plus a frame-time glance, because this is the risk (below).

**Risk — and it is the real one:** `woodland-warfare-ww3` carries **1202** density objects and `river-zeta-ww3`
**1231**. That is ~1200 new proximity triggers per map. I have **not** measured the cost.
**Cheap mitigation if it bites:** put the emitter on the tree-*clump* actors (`TC01`–`TC05`, densities 55) and
`T15` only, instead of `^Tree` — fewer actors, and they are the cells players read as "woods" anyway.

**Second risk:** forests become strongly defensive for infantry. That is the requested direction, but it is a
balance change and should be seen before it is called good.

### Option B — vehicle state modifiers (item 8, exactly as asked) · **recommended #2**

**What:** in `^Vehicle` (`vehicles.yaml:66` area) — grant `moving` via `GrantConditionOnMovement`, grant
`firinganyweapon` via `GrantConditionOnAttack` (`RevokeDelay: 12`, mirroring `infantry.yaml:777-781`), then two
`DetectableAddativeModifier`s. Optionally one `Inherits@DetectableRangeCircles` line so vehicles get the gauge
too — which also clears the lint noise from §0.

**Surface:** ~14 lines, one file (plus one line if the circles are taken).

**The trap that decides the sign, and it would waste the change if missed:** vehicle base is **2**, which is
already the effective floor (§1.2). **A negative modifier on a vehicle is completely inert** — `2 − 1 = 1`
still reads as 2 and is still 28c. So item 8 must be authored as a **positive stationary bonus**, not a
negative moving penalty:

| state | modifier | CV | seen from |
|---|---|---|---|
| moving | — | 2 | 28c (unchanged) |
| stationary | `!moving` → **+1** | 3 | **25c** |
| stationary, firing | `+1` and `−1` | 2 | 28c |

That is precisely the owner's "stationary roughly −1 [visibility], firing more visible", and it is strictly
weaker than infantry's stationary swing (+2, and +3 with cover). Firing gives back exactly what stopping
earned.

**Verification:** 1 run, and it can share a slot with Option A.

### Option C — infantry base `Vision: 3 → 5` · **the dial, if A is not enough**

**What:** one line, `infantry.yaml:97`.

**Why 5 and not 4:** base 5 is the smallest value that puts **both** broken modifiers back into a live part of
the ladder. Moving becomes CV 4 (22c) instead of 2; moving-and-firing becomes CV 2 (28c) instead of CV 2 —
**so firing while moving finally costs 6 cells instead of nothing** (§2.2). Base 4 fixes neither: moving+firing
is still `4−1−2 = 1` → floored → 28c.

| state | base 3 (today) | base 4 | **base 5** |
|---|---|---|---|
| moving | 28c | 25c | **22c** |
| moving + firing | 28c | 28c | **28c** (now a real 6-cell penalty) |
| stopped ≥12 s | 19c | 16c | **13c** |
| stopped + 3 cover | 10c | 7c | **7c** (ceiling) |

**Cost:** one line; 1 run shared with the others. The circles follow automatically — they key on
`visibility-N`, so the gauge stays truthful with no second edit.

**Caution:** base 5 makes a stopped, dug-in, ranked rifleman hit the ceiling easily. `DetectableCeilingTest`'s
own header already names *"a Sniper at rank 3, stopped, reaches it from base 5"* — pushing line infantry to
base 5 puts ordinary riflemen into that regime with cover and rank. Expect infantry to become hard to dig out.

### Option D — infantry base `3 → 4` (the literal reading of "−2 cells")

One line. Everything 3 cells tighter. **Fixes nothing structural** — the moving+firing dead spot survives
(above), and §4.3 shows it does not deliver sneaking. Listed because it is what was asked for; **subsumed by
Option C**, which costs exactly the same.

### Option E — vehicles inherit the concealment circles only

One line, **no balance change at all**, `@stable` untouched, and it clears the §0 lint line. Pure hygiene.
Worth folding into B rather than doing alone.

### Rejected — anything touching `^StandardVision`

Observer side, global, blinds your own army, and forces a hand-mirrored second edit to
`infantry.yaml:808-908`. See §4.1/§4.2.

---

## 6. Recommendation

**Ship A + B together, and hold C as the dial.**

- **A** is the only option that serves the stated end. The goal is sneaking *while moving*, and moving is the
  one state where every modifier is either absent or swallowed by the floor. Terrain is the only lever that
  works there, the mechanism already ships and is hard concealment, the maps have the forest, and the reason
  it does not pay out today is a one-block misattachment to burnt trees at a third of a cell.
- **B** is item 8 delivered as asked, self-contained, ~14 lines, and the sign trap above is the only thing
  that would have made it a wasted change.
- **C** is one line and can be turned after A is seen in play. Turning it *first* would spend the balance
  budget on the case (stationary infantry) that already works, and leave the case that does not (moving) still
  broken.

**On the owner's "−2 cells": I would not do it as stated.** The available quantum is 3 cells, and the change it
describes buys 3 cells uniformly while leaving the sneak case arithmetically unreachable (§4.3). C is the same
one-line edit aimed at the modifiers that are currently dead.

**Total if all three ship:** 3 files, ~20 lines, all YAML, no C#, no new traits — and **one** run covers all of
it.

---

## 7. `@stable` movement — flagged, per `CLAUDE.md`

**Every option except E moves `@stable`, and it must not be silent.**

Both bot profiles are fog-respecting and participate in the influence stack —
`DOCS/reference/influence-stack.md:23,31`: *"the participants are the two fog-respecting bots (`@experimental`
+ `@stable`) plus humans"*, and `@stable` has participated since the 2026-08-02 parity promotion. The belief
store's live sightings come from `Actor.CanBeViewedByPlayer(player)` (`influence-stack.md:39`), which resolves
through exactly the `Detectable` → `MapLayers.IsDetected` chain re-verified in §1.

So changing infantry or vehicle detectability changes **when both bots acquire contacts**, which feeds the
danger and control fields and every consumer above them. This is deliberate improvement flowing to `@stable`,
which `CLAUDE.md` permits — but the **benchmark baseline must be re-taken** after any of A/B/C merges, and the
commit message must say so.

There is no default-off gating pattern available here: these are actor-level `Detectable` numbers, not
per-profile trait fields, so the "new behavioural field defaults to baseline" convention does not apply. The
change is visible to both profiles by construction.

**Also relevant to baseline comparability:** `influence-stack.md:40` records that baselines taken before
2026-08-27 are already not comparable. `1ff73ae5` (2026-08-20, §1.2) moved every reveal distance 3 cells
outward and sits inside that window.

---

## 8. Files I touched, and what lint would say

I ran neither `./utility.sh --check-yaml` nor `make test` (manager-serialized). **No rule was changed.** The
only edit on this branch is comment-only:

- **`mods/ww3mod/rules/ingame/infantry.yaml`** — the explanatory comment above `^DetectableRangeCircles`
  (`:789-807`) asserted the clamp is `[1, MapLayers.VisionLayers - 1] = [1, 10]` and cited
  `Detectable.cs:82-87,162`. Both are stale: the code clamps to `VisionLayers - 2 = 9` at
  `Detectable.cs:118-125`, and the line refs have drifted. Corrected in place, and the now-dead
  `visibility-10` ring is labelled as such. **No `RequiresCondition`, `Range` or trait line was altered**, so
  the lint surface is unchanged — a comment-only diff cannot move `--check-yaml` output. If it somehow does,
  the diff is one contiguous comment block and is trivially revertible.

`make all` and `dotnet test` were not run because no compiled code was touched.

---

## 9. What I did not verify

- **The game was never launched.** Every distance in §1.4, §2.2, §4.3 and §5 is arithmetic over the ladder
  (`defaults.yaml:95-134`) and the reveal predicate (`MapLayers.cs:600-603`). No number here is observed.
- **The ~1200-proximity-trigger cost of Option A is unmeasured.** It is the single largest risk in the
  recommendation and I have no figure for it.
- **I did not confirm that `ProximityExternalCondition` behaves identically on a living `^Tree` as on
  `^TreeHusk`.** The husk precedent is strong (same trait, same condition, same receiver) but trees differ in
  footprint and in carrying `SpawnActorOnDeath`; I read no code that would break, and read no code that
  proves it either.
- **The map census in §3 counts authored actors in `map.yaml` by type against the density table in
  `decoration.yaml`.** It does not account for footprint overlap or for how the density actually distributes
  along any particular sightline, so "summed density" is an upper bound on what a given sightline crosses,
  not a prediction.
- **I did not audit aircraft.** `aircraft.yaml:70` grants `+3` when not airborne and is untouched by every
  option here, but I did not check whether any option interacts with landed aircraft.
- **I did not re-derive the shadow layer's frozen-at-map-load behaviour** (recon §2.4, `Building.cs:372-383`).
  I read `Map.cs:1095-1125` only far enough to confirm `FlushPendingShadowUpdates` is still documented as
  unused. If shadow were live rather than frozen, Option A's interaction with burning forests would change.
</content>
</invoke>
