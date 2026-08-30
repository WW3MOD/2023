# Cursor honesty audit — does the cursor's promise match the order the game accepts?

**Date:** 2026-08-30. **Branch:** `wt/cursor-honesty`. **Base:** `main @ 5a985337`.
**Method:** static reading only. No launch, no build, no `make test`.

The question: *when the cursor changes to promise an order, does the game always carry that order
out?* The bug shape hunted is a `CanTarget`-style predicate used for **display** drifting from the
validity check applied on **execution**.

---

## The layer that is already correct — do not re-audit it

`UnitOrderGenerator.GetCursor` (`engine/OpenRA.Mods.Common/Orders/UnitOrderGenerator.cs:135`) and the
actual click (`Order`, `:52`) both resolve through the **same** `OrdersForSelection` → `OrderForUnit`
(`:271-309`). The comment at `:132-134` says why in as many words. So at the *targeter-selection*
layer, cursor and click cannot disagree — that class of bug has already been closed here, and
`OrderFallbackMath` pins the retry gate with unit tests.

Every divergence below therefore lives **downstream** of targeter selection, in one of four places:

1. targeter accepts → `IIssueOrder.IssueOrder` returns `null` (the engine logs this itself:
   `CheckSameOrder`, `UnitOrderGenerator.cs:325-332`);
2. order issued → `IResolveOrder.ResolveOrder` re-checks a **different** predicate and drops it;
3. order resolved → the **Activity** declines at runtime, silently;
4. the mirror: an order that works but shows **no** cursor, or a cursor that names a *different
   action* from the one performed.

Categories 2 and 4 are where everything interesting was found.

---

## Table

`file:line` is given for both sides of every pair. Provenance is marked: **[R]** = I read these exact
lines myself; **[S]** = established by a sub-audit and not independently re-read by me.

| Cursor | Display predicate | Execution predicate | Verdict |
|---|---|---|---|
| `enter` on own **Supply Route** | `DeliversCash.cs:126` (prio 5) + `:128-139`; cursor `:38` | `DeliversCash.cs:88-92` → `GoDonateCash:101-108` — **ignores the target**, queues `RotateToEdge` | **DIVERGE (semantic)** [R] |
| `attackmove` | `AttackMove.cs:140-154` — terrain + explored only, **no ammo test** | `AttackMove.cs:109` — `!order.Queued && AmmoPool.CannotFight(self)` → drop | **DIVERGE** [R] |
| `repair` (engineer wrench) | `AttackBase.cs:809` — armament-scoped dryness | `AttackBase.cs:502` — actor-scoped `AmmoPool.CannotFight` | **DIVERGE** [R] |
| `attack` w/ paused armament | `AttackBase.cs:807` — *deliberately* picks a paused armament | `Armament.cs:327` `CanFire` false; `Attack.cs:256` only bails if opted in | **DIVERGE (cat. 3)** [R] |
| `enter-blocked` on a **full** transport | `EnterAlliedActorTargeter.cs:35-50` — `useEnterCursor` picks *art only*, returns `true` | `Passenger.cs:201-202` — `!CanEnter` → drop | **DIVERGE (soft)** [R] |
| `guard` (command bar) on a dry unit | `GuardOrderGenerator.cs:63-73` — `GuardableInfo` only | `Guard.cs:51-59` → `AttackMoveActivity.cs:93` ends at once | **DIVERGE** [S] |
| `deploy` (Cargo unload) | `Cargo.cs:346-347` → `CanUnload():435-447`, `BlockedByActor.None` | `UnloadCargo.cs:104-116` — `GetAvailableSubCell` default `BlockedByActor.All` | **DIVERGE** [S] |
| `deploy` (Minelayer, no mines) | `Minelayer.cs:103` — terrain only, no ammo test | `Minelayer.cs:152-156` — also no ammo test; `LayMines.cs:94-104` returns | **DIVERGE (cat. 3)** [S] |
| `enter` on a **frozen** vehicle (CrewMember) | `EnterAlliedActorTargeter.cs:52-70` — paints full green `enter` | `CrewMember.cs:88` — `Type != TargetType.Actor` → drop | **DIVERGE (hard)** [S] |
| `attack` on a **frozen** Supply Route | `AttacksSupplyRoutes.cs:122-133` | `AttacksSupplyRoutes.cs:77` — requires `TargetType.Actor` | **DIVERGE (latent)** [S] |
| `assaultmove` / `assaultmove-blocked` | never emitted — `AttackMove.cs:117` hardcodes `assaultMoving = false` | n/a | **DEAD UI** [R] |
| `move` onto a **passable but unreachable** cell | `Mobile.cs:1180` — single-cell `MovementCostForCell`, actor-blind | `PathFinder.cs:122-123` `NoPath`; `Move.cs:146-152`, `:173-177` completes without moving | **DIVERGE** [S] |
| `move-blocked` (ground) | `Mobile.cs:1178-1181` | `Mobile.cs:1027-1028` / `Move.cs:142` | AGREE [S] |
| `move-rough` (`TerrainCursors`) | `Mobile.cs:1184`, dict declared `Mobile.cs:70` | — | **DEAD UI** — dict never populated in `mods/` [R] |
| `move` / `move-blocked` (**air**) | `Aircraft.cs:1572` — blocks on `IsTraitPaused` | `Aircraft.cs:1254` — checks `IsTraitDisabled` only, queues `Fly` anyway | **MIRROR (over-warns)** [R] |
| `deploy` (queued `GrantConditionOnDeploy` / `DeployTransform`) | `DeployOrderTargeter.cs:20`, `:38` — zero-arg `Func<string>`, reads `self.Location` **now** | `Transforms.cs:146` skips re-check when queued; `GrantConditionOnDeploy.cs:257-258` returns mute | **DIVERGE (queued only)** [S] |
| `deploy` (`DropSupplyCache`) | `DropsSupplyCache.cs:560-561` → `CanDropCache():132-139` | `:214-218` → `UnloadSupplyCache.cs:41-44` | AGREE [S] |
| `enter` (`PickupSupply` / `Restock` / `DeliverSupply`) | `DropsSupplyCache.cs:626-642`, `:655-686`, `:699-714` | `:259-270`, `:273-288`, `:291-312` — all strictly looser | AGREE [S] |
| `enter` / `enter-blocked` (aircraft rearm pad) | `Aircraft.cs:1188` via `EnterAlliedActorTargeter.cs:48` | `Aircraft.cs:1303-1308` — identical conjunction re-tested | AGREE [S] |
| rally point (`move` / `ability` / `attackmove`) | `RallyPoint.cs:233-274` | `RallyPoint.cs:180-199` | AGREE [S] |
| `attack` / `attackoutsiderange` (in ammo) | `AttackBase.cs:809-818` | `AttackBase.cs:502` | AGREE [R] |
| force-fire at ground | `AttackBase.cs:844` `CannotFight` | `AttackBase.cs:502` — same function | AGREE [R] |
| `c4` (Demolition) | `Demolition.cs:143` `IDemolishable.IsValidTarget` | `Demolition.cs:98` — same predicate | AGREE [S] |
| `goldwrench` (technician capture) | `Captures.cs:151` → `CaptureManager.CanTarget:120-154` | `CaptureManager:156-167` — same manager | AGREE [S] |
| `enter` (soldier "clear" capture) | `Captures.cs:145` → same manager | same | AGREE on order; outcome legibility gap [S] |
| `goldwrench` (bridges) | `RepairsBridges.cs:131-165` | `RepairsBridges.cs:96-112` | AGREE [S] |
| `enter` (Repairable) | `Repairable.cs:100-106` | `Repairable.cs:138` — strictly more permissive | AGREE [S] |
| `heal` (AttendAlly) | `AttendAlly.cs:96-102` | `AttendAlly.cs:126-141` | AGREE except classic-mouse (below) [S] |
| `deploy` (GarrisonManager) | `GarrisonManager.cs:1350` — hardcoded, but gated on emission `:1344-1348` | `:1441-1472` unconditional | AGREE [S] |
| stance / engagement / cohesion / **resupply-behaviour** | 4 selectors, all `.Where(at => at.Info.EnableStances)` (`StanceSelectorLogic.cs:73`, `EngagementStanceSelectorLogic.cs:73`, `CohesionSelectorLogic.cs:74`, `ResupplyBehaviorSelectorLogic.cs:75`) | `AutoTarget.cs:578-588` — same `Info.EnableStances` gate | **AGREE** [R] |

**On the stance and resupply-behaviour orders specifically:** these are command-bar orders, not
target-clicks, so they have no cursor by design — that is not the mirror defect. The display/execution
pair that *does* exist for them (the four selector widgets vs `AutoTarget.ResolveOrder`) reads the
identical `EnableStances` flag on both sides. Only one actor in the mod sets it false
(`aircraft-america.yaml:613`), and the selectors filter that actor out. **This family is clean.**

---

## Ranked divergences

Ranked by *how often a player hits it* × *how misleading it is when they do*.

### 1. `enter` on your own Supply Route rotates the unit off the map and sells it — CLOSED, NOT A DEFECT

> **User ruling, 2026-08-30 — intended, no change:** *"It is just a way to order evacuation by
> in-game click (no hotkey needed), I don't see any issue. The icon is okay, there is nothing better
> I think."*
>
> **The reasoning, which is the part this audit was missing:** the mouse affordance **existing
> without a hotkey is the point**. It is not a redundant duplicate of the Evacuate command-bar
> button — it is the no-hotkey route to the same order, and `enter` is an accepted fit for "send
> this unit into the Supply Route and off the map".
>
> **Every fact below is correct and was verified.** The cursor does mean something else elsewhere,
> the consequence *is* irreversible, and the resolver *does* discard the target it was handed. None
> of it added up to a defect. **A divergence between what a pointer conventionally means and what an
> order does is evidence, not a conclusion** — re-deriving these facts will reproduce the same
> alarming and correct analysis, so the ruling is also pinned in code at `DeliversCash.cs:101`
> (`GoDonateCash`, the `Rotation` branch) where the next reader will trip over it first.
>
> Retained below unchanged as the worked example of that lesson. **Do not re-file.**

The mechanism, for anyone who rediscovers it:

- **Display:** `DeliversCashOrderTargeter`, priority **5**, cursor `"enter"`
  (`DeliversCash.cs:38`, `:126`, `:128-139`). It accepts any target carrying
  `AcceptsDeliveredCash` whose `ValidTypes` contains the unit's `Type`.
- **Execution:** `ResolveOrder:88-92` → `GoDonateCash:94-108`. For `Type == "Rotation"` it
  **ignores `target` entirely** and queues `RotateToEdge(self, true, amount)` — the unit walks to
  the nearest **map edge** and is permanently removed for a refund.

Reachability, all verified in YAML:
- `DeliversCash@Rotation: Type: Rotation` is on `^Infantry` (`infantry.yaml:156`), `^Vehicle`
  (`vehicles.yaml:102`) and aircraft (`aircraft.yaml:148`, `:198`) — i.e. **every combat unit**.
- `SUPPLYROUTE:` (`structures.yaml:222`) carries `AcceptsDeliveredCash: ValidTypes: Rotation`
  (`:370-371`). Ordinary `^Building` accepts only `MoneyTransfer` (`:123-124`), so the SR is the
  sole target.
- A player's relationship with themselves is `Ally` (`Player.cs:250-251`) and the targeter passes
  `targetAllyUnits: true`, so the ally guard at `UnitOrderTargeter.cs:54` does not reject.
- Priority **5** beats `Mobile.MoveOrderTargeter`'s **4** (`Mobile.cs:1163`), and Mobile refuses
  actor targets outright anyway (`:1166`). `OrderForUnit` returns the first accepting targeter, so
  `DeliverCash` wins on the first pass. There is no competing trait: SUPPLYROUTE has no `Cargo`.

So: **select any unit, right-click your own Supply Route, and it walks off the map and is sold.**
The cursor shown is `enter`, which everywhere else in this game means "get inside this building".
The Supply Route is the player's home structure and one of the most-clicked objects on the map.

Note the mechanic itself is intended and wanted — "rotating out" is the economy loop
(`game-model.md:13`) — and it already has a **dedicated affordance**: an Evacuate command-bar button
and hotkey (`chrome/ingame-player.yaml:322`, `CommandBarLogic.cs:255`, resolved at
`DeliversCash.cs:82`). The right-click path is a *second*, undocumented affordance wearing a
borrowed cursor.

One further inconsistency worth noting: the how-to-play text says units evacuate *"leaving via your
Supply Route"* (`chrome/ingame-info-howtoplay.yaml:144`), but `RotateToEdge` walks to the **map
edge**, not to the SR. The documented mental model and the code disagree independently of the cursor.

**Resolved: neither.** The gesture and the icon both stand — see the ruling at the top of this
section.

### 2. `attackmove` on an out-of-ammo unit does nothing at all — VERIFIED [R] — **FIXED 2026-08-30**

> **Fixed on this branch.** Display-side only, no new art, no behaviour change: both cursor paths
> now show the existing `attackmove-blocked` when the order would be refused.
> `AttackMoveTargeter.CanTarget` (`AttackMove.cs:145-162`) mirrors `ResolveOrder`'s gate per-unit;
> `AttackMoveOrderGenerator.GetCursor` (`:229-244`) mirrors it across the whole selection, because
> that path issues one grouped order and the click still achieves something while **any** subject
> can fight. **Both keep the execution gate's `!order.Queued` scoping** — a shift-queued attack-move
> on a dry unit is deliberately accepted and runs once the unit rearms, so blocking it on display
> would have traded this lie for its exact mirror. Build clean, 1960/1960 NUnit green.

What it was, before the fix above:

- **Display:** `AttackMoveTargeter.CanTarget` checked target type, `IMove` presence, and whether the
  cell was explored. **It never consulted ammo.** Cursor: clean `attackmove`.
- **Execution:** `AttackMove.ResolveOrder:109` — `if (!order.Queued && AmmoPool.CannotFight(self))
  return;` — drops the order silently.

Alt-click is a core gesture and units run dry constantly. The player sees an unambiguous
`attackmove` cursor over open ground, clicks, and the unit does not move — **not even the plain Move
it would otherwise have made**, because the targeter won at priority 4 and consumed the click.

The refusal is deliberate and well-reasoned (the comment at `:104-108` explains the queued/unqueued
split). **Only its silence is the defect.**

**Fix taken: stopped showing the cursor** — swapped to the existing `attackmove-blocked` art. Note
the click is still consumed and the unit still gets no plain-Move fallback; that was deliberate and
is unchanged. The blocked art is now the thing that tells the player why.

### 3. `move` onto a passable-but-unreachable cell — VERIFIED [S]

The most-clicked cursor in the game, and the two sides ask different questions:

- **Display:** `MoveOrderTargeter` asks only `Locomotor.MovementCostForCell(location)`
  (`Mobile.cs:1180`) — terrain-only, **single-cell**, actor-blind, and with no `fromCell` it skips
  even the height-discontinuity rule. A grass cell on the far side of a river passes.
- **Execution:** `FindPathToTargetCell` runs a full search and returns `NoPath` when the destination
  is disconnected (`PathFinder.cs:122-123`). `Move.OnFirstRun` tries all four `BlockedByActor` levels
  and ends with an empty path (`Move.cs:146-152`); `Move.Tick` then sets `destination = mobile.ToCell`
  and completes (`:173-177`).

The unit does not move, does not turn, and says nothing — there is no failure sound or text on this
path. `tools/nav-guard/` exists precisely because sealed pockets occur on shipped maps.

This ranks below the two above it despite being the commonest cursor, because it is the *least
surprising* lie: every RTS behaves this way, and the player generally reads it as "bad click" rather
than "the game lied". It is listed high only because of volume.

**Honest fix: make the order work** — clamp to the nearest *reachable* cell rather than the nearest
*enterable* one. `Move` already relocates via `NearestMoveableCell` (`Mobile.cs:834-855`), but that
helper tests `CanEnterCell`/`CanStayInCell` only, so on an unreachable island every candidate cell
also passes and the relocation is a no-op. **Suppressing the cursor instead would leak map
connectivity through the pointer** and is the worse option — it would tell the player through fog
which cells are sealed.

### 4. `repair` on an ammo-dry engineer does nothing — VERIFIED [R]

The two sides ask the same question with **different scopes**, and this is the cleanest instance of
"two predicates that look equivalent, are not" in the codebase:

- **Display** (`AttackBase.cs:809`): `armaments.All(a => a.AmmoPool != null && !a.AmmoPool.HasAmmo)`
  — **armament-scoped**, and an armament with no pool at all makes the whole test false.
- **Execution** (`AttackBase.cs:502`): `AmmoPool.CannotFight(self)` = `AllPoolsEmpty(self) &&
  !HasTraitInfo<AircraftInfo>()` (`AmmoPool.cs:585-588`) — **actor-scoped**.

The engineer `^E6` has `Armament@Repair` and `Armament@ClearMines` (`infantry.yaml:1953-1962`) that
appear in **no** `AmmoPool.Armaments` list — the two pools cover `primary` and `secondary` only
(`infantry.yaml:1901`, `:1932`). So `Armament.AmmoPool` (`Armament.cs:226-233`) returns `null` for
the wrench, the display test is false, and the `repair` cursor lights. Once the MP5 **and** all 3 C4
are spent, `AllPoolsEmpty` is true, and the order is dropped at `:502`.

Player right-clicks a damaged friendly vehicle with the repair cursor lit and gets nothing — no
move, no voice, no wrench.

**Honest fix: make the order work.** The execution test should be armament-scoped like the display
test. A dry rifle has no bearing on whether a man can hold a wrench. (Marked as a design call because
`:502`'s actor-scoped form is deliberate and shared — the comment at `:495-501` explains it — so
narrowing it needs care and is not a one-liner.)

### 5. `attack` on a paused armament: drives over, aims, fires nothing, never goes idle — VERIFIED [R]

This mechanism is already documented (`DOCS/reference/conventions.md:346`); what this audit adds is
**the live census**. `AttackOrderTargeter` *deliberately* selects a paused armament
(`AttackBase.cs:807`: `FirstOrDefault(x => !x.IsTraitPaused) ?? armaments.First()`) and
`ChooseArmamentsForTarget` filters `IsTraitDisabled` but not `IsTraitPaused`. So the cursor shows and
the order is accepted. `Armament.CanFire` then declines on pause (`Armament.cs:327`), and because
`Attack.TickAttack` still reports `Attacking`, **the activity never completes and every `INotifyIdle`
behaviour — auto-target, auto-follow, auto-rearm — is silenced for the duration.**

The escape is `AttackBaseInfo.AbandonWhenArmamentsPaused` (`AttackBase.cs:72`, default `false`).
**Exactly one actor in the entire mod opts in** — `^MEDI` (`infantry.yaml:2308`) — against **64**
armament-level `PauseOnCondition` gates in non-husk rules.

The widest live entrance: **every armed vehicle** carries `|| empdisable ||
heavy-damage-attained` on its armaments, and `heavy-damage-attained` is granted at damage state
Heavy *and* Critical (`defaults.yaml:243-245`). A tank at heavy damage shows a normal `attack`
cursor, drives into range, aims, and never fires. [census S, mechanism R]

**Honest fix: split the decision.** For the vehicle case the refusal is intended balance, so the fix
is **display-side** — the targeter knows it picked a paused armament at `:807` and should say so with
a blocked variant. Separately, traits with long-lived pauses should opt into
`AbandonWhenArmamentsPaused` so the unit at least drops to idle instead of aiming forever. Do **not**
widen the abandon test to "cannot fire": `CanFire` is also false on `IsReloading`/`IsWaitingBurst`/
`IsAiming`, which are true on ordinary ticks of a healthy weapon.

### 6. `enter-blocked` on a full transport consumes the click and kills the move fallback — VERIFIED [R]

`EnterAlliedActorTargeter.CanTargetActor` (`:35-50`) gates acceptance on `canTarget`
(`IsCorrectCargoType` — cargo type + `LoadingBlocked`, `Passenger.cs:105-121`) and uses
`useEnterCursor` (`CanEnter`, which includes `HasSpace`, `:123-126`) **only to choose the cursor
art**. It then returns `true` regardless. `Passenger.ResolveOrder:201-202` drops the order on
`!CanEnter`.

The cursor art itself is honest — the player is shown `enter-blocked`. The real harm is second-order:
because the targeter returned `true` at priority **5**, it consumed the click, so the player also
**loses the move they would otherwise have got** onto that cell. Right-clicking a full APC does
nothing whatever. Every shipped APC has `MaxWeight` 6–12 against infantry `Weight: 1`, so this is
reached constantly. [S, predicates re-read by me]

**Honest fix: make the order work** — queue `RideTransport` anyway and let it cancel on arrival if
still full, which is the skip-ahead behaviour the file already implements for shift-queued chains.

### 7. `guard` on a dry unit never follows — VERIFIED [S]

`GuardOrderGenerator.GetCursor` (`:63-73`) filters on `GuardableInfo` only; `Guard.GuardTarget`
(`:51-59`) queues an `AttackMoveActivity` that ends immediately on `AmmoPool.CannotFight`
(`AttackMoveActivity.cs:93`). Same shape as #2. **Honest fix: stop showing the cursor** — fold the
ammo test into the generator's `canGuard` and fall back to its existing `move-blocked`.

Related, same trait: `CommandBarLogic.cs:123` passes the **whole** selection to the generator, whose
constructor does not filter by `GuardInfo` (only `SelectionChanged:58` does), and the button is
enabled if *any one* actor qualifies. With a mixed selection the cursor promises for all and only
some obey.

### 8. `deploy` promised where nobody can get out — VERIFIED predicates [S], INFERRED permanence

`Cargo.CanUnload()` (`:435-447`) asks `CanEnterCell(c, null, BlockedByActor.None)` — **terrain only,
blocking actors ignored**. `UnloadCargo.ChooseExitSubCell` (`:104-116`) then uses
`GetAvailableSubCell`'s `BlockedByActor.All` default and returns null, so the activity fires
`NotifyBlocker` + `Wait(10)` and re-tests the same permissive gate at `:208`.

With mobile neighbours this is a delay, not a refusal. **Not settled by reading:** whether an
immobile ring (a transport parked between buildings) makes it permanent. **Honest fix: stop showing
the cursor** — give the display call site `BlockedByActor.All` so it reads `deploy-blocked`, and
terminate the activity rather than looping.

### 9. A queued `deploy` is judged against the wrong cell, and fails mute — VERIFIED code [S]

`DeployOrderTargeter` takes its cursor from a **zero-argument** `Func<string>`
(`DeployOrderTargeter.cs:20`, `:38`). It has no access to the order queue, so both
`GrantConditionOnDeploy.CanDeploy()` and `Transforms.CanDeploy()` answer for `self.Location`
*right now*. Shift-click a deploy after a move and you get a green `deploy` cursor for a decision
that will actually be taken somewhere else entirely.

Worse, the refusal is then silent: `Transforms.DeployTransform` explicitly skips its own re-check
when `queued` (`Transforms.cs:146`), so the "cannot deploy here" notification at `:151-157` never
plays, and `Transform.Tick:52-55` returns without a word. `GrantConditionOnDeploy` has no feedback
branch at all — `Deploy()` just returns at `:257-258`.

**Honest fix: make the order work** — surface the same refusal notification from the activity, so a
queued deploy that lands badly is at least audible. `DropsSupplyCache` already gets this right and
documents why (`:583-593`), so the pattern to copy is in-tree.

### 10. `deploy` on a minelayer with no mines — VERIFIED [S]

`Minelayer.Orders` (`:98-105`) and `ResolveOrder` (`:152-156`) both check terrain and **neither
checks ammo**; `LayMines.cs:94-104` discovers the empty pool and returns. Note its sibling
`Demolition` *is* gated (`RequiresCondition: ammo-secondary`, `infantry.yaml:2174`) while
`Minelayer` is not. **Honest fix: stop showing the cursor** — add `&& pool.HasAmmo` to the `Func`
at `Minelayer.cs:103`, which makes the existing `deploy-blocked` art carry the message.

### 11. `enter` on a fogged vehicle (CrewMember) — VERIFIED [S]

`CanTargetFrozenActor` (`EnterAlliedActorTargeter.cs:52-70`) reads the **real** actor's crew state
and paints the full green `enter`; `CrewMember.ResolveOrder:88` discards any non-`Actor` target.
Green cursor, nothing happens. `Passenger` handles this case correctly (`:133-141`, `:194-199`), so
the fix is to copy that pattern — **make the order work**. Secondary concern, out of scope: that
same method dereferences `target.Actor`, leaking live crew state through fog.

### 12. Dead and latent, for completeness

- **`assaultmove` / `assaultmove-blocked` are dead UI** — `AttackMove.cs:117` hardcodes
  `assaultMoving = false` with the comment `// WW3MOD: AssaultMove disabled`, and the generator does
  the same. The cursors remain defined at `cursors.yaml:221-229`. [R]
- **`guard` has no `IOrderTargeter` at all** — it is reachable only via the command bar
  (`chrome/ingame-player.yaml:154`). Right-click never produces it. This is the *absence* case, and
  it is a deliberate design choice rather than a defect. [S]
- **Frozen Supply Route** — `AttacksSupplyRoutes.CanTargetFrozenActor` (`:122-133`) accepts what
  `ResolveOrder:77` rejects. Unreachable today only because `SUPPLYROUTE` sets
  `AlwaysVisibleRelationships: Ally, Neutral, Enemy` (`structures.yaml:262-263`); narrow that YAML
  line and the divergence goes live silently. [S]
- **`heal` under classic mouse style** — the previously recorded entry
  (`WORKSPACE/bugs/discovered.md:63`) still describes the code accurately. **One citation has
  drifted:** `TargetOverridesSelection` is now at `AttendAlly.cs:109-118`, not `:101-110`. A sweep
  confirmed `AttendAlly` is the **only** targeter with a narrowed `TargetOverridesSelection`; every
  other one returns unconditional `true`. [S]
- **`Aircraft.Orders` lacks the disabled-guard `Mobile` has.** `Aircraft.Orders` (`:1170-1192`) is
  not gated on `!IsTraitDisabled` while `Aircraft.IssueOrder` (`:1196`) is — so a disabled `Aircraft`
  trait would show a cursor and then log the engine's own
  `"BUG: in order targeter - decided on Move but then didn't order"`. This is the only **category-1**
  candidate found in the whole audit. `Mobile.cs:1002` has the guard Aircraft lacks. **Currently
  unreachable** — no `RequiresCondition` is set on any `Aircraft:` block in `mods/`. Worth a one-line
  guard for symmetry, not a live bug. [S]
- **Aircraft are exempt from `CannotFight`** (`AmmoPool.cs:587`), so for aircraft the *display* test
  at `:809` is **stricter** than the execution test at `:502`. That is the safe direction — no false
  promise — but it means an empty aircraft shows no attack cursor even though the engine would accept
  the order. Worth knowing before anyone "fixes" the asymmetry in the wrong direction. [R]

### Mirror cases (cursor says no, game says yes)

- **Shift-queued unload:** `Cargo.cs:393` skips `CanUnload()` when `order.Queued`, so
  `deploy-blocked` + shift queues a working unload. Defensible — conditions change by execution
  time — but the cursor is lying at click time. [S]
- **Queued attack / attack-move on a dry unit:** both `:502` and `AttackMove.cs:109` are scoped to
  `!order.Queued`, so a shift-queued order behind a resupply is accepted while the unqueued one is
  refused, with no cursor difference between them. This is deliberate and documented. [R]
- **Paused aircraft over-warn.** `AircraftMoveOrderTargeter` shows `BlockedCursor` when
  `aircraft.IsTraitPaused` (`Aircraft.cs:1572`), but `Aircraft.ResolveOrder` checks only
  `IsTraitDisabled` (`:1254`) and queues `Fly` regardless; `MovementSpeed` is 0 while paused so the
  flight simply resumes on unpause. **`Mobile` was deliberately moved away from exactly this
  mapping** — the comment at `Mobile.cs:1174-1177` states the reasoning outright ("Showing
  BlockedCursor for a transient pause is misleading; only flag truly unreachable destinations") —
  **and Aircraft was not brought along.** Live entrances: `^Drone` pauses on `dronedisable`
  (`aircraft.yaml:331`) and `^AircraftAffectedByEMP` on `empdisable` (`:380`). Over-warning is the
  safe direction, so this ranks low, but it is a one-line inconsistency with a documented
  counterpart. **Honest fix: stop showing the blocked cursor** — drop the `!aircraft.IsTraitPaused`
  term so air matches ground. [R]

### Legibility gaps (cursor is honest about the order, unclear about the outcome)

Not divergences — listed because they are the same player-facing complaint and cheap to fix.

- **`DeliverSupply` and `Restock` share one cursor** (`DropsSupplyCache.cs:653`, `:697` both use
  `info.RestockCursor`), so Ctrl+click and plain right-click on a Logistics Centre look identical.
  The comment at `:657-664` records that this cursor identity previously hid a fully-dead feature
  for months. The order layer is now correct; the pointer still cannot tell the two apart. [S]
- **Soldier "clear" capture shows a generic `enter`.** Display and execution agree — both route
  through the same `CaptureManager` — but `enter` does not tell a new player the building will go
  **Neutral** rather than become theirs, and `goldwrench` does not tell them the technician **dies**
  (`game-model.md:33-47`). An icon/tooltip problem, not an order-honesty problem. [S]

---

## What could not be settled by reading

- Whether the boxed-in `Cargo` unload loop (#7) is genuinely permanent in play, or resolves once a
  neighbour moves.
- Whether the paused-armament wedge (#4) is observable as "tank sits still" in a real match, or is
  masked by the unit dying first.
- `Attack.cs:275-278` treats `losBlocked` as `needsToMove` and re-queues `MoveWithinRange` even when
  already inside `maxRange`, while `TargetInFiringArc` (`AttackBase.cs:269-271`) refuses on the same
  LOS test. Whether that loops or converges is runtime behaviour.

Each of these needs a launch slot.

## Incidental, filed separately

`defaults.yaml` declares `^AutoTargetAir:` twice (`:554`, `:706`) and `^AutoTargetGroundAntiInf:`
twice (`:606`, `:642`). MiniYaml merges adjacent top-level entries silently. Logged in
`WORKSPACE/bugs/discovered.md`.

---

# Decision groups for findings 3–11

Written 2026-08-30 after findings 1 (closed, not a defect) and 2 (fixed) were resolved. The
remaining nine are **not nine questions**. They collapse into **three genuine choices, one
standalone, and one batch that needs no decision at all**. For each, the point is where the two
directions diverge in *consequence*, not in implementation.

## Decision A — readiness gates: should an unqueued order on a not-ready unit be *refused* or *remembered*?

**Covers findings 5 (paused armament), 7 (`guard` on a dry unit), 10 (minelayer with no mines).**

What the player notices today: the cursor promises, the click lands, nothing happens, and no sound
or message explains it. What they'd notice after either fix: something honest — but two very
different somethings.

**Where the directions actually diverge:** today, `!order.Queued` is load-bearing punctuation.
Shift-queued means *"when you are able"*; unqueued means *"now"*, and "now" is legitimately
refusable. **Suppressing the cursor keeps that distinction and makes readiness visible in the
pointer** — the pointer becomes a live readout of a state the player can already see in ammo pips
and damage decoration, so nothing is leaked. **Making the order remembered deletes the
distinction** — unqueued and shift-queued would come to mean the same thing, and the player loses
the ability to say "go now" and be told no.

That is the whole question, and it is one question, not three. My read: suppress. It preserves an
existing, deliberate, documented semantic.

**Finding 4 (engineer `repair`) is a sub-case with a clearly-right answer** and probably should not
be part of the vote: it is not a missing test but a **scope mismatch** — display is armament-scoped
(`AttackBase.cs:809`), execution is actor-scoped (`:502`), and a spent rifle vetoing a wrench is
indefensible under either direction above. It only needs a ruling because `:502`'s actor scope is
deliberate and shared with six other call sites.

> **NOT PRE-AUTHORISED — suppression-adjacent.** Finding 5's live gates include
> `PauseOnCondition: suppressed >= 10` on `^AT` (`infantry.yaml:1739`) and the engineer's repair
> armament (`:1956`), and finding 4 is that same engineer armament. Any work here touches the
> suppression mechanic and needs explicit sign-off beyond this group's direction.
>
> Finding 5 also carries a **separate and worse problem that is not a cursor decision at all**: the
> unit accepts, walks over, aims, and never goes idle, silencing auto-target, auto-follow and
> auto-rearm for the duration. That wants `AbandonWhenArmamentsPaused` regardless of which way A
> goes. Do not let the cursor question absorb it.

## Decision B — silent failure at the destination: tighten the cursor, or make the failure audible?

**Covers findings 8 (`deploy` where nobody can exit) and 9 (queued deploy judged against the wrong cell).**

**Where the directions diverge:** these two look identical and are not, because in one the cursor
*can* know and in the other it *cannot*.

- **Finding 8 — the cursor can know.** `Cargo.CanUnload()` deliberately passes
  `BlockedByActor.None` while the activity uses `BlockedByActor.All`. Tightening the display test is
  a one-argument change. The information consequence is mild: it reveals whether the cells around
  **your own transport** are occupied, which is your own information. There is a real cost though —
  the cursor would then flicker as neighbours walk past, and a "blocked" that clears in half a
  second is its own kind of lie.
- **Finding 9 — the cursor cannot know.** `DeployOrderTargeter` takes a **zero-argument**
  `Func<string>` (`DeployOrderTargeter.cs:20`). It has no access to the queue and therefore cannot
  answer for a cell the unit has not reached yet. **Tightening is not available here**; only making
  the refusal audible from the activity is. `DropsSupplyCache` already does this and documents why
  (`:583-593`), so the pattern is in-tree.

So B is really: *do we accept a flickering cursor for 8, and do we spend the plumbing to make
deploy failures audible for 9?* If the answer to the second is yes, it subsumes the first — an
audible failure fixes both without touching either display test.

## Decision C — a targeter that consumes the click and then discards it

**Covers findings 6 (full transport) and 11 (`enter` on a fogged vehicle).**

**This is the only group where the fix changes what a click *does*, not just what it shows** — so it
carries the most regression risk and deserves the most care.

**Where the directions diverge:** the shared harm is that `CanTarget` returns `true` at priority 5,
consuming the click, and `ResolveOrder` then drops it — so the player loses the **plain Move they
would otherwise have received**. Two ways out, and they give the player *different things*:

- **Return `false` from the targeter** → the click falls through to Move. The player right-clicks a
  full APC and their infantry walks to it. Good for finding 6, where "walk over there and wait for
  a seat" is usually what was meant. **Costs the `enter-blocked` art its only job** — the player is
  no longer told the transport is full, they just move.
- **Make the order work** → queue the action and let it resolve on arrival. Good for finding 11,
  where the target is a fogged vehicle the player genuinely wants to crew and `Passenger` already
  implements the correct frozen-target pattern (`:133-141`, `:194-199`) for `CrewMember` to copy.

Note these pull opposite ways for the two findings, which is exactly why they should be decided
together rather than one at a time.

## Standalone — finding 3, the pathing one

**Only one direction is actually available, so this is a "do we spend it?" question, not a
direction question.**

Suppressing the `move` cursor over unreachable ground would require a **full pathfind per mouse
move** (cost), and — decisively — **it would leak map connectivity through the pointer**: the player
could sweep the mouse across unexplored terrain and read off which regions are sealed, without ever
scouting. That is information the fog is there to withhold. **Suppression is disqualified on
correctness, not on effort.**

That leaves making the order work: clamp to the nearest *reachable* cell rather than the nearest
*enterable* one. `Move` already relocates via `NearestMoveableCell` (`Mobile.cs:834-855`), but that
helper tests `CanEnterCell`/`CanStayInCell` only, so on an unreachable island every candidate also
passes and the relocation is a no-op. This touches the most-used order in the game and is the
highest-risk item in the whole audit.

## Batch — no decision needed, low risk, approve or drop as one

- **Aircraft pause over-warns** (`Aircraft.cs:1572` blocks on `IsTraitPaused`; `:1254` does not).
  One-line deletion, and `Mobile.cs:1174-1177` already documents the rationale for the ground
  equivalent — Aircraft was simply not brought along.
- **`assaultmove` / `assaultmove-blocked` and `move-rough` are dead art** — no code path emits
  them. Delete or leave; no behaviour either way.
- **`DeliverSupply` and `Restock` share one cursor** (`DropsSupplyCache.cs:653`, `:697`). Cursor
  identity here previously hid a fully-dead feature for months (`:657-664`). Cheap legibility fix.
- **`Aircraft.Orders` lacks the `!IsTraitDisabled` guard `Mobile.cs:1002` has.** Unreachable today;
  a one-line guard for symmetry.
- **Finding 12's frozen Supply Route** is latent-only, held shut by one YAML line
  (`structures.yaml:262-263`). Worth a comment so narrowing that line does not silently open it.
