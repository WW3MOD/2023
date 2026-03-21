# Hotboard

> Live dashboard. Max 40 lines. Oldest items rotate out.

## Active Concerns
- **Control bar overhaul needs playtesting** — 4 stance bars, click-modifier system, dummy Patrol/Evacuate
- **Supply Route contestation needs playtesting** — bar depletion/recovery, production slowdown, notifications
- Bar toggle buttons not yet wired in chrome YAML (BarToggleLogic exists, needs toggle widgets)
- Combat sim Phase 2: MiniYaml loader (auto-read unit stats from YAML)
- Three-mode move system needs playtesting (OverkillThreshold, UnderFireDuration tuning)
- Infantry mid-cell redirect needs playtesting (RedirectSpeedPenalty tuning)
- Vehicle crew system needs playtesting (ejection delays, commander substitution)
- Supply truck needs playtesting (range, delays, supply costs)
- Garrison shelter/port system needs playtesting
- Tank frontal armor vs tank guns = near-stalemate (sim shows pen 50 vs 700 thickness = 7% dmg)

## Recent Wins
- **Control bar overhaul** — 4 stance bars + click-modifier meta-system + UnitDefaultsManager + dummy commands
- **Supply Route contestation system** — graduated control bar replaces binary contest
- Combat balance simulator Phase 1 complete
- Three-mode move system (Move/Attack-Move/Force-Move)

## Quick Stats
- Engine files modified: 268+
- Maps: 13
- Last commit: Control bar overhaul (tooltips, modifiers, defaults)
