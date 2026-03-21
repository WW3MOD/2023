# Hotboard

> Live dashboard. Max 40 lines. Oldest items rotate out.

## Active Concerns
- **AI overhaul needs playtesting** — Tiers 0-3.1 implemented, major new capabilities:
  - ScoutBotModule: humvee/BTR scouts explore map, post enemy intel
  - Multi-axis attacks: large armies split into 2 squads hitting different targets
  - GarrisonBotModule: AI garrisons defense structures with infantry
  - SupplyFollowerBotModule: trucks follow army clusters for field resupply
  - AdaptiveProductionBotModule: builds counter-units based on enemy composition
  - Smart retreat + regroup, ammo awareness, 3 personalities (Normal/Rush/Turtle)
- **Control bar overhaul needs playtesting** — 4 stance bars, click-modifier system
- **Supply Route contestation needs playtesting** — bar depletion/recovery, production slowdown
- Three-mode move system needs playtesting (OverkillThreshold, UnderFireDuration tuning)
- Infantry mid-cell redirect needs playtesting (RedirectSpeedPenalty tuning)
- Vehicle crew system needs playtesting (ejection delays, commander substitution)
- Supply truck needs playtesting (range, delays, supply costs)
- Garrison shelter/port system needs playtesting
- Tank frontal armor vs tank guns = near-stalemate (sim shows pen 50 vs 700 thickness = 7% dmg)

## Recent Wins
- **AI overhaul Tiers 0-3.1** — 5 new C# bot modules, multi-axis attacks, adaptive production
- **Control bar overhaul** — 4 stance bars + click-modifier meta-system + UnitDefaultsManager
- **Supply Route contestation system** — graduated control bar replaces binary contest

## Quick Stats
- Engine files modified: 278+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
- New AI modules: 5 (Scout, Garrison, SupplyFollower, AdaptiveProduction, ThreatMap)
- Next AI tier: Tier 3.2+ (economy scoring, personality tactics, map analysis)
