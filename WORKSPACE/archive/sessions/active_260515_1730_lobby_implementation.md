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

- [x] 0 — Palette doc + bevel convention (d46fcfaf)
- [x] 1 — Top bar + CTA bar (7fe8744d)
- [x] 2 — Settings hero grid (a0633ecf + ac077dc2 reroute)
- [x] 3 — Map panel + brackets (b2f4997c)
- [x] 4 — Layout swap — players to right, settings to bottom-left, chat to bottom-right (04baa003)
- [x] 5 — Players V5 rows (e4a14d1d, 63d9629c team/handicap kill)
- [ ] 6 — Inline map browse — **DEFERRED.** Existing Change Map button works.
- [x] 7 — Chat restyle / notification palette (51350dd1)
- [x] 8 — Polish: O3 flat (653e10b3), Start Game green chrome (c6503e95),
       opaque panel bg (cb9041c1), chat panel chrome (d17def8e)

## Notes

Will commit each phase as a separate commit. Build between phases to
catch breakage early.
