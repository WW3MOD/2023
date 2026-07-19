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

Round 1 verified 2026-07-19 via build + 3 SCREENSHOT captures (2560 default,
Music tab, 1366×768) — all fixes confirmed on screen.

## Round 2 — 2026-07-19 (post-verification improvements)

12-item list reviewed by user ("fix everything"); 4 parallel agents
(commits 5e776fcf, 8be9a923, 934923fd, 9c7c0c8a):
- Corner brackets now track the letterboxed preview image per-tick from
  MapPreviewLogic (YAML positions = fallback); map-type line uppercased
- Spectator latency chip removed (LATENCY_REGION parked — hard Get);
  1px alpha inner bevel on color tiles; option rows 50→44 / 36→32
- CTA stats strip (SLOTS n/m · BOTS n · SPECTATORS n, live GetText);
  SKIRMISH GAME accent title; Players action row rides up to hug the
  roster via ContentHeight clamp
- **Test.LaunchLobbyMap root cause**: SkirmishLogic.ClientJoined restores
  %APPDATA%/OpenRA/skirmish.ww3mod.yaml and issues a map order that
  overrides the seed — wrapper now backs up/restores that file (and
  settings.yaml on Windows via MINGW uname case); music panel nits
- Kept deliberately: "Replay last" (decisions.md 8 amended)

Verified by build + re-capture (2560 + 1366×768): river-zeta seeds
correctly, brackets hug the image, stats live, action row hugs roster,
two option-row fix confirmed. Remaining engine-side nicety (skip
skirmish-restore when TestMode seeds a map) noted in BACKLOG.

## Notes

Will commit each phase as a separate commit. Build between phases to
catch breakage early.

## Round 3 - 2026-07-19 (spacing/modernization pass)

User feedback: Start button touched the bottom; settings quadrant "not so
pretty" - modernize margins without losing core feel. 8px-base spacing spec:
- Settings quadrant (6d7bc114): 16px panel insets, 12px gutters, uppercase
  TinyBold option labels with colons stripped at display point, dropdown
  rows 54 (12 label + 18 gap-offset + 30 control), checkbox rows 38,
  ACTIVE CHANGES chips start y36/x16, preset bar 36px centered controls
  with uniform widths (619px budget at 1366), SECTION_BG tint removed
- Outer chrome (4a72dadd): chat compose 30px at 8px insets, display 8px
  insets, map strip 16px insets with -16 overlap guard, scenario strip
  inset 16, stats pitch 24/132/220
- CTA (d8d2de4f, lead): bar deepened 60->72, button 44 recentered 14/14 -
  8px in a 60 bar still read as touching at 2560
Screenshot-verified at 2560 (spacing-pass, cta-72 captures).
