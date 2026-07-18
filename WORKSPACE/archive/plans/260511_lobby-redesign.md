# WW3MOD Lobby Redesign — Spec (v2)

## Context

The lobby currently has three tabs (`Players | Options | Music`) but the `Players` tab is doing too much: it shows the roster, the active-changes bar, the preset row, *and* a "COMMON OPTIONS" block — yet it's labeled "Players". The label lies to the user about what lives there. At the same time the player list scrolls internally even though it doesn't need to (5–8 rows fit easily in the available vertical space), and the right column is starved while a 25%-of-screen chat panel sits empty pre-match.

**New framing (this revision):** the first tab is the **MATCH** tab — a single overview page that contains everything a host adjusts *per match*: the player roster (auto-expanding to fit all slots, no inner scroll), the common-options grid, active changes + presets, and the map preview. The second tab is **ADVANCED** — rarely-changed knobs (handicap-style toggles, placeholder gameplay options, developer flags). The third tab is **MUSIC**, unchanged in scope.

Goal: one page, one glance, to answer *"is the match ready to start?"* Move every rarely-touched lever out of sight. Get the lobby visually quiet so the *content* (who's playing, on what map, with what changes) is what the eye reads.

Tab-1 name choice: `MATCH` (selected). Rejected: `Players` (misleading — it has options too), `Overview` (vague), `Setup` (verb-ish but fine), `Lobby` (recursive — the whole screen is the lobby). Open to override.

## Constraints

- **Minimize engine churn.** `LobbyLogic.cs` and `LobbyOptionsLogic.cs` are upstream OpenRA code. Push as much new logic as possible into new files (`LobbyActiveChangesLogic.cs`, `LobbyPresetLogic.cs`) to avoid merge friction.
- **Host-only mutation.** Only the host can change options, save/load presets, or reset. Clients see read-only state. Save/Reset buttons gated by `orderManager.LocalClient.IsAdmin`.
- **Network protocol untouched.** Option changes still flow through `OrderManager.IssueOrder("option Id Value")`. No new order types.
- **Trait interface preserved.** `ILobbyOptions` stays as-is. Adding new options remains a "drop a trait into a YAML" exercise.
- **No regressions to slot widgets.** Player name, color, team, spawn, ready — these are working. Faction-dropdown removal (replaced by clicking the flag icon) is the one slot-widget change in scope.

## High-order principles (drives every design choice below)

1. **Primary action is unmistakable.** `Start Game` is a real CTA — full-width accent strip at the bottom of the screen, not a button-amongst-buttons.
2. **Back is nowhere near Start.** Top-left, separated by the entire screen. No mis-click hazard.
3. **Tabs label what's below them, not above.** Move the tab strip to the top of its content. Resolves the current "Options are in the Players tab" confusion.
4. **The MATCH tab is the screen.** Every common adjustment lives there. Switching to ADVANCED should feel like opening a settings drawer — rare, intentional.
5. **Auto-expand, no inner scroll.** The player roster grows to fit the actual slot count. If we run out of vertical space we shrink the options grid before we add a scrollbar to the roster. The current internal-scrollbar behavior is a bug.
6. **The map is the centerpiece.** Right column has the largest map preview the layout allows. Map authoring info (name, author, scenarios) clusters on the map card.
7. **Chat is invisible pre-game.** Collapses to a one-line ticker. Expands on click or on new-message arrival.
8. **Closed slots are quiet.** Single-line placeholder rows, no flag, no faction widget, no per-row `+` button. Whole roster row reads as `Closed · click to open`.
9. **Active changes are interactive.** Chips are clickable (jump to the option), neutral-colored with `+`/`−` prefix and a separate `!` accent for warning-class options. No pink/green pastel.

## Affected files

**Mod chrome YAML:**
- `mods/ww3mod/chrome/lobby-options.yaml` — tab strip moved to top, 3 tab buttons (`MATCH` / `ADVANCED` / `MUSIC`). Drop SUMMARY label. Add section dividers.
- `mods/ww3mod/chrome/lobby-mainmenu.yaml` (or equivalent root) — Start Game promoted to full-width accent strip; Back moved to top-left header.
- *(new)* `mods/ww3mod/chrome/lobby-presets.yaml` — preset dropdown template, Save-As dialog, Manage modal.
- *(new)* `mods/ww3mod/chrome/lobby-active-changes.yaml` — chip rail widget template.

**Engine chrome YAML:**
- `engine/mods/common/chrome/lobby-players.yaml` — narrow the player-list area to reserve x ≥ 540 for the persistent right rail. Switch from fixed-height + inner scroll to height = `parent.height - reservedBottom`, and the slot-list to auto-expand based on slot count.

**Engine logic (extend, not replace):**
- `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyOptionsLogic.cs` — replace `CategoryOverrides`/`CategoryAliases` with a 2-bucket map (`common` / `advanced`). Add a `SectionGroups` table (option Id → section name + is-all-placeholder flag). Update `HiddenOptionIds`. Read a new `Placeholder` flag from options when rendering (dim row + register tooltip).
- *(new)* `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyActiveChangesLogic.cs` — diff `LobbyInfo.GlobalSettings.LobbyOptions` against defaults; classify each non-default option (Increased / Decreased / Warning); render chips; chip click switches tab + scrolls + briefly highlights the corresponding option row.
- *(new)* `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyPresetLogic.cs` — populate preset dropdown; load/save/delete; serialize current `LobbyOptions` to YAML in support dir; manage the implicit `Default` and auto-updated `Last game` entries.
- `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbySlotsLogic.cs` (or equivalent) — closed slots render as single-line `Closed · click to open` rows; faction icon column becomes click-to-cycle (replaces the per-row faction dropdown); spawn dropdown becomes a click target wired to the map preview's spawn letters.

**Option metadata:**
- `engine/OpenRA.Mods.Common/Traits/World/LobbyDummyOptions.cs` — add `Placeholder = true` to every dummy option.
- `engine/OpenRA.Mods.Common/Traits/LobbyOption.cs` — add `bool Placeholder { get; }` to the interface (default `false`).

**Persistence:**
- *(new)* `<SupportDir>/lobby-presets.yaml` — flat list of named presets.

## Design specifics

### Top-level layout

```
+----------------------------------------------------+
| < Back        Mission Briefing — River Zeta WW3    |  header bar (Back top-left)
+--------------------------------+-------------------+
| [MATCH] [ADVANCED] [MUSIC]     |                   |  tabs top, content below
+--------------------------------+   MAP PREVIEW     |
|                                |   (large)         |
|  (tab content — see below)     |                   |
|                                +-------------------+
|                                | River Zeta WW3    |  map metadata card
|                                | Conquest · 2 scen.|
|                                | by Medium Tank…   |
|                                +-------------------+
|                                | PRESET            |
|                                | [Default ▾] [⚙]   |
+--------------------------------+-------------------+
| Chat ticker (1 line, collapsed) — click to expand  |
+----------------------------------------------------+
|                  ████  START GAME  ████             |  primary CTA strip
+----------------------------------------------------+
```

- Left column ~520px. Tab content here.
- Right column ~280px. Map preview top (≥ 220h), map card, preset bar.
- Right column visible on all three tabs — context never disappears.
- Chat: collapsed to 28px single-line ticker pre-game. Click expands to a 200px overlay over the bottom of the left column without obscuring the CTA strip.
- Start Game: full-width accent strip, ~52px tall, only enabled when local player is ready.

### MATCH tab content (left column, top to bottom)

1. **Player roster — auto-expanding.**
   - Open slots and bot slots render full-row with name, color swatch, faction flag (clickable, cycles), team, spawn (clickable, syncs to map letters), ready toggle (large, row-wide tinting on toggle).
   - Closed slots render as slim one-line `Closed · click to open` placeholders (~22px tall vs ~44px for an active slot).
   - Spectator slot is a single dedicated row at the bottom of the roster, not crammed into the slot list.
   - Below the roster: a small `+ Add bot` action button. Drop the `Remove Bots` / `Auto-Team v` row — both become row-level actions (right-click slot menu).
   - Roster height = slot count × row height. No internal scroll. If the lobby's total height isn't enough to fit everything, the options grid scrolls — never the roster.
2. **Active changes strip (inline, full-width).**
   - Sits between the roster and the options grid.
   - Chips: neutral dark-fill, white text, `+`/`−`/`!` prefix.
   - `(2 changes from defaults)` counter as a label.
   - Clickable chip jumps to the option (scrolls grid, pulses border 2 frames).
   - Empty state: no strip rendered (no `(0)` placeholder).
3. **Common options grid — three subgroups.**
   - **Economy:** Starting Cash · Income Modifier · Passive Income.
   - **Match:** Game Speed · Doomsday Clock · Starting Units.
   - **World:** Explored Map · Fog of War · Kill Bounties · Separate Team Spawns.
   - Each subgroup gets a small uppercase subheader. Inputs aligned in a consistent 2-column grid within each subgroup.
   - Drop the word "COMMON" from the section header — these *are* the options on MATCH.

### ADVANCED tab content

Sections in order, each with a header. All section content is opt-in / rarely changed.

1. **Per-player handicaps** — moved out of the slot row (frees roster width). One row per active slot with a percent slider.
2. **Unit availability** — header reads `UNIT AVAILABILITY (28 toggles — placeholders, not yet wired)` in amber. Sub-groups Infantry / Vehicles / Aircraft, ~4 toggles per row.
3. **Combat tuning** — header amber, `(placeholders — not yet wired)`. 7 dropdowns (weapon range, damage, suppression, veterancy, sight, build speed, supply capacity).
4. **Game rules** — mixed: Friendly Fire (placeholder, dimmed) · Crates (working) · Short Game (working) · Tech Level (working).
5. **Developer** — Developer Mode · Sync Debug.

### MUSIC tab

Unchanged from current behavior. Just inherits the new tab position (top of content) and primary-CTA strip.

### Player roster — slot widget details

- **Faction column:** single clickable flag icon, no dropdown. Click cycles: Any → America → Russia → (future: Ukraine) → Any. Right-click opens a popup with all options (keeps discoverability).
- **Spawn column:** clickable letter chip. Click cycles through available spawn letters. Hovering a spawn letter on the map preview highlights the corresponding slot row (and vice versa).
- **Ready column:** removed as a separate column. Each row gets a `Ready` button at the right edge that, when toggled, tints the whole row green and replaces the button text with `✓ Ready`. Bots auto-ready and show the tint permanently.
- **Handicap column:** removed from MATCH. Moved to ADVANCED → Per-player handicaps.
- **Closed slot:** `Closed · click to open` placeholder. Click toggles to `Open` (which then gets a name field and the same row-widgets as an active slot). Drop the per-row `+` button.

### Active changes — chip semantics

Diff `LobbyInfo.GlobalSettings.LobbyOptions` against defaults. For each non-default option, render one chip:

| Class | Prefix | Visual |
|---|---|---|
| Increased | `+` | neutral chip, prefix in `#15803d` (green) |
| Decreased | `−` | neutral chip, prefix in `#b91c1c` (red) |
| Warning | `!` | neutral chip, full chip stroked in `#c2410c` (amber) |

Warning list (hardcoded, expandable): `timelimit` (Doomsday Clock), `cheats` (Developer Mode), `friendly-fire`, `crates` (when ON).

Chip click → switch tab containing the option → scroll into view → 2-frame border pulse on the row.

### Preset system

**Storage:** `<Platform.SupportDir>/lobby-presets.yaml`.

**Schema:**
```yaml
Presets:
    My quick 1v1:
        Created: 2026-05-11
        Options:
            startingcash: 30000
            timelimit: 30
            fog: false
```

**Built-in entries** (always at the top of the dropdown):
- `Default` — clears every option override back to engine defaults.
- `Last game` — auto-updated on lobby→game transition. Listed but does not auto-apply on next lobby open.

**Dropdown order**: `Default` · `Last game` · `──────` · user presets (alphabetical) · `──────` · `Manage presets…`

**Save As**: text-input modal. Rejects empty, `Default`, `Last game`, duplicates, path chars.

**Reset**: equivalent to picking `Default`.

**Manage modal**: list with Rename / Delete per row. Confirm on Delete.

**Host gating**: when `IsAdmin == false`, dropdown read-only; Save/Reset disabled.

### Placeholder visual treatment

- **Section level:** if every option in a section has `Placeholder=true`, header gets `(placeholders — not yet wired)` in amber.
- **Row level:** 70% opacity, neutral text color.
- **Tooltip:** hover the row → `Not yet implemented — visual placeholder for future feature.`

## Phased implementation

Each phase is independently shippable and visually verifiable. Phases 1–3 are pure layout / structural; Phases 4–6 add features.

### Phase 1 — Structural reskin (highest leverage)

**Goal:** the screen *looks* like the new design even before any options work changes. Resolves the worst IA issues.

1. Move the tab strip (`MATCH` / `ADVANCED` / `MUSIC`) to the top of its content.
2. Rename tab 1 from `Players` to `MATCH`.
3. Promote `Start Game` to a full-width accent strip at the bottom.
4. Move `Back` to the top-left header.
5. Switch the player-list widget from fixed-height + inner scroll to auto-expand based on slot count.
6. Move `Spectate` out of the slot list into a dedicated single-row widget below the roster.

**Files:** `engine/mods/common/chrome/lobby-players.yaml`, `mods/ww3mod/chrome/lobby-options.yaml`, mod root chrome (Start/Back). No engine `.cs` changes required.

**Verification:** open a local skirmish, see 3 tabs top-aligned, see `MATCH` as tab 1, see Start Game as a strip, see Back top-left, change slot count from 2 to 8 and watch the roster grow without a scrollbar.

### Phase 2 — Player roster polish

1. Closed slots render as one-line `Closed · click to open` placeholders.
2. Drop per-row `+ Add bot` glyphs. Keep `+ Add bot` as a single button below the roster.
3. Replace the faction dropdown with a click-to-cycle flag icon (right-click for menu).
4. Drop the handicap column from MATCH (moves to ADVANCED in Phase 5).
5. Replace the tiny `Ready` checkbox with a row-edge button that tints the whole row green when armed.
6. Wire spawn letters: clicking a slot's spawn cycles letters; hovering a spawn letter on the map preview highlights the matching slot.

**Files:** `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbySlotsLogic.cs`, slot widget chrome.

### Phase 3 — Right-rail map prominence

1. Reserve a 280px right column visible on every tab.
2. Map preview enlarged (≥ 220h), placed top of the rail.
3. Map metadata card below the preview: title, scenario count, author. `Change Map` becomes a small button under the card (or click-the-preview opens the chooser).
4. Drop the floating `Conquest skirmish` caption (now part of the card).
5. Preset bar at the bottom of the rail (placeholder container — populated in Phase 6).

**Files:** `engine/mods/common/chrome/lobby-players.yaml` (rail container), map widget refactor.

### Phase 4 — Common options reorganization

1. Replace `COMMON OPTIONS` header with three subgroup headers: **Economy**, **Match**, **World**.
2. Group options accordingly; align inputs in a consistent 2-column grid per subgroup.
3. Standardize input widths and value formatting.
4. Move handicap into ADVANCED → Per-player handicaps.
5. Tag every dummy option with `Placeholder = true`. Render placeholder rows at 70% opacity with the tooltip. Section header gets the amber `(placeholders — not yet wired)` suffix when *all* options in a section are placeholders.

**Files:** `LobbyOptionsLogic.cs` (CategoryOverrides → 2-bucket; SectionGroups dict; Placeholder support), `LobbyDummyOptions.cs`, `LobbyOption.cs` (interface flag).

### Phase 5 — ADVANCED tab content

1. Render ADVANCED with the four sections defined above (per-player handicaps · unit availability · combat tuning · game rules · developer).
2. Per-player handicaps section: read live slot list, render one slider per slot.
3. Verify placeholder treatment (amber section header where appropriate, dimmed rows, tooltip).

**Files:** `LobbyOptionsLogic.cs`, ADVANCED-tab chrome.

### Phase 6 — Active changes chips

1. New file `LobbyActiveChangesLogic.cs`.
2. On every option change, walk `LobbyInfo.GlobalSettings.LobbyOptions`, diff against defaults, build chip descriptors.
3. Render chips in the inline strip on MATCH (between roster and options grid).
4. Click → switch tab → scroll option into view → 2-frame border pulse.
5. Empty state: don't render the strip.

**Files:** new `LobbyActiveChangesLogic.cs`, MATCH chrome.

### Phase 7 — Presets

1. New file `LobbyPresetLogic.cs`.
2. Read/write `<SupportDir>/lobby-presets.yaml`.
3. Populate the rail's preset dropdown with `Default` · `Last game` · user presets · `Manage…`.
4. Save As: modal text-input, snapshot non-default options, write YAML.
5. Reset: re-issue defaults for every overridden option.
6. Last game: hook lobby→game transition, snapshot on transition.
7. Manage modal: rename / delete with confirm.
8. Host gating: read-only dropdown + disabled buttons when not admin.

**Files:** new `LobbyPresetLogic.cs`, preset chrome.

### Phase 8 — Chat collapse + visual polish

1. Chat collapsed to a 28px single-line ticker pre-game. Expand on click or on new-message arrival.
2. Drop the `All` filter button (single channel pre-game).
3. Unify button styles (one border treatment across the lobby).
4. Tone down chat timestamp color from saturated yellow to muted neutral.
5. Hide dead scrollbars (don't render when content fits).
6. Faction flag icons: subtle outline so closed-slot ≠ "Russian-looking".

**Files:** lobby chat widget, button styles, scrollbar visibility logic.

### Phase 9 — Multiplayer + autotest regression

1. Host on localhost + 1 client. Verify chip sync, preset read-only-as-client, option propagation.
2. `./tools/autotest/run-batch.sh --all` for regression sweep.
3. Visual sanity sweep with no presets, with many presets, with many active changes (≥ 8 chips), with a 6-player roster.

## Risks / open questions

- **Engine merge friction.** Every touch to `LobbyOptionsLogic.cs` and `LobbySlotsLogic.cs` compounds upstream-merge cost. Push logic into new files when possible.
- **Map-tied option defaults.** If a map declares a non-engine default (e.g. starting cash = 50k), Active Changes should compare against the *map's* default, not engine default. Verify `option.DefaultValue` reflects map override at runtime.
- **Section-all-placeholder logic.** If we wire one option in a placeholder section, the amber suffix becomes misleading. Compute per-render — only show amber if *every* option in section has `Placeholder=true`.
- **Auto-expanding roster vs. small windows.** If the lobby window is shrunk below the roster's natural height (impossible at default resolution, possible if user shrinks), the options grid would have to scroll. Decide: options grid gains internal scroll, *not* the roster. Document and enforce.
- **Spawn-map linking.** Wiring slot-row spawn-letter clicks to map-preview spawn letters requires a shared model. Check if `MapPreviewWidget` already exposes per-spawn click events; if not, this is a small engine addition.
- **Faction-icon click-to-cycle UX.** Three states (Any / America / Russia) is fine; four (with Ukraine) is borderline. If we hit > 4 factions, switch the cycle-on-click for a popup.

## Verification (end-to-end)

1. `make test` after each phase.
2. `make all` on macOS, expect zero warnings.
3. Single-player smoke (River Zeta WW3): all phases visually confirmed per their step list.
4. Multiplayer smoke: chip sync + host gating + protocol intact.
5. `./tools/autotest/run-batch.sh --all`.

## Out of scope (v1)

- Per-map preset scoping (global only).
- Map-trait-declared chip classifications (hardcoded warning list).
- Drag-reorder in Manage modal.
- Preset import/export UI (YAML manual copy works).
- Custom modal styling.
- Wiring placeholder options to actual gameplay (separate tracker work).

---

## After approval

1. Tracker entry in `WORKSPACE/RELEASE_V1.md`.
2. Begin Phase 1 — structural reskin. Visual diff is large, code surface is small, no engine `.cs` changes. Easy to verify by eye.
