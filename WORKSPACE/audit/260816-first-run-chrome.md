# First-run experience & product chrome — release audit

**Repo state:** `main @ 55459146` (clean, up to date with `origin/main`).
**Date:** 2026-08-16. **Method:** static read of the tree only. No game launch, no
autotests, no edits. Nothing here was committed.

**Scope:** the first ten minutes of a new player's experience, and every piece of
product chrome around the game — identity text, main menu, settings, lobby,
onboarding, command bar/hotkeys/tooltips, maps & map browser, credits/version,
loading screens, observer and score UI.

**How to read confidence:** "high" means I read the code or data that produces the
string/behaviour and traced the override chain. "medium" means I read the
producing code but one hop (a network response, a runtime count) is unverified.
"low" means inferred from structure.

---

## Verdict up front

The *content* re-skin is real and mostly done — faction names, unit tooltips, the
loading-screen flavour text, the credits file, the ModContent install copy and the
main-menu title all correctly read WW3MOD. Commit `4836ceed` did land.

What it missed is everything that is **not** a `.ftl` key WW3MOD chose to override:
the ~40 stock engine strings inherited unchanged, the packaging layer, and the two
menus that were never re-pointed at WW3MOD content (Missions, and the bot selector).
The most damaging findings are not "a word says Red Alert" — they are **three
menu entries that lead somewhere broken**, which a new player will hit in the first
five minutes.

---

## 1. BLOCKERS

### **[BLOCKER]** The Missions button opens a browser listing 175 internal autotest and demo scenarios

**Perceived:** A new player clicks Singleplayer → Missions and gets a list headed
"Missions" containing entries like `TEST: AA overkill re-mark cadence, no scripting
(diagnostic)` and `DEMO: WGM suite — tree gate`, alongside empty "Allied Campaign"
and "Soviet Campaign" groups.

Evidence — three independent defects compounding:

- `mods/ww3mod/missions.yaml` is the **unmodified stock Red Alert campaign list**:
  60 mission UIDs under the headings `Allied Campaign:` / `Soviet Campaign:`
  (`allies-01` … `soviet-11b`, `ant-01`). Those maps live only in
  `engine/mods/ra/maps/`, which is **not** in WW3MOD's `MapFolders`
  (`mods/ww3mod/mod.yaml:89-98`), so both groups resolve to zero previews.
- Because `mods/ww3mod/mod.yaml:317-318` registers that file at all,
  `MainMenuLogic.cs:371-375` computes `hasCampaign = Manifest.Missions.Length > 0`
  → **true**, so the button is never disabled.
- `CLAUDE.md` and `mod.yaml:93-95` both claim `Class=Unknown` hides the autotest
  scenarios "from every UI tab (lobby, missions, main-menu chooser)". That is
  **false for the mission browser**. `MapChooserLogic.cs:235-237` filters by class;
  `MissionBrowserLogic.cs:183-187` builds its "loose missions" group with **no class
  filter at all** — only `Status == Available && Visibility.HasFlag(MissionSelector)`.
  All 175 scenarios under `tools/autotest/scenarios/` declare
  `Visibility: MissionSelector` and `RequiresMod: ww3mod` (verified: 175/175), and
  `MapPreview.cs:445-446` sets `Status = Available` on exactly that basis.

Confidence: **high** (code path read end to end; scenario count confirmed by grep).
Fix size: **minutes** for the immediate bleed (empty `missions.yaml` → button
auto-disables; and/or drop `MissionSelector` from the scenario template). **Hours**
if you also want a class filter in `MissionBrowserLogic`.

---

### **[BLOCKER]** The second screen a brand-new player sees asks permission to send data "to help us optimize OpenRA"

**Perceived:** On first launch, after the "Establishing Battlefield Control" setup
prompt, a consent dialog reads *"We would like to collect some system details that
will help us optimize OpenRA."*

Evidence: `engine/mods/common/fluent/chrome.ftl:269`
(`label-mainmenu-system-info-prompt-text-a`). **Not overridden** —
`mods/ww3mod/languages/en.ftl` contains no `system-info` key (verified by grep).
Shown by `SystemInfoPromptLogic.ShouldShowPrompt()` (`:44`) before the main menu
appears (`MainMenuLogic.cs:509-518`). The data is appended to the version-check
query to `master.openra.net` (`SystemInfoPromptLogic.cs:52`).

Note the sibling title string `:268` was already re-themed to "Establishing
Battlefield Control", so someone edited this block and stopped one line short.

Confidence: **high**. Fix size: **minutes** (three `.ftl` overrides).

---

### **[BLOCKER]** Two dev/test maps ship as playable Conquest maps and are unwinnable

**Perceived:** A player picks "Arena: Tank Duel (3v3 Abrams vs T-90)" by
`Author: Combat Sim` from the normal skirmish map list, spawns with no Supply
Route and no reinforcements, and the match can never end.

Evidence:
- `mods/ww3mod/maps/arena-tank-duel/map.yaml` — `Visibility: Lobby, Shellmap`,
  `Categories: Conquest`. Its `rules.yaml` removes `-ConquestVictoryConditions`,
  `-SpawnStartingUnits` and `-MapStartingLocations`.
- `mods/ww3mod/maps/shellmap-open-field/map.yaml` ("Frontline: Open Field",
  `Author: WW3MOD`) — identical visibility, identical rules stripping. It is the
  menu-backdrop map, not a skirmish map.

Because `SpawnStartingUnits` is what places `BaseActor: supplyroute`
(`mods/ww3mod/rules/world.yaml:444-492`), removing it means no Supply Route exists.

Confidence: **high**. Fix size: **minutes** (drop `Lobby` from `Visibility` on both).

---

### **[BLOCKER]** The lobby's only two AI options are named "Experimental AI" and "Stable AI 0802"

**Perceived:** A player opening the bot dropdown in a skirmish lobby chooses
between "Experimental AI" and "Stable AI 0802". There is no Easy/Normal/Hard, no
description, and no indication which is meant to be played against.

Evidence: `mods/ww3mod/rules/ai/ai.yaml:44-51` —
`ModularBot@experimental: Name: Experimental AI / Type: experimental` and
`ModularBot@stable: Name: Stable AI 0802 / Type: stable`. No other `ModularBot`
exists. `mods/ww3mod/languages/en.ftl` has no override for either name.
`0802` is an internal build date.

This is developer vocabulary shipped as the primary player-facing difficulty
choice, in the one menu every singleplayer must pass through.

Confidence: **high**. Fix size: **minutes** to rename; **unknown** if you want a
real difficulty ladder behind it.

---

## 2. SHOULD-FIX

### **[SHOULD-FIX]** Sidebar production-tab hotkeys are all unbound; F1–F12 is fully consumed by dead RA bindings

**Perceived:** Pressing `E`/`R`/`T`/`Y`/`U`/`I` does nothing — switching sidebar
tabs is mouse-only. Opening Settings → Hotkeys shows ~35 bindings for mechanics
this mod does not have.

Evidence:
- `mods/ww3mod/hotkeys.yaml:1,6,11,16,21,26` — `ProductionTypeBuilding`,
  `…Defense`, `…Infantry`, `…Vehicle`, `…Aircraft`, `…Naval` all have **empty
  defaults**, with the intended keys left as trailing comments (`# E`, `# R`, …).
  The five wired tab buttons (`ingame-player.yaml:1355,1373,1391,1425,1443`)
  therefore respond to no key and, per `ButtonTooltipLogic.cs:26-42`, show no
  hotkey in their tooltip either.
  (Likely cause: `ShowTerritory: T` at `hotkeys.yaml:46` took `T` from the
  Infantry tab and the rest were commented out with it.)
- Dead-but-bound, all confirmed by grepping `mods/ww3mod/rules/` for the trait:
  `CycleHarvesters` **N** (no `Harvester:` anywhere), `Repair` **C**, `Sell` **Z**
  (`Sellable:` exists but no SELL_BUTTON is ever created, so the order generator is
  never bound), `CycleProductionBuildings` **Tab**, `PowerDown`
  (`hotkeys.yaml:51`, no `PowerManager`), `StatisticsEconomy` **F3**,
  `StatisticsProduction` **F4**, and `Production01..24` occupying
  **F1–F12 + Ctrl+F1–F12** (`engine/mods/common/hotkeys/production-common.yaml:11-126`).
- Also unbound by default and therefore invisible: `SupportPower01..06`
  (`supportpowers.yaml:1-26`) — support powers are **mouse-only**;
  `RemoveFromControlGroup` (`control-groups.yaml:291`).

Confidence: **high**. Fix size: **hours** (rebinding is minutes; deciding the new
key map and pruning the dead list is the work).

---

### **[SHOULD-FIX]** ~50 garrison and cargo buttons have no tooltip and no hotkey; several are labelled just "X"

**Perceived:** Selecting a transport or a garrisoned building shows a grid of tiny
buttons, some labelled with a bare `X`, none of which explain what they do on hover.

Evidence: `mods/ww3mod/chrome/ingame-player.yaml` —
`GARRISON_PANEL :623-790` (`EJECT_PORT_0..7` at `:643-741`, label `"X"`;
`EJECT_ALL :773`) and `CARGO_PANEL :792-1135` (`MARK_CARGO_0..9`,
`RALLY_CARGO_0..9`, `EJECT_CARGO_0..9`, `MARK_ALL_CARGO :1084`,
`DEPLOY_MARKED :1091`, `DROP_ONE_SUPPLY :1105`, `DROP_SUPPLY :1112`,
`UNLOAD_ALL_TROOPS :1120`). **None** carries `TooltipText`, `TooltipContainer`
or `Key`.

This is wider than PIPELINE 60/61, which cover the Evacuate button (already
present — `ingame-player.yaml:327`, `Key: Evacuate`) and hotkey-in-tooltip.

Confidence: **high**. Fix size: **hours**.

---

### **[SHOULD-FIX]** Installer, Start Menu, registry, crash dialog and Discord all identify the product as OpenRA

**Perceived:** The player installs to `Program Files\OpenRA WW3MOD`, gets a Start
Menu folder called "OpenRA", sees "OpenRA" in Add/Remove Programs, and their
Discord friends see them playing OpenRA.

Evidence (`mod.config` unless noted):
- `:89` `PACKAGING_WINDOWS_INSTALL_DIR_NAME="OpenRA WW3MOD"`
- `:93` `PACKAGING_WINDOWS_REGISTRY_KEY="OpenRAWW3MOD"`
- `:47` `PACKAGING_WEBSITE_URL="http://openra.net"`
- `:51` `PACKAGING_FAQ_URL="http://wiki.openra.net/FAQ"` — this is the URL the
  **"View FAQ" button on the crash dialog** opens
  (`engine/OpenRA.WindowsLauncher/Program.cs:57-61,144`)
- `:75` `PACKAGING_DISCORD_APPID=""` — empty, and inconsistent with
  `mod.yaml:447-448` which sets `ApplicationId: 699222659766026240`
- `packaging/windows/buildpackage.nsi:54` Start Menu folder `"OpenRA"`;
  `:137,:201` desktop shortcut `OpenRA - WW3MOD.lnk`
- `packaging/macos/buildpackage.sh:94` app bundle `OpenRA - WW3MOD.app`
- `engine/Directory.Build.props:17` `<Product>OpenRA</Product>` → every DLL's
  Windows file-properties Product field

Discord specifically: `mod.yaml` sets only `ApplicationId`, so
`engine/OpenRA.Mods.Common/DiscordService.cs:35` uses the default
`Tooltip = "Open Source real-time strategy game engine for early Westwood titles."`,
published as `LargeImageText` (`:181`). The application **name** ("OpenRA") lives on
Discord's servers against that app id and **cannot be fixed in this repo** — it
needs a WW3MOD-registered Discord application.

Confidence: **high** for the packaging strings; **medium** for the Discord display
name (near-certain, but the app id's registered name is not readable from here).
Fix size: **hours** (plus an external step to register a Discord app).

---

### **[SHOULD-FIX]** "My OpenRA Server" is the prefilled default server name

**Perceived:** A player hosting a game for the first time publishes a server to the
public list named "My OpenRA Server" unless they retype it.

Evidence: `engine/mods/common/chrome/multiplayer-createserver.yaml:27`
`Text: My OpenRA Server`. This is a **hardcoded literal, not a fluent key**, so it
cannot be fixed from `en.ftl` — it needs a mod-owned copy of that chrome file or an
engine edit.

Confidence: **high**. Fix size: **minutes**.

---

### **[SHOULD-FIX]** Faction descriptions are blank, and Random Side says "vanilla"

**Perceived:** Choosing a side in the lobby, the player sees "America" and "Russia"
with no description of what either plays like; the random option is called
"Any Side" and describes itself as choosing "a random **vanilla** side".

Evidence: `mods/ww3mod/rules/world.yaml:242-253` —
`Faction@0: Name: America / Description: America\n` and
`Faction@1: Name: Russia / Description: Russia\n` (the description is the name plus
a newline). `Faction@randomside:236-241` — `Name: Any Side`,
`Description: Random Side\nA random vanilla side will be chosen when the game starts.`
Neither America nor Russia declares a `Side:` field (the commented-out Ukraine at
`:246-250` did).

Also worth reconciling: `credits.txt:2-3` and CLAUDE.md describe "NATO and America
against BRICS and Russia", but only two factions ship. The copy promises more than
the build delivers.

Confidence: **high**. Fix size: **minutes**.

---

### **[SHOULD-FIX]** Inherited engine strings still say "OpenRA" across the lobby, map browser and server browser

**Perceived:** Joining a server with a map you don't have shows "Searching OpenRA
Resource Center…"; the server browser says servers may require an "OpenRA forum
account".

Evidence — all in `engine/mods/common/fluent/chrome.ftl`, **none overridden** in
`mods/ww3mod/languages/en.ftl` (verified by grep):
`:191` `Searching OpenRA Resource Center...` · `:193` `OpenRA Resource Center` ·
`:186` `with this version of OpenRA` · `:232`,`:359` `Requires OpenRA forum account` ·
`:240-242` + `:317-318` `You are running an outdated version of OpenRA. Download the
latest version from www.openra.net` · `:407`,`:412-417` `Connect to an OpenRA forum
account` / `Failed to connect to the OpenRA forum.`
And `engine/mods/common/fluent/common.ftl:89`,`:512`,`:517` (same Resource Center /
forum wording).

Sub-finding on the **update notice** specifically: WW3MOD does not override
`WebServices` (`engine/OpenRA.Mods.Common/WebServices.cs:21-26` — all six URLs point
at `master.openra.net`), and `mod.yaml:3` still declares
`Version: release-20230225`. `MainMenuLogic.cs:500-504` shows the "outdated version
of OpenRA" banner iff the master server returns `outdated` for
`mod=ww3mod&version=release-20230225`. Whether it returns `outdated` or `unknown`
(which suppresses the banner) is a live network response I did not query.
**Confidence: medium — NEEDS VISUAL CHECK: launch to the main menu with network up
and look for a two-line yellow notice under the news button.**

Related and certain: `MapRepository = https://resource.openra.net/map/` means the
lobby's map-download path can never resolve a WW3MOD map, so the "Searching…" state
is terminal.

Confidence: **high** for the strings, **medium** for the update banner.
Fix size: **minutes** for the `.ftl` overrides; **minutes** for the version string.

---

### **[SHOULD-FIX]** The Force Move tooltip promises Chrono Tanks

**Perceived:** Hovering Force Move on the command bar — a button used every match —
the description ends "Chrono Tanks will teleport towards the target location."

Evidence: `mods/ww3mod/chrome/ingame-player.yaml:118` (`TooltipDesc`). This is
WW3MOD's **own** file, hardcoded, not inherited.

Note the broader issue: **every command-bar tooltip in WW3MOD is hardcoded English**
rather than a fluent key (contrast `engine/mods/ra/chrome/ingame-player.yaml:67`,
which uses `button-command-bar-attack-move.tooltip`). The command bar is
unlocalisable. Not a v1 blocker, but it is why these strings escaped the `.ftl`
sweep.

Confidence: **high**. Fix size: **minutes** for the one line.

---

### **[SHOULD-FIX]** The onboarding panel explains the economy and nothing else

**Perceived:** The first-run "There is no Construction Yard" briefing explains where
units come from, then hands the player a HUD with ~15 command buttons, 12 stance
buttons and a garrison panel it never mentions.

Evidence: `mods/ww3mod/chrome/ingame-info-howtoplay.yaml` — four sections, all
about the reinforcement economy and Supply Route contest, plus an Evacuate footnote.
**Zero controls, zero hotkeys**, no mention of stances, cohesion, engagement
policy, resupply policy, garrisoning, or suppression. There is no other in-game
controls reference anywhere.

**Accuracy check against the shipped model — the panel is substantially correct:**
- "tech level" — real; `TechTree:` is live at `mods/ww3mod/rules/player.yaml:14`.
- "one Supply Route, fixed, cannot build/move/destroy, indestructible" — correct;
  `structures.yaml:202-273`, `Armor: Type: Indestructable` at `:271`.
- "rally point is the only part you set" — correct; `RallyPoint:` at `:272`.
- "Supply Route contested!" — correct verbatim;
  `SupplyRouteContestation.ContestationTextNotification` at `:269`.
- **One overstatement:** "holding it long enough **puts them out of the match**".
  The actual mechanic (`engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs:24-25,354-373`)
  makes the player **passive** — production and income freeze — and is **reversible**
  ("Supply Route reclaimed! Production resuming.", `:76`). Outright defeat only
  follows when there are no allies or no remaining active team Supply Routes.

**Reachability (verified):** the panel is not lost after first run. `GameInfoLogic.cs:104-105`
adds a "How to Play" tab whenever the container exists, so it is always reachable
via the in-game Options button. It **auto-opens** only once — `HowToPlayVersion`
is written on open, not on dismiss (`MenuButtonsChromeLogic.cs:106-111`).
**In multiplayer it never auto-opens** (`ShouldShowHowToPlay` requires
`NonBotClients.Count() == 1`, `:126-133`), by deliberate design — the tab is still
there, but a new player's first *multiplayer* match comes with no briefing at all.

Confidence: **high**. Fix size: **hours** (a controls page).

---

## 3. POLISH

### **[POLISH]** Seven of ten shipped maps are stock OpenRA-RA maps with " WW3" appended

**Perceived:** The map list reads "Polar Disorder WW3", "River Zeta WW3", "Seventh
Woods WW3", "Twin Rivers WW3", "X-Lake WW3", "Siberian Pass WW3", "Woodland Warfare
WW3" — a naming convention that reads as a work-in-progress marker.

Evidence: `mods/ww3mod/maps/*/map.yaml` `Title:` fields. Verified that
`polar-disorder.oramap`, `Siberian-Pass.oramap`, `x-lake.oramap` and
`a-nuclear-winter` all exist in `engine/mods/ra/maps/`. Original author strings are
retained verbatim (`PizzaAtomica`, `Janitor`, `Azmac`, `Super Newbie`,
`Medium Tank, ZxGanon, XavierX`, `The Echo of Damnation`, `Lucian`) — which is
correct licensing practice, but combined with the titles it reads as unfinished.

Confidence: **high**. Fix size: **minutes** (retitle).

### **[POLISH]** No shipped map has a description

**Perceived:** The map browser's detail pane is blank for every map.
Evidence: zero `Description` hits across all ten `mods/ww3mod/maps/*/map.yaml`.
Confidence: **high**. Fix size: **hours**.

### **[POLISH]** Every map is flagged `Shellmap`, including the tank-duel harness

Evidence: all ten declare `Visibility: Lobby, Shellmap`. Benign today —
`Game.cs:682-689` returns the `FirstTimeShellmap` title match while `ShellmapOrder`
is empty (the default, `Settings.cs:325`), and `River Zeta WW3` matches. But
`GetAvailableShellmaps()` (`Game.cs:646-661`) applies no class or sanity filter, so
a player who sets a shellmap preference can get the 66×34 arena strip as their main
menu background.
Confidence: **medium** (the fallback path is inferred, not observed).
Fix size: **minutes**.

### **[POLISH]** Credits screen ends with three empty placeholder sections

**Perceived:** Scrolling the WW3MOD credits tab, the last three headings — MUSIC,
ART, SOUND — each read "(nothing here yet)".
Evidence: `mods/ww3mod/credits.txt:49-61`.
Otherwise the credits are in good shape: `credits.txt:1-46` names FreadyFish and
CmdrBambi, states GPLv3 with a link, points at OpenRA source, and disclaims the C&C
trademarks; `chrome/credits.yaml:22-30` gives both a WW3MOD tab and an engine tab.
The licence **is** surfaced to the player.
Confidence: **high**. Fix size: **minutes**.

### **[POLISH]** Observer "Harvesters" column always reads 0; Support Powers tab is empty

Evidence: `mods/ww3mod/chrome/ingame-observer.yaml:426` is a hardcoded
`Text: Harvesters` bound to `world.ActorsWithTrait<Harvester>()`
(`ObserverStatsLogic.cs:459-460`); the mod has zero `Harvester:` actors. The
Support Powers tab (`:501-507`) is empty because all airstrike powers are commented
out for v1 (`mods/ww3mod/rules/player.yaml:57-133`). The adjacent "Oil Derricks"
column (`:429-434`) **is** alive (`structures-neutral.yaml:29`).
The end-of-game `SKIRMISH_STATS` panel (`engine/mods/common/chrome/ingame-infostats.yaml`)
is Player/Faction/Score/Actions only — no economy columns, fine as-is.
Confidence: **high**. Fix size: **minutes**.

### **[POLISH]** In-game text still references construction, silos and ore

**Perceived:** Mid-match messages read "Unable to build more.", "New construction
options." (with an RA voice line saying the same), and "Silos needed."

Evidence: `mods/ww3mod/rules/player.yaml:23,45,103,233`;
`mods/ww3mod/rules/sound/notifications.yaml:58` (`NewOptions: newopt1`).
`ResourceStorageWarning` at `player.yaml:231` should never fire (no
`StoresResources` in the mod) but is dead code carrying wrong copy.
Confidence: **high** for the strings; **medium** that "Silos needed." can never
actually appear. Fix size: **minutes**.

### **[POLISH]** Destroyed-building husks are named after Red Alert structures

**Perceived:** Hovering wreckage shows "Construction Yard (Destroyed)", "Ore
Refinery (Destroyed)", "Ore Silo (Destroyed)", "Power Plant (Destroyed)",
"Arabian Barracks (Destroyed)", "Husk (Ore Truck)".
Evidence: `mods/ww3mod/rules/husks/husks-buildings.yaml:10,21,37,80,87,207`;
`husks-vehicles.yaml:36,43`; `mods/ww3mod/rules/misc.yaml:220` "Ore Drill".
**NEEDS VISUAL CHECK:** confirm any of these husk types actually spawn in a WW3MOD
match — if the parent actors are unreachable, this is dead data, not player-visible.
Confidence: **medium**. Fix size: **minutes**.

### **[POLISH]** `mod.yaml` still points at openra.net for website and icon

Evidence: `mods/ww3mod/mod.yaml:5` `Website: https://www.openra.net`, `:7`
`WebIcon32: …/icons/ra_32x32.png`. Both carry `TODO(release)` comments
(`:4`, `:6`) — known, unactioned. Surfaces in the server browser and mod chooser.
Confidence: **high**. Fix size: **minutes**, blocked on the user picking a homepage.

### **[POLISH]** Debug Menu checkbox is exposed in the lobby to every player

Evidence: `mods/ww3mod/rules/player.yaml:175-176` `DeveloperMode: CheckboxDisplayOrder: 90`.
The Settings → Advanced debug options are correctly hidden behind the stock
`DisplayDeveloperSettings` gate (`settings-advanced.yaml:107-197`), but the lobby
checkbox is not.
Confidence: **high**. Fix size: **minutes**.

---

## 4. COSMETIC / NOTES

- **[COSMETIC]** `Button@TAKE_COVER` (`ingame-player.yaml:227-245`) has **no `Key:`**
  — the only command-bar button without one — and reuses `ImageName: deploy`, the
  same icon as the Deploy button. **NEEDS VISUAL CHECK: open the command bar with
  infantry selected and confirm Take Cover and Deploy do not render identically.**
  Confidence: high on the YAML, medium on the visual collision. Minutes.
- **[COSMETIC]** `mods/ww3mod/chrome/garrison-panel.yaml` is **not listed in
  `mod.yaml`'s ChromeLayout** — a dead file; the live panel is inline at
  `ingame-player.yaml:623`. Confidence: high. Minutes (delete).
- **[COSMETIC]** The in-game info panel picks `TAB_CONTAINER_{numTabs}`
  (`GameInfoLogic.cs:107`). With 6 tabs the buttons are 80px wide
  (`ingame-info.yaml:119-159`) and must fit "How to Play" at Bold 14.
  **NEEDS VISUAL CHECK: open the in-game Options panel in a multiplayer match with
  the debug menu on (the 6-tab case) and confirm the tab labels are not clipped.**
  Confidence: low. Minutes.
- **[COSMETIC]** `mod.yaml:3` `Version: release-20230225` — the upstream OpenRA
  release tag shipped as WW3MOD's version. Visible in the server browser and in
  replay-compatibility errors. Minutes.
- **[COSMETIC]** A "Video Volume" slider is shown (`settings-audio.yaml:126`) though
  the movies package is commented out at `mod.yaml:18`. Minutes.
- **[COSMETIC]** `mods/ww3mod/rules/sound/music.yaml` is the stock RA soundtrack
  from the optional `scores.mix` package; the mod ships one own track
  (`bits/sounds/music/journey.aud`). **NEEDS VISUAL CHECK on an install that skipped
  the music download: does the Music player render empty, and does
  `MusicPlaylist VictoryMusic: score` (`world.yaml:5-7`) fail silently?**
  Confidence: medium.
- **[COSMETIC]** ~45 greyed-out placeholder lobby options exist
  (`LobbyDummyOptions`, `world.yaml:618`), each stamped "Not yet implemented —
  visual placeholder for a future feature." `LobbyOptionsLogic.cs:365` hides any
  section where *all* options are placeholders, which should hide almost all of
  them. **NEEDS VISUAL CHECK: open a skirmish lobby, expand advanced options, and
  confirm no grey "not yet implemented" rows are visible.** If any are, delete them
  for release rather than dimming them. Confidence: medium.

---

## 5. Verified NON-findings — do not chase these

- **Map previews are fine.** All ten maps have a real, correctly-sized `map.png`.
  Player counts and spawn counts agree on all ten.
- **Supply Routes are correctly absent from map files** — they are auto-placed by
  `SpawnStartingUnits` (`world.yaml:444-492`), so "no SUPPLYROUTE in map.yaml" is
  correct, not a bug. No `mcv`/`fact`/`proc`/`silo`/ore actors in any shipped map.
- **RA-era lobby options are already correctly suppressed.** `world.yaml:610-616`
  hides Build Radius, Build off Ally ConYards, Short Game and Tech Level;
  Redeployable MCVs was deleted outright (`player.yaml:200`). Starting Units was
  genuinely reworked around `BaseActor: supplyroute`. Time Limit was re-themed to
  "Doomsday Clock". This area is in good shape.
- **`mods/ww3mod/installer/*.yaml`** naming Red Alert discs is correct — those are
  the actual source media. The `ModContent` install copy (`mod.yaml:399-401`) is
  already WW3MOD-branded and honest about needing RA data.
- **`ra|fluent/*.ftl` actor and faction keys are unreachable** — WW3MOD actors carry
  literal `Tooltip: Name:` strings, and `mod.yaml` loads only `ww3mod|rules/*`.
  `mod-title = Red Alert` / `mod-windowtitle = OpenRA - Red Alert` in `ra/fluent/ra.ftl`
  are referenced nowhere; `mod.yaml:2,8` set `Title`/`WindowTitle` to `WW3MOD`.
- **The crash dialog title** uses the embedded `DisplayName` = WW3MOD. Only its
  "View FAQ" link is wrong (see the packaging finding).
- **`label-openra = WW3MOD`** (`en.ftl:28`) and `label-engine-credits = OpenRA`
  (`:31`) are both correct and deliberate.
- **Loading screen** is mod-owned art plus 20 lines of well-themed WW3 flavour text
  (`en.ftl:2-21`). No leak.
- **Main menu buttons** other than Missions all reach real panels: Skirmish,
  Multiplayer, Settings, Load, Replays, Music, Map Editor, Asset Browser, Content,
  Credits, Quit. No other dead ends found.
- **News button is off by default** — `Settings.cs:296` `FetchNews = false`, so the
  OpenRA project news feed is not shown unless the player opts in. Its label was
  already re-themed to "Battlefield News" (`chrome.ftl:316`).
