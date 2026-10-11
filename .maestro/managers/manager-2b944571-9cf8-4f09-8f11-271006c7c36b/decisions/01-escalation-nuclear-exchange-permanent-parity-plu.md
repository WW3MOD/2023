# Escalation nuclear exchange — permanent parity plus a retaliation window (user ruling 2026-09-13)

_Recorded 2026-09-13T15:40:37.422Z by 7a1529e6_

Ruled by the user 2026-09-13 in chat and on question `s0Ua-WgOue4dDMlmM9hai`. Supersedes decision 06's shared ratchet and decisions 13/14/20's stock + attribution machinery. Two sides only (decision 15 stands).

## The model

- **Release.** From the moment the peace is broken (DEFCON 1) a countdown runs (lobby: "First warheads", default 10:00 — the shipped `NuclearReleaseDelayTicks` base, kept on the user's explicit choice over match-start). When it expires, both sides hold the lowest yield (1 kt band) **permanently**, as a repeatable power on its own cooldown.
- **Bookkeeping is cooldown-unlock, not stock.** A side has a *permanent level* (the highest band it may fire freely, on cooldown) and possibly one *retaliation grant*.
- **Any launch arms the other side** — wherever it lands (no damage attribution, no demonstration shot). When side A fires yield band Y, side B: (1) gains Y **permanently** (parity in kind), and (2) receives a **retaliation window** (lobby: "Retaliation window", default 3:00) during which it may fire **one band up, Y+1**, which must be ready to fire the instant the window opens. If the window lapses unused, B stays at Y ("goes down to the level below"). Firing Y+1 inside the window in turn arms A with Y+1 permanently and a window for Y+2. Repeat hits at Y re-open the window; a hit at a higher band raises the window's band to the max.
- **Game-enders exist only as a retaliation grant** (window after being hit by 100 kt). Firing one begins the ending: both sides get 15 s to place their game-enders, Dead Hand places for anyone who does not, everything flies, the game ends. If the window lapses, no side holds a game-ender — there is no indefinite "draw card".
- **Same ending at the time limit** when "Nuclear ending" is on.
- **Nuclear Posture** (Limited / Flexible / Massive) scales the cooldowns. No host ceiling: `nuclear-ceiling` is dropped.

## Why the user judged it sound

The winner's correct play is restraint: hold at the loser's permanent level, absorb the small strikes on cooldown, never take the window. The loser's is attrition at its permanent level. Escalation only happens by mutual choice, in bounded windows, and the top rung is reachable only through a chain of deliberate replies. A held apocalypse cannot stall the game because grants expire.

## Timeline consequence

The lobby timeline shows **phase lengths**, not absolute times (user: "it can show how long each phase is and that will be enough"). No-rush is a length; peace is "until the first kill"; warheads is a length after that; the time limit is the one absolute clock. Dropdowns are the source of truth; the timeline is a read-only overview. The only consistency rule left is time-limit vs no-rush.

## Deferred, in order

HUD ledger + transition sounds (after the runtime lands); bots (last — they fire only when losing, and fire eagerly in the peace phase).
