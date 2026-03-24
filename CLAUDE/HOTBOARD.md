# Hotboard

> Live dashboard. Max 40 lines. Oldest items rotate out.

## Active Concerns
- **Helicopter emergency landing needs playtesting** — autorotation descent rate, crash spin, crew ejection, safe landing repair flow
- **Stance system rework in progress** — Phase 1+2 done. Phase 3 (Cohesion) + Phase 4 (Patrol) next
- **AI overhaul needs playtesting** — Tiers 0-3.1 implemented, major new capabilities
- **Supply Route contestation needs playtesting** — bar depletion/recovery, production slowdown
- Three-mode move system needs playtesting (OverkillThreshold, UnderFireDuration tuning)
- Vehicle crew system needs playtesting (ejection delays, commander substitution)
- Tank frontal armor vs tank guns = near-stalemate (sim shows pen 50 vs 700 thickness = 7% dmg)
- **Capture bug partially fixed** — frozen actor staleness addressed, monitor next playtest

## Recent Wins
- **Helicopter emergency landing system** — Two-tier: heavy=autorotation (steerable, safe landing, repairable), critical=crash (spinning, destroyed). Crew survives ground landing only
- **Stance Phase 1+2 complete** — Modifier rework, enum renames, tooltips above buttons, resupply behavior wired (Auto/Hold/Evacuate in AmmoPool), NeedsResupply flag, SupplyProvider Hunt integration
- **AI overhaul Tiers 0-3.1** — 5 new C# bot modules, multi-axis attacks, adaptive production
- **Supply Route contestation system** — graduated control bar replaces binary contest

## Stance Rework Progress
- [x] Phase 1: Modifier system, enum renames, defaults, tooltips
- [x] Phase 2: Resupply behavior (needs-resupply flag, auto-seek, evacuate-on-empty, truck hunt)
- [ ] Phase 3: Cohesion behavior (waypoint distribution on group moves)
- [ ] Phase 4: Patrol system (waypoint loop with attack-move between points)

## Quick Stats
- Engine files modified: 278+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
