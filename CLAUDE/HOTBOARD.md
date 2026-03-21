# Hotboard

> Live dashboard. Max 40 lines. Oldest items rotate out.

## Active Concerns
- **AI overhaul needs playtesting** — Tier 0+1+2.1 implemented, test all 3 bot types vs player
  - Verify Normal/Rush/Turtle AI appear in lobby dropdown
  - Verify AI builds logistics centers, helipads, defenses
  - Verify AI squads retreat and regroup instead of dying
  - Verify AI doesn't attack buildings being captured
  - Verify supply trucks excluded from attack squads
- **Control bar overhaul needs playtesting** — 4 stance bars, click-modifier system
- **Supply Route contestation needs playtesting** — bar depletion/recovery, production slowdown
- Three-mode move system needs playtesting (OverkillThreshold, UnderFireDuration tuning)
- Infantry mid-cell redirect needs playtesting (RedirectSpeedPenalty tuning)
- Vehicle crew system needs playtesting (ejection delays, commander substitution)
- Supply truck needs playtesting (range, delays, supply costs)
- Garrison shelter/port system needs playtesting
- Tank frontal armor vs tank guns = near-stalemate (sim shows pen 50 vs 700 thickness = 7% dmg)

## Recent Wins
- **AI overhaul Tier 0-2.1** — logistics building, 3 personalities, ThreatMapManager, BotBlackboard, smart retreat+regroup
- **Control bar overhaul** — 4 stance bars + click-modifier meta-system + UnitDefaultsManager
- **Supply Route contestation system** — graduated control bar replaces binary contest
- Combat balance simulator Phase 1 complete
- Three-mode move system (Move/Attack-Move/Force-Move)

## Quick Stats
- Engine files modified: 272+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
- Next AI tier: Tier 2.2 (multi-axis attacks), Tier 1.3 (scouting module)
