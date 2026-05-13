# Lobby redesign — workspace

In-flight redesign of the WW3MOD skirmish lobby. Design happens in HTML
mockups under [`mockups/`](mockups/); once a direction is locked we
port it to OpenRA chrome (YAML + C# logic).

## Status

**Phase:** design iteration in HTML. Not yet ported to the in-game lobby.

**Latest:** [`mockups/player-list-variants.html`](mockups/player-list-variants.html) —
four player-row treatments (V1–V4) for the players quadrant. **Pending
pick.**

**Full-page reference for the agreed direction:**
[`mockups/full-page-final.html`](mockups/full-page-final.html) — single
polished example; underline tabs, dynamic player block, every
redundancy stripped. Player rows still use the older avatar style here
— supersede with the chosen V1–V4 variant when picked.

See [`decisions.md`](decisions.md) for the full decision log, and
[`next-steps.md`](next-steps.md) for what's left to choose and port.

## What's locked

Read [`decisions.md`](decisions.md) for reasoning. Quick summary:

1. **Layout:** 2×2 quadrant. Map TL · Settings BL · Players TR · Chat BR.
2. **Sizing (Q4):** map ~720 / settings rest on left; players auto-sized
   to slot count + spectator, chat fills the remainder on the right.
3. **Right-side options:** O3 Flat — no categories, every option a hero
   card in a 4-wide grid.
4. **Tabs:** moved into the panels (no global Map/Music toggle). Map
   panel gets `Map / Change Map`; Settings panel gets `Settings / Music`.
5. **Map browse:** opens inline in the map panel (no modal); search +
   filter chips + 3-column grid of map cards.
6. **Tab style:** underline (M2) — quietest treatment.
7. **Empty slots & spectator:** rendered inline in the players list with
   their action affordances; no separate roster.
8. **CTA:** single big green "Start Game" button centered in the bottom
   bar.

## What's open

- **Player row style** — V1 / V2 / V3 / V4 from
  [`player-list-variants.html`](mockups/player-list-variants.html).
- **Handicap** — removed from main row; needs an access path (context
  menu? expandable detail?). Track in a discovery if you go without it.
- **Responsiveness** — mockups target 1440p. OpenRA chrome is
  pixel-positioned; pick a sizing target before porting or write the
  row template with proportional widths.
- **Map browse details** — filter chip set, card density, sort options.

## How to come back to this

1. Open [`mockups/full-page-final.html`](mockups/full-page-final.html)
   in a browser to see the locked-in look.
2. Open [`mockups/player-list-variants.html`](mockups/player-list-variants.html)
   to pick V1–V4 (the remaining open decision).
3. Read [`decisions.md`](decisions.md) to understand why each piece
   landed where it did.
4. When ready to port: see [`next-steps.md`](next-steps.md) for the
   in-game implementation plan.

## Files in this workspace

```
WORKSPACE/lobby/
├── README.md            — this file (overview + status)
├── decisions.md         — decision log with rationale
├── next-steps.md        — what to do next + porting plan
└── mockups/
    ├── index.html              — original mockup index (legacy)
    ├── variations.html         — early layout variations (legacy)
    ├── right-panel.html        — round 1: A–E right-side directions
    ├── right-panel-hero.html   — round 2: D0–D4 hero metrics at 1440p
    ├── right-panel-all-hero.html — round 3: L1–L5 × F1–F6 layouts/features
    ├── right-panel-l1-order.html — round 4: O1–O5 reordering of L1
    ├── full-page-o3.html       — round 5: P1–P4 full lobby with O3 locked
    ├── full-page-quadrants.html — round 6: Q1–Q4 2×2 quadrant variants
    ├── full-page-q4-locked.html — round 7: M1–M4 tab styles + map browse
    ├── full-page-final.html    — round 8: single polished example
    └── player-list-variants.html — round 9: V1–V4 player rows (CURRENT)
```
