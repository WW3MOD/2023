# Recon — in-game info panel (`GAME_INFO_PANEL`) UI redesign

**Date:** 2026-08-30 · **Ref:** `main` @ `7de03906` (main checkout, clean against `origin/main`)
**Scope:** read-only analysis + mockups. No game code or YAML changed.

Mockups: [`WORKSPACE/mockups/`](mockups/)
— [A · Corrected](mockups/ingame-info-a-corrected.html)
· [B · Scoreboard](mockups/ingame-info-b-scoreboard.html)
· [C · Dashboard](mockups/ingame-info-c-dashboard.html)
· [Options tab](mockups/ingame-info-options.html)

---

## 1. Current state

### 1.1 Geometry

The panel is instantiated from `IngameMenuLogic.cs:223` into `PANEL_ROOT`. That root is
**580 × 500** at X 175 inside a 760 × 500 `COMBINED_PANEL`
(`engine/mods/common/chrome/ingame-menu.yaml:14-18`, `:40-44`). Every X coordinate below is absolute
inside that 580px panel.

`mods/ww3mod/chrome/ingame-info.yaml` is a near-verbatim copy of
`engine/mods/common/chrome/ingame-info.yaml`; the mod adds only `TAB_CONTAINER_6` (`:119-159`) and
`HOWTOPLAY_PANEL` (`:181-184`). The tab strip sits at `Y: 50, Height: 25` → **Y 50→75**
(e.g. `:38-42`), and every content panel is pinned at `Y: 65` (`:160-184`; upstream `:119-139`).

**Content therefore starts 10px inside the tab strip.** Confirmed, inherited from upstream, not
introduced by this mod.

### 1.2 Which tabs a spectator actually gets

From `GameInfoLogic.cs:70-105`:

| Tab | Gate | Spectator (6-AI) |
|---|---|---|
| Objectives | `iop.PanelName != null` (`:77-78`) | **yes** → `SKIRMISH_STATS` |
| Briefing | needs `MissionDataInfo.Briefing` (`:81-83`) | no (skirmish map) |
| Options | unconditional (`:86`) | **yes** |
| Debug | requires `world.LocalPlayer != null` (`:94`) | **no** — spectators have no `LocalPlayer` |
| Chat | requires `NonBotClients.Count() > 1` (`:97-98`) | **no** — one human, six bots |
| How to Play | container exists (`:104-105`) | **yes** |

Three tabs → `TAB_CONTAINER_3` (`:108`), width 360, X = (580−360)/2 = **110**. Exactly the screenshot.

### 1.3 The alignment defects have a single mechanical cause

`ingame-infostats.yaml` declares the header row and the data row as **two unrelated coordinate
systems** that were never reconciled. Headers live in `Container@STATS_HEADERS` at `X: 22`
(`:31-34`); rows live in `ScrollPanel@PLAYER_LIST` at `X: 20` (`:60-65`) with
`Container@PLAYER_TEMPLATE` at `X: 2` (`:86-89`) — also base 22, so the two bases agree and every
divergence below is a hand-typed child offset that drifted.

| Column | Header X (abs) | Row X (abs) | Δ | Cause |
|---|---|---|---|---|
| Player | **32** (`:36-39`, X:10) | **51** (`:104-108`, X:29) | **19px** | row reserves 29px for `Image@PROFILE` (`:91-95`); header does not |
| Faction | **252** (`:42-47`, X:230) | **286** (`:114-120`, X:264) | **34px** | header aligns to `FACTIONFLAG` (`:109-113`, X:230), not to the faction *text* |
| Score | **419** (`:48-53`, X:397) | **414** (`:121-125`, X:392) | **5px** | plain drift |

Additionally: **no `Align` property appears anywhere in `ingame-infostats.yaml`**, so Score is
left-aligned and digits go ragged. The observer panel next door does this correctly —
`Align: Right` appears 20+ times in `mods/ww3mod/chrome/ingame-observer.yaml` (`:299`, `:308`,
`:317`, …). This panel simply never received the same pass.

`Label@ACTIONS` (`:54-59`) is hidden at `GameInfoStatsLogic.cs:130-131` when no other non-bot client
exists — which is why the screenshot shows three headers, not four.

### 1.4 The missing margin is *worse* for spectators, and that is a second bug

`GameInfoStatsLogic.cs:119-128` — when `world.LocalPlayer` is null (spectator) the objective block is
hidden and everything above it is pulled up by the block's **full 75px height**, unconditionally:

```
statsHeader.Bounds.Y -= 75   →  81 − 75 = 6   →  absolute 65 + 6  = 71
playerPanel.Bounds.Y -= 75   → 105 − 75 = 30  →  absolute 65 + 30 = 95
```

The tab strip ends at **Y 75**. The header row's box therefore begins at **Y 71 — four pixels inside
the tab strip.** Glyphs do not collide only because `LabelWidget` centres text vertically in its
25px box; the visible gap between the tab bottom and the header text is ~2px.

For a *player* the same headers sit at 65 + 81 = **146**, comfortably clear. So the user's
"no separation to the tabs above" is not one defect but two stacked: the inherited `Y: 65` pin, plus
an unclamped spectator shift that eats the remaining margin and then some. **Fixing only the `Y: 65`
pin would leave the spectator case still colliding** — worth stating plainly, because that is the
obvious partial fix someone would reach for first.

---

## 2. Why the Options tab is empty — and it is not a spectator issue

**It is empty for everyone, player and spectator alike.** Nothing in the chain below reads
`LocalPlayer`, `IsObserver`, or any player scope.

1. `GameInfoLogic.cs:167-174` loads `LOBBY_OPTIONS_PANEL` with
   `configurationDisabled: () => true` (`:172`) — correct, read-only is what we want.
2. `LobbyOptionsLogic` picks its category from a hidden `Label@CATEGORY_FILTER`, defaulting to
   `CategoryAdvanced` when absent (`LobbyOptionsLogic.cs:229-230`).
3. `engine/mods/common/chrome/ingame-info-lobby-options.yaml` contains **no** `CATEGORY_FILTER`. The
   only one in the tree is `engine/mods/common/chrome/lobby-players.yaml:878`, i.e. the pre-game
   lobby. So the in-game panel is **Advanced**.
4. Every working option — `fog`, `explored`, `startingcash`, `gamespeed`, `timelimit`,
   `startingunits`, `passiveincome`, `incomemodifier`, `bounty`, `cheats`, sync reports — is in
   `CommonOptionIds` (`:71-86`) and is therefore **filtered out** (`:326-328`).
5. What remains is `LobbyDummyOptions` (attached at `mods/ww3mod/rules/world.yaml:443`), and that
   trait stamps `Placeholder = true` on **every** option it yields (`LobbyDummyOptions.cs:30-40`,
   specifically `:37`) — including the two Rules options (`:208-219`).
6. `RenderAdvancedSections` skips any section whose options are *all* placeholders
   (`:379-380`). Unit Availability, Combat Tuning and Game Rules are all-placeholder → all three
   skipped. The catch-all "Other" section has the same guard (`:391`).
7. The only other source that could have produced a non-placeholder Advanced option is
   `PowersLobbyOptions` (`airstrikes`, `airstrike-cooldown`) — **commented out** at
   `mods/ww3mod/rules/world.yaml:555`.

Nothing is left to draw. The scroll panel renders, empty.

### 2.1 What *should* be in there

**My opinion: the match's own rules, read-only — and that is nearly free.**

The question a player or spectator actually has mid-match is *"what settings is this game running
under?"* Today the only way to find out is to quit to the lobby. That is the content this tab is
shaped for, and it is already written; it is being filtered away.

| Item | Verdict | Where it comes from |
|---|---|---|
| Match / Economy / World settings, read-only | **free** | add `Label@CATEGORY_FILTER: Common` to the in-game panel. Disabled rendering is already on via `configurationDisabled` |
| Section headers | **near-free** | needs a `SECTION_HEADER_TEMPLATE`; `AddSectionHeader` silently no-ops without one (`:225`, `:261-262`). Copy `lobby-players.yaml:888` |
| Fog view (All Players / Disable Shroud / per-player) | **reuse** | `ObserverShroudSelectorLogic`, mounted as `SHROUD_SELECTOR` at `mods/ww3mod/chrome/ingame-observer.yaml:103-104` |
| Observer stat mode (8 modes) | **reuse** | `STATS_DROPDOWN`, same file `:248` |
| Follow selected player (camera lock) | **new** | genuinely absent. `ObserverStatsLogic` only does a one-shot viewport centre on a player's base when a row is clicked. Continuous follow does not exist |
| Chat while spectating | **new / separate** | the Chat tab is hidden outright when there is one non-bot client (`GameInfoLogic.cs:97-98`) — exactly the 6-AI case |

Game speed is worth a note: it appears as a read-only row here, but a spectator arguably wants it
*live*. That would be a real gameplay-order change, not a display change, and I have deliberately not
proposed it.

---

## 3. Proposed column set

Everything below is already tracked. `PlayerStatistics` is attached to every player at
`mods/ww3mod/rules/player.yaml:204`, and `GameInfoStatsLogic.cs:143` already resolves it.

| Column | Source | Why |
|---|---|---|
| Army | `PlayerStatistics.ArmyValue` (`PlayerStatistics.cs:54`) | best single "who is winning" number; conspicuously absent today |
| Kills | `UnitsKilled` (`:48`) | separates *ahead because they fought* from *ahead because unopposed* |
| Lost | `UnitsDead` (`:49`) | same, inverted |
| Cash | `PlayerResources.Cash` | in a call-in economy, banked cash is unspent combat power |
| Score | `Experience` (`:36`) | unchanged, still the sort key |

**Rejected, with reasons:** APM (`OrderCount`, `:34`) — meaningless for bots, near-meaningless for
one human. Harvesters / Oil Derricks — already in the observer Economy tab, not decisive here.
Buildings killed/lost (`:51-52`) — a fourth and fifth combat number for little marginal signal.
**Ping — not available**; `Session.Client` exposes a coarse `ConnectionQuality` enum, not latency.

**Supply Route contestation** (`ControlBarFraction`, `IsPassive` —
`SupplyRouteContestation.cs:220-225`) is the most WW3MOD-specific number available and appears in
direction C. Caveat: it is an **actor** trait on `SUPPLYROUTE`
(`mods/ww3mod/rules/ingame/structures.yaml:222` / `:303`), not a player trait, so a consumer must
resolve `world.ActorsHavingTrait<SupplyRouteContestation>()` filtered by owner.

### 3.1 The two audiences want different tables

A spectator has no fog to respect and should see everything. **A mid-match player must not.** Army
value, kill counts and cash are intelligence; this mod has invested real work in fog discipline, and
a scoreboard that leaks enemy army value through the Esc menu would quietly undo it. Direction B
therefore redacts enemy intel columns to `—` and fills them in at game end — mirroring what the
faction column already does today (`GameInfoStatsLogic.cs:256-267` discloses the true faction once
`WinState != Undefined`). **This is the single most important design constraint in the whole
document, and it is the one a naive "just add columns" implementation would get wrong.**

---

## 4. Feasibility verdicts

Widget inventory verified against `engine/OpenRA.Mods.Common/Widgets/`. All of `Container`,
`Background`, `ScrollPanel`, `ScrollItem`, `Label`, `LabelWithTooltip`, `Button`, `Image`,
`ColorBlock`, `GradientColorBlock`, `ProgressBar`, `Checkbox`, `DropDownButton`, `LineGraph`,
`ObserverArmyIcons`, `ObserverProductionIcons`, `ObserverSupportPowerIcons`, `MiniMap` exist.
**No mockup here invents a widget type.**

MiniYaml rules observed per `DOCS/reference/conventions.md:148` — blank lines between top-level
entries are significant; all proposed edits are child nodes of existing top-level entries, so the
adjacent-merge trap does not apply, but any new top-level template must carry its blank line.

| Direction | Widgets | New C#? | Size |
|---|---|---|---|
| **A · Corrected** | all existing, all already in this panel | ~6 lines: clamp the spectator shift in `GameInfoStatsLogic.cs:119-128` | **S** — one sitting |
| **B · Scoreboard** | + `ColorBlock` for the share bar | ~70 lines in `GameInfoStatsLogic.cs` to bind five columns + the relationship gate | **M** |
| **C · Dashboard** | + `LineGraph`, `ObserverArmyIcons` (both already instantiated in `ingame-observer.yaml`) | new logic class, ~200 lines, incl. the Supply Route owner lookup | **L** — multi-session |
| **Options** | all existing | none for the settings list; wiring only for the reused observer dropdowns | **S** for the free part |

Direction C's riskiest piece is the Supply Route lookup, because it is the only part not already
done somewhere. I would build that first to de-risk it, not last.

---

## 5. Ranking and recommendation

**Ship A + the Options `CATEGORY_FILTER` label first, as one small change. Then B. Treat C as a
separate proposal.**

1. **A + Options one-liner** — highest value per hour by a wide margin. It fixes both defects the
   user actually reported, it fills the empty tab he actually asked about, and it is almost entirely
   YAML. There is no reason to bundle it behind a larger redesign.
2. **B · Scoreboard** — the right answer to "maybe more columns". It changes what the panel is *for*
   without changing what it *is*, and it forces the ally/enemy disclosure question to be decided
   deliberately rather than by accident.
3. **C · Dashboard** — the most interesting and the one I would not start yet. Its real argument is
   that the Esc menu currently shows strictly *less* than the observer HUD it covers up; that is a
   genuine gap. But it is L-sized, and A+B will change how the user feels about the panel enough
   that C should be re-specified afterwards rather than committed to now.

The one thing I would not do is A alone. Fixing the margins and leaving the tab empty answers two of
the user's four points and leaves the one he asked a direct question about untouched.

---

## Watch

- **I did not run the game.** Every geometry claim is arithmetic from YAML plus the screenshot. The
  4px tab/header overlap in §1.4 is derived, not observed — I am confident in the numbers but I have
  not seen a pixel ruler on it. The claim I would most want verified in-game is that
  `LabelWidget` vertically centres in its box; if it top-aligns instead, the header text genuinely
  overlaps the tab buttons and the defect is worse than I have described.
- **The "Options is empty for players too" claim is the one I would bet on being wrong**, and it is
  the load-bearing claim of §2. I established it by reading the filter chain, not by opening the tab
  as a player. It fails if any map ships a `ScriptLobbyDropdown` (e.g. a `difficulty` option), which
  would be unsectioned, non-placeholder, and would populate "Other". River Zeta almost certainly has
  none — but a mission map would, and then the tab is non-empty there and my one-line fix is
  incomplete rather than wrong. **Cheapest possible check: open Options as a player on River Zeta.**
- **`PlayerResources.Cash` field names are second-hand.** I verified `PlayerStatistics` line by line
  myself but took `PlayerResources` (`Cash`, `Earned`, `Spent`, `Income`) from a subagent's report
  without opening the file. If a column binds wrong, that is where.
- **I have not verified that `ObserverShroudSelectorLogic` can be mounted twice** (once in the HUD,
  once in the menu) without the two instances fighting over shroud state. I asserted "wiring, not
  invention" — that assertion is untested and is the weakest claim in §2.1.
- **Colour and chrome fidelity in the mockups is eyeballed from the screenshot**, not sampled from
  `chrome.png`. The layout geometry is exact; the palette is approximate. Note also that the lobby
  has been redesigned to a documented pure-grayscale palette
  (`mods/ww3mod/chrome/_lobby-palette.yaml`) while this panel still wears the old amber RA chrome. I
  kept the mockups amber to match the screenshot the user is comparing against, but **that
  inconsistency is a real open design question I have not resolved** — if the grayscale language is
  meant to win, all four mockups are the wrong colour.
