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

> ⚠️ **VERDICT 2026-09-01 (`main @ bd8e7290`) — HALF SHIPPED. RE-SCOPE, DO NOT CLOSE.**
> **Shipped:** `d02f41d0` renamed the internal build date away — `rules/ai/ai.yaml:54` now reads `Name: Standard AI` (`Type: stable` at `:55`); `:49` is `Name: Experimental AI`, `:50` `Type: experimental`. The split survives, because the build date was the part that read badly, not the split.
> **NOT shipped, and this is the whole remaining item:** there is still **no difficulty ladder and no descriptions** — `grep -c 'Difficulty\|Description' rules/ai/ai.yaml` returns **0**.
> **The file is `rules/ai/ai.yaml`, not `rules/ai.yaml`** — the old cite below sent at least one reader to a path that does not exist.

> ⚠️ **VERDICT 2026-09-05 (`main @ eacc8f44`) — THE "MINUTES" ESTIMATE IS WRONG, AND NOT BY A LITTLE. STILL RE-SCOPE, STILL DO NOT CLOSE.**
> Full measurement in `WORKSPACE/DISCOVERIES.md` §2026-09-05 *"the whole live delta … is THREE behaviours"*. Four findings:
>
> **(1) THERE IS NO RENDER SITE. The description half cannot be done in YAML at all.** `IBotInfo` (`engine/OpenRA.Game/Traits/TraitsInterfaces.cs:421-425`) exposes exactly `Type` and `Name`; `ModularBotInfo` has no `Description` and no `Difficulty`. `FieldLoader.UnknownFieldAction` (`FieldLoader.cs:61-62`) **throws** — putting `Description:` in `ai.yaml` today does not fail to render, **it stops the mod loading.** And the picker itself (`LobbyUtils.ShowSlotDropDown:59-98` → `LABEL_DROPDOWN_TEMPLATE`, `engine/mods/common/chrome/dropdowns.yaml:17-26`) is a single `Label@LABEL` at `Height: 25` with no second line and no `TooltipContainer`. **Minimum real cost: 5 files** — `IBotInfo` + its two implementers (`ModularBotInfo`, `DummyBotInfo`), `LobbyUtils.SlotDropDownOption`/`SetupItem`, a ww3mod-owned dropdown template with `TooltipContainer: TOOLTIP_CONTAINER` added to `mod.yaml`'s `ChromeLayout`, and fluent strings in `languages/en.ftl`. Plus a **launch to verify tooltip placement over the dropdown panel**, which no static check substitutes for. Call it a focused session, not minutes.
> **The near-miss is worth knowing** so nobody re-derives it: the tooltip path is *one YAML line* short. `ScrollItemWidget : ButtonWidget` already inherits `TooltipContainer`/`TooltipDesc`, `Setup` clones the template so the copy ctor carries them (`ScrollItemWidget.cs:75`, `ButtonWidget.cs:139-146`), `TOOLTIP_CONTAINER` exists in the lobby tree (`lobby.yaml:499`), and `BUTTON_TOOLTIP` already has a `Label@DESC` (`tooltips.yaml:15-36`). It draws nothing only because `TooltipContainer` is `readonly`/YAML-only and the dropdown template omits it, so `ButtonWidget.MouseEntered` early-returns (`ButtonWidget.cs:232-233`). The worked example is one widget away: `HANDICAP_DROPDOWN` (`lobby-players.yaml:284-288`) sets it; `SLOT_OPTIONS` (`:262-271`) does not.
> **Cheaper variant, different moment:** tooltip on `SLOT_OPTIONS` itself — 2 files + fluent, no new chrome file. But it describes the bot *already in the slot*, so the player reads it **after** choosing, not while choosing. Probably not what the blocker wants.
>
> **(2) NO DIFFICULTY RELATIONSHIP EXISTS TO DESCRIBE, AND THE OBVIOUS COPY IS A LIE.** Per the 2026-09-05 user ruling, `@stable` is a re-synced COPY of `@experimental` held as a benchmark control — not an easier, safer or better-tested opponent. Measured at this ref: **17 of 19 module pairs are config-identical**, 1 more (`LogisticsCenter`) differs only by restating C# defaults, and the entire live delta is **three behaviours**, all additions on `@experimental`: flanking (`FlankingEnabled: true`, `ai.yaml:934`), idle-truck field resupply (`IdleTruckHunt`, `ai.yaml:1685`), garrison commit-on-order (`CommitGarrisonedUnits`, `ai.yaml:1480`). **`@experimental` is not established to be stronger** — `ai.yaml:929-933` records the flanking constants as *conservative first guesses, NOT measured*, and the close-in ratchet was armed then switched back off for losing (`1452a82f`). **Do not write "balanced / for new players" vs "harder / for veterans".**
> **Trap:** `IdleTruckHunt` is named "hunt" but is a **resupply** behaviour — the truck seeks *its own* ammo-starved infantry inside a 20-cell leash, infantry-only by construction. Copy calling it a raid or a truck hunt is false.
>
> **(3) COPY, IF TWO OPPONENTS SHIP.** Written to be true at this ref, to avoid any difficulty claim, to name no glyph, and to **not restate the profile noun** — so the pending `@experimental`/`@stable` rename stays a single edit at `ai.yaml:82` and `:89` (the two `Name:` lines) and does not touch the descriptions:
> - *Experimental AI* — "The opponent under active development. Every AI change lands here first, so it plays the newest tactics: flanking attacks that come in on two bearings at once, supply trucks that drive out to rearm infantry in the field, and tighter coordination between its defending and attacking squads. Its behaviour changes from build to build, and a new tactic is not always an improvement."
> - *Standard AI 0902* — "A snapshot of the same AI, frozen on 2 September and left unchanged so that changes to the AI can be measured against a fixed reference. Same rules, same units, same economy — minus the three newest tactics. Pick this one for an opponent that does not move while you learn it."
>
> **(4) THE HONEST RECOMMENDATION IS TO SHIP ONE OPPONENT, and this needs a user decision.** For a public release to strangers, the second entry is a developer instrument: its name carries a date stamp (`Standard AI 0902`) that reads as a build artifact, its purpose (measurement control) is meaningless to a player, and its only player-visible property is that it lacks three tactics. A stranger cannot make an informed choice between them because there is no meaningful choice to make. **But deleting it is not free:** `TournamentConfig.cs:28-29` defaults `P1Bot`/`P2Bot` to `"stable"`, and `LobbyCommands.cs:618` validates `slot_bot` against the live bot list, so removing the entry breaks the tournament harness and every benchmark script. Hiding it from the picker instead needs a `Hidden`/`Internal` bool on `IBotInfo` plus a `Where` in `ShowSlotDropDown` — **the same shape and size of engine change as adding `Description`.** So all three paths (describe both / hide one / delete one) cost roughly the same, and the choice is a product call, not a cost one. `AIUtils.DefaultBotType` is already `"experimental"` (`AIUtils.cs:113`, user ruling 2026-08-19), so a single-opponent release needs no default change.
>
> **DIFFICULTY LADDER — SCOPING ONLY, NOT BUILT (as R4 itself asks).** **The engine has no difficulty concept for bots whatsoever.** There is no `Difficulty` field, no tier enum, no handicap hook on `IBotInfo`; the only per-player difficulty-shaped thing in the lobby is `HANDICAP_DROPDOWN`, which is a **flat damage/build-speed handicap applied to any player**, human or bot — not an AI skill setting, and using it as one would be a second lie in the same menu. So a ladder is **per-module tuning**, and the knob count is the problem: a rung is a whole `ModularBot` instance plus its own gated copy of every module. Today that is **1 `ModularBot` + 19 gated module blocks ≈ 340 lines of YAML per profile**, and a three-rung ladder means **three such sets to write, keep in sync, and re-sync forever after** — on top of the two that already exist, against a bot whose tuning is explicitly unmeasured. What a rung would have to actually vary, if it is to be honest rather than cosmetic: (a) **reaction and scan cadence** — `ScanInterval`, `ReevaluateInterval`, `ReorderDwellTicks`; (b) **force thresholds** — `FlankMinForceSize`, `AssaultMassRatioPct`, the `SquadManager` sizes, i.e. how much mass it insists on before committing; (c) **which tactics are on at all** — the three deltas above are already a de-facto ladder rung; (d) **economy aggression** — `AdaptiveProduction` and `UnitBuilder` ratios. **The cheap and honest version, if a ladder is wanted for release:** do NOT build rungs out of module copies. Add one `IBotInfo`-level scalar the modules read (a percentage applied to reaction cadence and force thresholds) so a rung is one number, not a 340-line clone. That is a real engine design task and belongs in its own item, not in a chrome blocker. **Recommend: descriptions and the one/two-opponent decision ship in R4; the ladder becomes a separate numbered item and does NOT block release.**

> ✅ **USER RULING 2026-09-05 — LEAVE IT EXACTLY AS IT IS. R4's chrome half is CLOSED-BY-DECISION, not by implementation.**
> Put to the user with four costed options (drop the date only / drop the date and wire the tooltip / ship one opponent / leave it). They chose **leave it**, which re-affirms the deferral already recorded in the DEFERRED-TO-FINAL-POLISH table above: *the stable-vs-experimental split is a working development affordance while bot work continues, and renaming now buys presentation the release does not need yet.*
> **So `Standard AI 0902` stays, date and all, and that is deliberate on the user's own say-so — do not "fix" it.** The two commits pulling in opposite directions (`d02f41d0` removing `0802`, `3318d5c7` re-adding `0902`) are now resolved in favour of keeping the stamp: it records WHICH snapshot the copy is, which is worth more during development than the presentation costs.
> **Revisit when bot work stops**, at which point the user has separately said they intend to rename both profiles — they floated *Staging / Main*, the agent countered *Development / Release* on the grounds that "stable" wrongly implies better-tested and "Main" collides with the git branch. **The noun is unsettled and is the user's to pick.** When it is picked, it is two lines: `ai.yaml:82` and `:89`.
> **What is NOT closed by this ruling:** the difficulty ladder, which the verdict above scopes as an engine design task (~340 lines of cloned YAML per rung today, or one `IBotInfo`-level scalar done properly). It should become its own item and it does not block release.

**R4. ~~The lobby's only AI opponents are "Experimental AI" and "Stable AI 0802".~~ → RE-SCOPED: names are fixed; the ladder and descriptions are not.** *(chrome)*
**Perceived:** in the one menu every single-player passes through, the opponent picker offers a lab name and an internal build date, with no difficulty ladder and no descriptions.
`ai.yaml:81-92` (**cite corrected 2026-09-05** — the old `:44-51` predates the file growing; `ModularBot@experimental` now opens at `:81`, `@stable` at `:86`, and the two `Name:` lines are `:82` and `:89`). **Note this collides with the bot ranking rule:** it is chrome, not bot intelligence, so it is cheap and it is a blocker — the bot can stay exactly as good as it is today and this still needs fixing. **Size:** minutes for naming/descriptions; a real difficulty ladder is larger and is a separate decision.

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

> ✅ **VERDICT 2026-09-01 (`main @ bd8e7290`) — SUPERSEDES THE BLOCK ABOVE. TWO OF THREE HALVES SHIPPED at `06333250`. RE-SCOPE TO HOTKEYS ONLY.** Every number below re-counted in the current file, not relayed. The region moved: `GARRISON_PANEL` is now at `ingame-player.yaml:610`, `CARGO_PANEL` at `:797` (region `:610-983`); the file is 1379 lines.
> - **Labels — DONE. `Text: X` no longer occurs anywhere in the file (0 hits).** The eight now read `Text: Out` at `:636, 653, 670, 687, 704, 721, 738, 755`. **The block above asserting `Text: X` at `:655, 669, 683, 697, 711, 725, 739, 753` is FALSE as of `06333250` and is retained only for its `ed5ee6b6` lesson.**
> - **Tooltips — DONE.** 11 `TooltipText:` inside `:610-983`, **zero of them inside comments** (the old count's comment-trap is gone because the comments are gone).
> - **Hotkeys — UNTOUCHED. `Key:` count inside `:610-983` is 0.**
> The 11 buttons are `EJECT_PORT_0..7` (`:630, 647, 664, 681, 698, 715, 732, 749`), `EJECT_ALL:784`, `UNLOAD_ALL_TROOPS:958`, `DROP_SUPPLY:974`.
> **The visible half — the wall of single-letter buttons — is fixed.** What is left is the least player-visible third of the finding, and R5's verdict already downgraded the hotkey class to COSMETIC.

**R6. ~~~50~~ ~~11 garrison and cargo buttons have no tooltip and no hotkey; 8 are labelled just `X`.~~ → RE-SCOPED: labels and tooltips shipped; 11 buttons still have no hotkey. COSMETIC.** *(chrome)*
**Perceived:** effectively fixed. The wall of `X` buttons now reads `Out` and every button explains itself on hover. `ingame-player.yaml:610-983`. **Size:** minutes, and only worth spending during final pre-release polish. Widened PIPELINE item 60 (shipped).

> ❌ **VERDICT 2026-09-01 (`main @ bd8e7290`) — STILL OPEN, 0 OF 5 SYMPTOMS FIXED.** Commits have landed since that *look* like they addressed it (`d7279968` "release identity: separate the two version strings", `60969fb8` the packaging audit re-stamp); **neither touches `mod.config`, the NSI script, or `Directory.Build.props`.** This is the failure mode R6 demonstrated — **a plausible-looking commit is not evidence; the file is.** All five re-verified present at this commit:
>
> | Symptom | Current state |
> |---|---|
> | Install dir | `mod.config:86` `PACKAGING_WINDOWS_INSTALL_DIR_NAME="OpenRA WW3MOD"` |
> | Registry key | `mod.config:90` `PACKAGING_WINDOWS_REGISTRY_KEY="OpenRAWW3MOD"` |
> | Start Menu folder | `packaging/windows/buildpackage.nsi:54` `MUI_STARTMENUPAGE_DEFAULTFOLDER "OpenRA"` |
> | `<Product>OpenRA</Product>` | `engine/Directory.Build.props:17` |
> | Crash-dialog FAQ | `mod.config:51` `PACKAGING_FAQ_URL="http://wiki.openra.net/FAQ"` |
>
> **Two more instances the original finding missed:** `buildpackage.nsi:128` writes to `$APPDATA\OpenRA\ModMetadata`, and `:138`/`:203` name the desktop shortcut `"OpenRA - ${PACKAGING_DISPLAY_NAME}"`.
> ✅ **RE-CONFIRMED 2026-09-02 (`main @ 6a7e1839`) — all seven instances still present, nothing has moved. Two `mod.config` cites corrected above (`:89`→`:86`, `:93`→`:90`); `mod.config:51`, `buildpackage.nsi:54/128/138/203` and `Directory.Build.props:17` are unchanged and exact.** This is the cheapest release-blocking item in the file and it has now survived two verification passes untouched.

**R7. The install chain identifies the product as OpenRA.** *(chrome; wave-2 install audit went deeper)*
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

> ❌ **VERDICT 2026-09-02 (`main @ 6a7e1839`) — THE HEADLINE IS REFUTED. "The loop is a dead end" is FALSE; the economy has no hole. DOWNGRADE, do not dispatch as filed.**
> The finding's trait census is correct and its conclusion does not follow. `AbsorbsSupplyCache` really does have **exactly one** declaration in `mods/` (`structures.yaml:594`, under `LOGISTICSCENTER:`; the `misc.yaml:424` hit is a comment) — but that trait is one of *three* supply paths, and the audit read only it.
> - **cache → truck WORKS, unconditionally.** `PickupSupplyOrderTargeter` (`DropsSupplyCache.cs:643-669`) gates on `target.Info.Name == SupplyCacheActor` + allied + both-sides-have-headroom, and **nothing else — no config flag.** It is yielded unconditionally at `:584`. Shipped in **`06dc950f` *"supply: a truck can pick supplies back up off the ground"* (2026-08-12) — four days BEFORE the audit that filed this finding** — with `tools/autotest/scenarios/test-truck-collects-cache/` covering it.
> - **truck → cache WORKS.** `DropSupplyCacheHere` merges into an existing cache on the cell rather than spawning a second one (`:174-190`, `existingProvider.AddSupply(amount)` + `[supply] crate-merged` log).
> - The cited `:705` is now `hostProvider?.Info.TotalSupply ?? 0`; the real `AbsorbsSupplyCache` gates are `:321` and `:694`.
> **`RELEASE_V1.md:52-53`'s "STILL OPEN 2026-09-01" note repeated the original error** — it re-read the one gate the audit named instead of asking whether *any* path existed. Correct it there too.

**R12. ~~A supply truck cannot replenish a dropped supply cache — the loop is a dead end.~~ → RE-SCOPED: both directions work; what is missing is a one-click cursor. SHOULD-FIX at most.** *(systems)*
**Perceived:** the round trip works, but topping up a cache means driving the truck onto the cache's cell and using the drop order rather than clicking the cache — discoverable only by accident. Pickup, by contrast, has its own cursor and works on a click. **Size:** minutes–hours, autotest-verifiable, no launch.

### SHOULD-FIX — noticed within the first few matches

**R14. Capturing a helicopter by pilot entry yields a burning wreck that explodes in ~12 seconds.** *(systems)*
**Perceived:** the player pulls off the capture — a genuinely cool mechanic — and the prize is speed-zero, firepower-zero and on fire. Either the reward works or the mechanic should not be presented. **Size:** hours mechanically, **but it needs a design call first**; autotest-verifiable.
> ✅ **VERDICT 2026-09-02 (`main @ 6a7e1839`) — OPEN, CONCLUSION CONFIRMED, STATED REASON PARTLY WRONG. Fix the premise before briefing anyone.** The gate is `CheckDisabledRecovery` at **`HeliEmergencyLanding.cs:412-429`** (not `:411-416`), live code called from `DamageStateChanged` at `:202`; it needs `disabledToken` set AND `health.DamageState < AutorotationDamageState` (`= DamageState.Heavy`, `:99`).
> **`Repairable` was NOT deleted** — it survives at `aircraft.yaml:208-209` (`RepairActors: hpad`). What was removed is named in-tree at `aircraft.yaml:356-358`: *"RepairableBuilding@CrashDisabled and Targetable@VehicleRepair removed 260509."* **The gate is unsatisfiable for three independent reasons, any one of which is sufficient** — so a fix that addresses only one changes nothing: (1) `ChangesHealth@CrashBurn` (`aircraft.yaml:259-262`, −2%/5 ticks on `crash-disabled`) drains monotonically and **every** `ChangesHealth` in the mod has a negative step, with zero heal auras or crates anywhere; (2) `SpeedMultiplier@CrashDisabled Modifier: 0` (`:327-329`) means it cannot travel to a repair actor at all; (3) `hpad` is `~disabled` (`structures.yaml:688`) and appears on **0 of the 10 shipped maps**.

**R15. ~~Vehicle commander substitution was never built.~~ → RE-SCOPED: substitution ships in YAML; only the INVERSE is missing.** *(systems)*
**Perceived:** a crew member's death degrades the vehicle and it never recovers, and nothing tells the player that. **Size:** unknown — a feature, not a fix; unit-testable.
> ⚠️ **VERDICT 2026-09-02 (`main @ 6a7e1839`) — OPEN, BUT THE HEADLINE IS ACTIVELY FALSE AND WOULD SEND A WORKER TO REBUILD SHIPPED CONTENT.** "Substitution was never built" is wrong: it exists, in **YAML rather than C#**, which is why a `VehicleCrew.cs` grep finds nothing. `^CrewedVehicle3` (`vehicles.yaml:323-353`) ships `@CommanderDrives` (40% speed when `!has-driver && has-commander`, `:329-331`), `@CommanderGuns` (50% turret, `:337-339`) and `InaccuracyMultiplier@CommanderGuns` (200%, `:341-343`), on 6 vehicles. **The commander already substitutes for a dead driver.**
> **What is genuinely missing is the inverse:** nothing promotes anyone *into* a vacated commander slot, so `@NoCommander` (150%, `:335-337`) sticks permanently. The `VehicleCrew.cs` slot API (`:557-691`) has no reassignment verb; manual re-crew via `EnterAsCrew.cs:73` → `FillSlot` is the only route back. **Unmentioned by the finding and arguably worse:** `^CrewedVehicle2` (`:297-305`) has no substitution in *either* direction — a lost driver is a permanent `Modifier: 0`. The audit's `:462-489` cite has drifted; `dea74837` (2026-09-01) added dismount/fan-out, not promotion.

**R16. ~~Cargo Phase 2D is sync-unsafe beyond R10, and Phase 3 was never built.~~ → SYNC HALF STALE; PHASE-3 HALF TOO VAGUE TO DISPATCH.** *(systems)*
> ❌ **VERDICT 2026-09-02 (`main @ 6a7e1839`) — DO NOT DISPATCH. Neither half is a workable brief.**
> **The vocabulary has no definition anywhere.** "Phase 2D" occurs **exactly once repo-wide — in this item's own stub.** It originates at `audit/260816-systems-completeness.md:51-53`: *"2A, 2B, 2C and 2E are complete. 2D (rally points) is built but sync-unsafe. Phase 3 … was never started."* So 2D's sync-unsafety **is** the eject-rally desync — the audit's own BLOCKER at `:55` — **which is R10, and R10 is closed** (`closed-items.md`, fixed `409b0fd2`, merged `c9f6a6c0`, generator deleted `7b5c692b`). **The word "beyond" was never supported by anything**, and re-verification found no second cargo sync defect named in the audit, this file, or `bugs/discovered.md`: the only `SetEjectRally` references in `engine/` are inside `Cargo.cs` (`:462-467`, `:628`, `:632`), no widget or generator survives, `Cargo.cs` carries **zero** `[Sync]` fields, and its sole public settable state `LoadingBlocked` (`:238`) is written from simulation.
> **Phase 3 is one line at `RELEASE_V1.md:138` with zero code hits** — it needs a design pass to become an item, not a worker. ⚠️ **Do not conflate it with `260722_phase3_redteam.md`**, which is the AI tactical-positioning phase and unrelated.

### POLISH

> ❌ **VERDICT 2026-09-01 (`main @ bd8e7290`) — STILL OPEN, VERBATIM.** The overstatement lives in `mods/ww3mod/chrome/ingame-info-howtoplay.yaml`, unchanged: `:123` *"reinforcements slow, then halt, and a red bar starts filling. If it"* · `:130` *"**fills, that side is out of the match.** Yours works the same way, so"*. `:137` does add *"push them off and it recovers"*, which softens the reversibility half — **but the specific sentence R9 names is present unchanged.** A fix is one line in one file.
> **Related and NOT the same defect — and the earlier note about it was itself stale, so re-read this rather than the version you may remember.** An abandoned 2026-08-19 branch recorded `SupplyRouteContestation.cs:73` carrying `"Supply Route overrun! Production and income frozen."` **Both halves of that cite are now wrong:** the field is at **`:98`** and reads **`"Supply Route overrun! Production frozen."`** — the "and income" claim was dropped at some point since. `PassiveTextNotification` is now defensible on its own terms, because contestation genuinely does freeze production.
> **What IS still a second instance of R9's defect is a different line: `SupplyRouteContestation.cs:580`** emits *"has lost their Supply Route! Production frozen."* — and **ownership never transfers** (see CLAUDE.md: contesting is not capturing), so "has lost their Supply Route" overstates in exactly the way the panel does. **Fix the panel and `:580`, not `:98`.**

**R9. The onboarding panel overstates Supply Route contestation.** *(chrome)*
It says losing the Route "puts them out of the match"; the shipped mechanic makes a player **passive and reversible** (`SupplyRouteContestation.cs:354-373`). Verified accurate otherwise — its Supply Route claims check out against `structures.yaml:222-390`. **Size:** minutes.

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
>
> **TRIAGE PASS 2026-09-02 (`main @ 6a7e1839`) — one grep or `git log -S` spent on the central premise of every open-looking item. Four premises were found FALSE and are corrected in place: R12, R16, 69 and 62's `rotor-stopped` line.** Items 57 and 65 were tagged `[SHIPPED — CLOSE]` but had never been archived, so they still read as live queue entries; both were re-verified against the file and are now in `archive/closed-items.md`. Items 32, 34 and 35 were re-cut against work that had landed since they were written. **Three new failure modes, all distinct from the two above and all worth carrying:**
> - **An item can be false at the moment it is filed.** R12 and R16 both describe defects that were already fixed by commits *predating the audit that filed them* (`06dc950f` beat R12 by four days). An audit finding is not a fresher fact than the code; it is just a more recent document.
> - **A re-verification can repeat the original error.** R12's 2026-09-01 re-check re-read the one gate the finding named and confirmed it, rather than asking whether *any* other path existed. **Re-verify the CLAIM ("the loop is a dead end"), not the CITATION.**
> - **A grep through `info.` is invisible to a grep for the string.** 62's "`rotor-stopped` has no grantor" was false when written because the grant reads `info.RotorStoppedCondition`, not a literal. Same shape as 69's grant-vs-geometry miss. **When a symbol looks unreachable, check the indirection before filing it as dead.**
> **And the cheapest structural lesson: `ai.yaml` line cites rot faster than anything else in the repo** — item 64's has now drifted four times, item 56's three. Record the KEY, not the line.

### Current user priorities — 2026-08-15 live-play batch

Framing for this batch (why 63/64 are not one item, and what 65 has to do with either) is in [`archive/session-notes.md`](pipeline/archive/session-notes.md). Items **63** and **66** from this batch are merged and archived to [`closed-items.md`](pipeline/archive/closed-items.md) — 66's *procurement ordering axis* dossier is still the reference for the unfinished lobby-verification arm.

### 64. Coordinated combined-arms push — the first tank attacks alone
`[PARTIALLY SHIPPED — the rendezvous is merged but SWITCHED OFF; the speed differential is untouched and is the visible half]`
**Perceived:** the opening push looks like a formation instead of a lone vehicle. Armour leads, a transport carrying infantry and a technician follows behind it, and the infantry arrive at the front protected rather than walking up on their own.
**More landed than "recon":** `ef608a62` publishes `PoiOffensiveBotModule.ForwardStagingAnchor` and folds it into the transport's drop-off via a new pure `RendezvousMath` — but `RendezvousWithOffensiveStaging` defaults **false** (`MountedTransportBotModule.cs:92`) and **`ai.yaml:1933`** (2026-09-02; was `:1723`, before that `:1635`, before that `:1625` — **this cite has now drifted four times, so grep the key and stop recording the line**) sets it false, so both profiles are byte-identical and this has never affected play. **The remaining work is (1) enable and measure, (2) the speed differential — infantry already walk to the same anchor from tick 3; the tank simply outruns them.** → [`items/64-combined-arms-push.md`](pipeline/items/64-combined-arms-push.md)

> ✅ **UPDATE 2026-09-01 (`main @ bd8e7290`) — THE KNOWN BLOCKER IS FIXED; THE ITEM IS NOW ONE STEP, AND THAT STEP IS A RUN.** `RendezvousMaxWithdrawCells` exists (default **6**, `MountedTransportBotModule.cs:109`, consumed at `:1589`) — that is the lower bound whose absence turned a 26-cell delivery into a shuttle.
> **It is still switched OFF — re-confirmed 2026-09-02 at `main @ 6a7e1839`.** Exactly one YAML site sets it: `ai.yaml:1933 RendezvousWithOffensiveStaging: false`, and the C# default at `MountedTransportBotModule.cs:92` is `false` too, so nothing anywhere turns it on. `ai-america.yaml` and `ai-russia.yaml` touch it nowhere, so the other twin takes the `false` C# default. **Both profiles remain byte-identical, and this has still never run in a live match.**
> **The speed differential is confirmed untouched** — no speed-matching, lead-hold or follower gate exists in the bot modules.
> **Net effect on dispatch: do NOT send a worker to "fix the rendezvous" — it is fixed. The next action is to flip the flag and measure**, which under the standing launch rule is the manager's to run, not a worker's.
> **One caveat worth carrying:** `RendezvousMathTest.cs:185` carries the comment *"MEASURED, NOT REASONED — run 260815_202509, seed 1017, `RendezvousWithOffensiveStaging: true`."* So the **maths** has been measured under a hand-flipped flag; **shipped play has not.** Do not read that comment as evidence the feature has run in a match.

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
Declared fixed to the user at least three times. **A green scenario does NOT close this item**; the acceptance bar is a full bot-vs-bot match on a real map, with an added precondition clause so "no truck was ever bought" reads as instrument failure rather than a negative result. → [`items/56-supply-truck-delivery.md`](pipeline/items/56-supply-truck-delivery.md)

> ⚠️ **THIS ITEM'S CENTRAL PREMISE WAS STALE AND IS CORRECTED HERE (2026-09-01, `main @ bd8e7290`). Read this before dispatching anyone — briefing a worker to "implement the blunt fix" would send it to write code that already exists.**
> The struck framing said the user's pre-authorised blunt fix was unbuilt, that disabling danger awareness meant **"seven sites, not one seam"**, and that one site read a field *"that no config flag reaches"*. **All three are false.**
> - **`IgnoreDangerForDelivery` reaches every danger gate in the module, including the `ThreatMapManager` reader.** Consumed at `SupplyFollowerBotModule.cs:721, 899, 930, 1490, 1826, 2299` — and **`:2299` is `FindSafeFollowPosition`, the `ThreatMapManager` site the dossier says no flag can reach.** The flag does reach it.
> - **Correcting the count while we are here: that is SIX consumption sites, not seven.** The "seven sites" figure has been repeated in three documents and none of them lists seven line numbers — the 2026-08-19 note that first challenged this claim still wrote "all seven sites" above a list of six. Declaration is `:125`; `:731-732` are debug-string interpolation, not gates.
> - **It is switched ON in shipped content.** `ai.yaml:1339` (2026-09-02; was `:1129`, before that `:1041`) sets `IgnoreDangerForDelivery: true` on the shared `SupplyFollowerBotModule@supply`. C# default is `false` at `SupplyFollowerBotModule.cs:125`, so the YAML is doing the work. **Six consumption sites re-confirmed at `:721, 899, 930, 1490, 1826, 2299`** — unchanged since the 2026-09-01 correction.
> - **That instance is `enable-ai-any`, so this reaches `@stable` too** — a knowing, visible improvement flowing to the control, per CLAUDE.md policy. **The next benchmark baseline must be re-taken knowingly.**
>
> **So item 56 is a RUN, not a dispatch.** Its acceptance bar is unchanged and still binding. **Watch in the same match:** `test-supply-safe-front-keeps-cargo` is RED (the truck drops when it must not) and is unrefuted — if the live match looks good while that stays red, the two disagree and the scenario is the one to trust less, per this item's own founding lesson (*a test bed that always reaches a state cannot reveal a broken transition into it*).

> **2026-09-05 (`main @ 40577269`): the follow-path half is now built and measured.** Recon (`e071a500`) found the drop errand already commits but the FOLLOW path re-picked its cluster every scan with no memory; `40577269` adds `ClusterStickinessNeedMargin` (ai.yaml 1000 on the shared instance — reaches `@stable`). `test-supply-two-clusters-commit` went RED at `21690781` (6 reversals, delivery t1475) and GREEN at HEAD (0 reversals). **Scope correction:** at shipped config a full truck drops on its first scan and never reaches the follow path, so the churn only ever bit partial loads. **The acceptance bar above is unchanged and still owed:** one bot-vs-bot match on `tournament-s1-eco-river-zeta`, precondition `[composition] census` shows `earned>0` and a non-zero `truk`, then `crate-placed ÷ drop` against the 15.0% recorded at `c9626273` and the `drop-declined reason=` histogram against `NoDemand` 54.1%. `test-supply-safe-front-keeps-cargo` is RED **by configuration** (`IgnoreDangerForDelivery` makes `SafeFront` unreachable), not unrefuted — retire or re-spec it (backlog).

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
~~(b) is **blocked on test design, not engine work** … **A run today would be worthless.**~~ — **SUPERSEDED, see below.** The engine premise is untouched: `PreemptScanInterval: 25` at **five** inheritance bases (`defaults.yaml:396, 641, 726, 752, 833` — the old cite said four bases at `:329, 574, 659, 756`, wrong in both count and position), C# default still `0` at `AutoTarget.cs:212`, preempt read now at `:1088`. → [`items/44-aa-autotarget-arithmetic.md`](pipeline/items/44-aa-autotarget-arithmetic.md)

> ✅ **UPDATE 2026-09-01 (`main @ bd8e7290`) — THE TEST REDESIGN LANDED. 44(b) IS NOW BLOCKED ON A RUN, NOT ON DESIGN.** `test-autotarget-preempt-air` asserts **attribution rather than outcome** — the old assertion could not fail, which is why the earlier RED control passed. It now requires the helicopter hit **AND** `UncommittedScanCount` unmoved since HIND arrival (`test-autotarget-preempt-air.lua:137, 163, 196`); the unaided route must pass through an uncommitted scan while preemption bypasses `ScanForTarget`. **So it discriminates by construction.**
> **Two things to carry.** (1) It touched **engine code, not just the test** — `AutoTarget.cs:343` declares `UncommittedScanCount` (incremented at `:1166`) and `TestGlobal.cs:1195` exposes `Test.GetUncommittedScanCount`. It is claimed diagnostic-only and non-`[Sync]`; **that is the author's claim and has not been independently re-verified** — if a benchmark moves unexpectedly, look here first. (2) **No RED control has ever been run against the new assertion.**
> **Next action is a RED+GREEN pair on one scenario** — per `AUTOTEST.md`'s standing rule the green is not evidence without the red. Under the standing launch rule the manager runs it.

### 45. Missile system
`[BOTH previously-open deliverables DISCHARGED; ONE NEW open question named by the spec itself. Javelin still PARKED — do NOT re-dispatch it]`
**Perceived:** missiles behave the way the user expects. The user's severity read: *"has worked OKAY except the occasional misses… not catastrophic, but it breaks at some points."*
**`DOCS/reference/missiles.md` now exists AND §2–3 define the class taxonomy and the per-class miss-detonation rule** (`85d146c8`) — both things this item listed as outstanding. **But that is a SPEC, not an implementation**, and it names its own successor: the detonation test still measures to the aim point while the miss test now runs on physical separation, so the two are not commensurable — `ATGM` rolls `Inaccuracy: 512` against `CloseEnough: 298`, and a missile can sit physically inside the proximity radius without fusing. **That changes when every missile in the game detonates and needs its own measurement.** → [`items/45-missile-system.md`](pipeline/items/45-missile-system.md)
> ✅ **RE-VERIFIED 2026-09-02 (`main @ 6a7e1839`) — SPEC CONFIRMED SHIPPED AND THE SUCCESSOR QUESTION IS CONCRETE ENOUGH TO DISPATCH.** `DOCS/reference/missiles.md` is a real 37 KB doc created by `85d146c8` (a doc *creation*, checked against the diff — not the docs-edit misread that has bitten this file before); §2 "Weapon classes" and §3 "The miss-detonation rule — per class" both present.
> **⚠️ TRAP for whoever picks this up: `CloseEnough` does not appear in ATGM's YAML at all.** `Inaccuracy: 512` is at `weapons-missiles.yaml:12`, but `CloseEnough: 298` is the **engine default** at `Missile.cs:203` — so grepping the weapon for it returns nothing and reads as though the comparison is invented. It is not. `Missile.cs:895-900` already carries a `PITFALL` naming this exact incommensurability. **Autotest-shaped, no launch to design.**

### 46. Release artwork and audio — every asset slot is still empty or still somebody else's
`[ALL SLOTS RE-VERIFIED OPEN 2026-08-19 — nothing closed, one slot is WORSE than filed]`
**Perceived:** the game stops looking like a mod of another game at every point before the battlefield. Today the mod chooser shows stock Red Alert's icon and a stock install plays exactly one music track on infinite loop.
All user-side art/audio production; tooling and wiring are done and merged. **Load screen confirmed empty at PIXEL level, not by directory listing** (`loadscreen.png` left half 0/65536; `-2x` 0/262144; `-3x`'s pixels sit outside the logo area). **All 15 Russian cameo files are md5-identical to their America twins — not just `e3`.** Only the installer icon set cannot be settled in-repo. → [`items/46-release-art-audio.md`](pipeline/items/46-release-art-audio.md)
> ✅ **RE-CONFIRMED 2026-09-02 (`main @ 6a7e1839`) — ZERO DRIFT since 08-19, and the dispatch verdict is DO NOT DISPATCH.** Re-measured on disk rather than read from the docs: **15 of 15** Russian cameos still md5-identical (`aa, ar, at, e1, e2, e3, e4, e6, medi, mt, sf, sn, spy, tecn, tl`; `dr` still has no Russian variant at all); `loadscreen.png` still 0/65536 non-transparent in its left half; **exactly one music track ships** (`bits/sounds/music/journey.aud`); `mods/ww3mod/icon.png` is still byte-identical to `engine/mods/ra/icon.png`. **Every slot needs art or audio a worker cannot author — this belongs in `AWAITING-USER.md`, not the worker queue.** The only worker-shaped fragment is a 2-line `dr` sequence edit, and it is pointless until a `dr` Russian image exists.

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
**Done:** the cordon half landed and came out green (`67888986` + `097738f4`), so **the "adding cordons will hard-fail nav-guard" warning is spent**; and `ResupplyBehaviorSelectorLogic` now routes Evacuate through an order (`e49ff242`), as item 60 was predicted to do. **Still open, both small and both YAML/desk-only:** nav-guard covers exactly 10 maps (`tools/nav-guard/baseline.json`, `states.live` has 10 keys); and the `Versus` tables are still wrong verbatim — `IskanderTargeter`'s `Warhead@Target` (`weapons-missiles.yaml:394-401`) zeroes **Brick**, an armour class this ruleset does not have, while omitting Unarmored / Kevlar / Indestructable, which it does — and an omitted class takes the unmodified 100%, so omission is the opposite of a zero. `HIMARSTargeter` inherits it unchanged.
> ❌ **CORRECTION 2026-09-02 (`main @ 6a7e1839`) — THE `rotor-stopped` LINE IS FALSE AND WAS FALSE WHEN WRITTEN. Do not dispatch it.** The struck claim was *"only `rotor-stopped` has no grantor, consequence 6 permanently-dead traits and rotors that never stop."* **It has a grantor:** `HeliEmergencyLanding.cs:405-409` — `OnRotorsStopped()` grants `info.RotorStoppedCondition`, which defaults to `"rotor-stopped"` at `:79`; sole caller `HeliAutorotate.cs:65`. In-tree since **`52285daa` (2026-03-24)**, already an ancestor of the `5890b053` at which the claim was filed. **The grep missed it because the grant goes through `info.`, not a string literal** — which is the same class of error as item 69's grant-vs-geometry miss, and worth naming as a pattern rather than an accident. The lint entry is scenario-only and was reclassified as a deliberate exception in `lint-baseline.txt:195-218` (`017410b7`).
> **What narrowly survives, unsettled:** the only grant path is the post-autorotation wind-down, so a helicopter that lands *normally* may never receive `rotor-stopped`. That could not be settled by reading. It is a small autotest-or-launch question, **not** the six-dead-traits defect that was filed. → [`items/62-unrepresented-residue.md`](pipeline/items/62-unrepresented-residue.md)

---

### Live-play batch 2026-08-08 — transports

Batch framing and the two closed bullets are in [`archive/session-notes.md`](pipeline/archive/session-notes.md).

### 34. Transport pickup coordination — a tactical layer for humans AND bots
`[RE-SCOPED 2026-09-02 — the BOT half shipped and is ON; the HUMAN half is the item, and it has a blocker nobody has costed]`
**Perceived:** you order soldiers into a transport and it just works — the vehicle drives to them, waits, collects everyone nearby, then carries on with its queue. Today it drives off without waiting and the player has to micro it.
> ⚠️ **"Recon landed, nothing implemented" is STALE.** The wait-for-everyone behaviour shipped for bots on 2026-08-15: `FillBeforeDeparture` (`MountedTransportBotModule.cs:182`, C# default **false**) is set **true on BOTH profiles** — `ai.yaml:1848` (`@poi`/stable) and `:1906` (`@experimental`), with `BoardingStallTicks: 250`. **This is the rare case where the switched-off trap does NOT apply: it is genuinely enabled.** Landed `7286a15f`, merged `97289e48`.
**So the item is now the HUMAN path only, and that is what the user asked for anyway.** `RideTransport.cs` is the whole human path and it is one-directional: the *soldier* walks to the transport; the transport never moves toward the soldier. No wait/gather field exists on `Passenger` or `Cargo`.
**⚠️ BLOCKER, uncosted, and it invalidates the user's own phrasing:** `Cargo.cs:552` → `LockForPickup` (`:571`) calls `self.CancelActivity()` on the transport (`:578`). **A passenger reserving a seat wipes the transport's shift-queue**, silently. "…then carries on with its queue" cannot work until that is fixed, so this is two pieces of work, not one. Promoted to `conventions.md` in `2f9a6688`. → [`items/34-transport-pickup.md`](pipeline/items/34-transport-pickup.md)

### 35. Use transports for the opening derrick rush
`[RE-SCOPED 2026-09-02 — the diagnosis this item asks for HAS ALREADY BEEN RUN. Do not dispatch it as written.]`
**Perceived:** the early game land-grab looks planned — technicians ride to the money structures instead of walking the length of the map while transports sit idle nearby.
> ⚠️ **The stub's "find out why the shipped, enabled ferry does not visibly fire" is a task that was completed on 2026-08-15 and never folded back in.** `WORKSPACE/ferry-escort-scoping.md` (`0c59c7ad`) §5 reads the `ferried=` log the stub points at: **12 capture orders, 11 `ferried=False`, 1 `True` — ~8%.** The `True` at position 6 refutes the one-shot-latch hypothesis. **Cause identified: carrier starvation** — `TryReserveCaptureFerry` (`MountedTransportBotModule.cs:525-560`) requires a carrier both untasked *and* `cargo.IsEmpty()`.
> **The enablement claim holds** (checked, because "enabled on both profiles" is exactly the claim that is usually wrong here): `UseTransportForDistantCaptures` C# default **false** at `CaptureCoordinatorBotModule.cs:175`, set **true** at `ai.yaml:190` (`@experimental.tecn`) and `:2476` (`@stable.tecn`), no override in either faction file. Log at `CaptureCoordinatorBotModule.cs:1798-1799`.
> **One dossier line is false:** it claims transports are still eligible as combat units. `IsEligibleCombatUnit` (`PoiOffensiveBotModule.cs:2585`) ends `!UnitRoleResolver.IsTroopCarrier(a.Info)`; the exclusion landed `55326b3e` (2026-07-22) and `UseUnitRoles: true` on both profiles.
> **Caveat on the 8%: it is one match**, and I did not re-derive it from a raw log.
**Re-cut to: raise the ferry rate by fixing carrier supply.** First step is instrumentation, not a fix — `[ferry] refused reason=` does not exist (zero grep hits), so today a refusal is invisible. → [`items/35-derrick-rush-transports.md`](pipeline/items/35-derrick-rush-transports.md)

---

### 32. Faction balance audit — RU testing + US-vs-RU imbalance detection **[IN FLIGHT 2026-08-02, user-gated on runs + sign-off]**
**Perceived:** RU bots get the same test coverage as US bots; a measured verdict on whether US-vs-RU is imbalanced; and any stat rebalancing goes through an explicit user sign-off flow. From user 2026-08-02: mirror tests (US/US, RU/RU) isolate bot skill from faction imbalance; US-vs-RU probes measure the imbalance itself; **"I do not want you to change any unit stats without my explicit review and approval."**
> ✅ **RE-CUT 2026-09-02 (`main @ 6a7e1839`) — the 2026-08-20 stale flag was right and this is the re-cut it asked for. ALL THREE PARTS ARE AUTHORED; WHAT IS LEFT IS RUNS AND DOC HYGIENE.** The `[IN FLIGHT 2026-08-02]` tag is retired: nothing is in flight.
> - **(a) DONE in substance, STALE in its numbers.** `WORKSPACE/balance/260802-parity-audit.md` is a real 22 KB audit (pair-by-pair stat tables §2.1–2.9 ground, §3.1–3.5 air, AI-config asymmetry §4, findings §5, measurement plan §6). But it has decayed against shipped YAML: **§2.8's "humvee 450-cost" is wrong** (`vehicles-america.yaml:45` reads `Cost: 500`, `d4b0c061`; HP halved by `ff14ece3`), §2.6's tunguska duplicate `Health` is **gone**, §3.2's Mi-28 bug is **fixed** (`bba63d11`), and the air-armour-class work (`48ac091e`, `15b6201a`) invalidates §3's targeting claims. Several line cites drifted (abrams `A:481`→`:494`, HIMARS `A:1015`→`:1059`).
> - **(b) DONE — authored, never run.** Four scenarios exist under `tools/autotest/scenarios/`: `tournament-parity-mirror-us`, `-mirror-ru`, `-cross-usru`, `-cross-usru-swapped`, each complete with `tournament.yaml`. Factions verified in their `map.yaml`s; `Bot: stable`. Authored in `962bd0e6`, whose own message says *"— NOT run"*. **No result artifacts exist anywhere in the tree.**
> - **(c) DONE — flow in use, nothing pending, one doc defect.** `001` APPLIED, `002` SUPERSEDED (live question is user-side at `AWAITING-USER.md:87-101`), **`003` still reads `Status: PROPOSED` while its YAML already shipped at `bba63d11`** — already known and flagged at `AWAITING-USER.md:206`. `WORKSPACE/balance/README.md`'s proposals table lists all three as PROPOSED and is stale. **No proposal landed without approval** — 003 was an additive bug fix, not a stat re-price.
> **So this is a RUN plus an hour of desk work, not a dispatch at unbuilt code.** The runs are the three `run-tournament.sh` invocations in audit §6 (20 seeds each) and are the manager's to authorise; §5's eight SUSPICIOUS pairs are unmeasurable without them. **Not settled by reading:** whether the four scenarios still load cleanly — `4f4a1993` inset their `Bounds` after they were authored.

_Three parts: (a) static parity audit — US vs RU roster stat/cost comparison from YAML alone, no game runs; (b) mirror + cross-faction test configs authored ready-to-run (runs need a user grant, see `AWAITING-USER.md`); (c) proposals land as numbered docs in `WORKSPACE/balance/` — evidence, proposed change, expected effect — each individually signed off by the user before any YAML edit. Worker on `auto/balance-audit`._

### 22. Case 01 — forest ambush measurement (`cases/case-01-forest-ambush.md`) — **AWAITING ONE USER YES/NO, NOT MORE MEASUREMENT**

> ✅ **VERDICT 2026-08-19 (`main @ 5890b053`) — THE REFRAME IS DONE; WHAT IS OWED IS RATIFICATION, NOT AUTHORSHIP.** `918bf38b` is an ancestor. This entry says the bar "must reframe to *def casualties ≤ X AND att casualties ≥ Y over N seeds*" — **that exact shape exists**, variance-backed, in the case file's 2026-07-29 entry: **Bar A** = *mean def cost-loss ≤ 50cr AND mean att cost-loss ≥ 300cr over ≥6 seeds* (mined batch scores 0 / 350 → GREEN), plus optional per-seed hard guard **Bar B** = *every seed def = 0*. Teeth sit on the zero-variance defender axis; the attacker clause is deliberately soft because it is noisy (kills {4,3,5,4,2,3}, σ≈1 kill).
> **Read this before opening the file:** its own `## Bar` header at `:23-27` still says "NOT ratified… ratify before the bar gates autoburn iteration". True, but it reads as though no candidate exists — **the candidate is 14 lines further down.** The header is stale relative to the body.
> **Next step is a single user yes/no on Bar A (+B). No run is needed to get there.**

> ⚠️ **RE-FRAMED 2026-08-20 by the ambush research programme — the ASK is unchanged, what the test MEASURES is not. Do not close this on the strength of the research.** Re-verified at `main @ 57822b4e`: the bar numbers are live and still unratified (`parse-case01-bar.py:33-36` — `BAR_A_DEF_MAX_MEAN = 50`, `BAR_A_ATT_MIN_MEAN = 300`, `BAR_A_MIN_SEEDS = 6`, `BAR_B_DEF_MAX = 0`), so `AWAITING-USER.md` §4 is genuinely open.
> **But the scenario grants its own gate.** `test-case01-forest-ambush.lua:3` posts the defenders as *"USA, HUMAN, Ambush stance, `enable-ambush-tactics` granted"* — a configuration **no human player can reach in a real match**, because nothing outside `LaneAmbushBotModule` and the autotests' own Lua grants that token (item **68**). A green here prices the **bot's** ambush, not the one the player gets.
> **And the defence it measures rests on a cover term that is unreachable** (item **69**): the `+1/+2/+3` `object-proximity` ladder has one emitter repo-wide and no soldier can stand inside its radius.
> ⚠️ **SUPERSEDED 2026-09-02 — that sentence is now FALSE and it is the load-bearing half of this caveat.** Item 69's own verdict, 30-odd lines below in this file, records that `247408b8` put `^TreeCover` on `^Tree` (`decoration.yaml:56-60,141`), so **living trees emit `object-proximity` and 22.7% of in-bounds cells on woodland-warfare receive some bonus.** The cover term is reachable. **The dated block above is left unedited per the supersede-don't-rewrite rule — but do not carry "the defence rests on an unreachable term" into a dispatch.** The rest of the caveat (the scenario grants its own `enable-ambush-tactics` gate, so a green prices the *bot's* ambush) is untouched and still binding. `AWAITING-USER.md:123`'s reassurance that the correction "widens the defenders' margin" was written before that was known — **it is not wrong about the bar, but it is no longer the whole picture.**
> **Consequence, stated plainly:** ratifying is still worth one word — it makes a red result actionable instead of arguable. Just do not read a green as *"ambush works for players."* **Do not cite this scenario as evidence the coordination works** (`ambush-programme/README.md` §5): it is ~1000 commits stale and never asserts simultaneity.

**Perceived:** the payoff of 20+21, proven by a number: an equal-cost force walking into the treeline ambush is destroyed at ~3× the defenders' losses, repeatably.
_Scenario authored (`tools/autotest/scenarios/test-case01-forest-ambush/`, scripted attacker + defender squad under test); calibration batch RUN. Finding: the provisional **1:3 cost-weighted ratio is ill-posed** — a holding concealment drives defender losses to **zero** (÷0), so the bar must reframe to "def casualties ≤ X AND att casualties ≥ Y over N seeds" (DISCOVERIES 2026-07-28). **Bar ratification awaits user** before iterating to GREEN. Detect-enabled fire-lane variant authored as case-01b (`4846a60a`)._

### 39. Branding and release polish — the product introduces itself as WW3MOD
`[Phase C polish, NOT new v1 scope]`
**Perceived:** the game stops introducing itself as somebody else's. Nothing about the battlefield changes — this is the frame around it, and it is the first thing a new player reads.
Overlaps items 46 and R7. The asset-licensing half was split out as item 41. → [`items/39-branding-release-polish.md`](pipeline/items/39-branding-release-polish.md)

---

## SAFE WINS & AMBITIOUS SWINGS — 2026-09-02 proposals intake **[items 74–83]**

> **Source:** [`proposals/260902-safe-wins-and-swings.md`](proposals/260902-safe-wins-and-swings.md), assembled from five 2026-09-02 recon documents plus two pip notes and re-verified against code at `main @ 9b687fef`. **Every `file:line` in it was opened and read**, not relayed — a discipline that killed one proposal outright, refuted a headline sentence, corrected a cost model and found one item stronger than filed. Its own "Killed on verification — do not re-propose" list is load-bearing: **a Logistics Centre supply readout, "the game never says the SR is indestructible", "the detection margin is computed then discarded", and a self-concealment readout all SHIP.** Check that list before proposing anything adjacent.
>
> **⚠️ Position in the queue is NOT a ranking.** This block sits at the end of the ungated queue because it is new intake, not because it ranks below items 64/40/56. **Ordering these against the existing queue is an open call for the manager or the user** — nothing here has been ranked against anything above it.
>
> **Eight of the ten safe wins are NOT filed here, because workers are already implementing them** (2026-09-02): safe wins **1, 2, 3, 4** (bundled with 1), **5** (decoration half only), **6, 7** and **10**. Filing them would immediately reintroduce this queue's most expensive recurring defect. Branch corroboration at filing time: `wt/capture-affordance` (1+4), `wt/howtoplay` (2), `wt/damage-readout` (5), `wt/evac-refund` (6), `wt/contest-alarm` (7). **No branch could be named for safe wins 3 and 10** — both premises re-verified still-absent on `main`, so they are unstarted or unmerged, not shipped. **If one of those eight is later found never to have landed, re-file it from the proposal — do not assume this block covers it.**
>
> **The two safe wins below are here because each is blocked on something a worker cannot supply**, not because they are leftovers. **The eight swings keep their bet framing deliberately.** Three of them — 78, 81, 82 — have one-line or few-line diffs and are swings anyway: **a small diff against the mod's central mechanic or against seven units' engagement ranges is a balance change, not a safe win.** Do not let that distinction be lost by re-summarising them.

### 74. Neutralising an enemy building announces it to the Neutral player and to nobody else
`[NEEDS A USER CALL BEFORE ANY CODE — balance-adjacent, deliberately not being built]`
**Perceived:** your rifleman turns an enemy AA gun grey after a full minute inside it. No voice line, no text, no sound — and the player who just lost it is told nothing.
**Deliberately not in flight.** `game-model.md` already tracks soldier-neutralisation as close to unanswerable against a bot and a live balance risk; **making it audible will make players use it more.** That may well be right — an invisible dominant strategy is worse than a visible one — but it is the user's call, not a quiet ship. **Put the question, not the diff.** → [`items/74-neutralise-notification.md`](pipeline/items/74-neutralise-notification.md)

### 75. Infantry give no selection feedback at all
`[BLOCKED ON A SCREENSHOT PASS — two lines to change; the judgement is the whole item]`
**Perceived:** you box-select six riflemen and nothing on screen changes. No bracket, no highlight, no outline.
`ShowNever: true` occurs exactly once under `mods/` (`infantry.yaml:56`) against an engine default of `false`. **Blocked on exactly one thing: nobody can judge this by reading, and no screenshots could be taken the day it was filed.** `ShowNever` was almost certainly set on purpose. **Show the user two screenshots; do not delete the line.** → [`items/75-infantry-selection-brackets.md`](pipeline/items/75-infantry-selection-brackets.md)

### 76. Wake up the vehicle that stopped fighting
`[SWING — moves every armed vehicle on BOTH bot profiles; must be measured, not reasoned]`
**Perceived:** a damaged tank drives over, points at the enemy, and sits there for the rest of the match — it will not shoot when repaired, react when something drives past, or go for ammo if a truck parks beside it.
One root, four consequences: pausing an armament zeroes the autotarget scan radius, so the unit goes sensor-blind, its attack order never ends, it never re-fires the becoming-idle transition that asks for resupply, and the readout built for this cannot fire. **`4bbd0fad` fixed the cursor half only — do not read it as having addressed this.** → [`items/76-paused-armament-lockout.md`](pipeline/items/76-paused-armament-lockout.md)

### 77. The enemy Supply Route promises a move order and a health bar, and honours neither
`[SWING — cheap half is hours; the valuable half is a design decision]`
**Perceived:** you right-click the enemy SR with your whole armoured force. The cursor says *move*. Your army drives across the map, parks on it, and stands there being shot at, firing at nothing.
**The originating audit's "nothing ever told you it cannot be damaged" is FALSE** — the How To Play panel says it verbatim (`ingame-info-howtoplay.yaml:88-95`). The surviving defect is narrower: the panel says one thing and the cursor promises the opposite at the moment of the click. **The valuable half is the same shape as the sin `Passenger.cs:116-121` was reverted for** — silently reinterpreting one order as another. → [`items/77-enemy-sr-order-honesty.md`](pipeline/items/77-enemy-sr-order-honesty.md)

### 78. Evacuation goes to the nearest wall, not home
`[SWING — one-token diff, a balance change wearing a bugfix's clothes. ⚠️ The proposal's author flagged this as the entry they were LEAST confident about.]`
**Perceived:** a wrecked tank deep in enemy territory banks its refund through *their* back edge, uninterceptable — so a deep raid is a free option.
The mechanism is verified (aircraft use `self.Owner.HomeLocation`, ground uses `self.Location`; nine of ten maps have no `spawnarea`). **The PREMISE is not: nobody checked whether a unit in the enemy half is actually closer to the enemy edge, often enough to matter.** A free static settlement exists — spawn-and-bounds arithmetic over the ten maps, no launch. **Do that first; if it says the nearest edge is usually the owner's own, DROP this item.** → [`items/78-evacuation-edge-choice.md`](pipeline/items/78-evacuation-edge-choice.md)

### 79. Contestation should push the beachhead back
`[SWING — new gameplay on the mod's central mechanic; small blast radius, real balance question]`
**Perceived:** enemy units grinding your SR make your reinforcements arrive *in the wrong place*, not just more slowly — the drop point slides along the map edge and every unit has a longer, more exposed walk.
Both traits sit on the same actor and the edge choice funnels through one variable, so this is an `Info` field defaulting to zero displacement. **The bet: it STACKS two penalties on the losing player**, which can make comebacks worse — the opposite of what a graduated design is for. The honest version probably *replaces* part of the slowdown. → [`items/79-contestation-entry-displacement.md`](pipeline/items/79-contestation-entry-displacement.md)

### 80. A player-facing channel for "your shot did nothing"
`[SWING — the detector already runs in every shipped build; this is routing plus a design call]`
**Perceived:** a shot that connects and accomplishes nothing looks identical to one that hurt. With no health bar and a four-band pip, dozens of hits on a high-HP vehicle change nothing visible.
The anomaly gate runs on **every warhead application in the game** and already builds the string — gated on a developer checkbox. ⚠️ **HARD CONSTRAINT: do NOT do this by turning `DamageNumbers` on** — that default is test-guarded and was ruled a release blocker (former R17). And armour is only *one* reason a shot does nothing, so the channel can only ever be a true partial explanation. → [`items/80-ineffective-shot-channel.md`](pipeline/items/80-ineffective-shot-channel.md)

### 81. Enemy aircraft cannot contest a Supply Route; friendly aircraft defend one
`[SWING — ONE-LINE DIFF, and the clearest case of a cheap diff that is not a safe win. USER CALL.]`
**Perceived:** you park a gunship over the enemy beachhead and the bar does not move — while a friendly gunship over *your* SR counts its full purchase price as defensive value and triples recovery.
`IsRelevantActor` applies two different tests: an enemy actor must match `CaptorTypes`, an allied one need only cost money. Every aircraft is `Types: Plane` and `SUPPLYROUTE` overrides nothing. **The two possible fixes point in OPPOSITE balance directions** — adding `Plane` makes helicopters a cheap siege tool against a beachhead with no AA; excluding allied air is conservative. **Present as a question, not a diff.** → [`items/81-aircraft-contestation-asymmetry.md`](pipeline/items/81-aircraft-contestation-asymmetry.md)

### 82. Let a specialist stand off at its long weapon's range
`[SWING — seven YAML lines that are a balance change on seven multi-role units]`
**Perceived:** your Bradley has anti-tank missiles that reach a long way, and drives all the way into autocannon range — into the tank's own gun — before shooting.
`EngageAtLongestArmamentRange` defaults false and **exactly one actor opts in** (`tunguska`); the shipped default takes the *minimum* of every valid armament's range. The engine's own doc comment names the symptom in the player's words. **Wants `tools/combat-sim/` numbers BEFORE, not after, and goes through item 32's balance-proposal flow** — a range-engagement change is a stat change in all but name. → [`items/82-longest-armament-standoff.md`](pipeline/items/82-longest-armament-standoff.md)

### 83. The reserve remembers — veterans come back as veterans
`[SWING — LARGE. Reason (ii) in the dossier could kill it outright.]`
**Perceived:** you pull out a three-chevron Abrams and it comes back as *"Abrams (Veteran III)"* — cheaper than fresh, with its chevrons and a full magazine. Today "rotate out" means *sell*.
Closes the largest gap between what this game says it is and what it does: `supply-route.md` calls the SR where units muster from off-map reserves, **and there are no reserves.** Also fixes a real hole — veterancy is the only appreciating asset in this economy and the refund arithmetic cannot see it. ⚠️ **Its UI surface is the same unstarted work as "Cargo Phase 3", which R16 ruled too vague to dispatch — if that is hard, this is hard for the same missing design.** → [`items/83-veteran-reserve.md`](pipeline/items/83-veteran-reserve.md)

---

## AMBUSH, CONCEALMENT & COVER — 2026-08-20 research programme **[USER-GATED: NOTHING IN THIS BLOCK MAY BE IMPLEMENTED]**

> **Two hard gates, both the user's, both load-bearing. Neither is a manager call.**
>
> 1. **Nothing on stances, ambush, concealment or cover may be implemented until the user says so.** Verbatim: *"I will let you know when we are ready to implement, until then just ask me"* and *"it is my wish that you really get to the bottom of this before we start implementing."* This block therefore sits **last in execution order only because it is gated** — not because it ranks low. It is the most recent and best-evidenced work in this file.
> 2. ~~**Item 67 lands BEFORE item 69.**~~ **DISCHARGED BY EVENTS 2026-09-02 — both legs of it.** Ruled 2026-08-20 (`57822b4e`): repairing the cover ladder first makes a currently-unreachable invisibility tier reachable, so the visibility floor is cheap now and urgent later. **Leg 1 fell when cover became reachable** (item 69's verdict: `247408b8` shipped tree cover and it is enabled). **Leg 2 fell when the visibility floor itself shipped** — `1ff73ae5` + `1ad638e7`, both 2026-08-20, both on `main`; see item 67's verdict. **The user's ruling is not overridden here — it is spent.** Neither item now waits on the other.
>
> **What the research settled, and it inverts the framing every earlier ambush item used: ambush is NOT a feature to be built — it shipped.** `UnitStance { HoldFire, Ambush, FireAtWill }` is live at `AutoTarget.cs:22` (re-verified here at `main @ 57822b4e`) with a bound button, a gold `A` glyph, pre-aim, a coordinated spring, a five-trigger state machine, five passing autotests and both bot profiles. **Nine strands were dispatched and every one that examined a mechanism found it already built and quietly broken, not missing.** An item reading "design ambush", "build the ambush stance" or "add hold-fire" is describing merged work — the implementation is archived as **item 8** in [`closed-items.md`](pipeline/archive/closed-items.md).
>
> **Programme: [`ambush-programme/README.md`](ambush-programme/README.md).** The ninth strand, [`recon/260820-ambush-cover-detection-audit.md`](recon/260820-ambush-cover-detection-audit.md), is the only document there with a second independent pass behind it and **outranks the other eight wherever they disagree** — it already overturned one confident, widely-repeated, wrong story. **Defect detail belongs in [`bugs/discovered.md`](bugs/discovered.md), not here. These stay stubs.**
>
> **Deliberately NOT queued: a legibility / readout item.** That is precisely the mistake manager decision 08 records — shipping a readout *around* an absent behaviour and reporting it as the answer. Legibility is a sequencing rule, not a scope rule: it follows a behaviour fix, it does not substitute for one.

### 67. ~~Clamp minimum detectability to 1 — nothing is ever fully invisible~~ → RE-SCOPED: the user's stated case SHIPPED 2026-08-20; what survives is a different, far more expensive change
`[user-gated. The "MUST LAND BEFORE ITEM 69" sequencing ruling is DISCHARGED — see below. **Re-priced from "one-line clamp" to "moves fog rendering, radar and the AI belief layer".**]`

> ❌ **VERDICT 2026-09-02 (`main @ 26f9cec0`) — "NO CLAMP EXISTS" IS FALSE, AND THE CHEAP HALF OF THIS ITEM IS ALREADY SHIPPED AND TEST-GUARDED. Do not dispatch anyone at "add the clamp".** Read from `Detectable.cs` directly, not relayed from the proposal that flagged it.
> - **The clamp exists: `Detectable.ClampConcealment` (`engine/OpenRA.Mods.Common/Traits/Modifiers/Detectable.cs:118-125`)** — floors at 1, ceilings at `MapLayers.VisionLayers - 2` = **9** (`VisionLayers = 11`, `MapLayers.cs:75`). Called from `ITick.Tick` (`:129`) and `IsVisibleInner`. ⚠️ **The file is under `Traits/Modifiers/`, not `Traits/` — which is where the 2026-08-20 check looked.**
> - **The floor of 1 is NOT the thing the user asked for, and this is the trap the item's title walks into.** It floors the *concealment value*, and its purpose is an internal invariant — *"0 is shroud's level and must not be a concealment value"* (`:115-116`). It is pre-existing and `1ad638e7`'s own message says so: *"The floor of 1 is untouched. It already existed, and it is at the opposite end of the scale from this change."* **A floor on concealment is not a floor on the observer's ability to see. The item's title asks for the second and names the first.**
> - **What actually delivered the user's request is the CEILING, plus a comparison change — a two-commit pair, both 2026-08-20, both ancestors of `main`, verified with `git merge-base --is-ancestor`:** `1ff73ae5` *"Reveal is non-strict: a matching observer detects, top of the ladder included"* and `1ad638e7` *"Reserve the top vision level for observers: concealment now ceilings at 9"*, whose message opens *"Second half of the invisibility fix."*
> - **`1ff73ae5` names the user's exact scenario in its own commit message:** *"A Sniper or SF at rank 3, stopped, computes 5 + prone + dug-in + rank-veteran = 10 and was **invisible standing next to an enemy**."* That is item 67's `Perceived` line, and it is fixed.
> - **Guarded, not merely landed.** `engine/OpenRA.Test/OpenRA.Mods.Common/DetectableCeilingTest.cs` carries five tests including `ConcealmentCannotReachTheTopVisionBand` and `TopBandObserverDetectsTheMostConcealedUnitPossible`. The reveal predicate is `MapLayers.IsDetected` (non-strict, `:598`), deliberately kept **separate** from `IsVisible` (still strict) because level 1 doubles as the "explored, nobody looking" sentinel.
>
> **HOW THIS WAS MISSED, because the pattern is the one this queue keeps repeating.** The item says *"verified 2026-08-20, no `MinDetectab*` / `VisibilityFloor` / `MinVisibility` symbol anywhere."* The fix landed **2026-08-20 at 02:57**, and the symbol is called **`ClampConcealment`** — so the grep was true and useless. **The behaviour was searched for under three names it was never going to have.** Same class as item 62's `rotor-stopped` (`info.` indirection) and item 69's grant-vs-geometry miss: *grep for the behaviour, and if you cannot name it, read the file that would have to contain it.*
>
> **WHAT ACTUALLY SURVIVES, stated in the code's own words (`Detectable.cs:107-112`):** *"'Invisible while an enemy stands on top of it' is **closed**. 'Invisible in forest at range' is **NOT** — closing that needs the observer floor raised in `AddSource`, which also moves fog rendering, radar and the AI belief layer."*
> **That is a different item from the one filed, and it is much more expensive.** `1ad638e7` **considered and deliberately rejected exactly that fix**: *"flooring observer strength at 2 in `MapLayers.AddSource` would fix only the forest route and collides with level 1's second job as the 'explored, nobody looking' sentinel, changing fog rendering and the AI belief layer as a side effect."* **So the remaining work is the option a previous session priced and declined — re-opening it needs a reason, not just a re-file.**
>
> **Three consequences worth carrying.**
> 1. **The sequencing ruling is doubly discharged.** *"Item 67 lands before item 69"* was reasoned from cover being unreachable. Item 69's own 2026-09-02 verdict records that cover is now reachable; **and now 67's cheap half has shipped too.** Neither leg of the ruling still describes the code.
> 2. **`@stable` moved on 2026-08-20 and the benchmark control changed.** `1ad638e7` states it outright: *"`@stable` INHERITS this rather than being gated off it… the benchmark control has changed and the next baseline must be re-taken knowingly."* **Item 43 should treat 2026-08-20 as a baseline-invalidating date.**
> 3. ⚠️ **`1ff73ae5` was a real balance change that this queue records nowhere else.** Non-strict reveal moves the reveal radius **outward by one vision band for every unit in every match**, not only concealed ones — its own message: *"Scouting, first contact and every ambush trigger distance shift outward by one rung. That is a balance change, not just a bugfix."* **Anyone reading a post-08-20 ambush or scouting measurement against a pre-08-20 one is comparing across that shift.**
>
> **Trap for whoever touches `Detectable` next:** `visibility-10` is still declared in the `[GrantedConditionReference]` superset and is **never granted at runtime** — the tier-10 range circle consumes it and must survive a revert of `1ad638e7`. Do not "clean up" the unused condition.

**Perceived:** the headline case is delivered — you can always find a man you are standing on top of. **What remains is not visible on its own:** a unit at high concealment can still be undetectable *at range through forest*, because the sightline's tree shadow is subtracted from the observer before their strength is stamped (`MapLayers.cs:371-374`; `Map.ForestGroundShadow` returns 2 for crossed density 11-20, and one authored tree cell is density 10 — **so an observer crossing about two dense cells stamps 8 and cannot detect a ceiling-concealment target at any range**).
User verbatim, for the record: *"I think it is an error that they can become fully invisible… I think their visibility should be at least 1 at all times."* Original ruling and sequencing: `57822b4e`, `ambush-programme/README.md` §7.
**Size:** ~~one line~~ **medium-to-large and cross-cutting** — the surviving change is an observer floor in `MapLayers.AddSource`, which by the code's own account moves fog rendering, radar and the AI belief layer, and collides with level 1's sentinel role. **Not a clamp. Price it as an influence-stack change** — `DOCS/reference/influence-stack.md`, whose byte-identity and zero-RNG invariants apply.

### 68. A human clicking Ambush gets a different feature from the one the bots get
`[HEADLINE DEFECT of the 2026-08-20 programme; user-gated]`
**Perceived:** the player picks Ambush, reads a tooltip promising a coordinated hold-and-spring, and gets plain hold-fire. The bots get the real thing.
Stages 2–4 — halt-before-contact, the stationary hide-and-spring machine, the coordinated spring — are gated on `enable-ambush-tactics`, granted by `LaneAmbushBotModule` (`:451,474`) and by **six autotest scenarios** — five in Lua, plus `test-case01-forest-ambush`, which grants it in its own `map.yaml` as well — **and by nothing else** (re-verified by grep at `main @ 57822b4e`: 7 files, 6 scenarios). `AutoTarget.cs:93` describes the gate as *"a human opt-in / bot ledger commit / test map grants"* — **no human opt-in path ships**, and `LaneAmbushBotModule.cs:48` says so outright. **This is a regression against this feature's own shipped design ruling D** (item 8: *"human-settable + bot behind the same default-off gate from day one"*), not a design choice. **Five passing autotests do not contradict it — they pass because they grant the gate by hand.** *(Five passing against six granting is not an inconsistency: the sixth, `test-case01b-detect`, has never been run — see item 70.)* The grantor seam already exists, so this is a gating question, not a design one, and it is the strongest single candidate for the user's original complaint.

### 69. ~~The concealment cover ladder is dead end-to-end~~ → RE-SCOPED: the cover ladder SHIPPED; only the sibling detectability defects survive
`[user-gated; the item-67-before-69 sequencing ruling is DISCHARGED BY EVENTS — see below]`

> ❌ **VERDICT 2026-09-02 (`main @ 6a7e1839`) — THE HEADLINE PREMISE IS REFUTED. Do not dispatch anyone at "make forests grant cover"; that is merged and enabled.** This item was written 2026-08-20; the work landed after it and nobody re-cut the stub.
> - **`247408b8` *"Living trees grant cover; vehicles get stationary concealment"*** adds `^TreeCover` (`decoration.yaml:56-60`, `ProximityExternalCondition@ObjectProximity`, **Range 1024**), inherited by `^Tree` at `decoration.yaml:141`. **Living trees now emit `object-proximity`.** `598ab9ad` then re-anchored the clump trigger circles onto the authored density mass (clumps are 48.5% of woodland-warfare's 1202 trees). Measured in the commit's own comment block: **22.7% of in-bounds cells receive some bonus, 1.0% at +3** on woodland-warfare-ww3.
> - **The husk-geometry argument is now moot twice over.** `^TreeHusk`'s Range-384 emitter is still there (`husks.yaml:155-158`) and still unreachable — but as of **`b74f2aaa` + `50544f90` (2026-09-02) trees are indestructible**, so no tree husk can ever spawn at all. The "burning the forest down does not help" sentence describes a state the game can no longer reach.
> - Consumers moved: the `+1/+2/+3` ladder is at **`infantry.yaml:759-770`**, not `:704-715`.
> **What the 2026-08-20 audit got right and is worth keeping: all three parties checked the GRANT and none checked the GEOMETRY (`534e36d6`).** That lesson survives its own conclusion — `247408b8` fixed it by reasoning about trigger-centre offsets, which is the same lesson applied.

**Perceived:** delivered for the headline — a treeline now conceals better than open ground. What remains is not visible on its own.
**The surviving scope is the three sibling detectability-input defects from the same audit**, none of which the tree work touched: the two `dugin` timer bugs (`infantry.yaml:141` `ConditionWhenStill: dugin`, consumed `:775`), the −2 firing penalty being `primary`-armament only, and infantry CV topping out at 9. **One contradiction is deliberately preserved, not papered over** — the legibility strand computes a reachable CV 10, the audit says 9; both cannot be true, and the tree work changes the arithmetic on both sides, so **re-derive rather than resume the old argument.** ⚠️ **The `bugs/discovered.md` 2026-08-20 entry still carries the superseded "only burnt trees" framing** and is now two revisions stale, not one.
**Sequencing note:** the user's ruling *"item 67 lands before item 69"* was reasoned from cover being unreachable — repairing it later would make an invisibility tier reachable. **Cover is now reachable, so that ordering has already been overtaken: item 67's visibility floor is the urgent case the ruling anticipated, not the cheap one.** The ruling's intent is served by doing 67; its stated dependency no longer describes the code.

### 70. The coordinated spring is not coordinated, and the tooltip promises a zero aim delay that does not exist
`[user-gated]`
**Perceived:** the trap springs and nothing happens for a beat — the volley smears over a second or two, and an MBT stands in the open for ~3 s before firing. The Ambush tooltip promises *"zero aim delay."*
`TriggerNearbyAmbushAllies` sets a flag on each nearby ambusher and **never makes any of them shoot** — each fires on its own next scan, at WW3MOD's overridden 16–32-tick infantry interval against an engine default of 3–8, drawn per unit. Separately `Armament.AimingDelay` is 15 ticks on infantry and 30–50 on vehicles, is charged **in full after the spring**, and pre-aim never touches it (`PreAimAtTarget` only rotates facing). **The two are the same order of magnitude — fixing either alone leaves about half the lag.** **Trap:** the obvious fix routes through `ScanForTarget`, which re-arms off `SharedRandom` and would shift the shared RNG stream and break the frozen `@stable` baseline; the codebase already solved this exact problem for target preemption — copy that pattern verbatim. **Unmeasured:** the 1–2 s figure is derived from YAML and the timestep, not observed. `test-case01b-detect` was authored to measure precisely this and **has never been run once** — the cheapest measurement available anywhere in the programme.

### 71. Cover protects almost nothing, and Take Cover would march a squad onto burnt ground
`[user-gated; needs a design call before any code]`
**Perceived:** going prone or digging in feels like it should save you and does not — and a Take Cover button would confidently send soldiers to a position that stopped being cover several minutes ago.
Of the mod's **145 `DamageTypes:` declarations** (re-counted 2026-09-02; the filed figure was 109, and the count moves whenever weapons are added — **the load-bearing half is the numerator, not the denominator**) **exactly one** carries a `Prone*` token — `weapons-superweapons.yaml:399 DamageTypes: Prone30Percent` — and `InfantryStates.cs:200-203` applies `ProneDamageModifiers` only to warheads declaring a match — so prone reduces damage from one superweapon and nothing else. `dugin` is concealment-only, zero damage reduction. **The dominant protective effect is not damage reduction at all:** `ClearSightThreshold` (`Armament.cs:364`) refuses the shot outright once foliage on the line exceeds the weapon's threshold, which is almost certainly what players perceive as "cover working" — and nothing in the UI says so. **Map density is static:** `UpdateDensityForBuilding` and the shadow-update queue ship with their callers commented out, so a forest shelled flat still grants full cover, full concealment, and still refuses rifle shots. **The Take Cover button was deleted 2026-08-19 and the user has ruled it must be both automatic AND a button**, so building this means restoring it; item 61's dead-button analysis in [`closed-items.md`](pipeline/archive/closed-items.md) is the costing and records that it was inert at three levels.

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

### 72. (Post-release, user-requested record) AI-generated intermediate damage-state sprites for garrisonable buildings
**Perceived:** a shelled building visibly falls apart in stages instead of snapping intact→damaged and then sitting unchanged while its cover quietly collapses.
_Recorded 2026-09-01 at the user's request, in the same message that ruled destructibility "leave it as-is for now". **Post-release; do not pull into v1.**_
_**The engine does NOT wait on this art.** Damage is already capped to a terminal rubble state (`GarrisonManager.cs:1415-1435`) and occupant protection already interpolates continuously with a distinct rubble tier (`GarrisonProtection.cs:63-74`). The simulation has a gradient; only the presentation is binary. Sharpening that gradient is a **YAML-only** change worth doing with today's art — see P2 in [`garrison-destructibility-260901.md`](garrison-destructibility-260901.md). Anyone reaching this item before P2 has the order backwards._ → [`items/72-garrison-damage-sprites.md`](pipeline/items/72-garrison-damage-sprites.md)

### 73. (Post-release, user-requested record) Multi-block interconnected buildings — occupants relocate as parts collapse
**Perceived:** shelling a building drives its garrison from one wing to another; a large building is fought through room by room instead of being one HP pool with men attached.
_Recorded 2026-09-01 at the user's request, who classified it as "a lot of work" in the same breath as proposing it. **Post-release; large.**_
_**Four parts of today's model each assume one building = one indivisible actor**: ports are fixed at actor creation (`GarrisonManager.cs:112-127`, `:210-213`), capacity is a plain `readonly int` with no condition hook (`Cargo.cs:33`), health is a single pool, and there is no interior — shelter occupants are out of the world and port occupants sit on the building's own cell. Do not estimate this without reading the dossier's trap list first._ → [`items/73-multi-block-buildings.md`](pipeline/items/73-multi-block-buildings.md)

---

## Where the rest of it went

- **[`pipeline/archive/closed-items.md`](pipeline/archive/closed-items.md)** — every closed, retired or shipped numbered item, verbatim: 8, 20, 25, 30, 31, 33, 47, 50, 51, 52, 58, 59, 60, 61, 63, 66, R10, R13. Kept for their rulings and traps, not as tasks.
- **[`pipeline/archive/shipped-log.md`](pipeline/archive/shipped-log.md)** — the SHIPPED log and the 2026-07-29 harness LANDED note.
- **[`pipeline/archive/session-notes.md`](pipeline/archive/session-notes.md)** — the dated framing blocks (GATE lifted, process shift, standing grant), the 2026-08-11 SESSION STATE and its **method notes worth reusing**, the batch headers for 08-08 / 08-13 / 08-15, and the close-out intake reconciliation table.
