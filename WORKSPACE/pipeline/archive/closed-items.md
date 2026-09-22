# Archive — closed, retired and shipped queue items

_Split out of `WORKSPACE/PIPELINE.md` on 2026-08-19 at `main @ de78a1ed`. **Text is verbatim; nothing was summarised or dropped.**_

**This is not a graveyard — it is the reason the queue can be short.** Every entry here is finished, but several carry rulings and traps that are still load-bearing for anyone editing the code they describe. Read this file before concluding that something was never considered, and before re-proposing anything.

**Nothing in this file is a task. Do not dispatch from it.**

The highest-traffic entries, and why they are still worth reading:

| Item | Still load-bearing because |
|---|---|
| **58** (passenger bail) | Settles that **"critical damage" in the user's language means `DamageState.Heavy` (HP < 50%), NOT the `critical-damage` condition (HP < 25%)** — an implementer who reaches for the condition builds the wrong trigger. Also carries the grep trap: `Cargo.cs:775`'s single-frame `foreach` is passenger *damage sharing*, not the bail. That mistake has been made once already. |
| **59** (neutralise on capture) | The `CaptureToNeutral` primitive it built is the one **item 17** (Supply Route capture) needs. Records that the delay is **60 s, not 40** — the 40 came from the Lua harness's `TicksPerSecond`, which is not a fact about the mod. |
| **60 / 61** (command bar) | `TAKE_COVER` is inert at **three** levels, so the obvious fix accomplishes nothing. Removal is fully costed. Also: moving `@EVACUATE` onto a `command-icons` region **cannot be done** — no such region exists. |
| **50 / 17-of-the-FX-audit** | **DECLINED BY THE USER.** Do not re-propose either without new art or a fresh instruction. |
| **51** (supply drift clause) | The 6-cell allowance does **not** transfer between the two sibling scenarios; 1 is derivable, 6 is not. |
| **52** (lint noise) | `--check-yaml` re-runs the whole rules lint **once per map** and never deduplicates. **Diff the error LIST, never the count.** |
| **63 / 66** (procurement) | The floor mechanisms and the precedence axis these built are what make item 57's remaining scope smaller than it reads. |
| **57** (build composition) | `UnitFloors` / `UnitFloorPer` / `UnitFloorSupportedTypes` **is** the general standing-population floor mechanism, live on both factions. This item's own text calls the opposite its "durable finding" — **that sentence is false and is marked as such in place.** Also: the medic actor is `medi.america`, so a bare `grep "medi:"` returns nothing and reads as a refutation of a floor that is actually there. |
| **65** (fields swallow shells) | **The "no damage" half was a MISATTRIBUTION — do not re-file it.** `SpreadDamageWarhead` / `TargetDamageWarhead` — the two `^ArtilleryRound` actually uses — never had the invalid-actor early-out, so the shell always did its damage and only lost its feedback. The likelier cause of a "no damage" report is the balance question at `bugs/discovered.md:443`. |
| **47** (GPLv3) | **A grep's scope is part of its claim.** Two separate false findings on one day traced to exactly this. |
| **8** (ambush implementation) | **The load-bearing line is ruling D — *"human-settable + bot behind the same default-off gate from day one."*** What shipped grants `enable-ambush-tactics` to bot-posted units only, so **the bot-only gate is a regression against this item's own design decision, not a design choice** (live item **68**, 2026-08-20). Also the origin of the "gate (b) benchmark pricing" thread, parked as void-corpus in `AWAITING-USER.md`. **Do not re-file ambush as unbuilt: all four stages are here.** |
| **78** (evacuation edge) | **Ground evacuation is owner-anchored — do not "restore" `?? self.Location`.** `RotateToEdge.cs:209` falls back to `FriendlyEvacuationOrigin`, the nearest friendly `SUPPLYROUTE`; the **aircraft** branch still uses `?? self.Owner.HomeLocation` (`:195`) and is correct as it stands. Carries the reverse of this file's usual trap: the item's evidence lives under `tools/evac-edge-math/`, **not** `WORKSPACE/`, and a `WORKSPACE`-scoped grep therefore reads shipped work as unstarted — a 2026-09-21 audit did exactly that. The ~12× exposure cost is unapproved and parked in `AWAITING-USER.md`. |
| **56** (supply truck delivery) | **The acceptance bar is DISCHARGED — do not re-open this on a red scenario.** Settles that the trucks DO commit: 4 deliveries from 5 dispatches in one live match, zero errands open at the clock, and **zero x-travel reversals** on all four. Carries three traps. (1) The first census line with a `truk=` term reads **`truk=0+0`**, so `grep -F 'truk='` is not the precondition — sum `inWorld+inCargo`. (2) **`drop-declined reason=` is not a contiguous string** — the line is `drop-declined truck=<id>@<x>,<y> reason=<R>`, and `-F` does not rescue it. (3) `[supply] truck=` lines **do** carry per-scan `@x,y`; the ×10 read-out's "these logs carry no truck positions" is wrong, and it is why the movement reading sat undischarged. Also: the reversal criterion **was never part of the bar** — it is §3's spec for a proposed scenario. `test-supply-safe-front-keeps-cargo` is still red, **on the merits and for an `AutoSeekSupplies` reason, not a supply-module one**. |

---

### 63. Early build order — the bot opens with two medics **[MERGED `d54671d6` 2026-08-15 — mechanism shipped, LOBBY verification outstanding]**
_**STATUS CORRECTED 2026-08-17.** This carried an `[IN FLIGHT]` tag for two days after it merged, and that stale tag caused the item to be dispatched a second time against a premise that was already fixed. The fix is `UnitFloorPer: medi.* 20` + `UnitFloorSupportedTypes` (`ai-america.yaml:168/178`, `ai-russia.yaml:123/129`): the standing floor gets a denominator, so it is **0 below 20 infantry** and the opening call-ins go to line infantry. Verified offline via `--composition-plan`, symmetric across both factions. **Not verified in a lobby game** — see item 66 and [`recon/260817-procurement-ordering-axis-status.md`](../../recon/260817-procurement-ordering-axis-status.md)._
**Perceived:** the bot's opening looks like a plan. Line infantry arrive first so it can defend an early rush or apply early pressure; medics and other support arrive later, when there is an army for them to support.
_**The user, verbatim:** "All experimental bots start by building two medics. We recently did some changes to medics so that the bots are supposed to build more of them than they did before (I told that I usually have around 1 medic per 20 man squad), but I also said that I don't want the bots to build two supply trucks at the start because they are not needed at that point. I said we need to have some kind of 'Build orders' that makes sense in the early game, and building two medics as the first priority is BAD because at the start we need lots of soldiers to be able to defend ourselves from any early aggression, or our own soldiers to apply pressure early, but the medics can come a bit later when they are actually needed."_
_**This is the SECOND report of one defect, and that is the whole point of the item.** The first was "two supply trucks at the start" (item 57(a)). The user is telling us the disease was never cured, only the truck symptom. **Medics are wanted — at roughly 1 per 20 infantry, the user's own stated ratio. The bug is ordering, not quantity.** A support unit whose value is proportional to the army it supports must not be bought before that army exists._
_**IT INVERTS ITEM 57(b), AND THE INVERSION IS THE EVIDENCE.** 57(b) was filed 2026-08-13 titled **"No medics are being procured"** with a leading (explicitly unmeasured) hypothesis: `ForceCompositionMath.SelectDeficit` (`:206-230`) is an **argmax over `target − census` in per-mille**, so a 9‰ slot can only win when every larger slot is at or over target. It warned the structural fix was **"a floor, or a separate small-slot lane"**, not a bigger number. `47e4ede5` then gave small types a floor — and the symptom flipped from **never bought** to **bought first**. **The pendulum crossed the target rather than landing on it, which localises the defect precisely: at t=0 every census is zero, so every floor is maximally unmet at exactly the moment need is lowest, and the cheapest floored types clear first.**_
_**Acceptance must therefore be general, not medic-shaped.** A fix that only stops medics leaves the third instance to be reported by the user in a fortnight. Cross-references: 57(a) trucks, 57(c) the AA/floor irony (57(a) and 57(c) pull the same lever in opposite directions)._
_**Two traps carried forward into the brief:** (1) **two independent floor mechanisms already exist in different modules** — `UnitFloors` on `UnitBuilderBotModuleInfo`, and `CaptureCoordinatorBotModule.MaintainTecnFloor`, the latter invisible from UnitBuilder's vantage because it requests via the single-name `BuildUnit(IBot, string)` overload that bypasses the `UnitLimits` lottery filter. A third floor on a population one of them already owns has its winner decided by module ordering. (2) **measure mid-match, not end-of-match** — `wt/composition` drew a confident wrong conclusion from end-state snapshots (claimed `inCargo` was 0 "because no transports were active"; a mid-match read showed `medi.*=0+2`, both medics inside a transport and invisible to `world.Actors`)._
_**Economy caveat — re-derive it, do not inherit it.** `56bf7355` recorded the bot as broke (cash 0 on 194 of 195 snapshots where it wanted a truck). `20aa5a8a` / `b91b5a88` (merged `2c274589`, `779d0b62`) then claim to have fixed bot map-players having no economy at all. **The economic ground under this subsystem moved within the last few commits**; neither the old "bot is broke" finding nor the new fix should be assumed._

### 66. Supply trucks are barely PROCURED, and out-of-ammo resupply is now ruled the standing top priority **[MERGED `1bbfdb7c` 2026-08-15 — precedence axis shipped and measured; LOBBY arm NEVER RUN]**
_**STATUS CORRECTED 2026-08-17. THE ORDERING AXIS THIS ITEM ASKS FOR EXISTS.** `SupplyPrecedenceStallCycles: 4` (`ai-america.yaml:305`, `ai-russia.yaml:177`) implements the user's precedence ruling literally: the cycle banks cash and buys **nothing** until the truck is affordable, instead of falling through and spending the truck's money on a rifleman. Alongside it: `GateResupplyOnAmmoNeed`, `SupplyDemandSizing`, `SupplySizeFromNeed`, `SupplyTruckFloor: 0`. Measured two-arm at seed 4242 with pre-registered predictions — **USA truck ordered tick 1980, Russia tick 3030, both reach the field, against a paired baseline of ZERO trucks for either player**._
_**THE ONE THING STILL OPEN IS THE INSTRUMENT, NOT THE MECHANISM.** `1bbfdb7c` closes with: "nothing here says this reproduces in **lobby games** rather than tournament map-players" — and the user plays lobby games. That is this item's own first candidate, still unresolved. Run spec, metrics and a pre-registered pass bar: [`recon/260817-procurement-ordering-axis-status.md`](../../recon/260817-procurement-ordering-axis-status.md) §5. **Do not rebalance a quantity if it reads short** — the named cause is an unattributed residual drain from another module type, and the stall tolerance is already measured as unwidenable in both directions (3 abandons Russia early, 2 breaks USA)._
**Perceived:** when soldiers run dry, trucks appear promptly and in numbers. Today a lot of soldiers sit out of ammo and almost no trucks are ever bought.
_**The user, verbatim:** "There are a lot of soldiers now that are out of ammo but still I see almost no supply trucks being built. When I saw it being built it seems like it correctly went to resupply them. So I think we just need to adjust the build priority, so that when units are out of ammo it should prioritize supply trucks. So I want to see much more supply trucks. Remember, soldiers out of ammo are useless. That should be the first priority to solve at all times."_
_**THE LAST SENTENCE IS A PRIORITY RULING AND SHOULD BE TREATED AS ONE: resupplying dry units is the TOP procurement priority at all times.** Not a weight, not a per-mille share — a **precedence**. This is the ordering spine the early-game work (item 63) was missing, and it makes the opening explicit: **(1) resupply capacity whenever units are actually dry → (2) line infantry early → (3) support at ratio (medics ~1 per 20 infantry) once there is an army to support.**_
_**THIS IS ITEM 63's DEFECT WITH THE PENDULUM SWUNG THE OTHER WAY, and the pair is the strongest evidence the queue has for the underlying disease.** Trace it: user complains of **two trucks at t=0** (57(a)) → `SupplyTruckFloor` set to **0**, ammo-need gate left to do the work (`56bf7355`) → user now sees **almost no trucks ever**. Medics ran the same course in reverse (never bought → bought first). **The system has a notion of HOW MANY and no notion of WHEN.** Both complaints are that one missing axis. A fix that supplies the axis resolves both; a fix that rebalances quantities buys a third report with a different unit._
_**Evidence already on record that must be RECONCILED, not re-derived.** `56bf7355` measured on the pre-economy-fix build: `ammo-need=True` on **383/450 snapshots**, `starving` peaking at **6**, `trucks-desired=2` against `starving=2` at tick 1240 — **the gate was OPEN and asking** — and **no truck ever bought because `cash=0` on 194 of the 195 snapshots where `trucks-desired>0`** (the exception read `cash=40` against a 1000 truck; peak cash after tick 600 was 40). Its author concluded *"the gate is fine; the bot is broke."* **Then `20aa5a8a` / `b91b5a88` landed claiming bot map-players had no economy at all — and the user is reporting this from live play AFTER those merges.** Three candidates needing different fixes: the economy fix reached the tournament/map-player path but not the profile the user plays; the bot has cash but the truck **loses the argmax**; or affordability is fine and something downstream (the `UnitLimits` lottery, a cap, a claim) drops the request. **Settle which before designing — "the bot is broke" is a conclusion drawn from a build that has since changed underneath it.**_
_**Do not overcorrect into the bug we already had.** "Much more supply trucks" is the ask, but a truck floor at t=0 is exactly what the user reported first. The target is trucks **when units are dry**, promptly and in proportion to how many are dry — **not a larger constant.** Between the two reports the user has now demonstrated both failure modes of a constant. **Acceptance should be a RESPONSIVENESS measure** — time from first dry unit to truck ordered, or dry-unit count over time — not a count._
_**Cross-reference item 56:** the same report contains the first positive live sighting of truck *delivery* working, which separates the two items cleanly — conduct is not the current defect, procurement is._

### 58. Passengers should start leaving a dying vehicle earlier, and staggered **[SHIPPED — verified 2026-08-19 against `main @ 66fd33d3`; do NOT dispatch this]**
_**ALL FOUR ASKS ARE NOW SATISFIED.** Everything below is preserved for its vocabulary ruling and its trap list, but the "one real gap" it names is closed. The pacing shipped **2026-08-13** in **`042dbdc4`** ("cargo: pace the emergency bail instead of dumping the whole stick in one frame"), confirmed an ancestor of `main` with `git merge-base --is-ancestor`. `EmergencyBailOut` is no longer a single-frame `foreach`: it is a self-scheduling chain, `EmergencyBailStep` (`Cargo.cs:891-940`), which calls the same `NextUnloadDelay(bailUnloaded, UnloadGroupSize, IntraGroupUnloadDelay, InterGroupUnloadDelayMultiplier)` cadence as the ordered path and re-arms through a `DelayedAction` each step. Trap (1) below — the hull dying mid-stagger — was handled with it: `Killed` owns the cargo from that point and `EjectOnDeath` places whoever had not reached the door._
_**Trap for whoever re-reads the code.** `Cargo.cs:775` still holds a single-frame `foreach (var passenger in Passengers.ToList())`. That is **passenger damage sharing** inside `INotifyDamage.Damaged` — NOT the bail. Grepping for that loop and concluding the item is still open is a mistake already made once, on 2026-08-19, while confirming this very entry._
_Found stale by the cargo/garrison reconciliation (`WORKSPACE/cargo-garrison-status-260819.md`). This is the **fifth** queue item this week found to describe already-merged work, and the first caught while still in the top-of-queue batch — the others were caught only after a worker had been dispatched at them._

**Perceived:** infantry spill out of a burning APC in a steady stream as it rolls, instead of the whole squad appearing in one frame.
_User's ask, verbatim in substance: ejection begins as soon as the vehicle is in critical damage, proceeds at the NORMAL unload cadence rather than all at once, and has NO delay — they start getting out immediately even while the vehicle is still moving, without waiting for it to stop._
_**VOCABULARY — settled, and the disambiguation is the point.** In this user's language **"critical damage" means `DamageState.Heavy`, i.e. HP below 50%** — the state where the vehicle catches fire and is doomed. It does **NOT** mean the `critical-damage` CONDITION, which in this mod is HP below **25%** (`defaults.yaml:194-196`, `GrantConditionOnDamageState@CriticalDamage`, `ValidDamageStates: Critical`). Thresholds from `Health.cs:95-104`: `<25%` → Critical, `<50%` → Heavy, `<75%` → Medium. **An implementer who reaches for the condition will build the wrong trigger.**_
_**VERIFIED AGAINST THE CODE 2026-08-13 — three of the four asks are already satisfied, and this is what makes the item small:**_
_- **Trigger is already right.** `CargoInfo.EmergencyBailDamageState = DamageState.Heavy` (`Cargo.cs:94`) — already the user's "critical damage". (Aircraft use a separate `AircraftEmergencyBailDamageState = DamageState.Critical`, `Cargo.cs:102`, selected at `:682`.) **No cargo or bail code reads the `critical-damage` condition at all** — that condition drives only smoke trails, speed/accuracy penalties and pips._
_- **There is already NO delay.** `EmergencyBailDelay` defaults to **0** (`Cargo.cs:110`) and **no YAML anywhere in `mods/` sets it.**_
_- **It already does NOT wait for the vehicle to stop.** `EmergencyBailOut` (`Cargo.cs:760-853`) has no `IsMoving`, velocity or stationary check; it reads `self.CenterPosition`/`self.Location` and places passengers immediately. The code says so itself at `Cargo.cs:753`: "no waiting for the hull to roll to a stop"._
_**THE ONE REAL GAP: the pacing.** `EmergencyBailOut` is a single `foreach (var passenger in Passengers.ToList())` (`Cargo.cs:775`) inside one tick, placing every man with `SetPosition`. **That bypass is DELIBERATE, not an oversight** — `Cargo.cs:90-91` states it: "This bail ignores the group pacing above — an ordered dismount is a drill, this is a burning vehicle." **So this item is a reversal of a deliberate design decision, and should be written up as one.**_
_**Next concrete step:** apply the ordered-unload cadence to the emergency path. The cadence already exists at `UnloadCargo.cs:183-203` — `NextUnloadDelay()` returns `IntraGroupUnloadDelay` normally and `× InterGroupUnloadDelayMultiplier` every `UnloadGroupSize`-th passenger. Shipped defaults (`Cargo.cs:70,80,85`): **`UnloadGroupSize: 2`, `IntraGroupUnloadDelay: 4`, `InterGroupUnloadDelayMultiplier: 3`** — pairs out 4 ticks apart, 12 ticks between pairs. **No mod YAML overrides any of the four fields**, so tuning is available without touching engine defaults. Keep `BeforeUnloadDelay` (8) and `AfterUnloadDelay` (25) OUT of the emergency path — those are the "drill" delays the user is explicitly rejecting._
_**Two things to get right, both trap-shaped:** (1) the bail currently completes inside one tick, so a paced version must survive the vehicle being **destroyed mid-stagger** — the remaining passengers need a defined outcome, and that is a behaviour question the user has not been asked. (2) **Transports with an `Aircraft` trait take a different branch** (`Cargo.cs:700-707`) that queues a normal `UnloadCargo`, which already paces **and** already lands first — do not "fix" that path into matching the ground one._
_**RESOLVES AN OPEN USER GATE — remove it from [`AWAITING-USER.md`](../../AWAITING-USER.md) when this is filed.** [`closeout/6361c2be.md`](../../closeout/6361c2be.md) §3 recorded an unanswered "bail order" question: passengers bail ~45 ticks before the crew, and it proposed `EmergencyBailDelay: 45` to restore parity by making passengers **WAIT**. **The user has now ruled the other way** — they want passengers out EARLIER and with no delay, so **the parity fix is REJECTED; do not re-propose it.** The caveat that travelled with it falls away with it: `EmergencyBailDelay > 0` is an uncovered code path (`Cargo.cs:718-749`, the `DelayedAction` + re-check branch, exercised by no test and no YAML), and that risk disappears because the value stays 0._
_Related, already shipped: `18838dd7` built the group pacing — but built it for the **ordered** unload path only._

### 59. A soldier should NEUTRALISE a captured money structure, not capture it **[SHIPPED — verified 2026-08-19 against `main @ 815804f1`, this entry was stale]**
_**CLOSED. No code change was needed; the behaviour below already ships, and wider than asked.** The audit in this entry is dated 2026-08-13 — the **same day** the feature landed — so it was written against pre-fix code and the entry was never closed. Shipped in `d3978f32` (2026-08-13, scoped to tech buildings) and widened to all 23 capturable actors in `072d30d9` (2026-08-14) on an explicit user ruling. **The "missing primitive" this entry is built around now exists:** `CapturesInfo.CaptureToNeutral` (`Captures.cs:51`, defaults `false`), honoured at `CaptureActor.cs:125`, with the world owner resolved structurally rather than by name (`:120-124`); ejection is `EnterBehaviour: Exit`, which `ApplyEnterBehaviour` (`:148-162`) implements by falling through the switch with no case. Both are set on `^CapturesOccupiedBuildings` (`infantry.yaml:852-853`). **It also already serves item 17** — `DOCS/reference/supply-route.md:74` was updated when it landed and says so; that cross-check is done, do not re-derive it. **Open question 1 ANSWERED — bots never reach the soldier-capture path:** `CapturingActorTypes` occurs in exactly two places in `mods/` (`ai.yaml:113` `@experimental.tecn`, `:2049` `@stable.tecn`), both `tecn,tecn.russia,tecn.america`; vanilla `CaptureManagerBotModule` is never instantiated at all, so **`@stable` did not drift** on the capture-issuing side. Bots remain affected as victims (no reclaim logic, tecn cap 3) — tracked at `DOCS/gameplay/capturing.md` open question 5. **Open question 2 ANSWERED:** the observation went through the occupied path, and its delay is **60 s, not the 40 s this queue and that doc both said** — `1000` ticks at `Timestep: 60` (`mod.yaml:358`/`:382`) is 16.67 ticks/s; the 40 came from the Lua harness's `TicksPerSecond = 25`, which `conventions.md:166` explicitly flags as not a fact about the mod. Doc corrected in `wt/neutralise`. Full write-up: `WORKSPACE/DISCOVERIES.md` 2026-08-19._
**Perceived:** a lone rifleman can no longer flip an enemy-held Derrick into his own income. He can deny it — the building goes Neutral and he walks back out — but owning it still costs you a technician.
_User's ask: today Rifleman and AR can **capture** money structures such as the Derrick when an enemy already holds them. Wanted: a soldier entering an **enemy-held** money structure turns it **NEUTRAL** and is then **EJECTED** again (the soldier is **not** consumed); taking actual ownership still requires bringing your own technician._
_**VERIFIED 2026-08-13 — the mechanic the user is describing is real and working, so this is a change, not a bug fix.** Chain proving money structures are genuinely capturable today (unlike `SUPPLYROUTE`, which per CLAUDE.md carries no `Capturable` and no `CaptureManager`): `OILB` "Oil Derrick" (`structures-neutral.yaml:1-31`, `CashTrickler: Amount: 50`) and `FCOM` "Expansion Post" (`:35-`, `Amount: 100`) both `Inherits: ^TechBuilding` (`structures.yaml:120-121`) → `^BasicBuilding` (`:1`) → `Inherits@NeutralOrOccupiedCapturable` (`:10`) → `^NeutralOrOccupiedCapturable` (`:149-157`), which declares `CaptureManager`, `Capturable@neutral: Types: building-neutral` and `Capturable@occupied: Types: building-occupied`._
_**How capture is gated on the infantry today, with the actual actor keys:** two templates in `infantry.yaml` — `^CapturesOccupiedBuildings` (`:927-938`: `CaptureTypes: building-occupied`, `CaptureDelay: 1000`, `ValidRelationships: Enemy`) and `^CapturesNeutralBuildings` (`:939-948`: `CaptureTypes: building-neutral`, `CaptureDelay: 20`, `ConsumedByCapture: true`). **Inheritors of the OCCUPIED template — these are the actors to change:** `^E1`/`E1` Conscript (`:1127`/`:1177`), `^E3`/`E3` **Rifleman** (`:1194`/`:1297`, veteran `E3R1` `:1299`), `^AR`/`AR` **Automatic Rifleman** (`:1313`/`:1373`), `^TL`/`TL` Team Leader (`:1450`/`:1532`), `^PILOT`/`PILOT` (`:2407`/`:2456`). **The technician `^TECN`/`TECN` (`:2258`/`:2297`) is the ONLY inheritor of the neutral template** (`:2262`) — so "ownership requires your own technician" is already the shipped rule for neutral buildings, and this item extends it to enemy-held ones._
_**The soldier IS consumed today, and by DEFAULT rather than by declaration.** `^CapturesOccupiedBuildings` does not set `ConsumedByCapture`, and `Captures.cs:41` defaults it to **`true`**; disposal at `CaptureActor.cs:136`. Repo-wide the only `ConsumedByCapture` line in `mods/` is `infantry.yaml:945`. **Note there is already a non-consumed branch** at `CaptureActor.cs:65-71`, which captures **without entering** and does not dispose — that is not the requested behaviour (the user wants entry, then ejection) but it proves the disposal is separable._
_**THE MISSING PRIMITIVE IS ALREADY DOCUMENTED — cross-reference it rather than re-deriving it.** `WORKSPACE/DISCOVERIES.md:3084-3086` and `DOCS/reference/supply-route.md:72` already state exactly this problem, for the Supply Route: **vanilla `Captures`/`Capturable` transfers to the CAPTURER, not to Neutral** (`CaptureActor.cs:122-126`, `ChangeOwnerInPlaceSync(self.Owner)`), so a "goes Neutral instead" design **cannot be built from vanilla capture traits alone — it needs a custom on-capture hook.** This item and the parked Supply-Route-capture item (17) want the **same missing primitive**; whoever builds it should check whether one hook serves both._
_**What exists to build on, verified:** `Infiltrates` (`engine/OpenRA.Mods.Cnc/Traits/Infiltration/Infiltrates.cs:22-52`) has `EnterBehaviour` defaulting to `Dispose`, **but the enum includes `Exit` (`Enter.cs:20`) and this mod already uses it** — `infantry.yaml:1955-1959` `Infiltrates@RestoreTechHusk` with `EnterBehaviour: Exit`, and `RepairsBridges: EnterBehaviour: Exit` at `:1953`. **So "enter, do a thing, walk back out un-consumed" is already a working shape in this mod.** What does NOT exist is an `InfiltrateForOwnerChange`-style effect trait — the shipped `InfiltrateFor*` set is Cash / Decoration / Exploration / PowerOutage / SupportPower / SupportPowerReset / Transform. `TemporaryOwnerManager` (`:26-45`) is the wrong tool: it reverts to the ORIGINAL owner after a duration and is driven by the `ChangeOwner` warhead, not by capture._
_**Cross-reference, do not contradict:** item **35** ("use transports for the opening derrick rush") is about ferrying **technicians** to derricks and already notes that captures consume the technician. This item does not change that path — it changes what a **rifleman** does to an **enemy-held** structure. Both can be true at once._
_**Could not determine from the code, and left OPEN rather than guessed:** (1) whether bots reach the soldier-capture path at all (bot capture keys on `CaptureCoordinatorBotModule`, which was not traced); (2) whether the capture the user observed went through the **occupied** path (`CaptureDelay: 1000` ≈ 60s) or a neutral one — the delay is long enough that the observation is worth re-confirming before the fix is scoped._

### 60. A dedicated Evacuate button in the command bar **[SHIPPED — verified 2026-08-19, this entry was stale]**

> **DONE. `Button@EVACUATE` exists at `ingame-player.yaml:333`** — verified independently by a dispatched worker and by the manager before merging `b2fc8ee2`. The entry below describes work that has already been done; it is kept for its analysis, not as a task. **Item 61 is likewise shipped: the command bar carries 30 `Key:` bindings.**
>
> **What is genuinely left, and it is a different item:** the `TAKE_COVER` button renders and highlights and does nothing. It is inert at THREE levels, so the obvious fix accomplishes nothing — no `TakeCover` hotkey definition exists in any of the nine loaded files (`Key: TakeCover` would resolve to `Hotkey.Invalid`, `HotkeyManager.cs:48-58`); `CommandBarLogic.cs:262-268` never assigns `OnClick`, and since key routing IS wired by default (`ButtonWidget.cs:96-97`) a working binding would route a press into the default no-op at `:73`; and no ww3mod actor carries a `TakeCover` trait to receive an order. Its removal is fully costed in `WORKSPACE/bugs/discovered.md` (block `233-251` plus 12 numeric edits with from→to values) and deferred only because the reflow needs a launch to confirm nothing lands off-grid.
>
> _Also on the record from that pass: the 2026-08-16 audit's "free" suggestion to move `@EVACUATE` onto a 24×24 `command-icons` region **cannot be done** — no evacuate region exists in that collection, so it is blocked on art rather than free._

### 60 (original entry, retained for its analysis). A dedicated Evacuate button in the command bar **[do together with 61 — same subsystem, chrome/UI only]**
**Perceived:** Evacuate is a visible button you can find, next to the commands you already use, instead of an Alt-click on a stance nobody would guess.
_User's ask: there was previously an Alt-click on one of the stances; they are unsure it still works and consider it a bad solution regardless — hidden and hard to find. Wanted: a real button next to "Auto enter" and the other commands._
_**VERIFIED 2026-08-13 — the Alt-click path DOES still work, so this is a discoverability change, not a repair.** `ResupplyBehaviorSelectorLogic.cs:57` — `else if (mods.HasModifier(Modifiers.Alt)) DoNow(behavior)`, with the `Evacuate` case at `:129-137`, bound to `RESUPPLY_EVACUATE` (`:40-42`). The tooltip already documents it (`ingame-player.yaml:588`, "Alt+Click: Evacuate NOW"). **The user's instinct that it is undiscoverable is right; their doubt that it works is not.**_
_**This is a small item and the cost is known.** The order string is exactly **`"Evacuate"`**, resolved at `DeliversCash.cs:82` (`order.OrderString == "Evacuate" && info.Type == "Rotation"` → `GoDonateCash` → `RotateToEdge`, `:109`). Carriers are the base templates: `infantry.yaml:155`, `vehicles.yaml:102`, `aircraft.yaml:124,166`. **There is no `OrderTargeter` and none is needed** — `DeliversCash.Orders` yields only `DeliverCash` (`:60`), so Evacuate is a keyboard-style **unqueued** order exactly like `Stop` and `Scatter`. Working precedent for issuing it: `HelicopterSquadBotModule.cs:1747`, `PoiOffensiveBotModule.cs:2567,2659`._
_**Next concrete step, with the layout fact that makes it cheap:** add one button to `Container@COMMAND_BAR` (`ingame-player.yaml:79`, X=14, Width=454). **X=272 is an empty slot, and the last button ends at 426 inside a 454-wide container — so there is room for one more 34px button without resizing anything.** In `CommandBarLogic.cs` the per-button pattern is uniform (`GetOrNull<ButtonWidget>` → `WidgetUtils.BindButtonIcon` → `IsDisabled` → `IsHighlighted` → `OnClick` → `OnKeyPress`), disabled flags all computed in one place (`UpdateStateIfNecessary()`, `:404-433`), and the simplest order-issuing form is `PerformKeyboardOrderOnSelection(a => new Order("Evacuate", a, false))` (pattern at `:435`). **Estimated cost: one bool field, one line in `UpdateStateIfNecessary`, one ~15-line block, one YAML entry.** Keep the Alt-click; adding a button does not require removing it._
_**Found while checking this, and it is NOT cosmetic — flag to whoever owns item 42.** `ResupplyBehaviorSelectorLogic.DoNow`'s Evacuate case calls `at.Actor.QueueActivity(false, new RotateToEdge(...))` **directly at `:134` instead of issuing the order** — the exact synced-write shape that was just fixed in three bot modules at `91056894`. **This one is client-local UI code, so it is NOT host-only and is not the same desync**, but it is also **not order-routed, so `GameSave` never records it** — which is the property that made the bot version break saved-game restore. **Unverified at runtime; recorded as suspicious, not proven.** Building this button on the order path (as specced above) fixes it for the new button but leaves the Alt-click on the direct call unless it is converted too._

### 61. Every command needs a hotkey, visible in its tooltip **[do together with 60]**
**Perceived:** you can drive the command bar from the keyboard, and the tooltip tells you which key.
_User's ask: audit the command bar — every command gets a hotkey, and the tooltip shows it. Modifier combinations are fine where a command has variants. **The user invites proposals for the missing ones.**_
_**THE TOOLTIP HALF IS ALREADY BUILT — this is wiring, not widget work.** `ButtonTooltipLogic.cs:26-42` reads `button.Key.GetValue()` and, if valid, un-hides `Label@HOTKEY` rendering `$"({key.DisplayString()})"`. Both templates carry that label: `BUTTON_TOOLTIP` (`engine/mods/common/chrome/tooltips.yaml:15`, the `ButtonWidget` default per `ButtonWidget.cs:22`) and `BUTTON_WITH_DESC_HIGHLIGHT_TOOLTIP` (`:38`). **Setting `Key:` in YAML is sufficient. Template to copy: `ATTACK_MOVE` at `ingame-player.yaml:94`.**_
_**Fluent is NOT in the way — the implementer edits YAML, not `.ftl`.** `ButtonWidget.cs:88-89` passes tooltip text through `FluentProvider.GetMessage`, and `FluentProvider.cs:64` falls back to returning the key verbatim when no bundle matches. All command-bar strings are literal English in `ingame-player.yaml`; neither `mods/ww3mod/languages/en.ftl` nor `engine/mods/common/fluent/common.ftl` contains them. (`engine/mods/common/fluent/hotkeys.ftl:100-108` holds settings-screen hotkey *descriptions* only and is **stale** — no entries for patrol / autoenter / engagement / cohesion / resupply, which fall back to `game.yaml`'s `Description:`.)_
_**THE AUDIT, done. Command bar is `Container@COMMAND_BAR` (`ingame-player.yaml:79`); bindings live in `engine/mods/common/hotkeys/game.yaml` (`mods/ww3mod/hotkeys.yaml` adds none of these).**_

| Button | line | `Key:` | Bound to |
|---|---|---|---|
| `ATTACK_MOVE` | :87 | AttackMove | **A** (`game.yaml:72`) |
| `FORCE_MOVE` | :106 | — | **NONE** |
| `FORCE_ATTACK` | :125 | — | **NONE** |
| `GUARD` | :144 | Guard | **D** (`:97`) |
| `DEPLOY` | :163 | Deploy | **F** (`:92`) |
| `RESUPPLY` | :183 | Resupply | **R** (`:87`) |
| `PATROL` | :203 | Patrol | **P** (`:162`) |
| `TAKE_COVER` | :223 | — | **NONE** |
| `AUTO_ENTER` | :242 | AutoEnter | **L** (`:167`) |
| `SCATTER` | :262 | Scatter | **S** (`:82`) |
| `STOP` | :282 | Stop | **S** ⚠ (`:77`) |
| `QUEUE_ORDERS` | :302 | — | **NONE** |

_Four separate containers sit to the right and are **not** part of `COMMAND_BAR`, all fully bound: `STANCE_BAR` (`:321`, Alt+A/G/F), `ENGAGEMENT_STANCE_BAR` (`:390`, Ctrl+Alt+A/D/F), `COHESION_BAR` (`:459`, Ctrl+Alt+1/2/3), `RESUPPLY_BEHAVIOR_BAR` (`:528`, Ctrl+Alt+4/5/6). They are the precedent for "modifier combinations are fine"._
_**Unbound, and this is the list the user is choosing bindings for: `FORCE_MOVE`, `FORCE_ATTACK`, `TAKE_COVER`, `QUEUE_ORDERS`.** Note before proposing: `FORCE_MOVE`, `FORCE_ATTACK` and `QUEUE_ORDERS` are **modifier-hold modes** (Alt / Ctrl / Shift) with no discrete key today — giving them a press-key binding is a small design decision, not just a YAML line._
_**TWO DEFECTS FOUND BY THE AUDIT, both needing a decision before any binding is added:**_
_- **`Stop` and `Scatter` are BOTH bound to `S`** (`game.yaml:77` and `:82`). Both live in the same widget subtree and `SCATTER` appears earlier in the children list (`:262` vs `:282`), so Scatter should win the keypress — **strong inference, NOT verified at runtime.** Worth one live check, and it means one of the two currently has no working hotkey._
_- **`TAKE_COVER` is a dead button.** `CommandBarLogic.cs:242-248` wires **only `IsDisabled`** — no `OnClick`, no `OnKeyPress`, and no other reference anywhere in the codebase. **The audit must decide whether to implement it or remove it**; binding a hotkey to it as-is would ship a key that does nothing._

---

### 47. ~~GPLv3 licence text is missing from the Linux and macOS packages~~ — **RETIRED 2026-08-12: FALSE ALARM, the licence already ships**
**Perceived:** nothing, and nothing was needed. **No work is due on this item; do not re-open it.**
_**Verified 2026-08-12** (`25396a33`): `COPYING` is installed by the **shared** helper `install_data` at `engine/packaging/functions.sh:111`, which loops `VERSION AUTHORS COPYING IP2LOCATION…`. Both scripts call it — Linux at `buildpackage.sh:69` → `${APPDIR}/usr/lib/openra/COPYING`, then `appimagetool` packs the whole AppDir; macOS at `buildpackage.sh:142` → `…app/Contents/Resources/COPYING`, then `hdiutil create -srcfolder` packs the bundle. **Adding explicit copies would have shipped a duplicate licence file, not closed a gap.**_
_**Why the item existed, and the lesson worth more than the item:** its evidence was `grep -rln COPYING packaging/` — scoped to `packaging/`, which **excludes `engine/packaging/`**, where the install actually happens. The clincher nobody checked: Windows needs an explicit `File` directive only because NSIS requires one per file, and that same Windows script's portable zip (`buildpackage.sh:126`, `zip -r ./*`) picks up `COPYING` with no mention at all — the identical whole-directory mechanism Linux and macOS use. **A grep's scope is part of its claim.** Two separate false findings on 2026-08-12 traced to exactly this: this one, and a manager's "the curation pass has never run" (it had — 125 times; the grep assumed the wrong tag form)._
_Residual, if anyone ever wants it closed empirically rather than by argument: run the Linux package and `unsquashfs -l` the AppImage. The reasoning rests on `set -o errexit` + `install -m644` failing loudly on a missing source, which `engine/COPYING` (35147 bytes, byte-identical to root `COPYING`) satisfies._
_**`closeout/art-6cde8456.md:36` still asserts the gap as real** and was deliberately left unedited — it is a historical close-out record, not a live queue._
_Related and already verified, so do not re-check: **packaging has no assembly allowlist** — `engine/packaging/functions.sh:66` does `for LIB in "${SRC_PATH}/bin/"*.dll` and `windows/buildpackage.nsi:97` does `File "${SRCDIR}\*.dll"`. Both are wildcards, so `NVorbis.dll` ships and Ogg will not silently fail for players while working in dev._

### 50. ~~FX audit items 10 and 12 — two one-line edits, held on a verdict that was skipped~~ — **RETIRED 2026-08-19: DECLINED BY THE USER. Do not re-propose.**

> **The silence WAS a rejection.** Asked directly on 2026-08-19 whether items 10 and 12 had been missed or declined during the FX verdict pass, the user chose *"Declined — retire both from the queue"*. The manager's standing read — that skipping them without comment was an oversight, because item 17 had been declined explicitly and in the user's own words — **was wrong**. Both are struck.
>
> **This lands the same way as item 17: the explosion ladder and the cook-off effects are as the user wants them, and neither is to be re-proposed without new art or a fresh instruction.** The entry below is retained only so nobody re-derives the proposal from scratch and re-files it as new.
>
> _Holding them rather than assuming in either direction was still correct — the cost of asking was one question, and the assumption would have shipped two unwanted changes to how the game looks and sounds._

### 50 (original entry, retained so it is not re-derived). FX audit items 10 and 12 — two one-line edits, held on a verdict that was skipped **[RETIRED — see above]**

_**RECONCILED 2026-08-13 against `main @ dc899995`: `git log 4d3c8f90..HEAD -- mods/ww3mod/rules/weapons/` returns ZERO commits.** Both edits are still exactly as filed and both are still held on the same skipped verdict. No action._

**Perceived:** a burning vehicle stops going *tink*, and a thrown grenade stops out-exploding a 40mm GL round by four rungs.
_Source: [`closeout/6361c2be.md`](../../closeout/6361c2be.md) §1. Both re-checked against `35876332` and **untouched upstream**. Full context in the committed audit `WORKSPACE/fx-audit.md`, "Changes I would make, in order"._
_- **Item 10:** `VehicleCookoffTiny Warhead@Effect` is still `Explosions: piff` / `ImpactSounds: gun27.aud` (`mods/ww3mod/rules/weapons/weapons-explosions.yaml:50-53`) — a small-arms dust puff and a pistol report for a vehicle cook-off. **Next step:** swap to `Explosions: napalm_small` / `ImpactSounds: firebl3.aud`._
_- **Item 12:** `HandGrenade Warhead@Effect` is still `Explosions: explosion_medium` (`mods/ww3mod/rules/weapons/weapons-ballistics.yaml`, `HandGrenade:` block). **Next step:** demote to `Explosions: explosion_small`._
_**Why they are gated and not just done:** the user's verdict pass worked down the audit list and ruled on every other item — 6/7/8/9/11/13/14 to do, 16 resolved as *remove* the contrails, 18 conditionally, 17 declined — but **skipped 10 and 12 without comment**. That manager read it as an oversight rather than a rejection and **held both rather than assuming in either direction**. Parked in [`AWAITING-USER.md`](../../AWAITING-USER.md)._
_**Audit item 17 (un-flatten the middle of the explosion ladder) was explicitly DECLINED by the user** — *"we need more effects, but I don't think it is worth it now"*. Recorded in `DISCOVERIES.md`; **do not re-propose without new art.**_

### 51. Harden `test-supply-safe-front-keeps-cargo` — ~~it passes for the right reason but does not assert it~~ **IT NOW FAILS, AND FOR WHAT LOOKS LIKE A REAL REASON** **[PARTIALLY SHIPPED — hardening landed `be181baf`; the scenario clause is UNEXECUTED]**

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — PARTIALLY SHIPPED, and the queued next step turned out to be WRONG and was corrected in flight.**
>
> **`wt/supply-oracle` (`be181baf`, *"Assert the safe-front platoon held, at 1 cell rather than the queued 6"*) is an ancestor of `main`.** The drift clause is in, and the measurement is now **shared** with the sibling scenario via `TestHarness.DriftTracker` (`mods/ww3mod/scripts/test-helpers.lua:117`) rather than duplicated.
> **The "next concrete step" recorded below said allowance 6; 6 does not transfer and would have been a hole.** It is licensed solely by walking to a crate dropped short, and this scenario's clause 2 fails any run in which a crate ever existed, so that walk can never legitimately occur here. **1 is derivable rather than conventional:** TRUK's aura is `5c0` compared as distance *squared*, so a truck at the centre man's aura edge (39,16) covers only him (25 ≤ 25; flankers 26 and 29) and **one cell west puts every man inside** (17 and 20). A crate having existed still relaxes it to 6 deliberately, so that when clause 2 fails the verdict stays about the crate instead of stacking a bogus front-collapse on a real signal.
>
> **WHAT REMAINS — CANNOT SETTLE WITHOUT A LAUNCH.** The scenario clause is **UNEXECUTED**; no launch was ever allocated. What *is* executed is `engine/OpenRA.Test/OpenRA.Mods.Common/SupplyDriftClauseTest.cs` — 7 tests under NUnit against the real Lua file via Eluant, **4 of them RED-verified by sabotaging the mechanism**. So the *measurement* is proven and only the *scenario verdict* is not.
> **The run that would settle it:** one `run-test.sh test-supply-safe-front-keeps-cargo`, establishing whether it is still RED for the doctrine reason recorded 2026-08-14 (truck unloads at x=39 on a front where no enemy exists and believed danger is 0 everywhere — the dangerous-front branch firing where the quiet-front branch is doctrine). **It is also the cheapest deterministic instrument pointing at item 56's mode selector, so pair it with a 56 dispatch rather than spending it alone.**

_**STATUS CHANGE 2026-08-14.** This item's title and framing assume the scenario is **green** and therefore a suspect oracle — something that "passes for the right reason but does not assert it", i.e. a test you cannot trust to catch a regression. **It is now RED**, and red for a reason that reads as a genuine doctrine violation rather than a fitted assertion: the truck drives to x=39 and unloads a supplycache on a front where **no enemy actor exists and believed danger is 0 everywhere**, which is the dangerous-front branch firing where the quiet-front branch is doctrine. **Verified pre-existing and NOT caused by the 2026-08-14 economy-gate fix** — it fails identically on both sides of that change at the same seed (`seed 5002`), the only difference anywhere in the two failure notes being one unit's ammo (71 → 70)._
_**What this changes for this item:** the scenario is promoted from **suspect-oracle to useful-signal**. It should no longer be treated as untrustworthy by default, and it is currently the cheapest deterministic instrument pointing at PIPELINE item 56's mode selector (full write-up in [`bugs/discovered.md`](../../bugs/discovered.md) 2026-08-14; cross-referenced from item 56). **The hardening described below is still worth doing** — a test that fails for the right reason today can still stop asserting it tomorrow, which is exactly this item's original point — but it is no longer a prerequisite for trusting the signal._
_**A note on this item's own trap, honoured 2026-08-14:** the "piping into `tail` still returns `tail`'s status" obligation recorded below is live and was hit twice in one session. Both regression batches were therefore read from the printed `Summary`/`AUTOTEST_VERDICT` lines and the per-run `result.json`, never from a pipeline exit code._

_**RECONCILED 2026-08-13 against `main @ dc899995`: `git log 4d3c8f90..HEAD -- tools/autotest/scenarios/test-supply-safe-front-keeps-cargo/` returns ZERO commits.** The scenario still has no drift clause and the next concrete step is unchanged._
_**New caveat on this item's own evidence, from `d0a23b0d`.** This item's claim that the scenario "passes for the right reason" was **verified from the log**, and the harness that wrote those logs was destroying and misreporting verdicts — four incidents in two days across three mechanisms, all fixed at `4116886b`/`d0a23b0d`: a shared `~/.ww3mod-tests/result.json` that one run's `rm -f` could delete out from under another, an exit status lost to a pipe, and a crash indistinguishable from a fail. **This does not invalidate the reading** — a peak-drift value read out of a run log is not the kind of claim those defects corrupted — **but any verdict cited anywhere in this queue that predates `d0a23b0d` and rests on an exit code rather than on logged measurements should be re-confirmed before it is built on.** The harness now emits an `AUTOTEST_VERDICT` banner as its literal last line from an `EXIT` trap, with named outcomes (CRASH / NO-RESULT / TIMEOUT-FAIL / BAD-VERDICT / INTERRUPTED / HARNESS-ERROR) and a per-run result directory. **Honest boundary the harness fix does NOT close: piping into `tail` still returns `tail`'s status** — that is a documented caller obligation, not a solved problem._

**Perceived:** nothing. This is the measuring instrument, not the game.
_Source: [`closeout/bdedd544.md`](../../closeout/bdedd544.md) §1b. Confirmed by grep at `35876332`: the scenario has **no drift clause**. It passes for the right reason — verified from the log, the truck reached x=39 against a platoon at x=44, served from its aura, kept its cargo, platoon held — but **it does not assert the platoon held position**, so the front-collapse failure that fooled the danger scenario would slip straight through it._
_**Next concrete step:** copy the peak-drift clause from `test-supply-under-danger`, allowance **6 cells** (5-cell crate walk + 1 tolerance), tracked as the **peak over the run** rather than the value at verdict._
_**DONE 2026-08-19 on `wt/supply-oracle` — and the 6-cell half of that step was WRONG; corrected to 1.** The clause is in, and the measurement is now SHARED with the sibling (`TestHarness.DriftTracker`, `mods/ww3mod/scripts/test-helpers.lua`) rather than duplicated. But 6 does not transfer: it is licensed solely by walking to a crate dropped short, and this scenario's clause 2 **fails any run in which a crate ever existed**, so that walk can never legitimately occur here. The sibling records what unconditional 6 does with no crate on the ground — at `9861bcf4` all five men walked out to meet the TRUCK and the run **PASSED at drift 5 with `crate=NONE placed`**. What transfers is its `HOLD_DRIFT = 1`, and 1 is derivable rather than conventional: TRUK's aura is `5c0` compared as distance SQUARED, so a truck at the centre man's aura edge (39,16) covers only him (25 ≤ 25, flankers 26 and 29) and **one cell west puts every man inside** (17 and 20). A crate having existed still relaxes it to 6, deliberately, so that when clause 2 fails the verdict stays about the crate instead of stacking a bogus front-collapse on a real signal._
_**The scenario clause is UNEXECUTED** (no launches allocated). What IS executed: the drift measurement itself, under NUnit against the real Lua file via Eluant — `SupplyDriftClauseTest`, 7 tests, 4 of them RED-verified by sabotaging the mechanism. Derivation in `DISCOVERIES.md` 2026-08-19; one incidental defect spun off into `bugs/discovered.md` 2026-08-19 (the sibling reads its allowance LIVE, so a consumed crate can retro-fail the platoon — false-FAIL direction only)._
_Corroborating rather than duplicating: the second machine independently hit this same class at `74e220f5` — *"a matched pair green on both sides still did not protect a third doctrine."_
_**Solved upstream, no action:** the three stance scenarios, fixed at `a4d85b0c` (the scenarios were disabling the trait they test). That session's reading — "units never leave the spawn cell, so the cover-seek order is never issued" — was right about the symptom and **wrong about the cause**._

### 52. Lint noise — 402 of 496 errors are one defect multiplied by the per-map re-run **[DONE at `4d3c8f90` — but it spun off THREE new open defects that this queue did not carry; they are now item 62]**

_**RECONCILED 2026-08-13 against `main @ dc899995`. The "IN FLIGHT, no commits yet" tag is stale by one merge: `wt/lint-sellable` committed and merged as `4d3c8f90` — "the YAML lint is usable as a merge gate again". The branch and worktree are gone.**_
_**The fix landed in exactly the shape this item prescribed**, at `mods/ww3mod/rules/ingame/structures-defenses.yaml`: GTWR (`:76`, `-CaptureManager:` at `:81`, `Sellable` / `RequiresCondition: !build-incomplete && !being-demolished` at `:86-87`), PBOX (`:167`, `:171`, `:183-184`), HBOX (`:252`, `:270-271`) — each stripping `-CaptureManager:` and overriding `Sellable` to drop the `!being-captured` term. The source term survives only on the shared template (`structures.yaml:116`, granted at `:151`). Provenance: `2fedd71b`, merged `4d3c8f90`. **`4d19a8e4` separately removed the 15 `EjectionSurvivalRate` nodes; `git grep -c EjectionSurvivalRate -- mods/` is now zero, so that class is closed with no residue.**_
_**The count: `4d3c8f90` claims 583 → 100 residual, and `4d19a8e4` should take that to ~85 — but NO file in the repo asserts the current number.** `WORKSPACE/bugs/discovered.md:644` still reads "402 of the mod's 496" with no FIXED annotation and is the stalest statement of it; that line, not this item, is what will mislead the next reader. **The load-bearing finding stands and is unaffected: `--check-yaml` re-runs the whole rules lint once per map and never deduplicates, so any branch adding one autotest scenario raises the total by exactly 3 with no code involvement. Diff the error LIST, never the count.**_

**Perceived:** nothing in game. It restores a signal that is currently unusable.
_Source: [`closeout/fixes-cfcaa2ca.md`](../../closeout/fixes-cfcaa2ca.md) §4b, also recorded in `WORKSPACE/bugs/discovered.md` @ `35876332`._
_**Fix shape for the underlying defect:** override `Sellable.RequiresCondition` on **GTWR / PBOX / HBOX** to drop the `!being-captured` term. **One line per actor; takes 496 → 94.**_
_**The count itself is not a usable signal, and that is the load-bearing finding.** `--check-yaml` re-runs the entire rules lint **once per map** with custom rules and **never deduplicates**, so 3 errors in the default ruleset become 402 of the mod's 496. **Any branch adding one autotest scenario raises the total by exactly 3 with no code involvement. Diff the error LIST, never the count.**_
_**Companion trap, recorded in the same place:** a scenario folder is registered as a map source from `tools/`, not `mods/` (`mod.yaml:96`), so an isolation experiment that swaps the `mods/` directory to test "YAML or C#?" **cannot** remove a scenario map and will exonerate it regardless — a clean result from an experiment with no power to produce a dirty one. **It caused two misdiagnoses on that branch.**_

### 33. Heli lift enablement — fix the shared `IsIdle` misuse so transport missions fly for the first time — **DONE 2026-08-06** (`3e59fed1`, reconciled into main @ `38a6d5fa`; NUnit 1154/1154)
_**Delivered on `auto/transport-lift` across two adversarial review rounds.** All four ungated sites fixed plus the mandatory `:517` companion (`ShouldBuyTransport`'s idle-transport count), so the buy/evac money pump this item warned about cannot open. Two things came out DIFFERENT from the scope as written, both deliberate:_
_(a) **The airframe and infantry defects are not the same bug and needed different fixes.** For an airframe the test is DEAD, provably: `Actor.Tick` fires `INotifyBecomingIdle` and runs the newly-queued activity inside the SAME tick (`Actor.cs:290-299`), so `CurrentActivity == null` is never observable from a bot module for any actor whose traits queue on idle. Predicate hoisted to `AIUtils.IsUnoccupiedAirframe`. For INFANTRY the test is reachable but unsatisfiable in practice, so the `IsUnoccupied` pattern would have been the wrong fix — replaced with availability-for-tasking (reserve-zone + uncommitted ledger + not-reserved), shared by the demand signal and the loader so they cannot disagree. `IsReadyForMission` deliberately keeps raw `!IsIdle`: its branch body is a single `Resupply` type test, so both arms return the same answer for every other activity._
_(b) **Waking the path exposed three unexercised defects in PRE-EXISTING code**, none of which this item anticipated: the load was uncapped (`.Take(cargo.Info.MaxWeight)`, `tran` = 36 against a dispatch threshold of 4) and stranded passengers pinned `Cargo`'s pickup lock permanently, so the FIRST lift would have bricked that airframe; heli lift and `MountedTransport` were no longer mutually poach-safe on `@stable` (`IsIdle` had silently been doing that job, and neither module's `@stable` twin writes the ledger — reading a ledger nobody writes arbitrates nothing); and the passenger filter was absent, so a lift would have flown the idle TECN capture engineer to the drop zone. All three fixed and pinned._
_**Live-validation item for the user:** `ForwardStaging` now actually executes on `@stable` after being inert since it shipped — bare `Move` to 40% of the SR→top-POI vector, no danger-field consultation on the staging leg, never observed in a game. **Not fixed, out of scope:** `TransportMissionSlots: 0` on `@stable` means lifts still compete with the attack loop for `MaxActiveSquads` — profile tuning, not the `IsIdle` bug._
**Perceived:** transport helicopters actually DO something — they pick up infantry and fly lift missions instead of hovering at the SR until they evacuate. This has never worked for any bot since the module shipped.
_User decision (2026-08-06, answering the posted scope question): **fix the shared code directly, keep it simple** — "I dont care if stable gets some upgrades as well". Byte-identity waived: @stable behavior change is accepted; old benchmark numbers were already non-comparable since the composition-baseline merge (`2eb79262`). Scope: apply the corrected idleness test (`IsUnoccupied`-style: `IsIdle || (CurrentActivity is FlyIdle && NextActivity == null)`, pattern from `c89d20bb`) at all four ungated sites — `TryLaunchTransportMission` (`HelicopterSquadBotModule.cs:965-976`), `ShouldBuyTransport` (`UnitBuilderBotModule.cs:512`), attack-heli `EvacuateWhenIdle` (`:1353`), `ForwardStaging` (`:623`). **MANDATORY companion in the SAME change** (churn-risk finding, recorded in DISCOVERIES at the `c89d20bb` merge): fix the `:517` idle-transport count too, or the now-live evac becomes a buy/evac money pump once missions launch (tran UnitLimit 2 × TransportMissionSlots 1 × evac at 900 ticks). Full technical detail: DISCOVERIES 2026-08-05 transport entry, STILL-OPEN + churn-risk paragraphs. Pipeline shape: worktree implementer → adversarial review (behavior change, @stable affected — review is not optional) → merge → push._

### 30. Composition intelligence — threat-aware unit buying — **DONE 2026-08-02** (`f05e31b7`, in main @ `1c1469a8`)
**Perceived:** bots buy what the battlefield calls for — attack helicopters appear (rarely, when income allows) against weak enemy AA instead of littlebird-only spam; AT shows up against armor pushes. From user live-play: "does the bot have some way of buying intelligently? … for all units there could be some kind of system to determine what is needed most."
_Both parts landed @ `f05e31b7`: (a) root cause CONFIRMED as the UnitBuilder case-mismatch family — heli pool keys `HELI`/`TRAN` uppercase, only `littlebird` matched; keys lowercased + pools filled (`america.heli` heli 80 / littlebird 40, `russia.heli` mi28 50). Config defect, no unit stats touched — but shared blocks, so `@stable` benchmark control re-baselines. (b) `CompositionNeedMath` (integer-only, zero RNG, 11 NUnit pins): counter-score + AA-gap air-opportunity score + budget-reserve affordability + deterministic argmax; default-OFF fields on AdaptiveProductionBotModule, only `@experimental` enables (weights 100, `AirStrikeUnits: heli, a10` / `mi28, frog`); `@stable` omits all → byte-identical. Effect measurement rides the user-gated benchmark re-baseline._

### 31. Aggressiveness slider + opportunistic advance (user-mandated, folded into Brain design) — **MERGED @ `af8bca1f` (2026-08-05), gates OFF; awaiting the user-gated priced sweep**
**Perceived:** bots exploit granted opportunities — when a sector is undefended and a free path forward exists, they advance and keep pressure up instead of only spreading to POIs. And instead of discrete bot personalities ("Rush bot"), an **Aggressiveness slider**: YAML/programmatic now for test sweeps to find the right baseline, lobby-facing later. Other knobs (risk tolerance, capture-vs-combat priority) follow the same slider pattern.
_User 2026-08-02: "we will need these things eventually … make sure we have a plan." Design scope in `plans/260802_squad_brain_design.md` §2.6/§2.7._
_**Merged to main @ `af8bca1f` (2026-08-05) after three adversarial review rounds (round 1: merge-safe at defaults CONFIRMED, 4 FIX + 3 NIT, all addressed); NUnit 1136/1136 on merged main.** (a) **Slider infrastructure**: `Aggressiveness` is a sweepable `Info` field on `PoiOffensiveBotModuleInfo`, read ONLY through pure `PoiOffenseMath.ShiftByKnob`, which now also `ClampKnob`s to 0..100 so an out-of-range sweep point degenerates to an extreme instead of past it. Two consumers, each with its own base/slope pair. (b) **Opportunistic advance**: new pure `OpportunisticAdvanceMath` (§2.6's four-condition grant test + extend-while-clear descent of the frontier BFS) consumed by `PoiOffensiveBotModule.StageFreePool` — the idle reserve splits, a knob-sized screen walks into granted ground, the rest keeps mustering. The walk halts on our side of the frontline contour and every spread slot is grant-tested before a unit is ordered to it; the abort is structural — the anchor is re-derived every eval under a one-way adopt rule (giving ground is never damped), with `AdvanceHysteresisCells: 0` because a scalar map-cell threshold against a sector-quantised anchor either damps nothing or permanently refuses one-sector re-deepening. Chaining is within a walk, not a cross-eval ratchet: each eval re-seeds from the staging anchor, so the screen holds a bounded offset ahead of the muster point. **`OpportunisticAdvanceEnabled: false` in `@experimental` too** — enablement is the user-gated priced sweep, not this lane. No unit stats touched; `@stable` omits every new field ⇒ byte-identical. Sweep spec + grant parked in [`AWAITING-USER.md`](../../AWAITING-USER.md): `Aggressiveness ∈ {20,35,50,65,80}` moves all three dials — depth `{1,2,3,4,5}`, ceiling `{8,14,20,26,32}`, force `{3,4,5,6,7}` — after review FIX 3 widened the depth/force slopes to 7 (integer truncation had left the first cut's grid flat on two of three dials)._

### 20. Recon — do trees conceal? — **DONE 2026-07-28**, findings: [`recon/260728-trees-concealment.md`](../../recon/260728-trees-concealment.md)
**Answer: YES, mechanically, but WEAKLY.** Trees (destructible neutral actors) feed a density field → precomputed shadow layer → subtracted from projected vision strength before `Detectable` reads it. Magnitude is the problem: ~1 strength point per fully-dense tree cell on the sightline; a stock infantryman (Vision 3) needs ~7 such cells to every viewer to vanish. Deep forest (stacked density) works; thin treelines barely register. The Stage-3 `Vision: 9` override was a range trick, not a trees trick. **Case-01 is buildable**, but likely needs concealment strengthening (7 seams mapped in the recon doc). Dormant ready-to-wire engine seams found: `TerrainModifiesDamage` (terrain cover damage reduction, zero users), `BlocksSight` (hard LOS block, zero users/callers). Open question ANSWERED 2026-08-02: **BAKED** — tree death never updates density/shadow layers (the `Building.RemovedFromWorld` mutators are dead code, disabled 260503 for lag); treelines are permanent cover. Evidence + incremental-update feasibility: [`recon/260802-shadowlayer-tree-death.md`](../../recon/260802-shadowlayer-tree-death.md). **Items 21+22 ungated.** Companion movement recon: [`recon/260728-movement-locomotion.md`](../../recon/260728-movement-locomotion.md) (engine-modernization study, pipeline additions pending user discussion)._

### 25. Benchmark re-baseline — restore the measurement instrument — **DONE 2026-07-29** (`5dc14934`); follow-on item-24 A/B **DONE** (`db1cff01`)
**Perceived:** nothing directly, but every future "did the bot get better?" claim becomes trustworthy again.
_Re-baseline landed @ `5dc14934` (60 matches / 0 crashes; result card `ai-bench/runs/260728_rebaseline_result.md`; cal re-zero: S1 spawn-bias mirror-cancels, S2 noise band ±$2000). Rode-along gate (b) ambush pricing: **lean-OFF but inconclusive** (rungs disagree at noise scale) — user decision, ai.yaml unchanged. Headline finding: @experimental underperforms @stable, attributed to a capture-contest loss (recon `260729-exp-deficit-attribution.md` @ `a885a141`; lever design @ `d80b750b`; 4.D instrumentation spec @ `c4ba0eee` — awaiting user direction). Unblocked item-24 gate-enablement A/B, now also **DONE @ `db1cff01`**: fresh 40-match S1+S2 paired run, Arm B (repoint gates ON) byte-identical to Arm A → **KEEP OFF recommendation**; gates remain committed-ON at HEAD pending the user's call (result card `ai-bench/runs/260729_item24_ab_result.md`)._

### 8. Ambush behavior — IMPLEMENTATION **[ALL STAGES SHIPPED 2026-07-25 — only gate (b) benchmark pricing remains, user-gated]**
**Perceived:** hidden Ambush units that hold fire until spotted or until springing the trap at the best moment; units *feel alive*, reacting to being seen/unseen. Human-settable stance first, **default off** so nothing changes for players who don't opt in.
_Stages 1+2 merged @ `3ddd0b40`, Stage 3 (stationary state machine) merged @ `d7549f83` — see SHIPPED. **Gate (a) CLEARED 2026-07-25**: granted RED+GREEN batch all-green (6/6 — convoy/enemy-stops/fast-convoy, RED timeout-correct + GREEN pass each; seeds in `15922a38`). The batch caught a real Stage-3 defect: `ScanForTarget`'s cooldown-Invalid was treated as target-lost, wiping the cadence sample counters every off-scan tick so score triggers could never fire — fixed @ `15922a38` (gated-path-only, review MERGE/0-FIX/4-OBS). **Stage 4 SHIPPED 2026-07-25** — bot lane-ambush consumer merged @ `2b6c6166`, see SHIPPED. Remaining: **gate (b)** @experimental benchmark pricing before any default-on (user goahead — multi-test). Carried OBS for the pricing/default-on decision: s3 detection-path spring **OBS-D CLOSED 2026-07-29** — now covered by `test-ambush-detection` (commit `64b39f50`; RED timeout seed 142960984 / GREEN pass seed 753014554, rebuilt @ `6bc11710`); s4 review OBS-3 anchor-set oscillation on FFA/2v2 close-scored SRs; s4 OBS-4 sprung unit re-tasked within ≤100-tick window (bounded)._
_UNGATED 2026-07-25 — forks resolved on the user's delegation ("do you think it will work? If so, queue it up"): **A** prone = cosmetic only on infantry ambushers (reads as ambush + keeps its damage modifier; no concealment claim — real prone-concealment parked for the separate stealth-mechanics discussion); **B** halt-before-contact on attack-move + auto-move only, plain Move always obeyed; **C** spring at peak density by default, rear-arc shots only when geometry gives them free (L-shape), never by waiting past peak (AT-suppression trap); **D** human-settable + bot behind the same default-off gate from day one. Staged plan: `plans/260722_ambush_undetected_design.md` §6 — Stage 1 executor `!stance-ambush`/`!stance-holdfire` opt-out (near-free bug fix) → 2 halt-before-contact → 3 stationary state machine → 4 bot lane-ambush consumer (benchmark-gated). @stable/control bots stay byte-identical throughout._


### R10. Cargo eject rally points were set client-locally and never ordered — **DONE, verified 2026-08-19**

**~~R10. A NEW multiplayer desync: cargo eject rally points are set client-locally and never ordered.~~** *(source: `audit/260816-systems-completeness.md`; **independently re-verified by the manager**)* — **DONE, verified 2026-08-19 against `main @ 08b255f7`.** Fixed at `409b0fd2` (merged `c9f6a6c0`, 2026-08-16) — the prescribed fix shape below was followed exactly: the rally point now travels as a replicated `Order`. `EjectRallyOrderGenerator.cs` was subsequently **deleted** at `7b5c692b`; handling lives in `Cargo.cs` (`SetEjectRallyOrderString`/`ClearEjectRallyOrderString` at `:560-561`, resolved at `:406-414`). Confirmed no client-local mutation survives: the only `SetEjectRally`/`ClearEjectRally` callers at HEAD are inside `Cargo.ResolveOrder` and inside simulation activities (`Cargo.cs:1005`, `UnloadCargo.cs:175`), and no widget references `EjectRally` at all. **This item was still ranked FIRST as a live blocker three days after it was fixed** — the stale-blocker shape that wastes a dispatch. Text below kept for the fix-shape reasoning, which remains good guidance.
**Perceived:** two people play, one of them right-clicks a rally point for a passenger, and from that moment the two machines are playing different games. Nobody can attribute it — the reports will read "multiplayer is broken".
**This is a second, entirely separate desync from item 42, and it was not on any list.** `EjectRallyOrderGenerator.cs:62` calls `cargo.SetEjectRally(passengerActorId, target)` **directly and then `yield break`s without yielding an `Order`** — manager-verified: the generator's `OrderInner` sets simulation state and returns no order at all. The value lands in a plain, non-`[Sync]` dictionary (`Cargo.cs:190`), which the simulation later reads at `UnloadCargo.cs:131` and turns into a real `Move` at `:157-160`. The passenger walks on the ordering client and stands still everywhere else. **Replays never fire it**, which is why no test caught it.
**FIX SHAPE IS LOAD-BEARING — do not get this wrong:** the fix is to **issue a proper `Order`** so the intent travels to every client. It is **NOT** to add `[Sync]` to the dictionary — that would hash a field that is legitimately absent on other clients and convert a silent divergence into a loud one without fixing the cause. (This is exactly the trap recorded in the user's standing feedback: prove a field is never client-local before adding `[Sync]`.)
**Size:** ~30 lines. **Ranked first because it is the only finding that damages other players rather than the one who triggers it, it is silent and unattributable, and it is cheap.**


### R13. Garrison suppression is silently binary — **DONE, premise corrected 2026-08-17**

**~~R13. Garrison suppression is silently binary~~** *(systems)* — **DONE, premise corrected 2026-08-17 (`wt/garrison-suppression`)**
The dead field was real; the diagnosis built on it was not. **Do not implement a duck-tier fire penalty — it double-applies.** `AttackGarrisoned` fires the soldier's *own* `Armament`, which reads burst / burst-wait / inaccuracy modifiers off the soldier (`Armament.cs:253-258`), so the ten-tier `^SuppressionEffects` ladder already degrades garrison fire, from suppression 1 upward rather than 30. What was actually missing was the *readout* — no suppression row in the building's pip grid, none in the panel, and the soldier's own pips need him selected while he is a 40%-alpha ghost on the building's cell. Fixed presentation-only. `IsDucking` and its sole input `SuppressionDuckThreshold` deleted, with a `// PITFALL:` left at the site.

---

_Archived 2026-09-02 by the pipeline triage pass. Both entries below were tagged `[SHIPPED — CLOSE]` in `PIPELINE.md` but had never been moved, so they kept reading as live queue items. Each was re-verified against the file before archiving; the verification is recorded in each entry._

### 57. ~~Bot build composition — one item, three symptoms, same subsystem~~ **[ALL THREE SHIPPED — CLOSE]**

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — ALL THREE SUB-ITEMS SHIPPED. CLOSE. And the flag guessed the wrong survivor.**
>
> **(a) DONE.** `SupplyTruckFloor: 3` + `SupplyTruckFloorPer: 10` (`ai-america.yaml:378-379`, `ai-russia.yaml:204-205`) — the denominatored floor is the structural fix (a) argued for. **And it satisfies the user's actual ask by construction, which matters because (a) below proposes a different fix (`SupplyTruckFloor: 0`)**: `EffectiveFloor = min(cap, supported/N)`, so at t=0 with zero supported units the floor is **0**. No opening truck buy — *without* deleting the design decision the floor exists to protect.
>
> **(b) DONE.** `UnitFloors: medi.america: 2` (`:148-150`) + `UnitFloorPer: medi.america: 10` (`:209-210`). The unmeasured argmax hypothesis below never had to be settled: the symptom **inverted** to medics-bought-first (a flat floor is maximally unmet at t=0, because at t=0 every census is zero) and was then fixed by the same denominator. The YAML comment at `:151-175` carries the derivation, including why the ratio is **10 and not the user's own "20"** — `min(cap, supported/N)` is ZERO on every cycle where `supported < N`, so a denominator above the cliff makes the floor **inert**, not conservative.
>
> **(c) DONE — the flag called this the likely survivor; it is the most clearly finished of the three.** **`aa.america: 2` is in `UnitFloors`** (`:150`), and `aa.*` is in `UnitFloorSupportedTypes` on both factions (`:219`, `ai-russia.yaml:141`). The user's ask — *"one or two AA soldiers present at all times"* — is implemented. The comment at `:214-217` records that `aa` deliberately gets **no** ratio, because its denominator is enemy **air**, not own army size, and it already has the right instruments (`UnitDelays 2000`, plus `ScaleAntiAirToThreat` for the vehicle). The rule stated there is worth keeping: *"a floored type needs a ratio here OR a threat gate OR a delay; with none of the three it is an opening buy by construction."*
>
> **⚠️ THIS ITEM'S DURABLE FINDING IS NOW FALSE. Do not carry either sentence forward.** The text below states *"there is no standing-population floor for ANY unit type except `truk`"* and calls it the durable finding of this item. `UnitFloors` / `UnitFloorPer` / `UnitFloorSupportedTypes` **is** that general mechanism, live on both factions. The companion irony — that (a) and (c) "pull the same lever in opposite directions" — is void too: they now pull different levers, which is exactly why both could be satisfied at once.

**Perceived:** the bot opens with combat units instead of two idle supply trucks, medics appear, and there is always an AA soldier or two on the field.
_All three observed in the same `@experimental` bot-vs-bot match on 2026-08-13. **Kept as one item because they are one subsystem — `UnitBuilderBotModule` procurement — but they have three different mechanisms and one of them is not confirmed.**_
_**(a) Two supply trucks are procured at the very start, before anything has spent ammo. CONFIRMED, and it is DELIBERATE — this is a policy change, not a bug fix.** `SupplyFleetMath.DesiredTrucks` (`SupplyFleetMath.cs:52`) clamps to `[floor, ceiling]`, and **`SupplyTruckFloor: 2`** is set at `ai-america.yaml:132` and `ai-russia.yaml:114`. At t=0 with zero starving customers the honest demand is **0** and the clamp returns **2**. The ammo-need gate that would otherwise stop it is explicitly bypassed: `UnitBuilderBotModule.cs:522-523` reads `!SupplyFleetUnderDesired(name) && !AnyFieldedUnitNeedsResupply()`, and the comment at `:519-521` states the floor "exists precisely to be held while nobody is dry". **`GateResupplyOnAmmoNeed: true` is live but inert against the floor.**_
_**Next concrete step for (a):** the user's ask — no supply truck until at least one unit is below full ammo — is **`SupplyTruckFloor: 0` plus letting the existing `AnyFieldedUnitNeedsResupply()` gate do its job.** That is a two-line YAML change, but **it deletes a deliberate design decision**, so the trade should be stated: the floor exists so the first dry unit does not wait a full procurement cycle. **Both profiles are affected** — the floor is set per-faction in `ai-america.yaml`/`ai-russia.yaml`, not per-profile; check whether `@stable` reads the same keys before assuming this is `@experimental`-only. Cross-reference item **43**._
_**(b) No medics are being procured. NOT CONFIRMED — the leading hypothesis is stated as a hypothesis, deliberately.** The simple explanation is refuted: **the medic IS in the procurement table.** `medi.america: 40` in `UnitsToBuild` (`ai-america.yaml:79`), `medi.america: 9` in `UnitTargetShares` (`:198`), buildable (`infantry-america.yaml:81-84`), cost 100 (`^MEDI`, `infantry.yaml`). **Leading hypothesis, unmeasured:** the pick rule `ForceCompositionMath.SelectDeficit` (`:206-230`) is an **argmax over `target − census` in per-mille**, so a **9‰ slot has a maximum possible deficit of 9** and can only win when *every* larger slot (abrams 190, at 66, aa 28, …) is at or over target. **This was NOT confirmed from a log — there is no match data showing the medic deficit losing the argmax.** Do not treat it as measured._
_**Next concrete step for (b):** get one match's procurement decisions logged before changing any weight. If the argmax hypothesis holds, the fix is structural (a floor, or a separate small-slot lane), not a bigger number — **raising `medi` from 9‰ would work by crowding out something else, which is the shape of fix that has to be traded deliberately rather than nudged.**_
_**(c) AA soldiers present early, gone a few minutes in. PARTLY EXPLAINED, and the explanation names a real structural gap.** The actor is **`aa.america` / `aa.russia`** (`^AA`, cost 300, `infantry.yaml`) — **`strykershorad` / `tunguska` are the VEHICLE AA and are a different thing.** `ScaleAntiAirToThreat` + `AntiAirUnitTypes` (`ai-america.yaml:135-138`) cover **only the vehicle**, and they are a **CAP, not a floor** (`ShouldBuildMoreAntiAir`, `:676-683`), with **`AntiAirBaseline: 0`** — so zero observed enemy air means zero vehicle AA. Infantry AA is ungated but rides the same 28‰ argmax as (b)._
_**THE STRUCTURAL GAP, and it is the durable finding of this item: there is no standing-population floor for ANY unit type except `truk`.** Replacement is *emergent* from the census (a death does re-open a deficit) rather than explicit. **The user's ask — "one or two AA soldiers present at all times" — is asking for a mechanism that does not exist**, and the only existing instance of it is the very `SupplyTruckFloor` that symptom (a) wants removed. **That irony is worth stating in any plan: (a) and (c) pull the same lever in opposite directions.**_
_**CORRECTION (2026-08-14, `wt/composition`) — the "except `truk`" claim was wrong in BOTH directions, and the reason it was wrong is worth more than the claim.** (1) A second floor already existed: **`CaptureCoordinatorBotModule.MaintainTecnFloor`** (`TecnFloor` / `TecnFloorMax` / `ScaleTecnFloorToPois`) maintains the technician population. It is unreachable from `UnitBuilderBotModule`'s vantage because it requests through `IBotRequestUnitProduction` → the single-name `BuildUnit(IBot, string)` overload, which bypasses the `UnitLimits` lottery filter entirely — so a reader auditing procurement from the UnitBuilder side sees no trace of it. It is however a capture-**demand** floor, not a standing-population one: reached only from the `idleCapturers.Length == 0` branch and returning early on `!CaptureTargetExists()` ("we never spend budget on a TECN with nothing to capture"). **So the technician needs nothing from `UnitFloors`, and adding one would have been a second floor on the same population with the winner decided by module ordering.** (2) `UnitFloors` now exists on `UnitBuilderBotModuleInfo` as the general mechanism, default unset._
_**Could not determine, and left open rather than guessed:** whether the observed AA disappearance is a *procurement* failure or the AA infantry being **consumed by transports/garrisons** — `ai.yaml:1373` and `:1406` list `aa.*` as `PassengerTypes`, and **garrisoned units leave `world.Actors` entirely** (`UnitBuilderBotModule.cs:1047-1053`), so they would read as dead to the census while still alive on the map. **That distinction must be settled before anything is built**, because the two causes need opposite fixes._
_**STILL OPEN after 2026-08-14 (`wt/composition`), but the picture sharpened and one of my own earlier statements here was WRONG.** A new unconditional `[composition] census` line splits every type into `inWorld+inCargo`. An earlier note on this branch claimed `inCargo` was 0 everywhere because "no transports were active" — **that was an artefact of reading only end-of-match snapshots.** A third instrumented match shows `medi.america=0+2` and `medi.russia=0+2` mid-match: **both medics loaded into a transport at once, invisible to `world.Actors`.** So transports ARE lifting infantry on this map, and the cargo credit in `OwnedOrPending` is load-bearing rather than defensive — without it the floor would have read zero medics and bought two more._
_**Why the AA half is still open, and it is now a different reason.** AA cannot be observed being consumed because it is never bought in the first place: `strykershorad` NEVER at any attrition rate, and `aa.*` was never affordable in these matches. So consumption-vs-procurement cannot be separated on AA until AA actually reaches the field. **Procurement failure IS independently confirmed** (offline: zero AA soldiers bought in 200 cycles at 1-in-40), and the medic result shows consumption is real for at least one floored type — so the two causes are not exclusive and both look live._
_**CORRECTION to the source pointer, so nobody hunts for it in the wrong file:** the concurrency warning cited with this batch is **not** in `closeout/bdedd544.md` §4 (that section is the burning-crew ruling). It is in **`WORKSPACE/DISCOVERIES.md:829`**, verbatim: *"A standing-population cap and a high replacement rate produce the same id count; only the overlap distinguishes them. When asked 'how many X does the bot have', measure concurrency, never distinct identities."*_
_**And the good news on that front, verified: the warning does not bite here.** All the counting code in (a)–(c) already measures **concurrently-alive** actors: `CensusValues` (`:1006-1019`, `!IsDead && IsInWorld`), `SupplyTrucksOwnedOrPending` (`:627-629`, additionally excluding `CountsAsEmpty` hulls), `ShouldBuildMoreAntiAir` (`:678-679`), `CountStarvingCustomers` (`:589-591`). **No cumulative-id counting was found — there is no defect of that shape in this subsystem.**_
_Cross-reference: [`closeout/bdedd544.md`](../../closeout/bdedd544.md) records `SupplyDemandSizing` on `UnitBuilderBotModule` and `SupplyFleetMath.DesiredTrucks` as the demand-driven sizing that already exists — **(a) is a refinement of that machinery, not new machinery.** Item **30** (composition intelligence, `f05e31b7`) is the threat-aware buying layer these weights feed._

_**Re-verified 2026-09-02 (`main @ 6a7e1839`) before archiving — all three symptoms confirmed in shipped YAML, and every line cite above has DRIFTED.** (a) `SupplyTruckFloor: 3` + `SupplyTruckFloorPer: 10` at `ai-america.yaml:624-625` and `ai-russia.yaml:320-321`. (b) `medi.america: 2` in `UnitFloors` (`ai-america.yaml:193`) + `medi.america: 10` in `UnitFloorPer` (`:316`) — **the actor is `medi.america`, not `medi`**, which is why a bare `grep "medi:"` returns nothing and reads as a refutation. (c) `aa.america: 2` at `ai-america.yaml:194`, `aa.russia: 2` at `ai-russia.yaml:152`. **Do not trust any `ai-*.yaml` line number in this entry** — these files carry long comment blocks that are edited constantly; grep the key, never the line._

---

### 65. ~~Field actors swallow artillery shells~~ **[SHIPPED `db01b0ae` — CLOSE]**

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — SHIPPED. CLOSE. The fix rode a different commit than either branch the flag named.**
>
> **The fix is `db01b0ae`** *"fields: a shell landing on a field no longer loses its explosion and sound"* (on `wt/field-impact`, ancestor of `main`). Impact classification now skips `IsGroundCover()` actors in **both** copies of `ActorTypeAtImpact` — `CreateEffectWarhead.cs:82` and `WarheadAS.cs:48` — checked *before* the hitshape lookup, so it is cheaper than the code it skips, which matters at 3,187 field actors on river-zeta.
>
> **It honours this item's own acceptance criterion.** The hypothesis below was confirmed exactly: `^CivField` carries a whole-cell HitShape with `Targetable` commented out, so it registered as `anyInvalidActor`, `ActorTypeAtImpact` returned `Invalid`, and `DoImpact` early-returned before the sprite and the `Game.Sound.Play`. **The fix extends the existing `GroundCover` pattern rather than making fields targetable** — which is the transparency-not-targetability reading this item insisted on, and a PITFALL now says so in place at `civilian.yaml:159-166`.
>
> **⚠️ THE DAMAGE HALF WAS A MISATTRIBUTION, AND IT IS SETTLED — do not re-file it.** The invalid-actor early-out exists **only** in `CreateEffectWarhead` and `WarheadAS.IsValidImpact` (reached only by the delayed-weapon warheads). `SpreadDamageWarhead` and `TargetDamageWarhead` — **the two `^ArtilleryRound` actually uses for damage** — have no invalid-actor early-out and always ran. **The shell was doing its damage all along and only losing its feedback.**
>
> **The likelier cause of the user's "no damage" observation is a BALANCE question and belongs elsewhere:** `bugs/discovered.md:443` — `^ArtilleryRound` (`weapons-ballistics.yaml:613-640`) has `Inaccuracy: 2c0` while its effective radius is **1 cell against infantry and ¼ cell against a vehicle**, so a shell aimed at infantry may routinely do nothing. **Unverified; needs a combat-sim check, not this item.**
>
> **Shipped with a RED-verified autotest** — `tools/autotest/scenarios/test-field-swallows-shell/`, RED as *"shell fired (ammo 39 → 34) but 0 impact effects in 30s"*, GREEN after; NUnit 1435/1435; nav-guard byte-identical over 10 maps / 190 pairs, as expected for a change that does not touch the movement layer.
>
> **On the "prior art" line below: `auto/field-actors` (`73996d96`) IS merged, but it is NOT this fix.** It covers ten *movement and placement* cell-checks (`DropsSupplyCache.CanDropCache`, `DropsCrate.CanDeploy`, `Aircraft.CanLand`/`CanEnterCell`/`CanEnterTargetNow`, `HeliEmergencyLanding.IsSuitableTerrain`, `Husk.GetAvailableSubCell`, `CrateInfo.GetAvailableSubCell`, `CrateSpawner.ChooseDropCell`, `BuildingUtils.IsCellBuildable`) and touches **no warhead path**. It is what made `GroundCover` exist; `db01b0ae` is what applied it to impact.

**Perceived:** artillery fired at infantry standing in a crop field kills them. Today the shell lands and simply vanishes — no damage, no explosion, no sound.
_**The user, verbatim:** "I saw an artillery firing at soldiers in a field. And remember these 'Fields' are just a bunch of 'Field' actors put in a grid. When the artillery shell lands on a field actor, it seems like the field actor swallows the shell and the shell does no damage, and shows no explosion effect or sound. I think we need to alter the field actor so that it is a valid target for any weapon. The field actor is technically an actor, but should behave just like if it was a 'tile', and now it clearly doesn't."_
_**The acceptance criterion is the user's own last sentence: a field cell must be indistinguishable from bare ground to any weapon.** Note the tension inside the report — "make it a valid target" versus "should behave just like a tile". **A tile is not a target.** A field that soaks a shell as a valid target would be as wrong as one that swallows it; the wanted behaviour is transparency, not targetability._
_**Mechanism identified before dispatch, stated as a hypothesis to be confirmed against the engine rather than assumed.** `^CivField` (`mods/ww3mod/rules/ingame/civilian.yaml:129`) inherits `^1x1Shape` — **so it has a HitShape** — while `Health:` and `Targetable:` are both **commented out** (`:163-166`). A projectile resolving impact onto an actor with a hit shape is classified as a target hit rather than a ground impact; the warhead's validity check then finds no target types on the victim and `DoImpact` returns early **before** damage, effects or sound. That produces exactly the reported symptom._
_**There is precedent for the shape of the fix, in the actor's own comments.** `^CivField` already carries `GroundCover: true` with the note: "PassClasses above only buys MOVEMENT — it is read by Locomotor alone. GroundCover is what makes every other 'is this cell free' test (crate drop, heli touchdown, husk, placement) see through them as well." **Projectile impact is evidently one more subsystem that does not yet see through fields.**_
_**Scale is a design constraint, not a footnote: 3,187 of river-zeta's 4,544 actors are fields.** Anything added here runs on thousands of actors and per-impact cost matters._
_**Prior art to check first:** branch `auto/field-actors` exists (worktree `/Users/fredrik/worktrees/ww3mod/field-actors`, `73996d96`) — may hold the fix, a rejected approach, or nothing relevant._

---

_**Re-verified 2026-09-02 (`main @ 6a7e1839`) before archiving.** `db01b0ae` confirmed an ancestor of `HEAD` by `git merge-base --is-ancestor`, and confirmed a CODE commit rather than a docs edit by reading its diff stat and message. The `IsGroundCover()` skip is present in **both** copies: `engine/OpenRA.Mods.Common/Warheads/CreateEffectWarhead.cs:82` and `engine/OpenRA.Mods.Common/Warheads/WarheadAS.cs:48`._

---

### 78. Evacuation goes to the nearest wall, not home **[SHIPPED `ac16f2b6` 2026-09-05 — CLOSED 2026-09-21 after the premise was independently re-measured]**

_**CLOSED 2026-09-21 at `main @ 70e63582`** (`wt/item78-study`; arithmetic in
[`WORKSPACE/audit/260921-item78-edge-arithmetic.md`](../../audit/260921-item78-edge-arithmetic.md))._

_**Why it sat open for sixteen days after it shipped.** The stub made a free static study a
precondition and said to DROP the item if the study went the other way. The study was done
(2026-09-05, `tools/evac-edge-math/`) and the fix shipped the same day, but the stub was never
retired — and the 2026-09-21 release-readiness audit then re-read it as open, recording "No
evacuation-edge study exists in `WORKSPACE/`". **That sentence is true and misleading: the study is
under `tools/`, not `WORKSPACE/`, so a `WORKSPACE`-scoped grep cannot see it.** This is the pipeline
README's "a merged branch is not a finished item" trap running in reverse — finished work read as
unstarted because its evidence was filed outside the search path._

_**The premise HELD and is re-confirmed independently at `70e63582`.** Nine unit-anchored maps,
raid population (n = 7170 owner/cell pairs), pre-fix: **14.4%** exit through the owner's own wall,
**70.4%** through an opponent's. Weighting the nine per-map rows by sample size reproduces the
2026-09-05 figures to the printed precision (14.36 / 70.37 / 15.27). Post-fix: **99.5%** own,
**0.4%** opponent. DROP would have been the wrong call._

_**Carried residual — a user decision, not a work item.** The fix multiplies evacuation exposure
~12× (median drive 9 → 108 cells, straight-line and therefore a lower bound). The user approved the
rule, not that consequence. Moved to `AWAITING-USER.md` on closure so this archive entry is not the
only pointer to it._

_**Still unscenarioed:** the allied-SR clause. `tools/autotest/scenarios/test-evac-exits-own-side/`
grades the core rule with an enemy Supply Route as the negative control; discriminating an own SR
from a nearer ALLIED one needs a third player and a `PlayerReference` alliance, an idiom no scenario
in this tree uses._

---

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

---

### 56. Supply trucks still do not commit to a delivery **[CLOSED 2026-09-22 — the binding acceptance bar is DISCHARGED on a live bot-vs-bot match]**

_**CLOSED 2026-09-22 at `main @ a6fe9e94`** (`wt/item56-close`; the bar was run by the manager at
`main @ 0f6912b8`, built at `d69e6883` + a docs merge, `git_dirty: false`). Run dir
`tools/autotest/tournament-results/260922_0211_tournament-s1-eco-river-zeta`; full numeric
re-derivation with the exact greps in [`WORKSPACE/DISCOVERIES.md`](../../DISCOVERIES.md)
§2026-09-22 "The item-56 acceptance bar is DISCHARGED"._

_**The bar, verbatim, was: "a full bot-vs-bot match on a real map showing a truck complete a
delivery", with the 2026-08-14 precondition clause. Both halves are met.**_

_**Precondition.** `earned>0` from tick 80 on both sides; non-zero census `truk` from tick 4640
(Russia) and tick 5760 (USA); peaks 7 (USA) / 3 (Russia); last census tick 7480 `earned=78420` /
`earned=55454`, `trucks-desired` 6 / 3, `held-first-truck=True` both. **Trap recorded on the way
past: the first census line carrying a `truk=` term reads `truk=0+0`** — the term is printed one
40-tick census before the truck exists, so `grep -F 'truk='` answers the wrong question and the
`inWorld+inCargo` pair must be summed._

_**Delivery.** One match, 7,500 ticks, `time_limit`, USA-bot (`experimental`) 86,633 vs Russia-bot
(`stable`) 53,215. **5 dispatches, 4 `crate-placed`, 1 `crate-refused reason=never-arrived` — the
accounting closes exactly and leaves ZERO errands open at the clock.** Two deliveries per side
(Russia 750 + 750, USA 710 + 710), first at ~tick 5,250. Ratio `crate-placed ÷ drop` = **4/5 =
80.0%**, against the ×10 re-baseline's 41.0% raw and 64% resolved-only. **N=5 is a direction, not a
rate** — the bar is an existence bar, and existence is now shown four times by two different bots._

_**The movement reading that Bar run 2 declared impossible was possible, and it came out at zero.**
That reading — "at most one x-travel direction reversal between first dispatch and first
`crate-placed`" — **was never part of the binding bar**: it is authored in the dossier's §3 as the
PASS spec for the proposed `test-supply-two-clusters-commit` scenario, and Bar run 2 imported it
into the bar and then recorded it as undischarged. It is discharged now regardless. `[supply]
truck=` lines carry `@<x>,<y>` once per 150-tick scan; collapsing duplicates, **all four successful
deliveries are strictly monotone in x from dispatch to crate — 0 reversals each.** The one reversal
at each tail is the post-drop egress, which is the wanted behaviour. The refused truck (4842) has 2.
**Limit of the instrument: a reversal with a period under 150 ticks is invisible, bounded at roughly
±4 cells.**_

_**What did NOT close with it, and is the honest residue.**_
- _**`crate-refused reason=never-arrived` is the surviving defect class**, 1 of 5 tonight and **the
  only refusal reason in the ×10 batch (29 across 15 trucks)**. Truck 4842 was refused while at
  x=15 en route to an ordered cell at x=27, then drove on to x=37 and parked there for ten scans.
  **This is an arrival/pathing failure downstream of a commitment that held** — the errand kept its
  frozen anchor — so it is a different defect from the one this item was opened for. Not filed as a
  new item here; it is the obvious next thing to instrument._
- _**The 23% re-dispatch-without-crate residue of the ×10 batch did not reproduce (0 of 5), but 5
  dispatches cannot refute 78.** Tonight does not settle it; it fails to reproduce it._
- _**`Covered` overtook `NoDemand`** (14 vs 12, 53.8% / 46.2%), inverting the ×10 batch's 55.2% /
  41.4%. Both are demand-side facts about the battlefield, not commitment defects. `LowLoad` and
  `NoAnchor` never fired._

_**Watch alongside it — `test-supply-safe-front-keeps-cargo` is still RED, and the framing has been
corrected twice, so read this rather than the version in the stub.** It is NOT "red and unrefuted":
recon (2026-09-05) showed it was red **by configuration** — `IgnoreDangerForDelivery: true` makes
the `SafeFront` limb unreachable — after which it was **re-specced** (2026-09-06) with map-local
`IgnoreDangerForDelivery: false` and `ForwardStagingEnabled: false`, both still present at
`tools/autotest/scenarios/test-supply-safe-front-keeps-cargo/rules.yaml:112,151`. It is now red **on
the merits**, on clause 4 (platoon drift 6 against an allowance of 1), and the cause is traced in
this dossier to `AutoSeekSupplies` walking men out to meet an inbound truck — **not to the supply
module and not to anything this item closed.** Tonight's full-suite triage still records it red and
takes no action on it (`WORKSPACE/audit/260922-tick16-suite-triage.md` §A-ii "Documented reds").
**Do not commit the `expected-status: fail` file proposed at `audits/260901-autotest-suite-audit.md`
§D.1 — the scenario is expected to PASS.**_

_**Two supply/ammo-adjacent findings from the same night's tick-rate triage, carried as pointers.**
Neither bears on the delivery result; neither is tagged `[HIGH]` — that audit classifies by
`(i) real behaviour defect` / `(ii) threshold question`, not by severity, so these are named by
subject match:_
- _**`test-truck-halts-to-serve`** — class (ii), **deliberately not filed as a bug**. Post-budget-fix
  the truck passes `DrovePastLine` (x=34) with all four riflemen at 492-or-better of 500 ammo. More
  budget cannot help; the verdict is gated on position, not deadline. **The open question is a
  ruling:** whether `short` (any `ammo < FullAmmo`) should demand a literal top-off at 1.6% short,
  or whether the last aura batch genuinely never lands._
- _**`test-crate-force-attack`** — class (i), filed. The tank survived and the crate's health never
  moved, identically at 333 and 500 ticks. Relevant here only because a delivered supply drop **is**
  a crate actor (`supplycache`), so whatever makes a crate untouchable by force-attack may govern
  the caches these trucks are now successfully placing._

_**The mechanisms this item is closed against all shipped before the bar was run** — the item's last
open half was the RUN, not code. In order: `IgnoreDangerForDelivery` (the user's pre-authorised blunt
fix, `true` on the shared `SupplyFollowerBotModule@supply`, `enable-ai-any`, so it reaches
`@stable`); `adf7aab8` demoting `NoDemand`/`Covered` from abort conditions to dispatch gates;
`SelectionMinStarvingUnits` (`c60a468d`); `ClusterStickinessNeedMargin` + `SupplyLogisticsMath.KeepHeldCluster`
(`40577269`); `SupplyLogisticsMath.FollowBoxScanOrder`. **The next benchmark baseline must be re-taken
knowingly** — `@stable` moved with all of them._

---

### 56. SUPPLY TRUCKS STILL DO NOT COMMIT TO A DELIVERY **[HIGHEST PRIORITY IN THE WHOLE QUEUE — above item 40]**

> ⚠️ **READ THIS FIRST — CORRECTED 2026-09-01 (`main @ bd8e7290`). THE BLUNT FIX IS BUILT AND SWITCHED ON. Do NOT dispatch a worker to implement it.**
>
> This dossier says below, in more than one place, that the danger sites are "still unexamined", that the user's pre-authorised blunt fix is unspent, and that **"no config flag reaches"** the `ThreatMapManager` site. **All of that is false.** Verified by reading the code, not commit messages:
>
> - **`IgnoreDangerForDelivery` reaches every danger gate in the module** — consumed at `SupplyFollowerBotModule.cs:721, 899, 930, 1490, 1826, 2299`. **`:2299` is `FindSafeFollowPosition`, the `ThreatMapManager` reader this dossier's own caution says no flag can reach.** That caution is spent; do not re-derive it.
> - **The count is SIX, not seven.** "Seven sites, not one seam" has been repeated across three documents and no version of it ever listed seven line numbers. `:125` is the declaration; `:731-732` are debug-string interpolation, not gates. If you need the seventh, find it before citing it.
> - **It is switched ON in shipped content.** `ai.yaml:1129` sets `IgnoreDangerForDelivery: true` on the shared `SupplyFollowerBotModule@supply`.
> - **That instance is `enable-ai-any`, so it reaches `@stable` too.** Allowed by CLAUDE.md policy, but **the next benchmark baseline must be re-taken knowingly.**
>
> **What is actually open is unchanged and is the whole item: THE MATCH.** The binding acceptance bar below, with its `earned>0` / `truk>0` precondition clause, still stands word for word — it was never discharged. **So the correct next action on item 56 is *schedule one bot-vs-bot match and read it*, not *write code*.**
>
> **Watch alongside it:** `test-supply-safe-front-keeps-cargo` is RED — the truck drops when it must not — and is unrefuted. If the live match looks healthy while that scenario stays red, trust the match and distrust the scenario, per this item's own founding lesson at the acceptance bar.
**Perceived:** a supply truck drives to where supplies are needed, drops its supply, and leaves. Today it goes back and forth and never commits.
_**The user, verbatim:** "Supply trucks are STILL going back and forth, not committing to their supply mission. I have told you now a hundred times... every time you tell me it is fixed and the results are more or less the same. Please, please, please make it work now."_
_**Wanted behaviour, in full:** the truck drives near where supplies are needed, **DROPS its supply**, then evacuates. Evacuation already happens automatically when the truck is idle and out of supplies — that half is not being asked for._
_**FIRST POSITIVE LIVE REPORT ON THIS ITEM — 2026-08-15, and it arrived incidentally inside a complaint about something else.** The user, reporting that trucks are barely being **built**: *"When I saw it being built it seems like it correctly went to resupply them."* **After a hundred told-you-so's, the truck's CONDUCT was observed working.** Treat this as one live observation, not a closure — it is a single sighting, unmeasured, and the user was watching procurement rather than delivery. But it materially changes the item's posture: the next person here should **verify whether this item is already closed** before spending the pre-authorised blunt fix on it, rather than assuming six merges all missed. **It also cleanly separates 56 from item 66** — the truck's conduct is not the current defect; its procurement is._
_**THE USER HAS PRE-AUTHORISED THE BLUNT FIX, and this permission is the point of the item — it removes the constraint that has repeatedly made this fail.** Verbatim: **"Even if we need to completely disable their danger awareness then that is better than once again having them not work."** Nobody needs to preserve danger awareness for supply trucks to close this. **Do not spend the attempt protecting it.**_
_**BINDING ACCEPTANCE BAR — a green scenario does NOT close this item.** The bar is **a full bot-vs-bot match on a real map showing a truck complete a delivery.** [`closeout/bdedd544.md`](../../closeout/bdedd544.md) §4 is explicit about why: **both supply scenarios were green while the user's own game showed trucks never delivering** — "a test bed that always reaches a state cannot reveal a broken transition into it". **This item has been declared fixed at least three times against scenario evidence.** Cross-reference `HOTBOARD.md`'s standing line: "Supply trucks: much built, still not demonstrated fixed."_

_**THE BAR WAS UNRUNNABLE UNTIL 2026-08-14, AND WOULD HAVE PRODUCED A FOURTH FALSE NEGATIVE. It needs one added clause, not a rewrite.** The bar names polar-disorder or river-zeta through the tournament harness — and in that harness **the bot could not afford a truck at all**, because map-player bots had no economy (`DISCOVERIES.md` 2026-08-14; item 43's reframe). **Measured on `tournament-s1-eco-river-zeta` before the fix: 492 `[supply] scan` lines across the whole match, every single one `trucks=0 … owned=0`, for BOTH the `@experimental` and the `@stable` side.** Zero trucks were ever bought, so zero deliveries were possible, and the run would have read as "the truck still does not commit" — the exact false negative this bar exists to prevent, on an item already declared fixed to the user three times. **Anyone who had run this bar between 2026-08-13 and the economy fix would have reported failure and gone looking for it in `SupplyFollowerBotModule`, where it is not.**_
_**After the fix the bar IS runnable, and the affordability question is settled.** Same scenario, same seed, one build apart: `@experimental` fields up to **5 trucks alive / 3 eligible**, first at tick **8 440**; `@stable` reaches **1**. So the map and harness choice do not need changing._
_**Required re-wording — add a PRECONDITION clause, keep the bar itself:** "a full bot-vs-bot match on a real map showing a truck complete a delivery, **on a build where the bots have an economy (`PlayerResources` gate fixed 2026-08-14) — verified for that specific run by confirming the `[composition] census` reports `earned>0` AND a non-zero `truk` term at some point in the match, before any delivery evidence is read. A match in which no truck is ever bought is an INSTRUMENT FAILURE, not a negative result, and must not be recorded as one.**" Without that clause the bar cannot distinguish "the truck would not deliver" from "there was never a truck"._
_**Use the census `truk` term, NOT `[supply] scan trucks=`, as the precondition** — this is a trap worth one line. The scan count is the **eligible** list and excludes `IsLowOnSupply`, so a truck that has **just completed a delivery** legitimately reports `trucks=0` on that line. Read against a single scan line it would manufacture a false "instrument failure" verdict on precisely the successful run this item is trying to catch. As "at least one scan in the whole match showed `trucks>0`" it is sound, but the census `truk` term (`inWorld+inCargo`) is the robust signal and `earned>0` is the robust economy check._
_**Not yet established, and it is the actual open question:** whether a truck that now exists **completes a delivery**. These runs measured affordability and ownership only — the `[supply]` delivery/drop evidence was not analysed, and no run reached a match verdict (`run-test.sh` does not arm `BotVsBotMatchWatcher`). **The seven danger sites and the leading suspect at site 4 are all still unexamined.** The fix removes a blocker in front of this item; it does not touch it._

_**NEW EVIDENCE 2026-08-14 — the selector fails in BOTH directions, which is the strongest mechanism signal this item has had. CANDIDATE OBSERVATION, NOT A DIAGNOSIS.** Full entry in [`bugs/discovered.md`](../../bugs/discovered.md), 2026-08-14. `test-supply-safe-front-keeps-cargo` is RED, and **fails identically with and without the economy fix** (paired same-seed runs, `seed 5002`) — so it is pre-existing and unrelated to that change. Its failure note records the truck driving from x=14 to **x=39** (platoon at x=44), **unloading a supplycache and emptying itself**, on a front where **no enemy actor exists and believed danger is 0 everywhere**. That is the **dangerous-front** branch (stop short, dump the load, egress) executing where the **quiet-front** branch (close to the aura, serve in place, keep cargo) is the doctrine. **This item chases the same selector from the opposite side** — its reported symptom is "drives up and does NOT drop", and this is "drops when it must not". One selector failing in both directions argues the defect is in the **selection**, not in the drop or the follow logic, and makes site 4 (`SupplyDropMath.DangerSelectsDrop`, `SupplyDropMath.cs:388`) a materially stronger suspect than it was._
_**Two cautions before anyone acts on it.** (1) **Which site fired is UNTRACED** — site 4 is inference from the symptom, not evidence. (2) **"Believed danger is 0" does NOT clear site 7.** `:2152 FindSafeFollowPosition` reads **`ThreatMapManager`, not `DangerFieldLayer`** (this item's own verified finding), and the scenario's "believed danger 0 everywhere" is a statement about `DangerFieldLayer` only — so a non-zero threat reading at site 7 remains entirely possible ~~and **no config flag reaches it**~~ **— SUPERSEDED 2026-09-01: `IgnoreDangerForDelivery` does reach it, at `SupplyFollowerBotModule.cs:2299`.** The surviving half of this caution still holds and is worth keeping: the two fields are genuinely different, so **a trace that logs only one of them will return a confident wrong answer.**_
_**Concrete next step, and it is cheap:** trace which site sets the mode on `test-supply-safe-front-keeps-cargo` at `seed 5002`, logging **both** the `DangerFieldLayer` and `ThreatMapManager` values that fed the decision. The scenario is deterministic, fast, and already RED — a better instrument for this item than anything currently listed under it. **Nobody has done this.**_
_**VERIFIED AGAINST THE CODE 2026-08-13 — the single most useful finding is that "disable danger awareness" is SEVEN sites, not one seam.** One module owns the whole loop: `engine/OpenRA.Mods.Common/Traits/BotModules/SupplyFollowerBotModule.cs` (2553 lines), configured at `mods/ww3mod/rules/ai/ai.yaml:845-1110`, `ScanInterval: 150`. Per scan (`:866` onward): find clusters → **danger-gate them (`:909`)** → `SectorSpread` assignment (`:935-946`) → per truck pick `bestCluster` (`:990-1011`) → classify errand (`:1018`) → evac branch (`:1097-1123`) → drop branch (`:1138-1143`) → hunt (`:1147-1153`) → follow `Move` (`:1157-1210`). Destination is `ResolveDropAnchor` (`:1588`), and **while an errand runs it is frozen** (`:1612-1622`)._
_**The seven danger entry points, each verified by reading the code:**_
_1. `:909` → `:2061 SelectServableClusters` — drops clusters with `Danger >= releaseLevel`; a relief valve keeps the least-dangerous needy one. Flag `DangerEvac` (`ai.yaml:903`)._
_2. `:2313 StepEvac` — the evac decision itself (`EvacuateWithDwell`, on danger-at-truck and danger-at-destination)._
_3. `:1089 EvacAllowed` — a priority rule that *suppresses* evac when errand ≠ `None` (`ai.yaml:1044`, `:1066`)._
_4. `:1370-1390 DangerSelectsDrop` — **mode selection; sets `reason=SafeFront`, `drop=false`** (`ai.yaml:1093-1104`). Gated `!dispatched`, so it cannot abort an in-flight drop._
_5. `:1699-1704` — the SR-descent anchor guard (`DropDangerSafeUnits`, `ai.yaml:1005`). **This is the ONLY danger path that can still turn a RUNNING delivery around**: a failed descent yields `NoAnchor`, which revokes a dispatched errand at `:1403`. It applies only to the *fallback* anchor._
_6. `:1195 GroundDangerNav.DetourWaypoint` (Stage-E, `DangerFieldRouting`, `ai.yaml:880`) — reroutes the follow `Move`; does not cancel._
_7. `:2152 FindSafeFollowPosition` — **reads a DIFFERENT field: `ThreatMapManager`, not `DangerFieldLayer`.** **No flag covers it.** This is the one that will be missed by anyone who disables "danger awareness" by flipping the documented flags._
_**Next concrete step, and it is now cheap to state precisely:** flip `DangerEvac`, `DropRequiresDanger`, `DropDangerSafeUnits` and `DangerFieldRouting`, **and additionally neutralise the `ThreatMapManager` read at `:2152`, which no flag reaches.** Then run the acceptance bar above. **The leading suspect for the specific symptom "drives up but doesn't drop" is site 4** — `SupplyDropMath.DangerSelectsDrop` (`SupplyDropMath.cs:388`) selects between the two modes on a floor-OR-own-median-OR-absolute test, and the quiet-front branch is precisely "close to the aura, serve in place, **keep cargo**"._
_**The two modes, so nobody re-derives them:** dangerous front → stop `DropShortCells: 5` short, unload the whole 750 as a SUPPLYCACHE, egress (`8d0ff18b`). Quiet front → close to the aura, serve in place, keep cargo (`94eb30de`). **Mode selection is itself a danger consumer**, which is why "the truck arrives and nothing happens" is a danger symptom even though no evac fired._
_**Damping that ALREADY exists — do not re-propose any of it as the fix.** Errand classification + evac priority (`SupplyDropMath.ClassifyErrand`/`EvacAllowed`); the frozen in-flight destination (`:1612-1622`); `DropAnchorHysteresisCells` Chebyshev band (`:1646`); `EvacDwellScans` + `EvacReleaseHysteresisUnits` leg model; the follow-`Move` deadband `RepathThresholdCells` (`ai.yaml:876`); the `SupplyProvider` residue latch `ResidueConfirmScans: 5` (`SupplyProvider.cs:58,441`). **And a trap worth knowing: the Stage-1 order gate (`ModularBot.cs:127-145`) does NOT apply here** — every supply order is unmarked, hence `BotOrderDamping.Protected`, hence never suppressed (`ModularBot.cs:123`, with the reasoning at `:1170-1179`). **So "the order gate is eating the delivery order" is already ruled out.**_
_**Scenarios that exist (do NOT run without a goahead — autotest runs are user-gated):** `test-supply-under-danger`, `test-supply-safe-front-keeps-cargo`, `test-supply-far-front-reached`, `test-dry-resupply-reaches-truck`, `test-infantry-seek-supplies`, `test-queued-attackmove-survives-resupply`. The first pair is green together (`9aa441a7`); `test-supply-far-front-reached` passes for the first time (`377085db`). **All of that is exactly the evidence the acceptance bar rejects.**_
_**Cross-references, not duplicates:** item **40** (danger-scale rework) is the principled version of this — it fixes the danger UNIT so the gates stop firing unconditionally. **This item is the blunt version and outranks it**, because the user has authorised bypassing danger entirely for trucks rather than waiting for the scale to be right. Item **51** hardens `test-supply-safe-front-keeps-cargo`, which is instrument work under the same subsystem. The negative evidence gate is `084367b0`: evac level 1,706 against live median cells of 27,919 (USA) / 94,010 (Russia)._

---

## Recon 2026-09-05 — read-only, `main @ 95bdffb2` (`wt/item56-recon`)

> **STATUS UPDATED 2026-09-05 (`wt/item56`, base `main @ eacc8f44`) — §3's fix is BUILT AND SWITCHED ON, and §2's reachability claim is WRONG. The item is still open on the same thing it has always been open on: THE MATCH.**
>
> **Built:** `ClusterStickinessNeedMargin` (engine default `0` = off; `ai.yaml:1547` sets `1000`), `SupplyLogisticsMath.KeepHeldCluster`, and an optional `held` seed on `AssignSectors` so the spread honours the same hold. Cluster identity across scans is the cluster's lowest-ActorID member, not a centroid cell. Build clean, `dotnet test` 2649/0. New scenario `test-supply-two-clusters-commit` — **not yet run; the manager runs it.**
>
> **§2's "roughly 85% of trucks are on the follow path" is a pre-`c60a468d` figure and does not describe HEAD.** `DropAnchorAtCluster` derives the drop anchor from the cluster just picked, and `SelectionMinStarvingUnits` and `DropMinStarvingUnits` are both `1` over overlapping discs — so **any cluster that selection admits also clears the drop's demand gate**, the errand is issued on that same scan, and `ResolveDropAnchor` freezes it. A loaded truck that has a cluster essentially never reaches the follow path. What the fix governs is the trucks the drop DECLINED (`LowLoad`, `Covered`, `NoAnchor`) and every profile without the drop mode. Full working in `DISCOVERIES.md` 2026-09-05. **Same caveat applies to §4's `NoDemand` 54.1%: that baseline predates the selection gate — re-derive it, do not compare to it.**
>
> **§5(c) is now a one-line note in the curated doc**, at `supply-route.md`'s two-mode table: shipped content disables the selector at `SupplyFollowerBotModule.cs:1662`. **§5's other spent caution:** `FindSafeFollowPosition` is no longer "the site no config flag reaches" — `IgnoreDangerForDelivery` is the first thing it tests (`:2484`).


**No build, no launch, no test run, no YAML validator.** Everything below is from reading files and git history at that SHA. Every line and SHA cite was checked in this worktree; claims that could not be settled by reading are labelled **HYPOTHESIS** with the one thing that would confirm them.

### 1. Verdict on the premise: OPEN, and narrower than the item states

**Still a RUN, not a dispatch — the 2026-09-01 correction stands.** Two things have changed since that correction, and neither is recorded anywhere in `WORKSPACE/`:

- **A behaviour-changing gate landed AFTER the correction and has never been measured.** `c60a468d` (2026-09-03) added `SelectionMinStarvingUnits`, set to `1` at `ai.yaml:1619` (C# default `0` = off, `SupplyFollowerBotModule.cs:459`), applied at `SupplyFollowerBotModule.cs:998-999`. It removes from selection every cluster holding nobody starving — 81.5% of kept clusters in the last baseline. It sits on the `enable-ai-any` instance, so it **moves `@stable` too**. The acceptance-bar match has never been run on a build containing it.
- **The strict wording "never commits" is already refuted by measurement.** `c60a468d`'s own message records a HEAD tournament match at `c9626273`: **delivery success 15.0%**, `NoDemand` 54.1% of drop-declines, truck loss 27% experimental / 42% stable. Trucks DO complete deliveries; they complete few of them. Read the item as *"most trips end without a drop"*, not *"no trip ever ends in a drop"*.
  > **HYPOTHESIS:** "delivery success" means `[supply] crate-placed` divided by `[supply] drop`. That definition is written down nowhere in the repo, and neither is the tournament config used — "VERIFY0903 baseline, match 1" appears in that commit and in a code comment at `SupplyFollowerBotModule.cs:991-996` and **nowhere in `WORKSPACE/`** (`grep -rn VERIFY0903 WORKSPACE` returns nothing). Confirm by re-deriving both numbers from a fresh log before comparing anything to 15.0%.

**Which half is shipped and which is open:**

| path | commitment machinery | verdict |
|---|---|---|
| **drop errand, once dispatched** | frozen destination (`ResolveDropAnchor`, `SupplyFollowerBotModule.cs:1755-1768`); dispatch gates cannot abort (`StepDrop:1450-1483`, `adf7aab8`); drop outranks evac (`:1154-1214`, `e0249693` / `63f2ec48`); anchor hysteresis (`:1789-1793`) | **SHIPPED, and dense.** Nothing on this path re-plans mid-run. |
| **follow path — the truck that was never dispatched** | **none whatsoever** | **OPEN. This is the entire remaining mechanism.** |

### 2. The mechanism as it stands

**How a truck chooses.** Every `ScanInterval: 150` ticks (`ai.yaml:1486`) the module rebuilds its clusters from scratch and re-picks per truck at `SupplyFollowerBotModule.cs:1094-1097` — highest `AmmoNeed` first, nearest as the tie-break — or, with `SectorSpread: true` (shipped), from `SupplyLogisticsMath.AssignSectors` (`:1030-1038`), a greedy assignment recomputed from scratch each scan with no memory of the previous one.

**When it re-decides.** Every scan, unconditionally. **There is no per-truck cluster memory anywhere in the module.** Grepping for stickiness returns only `DropAnchorHysteresisCells` (the drop anchor) and `EvacReleaseHysteresisUnits` (evac), plus `lastFollow` — which is a *cell* deadband, not a *target* one. `ShouldReissueFollow` (`:1375-1381`, delegating to `SupplyLogisticsMath.ShouldReissueFollow`) suppresses a re-issue only while the new follow cell is within `RepathThresholdCells: 3` of the old one. A cluster switch moves the follow cell much further than 3 cells, so it always re-issues — and the `Move` is non-queued, so it cancels the drive already in progress.

**Why the ordering can invert with nothing else changing.** `AmmoNeed` is a live quantity that moves as men shoot and are fed. Two adjacent scans can rank two clusters differently on their own, with no danger term, no enemy and no event involved. That is a truck turning around every 150 ticks: the reported symptom, with no danger input anywhere in it.

**What can still make it drop a delivery.** Only `LowLoad` (monotone — a truck only loses supply, so it cannot oscillate) and `NoAnchor` (cannot fire while dispatched, because the frozen anchor always has a value). `NoDemand` and `Covered` were explicitly demoted from abort conditions to dispatch gates by `adf7aab8` (2026-08-13); the comment at `:1450-1470` names "approach, Stop, approach" as the symptom that change removed. **Evac is entirely off:** `evac = DangerEvac && dangerField != null && !IgnoreDangerForDelivery` (`:913`), and the flag is `true` at `ai.yaml:1616`.

**So the one surviving oscillator is target churn on the follow path** — reached by every truck not currently dispatched, which at the last measurement is roughly 85% of them.

### 3. Smallest plausible fix

**One new `Info` field: per-truck cluster stickiness, defaulting to OFF.** Hold the cluster a truck is already serving; at `:1094`, if the held cluster is still in this scan's list and still inside that truck's leash, keep it unless a challenger beats it on `AmmoNeed` by a configured margin. Release on: cluster gone, leash broken, truck emptied, or a drop dispatched (`dropTarget` already supersedes). Prune it in the same `activeTrucks` sweep that already prunes `lastFollow` (`:804-808`).

Three reasons this is the right size. It mirrors `DropAnchorHysteresisCells`, the same idea already accepted one layer down. It needs no danger term, so item 40's rescale cannot rot it. And a C# default of `0` keeps `@stable` byte-identical until `ai.yaml` sets the key, satisfying `architecture.md` §"Adding a behavioural field to a trait shared by both bot profiles".

**Do not propose any of these — they all already exist:** the errand freeze, the drop-anchor hysteresis, the follow-cell deadband, the evac dwell model, the `SupplyProvider` residue latch, and the Stage-1 order-gate exemption.

**The autotest that would measure it.** No existing scenario can. `test-supply-under-danger`, `test-supply-far-front-reached` and `test-supply-safe-front-keeps-cargo` each contain **exactly one cluster**, and a single cluster makes re-pick oscillation structurally impossible. That is this item's founding lesson in its purest form: *a test bed that always reaches a state cannot reveal a broken transition into it.*

> **`test-supply-two-clusters-commit`** — one bot truck; **two** starving platoons on opposite bearings roughly 20 cells apart, both inside the follow leash; no enemy on the map. Drain them so their `AmmoNeed` values sit close enough to cross during the trip.
>
> **What counts as the answer:** count direction reversals in the truck's x-travel between its first dispatch and the first `[supply] crate-placed`. **PASS** = at most one reversal, and a crate placed inside the tick budget. **FAIL** = two or more reversals, or no crate. Assert on movement, not on the module's internal target — the user's complaint is about what the truck visibly does.

### 4. Existing scenarios to run FIRST, ranked, with expected verdicts

1. **`test-supply-far-front-reached`** — expect **GREEN**. The closest thing to a commitment test that exists (platoon 41 cells out, beyond `MaxFollowDistance: 35`; its own description says *"a truck that never sets off is the FAIL this guards"*), and the cheapest check that `SelectionMinStarvingUnits: 1` did not starve the selector of clusters. **A red here would mean the 2026-09-03 gate broke dispatch, and would outrank everything else in this item.** This is the single `run-test.sh` the CLAUDE.md rule contemplates.
2. **`test-supply-safe-front-keeps-cargo`** — expect **RED**, and **the red is correct behaviour, not a defect** (§5a). Worth one run only to confirm that mechanism from the log, after which the scenario should be re-specced or retired. **Do not read its red as evidence for this item.**
3. **`test-supply-under-danger`** — expect **GREEN**, low information. Same single-cluster shape as #1, and with evac off it exercises very nearly the same path.
4. **The acceptance bar itself** — one bot-vs-bot tournament match. `tournament-s1-eco-river-zeta` is the config the dossier names and the one where affordability is already established (experimental fields 5 trucks alive / 3 eligible, first at tick 8 440). **Read the precondition FIRST:** a `[composition] census` line (emitted at `UnitBuilderBotModule.cs:921`) showing `earned>0` and a non-zero `truk` term. Then `[supply] crate-placed` against `[supply] drop`, then the `drop-declined reason=` histogram against `NoDemand` 54.1%.

Running 1–3 together is user-gated multi-test territory; #1 alone is not.

### 5. Where the code contradicts this dossier and `PIPELINE.md`

**(a) `test-supply-safe-front-keeps-cargo` is RED BY CONFIGURATION. Its red is not a selector defect, and the "the selector fails in BOTH directions" finding above is very probably an artefact.**

The mode gate reads `if (drop && Info.DropRequiresDanger && !Info.IgnoreDangerForDelivery && !dispatched && cluster != null)` (`SupplyFollowerBotModule.cs:1526`). With the flag on, **`SafeFront` cannot fire**, the drop mode is unconditional, and the truck unloads a crate on any front. The scenario's clause 2 is "no `supplycache` ever existed"; its `rules.yaml:4` states outright *"Nothing here touches SupplyFollowerBotModule"*; and its map runs `Bot: experimental` (`map.yaml:119`), which holds `enable-ai-any` and therefore the flag. **The scenario asserts the exact behaviour the user asked to have removed, so at shipped config it can only pass by the truck failing to deliver.**

The timeline settles it: `9aa441a7` (2026-08-10) — the pair green together, no crate; `b87aeb62` (2026-08-13) — `IgnoreDangerForDelivery: true` lands; 2026-08-14 — the red is observed and written up at `bugs/discovered.md:2559-2578` as *"the dangerous-front branch executing where no enemy exists"*, concluding *"there is no input under which the dangerous branch is the correct selection here."* **There is one: the flag.** The selector never ran.

> **HYPOTHESIS**, one run to confirm: re-run it and read the `[supply] init` line (`:743-748`). `ignore-danger=True` proves site 4 was short-circuited, and the absence of any `reason=SafeFront` in the log confirms it. *What could not be settled by reading: whether both arms of the 2026-08-14 paired run were on a build containing `b87aeb62`. The dates make it very likely; the run records are not in the repo.*
>
> Consequence for `PIPELINE.md:301`: **"if the live match looks good while that stays red, trust the match" is right for the wrong reason.** The two do not disagree, and the scenario is not "unrefuted" — it is refuted here.

**(b) `c60a468d`'s own correction is wrong, and the code comment now repeats it.** The commit says *"ai.yaml said `DropMinStarvingUnits` was 1. It has been 3 since `a86e2fb6`"*, and rewrote the comment block to read `3`. **The shipped value is 1.** `a86e2fb6` (2026-08-08) set 3; `63f2ec48` (2026-08-10) lowered it to 1 (`git blame -L1854,1854 mods/ww3mod/rules/ai/ai.yaml`); it is the only occurrence anywhere under `mods/`, and it sits inside `SupplyFollowerBotModule@supply:` (block `1483`–`1895`; the next trait header is `AdaptiveProductionBotModule@experimental.america:` at `1896`). Two consequences:

- The measured claim at `SupplyFollowerBotModule.cs:994` — *"450 of 455 (98.9%) could not clear `DropMinStarvingUnits`"* — is computed against 3. Against the real bar of 1 the correct figure is the zero-starving count, **371/455 = 81.5%**. The diagnosis survives (81.5% is still the dominant term); the arithmetic and the 98.9% do not.
- `SelectionMinStarvingUnits`' entire stated rationale — *"DELIBERATELY A LOWER BAR THAN `DropMinStarvingUnits`"* (`SupplyFollowerBotModule.cs:452-456`, repeated at `ai.yaml:1618-1622`) — **does not hold in shipped content: both are 1.** The design property is unmet. It fails in the safe direction (dispatch is stricter than intended, not looser), so nothing needs reverting — but nobody should reason from that `Desc` again.

**(c) `DOCS/reference/supply-route.md` §"Forward delivery" describes a mode selector that shipped content disables, and never names the flag that disables it.** `:109-130` gives the two-mode table and states *"The floor is what protects the quiet-front branch … a map with no believed enemy serves in place regardless."* `grep IgnoreDangerForDelivery DOCS/reference/supply-route.md` returns **nothing**; same for `economy.md:301`. The curated docs describe the principled design; the mod ships the blunt override. Not fixed here — recon is read-only — but this is a curation item.

**(d) Line-number drift, exactly as `PIPELINE.md:259` predicted.** `IgnoreDangerForDelivery: true` is at **`ai.yaml:1616`**, not `:1339` (nor `:1129`, nor `:1041`). Consumption sites re-derived at this SHA: `SupplyFollowerBotModule.cs:735, 913, 944, 1526` plus two later sites; `:735` and `:913` were read individually, the rest located by grep. **Record the KEY, not the line.**

**(e) Minor, same class.** `SupplyFollowerBotModule.cs:2622` argues *"`DropMinSupply` is 250, so a truck eligible to be dispatched affords the costliest batch (65) almost four times over"*. `ai.yaml:1861` ships **100**. The conclusion survives (100 > 65); the number and the "four times" do not. The dossier's scenario list is otherwise accurate — `test-infantry-seek-supplies` does still exist.

### 6. Nothing here needs a code change before the run

The blunt fix is on, the commitment invariants are in place on the drop path, and the one unmeasured change (`SelectionMinStarvingUnits`) points the right way. **The next action is still the match.** Hold the follow-path stickiness fix in §3 until the match log says whether target churn is what the trucks are actually spending their time on — `[supply] truck … target=` changing cluster between consecutive scans on the same truck is the single readout that decides it.

### Bar run 1 — 2026-09-05, `main @ 40577269` (+docs), `tools/autotest/tournament-results/260905_item56_bar_s1`

One `tournament-s1-eco-river-zeta` match, `--seeds 1`, hidden, 7500 ticks (time limit), USA `@experimental` 88272 vs Russia `@stable` 81620. **Precondition met:** last census `earned=36734` / `33530`, `trucks-desired=6` both sides, ~10 `truk` in play. **`[supply] init`** shows `ignore-danger=True` on both bots (so `SafeFront` never fires — consistent with the scenario being red by configuration).

- **Drops dispatched: 5** (`[supply] drop … new`). **`crate-placed`: 2** (both USA, 750 and 555 supply). The other 3 were dispatched in the last ~600 ticks and were still `drop-inflight` at the time limit — **zero errands abandoned.** Read as 2/5 = 40 % delivered-before-clock against the 15.0 % at `c9626273`, but N=5 in one match is not a number, it is a direction.
- **Declines: 27** — `NoDemand` 17 (63 %), `Covered` 10. `NoDemand` is still the dominant reason a full truck does not drop, i.e. nobody starving within reach — a demand-side fact, not a commitment defect. `Covered` cases all show `cache-near ≥ 750`, i.e. a crate already down.
- **Stickiness is live on the follow path:** `[supply] truck … held=<actorId>/margin=1000` for 63 truck-scans across 7 held clusters, `held=<none>` for 62 — trucks with no cluster in leash. No `release` storms (3 `release` lines total).
- `@stable` (Russia) placed 0 crates in 5 minutes; its two drops were dispatched at ticks ≈6900–7300. Whether that is the map's spawn asymmetry or the profile is not answerable from one match.

**Still owed for the acceptance bar as written:** the N-match reading, which the item-43 re-baseline (`WORKSPACE/ai-bench/RUNBOOK-260905.md`, batch 3 is this exact scenario ×10 mirrored) produces for free — read `crate-placed ÷ drop` and the decline histogram out of those logs rather than spending a separate batch.

### Bar run 2 — the ×10 reading, 2026-09-06 (stamped `9cb423d4`, code `bb89f9fd`), `tools/autotest/tournament-results/260905_rebaseline_s1_exp`

**The N-match reading is taken.** Read out of batch 3 of the item-43 re-baseline exactly as the paragraph above anticipated — ten `tournament-s1-eco-river-zeta` matches, `--mirror`, seeds 1017…10017, 7,500 ticks each, no extra batch spent. Card: [`../../benchmarks/260905-rebaseline.md`](../../benchmarks/260905-rebaseline.md).

**Headline: 32 crates placed against 78 drops dispatched = 41.0 % delivered, against 15.0 % at `c9626273`.** `NoDemand` remains the dominant decline at **55.2 %**, essentially unmoved from the 54.1 % quoted for `c9626273`.

| m | `[supply] drop ` | `crate-placed` | delivered | `NoDemand` | `Covered` | `NoAnchor` |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 13 | 7 | 53.8 % | 8 | 10 | 1 |
| 2 | 9 | 5 | 55.6 % | 9 | 3 | 0 |
| 3 | 5 | 1 | 20.0 % | 6 | 12 | 5 |
| 4 | 3 | 0 | 0.0 % | 9 | 5 | 0 |
| 5 | 7 | 0 | 0.0 % | 15 | 4 | 0 |
| 6 | 8 | 3 | 37.5 % | 12 | 8 | 0 |
| 7 | 13 | 5 | 38.5 % | 5 | 10 | 0 |
| 8 | 6 | 6 | 100.0 % | 7 | 11 | 0 |
| 9 | 5 | 3 | 60.0 % | 15 | 5 | 0 |
| 10 | 9 | 2 | 22.2 % | 14 | 7 | 0 |
| **all** | **78** | **32** | **41.0 %** | **100 (55.2 %)** | **75 (41.4 %)** | **6 (3.3 %)** |

Decline histogram, batch total, **181 declines**: `NoDemand` 100 (55.2 %), `Covered` 75 (41.4 %), `NoAnchor` 6 (3.3 %). **`LowLoad` never fires — zero occurrences in ten matches.**

**Denominator note, because it decides how the 41 % is read.** All 78 `[supply] drop ` lines end in `new` — every one is a fresh dispatch, none is a re-log — so the ratio is *crates ÷ dispatches*, matching the hypothesised definition of "delivery success" at §4. That definition is still a **HYPOTHESIS**: it is written down nowhere in the repo, and neither is the tournament config behind the 15.0 %, so **41.0 % vs 15.0 % is a comparison of two numbers computed the same way by assumption, not by verification.** The `NoDemand` comparison is weaker still — §2's own caution says the 54.1 % baseline predates the selection gate and should be re-derived rather than compared to. Treat 55.2 % as *this instrument's* figure and the near-equality with 54.1 % as a coincidence unless someone re-derives the old one.

**Full per-dispatch accounting — this is what actually answers the commitment question.** The 78 dispatches resolve exactly: **32 placed a crate + 18 re-dispatched without one + 28 still open at the clock = 78.**

- **28 of the 78 (36 %) were simply still in flight when the 7,500-tick clock stopped.** Excluding them, delivery is **32 / 50 = 64 %**. Matches 4 and 5 score 0 % solely because 3 of 3 and 6 of 7 of their dispatches were unresolved at the limit; match 8 is 6/6.
- **18 dispatches (23 %) ended with the same truck being dispatched again before placing a crate.** That is the residual abandonment, and it is the number to attack next — down from "never commits", but not zero.
- **`crate-refused reason=never-arrived` fires 29 times across 15 distinct trucks**, and it is the only refusal reason in the batch. The errand commits and holds its frozen anchor; the truck then **fails to reach the ordered cell within the 2-cell tolerance**. This is a *pathing/arrival* failure, not a commitment failure — a different defect from the one this item was opened for, and it is plausibly what most of the 18 re-dispatches are. **HYPOTHESIS, not measured:** the re-dispatches and the `never-arrived` refusals are the same trucks. Confirming it needs a truck-id join across the two line types, which this reading did not do.

**Bot attribution (by `notes.players[].bot_type`, not by slot — the batch is mirrored):** of the 32 crates, **`@stable` placed 21 and `@experimental` placed 11.** Bar run 1's observation that `@stable` placed 0 crates in its single match **does not survive N=10** — `@stable` is the better deliverer here, by roughly 2:1. Both profiles share `SupplyFollowerBotModule@supply` (`enable-ai-any`), so this is not a profile-gated difference in the module; what causes it is unexplained.

**Verdict against the acceptance bar.** The bar's precondition (`earned>0`, non-zero `truk`) is met — this is the same scenario and the same live-economy build as bar run 1, and all ten matches ran the full clock with trucks in play. On the numeric half the item has moved a long way: **41 % delivered (64 % of dispatches that had time to finish), zero `LowLoad` aborts, and every unresolved errand still holding its anchor at the clock rather than abandoned.** The bar as written also demands a movement reading — *at most one x-travel direction reversal between first dispatch and first `crate-placed`* — and **that is NOT discharged here**: these logs carry no per-tick truck positions, so the reversal count cannot be computed from them. **What remains open on item 56 is the reversal assertion and the 18-dispatch abandonment residue, not the delivery rate.**

---

## Recon §5(a) discharged — 2026-09-06, `wt/safe-front` off `main @ 9cb423d4`

**`test-supply-safe-front-keeps-cargo` was RE-SPECCED, not retired** (the "retire or re-spec it (backlog)" line at `PIPELINE.md`). It now carries a scenario-local `IgnoreDangerForDelivery: false` on `SupplyFollowerBotModule@supply`, so the mode it asserts is reachable on that map and nowhere else. **No engine code and no shipped `ai.yaml` were touched.**

**Why re-spec rather than retire, and rather than invert to the shipped contract.** The shipped contract — the drop mode firing unconditionally — is *already* asserted by `test-supply-under-danger` (`REQUIRE_CRATE = true`). With the bypass on, both maps take the same unconditional-drop path, so inverting this scenario would have asserted the sibling's assertion twice and left the **serve-in-place branch under test nowhere**. That branch is the documented half of the doctrine (`supply-route.md` §"Forward delivery") and is exactly what item **40**'s danger-scale rework exists to restore, so it is the half worth keeping instrumented. Retiring also had a cost recon did not price: `engine/OpenRA.Test/.../SupplyDriftClauseTest.cs` **parses `local HOLD_DRIFT` out of this scenario's `.lua`**, and `ReadScenarioConstant` degrades to `Assert.Ignore` when the file is missing — deleting the directory would have silently un-executed a green test rather than failing it. Both constants the fixture reads (`HOLD_DRIFT = 1` here, `MAX_DRIFT = 6` in the sibling) are unchanged.

**The mode gate, re-cited at this SHA — recon's `:1526` has drifted to `:1662`.** Record the KEY, not the line:

```csharp
if (drop && Info.DropRequiresDanger && !Info.IgnoreDangerForDelivery && !dispatched && cluster != null)
```

`ai.yaml:1882` sets `DropRequiresDanger: true`; `ai.yaml:1676` sets `IgnoreDangerForDelivery: true` and short-circuits it. Recon §5(a) is confirmed exactly as written, by reading.

**NEW, and it is the part that matters for reading the run: THE FALSE PASS.** Recon framed the red as the problem. With the gate reachable the risk inverts, and the four Lua clauses cannot see it. Any drop decline — `NoDemand`, `Covered`, `LowLoad`, `NoAnchor` — also leaves `drop = false`, after which the truck takes the follow path, drives to the platoon and serves from its aura: no crate, cargo kept, ammo up, platoon held. **All four clauses green, mode selector never consulted.** So `Test.Pass` alone does not discharge anything here; `reason=SafeFront` in `debug.log` does.

**`ai.yaml:1914` ships `DropMinStarvingUnits: 1`, confirming recon §5(b) a second time** — and the map's own comment claimed `3`, so the stale value has now been quoted in three places (two code comments plus that map). Corrected in `map.yaml`; the two code comments are engine files and were left alone.

**Two things this makes stale elsewhere, both left as pointers rather than rewrites:**
- `WORKSPACE/audits/260901-autotest-suite-audit.md` §D.1 proposes committing an `expected-status: fail` declaration for this scenario. **Do not commit it.** It was conditional on a run confirming the red, and the red has now been explained rather than confirmed. No `expected-status` file is added: the scenario is expected to PASS.
- `bugs/discovered.md` (2026-08-14) concludes *"there is no input under which the dangerous branch is the correct selection here"*. The input was the flag; recon already said so, and the scenario now removes it locally.

**HYPOTHESIS — the one thing reading cannot settle.** Clearing the flag re-arms all seven bypass sites, not one. Six are inert or off-path on an enemy-free map and each is argued line-by-line in the scenario's `rules.yaml`; the seventh, `FindSafeFollowPosition` (`:2484`), genuinely runs. It argmaxes `friendlyValue - enemyValue` over a ±3 box, so with no enemy it maximises friendly density and walks the follow cell **toward** the platoon rather than away — the helpful direction for clauses 1 and 4, but the cell is no longer the centroid. **If the scenario fails clause 4 with the truck short of aura range, that is the site.**

### First run of the re-specced scenario — 2026-09-06, main @ 8802a781, run `260906_090304_p9113_test-supply-safe-front-keeps-cargo`

**The instrument works, and the scenario is now RED BY MERIT.** `debug.log`: `[supply] init … ignore-danger=False` (the map-local override merged); `drop-declined` reasons NoDemand ×2, **SafeFront ×1** (the mode gate was consulted and took the serve-in-place limb — this is the line the re-spec exists to make possible). Verdict FAIL on clause 4: peak platoon drift 6 cells against an allowed 1 (per man 5/6/5/5/5 from spawn x=44); primary ammo refilled to 100/100/100/100/100, so the resupply itself happened. **The truck went from x=14 to a furthest x=37 and stayed there** — ~7 cells short of the platoon at x=44 — so the men closed the distance, not the truck. Per the re-spec note above, clause 4 breaking with the truck short of aura range points at the follow-position/closing logic (`FindSafeFollowPosition`, `SupplyFollowerBotModule.cs:2484` at this SHA), not at the mode gate. Hypothesis, unverified: the safe-front follow cell is chosen at threat-argmax and the truck treats reaching it as done without a second leg into aura range. One run; the defect is now a pipeline finding, not a scenario-hygiene item.

**HYPOTHESIS SETTLED — 2026-09-06, `wt/safe-front-close` off `main @ da7dd984`. Half right, and the half that was right is not the half that matters.** The follow cell IS chosen at threat-argmax and the truck DOES treat reaching it as done — but the reason is a **total tie**, not a gradient. Every `follow=` line in the run is the centroid plus exactly `(-3, -3)`, across nine scans and five centroids, and a real friendly-density gradient would have peaked the score AT the centroid, so a corner can only win by the strict `>` keeping whichever cell was scanned first. It is structural: `GetThreat` samples a radius of `CellSize: 8` cells (`world.yaml:297`), **wider than the ±3 box**, so all 49 candidates enclose the same actors and return the same float. The scenario's own site-7 note predicted the argmax would walk the cell *toward* the platoon; it cannot, because there is no gradient inside the box to walk along. **Fixed** by scanning nearest-first (`SupplyLogisticsMath.FollowBoxScanOrder`); inert in shipped content, which sets `IgnoreDangerForDelivery: true`.

**AND IT DOES NOT MAKE CLAUSE 4 GREEN — the drift is a different trait.** `[seek] leave tick=250 … provider=truk@26,16 dist=18c leash=20c` ×5: all five riflemen left the front the moment the inbound truck crossed their 20-cell leash (`AutoSeekSupplies.cs:197`). At the measured speeds (truck 0.0487 c/t from the scan positions; infantry ≈0.02 c/t, derived) aura entry lands at tick ≈437 with the truck at x≈35 — *before* it reaches either 37 or 44 — so **the meeting point does not depend on the truck's target at all.** Moving the target only lowers the worst man's `dy` from 5 to 2, predicting a peak drift of **≈4** against an allowance of 1. The safe-front doctrine needs a second half nobody has built: a man must not walk out to a provider already inbound to him. Not attempted here — `AutoSeekSupplies` sits on `^Soldier` and governs human-owned units and both bot profiles, so it is the manager's call. Full working, with the log lines, in `WORKSPACE/DISCOVERIES.md` 2026-09-06.

### Second run of the re-specced scenario — 2026-09-06, `wt/safe-front-close @ 10b4a88c`, run `260906_092137_p10284`

**THE TRUCK-SIDE FIX TOOK, AND IT MOVED THE DRIFT BY ZERO.** `ignore-danger=False`; all 14 `[supply] cluster` lines now read `follow == cell` (it was `cell − (3,3)` at every site in the previous run); truck furthest **x=40** (was 37), still on the map; `reason=SafeFront` ×1; `crate=NONE`. Verdict FAIL on clause 4 alone, **peak drift 6**, per-man trace `44->47(5) 44->43(6) 44->39(5) 44->47(5) 44->47(5)` — **byte-identical to the pre-fix run despite a different seed** (`-2060360141` vs `-311826861`).

**The sequence, from `debug.log`:**

| tick | line | what |
|---|---|---|
| 15 | `[exp-ammo] withhold module=offense … unit=e3#10..14 cell=44,14..18 reason=starving` | all five withheld from the offense module *because* they are starving |
| 250-254 | `[seek] leave … provider=truk@26,16 dist=18c leash=20c` ×5 | all five leave the front as the inbound truck crosses the 20-cell leash |
| 277 / 352 / **427** | `[exp-poi] disperse … centroid=(43,16)` / `(41,16)` / **`(39,16)`** | the platoon centroid walks **5 cells west** |
| ~500 | scan 4 `starving=4` → scan 5 `starving=0`, ammo 100/100/100/100/100 | served from the truck's aura |
| 502→802 | centroid `(40,16)`→`(42,16)`→`(43,16)`→**`(44,16)`** | `SeekSuppliesAndReturn` walks them home |
| **815** | `[exp-ammo] release … unit=e3#10..14 cell=44,14..18` | fed ⇒ no longer starving ⇒ **released into the free pool** |
| **815** | `[exp-staging] player=USA-bot anchor=43,17 idle=5 staged=5` | **same tick**: forward staging fans all five onto a ring around `43,17` |
| 952-1102 | `disperse … centroid=(44,17) clumpRadiusCells=2 → 3 → 4` | the fan opening; final x = 47/43/39/47/47 |

**THE EAST MOVES ARE FORWARD STAGING, not a `SeekSuppliesAndReturn` overshoot and not a moved anchor.** `PoiOffensiveBotModule` (`:2940-2995`) issues one `AttackMove` per unit to `ForwardStagingMath.SpreadSlot(anchor, StableSlot(u.ActorID, rings), StagingSpreadStepCells)`. With `StagingStandoffCells: 6` / `StagingSpreadStepCells: 2` (`ai.yaml:732-734`) the widest ring is Chebyshev **4** from the anchor, so slots run x∈[39,47], y∈[13,21] around `43,17` — exactly the observed 47/43/39. The return leg is innocent: the centroid is back at `(44,16)` by tick 802, and the release lines at 815 print each man at his **spawn cell**, so they were home and stationary when staging picked them up.

**WHY BYTE-IDENTICAL.** Both movers are deterministic and neither reads the truck's row. The truck's **x-progress is unchanged by the fix** — 14/21/29/36 at scans 1-4 in *both* runs; only its y changed (16 throughout, versus 15→14→13 before). And `StableSlot` is keyed on `ActorID`, so five identically-spawned men draw the same five slots in any run.

**THE ≈4 PREDICTION WAS WRONG, AND THE ERROR WAS THE MODEL, NOT THE SPEED ESTIMATE.** The kinematics were right: predicted aura entry at tick ≈437, and the centroid reaches its westernmost `(39,16)` at tick **427**. What was wrong is that each man was modelled as walking **along his spawn row**, which credited the fix with dropping the worst man's `dy` from 5 to 2 and bought ~2 cells of drift with it. `SeekSuppliesAndReturn` paths the man **at** the truck, so he closes in both axes and collapses his own `dy` long before contact — in both runs. The truck's row was therefore never a term in the meeting point, and the fix could only ever have changed the drift through a quantity the men were already zeroing themselves.

**A RIGOROUS LOWER BOUND, worth stating because it survives any model.** All five walked the same way, so the centroid's westward displacement IS the mean westward drift; mean 5 ⇒ **max ≥ 5**, and Chebyshev drift ≥ |dx| drift. The seek excursion alone therefore puts the peak at **≥5 by tick 427**, against an allowance of 1, before staging has fired at all. Staging is separately capable of ~5 (a man from `(44,16)` to slot `(39,17)` is Chebyshev 5). **Which of the two owns the reported 6 is NOT separable from this log** — neither `[exp-poi] disperse` nor `[exp-staging]` records per-actor destinations. `LayeredDefenceBotModule` carries exactly that line (`[defence] assign unit=…@… → …`, `:549`) and it fired **zero** times here; staging wants the same instrumentation.

**CONSEQUENCE FOR THE ITEM, AND IT IS THE DECISION-RELEVANT PART: CLAUSE 4 CANNOT PASS AS THE SCENARIO IS CONFIGURED, EVEN WITH BOTH SUPPLY DEFECTS FIXED.** Being fed is what *releases* these men into the free pool (`withhold reason=starving` at tick 15 → `release` at 815), and forward staging then repositions them unconditionally. Even slot 0 — the bare anchor `43,17` — is Chebyshev **3** from the man spawned at `(44,14)`, so no assignment staging can make satisfies an allowance of 1. **A successful resupply necessarily triggers the repositioning that fails the clause.** This is a scenario-configuration gap, not a clause defect, and the clause is left alone per the dispatch: the map's `rules.yaml` needs `ForwardStagingEnabled: false` (or the offense module removed) alongside the `IgnoreDangerForDelivery: false` it already carries, so the scenario measures the supply layer rather than the offense layer. Until then a green clause 4 is unreachable and a red one is not evidence about supply.

**OVERRIDE APPLIED 2026-09-06 (same branch): the map now carries `ForwardStagingEnabled: false` on `PoiOffensiveBotModule@experimental` next to its `IgnoreDangerForDelivery: false`** — required because a successful resupply is precisely what releases the withheld men into the free pool (`[exp-ammo] withhold … reason=starving` tick 15 → `release` tick 815), and `StageFreePool` then repositions them unconditionally on that same tick, by at least Chebyshev 3 even at the bare anchor. Field declared `PoiOffensiveBotModule.cs:563` (engine default false), shipped `true` at `ai.yaml:731`; the map pins `Bot: experimental` (`map.yaml:119`). The run-side guard against a mis-keyed override taking silently is that `[exp-staging]` must be ABSENT from `debug.log`.

**THE GATE THAT WOULD FIX THE SEEK HALF, stated precisely so the user can be asked.** In `AutoSeekSupplies.TickIdle`, between `FindNearestUsableProvider()` and the `QueueActivity(new SeekSuppliesAndReturn(...))` at `:202`: a new `Info` field — engine default **false**, i.e. today's behaviour, per the shared-trait rule — suppressing the dispatch while the chosen provider is **closing on us**. The cheapest deterministic test with no history problem: cache the chosen provider's `ActorID` and squared distance per scan, and if the same provider is chosen again with a **strictly decreased** squared distance, hold this scan. Zero RNG, one `uint` + one `long` per actor, pruned alongside the existing per-actor state. It is self-limiting and cannot deadlock — the man re-asks every `ScanInterval: 40`, so a truck that parks, stalls or drives away is fetched from within ~40 ticks. It must NOT be applied to the `ReturnWhenEmpty` break-off path (`ITick.Tick`): a wholly dry man should still break off. And `SupplyProvider.OnSupplyErrand` cannot serve as the signal, because it reads only `RestockSupply`/`PlaceSupplyCache`/`CollectSupplyCache`/`DeliverSupply` while the follow path issues a plain `Move`.

**What that costs a HUMAN-OWNED squad, since `AutoSeekSupplies` sits on `^Soldier` and has no owner-side split.** With the field on, a player's low-ammo infantry standing idle no longer trots off to intercept a supply truck that is driving toward them — they hold and are served in place, which is the behaviour the doctrine describes and most players would expect. The costs: (a) up to one extra scan (~40 ticks, ≈1.6 s) of delay before fetching from a truck that turns out to be merely passing by, because the first observation has no previous distance to compare against; (b) a squad that would previously have met a truck halfway now waits for it, so time-to-resupply rises whenever the truck is slower than the men; (c) units under an explicit player order are unaffected either way — this is the idle path only. **Nothing changes unless the field is set in YAML**, and setting it on `^Soldier` moves human play and both bot profiles together. That is the choice to put to the user.
