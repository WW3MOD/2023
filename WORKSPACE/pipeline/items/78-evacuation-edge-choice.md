### 78. Evacuation goes to the nearest wall, not home

`[SWING — ONE-TOKEN DIFF, and a balance change wearing a bugfix's clothes. The proposal's own author flagged this as the entry they were LEAST confident about — read "What is not verified" before costing it.]`

**Perceived:** a wrecked tank deep in enemy territory banks its refund in seconds through their back
edge, uninterceptable. A deep raid is therefore a free option: push in, do damage, cash out whatever
survives at the nearest wall.

**Source:** `WORKSPACE/proposals/260902-safe-wins-and-swings.md` §Tier 2, swing 3, **and its closing
section "The one I am least confident about, and what would settle it."** Filed 2026-09-02.

---

#### Mechanism — and the fix is one token

The aircraft branch already does the right thing. `RotateToEdge.cs:153-154`:

```csharp
var spawnAreaHint = FindClosestSpawnAreaForOwner(self);
var searchOrigin = spawnAreaHint ?? self.Owner.HomeLocation;
```

The ground branch, twelve lines below, is `spawnAreaHintGround ?? self.Location` (`:165-166`).
Re-read in this worktree 2026-09-02: both branches are verbatim as described.

On nine of ten maps `FindClosestSpawnAreaForOwner` returns null (only `river-zeta-ww3/map.yaml`
contains any `spawnarea` actor, verified by grep across `mods/ww3mod/maps/`), so **a ground unit's
exit resolves from its own position.** The `CanReach` pathfinder guard already exists at `:175-180`.

#### Citation that proves it does not exist

The four-line ground branch quoted above is the whole edge choice. There is no owner-side term, no
interception hook, and no `evacuating`-gated targetability change. Not in `PIPELINE.md`.
`RELEASE_V1.md:56` is adjacent and scoped to the last few tiles past the boundary — a different
thing that composes with this rather than containing it.

#### ⚠️ What is NOT verified — and it is the premise, not the mechanism

**The proposal's author flagged this as the entry they were least confident about, and the reason is
not the code.** What was read is solid: the two branches really do differ, and nine of ten maps
really have no `spawnarea`. Those are reads, not relays.

**What was never verified is whether it matters.** The whole value rests on an unmeasured geometric
assumption — that a unit which has pushed into the enemy half is meaningfully *closer* to the
enemy's back edge than to its own, often enough and by enough margin to make evacuation a free
option. On a map whose spawns sit near opposite edges that is obviously true; on a map with a long
neutral middle, or with fighting concentrated around central objectives, **it may almost never
bind.** Nobody has watched it happen and the ten maps' geometry was not read.

**If the premise is weak, this is a balance change to a path shared by five callers and both bot
profiles, bought for nothing.**

#### What would settle it, cheapest first

1. **Static, no launch, and it could have been done in the originating pass with more time.** For
   each of the ten shipped maps, take the spawn points and the map bounds and compute, over a grid
   of plausible engagement cells, whether the nearest edge is the owner's or the enemy's. That is
   arithmetic on `map.yaml` and answers the premise directly, per map, with no game running.
   **Do this first. It is free.**
2. **If a launch slot is going spare:** place one own-player unit in the far corner of
   `twin-rivers-ww3` (spawns `112,92` / `112,28`, zero `spawnarea`), issue `Evacuate`, and log the
   chosen edge cell. **The answer that counts:** whether the chosen cell's edge is the one nearest
   the *unit* or the one nearest `self.Owner.HomeLocation`. Read `result.json` from the run
   directory — **not piped through `tail`**. Latch the cell from a notification hook, **not** by
   polling `Actor.Location`, which leads a moving unit by one cell and has already destroyed one
   run's answer this week.

**If (1) shows the nearest edge is usually the owner's own, this item should be DROPPED rather than
rewritten.**

#### What makes it a bet

It is **a balance change wearing a bugfix's clothes.** `RotateToEdge` is the shared path for the
manual Evacuate order, the evacuate-when-dry stance, `DropsSupplyCache`'s empty truck return,
`VehicleCrew` and `EvacuateWhenUnrearmable` — so it moves **both bot profiles by construction** and
must be called out in the commit message per CLAUDE.md's `@stable` policy.

A unit that cannot path home falls back to today's behaviour, which is fine but **must be a
documented decision rather than an accident.**

#### Size

Small diff, medium work — the cost is measurement and balance review, and step (1) above may
eliminate the item entirely.

---

## MEASURED 2026-09-05 — the premise HOLDS. Do not drop this item (`wt/evac-edge-math`, base `main @ 95bdffb2`)

Step (1) above was done, statically, as instructed. Tool and full method:
[`tools/evac-edge-math/`](../../../tools/evac-edge-math/README.md). No game was launched.

**The author's fear was that the nearest edge would usually be the owner's own. It is not.**
Across the nine shipped maps with no `spawnarea`, for a unit within 20 cells of an opponent's
spawn: **14.4%** exit through their own back wall, **70.4%** through an opponent's, **15.3%**
through a neutral flank. Median drive to the exit is **9.0 cells**; under the proposed
`?? self.Owner.HomeLocation` it would be **108.4 cells**. That is the whole item, and the
margin is not close.

The 14.4% own-wall figure comes **entirely** from `twin-rivers` and `x-lake`, the two maps that
put two spawns on the same wall — there "own wall" is also an opponent's own wall. On all six
two-spawn maps and on `seventh-woods`, the raid-population own-wall rate is **0.0%**.

The dossier's own suggested launch test, answered on paper — twin-rivers, own unit
(owner spawn `1,22`) in the far corner `124,124`:

| | exit cell | wall | drive |
|---|---|---|---|
| shipped | `124,126` | Bottom | **2.0 cells** |
| under the fix | `1,22` | Left | **159.8 cells** |

At the shipped default timestep of 60 ms (`mod.yaml:381-383`, 16.67 tps) and a representative
tracked `Speed: 70` (`vehicles.yaml:171`, 1024 WDist/cell → 1.14 cells/s), 9 cells is ≈ **8 s**
and 108 cells is ≈ **95 s**, straight-line. "In seconds" is accurate.

### Two corrections to this dossier's own text

**1. `river-zeta-ww3` is already living under the proposed fix, and is the natural experiment.**
`FindClosestSpawnAreaForOwner` anchors on the `spawnarea` nearest the player's own
`ProductionFromMapEdge` (`RotateToEdge.cs:111-127`) — the `SUPPLYROUTE`, which `world.yaml:463+`
places at their `mpspawn`. That is an **owner-side** term, not a position-derived one. So on
river-zeta the ground branch already does what item 78 asks for, and it measures **100.0% own
wall, drive 74.8 vs fix 70.6 (−4.2)** — identical to the fix within the spawnarea-vs-spawn offset.
The other nine maps measure 0–29% own wall and drive 6–18. The item is therefore not "add an
owner-side term"; it is **"make the null fallback agree with the non-null path"**, which is a
smaller and better-motivated change than the dossier frames. `DOCS/reference/economy.md:118-126`
already documents the anchor; it had not been connected to this item.

**2. "Uninterceptable" is not supported and should be struck.** The `evacuating` condition
deprioritises selection only; there is no targetability change, as this dossier already says.
The unit is fully shootable for the whole drive. What the measurement supports is **8 seconds of
exposure instead of 95** — a short trip, not an immune one. Nothing here shows the trip is *safe*,
and see the caveat below.

### What this does NOT settle

The 9-cell drive is through the enemy's most defended ground; the 108-cell drive home is mostly
through open or friendly ground. Whether 9 hostile cells is cheaper than 108 mixed ones is a
balance question that straight-line geometry cannot answer, and it is the one real argument
against acting. Cost the change on the geometry; do not claim the measurement proves the raid is
*free*, only that the exit is *near*.

### On the engine's edge choice vs the intuitive one — they agree

The brief for this measurement expected a possible disagreement. There is essentially none, and
that is worth recording so nobody re-checks it:

- The intuitive model (perpendicular foot on the nearest wall) names a different **wall** than the
  exact unfiltered argmin over the perimeter in **0.8%** of sampled cells — corner ties, plus
  `ChooseClosestEdgeCell`'s exclusive-`Bounds.Right` off-by-one, which can name a cell one *past*
  the bounds.
- The `CanEnterCell && CanReach` filter moves the chosen **cell** off the unfiltered argmin in
  **26.5%** of cells, but changes which **wall** in only **2.1%**. The filter shifts the exit
  along a wall far more often than it shifts it to another wall.

So "nearest edge" ≈ "perpendicular distance to the Bounds rectangle" is a sound mental model for
the ground branch. The two things that are *not* intuitive are (a) which chooser each branch calls,
and (b) that the sort origin and the reachability origin are different (`searchOrigin` vs
`self.Location`, `RotateToEdge.cs:165-180`).

---

## SHIPPED 2026-09-05 on a direct user ruling (`wt/evac-home-edge`, base `main @ 78a97b57`)

**The user rejected the balance framing rather than picking a side of it:** *"I dont get it, can
they evacuate on any side? All evacuation should only happen on our own side, the one where our
units spawn. Possibly that they can evacuate at any allied SR as well, on their edge, if it is
closer. But not 'Any wall' (What is a wall?)"* Built as a correctness fix accordingly — a unit
returning to off-map reserves through the ENEMY's border was never a design anybody chose.

**The change** is `RotateToEdge.ChooseEdgeCell`'s ground branch: `?? self.Location` becomes
`?? FriendlyEvacuationOrigin(self)`, a new helper returning the nearest friendly `SUPPLYROUTE`
(`ProductionFromMapEdge` filtered by `self.Owner.IsAlliedWith(a.Owner)`, ranked by distance to
the unit), then `self.Owner.HomeLocation`, then `self.Location`. **The aircraft branch and
`FindClosestSpawnAreaForOwner` are untouched**, so `AmmoPool`'s evacuate-vs-rearm decision does
not move.

**The allied-SR extension WAS built** — this dossier's costing question, answered: it is one
`Where` clause, it needs no new config, and because `Player.RelationshipWith` returns `Ally` for
`this == other` it degenerates to the player's own Supply Route in a free-for-all, so the ally
clause cannot change a solo game. It did not complicate the core change.

**Measured after, same tool, nine unit-anchored maps, `raid` population:** own wall
14.4% → **99.5%**, opponent's wall 70.4% → **0.4%**, median drive 9.0 → **108.1** cells.
`river-zeta-ww3` is unchanged in every population, as predicted — it is the control.

### The consequence the user has NOT approved, and must hear

They approved the rule, not this. At 12.0x exposure the likely real effect is that forward units
**die before banking anything**: `INotifySold.Sold` fires only on reaching the edge
(`RotateToEdge.cs:517`), so a unit killed en route banks zero, and surviving it banks less
because the refund is scaled by current HP (`:511`). The `evacuating` condition provides **no
defensive protection whatever** — `SelectionPriorityModifier` feeds only the player's own
mouse/box-select (`SelectableExts.cs:29-36`), never targeting — so this dossier's "uninterceptable"
was wrong in one direction and the auto-target framing is wrong in the other. Full arithmetic,
the bot-census second-order effect, and the scenario census in `WORKSPACE/DISCOVERIES.md`
2026-09-05.

### Left undone, deliberately

- **`Map.ChooseClosestEdgeCell`'s exclusive-`Bounds.Right` off-by-one is untouched** and filed
  separately. It is in the blast radius only via the unit-anchored retry at `RotateToEdge.cs:362`,
  which this change makes more likely to fire; it was not chased.
- **The allied-SR path has no scenario.** `tools/autotest/scenarios/test-evac-exits-own-side/`
  grades the core with an ENEMY Supply Route as the negative control for the relationship filter.
  Discriminating own-SR from a nearer ALLIED SR needs a third player and a `PlayerReference`
  alliance, an idiom no scenario in this tree uses; authoring one blind under the launch freeze
  risked a false "the ally clause is broken" verdict. See that scenario's `description.txt`.
