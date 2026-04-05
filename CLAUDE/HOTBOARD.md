# Hotboard

> Live dashboard. Max 40 lines. Oldest items rotate out.

## Active Concerns
- **Garrison system overhaul in progress** — Phases 1-6 done (indestructible, ownership, directional targeting, suppression, visuals). Phase 4 (sidebar panel icon rewrite) pending. Plan: `.claude/plans/graceful-mapping-locket.md`
- **Unified Transport & Supply System** — Phases 1, 2A-E done. Next: Phase 3 (template sidebar). Plan: `CLAUDE/plans/purrfect-munching-balloon.md`
- **Helicopter crash + crew overhaul needs playtesting**
- **Stance system rework complete** — All 4 phases done, needs playtesting
- **AI overhaul needs playtesting** — Tiers 0-3.1 implemented
- **Supply Route contestation needs playtesting**
- Three-mode move system needs playtesting
- Vehicle crew system needs playtesting
- Tank frontal armor vs tank guns = near-stalemate (sim shows pen 50 vs 700 thickness = 7% dmg)
- **Upstream merge in progress** — BACKUP/WW3MOD branch `upstream-merge-2025`

## Recent Wins
- **Garrison overhaul Phases 1-6** — Indestructible buildings (IDamageModifier 1HP min), dynamic ownership (enter→claim, empty→neutral, allies share), directional port targetability (reverse arc check), suppression integration (duck/recall/lockout), protection % text overlay, pips centered at bottom, force-fire-only building targeting when garrisoned
- **Cargo system Phases 2A-E** — CargoSupply, TRUK→pure transport, cargo panel
- **SUPPLYCACHE Phase 1** — NoAutoTarget + ProximityCapturable
- **Helicopter crash + crew overhaul** — VehicleCrew on all helis, critical crash = total loss
- **Stance rework Phases 1-4** — Modifiers, enums, tooltips, resupply, cohesion, patrol
- **AI overhaul Tiers 0-3.1** — 5 new C# bot modules, multi-axis attacks

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
