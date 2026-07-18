# Lobby redesign — implementation plan

> **STATUS (2026-07-18): shipped, with deviations.** All phases landed (see
> `archive/sessions/active_260515_1730_lobby_implementation.md` for the commit
> map + the 260718 finishing pass). Where this doc and the tree disagree, the
> tree won:
> - **Phase 4** shipped as a plain 2×2 quadrant *swap* (commit 6cfb6d23) — the
>   unified left-column ScrollPanel was abandoned; no `LEFT_COL_SCROLL` exists.
> - **Music** is a tab over *chat* (BR), not inside Settings (BL) — a69c05fa.
>   decisions.md entry 4 predates this.
> - **Phase 2 hero tiles** were not built; settings render as 36px checkbox
>   rows + 50px dropdown rows in the 4-col grid. Option order comes from
>   `DisplayOrder` + Economy/Match/World buckets, not the plan's list (several
>   planned options are hidden via `HiddenOptionIds`).
> - **Phase 6** ships the restyled *stock* chooser inline, not the designed
>   browser (chips / CURRENT badge / search-top) — see BACKLOG.
> - **Phase 7** scope cuts (3-way filter, SYSTEM chip, mono timestamp) — see
>   BACKLOG.

Scope: port the locked design ([`mockups/full-page-realistic.html`](mockups/full-page-realistic.html))
into the live OpenRA chrome for the WW3MOD skirmish lobby.

**Design at a glance:**
- 2×2 quadrant layout — TL Map · TR Players · BL Settings · BR Chat.
- LEFT column is **one scroll area**: map preview stacked on settings.
- Pure grayscale palette (no hue), light-gray accent, green only for go-states.
- V5 player rows: color tile · flag · spawn · name · ready.
- O3 flat options: 4-wide hero grid, no category headers.
- Inline map browse (no modal) — `Map / Change Map` tabs swap panel body.
- Single big green `▶ Start Game` CTA at the bottom.

**Non-goals (out of scope for this plan):**
- Multiplayer-specific lobby (server browser, kick UI). Skirmish only first.
- Replay viewer, map editor entry points.
- Map browse beyond the basic search + filter chips + grid (advanced filters/sort
  later).
- Handicap UI access path — deferred until usage data exists.

**Source of truth:** [`decisions.md`](decisions.md) is the design log;
[`mockups/full-page-realistic.html`](mockups/full-page-realistic.html) is
the visual target; this doc is the porting plan.

---

## Phase 0 — Palette + reusable primitives

Goal: lay the foundations every later phase depends on. No user-visible
change yet (or just the palette swap).

### 0.1 Palette tokens
- WW3MOD chrome colors are inline ARGB hex strings on each ColorBlock,
  not centralized. There's no theme system to update — every value
  ships per-widget.
- **Action:** introduce a single `mods/ww3mod/chrome/_lobby-palette.yaml`
  documentation stub with the canonical values, and **search-and-replace**
  any existing greenish/blueish lobby chrome to the grayscale palette as
  we touch each file. (No engine support for tokens — discipline only.)

Canonical values (paste into the doc stub):

| Token | Hex | Used for |
|---|---|---|
| `bg-app` | `#0a0a0a` | outer fill behind panels |
| `bg-panel` | `#141414` | panel body |
| `bg-panel-2` | `#1a1a1a` | inner card / opt tile |
| `bg-banner` | `#1c1c1c` | panel head / strip / topbar / CTA |
| `bg-button` | `#222222` | button / pill body |
| `bevel-light` | `#3a3a3a` | top + left 1px on every panel/button |
| `bevel-dark` | `#030303` | bottom + right 1px |
| `line` | `#262626` | dividers |
| `line-soft` | `#1d1d1d` | row separators |
| `accent` | `#b4b4b4` | tabs (active), links, brackets, host badge |
| `accent-dim` | `#6e6e6e` | sys-event chip border |
| `ink` | `#d4d4d4` | primary text |
| `ink-2` | `#969696` | secondary text |
| `ink-3` | `#686868` | inactive tabs, meta |
| `ink-4` | `#404040` | placeholder |
| `go` | `#6ec890` | ready tick + Start Game fill |
| `go-text` | `#0b110d` | text on Start Game |
| `us` | `#6f93b8` | US flag fill |
| `ru` | `#b87070` | RU flag fill |

### 0.2 Bevel pattern
- No reusable `BevelBackground` widget exists. Every panel/button uses
  4 ColorBlocks (top, left, right, bottom).
- **Action:** establish a YAML convention — top+left = `bevel-light`,
  bottom+right = `bevel-dark`. Define once in the palette doc; every
  later phase follows it. No engine work.

### 0.3 Corner-bracket sprite
- Map preview gets four 12×12 L-marks in the corners (Westwood touch).
- **Action:** add `mods/ww3mod/uibits/corner-bracket.png` (12×12, 4
  rotations or single sprite mirrored). Register in `mods/ww3mod/chrome.yaml`
  as `^Brackets`. Alternative if asset work is too heavy: compose from
  2 ColorBlocks per corner (8 total per panel) — uglier but no sprite
  needed.
- **Recommendation:** start with 8-ColorBlock fallback to unblock
  Phase 3; add sprite as a polish pass.

### 0.4 Font check
- Audit confirmed: `BigBold 24` already registered, already used by
  `START_GAME_BUTTON` and `SERVER_NAME`. No mod.yaml change needed.

**Phase 0 deliverable:** `_lobby-palette.yaml` doc stub committed,
optional sprite added. **Reviewable:** palette legend visible in repo,
nothing breaking yet.

---

## Phase 1 — Top bar + CTA bar (outer chrome)

Goal: re-skin the top status bar and the bottom CTA bar. Easiest visual
change, low blast radius.

**Files:** `engine/mods/common/chrome/lobby.yaml` (top bar widgets near
DISCONNECT_BUTTON + START_GAME_BUTTON region).

**Changes:**
- Top bar background → `bg-banner` (`#1c1c1c`) with bevel-dark bottom
  border.
- Back button → bevel chrome, label uppercase Bold 14.
- Title → ZoodRangmah serif, accent color, letter-spaced. Sub-text in
  `ink-3`.
- Status indicator → 8×8 `go` colorblock + Tiny/TinyBold 10 `LOBBY READY`.
- Preset pill → bevel chrome.
- CTA bar background → `bg-banner`, bevel-dark top border.
- Stats strip on left — Tiny 10 labels, ink primary value: `Slots
  filled`, `Players`, `Bots`, `Spectators`.
- `✓ Ready` chunky bevel button.
- `▶ Start Game` → existing widget; restyle fill to `go`, border bevel
  in light/dark green, text BigBold uppercase. Width = 220–240, height
  = 44.

**Risk:** START_GAME_BUTTON has wired logic already; only restyle the
chrome, don't touch the click handler.

**Phase 1 deliverable:** lobby launches with new top bar and CTA bar;
everything else still old. **Test:** open skirmish lobby, click Start —
game launches.

---

## Phase 2 — Settings panel (BL)

Goal: re-style the already-flat options grid to match the design.
**Easiest big quadrant — start here.**

**Files:**
- `mods/ww3mod/chrome/lobby-options.yaml` — already 2-col grid; bump to
  4-col, restyle to hero tiles.
- `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyOptionsLogic.cs` —
  no logic change; verify column-count math holds.

**Changes:**
- Drop the 2-column row templates; introduce a 4-column tile template.
  Each tile: 60px tall, bevel chrome, label (Tiny 10 ink-3) over value
  (MediumBold 18 ink). Toggle in top-right (chunky 26×14 rect),
  dropdown chevron bottom-right.
- Drop section-header rows entirely (O3 locked direction).
- Order tiles by frequency-of-change (top → bottom): Starting Cash,
  Tech Level, Game Speed, Starting Units, Allow Spectators, Crates,
  Fog of War, Short Game, Bot Difficulty, Build Off Ally, Daylight,
  Debug Menu.
- Panel head: tabs `Settings / Music`. Underline accent on active.

**Gotcha (from audit):** the existing override removed grid layout in
favor of 2-column. Reverting to 4-column is straightforward — just
recalculate the X offsets and widths (each column = `PARENT_WIDTH * 25
/ 100` minus gap).

**Phase 2 deliverable:** Settings tab renders as the 4×3 hero grid in
the new palette.

---

## Phase 3 — Map panel (TL)

Goal: re-style the map preview, push the title strip to BigBold 24, add
corner brackets.

**Files:**
- `engine/mods/common/chrome/lobby-mappreview.yaml`

**Changes:**
- Wrap map preview in a panel with `bg-panel` body and bevel chrome.
- Panel head: tabs `Map / Change Map` (Change Map disabled in this
  phase — Phase 6 wires it).
- Map preview area takes top of body (currently 480 - 36 head - 50
  strip = 394 tall).
- Bottom strip 50px tall, `bg-banner` background, bevel-dark top
  border. Map name LEFT in BigBold 24 (font swap from `Bold` →
  `BigBold` on `LabelWithTooltip@MAP_TITLE`). Author + meta on RIGHT
  in Tiny 10 ink-3 uppercase.
- Four corner brackets in `accent`, 12×12, inset 8px from each corner
  of the preview area (NOT the strip). 8 ColorBlocks each (Phase 0
  fallback) until sprite ships.
- Remove "i Briefing" affordance — design choice, gone.

**Phase 3 deliverable:** map preview renders in new chrome; old/new
side-by-side feel obvious. Change Map tab present but inactive.

---

## Phase 4 — Left column: unify into one scroll

Goal: wrap TL (Map) + BL (Settings) into a single ScrollPanelWidget
that shares one scrollbar.

**Files:**
- `engine/mods/common/chrome/lobby.yaml` — restructure root: add
  `LEFT_COL_SCROLL` containing `MAP_PREVIEW_ROOT` + `OPTIONS_PANEL_ROOT`
  stacked.

**Changes:**
- Replace the existing TOP_PANELS_ROOT and the LEFT_COLUMN_PLAYERS
  scroll-height calc with:
  - `ScrollPanel@LEFT_COL_SCROLL` — width 720, height = body height,
    `ScrollBar=Right`, `CollapseHiddenChildren=False`.
  - Inside it: `Container@LEFT_MAP_SECTION` (height 480) +
    `Container@LEFT_SETTINGS_SECTION` (intrinsic height from option
    grid).
- Visual divider between map and settings: 1px bevel-dark line
  (already part of each panel's bottom border — naturally aligns).
- Player list height formula recomputed: was `PARENT_HEIGHT * 2 / 5 -
  76`, becomes a fixed `380` (Phase 5 gets the auto-size based on slot
  count).

**Gotcha (from audit):** Player row currently 909px wide (sum of
hardcoded columns). The right column at 1440 - 720 - 16*2 - 10 (gap) =
~678px will overflow. **Phase 5 must reduce the row width before the
left column wraps**, OR Phase 4 ships with a temporarily-narrower left
column (e.g. 600px) so the right column still fits.
- **Recommendation:** ship Phase 4 with left col at 600px wide as an
  interim, get scroll behavior validated, then Phase 5's V5 row swap
  brings the right column down to fit, and Phase 4 widens to 720 in a
  small follow-up commit.

**Phase 4 deliverable:** scrolling on the left column works (drag
scrollbar, mouse wheel inside both map preview and settings region).

---

## Phase 5 — Players panel (TR)

Goal: replace the 9-column player row with the V5 5-column layout.

**Files:**
- `engine/mods/common/chrome/lobby-players.yaml` — rebuild row
  templates.
- `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyUtils.cs` —
  update `SetupEditableSlotWidget` / `SetupSlotWidget` /
  `SetupEmptySlotButtons` to address the new widget IDs.

**Changes:**
- New row geometry (left → right): `28px` color tile · `40×26` flag
  · `28px` spawn letter · `1fr` name · `36px` ready tick.
- Row templates:
  - `TEMPLATE_HUMAN_HOST` — name + steel `HOST` badge.
  - `TEMPLATE_HUMAN_PLAYER` — name only.
  - `TEMPLATE_AI_PLAYER` — name + Tiny `EXPERIMENTAL` / similar tag.
  - `TEMPLATE_EMPTY` — single-row `▸ Play here` / `+ Add bot` /
    `Close` actions inline. (Logic unchanged.)
  - `TEMPLATE_SPECTATOR` — collapsed: 28px color block (greyed) + name +
    Tiny `Spectating` tag, top-bordered separator.
- Color tile: 28×28 ColorBlock with internal bevel (1px lighter
  top/left, 1px darker bottom/right against the player color).
- Faction flag (40×26): wraps existing flag image collection
  (`mods/ww3mod/uibits/flags.png`). **Gotcha (from audit):** the flag
  currently only renders inside the dropdown face, not as a standalone
  cell — needs a new `Image@PLAYER_FACTION_FLAG` widget reading
  `ImageCollection: flags`, `ImageName: <factionId>`. Add a small
  click-to-open-dropdown overlay if we want flag-click as faction-pick.
- Spawn letter: `MediumBold 18` mono, white-on-`bg-panel`. Click cycles
  spawn (existing behavior preserved).
- Name: `MediumBold 18`. Editable name input (own slot) reuses existing
  TextField inline-edit pattern.
- HOST badge: 1px `accent-dim` border, accent text, Tiny 10 letter-
  spaced `HOST`.
- Drop columns: Team (move to context menu, Phase 8), Handicap (drop
  per locked decision; backlog the access path).

**Auto-team button:** keep existing logic, restyle as the right-side
link in the panel head.

**Player panel height:** auto-size via `Height = headRow + (slot count
* row height) + spectator strip`. Hard-cap at right-column height to
prevent overflow when 12-slot maps are added later.

**Phase 5 deliverable:** all four row states render correctly; faction
swap, color swap, spawn cycle, ready toggle all work.

---

## Phase 6 — Inline map browse (Change Map)

Goal: clicking `Change Map` in the TL panel head swaps the body to the
inline map list (no modal).

**Files:**
- `engine/mods/common/chrome/lobby-mappreview.yaml` — add a sibling
  `MAP_BROWSE_TAB` container under the panel; toggled by panel logic.
- `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/MapPreviewLogic.cs` —
  add tab-switch handler. Existing map list logic from
  `LobbyMapListLogic.cs` (or equivalent) gets re-parented into the new
  container.

**Changes:**
- Body of the browse tab: search input (top, full width) + filter chip
  row (`All · 2-player · 4-player · 6+ · Conquest · Scenarios`) + map
  card grid (3 cols at left-col width 720 — each card ~228 wide). Each
  card: thumbnail + name + author + spawn count.
- Current map outlined in `accent` with a `CURRENT` Tiny tag.
- Click another card → swap selection, flip back to preview tab,
  trigger existing map-load chain.

**Phase 6 deliverable:** map switching works without leaving the
lobby.

---

## Phase 7 — Chat panel (BR)

Goal: re-style the chat list and compose row.

**Files:**
- `engine/mods/common/chrome/lobby.yaml` — chat region (currently
  `LOBBYCHAT` below the player block).

**Changes:**
- Move chat to its own quadrant (BR) — geometry handled by Phase 4's
  layout.
- Background `bg-panel`; head with `Chat (N msg)` tab + right-side
  `All · Allies · Spectators` filter (link-styled).
- Message row: `[ts ink-4 mono]` `[name us/ru/sys]` `[text ink]`.
- System messages: name renders as a small bordered chip
  (`SYSTEM` in accent-dim, 1px line border) followed by message text in
  ink-3.
- Compose row: 38px tall, bevel-dark top border. Left: mode pill
  (`All ▾`) in accent. Right: borderless text input.

**Phase 7 deliverable:** chat reads cleanly, send works, mode toggle
works.

---

## Phase 8 — Polish + edge cases

- Hover states everywhere (bg → `bg-row-hover` `#222222`).
- Tooltip parity check — all old tooltips still attached.
- Handicap: add a discovery to `WORKSPACE/DISCOVERIES.md` noting it's
  unreachable in v1; track usage.
- Team selection: confirm context-menu access path or admit deferral.
- Replace the 8-ColorBlock corner brackets with the proper sprite
  (Phase 0.3) if the asset shipped.
- Settings: any toggle/dropdown that doesn't fit the tile shape
  (multi-line value) — shrink value font or add an ellipsis.
- Cross-resolution test: 1920×1080, 2560×1440, 1366×768. Player row
  must not overflow at 1366. Left column scrollbar must always be
  visible at narrow widths.
- Accessibility: contrast ratio check on grayscale text. ink-3 on
  bg-banner is borderline (likely fine, verify).

---

## Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Player row overflows narrow screens after V5 swap | Medium | Medium | V5 is narrower than current 909px; verify at 1366 in Phase 5. Hard cap row width to right-col width with ellipsis on name. |
| ScrollPanelWidget doesn't nest map preview cleanly (re-render cost on scroll) | Medium | Medium | Audit gotcha — measure FPS while scrolling left col with preview visible. Fall back to non-scrolling left col if expensive. |
| Tab visibility-toggle pattern fights left-col scroll math | Low | Medium | Both tabs (Map / Change Map) share the same panel container; only their bodies swap, scroll geometry stable. |
| Faction flag widget rendering breaks dropdown click target | Low | Low | Wrap flag in a clickable container that proxies to existing dropdown trigger. |
| Bevel rendering at non-1x UI scales (e.g. 2× HiDPI) | Low | Medium | OpenRA scales sprites and widgets uniformly; 1px bevel becomes 2px at 2× — design tolerates this. Verify in Phase 1. |
| BigBold 24 map name truncation on long map names | Medium | Low | Existing `LabelWithTooltip` handles overflow with tooltip; verify behavior carries over after font swap. |

---

## Rollback strategy

Each phase is its own commit (or small commit cluster) on `main`. To
revert any phase:

```bash
git revert <phase-N-commit-sha>
```

Critical phases (3, 4, 5) get an `active_*` session log under
`WORKSPACE/archive/sessions/` so concurrent work doesn't collide.

If Phase 4 (left scroll) proves too risky during integration, fall
back: keep map and settings as separate panels (no shared scroll), each
with its own height. The design degrades gracefully — only the visual
"unified column" is lost.

---

## Test plan

**Per phase:**
- Build clean: `make all` (or `./make.ps1 all` on Windows).
- Launch skirmish lobby cold (`./launch-game.sh`).
- Run any `lobby-*` autotests once they exist (none today; Phase 8
  could add one).
- Manual smoke: open lobby → switch faction → swap color → cycle spawn
  → toggle ready → start game. Should reach in-game with no error.
- SCREENSHOT pass: capture the lobby in each state (idle, all-ready,
  map-browse open) and visually diff against the mockup.

**End-to-end after Phase 8:**
- All-ready 2v2 with 2 humans + 2 bots → start → in-game.
- 1v1 with 1 human + 1 spectator → start.
- Map browse: type in search → filter chips → select a different
  map → returns to preview tab with the new map loaded.
- Resize window during lobby (if supported) — layout reflows
  gracefully.

---

## Recommended phase order + sizing

| Phase | Effort | Risk | Dependencies | Order |
|---|---|---|---|---|
| 0 — Palette + primitives | S | L | — | 1st |
| 1 — Top bar + CTA | S | L | 0 | 2nd |
| 2 — Settings (BL) | M | L | 0 | 3rd |
| 3 — Map (TL) | M | M | 0 | 4th |
| 4 — Left scroll wrap | M | M | 2, 3 | 5th |
| 5 — Players (TR) | L | M | 0 | 6th |
| 6 — Inline map browse | M | M | 3 | 7th |
| 7 — Chat (BR) | S | L | 0 | 8th |
| 8 — Polish | M | L | all | 9th |

S/M/L = Small (1 session) / Medium (1–2 sessions) / Large (2–3 sessions).
Risk L/M/H = Low / Medium / High.

**Total estimate:** 12–18 work sessions to ship the full lobby.

---

## When this plan ships

After Phase 8 commits and a clean playtest, this doc moves to
`WORKSPACE/archive/lobby-redesign-shipped.md` with a final retrospective
appended. The mockups stay under `WORKSPACE/lobby/mockups/` as
historical reference.
