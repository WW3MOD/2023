# Opportunities — PLAYER EXPERIENCE (the stranger's first hour)

**Ref: `main @ c276679c`, 2026-10-07. Worktree `wt/scout-px`. Read-only pass: no build, no launch,
no lint, no autotest.** Every `file:line` below was opened and read at that ref. Where a claim could
not be confirmed by reading it is labelled **HYPOTHESIS** with the thing that would settle it.

**Angle.** What a stranger who downloads the mod meets in their first match: is it clear what to do,
is it clear what is happening, does the game answer when they act. AI, balance, engine robustness and
content gaps are other scouts' lanes and are deliberately not covered.

**Not re-proposed — already known or queued, cited instead.** R-list (`PIPELINE.md` "RELEASE AUDIT —
RANKED FINDINGS"): R4 lobby AI names (ruled), R5/R6 hotkeys (cosmetic), R9 contestation copy (closed),
R12 cache top-up cursor, R14 captured heli, R15 commander promotion. Queue: **74** neutralise
notification, **75** infantry selection (ruled closed 2026-09-30), **77** enemy-SR cursor says *move*,
**79** contestation entry displacement, **80** "your shot did nothing", **81** aircraft contestation,
**83** veteran reserve, **46/48** art, audio, voices, map previews. Audit
`audit/260921-release-readiness.md`: D1 version panel (fixed — `MainMenuLogic.cs:278` comment), I1
hotkey list (shipped `ea72135f`), I2 Escalation teaching, U1 tooltip opacity (shipped `a2b6a760`).
Proposals `proposals/260902-safe-wins-and-swings.md` safe wins 1–10 and its "Killed on verification"
list. Nothing below duplicates any of those; where one is adjacent it says so.

---

## Ranked list

| # | Title | Severity | Size |
|---|---|---|---|
| 1 | The Objectives tab tells you to "Destroy all opposition!" — the one thing this game makes impossible | **SHOULD-FIX** | minutes |
| 2 | Ordering reinforcements is silent from click to arrival — including when the money runs out | **SHOULD-FIX** | hours |
| 3 | A dry unit turns round and walks off the map, and nothing says why | **SHOULD-FIX** | hours |
| 4 | "Holding fire" and "walking off the map" are the same orange pip in the same corner | **SHOULD-FIX** | minutes |
| 5 | The attacker's half of contestation is unlit — no ring until you click the enemy beachhead, no milestones | **SHOULD-FIX** | hours + one user call |
| 6 | The rally line never draws the march — the one segment the unit actually walks | **POLISH** | hours |
| 7 | Nine kinds of per-unit indicator and no legend anywhere | **POLISH** | hours (copy + one image) |
| 8 | The default first match puts nothing on the map but the beachhead (user call) | **POLISH** — HYPOTHESIS on impact | minutes to flip, one playtest to judge |
| 9 | "Silos needed." is still wired | **COSMETIC** | minutes |

Ranking function is the release-audit ladder (`PIPELINE.md` §"Severity ladder"): what a stranger
meets, how early, how visibly; cost only breaks ties. 1 ranks first because it contradicts the core
concept on the dialog's **default** tab and is a one-line fix. 2, 3 and 5 are the three moments in a
first match where something important happens and the game does not say so; 4 is the one-line defect
that makes 3's only existing cue unreadable. 6–7 are legibility. 8 is a design question.

---

## 1. The Objectives tab tells you to "Destroy all opposition!" — the one thing this game makes impossible

**Today.** The player opens the in-game info dialog (the game-info button / its hotkey). It opens on
the **Objectives** tab, which reads **"Mission: Destroy all opposition!"**. One tab to the right, How
To Play says **"You win by cutting their link, not by levelling their base."** The enemy beachhead
cannot be damaged by any weapon. A stranger who believes the first tab spends the match trying to
kill a building that cannot die. At match end the same objective is what is marked completed/failed.

**After.** The Objectives tab reads e.g. **"Mission: Contest the enemy Supply Route until it falls —
and keep yours."** Both tabs say the same thing.

**Evidence.**
- `engine/OpenRA.Mods.Common/Traits/Player/ConquestVictoryConditions.cs:23-24` — `Objective` field,
  default `"Destroy all opposition!"`; added to the player at `:73`
  (`mo.Add(self.Owner, info.Objective, "Primary", inhibitAnnouncement: true)`).
- `mods/ww3mod/rules/player.yaml:1041` — `ConquestVictoryConditions:` with **no fields**, so the
  default string ships.
- `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/GameInfoStatsLogic.cs:105-108` — the stats panel
  renders `mo.Objectives[0].Description` as the checkbox text.
- `mods/ww3mod/rules/world.yaml:697-698` — `ObjectivesPanel: PanelName: SKIRMISH_STATS`, so the tab
  is present in every lobby game.
- `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/GameInfoLogic.cs:72-78` adds Objectives first and
  `:99-105` adds How To Play **last**, with the comment "Added last so AutoSelect still resolves to
  Objectives as before"; `:136-137` resolves `AutoSelect` to the first visible panel. **Objectives is
  the default tab.**
- `mods/ww3mod/chrome/ingame-info-howtoplay.yaml:109` — "You win by cutting their link, not by
  levelling their base." `:116-137` describe contestation.
- `mods/ww3mod/rules/ingame/structures.yaml:300-304` — the SR is untargetable by every warhead.
- `git log -S'Objective:' -- mods/ww3mod/rules/player.yaml` → **no commits**: the field has never
  been set. (`engine/mods/common/fluent/chrome.ftl:173` carries the same text as
  `checkbox-stats-objective`; that key is not what the panel renders — `:108` overwrites the text.)

**Why this rank.** SHOULD-FIX, top of list: reachable in every match, on the default tab of the only
in-game help dialog, contradicting the mod's central mechanic, fixable in one line. Not a BLOCKER only
because How To Play, auto-shown once on first run (`MainMenuLogic.cs:895-904`), tells the truth.

**Cheapest verification.** Static: after the edit, `grep -A1 '^\tConquestVictoryConditions:'
mods/ww3mod/rules/player.yaml` shows the `Objective:` line. Visual: one SCREENSHOT.md capture of the
info dialog in a skirmish. Gates: `make test` (YAML) — `Objective` is a plain string field, no
fluent lookup.

**Dispatch-ready brief.**
- **Files:** `mods/ww3mod/rules/player.yaml:1041` only.
- **Approach:** add `Objective: <copy>` under `ConquestVictoryConditions:`. Copy must match
  `SupplyRouteContestation.cs:24-26` [Desc] exactly — passive if a teammate still holds a Route,
  defeated if not — and must not say "capture" (ownership never transfers; CLAUDE.md). Suggested:
  *"Contest the enemy Supply Route until it falls — and hold your own."* Keep it under ~60 chars;
  the checkbox label is a single line (`chrome/ingame-infostats.yaml:52-58`).
- **Acceptance:** screenshot shows the new text; `make test` green; `ww3mod` lint baseline unchanged.
- **Read first:** `DOCS/reference/supply-route.md`, `DOCS/reference/conventions.md` (MiniYaml
  blank-line and tab rules), `DOCS/recipes/SCREENSHOT.md`.
- **Risks:** the Escalation/Time Limit modes end matches by other routes
  (`ConquestVictoryConditions.cs:94+` `NotifyTimerExpired`, `DoomsdayStrike`); the copy should not
  claim contestation is the *only* way a match ends. Scenario maps that override
  `ConquestVictoryConditions` are unaffected (map rules win).

---

## 2. Ordering reinforcements is silent from click to arrival — including when the money runs out

**Today.** The player clicks a unit in the sidebar. No voice, no text. If cash runs dry mid-order the
queue simply stops ticking — no "insufficient funds" line. When the unit is delivered it appears at
the map edge, usually off-screen, with no line or sound. Cancel and pause are silent too. The only
sign anything happened is the cameo clock. For a mod whose whole premise is "units march in from
off-map", the moment a unit *enters the map* is the one the game says nothing about.

**After.** Text lines (voice optional, see Risks): *"Reinforcements inbound."* on delivery;
*"Insufficient budget — order paused."* (throttled, the trait already has a 30 s interval);
optionally *"Order cancelled."* / *"On hold."*.

**Evidence.**
- `mods/ww3mod/rules/player.yaml:23-80` — every queue (`@Building`, `@Defense`, `@Vehicle`,
  `@Infantry`, `@Ship`) has `ReadyTextNotification`, `QueuedAudio`, `OnHoldAudio`, `CancelledAudio`
  **commented out**; no `ReadyAudio` at all. Live: only `BlockedTextNotification` / `BlockedAudio`.
- `engine/OpenRA.Mods.Common/Traits/Player/ProductionQueue.cs:62,65` defaults `null`; the delivery
  path at `:553-559` plays `ReadyAudio` and posts `ReadyTextNotification` when `BuildUnit` succeeds —
  i.e. it fires at the moment the unit spawns at the edge, which is the event to announce.
- `ProductionQueue.cs:881` charges per frame with `TakeCash(costThisFrame, true)`;
  `PlayerResources.cs:293-302` would post `InsufficientFundsTextNotification` — `:45,48` default
  `null`, and `player.yaml:1045-1061` sets neither.
- **History, read before changing it:** `git show 1921bf39 -- mods/ww3mod/rules/player.yaml` *removed*
  `InsufficientFundsNotification: InsufficientFunds` and `InsufficientFundsTextNotification:
  Insufficient funds.`; the commit subject ("Palette preview fix…") does not say why. The ready/queue
  lines have been commented since `7362fbc6` "Starting point". Neither looks like a recorded ruling
  (grep of `WORKSPACE`/`DOCS` for `ReadyAudio`/`QueuedAudio` finds nothing).
- `mods/ww3mod/languages/en.ftl:762` — the match-start line promises "They march in from the map
  edge", so the game itself frames arrival as the event.

**Why this rank.** SHOULD-FIX: every player orders a unit in the first minute; silence at the
moment of action is the classic "is it broken?" impression. Below #1 only because the cameo clock
does give *some* feedback.

**Cheapest verification.** One SCREENSHOT.md capture of the text-notification area a few ticks
after a delivery in a skirmish. **HYPOTHESIS** that a Lua test can read transient lines — if
`TextNotificationsManager` has no scripting surface, a screenshot is the only evidence.

**Dispatch-ready brief.**
- **Files:** `mods/ww3mod/rules/player.yaml:23-80` (queues), `:1045` (`PlayerResources`).
- **Approach:** YAML only. Set `ReadyTextNotification` on `@Infantry`, `@Vehicle` (and aircraft if a
  separate queue exists — check `ProductionQueue@`/`Classic*` blocks below `:80`); set
  `InsufficientFundsTextNotification` on `PlayerResources` (keep the 30 s interval at
  `PlayerResources.cs:51`). **Text first, voice second**: the only speech assets are Red Alert EVA
  (`rules/sound/notifications.yaml:105 UnitReady: unitrdy1`), which is item 48's identity problem;
  do not add RA voice lines without the user's say-so.
- **Acceptance:** screenshot shows the delivery line; a zero-cash order shows the funds line once,
  not per frame; `make test` green.
- **Read first:** `DOCS/reference/game-model.md` §Reinforcement Model, `DOCS/reference/economy.md`,
  `DOCS/recipes/SCREENSHOT.md`.
- **Risks:** chatter — a 10-unit infantry order posts 10 lines (`ProductionQueue.cs:538-559` loops
  per item). If that reads as spam, throttle in C# or announce only the first of a batch. The funds
  line was removed once for an unrecorded reason; **ask the user whether that was deliberate before
  restoring it** (the ready line can ship without that answer).

---

## 3. A dry unit turns round and walks off the map, and nothing says why

**Today.** A rifleman empties its magazine and, under the default **Auto** resupply stance, if no
truck/depot/cache is in reach it **immediately** turns and marches to the nearest map edge to be
sold back. The player sees a unit leave the fight unasked. The only cue is a 4-pixel orange pip
(top-right of the unit — and identical to the holding-fire mark, see #4); the refund "+$N" floats up at the map edge when the walk ends, usually
off-screen. No text line, no minimap ping, no sound at the moment of the decision.

**After.** At the moment the unit decides to leave: a throttled text line — *"Rifleman out of
ammunition — no resupply in reach, withdrawing."* — and a minimap ping at the unit. Optional: one
line per batch when several leave in the same second.

**Evidence.**
- `mods/ww3mod/rules/defaults.yaml:433-434` — `InitialResupplyBehavior: Auto` (and AI) for
  everything inheriting `^AutoTarget` defaults.
- `engine/OpenRA.Mods.Common/Traits/AmmoPool.cs:766-790` — `AutoRearmIfDry`; the comment at
  `:785-788` records the 2026-08-27 user ruling *"'Auto' should mean that they evacuate if no rearm
  actor exists"*, "immediately and with no grace period".
- `AmmoPool.cs:955-962` — `EvacuateForRefund` queues `RotateToEdge` and `ShowTargetLines()` — no
  notification. `AmmoPool.cs` is absent from the set of files that call `AddTransientLine`
  (grep over `engine/OpenRA.Mods.Common`); so are `AutoSeekSupplies.cs` and `Rearmable.cs`.
- Pip: `mods/ww3mod/rules/ingame/infantry.yaml:162-166`, `vehicles.yaml:138-143`,
  `aircraft.yaml:182-187, 236-241` (`WithDecoration@Evacuating`, `pip-orange`, `TopRight`).
- Arrival refund text exists: `tools/autotest/scenarios/test-evac-refund-indicator/*.lua` header
  (FloatingText clamped into bounds by `adfb0f2f`). That is the *end* of the walk, not the start.
- Some units default to `Evacuate` outright, e.g. `vehicles-america.yaml:767-770` ("Lifetime ammo
  cap — empty magazine triggers evacuation").
- `git log -S'EvacuateForRefund'` → `7a960ba6` "resupply: Auto evacuates when nothing can rearm the
  unit", `8bdb8614` — the behaviour is recent and deliberate; nothing added messaging.

**Why this rank.** SHOULD-FIX: a stranger without a supply truck (they will not have one in their
first match — it is a separate purchase) sees infantry desert within minutes and reads it as a bug.
The behaviour is ruled correct; only its legibility is proposed. Adjacent to safe win 6 (a number on
the Evacuate button) and proposal 3 (the heli pip) — both are about the *manual* or the *marker*;
this is about the *announcement*.

**Cheapest verification.** A single autotest already stages the scenario: `test-evac-suite` /
`test-dry-evac-drops-queued-order` produce dry units with no host. Add a screenshot capture at the
evacuation tick (SCREENSHOT.md). No new mechanism to measure.

**Dispatch-ready brief.**
- **Files:** `engine/OpenRA.Mods.Common/Traits/AmmoPool.cs` (the `Auto`→evacuate branch in
  `AutoRearmIfDry`, and the `Evacuate` branch if wanted), possibly a small per-player throttle trait
  on the player actor (`mods/ww3mod/rules/player.yaml`), modelled on `BaseAttackNotifier`
  (`player.yaml:1091-1093`) which already does throttled text + radar ping.
- **Approach:** emit only for the **local** owner, render-side (`TextNotificationsManager` +
  `MiniMapPings`), never as simulation state — copy the client-local latch pattern documented at
  `SupplyRouteContestation.cs:229` ("Deliberately NOT [Sync]"). Throttle per player (e.g. one line
  per 10 s, count batched). Text names the unit's tooltip name.
- **Acceptance:** screenshot shows line + ping on the first auto-evacuation; a squad of 10 going dry
  together produces one line, not ten; NUnit for the throttle maths; `.\make.ps1 check` green
  (analyzers); one targeted autotest run.
- **Read first:** `DOCS/reference/economy.md` (resupply), `DOCS/reference/conventions.md` (engine
  code rules), `DOCS/reference/architecture.md` §"Adding a behavioural field to a trait shared by
  both bot profiles" — this must be **render-only** so neither bot profile changes.
- **Risks:** desync if anyone stores the throttle in synced state; bots do not need the line, so
  gate on `self.Owner == world.LocalPlayer`.

---

## 4. "Holding fire" and "walking off the map" are the same orange pip in the same corner

**Today.** A unit that is deliberately not shooting (anti-overkill, or breaking off a dying target)
shows a small **orange pip, top-right**. A unit that has run dry and is **leaving the battlefield**
(#3) shows a small **orange pip, top-right** — the same sprite, the same anchor, the same colour.
The one cue the game gives for an unasked evacuation is indistinguishable from "this unit is fine,
it just chose not to fire".

**After.** The evacuating mark gets its own glyph or colour (e.g. a red/white pip, or a small arrow
from an existing sequence), or moves to a free slot. Holding-fire keeps amber, which its comment
justifies.

**Evidence.**
- `mods/ww3mod/rules/defaults.yaml:893-901` — `^HoldingFireMarker: WithHoldingFireDecoration:
  RequiresSelection: false … Image: pips … Sequence: pip-orange … Position: TopRight`, with the
  comment at `:896-898` choosing amber because "TopRight lands on the selection box's own white
  corner bracket".
- `defaults.yaml:415-416` — `^AutoTarget:` inherits `^HoldingFireMarker`; so do `:679`, `:791`,
  `:875` (the other AutoTarget bases). That is every armed unit.
- `mods/ww3mod/rules/ingame/infantry.yaml:162-166` and `vehicles.yaml:138-143` (and
  `aircraft.yaml:182-187, 236-241`) — `WithDecoration@Evacuating: Image: pips, Sequence: pip-orange,
  Position: TopRight, RequiresCondition: evacuating`.
- `mods/ww3mod/sequences/sequences-misc.yaml:285-286` — `pip-orange: pips2, Start: 7`; one frame,
  shared.
- `engine/OpenRA.Mods.Common/Traits/Render/WithHoldingFireDecoration.cs:15-17` [Desc]: the marker
  exists because "a unit standing next to a live enemy and not firing is indistinguishable from a
  bug" — the evacuating pip reintroduces exactly that ambiguity in the other direction.
- `defaults.yaml:920` lists `TopRight` as "the holding-fire pip" in the slot map, i.e. the slot
  survey that placed later marks did not count the evacuating pip.

**Why this rank.** SHOULD-FIX, and the cheapest item here after #1. Ranked under #3 because it is
only harmful in the situation #3 describes; ranked above everything else because it is a one-line
fix to a live ambiguity on every armed unit.

**Cheapest verification.** One screenshot of two units side by side, one evacuating, one holding
fire (`test-evac-suite` stages evacuations; a HoldFire unit next to a dying target stages the
other). Static: `grep -rn 'pip-orange' mods/ww3mod/rules` shows one meaning per sequence.

**Dispatch-ready brief.**
- **Files:** the four `WithDecoration@Evacuating` blocks (`infantry.yaml:162`, `vehicles.yaml:138`,
  `aircraft.yaml:182`, `:236`); possibly `sequences-misc.yaml` for a new pip frame.
- **Approach:** change `Sequence:` on the evacuating blocks to an unused, already-shipped `pips2`
  frame (check `sequences-misc.yaml:270-290` for what each `Start:` index is spoken for — the
  holding-fire comment at `defaults.yaml:889-892` warns that sequences naming no shipped file render
  nothing). Do **not** change the holding-fire pip; its colour is reasoned in-tree.
- **Acceptance:** screenshot with both marks visibly different at 1080p; `make test` green.
- **Read first:** `DOCS/recipes/SCREENSHOT.md`, the slot map at `defaults.yaml:914-922`.
- **Risks:** colour vocabulary is crowded (`defaults.yaml:908-912` "every colour in both idioms is
  already spoken for") — pick by screenshot, not by name. If no free colour reads well, move the
  evacuating mark to Bottom beside the ammo row, where "out of ammo, leaving" is semantically at home.

---

## 5. The attacker's half of contestation is unlit — no ring until you click the enemy beachhead, no milestones

**Today.** How To Play says "Park units inside the enemy Supply Route ring". The ring is not drawn
unless the player **clicks the enemy Supply Route** to select it. While the attacker grinds the bar,
every voice and text milestone (contested, degraded, defeat imminent) goes to the **defender** only.
The attacker's only feedback is the bar on the enemy building; the first sentence they get is the
system line when the enemy goes passive. The winning player's progress is the quietest thing in the
game.

**After.** (a) The enemy ring is visible when the player has any of their own combat units selected
(or always, thin) — *user call, see Risks*. (b) Attacker-side text milestones at the same three
thresholds: *"Enemy Supply Route contested."* / *"Enemy reinforcements slowed."* / *"Enemy Supply
Route collapsing."*

**Evidence.**
- `mods/ww3mod/rules/ingame/structures.yaml:391-395` — `WithRangeCircle@Contestation`, no
  `Visible:` field → default.
- `engine/OpenRA.Mods.Common/Traits/Render/WithRangeCircle.cs:55` default
  `Visible = RangeCircleVisibility.WhenSelected`; `:89` implements both
  `IRenderAnnotationsWhenSelected` and `IRenderAnnotations`; `:159` / `:166` route by `Visible`.
  `ValidRelationships: Ally, Enemy, Neutral` (`structures.yaml:394`) means enemies *may* see it —
  but only on selection.
- `git log -S'WithRangeCircle@Contestation'` → `3c375c2a` (2026-03-23) "Show contestation range
  circle on Supply Route **selection**" — selection-gating was the original intent; nobody has
  revisited it since contestation became the win condition.
- Enemy SR is always visible by user ruling: `structures.yaml:333-337` ("SETTLED — 2026-08-27…").
- Defender-only messaging: `engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs:638-643`
  (contested: `self.Owner == localPlayer || localPlayer.IsAlliedWith(self.Owner)`), slowdown
  `:670-674`, defeat warning `:690-692` — all to `self.Owner`. Attacker sees `:727` (system line, at
  passive) and the always-visible bar (`:1021-1047`, `IAlwaysVisibleBar`).
- `ingame-info-howtoplay.yaml:116` — "Park units inside the enemy Supply Route ring to contest it."

**Why this rank.** SHOULD-FIX: this is the win path. A stranger told to "park in the ring" cannot
see the ring, and gets no confirmation they are doing the right thing. Safe win 7 (`wt/contest-alarm`)
improved the *defender's* alarms; item 77 covers the *cursor*; item 79 the *drop point*. None
covers the attacker's view.

**Cheapest verification.** Ring: one screenshot with own units selected near the enemy SR, before
and after. Milestones: one autotest — an existing contestation scenario (`ls tools/autotest/scenarios
| grep contest`) — plus a screenshot at the slowdown threshold.

**Dispatch-ready brief.**
- **Files:** (a) `mods/ww3mod/rules/ingame/structures.yaml:391-395` — either `Visible: Always`
  scoped to `ValidRelationships: Enemy` via a second `WithRangeCircle@ContestationEnemy`, or a small
  engine change so a circle can show "when any own unit is selected". (b)
  `engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs` — add `Attacker*TextNotification`
  Info fields, **defaulting to empty** (so nothing changes until YAML opts in), fired from the same
  three transition methods for `localPlayer` enemies of `self.Owner` who have a unit in range.
- **Acceptance:** screenshots of ring visibility; attacker text appears once per threshold per
  `NotifyInterval` (`:131`); defender messages byte-identical to today; `.\make.ps1 check` green.
- **Read first:** `DOCS/reference/supply-route.md`, `DOCS/reference/game-model.md`, CLAUDE.md
  hard rule on contestation ≠ capture (no "capturing" wording), `DOCS/recipes/SCREENSHOT.md`.
- **Risks:** an always-on enemy ring on four SRs in a 4-player map is clutter — that is why (a) is a
  **user call**; present the two screenshots. (b) must stay render-side / client-local like the
  existing latches (`:229`). Do not ping the defender's position to the attacker's minimap beyond
  what is already visible — the SR is visible by ruling, so a ping at it leaks nothing.

---

## 6. The rally line never draws the march — the one segment the unit actually walks

**Today.** Select your Supply Route: a dashed rally line runs from the building to the flag. But
units do not start at the building — they enter at the map edge and walk *to* it. The edge→SR leg,
which How To Play says "the enemy can ambush on the way", is never drawn. A new player cannot see
the route their reinforcements will take.

**After.** The rally line starts at the edge cell `ProductionFromMapEdge` would spawn at, then runs
through the SR to the flag (or straight to the flag, matching the real path).

**Evidence.**
- `engine/OpenRA.Mods.Common/Effects/RallyPointIndicator.cs:93-94` —
  `targetLineNodes.Insert(0, building.CenterPosition + exit SpawnOffset)`; node 0 is the building.
  Unchanged since `7362fbc6` "Starting point" (`git log -S'targetLineNodes.Insert(0'`).
- `mods/ww3mod/rules/ingame/structures.yaml:472-474` — `ProductionFromMapEdge: Produces: Infantry,
  Soldier, Vehicle, Aircraft, Helicopter`; `engine/OpenRA.Mods.Common/Traits/ProductionFromMapEdge.cs:21`
  "Produce a unit on the closest map edge cell"; `:33` caches `spawnLocation`.
- `ingame-info-howtoplay.yaml:67-81` — "enters at the map edge nearest your Supply Route … the enemy
  can ambush it on the way."
- Previously noted, never filed: `proposals/260902-safe-wins-and-swings.md:778-780` ("the segment the
  unit actually walks — map edge to SR — is the one segment never drawn"). Re-verified here.

**Why this rank.** POLISH: only visible when the SR is selected or Show-All-Orders is held
(`RallyPointIndicator.cs:103-107`); a player can play without it. But it is the single visual that
would teach the reinforcement model at a glance.

**Cheapest verification.** One screenshot with the SR selected, before/after.

**Dispatch-ready brief.**
- **Files:** `engine/OpenRA.Mods.Common/Effects/RallyPointIndicator.cs` (`UpdateTargetLineNodes`
  region `:80-95`); read the spawn cell from the building's `ProductionFromMapEdge` (expose its
  candidate cell read-only if private — `:24-33`).
- **Approach:** if the building has `ProductionFromMapEdge`, insert the edge cell centre as node 0
  and keep the building as node 1. Render-only.
- **Acceptance:** screenshot; `test-production-bad-rally` and `test-sr-rally-modifiers` still pass
  (one run each, with goahead); `.\make.ps1 check` green.
- **Read first:** `DOCS/reference/conventions.md` (engine code rules),
  `DOCS/reference/supply-route.md`.
- **Risks:** the spawn cell is chosen among `SpawnCandidateCount` (`ProductionFromMapEdge.cs:25`)
  candidates and may differ per unit — draw the centre candidate and accept it is approximate. Item
  79 (displacement) would move it; if 79 ships, the line must read the displaced value.

---

## 7. Nine kinds of per-unit indicator and no legend anywhere

**Today.** Units carry: a graded visibility diamond (hollow/solid, coloured), rank chevrons, a
damage bar, suppression chevrons, a critical mark, cargo pips, a holding-fire pip, two stance glyphs,
an evacuating pip, and an ammo row. How To Play explains the economy and contestation and **none of
these**. A stranger sees coloured dots and must guess.

**After.** A second How To Play section (or a tab) — *"Reading your units"* — with one annotated
strip image and one line per indicator.

**Evidence.**
- `mods/ww3mod/rules/defaults.yaml:914-922` — the slot map lists the vocabulary: damage (y=0),
  suppression (-3), critical (-5), cargo (-10 and up), control group (TopLeft), holding-fire pip
  (TopRight), rank chevrons, ISelectionBar stack + ammo row (Bottom); `:934-940` the graded diamond;
  `:903-912` the two render-only stance marks (`WithTextDecoration`); `:886-901` the holding-fire
  pip (see #4).
- Evacuating pip: `infantry.yaml:162-166`, `vehicles.yaml:138-143`.
- Ammo pips are selection-only: `infantry.yaml:1175-1179` `RequiresSelection: true`.
- `mods/ww3mod/chrome/ingame-info-howtoplay.yaml` (177 lines) — every `Text:` at `:11-151` is about
  economy/reinforcement/contestation; the footer `:177` points to Settings > Hotkeys. No indicator copy.
- `defaults.yaml:945-946` says the diamond "has never been seen on screen and a tuning round is
  expected" — **so the legend should wait for that tuning** (standing rule in `PIPELINE.md`
  §DEFERRED: presentational work waits until the thing it presents stops changing).

**Why this rank.** POLISH, and **time it after the diamond tuning round**. High value per hour once
the vocabulary is stable; zero mechanical risk.

**Cheapest verification.** Screenshot of the panel; a static check that every decoration named in
the legend still exists in YAML.

**Dispatch-ready brief.**
- **Files:** `mods/ww3mod/chrome/ingame-info-howtoplay.yaml` (and `mainmenu-howtoplay.yaml` if it
  includes the same panel), one new image under `mods/ww3mod/bits/` or a chrome sprite.
- **Approach:** capture one frame with a squad and a vehicle showing each indicator (a DEMO.md
  scenario, no verdict), crop, annotate. Bump `MainMenuLogic.HowToPlayVersion` (`:40`) so existing
  players see the new copy once.
- **Acceptance:** screenshot of the panel at 1080p and at the smallest supported resolution (text
  must not overflow `PARENT_WIDTH - 40`); user sign-off on copy.
- **Read first:** `DOCS/recipes/DEMO.md`, `DOCS/recipes/SCREENSHOT.md`, `WORKSPACE/diamond-pip-design-260903.md`.
- **Risks:** stale on the next pip change — keep the legend generated from the same demo scenario so
  re-capturing is one command.

---

## 8. The default first match puts nothing on the map but the beachhead (user call)

**Today.** A stranger who presses Start without touching options gets **Starting Units: None** — a
Supply Route and empty ground, and a one-line hint (`en.ftl:762`). Their first minute is spent
finding the sidebar and waiting for the first unit to walk in from the edge.

**After (if the user wants it).** The skirmish default becomes **Squad** (two fire teams, AT, medic
— already defined), so the first thing a stranger does is *command*, not *wait*.

**Evidence.**
- `mods/ww3mod/rules/world.yaml:674-679` — `SpawnStartingUnits:`; comment "Default stays `none`, so a
  match nobody touches is the match it was before this option existed" (added with Forward
  Deployment, `a49afc13`).
- Squad packages: `world.yaml:604-619`; `none`: `:598-602`.

**Why this rank.** POLISH and a **design call**, not a defect. **HYPOTHESIS** that the empty opening
hurts first-match retention — settled only by watching one new player, or by the user's own read.
Bots receive the same package, so it is symmetric, but it changes every benchmark/tournament
baseline that does not pin the option — check `tools/autotest` tournament configs pin it before
flipping.

**Cheapest verification.** Ask the user. If yes: one smoke run (`./make.ps1 smoke`) to confirm every
shipped map still constructs.

**Dispatch-ready brief.**
- **Files:** `mods/ww3mod/rules/world.yaml:674` (`SpawnStartingUnits: DropdownDefault:` or the
  engine field name — read `SpawnStartingUnits.cs` Info first).
- **Acceptance:** lobby shows Squad by default; `make test` green; smoke exit 0; benchmark doc notes
  the default change.
- **Read first:** `DOCS/reference/game-model.md`, CLAUDE.md `@stable` rule (a default that changes
  bot matches must be said in the commit message).
- **Risks:** silently invalidates benchmark baselines that rely on the lobby default.

---

## 9. "Silos needed." is still wired

**Today.** Nothing visible — the warning almost certainly never fires. Red Alert residue.

**Evidence.** `mods/ww3mod/rules/player.yaml:1122-1124` — `ResourceStorageWarning:
TextNotification: Silos needed.`; `engine/OpenRA.Mods.Common/Traits/Player/ResourceStorageWarning.cs:56`
fires when `Resources > Threshold% * ResourceCapacity`. WW3MOD has no harvested resources and no
silos (`player.yaml:4-7` `ResourceValues` commented). **HYPOTHESIS** that `Resources` stays 0 for
the whole match — confirm by grepping for any `GiveResources` caller reachable in WW3MOD rules.

**Why this rank.** COSMETIC. Delete the three lines during final polish; if it *can* fire, it is a
SHOULD-FIX ("Silos needed" in a game with no silos).

**Brief.** Remove `player.yaml:1122-1124`; `make test` green. Read `conventions.md` §MiniYaml.

---

## Watch

- **Nothing here was seen on screen.** Every "today" is reconstructed from code paths. #1 and #4 are the
  most certain (#1: three files agree and the string has never been overridden; #4: identical YAML values); #5's "attacker gets no
  milestones" rests on reading three methods and could miss a fourth path in the 1000+-line trait.
- **#2's funds line was removed on purpose by someone** (`1921bf39`) for a reason that is not
  written down. I recommend shipping the delivery line regardless and *asking* about the funds line.
- **#3 assumes the default stance is what a stranger has.** The lobby or a hotkey may change it;
  I did not trace whether the command bar shows the current resupply stance.
- I did not check whether the Objectives text also appears on the post-game screen; if it does,
  #1's fix covers it, if not, nothing is lost.
