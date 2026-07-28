# Backlog

> Deferred tasks, ideas, and parking lot items.
> `[ ]` pending | `[x]` done | `[dropped]` irrelevant
> v1 items live in `RELEASE_V1.md` — this file is for v1.1+ and parking-lot ideas only.

## Deferred Tasks
- [x] **Lobby Phase 6 — inline map browse** — shipped 260515 as phase 12 (`MAPCHOOSER_INLINE`, stock chooser re-parented inline) + finishing pass 260718 (focus handoff, refresh-on-open, host-change flip-back, panel chrome, narrow-width fits).
- [ ] **Lobby designed map browser** — the *locked* design (decisions.md 7b: search bar on top, filter chips All·2p·4p·6+·Conquest·Scenarios, CURRENT badge + accent outline on current map, single-click select flips back to preview) was never built; what ships is the restyled stock chooser (category dropdown, OK/Cancel, title-order sort). Functional, visibly not the mockup. *v1.1*
- [ ] **Lobby text ink decision** — palette says primary text = ink `#d4d4d4`, but nearly every label inherits pure white from `metrics.yaml` defaults (`TextColor`/`ButtonTextColor`). One-line global override in `mods/ww3mod/metrics.yaml` would fix the whole lobby but also recolors the in-game HUD — needs a user call + visual pass. *decision*
- [ ] **Lobby chat polish not shipped (deliberate cuts, recorded 260718)** — 3-way chat filter (All/Allies/Spectators), SYSTEM bordered-chip message styling, mono timestamp font (no mono face registered). Binary All/Team toggle + gray system lines ship instead. *v1.1*
- [ ] **Lobby dead chrome sweep** — `SKIRMISH_TABS`/`MULTIPLAYER_TABS` (~100 lines, force-hidden, duplicate child IDs) and the unreachable Servers panel (`PanelType.Servers` never assigned; `LOBBY_SERVERS_BIN` loads in MP but can never show). Inert; delete when the MP lobby gets its pass. *v1.1*
- [ ] **SkirmishLogic vs test map seed (engine-side nicety)** — `SkirmishLogic.ClientJoined` restores `skirmish.<mod>.yaml` and re-orders the map, overriding `Test.LaunchLobbyMap`; the wrapper works around it by backing the file up. Cleaner: skip the restore when `TestMode.IsActive` with a seeded map. *tooling, v1.1*
- [ ] **Dynamic players/chat quadrant split** — locked decision 6 (players sized to slot count, chat soaks the remainder) still unimplemented; round-2's action-row clamp removes the dead-space sting, so this is now pure polish. *v1.1*
- [ ] **Team/Handicap access path** — per-player dropdowns parked off-screen; only host Auto-Team assigns teams, so a non-host can never enable their own Team chat in MP. Restore an affordance (context menu?) or hide the chat mode pill in MP too. *v1.1*
- [ ] **Lobby handicap access path** — V5 row dropped Handicap; column parked at X:-200 W:1 H:1 in lobby-players.yaml. Decide path (context-menu / expandable detail / spawn-cell overload / drop entirely) once usage telemetry exists. See `DISCOVERIES.md` 2026-05-18. *v1.1*
- [ ] **Lobby 1366×768 bottom-strip overflow** — at 1366 wide the setup-row buttons overflow into SPECTATE_AREA by ~80px. Phase 11 tightened widths to give 1920×1080 headroom; 1366 still cramped. Fix only if a player reports it. *v1.1*
- [x] **Flashing pips honour real-time, not game-speed** — shipped 260728 (merge `184b40bf`): `WithDecorationBase` blink phase now derives from `Game.RunTime` anchored to `Ui.Timestep` (render-only, pure helper `DecorationBlink.PhaseIndex`, 6 NUnit pins). Fixes all `WithDecoration` blinks incl. out-of-ammo pip and low-fuel/damage vehicle blink. `WithHealFlash` is a different (event-driven) mechanism, untouched. *Reported 260503*
- [ ] Per-Supply-Route production queues (requires engine changes)
- [ ] Per-unit rot sprites (bleedout uses generic e1 frames)
- [ ] Group Scatter polish (mixed unit types, UI feedback)
- [ ] Cherry-pick useful parts from skane/xavi branches
- [ ] Extract useful maps from maps branch
- [ ] Clean up stale branches (bypass, counterbattery, speed)

## Ideas Parking Lot

### Sprite/asset generation — deferred for hands-on work with the user (2026-07-28)
Source: `WORKSPACE/recon/260728-sprite-tooling.md`. The tooling FOUNDATION went to PIPELINE item 29 (wrapper scripts, loose-file verify, PngSheet enable); everything below is the *creative* use of that pipeline — the user wants a hands-on approach, so do NOT start these autonomously.
- [ ] **Graded tree damage art (image-gen pilot)** — engine already supports 4 damage tiers (`scuffed-`/`scratched-`/`damaged-`/`critical-`, auto-selected at 75/50/25% HP); trees have art for none. Pilot: export one tree, generate 4 damage variants, quantize to `temperat.pal`, wire sequences, screenshot eval. The proof-of-concept for the whole gen-assisted content strategy. *hands-on*
- [ ] **Graded building damage art** — buildings ship 2 visual states (bare + `damaged-`); `scuffed-`/`scratched-`/`critical-` sequences are 0-2 uses across the mod. Same recipe as trees, per-building art volume. *hands-on*
- [ ] **Multi-part buildings (independently destructible wings)** — shipped-tools route: N co-located actors + condition graph (no C#); elegant route: new per-segment health trait. Bridge precedent is tile-based — wrong fit. Needs a design session first. *hands-on, design-first*
- [ ] **Integrated image-gen step** — agent-driven "take this tree, generate it 20/40/60/80% damaged" automation (API integration, quantization step `-remap temperat.pal`). Only worth building after the manual pilot proves the art direction. *post-pilot*

### Engine modernization — deferred pending user judgment (2026-07-28)
Source: `WORKSPACE/recon/260728-trees-concealment.md` + `recon/260728-movement-locomotion.md` (engine-modernization study). The safe subset went to PIPELINE items 26–28; everything below changes game feel/balance in ways the user must weigh — do NOT implement autonomously.
- [ ] **Tanks crush trees** — today forests are absolute vehicle barriers (no locomotor Crushes/Passes tree). MBTs flattening small trees is realistic (husk + destruction plumbing exists) but removes forests as vehicle-proof terrain. *user call*
- [ ] **Forest movement speed penalty** — trees currently slow no one (infantry cross at underlying-tile speed). Realistic, one YAML table, but slows pace in exactly the terrain case-01 wants used. *user call*
- [ ] **Per-weapon clear-sight thresholds** (`FiringLOS`) — snipers need clean LOS, MGs spray through light foliage. Deepens the foliage game; new per-weapon tuning surface. *user call*
- [ ] **Vehicle `CanRedirectMidCell`** — would kill the standstill stop-turn, but the code path was written for cell-sharing infantry; verify for FullCell vehicles before any trial. *verify-first*
- [ ] **`BlocksSight` on dense forest cores** — dormant trait, binary "cannot see past"; strong ambush enabler but binary walls may feel gamey next to the graded shadow model. *user call*
- [ ] **Dynamic battlefield foliage** — fire/artillery deforestation opening sightlines over a battle. Gate question ANSWERED (recon 260728, `WORKSPACE/recon/260728-shadowlayer-tree-death.md`): layers are **BAKED** — `DensityLayer`/`ShadowLayer` freeze at map load; the tree-death density decrement was disabled 260503 for lag, so dead trees keep full concealment + item-26 damage cover forever. Cheap seam: re-enable the `RemovedFromWorld` density decrement (damage-cover + concealment go live, no shadow recompute). Expensive seam: vision needs a local-window ShadowLayer rebuild. ⚠ density feeds an `IDamageModifier` → baseline divergence; re-baseline before enabling. *user call, unblocked*
- [ ] **Helicopter spotting asymmetry lean-in** — airborne sightlines already attenuate less through trees; tuning the gap makes helis the doctrinal counter to forest ambushes. *user call*
- [ ] **Movement rungs (c)/(d)** — any-angle multi-cell segments / true off-grid occupancy or theta\*. Highest payoff, highest risk (breaks PopPath/blocking assumptions, determinism surface). Only if rung (b) proves appetite. *post-(b)*
- [ ] **Deploy-to-prone → stance-governed (user idea, 2026-07-21)** — **RESEARCH DONE 2026-07-28**: `WORKSPACE/recon/260728-deploy-prone.md` (@ `c85ac3b0`). Findings: deploy lives on `^CamoSoldier` (not ^Soldier), grants exactly `deployed` — one of four OR-clauses into `prone`; stationary units already prone via `!moving`, so deploy's ONLY unique effect is **crawl** (prone while moving). No AI ever deploys. Prone payload: −40% speed, damage reduction, smaller hitshape, **+1 concealment tier** (architecture.md corrected @ `eaf89c8a`; ambush design doc §3.1 still carries the old error — curation flag). Phase-3 opt-out: no new conflict (Ambush already opts out via stance gate). **Recommended shape C-lite**: keep `deployed`/`prone` tokens as low-level primitives (preserves engineer auto-prone), remove the manual deploy button, drive prone-while-moving from the Ambush fire-stance as a rider on the ambush-widening work. **Awaiting user call on shape — do not implement.** *user call, research complete*
- [ ] Engine upgrade to release-20250330 (12-22 sessions, defer until gameplay done)
- [ ] Ukraine as third faction
- [ ] Ammo costs money — full economy rework (separate from current SupplyValue tier work)

## Completed
