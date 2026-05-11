# WW3MOD Lobby Redesign — Spec

## Context

The lobby Options panel was reorganized into 6 tabs (COMBAT / ECONOMY / UNITS / MAP / RULES / ADV), and it now feels worse than before. Most tabs are sparse (4–5 options each), navigation costs clicks for trivial tweaks, and the structure hides that **~80% of advertised options are unimplemented placeholders** — 28 unit toggles, weapon range, damage scale, suppression, veterancy, build speed, supply capacity, sight range, friendly fire, bounty %, powers-enabled. The "Settings: …" summary at the top is read-only — no jump-to, no visual hierarchy, no warning treatment for high-impact options like the Doomsday Clock.

This redesign collapses to **two purposeful pages** — `PLAYERS` (roster + the 10 commonly-changed options that actually work) and `ADVANCED` (everything else, grouped, with clearly-labeled placeholder sections). A persistent **right-column rail** shows map preview, preset save/load bar, and color-coded Active Changes chips that act as both audit and shortcuts (click-to-jump-to-option).

Goal: surface what's *real* and *changed* in one glance; banish placeholder noise to a clearly-labeled corner; give hosts repeatable presets so they stop fighting the lobby every match.

## Constraints

- **Minimize engine churn.** `LobbyLogic.cs` and `LobbyOptionsLogic.cs` are upstream OpenRA code. Bulk of new logic goes into *new* files (`LobbyActiveChangesLogic.cs`, `LobbyPresetLogic.cs`) to avoid merge conflicts.
- **Host-only mutation.** Only the host can change options, save/load presets, or reset. Clients see read-only state. Save/Reset buttons gated by `orderManager.LocalClient.IsAdmin`.
- **Network protocol untouched.** Option changes still flow through `OrderManager.IssueOrder("option Id Value")`. No new order types.
- **Trait interface preserved.** `ILobbyOptions` stays as-is. Adding new options remains a "drop a trait into a YAML" exercise.
- **No regressions to slot widgets.** Player name, faction, color, team, spawn, handicap, ready — these are working areas, untouched.

## Affected files

**Mod chrome YAML:**
- `mods/ww3mod/chrome/lobby-options.yaml` — major rewrite. 6 tab buttons → 2 (PLAYERS / ADVANCED). Drop the SUMMARY label at top. Add section dividers in the option grid.
- *(new)* `mods/ww3mod/chrome/lobby-presets.yaml` — preset dropdown template, Save-As dialog, Manage-presets modal.
- *(new)* `mods/ww3mod/chrome/lobby-active-changes.yaml` — chip rail widget template.

**Engine chrome YAML:**
- `engine/mods/common/chrome/lobby-players.yaml` — narrow the player-list area to reserve x ≥ 540 for the persistent right rail. Player list compresses from full-width to ~520px.

**Engine logic (extend, not replace):**
- `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyOptionsLogic.cs` — replace `CategoryOverrides`/`CategoryAliases` with a 2-bucket map (`common` / `advanced`). Add a `SectionGroups` table (option Id → section name + is-all-placeholder flag). Update `HiddenOptionIds`. Read a new `Placeholder` flag from options when rendering (dim row + register tooltip).
- *(new)* `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyActiveChangesLogic.cs` — diff `LobbyInfo.GlobalSettings.LobbyOptions` against defaults; classify each non-default option (Increased / Decreased / Warning); render colored chips; chip click switches tab + scrolls + briefly highlights the corresponding option row.
- *(new)* `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyPresetLogic.cs` — populate preset dropdown; load/save/delete; serialize current `LobbyOptions` to YAML in support dir; manage the special "Default" (implicit) and "Last game" (auto) entries.

**Option metadata:**
- `engine/OpenRA.Mods.Common/Traits/World/LobbyDummyOptions.cs` — add `Placeholder = true` to every dummy option (or a class-level marker; either works).
- `engine/OpenRA.Mods.Common/Traits/LobbyOption.cs` (or wherever `ILobbyOptions` lives) — add `bool Placeholder { get; }` to the interface (default `false`).

**Persistence:**
- *(new)* `~/Library/Application Support/OpenRA/lobby-presets.yaml` (macOS path; engine uses `Platform.SupportDir`) — flat list of named presets.

## Design specifics

### Layout (both tabs)

```
+----------------------------------+----------------+
| Mission Briefing — <map name>                    |  header bar
+----------------------------------+----------------+
| [PLAYERS] [ADVANCED]             |  [ Map Prev ]  |  tabs left, map right
+                                  +----------------+
| (tab content, see below)         |  PRESET        |  persistent right rail
|                                  |  [Default ▾]   |
|                                  |  Save  Reset   |
|                                  |                |
|                                  |  ACTIVE CHANGES|
|                                  |  + chip        |
|                                  |  ! chip        |
|                                  |  - chip        |
+----------------------------------+----------------+
| Chat: [........]      [Ready]    [Start Game]    |
+--------------------------------------------------+
```

- Left column: ~520px wide. Tab content goes here.
- Right column: 240px wide. Map preview (top, 150h) + preset bar (80h) + Active Changes (remaining vertical space, vertically-stacked chips).
- Right column visible on **both** PLAYERS and ADVANCED — context never disappears.

### PLAYERS tab content (left column)

1. **Player list** — 5–8 rows. Each row: name, color, faction, team, spawn, handicap, ready check. Width compressed to ~492px (down from full-width). Bot/spectator add-buttons at the bottom.
2. **COMMON OPTIONS** section header.
3. **Options grid — only the 10 working options**:
   - Dropdowns (3-col layout, 2 per row): Starting Cash · Passive Income · Income Modifier · Starting Units · Game Speed · Doomsday Clock
   - Checkboxes (4-col row): Explored Map · Fog of War · Kill Bounties · Separate Team Spawns
4. **Border-color cross-reference**: a dropdown whose current value is non-default gets its border tinted to match the chip color (green = raised, red = lowered, amber = warning). Doomsday Clock with `30 min` shows an amber border on PLAYERS even before you look at the right rail.

### ADVANCED tab content (left column)

Sections, in order, each with a header:

1. **UNIT AVAILABILITY** — section header reads `UNIT AVAILABILITY  (28 toggles — placeholders, not yet wired)` in amber. Sub-groups: Infantry / Vehicles / Aircraft. ~4 toggles per row, ~24 visible without scroll, scrollable for the full 28.
2. **COMBAT TUNING** — header amber, label `(placeholders — not yet wired)`. 7 dropdowns (weapon range, damage, suppression, veterancy, sight, build speed, supply capacity).
3. **GAME RULES** — mixed: Friendly Fire (placeholder, dimmed) · Crates (working) · Short Game (working) · Tech Level dropdown (working).
4. **DEVELOPER** — Developer Mode (working) · Sync Debug (working).

### Placeholder visual treatment

- **Section level**: if all options in a section are `Placeholder=true`, the section header shows `<NAME>  (placeholders — not yet wired)` in amber (`#c2410c`).
- **Row level**: option label and value rendered at 70% opacity (color `#adb5bd`). No inline `(?)` glyph — keeps rows visually clean.
- **Tooltip on hover**: any placeholder row, anywhere on the row, shows the tooltip "Not yet implemented — visual placeholder for future feature." Engine tooltip widget handles this; just register text.

### Active Changes chips

For each option whose current value differs from its default:

| Classification | Color (fill / stroke) | When |
|---|---|---|
| Increased | `#b2f2bb` / `#15803d` (green) | Numeric > default; boolean was OFF, now ON |
| Decreased | `#ffc9c9` / `#b91c1c` (red) | Numeric < default; boolean was ON, now OFF |
| Warning | `#ffd8a8` / `#c2410c` (amber) | Option id is in the warning list, regardless of direction |

**Warning list** (hardcoded, expandable): `timelimit` (Doomsday Clock), `cheats` (Developer Mode), `friendly-fire`, `crates` (when ON — surprise factor).

Chip text format:
- Numeric: `+ Income $30,000  (was $20k)` / `- Sight 50%  (was 100%)`
- Boolean ON: `+ Crates ON`
- Boolean OFF: `- Fog of War OFF`
- Warning: `!  Doomsday  30 min` (no direction prefix)

**Chip click behavior**:
1. Switch to the tab containing that option (PLAYERS or ADVANCED).
2. Scroll the option row into view (if ADVANCED and the section is scrolled).
3. Pulse the row's border 3 times (3-frame yellow flash) so the eye lands on it.

### Preset system

**Storage:** `~/Library/Application Support/OpenRA/lobby-presets.yaml` (engine uses `Platform.SupportDir`, so this is auto-resolved across platforms).

**Schema:**
```yaml
Presets:
    My quick 1v1:
        Created: 2026-05-11
        Options:
            startingcash: 30000
            timelimit: 30
            fog: false
    Tournament rules:
        Created: 2026-05-08
        Options:
            startingcash: 20000
            techlevel: medium
            crates: false
```

**Built-in entries** (not in YAML, always present at top of dropdown):
- `Default` — clears all option overrides back to engine defaults.
- `Last game` — auto-updated when the lobby transitions to in-game (event hook on LobbyLogic). On lobby open, available as a preset but **does not auto-apply**. User must explicitly select it.

**Dropdown order**: `Default` · `Last game` · `——————` · user presets (alphabetical) · `——————` · `Manage presets...`

**Save As**: text-input modal. Validates: name not empty, not "Default", not "Last game", not duplicate. Adds entry to YAML, writes file, selects new entry.

**Reset**: issues option orders to clear every option override (same effect as picking `Default`).

**Manage presets...**: modal with a list, each row has Rename and Delete buttons. Confirm dialog on Delete.

**Host gating**: when `orderManager.LocalClient.IsAdmin == false`, the dropdown is read-only (shows current preset name but can't change), Save As / Reset buttons are disabled and grayed.

## Step-by-step implementation

### Step 1 — Add Placeholder metadata
1. Extend `ILobbyOptions` (or the concrete `LobbyOption` class) with `bool Placeholder { get; }`, default `false`.
2. Set `Placeholder = true` on every option in `LobbyDummyOptions.cs`.
3. In `LobbyOptionsLogic.cs`, when rendering a row: if `option.Placeholder`, set widget opacity to 0.7 and attach tooltip "Not yet implemented — visual placeholder for future feature."
4. **Build + smoke test** — open lobby, confirm all 35+ placeholder rows are visually dimmed in current 6-tab layout. (Pre-layout-change check.)

### Step 2 — Layout reshuffle (PLAYERS + ADVANCED only)
1. Rewrite `lobby-options.yaml` chrome:
   - 2 tab buttons (PLAYERS, ADVANCED).
   - Drop SUMMARY label.
   - Section divider widgets (text rows + horizontal rule).
2. Update `LobbyOptionsLogic.CategoryOverrides` to map all option Ids to `common` or `advanced`.
3. Add `SectionGroups` dict in `LobbyOptionsLogic.cs` — maps option Id → section name (e.g. `unit-availability`, `combat-tuning`, `game-rules`, `developer`, `common`).
4. When rendering ADVANCED tab, group options by section. Render section header before each group; suffix `(placeholders — not yet wired)` in amber if every option in the section has `Placeholder=true`.
5. PLAYERS tab gets a single "COMMON OPTIONS" section header above the option grid, no amber suffix (all working).
6. **Build + smoke test** — confirm 2 tabs, correct grouping, amber section headers on UNIT AVAILABILITY and COMBAT TUNING.

### Step 3 — Right-column rail (map + placeholder widgets for preset and chips)
1. Modify `engine/mods/common/chrome/lobby-players.yaml`:
   - Player list area narrows to width 0..520.
   - Add a right-rail Container widget at x=540, y=panel-top, width=240.
   - Map preview moved inside the rail at top.
2. Below map preview in the rail: placeholder Container for `PRESET_BAR` (will be filled by LobbyPresetLogic).
3. Below that: placeholder Container for `ACTIVE_CHANGES` (will be filled by LobbyActiveChangesLogic).
4. The rail container is visible regardless of which tab is active — it lives outside the tab content, attached to the lobby root.
5. **Build + smoke test** — switch tabs, map preview stays visible on the right.

### Step 4 — Active Changes chips
1. New file `LobbyActiveChangesLogic.cs`:
   - Constructor takes the chip container widget and the `OrderManager`.
   - Subscribes to lobby option change events (existing engine hook).
   - On change: walks `LobbyInfo.GlobalSettings.LobbyOptions`, compares each value against the trait's `DefaultValue`. Produces a `List<ChipDescriptor>`.
   - `ChipDescriptor`: `{ OptionId, Label, Classification, TargetTab }`.
   - Classification logic:
     - If `OptionId in WarningSet` → Warning.
     - Else if numeric and value > default → Increased.
     - Else if numeric and value < default → Decreased.
     - Else if boolean and value != default → boolean-state determines Increased/Decreased (ON=Increased, OFF=Decreased).
   - Clears and rebuilds chip widgets inside the rail container.
2. Chip widget: small Rectangle with fill+stroke colors per classification + Label child. Bind click handler.
3. Click handler:
   - Calls `LobbyLogic.SwitchTab(targetTab)`.
   - Calls a new `LobbyOptionsLogic.ScrollToOption(optionId)` method that scrolls the options scroll panel and pulses the row.
4. **Border-color cross-reference**: when rendering option rows, if the option is in the current chip list, tint the widget's border with the chip color.
5. **Build + smoke test** — change Starting Cash to 30000, watch a green chip appear. Toggle Fog off, red chip. Set Doomsday to 30, amber chip. Click each — verify tab switch + highlight.

### Step 5 — Preset system
1. New file `LobbyPresetLogic.cs`:
   - On lobby open: read `lobby-presets.yaml` from `Platform.SupportDir`. Parse into `Dictionary<string, Preset>`.
   - Populate dropdown widget with built-ins + user presets + "Manage" entry.
2. **Default**: when selected, issues `option <id> <defaultValue>` for every option whose current value is non-default.
3. **Apply preset**: when a user preset is selected, issues `option <id> <value>` for each entry in `preset.Options`. Options not in the preset are reset to default.
4. **Save As**: opens a modal text-input dialog (use existing `TextFieldWidget` + modal pattern from main menu). On confirm: snapshot current non-default options, add new preset entry, serialize YAML, write file.
5. **Reset**: same as picking Default.
6. **Last game**: hook into the existing lobby→game transition event. Snapshot non-default options and write to a fixed "Last game" key in the YAML.
7. **Manage modal**: list view + per-row Rename and Delete buttons. Delete shows confirm dialog. Rename opens a small inline text field.
8. **Host gating**: in the logic constructor, watch `orderManager.LocalClient.IsAdmin`. When false: dropdown becomes a read-only label, Save/Reset buttons are disabled (visual: 50% opacity, no click handler).
9. **Build + smoke test** — full preset round-trip:
   - Change options, Save As "test1", quit, relaunch, open lobby — confirm "test1" present.
   - Reset, confirm everything back to default.
   - Load "test1", confirm restoration.
   - Manage → delete "test1", confirm removed and YAML updated.
   - Start a game and exit, confirm "Last game" populated.

### Step 6 — Multiplayer test
1. Host on localhost, join as a second client.
2. As host: change options, save preset, load preset. Confirm chips visible to client.
3. As client: confirm dropdown is read-only, Save/Reset disabled.
4. Confirm option sync still works end-to-end (no protocol regression).

## Risks / open questions

- **Engine merge friction.** Each touch to `LobbyOptionsLogic.cs` compounds future upstream-merge cost. Mitigation: keep edits surgical, push as much logic as possible into the new `*ChangesLogic.cs` / `*PresetLogic.cs` files.
- **Map-tied defaults.** If a map's `MapOptions` trait declares a non-engine default (e.g. starting cash = 50k for an economy map), Active Changes should compare against the *map's* default, not engine default. Confirm `option.DefaultValue` reflects the map override at runtime. Likely already correct via `WorldActorInfo.TraitInfos<ILobbyOptions>().Default`; verify in playtest.
- **Section-all-placeholder logic.** If a section ever mixes working + placeholder (e.g. we wire one unit-toggle), the amber section header becomes misleading. Mitigation: compute per-render — only show amber suffix if *every* option in section has `Placeholder=true`. Single source of truth.
- **Preset name collisions / sanitization.** Save As should reject empty names, reserved names ("Default", "Last game"), and slashes/path chars that would break YAML. Show inline validation on the modal.
- **Long preset list.** No pagination in v1. If a user accumulates >20 presets the dropdown gets tall. Manage modal mitigates by letting them prune. Revisit if it becomes an issue.
- **Warning list is hardcoded.** If we want modders / map-makers to flag custom options as warnings, we need a `Warning: true` flag on the option trait. Defer until needed — current list is small and stable.
- **Chip overflow.** If a host changes 8+ options the right rail runs out of vertical space. v1 just clips at the chat-box boundary. If it's a real issue, add a "show all (N)" expander.

## Verification (end-to-end)

1. **Lint / YAML validation** — `make test` after each step.
2. **Build** — `make all` on macOS, expect zero warnings.
3. **Single-player smoke** — launch local skirmish on River Zeta WW3:
   - Confirm 2-tab layout. PLAYERS shows roster + 10 common options. ADVANCED shows the 4 sections with correct amber headers.
   - Toggle 3 options of different types (Starting Cash up, Fog off, Doomsday 30m). Confirm 3 chips appear in the right rail with correct colors.
   - Click each chip — confirm tab switch, scroll, and row pulse.
   - Save current settings as "test preset". Verify dropdown shows it.
   - Reset, confirm all options return to default and chips disappear.
   - Load "test preset" — confirm all options restored, chips reappear.
   - Quit to main menu, relaunch, open lobby — confirm "test preset" still listed.
   - Hover any (?)-marked row in ADVANCED — confirm tooltip "Not yet implemented".
4. **Multiplayer smoke** — host + 1 client (loopback or LAN):
   - As host change options. Client sees same chips.
   - As client confirm dropdown is read-only, Save/Reset disabled.
   - As host load a preset. Client sees options sync correctly.
5. **Regression** — `./tools/autotest/run-batch.sh --all` to confirm no autotests broken.
6. **Visual sanity** — open ADVANCED with no presets saved. Confirm placeholder sections are visibly muted, no overlap of map preview with chat, no clipping of long chip text.

## Out of scope for v1

- Per-map preset scoping. v1 is global. Add later if needed.
- Custom chip classifications declared by map traits. v1 uses a hardcoded warning list.
- Drag-to-reorder presets in the Manage modal.
- Importing/exporting preset YAML files between machines (the file is just a YAML — manual copy works).
- Visual polish on the modal dialogs — use existing engine modal styling, no custom theme.
- Wiring any of the placeholder options to actual gameplay — that's separate work tracked in `RELEASE_V1.md`.

---

## After approval

1. Copy this file to `WORKSPACE/plans/260511_lobby-redesign.md` (project's plan archive).
2. Add a tracker entry to `WORKSPACE/RELEASE_V1.md` (or wherever the user wants it logged).
3. Begin Step 1 — placeholder metadata, smallest change, easy to verify.
