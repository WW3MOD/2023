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
