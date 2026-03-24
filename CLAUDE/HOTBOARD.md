# Hotboard

> Live dashboard. Max 40 lines. Oldest items rotate out.

## Active Concerns
- **Stance system rework in progress** — Phase 1 done (modifiers, enums, defaults, tooltips). Phase 2-4 next
- **AI overhaul needs playtesting** — Tiers 0-3.1 implemented, major new capabilities
- **Supply Route contestation needs playtesting** — bar depletion/recovery, production slowdown
- Three-mode move system needs playtesting (OverkillThreshold, UnderFireDuration tuning)
- Infantry mid-cell redirect needs playtesting (RedirectSpeedPenalty tuning)
- Vehicle crew system needs playtesting (ejection delays, commander substitution)
- Garrison shelter/port system needs playtesting
- Tank frontal armor vs tank guns = near-stalemate (sim shows pen 50 vs 700 thickness = 7% dmg)
- **Capture bug partially fixed** — frozen actor staleness addressed, monitor next playtest

## Recent Wins
- **Stance Phase 1 complete** — Modifier rework (Ctrl+Alt=type default, Alt=Do Now), ResupplyBehavior renamed (Auto/Evacuate), Evacuate button removed, medic/engineer default to Hunt, tooltips anchored above buttons
- **5 playtest bugs fixed** — AT overkill skip, supply truck resupply, evacuate button, attack-move cursor, capture staleness
- **AI overhaul Tiers 0-3.1** — 5 new C# bot modules, multi-axis attacks, adaptive production
- **Supply Route contestation system** — graduated control bar replaces binary contest

## Stance Rework Progress
- [x] Phase 1: Modifier system, enum renames, defaults, tooltips
- [ ] Phase 2: Resupply behavior (needs-resupply flag, auto-seek, evacuate-on-empty, truck hunt)
- [ ] Phase 3: Cohesion behavior (waypoint distribution on group moves)
- [ ] Phase 4: Patrol system (waypoint loop with attack-move between points)

## Quick Stats
- Engine files modified: 278+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
