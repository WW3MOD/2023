# WW3MOD Economy & Supply System

This doc is the source of truth for how money, ammo, and supply move through a match. It's written from a gameplay perspective with technical detail where it matters.

If anything here disagrees with code, the doc is right and the code needs to change — file a fix, don't quietly drift.

> **Related:** [`supply-route.md`](supply-route.md) covers the Supply Route (the sector beachhead — fixed at spawn, not a factory). This doc is about the cash/ammo/supply pipeline; that doc is about the building those flow through.

## Where cash comes from

**Income is a per-tick allocation stream, not a starting balance** — `PassiveIncome` 100 every `PassiveIncomeInterval` 50 ticks after a `PassiveIncomeInitialDelay` of 50 (`PlayerResources.cs:63-69`), entirely independent of `DefaultCash` (`:32`, 20000; the mod leaves it at the engine default — `player.yaml:167` has it commented out). There is no harvesting and no resource patch: this stream and evacuation refunds are the whole of a player's income.

**One gate decides whether a player has an economy at all, in BOTH directions.** `PlayerResources.Tick` pays passive income, pays building income and charges upkeep on a single line (`:209`), behind `if (self.Owner.Playable || (self.Owner.IsBot && !self.Owner.NonCombatant))` (`:208`). A player failing it is not *poor* — it is disconnected from the economy entirely, earning nothing and paying no upkeep. **`Playable` means "occupies a lobby slot", not "is a real participant"**: `PlayerReference.Playable` defaults to `false` (`PlayerReference.cs:24`) and map players copy it verbatim, while a lobby-slot player keeps the `true` field initialiser. The `IsBot` disjunct exists because a **map-player bot is not `Playable`** — which is how every `tournament-*` scenario declares its bots. *(There is a PITFALL at the site. Consequence for anyone reading old numbers: before that disjunct was added, every bot-vs-bot tournament ran as a no-income match for **both** profiles — `earned=0` for the whole match while a do-nothing Observer accrued normally. **Any benchmark artefact predating the fix measured bots that could not buy anything**, and cannot be compared against a current run.)*

**Consequence for scenario authors, and it has burned tests: `DefaultCash: 0` does NOT freeze a force.** Several scenarios carry a comment claiming "no cash anywhere, so nothing is produced — the only actors are the pre-placed ones", and it is false: a bot observed at `cash=0` on tick 40 had 1437 by tick 750 and had bought two units in between. **Any scenario relying on "the placed force is the whole force" for attribution is relying on something untrue — count named actors instead**, since named actors keep their identity while the population around them grows.

## Core principles

1. **Every unit, every magazine, every supply box has a cost.** Cash spent buys ammo + body together. Selling or evacuating refunds what's left.
2. **A unit of supply is worth a fixed amount of cash** wherever it sits — in an LC, a truck, a cache. When the player gets it back on evac, capture, or absorb, it returns at face value.
3. **Supply is finite.** A Logistics Center spawns with a fixed pool. Trucks carry a fixed amount. Drained pools stay drained until the player brings in more supply. **An LC is obtained by deploying an `LCCV`** (`vehicles.yaml`, Cost **3000** as of 2026-08-22 — raised from 1200 by user ruling and held equal to `logisticscenter`'s own Cost — `Prerequisites: ~techlevel.low`, `Transforms: IntoActor: logisticscenter`), or by capturing one of the Neutral pre-placed LCs on the three maps that have any. **It is NOT obtained from the build sidebar** — `logisticscenter` carries `Buildable.Prerequisites: ~disabled` (`structures.yaml:367`), so its icon never appears. *(**Corrected 2026-08-17.** This principle previously read "LCs are **not** buildable … the only ones in a match are the Neutral pre-placed ones you can capture", inferring unobtainability from `~disabled`. That inference is wrong: `Transforms.CanDeploy` (`Transforms.cs:93-99`) tests only trait-disabled state and cell placement and **never consults `Buildable.Prerequisites`**, so the deploy route is wide open. The error funded a money pump — sell value 3500 for a 1200 LCCV — fixed in the same commit. Downstream reasoning that cited the old claim: `WORKSPACE/audit/260816-bug-reconciliation.md:259`, `WORKSPACE/DISCOVERIES.md` 2026-08-16. **General lesson: `~disabled` gates the sidebar icon, not the actor's existence** — enumerate every route to an actor, not just the build queue.)*
   **The mirror image: `~techlevel.*` is decorative, not a gate.** `MapOptions.TechLevel` defaults to `"unrestricted"` (`MapOptions.cs:52`), `world.yaml:435` sets `TechLevelDropdownVisible: false` so the lobby never offers a lower one, and no map or autotest scenario overrides it (checked repo-wide at `de78a1ed`). `ProvidesTechPrerequisite@unrestricted` (`player.yaml:222-225`) grants `techlevel.infonly`, `.low`, `.medium`, `.high` and `.unrestricted` in one block, and those five are the only tiers any **live** rule asks for. So an actor whose only prerequisite is `~techlevel.<tier>` is unconditionally available — the LCCV's `~techlevel.low` above restricts nothing. Two guards, because this is a dated observation and not an invariant: a map that set `TechLevel` would re-arm every one of them; and `~techlevel.futuristic` is *asked for* by three actors while **nothing provides it**, which would make it behave exactly like `~disabled` — harmless today only because all three of those blocks are commented out (`vehicles.yaml:722`, `vehicles-america.yaml:1155`, `vehicles-russia.yaml:1064`). Uncommenting one silently yields an actor with no route into play.
4. **Trucks, LCs, and dropped supply caches share one trait** (`SupplyProvider`). Players see the same UI (range circle, supply bar) everywhere.
5. **`ReloadCount` is the canonical batch size for rearm.** Whether a Bradley docks at the LC or a soldier waits next to a truck, the per-pool `ReloadCount` decides how many rounds arrive per cycle and `SupplyValue` decides what one cycle costs. (Aircraft have no reachable host at all — see "What rearms what".)

## What rearms what

| Unit class | Rearms at |
|---|---|
| Infantry | TRUK (supply truck), SUPPLYCACHE (dropped box), Logistics Center |
| Ground vehicles | Logistics Center only — **except `himars` and `iskander`, which rearm nowhere.** Those two carry no `Rearmable` trait at all, so there is no host to pull from, and the LC push cannot reach them either (they no longer declare `replenish-vehicles`). Once spent they are finished as combatants; evacuating for a refund is the whole plan for them — see "Evacuate-when-dry is opt-in per actor" below. |
| Aircraft | Helicopters: HPAD only. Planes: AFLD only. **Both are map-placed-only structures and are never buildable — that is the design, not a gap (user ruling 2026-08-19).** Where no host is present a helicopter does not rearm at all; it **evacuates** off the map for a refund. See "Aircraft ammunition…" below. |
| Static defenses (CRAM, AGUN) | Self-reload via `ReloadAmmoPool` (no external supply consumed) |

> **INVERTED 2026-08-30 by user ruling, and the previous state is quoted because reasoning downstream of it is still in the tree.** The ground-vehicle row used to read *"except `m270`, `grad` and `tos`, which rearm nowhere … the LC push … is gated on the recipient declaring `replenish-vehicles`, which only `himars` and `iskander` do"*. That split was an accident of the YAML that nobody decided. The ruling — *"Rocket artillery should be possible to rearm at the LC"* and *"Iskander and HIMARS should not be rearmable, they must be evacuated"* — swapped the two classes wholesale: the three tactical MLRS gained `Rearmable: RearmActors: logisticscenter`, and both strategic launchers lost both their `Rearmable` and their `ExternalCondition@VehicleReplenish`.
>
> **Consequence with no client: the Logistics Centre's `RearmCondition: replenish-vehicles` push arm can no longer select anything in the shipped game.** It is retained deliberately as a hook (see the note at `structures.yaml`), and `test-who-pays-for-a-rearm` re-declares both traits on a himars in its scenario-local `rules.yaml` so the docked double-serve regression stays measurable. **Every vehicle that rearms in the shipped game now does so through the `Rearmable`/`Resupply` PULL, and none through the push.**

Vehicles are budgeted around dock-at-LC logistics. Adding `truk` to `Rearmable.RearmActors` on a vehicle is a balance change.

### Host discovery is ONE list, and it is declared per-template with no shared default

`AmmoPool.ChooseResupplier` (`AmmoPool.cs:774-807`) is the only host-discovery path in the engine — `AutoSeekSupplies` routes solely through it — and both its branches filter on `rearmInfo.RearmActors.Contains(a.Info.Name)`. **An actor absent from that list is not deprioritised, it is invisible**, however close and however full. That is the entire mechanism by which SUPPLYCACHE was push-only; it took no code to enforce and none to undo.

The trap is that `RearmActors` has **no `^Soldier`-level declaration** — it appears once per infantry template carrying a `Rearmable`, 14 times in `infantry.yaml` (`^E1`:1125, `^E3`:1244, `^AR`:1325, `^E2`:1388, `^TL`:1487, `^MT`:1559, `^SN`:1634, `^AT`:1706, `^AA`:1776, `^E6`:1916, `^E4`:2009, `^SF`:2117, `^DR`:2367, `^PILOT`:2448), so there is no single place to edit and no inheritance to lean on. A 15th template added later silently does not seek, and the failure is per-unit-type — it presents as *"the rifleman works and the sniper doesn't"*, which reads like a pathing or stance bug. `SupplyCacheSeekTest.cs:58` enumerates the templates **from the file** rather than from a hardcoded roster, precisely so a new one is caught.

**The counterpart fact:** the same query filters `a.Owner == self.Owner` — strict equality, not a relationship test. So "seek ammo" can never become "take enemy loot" even though SUPPLYCACHE is `ProximityCapturable`; capture stays a separate on-contact mechanism. **Allied units do not share hosts either.**

### Two host-discovery paths disagree about the same actor, and which one a unit gets depends only on which trigger fired

`AutoSeekSupplies.CanServe` is the **strict, proximity-push-shaped** one; `AmmoPool.ChooseResupplier` is the **permissive, destination-shaped** one. `CanServe` rejects the Logistics Centre twice — once for carrying a `DockedCondition` (`AutoSeekSupplies.cs:486`) and again for the `replenish-vehicles` gate infantry cannot satisfy (`:492`) — while `ChooseResupplier` applies neither gate and returns the LC happily.

**And infantry rearm at an LC by PROXIMITY, not by the supply push.** `LOGISTICSCENTER` carries `ProximityExternalCondition@ReplenishSoldiers` granting `replenish-soldiers` to allies within `4c0` (`structures.yaml:407-411`), and **18** infantry `ReloadAmmoPool` declarations are gated on exactly that condition. A soldier who simply *stands* next to an LC refills. The LC's own `SupplyProvider` (`:412-420`, `Range: 2c0`, `RearmCondition: replenish-vehicles`) is a separate mechanism aimed at vehicles. So the comment on `CanServe`'s first rejection — *"Walking into its aura would achieve nothing"* (`:483-485`) — is true of the **provider trait it was written about** and misleading about the **actor**, because walking into the LC's aura is precisely what rearms a soldier. Same shape as the `RestockThreshold` case below: a sentence true of the field it names and false of the thing it is read as describing.

### A pool consumed by a TRAIT rather than an Armament is idle-triggered only

`AmmoPool.AutoRearmIfDry` has **two** entry points, not one: `INotifyBecomingIdle` (`AmmoPool.cs:667-669`) and `INotifyAttack.Attacking` on the tick a pool's last round is spent (`:656-663`). Neither consults `AutoSeekSuppliesInfo`, so **`AutoSeekSupplies.ReturnWhenEmpty` gates only the `AutoSeekSupplies` seek** and has no authority over this path. But the attack-path dispatch is keyed on `Info.Armaments.Contains(a.Info.Name)` (`:658`) — it fires only for a *named Armament* that drew from the pool. A pool spent by a trait (`Demolish.cs:79`, `LayMines.cs:210`, both calling `TakeAmmo` directly, whose own dispatch call is commented out at `:263-266`) is invisible to the non-idle trigger. `^E6`'s C4 is the live case: no Armament named `secondary` exists on him at all.

**When bounding these paths, bound the METHOD, not the notification** — that is what covers both entry points. `AmmoPoolInfo.DryRearmLeashCells` (`:113`, default 30 chessboard cells, math in `SupplyHuntMath.WithinCellBudget`) deliberately sits on `AmmoPoolInfo` rather than beside its equal-valued sibling `AutoSeekSuppliesInfo.ReturnWhenEmptyLeashCells` (`:88`), because **`AutoSeekSupplies` is declared on `^Soldier` and `^E6` only, while the dry-rearm path runs on every non-aircraft actor with an `AmmoPool`, vehicles included.** Reading the other trait's Info would have left every vehicle unleashed while looking complete at the only site anyone reads. Shared value (pinned equal by `DryRearmLeashTest`) and shared math; separate fields, on purpose.

### A dry VEHICLE never re-evaluates resupply, and the autotargeting is what accidentally rescues it

*(Settled by reading, 2026-08-30, after being raised and left open at least twice. This corrects an earlier and more generous phrasing — "it asks once, at the wrong time" — which is the **best** case, not the general one.)*

`AmmoPool` declares `INotifyCreated, INotifyAttack, INotifyBecomingIdle, IResolveOrder, ISync` (`AmmoPool.cs:260`). **There is no `ITick` and no `INotifyIdle`**, so `AutoRearmIfDry` has exactly two triggers: `INotifyAttack.Attacking` (dispatch at `:864`) — the shot that empties the pool — and `INotifyBecomingIdle` (`:868-870`). `Actor.Tick` computes `var wasIdle = IsIdle;` *before* running the activity and then branches `if (!wasIdle && IsIdle) { …OnBecomingIdle… } else if (wasIdle) { …TickIdle… }` (`Actor.cs:318-333`) — an if/else on the **transition**, not a per-tick call.

Two consequences, and the second is worse than the first:

- **A unit that runs dry while busy asks once, then never again.** It stays idle from that tick on, so `!wasIdle` is false every subsequent tick and the transition cannot recur. There is no retry loop; there is no loop.
- **A unit that is ALREADY IDLE when it runs dry asks ZERO times.** It is idle on the tick the check runs, fails the edge test, and fails it identically forever. `INotifyAttack.Attacking` cannot cover for it because a unit with no ammunition does not shoot. This is the ordinary state of a vehicle holding position that fires its last round, and of any unit spawned or delivered dry with no order — **a truck can park on top of it and it will not notice.**

**Why infantry are fine and vehicles are not.** `AutoSeekSupplies` carries the only `ITick` in this system (`AutoSeekSupplies.cs:228`) and appears at exactly two sites mod-wide, both in `infantry.yaml` — `:251` on `^Soldier` (`Enabled: true`, `ReturnWhenEmpty: true`) and `:2021` on `^E6`, which opts `ReturnWhenEmpty` back off. **No vehicle has it**, which is the same asymmetry `DryRearmLeashCells` exists for (above). The second, *accidental*, rescue is `AutoTarget` itself: it issues an attack, `Attack.cs:117` ends it immediately on `AmmoPool.CannotFight`, the unit goes non-idle then idle, and the transition fires. **For a vehicle, the autotargeting a player might want to switch off is the only thing making it re-check whether it can rearm** — which is the concrete argument against any wholesale disable of automatic behaviour.

**Sharpest case:** `strykershorad` and `tunguska` mix `Essential` and non-`Essential` pools and carry no `AutoSeekSupplies`. `Attack.cs:117` reads `CannotFight` = *all* pools empty, which is false while the non-essential pool holds rounds, so the unit never goes idle either — their only trigger is the `Attacking` notification on the exact tick the essential pool empties. One dispatch opportunity per match. A tunguska out of SAMs with a full cannon is the motivating example in `AmmoPool.cs`'s own `Essential` documentation, and it is the case that does not work.

**Fix shape, if someone builds it:** a small `ITick` that re-asks `AutoRearmIfDry` on a cadence closes it, and it **must be idle-gated** — the gap is specifically the unit that decided to hold, which is idle by definition, so an ungated version would instead interrupt live player orders, a much larger change wearing the same clothes. It must also guard on `AmmoPool.IsSeekingRearm`, because `AutoRearmIfDry` has no such guard of its own and is safe today only because it cannot fire while a unit is walking; a periodic caller would tear down and re-plan the same errand forever.

**Scenario-authoring corollary, because it has already cost two runs:** a dry unit placed on a map with no order will sit at `ammo=0` in *both* the RED and GREEN arms and fail identically, with the fix demonstrably present in the built source — nothing dispatched on either side because nothing ever asked. Give the unit one cell of movement to finish, so that completing it is a real `!wasIdle → IsIdle` transition, and prime it *away* from the destination under test so the priming move cannot be confused with the errand.

### No vehicle can enter `SeekSupplyProvider` — a RULESET property, one YAML line from being false

`AmmoPool.AutoRearm` is the only construction site for `Activities.SeekSupplyProvider` (`AmmoPool.cs:923`), and it takes that branch only when the chosen host's `SupplyProvider` has an **empty `DockedCondition`** (`:915-921`); otherwise the unit is queued a `Resupply` instead. Every ground vehicle names `logisticscenter` and nothing else in `RearmActors`, and `LOGISTICSCENTER` is the **only** provider in the mod that sets `DockedCondition` (`structures.yaml:496` — the mod-wide grep returns that one assignment). So every vehicle resolves to the docking path and **the live clientele of `SeekSupplyProvider` is infantry.** The `Evacuate` detour funnels through the same `AutoRearm`, so it adds no vehicles either.

It matters in both directions. It **shrinks** what a change to that activity can affect — no vehicle behaviour, therefore no vehicle-side regression surface, however the retarget or validity tests are tuned. And **it is one line from being false**: adding `supplycache` or `truk` to any vehicle's `RearmActors` puts vehicles into that activity for the first time, against logic that has only ever run for infantry — including the in-range park branch, which has no stall guard of its own for anything `AutoSeekSupplies` did not dispatch, and that trait is infantry-only. **If you are editing `RearmActors` on a vehicle, read this first.**

### A rearm host refills only the pools named in `Rearmable.AmmoPools` — with one exception that is easy to miss

Three refill sites iterate the filtered `Rearmable.RearmableAmmoPools` (built at `Rearmable.cs:44`): `RearmTick` (`:60`, docked hosts and the LC), the `SupplyProvider` passive aura (`SupplyProvider.cs:853`), and `QuickRearm` (`:46`). **But the enumeration is not complete, and a 2026-08-21 claim that it was is wrong:** `EnterCarrierMaster.cs:49-53` refills **every** pool on the actor via `self.TraitsImplementing<AmmoPool>()`, bypassing `Rearmable` entirely, and `CarrierMaster` is in use in this mod (`infantry.yaml:2323`). The Lua scripting API (`AmmoPoolProperties.cs:62`) is a fourth writer. So `Essential ⊆ Rearmable.AmmoPools` is necessary but **not** sufficient on a carrier-capable actor.

### Pool NAME never implies pool ROLE

`primary-ammo` is used 40 times and `secondary-ammo` 15 — just enough regularity to make a name-based rule look safe. It is not, and the counterexamples are headline units:

- **`tunguska`** — `primary-ammo` (`vehicles-russia.yaml:872`) is the self-defence 30mm, while the **9M311 SAMs that define the vehicle** sit in `secondary-ammo` (`:902`). This kills any "primary is the main weapon" default outright.
- **`F16` and `MIG`** — naming is *inverted* relative to every other airframe: `primary-ammo` is the 6-missile AAM rack (SupplyValue 100), `secondary-ammo` the 150-round 20mm cannon (SupplyValue 1) (`aircraft-america.yaml:605-639`, `aircraft-russia.yaml:625-660`).
- **`MNLY`** — `mines-ammo` (`vehicles.yaml:485`) feeds the `Minelayer` **trait**, not an `Armament` at all, so resolving it as a weapon finds nothing.

**The reliable resolution is three hops, every time:** pool `Armaments:` → the matching `Armament@N.Weapon:` → that weapon's `ValidTargets`/`InvalidTargets`. `SupplyValue` corroborates but measures expense, not role.

**`Rearmable.AmmoPools` and "every `AmmoPool` on the actor" are different sets, and among actors that HAVE a `Rearmable`, `^E6` is the only divergence in the corpus** — it declares `AmmoPools: secondary-ammo` (`infantry.yaml:1917`) while carrying a 100-round `primary-ammo` (`:1836`). Resolved across all 879 rule entries; 110 carry a pool, 90 of those have a `Rearmable`, one diverges. (Scope caveat: 20 actors hold pools with **no** `Rearmable` at all — the crew templates, `F16`, `AGUN`, `CRAM`, `FTUR`, `himars`, `iskander` and the two airstrike variants (**swapped 2026-08-30**: this list read `grad`, `m270`, `tos` before the artillery-doctrine ruling). Read "only divergence" as scoped to actors that have one.) Any per-pool trigger that sends a unit to a host for a pool the host will not refill produces a seek→refill→still-empty→seek loop — and `^E6` being the single case is exactly what makes it easy to test against 13 actors and conclude the sets are identical.

### A trait default is a DECISION the actor never made, and an omitted field can cancel a stated one

*(Fixed; recorded because the shape recurs.)* SUPPLYCACHE was reported as not rearming units despite showing plenty of supply. It was not missing `SupplyProvider` — it carried one, with a comment stating outright that a crate "serves down to the last usable batch" because it has no drive home to reserve supply for. **That sentence was true about the field it named (`RemoveBelowSupply: 1`) and false about the actor**, because a *second* field governs the same outcome and was never written down: `SupplyProviderInfo.RestockThreshold` defaults to **50** (`SupplyProvider.cs:38`) and the tick ladder stops serving below it. So the crate withheld its last 49 supply while sitting *above* the removal floor of 1 — which serving was the only thing that could have carried it down to. It parked in the world permanently, supply bar visible, sprite intact, serving nobody.

**The shape worth banking: two fields both governing "when does this provider stop", one stated and one inherited, with the removal floor set BELOW the serving floor. That gap is an absorbing state.** Setting the stated field was a real fix to a real bug and it *moved* the failure rather than removing it. **Whenever you lower a despawn/cleanup threshold, ask what OTHER threshold decides whether the actor can still do the work that would carry it down to the new one** — if the answer is a default you did not write, the actor now has a band it can never leave. Reachability was not exotic: `DropsSupplyCache` seeds a crate with the truck's exact remaining load (`DropsSupplyCache.cs:155`, `:197`), so a crate could be **born** inside the dead band without ever being drained into it. Closed by setting `RestockThreshold: 0` explicitly on SUPPLYCACHE (`misc.yaml:486`, with the reasoning in-file at `:477-485`).

**Corollary for tests:** the serving clause was written out twice — once in `TickServing`, once in `CanServeNow`, which exists precisely to mirror that ladder — so the two could have disagreed and nothing would have failed. It is now extracted as the pure `SupplyProvider.ReservesRemainderForRestock` (`:1184`) and read by both (`:383`, `:1145`). **A YAML-corpus test that restated the crate's config in a fixture would have been green throughout; resolving unset fields through `new SupplyProviderInfo()` is what made the omission visible, because the omission WAS the bug.**

**Aircraft ammunition and health are one-way on every shipped map — and as of 2026-08-19 that is INTENDED, not an unmet spec.** The user closed this design decision directly:

> *"Airplanes uses the airfield, helicopters use helipad, if those do not exist they must evacuate (They cannot be rearmed in that case). Airplanes are not in the game now, and probably wont be either so no need to look into that, but helipads should be possible to use to rearm helicopters, if a helipad exists (Cannot be built in this mod, can only be used if one exist on a map as a neutral/capturable structure)"*

Three things follow, and each contradicts a fix someone will otherwise propose. (1) **Do not repoint aircraft at `logisticscenter`.** A previous change did exactly that and was reverted (`68e8b885`). (2) **The `~disabled` build prerequisite on `hpad`/`afld` is correct and stays** — these are map-placed structures, not buildable ones. (3) **Absence of a host is not a bug to be fixed; it has a defined behaviour** — the helicopter evacuates (`EvacuateWhenUnrearmable` → `RotateToEdge`, refunding `GetEvacuationRefund` exactly as the manual `Evacuate` order does). `hpad` already carries everything the air rearm route needs — `Reservable`, `RepairsUnits`, an `Exit`, and `CaptureManager`/`Capturable` inherited via `^BasicBuilding` → `^NeutralOrOccupiedCapturable` — so a map that places one Neutral gives helicopters a capturable rearm point with no rules change. Note `ReturnToBase.ChooseResupplier` filters on `a.Owner == self.Owner` (**not** alliance), so a pad must be *captured* before it serves you; and `hpad` currently has **no world sprite** (`hpad.shp`/`hpadmake.shp` are in `lint-baseline.txt` as missing), which is the outstanding blocker to actually placing one. Every airframe declares `Rearmable.RearmActors: hpad`/`afld` and `Repairable.RepairActors: hpad`/`afld` (`aircraft.yaml:79-80`, `:162-163`; `aircraft-america.yaml:219,376,498`; `aircraft-russia.yaml:224,392,530,625`), but **both hosts carry `Buildable.Prerequisites: ~disabled`** (`structures.yaml:432`, `:500`) and **nothing in the repo provides `disabled`**, so neither can be built; and neither is pre-placed on any of the ten shipped maps. `logisticscenter` — which does have `RepairsUnits` (`structures.yaml:377`) and `SupplyProvider` (`:387`), is pre-placed as a Neutral capturable on `polar-disorder`, `river-zeta` and `woodland-warfare`, and is already named by every infantry and ground-vehicle `RearmActors`/`RepairActors` list — is **not** named by any aircraft. So no aircraft can rearm or repair anywhere today. There is one latent exception that no code drives: `ReloadAmmoPool@1/@2` on the airframes is gated `unit.docked && !airborne` (e.g. `aircraft-america.yaml:175`, `:205`), and a *captured* LC grants `unit.docked` within 2c0 (`structures.yaml:400-404`), so an aircraft landed beside a captured LC would trickle-refill. Nothing — human UI or bot — ever sends one there.

Consequences worth knowing before touching aircraft logic: `ReturnToBase` resolves no resupplier and degrades to `FlyIdle`-then-finish (`ReturnToBase.cs:127-128`), and any gate of the form "wait until healthy / wait until full" is **unsatisfiable, not merely pessimistic**. The bot readiness gates therefore ask whether a host actually exists rather than whether one is named (`AirframeReadiness`), and `Aircraft` refuses a `ReturnToBase` order when none does, so a no-op return cannot cancel a live attack.

That `FlyIdle` hold is what `EvacuateWhenUnrearmable` now terminates for a **player-owned** helicopter, and the reason it is a unit trait rather than a branch inside `ReturnToBase` is that aircraft are carved out of the entire `ResupplyBehavior` axis one layer up: `AmmoPool.AutoRearmIfAllEmpty` returns immediately for anything with an `AircraftInfo` (`AmmoPool.cs:233`), so the `Evacuate` stance that serves `m270`/`grad`/`tos` never sees an airframe. **Bot** helicopters are deliberately excluded from the trait — `HelicopterSquadBotModule.EvacuateWhenIdle` (@experimental) already owns that disposition and keeps its own `evacuating` ledger so a heli mid-exit is never re-tasked; a second evacuator would issue `RotateToEdge` behind the module's back. Two properties of the trait are load-bearing and easy to break. It refuses any airframe that has **no `AmmoPool`** or **no `Rearmable`** — both shipped transports (`TRAN` Chinook, `HALO` Mi-8) inherit `^Helicopter` and have neither, being `Cargo` airframes with no armament, so today the pool count alone already refuses them; the `Rearmable` term is kept because it is what the *ruling* says, and an armed transport would be spent-able and permanently hostless at once (a null `RearmableInfo` makes `AnyResupplierExists` false forever) and so would read exactly like a dry Apache. And it tests `AIUtils.IsUnoccupiedAirframe` rather than `Actor.IsIdle`, which is **never true for a hovering helicopter** — though note `FlyIdle.Tick` does forward `INotifyIdle.TickIdle` to the actor's traits (`FlyIdle.cs:49-51`), which is what lets a unit trait see a helicopter that is holding on `FlyIdle` at all.

**A truck can never rearm a vehicle anywhere — the split is structural, not a tuning choice.** The provider PUSH is gated on the recipient declaring the provider's `RearmCondition` as an `ExternalCondition`; TRUK (`vehicles.yaml:546`) and SUPPLYCACHE (`misc.yaml:412`) both name `replenish-soldiers`, which is declared **only** on `^Soldier` (`infantry.yaml:214-215`). The only provider naming `replenish-vehicles` is the static `logisticscenter` (`structures.yaml:394`), additionally `DockedCondition: unit.docked` — so a vehicle can only ever be served standing at a fixed building. Two corollaries that are easy to get wrong: (1) **as of 2026-08-30 the LC *push* reaches NOTHING** — `himars` and `iskander` were the only two vehicles declaring the receiving condition and the strategic-launcher ruling removed it from both, so **every** vehicle that rearms now does so through the `Rearmable`/`Resupply` **pull**, not `SupplyProvider`. The arm is kept as a deliberate hook and is exercised only by `test-who-pays-for-a-rearm`, which re-declares the condition in its scenario-local `rules.yaml`; (2) a cache is served to infantry only, and **as of 2026-08-21 it can also be walked to**. It was push-only until then — no `RearmActors` list named `supplycache`, so `AmmoPool.ChooseResupplier` could never return one and nothing ever sent a unit to a crate. **That was a deliberate design property, not a defect, and it was overruled by user ruling** ("they want infantry to seek crates"): all 14 infantry templates carrying a `Rearmable` now name `supplycache` alongside `truk` and `logisticscenter`, so a soldier below his ammo threshold paths to a dropped crate exactly as he already did to a Logistics Center. **Own crates only** — `ChooseResupplier` filters on `a.Owner == self.Owner`, so seeking ammo never doubles as taking enemy loot; capturing an enemy crate remains the separate, unchanged `ProximityCapturable`-on-contact mechanism. Vehicles were not given the crate and remain LC-only. Pinned by `SupplyCacheSeekTest`. Any "supply truck follows the army" reasoning that counts vehicle ammo as demand is counting demand the truck cannot relieve (`SupplyFollowerBotModule`'s cluster `AmmoNeed` sums every `AmmoPool` in the cluster, vehicles included — `:523-531` — so a pure-armour cluster still attracts a truck that can do nothing for it).

**The aura `Range` once gated SELECTION only, never DELIVERY.** All three `Info.Range` comparisons were dimensionally correct; the defect was a *missing* comparison — `ResupplyTarget()` called `GiveAmmo` with no range check, so a target that walked out of the aura during the `RearmDelay` wait (or one picked by the unbounded Hunt scan below) was served anyway, in one case clean across the map. Fixed by extracting `SupplyProvider.InAuraRange(WPos, WPos, WDist)` (`:927-930`) — a **squared** horizontal comparison matching `WorldUtils.FindActorsInCircle`'s own `HorizontalLengthSquared <= r.LengthSquared` (`WorldUtils.cs:84`) rather than the floor()'d `WVec.HorizontalLength` (`Exts.ISqrt` defaults to `ISqrtRoundMode.Floor`, `Exts.cs:306`), so selection and delivery agree exactly on the boundary — applied at `IsValidTarget` (`:470`), `SetTarget` (`:516`), `SyncTargetCondition` (`:552`) and the new delivery gate (`:664-675`). The gate **keeps** the target and re-arms `rearmTicks` rather than dropping it, so an approaching provider still serves on arrival instead of thrashing its target pick. Sheltered garrison passengers stay exempt (they are `!IsInWorld` with a stale `CenterPosition`, and their building was in range when picked). The general lesson: a proximity aura's range must be re-checked at the moment of effect, because the wait between selection and delivery is where the geometry changes.

**There is an UNBOUNDED whole-map provider hunt in the engine, one stance away from live.** `SupplyProvider.UpdateTarget` falls through to `FindNeedsResupplyTarget` when `AutoTarget.EngagementStanceValue >= EngagementStance.Hunt` (`:302-304`), and that helper scans `world.ActorsHavingTrait<AmmoPool>()` with **no range term and no leash** (`:356-364`); `SetTarget` then drives the provider to it (`:513-525`). It is dormant for TRUK by default — TRUK's `AutoTarget` block overrides only `InitialResupplyBehavior*` (`vehicles.yaml:514-516`), so it ships `Defensive`, the engine default for both the human and AI fields (`AutoTarget.cs:160/163`), and TRUK inherits `^AutoTarget` rather than the `^Combatant` chain that some shipped maps flip to Hunt. But Hunt is both player- and AI-settable, and `UnitDefaultsManager` persists a human's per-type stance across games — so "supply trucks don't wander" is a default, not an invariant. Know this before anyone "enables truck hunting" by flipping a stance.

**Provider rearm is a PUSH gated on the RECIPIENT's condition, not on `RearmActors`.** `SupplyProvider.Tick` scans `FindActorsInCircle`, picks the greatest-need friendly `Rearmable` in range, and calls `GiveAmmo` on it directly (`SupplyProvider.cs:225/:308/:546`) — it **never consults the recipient's `Rearmable.RearmActors`**. What gates the push is `IsValidTarget` (`:403-440`): the recipient must be a friendly `Rearmable` with a non-full pool AND (when the provider sets one) carry the provider's `RearmCondition` external condition — default `replenish-soldiers` (`:59`), which only infantry hold. That is why a truck/cache tops up nearby infantry but skips vehicles, with no driving involved. `RearmActors` gates the *other* path — the recipient-initiated drive-to-a-host PULL (`AmmoPool.ChooseResupplier`, `:340`) — so adding `truk` to a vehicle's `RearmActors` lets that vehicle dock at a truck but does not make the truck's push serve it.

**An `AmmoPool` never refills itself on the battlefield — passive trickle is opt-in via `ReloadAmmoPool`.** `AmmoPool` is not `ITick` (`AmmoPool.cs:111` implements only `INotifyCreated, INotifyAttack, INotifyBecomingIdle, IResolveOrder, ISync`), and its self-reload method `AmmoPool.Reload()` (`AmmoPool.cs:361`) has **zero callers** engine-wide — so the `RemainingTicks`/`FullReloadTicks`/`FullReloadSteps` countdown it decrements (`:366`) never advances. The `ReloadDelay` field only *seeds* that inert countdown (`:237`) and is re-seeded on a dock rearm (`Rearmable.cs:52,66`). Actual in-field trickle exists **only** on the separate `ReloadAmmoPool` trait (`ReloadAmmoPool.cs:46`, which *is* `ITick` and calls `ammoPool.GiveAmmo(self, Count)` every `Delay` ticks, `:91`). A unit that does not carry `ReloadAmmoPool` therefore cannot top up in place — it must retreat and rearm at a provider (LC / TRUK / cache via `Rearmable` + `Resupply`). `ReloadAmmoPool` appears in only 7 mod YAML files (mostly static defenses); most units, including tunguska AA, lack it. **Do not read `AmmoPool.ReloadDelay` as "seconds to self-reload in the field" — it drives nothing without `ReloadAmmoPool`.**

**"A resupplier exists" is the engine's whole reachability test.** `AmmoPool.ChooseResupplier` ends in `ClosestToIgnoringPath` (`AmmoPool.cs:343-344`) and filters only on ownership, `RearmActors` membership and `CurrentSupply > 0` (`:331-341`) — no path check, and no `IsInWorld` check either. So a depot across an unfordable river reads as reachable, and any consumer that wants real reachability must supply its own proxy (and should document it as a proxy). `AutoRearm`'s own null branch still just sets `NeedsResupply = true` on every pool and returns — and the flag has exactly **one** reader engine-wide, the Hunt-stance provider scan (`SupplyProvider.FindNeedsResupplyTarget`, `SupplyProvider.cs:622`), which does not dispose of the unit. **Corrected 2026-08-27:** this sentence previously claimed *two* readers, naming `UnitBuilderBotModule.AnyFieldedUnitNeedsResupply` as a production gate. That method merely *contains the name* — its body never touches the property, computing need from `Info.Ammo` / `CurrentAmmoCount` / `Info.SupplyValue` via `ResupplyDemand.UnitNeed` (`UnitBuilderBotModule.cs:924-942`). Grep the property access, not the string. What such a unit no longer does is keep fighting: the attack activities test `AmmoPool.CannotFight` and end, so it drops the attack order and falls idle rather than standing in range aiming a weapon it cannot fire.

**But "it retries resupply once per idle cycle" is only true of a unit that keeps RE-ENTERING idle.** This paragraph used to end by saying that going idle re-enters the `INotifyBecomingIdle` dispatch "so it retries resupply once per cycle instead of asking exactly once and never again". `Actor.Tick` fires `OnBecomingIdle` only on the `!wasIdle && IsIdle` **transition** (`Actor.cs:317-323`); the per-tick `tickIdles` loop on the next line requires `INotifyIdle`, which `AmmoPool` does not implement, and `AmmoPool` is not `ITick` either (`AmmoPool.cs:212` declares `INotifyCreated, INotifyAttack, INotifyBecomingIdle, IResolveOrder, ISync`). Its other re-entry, `INotifyAttack.Attacking`, cannot fire on an actor with no ammunition. **So a dry unit that falls idle and simply stands there asks exactly once and never again** — the "once per cycle" reading holds only while something keeps ending and restarting its activity.

> **AMENDED 2026-08-27 by user ruling.** This paragraph used to conclude: *"nothing at the unit level removes a dry unit with no reachable source — that judgement belongs at the sector level, not in a unit trait."* **That is no longer true**, and the sentence is quoted here rather than deleted so the change is legible. The user ruled that `Auto` must mean "evacuate if no rearm actor exists", so `AmmoPool.AutoRearmIfDry`'s `Auto` arm now disposes of the unit itself, via the pure `SupplyHuntMath.DecideAutoDisposition` (pinned by `ResupplyAutoFallbackTest`). Note the in-code comment the old text quoted — *"Evacuation only happens when `ResupplyBehavior` is explicitly set to `Evacuate`"* — survives only on `AutoRearm`'s own null branch, which the `Auto` arm no longer reaches.
>
> **The architectural principle the old sentence stated is still right, and the exception is deliberately narrow.** Sector-level judgement remains where the bot's version lives (`AmmoEvacMath` + `PoiOffensiveBotModule`); the unit-level arm fires only on a conjunction that needs no sector knowledge — wholly dry, mobile, naming rearm actors at all, positive leash, and **no host that exists** either inside the leash or able to travel to us.
>
> **State the predicate as "none within 30 cells", not "no rearm actor exists".** The ruling was worded the second way and the code is the first: the leash is `AmmoPoolInfo.DryRearmLeashCells`, shipping at **30** and overridden in no mod YAML, against maps running 66×34 up to 128×128. So a wholly dry vehicle with a fully stocked Logistics Centre **31 cells away evacuates** — the depot is real, stocked and irrelevant, because it is static and the unit will not cross to it. That is intended (pinned by `DrainedDistantStaticDepotStillEvacuates`), but it is a materially broader trigger than the ruling's wording suggests, and no doc or commit message should repeat the narrower phrasing.
>
> The distinction that keeps it honest is **drained ≠ absent.** Because `RearmsUnits` appears nowhere in `mods/ww3mod/`, every host is a `SupplyProvider` and `ChooseResupplier` filters them on `CurrentSupply > 0` — so a null answer there also means *"the depot is standing right there but empty"*. That state is recoverable (`AbsorbsSupplyCache` calls `SupplyProvider.AddSupply` from nearby caches) and routine (`iskander` `SupplyValue: 1500` against the LC's `TotalSupply: 2250`, so one LC cannot fill one Iskander twice). **The evacuation gate therefore reads host EXISTENCE, never host stock.** The `Auto` arm also now applies `HostCanAffordSomethingWeNeed` before dispatching, matching the `Evacuate` arm, so a depot holding less than one batch is not treated as a destination.
>
> `Hold` is unchanged and still stands-and-flags, pending a separate ruling. A unit with **no `Rearmable` at all** is excluded outright — `^CrewMember` and every ejected crewman under it declare none anywhere in the `^CamoSoldier → ^Soldier → ^Infantry` chain, and a unit that never had a depot does not have a missing one.

**The dry-dispatch trigger is `Essential`-dry, NOT every-pool-empty.** This paragraph previously stated that `AutoRearmIfAllEmpty` requires **every** pool empty, so that "a unit dry on its main gun but holding a loaded secondary never enters the path at all". That was falsified by the `Essential` mechanism (2026-08-21) and the method's rename to `AutoRearmIfDry`: the gate is now `OutOfEssentialAmmo`, so a rifleman with a spent rifle and a live RPG round, or a tunguska out of SAMs with a full cannon, **does** enter the path. Consequences worth holding onto: the *seek* tier applies to those units, but the *evacuation* tier deliberately does not — it re-tests `AllPoolsEmpty`, because seeking is recoverable while evacuation is terminal and a unit that can still fire must not be spent for a refund.

**Evacuate-when-dry is opt-in per actor, and it is not just trucks.** Mind the names: `ResupplyBehavior` is the **enum type** (`AutoTarget.cs:28`, values `Hold`/`Auto`/`Evacuate`) — there is no `ResupplyBehavior:` YAML key. The two settable fields are **`InitialResupplyBehavior`** (human-owned) and **`InitialResupplyBehaviorAI`** (bot- or non-playable-owned), chosen at `AutoTarget.cs:473`, and an actor that sets only one keeps the default on the other. The `^AutoTarget` default is `Auto` for both (`defaults.yaml:322-323`); **six** actors override to `Evacuate`, all setting both keys — TRUK plus all five rocket-artillery pieces: **m270**, **grad** and **tos** (which have carried it since before the ruling), and **`himars`** and **`iskander`**, which gained it on 2026-08-30. A spent Grad rotating to the map edge is designed behaviour, not a bug.

**On the two launchers the stance is LOAD-BEARING, not cosmetic, and this is the trap worth banking.** Removing an actor's `Rearmable` does NOT by itself make it evacuate. `AutoRearmIfDry` switches on the stance, and the default `Auto` arm calls `SupplyHuntMath.DecideAutoDisposition`, which returns **`HoldAndFlag`** at `SupplyHuntMath.cs:269` for any actor naming no rearm actors — deliberately, so a `^CrewMember` who empties his pistol does not walk off the map. A launcher stripped of `Rearmable` and left on `Auto` therefore stands still forever: combat-inert, never disposed, flagging `NeedsResupply` for a rescue nothing can perform, since that flag's only reader is a Hunt-stance provider that must DRIVE to the client and the Centre is a building. The explicit `Evacuate` stance takes the other arm, whose `ChooseResupplier` returns null on a null `RearmableInfo` and falls straight through to `EvacuateForRefund`. Pinned by `test-strategic-launcher-ignores-depot`, whose RED arm sabotages the stance rather than the `Rearmable` for exactly this reason.

**`MobileInfo.LocomotorInfo.SharesCell` is the name-free infantry/vehicle discriminator.** `MobileInfo.LocomotorInfo` is resolved in `RulesetLoaded` (`Mobile.cs:143-154`), so it is readable straight off an `ActorInfo` with no trait instance and no `TraitDictionary` lookup. In WW3MOD `SharesCell: true` appears on exactly the four `foot*` locomotors (`world.yaml:32/47/64/80`) and no vehicle locomotor — which cleanly separates "topped up in place by a truck/cache push" (infantry) from "must drive to a depot" (vehicles) without an actor-name list and its case-mismatch hazard. Caveat: the `Walker` locomotor (`world.yaml:193`) does not share cells, so an infantry-like actor using it would misclassify.

## Ammo pools, batches, and per-round cost

### Properties

```yaml
AmmoPool@1:
    Ammo: 900           # Maximum rounds in the pool.
    ReloadCount: 100    # Batch size — rounds delivered per rearm cycle.
                        # Default 1 (per-round semantics).
    ReloadDelay: 50     # Ticks between rearm batches when self-reloading.
    SupplyValue: 5      # Cost per BATCH (not per round). Used for both:
                        #   - rearm: supply spent per batch delivered
                        #   - evac/sell: cash deducted per missing batch
```

Pool budget = `(Ammo / ReloadCount) × SupplyValue`. For Bradley 25mm above: `(900 / 100) × 5 = 45`.

### Why batches

Batching keeps integer math honest while letting us express low per-round cost. `ReloadCount: 100, SupplyValue: 5` is ~0.05 effective per round — affordable for a 900-round bulk autocannon on a 1500-cost IFV, with whole-number bookkeeping.

### One property, two uses

`SupplyValue` is the single cost-per-batch property. It's charged when a supply provider hands over a batch (rearm) and deducted when a unit evacuates with that batch missing (evac/sell).

### Tooltip format

The pool tooltip renders the batch math directly, as typed rows under the weapon's name
(`AmmoPoolInfo.ProvideTooltipDescription`):

```
25MM CHAINGUN
AMMO ......................... 900 rounds
REFILL ....................... 9 × 5 = 45 supply
```

Players see what one cycle costs and how many cycles fill the pool, not an opaque per-round number.

*(Corrected 2026-08-30: this documented `Ammo: 900 (9 batches × 100 rounds × 5 supply = 45)`, one
concatenated line. The round count moved to its own `Ammo` row when the tooltip became typed rows —
it is a capacity, not a term of a price — leaving `batches × supply = total` as the whole of the
refill arithmetic. The per-round figure is still never printed.)*

### Artillery salvo economics

A single artillery *volley* is priced as a batch, not per round: `SalvoCost(burst, reloadCount, supplyValue) = ceil(Burst / ReloadCount) × SupplyValue` (`FiresEconMath.cs:90`), the same whole-batch rounding the rearm/evac math uses. This is why rocket launchers and tube guns sit in different economic weight classes:

- **Rocket launchers fire a large `Burst`** (Grad 40, TOS 24, M270 12 — `weapons-ballistics.yaml`), so a volley repays hundreds of supply (e.g. Grad `8×85≈680`, M270 `12×70=840`, TOS `8×120=960`).
- **Tube guns fire `Burst` 1–3** (Paladin 3, Giatsint 1), so a shell repays ~60 supply.

So a lone $100 infantryman is worth a tube shell but not a Grad volley — the arithmetic reason a fires AI should gate rocket fire on target value.

**The AoE that catches a formation comes from the salvo spread, not the warhead.** The lethal footprint is the `Burst` rounds scattered across the projectile's `Inaccuracy` (the beaten zone), NOT the per-round `SpreadDamageWarhead.Spread` — which is sub-cell on every piece (64–196, i.e. < 0.2 cell; 1 cell = 1024). A cluster/AoE radius derived from warhead `Spread` alone would catch almost nothing.

## The supply chain

### Logistics Center (LC)

`Valued.Cost` **3000**, and **fielded by deploying a `LCCV` that also costs 3000** (see Core principle 3). *(Corrected 2026-08-30: both numbers read 3500 / 1200 here, the pre-2026-08-22 values. User ruling — "the LC should cost 3000" — raised LCCV from 1200 and holds the two equal, since the same ruling treats the driving and deployed forms as one thing. `structures.yaml` `Valued: Cost: 3000`; `vehicles.yaml` LCCV `Valued: Cost: 3000`. **The `RefundPercent` invariant below still holds at the new numbers** — 34 ≤ 100 × 3000/3000 = 100 — so the property is intact and only its worked example was stale.)* Spawns with `SupplyProvider.TotalSupply: 2250` (`structures.yaml:475`). *(Corrected 2026-08-30: this read 3000, the pre-2026-08-22 value. The reduction to 2250 was a user ruling — "There is no difference between when it is driving or when it is deployed, it carries the supplies it carries" — matching LCCV's undeployed load. Sizing arguments that cite 3000 predate it.)* The pool drains as:
- Vehicles dock and rearm directly (`SupplyValue × batches given`).
- Trucks drive in to restock (truck pulls supply from LC; LC drops by exactly the amount taken).

When the LC's pool hits zero it stops servicing rearm requests. The player deploys another LCCV, or relies on trucks that still have supply.

**Salvage is capped at the LCCV's cost, and that cap is load-bearing.** `Sellable.RefundPercent: 34` on `logisticscenter` puts the sell refund at 1020 against the 3000 it costs to field one, and `SpawnActorsOnSell.ValuePercent: 0` stops the sale additionally emitting technicians. The money pump this closed was real at the *old* numbers: deploy-and-sell paid 3500 in cash plus up to five 250-credit technicians for a 1200 outlay, repeatable. *(Figures updated 2026-08-30 with the 3000/3000 costs above; at parity the constraint is slack — `RefundPercent ≤ 100` — rather than the tight 34-vs-34.3 it once was.)* `RefundPercent` rather than `CustomSellValue` deliberately: the LC is capturable (via `^BasicBuilding` → `^NeutralOrOccupiedCapturable`) and bots rank capture targets by `GetSellValue` (`CaptureManagerBotModule.cs:147`), so its strategic valuation must track its full Cost while only its scrap value moves. **If either Cost changes, recompute: `RefundPercent ≤ 100 × LCCV.Cost / logisticscenter.Cost`.**

### Supply Truck (TRUK)

Cost 1000. Spawns with `SupplyProvider.TotalSupply: 750`.

Truck behavior:
- Drives near friendly **infantry** that need rearm. Delivers `ReloadCount` rounds per cycle, charges `SupplyValue` per batch from its own pool.
- Serves units whose `Rearmable.RearmActors` lists `truk` (infantry).
- **HALTS while it has a customer, and resumes the order it already had.** `SupplyProvider.ServingCondition` (`supply-serving`, set only on TRUK) is granted whenever the last aura scan found a unit that could be handed a batch right now, and TRUK's `Mobile.PauseOnCondition` names it — so a truck under a move order stops for the units it would otherwise drive past, and moves on the moment the last of them is topped up. Pausing `Mobile` rather than cancelling is what makes resuming free: `Move.Tick` returns false while paused (`Move.cs:168`) and the activity is left standing. **The escape hatch is the `HoldPosition` engagement stance**, which switches the halt off — that value was chosen because it is the only one on a live stance axis with no other truck-side reader, and because it already means "does what I told it" elsewhere (`ControlAllUnitsManager.cs:56-59`). `ResupplyBehavior` could not carry this: all three of its values are already live on TRUK. Two consequences worth knowing: a halted truck will not obey a fresh move order until its customers are full or you switch it to `HoldPosition`; and the halt terminates by construction, because serving is what empties the set that holds it.
- When low (`currentSupply < RestockThreshold`, 50) an **Auto**-stance truck drives back to nearest LC (`RestockActors: logisticscenter`) and refills. But TRUK's default resupply stance is **Evacuate** for both human and AI (`vehicles.yaml:514-515`), so a low truck normally rotates to the map edge to return its credit rather than shuttling — see the residue-evac rule below.
- Refill drains the LC's `currentSupply` by the amount taken. A truck that needs 600 supply takes 600 from the LC, leaving the LC with 2400. If the LC has less than the truck wants, the truck takes what's there and leaves partially full.
- Can drop its remaining supply as a SUPPLYCACHE box — by the player's deploy command, or, for a bot-owned truck, as the **dangerous-mode delivery**. The drop is all-or-nothing either way (`DropSupplyCacheHere` → `SetSupply(0)`): there is no partial unload.
- **A bot truck's delivery MODE is chosen by believed danger; danger never decides whether to go.** Quiet front → close to aura range, serve in place, **keep** the remainder for the next customer. Under fire → stop short of the platoon, unload everything, egress. The classifier, the commitment invariants, and why infantry walking to a placed crate is correct behaviour are in [`supply-route.md`](supply-route.md) §"Forward delivery" — not restated here.
- **A COMMITTED SUPPLY ERRAND is never interrupted, by the halt above or by the dry break-off below.** `SupplyProvider.OnSupplyErrand` walks the activity queue for the three named errand types — `RestockSupply` (drive to a host and refill), `PlaceSupplyCache` (drive to a cell and unload), `CollectSupplyCache` (drive to a crate and load it) — and both mechanisms stand down while one is running. Each errand is a *named type* precisely so this question can be asked at all: a bare `MoveTo` is indistinguishable from a player's move order, which is why the distinction did not exist before. The failure this prevents is not theoretical — a truck that halts to serve the platoon it was sent to unload *near* never reaches the drop cell, never places a crate, and lingers in the danger the errand was routing it out of, while the platoon still ends up resupplied so the outcome looks fine.
- **Runs dry under orders → breaks off.** `DropsSupplyCache` re-checks `CountsAsEmpty` every `DryMoveScanInterval` (25) ticks while the truck is *not* idle, and cancels the current activity so the shipped `INotifyBecomingIdle` return/evacuation can run. Needed because that return is idle-triggered and a truck driving somewhere is never idle, so an empty truck used to complete the whole drive first — the same blind spot `AutoSeekSupplies.ReturnWhenEmpty` closes for infantry. **The rule is "cancel a move that is invalidated by being empty; never cancel a move that exists to stop being empty."** Exempt: any committed supply errand (above), a truck already evacuating (`evacuating` condition — that *is* the disposition being aimed for), and `ResupplyBehavior: Hold`, where `EvacuateOrRestock` returns immediately so a cancel would destroy the standing order and put nothing in its place.

**Unusable-residue trucks count-as-empty and evacuate** (gated by `SupplyProvider.EvacuateOnUnusableResidue`, true only on TRUK — `vehicles.yaml:549`). A near-empty truck holding a residue too small to give any nearby soldier a batch would otherwise park at the front forever:

- `SupplyProvider.CountsAsEmpty` = `currentSupply <= 0 || residueUnusable` (`SupplyProvider.cs:120`). The `residueUnusable` latch is set from a `MinNeedThreshold`-aware `ResidueVerdict` (`:693`, NUnit-pinned) — true (evac) when no reachable unit can be served a batch, false (keep serving) when one can, null (leave the latch) when there is no demand. Every refill path clears the latch, so a full truck never shows a phantom red bar.
- An Evacuate truck **keeps serving below `RestockThreshold`** (`KeepServingBelowThreshold`, `:272-274`) instead of reserving the last bit for a drive home it will never make — it serves down to the last usable batch, *then* the residue goes unusable and `CountsAsEmpty` carries it to evac. Self-restock is stance-aware (`ShouldSelfRestock`, `:257`): an Evacuate truck does not auto-shuttle to an LC (switching it to Auto restores restock-when-low).
- The supply **selection bar turns red** (`ISelectionBar.GetColor`, `:674`) while `residueUnusable`; a truly drained truck (`currentSupply == 0`) stays amber. The empty-evac path itself is `DropsSupplyCache.OnBecomingIdle` plus an `ITick` re-check (a truck that goes idle then has its residue become unusable never gets a second `INotifyBecomingIdle`), both gated on `CountsAsEmpty`. `SupplyFollowerBotModule.IsLowOnSupply` also returns true on `CountsAsEmpty` so the bot stops re-tasking an evacuating truck forward.

### SUPPLYCACHE (dropped supply box)

Spawned when a truck unloads its supply on the ground. Functionally a stationary truck — same `SupplyProvider` trait, same UI:

- **Range circle** showing rearm reach (5 cells, matching TRUK). *(Pending visual verification in-game.)*
- **Selection bar** showing remaining supply. *(Pending visual verification in-game.)*
- Sprite tier (Full/Mid/Low) reflects the supply remaining.
- Capturable by enemies (`ProximityCapturable`) — if the enemy reaches it first, the supply changes hands at full value.
- **Auto-targetable like the truck — carries no `NoAutoTarget`.** Its `Targetable: TargetTypes: Ground, Structure` (`misc.yaml:387`) matches the base `AutoTargetPriority@FireAtWill`, so nearby enemies engage and destroy it unaided (HP 5000, Light armor). An earlier `NoAutoTarget` that made it inert to enemy fire was removed.
- **Serves down to empty — and that takes BOTH `RemoveBelowSupply: 1` and `RestockThreshold: 0`.** They are a matched pair answering different questions, and until 2026-08-21 only the first was set, which made this bullet false. `RemoveBelowSupply` decides when a crate **despawns**: `SupplyProvider.Tick` disposes a provider once `currentSupply < RemoveBelowSupply`. `RestockThreshold` decides when it stops **serving** (`SupplyProvider.ReservesRemainderForRestock`, read by both the tick ladder and `CanServeNow`) — and it **defaults to 50**, which SUPPLYCACHE never overrode. A crate holding 1..49 supply therefore served nobody while sitting *above* the removal floor that would have cleaned it up, parking permanently with a visible supply bar and an intact sprite: the "crate doesn't rearm even though it has supplies left" report (live play, 2026-08-21). The band is reachable by draining into it *or* by being dropped into it — `DropsSupplyCache` seeds a crate with the truck's exact remaining load (`DropsSupplyCache.cs:199`), and an Evacuate-stance truck serves below its own threshold. A truck reserves that remainder to afford the drive to an LC; a crate has nowhere to drive, so it reserves nothing. Pinned by `SupplyCacheTruckParityTest` and the `test-crate-rearm-low` autotest.
- **How far a unit will walk to rearm, all three paths (2026-08-21).** `AutoSeekSupplies` idle seek at `SupplyHuntLeashCells` **20** straight-line cells; its `ReturnWhenEmpty` break-off from a live order at `ReturnWhenEmptyLeashCells` **30** chessboard cells; and the dry self-dispatch — `AmmoPool.AutoRearmIfAllEmpty`, reached both from `INotifyBecomingIdle` and from firing the last round — at `AmmoPoolInfo.DryRearmLeashCells` **30** chessboard cells. That third bound is new: the path had no distance test at all, which only started to matter once crates became destinations. Beyond any of them the unit stays put and raises `NeedsResupply` so a Hunt-stance provider comes to it instead. **All three use `SupplyHuntMath.WithinCellBudget`/`WithinLeash`, and `0` means "admits nothing" in all three** — not "unlimited", which is what `0` means on `PoiOffensiveBotModule.OutOfAmmoRearmSeekRadiusCells`. Two opposite zero-semantics for one idea already exist in this codebase; state which you mean, never infer it.
- **Two 2026-08-21 changes compound on drain rate, and neither was sized against the other.** `RearmDelay` went 25 -> 6 (four batches a second where there was one), and infantry began *walking to* crates rather than only being served by whoever stood in the aura. Under the old push-only model a crate's 750 supply was spent only by passers-by at a quarter of this rate; it can now be actively consumed by units that came for it. **Nobody has measured how long a dropped crate survives under the combined change** — the expectation is "much faster than before" with no number attached. If forward dumps start feeling disposable, this pairing is the first place to look, and `RearmDelay` is the cheaper of the two to walk back.
- **Radius and rate match TRUK exactly — `Range: 5c0`, `RearmDelay: 6`.** A crate is the truck's own load set on the ground, serving the same infantry through the same `replenish-soldiers` push, so dropping one buys a *stationary* resupply, not a weaker one. It previously ran at `4c0`/`25` — a quarter the rate and a cell shorter in reach — for no recorded reason (live play, 2026-08-21: "it should rearm just the same as the supply truck does, same radius/speed etc."). Pinned against TRUK by `SupplyCacheTruckParityTest`, which also pins `RenderRangeCircle@Supply.FallbackRange` to `Range` — that circle is the only thing telling a player how far a crate reaches, so a stale copy draws a lie on the map.
- Sits in place until drained, captured, or destroyed. The player recovers a cache's remaining supply by absorbing it into a friendly LC (the LC's `AbsorbsSupplyCache` trait pulls in any nearby cache), by **right-clicking it with a supply truck** (the `PickupSupply` order — the truck drives over and loads whatever fits inside its own headroom), or by spending it through infantry rearming off it. Both routes are available on every map: the truck needs no host, and the LC route needs an LC, which any player can field by deploying an `LCCV` (Core principle 3) as well as by capturing one of the three pre-placed Neutral ones. *(**Corrected 2026-08-17.** This bullet previously claimed the LC route was "mostly unavailable … on the other seven a truck is the *only* way supply put on the ground ever comes back", reasoning from the same `~disabled` mistake corrected at Core principle 3.)*
- **Truck collection is an ORDER, never an aura — and that asymmetry with the LC is deliberate.** `DropSupplyCacheHere` places the crate on the truck's *own* cell, so a proximity absorber on a truck would re-swallow the load it had just dropped; it would also eat forward dumps the player placed on purpose for infantry to walk to. Both failures are silent and both undo a deliberate act, so collection is asked for, exactly like the drop it mirrors. The transfer is capped at the truck's headroom: a crate holding more than the truck can take is partially emptied and stays put with the remainder.

### Cash flow recap

| Action | Cash effect |
|---|---|
| Call in unit (any) | `−Cost` (cash drops by full unit cost; ammo is bundled in) |
| Unit destroyed in combat | Permanent loss of `Cost` |
| Unit rotated to map edge with full ammo | `+Cost × HP/MaxHP` |
| Unit rotated to map edge with empty ammo | `+(Cost − sum_pools(missing_batches × SupplyValue)) × HP/MaxHP` |
| Sell building with supply (LC) | `+max(0, Cost − missing_supply_value) × RefundPercent/100 × HP/MaxHP` — supply refunds at constant rate; the LC sets `RefundPercent: 34` (see "Logistics Center") |
| Truck drops cache, drains in field | Spent supply is gone; remaining supply still recoverable via absorb/capture |
| Capture an enemy SUPPLYCACHE | Free supply at full value (war booty) |
| LC absorbs nearby friendly SUPPLYCACHE | Supply transfers from cache to LC at full value |

Sell formula. `GetSellValue` is the single path for the **salvage value**; the **cash paid** then scales it by health and, on the evacuation paths, by the owner's handicap:

```
sellValue = max(0, Cost
                 − sum_pools(floor(missing_rounds / ReloadCount) × SupplyValue)
                 − missing_supply_value)     // for actors with a SupplyProvider
                                             // (CustomSellValue.cs:36-51)

refund    = sellValue × RefundPercent/100 × HP/MaxHP        // Sell.cs:41, RotateToEdge.cs:381
evacRefund = handicapAdjust(sellValue) × HP/MaxHP           // RotateToEdge.cs:377
             where handicapAdjust(v) = v × 100/(100−handicap)
```

**The `HP/MaxHP` term is not optional and it is not decoration** — it is what stops a wrecked unit being worth a fresh one, and every code path applies it (`RotateToEdge.DoSell`, `Sell.cs`, and the `Sellable` tooltip). *(**Added 2026-08-17.** The formula in this file previously omitted it entirely, while three bot modules cited this file as the source for `GetSellValue × HP/MaxHP`. The code was right and the doc was incomplete; the doc is what changed.)*

`handicapAdjust` mirrors `HandicapProductionMultiplier`, which inflates what a handicapped player *pays* by the identical factor — so the two must move together. All three evacuation paths (`DeliversCash` rotation, `AmmoPool`'s evacuate-when-dry, `DropsSupplyCache`'s empty-truck return) go through `CustomSellValueExts.GetEvacuationRefund`; **do not compute an evacuation refund any other way.**

## Per-platform ammo budget targets

These are guideline ratios (`pool budget / unit Cost`). Specific per-pool values live in the YAML; the vehicle roster is tabulated below.

**Audit against these ratios and nothing else — two rival cost models are still lying around.** A 2026-08-16 sweep of ~63 `AmmoPool`s found three models in the repo and only one of them shipped:
1. **This section's budget ratios** — pick `SupplyValue` so `(Ammo/ReloadCount) × SupplyValue` lands at a target fraction of `Valued.Cost`. This is what the YAML implements, and the per-pool comments are written in it (e.g. `vehicles-america.yaml:519`, "8 batches × 5 shells × 30 supply = 240 (9.6% of cost 2500)").
2. **A per-shot tier table** in `WORKSPACE/archive/plans/260506_supply_ammo_economy.md:306-320` (T0=1 … T9=1500). **Stale — do not audit against it.** It survives only at the top end (T8 Hellfire 200, T9 HIMARS/Iskander 1500); T0–T5 are off by 8×–150× because the ratio model drove bulk per-shot costs toward zero. It also disagrees with this file on two munitions, and the YAML follows this file in both.
3. **A `SupplyValue == CreditValue` convention** from the same plan. **`CreditValue` does not exist on `AmmoPoolInfo` and no YAML sets it** — the only near-name is `SupplyProviderInfo.SupplyCreditValue` (`SupplyProvider.cs:86`), a different field on a different trait, used for the *supply pool's* refund. The convention was superseded, never implemented.

Corollary that cost a real bug: **`Buildable.Description` strings are a consumer of the cost model.** Ten infantry blurbs quoted per-shot numbers from model (2) and nine were wrong against shipped YAML; they were re-synced in `46e6950a`. When a rate changes, grep `Description:` for numerals before calling the re-tune done.

| Class | Total pool budget | Reason |
|---|---|---|
| Bulk MG / autocannon / SMG / rifle (high Ammo, cheap rounds) | ~3–10% | Bullets cost something — even cheap rounds drain truck supply, so a unit can't sustain indefinitely. Batch-cost lets us keep individual rounds nearly free while the pool total still bites. |
| Tank main gun (40 shells) | ~10% | Ammo is cheap relative to a tank. Empty tank evac refunds ~90% of cost. |
| Infantry RPG / ATGM / MANPADS (1–3 missiles) | ~30–65% | Missile-tier ammo — significant deduction, but the soldier's body still has value. |
| IFV ATGM (Bradley TOW, BMP-2 WGM) | ~40% | Real-world ratio. The missile load is the IFV's main combat value above the autocannon. |
| Helicopter / aircraft Hellfire | ~13–27% | Universal Hellfire rate per missile regardless of platform. |
| Mobile artillery (155mm / 152mm) | ~25% | Shell pool sized to artillery doctrine. |
| MLRS one-shot magazine | ~45–50% | The rocket pod *is* the platform's value. |
| Long-range missile platform (HIMARS, Iskander) | ~50% | Two missiles per launcher; the launcher is mostly the missiles. |

### The vehicle ammo roster — every armed vehicle carries a live pool

**There is no armed vehicle anywhere that shoots without drawing from a pool**, and the capacities are deliberately small. Audited across all four vehicle files (as of 2026-08):

| File | Actor | Live pool capacities |
|---|---|---|
| `vehicles-america.yaml` | `humvee` | 300 |
| | `m113` | 500 |
| | `bradley` | 900 + 8 |
| | `abrams` | **40** |
| | `m109` | **39** |
| | `m270` | **12** |
| | `strykershorad` | 400 + 8 + 4 |
| | `HIMARS` | **2** |
| `vehicles-russia.yaml` | `btr` | 500 |
| | `bmp2` | 900 + 8 |
| | `t90` | **40** |
| | `giatsint` | **39** |
| | `grad` | 40 |
| | `tos` | **24** |
| | `tunguska` | 180 + 8 |
| | `iskander` | **2** |
| `vehicles-ukraine.yaml` | `t72` | **40** |
| `vehicles.yaml` | `MNLY` | 10 (`mines-ammo`, feeds `Minelayer`, not an armament) |

The only pool-less vehicles are the ones with **no gun**: `MSAR`, `TRUK`, `LCCV` (unarmed support), the shared `^Vehicle`/`^WheeledVehicle`/`^TrackedVehicle`/`^Walker` templates, and the two projectile-carrier actors `HIMARSMissile`/`IskanderMissile`. At 40 rounds for a main battle tank and 2 for `iskander`/`HIMARS`, **a vehicle running dry mid-battle is the normal course of a fight, not an edge case.**

**`vehicles.yaml` is the least representative member of its own category — do not read it as "the vehicles".** It holds the shared templates plus a handful of unarmed support units; every actual combat vehicle lives in `vehicles-america.yaml`, `vehicles-russia.yaml` or `vehicles-ukraine.yaml`. Grepping `vehicles.yaml` alone for `AmmoPool` returns one live block and several commented-out ones, which reads as "vehicles used to have ammo and it was disabled" — the exact opposite of the truth. The commented blocks are not disabled pools on live units; they are fragments of two **entirely commented-out actors** (`SandBagLayer` ~`:650`, `timberwolf` ~`:709`, disabled in `296a529c`, 2023-06-20), and a commented `AmmoPool@1:` line looks identical either way. **General rule: before concluding anything about "all vehicles", grep every file under `mods/ww3mod/rules/` rather than the file whose name matches the concept.** The per-faction split means the category-named file is frequently the least representative one.

**Two structural splits inside that roster**, both easy to get wrong by generalising from the tanks:

- **The attack trait is not uniformly `AttackTurreted`.** `giatsint` (`vehicles-russia.yaml:489`) and `iskander` (`:985`) are `AttackFrontal`; the other fifteen are `AttackTurreted`. `AttackFrontal.GetAttackActivity` returns `Activities.Attack` while `AttackTurreted` inherits `AttackFollow.AttackActivity` — so **the vehicle roster is split across both attack activities**, and `Activities/Attack.cs` is not the infantry-only path it looks like.
- **No vehicle has `AutoSeekSupplies` at all.** The trait appears on exactly two templates in the whole mod, both infantry: `^Soldier` (`infantry.yaml:228`, `ReturnWhenEmpty: true`) and `^E6` (`:1949`, `ReturnWhenEmpty: false`). So none of that trait's machinery — neither the idle seek nor `ReturnWhenEmpty` — applies to a single vehicle, whatever its `Rearmable` status.

**`AutoSeekSupplies.ReturnWhenEmpty` bounds what it will interrupt an order FOR — it is not a guarantee that a dry unit stops attacking.** Its `ITick` returns immediately when the actor has no `Rearmable` (`AutoSeekSupplies.cs:226-227`), and when `ChooseResupplier` returns null, the host is out of the world, or the host fails `WithinReturnLeash` (`ReturnWhenEmptyLeashCells`, default 30, `:84`), it flags `NeedsResupply` and **deliberately leaves the unit's current order alone** (`:281-288`) — "an unreachable errand is worse than none". That decision is correct on its own terms. It does mean that anything which must release a dry unit from an undischargeable order has to live in the attack activities, not in the seek trait.

**"Can this unit shoot?" has THREE different answers, and picking the wrong grain is a recurring bug.**

| Grain | Predicate | Right for | Wrong for |
|---|---|---|---|
| Per-**actor** | `AmmoPool.CannotFight(self)` — `AllPoolsEmpty && !HasTraitInfo<AircraftInfo>()` (`AmmoPool.cs:216`) | "nothing left to shoot with, anywhere": resupply dispatch, cheap early-outs | **any question about a specific target** — a unit can be unable to engage what is in front of it while a pool it cannot use here is still loaded |
| Per-**armament, per-target** | `ChooseArmamentsForTarget(t, force)` **plus** `!a.IsTraitPaused` | "is there a weapon both legal against THIS target and able to fire?" — e.g. should I stop moving to aim | — (`ChooseArmamentsForTarget` supplies only the legality half; it filters `IsTraitDisabled`, and an empty armament is *paused*, not disabled) |
| Per-**armament firing gate** | `Armament.CanFire` (`Armament.cs:327`) | the shot itself | **any movement or engagement decision** — it also tests `IsReloading`/`IsWaitingBurst`/`IsAiming`, transient per-shot states, so reading it would make a unit decline to engage merely because it is between bursts |

Aircraft are carved out of the per-actor grain deliberately: a dry airframe recovers through its own idle `ReturnToBase` flow, and tearing its activity down from outside fights that flow (see "What rearms what" — no aircraft host exists today anyway).

**The concrete case, and it is not exotic.** `^E3`, the standard rifleman, carries a DMR (`primary`, 100 rounds) and an RPG (`secondary`, 1 round) whose weapon declares `InvalidTargets: Infantry` (`weapons-ballistics.yaml:302`). After any infantry-vs-infantry firefight the DMR is spent and the RPG is still loaded — there was never a legal target for it. So against infantry his only offered armament is one he cannot fire, while `CannotFight` reads **false** because a pool is loaded. Both his `ReloadAmmoPool`s are gated `RequiresCondition: replenish-soldiers`, so it does not heal on its own. **Generalisation: a multi-pool unit whose weapons have disjoint `ValidTargets` will routinely sit at "empty for the enemy in front of me, loaded overall", and any per-actor ammo predicate is blind to exactly that state.**

Two alternatives that are simply wrong at every grain: `Rearmable.RearmableAmmoPools` (answers a different question) and the `^AmmoDecoration` red-pip condition (implied by emptiness, not equal to it).

**`Rearmable.AmmoPools` and "every `AmmoPool` on the actor" are DIFFERENT SETS — and on 13 of 14 infantry classes they coincide, which is exactly what makes the wrong one look right.** `Rearmable` filters to its declared list (`Rearmable.cs:44`), so it answers "which pools does a *host* refill?", not "which pools does this unit have?". Of the 14 `infantry.yaml` classes carrying both traits (`^E1 ^E3 ^AR ^E2 ^TL ^MT ^SN ^AT ^AA ^E6 ^E4 ^SF ^DR ^PILOT`), the three two-pool combat classes (`^E3`, `^TL`, `^SF`) all list both pools — so a test on any of them passes either way. **`^E6` is the single divergence:** it lists only `secondary-ammo` (`infantry.yaml:1941-1943`) while also carrying a 100-round `primary-ammo` (`:1859-1863`). Scope the count to `infantry.yaml` — `^CrewMember` (`crew.yaml:5`) is a 15th armed man-class with a single, listed pool, so counting it gives 14 of 15 instead.

**The caveat on `IsTraitPaused` attaches to the QUESTION, not to the predicate.** `AmmoPool.cs:210-214` warns against "every armament paused" as a `CannotFight` substitute, and that warning is right *for resupply dispatch* — but pause-for-any-reason is exactly correct for "should I stop walking to aim at this?", where it also picks up `suppressed >= 10`, `empdisable`, `heavy-damage-attained` and `inwater`, each of which wedges a move by the identical route. A warning recorded against one call site is not a global prohibition. *(That comment's stated example is itself inaccurate — see the note at the site; the caveat stands on the other terms.)*

**Do NOT fix this inside `ChooseArmamentsForTarget`.** Its `// FF TODO Check ammo?` reads like an invitation, but `AttackBase.AbandonWhenArmamentsPaused` was added *specifically* because "every armament paused" must not end an attack by default — holding aim through a brief pause is the wanted behaviour. Filtering paused armaments in the shared method would silently flip that opt-in on for every unit in the game and strip the attack cursor off any momentarily-paused one. **A standing TODO is not evidence that the change is safe where it sits: check whether a later opt-in was built precisely to avoid making it there.**

### Munition consistency rule

The same munition costs the same supply across every platform:
- **Hellfire**: per-missile SupplyValue 200 (Apache, MI-28, A-10, Stryker SHORAD, Littlebird).
- **ATGM** (TOW / Konkurs): per-missile 65–75 (Bradley 75, BMP-2 65, AT specialist 65).
- **MANPAD / short-range SAM**: per-missile 65 (Stryker Stinger, Tunguska 9M311, AA specialist).
- **Air-to-air missile**: per-missile 100 (F-16, MIG).

If a platform's missile rate changes, change every other platform that fires the same munition.

### Infantry empty-evac base

Most line-infantry classes train similarly. The cost above body+training baseline is the ammunition load. So when a soldier evacuates with all ammo expended, they refund roughly the same baseline:

| Tier | Empty evac refund | Examples |
|---|---|---|
| Conscript | ~50 | E1 |
| Line infantry | ~100 | E3 (rifleman+RPG), AR (LMG), E2 (grenadier), MT (mortar), AT (ATGM), AA (MANPAD), E4 (flame), E6 (engineer), MEDI, DR (drone) |
| Squad role w/ extra training | ~150 | TL (team leader) |
| Premium specialist | ~200 | SN (sniper) |
| Elite | ~500 | SF (special forces), PILOT (and ranks) |

Per pool: `SupplyValue = (Cost − base) / batches`, where `batches = Ammo / ReloadCount`.

## Engine architecture

### Single trait: `SupplyProvider`

Trucks, LCs, and SUPPLYCACHEs all use `SupplyProvider`. They differ only in YAML config:

| Source | TotalSupply | RestockActors | Notes |
|---|---|---|---|
| `logisticscenter` | 2250 | (none) | Mounts at base; drains until empty. `AbsorbsSupplyCache` recovers dropped boxes. *(Corrected 2026-08-30: this cell read 3000, contradicting the prose above it. `structures.yaml` sets `TotalSupply: 2250` and `SupplyCreditValue: 2250`.)* |
| `truk` | 750 | `[logisticscenter]` | Mobile; drives to LC when low; can drop a SUPPLYCACHE. |
| `supplycache` | 750 | (none) | Stationary; TRUK's own radius/rate (`5c0`/`6`). Reserves nothing (`RestockThreshold: 0`), so it serves down to supply 0, then despawns (`RemoveBelowSupply: 1`) or is captured. |

### Rearm cost math

**Corrected 2026-08-30 — the multi-batch pseudocode that stood here is NOT what ships, and the difference is a rounding loss the reader would not predict.** The single serving primitive is `AmmoPool.TryServeBatch` (`AmmoPool.cs:440-461`), and it hands over **exactly one batch per call** and deducts the **full `SupplyValue` regardless of how few rounds actually landed**:

```csharp
var cost = pool.Info.SupplyValue;
if (provider != null && provider.CurrentSupply < cost)
    return false;

var batch = Math.Max(1, pool.Info.ReloadCount);
var missing = pool.Info.Ammo - pool.CurrentAmmoCount;
if (!pool.GiveAmmo(client, Math.Min(batch, missing)))
    return false;

// Charged AFTER the ammunition lands, so a GiveAmmo that declines cannot bill the depot.
provider?.DeductSupply(cost);
```

So a pool one round short of full still costs a whole batch, and a unit needing three batches takes three `RearmDelay` cycles rather than one transfer. The previous text computed `batchesToGive = Math.Min(batchesNeeded, batchesAvailable)` and charged proportionally; **any sizing argument derived from it understates both the time and the supply a refill costs.**

In `CustomSellValue`:
```csharp
foreach (var pool in a.TraitsImplementing<AmmoPool>())
{
    if (pool.Info.SupplyValue > 0)
    {
        var missingBatches = (pool.Info.Ammo - pool.CurrentAmmoCount) / pool.Info.ReloadCount;
        missingAmmoValue += missingBatches * pool.Info.SupplyValue;
    }
}
```

### LC restock drain

In `SupplyProvider.TryRestock` (called on the truck), when the truck arrives at the LC:
```csharp
var taken = Math.Min(Info.TotalSupply - currentSupply, lcSupplyProvider.CurrentSupply);
lcSupplyProvider.RemoveSupply(taken);
currentSupply += taken;
```

The LC pool drops by exactly what the truck took. Truck might leave partially full if the LC didn't have enough.

## Where the live values live

This doc describes the rules. The current per-pool numbers are in YAML and shift more often than this doc updates. To list every pool's batch math:

```
git grep -nE 'AmmoPool|ReloadCount|SupplyValue' mods/ww3mod/rules/
```

## When tuning further

- **Munition consistency**: a Hellfire is a Hellfire. Changing Apache's per-batch SupplyValue means changing every Hellfire-firing platform to match.
- **Per-tier infantry baseline**: when raising a soldier's `Cost`, the extra goes into the ammo budget so the empty-evac refund stays at the tier baseline.
- **Bulk-ammo cap**: a pool's full budget fits inside one truck-load (~750). Above that, combat economics break down.
- **Pool budget ceiling**: a pool's `(Ammo / ReloadCount) × SupplyValue` is at most `Cost − minimum-empty-refund`, so an empty unit always retains some salvage value.
