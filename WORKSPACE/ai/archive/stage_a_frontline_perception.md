# Stage A — Frontline perception (foundation)

> First stage of the doctrine in [`doctrine.md`](doctrine.md). The
> smallest piece that unlocks everything else: the AI knows where the
> frontline is, and the user can see what the AI sees.
>
> **No bot decisions change in this stage** — it's pure perception
> + visualisation. That makes it safe to ship: nothing the AI does
> today gets worse, and the data is in place for the next stages.

## What "done" looks like

You launch a match vs v2 AI. You press the overlay hotkey (e.g. F11).
A coloured **band of cells** appears on the map showing where the AI
thinks the frontline is — i.e., the cells where both your influence
and the AI's influence are non-zero. The band:

- Sits where the actual contact between forces is at the start of a
  match (small, near the centre or near each Supply Route depending
  on who's pushing).
- **Moves** as you advance forces (band shifts toward enemy).
- **Retreats** as you fall back.
- **Thickens** where both sides have many units close.
- **Disappears** in sectors where one side has been wiped out (no
  enemy influence → no contested zone).

Toggling the overlay off restores the normal view. Toggling on
re-shows the band.

That's the whole acceptance criterion. No bot module reads from it
yet — that's Stage B.

## Why this first

Doctrine in [`doctrine.md`](doctrine.md) names seven derived
quantities (frontline, sector ratio, weak enemy sector, pressure
direction, safe rear, reserve home, reinforcement-lane danger). **All
seven derive from one shared influence map.** Get the influence map
right and the rest unlock cheaply. Get it wrong and every later
phase inherits the bug.

Visible verification (the overlay) means we don't need to wait until
Phase D to know if the influence numbers are right.

## Existing surface area

`ThreatMapManager` (`engine/.../BotModules/ThreatMapManager.cs`, ~428 LOC)
already exists. It's an 8×8 cell-block grid with separate military and
economic layers, refreshed periodically. Read it before writing new
code — most likely it gets *evolved* (denser grid, frontline derivation,
overlay rendering) rather than replaced.

The new code in this stage:

1. **Densify or replace ThreatMapManager** to per-cell or 2× cell
   granularity (4-cell blocks instead of 8-cell). Trade-off discussion
   in the influence-map docstring.
2. **Frontline derivation** as a new computed layer on top of the
   influence map. Output: `bool[width,height]` "is this cell in the
   contested band?".
3. **Debug overlay renderer.** OpenRA has a debug rendering pipeline
   (used by `WorldRenderer.DrawText` and similar). Find the existing
   render hook used by any current overlay; reuse it.
4. **Toggle hotkey** wired through the existing hotkey system.

## TODOs for Stage A (verifiable by the user)

Each TODO is a separate commit point. The user verifies each by the
listed observation, then we move to the next.

### TODO A.1 — Influence map at usable granularity

- [ ] Decide the data structure: per-cell `int[width,height]` for
      friendly + same for enemy, or 2-cell-block grid. Choose 2-cell
      blocks if perf is a concern; per-cell otherwise. Document the
      choice in code comments.
- [ ] Implement `InfluenceMap` as a world trait (one instance per
      world, not per player — we project per-player views off it).
- [ ] Refresh every 25 ticks (~1 sim-sec). Stagger across AI players
      so they don't all tick on the same frame.
- [ ] Per-actor contribution: `Actor.GetSellValue() / 100` weighted
      into a small square around the actor (radius e.g. 3 cells,
      Gaussian falloff or flat — start with flat for simplicity).
- [ ] Unit tests in `engine/OpenRA.Test/`:
      - empty world → all zeros
      - one infantry at (10,10) → non-zero in radius 3 around (10,10),
        zero elsewhere
      - two opposing infantry at (10,10) and (12,10) → cells in
        between have non-zero friendly AND non-zero enemy values

**Verify by user:** unit tests pass. No in-game change yet.

### TODO A.2 — Frontline derivation

- [ ] Method on `InfluenceMap` that, given a perspective player,
      returns a `bool[w,h]` where cell is true iff `friendly[c] > 0
      AND enemy[c] > 0`.
- [ ] Optionally a "thickness" or strength value: `min(friendly,enemy)
      / max(...)` per cell (so cells with balanced influence have
      higher frontline weight than lopsided cells).
- [ ] Unit test: same two-opposing-infantry case → frontline cells
      sit between them, not on either unit.

**Verify by user:** unit tests pass.

### TODO A.3 — Debug overlay infrastructure

- [ ] Find OpenRA's existing in-game debug render path (probably
      something on `IRenderAnnotations` or similar) and hook a new
      `FrontlineOverlay` trait into it.
- [ ] Render: for each frontline cell, draw a coloured fill (e.g.
      semi-transparent orange) over the cell.
- [ ] Initially: always-on for testing. Toggle comes in A.4.

**Verify by user:** launch a match vs v2 AI. Without pressing
anything, you should already see orange cells where the two forces
contact each other. Move your units; the orange band moves with
them.

### TODO A.4 — Hotkey toggle

- [ ] Add `Settings.Game.AIDebugOverlay` (bool) defaulting to false.
- [ ] Hotkey (F11 default, configurable) toggles the setting.
- [ ] `FrontlineOverlay` reads the setting; draws only if true.

**Verify by user:** F11 toggles the orange band on and off. State
persists across game pause but resets per match.

### TODO A.5 — Per-player perspective

- [ ] The overlay should default to *the observing player's*
      perspective — friendly = observing player, enemy = enemies of
      observing player. Spectator slot uses no specific perspective
      (show all contested cells regardless of point of view).
- [ ] Tournament watcher (`BotVsBotMatchWatcher`) can optionally log
      frontline-cell-count per tick into the watcher.log — useful
      for the next stage but not required for Stage A acceptance.

**Verify by user:** in a 1v1 you played as USA, the overlay shows
the frontline from USA's view. Switch to a spectator scenario, the
overlay shows the contested zone for all players together.

### TODO A.6 — Docs + demo

- [ ] Update `WORKSPACE/ai/doctrine.md` Phase A status to "shipped".
- [ ] Add `DOCS/gameplay/ai-overlay.md` (new gameplay doc) describing
      the overlay for a curious player: how to toggle, what the colour
      means.
- [ ] New demo: `tools/autotest/scenarios/demo-frontline-overlay/`
      that loads a match with two pre-spawned armies pushing toward
      each other so the band is immediately visible.

**Verify by user:** `./tools/autotest/run-demo.sh demo-frontline-overlay`
launches a match. Within 5 sim-sec the orange band is visible. You can
toggle it off/on with F11.

## Acceptance for Stage A overall

1. All 6 TODOs landed and committed.
2. Demo scenario plays as expected.
3. Unit tests pass.
4. No regression in existing autotests (`./tools/autotest/run-batch.sh --all`).
5. Doctrine doc Phase A status updated.

Once all of the above hold, we move on to Stage B (defensive layer
placement) — which is where the AI's *behaviour* starts to use this
data.

## Progress (260512)

- **A.1 — Influence map.** Shipped. `InfluenceMap` world trait with
  per-player layers, refreshed every 25 ticks, CellSize=2 grid.
  10 math tests pass (`InfluenceMapMathTest`).
- **A.2 — Frontline derivation.** Shipped (folded into A.1).
  `InfluenceMapMath.DeriveFrontline` static helper + math tests.
- **A.3 — Debug overlay.** Shipped. `FrontlineOverlay` world trait
  toggled via `/frontline` chat command. Renders filled orange
  circles at contested grid cells (`CircleAnnotationRenderable`).
- **A.5 — Per-player perspective.** Shipped alongside A.3 — overlay
  uses `world.LocalPlayer` POV with fallback to all-perspective for
  spectators.
- **A.4 — Hotkey toggle.** Not started. Chat command (`/frontline`)
  works as the interim toggle. F11 binding is straightforward but
  hasn't been wired.
- **A.6 — Demo + docs.** Demo `demo-frontline-overlay` shipped;
  gameplay doc `DOCS/gameplay/ai-overlay.md` shipped.

**Try it:**

```bash
./tools/autotest/run-demo.sh demo-frontline-overlay
```

Then in chat: `/frontline` — the orange band appears between the two
armies. Move a Bradley forward, the band shifts with it.

## Out of scope for Stage A

To keep this stage tight:

- **No bot modules consume the frontline.** That's Stage B.
- **No persistence to disk.** Influence map is runtime-only.
- **No frontline "smoothing" or band-tracking over time.** A simple
  per-tick recompute is fine; smoothing comes later if the visual is
  jittery in practice.
- **No multi-frontline detection.** If there are two contested zones
  (e.g. multi-player FFA), the overlay just shows both; no separation
  of axes. Stage B-or-later may need it.

## Risks

- **Perf.** Recomputing a per-cell map every 25 ticks could be slow
  on big maps. Mitigation: start with 2-cell blocks (4× cheaper); add
  a budget check; if needed step down to 4-cell.
- **Overlay clutter.** If the band covers half the map in a heavy
  endgame, the visual loses meaning. Mitigation: cap colour intensity
  by min(friendly, enemy) — wide blowout zones fade, tight
  engagements stay bright.
- **Hotkey conflict.** F11 might already be bound. Check the
  hotkey registry; pick the first free key.
