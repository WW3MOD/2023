# Lobby redesign — decision log

Choices made across the design rounds, with the reasoning behind each.
Newest at top. Each entry names the mockup round it was locked in.

---

## 9 — Player row style — **OPEN**

Mockup: [`mockups/player-list-variants.html`](mockups/player-list-variants.html)

Four directions on the table. All drop the avatar; all use bigger type:

- **V1 Bold name** — single line, 26px hero name dominates; faction
  text + small flag; color as a dot; spawn 22px mono; ready 26px.
- **V2 Color edge** — 4px team-color stripe on left edge replaces the
  swatch column. Two-line: 22px name + quiet meta sub-line.
- **V3 Faction-led** — subtle US/RU tinted background + flag plate on
  left replaces avatar; color is a wide bar.
- **V4 Justified columns** — table rhythm with explicit headers; mono
  tile for spawn; AI variant tag as tracked-uppercase sub-line.

**Pending:** pick a variant (or call out a trait mix).

---

## 8 — Strip everything unnecessary — **LOCKED**

Mockup: [`mockups/full-page-final.html`](mockups/full-page-final.html)

Cut from the final pass:
- Section titles next to tabs (the tab labels *are* the title).
- Triple-redundant status (CTA right-side text, "1 modified" label,
  separate "Changes 1" badge — kept one canonical signal).
- "Open in editor" / "Sort: Recent" / "Replay last" / "Mute all" /
  "Whisper…" / team-balance bar — premature or rare.
- Speculative dashed cards (prototype filler).
- Trail descriptions under every hero card (kept only where the value
  alone is ambiguous).
- Map quadrant "Change Map" button (the tab handles it).
- Preset bar "Rename" / "Delete" (folded into Load menu).
- "Handicap" column on player rows (rare; move to context menu).

Polished:
- Consistent 8/12/14/18/22 spacing scale.
- Single accent treatment (green dot + accent-bg, nothing competes).
- Chat compose unified into one bordered shell (mode pill + input,
  Enter sends, no separate Send button).
- CTA bar: simple grid with one big green button.
- Top bar: quieter, status reduced to a dot + one phrase.

---

## 7 — Tab style: **underline** — **LOCKED**

Mockup: [`mockups/full-page-q4-locked.html`](mockups/full-page-q4-locked.html) (M2)

Three styles compared:
- **M1 Pill** — filled-pill buttons in a padded container.
- **M2 Underline** ← picked. Uppercase letterspaced text with a 2px
  accent rail under the active tab. Section title collapses *into* the
  tabs (one less thing to render).
- **M3 Segmented** — iOS-style with inset shadow on active pill.

**Why:** quietest treatment. Aligns with the user's recurring
"less is more" preference. Tabs sit visually with the existing 2px
accent rails used elsewhere in the chrome.

---

## 7b — Map browse opens inline — **LOCKED**

Mockup: [`mockups/full-page-q4-locked.html`](mockups/full-page-q4-locked.html) (M4)

Clicking "Change Map" swaps the map panel's body — no modal. Body
becomes: search bar + filter chips ("All · 2-player · 4-player · 6+ ·
Conquest · Scenarios") + 3-column grid of map cards. Current map
outlined in accent with a "CURRENT" badge. Clicking another card
swaps the selection and flips back to the preview tab.

**Why:** modals fragment the experience; inline swap keeps the lobby
context intact. The map panel already has the right shape for this.

The same swap pattern applies to Settings ↔ Music in the BL quadrant.

---

## 6 — Q4 sizing locked — **LOCKED**

Mockup: [`mockups/full-page-quadrants.html`](mockups/full-page-quadrants.html) (Q4)

Quadrant split: left column 720 / 560 (map / settings), right column
auto-sized players + chat-fills-rest.

**Why:** the user explicitly clarified — chat-dominant *not* because
they're social, but because the player list should take **only as much
space as it needs for the slot count**, and chat soaks up the rest. So
players is dynamic (depends on max-players), chat is the remainder.

This makes the layout adaptive: a 2-player map gives chat a huge area,
an 8-player map gives chat less. The geometry handles both cleanly.

---

## 5 — Layout: 2×2 quadrants — **LOCKED**

Mockup: [`mockups/full-page-quadrants.html`](mockups/full-page-quadrants.html)

```
┌─────────────────┬─────────────────┐
│                 │                 │
│   MAP           │   PLAYERS       │
│                 │                 │
├─────────────────┼─────────────────┤
│                 │                 │
│   SETTINGS      │   CHAT          │
│                 │                 │
└─────────────────┴─────────────────┘
```

Settings moves under the map (was on the right). Players moves to top-
right (was bottom-left). Chat becomes a dedicated quadrant (was a
ticker strip at the bottom).

**Why:**
- Map + settings naturally pair (look at the map, decide the rules).
- Players + chat naturally pair (who's here, what are they saying).
- Each section gets a real area instead of a strip.
- Symmetry of the 2×2 grid simplifies the visual hierarchy.

---

## 4 — Music tab moves out of global, into Settings — **LOCKED** *(superseded in implementation: shipped as a `Chat / Music` toggle over the chat quadrant instead — a69c05fa; the tab sits on the panel it swaps, so the pattern holds, just in BR not BL)*

Mockup: [`mockups/full-page-q4-locked.html`](mockups/full-page-q4-locked.html)

The global Map / Music toggle in the top bar is removed entirely. Music
becomes a tab *inside* the Settings quadrant: `Settings / Music`. Same
inline-swap pattern as the map's `Map / Change Map`.

**Why:** Map/Music doesn't make conceptual sense as a top-level toggle
once the layout becomes quadrants. The map has its own panel already.
Music is a side concern that belongs near other settings.

---

## 3 — Right-side options layout: O3 Flat — **LOCKED**

Mockup: [`mockups/right-panel-l1-order.html`](mockups/right-panel-l1-order.html) (O3)

No category headers. ~16 hero cards in one continuous 4-wide grid,
ordered by *how often a host changes them* (most-touched at top,
rarely-touched at bottom). The visual rhythm of the grid is the only
structure.

**Why:** user feedback — "less is more, I am open to dropping the
categories completely and just listing everything in unnamed groups."
Section headers were adding visual chrome without earning their keep.

---

## 2 — Hero card vocabulary: "All Hero" (L1) — **LOCKED**

Mockup: [`mockups/right-panel-all-hero.html`](mockups/right-panel-all-hero.html) (L1)

Every option — dropdowns AND toggles — uses the same hero card
treatment: 11px uppercase label, 22px value, optional 11px trail.
Toggles get a switch in the top-right corner; dropdowns get a chevron.

**Why:** consistency. The user picked this over masonry (L2), section
bands (L3), compact 6-wide (L4), two-column (L5).

---

## 1 — Settings reorg before redesign — **DONE (during initial pass)**

Implemented before the design rounds started (commits on `main`):
- `UNIT AVAILABILITY` / `COMBAT TUNING` / `GAME RULES` placeholder
  sections hidden — they only contained `Placeholder=true` options.
- `Friendly Fire` / `Powers Enabled` no longer rendered (also
  placeholders).
- `DEVELOPER` section retired — `sync` hidden outright (debug-only
  toggle), `cheats` (Debug Menu) promoted into `WORLD`.
- Lobby map preview "title strip" treatment — name left, author/type
  right, both pinned to the 30px footer instead of floating.

These ship in the in-game lobby today. Subsequent rounds rebuild the
whole layout in HTML and will replace what's there once locked.
