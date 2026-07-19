# Lobby redesign — workspace

In-flight redesign of the WW3MOD skirmish lobby. Design happens in HTML
mockups under [`mockups/`](mockups/); once a direction is locked we
port it to OpenRA chrome (YAML + C# logic).

## Status

**Phase:** design **LOCKED** — awaiting implementation approval.

**Implementation plan:** [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md)
— phased port (9 phases, S/M/L sizing, risks, rollback, test plan).

**Visual target:** [`mockups/full-page-realistic.html`](mockups/full-page-realistic.html)
— pure grayscale chrome, 2×2 quadrants, left column unified scroll,
V5 player rows, BigBold 24 map name.

**Decision log:** [`decisions.md`](decisions.md).

## How to come back to this

1. Open [`mockups/full-page-realistic.html`](mockups/full-page-realistic.html)
   in a browser to see the locked design.
2. Read [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) for the
   phased port plan. Each phase is a separate commit on `main`.
3. Read [`decisions.md`](decisions.md) for the reasoning behind each
   design choice.

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

All locked. Player row direction is V5 (color-led). Palette is pure
grayscale. Left column is a single shared scroll area.

## Files in this workspace

```
WORKSPACE/lobby/
├── README.md                — this file (overview + status)
├── IMPLEMENTATION_PLAN.md   — phased port plan (CURRENT focus)
├── decisions.md             — design decision log
├── (next-steps.md moved to REVIEW/ — superseded by IMPLEMENTATION_PLAN.md)
└── mockups/
    ├── full-page-realistic.html   — locked design (pure-gray, V5, left scroll)
    ├── color-options.html         — A–F palette comparison (pick was C → pure gray)
    ├── player-list-variants.html  — V1–V5 row treatments (pick was V5)
    ├── full-page-final.html       — early "modern web" reference
    ├── full-page-q4-locked.html   — tab style + map browse direction
    ├── full-page-quadrants.html   — 2×2 quadrant variants
    ├── full-page-o3.html          — full lobby with O3 locked
    ├── right-panel-l1-order.html  — option ordering pass
    ├── right-panel-all-hero.html  — hero card vocabulary
    ├── right-panel-hero.html      — hero metrics at 1440p
    ├── right-panel.html           — round-1 right-side directions
    ├── variations.html            — early layout variations (legacy)
    └── index.html                 — original mockup index (legacy)
```
