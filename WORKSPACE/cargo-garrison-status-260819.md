# Cargo & Garrison — status reconciliation and ranked proposals

**Date:** 2026-08-19 · **Branch:** `wt/cargo-garrison` · **Base:** `main @ 66fd33d3` (= `origin/main`, clean)
**Status:** research only. Nothing implemented. No game was launched. Every decision below is reserved for the user.
**Ranking rule applied:** the user ruled *legibility first* — §4 ranks clarity work above behavioural work, and the two are kept in separate lists.

Every claim about current behaviour carries `file:line` read at `66fd33d3`. Where I infer rather than read, it says **INFERENCE**.

---

## 0. The short answer

**Garrison is in better shape than its own documentation says. Cargo is in worse shape than its commit log suggests.**

Since `garrison-proposals.md` was written three days ago, three of its six proposals shipped — the suppression readout, the `IsDucking` deletion, and the hardcoded-cover fix. Nobody updated the doc, so it still reads as an open proposal set. Meanwhile cargo went the other way: a passenger-row affordance was built and then **deleted**, leaving the cargo panel with less information in it than the garrison panel next door, and leaving a whole feature (eject rally points) wired at both ends with nothing in the middle to issue the order.

The single most consequential finding is not in either system. It is that **PIPELINE item 58 describes work that shipped six days ago** — the same failure mode as items 59, 60 and 61. It is currently sitting in the top-of-queue batch. Details in §2.

---

## 1. Status reconciliation

### 1a. Garrison — what is true today

| Capability | State | Where |
|---|---|---|
| Suppression pip row on the building grid | **SHIPPED 2026-08-17** (`97414046`) | `WithGarrisonDecoration.cs:84-88` — `SlotRows = 4`, `SuppressionRow = 3`. Render loop `:325-415`, pip emitted `:392-405`, sequence picked by `GetSuppressionSequence` `:178-190`, bucketing `GetConditionCount` into `pip-suppression-1..10` (`:61-65`). Drawn for shelter occupants too (`:142`). |
| Panel cover % derived, not hardcoded | **SHIPPED** | `GarrisonPanelLogic.cs:189-202` `CoverPercent(Actor)` walks the soldier's enabled `DamageMultiplier` traits, so it tracks `DamageMultiplier@GarrisonCover: Modifier: 20` (`rules/ingame/infantry.yaml:189-191`) automatically. Deliberately not widened to `IDamageModifier` — comment `:191-193` notes `TerrainModifiesDamage` would NRE on a null attacker. |
| `PortState.IsDucking` | **DELETED** | Verified myself: the only occurrence of the token anywhere in `engine/`, `mods/` or `tools/` is the gravestone comment at `GarrisonManager.cs:98` — *"A dead `SuppressionDuckThreshold`/`IsDucking` pair used to sit here inviting exactly that."* Its input `SuppressionDuckThreshold` went with it. |
| Graduated suppression simulation | Live, ten tiers | `^SuppressionEffects` `rules/ingame/infantry.yaml:381-392`; sub-templates speed `:393`, vision `:424`, burst `:455`, burst-wait `:486`, inaccuracy `:517`, pips `:548`. `ExternalCondition@Suppression` `:388` (`TotalCap: 100`, `ReduceTicks: 5`). |
| Forced recall under fire | Live | `SuppressionRecallThreshold = 60` (`GarrisonManager.cs:100`), recall at `:631-635`, port locked `SuppressionLockoutTicks = 50` (`:110`). |
| Re-deploy hysteresis | Live | `SuppressionRedeployThreshold = 30` (`:107`), gates re-deploy at `:466-467`, `:697`. **Note:** the old doc treated "30" as purely vestigial. It is not — 30 is now a real re-deploy gate, just not a fire penalty. |
| Four player-control orders | **STILL UNREACHABLE** | `AssignGarrisonPort` `:1407`, `SwapGarrisonPorts` `:1489`, `SetGarrisonPortTarget` `:1514`, `ClearGarrisonPortTarget` `:1530`. Repo-wide grep returns one hit each — the `case` label. No UI issues any of them. Panel issues only `Unload` (`GarrisonPanelLogic.cs:51`) and `EjectGarrisonPassenger` (`:242`). |
| Garrison notifications / EVA / how-to-play | **STILL ZERO** | No `garrison` match in `rules/sound/notifications.yaml`, `languages/en.ftl`, `chrome/ingame-info-howtoplay.yaml`, `chrome/mainmenu-howtoplay.yaml`. |
| `GarrisonProtection.GetCurrentProtection()` | Exists, resolved | `GarrisonProtection.cs:63-74` — linear interpolation from `CriticalProtection` (30, `:25`) to `BaseProtection` (80, `:22`) by HP%, with `RubbleProtection` (30, `:30`) clamped at HP ≤ 1. |

**Answered open question:** garrison cover does **not** damp suppression. Suppression arrives via `Warhead@Suppression_N: GrantExternalCondition` (`rules/weapons/weapons-effects.yaml:155-379`); `GrantExternalConditionWarhead.DoImpact` calls `ExternalCondition.GrantCondition` directly and never consults `IDamageModifier`. A port soldier taking 20% damage takes **100% suppression**. The old doc suspected this; it is now confirmed by reading the warhead path.

### 1b. Cargo — what is true today

| Capability | State | Where |
|---|---|---|
| Class-grouped unload menu on **J** | **SHIPPED, live** | `CargoUnloadMenuLogic.cs:29`; binding `hotkeys.yaml:54`; layout `mods/ww3mod/chrome/unload-menu.yaml`. Groups by `ISelectable.Class` with actor-name fallback (`:200-204`), one row per class + `x<count>` + an `ALL` chip (`:158-172`). Positioned at `Viewport.LastMousePos`, flips rather than clamps (`:247-263`). ESC closes (`:121-129`); a full-screen `MaskWidget` dismisses on outside click (`:133-139`). |
| 16-row cap | **REMOVED today** (`87ede0ef` → `b7bd4c73`) | Verified myself: `MaxListHeight` has zero hits in `engine/` or `mods/`. The ceiling is now derived from screen height at `CargoUnloadMenuLogic.cs:180-182`. |
| Queued-unload waypoint markers, one per cell | **SHIPPED, live** | Marker is a tile-only `TargetLineNode` at `UnloadCargo.cs:96-101`; sprite from `CargoInfo.UnloadMarkerImage/Sequence/Alpha` (`Cargo.cs:166,170,174`); cell from `Cargo.PredictedUnloadCell` (`:426-433`). Dedup lives in `DrawLineToTarget.TileNodes` (`:118-128`), keyed on `(WPos, Sprite)`. |
| Cargo panel renders at all | **FIXED 2026-08-17** (`c9fdf334`) | The two undefined symbols were **`WINDOW_RIGHT`** and **`WINDOW_BOTTOM`**, renamed out of upstream. `VariableExpression.ParseSymbol` returns 0 on a miss, so the panel painted at X=−240 / Y=−340. Now `ingame-player.yaml:788-789` uses `WINDOW_WIDTH - 240` / `WINDOW_HEIGHT - 112`. Guarded since `a72a88ba` by `CheckChromeIntegerExpressions`. |
| Staggered emergency bail | **SHIPPED 2026-08-13** (`042dbdc4` + follow-ups) | Verified myself: `Cargo.cs:82` `IntraGroupUnloadDelay = 4`, `:87` `InterGroupUnloadDelayMultiplier = 3`, `:100` `EmergencyBailDamageState`, `:108` `AircraftEmergencyBailDamageState`, `:116` `EmergencyBailDelay`. See §2 — the pipeline does not know this. |
| Passenger row as a clickable button | **BUILT, THEN DELETED** | Shipped `eb5e5de0`, removed `7b5c692b` (2026-08-17). `CargoPanelLogic.cs:41-108` at HEAD has no per-passenger rows — only header, hint, Unload-All, supply label, Drop-Supply. |
| "Evacuate" button | Live — **but it is not about passengers** | `Button@EVACUATE` `ingame-player.yaml:333`, logic `CommandBarLogic.cs:244-259`, enabled only for `DeliversCashInfo.Type == "Rotation"` (`:447`). This is supply/reinforcement rotation, not dismounting troops. Anyone reading pipeline 60 as "unload UI" is misreading it. |
| Eject rally points | **ORPHANED — no issuer** | Verified myself. Resolved at `Cargo.cs:406-414`, state at `:560-582`, consumed at `UnloadCargo.cs:174-175` and cleared at `Cargo.cs:1005`. Nothing in the tree issues `SetEjectRally` or `ClearEjectRally` — `EjectRallyOrderGenerator.cs` and the panel's rally buttons were deleted at `7b5c692b`. ~25 lines of trait state plus an activity branch that can never fire. |
| Cargo panel on multi-select | **Vanishes** | Verified myself: `CargoPanelLogic.cs:135` `if (selected.Length != 1) return;`. Select two loaded transports and you are told nothing about either. |

**The unload-menu height arithmetic, worked out.** This is the number the user asked for, derived from code, no launch:

- Row height 22, `ItemSpacing: 1`, `TopBottomSpacing: 0` (`unload-menu.yaml:36,25,24`); list `Y: 19` (`:21`); `ScreenMargin = 4` (`CargoUnloadMenuLogic.cs:36`); `ScrollBar: Hidden` (`unload-menu.yaml:32`).
- `ListLayout.AdjustChild` (`ListLayout.cs:22-30`) gives `ContentHeight = 2T − I + N(22 + I) = 23N − 1`. For N = 24 → **551**, which matches the live measurement recorded in `DISCOVERIES.md:21-22` (`rows=24 content=551 clip=551 panel=574 screen=1224`).
- `CargoUnloadMenuLogic.cs:180-182`: `ceiling = max(22, screenH − 19 − 8)`; `list.H = min(ceiling, 23N − 1)`; `menu.H = list.H + 23`.
- **All 24 rows survive iff `screenH ≥ 578`.** Below that, fully visible rows = `floor((screenH − 27 − 22)/23) + 1`: at 577 you lose ~1, at 480 you lose 5.

Two things soften this. The *menu* never overflows the screen — `PositionAtCursor` clamps y to ≥ 4 (`:257-262`). And the clipped tail is still reachable by mouse wheel, because `ScrollPanelWidget.HandleMouseInput` handles `Scroll` unconditionally (`ScrollPanelWidget.cs:342-346`) regardless of `ScrollBar: Hidden`. So the failure mode below 578px is **"the tail is unadvertised", not "the tail is unreachable"** — materially less bad than the brief feared. I found no enforced minimum window size in the engine, so a user-set small window can still reach that state. **INFERENCE** on that last clause: I read `Settings.cs:204` for the 768 default but did not exhaustively search for a floor.

---

## 2. Things in the docs that are now FALSE

Listed worst-first by cost if acted on.

1. **`PIPELINE.md:389-401` (item 58) — "only the pacing is missing".** **FALSE.** The pacing shipped `042dbdc4` on 2026-08-13, plus six follow-ups. Proof at `Cargo.cs:82,87` (`IntraGroupUnloadDelay`, `InterGroupUnloadDelayMultiplier`) and `:892`/`:938`/`:940` (`EmergencyBailStep`, re-queue via `DelayedAction`). The entry's "Next concrete step" describes code that exists. **This item is in the top-of-queue batch and would burn a worker exactly as 59/60/61 did.** Its genuine residue is a *different* thing: the known side-effect already filed at `bugs/discovered.md:595` (bailing men blocking `Cargo.Killed`'s exits), which is untested.
2. **`HOTBOARD.md:16`** — one bullet carrying three false claims: 58 "only the pacing is missing" (shipped), 60+61 to "run in PARALLEL" (both shipped, `ed5ee6b6`), 59 needing "a primitive that does not exist" (it exists, `Captures.cs:51 CaptureToNeutral`). This line is a pre-batch snapshot that was never retired.
3. **`RESUME-260816-dispatch-pack.md:119`** — states `IsDucking` "is written every tick and read by nothing (`GarrisonManager.cs:641`)" and presents it as the **anchor finding for a dispatch**. False since `97414046`. This is the single most dangerous stale line in the workspace, because it is dispatch-shaped: it would send a worker at a deleted field. Same claim at `RESUME-260816.md:92`. The pack's `:180-181` also call U5 (garrison) and U6 (selective unload) "not started"; both shipped.
4. **`garrison-proposals.md:52`** — "the pip grid renders exactly **three** rows… **There is no suppression row**". False: `SlotRows = 4` (`WithGarrisonDecoration.cs:84-88`). **`:54`** — "No suppression" in the panel, and `(80% cover)` hardcoded. False on both counts (`GarrisonPanelLogic.cs:178-184`, `:189-202`). **`:128`** — proposal #2 "Delete `IsDucking`" is **obsolete, done**.
5. **`garrison-mockup.html:167`** — repeats the `IsDucking` claim. This one is a *user-facing artifact*, so it is the one most likely to mislead the user directly.
6. **`PIPELINE.md:677`** (item 51) — header says `[OPEN — status changed 2026-08-14]` while its own body at `:686` says `DONE 2026-08-19 on wt/supply-oracle`. Header/body contradiction inside a single entry.
7. **`PIPELINE.md:141-142`** (R6) — "~50 garrison/cargo buttons, no tooltip/hotkey". The count predates the command-bar work (`ed5ee6b6`, 30 `Key:` bindings); the cited range now has 7 `Key:` and 13 `TooltipText:`. Premise stale — needs a re-count, not a dispatch.
8. **`RELEASE_V1.md:148`** — the garrison-visuals `[decision]` entry names a trait that does not exist (already flagged at `audit/260816-systems-completeness.md:240`), and its ask (c) "replace green chevron with health-style pips" is substantially delivered by the 4-row grid.

**Credit where due:** `PIPELINE.md:162` was *correctly* updated — it now says "Do not implement a duck-tier fire penalty", which is the right call and prevents the double-penalty bug the old doc warned about. `DISCOVERIES.md:7604-7614` correctly describes `MaxListHeight` in the past tense. Not everything rotted.

---

## 3. Open decisions from `garrison-proposals.md`, resurfaced

The user never ruled on these. Three self-resolved. Three are still live and still applicable.

| # | Proposal | Status now |
|---|---|---|
| #1 | Show suppression in the pip grid + panel | **DONE** (`97414046`) — but never screenshotted, see §5 |
| #2 | Delete `IsDucking` | **DONE** |
| #3 | Name the pinned moment (floating "PINNED", maybe EVA) | **STILL OPEN, still applicable.** The panel now prints `PINNED n`, but the *building grid* cannot express it — all ten `pip-suppression-*` frames are the same 6×3 chevron differing only in hue (`WithGarrisonDecoration.cs:56-60`), so there is no growing-bar cue and no event marker at the moment of recall. There are still zero garrison notifications. |
| #4 | Say "shelter" and "firing position" in words | **HALF DONE.** The hardcoded-80% half is fixed. The vocabulary half is untouched — the panel still prefixes shelter occupants with a bare `[S]` (`GarrisonPanelLogic.cs:227`) and nothing anywhere explains the two-tier model. |
| #5 | Surface the four dead orders | **STILL OPEN, still applicable, still blocked** on the `CachedArmaments` bug — see §4-B1 |
| #6 | A distinct garrison cursor (today it is generic `enter`, `cursors.yaml:105`) | **STILL OPEN, still applicable, still low ratio** |

---

## 4. Ranked proposals

Legibility first, per the user's ruling. Within each list, ranked by perceived improvement per unit of work.

### A. Legibility — what the player can tell about who is inside what

**A1 · The cargo panel shows less than the garrison panel does, for the same act · small · highest ratio**

Today the cargo panel is a header, a hint, and an Unload-All button (`CargoPanelLogic.cs:41-108`). The garrison panel next door lists every occupant with ammo, cover %, and a per-occupant eject `X`. Both answer "who is inside this thing" and only one of them does. The passenger-row affordance *existed* and was deleted at `7b5c692b`; the deletion's own rationale (`CargoPanelLogic.cs:19-23`) argues the menu is the better home for it — **but that argument applies equally to the garrison panel, which was not changed.** So the two siblings now disagree about their own design principle.

- **Player sees:** the loaded Chinook's contents without opening a modal over the battlefield.
- **Decision needed from the user, not from me:** converge *up* (restore rows in cargo) or converge *down* (strip the garrison panel to a hint + Eject All and make `J` work for garrisons too). Either is defensible; having both is not.

**A2 · The cargo panel vanishes on multi-select · small**

`CargoPanelLogic.cs:135` bails on `selected.Length != 1`. Select two loaded APCs and you are told nothing about either — not even a total. The `J` menu has the same restriction (`:90-97`). Multi-select is the normal way to handle a transport group, so the panel is absent exactly when the player has the most units to keep track of.

- **Player sees:** an aggregate ("3 transports · 14 troops") instead of an empty corner.

**A3 · Two verbs for one act · trivial**

Garrison says **"Eject All"**; cargo says **"Unload All Troops"**; the hotkey is **UnloadMenu**; the order is `EjectGarrisonPassenger` on one side and `UnloadCargoPassenger` on the other. Pick one word and use it everywhere the player can read it.

**A4 · Say "shelter" and "firing position" in words · trivial** *(the surviving half of old #4)*

`[S]` (`GarrisonPanelLogic.cs:227`) is not readable by a stranger. Nothing anywhere states the two-tier model — no how-to-play entry, no tooltip. Six soldiers go in and two appear at windows for reasons never given.

**A5 · The panel hides cover to show suppression · trivial**

`GetPortText` swaps the cover figure *out* for the suppression figure when suppressed (`GarrisonPanelLogic.cs:180-183`). Intentional — the commit says it keeps the row width stable — but it means the moment cover matters most is the moment you can no longer see it. Worth a second look now that the row exists.

**A6 · Name the pinned moment · small** *(old #3, unchanged and still applicable)*

The recall at suppression 60 (`GarrisonManager.cs:631-635`) is still silent. The panel's `PINNED n` is only visible if the panel is open. A floating marker over the building, or the first garrison EVA line in the game, would give the most confusing garrison event a cause. Needs a per-building cooldown — a 4-port tower under fire would spam it.

**A7 · The suppression pips cannot show a trend · small**

All ten frames are the same chevron in different hues (`WithGarrisonDecoration.cs:56-60`), so the grid conveys severity but not direction or proximity-to-recall. A filling bar would predict the recall a beat before it happens; that prediction is most of the value the readout was supposed to deliver.

**A8 · UI strings are hardcoded English C#, not Fluent · small, mechanical**

`CARGO [n troops]`, `[S] {name}`, `PINNED n` are all literals, unlike the rest of the chrome. The `J` hint is the one thing done right — it reads `modData.Hotkeys["UnloadMenu"]` (`CargoPanelLogic.cs:65,69`) rather than hardcoding the letter, and is the pattern the rest should follow.

**A9 · A distinct garrison cursor · small · low ratio** *(old #6)* — still the generic `enter` (`cursors.yaml:105`), identical to boarding a transport.

### B. Behaviour — needs a test and a run

**B1 · `SwapGarrisonPorts` fires through the wrong soldier's armaments · small fix, blocking for A-tier port UI**

Verified by reading `GarrisonManager.cs:1489-1512`: the handler swaps `DeployedSoldier` (`:1500,:1502`) and `ConditionToken` (`:1501,:1503`) and resets targeting, but **never touches `CachedArmaments`** — which is the field the firing path actually reads (`:858`). After a swap each port would fire through the other soldier's weapons. It is latent only because nothing issues the order. `AssignGarrisonPort` handles it correctly at `:1447-1449`, in the same file, so this is an asymmetric omission rather than a design choice. **Fix this before anyone exposes the port orders.**

**B2 · Reopening the unload menu cancels queued unloads · small**

Read, not run. `Open()` resets `hasDropped = false` (`CargoUnloadMenuLogic.cs:103`); `Drop` sends `queued: hasDropped` (`:236`) and latches it true (`:241`). The comment at `:49-51` states plainly that sending `queued:false` twice "would `CancelActivity()` the unload that…". So: drop three men → ESC → press J → drop again, and the second order arrives unqueued and cancels the first three. The latch exists precisely to prevent this and does not survive a close/reopen. **The code path is verified; the in-game consequence is not** — see §5.

**B3 · Decide the fate of eject rally points · trivial either way**

Fully wired at both ends, issued by nothing (`Cargo.cs:406-414`, `:560-582`, `UnloadCargo.cs:174-175`). Either give it an issuer or delete it. Leaving orphaned order-handling in a synced trait is how the next reader concludes a feature works.

**B4 · Surface the four garrison orders · medium-to-large** *(old #5, unchanged)*

Real control — put the AT soldier on the north port, aim that port at that tank. Blocked on B1. Also fights the auto-deploy loop: `IdleRecallTicks` (250, `:66`), `SuppressionRedeployThreshold` (`:107`), `RedeployBlackoutTicks`. `PlayerOverride` is honoured for targeting only (`:1479-1484`, `:1521`), so a hand-placed soldier being auto-recalled 250 ticks later would feel worse than no control at all.

**B5 · Design question: should garrison cover damp suppression? · needs a ruling first**

Confirmed in §1a: it does not. A soldier behind 80% cover absorbs 100% of the suppression. That means the recall threshold fires far more often than a reader of the cover number would expect, and it makes the new readout *more* valuable, not less. **This is a balance decision, not a bug** — I am flagging it, not proposing a value.

**B6 · The staggered bail has no test**

Item 58's shipped mechanism (`Cargo.cs:892`, `:938-940`) has no autotest, and its known hazard — bailing men blocking `Cargo.Killed`'s exits, `bugs/discovered.md:595` — is untested. This is the one genuine test gap in the area. Existing coverage is otherwise good: `test-unload-menu`, `test-unload-menu-classes`, `test-unload-menu-pacing`, `test-unload-marker-stacking`, `test-unload-queued-after-waypoints`, `test-garrison-suppression-readout`, `test-evac-suite`, `test-ferry-fills-seats`, `test-field-heli-unload`, `test-spread-cargo-no-enter`, `test-sr-evacuate-cursor`, `test-supply-safe-front-keeps-cargo`.

### C. Housekeeping — no player-visible effect

- `LogicTicker@CARGO_TICKER` (`ingame-player.yaml:793`) is dead; `CargoPanelLogic` moved to `panel.IsVisible` (`:112-116`). Its sibling `GARRISON_TICKER` *is* live (`GarrisonPanelLogic.cs:110-118`) — two sibling panels using two mechanisms for one job.
- `mods/ww3mod/chrome/garrison-panel.yaml` is a byte-identical duplicate of `ingame-player.yaml:629-785`, absent from `mod.yaml`'s ChromeLayout — invisible to the game *and* to `CheckChromeIntegerExpressions`. Already recorded at `bugs/discovered.md:1975`; still not deleted.
- `GarrisonProtection.cs:28` — the `[Desc]` says `RubbleProtection` is "Lower than CriticalProtection"; both default to 30 (`:25`, `:30`). Comment or value is wrong.
- `hotkeys.yaml:51` cites `UnloadCargo.cs:69` for the shuffled exit-cell pick; that line is an assignment. Real code is `ChooseExitSubCell` at `UnloadCargo.cs:104`.
- `Cargo.cs:297-305` — commented-out `PickUpClosestActors` stub, empty body, typo, no callers.
- `MountedTransportBotModule.cs:891` still issues `"UnloadCargo"`, a string matching no `ResolveOrder` case. Dead on both bot profiles by config, but one YAML edit from being re-enabled into a silent no-op.
- The panels geometrically overlap (GARRISON `Y: WINDOW_HEIGHT-260 H:240`, CARGO `Y: WINDOW_HEIGHT-112 H:92`, same X). Harmless only because `CargoPanelLogic.cs:145-146` makes them mutually exclusive.

---

## 5. MANAGER: please run this

Collected so one launch grant covers the lot. Nothing here was run by me — the constraint is launches are serialized through the manager.

**M1 · The garrison suppression readout has never been seen.** Commit `97414046` states in its own message: *"Screenshots not yet captured."* `DOCS/recipes/SCREENSHOT.md` applies by default to visual work. This is the highest-value item in this list — a shipped, unverified visual feature.
- **Scenario:** `tools/autotest/scenarios/test-garrison-suppression-readout/` already exists.
- **Look at:** does the 4th pip row appear on the building; do the hue tiers read as escalation at gameplay zoom; does the row crowd a small building at 10 occupants; does the panel's `PINNED n` appear before the soldier vanishes inside.
- **Answer counts as:** a screenshot of a garrisoned building under fire at ≥ suppression 60.

**M2 · The 24-class unload menu at a small window.** Confirmed at 1224px only (`DISCOVERIES.md:21-22`). My arithmetic says the cliff is at **screenH = 578**, and that below it the tail is clipped but still wheel-scrollable.
- **Look at:** run `test-unload-menu-classes` at a window ~560px tall. Does the list clip at the bottom with no scrollbar? Does the wheel still reach row 24?
- **Answer counts as:** a screenshot at ~560px plus a wheel-scroll to the last row. If the wheel reaches it, this drops from "silent data loss" to "cosmetic".

**M3 · Reopen-cancels-queued-unloads (B2).** Repro without a scenario file: load a transport, press J, click a class, ESC, press J again, click another class. Do the first men still dismount, or did the second order cancel them?
- **Answer counts as:** watching whether the first group completes its unload.

**M4 · Can a garrisoned soldier be click-selected?** Open since `garrison-proposals.md:196-200`. `Selectable` (`rules/ingame/infantry.yaml:57`) has no `RequiresCondition`, but the 40%-alpha ghost stands on the building's own cell. Less important now that the grid shows suppression, but it decides whether the soldier's own pips (`:548`, `RequiresSelection: true`) are reachable at all.
- **Answer counts as:** left-click a ghost sprite at a window on a garrisoned `GTWR` and screenshot whether pips appear.

**M5 · Supply-truck halt play-feel.** Standing want, carried over; no new information from this pass.

---

## 6. What I think is wrong — opinion, clearly labelled

Everything above §6 is measured. This is judgement.

**Cargo lost a fight it should have won.** The passenger-row affordance was built, shipped, and deleted inside about ten days, and the thing that replaced it is a modal that covers the battlefield and can only be opened for exactly one selected transport. The reasoning in `CargoPanelLogic.cs:19-23` is coherent on its own terms — but it was applied to one of two sibling panels, so the codebase now holds two contradictory answers to the same design question and no record of which one won. **That, more than any single missing feature, is what makes this area feel unfinished.** A player who learns the garrison panel is actively misled about the cargo panel, and vice versa.

**Garrison's remaining problem is no longer rendering — it is vocabulary.** Three days ago the honest answer was "the simulation is invisible". That got fixed. What is left is that the words are wrong or absent: `[S]`, two verbs for one act, zero notifications, no how-to-play line, no cursor of its own. That is a much cheaper problem than the one it replaced, and it is almost entirely strings.

**The documentation is the actual liability.** I found eight false statements across five workspace files, one of them in a user-facing HTML mockup and one of them shaped like a dispatch brief. In the same week, four pipeline items turned out to describe already-merged work. The pattern is consistent: **the fix lands, the fixer updates the code and the commit message, and nothing updates the queue.** Item 58 is sitting in the top-of-queue batch right now with a false header. I would rank re-reading the top of `PIPELINE.md` against `git log` above any of the proposals in §4 — it is the cheapest thing on this page and it is the one that has repeatedly cost a full worker dispatch.

**One thing I would not do.** Do not wire the four garrison orders yet. The sim half is written, which makes it look like a cheap win, but `SwapGarrisonPorts` is quietly wrong (B1), hand-assignment fights three separate auto-deploy timers, and `PlayerOverride` only covers targeting. It is a medium-to-large piece of work wearing a small piece of work's clothes.
