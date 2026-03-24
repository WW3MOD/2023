# Hotboard

> Live dashboard. Max 40 lines. Oldest items rotate out.

## Active Concerns
- **Helicopter emergency landing needs playtesting** — autorotation now has steering, acceleration ramp, and flare before touchdown
- **Stance system rework complete** — All 4 phases done, needs playtesting
- **AI overhaul needs playtesting** — Tiers 0-3.1 implemented, major new capabilities
- **Supply Route contestation needs playtesting** — bar depletion/recovery, production slowdown
- Three-mode move system needs playtesting (OverkillThreshold, UnderFireDuration tuning)
- Vehicle crew system needs playtesting (ejection delays, commander substitution)
- Tank frontal armor vs tank guns = near-stalemate (sim shows pen 50 vs 700 thickness = 7% dmg)
- **Capture bug partially fixed** — frozen actor staleness addressed, monitor next playtest

## Recent Wins
- **Damage state threshold fix** — Heavy/Medium were unreachable (math bug), now configurable per-actor. Health bar gradient: green→yellow→orange→red
- **Helicopter emergency landing system** — Two-tier: heavy=autorotation (steering+acceleration+flare, safe landing, repairable), critical=crash (spinning, destroyed). Crew survives ground landing only
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
