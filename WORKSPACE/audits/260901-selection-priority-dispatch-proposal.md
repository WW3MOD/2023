# Selection pruning vs. the technician dispatch gesture — costed proposal

**Date:** 2026-09-01 · **Base:** `main` @ `9cd1e0d8` · **Status:** proposal only, nothing implemented.

Commissioned against `WORKSPACE/HANDOFF-260901.md` §C. No build was run (machine loaded); every
claim below is from reading the tree at the cited lines, plus one static measurement over the
shipped maps using nav-guard's decoder.

---

## 1. Premise check

**The defect is real and unfixed.** `SubsetWithHighestSelectionPriority`
(`engine/OpenRA.Game/SelectableExts.cs:96-103`) groups by exact priority, orders descending and
returns `FirstOrDefault()` — the top group only. Nothing has landed against it since
`22d3dccd`. Two details in the handoff are wrong, and one of them changes the answer.

### Correction 1 — FCOM/BIO are priority **2**, not 10

The handoff reads the engine default (`Selectable.cs:19`, `Priority = 10`) for actors that
declare no `Priority`. But `^SelectableBuilding` sets `Priority: 2`
(`mods/ww3mod/rules/defaults.yaml:1005-1008`), and FCOM/BIO reach it through
`^TechBuilding` → `^BasicBuilding` → `Inherits@Selection: ^SelectableBuilding`
(`structures.yaml:141`, `:7`). Their own `Selectable:` blocks
(`structures-neutral.yaml:37-38`, `:73-74`) set only `Bounds`, and MiniYaml merges children in
place, so the inherited `Priority` survives. Verified by resolving the inheritance chain
mechanically, not by eye.

The three `Priority: 0` values are confirmed exactly as described: OILB
`structures-neutral.yaml:8`, MISS `:108`, HOSP `:151`.

**The full ladder of capturable structures is three tiers, not two:**

| Tier | Template / actor | Site |
|---|---|---|
| 4 | `^SelectableCombatBuilding` — every `^Defense` (bunkers, turrets) | `structures-defenses.yaml:24-27` |
| 2 | `^SelectableBuilding` — FCOM, BIO, and **every other `^BasicBuilding`** | `defaults.yaml:1005-1008` |
| 0 | OILB, MISS, HOSP | `structures-neutral.yaml:8`, `:108`, `:151` |

Defenses are capturable too — `^Defense: Inherits: ^Building` (`structures-defenses.yaml:2`)
reaches `^BasicBuilding`, which carries `Inherits@NeutralOrOccupiedCapturable`
(`structures.yaml:10`). So a box holding a bunker and a derrick selects the bunker alone.

### Correction 2 — the relationship offset splits the groups again, and it is the bigger splitter

This is the finding that reframes the item. `SelectableExts.SelectionPriority(Actor)`
(`:29-52`) subtracts `PriorityRange = 30` **per relationship step** after the YAML value:
ally −30, neutral −60, enemy −90. The whole YAML ladder spans 0–10; one relationship step is
three times that.

Every dispatch target is by construction non-own — `selectedCaptureTargets` filters
`a.Owner != world.LocalPlayer` (`CommandBarLogic.cs:491`). So every one of them carries an
offset, and targets with different owners land in different groups **whatever their YAML
priority is**:

| Structure | Owner | Group key |
|---|---|---|
| Oil derrick | Neutral | `0 − 60 = −60` |
| Nuclear reactor | Neutral | `2 − 60 = −58` |
| Bunker | Neutral | `4 − 60 = −56` |
| Oil derrick | Enemy | `0 − 90 = −90` |
| Nuclear reactor | Enemy | `2 − 90 = −88` |

Two oil derricks, one neutral and one enemy-held, are −60 and −90: **different groups.** The
handoff's "box-selecting three oil derricks works" holds only while all three share an owner.

Every capturable structure on all ten shipped maps starts `Owner: Neutral` (checked across
`mods/ww3mod/maps/*/map.yaml`), so early game the split is purely the YAML tiers. The
relationship split appears the moment anyone captures anything — which is exactly the
mid-game state in which a player wants to retake a lost derrick.

**Consequence: equalising the YAML `Priority` values cannot fix this on its own.** That is
the handoff's first proposed fix, and it is a partial one.

### Correction 3 (scope, in the feature's favour) — only the F gesture is affected

Right-click dispatch resolves its target through `UnitOrderGenerator.TargetForInput`
(`UnitOrderGenerator.cs:35-47`), which calls `WithHighestSelectionPriority` — a
`MaxByOrDefault` **ranking** (`SelectableExts.cs:67-75`), not the group filter. It picks one
actor under the cursor and is not affected. The pruning breaks part (c) — the F spread —
and nothing else.

### There is a working manual workaround today

`Selection.Combine` with `isCombine` does `actors.UnionWith(...)` and never re-prunes
(`Selection.cs:108-114`). So **shift-drag a second box, or shift-click each structure, and the
tiers accumulate.** A player who knows this can build any mixed selection and F works on all of
it. That lowers the severity from "broken" to "clumsy", and it is the thing to tell the user in
the interim.

---

## 2. Blast radius — the handoff's "changes selection behaviour globally" is overstated

Four call sites, all in UI widget code:

| # | Site | What it feeds | Affected by a non-own-only change? |
|---|---|---|---|
| 1 | `SelectionUtils.cs:131` | box-select, `controlAll` dev mode, own units only | No — already filtered to `x.Owner == viewer` (`:129`) |
| 2 | `SelectionUtils.cs:135` | **ordinary box-select** | Yes — this is the one |
| 3 | `SelectAllUnitsHotkeyLogic.cs:53` | select-all-on-screen | No in normal play — see below |
| 4 | `SelectAllUnitsHotkeyLogic.cs:61` | select-all-in-world | No in normal play — see below |

Sites 3 and 4 take their actor set from `GetPlayersToIncludeInSelection`
(`SelectionUtils.cs:138-149`), which returns `new[] { viewer }` unless the viewer is a
spectator or shroud is disabled. In ordinary play they only ever see own actors, so any change
scoped to non-own actors leaves them byte-identical.

Site 2 also feeds the **drag-time rollover**, not just the committed selection —
`WorldInteractionControllerWidget.cs:72` and `:77` call the same function and pass the result
to `Selection.SetRollover` (`:80`). Any change here changes what highlights under the cursor as
well as what ends up selected. That is a feature, not a hazard: see §4.

**Nothing outside the UI reads selection priority at all.** `ISelectableInfo` appears in seven
files, all of them selection, order-generator or trait-interface files; `.SelectionPriority(` has
no callers outside `SelectableExts.cs` itself; and `Traits/BotModules/` contains no reference to
`ISelectableInfo`, `SelectionPriority` or `world.Selection`. **So no option here can move
`@stable` or any bot**, and none can desync — `Selection` is a local client object and priority
only steers which local click becomes which order.

**There is also a pure-function test precedent.** `SelectionPriorityMath` already exists purely so
selection rules can be pinned in NUnit without constructing a `World`
(`engine/OpenRA.Test/OpenRA.Mods.Common/SelectionPriorityMathTest.cs`). An engine change here can
be covered the same way, which is what makes option B's verification cost as low as it is.

---

## 3. Ranked options

Effort is given as change-surface + verification cost. "YAML" and "engine" are kept apart because
YAML rules load at runtime with no rebuild, and engine changes need `make all` + NUnit + a launch.

### A — Delete the three `Priority: 0` lines *(recommended first step)*

**Surface:** remove `Priority: 0` from `structures-neutral.yaml:8`, `:108`, `:151` (keep the
surrounding `Bounds`). Three deleted lines. OILB/MISS/HOSP then inherit `^SelectableBuilding`'s 2
and join FCOM, BIO and every other non-defense building in one tier.

**Verification:** no rebuild — rules load at runtime. One launch, box two adjacent neutral
derricks plus a reactor, press F. YAML gate + `make nav-guard` unaffected (no terrain, no
blocking, no actor placement).

**In play:** the common early-game case starts working — a box over a cluster of neutral tech
buildings selects all of them and F spreads across the lot. Defenses at tier 4 and the
relationship split both remain.

**Breaks:** the 0-tier's only current effect is ordering *between* structures, since own-vs-other
is already separated by the 30-point relationship gap and any box containing a unit excludes all
buildings anyway. The one visible change is that a box over your own base now also picks up a
derrick you have captured, where before it took the other buildings only. `Priority: 0` was set in
`98a4dc09` "rules WIP" (2023-03-21), the earliest bulk rules commit, with no recorded rationale —
it is very likely inherited Red Alert cruft rather than a WW3MOD decision, though the stock RA mod
is not in-repo so that could not be confirmed.

**Cost: lowest available. Fixes the common case, no engine risk, no `@stable` exposure.**

### B — Collapse the YAML tier for non-own actors, keeping the relationship band

**Surface:** in `SelectableExts.SubsetWithHighestSelectionPriority` (`:96-103`), group on a key
that keeps the relationship band but discards the YAML priority for actors the viewer does not
own — `(relationshipRank, own ? priority : 0)`. Extract the key into `SelectionPriorityMath` so
NUnit can pin it, matching the existing precedent. Roughly 15 engine lines plus a fixture.

**Verification:** `make all` + `dotnet test` + one launch. Sites 1, 3 and 4 are provably untouched
in normal play (§2), so the launch only has to exercise ordinary box-select.

**In play:** a box over any mix of *same-owner* structures selects all of them — neutral derrick +
neutral bunker + neutral reactor together. Your own units still win any box they appear in
(own key ≥ 0, every other band ≤ −30). Enemy units and enemy buildings would now co-select where
today the unit wins.

**Breaks:** the ally case. Today a box over an ally's tank and an ally's building takes the tank;
after, both. Low harm — you cannot order allied actors — but it is a real behaviour change, and it
also lands on the drag rollover.

**Does not fix** the neutral-vs-enemy split. **A and B together fix every same-owner case and
leave only mixed-owner.**

### C — Collapse the relationship band too, for non-own actors

**Surface:** as B, but one key for everything the viewer does not own.

**Verification:** same as B.

**In play:** every mixed selection of foreign structures works, including neutral + enemy-held —
the full mid-game retake case.

**Breaks:** meaningfully more. Boxing your ally's units alongside enemy units in a shared fight
now selects both, and in `controlAll` developer mode — where you *can* order foreign actors — the
Own > Enemy > Allied > Neutral ordering that `CalculateControlAllPriority`
(`SelectionUtils.cs:75-105`) deliberately establishes gets flattened for the box path. I would not
ship C without the user seeing it, because it changes what a drag over a busy battlefield picks
up.

### D — Let the gesture consult the unpruned set *(the third framing — investigated, not recommended)*

The task asked whether the dispatch could read past the pruning without changing what the player
sees selected. **It cannot, as the code stands.** `selectedCaptureTargets` reads
`world.Selection.Actors` (`CommandBarLogic.cs:490`); the pruning happened upstream inside
`SelectActorsInBoxWithDeadzone`, and `allInBox` (`SelectionUtils.cs:117`) is a local that is
discarded. By the time F is pressed the dropped structures are gone and there is no box to
re-derive them from.

Making it possible means retaining state: stash the non-own capturable part of `allInBox` on
`Selection` when a box commits (`WorldInteractionControllerWidget.cs:153`) and clear it on every
other selection route. Perhaps 30 engine lines.

**Rejected on behaviour, not on cost.** F would then act on structures the player can see are not
selected — no box, no health bar, no highlight — and the result would depend on invisible history
(fine right after a drag, wrong after a control-group recall). That is a worse legibility failure
than the one being fixed. Recorded so the next reader does not re-derive it.

---

## 4. Is the player told anything today?

**Partly — and the existing tell is better than the handoff assumes, which changes what is worth
building.**

The drag-time rollover runs the *same* pruning: `WorldInteractionControllerWidget.cs:72` calls
`SelectActorsInBoxWithDeadzone` and feeds `Selection.SetRollover` (`:80`). Rollover membership
makes `SelectionDecorationsBase` force `displayHealth = displayExtra = true` (`:104-107`). So
while the box is being dragged, the structures that will survive the prune show health bars and
the ones being dropped do not. The preview is live and it is exactly accurate.

What it is not is *salient*: it is a health bar appearing on some buildings and not others, at the
moment the player is watching the box edge. And `RenderSelectionBox` is gated on `selected`
(`:109`), so rollover deliberately draws no outline.

**Cheap upgrade, if wanted:** let rollover draw the selection box outline too — `selected` becomes
`selected || rolloverContains` at `SelectionDecorationsBase.cs:109`. One line, engine, affects
every drag in the game. It would make the prune obvious rather than merely visible.

**My reading: this is worth less than option A, not more.** Legibility tells the player *that*
their derrick was dropped; it does not let them dispatch to it. And a player who has already
learned the shift-box workaround (§1) does not need the tell. If A ships, the common case stops
being pruned at all and there is much less left to make legible. I would not spend the engine
change on the outline first.

---

## 5. The second weakness: straight-line vs path length

Both dispatch paths use straight-line distance — `Evaluate` orders by
`(target.CenterPosition - p.Actor.CenterPosition).LengthSquared`
(`CaptureDispatchManager.cs:144`), and `CostMatrix` fills
`(targets[j] - capturers[i]).Length` (`CaptureDispatchMath.cs:218-229`).

### How wrong it gets — measured, not estimated

I rebuilt the `foot` movement graph for all ten shipped maps with nav-guard's decoder
(`build_cell_model` + the `tagged` squeeze variant, matching `make nav-guard`), ran Dijkstra
(100 orthogonal / 141 diagonal) from each real OILB/FCOM/BIO/MISS/HOSP position on the map, and
compared the technician the current code picks against the one true path length picks. 3128
trials, pools of 4 candidate cells, seed 20260901:

| | |
|---|---|
| Straight-line pick differs from path-length pick | **6.4%** |
| Straight-line pick is **entirely unreachable** on foot | **0.5%** |
| Path/straight-line ratio | median **1.08**, p95 **1.76**, max **6.47** |

Worst maps: `polar-disorder-ww3` 15.0% wrong / max 5.78×; `siberian-pass-ww3` 11.0% wrong /
median 1.20; `x-lake-ww3` 8.0%. Best: `seventh-woods-ww3` 0.8% wrong, max 1.25× — open terrain,
essentially exact.

**The 0.5% is the case that reads as broken.** A wrong-but-reachable pick sends a technician who
arrives a bit late; nobody notices. An *unreachable* pick issues the order, the technician walks
to the near side of the water and stops, and the structure is never taken — and because
`CommittedTarget` reads the activity queue (`CaptureDispatchManager.cs:103-117`), that technician
now counts as busy and is excluded from every later dispatch. One bad pick removes a unit from the
pool for the rest of the match.

Caveat on the numbers, stated plainly: candidate cells were sampled uniformly over passable
terrain, so they are more spread out than real technicians, which cluster near a player's own
side. That probably **overstates** the wrong-pick rate for a typical dispatch and **understates**
it for the cross-map retake that motivates the feature.

### Is path length cheaply available there?

**Reachability: yes, essentially free.** `PathFinder.PathExistsForLocomotor(locomotor, source,
target)` (`Traits/World/PathFinder.cs:190-193`) delegates to
`HierarchicalPathFinder.PathExists` (`:935`), which compares precomputed abstract domain IDs
after a cached `RebuildDomains()` — two lookups, no search. It is terrain-only
(`BlockedByActor.None`), which is the right question here.

**True path cost: no.** The only exact answer is `HierarchicalPathFinder.FindPath` (`:830`),
which returns a `List<CPos>` — a full A* per pair. `costEstimator` is
`PathSearch.DefaultCostEstimator` (`:274`), a straight-line heuristic, so it is no better than
what the code already does. For F with 6 technicians and 5 structures that is 30 searches on one
keypress.

### Options

**E — mark unreachable pairs infeasible.** In `DispatchAcross`, extend the existing feasibility
loop (`CaptureDispatchManager.cs:218-230`) — which already writes `CaptureDispatchMath.Infeasible`
for capture-rule violations — with a `PathExistsForLocomotor` test, and add the same guard to
`Evaluate`'s nearest-free pick. ~15 engine lines, no new maths, no change to
`CaptureDispatchMath`'s pure signature. Kills the 0.5% outright and costs approximately nothing
per query. **This is the one worth doing.**

**F — true path cost.** Change `CostMatrix` to accept precomputed costs (keeping it pure and
NUnit-pinned) and fill them with `FindPath` lengths at the call site. Buys the remaining ~6%.
Costs `capturers × targets` A* searches on a keypress, and would want a cell-count budget the way
`AmmoPool.ChooseResupplier` needs one. **Not worth it now** — the median error is 8% and the
failure it prevents is "arrived slightly later", which no player will attribute to anything.

---

## 6. Recommendation

1. **Option A** — delete three YAML lines. Fixes the common early-game case at near-zero cost and
   no engine risk. Ship it alone if nothing else.
2. **Option E** — the reachability guard. Cheap, kills the only genuinely broken-looking dispatch
   failure, and removes the "technician permanently marked busy" trap.
3. **Option B** — if A proves insufficient in play. Small, testable, provably confined to
   ordinary box-select.
4. **Option C only with the user's sign-off**, and **F not at all for now.**

A and E are independent and could go in one branch or two. Neither touches bots, simulation or
determinism (§2), so **there is no `@stable` movement to declare for any of the recommended
options** — worth stating explicitly since `CLAUDE.md` requires it, and here the honest answer is
that selection priority is invisible to every bot profile.

---

## Watch

- **Nothing was built or run.** No `make all`, no `dotnet test`, no launch — the machine was
  loaded and this was commissioned as a code read. Every engine-behaviour claim is from reading
  the cited lines.
- **The inheritance resolution behind Correction 1 used a throwaway Python approximation of
  MiniYaml**, not the engine's `ResolveInherits`. It agrees with reading the chain by hand
  (`structures-neutral.yaml:35` → `structures.yaml:141` → `:7` → `defaults.yaml:1005`), and the
  handoff's contrary "10" is explained by reading `Selectable.cs:19` instead of the chain — but
  the cheapest confirmation is one launch: box a derrick and a reactor and see which survives. If
  FCOM/BIO really were 10, the reactor would still win and the symptom would look identical, so
  **the symptom cannot distinguish the two — only the value can.** `--check-yaml` dumps resolved
  rules and would settle it in seconds on an unloaded machine.
- **The distance measurement is a static model, not the game's pathfinder.** It uses nav-guard's
  decoder, which reimplements terrain and blocking; it charges a flat 141 for diagonals and
  ignores per-terrain movement speeds, so the true in-game ratios will differ. Direction and order
  of magnitude are what I would defend; the specific 6.4% I would not. The scripts were deleted as
  throwaway — the method above is enough to rebuild them.
- **I did not verify that the F gesture reaches `DispatchAcross` at all in a live game.** The
  guard at `CommandBarLogic.cs:524` requires `selectedCaptureTargets.Length > 0` **and** that no
  selected actor can issue a deploy order. Since `selectedCaptureTargets` fills only from non-own
  actors and deploys only from own ones, the commit message argues they cannot overlap — I read
  that argument and believe it, but §B of the handoff records that
  `test-capture-dispatch-bottleneck` **has never run green**. If that scenario is red for a
  reason other than staging, this whole proposal may be costing a fix for a gesture that does not
  fire. **Cheapest check by far: run that one autotest.** It is the single thing most likely to
  make this recommendation the wrong call.
- **What would most change my ranking:** if the user's actual complaint turns out to be the
  mid-game retake (mixed neutral + enemy-held), then A is nearly useless and the real answer is
  B+C or a rethink. One question to the user — "when it felt broken, were the buildings all
  unowned, or had the enemy taken some?" — separates those at no cost.
