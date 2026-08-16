# Netcode audit — predictions registered BEFORE verification

Registered 2026-08-16 against `main @ 8b4ae9cd` (brief said `0dd133e7`; main advanced
two commits — `1d89d1b0` + merge `8b4ae9cd` — during the read-in phase).

Written before any code was read on these questions, so that wrong predictions are
reportable as findings.

| # | Prediction | Confidence |
|---|---|---|
| P1 | `EnableSyncReports` defaults to **false** in a default lobby, so a stranger hosting produces **no** sync reports on either machine | 60% |
| P2 | The server browser mechanically works — a WW3MOD host appears in the WW3MOD browser via `master.openra.net`, filtered by mod id | 70% |
| P3 | The advertised version string is `release-20230225`, which collides with stock Red Alert's identity in any shared listing | 75% |
| P4 | No rejoin exists; a dropped player's slot cannot be reclaimed mid-match | 90% |
| P5 | Host quitting ends the match for everyone (in-process listen server) | 80% |
| P6 | UPnP/NAT is implemented and the create-server dialog gives port guidance | 85% |
| P7 | Kick / spectator / force-start all work, inherited unmodified from upstream | 80% |
| P8 | Replays record by default and are browsable from the main menu | 85% |

Verdicts are recorded in `260816-netcode.md`.
