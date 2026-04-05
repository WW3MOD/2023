# Session: Garrison System Overhaul
**Started:** 2026-04-05
**Status:** in-progress
**Plan:** `CLAUDE/plans/graceful-mapping-locket.md` (also at `.claude/plans/`)

## Task Summary
Comprehensive garrison system overhaul covering:
1. Building indestructibility (IDamageModifier, 1 HP min)
2. Ownership model (claim on enter, neutral on empty, allies share)
3. Directional port targetability (reverse arc check)
4. Building targeting priority
5. Pip placement fix (center bottom)
6. Protection % text overlay
7. Sidebar panel — full icon rewrite
8. Panel force-fire and exit-move orders
9. Port suppression integration (duck + recall + lockout)
10. Suppression display in panel

## Intended Files
- `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs`
- `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonProtection.cs`
- `engine/OpenRA.Mods.Common/Traits/Render/WithGarrisonDecoration.cs`
- `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/GarrisonPanelLogic.cs`
- `engine/OpenRA.Mods.Common/Traits/Attack/AttackGarrisoned.cs`
- `engine/OpenRA.Mods.Common/Traits/Targetable.cs` (or new directional targetable)
- `mods/ww3mod/chrome/garrison-panel.yaml`
- `mods/ww3mod/chrome/ingame-player.yaml`
- `mods/ww3mod/rules/ingame/structures-defenses.yaml`
- `mods/ww3mod/rules/ingame/civilian.yaml`
