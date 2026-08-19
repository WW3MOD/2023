# PIPELINE — living roadmap

> **This is the living roadmap, and it holds LIVE WORK ONLY.** The queue reads strictly top-to-bottom in execution order: the top item is the **next thing to start**, everything under it follows in order. The manager re-evaluates ordering every time an item is added or finishes. **You steer by reordering lines, commenting, or striking items** — say the word and the order changes.
>
> **Every item is framed by "How will this be perceived in the game?"** — what the player or a watching viewer actually sees change. Technical notes are secondary: one line, with doc/commit refs.
>
> Source-of-truth for scope stays `RELEASE_V1.md`; what's-in-motion stays `HOTBOARD.md`; this file is the ordered plan of attack.

## How this file is split — read this before you go looking for something

Each queue entry below is a **stub**: what it is, what the player would see change, its status, and a link. The full dossier — mechanism, file:line evidence, refuted hypotheses, traps, "do not re-propose this" rulings — lives in its own file under [`pipeline/items/`](pipeline/items/). **Read the stub to choose the work; read the dossier once you have chosen it.** Nobody should ever need to read all of them.

| You want… | Go to |
|---|---|
| The next thing to start | the queue below, top down |
| Everything known about one item | `pipeline/items/<NN>-<slug>.md` — linked from its stub |
| Why a finished item was done the way it was; a vocabulary ruling; a "do not re-propose" | [`pipeline/archive/closed-items.md`](pipeline/archive/closed-items.md) |
| What shipped, in order | [`pipeline/archive/shipped-log.md`](pipeline/archive/shipped-log.md) |
| Dated session snapshots, batch framing, and the reusable **method notes** | [`pipeline/archive/session-notes.md`](pipeline/archive/session-notes.md) |
| How to add, close or split an item | [`pipeline/README.md`](pipeline/README.md) |

**Archiving is not discarding.** Closed items keep their full text — item 58's ruling that *"critical damage" means `DamageState.Heavy`, not the `critical-damage` condition*, and its grep trap, are still load-bearing for anyone touching `Cargo.cs` and are preserved verbatim in `closed-items.md`. Search the archive before concluding something was never considered.

> **Line-number references are broken by this split.** Anything written before 2026-08-19 that cites `PIPELINE.md:NNN` (several `WORKSPACE/audit/*`, `cargo-garrison-status-260819.md`, `garrison-proposals.md`, `lobby/UX_REVIEW_260819.md`, `scoping/neutralise-capture.md`) points into the pre-split file. Those are dated snapshots and were deliberately left unedited per the standing rule that a dated record gets superseded, not rewritten — resolve them against `git show de78a1ed:WORKSPACE/PIPELINE.md`. **Item-number references (`PIPELINE item 40`) are unaffected: item numbers are stable and never reused.**

---

## RELEASE AUDIT 2026-08-16 — framing, ranking function, and operating rules **[BINDING — user-answered, do not re-ask]**

> **What this block is.** On 2026-08-16 the user opened a release push: *"audit the whole project and find whatever is not working or polished enough… put everything in the pipeline and keep the priority updated as you go."* Explicitly a **discussion phase first** — nothing is implemented until the user gives a goahead, after which the manager works autonomously. Audit started at **main @ `55459146`**, clean and in sync with origin.
>
> Four framing questions were put to the user and answered. **The answers below are the ranking function for every item in this file from here on**, and they reorder the queue substantially.

### 1. Audience: **PUBLIC RELEASE TO STRANGERS** (itch / ModDB / Discord-wide)

First impression decides everything; a stranger who bounces in the first ten minutes never comes back. **This promotes a whole class of items from polish to blocker:** missing audio, placeholder or RA-era art, any surviving Red Alert identity string, and a first match a new player cannot make sense of. **It also makes the unresolved 2-human multiplayer desync (item 42) a hard blocker** — it was only tolerable under the friends-and-testers reading, which the user rejected.

### 2. Bot quality: **between "credible" and "not embarrassing"**

The user picked both middle options, noting *"Somewhere in between, I hope we can make it a bit better."* The rule this yields:

- **HIGH:** visibly-stupid bot behaviour a player would screenshot — the lone tank pushing alone, soldiers standing around out of ammo, supply trucks never bought. These are the live-play reports already in the queue (items 63/64/66) and they stay near the top.
- **NOT RELEASE-GATING:** the deep architecture — danger-scale rework stage (c) (item 40), the `@stable` benchmark re-baseline (item 43), coordination architecture beyond the visible symptom. Wanted, not blocking. **Cheap incremental improvement is in scope; open-ended rework is not.**

### 3. Compute and simulation authority: **THE MANAGER OWNS IT**

User verbatim: *"Running simulations takes a lot of computing power… you will have to keep track of the budget and see if you are on pace to finish, and if so you can allocate some time/compute to simulations when necessary, but if every worker/submanager starts running simulations then it will be chaos. So it is entirely up to you to decide how to manage it. You will be in charge (after I give you the goahead on implementing, for now during the audit dont launch any simulations)."*

Binding sub-rules:
- **No simulations, autotests, batches or game launches during the audit phase.** Full stop.
- After the goahead, **simulation authority is centralised in the manager. A worker never self-authorises a run** — it asks, or it is dispatched with the run already sanctioned in its brief. This supersedes nothing in the no-autonomous-multi-test rule; it adds a second gate on top of it.
- The manager tracks budget pace and spends surplus compute on measurement **only when on pace to finish** the committed work.

### 4. Scope: **"Everything now in game should be made to work"**

The user's own framing: *"Some things are already disabled, some units etc, those can just be left as they are, players won't notice them."*

**The axis is VISIBILITY, not completeness.** If a player can see it or touch it, it must work. Already-disabled content (hidden units, the shelved airstrike support powers) **stays disabled and gets zero effort** — do not re-enable, do not polish, do not audit further. **Hiding a currently-VISIBLE broken thing to dodge the work is not the assumed move** — that is a decision to put to the user, not a shortcut to take.

### Severity ladder used by every audit finding filed under this push

| Level | Test |
|---|---|
| **BLOCKER** | A stranger's first session hits it: crash, softlock, desync, unplayable match, or an immediately-visible "this is unfinished" signal (silent weapons, missing cameo, Red Alert text) |
| **SHOULD-FIX** | Noticed within the first few matches; makes the game feel rough but not broken |
| **POLISH** | Noticed by an engaged player; absence reads as missing depth, not as a defect |
| **COSMETIC** | Noticed only if looked for |

### Budget note (2026-08-16, audit start)

Seven-day window at **82% used, resetting in ~3.6h**; five-hour window at 17%. **Practical consequence: the audit phase is read-heavy and cheap, so it fits inside the tail of the current window; any simulation-heavy measurement should be scheduled AFTER the seven-day reset**, not before it.

---

---

## RELEASE AUDIT — RANKED FINDINGS **[LIVE LIST — grows as audit reports land]**

> **This is the release list.** It is ranked by the function in the block above: what a stranger encounters, how early, and how visibly — cost only as a tiebreak. Items are numbered `R#` and **the numbers are stable**; priority is the ORDER, so items move up and down and their numbers do not change.
>
> **Nothing here is being implemented.** The user's instruction is explicit: audit and discuss first, implement on an explicit goahead.
>
> **Audit status:** wave 1 = build/test health, first-run chrome ✅, content completeness, bug reconciliation, systems completeness. Wave 2 = install/packaging (running), netcode, crash sweep, maps, performance. Full reports under [`WORKSPACE/audit/`](audit/).

### DEFERRED TO FINAL PRE-RELEASE POLISH **[user ruling 2026-08-16 — do NOT do these now]**

> **The user's correction to the plan, verbatim:** *"it doesn't need to be fully releasable after you are done in this session… don't be too eager to do it all now, there is still a lot of work… at a later point we will do the final polish to make it fully releasable, and that is the time to do that kind of thing."*
>
> **This is a real ordering principle, not a delay.** A polish item done now gets re-done later anyway, because the thing it polishes is still moving. Anything below is correct work at the wrong time.

| Item | Why it waits |
|---|---|
| **R4 — lobby AI opponent names** ("Experimental AI" / "Stable AI 0802") | **The bots are still under development, and the stable-vs-experimental split is actively useful while that is true.** Renaming now would remove a working development affordance to buy presentation the release does not need yet. Revisit when bot work stops. |
| **U9 — art + audio TODO lists** | User owns this work and it needs a lot of their attention: *"you can skip it fully now… just document it as a standing todo pre-release."* The content-completeness audit's report **is** that standing document — write it, then stop. |
| **U4 — command bar icon placeholders** | Same ruling. The duplicate-map table (19 of 25 buttons share art across 11 sprites; **14 new icons needed**) is the deliverable; generating placeholder glyphs is not wanted. |

**Standing rule extracted from this ruling, for whoever picks the queue up:** before doing any item whose value is *presentational*, ask whether the thing it presents is still changing. If it is, the item belongs here, not in the active queue.

### SCOPE RELIEF — three headline systems are DONE and the tracker says otherwise

The systems audit expected to find the `ForwardStaging` failure mode repeating (a feature that ships structurally unreachable and stays inert). **It does not repeat anywhere in this slice** — every system traced to a reachable path on actors that exist in shipped rules. `RELEASE_V1.md` errs in the *opposite* direction and understates three systems:

| System | Tracker says | Actually |
|---|---|---|
| **Stance rework (4 phases)** | `[ ]` open | **LIVE AND COMPLETE** — all four modifier axes plus patrol are wired |
| **Supply Route contestation** | `[ ]` open | **LIVE AND COMPLETE** — control bar, production slowdown and notifications all ship |
| **Three-mode move system** | `[ ]` open | **LIVE AND COMPLETE** |

`RELEASE_V1.md` should be corrected. Under the "everything visible must work" rule these three are **not release work at all** — which removes a large, intimidating block from the middle of the tracker and is the single biggest piece of good news in the audit so far.

### BLOCKERS — a stranger hits these in the first session

> ⚠️ **PREMISE CHECK 2026-08-19 (`main @ de78a1ed`) — LOOKS ALREADY SHIPPED. `MissionBrowserLogic.cs` now filters `p.Class != MapClassification.Unknown` on the loose-missions path, with a comment naming this exact defect ("without the same filter here that folder was the one UI a player could reach it from"). The `missions.yaml` stock-campaign half was not re-checked. **Verify before dispatching.****

**R1. The Missions button opens a list of 175 internal test scenarios.** *(source: `audit/260816-first-run-chrome.md`)*
**Perceived:** a new player clicks Missions expecting a campaign and gets `test-supply-far-front-reached`, `demo-heli-lanes` and 173 more internal artefacts, above two empty campaign groups labelled Allied and Soviet.
The mechanism is verified: `MissionBrowserLogic.cs:183-187` builds its "loose missions" group filtering **only** on `Status == Available` and `Visibility.HasFlag(MissionSelector)` — **there is no class filter on that path.** So the comment at `mod.yaml:93` — *"Class=Unknown hides them from every UI tab (lobby, missions, main-menu chooser)"* — **is false for the mission browser**, and has been believed by every session since it was written. Compounded by `missions.yaml` still holding the stock Red Alert campaign list (`allies-01`…`soviet-11b`), which is also what keeps the Missions button enabled at all (`MainMenuLogic.cs:371`).
_Correction to note: the false claim lives in `mod.yaml`'s comment, **not** in `CLAUDE.md` — the audit report's headline says both; the grep says one._
**Size:** minutes.

> ⚠️ **PREMISE CHECK 2026-08-19 (`main @ de78a1ed`) — LOOKS ALREADY SHIPPED, and deliberately so. `mods/ww3mod/languages/en.ftl:39` now overrides the string to "…help us optimize the OpenRA engine that WW3MOD runs on", with a comment at `:36` recording that it was deliberately NOT reworded to "optimize WW3MOD" because the payload really does go upstream. That is a decision, not an oversight — **do not re-flip it.****

**R2. The second screen a new player ever sees asks them to "help us optimize OpenRA".** *(chrome)*
**Perceived:** the first-run consent dialog names a different product than the one they just installed.
`chrome.ftl:269`, not overridden in the mod's `en.ftl`. Its sibling title *was* re-themed to "Establishing Battlefield Control" — the branding pass stopped one line short, which is the tell that there are more like it. **Size:** minutes.

> ⚠️ **PREMISE CHECK 2026-08-19 (`main @ de78a1ed`) — LOOKS ALREADY SHIPPED. Both maps now read `Visibility: Shellmap` (`arena-tank-duel/map.yaml:15`, `shellmap-open-field/map.yaml:15`) — the `Lobby` flag this finding is built on is gone, so neither appears in the lobby map list. **Verify before dispatching.****

**R3. Two developer maps ship as playable Conquest maps and cannot be won.** *(chrome)*
**Perceived:** a player picks a map from the lobby list, starts, and has no Supply Route and no victory condition — an unwinnable, unexplainable match.
`arena-tank-duel` (`Author: Combat Sim`) and `shellmap-open-field` are both `Visibility: Lobby, Shellmap`, and their `rules.yaml` strips `-ConquestVictoryConditions` and `-SpawnStartingUnits` — the latter is what places the Supply Route. **Size:** minutes.

**R4. The lobby's only AI opponents are "Experimental AI" and "Stable AI 0802".** *(chrome)*
**Perceived:** in the one menu every single-player passes through, the opponent picker offers a lab name and an internal build date, with no difficulty ladder and no descriptions.
`ai.yaml:44-51`. **Note this collides with the bot ranking rule:** it is chrome, not bot intelligence, so it is cheap and it is a blocker — the bot can stay exactly as good as it is today and this still needs fixing. **Size:** minutes for naming/descriptions; a real difficulty ladder is larger and is a separate decision.

### SHOULD-FIX — noticed within the first few matches

> ⚠️ **PREMISE CHECK 2026-08-19 (`main @ de78a1ed`) — HALF SHIPPED, and the finding needs splitting. The `ProductionType*` half is DONE — `mods/ww3mod/hotkeys.yaml` now binds Infantry/Vehicle/Aircraft to **Y/U/I**, with a comment explaining that E/R/T were taken by Evacuate/Resupply/ShowTerritory. The **`SupportPower01..06` half still holds**: all six remain declaration-only in `engine/mods/common/hotkeys/supportpowers.yaml` with no key, so support powers really are mouse-only. Re-scope this finding to the support-power half.**

**R5. Sidebar tab hotkeys are all unbound, and ~35 dead Red Alert bindings remain.** *(chrome)*
**Perceived:** the keyboard barely works. All six `ProductionType*` defaults are empty at `hotkeys.yaml:1-29` with the intended keys sitting in comments (`# E`, `# R`…), likely collateral from adding `ShowTerritory: T`. Meanwhile `Production01..24` consumes all of F1–F12, and **`SupportPower01..06` are unbound — support powers are mouse-only.** **Size:** hours. Supersedes and widens PIPELINE item 61.

> ⚠️ **PREMISE CHECK 2026-08-19 (`main @ de78a1ed`) — COUNT IS STALE — independently flagged by `WORKSPACE/cargo-garrison-status-260819.md:74` and confirmed here. The cited range `ingame-player.yaml:623-1135` now carries **12 `TooltipText:` and 6 `Key:`**. The command-bar work (`ed5ee6b6`) landed after this finding was written. **This needs a re-count, not a dispatch.****

**R6. ~50 garrison and cargo buttons have no tooltip and no hotkey; several are labelled just `X`.** *(chrome)*
**Perceived:** an unexplained wall of buttons, some with a single letter on them. `ingame-player.yaml:623-1135`. **Size:** hours. Widens PIPELINE item 60.

**R7. The install chain identifies the product as OpenRA.** *(chrome; wave-2 install audit is going deeper)*
**Perceived:** install dir `OpenRA WW3MOD`, registry key `OpenRAWW3MOD`, Start Menu folder `OpenRA`, `<Product>OpenRA</Product>`, and the crash dialog's FAQ button opens `wiki.openra.net`. **Size:** hours. Discord rich presence needs a WW3MOD app id and is not fixable in-repo.

> ⚠️ **PREMISE CHECK 2026-08-19 (`main @ de78a1ed`) — ALREADY SHIPPED — both halves. `75ac6941` ("Write the faction descriptions and fix the Random Side string") filled America, Russia and Random Side in `world.yaml`, and `1c30bef7` (2026-08-19, one commit before this split) unescaped the `\n` so the tooltip bodies actually render. **Strong candidate for closing outright.****

**R8. Faction descriptions are blank, and Random Side offers "a random vanilla side".** *(chrome)*
`world.yaml:242-253` — `Description: America` with nothing after it. **Perceived:** the faction picker teaches a new player nothing about the two sides the whole game is built on. **Size:** minutes to fill, longer to write well.

> ⚠️ **PREMISE CHECK 2026-08-19 (`main @ de78a1ed`) — LOOKS ALREADY SHIPPED. `ProductionTooltipLogic.cs` no longer computes `Ammo * SupplyValue`; it sums `p.PoolBudget`, and `AmmoPool.cs:69` defines `PoolBudget => BatchCount * SupplyValue` — the same expression the per-pool lines print at `:97-98`. Total and per-pool lines now agree **by construction**, which is exactly what this finding asked for. **Verify the displayed number before dispatching.****

**R11. The production tooltip's ammo total is wrong by up to 100×.** *(systems)*
**Perceived:** the first number a player ever reads about this mod's economy is nonsense — a Bradley costing 1500 shows **"Total ammo cost: 5100"**, while its own two per-pool lines directly above say 45 and 600. The true total is 645.
`ProductionTooltipLogic.cs:213` computes `Ammo * SupplyValue` and **omits `ReloadCount`**, which the per-pool lines immediately above it *do* apply (`AmmoPool.cs:90-96`). 645 is exactly the ~43% of unit cost that `economy.md:167` targets, so the spec is right and the display is wrong. This is verify-point 4 of the economy overhaul's never-confirmed checklist. **Size:** one line.
_Filed as SHOULD-FIX by the auditor; **promoted to blocker** because under a public release the economy tooltip is a first-session, first-impression surface and the error is visible without any special play._

**R12. A supply truck cannot replenish a dropped supply cache — the loop is a dead end.** *(systems)*
**Perceived:** the player drops a cache, tries to top it up with another truck, and gets no cursor and no order — nothing happens and nothing explains why. On the seven maps with no Logistics Centre, a truck is the *only* route by which ground supply returns, so the economy has a hole in it.
`DropsSupplyCache.cs:705` requires the target to carry `AbsorbsSupplyCache`, which **only `logisticscenter` has**. This is the item already flagged urgent at `RELEASE_V1.md:52`. **Size:** hours.

### SHOULD-FIX — noticed within the first few matches

**R14. Capturing a helicopter by pilot entry yields a burning wreck that explodes in ~12 seconds.** *(systems)*
**Perceived:** the player pulls off the capture — a genuinely cool mechanic — and the prize is speed-zero, firepower-zero and on fire. The recovery gate at `HeliEmergencyLanding.cs:411-416` **can never be satisfied**, because the repair traits it depends on were deleted in the 260509 design reversal. Either the reward works or the mechanic should not be presented. **Size:** hours; needs a design call first.

**R15. Vehicle commander substitution was never built.** *(systems)*
**Perceived:** a tank that loses its commander is permanently degraded with no way to recover, and nothing tells the player that. Ejection and re-entry both work; there is no promotion path in `VehicleCrew.cs`. **Size:** unknown — this is a feature, not a fix.

**R16. Cargo Phase 2D is sync-unsafe beyond R10, and Phase 3 was never built.** *(systems)*
Tracked separately from R10 so the desync fix is not confused with the phase's remaining scope. **Size:** unknown.

### POLISH

**R9. The onboarding panel overstates Supply Route contestation.** *(chrome)*
It says losing the Route "puts them out of the match"; the shipped mechanic makes a player **passive and reversible** (`SupplyRouteContestation.cs:354-373`). Verified accurate otherwise — its Supply Route claims check out against `structures.yaml:202-273`. **Size:** minutes.

---
---

## QUEUE

> **Order is execution order.** One known inconsistency is preserved rather than silently resolved: item **56** carries the tag *"HIGHEST PRIORITY IN THE WHOLE QUEUE — above item 40"* while item **40** sits above it here, and 40's own header agrees it was displaced. **That is a live ordering question for the user, not a transcription error.**
>
> **⚠️ Standing hazard, and it is this queue's most expensive recurring defect.** In the week to 2026-08-19, **five items were found to describe already-merged work**; two of them cost a worker dispatched at nothing. Entries tagged `[IN FLIGHT]` have twice outlived their own merge. **Before dispatching anyone, spend one `git log -S <symbol>` or one grep on the item's central premise.** Stubs carrying a ⚠️ below already failed that check once.

### Current user priorities — 2026-08-15 live-play batch

Framing for this batch (why 63/64 are not one item, and what 65 has to do with either) is in [`archive/session-notes.md`](pipeline/archive/session-notes.md). Items **63** and **66** from this batch are merged and archived to [`closed-items.md`](pipeline/archive/closed-items.md) — 66's *procurement ordering axis* dossier is still the reference for the unfinished lobby-verification arm.

### ⚠️ 64. Coordinated combined-arms push — the first tank attacks alone
`[TAGGED IN FLIGHT; BRANCH IS MERGED]`
**Perceived:** the opening push looks like a formation instead of a lone vehicle. Armour leads, a transport carrying infantry and a technician follows behind it, and the infantry arrive at the front protected rather than walking up on their own.
`wt/combined-arms` (`7c692ced`) is an ancestor of `main`. The recon that reframed this item — armour and mounted infantry compute destinations with *different arithmetic that cannot see each other* — is the durable content. → [`items/64-combined-arms-push.md`](pipeline/items/64-combined-arms-push.md)

### ⚠️ 65. Field actors swallow artillery shells
`[TAGGED IN FLIGHT; BRANCH IS MERGED]`
**Perceived:** artillery fired at infantry standing in a crop field kills them. Today the shell lands and simply vanishes — no damage, no explosion, no sound.
`wt/field-impact` (`03ffcaa7`) is an ancestor of `main`, and the "prior art to check" branch it names is titled *"ground cover stops blocking every non-movement cell check"*. → [`items/65-field-actors-swallow-shells.md`](pipeline/items/65-field-actors-swallow-shells.md)

---

### 40. Danger-scale rework — stop the bot treating ordinary ground as lethal
`[stage (a) DONE ddcc5d6c; stage (b) instrument landed; stage (c) OPEN and is now the whole item]`
**Perceived:** the bot stops flinching at nothing. Supply trucks actually deliver instead of driving part-way and turning back; units stop refusing to advance across terrain that is not in fact dangerous.
All 18 ground thresholds (plus 1 air) sit 8×–459× below the live median cell of the field they gate. **Gates the `@stable` benchmark re-baseline (item 43), which in turn gates 22, 25, 31 and ambush gate (b)** — nothing downstream of a measurement moves until the instrument is right. Moves BOTH bot profiles by construction; no seam exists to hide behind. → [`items/40-danger-scale-rework.md`](pipeline/items/40-danger-scale-rework.md)

---

### Current user priorities — 2026-08-13 live-play batch

Batch framing in [`archive/session-notes.md`](pipeline/archive/session-notes.md). Items **58, 59, 60 and 61** from this batch are all shipped and archived — **58's vocabulary ruling and grep trap, and 61's `TAKE_COVER` dead-button analysis, are still live guidance** in [`closed-items.md`](pipeline/archive/closed-items.md).

### 56. Supply trucks still do not commit to a delivery
`[the item's own tag reads: HIGHEST PRIORITY IN THE WHOLE QUEUE — above item 40]`
**Perceived:** a supply truck drives to where supplies are needed, drops its supply, and leaves. Today it goes back and forth and never commits.
Declared fixed to the user at least three times. **The user has pre-authorised the blunt fix** — disabling danger awareness for trucks entirely is explicitly acceptable. **A green scenario does NOT close this item**; the acceptance bar is a full bot-vs-bot match on a real map, with an added precondition clause so "no truck was ever bought" reads as instrument failure rather than a negative result. Disabling danger awareness is **seven sites, not one seam**, and one of them reads a different field that no config flag reaches. → [`items/56-supply-truck-delivery.md`](pipeline/items/56-supply-truck-delivery.md)

### ⚠️ 57. Bot build composition — one item, three symptoms, same subsystem
**Perceived:** the bot opens with combat units instead of two idle supply trucks, medics appear, and there is always an AA soldier or two on the field.
**Both confirmed sub-items have been overtaken by later work** — the truck floor now has a denominator and the unit floors now exist. Only (c), the AA half, plausibly survives as filed. Re-scope before dispatching. The durable finding — that a standing-population floor and a capture-demand floor are different things, and a third floor on the same population has its winner decided by module ordering — holds regardless. → [`items/57-bot-build-composition.md`](pipeline/items/57-bot-build-composition.md)

---

### Close-out intake — items 42–54 and 62

Thirteen items folded in from nine archived manager sessions on 2026-08-12, then reconciled against a later `main` on 2026-08-13. **The reconciliation table, its per-item verdicts and the method note it earned** ("four items were confirmed OPEN by an EMPTY path-scoped log rather than by argument") are in [`archive/session-notes.md`](pipeline/archive/session-notes.md). Items **47, 50, 51, 52** are closed or retired and live in [`closed-items.md`](pipeline/archive/closed-items.md) — **50 and its sibling 17 were DECLINED by the user; do not re-propose either.**

### 42. Multiplayer desync — three separate things, kept separate
`[PARTIAL — one cause fixed (91056894); a second located and named but NOT fixed; the 2-human desync remains UNTESTED against any of it]`
**Perceived:** two humans can finish a game together. Four 2-human games desynced within seconds each.
Promoted to a **hard release blocker** by the 2026-08-16 audience ruling. Four confident causes have already dissolved under measurement — attack the current one, do not adopt it. The confirming test is user-side and needs the user hosting. → [`items/42-multiplayer-desync.md`](pipeline/items/42-multiplayer-desync.md)

### 43. `@stable` benchmark re-baseline
`[OPEN, user-gated: multi-test grant. Framing REVISED 2026-08-14 — this is not a stale baseline]`
**Perceived:** nothing directly. But until it is taken, every "did the bot get better?" number is untrustworthy.
**Every benchmark number ever taken from the `tournament-*` suite is VOID, not stale** — both bots in every one of those matches had no economy at all. It cannot be discharged by re-running the old ladder and diffing, because there is no valid prior number to diff against. Gated behind item 40. → [`items/43-benchmark-rebaseline.md`](pipeline/items/43-benchmark-rebaseline.md)

### ⚠️ 44. AA and autotarget arithmetic
`[(a) DONE 16eca8e8. (b) OPEN and its shape changed]`
**Perceived:** an AA battery shoots at the helicopter in front of it, instead of four AA serialising at ~185-tick spacing and taking ~34 seconds to all join.
(b) is **blocked on test design, not engine work**: both required runs were performed and the RED control PASSED, so the test cannot discriminate the fix. One artefact this item quotes no longer matches the tree. → [`items/44-aa-autotarget-arithmetic.md`](pipeline/items/44-aa-autotarget-arithmetic.md)

### ⚠️ 45. Missile system
`[LARGELY DONE; the Javelin loop is PARKED BY USER RULING — do NOT re-dispatch it]`
**Perceived:** missiles behave the way the user expects. The user's severity read: *"has worked OKAY except the occasional misses… not catastrophic, but it breaks at some points."*
**Reopen the Javelin sub-item only with a video recording** — eight diagnoses have been spent on it and static reading keeps failing to settle it. What is genuinely left is the per-weapon-class miss-detonation rule. → [`items/45-missile-system.md`](pipeline/items/45-missile-system.md)

### ⚠️ 46. Release artwork and audio — four asset slots still empty or still somebody else's
**Perceived:** the game stops looking like a mod of another game at every point before the battlefield. Today the mod chooser shows stock Red Alert's icon and a stock install plays exactly one music track on infinite loop.
All user-side art/audio production; tooling and wiring are done and merged. Two slots re-confirmed open here. Carries the decoded cameo house style and two dead-end icon paths that look wired. → [`items/46-release-art-audio.md`](pipeline/items/46-release-art-audio.md)

### 48. Product-shaped gaps — voices, screenshots, map previews
`[PARTIAL — the onboarding half is DONE (dd6171cd)]`
**Perceived:** a US GI says "Yes sir" for a Russian conscript.
→ [`items/48-product-shaped-gaps.md`](pipeline/items/48-product-shaped-gaps.md)

### 49. One verification launch — three claims, one game start
`[OPEN — all three primary claims untouched]`
**Perceived:** nothing new. This is the cost of the fact that everything the art/audio manager shipped is code-read and never observed — no sprite rendered, no sound played, no game launched.
Bundled deliberately so it costs one launch, not three. → [`items/49-verification-launch.md`](pipeline/items/49-verification-launch.md)

### 53. Networking leftovers — four items deliberately deferred
`[OPEN — verified untouched 2026-08-13; dead Fluent keys re-confirmed present 2026-08-19]`
**Perceived:** varies per item; only the dedicated server is one a player would feel — and the user **explicitly declined** it for now.
Carries a residual user-side action (a DHCP reservation) without which a lease shuffle re-breaks the port forward identically. → [`items/53-networking-leftovers.md`](pipeline/items/53-networking-leftovers.md)

### ⚠️ 54. Carried defects and hygiene — found, recorded, unowned
`[PARTIAL — two of eight lines DONE; one more line is now dead]`
**Perceived:** individually small. Grouped so they are findable, not because they belong together.
Contains the record that the **only artifacts of the unresolved 2-human desync aged out and are gone** — if a desync is reproduced again, copy the replays out immediately rather than filing a note to do it later. → [`items/54-carried-defects-hygiene.md`](pipeline/items/54-carried-defects-hygiene.md)

### ⚠️ 62. Residue found in the 08-12/08-13 log and represented nowhere else
`[filed 2026-08-13; each line is new open work, none of it owned]`
**Perceived:** varies. The map-cordon line is the only one a player could feel, and only if it is done wrong.
The `halo` line is the **inverse and more dangerous class** of the lint defect item 52 fixed — a `RequiresCondition` that can never be true means the trait is permanently OFF. Also: adding map cordons will hard-fail nav-guard and needs a deliberate re-bless on played maps. → [`items/62-unrepresented-residue.md`](pipeline/items/62-unrepresented-residue.md)

---

### Live-play batch 2026-08-08 — transports

Batch framing and the two closed bullets are in [`archive/session-notes.md`](pipeline/archive/session-notes.md).

### 34. Transport pickup coordination — a tactical layer for humans AND bots
**Perceived:** you order soldiers into a transport and it just works — the vehicle drives to them, waits, collects everyone nearby, then carries on with its queue. Today it drives off without waiting and the player has to micro it.
Explicitly wanted for HUMAN play too. Recon landed, nothing implemented: **batching is not the defect** — what is missing is *demand*. → [`items/34-transport-pickup.md`](pipeline/items/34-transport-pickup.md)

### 35. Use transports for the opening derrick rush
**Perceived:** the early game land-grab looks planned — technicians ride to the money structures instead of walking the length of the map while transports sit idle nearby.
**The item's KIND changed and its dependency on 34 was retracted:** the ferry is already built and enabled on both profiles, so the work is "find out why the shipped, enabled ferry does not visibly fire". One of the two candidate causes has since been killed. Diagnosis needs zero code — the module already logs `ferried=True|False` on every capture order. → [`items/35-derrick-rush-transports.md`](pipeline/items/35-derrick-rush-transports.md)

---

### 32. Faction balance audit — RU testing + US-vs-RU imbalance detection **[IN FLIGHT 2026-08-02, user-gated on runs + sign-off]**
**Perceived:** RU bots get the same test coverage as US bots; a measured verdict on whether US-vs-RU is imbalanced; and any stat rebalancing goes through an explicit user sign-off flow. From user 2026-08-02: mirror tests (US/US, RU/RU) isolate bot skill from faction imbalance; US-vs-RU probes measure the imbalance itself; **"I do not want you to change any unit stats without my explicit review and approval."**
_Three parts: (a) static parity audit — US vs RU roster stat/cost comparison from YAML alone, no game runs; (b) mirror + cross-faction test configs authored ready-to-run (runs need a user grant, see `AWAITING-USER.md`); (c) proposals land as numbered docs in `WORKSPACE/balance/` — evidence, proposed change, expected effect — each individually signed off by the user before any YAML edit. Worker on `auto/balance-audit`._

### ⚠️ 22. Case 01 — forest ambush measurement (`cases/case-01-forest-ambush.md`) — **CALIBRATING (bar-ADJUST awaiting user)**

> ⚠️ **PREMISE CHECK 2026-08-19** — the bar may have moved past "awaiting ratification". `918bf38b` on `WORKSPACE/cases/` is titled *"docs(case-01): mine calibration batch → ratifiable two-clause bar (variance-backed)"*, which is the reframe this entry says is still owed. **Read the case file before re-asking the user.**

**Perceived:** the payoff of 20+21, proven by a number: an equal-cost force walking into the treeline ambush is destroyed at ~3× the defenders' losses, repeatably.
_Scenario authored (`tools/autotest/scenarios/test-case01-forest-ambush/`, scripted attacker + defender squad under test); calibration batch RUN. Finding: the provisional **1:3 cost-weighted ratio is ill-posed** — a holding concealment drives defender losses to **zero** (÷0), so the bar must reframe to "def casualties ≤ X AND att casualties ≥ Y over N seeds" (DISCOVERIES 2026-07-28). **Bar ratification awaits user** before iterating to GREEN. Detect-enabled fire-lane variant authored as case-01b (`4846a60a`)._

### 39. Branding and release polish — the product introduces itself as WW3MOD
`[Phase C polish, NOT new v1 scope]`
**Perceived:** the game stops introducing itself as somebody else's. Nothing about the battlefield changes — this is the frame around it, and it is the first thing a new player reads.
Overlaps items 46 and R7. The asset-licensing half was split out as item 41. → [`items/39-branding-release-polish.md`](pipeline/items/39-branding-release-polish.md)

---

## PARKED — nothing is due, listed so the artifact is discoverable

> **User-gate queue:** everything parked on a user decision/review/grant lives in [`AWAITING-USER.md`](AWAITING-USER.md) — check it before assuming an item is actionable.

### 17. (User-deferred) Supply Route capture wiring
**Perceived:** a major new win lever — you can raid and flip the enemy's reinforcement beachhead. Enemy SR → forced neutral → capturable, so knocking out their Supply Route becomes a real strategic goal.
_Deferred by you until the opening-economy AI (item 12) is solid — a bot that can't manage its own economy shouldn't be handed a new economic target._
_**The missing primitive this needed now EXISTS**, built for item 59: `CapturesInfo.CaptureToNeutral` (`Captures.cs:51`), with `DOCS/reference/supply-route.md:74` updated when it landed. That cross-check is done — do not re-derive it. **`SUPPLYROUTE` still carries no `Capturable` and no `CaptureManager`**, so the wiring itself is untouched (see CLAUDE.md's hard rule)._

### 18. (Future) "Should I attack?" endgame decision layer
**Perceived:** bots consciously shift gears — from securing income to committing to a decisive offensive (and later to SR denial) — instead of drifting into an aimless late game. You can watch the AI make the call to go for the kill.

### 41. (Parked — planning artifact only) Asset licensing and redistribution removal
**Perceived:** nothing. Deliberately. **The decision is already made: ship as-is and accept the risk.** This is the document to open when there is a *reason* — a takedown notice, a storefront submission — not a backlog of chores. → [`items/41-asset-licensing.md`](pipeline/items/41-asset-licensing.md)

### 55. (Documented, not scheduled) Multiplayer continuity — disconnects, rejoin, claimable slots
**Perceived:** a dropped player stops ruining the match.
Written up at the user's request with the explicit instruction not to implement it now. **This is two features, not one** — continuity needs nothing transferred; admission is the entire bill, and admission is gated on determinism that is currently RED (see item 42). → [`items/55-multiplayer-continuity.md`](pipeline/items/55-multiplayer-continuity.md)

---

## Where the rest of it went

- **[`pipeline/archive/closed-items.md`](pipeline/archive/closed-items.md)** — every closed, retired or shipped numbered item, verbatim: 8, 20, 25, 30, 31, 33, 47, 50, 51, 52, 58, 59, 60, 61, 63, 66, R10, R13. Kept for their rulings and traps, not as tasks.
- **[`pipeline/archive/shipped-log.md`](pipeline/archive/shipped-log.md)** — the SHIPPED log and the 2026-07-29 harness LANDED note.
- **[`pipeline/archive/session-notes.md`](pipeline/archive/session-notes.md)** — the dated framing blocks (GATE lifted, process shift, standing grant), the 2026-08-11 SESSION STATE and its **method notes worth reusing**, the batch headers for 08-08 / 08-13 / 08-15, and the close-out intake reconciliation table.
