# Stage B — Defensive layer placement

> Builds on Stage A's InfluenceMap + frontline. The v2 bot now *uses*
> that perception to position units in the doctrine's three-layer
> structure. This is where the doctrine starts becoming visible in
> matches.

## What "done" looks like

Load a match against v2 AI on a map with neutrals (e.g. capture-arena).
Within ~90 sim-sec you should be able to see:

- **A thin screen of light infantry** spread along the frontline. Sparse
  spacing (1–2 infantry per cluster). Some in treelines, some in
  garrisonable buildings. Not packed densely — bombardment-resilient
  by design.
- **A main line of vehicles + the rest of the infantry** at standoff
  distance behind the screen. Tanks/IFVs forward in firing position,
  artillery further back, AA mixed in.
- **Reserve units** (heli, transport, mobile fast units) hanging
  back near the SR — that's Stage C, not part of B's acceptance.

Toggle `/frontline` and the contested band should sit right at the
screen's forward edge, with the main line just behind it.

## Phasing (B.1 → B.3)

### B.1 — Basic layer placement (this slice)

Single new bot module reads `InfluenceMap.GetFrontline(perspective)` and
dispatches idle units to one of two positions:

- **Screen** units (light infantry: `e3`, `ar`, `at`, `sn`, `tl`,
  `e2`, `e4`, `medi` and faction variants): nearest frontline cell.
  No cover yet — just "stand at the contested edge".
- **Main line** units (everything else combat-capable that's idle):
  frontline cell shifted by `MainLineStandoffCells` toward the bot's
  own SR. Tanks, IFVs, AA, ATGM teams, artillery.

Excluded from auto-placement:
- `tecn` (capture coordinator owns these)
- `e6` (engineers — repair/mine specialist, no combat role here)
- `truk` (supply follower owns)
- Aircraft (helis + fixed-wing have their own squad managers)

If the frontline is empty (no enemy contact yet), the module does
nothing — units fall back to whatever the existing
`SquadManagerBotModule` was doing. This avoids breaking opening play.

Cooldown: each unit gets a fresh order at most once per
`AssignCooldownTicks` (default 250 ≈ 10 sim-sec). Prevents the module
from spamming move orders every tick onto a unit that's already
moving.

**Verifies:** demo or tournament match, watch units stream toward
the frontline in two waves — light infantry forward, heavies behind.

### B.2 — Treeline / cover preference

Same module, smarter screen positioning:

- For each screen-eligible unit, after picking the rough screen cell
  from B.1, **snap to the nearest cover cell within K cells** (treeline
  terrain types, garrisonable buildings).
- Cover types pulled from terrain info (`TerrainTypeInfo` per cell).
- Garrisonable buildings: detect via `Garrisonable` trait on the actor
  at that cell. Trigger garrison-enter order instead of move.

**Verifies:** match with treelines and a capturable building near the
frontline — infantry should go into the treeline / building, not stand
in the open at the contested cells.

### B.3 — Fields-of-fire coverage check

Main-line positioning becomes smarter:

- Each main-line slot is required to be in range of at least N other
  main-line slots (overlapping fields of fire).
- Slot pool: standoff positions evenly spaced along the frontline,
  filtered by terrain (vehicles can't sit in trees).
- Module fills slots greedily but never leaves a screen cell uncovered.

**Verifies:** main line forms with no gaps; if you push the screen at
a specific sector, no single tank is left isolated.

## Out of scope for Stage B

- **Reserve management.** That's Stage C — different module reads
  pressure deltas off the InfluenceMap.
- **Coordinated retreat.** Surviving screen units pulling back through
  the main line — Stage C/D depending on complexity.
- **Personality differentiation.** Stage E — Rush/Normal/Turtle change
  YAML weights on the same module.
- **3:1 offensive concentration.** Stage D — separate module reading
  enemy-sector weakness.

## TODOs for B.1 (this slice)

### TODO B.1.1 — Module skeleton + idle-unit dispatch

- [ ] `engine/OpenRA.Mods.Common/Traits/BotModules/LayeredDefenceBotModule.cs`.
- [ ] `IBotTick` every 75 ticks (3 sim-sec, slower than the capture
      module so we don't fight for compute).
- [ ] Reads `World.WorldActor.Trait<InfluenceMap>()`. Bail early if no
      contested cells — module is dormant before contact.
- [ ] Per-unit `assignedAtTick` dictionary; skip units assigned in the
      last `AssignCooldownTicks`.

### TODO B.1.2 — Layer classification + position picking

- [ ] YAML: `ScreenUnitTypes` (light infantry), `MainLineUnitTypes`
      (heavy infantry + vehicles + arty), `MainLineStandoffCells` (int,
      default 6).
- [ ] For each idle screen-eligible unit, find the nearest contested
      cell — assign that position.
- [ ] For each idle main-line-eligible unit, find the nearest contested
      cell, shift by `MainLineStandoffCells` along the vector from the
      cell toward the bot's own SR.
- [ ] Issue `AttackMove` order for both. Record assignment tick.

### TODO B.1.3 — Wire under `enable-ai-v2`

- [ ] Add `LayeredDefenceBotModule@v2` to `mods/ww3mod/rules/ai/ai.yaml`
      under `enable-ai-v2`.
- [ ] Match the existing convention (defines, faction variants if
      needed — initial version is faction-agnostic).

### TODO B.1.4 — Verify visually

- [ ] Build, smoke-test (any existing autotest still passes).
- [ ] Either reuse `tournament-capture-arena-2p` (v2 already plays
      USA-bot) or stand up a tiny `demo-layered-defence` scenario.
- [ ] Launch as a demo, toggle `/frontline`, watch the layers form
      over the first ~90 sim-sec.

### TODO B.1.5 — Acceptance + commit

- [ ] User confirms screen-vs-main-line distinction is visible.
- [ ] Update HOTBOARD + this doc with status.
- [ ] Tournament batch (optional) on capture-arena to see if winrate
      moves. Stage B may also REDUCE winrate at first if positioning
      pulls units out of effective firing positions — that's OK; B.2/B.3
      add cover and field-of-fire which should recover and exceed.

## Risks / things to watch

- **Pulling units away from the army.** If the module yanks a Bradley
  to a standoff position that's WORSE than where its squad was, v2
  could end up weaker. Mitigation: only act on truly-idle units;
  existing SquadManagerBotModule still owns engagement.
- **Thrashing.** Units that complete a defensive AttackMove become idle
  again and get reassigned to the same position. Mitigation: the
  cooldown bounds reassignment frequency.
- **Empty frontline.** Module must do nothing — opening play hands off
  to existing logic.
- **Symmetric maps with no clear "behind".** If the SR is on the same
  axis as the frontline, the standoff vector degenerates. Mitigation:
  if the SR is within 2 cells of the frontline cell, skip standoff
  shift and just place at the frontline.
