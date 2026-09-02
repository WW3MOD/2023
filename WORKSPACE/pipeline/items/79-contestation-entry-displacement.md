### 79. Contestation should push the beachhead back

`[SWING — new gameplay on the mod's central mechanic. Small blast radius, real balance question: it STACKS two penalties on the losing player.]`

**Perceived:** enemy units grind your Supply Route. Instead of only arriving *more slowly*, your
reinforcements start arriving *in the wrong place* — the drop point slides down the map edge, then
to a different edge, and every unit has a longer, more exposed walk. Push them off and it walks
home.

**Source:** `WORKSPACE/proposals/260902-safe-wins-and-swings.md` §Tier 2, swing 4. Filed 2026-09-02.

---

#### Mechanism

Both traits sit on the same actor, so **no world scan is needed.** The edge choice funnels through
one variable — `ProductionFromMapEdge.cs:100` (aircraft) and `:118` (ground):

```csharp
var searchOrigin = spawnAreaHint ?? self.Location;
```

and candidates are already enumerated (`:101`, `:122`, `GetSpawnCandidatesOnSameEdge`). **A
contestation-scaled offset on `searchOrigin`, or a biased index into `candidates`, is the whole
change** — behind an `Info` field defaulting to zero displacement so `@stable` and every existing
map are byte-identical until it is turned on.

That default-inert shape is required, not optional: per CLAUDE.md, a new behavioural `Info` field on
a trait shared by both bot profiles must default to baseline so `@stable` never changes without
anyone noticing.

#### Citation that proves it does not exist

`grep -c "SupplyRouteContestation"
engine/OpenRA.Mods.Common/Traits/ProductionFromMapEdge.cs` returns **0** — re-verified 2026-09-02 in
this worktree. The two traits do not reference each other in either direction. No contestation,
health or player-state term enters the edge choice.

#### And it is now cheaply measurable — which was not true when the design recon was written

The merge at `9b687fef` added `tools/autotest/scenarios/test-sr-entry-cell`, which pins the entry
cell numerically off `Trigger.OnProduction` (`ProductionFromMapEdge` raises `UnitProduced` at
`:200`). **A displacement change has a ready-made regression pin.**

⚠️ **Do not measure it by polling `Actor.Location` — that leads a moving unit by one cell**, which
is what the same merge's `DISCOVERIES.md` entry records costing a run.

#### What makes it a bet

It **stacks two penalties on the losing player** — slower production *and* longer walks — which can
turn a bad position into an unrecoverable one and make comebacks worse, **the opposite of what a
graduated design is for.** The honest version probably *replaces* part of the production slowdown
rather than adding to it, and that is a balance decision, not an implementation detail.

Second: on a small map, or an SR near a corner, the displaced entry point may have nowhere to go, so
**the effect is inconsistent per map.**

#### Size

Medium. **Small blast radius for new gameplay:** two traits on one actor, one default-inert field,
no RNG, no new actor, no UI.

#### Related

- Real contestation timings, for anyone writing copy or picking a scale: `BaseTicks: 1500` is 90 s,
  `MinTicks: 500` is 30 s, `BaseRecoveryTicks: 3000` is 180 s. **The 25 tps assumption is wrong and
  understates all three by 1.5×** — see CLAUDE.md and `DOCS/reference/conventions.md`.
- Item 81 (aircraft contestation asymmetry) touches the same trait's eligibility test. Landing both
  without a baseline between them makes attribution impossible.
