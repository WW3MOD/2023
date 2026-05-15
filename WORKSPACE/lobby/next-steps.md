# Lobby redesign — next steps  ⚠️ SUPERSEDED

This doc is kept for historical reference. The current plan lives in
[`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) — phased port with
risks and test plan. Read that instead.

---


What's left to do, in roughly the order it should happen.

## Open design decisions

### 1. Pick a player row style

[`mockups/player-list-variants.html`](mockups/player-list-variants.html)
shows V1–V4. Pick one (or call out a trait mix) before porting.

Quick decision frame:
- **V1 Bold name** if you want the name to dominate at a glance.
- **V2 Color edge** if you want identity-by-color and a quieter row.
- **V3 Faction-led** if team identity matters more than individual.
- **V4 Justified columns** if more columns are likely to come back
  (handicap, team-number, latency).

### 2. Decide where Handicap goes

Removed from the main player row to make room for bigger type. Need an
access path. Options:

- **Context menu** on right-click — discoverable but hidden.
- **Expandable detail** — click the player row to expand handicap +
  team + latency below.
- **Dropdown menu** from the spawn cell — overloads the cell with a
  hover state.
- **Drop it entirely** if it's never used. Track usage first.

### 3. Map browse details

Inline swap is locked, but the contents are still flexible:

- Filter chip set. Currently: All · 2-player · 4-player · 6+ ·
  Conquest · Scenarios. Add Co-op? Custom?
- Card density. 3-column at 1440p. At 1080p maybe 2-column.
- Sort options. Recent / Most played / Alphabetical / Player count.
- Search behaviour. Match name only? Or also author / tags?

## Porting plan

Once V1–V4 is picked, port to OpenRA chrome. The pieces:

### YAML changes — `engine/mods/common/chrome/`

- **`lobby.yaml`** — restructure root: remove the global Map/Music
  toggle, define the 2×2 body grid.
- **`lobby-mappreview.yaml`** — split into `MAP_PREVIEW_TAB` and
  `MAP_BROWSE_TAB` containers. The browse tab renders the inline map
  list (search + filter chips + grid of map cards). LobbyLogic swaps
  the visible tab.
- **`lobby-players.yaml`** — rebuild row template per chosen V variant.
  Drop avatar, drop handicap (or move to context menu).
- **`lobby-music.yaml`** — change root container so it slots into the
  BL Settings quadrant when the Music tab is active.

### C# changes — `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/`

- **`LobbyLogic.cs`** — wire the new tab-switching: Map↔Browse in the
  TL quadrant, Settings↔Music in the BL quadrant.
- **`LobbyOptionsLogic.cs`** — already mostly there for O3 flat (it
  hides placeholder sections). Verify the grid is 4-wide and the
  category ordering matches the agreed "frequency-of-change" order.
- **`MapPreviewLogic.cs`** — extend to handle the inline browse state.
  Currently shows preview only.
- **`LobbyUtils.cs`** — update slot widget rendering to the chosen V
  variant. SetupEditableSlotWidget / SetupSlotWidget / SetupEmptySlotButtons.

### Pitfalls to watch

- **OpenRA chrome is pixel-positioned.** No flexbox, no grid, no auto.
  Every X/Y/Width/Height is absolute. Decide a fixed sizing target
  (1440p reference) and let the engine scale. Most widget templates can
  use `PARENT_WIDTH * N / 100` expressions for proportional widths.
- **No rounded corners.** Card visual = ColorBlock for bg + thin
  ColorBlock for border on each side. Tedious but workable.
- **No drop shadows, gradients, or glow effects.** Glow on active cards
  in the mockup is decorative; in-game we just use the accent border.
- **Animations are limited.** Hover states usually static; no smooth
  transitions on switch toggles.
- **Bot names already work** (commit `bec2c7e0` added the fallback).
  Player row should still use `c.Name` for the display name.
- **Map title strip** changes already shipped (commit `90940c26`); the
  card-footer chrome is in the live lobby already.

## Quick start when resuming

```bash
# Open the locked-in look
open WORKSPACE/lobby/mockups/full-page-final.html

# Pick the player row variant
open WORKSPACE/lobby/mockups/player-list-variants.html

# Read the decision log
cat WORKSPACE/lobby/decisions.md
```

When ready to start porting, take one quadrant at a time:
1. Settings (BL) — already partially modernised; easiest to finish.
2. Map (TL) — needs the inline browse work in MapPreviewLogic.
3. Players (TR) — picks up the chosen V variant.
4. Chat (BR) — mostly cosmetic resize + compose row.

Each can ship as its own commit on `main`.
