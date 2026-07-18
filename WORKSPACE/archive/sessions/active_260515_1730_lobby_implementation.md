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
- [x] 6 — Inline map browse — deferred at first, then shipped as phase 12 (a69c05fa) using the stock chooser inline; the *designed* browser (chips/CURRENT badge) stays in BACKLOG.
- [x] 7 — Chat restyle / notification palette (51350dd1)
- [x] 8 — Polish: O3 flat (653e10b3), Start Game green chrome (c6503e95),
       opaque panel bg (cb9041c1), chat panel chrome (d17def8e)
- [x] 9 — Full-width rows (58194ec8)
- [x] 10 — Squared cells, two-square dropdowns, flat action buttons, Spectate moved (f95785f9 + 6f794c16 NRE fix)
- [x] 11 — Row hover bg, remaining row chrome flattened, cross-resolution tighten, handicap deferral note
- [x] 12 — Inline map browser + Music tab over chat (a69c05fa), then 6-fix batch (0100022f) + close-toggle X (06b70a94)

## Finishing pass — 2026-07-18

Five parallel audit agents cross-checked chrome vs C# vs plan vs mockups;
six parallel fix agents shipped the findings (commits 7e43d5a3, edf23701,
b91b7d08, 36f82133, 41f6f891, 869f3221 + follow-up cleanup):

- **Layout:** BL settings quadrant no longer collapses at ≤1080p (proportional
  options/changes split replaces `PARENT_HEIGHT - 240`); column bottoms aligned
  at PH-80; TL/TR seam at S+108; Chat/Music toggle centered 8/8; Start Game
  centered 6/6 in the CTA bar; preset bar fits 1366.
- **Engine:** `ImageWidget.ScaleToBounds` (flags actually fill their cell now);
  `lobby-button-highlighted*` chrome variants (active tabs had NO fill);
  keyboard-focus handoff on map-browser tab switches (hidden filter field ate
  keystrokes — Enter could silently change the map).
- **Behavior:** map browser rebuilds on open (stale-list fix), closes on OK
  even when re-picking the current map, flips back when the host changes map;
  status dot/label + Start Game chrome now track real readiness; unread CHAT (n)
  badge while Music tab open; active-changes chips cap to measured height with
  `+N more`, filter hidden/scenario options, share one option-id list with
  LobbyOptionsLogic (drift PITFALL resolved); EMPTY_HINT_ACCENT no longer
  destroyed by the chip rebuild keep-list.
- **Rows:** name/profile overlap fixed; ready ticks aligned across all five row
  templates; lobby-checkbox chrome on ready boxes; closed-slot label out of the
  color box; Open toggle widens; latency chip repositioned; color tiles unified.
- **Chrome:** music panel got the chat panel's skin (was raw dialog3 with
  floating headers overlapping the toggle); map preview + inline browser got
  flat bevel skins; compose row restyled; duplicate CHAT header dropped; M2
  underline tabs (flat, uppercase); dead `LOBBY_OPTIONS_BIN` chrome deleted;
  palette doc alpha-order corrected (RRGGBBAA) + sanctioned-exceptions section.

Not built/launched (user machine under load) — verification pending.

## Notes

Will commit each phase as a separate commit. Build between phases to
catch breakage early.
