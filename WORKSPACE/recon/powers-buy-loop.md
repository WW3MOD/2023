# Buying, banking and firing missile powers — verification of the proxy route

**Researched against `main @ 2c8488ef`** in worktree `wt/powers-buyloop` (`git status -sb` → `## wt/powers-buyloop`, no upstream to be behind; tree clean at start). **Static analysis only — no game runs, no autotests, no YAML lint, no screenshots.** Every claim carries a `file:line` I read at this SHA. Nothing below was taken from the prior recon without re-reading the code.

**Timestep:** 60 ms ⇒ **16.667 ticks/s**. `seconds = ticks × 0.06`.

**Scope.** Verifies `WORKSPACE/recon/powers-and-preloaded-transports.md` §1.5 / §1.8 and answers the lifecycle and lobby questions it left open. It advocates for nothing.

---

## 0. Headline findings

1. **The proxy buy→bank→spend loop works and needs zero new engine traits — confirmed, and better supported than the prior recon claimed.** `Production.Produce`'s bodiless branch (`Production.cs:126-131`) is not an accident of the code: `ProximityExternalCondition.UnitProducedByOther` opens with `if (produced.OccupiesSpace == null) return;` (`ProximityExternalCondition.cs:138-140`), an explicit engine guard for exactly this case, live in this mod at ten sites in `husks.yaml`. §1.
2. **The prior recon's "second trap" does not exist, and its cited evidence is a misread.** It says `RankAccumulation` would throw on a proxy, citing `player.yaml:22` as carrying `Types: Infantry, Vehicle, Plane, Ship`. `player.yaml:22` is a bare `RankAccumulation:` with **no fields**; the `Types:` at `:17` belongs to **`ProximityCaptor:`** (`:16`), and `RankAccumulationInfo` has no `Types` field at all (`RankAccumulation.cs:276-306`). The real guard is `Accrues()` (`:354-357`), which requires `GainsExperienceInfo` — so `PeekRank` returns 0 (`:401-404`), `rank > 0` is false, and `VeterancyLevelInit` is never constructed (`ProductionQueue.cs:734-736`). **A proxy is safe on any queue, new or existing.** §1.3.
3. **There is a real trap nobody named, and it is a hard crash rather than a silent hang.** `ProductionPaletteWidget.cs:683` calls `item.TraitInfo<RenderSpritesInfo>()` **unguarded**, and `ActorInfo.TraitInfo<T>()` throws (`ActorInfo.cs:188` → `TypeDictionary.cs:65-70`). **Every buy-menu proxy must carry `RenderSprites:`.** The three commented `powerproxy.*` blocks at `misc.yaml:318-368` carry only `AlwaysVisible:` and a power — no `RenderSprites`, no `Buildable`, no `Valued` — because in RA they were crate and infiltration grants, never sidebar items. **They are not a drop-in template for this model.** §1.4.
4. **The `ProductionFromMapEdge` silent-hang trap is real and verified; its line numbers have moved.** `Produce` returns false at `ProductionFromMapEdge.cs:162-163` when no `location` resolved, and `location` is only ever assigned for a producee with `AircraftInfo` or `MobileInfo` (`:98-158`). `ClassicProductionQueue.BuildUnit` then returns false with the item still `Done` in the queue and retries forever (`ClassicProductionQueue.cs:113-133`). The correct target is `Production@Local` at **`structures.yaml:362-364`** (the prior recon says `:361-363`; off by one at this SHA). §1.2.
5. **Charges are unbounded by default. Three YAML caps exist, only one reaches the bank, and that one is broken by the actor leak.** `BuildableInfo.BuildLimit` counts `ActorsHavingTrait<Buildable>()` owned by the player (`ProductionQueue.cs:424-425`, `:469`), and a **spent** proxy is still such an actor. `BuildLimit: 3` therefore means "three airstrikes for the whole match", not "hold three at a time". **If you want a bank cap, disposal stops being optional.** §2.1.
6. **Nothing breaks past six icons.** Six is only the hotkey count (`ingame-player.yaml:29`; `SupportPowersWidget.cs:161` hands `null` past it). Layout is a single unwrapped column at 56 px pitch with no clipping anywhere on the path, the chrome frame is cloned per icon (`SupportPowerBinLogic.cs:56-69`), and `eventBounds` grows to the union (`SupportPowersWidget.cs:168`). At N=8 the column ends at y=448 and collides with nothing. The first collision is with the bottom command bar at the **12th** icon at 720p and the **19th** at 1080p. §2.2.
7. **A banked charge survives the producer's death outright, and the shipped Supply Route can be neither sold nor sensibly destroyed.** The proxy is an independent player-owned actor; `SupportPowerManager.ActorRemoved` keys on the removed actor (`:77-94`) and the SR appears nowhere on that path. `SUPPLYROUTE` (`structures.yaml:222`) inherits only `^ExistsInWorld`, `^SpriteActor`, `^SelectableBuilding` (`:223-225`) — **not `^Building`**, which is where `Sellable:` lives (`:135`, inside `^Building:` at `:68`). §2.3.
8. **Refund on cancel works on the normal path, as a partial refund of what was actually paid.** Money drains per tick during the build (`ProductionQueue.cs:823-828`), and every cancel path refunds `TotalCost - RemainingCost` (`:628`, `:638`, `:189`, `:391`, `:672`). A **contested** Supply Route does not refund — it halts the drip at speed modifier 0 (`:340-357`, `SupplyRouteContestation.cs:443-448`) and the part-paid item freezes. §2.4.
9. **Per-power lobby toggles need ZERO C#, and the master toggle is already sitting in the lobby unwired.** `LobbyPrerequisiteCheckbox` (`LobbyPrerequisiteCheckbox.cs:19-57`) is a fully generic, YAML-declarable checkbox that grants prerequisites; three commented instances already exist at `player.yaml:273, :282, :289` (including `@NuclearAllowed`) and two live ones at `coop-missions-rules.yaml:7, :9`. And `LobbyDummyOptions.cs:217-219` **already publishes a `powers-enabled` checkbox** in the Rules section — a `Placeholder` with no reader. §3.2.
10. **The prior recon's §1.8 claims both hold exactly.** `LobbyChargeIntervalId` has zero readers repo-wide (declaration `SupportPower.cs:25` plus two commented YAML usages); `PowersLobbyOptions.AirstrikesEnabled` / `AirstrikeCooldown` are assigned at `:108-111` and read nowhere. The airstrike checkbox works only through the generic `GrantConditionOnLobbyOption@airstrikes` at `player.yaml:115-118`. §3.1.
11. **Gating the buildable beats gating the power, at identical cost, and the difference is visible to the player.** A `~`-prefixed prerequisite hides the item outright (`TechTree.cs:151-183` → `ProductionQueue.cs:257-259`); an unprefixed one leaves a greyed unbuildable cameo. Gating the *power* instead leaves a **clickable-but-inert icon reading "ON HOLD"** in the top-left (`SupportPowersWidget.cs:241-244`, `ClickIcon` `:176-183`) — or, with `Prerequisites`, lets the player pay for a charge whose icon never appears. §3.4.

---

# 1. Q1 — the proxy purchase route, walked

## 1.1 The path, step by step

Assume a `ClassicProductionQueue@Support` on the Player actor (matching the six that already ship there, `player.yaml:23-93`) with `Type: Support`, and `Support` appended to `Production@Local: Produces:` on `SUPPLYROUTE`.

1. **`ClassicProductionQueue.Tick`** (`ClassicProductionQueue.cs:51-72`) walks `world.ActorsWithTrait<Production>()` and sets `Enabled` from any owned, non-disabled `Production` whose `Produces` contains `Support`. `Production@Local` on the SR qualifies.
2. **`ProductionItem.Tick`** (`ProductionQueue.cs:793-834`) drains cash per tick: `costThisFrame = RemainingCost - expectedRemainingCost`, then `pr.TakeCash(costThisFrame, true)` (`:823-826`). Sets `Done = true` at `:833`.
3. **`ClassicProductionQueue.BuildUnit`** (`:97-134`) enumerates producers, calls `CreateProductionInits` (`ProductionQueue.cs:726-739`), then `p.Trait.Produce(p.Actor, unit, type, inits, item.TotalCost)` at `:122`.
4. **`Production.Produce`** (`Production.cs:120-133`):
   - `IsTraitDisabled || IsTraitPaused || Reservable.IsReserved(self)` at `:121-122`. **`SUPPLYROUTE` has no `Reservable`** — the two in `structures.yaml` (`:621`, `:696`) belong to later actors; the SR body runs `:222-391`. `PauseOnCondition: disabled || build-incomplete` (`:364`) is the only live gate.
   - `SelectExit` at `:126` → `RandomExitOrDefault` / `NearestExitOrDefault` (`Exit.cs:81-99`, `:51-65`). Every `Exit@*` on the SR declares a non-empty `ProductionTypes` list (`structures.yaml:322-361`), none containing `Support`, so `Exits()` filters to empty (`Exit.cs:78`) and the helper **returns `null` cleanly** at `:98` — no throw, no empty-sequence exception.
   - `:127`: `exit != null || self.OccupiesSpace == null || !producee.HasTraitInfo<IOccupySpaceInfo>()`. The **third** disjunct is true for a bodiless proxy ⇒ `DoProduction(self, producee, null, …)` ⇒ `return true`.
5. **`Production.DoProduction`** (`:51-103`) with `exitinfo == null`: the entire positional block at `:60-89` is skipped — no `LocationInit`, no `CenterPositionInit`, no `FacingInit`, no `CreationActivityDelayInit`, no `RallyPointInit`. `td` is a verbatim clone of `inits`. `CreateActor(producee.Name, td)` at `:96`.
6. **`World.CreateActor`** (`World.cs:387-392`) → `new Actor(...)` → `Initialize(true)`. The `Actor` ctor (`Actor.cs:213-238`) constructs every trait; `OccupiesSpace` at `:223` simply stays `null` because no trait matches.
7. **`World.Add`** (`:394-402`) fires `ActorAdded(a)` at `:398` — the event `SupportPowerManager` subscribes to in its ctor (`SupportPowerManager.cs:44`).

### The TypeDictionary the proxy ends up with

Exactly two inits: **`OwnerInit`** and **`FactionInit`** (`ProductionQueue.cs:728-732`). `VeterancyLevelInit` is conditional on `rank > 0` and cannot fire — §1.3.

### Does anything downstream assume a position?

- **`AddToMaps`** (`World.cs:297-302`) — the only thing that enters an actor into `ActorMap` and `ScreenMap` — is called exclusively by `IOccupySpace` traits. A bodiless proxy is **never registered in either map**, so it is never rendered, never mouse-pickable, and never returned by a spatial query. That is also why adding `RenderSprites` to it (§1.4) is inert in-world.
- `INotifyProduction.UnitProduced` fires at `Production.cs:97-99` with `exit = CPos.Zero`. On `SUPPLYROUTE` the only implementor is `GrantExternalConditionToProduced` (`structures.yaml:369-370`), whose body touches no position and null-safes on a missing `ExternalCondition` (`GrantExternalConditionToProduced.cs:34-42`).
- `INotifyOtherProduction.UnitProducedByOther` fires at `Production.cs:101-103` **world-wide**. The implementor in play is `ProximityExternalCondition`, which **guards the bodiless case first** (`:138-140`), with the comment *"If the produced Actor doesn't occupy space, it can't be in range."*
- **`Actor.Location` is `OccupiesSpace.TopLeft` (`Actor.cs:78`) and `CenterPosition` is `OccupiesSpace.CenterPosition` (`:79`).** Both NRE on a bodiless actor. Nothing on the produce path reaches them — but this is the landmine for any trait you later bolt onto the proxy. See §2.5.

**Verdict on Q1's core claim: confirmed.** Zero new engine traits for buy → bank → spend.

Two shipped precedents create a proxy the same way, both through a frame-end task carrying only an `OwnerInit`: `SupportPowerCrateAction.cs:41-44` and `InfiltrateForSupportPower.cs:74-77`.

## 1.2 Trap 1 — `ProductionFromMapEdge` (real; line numbers moved)

`ProductionFromMapEdge.Produce` (`:82-220`) overrides the base entirely:

- `var location = spawnLocation;` (`:92`) — a `CPos?` populated only from a `ProductionSpawnLocationInit` on the **producer** (`:33`, `:44-46`). `SUPPLYROUTE` supplies none.
- The two branches that assign `location` are gated on `aircraftInfo != null` (`:98-112`) and `mobileInfo != null` (`:116-158`). A bodiless proxy has neither.
- `if (!location.HasValue) return false;` (`:162-163`).

`ClassicProductionQueue.BuildUnit` then falls out of its producer loop (`:113-128`); because `anyProducers` is true it does **not** call `CancelProduction` (`:130-131`) — it returns false. The item stays in `Queue` with `Done == true`, `TickInner` calls `BuildUnit` again next tick, forever. **No exception, no log line, no player-visible error: the cameo sits at 100%.**

> **Correction to the prior recon.** It cites `ProductionFromMapEdge.cs:85-86, 96-155, 158-159` and `structures.yaml:361-363` / `:364-366`. At `2c8488ef` those are `:84-85`, `:98-158`, `:162-163`, and `structures.yaml:362-364` (`Production@Local`) / `:365-367` (`ProductionFromMapEdge`). The mechanism is right; every citation drifts by 1–4 lines.

## 1.3 Trap 2 — `RankAccumulation`: DOES NOT EXIST

The prior recon says a proxy on an existing queue would throw at `ProductionQueue.cs:734-736`, and that a new queue type avoids it by staying out of `RankAccumulation`'s `Types`.

**Both halves are wrong.**

- `RankAccumulationInfo` (`RankAccumulation.cs:276-306`) declares six fields: `Rank1BaseIntervalTicks`, `CostReferenceBuildTicks`, `Rank1IntervalMultiplier`, `Rank1MaxIntervalTicks`, `HigherTierIntervalMultiplier`, `Caps`. **There is no `Types` field**, so there is nothing to keep a proxy out of.
- `player.yaml:22` is a bare `RankAccumulation:` with no fields beneath it. The `Types: Infantry, Vehicle, Plane, Ship` at `:17` belongs to **`ProximityCaptor:`** at `:16` — an unrelated trait.
- The actual gate is `Accrues(ActorInfo)` (`:354-357`): `actor.HasTraitInfo<BuildableInfo>() && actor.HasTraitInfo<GainsExperienceInfo>()`. It runs once over every rules actor in the constructor (`:331-346`). A proxy with `Buildable` but no `GainsExperience` **never enters `stocks`**.
- `PeekRank` (`:401-404`) returns `stocks.TryGetValue(...) ? stock.Peek() : 0` ⇒ **0**.
- `ProductionQueue.cs:734-736` reads `rank = rankAccumulation?.PeekRank(unit.Name) ?? 0;` then `if (rank > 0) inits.Add(new VeterancyLevelInit(unit.TraitInfo<GainsExperienceInfo>(), rank));` — the throwing call sits **inside** the `rank > 0` branch and is unreachable.

The doc comment on `Accrues` says so outright (`:349-353`): *"GainsExperience is the gate rather than a hand-kept list … this also guarantees we never hand levels to an actor lacking the trait."*

**A power proxy is safe on any queue.** A separate `Support` queue type is still the right call — its own buy tab, its own `BuildTimeSpeedReduction`, its own `QueueLimit`/`ItemLimit` — but it stands on those merits, not on avoiding a trap that is not there.

## 1.4 Trap 3 — the one nobody named: the buy cameo throws without `RenderSprites`

`ProductionPaletteWidget.RefreshIcons` (`:677-701`):

```
var rsi = item.TraitInfo<RenderSpritesInfo>();      // :683  — UNGUARDED
var icon = new Animation(World, rsi.GetImage(item, faction));
var bi = item.TraitInfo<BuildableInfo>();           // :685  — also unguarded
```

`ActorInfo.TraitInfo<T>()` is `traits.Get<T>()` (`ActorInfo.cs:188`), and `TypeDictionary.Get<T>(true)` throws `InvalidOperationException: TypeDictionary does not contain instance of type ...` (`TypeDictionary.cs:65-70`). This loop runs for every item in `AllBuildables`, i.e. the moment the Support tab is displayed.

The three commented `powerproxy.*` at `misc.yaml:318-368` carry `AlwaysVisible:` plus a power block and nothing else, because in RA they were crate and infiltration grants (`SupportPowerCrateAction.cs:41-44`, `InfiltrateForSupportPower.cs:74-77`) — never sidebar items.

**A buy-menu proxy needs, minimally:**

- `RenderSprites:` — inert in-world (never enters `ScreenMap`, §1.1) but required by the palette widget;
- `Buildable:` with `Queue: Support` and an `Icon:` naming a frame on the proxy's sequence (`Buildable.cs:27`, `:40`; the palette resolves the image through `RenderSpritesInfo.GetImage` at `ProductionPaletteWidget.cs:684`);
- `Valued: Cost:`;
- `AlwaysVisible:`;
- the `SupportPower` subclass block with `OneShot: true`, `AllowMultiple: true`, `ChargeInterval: 0`.

That last field is worth stating precisely. With `ChargeInterval: 0`, `TotalTicks == 0` (`SupportPowerManager.cs:178`) and `remainingSubTicks == 0` on either branch of `:179`, so `RemainingTicks == 0` immediately; `Active` is set on the manager's next `Tick()` (`:200`), making the icon `Ready` **one tick (60 ms) after purchase**. The clock ring short-circuits to the full-charge frame (`SupportPowersWidget.cs:217`) and reads as permanently ready, which is correct.

## 1.5 The three mechanisms the loop rests on — re-read and confirmed

| Claim | Verdict |
|---|---|
| `SupportPowerManager.ActorAdded` auto-registers | **Confirmed**, `:53-75` (prior recon says `:52-73`). Owner-matched at `:55-56`, iterates `a.TraitsImplementing<SupportPower>()`, creates the instance via `t.CreateInstance(key, this)` at `:64`. |
| `AllowMultiple` keys per ActorID | **Confirmed exactly.** `MakeKey` at `:48-51`: `sp.Info.AllowMultiple ? sp.Info.OrderName + "_" + sp.Self.ActorID : sp.Info.OrderName`. N proxies ⇒ N dictionary entries ⇒ N icons. |
| `OneShot` removes the spent icon | **Confirmed.** `Activate` sets `oneShotFired = true` and calls `PrerequisitesAvailable(false)` (`:265-269`); either alone makes `Disabled` true (`:153-157`); `RefreshIcons` filters `!p.Disabled` (`SupportPowersWidget.cs:136`). |

One ordering nit worth knowing: `RefreshIcons` sorts by `p.Info.SupportPowerPaletteOrder` only (`:137`), and ties fall back to `Dictionary.Values` order, which is not contractually stable across removals. Instances of the *same* power share a palette order and are visually identical, so this is invisible **provided each distinct power gets a distinct `SupportPowerPaletteOrder`** — the default is 9999 for all of them (`SupportPower.cs:160-161`).

---

# 2. Q2 — the lifecycle questions

## 2.1 How many charges can be banked?

**Unbounded by default.** `SupportPowerManager.Powers` is a plain `Dictionary` (`:31`) with no capacity logic anywhere in the file.

Three YAML caps exist, and they are not interchangeable:

| Knob | Where | What it actually limits |
|---|---|---|
| `ProductionQueueInfo.QueueLimit` (default 0 = off) | `ProductionQueue.cs:51`; enforced `:407-412`, `:460-461` | Total items **in the queue**. Not the bank. |
| `ProductionQueueInfo.ItemLimit` (default 999) | `:48`; enforced `:415-420`, `:463-464` | Copies of one type **in the queue**. Not the bank. |
| `BuildableInfo.BuildLimit` (default 0 = off) | `Buildable.cs:33`; enforced `ProductionQueue.cs:422-428`, `:466-471` | `queued + owned`, where `owned = World.ActorsHavingTrait<Buildable>().Count(a => a.Info.Name == name && a.Owner == self.Owner)`. **The only one that reaches the bank.** |

**And `BuildLimit` is broken by the leak.** `BuildableInfo : TraitInfo<Buildable>` (`Buildable.cs:18`) creates an empty marker trait (`:68`) on every buildable actor. A **spent** proxy is still an undisposed, player-owned actor carrying `Buildable`, so it keeps counting at `:425` and `:469`. `BuildLimit: 3` therefore means *three cruise missiles for the whole match*; the fourth purchase attempt is silently dropped (`:473-474` returns with no notification and no sound).

> **This is the load-bearing consequence of §2.5.** Disposal is cosmetic while the bank is uncapped and becomes **mandatory** the moment a cap is wanted.

## 2.2 What happens past six icons

**Six is the hotkey count, not a layout limit.** `HotkeyCount: 6` at `ingame-player.yaml:29`; `SupportPowersWidget.cs:161` assigns `IconCount < HotkeyCount ? hotkeys[IconCount] : null`, and `HandleKeyPress` (`:192`) skips null-hotkey icons. Icons 7 and up are mouse-only. No wrap, no error, no lint issue (`LinterHotkeyNames` at `:71-91` only checks that a prefix exists when the count is non-zero).

**Geometry, read off the layout code:**

- `IconSize: 62, 46` (`ingame-player.yaml:22`). `IconMargin` is **not** set in YAML, so it takes the widget default **10** (`SupportPowersWidget.cs:34`). `Horizontal` is not set, so it is **false** (`:48`).
- `Container@SUPPORT_POWERS` sits at `X: 10, Y: 10` with **no Width or Height declared** (`ingame-player.yaml:16-19`); the palette declares no X/Y, so `RenderBounds` originates at (10, 10).
- Vertical branch at `:149`: `rect = new Rectangle(rb.X, rb.Y + IconCount * (IconSize.Y + IconMargin), 62, 46)` ⇒ **icon _i_ occupies `(10, 10 + 56i, 62, 46)`**. A single unwrapped column at 56 px pitch, extending indefinitely.

**At N = 8:** icons run y = 10 → 448, x = 10 → 72. Icon index 7 (the 8th) is the last. Indices 6 and 7 have no hotkey. Everything else behaves:

- **Nothing clips.** `ContainerWidget` (`Widget.cs:622-641`) has no `Draw` override and no scissor; `Widget.DrawOuter` (`:500-506`) has none either. `SupportPowersWidget.Draw` (`:204-250`) draws at absolute `p.Pos` with no bounds test.
- **Clicks keep working.** `eventBounds = icons.Keys.Union()` (`:168`) — the union grows with the column.
- **The chrome frame follows.** `SupportPowerBinLogic` clones `Image@ICON_TEMPLATE` once per icon at the same `IconSize.Y + IconMargin` pitch on every `OnIconCountChanged` (`SupportPowerBinLogic.cs:36-69`). There is no template pool to exhaust.
- **No horizontal collision with the buy menu.** The sidebar lives at `X: WINDOW_WIDTH - 250` (`ingame-player.yaml:1007`, `:1151`); the power timer at `X: 80` (`:41`). The icon column spans x 10–72.

**Where it does eventually break: the bottom command bar.** Those panels sit at `Y: WINDOW_HEIGHT - HEIGHT - 5` with `Height: 44` (`ingame-player.yaml:44-48` and its six siblings through `:80`), i.e. a top edge at `WINDOW_HEIGHT − 49`. Icon _i_'s bottom edge is `56 + 56i`, so the first overlap is at `i > (WINDOW_HEIGHT − 105) / 56`:

| Window height | First icon touching the command bar | First icon fully off-screen |
|---|---|---|
| 720 | i = 11 → the **12th** | i = 13 → the **14th** |
| 1080 | i = 18 → the **19th** | i = 20 → the **21st** |

**So "what breaks past six" is: nothing, for a long time.** Hotkeys stop at 6; the visual budget is roughly 11 icons at 720p and 18 at 1080p. If the design wants more, `Horizontal: true` (`SupportPowersWidget.cs:48`) switches the same code path to a row (`:147`) with no other change, and `SupportPowerBinLogic` already handles both orientations (`:48-51`, `:63-66`).

## 2.3 Does a banked power survive the producer's death?

**Yes — and for the shipped Supply Route the question is moot.**

- The proxy is an independent actor owned by the **player** (`OwnerInit(self.Owner)`, `ProductionQueue.cs:730`). It holds no reference to the producer, and `DoProduction` gives it none (§1.1 — no inits beyond owner and faction).
- `SupportPowerManager.ActorRemoved` (`:77-94`) keys on the **removed actor**: it early-returns unless `a.Owner == Self.Owner && a.Info.HasTraitInfo<SupportPowerInfo>()` (`:79-80`), then removes that actor's own `SupportPower` traits from their instance lists (`:82-85`). The producing building carries no `SupportPower`, so its death is a no-op here.
- **`SUPPLYROUTE` is not sellable.** It inherits only `^ExistsInWorld`, `^SpriteActor`, `^SelectableBuilding` (`structures.yaml:223-225`) — **not `^Building`** (`:68`), which is where `Sellable:` sits (`:135`). Nothing in the SR body (`:222-391`) adds one.

**What *would* happen if the last producer went away** (a different building hosting the queue, or the SR under `OwnerLostAction` at `:270`): `ClassicProductionQueue.Tick` recomputes `Enabled` from live `Production` traits every tick (`ClassicProductionQueue.cs:54-66`) and calls `ClearQueue()` when it goes false (`:68-69`), refunding everything **in the queue** (`ProductionQueue.cs:185-190`). **Already-banked proxies are untouched and remain spendable.**

For a queue hosted on a *building* rather than the player, `ProductionQueue` also implements `INotifyKilled` (`:215`) and `INotifySold` (`:216`), both `ClearQueue(); Enabled = false;`. Same conclusion, same scope: the queue only.

## 2.4 Refund, sell, cancel

**How the money leaves in the first place: not up front.** `ProductionItem.Tick` (`ProductionQueue.cs:793-834`) computes `expectedRemainingCost = TotalCost * RemainingTime / TotalTime` and takes the difference each tick via `pr.TakeCash(costThisFrame, true)` (`:823-826`). A player who cannot afford this tick's slice simply does not advance (`:825-826` early-returns). This is the mod's budget-allocation model applied unchanged: a power is billed exactly like a unit call-in, over its build time.

**Cancel (normal path): refunded, partially, correctly.** `CancelProductionInner` (`:604-641`) peels the most recently added copy and calls `playerResources.GiveCash(item.TotalCost - item.RemainingCost)` (`:638`) — precisely what was paid. The `Infinite` branch (`:614-632`) does the same per item at `:628`. The identical expression appears at `:189` (`ClearQueue`), `:391` (`CancelUnbuildableItems`, e.g. a prerequisite lost mid-build) and `:672` (`InfiniteBuildLimit` overflow). **There is no path that discards an item without refunding it.**

**Producer sold mid-build.** Not reachable for the shipped SR (§2.3). For a hypothetical sellable host: `INotifySold.Selling` → `ClearQueue()` if the queue is on that building (`:216`); if the queue is on the Player actor, `ClassicProductionQueue.Tick`'s `Enabled` recompute catches it a tick later and `ClearQueue()`s (`ClassicProductionQueue.cs:68-69`). **Either way, full partial refund.**

**Supply Route contested mid-build — no refund, and that is right.** `SupplyRouteContestation` implements `IProductionSpeedModifier` (`:153`) and returns **0** on a hard lockout (`:443-448`, and the comment at `:546-547`). It does **not** disable or pause the `Production` trait, so `Enabled` stays true and `ClearQueue` never runs. `ProductionQueue.TickInner` at `:340-357` simply does not call `Queue[0].Tick` while the modifier is 0. **A part-paid power freezes at its current progress and resumes when the bar recovers.** (`GetProductionSpeedModifier` at `:360-374` takes the min across the player's producers, skipping disabled and paused ones at `:365-366`.)

## 2.5 The spent-proxy residue — the leak is real, and worse than described

The prior recon says spent proxies leak an actor plus a dictionary entry, closable with ~10 lines. **The actor leak is real. The dictionary leak is not closable that way, and no shipped trait can do the disposal.**

**The actor leak — confirmed.** Nothing removes the proxy. `SupportPowerInstance.Activate` (`SupportPowerManager.cs:243-270`) sets flags and calls `power.Activate(power.Self, order, Manager)`; the base `SupportPower.Activate` (`SupportPower.cs:223-236`) fires the mini-map ping and notifies `INotifySupportPower`. No disposal anywhere on the path.

**The dictionary entry leaks even if you dispose the actor.** `ActorRemoved` (`:87`):

```
if (Powers[key].Instances.Count == 0 && !Powers[key].Disabled)
{
    Powers.Remove(key);
    TechTree.Remove(key);
    ...
}
```

A `oneShotFired` power has `Disabled == true` (`:153-157`), so `!Disabled` is false and the branch never runs. **The `Powers` entry — and its `TechTree` entry, if `Prerequisites` were set (`:66-70`) — survives disposal permanently.**

*Is the orphaned entry dangerous?* I checked every consumer of `SupportPowerInstance` and found none that dereferences the now-null `Info` (`:159`, `Instances.Select(i => i.Info).FirstOrDefault()`):

- `SupportPowerManager.Tick` → `power.Tick()`: `instancesEnabled` false, `Active` false, returns at `:201-202` before touching `Info`.
- `SupportPowersWidget.RefreshIcons` `:136-137`: the `Where(p => !p.Disabled)` runs before the `OrderBy(p => p.Info…)`, so the orphan never reaches the dereference.
- `SupportPowerTimerWidget` `:42`: guards `p.Instances.Count > 0` explicitly.
- `ResolveOrder` `:105-106` → `Activate` → `if (!Ready) return;` at `:245`.

So the cost is memory plus one dead dictionary slot per spent charge. **The reason to fix it is `BuildLimit` (§2.1), not tidiness.**

**Cheapest correct disposal: a new trait. Nothing that ships can do it.**

- `INotifySupportPower.Activated(self)` is the right hook — it is invoked on the proxy itself at `SupportPower.cs:234-235`. Its only implementors today are `WithSupportPowerActivationAnimation`, `WithSupportPowerActivationOverlay` and `Cloak`; **none grants a condition**, so nothing shipped can be chained off an activation.
- `KillsSelf` (`KillsSelf.cs:17-83`) is the only self-removal trait under `Traits/`, and it is unusable here on two counts: (a) nothing grants a condition on activation to drive its `RequiresCondition`; (b) its `ITick.Tick` reads `self.World.Map.Contains(self.Location)` at `:64`, and `Actor.Location` is `OccupiesSpace.TopLeft` (`Actor.cs:78`) — **NullReferenceException on a bodiless actor**.
  *(Hypothesis, not observed: the `Delay: 0` path routes through `TraitEnabled` → `AddedToWorld` (`:45-57`) without touching `Location`, and the frame-end `Kill` may land before any `Tick`. The check that would settle it is a run with `KillsSelf: RemoveInstead: true, Delay: 0` on a bodiless proxy, watching `debug.log` for an NRE in `KillsSelf.Tick`. I would not build on it either way.)*

**The shape I would write:** a ~15-line `RemoveSelfOnSupportPowerActivated : INotifySupportPower` whose `Activated(self)` queues `self.World.AddFrameEndTask(w => self.Dispose())`. Frame-end is required — `Activate` runs inside order resolution with the instance mid-iteration in `Powers` — and it matches both shipped proxy-creation sites, which are also frame-end tasks (`SupportPowerCrateAction.cs:41`, `InfiltrateForSupportPower.cs:74`). `Dispose()` is the right call rather than `Kill()`: the proxy has no `IHealth`, which is the same branch `KillsSelf.Kill` takes at `:78-79`.

## 2.6 Defeat and game end

Cheap to establish, and one line covers it: `SupportPowerInstance.Disabled` has `Manager.Self.Owner.WinState == WinState.Lost` as its **first** clause (`SupportPowerManager.cs:154`). On defeat every one of that player's powers goes `Disabled`, `RefreshIcons` filters them all (`SupportPowersWidget.cs:136`), and the top-left bin empties. `Activate` is unreachable (`:245`: `Ready` needs `Active` needs `!Disabled`). The proxy actors themselves are untouched — they carry no `OwnerLostAction` — but they are inert.

---

# 3. Q3 — lobby gating at two levels

## 3.1 What `PowersLobbyOptions` publishes, and what reads it

`PowersLobbyOptionsInfo : TraitInfo, ILobbyOptions` (`PowersLobbyOptions.cs:20`), commented at `world.yaml:568-570`. `ILobbyOptions.LobbyOptions` (`:58-89`) yields exactly two entries, both in group `"Powers"`:

1. **`airstrikes`** — `LobbyBooleanOption`, default `AirstrikeCheckboxEnabled = true` (`:29`), yielded `:60-68`.
2. **`airstrike-cooldown`** — `LobbyOption` dropdown, 2/3/4/5/8 min, default `"4min"` (`:47`; values `:70-77`; yielded `:79-88`).

**Both prior-recon claims verified:**

- **`AirstrikesEnabled` has zero readers.** `grep -rn "AirstrikesEnabled\|AirstrikeCooldown\b" --include=*.cs --include=*.yaml .` returns only the two declarations (`:98`, `:99`) and their own assignments (`:108`, `:110`). The working path is the generic `GrantConditionOnLobbyOption@airstrikes` (`player.yaml:115-118`), which reads the raw option id via `World.LobbyInfo.GlobalSettings.OptionOrDefault` (`GrantConditionOnLobbyOption.cs:47-48`) and grants `airstrikes-disabled` (`:52-53`), consumed by the two `AirstrikePower` blocks' `PauseOnCondition` (`player.yaml:120`, `:141`).
- **`airstrike-cooldown` is entirely dead.** `grep -rn "LobbyChargeIntervalId" --include=*.cs --include=*.yaml .` returns three hits: the field declaration at `SupportPower.cs:25` and two **commented** YAML usages at `player.yaml:126, :147`. Nothing parses it.

*(While here: the `[Desc]` at `SupportPower.cs:24` still asserts "Parsed at 25 ticks/second". The live rate is 16.667 — this is one of the ten remaining 25-tps sites CLAUDE.md flags. Not in scope to fix here, but it sits directly on the field this feature would revive.)*

## 3.2 Can a new checkbox be declared in YAML alone?

**Yes — via `LobbyPrerequisiteCheckbox`, with one caveat about where it lands.**

`LobbyPrerequisiteCheckboxInfo : TraitInfo, ILobbyOptions, ITechTreePrerequisiteInfo` (`LobbyPrerequisiteCheckbox.cs:19-57`), `[TraitLocation(SystemActors.Player)]` (`:17`). Every knob is a YAML field: `ID`, `Label`, `Description`, `Enabled`, `Locked`, `Visible`, `DisplayOrder`, `Prerequisites` (`:21-46`). It publishes one `LobbyBooleanOption` (`:52-53`) and, on `Created`, grants its `Prerequisites` to the player's tech tree when the box is ticked (`:69-76`). **Multiple `@`-labelled instances on the Player actor are ordinary MiniYaml.**

It is already used in-mod (`coop-missions-rules.yaml:7`, `:9`), and there are three commented instances in `player.yaml:273-279`, `:282-287`, `:289-295` — the last being `@NuclearAllowed` granting `global-nuclear`, exactly the shape a per-power toggle wants.

Player-actor options do reach the lobby: `LobbyOptionsLogic.RebuildOptions` reads `mapPreview.PlayerActorInfo.TraitInfos<ILobbyOptions>()` **and** the world actor's (`:310-312`).

**The caveat, and it is the only thing that is not free.** WW3MOD's lobby does **not** route on `LobbyOption.Category`. It uses two hardcoded C# tables:

- `GetCategory` (`LobbyOptionsLogic.cs:180-183`) → `Common` if the id is in `CommonOptionIds` (`:71-86`), else `Advanced`.
- `GetSection` (`:185-188`) → `OptionSection.TryGetValue(id, out var s) ? s : "Other"`, with `OptionSection` hardcoded at `:138-178`.

So a YAML-only checkbox appears in the **Advanced** tab under the implicit **"Other"** section at the bottom (`RenderAdvancedSections` `:391-400`). It works, it is visible, it toggles and it syncs — it just is not grouped with the other power options. **Putting it in the right section costs one dictionary line per option** in `OptionSection`. (`powers-enabled` already has one, mapped to `SectionGameRules` at `:177`.)

**Is there a count limit?** No. `RenderFlatOptions` (`:403-421`) clones a multi-column `checkboxRowTemplate` as needed and grows `optionsContainer.Bounds.Height`; the container feeds a scroll panel (`panel.ContentHeight = yMargin + optionsContainer.Bounds.Height`, `:339`). The Unit Availability section already carries **24** checkboxes (`LobbyDummyOptions.cs:107-205`), so the pattern is proven at that scale — well past any plausible power count. One behaviour to know: `RenderAdvancedSections` **hides a section outright when every option in it is a `Placeholder`** (`:378-383`), and does the same for the "Other" bucket (`:394`). A real (non-placeholder) option pulls its section back into view, which is the intended design (`:378-381`).

### The master toggle already exists in the lobby

`LobbyDummyOptions.cs:217-219`:

```
yield return new LobbyBooleanOption(
    "powers-enabled", "Powers Enabled", "Enable support powers (airstrikes, etc.)",
    true, 80, true, false, "Rules");
```

It renders dimmed because `ILobbyOptions.LobbyOptions` stamps `Placeholder = true` on **every** option `BuildOptions` yields (`:30-40`), and `LobbyOption.Placeholder` is what makes the lobby draw it as not-yet-wired (`TraitsInterfaces.cs:652-656`).

**Two ways to make it real:**

- **~6 lines of C#:** move the `powers-enabled` yield out of `BuildOptions` into `LobbyOptions` ahead of the stamping loop, plus one `GrantConditionOnLobbyOption@powers` block in `player.yaml`. Its `OptionSection` entry already exists (`:177`).
- **Zero C#:** delete those three lines from `LobbyDummyOptions` and declare a `LobbyPrerequisiteCheckbox@Powers` with `ID: powers-enabled` instead. That lands in "Other" unless the section line is also added, and it gates by prerequisite rather than by condition — which §3.4 argues is the better gate anyway.

## 3.3 What a granted condition actually does to a power, and what the player sees

`SupportPowerInfo : PausableConditionalTraitInfo` (`SupportPower.cs:18`), so **both** `RequiresCondition` and `PauseOnCondition` are available on every power. They produce visibly different results:

| Mechanism | Engine effect | **What the player sees** |
|---|---|---|
| `PauseOnCondition` (what `player.yaml:120, :141` use today) | `IsTraitPaused` ⇒ `Active = false` (`SupportPowerManager.cs:200`); `Disabled` unaffected | **Icon still present.** `RefreshIcons` only filters `Disabled` (`SupportPowersWidget.cs:136`), so the cameo is drawn, and `Draw`'s `else if (!p.Power.Active)` branch prints **"ON HOLD"** over it (`:241-244`; text from `ingame-player.yaml:26`). Clicking plays the `InsufficientPower` sound/notification and does nothing (`ClickIcon` `:176-183`). |
| `RequiresCondition` | `IsTraitDisabled` ⇒ `instancesEnabled = false` (`:196`) ⇒ `Disabled = true` (`:156`) | **No icon at all.** Filtered out of `RefreshIcons` entirely. |
| `SupportPowerInfo.Prerequisites` unmet (`SupportPower.cs:61`; tech-tree registration `:66-70`) | `PrerequisitesAvailable(false)` ⇒ `prereqsAvailable = false` ⇒ `Disabled = true` (`:155`) | **No icon at all.** |

## 3.4 The design comparison

Two places to put the gate. With the proxy/purchase model they are **not** equivalent.

**(A) Gate the POWER** — a `LobbyPrerequisiteCheckbox` grants `global-power-cruise`; the power block carries `Prerequisites: global-power-cruise` (or the inverse, `GrantConditionOnLobbyOption` + `RequiresCondition`).

- **Cost:** zero C# — one `LobbyPrerequisiteCheckbox` block plus one line on the power — plus one `OptionSection` line if you want it in the right lobby section.
- **Player-visible result: bad.** The buy cameo is **still in the Support tab**, because nothing on this path touches `Buildable`. The player can buy the power, pay for it over its full build time, and receive a proxy whose icon never appears. That is a money sink with no feedback.

**(B) Gate the BUILDABLE** — the same `LobbyPrerequisiteCheckbox` grants `global-power-cruise`; the proxy carries `Buildable: Prerequisites: ~global-power-cruise`.

- **Cost: identical** — zero C#, the same checkbox block, one line on the proxy instead of on the power.
- **Player-visible result: the buy entry is absent.** The `~` prefix makes the prerequisite *hidden*: `TechTree` tracks hiddenness separately (`IsHidden`, `TechTree.cs:151-169`) and fires `PrerequisitesItemHidden` (`:181-183`) → `ProductionQueue.cs:257-259` sets `ProductionState.Visible = false` → the item drops out of `allProducibles` (`:169`) and therefore out of `AllItems()` (`:282`) and the palette. Without the `~`, the cameo stays visible but greyed and unbuildable (`Buildable = false`, `:170`, `:292`).
- It also closes (A)'s hole for free: no purchase ⇒ no proxy ⇒ no icon.

**(B) is strictly better here, at the same price.** It is also the cleaner master toggle: one `~global-powers` on every proxy's `Buildable.Prerequisites` empties the whole tab, and `Buildable.Prerequisites` is a list, so master-AND-per-power is `Prerequisites: ~global-powers, ~global-power-cruise` with no extra machinery.

**Two things (B) does not do, one of which matters:**

1. **It does not remove already-banked charges.** `LobbyPrerequisiteCheckbox` evaluates once at `Created` (`:69-74`), so this is a match-start decision. For a lobby setting that is correct; it only matters if someone later wants a mid-match toggle.
2. **Hiding every item in a queue does not hide the queue's tab.** An empty Support tab button would remain. That is buy-menu territory (`ingame-player.yaml:1195-1310`: three live `ProductionTypeButton`s at Y 2/31/62 on a 31 px pitch inside a 240-tall container, with a commented `@NAVAL` occupying the Y:93 slot) and is the other worker's call.

### MiniYaml hazards specific to these edits

Per `conventions.md:238-254`, three ways an override silently does nothing, all live for this work:

- **Blank lines are significant** — the new `powerproxy.*` top-level entries in `misc.yaml` must each be separated by a blank line, or they merge into the preceding entry.
- **Top-level keys merge case-sensitively** — `Production@Local` must be edited in place at `structures.yaml:362`; a new top-level `supplyroute:` block would contribute a separate key and (with only field overrides) fail the mod's duplicate-key check at load.
- **`Inherits@` / `-Key:` apply positionally** — relevant if any proxy is built by inheriting a shared `^PowerProxy` template.

---

## 4. Files touched

**None.** This document is the only addition. No YAML, no C#, no map and no scenario was modified; `git status` shows `WORKSPACE/recon/powers-buy-loop.md` as the sole new file.

---

## 5. Runs I would like the manager to perform

I launched nothing. Three checks, in priority order. **R2 and R3 need a throwaway proxy actor and a `Support` queue that do not exist yet — they are not runnable against `2c8488ef` as it stands**, and are worth queueing only once someone writes the first proxy.

### R1 — the ruleset gate. Cheapest of the three; runnable the moment YAML lands.

```sh
Mod=ww3mod ./utility.sh --dump-balance-json
```

**What counts as the answer:** exit code 0. Per `conventions.md:264-276` this forces the full `ResolveInherits` pass and is the only gate that catches a rules-resolution failure — neither a compile error nor unit-testable. Seconds, no game slot, no window focus. **It proves nothing about the produce path**, only that the ruleset resolves.

### R2 — does the bodiless producee actually complete? (§1.1, executed.)

*Precondition:* a `powerproxy.test` carrying `RenderSprites:`, `AlwaysVisible:`, `Valued: Cost: 500`, `Buildable: Queue: Support`, and a `SupportPower` subclass with `OneShot: true, AllowMultiple: true, ChargeInterval: 0`; a `ClassicProductionQueue@Support` on the Player actor; `Support` appended to `Production@Local: Produces:` at `structures.yaml:363`.

```sh
./run-test.sh <new-scenario>     # queues one powerproxy.test, waits ~200 ticks
```

**What counts as the answer, in descending order of value:**

- **PASS:** one top-left support-power icon appears within ~2 s of the item reaching 100%, and `debug.log` carries no exception. That confirms §1.1 end to end.
- **The failure that would mean §1.2's fix was not applied:** the cameo sits at 100% indefinitely with **no log line at all**. The item is on `ProductionFromMapEdge`, not `Production@Local`.
- **The failure that would confirm §1.4:** `InvalidOperationException: TypeDictionary does not contain instance of type 'OpenRA.Mods.Common.Traits.RenderSpritesInfo'`, thrown on the tick the Support tab is first drawn. The proxy is missing `RenderSprites`.
- **Any `NullReferenceException` naming `Actor.Location` or `Actor.CenterPosition`** would mean I missed a positional consumer on the produce path. That is the conclusion I hold least firmly and the specific line worth grepping `debug.log` for.

### R3 — the bank-cap interaction (§2.1). Only if a cap is wanted.

*Precondition:* R2 green, plus `BuildLimit: 3` on `powerproxy.test`.

```sh
./run-test.sh <scenario: buy 3, fire all 3, attempt a 4th>
```

**What counts as the answer:** the 4th purchase is **refused** — no cameo response, no cash deducted. That confirms spent proxies still count at `ProductionQueue.cs:424-425` and that disposal is a prerequisite for using `BuildLimit`. If the 4th purchase **succeeds**, my reading of `ActorsHavingTrait<Buildable>` is wrong and §2.1's conclusion must be retracted.
