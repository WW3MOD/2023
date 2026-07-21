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
- [ ] **Flashing pips honour real-time, not game-speed** — out-of-ammo pip blinks on game ticks; on fast-forward they strobe, on slow they crawl. Drive flash from wall-clock instead. *Reported 260503*
- [ ] Per-Supply-Route production queues (requires engine changes)
- [ ] Per-unit rot sprites (bleedout uses generic e1 frames)
- [ ] Group Scatter polish (mixed unit types, UI feedback)
- [ ] Cherry-pick useful parts from skane/xavi branches
- [ ] Extract useful maps from maps branch
- [ ] Clean up stale branches (bypass, counterbattery, speed)

## Ideas Parking Lot
- [ ] **Deploy-to-prone → stance-governed (user idea, 2026-07-21)** — every soldier has "deploy" = forced prone (`GrantConditionOnDeploy`, `infantry.yaml` ^Soldier). Legacy feature from years back, barely used; forced-prone is a *behavior* and should be governed by the stance system (possibly related to the discussed Ambush stance) or an existing/new trait — not a manual per-unit deploy toggle. **Shape deliberately unresolved: research + verify first** — map what deploy-prone actually grants today (conditions, speed/prone modifiers, AI usage if any), then propose where the control should live (stance vs trait) before any implementation. ⚠ Interaction: Phase 3 tactical positioning treats `deployed` as a positioning/auto-target opt-out signal — changing deploy semantics must revisit that opt-out. *research-first, post-Phase-3*
- [ ] Engine upgrade to release-20250330 (12-22 sessions, defer until gameplay done)
- [ ] Ukraine as third faction
- [ ] Ammo costs money — full economy rework (separate from current SupplyValue tier work)

## Completed
