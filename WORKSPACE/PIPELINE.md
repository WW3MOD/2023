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
| **Stance rework (4 phases)** | `[ ]` open | **LIVE** — all four modifier axes plus patrol are wired. ⚠️ *"complete" narrowed 2026-08-20:* the **fire** axis ships, but no stance touches detectability (`stance-ambush`/`stance-holdfire` have **zero** `RequiresCondition` consumers in `mods/` — which is correct per the user's design ruling), and the **Ambush** stance's widened half reaches bot-posted units only. See items 67–71 |
| **Supply Route contestation** | `[ ]` open | **LIVE AND COMPLETE** — control bar, production slowdown and notifications all ship |
| **Three-mode move system** | `[ ]` open | **LIVE AND COMPLETE** |

`RELEASE_V1.md` should be corrected. Under the "everything visible must work" rule these three are **not release work at all** — which removes a large, intimidating block from the middle of the tracker and is the single biggest piece of good news in the audit so far.

### BLOCKERS — a stranger hits these in the first session

> **R17 was created and discharged on 2026-08-30, within hours, and is deliberately not left behind as a closed entry.** It tracked the developer damage overlay shipping default-ON, taken as time-boxed debt on the user's ruling *"that could be made default on for now, as long as we change it before release"*. The deferral ran out of road the same day: `main` is now pushed as work lands and the user play-tests from a different machine, so default-on would have put floating damage numbers over every unit on their next pull — and directly under a planned full play-through whose whole purpose is filing polish items, which would then have been filed against a debug build. `DebugVisualizations.DamageNumbers` now defaults `false`; the *Damage Numbers* checkbox and the `hitcheck.log` anomaly channel are unchanged, and the detector never depended on the flag.
> **The guard that enforced R17 is still live, and now points forward instead of back.** `DebugVisualizationDefaultsTest` asserts that an entry carrying the marker `HITCHECK-OVERLAY-DEFAULT-ON` exists in this file **if and only if** that default is `true`. With the default off there is correctly no entry — and anyone who turns the overlay back on must file one here in the same commit or the build fails. **Do not read the absence of R17 as the guard having been retired.** Number not reused.

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — SHIPPED, BOTH HALVES. CLOSE.** `c9dc5b3e` *"Hide internal autotest scenarios from the Missions browser"*. Half 1: the class filter exists — `MissionBrowserLogic.cs:189-191` now carries `p.Class != MapClassification.Unknown`, and the same filter was added to the button-enable check at `MainMenuLogic.cs:382-385`. Half 2, **the half the flagger explicitly did not check, is also fixed — but not the way this finding predicted**: `mods/ww3mod/missions.yaml` **was deleted** (61 lines, same commit) and `mod.yaml` now has no `Missions:` manifest section at all, so `hasCampaign == false` and the two empty Allied/Soviet groups are never constructed (`MissionBrowserLogic.cs:154`). No stock RA campaign entries survive.
> **Residual, not worth its own launch:** no map under `mods/ww3mod/maps/` sets `MissionSelector`, so the button is probably disabled outright now — that depends on the runtime map cache and is the only part reading cannot settle. Fold the observation into item 49's verification launch rather than spending a run on it.

**R1. ~~The Missions button opens a list of 175 internal test scenarios.~~ [CLOSED — shipped `c9dc5b3e`]** *(source: `audit/260816-first-run-chrome.md`)*
**Perceived:** a new player clicks Missions expecting a campaign and gets `test-supply-far-front-reached`, `demo-heli-lanes` and 173 more internal artefacts, above two empty campaign groups labelled Allied and Soviet.
The mechanism is verified: `MissionBrowserLogic.cs:183-187` builds its "loose missions" group filtering **only** on `Status == Available` and `Visibility.HasFlag(MissionSelector)` — **there is no class filter on that path.** So the comment at `mod.yaml:93` — *"Class=Unknown hides them from every UI tab (lobby, missions, main-menu chooser)"* — **is false for the mission browser**, and has been believed by every session since it was written. Compounded by `missions.yaml` still holding the stock Red Alert campaign list (`allies-01`…`soviet-11b`), which is also what keeps the Missions button enabled at all (`MainMenuLogic.cs:371`).
_Correction to note: the false claim lives in `mod.yaml`'s comment, **not** in `CLAUDE.md` — the audit report's headline says both; the grep says one._
**Size:** minutes.

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — SHIPPED. CLOSE. And the wording is a DECISION — do not re-flip it.** `en.ftl:39` reads *"…help us optimize the OpenRA engine that WW3MOD runs on"*, with the reasoning at `:33-38`: the payload is sent to `master.openra.net` (`WebServices.GameNews`), **so naming WW3MOD as the recipient would be false on a consent dialog.** Override confirmed live, not merely present: the key is identical to the base at `engine/mods/common/fluent/chrome.ftl:269` and `mod.yaml` lists `ww3mod|languages/en.ftl` **last** in `FluentMessages:`, so it wins. Siblings checked — `:268` is already themed ("Establishing Battlefield Control"), `:270`/`:271` are engine-neutral and carry no OpenRA brand. **The finding's tell — "the branding pass stopped one line short, so there are more like it" — did not pay out here.**

**R2. ~~The second screen a new player ever sees asks them to "help us optimize OpenRA".~~ [CLOSED — deliberate wording]** *(chrome)*
**Perceived:** the first-run consent dialog names a different product than the one they just installed.
`chrome.ftl:269`, not overridden in the mod's `en.ftl`. Its sibling title *was* re-themed to "Establishing Battlefield Control" — the branding pass stopped one line short, which is the tell that there are more like it. **Size:** minutes.

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — SHIPPED, AND THE CLASS IS CLEAN. CLOSE.** `6b162ca2` *"Take the two developer maps out of the lobby map list"*. Both now read `Visibility: Shellmap` (`arena-tank-duel/map.yaml:15`, `shellmap-open-field/map.yaml:15`).
> **The part the flag did not do — the class sweep — was done here, because this finding describes a KIND of defect, not two files.** Repo-wide, only three files strip `ConquestVictoryConditions` or `SpawnStartingUnits`: the two named maps' `rules.yaml` (`:2,9` each) and `river-zeta-ww3/scenarios.yaml:558,564`. The third sits **inside a scenario definition, not base rules**, so it never reaches the lobby map list. All eight remaining `Lobby, Shellmap` maps strip neither. **No un-fixed instance exists.**

**R3. ~~Two developer maps ship as playable Conquest maps and cannot be won.~~ [CLOSED — shipped `6b162ca2`]** *(chrome)*
**Perceived:** a player picks a map from the lobby list, starts, and has no Supply Route and no victory condition — an unwinnable, unexplainable match.
`arena-tank-duel` (`Author: Combat Sim`) and `shellmap-open-field` are both `Visibility: Lobby, Shellmap`, and their `rules.yaml` strips `-ConquestVictoryConditions` and `-SpawnStartingUnits` — the latter is what places the Supply Route. **Size:** minutes.

**R4. The lobby's only AI opponents are "Experimental AI" and "Stable AI 0802".** *(chrome)*
**Perceived:** in the one menu every single-player passes through, the opponent picker offers a lab name and an internal build date, with no difficulty ladder and no descriptions.
`ai.yaml:44-51`. **Note this collides with the bot ranking rule:** it is chrome, not bot intelligence, so it is cheap and it is a blocker — the bot can stay exactly as good as it is today and this still needs fixing. **Size:** minutes for naming/descriptions; a real difficulty ladder is larger and is a separate decision.

### SHOULD-FIX — noticed within the first few matches

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — DOWNGRADE TO COSMETIC. One half shipped, one half is true-but-moot, and the headline count is REFUTED.**
> **(1) `ProductionType*` half — DONE.** `a9552780` *"Drop the dead build-menu categories and bind the surviving tabs"*: Infantry/Vehicle/Aircraft bound to **Y/U/I** at `hotkeys.yaml:4,9,14`, wired at `chrome/ingame-player.yaml:1055,1073,1091`.
> **(2) `SupportPower01..06` half — literally true, but it binds nothing a player can press.** All six are still declaration-only at `engine/mods/common/hotkeys/supportpowers.yaml:1,6,11,16,21,26`. **But no support power is reachable today:** every `AirstrikePower`/`ParatroopersPower` in `rules/player.yaml:114-605` is commented out, and the only live `NukePower` (`rules/ingame/structures-defenses.yaml:1110`) sits on a silo with `Buildable: Prerequisites: ~disabled` and `PauseOnCondition: disabled`. This is a settings-screen wart, not a keyboard defect — **and under the 2026-08-16 scope ruling ("already-disabled content stays disabled and gets zero effort") it is explicitly out of scope to fix by enabling anything.**
> **(3) "~35 dead Red Alert bindings" — REFUTED AS COUNTED.** The real total of unbound declarations across all **nine loaded** hotkey files is **9**: the six above, plus `observer.yaml:66 StatisticsGraph`, `:71 StatisticsArmyGraph`, and `control-groups.yaml:291 RemoveFromControlGroup`. **`ra|hotkeys/*` is not in the manifest, so no RA-specific hotkey file loads at all** — the ~35 was counted against a file set this mod does not load. Nothing RA-era survives here.

**R5. ~~Sidebar tab hotkeys are all unbound, and ~35 dead Red Alert bindings remain.~~ → RE-SCOPED: 9 unbound declarations, none player-reachable. COSMETIC.** *(chrome)*
**Perceived:** effectively nothing today. Six of the nine are support powers no player can trigger; the other three are observer/control-group leftovers. **Size:** minutes, and only worth spending during final pre-release polish. Supersedes and widens PIPELINE item 61.

> ❌ **VERDICT 2026-08-19 (`main @ 5890b053`) — STILL OPEN. THE FLAG WAS WRONG; DO NOT CLOSE THIS.** The flag (and `cargo-garrison-status-260819.md:74`) attributed the improvement to command-bar work landing after the finding was written. **`ed5ee6b6` is not command-bar work at all — it is a `WORKSPACE/PIPELINE.md` edit ("Mark pipeline items 60 and 61 SHIPPED").** The real command-bar commits on this file (`746c592c`, `87b2b74d`, `c9fdf334`, `7b5c692b`, `87ede0ef`) all landed **above line 623 — the garrison and cargo panels below were never touched.** Two documents now agree on a number that was measured against the wrong region.
> **Actual re-count of `ingame-player.yaml:623-1135`** (file is 1222 lines): 24 button widgets, 3 commented out → **21 live**. `TooltipText:` = 12 lines but **2 are inside comments** (`:1104`, `:1126`) → **10 live**. `Key:` = **5, not 6** (`:867`, `:955`, `:1055`, `:1073`, `:1091`).
> **The `X` buttons are all still there — verified by direct grep, not relayed: 8 of them, `Text: X` at `:655, 669, 683, 697, 711, 725, 739, 753`** (`Button@EJECT_PORT_0..7`, block `:649-753`). **None has a tooltip or a key.** Three more have real labels but neither: `EJECT_ALL:779`, `UNLOAD_ALL_TROOPS:807`, `DROP_SUPPLY:820`.
> **Re-scope, don't close: the finding shrinks from "~50" to 11 buttons, 8 of them labelled `X`** — and the single-letter-button symptom, which is the visible half, is untouched.

**R6. ~~~50~~ 11 garrison and cargo buttons have no tooltip and no hotkey; 8 are labelled just `X`.** *(chrome)*
**Perceived:** an unexplained wall of buttons, eight of them a single letter. `ingame-player.yaml:649-820`. **Size:** hours. Widens PIPELINE item 60.

**R7. The install chain identifies the product as OpenRA.** *(chrome; wave-2 install audit is going deeper)*
**Perceived:** install dir `OpenRA WW3MOD`, registry key `OpenRAWW3MOD`, Start Menu folder `OpenRA`, `<Product>OpenRA</Product>`, and the crash dialog's FAQ button opens `wiki.openra.net`. **Size:** hours. Discord rich presence needs a WW3MOD app id and is not fixable in-repo.

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — SHIPPED. CLOSE. But the flag's mechanism is WRONG in a way that will bite the next reader.** All four `Faction@` blocks checked in `rules/world.yaml`: `@randomside` (`:236-241`, the "random vanilla side" string is gone), `@0` America (`:242-245`) and `@1` Russia (`:251-254`) all filled. `# Faction@1` Ukraine (`:246-250`) is commented out and inert.
> **⚠️ THE LITERAL `\n` IS STILL IN `world.yaml`, AND THAT IS CORRECT — DO NOT "FIX" IT.** The flag says `1c30bef7` "unescaped the `\n`". It did not do so in YAML; **the unescape is in engine C#** — `LobbyUtils.SplitDescription` does `description?.Replace("\\n", "\n")` at `Lobby/LobbyUtils.cs:218-222` with the comment *"MiniYaml does not unescape"*, guarded by `engine/OpenRA.Test/FactionDescriptionSplitTest.cs` (102 lines, same merge `de78a1ed`). **Anyone who "cleans up" the escaped `\n` in `world.yaml` now breaks every faction tooltip body** — which is exactly the defect `1c30bef7` was fixing, re-introduced from the other side.

**R8. ~~Faction descriptions are blank, and Random Side offers "a random vanilla side".~~ [CLOSED — shipped `75ac6941` + `1c30bef7`]** *(chrome)*
`world.yaml:242-253` — `Description: America` with nothing after it. **Perceived:** the faction picker teaches a new player nothing about the two sides the whole game is built on. **Size:** minutes to fill, longer to write well.

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — SHIPPED. CLOSE. Verified by arithmetic, not by resemblance.** `db2b2fa6` *"fix: apply ReloadCount to the production tooltip's grand ammo total"*.
> **The flag's phrase "agree by construction" was checked rather than accepted, because the original bug WAS two look-alike expressions drifting.** They are now literally the same property: the total sums `p.PoolBudget` (`ProductionTooltipLogic.cs:205-211`) and each per-pool line prints `PoolBudget` from the *same* `AmmoPoolInfo` instance (`AmmoPool.cs:96-98`, reached via `IProvideTooltipDescription` at `ProductionTooltipLogic.cs:195-199`). One definition at `AmmoPool.cs:67-69`. **The eligibility predicates match too** — renderer filters `Ammo > 0 && SupplyValue > 0` (`:206`), `ProvideTooltipDescription` returns null on the negation (`AmmoPool.cs:77-78`) — so no pool can be counted in the total while absent from the list.
> **`ReloadCount` and `BatchCount` are different things, not a rename:** `ReloadCount` is the YAML field (`AmmoPool.cs:41`, still driving the live reload path at `:293`/`:466`); `BatchCount` is derived as `ceil(Ammo / max(1, ReloadCount))`.
> **Bradley worked by hand from shipped YAML** (`vehicles-america.yaml:285`, `Cost: 1500`): pool 1 (`:368-376`) Ammo 900 / ReloadCount 100 / SupplyValue 5 → **45**; pool 2 (`:392-400`) Ammo 8 / default ReloadCount 1 / SupplyValue 75 → **600**; **total 645** = 43% of cost, matching `economy.md:167`. Old code gave 900×5 + 8×75 = **5100** — the reported symptom exactly. `AmmoPoolTest.cs` added; **not run here** (no build).

**R11. ~~The production tooltip's ammo total is wrong by up to 100×.~~ [CLOSED — shipped `db2b2fa6`]** *(systems)*
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
> **⚠️ Standing hazard, and it is this queue's most expensive recurring defect.** In the week to 2026-08-19, **five items were found to describe already-merged work**; two of them cost a worker dispatched at nothing. Entries tagged `[IN FLIGHT]` have twice outlived their own merge. **Before dispatching anyone, spend one `git log -S <symbol>` or one grep on the item's central premise.**
>
> **⚠️ The subject with the freshest research is at the BOTTOM of this file, not the top.** Everything on ambush, concealment, cover, stances and detection lives in the user-gated block immediately above `## PARKED` — **items 67–71**, plus a re-framed **item 22**. It is last in execution order *only* because the user has gated it. **Read the block header before touching any of it: ambush SHIPPED and is not a feature to be built.**
>
> **VERDICT PASS COMPLETE 2026-08-19 (`main @ 5890b053`).** The 17 one-grep-deep ⚠️ flags set during the file split have all been converted to verdicts and the ⚠️ markers removed; each item now carries a dated ✅/❌ block naming the commit or the evidence. **Read that block before re-checking anything — it exists so the next person does not repeat the grep.** Two lessons from the pass, both worth generalising:
> - **A merged branch is not a finished item.** Item 64's branch merged carrying a rendezvous that is *switched off by default*; item 65's named branch carried test hygiene while the actual fix rode a different one. Always read what the branch *contained*.
> - **A flag can be wrong in the expensive direction.** R6 was flagged "count is stale, needs a re-count not a dispatch" on the strength of a commit (`ed5ee6b6`) that turned out to be a `PIPELINE.md` edit, not command-bar work. Eight placeholder `X` buttons are still there. **Two documents agreeing on a number is not evidence; the file is.**

### Current user priorities — 2026-08-15 live-play batch

Framing for this batch (why 63/64 are not one item, and what 65 has to do with either) is in [`archive/session-notes.md`](pipeline/archive/session-notes.md). Items **63** and **66** from this batch are merged and archived to [`closed-items.md`](pipeline/archive/closed-items.md) — 66's *procurement ordering axis* dossier is still the reference for the unfinished lobby-verification arm.

### 64. Coordinated combined-arms push — the first tank attacks alone
`[PARTIALLY SHIPPED — the rendezvous is merged but SWITCHED OFF; the speed differential is untouched and is the visible half]`
**Perceived:** the opening push looks like a formation instead of a lone vehicle. Armour leads, a transport carrying infantry and a technician follows behind it, and the infantry arrive at the front protected rather than walking up on their own.
**More landed than "recon":** `ef608a62` publishes `PoiOffensiveBotModule.ForwardStagingAnchor` and folds it into the transport's drop-off via a new pure `RendezvousMath` — but `RendezvousWithOffensiveStaging` defaults **false** and `ai.yaml:1625` sets it false, so both profiles are byte-identical and this has never affected play. **The remaining work is (1) enable and measure, (2) the speed differential — infantry already walk to the same anchor from tick 3; the tank simply outruns them.** → [`items/64-combined-arms-push.md`](pipeline/items/64-combined-arms-push.md)

### 65. ~~Field actors swallow artillery shells~~ **[SHIPPED `db01b0ae` — CLOSE]**
`[the damage half was a MISATTRIBUTION and is settled; what survives is a separate balance question, not this item]`
**Perceived:** delivered — a shell landing in a field now produces its explosion and sound.
Impact classification skips `IsGroundCover()` actors in **both** copies of `ActorTypeAtImpact`; damage was never gated and always ran. Shipped with a RED-verified autotest. **Do not re-file: the "no damage" half belongs to `bugs/discovered.md:443` (artillery damage radii smaller than its own inaccuracy) and needs combat-sim, not this item.** → [`items/65-field-actors-swallow-shells.md`](pipeline/items/65-field-actors-swallow-shells.md)

---

### 40. Danger-scale rework — stop the bot treating ordinary ground as lethal
`[stage (a) DONE ddcc5d6c; stage (b) instrument landed; stage (c) OPEN and is now the whole item]`
**Perceived:** the bot stops flinching at nothing. Supply trucks actually deliver instead of driving part-way and turning back; units stop refusing to advance across terrain that is not in fact dangerous.
All 18 ground thresholds (plus 1 air) sit 8×–459× below the live median cell of the field they gate. **Gates the `@stable` benchmark re-baseline (item 43), which in turn gates item 22 and ambush gate (b)** — nothing downstream of a measurement moves until the instrument is right. _(Cross-reference corrected 2026-08-20: the old list read "22, 25, 31". **Items 25 and 31 are archived** — 25 shipped `5dc14934` on 2026-07-29, 31 merged `af8bca1f` with its gates off — so neither can be gated by a future measurement. **The dossier still carries the old list**; it was left unedited because item 40's subject was not researched here.)_ Moves BOTH bot profiles by construction; no seam exists to hide behind. → [`items/40-danger-scale-rework.md`](pipeline/items/40-danger-scale-rework.md)

---

### Current user priorities — 2026-08-13 live-play batch

Batch framing in [`archive/session-notes.md`](pipeline/archive/session-notes.md). Items **58, 59, 60 and 61** from this batch are all shipped and archived — **58's vocabulary ruling and grep trap, and 61's `TAKE_COVER` dead-button analysis, are still live guidance** in [`closed-items.md`](pipeline/archive/closed-items.md).

### 56. Supply trucks still do not commit to a delivery
`[the item's own tag reads: HIGHEST PRIORITY IN THE WHOLE QUEUE — above item 40]`
**Perceived:** a supply truck drives to where supplies are needed, drops its supply, and leaves. Today it goes back and forth and never commits.
Declared fixed to the user at least three times. **The user has pre-authorised the blunt fix** — disabling danger awareness for trucks entirely is explicitly acceptable. **A green scenario does NOT close this item**; the acceptance bar is a full bot-vs-bot match on a real map, with an added precondition clause so "no truck was ever bought" reads as instrument failure rather than a negative result. Disabling danger awareness is **seven sites, not one seam**, and one of them reads a different field that no config flag reaches. → [`items/56-supply-truck-delivery.md`](pipeline/items/56-supply-truck-delivery.md)

### 57. ~~Bot build composition — one item, three symptoms, same subsystem~~ **[ALL THREE SHIPPED — CLOSE]**
**Perceived:** delivered — the bot no longer opens with two idle supply trucks or medics, and AA soldiers are held at a standing floor of 2.
**The flag guessed the wrong survivor.** It said "only (c), the AA half, plausibly survives"; **(c) is the most clearly done of the three** — `aa.america: 2` is in `UnitFloors` (`ai-america.yaml:150`) and `aa.*` is in `UnitFloorSupportedTypes` on both factions, which is precisely the user's "one or two AA soldiers at all times". (a) is `SupplyTruckFloor: 3` + `SupplyTruckFloorPer: 10`; (b) is `medi: 2` per 10.
**⚠️ THIS ITEM'S DURABLE FINDING IS NOW FALSE — do not carry it forward.** It reads *"there is no standing-population floor for ANY unit type except `truk`"*, and *"(a) and (c) pull the same lever in opposite directions."* `UnitFloors`/`UnitFloorPer`/`UnitFloorSupportedTypes` is that general mechanism, on both factions; (a) and (c) now pull **different** levers. → [`items/57-bot-build-composition.md`](pipeline/items/57-bot-build-composition.md)

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

### 44. AA and autotarget arithmetic
`[(a) DONE 16eca8e8 — confirmed live. (b) STILL OPEN; premise re-verified 2026-08-19]`
**Perceived:** an AA battery shoots at the helicopter in front of it, instead of four AA serialising at ~185-tick spacing and taking ~34 seconds to all join.
(b) is **blocked on test design, not engine work** — and re-checking confirmed the engine premise is untouched (`PreemptScanInterval: 25` at four bases, C# default still 0). **A run today would be worthless: the scenario's own header records that it cannot discriminate the fix at any seed. What is needed is a redesigned test, not a launch.** Three stale references inside the dossier are now corrected. → [`items/44-aa-autotarget-arithmetic.md`](pipeline/items/44-aa-autotarget-arithmetic.md)

### 45. Missile system
`[BOTH previously-open deliverables DISCHARGED; ONE NEW open question named by the spec itself. Javelin still PARKED — do NOT re-dispatch it]`
**Perceived:** missiles behave the way the user expects. The user's severity read: *"has worked OKAY except the occasional misses… not catastrophic, but it breaks at some points."*
**`DOCS/reference/missiles.md` now exists AND §2–3 define the class taxonomy and the per-class miss-detonation rule** (`85d146c8`) — both things this item listed as outstanding. **But that is a SPEC, not an implementation**, and it names its own successor: the detonation test still measures to the aim point while the miss test now runs on physical separation, so the two are not commensurable — `ATGM` rolls `Inaccuracy: 512` against `CloseEnough: 298`, and a missile can sit physically inside the proximity radius without fusing. **That changes when every missile in the game detonates and needs its own measurement.** → [`items/45-missile-system.md`](pipeline/items/45-missile-system.md)

### 46. Release artwork and audio — every asset slot is still empty or still somebody else's
`[ALL SLOTS RE-VERIFIED OPEN 2026-08-19 — nothing closed, one slot is WORSE than filed]`
**Perceived:** the game stops looking like a mod of another game at every point before the battlefield. Today the mod chooser shows stock Red Alert's icon and a stock install plays exactly one music track on infinite loop.
All user-side art/audio production; tooling and wiring are done and merged. **Load screen confirmed empty at PIXEL level, not by directory listing** (`loadscreen.png` left half 0/65536; `-2x` 0/262144; `-3x`'s pixels sit outside the logo area). **All 15 Russian cameo files are md5-identical to their America twins — not just `e3`.** Only the installer icon set cannot be settled in-repo. → [`items/46-release-art-audio.md`](pipeline/items/46-release-art-audio.md)

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

### 54. Carried defects and hygiene — found, recorded, unowned
`[RE-SCOPED 2026-08-19 — only TWO lines survive: the smoking husk and the unmeasured sync-report cost]`
**Perceived:** individually small. Grouped so they are findable, not because they belong together.
Four lines are now dead or done: `TEMPmt.txt` is gone (**and was never tracked, so no user say-so is owed**), the two desync replays are confirmed unrecoverable, the `river-zeta` edits landed (`8343900b`), and **both `exp-terr-bias` claims are stale** — it is no longer the only unmerged branch (six are) and the `Passenger.cs` fix it needed is now on `main` (`cb9d54c7`). Contains the record that the **only artifacts of the unresolved 2-human desync aged out and are gone** — if a desync is reproduced again, copy the replays out immediately rather than filing a note to do it later. → [`items/54-carried-defects-hygiene.md`](pipeline/items/54-carried-defects-hygiene.md)

### 62. Residue found in the 08-12/08-13 log and represented nowhere else
`[RE-SCOPED 2026-08-19 — two of five lines done; the halo line is half wrong and half worse than filed]`
**Perceived:** the rotor line is the one a player can see — helicopter rotors never visually stop.
**Done:** the cordon half landed and came out green (`67888986` + `097738f4`), so **the "adding cordons will hard-fail nav-guard" warning is spent**; and `ResupplyBehaviorSelectorLogic` now routes Evacuate through an order (`e49ff242`), as item 60 was predicted to do. **Still open:** nav-guard still covers only 10 maps; the `Versus` tables are still wrong verbatim. **The `halo` line needs correcting, not just carrying** — three of its four conditions DO have a grantor; only `rotor-stopped` has none, and its consequence (6 permanently-dead traits, rotors that never stop) was never spelled out. → [`items/62-unrepresented-residue.md`](pipeline/items/62-unrepresented-residue.md)

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
> ⚠️ **FLAGGED STALE 2026-08-20 — not researched here, not rewritten. Verify before dispatching.** Two cheap signals, both against `main @ 57822b4e`: **(1) the named branch `auto/balance-audit` does not exist** — the balance work that landed came through `wt/balance-parity` (merge `c8ad2baa`) and `7ec36b8c`. **(2) Part (a) looks DONE:** `WORKSPACE/balance/260802-parity-audit.md` exists, and part (c)'s numbered-proposal flow is clearly in use (`001-tunguska-duplicate-health.md`, `002-himars-iskander-parity.md`, `003-mi28-secondary-air.md`, plus two dated 260819 docs). The `[IN FLIGHT 2026-08-02]` tag is 18 days old and this queue's header warns that tag has twice outlived its own merge. **Someone who owns this subject should re-cut the stub to whatever is actually left.**

_Three parts: (a) static parity audit — US vs RU roster stat/cost comparison from YAML alone, no game runs; (b) mirror + cross-faction test configs authored ready-to-run (runs need a user grant, see `AWAITING-USER.md`); (c) proposals land as numbered docs in `WORKSPACE/balance/` — evidence, proposed change, expected effect — each individually signed off by the user before any YAML edit. Worker on `auto/balance-audit`._

### 22. Case 01 — forest ambush measurement (`cases/case-01-forest-ambush.md`) — **AWAITING ONE USER YES/NO, NOT MORE MEASUREMENT**

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — THE REFRAME IS DONE; WHAT IS OWED IS RATIFICATION, NOT AUTHORSHIP.** `918bf38b` is an ancestor. This entry says the bar "must reframe to *def casualties ≤ X AND att casualties ≥ Y over N seeds*" — **that exact shape exists**, variance-backed, in the case file's 2026-07-29 entry: **Bar A** = *mean def cost-loss ≤ 50cr AND mean att cost-loss ≥ 300cr over ≥6 seeds* (mined batch scores 0 / 350 → GREEN), plus optional per-seed hard guard **Bar B** = *every seed def = 0*. Teeth sit on the zero-variance defender axis; the attacker clause is deliberately soft because it is noisy (kills {4,3,5,4,2,3}, σ≈1 kill).
> **Read this before opening the file:** its own `## Bar` header at `:23-27` still says "NOT ratified… ratify before the bar gates autoburn iteration". True, but it reads as though no candidate exists — **the candidate is 14 lines further down.** The header is stale relative to the body.
> **Next step is a single user yes/no on Bar A (+B). No run is needed to get there.**

> ⚠️ **RE-FRAMED 2026-08-20 by the ambush research programme — the ASK is unchanged, what the test MEASURES is not. Do not close this on the strength of the research.** Re-verified at `main @ 57822b4e`: the bar numbers are live and still unratified (`parse-case01-bar.py:33-36` — `BAR_A_DEF_MAX_MEAN = 50`, `BAR_A_ATT_MIN_MEAN = 300`, `BAR_A_MIN_SEEDS = 6`, `BAR_B_DEF_MAX = 0`), so `AWAITING-USER.md` §4 is genuinely open.
> **But the scenario grants its own gate.** `test-case01-forest-ambush.lua:3` posts the defenders as *"USA, HUMAN, Ambush stance, `enable-ambush-tactics` granted"* — a configuration **no human player can reach in a real match**, because nothing outside `LaneAmbushBotModule` and the autotests' own Lua grants that token (item **68**). A green here prices the **bot's** ambush, not the one the player gets.
> **And the defence it measures rests on a cover term that is unreachable** (item **69**): the `+1/+2/+3` `object-proximity` ladder has one emitter repo-wide and no soldier can stand inside its radius. `AWAITING-USER.md:123`'s reassurance that the correction "widens the defenders' margin" was written before that was known — **it is not wrong about the bar, but it is no longer the whole picture.**
> **Consequence, stated plainly:** ratifying is still worth one word — it makes a red result actionable instead of arguable. Just do not read a green as *"ambush works for players."* **Do not cite this scenario as evidence the coordination works** (`ambush-programme/README.md` §5): it is ~1000 commits stale and never asserts simultaneity.

**Perceived:** the payoff of 20+21, proven by a number: an equal-cost force walking into the treeline ambush is destroyed at ~3× the defenders' losses, repeatably.
_Scenario authored (`tools/autotest/scenarios/test-case01-forest-ambush/`, scripted attacker + defender squad under test); calibration batch RUN. Finding: the provisional **1:3 cost-weighted ratio is ill-posed** — a holding concealment drives defender losses to **zero** (÷0), so the bar must reframe to "def casualties ≤ X AND att casualties ≥ Y over N seeds" (DISCOVERIES 2026-07-28). **Bar ratification awaits user** before iterating to GREEN. Detect-enabled fire-lane variant authored as case-01b (`4846a60a`)._

### 39. Branding and release polish — the product introduces itself as WW3MOD
`[Phase C polish, NOT new v1 scope]`
**Perceived:** the game stops introducing itself as somebody else's. Nothing about the battlefield changes — this is the frame around it, and it is the first thing a new player reads.
Overlaps items 46 and R7. The asset-licensing half was split out as item 41. → [`items/39-branding-release-polish.md`](pipeline/items/39-branding-release-polish.md)

---

## AMBUSH, CONCEALMENT & COVER — 2026-08-20 research programme **[USER-GATED: NOTHING IN THIS BLOCK MAY BE IMPLEMENTED]**

> **Two hard gates, both the user's, both load-bearing. Neither is a manager call.**
>
> 1. **Nothing on stances, ambush, concealment or cover may be implemented until the user says so.** Verbatim: *"I will let you know when we are ready to implement, until then just ask me"* and *"it is my wish that you really get to the bottom of this before we start implementing."* This block therefore sits **last in execution order only because it is gated** — not because it ranks low. It is the most recent and best-evidenced work in this file.
> 2. **Item 67 lands BEFORE item 69.** Ruled 2026-08-20 (`57822b4e`): repairing the cover ladder first makes a currently-unreachable invisibility tier reachable, so the visibility floor is cheap now and urgent later.
>
> **What the research settled, and it inverts the framing every earlier ambush item used: ambush is NOT a feature to be built — it shipped.** `UnitStance { HoldFire, Ambush, FireAtWill }` is live at `AutoTarget.cs:22` (re-verified here at `main @ 57822b4e`) with a bound button, a gold `A` glyph, pre-aim, a coordinated spring, a five-trigger state machine, five passing autotests and both bot profiles. **Nine strands were dispatched and every one that examined a mechanism found it already built and quietly broken, not missing.** An item reading "design ambush", "build the ambush stance" or "add hold-fire" is describing merged work — the implementation is archived as **item 8** in [`closed-items.md`](pipeline/archive/closed-items.md).
>
> **Programme: [`ambush-programme/README.md`](ambush-programme/README.md).** The ninth strand, [`recon/260820-ambush-cover-detection-audit.md`](recon/260820-ambush-cover-detection-audit.md), is the only document there with a second independent pass behind it and **outranks the other eight wherever they disagree** — it already overturned one confident, widely-repeated, wrong story. **Defect detail belongs in [`bugs/discovered.md`](bugs/discovered.md), not here. These stay stubs.**
>
> **Deliberately NOT queued: a legibility / readout item.** That is precisely the mistake manager decision 08 records — shipping a readout *around* an absent behaviour and reporting it as the answer. Legibility is a sequencing rule, not a scope rule: it follows a behaviour fix, it does not substitute for one.

### 67. Clamp minimum detectability to 1 — nothing is ever fully invisible
`[RULED by the user 2026-08-20; implementation user-gated. MUST LAND BEFORE ITEM 69]`
**Perceived:** you can always find a man you are standing on top of. Today a sufficiently concealed unit can drop out of standard vision entirely, which the user reads as a bug, not a reward.
User verbatim: *"I think it is an error that they can become fully invisible… I think their visibility should be at least 1 at all times."* **No clamp exists** — verified 2026-08-20, no `MinDetectab*` / `VisibilityFloor` / `MinVisibility` symbol anywhere under `engine/OpenRA.Mods.Common/Traits/` or `mods/`. Once it holds, the concealment gauge's top tier becomes unreachable and the "vanishing ring" cliff stops existing on its own. Ruling and sequencing: `57822b4e`, `ambush-programme/README.md` §7.

### 68. A human clicking Ambush gets a different feature from the one the bots get
`[HEADLINE DEFECT of the 2026-08-20 programme; user-gated]`
**Perceived:** the player picks Ambush, reads a tooltip promising a coordinated hold-and-spring, and gets plain hold-fire. The bots get the real thing.
Stages 2–4 — halt-before-contact, the stationary hide-and-spring machine, the coordinated spring — are gated on `enable-ambush-tactics`, granted by `LaneAmbushBotModule` (`:451,474`) and by **six autotest scenarios** — five in Lua, plus `test-case01-forest-ambush`, which grants it in its own `map.yaml` as well — **and by nothing else** (re-verified by grep at `main @ 57822b4e`: 7 files, 6 scenarios). `AutoTarget.cs:93` describes the gate as *"a human opt-in / bot ledger commit / test map grants"* — **no human opt-in path ships**, and `LaneAmbushBotModule.cs:48` says so outright. **This is a regression against this feature's own shipped design ruling D** (item 8: *"human-settable + bot behind the same default-off gate from day one"*), not a design choice. **Five passing autotests do not contradict it — they pass because they grant the gate by hand.** *(Five passing against six granting is not an inconsistency: the sixth, `test-case01b-detect`, has never been run — see item 70.)* The grantor seam already exists, so this is a gating question, not a design one, and it is the strongest single candidate for the user's original complaint.

### 69. The concealment cover ladder is dead end-to-end
`[user-gated; AND BLOCKED BEHIND ITEM 67 by the user's sequencing ruling]`
**Perceived:** hiding in a forest does nothing. The largest single term in the concealment stack never fires, so the treeline a player picks for cover is worth exactly what open ground is worth.
`object-proximity` (`+1/+2/+3`, consumed at `infantry.yaml:704-715`) has **exactly one emitter repo-wide** — `^TreeHusk` (`husks.yaml:118-121`). Living trees emit nothing. **And burning the forest down does not help:** the audit's geometry pass shows the husk's trigger radius sits inside a cell the husk itself blocks, so zero of 23 husk types are reachable. **Three separate parties grepped this and produced the same plausible wrong story, because all three checked the GRANT and none checked the GEOMETRY** (`534e36d6`) — do not re-derive this from the grant. Carries three sibling detectability-input defects from the same audit: the two `dugin` timer bugs, the −2 firing penalty being `primary`-armament only, and infantry CV topping out at 9. **One contradiction is deliberately preserved, not papered over** — the legibility strand computes a reachable CV 10, the audit says 9; both cannot be true. ⚠️ **The existing `bugs/discovered.md` 2026-08-20 entry still carries the superseded "only burnt trees" framing**; the parallel bug-filing pass owns correcting it.

### 70. The coordinated spring is not coordinated, and the tooltip promises a zero aim delay that does not exist
`[user-gated]`
**Perceived:** the trap springs and nothing happens for a beat — the volley smears over a second or two, and an MBT stands in the open for ~3 s before firing. The Ambush tooltip promises *"zero aim delay."*
`TriggerNearbyAmbushAllies` sets a flag on each nearby ambusher and **never makes any of them shoot** — each fires on its own next scan, at WW3MOD's overridden 16–32-tick infantry interval against an engine default of 3–8, drawn per unit. Separately `Armament.AimingDelay` is 15 ticks on infantry and 30–50 on vehicles, is charged **in full after the spring**, and pre-aim never touches it (`PreAimAtTarget` only rotates facing). **The two are the same order of magnitude — fixing either alone leaves about half the lag.** **Trap:** the obvious fix routes through `ScanForTarget`, which re-arms off `SharedRandom` and would shift the shared RNG stream and break the frozen `@stable` baseline; the codebase already solved this exact problem for target preemption — copy that pattern verbatim. **Unmeasured:** the 1–2 s figure is derived from YAML and the timestep, not observed. `test-case01b-detect` was authored to measure precisely this and **has never been run once** — the cheapest measurement available anywhere in the programme.

### 71. Cover protects almost nothing, and Take Cover would march a squad onto burnt ground
`[user-gated; needs a design call before any code]`
**Perceived:** going prone or digging in feels like it should save you and does not — and a Take Cover button would confidently send soldiers to a position that stopped being cover several minutes ago.
Of the mod's **109 `DamageTypes:` declarations, exactly one** carries a `Prone*` token, and `InfantryStates.cs:200-203` applies `ProneDamageModifiers` only to warheads declaring a match — so prone reduces damage from one superweapon and nothing else. `dugin` is concealment-only, zero damage reduction. **The dominant protective effect is not damage reduction at all:** `ClearSightThreshold` (`Armament.cs:364`) refuses the shot outright once foliage on the line exceeds the weapon's threshold, which is almost certainly what players perceive as "cover working" — and nothing in the UI says so. **Map density is static:** `UpdateDensityForBuilding` and the shadow-update queue ship with their callers commented out, so a forest shelled flat still grants full cover, full concealment, and still refuses rifle shots. **The Take Cover button was deleted 2026-08-19 and the user has ruled it must be both automatic AND a button**, so building this means restoring it; item 61's dead-button analysis in [`closed-items.md`](pipeline/archive/closed-items.md) is the costing and records that it was inert at three levels.

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
