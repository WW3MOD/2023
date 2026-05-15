# Lobby implementation — active session

Started: 2026-05-15 17:30
Status: in-progress
Plan: [`WORKSPACE/lobby/IMPLEMENTATION_PLAN.md`](../../lobby/IMPLEMENTATION_PLAN.md)

## Intended files (claim list — heads-up to parallel work)

YAML chrome:
- `engine/mods/common/chrome/lobby.yaml`
- `engine/mods/common/chrome/lobby-mappreview.yaml`
- `engine/mods/common/chrome/lobby-players.yaml`
- `engine/mods/common/chrome/lobby-music.yaml`
- `mods/ww3mod/chrome/lobby-options.yaml`
- `mods/ww3mod/chrome/_lobby-palette.yaml` (new — Phase 0)

C# logic:
- `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyLogic.cs`
- `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyOptionsLogic.cs`
- `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/MapPreviewLogic.cs`
- `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyUtils.cs`

Possibly:
- `mods/ww3mod/chrome.yaml` (corner-bracket sprite registration, Phase 0)
- `mods/ww3mod/uibits/corner-bracket.png` (new sprite, optional)

## Phase progress

- [ ] 0 — Palette doc + bevel convention
- [ ] 1 — Top bar + CTA bar
- [ ] 2 — Settings hero grid
- [ ] 3 — Map panel + brackets
- [ ] 4 — Left column scroll
- [ ] 5 — Players V5 rows
- [ ] 6 — Inline map browse
- [ ] 7 — Chat restyle
- [ ] 8 — Polish

## Notes

Will commit each phase as a separate commit. Build between phases to
catch breakage early.
