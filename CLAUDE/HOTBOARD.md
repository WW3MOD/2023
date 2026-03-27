# Hotboard

> Live dashboard. Max 40 lines. Oldest items rotate out.

## Active Concerns
- **Unified Transport & Supply System** — Phases 1, 2A-E done. Next: Phase 3 (template sidebar). Plan: `CLAUDE/plans/purrfect-munching-balloon.md`
- **Helicopter crash + crew overhaul needs playtesting** — VehicleCrew on all helis, critical crash kills all, safe landing evacuates crew+passengers to neutral, capture-by-pilot-entry
- **Stance system rework complete** — All 4 phases done, needs playtesting
- **AI overhaul needs playtesting** — Tiers 0-3.1 implemented, major new capabilities
- **Supply Route contestation needs playtesting** — bar depletion/recovery, production slowdown
- Three-mode move system needs playtesting (OverkillThreshold, UnderFireDuration tuning)
- Vehicle crew system needs playtesting (ejection delays, commander substitution)
- Tank frontal armor vs tank guns = near-stalemate (sim shows pen 50 vs 700 thickness = 7% dmg)
- **Upstream merge in progress** — BACKUP/WW3MOD branch `upstream-merge-2025`. 1226 safe files + 294 manual merge. Plan: `CLAUDE/plans/260324_upstream_merge.md`

## Recent Wins
- **Cargo system Phases 2A-E** — CargoSupply, TRUK→pure transport, cargo panel (eject/mark/rally), supply→SUPPLYCACHE merge, waypoint selective unload, pre-queued ejection rally points
- **SUPPLYCACHE Phase 1** — NoAutoTarget (manual attack only) + ProximityCapturable (1.5 cell range, sticky ownership transfer)
- **Damage state threshold fix** — Heavy/Medium were unreachable (math bug), now configurable per-actor. Health bar gradient: green→yellow→orange→red
- **Helicopter crash + crew overhaul** — VehicleCrew on all helis (Pilot+Gunner or Pilot+Copilot). Critical crash = total loss. Safe landing = crew evacuates, heli goes neutral, capturable by any pilot entering. Anyone can repair neutral helis. RepairableBuilding.ValidRelationships field added
- **Stance rework Phases 1-4 complete** — Modifiers, enums, tooltips, resupply behavior, cohesion distribution, patrol system
- **AI overhaul Tiers 0-3.1** — 5 new C# bot modules, multi-axis attacks, adaptive production
- **Supply Route contestation system** — graduated control bar replaces binary contest

## Stance Rework Progress
- [x] Phase 1: Modifier system, enum renames, defaults, tooltips
- [x] Phase 2: Resupply behavior (needs-resupply flag, auto-seek, evacuate-on-empty, truck hunt)
- [x] Phase 3: Cohesion behavior (waypoint distribution on group moves)
- [x] Phase 4: Patrol system (waypoint loop with attack-move between points)

## Quick Stats
- Engine files modified: 278+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
